<!-- provenance: verbose, boundary-bleed -->
# AsyncSocket.Main

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`AsyncSocket` (`IO::Networking::AsyncSocket`) is the core abstraction for asynchronous TCP socket communication in the MaNGOS network layer. This specific translation unit implements the class’s construction, destruction, state inspection, and a utility method for discarding incoming data. The remaining I/O logic (`Read`, `Write`, `InitializeAndFixateMemoryLocation`, etc.) is implemented in other partials of the class (e.g., `AsyncSocket._posix` or `AsyncSocket._win32`).

Key responsibilities of this unit:
1.  **Lifecycle Management:** Initializing atomic state flags and cleaning up resources (socket handles, pending operations) upon destruction.
2.  **Move Semantics:** Safely transferring ownership of the socket and its internal state between objects, ensuring the source object is neutralized to prevent double-closure.
3.  **State Reporting:** Providing methods to query the socket’s closing status and remote peer identity.
4.  **Data Skipping:** Offering a safe, high-level way to discard incoming bytes without manual buffer management.

## Member-by-Member Behavior

### Construction and Destruction

**`AsyncSocket` (Move Constructor)**
Transfers ownership of an existing `AsyncSocket` to a new instance. It moves all context, descriptor, callback, and platform-specific members (IOCP tasks on Windows; buffer pointers/sizes on POSIX). Crucially, it asserts that the destination object is not already initialized (`IS_INITIALIZED` flag clear), preventing moves of sockets whose memory addresses are fixed in kernel event loops. It swaps the atomic state and marks the source object with `WAS_MOVED_NO_DTOR` to suppress its destructor.

**`~AsyncSocket` (Destructor)**
Handles socket teardown:
1.  Returns immediately if the object was moved-from (`WAS_MOVED_NO_DTOR`), avoiding double-closure.
2.  Logs the remote IP address for debugging.
3.  Calls `m_descriptor.CloseSocket()` to release the OS socket handle.
4.  Asserts that no asynchronous operations (`CONTEXT_PRESENT`, `WRITE_PRESENT`, `READ_PRESENT`) are pending. This catches bugs where a socket is destroyed while I/O is still in flight, which would cause use-after-free or crashes.

### State and Identity

**`IsClosing`**
Returns `true` if the `SHUTDOWN_PENDING` flag is set in `m_atomicState`. This indicates the socket is shutting down and should reject new I/O requests. It is used by POSIX-specific socket option setters (in `AsyncSocket._posix`) to avoid configuring a dying socket.

**`GetRemoteEndpoint`**
Delegates to `m_descriptor.GetRemoteEndpoint()` to retrieve the connected peer’s IP address and port. Used by higher-level sockets (e.g., `AuthSocket`) to identify clients.

**`GetRemoteIpString`**
Returns a string representation of the remote peer’s IP address (IPv4 or IPv6). Derived from `GetRemoteEndpoint()`, it is widely used for logging and debugging across the network stack.

### Data Transfer Utilities

**`ReadSkip`**
Discards a specified number of bytes from the incoming stream. It allocates a temporary `std::vector<uint8_t>`, performs an asynchronous `Read` into it, and invokes the user callback once complete. The lambda captures the buffer by value, ensuring it remains alive until the async operation finishes, thus preventing use-after-free.

## Cross-Unit Boundaries

*   **`SocketDescriptor`**:
    *   *Called by:* `AsyncSocket` (ctor, dtor, `GetRemoteEndpoint`).
    *   *Role:* Manages the raw OS socket handle. `AsyncSocket` delegates creation, closure, and endpoint retrieval to this unit.

*   **`ReadableBuffer`**:
    *   *Called by:* `AsyncSocket` (ctor).
    *   *Role:* Represents data ready to be sent. The `Write` method signature in the header declares this dependency, though its implementation resides in another partial.

*   **`Log.Main`**:
    *   *Called by:* `~AsyncSocket`.
    *   *Role:* Logs debug information when a socket is destroyed.

*   **`Errors`**:
    *   *Called by:* `~AsyncSocket` (via assertions).
    *   *Role:* Handles fatal errors if assertions fail (e.g., pending operations during destruction).

*   **Higher-Level Sockets (`AuthSocket`, `RASocket`, `WorldSocket`)**:
    *   *Call into:* `AsyncSocket` methods (`ReadSkip`, `GetRemoteIpString`, `GetRemoteEndpoint`).
    *   *Role:* These classes inherit from `AsyncSocket` to implement game protocols. They use this unit’s methods for logging, client identification, and data skipping.

*   **`AsyncSocket._posix` (Sibling Partial)**:
    *   *Calls into:* `AsyncSocket` methods (`IsClosing`, `GetRemoteIpString`).
    *   *Role:* The POSIX-specific partial handles `epoll`/`kqueue` integration. It queries `IsClosing` to stop processing events and uses `GetRemoteIpString` for logging.

## Data Model

This unit operates entirely in memory and interacts with the OS network stack. It does **not** access any database tables.

## Notable Implementation Details

1.  **Atomic State Machine**: Uses `std::atomic<int> m_atomicState` with bitmask flags to manage concurrency without mutexes. Flags like `READ_PRESENT` and `WAS_MOVED_NO_DTOR` ensure thread-safe cleanup and prevent double-closure.
2.  **Move Safety**: The move constructor asserts that the destination is not `IS_INITIALIZED`. This prevents moving sockets whose memory addresses are registered in kernel event loops (POSIX), which would break the event dispatch mechanism.
3.  **Buffer Lifetime in `ReadSkip`**: The `ReadSkip` method demonstrates the correct pattern for async reads: allocating a buffer and capturing it by value in the callback lambda to extend its lifetime until the operation completes.

## Member Reference

**AsyncSocket**  
Move constructor. Transfers ownership of socket state, descriptors, and callbacks. Asserts destination is not initialized. Marks source as moved-to-prevent-destruction.

**~AsyncSocket**  
Destructor. Skips if moved-from. Logs remote IP. Closes socket via `SocketDescriptor`. Asserts no pending async operations.

**IsClosing**  
Returns `true` if `SHUTDOWN_PENDING` flag is set. Used by POSIX partial to halt event processing.

**ReadSkip**  
Discards `skipSize` bytes. Allocates temp buffer, performs async `Read`, invokes callback. Captures buffer by value to ensure lifetime.

**GetRemoteEndpoint**  
Delegates to `m_descriptor` to return peer’s IP endpoint.

**GetRemoteIpString**  
Returns string representation of remote IP. Derived from `GetRemoteEndpoint()`. Used for logging.

---

<!-- machine-true, projected from graph.json -->

## Map — AsyncSocket.Main

*Source:* AsyncSocket.cpp, AsyncSocket.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AsyncSocket | ctor | ReadableBuffer/ReadableBuffer#2, SocketDescriptor/SocketDescriptor | AuthSocket/AuthSocket, RASocket/RASocket, WorldSocket/WorldSocket | — |
| ~AsyncSocket | dtor | Errors/PrintStacktraceAndThrow, Log.Main/Out, SocketDescriptor/CloseSocket | — | — |
| IsClosing | method | — | AsyncSocket._posix/SetNativeSocketOption_NoDelay, AsyncSocket._posix/SetNativeSocketOption_SystemOutgoingSendBuffer | — |
| ReadSkip | method | AsyncSocket._posix/Read | AuthSocket/_HandleRealmList | — |
| GetRemoteEndpoint | method | — | AuthSocket/LoadRealmlistAndWriteIntoBuffer | — |
| GetRemoteIpString | method | — | AsyncSocket._posix/CloseSocket, AsyncSocket._posix/OnIoEvent, AsyncSocket._posix/PerformNonBlockingRead, AsyncSocket._posix/PerformNonBlockingWrite, AsyncSocket._posix/Read, AsyncSocket._posix/ReadSome, AuthSocket/AuthSocket, RASocket/DoRecvIncomingData, RASocket/HandleInput_Authenticated, RASocket/HandleInput_GotUsername, RASocket/SendAndDisconnect, RASocket/SendAndRecvNextInput, RASocket/Start, RASocket/~RASocket, realmd_Main/main, WorldSocket/DoRecvIncomingData, WorldSocket/WorldSocket, WorldSocketMgr/OnNewClientConnected | — |

---

<!-- verify: boundary-bleed | foreign: AsyncSocket, Write -->
