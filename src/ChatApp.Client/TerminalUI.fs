module ChatApp.Client.TerminalUI

open System
open System.Threading
open System.Threading.Tasks
open ChatApp.Domain.Types
open ChatApp.Client

/// ANSI Console Colors
module Color =
    let reset = "\u001b[0m"
    let red = "\u001b[31m"
    let green = "\u001b[32m"
    let yellow = "\u001b[33m"
    let blue = "\u001b[34m"
    let magenta = "\u001b[35m"
    let cyan = "\u001b[36m"
    let gray = "\u001b[37m"
    let bright = "\u001b[1m"
    
    let colorize color text = $"{color}{text}{reset}"

/// Terminal UI that handles display and input
type TerminalUI(client: ChatClient) as this =
    let mutable running = false
    let mutable inputTask: Task option = None
    let cts = new CancellationTokenSource()
    
    do
        client.OnEvent(fun event -> this.HandleClientEvent(event))
    
    /// Format timestamp to short readable format
    let formatTimestamp (Timestamp timestamp) =
        timestamp.ToString("HH:mm:ss")
    
    /// Format user handle for display
    let formatUserHandle (userHandle: UserHandle) =
        Color.colorize Color.cyan (UserHandle.value userHandle)
    
    /// Format room name for display
    let formatRoomName (roomName: RoomName) =
        Color.colorize Color.green (RoomName.value roomName)
    
    /// Format a full message for display
    let formatMessage (message: Message) =
        let timeStr = formatTimestamp message.Timestamp
        let authorStr = formatUserHandle message.Author
        let content = MessageContent.value message.Content
        $"[{timeStr}] {authorStr}: {content}" 
    
    /// Clear the console for a refresh
    let clearConsole() =
        Console.Clear()
    
    /// Display the header including current room
    let displayHeader (state: ClientState) =
        let usernameStr = 
            match state.Username with
            | Some handle -> formatUserHandle handle
            | None -> Color.colorize Color.red "Not logged in"
        
        let roomStr =
            match state.CurrentRoom with
            | Some room -> $"Room: {formatRoomName room}"
            | None -> Color.colorize Color.yellow "Not in a room"
        
        printfn $"{Color.bright}F# Chat Client{Color.reset} - User: {usernameStr} - {roomStr}"
        printfn $"""{String.replicate 60 "-"}"""

    /// Display available commands
    let displayCommands() =
        printfn $"{Color.bright}Commands:{Color.reset}"
        printfn $"  /join <room>    - Join a chat room"
        printfn $"  /leave          - Leave current room"
        printfn $"  /list           - List available rooms"
        printfn $"  /clear          - Clear the screen"
        printfn $"  /quit           - Exit the application"
        printfn $"  <message>       - Send message to current room"
    
    /// Display the message history
    let displayMessages (messages: Message list) =
        printfn $"{Color.bright}Messages:{Color.reset}"
        
        if messages.IsEmpty then
            printfn "  No messages yet"
        else
            messages
            |> List.sortBy (fun m -> match m.Timestamp with Timestamp t -> t)
            |> List.iter (fun m -> printfn $"  %s{formatMessage m}")
    
    /// Display the list of available rooms
    let displayRooms (rooms: (RoomName * int) list) =
        printfn $"{Color.bright}Available Rooms:{Color.reset}"
        
        if rooms.IsEmpty then
            printfn "  No rooms available"
        else
            rooms
            |> List.iter (fun (roomName, participantCount) -> 
                let name = formatRoomName roomName
                printfn $"  {name} ({participantCount} participants)")
    
    /// Display an error message
    let displayError (message: string) =
        let errorMessage = Color.colorize Color.red $"ERROR: {message}"
        printfn $"%s{errorMessage}"
    
    /// Display a notification message
    let displayNotification (message: string) =
        let notification = Color.colorize Color.yellow message
        printfn $"%s{notification}"
    
    /// Display the entire UI
    let refreshUI (state: ClientState) =
        clearConsole()
        displayHeader state
        printfn ""
        
        match state.CurrentRoom with
        | Some _ -> displayMessages state.RoomHistory
        | None -> 
            if state.AvailableRooms.IsEmpty then
                displayNotification "Use /list to see available rooms"
            else
                displayRooms state.AvailableRooms
        
        printfn ""
        displayCommands()
        printfn ""
        Console.Write("> ")
    
    /// Parse and execute command from user input
    let executeCommand (input: string) =
        if String.IsNullOrWhiteSpace(input) then
            Console.Write("> ") // Just reshow prompt for empty input
        elif input.StartsWith("/") then
            let parts = input.TrimStart('/').Split(' ', StringSplitOptions.RemoveEmptyEntries)
            match parts with
            | [| "join"; roomName |] -> 
                match client.State.Username with
                | Some handle -> client.JoinRoom(UserHandle.value handle, roomName) |> ignore
                | None -> 
                    displayError "Please set a username first"
                    Console.Write("> ")
            | [| "leave" |] -> 
                client.LeaveRoom() |> ignore
            | [| "list" |] -> 
                client.ListRooms() |> ignore
            | [| "clear" |] -> 
                refreshUI client.State
            | [| "quit" |] | [| "exit" |] ->
                running <- false
            | _ -> 
                displayError $"Unknown command: {input}"
                Console.Write("> ")
        else
            // Regular message
            match client.State.CurrentRoom with
            | Some _ -> 
                client.SendMessage(input) |> ignore
                // Don't write prompt here - wait for message confirmation
            | None -> 
                displayError "Join a room first to send messages"
                Console.Write("> ")
    
    /// Handle input loop
    let rec inputLoop() =
        while running && not cts.Token.IsCancellationRequested do
            let input = Console.ReadLine()
            executeCommand input
            // Don't write prompt here - let events handle it or the command itself
    
    /// Handle client events
    member private _.HandleClientEvent(event: ClientEvent) =
        match event with
        | MessageReceived message ->
            // Display all received messages (including our own for confirmation)
            Console.WriteLine($"  {formatMessage message}")
            Console.Write("> ")
        
        | JoinedRoom (roomName, _) ->
            refreshUI client.State
            displayNotification $"Joined room {formatRoomName roomName}"
            Console.Write("> ")
        
        | LeftRoom roomName ->
            refreshUI client.State
            displayNotification $"Left room {formatRoomName roomName}"
            Console.Write("> ")
        
        | RoomsListed _rooms ->
            // Don't refresh UI for room list - just display the rooms inline
            displayRooms client.State.AvailableRooms
            Console.Write("> ")
        
        | UserJoinedRoom (handle, roomName) ->
            // Only show if it's not us and we're in the same room
            match client.State.Username, client.State.CurrentRoom with
            | Some ourHandle, Some ourRoom when handle <> ourHandle && roomName = ourRoom ->
                displayNotification $"{formatUserHandle handle} joined {formatRoomName roomName}"
                Console.Write("> ")
            | _ -> () // Don't show our own join or joins from other rooms
        
        | UserLeftRoom (handle, roomName) ->
            // Only show if it's not us and we're in the same room
            match client.State.Username, client.State.CurrentRoom with
            | Some ourHandle, Some ourRoom when handle <> ourHandle && roomName = ourRoom ->
                displayNotification $"{formatUserHandle handle} left {formatRoomName roomName}"
                Console.Write("> ")
            | _ -> () // Don't show our own leave or leaves from other rooms
        
        | ErrorOccurred message ->
            displayError message
            Console.Write("> ")
        
        | ConnectionClosed ->
            displayError "Connection to server closed"
            running <- false
    
    /// Start the terminal UI
    member this.Start() =
        running <- true
        refreshUI client.State
        
        // Start input processing task
        inputTask <- Some (Task.Run(inputLoop, cts.Token))
    
    /// Stop the terminal UI
    member this.Stop() =
        running <- false
        cts.Cancel()
        
        match inputTask with
        | Some task ->
            try task.Wait(1000) |> ignore
            with _ -> ()
        | None -> ()
    
    /// Property to check if UI is running
    member _.Running = running
    
    interface IDisposable with
        member this.Dispose() =
            this.Stop()
            cts.Dispose()