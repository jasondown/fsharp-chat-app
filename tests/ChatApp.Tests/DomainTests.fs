module ChatApp.Tests.DomainTests

open System
open Xunit
open ChatApp.Domain.Types

/// Tests for the UserHandle validation logic
module UserHandleTests =
    
    [<Fact>]
    let ``UserHandle.create succeeds with valid input`` () =
        let result = UserHandle.create "alice-123"
        match result with
        | Ok handle -> Assert.Equal("alice-123", UserHandle.value handle)
        | Error err -> Assert.Fail($"Expected success but got error: {err}")
    
    [<Fact>]
    let ``UserHandle.create fails with empty input`` () =
        let result = UserHandle.create ""
        match result with
        | Error EmptyUserHandle -> () // Expected
        | Ok handle -> Assert.Fail($"Expected failure but got: {handle}")
        | Error err -> Assert.Fail($"Expected EmptyUserHandle but got: {err}")
    
    [<Fact>]
    let ``UserHandle.create fails with invalid characters`` () =
        let result = UserHandle.create "alice@123"
        match result with
        | Error (InvalidUserHandleChars _) -> () // Expected
        | Ok handle -> Assert.Fail($"Expected failure but got: {handle}")
        | Error err -> Assert.Fail($"Expected InvalidUserHandleChars but got: {err}")

/// Tests for the RoomName validation logic
module RoomNameTests =
    
    [<Fact>]
    let ``RoomName.create succeeds with valid input`` () =
        let result = RoomName.create "general-chat"
        match result with
        | Ok room -> Assert.Equal("general-chat", RoomName.value room)
        | Error err -> Assert.Fail($"Expected success but got error: {err}")
    
    [<Fact>]
    let ``RoomName.create fails with empty input`` () =
        let result = RoomName.create ""
        match result with
        | Error EmptyRoomName -> () // Expected
        | Ok room -> Assert.Fail($"Expected failure but got: {room}")
        | Error err -> Assert.Fail($"Expected EmptyRoomName but got: {err}")
    
    [<Fact>]
    let ``RoomName.create fails with invalid characters`` () =
        let result = RoomName.create "general chat"  // space is invalid
        match result with
        | Error (InvalidRoomNameChars _) -> () // Expected
        | Ok room -> Assert.Fail($"Expected failure but got: {room}")
        | Error err -> Assert.Fail($"Expected InvalidRoomNameChars but got: {err}")

/// Tests for the MessageContent validation logic  
module MessageContentTests =
    
    [<Fact>]
    let ``MessageContent.create succeeds with valid input`` () =
        let result = MessageContent.create "Hello, world!"
        match result with
        | Ok content -> Assert.Equal("Hello, world!", MessageContent.value content)
        | Error err -> Assert.Fail($"Expected success but got error: {err}")
    
    [<Fact>]
    let ``MessageContent.create fails with empty input`` () =
        let result = MessageContent.create ""
        match result with
        | Error EmptyMessageContent -> () // Expected
        | Ok content -> Assert.Fail($"Expected failure but got: {content}")
        | Error err -> Assert.Fail($"Expected EmptyMessageContent but got: {err}")
    
    [<Fact>]
    let ``MessageContent.create fails with too long input`` () =
        let veryLongMessage = String.replicate 2000 "a"  // 2000 'a's
        let result = MessageContent.create veryLongMessage
        match result with
        | Error (MessageContentTooLong _) -> () // Expected
        | Ok content -> Assert.Fail($"Expected failure but got: {content}")
        | Error err -> Assert.Fail($"Expected MessageContentTooLong but got: {err}")