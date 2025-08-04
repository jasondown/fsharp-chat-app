module ChatApp.Client.Program

open System.Threading
open ChatApp.Client.Args

[<EntryPoint>]
let main argv =
    try
        let args = parseArgs argv
        
        let username = args.GetResult Username
        
        // Default values
        let host = args.TryGetResult Host |> Option.defaultValue "localhost"
        let port = args.TryGetResult Port |> Option.defaultValue 5000
        
        printfn "F# Chat Client"
        printfn $"Connecting to {host}:{port} as '{username}'..."
        
        use client = new ChatClient(host, port)
        
        if not (client.Connect()) then
            printfn "Failed to connect. Exiting..."
            1
        else
            // Set the username in the client state BEFORE starting UI
            if not (client.SetUsername(username)) then
                printfn "Failed to set username. Exiting..."
                1
            else
                // Check if we need to join a room right away
                match args.TryGetResult Room with
                | Some roomName ->
                    if not (client.JoinRoom(username, roomName)) then
                        printfn $"Failed to join room '{roomName}'"
                | None -> ()
                
                use ui = new TerminalUI.TerminalUI(client)
                ui.Start()
                                
                // Wait for UI to complete (when user quits)
                while ui.Running do
                    Thread.Sleep(100)
                
                0
            
    with ex ->
        printfn $"Error: {ex.Message}"
        1