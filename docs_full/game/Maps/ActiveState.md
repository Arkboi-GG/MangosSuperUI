# ActiveState

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ActiveState

## Purpose & Responsibilities

`ActiveState` is a concrete implementation of the `GridState` abstract base class, representing one of four possible lifecycle states for a game world grid within the MaNGOS server architecture. Specifically, it denotes that a grid is currently loaded in memory and actively processing updates.

The class is part of a State pattern implementation used to manage the lifecycle of spatial grids (`NGridType`) within a `Map`. Its primary responsibility is to define the behavior for updating an active grid, including processing entities, handling time deltas, and potentially transitioning the grid to another state (such as `IdleState` or `RemovalState`) based on activity levels.

As defined in `GridStates.h`, `ActiveState` inherits from `GridState` and implements the pure virtual `Update` method. It contains no data members and relies entirely on the parameters passed to `Update` to perform its logic. The destructor is explicitly declared but empty, indicating that `ActiveState` instances do not manage dynamic resources requiring custom cleanup.

## Member-by-Member Behavior

### **~ActiveState**
This is the destructor for the `ActiveState` class. It is declared as `override` to satisfy the interface contract with `GridState`. The implementation is empty (`{}`), reflecting that `ActiveState` holds no heap-allocated memory or resources that require explicit release. This is consistent with the design of the other state classes (`InvalidState`, `IdleState`, `RemovalState`) in the same header, which also have empty destructors.

## Cross-Unit Boundaries

The `ActiveState` class itself does not initiate calls to other units in the provided map. However, it is designed to be called by the grid management system (likely within `Map.cpp` or a related grid manager unit) via the `Update` method.

*   **Called By:** While not explicitly listed in the "Called by" column of the map for `~ActiveState`, the `Update` method (which is part of the `ActiveState` interface) is called by the grid update loop in the `Map` unit. The `Map` unit iterates over grids and invokes `Update` on their current `GridState` instance.
*   **Calls Out:** The `Update` method implementation (not shown in the header, but implied by the signature) likely interacts with:
    *   `Map`: To access map-level data or trigger map-wide events.
    *   `NGridType`: To update the specific grid's internal state, such as processing creatures, objects, or spell effects.
    *   `GridInfo`: To read or write metadata about the grid, such as its last update time or activity count, which may influence state transitions.

Since the source code provided is only the header file, the exact cross-unit calls made within the `Update` method body are not visible. However, the signature indicates that `ActiveState::Update` receives references to these core components, enabling it to coordinate updates across the map, grid, and info structures.

## Data Model

The `ActiveState` class does not directly interact with any database tables. It operates purely in-memory, managing the runtime state of game world grids. No SQL queries or table references are present in the provided source code.

## Notable Implementation Details

*   **State Pattern:** `ActiveState` is part of a classic State pattern implementation. The `GridState` base class defines the interface (`Update`), and each derived class (`ActiveState`, `IdleState`, etc.) provides specific behavior for that state. This allows the grid management system to change behavior dynamically by swapping the `GridState` pointer without altering the calling code.
*   **Empty Destructor:** The explicit empty destructor `~ActiveState() override {}` is a deliberate choice to ensure proper virtual destruction while signaling that no resource cleanup is needed. This is a common practice in C++ when implementing interfaces with virtual destructors, even if the derived class has no resources to clean up.
*   **Const Correctness:** The `Update` method is marked `const`, indicating that it does not modify the `ActiveState` object itself. Any state changes occur through the referenced parameters (`Map`, `NGridType`, `GridInfo`), preserving the immutability of the state object.
*   **No Data Members:** `ActiveState` contains no member variables. This makes it a lightweight, stateless object that can be safely shared or copied without concern for resource management or synchronization.

## Member Reference

**~ActiveState**: Destructor for the `ActiveState` class. Explicitly declared as `override` to satisfy the `GridState` interface. The implementation is empty, as the class holds no dynamic resources.

---

<!-- machine-true, projected from graph.json -->

## Map — ActiveState

*Source:* GridStates.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ~ActiveState | dtor | — | — | — |
