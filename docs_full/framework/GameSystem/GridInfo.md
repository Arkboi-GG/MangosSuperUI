# GridInfo

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`GridInfo` and `NGrid` manage the lifecycle and spatial containment of game world grids. `GridInfo` tracks idle timers and unload locks to prevent premature removal of grids containing active objects or explicitly protected zones. `NGrid` aggregates an $N \times N$ array of underlying `Grid` objects, providing a unified interface for object insertion, removal, and iteration across the sub-grid cells. It delegates heavy spatial logic to `GameSystem/Grid.h` while maintaining high-level state (ID, coordinates, load status) and coordinating with `GridStates` for memory management.

## Member-by-Member Behavior

### GridInfo
A lightweight struct tracking grid expiration and unload safety.

*   **Constructors (`GridInfo`, `GridInfo#2`):** Initialize the `TimeTracker` and lock fields. The parameterized constructor sets an initial expiry and inverts the `unload` flag to set `i_unloadExplicitLock` (i.e., `unload=true` means the grid is *not* explicitly locked).
*   **`getTimeTracker`:** Exposes the internal timer for inspection by `GridStates/Update`.
*   **`getUnloadLock`:** Returns `true` if the grid is protected from unloading due to either active references (`i_unloadActiveLockCount > 0`) or an explicit manual/config lock. Called by `GridStates/Update#4` to verify safety before removal.
*   **Lock Management (`setUnloadExplicitLock`, `incUnloadActiveLock`, `decUnloadActiveLock`):** Control the unload protection state. `decUnloadActiveLock` guards against underflow by checking the count before decrementing.
*   **Timer Control (`setTimer`, `ResetTimeTracker`, `UpdateTimeTracker`):** Manage the idle timer. `UpdateTimeTracker` advances the timer and is called by `GridStates/Update` to track how long a grid has been inactive.

### NGrid
A template class representing a macro-grid of $N \times N$ sub-cells.

*   **Initialization:** The constructor initializes grid metadata (ID, coordinates) and creates the `GridInfo` instance.
*   **State & Metadata Accessors:** `GetGridId`, `SetGridId`, `getX`, `getY`, `GetGridState`, `SetGridState`, `isGridObjectDataLoaded`, and `setGridObjectDataLoaded` manage the grid's identity, lifecycle state (`grid_state_t`), and data loading status.
*   **Sub-Grid Access:** `operator()` and the private `getGridType` provide bounds-checked access to the `i_cells[N][N]` array. Assertions enforce valid indices.
*   **Object Management:** `AddWorldObject`, `RemoveWorldObject`, `AddGridObject`, and `RemoveGridObject` delegate object operations to the specific sub-cell identified by `(x, y)`. `ActiveObjectsInGrid` aggregates active object counts from all sub-cells, aiding the grid manager in determining emptiness.
*   **Iteration:** `Visit` overloads allow traversing objects in either a specific sub-cell or the entire $N \times N$ grid using the visitor pattern, delegating to underlying `GridType::Visit`.
*   **Linking:** `link` integrates the grid into the reference-counting system via `GridReference`, enabling safe shared ownership.
*   **Delegation:** Methods like `getGridInfoRef`, `getTimeTracker`, `getUnloadLock`, and timer controls forward directly to the internal `i_GridInfo` instance, exposing lifecycle controls to `GridStates`.

## Cross-Unit Boundaries

*   **`GridStates/Update` & `GridStates/Update#4`:**
    *   **Direction:** Called by `GridStates`.
    *   **Collaboration:** `GridStates` drives the grid lifecycle loop. It calls `UpdateTimeTracker` to age grids, `getTimeTracker` to check expiration, and `getUnloadLock` to confirm a grid is safe to remove. This keeps `NGrid` passive regarding its own destruction.
*   **`GameSystem/Grid.h` (`Grid` class):**
    *   **Direction:** Called by `NGrid`.
    *   **Collaboration:** `NGrid` contains `GridType i_cells[N][N]`. All spatial storage and retrieval logic is delegated to these instances.
*   **`GridReference.h` (`GridReference` class):**
    *   **Direction:** Called by `NGrid`.
    *   **Collaboration:** Used in `link` to manage reference counts and neighbor relationships, preventing circular dependencies.

## Data Model

This unit interacts with no database tables. It manages purely in-memory state for grid lifecycle and object containment.

## Notable Implementation Details

*   **Bitfield Packing:** `GridInfo` uses bitfields (`uint16` for active lock count, `bool` for explicit lock) to minimize memory footprint, critical as every grid instance holds one.
*   **Underflow Safety:** `decUnloadActiveLock` checks `if (i_unloadActiveLockCount)` before decrementing, preventing wrap-around errors that would permanently lock a grid.
*   **Compile-Time Sizing:** The template parameter `N` fixes the sub-grid array size at compile time, avoiding dynamic allocation overhead for the cell structure.
*   **Assertion Bounds Checking:** `operator()` uses `assert` for index validation, treating out-of-bounds access as a developer error rather than a recoverable runtime condition.

## Member Reference

**GridInfo**: Default constructor initializing timer to 0 and locks to false/0.

**GridInfo#2**: Parameterized constructor initializing timer to `expiry` and explicit lock to `!unload`.

**getTimeTracker**: Returns const reference to `i_timer`. Called by `GridStates/Update` and `GridStates/Update#4`.

**getUnloadLock**: Returns true if `i_unloadActiveLockCount` > 0 or `i_unloadExplicitLock` is true. Called by `GridStates/Update#4`.

**setUnloadExplicitLock**: Sets `i_unloadExplicitLock` to the given boolean.

**incUnloadActiveLock**: Increments `i_unloadActiveLockCount`.

**decUnloadActiveLock**: Decrements `i_unloadActiveLockCount` if non-zero.

**setTimer**: Assigns `pTimer` to `i_timer`.

**ResetTimeTracker**: Resets `i_timer` with the given interval.

**UpdateTimeTracker**: Updates `i_timer` with the given time difference. Called by `GridStates/Update` and `GridStates/Update#4`.

---

<!-- machine-true, projected from graph.json -->

## Map — GridInfo

*Source:* NGrid.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GridInfo | ctor | — | — | — |
| GridInfo#2 | ctor | — | — | — |
| getTimeTracker | method | — | GridStates/Update, GridStates/Update#4 | — |
| getUnloadLock | method | — | GridStates/Update#4 | — |
| setUnloadExplicitLock | method | — | — | — |
| incUnloadActiveLock | method | — | — | — |
| decUnloadActiveLock | method | — | — | — |
| setTimer | method | — | — | — |
| ResetTimeTracker | method | — | — | — |
| UpdateTimeTracker | method | — | GridStates/Update, GridStates/Update#4 | — |
