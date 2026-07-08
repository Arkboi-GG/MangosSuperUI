<!-- provenance: verbose -->
# FactoryHolder

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# FactoryHolder

`FactoryHolder` is a template base class implementing a self-registering abstract factory pattern. It enables derived classes to register themselves with a global singleton registry (`FactoryHolderRepository`) identified by a unique `Key` (default `std::string`). Clients retrieve registered factories by key and invoke the pure virtual `Create` method to instantiate objects of type `T` without coupling to concrete types.

The unit also defines `Permissible`, a standalone interface for capability scoring. Implementations return an integer priority for a given input object, allowing systems to select the best handler among candidates. `Permissible` is unrelated to `FactoryHolder` but co-located for convenience.

This unit performs no database I/O, network communication, or game-specific logic. It is a pure C++ infrastructure component.

## Member-by-Member Behavior

### Registration and Lifecycle

**`FactoryHolder<T, Key>`**  
Constructs the holder, storing the provided `Key` in `i_key`. Does not register the instance.

**`~FactoryHolder<T, Key>`**  
Virtual destructor for safe polymorphic deletion. Does not deregister the instance.

**`key()`**  
Returns the stored `i_key`.

**`RegisterSelf()`**  
Inserts `this` into the global `FactoryHolderRepository` singleton (backed by `ObjectRegistry`) using `i_key`. Makes the factory discoverable.

**`DeregisterSelf()`**  
Removes `this` from the `FactoryHolderRepository`. Passes `false` to `RemoveItem`, indicating the registry should not delete the object.

### Factory Creation

**`Create()`**  
Pure virtual method. Derived classes implement this to allocate and return a new `T*`. Accepts optional `void* data` for construction context. Callers own the returned pointer.

### Capability Scoring

**`~Permissible<T>`**  
Virtual destructor for the `Permissible` interface.

**`Permit()`**  
Pure virtual method. Derived classes implement this to return an `int` score indicating suitability for handling a given `T*`. Higher scores imply higher priority.

## Cross-Unit Boundaries

-   **`ObjectRegistry`**: `FactoryHolder` calls `ObjectRegistry::InsertItem` and `RemoveItem` via the `FactoryHolderRepository` singleton. `ObjectRegistry` manages the storage and lookup of factory instances by key.
-   **`MaNGOS::Singleton`**: `FactoryHolder` uses `MaNGOS::Singleton` to ensure a single global `FactoryHolderRepository` instance exists.

Other units inherit from `FactoryHolder` to provide concrete factories, calling `RegisterSelf` during initialization. Clients retrieve factories from the repository and call `Create`.

## Data Model

This unit does not interact with any database tables.

## Notable Implementation Details

-   **Explicit Registration**: Factories must call `RegisterSelf` explicitly; construction does not trigger registration.
-   **Manual Ownership**: `Create` returns raw pointers; callers manage lifetime. `DeregisterSelf` does not delete the factory object.
-   **Generic Key**: The `Key` template parameter allows non-string identifiers if needed.
-   **Independent Interfaces**: `Permissible` is not part of the factory hierarchy; it is a separate interface for priority-based selection.

## Member Reference

**`FactoryHolder<T, Key>`**  
Constructor storing the provided `Key` in `i_key`. Does not register the instance.

**`~FactoryHolder<T, Key>`**  
Virtual destructor. Does not deregister the instance.

**`key()`**  
Returns the stored `i_key`.

**`RegisterSelf()`**  
Inserts `this` into the global `FactoryHolderRepository` singleton using `i_key`.

**`DeregisterSelf()`**  
Removes `this` from the `FactoryHolderRepository` singleton, passing `false` to prevent deletion.

**`Create()`**  
Pure virtual method for derived classes to implement object instantiation, returning a `T*`.

**`~Permissible<T>`**  
Virtual destructor for the `Permissible` interface.

**`Permit()`**  
Pure virtual method for derived classes to implement capability scoring, returning an `int` priority for a given `T*`.

---

<!-- machine-true, projected from graph.json -->

## Map — FactoryHolder

*Source:* FactoryHolder.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| FactoryHolder<T, Key> | ctor | — | — | — |
| ~FactoryHolder<T, Key> | dtor | — | — | — |
| key | function | — | — | — |
| RegisterSelf | function | — | — | — |
| DeregisterSelf | function | — | — | — |
| Create | decl | — | — | — |
| ~Permissible<T> | dtor | — | — | — |
| Permit | decl | — | — | — |
