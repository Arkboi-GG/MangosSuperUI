<!-- provenance: verbose -->
# ObjectRegistry

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`ObjectRegistry` is a templated, owning container managing the lifetime and lookup of objects of type `T` identified by a unique `Key` (defaulting to `std::string`). Designed as a Singleton via `MaNGOS::OperatorNew`, it provides a centralized registry for game entities or configuration data. The class assumes ownership of all inserted pointers, automatically deleting them upon removal or registry destruction.

## Member-by-Member Behavior

### Construction and Destruction

*   **`ObjectRegistry<T, Key>`**: Protected constructor initializing an empty `i_registeredObjects` map. Direct instantiation is prevented; the class relies on `MaNGOS::OperatorNew` (granted friend access) to enforce Singleton semantics.
*   **`~ObjectRegistry<T, Key>`**: Destructor that iterates through `i_registeredObjects`, deleting every stored object pointer, and then clears the map. This ensures complete cleanup of owned resources when the registry is destroyed.

### Item Management

*   **`InsertItem`**: Inserts a pointer `obj` with `key`. If `key` already exists:
    *   If `replace` is `false` (default), returns `false` and leaves the registry unchanged.
    *   If `replace` is `true`, deletes the existing object, erases the entry, and inserts the new object.
    *   If `key` does not exist, inserts the new object. Returns `true` on success.
*   **`RemoveItem`**: Removes the entry for `key`. If `delete_object` is `true` (default), it deletes the pointed-to object before erasing the map entry. If the key is missing, no action occurs.
*   **`HasItem`**: Returns `true` if `key` exists in the registry, `false` otherwise.

### Retrieval

*   **`GetRegistryItem`**: Returns a `const T*` for the given `key`, or `nullptr` if not found.
*   **`GetRegisteredItems` (vector overload)**: Appends all current keys to the provided `std::vector<Key>`. It resizes the vector to accommodate existing content plus all registry keys, then copies keys from the map. Returns the total number of items in the registry.
*   **`GetRegisteredItems` (map overload)**: Returns a `const` reference to the internal `std::map<Key, T*>`, allowing efficient iteration without copying keys.

## Cross-Unit Boundaries

The MAP indicates no external calls or callers for this unit. However, the header includes `Policies/Singleton.h` and declares `friend class MaNGOS::OperatorNew<ObjectRegistry<T, Key> >`. This grants the Singleton infrastructure access to the protected constructor, enabling the creation of a single global instance per template specialization. All interaction with this registry occurs through this Singleton instance.

## Data Model

This unit does not interact with any database tables. It operates entirely in memory using `std::map`.

## Notable Implementation Details

1.  **Strict Ownership Contract**: The registry deletes all objects it stores. Callers must pass heap-allocated pointers. If an object needs to be removed without deletion, `RemoveItem(key, false)` must be used. Failure to adhere to this contract causes double-free errors or memory leaks.
2.  **Replacement Safety**: `InsertItem(..., true)` deletes the old object before inserting the new one. Callers must ensure no other part of the system holds a valid pointer to the replaced object, as it becomes dangling immediately after replacement.
3.  **Thread Safety**: The class uses `std::map` without synchronization primitives. It is **not thread-safe**. Concurrent access from multiple threads requires external locking.
4.  **Inefficient Vector Copy**: The vector overload of `GetRegisteredItems` copies all keys into a vector. For large registries, this is costly compared to iterating the map returned by the second overload. The code comment explicitly labels this "Inefficiently."

## Member Reference

**GetRegistryItem**
Returns a `const T*` for the specified `key`, or `nullptr` if the key is not found in the internal map.

**InsertItem**
Inserts `obj` with `key`. If `key` exists and `replace` is `false`, returns `false`. If `replace` is `true`, deletes the existing object, erases the entry, and inserts the new one. Returns `true` on success.

**RemoveItem**
Removes the entry for `key`. If `delete_object` is `true` (default), deletes the object pointer before erasing the entry. No action if `key` is missing.

**HasItem**
Returns `true` if `key` exists in the registry, `false` otherwise.

**GetRegisteredItems**
Appends all registry keys to the provided `std::vector<Key>`, resizing the vector as needed. Returns the total count of items in the registry.

**GetRegisteredItems#2**
Returns a `const` reference to the internal `std::map<Key, T*>`, enabling direct iteration over the registry contents.

**ObjectRegistry<T, Key>**
Protected constructor initializing an empty map. Enforces Singleton instantiation via friend access to `MaNGOS::OperatorNew`.

**~ObjectRegistry<T, Key>**
Deletes all stored object pointers and clears the map to prevent memory leaks upon destruction.

---

<!-- machine-true, projected from graph.json -->

## Map — ObjectRegistry

*Source:* ObjectRegistry.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GetRegistryItem | function | — | — | — |
| InsertItem | function | — | — | — |
| RemoveItem | function | — | — | — |
| HasItem | function | — | — | — |
| GetRegisteredItems | function | — | — | — |
| GetRegisteredItems#2 | function | — | — | — |
| ObjectRegistry<T, Key> | ctor | — | — | — |
| ~ObjectRegistry<T, Key> | dtor | — | — | — |
