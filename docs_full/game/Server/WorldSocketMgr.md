# WorldSocketMgr

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# WorldSocketMgr

**Purpose & Responsibilities**

`WorldSocketMgr` is the singleton responsible for managing the TCP listener for the World Server. It binds to a configured IP and port, accepts incoming connections, and initializes them into `WorldSocket` objects. It handles low-level socket configuration (TCP_NODELAY, send buffers) and supports PROXY protocol v2 for retrieving real client IPs behind trusted reverse proxies. It does not handle game logic or packet parsing.

**Member-by-Member Behavior**

### Lifecycle Management

**`StartWorldNetworking`** initializes the listener. It stores the `IO::IoContext` and `WorldSocketMgrOptions`. It calls `AsyncSocketAcceptor_posix/CreateAndBindServer` to bind the socket; if this fails, it logs an error via `Log.Main/Out` and returns `false`. On success, it registers `OnNewClientConnected` as the accept callback via `AsyncSocketAcceptor_posix/AutoAcceptSocketsUntilClose`.

**`StopWorldNetworking`** shuts down the listener. It logs a minimal message via `Log.Main/Out` and calls `AsyncSocketAcceptor_posix/ClosePortAndStopAcceptingNewConnections` on the stored listener, resetting the pointer to `nullptr`.

**`WorldSocketMgr`** is the default constructor. As a singleton, it relies on `MaNGOS::Singleton` for instantiation and performs no custom initialization.

### Connection Handling

**`OnNewClientConnected`** is invoked for each accepted connection. It:
1.  Selects an I/O context via `GetLeastUsedIoContext` (currently always the main context).
2.  Wraps the `SocketDescriptor` in a `WorldSocket` containing an `AsyncSocket._posix/AsyncSocket`.
3.  Calls `AsyncSocket._posix/InitializeAndFixateMemoryLocation`. Failure logs an error via `Log.Main/Out` and `NetworkError/ToString`, returning early (implicitly closing the socket).
4.  Optionally sets `SystemOutgoingSendBuffer` via `AsyncSocket._posix/SetNativeSocketOption_SystemOutgoingSendBuffer` and `TCP_NODELAY` via `AsyncSocket._posix/SetNativeSocketOption_NoDelay`. Failures are logged as non-fatal warnings via `Log.Main/Out`.
5.  Checks if the remote IP (from `WorldSocket/GetRemoteIpString` -> `AsyncSocket.Main/GetRemoteIpString`) is in `trustedProxyIps`.
    *   **If Trusted:** Calls `ProxyV2Reader/ReadProxyV2Handshake`. On success, it stores the real IP (via `IpAddress/ToString`) in `WorldSocket::m_remoteIpAddressStringAfterProxy`, logs acceptance, and calls `WorldSocket/Start`. On failure, it logs the error and returns (closing the socket).
    *   **If Not Trusted:** Logs acceptance and immediately calls `WorldSocket/Start`.

### I/O Context Management

**`GetLeastUsedIoContext`** returns the main `m_ioContext`. Despite the name and a `TODO` comment referencing TrinityCore’s thread affinity, it does not perform load balancing.

**Cross-Unit Boundaries**

*   **`Master/Run`**: Calls `StartWorldNetworking` and `StopWorldNetworking` to control the network layer.
*   **`AsyncSocketAcceptor_posix`**: `CreateAndBindServer` (binds socket), `AutoAcceptSocketsUntilClose` (registers callback), `ClosePortAndStopAcceptingNewConnections` (shuts down).
*   **`Log.Main`**: `Out` is used for all logging (errors, warnings, info).
*   **`SocketDescriptor`**: Constructor called when moving descriptors into `AsyncSocket`.
*   **`WorldSocket`**: `GetRemoteIpString` (identifies peer), `Start` (begins protocol processing).
*   **`AsyncSocket.Main` / `AsyncSocket._posix`**: `GetRemoteIpString`, `AsyncSocket` (constructor), `InitializeAndFixateMemoryLocation` (critical init), `SetNativeSocketOption_NoDelay`/`SetNativeSocketOption_SystemOutgoingSendBuffer` (config).
*   **`ProxyV2Reader`**: `ReadProxyV2Handshake` parses PROXY v2 headers for trusted proxies.
*   **`IpAddress`**: `ToString` formats the IP from the proxy header.
*   **`NetworkError`**: `ToString` converts errors to strings for logging.

**Data Model**

This unit does not interact with any database tables. Configuration is held in memory in `WorldSocketMgrOptions`.

**Notable Implementation Details**

1.  **Non-Fatal Socket Options:** Failures to set `TCP_NODELAY` or send buffer size in `OnNewClientConnected` are logged but do not abort the connection, treating them as warnings.
2.  **Proxy Protocol:** `WorldSocket::Start` is deferred until `ProxyV2Reader/ReadProxyV2Handshake` completes for trusted proxies. Incorrect `trustedProxyIps` configuration leads to wrong IP resolution.
3.  **Single I/O Context:** `GetLeastUsedIoContext` always returns the main context, ignoring potential load balancing benefits mentioned in the `TODO`.
4.  **Implicit Closure:** Early returns in `OnNewClientConnected` (on init or proxy parse failure) rely on object destruction to close sockets, as noted by "implicit close()" comments.

## Member Reference

**StartWorldNetworking**: Initializes the listener by binding via `AsyncSocketAcceptor_posix/CreateAndBindServer`. Registers `OnNewClientConnected` via `AsyncSocketAcceptor_posix/AutoAcceptSocketsUntilClose`. Logs errors via `Log.Main/Out` if binding fails. Returns `true` on success, `false` on failure.

**WorldSocketMgr**: Default constructor for the singleton manager. Performs no initialization.

**StopWorldNetworking**: Shuts down the listener by calling `AsyncSocketAcceptor_posix/ClosePortAndStopAcceptingNewConnections` and resetting the pointer. Logs a minimal message via `Log.Main/Out`.

**OnNewClientConnected**: Handles new TCP connections. Wraps the descriptor in a `WorldSocket` with `AsyncSocket._posix/AsyncSocket`. Initializes memory via `AsyncSocket._posix/InitializeAndFixateMemoryLocation` (fatal if failed). Configures TCP options (`SetNativeSocketOption_NoDelay`, `SetNativeSocketOption_SystemOutgoingSendBuffer`) with non-fatal error handling. If the remote IP is in `trustedProxyIps`, parses PROXY v2 headers via `ProxyV2Reader/ReadProxyV2Handshake` to resolve the real client IP; otherwise, proceeds directly. Finally, calls `WorldSocket/Start`. Uses `Log.Main/Out` and `NetworkError/ToString` for diagnostics.

**GetLeastUsedIoContext**: Returns the main `m_ioContext`. Does not currently implement load balancing or thread affinity selection, despite the method name and `TODO` comment suggesting future intent.

---

<!-- machine-true, projected from graph.json -->

## Map — WorldSocketMgr

*Source:* WorldSocketMgr.cpp, WorldSocketMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| StartWorldNetworking | method | AsyncSocketAcceptor_posix/AutoAcceptSocketsUntilClose, AsyncSocketAcceptor_posix/CreateAndBindServer, Log.Main/Out, SocketDescriptor/SocketDescriptor | Master/Run | — |
| WorldSocketMgr | ctor | — | — | — |
| StopWorldNetworking | method | AsyncSocketAcceptor_posix/ClosePortAndStopAcceptingNewConnections, Log.Main/Out | Master/Run | — |
| OnNewClientConnected | method | AsyncSocket.Main/GetRemoteIpString, AsyncSocket._posix/AsyncSocket, AsyncSocket._posix/InitializeAndFixateMemoryLocation, AsyncSocket._posix/SetNativeSocketOption_NoDelay, AsyncSocket._posix/SetNativeSocketOption_SystemOutgoingSendBuffer, IpAddress/ToString, Log.Main/Out, NetworkError/ToString, ProxyV2Reader/ReadProxyV2Handshake, SocketDescriptor/SocketDescriptor, WorldSocket/GetRemoteIpString, WorldSocket/Start | — | — |
| GetLeastUsedIoContext | method | — | — | — |
