# _SCallback

<!-- documentation: model-written from source via the local LLM; review before trusting -->

The `_SCallback` unit in `DatabaseCallback.h` is a family of C++ template classes designed to wrap static function pointers and their arguments into a uniform, executable object. It serves as the foundational building block for asynchronous or deferred execution within the MaNGOS server framework, specifically for scenarios where a static function (rather than a member function) needs to be invoked later with a fixed set of parameters.

### Purpose & Responsibilities

The primary responsibility of `_SCallback` is **function binding**. It captures a pointer to a static function (`void (*)(...)`) and up to four additional parameters at construction time. It then provides a mechanism (`_Execute`) to invoke that function with the captured arguments later.

This unit is part of a larger callback infrastructure (`MaNGOS` namespace) that distinguishes between:
1.  **Member callbacks** (`_Callback`): Invoking methods on specific object instances.
2.  **Static callbacks** (`_SCallback`): Invoking free/static functions.

`_SCallback` is specialized via template partial specialization to support functions taking 0, 1, 2, 3, or 4 arguments. This allows the rest of the system to treat all these different function signatures uniformly through the common interface provided by derived classes like `SQueryCallback` or `_ICallback`.

### Member-by-Member Behavior

The unit consists of five distinct template specializations of the `_SCallback` class, plus two constructors and one method defined within them. The behavior is identical across specializations, differing only in the number of stored parameters.

#### 1. `_Execute`
*   **Kind:** Protected method.
*   **Behavior:** Invokes the stored function pointer (`m_method`) using the stored parameters (`m_param1` through `m_param4`).
*   **Implementation Detail:** The first parameter (`m_param1`) is always moved (`std::move`) into the function call. Subsequent parameters (`m_param2`, etc.) are passed by value/reference depending on the function signature, but notably, they are *not* moved in the `_Execute` call itself, although they were potentially moved during construction in some specializations. This suggests `m_param1` is expected to be a resource-heavy object (like a `std::unique_ptr`) that should be transferred ownership, while other parameters are likely lightweight copies or references.

#### 2. `_SCallback` (Constructors)
*   **Kind:** Public constructor.
*   **Behavior:** Initializes the internal state:
    *   Stores the function pointer in `m_method`.
    *   Stores the arguments in `m_param1` through `m_param4`.
*   **Argument Handling:**
    *   `param1` is always constructed via `std::move(param1)`. This is critical for types like `std::unique_ptr<QueryResult>` used in `SQueryCallback`, ensuring ownership is transferred from the caller to the callback object immediately upon creation.
    *   Other parameters (`param2`, etc.) are copied or moved depending on the specific template instantiation and how the caller passes them, but the storage member is assigned directly.
*   **Copy Constructor:** The zero-parameter specialization (`template<> class _SCallback<>`) explicitly defines a copy constructor. This allows copying of callback objects that hold no parameters, only a function pointer. Other specializations rely on implicitly generated copy constructors, which will copy the stored parameters.

### Cross-Unit Boundaries

While the MAP indicates no direct calls to or from other units for `_SCallback` itself, its design is tightly coupled with the broader callback hierarchy in `DatabaseCallback.h`:

1.  **Called by `_IQueryCallback` and `_ICallback`:**
    *   `_SCallback` is rarely used directly by application code. Instead, it is embedded as a base class within `_IQueryCallback<_SCallback<...>>` and `_ICallback<_SCallback<...>>`.
    *   These wrapper classes inherit from `_SCallback` to gain access to `_Execute` and the stored parameters.
    *   For example, `SQueryCallback` inherits from `_IQueryCallback<_SCallback<std::unique_ptr<QueryResult>, ...>>`. When `SQueryCallback::Execute()` is called, it delegates to `_IQueryCallback::Execute()`, which in turn calls `_SCallback::_Execute()`.

2.  **Integration with `QueryResult`:**
    *   In the context of `SQueryCallback`, the first parameter (`m_param1`) is typically a `std::unique_ptr<QueryResult>`.
    *   The `_Execute` method passes this pointer to the static function. This allows the static function to process the database query result asynchronously.

### Data Model

This unit does not interact directly with any database tables. It operates entirely in memory, managing function pointers and temporary data structures. The `QueryResult` object it often carries is a runtime representation of database results, but `_SCallback` itself performs no SQL operations.

### Notable Implementation Details

1.  **Move Semantics for First Parameter:**
    *   Every specialization moves `param1` into `m_param1` during construction and moves `m_param1` into the function call during `_Execute`. This pattern strongly implies that `param1` is intended to be a unique resource (like a smart pointer) that should not be copied. This is efficient and prevents accidental double-deletion of resources like `QueryResult`.

2.  **Template Specialization Strategy:**
    *   Instead of using variadic templates (which were introduced in C++11 and might not have been fully utilized or preferred in this older codebase), the code uses explicit partial specializations for 0–4 parameters. This increases code size but ensures compatibility with older compilers and provides clear, predictable behavior for each arity.

3.  **No Virtual Dispatch in `_SCallback`:**
    *   `_SCallback` itself has no virtual functions. Polymorphism is achieved by deriving from it into `_ICallback` or `_IQueryCallback`, which provide the virtual `Execute()` interface. This keeps the static callback lightweight until it is wrapped for polymorphic use.

4.  **Thread Safety:**
    *   `_SCallback` contains no synchronization primitives. Thread safety is managed by the higher-level wrappers (e.g., `IQueryCallback` has a `threadSafe` flag). The callback object itself must be passed safely to the thread that will execute it.

## Member Reference

**_Execute**: Protected method that invokes the stored static function pointer (`m_method`) with the stored parameters. `m_param1` is moved into the call; other parameters are passed as-is. This is the core execution engine of the callback.

**_SCallback#2**: Constructor for the 4-parameter specialization (`_SCallback<ParamType1, ParamType2, ParamType3, ParamType4>`). Initializes `m_method` and stores `param1` (moved), `param2`, `param3`, and `param4`.

**_SCallback**: Constructor for various specializations (0, 1, 2, 3 parameters). Initializes `m_method` and stores the respective parameters, always moving `param1` if present. The 0-parameter specialization also includes a copy constructor.

---

<!-- machine-true, projected from graph.json -->

## Map — _SCallback

*Source:* DatabaseCallback.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| _Execute | method | — | — | — |
| _SCallback#2 | ctor | — | — | — |
| _SCallback | ctor | — | — | — |
