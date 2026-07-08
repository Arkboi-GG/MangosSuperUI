# ThreadPool

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ThreadPool

## Purpose & Responsibilities

`ThreadPool` is a generic, reusable concurrency utility within the `wowvmangos` codebase designed to execute collections of callable tasks (`std::function<void()>`) across a fixed number of worker threads. It abstracts the complexity of thread lifecycle management, synchronization, and workload distribution, allowing other subsystems (such as `Map`, `MapManager`, and `SqlOperations`) to offload computationally expensive or blocking operations without managing threads directly.

The class supports two distinct execution strategies for distributing tasks among workers:
1.  **Single Queue (`SingleQueue` / `worker_sq`):** Workers compete for tasks from a shared index. This is suitable for independent tasks where order does not matter and load balancing is dynamic.
2.  **Multi Queue (`MultiQueue` / `worker_mq`):** Tasks are statically partitioned among workers based on their ID. Worker $N$ processes tasks at indices $N, N+Size, N+2*Size$, etc. This reduces contention on the shared index but requires the workload to be divisible evenly or tolerate uneven distribution.

Additionally, `ThreadPool` provides specialized wrappers (`MySQL`) that initialize and finalize MySQL connections per thread, ensuring thread-safe database access for SQL-related workloads.

The pool operates in a state machine fashion (`Status`), transitioning between `STOPPED`, `STARTING`, `READY`, `PROCESSING`, `ERROR`, and `TERMINATING`. It supports pre- and post-execution callbacks (`pre`, `post`) for each workload batch and offers configurable error handling policies (`IGNORE`, `LOG`, `TERMINATE`, `NONE`).

## Member-by-Member Behavior

### Initialization and Lifecycle

**`ThreadPool` (Constructor)**
Initializes the pool with a name, thread count, clearing policy, and error handling mode. It reserves memory for the worker vector but does **not** create threads yet. Threads are created lazily via the `start()` template method (defined in the header, not listed in the MAP as a separate member because it is inline/template, but referenced by the constructor's design). The `m_status` starts as `STOPPED`.

**`~ThreadPool` (Destructor)**
Sets the status to `TERMINATING` and notifies all waiting workers to exit their loops. It relies on the `worker` destructors to join the actual OS threads. Note that the destructor does not explicitly delete the `m_workers` vector elements; however, since `m_workers` is a `std::vector<std::unique_ptr<worker>>`, the unique pointers will automatically delete the worker objects, triggering `worker::~worker`, which joins the threads.

### Workload Management

**`processWorkload` (Method 1: No Workload Argument)**
Triggers the execution of the currently loaded `m_workload`.
1.  Checks if `ClearMode::AT_NEXT_WORKLOAD` is active and the pool is dirty; if so, clears old data.
2.  Validates that the pool is `READY` and has tasks.
3.  Resets the result promise, marks the pool as `dirty`, sets `m_active` to the minimum of thread count and workload size, and resets `m_index` to 0.
4.  Sets status to `PROCESSING`.
5.  Iterates through the first `m_active` workers and calls their `prepare(pre, post)` method, setting up pre/post hooks.
6.  Notifies all workers via `m_waitForWork`.
7.  Returns a `std::future<void>` that resolves when the workload completes or errors.

**`processWorkload` (Method 2: Lvalue Reference Workload)**
Assigns the provided `workload` vector to `m_workload`, marks the pool as clean (`m_dirty = false`), and delegates to Method 1.

**`processWorkload` (Method 3: Rvalue Reference Workload)**
Moves the provided `workload` vector into `m_workload`, marks the pool as clean, and delegates to Method 1. This allows efficient transfer of large task lists without copying.

**`operator<<`**
Appends a single callable task to the `m_workload` vector.
*   **Safety Check:** Throws an exception if the pool is currently `PROCESSING` or in `ERROR` state, preventing race conditions where tasks are added while workers are actively consuming them.
*   **Cleanup:** If `ClearMode::AT_NEXT_WORKLOAD` is set and the pool is dirty, it clears the existing workload before adding the new task.
*   **Note:** The header comments explicitly state this is **NOT threadsafe**. It must be called from a single thread (typically the main game loop or update thread) before invoking `processWorkload`.

**`clearWorkload`**
Resets `m_dirty` to false and clears the `m_workload` vector. Used to discard pending tasks. Like `operator<<`, it is not threadsafe and should only be called when no workers are active.

### Status and Inspection

**`status`**
Returns the current `Status` enum value. Used by callers (e.g., `MapManager/Update`) to check if the pool is ready to accept new work or if a previous operation failed.

**`size`**
Returns the configured number of threads (`m_size`). Used by callers to determine parallelism capacity.

**`taskErrors`**
Returns a copy of the `m_errors` vector, which contains `std::exception_ptr` objects captured during task execution. This is primarily used for debugging or logging after a workload completes with errors.

### Worker Internals

The `worker` struct is the base class for all thread implementations.

**`worker` (Constructor)**
Creates a new OS thread using `IO::Multithreading::CreateThread`. The thread executes `loop_wrapper`. The thread name includes the pool name and worker ID for debugging.

**`~worker` (Destructor)**
Joins the underlying OS thread, ensuring orderly shutdown.

**`loop_wrapper`**
The top-level entry point for the worker thread. It implements the error handling strategy:
*   **NONE:** Calls `loop()` directly. Exceptions propagate up (likely crashing the thread or process depending on context).
*   **IGNORE/LOG/TERMINATE:** Wraps `loop()` in a try-catch block.
    *   If an exception occurs, it captures it.
    *   If `IGNORE`, it recursively calls `loop_wrapper()` to continue processing subsequent tasks (skipping the failed one implicitly by moving to the next iteration in `loop`).
    *   If `LOG`, it logs the exception message via `Log.Main/Out` and then continues.
    *   If `TERMINATE`, it sets the pool status to `TERMINATING` and exits.
    *   In all non-NONE cases, if the pool isn't terminating, it recursively calls `loop_wrapper()` to keep the thread alive for future work.

**`prepare`**
Sets the `busy` flag to true and stores the `pre` and `post` callbacks. This is called by `ThreadPool::processWorkload` before notifying workers.

**`loop`**
The main infinite loop of the worker:
1.  Calls `waitForWork()` to block until notified or terminated.
2.  Checks for termination.
3.  Executes `pre` callback if present.
4.  Calls the virtual `doWork()` method (implemented by subclasses).
5.  Executes `post` callback if present.
6.  Sets `busy` to false.
7.  Decrements `m_active`. If `m_active` reaches zero, it means all workers have finished their assigned slice of the workload.
    *   If `ClearMode::UPPON_COMPLETION`, it clears the workload.
    *   If an error occurred (`Status::ERROR`), it sets the result promise with the exception.
    *   Otherwise, it sets the result promise successfully.
    *   Resets status to `READY`.

**`waitForWork`**
Acquires a shared lock on `m_mutex` and waits on `m_waitForWork` condition variable until `busy` becomes true or the pool status is `TERMINATING`. This prevents busy-waiting.

### Specific Worker Implementations

**`worker_mq` (Multi-Queue Worker)**
*   **`worker_mq` (Constructor):** Initializes the base `worker`.
*   **`prepare`:** Overrides the base `prepare` to set the internal iterator `it` to the worker's `id`. This ensures each worker starts at a different offset in the workload vector.
*   **`doWork`:** Processes tasks starting at `it`, incrementing `it` by `pool->m_size` each time. This static partitioning minimizes lock contention on `m_index` but can lead to load imbalance if tasks vary significantly in duration.

**`worker_sq` (Single-Queue Worker)**
*   **`worker_sq` (Constructor):** Initializes the base `worker`.
*   **`doWork`:** Uses the atomic `m_index` to fetch tasks. Each worker atomically increments `m_index` to claim the next task. This provides better load balancing for variable-duration tasks but introduces atomic overhead and potential contention.

**`worker_mysql<T>` (MySQL Wrapper)**
*   **`worker_mysql` (Constructor):** Template constructor initializing the base worker type `T`.
*   **`doWork`:** Wraps the base `T::doWork()` call with `mysql_thread_init()` and `mysql_thread_end()`. This ensures that each thread has a properly initialized MySQL context before executing SQL tasks, which is critical for thread safety in MySQL client libraries.

## Cross-Unit Boundaries

### Callers (Who uses ThreadPool?)

1.  **`Map.Main`**:
    *   Calls `processWorkload` (no args) in `SendObjectUpdates`, `UpdateActiveCellsAsynch`, `UpdateCells`, and `UpdateVisibilityForRelocations`. These methods likely populate the workload with object update calculations and visibility checks, then trigger the pool to process them asynchronously.
    *   Calls `status` in `UpdateCells` to check if previous updates are complete.
    *   Calls `size` in `SendObjectUpdates`, `UpdateActiveCellsAsynch`, and `UpdateVisibilityForRelocations` to potentially adjust logic based on available parallelism.

2.  **`MapManager`**:
    *   Calls `processWorkload` (overloads #2 and #3) in `Update`. This suggests `MapManager` batches map-level updates and submits them to the pool.
    *   Calls `status` and `size` in `Update` for coordination and scaling decisions.

3.  **`SqlOperations`**:
    *   Calls `processWorkload` (no args) in `Update`. Likely used for asynchronous SQL query execution or result processing.
    *   Instantiates `ThreadPool` in `SqlResultQueue`.

4.  **`World`**:
    *   Calls `processWorkload` (overloads #2 and #3) in `Update`. Used for global world state updates.
    *   Instantiates `ThreadPool` in `Update`.

### Callees (Who does ThreadPool call?)

1.  **`CreateThread`**:
    *   Called by `worker` constructor. Used to create the underlying OS threads with specific naming conventions.

2.  **`Log.Main`**:
    *   Called by `loop_wrapper` via `sLog.Out`. Used to log exceptions caught during task execution when `ErrorHandling` is set to `LOG` or `TERMINATE`.

## Data Model

This unit does not interact directly with database tables. It is a pure concurrency abstraction. While it supports MySQL-specific thread initialization (`worker_mysql`), it does not execute SQL queries itself; it merely executes `Callable` objects provided by other units (like `SqlOperations`) which may perform database interactions. Therefore, no database tables are touched by `ThreadPool` directly.

## Notable Implementation Details

1.  **Recursive Error Handling Loop:**
    In `loop_wrapper`, if an exception is caught and `ErrorHandling` is not `TERMINATE`, the function recursively calls itself (`loop_wrapper()`). This is an unusual pattern. Typically, a loop would continue iteratively. Recursion here risks stack overflow if exceptions occur frequently in a tight loop, although in practice, exceptions are rare. However, it effectively restarts the `loop()` cycle after handling the error.

2.  **Static Partitioning vs. Dynamic Stealing:**
    The `MultiQueue` (`worker_mq`) implementation uses static partitioning (`it += pool->m_size`). This is highly efficient for uniform tasks but dangerous if one task is significantly slower than others, leading to straggler effects where some threads finish early and idle while others remain busy. The `SingleQueue` (`worker_sq`) uses atomic index incrementing, which balances load better but has higher contention overhead. Users must choose the appropriate worker type (`start<SingleQueue>()` vs `start<MultiQueue>()`) based on task characteristics.

3.  **Non-Threadsafe Workload Modification:**
    The `operator<<` and `clearWorkload` methods are explicitly documented as not threadsafe. They must be called from a single producer thread (likely the main game loop) before `processWorkload` is invoked. Mixing concurrent additions to the workload with active processing will lead to undefined behavior.

4.  **MySQL Thread Safety:**
    The `worker_mysql` wrapper ensures `mysql_thread_init()` and `mysql_thread_end()` are called around every `doWork` invocation. This is crucial because MySQL client libraries are not inherently thread-safe across different threads unless each thread initializes its own connection context. This wrapper allows the same `ThreadPool` instance to safely run SQL tasks.

5.  **Atomic Index Contention:**
    In `worker_sq::doWork`, `pool->m_index++` is performed atomically. While `std::atomic` is lock-free on most modern architectures, high contention from many threads simultaneously fetching tasks can still cause performance degradation due to cache line bouncing.

6.  **State Machine Rigidity:**
    The pool transitions strictly through states. Once `PROCESSING` begins, no new tasks can be added (`operator<<` throws). The pool must return to `READY` before accepting new work. This enforces a strict batch-processing model rather than a continuous stream model.

7.  **Exception Propagation in `NONE` Mode:**
    If `ErrorHandling::NONE` is selected, exceptions thrown by tasks will propagate out of `loop_wrapper`. Since `loop_wrapper` runs in a detached thread (created by `CreateThread`), uncaught exceptions in a thread typically terminate the entire process. This mode should only be used for debugging or when tasks are guaranteed not to throw.

8.  **Dirty Flag Logic:**
    The `m_dirty` flag is used in conjunction with `ClearMode::AT_NEXT_WORKLOAD`. If set, the workload is cleared before processing the next batch. This prevents stale tasks from being executed if the caller forgets to clear the workload manually.

## Member Reference

**ThreadPool** (ctor): Initializes the pool with name, thread count, clear mode, and error handling. Reserves worker vector memory. Does not start threads.

**~ThreadPool** (dtor): Sets status to TERMINATING, notifies workers, and relies on unique_ptr destruction to join threads.

**processWorkload** (method): Triggers execution of the current workload. Validates state, sets up pre/post hooks, notifies workers, and returns a future.

**processWorkload#2** (method): Assigns lvalue workload, marks clean, and delegates to primary processWorkload.

**processWorkload#3** (method): Moves rvalue workload, marks clean, and delegates to primary processWorkload.

**status** (method): Returns current pool status enum.

**size** (method): Returns configured thread count.

**taskErrors** (method): Returns vector of exception pointers from last workload.

**waitForWork** (method): Blocks worker thread until work is available or pool terminates.

**operator<<** (method): Appends task to workload. Throws if pool is processing. Not threadsafe.

**clearWorkload** (method): Clears workload vector and dirty flag. Not threadsafe.

**worker** (ctor): Creates OS thread via CreateThread, starting loop_wrapper.

**~worker** (dtor): Joins the OS thread.

**loop_wrapper** (method): Top-level thread loop with error handling logic (catch, log, ignore, terminate). Calls Log.Main/Out on errors.

**prepare** (method): Sets busy flag and stores pre/post callbacks.

**loop** (method): Main worker loop. Waits for work, executes pre/doWork/post, decrements active count, and signals completion if all workers done.

**worker_mq** (ctor): Initializes Multi-Queue worker.

**doWork** (method): Executes tasks in static partition (index += size).

**prepare#2** (method): Overrides prepare to set initial iterator to worker ID.

**worker_sq** (ctor): Initializes Single-Queue worker.

**doWork#2** (method): Executes tasks using atomic index increment for dynamic load balancing.

**worker_mysql<T>** (ctor): Initializes MySQL-aware worker.

**doWork#3** (function): Wraps base doWork with mysql_thread_init/end for thread safety.

---

<!-- machine-true, projected from graph.json -->

## Map — ThreadPool

*Source:* ThreadPool.cpp, ThreadPool.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ThreadPool | ctor | — | Map.Main/Map, MapManager/MapManager, MapManager/Update, SqlOperations/SqlResultQueue, World/Update | — |
| ~ThreadPool | dtor | — | — | — |
| processWorkload | method | — | Map.Main/SendObjectUpdates, Map.Main/UpdateActiveCellsAsynch, Map.Main/UpdateCells, Map.Main/UpdateVisibilityForRelocations, SqlOperations/Update | — |
| processWorkload#2 | method | — | MapManager/Update, World/Update | — |
| processWorkload#3 | method | — | MapManager/Update | — |
| status | method | — | Map.Main/UpdateCells, MapManager/Update | — |
| size | method | — | Map.Main/SendObjectUpdates, Map.Main/UpdateActiveCellsAsynch, Map.Main/UpdateVisibilityForRelocations, MapManager/Update | — |
| taskErrors | method | — | — | — |
| waitForWork | method | — | — | — |
| operator<< | method | — | — | — |
| clearWorkload | method | — | — | — |
| worker | ctor | CreateThread/CreateThread | — | — |
| ~worker | dtor | — | — | — |
| loop_wrapper | method | Log.Main/Out | — | — |
| prepare | method | — | — | — |
| loop | method | worker/doWork | — | — |
| worker_mq | ctor | — | — | — |
| doWork | method | — | — | — |
| prepare#2 | method | — | — | — |
| worker_sq | ctor | — | — | — |
| doWork#2 | method | — | — | — |
| worker_mysql<T> | ctor | — | — | — |
| doWork#3 | function | — | — | — |
