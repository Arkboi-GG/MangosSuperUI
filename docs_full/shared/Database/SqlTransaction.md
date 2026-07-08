<!-- provenance: verbose -->
# SqlTransaction

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`SqlTransaction` is a composite `SqlOperation` that groups multiple SQL commands into a single atomic unit. It inherits from `SqlOperation` to integrate with the asynchronous database thread pool. Its primary role is to ensure that a sequence of operations—added via `DelayExecute`—execute together within a single database transaction context, guaranteeing atomicity: either all contained operations succeed and commit, or the group fails and rolls back.

## Member-by-Member Behavior

### Construction and Lifecycle

**`SqlTransaction`**
The constructor initializes the base `SqlOperation` with a unique `serialId` for lifecycle tracking. The internal `m_queue` vector is initialized empty, ready to accept child operations.

**`~SqlTransaction`**
The destructor iterates through `m_queue` and deletes each `SqlOperation*` pointer, freeing memory associated with individual statements upon transaction completion.

### Queueing Operations

**`DelayExecute`**
Adds a `SqlOperation*` to the internal `m_queue`. `SqlTransaction` takes ownership of the pointer; callers must not delete the object after passing it. This allows fluent construction of transactions by appending operations before submission.

### Execution

The `Execute` method (inherited from `SqlOperation`) performs the following:
1. Sends `BEGIN TRANSACTION` to the `SqlConnection`.
2. Iterates through `m_queue`, calling `Execute(conn)` on each child.
3. If any child returns `false`, it sends `ROLLBACK` and returns `false`.
4. If all children succeed, it sends `COMMIT` and returns `true`.

## Cross-Unit Boundaries

### Called By: `Database/BeginTransaction`

*   **Direction:** `Database` → `SqlTransaction`
*   **Data Crossing:** A `uint32` serial ID is passed to the constructor.
*   **Why:** To create transaction containers decoupled from connection pool management.

### Called By: `Database/Execute` and `Database/ExecuteStmt`

*   **Direction:** `Database` → `SqlTransaction` (via `SqlOperation` interface)
*   **Data Crossing:** The `SqlTransaction` object is enqueued into the database thread's work queue.
*   **Why:** To process transactions asynchronously alongside simple queries, preventing main-thread blocking.

### Calls Out: None

`SqlTransaction` does not directly call into other units. Its `Execute` method interacts with `SqlConnection` (passed as an argument), but this interaction is local to the database execution context.

## Data Model

`SqlTransaction` does not touch any database tables directly. It is a control-flow mechanism. The `SqlOperation` objects it contains (e.g., `SqlPlainRequest`) execute SQL statements interacting with various tables. `SqlTransaction` is agnostic to the content of its child operations and defines no specific table schema.

## Notable Implementation Details

1.  **Ownership Semantics:** `SqlTransaction` takes ownership of pointers passed to `DelayExecute`. The destructor manually deletes these pointers. Callers must use `new` for child operations; stack-allocated objects cannot be safely added unless the transaction is destroyed before the stack frame exits.
2.  **No Nested Transactions:** The implementation assumes flat nesting. Adding another `SqlTransaction` to a `SqlTransaction` is unsupported and likely causes `BEGIN`/`COMMIT` conflicts. Users should flatten operations.
3.  **Error Propagation:** `Execute` stops processing child operations upon the first failure. Remaining operations are not attempted, but rollback ensures consistency.
4.  **Thread Safety:** `SqlTransaction` is not thread-safe. It is constructed on the main thread, populated, and moved to the database thread for execution. No other thread should access it after handoff.

## Member Reference

**`SqlTransaction`**
Constructor that initializes the base `SqlOperation` with a given `serialId`. It prepares the internal `m_queue` vector to hold child SQL operations. This object is typically allocated by `Database::BeginTransaction()` and intended for asynchronous execution.

**`DelayExecute`**
Adds a `SqlOperation*` to the internal queue of the transaction. The transaction takes ownership of the pointer, meaning the caller must not delete the object afterward. This method enables the fluent construction of multi-statement transactions by allowing sequential addition of plain requests, prepared statements, or other operations before the transaction is submitted to the database thread.

---

<!-- machine-true, projected from graph.json -->

## Map — SqlTransaction

*Source:* SqlOperations.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| SqlTransaction | ctor | — | Database/BeginTransaction | — |
| DelayExecute | method | — | Database/Execute, Database/ExecuteStmt | — |
