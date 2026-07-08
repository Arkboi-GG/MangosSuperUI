# IoContext

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`IoContext` is the platform-specific abstraction for asynchronous I/O event loops within the `IO` namespace. It encapsulates the underlying OS mechanism for monitoring file descriptors or handles: `epoll` on Linux, I/O Completion Ports (IOCP) on Windows, and `kqueue` on macOS/BSD. Created via the static factory `CreateIoContext` and managed by `std::unique_ptr`, it is non-copyable and non-movable. Its primary role is to provide the OS handle to lower-level components (sockets, acceptors) for registration and to manage the lifecycle of the event loop via `RunUntilShutdown`.

## Member-by-Member Behavior

### Lifecycle and State

*   **`CreateIoContext`**: Static factory returning a `std::unique_ptr<IoContext>`. Returns `nullptr` on failure.
*   **`~IoContext`**: Destructor cleaning up OS resources.
*   **`RunUntilShutdown`**: Blocks the calling thread in the main event loop until `Shutdown()` is invoked. Supports concurrent execution from multiple threads, though limited to core count is recommended.
*   **`IsRunning`**: Returns the status of the `m_isRunning` flag.
*   **`Shutdown`**: Signals the loop to terminate.

### Platform-Specific Accessors

*   **`GetUnixEpollDescriptor`** (Linux): Returns `m_epollDescriptor`.
*   **`GetWindowsCompletionPort`** (Windows): Returns `m_completionPort`.
*   **`GetKqueueDescriptor`** (macOS/BSD): Returns `m_kqueueDescriptor`.

### Thread Communication

*   **`PostOperationForImmediateInvocation`** (Windows): Posts a task to the IOCP to wake the loop.
*   **`PostForImmediateInvocation`** (Linux/macOS/BSD): Signals the I/O thread to invoke `SystemIoEventReceiver::OnIoEvent`. On Linux, this involves writing to `m_contextSwitchNotifyEventFd` and queuing the receiver in `m_contextSwitchQueue` under `m_contextSwitchQueueLock`.

## Cross-Unit Boundaries

*   **Called by `AsyncSocket._posix/InitializeAndFixateMemoryLocation`**: Retrieves the epoll descriptor via `GetUnixEpollDescriptor` to register the socket with the event loop.
*   **Called by `AsyncSocketAcceptor_posix/CreateAndBindServer`**: Retrieves the epoll descriptor via `GetUnixEpollDescriptor` to register the acceptor socket.

## Data Model

This unit does not interact with any database tables.

## Notable Implementation Details

*   **Platform Abstraction**: Heavy use of `#if defined(...)` results in distinct member variables and methods per OS.
*   **Deleted Special Members**: Copy/move constructors and assignment operators are deleted to enforce unique ownership of OS handles.
*   **Linux Context Switching**: Uses an `eventfd` (`m_contextSwitchNotifyEventFd`) to wake `epoll_wait` and a mutex-protected queue (`m_contextSwitchQueue`) to safely transfer `SystemIoEventReceiver` pointers from worker threads to the I/O thread.
*   **Epoll Data Interpretation**: The `IoContextEpollTargetType` enum defines how `epoll_event.data.u32` is interpreted: `IoEventReceiverFunction` for standard I/O or `ContextSwitchRequest` for internal wake-ups.

## Member Reference

*   **IoContext#2**: Deleted copy constructor declaration.
*   **operator=#2**: Deleted copy assignment operator declaration.
*   **IoContext**: Private platform-specific constructor declarations.
*   **operator=**: Deleted move assignment operator declaration.
*   **GetUnixEpollDescriptor**: Linux-only method returning the `epoll` file descriptor.

---

<!-- machine-true, projected from graph.json -->

## Map — IoContext

*Source:* IoContext.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| IoContext#2 | decl | — | — | — |
| operator=#2 | decl | — | — | — |
| IoContext | decl | — | — | — |
| operator= | decl | — | — | — |
| GetUnixEpollDescriptor | method | — | AsyncSocket._posix/InitializeAndFixateMemoryLocation, AsyncSocketAcceptor_posix/CreateAndBindServer | — |
