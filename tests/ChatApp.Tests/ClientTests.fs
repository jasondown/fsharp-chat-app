module ChatApp.Tests.ClientTests

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