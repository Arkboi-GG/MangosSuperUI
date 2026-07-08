<!-- provenance: verbose -->
# AsyncSocketAcceptor_posix

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`AsyncSocketAcceptor_posix` implements the POSIX-specific logic for accepting incoming TCP connections in the `wowvmangos` network stack. It manages the lifecycle of a listening socket: creation, binding, event registration (`epoll` on Linux, `kqueue` on BSD/macOS), and connection acceptance. Upon receiving a readiness event, it calls `accept()`, configures the new client socket for non-blocking I/O, and invokes a callback to hand off the connection to higher-level handlers.

## Member-by-Member Behavior

### Initialization and Lifecycle

**`AsyncSocketAcceptor` (Constructor)**
Initializes the acceptor with an `IoContext` pointer and a pre-created native socket handle. Sets `m_wasClosed` to `false` and `m_onNewSocketCallback` to `nullptr`.

**`~AsyncSocketAcceptor` (Destructor)**
Asserts `m_wasClosed` is `true` via `MANGOS_ASSERT`. If false, it triggers `Errors::PrintStacktraceAndThrow`, enforcing that callers explicitly close the socket before destruction.

**`ClosePortAndStopAcceptingNewConnections`**
Sets `m_wasClosed` to `true` and closes the underlying native socket using `::close()`. Called during shutdown by `Master::Run`, `realmd_Main::main`, and `WorldSocketMgr::StopWorldNetworking`.

### Server Setup

**`CreateAndBindServer`**
Static factory method that creates, configures, and registers a new `AsyncSocketAcceptor`:
1.  Parses the bind IP string using `IpAddress::TryParseFromString`; returns `nullptr` on failure.
2.  Creates a TCP socket (`AF_INET`, `SOCK_STREAM`).
3.  Sets `SO_REUSEADDR` to `1` to avoid `TIME_WAIT` conflicts on restart.
4.  Binds the socket to the IP/port using `Internal::inet_pton` and `::bind`.
5.  Starts listening with a backlog of 50.
6.  Instantiates `AsyncSocketAcceptor` and registers it with the event loop:
    *   **Linux**: Uses `epoll_ctl` with `EPOLLIN | EPOLLERR` (level-triggered) via `IoContext::GetUnixEpollDescriptor`.
    *   **BSD/macOS**: Uses `kevent` with `EVFILT_READ | EV_ERROR` via `IoContext::GetKqueueDescriptor`.
7.  Returns a `std::unique_ptr<AsyncSocketAcceptor>` on success, or `nullptr` on any error, logging via `Log.Main::Out`.

### Event Handling and Connection Acceptance

**`AutoAcceptSocketsUntilClose`**
Stores the callback function (`m_onNewSocketCallback`) to be invoked when a new client connects. Called by `Master::SetupRemoteAccessServer`, `realmd_Main::main`, and `WorldSocketMgr::StartWorldNetworking`.

**`OnIoEvent`**
Entry point for I/O events. Delegates directly to `OnNewClientToAcceptAvailable`, ignoring the specific event bitmask since any event on this socket implies readiness to accept.

**`OnNewClientToAcceptAvailable`**
Handles the actual acceptance of a new TCP connection:
1.  Returns early if no callback is set.
2.  Calls `::accept()` to retrieve the new client socket and peer address.
3.  Converts the peer IP to a string using `Internal::inet_ntop` and constructs an `IpEndpoint`.
4.  Wraps the native socket and endpoint in a `SocketDescriptor`.
5.  Sets the new socket to non-blocking mode using `Utils_Unix::SetFdStatusFlag`. If this fails, it logs the error, closes the socket via `SocketDescriptor::CloseSocket`, and returns.
6.  Invokes `m_onNewSocketCallback` with the new `SocketDescriptor`.

## Cross-Unit Boundaries

### Dependencies (Calls Out)

*   **`Internal/inet_pton` / `Internal/inet_ntop`**: Convert between string and binary IP representations in `CreateAndBindServer` and `OnNewClientToAcceptAvailable`.
*   **`IoContext/GetUnixEpollDescriptor` / `IoContext/GetKqueueDescriptor`**: Provide file descriptors for event registration in `CreateAndBindServer`.
*   **`IpAddress/TryParseFromString`**: Validates bind IP strings in `CreateAndBindServer`.
*   **`Log.Main/Out`**: Logs errors throughout the unit.
*   **`SystemErrorToString/SystemErrorToString`**: Converts `errno` to readable strings for logging.
*   **`SocketDescriptor/CloseSocket` / `SocketDescriptor/SocketDescriptor`**: Manages the lifecycle and construction of new client sockets in `OnNewClientToAcceptAvailable`.
*   **`Utils_Unix/SetFdStatusFlag`**: Sets `O_NONBLOCK` on new client sockets in `OnNewClientToAcceptAvailable`.
*   **`Errors/PrintStacktraceAndThrow`**: Triggered by destructor assertion failure.

### Callers (Called By)

*   **`Master/SetupRemoteAccessServer`**, **`realmd_Main/main`**, **`WorldSocketMgr/StartWorldNetworking`**: Call `CreateAndBindServer` and `AutoAcceptSocketsUntilClose` to initialize listeners.
*   **`Master/Run`**, **`realmd_Main/main`**, **`WorldSocketMgr/StopWorldNetworking`**: Call `ClosePortAndStopAcceptingNewConnections` during shutdown.

## Data Model

This unit does not interact with any database tables. It operates entirely on in-memory structures and OS-level socket handles.

## Notable Implementation Details

1.  **Level-Triggered Events**: On Linux, `EPOLLIN` is used instead of `EPOLLET`. This ensures the event loop continues notifying the acceptor until all pending connections are accepted, simplifying logic by avoiding the need to loop until `EAGAIN`.
2.  **Non-Blocking Client Sockets**: New client sockets are immediately set to `O_NONBLOCK` before handoff. This prevents blocking the event loop thread during subsequent I/O operations.
3.  **Strict Lifecycle**: The destructor asserts `m_wasClosed` is true, forcing callers to explicitly close the socket. This prevents accidental descriptor leaks.
4.  **Macro Undefinitions**: `inet_pton` and `inet_ntop` are explicitly undefined on BSD platforms to resolve potential macro conflicts with standard library implementations.

## Member Reference

**AsyncSocketAcceptor**
Constructor initializing `m_ctx`, `m_acceptorNativeSocket`, `m_wasClosed` (false), and `m_onNewSocketCallback` (nullptr).

**CreateAndBindServer**
Static factory creating a TCP socket, setting `SO_REUSEADDR`, binding to IP/port, listening with backlog 50, registering with `epoll`/`kqueue`, and returning a `unique_ptr<AsyncSocketAcceptor>`. Returns `nullptr` on failure.

**~AsyncSocketAcceptor**
Destructor asserting `m_wasClosed` is true; calls `Errors::PrintStacktraceAndThrow` if assertion fails.

**ClosePortAndStopAcceptingNewConnections**
Sets `m_wasClosed` to true and closes the native socket via `::close()`.

**AutoAcceptSocketsUntilClose**
Stores the `std::function` callback for new socket notifications.

**OnNewClientToAcceptAvailable**
Accepts a new connection, converts peer address to `IpEndpoint`, sets socket to non-blocking, and invokes the callback. Closes socket on error.

**OnIoEvent**
Delegates to `OnNewClientToAcceptAvailable`.

---

<!-- machine-true, projected from graph.json -->

## Map — AsyncSocketAcceptor_posix

*Source:* AsyncSocketAcceptor_posix.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AsyncSocketAcceptor | ctor | — | — | — |
| CreateAndBindServer | method | Internal/inet_pton, IoContext/GetUnixEpollDescriptor, IpAddress/TryParseFromString, Log.Main/Out, SystemErrorToString/SystemErrorToString | Master/SetupRemoteAccessServer, realmd_Main/main, WorldSocketMgr/StartWorldNetworking | — |
| ~AsyncSocketAcceptor | dtor | Errors/PrintStacktraceAndThrow | — | — |
| ClosePortAndStopAcceptingNewConnections | method | — | Master/Run, realmd_Main/main, WorldSocketMgr/StopWorldNetworking | — |
| AutoAcceptSocketsUntilClose | method | — | Master/SetupRemoteAccessServer, realmd_Main/main, WorldSocketMgr/StartWorldNetworking | — |
| OnNewClientToAcceptAvailable | method | Internal/inet_ntop, IpEndpoint/IpEndpoint#2, Log.Main/Out, NetworkError/ToString, SocketDescriptor/CloseSocket, SocketDescriptor/SocketDescriptor, SocketDescriptor/SocketDescriptor#2, SystemErrorToString/SystemErrorToString, Utils_Unix/SetFdStatusFlag | — | — |
| OnIoEvent | method | — | — | — |
