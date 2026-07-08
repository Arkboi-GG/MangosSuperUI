# Creature Respawn Times

<!-- aliases: respawn time, respawn rate, spawn timer, faster respawns, mob respawn, creature spawn time, dynamic respawn -->
<!-- documentation: model-written from source via the local LLM; review before trusting -->

Creature respawn times in VMaNGOS are determined by a combination of static database definitions, runtime configuration for dynamic scaling, and server-side persistence logic. The process flows from the initial death of a `Creature`, through the calculation of its respawn delay, to the eventual reloading of the object into the world grid.

### How It Works

When a `Creature` dies, the server calculates when it should reappear. This involves two distinct concepts: the **Respawn Delay** (the base duration defined in the database) and the **Respawn Time** (the absolute Unix timestamp when the creature will actually spawn).

1.  **Base Delay Definition**: The fundamental respawn interval is stored in the `creature` table in the `mangos` database. Specifically, the columns `spawntimesecsmin` and `spawntimesecsmax` define a range. During loading (`ObjectMgr::LoadCreatures`), if `spawntimesecsmax` is less than `spawntimesecsmin`, the server logs an error and adjusts `spawntimesecsmax` to equal `spawntimesecsmin`. The effective base delay used for calculations is typically the average or the specific value set via `Creature::SetRespawnDelay`.

2.  **Dynamic Respawn Scaling**: Before the final delay is locked in, the server may apply dynamic reductions based on player population. This is handled by `Creature::ApplyDynamicRespawnDelay`. This function checks several conditions:
    *   The creature must be on a continent (Map ID <= 1).
    *   The creature must be a generic subtype (`CREATURE_SUBTYPE_GENERIC`).
    *   Elite creatures require a specific spawn flag (`SPAWN_FLAG_FORCE_DYNAMIC_ELITE`) to be affected.
    *   The creature's level must be below `DynamicRespawn.AffectLevelBelow`.
    *   The base delay must be below `DynamicRespawn.AffectRespawnTimeBelow`.
    *   The calculated delay must be above `DynamicRespawn.MinRespawnTime`.

    If these conditions are met, the server counts players within `DynamicRespawn.Range`. If the count exceeds `DynamicRespawn.PlayersThreshold`, the delay is reduced. The reduction formula is:
    `Reduction = (PlayerCount * DynamicRespawn.PercentPerPlayer / 100.0) * OriginalDelay`
    This reduction is capped by `DynamicRespawn.MaxReductionRate`. The final delay is further clamped by `DynamicRespawn.MinRespawnTime` (and potentially `DynamicRespawn.MinRespawnTimeIndoors` or `DynamicRespawn.MinRespawnTimeElite` if those config keys were present, though only `MinRespawnTime` is explicitly listed in the provided CONFIG slice). Note: The source code references `CONFIG_UINT32_DYN_RESPAWN_MIN_RESPAWN_TIME_INDOORS` and `CONFIG_UINT32_DYN_RESPAWN_MIN_RESPAWN_TIME_ELITE`, but these keys are **not** present in the provided CONFIG text. Therefore, only `DynamicRespawn.MinRespawnTime` is guaranteed to be configurable via the provided template.

3.  **Persistence and Saving**: Once the delay is determined, the absolute respawn time (`m_respawnTime`) is set. The server saves this state to ensure it survives restarts. The timing of this save is controlled by `SaveRespawnTimeImmediately`. If enabled (default 1), `Creature::SaveRespawnTime` writes the respawn time to the persistent state immediately upon death. Otherwise, it may be saved during periodic intervals. The data is stored in the map's persistent state, not directly back into the `creature` table's `spawntimesecs` columns (which remain static).

4.  **Respawning**: When the server's update loop detects that `m_respawnTime` has passed, `Creature::Respawn` is called. This removes the corpse, resets the creature's state, and triggers `Map::CreatureRespawnRelocation` to move the creature object to its home coordinates (`GetRespawnCoord`). If the creature is part of a group (`CreatureGroup`), `Map::LoadCreatureSpawnWithGroup` ensures linked creatures respawn together if configured.

5.  **Manual Intervention**: Game Masters can override these times using commands like `HandleNpcSpawnSetRespawnTimeCommand`, which directly updates the `spawntimesecsmin` and `spawntimesecsmax` columns in the `creature` table and sets the immediate respawn delay.

## How to Modify

### Config

The following keys in `mangosd.conf` control the dynamic adjustment of respawn times. By default, dynamic respawn is disabled (`DynamicRespawn.Range = -1`).

*   **`DynamicRespawn.Range`** (default `-1`): The radius in yards around a dead creature to search for players. Set to `-1` to disable dynamic respawn entirely. Set to a positive integer (e.g., `100`) to enable.
*   **`DynamicRespawn.PercentPerPlayer`** (default `0`): The percentage of the original respawn time to reduce for each player found within the range. For example, if set to `10`, each player reduces the respawn time by 10%.
*   **`DynamicRespawn.MaxReductionRate`** (default `0`): The maximum percentage the respawn time can be reduced, regardless of player count. For example, `50` means the respawn time can never be reduced by more than half.
*   **`DynamicRespawn.MinRespawnTime`** (default `0`): The absolute minimum respawn time in seconds. Even with many players, the respawn time will not drop below this value.
*   **`DynamicRespawn.AffectRespawnTimeBelow`** (default `0`): Dynamic respawn only applies to creatures whose base respawn time is *below* this value (in seconds). Set to `0` to affect all creatures.
*   **`DynamicRespawn.AffectLevelBelow`** (default `0`): Dynamic respawn only applies to creatures whose level is *below* this value. Set to `0` to affect all levels.
*   **`DynamicRespawn.PlayersThreshold`** (default `0`): The minimum number of players required within the range before any reduction is applied.
*   **`DynamicRespawn.PlayersMaxLevelDiff`** (default `0`): Only players within this level difference of the creature are counted. Set to `0` to ignore level differences.
*   **`SaveRespawnTimeImmediately`** (default `1`): If `1`, the server saves the remaining respawn time to disk immediately when a creature dies. If `0`, it waits for the next periodic save. Setting this to `1` prevents loss of respawn progress during unexpected server crashes.

### Database

The primary source of truth for respawn times is the `creature` table in the `mangos` database.

*   **`creature.spawntimesecsmin`**: The minimum respawn time in seconds.
*   **`creature.spawntimesecsmax`**: The maximum respawn time in seconds.

To change the respawn time for a specific creature spawn (identified by `guid`), update these columns directly:

```sql
UPDATE `creature` 
SET `spawntimesecsmin` = 300, `spawntimesecsmax` = 300 
WHERE `guid` = 12345;
```

To change the respawn time for all spawns of a specific creature type (identified by `id`), update all matching rows:

```sql
UPDATE `creature` 
SET `spawntimesecsmin` = 600, `spawntimesecsmax` = 600 
WHERE `id` = 12345;
```

Note: Changing these values requires a server restart or a creature reload (`reload creature`) to take effect for existing spawns. Newly spawned creatures will use the new values immediately.

### Code

If you need to modify the logic of how respawn times are calculated, saved, or applied, you must edit the C++ source code and rebuild the server.

*   **`Creature::ApplyDynamicRespawnDelay`** (`Creature.cpp`): Edit this method to change the algorithm for dynamic respawn reduction. For example, you could remove the check for `DynamicRespawn.AffectLevelBelow` or change the formula for calculating the reduction.
*   **`Creature::SaveRespawnTime`** (`Creature.cpp`): Edit this method to change how and when respawn times are persisted. For example, you could force all respawn times to be saved immediately regardless of the `SaveRespawnTimeImmediately` config.
*   **`Map::LoadCreatureSpawn`** (`Map.cpp`): Edit this method to change how respawn times are initialized when a creature is loaded from the database. For example, you could apply a global multiplier to all respawn times upon loading.
*   **`Creature::Respawn`** (`Creature.cpp`): Edit this method to change what happens when a creature actually respawns. For example, you could add a custom event or sound effect.

## Path Reference

**BattleGroundMgr/LoadBattleEventIndexes**
Unit: BattleGroundMgr.cpp
Role: Queries the `creature_battleground` table to load event associations, indirectly involving creature GUIDs but not directly managing respawn times.

**ChatHandler.CreatureCommands/HandleNpcSpawnSetEntryCommand**
Unit: CreatureCommands.cpp
Role: Allows GMs to change a creature's template entry, which may indirectly affect respawn times if the new template has different default values, but does not directly modify respawn time columns.

**ChatHandler.CreatureCommands/HandleNpcSpawnWanderDistCommand**
Unit: CreatureCommands.cpp
Role: Updates the `wander_distance` column in the `creature` table, unrelated to respawn times.

**ChatHandler.CreatureCommands/HandleNpcSpawnSetRespawnTimeCommand**
Unit: CreatureCommands.cpp
Role: Directly updates the `spawntimesecsmin` and `spawntimesecsmax` columns in the `creature` table and sets the immediate respawn delay for a specific creature GUID.

**ChatHandler.CreatureCommands/HandleNpcAddEntryCommand**
Unit: CreatureCommands.cpp
Role: Adds additional template IDs to a multi-ID spawn, unrelated to respawn times.

**ChatHandler.CreatureCommands/HandleNpcMoveHelperCommand**
Unit: CreatureCommands.cpp
Role: Moves a creature and optionally respawns it if dead, triggering the respawn logic but not modifying the underlying respawn time configuration.

**ChatHandler.CreatureCommands/HandleNpcSpawnSetMoveTypeCommand**
Unit: CreatureCommands.cpp
Role: Updates the `movement_type` column in the `creature` table, unrelated to respawn times.

**ChatHandler.CreatureCommands/HandleWpAddCommand**
Unit: CreatureCommands.cpp
Role: Adds waypoints to a creature's path, unrelated to respawn times.

**ChatHandler.CreatureCommands/HandleEscortHideWpCommand**
Unit: CreatureCommands.cpp
Role: Removes visual waypoint markers, unrelated to respawn times.

**ChatHandler.LookupCommands/HandleListCreatureCommand**
Unit: LookupCommands.cpp
Role: Lists creatures by entry ID, querying the `creature` table but not modifying respawn times.

**ChatHandler.TeleportCommands/HandleGoCreatureCommand**
Unit: TeleportCommands.cpp
Role: Teleports a GM to a creature, unrelated to respawn times.

**Creature.Main/SetAInitializeOnRespawn**
Unit: Creature.h
Role: Sets a flag to reinitialize the creature's AI upon respawn, affecting behavior after respawn but not the timing itself.

**Creature.Main/GetRespawnTime**
Unit: Creature.h
Role: Returns the absolute Unix timestamp (`m_respawnTime`) when the creature is scheduled to respawn.

**Creature.Main/SetRespawnTime**
Unit: Creature.h
Role: Sets the absolute Unix timestamp (`m_respawnTime`) for the creature's respawn, calculated from the current time plus the delay.

**Creature.Main/GetRespawnDelay**
Unit: Creature.h
Role: Returns the base respawn delay in seconds (`m_respawnDelay`), typically derived from the database values.

**Creature.Main/SetRespawnDelay**
Unit: Creature.h
Role: Sets the base respawn delay in seconds (`m_respawnDelay`), which is then used to calculate the absolute respawn time.

**Creature.Main/SaveToDB#2**
Unit: Creature.cpp
Role: Saves the creature's current state, including `spawntimesecsmin` and `spawntimesecsmax` (derived from `m_respawnDelay`), back to the `creature` table.

**Creature.Main/CreatureRespawnDeleteWorker**
Unit: Creature.cpp
Role: A helper functor used to clean up persistent respawn state data when a creature is deleted from the database.

**Creature.Main/DeleteFromDB#2**
Unit: Creature.cpp
Role: Removes the creature from the `creature` table and associated tables, clearing any persistent respawn state.

**Creature.Main/Respawn**
Unit: Creature.cpp
Role: Executes the respawn process, removing the corpse, resetting the creature's state, and scheduling the next respawn if applicable.

**Creature.Main/DynamicRespawnRatesChecker**
Unit: Creature.cpp
Role: A helper functor used to count nearby players and check for escorts when applying dynamic respawn delays.

**Creature.Main/ApplyDynamicRespawnDelay**
Unit: Creature.cpp
Role: Calculates and applies dynamic reductions to the respawn delay based on player population and configuration settings.

**Creature.Main/SaveRespawnTime**
Unit: Creature.cpp
Role: Persists the current respawn time to the map's persistent state, ensuring it survives server restarts.

**Creature.Main/GetRespawnTimeEx**
Unit: Creature.cpp
Role: Returns the effective respawn time, accounting for corpse decay timers if the creature is dead but not yet despawned.

**Creature.Main/GetRespawnCoord**
Unit: Creature.cpp
Role: Retrieves the coordinates where the creature will respawn, used by the relocation logic.

**CreatureLinkingMgr/IsLinkingEntryValid**
Unit: CreatureLinkingMgr.cpp
Role: Validates creature linking entries, which can include respawn dependencies, but does not directly manage respawn times.

**GameEventMgr.Main/LoadFromDB**
Unit: GameEventMgr.cpp
Role: Loads game event data, including creature associations, which can temporarily disable spawns but does not modify base respawn times.

**Map.Main/CreatureRespawnRelocation**
Unit: Map.cpp
Role: Moves a respawning creature to its designated coordinates and reinitializes its movement.

**Map.Main/LoadCreatureSpawn**
Unit: Map.cpp
Role: Loads a creature from the database into the world, initializing its respawn time and delay based on database values and configuration.

**Map.Main/LoadCreatureSpawnWithGroup**
Unit: Map.cpp
Role: Loads a creature and its linked group members, ensuring they respawn together if configured.

**Map.ScriptCommands/ScriptCommand_RespawnGameObject**
Unit: ScriptCommands.cpp
Role: Respawns a GameObject, unrelated to creature respawn times.

**Map.ScriptCommands/ScriptCommand_RespawnCreature**
Unit: ScriptCommands.cpp
Role: Triggers the immediate respawn of a dead creature via script, bypassing the normal delay.

**Map.ScriptCommands/ScriptCommand_LoadCreatureSpawn**
Unit: ScriptCommands.cpp
Role: Loads a creature spawn from the database via script, initializing its respawn time.

**ObjectMgr/LoadAllIdentifiers**
Unit: ObjectMgr.cpp
Role: Loads unique identifiers for various entities, including creatures, but does not handle respawn times.

**ObjectMgr/LoadCreatures**
Unit: ObjectMgr.cpp
Role: Loads all creature spawn data from the `creature` table, including `spawntimesecsmin` and `spawntimesecsmax`, and validates/adjusts these values.

**ObjectMgr/SetHighestGuids**
Unit: ObjectMgr.cpp
Role: Initializes GUID ranges, unrelated to respawn times.

**PoolManager/LoadFromDB**
Unit: PoolManager.cpp
Role: Loads pool data, which can control which creatures are active, but does not modify their individual respawn times.

**WaypointManager/Load**
Unit: WaypointManager.cpp
Role: Loads waypoint paths, unrelated to respawn times.

**World/LoadConfigSettings**
Unit: World.cpp
Role: Reads configuration settings, including the `DynamicRespawn.*` keys, which influence respawn time calculations.

**GridMap/IsOutdoors**
Unit: GridMap.cpp
Role: Determines if a location is outdoors, used by `ApplyDynamicRespawnDelay` to potentially apply different minimum respawn times for indoor areas (though the specific config key for indoor minimums is not present in the provided CONFIG slice).

---

<!-- machine-true, projected from graph.json -->

## Map — Creature Respawn Times

*Source:* BattleGroundMgr.cpp, CreatureCommands.cpp, LookupCommands.cpp, TeleportCommands.cpp, Creature.h, Creature.cpp, CreatureLinkingMgr.cpp, GameEventMgr.cpp, Map.cpp, ScriptCommands.cpp, ObjectMgr.cpp, PoolManager.cpp, WaypointManager.cpp, World.cpp, GridMap.cpp
*Config keys:* DynamicRespawn.Range (default -1), DynamicRespawn.PercentPerPlayer (default 0), DynamicRespawn.MaxReductionRate (default 0), DynamicRespawn.MinRespawnTime (default 0), DynamicRespawn.AffectRespawnTimeBelow (default 0), DynamicRespawn.AffectLevelBelow (default 0), DynamicRespawn.PlayersThreshold (default 0), DynamicRespawn.PlayersMaxLevelDiff (default 0), SaveRespawnTimeImmediately (default 1)
*Tables:* creature

| Member | Kind | Source | Role |
|---|---|---|---|
| BattleGroundMgr/LoadBattleEventIndexes | method | BattleGroundMgr.cpp:1629-1741 | seed — queries creature |
| ChatHandler.CreatureCommands/HandleNpcSpawnSetEntryCommand | method | CreatureCommands.cpp:188-215 | seed — queries creature |
| ChatHandler.CreatureCommands/HandleNpcSpawnWanderDistCommand | method | CreatureCommands.cpp:666-700 | seed — queries creature |
| ChatHandler.CreatureCommands/HandleNpcSpawnSetRespawnTimeCommand | method | CreatureCommands.cpp:734-765 | seed — queries creature |
| ChatHandler.CreatureCommands/HandleNpcAddEntryCommand | method | CreatureCommands.cpp:1066-1130 | seed — queries creature |
| ChatHandler.CreatureCommands/HandleNpcMoveHelperCommand | method | CreatureCommands.cpp:1256-1317 | seed — queries creature |
| ChatHandler.CreatureCommands/HandleNpcSpawnSetMoveTypeCommand | method | CreatureCommands.cpp:1319-1375 | seed — queries creature |
| ChatHandler.CreatureCommands/HandleWpAddCommand | method | CreatureCommands.cpp:1766-1904 | seed — queries creature |
| ChatHandler.CreatureCommands/HandleEscortHideWpCommand | method | CreatureCommands.cpp:2591-2632 | seed — queries creature |
| ChatHandler.LookupCommands/HandleListCreatureCommand | method | LookupCommands.cpp:99-165 | seed — queries creature |
| ChatHandler.TeleportCommands/HandleGoCreatureCommand | method | TeleportCommands.cpp:370-507 | seed — queries creature |
| Creature.Main/SetAInitializeOnRespawn | method | Creature.h:218-224 | seed — Creature.*/*Respawn* |
| Creature.Main/GetRespawnTime | method | Creature.h:363-363 | seed — Creature.*/*Respawn* |
| Creature.Main/SetRespawnTime | method | Creature.h:365-365 | seed — Creature.*/*Respawn* |
| Creature.Main/GetRespawnDelay | method | Creature.h:372-372 | seed — Creature.*/*Respawn* |
| Creature.Main/SetRespawnDelay | method | Creature.h:373-373 | seed — Creature.*/*Respawn* |
| Creature.Main/SaveToDB#2 | method | Creature.cpp:1604-1675 | seed — queries creature |
| Creature.Main/CreatureRespawnDeleteWorker | ctor | Creature.cpp:2083-2083 | seed — Creature.*/*Respawn* |
| Creature.Main/DeleteFromDB#2 | method | Creature.cpp:2104-2120 | seed — queries creature |
| Creature.Main/Respawn | method | Creature.cpp:2308-2324 | seed — Creature.*/*Respawn* |
| Creature.Main/DynamicRespawnRatesChecker | ctor | Creature.cpp:2606-2610 | seed — Creature.*/*Respawn* |
| Creature.Main/ApplyDynamicRespawnDelay | method | Creature.cpp:2633-2713 | seed — Creature.*/*Respawn* |
| Creature.Main/SaveRespawnTime | method | Creature.cpp:2715-2724 | seed — Creature.*/*Respawn* |
| Creature.Main/GetRespawnTimeEx | method | Creature.cpp:3236-3245 | seed — Creature.*/*Respawn* |
| Creature.Main/GetRespawnCoord | method | Creature.cpp:3247-3284 | seed — Creature.*/*Respawn* |
| CreatureLinkingMgr/IsLinkingEntryValid | method | CreatureLinkingMgr.cpp:169-256 | seed — queries creature |
| GameEventMgr.Main/LoadFromDB | method | GameEventMgr.cpp:165-671 | seed — queries creature |
| Map.Main/CreatureRespawnRelocation | method | Map.cpp:1507-1532 | seed — Map.*/*Respawn* |
| Map.Main/LoadCreatureSpawn | method | Map.cpp:3722-3758 | seed — Map.*/*Respawn* |
| Map.Main/LoadCreatureSpawnWithGroup | method | Map.cpp:3760-3776 | seed — Map.*/*Respawn* |
| Map.ScriptCommands/ScriptCommand_RespawnGameObject | method | ScriptCommands.cpp:357-399 | seed — Map.*/*Respawn* |
| Map.ScriptCommands/ScriptCommand_RespawnCreature | method | ScriptCommands.cpp:2081-2102 | seed — Map.*/*Respawn* |
| Map.ScriptCommands/ScriptCommand_LoadCreatureSpawn | method | ScriptCommands.cpp:2502-2516 | seed — Map.*/*Respawn* |
| ObjectMgr/LoadAllIdentifiers | method | ObjectMgr.cpp:180-328 | seed — queries creature |
| ObjectMgr/LoadCreatures | method | ObjectMgr.cpp:2294-2479 | seed — queries creature |
| ObjectMgr/SetHighestGuids | method | ObjectMgr.cpp:7655-7713 | seed — queries creature |
| PoolManager/LoadFromDB | method | PoolManager.cpp:667-1030 | seed — queries creature |
| WaypointManager/Load | method | WaypointManager.cpp:33-382 | seed — queries creature |
| World/LoadConfigSettings | method | World.cpp:440-1245 | seed — reads config DynamicRespawn.Range |
| GridMap/IsOutdoors | method | GridMap.cpp:879-889 | related — 1 hop from a seed |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `areatrigger_template`: id smallint(4) unsigned PK, build smallint(4) unsigned PK, name varchar(128)?, map_id smallint(3) unsigned, x float, y float, z float, radius float, box_x float, box_y float, box_z float, box_orientation float, cooldown int(10) unsigned, condition_id int(10) unsigned, script_id int(10) unsigned, script_name varchar(64)
- `auction`: id int(11) unsigned PK, house_id int(11) unsigned, item_guid int(11) unsigned, item_id int(11) unsigned, seller_guid int(11) unsigned, buyout_price int(11), expire_time bigint(40), buyer_guid int(11) unsigned, last_bid int(11), start_bid int(11), deposit int(11)
- `battleground_events`: map smallint(5) unsigned PK, event1 tinyint(3) unsigned PK, event2 tinyint(3) unsigned PK, description varchar(255)
- `character_gifts`: guid int(20) unsigned, item_guid int(11) unsigned PK, item_id int(20) unsigned, flags int(20) unsigned
- `character_inventory`: guid int(11) unsigned, bag int(11) unsigned, slot tinyint(3) unsigned, item_guid int(11) unsigned PK, item_id int(11) unsigned
- `conditions`: condition_entry mediumint(8) unsigned PK, type tinyint(3), value1 int(11), value2 int(11), value3 int(11), value4 int(11), flags tinyint(3) unsigned
- `creature`: guid int(10) unsigned PK, id mediumint(8) unsigned, id2 mediumint(8) unsigned, id3 mediumint(8) unsigned, id4 mediumint(8) unsigned, id5 mediumint(8) unsigned, map smallint(5) unsigned, position_x float, position_y float, position_z float, orientation float, spawntimesecsmin int(10) unsigned, spawntimesecsmax int(10) unsigned, wander_distance float, health_percent float, mana_percent float unsigned, movement_type tinyint(3) unsigned, spawn_flags int(10) unsigned, visibility_mod float?, patch_min tinyint(3) unsigned, patch_max tinyint(3) unsigned
- `creature_addon`: guid int(10) unsigned PK, patch tinyint(3) unsigned PK, display_id smallint(5) unsigned, mount_display_id smallint(6), equipment_id int(11), stand_state tinyint(3) unsigned, sheath_state tinyint(3) unsigned, emote_state smallint(5) unsigned, auras text?
- `creature_battleground`: guid int(10) unsigned PK, event1 tinyint(3) unsigned PK, event2 tinyint(3) unsigned
- `creature_movement`: id int(10) unsigned PK, point mediumint(8) unsigned PK, position_x float, position_y float, position_z float, orientation float, waittime int(10) unsigned, wander_distance float unsigned, script_id mediumint(8) unsigned, path_id int(10) unsigned
- `creature_movement_special`: id int(10) unsigned PK, point mediumint(8) unsigned PK, position_x float, position_y float, position_z float, orientation float, waittime int(10) unsigned, wander_distance float unsigned, script_id mediumint(8) unsigned, path_id int(10) unsigned
- `creature_movement_template`: entry mediumint(8) unsigned PK, point mediumint(8) unsigned PK, position_x float, position_y float, position_z float, orientation float, waittime int(10) unsigned, wander_distance float unsigned, script_id mediumint(8) unsigned, path_id int(10) unsigned
- `creature_spells`: entry int(11) unsigned PK, name varchar(255), spellId_1 smallint(5) unsigned, probability_1 tinyint(3) unsigned, castTarget_1 tinyint(2) unsigned, targetParam1_1 smallint(5) unsigned, targetParam2_1 smallint(5) unsigned, castFlags_1 smallint(5) unsigned, delayInitialMin_1 smallint(5) unsigned, delayInitialMax_1 smallint(5) unsigned, delayRepeatMin_1 smallint(5) unsigned, delayRepeatMax_1 smallint(5) unsigned, scriptId_1 mediumint(8) unsigned, spellId_2 smallint(5) unsigned, probability_2 tinyint(3) unsigned, castTarget_2 tinyint(2) unsigned, targetParam1_2 smallint(5) unsigned, targetParam2_2 smallint(5) unsigned, castFlags_2 smallint(5) unsigned, delayInitialMin_2 smallint(5) unsigned, delayInitialMax_2 smallint(5) unsigned, delayRepeatMin_2 smallint(5) unsigned, delayRepeatMax_2 smallint(5) unsigned, scriptId_2 mediumint(8) unsigned, spellId_3 smallint(5) unsigned, probability_3 tinyint(3) unsigned, castTarget_3 tinyint(2) unsigned, targetParam1_3 smallint(5) unsigned, targetParam2_3 smallint(5) unsigned, castFlags_3 smallint(5) unsigned, delayInitialMin_3 smallint(5) unsigned, delayInitialMax_3 smallint(5) unsigned, delayRepeatMin_3 smallint(5) unsigned, delayRepeatMax_3 smallint(5) unsigned, scriptId_3 mediumint(8) unsigned, spellId_4 smallint(5) unsigned, probability_4 tinyint(3) unsigned, castTarget_4 tinyint(2) unsigned, targetParam1_4 smallint(5) unsigned, targetParam2_4 smallint(5) unsigned, castFlags_4 smallint(5) unsigned, delayInitialMin_4 smallint(5) unsigned, delayInitialMax_4 smallint(5) unsigned, delayRepeatMin_4 smallint(5) unsigned, delayRepeatMax_4 smallint(5) unsigned, scriptId_4 mediumint(8) unsigned, spellId_5 smallint(5) unsigned, probability_5 tinyint(3) unsigned, castTarget_5 tinyint(2) unsigned, targetParam1_5 smallint(5) unsigned, targetParam2_5 smallint(5) unsigned, castFlags_5 smallint(5) unsigned, delayInitialMin_5 smallint(5) unsigned, delayInitialMax_5 smallint(5) unsigned, delayRepeatMin_5 smallint(5) unsigned, delayRepeatMax_5 smallint(5) unsigned, scriptId_5 mediumint(8) unsigned, spellId_6 smallint(5) unsigned, probability_6 tinyint(3) unsigned, castTarget_6 tinyint(2) unsigned, targetParam1_6 smallint(5) unsigned, targetParam2_6 smallint(5) unsigned, castFlags_6 smallint(5) unsigned, delayInitialMin_6 smallint(5) unsigned, delayInitialMax_6 smallint(5) unsigned, delayRepeatMin_6 smallint(5) unsigned, delayRepeatMax_6 smallint(5) unsigned, scriptId_6 mediumint(8) unsigned, spellId_7 smallint(5) unsigned, probability_7 tinyint(3) unsigned, castTarget_7 tinyint(2) unsigned, targetParam1_7 smallint(5) unsigned, targetParam2_7 smallint(5) unsigned, castFlags_7 smallint(5) unsigned, delayInitialMin_7 smallint(5) unsigned, delayInitialMax_7 smallint(5) unsigned, delayRepeatMin_7 smallint(5) unsigned, delayRepeatMax_7 smallint(5) unsigned, scriptId_7 mediumint(8) unsigned, spellId_8 smallint(5) unsigned, probability_8 tinyint(3) unsigned, castTarget_8 tinyint(2) unsigned, targetParam1_8 smallint(5) unsigned, targetParam2_8 smallint(5) unsigned, castFlags_8 smallint(5) unsigned, delayInitialMin_8 smallint(5) unsigned, delayInitialMax_8 smallint(5) unsigned, delayRepeatMin_8 smallint(5) unsigned, delayRepeatMax_8 smallint(5) unsigned, scriptId_8 mediumint(8) unsigned
- `creature_template`: entry mediumint(8) unsigned PK, patch tinyint(3) unsigned PK, name char(100), subname char(100)?, level_min tinyint(3) unsigned, level_max tinyint(3) unsigned, faction smallint(5) unsigned, npc_flags int(10) unsigned, gossip_menu_id mediumint(8) unsigned, display_id1 mediumint(8) unsigned, display_id2 mediumint(8) unsigned, display_id3 mediumint(8) unsigned, display_id4 mediumint(8) unsigned, display_scale1 float, display_scale2 float, display_scale3 float, display_scale4 float, display_probability1 smallint(5) unsigned, display_probability2 smallint(5) unsigned, display_probability3 smallint(5) unsigned, display_probability4 smallint(5) unsigned, display_total_probability smallint(5) unsigned, mount_display_id smallint(5) unsigned, speed_walk float, speed_run float, detection_range float, call_for_help_range float, leash_range float, type tinyint(3) unsigned, pet_family tinyint(4) unsigned, rank tinyint(3) unsigned, unit_class tinyint(3) unsigned, xp_multiplier float, health_multiplier float, mana_multiplier float, armor_multiplier float, damage_multiplier float, damage_variance float, damage_school tinyint(4) unsigned, base_attack_time int(10) unsigned, ranged_attack_time int(10) unsigned, holy_res smallint(5), fire_res smallint(5), nature_res smallint(5), frost_res smallint(5), shadow_res smallint(5), arcane_res smallint(5), trainer_type tinyint(4) unsigned, trainer_spell smallint(5) unsigned, trainer_class tinyint(3) unsigned, trainer_race tinyint(3) unsigned, loot_id mediumint(8) unsigned, pickpocket_loot_id mediumint(8) unsigned, skinning_loot_id mediumint(8) unsigned, gold_min mediumint(8) unsigned, gold_max mediumint(8) unsigned, spell_id1 smallint(5) unsigned, spell_id2 smallint(5) unsigned, spell_id3 smallint(5) unsigned, spell_id4 smallint(5) unsigned, spell_list_id int(11) unsigned, pet_spell_list_id mediumint(8) unsigned, spawn_spell_id smallint(5) unsigned, auras text?, ai_name char(64), movement_type tinyint(3) unsigned, inhabit_type tinyint(3) unsigned, civilian tinyint(3) unsigned, racial_leader tinyint(3) unsigned, equipment_id mediumint(8) unsigned, trainer_id mediumint(8) unsigned, vendor_id mediumint(8) unsigned, mechanic_immune_mask int(10) unsigned, school_immune_mask int(10) unsigned, immunity_flags int(10) unsigned, static_flags1 int(10) unsigned, static_flags2 int(10) unsigned, flags_extra int(10) unsigned, script_name char(64)
- `game_event`: entry mediumint(8) unsigned PK, start_time timestamp, end_time timestamp, occurence bigint(20) unsigned, length bigint(20) unsigned, holiday mediumint(8) unsigned, description varchar(255)?, hardcoded tinyint(3), disabled tinyint(3), patch_min tinyint(3) unsigned, patch_max tinyint(3) unsigned
- `game_event_creature`: guid int(10) unsigned PK, event smallint(6) PK
- `game_event_creature_data`: guid int(10) unsigned PK, patch tinyint(3) unsigned PK, entry_id mediumint(8) unsigned, display_id mediumint(8) unsigned, equipment_id mediumint(8) unsigned, spell_start smallint(5) unsigned, spell_end smallint(5) unsigned, event smallint(5) unsigned PK
- `game_event_gameobject`: guid int(10) unsigned PK, event smallint(6) PK
- `game_event_mail`: event smallint(6) PK, raceMask mediumint(8) unsigned PK, quest mediumint(8) unsigned PK, mailTemplateId mediumint(8) unsigned, senderEntry mediumint(8) unsigned
- `game_event_quest`: quest mediumint(8) unsigned PK, event smallint(5) unsigned PK, patch_min tinyint(3) unsigned
- `gameobject`: guid int(10) unsigned PK, id mediumint(8) unsigned, map smallint(5) unsigned, position_x float, position_y float, position_z float, orientation float, rotation0 float, rotation1 float, rotation2 float, rotation3 float, spawntimesecsmin int(11), spawntimesecsmax int(11), animprogress tinyint(3) unsigned, state tinyint(3) unsigned, spawn_flags int(10) unsigned, visibility_mod float?, patch_min tinyint(3) unsigned, patch_max tinyint(3) unsigned
- `gameobject_battleground`: guid int(10) unsigned PK, event1 tinyint(3) unsigned PK, event2 tinyint(3) unsigned PK
- `gameobject_template`: entry mediumint(8) unsigned PK, patch tinyint(3) unsigned PK, type tinyint(3) unsigned, displayId mediumint(8) unsigned, name varchar(100), icon varchar(100), faction smallint(5) unsigned, flags int(10) unsigned, size float, data0 int(10), data1 int(11), data2 int(10), data3 int(10), data4 int(10), data5 int(10), data6 int(11), data7 int(10), data8 int(10), data9 int(10), data10 int(10), data11 int(10), data12 int(10), data13 int(10), data14 int(10), data15 int(10), data16 int(10), data17 int(10), data18 int(10), data19 int(10), data20 int(10), data21 int(10), data22 int(10), data23 int(10), mingold mediumint(8) unsigned, maxgold mediumint(8) unsigned, script_name varchar(64)
- `gossip_menu`: entry smallint(6) unsigned PK, text_id mediumint(8) unsigned PK, script_id mediumint(8) unsigned, condition_id mediumint(8) unsigned
- `groups`: group_id int(11) unsigned PK, leader_guid int(11) unsigned, main_tank_guid int(11) unsigned, main_assistant_guid int(11) unsigned, loot_method tinyint(4) unsigned, loot_threshold tinyint(4) unsigned, looter_guid int(11) unsigned, icon1 int(11) unsigned, icon2 int(11) unsigned, icon3 int(11) unsigned, icon4 int(11) unsigned, icon5 int(11) unsigned, icon6 int(11) unsigned, icon7 int(11) unsigned, icon8 int(11) unsigned, is_raid tinyint(1) unsigned
- `guild`: guild_id int(6) unsigned PK, name varchar(255), leader_guid int(6) unsigned, emblem_style int(5), emblem_color int(5), border_style int(5), border_color int(5), background_color int(5), info text, motd varchar(255), create_date bigint(20)
- `item_loot`: guid int(11) unsigned PK, owner_guid int(11) unsigned, item_id int(11) unsigned PK, amount int(11) unsigned, property int(11)
- `item_template`: entry mediumint(8) unsigned PK, patch tinyint(3) unsigned PK, class tinyint(3) unsigned, subclass tinyint(3) unsigned, name varchar(255), description varchar(255), display_id mediumint(8) unsigned, quality tinyint(3) unsigned, flags int(10) unsigned, buy_count tinyint(3) unsigned, buy_price int(10) unsigned, sell_price int(10) unsigned, inventory_type tinyint(3) unsigned, allowable_class mediumint(9), allowable_race mediumint(9), item_level tinyint(3) unsigned, required_level tinyint(3) unsigned, required_skill smallint(5) unsigned, required_skill_rank smallint(5) unsigned, required_spell smallint(5) unsigned, required_honor_rank mediumint(8) unsigned, required_city_rank mediumint(8) unsigned, required_reputation_faction smallint(5) unsigned, required_reputation_rank smallint(5) unsigned, max_count smallint(5) unsigned, stackable smallint(5) unsigned, container_slots tinyint(3) unsigned, stat_type1 tinyint(3) unsigned, stat_value1 smallint(6), stat_type2 tinyint(3) unsigned, stat_value2 smallint(6), stat_type3 tinyint(3) unsigned, stat_value3 smallint(6), stat_type4 tinyint(3) unsigned, stat_value4 smallint(6), stat_type5 tinyint(3) unsigned, stat_value5 smallint(6), stat_type6 tinyint(3) unsigned, stat_value6 smallint(6), stat_type7 tinyint(3) unsigned, stat_value7 smallint(6), stat_type8 tinyint(3) unsigned, stat_value8 smallint(6), stat_type9 tinyint(3) unsigned, stat_value9 smallint(6), stat_type10 tinyint(3) unsigned, stat_value10 smallint(6), delay smallint(5) unsigned, range_mod float, ammo_type tinyint(3) unsigned, dmg_min1 float, dmg_max1 float, dmg_type1 tinyint(3) unsigned, dmg_min2 float, dmg_max2 float, dmg_type2 tinyint(3) unsigned, dmg_min3 float, dmg_max3 float, dmg_type3 tinyint(3) unsigned, dmg_min4 float, dmg_max4 float, dmg_type4 tinyint(3) unsigned, dmg_min5 float, dmg_max5 float, dmg_type5 tinyint(3) unsigned, block mediumint(8) unsigned, armor smallint(5), holy_res smallint(5), fire_res smallint(5), nature_res smallint(5), frost_res smallint(5), shadow_res smallint(5), arcane_res smallint(5), spellid_1 smallint(5) unsigned, spelltrigger_1 tinyint(3) unsigned, spellcharges_1 tinyint(4), spellppmrate_1 float, spellcooldown_1 int(11), spellcategory_1 smallint(5) unsigned, spellcategorycooldown_1 int(11), spellid_2 smallint(5) unsigned, spelltrigger_2 tinyint(3) unsigned, spellcharges_2 tinyint(4), spellppmrate_2 float, spellcooldown_2 int(11), spellcategory_2 smallint(5) unsigned, spellcategorycooldown_2 int(11), spellid_3 smallint(5) unsigned, spelltrigger_3 tinyint(3) unsigned, spellcharges_3 tinyint(4), spellppmrate_3 float, spellcooldown_3 int(11), spellcategory_3 smallint(5) unsigned, spellcategorycooldown_3 int(11), spellid_4 smallint(5) unsigned, spelltrigger_4 tinyint(3) unsigned, spellcharges_4 tinyint(4), spellppmrate_4 float, spellcooldown_4 int(11), spellcategory_4 smallint(5) unsigned, spellcategorycooldown_4 int(11), spellid_5 smallint(5) unsigned, spelltrigger_5 tinyint(3) unsigned, spellcharges_5 tinyint(4), spellppmrate_5 float, spellcooldown_5 int(11), spellcategory_5 smallint(5) unsigned, spellcategorycooldown_5 int(11), bonding tinyint(3) unsigned, page_text mediumint(8) unsigned, page_language tinyint(3) unsigned, page_material tinyint(3) unsigned, start_quest mediumint(8) unsigned, lock_id mediumint(8) unsigned, material tinyint(4), sheath tinyint(3) unsigned, random_property mediumint(8) unsigned, set_id mediumint(8) unsigned, max_durability smallint(5) unsigned, area_bound mediumint(8) unsigned, map_bound smallint(6), duration int(11) unsigned, bag_family mediumint(9), disenchant_id mediumint(8) unsigned, food_type tinyint(3) unsigned, min_money_loot int(10) unsigned, max_money_loot int(10) unsigned, wrapped_gift mediumint(8) unsigned, extra_flags tinyint(1) unsigned, other_team_entry int(11) unsigned?
- `mail`: id int(11) unsigned PK, message_type tinyint(3) unsigned, stationery tinyint(3), mail_template_id mediumint(8) unsigned, sender_guid int(11) unsigned, receiver_guid int(11) unsigned, subject longtext?, item_text_id int(11) unsigned, has_items tinyint(3) unsigned, expire_time bigint(40), deliver_time bigint(40), money int(11) unsigned, cod int(11) unsigned, checked tinyint(3) unsigned
- `mail_items`: mail_id int(11) unsigned PK, item_guid int(11) unsigned PK, item_id int(11) unsigned, receiver_guid int(11) unsigned
- `npc_vendor_template`: entry mediumint(8) unsigned PK, slot smallint(5) unsigned, item mediumint(8) unsigned PK, maxcount tinyint(3) unsigned, incrtime int(10) unsigned, itemflags int(10) unsigned, condition_id mediumint(8) unsigned
- `petition`: owner_guid int(10) unsigned PK, petition_guid int(10) unsigned?, charter_guid int(10) unsigned?, name varchar(255)
- `pool_creature`: guid int(10) unsigned PK, pool_entry smallint(5) unsigned, chance float unsigned, description varchar(255), flags int(10) unsigned, patch_min tinyint(3) unsigned, patch_max tinyint(3) unsigned
- `pool_creature_template`: id int(10) unsigned PK, pool_entry smallint(5) unsigned, chance float unsigned, description varchar(255), flags int(10) unsigned, patch_min tinyint(3) unsigned, patch_max tinyint(3) unsigned
- `pool_gameobject`: guid int(10) unsigned PK, pool_entry smallint(5) unsigned, chance float unsigned, description varchar(255), flags int(10) unsigned, patch_min tinyint(3) unsigned, patch_max tinyint(3) unsigned
- `pool_gameobject_template`: id int(10) unsigned PK, pool_entry smallint(5) unsigned, chance float unsigned, description varchar(255), flags int(10) unsigned, patch_min tinyint(3) unsigned, patch_max tinyint(3) unsigned
- `pool_pool`: pool_id smallint(5) unsigned PK, mother_pool smallint(5) unsigned, chance float, description varchar(255), flags int(10) unsigned
- `pool_template`: entry smallint(5) unsigned PK, max_limit int(10) unsigned, description varchar(255), flags int(11) unsigned, instance mediumint(8), patch_min tinyint(3) unsigned PK, patch_max tinyint(3) unsigned PK
- `quest_template`: entry mediumint(8) unsigned PK, patch tinyint(3) unsigned PK, Method tinyint(3) unsigned, ZoneOrSort smallint(6), MinLevel tinyint(3) unsigned, MaxLevel tinyint(3) unsigned, QuestLevel tinyint(3) unsigned, Type smallint(5) unsigned, RequiredClasses smallint(5) unsigned, RequiredRaces smallint(5) unsigned, RequiredSkill smallint(5) unsigned, RequiredSkillValue smallint(5) unsigned, RequiredCondition mediumint(8) unsigned, RepObjectiveFaction smallint(5) unsigned, RepObjectiveValue mediumint(9), RequiredMinRepFaction smallint(5) unsigned, RequiredMinRepValue mediumint(9), RequiredMaxRepFaction smallint(5) unsigned, RequiredMaxRepValue mediumint(9), SuggestedPlayers tinyint(3) unsigned, LimitTime int(10) unsigned, QuestFlags smallint(5) unsigned, SpecialFlags tinyint(3) unsigned, PrevQuestId mediumint(9), NextQuestId mediumint(9), ExclusiveGroup mediumint(9), BreadcrumbForQuestId mediumint(9) unsigned, NextQuestInChain mediumint(8) unsigned, SrcItemId mediumint(8) unsigned, SrcItemCount tinyint(3) unsigned, SrcSpell smallint(5) unsigned, Title text?, Details text?, Objectives text?, OfferRewardText text?, RequestItemsText text?, EndText text?, ObjectiveText1 text?, ObjectiveText2 text?, ObjectiveText3 text?, ObjectiveText4 text?, ReqItemId1 mediumint(8) unsigned, ReqItemId2 mediumint(8) unsigned, ReqItemId3 mediumint(8) unsigned, ReqItemId4 mediumint(8) unsigned, ReqItemCount1 smallint(5) unsigned, ReqItemCount2 smallint(5) unsigned, ReqItemCount3 smallint(5) unsigned, ReqItemCount4 smallint(5) unsigned, ReqSourceId1 mediumint(8) unsigned, ReqSourceId2 mediumint(8) unsigned, ReqSourceId3 mediumint(8) unsigned, ReqSourceId4 mediumint(8) unsigned, ReqSourceCount1 mediumint(8) unsigned, ReqSourceCount2 mediumint(8) unsigned, ReqSourceCount3 mediumint(8) unsigned, ReqSourceCount4 mediumint(8) unsigned, ReqCreatureOrGOId1 mediumint(9), ReqCreatureOrGOId2 mediumint(9), ReqCreatureOrGOId3 mediumint(9), ReqCreatureOrGOId4 mediumint(9), ReqCreatureOrGOCount1 smallint(5) unsigned, ReqCreatureOrGOCount2 smallint(5) unsigned, ReqCreatureOrGOCount3 smallint(5) unsigned, ReqCreatureOrGOCount4 smallint(5) unsigned, ReqSpellCast1 smallint(5) unsigned, ReqSpellCast2 smallint(5) unsigned, ReqSpellCast3 smallint(5) unsigned, ReqSpellCast4 smallint(5) unsigned, RewChoiceItemId1 mediumint(8) unsigned, RewChoiceItemId2 mediumint(8) unsigned, RewChoiceItemId3 mediumint(8) unsigned, RewChoiceItemId4 mediumint(8) unsigned, RewChoiceItemId5 mediumint(8) unsigned, RewChoiceItemId6 mediumint(8) unsigned, RewChoiceItemCount1 smallint(5) unsigned, RewChoiceItemCount2 smallint(5) unsigned, RewChoiceItemCount3 smallint(5) unsigned, RewChoiceItemCount4 smallint(5) unsigned, RewChoiceItemCount5 smallint(5) unsigned, RewChoiceItemCount6 smallint(5) unsigned, RewItemId1 mediumint(8) unsigned, RewItemId2 mediumint(8) unsigned, RewItemId3 mediumint(8) unsigned, RewItemId4 mediumint(8) unsigned, RewItemCount1 smallint(5) unsigned, RewItemCount2 smallint(5) unsigned, RewItemCount3 smallint(5) unsigned, RewItemCount4 smallint(5) unsigned, RewRepFaction1 smallint(5) unsigned, RewRepFaction2 smallint(5) unsigned, RewRepFaction3 smallint(5) unsigned, RewRepFaction4 smallint(5) unsigned, RewRepFaction5 smallint(5) unsigned, RewRepValue1 mediumint(9), RewRepValue2 mediumint(9), RewRepValue3 mediumint(9), RewRepValue4 mediumint(9), RewRepValue5 mediumint(9), RewRepSpilloverMask tinyint(3) unsigned, RewXP mediumint(9) unsigned, RewOrReqMoney int(11), RewMoneyMaxLevel int(10) unsigned, RewSpell smallint(5) unsigned, RewSpellCast smallint(5) unsigned, RewMailTemplateId mediumint(8), RewMailDelaySecs int(11) unsigned, RewMailMoney int(10) unsigned, PointMapId smallint(5) unsigned, PointX float, PointY float, PointOpt mediumint(8) unsigned, DetailsEmote1 smallint(5) unsigned, DetailsEmote2 smallint(5) unsigned, DetailsEmote3 smallint(5) unsigned, DetailsEmote4 smallint(5) unsigned, DetailsEmoteDelay1 int(11) unsigned, DetailsEmoteDelay2 int(11) unsigned, DetailsEmoteDelay3 int(11) unsigned, DetailsEmoteDelay4 int(11) unsigned, IncompleteEmote smallint(5) unsigned, CompleteEmote smallint(5) unsigned, OfferRewardEmote1 smallint(5) unsigned, OfferRewardEmote2 smallint(5) unsigned, OfferRewardEmote3 smallint(5) unsigned, OfferRewardEmote4 smallint(5) unsigned, OfferRewardEmoteDelay1 int(11) unsigned, OfferRewardEmoteDelay2 int(11) unsigned, OfferRewardEmoteDelay3 int(11) unsigned, OfferRewardEmoteDelay4 int(11) unsigned, StartScript mediumint(8) unsigned, CompleteScript mediumint(8) unsigned

*`?` = nullable, `PK` = primary key column.*

