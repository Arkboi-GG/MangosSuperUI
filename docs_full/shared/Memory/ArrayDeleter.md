<!-- provenance: failed-members -->
# ArrayDeleter

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`ArrayDeleter` (defined in `ArrayDeleter.h`) provides a custom deleter functor for use with `std::shared_ptr`. Its specific responsibility is to ensure that memory allocated via `new[]` (array allocation) is correctly deallocated using `delete[]`, rather than the scalar `delete` operator.

This utility addresses a limitation noted in the code comments regarding C++14 standards support within the MaNGOS codebase: standard `std::shared_ptr` did not natively support array types (`T[]`) with automatic correct deletion semantics in older standard library implementations or strict pre-C++17 contexts. By providing this explicit deleter, the codebase allows developers to manage dynamic arrays with reference-counted smart pointers safely.

## Member-by-Member Behavior

The unit consists of a single template struct, `MaNGOS::Memory::array_deleter<T>`, containing one callable member.

### Deletion Logic

*   **`operator()`**: This function implements the deletion logic. It accepts a pointer to a constant array of type `T` (`T const* p`). Inside the body, it executes `delete[] p;`. This ensures that the destructor is called for each element in the array and that the memory block is freed correctly according to C++ rules for array allocations. The parameter is marked `const` because the deleter does not need to modify the pointed-to data, only release the memory resource.

## Cross-Unit Boundaries

According to the provided MAP, `ArrayDeleter` has no outgoing calls to other units and is not listed as being called by other specific units in the cross-reference map. However, its design intent is to be passed as a template argument or constructor parameter to `std::shared_ptr` instances elsewhere in the codebase. It acts as a leaf node in the dependency graph, relying solely on the C++ runtime's memory management operators.

## Data Model

This unit does not interact with any database tables. It operates entirely on heap memory managed by the C++ runtime.

## Notable Implementation Details

*   **Const Correctness**: The `operator()` takes `T const* p`. While `delete[]` itself does not require the pointer to be non-const, this signature aligns with the typical usage pattern where the shared_ptr might hold a const view of the array, or simply reflects defensive coding practices.
*   **Template Nature**: The struct is templated on `T`, allowing it to be reused for any data type (`uint8_t`, `char`, custom structs, etc.).
*   **Namespace Organization**: It resides in `MaNGOS::Memory`, indicating it is part of a broader set of memory management utilities within the project.
*   **C++ Standard Context**: The comment explicitly mentions C++14 limitations. In modern C++ (C++17 and later), `std::shared_ptr<T[]>` supports array deletion natively without a custom deleter. This unit exists primarily for compatibility with the compiler standards targeted by this version of MaNGOS.

## Member Reference

**operator()**
A template member function of `array_deleter<T>` that performs array deallocation. It accepts a `T const*` pointer and invokes `delete[]` on it. This ensures proper cleanup of dynamically allocated arrays when used with `std::shared_ptr`.

---

<!-- machine-true, projected from graph.json -->

## Map — ArrayDeleter

*Source:* ArrayDeleter.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| operator() | function | — | — | — |

---

<!-- verify: failed-members | invented: operator -->
