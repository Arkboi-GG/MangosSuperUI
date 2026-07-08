# TimerHandle

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# TimerHandle

`TimerHandle` is a lightweight RAII-style handle representing a scheduled asynchronous timer event within the `IO::Timer` subsystem. It encapsulates a reference to the owning `AsyncSystemTimer` instance and the specific callback function to be executed when the timer expires. Its primary responsibility is to provide a mechanism for the caller to cancel the pending timer before it fires, thereby preventing the execution of stale or irrelevant callbacks.

## Purpose & Responsibilities

The core purpose of `TimerHandle` is to manage the lifecycle of a single timer registration. When a timer is scheduled via `AsyncSystemTimer`, a `TimerHandle` is returned to the caller. This handle serves two functions:
1.  **Identity:** It holds the context (`m_asyncSystemTimer`) and payload (`m_callback`) associated with the timer.
2.  **Cancellation:** It provides the `Cancel()` method, which allows the owner of the handle to request the removal of the timer from the system's active queue.

This design decouples the scheduling logic (inside `AsyncSystemTimer`) from the cancellation logic (exposed via `TimerHandle`), ensuring that only entities holding a valid handle can cancel a specific timer.

## Member-by-Member Behavior

### Construction
The constructor `TimerHandle` initializes the internal state with the pointer to the managing `AsyncSystemTimer` and moves the provided `std::function<void()>` callback into the member variable `m_callback`. This transfer of ownership for the callback ensures efficient storage without unnecessary copies.

### Cancellation
The `Cancel()` method is the primary operational interface. When invoked, it delegates the actual removal work to the owning `AsyncSystemTimer` by calling `_DeleteTimer(this)`. This indicates that `TimerHandle` does not manage the underlying data structures (such as priority queues or hash maps) itself; instead, it acts as a key or token that the central timer manager uses to locate and remove the corresponding entry.

## Cross-Unit Boundaries

`TimerHandle` interacts primarily with `AsyncSystemTimer` and is consumed by socket management classes.

*   **Calls Out:**
    *   **`AsyncSystemTimer::_DeleteTimer`:** Called by `TimerHandle::Cancel`. The `TimerHandle` passes its own address (`this`) to the timer manager. This suggests that `AsyncSystemTimer` likely stores pointers to `TimerHandle` instances (or uses them as unique identifiers) to track active timers. The direction is from the handle to the manager, requesting deletion.

*   **Called By:**
    *   **`AsyncSystemTimer::_ScheduleFunctionOnceMs`:** This internal scheduling method constructs a new `TimerHandle` and returns it to the caller. This is the factory point for handles.
    *   **`AuthSocket::~AuthSocket`:** The destructor of `AuthSocket` calls `Cancel()` on its associated timer handle. This ensures that if an authentication socket is destroyed while a timer is still pending (e.g., a timeout waiting for auth data), the timer is cleaned up to prevent dangling callback executions.
    *   **`WorldSocket::_HandleAuthSession`:** This method likely cancels a previous timer upon successful authentication or session establishment, preventing redundant timeout handling.
    *   **`WorldSocket::~WorldSocket`:** Similar to `AuthSocket`, the destructor cancels any pending timers to ensure clean resource release when a world socket connection is closed.

These call sites highlight that `TimerHandle` is integral to the lifetime management of network sockets, ensuring that time-based events (like timeouts) do not outlive the objects they monitor.

## Data Model

`TimerHandle` does not interact with any database tables. It operates entirely in memory, managing transient runtime state related to asynchronous event scheduling.

## Notable Implementation Details

*   **Raw Pointer Ownership:** The `TimerHandle` stores a raw pointer `m_asyncSystemTimer`. It assumes that the `AsyncSystemTimer` instance outlives the `TimerHandle`. If the `AsyncSystemTimer` is destroyed while `TimerHandle` instances still exist, calling `Cancel()` would result in undefined behavior (dereferencing a dangling pointer). The caller must ensure proper ordering of destruction.
*   **Self-Reference in Deletion:** The `Cancel()` method passes `this` to `_DeleteTimer`. This implies that the `AsyncSystemTimer`'s internal data structure likely uses the `TimerHandle`'s memory address as a unique key or identifier for the timer entry. This is a common pattern for O(1) or O(log N) lookup/removal if the container supports pointer-based keys.
*   **No Callback Execution on Cancel:** The `Cancel()` method does not invoke `m_callback`. It simply requests removal. If the timer has already fired or is being processed, the behavior depends on `AsyncSystemTimer::_DeleteTimer`'s implementation (not shown here), but typically, cancellation prevents future execution.

## Member Reference

**TimerHandle**  
Constructor that initializes the handle with a pointer to the owning `AsyncSystemTimer` and moves the callback function into the member variable.

**Cancel**  
Method that requests the removal of this timer from the active schedule by calling `AsyncSystemTimer::_DeleteTimer` with the handle's address. Used by socket destructors and session handlers to clean up pending timeouts.

---

<!-- machine-true, projected from graph.json -->

## Map — TimerHandle

*Source:* TimerHandle.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| TimerHandle | ctor | — | AsyncSystemTimer/_ScheduleFunctionOnceMs | — |
| Cancel | method | AsyncSystemTimer/_DeleteTimer | AuthSocket/~AuthSocket, WorldSocket/_HandleAuthSession, WorldSocket/~WorldSocket | — |
