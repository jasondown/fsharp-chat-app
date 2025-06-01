namespace ChatApp.Application

open FsToolkit.ErrorHandling
open System
open ChatApp.Domain.Types
open ChatApp.Domain.Protocol

type CommandError =
    | ValidationError of ValidationError
    | UserNotFound of string
    | RoomNotFound of string
    | AlreadyInRoom of string
    | NotInRoom of string
    | NotAuthorized of string
    | SystemError of string

module Commands =
    
    /// Create a new message
    let createMessage (userHandle: UserHandle) (roomName: RoomName) (content: MessageContent) =
        {
            Id = MessageId(Guid.NewGuid())
            Author = userHandle
            Room = roomName
            Content = content
            Timestamp = Timestamp(DateTimeOffset.UtcNow)
        }
    
    /// Join a room command handler
    let joinRoom
        (userHandle: string)
        (roomName: string)
        // Function to check if room exists and get its history
        (findRoom: string -> Result<Room, CommandError>)
        : Result<RoomName * Message list, CommandError> =
        
        result {
            // Validate inputs
            let! handle = UserHandle.create userHandle
                          |> Result.mapError ValidationError
            
            let! room = RoomName.create roomName
                        |> Result.mapError ValidationError
            
            // Check if room exists and get its message history
            let! existingRoom = findRoom (RoomName.value room)
            
            // Return the room and its message history
            return (room, existingRoom.Messages)
        }
    
    /// Send a message command handler  
    let sendMessage
        (userHandle: string)
        (roomName: string)
        (messageContent: string)
        (findRoom: string -> Result<Room, CommandError>)
        : Result<Message, CommandError> =
        
        result {
            // Validate inputs
            let! handle = UserHandle.create userHandle
                          |> Result.mapError ValidationError
            
            let! room = RoomName.create roomName
                        |> Result.mapError ValidationError
            
            let! content = MessageContent.create messageContent
                           |> Result.mapError ValidationError
            
            // Check if room exists
            let! _ = findRoom (RoomName.value room)
            
            // Create the message
            let message = createMessage handle room content
            
            return message
        }