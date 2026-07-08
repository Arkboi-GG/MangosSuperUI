# IQueryCallback

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# IQueryCallback

`IQueryCallback` is the abstract base class defining the interface for asynchronous database query callbacks within the MaNGOS server framework. It serves as the contract between the database execution layer (`SqlOperations`) and the user-defined logic that processes query results.

The primary responsibility of `IQueryCallback` is to standardize how query results are delivered to callback handlers. It ensures that:
1.  The callback object can be executed via a uniform `Execute()` method.
2.  The result of a database query (wrapped in a `std::unique_ptr<QueryResult>`) can be injected into the callback before execution via `SetResult()`.
3.  The callback can retrieve that result via `GetResult()`.
4.  The system can determine if the callback's execution context requires thread safety checks via `IsThreadSafe()`.

This unit contains **no implementation logic** for the callbacks themselves; it only defines the interface. The actual callback implementations are provided by the template classes `QueryCallback`, `SQueryCallback`, `_IQueryCallback`, and their underlying `_Callback`/`_SCallback` helpers, all defined in the same header. These templates implement `IQueryCallback` to bind specific member functions or static functions to the query result.

## Member-by-Member Behavior

### Interface Definition

*   **`IQueryCallback` (ctor)**: Initializes the `threadSafe` member variable to `true`. This default assumes that callbacks are safe to execute from any thread unless explicitly marked otherwise (though the mechanism for marking them otherwise is not exposed in this base class, implying subclasses or derived usage patterns handle this).
*   **`~IQueryCallback` (dtor)**: Virtual destructor ensuring proper cleanup of derived callback objects.
*   **`Execute` (decl)**: Pure virtual function. Derived classes must implement this to define what happens when the callback is triggered. In the template implementations (`_IQueryCallback`), this typically calls the underlying `_Execute()` method of the bound function wrapper.
*   **`SetResult` (decl)**: Pure virtual function. Called by the database layer (`SqlOperations`) to pass the `QueryResult` pointer to the callback. The template implementations store this in the first parameter slot (`m_param1`) of the underlying callback wrapper.
*   **`GetResult` (decl)**: Pure virtual function. Allows the callback handler to access the query result. The template implementations return a reference to the stored `m_param1`.
*   **`IsThreadSafe` (method)**: Returns the value of the `threadSafe` boolean member. Used by `SqlOperations::Update` to determine synchronization requirements.

### Template Implementations (Supporting Classes)

While not part of the `IQueryCallback` interface itself, the following templates in this unit provide the concrete implementations:

*   **`_IQueryCallback<CB>`**: A CRTP (Curiously Recurring Template Pattern) helper that inherits from both a callback wrapper (`CB`, e.g., `_Callback` or `_SCallback`) and `IQueryCallback`. It implements the pure virtual methods by delegating to the `CB` base:
    *   `Execute()` calls `CB::_Execute()`.
    *   `SetResult()` moves the result into `CB::m_param1`.
    *   `GetResult()` returns `CB::m_param1`.
*   **`QueryCallback<Class, ...>`**: Binds a member function of a specific class instance to a query result. It inherits from `_IQueryCallback<_Callback<...>>`. The first parameter of the bound member function is always `std::unique_ptr<QueryResult>`.
*   **`SQueryCallback<...>`**: Binds a static function (or free function) to a query result. It inherits from `_IQueryCallback<_SCallback<...>>`. The first parameter of the bound function is always `std::unique_ptr<QueryResult>`.
*   **`_Callback` / `_SCallback`**: Low-level wrappers that store the object pointer/method pointer (or function pointer) and up to 4 additional parameters. They provide the `_Execute()` method that performs the actual invocation using `std::move` for the first parameter (the result) to ensure efficient transfer of ownership.

## Cross-Unit Boundaries

*   **Called by `SqlOperations/CancelAll`**: `SqlOperations` likely holds a collection of pending callbacks. When canceling all operations, it iterates through them and may call `Execute()` or clean them up. The `IQueryCallback` interface allows `SqlOperations` to treat all callbacks uniformly.
*   **Called by `SqlOperations/Update`**: `SqlOperations::Update` is responsible for processing completed queries. It retrieves the result and calls `SetResult()` on the associated `IQueryCallback` to inject the data. It also calls `IsThreadSafe()` to decide whether to invoke the callback immediately on the current thread or queue it for the main thread, depending on the server's threading model.
*   **Called by `SqlOperations/Execute#3`**: Another overload of `SqlOperations::Execute` likely handles the initial registration or immediate execution of synchronous queries, also interacting with the callback's `SetResult` or `Execute` methods.

## Data Model

This unit does not interact directly with any database tables. It operates entirely on in-memory objects (`QueryResult`). The `QueryResult` object itself contains the data fetched from tables, but `IQueryCallback` is agnostic to the specific tables involved.

## Notable Implementation Details

1.  **Parameter Order Rigidity**: In `QueryCallback` and `SQueryCallback`, the `std::unique_ptr<QueryResult>` is **always** the first parameter passed to the bound function. Subsequent parameters (`ParamType1`, etc.) are user-defined context data. This is enforced by the template structure where `m_param1` is reserved for the result.
2.  **Move Semantics**: The `_Execute()` methods in `_Callback` and `_SCallback` use `std::move(m_param1)` when invoking the target function. This ensures the `QueryResult` ownership is transferred efficiently to the handler, avoiding deep copies of potentially large result sets.
3.  **Thread Safety Default**: The `threadSafe` flag defaults to `true` in the constructor. This implies that unless a specific mechanism exists to set it to `false` (not visible in this base class, possibly handled by derived classes or factory functions not shown here), callbacks are assumed to be thread-safe. Misuse here could lead to race conditions if the callback modifies non-thread-safe global state.
4.  **Template Specialization**: The code uses explicit template specializations for `_Callback` and `_SCallback` for different arities (0 to 4 parameters). This was necessary before variadic templates (C++11) became widespread, allowing flexible callback signatures while maintaining type safety.
5.  **CRTP Usage**: `_IQueryCallback` uses CRTP to avoid virtual function overhead for the `Execute`, `SetResult`, and `GetResult` calls if the compiler can inline them, though since `IQueryCallback` has virtual methods, dynamic dispatch will occur when called through the base pointer in `SqlOperations`.

## Member Reference

*   **IQueryCallback**: Constructor that initializes the `threadSafe` member to `true`.
*   **Execute**: Pure virtual function declared in the interface; implemented by derived templates to invoke the bound user function.
*   **~IQueryCallback**: Virtual destructor for safe deletion of derived callback objects.
*   **SetResult**: Pure virtual function declared in the interface; implemented by derived templates to store the `QueryResult` in the first parameter slot.
*   **GetResult**: Pure virtual function declared in the interface; implemented by derived templates to return the stored `QueryResult`.
*   **IsThreadSafe**: Inline method returning the `threadSafe` boolean flag, used by `SqlOperations` to determine execution context.

---

<!-- machine-true, projected from graph.json -->

## Map — IQueryCallback

*Source:* DatabaseCallback.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| IQueryCallback | ctor | — | — | — |
| Execute | decl | — | SqlOperations/CancelAll, SqlOperations/Update | — |
| ~IQueryCallback | dtor | — | — | — |
| SetResult | decl | — | SqlOperations/CancelAll, SqlOperations/Execute#3 | — |
| GetResult | decl | — | — | — |
| IsThreadSafe | method | — | SqlOperations/Update | — |
