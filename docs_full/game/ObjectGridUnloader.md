# ObjectGridUnloader

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`ObjectGridUnloader` is a transient helper class in `ObjectGridLoader.h` that cleans up a specific grid (`NGridType`) when it is removed from memory. It implements the Visitor pattern to iterate over every cell within the grid, delegating the actual removal of entities (players, creatures, game objects) to `GridLoader`. It works alongside `ObjectGridLoader` (loading) and `ObjectGridStoper` (pausing logic) as part of the spatial partitioning lifecycle. The class holds no persistent state beyond a reference to the target grid.

## Member-by-Member Behavior

### Construction
**`ObjectGridUnloader`**
The constructor accepts a non-const reference to an `NGridType` (`i_grid`). It stores this reference to allow access to the grid’s cell structure during iteration. No other initialization occurs.

### Unloading Logic
**`UnloadN`**
This is the primary entry point for unloading. It executes nested loops iterating `x` and `y` from `0` to `MAX_NUMBER_OF_CELLS - 1`. For each coordinate pair `(x, y)`:
1. It creates a local `GridLoader<Player, AllWorldObjectTypes, AllGridObjectTypes>` instance.
2. It calls `loader.Unload(i_grid(x, y), *this)`.
3. `GridLoader` accesses the specific cell `i_grid(x, y)` and invokes the `ObjectGridUnloader`’s `Visit` methods for each object map contained in that cell.

## Cross-Unit Boundaries

### Called By
- **`Map.Main/UnloadGrid`**: The `Map` class instantiates `ObjectGridUnloader` and calls `UnloadN` when a grid is being purged from the map.

### Calls Out
- **`GridLoader`**: `UnloadN` constructs `GridLoader` instances and calls their `Unload` method. `GridLoader` iterates over the cell’s object maps and dispatches calls to `ObjectGridUnloader`’s `Visit` methods.
- **`NGridType`**: Accessed via `i_grid(x, y)` to retrieve individual cells for processing.

## Data Model

This unit interacts exclusively with in-memory data structures. It does not query or modify any database tables.

## Notable Implementation Details

1.  **Per-Cell `GridLoader` Allocation**: `UnloadN` creates a new `GridLoader` instance for every cell. While `GridLoader` is likely lightweight, this pattern prioritizes simplicity over potential micro-optimizations like reusing a single loader instance.
2.  **Visitor Dispatch**: The class relies on `GridLoader` to identify which object maps exist in a cell and to call the appropriate `Visit` method. `ObjectGridUnloader` itself does not iterate over object types; it only provides the callback interface.
3.  **Fixed Grid Structure**: The loops depend on `MAX_NUMBER_OF_CELLS`, assuming a uniform, fixed subdivision of the grid.

## Member Reference

**ObjectGridUnloader**
Constructor that initializes the `i_grid` reference with the provided `NGridType&`.

**UnloadN**
Iterates over all cells in the grid using nested loops. For each cell, it creates a `GridLoader` instance and calls `loader.Unload(i_grid(x, y), *this)`, delegating the removal of objects within that cell to the `GridLoader` utility.

---

<!-- machine-true, projected from graph.json -->

## Map — ObjectGridUnloader

*Source:* ObjectGridLoader.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ObjectGridUnloader | ctor | — | Map.Main/UnloadGrid | — |
| UnloadN | method | — | Map.Main/UnloadGrid | — |
