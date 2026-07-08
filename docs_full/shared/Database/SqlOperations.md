# SqlOperations

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# SqlOperations

**Purpose & Responsibilities**

`SqlOperations` provides the abstraction layer for executing SQL statements and queries within the `wowvmangos` database subsystem. It distinguishes between two primary modes of operation: **synchronous/transactional statements** (which run immediately on a database connection) and **asynchronous queries** (which run on a background thread pool and return results via callbacks).

The unit defines several key classes:
1.  **`SqlOperation`**: The base interface for any database action.
2.  **`SqlPlainRequest`, `SqlPreparedRequest`, `SqlTransaction`**: Concrete implementations for synchronous DML/DDL operations and transactional batches. These are typically used for writes or immediate reads where the caller blocks until completion.
3.  **`SqlQuery` and `SqlQueryHolder`**: Implementations for asynchronous data retrieval. `SqlQuery` handles single queries, while `SqlQueryHolder` allows batching multiple queries into a single asynchronous job, reducing thread overhead.
4.  **`SqlResultQueue`**: A thread-safe queue that manages the synchronization of query results back to the main game threads. It separates "thread-safe" callbacks (executed on worker threads) from "thread-unsafe" ones (executed on the main thread to prevent race conditions).

This unit does not interact with specific game tables directly; instead, it provides the mechanism by which other units (e.g., `WorldSession`, `Player`) submit SQL strings and receive `QueryResult` objects.

## Member-by-Member Behavior

### Synchronous Operations (`SqlPlainRequest`, `SqlPreparedRequest`, `SqlTransaction`)

These classes implement the `SqlOperation` interface and are designed to be executed directly on a `SqlConnection`. They acquire a lock on the connection before executing to ensure thread safety.

*   **`SqlPlainRequest`**: Wraps a raw SQL string. Its `Execute` method locks the connection and calls `Database/Execute`. It is used for simple, non-parameterized statements.
*   **`SqlPreparedRequest`**: Wraps a prepared statement index and parameters. Its `Execute` method locks the connection and calls `Database/ExecuteStmt`. This is used for parameterized queries to prevent SQL injection and improve performance for repeated statements.
*   **`SqlTransaction`**: Manages a vector of `SqlOperation` pointers. Its `Execute` method begins a transaction on the connection, iterates through the queued operations, and executes them sequentially. If any operation fails, it rolls back the entire transaction. If all succeed, it commits. This ensures atomicity for complex updates involving multiple steps.

### Asynchronous Queries (`SqlQuery`, `SqlQueryHolder`, `SqlQueryHolderEx`)

These classes facilitate non-blocking database reads. The workflow involves submitting a query to a delay queue, which eventually runs on a background thread, stores the result in the holder, and signals the original thread via a callback.

*   **`SqlQuery`**: Represents a single asynchronous query. Its `Execute` method runs on a background thread. It acquires the connection lock, executes the query via `Database/Query`, moves the resulting `QueryResult` into the associated `IQueryCallback` via `IQueryCallback/SetResult`, and adds the callback to the `SqlResultQueue` for synchronization.
*   **`SqlQueryHolder`**: Acts as a container for multiple queries.
    *   **`SetQuery`** and **`SetPQuery`**: Store SQL strings at specific indices. `SetPQuery` uses `vsnprintf` to format the string safely. Both methods log errors if the index is out of bounds or if a slot is already occupied.
    *   **`SetSize`**: Pre-allocates memory for the query vector to optimize performance.
    *   **`Execute`**: Does not execute SQL itself. Instead, it creates a `SqlQueryHolderEx` object and adds it to the `Database/AddToSerialDelayQueue`. This defers execution to a dedicated delay thread.
    *   **`TakeResult`**: Retrieves and removes the `QueryResult` from a specific index. It logs an error if the index is invalid or empty. The caller takes ownership of the returned pointer.
    *   **`SetResult`**: Stores a `QueryResult` into a specific index. This is called by `SqlQueryHolderEx` after execution.
    *   **`DeleteAllResults`**: Clears all stored results, freeing memory. This is crucial for cleanup if results are not consumed.
*   **`SqlQueryHolderEx`**: A wrapper that holds references to the `SqlQueryHolder`, callback, and result queue. Its `Execute` method runs on the delay thread. It iterates through the holder's queries, executes each one on the provided connection, stores the results back into the holder via `SetResult`, and finally adds the callback to the `SqlResultQueue` to notify the main thread.

### Result Synchronization (`SqlResultQueue`)

*   **`SqlResultQueue`**: Inherits from `LockedQueue` to provide thread-safe access. It manages a `ThreadPool` for executing thread-safe callbacks.
*   **`Update`**: Called periodically (likely by the main loop). It processes pending callbacks:
    *   Thread-safe callbacks are dispatched to the `m_callbackThreads` pool for parallel execution.
    *   Thread-unsafe callbacks are executed synchronously on the current thread to maintain consistency with game state.
    *   It logs a warning if the number of unsafe queries exceeds 1000, indicating a potential bottleneck.
*   **`CancelAll`**: Used during server shutdown. It drains the queue, sets results to `nullptr`, executes the callbacks (to allow graceful cleanup), and deletes them.

## Cross-Unit Boundaries

*   **`Database`**: `SqlOperations` relies heavily on `Database` (specifically `SqlConnection`) for the actual execution of SQL. Methods like `Execute`, `ExecuteStmt`, `Query`, `BeginTransaction`, `CommitTransaction`, and `RollbackTransaction` are called on the connection object. The `Database` unit also provides the `AddToSerialDelayQueue` method for scheduling asynchronous work.
*   **`Lock`**: All `Execute` methods in `SqlPlainRequest`, `SqlPreparedRequest`, `SqlTransaction`, `SqlQuery`, and `SqlQueryHolderEx` use `Lock/Lock` (via the `LOCK_DB_CONN` macro) to acquire a mutex on the `SqlConnection`. This prevents concurrent access to the same database connection from multiple threads.
*   **`IQueryCallback`**: This interface is central to the asynchronous model. `SqlQuery` and `SqlQueryHolderEx` call `SetResult` to store the outcome. `SqlResultQueue::Update` and `CancelAll` call `Execute` to trigger the callback logic in the calling unit (e.g., `Player.Main/LoadFromDB`). `IsThreadSafe` is checked to determine execution context.
*   **`ThreadPool`**: `SqlResultQueue` uses `ThreadPool` to manage worker threads for thread-safe callbacks. `processWorkload` is called to trigger execution, and `wait` ensures the main thread blocks until these callbacks complete.
*   **`Log.Main`**: Various methods log errors (e.g., out-of-bounds indices, truncation) or performance warnings (e.g., high unsafe query count).
*   **`WorldTimer`**: `SqlResultQueue::Update` uses `getMSTime` and `getMSTimeDiffToNow` to enforce timeouts on the processing of unsafe queries, preventing the main thread from hanging indefinitely.

## Data Model

This unit does not interact with specific database tables. It operates on raw SQL strings and `QueryResult` objects. The SQL content is determined by the callers (e.g., `WorldSession`, `Player`), which construct queries against tables such as `characters`, `account`, etc. `SqlOperations` is agnostic to the schema.

## Notable Implementation Details

*   **Memory Management**: `SqlPlainRequest` and `SqlQuery` use `mangos_strdup` to copy SQL strings, ensuring the original buffer can be freed by the caller. Their destructors explicitly `delete[]` these buffers. `SqlPreparedRequest` takes ownership of `SqlStmtParameters` and deletes it in its destructor.
*   **Thread Safety Strategy**: The system distinguishes between thread-safe and thread-unsafe callbacks. Thread-safe callbacks (e.g., those that only update independent data structures) are offloaded to a thread pool. Thread-unsafe callbacks (e.g., those that modify player state) are executed on the main thread. This hybrid approach balances performance with correctness.
*   **Error Handling**: `SetQuery` and `SetPQuery` perform bounds checking and log errors if indices are invalid or slots are reused. `TakeResult` also validates indices. This prevents undefined behavior from incorrect usage.
*   **Bottleneck Detection**: `SqlResultQueue::Update` monitors `numUnsafeQueries`. If it exceeds 1000, a performance warning is logged. This indicates that too many queries are forcing main-thread execution, which can degrade server tick rates.
*   **Transaction Atomicity**: `SqlTransaction::Execute` ensures that either all queued operations succeed or none do. It catches failures early and rolls back, maintaining database integrity.
*   **Asynchronous Batching**: `SqlQueryHolder` allows multiple queries to be batched into a single asynchronous job. This reduces the overhead of thread switching and queue management compared to issuing many individual `SqlQuery` objects.

## Member Reference

**Execute** (SqlPlainRequest): Acquires a lock on the `SqlConnection` and executes the stored SQL string via `Database/Execute`. Returns the success status.

**~SqlTransaction**: Destructor that cleans up the internal queue of `SqlOperation` pointers by deleting each element.

**Execute#6** (SqlTransaction): Executes a batch of operations within a transaction. Begins a transaction, iterates through the queue executing each `SqlOperation`, and rolls back if any fail. Commits if all succeed. Uses `Lock/Lock` for connection safety.

**SqlPlainRequest** (ctor): Constructor that duplicates the input SQL string using `mangos_strdup` to ensure lifetime independence from the caller.

**~SqlPlainRequest**: Destructor that frees the duplicated SQL string buffer.

**SqlPreparedRequest** (ctor): Constructor that stores the prepared statement index and takes ownership of the `SqlStmtParameters` pointer.

**~SqlPreparedRequest**: Destructor that deletes the owned `SqlStmtParameters` object.

**Execute#2** (SqlPreparedRequest): Acquires a lock on the `SqlConnection` and executes the prepared statement with its parameters via `Database/ExecuteStmt`. Returns the success status.

**Execute#3** (SqlQuery): Executes the stored SQL query on the provided connection. Moves the resulting `QueryResult` into the `IQueryCallback` via `SetResult` and adds the callback to the `SqlResultQueue` for synchronization. Uses `Lock/Lock` for connection safety.

**Update** (SqlResultQueue): Processes pending callbacks. Dispatches thread-safe callbacks to a `ThreadPool` and executes thread-unsafe callbacks synchronously. Enforces a timeout on unsafe query processing and logs warnings if the count exceeds 1000. Waits for thread pool jobs to complete.

**SqlResultQueue** (ctor): Initializes the queue, sets `numUnsafeQueries` to 0, and starts a `ThreadPool` with 6 threads for handling thread-safe callbacks.

**~SqlResultQueue**: Destructor for the result queue.

**CancelAll** (SqlResultQueue): Drains the queue, sets results to `nullptr` for each callback, executes them to allow cleanup, and deletes the callbacks. Used during server shutdown.

**Execute#4** (SqlQueryHolder): Defers execution by creating a `SqlQueryHolderEx` object and adding it to the `Database/AddToSerialDelayQueue`. Returns true on success.

**SetQuery** (SqlQueryHolder): Stores a SQL string at the specified index. Validates bounds and checks for existing entries. Logs errors on failure.

**SetPQuery** (SqlQueryHolder): Formats a SQL string using `vsnprintf` and stores it at the specified index via `SetQuery`. Validates format string and truncation. Logs errors on failure.

**TakeResult** (SqlQueryHolder): Retrieves and removes the `QueryResult` from the specified index. Validates bounds and emptiness. Logs errors on failure. Transfers ownership to the caller.

**SetResult** (SqlQueryHolder): Stores a `QueryResult` into the specified index. No validation is performed; assumes the index is valid.

**DeleteAllResults** (SqlQueryHolder): Iterates through all query slots and resets the `QueryResult` pointers, freeing associated memory.

**SetSize** (SqlQueryHolder): Resizes the internal query vector to the specified size, pre-allocating memory for efficiency.

**Execute#5** (SqlQueryHolderEx): Executes all queries stored in the referenced `SqlQueryHolder`. For each non-empty query, it executes the SQL on the connection, stores the result in the holder via `SetResult`, and finally adds the callback to the `SqlResultQueue`. Uses `Lock/Lock` for connection safety.

---

<!-- machine-true, projected from graph.json -->

## Map — SqlOperations

*Source:* SqlOperations.cpp, SqlOperations.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Execute | method | Database/Execute#3, Lock/Lock#5 | — | — |
| ~SqlTransaction | dtor | — | — | — |
| Execute#6 | method | Database/BeginTransaction#2, Database/CommitTransaction#2, Database/RollbackTransaction#2, Lock/Lock#5, SqlOperation/Execute | Database/CommitTransactionDirect | — |
| SqlPlainRequest | ctor | — | Database/Execute | — |
| ~SqlPlainRequest | dtor | — | — | — |
| SqlPreparedRequest | ctor | — | Database/ExecuteStmt | — |
| ~SqlPreparedRequest | dtor | — | — | — |
| Execute#2 | method | Database/ExecuteStmt#2, Lock/Lock#5 | — | — |
| Execute#3 | method | Database/Query#2, IQueryCallback/SetResult, Lock/Lock#5 | — | — |
| Update | method | IQueryCallback/Execute, IQueryCallback/IsThreadSafe, Log.Main/Out, shared_Util/getMSTime, ThreadPool/processWorkload, WorldTimer/getMSTimeDiffToNow | Database/ProcessResultQueue | — |
| SqlResultQueue | ctor | ThreadPool/ThreadPool | Database/Initialize | — |
| ~SqlResultQueue | dtor | — | — | — |
| CancelAll | method | IQueryCallback/Execute, IQueryCallback/SetResult | Database/StopServer | — |
| Execute#4 | method | Database/AddToSerialDelayQueue, SqlQueryHolderEx/SqlQueryHolderEx | — | — |
| SetQuery | method | Log.Main/Out | — | — |
| SetPQuery | method | Log.Main/Out | AsyncCommandHandlers/HandleDataAfterPlayerLookup, ChatHandler.LookupCommands/LookupPlayerSearchCommand, World/BanAccount, WorldSession.CharacterHandler/Initialize | — |
| TakeResult | method | Log.Main/Out | AsyncCommandHandlers/HandleDelayedMoneyQuery, AsyncCommandHandlers/operator()#2, Player.Main/LoadFromDB, World/HandleAccountSelectResult, WorldSession.CharacterHandler/HandlePlayerLogin | — |
| SetResult | method | — | — | — |
| DeleteAllResults | method | — | AsyncCommandHandlers/HandleDelayedMoneyQuery, AsyncCommandHandlers/operator()#2, World/HandleAccountSelectResult, WorldSession.CharacterHandler/~LoginQueryHolder | — |
| SetSize | method | — | AsyncCommandHandlers/HandleDataAfterPlayerLookup, ChatHandler.LookupCommands/LookupPlayerSearchCommand, World/BanAccount, WorldSession.CharacterHandler/Initialize | — |
| Execute#5 | method | Database/Query#2, Lock/Lock#5 | — | — |
