namespace ChatApp.Client

open System
open System.Net.Sockets
open System.Threading
open System.Threading.Tasks
open ChatApp.Domain.Types
open ChatApp.Domain.Protocol
open ChatApp.Infrastructure.Protocol

/// Represents the state of a chat client
type ClientState = {
    Username: UserHandle option
    CurrentRoom: RoomName option
    RoomHistory: Message list
    AvailableRooms: (RoomName * int) list
}

/// Event types for UI updates
type ClientEvent =
    | MessageReceived of Message
    | JoinedRoom of RoomName * Message list
    | LeftRoom of RoomName
    | RoomsListed of (RoomName * int) list
    | UsersListed of RoomName * UserHandle list
    | UserJoinedRoom of UserHandle * RoomName
    | UserLeftRoom of UserHandle * RoomName
    | ErrorOccurred of string
    | ConnectionClosed

/// Chat client with TCP connection to server
type ChatClient(host: string, port: int) =
    let mutable client: TcpClient option = None
    let mutable cancellationSource = new CancellationTokenSource()
    let mutable receiverTask: Task option = None
    let mutable clientState = { 
        Username = None
        CurrentRoom = None 
        RoomHistory = []
        AvailableRooms = []
    }
    
    let onEventHandlers = ResizeArray<ClientEvent -> unit>()
    
    /// Add handler for client events
    member _.OnEvent(handler: ClientEvent -> unit) =
        onEventHandlers.Add(handler)
    
    /// Trigger event to all registered handlers
    member private _.TriggerEvent(event: ClientEvent) =
        for handler in onEventHandlers do
            try
                handler event
            with ex ->
                printfn $"Error in event handler: %s{ex.Message}"
    
    /// Connect to the server
    member this.Connect() =
        if client.IsSome then
            this.Disconnect()
        
        try
            let tcpClient = new TcpClient()
            tcpClient.Connect(host, port)
            client <- Some tcpClient
            cancellationSource <- new CancellationTokenSource()
            
            // Start receiver task to process incoming messages
            receiverTask <- Some (Task.Run(this.MessageReceiver, cancellationSource.Token))
            
            true
        with ex ->
            printfn $"Failed to connect: %s{ex.Message}"
            this.TriggerEvent(ErrorOccurred $"Failed to connect to {host}:{port}: {ex.Message}")
            false
    
    /// Disconnect from the server
    member this.Disconnect() =
        cancellationSource.Cancel()
        
        match client with
        | Some c -> 
            try 
                c.Close()
                c.Dispose()
            with _ -> ()
        | None -> ()
        
        client <- None
        receiverTask <- None
    
    /// Get current client state
    member _.State = clientState
    
    /// Background task to receive messages from server
    member private this.MessageReceiver() : unit =
        let mutable continueReceiving = true
        
        while continueReceiving && not cancellationSource.Token.IsCancellationRequested do
            try
                match client with
                | Some c when c.Connected ->
                    match TcpProtocol.readServerMessage c with
                    | Result.Ok message ->
                        this.ProcessServerMessage(message)
                    | Result.Error TcpProtocol.ConnectionClosed ->
                        continueReceiving <- false
                        this.TriggerEvent(ConnectionClosed)
                    | Result.Error error ->
                        this.TriggerEvent(ErrorOccurred $"Error reading from server: {error}")
                | _ ->
                    continueReceiving <- false
            with ex ->
                if not cancellationSource.Token.IsCancellationRequested then
                    this.TriggerEvent(ErrorOccurred $"Error in message receiver: {ex.Message}")
                continueReceiving <- false
        
        if not cancellationSource.Token.IsCancellationRequested then
            this.TriggerEvent(ConnectionClosed)
    
    /// Process a server message and update state
    member private this.ProcessServerMessage(message: ServerMessage) =
        match message with
        | ServerMessage.JoinedRoom (roomName, messages) ->
            clientState <- { clientState with CurrentRoom = Some roomName; RoomHistory = messages }
            this.TriggerEvent(JoinedRoom (roomName, messages))
        
        | ServerMessage.LeftRoom roomName ->
            clientState <- { clientState with CurrentRoom = None }
            this.TriggerEvent(LeftRoom roomName)
        
        | ServerMessage.MessageReceived message ->
            clientState <- { clientState with RoomHistory = message :: clientState.RoomHistory }
            this.TriggerEvent(MessageReceived message)
            
        | RoomList rooms ->
            clientState <- { clientState with AvailableRooms = rooms }
            this.TriggerEvent(RoomsListed rooms)
            
        | UserList (roomName, users) ->
            this.TriggerEvent(UsersListed (roomName, users))
            
        | UserJoined (userHandle, roomName) ->
            this.TriggerEvent(UserJoinedRoom (userHandle, roomName))
            
        | UserLeft (userHandle, roomName) ->
            this.TriggerEvent(UserLeftRoom (userHandle, roomName))
            
        | ServerMessage.Error errorMessage ->
            this.TriggerEvent(ErrorOccurred errorMessage)
    
    /// Send command to server
    member private this.SendCommand(command: ClientCommand) =
        match client with
        | Some c when c.Connected ->
            match TcpProtocol.sendClientCommand c command with
            | Result.Ok () -> true
            | Result.Error error ->
                this.TriggerEvent(ErrorOccurred $"Failed to send command: {error}")
                false
        | _ ->
            this.TriggerEvent(ErrorOccurred "Not connected to server")
            false
    
    /// Join a room
    member this.JoinRoom(username: string, roomName: string) =
        match UserHandle.create username, RoomName.create roomName with
        | Result.Ok userHandle, Result.Ok roomNameObj ->
            // Update state first to reflect intent
            clientState <- { clientState with Username = Some userHandle }
            this.SendCommand(JoinRoom (userHandle, roomNameObj))
        | Result.Error userErr, _ ->
            this.TriggerEvent(ErrorOccurred $"Invalid username: {userErr}")
            false
        | _, Result.Error roomErr ->
            this.TriggerEvent(ErrorOccurred $"Invalid room name: {roomErr}")
            false
    
    /// Leave current room
    member this.LeaveRoom() =
        match clientState.Username, clientState.CurrentRoom with
        | Some username, Some _ ->
            this.SendCommand(LeaveRoom username)
        | _ ->
            this.TriggerEvent(ErrorOccurred "Not in a room")
            false
    
    /// Send a message to the current room
    member this.SendMessage(message: string) =
        match clientState.Username, clientState.CurrentRoom with
        | Some username, Some roomName ->
            match MessageContent.create message with
            | Result.Ok content ->
                this.SendCommand(SendMessage (username, roomName, content))
            | Result.Error err ->
                this.TriggerEvent(ErrorOccurred $"Invalid message: {err}")
                false
        | _ ->
            this.TriggerEvent(ErrorOccurred "Not in a room")
            false
            
    /// List available rooms
    member this.ListRooms() =
        this.SendCommand(ListRooms)
    
    /// Get room history
    member this.GetRoomHistory(roomName: string) =
        match RoomName.create roomName with
        | Result.Ok roomNameObj ->
            this.SendCommand(GetRoomHistory roomNameObj)
        | Result.Error err ->
            this.TriggerEvent(ErrorOccurred $"Invalid room name: {err}")
            false
    
    /// List users in current room
    member this.ListUsersInCurrentRoom() =
        this.SendCommand(ListUsers None)
    
    /// List users in specific room
    member this.ListUsersInRoom(roomName: string) =
        match RoomName.create roomName with
        | Result.Ok roomNameObj ->
            this.SendCommand(ListUsers (Some roomNameObj))
        | Result.Error err ->
            this.TriggerEvent(ErrorOccurred $"Invalid room name: {err}")
            false
    
    /// Set the username for this client session
    member this.SetUsername(username: string) =
        match UserHandle.create username with
        | Result.Ok userHandle ->
            clientState <- { clientState with Username = Some userHandle }
            true
        | Result.Error err ->
            this.TriggerEvent(ErrorOccurred $"Invalid username: {err}")
            false
    
    interface IDisposable with
        member this.Dispose() =
            this.Disconnect()
            cancellationSource.Dispose()
