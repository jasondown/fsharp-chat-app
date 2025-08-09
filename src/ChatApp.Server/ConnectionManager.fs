namespace ChatApp.Server

open System
open System.Net.Sockets
open ChatApp.Domain.Types
open ChatApp.Domain.Protocol
open ChatApp.Application
open ChatApp.Infrastructure.Protocol
open ChatApp.Infrastructure.Repositories
open Serilog

/// Represents a connected client
type ClientConnection = {
    Id: Guid
    Client: TcpClient
    UserHandle: UserHandle option
    CurrentRoom: RoomName option
}

/// Messages for the connection manager actor
type ConnectionManagerMessage =
    | ClientConnected of Guid * TcpClient
    | ClientDisconnected of Guid
    | ClientCommand of Guid * ClientCommand
    | BroadcastToRoom of RoomName * ServerMessage
    | BroadcastToAll of ServerMessage
    | GetConnectedClients of AsyncReplyChannel<ClientConnection list>

/// Thread-safe connection manager using MailboxProcessor
type ConnectionManager(logger: ILogger) =
    
    let chatService = ChatService(InMemoryRoomRepository())
    
    /// Actor that manages all client connections
    let connectionActor = MailboxProcessor.Start(fun inbox ->
        
        // Define helper functions that need access to inbox
        let sendMessageToClient (client: TcpClient) (message: ServerMessage) = async {
            try
                match TcpProtocol.sendServerMessage client message with
                | Result.Ok () -> ()
                | Result.Error err ->
                    logger.Warning("Failed to send message to client: {Error}", err)
            with
            | ex -> logger.Error(ex, "Exception sending message to client")
        }
        
        // Helper function to get users in a specific room
        let getUsersInRoom (connections: Map<Guid, ClientConnection>) (roomName: RoomName) =
            connections
            |> Map.values
            |> Seq.choose (function
                | { CurrentRoom = Some r; UserHandle = Some h } when r = roomName -> Some h
                | _ -> None)
            |> Seq.toList
        
        let rec processClientCommand (connections: Map<Guid, ClientConnection>) (connection: ClientConnection) (command: ClientCommand) = async {
            try
                match command with
                | JoinRoom (userHandle, roomName) ->
                    match chatService.JoinRoom(UserHandle.value userHandle, RoomName.value roomName) with
                    | Result.Ok (joinedRoomName, messageHistory) ->
                        // Send room history to the joining client
                        let joinedMsg = JoinedRoom (joinedRoomName, messageHistory)
                        do! sendMessageToClient connection.Client joinedMsg
                        
                        // Notify other users in the room
                        let userJoinedMsg = UserJoined (userHandle, roomName)
                        inbox.Post(BroadcastToRoom (roomName, userJoinedMsg))
                        
                        // Update connection to track user and room
                        return { connection with UserHandle = Some userHandle; CurrentRoom = Some roomName }
                    | Result.Error err ->
                        let errorMsg = ServerMessage.Error $"Failed to join room: {err}"
                        do! sendMessageToClient connection.Client errorMsg
                        return connection
                        
                | LeaveRoom userHandle ->
                    match connection.CurrentRoom with
                    | Some roomName ->
                        match chatService.LeaveRoom(UserHandle.value userHandle, RoomName.value roomName) with
                        | Result.Ok _ ->
                            let leftRoomMsg = LeftRoom roomName
                            do! sendMessageToClient connection.Client leftRoomMsg
                            
                            let userLeftMsg = UserLeft (userHandle, roomName)
                            inbox.Post(BroadcastToRoom (roomName, userLeftMsg))
                            
                            return { connection with CurrentRoom = None }
                        | Result.Error err ->
                            let errorMsg = ServerMessage.Error $"Failed to leave room: {err}"
                            do! sendMessageToClient connection.Client errorMsg
                            return connection
                    | None ->
                        let errorMsg = ServerMessage.Error "Not currently in any room"
                        do! sendMessageToClient connection.Client errorMsg
                        return connection
                        
                | SendMessage (userHandle, roomName, content) ->
                    match chatService.SendMessage(UserHandle.value userHandle, RoomName.value roomName, MessageContent.value content) with
                    | Result.Ok message ->
                        let messageReceivedMsg = MessageReceived message
                        inbox.Post(BroadcastToRoom (roomName, messageReceivedMsg))
                        return connection
                    | Result.Error err ->
                        let errorMsg = ServerMessage.Error $"Failed to send message: {err}"
                        do! sendMessageToClient connection.Client errorMsg
                        return connection
                        
                | ListRooms ->
                    match chatService.ListRooms() with
                    | Result.Ok rooms ->
                        let roomListMsg = RoomList rooms
                        do! sendMessageToClient connection.Client roomListMsg
                        return connection
                    | Result.Error err ->
                        let errorMsg = ServerMessage.Error $"Failed to list rooms: {err}"
                        do! sendMessageToClient connection.Client errorMsg
                        return connection
                        
                | GetRoomHistory roomName ->
                    let notImplementedMsg = ServerMessage.Error "GetRoomHistory not yet implemented"
                    do! sendMessageToClient connection.Client notImplementedMsg
                    return connection
                    
                | ListUsers roomOption ->
                    match roomOption with
                    | Some roomName ->
                        match chatService.GetRoom(RoomName.value roomName) with
                        | Result.Ok _ ->
                            let users = getUsersInRoom connections roomName
                            do! sendMessageToClient connection.Client (UserList (roomName, users))
                            return connection
                        | Result.Error err ->
                            // log err somewhere server-side
                            do! sendMessageToClient connection.Client (ServerMessage.Error $"Room '{RoomName.value roomName}' does not exist")
                            return connection

                    | None ->
                        // List users in current room
                        match connection.CurrentRoom with
                        | Some currentRoom ->
                            let users = getUsersInRoom connections currentRoom
                            let userListMsg = UserList (currentRoom, users)
                            do! sendMessageToClient connection.Client userListMsg
                            return connection
                        | None ->
                            let errorMsg = ServerMessage.Error "Not currently in any room"
                            do! sendMessageToClient connection.Client errorMsg
                            return connection
                    
            with
            | ex ->
                logger.Error(ex, "Error processing command from client {ClientId}", connection.Id)
                let errorMsg = ServerMessage.Error "Internal server error"
                do! sendMessageToClient connection.Client errorMsg
                return connection
        }
        
        let broadcastToRoom (connections: Map<Guid, ClientConnection>) (roomName: RoomName) (message: ServerMessage) (excludeClient: Guid option) = async {
            let roomClients = 
                connections 
                |> Map.toSeq
                |> Seq.map snd
                |> Seq.filter (fun conn -> 
                    match conn.CurrentRoom with
                    | Some currentRoom when currentRoom = roomName -> 
                        match excludeClient with
                        | Some excludeId -> conn.Id <> excludeId
                        | None -> true
                    | _ -> false)
            
            for client in roomClients do
                do! sendMessageToClient client.Client message
        }
        
        let broadcastToAllClients (connections: Map<Guid, ClientConnection>) (message: ServerMessage) = async {
            for KeyValue(_, connection) in connections do
                do! sendMessageToClient connection.Client message
        }
        
        // Main message loop
        let rec loop (connections: Map<Guid, ClientConnection>) = async {
            let! message = inbox.Receive()
            
            match message with
            | ClientConnected (clientId, tcpClient) ->
                logger.Information("Client {ClientId} connected", clientId)
                let newConnection = {
                    Id = clientId
                    Client = tcpClient
                    UserHandle = None
                    CurrentRoom = None
                }
                let updatedConnections = connections.Add(clientId, newConnection)
                return! loop updatedConnections
                
            | ClientDisconnected clientId ->
                logger.Information("Client {ClientId} disconnected", clientId)
                
                match connections.TryFind clientId with
                | Some connection ->
                    match connection.UserHandle, connection.CurrentRoom with
                    | Some userHandle, Some roomName ->
                        match chatService.LeaveRoom(UserHandle.value userHandle, RoomName.value roomName) with
                        | Result.Ok _ ->
                            let userLeftMsg = UserLeft (userHandle, roomName)
                            do! broadcastToRoom connections roomName userLeftMsg (Some clientId)
                        | Result.Error err ->
                            logger.Warning("Failed to remove disconnected user {User} from room {Room}: {Error}", 
                                         UserHandle.value userHandle, RoomName.value roomName, err)
                    | _ -> ()
                | None -> ()
                
                let updatedConnections = connections.Remove(clientId)
                return! loop updatedConnections
                
            | ClientCommand (clientId, command) ->
                logger.Debug("Processing command from client {ClientId}: {Command}", clientId, command)
                
                match connections.TryFind clientId with
                | Some connection ->
                    let! updatedConnection = processClientCommand connections connection command  
                    let updatedConnections = connections.Add(clientId, updatedConnection)
                    return! loop updatedConnections
                | None ->
                    logger.Warning("Received command from unknown client {ClientId}", clientId)
                    return! loop connections
                    
            | BroadcastToRoom (roomName, message) ->
                do! broadcastToRoom connections roomName message None
                return! loop connections
                
            | BroadcastToAll message ->
                do! broadcastToAllClients connections message
                return! loop connections
                
            | GetConnectedClients replyChannel ->
                replyChannel.Reply(connections |> Map.toList |> List.map snd)
                return! loop connections
        }
        
        loop Map.empty
    )
    
    /// Add a new client connection
    member _.AddClient(clientId: Guid, tcpClient: TcpClient) =
        connectionActor.Post(ClientConnected (clientId, tcpClient))
    
    /// Remove a client connection
    member _.RemoveClient(clientId: Guid) =
        connectionActor.Post(ClientDisconnected clientId)
    
    /// Process a command from a client
    member _.ProcessCommand(clientId: Guid, command: ClientCommand) =
        connectionActor.Post(ClientCommand (clientId, command))
    
    /// Broadcast message to all clients in a room
    member _.BroadcastToRoom(roomName: RoomName, message: ServerMessage) =
        connectionActor.Post(BroadcastToRoom (roomName, message))
    
    /// Broadcast message to all clients
    member _.BroadcastToAll(message: ServerMessage) =
        connectionActor.Post(BroadcastToAll message)
    
    /// Get list of connected clients (for debugging/monitoring)
    member _.GetConnectedClients() =
        connectionActor.PostAndReply(GetConnectedClients)