# PointMovementGenerator

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# PointMovementGenerator

**Purpose & Responsibilities**

`PointMovementGenerator` and its derived classes define the core logic for moving `Unit` objects (specifically `Player` and `Creature`) to specific coordinates or along calculated paths within the WoWVMaNGOS world. This unit implements several distinct movement strategies:

1.  **Point-to-Point Movement (`PointMovementGenerator`)**: Moves a unit to a fixed set of coordinates `(x, y, z)` with optional orientation, speed, and movement mode (walk/run/fly/fall). It handles the lifecycle of the movement spline, notifying the unit's AI when the movement completes.
2.  **Distancing Movement (`DistancingMovementGenerator`)**: A specialized point movement used primarily for kiting or fleeing, ensuring the unit maintains distance while moving to a target location.
3.  **Assistance Movement (`AssistanceMovementGenerator`)**: Moves a `Creature` to a specific location to assist an ally, typically triggered by assistance calls. Upon arrival, it triggers further assistance-seeking behaviors.
4.  **Effect Movement (`EffectMovementGenerator`)**: A minimal generator used to prevent other movement generators from interrupting a spell effect or channel. It does not actively drive movement but acts as a protective layer.
5.  **Charge Movement (`ChargeMovementGenerator`)**: Implements complex charging mechanics, including pathfinding to a victim, predicting the victim's future position based on latency and movement vectors, and triggering attacks upon arrival.

These classes integrate with the `MoveSpline` system to handle the actual interpolation of movement and with the AI systems (`PlayerAI`, `CreatureAI`) to notify them of movement completion or interruption.

## Member-by-Member Behavior

### PointMovementGenerator<T>

This template class manages basic movement to a static destination.

*   **Construction (`PointMovementGenerator<T>`)**: Initializes the generator with a unique ID, destination coordinates `(x, y, z)`, movement options (e.g., walk, run, fly), speed, and final orientation.
*   **Initialization (`Initialize`)**: Stops any current movement, sets the unit's state to `ROAMING` and `ROAMING_MOVE`, and configures a `MoveSplineInit` object. It applies the stored destination, speed, orientation, and movement modes (walk/run/fly/fall/cyclic) before launching the spline.
*   **Finalization (`Finalize`)**: Clears the roaming states and calls `MovementInform` to notify the unit's AI that the movement has completed.
*   **Interruption (`Interrupt`)**: Clears the roaming states if the movement is interrupted before completion.
*   **Reset (`Reset`)**: Stops current movement and re-applies the roaming states, effectively restarting the movement intent without recalculating the destination.
*   **Update (`Update`)**: Checks if the unit is unable to move. If the movement spline is not finalized and speed recalculation is pending, it re-initializes the movement. Returns `true` if the movement is still ongoing.
*   **Movement Inform (`MovementInform`)**: Specialized for `Player` and `Creature`. If the unit is alive, it notifies the respective AI (`PlayerAI::MovementInform` or `CreatureAI::MovementInform`). For summoned creatures, it also notifies the summoner's AI (`CreatureAI::SummonedMovementInform` or `GameObjectAI::SummonedMovementInform`) if the summoner is a creature or game object.
*   **Speed Change (`UnitSpeedChanged`)**: Flags that the speed needs to be recalculated on the next update.
*   **Type Retrieval (`GetMovementGeneratorType`)**: Returns `POINT_MOTION_TYPE`.
*   **Destination Retrieval (`GetDestination`)**: Returns the stored destination coordinates.

### DistancingMovementGenerator<T>

Derived from `PointMovementGenerator`, this class modifies the update and inform behavior for distancing scenarios.

*   **Construction (`DistancingMovementGenerator<T>`)**: Initializes with destination coordinates and default options for pathfinding and running.
*   **Update (`Update`)**: Similar to the base class but returns `false` if the unit cannot move, rather than continuing to return `true` while waiting.
*   **Movement Inform (`MovementInform`)**: For `Creature`, it triggers `CreatureAI::DoSpellsListCasts(1)`, likely to cast spells associated with distancing. For `Player`, it does nothing.
*   **Type Retrieval (`GetMovementGeneratorType`)**: Returns `DISTANCING_MOTION_TYPE`.

### AssistanceMovementGenerator

Derived from `PointMovementGenerator<Creature>`, this class handles movement to assist allies.

*   **Construction (`AssistanceMovementGenerator`)**: Initializes with destination coordinates.
*   **Initialization (`Initialize`)**: Checks if the unit can react or move. If so, it stops current movement, sets roaming states, and launches a spline to the destination using the unit's fleeing speed.
*   **Finalization (`Finalize`)**: Clears roaming states, disables the "no call assistance" flag, calls `Creature::CallAssistance`, and if the unit is alive, initiates a seek-assistance-distract movement based on a world configuration delay.

### EffectMovementGenerator

A lightweight generator to protect active effects from being interrupted by other movement commands.

*   **Construction (`EffectMovementGenerator`)**: Stores an ID for the effect.
*   **Initialization/Interruption/Reset**: Empty implementations, as it does not drive movement.
*   **Update (`Update`)**: Returns whether the underlying movement spline is finalized.
*   **Finalization (`Finalize`)**: If the unit is a `Creature` and the spline is finalized, it notifies the AI via `CreatureAI::MovementInform` with `EFFECT_MOTION_TYPE`. It then restores the unit's previous movement state.

### ChargeMovementGenerator<T>

Implements complex charging logic with pathfinding and victim prediction.

*   **Construction (`ChargeMovementGenerator<T>`)**: Initializes with attacker, victim, extrapolation delay, attack trigger flag, speed, and melee reach. It immediately computes the initial path.
*   **Path Computation (`ComputePath`)**:
    *   Sets the transport if the attacker is on one.
    *   Calculates speed (defaulting to 4x run speed, capped at 24.0).
    *   Gets the victim's current position.
    *   Calculates a base path to the victim.
    *   If the victim is a moving player and movement extrapolation is enabled, it predicts the victim's future position by accounting for spell batching time, client movement time, and network latency. It updates the path to this predicted position and adjusts for melee reach.
    *   If not a moving player, it updates the path for melee reach based on the current victim position.
*   **Initialization (`Initialize`)**: Stops current movement, checks if a valid path exists, sets roaming states, clears movement flags, and launches a spline along the calculated path, facing the target GUID.
*   **Finalization (`Finalize`)**: Clears roaming states. Applies pending root/stun states. If configured to trigger an attack, it attacks the victim if the victim is still selected or if the attacker is not a player. Restores previous movement.
*   **Interruption (`Interrupt`)**: Clears roaming states and applies pending root/stun states.
*   **Reset (`Reset`)**: Clears roaming states and applies pending root/stun states.
*   **Update (`Update`)**: Handles scheduled stop movements. If speed recalculation is needed, it recalculates the path to the current end position and re-initializes. Returns whether the spline is finalized.
*   **Speed Change (`UnitSpeedChanged`)**: Flags for speed recalculation.
*   **Type Retrieval (`GetMovementGeneratorType`)**: Returns `CHARGE_MOTION_TYPE`.

## Cross-Unit Boundaries

*   **MoveSplineInit / MoveSpline**: All movement generators rely heavily on `MoveSplineInit` to configure and launch movement splines, and `MoveSpline` to check finalization status. The generators pass coordinates, speeds, orientations, and modes to `MoveSplineInit`.
*   **AI Systems (PlayerAI, CreatureAI, GameObjectAI)**:
    *   `PointMovementGenerator::MovementInform` calls `PlayerAI::MovementInform` or `CreatureAI::MovementInform` to notify the AI of movement completion.
    *   For summoned creatures, it calls `CreatureAI::SummonedMovementInform` or `GameObjectAI::SummonedMovementInform` on the summoner.
    *   `DistancingMovementGenerator::MovementInform` calls `CreatureAI::DoSpellsListCasts`.
    *   `EffectMovementGenerator::Finalize` calls `CreatureAI::MovementInform`.
*   **Unit State Management**: Generators call `Unit::AddUnitState`, `Unit::ClearUnitState`, `Unit::HasUnitState`, `Unit::IsStopped`, `Unit::StopMoving`, and `Unit::RestoreMovement` to manage the unit's internal state flags related to movement and control.
*   **Pathfinding**: `ChargeMovementGenerator` uses `PathFinder` to calculate paths to victims, updating for melee reach and transports.
*   **World Configuration**: `AssistanceMovementGenerator::Finalize` reads `CONFIG_UINT32_CREATURE_FAMILY_ASSISTANCE_DELAY`. `ChargeMovementGenerator::ComputePath` reads `CONFIG_BOOL_ENABLE_MOVEMENT_EXTRAPOLATION_CHARGE`.
*   **Player Session/Latency**: `ChargeMovementGenerator::ComputePath` accesses `Player::GetSession()->GetLatency()` to improve victim position prediction.
*   **Map/Object Retrieval**: `PointMovementGenerator::MovementInform` uses `Map::GetCreature` and `Map::GetGameObject` to find summoners. `ChargeMovementGenerator::Finalize` uses `Map::GetUnit` to retrieve the victim for attacking.

## Data Model

This unit does not interact directly with database tables. It operates entirely on in-memory object states and configurations.

## Notable Implementation Details

*   **Victim Prediction in Charges**: `ChargeMovementGenerator::ComputePath` contains sophisticated logic to predict where a moving player victim will be when the charge arrives. It accounts for server-side spell batching delays, the time the client has been moving since the last packet, and network latency. This prediction is capped at 1500ms of delay.
*   **Summoner Notification**: When a summoned creature finishes a point movement, `PointMovementGenerator::MovementInform` checks if the summoner is a creature or game object and notifies their AI. This allows summoners to react to their summons' actions.
*   **Pending States in Charge**: `ChargeMovementGenerator` carefully manages `UNIT_STATE_PENDING_ROOT` and `UNIT_STATE_PENDING_STUNNED` during finalization, interruption, and reset. These pending states are converted to actual root/stun states, ensuring that crowd control effects applied during a charge take effect immediately upon stopping.
*   **Effect Protection**: `EffectMovementGenerator` is designed to be non-intrusive. Its primary role is to exist in the movement master stack to prevent other generators from interrupting an active effect, rather than to drive movement itself.
*   **Speed Recalculation**: Both `PointMovementGenerator` and `ChargeMovementGenerator` support dynamic speed changes via `UnitSpeedChanged`, which flags `m_recalculateSpeed`. The next `Update` cycle will re-initialize the movement with the new speed.

## Member Reference

*   **PointMovementGenerator<T>**: Constructor initializing ID, coordinates, options, speed, and orientation.
*   **Initialize#3**: Stops unit, sets roaming states, configures and launches `MoveSplineInit` with destination, speed, orientation, and movement modes.
*   **~PointMovementGenerator<T>**: Destructor.
*   **MovementInform#6**: Declaration for `Player` specialization.
*   **UnitSpeedChanged#2**: Sets `m_recalculateSpeed` to true.
*   **GetMovementGeneratorType#3**: Returns `POINT_MOTION_TYPE`.
*   **GetDestination**: Returns stored x, y, z coordinates.
*   **Finalize#4**: Clears roaming states and calls `MovementInform`.
*   **DistancingMovementGenerator<T>**: Constructor initializing with coordinates and default pathfinding/run options.
*   **GetMovementGeneratorType#2**: Returns `DISTANCING_MOTION_TYPE`.
*   **Interrupt#2**: Clears roaming states.
*   **MovementInform#5**: Declaration for `Creature` specialization.
*   **Reset#2**: Stops unit and sets roaming states.
*   **Update#4**: Checks move ability, handles speed recalculation, returns spline finalization status.
*   **MovementInform#4**: Notifies `PlayerAI::MovementInform` if player is alive.
*   **ChargeMovementGenerator<T>**: Constructor initializing path, victim GUID, speed, and computing initial path.
*   **MovementInform#3**: Notifies `CreatureAI::MovementInform` and summoner AI if applicable.
*   **GetMovementGeneratorType**: Returns `CHARGE_MOTION_TYPE`.
*   **UnitSpeedChanged**: Sets `m_recalculateSpeed` to true.
*   **Update#3**: Handles stop scheduling, speed recalculation, and returns spline finalization status.
*   **MovementInform**: Notifies `CreatureAI::MovementInform` with `EFFECT_MOTION_TYPE` and restores movement.
*   **MovementInform#2**: Empty implementation for `Player`.
*   **Initialize**: Stops unit, checks reaction/move ability, sets roaming states, launches spline with fleeing speed.
*   **Finalize**: Clears roaming states, calls assistance, initiates seek-assistance-distract movement.
*   **Update**: Returns spline finalization status.
*   **Finalize#2**: Clears roaming states, applies pending root/stun, triggers attack if configured, restores movement.
*   **Initialize#2**: Stops unit, checks path validity, sets roaming states, launches spline along path facing target.
*   **Finalize#3**: Clears roaming states and calls `MovementInform`.
*   **ComputePath**: Calculates path to victim, predicts player position using latency and movement data, updates for melee reach.
*   **Interrupt**: Clears roaming states, applies pending root/stun.
*   **Reset**: Clears roaming states, applies pending root/stun.
*   **Update#2**: Recalculates path if speed changed, returns spline finalization status.

---

<!-- machine-true, projected from graph.json -->

## Map — PointMovementGenerator

*Source:* PointMovementGenerator.cpp, PointMovementGenerator.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| PointMovementGenerator<T> | ctor | — | — | — |
| Initialize#3 | function | MoveSplineInit/Launch, MoveSplineInit/MoveTo#2, MoveSplineInit/SetCyclic, MoveSplineInit/SetFacing#2, MoveSplineInit/SetFall, MoveSplineInit/SetFly, MoveSplineInit/SetVelocity, MoveSplineInit/SetWalk | — | — |
| ~PointMovementGenerator<T> | dtor | — | — | — |
| MovementInform#6 | decl | — | — | — |
| UnitSpeedChanged#2 | function | — | — | — |
| GetMovementGeneratorType#3 | function | — | — | — |
| GetDestination | function | — | — | — |
| Finalize#4 | function | — | — | — |
| DistancingMovementGenerator<T> | ctor | — | — | — |
| GetMovementGeneratorType#2 | function | — | — | — |
| Interrupt#2 | function | — | — | — |
| MovementInform#5 | decl | — | — | — |
| Reset#2 | function | — | — | — |
| Update#4 | function | — | — | — |
| MovementInform#4 | method | Player.Main/AI, PlayerAI/MovementInform, Unit.Main/IsAlive | — | — |
| ChargeMovementGenerator<T> | ctor | — | — | — |
| MovementInform#3 | method | Creature.Main/AI, Creature.Main/IsTemporarySummon, CreatureAI/MovementInform, CreatureAI/SummonedMovementInform, GameObject/AI, GameObjectAI/SummonedMovementInform, Map.Main/GetCreature, Map.Main/GetGameObject, ObjectGuid/IsCreature, TemporarySummon/GetSummonerGuid, Unit.Main/IsAlive, WorldObject.Object/GetMap | — | — |
| GetMovementGeneratorType | function | — | — | — |
| UnitSpeedChanged | function | — | — | — |
| Update#3 | function | — | — | — |
| MovementInform | method | Creature.Main/AI, CreatureAI/DoSpellsListCasts | — | — |
| MovementInform#2 | method | — | — | — |
| Initialize | method | Creature.Main/GetFleeingSpeed, MoveSplineInit/Launch, MoveSplineInit/MoveSplineInit, MoveSplineInit/MoveTo#2, MoveSplineInit/SetVelocity, MoveSplineInit/SetWalk, Unit.Main/AddUnitState, Unit.Main/HasUnitState, Unit.Main/IsStopped, Unit.Main/StopMoving | — | — |
| Finalize | method | Creature.Main/CallAssistance, Creature.Main/SetNoCallAssistance, Creature.MotionMaster/MoveSeekAssistanceDistract, Unit.Main/ClearUnitState, Unit.Main/GetMotionMaster, Unit.Main/IsAlive, World/getConfig#4 | — | — |
| Update | method | MoveSpline/Finalized | — | — |
| Finalize#2 | method | Creature.Main/AI, CreatureAI/MovementInform, MoveSpline/Finalized, Object/GetTypeId, Unit.Main/RestoreMovement | — | — |
| Initialize#2 | function | MoveSplineInit/Launch, MoveSplineInit/Move, MoveSplineInit/SetFacingGUID, MoveSplineInit/SetVelocity, MoveSplineInit/SetWalk, PathInfo/getPathType | — | — |
| Finalize#3 | function | — | — | — |
| ComputePath | function | Object/ToPlayer, PathInfo/SetTransport, Player.Main/GetSession, shared_Util/getMSTime, Unit.Main/ExtrapolateMovement, Unit.Main/IsMovedByPlayer, World/getConfig, WorldObject.Object/GetPosition#2, WorldObject.Object/GetPositionZ, WorldObject.Object/IsMoving, WorldObject.Object/UpdateAllowedPositionZ, WorldObject.PathFinder/calculate#2, WorldObject.PathFinder/UpdateForMelee, WorldSession.Main/GetLatency | — | — |
| Interrupt | function | — | — | — |
| Reset | function | — | — | — |
| Update#2 | function | PathInfo/getEndPosition#2, WorldObject.PathFinder/calculate#2 | — | — |
