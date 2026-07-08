<!-- provenance: verbose -->
# SocketDescriptor

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# SocketDescriptor

## Purpose & Responsibilities

`SocketDescriptor` is a lightweight, RAII-style wrapper around a native operating system socket handle (`IO::Native::SocketHandle`). It enforces strict lifecycle management for network connections within the `IO::Networking` subsystem, ensuring that a socket is explicitly closed before the descriptor object is destroyed. This prevents resource leaks and undefined behavior associated with dangling file descriptors.

The class is non-copyable but movable, inheriting from `MaNGOS::Policies::NoCopyButAllowMove`. This reflects the semantic reality that a socket is a unique resource owned by exactly one entity; moving ownership transfers the responsibility for closing the socket to the new owner. The descriptor cannot be detached from the socket (no `release()` method); it *is* the socket's lifecycle manager.

Key constraints:
1.  **Explicit Closure:** The destructor asserts that `m_isClosed` is `true`. Callers must invoke `CloseSocket()` explicitly, making the shutdown sequence deterministic.
2.  **Immutable Identity:** The remote endpoint (`IpEndpoint`) and native handle are stored as `const` members, preventing changes after construction.

## Member-by-Member Behavior

### Construction and Destruction

**`SocketDescriptor` (Constructors)**
Two constructors initialize the descriptor with a native socket handle and the IP endpoint of the remote peer, setting `m_isClosed` to `false`.
*   The primary constructor takes `nativeSocket` and `remoteEndpoint` directly. It is called by `AsyncSocket::Main::AsyncSocket`, `AsyncSocket::_posix::AsyncSocket`, `AsyncSocketAcceptor_posix::OnNewClientToAcceptAvailable`, `Master::SetupRemoteAccessServer`, `realmd_Main::main`, `WorldSocketMgr::OnNewClientConnected`, and `WorldSocketMgr::StartWorldNetworking`.
*   A second constructor variant (labeled `SocketDescriptor#2` in the MAP) is called by `AsyncSocketAcceptor_posix::OnNewClientToAcceptAvailable` and `SocketConnector::ConnectBlocking`.

**Move Constructor**
Defined in the header, this constructor transfers ownership of the native socket and remote endpoint from `other` to the new instance. Crucially, it sets `other.m_isClosed = true` to prevent the moved-from object from asserting in its destructor, as the responsibility for the socket has been transferred.

**`~SocketDescriptor` (Destructor)**
Performs a sanity check using `MANGOS_ASSERT(m_isClosed)`. If the socket was not explicitly closed prior to destruction, the assertion fails, triggering a stack trace via `Errors::PrintStacktraceAndThrow`. The destructor itself does *not* close the socket; it only verifies that it has already been closed.

### State Management

**`CloseSocket`**
The primary action method for lifecycle management. It checks `m_isClosed`; if already closed, it returns immediately (idempotent). Otherwise, it sets `m_isClosed` to `true` and delegates the actual OS-level socket closure to `IO::Networking::Internal::CloseSocket`. This separation allows platform-specific implementations to handle low-level syscalls without exposing them to higher-level logic. It is called by `AsyncSocket::Main::~AsyncSocket` and `AsyncSocketAcceptor_posix::OnNewClientToAcceptAvailable`.

### Accessors

**`IsClosed`**
Returns the current state of `m_isClosed`. While listed in the MAP, it is not currently called by any other unit in the provided cross-reference data.

**`GetNativeSocket`**
Returns a constant reference to the underlying `IO::Native::SocketHandle`. This is heavily used by the POSIX-specific asynchronous socket implementation (`AsyncSocket._posix`) for all I/O operations, including `Read`, `Write`, `SetNativeSocketOption_NoDelay`, `SetNativeSocketOption_SystemOutgoingSendBuffer`, `OnIoEvent`, `PerformNonBlockingRead`, `PerformNonBlockingWrite`, `InitializeAndFixateMemoryLocation`, and `CloseSocket`. By exposing the raw handle, `SocketDescriptor` allows lower-level I/O multiplexing code to interact directly with the OS while maintaining high-level ownership semantics.

**`GetRemoteEndpoint`**
Returns a constant reference to the `IpEndpoint` of the remote peer. This allows higher-level logic to identify the connected peer without querying the OS. Like `IsClosed`, it is not currently shown as being called by other units in the MAP.

## Cross-Unit Boundaries

`SocketDescriptor` acts as a bridge between high-level connection management and low-level OS I/O.

*   **Dependency on `IO::Networking::Internal`:**
    *   **Direction:** Outbound call from `CloseSocket`.
    *   **Purpose:** Delegates the actual OS-specific socket closure. This abstraction allows `SocketDescriptor` to remain platform-agnostic regarding the specific syscall used to close a socket.

*   **Integration with `AsyncSocket` Family:**
    *   **Direction:** Inbound calls from `AsyncSocket` constructors and destructors; Outbound calls from `AsyncSocket._posix` methods.
    *   **Purpose:** `AsyncSocket` objects own a `SocketDescriptor`. They construct it upon successful connection/acceptance and destroy it during cleanup. The POSIX-specific methods in `AsyncSocket._posix` retrieve the native handle via `GetNativeSocket` to perform syscalls like `read()`, `write()`, or `setsockopt()`.

*   **Integration with Connection Managers:**
    *   **Direction:** Inbound calls from `AsyncSocketAcceptor_posix`, `WorldSocketMgr`, and `Master`.
    *   **Purpose:** These managers create `SocketDescriptor` instances when new connections arrive. For example, `AsyncSocketAcceptor_posix::OnNewClientToAcceptAvailable` creates a descriptor for a newly accepted client socket. `WorldSocketMgr::OnNewClientConnected` wraps the accepted socket in a descriptor for further processing by the world server.

## Data Model

This unit does not interact with any database tables. It operates entirely in memory, managing OS-level resources.

## Notable Implementation Details

1.  **Assertion-Driven Lifecycle:** The destructor relies on `MANGOS_ASSERT(m_isClosed)`. This defensive technique shifts the burden of correctness to the caller. If a developer forgets to call `CloseSocket()`, the program aborts in debug builds, highlighting the bug immediately rather than leaking resources silently.
2.  **Move Semantics Safety:** The move constructor sets `other.m_isClosed = true`. This is essential because the moved-from object will still be destructed. Without this step, the destructor of the moved-from object would assert failure, even though the socket was successfully transferred.
3.  **Idempotent Close:** `CloseSocket` checks `m_isClosed` before proceeding. This makes it safe to call multiple times, which is useful in error handling paths where a socket might be closed in both a cleanup routine and an exception handler.
4.  **Const Correctness:** Both `m_nativeSocket` and `m_remoteEndpoint` are declared `const`. This enforces that once a `SocketDescriptor` is created, the underlying socket handle and the remote peer's address cannot be changed, preventing accidental reassignment or corruption.

## Member Reference

**SocketDescriptor#2** (Constructor)
Initializes `m_nativeSocket`, `m_remoteEndpoint`, and sets `m_isClosed` to `false`. Called by `AsyncSocketAcceptor_posix::OnNewClientToAcceptAvailable` and `SocketConnector::ConnectBlocking`.

**~SocketDescriptor** (Destructor)
Asserts that `m_isClosed` is `true`. If not, triggers `Errors::PrintStacktraceAndThrow`. Does not close the socket itself.

**CloseSocket**
Sets `m_isClosed` to `true` and calls `IO::Networking::Internal::CloseSocket` if not already closed. Idempotent. Called by `AsyncSocket::Main::~AsyncSocket` and `AsyncSocketAcceptor_posix::OnNewClientToAcceptAvailable`.

**SocketDescriptor** (Constructor)
Primary constructor taking `nativeSocket` and `remoteEndpoint`. Initializes members and sets `m_isClosed` to `false`. Called by `AsyncSocket::Main::AsyncSocket`, `AsyncSocket::_posix::AsyncSocket`, `AsyncSocketAcceptor_posix::OnNewClientToAcceptAvailable`, `Master::SetupRemoteAccessServer`, `realmd_Main::main`, `WorldSocketMgr::OnNewClientConnected`, and `WorldSocketMgr::StartWorldNetworking`.

**IsClosed**
Returns the value of `m_isClosed`. Currently not called by any other unit in the provided MAP.

**GetNativeSocket**
Returns a const reference to `m_nativeSocket`. Heavily used by `AsyncSocket::_posix` methods for I/O operations (`Read`, `Write`, `OnIoEvent`, etc.).

**GetRemoteEndpoint**
Returns a const reference to `m_remoteEndpoint`. Currently not called by any other unit in the provided MAP.

---

<!-- machine-true, projected from graph.json -->

## Map — SocketDescriptor

*Source:* SocketDescriptor.cpp, SocketDescriptor.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| SocketDescriptor#2 | ctor | — | AsyncSocketAcceptor_posix/OnNewClientToAcceptAvailable, SocketConnector/ConnectBlocking | — |
| ~SocketDescriptor | dtor | Errors/PrintStacktraceAndThrow | — | — |
| CloseSocket | method | Internal/CloseSocket | AsyncSocket.Main/~AsyncSocket, AsyncSocketAcceptor_posix/OnNewClientToAcceptAvailable | — |
| SocketDescriptor | ctor | — | AsyncSocket.Main/AsyncSocket, AsyncSocket._posix/AsyncSocket, AsyncSocketAcceptor_posix/OnNewClientToAcceptAvailable, Master/SetupRemoteAccessServer, realmd_Main/main, WorldSocketMgr/OnNewClientConnected, WorldSocketMgr/StartWorldNetworking | — |
| IsClosed | method | — | — | — |
| GetNativeSocket | method | — | AsyncSocket._posix/CloseSocket, AsyncSocket._posix/InitializeAndFixateMemoryLocation, AsyncSocket._posix/OnIoEvent, AsyncSocket._posix/PerformNonBlockingRead, AsyncSocket._posix/PerformNonBlockingWrite, AsyncSocket._posix/Read, AsyncSocket._posix/ReadSome, AsyncSocket._posix/SetNativeSocketOption_NoDelay, AsyncSocket._posix/SetNativeSocketOption_SystemOutgoingSendBuffer, AsyncSocket._posix/Write | — |
| GetRemoteEndpoint | method | — | — | — |
