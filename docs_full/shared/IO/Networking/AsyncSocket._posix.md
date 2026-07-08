<!-- provenance: boundary-bleed -->
# AsyncSocket._posix

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`AsyncSocket` (specifically the POSIX implementation in `AsyncSocket_posix.cpp`) is the core abstraction for non-blocking, asynchronous TCP socket communication within the MaNGOS network layer. It wraps a native file descriptor (`SocketDescriptor`) and integrates with the operating system’s event notification mechanism—`epoll` on Linux or `kqueue` on BSD/macOS—to drive I/O operations without blocking threads.

Key responsibilities include:
1.  **Event Registration:** Registering the socket with the kernel’s I/O multiplexer (`epoll_ctl` or `kevent`) using edge-triggered semantics. This requires the object’s memory address to remain stable ("fixated") after initialization.
2.  **Asynchronous I/O Coordination:** Managing concurrent read and write requests via atomic state flags. It ensures that only one read and one write operation can be pending at any time, preventing race conditions in the event loop.
3.  **Buffer Management:** Handling partial reads/writes. If a `recv()` or `send()` call returns fewer bytes than requested (or `EWOULDBLOCK`), the socket retains the buffer pointers and byte counts, resuming the transfer when the next I/O event arrives.
4.  **Thread Safety & Context Switching:** Providing a mechanism (`EnterIoContext`) to schedule callbacks on the I/O thread, ensuring that computationally expensive tasks or state mutations happen in the correct thread context.
5.  **Graceful Shutdown:** Coordinating the closure of sockets by signaling shutdown to the OS, waiting for pending operations to complete or fail, and invoking cleanup callbacks.

This unit does not handle protocol parsing, encryption, or connection acceptance; those are handled by higher-level classes like `AuthSocket`, `WorldSocket`, and `WorldSocketMgr`. `AsyncSocket` strictly manages the raw byte stream transport.

## Member-by-Member Behavior

### Initialization and Lifecycle

**`InitializeAndFixateMemoryLocation`**
This is a critical setup step. It registers the socket with the OS event loop:
-   **Linux:** Uses `epoll_ctl` to add the socket with events `EPOLLIN`, `EPOLLOUT`, `EPOLLERR`, `EPOLLRDHUP`, and `EPOLLET` (Edge Triggered). The `data.ptr` field of the `epoll_event` is set to `this`, allowing the event loop to identify the socket directly from the event structure.
-   **BSD/macOS:** Uses `kevent` to register `EVFILT_READ` and `EVFILT_WRITE` filters with `EV_CLEAR` (edge-triggered equivalent). The `udata` field is set to `this`.
-   Sets the `IS_INITIALIZED` atomic flag. If registration fails, it returns a `NetworkError`. Once initialized, the object cannot be moved because its address is embedded in kernel structures.

**`CloseSocket`**
Initiates a graceful shutdown. It sets the `SHUTDOWN_PENDING` flag and calls `::shutdown(fd, SHUT_RDWR)` to disable further sends and receives on the socket. It does *not* close the file descriptor immediately to avoid race conditions if the descriptor ID is reused.

### Asynchronous I/O Operations

**`Read`**
Initiates a request to read exactly `size` bytes into `target`.
-   Checks atomic state to ensure no other read is pending.
-   Attempts an immediate `::recv()`. If data is available, it fills as much of the buffer as possible.
-   If the full `size` is not received, it stores the remaining buffer pointer, bytes left, and the callback in member variables, setting the `READ_PRESENT` flag.
-   If `::recv()` returns 0, it treats this as a half-close, triggers `StopPendingTransactionsAndForceClose`, and invokes the callback with `SocketClosed`.
-   If `::recv()` returns `EWOULDBLOCK`, it queues the operation for the next `EPOLLIN`/`EVFILT_READ` event.

**`ReadSome`**
Similar to `Read`, but designed to retrieve *any* available data up to `maxSize`.
-   Key difference: It sets `m_readDstBufferSize` to 0. In `PerformNonBlockingRead`, this flag indicates that the operation should complete after a single successful `::recv()` call, regardless of whether the buffer is full.
-   Used by `RASocket` for receiving variable-length remote access commands.

**`Write`**
Initiates a request to send all bytes from `source` (`ReadableBuffer`).
-   Checks atomic state to ensure no other write is pending.
-   Attempts an immediate `::send()`.
-   If not all bytes are sent, it stores the source buffer, the number of bytes already transferred, and the callback, setting `WRITE_PRESENT`.
-   If `::send()` returns `EWOULDBLOCK`, it waits for the next `EPOLLOUT`/`EVFILT_WRITE` event.
-   Note: The caller must ensure `source` remains valid until the callback is invoked.

### Event Handling and Execution

**`OnIoEvent`**
The entry point called by the `IoContext` when an event occurs on this socket.
-   **Linux:**
    -   `EPOLLERR`: Retrieves the socket error via `getsockopt(SO_ERROR)` and forces close.
    -   `EPOLLRDHUP`: Detects peer hang-up (TCP FIN received) and forces close.
    -   `EPOLLIN`: Calls `PerformNonBlockingRead`.
    -   `EPOLLOUT`: Calls `PerformNonBlockingWrite`.
-   **BSD/macOS:**
    -   `EVFILT_EXCEPT`: Handles exceptions/errors similarly to `EPOLLERR`.
    -   `EVFILT_READ`: Calls `PerformNonBlockingRead`.
    -   `EVFILT_WRITE`: Calls `PerformNonBlockingWrite`.
-   `CALLBACK_EVENT_FLAG` (User-defined filter): Calls `PerformContextSwitch`.

**`PerformNonBlockingRead`**
Executes the pending read operation.
-   Acquires the `READ_PENDING_LOAD` lock.
-   Handles a rare race condition where an event arrives before the `READ_PRESENT` flag is set by yielding until the flag is set.
-   Calls `::recv()` to fill the remaining portion of the buffer.
-   If the buffer is full (or `ReadSome` mode), it clears the state, moves the callback, and invokes it with the total bytes read.
-   If `::recv()` returns 0, it forces close.

**`PerformNonBlockingWrite`**
Executes the pending write operation.
-   Acquires the `WRITE_PENDING_LOAD` lock.
-   Handles the same race condition as read.
-   Calls `::send()` starting from `m_writeSrcAlreadyTransferred`.
-   Updates `m_writeSrcAlreadyTransferred`.
-   If all bytes are sent, it clears the state and invokes the callback.

**`PerformContextSwitch`**
Executes a pending context-switch callback.
-   Acquires the `CONTEXT_PENDING_LOAD` lock.
-   Checks if the socket is shutting down; if so, invokes the callback with `SocketClosed`.
-   Otherwise, invokes the callback with `NoError`.
-   This allows user code to run in the I/O thread context, synchronized with the event loop.

**`EnterIoContext`**
Schedules a callback to run on the I/O thread.
-   Stores the callback and sets `CONTEXT_PRESENT`.
-   Posts a user-defined event to the `IoContext` (`PostForImmediateInvocation`), which triggers `OnIoEvent` with the `CALLBACK_EVENT_FLAG`, leading to `PerformContextSwitch`.

### Cleanup and Error Handling

**`StopPendingTransactionsAndForceClose`**
Forces the socket to close, aborting any pending operations.
-   Calls `CloseSocket()` to set `SHUTDOWN_PENDING`.
-   Sets `IGNORE_TRANSFERS` to prevent new events from being processed.
-   Spins (yielding) until any ongoing `READ_PENDING_LOAD` or `WRITE_PENDING_LOAD` operations complete.
-   Invokes pending read/write callbacks with `SocketClosed` error.
-   Does *not* clear `CONTEXT_PRESENT` because the context switch queue holds a raw pointer; clearing it here could cause issues if the context switch is already in flight.

**`SetNativeSocketOption_NoDelay`**
Sets the `TCP_NODELAY` socket option to disable Nagle's algorithm. Returns `NetworkError` if the socket is closing or the syscall fails.

**`SetNativeSocketOption_SystemOutgoingSendBuffer`**
Sets the `SO_SNDBUF` socket option to hint the OS about the desired send buffer size. Returns `NetworkError` if the socket is closing or the syscall fails.

## Cross-Unit Boundaries

### Dependencies (Calls Out)

*   **`SocketDescriptor`**: Used extensively to retrieve the native file descriptor (`GetNativeSocket`) and remote endpoint information (`GetRemoteEndpoint`).
*   **`IoContext`**:
    *   `GetUnixEpollDescriptor` / `GetKqueueDescriptor`: To register the socket with the kernel event loop.
    *   `PostForImmediateInvocation`: To schedule context switches.
*   **`NetworkError`**: Constructs error objects returned to callbacks or callers.
*   **`Log.Main`**: Logs errors, debug messages, and warnings (e.g., "socket half-closed", "epoll error").
*   **`SystemErrorToString`**: Converts `errno` to human-readable strings for logging.
*   **`ReadableBuffer`**: Used in `Write` and `PerformNonBlockingWrite` to access buffer pointers and sizes.
*   **`Errors.PrintStacktraceAndThrow`**: Called in `InitializeAndFixateMemoryLocation` and `SetNativeSocketOption_SystemOutgoingSendBuffer` on assertion failures or critical errors.

### Callers (Called By)

*   **`WorldSocketMgr`**:
    *   `OnNewClientConnected`: Creates new `AsyncSocket` instances, initializes them, and sets socket options (`NoDelay`, `SendBuffer`).
*   **`AuthSocket`**:
    *   `DoRecvIncomingData`, `_HandleLogonChallenge`, etc.: Calls `Read` to receive authentication packets.
    *   `_HandleLogonProof__PostRecv`, `_HandleRealmList`, etc.: Calls `Write` to send authentication responses.
    *   `CloseSocket`: Calls `AsyncSocket::CloseSocket`.
*   **`WorldSocket`**:
    *   `DoRecvIncomingData`: Calls `Read` to receive game world packets.
    *   `HandleResultOfAsyncWrite`: Calls `Write` for subsequent writes.
    *   `SendPacket`: Calls `EnterIoContext` to ensure sending happens in the I/O thread.
    *   `CloseSocket`: Calls `AsyncSocket::CloseSocket`.
*   **`RASocket`**:
    *   `Start`: Initializes the socket.
    *   `DoRecvIncomingData`: Calls `ReadSome` for remote access input.
    *   `SendAndDisconnect`, `SendAndRecvNextInput`: Calls `Write`.
*   **`realmd_Main/main`**: Directly creates and initializes sockets for the realm daemon.
*   **`ProxyV2Reader`**: Calls `Read` for proxy handshake data.

## Data Model

This unit operates entirely on in-memory buffers and kernel socket buffers. It does not interact with any database tables.

## Notable Implementation Details

### Edge-Triggered Semantics and Memory Fixation
The use of edge-triggered I/O (`EPOLLET` / `EV_CLEAR`) means the kernel only notifies the application when the socket state *changes* (e.g., becomes readable). The application must drain the buffer completely. To support this efficiently, `AsyncSocket` registers its own memory address (`this`) in the `epoll_event` or `kevent` structure. This eliminates the need for a lookup table or trampoline function in the event loop, improving performance. However, it mandates that the `AsyncSocket` object must not be moved in memory after `InitializeAndFixateMemoryLocation` is called. The `IS_INITIALIZED` flag enforces this constraint.

### Atomic State Machine
Concurrency is managed via a single `std::atomic<int>` (`m_atomicState`) containing bitflags. This avoids mutexes in the hot path.
-   **Pending vs. Present:** Flags like `READ_PENDING_SET` indicate an operation is being initiated, while `READ_PRESENT` indicates an operation is active/waiting for I/O. `READ_PENDING_LOAD` indicates the event handler is currently processing the operation.
-   **Race Condition Handling:** In `PerformNonBlockingRead` and `PerformNonBlockingWrite`, there is a spin-loop (`while ... yield`) to handle the race where an I/O event arrives before the `PRESENT` flag is set by the initiating thread. This ensures no events are missed.

### Partial I/O Handling
Both `Read` and `Write` attempt immediate I/O. If the OS returns `EWOULDBLOCK`, the operation is queued. The member variables `m_readDstBuffer`, `m_readDstBufferBytesLeft`, `m_writeSrc`, and `m_writeSrcAlreadyTransferred` track progress. Subsequent events resume from where they left off. This is crucial for handling slow clients or network congestion without dropping data.

### Context Switching
`EnterIoContext` provides a way to execute arbitrary code on the I/O thread. This is used by `WorldSocket::SendPacket` to ensure that packet serialization and sending happen in the same thread that handles I/O events, avoiding cross-thread locking on the socket state. It works by posting a synthetic event to the `IoContext`, which triggers `OnIoEvent` -> `PerformContextSwitch`.

### Shutdown Sequence
`StopPendingTransactionsAndForceClose` is complex because it must safely abort pending operations. It sets `IGNORE_TRANSFERS` to stop new events, then spins until any ongoing `LOAD` operations finish. It then manually invokes the pending callbacks with `SocketClosed` errors. It deliberately does *not* clear `CONTEXT_PRESENT` to avoid invalidating pointers held by the context switch queue.

## Member Reference

**AsyncSocket**
Constructor implemented in a sibling partial of this class; initializes the `IoContext` pointer and takes ownership of the `SocketDescriptor`. No kernel registration occurs here.

**InitializeAndFixateMemoryLocation**
Registers the socket with the OS event loop (`epoll` or `kqueue`) using edge-triggered semantics. Sets the `IS_INITIALIZED` flag. Returns `NetworkError` on failure. Must be called before any I/O operations.

**Read**
Initiates an asynchronous read of exactly `size` bytes into `target`. Handles immediate data availability, `EWOULDBLOCK` queuing, and half-close detection. Stores state for partial reads.

**ReadSome**
Initiates an asynchronous read of up to `maxSize` bytes. Completes after a single successful `recv()` call, regardless of buffer fullness. Used for variable-length inputs.

**Write**
Initiates an asynchronous write of all bytes from `source`. Handles immediate data transmission, `EWOULDBLOCK` queuing, and partial sends. Caller must keep `source` valid until callback.

**CloseSocket**
Signals shutdown to the OS (`SHUT_RDWR`) and sets `SHUTDOWN_PENDING`. Does not close the file descriptor immediately.

**PerformNonBlockingRead**
Internal handler for `EPOLLIN`/`EVFILT_READ` events. Resumes pending reads, handles partial fills, and invokes callbacks upon completion or error. Includes spin-wait for race conditions.

**PerformNonBlockingWrite**
Internal handler for `EPOLLOUT`/`EVFILT_WRITE` events. Resumes pending writes, updates byte counters, and invokes callbacks upon completion. Includes spin-wait for race conditions.

**PerformContextSwitch**
Internal handler for user-posted context switch events. Executes the stored callback on the I/O thread, checking for shutdown status first.

**StopPendingTransactionsAndForceClose**
Forces socket closure by aborting pending I/O operations. Waits for ongoing operations to finish, then invokes their callbacks with `SocketClosed` errors. Sets `IGNORE_TRANSFERS`.

**EnterIoContext**
Schedules a callback to run on the I/O thread by posting a synthetic event to the `IoContext`. Used for thread-safe execution of I/O-related logic.

**OnIoEvent**
Entry point for the event loop. Dispatches events to `PerformNonBlockingRead`, `PerformNonBlockingWrite`, `PerformContextSwitch`, or error handlers based on event type.

**SetNativeSocketOption_NoDelay**
Sets the `TCP_NODELAY` socket option to disable Nagle's algorithm. Returns `NetworkError` if the socket is closing or the syscall fails.

**SetNativeSocketOption_SystemOutgoingSendBuffer**
Sets the `SO_SNDBUF` socket option to hint the OS about the desired send buffer size. Returns `NetworkError` if the socket is closing or the syscall fails.

---

<!-- machine-true, projected from graph.json -->

## Map — AsyncSocket._posix

*Source:* AsyncSocket_posix.cpp, AsyncSocket.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AsyncSocket | ctor | SocketDescriptor/SocketDescriptor | Master/SetupRemoteAccessServer, realmd_Main/main, WorldSocketMgr/OnNewClientConnected | — |
| InitializeAndFixateMemoryLocation | method | Errors/PrintStacktraceAndThrow, IoContext/GetUnixEpollDescriptor, Log.Main/Out, NetworkError/NetworkError, NetworkError/NetworkError#2, SocketDescriptor/GetNativeSocket, SystemErrorToString/SystemErrorToString | RASocket/Start, realmd_Main/main, WorldSocketMgr/OnNewClientConnected | — |
| Read | method | AsyncSocket.Main/GetRemoteIpString, Log.Main/Out, NetworkError/NetworkError, NetworkError/NetworkError#2, SocketDescriptor/GetNativeSocket | AsyncSocket.Main/ReadSkip, AuthSocket/DoRecvIncomingData, AuthSocket/_HandleLogonChallenge, AuthSocket/_HandleLogonProof, AuthSocket/_HandleReconnectChallenge, AuthSocket/_HandleReconnectProof, AuthSocket/_HandleXferResume, ProxyV2Reader/ReadProxyV2Handshake, WorldSocket/DoRecvIncomingData | — |
| ReadSome | method | AsyncSocket.Main/GetRemoteIpString, Log.Main/Out, NetworkError/NetworkError, NetworkError/NetworkError#2, SocketDescriptor/GetNativeSocket | RASocket/DoRecvIncomingData | — |
| Write | method | Log.Main/Out, NetworkError/NetworkError, NetworkError/NetworkError#2, ReadableBuffer/GetPtr, ReadableBuffer/GetSize, ReadableBuffer/operator=#2, SocketDescriptor/GetNativeSocket | AuthSocket/RepeatInternalXferLoop, AuthSocket/_HandleLogonChallenge, AuthSocket/_HandleLogonProof__PostRecv, AuthSocket/_HandleLogonProof__PostRecv_HandleInvalidVersion, AuthSocket/_HandleRealmList, AuthSocket/_HandleReconnectChallenge, AuthSocket/_HandleReconnectProof, RASocket/DoRecvIncomingData, RASocket/SendAndDisconnect, RASocket/SendAndRecvNextInput, WorldSocket/HandleResultOfAsyncWrite | — |
| CloseSocket | method | AsyncSocket.Main/GetRemoteIpString, Log.Main/Out, SocketDescriptor/GetNativeSocket | AuthSocket/CloseSocket, WorldSocket/CloseSocket | — |
| PerformNonBlockingRead | method | AsyncSocket.Main/GetRemoteIpString, Log.Main/Out, NetworkError/NetworkError, SocketDescriptor/GetNativeSocket, SystemErrorToString/SystemErrorToString | — | — |
| PerformNonBlockingWrite | method | AsyncSocket.Main/GetRemoteIpString, Log.Main/Out, NetworkError/NetworkError, ReadableBuffer/GetPtr, ReadableBuffer/GetSize, ReadableBuffer/operator=#3, SocketDescriptor/GetNativeSocket, SystemErrorToString/SystemErrorToString | — | — |
| PerformContextSwitch | method | Errors/PrintStacktraceAndThrow, NetworkError/NetworkError | — | — |
| StopPendingTransactionsAndForceClose | method | NetworkError/NetworkError, ReadableBuffer/operator=#3 | — | — |
| EnterIoContext | method | IoContext_linux/PostForImmediateInvocation, NetworkError/NetworkError | WorldSocket/SendPacket | — |
| OnIoEvent | method | AsyncSocket.Main/GetRemoteIpString, Log.Main/Out, SocketDescriptor/GetNativeSocket, SystemErrorToString/SystemErrorToString | — | — |
| SetNativeSocketOption_NoDelay | method | AsyncSocket.Main/IsClosing, NetworkError/FromSystemError, NetworkError/NetworkError, SocketDescriptor/GetNativeSocket | WorldSocketMgr/OnNewClientConnected | — |
| SetNativeSocketOption_SystemOutgoingSendBuffer | method | AsyncSocket.Main/IsClosing, Errors/PrintStacktraceAndThrow, NetworkError/FromSystemError, NetworkError/NetworkError, SocketDescriptor/GetNativeSocket | WorldSocketMgr/OnNewClientConnected | — |

---

<!-- verify: boundary-bleed | foreign: AsyncSocket, GetRemoteEndpoint -->
