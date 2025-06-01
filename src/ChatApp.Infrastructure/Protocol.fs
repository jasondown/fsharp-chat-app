namespace ChatApp.Infrastructure.Protocol

open System
open System.Text
open System.Net.Sockets
open FsToolkit.ErrorHandling
open ChatApp.Domain.Protocol
open ChatApp.Infrastructure.Serialization

/// Protocol formatting and framing
module TcpProtocol =
    
    /// Error types for TCP protocol handling
    type ProtocolError =
        | ConnectionClosed
        | InvalidMessageFormat of string
        | SerializationError of string
        | NetworkError of exn
    
    /// Maximum message size
    let private maxMessageSize = 1024 * 1024  // 1MB
    
    /// Send a message with proper framing
    let sendMessage (client: TcpClient) (message: string) : Result<unit, ProtocolError> =
        try
            let stream = client.GetStream()
            let messageBytes = Encoding.UTF8.GetBytes(message)
            
            if messageBytes.Length > maxMessageSize then
                Result.Error (InvalidMessageFormat $"Message exceeds maximum size: {messageBytes.Length} bytes")
            else
                let lengthBytes = BitConverter.GetBytes(messageBytes.Length)
                stream.Write(lengthBytes, 0, lengthBytes.Length)
                stream.Write(messageBytes, 0, messageBytes.Length)
                stream.Flush()
                Result.Ok ()
        with
        | ex -> Result.Error (NetworkError ex)
    
    /// Read a message with proper framing
    let readMessage (client: TcpClient) : Result<string, ProtocolError> =
        try
            let stream = client.GetStream()
            
            if not client.Connected || not stream.CanRead then
                Result.Error ConnectionClosed
            else
                let lengthBuffer = Array.zeroCreate 4
                let bytesRead = stream.Read(lengthBuffer, 0, 4)
                
                if bytesRead = 0 then
                    Result.Error ConnectionClosed
                elif bytesRead < 4 then
                    Result.Error (InvalidMessageFormat "Incomplete length prefix")
                else
                    let messageLength = BitConverter.ToInt32(lengthBuffer, 0)
                    
                    if messageLength <= 0 then
                        Result.Error (InvalidMessageFormat $"Invalid message length: {messageLength}")
                    elif messageLength > maxMessageSize then
                        Result.Error (InvalidMessageFormat $"Message too large: {messageLength} bytes")
                    else
                        let messageBuffer = Array.zeroCreate messageLength
                        let rec readData bytesReadSoFar =
                            if bytesReadSoFar < messageLength then
                                let read = stream.Read(messageBuffer, bytesReadSoFar, messageLength - bytesReadSoFar)
                                if read = 0 then
                                    Result.Error ConnectionClosed
                                else
                                    readData (bytesReadSoFar + read)
                            else
                                Result.Ok ()
                                
                        match readData 0 with
                        | Result.Ok () ->
                            let message = Encoding.UTF8.GetString(messageBuffer)
                            Result.Ok message
                        | Result.Error err -> Result.Error err
        with
        | ex -> Result.Error (NetworkError ex)
    
    /// Send a client command
    let sendClientCommand (client: TcpClient) (command: ClientCommand) : Result<unit, ProtocolError> =
        let json = JsonSerialization.serializeClientCommand command
        sendMessage client json
    
    /// Read a server message
    let readServerMessage (client: TcpClient) : Result<ServerMessage, ProtocolError> =
        result {
            let! json = readMessage client
            let! message = JsonSerialization.deserializeServerMessage json
                           |> Result.mapError SerializationError
            return message
        }
    
    /// Send a server message
    let sendServerMessage (client: TcpClient) (message: ServerMessage) : Result<unit, ProtocolError> =
        let json = JsonSerialization.serializeServerMessage message
        sendMessage client json
    
    /// Read a client command
    let readClientCommand (client: TcpClient) : Result<ClientCommand, ProtocolError> =
        result {
            let! json = readMessage client
            let! command = JsonSerialization.deserializeClientCommand json
                           |> Result.mapError SerializationError
            return command
        }
