# ObjectGridStoper

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`ObjectGridStoper` is a visitor-style helper class used to halt the processing of entities (creatures and game objects) within a specific grid (`NGridType`). It is part of the grid lifecycle management system, ensuring that active entities are properly stopped—typically pausing AI, movement, or updates—before the grid is unloaded or transitioned to a different state. The class implements the Visitor pattern, allowing a `GridLoader` to traverse the grid's cells and delegate the specific "stop" actions for each object type to `ObjectGridStoper`.

## Member-by-Member Behavior

### Initialization
**`ObjectGridStoper`**
The constructor accepts a reference to an `NGridType` object (`i_grid`). This reference is stored internally and defines the scope of the stopping operation. No additional state or counters are initialized.

### Core Stopping Logic
**`StopN`**
This method orchestrates the stopping of all objects within the entire grid. It iterates over every cell in the grid using nested loops bounded by `MAX_NUMBER_OF_CELLS` for both X and Y axes. For each cell `(x, y)`:
1. It instantiates a local `GridLoader<Player, AllWorldObjectTypes, AllGridObjectTypes>`.
2. It calls `loader.Stop(i_grid(x, y), *this)`, passing the current cell and the `ObjectGridStoper` instance as the visitor.
3. The `GridLoader` traverses the objects within that cell and invokes the appropriate `Visit` methods on the `ObjectGridStoper` for each object type found.

### Visitor Methods
The class provides `Visit` methods to handle specific object maps. While the detailed implementations of `Visit(CreatureMapType&)` and `Visit(GameObjectMapType&)` are not visible in the header, their signatures indicate they receive maps of creatures and game objects, respectively, and are responsible for stopping the entities contained therein.

A template method `Visit(GridRefManager<NONACTIVE>&)` is provided as a no-op for non-active object managers. This ensures that object types not relevant to the stopping process are ignored efficiently without requiring explicit checks.

### Placeholder/Declaration
**`MoveToRespawnN`**
This method is declared but not implemented in this header. It likely prepares the grid for a respawn state, possibly moving objects to a different logical state or location. Its implementation resides elsewhere.

## Cross-Unit Boundaries

### Calls Out
*   **`GridLoader`**: `StopN` creates instances of `GridLoader<Player, AllWorldObjectTypes, AllGridObjectTypes>` and calls its `Stop` method. This delegates the traversal of the grid's internal data structures to the `GridLoader`, which then calls back into `ObjectGridStoper`'s `Visit` methods.
*   **`NGridType`**: The constructor and `StopN` access the `i_grid` reference to retrieve individual cells via operator `()`.

### Called By
*   **`GridStates/Update`**: According to the MAP, `ObjectGridStoper` is instantiated and its `StopN` method is called by the `GridStates/Update` unit. This indicates that stopping a grid is part of the broader grid state transition or update cycle, likely occurring when a grid is being deactivated or prepared for unloading.

## Data Model

This unit does not interact directly with any database tables. It operates entirely on in-memory game objects (`Creature`, `GameObject`) managed within the grid structures.

## Notable Implementation Details

1.  **Local `GridLoader` Instantiation**: In `StopN`, a new `GridLoader` instance is created for *every* cell. This suggests that the `GridLoader` might hold temporary state or that the design prioritizes simplicity over potential optimization of reusing a single loader instance.
2.  **Visitor Pattern**: The class strictly adheres to the Visitor pattern. The `GridLoader` drives the traversal, and `ObjectGridStoper` provides the specific actions for each object type. This decouples the traversal logic from the action logic.
3.  **No-Op for Non-Active Objects**: The template `Visit` method for `GridRefManager` is empty. This is a deliberate design choice to ignore object types that do not need to be stopped, avoiding unnecessary checks or operations.
4.  **Missing Implementation**: The `MoveToRespawnN` method is declared but not defined in this header. Its implementation likely resides in a corresponding `.cpp` file or another partial, but its absence here means `ObjectGridStoper` cannot currently perform respawn-related movements unless that functionality is added elsewhere.

## Member Reference

**ObjectGridStoper**
Constructor that initializes the `ObjectGridStoper` with a reference to the `NGridType` grid it will operate on.

**MoveToRespawnN**
Declared method intended to move objects to a respawn state. Implementation is not present in this unit.

**StopN**
Iterates over all cells in the grid, creating a `GridLoader` for each cell and invoking `Stop` on it with the `ObjectGridStoper` instance as the visitor. This triggers the stopping of all relevant objects within the grid.

---

<!-- machine-true, projected from graph.json -->

## Map — ObjectGridStoper

*Source:* ObjectGridLoader.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ObjectGridStoper | ctor | — | GridStates/Update | — |
| MoveToRespawnN | decl | — | — | — |
| StopN | method | — | GridStates/Update | — |
