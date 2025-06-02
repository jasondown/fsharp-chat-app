module ChatApp.Tests.ServerTests

open System
open System.Net
open System.Net.Sockets
open System.Threading
open System.Threading.Tasks
open Xunit
open Serilog
open ChatApp.Domain.Protocol
open ChatApp.Server
open ChatApp.Infrastructure.Protocol

/// Create a test logger
let createTestLogger() : ILogger =
    LoggerConfiguration()
        .MinimumLevel.Debug()
        .WriteTo.Console()
        .CreateLogger()

/// Helper for creating a test client connection
let createTestClient(port: int) =
    let client = new TcpClient()
    client.Connect(IPAddress.Loopback, port)
    client

/// Tests for ConnectionManager
module ConnectionManagerTests =
    
    [<Fact>]
    let ``ConnectionManager should track client connections`` () =
        let logger = createTestLogger()
        let manager = ConnectionManager(logger)
        
        let clientId = Guid.NewGuid()
        use client = new TcpClient()
        
        manager.AddClient(clientId, client)
        let initialClients = manager.GetConnectedClients()
        Assert.Equal(1, initialClients.Length)
        Assert.Equal(clientId, initialClients[0].Id)
        Assert.Equal(None, initialClients[0].UserHandle)
        Assert.Equal(None, initialClients[0].CurrentRoom)
        
        // Clean up
        manager.RemoveClient(clientId)
        let finalClients = manager.GetConnectedClients()
        Assert.Empty(finalClients)

/// Tests for TcpChatServer
module TcpChatServerTests =
    
    // Helper to run a server for a short period
    let runServerForTest (port: int) (action: TcpChatServer -> Async<unit>) : unit =
        async {
            let logger = createTestLogger()
            use server = new TcpChatServer(port, logger)
            
            // Start server on a background task
            use cts = new CancellationTokenSource()
            let _serverTask = Task.Run(fun () -> 
                try
                    Async.RunSynchronously(server.StartAsync(), cancellationToken = cts.Token)
                with
                | :? OperationCanceledException -> ()
            )
            
            do! Async.Sleep(200) // Give server time to start
            
            try
                do! action server
            finally
                cts.Cancel()
                server.Stop()
        }
        |> Async.RunSynchronously
    
    [<Fact>]
    let ``TcpChatServer should accept client connections`` () =
        let port = 5001
        
        let testAction (server: TcpChatServer) = async {
            // Connect a client
            use _client = createTestClient(port)
            
            do! Async.Sleep(100) // Give server time to process
            
            // Check if client was accepted
            let connectedClients = server.GetConnectedClients()
            Assert.Equal(1, connectedClients.Length)
        }
        
        runServerForTest port testAction
    
    [<Fact>]
    let ``TcpChatServer should handle basic client commands`` () =
        let port = 5002
        
        let testAction (_server: TcpChatServer) = async {
            // Connect a client
            use client = createTestClient(port)
            
            // Send ListRooms command
            let listRoomsCommand = ListRooms
            match TcpProtocol.sendClientCommand client listRoomsCommand with
            | Result.Ok () -> ()
            | Result.Error err -> Assert.Fail($"Failed to send command: {err}")
            
            // Give server time to process
            do! Async.Sleep(100)
            
            // Read the response
            let response = TcpProtocol.readServerMessage client
            
            match response with
            | Result.Ok (RoomList _) -> 
                () // Success - got a room list back
            | Result.Ok other -> 
                Assert.Fail($"Expected RoomList but got: {other}")
            | Result.Error err -> 
                Assert.Fail($"Error reading server response: {err}")
        }
        
        runServerForTest port testAction
