namespace ChatApp.Application

open ChatApp.Domain.Types
open FsToolkit.ErrorHandling

/// Interface for room storage
type IRoomRepository =
    abstract GetRoom: string -> Result<Room, CommandError>
    abstract CreateRoom: RoomName -> Result<Room, CommandError>  
    abstract AddMessageToRoom: roomName:string * message:Message -> Result<Room, CommandError>
    abstract AddUserToRoom: roomName:string * userHandle:UserHandle -> Result<Room, CommandError>
    abstract RemoveUserFromRoom: roomName:string * userHandle:UserHandle -> Result<Room, CommandError>
    abstract ListRooms: unit -> Result<(RoomName * int) list, CommandError>

/// Main chat service for handling application logic
type ChatService(roomRepository: IRoomRepository) =
    
    /// Join a room (create if it doesn't exist)
    member _.JoinRoom(userHandle: string, roomName: string) : Result<RoomName * Message list, CommandError> =
        result {
            let! userHandleObj = UserHandle.create userHandle |> Result.mapError ValidationError
            let! roomNameObj = RoomName.create roomName |> Result.mapError ValidationError
            
            // Try to get the room, create it if it doesn't exist
            let! room = 
                match roomRepository.GetRoom(RoomName.value roomNameObj) with
                | Result.Ok existingRoom -> Result.Ok existingRoom
                | Result.Error (CommandError.RoomNotFound _) -> 
                    // Room doesn't exist, create it
                    roomRepository.CreateRoom(roomNameObj)
                | Result.Error err -> Result.Error err
            
            // Add the user to the room participants
            let! _ = roomRepository.AddUserToRoom(RoomName.value roomNameObj, userHandleObj)
            
            return (roomNameObj, room.Messages)
        }
    
    /// Send a message
    member _.SendMessage(userHandle: string, roomName: string, messageContent: string) : Result<Message, CommandError> =
        result {
            let! message = Commands.sendMessage userHandle roomName messageContent roomRepository.GetRoom
            let! _ = roomRepository.AddMessageToRoom(RoomName.value message.Room, message)
            return message
        }
    
    /// Get the list of available rooms with user counts
    member _.ListRooms() : Result<(RoomName * int) list, CommandError> =
        roomRepository.ListRooms()
    
    /// Create a new room
    member _.CreateRoom(roomName: string) : Result<Room, CommandError> =
        result {
            let! roomNameObj = RoomName.create roomName
                               |> Result.mapError ValidationError
            return! roomRepository.CreateRoom roomNameObj
        }
    
    /// Leave a room
    member _.LeaveRoom(userHandle: string, roomName: string) : Result<RoomName, CommandError> =
        result {
            let! handle = UserHandle.create userHandle
                          |> Result.mapError ValidationError
            
            let! room = RoomName.create roomName
                        |> Result.mapError ValidationError
            
            let! _ = roomRepository.RemoveUserFromRoom(RoomName.value room, handle)
            
            return room
        }
    
    /// Get a room by name
    member _.GetRoom(roomName: string) : Result<Room, CommandError> =
        roomRepository.GetRoom(roomName)
