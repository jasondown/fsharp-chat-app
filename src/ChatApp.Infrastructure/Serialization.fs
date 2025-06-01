namespace ChatApp.Infrastructure.Serialization

open System
open ChatApp.Domain.Types
open ChatApp.Domain.Protocol
open Thoth.Json.Net

/// JSON serialization helpers for protocol messages
module JsonSerialization =
    
    /// Encoders for domain types
    module private Encoders =
        
        let encodeUserHandle handle =
            Encode.string (UserHandle.value handle)
            
        let encodeRoomName name =
            Encode.string (RoomName.value name)
            
        let encodeMessageContent content =
            Encode.string (MessageContent.value content)
            
        let encodeMessageId (MessageId id) =
            Encode.string (id.ToString())
            
        let encodeTimestamp (Timestamp ts) =
            Encode.string (ts.ToString("o"))  // ISO 8601 format
            
        let encodeMessage (msg: Message) =
            Encode.object [
                "id", encodeMessageId msg.Id
                "content", encodeMessageContent msg.Content
                "author", encodeUserHandle msg.Author
                "room", encodeRoomName msg.Room
                "timestamp", encodeTimestamp msg.Timestamp
            ]
        
        let encodeClientCommand (command: ClientCommand) =
            match command with
            | JoinRoom (handle, room) ->
                Encode.object [
                    "type", Encode.string "JoinRoom"
                    "handle", encodeUserHandle handle
                    "room", encodeRoomName room
                ]
            | LeaveRoom handle ->
                Encode.object [
                    "type", Encode.string "LeaveRoom"
                    "handle", encodeUserHandle handle
                ]
            | SendMessage (handle, room, content) ->
                Encode.object [
                    "type", Encode.string "SendMessage"
                    "handle", encodeUserHandle handle
                    "room", encodeRoomName room
                    "content", encodeMessageContent content
                ]
            | ListRooms ->
                Encode.object [
                    "type", Encode.string "ListRooms"
                ]
            | GetRoomHistory room ->
                Encode.object [
                    "type", Encode.string "GetRoomHistory"
                    "room", encodeRoomName room
                ]
        
        let encodeServerMessage (message: ServerMessage) =
            match message with
            | JoinedRoom (room, messages) ->
                Encode.object [
                    "type", Encode.string "JoinedRoom"
                    "room", encodeRoomName room
                    "messages", Encode.list (List.map encodeMessage messages)
                ]
            | LeftRoom room ->
                Encode.object [
                    "type", Encode.string "LeftRoom"
                    "room", encodeRoomName room
                ]
            | MessageReceived msg ->
                Encode.object [
                    "type", Encode.string "MessageReceived"
                    "message", encodeMessage msg
                ]
            | RoomList rooms ->
                let encodeRoomInfo (name, count) =
                    Encode.object [
                        "name", encodeRoomName name
                        "participantCount", Encode.int count
                    ]
                Encode.object [
                    "type", Encode.string "RoomList"
                    "rooms", Encode.list (List.map encodeRoomInfo rooms)
                ]
            | UserJoined (handle, room) ->
                Encode.object [
                    "type", Encode.string "UserJoined"
                    "handle", encodeUserHandle handle
                    "room", encodeRoomName room
                ]
            | UserLeft (handle, room) ->
                Encode.object [
                    "type", Encode.string "UserLeft"
                    "handle", encodeUserHandle handle
                    "room", encodeRoomName room
                ]
            | Error errorMsg ->
                Encode.object [
                    "type", Encode.string "Error"
                    "message", Encode.string errorMsg
                ]
    
    /// Decoders for domain types        
    module private Decoders =
        
        let userHandleDecoder : Decoder<UserHandle> =
            Decode.string
            |> Decode.andThen (fun s ->
                match UserHandle.create s with
                | Result.Ok handle -> Decode.succeed handle
                | Result.Error e -> Decode.fail $"Invalid user handle: {e}"
            )
        
        let roomNameDecoder : Decoder<RoomName> =
            Decode.string
            |> Decode.andThen (fun s ->
                match RoomName.create s with
                | Result.Ok room -> Decode.succeed room
                | Result.Error e -> Decode.fail $"Invalid room name: {e}"
            )
        
        let messageContentDecoder : Decoder<MessageContent> =
            Decode.string
            |> Decode.andThen (fun s ->
                match MessageContent.create s with
                | Result.Ok content -> Decode.succeed content
                | Result.Error e -> Decode.fail $"Invalid message content: {e}"
            )
        
        let messageIdDecoder : Decoder<MessageId> =
            Decode.string
            |> Decode.andThen (fun s ->
                match Guid.TryParse s with
                | true, guid -> Decode.succeed (MessageId guid)
                | false, _ -> Decode.fail $"Invalid message ID: {s}"
            )
        
        let timestampDecoder : Decoder<Timestamp> =
            Decode.string
            |> Decode.andThen (fun s ->
                match DateTimeOffset.TryParse s with
                | true, dt -> Decode.succeed (Timestamp dt)
                | false, _ -> Decode.fail $"Invalid timestamp: {s}"
            )
        
        let messageDecoder : Decoder<Message> =
            Decode.object (fun get ->
                {
                    Id = get.Required.Field "id" messageIdDecoder
                    Content = get.Required.Field "content" messageContentDecoder
                    Author = get.Required.Field "author" userHandleDecoder
                    Room = get.Required.Field "room" roomNameDecoder
                    Timestamp = get.Required.Field "timestamp" timestampDecoder
                }
            )
        
        let clientCommandDecoder : Decoder<ClientCommand> =
            Decode.field "type" Decode.string
            |> Decode.andThen (fun typeStr ->
                match typeStr with
                | "JoinRoom" ->
                    Decode.object (fun get ->
                        let handle = get.Required.Field "handle" userHandleDecoder
                        let room = get.Required.Field "room" roomNameDecoder
                        JoinRoom (handle, room)
                    )
                | "LeaveRoom" ->
                    Decode.object (fun get ->
                        let handle = get.Required.Field "handle" userHandleDecoder
                        LeaveRoom handle
                    )
                | "SendMessage" ->
                    Decode.object (fun get ->
                        let handle = get.Required.Field "handle" userHandleDecoder
                        let room = get.Required.Field "room" roomNameDecoder
                        let content = get.Required.Field "content" messageContentDecoder
                        SendMessage (handle, room, content)
                    )
                | "ListRooms" -> Decode.succeed ListRooms
                | "GetRoomHistory" ->
                    Decode.object (fun get ->
                        let room = get.Required.Field "room" roomNameDecoder
                        GetRoomHistory room
                    )
                | _ -> Decode.fail $"Unknown command type: {typeStr}"
            )
        
        let serverMessageDecoder : Decoder<ServerMessage> =
            Decode.field "type" Decode.string
            |> Decode.andThen (fun typeStr ->
                match typeStr with
                | "JoinedRoom" ->
                    Decode.object (fun get ->
                        let room = get.Required.Field "room" roomNameDecoder
                        let messages = get.Required.Field "messages" (Decode.list messageDecoder)
                        JoinedRoom (room, messages)
                    )
                | "LeftRoom" ->
                    Decode.object (fun get ->
                        let room = get.Required.Field "room" roomNameDecoder
                        LeftRoom room
                    )
                | "MessageReceived" ->
                    Decode.object (fun get ->
                        let msg = get.Required.Field "message" messageDecoder
                        MessageReceived msg
                    )
                | "RoomList" ->
                    Decode.object (fun get ->
                        let roomsDecoder = Decode.list (
                            Decode.object (fun get ->
                                let name = get.Required.Field "name" roomNameDecoder
                                let count = get.Required.Field "participantCount" Decode.int
                                (name, count)
                            )
                        )
                        let rooms = get.Required.Field "rooms" roomsDecoder
                        RoomList rooms
                    )
                | "UserJoined" ->
                    Decode.object (fun get ->
                        let handle = get.Required.Field "handle" userHandleDecoder
                        let room = get.Required.Field "room" roomNameDecoder
                        UserJoined (handle, room)
                    )
                | "UserLeft" ->
                    Decode.object (fun get ->
                        let handle = get.Required.Field "handle" userHandleDecoder
                        let room = get.Required.Field "room" roomNameDecoder
                        UserLeft (handle, room)
                    )
                | "Error" ->
                    Decode.object (fun get ->
                        let msg = get.Required.Field "message" Decode.string
                        Error msg
                    )
                | _ -> Decode.fail $"Unknown server message type: {typeStr}"
            )
    
    /// Public API
    
    /// Serialize a client command to JSON string
    let serializeClientCommand (command: ClientCommand) : string =
        Encoders.encodeClientCommand command
        |> Encode.toString 0
    
    /// Deserialize a JSON string to a client command
    let deserializeClientCommand (json: string) : Result<ClientCommand, string> =
        Decode.fromString Decoders.clientCommandDecoder json
    
    /// Serialize a server message to JSON string
    let serializeServerMessage (message: ServerMessage) : string =
        Encoders.encodeServerMessage message
        |> Encode.toString 0
    
    /// Deserialize a JSON string to a server message
    let deserializeServerMessage (json: string) : Result<ServerMessage, string> =
        Decode.fromString Decoders.serverMessageDecoder json
