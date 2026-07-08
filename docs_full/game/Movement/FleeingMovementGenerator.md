# FleeingMovementGenerator

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# FleeingMovementGenerator

**Purpose & Responsibilities**

`FleeingMovementGenerator` controls the movement of `Player` and `Creature` units escaping a specific threat (`i_frightGuid`). It calculates escape vectors relative to the threat, maintains a "quiet zone" distance (28–38 yards), and executes movement via pathfinding and splines. The class handles state transitions (start, interrupt, finalize) and periodic re-evaluation of the flee path.

`TimedFleeingMovementGenerator` extends this for `Creature`s with a fixed duration. Upon expiration, it automatically resumes combat with the previous victim if valid.

This unit accesses no database tables.

## Member-by-Member Behavior

### Initialization and Lifecycle

*   **`FleeingMovementGenerator<T>`**: Stores the threat GUID, initializes timing and speed flags.
*   **`Initialize`**: Sets `UNIT_STATE_FLEEING` and `UNIT_STATE_FLEEING_MOVE`, stops current movement, updates control, and triggers the first `_setTargetLocation`. For `Unit`s, it enforces walk mode if `_forceWalking` is set and clears the target GUID.
*   **`Finalize`**:
    *   **`Player`**: Clears flee states, stops movement, updates control.
    *   **`Creature`**: Restores walk/run state based on pre-flee status, clears flee states, updates control.
*   **`Interrupt`**: Clears `UNIT_STATE_FLEEING_MOVE` but preserves `UNIT_STATE_FLEEING`, allowing external systems to detect the fleeing intent even if movement is paused.
*   **`Reset`**: Calls `Initialize` to restart the flee sequence.

### Movement Calculation

*   **`_setTargetLocation`**: Orchestrates the flee step. It aborts if the owner is stunned, non-reactive, or a teleporting `Player`. It calls `_getPoint` for a destination, then uses `PathFinder` to compute a path (excluding steep slopes, limited to 30 yards). If no path exists, it delays the next check by 1–1.5 seconds. If a path exists, it launches a `MoveSplineInit` with optional custom speed and sets the next check timer to travel time plus 0.8–1.5 seconds.
*   **`_getPoint`**: Determines the target coordinate.
    *   Resolves `i_frightGuid` via `ObjectAccessor`. If the threat is missing or too close (<0.2f), it picks a random angle.
    *   **Close (<28f)**: Flees directly away, distance proportional to proximity.
    *   **Far (>38f)**: Drifts back toward the quiet zone, angle roughly opposite the threat.
    *   **Quiet Zone**: Moves randomly within the zone.
    *   Validates coordinates against map bounds (`MaNGOS::IsValidMapCoord`) and walkability (`GetWalkRandomPosition`). Falls back to current position if invalid.

### Update Loop

*   **`Update`**:
    *   Aborts if the owner is dead.
    *   If stunned/non-reactive, clears `UNIT_STATE_FLEEING_MOVE` and returns `true` (idle).
    *   If the check timer expires or `_forceUpdate` is set, calls `_setTargetLocation`.
    *   For `Creature`s, calls `CallForHelp(10.0f)`.
    *   Returns `true` to keep the generator active.

### Timed Fleeing (`TimedFleeingMovementGenerator`)

*   **`Initialize`**: Asserts owner is a `Unit`, disables forced walking, and delegates to `FleeingMovementGenerator<Creature>::Initialize`.
*   **`Finalize`**: Clears flee states. If the creature is alive and not confused/fleeing/possessed, it stops attacking and calls `CreatureAI::AttackStart` on the current victim to resume combat.
*   **`Update`**: Checks death and stun states. If the total flee time (`i_totalFleeTime`) expires, returns `false` to remove the generator. Otherwise, it bypasses `FleeingMovementGenerator::Update` and calls `MovementGeneratorMedium::Update` directly to avoid redundant processing.

## Cross-Unit Boundaries

*   **`PathFinder`**: `_setTargetLocation` configures and runs pathfinding. It interprets `PATHFIND_NOPATH` to delay retries.
*   **`MoveSplineInit`**: `_setTargetLocation` uses this to convert the calculated path into executable movement splines.
*   **`ObjectAccessor`**: `_getPoint` resolves the threat GUID to a live `Unit` to calculate relative angles and distances.
*   **`GridDefines`**: `_getPoint` uses `IsValidMapCoord` to prevent crashes from out-of-bounds targets.
*   **`Unit.Main`**: Used for state management (`Add/Clear/HasUnitState`), movement control (`StopMoving`, `SetWalk`), and context queries (`IsAlive`, `GetVictim`, `GetDistance`).
*   **`CreatureAI`**: `TimedFleeingMovementGenerator::Finalize` calls `AttackStart` to resume combat.
*   **`TimeTracker`**: Manages `i_nextCheckTime` (re-calculation interval) and `i_totalFleeTime` (timed flee duration).
*   **`shared_Util`**: `urand` and `frand` add randomness to timers and flee angles.

## Notable Implementation Details

*   **Quiet Zone Logic**: `_getPoint` prevents infinite fleeing by keeping units within 28–38 yards of the threat. This keeps NPCs on-screen and engaged.
*   **Pathfinding Resilience**: If `PathFinder` fails, `_setTargetLocation` does not crash; it waits 1–1.5 seconds and retries, handling temporary geometry issues.
*   **Timed Bypass**: `TimedFleeingMovementGenerator::Update` calls the grandparent `MovementGeneratorMedium::Update` instead of the parent. This skips `FleeingMovementGenerator::Update`'s `CallForHelp` and `_setTargetLocation` logic, giving the timed variant strict control over its lifecycle.
*   **State Persistence**: `Interrupt` preserves `UNIT_STATE_FLEEING`, distinguishing between "stopped moving" and "no longer fleeing."

## Member Reference

*   **`FleeingMovementGenerator<T>`**: Constructor storing threat GUID and initializing flags.
*   **`Finalize#4`**: Declaration placeholder.
*   **`_setTargetLocation`**: Calculates destination via `_getPoint`, runs `PathFinder`, and launches `MoveSplineInit`; delays on failure.
*   **`UnitSpeedChanged`**: Sets `_forceUpdate` to trigger immediate path recalculation.
*   **`GetMovementGeneratorType`**: Returns `FLEEING_MOTION_TYPE`.
*   **`_getPoint`**: Computes flee coordinate based on threat distance/angle and quiet zone rules; validates map bounds.
*   **`Initialize#2`**: Declaration placeholder.
*   **`Finalize#2`**: Declaration placeholder.
*   **`Finalize`**: Specialized cleanup for `Player` (stop) and `Creature` (restore walk/run).
*   **`Interrupt`**: Clears `UNIT_STATE_FLEEING_MOVE`, retains `UNIT_STATE_FLEEING`.
*   **`Reset`**: Calls `Initialize` to restart fleeing.
*   **`Update#2`**: Declaration placeholder.
*   **`Initialize`**: Sets flee states, stops movement, updates control, triggers first `_setTargetLocation`.
*   **`Finalize#3`**: Declaration placeholder.
*   **`Update`**: Checks death/stun, triggers `_setTargetLocation` on timer, calls `CallForHelp` for creatures.

---

<!-- machine-true, projected from graph.json -->

## Map — FleeingMovementGenerator

*Source:* FleeingMovementGenerator.cpp, FleeingMovementGenerator.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| FleeingMovementGenerator<T> | ctor | — | — | — |
| Finalize#4 | decl | — | — | — |
| _setTargetLocation | function | MoveSplineInit/Launch, MoveSplineInit/Move, MoveSplineInit/SetVelocity, MoveSplineInit/SetWalk, PathInfo/ExcludeSteepSlopes, PathInfo/getPathType, PathInfo/SetTransport, Player.Main/IsBeingTeleported, shared_Util/urand, TimeTracker/Reset, WorldObject.PathFinder/calculate#2, WorldObject.PathFinder/CutPathWithDynamicLoS, WorldObject.PathFinder/setPathLengthLimit | — | — |
| UnitSpeedChanged | function | — | — | — |
| GetMovementGeneratorType | function | — | — | — |
| _getPoint | function | GridDefines/IsValidMapCoord#3, ObjectAccessor/GetUnit, shared_Util/frand | — | — |
| Initialize#2 | function | — | — | — |
| Finalize#2 | method | Unit.Main/ClearUnitState, Unit.Main/StopMoving, Unit.Main/UpdateControl | — | — |
| Finalize | method | Unit.Main/ClearUnitState, Unit.Main/HasUnitState, Unit.Main/SetWalk, Unit.Main/UpdateControl | — | — |
| Interrupt | function | — | — | — |
| Reset | function | — | — | — |
| Update#2 | function | TimeTracker/Passed, TimeTracker/Update | — | — |
| Initialize | method | Errors/PrintStacktraceAndThrow, Object/GetTypeId | — | — |
| Finalize#3 | method | Creature.Main/AI, CreatureAI/AttackStart, Object/HasFlag, Unit.Main/AttackStop, Unit.Main/ClearUnitState, Unit.Main/GetVictim, Unit.Main/IsAlive, Unit.Main/UpdateControl | — | — |
| Update | method | TimeTracker/Passed, TimeTracker/Update, Unit.Main/ClearUnitState, Unit.Main/HasUnitState, Unit.Main/IsAlive | — | — |
