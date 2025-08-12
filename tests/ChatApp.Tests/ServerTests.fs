module ChatApp.Tests.ServerTests

open System
open System.Net
open System.Net.Sockets
open System.Threading
open System.Threading.Tasks
open Xunit
open Serilog
open ChatApp.Domain.Types
open ChatApp.Domain.Protocol
open ChatApp.Server
open ChatApp.Infrastructure.Protocol

/// Create a test logger
let createTestLogger() : ILogger =
    LoggerConfiguration()
        .MinimumLevel.Debug()
        .WriteTo.Console()
        .CreateLogger()

/// Helper for creating a test client connection
let createTestClient(port: int) =
    let client = new TcpClient()
    client.Connect(IPAddress.Loopback, port)
    client

/// Helper to create test domain objects
let createUserHandle name = 
    match UserHandle.create name with
    | Result.Ok handle -> handle
    | Result.Error err -> failwith $"Failed to create UserHandle: {err}"

let createRoomName name = 
    match RoomName.create name with
    | Result.Ok room -> room
    | Result.Error err -> failwith $"Failed to create RoomName: {err}"

let createMessageContent text =
    match MessageContent.create text with
    | Result.Ok content -> content
    | Result.Error err -> failwith $"Failed to create MessageContent: {err}"

/// Tests for ConnectionManager
module ConnectionManagerTests =
    
    [<Fact>]
    let ``ConnectionManager should track client connections`` () =
        let logger = createTestLogger()
        let manager = ConnectionManager(logger)
        
        let clientId = Guid.NewGuid()
        use client = new TcpClient()
        
        manager.AddClient(clientId, client)
        let initialClients = manager.GetConnectedClients()
        Assert.Equal(1, initialClients.Length)
        Assert.Equal(clientId, initialClients[0].Id)
        Assert.Equal(None, initialClients[0].UserHandle)
        Assert.Equal(None, initialClients[0].CurrentRoom)
        
        // Clean up
        manager.RemoveClient(clientId)
        let finalClients = manager.GetConnectedClients()
        Assert.Empty(finalClients)

/// Tests for TcpChatServer
module TcpChatServerTests =
    
    // Helper to run a server for a short period
    let runServerForTest (port: int) (action: TcpChatServer -> Async<unit>) : unit =
        async {
            let logger = createTestLogger()
            use server = new TcpChatServer(port, logger)
            
            // Start server on a background task
            use cts = new CancellationTokenSource()
            let _serverTask = Task.Run(fun () -> 
                try
                    Async.RunSynchronously(server.StartAsync(), cancellationToken = cts.Token)
                with
                | :? OperationCanceledException -> ()
            )
            
            do! Async.Sleep(200) // Give server time to start
            
            try
                do! action server
            finally
                cts.Cancel()
                server.Stop()
        }
        |> Async.RunSynchronously
    
    [<Fact>]
    let ``TcpChatServer should accept client connections`` () =
        let port = 5001
        
        let testAction (server: TcpChatServer) = async {
            // Connect a client
            use _client = createTestClient(port)
            
            do! Async.Sleep(100) // Give server time to process
            
            // Check if client was accepted
            let connectedClients = server.GetConnectedClients()
            Assert.Equal(1, connectedClients.Length)
        }
        
        runServerForTest port testAction
    
    [<Fact>]
    let ``TcpChatServer should handle basic client commands`` () =
        let port = 5002
        
        let testAction (_server: TcpChatServer) = async {
            // Connect a client
            use client = createTestClient(port)
            
            // Send ListRooms command
            let listRoomsCommand = ListRooms
            match TcpProtocol.sendClientCommand client listRoomsCommand with
            | Result.Ok () -> ()
            | Result.Error err -> Assert.Fail($"Failed to send command: {err}")
            
            // Give server time to process
            do! Async.Sleep(100)
            
            // Read the response
            let response = TcpProtocol.readServerMessage client
            
            match response with
            | Result.Ok (RoomList _) -> 
                () // Success - got a room list back
            | Result.Ok other -> 
                Assert.Fail($"Expected RoomList but got: {other}")
            | Result.Error err -> 
                Assert.Fail($"Error reading server response: {err}")
        }
        
        runServerForTest port testAction
    
    [<Fact>]
    let ``TcpChatServer should handle ListUsers for specific room`` () =
        let port = 5003
        
        let testAction (_server: TcpChatServer) = async {
            // Connect two clients
            use client1 = createTestClient(port)
            use client2 = createTestClient(port)
            
            do! Async.Sleep(100)
            
            let alice = createUserHandle "alice"
            let bob = createUserHandle "bob"
            let room = createRoomName "test-room"
            
            // Have both clients join the same room
            match TcpProtocol.sendClientCommand client1 (JoinRoom (alice, room)) with
            | Result.Ok () -> ()
            | Result.Error err -> Assert.Fail($"Failed to send join command: {err}")
            
            match TcpProtocol.sendClientCommand client2 (JoinRoom (bob, room)) with
            | Result.Ok () -> ()
            | Result.Error err -> Assert.Fail($"Failed to send join command: {err}")
            
            do! Async.Sleep(200) // Give server time to process joins
            
            // Helper to read messages until we get the one we want or timeout
            let rec readUntilUserList (client: TcpClient) (maxAttempts: int) =
                if maxAttempts <= 0 then
                    Assert.Fail("Timed out waiting for UserList message")
                    failwith "unreachable"
                else
                    match TcpProtocol.readServerMessage client with
                    | Result.Ok (UserList _ as msg) -> msg
                    | Result.Ok _ -> 
                        // Got a different message, keep reading
                        readUntilUserList client (maxAttempts - 1)
                    | Result.Error err -> 
                        Assert.Fail($"Error reading server response: {err}")
                        failwith "unreachable"
            
            // Now request user list for the room
            match TcpProtocol.sendClientCommand client1 (ListUsers (Some room)) with
            | Result.Ok () -> ()
            | Result.Error err -> Assert.Fail($"Failed to send list users command: {err}")
            
            do! Async.Sleep(100)
            
            // Read messages until we get UserList
            let response = readUntilUserList client1 10
            
            match response with
            | UserList (returnedRoom, users) -> 
                Assert.Equal(room, returnedRoom)
                Assert.Equal(2, List.length users)
                Assert.Contains(alice, users)
                Assert.Contains(bob, users)
            | other -> 
                Assert.Fail($"Expected UserList but got: {other}")
        }
        
        runServerForTest port testAction
    
    [<Fact>]
    let ``TcpChatServer should handle ListUsers for non-existent room`` () =
        let port = 5004
        
        let testAction (_server: TcpChatServer) = async {
            use client = createTestClient(port)
            
            do! Async.Sleep(100)
            
            let nonExistentRoom = createRoomName "non-existent"
            
            // Request user list for non-existent room
            match TcpProtocol.sendClientCommand client (ListUsers (Some nonExistentRoom)) with
            | Result.Ok () -> ()
            | Result.Error err -> Assert.Fail($"Failed to send list users command: {err}")
            
            do! Async.Sleep(100)
            
            // Should receive an error
            let response = TcpProtocol.readServerMessage client
            
            match response with
            | Result.Ok (ServerMessage.Error msg) -> 
                Assert.Contains("does not exist", msg)
            | Result.Ok other -> 
                Assert.Fail($"Expected Error but got: {other}")
            | Result.Error err -> 
                Assert.Fail($"Error reading server response: {err}")
        }
        
        runServerForTest port testAction
    
    [<Fact>]
    let ``TcpChatServer should handle ListUsers for current room when not in room`` () =
        let port = 5005
        
        let testAction (_server: TcpChatServer) = async {
            use client = createTestClient(port)
            
            do! Async.Sleep(100)
            
            // Request user list for current room (None) when not in a room
            match TcpProtocol.sendClientCommand client (ListUsers None) with
            | Result.Ok () -> ()
            | Result.Error err -> Assert.Fail($"Failed to send list users command: {err}")
            
            do! Async.Sleep(100)
            
            // Should receive an error
            let response = TcpProtocol.readServerMessage client
            
            match response with
            | Result.Ok (ServerMessage.Error msg) -> 
                Assert.Contains("not currently in any room", msg.ToLower())
            | Result.Ok other -> 
                Assert.Fail($"Expected Error but got: {other}")
            | Result.Error err -> 
                Assert.Fail($"Error reading server response: {err}")
        }
        
        runServerForTest port testAction
    
    [<Fact>]
    let ``TcpChatServer should handle GetRoomHistory for existing room`` () =
        let port = 5006
        
        let testAction (_server: TcpChatServer) = async {
            // Connect a client
            use client = createTestClient(port)
            
            do! Async.Sleep(100)
            
            let alice = createUserHandle "alice"
            let room = createRoomName "history-test"
            
            // Join the room
            match TcpProtocol.sendClientCommand client (JoinRoom (alice, room)) with
            | Result.Ok () -> ()
            | Result.Error err -> Assert.Fail($"Failed to send join command: {err}")
            
            do! Async.Sleep(100)
            
            // Send some messages
            let message1 = createMessageContent "Hello, world!"
            let message2 = createMessageContent "This is a test"
            
            match TcpProtocol.sendClientCommand client (SendMessage (alice, room, message1)) with
            | Result.Ok () -> ()
            | Result.Error err -> Assert.Fail($"Failed to send message: {err}")
            
            match TcpProtocol.sendClientCommand client (SendMessage (alice, room, message2)) with
            | Result.Ok () -> ()
            | Result.Error err -> Assert.Fail($"Failed to send message: {err}")
            
            do! Async.Sleep(200) // Give server time to process messages
            
            // Helper to read messages until we get RoomHistory or timeout
            let rec readUntilRoomHistory (client: TcpClient) (maxAttempts: int) =
                if maxAttempts <= 0 then
                    Assert.Fail("Timed out waiting for RoomHistory message")
                    failwith "unreachable"
                else
                    match TcpProtocol.readServerMessage client with
                    | Result.Ok (RoomHistory _ as msg) -> msg
                    | Result.Ok _ -> 
                        // Got a different message, keep reading
                        readUntilRoomHistory client (maxAttempts - 1)
                    | Result.Error err -> 
                        Assert.Fail($"Error reading server response: {err}")
                        failwith "unreachable"
            
            // Request room history
            match TcpProtocol.sendClientCommand client (GetRoomHistory room) with
            | Result.Ok () -> ()
            | Result.Error err -> Assert.Fail($"Failed to send get history command: {err}")
            
            do! Async.Sleep(100)
            
            // Read messages until we get RoomHistory
            let response = readUntilRoomHistory client 10
            
            match response with
            | RoomHistory (returnedRoom, messages) -> 
                Assert.Equal(room, returnedRoom)
                Assert.True(List.length messages >= 2, "Should have at least 2 messages")
                // Check that our messages are in the history
                let messageContents = messages |> List.map (fun m -> MessageContent.value m.Content)
                Assert.Contains("Hello, world!", messageContents)
                Assert.Contains("This is a test", messageContents)
            | other -> 
                Assert.Fail($"Expected RoomHistory but got: {other}")
        }
        
        runServerForTest port testAction
    
    [<Fact>]
    let ``TcpChatServer should handle GetRoomHistory for non-existent room`` () =
        let port = 5007
        
        let testAction (_server: TcpChatServer) = async {
            use client = createTestClient(port)
            
            do! Async.Sleep(100)
            
            let nonExistentRoom = createRoomName "non-existent-history"
            
            // Request history for non-existent room
            match TcpProtocol.sendClientCommand client (GetRoomHistory nonExistentRoom) with
            | Result.Ok () -> ()
            | Result.Error err -> Assert.Fail($"Failed to send get history command: {err}")
            
            do! Async.Sleep(100)
            
            // Should receive an error
            let response = TcpProtocol.readServerMessage client
            
            match response with
            | Result.Ok (ServerMessage.Error msg) -> 
                Assert.Contains("does not exist", msg)
            | Result.Ok other -> 
                Assert.Fail($"Expected Error but got: {other}")
            | Result.Error err -> 
                Assert.Fail($"Error reading server response: {err}")
        }
        
        runServerForTest port testAction
    
    [<Fact>]
    let ``TcpChatServer should auto-leave current room when joining different room`` () =
        let port = 5008
        
        let testAction (_server: TcpChatServer) = async {
            // Connect three clients
            use client1 = createTestClient(port) // Alice - will switch rooms
            use client2 = createTestClient(port) // Bob - stays in room1
            use client3 = createTestClient(port) // Charlie - in room2
            
            do! Async.Sleep(100)
            
            let alice = createUserHandle "alice"
            let bob = createUserHandle "bob"
            let charlie = createUserHandle "charlie"
            let room1 = createRoomName "room1"
            let room2 = createRoomName "room2"
            
            // Alice and Bob join room1
            match TcpProtocol.sendClientCommand client1 (JoinRoom (alice, room1)) with
            | Result.Ok () -> ()
            | Result.Error err -> Assert.Fail($"Failed to send join command: {err}")
            
            match TcpProtocol.sendClientCommand client2 (JoinRoom (bob, room1)) with
            | Result.Ok () -> ()
            | Result.Error err -> Assert.Fail($"Failed to send join command: {err}")
            
            // Charlie joins room2
            match TcpProtocol.sendClientCommand client3 (JoinRoom (charlie, room2)) with
            | Result.Ok () -> ()
            | Result.Error err -> Assert.Fail($"Failed to send join command: {err}")
            
            do! Async.Sleep(200) // Give server time to process joins
            
            // Clear any existing messages
            let clearMessages (client: TcpClient) =
                let mutable ``continue`` = true
                while ``continue`` do
                    client.Client.Poll(100, SelectMode.SelectRead) |> ignore
                    if client.Available > 0 then
                        TcpProtocol.readServerMessage client |> ignore
                    else
                        ``continue`` <- false
            
            clearMessages client1
            clearMessages client2
            clearMessages client3
            
            // Now Alice switches from room1 to room2
            match TcpProtocol.sendClientCommand client1 (JoinRoom (alice, room2)) with
            | Result.Ok () -> ()
            | Result.Error err -> Assert.Fail($"Failed to send join command: {err}")
            
            do! Async.Sleep(200) // Give server time to process
            
            // Bob should receive a UserLeft notification in room1
            let rec checkForUserLeft (client: TcpClient) (attempts: int) =
                if attempts <= 0 then
                    false
                else
                    match TcpProtocol.readServerMessage client with
                    | Result.Ok (UserLeft (user, room)) when user = alice && room = room1 -> true
                    | _ -> checkForUserLeft client (attempts - 1)
            
            Assert.True(checkForUserLeft client2 5, "Bob should receive UserLeft notification for Alice in room1")
            
            // Charlie should receive a UserJoined notification in room2
            let rec checkForUserJoined (client: TcpClient) (attempts: int) =
                if attempts <= 0 then
                    false
                else
                    match TcpProtocol.readServerMessage client with
                    | Result.Ok (UserJoined (user, room)) when user = alice && room = room2 -> true
                    | _ -> checkForUserJoined client (attempts - 1)
            
            Assert.True(checkForUserJoined client3 5, "Charlie should receive UserJoined notification for Alice in room2")
            
            // Verify room participants
            match TcpProtocol.sendClientCommand client2 (ListUsers (Some room1)) with
            | Result.Ok () -> ()
            | Result.Error err -> Assert.Fail($"Failed to send list users command: {err}")
            
            do! Async.Sleep(100)
            
            let rec readUntilUserList (client: TcpClient) (maxAttempts: int) =
                if maxAttempts <= 0 then
                    failwith "Timed out waiting for UserList"
                else
                    match TcpProtocol.readServerMessage client with
                    | Result.Ok (UserList _ as msg) -> msg
                    | Result.Ok _ -> readUntilUserList client (maxAttempts - 1)
                    | Result.Error err -> failwith $"Error reading: {err}"
            
            match readUntilUserList client2 10 with
            | UserList (_, users) ->
                Assert.Equal(1, List.length users)
                Assert.Contains(bob, users)
                Assert.DoesNotContain(alice, users)
            | _ -> Assert.Fail("Expected UserList")
            
            // Check room2 participants
            match TcpProtocol.sendClientCommand client3 (ListUsers (Some room2)) with
            | Result.Ok () -> ()
            | Result.Error err -> Assert.Fail($"Failed to send list users command: {err}")
            
            do! Async.Sleep(100)
            
            match readUntilUserList client3 10 with
            | UserList (_, users) ->
                Assert.Equal(2, List.length users)
                Assert.Contains(charlie, users)
                Assert.Contains(alice, users)
            | _ -> Assert.Fail("Expected UserList")
        }
        
        runServerForTest port testAction
    
    [<Fact>]
    let ``TcpChatServer should handle idempotent join to same room`` () =
        let port = 5009
        
        let testAction (_server: TcpChatServer) = async {
            use client = createTestClient(port)
            
            do! Async.Sleep(100)
            
            let alice = createUserHandle "alice"
            let room = createRoomName "test-room"
            let messageContent = createMessageContent "Test message"
            
            // Join room first time
            match TcpProtocol.sendClientCommand client (JoinRoom (alice, room)) with
            | Result.Ok () -> ()
            | Result.Error err -> Assert.Fail($"Failed to send join command: {err}")
            
            do! Async.Sleep(100)
            
            // Send a message
            match TcpProtocol.sendClientCommand client (SendMessage (alice, room, messageContent)) with
            | Result.Ok () -> ()
            | Result.Error err -> Assert.Fail($"Failed to send message: {err}")
            
            do! Async.Sleep(100)
            
            // Clear any pending messages
            while client.Available > 0 do
                TcpProtocol.readServerMessage client |> ignore
            
            // Join the same room again (should be idempotent)
            match TcpProtocol.sendClientCommand client (JoinRoom (alice, room)) with
            | Result.Ok () -> ()
            | Result.Error err -> Assert.Fail($"Failed to send join command: {err}")
            
            do! Async.Sleep(100)
            
            // Should receive JoinedRoom with message history
            let rec findJoinedRoom (attempts: int) =
                if attempts <= 0 then
                    failwith "Timed out waiting for JoinedRoom"
                else
                    match TcpProtocol.readServerMessage client with
                    | Result.Ok (JoinedRoom (roomName, messages)) -> (roomName, messages)
                    | Result.Ok _ -> findJoinedRoom (attempts - 1)
                    | Result.Error err -> failwith $"Error reading: {err}"
            
            let joinedRoom, messages = findJoinedRoom 5
            Assert.Equal(room, joinedRoom)
            Assert.True(List.length messages >= 1, "Should have at least the message we sent")
            
            // Verify we're still in the room by sending another message
            let messageContent2 = createMessageContent "Still here"
            match TcpProtocol.sendClientCommand client (SendMessage (alice, room, messageContent2)) with
            | Result.Ok () -> ()
            | Result.Error err -> Assert.Fail($"Should still be able to send messages: {err}")
        }
        
        runServerForTest port testAction
    
    [<Fact>]
    let ``TcpChatServer should not send duplicate notifications for idempotent join`` () =
        let port = 5010
        
        let testAction (_server: TcpChatServer) = async {
            // Connect two clients
            use client1 = createTestClient(port) // Alice
            use client2 = createTestClient(port) // Bob - will monitor notifications
            
            do! Async.Sleep(100)
            
            let alice = createUserHandle "alice"
            let bob = createUserHandle "bob"
            let room = createRoomName "notification-test"
            
            // Both join the room
            match TcpProtocol.sendClientCommand client1 (JoinRoom (alice, room)) with
            | Result.Ok () -> ()
            | Result.Error err -> Assert.Fail($"Failed to send join command: {err}")
            
            match TcpProtocol.sendClientCommand client2 (JoinRoom (bob, room)) with
            | Result.Ok () -> ()
            | Result.Error err -> Assert.Fail($"Failed to send join command: {err}")
            
            do! Async.Sleep(200)
            
            // Clear Bob's message queue
            while client2.Available > 0 do
                TcpProtocol.readServerMessage client2 |> ignore
            
            // Alice joins the same room again
            match TcpProtocol.sendClientCommand client1 (JoinRoom (alice, room)) with
            | Result.Ok () -> ()
            | Result.Error err -> Assert.Fail($"Failed to send join command: {err}")
            
            do! Async.Sleep(200)
            
            // Bob should NOT receive any UserJoined notification for Alice
            let mutable receivedUserJoined = false
            while client2.Available > 0 do
                match TcpProtocol.readServerMessage client2 with
                | Result.Ok (UserJoined (user, _)) when user = alice ->
                    receivedUserJoined <- true
                | _ -> ()
            
            Assert.False(receivedUserJoined, "Bob should not receive UserJoined for Alice's idempotent join")
        }
        
        runServerForTest port testAction