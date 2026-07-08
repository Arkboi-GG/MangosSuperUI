<!-- provenance: verbose -->
# FearMovementGenerator

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# FearMovementGenerator

**Purpose & Responsibilities**

`FearMovementGenerator` implements the movement logic for units (`Player` and `Creature`) under the effect of fear. It calculates escape vectors away from a specific threat source (`i_frightGuid`), navigates obstacles via the pathfinding system, and manages state transitions.

The unit provides two implementations:
1.  **`FearMovementGenerator<T>`**: A template class for `Player` and `Creature` that continuously re-evaluates the flee path until the effect ends or is interrupted.
2.  **`TimedFearMovementGenerator`**: A subclass for `Creature` that enforces a fixed duration. Upon expiration, it restores the creature to combat mode, targeting its previous victim.

No database tables are accessed; all logic is driven by runtime state, spatial calculations, and the pathfinding engine.

## Member-by-Member Behavior

### Initialization and State Management

**`FearMovementGenerator<T>` (Constructor)**
Initializes the generator with the GUID of the fear source (`i_frightGuid`). It sets up internal timers (`i_initialFleeTime`, `i_nextCheckTime`) and flags (`_timeInitDone`, `_pointInitDone`) to distinguish the initial burst of movement from subsequent random wandering. Defaults to non-walking mode.

**`Initialize` (Method)**
Activates the fear movement. Sets `UNIT_STATE_FLEEING` and `UNIT_STATE_FLEEING_MOVE`. For client builds > 1.6.1, it immediately stops current movement to ensure instant reaction. Updates control flags, forces walking mode for Creatures if configured, clears the target GUID, and triggers `_setTargetLocation` for the first escape vector.

**`Finalize` (Method)**
Deactivates the fear movement.
*   **Player specialization:** Clears fleeing states, stops movement, and updates control.
*   **Creature specialization:** Restores walk/run state based on prior `UNIT_STATE_RUNNING` flag, clears fleeing states, and updates control.

**`TimedFearMovementGenerator::Finalize` (Method)**
Overrides base finalize for timed fears. Clears states and updates control. If the creature is alive and has a victim, it stops attacking and calls `CreatureAI::AttackStart` on the victim to resume combat immediately.

**`Interrupt` (Function)**
Called when the generator is disabled but the fear effect may persist. Clears only `UNIT_STATE_FLEEING_MOVE`, preserving `UNIT_STATE_FLEEING` to allow state persistence if the generator is re-enabled.

**`Reset` (Function)**
Re-initializes the fear movement by calling `Initialize`, used to refresh the pathing context.

### Path Calculation and Navigation

**`_setTargetLocation` (Function)**
Core logic for determining the next destination.
1.  **Validation:** Returns early if the owner is invalid, being teleported (Players), or in a non-reactive/non-movable state (stunned, rooted), excluding the fleeing state itself.
2.  **Point Generation:** Calls `_getPoint` for destination coordinates.
3.  **Pathfinding:** Configures `PathFinder` to exclude steep slopes, respect transports, and limit path length to 45.0f. Applies dynamic Line-of-Sight cuts.
4.  **Failure Handling:** If no path is found (`PATHFIND_NOPATH`), it resets the check timer to a random 1000–1500ms interval and returns.
5.  **Execution:** Launches a `MoveSpline` with the calculated path, setting velocity if custom speed is defined. Schedules the next check based on travel time plus a random buffer.

**`_getPoint` (Function)**
Calculates the specific `(x, y, z)` coordinate.
1.  **Initial Phase:** If `i_initialFleeTime` has not passed and `_pointInitDone` is false, it locates the fear source via `ObjectAccessor`. It calculates an angle away from the source (or random if too close) and a distance scaled from `DEFAULT_INIT_FLEE_DIST`. Marks `_pointInitDone` true.
2.  **Subsequent Phase:** Picks a random angle and distance within `POST_INIT_RADIUS` (20.0f).
3.  **Validation:** Computes raw coordinates from the owner's safe position. Validates against `GridDefines::IsValidMapCoord`. Uses `Map::GetWalkHitPosition` to ensure ground/water validity, falling back to the owner's position if invalid. Ensures reachability via `Map::GetWalkRandomPosition` within 5.0f.

### Update Loop

**`Update` (Function)**
Executed periodically.
1.  **Health/State Check:** Terminates if dead. If stunned/rooted (excluding fleeing), clears `UNIT_STATE_FLEEING_MOVE` and remains active.
2.  **Timer Logic:** Updates `i_nextCheckTime` and `i_initialFleeTime`. During the initial phase, if the fear source is distant (> 28.0f), it randomly extends the initial flee time.
3.  **Path Refresh:** If the check timer expires and the previous spline is finalized, or if `_forceUpdate` is set, it calls `_setTargetLocation`.

**`TimedFearMovementGenerator::Update` (Method)**
Overrides base update. Checks health and reaction states. Updates `i_totalFleeTime`. If the total fear time expires, returns `false` to terminate the generator. Otherwise, delegates to `FearMovementGenerator<Creature>::Update`.

**`UnitSpeedChanged` (Function)**
Sets `_forceUpdate` to `true`, forcing a path recalculation on the next update to account for speed changes.

**`GetMovementGeneratorType` (Function)**
Returns `FLEEING_MOTION_TYPE` (base) or `TIMED_FLEEING_MOTION_TYPE` (timed subclass).

## Cross-Unit Boundaries

*   **Pathfinding (`WorldObject.PathFinder`, `PathInfo`):** `_setTargetLocation` calls `calculate`, `SetTransport`, `ExcludeSteepSlopes`, `getPathType`, and `CutPathWithDynamicLoS` to generate a navigable path from raw coordinates.
*   **Movement Spline (`MoveSplineInit`):** `_setTargetLocation` calls `Launch`, `Move`, `SetVelocity`, and `SetWalk` to execute the calculated path.
*   **Object Access (`ObjectAccessor`):** `_getPoint` and `Update` call `GetUnit` to resolve `i_frightGuid` into a live `Unit` pointer for distance/angle calculations.
*   **Unit State (`Unit.Main`):** `Initialize`, `Finalize`, `Interrupt`, and `Update` manipulate state flags (`AddUnitState`, `ClearUnitState`, `HasUnitState`), control (`UpdateControl`), and movement (`StopMoving`, `IsAlive`, `GetVictim`, `AttackStop`).
*   **Creature AI (`Creature.Main`, `CreatureAI`):** `TimedFearMovementGenerator::Finalize` calls `AI` and `AttackStart` to resume combat. `Initialize` calls `GetFleeingSpeed`.
*   **Utilities (`shared_Util`, `TimeTracker`, `ShortTimeTracker`, `GridDefines`):** Used for random number generation (`urand`, `frand`), timer management (`Reset`, `Passed`, `Update`, `GetExpiry`), and coordinate validation (`IsValidMapCoord`).

## Data Model

This unit does not access any database tables.

## Notable Implementation Details

1.  **Client Build Compatibility:** `Initialize` uses `#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_6_1` to interrupt movement immediately in newer clients, whereas older clients wait for the current spline to finalize.
2.  **Two-Phase Fleeing:** `i_initialFleeTime` and `_pointInitDone` manage a distinct initial phase (fleeing directly away from the threat) followed by random wandering. This simulates panicked behavior more realistically than pure random movement.
3.  **Path Failure Resilience:** If `PathFinder` fails, `_setTargetLocation` does not crash. It resets the check timer to a longer random interval (1000–1500ms) to avoid infinite loops and retry later.
4.  **Combat Resumption:** `TimedFearMovementGenerator::Finalize` explicitly calls `CreatureAI::AttackStart` on the victim, ensuring creatures re-engage immediately after fear expires rather than standing idle.
5.  **Transport Awareness:** `PathFinder` is configured with `SetTransport` to handle units on moving transports, calculating paths in local space.

## Member Reference

**_setTargetLocation**
Calculates a new destination for the fleeing unit. Validates owner state, retrieves a target point via `_getPoint`, computes a path using `PathFinder` (excluding steep slopes, limiting length), and launches a `MoveSpline` if successful. If no path is found, it schedules a retry.

**FearMovementGenerator<T>**
Constructor initializing the generator with the fear source GUID (`i_frightGuid`) and setting up internal timers and flags for the initial flee phase and check intervals.

**Finalize#4**
Declaration of the `Finalize` method in the header file.

**UnitSpeedChanged**
Sets `_forceUpdate` to `true`, triggering a path recalculation on the next update cycle to account for changed movement speeds.

**GetMovementGeneratorType**
Returns `FLEEING_MOTION_TYPE`, identifying this generator to the movement management system.

**_getPoint**
Determines the specific `(x, y, z)` coordinate to flee towards. In the initial phase, it calculates a vector away from the fear source. In subsequent phases, it picks a random direction. Validates the coordinate against map bounds and walkable terrain.

**Initialize#2**
Declaration of the `Initialize` method in the header file.

**Finalize#2**
Template specialization declaration for `Player` in the header file.

**Finalize**
Template specialization for `Player`: Clears fleeing states, stops movement, and updates control. Template specialization for `Creature`: Restores walk/run state, clears fleeing states, and updates control.

**Interrupt**
Clears `UNIT_STATE_FLEEING_MOVE` but preserves `UNIT_STATE_FLEEING`, allowing the fear state to persist if the generator is temporarily disabled.

**Reset**
Re-initializes the fear movement by calling `Initialize`, effectively restarting the flee logic.

**Update#2**
Declaration of the `Update` method in the header file.

**Initialize**
Starts the fear movement. Sets fleeing states, stops current movement (for newer clients), updates control, and triggers the first path calculation via `_setTargetLocation`.

**Finalize#3**
Template specialization declaration for `Creature` in the header file.

**TimedFearMovementGenerator**
Constructor for the timed variant. Initializes the base `FearMovementGenerator` with the fear source GUID and sets the total fear duration timer (`i_totalFleeTime`). Configures the initial flee time with randomness.

**Update**
Base template update: Checks health and reaction states, updates timers, and recalculates the path if the check timer expires or forced. Timed variant update: Adds a check for the total fear duration, terminating the generator if time expires, otherwise delegating to the base update.

---

<!-- machine-true, projected from graph.json -->

## Map — FearMovementGenerator

*Source:* FearMovementGenerator.cpp, FearMovementGenerator.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| _setTargetLocation | function | MoveSplineInit/Launch, MoveSplineInit/Move, MoveSplineInit/SetVelocity, MoveSplineInit/SetWalk, PathInfo/ExcludeSteepSlopes, PathInfo/getPathType, PathInfo/SetTransport, Player.Main/IsBeingTeleported, shared_Util/urand, TimeTracker/Reset, WorldObject.PathFinder/calculate#2, WorldObject.PathFinder/CutPathWithDynamicLoS, WorldObject.PathFinder/setPathLengthLimit | — | — |
| FearMovementGenerator<T> | ctor | — | — | — |
| Finalize#4 | decl | — | — | — |
| UnitSpeedChanged | function | — | — | — |
| GetMovementGeneratorType | function | — | — | — |
| _getPoint | function | GridDefines/IsValidMapCoord#3, ObjectAccessor/GetUnit, shared_Util/frand, ShortTimeTracker/Passed | — | — |
| Initialize#2 | function | — | — | — |
| Finalize#2 | method | Unit.Main/ClearUnitState, Unit.Main/StopMoving, Unit.Main/UpdateControl | — | — |
| Finalize | method | Unit.Main/ClearUnitState, Unit.Main/HasUnitState, Unit.Main/SetWalk, Unit.Main/UpdateControl | — | — |
| Interrupt | function | — | — | — |
| Reset | function | — | — | — |
| Update#2 | function | ObjectAccessor/GetUnit, shared_Util/frand, ShortTimeTracker/GetExpiry, ShortTimeTracker/Reset, ShortTimeTracker/Update, TimeTracker/Passed, TimeTracker/Update | — | — |
| Initialize | method | Creature.Main/GetFleeingSpeed, Errors/PrintStacktraceAndThrow, Object/GetTypeId | — | — |
| Finalize#3 | method | Creature.Main/AI, CreatureAI/AttackStart, Unit.Main/AttackStop, Unit.Main/ClearUnitState, Unit.Main/GetVictim, Unit.Main/IsAlive, Unit.Main/UpdateControl | — | — |
| TimedFearMovementGenerator | ctor | shared_Util/urand, ShortTimeTracker/Reset, TimeTracker/TimeTracker | Creature.MotionMaster/MoveFeared | — |
| Update | method | TimeTracker/Passed, TimeTracker/Update, Unit.Main/ClearUnitState, Unit.Main/HasUnitState, Unit.Main/IsAlive | — | — |
