module ChatApp.Client.Args

open Argu

/// Command line arguments for the chat client
type ClientArgs =
    | [<Mandatory>] Username of name:string
    | [<AltCommandLine("-r")>] Room of roomName:string
    | [<AltCommandLine("-h")>] Host of hostname:string
    | [<AltCommandLine("-p")>] Port of portNumber:int
    | [<AltCommandLine("-c")>] CreateRoom
    | Version
with
    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | Username _ -> "User handle (required)"
            | Room _ -> "Room to join (optional, can join later)"
            | Host _ -> "Hostname to connect to (default: localhost)"
            | Port _ -> "Port number to connect to (default: 5000)"
            | CreateRoom -> "Create a new room if it doesn't exist"
            | Version -> "Display version information"

/// Parse command line arguments
let parseArgs argv =
    let errorHandler = ProcessExiter()
    let parser = ArgumentParser.Create<ClientArgs>(programName = "chatapp", errorHandler = errorHandler)
    parser.ParseCommandLine(argv)
    