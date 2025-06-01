namespace ChatApp.Application

open System
open System.Threading
open ChatApp.Domain.Types
open ChatApp.Domain.Protocol
open FsToolkit.ErrorHandling

/// Interface for room storage
type IRoomRepository =
    abstract GetRoom: string -> Result<Room, CommandError>
    abstract CreateRoom: RoomName -> Result<Room, CommandError>  
    abstract AddMessageToRoom: string -> Message -> Result<Room, CommandError>
    abstract AddUserToRoom: string -> UserHandle -> Result<Room, CommandError>
    abstract RemoveUserFromRoom: string -> UserHandle -> Result<Room, CommandError>
    abstract ListRooms: unit -> Result<(RoomName * int) list, CommandError>

/// Main chat service for handling application logic
type ChatService(roomRepository: IRoomRepository) =
    
    /// Join a room
    member _.JoinRoom(userHandle: string, roomName: string) : Result<RoomName * Message list, CommandError> =
        Commands.joinRoom userHandle roomName roomRepository.GetRoom
    
    /// Send a message
    member _.SendMessage(userHandle: string, roomName: string, messageContent: string) : Result<Message, CommandError> =
        result {
            let! message = Commands.sendMessage userHandle roomName messageContent roomRepository.GetRoom
            let! _ = roomRepository.AddMessageToRoom (RoomName.value message.Room) message
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
            
            let! _ = roomRepository.RemoveUserFromRoom (RoomName.value room) handle
            
            return room
        }