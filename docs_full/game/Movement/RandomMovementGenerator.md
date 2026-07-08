<!-- provenance: verbose -->
# RandomMovementGenerator

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# RandomMovementGenerator

**Purpose & Responsibilities**

`RandomMovementGenerator` drives `Creature` entities to roam randomly within a configurable radius (`i_wanderDistance`) of a starting position (`i_startPosition`). It supports two distinct movement modes:
1.  **Ground Roaming:** The creature selects random points within the wander radius, moves to them using pathfinding (excluding steep slopes), pauses for a randomized duration, and repeats.
2.  **Flying Roaming:** If the creature can fly, it executes a continuous circular flight pattern around the starting position, bypassing standard pathfinding for smooth aerial movement.

The generator manages the lifecycle of this behavior through initialization, periodic updates, and cleanup, ensuring roaming states are correctly set and cleared on the `Creature`. It does not interact with any database tables.

## Member-by-Member Behavior

### Initialization and Lifecycle

**`RandomMovementGenerator` (Constructor)**
Initializes the generator with a `Creature`. It sets `i_startPosition` and `i_wanderDistance`:
*   If `use_current_position` is true, it uses the creature's current coordinates via `WorldObject.Object/GetPosition#2`.
*   Otherwise, it retrieves respawn coordinates and default wander distance via `WorldObject.Object/GetRespawnCoord`.
*   It overrides `i_wanderDistance` if a positive value is provided.
*   It initializes `i_nextMoveTime` to 1000ms.

**`Initialize`**
Activates the generator. It verifies the creature is alive via `Unit.Main/IsAlive`. If alive, it sets `UNIT_STATE_ROAMING` and `UNIT_STATE_ROAMING_MOVE` via `Unit.Main/AddUnitState` and resets the move timer to 1000ms via `ShortTimeTracker/Reset`.

**`Reset`**
Delegates to `Initialize`, restarting roaming from the current state.

**`Interrupt`** and **`Finalize`**
Both perform identical cleanup:
1.  Clear `UNIT_STATE_ROAMING` and `UNIT_STATE_ROAMING_MOVE` via `Unit.Main/ClearUnitState`.
2.  Adjust walking state via `Unit.Main/SetWalk`. The creature stops running if it lacks `UNIT_STATE_RUNNING` (checked via `Unit.Main/HasUnitState`), returning to a neutral posture.

### Movement Logic

**`_setRandomLocation`**
Core logic for determining the next movement target, branching on flight capability:

*   **Flying Creatures:**
    *   Checks `Creature.Main/CanFly`.
    *   Constructs a circular path using trigonometry around `i_startPosition` with radius `i_wanderDistance`.
    *   Initializes `MoveSplineInit` via `MoveSplineInit/MoveSplineInit`, configures it to fly (`MoveSplineInit/SetFly`) and not walk (`MoveSplineInit/SetWalk`), sets the path via `MoveSplineInit/MovebyPath`, and launches via `MoveSplineInit/Launch`.
    *   Resets the timer to 0 via `ShortTimeTracker/Reset`, implying continuous motion.

*   **Ground Creatures:**
    *   Finds a valid random point within `i_wanderDistance` using `WorldObject.Object/GetRandomPoint`. Returns early if no valid terrain exists.
    *   Adds `UNIT_STATE_ROAMING_MOVE` via `Unit.Main/AddUnitState`.
    *   Initializes `MoveSplineInit`, configures pathfinding excluding steep slopes (`MOVE_PATHFINDING | MOVE_EXCLUDE_STEEP_SLOPES`) via `MoveSplineInit/MoveTo#2`, and sets walk/run state based on `Creature.Main/HasExtraFlag` (`CREATURE_FLAG_EXTRA_ALWAYS_RUN`) via `MoveSplineInit/SetWalk`.
    *   Launches via `MoveSplineInit/Launch`.
    *   **Step Management:**
        *   If `i_wanderSteps` > 0, decrements steps and sets a short 50ms pause via `ShortTimeTracker/Reset`.
        *   If `i_wanderSteps` == 0, sets a long pause (4–10 seconds) via `shared_Util/urand` and `ShortTimeTracker/Reset`, then sets new random steps (0–2 or 0–8) via `shared_Util/urand`.

### Update Loop

**`Update`**
Synchronous hook. Checks expiration (`i_expireTime`). If expired, returns `false`. Otherwise, decrements expiry and signals `MotionMaster` for an async update via `Unit.Main/GetMotionMaster` and `MotionMaster/SetNeedAsyncUpdate`.

**`UpdateAsync`**
Primary execution loop, protected by `creature.asyncMovesplineLock`:
1.  **Cannot Move/Distracted:** If `UNIT_STATE_CAN_NOT_MOVE` or `UNIT_STATE_DISTRACTED` (via `Unit.Main/HasUnitState`), expires timer (`i_nextMoveTime.Reset(0)`) and clears `UNIT_STATE_ROAMING_MOVE`.
2.  **Spell Casted:** If `SpellCaster/IsNoMovementSpellCasted` is true, stops movement via `Unit.Main/StopMoving` if not already stopped (`Unit.Main/IsStopped`).
3.  **Movement Finalized:** If `MoveSpline/Finalized` is true, updates timer via `ShortTimeTracker/Update`. If timer passed (`ShortTimeTracker/Passed`), triggers `_setRandomLocation`.

### Utilities

**`GetMovementGeneratorType`**
Returns `RANDOM_MOTION_TYPE`.

**`AddPauseTime`**
Extends pause duration. If `waitTimeDiff` exceeds current expiry, resets timer via `ShortTimeTracker/Reset`.

**`GetResetPosition`**
Determines reset position:
*   If within wander distance of start (`WorldObject.Object/IsWithinDist2d`), returns current position via `WorldObject.Object/GetPosition#2`.
*   Otherwise, returns `i_startPosition`.

## Cross-Unit Boundaries

*   **`Creature` (Main/MotionMaster):** Primary entity. Reads flags (`CanFly`, `HasExtraFlag`), manages states (`AddUnitState`, `ClearUnitState`, `HasUnitState`), and accesses `MotionMaster` for async updates.
*   **`MoveSplineInit` / `MoveSpline`:** Constructs and launches movement paths for flying (circular) and ground (pathfinding) modes.
*   **`ShortTimeTracker`:** Manages timing for pauses between movement steps.
*   **`Unit` (Main):** Provides state management (`IsAlive`, `IsStopped`, `SetWalk`) and motion master access.
*   **`WorldObject` (Object):** Provides spatial utilities (`GetRandomPoint`, `IsWithinDist2d`, `GetPosition#2`).
*   **`SpellCaster`:** Checked via `IsNoMovementSpellCasted` to prevent movement during immobilizing spells.
*   **`shared_Util`:** Uses `urand` for random pause durations and step counts.

## Data Model

This unit does not interact with any database tables. Configuration is derived from the `Creature` object's memory state or constructor arguments.

## Notable Implementation Details

*   **Flying vs. Ground Divergence:** Flying creatures use a pre-calculated circular spline, avoiding pathfinding issues in air. Ground creatures use pathfinding with steep slope exclusion.
*   **Step-Based Pacing:** Ground roaming uses a burst model: random steps (0–8) with short pauses (50ms), followed by a long pause (4–10s). This mimics natural animal behavior.
*   **Async Safety:** `UpdateAsync` acquires `creature.asyncMovesplineLock` to prevent race conditions when checking spline finalization or modifying states.
*   **Timer Reset on Fly:** Flying creatures reset the timer to 0 immediately after launching the circular path, suggesting continuous motion until interrupted.
*   **Reset Position Fallback:** `GetResetPosition` returns the current position if within the wander zone, preventing jarring teleportation during minor interruptions.

## Member Reference

**_setRandomLocation**: Core logic for setting the next movement target. Branches on `Creature.Main/CanFly`: flying creatures get a circular spline path via `MoveSplineInit`; ground creatures get a random point via `WorldObject.Object/GetRandomPoint` with pathfinding. Manages step counts and pause timers via `ShortTimeTracker` and `shared_Util/urand`.

**RandomMovementGenerator**: Constructor. Initializes `i_startPosition` and `i_wanderDistance` from `Creature` (via `WorldObject.Object/GetPosition#2` or `GetRespawnCoord`). Sets initial timer.

**GetMovementGeneratorType**: Returns `RANDOM_MOTION_TYPE`.

**AddPauseTime**: Extends the next move timer if the provided `waitTimeDiff` is greater than the current remaining time, using `ShortTimeTracker/Reset`.

**Initialize**: Activates roaming. Checks `Unit.Main/IsAlive`, sets `UNIT_STATE_ROAMING` and `UNIT_STATE_ROAMING_MOVE` via `Unit.Main/AddUnitState`, and resets timer via `ShortTimeTracker/Reset`.

**Reset**: Delegates to `Initialize`.

**Interrupt**: Cleanup. Clears roaming states via `Unit.Main/ClearUnitState` and adjusts walk state via `Unit.Main/SetWalk` based on `Unit.Main/HasUnitState`.

**Finalize**: Identical to `Interrupt`. Clears roaming states and adjusts walk state.

**Update**: Synchronous hook. Checks expiration, decrements timer, and signals `MotionMaster/SetNeedAsyncUpdate` via `Unit.Main/GetMotionMaster`.

**UpdateAsync**: Async execution loop. Protected by lock. Handles immobility states (`Unit.Main/HasUnitState`), spell casting (`SpellCaster/IsNoMovementSpellCasted`), and spline finalization (`MoveSpline/Finalized`). Triggers `_setRandomLocation` when timer passes (`ShortTimeTracker/Passed`).

**GetResetPosition**: Returns current position if within wander distance (`WorldObject.Object/IsWithinDist2d`), otherwise returns start position. Uses `WorldObject.Object/GetPosition#2`.

---

<!-- machine-true, projected from graph.json -->

## Map — RandomMovementGenerator

*Source:* RandomMovementGenerator.cpp, RandomMovementGenerator.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| _setRandomLocation | method | Creature.Main/CanFly, Creature.Main/HasExtraFlag, MoveSplineInit/Launch, MoveSplineInit/MovebyPath, MoveSplineInit/MoveSplineInit, MoveSplineInit/MoveTo#2, MoveSplineInit/SetFirstPointId, MoveSplineInit/SetFly, MoveSplineInit/SetWalk, shared_Util/urand, ShortTimeTracker/Reset, Unit.Main/AddUnitState, WorldObject.Object/GetRandomPoint | — | — |
| RandomMovementGenerator | ctor | — | Creature.MotionMaster/MoveRandom | — |
| GetMovementGeneratorType | method | — | — | — |
| AddPauseTime | method | — | Creature.MotionMaster/PauseOutOfCombatMovement | — |
| Initialize | method | ShortTimeTracker/Reset, Unit.Main/AddUnitState, Unit.Main/IsAlive | — | — |
| Reset | method | — | — | — |
| Interrupt | method | Unit.Main/ClearUnitState, Unit.Main/HasUnitState, Unit.Main/SetWalk | — | — |
| Finalize | method | Unit.Main/ClearUnitState, Unit.Main/HasUnitState, Unit.Main/SetWalk | — | — |
| Update | method | MotionMaster/SetNeedAsyncUpdate, Unit.Main/GetMotionMaster | — | — |
| UpdateAsync | method | MoveSpline/Finalized, ShortTimeTracker/Passed, ShortTimeTracker/Reset, ShortTimeTracker/Update, SpellCaster/IsNoMovementSpellCasted, Unit.Main/ClearUnitState, Unit.Main/HasUnitState, Unit.Main/IsStopped, Unit.Main/StopMoving | — | — |
| GetResetPosition | method | WorldObject.Object/GetPosition#2, WorldObject.Object/IsWithinDist2d | — | — |
