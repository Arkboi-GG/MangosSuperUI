# CreatureData

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# CreatureDefines.h

**Purpose & Responsibilities**

`CreatureDefines.h` is a foundational definition header for the `wowvmangos` server core. It does not contain executable logic or class implementations; rather, it provides the **data structures**, **enumerations**, and **constants** required to represent non-player characters (creatures) within the game world.

Its primary responsibilities are:
1.  **Defining Creature Metadata:** Providing structs (`CreatureInfo`, `CreatureData`, `CreatureDataAddon`) that mirror database tables (`creature_template`, `creature`, `creature_addon`) to hold static and instance-specific creature properties.
2.  **Establishing Behavioral Flags:** Defining extensive bitmasks (`CreatureStaticFlags`, `CreatureFlagsExtra`, `CreatureImmunityFlags`) that dictate how creatures interact with combat, AI, pathfinding, and player interactions.
3.  **Supporting Randomization Logic:** Implementing lightweight helper methods within `CreatureData` and `EquipmentTemplate` to handle random selection of creature IDs, respawn times, and equipment sets during spawn events.
4.  **Standardizing Constants:** Providing global constants for speeds, ranges, and limits (e.g., `MAX_DISPLAY_IDS_PER_CREATURE`, `DEFAULT_NPC_RUN_SPEED_RATE`) used throughout the engine.

This unit is purely declarative. It relies on `Common.h`, `SharedDefines.h`, `UnitDefines.h`, `ObjectGuid.h`, and `Util.h` for underlying types and utility functions.

---

## Member-by-Member Behavior

The members defined in this unit are grouped by the subsystem they serve. Most are simple data containers or enums. The notable behavioral logic resides in the helper methods of `CreatureData` and `EquipmentTemplate`.

### Creature Identification and Spawning (`CreatureData`)

The `CreatureData` struct represents a specific spawn instance of a creature in the world, corresponding to the `creature` database table. It contains position, spawn timing, and a list of possible creature entries (IDs) that can occupy this spawn point.

*   **`GetObjectGuid`**: Constructs an `ObjectGuid` for the creature. It uses the high GUID type for units (`HIGHGUID_UNIT`), the first creature ID in the spawn list (`creature_id[0]`), and a provided low GUID. This is critical for uniquely identifying the creature instance in memory and network packets.
*   **`GetRandomRespawnTime`**: Calculates a random respawn duration between `spawntimesecsmin` and `spawntimesecsmax`. This is used when a creature dies and needs to schedule its next appearance.
*   **`ChooseCreatureId`**: Selects one creature entry from the `creature_id` array. If multiple IDs are configured for a single spawn point, it picks one randomly. If no valid IDs are found, it defaults to ID `1` (a fallback, likely indicating a configuration error or placeholder).
*   **`HasCreatureId`**: Checks if a specific creature ID exists within the `creature_id` array. This is used to verify if a spawn point supports a particular creature type.
*   **`GetCreatureIdCount`**: Counts how many valid creature IDs are configured for this spawn point. This determines the pool size for random selection.

### Equipment Randomization (`EquipmentTemplate`)

The `EquipmentTemplate` struct manages probabilistic equipment sets for creatures.

*   **`ChooseEquipmentEntry`**: Iterates through the `equipment` vector, accumulating probabilities until a random roll falls within a specific entry's range. This allows creatures to spawn with different gear sets based on weighted chances. If `totalProbability` is zero or no entry matches, it returns `nullptr`.

### Creature Template Information (`CreatureInfo`)

The `CreatureInfo` struct holds static data for a creature type, corresponding to the `creature_template` table.

*   **`GetHighGuid`**: Returns `HIGHGUID_UNIT`, indicating that creatures are treated as Unit objects in the object hierarchy.
*   **`GetObjectGuid`**: Similar to `CreatureData::GetObjectGuid`, but uses the `entry` field (the creature template ID) instead of a dynamic spawn ID.
*   **`IsTameable`**: Determines if a creature can be tamed by a hunter. It requires the creature to be a Beast (`CREATURE_TYPE_BEAST`), have a valid pet family, and possess the `CREATURE_STATIC_FLAG_TAMEABLE` flag.
*   **`GetTypeFlags`**: Converts internal static flags (`static_flags1`, `static_flags2`) into a bitmask suitable for sending to the client (`CreatureTypeFlags`). This filters out server-only flags and maps them to client-visible behaviors like tameness, ghost visibility, and wound animations.

### Data Model

This unit defines structs that directly map to database tables. While the header itself does not execute SQL, these structs are populated by other units (e.g., `Creature.Main/LoadFromDB`) using data from these tables.

1.  **`creature_template`**: Mapped to `CreatureInfo`. Contains static definitions for creature types, including name, level, faction, stats, spells, and AI name.
2.  **`creature`**: Mapped to `CreatureData`. Contains instance-specific data for each spawn point, including position, spawn times, and the list of possible creature IDs (`creature_id`).
3.  **`creature_addon`**: Mapped to `CreatureDataAddon`. Contains additional visual and state data for a specific creature instance, such as display ID, mount ID, equipment ID, stand state, and active auras.

No other database tables are directly referenced by the structures in this unit.

---

## Cross-Unit Boundaries

Members in `CreatureDefines.h` are called by various other units to retrieve creature identification, spawn configuration, and randomization results.

*   **`GetObjectGuid` (in `CreatureData`)**:
    *   Called by **`ChatHandler.Chat/ExtractGuidFromLink`**, **`ChatHandler.CreatureCommands/HandleNpcDeleteCommand`**, **`HandleNpcMoveHelperCommand`**, **`HandleWpAddCommand`**, **`HandleWpExportCommand`**, **`HandleWpModifyCommand`**, **`HandleWpShowCommand`**, and **`HandleGoCreatureCommand`**. These chat commands use the GUID to identify and manipulate specific creature instances via console commands.
    *   Called by **`Creature.Main/AddToRemoveListInMaps`** to manage creature removal from maps.
    *   Called by **`CreatureGroups/ConvertDBGuid`** to convert database GUIDs to in-memory GUIDs.
    *   Called by **`GameEventMgr.Main/UpdateCreatureData`** to update creature data during game events.
    *   Called by **`Map.Main/LoadCreatureSpawn`** to initialize creatures when a map loads.
    *   Called by **`PoolManager/Despawn1Object`** and **`PoolManager/ReSpawn1Object`** to manage creature pooling and respawning.

*   **`GetRandomRespawnTime` (in `CreatureData`)**:
    *   Called by **`Creature.Main/LoadFromDB`** to set initial respawn timers when loading creatures from the database.
    *   Called by **`PoolManager/Spawn1Object`** to determine respawn intervals for pooled creatures.

*   **`ChooseCreatureId` (in `CreatureData`)**:
    *   Called by **`Creature.Main/LoadFromDB`** and **`Creature.Main/Update`** to select which creature template to instantiate at a spawn point.
    *   Called by **`CreatureGroups/ChooseCreatureId`** to handle group spawning logic.

*   **`HasCreatureId` (in `CreatureData`)**:
    *   Called by **`CreatureGroups/ChooseCreatureId`** to validate creature IDs in group spawns.
    *   Called by **`ObjectMgr/operator()`** to check if a specific creature ID is associated with a spawn point.

*   **`GetCreatureIdCount` (in `CreatureData`)**:
    *   Called by **`ChatHandler.CreatureCommands/HandleNpcAddEntryCommand`** to determine how many IDs are already assigned to a spawn point.
    *   Called by **`Creature.Main/LoadFromDB`** to process the list of creature IDs for a spawn.

---

## Notable Implementation Details

1.  **Fallback Creature ID**: In `CreatureData::ChooseCreatureId`, if no valid creature IDs are found in the `creature_id` array, the function returns `1`. This is a hard-coded fallback. Creature ID `1` is typically a placeholder or invalid entry in most databases, so this likely indicates a misconfigured spawn point. Maintainers should ensure that all spawn points have at least one valid creature ID.

2.  **Probabilistic Equipment Selection**: `EquipmentTemplate::ChooseEquipmentEntry` uses a cumulative probability approach. It iterates through the equipment list, adding each entry's probability to a running sum. A random number is generated once, and the first entry whose cumulative sum exceeds the random number is selected. This ensures that the probabilities are respected correctly. If `totalProbability` is zero, it returns `nullptr`, meaning no equipment is equipped.

3.  **Static Flags Mapping**: `CreatureInfo::GetTypeFlags` carefully maps internal server flags to client-facing flags. For example, `CREATURE_STATIC_FLAG_TAMEABLE` maps to `CREATURE_TYPEFLAGS_TAMEABLE`. This separation allows the server to maintain detailed internal state while sending only relevant information to the client.

4.  **Pack Alignment**: The header uses `#pragma pack(1)` around the `CreatureInfo` and related structs. This ensures that the memory layout of these structs is tightly packed, which is crucial for binary compatibility with database records or network packets if they are serialized directly. However, most modern usage likely involves copying fields individually, so this may be legacy or for specific serialization routines.

5.  **Maximum Creature IDs**: `MAX_CREATURE_IDS_PER_SPAWN` is defined as `5`. This limits the number of different creature templates that can be assigned to a single spawn point. The `CreatureData::creature_id` array is sized accordingly.

6.  **Speed Reductions**: Constants like `SPEED_REDUCTION_HP_15`, `SPEED_REDUCTION_HP_10`, and `SPEED_REDUCTION_HP_5` define speed multipliers for creatures at low health percentages. These are used elsewhere in the codebase to implement "wounded slowdown" mechanics, unless disabled by `CREATURE_STATIC_FLAG_2_NO_WOUNDED_SLOWDOWN`.

7.  **Immunity Flags**: `CreatureImmunityFlags` defines bitmasks for various immunities (AOE, Taunt, Stat Mods, etc.). These are used in combat calculations to determine if a spell effect applies to a creature.

8.  **Vendor Item Limits**: `MAX_VENDOR_ITEMS` is set to `128`, reflecting a limitation in the `SMSG_LIST_INVENTORY` packet size. This restricts the number of items a vendor can sell.

---

## Member Reference

**GetObjectGuid**  
Constructs an `ObjectGuid` for the creature instance using the first creature ID in the spawn list and a provided low GUID. Used by chat commands, map loading, and pool management to identify creatures.

**GetRandomRespawnTime**  
Returns a random integer between `spawntimesecsmin` and `spawntimesecsmax`. Used by `Creature.Main/LoadFromDB` and `PoolManager/Spawn1Object` to set respawn timers.

**ChooseCreatureId**  
Selects a random creature ID from the `creature_id` array. If no valid IDs are present, defaults to `1`. Used by `Creature.Main/LoadFromDB`, `Creature.Main/Update`, and `CreatureGroups/ChooseCreatureId` to determine which creature template to spawn.

**HasCreatureId**  
Checks if a given creature ID exists in the `creature_id` array. Used by `CreatureGroups/ChooseCreatureId` and `ObjectMgr/operator()` for validation.

**GetCreatureIdCount**  
Counts the number of valid creature IDs in the `creature_id` array. Used by `ChatHandler.CreatureCommands/HandleNpcAddEntryCommand` and `Creature.Main/LoadFromDB` to determine the size of the spawn pool.

---

<!-- machine-true, projected from graph.json -->

## Map — CreatureData

*Source:* CreatureDefines.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GetObjectGuid | method | — | ChatHandler.Chat/ExtractGuidFromLink, ChatHandler.CreatureCommands/HandleNpcDeleteCommand, ChatHandler.CreatureCommands/HandleNpcMoveHelperCommand, ChatHandler.CreatureCommands/HandleWpAddCommand, ChatHandler.CreatureCommands/HandleWpExportCommand, ChatHandler.CreatureCommands/HandleWpModifyCommand, ChatHandler.CreatureCommands/HandleWpShowCommand, ChatHandler.TeleportCommands/HandleGoCreatureCommand, Creature.Main/AddToRemoveListInMaps, CreatureGroups/ConvertDBGuid, GameEventMgr.Main/UpdateCreatureData, Map.Main/LoadCreatureSpawn, PoolManager/Despawn1Object, PoolManager/ReSpawn1Object | — |
| GetRandomRespawnTime | method | — | Creature.Main/LoadFromDB, PoolManager/Spawn1Object | — |
| ChooseCreatureId | method | — | Creature.Main/LoadFromDB, Creature.Main/Update, CreatureGroups/ChooseCreatureId | — |
| HasCreatureId | method | — | CreatureGroups/ChooseCreatureId, ObjectMgr/operator() | — |
| GetCreatureIdCount | method | — | ChatHandler.CreatureCommands/HandleNpcAddEntryCommand, Creature.Main/LoadFromDB | — |
