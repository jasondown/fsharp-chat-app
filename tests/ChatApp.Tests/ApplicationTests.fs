module ChatApp.Tests.ApplicationTests

open Xunit
open ChatApp.Domain.Types
open ChatApp.Application
open ChatApp.Infrastructure.Repositories

/// Helper function to extract Result values
let getResultValue result =
    match result with
    | Result.Ok value -> value
    | Result.Error err -> failwith $"Expected success but got error: {err}"

/// Tests for ChatService class
module ChatServiceTests =
    
    let createTestUserHandle() = UserHandle.create "test-user" |> getResultValue
    let createTestRoomName() = RoomName.create "test-room" |> getResultValue
    let createTestMessageContent() = MessageContent.create "Hello, world!" |> getResultValue
    
    [<Fact>]
    let ``ChatService should create room if it doesn't exist`` () =
        let chatService = ChatService(InMemoryRoomRepository())
        let roomName = "new-room"
        
        let result = chatService.CreateRoom(roomName)
        
        match result with
        | Result.Ok room -> 
            Assert.Equal(roomName, RoomName.value room.Name)
            Assert.Empty(room.Messages)
            Assert.Empty(room.Participants)
        | Result.Error err -> Assert.Fail($"Expected success but got: {err}")
    
    [<Fact>]
    let ``ChatService should allow joining existing room`` () =
        let chatService = ChatService(InMemoryRoomRepository())
        let roomName = "join-test"
        let userName = "alice"
        
        // First create the room
        let _ = chatService.CreateRoom(roomName)
        
        // Then join it
        let result = chatService.JoinRoom(userName, roomName)
        
        match result with
        | Result.Ok (joinedRoom, messages) ->
            Assert.Equal(roomName, RoomName.value joinedRoom)
            Assert.Empty(messages)
        | Result.Error err -> Assert.Fail($"Expected success but got: {err}")
    
    [<Fact>]
    let ``ChatService should send message to room`` () =
        let chatService = ChatService(InMemoryRoomRepository())
        let roomName = "message-test"
        let userName = "bob"
        let messageText = "Hello everyone!"
        
        // Create room and join it
        chatService.CreateRoom(roomName) |> ignore
        chatService.JoinRoom(userName, roomName) |> ignore
        
        // Send a message
        let sendResult = chatService.SendMessage(userName, roomName, messageText)
        
        match sendResult with
        | Result.Ok message ->
            Assert.Equal(messageText, MessageContent.value message.Content)
            Assert.Equal(userName, UserHandle.value message.Author)
            Assert.Equal(roomName, RoomName.value message.Room)
            
            // Verify the message appears in room history
            let _, history = chatService.JoinRoom(userName, roomName) |> getResultValue
            Assert.Single(history) |> ignore
            let lastMessage = List.head history
            Assert.Equal(messageText, MessageContent.value lastMessage.Content)
            
        | Result.Error err -> Assert.Fail($"Expected success but got: {err}")
    
    [<Fact>]
    let ``ChatService should reject invalid inputs`` () =
        let chatService = ChatService(InMemoryRoomRepository())
        
        // Try to join with invalid username
        let invalidUserResult = chatService.JoinRoom("", "test-room")
        
        match invalidUserResult with
        | Result.Error (CommandError.ValidationError _) -> () // Expected
        | Result.Ok _ -> Assert.Fail("Expected validation error for empty username")
        | Result.Error err -> Assert.Fail($"Expected ValidationError but got: {err}")
        
        // Try to join invalid room name
        let invalidRoomResult = chatService.JoinRoom("alice", "")
        
        match invalidRoomResult with
        | Result.Error (CommandError.ValidationError _) -> () // Expected  
        | Result.Ok _ -> Assert.Fail("Expected validation error for empty room name")
        | Result.Error err -> Assert.Fail($"Expected ValidationError but got: {err}")
    
    [<Fact>]
    let ``ChatService should list rooms`` () =
        let chatService = ChatService(InMemoryRoomRepository())
        
        // Create a couple of rooms
        chatService.CreateRoom("room1") |> ignore
        chatService.CreateRoom("room2") |> ignore
        chatService.CreateRoom("room3") |> ignore
        
        // Join a user to one room
        chatService.JoinRoom("user1", "room1") |> ignore
        
        let result = chatService.ListRooms()
        
        match result with
        | Result.Ok rooms ->
            Assert.Equal(3, List.length rooms)
            
            // Find room1 and check participant count
            let room1 = rooms |> List.find (fun (name, _) -> RoomName.value name = "room1")
            let _, participantCount = room1
            Assert.Equal(1, participantCount)
            
            // Other rooms should have 0 participants
            let room2 = rooms |> List.find (fun (name, _) -> RoomName.value name = "room2")  
            let _, room2Count = room2
            Assert.Equal(0, room2Count)
            
        | Result.Error err -> Assert.Fail($"Expected success but got: {err}")

    [<Fact>]
    let ``ChatService.JoinRoom should create room if it doesn't exist`` () =
        let repository = InMemoryRoomRepository()
        let service = ChatService(repository)
        
        // Room doesn't exist initially
        match service.ListRooms() with
        | Result.Ok rooms -> Assert.Empty(rooms)
        | Result.Error err -> Assert.Fail($"Expected success but got error: {err}")
        
        // Join non-existent room should create it
        match service.JoinRoom("alice", "new-room") with
        | Result.Ok (roomName, messages) ->
            Assert.Equal("new-room", RoomName.value roomName)
            Assert.Empty(messages) // New room has no messages
        | Result.Error err -> Assert.Fail($"Expected success but got error: {err}")
        
        // Room should now exist
        match service.ListRooms() with
        | Result.Ok roomsAfterJoin ->
            let roomList = roomsAfterJoin |> List.toArray
            Assert.Single(roomList) |> ignore
            let (createdRoom, participantCount) = roomList.[0]
            Assert.Equal("new-room", RoomName.value createdRoom)
            Assert.Equal(1, participantCount) // Alice joined
        | Result.Error err -> Assert.Fail($"Expected success but got error: {err}")