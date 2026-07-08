# Movement Speed

<!-- aliases: movement speed, run speed, move faster, increase speed, mount speed -->
<!-- documentation: model-written from source via the local LLM; review before trusting -->

Movement speed in VMaNGOS is governed by a layered system that combines static template data, dynamic aura modifiers, and explicit AI-driven overrides. The core calculation happens in `Unit::UpdateSpeed`, which determines the effective speed rate for a specific movement type (`MOVE_WALK`, `MOVE_RUN`, `MOVE_SWIM`, etc.). This calculated rate is then applied via `Unit::SetSpeedRate`, which handles the network synchronization: for player-controlled units, it queues a change requiring client acknowledgment (`MovementPacketSender::AddSpeedChangeToController`); for server-controlled entities (NPCs, pets), it immediately applies the rate and broadcasts the change to observers (`MovementPacketSender::SendSpeedChangeToAll`).

For creatures, the base speed is not a single value but derived from `Creature::GetBaseRunSpeedRate` or `Creature::GetBaseWalkSpeedRate`. These methods prioritize mount-specific display addons, then the creature's current display ID, then the static `creature_template` entry, falling back to a default constant if no data is found. During combat, `Unit::UpdateSpeed` further modifies this base by applying positive aura modifiers (like speed buffs), negative modifiers (slows), and specific penalties for low health (wounded slowdown) unless the creature is a world boss or has immunity flags.

Boss AI scripts frequently bypass the aura-based calculation by directly calling `SetSpeedRate` or `UpdateSpeed` with a hardcoded ratio to enforce encounter mechanics. For example, `boss_onyxia` switches between `ONYXIA_NORMAL_SPEED` and `ONYXIA_BREATH_SPEED` during phase transitions, while `boss_buru` halves his speed when locking onto a target and resets it upon enraging. Quest NPCs, such as those in `quest_stormwind_rendezvous`, use similar direct calls to ensure cinematic timing remains consistent regardless of player buffs or debuffs.

Administrators can manipulate these speeds at runtime using chat commands. `ChatHandler::HandleModifySpeedCommand` allows changing a player's run speed, `HandleModifySwimCommand` affects swimming, and `HandleModifyBWalkCommand` adjusts backward movement. For NPCs, `ChatHandler::HandleModifyASpeedCommand` applies a uniform modifier to walk, run, and swim speeds. All these commands ultimately funnel through `Unit::UpdateSpeed` or `SetSpeedRate`, ensuring the changes are properly synchronized with the client via the movement handler (`WorldSession::HandleForceSpeedChangeAckOpcodes`).

## How to Modify

### Config
No dedicated configuration key exists for global movement speed scaling in the provided material. While the project background mentions a `Rate.*` family for XP and drops, the source slices for movement speed do not reference any `CONFIG_FLOAT_RATE_*` keys for speed. Speed adjustments must be made via Database or Code changes.

### Database
Creature base speeds are defined in the `creature_template` table. Although the specific schema is not provided, the code in `Creature::GetBaseRunSpeedRate` and `Creature::GetBaseWalkSpeedRate` explicitly checks `GetCreatureInfo()->speed_run` and `GetCreatureInfo()->speed_walk`. Modifying these columns in `creature_template` will change the baseline speed for all instances of that creature entry. Additionally, `CreatureDisplayInfoAddon` (referenced in the code as `sCreatureDisplayInfoAddonStorage`) may contain `speed_walk` and `speed_run` values that override template values if a specific display ID or mount ID is used.

### Code
To change speed behavior globally or for specific encounters, edit the following members:
*   **`Unit::UpdateSpeed`** (`Unit.cpp`): Modify the logic here to alter how auras, health percentages, or pet ownership affect speed. For example, removing the `SPEED_REDUCTION_HP_5` penalty would prevent wounded slowdowns.
*   **`Creature::GetBaseRunSpeedRate` / `GetBaseWalkSpeedRate`** (`Creature.cpp`): Change the fallback constants (`DEFAULT_NPC_RUN_SPEED_RATE`, `DEFAULT_NPC_WALK_SPEED_RATE`) or the priority order of data sources (template vs. display addon).
*   **Boss AI Scripts** (e.g., `boss_onyxia.cpp`, `boss_buru.cpp`): Edit the hardcoded speed ratios passed to `SetSpeedRate` or `UpdateSpeed` within `UpdateAI`, `Reset`, or phase transition methods to adjust encounter pacing.
*   **`MovementPacketSender::AddSpeedChangeToController`** (`MovementPacketSender.cpp`): Adjust the calculation of `newSpeedFlat` if you need to change how speed rates are converted to flat values for client synchronization.

## Path Reference

**Unit.Main/UpdateSpeed** (Unit.cpp): Calculates the final speed rate by combining base creature stats, aura modifiers, and health-based penalties, then delegates application to `SetSpeedRate`.

**Unit.Main/SetSpeedRateHelper** (Unit.cpp): A helper constructor used to propagate speed changes to controlled units like pets and guardians.

**Unit.Main/SetSpeedRate** (Unit.cpp): Orchestrates the speed change by checking for pending network updates, queuing changes for player-controlled units, or immediately broadcasting changes for server-controlled units.

**Unit.Main/SetSpeedRateReal** (Unit.cpp): Directly sets the internal speed rate array and propagates the change to controlled units without network queuing.

**boss_bug_trio/EnterEvadeMode** (boss_bug_trio.cpp): Restores normal run speed when the encounter evades, clearing the devour phase speed boost.

**boss_bug_trio/TriggerDevour** (boss_bug_trio.cpp): Boosts run speed to 2.7x and moves the creature to a corpse to initiate the devour mechanic.

**boss_bug_trio/UpdateAI** (boss_bug_trio.cpp): Manages the devour timer and restores normal speed after the healing phase completes.

**boss_buru/Reset** (boss_buru.cpp): Initializes the boss with a halved run speed (0.5x) for the pre-enrage phase.

**boss_buru/UpdateAI** (boss_buru.cpp): Dynamically adjusts speed between 0.5x (target locked) and 1.0x (enraged/free) based on encounter state.

**boss_four_horsemen/UpdateAI#3** (boss_four_horsemen.cpp): Sets summoned Voidzone creatures to a very low speed (0.1x) to keep them stationary.

**boss_herod/UpdateAI#2** (boss_herod.cpp): Sets Herod's walk speed to 2.2x during his initial movement phase before combat.

**boss_onyxia/Reset#2** (boss_onyxia.cpp): Sets Onyxia's run speed to `ONYXIA_NORMAL_SPEED` when the encounter resets.

**boss_onyxia/PhaseTwo** (boss_onyxia.cpp): Adjusts speed to `ONYXIA_BREATH_SPEED` when Onyxia prepares to cast Deep Breath.

**boss_onyxia/DoMovement** (boss_onyxia.cpp): Resets speed to `ONYXIA_NORMAL_SPEED` when Onyxia moves between waypoints in Phase 2.

**boss_onyxia/PhaseTransition** (boss_onyxia.cpp): Manages speed changes during takeoff and landing transitions between phases.

**boss_ossirian/Reset** (boss_ossirian.cpp): Initializes Ossirian with standard 1.0x walk and run speeds.

**boss_ossirian/UpdateAI** (boss_ossirian.cpp): Gradually increases Ossirian's run speed over time using a timer-based formula.

**boss_sartura/EnterEvadeMode#2** (boss_sartura.cpp): Restores normal run speed when the encounter evades.

**boss_sartura/ImpaleAssist** (boss_sartura.cpp): Boosts run speed to 2.5x to charge towards a target for the Impale ability.

**boss_sartura/MovementInform** (boss_sartura.cpp): Restores normal run speed after the Impale charge movement completes.

**ChatHandler.CharacterCommands/HandleMountCommand** (CharacterCommands.cpp): Sets player run speed to 2.0x when mounting a creature.

**ChatHandler.CharacterCommands/HandleModifySpeedCommand** (CharacterCommands.cpp): Allows admins to modify a player's run speed via chat command.

**ChatHandler.CharacterCommands/HandleModifySwimCommand** (CharacterCommands.cpp): Allows admins to modify a player's swim speed via chat command.

**ChatHandler.CharacterCommands/HandleModifyBWalkCommand** (CharacterCommands.cpp): Allows admins to modify a player's backward run speed via chat command.

**ChatHandler.UnitCommands/HandleModifyASpeedCommand** (UnitCommands.cpp): Allows admins to modify walk, run, and swim speeds for any unit via chat command.

**Creature.Main/IsWorldBoss** (Creature.h): Checks if a creature is a world boss, which exempts it from wounded slowdown penalties in `Unit::UpdateSpeed`.

**Creature.Main/InitEntry** (Creature.cpp): Initializes the creature's entry data and triggers initial speed updates for walk and run modes.

**Creature.Main/DoFlee** (Creature.cpp): Triggers fleeing behavior and updates run speed to reflect the flee state.

**Creature.Main/DoFleeToGetAssistance** (Creature.cpp): Triggers fleeing to assistance and updates run speed accordingly.

**Creature.Main/GetBaseWalkSpeedRate** (Creature.cpp): Retrieves the base walk speed from display addons, template, or defaults.

**Creature.Main/GetBaseRunSpeedRate** (Creature.cpp): Retrieves the base run speed from display addons, template, or defaults.

**instance_dire_maul/UpdateFormationSpeed** (instance_dire_maul.cpp): Adjusts the walk speed of Monstruosities in Dire Maul to maintain formation spacing.

**moonglade/WaypointReached** (moonglade.cpp): Sets Remulos' walk speed to 2.2x during the "Nightmare Manifests" escort quest.

**MovementPacketSender/AddSpeedChangeToController** (MovementPacketSender.cpp): Queues speed changes for player-controlled units, calculating flat speed and incrementing movement counters.

**MovementPacketSender/GetChangeTypeByMoveType** (MovementPacketSender.cpp): Maps internal `UnitMoveType` enums to network `MovementChangeType` identifiers.

**MovementPacketSender/SendSpeedChangeToAll** (MovementPacketSender.cpp): Broadcasts speed changes to all observers for server-controlled units.

**quest_stormwind_rendezvous/UpdateAI** (quest_stormwind_rendezvous.cpp): Manages Windsor's movement speeds during the Stormwind Rendezvous quest event.

**quest_stormwind_rendezvous/UpdateAI#2** (quest_stormwind_rendezvous.cpp): Manages Squire Rowe's movement speeds during the quest event.

**quest_stormwind_rendezvous/AreaTrigger_at_stormwind_gates** (quest_stormwind_rendezvous.cpp): Spawns Windsor with a 1.0x run speed for legacy quest triggers.

**WorldSession.MovementHandler/HandleForceSpeedChangeAckOpcodes** (MovementHandler.cpp): Processes client acknowledgments for speed changes, validating counters and applying the final speed rate.

---

<!-- machine-true, projected from graph.json -->

## Map — Movement Speed

*Source:* Unit.cpp, boss_bug_trio.cpp, boss_buru.cpp, boss_four_horsemen.cpp, boss_herod.cpp, boss_onyxia.cpp, boss_ossirian.cpp, boss_sartura.cpp, CharacterCommands.cpp, UnitCommands.cpp, Creature.h, Creature.cpp, instance_dire_maul.cpp, moonglade.cpp, MovementPacketSender.cpp, quest_stormwind_rendezvous.cpp, MovementHandler.cpp
*Config keys:* —
*Tables:* —

| Member | Kind | Source | Role |
|---|---|---|---|
| Unit.Main/UpdateSpeed | method | Unit.cpp:7174-7315 | seed — Unit.*/UpdateSpeed* |
| Unit.Main/SetSpeedRateHelper | ctor | Unit.cpp:7358-7358 | seed — Unit.*/SetSpeed* |
| Unit.Main/SetSpeedRate | method | Unit.cpp:7367-7393 | seed — Unit.*/SetSpeed* |
| Unit.Main/SetSpeedRateReal | method | Unit.cpp:7395-7400 | seed — Unit.*/SetSpeed* |
| boss_bug_trio/EnterEvadeMode | method | boss_bug_trio.cpp:65-70 | related — 1 hop from a seed |
| boss_bug_trio/TriggerDevour | method | boss_bug_trio.cpp:125-137 | related — 1 hop from a seed |
| boss_bug_trio/UpdateAI | method | boss_bug_trio.cpp:167-206 | related — 1 hop from a seed |
| boss_buru/Reset | method | boss_buru.cpp:89-117 | related — 1 hop from a seed |
| boss_buru/UpdateAI | method | boss_buru.cpp:151-325 | related — 1 hop from a seed |
| boss_four_horsemen/UpdateAI#3 | method | boss_four_horsemen.cpp:453-496 | related — 1 hop from a seed |
| boss_herod/UpdateAI#2 | method | boss_herod.cpp:279-309 | related — 1 hop from a seed |
| boss_onyxia/Reset#2 | method | boss_onyxia.cpp:158-201 | related — 1 hop from a seed |
| boss_onyxia/PhaseTwo | method | boss_onyxia.cpp:423-501 | related — 1 hop from a seed |
| boss_onyxia/DoMovement | method | boss_onyxia.cpp:503-536 | related — 1 hop from a seed |
| boss_onyxia/PhaseTransition | method | boss_onyxia.cpp:567-658 | related — 1 hop from a seed |
| boss_ossirian/Reset | method | boss_ossirian.cpp:96-134 | related — 1 hop from a seed |
| boss_ossirian/UpdateAI | method | boss_ossirian.cpp:224-289 | related — 1 hop from a seed |
| boss_sartura/EnterEvadeMode#2 | method | boss_sartura.cpp:477-481 | related — 1 hop from a seed |
| boss_sartura/ImpaleAssist | method | boss_sartura.cpp:483-488 | related — 1 hop from a seed |
| boss_sartura/MovementInform | method | boss_sartura.cpp:490-498 | related — 1 hop from a seed |
| ChatHandler.CharacterCommands/HandleMountCommand | method | CharacterCommands.cpp:1191-1214 | related — 1 hop from a seed |
| ChatHandler.CharacterCommands/HandleModifySpeedCommand | method | CharacterCommands.cpp:4549-4594 | related — 1 hop from a seed |
| ChatHandler.CharacterCommands/HandleModifySwimCommand | method | CharacterCommands.cpp:4596-4641 | related — 1 hop from a seed |
| ChatHandler.CharacterCommands/HandleModifyBWalkCommand | method | CharacterCommands.cpp:4643-4688 | related — 1 hop from a seed |
| ChatHandler.UnitCommands/HandleModifyASpeedCommand | method | UnitCommands.cpp:2202-2242 | related — 1 hop from a seed |
| Creature.Main/IsWorldBoss | method | Creature.h:203-209 | related — 1 hop from a seed |
| Creature.Main/InitEntry | method | Creature.cpp:306-435 | related — 1 hop from a seed |
| Creature.Main/DoFlee | method | Creature.cpp:1092-1123 | related — 1 hop from a seed |
| Creature.Main/DoFleeToGetAssistance | method | Creature.cpp:1125-1157 | related — 1 hop from a seed |
| Creature.Main/GetBaseWalkSpeedRate | method | Creature.cpp:1166-1187 | related — 1 hop from a seed |
| Creature.Main/GetBaseRunSpeedRate | method | Creature.cpp:1189-1210 | related — 1 hop from a seed |
| instance_dire_maul/UpdateFormationSpeed | method | instance_dire_maul.cpp:759-797 | related — 1 hop from a seed |
| moonglade/WaypointReached | method | moonglade.cpp:322-415 | related — 1 hop from a seed |
| MovementPacketSender/AddSpeedChangeToController | function | MovementPacketSender.cpp:46-65 | related — 1 hop from a seed |
| MovementPacketSender/GetChangeTypeByMoveType | function | MovementPacketSender.cpp:88-101 | related — 1 hop from a seed |
| MovementPacketSender/SendSpeedChangeToAll | function | MovementPacketSender.cpp:155-167 | related — 1 hop from a seed |
| quest_stormwind_rendezvous/UpdateAI | method | quest_stormwind_rendezvous.cpp:241-799 | related — 1 hop from a seed |
| quest_stormwind_rendezvous/UpdateAI#2 | method | quest_stormwind_rendezvous.cpp:897-956 | related — 1 hop from a seed |
| quest_stormwind_rendezvous/AreaTrigger_at_stormwind_gates | function | quest_stormwind_rendezvous.cpp:1014-1061 | related — 1 hop from a seed |
| WorldSession.MovementHandler/HandleForceSpeedChangeAckOpcodes | method | MovementHandler.cpp:410-513 | related — 1 hop from a seed |
