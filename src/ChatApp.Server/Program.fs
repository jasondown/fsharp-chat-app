module ChatApp.Server.Program

open System
open System.Threading
open Serilog
open ChatApp.Server

/// Configure Serilog logger
let configureLogger () =
    LoggerConfiguration()
        .MinimumLevel.Information()
        .WriteTo.Console(outputTemplate = "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
        .WriteTo.File("logs/server-.log", rollingInterval = RollingInterval.Day)
        .CreateLogger()

/// Parse and validate the port number from arguments
let parsePort (args: string[]) (logger: ILogger) =
    if args.Length > 0 then
        match Int32.TryParse(args[0]) with
        | true, p when p > 1024 && p < 65536 ->
            logger.Information("Using provided port: {Port}", p)
            p
        | _ ->
            logger.Warning("Invalid port '{PortArg}' provided. Falling back to default port 5000.", args[0])
            5000
    else
        logger.Information("No port provided. Using default port 5000.")
        5000

/// Handle Ctrl+C for graceful shutdown
let setupShutdownHandler (logger: ILogger) (server: TcpChatServer) =
    let cts = new CancellationTokenSource()
    Console.CancelKeyPress.Add(fun args ->
        server.Stop()
        logger.Information("Ctrl+C received. Shutting down server...")
        args.Cancel <- true
        cts.Cancel()
    )
    cts

[<EntryPoint>]
let main args =
    let logger = configureLogger()
    Log.Logger <- logger // Optional: allows global use if needed

    try
        logger.Information("ChatApp Server starting...")

        let port = parsePort args logger

        use server = new TcpChatServer(port, logger)
        let cts = setupShutdownHandler logger server

        logger.Information("Server listening on port {Port}. Press Ctrl+C to stop.", port)

        try
            Async.RunSynchronously(server.StartAsync(), cancellationToken = cts.Token)
        with
        | :? OperationCanceledException ->
            logger.Information("Cancellation requested. Server is shutting down.")

        logger.Information("Server stopped.")
        0
    with
    | ex ->
        logger.Fatal(ex, "Fatal error in ChatApp Server")
        Console.Error.WriteLine($"Fatal error: {ex.Message}")
        1
