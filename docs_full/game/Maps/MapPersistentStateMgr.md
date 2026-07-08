<!-- provenance: failed-members -->
# MapPersistentStateMgr

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# MapPersistentStateMgr

## Purpose & Responsibilities

`MapPersistentStateMgr` is the central subsystem responsible for managing the lifecycle, persistence, and reset scheduling of map instances in the WoWVMaNGOS server. It handles three distinct categories of map states:
1.  **World States** (`WorldPersistentState`): Non-instanceable maps (e.g., continents) that persist indefinitely and share a single state object per map ID.
2.  **Dungeon/Raid States** (`DungeonPersistentState`): Instanceable maps where players and groups form bindings (lockouts). These states track player/group bindings, instance-specific data (boss kills, etc.), and respawn timers for creatures and game objects within that specific instance.
3.  **BattleGround/Arena States** (`BattleGroundPersistentState`): Temporary instances that do not persist across server restarts and have simplified lifecycle rules.

The manager provides:
*   **State Creation & Retrieval:** Factory methods to create or retrieve persistent state objects based on Map ID and Instance ID.
*   **Respawn Time Management:** Tracking and saving creature/gameobject respawn times to the database (`creature_respawn`, `gameobject_respawn`) for instances that are not currently loaded in memory.
*   **Instance Reset Scheduling:** A scheduler (`DungeonResetScheduler`) that calculates and triggers resets for normal dungeons (based on max respawn time + offset) and raids (based on global weekly/daily schedules).
*   **Database Integrity:** Startup routines to clean up orphaned instance data, remap instance IDs to ensure continuity, and load saved respawn times.

## Member-by-Member Behavior

### State Base Class: `MapPersistentState`

This abstract base class holds data common to all map types.

*   **`MapPersistentState` (ctor)** / **`~MapPersistentState` (dtor)**: Initializes the map and instance IDs. The destructor is virtual to support polymorphic deletion.
*   **`GetMapEntry`**: Retrieves the `MapEntry` DBC structure. If the state is currently attached to a loaded `Map` object (`m_usedByMap`), it delegates to that map; otherwise, it looks up the entry from the global `sMapStorage`.
*   **`UnloadIfEmpty`**: Checks if the state can be unloaded via `CanBeUnload()`. If so, it requests removal from the `MapPersistentStateManager` and returns `false` (indicating the object is about to be destroyed). Returns `true` if it remains valid.
*   **`GetInstanceId`** / **`GetMapId`**: Simple accessors for the instance and map identifiers.
*   **`IsUsedByMap`** / **`GetMap`** / **`SetUsedByMapState`**: Manages the link between the persistent state and the runtime `Map` object. Setting the map to `nullptr` (when a map unloads) triggers `UnloadIfEmpty()` to see if the state can be discarded from memory.
*   **`SaveCreatureRespawnTime`** / **`SaveGORespawnTime`**: Persists respawn times to the database.
    *   Logic: If the respawn time is in the future, it inserts/replaces the row in `creature_respawn` or `gameobject_respawn`. If the time is in the past (already respawned), it deletes the row.
    *   Constraint: BattleGrounds/Arenas skip database writes entirely, as they do not persist across restarts.
*   **`SetCreatureRespawnTime`** / **`SetGORespawnTime`**: Updates the in-memory map of respawn times. If the time is in the past, it removes the entry and calls `UnloadIfEmpty()`.
*   **`ClearRespawnTimes`**: Clears all in-memory respawn data and attempts to unload the state.
*   **`AddCreatureToGrid`** / **`RemoveCreatureFromGrid`** / **`AddGameobjectToGrid`** / **`RemoveGameobjectFromGrid`**: Maintains a spatial index (`m_gridObjectGuids`) of objects spawned dynamically (e.g., by pools) within specific grid cells. This allows the grid loader to know which objects exist in a cell even if the main map isn't fully loaded or to optimize loading. Uses `ComputeCellPair` to determine the cell ID.
*   **`GetCellObjectGuids`** / **`GetCellObjectGuidsMutex`**: Provides read-only access to the grid object map and its mutex for thread-safe iteration by `ObjectGridLoader`.
*   **`InitPools`**: Initializes the pool system (`sPoolMgr` and `sGameEventMgr`) for this specific map state if not already done. This ensures pool spawns are tracked correctly for this instance.
*   **`CanBeUnload`**: Virtual method returning `true` if the state is not currently attached to a loaded map. Subclasses override this to add additional constraints (e.g., active player bindings).
*   **`HasRespawnTimes`**: Returns `true` if there are any pending respawn times in memory.

### World State: `WorldPersistentState`

*   **`WorldPersistentState` (ctor)** / **`~WorldPersistentState` (dtor)**: Standard construction/destruction.
*   **`CanBeUnload`**: Overrides the base class to **always return `false`**. World states (continents) are never unloaded from memory while the server is running, ensuring their persistent data (like pool states) remains available.

### Dungeon State: `DungeonPersistentState`

*   **`DungeonPersistentState` (ctor)** / **`~DungeonPersistentState` (dtor)**: Initializes reset time and reset capability. The destructor calls `UnbindThisState()` to clean up player/group bindings.
*   **`UnbindThisState`**: Iterates through all bound players and groups and calls their respective `UnbindInstance` methods to remove the lockout/association.
*   **`CanBeUnload`**: Returns `true` only if the base class allows it AND there are no remaining player/group bindings (`!HasBounds()`) AND no pending respawn times (`!HasRespawnTimes()`).
*   **`SaveToDB`**: Saves the instance record to the `instance` table. It serializes the `InstanceData` (boss kills, etc.) from the currently loaded map (if any) and escapes the string before insertion.
*   **`DeleteRespawnTimesAndData`**: Removes all creature and gameobject respawn records for this instance from the database and clears the instance data field in the `instance` table. It then clears in-memory respawn times.
*   **`DeleteFromDB`**: Delegates to `MapPersistentStateManager::DeleteInstanceFromDB` to remove all traces of the instance from the database (bindings, respawns, instance record).
*   **`GetResetTimeForDB`**: Returns the reset time for normal dungeons. For raids, it returns `0` because raids use global reset times stored separately in `instance_reset`.
*   **`HasBounds`**: Helper to check if any players or groups are bound to this instance.

### BattleGround State: `BattleGroundPersistentState`

*   **`BattleGroundPersistentState` (ctor)** / **`~BattleGroundPersistentState` (dtor)**: Standard construction/destruction.
*   **`CanBeUnload`**: Overrides base class to return `true` if not used by a map. Unlike dungeons, BGs do not block unload based on respawn times or bindings, as they are ephemeral.

### Reset Scheduler: `DungeonResetScheduler`

*   **`DungeonResetScheduler` (ctor)**: Links to the manager instance.
*   **`GetMaxResetTimeFor`**: Calculates the maximum reset period for a map based on its `resetDelay` DBC field multiplied by days.
*   **`CalculateNextResetTime`**: Computes the next scheduled reset time for a raid/heroic map, accounting for the configured server reset hour offset.
*   **`LoadResetTimes`**: Called at startup.
    1.  Loads current reset times for normal dungeons from the `instance` table.
    2.  Updates these times if the max creature respawn time in `creature_respawn` suggests a later reset is needed (respawn time + 2 hours).
    3.  Loads global reset times for raids from `instance_reset`.
    4.  Cleans up expired instances in memory.
*   **`ScheduleAllDungeonResets`**: Called after instance packing. Schedules all pending normal dungeon resets and initializes global raid reset schedules into the priority queue (`m_resetTimeQueue`).
*   **`ScheduleReset`**: Adds or removes a `DungeonResetEvent` from the scheduler's priority queue. Validates that the instance exists and matches the map ID.
*   **`Update`**: The main tick function. Processes all events in the queue that have passed their trigger time.
    *   For `RESET_EVENT_NORMAL_DUNGEON`: Triggers `_ResetInstance` for that specific instance.
    *   For Global Resets/Warnings: Triggers `_ResetOrWarnAll`. If it's a warning, it schedules the next warning. If it's the final reset, it performs the reset and schedules the next global cycle.
*   **`ResetAllRaid`**: Emergency command handler. Forces all raid resets to occur immediately (with a short warning delay) by manipulating the scheduler queue.

### Manager: `MapPersistentStateManager`

*   **`MapPersistentStateManager` (ctor)** / **`~MapPersistentStateManager` (dtor)**: Singleton initialization. Destructor cleans up all state objects.
*   **`AddPersistentState`**: Factory method. Creates the appropriate subclass (`DungeonPersistentState`, `BattleGroundPersistentState`, or `WorldPersistentState`) based on the `MapEntry`. Registers it in the internal maps. If it's a new dungeon, it saves it to the DB and schedules its reset.
*   **`GetPersistentState`**: Retrieves a state object by Map ID and Instance ID.
*   **`RemovePersistentState`**: Removes a state from the manager's maps. Calls `_ResetSave` to handle cleanup.
*   **`DeleteInstanceFromDB`**: Static helper to delete all database records associated with a specific instance ID (from `instance`, `character_instance`, `group_instance`, `creature_respawn`, `gameobject_respawn`).
*   **`_DelHelper`**: Generic helper to delete rows from a table based on a join condition. Used extensively in cleanup routines.
*   **`CleanupInstances`**: Startup routine.
    1.  Loads reset times.
    2.  Deletes character/group bindings for non-existent characters/groups.
    3.  Deletes instances with no bindings.
    4.  Deletes orphaned respawn data.
*   **`PackInstances`**: Startup routine. Remaps instance IDs to be contiguous starting from `RESERVED_INSTANCES_LAST`. This prevents ID gaps and ensures efficient allocation. It updates all relevant tables (`instance`, `creature_respawn`, `gameobject_respawn`, `corpse`, `characters`, `character_instance`, `group_instance`).
*   **`ScheduleInstanceResets`**: Triggers the scheduler to populate its queue after cleanup/packing.
*   **`_ResetSave`**: Internal helper to delete a state object from the manager's maps, unlocking the instance lists temporarily to allow safe deletion.
*   **`_ResetInstance`**: Handles the reset of a single normal dungeon instance. If the map is loaded, it triggers a soft reset on the map object. Otherwise, it deletes the state and database records.
*   **`_ResetOrWarnAll`**: Handles global raid/heroic resets or warnings.
    *   If resetting: Unbinds all players, teleports them to homebind, resets all loaded maps of that type, and deletes all database records for that map ID.
    *   If warning: Sends reset warnings to all loaded maps of that type.
*   **`GetStatistics`**: Counts total states, bound players, and bound groups for reporting.
*   **`_CleanupExpiredInstancesAtTime`**: Helper to delete expired instances from the DB based on a timestamp.
*   **`LoadCreatureRespawnTimes`** / **`LoadGameobjectRespawnTimes`**: Startup routines. Load all pending respawn times from the database into memory, associating them with the correct persistent state objects. They filter out outdated respawns and verify map consistency.

## Cross-Unit Boundaries

*   **`Map.Main`**:
    *   `MapPersistentState` calls `Map.Main/GetMapEntry` to retrieve map metadata.
    *   `Map.Main` calls `MapPersistentState/SetUsedByMapState` to link the runtime map to its persistent state.
    *   `Map.Main` calls `MapPersistentState/UnloadIfEmpty` indirectly via `SetUsedByMapState` when unloading.
    *   `MapPersistentStateManager` calls `Map.Main/Reset` and `Map.Main/TeleportAllPlayersTo` during global resets.
*   **`Player.Main`** & **`game_Group_Group`**:
    *   These units call `MapPersistentState/GetInstanceId` and `GetMapId` to manage player/group bindings.
    *   `DungeonPersistentState/UnbindThisState` calls `Player.Main/UnbindInstance` and `game_Group_Group/UnbindInstance` to remove lockouts.
*   **`Creature.Main`** & **`GameObject`**:
    *   Call `MapPersistentState/SaveCreatureRespawnTime` and `SaveGORespawnTime` when an object dies or respawns.
    *   Call `GetCreatureRespawnTime` and `GetGORespawnTime` to check if an object should respawn.
*   **`PoolManager`**:
    *   Calls `MapPersistentState/AddCreatureToGrid`, `RemoveCreatureFromGrid`, etc., to track pool-spawned objects.
    *   Calls `InitPools` to initialize pool data for a new instance.
*   **`Database`**:
    *   Extensive interaction for saving/loading instance data, respawn times, and performing cleanup/packing operations.
*   **`World`**:
    *   Calls `MapPersistentStateManager/CleanupInstances`, `PackInstances`, `ScheduleInstanceResets`, `LoadCreatureRespawnTimes`, and `LoadGameobjectRespawnTimes` during server startup.
    *   Provides `GetGameTime` for respawn calculations.
*   **`ChatHandler`**:
    *   Various commands call into the manager to list pools, stats, or force resets.

## Data Model

The unit interacts with the following database tables:

*   **`instance`**: Stores the core instance record (`id`, `map`, `reset_time`, `data`). `data` contains serialized boss kill states.
*   **`instance_reset`**: Stores global reset times for raid/heroic maps (`map`, `reset_time`).
*   **`creature_respawn`**: Tracks pending creature respawns (`guid`, `respawn_time`, `instance`, `map`).
*   **`gameobject_respawn`**: Tracks pending gameobject respawns (`guid`, `respawn_time`, `instance`, `map`).
*   **`character_instance`**: Links characters to instances (`guid`, `instance`, `permanent`).
*   **`group_instance`**: Links groups to instances (`leader_guid`, `instance`, `permanent`).
*   **`characters`**: Updated during `PackInstances` to reflect remapped instance IDs.
*   **`corpse`**: Updated during `PackInstances` to reflect remapped instance IDs.

## Notable Implementation Details

*   **Instance ID Packing**: The `PackInstances` method is critical for maintaining database integrity. It shifts all instance IDs up by `RESERVED_INSTANCES_LAST` and then compacts them to remove gaps. This is done because instance IDs are allocated sequentially, and gaps can accumulate over time. The operation is transactional and updates multiple tables to maintain referential integrity.
*   **Reset Scheduling Logic**: Normal dungeons reset individually based on the latest creature respawn time + 2 hours. Raids reset globally based on a fixed schedule. The scheduler uses a priority queue (`std::multimap`) to efficiently process events in chronological order.
*   **Thread Safety**: `MapPersistentState` uses a `std::shared_timed_mutex` (`m_cellObjectGuidsMutex`) to protect the grid object map, allowing concurrent reads by `ObjectGridLoader` while exclusive writes are performed by pool managers.
*   **BattleGround Exclusion**: BattleGrounds are explicitly excluded from database persistence for respawn times and instance records. They are treated as ephemeral states that disappear upon server restart or map unload.
*   **Startup Sequence**: The manager enforces a strict startup sequence: `CleanupInstances` -> `PackInstances` -> `ScheduleInstanceResets` -> `LoadCreatureRespawnTimes` -> `LoadGameobjectRespawnTimes`. This order ensures that database records are consistent before being loaded into memory.

## Member Reference

**MapPersistentState** (ctor): Initializes map and instance IDs.
**~MapPersistentState** (dtor): Virtual destructor for polymorphic cleanup.
**GetMapEntry**: Retrieves `MapEntry` from the attached map or global storage.
**UnloadIfEmpty**: Checks `CanBeUnload()` and removes self from manager if true.
**GetInstanceId**: Returns the instance ID.
**GetMapId**: Returns the map ID.
**IsUsedByMap**: Returns true if linked to a loaded `Map` object.
**SaveCreatureRespawnTime**: Saves/updates/deletes creature respawn record in DB; skips for BGs.
**GetMap**: Returns the pointer to the attached `Map` object.
**SetUsedByMapState**: Links/unlinks the state to a `Map` object; triggers unload check if unlinking.
**GetCreatureRespawnTime**: Returns in-memory respawn time for a creature GUID.
**GetGORespawnTime**: Returns in-memory respawn time for a GO GUID.
**SaveGORespawnTime**: Saves/updates/deletes GO respawn record in DB; skips for BGs.
**GetSpawnedPoolData**: Returns reference to pool data for this state.
**GetCellObjectGuids**: Returns pointer to grid object map for a specific cell.
**GetCellObjectGuidsMutex**: Returns reference to the mutex protecting grid object data.
**SetCreatureRespawnTime**: Updates in-memory creature respawn time; removes if past.
**HasRespawnTimes**: Returns true if any respawn times are stored in memory.
**SetGORespawnTime**: Updates in-memory GO respawn time; removes if past.
**ClearRespawnTimes**: Clears all in-memory respawn times and attempts unload.
**CanBeUnload#3**: Base implementation returns true if not used by a map.
**AddCreatureToGrid**: Adds a creature GUID to the grid cell map.
**RemoveCreatureFromGrid**: Removes a creature GUID from the grid cell map.
**AddGameobjectToGrid**: Adds a GO GUID to the grid cell map.
**RemoveGameobjectFromGrid**: Removes a GO GUID from the grid cell map.
**InitPools**: Initializes pool system for this state if not already done.
**CanBeUnload#4**: `WorldPersistentState` override; always returns false.
**DungeonPersistentState** (ctor): Initializes reset time and binding lists.
**~DungeonPersistentState** (dtor): Calls `UnbindThisState` to clean bindings.
**UnbindThisState**: Removes instance bindings from all associated players and groups.
**CanBeUnload#2**: Returns true if no bindings, no respawn times, and base allows unload.
**SaveToDB**: Inserts/updates instance record in DB with serialized data.
**DeleteRespawnTimesAndData**: Removes respawn records and instance data from DB.
**DeleteFromDB**: Delegates to manager to delete all instance records from DB.
**GetResetTimeForDB**: Returns reset time for normal dungeons; 0 for raids.
**CanBeUnload**: Alias for `CanBeUnload#2`.
**GetMaxResetTimeFor**: Calculates max reset period for a map type.
**CalculateNextResetTime**: Computes next global reset time for raids.
**LoadResetTimes**: Loads and validates reset times from DB at startup.
**ScheduleAllDungeonResets**: Populates scheduler queue with pending resets.
**ScheduleReset**: Adds/removes reset events from the scheduler queue.
**Update**: Processes scheduler queue, triggering resets or warnings.
**ResetAllRaid**: Forces immediate raid resets via scheduler manipulation.
**MapPersistentStateManager** (ctor): Initializes singleton and scheduler.
**~MapPersistentStateManager** (dtor): Cleans up all state objects.
**AddPersistentState**: Factory method to create and register state objects.
**GetPersistentState**: Retrieves state object by map/instance ID.
**DeleteInstanceFromDB**: Static helper to delete all DB records for an instance.
**RemovePersistentState**: Removes state from manager maps and triggers cleanup.
**_DelHelper**: Generic DB deletion helper using joins.
**CleanupInstances**: Startup routine to clean orphaned DB records.
**PackInstances**: Startup routine to compact instance IDs in DB.
**ScheduleInstanceResets**: Triggers scheduler population after packing.
**_ResetSave**: Internal helper to safely delete state from manager maps.
**_ResetInstance**: Handles reset of a single normal dungeon instance.
**MapPersistantStateResetWorker** (ctor): Worker struct for global resets.
**operator()**: Teleports players and resets map during global reset.
**MapPersistantStateWarnWorker** (ctor): Worker struct for reset warnings.
**operator()#2**: Sends reset warnings to loaded maps.
**_ResetOrWarnAll**: Handles global raid resets or warnings.
**GetStatistics**: Counts states, players, and groups for reporting.
**_CleanupExpiredInstancesAtTime**: Helper to delete expired instances from DB.
**LoadCreatureRespawnTimes**: Loads creature respawn times from DB into memory.
**LoadGameobjectRespawnTimes**: Loads GO respawn times from DB into memory.

---

<!-- machine-true, projected from graph.json -->

## Map — MapPersistentStateMgr

*Source:* MapPersistentStateMgr.cpp, MapPersistentStateMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| MapPersistentState | ctor | — | — | — |
| ~MapPersistentState | dtor | — | — | — |
| GetMapEntry | method | Map.Main/GetMapEntry | ChatHandler.LookupCommands/HandlePoolListCommand, ChatHandler.MiscCommands/HandlePoolSpawnsCommand, PoolManager/InitSpawnPool | — |
| UnloadIfEmpty | method | — | — | — |
| GetInstanceId | method | — | ChatHandler.MiscCommands/HandleInstanceListBindsCommand, ChatHandler.MiscCommands/HandleInstanceUnbindHelper, ChatHandler.TeleportCommands/HandleGonameCommand, game_Group_Group/BindToInstance, game_Group_Group/Disband, game_Group_Group/ResetInstances, game_Group_Group/UnbindInstance, game_Group_Group/_addMember#2, game_Group_Group/_setLeader, Map.Main/BindPlayerOrGroupOnEnter, Map.Main/SetResetSchedule, MapManager/CanPlayerEnter, MapManager/CreateInstance, MapManager/ScheduleNewWorldOnFarTeleport, Player.Main/BindToInstance, Player.Main/ExecuteTeleportFar, Player.Main/LoadFromDB, Player.Main/ResetInstance, Player.Main/ResurrectUsingRequestData, Player.Main/SendRaidInfo, Player.Main/TeleportTo, Player.Main/UnbindInstance | — |
| GetMapId | method | — | ChatHandler.LookupCommands/HandlePoolListCommand, ChatHandler.MiscCommands/HandlePoolSpawnsCommand, game_Group_Group/BindToInstance, game_Group_Group/ResetInstances, Map.Main/BindPlayerOrGroupOnEnter, Map.Main/SetResetSchedule, Player.Main/BindToInstance, Player.Main/ResetInstance, Player.Main/SendRaidInfo, Player.Main/SendSavedInstances, PoolManager/Despawn1Object, PoolManager/Despawn1Object#2, PoolManager/ReSpawn1Object, PoolManager/ReSpawn1Object#2, PoolManager/Spawn1Object, PoolManager/Spawn1Object#2 | — |
| IsUsedByMap | method | — | — | — |
| SaveCreatureRespawnTime | method | Database/CreateStatement, MapEntry/IsBattleGround, SqlStatementID/SqlStatementID, World/GetGameTime | ChatHandler.HardcodedEvents/GetAliveCountAndUpdateRespawnTime, Creature.Main/LoadFromDB, Creature.Main/operator()#2, Creature.Main/Respawn, Creature.Main/SaveRespawnTime, PoolManager/Spawn1Object | creature_respawn |
| GetMap | method | — | PoolManager/Despawn1Object, PoolManager/Despawn1Object#2, PoolManager/ReSpawn1Object, PoolManager/ReSpawn1Object#2, PoolManager/Spawn1Object, PoolManager/Spawn1Object#2 | — |
| SetUsedByMapState | method | — | Map.Main/CrashUnload, Map.Main/Map, Map.Main/~Map | — |
| GetCreatureRespawnTime | method | — | Creature.Main/LoadFromDB, CreatureLinkingMgr/IsRespawnReady, PoolManager/GetPoolObjectRespawnTime | — |
| GetGORespawnTime | method | — | GameObject/LoadFromDB, PoolManager/GetPoolObjectRespawnTime#2 | — |
| SaveGORespawnTime | method | Database/CreateStatement, MapEntry/IsBattleGround, SqlStatementID/SqlStatementID, World/GetGameTime | GameObject/LoadFromDB, GameObject/operator()#2, GameObject/Respawn, GameObject/SaveRespawnTime, PoolManager/Spawn1Object#2 | gameobject_respawn |
| GetSpawnedPoolData | method | — | ChatHandler.MiscCommands/HandlePoolInfoCommand, ChatHandler.MiscCommands/HandlePoolSpawnsCommand, ChatHandler.MiscCommands/HandlePoolUpdateCommand, PoolManager/DespawnObject, PoolManager/SpawnObject | — |
| GetCellObjectGuids | method | — | ObjectGridLoader/Visit#2, ObjectGridLoader/Visit#4 | — |
| GetCellObjectGuidsMutex | method | — | ObjectGridLoader/Visit#2, ObjectGridLoader/Visit#4 | — |
| SetCreatureRespawnTime | method | World/GetGameTime | — | — |
| HasRespawnTimes | method | — | — | — |
| SetGORespawnTime | method | World/GetGameTime | — | — |
| ClearRespawnTimes | method | — | — | — |
| CanBeUnload#3 | method | — | — | — |
| AddCreatureToGrid | method | GridDefines/ComputeCellPair | PoolManager/Spawn1Object | — |
| RemoveCreatureFromGrid | method | GridDefines/ComputeCellPair | PoolManager/Despawn1Object | — |
| AddGameobjectToGrid | method | GridDefines/ComputeCellPair | PoolManager/Spawn1Object#2 | — |
| RemoveGameobjectFromGrid | method | GridDefines/ComputeCellPair | PoolManager/Despawn1Object#2 | — |
| InitPools | method | GameEventMgr.Main/Initialize#2, PoolManager/Initialize, SpawnedPoolData/IsInitialized, SpawnedPoolData/SetInitialized | Map.Main/SpawnActiveObjects | — |
| CanBeUnload#4 | method | — | — | — |
| DungeonPersistentState | ctor | Errors/PrintStacktraceAndThrow | — | — |
| ~DungeonPersistentState | dtor | — | — | — |
| UnbindThisState | method | game_Group_Group/UnbindInstance, Player.Main/UnbindInstance#2 | — | — |
| CanBeUnload#2 | method | DungeonPersistentState/HasBounds | — | — |
| SaveToDB | method | Database/escape_string, Database/PExecute#2, InstanceData/Save, Map.Main/GetInstanceData | — | instance |
| DeleteRespawnTimesAndData | method | Database/BeginTransaction, Database/CommitTransaction, Database/PExecute#2 | Map.Main/UnloadAll#2 | creature_respawn, gameobject_respawn, instance |
| DeleteFromDB | method | — | game_Group_Group/ResetInstances, Player.Main/ResetInstance | — |
| GetResetTimeForDB | method | DungeonPersistentState/GetResetTime | — | — |
| CanBeUnload | method | — | — | — |
| GetMaxResetTimeFor | method | — | — | — |
| CalculateNextResetTime | method | World/getConfig#4 | — | — |
| LoadResetTimes | method | Database/DirectPExecute, Database/Query, DungeonResetScheduler/SetResetTimeFor, Field/GetUInt32, Field/GetUInt64, Log.Main/Out, MapEntry/IsDungeon, MapEntry/IsRaid, QueryResult/Fetch, QueryResult/NextRow, SQLStorage/GetMaxEntry, World/getConfig#4 | — | creature_respawn, instance, instance_reset |
| ScheduleAllDungeonResets | method | Database/DirectPExecute, Database/Query, DungeonResetEvent/DungeonResetEvent#2, DungeonResetScheduler/GetResetTimeFor, DungeonResetScheduler/SetResetTimeFor, Field/GetUInt32, Field/GetUInt64, Log.Main/Out, MapEntry/IsDungeon, QueryResult/Fetch, QueryResult/NextRow, World/getConfig#4 | — | instance, instance_reset |
| ScheduleReset | method | DungeonResetEvent/operator==, Log.Main/Out | Map.Main/SetResetSchedule | — |
| Update | method | Database/DirectPExecute, DungeonResetScheduler/GetResetTimeFor, DungeonResetScheduler/SetResetTimeFor, Errors/PrintStacktraceAndThrow | — | instance_reset |
| ResetAllRaid | method | DungeonResetScheduler/SetResetTimeFor | ChatHandler.ServerCommands/HandleServerResetAllRaidCommand | — |
| MapPersistentStateManager | ctor | DungeonResetScheduler/DungeonResetScheduler | — | — |
| ~MapPersistentStateManager | dtor | — | — | — |
| AddPersistentState | method | BattleGroundPersistentState/BattleGroundPersistentState, DungeonPersistentState/SetResetTime, DungeonResetEvent/DungeonResetEvent#2, Log.Main/Out, MapEntry/IsBattleGround, MapEntry/IsDungeon, WorldPersistentState/WorldPersistentState | Map.Main/Map, ObjectMgr/LoadGroups, Player.Main/_LoadBoundInstances, PlayerBotAI/SpawnNewPlayer | — |
| GetPersistentState | method | — | — | — |
| DeleteInstanceFromDB | method | Database/BeginTransaction, Database/CommitTransaction, Database/PExecute#2 | — | character_instance, creature_respawn, gameobject_respawn, group_instance, instance |
| RemovePersistentState | method | — | — | — |
| _DelHelper | method | Database/escape_string, Database/PExecute#2, Database/PQuery, Errors/PrintStacktraceAndThrow, Field/GetCppString, QueryResult/Fetch, QueryResult/NextRow, shared_Util/StrSplit | — | — |
| CleanupInstances | method | Database/BeginTransaction, Database/CommitTransaction, Database/PExecute#2, Log.Main/Out, ProgressBar/BarGoLink, ProgressBar/step | World/SetInitialWorldSettings | creature_respawn, gameobject_respawn, instance |
| PackInstances | method | Database/BeginTransaction, Database/CommitTransaction, Database/Execute#2, Database/PExecute#2, Database/Query, Field/GetUInt32, Log.Main/Out, ProgressBar/BarGoLink#3, ProgressBar/step, QueryResult/Fetch, QueryResult/NextRow | World/SetInitialWorldSettings | characters, character_instance, corpse, creature_respawn, gameobject_respawn, group_instance, instance |
| ScheduleInstanceResets | method | — | World/SetInitialWorldSettings | — |
| _ResetSave | method | Log.Main/Out | — | — |
| _ResetInstance | method | Log.Main/Out, Map.Main/GetId, Map.Main/IsDungeon, Map.Main/Reset | — | — |
| MapPersistantStateResetWorker | ctor | — | — | — |
| operator() | method | Map.Main/Reset, Map.Main/TeleportAllPlayersTo | — | — |
| MapPersistantStateWarnWorker | ctor | — | — | — |
| operator()#2 | method | Map.Main/SendResetWarnings | — | — |
| _ResetOrWarnAll | method | Database/BeginTransaction, Database/CommitTransaction, Database/PExecute#2, Log.Main/Out, MapEntry/IsDungeon | — | character_instance, group_instance, instance, instance_reset |
| GetStatistics | method | DungeonPersistentState/GetGroupCount, DungeonPersistentState/GetPlayerCount, MapEntry/IsDungeon | ChatHandler.MiscCommands/HandleInstanceStatsCommand | — |
| _CleanupExpiredInstancesAtTime | method | — | — | — |
| LoadCreatureRespawnTimes | method | Database/DirectExecute, Database/Query, Field/GetUInt32, Field/GetUInt64, Log.Main/Out, MapEntry/IsDungeon, ObjectMgr/GetCreatureData, ProgressBar/BarGoLink, ProgressBar/BarGoLink#3, ProgressBar/step, QueryResult/Fetch, QueryResult/GetRowCount, QueryResult/NextRow, World/getConfig | World/SetInitialWorldSettings | creature_respawn, instance |
| LoadGameobjectRespawnTimes | method | Database/DirectExecute, Database/Query, Field/GetUInt32, Field/GetUInt64, Log.Main/Out, MapEntry/IsDungeon, ObjectMgr/GetGOData, ProgressBar/BarGoLink, ProgressBar/BarGoLink#3, ProgressBar/step, QueryResult/Fetch, QueryResult/GetRowCount, QueryResult/NextRow | World/SetInitialWorldSettings | gameobject_respawn, instance |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `character_instance`: guid int(11) unsigned PK, instance int(11) unsigned PK, permanent tinyint(1) unsigned
- `characters`: guid int(11) unsigned PK, account int(11) unsigned, name varchar(12), race tinyint(3) unsigned, class tinyint(3) unsigned, gender tinyint(3) unsigned, skin tinyint(3) unsigned, face tinyint(3) unsigned, hair_style tinyint(3) unsigned, hair_color tinyint(3) unsigned, facial_hair tinyint(3) unsigned, level tinyint(3) unsigned, xp int(10) unsigned, money int(10) unsigned, character_flags int(10) unsigned, zone int(11) unsigned, map int(11) unsigned, instance int(11) unsigned, position_x float, position_y float, position_z float, orientation float, transport_guid bigint(20) unsigned, transport_x float, transport_y float, transport_z float, transport_o float, known_taxi_mask longtext?, current_taxi_path text?, online tinyint(3) unsigned, played_time_total int(11) unsigned, played_time_level int(11) unsigned, create_time bigint(20) unsigned, logout_time bigint(20) unsigned, rest_bonus float, reset_talents_multiplier int(11) unsigned, reset_talents_time bigint(20) unsigned, death_expire_time bigint(20) unsigned, stable_slots tinyint(1) unsigned, bank_bag_slots tinyint(1) unsigned, extra_flags int(11) unsigned, honor_rank_points float, honor_highest_rank int(11) unsigned, honor_standing int(11) unsigned, honor_last_week_hk int(11) unsigned, honor_last_week_cp float, honor_stored_hk int(11), honor_stored_dk int(11), watched_faction int(11), drunk smallint(5) unsigned, health int(10) unsigned, power1 int(10) unsigned, power2 int(10) unsigned, power3 int(10) unsigned, power4 int(10) unsigned, power5 int(10) unsigned, explored_zones longtext?, equipment_cache longtext?, ammo_id int(10) unsigned, action_bars tinyint(3) unsigned, deleted_account int(11) unsigned?, deleted_name varchar(12)?, deleted_time bigint(20)?, world_phase_mask int(11)?
- `corpse`: guid int(11) unsigned PK, player_guid int(11) unsigned, position_x float, position_y float, position_z float, orientation float, map int(11) unsigned, time bigint(20) unsigned, corpse_type tinyint(3) unsigned, instance int(11) unsigned
- `creature_respawn`: guid int(10) unsigned PK, respawn_time bigint(20), instance mediumint(8) unsigned PK, map int(5) unsigned?
- `gameobject_respawn`: guid int(10) unsigned PK, respawn_time bigint(20), instance mediumint(8) unsigned PK, map int(5) unsigned?
- `group_instance`: leader_guid int(11) unsigned PK, instance int(11) unsigned PK, permanent tinyint(1) unsigned
- `instance`: id int(11) unsigned PK, map int(11) unsigned, reset_time bigint(40), data longtext?
- `instance_reset`: map int(11) unsigned PK, reset_time bigint(40)

*`?` = nullable, `PK` = primary key column.*


---

<!-- verify: failed-members | invented: operator -->
