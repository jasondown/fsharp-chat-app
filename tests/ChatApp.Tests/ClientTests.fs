module ChatApp.Tests.ClientTests

open Xunit
open ChatApp.Client
open ChatApp.Domain.Types

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