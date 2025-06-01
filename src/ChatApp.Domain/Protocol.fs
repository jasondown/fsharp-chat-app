namespace ChatApp.Domain

/// Messages exchanged between client and server
module Protocol =
    
    open Types
    
    /// Commands sent from client to server
    type ClientCommand =
        | JoinRoom of UserHandle * RoomName
        | LeaveRoom of UserHandle
        | SendMessage of UserHandle * RoomName * MessageContent
        | ListRooms
        | GetRoomHistory of RoomName
    
    /// Server responses and events sent to clients
    type ServerMessage =
        | JoinedRoom of RoomName * Message list  // Room joined with message history
        | LeftRoom of RoomName
        | MessageReceived of Message              // New message in current room
        | RoomList of (RoomName * int) list      // Room names with participant counts
        | UserJoined of UserHandle * RoomName    // Another user joined the room  
        | UserLeft of UserHandle * RoomName      // Another user left the room
        | Error of string                        // Error message
    
    /// Connection state events
    type ConnectionEvent =
        | ClientConnected of UserHandle
        | ClientDisconnected of UserHandle
        | ConnectionLost of UserHandle
        | ServerShutdown