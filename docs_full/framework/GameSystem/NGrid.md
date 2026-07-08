<!-- provenance: verbose, failed-members -->
# NGrid

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`NGrid` is a template class that aggregates an $N \times N$ matrix of smaller `Grid` objects into a single logical spatial unit. It serves as a higher-level abstraction for managing large world areas, identified by global coordinates `(x, y)` and a unique ID.

Key responsibilities:
1.  **Spatial Composition:** Managing the array of sub-grids and delegating object operations (add/remove/visit) to the correct cell based on local coordinates.
2.  **Lifecycle Management:** Tracking grid state (`GRID_STATE_ACTIVE`, `IDLE`, etc.) and managing unloading locks to prevent premature memory release while objects are active or explicitly locked.
3.  **Metadata Storage:** Holding the global grid ID, world coordinates, loading status, and timing information via the embedded `GridInfo` struct.

`NGrid` is a pure in-memory data structure; it performs no database operations.

## Member-by-Member Behavior

### Construction and Initialization
*   **`NGrid<N, ACTIVE_OBJECT, WORLD_OBJECT_TYPES, GRID_OBJECT_TYPES>`**: Initializes the grid with a unique ID, world coordinates `(x, y)`, an initial timer expiry, and an unload preference. Sets internal state to `GRID_STATE_INVALID` and marks object data as unloaded. Instantiates the `GridInfo` helper.

### Coordinate and State Accessors
*   **`GetGridId` / `SetGridId`**: Retrieve or update the unique identifier for this `NGrid` instance.
*   **`getX` / `getY`**: Return the world-space coordinates associated with this grid block.
*   **`GetGridState` / `SetGridState`**: Access the current lifecycle state (`grid_state_t`). This state determines whether the grid manager processes updates or unloads the grid.
*   **`isGridObjectDataLoaded` / `setGridObjectDataLoaded`**: Track whether persistent/static objects for this grid have been loaded, preventing redundant loading attempts.

### Cell Access and Delegation
*   **`operator()#2`**: Const accessor for the underlying `Grid` object at local coordinates `(x, y)` within the $N \times N$ matrix. Asserts `x < N` and `y < N`.
*   **`operator()`**: Mutable accessor for the underlying `Grid` object at local coordinates `(x, y)`. Asserts bounds.
*   **`getGridType`**: Private helper performing the same bounds-checked access as `operator()`, used internally by delegation methods.

### Object Management (Delegated)
These methods forward requests to the appropriate sub-grid cell determined by `(x, y)`.

*   **`AddWorldObject` / `RemoveWorldObject`**: Add or remove a dynamic world object from the specific sub-grid cell.
*   **`AddGridObject` / `RemoveGridObject`**: Add or remove a static grid object from the specific sub-grid cell. Returns a boolean indicating success.
*   **`Visit`**: Iterates over all sub-grids (or a specific one) and invokes a visitor pattern on each, allowing algorithms to traverse all objects in the $N \times N$ block.
*   **`ActiveObjectsInGrid`**: Sums the count of active objects across all $N^2$ sub-grids, used to determine if the grid is busy enough to remain loaded.

### Lifecycle and Locking Management
These methods interact with the embedded `GridInfo` struct to control unloading.

*   **`link`**: Links this `NGrid` instance to a `GridRefManager` for reference counting.
*   **`getGridInfoRef`**: Returns a pointer to the internal `GridInfo` struct.
*   **`getTimeTracker`**: Returns the timer tracking grid idle time.
*   **`getUnloadLock`**: Checks if the grid is protected from unloading. Returns true if either the active lock count is non-zero or an explicit lock is set.
*   **`setUnloadExplicitLock`**: Manually enables or disables an explicit lock, used for debugging or forced-loaded zones.
*   **`incUnloadActiveLock` / `decUnloadActiveLock`**: Increment or decrement the reference count for active objects. `decUnloadActiveLock` guards against underflow by checking if the count is greater than zero before decrementing.
*   **`ResetTimeTracker` / `UpdateTimeTracker`**: Manage the idle timer. `Reset` restarts the timer with a new interval; `Update` advances it by a time delta.

## Cross-Unit Boundaries

*   **Calls Out:**
    *   **`Grid` (from `GameSystem/Grid.h`)**: `NGrid` contains an array of `Grid` objects. All object manipulation (`AddWorldObject`, `Visit`, etc.) is delegated to these instances.
    *   **`GridRefManager`**: The `link` method accepts a pointer to a `GridRefManager` to register the grid for reference counting.
    *   **`TimeTracker`**: Used internally via `GridInfo` for timing logic.
    *   **`GridReference`**: Used internally for the intrusive pointer mechanism.

*   **Called By:**
    *   The MAP indicates no external callers are listed. In practice, `NGrid` is managed by the **GridManager**, which calls methods like `SetGridState`, `UpdateTimeTracker`, and `ActiveObjectsInGrid`.

## Data Model

`NGrid` does not interact directly with any database tables. It is an in-memory representation of spatial data. Persistence of grid objects is handled by higher-level systems that load data into `NGrid` via `AddGridObject` after querying the database.

## Notable Implementation Details

1.  **Template Parameters**: Templated on `N` (sub-grid dimension), `ACTIVE_OBJECT`, `WORLD_OBJECT_TYPES`, and `GRID_OBJECT_TYPES` to support different object hierarchies.
2.  **Bounds Checking**: `operator()` and `getGridType` use `assert(x < N)` and `assert(y < N)`. Correct usage requires callers to ensure coordinates are within range; release builds disable these assertions.
3.  **Locking Logic**: `getUnloadLock` combines `i_unloadActiveLockCount` (dynamic) and `i_unloadExplicitLock` (static). This dual-lock system prevents unloading during use while allowing manual overrides.
4.  **Underflow Protection**: `decUnloadActiveLock` checks `if (i_unloadActiveLockCount)` before decrementing to prevent counter wrap-around.
5.  **Visitor Pattern**: `Visit` methods support the Visitor design pattern, separating data storage from processing logic.
6.  **State Machine**: `grid_state_t` defines a lifecycle state machine. `GRID_STATE_REMOVAL` indicates a graceful shutdown process.

## Member Reference

*   **NGrid<N, ACTIVE_OBJECT, WORLD_OBJECT_TYPES, GRID_OBJECT_TYPES>**: Constructor initializing grid ID, coordinates, state, and `GridInfo`.
*   **operator()#2**: Const accessor for sub-grid at `(x, y)`, asserts bounds.
*   **operator()**: Mutable accessor for sub-grid at `(x, y)`, asserts bounds.
*   **GetGridId**: Returns the unique grid ID.
*   **SetGridId**: Sets the unique grid ID.
*   **GetGridState**: Returns the current lifecycle state (`grid_state_t`).
*   **SetGridState**: Updates the lifecycle state.
*   **getX**: Returns the world X coordinate.
*   **getY**: Returns the world Y coordinate.
*   **link**: Registers the grid with a `GridRefManager` for reference counting.
*   **isGridObjectDataLoaded**: Checks if static objects are loaded.
*   **setGridObjectDataLoaded**: Sets the loaded status flag.
*   **getGridInfoRef**: Returns a pointer to the internal `GridInfo` struct.
*   **getTimeTracker**: Returns the idle timer tracker.
*   **getUnloadLock**: Checks if unloading is prevented by active locks or explicit flags.
*   **setUnloadExplicitLock**: Enables/disables manual unloading prevention.
*   **incUnloadActiveLock**: Increments the active object lock counter.
*   **decUnloadActiveLock**: Decrements the active object lock counter, guarding against underflow.
*   **ResetTimeTracker**: Resets the idle timer with a new interval.
*   **UpdateTimeTracker**: Advances the idle timer by a time delta.
*   **ActiveObjectsInGrid**: Sums active objects across all sub-grids.
*   **getGridType**: Private helper to access sub-grid with bounds checking.

---

<!-- machine-true, projected from graph.json -->

## Map — NGrid

*Source:* NGrid.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| NGrid<N, ACTIVE_OBJECT, WORLD_OBJECT_TYPES, GRID_OBJECT_TYPES> | ctor | — | — | — |
| operator()#2 | function | — | — | — |
| operator() | function | — | — | — |
| GetGridId | function | — | — | — |
| SetGridId | function | — | — | — |
| GetGridState | function | — | — | — |
| SetGridState | function | — | — | — |
| getX | function | — | — | — |
| getY | function | — | — | — |
| link | function | — | — | — |
| isGridObjectDataLoaded | function | — | — | — |
| setGridObjectDataLoaded | function | — | — | — |
| getGridInfoRef | function | — | — | — |
| getTimeTracker | function | — | — | — |
| getUnloadLock | function | — | — | — |
| setUnloadExplicitLock | function | — | — | — |
| incUnloadActiveLock | function | — | — | — |
| decUnloadActiveLock | function | — | — | — |
| ResetTimeTracker | function | — | — | — |
| UpdateTimeTracker | function | — | — | — |
| ActiveObjectsInGrid | function | — | — | — |
| getGridType | function | — | — | — |

---

<!-- verify: failed-members | invented: operator -->
