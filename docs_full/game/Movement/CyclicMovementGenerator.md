<!-- provenance: verbose -->
# CyclicMovementGenerator

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# CyclicMovementGenerator

## Purpose & Responsibilities

`CyclicMovementGenerator` drives continuous, looping waypoint traversal for `Creature` objects. It retrieves a predefined path from `WaypointManager`, initiates movement along that sequence, and automatically restarts the loop upon completion. The generator distinguishes between an initial approach phase (if the creature spawns far from the path start) and the steady-state loop execution. It manages the creature’s roaming state flags (`UNIT_STATE_ROAMING`, `UNIT_STATE_ROAMING_MOVE`) throughout its lifecycle.

## Member-by-Member Behavior

### Lifecycle and State

**`CyclicMovementGenerator`** (ctor)  
Initializes internal pointer `i_path` to `nullptr` and `m_PathOrigin` to `PATH_NO_PATH`.

**`~CyclicMovementGenerator`** (dtor)  
Resets `i_path` to `nullptr`. Memory is owned by `WaypointManager`; no deletion occurs.

**`Initialize`**  
Marks the creature as roaming by calling `Unit.Main/AddUnitState` with `UNIT_STATE_ROAMING | UNIT_STATE_ROAMING_MOVE`.

**`Reset`**  
Identical to `Initialize`; re-applies roaming state flags.

**`Finalize`**  
Clears roaming state flags via `Unit.Main/ClearUnitState` and sets the creature to walk if it is not in a running state, using `Unit.Main/SetWalk`.

**`Interrupt`**  
Identical to `Finalize`; clears roaming state and adjusts walk/run mode.

### Path Loading

**`LoadPath`**  
Fetches waypoint data for a specific GUID and Entry.  
1. Logs the operation via `Log.Main/HasLogFilter` and `Log.Main/Out`.  
2. If `wpOrigin` is `PATH_NO_PATH`, calls `WaypointManager/GetDefaultPath`; otherwise, calls `WaypointManager/GetPathFromOrigin`.  
3. Validates the result: if `i_path` is null or contains fewer than 2 points, logs an error via `Log.Main/Out` and returns without setting the path.

**`InitializeWaypointPath`**  
Primary entry point for starting cyclic movement.  
1. Defaults `overwriteGuid` and `overwriteEntry` to the creature’s own GUID/Entry (via `Object/GetGUIDLow`/`Object/GetEntry`) if zero.  
2. Calls `LoadPath` to populate `i_path`.  
3. If a valid path exists, invokes `_setTargetLocation` to begin movement.

### Movement Execution

**`_setTargetLocation`**  
Calculates and launches the next movement spline.  
1. **Guard:** Returns immediately if the creature has `UNIT_STATE_CAN_NOT_MOVE`.  
2. **Initial Approach:** If the creature is more than 10 units from the first waypoint (`i_path->at(0)`), it uses `PathFinder` (`WorldObject.PathFinder/calculate#2`) to compute a direct path to the start. It then configures `MoveSplineInit` with flight/walk settings (`Creature.Main/CanFly`, `Creature.Main/HasExtraFlag`) and launches via `MoveSplineInit/Move` and `MoveSplineInit/Launch`.  
3. **Loop Execution:** If within 10 units, it converts the `WaypointPath` nodes into a `PointsArray` of `G3D::Vector3` coordinates. It configures `MoveSplineInit` similarly, calls `MoveSplineInit/MovebyPath` with the full array, sets the start index to 1 via `MoveSplineInit/SetFirstPointId`, and launches.

**`Update`**  
Periodically checks movement status.  
1. Returns `false` if `i_path` is invalid (< 2 points).  
2. Checks if the current spline is finished via `MoveSpline/Finalized`.  
3. If finalized, calls `_setTargetLocation`. Since the creature is at the last waypoint, this typically triggers the "Initial Approach" logic to pathfind back to the first node, closing the loop.  
4. Returns `true` to keep the generator active.

**`GetMovementGeneratorType`**  
Returns `CYCLIC_MOTION_TYPE`.

## Cross-Unit Boundaries

*   **`WaypointManager`**: Supplies path data via `GetDefaultPath` and `GetPathFromOrigin`. `CyclicMovementGenerator` holds a raw pointer to this data.
*   **`Creature` / `Unit`**: The generator manipulates state (`AddUnitState`, `ClearUnitState`, `SetWalk`) and queries properties (`CanFly`, `HasExtraFlag`, `GetDistance`).
*   **`MoveSplineInit` / `MoveSpline`**: Constructs movement commands (`MoveSplineInit`) and monitors completion (`MoveSpline/Finalized`).
*   **`PathFinder`**: Used in `_setTargetLocation` to calculate direct paths to the first waypoint when the creature is distant.
*   **`Log`**: Used for debug and error logging in `LoadPath`.

## Data Model

This unit does not interact directly with database tables. It retrieves waypoint data from `WaypointManager`, which abstracts the database layer.

## Notable Implementation Details

1.  **Implicit Loop Closure**: `_setTargetLocation` always checks distance to the *first* waypoint. When the loop finishes, the creature is at the last waypoint. If this is > 10 units from the start, `_setTargetLocation` triggers pathfinding back to the first node. The return trip is not necessarily the reverse of the outbound path.
2.  **Raw Pointer Ownership**: `i_path` is a raw pointer owned by `WaypointManager`. The destructor sets it to `nullptr` but does not delete it.
3.  **Hardcoded Threshold**: The 10-unit distance threshold in `_setTargetLocation` is hardcoded.
4.  **Redundant State Logic**: `Initialize`/`Reset` and `Finalize`/`Interrupt` have identical implementations.
5.  **Index Offset**: `MoveSplineInit/SetFirstPointId(1)` is used for loop execution, implying 1-based indexing for splines despite 0-based storage in `WaypointPath`.

## Member Reference

**`LoadPath`**: Retrieves waypoint path from `WaypointManager` for a given GUID/Entry. Validates path size (>= 2 points) and logs errors if invalid.

**`CyclicMovementGenerator`**: Constructor initializes `i_path` to `nullptr` and `m_PathOrigin` to `PATH_NO_PATH`.

**`~CyclicMovementGenerator`**: Destructor sets `i_path` to `nullptr`.

**`GetMovementGeneratorType`**: Returns `CYCLIC_MOTION_TYPE`.

**`Initialize`**: Adds `UNIT_STATE_ROAMING` and `UNIT_STATE_ROAMING_MOVE` to the creature.

**`InitializeWaypointPath`**: Resolves GUID/Entry, calls `LoadPath`, and triggers `_setTargetLocation` if path is valid.

**`Reset`**: Adds `UNIT_STATE_ROAMING` and `UNIT_STATE_ROAMING_MOVE` to the creature.

**`_setTargetLocation`**: Core movement logic. If distance to first waypoint > 10, pathfinds to it. Otherwise, launches full cyclic spline starting at point ID 1. Handles fly/walk modes.

**`Update`**: Checks if spline is finalized. If so, calls `_setTargetLocation` to restart loop. Returns `false` if path is invalid.

**`Finalize`**: Clears roaming state flags and sets creature to walk if not running.

**`Interrupt`**: Clears roaming state flags and sets creature to walk if not running.

---

<!-- machine-true, projected from graph.json -->

## Map — CyclicMovementGenerator

*Source:* CyclicMovementGenerator.cpp, CyclicMovementGenerator.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| LoadPath | method | Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out, WaypointManager/GetDefaultPath, WaypointManager/GetPathFromOrigin | — | — |
| CyclicMovementGenerator | ctor | — | Creature.MotionMaster/MoveCyclicWaypoint | — |
| ~CyclicMovementGenerator | dtor | — | — | — |
| GetMovementGeneratorType | method | — | — | — |
| Initialize | method | Unit.Main/AddUnitState | — | — |
| InitializeWaypointPath | method | Object/GetEntry, Object/GetGUIDLow | Creature.MotionMaster/Initialize, Creature.MotionMaster/InitializeNewDefault, Creature.MotionMaster/MoveCyclicWaypoint | — |
| Reset | method | Unit.Main/AddUnitState | — | — |
| _setTargetLocation | method | Creature.Main/CanFly, Creature.Main/HasExtraFlag, MoveSplineInit/Launch, MoveSplineInit/Move, MoveSplineInit/MovebyPath, MoveSplineInit/MoveSplineInit, MoveSplineInit/SetFirstPointId, MoveSplineInit/SetFly, MoveSplineInit/SetWalk, Unit.Main/HasUnitState, WorldObject.Object/GetDistance#4, WorldObject.PathFinder/calculate#2, WorldObject.PathFinder/PathInfo | — | — |
| Update | method | MoveSpline/Finalized | — | — |
| Finalize | method | Unit.Main/ClearUnitState, Unit.Main/HasUnitState, Unit.Main/SetWalk | — | — |
| Interrupt | method | Unit.Main/ClearUnitState, Unit.Main/HasUnitState, Unit.Main/SetWalk | — | — |
