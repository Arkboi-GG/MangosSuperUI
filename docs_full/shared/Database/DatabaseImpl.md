<!-- provenance: degenerate, verbose -->
# DatabaseImpl

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# DatabaseImpl

**Purpose & Responsibilities**

`DatabaseImpl.h` provides the template definitions for the `Database` class’s asynchronous query interface. It allows the game server to submit SQL operations to a background database thread without blocking the main game loop. The unit offers three submission mechanisms:
1.  **`AsyncQuery`**: Submits a pre-formed SQL string.
2.  **`AsyncPQuery`**: Formats a SQL string from a `printf`-style format string and arguments before submission.
3.  **`DelayQueryHolder`**: Submits a `SqlQueryHolder` object for complex or multi-step query construction.

Each mechanism supports callbacks to either member functions or static functions, with optional parameters (up to three). Each also has an `Unsafe` variant that marks the callback as non-thread-safe, allowing it to execute directly on the database thread to reduce context-switching overhead.

## Member-by-Member Behavior

All members validate inputs using macros (`ASYNC_QUERY_BODY`, `ASYNC_PQUERY_BODY`, `ASYNC_DELAYHOLDER_BODY`) which check for null pointers and the existence of the result queue (`m_pResultQueue`). If validation fails, they return `false`.

### Direct Queries (`AsyncQuery` / `AsyncQueryUnsafe`)
These members construct a `MaNGOS::QueryCallback` (for member functions) or `MaNGOS::SQueryCallback` (for static functions) containing the target method/function, any extra parameters, and a null `QueryResult`. They wrap this callback in a `SqlQuery` object and enqueue it via `AddToDelayQueue`. The `Unsafe` variants explicitly set `cb->threadSafe = false` on the callback object before enqueuing.

### Formatted Queries (`AsyncPQuery` / `AsyncPQueryUnsafe`)
These members use the `ASYNC_PQUERY_BODY` macro to expand the format string into a local buffer `szQuery` using `vsnprintf`. If `vsnprintf` returns `-1` (truncation), the query is aborted, an error is logged via `sLog.Out`, and `false` is returned. On success, they delegate to the corresponding `AsyncQuery` or `AsyncQueryUnsafe` member.

### Query Holders (`DelayQueryHolder` / `DelayQueryHolderUnsafe`)
These members validate the `SqlQueryHolder*` and delegate execution to `holder->Execute()`. They pass the constructed callback, the current `Database` instance (`this`), and the result queue to the holder. The `Unsafe` variants mark the callback as non-thread-safe before passing it to the holder.

## Cross-Unit Boundaries

*   **Calls Out:**
    *   `Database::AddToDelayQueue`: Enqueues `SqlQuery` objects for processing by the database thread.
    *   `MaNGOS::QueryCallback` / `MaNGOS::SQueryCallback`: Constructs callback objects defined in `SqlOperations.h`.
    *   `SqlQuery`: Wraps SQL strings and callbacks for the queue.
    *   `sLog.Out`: Logs errors if SQL formatting truncates.
    *   `SqlQueryHolder::Execute`: Delegates execution of complex queries to the `SqlQueryHolder` unit.

*   **Called By:**
    *   No external callers are listed in the MAP. These templates are instantiated by various game server components (e.g., `Player`, `Creature`) whenever asynchronous database access is required.

## Data Model

This unit does not interact with database tables directly. It constructs and submits SQL strings. Table interactions occur in the callbacks specified by callers or within `SqlQueryHolder` objects.

## Notable Implementation Details

*   **Truncation Safety:** `AsyncPQuery` variants strictly check for `vsnprintf` truncation. Executing truncated SQL is prevented to avoid syntax errors or security issues.
*   **Thread Safety Flag:** The `Unsafe` variants set `cb->threadSafe = false`. This signals the callback infrastructure to execute the callback on the database thread rather than posting it back to the game thread. Callers must ensure these callbacks do not access non-thread-safe game state.
*   **Template Overloads:** The code provides explicit overloads for 0, 1, 2, and 3 extra parameters. This predates variadic templates and ensures type safety for callback arguments.
*   **Memory Management:** Callbacks and `SqlQuery` objects are allocated with `new`. Ownership transfers to the queue (`AddToDelayQueue`) or the holder (`holder->Execute`), which are responsible for deletion after execution.

## Member Reference

**Database::AsyncQuery(Class* object, void (Class::*method)(std::unique_ptr<QueryResult>), char const* sql)**
Submits an async query with a member callback (no extra params). Creates `QueryCallback`, wraps in `SqlQuery`, and adds to delay queue.

**Database::AsyncQueryUnsafe(Class* object, void (Class::*method)(std::unique_ptr<QueryResult>), char const* sql)**
Same as above, but sets `cb->threadSafe = false`.

**Database::AsyncQuery(Class* object, void (Class::*method)(std::unique_ptr<QueryResult>, ParamType1), ParamType1 param1, char const* sql)**
Submits an async query with a member callback taking one extra parameter.

**Database::AsyncQueryUnsafe(Class* object, void (Class::*method)(std::unique_ptr<QueryResult>, ParamType1), ParamType1 param1, char const* sql)**
Same as above, but sets `cb->threadSafe = false`.

**Database::AsyncQuery(Class* object, void (Class::*method)(std::unique_ptr<QueryResult>, ParamType1, ParamType2), ParamType1 param1, ParamType2 param2, char const* sql)**
Submits an async query with a member callback taking two extra parameters.

**Database::AsyncQuery(Class* object, void (Class::*method)(std::unique_ptr<QueryResult>, ParamType1, ParamType2, ParamType3), ParamType1 param1, ParamType2 param2, ParamType3 param3, char const* sql)**
Submits an async query with a member callback taking three extra parameters.

**Database::AsyncQuery(void (*method)(std::unique_ptr<QueryResult>, ParamType1), ParamType1 param1, char const* sql)**
Submits an async query with a static callback taking one extra parameter. Uses `SQueryCallback`.

**Database::AsyncQuery(void (*method)(std::unique_ptr<QueryResult>, ParamType1, ParamType2), ParamType1 param1, ParamType2 param2, char const* sql)**
Submits an async query with a static callback taking two extra parameters.

**Database::AsyncQuery(void (*method)(std::unique_ptr<QueryResult>, ParamType1, ParamType2, ParamType3), ParamType1 param1, ParamType2 param2, ParamType3 param3, char const* sql)**
Submits an async query with a static callback taking three extra parameters.

**Database::AsyncQueryUnsafe(void (*method)(std::unique_ptr<QueryResult>, ParamType1), ParamType1 param1, char const* sql)**
Submits an async query with a static callback taking one extra parameter, marked thread-unsafe.

**Database::AsyncQueryUnsafe(void(*method)(std::unique_ptr<QueryResult>, ParamType1, ParamType2), ParamType1 param1, ParamType2 param2, char const* sql)**
Submits an async query with a static callback taking two extra parameters, marked thread-unsafe.

**Database::AsyncPQuery(Class* object, void (Class::*method)(std::unique_ptr<QueryResult>), char const* format,...)**
Formats SQL via `vsnprintf`; logs error and returns false if truncated. Otherwise delegates to `AsyncQuery`.

**Database::AsyncPQueryUnsafe(Class* object, void (Class::*method)(std::unique_ptr<QueryResult>), char const* format,...)**
Formats SQL and delegates to `AsyncQueryUnsafe`.

**Database::AsyncPQuery(Class* object, void (Class::*method)(std::unique_ptr<QueryResult>, ParamType1), ParamType1 param1, char const* format,...)**
Formats SQL and delegates to `AsyncQuery` with one extra parameter.

**Database::AsyncPQueryUnsafe(Class* object, void (Class::*method)(std::unique_ptr<QueryResult>, ParamType1), ParamType1 param1, char const* format,...)**
Formats SQL and delegates to `AsyncQueryUnsafe` with one extra parameter.

**Database::AsyncPQuery(Class* object, void (Class::*method)(std::unique_ptr<QueryResult>, ParamType1, ParamType2), ParamType1 param1, ParamType2 param2, char const* format,...)**
Formats SQL and delegates to `AsyncQuery` with two extra parameters.

**Database::AsyncPQuery(Class* object, void (Class::*method)(std::unique_ptr<QueryResult>, ParamType1, ParamType2, ParamType3), ParamType1 param1, ParamType2 param2, ParamType3 param3, char const* format,...)**
Formats SQL and delegates to `AsyncQuery` with three extra parameters.

**Database::AsyncPQuery(void (*method)(std::unique_ptr<QueryResult>, ParamType1), ParamType1 param1, char const* format,...)**
Formats SQL and delegates to static `AsyncQuery` with one extra parameter.

**Database::AsyncPQuery(void (*method)(std::unique_ptr<QueryResult>, ParamType1, ParamType2), ParamType1 param1, ParamType2 param2, char const* format,...)**
Formats SQL and delegates to static `AsyncQuery` with two extra parameters.

**Database::AsyncPQuery(void (*method)(std::unique_ptr<QueryResult>, ParamType1, ParamType2, ParamType3), ParamType1 param1, ParamType2 param2, ParamType3 param3, char const* format,...)**
Formats SQL and delegates to static `AsyncQuery` with three extra parameters.

**Database::AsyncPQueryUnsafe(void (*method)(std::unique_ptr<QueryResult>, ParamType1), ParamType1 param1, char const* format, ...)**
Formats SQL and delegates to static `AsyncQueryUnsafe` with one extra parameter.

**Database::AsyncPQueryUnsafe(void(*method)(std::unique_ptr<QueryResult>, ParamType1, ParamType2), ParamType1 param1, ParamType2 param2, char const* format, ...)**
Formats SQL and delegates to static `AsyncQueryUnsafe` with two extra parameters.

**Database::DelayQueryHolder(Class* object, void (Class::*method)(std::unique_ptr<QueryResult>, SqlQueryHolder*), SqlQueryHolder* holder)**
Validates holder and delegates to `holder->Execute()` with a member callback receiving the holder pointer.

**Database::DelayQueryHolderUnsafe(Class* object, void (Class::*method)(std::unique_ptr<QueryResult>, SqlQueryHolder*), SqlQueryHolder* holder)**
Same as above, but marks callback thread-unsafe.

**Database::DelayQueryHolder(Class* object, void (Class::*method)(std::unique_ptr<QueryResult>, SqlQueryHolder*, ParamType1), SqlQueryHolder* holder, ParamType1 param1)**
Delegates to `holder->Execute()` with a member callback receiving the holder pointer and one extra parameter.

**Database::DelayQueryHolder(void (*method)(std::unique_ptr<QueryResult>, SqlQueryHolder*, ParamType1), SqlQueryHolder* holder, ParamType1 param1)**
Delegates to `holder->Execute()` with a static callback receiving the holder pointer and one extra parameter.

**Database::DelayQueryHolderUnsafe(void (*method)(std::unique_ptr<QueryResult>, SqlQueryHolder*, ParamType1), SqlQueryHolder* holder, ParamType1 param1)**
Delegates to `holder->Execute()` with a static callback, marked thread-unsafe.

---

<!-- machine-true, projected from graph.json -->

## Map — DatabaseImpl

*Source:* DatabaseImpl.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
