# AccountSearchDisplayTask

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AccountSearchDisplayTask

## Purpose & Responsibilities

`AccountSearchDisplayTask` is a functor class that encapsulates the final display step of an asynchronous account search. It bridges the gap between an asynchronous database callback (which runs on a worker thread) and the main game loop thread, where session-safe operations like sending chat responses must occur. By wrapping the `QueryResult` and relevant parameters, it allows the result processing to be deferred until the main thread can safely execute it.

## Member-by-Member Behavior

### Construction

**`AccountSearchDisplayTask`**
The constructor accepts a `std::unique_ptr<QueryResult>` containing the raw database rows, a `uint32 accountId`, and a `uint32 limit`. It moves the `QueryResult` into a `std::shared_ptr<std::unique_ptr<QueryResult>>` member named `unsafeResult`. This double-wrapping is a specific workaround to allow the task object to be copied or moved into a `std::function`-based task queue, as `std::unique_ptr` is non-copyable and non-movable in certain scheduler contexts. The `accountId` and `limit` are stored as private members for use during execution.

### Execution

**`operator()`**
When invoked by the main thread's task scheduler, this method processes the stored `QueryResult`. It extracts the data and delegates the formatting and transmission of the account list to `AccountSearchHandler::ShowAccountListHelper` (defined in `AsyncCommandHandlers`). This ensures that the response is sent to the requesting Game Master's session in a thread-safe manner. After processing, the `unsafeResult` is reset to prevent memory leaks.

## Cross-Unit Boundaries

### Called By: `AsyncCommandHandlers/HandleAccountLookupResult`
*   **Direction**: `AsyncCommandHandlers` creates and schedules `AccountSearchDisplayTask`.
*   **Collaboration**: `HandleAccountLookupResult` (in `AsyncCommandHandlers.cpp`) receives the raw database result from an async query. It constructs an `AccountSearchDisplayTask` instance, passing the result, account ID, and limit. This task is then scheduled to run in the main thread. This separation ensures that the potentially heavy or blocking operation of formatting and sending chat messages occurs in the main thread, maintaining thread safety for session interactions.

### Calls Out: `AsyncCommandHandlers/ShowAccountListHelper` (via `AccountSearchHandler`)
*   **Direction**: `AccountSearchDisplayTask` calls into `AccountSearchHandler` (defined in the same header, implemented in `AsyncCommandHandlers.cpp`).
*   **Collaboration**: Inside `operator()`, the task invokes `AccountSearchHandler::ShowAccountListHelper`. It passes the `QueryResult` (extracted from `unsafeResult`), the `ChatHandler` (contextual, likely captured or retrieved via session), `count`, `limit`, and `title` flags. This helper function iterates over the result set, formats each account entry, and sends the output to the GM's chat window.

## Data Model

This unit does not directly interact with database tables. It operates on `QueryResult` objects that have already been fetched by previous stages of the asynchronous pipeline (specifically by `AccountSearchHandler::HandleAccountLookupResult`). The underlying data originates from the `account` table (and potentially linked character tables), but `AccountSearchDisplayTask` treats this data as an opaque result set to be formatted and displayed.

## Notable Implementation Details

1.  **Double Pointer Wrapping (`std::shared_ptr<std::unique_ptr<QueryResult>>`)**:
    The class uses a `std::shared_ptr` to hold a `std::unique_ptr<QueryResult>`. This is explicitly documented in the source comments as a workaround because `std::unique_ptr` is not movable into `std::function<void()>` in the context of the task scheduler used by the engine. The `shared_ptr` allows the task object to be copied/moved freely while maintaining exclusive ownership semantics for the underlying `QueryResult` via the inner `unique_ptr`.

2.  **Memory Safety Note**:
    The comment states that `unsafeResult` "will be deleted and set to nullptr when operator()". This implies that the `operator()` implementation is responsible for resetting the shared pointer or the unique pointer inside it after use, preventing memory leaks. The term "unsafe" in the variable name likely refers to the fact that the `QueryResult` was originally accessed in an async callback context, and this task brings it back to the main thread where it must be handled carefully to avoid race conditions with the database connection pool.

3.  **Thread Safety**:
    The class is designed to be executed in the main thread ("safe for session consistency"). This ensures that when `ShowAccountListHelper` sends packets to the client, it does so from the correct thread, avoiding crashes or undefined behavior associated with cross-thread session access.

## Member Reference

**AccountSearchDisplayTask**
Constructor that initializes the task with a database query result, account ID, and display limit. It wraps the `unique_ptr<QueryResult>` in a `shared_ptr` to facilitate movement into the task scheduler, storing the parameters for later execution by `operator()`.

---

<!-- machine-true, projected from graph.json -->

## Map — AccountSearchDisplayTask

*Source:* AsyncCommandHandlers.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AccountSearchDisplayTask | ctor | — | AsyncCommandHandlers/HandleAccountLookupResult | — |
