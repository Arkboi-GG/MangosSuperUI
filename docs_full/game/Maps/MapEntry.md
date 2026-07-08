# MapEntry

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

This unit defines the static configuration data for game maps (`MapEntry`) and the runtime management class for active map instances (`Map`). It serves as the foundational layer for spatial organization, object lifecycle, and environment rules within the WoWVMaNGOS server.

1.  **`MapEntry`**: A lightweight struct holding read-only metadata for every map ID. It determines fundamental properties such as whether a map is a continent, dungeon, raid, or battleground, and enforces global rules like mount permissions and instanceability.
2.  **`Map`**: The core class representing a live map instance. It manages the grid-based spatial partitioning, object storage (players, creatures, game objects), visibility updates, and the execution of database-driven scripts. It acts as the central hub for all gameplay logic occurring within a specific geographic area.
3.  **Subclasses**: `WorldMap`, `DungeonMap`, and `BattleGroundMap` inherit from `Map` to provide specialized behavior for open-world zones, instanced dungeons/raids, and PvP arenas, respectively.

## Member-by-Member Behavior

### Map Metadata (`MapEntry`)

The `MapEntry` struct provides accessor methods that interpret the `mapType` field to classify the map. These methods are pure logic based on the stored `mapType` and `id` fields.

*   **IsDungeon**: Returns `true` if `mapType` is `MAP_INSTANCE` or `MAP_RAID`. This is the primary check for determining if a map requires instance management (binding, resetting).
*   **IsNonRaidDungeon**: Returns `true` only if `mapType` is `MAP_INSTANCE`. This distinguishes standard dungeons from raids, allowing different reset schedules and difficulty rules.
*   **Instanceable**: Returns `true` if `mapType` is `MAP_INSTANCE`, `MAP_RAID`, or `MAP_BATTLEGROUND`. This indicates the map can exist in multiple simultaneous instances identified by unique instance IDs.
*   **IsRaid**: Returns `true` if `mapType` is `MAP_RAID`. Used to enforce raid-specific entry requirements and longer reset timers.
*   **IsBattleGround**: Returns `true` if `mapType` is `MAP_BATTLEGROUND`. Used to route players to PvP-specific logic and instance creation.
*   **IsMountAllowed**: Implements a whitelist exception. Mounts are generally disabled in dungeons (`!IsDungeon()`), but explicitly allowed in Zul'Gurub, Zul'Aman, Ahn'Qiraj Ruins, and Caverns of Time. This is checked during spell casting and movement acknowledgment.
*   **IsContinent**: Returns `true` if the map ID is 0 (Eastern Kingdoms) or 1 (Kalimdor). Used for initialization and pooling logic.

### Cross-Unit Boundaries

*   **MapManager**: Calls `MapEntry::Instanceable()`, `IsBattleGround()`, and `IsContinent()` to decide which `Map` subclass to instantiate (`WorldMap`, `DungeonMap`, or `BattleGroundMap`). It also calls `IsDungeon()` and `IsRaid()` for instance creation and reset scheduling.
*   **Player**: Calls `MapEntry::IsMountAllowed()` and `IsDungeon()` via `Map::GetMapEntry()` to validate movement and spell restrictions. `Player` also calls `Map::Add()` and `Map::Remove()` (virtual) when entering or leaving the map.
*   **MapPersistentStateMgr**: Calls `IsDungeon()` and `IsRaid()` to determine reset intervals and persistence strategies. It interacts with `Map::GetPersistentState()` to save/load respawn times.
*   **Spell**: Calls `IsMountAllowed()` and `IsDungeon()` during `CheckCast` and effect resolution to validate spell usability in the current zone.
*   **ObjectMgr**: Calls `IsContinent()` and `IsDungeon()` during server startup to load map templates and player info.
*   **ChatHandler**: Calls `Instanceable()` and `IsContinent()` for GM commands like pool listing and teleportation validation.
*   **WorldSession**: Calls `IsDungeon()`, `IsRaid()`, `IsBattleGround()`, and `IsMountAllowed()` during movement acknowledgment and corpse queries to enforce zone-specific rules.

## Data Model

The `MapEntry` struct corresponds to the `map_template` table in the database. The fields map directly to the following columns:
*   `id`: Primary key, unique map identifier.
*   `parent`: Parent map ID (used for transports).
*   `mapType`: Integer defining the map category (0=Normal, 1=Instance, 2=Raid, 3=Battleground).
*   `linkedZone`: Zone ID linked for exploration purposes.
*   `maxPlayers`: Maximum number of players allowed in the instance.
*   `resetDelay`: Time in seconds before the instance resets.
*   `ghostEntranceMap/X/Y`: Coordinates for the entrance portal in the ghost zone.
*   `name`: Human-readable map name.
*   `scriptId`: ID of the script associated with the map.

The `Map` class itself does not directly query the database but relies on `MapPersistentState` (accessed via `GetPersistentState()`) to interact with tables like `creature_respawn`, `gameobject_respawn`, and `instance_reset`.

## Notable Implementation Details

1.  **Thread Safety**: The `Map` class uses `std::shared_timed_mutex` for `m_objectsStore` to allow concurrent reads while serializing writes. Access must go through `InsertObject`, `EraseObject`, or `GetObject` templates.
2.  **Grid-Based Loading**: Maps are divided into grids. Only grids near players are kept in memory. `EnsureGridLoaded` triggers lazy loading, optimizing memory usage for large continents.
3.  **Scripting Engine**: The `Map` class hosts a database-driven scripting system. `ScriptedEvent` manages complex, multi-step interactions with timers and conditions. Over 90 `ScriptCommand_*` methods execute specific actions (e.g., `ScriptCommand_SummonCreature`), mapped to an array `m_ScriptCommands` for fast lookup.
4.  **Mount Exceptions**: `IsMountAllowed` hardcodes exceptions for specific dungeons (Zul'Gurub, etc.), bypassing the general "no mounts in dungeons" rule. This is a legacy design choice visible in the source.
5.  **Visibility Optimization**: `UpdateObjectVisibility` is computationally expensive and offloaded to `m_visibilityThreads`. It calculates visibility based on distance and line-of-sight, updating clients only when necessary.

## Member Reference

**IsDungeon**
Returns `true` if `mapType` is `MAP_INSTANCE` or `MAP_RAID`.

**IsNonRaidDungeon**
Returns `true` if `mapType` is `MAP_INSTANCE`.

**Instanceable**
Returns `true` if `mapType` is `MAP_INSTANCE`, `MAP_RAID`, or `MAP_BATTLEGROUND`.

**IsRaid**
Returns `true` if `mapType` is `MAP_RAID`.

**IsBattleGround**
Returns `true` if `mapType` is `MAP_BATTLEGROUND`.

**IsMountAllowed**
Returns `true` if not a dungeon, or if the map ID is Zul'Gurub, Zul'Aman, Ahn'Qiraj Ruins, or Caverns of Time.

**IsContinent**
Returns `true` if map ID is 0 or 1.

---

<!-- machine-true, projected from graph.json -->

## Map — MapEntry

*Source:* Map.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| IsDungeon | method | — | game_Group_Group/RewardGroupAtKill, Map.Main/DungeonMap, MapManager/CanPlayerEnter, MapManager/CreateNewInstancesForPlayers, MapManager/CreateTestMap, MapManager/ScheduleNewWorldOnFarTeleport, MapPersistentStateMgr/AddPersistentState, MapPersistentStateMgr/GetStatistics, MapPersistentStateMgr/LoadCreatureRespawnTimes, MapPersistentStateMgr/LoadGameobjectRespawnTimes, MapPersistentStateMgr/LoadResetTimes, MapPersistentStateMgr/ScheduleAllDungeonResets, MapPersistentStateMgr/_ResetOrWarnAll, ObjectMgr/GetGoBackTrigger, ObjectMgr/LoadGroups, ObjectMgr/LoadMapTemplate, Player.Main/LoadFromDB, Player.Main/ResurrectUsingRequestData, Player.Main/_LoadBoundInstances, Spell.Main/CheckCast, spell_warlock/OnCheckCast#4, WorldSession.MiscHandler/HandleAreaTriggerOpcode, WorldSession.MovementHandler/HandleMoveWorldportAck, WorldSession.QueryHandler/HandleCorpseQueryOpcode | — |
| IsNonRaidDungeon | method | — | — | — |
| Instanceable | method | — | ChatHandler.LookupCommands/HandlePoolListCommand, ChatHandler.MiscCommands/HandlePoolSpawnsCommand, ChatHandler.TeleportCommands/HandleGoZoneXYCommand, MapManager/CreateMap, MapManager/Initialize, ObjectMgr/LoadPlayerInfo, Player.Main/KillPlayer, Player.Main/LoadCorpse, Player.Main/TeleportToHomebind, Player.Main/_LoadHomeBind, PoolManager/CheckAndRemember, TransportMgr/GenerateWaypoints | — |
| IsRaid | method | — | game_Group_Group/RewardGroupAtKill, MapManager/CanPlayerEnter, MapPersistentStateMgr/LoadResetTimes, WorldSession.MovementHandler/HandleMoveWorldportAck | — |
| IsBattleGround | method | — | MapManager/CreateInstance, MapPersistentStateMgr/AddPersistentState, MapPersistentStateMgr/SaveCreatureRespawnTime, MapPersistentStateMgr/SaveGORespawnTime, Player.Main/LoadFromDB, Player.Main/TeleportTo, Player.Main/_LoadBGData, SpellMgr/GetSpellAllowedInLocationError, WorldSession.MovementHandler/HandleMoveWorldportAck | — |
| IsMountAllowed | method | — | Spell.Effects/EffectScriptEffect, Spell.Main/CheckCast, spell_item/OnCheckCast#7, WorldSession.MovementHandler/HandleMoveWorldportAck | — |
| IsContinent | method | — | MapManager/Initialize, ObjectMgr/LoadMapTemplate, PoolManager/LoadFromDB | — |
