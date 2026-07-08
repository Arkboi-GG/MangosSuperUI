# ThreadSpecificPtr

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`ThreadSpecificPtr` provides a mechanism for storing thread-local data associated with specific class instances, rather than just the current thread. In standard C++, `thread_local` variables are tied to the thread of execution, meaning all instances of a class share the same thread-local storage if they access the same variable. This limitation prevents having distinct thread-specific state for different objects of the same type.

This unit solves that by implementing a registry pattern using a `thread_local` holder (`ThreadSpecificHolder`) that maps class instance addresses (`void*`) to their respective thread-specific objects (`void*`). It acts as a drop-in replacement for libraries like `boost::thread_specific_ptr` or `ACE_TSS`, allowing developers to attach unique objects to specific instances within the context of the current thread. The design ensures that `instanceA` and `instanceB` on the same thread maintain separate thread-specific objects, while `instanceA` on Thread 1 and `instanceA` on Thread 2 also maintain separate objects.

## Member-by-Member Behavior

The functionality is split between the storage container (`ThreadSpecificHolder`) and the user-facing smart pointer interface (`ThreadSpecificPtr<T>`).

### Storage Management: `ThreadSpecificHolder`

*   **`~ThreadSpecificHolder`**: This destructor performs a critical integrity check. It iterates through the `thread_specific_ptr_data` map and asserts that every stored pointer is `nullptr`. This enforces the contract that all thread-specific objects must be explicitly released or reset to `nullptr` before the thread exits (and thus before the `thread_local` holder is destroyed). If any non-null pointers remain, it triggers an assertion failure via `Errors/PrintStacktraceAndThrow`, preventing memory leaks and indicating improper cleanup by the caller.

### Interface: `ThreadSpecificPtr<T>`

*   **`ThreadSpecificPtr<T>` (Constructor)**: Default constructor. Initializes the object but does not allocate any thread-specific storage yet. Storage is lazily created upon the first call to `reset`.
*   **`~ThreadSpecificPtr<T>` (Destructor)**: Intentionally empty. The comment in the source explains that calling `reset()` here is unsafe because the global `thread_local` holder (`gtl_ThreadSpecificPtrHolder`) may have already been destroyed if the `ThreadSpecificPtr` itself is a global or static object. Therefore, the responsibility for cleaning up the managed resource lies entirely with the user, who must ensure `release()` or `reset(nullptr)` is called before thread termination.
*   **`get`**: Retrieves the raw pointer to the thread-specific object associated with this instance. It looks up the address of `this` in the `thread_specific_ptr_data` map. If found, it casts the stored `void*` back to `T*` and returns it; otherwise, it returns `nullptr`.
*   **`operator->`**: Provides pointer-like syntax for accessing members of the managed object. It simply delegates to `get()`. Note that it does not perform null-checking; dereferencing a null result is undefined behavior.
*   **`release`**: Detaches the managed object from this `ThreadSpecificPtr`. It finds the entry for `this` in the map, sets the stored value to `nullptr` (effectively removing the association from the perspective of future `get` calls), and returns the raw pointer. The caller assumes ownership and is responsible for deleting the returned object.
*   **`reset`**: Updates the thread-specific object for this instance. If an object already exists for this instance on this thread, it deletes the old object (unless `new_value` is provided, in which case it replaces it). If no object exists, it inserts a new entry into the map mapping `this` to `new_value`. This is the primary method for initializing or updating the thread-specific state.

## Cross-Unit Boundaries

*   **Calls out**:
    *   `~ThreadSpecificHolder` calls into **`Errors/PrintStacktraceAndThrow`** (via `MANGOS_ASSERT`). This occurs only if the integrity check fails (i.e., non-null pointers remain in the map during destruction). This integration ensures that misuse of the API results in a clear crash with a stack trace rather than silent memory corruption or leaks.
*   **Called by**:
    *   No external units are listed as callers in the MAP. This suggests `ThreadSpecificPtr` is a utility class used internally by other components, likely embedded as members within larger classes that require thread-local instance state. Its usage is implicit through the member functions of those containing classes.

## Data Model

This unit does not interact with any database tables. All state is held in memory within the `thread_local` `ThreadSpecificHolder` instance.

## Notable Implementation Details

1.  **Manual Memory Management Contract**: Unlike `std::unique_ptr` or `std::shared_ptr`, `ThreadSpecificPtr` does not automatically clean up its managed resource in its destructor. The comment in `~ThreadSpecificPtr` explicitly warns against calling `reset()` in the destructor due to potential ordering issues with the `thread_local` holder's lifetime. Users **must** manually call `release()` or `reset(nullptr)` before the thread ends. Failure to do so will trigger an assertion in `~ThreadSpecificHolder`.
2.  **Instance Identity via Address**: The map key is `const_cast<void*>(static_cast<const void*>(this))`. This relies on the assumption that the address of the `ThreadSpecificPtr` object remains stable and unique for the lifetime of the object. If a `ThreadSpecificPtr` is moved or copied (though `NoCopyNoMove` policy prevents copying/moving), the address would change, breaking the association. The `NoCopyNoMove` policy enforces this constraint.
3.  **Thread Safety**: The `thread_local` keyword ensures that each thread has its own independent `ThreadSpecificHolder`. Therefore, operations on `ThreadSpecificPtr` are inherently thread-safe with respect to other threads, as they operate on disjoint memory. However, there is no synchronization within a single thread, which is sufficient since `thread_local` storage is accessed only by the owning thread.
4.  **Map Lookup Overhead**: Every access (`get`, `reset`, `release`) involves a lookup in `std::map` using the instance address as the key. While `std::map` offers logarithmic time complexity, this is less efficient than direct `thread_local` variable access. This trade-off is accepted for the flexibility of instance-specific thread-local storage.
5.  **Null Pointer Dereference Risk**: `operator->` does not check if `get()` returns `nullptr`. Calling `ptr->member` when the thread-specific object has not been initialized or has been released will result in undefined behavior. Developers must ensure the object is initialized via `reset` before use.

## Member Reference

**~ThreadSpecificHolder**: Destructor for the thread-local holder. Iterates over `thread_specific_ptr_data` and asserts that all values are `nullptr`. If any non-null pointer is found, it calls `Errors/PrintStacktraceAndThrow` to indicate a memory leak or improper cleanup.

**ThreadSpecificPtr<T>**: Default constructor for the template class. Initializes the object without allocating any thread-specific storage. Inherits from `NoCopyNoMove` to prevent copying or moving, ensuring instance identity via address remains valid.

**~ThreadSpecificPtr<T>**: Default destructor. Intentionally empty to avoid accessing the potentially destroyed `thread_local` holder. Relies on the user to clean up resources manually.

**get**: Retrieves the raw pointer to the thread-specific object. Looks up the address of `this` in the `thread_specific_ptr_data` map. Returns the casted `T*` if found, or `nullptr` if no object is associated with this instance on the current thread.

**operator->**: Delegates to `get()` to provide pointer-like access to the managed object. Does not perform null checks.

**release**: Detaches the managed object from this instance. Finds the entry for `this` in the map, sets the stored value to `nullptr`, and returns the raw `T*` pointer. The caller assumes ownership and must delete the returned object.

**reset**: Sets or updates the thread-specific object for this instance. If an existing object is found, it deletes it (unless `new_value` is provided, in which case it replaces it). If no entry exists, it inserts a new mapping from `this` to `new_value` into the `thread_specific_ptr_data` map.

---

<!-- machine-true, projected from graph.json -->

## Map — ThreadSpecificPtr

*Source:* ThreadSpecificPtr.cpp, ThreadSpecificPtr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ~ThreadSpecificHolder | dtor | Errors/PrintStacktraceAndThrow | — | — |
| ThreadSpecificPtr<T> | decl | — | — | — |
| ~ThreadSpecificPtr<T> | decl | — | — | — |
| get | function | — | — | — |
| operator-> | function | — | — | — |
| release | function | — | — | — |
| reset | function | — | — | — |
