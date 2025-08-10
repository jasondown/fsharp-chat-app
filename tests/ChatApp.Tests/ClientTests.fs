module ChatApp.Tests.ClientTests

open System
open Xunit
open ChatApp.Client
open ChatApp.Domain.Types
open ChatApp.Domain.Protocol

/// Tests for ChatClient functionality
module ChatClientTests =
    
    [<Fact>]
    let ``SetUsername should succeed with valid username`` () =
        use client = new ChatClient("localhost", 5000)
        
        let result = client.SetUsername("alice")
        
        Assert.True(result)
        match client.State.Username with
        | Some handle -> Assert.Equal("alice", UserHandle.value handle)
        | None -> Assert.Fail("Username should be set")
    
    [<Fact>]
    let ``SetUsername should fail with invalid username`` () =
        use client = new ChatClient("localhost", 5000)
        let mutable errorReceived = false
        
        client.OnEvent(function
            | ErrorOccurred _ -> errorReceived <- true
            | _ -> ())
        
        let result = client.SetUsername("invalid@username")
        
        Assert.False(result)
        Assert.True(errorReceived)
        Assert.True(client.State.Username.IsNone)
    
    [<Fact>]
    let ``SetUsername should update client state`` () =
        use client = new ChatClient("localhost", 5000)
        
        // Initially no username
        Assert.True(client.State.Username.IsNone)
        
        // Set username
        client.SetUsername("bob") |> ignore
        
        // Should now have username
        Assert.True(client.State.Username.IsSome)
        Assert.Equal("bob", UserHandle.value client.State.Username.Value)
    
    [<Fact>]
    let ``ListUsersInCurrentRoom should fail when not in a room`` () =
        use client = new ChatClient("localhost", 5000)
        let mutable errorReceived = false
        let mutable errorMessage = ""
        
        client.OnEvent(function
            | ErrorOccurred msg -> 
                errorReceived <- true
                errorMessage <- msg
            | _ -> ())
        
        // Try to list users when not connected
        let result = client.ListUsersInCurrentRoom()
        
        Assert.False(result)
        Assert.True(errorReceived)
        Assert.Equal("Not connected to server", errorMessage)
    
    [<Fact>]
    let ``ListUsersInRoom should validate room name`` () =
        use client = new ChatClient("localhost", 5000)
        let mutable errorReceived = false
        let mutable errorMessage = ""
        
        client.OnEvent(function
            | ErrorOccurred msg -> 
                errorReceived <- true
                errorMessage <- msg
            | _ -> ())
        
        // Try with invalid room name
        let result = client.ListUsersInRoom("invalid@room")
        
        Assert.False(result)
        Assert.True(errorReceived)
        Assert.Contains("Invalid room name", errorMessage)
    
    [<Fact>]
    let ``ListUsersInRoom should accept valid room name`` () =
        use client = new ChatClient("localhost", 5000)
        let mutable errorReceived = false
        
        client.OnEvent(function
            | ErrorOccurred _ -> errorReceived <- true
            | _ -> ())
        
        // This will fail because not connected, but should not fail validation
        let result = client.ListUsersInRoom("valid-room")
        
        // Result is false because not connected, but no validation error
        Assert.False(result)
        Assert.True(errorReceived)
    
    [<Fact>]
    let ``UsersListed event should be triggered when receiving UserList message`` () =
        use client = new ChatClient("localhost", 5000)
        let mutable usersListReceived = false
        let mutable receivedRoom = None
        let mutable receivedUsers = []
        
        client.OnEvent(function
            | UsersListed (room, users) -> 
                usersListReceived <- true
                receivedRoom <- Some room
                receivedUsers <- users
            | _ -> ())
        
        // Simulate receiving a UserList message
        let testRoom = RoomName.create "test-room" |> Result.toOption |> Option.get
        let testUsers = [
            UserHandle.create "alice" |> Result.toOption |> Option.get
            UserHandle.create "bob" |> Result.toOption |> Option.get
        ]
        
        // We need to use reflection to call the private ProcessServerMessage method
        let processMethod = client.GetType().GetMethod("ProcessServerMessage", 
                                                        System.Reflection.BindingFlags.NonPublic ||| 
                                                        System.Reflection.BindingFlags.Instance)
        processMethod.Invoke(client, [| UserList (testRoom, testUsers) |]) |> ignore
        
        Assert.True(usersListReceived)
        Assert.Equal(Some testRoom, receivedRoom)
        Assert.Equal<UserHandle list>(testUsers, receivedUsers)
    
    [<Fact>]
    let ``GetRoomHistory should validate room name`` () =
        use client = new ChatClient("localhost", 5000)
        let mutable errorReceived = false
        let mutable errorMessage = ""
        
        client.OnEvent(function
            | ErrorOccurred msg -> 
                errorReceived <- true
                errorMessage <- msg
            | _ -> ())
        
        // Try with invalid room name
        let result = client.GetRoomHistory("invalid@room")
        
        Assert.False(result)
        Assert.True(errorReceived)
        Assert.Contains("Invalid room name", errorMessage)
    
    [<Fact>]
    let ``GetRoomHistory should accept valid room name`` () =
        use client = new ChatClient("localhost", 5000)
        let mutable errorReceived = false
        let mutable errorMessage = ""
        
        client.OnEvent(function
            | ErrorOccurred msg -> 
                errorReceived <- true
                errorMessage <- msg
            | _ -> ())
        
        // This will fail because not connected, but should not fail validation
        let result = client.GetRoomHistory("valid-room")
        
        // Result is false because not connected
        Assert.False(result)
        Assert.True(errorReceived)
        Assert.Equal("Not connected to server", errorMessage)
    
    [<Fact>]
    let ``RoomHistoryReceived event should be triggered when receiving RoomHistory message`` () =
        use client = new ChatClient("localhost", 5000)
        let mutable historyReceived = false
        let mutable receivedRoom = None
        
        client.OnEvent(function
            | RoomHistoryReceived room -> 
                historyReceived <- true
                receivedRoom <- Some room
            | _ -> ())
        
        // Simulate receiving a RoomHistory message
        let testRoom = RoomName.create "test-room" |> Result.toOption |> Option.get
        let testMessages = [
            {
                Id = MessageId (Guid.NewGuid())
                Content = MessageContent.create "Hello" |> Result.toOption |> Option.get
                Author = UserHandle.create "alice" |> Result.toOption |> Option.get
                Room = testRoom
                Timestamp = Timestamp DateTimeOffset.Now
            }
        ]
        
        // Use reflection to call the private ProcessServerMessage method
        let processMethod = client.GetType().GetMethod("ProcessServerMessage", 
                                                        System.Reflection.BindingFlags.NonPublic ||| 
                                                        System.Reflection.BindingFlags.Instance)
        processMethod.Invoke(client, [| RoomHistory (testRoom, testMessages) |]) |> ignore
        
        Assert.True(historyReceived)
        Assert.Equal(Some testRoom, receivedRoom)
    
    [<Fact>]
    let ``RoomHistory message should update client state when for current room`` () =
        use client = new ChatClient("localhost", 5000)
        
        // Set up client to be in a room
        let testRoom = RoomName.create "test-room" |> Result.toOption |> Option.get
        let testUser = UserHandle.create "alice" |> Result.toOption |> Option.get
        
        // Use reflection to set the current room and username without wiping other fields
        let stateField = client.GetType().GetField("clientState", 
                                                   System.Reflection.BindingFlags.NonPublic ||| 
                                                   System.Reflection.BindingFlags.Instance)
        let currentState = stateField.GetValue(client) :?> ClientState
        let newState = { currentState with CurrentRoom = Some testRoom; Username = Some testUser }
        stateField.SetValue(client, newState)
        
        // Fixed timestamps for deterministic testing
        let ts1 = Timestamp(DateTimeOffset(2025, 8, 1, 12, 0, 0, TimeSpan.Zero))
        let ts2 = Timestamp(DateTimeOffset(2025, 8, 1, 11, 59, 50, TimeSpan.Zero))
        
        // Create test messages
        let testMessages = [
            {
                Id = MessageId (Guid.NewGuid())
                Content = MessageContent.create "Message 1" |> Result.toOption |> Option.get
                Author = testUser
                Room = testRoom
                Timestamp = ts1
            }
            {
                Id = MessageId (Guid.NewGuid())
                Content = MessageContent.create "Message 2" |> Result.toOption |> Option.get
                Author = testUser
                Room = testRoom
                Timestamp = ts2
            }
        ]
        
        // Process RoomHistory message
        let processMethod = client.GetType().GetMethod("ProcessServerMessage", 
                                                       System.Reflection.BindingFlags.NonPublic ||| 
                                                       System.Reflection.BindingFlags.Instance)
        processMethod.Invoke(client, [| RoomHistory (testRoom, testMessages) |]) |> ignore
        
        // Verify state was updated
        Assert.Equal(Some testRoom, client.State.CurrentRoom)
        Assert.Equal(2, client.State.RoomHistory.Length)
        Assert.Collection(
            client.State.RoomHistory,
            (fun m -> 
                Assert.Equal("Message 1", MessageContent.value m.Content)
                Assert.Equal(ts1, m.Timestamp)
                Assert.Equal(testUser, m.Author)),
            (fun m -> 
                Assert.Equal("Message 2", MessageContent.value m.Content)
                Assert.Equal(ts2, m.Timestamp)
                Assert.Equal(testUser, m.Author))
        )
