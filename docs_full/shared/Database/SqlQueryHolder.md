# SqlQueryHolder

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# SqlQueryHolder

**Purpose & Responsibilities**

`SqlQueryHolder` is a container class within the `wowvmangos` database abstraction layer that groups multiple asynchronous SQL queries into a single logical unit. It allows the server to prepare several SQL statements ahead of time, execute them via a background thread pool, and retrieve their results in a synchronized manner. The class manages the lifecycle of `QueryResult` objects, holding ownership until results are explicitly transferred to the caller. It is agnostic to specific database schemas, operating on arbitrary SQL strings provided by its callers.

## Member-by-Member Behavior

### Construction and Destruction

*   **`SqlQueryHolder#2` (Constructor with ID)**: Initializes the holder with a specific `serialId`. This identifier tracks the operation within the broader database thread system. It is instantiated by `WorldSession.CharacterHandler` and `LoginQueryHolder`, indicating its use in session-specific or login-critical workflows where tracing the origin of the query batch is necessary.
*   **`SqlQueryHolder` (Default Constructor)**: Initializes the holder with a `serialId` of 0. This variant is used by `AsyncCommandHandlers/HandleDataAfterPlayerLookup`, suggesting its role in general asynchronous command processing where a unique serial ID is managed externally or is less critical.
*   **`~SqlQueryHolder`**: The destructor is defaulted. Since `m_queries` is a vector of `std::unique_ptr<QueryResult>`, destruction automatically cleans up all owned `QueryResult` objects, preventing memory leaks for any results not explicitly retrieved via `TakeResult`.

### Query Management and Execution Support

*   **`GetSize`**: Returns the current number of queries stored in the `m_queries` vector. Called by `AsyncCommandHandlers/operator()#2` to iterate through results or verify completion status.
*   **`GetSerialId`**: Returns the `serialId` assigned during construction. While listed in the map as having no external callers, this method provides the public interface for identifying the specific batch of queries, useful for logging or debugging.

## Cross-Unit Boundaries

### Called By

*   **`WorldSession.CharacterHandler`**: Uses `SqlQueryHolder#2` to batch database queries for character-related actions (e.g., loading/saving character data). The ID-based constructor ties these batches to a specific session context.
*   **`LoginQueryHolder`**: Uses `SqlQueryHolder#2` to manage queries during player login, a critical path requiring efficient execution of multiple lookups (account validation, character list retrieval).
*   **`AsyncCommandHandlers/HandleDataAfterPlayerLookup`**: Uses the default `SqlQueryHolder` constructor to group further asynchronous data fetching after an initial player lookup.
*   **`AsyncCommandHandlers/operator()#2`**: Calls `GetSize` to determine the number of available results, likely as part of the callback mechanism processing completed asynchronous executions.

### Calls Out

*   The `SqlQueryHolder` class does not directly call into other units in the provided MAP. Its interaction is primarily through being called by other units or through internal use of standard library containers and the `QueryResult` type. Actual database execution is delegated via the `Execute` method (not in MAP) to the `Database` and `SqlResultQueue` systems.

## Data Model

`SqlQueryHolder` does not interact with specific database tables. It operates on arbitrary SQL queries provided by callers via `SetQuery` or `SetPQuery` (methods not in MAP). These queries may target any table in the `wowvmangos` database, but the holder itself is schema-agnostic, storing only SQL text and resulting `QueryResult` objects.

## Notable Implementation Details

1.  **Ownership Transfer**: Results are stored as `std::unique_ptr<QueryResult>`. The `TakeResult` method (not in MAP) transfers ownership to the caller. If not taken, the destructor cleans up the results.
2.  **Thread Safety**: The class lacks internal mutexes. Thread safety is managed by surrounding infrastructure (`SqlResultQueue`, `LockedQueue`). `SqlQueryHolder` is typically created on the main thread, executed asynchronously, and accessed again on the main thread via callbacks after completion.
3.  **Serial ID Tracing**: The `serialId` allows the database subsystem to correlate batch completion with the initiating request, vital for complex asynchronous workflows like login sequences.

## Member Reference

*   **SqlQueryHolder#2**: Constructor initializing `SqlQueryHolder` with a specific `serialId` (parameter `id`). Used by `WorldSession.CharacterHandler` and `LoginQueryHolder` for traceable, session-specific query batches.
*   **SqlQueryHolder**: Default constructor initializing `SqlQueryHolder` with `serialId` 0. Used by `AsyncCommandHandlers/HandleDataAfterPlayerLookup` for general asynchronous batching.
*   **~SqlQueryHolder**: Destructor cleaning up the `m_queries` vector, automatically deleting any `QueryResult` objects still owned by the holder.
*   **GetSize**: Method returning the number of queries/results in the holder (`m_queries.size()`). Called by `AsyncCommandHandlers/operator()#2`.
*   **GetSerialId**: Method returning the `serialId` assigned during construction, allowing external identification of the query batch.

---

<!-- machine-true, projected from graph.json -->

## Map — SqlQueryHolder

*Source:* SqlOperations.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| SqlQueryHolder#2 | ctor | — | WorldSession.CharacterHandler/LoginQueryHolder | — |
| SqlQueryHolder | ctor | — | AsyncCommandHandlers/HandleDataAfterPlayerLookup | — |
| ~SqlQueryHolder | dtor | — | — | — |
| GetSize | method | — | AsyncCommandHandlers/operator()#2 | — |
| GetSerialId | method | — | — | — |
