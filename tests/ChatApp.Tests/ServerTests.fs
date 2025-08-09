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