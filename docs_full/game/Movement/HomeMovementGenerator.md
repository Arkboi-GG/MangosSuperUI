<!-- provenance: verbose -->
# HomeMovementGenerator

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# HomeMovementGenerator

## Purpose & Responsibilities

`HomeMovementGenerator` is a `MovementGenerator` specialization for `Creature` objects that manages the transition of a creature back to its designated home location. It determines the target coordinates (preferring a dynamic reset position from the previous movement context, falling back to static respawn coordinates), computes a path, and initiates movement or teleportation. Upon arrival, it cleans up temporary states (faction, speed debuffs) and notifies the creature’s AI.

## Member-by-Member Behavior

### Initialization and Targeting

**`HomeMovementGenerator`**
Constructs the generator, initializing the internal `arrived` flag to `false`.

**`Initialize`**
Entry point called by the motion master. Immediately delegates to `_setTargetLocation` to begin the return process.

**`_setTargetLocation`**
Core logic for determining destination and initiating movement:
1.  **Guard Clauses:** Returns immediately if the creature has `UNIT_STATE_CAN_NOT_MOVE` or is marked `CREATURE_STATIC_FLAG_SESSILE` (immobile).
2.  **State Cleanup:** Removes low-health aura states (`AURA_STATE_HEALTHLESS_15/10/5_PERCENT`) to ensure full-speed movement. Clears all dynamic unit states (`UNIT_STATE_ALL_DYN_STATES`).
3.  **Coordinate Selection:** Attempts to get a reset position from the top of the current motion master stack via `GetResetPosition`. If unavailable, falls back to `GetRespawnCoord`, which also provides orientation.
4.  **Pathfinding & Execution:** Uses `PathFinder` to calculate a route.
    *   If a valid path (`PATHFIND_NORMAL`) exists, it initializes a `MoveSplineInit`, sets facing (if available), configures running mode (`SetWalk(false)`), and launches the spline.
    *   If pathfinding fails, it teleports the creature to the target coordinates using `NearTeleportTo`.
5.  Resets `arrived` to `false`.

### Lifecycle Updates

**`Update`**
Polls the movement spline status. Sets `arrived = true` if `movespline->Finalized()` returns true. Returns `false` (stop generating) if arrived, `true` (continue) otherwise.

**`Finalize`**
Executes post-arrival logic if `arrived` is true:
1.  **Faction:** Clears temporary faction if `TEMPFACTION_RESTORE_REACH_HOME` is set.
2.  **Movement Mode:** Sets walking state to `true` only if the creature is not running and not levitating.
3.  **Addons:** Calls `LoadCreatureAddon(true)` to restore visual/mechanical addons.
4.  **AI Hook:** Calls `AI()->JustReachedHome()` to notify the specific AI implementation.

### Interface Compliance

**`Reset`**, **`Interrupt`**, **`~HomeMovementGenerator`**
Empty implementations satisfying the `MovementGenerator` interface.

**`GetMovementGeneratorType`**
Returns `HOME_MOTION_TYPE`.

## Cross-Unit Boundaries

### Calls Out
*   **`Creature.Main`**: `GetRespawnCoord`, `HasStaticFlag`, `ClearTemporaryFaction`, `GetTemporaryFactionFlags`, `LoadCreatureAddon`, `ClearUnitState`, `HasUnitState`, `ModifyAuraState`, `NearTeleportTo`, `SetWalk`, `AI`.
*   **`MovementGenerator`**: `GetResetPosition` (from top of motion master stack).
*   **`MoveSplineInit`**: `Launch`, `Move`, `SetFacing`, `SetWalk`.
*   **`WorldObject.PathFinder`**: `calculate`, `getPathType`.
*   **`Unit.Main`**: `GetMotionMaster`.
*   **`WorldObject.Object`**: `GetOrientation`, `IsLevitating`.
*   **`CreatureAI`**: `JustReachedHome`.
*   **`MoveSpline`**: `Finalized`.

### Called By
*   **`Creature.MotionMaster`**: `MoveTargetedHome` instantiates this generator.

## Data Model

This unit does not access any database tables. All data is derived from the `Creature` object’s in-memory state.

## Notable Implementation Details

*   **Forced Full Speed:** `_setTargetLocation` explicitly strips health-based slow auras. Creatures always return home at maximum speed, regardless of current HP.
*   **Teleport Fallback:** If `PathFinder` fails, `_setTargetLocation` uses `NearTeleportTo` instead of failing silently, ensuring the creature always reaches the destination.
*   **Dynamic Reset Preference:** The generator prefers `GetResetPosition` from the previous movement generator over static respawn coordinates, allowing for smoother transitions from patrols or other dynamic movements.
*   **Arrival Flag:** The `arrived` boolean is set in `Update` but acted upon in `Finalize`. This decouples the detection of spline completion from the side-effect-heavy finalization logic.

## Member Reference

**`Initialize`**: Delegates to `_setTargetLocation`.

**`Reset`**: Empty.

**`_setTargetLocation`**: Determines target coordinates, removes speed debuffs, pathfinds, and initiates movement or teleportation.

**`HomeMovementGenerator`**: Initializes `arrived` to `false`.

**`~HomeMovementGenerator`**: Default destructor.

**`Interrupt`**: Empty.

**`GetMovementGeneratorType`**: Returns `HOME_MOTION_TYPE`.

**`Update`**: Checks spline finalization; updates `arrived` flag.

**`Finalize`**: Restores faction, sets walk state, loads addons, and calls `JustReachedHome` if `arrived` is true.

---

<!-- machine-true, projected from graph.json -->

## Map — HomeMovementGenerator

*Source:* HomeMovementGenerator.cpp, HomeMovementGenerator.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Initialize | method | — | — | — |
| Reset | method | — | — | — |
| _setTargetLocation | method | Creature.Main/GetRespawnCoord, Creature.Main/HasStaticFlag, MovementGenerator/GetResetPosition, MoveSplineInit/Launch, MoveSplineInit/Move, MoveSplineInit/MoveSplineInit, MoveSplineInit/SetFacing#2, MoveSplineInit/SetWalk, PathInfo/getPathType, Unit.Main/ClearUnitState, Unit.Main/GetMotionMaster, Unit.Main/HasUnitState, Unit.Main/ModifyAuraState, Unit.Main/NearTeleportTo, WorldObject.Object/GetOrientation, WorldObject.PathFinder/calculate#2, WorldObject.PathFinder/PathInfo | — | — |
| HomeMovementGenerator | ctor | — | Creature.MotionMaster/MoveTargetedHome | — |
| ~HomeMovementGenerator | dtor | — | — | — |
| Interrupt | method | — | — | — |
| GetMovementGeneratorType | method | — | — | — |
| Update | method | MoveSpline/Finalized | — | — |
| Finalize | method | Creature.Main/AI, Creature.Main/ClearTemporaryFaction, Creature.Main/GetTemporaryFactionFlags, Creature.Main/LoadCreatureAddon, CreatureAI/JustReachedHome, Unit.Main/HasUnitState, Unit.Main/SetWalk, WorldObject.Object/IsLevitating | — | — |
