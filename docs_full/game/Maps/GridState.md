# GridState

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# GridState

## Purpose & Responsibilities

`GridState` is an abstract base class defining the interface for the State Pattern implementation used to manage the lifecycle and processing behavior of individual grid cells (`NGridType`) within the game world. The game world is partitioned into a grid, and each cell exists in one of several distinct states (Invalid, Active, Idle, Removal). This unit provides the polymorphic contract (`GridState`) and declares four concrete implementations (`InvalidState`, `ActiveState`, `IdleState`, `RemovalState`) that dictate how a grid responds to simulation ticks based on its current status.

The primary responsibility is to decouple the grid iteration loop from conditional logic. The `MapManager` treats all grids uniformly, invoking the `Update` method on the current `GridState` object, which then executes state-specific behavior. This unit contains only declarations; the implementation details reside in corresponding source files.

## Member-by-Member Behavior

### The Abstract Interface: `GridState`

*   **`~GridState`**: A virtual destructor ensuring proper cleanup of derived state objects. It is empty because `GridState` holds no data members.
*   **`Update`**: A pure virtual function declaring the update contract. It accepts references to the parent `Map`, the grid object itself (`NGridType&`), metadata about the grid (`GridInfo&`), the grid coordinates (`x`, `y`), and the time difference since the last update (`t_diff`). Derived classes implement this to define how the grid reacts to a simulation tick.

### Concrete State Implementations

Each derived class (`InvalidState`, `ActiveState`, `IdleState`, `RemovalState`) overrides `Update`. While the method bodies are not present in this header, their roles are defined by their class names and standard state-pattern usage:

*   **`InvalidState`**: Represents a grid cell that is invalid or uninitialized. Its `Update` method likely performs no operation or handles error conditions, serving as a safe placeholder to prevent access violations.
*   **`ActiveState`**: Represents a grid cell containing active entities (players, NPCs). Its `Update` method performs the heavy lifting of the game loop, including entity simulation, visibility checks, and AI ticks for the elapsed time `t_diff`.
*   **`IdleState`**: Represents an empty or inactive grid. Its `Update` method monitors for activity, checking if entities have entered or if conditions require transitioning back to `ActiveState`.
*   **`RemovalState`**: Represents a grid scheduled for unloading. Its `Update` method handles teardown, removing entities and cleaning up resources before the grid object is deleted or reused.

## Cross-Unit Boundaries

### Called By: `MapManager/UpdateGridState`

*   **Direction**: Incoming call.
*   **Collaboration**: The `MapManager` (specifically its `UpdateGridState` member) orchestrates the game world's simulation loop. It iterates through all loaded grids, retrieves the current `GridState` object for each, and invokes its `Update` method.
*   **Data Crossing the Boundary**:
    *   **Input**: `MapManager` passes the `Map` instance, the specific `NGridType` object, its `GridInfo`, coordinates (`x`, `y`), and the time delta (`t_diff`).
    *   **Output**: The state object modifies the internal state of the `NGridType` and potentially the `Map` (e.g., updating entities, visibility lists). `MapManager` relies on these side effects to maintain world consistency.
*   **Why**: This separation allows `MapManager` to remain agnostic of specific grid logic, delegating the "how" to the state objects via virtual dispatch.

## Data Model

This unit does not interact directly with any database tables. All operations are performed in-memory on `Map`, `NGridType`, and `GridInfo` objects. State transitions and updates are transient runtime behaviors.

## Notable Implementation Details

1.  **Pure Virtual Interface**: `GridState` is abstract with a pure virtual `Update` method, enforcing that every concrete state provides an implementation.
2.  **Const Correctness**: `Update` is declared `const`, implying the state object itself does not change internal state during the update. It operates on non-const references to `Map`, `NGridType`, and `GridInfo`, allowing modification of the world state.
3.  **No Data Members**: None of the state classes hold data members. They are purely behavioral, suggesting state-specific data (e.g., timers) is stored in `GridInfo` or `NGridType`. This keeps state objects lightweight and shareable.
4.  **Empty Destructors**: All destructors are explicitly defined but empty, ensuring correct vtable management in the inheritance hierarchy despite no cleanup needs.

## Member Reference

*   **~GridState**: Virtual destructor for the abstract base class. Ensures proper cleanup of derived state objects. Empty body.
*   **Update**: Pure virtual function defining the update contract. Takes `Map&`, `NGridType&`, `GridInfo&`, `uint32 const& x`, `uint32 const& y`, and `uint32 const& t_diff`. Implemented by derived classes to perform state-specific grid processing.

---

<!-- machine-true, projected from graph.json -->

## Map — GridState

*Source:* GridStates.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ~GridState | dtor | — | — | — |
| Update | decl | — | MapManager/UpdateGridState | — |
