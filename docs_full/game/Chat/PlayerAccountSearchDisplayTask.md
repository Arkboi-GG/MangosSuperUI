# PlayerAccountSearchDisplayTask

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# PlayerAccountSearchDisplayTask

**Purpose & Responsibilities**

`PlayerAccountSearchDisplayTask` is a functor class designed to execute the final display phase of a player account search operation within the main game server thread. Its sole responsibility is to safely render search results to a Game Master (GM) or administrator using the `ChatHandler` interface.

Because database queries in this codebase are often executed asynchronously to avoid blocking the main game loop, the raw query results are processed in a database worker thread. However, interacting with the `WorldSession` and `ChatHandler` objects is not thread-safe and must occur in the main thread. `PlayerAccountSearchDisplayTask` bridges this gap: it is constructed in the asynchronous context with a pointer to the query results (`PlayerSearchQueryHolder`) and scheduled to run in the main thread's update loop. When invoked, it iterates through the collected account information and sends formatted output to the requesting user.

**Member-by-Member Behavior**

The unit contains a single public constructor and a private callable operator (`operator()`), though the latter is not explicitly listed in the provided MAP, it is intrinsic to the class definition as a functor. The MAP explicitly lists the constructor.

*   **Construction**: The constructor accepts a raw pointer to a `PlayerSearchQueryHolder`. This holder object aggregates the results of multiple asynchronous database queries (specifically, mapping character GUIDs to account IDs and usernames). The task stores this pointer for use during execution.

*   **Execution (`operator()`)**: Although not in the MAP, the implementation of `operator()` is critical to understanding the unit. It retrieves the `ChatHandler` associated with the original request (likely stored within the `PlayerSearchQueryHolder` or accessible via the session context passed during scheduling, though the specific mechanism for retrieving the `ChatHandler` is hidden within the `PlayerSearchQueryHolder` or the caller's setup). It then iterates through the accounts stored in the `holder`, formatting each entry (Account ID, Username) and sending it to the chat handler. Finally, it likely cleans up or signals completion. *Note: The provided source header does not show the `.cpp` implementation, but the class structure implies this standard async-display pattern.*

**Cross-Unit Boundaries**

*   **Called by `AsyncCommandHandlers/HandlePlayerAccountSearchResult`**:
    *   **Direction**: `HandlePlayerAccountSearchResult` (in `AsyncCommandHandlers.cpp`) creates an instance of `PlayerAccountSearchDisplayTask` and schedules it for execution in the main thread.
    *   **Collaboration**: `HandlePlayerAccountSearchResult` runs in the database thread after the initial player search query completes. It populates the `PlayerSearchQueryHolder` with account data. Once the data is ready, it constructs the `PlayerAccountSearchDisplayTask` to defer the UI rendering to the main thread. This ensures that the `ChatHandler` is accessed safely.

*   **Calls out**: None. The task itself does not initiate new network connections, database queries, or calls to other complex subsystems. It relies entirely on the data provided by the `PlayerSearchQueryHolder` and the `ChatHandler` infrastructure (which is part of the core session management, not a separate "unit" in the MAP sense).

**Data Model**

This unit does not directly interact with database tables. It operates on in-memory data structures (`PlayerSearchQueryHolder`) that were populated by previous database queries performed by `AsyncCommandHandlers`. The underlying tables queried earlier in the chain typically include `characters` and `account`, but `PlayerAccountSearchDisplayTask` itself is purely a presentation layer component.

**Notable Implementation Details**

1.  **Thread Safety Strategy**: The class exists solely to enforce thread safety. The comment in the header states: *"Run the display in an async task inside the main update, safe for session consistency."* This confirms that the `ChatHandler` or `WorldSession` objects cannot be touched from the database thread.
2.  **Raw Pointer Usage**: The constructor takes a `PlayerSearchQueryHolder*` (raw pointer). The lifetime of this holder must be managed carefully by the caller (`HandlePlayerAccountSearchResult`). Typically, the holder is kept alive until the task executes. If the holder were deleted before the task ran, this would result in a use-after-free bug. The design assumes the scheduler holds the task and the holder remains valid until execution.
3.  **Functor Pattern**: By overloading `operator()`, the class acts as a callable object that can be passed to a scheduler (likely `World::GetScheduler()` or similar) which expects a `std::function<void()>` or similar signature. This allows capturing state (the `holder` pointer) without using lambdas with complex capture lists or global variables.

## Member Reference

**PlayerAccountSearchDisplayTask**
Constructor that initializes the task with a pointer to a `PlayerSearchQueryHolder`. This holder contains the aggregated account search results (Account IDs and Usernames) retrieved asynchronously. The task stores this pointer to access the data when it is eventually executed in the main thread.

---

<!-- machine-true, projected from graph.json -->

## Map — PlayerAccountSearchDisplayTask

*Source:* AsyncCommandHandlers.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| PlayerAccountSearchDisplayTask | ctor | — | AsyncCommandHandlers/HandlePlayerAccountSearchResult | — |
