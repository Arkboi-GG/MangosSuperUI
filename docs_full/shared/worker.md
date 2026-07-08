# worker

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ThreadPool.h: Worker Interface Declaration

## Purpose & Responsibilities

`ThreadPool.h` defines the `ThreadPool` class, a generic thread pool implementation designed to execute batches of callable tasks (`std::function<void()>`) concurrently. The header establishes the architectural contract for the thread pool, including its lifecycle management, configuration options (error handling, workload clearing policies), and the internal worker hierarchy.

Crucially, this header **only declares** the interface for the worker threads; it does not contain the implementation of the core execution logic. The actual behavior of how workers pick up tasks, handle errors, and synchronize is implemented in the corresponding `.cpp` file (not provided here, but referenced via the MAP). This unit serves as the primary include target for users of the `ThreadPool` and defines the abstract base class `worker` and its concrete specializations (`worker_sq`, `worker_mq`, `worker_mysql`).

The `ThreadPool` supports three distinct worker strategies:
1.  **Single Queue (`worker_sq`)**: Workers pull from a single shared queue.
2.  **Multi Queue (`worker_mq`)**: Workers operate with multiple queues or indices (likely for load balancing or partitioning).
3.  **MySQL Specific (`worker_mysql`)**: A template specialization that likely handles database connection pooling or transaction scoping, inheriting from either `worker_sq` or `worker_mq`.

## Member-by-Member Behavior

The MAP identifies only one member belonging to this specific translation unit/partial: `doWork`. However, because `doWork` is a pure virtual function in the abstract base class `worker` and overridden in nested structs within this header, the documentation below covers the declaration and intent of these related declarations as they define the contract enforced by this header.

### Core Worker Contract

*   **`worker::doWork`**: Declared as a pure virtual function (`virtual void doWork() = 0;`) in the `worker` base struct. This enforces that any concrete worker type must implement the logic for retrieving and executing a single task from the pool's workload. It is the central hook where the thread pool's concurrency model diverges based on the chosen worker strategy.

### Concrete Worker Implementations (Declared)

*   **`worker_sq::doWork`**: Overrides the base `doWork`. As indicated by the name "sq" (Single Queue), this implementation is expected to retrieve the next available task from the shared `m_workload` vector protected by `m_mutex`. It likely increments an index or pops from the front/back depending on the synchronization strategy defined in the `.cpp` implementation.

*   **`worker_mq::doWork`**: Overrides the base `doWork`. The "mq" (Multi Queue) designation suggests a more complex retrieval mechanism, potentially involving multiple queues or a different indexing scheme (note the `int it` member in `worker_mq`). This allows for finer-grained parallelism or reduced contention compared to the single queue approach.

*   **`worker_mysql::doWork`**: Overrides the base `doWork` within the template struct `worker_mysql<T>`. This specialization likely wraps the underlying worker's `doWork` call with database-specific setup or teardown logic (e.g., ensuring a MySQL connection is active or transactions are committed/rolled back). It inherits from `T` (which defaults to `worker_sq`), meaning it delegates the actual task retrieval to the parent worker type after performing its MySQL-specific duties.

## Cross-Unit Boundaries

*   **Called by `ThreadPool/loop`**: The MAP indicates that `doWork` is called by `ThreadPool/loop`. In the context of the `worker` struct defined in this header, `loop()` is a member function that runs in the thread's context. The `loop()` function (implemented in the `.cpp` file) contains the main event loop for each worker thread. It waits for work via `waitForWork()`, then repeatedly calls `doWork()` until the workload is exhausted or the pool is shutting down. This boundary represents the transition from the thread's control flow (waiting/synchronizing) to the actual task execution logic defined by the specific worker type.

## Data Model

This unit interacts with no database tables. It operates entirely on in-memory data structures (`std::vector`, `std::atomic`, `std::mutex`).

## Notable Implementation Details

1.  **Pure Virtual Enforcement**: The `worker` base class uses `virtual void doWork() = 0;`. This ensures that `ThreadPool` cannot be instantiated with an incomplete worker strategy. Any attempt to instantiate a `worker` directly will fail at compile time.
2.  **Template Specialization for MySQL**: The `worker_mysql` template allows the thread pool to be adapted for database-heavy workloads without changing the core thread pool logic. By templating on `T` (defaulting to `SingleQueue`), it provides flexibility in how tasks are retrieved while adding a layer of database-specific behavior.
3.  **Error Handling Configuration**: The `worker` constructor accepts an `ErrorHandling` mode. While the handling logic is in the `.cpp`, the declaration shows that each worker instance carries its own error handling policy, allowing for potential per-worker customization (though typically set uniformly by the pool).
4.  **Thread Safety Annotations**: Comments in the header explicitly mark `operator<<` and `clearWorkload` as "NOT threadsafe". This is a critical constraint for users: tasks must be added to the workload before starting the pool or during designated safe windows, and the workload must not be cleared while threads are active.
5.  **Status Enum**: The `Status` enum defines the lifecycle states (`STOPPED`, `STARTING`, `READY`, `PROCESSING`, `TERMINATING`). The `start()` method checks for `Status::STOPPED` before proceeding, preventing accidental restarts.

## Member Reference

**doWork**
Declares the pure virtual interface in `worker` and overrides it in `worker_sq`, `worker_mq`, and `worker_mysql`. It defines the contract for executing a single task from the pool's workload. The base `worker` class declares it as pure virtual (`= 0`), forcing derived classes to implement the specific logic for retrieving and running a task. `worker_sq` implements single-queue retrieval, `worker_mq` implements multi-queue/indexed retrieval, and `worker_mysql` wraps the parent's implementation with database-specific logic. Called by the `loop()` method of the worker thread (implemented in `ThreadPool.cpp`).

---

<!-- machine-true, projected from graph.json -->

## Map — worker

*Source:* ThreadPool.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| doWork | decl | — | ThreadPool/loop | — |
