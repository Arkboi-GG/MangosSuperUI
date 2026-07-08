# CreationPolicy

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# CreationPolicy

**Purpose & Responsibilities**

`CreationPolicy` defines four template classes in the `MaNGOS` namespace that encapsulate distinct strategies for object allocation, construction, and destruction. These policies allow client code to decouple object lifetime management from business logic by swapping the policy type. The unit provides implementations for standard heap allocation (`OperatorNew`), static storage with placement new (`LocalStaticCreation`), C-style memory management (`CreateUsingMalloc`), and custom callback delegation (`CreateOnCallBack`).

## Member-by-Member Behavior

### OperatorNew
Implements standard C++ dynamic memory management.
*   **Create**: Allocates an object of type `T` on the heap using `new` and returns the pointer.
*   **Destroy**: Destroys the object and frees memory using `delete`.

### LocalStaticCreation
Places objects in a static buffer, allowing manual construction/destruction cycles without heap allocation.
*   **Create**: Uses a `static` local variable (`si_localStatic`) of type `MaxAlign` as a pre-allocated buffer. It employs placement `new` to construct `T` within this buffer. The `MaxAlign` union ensures sufficient size and alignment for common types. Calling `Create` repeatedly without `Destroy` overwrites the previous object.
*   **Destroy**: Explicitly calls the destructor for `obj`. It does not free memory, as the buffer has static storage duration.

### CreateUsingMalloc
Separates memory allocation from object construction using C-style primitives.
*   **Create**: Allocates raw memory via `malloc`. If successful, it constructs `T` using placement `new` and returns the pointer; otherwise, it returns `nullptr`.
*   **Destroy**: Explicitly calls the destructor for `p`, then releases memory via `free`.

### CreateOnCallBack
Delegates lifecycle management to a user-provided class.
*   **Create**: Invokes `CALL_BACK::createCallBack()` and returns the result.
*   **Destroy**: Invokes `CALL_BACK::destroyCallBack(p)` with the object pointer.

## Cross-Unit Boundaries

This unit has no runtime dependencies on other MaNGOS units. All members are static functions within template classes. `CreateOnCallBack` depends on a compile-time template parameter `CALL_BACK`, but this is an injection mechanism, not a fixed linkage to another specific unit.

## Data Model

This unit does not interact with any database tables.

## Notable Implementation Details

1.  **Alignment Limits**: `LocalStaticCreation::MaxAlign` assumes `sizeof(T)` fits within the largest member of the union (typically `long double` or a pointer). Larger types cause undefined behavior due to buffer overflow.
2.  **Static Buffer Reuse**: In `LocalStaticCreation`, the static buffer persists for the program's lifetime. Overwriting an object without calling `Destroy` first skips the destructor, potentially leaking resources held by `T`.
3.  **Exception Safety**: None of the policies guarantee exception safety during construction. If `T`'s constructor throws, `OperatorNew` leaks memory (standard `new` behavior), and `CreateUsingMalloc` leaks the `malloc`'d block because `free` is never reached.
4.  **Null Handling**: `CreateUsingMalloc::Create` explicitly checks for `malloc` failure and returns `nullptr`, preventing undefined behavior from placement `new` on a null pointer.

## Member Reference

**Create#3** (in `OperatorNew`): Allocates an object of type `T` on the heap using `new` and returns the pointer.

**Destroy#3** (in `OperatorNew`): Deletes the object pointed to by `obj` using `delete`.

**Create#2** (in `LocalStaticCreation`): Constructs an object of type `T` in a static buffer using placement `new` and returns the pointer.

**Destroy#2** (in `LocalStaticCreation`): Explicitly calls the destructor of the object `obj`; does not free memory.

**Create** (in `CreateUsingMalloc`): Allocates memory using `malloc`, checks for failure, then constructs `T` using placement `new` and returns the pointer.

**Destroy** (in `CreateUsingMalloc`): Explicitly calls the destructor of `p`, then frees the memory using `free`.

**Create#4** (in `CreateOnCallBack`): Calls `CALL_BACK::createCallBack()` and returns the result.

**Destroy#4** (in `CreateOnCallBack`): Calls `CALL_BACK::destroyCallBack(p)` with the object pointer.

---

<!-- machine-true, projected from graph.json -->

## Map — CreationPolicy

*Source:* CreationPolicy.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Create#3 | function | — | — | — |
| Destroy#3 | function | — | — | — |
| Create#2 | function | — | — | — |
| Destroy#2 | function | — | — | — |
| Create | function | — | — | — |
| Destroy | function | — | — | — |
| Create#4 | function | — | — | — |
| Destroy#4 | function | — | — | — |
