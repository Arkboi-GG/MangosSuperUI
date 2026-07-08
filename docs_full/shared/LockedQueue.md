<!-- provenance: verbose -->
# LockedQueue

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`LockedQueue` is a thread-safe FIFO container template defined in `LockedQueue.h`. It wraps a configurable storage type (defaulting to `std::deque`) with a mutex (`LockType`) to ensure atomic access from multiple threads. It supports standard queue operations (`add`, `next`, `clear`, `empty`) with automatic locking, manual lock management (`lock`, `unlock`, `peek`), and a cancellation flag (`cancel`, `cancelled`) to signal termination.

## Member-by-Member Behavior

### Initialization and Lifecycle
*   **Constructor**: Initializes `_canceled` to `false`. `_lock` and `_queue` are default-initialized.
*   **Destructor**: Virtual destructor with no custom cleanup.

### Queue Operations
*   **`add`**: Two overloads. One copies (`T const&`), one moves (`T&&`) an item to the back of `_queue`. Both acquire the lock internally.
*   **`next`**: Two overloads. The standard version retrieves the front item into `result` and pops it, returning `true` if successful or `false` if empty. The templated version accepts a `Checker`; it calls `check.Process(_queue.front())` and only pops/returns `true` if the checker returns `true`. Both acquire the lock internally.
*   **`clear`**: Empties `_queue` under lock. Does not reset `_canceled`.
*   **`empty`**: Returns `_queue.empty()` under lock.
*   **`empty_unsafe`**: Returns `_queue.empty()` without locking. Intended for use only when the caller already holds the lock.

### Manual Locking and Inspection
*   **`lock` / `unlock`**: Expose the underlying mutex for manual control.
*   **`peek`**: Acquires the lock via `lock()` and returns a reference to `_queue.front()`. It **does not** release the lock; the caller must call `unlock()`.

### Cancellation
*   **`cancel`**: Sets `_canceled` to `true` under lock.
*   **`cancelled`**: Returns `_canceled` under lock.

## Cross-Unit Boundaries

`LockedQueue` is a self-contained utility template. It has no outgoing calls to other units and is not listed as being called by specific other units in the provided MAP. It relies only on standard library headers (`<deque>`, `<mutex>`, `<cassert>`).

## Data Model

This unit does not interact with any database tables.

## Notable Implementation Details

1.  **`peek` Lock Leak Risk**: `peek()` calls `lock()` but never `unlock()`. Callers must explicitly call `unlock()` after using the returned reference, otherwise the queue remains permanently locked.
2.  **Cancellation State Persistence**: `clear()` empties the queue but leaves `_canceled` unchanged. A queue can be both empty and cancelled. There is no `reset()` method to clear the cancellation flag.
3.  **Volatile Comment**: `_canceled` is declared as `/*volatile*/ bool`. Since access is protected by `_lock`, `volatile` is unnecessary and correctly commented out; `std::atomic` is not required here.
4.  **Checker Atomicity**: The `next(..., Checker&)` overload holds the lock during the `check.Process()` call, ensuring that the inspection and potential removal are atomic relative to other queue operations.

## Member Reference

**LockedQueue<T, LockType, StorageType>**: Constructor. Initializes `_canceled` to `false`.

**~LockedQueue<T, LockType, StorageType>**: Virtual destructor. No custom cleanup.

**add#2**: Overload accepting `T&&`. Moves item to back of `_queue` under lock.

**add**: Overload accepting `T const&`. Copies item to back of `_queue` under lock.

**next**: Retrieves front item into `result` and pops it. Returns `true` if successful, `false` if empty. Acquires lock. Also includes templated overload with `Checker`.

**peek**: Acquires lock and returns reference to front item. Does not release lock; caller must call `unlock()`.

**cancel**: Sets `_canceled` to `true` under lock.

**cancelled**: Returns `_canceled` under lock.

**lock**: Acquires internal `_lock`.

**unlock**: Releases internal `_lock`.

**clear**: Empties `_queue` under lock. Does not reset `_canceled`.

**empty_unsafe**: Returns `_queue.empty()` without locking.

**empty**: Returns `_queue.empty()` under lock.

---

<!-- machine-true, projected from graph.json -->

## Map — LockedQueue

*Source:* LockedQueue.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| LockedQueue<T, LockType, StorageType> | ctor | — | — | — |
| ~LockedQueue<T, LockType, StorageType> | dtor | — | — | — |
| add#2 | function | — | — | — |
| add | function | — | — | — |
| next | function | — | — | — |
| peek | function | — | — | — |
| cancel | function | — | — | — |
| cancelled | function | — | — | — |
| lock | function | — | — | — |
| unlock | function | — | — | — |
| clear | function | — | — | — |
| empty_unsafe | function | — | — | — |
| empty | function | — | — | — |
