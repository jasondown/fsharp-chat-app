namespace ChatApp.Server

open System
open System.Net
open System.Net.Sockets
open System.Threading
open System.Threading.Tasks
open ChatApp.Infrastructure.Protocol
open Serilog

/// Main TCP server that accepts client connections
type TcpChatServer(port: int, logger: ILogger) =
    
    let connectionManager = ConnectionManager(logger)
    let mutable tcpListener: TcpListener option = None
    let mutable isRunning = false
    let cancellationTokenSource = new CancellationTokenSource()
    
    /// Handle a single client connection
    let handleClient (clientId: Guid) (tcpClient: TcpClient) = async {
        logger.Information("Starting client handler for {ClientId}", clientId)
        connectionManager.AddClient(clientId, tcpClient)
        
        try
            let mutable continueProcessing = true
            while continueProcessing && tcpClient.Connected && not cancellationTokenSource.Token.IsCancellationRequested do
                try
                    let! result = Async.AwaitTask(Task.Run(fun () ->
                        TcpProtocol.readClientCommand tcpClient
                    ))
                    
                    match result with
                    | Result.Ok command ->
                        connectionManager.ProcessCommand(clientId, command)
                    | Result.Error TcpProtocol.ConnectionClosed ->
                        logger.Information("Client {ClientId} closed connection", clientId)
                        continueProcessing <- false
                    | Result.Error error ->
                        logger.Warning("Error reading from client {ClientId}: {Error}", clientId, error)
                        continueProcessing <- false
                with
                | :? ObjectDisposedException ->
                    logger.Information("Client {ClientId} connection disposed", clientId)
                    continueProcessing <- false
                | ex ->
                    logger.Error(ex, "Exception handling client {ClientId}", clientId)
                    continueProcessing <- false
        finally
            logger.Information("Cleaning up client {ClientId}", clientId)
            connectionManager.RemoveClient(clientId)
            try
                tcpClient.Close()
                tcpClient.Dispose()
            with
            | ex -> logger.Warning(ex, "Error disposing client {ClientId}", clientId)
    }
    
    /// Accept incoming client connections
    let acceptClients (listener: TcpListener) = async {
        logger.Information("Server listening for connections on port {Port}", port)
        
        try
            let mutable continueAccepting = true
            while continueAccepting && not cancellationTokenSource.Token.IsCancellationRequested do
                try
                    let! tcpClient = Async.AwaitTask(listener.AcceptTcpClientAsync())
                    let clientId = Guid.NewGuid()
                    let endpoint = tcpClient.Client.RemoteEndPoint.ToString()
                    
                    logger.Information("Accepted new client connection {ClientId} from {Endpoint}", clientId, endpoint)
                    
                    // Handle each client on a separate async workflow
                    Async.Start(handleClient clientId tcpClient, cancellationTokenSource.Token)
                    
                with
                | :? ObjectDisposedException when cancellationTokenSource.Token.IsCancellationRequested ->
                    logger.Information("Server stop requested, stopping client acceptance")
                    continueAccepting <- false
                | ex ->
                    logger.Error(ex, "Error accepting client connection")
                    do! Async.Sleep(1000) // Brief pause before retrying
        with
        | ex -> logger.Error(ex, "Fatal error in client acceptance loop")
    }
    
    /// Start the server
    member _.StartAsync() = async {
        if isRunning then
            logger.Warning("Server is already running")
            return ()
            
        try
            let listener = new TcpListener(IPAddress.Any, port)
            tcpListener <- Some listener
            
            listener.Start()
            isRunning <- true
            
            logger.Information("TCP Chat Server started on port {Port}", port)
            
            // Start accepting clients
            do! acceptClients listener
            
        with ex ->
            logger.Error(ex, "Failed to start server on port {Port}", port)
            isRunning <- false
            // Don't use reraise() here - raise the exception directly
            raise ex
    }
    
    /// Stop the server
    member _.Stop() =
        logger.Information("Stopping TCP Chat Server...")
        
        if isRunning then
            cancellationTokenSource.Cancel()
            isRunning <- false
            
            match tcpListener with
            | Some listener ->
                try
                    listener.Stop()
                    tcpListener <- None
                    logger.Information("TCP listener stopped")
                with
                | ex -> logger.Warning(ex, "Error stopping TCP listener")
            | None -> ()
            
            logger.Information("TCP Chat Server stopped")
        else
            logger.Information("Server was not running")
    
    /// Check if server is running
    member _.IsRunning = isRunning
    
    /// Get connected clients for monitoring
    member _.GetConnectedClients() =
        connectionManager.GetConnectedClients()
    
    interface IDisposable with
        member this.Dispose() =
            this.Stop()
            cancellationTokenSource.Dispose()
