<!-- provenance: verbose -->
# GridStates

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# GridStates

## Purpose & Responsibilities

`GridStates` implements the State Pattern for managing the lifecycle of spatial grids (`NGridType`) within the `Map` system. It defines four concrete states—`InvalidState`, `ActiveState`, `IdleState`, and `RemovalState`—that dictate how a grid behaves during periodic updates. The primary goal is resource optimization: keeping grids in memory only when necessary, transitioning them to idle when empty, and unloading them from memory when safe.

## Member-by-Member Behavior

### InvalidState
Represents a grid in an undefined or error condition.
*   **`Update`**: A no-op. The grid remains frozen in this state until external code changes its state.

### ActiveState
Represents a grid currently populated with active objects or recently active.
*   **`Update`**:
    1.  Updates the time tracker (`GridInfo::UpdateTimeTracker`).
    2.  If the tracked interval has passed (`TimeTracker::Passed`), it checks for activity:
        *   If the grid contains no active objects (`grid.ActiveObjectsInGrid() == 0`) AND no active objects exist in nearby grids (`Map::ActiveObjectsNearGrid`), it halts all objects in the grid using `ObjectGridStoper::StopN` and transitions the grid to `GRID_STATE_IDLE`.
        *   Otherwise, it resets the grid expiry timer via `Map::ResetGridExpiry` with a 0.1f second delay, keeping the grid active.

### IdleState
A brief transitional state for grids deemed inactive.
*   **`Update`**:
    1.  Resets the grid expiry timer via `Map::ResetGridExpiry`.
    2.  Transitions the grid to `GRID_STATE_REMOVAL`.
    3.  Logs the transition using `Log::Out`, noting the grid coordinates and map ID (`Map::GetId`).

### RemovalState
Represents a grid scheduled for unloading.
*   **`Update`**:
    1.  Checks if an unload lock is held (`GridInfo::getUnloadLock`). If locked, it does nothing.
    2.  If unlocked, it updates the time tracker. If the interval has passed (`TimeTracker::Passed`), it attempts to unload the grid via `Map::UnloadGrid`.
    3.  If `UnloadGrid` fails (returns `false`), it logs the deferral via `Log::Out` and resets the grid expiry timer via `Map::ResetGridExpiry`, remaining in `RemovalState` to retry later.

## Cross-Unit Boundaries

### Calls Out

*   **`GridInfo`**:
    *   `getTimeTracker()`: Retrieves the time tracking object (`ActiveState`, `RemovalState`).
    *   `UpdateTimeTracker(uint32)`: Advances the internal clock (`ActiveState`, `RemovalState`).
    *   `getUnloadLock()`: Checks if the grid is protected from unloading (`RemovalState`).
*   **`Map.Main`**:
    *   `ActiveObjectsNearGrid(uint32, uint32)`: Checks adjacent grids for activity (`ActiveState`).
    *   `ResetGridExpiry(NGridType&, float)`: Resets the timer for the next evaluation (`ActiveState`, `IdleState`, `RemovalState`).
    *   `GetId()`: Retrieves the map ID for logging (`IdleState`, `RemovalState`).
    *   `UnloadGrid(uint32, uint32, bool)`: Performs the actual memory deallocation (`RemovalState`).
*   **`ObjectGridStoper`**:
    *   `ObjectGridStoper(NGridType&)`: Constructor to prepare for stopping objects (`ActiveState`).
    *   `StopN()`: Halts all game objects within the grid (`ActiveState`).
*   **`TimeTracker`**:
    *   `Passed()`: Determines if the configured interval has elapsed (`ActiveState`, `RemovalState`).
*   **`Log.Main`**:
    *   `Out(...)`: Emits debug logs for state transitions and failed unloads (`IdleState`, `RemovalState`).

### Called By

No other units directly call members of `GridStates` according to the map. These states are likely instantiated and stored within `GridInfo` or `NGridType`, with their `Update` methods invoked polymorphically by the grid manager loop.

## Data Model

This unit does not interact with any database tables. All state management is performed in-memory.

## Notable Implementation Details

1.  **Throttled Checks**: `ActiveState::Update` uses `TimeTracker` to throttle expensive activity checks, avoiding per-cycle overhead.
2.  **Immediate Idle Transition**: `IdleState::Update` immediately transitions the grid to `GRID_STATE_REMOVAL`. The `IdleState` is a transient bridge, not a holding pattern.
3.  **Safety Locks**: `RemovalState` respects `GridInfo::getUnloadLock()` to prevent race conditions during unloading.
4.  **Object Stopping**: `ActiveState` uses `ObjectGridStoper` to halt objects before transitioning to idle, ensuring no logic runs on a grid being prepared for removal.
5.  **Log Discrepancy**: `IdleState::Update` logs "moved to IDLE state" but sets the state to `GRID_STATE_REMOVAL`.

## Member Reference

**~InvalidState**
Destructor for `InvalidState`. Default behavior.

**Update#3**
Method `InvalidState::Update`. A no-op function that performs no actions, leaving the grid in an invalid state indefinitely until externally modified.

**Update**
Method `ActiveState::Update`. Throttles activity checks using `GridInfo::UpdateTimeTracker` and `TimeTracker::Passed`. If the grid has no active objects (`grid.ActiveObjectsInGrid() == 0`) and no nearby active objects (`Map::ActiveObjectsNearGrid`), it stops all objects via `ObjectGridStoper::StopN` and transitions the grid to `GRID_STATE_IDLE`. Otherwise, it resets the grid expiry timer via `Map::ResetGridExpiry` with a 0.1f second delay.

**Update#2**
Method `IdleState::Update`. Immediately resets the grid expiry timer via `Map::ResetGridExpiry`, transitions the grid to `GRID_STATE_REMOVAL`, and logs the transition using `Log::Out` with the map ID from `Map::GetId`.

**Update#4**
Method `RemovalState::Update`. Checks if an unload lock is held via `GridInfo::getUnloadLock`. If unlocked, it updates the time tracker and checks if the timeout has passed via `TimeTracker::Passed`. If so, it attempts to unload the grid via `Map::UnloadGrid`. If unloading fails, it logs the deferral via `Log::Out` and resets the grid expiry timer via `Map::ResetGridExpiry`.

---

<!-- machine-true, projected from graph.json -->

## Map — GridStates

*Source:* GridStates.cpp, GridStates.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Update#3 | method | — | — | — |
| Update | method | GridInfo/getTimeTracker, GridInfo/UpdateTimeTracker, Map.Main/ActiveObjectsNearGrid, Map.Main/ResetGridExpiry, ObjectGridStoper/ObjectGridStoper, ObjectGridStoper/StopN, TimeTracker/Passed | — | — |
| ~InvalidState | dtor | — | — | — |
| Update#2 | method | Log.Main/Out, Map.Main/GetId, Map.Main/ResetGridExpiry | — | — |
| Update#4 | method | GridInfo/getTimeTracker, GridInfo/getUnloadLock, GridInfo/UpdateTimeTracker, Log.Main/Out, Map.Main/GetId, Map.Main/ResetGridExpiry, Map.Main/UnloadGrid, TimeTracker/Passed | — | — |
