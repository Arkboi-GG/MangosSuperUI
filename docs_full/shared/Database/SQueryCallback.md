# SQueryCallback

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# SQueryCallback

**Purpose & Responsibilities**

`SQueryCallback` is a template class family within the `MaNGOS` namespace that implements **static** asynchronous query callbacks for the database subsystem. It allows the server to register a static function (a function pointer) to be executed once a database query completes, passing the resulting `QueryResult` and up to three additional user-defined parameters to that function.

It is part of a broader callback infrastructure (`DatabaseCallback.h`) that enables non-blocking database operations. While `QueryCallback` handles member functions of specific objects, `SQueryCallback` is designed for contexts where no specific object instance is relevant, or where the handler is a global/static utility function. It inherits from `_IQueryCallback`, which provides the interface required by the database thread pool to invoke the callback and manage the `QueryResult` lifecycle.

**Member-by-Member Behavior**

The unit defines four explicit specializations of the `SQueryCallback` template, differing only by the number of additional parameters (0 to 3) passed alongside the `QueryResult`. All share the same structural pattern:

1.  **Inheritance**: Each specialization inherits from `_IQueryCallback<_SCallback<...>>`. The `_IQueryCallback` wrapper exposes the `IQueryCallback` interface (including `Execute()`, `SetResult()`, `GetResult()`) while delegating storage and execution to the underlying `_SCallback` template.
2.  **Storage**: The underlying `_SCallback` stores:
    *   A function pointer (`Method`) to the static handler.
    *   A `std::unique_ptr<QueryResult>` containing the query data.
    *   Up to three additional parameters (`param1`, `param2`, `param3`) of arbitrary types, which are forwarded to the handler.
3.  **Execution**: When the database system calls `Execute()` on the callback, `_IQueryCallback` delegates to `_SCallback::_Execute()`, which invokes the stored function pointer with the moved `QueryResult` and the copied parameters.

**Cross-Unit Boundaries**

*   **Calls Out**: None. `SQueryCallback` is a pure data-holder and dispatcher. It does not initiate any calls itself; it only holds references to functions and data provided at construction.
*   **Called By**: Other units in the codebase (not shown in this MAP) construct `SQueryCallback` instances and pass them to the database connection pools. The database thread pool calls `Execute()` on these objects when queries complete. The `QueryResult` type comes from `QueryResult.h` (included via `DatabaseCallback.h`'s dependencies).

**Data Model**

This unit does not interact directly with database tables. It operates on `QueryResult` objects, which are opaque containers for rows returned by SQL queries. The specific tables accessed depend entirely on the SQL string used by the caller who constructed the `SQueryCallback`.

**Notable Implementation Details**

*   **Static vs. Member Callbacks**: `SQueryCallback` uses `_SCallback` internally, which stores a raw function pointer. In contrast, `QueryCallback` uses `_Callback`, which stores an object pointer and a member function pointer. This distinction is critical: `SQueryCallback` cannot capture `this` pointers or member state, making it suitable only for stateless handlers or those that rely on global/static state.
*   **Parameter Limit**: The template supports up to 3 additional parameters beyond the `QueryResult`. If a handler requires more context, the caller must bundle extra data into a single struct or pointer passed as one of the three parameters.
*   **Move Semantics**: The `QueryResult` is stored as a `std::unique_ptr` and is moved into the callback during construction. This ensures ownership transfer and avoids copying potentially large result sets. The first parameter (`param1` in `_SCallback`) is always the `QueryResult`, and `std::move` is used when invoking the handler to transfer ownership to the callback function.
*   **Thread Safety Flag**: The base `IQueryCallback` class contains a `bool threadSafe` member, initialized to `true` in its constructor. This flag indicates whether the callback's `Execute()` method can be safely called from any thread (typically the database worker thread). Callers should ensure their static handler functions are thread-safe if this flag remains true.
*   **Template Specialization Pattern**: The code uses explicit template specialization for 0, 1, 2, and 3 parameters. This was likely done to avoid variadic templates (pre-C++11) or to maintain compatibility with older compilers. Each specialization manually defines the `typedef` for the underlying `_SCallback` and forwards arguments correctly.

## Member Reference

**SQueryCallback**
Constructor for the static query callback template. There are four specializations:
1.  `SQueryCallback<>(Method, std::unique_ptr<QueryResult>)`: No extra parameters.
2.  `SQueryCallback<ParamType1>(Method, std::unique_ptr<QueryResult>, ParamType1)`: One extra parameter.
3.  `SQueryCallback<ParamType1, ParamType2>(Method, std::unique_ptr<QueryResult>, ParamType1, ParamType2)`: Two extra parameters.
4.  `SQueryCallback<ParamType1, ParamType2, ParamType3>(Method, std::unique_ptr<QueryResult>, ParamType1, ParamType2, ParamType3)`: Three extra parameters.

Each constructor initializes the underlying `_IQueryCallback` wrapper, which in turn constructs the `_SCallback` base with the provided function pointer, result, and parameters. The `QueryResult` is moved into the callback structure.

---

<!-- machine-true, projected from graph.json -->

## Map — SQueryCallback

*Source:* DatabaseCallback.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| SQueryCallback | ctor | — | — | — |
