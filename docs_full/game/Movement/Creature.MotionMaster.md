<!-- provenance: boundary-bleed -->
# Creature.MotionMaster

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# MotionMaster

## Purpose & Responsibilities

`MotionMaster` is the central controller for movement behavior of `Unit` objects (primarily `Creature`s, but also `Player`s for specific mechanics like taxi flights) within the WoWVMaNGOS server. It implements a **stack-based movement generator system**, allowing multiple layers of movement behaviors to coexist and interact. For example, a creature might be following a waypoint path (bottom of the stack) while simultaneously chasing a target (top of the stack). When the chase ends, the creature automatically resumes its waypoint patrol.

Key responsibilities include:
1.  **Managing the Movement Stack:** Maintaining a `std::stack` of `MovementGenerator` objects. The top of the stack represents the currently active movement behavior.
2.  **Initializing Default Behaviors:** Setting up idle, random, or waypoint movements based on creature configuration upon spawn or respawn via `Initialize` or `InitializeNewDefault`.
3.  **Processing Movement Updates:** Iterating through the stack during game ticks (`UpdateMotion`, `UpdateMotionAsync`) to advance positions, handle splines, and manage expiration of temporary movements.
4.  **Handling State Transitions:** Providing methods to switch between movement types (e.g., `MoveChase`, `MoveFleeing`, `MoveHome`) by pushing new generators onto the stack or popping expired ones.
5.  **Resource Management:** Ensuring dynamic `MovementGenerator` instances are properly finalized and deleted to prevent memory leaks, distinguishing between static singleton generators (like `si_idleMovement`) and heap-allocated ones.

The class operates primarily on `Creature` objects but also supports `Player` movement for specific mechanics like taxi flights, fear effects, and confusion. It does not directly manipulate database tables; all movement logic is runtime-driven, though initial movement types may be configured via creature data loaded from the database elsewhere in the system.

## Member-by-Member Behavior

### Initialization and Lifecycle

**`Initialize`**
Resets the movement stack completely. It stops any current movement, clears all existing generators, and pushes a new default generator based on the creature's configuration. If the owner is a living, unpossessed creature, it uses `CreatureAISelector::selectMovementGenerator` to determine the appropriate default (e.g., Idle, Random, Waypoint). For waypoints and cyclic paths, it explicitly initializes the path data. If the owner is a pet or possessed, it resets charm-related movement flags. If no suitable generator is found or the owner is invalid, it defaults to `si_idleMovement`.

**`InitializeNewDefault`**
Changes the creature's default movement type without interrupting the currently active movement generator if possible. It checks if the stack is empty (calling `Initialize` if so) or if the current default matches the new desired type. If a change is needed, it pops the current top generator, clears the rest of the stack, pushes the new default generator, and then pushes the previously popped generator back on top if it differs from the new default. This allows a creature to change its underlying patrol pattern while continuing a temporary action like chasing.

**`~MotionMaster`**
Destructor that cleans up all remaining movement generators. It iterates through the stack and the expiration list (`m_expList`), deleting any non-static generators. It intentionally skips calling `Finalize` on these generators to avoid accessing potentially deallocated owner memory during destruction.

### Movement Updates

**`UpdateMotion`**
Called periodically by the main game loop. It first checks if the owner is unable to move (`UNIT_STATE_CAN_NOT_MOVE`). If not, it asserts the stack is not empty and updates the top generator. If the top generator returns `false` (indicating completion or expiration), it triggers `MovementExpired`. It also processes the `m_expList`, deleting expired generators and re-initializing the stack if it becomes empty. Finally, it handles reset flags if necessary.

**`UpdateMotionAsync`**
Similar to `UpdateMotion` but designed for asynchronous execution contexts. It updates the top generator using `UpdateAsync` and manages the `m_needsAsyncUpdate` flag. It also processes the expiration list similarly to `UpdateMotion`.

### Stack Management and Cleanup

**`DirectClean`**
Immediately removes movement generators from the stack. If `all` is true, it clears the entire stack; otherwise, it keeps the bottom generator. It collects the removed generators, calls `Finalize` on them, and deletes non-static instances. If `reset` is true and the stack is not empty, it resets the new top generator.

**`DelayedClean`**
Marks generators for delayed deletion. Instead of immediately deleting them, it adds non-static generators to `m_expList`. This prevents issues where `Finalize` might trigger further movement commands that rely on the generator still being logically present. It sets the `MMCF_RESET` flag if required.

**`DirectExpire`**
Removes the top generator and any subsequent "targeted" generators (Chase/Follow) if the expired generator was not a distancing type. It finalizes and deletes these generators immediately. If the stack becomes empty, it calls `Initialize`. It ensures the new top generator is reset if requested.

**`DelayedExpire`**
Similar to `DirectExpire` but defers deletion by adding generators to `m_expList`. It sets the `MMCF_RESET` flag if needed.

**`ClearType`**
Iterates through the stack and removes all generators of a specified `MovementGeneratorType`. It finalizes and erases them from the stack.

### Movement Actions

**`MoveIdle`**
Pushes the static `si_idleMovement` generator onto the stack if the stack is empty or the top is not already idle. This effectively stops the unit.

**`MoveRandom`**
Creates a new `RandomMovementGenerator` and pushes it via `Mutate`. It logs an error if the owner is not a creature. Parameters allow specifying whether to use the current position, wander distance, and expiration time.

**`MoveTargetedHome`**
Attempts to move the unit to its home position. It first clears the stack. If the owner is a creature without a charmer/owner, it checks for linking events (linked mobs) and follows the master if applicable. Otherwise, it creates a `HomeMovementGenerator`. If the creature has a charmer/owner, it checks if it should stay or follow the owner using `FollowMovementGenerator`.

**`MoveConfused`**
Creates a `ConfusedMovementGenerator` for either a Player or Creature and pushes it via `Mutate`.

**`MoveChase`**
Creates a `ChaseMovementGenerator` targeting a specific `Unit`. It interrupts the current movespline if the owner is a creature and not stopped. It distinguishes between Player and Creature implementations.

**`MoveFollow`**
Creates a `FollowMovementGenerator` to follow a target `Unit`. It clears the stack before mutating. It checks for lost control states and ignores requests if the target is null.

**`MovePoint`**
Creates a `PointMovementGenerator` to move to specific coordinates (x, y, z). It supports options like pathfinding, walk/run/fly modes, and final orientation. It distinguishes between Player and Creature implementations.

**`MoveSeekAssistance`**
Creates an `AssistanceMovementGenerator` to move towards specific coordinates to seek help. Logs an error if the owner is a Player.

**`MoveSeekAssistanceDistract`**
Creates an `AssistanceDistractMovementGenerator` for a specified duration. Logs an error if the owner is a Player.

**`MoveFleeing`**
Creates a `FleeingMovementGenerator` or `TimedFleeingMovementGenerator` to run away from an enemy `Unit`. It distinguishes between Player and Creature implementations.

**`MoveFeared`**
Creates a `FearMovementGenerator` or `TimedFearMovementGenerator` to run away from an enemy `Unit`. It distinguishes between Player and Creature implementations.

**`MoveWaypointAsDefault`**
Sets up a `WaypointMovementGenerator` as the base/default layer of the stack. If there are existing generators, it pops the top, clears the rest, pushes the waypoint generator, and then pushes the popped generator back on top. This allows a creature to resume its waypoint path after a temporary movement ends.

**`MoveWaypoint`**
Creates a `WaypointMovementGenerator` and pushes it via `Mutate`. This is typically used for temporary waypoint sequences rather than setting the default behavior.

**`MoveCyclicWaypoint`**
Creates a `CyclicMovementGenerator` and pushes it via `Mutate`. Similar to `MoveWaypoint` but for cyclic paths.

**`MoveTaxiFlight` (overloads)**
Handles taxi flight movement for Players. One overload (`MoveTaxiFlight#2`) takes a path ID and node index, looking up the path in `sTaxiPathNodesByPath`. The other overload (`MoveTaxiFlight`) retrieves the current taxi path from the Player's taxi data. Both create a `FlightPathMovementGenerator` and push it via `Mutate`. Logs errors for non-players or invalid paths.

**`MoveDistract`**
Creates a `DistractMovementGenerator` for a specified timer and pushes it via `Mutate`.

**`MoveJump`**
Currently a stub function with no implementation, containing commented-out code for parabolic movement. It logs nothing and performs no action.

**`MoveCharge`**
Creates a `ChargeMovementGenerator` to charge at a target `Unit`. It calculates melee reach if requested and distinguishes between Player and Creature implementations.

**`MoveDistance`**
Calculates a point at a specific distance from a target and creates a `DistancingMovementGenerator` to move there. It performs line-of-sight and height checks. Returns `false` if conditions are not met (e.g., no LOS, too high, or already at the point).

### Utility and Query Methods

**`Mutate`**
Internal method to push a new `MovementGenerator` onto the stack. It handles interruption logic: if the top generator is Chase, Home, Distract, or Distancing, and the new generator is not Distancing, it expires the current top. It then calls `Initialize` on the new generator and pushes it.

**`PropagateSpeedChange`**
Iterates through all generators in the stack and calls `UnitSpeedChanged` on each, ensuring they update their internal calculations based on the unit's new speed.

**`SetNextWaypoint`**
Searches the stack in reverse for a `WaypointMovementGenerator` and sets its next waypoint ID. Returns `false` if no such generator is found.

**`getLastReachedWaypoint`**
Searches the stack in reverse for a `WaypointMovementGenerator` and returns the last reached waypoint ID. Returns 0 if not found.

**`GetMovementGeneratorTypeName`**
Static helper that converts a `MovementGeneratorType` enum value to a human-readable string constant.

**`GetCurrentMovementGeneratorType`**
Returns the type of the top generator on the stack. Returns `IDLE_MOTION_TYPE` if the stack is empty.

**`GetUsedMovementGeneratorsList`**
Populates a vector with the types of all generators currently on the stack.

**`IsUsingIdleOrDefaultMovement`**
Checks if the current movement is considered "idle" or a default passive movement (Idle, Random, Waypoint, Cyclic, Patrol) and if the stack size is small (<=1). This helps determine if a creature is actively engaged in combat-like movement.

**`GetWaypointPathInformation`**
Searches for a `WaypointMovementGenerator` in the stack and appends its path information to an output stream.

**`GetDestination`**
Retrieves the final destination coordinates from the owner's `movespline`. It uses a try-lock to avoid deadlocks with async updates. Returns `false` if the lock cannot be acquired or the spline is finalized.

**`UpdateFinalDistanceToTarget`**
Passes a distance value to the top generator's `UpdateFinalDistance` method, used for adjusting movement precision near targets.

**`ReInitializePatrolMovement`**
Searches the stack for a `PatrolMovementGenerator` and re-initializes its patrol path.

**`PauseOutOfCombatMovement`**
Defined in `Creature.h` but implemented in `MotionMaster.cpp`. It pauses random or waypoint movement for a specified duration if the creature is not in combat. It stops the current movement and adds pause time to the respective generator.

## Cross-Unit Boundaries

`MotionMaster` interacts extensively with other units to coordinate movement behavior:

*   **`CreatureAISelector`**: Called by `Initialize` and `InitializeNewDefault` to determine the appropriate default `MovementGenerator` for a creature based on its entry and configuration.
*   **`MovementGenerator` (and subclasses)**: `MotionMaster` creates, initializes, updates, finalizes, and deletes instances of various `MovementGenerator` subclasses (e.g., `WaypointMovementGenerator`, `ChaseMovementGenerator`). It relies on their `Update`, `Finalize`, `Reset`, and `Interrupt` methods.
*   **`Unit.Main`**: `MotionMaster` frequently checks unit states (`HasUnitState`, `IsStopped`, `IsAlive`, `IsCreature`, `IsPlayer`) and controls movement status (`StopMoving`, `SetIsAtStay`, etc.). It also accesses charm info and transport data.
*   **`Creature.Main`**: Accessed for creature-specific properties like `GetDefaultMovementType`, `GetCharmerOrOwner`, `HasStaticFlag`, and `GetCreatureLinkingHolder`. Note: Methods like `Create`, `Respawn`, and `Execute` belong to other units (e.g., `Creature`, `BasicEvent`) and are not part of `MotionMaster`'s behavior.
*   **`Log.Main`**: Used for debugging and error logging throughout movement operations.
*   **`Errors`**: `PrintStacktraceAndThrow` is called in update methods if assertions fail (e.g., empty stack).
*   **`ChatHandler`**: Various chat commands (e.g., `HandleNpcSetMoveTypeCommand`, `HandleDebugMoveCommand`) call `MotionMaster` methods to manually control creature movement for testing or admin purposes.
*   **`ScriptedAI` and Boss Scripts**: Numerous boss and script AI classes (e.g., `boss_nefarian`, `ScriptedEscortAI`) call `MotionMaster` methods like `MoveChase`, `MovePoint`, `MoveWaypoint` to execute complex encounter mechanics.
*   **`Map.Main`**: `UpdateCells` calls `UpdateMotionAsync` to process movement asynchronously. `GetCreatureLinkingHolder` is used in `MoveTargetedHome` for linked mobs.
*   **`Player.Main`**: Accessed in `MoveTaxiFlight` to retrieve taxi path data.
*   **`AiBotAI` / `BattleBotAI`**: Bot AI systems interact with `MotionMaster` to control bot movement, often calling `MoveChase`, `MovePoint`, and querying current movement types.

## Data Model

`MotionMaster` does not directly interact with any database tables. Movement configurations (such as default movement types and waypoint paths) are loaded into memory by other components (e.g., `Creature` loading routines) from tables like `creature`, `creature_addon`, and `waypoint_data`. `MotionMaster` operates solely on the in-memory representations of these configurations.

## Notable Implementation Details

1.  **Stack-Based Architecture**: The use of a `std::stack` allows for layered movement behaviors. Temporary actions (chase, fear) sit on top of persistent behaviors (patrol, idle). When the top action completes, it is popped, revealing the underlying behavior.
2.  **Static vs. Dynamic Generators**: The `isStatic` inline function checks if a generator is the singleton `si_idleMovement`. Static generators are never deleted, while dynamic ones are managed via `new`/`delete` and tracked in `m_expList` for delayed cleanup.
3.  **Delayed Cleanup**: `DelayedClean` and `DelayedExpire` defer deletion of generators to prevent use-after-free bugs. This is crucial because `Finalize` can trigger callbacks (like `MovementInform`) that might initiate new movements, which would be unsafe if the generator were immediately destroyed.
4.  **Thread Safety in `GetDestination`**: The method uses `std::try_to_lock` on `m_owner->asyncMovesplineLock` to avoid deadlocks when accessing the movespline's final destination during async updates. If the lock fails, it returns `false` rather than blocking.
5.  **Interrupt Logic in `Mutate`**: When pushing a new generator, `Mutate` checks if the current top generator should be interrupted. Chase, Home, Distract, and Distancing movements are generally interrupted by new movements, except when the new movement is Distancing (which might complement a chase).
6.  **Player vs. Creature Handling**: Many movement methods (e.g., `MoveChase`, `MovePoint`) have distinct branches for `Player` and `Creature` owners, creating different generator subclasses tailored to each entity type.
7.  **Stubbed `MoveJump`**: The `MoveJump` method is currently empty, indicating that jump mechanics are either handled elsewhere or not yet implemented in this version of the codebase.
8.  **Waypoint Initialization**: `Initialize` and `InitializeNewDefault` contain specific switch cases for `WAYPOINT_MOTION_TYPE` and `CYCLIC_MOTION_TYPE` to call `InitializeWaypointPath` on the respective generators, ensuring path data is loaded correctly.

## Member Reference

**isStatic**: Inline helper function that returns true if the given `MovementGenerator` pointer is the static singleton `si_idleMovement`.

**Initialize**: Resets the movement stack, stops current movement, clears all generators, and pushes a new default generator based on the owner's type and configuration. Initializes waypoint/cyclic paths if applicable.

**InitializeNewDefault**: Changes the default movement generator without interrupting the current top generator if possible. Pops the current top, clears the rest, pushes the new default, and restores the popped generator if it differs from the new default.

**~MotionMaster**: Destructor that deletes all non-static movement generators in the stack and expiration list without calling `Finalize` to avoid accessing deallocated memory.

**UpdateMotion**: Updates the top movement generator. If it returns false, triggers expiration. Processes the expiration list, deleting old generators and re-initializing if the stack is empty. Handles reset flags.

**UpdateMotionAsync**: Asynchronous version of `UpdateMotion`. Updates the top generator via `UpdateAsync` and processes the expiration list.

**DirectClean**: Immediately removes generators from the stack (all or except the bottom one), finalizes them, and deletes non-static instances. Resets the new top if requested.

**DelayedClean**: Marks generators for delayed deletion by adding them to `m_expList`. Sets reset flags if needed. Prevents immediate deletion to allow `Finalize` callbacks to complete safely.

**DirectExpire**: Removes the top generator and any subsequent targeted generators (Chase/Follow) if not distancing. Finalizes and deletes them immediately. Re-initializes if stack is empty.

**DelayedExpire**: Similar to `DirectExpire` but defers deletion by adding generators to `m_expList`. Sets reset flags if needed.

**MoveIdle**: Pushes the static `si_idleMovement` generator onto the stack if not already present at the top or if the stack is empty.

**MoveRandom**: Creates and pushes a `RandomMovementGenerator` via `Mutate`. Logs an error if the owner is not a creature.

**MoveTargetedHome**: Clears the stack and moves the unit to its home position or follows its owner/charmer. Handles linked mobs and transport passengers.

**MoveConfused**: Creates and pushes a `ConfusedMovementGenerator` for the owner (Player or Creature).

**MoveChase**: Creates and pushes a `ChaseMovementGenerator` targeting a specific unit. Interrupts current movespline for creatures.

**MoveFollow**: Clears the stack and creates a `FollowMovementGenerator` to follow a target unit. Ignores requests if the owner has lost control.

**MovePoint**: Creates and pushes a `PointMovementGenerator` to move to specific coordinates with optional pathfinding and mode settings.

**MoveSeekAssistance**: Creates and pushes an `AssistanceMovementGenerator` to move towards specific coordinates. Logs an error if the owner is a Player.

**MoveSeekAssistanceDistract**: Creates and pushes an `AssistanceDistractMovementGenerator` for a specified duration. Logs an error if the owner is a Player.

**MoveFleeing**: Creates and pushes a `FleeingMovementGenerator` or `TimedFleeingMovementGenerator` to flee from an enemy unit.

**MoveFeared**: Creates and pushes a `FearMovementGenerator` or `TimedFearMovementGenerator` to flee from an enemy unit.

**MoveWaypointAsDefault**: Sets up a `WaypointMovementGenerator` as the base layer of the stack, preserving the current top generator if present.

**MoveWaypoint**: Creates and pushes a `WaypointMovementGenerator` via `Mutate` for temporary waypoint sequences.

**MoveCyclicWaypoint**: Creates and pushes a `CyclicMovementGenerator` via `Mutate` for cyclic path sequences.

**MoveTaxiFlight#2**: Overload that takes a path ID and node index, looks up the path, and creates a `FlightPathMovementGenerator` for Players.

**MoveTaxiFlight**: Overload that retrieves the current taxi path from the Player's taxi data and creates a `FlightPathMovementGenerator`.

**MoveDistract**: Creates and pushes a `DistractMovementGenerator` for a specified timer.

**Mutate**: Internal method to push a new generator. Handles interruption of specific top generators (Chase, Home, etc.) and initializes the new generator.

**PropagateSpeedChange**: Iterates through all generators and calls `UnitSpeedChanged` to update their internal calculations.

**SetNextWaypoint**: Searches the stack for a `WaypointMovementGenerator` and sets its next waypoint ID.

**getLastReachedWaypoint**: Searches the stack for a `WaypointMovementGenerator` and returns its last reached waypoint ID.

**GetMovementGeneratorTypeName**: Static helper converting `MovementGeneratorType` enums to string constants.

**GetCurrentMovementGeneratorType**: Returns the type of the top generator, or `IDLE_MOTION_TYPE` if the stack is empty.

**GetUsedMovementGeneratorsList**: Populates a vector with the types of all generators on the stack.

**IsUsingIdleOrDefaultMovement**: Checks if the current movement is a passive default type and the stack is small.

**GetWaypointPathInformation**: Appends path information from a `WaypointMovementGenerator` in the stack to an output stream.

**GetDestination**: Retrieves the final destination from the owner's movespline using a try-lock to avoid deadlocks.

**UpdateFinalDistanceToTarget**: Passes a distance value to the top generator's `UpdateFinalDistance` method.

**MoveJump**: Stub function with no implementation.

**MoveCharge**: Creates and pushes a `ChargeMovementGenerator` to charge at a target unit.

**MoveDistance**: Calculates a point at a distance from a target and creates a `DistancingMovementGenerator` if LOS and height checks pass.

**ClearType**: Removes all generators of a specified type from the stack, finalizing and erasing them.

**ReInitializePatrolMovement**: Finds a `PatrolMovementGenerator` in the stack and re-initializes its patrol path.

**PauseOutOfCombatMovement**: Pauses random or waypoint movement for a specified duration if the creature is not in combat. Implemented in `MotionMaster.cpp` but declared in `Creature.h`.

---

<!-- machine-true, projected from graph.json -->

## Map — Creature.MotionMaster

*Source:* MotionMaster.cpp, MotionMaster.h, Creature.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| isStatic | function | — | — | — |
| Initialize | method | CreatureAISelector/selectMovementGenerator, CyclicMovementGenerator/InitializeWaypointPath, MotionMaster/Clear, MovementGenerator/GetMovementGeneratorType, MovementGenerator/Initialize#2, Object/IsCreature, Unit.Main/GetCharmInfo, Unit.Main/HasUnitState, Unit.Main/IsAlive, Unit.Main/IsStopped, Unit.Main/SetIsAtStay, Unit.Main/SetIsFollowing, Unit.Main/SetIsReturning, Unit.Main/StopMoving, WaypointMovementGenerator/InitializeWaypointPath | boss_gothik/Reset#2, boss_omen/MovementInform, boss_razorgore/UpdateAI#2, boss_thaddius/Reset#3, boss_vectus/MoveInLineOfSight, burning_steppes/Reset#2, burning_steppes/Transform, ChatHandler.CreatureCommands/HandleNpcGroupAddCommand, ChatHandler.CreatureCommands/HandleNpcGroupAddRelCommand, ChatHandler.CreatureCommands/HandleNpcGroupDelCommand, ChatHandler.CreatureCommands/HandleNpcSetMoveTypeCommand, ChatHandler.CreatureCommands/HandleNpcSetWanderDistCommand, ChatHandler.CreatureCommands/HandleNpcSpawnSetMoveTypeCommand, ChatHandler.CreatureCommands/HandleNpcSpawnWanderDistCommand, ChatHandler.CreatureCommands/HandleWpModifyCommand, Creature.Main/AIM_Initialize, Creature.Main/JoinCreatureGroup, Creature.Main/SetDeathState, CreatureGroups/DisbandGroup, CreatureGroups/OnRespawn, dreadsteed_ritual/SummonGuard, dreadsteed_ritual/SummonImp, dreadsteed_ritual/WaveSpawn, instance_blackrock_depths/HandleBarPatrons, instance_dire_maul/MovementInform, Map.Main/CreatureRespawnRelocation, Map.ScriptCommands/ScriptCommand_SummonCreature, PlayerBotAI/SpawnNewPlayer, ruins_of_ahnqiraj/OssirianTornadoAI, ruins_of_ahnqiraj/UpdateAI#12, silithus/Reset#10, silithus/Transform, Unit.Main/RestoreMovement, WaypointMovementGenerator/StartMove#2, winterspring/Reset, winterspring/Transform, WorldSession.CharacterHandler/HandlePlayerLogin | — |
| InitializeNewDefault | method | Creature.Main/GetDefaultMovementType, CreatureAISelector/selectMovementGenerator, CyclicMovementGenerator/InitializeWaypointPath, MotionMaster/Clear, MovementGenerator/Finalize#2, MovementGenerator/GetMovementGeneratorType, MovementGenerator/Initialize#2, Object/ToCreature, Unit.Main/HasUnitState, WaypointMovementGenerator/InitializeWaypointPath | boss_omen/OnFireworkLaunch, Map.ScriptCommands/ScriptCommand_SetDefaultMovement | — |
| ~MotionMaster | dtor | — | — | — |
| UpdateMotion | method | Errors/PrintStacktraceAndThrow, MotionMaster/MovementExpired, MovementGenerator/Reset#2, MovementGenerator/Update#2, Unit.Main/HasUnitState | Unit.Main/Update | — |
| UpdateMotionAsync | method | Errors/PrintStacktraceAndThrow, MovementGenerator/UpdateAsync, Unit.Main/HasUnitState | Map.Main/UpdateCells, Unit.Main/Update | — |
| DirectClean | method | Errors/PrintStacktraceAndThrow, MovementGenerator/Finalize#2, MovementGenerator/Reset#2 | — | — |
| DelayedClean | method | MovementGenerator/Finalize#2 | — | — |
| DirectExpire | method | MovementGenerator/Finalize#2, MovementGenerator/GetMovementGeneratorType, MovementGenerator/Reset#2 | — | — |
| DelayedExpire | method | MovementGenerator/Finalize#2, MovementGenerator/GetMovementGeneratorType | — | — |
| MoveIdle | method | — | AiBotAI.Combat/UpdateInCombatAI_Mage, AiBotAI.Main/MovementInform, AiBotAI.Movement/StopMoving, BattleBotAI.Main/StopMoving, BattleBotAI.Main/UpdateInCombatAI_Mage, blackrock_depths/DoPotionOfLoveIfCan, blackrock_depths/UpdateAI#5, blackrock_depths/WaypointReached#5, boss_bug_trio/MovementInform, boss_gothik/Aggro, boss_heigan/EventStartDance, boss_majordomo_executus/MovementInform, boss_maleki_the_pallid/UpdateAI, boss_nefarian/MovementInform, boss_noth/TeleportToBalc, boss_onyxia/PhaseTransition, boss_sapphiron/UpdateAI, boss_thaddius/DamageTaken, boss_vaelastrasz/UpdateAI, burning_steppes/BeginEvent, burning_steppes/JustDidDialogueStep, ChatHandler.DebugCommands/HandleDebugMoveCommand, ChatHandler.PlayerBotMgr/HandlePartyBotPauseApplyHelper, CreatureAI/SetCombatMovement, eastern_plaguelands/JustReachedHome, instance_blackrock_depths/HandleBarPatrol, instance_blackrock_spire/DoSendNextStadiumWave, instance_dire_maul/MovementInform, instance_sunken_temple/OnCreatureCreate, instance_zulgurub/SpawnRandomBoss, Map.ScriptCommands/ScriptCommand_SetMovementType, molten_core/FeignDeath, npcs_special/npc_target_dummyAI, PartyBotAI/DrinkAndEat, PartyBotAI/UpdateAI, PetAI/DoAttack, PetAI/MovementInform, PetAI/_stopAttack, scholo_trash/DamageTaken, ScriptedAI/DoStartNoMovement, ScriptedEscortAI/Start, ScriptedFollowerAI/SetFollowComplete, ScriptedFollowerAI/SetFollowPaused, ScriptedFollowerAI/StartFollow, ScriptedPetAI/ResetPetCombat, silithus/BeginEvent, silithus/EnterEvadeMode, Totem/Update, ungoro_crater/BeginEvent, Unit.Main/HandlePetCommand, Unit.Main/SetDeathState, Unit.SpellAuras/HandleModCharm, Unit.SpellAuras/ModPossessPet, wailing_caverns/JustSummoned, winterspring/BeginEvent, winterspring/SpellHit#2 | — |
| MoveRandom | method | Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out, Object/GetGuidStr, Object/IsCreature, RandomMovementGenerator/RandomMovementGenerator | boss_four_horsemen/UpdateAI#3, boss_ouro/UpdateAI#2, boss_sapphiron/SetRandomMove, ChatHandler.DebugCommands/HandleDebugMoveCommand, duskwood/WaypointReached, eastern_plaguelands/JustSummoned, eastern_plaguelands/SummonedMovementInform, elemental_invasions/DoSpawn, instance_stratholme/SetData, Map.ScriptCommands/ScriptCommand_SetMovementType, ThreatListCopier.battleground_alterac/JustReachedHome, WaypointMovementGenerator/OnArrived | — |
| MoveTargetedHome | method | Creature.Main/HasStaticFlag, CreatureLinkingMgr/TryFollowMaster, HomeMovementGenerator/HomeMovementGenerator, Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out, Map.Main/GetCreatureLinkingHolder, MotionMaster/Clear, Object/GetGuidStr, Object/IsCreature, ObjectGuid/operator!, Transport/RemovePassenger, Unit.Main/GetCharmerOrOwner, Unit.Main/GetCharmerOrOwnerGuid, Unit.Main/GetCharmInfo, Unit.Main/HasUnitState, Unit.Main/IsAtStay, Unit.Main/IsLinkingEventTrigger, WorldObject.Object/GetMap, WorldObject.Object/GetTransport | boss_chromaggus/MovementInform, boss_mandokir/CheckVilebranchState, boss_vectus/EnterEvadeMode, CreatureAI/EnterEvadeMode, CreatureAI/operator(), duskwood/JustDied#2, instance_dire_maul/npc_mizzle_the_craftyAI, Map.ScriptCommands/ScriptCommand_SetMovementType, ScriptedAI/DoGoHome, ScriptedAI/EnterEvadeMode, ScriptedEscortAI/ReturnToCombatStartPosition, ScriptedFollowerAI/EnterEvadeMode, stormwind_city/DamageTaken#2, world_event_wareffort/EnterEvadeMode#2, world_event_wareffort/MoveToWaveBattlePosition, world_event_wareffort/MoveToWaveBattlePosition#2, world_event_wareffort/MoveToWaveBattlePosition#3 | — |
| MoveConfused | method | Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out, Object/GetGuidStr, Object/IsPlayer | ChatHandler.DebugCommands/HandleDebugMoveCommand, Map.ScriptCommands/ScriptCommand_SetMovementType, PlayerBotAI/OnPlayerLogin#3, Unit.Main/ModConfuseSpell | — |
| MoveChase | method | Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out, Object/GetGuidStr, Object/IsPlayer, Unit.Main/IsStopped, Unit.Main/StopMoving | AiBotAI.Combat/AttackStart, AiBotAI.Combat/UpdateInCombatAI_Druid, AiBotAI.Combat/UpdateInCombatAI_Hunter, AiBotAI.Combat/UpdateInCombatAI_Mage, AiBotAI.Combat/UpdateInCombatAI_Paladin, AiBotAI.Combat/UpdateInCombatAI_Priest, AiBotAI.Combat/UpdateInCombatAI_Warlock, AiBotAI.Combat/UpdateInCombatAI_Warrior, arena_challenge_ai/UpdateAI#3, azshara/UpdateAI#2, BattleBotAI.Main/AttackStart, BattleBotAI.Main/UpdateInCombatAI_Druid, BattleBotAI.Main/UpdateInCombatAI_Hunter, BattleBotAI.Main/UpdateInCombatAI_Mage, BattleBotAI.Main/UpdateInCombatAI_Paladin, BattleBotAI.Main/UpdateInCombatAI_Priest, BattleBotAI.Main/UpdateInCombatAI_Warlock, BattleBotAI.Main/UpdateInCombatAI_Warrior, boss_ayamiss/UpdateAI, boss_heigan/EventDanceEnd, boss_maleki_the_pallid/UpdateAI, boss_mandokir/UpdateAI, boss_mr_smite/AttackStart, boss_mr_smite/MovementInform, boss_nefarian/UpdateAI, boss_onyxia/PhaseTransition, boss_razorgore/PhaseSwitch, boss_razorgore/UpdateAI#2, CreatureAI/AttackStart, CreatureAI/SetCombatMovement, eastern_plaguelands/MoveInLineOfSight, eastern_plaguelands/SetAttackOnPeasantOrPlayer, Map.ScriptCommands/ScriptCommand_SetMovementType, PartyBotAI/AttackStart, PartyBotAI/UpdateAI, PartyBotAI/UpdateInCombatAI_Druid, PartyBotAI/UpdateInCombatAI_Hunter, PartyBotAI/UpdateInCombatAI_Mage, PartyBotAI/UpdateInCombatAI_Paladin, PartyBotAI/UpdateInCombatAI_Priest, PartyBotAI/UpdateInCombatAI_Warlock, PartyBotAI/UpdateInCombatAI_Warrior, PetAI/DoAttack, PetEventAI/AttackStart, PlayerAI/UpdateTarget, PlayerBotAI/UpdateAI, ruins_of_ahnqiraj/UpdateAI#12, ruins_of_ahnqiraj/UpdateAI#2, ScriptedAI/DoStartMovement, ScriptedPetAI/AttackStart, silithus/JustSummoned, ThreatListCopier.boss_ragnaros/SummonSonsOfFlame, Unit.Main/HandlePetCommand, Unit.Main/RestoreMovement, westfall/AttackStart, wetlands/AttackStart | — |
| MoveFollow | method | Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out, MotionMaster/Clear, Object/GetGuidStr, Object/IsPlayer, Unit.Main/HasUnitState | BattleBotAI.Main/UpdateAI, blackrock_depths/AreaTrigger_at_shadowforge_bridge, blackrock_depths/DoPotionOfLoveIfCan, blackrock_depths/UpdateAI#5, blackrock_depths/WaypointReached#5, boss_general_angerforge/JustSummoned, boss_gluth/ChaseGluth, boss_lethon/UpdateAI, boss_ouro/JustSummoned, boss_ouro/UpdateAI#2, boss_thermaplugg/UpdateAI, ChatHandler.CreatureCommands/HandleNpcFollowCommand, CreatureLinkingMgr/SetFollowing, gnomeregan/StartQuest, instance_blackrock_depths/Update, instance_dire_maul/Reset#8, Map.ScriptCommands/ScriptCommand_SetMovementType, moonglade/EnterEvadeMode#2, PartyBotAI/UpdateAI, PetAI/HandleReturnMovement, PetEventAI/UpdateAI, ScriptedFollowerAI/SetFollowPaused, ScriptedFollowerAI/StartFollow, ScriptedFollowerAI/UpdateAI, ScriptedPetAI/ResetPetCombat, ScriptedPetAI/UpdateAI, spell_item/OnSummon#2, stratholme/JustSummoned, undercity/UpdateAI, ungoro_crater/Reset#6, ungoro_crater/UpdateAI, Unit.Main/HandlePetCommand, Unit.SpellAuras/HandleModCharm, Unit.SpellAuras/PeriodicDummyTick, wetlands/UpdateAI, world_event_wareffort/FollowSaurfang | — |
| MovePoint | method | Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out, Object/GetGuidStr, Object/IsPlayer | AiBotAI.Movement/MovePointRun, arathi_highlands/JustSummoned#2, ashenvale/EnragedFoulwealdJustDied, ashenvale/EventStart, ashenvale/UpdateAI, ashenvale/UpdateAI#2, azshara/MovementInform, azshara/Reset, BattleBotAI.BattleBotWaypoints/MoveToNextPoint, BattleBotAI.BattleBotWaypoints/MoveToNextPointSpecial, BattleBotAI.BattleBotWaypoints/WSG_AtAllianceFlag, BattleBotAI.BattleBotWaypoints/WSG_AtHordeFlag, BattleBotAI.Main/OnEnterBattleGround, blackrock_depths/Activate, blackrock_depths/Aggro#2, blackrock_depths/AreaTrigger_at_shadowforge_bridge, blackrock_depths/MovementInform, blackrock_depths/SummonRingBoss, blackrock_depths/SummonRingMob, blackrock_depths/WaypointReached#5, boss_ayamiss/UpdateAI, boss_bug_trio/TriggerDevour, boss_chromaggus/MovementInform, boss_chromaggus/UpdateAI, boss_gahzranka/CheckSpawnStatus, boss_gahzranka/MovementInform, boss_herod/JustSummoned, boss_herod/MovementInform, boss_herod/UpdateAI#2, boss_maexxna/SetVictim, boss_majordomo_executus/DomoEvent, boss_majordomo_executus/MovementInform, boss_mandokir/SpellHitTarget#2, boss_mr_smite/PhaseEquipStart, boss_mr_smite/UpdateAI, boss_nefarian/MovementInform, boss_nefarian/UpdateAI, boss_omen/MovementInform, boss_omen/OnFireworkLaunch, boss_onyxia/DoMovement, boss_onyxia/PhaseTransition, boss_onyxia/PhaseTwo, boss_sapphiron/PickNewTarget, boss_sapphiron/UpdateAI, boss_sartura/ImpaleAssist, boss_thermaplugg/JustSummoned, boss_urok/UpdateAI#2, boss_viscidus/JustSummoned, ChatHandler.CreatureCommands/HandleComeToMeCommand, ChatHandler.DebugCommands/HandleDebugMoveToCommand, darkshore/MovementInform, darkshore/UpdateAI#2, deadmines/GOHello_go_defias_gunpowder, deadmines/SummonedMovementInform, desolace/SetMagnetGuid, dreadsteed_ritual/SummonGuard, dreadsteed_ritual/SummonImp, dreadsteed_ritual/WaveSpawn, durotar/UpdateAI, duskwood/JustSummoned, duskwood/UpdateAI#4, duskwood/WaypointReached, eastern_plaguelands/JustSummoned, eastern_plaguelands/MovementInform#2, eastern_plaguelands/MovementInform#3, eastern_plaguelands/SummonedMovementInform, eastern_plaguelands/UpdateAI, eastern_plaguelands/UpdateAI#4, eastern_plaguelands/UpdateAI#5, feralas/MoveInLineOfSight, feralas/UpdateAI#4, feralas/UpdateFollowerAI, gnomeregan/JustSummoned, hillsbrad_foothills/UpdateAI, hinterlands/JustSummoned, instance_blackrock_depths/HandleBarPatrol, instance_blackrock_spire/DoSendNextStadiumWave, instance_blackrock_spire/JustDidDialogueStep, instance_deadmines/Update, instance_dire_maul/goToFengus, instance_dire_maul/MovementInform#2, instance_dire_maul/MovementInform#3, instance_dire_maul/QuestRewarded_npc_knot_thimblejack, instance_dire_maul/UpdateAI#2, instance_naxxramas.Main/Update, instance_razorfen_downs/SetData, instance_scarlet_monastery/SetData, instance_scarlet_monastery/Update, instance_stratholme/MoveAbomnationMob, instance_stratholme/SetData, instance_stratholme/SummonRamstein, instance_stratholme/Update, instance_zulfarrak/MoveNPCIfAlive, instance_zulfarrak/SendAddsUpStairs, Map.ScriptCommands/ScriptCommand_MoveTo, moonglade/JustSummoned, moonglade/SummonedMovementInform, moonglade/UpdateAI, moonglade/UpdateAI#2, moonglade/UpdateEscortAI, moonglade/WaypointReached, npcs_special/SpellHit, npc_j_eevee/UpdateAI, npc_j_eevee/UpdateAI#2, PetAI/HandleReturnMovement, PlayerBotAI/UpdateAI, quest_stormwind_rendezvous/AreaTrigger_at_stormwind_gates, quest_stormwind_rendezvous/EndScene, quest_stormwind_rendezvous/MovementInform, quest_stormwind_rendezvous/UpdateAI, quest_stormwind_rendezvous/UpdateAI#2, razorfen_kraul/DoFindNewTuber, ScriptedEscortAI/ReturnToCombatStartPosition, ScriptedEscortAI/UpdateAI, silithus/ResetOtherNPCsPosition, silithus/SummonedMovementInform, silithus/UpdateAI, silithus/UpdateAI#4, silithus/UpdateAI#7, Spell.Effects/EffectDummy, stonetalon_mountains/JustSummoned, stormwind_city/ResetThug, tanaris/UpdateFollowerAI, the_barrens/UpdateAI#2, ThreatListCopier.battleground_alterac/checkAerialStatus, ThreatListCopier.battleground_alterac/JustRespawned, ThreatListCopier.battleground_alterac/Reset#10, ThreatListCopier.battleground_alterac/Reset#6, ThreatListCopier.battleground_alterac/Reset#7, uldaman/UpdateAI, wailing_caverns/MovementInform, wailing_caverns/SendAttackerToMe, wailing_caverns/UpdateEscortAI, world_event_wareffort/UpdateAI#2, world_event_wareffort/UpdateAI#3, zulfarrak/DestroyDoor, zulfarrak/initBlyCrewMember, zulfarrak/MovementInform, zulfarrak/OnTrigger_at_antusul, zulfarrak/RunAfterExplosion1, zulfarrak/RunAfterExplosion2 | — |
| MoveSeekAssistance | method | AssistanceMovementGenerator/AssistanceMovementGenerator, Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out, Object/GetGuidStr, Object/IsPlayer | Creature.Main/DoFleeToGetAssistance, instance_naxxramas.Main/FleeToHorse | — |
| MoveSeekAssistanceDistract | method | AssistanceDistractMovementGenerator/AssistanceDistractMovementGenerator, Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out, Object/GetGuidStr, Object/IsPlayer | PointMovementGenerator/Finalize | — |
| MoveFleeing | method | Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out, Object/GetGuidStr, Object/GetObjectGuid, Object/IsPlayer, TimedFleeingMovementGenerator/TimedFleeingMovementGenerator | ChatHandler.DebugCommands/HandleDebugMoveCommand, CritterAI/DamageTaken, CritterAI/SpellHit, Map.ScriptCommands/ScriptCommand_SetMovementType, npcs_special/SpellHit#3, PlayerBotAI/OnPlayerLogin#2, Unit.Main/ModConfuseSpell | — |
| MoveFeared | method | FearMovementGenerator/TimedFearMovementGenerator, Object/GetObjectGuid, Object/IsPlayer | ChatHandler.DebugCommands/HandleDebugMoveCommand, Unit.Main/ModConfuseSpell | — |
| MoveWaypointAsDefault | method | Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out, MotionMaster/Clear, Object/GetEntry, Object/GetGuidStr, Object/IsCreature, WaypointMovementGenerator/InitializeWaypointPath, WaypointMovementGenerator/WaypointMovementGenerator | CreatureGroups/OnMemberDied | — |
| MoveWaypoint | method | Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out, Object/GetEntry, Object/GetGuidStr, Object/IsCreature, WaypointMovementGenerator/InitializeWaypointPath, WaypointMovementGenerator/WaypointMovementGenerator | blackrock_depths/AreaTrigger_at_shadowforge_bridge, blackrock_depths/GOHello_go_thunderbrew_laguer_keg, ChatHandler.HardcodedEvents/SummonPallid, instance_blackrock_spire/DoSendNextStadiumWave, instance_blackrock_spire/JustDidDialogueStep, instance_razorfen_kraul/SetData, instance_ruins_of_ahnqiraj/Update, instance_sunken_temple/SetData, Map.ScriptCommands/ScriptCommand_SetMovementType, Map.ScriptCommands/ScriptCommand_StartWaypoints, OutdoorPvPEP/SummonSpiritOfVictory, spell_item/OnSummon#2 | — |
| MoveCyclicWaypoint | method | CyclicMovementGenerator/CyclicMovementGenerator, CyclicMovementGenerator/InitializeWaypointPath, Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out, Object/GetEntry, Object/GetGuidStr, Object/IsCreature | — | — |
| MoveTaxiFlight#2 | method | FlightPathMovementGenerator/FlightPathMovementGenerator, Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out, Object/GetGuidStr, Object/IsPlayer | WorldSession.TaxiHandler/SendDoFlight | — |
| MoveTaxiFlight | method | FlightPathMovementGenerator/FlightPathMovementGenerator, Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out, Object/GetGuidStr, Object/IsPlayer, Object/ToPlayer, Player.Main/GetTaxi, PlayerTaxi/GetTaxiPath | WorldSession.TaxiHandler/SendDoFlight | — |
| MoveDistract | method | DistractMovementGenerator/DistractMovementGenerator, Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out, Object/GetGuidStr | CreatureAI/TriggerAlertDirect, Map.ScriptCommands/ScriptCommand_SetMovementType, Spell.Effects/EffectDistract | — |
| Mutate | method | MotionMaster/MovementExpired, MovementGenerator/GetMovementGeneratorType, MovementGenerator/Initialize#2, MovementGenerator/Interrupt#2 | AiBotMovementGenerators/IssueSmoothedPath | — |
| PropagateSpeedChange | method | MotionMaster/end, MovementGenerator/UnitSpeedChanged | — | — |
| SetNextWaypoint | method | MovementGenerator/GetMovementGeneratorType, WaypointMovementGenerator/SetNextWaypoint | — | — |
| getLastReachedWaypoint | method | MovementGenerator/GetMovementGeneratorType, WaypointMovementGenerator/getLastReachedWaypoint | Conditions/Evaluate | — |
| GetMovementGeneratorTypeName | method | — | ChatHandler.CharacterCommands/HandleCharacterAIInfoCommand, ChatHandler.CreatureCommands/HandleNpcAIInfoCommand, ChatHandler.UnitCommands/HandleListMoveGensCommand | — |
| GetCurrentMovementGeneratorType | method | MovementGenerator/GetMovementGeneratorType | AiBotAI.Combat/CheckForUnreachableTarget, AiBotAI.Combat/DrinkAndEat, AiBotAI.Combat/UpdateInCombatAI_Druid, AiBotAI.Combat/UpdateInCombatAI_Hunter, AiBotAI.Combat/UpdateInCombatAI_Mage, AiBotAI.Combat/UpdateInCombatAI_Paladin, AiBotAI.Combat/UpdateInCombatAI_Priest, AiBotAI.Combat/UpdateInCombatAI_Warlock, AiBotAI.Combat/UpdateInCombatAI_Warrior, AiBotAI.Grind/DoGrindPatrol, AiBotAI.Main/UpdateAI, AiBotAI.Movement/DoRandomWander, azshara/UpdateAI#2, BattleBotAI.Main/CheckForUnreachableTarget, BattleBotAI.Main/DrinkAndEat, BattleBotAI.Main/OnJustDied, BattleBotAI.Main/OnLeaveBattleGround, BattleBotAI.Main/UpdateAI, BattleBotAI.Main/UpdateInCombatAI_Druid, BattleBotAI.Main/UpdateInCombatAI_Hunter, BattleBotAI.Main/UpdateInCombatAI_Mage, BattleBotAI.Main/UpdateInCombatAI_Paladin, BattleBotAI.Main/UpdateInCombatAI_Priest, BattleBotAI.Main/UpdateInCombatAI_Warlock, BattleBotAI.Main/UpdateInCombatAI_Warrior, BattleBotAI.Main/UpdateWaypointMovement, boss_gluth/UpdateAI#2, boss_patchwerk/CustomGetTarget, boss_sapphiron/SetRandomMove, boss_sapphiron/UpdateReachable, burning_steppes/Reset#2, ChatHandler.CharacterCommands/HandleCharacterAIInfoCommand, ChatHandler.CreatureCommands/HandleNpcAIInfoCommand, ChatHandler.CreatureCommands/HandleNpcUnFollowCommand, ChatHandler.CreatureCommands/HandleWpAddCommand, ChatHandler.CreatureCommands/HandleWpExportCommand, ChatHandler.CreatureCommands/HandleWpModifyCommand, ChatHandler.CreatureCommands/HandleWpShowCommand, ChatHandler.PlayerBotMgr/StopPartyBotAttackHelper, Creature.Main/CanFleeFromCallForHelpAgainst, Creature.Main/IsInEvadeMode, Creature.Main/TryToCast, CreatureAI/SetCombatMovement, CritterAI/DamageTaken, CritterAI/SpellHit, eastern_plaguelands/UpdateAI, feralas/EnterEvadeMode, feralas/UpdateAI#4, PartyBotAI/DrinkAndEat, PartyBotAI/UpdateAI, PartyBotAI/UpdateInCombatAI_Druid, PartyBotAI/UpdateInCombatAI_Hunter, PartyBotAI/UpdateInCombatAI_Mage, PartyBotAI/UpdateInCombatAI_Paladin, PartyBotAI/UpdateInCombatAI_Priest, PartyBotAI/UpdateInCombatAI_Warlock, PartyBotAI/UpdateInCombatAI_Warrior, Player.Main/TaxiStepFinished, PlayerAI/UpdateTarget, PlayerBotAI/UpdateAI, ScriptedEscortAI/ReturnToCombatStartPosition, ScriptedEscortAI/Start, ScriptedEscortAI/UpdateAI, ScriptedFollowerAI/EnterEvadeMode, ScriptedFollowerAI/SetFollowComplete, ScriptedFollowerAI/SetFollowPaused, ScriptedFollowerAI/StartFollow, silithus/Reset#10, ThreatListCopier.battleground_alterac/UpdateAI#4, Totem/Update, Unit.Main/CantPathToVictim, Unit.Main/RestoreMovement, Unit.Main/SelectHostileTarget, Unit.SpellAuras/HandleAuraModRoot, Unit.SpellAuras/HandleAuraModStun, Unit.SpellAuras/HandlePreventFleeing, Unit.SpellAuras/PeriodicDummyTick, WaypointMovementGenerator/StartMove, winterspring/Reset, WorldSession.MovementHandler/ExecuteTeleportNear, WorldSession.MovementHandler/HandleMoveWorldportAck, WorldSession.TaxiHandler/SendDoFlight | — |
| GetUsedMovementGeneratorsList | method | MotionMaster/begin#2, MotionMaster/end#2, MovementGenerator/GetMovementGeneratorType | ChatHandler.UnitCommands/HandleListMoveGensCommand | — |
| IsUsingIdleOrDefaultMovement | method | — | CreatureAI/SetCombatMovement | — |
| GetWaypointPathInformation | method | MovementGenerator/GetMovementGeneratorType, WaypointMovementGenerator/GetPathInformation#2 | — | — |
| GetDestination | method | MoveSpline/FinalDestination, MoveSpline/Finalized | ChatHandler.UnitCommands/HandleMovegensCommand, WorldObject.Object/operator()#2 | — |
| UpdateFinalDistanceToTarget | method | MovementGenerator/UpdateFinalDistance | — | — |
| MoveJump | method | — | — | — |
| MoveCharge | method | Object/IsPlayer, Object/ToCreature, Object/ToPlayer, Unit.Main/GetCombatReachToTarget | ChatHandler.UnitCommands/HandleChargeCommand, Map.ScriptCommands/ScriptCommand_SetMovementType, Spell.Main/OnSpellLaunch | — |
| MoveDistance | method | Object/IsPlayer, Unit.Main/GetCollisionHeight, WorldObject.Object/GetAngle, WorldObject.Object/GetDistanceSqr, WorldObject.Object/GetNearPoint, WorldObject.Object/GetPositionZ, WorldObject.Object/GetTransport, WorldObject.Object/IsWithinLOS | AiBotAI.Combat/UpdateInCombatAI_Druid, AiBotAI.Combat/UpdateInCombatAI_Hunter, AiBotAI.Combat/UpdateInCombatAI_Mage, AiBotAI.Combat/UpdateInCombatAI_Rogue, BattleBotAI.Main/UpdateInCombatAI_Druid, BattleBotAI.Main/UpdateInCombatAI_Hunter, BattleBotAI.Main/UpdateInCombatAI_Mage, BattleBotAI.Main/UpdateInCombatAI_Rogue, ChatHandler.DebugCommands/HandleDebugMoveDistanceCommand, Creature.Main/MoveAwayFromTarget, PartyBotAI/RunAwayFromTarget | — |
| ClearType | method | MotionMaster/begin, MotionMaster/end, MotionMaster/erase, MovementGenerator/Finalize#2, MovementGenerator/GetMovementGeneratorType | Unit.Main/ModConfuseSpell | — |
| ReInitializePatrolMovement | method | MotionMaster/begin, MotionMaster/end, MovementGenerator/GetMovementGeneratorType, WaypointMovementGenerator/InitPatrol | CreatureGroups/OnMemberDied, CreatureGroups/RemoveTemporaryLeader | — |
| PauseOutOfCombatMovement | method | MotionMaster/GetCurrent, RandomMovementGenerator/AddPauseTime, Unit.Main/GetMotionMaster, Unit.Main/IsInCombat, Unit.Main/IsStopped, Unit.Main/StopMoving, WaypointMovementGenerator/AddPauseTime | Map.ScriptCommands/ScriptCommand_Emote, WorldSession.BattleGroundHandler/HandleBattlemasterHelloOpcode, WorldSession.ItemHandler/SendListInventory, WorldSession.NPCHandler/HandleGossipHelloOpcode, WorldSession.NPCHandler/HandleGossipSelectOptionOpcode, WorldSession.QuestHandler/HandleQuestgiverHelloOpcode | — |

---

<!-- verify: boundary-bleed | foreign: create, Creature, Execute, respawn -->
