<!-- provenance: verbose -->
# ConfusedMovementGenerator

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ConfusedMovementGenerator

**Purpose & Responsibilities**

`ConfusedMovementGenerator` drives the erratic, short-range movement of units (Players or Creatures) in a "confused" state. It inherits from `MovementGeneratorMedium` and manages the state lifecycle: initializing position, periodically calculating random nearby paths, and cleaning up state upon exit. The generator enforces walking speed, avoids steep slopes, limits path length to 4.0 units, and handles transport-relative coordinates.

**Member-by-Member Behavior**

### Lifecycle and State

*   **`ConfusedMovementGenerator<T>`**: Initializes internal reference coordinates `i_x`, `i_y`, `i_z` to `0.0f`.
*   **`Initialize`**: Captures the unit’s current safe position (including transport offsets) into `i_x/y/z`. If the unit is mid-movement (`movespline` not finalized), it stops immediately via `Unit.Main/StopMoving`. It then adds `UNIT_STATE_CONFUSED` and updates control via `Unit.Main/UpdateControl`.
*   **`Reset`**: Stops current movement via `Unit.Main/StopMoving`, re-adds `UNIT_STATE_CONFUSED`, and updates control via `Unit.Main/UpdateControl`.
*   **`Interrupt`**: Empty implementation. Interrupting a confused unit does not immediately halt movement or clear state; the unit continues until the next update cycle or explicit finalization.
*   **`SetStartPosition`**: Manually overrides the internal reference coordinates `i_x/y/z`.
*   **`GetMovementGeneratorType`**: Returns `CONFUSED_MOTION_TYPE`.

### Movement Logic

*   **`Update`**: Executed periodically to drive movement. It skips execution if:
    1.  The unit has non-reactive states (`CAN_NOT_REACT`, `CAN_NOT_MOVE`, `STUNNED`, `PENDING_STUNNED`), excluding `CONFUSED`.
    2.  The unit is a `Player` currently being teleported (`Player.Main/IsBeingTeleported`).
    3.  The previous movement spline has not finalized.
    
    If active, it calculates a new destination:
    *   **On Transport**: Adds a random offset (-2 to 2) to `i_x` and `i_y` using `shared_Util/frand`.
    *   **Off Transport**: Uses `unit.GetRandomPoint` to find a valid ground point within 4.0 units of `i_x/y/z`. If no point is found, it returns early.
    
    It then constructs a `PathFinder` (`WorldObject.PathFinder`), configuring it to exclude steep slopes (`PathInfo/ExcludeSteepSlopes`), limit path length to 4.0 units (`WorldObject.PathFinder/setPathLengthLimit`), and account for transports (`PathInfo/SetTransport`). After calculating the path (`WorldObject.PathFinder/calculate#2`) and cutting it with dynamic LoS (`WorldObject.PathFinder/CutPathWithDynamicLoS`), it initializes a `MoveSplineInit` (`MoveSplineInit/Launch`, `MoveSplineInit/Move`, `MoveSplineInit/SetWalk`) to move the unit walking to the new point.

### Finalization

*   **`Finalize`** (Player specialization): Clears `UNIT_STATE_CONFUSED`, stops movement via `Unit.Main/StopMoving`, and updates control via `Unit.Main/UpdateControl`.
*   **`Finalize`** (Creature specialization): Clears `UNIT_STATE_CONFUSED` and updates control via `Unit.Main/UpdateControl`, but notably does *not* call `StopMoving`.

**Cross-Unit Boundaries**

*   **`Unit.Main`**: `Initialize`, `Reset`, and `Finalize` manipulate unit state flags (`AddUnitState`, `ClearUnitState`) and control (`UpdateControl`). `Finalize` for Players also calls `StopMoving`.
*   **`MoveSplineInit`**: `Update` uses `Launch`, `Move`, and `SetWalk` to execute the calculated path.
*   **`PathFinder` / `WorldObject.PathFinder`**: `Update` relies on `calculate#2`, `CutPathWithDynamicLoS`, `setPathLengthLimit`, `SetTransport`, and `ExcludeSteepSlopes` to generate valid, constrained paths.
*   **`Player.Main`**: `Update` checks `IsBeingTeleported` to prevent movement conflicts during teleportation.
*   **`shared_Util`**: `Update` uses `frand` for random coordinate offsets on transports.

**Data Model**

This unit interacts with no database tables.

**Notable Implementation Details**

*   **Asymmetric Finalization**: `Finalize` for `Player` explicitly stops movement, while `Finalize` for `Creature` does not. This likely reflects differing expectations for immediate client-side feedback or state transitions between players and NPCs.
*   **Transport Simplification**: On transports, random offsets are applied directly to coordinates without ground validation (`GetRandomPoint`), assuming the transport surface is navigable.
*   **Strict Path Constraints**: The 4.0 unit path length limit and steep slope exclusion enforce the "short, erratic steps" characteristic of confusion, preventing long dashes or impossible climbs.

## Member Reference

*   **Initialize**: Captures current position, stops pending movement, sets `UNIT_STATE_CONFUSED`, and updates control.
*   **ConfusedMovementGenerator<T>**: Constructor initializing internal coordinates to zero.
*   **Finalize#3**: Declaration of the template method `Finalize` in the header.
*   **SetStartPosition**: Sets internal reference coordinates `i_x/y/z`.
*   **Interrupt**: No-op; does not alter state or movement.
*   **GetMovementGeneratorType**: Returns `CONFUSED_MOTION_TYPE`.
*   **Reset**: Stops movement, re-applies `UNIT_STATE_CONFUSED`, and updates control.
*   **Update**: Skips if blocked/teleporting/moving; calculates random nearby destination; paths with constraints (walk, no steep slopes, max 4.0 units); launches spline.
*   **Finalize#2**: Creature specialization; clears `UNIT_STATE_CONFUSED` and updates control, but does not stop movement.
*   **Finalize**: Player specialization; clears `UNIT_STATE_CONFUSED`, stops movement, and updates control.

---

<!-- machine-true, projected from graph.json -->

## Map — ConfusedMovementGenerator

*Source:* ConfusedMovementGenerator.cpp, ConfusedMovementGenerator.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Initialize | function | — | — | — |
| ConfusedMovementGenerator<T> | ctor | — | — | — |
| Finalize#3 | decl | — | — | — |
| SetStartPosition | function | — | — | — |
| Interrupt | function | — | — | — |
| GetMovementGeneratorType | function | — | — | — |
| Reset | function | — | — | — |
| Update | function | MoveSplineInit/Launch, MoveSplineInit/Move, MoveSplineInit/SetWalk, PathInfo/ExcludeSteepSlopes, PathInfo/SetTransport, Player.Main/IsBeingTeleported, shared_Util/frand, WorldObject.PathFinder/calculate#2, WorldObject.PathFinder/CutPathWithDynamicLoS, WorldObject.PathFinder/setPathLengthLimit | — | — |
| Finalize#2 | method | Unit.Main/ClearUnitState, Unit.Main/StopMoving, Unit.Main/UpdateControl | — | — |
| Finalize | method | Unit.Main/ClearUnitState, Unit.Main/UpdateControl | — | — |
