<!-- provenance: verbose -->
# SqlOperation

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# SqlOperations

`SqlOperations.h` defines the abstract base class `SqlOperation` and its immediate interface for asynchronous database operations within the MaNGOS framework. This unit establishes the contract for queuing SQL work items—such as plain requests, prepared statements, transactions, and queries—to be executed by a dedicated database thread pool (`SqlDelayThread`). By decoupling database I/O from game logic threads, it prevents latency spikes in the main server loop.

The core abstraction is `SqlOperation`, which carries a `serialId` for ordering and defines a pure virtual `Execute(SqlConnection* conn)` method. Derived classes (defined in this header but implemented elsewhere or in companion files) handle specific SQL types. The unit also declares helper structures like `SqlResultQueue` for managing asynchronous query callbacks and `SqlQueryHolder` for batching multiple queries.

## Architecture and Class Hierarchy

### Base Abstraction: `SqlOperation`
The root of the hierarchy. It stores a `serialId` and defines the execution contract.
*   **Constructors:** Two constructors exist: one accepting a `uint32` ID, and a default constructor initializing ID to 0.
*   **Lifecycle:** `OnRemove()` is a virtual hook for cleanup, defaulting to `delete this`. This implies the queueing system owns the object and signals removal via this method.
*   **Execution:** `Execute` is pure virtual, requiring derived classes to implement the actual database interaction using a provided `SqlConnection`.

### Derived Operation Types
While their implementations reside in other units, the following classes derive from `SqlOperation` and are declared here:
*   **`SqlPlainRequest`:** Encapsulates a static SQL string, duplicating it via `mangos_strdup` for async safety.
*   **`SqlPreparedRequest`:** Encapsulates a prepared statement by index and a pointer to `SqlStmtParameters`, taking ownership of the parameters.
*   **`SqlTransaction`:** Groups multiple `SqlOperation` pointers. It manages their lifecycle, deleting them in its destructor.
*   **`SqlQuery`:** Represents a single asynchronous query with a callback. It duplicates the SQL string and holds references to the callback and result queue.
*   **`SqlQueryHolderEx`:** An adapter that wraps a `SqlQueryHolder` (a batch query container) to allow it to be queued as an `SqlOperation`.

### Result Management: `SqlResultQueue`
Manages pending asynchronous queries and their callbacks. It inherits from `LockedQueue` for thread safety and maintains a `ThreadPool` (`m_callbackThreads`) for dispatching callbacks, offloading processing from the database thread.

## Cross-Unit Boundaries

*   **SqlDelayThread/ProcessRequests:** Calls `SqlOperation::Execute` to process queued operations.
*   **Database/AddToSerialDelayQueue, Database/CommitTransaction, Database/GetTransactionSerialId:** Call `GetSerialId` to retrieve the operation's ID for ordering and transaction tracking.
*   **SqlOperations/Execute#6:** Refers to the implementations of `Execute` in derived classes (located in `SqlOperations.cpp`), which perform the actual database I/O.

## Data Model

This unit does not interact directly with database tables. It operates on raw SQL strings and connections. Table schemas are irrelevant to this unit's internal behavior.

## Notable Implementation Details

1.  **String Duplication:** `SqlPlainRequest` and `SqlQuery` manually duplicate SQL strings via `mangos_strdup`. This is critical for async safety, preventing use-after-free if the caller's buffer is destroyed before execution.
2.  **Manual Memory Management:** The code uses raw pointers and manual `delete` calls. `SqlTransaction` must carefully delete its child operations in its destructor. `SqlPreparedRequest` deletes its parameter object.
3.  **Callback Ownership:** `SqlQuery` does not delete its `m_callback`. The caller or `SqlResultQueue` must manage the callback's lifetime to avoid dangling pointers.
4.  **Adapter Pattern:** `SqlQueryHolderEx` exists solely to bridge the gap between `SqlQueryHolder` (non-operation) and the `SqlOperation` queueing system.

## Member Reference

**SqlOperation#2**
Constructor initializing `serialId` with a provided `uint32`.

**SqlOperation**
Default constructor initializing `serialId` to 0.

**GetSerialId**
Returns the `serialId`. Called by `Database` units for ordering.

**OnRemove**
Virtual cleanup method, defaulting to `delete this`.

**Execute**
Pure virtual method for database execution.

**~SqlOperation**
Virtual destructor.

---

<!-- machine-true, projected from graph.json -->

## Map — SqlOperation

*Source:* SqlOperations.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| SqlOperation#2 | ctor | — | — | — |
| SqlOperation | ctor | — | — | — |
| GetSerialId | method | — | Database/AddToSerialDelayQueue, Database/CommitTransaction, Database/GetTransactionSerialId | — |
| OnRemove | method | — | — | — |
| Execute | decl | — | SqlDelayThread/ProcessRequests, SqlOperations/Execute#6 | — |
| ~SqlOperation | dtor | — | — | — |
