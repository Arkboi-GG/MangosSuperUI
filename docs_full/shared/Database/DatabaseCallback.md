<!-- provenance: failed-members -->
# DatabaseCallback

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# DatabaseCallback

`DatabaseCallback.h` defines a suite of C++ template classes within the `MaNGOS` namespace that implement a callback mechanism for asynchronous operations, primarily database queries. It provides wrappers that allow the server to schedule a method call (on a specific object or a static function) to execute after an operation completes, passing any resulting data (such as a `QueryResult`) and additional user-defined parameters to that method.

The unit is purely declarative; it contains no `.cpp` implementation files. All logic resides in the header via templates. It relies on `QueryResult.h` for the `QueryResult` type.

## Purpose & Responsibilities

In the MaNGOS/WoWVMangos architecture, database queries are often executed asynchronously to avoid blocking the main game loop. When a query finishes, the system needs to invoke a handler function with the results. `DatabaseCallback` provides the infrastructure to bundle:
1.  **The target**: Either a member function pointer (`Class::*`) with an object instance, or a static/free function pointer.
2.  **The arguments**: Up to four additional parameters (of arbitrary types) alongside the primary data.
3.  **The execution interface**: A common base class (`ICallback` or `IQueryCallback`) that allows the database scheduler to invoke the stored method uniformly via `Execute()`.

It supports two main categories:
*   **Generic Callbacks** (`Callback`, `_SCallback`): For general-purpose deferred execution where no specific result type is enforced.
*   **Query Callbacks** (`QueryCallback`, `SQueryCallback`): Specifically designed to receive a `std::unique_ptr<QueryResult>` as the first argument, allowing handlers to process database rows immediately upon completion.

## Member-by-Member Behavior

The unit is organized into three layers of abstraction:
1.  **Base Implementations** (`_Callback`, `_SCallback`): Store the raw pointers and parameters. They define the protected `_Execute()` method that performs the actual invocation.
2.  **Interface Adapters** (`_ICallback`, `_IQueryCallback`): Inherit from both a Base Implementation and an Interface (`ICallback`/`IQueryCallback`). They expose the public `Execute()` method required by the scheduler.
3.  **Public Facades** (`Callback`, `QueryCallback`, etc.): Concrete template instantiations that users construct. They initialize the adapter layer.

### Generic Callbacks

#### `_Callback` (Variants)
These are the core storage classes for member function callbacks. There are five specializations based on the number of additional parameters (0 to 4).
*   **Storage**: Holds `Class *m_object`, `Method m_method` (pointer-to-member), and `m_param1` through `m_param4`.
*   **Behavior**: The constructor stores these values. Note that `m_param1` is always moved (`std::move`) during construction and execution, while subsequent parameters are copied. This implies `m_param1` is intended for move-only types (like smart pointers or strings), while others are expected to be cheap-to-copy or simple types.
*   **Execution**: The protected `_Execute()` method invokes `(m_object->*m_method)(...)` with the stored arguments.

#### `_SCallback` (Variants)
Similar to `_Callback`, but for static or free functions.
*   **Storage**: Holds `Method m_method` (function pointer) and `m_param1` through `m_param4`.
*   **Behavior**: Same move/copy semantics as `_Callback`.
*   **Execution**: The protected `_Execute()` method invokes `(*m_method)(...)`.

#### `ICallback`
An abstract base class defining the contract for all generic callbacks.
*   **`Execute()`**: Pure virtual function. The database scheduler calls this to trigger the stored action.
*   **Destructor**: Virtual, ensuring proper cleanup of derived template instances.

#### `_ICallback`
A template adapter that inherits from a specific `_Callback` or `_SCallback` specialization and `ICallback`.
*   **`Execute()`**: Implements the pure virtual `ICallback::Execute()` by calling `CB::_Execute()`. This bridges the specific template instantiation to the polymorphic interface.

#### `Callback` (Variants)
Public-facing classes for member function callbacks.
*   **Construction**: Takes an object pointer, a member function pointer, and up to 4 parameters.
*   **Inheritance**: Inherits from `_ICallback<_Callback<...>>`.
*   **Usage**: Users instantiate these to register a member function to be called later.

### Query Callbacks

These are specialized for database results. They ensure the first parameter passed to the callback is always the `QueryResult`.

#### `IQueryCallback`
Abstract base class for query-specific callbacks.
*   **`threadSafe`**: A boolean flag indicating whether the callback can be executed on a thread different from the one that created it. Defaults to `true`.
*   **`SetResult()`**: Pure virtual. Allows the database system to inject the `QueryResult` into the callback before execution.
*   **`GetResult()`**: Pure virtual. Allows retrieving the result (though typically the result is passed during execution).
*   **`IsThreadSafe()`**: Returns the `threadSafe` flag.

#### `_IQueryCallback`
Adapter for query callbacks. Inherits from a Base Callback (which expects `std::unique_ptr<QueryResult>` as its first parameter) and `IQueryCallback`.
*   **`SetResult()`**: Moves the incoming `result` into `CB::m_param1`. Since the underlying `_Callback` or `_SCallback` stores `m_param1` as the first argument, this effectively sets the `QueryResult` argument for the eventual call.
*   **`GetResult()`**: Returns a reference to `CB::m_param1`.
*   **`Execute()`**: Calls `CB::_Execute()`.

#### `QueryCallback` (Variants)
Public-facing classes for member function callbacks that expect a `QueryResult`.
*   **Construction**: Takes an object pointer, a member function pointer, a `std::unique_ptr<QueryResult>`, and up to 3 additional parameters.
*   **Inheritance**: Inherits from `_IQueryCallback<_Callback<Class, std::unique_ptr<QueryResult>, ...>>`.
*   **Note**: The `QueryResult` is passed by value (move) in the constructor, but `IQueryCallback::SetResult` is also provided, suggesting the result might be injected later by the DB system. However, the constructor signature in `QueryCallback` explicitly takes the result. This suggests two usage patterns: either the result is known at creation time (unlikely for async), or the constructor is used to bind the *other* params and the object/method, while the DB system uses `SetResult` to provide the actual result. Looking at `_IQueryCallback::SetResult`, it overwrites `m_param1`. Therefore, the `QueryResult` passed to the `QueryCallback` constructor is likely ignored or serves as a placeholder, and the real result is injected via `SetResult` before `Execute` is called.

#### `SQueryCallback` (Variants)
Static function equivalents of `QueryCallback`.
*   **Construction**: Takes a function pointer, a `std::unique_ptr<QueryResult>`, and up to 3 additional parameters.
*   **Inheritance**: Inherits from `_IQueryCallback<_SCallback<std::unique_ptr<QueryResult>, ...>>`.

## Cross-Unit Boundaries

*   **Calls Out**: None. This unit is a header-only template library. It does not call any other units.
*   **Called By**:
    *   **Database Scheduler/Pool**: Units managing database connections (e.g., `DatabaseWorker`, `QueryExecutor`) will create instances of `Callback` or `QueryCallback`, push them onto a queue, and later call `Execute()` on them.
    *   **Game Logic**: Any part of the server code needing to perform an async DB query will instantiate these callbacks to handle the response. For example, a player login handler might create a `QueryCallback<PlayerLoginHandler, uint32>` to process the account verification result.

## Data Model

This unit does not interact with database tables directly. It operates on `QueryResult` objects, which are opaque handles to the results of SQL queries executed by lower-level database drivers. The schema of the underlying tables is irrelevant to this unit; it only cares about the `QueryResult` interface.

## Notable Implementation Details

1.  **Move Semantics for First Parameter**: In all `_Callback` and `_SCallback` variants, `m_param1` is constructed using `std::move(param1)` and invoked using `std::move(m_param1)`. Parameters 2–4 are copied. This is a significant constraint: if you pass a non-moveable type as the first parameter, it will fail to compile or behave unexpectedly. Conversely, if you pass a move-only type (like `std::unique_ptr`) as `param2`, it will fail to compile because the copy constructor is deleted. Designers of callbacks must place move-only types in the first slot.

2.  **Template Bloat**: The unit manually specializes templates for 0, 1, 2, 3, and 4 parameters. This pre-C++17 approach avoids variadic templates but results in significant code duplication. Each specialization repeats the storage and execution logic.

3.  **Thread Safety Flag**: `IQueryCallback` has a `threadSafe` boolean. This is crucial for the MaNGOS architecture, which likely separates the database thread(s) from the world/game thread. If a callback is marked `threadSafe=false`, the scheduler must marshal the execution back to the original thread (likely the world thread) before calling `Execute()`. If `true`, it can be executed directly on the DB thread.

4.  **Result Injection vs. Construction**: As noted, `QueryCallback` constructors accept a `QueryResult`, but `IQueryCallback::SetResult` allows overwriting it. The typical flow is:
    1.  User creates `QueryCallback` with dummy/null result and binds object/method/extra params.
    2.  DB system executes query.
    3.  DB system calls `callback->SetResult(actual_result)`.
    4.  DB system calls `callback->Execute()`.
    This decouples the binding of the handler from the availability of the data.

5.  **Null Struct**: The `null` struct is defined but unused in the visible code. It was likely intended as a placeholder for empty parameters in older template metaprogramming techniques, but the current code uses `void` defaults and explicit specializations instead.

6.  **Copy Constructors**: `_Callback<Class>` and `_SCallback<>` (the zero-param versions) have explicit copy constructors. Other variants rely on implicitly generated ones. This asymmetry might lead to subtle bugs if a user tries to copy a callback with parameters that are not copyable, relying on the default behavior which would attempt to copy `m_param1` (which might be move-only). However, since `m_param1` is stored by value, the implicit copy constructor would try to copy it. If `T1` is `std::unique_ptr`, the implicit copy constructor is deleted. The explicit copy constructors in the zero-param versions don't help with the multi-param versions. Users must ensure their callback parameters are copyable if they intend to copy the callback object itself.

## Member Reference

**_Execute#9**
Protected method in `_Callback<Class, ParamType1, ParamType2, ParamType3, ParamType4>`. Invokes the stored member function with all four parameters. `m_param1` is moved.

**_Callback<Class, ParamType1, ParamType2, ParamType3, ParamType4>**
Template class storing a member function pointer, object pointer, and 4 parameters. Base for 4-param callbacks.

**_Execute#7**
Protected method in `_Callback<Class, ParamType1, ParamType2, ParamType3>`. Invokes the stored member function with three parameters. `m_param1` is moved.

**_Callback<type-parameter-0-0, type-parameter-0-1, type-parameter-0-2, type-parameter-0-3, void>**
Partial specialization of `_Callback` for 3 parameters.

**_Execute#5**
Protected method in `_Callback<Class, ParamType1, ParamType2>`. Invokes the stored member function with two parameters. `m_param1` is moved.

**_Callback<type-parameter-0-0, type-parameter-0-1, type-parameter-0-2, void, void>**
Partial specialization of `_Callback` for 2 parameters.

**_Execute#3**
Protected method in `_Callback<Class, ParamType1>`. Invokes the stored member function with one parameter. `m_param1` is moved.

**_Callback<type-parameter-0-0, type-parameter-0-1, void, void, void>**
Partial specialization of `_Callback` for 1 parameter.

**_Execute**
Protected method in `_Callback<Class>`. Invokes the stored member function with no parameters.

**_Callback<type-parameter-0-0, void, void, void, void>#2**
Partial specialization of `_Callback` for 0 parameters. Includes a copy constructor.

**_Callback<type-parameter-0-0, void, void, void, void>**
Primary template declaration for `_Callback` (defaults to 4 params, but this entry likely refers to the 0-param specialization in the MAP context due to the #2 duplicate). Actually, the MAP lists `_Callback<type-parameter-0-0, void, void, void, void>` twice. One is the primary template with defaults, the other is the explicit specialization. The source shows `template<class Class> class _Callback<Class>` which is the 0-param case.

**_Execute#8**
Protected method in `_SCallback<ParamType1, ParamType2, ParamType3, ParamType4>`. Invokes the stored static function with four parameters. `m_param1` is moved.

**_SCallback<ParamType1, ParamType2, ParamType3, ParamType4>**
Template class storing a static function pointer and 4 parameters. Base for 4-param static callbacks.

**_Execute#6**
Protected method in `_SCallback<ParamType1, ParamType2, ParamType3>`. Invokes the stored static function with three parameters. `m_param1` is moved.

**_SCallback<type-parameter-0-0, type-parameter-0-1, type-parameter-0-2, void>**
Partial specialization of `_SCallback` for 3 parameters.

**_Execute#4**
Protected method in `_SCallback<ParamType1, ParamType2>`. Invokes the stored static function with two parameters. `m_param1` is moved.

**_SCallback<type-parameter-0-0, type-parameter-0-1, void, void>**
Partial specialization of `_SCallback` for 2 parameters.

**_Execute#2**
Protected method in `_SCallback<ParamType1>`. Invokes the stored static function with one parameter. `m_param1` is moved.

**_SCallback<type-parameter-0-0, void, void, void>**
Partial specialization of `_SCallback` for 1 parameter.

**_ICallback<CB>**
Template adapter inheriting from `CB` (a `_Callback` or `_SCallback`) and `ICallback`. Implements `Execute()` by calling `CB::_Execute()`.

**Execute**
Public virtual method in `ICallback`. Pure virtual. Implemented by `_ICallback` to trigger the stored action.

**Callback<Class, ParamType1, ParamType2, ParamType3, ParamType4>**
Public facade for 4-param member function callbacks. Inherits from `_ICallback<_Callback<...>>`.

**Callback<type-parameter-0-0, type-parameter-0-1, type-parameter-0-2, type-parameter-0-3, void>**
Partial specialization of `Callback` for 3 parameters.

**Callback<type-parameter-0-0, type-parameter-0-1, type-parameter-0-2, void, void>**
Partial specialization of `Callback` for 2 parameters.

**Callback<type-parameter-0-0, type-parameter-0-1, void, void, void>**
Partial specialization of `Callback` for 1 parameter.

**Callback<type-parameter-0-0, void, void, void, void>**
Partial specialization of `Callback` for 0 parameters.

**_IQueryCallback<CB>**
Template adapter inheriting from `CB` (a `_Callback` or `_SCallback` expecting `QueryResult` as first param) and `IQueryCallback`. Implements `Execute()`, `SetResult()`, and `GetResult()`.

**Execute#2**
Public virtual method in `IQueryCallback`. Pure virtual. Implemented by `_IQueryCallback`.

**SetResult**
Public virtual method in `IQueryCallback`. Injects a `QueryResult` into the callback's first parameter slot.

**GetResult**
Public virtual method in `IQueryCallback`. Retrieves the stored `QueryResult`.

**QueryCallback<Class, ParamType1, ParamType2, ParamType3>**
Public facade for 3-param member function query callbacks. Inherits from `_IQueryCallback<_Callback<Class, std::unique_ptr<QueryResult>, ...>>`.

**QueryCallback<type-parameter-0-0, type-parameter-0-1, type-parameter-0-2, void>**
Partial specialization of `QueryCallback` for 2 parameters.

**QueryCallback<type-parameter-0-0, type-parameter-0-1, void, void>**
Partial specialization of `QueryCallback` for 1 parameter.

**QueryCallback<type-parameter-0-0, void, void, void>**
Partial specialization of `QueryCallback` for 0 parameters.

**SQueryCallback<ParamType1, ParamType2, ParamType3>**
Public facade for 3-param static function query callbacks. Inherits from `_IQueryCallback<_SCallback<std::unique_ptr<QueryResult>, ...>>`.

**SQueryCallback<type-parameter-0-0, type-parameter-0-1, void>**
Partial specialization of `SQueryCallback` for 2 parameters.

**SQueryCallback<type-parameter-0-0, void, void>**
Partial specialization of `SQueryCallback` for 1 parameter.

**SQueryCallback<>**
Explicit specialization of `SQueryCallback` for 0 parameters.

---

<!-- machine-true, projected from graph.json -->

## Map — DatabaseCallback

*Source:* DatabaseCallback.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| _Execute#9 | function | — | — | — |
| _Callback<Class, ParamType1, ParamType2, ParamType3, ParamType4> | ctor | — | — | — |
| _Execute#7 | function | — | — | — |
| _Callback<type-parameter-0-0, type-parameter-0-1, type-parameter-0-2, type-parameter-0-3, void> | ctor | — | — | — |
| _Execute#5 | function | — | — | — |
| _Callback<type-parameter-0-0, type-parameter-0-1, type-parameter-0-2, void, void> | ctor | — | — | — |
| _Execute#3 | function | — | — | — |
| _Callback<type-parameter-0-0, type-parameter-0-1, void, void, void> | ctor | — | — | — |
| _Execute | function | — | — | — |
| _Callback<type-parameter-0-0, void, void, void, void>#2 | ctor | — | — | — |
| _Callback<type-parameter-0-0, void, void, void, void> | ctor | — | — | — |
| _Execute#8 | function | — | — | — |
| _SCallback<ParamType1, ParamType2, ParamType3, ParamType4> | ctor | — | — | — |
| _Execute#6 | function | — | — | — |
| _SCallback<type-parameter-0-0, type-parameter-0-1, type-parameter-0-2, void> | ctor | — | — | — |
| _Execute#4 | function | — | — | — |
| _SCallback<type-parameter-0-0, type-parameter-0-1, void, void> | ctor | — | — | — |
| _Execute#2 | function | — | — | — |
| _SCallback<type-parameter-0-0, void, void, void> | ctor | — | — | — |
| _ICallback<CB> | ctor | — | — | — |
| Execute | function | — | — | — |
| Callback<Class, ParamType1, ParamType2, ParamType3, ParamType4> | ctor | — | — | — |
| Callback<type-parameter-0-0, type-parameter-0-1, type-parameter-0-2, type-parameter-0-3, void> | ctor | — | — | — |
| Callback<type-parameter-0-0, type-parameter-0-1, type-parameter-0-2, void, void> | ctor | — | — | — |
| Callback<type-parameter-0-0, type-parameter-0-1, void, void, void> | ctor | — | — | — |
| Callback<type-parameter-0-0, void, void, void, void> | ctor | — | — | — |
| _IQueryCallback<CB> | ctor | — | — | — |
| Execute#2 | function | — | — | — |
| SetResult | function | — | — | — |
| GetResult | function | — | — | — |
| QueryCallback<Class, ParamType1, ParamType2, ParamType3> | ctor | — | — | — |
| QueryCallback<type-parameter-0-0, type-parameter-0-1, type-parameter-0-2, void> | ctor | — | — | — |
| QueryCallback<type-parameter-0-0, type-parameter-0-1, void, void> | ctor | — | — | — |
| QueryCallback<type-parameter-0-0, void, void, void> | ctor | — | — | — |
| SQueryCallback<ParamType1, ParamType2, ParamType3> | ctor | — | — | — |
| SQueryCallback<type-parameter-0-0, type-parameter-0-1, void> | ctor | — | — | — |
| SQueryCallback<type-parameter-0-0, void, void> | ctor | — | — | — |

---

<!-- verify: failed-members | invented: SQueryCallback<> -->
