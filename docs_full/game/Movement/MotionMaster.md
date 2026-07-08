<!-- provenance: verbose -->
# MotionMaster

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# MotionMaster

## Purpose & Responsibilities

`MotionMaster` manages the movement state of a `Unit` using a priority stack of `MovementGenerator` objects. Inheriting from `std::stack<MovementGenerator*>`, it ensures the most recently added movement (highest priority) is active at the top. When a high-priority movement (e.g., fleeing, chasing) completes or is cleared, it is removed, revealing the underlying lower-priority movement (e.g., idle, patrol).

Responsibilities:
1.  **Stack Orchestration:** Pushing, popping, and iterating over movement generators.
2.  **Lifecycle Management:** Initializing default movements, handling expiration, and cleaning up resources safely during update loops.
3.  **Update Coordination:** Integrating with the game loop via `UpdateMotion` and supporting asynchronous updates via `NeedsAsyncUpdate`.
4.  **Command Interface:** Providing `Move*` methods for AI, spells, and commands to request specific behaviors.

It delegates pathfinding and coordinate calculation to `MovementGenerator` subclasses.

## Member-by-Member Behavior

### Construction and Initialization

*   **`MotionMaster` (ctor):** Initializes the master for a `Unit`, setting `m_needsAsyncUpdate` to false, storing the owner pointer, and initializing the stack.

### Stack Access and Iteration

*   **`GetCurrent`:** Returns a const pointer to the top `MovementGenerator`. Primary query for current movement state.
*   **`begin` / `end`:** Non-const iterators for the underlying container. Used for debugging (`ChatHandler.UnitCommands/HandleMovegensCommand`) and internal modifications (`Creature.MotionMaster/ClearType`, `ReInitializePatrolMovement`).
*   **`begin#2` / `end#2`:** Const iterators. Used by `Creature.MotionMaster/GetUsedMovementGeneratorsList` to read the stack without modification.
*   **`erase`:** Removes an element at a specific iterator. Used by `Creature.MotionMaster/ClearType` to remove specific generators.

### Movement Control and Cleanup

*   **`Clear`:** Removes generators from the stack. Uses `DirectClean` for immediate removal or `DelayedClean` if called during an update cycle (`MMCF_UPDATE` flag) to prevent iterator invalidation. Called extensively by AI, Unit core, and scripts to stop movement.
*   **`MovementExpired`:** Handles completion of a generator. Pops the top generator and cleans up. Like `Clear`, it supports delayed expiration via `DelayedExpire` if called during an update. Called by AI, Chat handlers, and Unit core when movements finish.
*   **`ClearType`:** Removes all generators of a specific `MovementGeneratorType` from the stack.

### Asynchronous Update Handling

*   **`NeedsAsyncUpdate`:** Returns `m_needsAsyncUpdate`. Checked by `Unit.Main/Update` to trigger `UpdateMotionAsync`.
*   **`SetNeedAsyncUpdate`:** Sets `m_needsAsyncUpdate` to true. Called by `RandomMovementGenerator/Update` and `TargetedMovementGenerator/Initialize` when immediate processing is required.

## Cross-Unit Boundaries

### Called By (Inputs)

*   **AI Systems (`AiBotAI`, `BattleBotAI`, `CreatureAI`, `PetAI`, etc.):**
    *   **`GetCurrent`:** Queries current movement to decide actions (e.g., `AiBotAI.Combat/CheckForUnreachableTarget`).
    *   **`Clear`:** Stops movement on state changes (e.g., `CreatureAI/EnterEvadeMode`, `PetAI/_stopAttack`).
    *   **`MovementExpired`:** Reacts to movement completion (e.g., `ScriptedEscortAI/Start`).
*   **Unit Core (`Unit.Main`):**
    *   **`GetCurrent`:** Checks reachability (`CantPathToVictim`) and casting ability (`TryToCast`).
    *   **`NeedsAsyncUpdate`:** Drives async updates in `Unit.Main/Update`.
    *   **`Clear`:** Clears movement on death (`SetDeathState`) or charm (`HandleModCharm`).
*   **Chat Handlers (`ChatHandler`):**
    *   **`GetCurrent`:** Displays waypoint info (`HandleWpShowCommand`).
    *   **`begin`/`end`:** Lists generators (`HandleMovegensCommand`).
    *   **`Clear`:** Debug stop (`HandleDebugMoveCommand`).
*   **Spells and Effects (`Unit.SpellAuras`):**
    *   **`Clear`:** Prevents conflict with roots/charms.
    *   **`MovementExpired`:** Triggers effects on movement end.

### Calls Out (Outputs)

*   **`MovementGenerator` Subclasses:**
    *   `Move*` methods construct and push specific generators.
    *   `SetNeedAsyncUpdate` is called by `RandomMovementGenerator` and `TargetedMovementGenerator` for immediate processing.

## Data Model

`MotionMaster` does not access database tables directly. It operates on in-memory `MovementGenerator` objects. Waypoint data used by `MoveWaypoint` is loaded into memory by other components (`Creature`, `Map`).

## Notable Implementation Details

1.  **Stack Priority:** Last-in-first-out ensures high-priority movements override lower ones.
2.  **Delayed Cleanup:** `MMCF_UPDATE` flag enables `DelayedClean`/`DelayedExpire` to safely modify the stack during updates.
3.  **Async Updates:** `m_needsAsyncUpdate` allows critical movements to bypass standard ticks.
4.  **Friend Class:** `AiBotMovementIssuer` is a friend, allowing direct injection of custom generators via `Mutate`.

## Member Reference

**MotionMaster**
Constructor. Initializes the motion master for a given `Unit`, setting up internal flags and the owner pointer. Inherits from `std::stack<MovementGenerator*>`.

**GetCurrent**
Returns a const pointer to the `MovementGenerator` at the top of the stack. Used by AI, Unit core, and Chat handlers to query the current movement state.

**begin#2**
Returns a const iterator to the beginning of the underlying container. Used by `Creature.MotionMaster/GetUsedMovementGeneratorsList` to iterate over all movement generators.

**end#2**
Returns a const iterator to the end of the underlying container. Used by `Creature.MotionMaster/GetUsedMovementGeneratorsList` to iterate over all movement generators.

**begin**
Returns a non-const iterator to the beginning of the underlying container. Used by `ChatHandler.UnitCommands/HandleMovegensCommand`, `Creature.MotionMaster/ClearType`, and `Creature.MotionMaster/ReInitializePatrolMovement` to inspect or modify the stack.

**end**
Returns a non-const iterator to the end of the underlying container. Used by `ChatHandler.UnitCommands/HandleMovegensCommand`, `Creature.MotionMaster/ClearType`, `Creature.MotionMaster/PropagateSpeedChange`, and `Creature.MotionMaster/ReInitializePatrolMovement` to inspect or modify the stack.

**erase**
Removes the element at the specified iterator from the underlying container. Used by `Creature.MotionMaster/ClearType` to remove specific generators.

**Clear**
Removes movement generators from the stack. Supports direct and delayed cleanup to ensure safety during update cycles. Called by AI, Unit core, Chat handlers, and various scripts to stop movement.

**MovementExpired**
Handles the expiration of the current movement generator. Pops it off the stack and triggers cleanup. Supports direct and delayed expiration. Called by Chat handlers, Creature MotionMaster, AI, and Unit core when a movement completes.

**NeedsAsyncUpdate**
Returns the `m_needsAsyncUpdate` flag. Used by `Unit.Main/Update` to determine if an asynchronous update is required.

**SetNeedAsyncUpdate**
Sets the `m_needsAsyncUpdate` flag to true. Called by `RandomMovementGenerator/Update` and `TargetedMovementGenerator/Initialize` when immediate processing is needed.

---

<!-- machine-true, projected from graph.json -->

## Map — MotionMaster

*Source:* MotionMaster.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| MotionMaster | ctor | — | Unit.Main/Unit | — |
| GetCurrent | method | — | AiBotAI.Combat/CheckForUnreachableTarget, BattleBotAI.Main/CheckForUnreachableTarget, BattleBotAI.Main/UpdateAI, boss_sapphiron/UpdateReachable, ChatHandler.CreatureCommands/HandleWpAddCommand, ChatHandler.CreatureCommands/HandleWpExportCommand, ChatHandler.CreatureCommands/HandleWpModifyCommand, ChatHandler.CreatureCommands/HandleWpShowCommand, Creature.Main/TryToCast, Creature.Main/Update, Creature.MotionMaster/PauseOutOfCombatMovement, Unit.Main/CantPathToVictim | — |
| begin#2 | method | — | Creature.MotionMaster/GetUsedMovementGeneratorsList | — |
| end#2 | method | — | Creature.MotionMaster/GetUsedMovementGeneratorsList | — |
| begin | method | — | ChatHandler.UnitCommands/HandleMovegensCommand, Creature.MotionMaster/ClearType, Creature.MotionMaster/ReInitializePatrolMovement | — |
| end | method | — | ChatHandler.UnitCommands/HandleMovegensCommand, Creature.MotionMaster/ClearType, Creature.MotionMaster/PropagateSpeedChange, Creature.MotionMaster/ReInitializePatrolMovement | — |
| erase | method | — | Creature.MotionMaster/ClearType | — |
| Clear | method | — | AiBotAI.Combat/UpdateInCombatAI_Hunter, AiBotAI.Movement/StopMoving, arathi_highlands/JustSummoned#2, BattleBotAI.Main/StopMoving, BattleBotAI.Main/UpdateInCombatAI_Hunter, blackrock_depths/GOHello_go_thunderbrew_laguer_keg, boss_gluth/ChaseGluth, boss_gothik/UpdateAI, boss_majordomo_executus/MovementInform, boss_mr_smite/PhaseEquipStart, boss_mr_smite/UpdateAI, boss_omen/MovementInform, boss_onyxia/PhaseTransition, boss_sapphiron/PickNewTarget, boss_sapphiron/SetRandomMove, boss_sapphiron/UpdateAI, boss_thaddius/DamageTaken, burning_steppes/BeginEvent, burning_steppes/JustDidDialogueStep, ChatHandler.DebugCommands/HandleDebugMoveCommand, ChatHandler.HardcodedEvents/SummonPallid, ChatHandler.PlayerBotMgr/StopPartyBotAttackHelper, Creature.Main/Update, Creature.MotionMaster/Initialize, Creature.MotionMaster/InitializeNewDefault, Creature.MotionMaster/MoveFollow, Creature.MotionMaster/MoveTargetedHome, Creature.MotionMaster/MoveWaypointAsDefault, CreatureAI/EnterEvadeMode, CreatureAI/operator(), dreadsteed_ritual/SummonGuard, dreadsteed_ritual/SummonImp, dreadsteed_ritual/WaveSpawn, eastern_plaguelands/JustReachedHome, eastern_plaguelands/SetAttackOnPeasantOrPlayer, elemental_invasions/DoSpawn, feralas/EnterEvadeMode, feralas/UpdateAI#4, instance_dire_maul/EnterEvadeMode, instance_dire_maul/MovementInform#2, instance_stratholme/SetData, Map.Main/CreatureRespawnRelocation, Map.ScriptCommands/ScriptCommand_SetMovementType, Map.ScriptCommands/ScriptCommand_StartWaypoints, molten_core/FeignDeath, npcs_special/npc_target_dummyAI, npcs_special/SpellHit#3, OutdoorPvPEP/SummonSpiritOfVictory, PartyBotAI/DrinkAndEat, PartyBotAI/UpdateAI, PartyBotAI/UpdateInCombatAI_Hunter, PartyBotAI/UpdateInCombatAI_Mage, PetAI/DoAttack, PetAI/HandleReturnMovement, PetAI/MovementInform, PetAI/_stopAttack, PlayerAI/PlayerControlledAI, PlayerAI/UpdateTarget, scholo_trash/DamageTaken, ScriptedFollowerAI/SetFollowComplete, ScriptedFollowerAI/SetFollowPaused, ScriptedFollowerAI/StartFollow, ScriptedPetAI/ResetPetCombat, silithus/BeginEvent, silithus/DoTimeStopArmy, undercity/UpdateAI, Unit.Main/HandlePetCommand, Unit.Main/SetDeathState, Unit.SpellAuras/HandleModCharm, Unit.SpellAuras/ModPossessPet, winterspring/BeginEvent | — |
| MovementExpired | method | — | ChatHandler.CreatureCommands/HandleNpcUnFollowCommand, ChatHandler.TeleportCommands/HandleGoHelper, ChatHandler.TeleportCommands/HandleGonameCommand, ChatHandler.TeleportCommands/HandleGroupgoCommand, ChatHandler.TeleportCommands/HandleNamegoCommand, ChatHandler.TeleportCommands/HandleTeleGroupCommand, Creature.MotionMaster/Mutate, Creature.MotionMaster/UpdateMotion, CreatureAI/SetCombatMovement, Player.Main/SummonIfPossible, PlayerBotAI/UpdateAI, ScriptedEscortAI/Start, Unit.SpellAuras/HandlePreventFleeing, WorldSession.BattleGroundHandler/HandleBattleFieldPortOpcode, WorldSession.MovementHandler/HandleMoveWorldportAck, WorldSession.TaxiHandler/SendDoFlight | — |
| NeedsAsyncUpdate | method | — | Unit.Main/Update | — |
| SetNeedAsyncUpdate | method | — | RandomMovementGenerator/Update, TargetedMovementGenerator/Initialize, TargetedMovementGenerator/Initialize#2 | — |
