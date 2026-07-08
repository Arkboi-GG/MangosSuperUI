<!-- provenance: boundary-bleed -->
# Map.ScriptCommands

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Map.ScriptCommands

## Purpose & Responsibilities

This unit implements the **database-driven scripting engine actions** for the `Map` class in the WoWVMaNGOS server. It provides a large set of methods (`ScriptCommand_*`) that act as the atomic actions available to database scripts (typically stored in tables like `smart_scripts` or `areatrigger_scripts`, though the specific table names are not exposed in this unit).

Each method corresponds to a specific command ID defined in `ScriptCommands.h` (e.g., `SCRIPT_COMMAND_TALK`, `SCRIPT_COMMAND_SUMMON_CREATURE`). These methods are invoked by the `Map`'s script processing loop (implemented in `Map.cpp`, outside this unit) to execute scripted behaviors such as dialogue, movement, combat actions, quest updates, and instance state changes.

Key responsibilities include:
1.  **Validation:** Checking if the `source` and `target` objects provided by the script engine are valid (non-null, correct type, alive, etc.).
2.  **Execution:** Performing the specific action (e.g., casting a spell, moving a creature, updating instance data).
3.  **Control Flow:** Returning `true` to abort the rest of the script sequence if a critical failure occurs (controlled by the `SF_GENERAL_ABORT_ON_FAILURE` flag in `ScriptInfo`), or `false` to continue.
4.  **Logging:** Emitting detailed error logs via `Log.Main/Out` when inputs are invalid, aiding in script debugging.

This unit does not define the script storage or scheduling logic; it strictly defines the *actions* that can be taken. It relies heavily on cross-unit calls to `Creature`, `Unit`, `Player`, `GameObject`, `ScriptMgr`, `ObjectMgr`, and various AI and Motion Master systems. Note that while these methods are members of the `Map` class, they delegate map-specific lookups (like `GetGameObject` or `GetInstanceData`) to other partials of the `Map` class (e.g., `Map.cpp` or `Map.h` declarations implemented elsewhere).

## Member-by-Member Behavior

The members are grouped by functional subsystems.

### Communication & Emotes
*   **ScriptCommand_Talk**: Makes the `source` object speak. It supports random text selection from multiple IDs. It delegates to `ScriptMgr/DoScriptText`.
*   **ScriptCommand_Emote**: Makes the `source` unit perform an emote. It supports standard emotes and "targeted" emotes (where the creature faces and gestures toward a specific target). Targeted emotes involve pausing movement, adding a state flag, and scheduling cleanup events via `EventProcessor`.

### Movement & Positioning
*   **ScriptCommand_MoveTo**: Moves a `Creature` to specified coordinates. Supports absolute, relative-to-target, distance-from-target, and random-point coordinate types. Calculates speed based on travel time. Delegates to `Creature.MotionMaster/MovePoint` or `Unit.Main/MonsterMoveWithSpeed`.
*   **ScriptCommand_SetMovementType**: Sets the creature's movement generator type (Idle, Random, Waypoint, Chase, Flee, Follow, Charge, etc.). Clears existing movement if requested.
*   **ScriptCommand_SetHomePosition**: Updates the creature's home position (spawn point) to provided coordinates, current position, or default template position.
*   **ScriptCommand_TurnTo**: Rotates the unit to face a target object or a specific orientation angle.
*   **ScriptCommand_SetRun**: Toggles whether the creature walks or runs.
*   **ScriptCommand_SetFly**: Enables or disables flying for the unit.
*   **ScriptCommand_SetDefaultMovement**: Resets the creature's default movement behavior (e.g., wander distance, movement type).

### Combat & Aggression
*   **ScriptCommand_AttackStart**: Forces the creature to attack a specific target. Validates if the target is a valid attack target.
*   **ScriptCommand_AssistUnit**: Makes the creature assist a target by attacking the target's attacker.
*   **ScriptCommand_CombatStop**: Forces the unit to stop combat and delete its threat list.
*   **ScriptCommand_Evade**: Forces the creature to enter evade mode (retreat to home, clear threat, stop combat).
*   **ScriptCommand_Flee**: Makes the creature flee, optionally seeking assistance.
*   **ScriptCommand_CallForHelp**: Calls for help from nearby creatures within a radius.
*   **ScriptCommand_ZoneCombatPulse**: Sets the creature's combat status with the zone (used for area-wide combat states).
*   **ScriptCommand_AddThreat**: Adds threat to a specific target.
*   **ScriptCommand_ModifyThreat**: Modifies threat percentage for a target or all attackers.
*   **ScriptCommand_InterruptCasts**: Interrupts non-melee spells being cast by the unit.

### Spells & Auras
*   **ScriptCommand_CastSpell**: Casts a spell from the source to the target. Handles interruption of previous casts and triggered spell flags.
*   **ScriptCommand_AddAura**: Applies an aura (buff/debuff) to the unit.
*   **ScriptCommand_RemoveAura**: Removes auras from the unit, either all or specific ones by spell ID.
*   **ScriptCommand_AddSpellCooldown**: Adds a cooldown to a spell for the unit.
*   **ScriptCommand_RemoveSpellCooldown**: Removes a specific or all spell cooldowns.
*   **ScriptCommand_CreatureSpells**: Randomly selects a spell list for the creature's AI based on weighted chances.

### Creature Appearance & State
*   **ScriptCommand_Morph**: Changes the creature's display ID (model) or demorphs it. Updates speed stats.
*   **ScriptCommand_Mount**: Mounts or dismounts the creature. Can set a permanent default mount.
*   **ScriptCommand_SetEquipment**: Equips items in the creature's slots or resets to default equipment.
*   **ScriptCommand_SetStandState**: Sets the unit's standing/sitting/kneeling state.
*   **ScriptCommand_SetSheath**: Sets the weapon sheath state (melee, ranged, polearm).
*   **ScriptCommand_SetFaction**: Temporarily changes the creature's faction.
*   **ScriptCommand_SetActiveObject**: Marks the creature as an active object (always updated by the server).
*   **ScriptCommand_SetReactState**: Sets the creature's reaction state (Passive, Defensive, Aggressive).
*   **ScriptCommand_SetCommandState**: Issues a pet command (Stay, Attack, Follow, etc.).
*   **ScriptCommand_SetMeleeAttack**: Enables or disables melee attacks in the AI.
*   **ScriptCommand_SetCombatMovement**: Enables or disables movement during combat in the AI.
*   **ScriptCommand_SetPhase**: Sets the creature's phase (used for visibility/scripting logic). Supports increment/decrement.
*   **ScriptCommand_SetPhaseRandom**: Sets the phase to a random value from a list.
*   **ScriptCommand_SetPhaseRange**: Sets the phase to a random value within a range.
*   **ScriptCommand_UpdateEntry**: Changes the creature's entry ID (effectively transforming it into a different creature type).
*   **ScriptCommand_Invincibility**: Sets a health threshold below which the creature becomes invincible.

### Summons & Spawns
*   **ScriptCommand_SummonCreature**: Summons a temporary creature. Supports unique limits (checking for existing summons of the same entry), immediate attack targets, and starting scripts on the summon.
*   **ScriptCommand_SummonObject**: Summons a GameObject.
*   **ScriptCommand_LoadCreatureSpawn**: Loads a creature from the database spawn data. Can load with its group.
*   **ScriptCommand_LoadGameObject**: Loads a GameObject from the database spawn data.
*   **ScriptCommand_DespawnCreature**: Despawns or unsummons a creature with optional delays.
*   **ScriptCommand_DespawnGameObject**: Despawns a GameObject with optional respawn delay.
*   **ScriptCommand_RespawnCreature**: Respawns a dead creature. Can force respawn even if alive.
*   **ScriptCommand_RespawnGameObject**: Respawns a despawned GameObject.

### GameObjects & Doors
*   **ScriptCommand_OpenDoor**: Opens a door GameObject. Also triggers associated buttons if the target is a button.
*   **ScriptCommand_CloseDoor**: Closes a door GameObject. Also triggers associated buttons.
*   **ScriptCommand_ActivateGameObject**: Simulates a unit using a GameObject.
*   **ScriptCommand_SetGoState**: Sets the state of a GameObject (Ready, Active, Closed, Open, etc.).
*   **ScriptCommand_ResetDoorOrButton**: Resets a door or button to its default state.
*   **ScriptCommand_PlayCustomAnim**: Plays a custom animation on a GameObject.

### Quests & Rewards
*   **ScriptCommand_QuestExplored**: Triggers a quest exploration event for a player/group. Checks distance constraints.
*   **ScriptCommand_KillCredit**: Awards kill credit for a specific creature entry to a player/group.
*   **ScriptCommand_QuestCredit**: Awards quest credit for talking to a creature.
*   **ScriptCommand_FailQuest**: Fails a quest for a player/group.
*   **ScriptCommand_CreateItem**: Creates an item and adds it to a player's inventory.
*   **ScriptCommand_RemoveItem**: Removes an item from a player's inventory.

### Instance & Map Events
*   **ScriptCommand_SetData**: Sets, increments, or decrements instance data (uint32).
*   **ScriptCommand_SetData64**: Sets instance data (uint64), supporting raw values or source GUIDs.
*   **ScriptCommand_StartMapEvent**: Starts a timed scripted map event with success/failure conditions.
*   **ScriptCommand_EndMapEvent**: Ends a scripted map event, marking it as success or failure.
*   **ScriptCommand_AddMapEventTarget**: Adds an extra target to a running map event.
*   **ScriptCommand_RemoveMapEventTarget**: Removes targets from a map event based on conditions or specific criteria.
*   **ScriptCommand_SetMapEventData**: Sets, increments, or decrements data associated with a map event.
*   **ScriptCommand_SendMapEvent**: Sends an event signal to main, extra, or all targets of a map event.
*   **ScriptCommand_EditMapEvent**: Edits the success/failure conditions/scripts of a running map event.

### Script Control & Flow
*   **ScriptCommand_StartScript**: Starts another generic script, optionally with weighted random selection among multiple script IDs.
*   **ScriptCommand_StartScriptForAll**: Starts a script on all objects of a certain type within a radius.
*   **ScriptCommand_StartScriptOnGroup**: Starts a script on the source and all members of its group (player group or creature group).
*   **ScriptCommand_StartScriptOnZone**: Starts a script on all players in a specific zone.
*   **ScriptCommand_TerminateScript**: Terminates the current script sequence. Can conditionally terminate based on the presence of a nearby creature.
*   **ScriptCommand_TerminateCondition**: Terminates the script if a specific condition is met (or not met). Can also fail a quest.
*   **ScriptCommand_SendScriptEvent**: Sends a script event to the creature's AI.

### Utility & Misc
*   **ScriptCommand_TeleportTo**: Teleports a unit to specified coordinates.
*   **ScriptCommand_PlaySound**: Plays a sound effect, optionally distance-dependent or zone-wide.
*   **ScriptCommand_SendTaxiPath**: Activates a taxi flight path for a player.
*   **ScriptCommand_MeetingStone**: Adds a player to the LFG queue for a specific area.
*   **ScriptCommand_DealDamage**: Deals direct damage to a target.
*   **ScriptCommand_JoinCreatureGroup**: Joins a creature to another creature's group.
*   **ScriptCommand_LeaveCreatureGroup**: Removes a creature from its group.
*   **ScriptCommand_RemoveGuardians**: Removes guardians (pets/summons) from a unit.
*   **ScriptCommand_GameEvent**: Starts or stops a global game event.
*   **ScriptCommand_ServerVariable**: Sets a saved server variable.
*   **ScriptCommand_SetPvP**: Enables or disables PvP status for a player.
*   **ScriptCommand_FieldSet**: Directly sets a raw field value in the object's data structure.
*   **ScriptCommand_ModifyFlags**: Sets, removes, or toggles flags in an object's data fields.
*   **ScriptCommand_RemoveObject**: Removes a creature or GameObject from the world.

### Helper Functions
*   **ShouldAbortScript**: Inline helper that checks the `SF_GENERAL_ABORT_ON_FAILURE` flag in the script info.
*   **ChooseScriptIdToStart**: Helper that selects a script ID based on weighted random chances.

## Cross-Unit Boundaries

This unit acts as a facade, delegating almost all actual work to other subsystems.

*   **Creature / Unit / Player / GameObject**: The primary targets of the commands. Methods cast the generic `WorldObject*` pointers to these specific types to access their APIs (e.g., `Creature.Main/ToCreature`, `Unit.Main/ToUnit`).
*   **Creature.MotionMaster**: Used for all movement-related commands (`MoveTo`, `SetMovementType`, `StartWaypoints`).
*   **CreatureAI**: Used for combat and behavioral commands (`AttackStart`, `Evade`, `SetMeleeAttack`, `SetCombatMovement`, `SetPhase`, `SendScriptEvent`).
*   **ScriptMgr**: Used for text broadcasting (`DoScriptText`) and target resolution (`GetTargetByType`).
*   **ObjectMgr**: Used for retrieving template data (`GetGOData`, `GetCreatureTemplate`, `SetSavedVariable`).
*   **Map**: Used for map-level operations (`GetGameObject`, `GetInstanceData`, `StartScriptedEvent`, `PlayDirectSoundToMap`, `LoadCreatureSpawn`, `LoadGameObjectSpawn`). Note: These calls are made to the `Map` class instance that owns this partial, but the implementations reside in other parts of the `Map` class (e.g., `Map.cpp`).
*   **EventProcessor**: Used for scheduling delayed actions in targeted emotes.
*   **Log.Main**: Used extensively for error reporting.
*   **Group / CreatureGroups**: Used for group-based quests and script propagation.
*   **LFGMgr**: Used for meeting stone functionality.
*   **GameEventMgr**: Used for global game events.
*   **Conditions**: Used for evaluating termination conditions.

## Data Model

This unit does not directly query or modify database tables. It operates entirely on in-memory objects (`Creature`, `Unit`, `GameObject`, `InstanceData`, `ScriptedEvent`). The script data itself (`ScriptInfo`) is passed into these methods from the caller (presumably `Map::ScriptsProcess` in `Map.cpp`), which likely loads it from database tables such as `smart_scripts` or `areatrigger_scripts`. No SQL queries are present in this source file.

## Notable Implementation Details

1.  **Abort-on-Failure Logic**: Every command returns a boolean. If `ShouldAbortScript(script)` returns `true` (based on the `SF_GENERAL_ABORT_ON_FAILURE` flag), the method returns `true` to signal the script engine to stop executing subsequent steps. This allows scripts to fail gracefully or halt on critical errors.
2.  **Target Resolution Flexibility**: Many commands accept `source` and `target` as `WorldObject*`. They often check both arguments to see if either is the required type (e.g., `Player`, `Creature`). This allows scripts to specify targets in flexible ways.
3.  **Unique Summon Limits**: `ScriptCommand_SummonCreature` implements a "unique" check by searching the grid for existing creatures of the same entry within a radius. It counts alive (or dead, depending on flags) creatures and aborts if the limit is reached.
4.  **Targeted Emotes**: `ScriptCommand_Emote` has special logic for targeted emotes. It pauses movement, adds a state flag, and schedules two events: one to perform the emote and one to clean up the state after a delay. This prevents the creature from moving away while emoting.
5.  **Door/Button Coupling**: `ScriptCommand_OpenDoor` and `ScriptCommand_CloseDoor` check if the `target` is a Button GameObject. If so, they also trigger the button, ensuring doors and buttons stay synchronized.
6.  **Phase Management**: Phase commands (`SetPhase`, `SetPhaseRandom`, `SetPhaseRange`) require the creature to have a `CreatureEventAI`. They manipulate the `m_Phase` member of the AI. There is a hard limit (`MAX_PHASE`) beyond which it logs an error and aborts.
7.  **Map Events**: The map event commands (`StartMapEvent`, `EndMapEvent`, etc.) interact with `ScriptedEvent` objects stored in the `Map`'s `m_mScriptedEvents` map. These events support success/failure conditions, timers, and multiple targets.
8.  **Error Logging**: Extensive use of `sLog.Out` with `LOG_SCRIPTS` and `LOG_LVL_ERROR` ensures that misconfigured scripts (null pointers, wrong types, invalid fields) are easily debuggable.
9.  **Raw Field Access**: `ScriptCommand_FieldSet` and `ScriptCommand_ModifyFlags` allow direct manipulation of object fields. This is powerful but dangerous, hence the bounds checking against `GetValuesCount()`.
10. **Random Selection Helpers**: `ChooseScriptIdToStart` and similar logic in `CreatureSpells` implement weighted random selection, allowing designers to create varied behavior.

## Member Reference

**ShouldAbortScript**: Inline function that checks if the `SF_GENERAL_ABORT_ON_FAILURE` flag is set in the `ScriptInfo` structure. Returns `true` if the script should abort on failure.

**ScriptCommand_Talk**: Makes the source object speak text. Supports random text selection. Calls `ScriptMgr/DoScriptText`.

**ScriptCommand_Emote**: Performs an emote. Supports targeted emotes with movement pause and scheduled cleanup events.

**ScriptCommand_FieldSet**: Sets a raw uint32 value in the source object's data fields. Validates field index bounds.

**ScriptCommand_MoveTo**: Moves the creature to specified coordinates. Supports relative, distance-based, and random points. Calculates speed from travel time.

**ScriptCommand_ModifyFlags**: Sets, removes, or toggles flags in the source object's data fields. Validates field index bounds.

**ScriptCommand_InterruptCasts**: Interrupts non-melee spells cast by the source unit.

**ScriptCommand_TeleportTo**: Teleports the source unit to specified coordinates. Handles player vs. non-player teleportation differently.

**ScriptCommand_QuestExplored**: Triggers quest exploration for a player/group. Checks distance constraints.

**ScriptCommand_KillCredit**: Awards kill credit for a creature entry to a player/group.

**ScriptCommand_RespawnGameObject**: Respawns a despawned GameObject. Validates type and spawn status.

**ScriptCommand_SummonCreature**: Summons a temporary creature. Checks unique limits, sets AI, and can start a script on the summon.

**ScriptCommand_OpenDoor**: Opens a door GameObject. Also triggers associated buttons.

**ScriptCommand_CloseDoor**: Closes a door GameObject. Also triggers associated buttons.

**ScriptCommand_ActivateGameObject**: Simulates a unit using a GameObject.

**ScriptCommand_RemoveAura**: Removes auras from the source unit.

**ScriptCommand_CastSpell**: Casts a spell from source to target. Handles interruption and triggered flags.

**ScriptCommand_PlaySound**: Plays a sound effect. Supports distance-dependent and zone-wide playback.

**ScriptCommand_CreateItem**: Creates an item and adds it to a player's inventory.

**ScriptCommand_DespawnCreature**: Despawns or unsummons a creature.

**ScriptCommand_SetEquipment**: Equips items or resets equipment for a creature.

**ScriptCommand_SetMovementType**: Sets the creature's movement generator type (Idle, Chase, Flee, etc.).

**ScriptCommand_SetActiveObject**: Marks the creature as an active object.

**ScriptCommand_SetFaction**: Temporarily changes the creature's faction.

**ScriptCommand_Morph**: Changes the creature's display ID or demorphs it.

**ScriptCommand_Mount**: Mounts or dismounts the creature. Can set a permanent default mount.

**ScriptCommand_SetRun**: Toggles walking/running for the creature.

**ScriptCommand_AttackStart**: Forces the creature to attack a target.

**ScriptCommand_UpdateEntry**: Changes the creature's entry ID.

**ScriptCommand_SetStandState**: Sets the unit's standing/sitting/kneeling state.

**ScriptCommand_ModifyThreat**: Modifies threat percentage for a target or all attackers.

**ScriptCommand_SendTaxiPath**: Activates a taxi flight path for a player.

**ScriptCommand_TerminateScript**: Terminates the script sequence. Can conditionally terminate based on nearby creature presence.

**ScriptCommand_TerminateCondition**: Terminates the script if a condition is met. Can fail a quest.

**ScriptCommand_Evade**: Forces the creature to enter evade mode.

**ScriptCommand_SetHomePosition**: Updates the creature's home position.

**ScriptCommand_TurnTo**: Rotates the unit to face a target or orientation.

**ScriptCommand_MeetingStone**: Adds a player to the LFG queue.

**ScriptCommand_SetData**: Sets, increments, or decrements instance data (uint32).

**ScriptCommand_SetData64**: Sets instance data (uint64), supporting raw values or source GUIDs.

**ChooseScriptIdToStart**: Helper function that selects a script ID based on weighted random chances.

**ScriptCommand_StartScript**: Starts another generic script, optionally with weighted random selection.

**ScriptCommand_RemoveItem**: Removes an item from a player's inventory.

**ScriptCommand_RemoveObject**: Removes a creature or GameObject from the world.

**ScriptCommand_SetMeleeAttack**: Enables or disables melee attacks in the AI.

**ScriptCommand_SetCombatMovement**: Enables or disables movement during combat in the AI.

**ScriptCommand_SetPhase**: Sets the creature's phase. Supports increment/decrement. Requires `CreatureEventAI`.

**ScriptCommand_SetPhaseRandom**: Sets the phase to a random value from a list. Requires `CreatureEventAI`.

**ScriptCommand_SetPhaseRange**: Sets the phase to a random value within a range. Requires `CreatureEventAI`.

**ScriptCommand_Flee**: Makes the creature flee, optionally seeking assistance.

**ScriptCommand_DealDamage**: Deals direct damage to a target.

**ScriptCommand_ZoneCombatPulse**: Sets the creature's combat status with the zone.

**ScriptCommand_CallForHelp**: Calls for help from nearby creatures.

**ScriptCommand_SetSheath**: Sets the weapon sheath state.

**ScriptCommand_Invincibility**: Sets a health threshold for invincibility.

**ScriptCommand_GameEvent**: Starts or stops a global game event.

**ScriptCommand_ServerVariable**: Sets a saved server variable.

**ScriptCommand_CreatureSpells**: Randomly selects a spell list for the creature's AI.

**ScriptCommand_RemoveGuardians**: Removes guardians from a unit.

**ScriptCommand_AddSpellCooldown**: Adds a cooldown to a spell.

**ScriptCommand_RemoveSpellCooldown**: Removes a specific or all spell cooldowns.

**ScriptCommand_SetReactState**: Sets the creature's reaction state.

**ScriptCommand_StartWaypoints**: Starts waypoint movement for the creature.

**ScriptCommand_StartMapEvent**: Starts a timed scripted map event.

**ScriptCommand_EndMapEvent**: Ends a scripted map event.

**ScriptCommand_AddMapEventTarget**: Adds an extra target to a running map event.

**ScriptCommand_RemoveMapEventTarget**: Removes targets from a map event.

**ScriptCommand_SetMapEventData**: Sets, increments, or decrements map event data.

**ScriptCommand_SendMapEvent**: Sends an event signal to map event targets.

**ScriptCommand_SetDefaultMovement**: Resets the creature's default movement behavior.

**ScriptCommand_StartScriptForAll**: Starts a script on all objects of a certain type within a radius.

**ScriptCommand_EditMapEvent**: Edits the success/failure conditions/scripts of a running map event.

**ScriptCommand_FailQuest**: Fails a quest for a player/group.

**ScriptCommand_RespawnCreature**: Respawns a dead creature.

**ScriptCommand_AssistUnit**: Makes the creature assist a target by attacking its attacker.

**ScriptCommand_CombatStop**: Forces the unit to stop combat and delete its threat list.

**ScriptCommand_AddAura**: Applies an aura to the unit.

**ScriptCommand_AddThreat**: Adds threat to a specific target.

**ScriptCommand_SummonObject**: Summons a GameObject.

**ScriptCommand_SetFly**: Enables or disables flying for the unit.

**ScriptCommand_JoinCreatureGroup**: Joins a creature to another creature's group.

**ScriptCommand_LeaveCreatureGroup**: Removes a creature from its group.

**ScriptCommand_SetGoState**: Sets the state of a GameObject.

**ScriptCommand_DespawnGameObject**: Despawns a GameObject.

**ScriptCommand_LoadGameObject**: Loads a GameObject from database spawn data.

**ScriptCommand_QuestCredit**: Awards quest credit for talking to a creature.

**ScriptCommand_SetGossipMenu**: Sets the default gossip menu for a creature.

**ScriptCommand_SendScriptEvent**: Sends a script event to the creature's AI.

**ScriptCommand_SetPvP**: Enables or disables PvP status for a player.

**ScriptCommand_ResetDoorOrButton**: Resets a door or button to its default state.

**ScriptCommand_SetCommandState**: Issues a pet command.

**ScriptCommand_PlayCustomAnim**: Plays a custom animation on a GameObject.

**ScriptCommand_StartScriptOnGroup**: Starts a script on the source and all group members.

**ScriptCommand_LoadCreatureSpawn**: Loads a creature from database spawn data.

**ScriptCommand_StartScriptOnZone**: Starts a script on all players in a specific zone.

---

<!-- machine-true, projected from graph.json -->

## Map — Map.ScriptCommands

*Source:* ScriptCommands.cpp, ScriptCommands.h, Map.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ShouldAbortScript | function | — | — | — |
| ScriptCommand_Talk | method | Log.Main/Out, Object/GetTypeId, ScriptMgr/DoScriptText, Unit.Main/ToUnit | — | — |
| ScriptCommand_Emote | method | Creature.Main/AddCreatureState, Creature.Main/HasCreatureState, Creature.MotionMaster/PauseOutOfCombatMovement, EventProcessor/AddEvent, EventProcessor/CalculateTime, Log.Main/Out, Object/GetObjectGuid, Object/GetTypeId, Object/ToCreature, shared_Util/urand, TargetedEmoteCleanupEvent/TargetedEmoteCleanupEvent, TargetedEmoteEvent/TargetedEmoteEvent, Unit.Main/HandleEmote, Unit.Main/IsInCombat, Unit.Main/ToUnit, WorldObject.Object/GetOrientation | — | — |
| ScriptCommand_FieldSet | method | Log.Main/Out, Object/GetTypeId, Object/GetValuesCount, WorldObject.Object/SetUInt32Value | — | — |
| ScriptCommand_MoveTo | method | Creature.Main/ToCreature, Creature.MotionMaster/MovePoint, Log.Main/Out, Object/GetTypeId, shared_Util/frand, Unit.Main/GetMotionMaster, Unit.Main/HasUnitState, Unit.Main/IsAlive, Unit.Main/MonsterMoveWithSpeed, WorldObject.Object/GetAngle, WorldObject.Object/GetDistance#4, WorldObject.Object/GetNearPoint, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/GetRandomPoint | — | — |
| ScriptCommand_ModifyFlags | method | Log.Main/Out, Object/GetTypeId, Object/GetValuesCount, Object/HasFlag, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag | — | — |
| ScriptCommand_InterruptCasts | method | Log.Main/Out, Object/GetTypeId, SpellCaster/InterruptNonMeleeSpells, Unit.Main/ToUnit | — | — |
| ScriptCommand_TeleportTo | method | Log.Main/Out, Object/GetGuidStr, Object/GetTypeId, Player.Main/TeleportTo, Unit.Main/ToUnit, WorldObject.Object/FindMap | — | — |
| ScriptCommand_QuestExplored | method | Group/GetFirstMember, GroupReference/next, Log.Main/Out, Object/IsPlayer, Player.Main/AreaExploredOrEventHappens, Player.Main/FailQuest, Player.Main/GetGroup, Player.Main/ToPlayer, WorldObject.Object/IsWithinDistInMap | — | — |
| ScriptCommand_KillCredit | method | Log.Main/Out, Player.Main/KilledMonsterCredit, Player.Main/RewardPlayerAndGroupAtEvent, Player.Main/ToPlayer | — | — |
| ScriptCommand_RespawnGameObject | method | GameObject/GetGoType, GameObject/isSpawned, GameObject/SetLootState, GameObject/SetRespawnTime, Log.Main/Out, Map.Main/GetGameObject, Object/GetTypeId, ObjectGuid/ObjectGuid#3, ObjectMgr/GetGOData | — | — |
| ScriptCommand_SummonCreature | method | Creature.Main/AI, Creature.Main/IsTemporarySummon, Creature.Main/SetAI, Creature.MotionMaster/Initialize, CreatureAI/AttackStart, GridSearchers/GetCreatureListWithEntryInGrid#2, Log.Main/Out, Map.Main/ScriptsStart, NullCreatureAI/NullCreatureAI, Object/GetObjectGuid, Object/GetTypeId, ObjectDefines/IsRespawnableTempSummonType, ObjectGuid/ObjectGuid, ScriptMgr/GetTargetByType, Unit.Main/GetMotionMaster, Unit.Main/IsAlive, Unit.Main/SetWalk, Unit.Main/ToUnit, WorldObject.Object/GetDistance#4, WorldObject.Object/SummonCreature#2 | — | — |
| ScriptCommand_OpenDoor | method | GameObject/GetGoState, GameObject/GetGoType, GameObject/UseDoorOrButton, Log.Main/Out, Map.Main/GetGameObject, Object/GetTypeId, Object/IsType, ObjectGuid/ObjectGuid#3, ObjectMgr/GetGOData | — | — |
| ScriptCommand_CloseDoor | method | GameObject/GetGoState, GameObject/GetGoType, GameObject/UseDoorOrButton, Log.Main/Out, Map.Main/GetGameObject, Object/GetTypeId, Object/IsType, ObjectGuid/ObjectGuid#3, ObjectMgr/GetGOData | — | — |
| ScriptCommand_ActivateGameObject | method | GameObject/ToGameObject, GameObject/Use, Log.Main/Out, Unit.Main/ToUnit | — | — |
| ScriptCommand_RemoveAura | method | Log.Main/Out, Object/GetTypeId, Unit.Main/RemoveAllAuras, Unit.Main/RemoveAurasDueToSpell, Unit.Main/ToUnit | — | — |
| ScriptCommand_CastSpell | method | Creature.Main/TryToCast#2, Log.Main/Out, Object/ToCreature, Object/ToSpellCaster, Object/ToUnit, SpellCaster/CastSpell#2, SpellCaster/InterruptNonMeleeSpells, SpellCaster/IsNonMeleeSpellCasted, SpellCaster/ToSpellCaster | — | — |
| ScriptCommand_PlaySound | method | Log.Main/Out, Map.Main/IsContinent, Map.Main/PlayDirectSoundToMap, Object/GetTypeId, Player.Main/ToPlayer, WorldObject.Object/GetZoneId, WorldObject.Object/PlayDirectSound, WorldObject.Object/PlayDistanceSound | — | — |
| ScriptCommand_CreateItem | method | Log.Main/Out, Object/GetTypeId, Player.Main/SendNewItem, Player.Main/StoreNewItemInInventorySlot, Player.Main/ToPlayer | — | — |
| ScriptCommand_DespawnCreature | method | Creature.Main/DespawnOrUnsummon, Creature.Main/ToCreature, Log.Main/Out, Object/GetTypeId, Unit.Main/IsAlive | — | — |
| ScriptCommand_SetEquipment | method | Creature.Main/GetCreatureInfo, Creature.Main/LoadEquipment, Creature.Main/SetVirtualItem, Creature.Main/ToCreature, Log.Main/Out, Object/GetTypeId | — | — |
| ScriptCommand_SetMovementType | method | Creature.Main/MoveAwayFromTarget, Creature.Main/ToCreature, Creature.MotionMaster/MoveCharge, Creature.MotionMaster/MoveChase, Creature.MotionMaster/MoveConfused, Creature.MotionMaster/MoveDistract, Creature.MotionMaster/MoveFleeing, Creature.MotionMaster/MoveFollow, Creature.MotionMaster/MoveIdle, Creature.MotionMaster/MoveRandom, Creature.MotionMaster/MoveTargetedHome, Creature.MotionMaster/MoveWaypoint, Log.Main/Out, MotionMaster/Clear, Object/GetTypeId, shared_Util/frand, Unit.Main/GetMotionMaster, Unit.Main/GetPowerPercent, Unit.Main/GetVictim, Unit.Main/IsAlive, Unit.Main/StopMoving, Unit.Main/ToUnit, WorldObject.Object/IsMoving | — | — |
| ScriptCommand_SetActiveObject | method | Creature.Main/ToCreature, Log.Main/Out, Object/GetTypeId, WorldObject.Object/SetActiveObjectState | — | — |
| ScriptCommand_SetFaction | method | Creature.Main/ClearTemporaryFaction, Creature.Main/SetFactionTemporary, Creature.Main/ToCreature, Log.Main/Out, Object/GetTypeId | — | — |
| ScriptCommand_Morph | method | Creature.Main/ChooseDisplayId, Log.Main/Out, Object/GetTypeId, Object/IsCreature, ObjectMgr/GetCreatureTemplate, Unit.Main/DeMorph, Unit.Main/IsAlive, Unit.Main/SetDisplayId, Unit.Main/ToUnit, Unit.Main/UpdateSpeed | — | — |
| ScriptCommand_Mount | method | Creature.Main/ChooseDisplayId, Creature.Main/SetDefaultMount, Creature.Main/ToCreature, Log.Main/Out, Object/GetTypeId, ObjectMgr/GetCreatureTemplate, Unit.Main/IsAlive, Unit.Main/Mount, Unit.Main/ToUnit, Unit.Main/Unmount | — | — |
| ScriptCommand_SetRun | method | Creature.Main/ToCreature, Log.Main/Out, Object/GetTypeId, Unit.Main/SetWalk | — | — |
| ScriptCommand_AttackStart | method | Creature.Main/AI, Creature.Main/ToCreature, CreatureAI/AttackStart, Log.Main/Out, Object/GetTypeId, Unit.Main/IsAlive, Unit.Main/ToUnit, WorldObject.Object/IsValidAttackTarget | — | — |
| ScriptCommand_UpdateEntry | method | Creature.Main/ToCreature, Creature.Main/UpdateEntry, Log.Main/Out, Object/GetEntry, Object/GetTypeId | — | — |
| ScriptCommand_SetStandState | method | Log.Main/Out, Object/GetTypeId, Unit.Main/IsAlive, Unit.Main/SetStandState, Unit.Main/ToUnit | — | — |
| ScriptCommand_ModifyThreat | method | Creature.Main/ToCreature, Log.Main/Out, Object/GetTypeId, ScriptMgr/GetTargetByType, ThreatManager/getThreatList, ThreatManager/modifyThreatPercent#2, Unit.Main/GetThreatManager, Unit.Main/ToUnit | — | — |
| ScriptCommand_SendTaxiPath | method | Log.Main/Out, Object/GetTypeId, Player.Main/ActivateTaxiPathTo#2, Player.Main/ToPlayer, Unit.Main/IsAlive | — | — |
| ScriptCommand_TerminateScript | method | Log.Main/Out, NearestCreatureEntryWithLiveStateInObjectRangeCheck/NearestCreatureEntryWithLiveStateInObjectRangeCheck | — | — |
| ScriptCommand_TerminateCondition | method | Conditions/IsConditionSatisfied, Player.Main/GroupEventFailHappens, Player.Main/ToPlayer | — | — |
| ScriptCommand_Evade | method | Creature.Main/AI, Creature.Main/ToCreature, CreatureAI/EnterEvadeMode, Log.Main/Out, Object/GetTypeId, Unit.Main/IsAlive | — | — |
| ScriptCommand_SetHomePosition | method | Creature.Main/ResetHomePosition, Creature.Main/SaveHomePosition, Creature.Main/SetHomePosition, Creature.Main/ToCreature, Log.Main/Out, Object/GetTypeId | — | — |
| ScriptCommand_TurnTo | method | Log.Main/Out, Object/GetTypeId, Unit.Main/IsAlive, Unit.Main/SetFacingTo, Unit.Main/SetFacingToObject, Unit.Main/ToUnit | — | — |
| ScriptCommand_MeetingStone | method | LFGMgr/AddToQueue, Log.Main/Out, Object/GetTypeId, Player.Main/ToPlayer | — | — |
| ScriptCommand_SetData | method | InstanceData/GetData, InstanceData/SetData, Log.Main/Out, Map.Main/GetInstanceData | — | — |
| ScriptCommand_SetData64 | method | InstanceData/SetData64, Log.Main/Out, Map.Main/GetInstanceData, Object/GetGUID | — | — |
| ChooseScriptIdToStart | function | shared_Util/urand | — | — |
| ScriptCommand_StartScript | method | Map.Main/ScriptsStart, Object/GetObjectGuid, ObjectGuid/ObjectGuid | — | — |
| ScriptCommand_RemoveItem | method | Log.Main/Out, Object/GetTypeId, Player.Main/DestroyItemCount#2, Player.Main/ToPlayer | — | — |
| ScriptCommand_RemoveObject | method | GameObject/Delete, GameObject/SetLootState, Log.Main/Out, Object/GetTypeId, Object/ToCreature, Object/ToGameObject, WorldObject.Object/AddObjectToRemoveList | — | — |
| ScriptCommand_SetMeleeAttack | method | Creature.Main/AI, Creature.Main/ToCreature, CreatureAI/SetMeleeAttack, Log.Main/Out, Object/GetTypeId | — | — |
| ScriptCommand_SetCombatMovement | method | Creature.Main/AI, Creature.Main/ToCreature, CreatureAI/SetCombatMovement, Log.Main/Out, Object/GetTypeId | — | — |
| ScriptCommand_SetPhase | method | Creature.Main/AI, Creature.Main/ToCreature, Log.Main/Out, Object/GetEntry, Object/GetTypeId | — | — |
| ScriptCommand_SetPhaseRandom | method | Creature.Main/AI, Creature.Main/ToCreature, Log.Main/Out, Object/GetTypeId | — | — |
| ScriptCommand_SetPhaseRange | method | Creature.Main/AI, Creature.Main/ToCreature, Log.Main/Out, Object/GetTypeId, shared_Util/urand | — | — |
| ScriptCommand_Flee | method | Creature.Main/DoFlee, Creature.Main/DoFleeToGetAssistance, Creature.Main/ToCreature, Log.Main/Out, Object/GetTypeId, Unit.Main/IsAlive | — | — |
| ScriptCommand_DealDamage | method | Log.Main/Out, Object/GetTypeId, Unit.Main/DealDamage, Unit.Main/GetMaxHealth, Unit.Main/IsAlive, Unit.Main/ToUnit | — | — |
| ScriptCommand_ZoneCombatPulse | method | Creature.Main/SetInCombatWithZone, Creature.Main/ToCreature, Log.Main/Out, Object/GetTypeId, Unit.Main/IsAlive | — | — |
| ScriptCommand_CallForHelp | method | Creature.Main/CallForHelp, Creature.Main/ToCreature, Log.Main/Out, Object/GetTypeId, Unit.Main/IsAlive | — | — |
| ScriptCommand_SetSheath | method | Log.Main/Out, Object/GetTypeId, Unit.Main/SetSheath, Unit.Main/ToUnit | — | — |
| ScriptCommand_Invincibility | method | Creature.Main/ToCreature, Log.Main/Out, Object/GetTypeId, Unit.Main/GetMaxHealth, Unit.Main/SetInvincibilityHpThreshold | — | — |
| ScriptCommand_GameEvent | method | GameEventMgr.Main/StartEvent, GameEventMgr.Main/StopEvent | — | — |
| ScriptCommand_ServerVariable | method | ObjectMgr/SetSavedVariable | — | — |
| ScriptCommand_CreatureSpells | method | Creature.Main/AI, Creature.Main/ToCreature, CreatureAI/SetSpellsList#2, Log.Main/Out, Object/GetTypeId, shared_Util/urand | — | — |
| ScriptCommand_RemoveGuardians | method | Log.Main/Out, Object/GetTypeId, Unit.Main/RemoveGuardians, Unit.Main/RemoveGuardiansWithEntry, Unit.Main/ToUnit | — | — |
| ScriptCommand_AddSpellCooldown | method | Log.Main/Out, Object/GetObjectGuid, Object/GetTypeId, Object/ToPlayer, Player.Main/SendSpellCooldown, SpellCaster/AddCooldown, SpellMgr/GetSpellEntry, SpellMgr/Instance, Unit.Main/ToUnit | — | — |
| ScriptCommand_RemoveSpellCooldown | method | Log.Main/Out, Object/GetTypeId, SpellCaster/RemoveAllCooldowns, SpellCaster/RemoveSpellCooldown#2, Unit.Main/ToUnit | — | — |
| ScriptCommand_SetReactState | method | CharmInfo/SetReactState, Creature.Main/SetCreatureReactState, Creature.Main/ToCreature, Log.Main/Out, Object/GetTypeId, Unit.Main/GetCharmInfo | — | — |
| ScriptCommand_StartWaypoints | method | Creature.Main/ToCreature, Creature.MotionMaster/MoveWaypoint, Log.Main/Out, MotionMaster/Clear, Object/GetTypeId, Unit.Main/GetMotionMaster, Unit.Main/IsAlive | — | — |
| ScriptCommand_StartMapEvent | method | Map.Main/StartScriptedEvent | — | — |
| ScriptCommand_EndMapEvent | method | Map.Main/EndEvent | — | — |
| ScriptCommand_AddMapEventTarget | method | Log.Main/Out, Map.Main/GetScriptedMapEvent, ScriptedEvent/AddOrUpdateExtraTarget | — | — |
| ScriptCommand_RemoveMapEventTarget | method | Conditions/IsConditionSatisfied, Log.Main/Out, Map.Main/GetScriptedMapEvent, Map.Main/GetWorldObject, Object/GetObjectGuid, ObjectGuid/operator== | — | — |
| ScriptCommand_SetMapEventData | method | Log.Main/Out, Map.Main/GetScriptedMapEvent, ScriptedEvent/DecrementData, ScriptedEvent/IncrementData, ScriptedEvent/SetData | — | — |
| ScriptCommand_SendMapEvent | method | Log.Main/Out, Map.Main/GetScriptedMapEvent, Map.Main/SendEventToAdditionalTargets, Map.Main/SendEventToAllTargets, Map.Main/SendEventToMainTargets | — | — |
| ScriptCommand_SetDefaultMovement | method | Creature.Main/SetDefaultMovementType, Creature.Main/SetWanderDistance, Creature.Main/ToCreature, Creature.MotionMaster/InitializeNewDefault, Log.Main/Out, Object/GetTypeId, Unit.Main/GetMotionMaster, Unit.Main/IsAlive | — | — |
| ScriptCommand_StartScriptForAll | method | AllWorldObjectsInRange/AllWorldObjectsInRange, Log.Main/Out, Map.Main/ScriptsStart, Object/GetEntry, Object/GetObjectGuid, Object/IsCreature, Object/IsGameObject, Object/IsPlayer, Object/IsUnit, ObjectGuid/ObjectGuid | — | — |
| ScriptCommand_EditMapEvent | method | Log.Main/Out, Map.Main/GetScriptedMapEvent | — | — |
| ScriptCommand_FailQuest | method | Log.Main/Out, Player.Main/GroupEventFailHappens, Player.Main/ToPlayer | — | — |
| ScriptCommand_RespawnCreature | method | Creature.Main/Respawn, Creature.Main/SetDeathState, Creature.Main/ToCreature, Log.Main/Out, Object/GetTypeId, Unit.Main/IsAlive | — | — |
| ScriptCommand_AssistUnit | method | Creature.Main/EnterCombatWithTarget, Creature.Main/ToCreature, Log.Main/Out, Object/GetTypeId, Unit.Main/GetAttackerForHelper, Unit.Main/ToUnit, WorldObject.Object/IsValidAttackTarget, WorldObject.Object/IsWithinDistInMap | — | — |
| ScriptCommand_CombatStop | method | Log.Main/Out, Object/GetTypeId, Unit.Main/CombatStop, Unit.Main/DeleteThreatList, Unit.Main/IsInCombat, Unit.Main/ToUnit | — | — |
| ScriptCommand_AddAura | method | Log.Main/Out, Object/GetTypeId, Unit.Main/AddAura, Unit.Main/ToUnit | — | — |
| ScriptCommand_AddThreat | method | Creature.Main/ToCreature, Log.Main/Out, Object/GetTypeId, Unit.Main/AddThreat, Unit.Main/IsAlive, Unit.Main/IsInCombat, Unit.Main/ToUnit, WorldObject.Object/IsValidAttackTarget | — | — |
| ScriptCommand_SummonObject | method | Log.Main/Out, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SummonGameObject | — | — |
| ScriptCommand_SetFly | method | Log.Main/Out, Object/GetTypeId, Unit.Main/SetFly, Unit.Main/ToUnit | — | — |
| ScriptCommand_JoinCreatureGroup | method | Creature.Main/JoinCreatureGroup, Creature.Main/ToCreature, Log.Main/Out, Object/GetTypeId | — | — |
| ScriptCommand_LeaveCreatureGroup | method | Creature.Main/LeaveCreatureGroup, Creature.Main/ToCreature, Log.Main/Out, Object/GetTypeId | — | — |
| ScriptCommand_SetGoState | method | GameObject/SetGoState, GameObject/ToGameObject, Log.Main/Out | — | — |
| ScriptCommand_DespawnGameObject | method | GameObject/isSpawned, GameObject/SetLootState, GameObject/SetRespawnDelay, Log.Main/Out, Map.Main/GetGameObject, Object/GetTypeId, ObjectGuid/ObjectGuid#3, ObjectMgr/GetGOData | — | — |
| ScriptCommand_LoadGameObject | method | Map.Main/LoadGameObjectSpawn | — | — |
| ScriptCommand_QuestCredit | method | Log.Main/Out, Object/GetEntry, Object/GetObjectGuid, Object/IsPlayer, Player.Main/TalkedToCreature, Player.Main/ToPlayer | — | — |
| ScriptCommand_SetGossipMenu | method | Creature.Main/SetDefaultGossipMenuId, Creature.Main/ToCreature, Log.Main/Out, Object/GetTypeId | — | — |
| ScriptCommand_SendScriptEvent | method | Creature.Main/AI, Creature.Main/ToCreature, CreatureAI/OnScriptEventHappened, Log.Main/Out, Object/GetTypeId | — | — |
| ScriptCommand_SetPvP | method | Log.Main/Out, Object/GetTypeId, Player.Main/ToPlayer, Player.Main/UpdatePvP | — | — |
| ScriptCommand_ResetDoorOrButton | method | GameObject/ResetDoorOrButton, GameObject/ToGameObject, Log.Main/Out | — | — |
| ScriptCommand_SetCommandState | method | Creature.Main/ToCreature, Log.Main/Out, Object/GetTypeId, Unit.Main/HandlePetCommand, Unit.Main/ToUnit | — | — |
| ScriptCommand_PlayCustomAnim | method | GameObject/SendGameObjectCustomAnim, GameObject/ToGameObject, Log.Main/Out | — | — |
| ScriptCommand_StartScriptOnGroup | method | Creature.Main/GetCreatureGroup, CreatureGroups/GetLeaderGuid, CreatureGroups/GetMembers, Group/GetFirstMember, GroupReference/next, Log.Main/Out, Map.Main/ScriptsStart, Object/GetObjectGuid, Object/GetTypeId, Object/ToCreature, Object/ToPlayer, ObjectGuid/ObjectGuid, ObjectGuid/operator!=, Player.Main/GetGroup, Unit.Main/ToUnit | — | — |
| ScriptCommand_LoadCreatureSpawn | method | Map.Main/LoadCreatureSpawn, Map.Main/LoadCreatureSpawnWithGroup | — | — |
| ScriptCommand_StartScriptOnZone | method | Map.Main/ScriptsStart, Object/GetObjectGuid, ObjectGuid/ObjectGuid, Player.Main/GetCachedZoneId, Unit.Main/GetPet | — | — |

---

<!-- verify: boundary-bleed | foreign: Map -->
