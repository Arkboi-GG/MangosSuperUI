# TargetedMovementGenerator

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# TargetedMovementGenerator

**Purpose & Responsibilities**

`TargetedMovementGenerator` implements the core logic for two specific types of AI-driven movement in the WoW server emulation: **Chasing** (`ChaseMovementGenerator`) and **Following** (`FollowMovementGenerator`). These classes inherit from `TargetedMovementGeneratorMedium`, which provides shared state management for tracking a target unit, maintaining a desired offset/angle, and handling asynchronous path recalculation.

The primary responsibility of this unit is to calculate valid movement paths for a `Unit` (typically a `Creature` or `Player`) towards or alongside a target `Unit`. It handles complex scenarios including:
1.  **Pathfinding:** Using `PathFinder` to navigate around obstacles, respecting terrain heights and water boundaries.
2.  **Transport Handling:** Adjusting coordinates and path validity when the mover or target is on a moving vehicle (`GenericTransport`).
3.  **Collision Avoidance:** Implementing "spread" logic for chasers to avoid stacking on top of each other when attacking the same target.
4.  **State Management:** Integrating with the `MotionMaster` system to launch splines, update unit states (`UNIT_STATE_CHASE`, `UNIT_STATE_FOLLOW`), and notify AI systems upon movement completion or interruption.
5.  **Anti-Cheat/Edge Cases:** Detecting unreachable targets due to height differences (fly-hacks) and preventing redundant micro-movements for stationary followers.

This unit does not interact with any database tables. All logic is runtime-based, relying on the current world state, object positions, and configuration settings.

---

## Member-by-Member Behavior

### Shared Infrastructure (`TargetedMovementGeneratorMedium`)

The base template class `TargetedMovementGeneratorMedium<T, D>` holds common data members such as `m_fOffset` (distance from target), `m_fAngle` (angular offset), `m_bRecalculateTravel` (flag to trigger path update), and `i_target` (reference to the target unit).

*   **`IsFarEnoughToMoveStationaryFollower`**: Determines if a follower needs to move. It returns `true` if the owner is *not* within a distance of `1.4f * m_fOffset` from the target. This prevents jittery micro-adjustments when the target is stationary and the follower is already close enough.
*   **`UpdateAsync`**: The main entry point for asynchronous movement updates. It checks for validity (target exists, owner alive, not possessed/can't move). If valid, it locks the owner's movement spline mutex and calls `_setTargetLocation` to recalculate the path. It skips updates if the target is lost or the owner is under a "no movement" spell effect.
*   **`UpdateFinalDistance`**: Specialized implementations exist for `Player` (does nothing) and `Creature` (updates `m_fOffset` and sets `m_bRecalculateTravel = true`). This allows dynamic adjustment of the follow/chase distance.
*   **`IsReachable`**, **`GetTarget`**, **`UnitSpeedChanged`**: Simple accessors/mutators. `UnitSpeedChanged` forces a travel recalculation.

### Chasing Logic (`ChaseMovementGenerator`)

This generator moves the owner towards the target to engage in combat.

*   **`_setTargetLocation`**: The core pathfinding routine for chasing.
    1.  Validates target and owner mobility.
    2.  Handles transport mismatches (cannot path if on different transports).
    3.  If melee range is possible and Line-of-Sight (LOS) is clear, it may skip pathing entirely.
    4.  Calculates a destination point:
        *   If `m_fOffset` is 0 (melee), it uses `GetRandomAttackPoint` to avoid collision.
        *   If `m_fOffset` > 0 (ranged/pet), it calculates a point at `m_fOffset` distance and `m_fAngle` relative to the target's orientation. It uses movement extrapolation if configured.
    5.  Uses `PathFinder` to generate a path. If the path is incomplete or unreachable, it marks `m_bReachable = false`.
    6.  Applies special logic for pets (cutting paths through doors) and casters (updating for caster movement).
    7.  Launches the movement spline via `MoveSplineInit`.
    8.  Checks for "Fly-hacks": if the target is a player significantly higher than the destination, it triggers `OnUnreachable` on the player's cheat data.
*   **`Update`**: The tick handler for chase movement.
    1.  Checks for termination conditions (dead, lost target, no-movement spells).
    2.  Periodically (every 100ms via `m_checkDistanceTimer`), it checks if the target has moved significantly. If so, it sets `m_bRecalculateTravel = true` and requests an async update.
    3.  If the spline finalizes (movement completes):
        *   Calls `MovementInform` to notify the AI.
        *   If the target was reached, it calls `_reachTarget` (which initiates an attack).
        *   Triggers spread/backing logic (`DoBackMovement` or `DoSpreadIfNeeded`) if the creature is not flagged to disable it and hasn't exceeded spread attempts.
        *   Updates leash extension timers for creatures.
*   **`DoBackMovement`**: Moves the owner backward away from the target. Used when the target is deep in the owner's bounds (collision). It calculates a point behind the owner and launches a walk spline.
*   **`DoSpreadIfNeeded`**: Attempts to move the owner to the side to avoid overlapping with other attackers. It iterates through the target's attackers, finds one that is too close, and calculates a new position at a random angle offset. It limits spread attempts to `MAX_SPREAD_ATTEMPTS` (3).
*   **`TargetDeepInBounds`** / **`TargetWithinBoundsPercentDistance`**: Helper functions to determine if the target is physically inside the owner's bounding radius, triggering backing/spread logic.
*   **`_reachTarget`**: Initiates an attack on the target if melee range is reachable.
*   **`Initialize`** / **`Finalize`** / **`Interrupt`** / **`Reset`**: Lifecycle methods. `Initialize` adds `UNIT_STATE_CHASE` and `UNIT_STATE_CHASE_MOVE`. `Finalize`/`Interrupt` remove them. `Reset` re-initializes.
*   **`MovementInform`**: Notifies the `CreatureAI` (or summoner's AI) that chase movement has completed or been interrupted. It passes the target's GUID low.

### Following Logic (`FollowMovementGenerator`)

This generator moves the owner to stay near the target, typically for pets or summoned entities.

*   **`_setTargetLocation`**: Similar to chase, but with key differences:
    1.  Handles transport switching: if the target is on a transport and the owner is not, it adds the owner to the transport.
    2.  Calculates destination based on `m_fOffset` and `m_fAngle`.
    3.  Uses `PathFinder` with `cheat` mode enabled for pets (allows passing through certain obstacles).
    4.  Adjusts velocity: if the distance is large, it increases speed up to 2.1x to catch up. If distance is small (< 2.0f), it walks.
    5.  Sets facing to match the target's orientation.
*   **`Update`**: The tick handler for follow movement.
    1.  Checks termination conditions.
    2.  Periodically checks if the target moved. If the target is a stationary player and the follower is too far, it interrupts.
    3.  If the spline finalizes, it calls `MovementInform` and `_reachTarget` (which is empty for followers).
    4.  Ensures the owner faces the target if `m_fAngle` is 0.
*   **`EnableWalking`**: Returns `true` if the target is walking (for creatures). For players, it always returns `false` (players don't force followers to walk unless specified elsewhere, but here it's hardcoded false for Player specialization).
*   **`_updateSpeed`**: For creatures, it updates run/walk/swim speeds to match the owner/target context. For players, it does nothing.
*   **`Initialize`** / **`Finalize`** / **`Interrupt`** / **`Reset`**: Lifecycle methods. Adds/removes `UNIT_STATE_FOLLOW` and `UNIT_STATE_FOLLOW_MOVE`. Calls `_updateSpeed`.
*   **`MovementInform`**: Notifies the AI that follow movement has completed.

---

## Cross-Unit Boundaries

### Calls Out (Dependencies)

*   **`WorldObject.Object`**:
    *   `IsWithinDist`, `GetPositionX/Y/Z`, `GetClosePoint`, `GetNearPoint`, `GetAngle`, `IsMoving`, `IsInWorld`: Used extensively for spatial calculations, distance checks, and validating target existence.
    *   `PathFinder/calculate`, `CutPathWithDynamicLoS`, `Length`, `UpdateForCaster`: Core pathfinding operations.
    *   `GetObjectBoundingRadius`: Used for collision detection and spread logic.
*   **`Unit.Main`**:
    *   `AddUnitState`, `ClearUnitState`, `HasUnitState`: Manages internal unit state flags for chase/follow modes.
    *   `GetMotionMaster`: Accesses the motion system to set async updates.
    *   `GetAttackers`: Used in `DoSpreadIfNeeded` to find nearby enemies.
    *   `UpdateSpeed`: Syncs movement speeds in `FollowMovementGenerator`.
    *   `IsPet`, `IsCreature`, `IsPlayer`, `IsAlive`, `IsInCombat`, `IsMounted`: Type and state checks to tailor behavior.
    *   `GetOwnerGuid`, `GetCharmerOrOwner`: Used to determine master-slave relationships for speed adjustments.
*   **`Creature.Main`**:
    *   `AI`, `IsTemporarySummon`: Used in `MovementInform` to route notifications to the correct AI handler.
    *   `HasExtraFlag`, `UpdateLeashExtensionTime`: Specific creature logic for leash management and movement flags.
*   **`Player.Main`**:
    *   `GetCheatData`: Used to report unreachable targets (anti-cheat).
*   **`MoveSplineInit`**:
    *   `Launch`, `Move`, `MoveTo`, `SetWalk`, `SetFacingGUID`, `SetVelocity`: Constructs and starts the actual movement splines.
*   **`GenericTransport`**:
    *   `CalculatePassengerPosition`, `AddFollowerToTransport`, `RemoveFollowerFromTransport`: Handles coordinate transformations and entity attachment for vehicles.
*   **`PathFinder`**:
    *   `getPath`, `getPathType`, `SetTransport`: Retrieves path data and status.
*   **`World`**:
    *   `getConfig`: Checks `CONFIG_BOOL_ENABLE_MOVEMENT_EXTRAPOLATION_PET` to decide whether to predict target movement.
*   **`shared_Util`**:
    *   `getMSTime`, `urand`, `frand`: Time retrieval and random number generation for spread angles/timers.
*   **`MovementAnticheat`**:
    *   `OnUnreachable`: Reports suspicious vertical distance discrepancies.
*   **`TemporarySummon`**:
    *   `GetSummonerGuid`: Identifies who summoned the unit to notify the correct AI.
*   **`Map.Main`**:
    *   `GetCreature`, `GetGameObject`: Resolves summoner entities from GUIDs.
*   **`CreatureAI` / `GameObjectAI`**:
    *   `MovementInform`, `SummonedMovementInform`: Interfaces to notify AI systems of movement events.

### Called By

*   The MAP indicates no external units explicitly call into these members *from outside* in the provided cross-reference list, implying these are primarily driven by the internal `MotionMaster` loop or initialized by AI systems that instantiate these generators. However, `Initialize`, `Update`, `Finalize`, etc., are standard interface methods called by the `MotionMaster` infrastructure (not listed in the MAP's "Called by" column but implied by the class hierarchy `MovementGeneratorMedium`).

---

## Data Model

This unit interacts with **no database tables**. All data is transient, stored in memory within the `Unit`, `WorldObject`, and `PathFinder` objects.

---

## Notable Implementation Details

1.  **Asynchronous Path Recalculation**:
    The `UpdateAsync` method in `TargetedMovementGeneratorMedium` is critical for performance. Instead of recalculating paths every game tick, it uses a flag `m_bRecalculateTravel`. When the target moves significantly (detected in `Update`), this flag is set, and `MotionMaster` is asked to schedule an async update. This decouples heavy pathfinding from the main update loop.

2.  **Transport Coordinate Systems**:
    Both `_setTargetLocation` methods carefully handle `GenericTransport`. If the owner and target are on different transports, pathing is aborted (`m_bReachable = false`). If they are on the same transport, coordinates are transformed using `CalculatePassengerPosition`. Followers can switch transports to join their target, a feature unique to `FollowMovementGenerator`.

3.  **Spread and Backing Logic**:
    `ChaseMovementGenerator` includes sophisticated collision avoidance for melee mobs.
    *   **Backing**: If the target is "deep in bounds" (inside the mob's radius), the mob backs away.
    *   **Spreading**: If multiple mobs are attacking the same target, they attempt to spread out angularly. This is limited to 3 attempts (`MAX_SPREAD_ATTEMPTS`) to prevent infinite circling. The spread angle is random (`frand(0.4f, 1.0f)`).

4.  **Fly-Hack Detection**:
    In both `_setTargetLocation` and `Update`, there is a check: `(player->GetPositionZ() - allowed_dist - 5.0f) > dest.z`. If a player is significantly higher than the calculated destination (beyond allowed reach + 5 yards), it triggers `OnUnreachable`. This is a server-side anti-cheat measure to detect players using flight hacks to evade ground-based mobs.

5.  **Pet Pathfinding Cheats**:
    In `FollowMovementGenerator::_setTargetLocation`, `path.calculate(x, y, z, true)` is called with `true` for the cheat parameter. This allows pets to path through certain obstacles (like closed doors) that regular creatures cannot, ensuring they can follow their masters more reliably.

6.  **Movement Extrapolation**:
    If `CONFIG_BOOL_ENABLE_MOVEMENT_EXTRAPOLATION_PET` is enabled, the code predicts the target's future position (`ExtrapolateMovement`) to aim the path ahead of the target, reducing lag-induced overshooting.

7.  **Template Specializations**:
    Significant logic differs between `Player` and `Creature` instantiations.
    *   `UpdateFinalDistance` is a no-op for Players.
    *   `EnableWalking` for `FollowMovementGenerator` returns `false` for Players but checks the target's walking state for Creatures.
    *   `_updateSpeed` is a no-op for Players but syncs speeds for Creatures.

8.  **State Flags**:
    The unit strictly manages `UNIT_STATE_CHASE`, `UNIT_STATE_CHASE_MOVE`, `UNIT_STATE_FOLLOW`, and `UNIT_STATE_FOLLOW_MOVE`. These flags are added in `Initialize` and removed in `Finalize`/`Interrupt`, ensuring the rest of the engine knows the unit's movement intent.

---

## Member Reference

**IsFarEnoughToMoveStationaryFollower**
Checks if the owner is outside the threshold (`1.4f * m_fOffset`) of the target. Returns `true` if movement is needed.

**TargetedMovementGeneratorMedium<T, D>**
Constructor initializing offset, angle, timers, and target reference.

**UpdateFinalDistance#3**
Specialization for `Player`/`Chase`: No operation.

**UpdateFinalDistance#4**
Specialization for `Player`/`Follow`: No operation.

**~TargetedMovementGeneratorMedium<T, D>**
Destructor.

**IsReachable**
Returns the `m_bReachable` flag indicating if the last path calculation succeeded.

**UpdateFinalDistance**
Virtual method declaration.

**GetTarget**
Returns the pointer to the target unit.

**UnitSpeedChanged**
Sets `m_bRecalculateTravel` to `true` to trigger a path update.

**UpdateFinalDistance#2**
Specialization for `Creature`/`Chase`: Updates `m_fOffset` and sets recalculation flag.

**UpdateFinalDistance#5**
Specialization for `Creature`/`Follow`: Updates `m_fOffset` and sets recalculation flag.

**_setTargetLocation#3**
Declaration for `ChaseMovementGenerator`.

**UpdateAsync**
Handles asynchronous path recalculation. Checks validity, locks mutex, and calls `_setTargetLocation`.

**_setTargetLocation**
Core pathfinding logic for `ChaseMovementGenerator`. Calculates destination, handles transports, runs `PathFinder`, and launches spline. Includes fly-hack detection.

**ChaseMovementGenerator<T>**
Constructor with default offset/angle.

**ChaseMovementGenerator<T>#2**
Constructor with custom offset/angle.

**~ChaseMovementGenerator<T>**
Destructor.

**GetMovementGeneratorType**
Returns `CHASE_MOTION_TYPE`.

**Initialize#5**
Declaration for `ChaseMovementGenerator`.

**_clearUnitStateMove**
Static helper to clear `UNIT_STATE_CHASE_MOVE`.

**_addUnitStateMove**
Static helper to add `UNIT_STATE_CHASE_MOVE`.

**EnableWalking#3**
Always returns `false` for chasers.

**_lostTarget**
Checks if the owner's current victim is no longer the chase target.

**FollowMovementGenerator<T>**
Constructor with default offset/angle.

**FollowMovementGenerator<T>#2**
Constructor with custom offset/angle.

**~FollowMovementGenerator<T>**
Destructor.

**GetMovementGeneratorType#2**
Returns `FOLLOW_MOTION_TYPE`.

**Initialize#6**
Declaration for `FollowMovementGenerator`.

**_clearUnitStateMove#2**
Static helper to clear `UNIT_STATE_FOLLOW_MOVE`.

**_addUnitStateMove#2**
Static helper to add `UNIT_STATE_FOLLOW_MOVE`.

**EnableWalking#4**
Declaration for `FollowMovementGenerator`.

**_lostTarget#2**
Always returns `false` for followers (they don't lose target by changing victims).

**_reachTarget#2**
Empty implementation for followers.

**_updateSpeed#3**
Declaration for `FollowMovementGenerator`.

**Update**
Tick handler for `ChaseMovementGenerator`. Checks for target movement, triggers async updates, handles spline finalization, spread/backing logic, and leash extensions.

**TargetDeepInBounds**
Helper to check if target is 50% inside owner's bounds.

**TargetWithinBoundsPercentDistance**
Helper to check if target is within a percentage of the owner's bounding radius.

**DoBackMovement**
Moves owner backward to resolve collision with target.

**DoSpreadIfNeeded**
Moves owner sideways to avoid overlapping with other attackers.

**_reachTarget**
Initiates attack on target if melee range is reachable.

**Initialize#2**
Specialization for `Player`/`Chase`: Adds states, sets recalculation flag, requests async update.

**Initialize**
Specialization for `Creature`/`Chase`: Sets walk to false, adds states, sets recalculation flag, requests async update.

**Finalize**
Removes chase states.

**Interrupt**
Removes chase states.

**Reset**
Re-initializes the generator.

**MovementInform#3**
Empty implementation for non-Creature chasers.

**MovementInform**
Notifies `CreatureAI` or summoner's AI of chase movement completion/interruption.

**_setTargetLocation#2**
Core pathfinding logic for `FollowMovementGenerator`. Handles transport switching, calculates destination, runs `PathFinder` (with cheat for pets), adjusts velocity, and launches spline.

**Update#2**
Tick handler for `FollowMovementGenerator`. Checks for target movement, triggers async updates, handles spline finalization, and ensures facing.

**EnableWalking**
Specialization for `Creature`/`Follow`: Returns true if target is walking.

**EnableWalking#2**
Specialization for `Player`/`Follow`: Always returns false.

**_updateSpeed#2**
Specialization for `Player`/`Follow`: No operation.

**_updateSpeed**
Specialization for `Creature`/`Follow`: Updates run/walk/swim speeds if owner matches target's owner.

**Initialize#4**
Specialization for `Player`/`Follow`: Adds states, updates speed, sets target location.

**Initialize#3**
Specialization for `Creature`/`Follow`: Adds states, updates speed, sets target location.

**Finalize#2**
Removes follow states and updates speed.

**Interrupt#2**
Removes follow states and updates speed.

**Reset#2**
Re-initializes the generator.

**MovementInform#4**
Empty implementation for non-Creature followers.

**MovementInform#2**
Notifies `CreatureAI` or summoner's AI of follow movement completion/interruption.

---

<!-- machine-true, projected from graph.json -->

## Map — TargetedMovementGenerator

*Source:* TargetedMovementGenerator.cpp, TargetedMovementGenerator.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| IsFarEnoughToMoveStationaryFollower | function | WorldObject.Object/IsWithinDist | — | — |
| TargetedMovementGeneratorMedium<T, D> | ctor | — | — | — |
| UpdateFinalDistance#3 | method | — | — | — |
| UpdateFinalDistance#4 | method | — | — | — |
| ~TargetedMovementGeneratorMedium<T, D> | dtor | — | — | — |
| IsReachable | function | — | — | — |
| UpdateFinalDistance | method | — | — | — |
| GetTarget | function | — | — | — |
| UnitSpeedChanged | function | — | — | — |
| UpdateFinalDistance#2 | method | — | — | — |
| UpdateFinalDistance#5 | decl | — | — | — |
| _setTargetLocation#3 | decl | — | — | — |
| UpdateAsync | function | Object/IsInWorld | — | — |
| _setTargetLocation | function | GenericTransport/CalculatePassengerPosition, MovementAnticheat/OnUnreachable, MoveSplineInit/Launch, MoveSplineInit/Move, MoveSplineInit/SetFacingGUID, MoveSplineInit/SetWalk, PathInfo/getPath, PathInfo/getPathType, PathInfo/SetTransport, Player.Main/GetCheatData, Position/Position, shared_Util/getMSTime, World/getConfig, WorldObject.Object/GetPositionZ, WorldObject.PathFinder/calculate#2, WorldObject.PathFinder/CutPathWithDynamicLoS, WorldObject.PathFinder/Length, WorldObject.PathFinder/UpdateForCaster | — | — |
| ChaseMovementGenerator<T> | ctor | — | — | — |
| ChaseMovementGenerator<T>#2 | ctor | — | — | — |
| ~ChaseMovementGenerator<T> | dtor | — | — | — |
| GetMovementGeneratorType | function | — | — | — |
| Initialize#5 | decl | — | — | — |
| _clearUnitStateMove | function | — | — | — |
| _addUnitStateMove | function | — | — | — |
| EnableWalking#3 | function | — | — | — |
| _lostTarget | function | — | — | — |
| FollowMovementGenerator<T> | ctor | — | — | — |
| FollowMovementGenerator<T>#2 | ctor | — | — | — |
| ~FollowMovementGenerator<T> | dtor | — | — | — |
| GetMovementGeneratorType#2 | function | — | — | — |
| Initialize#6 | decl | — | — | — |
| _clearUnitStateMove#2 | function | — | — | — |
| _addUnitStateMove#2 | function | — | — | — |
| EnableWalking#4 | decl | — | — | — |
| _lostTarget#2 | function | — | — | — |
| _reachTarget#2 | function | — | — | — |
| _updateSpeed#3 | decl | — | — | — |
| Update | function | Creature.Main/HasExtraFlag, Creature.Main/IsPet, Creature.Main/UpdateLeashExtensionTime, GenericTransport/CalculatePassengerPosition, MovementAnticheat/OnUnreachable, Player.Main/GetCheatData, shared_Util/urand, ShortTimeTracker/Passed, ShortTimeTracker/Reset, ShortTimeTracker/Update, WorldObject.Object/GetPositionZ | — | — |
| TargetDeepInBounds | function | — | — | — |
| TargetWithinBoundsPercentDistance | function | Unit.Main/GetObjectBoundingRadius, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | — | — |
| DoBackMovement | function | MoveSplineInit/Launch, MoveSplineInit/MoveTo#2, MoveSplineInit/SetWalk, Unit.Main/GetObjectBoundingRadius, WorldObject.Object/GetClosePoint | — | — |
| DoSpreadIfNeeded | function | MoveSplineInit/Launch, MoveSplineInit/MoveTo#2, MoveSplineInit/SetWalk, Object/IsCreature, shared_Util/frand, Unit.Main/GetAttackers, Unit.Main/GetObjectBoundingRadius, WorldObject.Object/GetAngle, WorldObject.Object/GetNearPoint, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/IsMoving | — | — |
| _reachTarget | function | — | — | — |
| Initialize#2 | method | MotionMaster/SetNeedAsyncUpdate, Unit.Main/AddUnitState, Unit.Main/GetMotionMaster | — | — |
| Initialize | method | MotionMaster/SetNeedAsyncUpdate, Unit.Main/AddUnitState, Unit.Main/GetMotionMaster, Unit.Main/SetWalk | — | — |
| Finalize | function | — | — | — |
| Interrupt | function | — | — | — |
| Reset | function | — | — | — |
| MovementInform#3 | function | — | — | — |
| MovementInform | method | Creature.Main/AI, Creature.Main/IsTemporarySummon, CreatureAI/MovementInform, CreatureAI/SummonedMovementInform, GameObject/AI, GameObjectAI/SummonedMovementInform, Map.Main/GetCreature, Map.Main/GetGameObject, Object/GetGUIDLow, ObjectGuid/IsCreature, TemporarySummon/GetSummonerGuid, Unit.Main/IsAlive, WorldObject.Object/GetMap | — | — |
| _setTargetLocation#2 | function | GenericTransport/CalculatePassengerPosition, MoveSplineInit/Launch, MoveSplineInit/Move, MoveSplineInit/SetVelocity, MoveSplineInit/SetWalk, Object/IsPlayer, PathInfo/getPathType, PathInfo/SetTransport, shared_Util/getMSTime, Transport/AddFollowerToTransport, Unit.Main/IsInCombat, Unit.Main/IsMounted, World/getConfig, WorldObject.PathFinder/calculate#2, WorldObject.PathFinder/Length | — | — |
| Update#2 | function | GenericTransport/CalculatePassengerPosition | — | — |
| EnableWalking | method | WorldObject.Object/IsWalking | — | — |
| EnableWalking#2 | method | — | — | — |
| _updateSpeed#2 | method | — | — | — |
| _updateSpeed | method | Object/GetObjectGuid, ObjectGuid/operator!=, Unit.Main/GetOwnerGuid, Unit.Main/UpdateSpeed | — | — |
| Initialize#4 | method | Unit.Main/AddUnitState | — | — |
| Initialize#3 | method | Unit.Main/AddUnitState | — | — |
| Finalize#2 | function | — | — | — |
| Interrupt#2 | function | — | — | — |
| Reset#2 | function | — | — | — |
| MovementInform#4 | function | — | — | — |
| MovementInform#2 | method | Creature.Main/AI, Creature.Main/IsTemporarySummon, CreatureAI/MovementInform, CreatureAI/SummonedMovementInform, GameObject/AI, GameObjectAI/SummonedMovementInform, Map.Main/GetCreature, Map.Main/GetGameObject, Object/GetGUIDLow, ObjectGuid/IsCreature, TemporarySummon/GetSummonerGuid, Unit.Main/IsAlive, WorldObject.Object/GetMap | — | — |
