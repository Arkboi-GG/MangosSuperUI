# PlayerCharacterLookupDisplayTask

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# PlayerCharacterLookupDisplayTask

**Purpose & Responsibilities**

`PlayerCharacterLookupDisplayTask` is a functor class designed to execute the final display phase of an asynchronous character search operation within the main game loop thread. Its primary responsibility is to safely consume a database query result (`QueryResult`) containing character information and format it for presentation to a Game Master (GM) or administrator via the chat interface.

This class exists to solve a threading safety problem inherent in the `AsyncCommandHandlers` subsystem. Database queries for character lookups are executed asynchronously to avoid blocking the main server thread. However, the resulting data cannot be displayed immediately in the database callback because the `ChatHandler` and associated session objects are not thread-safe. By encapsulating the result and display logic in this task, the system can defer the actual output generation until the next main update cycle, ensuring that all interactions with the session and chat system occur on the correct thread.

The class holds a `std::shared_ptr<std::unique_ptr<QueryResult>>` to manage the lifetime of the query result. This specific double-pointer structure is a workaround for C++ type limitations where `std::unique_ptr` cannot be moved into a `std::function` context required by the task scheduler. The task ensures that the raw pointer is consumed and reset during execution to prevent memory leaks.

## Member-by-Member Behavior

### Construction
The constructor `PlayerCharacterLookupDisplayTask` initializes the task with the necessary data to perform the display:
1.  **`result`**: A `std::unique_ptr<QueryResult>` containing the rows returned from the database character search. This is wrapped in a `std::shared_ptr` to allow copying into the task object while maintaining ownership semantics.
2.  **`accountId`**: The ID of the account performing the search, likely used for logging or permission checks during display.
3.  **`limit`**: The maximum number of results to display, preventing excessive output in the chat window.

### Execution (`operator()`)
Although the implementation of `operator()` is not provided in the source snippet, its behavior is inferred from the class design and similar classes (`PlayerAccountSearchDisplayTask`, `AccountSearchDisplayTask`):
1.  It retrieves the raw `QueryResult` from the `unsafeResult` shared pointer.
2.  It iterates through the result set, respecting the `limit`.
3.  It formats the character data (name, level, race, etc.) into a string suitable for chat output.
4.  It sends this formatted string to the appropriate `ChatHandler` or session.
5.  It resets the `unsafeResult` to `nullptr` to release the memory held by the `unique_ptr`, ensuring no dangling pointers remain after the task completes.

## Cross-Unit Boundaries

### Called By: `AsyncCommandHandlers/HandlePlayerCharacterLookupResult`
The `PlayerCharacterLookupDisplayTask` is instantiated and scheduled by `HandlePlayerCharacterLookupResult` (located in `AsyncCommandHandlers`). This handler receives the raw database result from the asynchronous query. Instead of displaying the data immediately (which would be unsafe), it constructs a `PlayerCharacterLookupDisplayTask` object and pushes it onto a task queue or invokes it via a mechanism that ensures execution in the main thread. This boundary represents the transition from the asynchronous database thread context to the synchronous main game loop context.

### Calls Out: None
The MAP indicates no direct calls to other units. However, logically, the `operator()` implementation must interact with:
1.  **`ChatHandler`**: To send the formatted output to the user.
2.  **`WorldSession`**: Likely accessed via the `ChatHandler` or passed implicitly, to ensure the output is directed to the correct GM.
3.  **`QueryResult`**: To read the data fields.

These interactions are internal to the task's execution within the main thread and do not involve cross-thread or cross-unit dependencies beyond the standard session/chat infrastructure.

## Data Model

This unit does not directly interact with database tables. It consumes a pre-fetched `QueryResult` object. The underlying data originates from a character search query, likely involving the `characters` table, but the specific SQL schema is abstracted away by the `QueryResult` object passed into the constructor. No SQL statements are present in this unit's code.

## Notable Implementation Details

1.  **Double Pointer Workaround**: The use of `std::shared_ptr<std::unique_ptr<QueryResult>>` is a notable implementation detail. Standard `std::unique_ptr` is non-copyable and non-movable in certain contexts (like being stored in a `std::function` if the lambda captures by value in a way that requires copying). By wrapping it in a `shared_ptr`, the task object can be copied (e.g., when stored in a task queue) while still ensuring exclusive ownership of the underlying `QueryResult` until it is consumed. The comment in the code explicitly states: *"Somehow this class is not moveable when cased to a std::function<void()> ... so we wrap the result into a shared_ptr to still ensure memory 'safety'"*.

2.  **Memory Management Responsibility**: The task is responsible for cleaning up the `QueryResult`. The comment *"will be deleted and set to nullptr when operator()"* indicates that the `operator()` implementation must explicitly reset the `unsafeResult` to avoid memory leaks. Failure to do so would leave the `unique_ptr` dangling or leaked, depending on how the `shared_ptr` goes out of scope.

3.  **Thread Safety Context**: The class is designed to be executed in the main thread ("safe for session consistency"). This implies that the `operator()` must not perform any blocking I/O or database operations, as it runs in the critical path of the game loop. All heavy lifting (database access) is done before the task is created.

## Member Reference

**PlayerCharacterLookupDisplayTask**
Constructor that initializes the task with a `std::unique_ptr<QueryResult>` wrapped in a `std::shared_ptr`, an `accountId`, and a `limit`. It prepares the task for deferred execution in the main thread to safely display character search results.

---

<!-- machine-true, projected from graph.json -->

## Map — PlayerCharacterLookupDisplayTask

*Source:* AsyncCommandHandlers.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| PlayerCharacterLookupDisplayTask | ctor | — | AsyncCommandHandlers/HandlePlayerCharacterLookupResult | — |
