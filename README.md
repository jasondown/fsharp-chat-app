# F# Chat Application Strategy & Requirements

## Application Requirements

- **Persistence**: Messages stored in memory for now
- **Client Flow**:
  - Terminal-based client application
  - Users provide user handle when starting the client (and optionally a room)
  - Show previous messages upon joining a room
  - Real-time posting and receiving of messages within rooms

## Technical Architecture

### Core Strategy: TCP + MailboxProcessor + Functional Design

- **Communication**: Raw TCP sockets with custom protocol
- **Concurrency**: F# MailboxProcessor (actor model) for thread-safe state management
- **Serialization**: Thoth.Json.Net for F#-idiomatic JSON handling
- **Error Handling**: FsToolkit.ErrorHandling with Railway-oriented programming
- **Logging**: Serilog for structured logging
- **CLI**: Argu for command-line argument parsing

### Design Patterns

- **Domain-Driven Design** with functional programming
- **Command/Query Separation**
- **Railway-Oriented Programming** using `Result<'T, 'Error>`
- **Agent-Based Concurrency** with MailboxProcessor
- **Pure F# ADTs** for all message types and domain models

### **Project Structure**

```text
ChatApp/
├── src/
│   ├── ChatApp.Domain/          # Core domain types & ADTs
│   ├── ChatApp.Application/     # Business logic & use cases
│   ├── ChatApp.Infrastructure/  # TCP, storage, serialization
│   ├── ChatApp.Server/          # Server executable
│   └── ChatApp.Client/          # Terminal client executable
├── tests/
└── ChatApp.sln
```

### Technology Stack

- **Required Libraries**:
  - `FsToolkit.ErrorHandling` - Computation expressions & error handling
  - `Thoth.Json.Net` - F#-first JSON serialization
  - `Serilog` - Structured logging
  - `Argu` - F#-idiomatic CLI parsing
- **Runtime**: .NET 9
- **Platform**: Linux with JetBrains Rider

## Development Instructions

### **Response Format Requirements For AI Model**

- Break solution into **small, buildable chunks**
- Provide **complete step-by-step instructions** including:
  - Exact terminal commands for project creation
  - Package installation commands
  - File system locations for new files
- Code suggestions:
  - Explanations as code is presented
  - Always provide entire file contents after showing new code so it can be copied to replace a file
- When asking to see a file, do not suggest `cat` commands. I will provide the entire file contents.
- **Wait for confirmation** after each chunk before proceeding
- Allow opportunities to address **unexpected errors**
- Follow git Workflow

### **Git Workflow**

- Include `.gitignore` file setup if not already in place
- For a feature of bugfix, always suggest creating a branch (and provide the name) before making the code changes
- Provide **commit message suggestions** following conventional commit style (e.g. feat:/fix:/refactor:/test: etc.)
- Recommend **when to commit** during development
- Track project progress with git throughout

### **Development Environment**
- **OS**: Linux
- **IDE**: JetBrains Rider
- **Language**: F#
- **Framework**: .NET 9

---

## Project Status: Fully Functional Chat Application

### Completed Features

#### Project Structure & Setup
- Created solution with 5 projects following DDD architecture
- Set up proper project references and dependencies
- Initialized git repository with comprehensive `.gitignore`
- All projects building successfully
- Comprehensive test suite

#### Domain Layer
- Core domain types with validation (UserHandle, RoomName, MessageContent)
- Message and Room entities with proper encapsulation
- Client-Server protocol messages defined
- Connection events for state management
- Railway-oriented error handling with custom error types

#### Infrastructure Layer
- In-memory room repository with thread-safe operations
- JSON serialization using Thoth.Json.Net
- TCP protocol with length-prefixed framing
- High-level send/receive functions for type-safe communication

#### Application Layer
- ChatService with room management operations
- Command handlers for join/leave/send/list operations
- Automatic room creation when joining non-existent rooms
- Repository interface pattern for persistence abstraction

#### Server Implementation
- TcpChatServer with async client connection handling
- ConnectionManager using MailboxProcessor (actor model)
- Real-time message broadcasting to room participants
- User join/leave notifications
- User list tracking per room
- Graceful shutdown and resource cleanup
- Structured logging with Serilog

#### Client Implementation
- Terminal-based UI with ANSI color support
- Argument parsing with Argu
- Real-time message display
- Command system (/join, /leave, /list, /users, /history, /quit)
- Context-aware command display
- Async message handling with background tasks
- Event-driven UI updates

#### User List Feature
- `/users` command to list users in current room
- `/users <room>` command to list users in any room
- Proper error handling for non-existent rooms
- Real-time user tracking in ConnectionManager
- Full test coverage for the feature

#### Room History Feature
- `/history` command to reload message history for current room
- Server-side GetRoomHistory command handling
- RoomHistory protocol message for server responses
- Automatic history loading when joining a room
- Error handling for history requests in non-existent rooms
- Full test coverage for the feature

### Current File Structure

```bash
tree -I '[Bb]in|[Oo]bj'
.
├── ChatApp.sln
├── src
│   ├── ChatApp.Application
│   │   ├── ChatApp.Application.fsproj
│   │   ├── Commands.fs
│   │   └── Services.fs
│   ├── ChatApp.Client
│   │   ├── Args.fs
│   │   ├── ChatApp.Client.fsproj
│   │   ├── ChatClient.fs
│   │   ├── Program.fs
│   │   └── TerminalUI.fs
│   ├── ChatApp.Domain
│   │   ├── ChatApp.Domain.fsproj
│   │   ├── Protocol.fs
│   │   └── Types.fs
│   ├── ChatApp.Infrastructure
│   │   ├── ChatApp.Infrastructure.fsproj
│   │   ├── Protocol.fs
│   │   ├── Repositories.fs
│   │   └── Serialization.fs
│   └── ChatApp.Server
│       ├── ChatApp.Server.fsproj
│       ├── ConnectionManager.fs
│       ├── Program.fs
│       └── Server.fs
└── tests
   └── ChatApp.Tests
       ├── ApplicationTests.fs
       ├── ChatApp.Tests.fsproj
       ├── ClientTests.fs
       ├── DomainTests.fs
       ├── InfrastructureTests.fs
       ├── Program.fs
       └── ServerTests.fs
```

### Recent Bug Fixes (Latest Sessions)

#### Console Input Issues
   - Fixed `/list` command requiring enter to be pressed twice
   - Fixed `/clear` command showing double prompt
   - Removed `Console.SetCursorPosition` calls that were interfering with input buffer
   - Removed automatic room listing on startup that was causing race conditions

#### Double Enter Requirement
   - Fixed issue where sending messages required pressing enter twice
   - Root cause was `Console.SetCursorPosition` interfering with `Console.ReadLine`
   - Also fixed related UI display issues with notifications
   - Improved console input/output synchronization
   
#### Quit Command Error Message
   - Fixed misleading "Connection to server closed" error when using `/quit`
   - Added `isQuitting` flag to track graceful shutdown
   - Connection closed errors now only show for unexpected disconnections
   - Improves user experience when exiting the application normally
  
### How to Run

#### Start the Server
```bash
dotnet run --project src/ChatApp.Server/
# Or specify a custom port:
dotnet run --project src/ChatApp.Server/ -- --port 5001
```

#### Start Client(s)
```bash
# Basic usage
dotnet run --project src/ChatApp.Client/ -- --username alice

# With custom host/port
dotnet run --project src/ChatApp.Client/ -- --username bob --host localhost --port 5001

# Join room immediately
dotnet run --project src/ChatApp.Client/ -- --username charlie --room general
```

#### Client Commands
- `/join <room>` - Join or create a chat room
- `/leave` - Leave current room
- `/list` - List available rooms
- `/users` - List users in current room (when in a room)
- `/users <room>` - List users in specified room
- `/history` - Reload message history for current room (when in a room)
- `/clear` - Clear the screen
- `/quit` - Exit the application
- `<message>` - Send message to current room

### Remaining Features to Implement

1. **Default Rooms** - Server could start with pre-defined rooms
2. **Private Messages** - Direct messaging between users
3. **Persistence** - Save rooms/messages to disk
4. **Reconnection** - Handle network interruptions gracefully
5. **Room Management** - Delete empty rooms, room admin features

### Known Issues

1. **Messages are stored newest-first** (prepended to list)
2. **No rate limiting** or spam protection
3. **No maximum room/message limits**
4. **Room switching without leave** - When a user joins a new room without leaving their current room, the participant count in the first room remains incorrect (user is still counted but doesn't appear in `/users` list). The system doesn't broadcast a leave notification to the first room's participants. Need to decide whether to support multi-room membership or enforce single-room occupancy.

## Test Coverage

- **Domain Layer**: 6 tests (validation logic)
- **Infrastructure Layer**: 13 tests (repository, serialization, ListUsers/UserList)
- **Application Layer**: 9 tests (business logic, room creation, GetRoom)
- **Server Layer**: 13 tests (connection management, TCP communication, ListUsers handling, GetRoomHistory)
- **Client Layer**: 10 tests (username management, ListUsers methods, GetRoomHistory)
- **Total**: 51 tests, all passing
