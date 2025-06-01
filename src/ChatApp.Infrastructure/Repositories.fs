namespace ChatApp.Infrastructure.Repositories

open System
open System.Collections.Concurrent
open ChatApp.Domain.Types
open ChatApp.Application

/// In-memory implementation of the room repository
type InMemoryRoomRepository() =
    // Thread-safe collection to store rooms
    let rooms = ConcurrentDictionary<string, Room>()
    
    // Helper function to create an empty room
    let createEmptyRoom (roomName: RoomName) =
        {
            Name = roomName
            Messages = []
            Participants = Set.empty
            Created = Timestamp(DateTimeOffset.UtcNow)
        }
    
    // Helper to handle room not found errors
    let getRoom roomName =
        match rooms.TryGetValue roomName with
        | true, room -> Ok room
        | false, _ -> Error (CommandError.RoomNotFound roomName)
    
    // Helper to update a room
    let updateRoom (roomName: string) (updateFn: Room -> Room) =
        match rooms.TryGetValue roomName with
        | true, existingRoom ->
            let updatedRoom = updateFn existingRoom
            if rooms.TryUpdate(roomName, updatedRoom, existingRoom) then
                Ok updatedRoom
            else
                Error (CommandError.SystemError $"Failed to update room {roomName}")
        | false, _ -> Error (CommandError.RoomNotFound roomName)
    
    interface IRoomRepository with
        member _.GetRoom(roomName: string) : Result<Room, CommandError> =
            getRoom roomName
        
        member _.CreateRoom(roomName: RoomName) : Result<Room, CommandError> =
            let name = RoomName.value roomName
            let newRoom = createEmptyRoom roomName
            
            if rooms.TryAdd(name, newRoom) then
                Ok newRoom
            else
                // If room already exists, return it
                match rooms.TryGetValue name with
                | true, existingRoom -> Ok existingRoom
                | false, _ -> Error (CommandError.SystemError $"Failed to create room {name}")
        
        member _.AddMessageToRoom(roomName, message) : Result<Room, CommandError> =
            updateRoom roomName (fun room ->
                { room with Messages = message :: room.Messages }
            )
        
        member _.AddUserToRoom(roomName, userHandle) : Result<Room, CommandError> =
            updateRoom roomName (fun room ->
                { room with Participants = room.Participants.Add userHandle }
            )
        
        member _.RemoveUserFromRoom(roomName, userHandle) : Result<Room, CommandError> =
            updateRoom roomName (fun room ->
                { room with Participants = room.Participants.Remove userHandle }
            )
        
        member _.ListRooms() : Result<(RoomName * int) list, CommandError> =
            rooms.Values
            |> Seq.map (fun room -> (room.Name, room.Participants.Count))
            |> Seq.toList
            |> Ok
