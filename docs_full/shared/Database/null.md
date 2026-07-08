# null

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# DatabaseCallback.h

## Purpose & Responsibilities

`DatabaseCallback.h` defines the callback infrastructure used by the MaNGOS server to handle asynchronous operations, primarily database queries. Because database queries are often executed on worker threads separate from the main game loop or network handling threads, results cannot be returned synchronously. Instead, the caller constructs a **callback object** that encapsulates:
1.  The target object and method (or static function) to invoke upon completion.
2.  Any additional parameters required by that method.
3.  The `QueryResult` itself (for query-specific callbacks).

This unit provides two primary families of callback classes within the `MaNGOS` namespace:
*   **Generic Callbacks (`Callback`, `_Callback`, `_SCallback`, etc.)**: Used for general-purpose asynchronous notifications where no specific data payload (like a query result) is strictly tied to the callback interface, though parameters can still be passed.
*   **Query Callbacks (`QueryCallback`, `SQueryCallback`, `IQueryCallback`, etc.)**: Specialized wrappers that automatically manage a `std::unique_ptr<QueryResult>` as the first argument to the target method. These integrate with the `IQueryCallback` interface, which adds thread-safety flags and result storage mechanisms.

The design relies heavily on C++ template specialization to support methods with 0 to 4 arguments (plus the implicit `this` pointer for member functions). It uses macros (`TYPENAMES_N`, `PARAMS_N`) to reduce boilerplate in template declarations, although the visible specializations manually list parameters up to four.

## Member-by-Member Behavior

### The `null` Struct
The only member explicitly listed in the MAP for this unit is the `null` struct.

*   **`null()` (Constructor)**: A trivial default constructor for the empty struct `null`.
    *   **Purpose**: In C++ templates, `void` cannot be used as a template argument for certain contexts (e.g., as a parameter type in a function signature template if it needs to be instantiated as a variable). The `null` struct serves as a placeholder type to represent "no parameter" in the template machinery. For example, `_Callback<Class, null, null, ...>` indicates a callback with no extra parameters beyond the object/method.
    *   **Behavior**: Does nothing. It exists solely to satisfy template instantiation requirements.

### Template Infrastructure (Contextual Overview)
While not individual "members" in the traditional sense, the following template classes constitute the bulk of the unit's logic. They are organized by functionality:

#### 1. Base Callback Implementations (`_Callback` and `_SCallback`)
These are the internal engines that store the state (object pointer, method pointer, parameters) and provide the `_Execute()` method that performs the actual invocation.

*   **`_Callback<Class, P1, P2, P3, P4>`**: Stores a member function pointer (`Method`), an object pointer (`m_object`), and up to 4 parameters. `_Execute()` invokes `(m_object->*m_method)(...)`.
*   **`_SCallback<P1, P2, P3, P4>`**: Similar to `_Callback` but for **static** functions. It stores a function pointer (`m_method`) and parameters. `_Execute()` invokes `(*m_method)(...)`.
*   **Specializations**: Explicit specializations exist for 3, 2, 1, and 0 parameters to avoid storing unused `void` or `null` types and to generate correct function signatures.

#### 2. Interface Adapters (`_ICallback` and `_IQueryCallback`)
These classes inherit from the base implementations (`_Callback`/`_SCallback`) and implement the abstract interfaces `ICallback` or `IQueryCallback`.

*   **`_ICallback<CB>`**: Inherits from `CB` (a base callback) and `ICallback`. Its `Execute()` method simply calls `CB::_Execute()`. This allows polymorphic deletion and execution via `ICallback*`.
*   **`_IQueryCallback<CB>`**: Inherits from `CB` and `IQueryCallback`. It implements `SetResult()` and `GetResult()` by accessing `CB::m_param1`. This assumes the first parameter of the underlying callback is always the `QueryResult`.

#### 3. Public Factory Classes (`Callback`, `QueryCallback`, `SQueryCallback`)
These are the classes users of this library actually instantiate. They wrap the complex inheritance hierarchy into simple constructors.

*   **`Callback<Class, P1...>`**: Creates a generic callback for a member function.
*   **`QueryCallback<Class, P1...>`**: Creates a callback for a member function that receives a `QueryResult` as its first argument.
*   **`SQueryCallback<P1...>`**: Creates a callback for a **static** function that receives a `QueryResult` as its first argument.

## Cross-Unit Boundaries

*   **Calls Out**:
    *   **`QueryResult`**: The `QueryCallback` and `SQueryCallback` families depend on the `QueryResult` class (declared in `QueryResult.h`). They store `std::unique_ptr<QueryResult>` and pass it to the target method.
    *   **`std::unique_ptr`**: Used extensively for ownership management of the `QueryResult`.
    *   **`std::move`**: Used to transfer ownership of parameters into the callback objects.

*   **Called By**:
    *   **Database Worker Threads**: Other units (likely `DatabaseWorker` or similar) will hold `std::unique_ptr<ICallback>` or `std::unique_ptr<IQueryCallback>` objects. Upon query completion, they will call `Execute()` on these pointers. The polymorphic dispatch will route to the correct `_ICallback` or `_IQueryCallback` specialization, which then invokes the user-defined method.

## Data Model

This unit does not interact directly with database tables. It operates entirely in memory, managing pointers and temporary data structures. The `QueryResult` it handles contains data fetched from tables, but `DatabaseCallback.h` itself is agnostic to the schema.

## Notable Implementation Details

1.  **Macro-Based Template Generation**:
    The file uses macros like `TYPENAMES_1` through `TYPENAMES_10` and `PARAMS_1` through `PARAMS_10`. While the current template specializations only go up to 4 parameters, these macros suggest an intent to support more, or they are leftovers from a broader template framework. Currently, they are unused in the visible template definitions, which manually list `ParamType1` through `ParamType4`.

2.  **First Parameter Assumption for QueryResults**:
    In `QueryCallback` and `SQueryCallback`, the `QueryResult` is **always** the first parameter passed to the target method. This is hardcoded in the `_IQueryCallback` implementation:
    ```cpp
    void SetResult(std::unique_ptr<QueryResult> result) { CB::m_param1 = std::move(result); }
    ```
    This means any method targeted by a `QueryCallback` must accept `std::unique_ptr<QueryResult>` as its first argument. Additional parameters follow after it.

3.  **Thread Safety Flag**:
    `IQueryCallback` contains a `bool threadSafe` member. It defaults to `true` in the constructor. This flag likely informs the database worker whether it can execute the callback on the worker thread or if it must post it back to the main thread. The callback object itself does not enforce this; it merely carries the flag.

4.  **Move Semantics**:
    Parameters are moved into the callback objects (`std::move(param1)`). This is efficient for large objects but implies that the original variables passed to the callback constructor are invalidated.

5.  **No Virtual Destructor in Base Templates**:
    The `_Callback` and `_SCallback` templates do not have virtual destructors. However, they are never deleted directly; they are embedded within `_ICallback` or `_IQueryCallback`, which *do* have virtual destructors (inherited from `ICallback`/`IQueryCallback`). This is safe because the lifetime is managed by the outer wrapper.

## Member Reference

**null**
Default constructor for the empty `null` struct. Used as a placeholder type in template arguments to represent "no parameter" where `void` is invalid. Does nothing.

---

<!-- machine-true, projected from graph.json -->

## Map — null

*Source:* DatabaseCallback.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| null | ctor | — | — | — |
