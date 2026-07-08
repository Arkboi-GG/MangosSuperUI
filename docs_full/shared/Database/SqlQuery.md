# SqlQuery

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`SqlQuery` is a lightweight wrapper within the `wowvmangos` database abstraction layer representing a **single asynchronous SQL query**. It inherits from `SqlOperation` to integrate into the background database worker thread system. Its primary responsibilities are:
1.  Safely storing a deep copy of a raw SQL string (`char const*`) to ensure lifetime safety across thread boundaries.
2.  Holding references to a `MaNGOS::IQueryCallback` and a `SqlResultQueue` to facilitate result delivery back to the main game thread.
3.  Executing the query against a provided `SqlConnection` when processed by the database worker.

It handles simple, single-statement queries. More complex batches utilize `SqlQueryHolder` or `SqlQueryHolderEx`.

## Member-by-Member Behavior

### Construction and Destruction

**`SqlQuery` (Constructor)**
Initializes the object with three arguments:
1.  `sql`: The SQL command string.
2.  `callback`: A pointer to a `MaNGOS::IQueryCallback` implementation that receives the result.
3.  `queue`: A pointer to the `SqlResultQueue` for thread-safe result synchronization.

The constructor duplicates the SQL string using `mangos_strdup(sql)` into the private member `m_sql`, ensuring the query remains valid even if the caller's original string goes out of scope. It stores the callback and queue pointers directly. It implicitly invokes the default `SqlOperation` constructor, initializing the inherited `serialId` to 0.

**`~SqlQuery` (Destructor)**
Frees the memory allocated for the SQL string by casting `m_sql` to `char*` and deleting it with `delete []`. It does not delete `m_callback` or `m_queue`, as `SqlQuery` does not own these objects; their lifecycles are managed externally.

### Execution

**`Execute`**
Declared in the header, implemented in the corresponding `.cpp` file. It accepts a `SqlConnection*` and performs the database I/O for the stored SQL string. Upon completion, it delivers the resulting `QueryResult` to the main thread via the `m_queue` and `m_callback` mechanism.

## Cross-Unit Boundaries

*   **`SqlOperation` (Base Class):** `SqlQuery` inherits from `SqlOperation`, implementing the pure virtual `Execute(SqlConnection*)` method. It relies on the base class's default `OnRemove()` behavior (self-deletion) and inherits `GetSerialId()`.
*   **`SqlConnection` (Called by `Execute`):** Used during execution to perform the actual database query.
*   **`MaNGOS::IQueryCallback` (Stored Reference):** An interface pointer stored in `m_callback`. `SqlQuery` does not define this interface but uses it to notify the caller of completion.
*   **`SqlResultQueue` (Stored Reference):** Stored in `m_queue`. Used to enqueue results for safe processing by the main thread, acting as the synchronization bridge between the background database thread and the main game loop.
*   **`mangos_strdup` (Called by Constructor):** A utility function (likely in `Common.h`) used to allocate and copy the SQL string.

## Data Model

`SqlQuery` does not interact with specific database tables. It is a generic transport mechanism for arbitrary SQL strings. The tables accessed depend entirely on the content of the `sql` string provided by the caller.

## Notable Implementation Details

1.  **String Lifetime Management:** `SqlQuery` assumes ownership of the heap-allocated string created by `mangos_strdup`. The destructor manually frees this memory with `delete []`. If `mangos_strdup` returns `NULL` (allocation failure), the behavior is undefined in the header, potentially leading to crashes if `Execute` dereferences `m_sql`.
2.  **No Ownership of Dependencies:** `SqlQuery` does not manage the lifetime of `m_callback` or `m_queue`. Callers must ensure these objects remain valid until the query completes and the destructor runs.
3.  **Fixed Serial ID:** Unlike `SqlTransaction` or `SqlQueryHolderEx`, `SqlQuery` does not accept a `serialId` in its constructor. It always initializes the inherited `serialId` to 0 via the implicit call to `SqlOperation()`. This limits traceability for individual queries compared to batched operations.
4.  **Thread Safety:** The class is not thread-safe after construction. It is designed to be constructed on the main thread, transferred to a background thread for execution, and destroyed. Thread safety for result delivery is handled by the `SqlResultQueue`.

## Member Reference

**`SqlQuery`**
Constructor that initializes the `SqlQuery` object. It duplicates the provided SQL string using `mangos_strdup` to ensure lifetime safety, and stores pointers to the callback and result queue. It implicitly calls the default constructor of `SqlOperation`, setting `serialId` to 0.

**`~SqlQuery`**
Destructor that frees the memory allocated for the SQL string by casting `m_sql` to `char*` and deleting it with `delete []`. It does not delete the callback or queue pointers, as it does not own them.

---

<!-- machine-true, projected from graph.json -->

## Map — SqlQuery

*Source:* SqlOperations.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| SqlQuery | ctor | — | — | — |
| ~SqlQuery | dtor | — | — | — |
