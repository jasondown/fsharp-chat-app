namespace ChatApp.Domain

open System
open System.Text.RegularExpressions

/// Core domain types and validation for the chat application
module Types =
    
    /// Domain validation errors
    type ValidationError =
        | EmptyUserHandle
        | InvalidUserHandleChars of string
        | UserHandleTooLong of int
        | EmptyRoomName  
        | InvalidRoomNameChars of string
        | RoomNameTooLong of int
        | EmptyMessageContent
        | MessageContentTooLong of int
    
    /// Configuration for validation rules
    module private Config =
        let maxUserHandleLength = 20
        let maxRoomNameLength = 30
        let maxMessageLength = 1000
        let validIdentifierPattern = @"^[a-zA-Z0-9_-]+$"
    
    /// User handle - must be non-empty and contain only valid characters
    type UserHandle = private UserHandle of string
    
    /// Room name - must be non-empty and contain only valid characters  
    type RoomName = private RoomName of string
    
    /// Message content - must be non-empty
    type MessageContent = private MessageContent of string
    
    /// Unique identifier for messages
    type MessageId = MessageId of Guid
    
    /// Timestamp for when messages were sent
    type Timestamp = Timestamp of DateTimeOffset
    
    /// Validation and creation functions for domain types
    module UserHandle =
        /// Create a validated UserHandle
        let create (input: string) : Result<UserHandle, ValidationError> =
            if String.IsNullOrWhiteSpace(input) then
                Error EmptyUserHandle
            elif input.Length > Config.maxUserHandleLength then
                Error (UserHandleTooLong input.Length)
            elif not (Regex.IsMatch(input, Config.validIdentifierPattern)) then
                Error (InvalidUserHandleChars input)
            else
                Ok (UserHandle input)
        
        /// Extract the string value
        let value (UserHandle userHandle) = userHandle
    
    module RoomName =
        /// Create a validated RoomName
        let create (input: string) : Result<RoomName, ValidationError> =
            if String.IsNullOrWhiteSpace(input) then
                Error EmptyRoomName
            elif input.Length > Config.maxRoomNameLength then
                Error (RoomNameTooLong input.Length)
            elif not (Regex.IsMatch(input, Config.validIdentifierPattern)) then
                Error (InvalidRoomNameChars input)
            else
                Ok (RoomName input)
        
        /// Extract the string value
        let value (RoomName roomName) = roomName
    
    module MessageContent =
        /// Create a validated MessageContent
        let create (input: string) : Result<MessageContent, ValidationError> =
            if String.IsNullOrWhiteSpace(input) then
                Error EmptyMessageContent
            elif input.Length > Config.maxMessageLength then
                Error (MessageContentTooLong input.Length)
            else
                Ok (MessageContent input)
        
        /// Extract the string value
        let value (MessageContent content) = content
    
    /// A chat message in a room
    type Message = {
        Id: MessageId
        Content: MessageContent
        Author: UserHandle
        Room: RoomName
        Timestamp: Timestamp
    }
    
    /// A chat room containing messages and participants
    type Room = {
        Name: RoomName
        Messages: Message list
        Participants: Set<UserHandle>
        Created: Timestamp
    }
    
    /// Connected user information
    type User = {
        Handle: UserHandle
        CurrentRoom: RoomName option
        ConnectedAt: Timestamp
    }