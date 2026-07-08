<!-- provenance: boundary-bleed -->
# ChatHandler.CreatureCommands

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ChatHandler.CreatureCommands

## Purpose & Responsibilities

`ChatHandler.CreatureCommands` (implemented in `CreatureCommands.cpp`) provides the server-side logic for game master (GM) and administrative chat commands that manipulate non-player characters (NPCs/Creatures). It serves as the primary interface for world editing, debugging, and runtime modification of creature behavior, appearance, and persistence.

The unit handles three distinct categories of operations:
1.  **Inspection:** Retrieving detailed runtime and database-backed information about selected creatures (stats, AI state, spawn data).
2.  **Modification & Persistence:** Altering creature properties (entry, level, display ID, faction, movement type) and persisting these changes to the `creature` and `creature_addon` database tables. It distinguishes between temporary runtime changes and permanent database updates.
3.  **Waypoint & Escort Management:** A complex subsystem for creating, modifying, visualizing, and exporting creature movement paths (waypoints) and escort quest scripts. This involves spawning temporary visual markers, interacting with the `WaypointManager`, and writing to `script_waypoint` and `script_escort_data` tables.

## Member-by-Member Behavior

### Inspection Commands

These commands retrieve data from the selected creature and print it to the GM's chat window. They rely heavily on `Creature.Main`, `Unit.Main`, and helper methods from the `ChatHandler` class (defined in `Chat.h` and implemented in other partials).

*   **`HandleNpcSpawnInfoCommand`**: Displays persistent spawn data for a creature. It verifies the creature has static database spawn data (`CreatureData`). It outputs creature IDs (supporting multiple IDs per spawn), position, orientation, respawn times (min/max), movement type, wander distance, and visibility modifiers. It calls `shared_Util/secsToTimeString` to format durations.
*   **`HandleNpcInfoCommand`**: Displays comprehensive runtime statistics for a creature. This includes GUID, faction, NPC flags, display IDs, entry, level, equipment, health/mana pools, armor, loot IDs, AI name, script name, and position. It calculates the remaining respawn delay dynamically.
*   **`HandleNpcAIInfoCommand`**: Provides deep inspection of the creature's Artificial Intelligence and movement state. It reports the AI class name (via RTTI `typeid`), react state, charm state (if applicable), combat movement/melee attack enablement, current movement generator type, spline origin, and despawn type (for summons). It delegates detailed AI-specific info to `CreatureAI/GetAIInformation`.

### Modification Commands (Runtime & Persistent)

Many commands come in pairs: one for temporary runtime changes (e.g., `HandleNpcSet...`) and one for persistent database changes (e.g., `HandleNpcSpawnSet...`). The persistent versions typically require the creature to have valid `CreatureData` and execute SQL via `Database/PExecuteLog`.

*   **`HandleNpcSpawnSetEntryCommand`** vs **`HandleNpcSetEntryCommand`**: Changes the creature's template entry. The spawn version updates the `creature` table (`id` column) and calls `Creature.Main/UpdateEntry`. The runtime version only updates the in-memory object. Both reject pets.
*   **`HandleNpcSetLevelCommand`**: Sets the creature's level. For pets, it calls `Pet.Main/GivePetLevel`. For standard creatures, it manually sets health to `100 + 30 * level` and updates the level field. Note: This formula is hardcoded and may not reflect accurate scaling for all creature types.
*   **`HandleNpcSpawnSetDisplayIdCommand`** vs **`HandleNpcSetDisplayIdCommand`**: Changes the visual model. The spawn version updates the `creature_addon` table (`display_id` column) and the runtime object. It validates the display ID exists in the DBC store.
*   **`HandleNpcSpawnSetEmoteStateCommand`**: Sets the persistent emote state. Updates `creature_addon.emote_state` and the runtime `UNIT_NPC_EMOTESTATE` field. Validates the emote ID against the DBC store.
*   **`HandleNpcSpawnSetStandStateCommand`**: Sets the persistent stand state (standing/sitting/kneeling). Updates `creature_addon.stand_state`. Validates against `MAX_UNIT_STAND_STATE`.
*   **`HandleNpcSpawnSetSheathStateCommand`**: Sets the persistent sheath state (hand/gun/bow). Updates `creature_addon.sheath_state`. Validates against `MAX_SHEATH_STATE`.
*   **`HandleNpcSpawnSetAurasCommand`**: Applies a list of spell IDs as persistent auras. It parses space-separated spell IDs, validates them via `SpellMgr/GetSpellEntry`, applies them via `ChatHandler.UnitCommands/HandleAuraHelper`, and stores the space-separated string in `creature_addon.auras`. It manages memory for the aura array in the addon data structure.
*   **`HandleNpcSetFactionIdCommand`**: Temporarily changes the creature's faction using `Creature.Main/SetFactionTemporary`. Validates the faction ID via `ObjectMgr/GetFactionTemplateEntry`.
*   **`HandleNpcSetFlagCommand`**: Sets the `UNIT_NPC_FLAGS` field directly. Does not persist to DB.
*   **`HandleNpcSpawnSetDeathStateCommand`**: Forces a creature to spawn as dead or alive. It modifies the `spawn_flags` in `CreatureData` (adding/removing `SPAWN_FLAG_DEAD`), saves to DB, and respawns the creature.
*   **`HandleNpcSpawnWanderDistCommand`** vs **`HandleNpcSetWanderDistCommand`**: Sets the wander distance. The spawn version updates `creature.wander_distance` and persists it. The runtime version also sets the movement type to `RANDOM_MOTION_TYPE` if the distance is greater than zero.
*   **`HandleNpcSpawnSetRespawnTimeCommand`** vs **`HandleNpcSetRespawnTimeCommand`**: Sets respawn delays. The spawn version allows setting min/max times, updating `creature.spawntimesecsmin` and `spawntimesecsmax`, and calculating the average for the runtime delay.
*   **`HandleNpcSetReactStateCommand`**: Changes the creature's aggression mode (Passive/Defensive/Aggressive). Validates the input against `REACT_AGGRESSIVE`.
*   **`HandleNpcEvadeCommand`**: Forces the creature's AI to enter evade mode, stopping combat and resetting threat.
*   **`HandleNpcPlayEmoteCommand`**: Triggers a one-time emote animation.
*   **`HandleNpcSayCommand`**, **`HandleNpcYellCommand`**, **`HandleNpcTextEmoteCommand`**, **`HandleNpcWhisperCommand`**: Make the creature speak. `HandleNpcWhisperCommand` requires a target player and checks security permissions via `ChatHandler.Chat/HasLowerSecurity`.

### Spawning, Despawning, and Deletion

*   **`HandleNpcAddCommand`**: Creates a new permanent creature spawn. It generates a static low GUID, creates the creature object, saves it to the `creature` table, loads associated data (goods, quests), and adds it to the grid. It fails if no static GUIDs are available.
*   **`HandleNpcSummonCommand`**: Summons a temporary creature at the player's position using `WorldObject.Object/SummonCreature`.
*   **`HandleNpcDeleteCommand`**: Removes a creature. It checks if the GUID is referenced in scripts (`ScriptMgr/IsCreatureGuidReferencedInScripts`) to prevent accidental deletion of scripted spawns. It handles different subtypes (generic, pet, totem, temporary summon) with specific cleanup routines (e.g., `Pet.Main/Unsummon`, `Creature.Main/DeleteFromDB`).
*   **`HandleNpcDespawnCommand`**: Despawns the creature temporarily (it will respawn according to its timer).
*   **`HandleRespawnCommand`**: If a specific dead creature is selected, it respawns it. Otherwise, it respawns all dead creatures within the player's visibility distance using a grid visitor (`MaNGOS::RespawnDo`).
*   **`HandleNpcAddEntryCommand`**: Adds an additional creature template ID to a multi-ID spawn. It updates the `creature` table columns `id` through `id5`. It ensures no duplicates and respects the maximum ID count.
*   **`HandleNpcAddWeaponCommand`**: Equips a weapon to a specific slot (main hand, off hand, ranged) using `Creature.Main/SetVirtualItem`.
*   **`HandleNpcAddVendorItemCommand`** / **`HandleNpcDelVendorItemCommand`**: Manages vendor items for a creature. These interact with `ObjectMgr/AddVendorItem` and `ObjectMgr/RemoveVendorItem`. Note: These commands modify the vendor list for the *entry* globally, not just the specific instance, unless the underlying ObjectMgr logic handles instance-specific overrides (which is not evident in this unit's calls).

### Movement and Positioning

*   **`HandleNpcSpawnMoveCommand`** / **`HandleNpcMoveCommand`**: Both delegate to **`HandleNpcMoveHelperCommand`**.
*   **`HandleNpcMoveHelperCommand`**: Moves the creature to the player's current position and orientation. If `save` is true (from `HandleNpcSpawnMoveCommand`), it updates the `creature` table (`position_x`, `position_y`, `position_z`, `orientation`). It resets the creature's home position and respawns it if alive to apply movement changes.
*   **`HandleNpcSpawnSetMoveTypeCommand`** / **`HandleNpcSetMoveTypeCommand`**: Sets the movement type (Idle, Random, Waypoint, Cyclic). The spawn version persists to `creature.movement_type` and optionally deletes existing waypoints if `NODEL` is not specified.
*   **`HandleComeToMeCommand`**: Orders the creature to move to the player's coordinates using `Creature.MotionMaster/MovePoint`.
*   **`HandleNpcFollowCommand`** / **`HandleNpcUnFollowCommand`**: Makes the creature follow the player using `Creature.MotionMaster/MoveFollow`. Unfollow checks if the creature is currently following the player and stops the movement generator.
*   **`HandleNpcAllowMovementCommand`** / **`HandleNpcAllowAttackCommand`**: Toggles whether the creature moves during combat or performs melee attacks, via `CreatureAI/SetCombatMovement` and `CreatureAI/SetMeleeAttack`.

### Grouping and Linking

*   **`HandleNpcGroupAddCommand`** / **`HandleNpcGroupAddRelCommand`**: Adds a creature to a formation group led by another creature. It calculates relative angle and distance. `AddRel` uses the angle relative to the leader, while `Add` uses the angle relative to the player. It saves group data to DB if the creature has static spawn data.
*   **`HandleNpcGroupDelCommand`**: Removes a creature from its group. If it was the leader, it deletes the entire group from DB.
*   **`HandleNpcGroupLinkCommand`**: Links a creature to a master for aggro sharing. It writes to the `creature_linking` table (`guid`, `master_guid`, `flag`).

### Waypoint System (`.wp` commands)

This subsystem manages creature movement paths. It uses temporary summons (`VISUAL_WAYPOINT`) to visualize points.

*   **`Helper_CreateWaypointFor`**: Internal helper that spawns a `TemporarySummonWaypoint` creature at a specific node's coordinates to visualize it.
*   **`UnsummonVisualWaypoints`**: Internal helper that finds and removes all visual waypoint summons owned by a specific creature GUID.
*   **`HandleWpAddCommand`**: Adds a new waypoint to a creature's path. It determines the path origin (by entry, GUID, or special) and inserts the new node at the player's current position. It then refreshes the visual waypoints.
*   **`HandleWpModifyCommand`**: Modifies an existing waypoint. Subcommands include:
    *   `waittime`: Sets delay.
    *   `scriptid`: Sets a script ID to run at the node.
    *   `orientation`: Sets the facing angle.
    *   `del`: Deletes the node.
    *   `move`: Moves the node to the player's position.
    It interacts with `WaypointManager` to update the path data.
*   **`HandleWpShowCommand`**: Controls visualization.
    *   `on`: Spawns visual markers for all waypoints.
    *   `off`: Removes them.
    *   `info`: Shows details of a selected visual waypoint.
    *   `first`/`last`: Spawns a marker for the first/last point.
*   **`HandleWpExportCommand`**: Exports the current waypoint path to a SQL file. It generates `INSERT` statements for `creature_movement_template`, `creature_movement`, or `creature_movement_special` depending on the path origin.

### Escort System (`.escorte` commands)

These commands manage legacy-style escort quests stored in `script_waypoint` and `script_escort_data`.

*   **`HandleEscortShowWpCommand`**: Visualizes escort waypoints by spawning `VISUAL_WAYPOINT` creatures at coordinates retrieved from `ScriptMgr/GetPointMoveList`.
*   **`HandleEscortHideWpCommand`**: Removes visual waypoints by querying the `creature` table for `VISUAL_WAYPOINT` entries on the current map and deleting them.
*   **`HandleEscortAddWpCommand`**: Adds a waypoint to `script_waypoint` for a given creature entry. Uses the player's current position.
*   **`HandleEscortModifyWpCommand`**: Updates a waypoint in `script_waypoint`.
*   **`HandleEscortCreateCommand`**: Creates an entry in `script_escort_data` linking a creature to a quest and faction.
*   **`HandleEscortClearWpCommand`**: Deletes all waypoints for a creature from `script_waypoint`.

## Cross-Unit Boundaries

*   **`ChatHandler.Chat`**: Used extensively for parsing arguments (`ExtractUInt32`, `ExtractFloat`, etc.) and sending feedback to the user (`SendSysMessage`, `PSendSysMessage`). These are methods of the `ChatHandler` class itself, implemented in other partials.
*   **`Creature.Main` / `Unit.Main` / `WorldObject.Object`**: The core objects being manipulated. Methods like `GetCreatureData`, `UpdateEntry`, `SetDisplayId`, `Respawn`, and `GetMotionMaster` are central to almost every command.
*   **`Database`**: `WorldDatabase.PExecuteLog` and `PExecute` are used for all persistent changes. This unit is responsible for constructing safe SQL queries for the `creature`, `creature_addon`, `creature_linking`, `script_waypoint`, and `script_escort_data` tables.
*   **`WaypointManager`**: Interacts with this singleton to add, modify, delete, and query waypoint paths.
*   **`CreatureGroups`**: Manages creature formations. `CreatureGroupsManager::ConvertDBGuid` and `CreatureGroup` methods are used for grouping commands.
*   **`ObjectMgr`**: Used for lookups (`GetCreatureTemplate`, `GetFactionTemplateEntry`, `GetItemPrototype`) and global modifications (`AddVendorItem`, `RemoveVendorItem`).
*   **`ScriptMgr`**: Checks for script references before deletion (`IsCreatureGuidReferencedInScripts`) and retrieves escort points (`GetPointMoveList`).
*   **`SpellMgr`**: Validates spell IDs for aura commands.
*   **`ChatHandler.UnitCommands`**: `HandleAuraHelper` is reused to apply auras to creatures.

## Data Model

This unit interacts with the following database tables:

*   **`creature`**:
    *   Used for: Storing persistent spawn data.
    *   Columns accessed: `guid` (PK), `id` (and `id2`-`id5` for multi-spawns), `position_x`, `position_y`, `position_z`, `orientation`, `spawntimesecsmin`, `spawntimesecsmax`, `wander_distance`, `movement_type`.
    *   Operations: `UPDATE` for modifications, `INSERT` for new spawns, `DELETE` for removal.

*   **`creature_addon`**:
    *   Used for: Storing cosmetic and behavioral addons.
    *   Columns accessed: `guid` (PK), `display_id`, `stand_state`, `sheath_state`, `emote_state`, `auras`.
    *   Operations: `UPDATE` or `REPLACE INTO` to ensure the row exists.

*   **`creature_linking`**:
    *   Used for: Aggro linking.
    *   Columns accessed: `guid` (PK), `master_guid`, `flag`.
    *   Operations: `DELETE` followed by `INSERT` to update links.

*   **`script_waypoint`**:
    *   Used for: Legacy escort waypoints.
    *   Columns accessed: `entry` (PK), `pointid` (PK), `location_x`, `location_y`, `location_z`, `waittime`.
    *   Operations: `INSERT`, `UPDATE`, `DELETE`.

*   **`script_escort_data`**:
    *   Used for: Defining escort quests.
    *   Columns accessed: `creature_id`, `quest`, `escort_faction`.
    *   Operations: `DELETE` followed by `INSERT`.

## Notable Implementation Details

1.  **Persistence Logic**: Many commands distinguish between "Spawn" (persistent) and non-"Spawn" (runtime) variants. The persistent variants always check for `CreatureData` (static spawn data) and fail if the creature is a pet or temporary summon. This prevents accidental corruption of non-persistent entities.
2.  **SQL Injection Risk**: The code constructs SQL queries using string formatting (`sprintf`-style via `PExecuteLog`). While it uses `%u` and `%f` for numeric inputs, the `HandleNpcSpawnSetAurasCommand` inserts the raw `args` string into the `auras` column. Since `args` is validated to contain only numeric spell IDs separated by spaces, this is likely safe, but care is taken. However, `HandleEscortAddWpCommand` uses `sscanf` and direct insertion, which is generally safe for numeric inputs but lacks parameterized query safety for any potential string extensions.
3.  **Visual Waypoints**: The waypoint system relies on spawning actual `Creature` objects (`VISUAL_WAYPOINT`) to visualize paths. This is a heavy operation. `UnsummonVisualWaypoints` scans the grid for these specific creatures to clean them up. Failure to clean them up can lead to performance issues or visual clutter.
4.  **Hardcoded Health Formula**: `HandleNpcSetLevelCommand` uses `100 + 30 * lvl` for health. This is a simplistic approximation and may result in incorrect health values for high-level or elite creatures compared to their template definitions.
5.  **Multi-ID Spawns**: `HandleNpcSpawnInfoCommand` and `HandleNpcAddEntryCommand` support multiple creature IDs per spawn (`id` to `id5`). The code iterates through `MAX_CREATURE_IDS_PER_SPAWN` to display or add IDs, ensuring sorted order and no duplicates.
6.  **Script Protection**: `HandleNpcDeleteCommand` explicitly checks `ScriptMgr::IsCreatureGuidReferencedInScripts` before allowing deletion. This is a critical safeguard against breaking scripted encounters by removing essential NPCs.
7.  **Escort vs. Waypoint Systems**: There are two distinct waypoint systems: the modern `WaypointManager` (used by `.wp` commands) and the legacy `script_waypoint` table (used by `.escorte` commands). They are managed separately and do not interoperate directly in this unit.

## Member Reference

**HandleNpcSpawnInfoCommand**: Displays persistent spawn data (IDs, position, respawn times, movement type) for a selected creature with static DB data.
**HandleNpcInfoCommand**: Displays comprehensive runtime stats (level, health, faction, AI, position) for a selected creature.
**HandleNpcAIInfoCommand**: Displays AI class, react state, movement generator, and charm state for a selected creature.
**HandleNpcSpawnSetEntryCommand**: Persists a change to the creature's template entry in the `creature` table.
**HandleNpcSetEntryCommand**: Temporarily changes the creature's template entry in memory.
**HandleNpcSetLevelCommand**: Sets the creature's level and recalculates health using a hardcoded formula.
**HandleNpcSpawnSetDisplayIdCommand**: Persists a change to the creature's display ID in `creature_addon`.
**HandleNpcSetDisplayIdCommand**: Temporarily changes the creature's display ID in memory.
**HandleNpcSpawnSetEmoteStateCommand**: Persists a change to the creature's emote state in `creature_addon`.
**HandleNpcSpawnSetStandStateCommand**: Persists a change to the creature's stand state in `creature_addon`.
**HandleNpcSpawnSetSheathStateCommand**: Persists a change to the creature's sheath state in `creature_addon`.
**HandleNpcSpawnSetAurasCommand**: Applies and persists a list of spell IDs as auras in `creature_addon`.
**HandleNpcSetFactionIdCommand**: Temporarily changes the creature's faction.
**HandleNpcSetFlagCommand**: Temporarily sets the creature's NPC flags.
**HandleNpcTameCommand**: Allows the player to tame the selected creature (if eligible and no pet exists).
**HandleNpcSpawnSetDeathStateCommand**: Persists a change to the creature's death state (spawn as dead/alive) in `creature` spawn flags.
**HandleNpcDespawnCommand**: Temporarily despawns the selected creature.
**HandleRespawnCommand**: Respawns a selected dead creature or all dead creatures in visibility range.
**HandleNpcSpawnWanderDistCommand**: Persists a change to the creature's wander distance in `creature`.
**HandleNpcSetWanderDistCommand**: Temporarily changes the creature's wander distance and movement type.
**HandleNpcSpawnSetRespawnTimeCommand**: Persists min/max respawn times in `creature`.
**HandleNpcSetRespawnTimeCommand**: Temporarily changes the creature's respawn delay.
**HandleNpcSetReactStateCommand**: Temporarily changes the creature's react state (passive/aggressive).
**HandleNpcEvadeCommand**: Forces the creature's AI to evade combat.
**HandleNpcPlayEmoteCommand**: Triggers a one-time emote animation.
**HandleNpcSayCommand**: Makes the creature say text in local chat.
**HandleNpcYellCommand**: Makes the creature yell text in local chat.
**HandleNpcTextEmoteCommand**: Makes the creature perform a text emote.
**HandleNpcWhisperCommand**: Makes the creature whisper text to a specified player.
**HandleNpcAddCommand**: Creates a new permanent creature spawn in the DB and world.
**HandleNpcSummonCommand**: Summons a temporary creature at the player's position.
**HandleNpcDeleteCommand**: Permanently deletes a creature from the world and DB, checking for script references.
**HandleNpcAddEntryCommand**: Adds an additional template ID to a multi-ID spawn in the DB.
**HandleNpcAddWeaponCommand**: Equips a weapon to a specific slot on the creature.
**HandleNpcAddVendorItemCommand**: Adds an item to the creature's vendor list (global to entry).
**HandleNpcDelVendorItemCommand**: Removes an item from the creature's vendor list (global to entry).
**HandleNpcSpawnMoveCommand**: Moves the creature to the player's position and persists the new coordinates in the DB.
**HandleNpcMoveCommand**: Moves the creature to the player's position without persisting to DB.
**HandleNpcMoveHelperCommand**: Internal helper for moving creatures, handling both runtime and persistent updates.
**HandleNpcSpawnSetMoveTypeCommand**: Persists a change to the creature's movement type in `creature`.
**HandleNpcSetMoveTypeCommand**: Temporarily changes the creature's movement type.
**HandleComeToMeCommand**: Orders the creature to move to the player's coordinates.
**HandleNpcFollowCommand**: Makes the creature follow the player.
**HandleNpcUnFollowCommand**: Stops the creature from following the player.
**HandleNpcAllowMovementCommand**: Toggles combat movement for the creature's AI.
**HandleNpcAllowAttackCommand**: Toggles melee attack capability for the creature's AI.
**HandleNpcGroupAddCommand**: Adds a creature to a formation group, saving relative position to DB.
**HandleNpcGroupAddRelCommand**: Adds a creature to a formation group, calculating angle relative to the leader.
**HandleNpcGroupDelCommand**: Removes a creature from its formation group, deleting group data if it was the leader.
**HandleNpcGroupLinkCommand**: Links a creature to a master for aggro sharing in `creature_linking`.
**Helper_CreateWaypointFor**: Internal helper to spawn a visual waypoint creature.
**UnsummonVisualWaypoints**: Internal helper to remove visual waypoint creatures.
**HandleWpAddCommand**: Adds a new waypoint to a creature's path and refreshes visuals.
**HandleWpModifyCommand**: Modifies an existing waypoint (waittime, script, orientation, delete, move).
**HandleWpShowCommand**: Controls visualization of waypoints (on/off/info/first/last).
**HandleWpExportCommand**: Exports the current waypoint path to a SQL file.
**HandleEscortShowWpCommand**: Visualizes legacy escort waypoints.
**HandleEscortHideWpCommand**: Removes visual legacy escort waypoints.
**HandleEscortAddWpCommand**: Adds a waypoint to `script_waypoint`.
**HandleEscortModifyWpCommand**: Updates a waypoint in `script_waypoint`.
**HandleEscortCreateCommand**: Creates an entry in `script_escort_data`.
**HandleEscortClearWpCommand**: Deletes all waypoints for a creature from `script_waypoint`.

---

<!-- machine-true, projected from graph.json -->

## Map — ChatHandler.CreatureCommands

*Source:* CreatureCommands.cpp, Chat.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| HandleNpcSpawnInfoCommand | method | ChatHandler.Chat/GetSelectedCreature, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Creature.Main/GetCreatureData, Object/GetGUIDLow, Object/GetObjectGuid, ObjectGuid/GetString, shared_Util/secsToTimeString, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/GetVisibilityModifier, WorldObject.Object/IsActiveObject | — | — |
| HandleNpcInfoCommand | method | ChatHandler.Chat/GetSelectedCreature, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Creature.Main/GetAIName, Creature.Main/GetCreatureInfo, Creature.Main/GetCurrentEquipmentId, Creature.Main/GetRespawnDelay, Creature.Main/GetRespawnTimeEx, Creature.Main/GetScriptName, Object/GetEntry, Object/GetGUIDLow, Object/GetGuidStr, Object/GetUInt32Value, shared_Util/secsToTimeString, Unit.Main/GetArmor, Unit.Main/GetCreateHealth, Unit.Main/GetCreateMana, Unit.Main/GetDisplayId, Unit.Main/GetFactionTemplateId, Unit.Main/GetHealth, Unit.Main/GetLevel, Unit.Main/GetMaxHealth, Unit.Main/GetMaxPower, Unit.Main/GetNativeDisplayId, Unit.Main/GetPower, Unit.Main/GetPowerType, WorldObject.Object/GetInstanceId, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/GetVisibilityModifier, WorldObject.Object/IsActiveObject | — | — |
| HandleNpcAIInfoCommand | method | CharmInfo/GetCommandState, CharmInfo/GetReactState, ChatHandler.Chat/GetOnOffStr, ChatHandler.Chat/GetSelectedCreature, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Creature.Main/AI, Creature.Main/GetAIName, Creature.Main/GetCreatureReactState, Creature.Main/GetScriptName, Creature.Main/IsTemporarySummon, Creature.MotionMaster/GetCurrentMovementGeneratorType, Creature.MotionMaster/GetMovementGeneratorTypeName, CreatureAI/GetAIInformation, CreatureAI/IsCombatMovementEnabled, CreatureAI/IsMeleeAttackEnabled, MoveSpline/Finalized, MoveSpline/GetMovementOrigin, Object/GetEntry, ObjectDefines/TempSummonTypeToString, TemporarySummon/GetDespawnType, Unit.Main/GetCharmInfo, Unit.Main/GetMotionMaster, UnitDefines/CommandStateToString, UnitDefines/ReactStateToString | ChatHandler.UnitCommands/HandleUnitAIInfoCommand | — |
| HandleNpcSpawnSetEntryCommand | method | ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/GetSelectedCreature, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Creature.Main/GetCreatureData, Creature.Main/GetDBTableGUIDLow, Creature.Main/IsPet, Creature.Main/UpdateEntry, Database/PExecuteLog | — | creature |
| HandleNpcSetEntryCommand | method | ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/GetSelectedCreature, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Creature.Main/IsPet, Creature.Main/UpdateEntry | — | — |
| HandleNpcSetLevelCommand | method | ChatHandler.Chat/GetSelectedCreature, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Creature.Main/IsPet, Pet.Main/GivePetLevel, Unit.Main/SetHealth, Unit.Main/SetLevel, Unit.Main/SetMaxHealth | — | — |
| HandleNpcSpawnSetDisplayIdCommand | method | ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/GetSelectedCreature, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Creature.Main/GetCreatureAddon, Creature.Main/GetCreatureData, Creature.Main/GetDBTableGUIDLow, Creature.Main/IsPet, Database/PExecuteLog, Unit.Main/SetDisplayId, Unit.Main/SetNativeDisplayId | — | creature_addon |
| HandleNpcSetDisplayIdCommand | method | ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/GetSelectedCreature, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Creature.Main/IsPet, Unit.Main/SetDisplayId, Unit.Main/SetNativeDisplayId | — | — |
| HandleNpcSpawnSetEmoteStateCommand | method | ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/GetSelectedCreature, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Creature.Main/GetCreatureAddon, Creature.Main/GetCreatureData, Creature.Main/GetDBTableGUIDLow, Creature.Main/IsPet, Database/PExecuteLog, WorldObject.Object/SetUInt32Value | — | creature_addon |
| HandleNpcSpawnSetStandStateCommand | method | ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/GetSelectedCreature, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Creature.Main/GetCreatureAddon, Creature.Main/GetCreatureData, Creature.Main/GetDBTableGUIDLow, Creature.Main/IsPet, Database/PExecuteLog, Unit.Main/SetStandState, UnitDefines/UnitStandStateToString | — | creature_addon |
| HandleNpcSpawnSetSheathStateCommand | method | ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/GetSelectedCreature, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Creature.Main/GetCreatureAddon, Creature.Main/GetCreatureData, Creature.Main/GetDBTableGUIDLow, Creature.Main/IsPet, Database/PExecuteLog, Unit.Main/SetSheath, UnitDefines/SheathStateToString | — | creature_addon |
| HandleNpcSpawnSetAurasCommand | method | ChatHandler.Chat/GetSelectedCreature, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, ChatHandler.UnitCommands/HandleAuraHelper, Creature.Main/GetCreatureAddon, Creature.Main/GetCreatureData, Creature.Main/GetDBTableGUIDLow, Creature.Main/IsPet, Database/PExecuteLog, shared_Util/isNumeric, shared_Util/StrSplit, SpellMgr/GetSpellEntry, SpellMgr/Instance | — | creature_addon |
| HandleNpcSetFactionIdCommand | method | ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/GetSelectedCreature, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Creature.Main/SetFactionTemporary, ObjectMgr/GetFactionTemplateEntry | — | — |
| HandleNpcSetFlagCommand | method | ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/GetSelectedCreature, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, WorldObject.Object/SetUInt32Value | — | — |
| HandleNpcTameCommand | method | ChatHandler.Chat/GetSelectedCreature, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Creature.Main/IsPet, SpellCaster/CastSpell#2, Unit.Main/GetPetGuid, WorldSession.Main/GetPlayer | — | — |
| HandleNpcSpawnSetDeathStateCommand | method | ChatHandler.Chat/ExtractOnOff, ChatHandler.Chat/GetSelectedCreature, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Creature.Main/HasStaticDBSpawnData, Creature.Main/Respawn, Creature.Main/SaveToDB, Object/GetGUIDLow, ObjectMgr/GetCreatureData | — | — |
| HandleNpcDespawnCommand | method | ChatHandler.Chat/GetSelectedCreature, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Creature.Main/DespawnOrUnsummon | — | — |
| HandleRespawnCommand | method | ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Creature.Main/Respawn, Map.Main/GetVisibilityDistance, Object/GetTypeId, Player.Main/GetSelectionGuid, RespawnDo/RespawnDo, Unit.Main/IsDead, WorldObject.Object/GetMap, WorldSession.Main/GetPlayer | — | — |
| HandleNpcSpawnWanderDistCommand | method | ChatHandler.Chat/ExtractFloat, ChatHandler.Chat/GetSelectedCreature, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Creature.Main/GetCreatureData, Creature.Main/GetDBTableGUIDLow, Creature.Main/SetWanderDistance, Creature.MotionMaster/Initialize, Database/PExecuteLog, Unit.Main/GetMotionMaster | — | creature |
| HandleNpcSetWanderDistCommand | method | ChatHandler.Chat/ExtractFloat, ChatHandler.Chat/GetSelectedCreature, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Creature.Main/SetDefaultMovementType, Creature.Main/SetWanderDistance, Creature.MotionMaster/Initialize, Unit.Main/GetMotionMaster | — | — |
| HandleNpcSpawnSetRespawnTimeCommand | method | ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/GetSelectedCreature, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Creature.Main/GetCreatureData, Creature.Main/GetDBTableGUIDLow, Creature.Main/SetRespawnDelay, Database/PExecuteLog | — | creature |
| HandleNpcSetRespawnTimeCommand | method | ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/GetSelectedCreature, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Creature.Main/SetRespawnDelay | — | — |
| HandleNpcSetReactStateCommand | method | ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/GetSelectedCreature, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Creature.Main/GetName, Unit.Main/SetReactState | — | — |
| HandleNpcEvadeCommand | method | ChatHandler.Chat/GetSelectedCreature, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Creature.Main/AI, CreatureAI/EnterEvadeMode | — | — |
| HandleNpcPlayEmoteCommand | method | ChatHandler.Chat/GetSelectedCreature, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Unit.Main/HandleEmote | — | — |
| HandleNpcSayCommand | method | ChatHandler.Chat/GetSelectedCreature, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, WorldObject.Object/MonsterSay | — | — |
| HandleNpcYellCommand | method | ChatHandler.Chat/GetSelectedCreature, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, WorldObject.Object/MonsterYell | — | — |
| HandleNpcTextEmoteCommand | method | ChatHandler.Chat/GetSelectedCreature, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, WorldObject.Object/MonsterTextEmote | — | — |
| HandleNpcWhisperCommand | method | ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/HasLowerSecurity, Map.Main/GetCreature, ObjectGuid/operator!, Player.Main/GetSelectionGuid, WorldObject.Object/GetMap, WorldObject.Object/MonsterWhisper, WorldSession.Main/GetPlayer | — | — |
| HandleNpcAddCommand | method | ChatHandler.Chat/ExtractUint32KeyFromLink, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Creature.Main/Create, Creature.Main/Creature, Creature.Main/LoadFromDB, Creature.Main/SaveToDB#2, CreatureCreatePos/CreatureCreatePos#2, Map.Main/GetId, Object/GetGUIDLow, ObjectMgr/AddCreatureToGrid, ObjectMgr/GenerateStaticCreatureLowGuid, ObjectMgr/GetCreatureData, ObjectMgr/GetCreatureTemplate, WorldObject.Object/GetMap, WorldObject.Object/GetOrientation, WorldSession.Main/GetPlayer | — | — |
| HandleNpcSummonCommand | method | ChatHandler.Chat/ExtractUint32KeyFromLink, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, ObjectMgr/GetCreatureTemplate, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SummonCreature#2, WorldSession.Main/GetPlayer | — | — |
| HandleNpcDeleteCommand | method | ChatHandler.Chat/ExtractUint32KeyFromLink, ChatHandler.Chat/GetSelectedCreature, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Creature.Main/AddToRemoveListInMaps, Creature.Main/DeleteFromDB#2, Creature.Main/GetDBTableGUIDLow, Creature.Main/GetSubtype, CreatureData/GetObjectGuid, Map.Main/GetCreature, Object/GetGUIDLow, ObjectMgr/GetCreatureData, Pet.Main/Unsummon, ScriptMgr/IsCreatureGuidReferencedInScripts, TemporarySummon/UnSummon, Totem/UnSummon, Unit.Main/CombatStop, WorldObject.Object/AddObjectToRemoveList, WorldObject.Object/GetMap, WorldSession.Main/GetPlayer | — | — |
| HandleNpcAddEntryCommand | method | ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/GetSelectedCreature, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Creature.Main/GetCreatureData, CreatureData/GetCreatureIdCount, Database/PExecute#2, Object/GetGUIDLow, ObjectMgr/GetCreatureTemplate | — | creature |
| HandleNpcAddWeaponCommand | method | ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/GetSelectedCreature, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, Creature.Main/SetVirtualItem, ObjectMgr/GetItemPrototype | — | — |
| HandleNpcAddVendorItemCommand | method | ChatHandler.Chat/ExtractOptUInt32, ChatHandler.Chat/ExtractUint32KeyFromLink, ChatHandler.Chat/GetSelectedCreature, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Object/GetEntry, ObjectMgr/AddVendorItem, ObjectMgr/GetItemPrototype, ObjectMgr/IsVendorItemValid, WorldSession.Main/GetPlayer | — | — |
| HandleNpcDelVendorItemCommand | method | ChatHandler.Chat/ExtractUint32KeyFromLink, ChatHandler.Chat/GetSelectedCreature, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Object/GetEntry, ObjectMgr/GetItemPrototype, ObjectMgr/RemoveVendorItem, Unit.Main/IsVendor | — | — |
| HandleNpcSpawnMoveCommand | method | — | — | — |
| HandleNpcMoveCommand | method | — | — | — |
| HandleNpcMoveHelperCommand | method | ChatHandler.Chat/ExtractUint32KeyFromLink, ChatHandler.Chat/GetSelectedCreature, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Creature.Main/Respawn, Creature.Main/SetDeathState, Creature.Main/SetHomePosition, CreatureData/GetObjectGuid, Database/PExecuteLog, Map.Main/GetCreature, Object/GetGUIDLow, ObjectMgr/GetCreatureData, Unit.Main/IsAlive, WorldObject.Object/GetMap, WorldObject.Object/GetMapId, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldSession.Main/GetPlayer | — | creature |
| HandleNpcSpawnSetMoveTypeCommand | method | ChatHandler.Chat/ExtractLiteralArg, ChatHandler.Chat/GetSelectedCreature, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Creature.Main/GetCreatureData, Creature.Main/GetDBTableGUIDLow, Creature.Main/SetDefaultMovementType, Creature.MotionMaster/Initialize, Database/PExecuteLog, Unit.Main/GetMotionMaster, WaypointManager/DeletePath | — | creature |
| HandleNpcSetMoveTypeCommand | method | ChatHandler.Chat/ExtractLiteralArg, ChatHandler.Chat/GetSelectedCreature, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Creature.Main/SetDefaultMovementType, Creature.MotionMaster/Initialize, Unit.Main/GetMotionMaster | — | — |
| HandleComeToMeCommand | method | ChatHandler.Chat/GetSelectedCreature, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Creature.MotionMaster/MovePoint, Unit.Main/GetMotionMaster, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldSession.Main/GetPlayer | — | — |
| HandleNpcFollowCommand | method | ChatHandler.Chat/GetSelectedCreature, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Creature.Main/GetName, Creature.MotionMaster/MoveFollow, Unit.Main/GetMotionMaster, WorldSession.Main/GetPlayer | — | — |
| HandleNpcUnFollowCommand | method | ChatHandler.Chat/GetSelectedCreature, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Creature.Main/GetName, Creature.MotionMaster/GetCurrentMovementGeneratorType, MotionMaster/MovementExpired, Unit.Main/GetMotionMaster, WorldSession.Main/GetPlayer | — | — |
| HandleNpcAllowMovementCommand | method | ChatHandler.Chat/ExtractOnOff, ChatHandler.Chat/GetSelectedCreature, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Creature.Main/AI, Creature.Main/GetName, CreatureAI/SetCombatMovement | — | — |
| HandleNpcAllowAttackCommand | method | ChatHandler.Chat/ExtractOnOff, ChatHandler.Chat/GetSelectedCreature, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Creature.Main/AI, Creature.Main/GetName, CreatureAI/SetMeleeAttack | — | — |
| HandleNpcGroupAddCommand | method | ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/GetSelectedCreature, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Creature.Main/GetCreatureGroup, Creature.Main/HasStaticDBSpawnData, Creature.Main/SetCreatureGroup, Creature.MotionMaster/Initialize, CreatureGroups/AddMember, CreatureGroups/ConvertDBGuid, CreatureGroups/CreatureGroup, CreatureGroups/SaveToDb, Map.Main/GetCreature, Object/GetGUIDLow, Object/GetObjectGuid, Unit.Main/GetMotionMaster, WorldObject.Object/GetAngle, WorldObject.Object/GetMap, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldSession.Main/GetPlayer | — | — |
| HandleNpcGroupAddRelCommand | method | ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/GetSelectedCreature, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Creature.Main/GetCreatureGroup, Creature.Main/HasStaticDBSpawnData, Creature.Main/SetCreatureGroup, Creature.MotionMaster/Initialize, CreatureGroups/AddMember, CreatureGroups/ConvertDBGuid, CreatureGroups/CreatureGroup, CreatureGroups/SaveToDb, Map.Main/GetCreature, Object/GetGUIDLow, Object/GetObjectGuid, Unit.Main/GetMotionMaster, WorldObject.Object/GetAngle, WorldObject.Object/GetMap, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY | — | — |
| HandleNpcGroupDelCommand | method | ChatHandler.Chat/GetSelectedCreature, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Creature.Main/GetCreatureGroup, Creature.Main/GetName, Creature.Main/HasStaticDBSpawnData, Creature.Main/LeaveCreatureGroup, Creature.MotionMaster/Initialize, CreatureGroups/DeleteFromDb, CreatureGroups/GetOriginalLeaderGuid, CreatureGroups/SaveToDb, Object/GetGUIDLow, Object/GetObjectGuid, ObjectGuid/operator==, Unit.Main/GetMotionMaster | — | — |
| HandleNpcGroupLinkCommand | method | ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/GetSelectedCreature, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, CreatureGroups/ConvertDBGuid, Database/PExecute#2, Map.Main/GetCreature, Object/GetGUIDLow, WorldObject.Object/GetMap | — | creature_linking |
| Helper_CreateWaypointFor | function | Creature.Main/Create, Creature.Main/SetSummonPoint, CreatureCreatePos/CreatureCreatePos, Map.Main/GenerateLocalLowGuid, Object/GetObjectGuid, TemporarySummon/Summon, TemporarySummon/TemporarySummonWaypoint, Unit.Main/SetVisibility, WorldObject.Object/AddUnitMovementFlag, WorldObject.Object/GetMap, WorldObject.Object/SetActiveObjectState, WorldObject.Object/SetUInt32Value | — | — |
| UnsummonVisualWaypoints | function | AllCreaturesOfEntryInRange/AllCreaturesOfEntryInRange, Creature.Main/GetSubtype, ObjectGuid/operator==, TemporarySummon/GetSummonerGuid, TemporarySummon/UnSummon | — | — |
| HandleWpAddCommand | method | ChatHandler.Chat/ExtractOptInt32, ChatHandler.Chat/ExtractOptUInt32, ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/GetSelectedCreature, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Creature.Main/GetSubtype, Creature.Main/HasStaticDBSpawnData, Creature.MotionMaster/GetCurrentMovementGeneratorType, CreatureData/GetObjectGuid, CreatureInfo/GetHighGuid, Database/PQuery, Field/GetUInt32, Log.Main/Out, Map.Main/GetAnyTypeCreature, MotionMaster/GetCurrent, Object/GetEntry, Object/GetGUIDLow, Object/GetGuidStr, Object/GetObjectGuid, ObjectGuid/GetString, ObjectMgr/GetCreatureData, ObjectMgr/GetCreatureTemplate, QueryResult/Fetch, TemporarySummon/GetSummonerGuid, TemporarySummonWaypoint/GetPathId, TemporarySummonWaypoint/GetPathOrigin, TemporarySummonWaypoint/GetWaypointId, Unit.Main/GetMotionMaster, WaypointManager/AddNode, WaypointManager/GetDefaultPath, WaypointManager/GetOriginString, WaypointManager/GetPathFromOrigin, WaypointMovementGenerator/GetPathInformation, WorldObject.Object/GetMap, WorldObject.Object/GetMapId, WorldObject.Object/GetPosition#2, WorldSession.Main/GetPlayer | — | creature |
| HandleWpModifyCommand | method | ChatHandler.Chat/ExtractFloat, ChatHandler.Chat/ExtractLiteralArg, ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/GetSelectedCreature, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Creature.Main/GetSubtype, Creature.Main/Respawn, Creature.Main/SaveToDB, Creature.Main/SetDeathState, Creature.Main/SetDefaultMovementType, Creature.MotionMaster/GetCurrentMovementGeneratorType, Creature.MotionMaster/Initialize, CreatureData/GetObjectGuid, CreatureInfo/GetHighGuid, Log.Main/Out, Map.Main/GetAnyTypeCreature, MotionMaster/GetCurrent, Object/GetEntry, Object/GetGUIDLow, Object/GetGuidStr, Object/GetObjectGuid, ObjectGuid/GetString, ObjectMgr/GetCreatureData, ObjectMgr/GetCreatureTemplate, TemporarySummon/GetSummonerGuid, TemporarySummonWaypoint/GetPathId, TemporarySummonWaypoint/GetPathOrigin, TemporarySummonWaypoint/GetWaypointId, Unit.Main/GetMotionMaster, Unit.Main/IsAlive, Unit.Main/NearTeleportTo, WaypointManager/DeleteNode, WaypointManager/GetDefaultPath, WaypointManager/GetOriginString, WaypointManager/GetPathFromOrigin, WaypointManager/SetNodeOrientation, WaypointManager/SetNodePosition, WaypointManager/SetNodeScriptId, WaypointManager/SetNodeWaittime, WaypointMovementGenerator/GetPathInformation, WorldObject.Object/GetMap, WorldObject.Object/GetOrientation, WorldObject.Object/GetPosition#2, WorldSession.Main/GetPlayer | — | — |
| HandleWpShowCommand | method | ChatHandler.Chat/ExtractLiteralArg, ChatHandler.Chat/ExtractOptInt32, ChatHandler.Chat/ExtractOptUInt32, ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/GetSelectedCreature, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Creature.Main/GetName, Creature.Main/GetScriptName, Creature.Main/GetSubtype, Creature.MotionMaster/GetCurrentMovementGeneratorType, CreatureData/GetObjectGuid, CreatureInfo/GetHighGuid, Log.Main/Out, Map.Main/GetAnyTypeCreature, Map.Main/GetCreature, MotionMaster/GetCurrent, Object/GetEntry, Object/GetGUIDLow, Object/GetGuidStr, Object/GetObjectGuid, ObjectGuid/GetString, ObjectMgr/GetCreatureData, ObjectMgr/GetCreatureTemplate, TemporarySummon/GetSummonerGuid, TemporarySummonWaypoint/GetPathId, TemporarySummonWaypoint/GetPathOrigin, TemporarySummonWaypoint/GetWaypointId, Unit.Main/GetMotionMaster, WaypointManager/GetDefaultPath, WaypointManager/GetOriginString, WaypointManager/GetPathFromOrigin, WaypointMovementGenerator/GetPathInformation, WorldObject.Object/GetMap, WorldSession.Main/GetPlayer | — | — |
| HandleWpExportCommand | method | ChatHandler.Chat/ExtractLiteralArg, ChatHandler.Chat/ExtractOptInt32, ChatHandler.Chat/ExtractOptUInt32, ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/GetSelectedCreature, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Creature.Main/GetSubtype, Creature.MotionMaster/GetCurrentMovementGeneratorType, CreatureData/GetObjectGuid, Map.Main/GetAnyTypeCreature, MotionMaster/GetCurrent, Object/GetEntry, Object/GetGUIDLow, ObjectGuid/GetString, ObjectMgr/GetCreatureData, TemporarySummon/GetSummonerGuid, TemporarySummonWaypoint/GetPathId, TemporarySummonWaypoint/GetPathOrigin, Unit.Main/GetMotionMaster, WaypointManager/GetDefaultPath, WaypointManager/GetPathFromOrigin, WaypointMovementGenerator/GetPathInformation, WorldObject.Object/GetMap, WorldObject.Object/GetMapId, WorldSession.Main/GetPlayer | — | — |
| HandleEscortShowWpCommand | method | ChatHandler.Chat/ExtractUint32KeyFromLink, ChatHandler.Chat/GetSelectedCreature, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Creature.Main/Create, Creature.Main/Creature, Creature.Main/GetCreatureInfo, Creature.Main/LoadFromDB, Creature.Main/SaveToDB#2, CreatureCreatePos/CreatureCreatePos, CreatureInfo/GetHighGuid, Log.Main/Out, Map.Main/GenerateLocalLowGuid, Map.Main/GetId, Object/GetGUIDLow, ObjectMgr/GetCreatureTemplate, ScriptMgr/GetPointMoveList, Unit.Main/SetVisibility, WorldObject.Object/GetMap, WorldObject.Object/GetOrientation, WorldSession.Main/GetPlayer | — | — |
| HandleEscortHideWpCommand | method | ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Creature.Main/DeleteFromDB, Database/PExecuteLog, Database/PQuery, Field/GetUInt32, Log.Main/Out, Map.Main/GetCreature, Map.Main/GetId, ObjectGuid/ObjectGuid#3, QueryResult/Fetch, QueryResult/NextRow, WorldObject.Object/AddObjectToRemoveList, WorldObject.Object/GetMap, WorldSession.Main/GetPlayer | — | creature |
| HandleEscortAddWpCommand | method | ChatHandler.Chat/GetSelectedCreature, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SetSentErrorMessage, Common/finiteAlways, Database/PExecute#2, Database/PQuery, Field/GetUInt32, Object/GetEntry, QueryResult/Fetch, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldSession.Main/GetPlayer | — | script_waypoint |
| HandleEscortModifyWpCommand | method | ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SetSentErrorMessage, Common/finiteAlways, Database/PExecute#2, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldSession.Main/GetPlayer | — | script_waypoint |
| HandleEscortCreateCommand | method | ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SetSentErrorMessage, Database/PExecute#2 | — | script_escort_data |
| HandleEscortClearWpCommand | method | ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SetSentErrorMessage, Database/PExecute#2 | — | script_waypoint |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `creature`: guid int(10) unsigned PK, id mediumint(8) unsigned, id2 mediumint(8) unsigned, id3 mediumint(8) unsigned, id4 mediumint(8) unsigned, id5 mediumint(8) unsigned, map smallint(5) unsigned, position_x float, position_y float, position_z float, orientation float, spawntimesecsmin int(10) unsigned, spawntimesecsmax int(10) unsigned, wander_distance float, health_percent float, mana_percent float unsigned, movement_type tinyint(3) unsigned, spawn_flags int(10) unsigned, visibility_mod float?, patch_min tinyint(3) unsigned, patch_max tinyint(3) unsigned
- `creature_addon`: guid int(10) unsigned PK, patch tinyint(3) unsigned PK, display_id smallint(5) unsigned, mount_display_id smallint(6), equipment_id int(11), stand_state tinyint(3) unsigned, sheath_state tinyint(3) unsigned, emote_state smallint(5) unsigned, auras text?
- `creature_linking`: guid int(10) unsigned PK, master_guid int(10) unsigned, flag mediumint(8) unsigned
- `script_escort_data`: creature_id int(11)?, quest int(11)?, escort_faction int(11)?
- `script_waypoint`: entry mediumint(8) unsigned PK, pointid mediumint(8) unsigned PK, location_x float, location_y float, location_z float, waittime int(10) unsigned, point_comment text?

*`?` = nullable, `PK` = primary key column.*


---

<!-- verify: boundary-bleed | foreign: ChatHandler, update -->
