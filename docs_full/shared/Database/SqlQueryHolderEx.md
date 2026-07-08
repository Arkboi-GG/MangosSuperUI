# SqlQueryHolderEx

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# SqlQueryHolderEx

**SqlQueryHolderEx** is a thin wrapper within the `SqlOperations` framework that enables asynchronous execution of a batch of SQL queries (`SqlQueryHolder`) on the database delay thread. It bridges the high-level query grouping logic and the low-level connection pool execution mechanism by conforming to the `SqlOperation` interface.

### Purpose & Responsibilities

The primary responsibility of `SqlQueryHolderEx` is to encapsulate a `SqlQueryHolder` instance, along with its associated callback and result queue, into an object that can be queued and executed by the `SqlDelayThread`. Key responsibilities include:

1.  **Ownership Delegation**: It holds raw pointers to a `SqlQueryHolder`, a `MaNGOS::IQueryCallback`, and a `SqlResultQueue`. It does not own these objects; it merely references them during execution.
2.  **Execution Coordination**: Its `Execute` method triggers the actual execution of the underlying `SqlQueryHolder` against a specific `SqlConnection`.
3.  **Interface Conformance**: By inheriting from `SqlOperation`, it integrates into the asynchronous database processing pipeline, allowing it to be serialized and executed by the thread pool managing database connections.

### Member-by-Member Behavior

#### Constructor: `SqlQueryHolderEx`
*   **Signature**: `SqlQueryHolderEx(SqlQueryHolder* holder, MaNGOS::IQueryCallback* callback, SqlResultQueue* queue, uint32 id)`
*   **Behavior**: Initializes the base `SqlOperation` with a unique serial ID (`id`). It stores raw pointers to the provided `SqlQueryHolder` (`holder`), the callback object (`callback`), and the result queue (`queue`).
*   **Note**: The constructor assumes the lifetime of the pointed-to objects exceeds the lifetime of this `SqlQueryHolderEx` instance or is managed externally. No deep copying occurs.

### Cross-Unit Boundaries

*   **Called by `SqlOperations/Execute#4`**:
    *   **Context**: The `SqlOperations` unit creates instances of `SqlQueryHolderEx` to enqueue batch queries.
    *   **Direction**: Outbound call from `SqlOperations` to construct `SqlQueryHolderEx`.
    *   **Why**: To wrap a prepared batch of queries (`SqlQueryHolder`) into an executable operation that can be handed off to the asynchronous database thread pool.

*   **Calls into `SqlQueryHolder` (via `m_holder->Execute`)**:
    *   **Context**: Inside `SqlQueryHolderEx::Execute`.
    *   **Direction**: Inbound call from `SqlQueryHolderEx` to `SqlQueryHolder`.
    *   **Why**: To perform the actual SQL execution. `SqlQueryHolderEx` is merely a carrier; `SqlQueryHolder` contains the logic for iterating over multiple queries and handling results.

*   **Calls into `MaNGOS::IQueryCallback` (via `m_callback`)**:
    *   **Context**: Passed through to `SqlQueryHolder::Execute`.
    *   **Direction**: Indirect call. `SqlQueryHolderEx` passes the pointer to `SqlQueryHolder`, which then invokes methods on the callback when results are ready.
    *   **Why**: To notify the caller when the batch of queries has completed, providing access to the results.

*   **Calls into `SqlResultQueue` (via `m_queue`)**:
    *   **Context**: Passed through to `SqlQueryHolder::Execute`.
    *   **Direction**: Indirect call. Used by `SqlQueryHolder` to manage the lifecycle and synchronization of the query results.
    *   **Why**: To ensure thread-safe handling of query results between the database thread and the main game server threads.

### Data Model

This unit does not directly interact with database tables. It operates at the infrastructure level, managing the execution flow of SQL statements defined elsewhere. The actual table interactions occur within the `SqlQueryHolder` and the individual SQL strings it contains, which are not part of this unit's direct scope.

### Notable Implementation Details

1.  **Raw Pointer Usage**: `SqlQueryHolderEx` uses raw pointers (`SqlQueryHolder*`, `MaNGOS::IQueryCallback*`, `SqlResultQueue*`) rather than smart pointers or references. This implies strict lifetime management requirements: the objects pointed to must remain valid for the duration of the `SqlQueryHolderEx`'s existence and execution. If the `SqlQueryHolder` is deleted before `Execute` is called, this will lead to undefined behavior/crashes.
2.  **No Ownership**: Unlike some other `SqlOperation` subclasses (e.g., `SqlPlainRequest` which manages its own string memory), `SqlQueryHolderEx` does not take ownership of its components. It is a transient wrapper.
3.  **Delegation Pattern**: The `Execute` method is a pure delegation. It adds no logic of its own beyond forwarding the arguments. This keeps the wrapper lightweight but makes debugging dependent on understanding the `SqlQueryHolder` implementation.
4.  **Thread Safety**: While `SqlQueryHolderEx` itself has no internal state that requires locking, its usage is inherently tied to the thread-safety mechanisms of `SqlResultQueue` and the database connection pool. The `SqlResultQueue` is designed to handle cross-thread communication, ensuring that callbacks are invoked safely.

## Member Reference

**SqlQueryHolderEx**
Constructor that initializes the wrapper with a pointer to a `SqlQueryHolder`, a callback object, a result queue, and a serial ID. It sets up the necessary references for the `Execute` method to delegate work correctly. Assumes external lifetime management of all pointed-to objects.

---

<!-- machine-true, projected from graph.json -->

## Map — SqlQueryHolderEx

*Source:* SqlOperations.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| SqlQueryHolderEx | ctor | — | SqlOperations/Execute#4 | — |
