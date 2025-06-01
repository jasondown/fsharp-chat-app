module ChatApp.Tests.InfrastructureTests

open System
open Xunit
open ChatApp.Domain.Types
open ChatApp.Domain.Protocol
open ChatApp.Application
open ChatApp.Infrastructure.Repositories
open ChatApp.Infrastructure.Serialization

/// Helper function to extract value from Result in tests
let getResultValue result =
    match result with
    | Result.Ok value -> value
    | Result.Error err -> failwith $"Expected success but got error: {err}"

/// Tests for the in-memory repository implementation
module RepositoryTests =
    
    let createTestRoom () =
        RoomName.create "test-room" |> getResultValue
    
    let createTestUser () =
        UserHandle.create "test-user" |> getResultValue
    
    let createTestMessage room user content =
        let messageContent = MessageContent.create content |> getResultValue
        {
            Id = MessageId(Guid.NewGuid())
            Content = messageContent
            Author = user
            Room = room
            Timestamp = Timestamp(DateTimeOffset.UtcNow)
        }
    
    [<Fact>]
    let ``Repository should create a new room successfully`` () =
        let repo = InMemoryRoomRepository() :> IRoomRepository
        let roomName = createTestRoom ()
        
        let result = repo.CreateRoom(roomName)
        
        match result with
        | Result.Ok room -> 
            Assert.Equal(roomName, room.Name)
            Assert.Empty(room.Messages)
            Assert.Empty(room.Participants)
        | Result.Error err -> Assert.Fail($"Expected success but got: {err}")
    
    [<Fact>]
    let ``Repository should retrieve an existing room`` () =
        let repo = InMemoryRoomRepository() :> IRoomRepository
        let roomName = createTestRoom ()
        
        // First create the room
        let _ = repo.CreateRoom(roomName) |> ignore
        
        // Then retrieve it
        let result = repo.GetRoom(RoomName.value roomName)
        
        match result with
        | Result.Ok room -> Assert.Equal(roomName, room.Name)
        | Result.Error err -> Assert.Fail($"Expected success but got: {err}")
    
    [<Fact>]
    let ``Repository should return error for non-existent room`` () =
        let repo = InMemoryRoomRepository() :> IRoomRepository
        
        let result = repo.GetRoom("nonexistent")
        
        match result with
        | Result.Error (CommandError.RoomNotFound _) -> () // Expected
        | Result.Ok room -> Assert.Fail($"Expected error but got room: {room}")
        | Result.Error err -> Assert.Fail($"Expected RoomNotFound but got: {err}")
    
    [<Fact>]
    let ``Repository should add message to room`` () =
        let repo = InMemoryRoomRepository() :> IRoomRepository
        let roomName = createTestRoom ()
        let userHandle = createTestUser ()
        let message = createTestMessage roomName userHandle "Hello world"
        
        // Create room and add message
        let _ = repo.CreateRoom(roomName) |> ignore
        let result = repo.AddMessageToRoom(RoomName.value roomName, message)
        
        match result with
        | Result.Ok room -> 
            Assert.Single(room.Messages) |> ignore
            Assert.Equal(message, List.head room.Messages)
        | Result.Error err -> Assert.Fail($"Expected success but got: {err}")
    
    [<Fact>]
    let ``Repository should add and remove users from room`` () =
        let repo = InMemoryRoomRepository() :> IRoomRepository
        let roomName = createTestRoom ()
        let userHandle = createTestUser ()
        
        // Create room, add user, then remove user
        let _ = repo.CreateRoom(roomName) |> ignore
        let addResult = repo.AddUserToRoom(RoomName.value roomName, userHandle)
        
        match addResult with
        | Result.Ok room -> 
            Assert.Contains(userHandle, room.Participants)
            
            // Now remove the user
            let removeResult = repo.RemoveUserFromRoom(RoomName.value roomName, userHandle)
            match removeResult with
            | Result.Ok updatedRoom -> Assert.DoesNotContain(userHandle, updatedRoom.Participants)
            | Result.Error err -> Assert.Fail($"Expected success removing user but got: {err}")
        | Result.Error err -> Assert.Fail($"Expected success adding user but got: {err}")
    
    [<Fact>]
    let ``Repository should list all rooms with participant counts`` () =
        let repo = InMemoryRoomRepository() :> IRoomRepository
        let room1 = RoomName.create "room1" |> getResultValue
        let room2 = RoomName.create "room2" |> getResultValue
        let user1 = createTestUser ()
        
        // Create rooms and add a user to one
        let _ = repo.CreateRoom(room1) |> ignore
        let _ = repo.CreateRoom(room2) |> ignore
        let _ = repo.AddUserToRoom(RoomName.value room1, user1) |> ignore
        
        let result = repo.ListRooms()
        
        match result with
        | Result.Ok rooms -> 
            Assert.Equal(2, List.length rooms)
            // Check that one room has 1 participant, the other has 0
            let roomCounts = rooms |> List.map snd
            Assert.Contains(0, roomCounts)
            Assert.Contains(1, roomCounts)
        | Result.Error err -> Assert.Fail($"Expected success but got: {err}")

/// Tests for protocol message serialization
module SerializationTests =
    
    let createTestUserHandle () = UserHandle.create "alice" |> getResultValue
    let createTestRoomName () = RoomName.create "general" |> getResultValue
    let createTestMessageContent () = MessageContent.create "Hello world!" |> getResultValue
    
    [<Fact>]
    let ``Should serialize and deserialize JoinRoom command`` () =
        let handle = createTestUserHandle ()
        let room = createTestRoomName ()
        let command = JoinRoom (handle, room)
        
        let json = JsonSerialization.serializeClientCommand command
        let result = JsonSerialization.deserializeClientCommand json
        
        match result with
        | Result.Ok deserializedCommand -> Assert.Equal(command, deserializedCommand)
        | Result.Error err -> Assert.Fail($"Deserialization failed: {err}")
    
    [<Fact>]
    let ``Should serialize and deserialize SendMessage command`` () =
        let handle = createTestUserHandle ()
        let room = createTestRoomName ()
        let content = createTestMessageContent ()
        let command = SendMessage (handle, room, content)
        
        let json = JsonSerialization.serializeClientCommand command
        let result = JsonSerialization.deserializeClientCommand json
        
        match result with
        | Result.Ok deserializedCommand -> Assert.Equal(command, deserializedCommand)
        | Result.Error err -> Assert.Fail($"Deserialization failed: {err}")
    
    [<Fact>]
    let ``Should serialize and deserialize ListRooms command`` () =
        let command = ListRooms
        
        let json = JsonSerialization.serializeClientCommand command
        let result = JsonSerialization.deserializeClientCommand json
        
        match result with
        | Result.Ok deserializedCommand -> Assert.Equal(command, deserializedCommand)
        | Result.Error err -> Assert.Fail($"Deserialization failed: {err}")
    
    [<Fact>]
    let ``Should serialize and deserialize RoomList server message`` () =
        let room1 = createTestRoomName ()
        let room2 = RoomName.create "random" |> getResultValue
        let roomList = [(room1, 5); (room2, 3)]
        let message = RoomList roomList
        
        let json = JsonSerialization.serializeServerMessage message
        let result = JsonSerialization.deserializeServerMessage json
        
        match result with
        | Result.Ok deserializedMessage -> Assert.Equal(message, deserializedMessage)
        | Result.Error err -> Assert.Fail($"Deserialization failed: {err}")
    
    [<Fact>]
    let ``Should serialize and deserialize Error server message`` () =
        let message = ServerMessage.Error "Something went wrong"
        
        let json = JsonSerialization.serializeServerMessage message
        let result = JsonSerialization.deserializeServerMessage json
        
        match result with
        | Result.Ok deserializedMessage -> Assert.Equal(message, deserializedMessage)
        | Result.Error err -> Assert.Fail($"Deserialization failed: {err}")
    
    [<Fact>]
    let ``Should fail to deserialize invalid JSON`` () =
        let invalidJson = "{ invalid json }"
        
        let result = JsonSerialization.deserializeClientCommand invalidJson
        
        match result with
        | Result.Error _ -> () // Expected
        | Result.Ok command -> Assert.Fail($"Expected error but got command: {command}")
        