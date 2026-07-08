# TypeContainer

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# TypeContainer

**Purpose & Responsibilities**

`TypeContainer` is a low-level, compile-time heterogeneous container system designed to store pointers to objects of different types within a single logical structure. It is a foundational component of the MaNGOS/WowVMaNGOS engine, primarily used to manage game entities (such as creatures, players, items, and game objects) that reside in a specific spatial grid or zone.

The unit provides two distinct container implementations:
1.  **`TypeUnorderedMapContainer`**: A hash-map-based container (`std::unordered_map`) keyed by an `OBJECT_HANDLE`. This allows for O(1) lookup of specific objects by their unique ID. It supports a fixed set of types defined at compile time via a `TypeList`.
2.  **`TypeMapContainer`**: A list-based container using `GridRefManager`. This is likely used for iterating over all objects of various types within a grid cell without needing a specific key. It also supports a fixed set of types via `TypeList`.

Both containers utilize recursive template metaprogramming to decompose a `TypeList<H, T>` (Head and Tail) into individual storage slots for each type. This ensures type safety and avoids the overhead of dynamic type checking (like `dynamic_cast`) during insertion, removal, or lookup. The system assumes that the set of types stored in a container is known at compile time.

**Member-by-Member Behavior**

The unit defines two primary classes, each with a `GetElements` member. These members expose the internal storage structures to external code.

### TypeUnorderedMapContainer

This class manages a collection of objects accessible by a unique key (`KEY_TYPE`, defaulting to `OBJECT_HANDLE`).

*   **`insert`**: Adds an object pointer to the container. It recursively traverses the `TypeList` of supported types. If the object's type matches the current head of the list, it inserts the pointer into the corresponding `std::unordered_map`. If the key already exists, it asserts that the existing pointer is identical to the new one (preventing duplicate entries with different pointers for the same key) and returns `false`. Otherwise, it returns `true`.
*   **`erase`**: Removes an object from the container by key. It recursively searches for the matching type and removes the entry from the `std::unordered_map`.
*   **`find`**: Retrieves a pointer to an object by key. It recursively searches the `TypeList` until it finds the map corresponding to the requested `SPECIFIC_TYPE` and returns the pointer, or `nullptr` if not found.

### TypeMapContainer

This class manages a collection of objects using `GridRefManager`, which typically handles reference counting and safe iteration for grid-based entity management.

*   **`Count`**: Returns the number of objects of a specific `SPECIFIC_TYPE` currently in the container. It delegates to `MaNGOS::Count` (defined in `TypeContainerFunctions.h`, included at the bottom of the file).
*   **`insert`**: Adds an object pointer to the container. It delegates to `MaNGOS::Insert`. The object is added to the `GridRefManager` associated with its specific type.
*   **`remove`**: Removes an object pointer from the container. It delegates to `MaNGOS::Remove`. The object is removed from the `GridRefManager` associated with its specific type.

**Cross-Unit Boundaries**

*   **`TypeList` (from `Utilities/TypeList.h`)**: Both `TypeUnorderedMapContainer` and `TypeMapContainer` rely heavily on `TypeList` to define the set of allowed object types. The recursive template specializations for `ContainerUnorderedMap` and `ContainerMapList` destructure `TypeList<H, T>` to create separate storage for `H` (Head) and recurse on `T` (Tail).
*   **`GridRefManager` (from `GameSystem/GridRefManager.h`)**: `TypeMapContainer` uses `GridRefManager<OBJECT>` as its underlying storage mechanism for each type. This suggests that `TypeMapContainer` is intended for use in contexts where objects need to be managed with reference counting, likely to prevent deletion while being iterated over in a grid.
*   **`TypeContainerFunctions.h`**: Included at the end of `TypeContainer.h`. This file likely contains the implementations of `MaNGOS::Count`, `MaNGOS::Insert`, and `MaNGOS::Remove` used by `TypeMapContainer`. The `TypeUnorderedMapContainer` implements its own inline helper functions for insert/find/erase, so it does not depend on this file for those operations.
*   **`OBJECT_HANDLE`**: Used as the default `KEY_TYPE` for `TypeUnorderedMapContainer`. This is a typedef defined elsewhere (likely in `Platform/Define.h` or similar) representing a unique identifier for game objects.

**Data Model**

This unit does not interact with any database tables. It is purely an in-memory data structure.

**Notable Implementation Details**

1.  **Recursive Template Metaprogramming**: The core logic relies on template specialization to handle `TypeList`. For example, `ContainerUnorderedMap<TypeList<H, T>, KEY_TYPE>` contains two members: `_elements` (for type `H`) and `_TailElements` (for the rest of the list `T`). This recursion terminates at `TypeNull`, which has an empty specialization. This pattern allows the container to hold a heterogeneous set of types while maintaining type safety and compile-time knowledge of the layout.
2.  **Assertion on Duplicate Keys**: In `TypeUnorderedMapContainer::insert`, if a key already exists, the code asserts that the new object pointer is identical to the existing one (`assert(i->second == obj)`). This is a critical invariant: it prevents accidental overwriting of an object pointer with a different one for the same key, which would lead to memory leaks or dangling pointers. If the assertion fails, it indicates a logic error in the caller.
3.  **`TypeNull` Termination**: The `TypeList` is terminated by `TypeNull`. Specializations for `ContainerUnorderedMap<TypeNull, ...>` and `ContainerMapList<TypeNull>` are empty. This allows the recursive templates to stop gracefully.
4.  **Const Correctness**: `TypeMapContainer::GetElements` provides both const and non-const versions, allowing external code to access the internal `ContainerMapList` structure safely.
5.  **Separation of Concerns**: `TypeUnorderedMapContainer` handles keyed access (by ID), while `TypeMapContainer` handles unkeyed, reference-counted storage (by grid). This separation suggests that the engine needs both fast lookup by ID and safe iteration by spatial location.
6.  **No Dynamic Allocation in Container**: The containers themselves do not allocate memory for the objects they store; they only store pointers. The lifetime of the pointed-to objects is managed externally.

## Member Reference

**GetElements** (in `TypeMapContainer`)
Returns a non-const reference to the internal `ContainerMapList<OBJECT_TYPES>` structure. This exposes the raw storage for direct manipulation or iteration by external code.

**GetElements#2** (in `TypeMapContainer`)
Returns a const reference to the internal `ContainerMapList<OBJECT_TYPES>` structure. This allows read-only access to the container's contents.

---

<!-- machine-true, projected from graph.json -->

## Map — TypeContainer

*Source:* TypeContainer.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GetElements | function | — | — | — |
| GetElements#2 | function | — | — | — |
