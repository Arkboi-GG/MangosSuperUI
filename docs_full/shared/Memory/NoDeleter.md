<!-- provenance: failed-members -->
# NoDeleter

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# NoDeleter

**Purpose & Responsibilities**

`NoDeleter` is a minimal utility template struct located in `MaNGOS::Memory` that provides a "null" deleter for `std::shared_ptr`. Its sole responsibility is to suppress the default deletion behavior of smart pointers. When a `std::shared_ptr<T>` is constructed with an instance of `no_deleter<T>`, the pointer will manage the lifetime of the object for reference counting purposes but will **not** invoke `delete` on the underlying raw pointer when the last reference is released.

This mechanism is explicitly designed for scenarios where ownership of the allocated memory is transferred to another system or subsystem that is responsible for its eventual cleanup. The header documentation warns that using this deleter without ensuring external management of the memory will result in a memory leak.

**Member-by-Member Behavior**

The unit contains a single member function within the template struct `no_deleter`:

*   **operator()**: This is the call operator required by the `std::shared_ptr` deleter interface. It accepts a constant pointer to type `T` (`T const*`). The body of the function is empty; it performs no operations and does not call `delete`. This effectively makes the deleter a no-op, preventing the automatic destruction of the managed object.

**Cross-Unit Boundaries**

*   **Calls out**: None. The `operator()` function is self-contained and does not invoke any other units.
*   **Called by**: None. As a template struct intended to be passed as a constructor argument to `std::shared_ptr`, its invocation is handled internally by the standard library's smart pointer implementation, not by other units in the MaNGOS codebase directly.

**Data Model**

This unit does not interact with any database tables. It operates purely in memory as part of the C++ standard library integration.

**Notable Implementation Details**

*   **Memory Leak Risk**: The primary characteristic of this unit is that it disables automatic memory reclamation. Engineers using `MaNGOS::Memory::no_deleter` must ensure that the raw pointer passed to the `std::shared_ptr` is eventually freed by some other means (e.g., manual `delete`, placement new/destruction, or transfer to a different owner). Failure to do so guarantees a memory leak.
*   **Template Nature**: `no_deleter` is a template struct `template<typename T>`. This allows it to be used with any type `T`, providing type safety while maintaining the no-op behavior.
*   **Const Correctness**: The `operator()` takes `T const*` as its parameter. This aligns with the typical signature expected by `std::shared_ptr` deleters, though standard deleters often take non-const pointers to allow modification before deletion. Since no deletion occurs, the const qualifier is sufficient and appropriate.

## Member Reference

**operator()**
The call operator for the `no_deleter` struct. It takes a `T const*` argument and performs no action, thereby preventing `std::shared_ptr` from deleting the managed object.

---

<!-- machine-true, projected from graph.json -->

## Map — NoDeleter

*Source:* NoDeleter.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| operator() | function | — | — | — |

---

<!-- verify: failed-members | invented: operator -->
