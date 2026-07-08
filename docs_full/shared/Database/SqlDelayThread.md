<!-- provenance: verbose -->
# SqlDelayThread

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# SqlDelayThread

## Purpose & Responsibilities

`SqlDelayThread` is a worker thread responsible for executing deferred SQL operations asynchronously. It maintains two distinct queues:
1.  **General Async Queue (`m_sqlQueue`)**: Populated by the central `Database` engine via `NextDelayedOperation`.
2.  **Serial Delay Queue (`m_serialDelayQueue`)**: Populated locally via `addSerialOperation`, ensuring strict FIFO execution order for operations requiring serialization on this specific connection.

The thread runs a continuous loop (`run`) that sleeps briefly between iterations, processes pending requests from both queues, and periodically verifies database connectivity. It manages the lifecycle of its `SqlConnection`, deleting it upon destruction.

## Member-by-Member Behavior

### Lifecycle & Control

**`SqlDelayThread` (Constructor)**
Initializes `m_dbEngine`, `m_dbConnection`, and sets `m_running` to `true`.

**`~SqlDelayThread` (Destructor)**
Flushes any operations queued during shutdown by calling `ProcessRequests()`, then deletes `m_dbConnection` to close the underlying database link.

**`Stop`**
Sets `m_running` to `false`, signaling the `run` loop to exit after the current iteration. Called by `Database/HaltDelayThread`.

**`run`**
The main thread loop:
1.  Initializes MySQL thread context (`mysql_thread_init`) if not PostgreSQL.
2.  Loops while `m_running`:
    *   Sleeps for 10ms to prevent busy-waiting.
    *   Calls `ProcessRequests()` to execute pending SQL.
    *   Periodically (based on `m_dbEngine->GetPingIntervalMs()`) logs a debug message, calls `m_dbEngine->Ping()`, and executes a dummy `SELECT 1` query on `m_dbConnection` to verify reachability.
3.  Cleans up MySQL thread context (`mysql_thread_end`) if not PostgreSQL.

### Queue Management

**`Delay`**
Adds a `SqlOperation` to the general async queue (`m_sqlQueue`). Always returns `true`.

**`addSerialOperation`**
Adds a `SqlOperation` to the local serial queue (`m_serialDelayQueue`). Called by `Database/AddToSerialDelayQueue`.

**`HasAsyncQuery`**
Returns `true` if `m_serialDelayQueue` is not empty. Uses `empty_unsafe()`, implying the caller manages synchronization or accepts potential races. Called by `Database/HasAsyncQuery`.

### Execution

**`ProcessRequests`**
Executes all pending operations:
1.  Retrieves operations from the global engine via `m_dbEngine->NextDelayedOperation(s)`, executes them via `s->Execute(m_dbConnection)`, and deletes the object.
2.  Retrieves operations from the local serial queue via `m_serialDelayQueue.next(s)`, executes them, and deletes the object.

## Cross-Unit Boundaries

*   **Database Engine (`Database`)**:
    *   `SqlDelayThread` calls `GetPingIntervalMs` and `Ping` in `run` for health checks.
    *   `SqlDelayThread` calls `NextDelayedOperation` in `ProcessRequests` to fetch global async tasks.
    *   `Database` calls `addSerialOperation` (via `AddToSerialDelayQueue`) to enqueue serialized tasks.
    *   `Database` calls `HasAsyncQuery` (via `HasAsyncQuery`) to check for pending serial work.
    *   `Database` calls `Stop` (via `HaltDelayThread`) to initiate shutdown.
    *   `Database` calls `run` (via `InitDelayThread`) to start the thread.

*   **SqlOperation**:
    *   `ProcessRequests` calls `Execute` on each operation, passing the thread's `SqlConnection`.

*   **Logging (`Log.Main`)**:
    *   `run` calls `sLog.Out` to log periodic reachability checks.

## Data Model

This unit does not interact directly with database tables. It executes `SqlOperation` objects provided by other units. The only SQL string embedded in this code is `SELECT 1` in `run`, used solely for connection liveness verification.

## Notable Implementation Details

*   **Redundant Health Check**: `run` calls both `m_dbEngine->Ping()` and `m_dbConnection->Query("SELECT 1")`. A `TODO` comment questions this redundancy, noting that `Ping()` may already suffice.
*   **Memory Ownership**: `ProcessRequests` takes ownership of `SqlOperation` pointers from both queues and deletes them after execution. Producers must heap-allocate these objects.
*   **Thread Safety**: `m_running` is `volatile`, which is technically insufficient for cross-thread synchronization in modern C++ (prefer `std::atomic`), though it likely functions correctly here due to the sleep loop. `HasAsyncQuery` uses `empty_unsafe()`, bypassing locks.
*   **MySQL Specifics**: `run` conditionally calls `mysql_thread_init()` and `mysql_thread_end()` only when `DO_POSTGRESQL` is undefined.

## Member Reference

**SqlDelayThread**
Constructor initializing `m_dbEngine`, `m_dbConnection`, and `m_running`.

**~SqlDelayThread**
Destructor flushing pending requests and deleting `m_dbConnection`.

**addSerialOperation**
Adds `SqlOperation` to `m_serialDelayQueue`. Called by `Database/AddToSerialDelayQueue`.

**HasAsyncQuery**
Checks if `m_serialDelayQueue` is empty (unsafe). Called by `Database/HasAsyncQuery`.

**run**
Main loop: sleeps, processes requests, and periodically pings DB. Calls `Database/GetPingIntervalMs`, `Database/Ping`, `Database/Query#2`, and `Log.Main/Out`. Called by `Database/InitDelayThread`.

**Delay**
Adds `SqlOperation` to `m_sqlQueue`.

**Stop**
Sets `m_running` to false. Called by `Database/HaltDelayThread`.

**ProcessRequests**
Executes and deletes operations from global engine (`Database/NextDelayedOperation`) and local serial queue. Calls `SqlOperation/Execute`.

---

<!-- machine-true, projected from graph.json -->

## Map — SqlDelayThread

*Source:* SqlDelayThread.cpp, SqlDelayThread.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| SqlDelayThread | ctor | — | — | — |
| ~SqlDelayThread | dtor | — | — | — |
| addSerialOperation | method | — | Database/AddToSerialDelayQueue | — |
| HasAsyncQuery | method | — | Database/HasAsyncQuery | — |
| run | method | Database/GetPingIntervalMs, Database/Ping, Database/Query#2, Log.Main/Out | Database/InitDelayThread | — |
| Delay | method | — | — | — |
| Stop | method | — | Database/HaltDelayThread | — |
| ProcessRequests | method | Database/NextDelayedOperation, SqlOperation/Execute | — | — |
