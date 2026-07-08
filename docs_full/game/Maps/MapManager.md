# MapManager

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# MapManager

## Purpose & Responsibilities

`MapManager` is the central singleton responsible for the lifecycle management, spatial partitioning, and asynchronous updating of all game worlds (maps) in the server. It acts as the factory and registry for `Map` objects, handling the creation, finding, and deletion of both persistent continents and dynamic instances (dungeons, raids, battlegrounds).

Key responsibilities include:
1.  **Map Lifecycle:** Creating new `Map` instances (`WorldMap`, `DungeonMap`, `BattleGroundMap`) when players enter them, and unloading them when they become empty or crash.
2.  **Asynchronous Updates:** Orchestrating the parallel update of map grids using thread pools (`ThreadPool`). It separates continent updates (which are split into geographic regions to allow parallelism) from instance updates.
3.  **Teleport Coordination:** Managing "far teleports" (cross-map movements) by scheduling them to execute only when the target map is ready and the current map update cycle is complete, preventing race conditions during world state transitions.
4.  **Instance Management:** Generating unique instance IDs, validating player entry permissions (raid requirements, instance counts), and managing the persistence of instance states via the `instance` database table.
5.  **Spatial Partitioning:** Determining which specific "continent instance" ID a coordinate belongs to, allowing large continents (Eastern Kingdoms, Kalimdor) to be split into smaller, independently updatable chunks.

## Member-by-Member Behavior

### Initialization and Configuration

*   **`MapManager` (ctor)**: Initializes the singleton. It sets up two thread pools: `m_threads` for general map updates (configured by `CONFIG_UINT32_MAPUPDATE_INSTANCED_UPDATE_THREADS`) and `m_instanceCreationThreads` for creating new dungeon instances asynchronously. It also initializes the grid cleanup delay and the map update timer interval from world configuration settings.
*   **`~MapManager` (dtor)**: Cleans up all loaded maps by deleting their pointers and destroys the static grid state machine objects.
*   **`Initialize`**: Called during server startup. It initializes the state machine, loads the maximum instance ID from the database, and preloads terrain data for continents and instances if configured (`CONFIG_BOOL_TERRAIN_PRELOAD_CONTINENTS`, `CONFIG_BOOL_TERRAIN_PRELOAD_INSTANCES`).
*   **`InitStateMachine`** / **`DeleteStateMachine`**: Manages the static array of `GridState` objects (`InvalidState`, `ActiveState`, `IdleState`, `RemovalState`) used to manage the lifecycle of individual map grids.
*   **`SetGridCleanUpDelay`** / **`SetMapUpdateInterval`**: Configurable setters for the delay before empty grids are unloaded and the frequency of map updates, respectively. These are called by `World/LoadConfigSettings`.
*   **`InitMaxInstanceId`**: Queries the `instance` table for the maximum existing `id` to ensure new instance IDs are unique and sequential. It ensures the ID starts at least at `RESERVED_INSTANCES_LAST`.

### Map Creation and Retrieval

*   **`CreateMap`**: The primary entry point for obtaining a `Map` object for a `WorldObject`.
    *   If the map is **instanceable** (dungeon/raid/battleground), it delegates to `CreateInstance`.
    *   If the map is **non-instanceable** (continent), it checks if a `WorldMap` already exists for the calculated instance ID (based on continent splitting). If not, it creates a new `WorldMap`, registers it, creates its instance data, spawns active objects, and notifies zone scripts.
*   **`FindMap`**: Looks up a `Map` in the internal `i_maps` container using a composite key of `mapid` and `instanceId`. Returns `nullptr` if not found.
*   **`CreateBgMap`**: Specifically creates a battleground map. It loads terrain, generates a new instance ID, and delegates to `CreateBattleGroundMap`.
*   **`CreateInstance`**: Handles the logic for creating or retrieving an instance map for a player.
    *   For **Battlegrounds**, it retrieves the existing map associated with the player's battleground ID.
    *   For **Dungeons/Raids**, it checks if the player has a bound instance save (`DungeonPersistentState`). If so, it tries to find the existing map. If not found, it creates a new `DungeonMap` using `CreateDungeonMap`.
    *   If no save exists, it generates a new instance ID and creates a fresh `DungeonMap`.
*   **`CreateDungeonMap`**: Constructs a `DungeonMap`, optionally loading saved state from a `DungeonPersistentState` object, and spawns active objects.
*   **`CreateBattleGroundMap`**: Constructs a `BattleGroundMap`, links it to the `BattleGround` object, and spawns active objects.
*   **`CreateTestMap`**: A utility for debugging/testing that creates a map without standard validation, useful for forcing map loads.
*   **`DeleteTestMap`**: Removes a test map from the registry and deletes it.
*   **`DeleteInstance`**: Explicitly removes a specific instance from the registry, unloads all its grids, and deletes the map object.

### Map Updates and Synchronization

*   **`Update`**: The core tick function called by `World/Update`. It performs the following sequence:
    1.  Executes any pending delayed teleports.
    2.  Iterates through all loaded maps. For each map, it performs synchronous updates (`UpdateSync`) and marks it as not updated.
    3.  Queues asynchronous updates:
        *   **Instances**: Queued to `m_threads`.
        *   **Continents**: Split into multiple threads (`m_continentThreads`) based on geographic regions.
    4.  Starts a separate thread (`m_instanceCreationThreads`) to handle `CreateNewInstancesForPlayers`.
    5.  Waits for continent updates to finish using condition variables (`waitContinentUpdateFinishedUntil`).
    6.  Waits for instance updates to finish.
    7.  Executes `SwitchPlayersInstances` to move players between continent partitions.
    8.  Executes remaining delayed teleports.
    9.  Handles crashed maps (notifying scripts, unloading) and unloads empty maps that have exceeded their idle timeout.
*   **`UpdateGridState`**: Delegates the state transition of a specific grid to the appropriate `GridState` object. Note: The code contains a TODO warning that this static state array access is not thread-safe regarding shared grid data across instances.
*   **`RemoveAllObjectsInRemoveList`**: Iterates all maps and calls their `RemoveAllObjectsInRemoveList` method, typically used during shutdown or major state resets.
*   **`MarkContinentUpdateFinished`**: Called by continent update threads to signal completion. Increments a counter and notifies waiting threads if all continent parts are done.
*   **`IsContinentUpdateFinished`** / **`waitContinentUpdateFinishedFor`** / **`waitContinentUpdateFinishedUntil`**: Synchronization primitives used by the main update loop to wait for parallel continent updates to complete.

### Teleportation and Instance Switching

*   **`ScheduleFarTeleport`**: Schedules a teleport for a player. If the map system is not currently updating asynchronously (`asyncMapUpdating` is false), it executes immediately. Otherwise, it adds the player to `m_scheduledFarTeleports` to be processed after the update cycle.
*   **`ExecuteDelayedPlayerTeleports`**: Processes all queued far teleports.
*   **`ExecuteSingleDelayedTeleport`**: Executes a specific queued teleport. If execution fails, it clears the player's teleport semaphore.
*   **`CancelDelayedPlayerTeleport`**: Removes a player from the teleport queue, used during logout or cancellation.
*   **`ScheduleNewWorldOnFarTeleport`**: Prepares for a far teleport. If the destination is a dungeon and the instance doesn't exist yet, it schedules the player for instance creation via `CreateNewInstancesForPlayers`. Otherwise, it sends the "New World" packet immediately.
*   **`CreateNewInstancesForPlayers`**: Runs in a dedicated thread. It processes players waiting for instance creation, creates the `DungeonMap`, forces grid loading around the destination, binds the player, and sends the "New World" packet. If the player is no longer teleporting, it logs an error.
*   **`ScheduleInstanceSwitch`**: Schedules a player to switch their continent instance ID (e.g., moving from one geographic partition of Eastern Kingdoms to another).
*   **`SwitchPlayersInstances`**: Executes all scheduled instance switches, moving players to their new continent partitions.

### Validation and Utilities

*   **`CanPlayerEnter`**: Validates if a player can enter a specific map.
    *   Checks if the map is a raid and if the player is in a raid group (unless GM or cheat-enabled).
    *   Checks if the player has exceeded the maximum number of instances allowed.
    *   Sends appropriate error messages (`SendRaidGroupOnlyError`, `SendTransferAborted`) if validation fails.
*   **`IsValidMapCoord`** (overloads): Static helpers to validate coordinates against map boundaries. They delegate to `IsValidMAP` (checks map existence) and `MaNGOS::IsValidMapCoord`.
*   **`IsValidMAP`**: Checks if a map ID exists in `sMapStorage`.
*   **`ExistMapAndVMap`**: Checks if both the map data and visibility map (VMap) exist for a specific grid coordinate.
*   **`GetContinentInstanceId`**: Determines the specific instance ID for a continent based on X/Y coordinates. It uses hardcoded polygon limits (`IsNorthTo`) to split Eastern Kingdoms and Kalimdor into smaller, manageable chunks (e.g., `MAP0_STORMWIND_AREA`, `MAP1_ORGRIMMAR`). This allows parallel updating of different parts of the same continent.
*   **`IsNorthTo`**: A static helper function implementing a ray-casting algorithm to determine if a point is inside a polygon defined by `limits`. Used by `GetContinentInstanceId`.
*   **`GenerateInstanceId`**: Increments and returns the next available instance ID.
*   **`GetNumInstances`** / **`GetNumPlayersInInstances`**: Statistics methods that iterate through loaded maps to count dungeons and players within them.
*   **`Maps`**: Returns the internal map of all loaded maps.
*   **`InitializeVisibilityDistanceInfo`**: Iterates all maps and initializes their visibility distance calculations.
*   **`UnloadAll`**: Shuts down all maps, executes pending teleports, deletes map objects, and unloads terrain data.

## Cross-Unit Boundaries

*   **`Map`**: `MapManager` creates, stores, and updates `Map` objects. It calls `Map::DoUpdate`, `Map::UpdateSync`, `Map::UnloadAll`, `Map::CanUnload`, `Map::IsCrashed`, etc. `Map` objects call back to `MapManager` for synchronization (e.g., `MarkContinentUpdateFinished`).
*   **`Player`**: `MapManager` interacts heavily with `Player` for teleportation (`ExecuteTeleportFar`, `SetPendingFarTeleport`), instance binding (`GetBoundInstanceSaveForSelfOrGroup`), and validation (`IsGameMaster`, `GetGroup`). Players call `MapManager::CreateMap`, `FindMap`, `ScheduleFarTeleport`, etc.
*   **`World`**: `World` calls `MapManager::Update`, `Initialize`, `UnloadAll`, and config setters. `MapManager` reads configuration from `World`.
*   **`ThreadPool`**: `MapManager` uses `ThreadPool` to parallelize map updates and instance creation.
*   **`ZoneScriptMgr`**: `MapManager` notifies `ZoneScriptMgr` when maps are loaded (`MapLoaded`) or crashed (`OnMapCrashed`).
*   **`BattleGround`**: `MapManager` creates `BattleGroundMap` objects and links them to `BattleGround` instances.
*   **`Database`**: `MapManager` queries the `instance` table to initialize the max instance ID.
*   **`TerrainManager`**: `MapManager` loads terrain data during initialization and map creation.
*   **`MapPersistentStateMgr`**: Used to retrieve instance IDs from saved states.
*   **`Log`**: `MapManager` logs errors, warnings, and debug information.

## Data Model

*   **`instance`**:
    *   **Usage**: `MapManager::InitMaxInstanceId` queries this table to find the highest existing instance ID (`SELECT MAX(id) FROM instance`). This ensures that newly generated instance IDs do not conflict with existing persisted instances.
    *   **Columns**: `id` (PK), `map`, `reset_time`, `data`.

## Notable Implementation Details

*   **Continent Splitting**: Large continents are split into multiple "instances" (e.g., `MAP0_STORMWIND_AREA`, `MAP1_ORGRIMMAR`) to allow parallel processing. The `GetContinentInstanceId` function uses hardcoded polygon coordinates to determine which partition a player belongs to. This is a performance optimization to reduce the load on single threads.
*   **Asynchronous Teleports**: Far teleports are not executed immediately if the map system is in an update cycle. They are queued and executed after the update completes to prevent race conditions where a player might be moved to a map that is currently being modified or unloaded.
*   **Thread Safety**: `MapManager` uses a recursive mutex (`std::recursive_mutex`) for general protection. However, the `Update` method uses fine-grained locking and condition variables to synchronize continent updates. The `si_GridStates` array is static and accessed without locks, which is flagged as a potential thread-safety issue in the code comments.
*   **Instance Creation Thread**: A dedicated thread pool (`m_instanceCreationThreads`) is used to create new dungeon instances asynchronously. This prevents blocking the main update loop during heavy instance creation events.
*   **Crash Handling**: The `Update` method explicitly checks for crashed maps (`IsCrashed`) and handles them by notifying scripts and unloading them. This suggests that maps can enter a crashed state, likely due to internal errors or corruption.
*   **Hardcoded Coordinates**: The continent splitting logic relies on hardcoded floating-point coordinates for polygons. This makes it difficult to adjust region boundaries without recompiling.
*   **Memory Management**: `MapManager` takes ownership of `Map` objects and is responsible for deleting them. It also manages `ScheduledTeleportData` pointers, ensuring they are deleted after execution or cancellation.

## Member Reference

*   **`MapManager`**: Constructor. Initializes thread pools, timers, and grid cleanup delay from world configuration.
*   **`~MapManager`**: Destructor. Deletes all loaded maps and static grid state objects.
*   **`Initialize`**: Server startup routine. Preloads terrain for continents/instances if configured, initializes state machine, and loads max instance ID from DB.
*   **`InitStateMachine`**: Creates static `GridState` objects for invalid, active, idle, and removal states.
*   **`DeleteStateMachine`**: Destroys static `GridState` objects.
*   **`CancelInstanceCreationForPlayer`**: Removes a player from the set of players waiting for instance creation.
*   **`UpdateGridState`**: Delegates grid state updates to the appropriate static `GridState` object.
*   **`InitializeVisibilityDistanceInfo`**: Iterates all maps and calls `InitVisibilityDistance` on each.
*   **`SetGridCleanUpDelay`**: Sets the delay before empty grids are unloaded.
*   **`CreateMap`**: Factory method to get or create a `Map` for a `WorldObject`. Handles both instanceable and non-instanceable maps.
*   **`SetMapUpdateInterval`**: Sets the interval for map updates.
*   **`IsValidMapCoord#2`**: Static helper to validate 2D coordinates against map boundaries.
*   **`IsValidMapCoord#3`**: Static helper to validate 3D coordinates against map boundaries.
*   **`IsValidMapCoord#4`**: Static helper to validate 4D coordinates (including orientation) against map boundaries.
*   **`IsValidMapCoord`**: Static helper to validate a `WorldLocation` against map boundaries.
*   **`GenerateInstanceId`**: Increments and returns the next unique instance ID.
*   **`CreateBgMap`**: Creates a battleground map by loading terrain and delegating to `CreateBattleGroundMap`.
*   **`Maps`**: Returns the internal map of all loaded maps.
*   **`FindMap`**: Looks up a `Map` by map ID and instance ID.
*   **`CanPlayerEnter`**: Validates if a player can enter a map, checking raid requirements and instance limits.
*   **`MapManager#2`**: Copy constructor declaration (private, disabled).
*   **`operator=`**: Assignment operator declaration (private, disabled).
*   **`DeleteInstance`**: Removes a specific instance from the registry, unloads grids, and deletes the map.
*   **`ScheduleNewWorldOnFarTeleport`**: Prepares for a far teleport, scheduling instance creation if necessary.
*   **`CreateNewInstancesForPlayers`**: Runs in a dedicated thread to create dungeon instances for players waiting for teleportation.
*   **`Update`**: Main tick function. Orchestrates synchronous and asynchronous map updates, teleport execution, and map unloading.
*   **`RemoveAllObjectsInRemoveList`**: Iterates all maps and removes objects marked for deletion.
*   **`ExistMapAndVMap`**: Checks if map and VMap data exist for a specific grid coordinate.
*   **`IsValidMAP`**: Checks if a map ID exists in storage.
*   **`UnloadAll`**: Shuts down all maps, executes pending teleports, and unloads terrain.
*   **`InitMaxInstanceId`**: Queries the `instance` table to set the starting value for instance ID generation.
*   **`GetNumInstances`**: Counts the number of loaded dungeon maps.
*   **`GetNumPlayersInInstances`**: Counts the total number of players in all loaded dungeon maps.
*   **`CreateInstance`**: Logic to create or retrieve an instance map for a player, handling dungeons and battlegrounds.
*   **`CreateTestMap`**: Debug utility to create a map without standard validation.
*   **`DeleteTestMap`**: Removes a test map from the registry.
*   **`CreateDungeonMap`**: Constructs a `DungeonMap`, optionally loading saved state.
*   **`CreateBattleGroundMap`**: Constructs a `BattleGroundMap` and links it to a `BattleGround` object.
*   **`IsNorthTo`**: Static helper to check if a point is inside a polygon using ray casting.
*   **`GetContinentInstanceId`**: Determines the continent instance ID for a coordinate using hardcoded polygon limits.
*   **`ScheduleFarTeleport`**: Schedules a far teleport, executing immediately if safe, otherwise queuing it.
*   **`ExecuteDelayedPlayerTeleports`**: Processes all queued far teleports.
*   **`ExecuteSingleDelayedTeleport#2`**: Executes a single queued teleport for a specific player.
*   **`ExecuteSingleDelayedTeleport`**: Internal helper to execute a teleport from an iterator.
*   **`CancelDelayedPlayerTeleport`**: Removes a player from the teleport queue.
*   **`ScheduleInstanceSwitch`**: Schedules a player to switch their continent instance ID.
*   **`SwitchPlayersInstances`**: Executes all scheduled continent instance switches.
*   **`MarkContinentUpdateFinished`**: Signals that a continent update thread has finished.
*   **`IsContinentUpdateFinished`**: Checks if all continent update threads have finished.
*   **`waitContinentUpdateFinishedFor`**: Waits for continent updates to finish for a specified duration.
*   **`waitContinentUpdateFinishedUntil`**: Waits for continent updates to finish until a specified time point.

---

<!-- machine-true, projected from graph.json -->

## Map — MapManager

*Source:* MapManager.cpp, MapManager.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| MapManager | ctor | IntervalTimer/SetInterval, ThreadPool/ThreadPool, World/getConfig#4 | — | — |
| ~MapManager | dtor | — | — | — |
| Initialize | method | GridMap/LoadAll, GridMap/LoadTerrain, MapEntry/Instanceable, MapEntry/IsContinent, World/getConfig | World/SetInitialWorldSettings | — |
| InitStateMachine | method | — | — | — |
| DeleteStateMachine | method | — | — | — |
| CancelInstanceCreationForPlayer | method | — | WorldSession.Main/LogoutPlayer | — |
| UpdateGridState | method | GridState/Update | Map.Main/Update#3 | — |
| InitializeVisibilityDistanceInfo | method | Map.Main/InitVisibilityDistance#3 | — | — |
| SetGridCleanUpDelay | method | — | World/LoadConfigSettings | — |
| CreateMap | method | Errors/PrintStacktraceAndThrow, Map.Main/CreateInstanceData, Map.Main/SpawnActiveObjects, MapEntry/Instanceable, MapID/MapID#2, Object/GetTypeId, WorldMap/WorldMap, WorldObject.Object/GetInstanceId, ZoneScriptMgr/MapLoaded | Player.Main/Create, Player.Main/LoadFromDB, Player.Main/SwitchInstance, PlayerBotAI/BeforeAddToMap#2, Transport/TeleportTransport, WorldSession.MovementHandler/HandleMoveWorldportAck | — |
| SetMapUpdateInterval | method | — | World/LoadConfigSettings | — |
| IsValidMapCoord#2 | method | — | ChatHandler.TeleportCommands/HandleGoHelper, ObjectMgr/LoadMapTemplate | — |
| IsValidMapCoord#3 | method | — | ChatHandler.ObjectCommands/HandleGameObjectMoveCommand, Player.Main/_LoadHomeBind, Spell.Main/SetTargetMap | — |
| IsValidMapCoord#4 | method | — | ChatHandler.TeleportCommands/HandleGoHelper, ObjectMgr/LoadGameobjects, ObjectMgr/LoadGameTele, ObjectMgr/LoadPlayerInfo, Player.Main/TeleportTo | — |
| IsValidMapCoord | method | — | WorldSession.MovementHandler/HandleMoveWorldportAck | — |
| GenerateInstanceId | method | — | — | — |
| CreateBgMap | method | GridMap/LoadTerrain | BattleGroundMgr/CreateNewBattleGround | — |
| Maps | method | — | ChatHandler.ServerCommands/HandleListMapsCommand | — |
| FindMap | method | MapID/MapID#2 | ChatHandler.HardcodedEvents/GetAliveCountAndUpdateRespawnTime, ChatHandler.HardcodedEvents/GetMap, ChatHandler.MiscCommands/HandleInstanceContinentsCommand, game_Group_Group/ResetInstances, ObjectAccessor/ConvertCorpseForPlayer, ObjectMgr/AddCreData, ObjectMgr/AddGOData, ObjectMgr/MoveCreData, Player.Main/ExecuteTeleportFar, Player.Main/GetNextQuest, Player.Main/PrepareQuestMenu, Player.Main/ResetInstance, Player.Main/SaveNewPlayer, Player.Main/TeleportTo, PlayerBotAI/SpawnNewPlayer, WorldSession.CharacterHandler/HandlePlayerLogin, WorldSession.MovementHandler/HandleMoveWorldportAck, ZoneScript/AddCreature, ZoneScript/AddObject, ZoneScript/SetCapturePointData | — |
| CanPlayerEnter | method | Group/isRaidGroup, Log.Main/Out, MapEntry/IsDungeon, MapEntry/IsRaid, MapPersistentStateMgr/GetInstanceId, Player.Main/CheckInstanceCount, Player.Main/GetBoundInstanceSaveForSelfOrGroup, Player.Main/GetGroup, Player.Main/GetName, Player.Main/HasCheatOption, Player.Main/IsGameMaster, Player.Main/SendRaidGroupOnlyError, Player.Main/SendTransferAborted, World/getConfig | Player.Main/ExecuteTeleportFar, Player.Main/TeleportTo | — |
| MapManager#2 | decl | — | — | — |
| operator= | decl | — | — | — |
| DeleteInstance | method | Map.Main/Instanceable, Map.Main/UnloadAll#3, MapID/MapID#2 | — | — |
| ScheduleNewWorldOnFarTeleport | method | Errors/PrintStacktraceAndThrow, MapEntry/IsDungeon, MapPersistentStateMgr/GetInstanceId, Player.Main/GetBoundInstanceSaveForSelfOrGroup, Player.Main/GetTeleportDest, Player.Main/SendNewWorld | Player.Main/ExecuteTeleportFar | — |
| CreateNewInstancesForPlayers | method | Errors/PrintStacktraceAndThrow, Log.Main/Out, Map.Main/BindPlayerOrGroupOnEnter, Map.Main/CanEnter#2, Map.Main/ForceLoadGridsAroundPosition, MapEntry/IsDungeon, Object/GetGUIDLow, Player.Main/GetTeleportDest, Player.Main/HandleReturnOnTeleportFail, Player.Main/IsBeingTeleportedFar, Player.Main/SendNewWorld, WorldLocation/WorldLocation#2, WorldObject.Object/GetPosition | — | — |
| Update | method | IntervalTimer/GetCurrent, IntervalTimer/Passed, IntervalTimer/SetCurrent, IntervalTimer/Update, Map.Main/CanUnload, Map.Main/CrashUnload, Map.Main/DoUpdate, Map.Main/Instanceable, Map.Main/IsCrashed, Map.Main/IsUpdateFinished, Map.Main/MarkNotUpdated, Map.Main/ShouldUpdateMap, Map.Main/UnloadAll#3, Map.Main/UpdateSync, shared_Util/getMSTime, ThreadPool/processWorkload#2, ThreadPool/processWorkload#3, ThreadPool/size, ThreadPool/status, ThreadPool/ThreadPool, World/getConfig#4, ZoneScriptMgr/OnMapCrashed | World/Update | — |
| RemoveAllObjectsInRemoveList | method | Map.Main/RemoveAllObjectsInRemoveList | World/Update | — |
| ExistMapAndVMap | method | GridDefines/ComputeGridPair, GridMap/ExistMap, GridMap/ExistVMap | World/SetInitialWorldSettings | — |
| IsValidMAP | method | — | — | — |
| UnloadAll | method | GridMap/UnloadAll, Map.Main/UnloadAll#3 | WorldRunnable/operator() | — |
| InitMaxInstanceId | method | Database/Query, Field/GetUInt32, QueryResult/Fetch | — | instance |
| GetNumInstances | method | Map.Main/IsDungeon | ChatHandler.MiscCommands/HandleInstanceStatsCommand | — |
| GetNumPlayersInInstances | method | LinkedListHead/getSize, Map.Main/GetPlayers, Map.Main/IsDungeon | ChatHandler.MiscCommands/HandleInstanceStatsCommand | — |
| CreateInstance | method | Errors/PrintStacktraceAndThrow, MapEntry/IsBattleGround, MapID/MapID#2, MapPersistentStateMgr/GetInstanceId, Player.Main/GetBattleGroundId, Player.Main/GetBoundInstanceSaveForSelfOrGroup | — | — |
| CreateTestMap | method | Errors/PrintStacktraceAndThrow, Log.Main/Out, Map.Main/CreateInstanceData, Map.Main/DungeonMap, MapEntry/IsDungeon, MapID/MapID#2, WorldMap/WorldMap, ZoneScriptMgr/MapLoaded | — | — |
| DeleteTestMap | method | Map.Main/GetId, Map.Main/GetInstanceId, MapID/MapID#2 | — | — |
| CreateDungeonMap | method | Errors/PrintStacktraceAndThrow, Log.Main/Out, Map.Main/CreateInstanceData, Map.Main/DungeonMap, Map.Main/SpawnActiveObjects | — | — |
| CreateBattleGroundMap | method | BattleGround/GetTypeID, BattleGround/SetBgMap, BattleGroundMap/SetBG, Errors/PrintStacktraceAndThrow, Log.Main/Out, Map.Main/BattleGroundMap, Map.Main/CreateInstanceData, Map.Main/IsBattleGround, Map.Main/SpawnActiveObjects, MapID/MapID#2 | — | — |
| IsNorthTo | function | — | — | — |
| GetContinentInstanceId | method | World/getConfig | ChatHandler.HardcodedEvents/GetAliveCountAndUpdateRespawnTime, ChatHandler.HardcodedEvents/GetMap, ChatHandler.PlayerBotMgr/AddBattleBot, ChatHandler.PlayerBotMgr/HandleBotAddAiCommand, Corpse/LoadFromDB, Creature.Main/DeleteFromDB#2, ObjectMgr/LoadCreatures, ObjectMgr/LoadGameobjects, Player.Main/Create, Player.Main/ExecuteTeleportFar, Player.Main/LoadFromDB, Player.Main/TeleportTo, Player.Main/Update, PlayerBotAI/CreatePlayerBotAI, PoolManager/GetContinentInstanceIdForPool, Transport/TeleportTransport, TransportMgr/CreateTransport, WorldSession.MovementHandler/ExecuteTeleportNear, WorldSession.MovementHandler/HandleMoveWorldportAck, ZoneScript/AddCreature, ZoneScript/AddObject, ZoneScript/SetCapturePointData | — |
| ScheduleFarTeleport | method | Player.Main/ExecuteTeleportFar, Player.Main/SetPendingFarTeleport | Player.Main/TeleportTo | — |
| ExecuteDelayedPlayerTeleports | method | — | — | — |
| ExecuteSingleDelayedTeleport#2 | method | — | WorldSession.CharacterHandler/HandlePlayerLogin, WorldSession.Main/LogoutPlayer | — |
| ExecuteSingleDelayedTeleport | method | Player.Main/ExecuteTeleportFar, Player.Main/SetPendingFarTeleport, Player.Main/SetSemaphoreTeleportFar | — | — |
| CancelDelayedPlayerTeleport | method | Player.Main/SetPendingFarTeleport | Player.Main/~Player | — |
| ScheduleInstanceSwitch | method | Errors/PrintStacktraceAndThrow, Map.Main/GetId, WorldObject.Object/GetMap | Player.Main/Update, Transport/TeleportTransport | — |
| SwitchPlayersInstances | method | Object/IsInWorld, Player.Main/SwitchInstance, WorldObject.Object/GetMapId | — | — |
| MarkContinentUpdateFinished | method | Errors/PrintStacktraceAndThrow | Map.Main/Update#3 | — |
| IsContinentUpdateFinished | method | — | — | — |
| waitContinentUpdateFinishedFor | method | — | — | — |
| waitContinentUpdateFinishedUntil | method | — | Map.Main/Update#3 | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `instance`: id int(11) unsigned PK, map int(11) unsigned, reset_time bigint(40), data longtext?

*`?` = nullable, `PK` = primary key column.*

