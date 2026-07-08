<!-- provenance: verbose -->
# AsyncSystemTimer

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`AsyncSystemTimer` is a singleton service providing a low-resolution (~16ms accuracy), asynchronous timer facility for the server process. It allows scheduling a callback function to execute once after a specified duration.

**Critical Constraint:** This timer is **not** for in-game logic in `mangosd`; use `player->m_Events.AddEvent` instead. It is intended for system-level tasks.

The implementation is platform-specific:
*   **Unix-like (Linux, macOS, BSDs):** Uses a dedicated background thread (`_TimerThreadFunc`) monitoring a sorted `std::deque` of pending timers.
*   **Windows:** Declares support for native timer queues in the header, but the provided `.cpp` source only implements the Unix-like threading model.

## Member-by-Member Behavior

### Initialization and Lifecycle

**`AsyncSystemTimer` (Constructor)**
Creates a background thread named "SystemTimer" via `IO::Multithreading::CreateThread`, which executes `_TimerThreadFunc`.

**`~AsyncSystemTimer` (Destructor)**
Default destructor. Proper shutdown requires calling `RemoveAllTimersAndStopThread` beforehand to join the thread; otherwise, undefined behavior may occur.

**`RemoveAllTimersAndStopThread`**
Gracefully shuts down the service: sets `m_threadRunning` to `false`, notifies `m_sleepSemaphore` to wake the worker, and joins the thread. Called by `Master/Run` and `realmd_Main/main` during shutdown.

### Scheduling and Execution

**`_ScheduleFunctionOnceMs`**
Core scheduling logic for Unix-like platforms:
1.  Calculates absolute trigger time.
2.  Creates a `TimerHandle` and `InternalTimerEntry`.
3.  Inserts the entry into `m_orderedPendingTimer` (sorted by time) under `m_orderedPendingTimer_mutex`. Uses linear search (noted as TODO for binary search).
4.  If the new timer is the earliest, notifies `m_sleepSemaphore` to wake the worker.
5.  Returns a `shared_ptr` to the `TimerHandle`.

**`_TimerThreadFunc`**
Worker thread loop:
1.  Locks `m_orderedPendingTimer_mutex`.
2.  Determines `sleepUntil` (next timer's trigger time or max time if empty).
3.  If `sleepUntil` has passed, pops the front timer, unlocks, and executes its callback **outside** the lock.
4.  Otherwise, waits on `m_sleepSemaphore` until `sleepUntil` or notification.

**`_DeleteTimer`**
Removes a timer from `m_orderedPendingTimer` by matching the `TimerHandle` pointer. Called by `TimerHandle::Cancel`.

## Cross-Unit Boundaries

*   **`IO::Multithreading::CreateThread`**: Called by the constructor to spawn the worker thread.
*   **`IO::Timer::TimerHandle`**:
    *   `_ScheduleFunctionOnceMs` constructs and returns a `shared_ptr` to a `TimerHandle`.
    *   `_DeleteTimer` receives a raw `TimerHandle*` to identify the timer to remove.
    *   `TimerHandle::Cancel` (in `TimerHandle` unit) calls `_DeleteTimer`.
*   **`Master/Run` and `realmd_Main/main`**: Call `RemoveAllTimersAndStopThread` during shutdown.

## Data Model

No database tables are accessed. All state is held in memory (`std::deque`, `std::mutex`, `std::condition_variable`).

## Notable Implementation Details

1.  **Linear Insertion:** `_ScheduleFunctionOnceMs` uses linear scan for insertion into the deque. Performance degrades with many pending timers.
2.  **Callback Safety:** Callbacks execute outside the mutex lock in `_TimerThreadFunc`. Callers must ensure callbacks are thread-safe and do not deadlock with other timer operations.
3.  **Volatile Flag:** `m_threadRunning` is `volatile`, though `std::atomic<bool>` is preferred in modern C++. The condition variable ensures wake-up correctness regardless.
4.  **Windows Gap:** The header declares Windows-specific members (`m_nativeTimerQueueHandle`, etc.), but the `.cpp` lacks their implementation.

## Member Reference

**`AsyncSystemTimer`**
Constructor. Spawns the background thread via `IO::Multithreading::CreateThread`.

**`RemoveAllTimersAndStopThread`**
Stops the service: sets `m_threadRunning` to false, notifies the semaphore, and joins the thread. Called by `Master/Run` and `realmd_Main/main`.

**`_ScheduleFunctionOnceMs`**
Schedules a callback. Creates a `TimerHandle`, inserts it into the sorted deque, and wakes the worker if it's the earliest timer. Returns a `shared_ptr` to the handle.

**`~AsyncSystemTimer`**
Default destructor. Requires prior call to `RemoveAllTimersAndStopThread`.

**`AsyncSystemTimer#3`**
Deleted copy constructor declaration.

**`operator=#2`**
Deleted move assignment operator declaration.

**`AsyncSystemTimer#2`**
Deleted move constructor declaration.

**`operator=`**
Deleted copy assignment operator declaration.

**`_TimerThreadFunc`**
Worker thread loop. Waits for the next timer expiration, then executes its callback outside the lock.

**`_DeleteTimer`**
Removes a timer from the pending queue by handle pointer. Called by `TimerHandle::Cancel`.

---

<!-- machine-true, projected from graph.json -->

## Map — AsyncSystemTimer

*Source:* AsyncSystemTimer.cpp, AsyncSystemTimer.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AsyncSystemTimer | ctor | CreateThread/CreateThread | — | — |
| RemoveAllTimersAndStopThread | method | — | Master/Run, realmd_Main/main | — |
| _ScheduleFunctionOnceMs | method | TimerHandle/TimerHandle | — | — |
| ~AsyncSystemTimer | dtor | — | — | — |
| AsyncSystemTimer#3 | decl | — | — | — |
| operator=#2 | decl | — | — | — |
| AsyncSystemTimer#2 | decl | — | — | — |
| operator= | decl | — | — | — |
| _TimerThreadFunc | method | — | — | — |
| _DeleteTimer | method | — | TimerHandle/Cancel | — |
