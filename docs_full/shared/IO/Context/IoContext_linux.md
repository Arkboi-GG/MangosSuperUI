<!-- provenance: verbose -->
# IoContext_linux

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# IoContext_linux

**Purpose & Responsibilities**

`IoContext_linux` implements the Linux-specific asynchronous I/O event loop for the `wowvmangos` network subsystem. It wraps the `epoll` API to manage file descriptor events and uses an `eventfd` to allow worker threads to inject tasks into the I/O thread safely.

## Member-by-Member Behavior

### Initialization and Lifecycle

**`IoContext` (Constructor)**
Initializes the object with the provided `epoll` and `eventfd` file descriptors. Sets `m_isRunning` to `true`. This constructor is private and invoked only by `CreateIoContext`.

**`~IoContext` (Destructor)**
Closes `m_contextSwitchNotifyEventFd` and `m_epollDescriptor` using `::close()`.

**`CreateIoContext` (Static Factory Method)**
Creates and configures a new `IoContext` instance:
1.  Calls `::epoll_create(50)`. On failure, logs an error via `Log.Main/Out` using `SystemErrorToString/SystemErrorToString` and returns `nullptr`.
2.  Calls `::eventfd(0, 0)`. On failure, logs an error and returns `nullptr`.
3.  Registers the `eventfd` with `epoll` using `::epoll_ctl` with `EPOLLIN | EPOLLET` flags, tagged with `IoContextEpollTargetType::ContextSwitchRequest`. On failure, logs an error and returns `nullptr`.
4.  Returns a `std::unique_ptr<IO::IoContext>`.

### Event Loop Execution

**`RunUntilShutdown`**
Executes the main event loop while `m_isRunning` is `true`:
1.  Calls `::epoll_wait` with a 500ms timeout and a max capacity of 250 events. Errors other than `EINTR` are logged via `Log.Main/Out` with `SystemErrorToString/SystemErrorToString`; `EINTR` is ignored.
2.  Iterates through returned events:
    *   **Context Switch Request:** If the event data matches `IoContextEpollTargetType::ContextSwitchRequest`, it drains `m_contextSwitchQueue`. It locks `m_contextSwitchQueueLock`, re-checks emptiness, dequeues the front `SystemIoEventReceiver`, unlocks, and calls `OnIoEvent(0)` on the receiver.
    *   **Standard I/O Event:** For other events, it casts `event.data.ptr` to `SystemIoEventReceiver*` and calls `OnIoEvent(event.events)`.

**`IsRunning`**
Returns the current state of the `m_isRunning` flag.

**`Shutdown`**
Sets `m_isRunning` to `false`, causing the `RunUntilShutdown` loop to exit on its next iteration.

### Cross-Thread Task Posting

**`PostForImmediateInvocation`**
Allows other threads to post a `SystemIoEventReceiver*` for execution by the I/O thread:
1.  Manually locks `m_contextSwitchQueueLock`.
2.  Pushes the `eventReceiver` onto `m_contextSwitchQueue`.
3.  Unlocks `m_contextSwitchQueueLock`.
4.  Writes `1` to `m_contextSwitchNotifyEventFd` via `::eventfd_write`, triggering an `EPOLLIN` event to wake the I/O thread.

## Cross-Unit Boundaries

*   **`Master/Run` and `realmd_Main/main`**: Call `CreateIoContext` to initialize the context, `RunUntilShutdown` to start the loop, and `Shutdown` to stop it.
*   **`Log.Main/Out`**: Called by `CreateIoContext` and `RunUntilShutdown` to log errors during initialization or event processing.
*   **`SystemErrorToString/SystemErrorToString`**: Called by `CreateIoContext` and `RunUntilShutdown` to convert `errno` values to strings for logging.
*   **`AsyncSocket._posix/EnterIoContext`**: Calls `PostForImmediateInvocation` to register socket handlers with the I/O context.

## Data Model

This unit does not interact with any database tables.

## Notable Implementation Details

*   **Edge-Triggered Signaling:** The `eventfd` is registered with `EPOLLET`. `RunUntilShutdown` drains the entire `m_contextSwitchQueue` upon receiving a signal to ensure no tasks are missed.
*   **Double-Check Locking:** In `RunUntilShutdown`, the code checks `m_contextSwitchQueue.empty()` before and after acquiring the lock to reduce contention.
*   **Manual Mutex Management:** `PostForImmediateInvocation` uses manual `lock()`/`unlock()` instead of `std::lock_guard` to exclude the `::eventfd_write` call from the critical section.
*   **Fixed Event Buffer:** `RunUntilShutdown` uses a stack-allocated array of 250 events for `epoll_wait`. Excess events are deferred to subsequent iterations.
*   **Shutdown Timeout:** The 500ms `epoll_wait` timeout ensures `Shutdown` takes effect within half a second.

## Member Reference

**IoContext** Constructor; initializes file descriptors and sets `m_isRunning` to true.
**~IoContext** Destructor; closes `m_contextSwitchNotifyEventFd` and `m_epollDescriptor`.
**CreateIoContext** Static factory; creates `epoll` and `eventfd`, registers the latter with `epoll`, logs errors via `Log.Main/Out` and `SystemErrorToString/SystemErrorToString`, and returns a `unique_ptr`.
**RunUntilShutdown** Main loop; waits on `epoll` with 500ms timeout, dispatches context switch requests by draining `m_contextSwitchQueue`, and invokes `OnIoEvent` on receivers; logs errors via `Log.Main/Out` and `SystemErrorToString/SystemErrorToString`.
**IsRunning** Returns the `m_isRunning` flag.
**Shutdown** Sets `m_isRunning` to false.
**PostForImmediateInvocation** Enqueues a receiver into `m_contextSwitchQueue` under lock and signals the I/O thread via `eventfd_write`.

---

<!-- machine-true, projected from graph.json -->

## Map — IoContext_linux

*Source:* IoContext_linux.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| IoContext | ctor | — | — | — |
| ~IoContext | dtor | — | — | — |
| CreateIoContext | method | Log.Main/Out, SystemErrorToString/SystemErrorToString | Master/Run, realmd_Main/main | — |
| RunUntilShutdown | method | Log.Main/Out, SystemErrorToString/SystemErrorToString | Master/Run, realmd_Main/main | — |
| IsRunning | method | — | — | — |
| Shutdown | method | — | Master/Run, realmd_Main/main | — |
| PostForImmediateInvocation | method | — | AsyncSocket._posix/EnterIoContext | — |
