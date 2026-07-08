<!-- provenance: no-member-reference-section -->
# Map.Main

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Map — Map.Main

## Purpose & Responsibilities

The `Map` class (and its subclasses `DungeonMap` and `BattleGroundMap`) represents a single logical instance of a game world area, such as a continent, a dungeon instance, or a battleground. It is the central authority for all entities (players, creatures, game objects, corpses) residing within that specific spatial context.

Key responsibilities include:
1.  **Spatial Management:** Dividing the map into grids and cells, managing the loading and unloading of these grids based on activity, and tracking the precise location of every object.
2.  **Entity Lifecycle:** Adding and removing objects from the world, handling their visibility updates, and managing their movement and state updates.
3.  **Instance Logic:** For dungeons and raids, managing instance-specific data, player bindings, reset schedules, and encounter states.
4.  **Scripting Engine:** Executing database-driven scripts (`db_scripts`) and C++ scripted events, including timed actions, conditional checks, and target resolution.
5.  **Collision & Line-of-Sight:** Providing interfaces for terrain height queries, line-of-sight checks (static VMaps and dynamic objects), and pathfinding support via navigation meshes.
6.  **Communication:** Broadcasting packets to players within specific ranges or zones, and handling visibility updates for relocated units.

## Data Model

The `Map` unit interacts with two database tables to persist instance-specific state:

*   **`instance`**: Stores state for individual dungeon/raid instances.
    *   `id` (int(11) unsigned, PK): The unique instance ID.
    *   `map` (int(11) unsigned): The map ID this instance belongs to.
    *   `reset_time` (bigint(40)): The timestamp when the instance resets.
    *   `data` (longtext, nullable): Serialized instance data (boss states, flags, etc.).
*   **`world`**: Stores state for non-instanced maps (continents) that require persistent scripting state.
    *   `map` (int(11) unsigned, PK): The map ID.
    *   `data` (longtext, nullable): Serialized map-wide script data.

## Member-by-Member Behavior

### Construction, Destruction, and Initialization

*   **`Map` (ctor)**: Initializes the map with a specific ID, expiry time, and instance ID. It loads terrain data, sets up persistent state managers, initializes visibility distances, creates thread pools for continent maps (for parallel updates), spawns transports, and loads elevator transports.
*   **`~Map` (dtor)**: Cleans up resources by unloading all grids, decreasing scheduled script counts, releasing persistent state references, deleting instance data, and unloading terrain. It logs an error if corpses remain to be removed.
*   **`Map#2` (decl)**: Declares the copy constructor, which is deleted to prevent copying maps.
*   **`operator=` (decl)**: Declares the assignment operator, which is deleted to prevent assigning maps.
*   **`LoadMapAndVMap`**: Loads the visual map (VMap) data for a specific grid coordinate if not already loaded.
*   **`SpawnActiveObjects`**: Iterates through all creature and game object data to load grids containing "active" objects (those that must always be processed, like world bosses or critical quest NPCs).
*   **`InitVisibilityDistance`**: Sets the default visibility and grid activation distances based on whether the map is a continent, instance, or battleground.
*   **`InitVisibilityDistance#2`** (`DungeonMap`): Overrides the base method to set visibility distances specific to instances using `World::GetMaxVisibleDistanceInInstances`.
*   **`InitVisibilityDistance#3`** (`Map`): The base implementation sets distances using `World::GetMaxVisibleDistanceOnContinents`.
*   **`LoadElevatorTransports`**: Specifically loads elevator-type transports from the database and adds them to the map.
*   **`ActiveObjectsGridLoader` (ctor)**: A helper functor used by `SpawnActiveObjects` to iterate over object data and trigger grid loading for active spawns.

### Grid and Cell Management

*   **`EnsureGridCreated`**: Creates the underlying `NGridType` structure for a given grid pair if it doesn't exist, linking it to the map and loading the corresponding terrain/VMap data.
*   **`EnsureGridLoaded`**: Ensures a grid is created and its object data is loaded from the database if not already loaded. It prevents infinite recursion by marking the grid as "loading" before starting the load process.
*   **`EnsureGridLoadedAtEnter`**: A convenience wrapper that ensures a grid is loaded and adds a player to it if provided.
*   **`ForceLoadGridsAroundPosition`**: Forces the loading of grids around a specific coordinate, used for instance creation or teleportation.
*   **`LoadGrid`**: Ensures a grid is loaded and optionally locks it from unloading.
*   **`UnloadGrid`**: Unloads a specific grid, moving creatures to their respawn points or removing them, and freeing memory. It checks for active objects that might prevent unloading.
*   **`UnloadAll`** (`Map`): Unloads all grids on the map, removing all objects and transports.
*   **`UnloadAll#2`** (`DungeonMap`): Overrides `Map::UnloadAll` to teleport all players to their homebind and delete persistent respawn data if the instance is scheduled for reset.
*   **`UnloadAll#3`** (`BattleGroundMap`): Overrides `Map::UnloadAll` to teleport all players to the battleground entry point.
*   **`CheckGridIntegrity`**: Debug helper to verify that a creature's recorded cell matches its actual coordinates.
*   **`getNGrid` / `setNGrid`**: Accessors for the internal grid array.
*   **`buildNGridLinkage`**: Links a grid to the map for reference counting.
*   **`loaded`**: Checks if a specific grid is loaded and has its object data ready.
*   **`IsLoaded`**: Checks if the grid at specific coordinates is loaded.
*   **`IsRemovalGrid`**: Checks if a grid is in the removal state.
*   **`GetUnloadLock` / `SetUnloadLock`**: Manages explicit locks preventing a grid from unloading.
*   **`ResetGridExpiry`**: Resets the timer for when a grid becomes eligible for unloading.
*   **`GetGridExpiry`**: Returns the default grid expiry time.
*   **`ActiveObjectsNearGrid`**: Checks if any active objects or players are within a certain range of a grid, used to prevent unloading active areas.
*   **`AddToActive` / `RemoveFromActive`**: Manages the list of active non-player objects that require constant updating regardless of grid load status.
*   **`CanUnload`**: Checks if the map's unload timer has expired, allowing the map to be destroyed.

### Object Addition and Removal

*   **`Add` (Player)**: Adds a player to the map, initializing their visibility, sending initial transport and self data, and registering them with the map's reference manager.
*   **`Add#2`** (`DungeonMap`): Overrides `Map::Add` to perform instance entry checks, bind the player/group, and stop the reset schedule before calling the base `Add`.
*   **`Add#3`** (`Map`): The base implementation for adding a `Player*`.
*   **`Add#4`** (`Map`): Template declaration for adding generic objects.
*   **`Add#5`** (`Map`): Template implementation for adding generic `WorldObject`s (Creatures, GOs, etc.), handling grid loading and visibility.
*   **`Add#6`** (`Map`): Template specialization for adding `GenericTransport`s, which are tracked separately.
*   **`ExistingPlayerLogin`**: Handles the re-initialization of visibility for a player who is already on the map (e.g., after a disconnect/reconnect).
*   **`DeleteFromWorld`**: Deletes a player object from memory.
*   **`AddObjectToRemoveList`**: Adds an object to a deferred removal list, used for safe concurrent deletion.
*   **`RemoveAllObjectsInRemoveList`**: Processes the deferred removal list, actually deleting the objects.
*   **`Remove` (Player)**: Removes a player from the map, cleaning up visibility, removing them from the grid, and deleting the player object if requested.
*   **`Remove#2`** (`DungeonMap`): Overrides `Map::Remove` to log the removal and schedule the instance reset if the last player leaves.
*   **`Remove#3`** (`Map`): The base implementation for removing a `Player*`.
*   **`Remove#4`** (`Map`): Template declaration for removing generic objects.
*   **`Remove#5`** (`Map`): Template implementation for removing generic `WorldObject`s, handling active object status and respawn time saving.
*   **`Remove#6`** (`Map`): Template specialization for removing `GenericTransport`s.
*   **`AddToGrid`**: Template function to add an object to a specific grid cell.
*   **`AddToGrid#2`**: Specialization for `Creature`, distinguishing between pets (world objects) and regular creatures (grid objects).
*   **`AddToGrid#3`**: Specialization for `Player`.
*   **`AddToGrid#4`**: Specialization for `Corpse`, distinguishing between bones and fresh corpses.
*   **`RemoveFromGrid`**: Template function to remove an object from a specific grid cell.
*   **`RemoveFromGrid#2`**: Specialization for `Creature`.
*   **`RemoveFromGrid#3`**: Specialization for `Player`.
*   **`operator()`** (`ActiveObjectsGridLoader`): Functor implementation for iterating over `CreatureDataPair` to load grids for active creatures.
*   **`operator()#2`** (`ActiveObjectsGridLoader`): Functor implementation for iterating over `GameObjectDataPair` to load grids for active game objects.

### Relocation and Movement

*   **`PlayerRelocation`**: Updates a player's position, handling grid/cell transitions, updating visibility, and notifying the camera.
*   **`DoPlayerGridRelocation`**: Similar to `PlayerRelocation` but used for extrapolation or forced grid moves without full visibility updates.
*   **`CreatureRelocation`**: Updates a creature's position. If the new grid is not loaded, it attempts to move the creature to its respawn point.
*   **`CreatureCellRelocation`**: Handles the low-level cell transition for a creature.
*   **`CreatureRespawnRelocation`**: Moves a creature to its respawn coordinates, used when a creature moves into an unloaded area.
*   **`AddRelocatedUnit`**: Adds a unit to the list of units requiring visibility updates after relocation.
*   **`RemoveRelocatedUnit`**: Removes a unit from the relocation update list.
*   **`AddUnitToMovementUpdate`**: Adds a unit to the list for asynchronous motion updates.
*   **`RemoveUnitFromMovementUpdate`**: Removes a unit from the motion update list.

### Updates and Processing

*   **`Update`** (`Map`): The main update loop for the map. It processes sessions, players, cells, object updates, visibility, corpses, scripts, and weather. It also handles dynamic adjustment of visibility distances on continents to maintain performance.
*   **`Update#2`** (`DungeonMap`): Overrides `Map::Update` to call the base update.
*   **`Update#3`** (`BattleGroundMap`): Overrides `Map::Update` to also update the associated `BattleGround` object.
*   **`DoUpdate`**: Wrapper for `Update` that calculates the time difference.
*   **`UpdateSync`**: Updates transports synchronously.
*   **`UpdatePlayers`**: Iterates through all players on the map, updating their state. Skips inactive players on continents unless a threshold is reached.
*   **`UpdateCells`**: Updates objects in cells around active players and objects. Uses multi-threading on continents.
*   **`UpdateCellsAroundObject`**: Updates objects in cells surrounding a specific object.
*   **`MarkCellsAroundObject`**: Marks cells around an object for asynchronous update.
*   **`UpdateActiveCellsSynch` / `UpdateActiveCellsAsynch`**: Synchronous and asynchronous implementations for updating active cells.
*   **`UpdateActiveCellsCallback`**: Callback for thread-based cell updates.
*   **`ProcessSessionPackets`**: Processes incoming packets for all players on the map.
*   **`UpdateSessionsMovementAndSpellsIfNeeded`**: Triggers packet processing if enough time has passed.
*   **`SendObjectUpdates`**: Sends update packets for objects that have changed fields. Uses multi-threading.
*   **`UpdateVisibilityForRelocations`**: Updates visibility for units that have relocated. Uses multi-threading.
*   **`UpdateScriptedEvents`**: Processes timed scripted events.
*   **`ScriptsProcess`**: Executes scheduled database scripts.
*   **`UpdateObjectVisibility`**: Updates the visibility of objects for a specific player or object.
*   **`UpdateActiveObjectVisibility`**: Updates visibility for active non-player objects.
*   **`UpdateActiveObjectVisibility#2`**: Overload taking a `visibleGuids` set to track visibility.
*   **`UpdateActiveObjectVisibility#3`**: Overload taking both `visibleGuids` and `UpdateData` for compressed packet building.
*   **`AddUpdateObject`**: Adds an object to the list for client-side field updates.
*   **`RemoveUpdateObject`**: Removes an object from the client-side field update list.

### Visibility and Communication

*   **`MessageBroadcast`**: Sends a packet to all players within visibility range of a source object.
*   **`MessageBroadcast#2`**: Overload for broadcasting from a generic `WorldObject`.
*   **`MessageDistBroadcast`**: Sends a packet to all players within a specific distance of a source object.
*   **`MessageDistBroadcast#2`**: Overload for broadcasting from a generic `WorldObject`.
*   **`SendToPlayers`**: Sends a packet to all players on the map, optionally filtered by team.
*   **`SendToPlayersInZone`**: Sends a packet to all players in a specific zone.
*   **`SendDefenseMessage`**: Sends a defense message packet to all players.
*   **`SendMonsterTextToMap`**: Broadcasts monster chat text to all players.
*   **`PlayDirectSoundToMap`**: Plays a sound for all players in a specific zone or the entire map.
*   **`GetVisibilityDistance` / `GetGridActivationDistance`**: Returns the current visibility and activation distances.
*   **`SendInitSelf` / `SendInitTransports` / `SendRemoveTransports`**: Sends initial or removal data for transports and the player themselves.

### Querying Objects

*   **`GetPlayer`**: Retrieves a player by GUID, ensuring they are on this map.
*   **`GetGameObject`**: Retrieves a game object by GUID.
*   **`GetCreature`**: Retrieves a creature by GUID.
*   **`GetPet`**: Retrieves a pet by GUID.
*   **`GetDynamicObject`**: Retrieves a dynamic object by GUID.
*   **`GetCorpse`**: Retrieves a corpse by GUID.
*   **`GetAnyTypeCreature`**: Retrieves a creature or pet by GUID.
*   **`GetUnit`**: Retrieves a unit (player, creature, or pet) by GUID.
*   **`GetWorldObject`**: Retrieves any world object by GUID.
*   **`GetWorldObjectOrPlayer`**: Retrieves a world object or a player (anywhere) by GUID.
*   **`GetTransport` / `GetElevatorTransport`**: Retrieves transports by GUID.
*   **`GetPlayers`**: Returns the list of players on the map.
*   **`HavePlayers` / `HaveRealPlayers`**: Checks if there are any players (or non-bot players) on the map.
*   **`GetPlayersCountExceptGMs`**: Counts non-GM players.
*   **`GetInstanceData`**: Retrieves the instance-specific data object.
*   **`GetInstanceData#2`**: Const overload retrieving the instance-specific data object.
*   **`GetPersistentState`**: Retrieves the persistent state manager for the map.
*   **`GetPersistanceState`** (`BattleGroundMap`): Returns the `BattleGroundPersistentState`.
*   **`GetPersistanceState#2`** (`DungeonMap`): Returns the `DungeonPersistentState`.
*   **`GetPersistanceState#3`** (`WorldMap`): Returns the `WorldPersistentState`.
*   **`GetWeatherSystem`**: Retrieves the weather system.
*   **`GetCreatureLinkingHolder`**: Retrieves the holder for linked creatures.
*   **`GetTerrain`**: Retrieves the terrain data.
*   **`GetId` / `GetInstanceId` / `GetMapName` / `GetMapEntry`**: Basic metadata accessors.
*   **`IsDungeon` / `IsRaid` / `IsBattleGround` / `IsContinent` / `IsNonRaidDungeon` / `Instanceable`**: Type checkers.
*   **`GetScriptedMapEvent`**: Retrieves a running scripted event by ID.
*   **`GetScriptedMapEvent#2`**: Const overload retrieving a running scripted event by ID.
*   **`GetScriptId`**: Returns the script ID associated with the map.
*   **`GetCreateTime`**: Returns the timestamp when the map was created.
*   **`GetLastPlayerLeftTime`**: Returns the time when the last player left.

### Scripting

*   **`ScriptsStart`**: Adds a set of scripts to the execution queue.
*   **`ScriptCommandStart`**: Adds a single script command to the queue.
*   **`ScriptCommandStartDirect`**: Executes a script command immediately.
*   **`TerminateScript`**: Removes a script from the queue.
*   **`FindScriptInitialTargets` / `FindScriptFinalTargets`**: Resolves source and target objects for scripts.
*   **`StartScriptedEvent`**: Starts a complex, timed scripted event.
*   **`StartAreaTriggerScript`**: Starts a script triggered by an area trigger.
*   **`SendEventToMainTargets` / `SendEventToAdditionalTargets` / `SendEventToAllTargets`**: Sends events to creatures involved in a scripted event.
*   **`GetSourceObject` / `GetTargetObject`**: Retrieves objects involved in a scripted event.
*   **`UpdateEvent`** (`ScriptedEvent`): Updates the state of a scripted event, checking for expiration or condition satisfaction.
*   **`EndEvent`** (`ScriptedEvent`**: Ends a scripted event, triggering success or failure scripts.

### Collision and Pathfinding

*   **`GetHeight`**: Gets the terrain height at a specific point.
*   **`isInLineOfSight`**: Checks if two points have a clear line of sight, considering static VMaps and dynamic objects.
*   **`GetLosHitPosition`**: Finds the first collision point along a line.
*   **`GetWalkHitPosition`**: Finds a walkable position along a path, considering navigation meshes.
*   **`GetWalkRandomPosition`**: Finds a random walkable position within a radius.
*   **`GetSwimRandomPosition`**: Finds a random swimable position within a radius.
*   **`FindCollisionModel`**: Finds the static VMap model colliding with a line.
*   **`FindDynamicObjectCollisionModel`**: Finds the dynamic object colliding with a line.
*   **`InsertGameObjectModel` / `RemoveGameObjectModel` / `ContainsGameObjectModel`**: Manages dynamic object models in the collision tree.
*   **`GetDynamicObjectHitPos` / `GetDynamicTreeHeight` / `CheckDynamicTreeLoS`**: Queries the dynamic collision tree.
*   **`Balance`**: Balances the dynamic collision tree.

### Instance and Dungeon Specifics

*   **`CreateInstanceData`**: Loads or creates instance-specific data from the database.
*   **`BindToInstanceOrRaid`**: Binds players to an instance or raid.
*   **`TeleportAllPlayersTo`**: Teleports all players on the map to a specified location (homebind or BG entry).
*   **`CanEnter`** (`Map`): Base implementation always returns true.
*   **`CanEnter#2`** (`DungeonMap`): Overrides to check player caps, reset states, and combat restrictions.
*   **`CanEnter#3`** (`BattleGroundMap`): Overrides to check if the player is assigned to this battleground instance.
*   **`Add` (DungeonMap/BattleGroundMap)**: Overrides `Map::Add` to handle instance-specific entry logic.
*   **`Remove` (DungeonMap/BattleGroundMap)**: Overrides `Map::Remove` to handle instance-specific exit logic.
*   **`Reset`** (`DungeonMap`): Resets the dungeon instance.
*   **`PermBindAllPlayers`** (`DungeonMap`): Permanently binds all players to the instance.
*   **`SendResetWarnings`** (`DungeonMap`): Sends reset warnings to players.
*   **`SetResetSchedule`** (`DungeonMap`): Schedules the instance reset.
*   **`GetMaxPlayers`** (`DungeonMap`): Returns the maximum number of players allowed.
*   **`BindPlayerOrGroupOnEnter`** (`DungeonMap`): Binds a player or group to the instance upon entry.
*   **`GetPersistanceState`** (`DungeonMap`/`BattleGroundMap`): Returns the specific persistent state type.
*   **`Update`** (`BattleGroundMap`): Overrides `Map::Update` to also update the battleground object.
*   **`SetUnload`** (`BattleGroundMap`): Sets the unload timer for a battleground.
*   **`~DungeonMap`**: Destructor for `DungeonMap`.
*   **`~BattleGroundMap`**: Destructor for `BattleGroundMap`.

### Corpses and Bones

*   **`AddCorpseToRemove`**: Adds a corpse to the removal list.
*   **`RemoveCorpses`**: Processes the corpse removal list, spawning bones if necessary.
*   **`RemoveBones`**: Removes bones from the list.
*   **`RemoveOldBones`**: Removes expired bones.

### Spawning

*   **`SummonGameObject`**: Summons a game object at a specific location.
*   **`LoadCreatureSpawn`**: Loads a creature from the database.
*   **`LoadCreatureSpawnWithGroup`**: Loads a creature and its group.
*   **`LoadGameObjectSpawn`**: Loads a game object from the database.

### Other

*   **`GenerateLocalLowGuid`**: Generates a unique low GUID for an object.
*   **`SetWeather`**: Sets the weather for a zone.
*   **`CrashUnload`**: Emergency unload routine for crashed maps.
*   **`MarkAsCrashed` / `IsCrashed`**: Flags the map as crashed.
*   **`IsUpdateFinished` / `MarkNotUpdated`**: Tracks update completion.
*   **`SetUpdateDiffMod` / `GetUpdateDiffMod`**: Modifies update time differences.
*   **`GetCurrentClockTime`**: Returns the current map clock time.
*   **`PrintInfos`**: Prints performance information.
*   **`ShouldUpdateMap`**: Determines if the map should be updated based on activity.
*   **`resetMarkedCells` / `isCellMarked` / `markCell`**: Manages cell marking for updates.
*   **`SetTimer`**: Sets the grid expiry timer.
*   **`IsUnloading`**: Returns whether the map is currently in the process of unloading.

## Cross-Unit Boundaries

*   **MapManager**: Creates and destroys maps, updates grid states, and manages continent updates. `Map` calls `MapManager` to mark continent updates as finished and to update grid states. `MapManager` calls `Map` to create, delete, and update maps.
*   **GridMap**: Provides terrain and VMap data. `Map` loads and unloads terrain and VMap data through `GridMap`.
*   **ObjectMgr**: Provides creature and game object data. `Map` uses `ObjectMgr` to spawn active objects and load spawns.
*   **Player**: Represents a player character. `Map` adds, removes, and updates players. `Player` calls `Map` to relocate, send messages, and query objects.
*   **Creature**: Represents a non-player character. `Map` adds, removes, and relocates creatures. `Creature` calls `Map` to relocate and query objects.
*   **GameObject**: Represents a static or dynamic object. `Map` adds, removes, and queries game objects. `GameObject` calls `Map` to relocate and query objects.
*   **Corpse**: Represents a dead body. `Map` adds, removes, and manages corpses and bones.
*   **Transport**: Represents a moving vehicle. `Map` adds, removes, and updates transports.
*   **ScriptMgr**: Manages scripts. `Map` calls `ScriptMgr` to increase/decrease scheduled script counts and to create instance data. `ScriptMgr` calls `Map` to execute scripts.
*   **Conditions**: Evaluates conditions. `Map` uses `Conditions` to evaluate script conditions.
*   **World**: Provides global configuration and game time. `Map` uses `World` to get configuration values and game time.
*   **Log**: Logging facility. `Map` uses `Log` to log errors and debug information.
*   **ThreadPool**: Provides multi-threading support. `Map` uses `ThreadPool` for parallel updates on continents.
*   **ObjectAccessor**: Manages object access. `Map` uses `ObjectAccessor` to remove objects and add corpses to grids.
*   **Weather**: Manages weather effects. `Map` creates and updates a `WeatherSystem`.
*   **MapPersistentStateMgr**: Manages persistent state. `Map` creates and uses persistent state objects.
*   **VMapFactory / MMapFactory**: Provide VMap and MMap managers for collision and pathfinding. `Map` uses these for line-of-sight and pathfinding queries.
*   **DynamicTree**: Manages dynamic object collisions. `Map` inserts, removes, and queries dynamic object models.
*   **ChatHandler**: Provides chat commands. `Map` uses `ChatHandler` to print performance information.
*   **BattleGround**: Represents a battleground. `BattleGroundMap` updates the `BattleGround` object.
*   **Group**: Represents a player group. `DungeonMap` uses `Group` to bind players to instances.

---

<!-- machine-true, projected from graph.json -->

## Map — Map.Main

*Source:* Map.cpp, Map.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ~Map | dtor | GridMap/UnloadTerrain, Log.Main/Out, MapPersistentStateMgr/SetUsedByMapState, ScriptMgr/DecreaseScheduledScriptCount#2, TerrainInfo/GetMapId | — | — |
| GetTransport | method | ObjectGuid/GetEntry | MoveSplineInit/Launch, Player.Main/LoadFromDB, WorldSession.MovementHandler/HandleMoverRelocation | — |
| GetElevatorTransport | method | Object/GetObjectGuid, ObjectGuid/operator== | — | — |
| LoadMapAndVMap | method | GridMap/Load | — | — |
| Map | ctor | GridMap/LoadTerrain, MapPersistentStateMgr/AddPersistentState, MapPersistentStateMgr/SetUsedByMapState, ObjectMgr/GetFirstTemporaryCreatureLowGuid, ObjectMgr/GetFirstTemporaryGameObjectLowGuid, shared_Util/getMSTime, ThreadPool/ThreadPool, TransportMgr/SpawnTransportsOnMap, Weather/WeatherSystem, World/getConfig#4 | — | — |
| ActiveObjectsGridLoader | ctor | — | — | — |
| operator()#2 | method | Cell/Cell#2, GridDefines/ComputeCellPair | — | — |
| operator() | method | Cell/Cell#2, GridDefines/ComputeCellPair | — | — |
| SpawnActiveObjects | method | MapPersistentStateMgr/InitPools | MapManager/CreateBattleGroundMap, MapManager/CreateDungeonMap, MapManager/CreateMap | — |
| InitVisibilityDistance#3 | method | World/GetMaxVisibleDistanceOnContinents | MapManager/InitializeVisibilityDistanceInfo | — |
| AddToGrid#3 | method | Cell/CellX, Cell/CellY, WorldObject.Object/SetCurrentCell | — | — |
| AddToGrid#4 | method | Cell/CellX, Cell/CellY | — | — |
| AddToGrid | method | Cell/CellX, Cell/CellY, Corpse/GetType | — | — |
| AddToGrid#2 | method | Cell/CellX, Cell/CellY, Creature.Main/IsPet, WorldObject.Object/SetCurrentCell | — | — |
| RemoveFromGrid#3 | method | Cell/CellX, Cell/CellY | — | — |
| RemoveFromGrid | method | Cell/CellX, Cell/CellY, Corpse/GetType | — | — |
| RemoveFromGrid#2 | method | Cell/CellX, Cell/CellY, Creature.Main/IsPet | — | — |
| DeleteFromWorld | method | ObjectAccessor/RemoveObject#3 | WorldSession.Main/LogoutPlayer | — |
| EnsureGridCreated | method | Errors/PrintStacktraceAndThrow, World/getConfig | — | — |
| Map#2 | decl | — | — | — |
| operator= | decl | — | — | — |
| CanUnload | method | — | MapManager/Update | — |
| EnsureGridLoadedAtEnter | method | Cell/CellX, Cell/CellY, Cell/GridX, Cell/GridY, Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out, Player.Main/GetName | — | — |
| EnsureGridLoaded | method | Cell/CellX, Cell/CellY, Cell/GridX, Cell/GridY, Errors/PrintStacktraceAndThrow, Log.Main/Out, ObjectAccessor/AddCorpsesToGrid, ObjectGridLoader/LoadN, ObjectGridLoader/ObjectGridLoader | — | — |
| GetVisibilityDistance | method | — | Camera/UpdateVisibilityForOwner, ChatHandler.CreatureCommands/HandleRespawnCommand, ChatHandler.MiscCommands/HandleInstanceContinentsCommand, Corpse/IsVisibleForInState, DynamicObject/IsVisibleForInState, GameObject/IsVisibleForInState, Player.Main/SetLongSight, Unit.Main/CombatStopInRange, Unit.Main/InterruptAttacksOnMe, Unit.Main/InterruptSpellsCastedOnMe, WorldObject.Object/BuildUpdateData#2, WorldObject.Object/DestroyForNearbyPlayers, WorldObject.Object/IsWithinVisibilityDistanceOf, WorldObject.Object/RespawnNearCreaturesByEntry, WorldObject.Object/SendMessageToSetExcept | — |
| GetGridActivationDistance | method | — | ChatHandler.MiscCommands/HandleInstanceContinentsCommand, Player.Main/SetPosition, Player.Main/TeleportTo, Player.Main/Update, WorldSession.MovementHandler/HandleSetActiveMoverOpcode | — |
| IsRemovalGrid | method | — | ObjectMgr/AddCreData | — |
| IsLoaded | method | — | Creature.Main/operator()#4, GameObject/operator()#4, ObjectMgr/AddGOData, ObjectMgr/MoveCreData, PoolManager/Spawn1Object, PoolManager/Spawn1Object#2 | — |
| GetUnloadLock | method | — | — | — |
| ForceLoadGridsAroundPosition | method | Cell/Cell#2, GridDefines/ComputeCellPair | MapManager/CreateNewInstancesForPlayers | — |
| SetUnloadLock | method | — | — | — |
| ResetGridExpiry | method | — | GridStates/Update, GridStates/Update#2, GridStates/Update#4 | — |
| GetGridExpiry | method | — | — | — |
| GetCreateTime | method | — | ChatHandler.ServerCommands/HandleListMapsCommand, game_Battlegrounds_BattleGround/Update | — |
| LoadGrid | method | Cell/GridX, Cell/GridY | instance_scarlet_monastery/SetData | — |
| GetId | method | — | boss_gluth/SpellHit, boss_thaddius/OnPeriodicTrigger, boss_thaddius/OnPeriodicTrigger#2, ChatHandler.CreatureCommands/HandleEscortHideWpCommand, ChatHandler.CreatureCommands/HandleEscortShowWpCommand, ChatHandler.CreatureCommands/HandleNpcAddCommand, ChatHandler.ObjectCommands/HandleGameObjectAddCommand, Conditions/Evaluate, Conditions/Meets, Creature.Main/FallGround, Creature.Main/SetInCombatWithZone, GameObject/Create, game_Group_Group/AddMember, game_Group_Group/_homebindIfInstance, GridStates/Update#2, GridStates/Update#4, InstanceData/CheckConditionCriteriaMeet, InstanceData/SaveToDB, InstanceStatistics/IncrementKillCounter, instance_blackfathom_deeps/Load, instance_blackfathom_deeps/SetData, instance_blackrock_depths/Load, instance_blackrock_depths/SetData, instance_blackrock_spire/Load, instance_blackrock_spire/SetData, instance_blackwing_lair/Load, instance_blackwing_lair/SetData, instance_dire_maul/Load, instance_dire_maul/SetData, instance_gnomeregan/Load, instance_gnomeregan/SetData, instance_maraudon/SetData, instance_molten_core/Load, instance_molten_core/SetData, instance_naxxramas.Main/Load, instance_naxxramas.Main/SetData, instance_razorfen_downs/Load, instance_razorfen_downs/SetData, instance_razorfen_kraul/Load, instance_razorfen_kraul/SetData, instance_ruins_of_ahnqiraj/Load, instance_ruins_of_ahnqiraj/SetData, instance_scarlet_monastery/Load, instance_scarlet_monastery/SetData, instance_scholomance/Load, instance_scholomance/SetData, instance_shadowfang_keep/Load, instance_shadowfang_keep/SetData, instance_sunken_temple/Load, instance_sunken_temple/SetData, instance_temple_of_ahnqiraj/Load, instance_temple_of_ahnqiraj/SetData, instance_uldaman/Load, instance_uldaman/SetData, instance_wailing_caverns/Load, instance_wailing_caverns/SetData, instance_zulfarrak/Load, instance_zulfarrak/SetData, instance_zulgurub/Load, instance_zulgurub/SetData, MapManager/DeleteTestMap, MapManager/ScheduleInstanceSwitch, MapPersistentStateMgr/_ResetInstance, ObjectGridLoader/LoadN, ObjectGridLoader/Visit#2, ObjectGridLoader/Visit#4, ObjectGridLoader/Visit#8, Player.Main/SendLoot, Player.Main/SwitchInstance, Player.Main/Update, ScriptedInstance/GetSingleCreatureFromStorage, ScriptedInstance/GetSingleGameObjectFromStorage, ScriptedInstance/StartNextDialogueText, ScriptMgr/DoOrSimulateScriptTextForMap, TransportMgr/CreateTransport, TransportMgr/SpawnTransportsOnMap, WorldObject.Object/SetMap, WorldSession.Main/LogoutPlayer, WorldSession.MovementHandler/ExecuteTeleportNear, ZoneScript/AddCreature, ZoneScript/AddObject, ZoneScript/SetCapturePointData | — |
| Add#3 | method | AuraRemovalMgr/PlayerEnterMap, Cell/Cell#2, Cell/CellX, Cell/CellY, Cell/GridX, Cell/GridY, GridDefines/ComputeCellPair, Player.Main/AddToWorld, Player.Main/GetMapRef, Player.Main/GetSession, Player.Main/IsBeingTeleportedFar, PlayerBroadcaster/SetInstanceId, Unit.Main/SetSplineDonePending, ViewPoint/Event_AddedToWorld, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetViewPoint, WorldObject.Object/LoadMapCellsAround, WorldObject.Object/SetMap, WorldSession.Main/ClearIncomingPacketsByType, WorldSession.Main/PlayerLoading, ZoneScript/OnPlayerEnter#2 | Player.Main/SwitchInstance, PlayerBotAI/SpawnNewPlayer, WorldSession.CharacterHandler/HandlePlayerLogin, WorldSession.MovementHandler/HandleMoveWorldportAck | — |
| GetInstanceId | method | — | ChatHandler.PlayerBotMgr/HandlePartyBotAddCommand, ChatHandler.PlayerBotMgr/HandlePartyBotCloneCommand, ChatHandler.PlayerBotMgr/HandlePartyBotLoadCommand, ChatHandler.TeleportCommands/HandleGroupgoCommand, InstanceData/SaveToDB, instance_blackfathom_deeps/Load, instance_blackfathom_deeps/SetData, instance_blackrock_depths/Load, instance_blackrock_depths/SetData, instance_blackrock_spire/Load, instance_blackrock_spire/SetData, instance_blackwing_lair/Load, instance_blackwing_lair/SetData, instance_dire_maul/Load, instance_dire_maul/SetData, instance_gnomeregan/Load, instance_gnomeregan/SetData, instance_maraudon/SetData, instance_molten_core/Load, instance_molten_core/SetData, instance_naxxramas.Main/Load, instance_naxxramas.Main/SetData, instance_razorfen_downs/Load, instance_razorfen_downs/SetData, instance_razorfen_kraul/Load, instance_razorfen_kraul/SetData, instance_ruins_of_ahnqiraj/Load, instance_ruins_of_ahnqiraj/SetData, instance_scarlet_monastery/Load, instance_scarlet_monastery/SetData, instance_scholomance/Load, instance_scholomance/SetData, instance_shadowfang_keep/Load, instance_shadowfang_keep/SetData, instance_sunken_temple/Load, instance_sunken_temple/SetData, instance_temple_of_ahnqiraj/Load, instance_temple_of_ahnqiraj/SetData, instance_uldaman/Load, instance_uldaman/SetData, instance_wailing_caverns/Load, instance_wailing_caverns/SetData, instance_zulfarrak/Load, instance_zulfarrak/SetData, instance_zulgurub/Load, instance_zulgurub/SetData, MapManager/DeleteTestMap, ObjectAccessor/AddCorpsesToGrid, ObjectGridLoader/IsEnabledOnMap, ObjectGridLoader/IsEnabledOnMap#2, ObjectGridLoader/LoadHelper, TransportMgr/CreateTransport, WorldObject.Object/SetMap | — |
| CanEnter#3 | method | — | Player.Main/ExecuteTeleportFar, Player.Main/TeleportTo | — |
| GetMapEntry | method | — | ChatHandler.PlayerBotMgr/PartyBotAddRequirementCheck, MapPersistentStateMgr/GetMapEntry, Pet.Main/SetDeathState | — |
| Instanceable | method | — | ChatHandler.TeleportCommands/HandleGroupgoCommand, Creature.Main/LogDeath, GameObject/Update, InstanceData/SaveToDB, MapManager/DeleteInstance, MapManager/Update, ObjectAccessor/AddCorpsesToGrid, ObjectMgr/AddCreData, ObjectMgr/AddGOData, ObjectMgr/MoveCreData, Player.Main/Update, TransportMgr/CreateTransport, WorldObject.Object/IsWithinLootXPDist, WorldSession.NPCHandler/SendBindPoint | — |
| IsNonRaidDungeon | method | — | WorldSession.Main/LogoutPlayer | — |
| IsDungeon | method | — | BasicAI/IsProximityAggroAllowedFor, boss_four_horsemen/AggroRadius, boss_four_horsemen/MoveInLineOfSight, boss_heigan/MoveInLineOfSight, boss_loatheb/MoveInLineOfSight, boss_loatheb/MoveInLineOfSight#2, boss_maexxna/MoveInLineOfSight, boss_razuvious/MoveInLineOfSight, ChatHandler.PlayerBotMgr/PartyBotAddRequirementCheck, ChatHandler.TeleportCommands/HandleGonameCommand, Creature.Main/CanRespondToCallForHelpAgainst, Creature.Main/IsOutOfThreatArea, Creature.Main/SetInCombatWithZone, CreatureLinkingMgr/ProcessSlave, game_Group_Group/AddMember, game_Group_Group/ResetInstances, game_Group_Group/_homebindIfInstance, MapManager/GetNumInstances, MapManager/GetNumPlayersInInstances, MapPersistentStateMgr/_ResetInstance, Player.Main/GiveLevel, Player.Main/IsInInterFactionMode, Player.Main/ResetInstance, Player.Main/SetBattleGroundEntryPoint, Player.Main/Update, ScriptedAI/DoTeleportAll, sunken_temple/npc_malfurionAI, Unit.Main/SetInCombatState, wailing_caverns/UpdateEscortAI, WorldObject.Object/SetZoneScript | — |
| IsRaid | method | — | Player.Main/GiveLevel, WorldObject.Object/IsWithinLootXPDist | — |
| IsBattleGround | method | — | ChatHandler.TeleportCommands/HandleGonameCommand, ChatHandler.TeleportCommands/HandleNamegoCommand, CreatureGroups/Respawn, GridNotifiers/operator()#2, GridNotifiers/operator()#3, HonorMgr/Add, InstanceData/SaveToDB, MapManager/CreateBattleGroundMap, Player.Main/CreateCorpse, Player.Main/SetBattleGroundEntryPoint, scripts_battlegrounds_battleground/CorpseRemoved, ThreatListCopier.battleground_alterac/av_world_boss_baseai, ThreatListCopier.battleground_alterac/JustDied#3, ThreatListCopier.battleground_alterac/JustRespawned#3, ThreatListCopier.battleground_alterac/SelectCreatureEntry, ThreatListCopier.battleground_alterac/UpdateAI#9, WorldObject.Object/SetZoneScript | — |
| IsContinent | method | — | Map.ScriptCommands/ScriptCommand_PlaySound, ObjectGridLoader/IsEnabledOnMap, ObjectGridLoader/IsEnabledOnMap#2, Player.Main/Update, ScriptMgr/DoScriptText, TransportMgr/CreateTransport, Unit.Main/Update, WorldSession.MovementHandler/ExecuteTeleportNear | — |
| GetPersistentState | method | — | ChatHandler.HardcodedEvents/GetAliveCountAndUpdateRespawnTime, ChatHandler.LookupCommands/HandlePoolListCommand, ChatHandler.MiscCommands/HandlePoolInfoCommand, ChatHandler.MiscCommands/HandlePoolSpawnsCommand, ChatHandler.MiscCommands/HandlePoolUpdateCommand, ChatHandler.ObjectCommands/HandleGameObjectTargetCommand, Creature.Main/LoadFromDB, Creature.Main/Respawn, Creature.Main/SaveRespawnTime, Creature.Main/Update, CreatureLinkingMgr/IsRespawnReady, GameObject/Delete, GameObject/JustDespawnedWaitingRespawn, GameObject/LoadFromDB, GameObject/Respawn, GameObject/SaveRespawnTime, ObjectGridLoader/Visit#2, ObjectGridLoader/Visit#4, ObjectMgr/operator(), ObjectMgr/operator()#2 | — |
| resetMarkedCells | method | — | — | — |
| isCellMarked | method | — | — | — |
| markCell | method | — | — | — |
| HavePlayers | method | — | — | — |
| ExistingPlayerLogin | method | Cell/Cell#2, Cell/CellX, Cell/CellY, Cell/GridX, Cell/GridY, GridDefines/ComputeCellPair, PlayerBroadcaster/RemoveListener, Unit.Main/GetSpellAuraHolderMap, Unit.SpellAuras/UpdateAuraDuration, ViewPoint/Event_AddedToWorld, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetViewPoint | WorldSession.CharacterHandler/HandlePlayerLogin | — |
| GetPlayers | method | — | BattleGroundAV/CompleteQuestForAll, blackrock_depths/CheckForWipe, boss_ayamiss/UpdateAI#2, boss_buru/JustDied, boss_cthun/AggroRadius, boss_cthun/SelectRandomAliveNotStomach, boss_fankriss/UpdateAI, boss_four_horsemen/AggroRadius, boss_gluth/SpellHit, boss_gothik/HasLessPlayersPerSide, boss_gothik/SummonAdd, boss_nefarian/HandleClassCall, boss_nefarian/OnPeriodicTickEnd, boss_onyxia/CheckForTargetsInAggroRadius, boss_ossirian/UpdateAI#2, boss_razorgore/MortPhaseUn, boss_thaddius/DoPolarityShift, boss_thaddius/OnPeriodicTrigger, boss_thaddius/OnPeriodicTrigger#2, boss_urok/SpawnAtRune, boss_victor_nefarius/UpdateAI, ChatHandler.MiscCommands/HandleInstanceContinentsCommand, ChatHandler.PlayerBotMgr/PartyBotAddRequirementCheck, Creature.Main/SetInCombatWithZone, CreatureAI/ClearTargetIcon, eastern_plaguelands/Reset, instance_blackrock_depths/ReplacePrincessIfPossible, instance_blackrock_depths/SetData, instance_blackwing_lair/ApplyAura, instance_molten_core/Update, instance_naxxramas.Main/SetData, instance_stratholme/JoueurDansPiegeRat1, instance_stratholme/JoueurDansPiegeRat2, instance_stratholme/SetData, instance_stratholme/Update, instance_temple_of_ahnqiraj/JustDied, instance_temple_of_ahnqiraj/UpdateCThunWhisper, MapManager/GetNumPlayersInInstances, Player.Main/GiveLevel, ScriptedAI/DoTeleportAll, ScriptedInstance/DoUpdateWorldState, ScriptedInstance/GetPlayerInMap, scripts_battlegrounds_battleground/CorpseRemoved, ThreatListCopier.battleground_alterac/AV_NpcEventWorldBoss_A_AI, ThreatListCopier.battleground_alterac/AV_NpcEventWorldBoss_H_AI, ThreatListCopier.battleground_alterac/UpdateAI#8, Transport/SendCreateUpdateToMap, Transport/SendOutOfRangeUpdateToMap, wailing_caverns/UpdateEscortAI, WorldObject.Object/MonsterScriptToZone, WorldObject.Object/MonsterYellToZone, world_event_wareffort/AggroAllPlayerNear, world_event_wareffort/MoreThanOnePlayerNear | — |
| GetScriptedMapEvent | method | — | Conditions/Evaluate, Map.ScriptCommands/ScriptCommand_AddMapEventTarget, Map.ScriptCommands/ScriptCommand_EditMapEvent, Map.ScriptCommands/ScriptCommand_RemoveMapEventTarget, Map.ScriptCommands/ScriptCommand_SendMapEvent, Map.ScriptCommands/ScriptCommand_SetMapEventData, ScriptMgr/GetTargetByType, spell_item/OnCheckCast#5 | — |
| GetScriptedMapEvent#2 | method | — | Conditions/Evaluate | — |
| GetGameObject | method | — | ashenvale/JustDied, BattleBotAI.BattleBotWaypoints/StartNewPathToObjective, BattleGroundAV/HandleKillUnit, BattleGroundWS/RespawnFlagAfterDrop, blackrock_depths/DoGate, blackrock_depths/UpdateEscortAI#4, blackrock_depths/WaypointReached#4, boss_celebras_the_cursed/WaypointReached, boss_chromaggus/Reset, boss_chromaggus/UpdateAI, boss_razorgore/PhaseSwitch, boss_razorgore/SituationInitiale, boss_thermaplugg/UpdateAI, boss_urok/DespawnRunes, boss_urok/JustDied, boss_urok/SpawnAtRune, ChatHandler.Chat/GetGameObjectWithGuid, ChatHandler.ObjectCommands/getSelectedGameObject, ChatHandler.ObjectCommands/HandleGameObjectTargetCommand, Conditions/Evaluate, darkshore/GetPlayer, deadmines/GOHello_go_door_lever_dm, desolace/SetMagnetGuid, desolace/UpdateAI_corpse, dreadsteed_ritual/BreakNode, dreadsteed_ritual/EventEndedFail, dreadsteed_ritual/EventSecondPartStart, dreadsteed_ritual/GenerateGlyphAndNodeGuids, dreadsteed_ritual/gobjNextStep, dreadsteed_ritual/PhaseTwoEndedSuccess, DynamicObject/GetCaster, eastern_plaguelands/DespawnAll#2, felwood/UpdateAI, feralas/UpdateAI#4, GameObject/IsUseRequirementMet, GameObject/operator(), GameObject/operator()#4, game_Battlegrounds_BattleGround/DelObject, game_Battlegrounds_BattleGround/DoorClose, game_Battlegrounds_BattleGround/DoorOpen, game_Battlegrounds_BattleGround/SpawnBGObject, game_Battlegrounds_BattleGround/StartingEventDespawnDoors, gnomeregan/JustSummoned, gnomeregan/UpdateEscortAI, gnomeregan/WaypointReached, instance_blackrock_depths/HandleBarPatrol, instance_blackrock_depths/HandleBarPatrons, instance_blackrock_depths/Update, instance_blackrock_spire/DoSortRoomEventMobs, instance_blackwing_lair/SetData, instance_deadmines/OnCreatureDeath, instance_deadmines/SetData, instance_dire_maul/DoSortCristalsEventMobs, instance_dire_maul/OnPlayerEnter, instance_gnomeregan/SetData, instance_maraudon/SpewLarva, instance_molten_core/Update, instance_razorfen_downs/SetData, instance_ruins_of_ahnqiraj/SetData, instance_ruins_of_ahnqiraj/Update, instance_scarlet_monastery/SetData, instance_scholomance/OnCreatureDeath, instance_scholomance/SetData, instance_stratholme/SetData, instance_stratholme/Update, instance_stratholme/UpdateGoState, instance_sunken_temple/HandleStatueEventDone, instance_sunken_temple/OnCreatureDeath, instance_sunken_temple/ProcessStatueUsed, instance_sunken_temple/SetData, instance_sunken_temple/Update, instance_uldaman/SetData, Map.ScriptCommands/ScriptCommand_CloseDoor, Map.ScriptCommands/ScriptCommand_DespawnGameObject, Map.ScriptCommands/ScriptCommand_OpenDoor, Map.ScriptCommands/ScriptCommand_RespawnGameObject, MovementAnticheat/CheckFakeTransport, OutdoorPvPSI/ResetResourceCount, Player.Main/CheckDuelDistance, Player.Main/DuelComplete, Player.Main/GetGameObjectIfCanInteractWith, Player.Main/GetNextQuest, Player.Main/GetObjectByTypeMask, Player.Main/PrepareQuestMenu, Player.Main/SendLoot, Player.Main/SwitchInstance, Player.Main/TeleportTo, Player.Main/UpdateForQuestWorldObjects, PointMovementGenerator/MovementInform#3, PoolManager/Despawn1Object#2, PoolManager/ReSpawn1Object#2, razorfen_kraul/MovementInform, ScriptedInstance/DoOpenDoor, ScriptedInstance/DoResetDoor, ScriptedInstance/DoRespawnGameObject, ScriptedInstance/DoUseDoorOrButton, ScriptedInstance/GetSingleGameObjectFromStorage, ScriptMgr/GetTargetByType, silithus/Larksbane_DoAction, Spell.Effects/EffectDummy, Spell.Effects/EffectSummonObject, Spell.Main/AddGOTarget, Spell.Main/cancel, Spell.Main/DoAllEffectOnTarget, Spell.Main/GetAffectiveCasterObject, Spell.Main/GetCastingObject, Spell.Main/SendChannelStart, Spell.Main/SetTargetMap, Spell.Main/update, Spell.Main/UpdateOriginalCasterPointer, SpellCastTargetsInfo/Update, swamp_of_sorrows/WaypointReached, swamp_of_sorrows/WaypointStart, TargetedMovementGenerator/MovementInform, TargetedMovementGenerator/MovementInform#2, Unit.Main/Kill, Unit.SpellAuras/GetRealCaster, WorldSession.LootHandler/DoLootRelease, WorldSession.LootHandler/HandleAutostoreLootItemOpcode, WorldSession.LootHandler/HandleLootMasterGiveOpcode, WorldSession.LootHandler/HandleLootMoneyOpcode, WorldSession.SpellHandler/HandleGameObjectUseOpcode, ZoneScript/GetGameObject, zulfarrak/MovementInform | — |
| GetCreature | method | — | AiBotAI.Bridge/BridgeHandleAttackTarget, AiBotAI.Bridge/BridgeHandleInteractNpc, AiBotAI.Combat/HandleCombatStalemate, AiBotAI.Loot/DoAutoLoot, AiBotAI.Main/UpdateAI, AiBotDoctrineTeam/ResolveFocus, arena_challenge_ai/EnterCombat, arena_challenge_ai/EnterEvadeMode, BattleBotAI.BattleBotWaypoints/StartNewPathToObjective, blackrock_depths/CheckForWipe, blackrock_depths/DoPotionOfLoveIfCan, blackrock_depths/Reset#9, blackrock_depths/UpdateAI#4, blackrock_depths/UpdateEscortAI#2, boss_archaedas/UpdateAI, boss_arlokk/GetArlokkAI, boss_cthun/DespawnPortal, boss_cthun/setVisibility, boss_cthun/TeleportOnNewRandomTarget, boss_emperor_dagran_thaurissan/JustDied, boss_emperor_dagran_thaurissan/UpdateAI#2, boss_garr/Aggro, boss_garr/UpdateEvents, boss_golemagg/KillAdds, boss_golemagg/UpdateEvents#2, boss_gordok_king/UpdateAI, boss_gordok_king/UpdateAI#2, boss_herod/DespawnMyrmidons, boss_herod/EngageMyrmidons, boss_jandice_barov/UnsummonIllusions, boss_jindo/DespawnAllSummons, boss_jindo/UpdateAI#2, boss_lethon/UpdateAI, boss_loatheb/EnterEvadeMode, boss_majordomo_executus/Aggro, boss_majordomo_executus/Reset, boss_majordomo_executus/SummonedCreatureJustDied, boss_majordomo_executus/UpdateAI, boss_mandokir/DespawnRaptor, boss_mandokir/DespawnSpirits, boss_mandokir/KilledUnit, boss_mandokir/UpdateAI, boss_nefarian/EnterEvadeMode, boss_ossirian/JustDied, boss_ossirian/Reset, boss_ouro/UpdateAI, boss_razorgore/EnterCombat, boss_razorgore/PhaseSwitch, boss_razorgore/PopAdd, boss_razorgore/SituationInitiale, boss_razorgore/UpdateAI#2, boss_sartura/LeashEncounter, boss_sartura/LeashEncounter#2, boss_thermaplugg/JustReachedHome, boss_thermaplugg/UpdateAI, boss_tomb_of_seven/GetDwarfForPhase, boss_vaelastrasz/UpdateAI, boss_venoxis/Reset, burning_steppes/GetSpeakerByEntry, burning_steppes/JustDidDialogueStep, burning_steppes/WaypointReached, ChatHandler.CreatureCommands/HandleEscortHideWpCommand, ChatHandler.CreatureCommands/HandleNpcDeleteCommand, ChatHandler.CreatureCommands/HandleNpcGroupAddCommand, ChatHandler.CreatureCommands/HandleNpcGroupAddRelCommand, ChatHandler.CreatureCommands/HandleNpcGroupLinkCommand, ChatHandler.CreatureCommands/HandleNpcMoveHelperCommand, ChatHandler.CreatureCommands/HandleNpcWhisperCommand, ChatHandler.CreatureCommands/HandleWpShowCommand, ChatHandler.HardcodedEvents/Disable#5, ChatHandler.HardcodedEvents/GetActiveZones, ChatHandler.HardcodedEvents/GetAliveCountAndUpdateRespawnTime, ChatHandler.HardcodedEvents/HandleActiveCity, ChatHandler.HardcodedEvents/HandleActiveZone, ChatHandler.HardcodedEvents/isActiveZone, ChatHandler.HardcodedEvents/LogNextZoneTime, ChatHandler.HardcodedEvents/SummonMouth, ChatHandler.HardcodedEvents/SummonPallid, ChatHandler.TeleportCommands/HandleGoCreatureCommand, Conditions/Evaluate, Creature.Main/IsInEvadeMode, Creature.Main/LoadFromDB, Creature.Main/operator(), CreatureGroups/ChooseCreatureId, CreatureGroups/DisbandGroup, CreatureGroups/DoForAllMembers, CreatureGroups/OnLeaveCombat, CreatureGroups/OnMemberAttackStart, CreatureGroups/OnMemberDied, CreatureGroups/OnRespawn, CreatureGroups/RemoveTemporaryLeader, CreatureGroups/RespawnAll, CreatureLinkingMgr/CanSpawn#2, CreatureLinkingMgr/DoCreatureLinkingEvent, CreatureLinkingMgr/ProcessSlaveGuidList, CreatureLinkingMgr/TryFollowMaster, desolace/CaravanFaction, desolace/DespawnCaravan, desolace/DoTalk, desolace/GiveQuest, desolace/JustSummoned, duskwood/DespawnWatcher, duskwood/JustDied#2, duskwood/KilledUnit, duskwood/SummonStitches, duskwood/WaypointReached, eastern_plaguelands/DespawnAll, eastern_plaguelands/DespawnGuid, eastern_plaguelands/DespawnTroopers, eastern_plaguelands/UpdateAI, eastern_plaguelands/UpdateAI#3, elemental_invasions/UpdateAI, felwood/WaypointReached#2, feralas/JustDied#3, feralas/UpdateAI#4, GameEventMgr.Main/operator(), GameObject/IsUseRequirementMet, game_Battlegrounds_BattleGround/SendYell2ToAll, game_Battlegrounds_BattleGround/SendYellToAll, game_Battlegrounds_BattleGround/SpawnBGCreature, game_Group_Group/SendLootStartRollsForPlayer, gnomeregan/JustDied, gnomeregan/JustDied#2, gnomeregan/StartQuest, gnomeregan/UpdateFollowerAI, hillsbrad_foothills/CheckHelcularSpawned, hillsbrad_foothills/UpdateAI, instance_blackfathom_deeps/DoSpawnMobs, instance_blackfathom_deeps/IsWaveEventFinished, instance_blackrock_depths/HandleBarPatrol, instance_blackrock_depths/HandleBarPatrons, instance_blackrock_depths/OnCreatureDeath, instance_blackrock_depths/ReplacePrincessIfPossible, instance_blackrock_depths/SetData, instance_blackrock_spire/AreaTrigger_at_ubrs_the_beast, instance_blackrock_spire/DespawnStadiumSpectators, instance_blackrock_spire/DoSortRoomEventMobs, instance_blackrock_spire/OnUse, instance_blackwing_lair/GOHello_go_orb_of_domination, instance_blackwing_lair/OnCreatureDeath, instance_blackwing_lair/OnCreatureEnterCombat, instance_blackwing_lair/RecalculateThreat, instance_blackwing_lair/RespawnEggs, instance_blackwing_lair/SetData, instance_deadmines/Update, instance_dire_maul/DoSortCristalsEventMobs, instance_dire_maul/OnCreatureDeath, instance_dire_maul/SetData, instance_dire_maul/UpdateAI#8, instance_gnomeregan/SetData, instance_maraudon/SetData, instance_maraudon/Update, instance_naxxramas.boss_kelthuzad/DespawnAllIntroCreatures, instance_naxxramas.Main/DespawnPortal, instance_naxxramas.Main/GetClosestAnchorForGoth, instance_naxxramas.Main/GetGothSummonPointCreatures, instance_naxxramas.Main/SetGothTriggers, instance_razorfen_kraul/SetData, instance_ruins_of_ahnqiraj/GetData64, instance_ruins_of_ahnqiraj/GiveRepAfterRajaxxDeath, instance_ruins_of_ahnqiraj/IsAnyBossInCombat, instance_ruins_of_ahnqiraj/OnCreatureEnterCombat, instance_ruins_of_ahnqiraj/OnCreatureEvade, instance_ruins_of_ahnqiraj/SetAndorovSquadImmunity, instance_ruins_of_ahnqiraj/SetAndorovSquadRespawnTime, instance_ruins_of_ahnqiraj/SetData, instance_ruins_of_ahnqiraj/Update, instance_scarlet_monastery/SetData, instance_shadowfang_keep/Update, instance_stratholme/MoveAbomnationMob, instance_stratholme/OnCreatureDeath, instance_stratholme/SetData, instance_stratholme/StartSlaugtherSquare, instance_stratholme/Update, instance_sunken_temple/DoSpawnAtalarionIfCan, instance_sunken_temple/ProcessStatueUsed, instance_sunken_temple/SetData, instance_sunken_temple/Update, instance_temple_of_ahnqiraj/UpdateStomachOfCthun, instance_uldaman/DespawnMinion, instance_uldaman/RespawnMinion, instance_uldaman/SetData, instance_uldaman/SetData64, instance_uldaman/Update, instance_wailing_caverns/SetData, instance_zulfarrak/IsWaveAllDead, instance_zulfarrak/MoveNPCIfAlive, instance_zulfarrak/SendAddsUpStairs, instance_zulgurub/ProcessEventId_event_summon_gahzranka, instance_zulgurub/SetData, instance_zulgurub/UpdateHakkarPowerStacks, mob_anubisath_sentinel/CallBuddiesToAttack, mob_anubisath_sentinel/GetOtherSentinels, mob_anubisath_sentinel/GiveBuddyMyList, mob_anubisath_sentinel/JustDied, mob_anubisath_sentinel/Reset, moonglade/DoDespawnSummoned, moonglade/EnterEvadeMode, moonglade/JustDied, moonglade/UpdateAI, moonglade/UpdateAI#2, moonglade/UpdateEscortAI, moonglade/WaypointReached, npcs_special/EndEvent, npcs_special/GetPatientSpawnPosition, npcs_special/SpellHit, npcs_special/UpdateAI#8, Player.Main/GetObjectByTypeMask, Player.Main/GetSelectedCreature, Player.Main/SendLoot, PointMovementGenerator/MovementInform#3, PoolManager/Despawn1Object, PoolManager/ReSpawn1Object, quest_stormwind_rendezvous/GetGuard, quest_stormwind_rendezvous/PokeRowe, quest_stormwind_rendezvous/UpdateAI, ruins_of_ahnqiraj/GetTuubidAI, ruins_of_ahnqiraj/GetTuubidAI#2, scourge_invasion/JustDied#3, scourge_invasion/OnRemoveFromWorld, ScriptedInstance/GetSingleCreatureFromStorage, ScriptedInstance/Update, ScriptMgr/GetTargetByType, searing_gorge/Reset, searing_gorge/UpdateAI, silithus/AbortScene, silithus/AddKaldoreiThreat, silithus/DoCastTriggerSpellOnEnemies, silithus/DoTimeStopArmy, silithus/DoUnsummonArmy, silithus/ResetEvent, silithus/ResetOtherNPCsPosition, silithus/UpdateAI#4, silithus/UpdateAI#7, Spell.Effects/EffectDummy, stormwind_city/DamageTaken#2, stormwind_city/JustDied, stormwind_city/Reset#2, stormwind_city/ResetThug, stormwind_city/UpdateAI, stratholme/UpdateAI#3, tanaris/UpdateFollowerAI, TargetedMovementGenerator/MovementInform, TargetedMovementGenerator/MovementInform#2, the_barrens/UpdateAI#2, ungoro_crater/Aggro#2, ungoro_crater/Aggro#3, ungoro_crater/DamageTaken, ungoro_crater/DamageTaken#2, ungoro_crater/DemonDespawn, ungoro_crater/EnterEvadeMode, ungoro_crater/JustDied, ungoro_crater/JustReachedHome, ungoro_crater/UpdateAI#2, ungoro_crater/UpdateAI#3, Unit.Main/GetOwnerCreature, Unit.Main/GetTotem, Unit.Main/Kill, WaypointMovementGenerator/GetResetPosition#2, WaypointMovementGenerator/StartMove, WorldSession.BattleGroundHandler/HandleAreaSpiritHealerQueryOpcode, WorldSession.BattleGroundHandler/HandleAreaSpiritHealerQueueOpcode, WorldSession.BattleGroundHandler/HandleBattlemasterHelloOpcode, WorldSession.LootHandler/DoLootRelease, WorldSession.LootHandler/HandleAutostoreLootItemOpcode, WorldSession.LootHandler/HandleLootMasterGiveOpcode, WorldSession.LootHandler/HandleLootMoneyOpcode, WorldSession.TaxiHandler/SendTaxiStatus, world_event_wareffort/FollowSaurfang, world_event_wareffort/SetRespawnNearSaurfang, ZoneScript/GetCreature, zulfarrak/initBlyCrewMember, zulfarrak/MovementInform, zulfarrak/switchFactionIfAlive, zulfarrak/UpdateAI, zulfarrak/UpdateAI#2 | — |
| GetPet | method | — | ChatHandler.Chat/GetSelectedPet, Player.Main/GetMiniPet, Player.Main/GetObjectByTypeMask, Unit.Main/Attack, Unit.Main/AttackedBy, Unit.Main/FindGuardianWithEntry, Unit.Main/GetGuardianCountWithEntry, Unit.Main/GetPet, Unit.Main/RemoveGuardians, Unit.Main/SetInCombatWithVictim, Unit.Main/_GetPet, WorldSession.PetHandler/HandlePetRename | — |
| GetDynamicObject | method | — | Player.Main/GetObjectByTypeMask, SpellCaster/GetDynObject, SpellCaster/GetDynObject#2, SpellCaster/GetDynObjects, SpellCaster/RemoveAllDynObjects, SpellCaster/RemoveDynObject, Unit.SpellAuras/GetDynObject | — |
| Add#5 | method | Errors/PrintStacktraceAndThrow, GameObject/AddToWorld, GameObject/GetGoType, GameObject/Update, GridDefines/ComputeCellPair, Log.Main/Out, Object/GetGUIDLow, Object/GetTypeId, Object/IsInWorld, Transport/SendCreateUpdateToMap, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/SetMap | — | — |
| GetTerrain | method | — | MovementAnticheat/CheckWallClimb, Player.Main/SaveNewPlayer, Player.Main/UpdateTerainEnvironmentFlags, WorldObject.Object/GetRandomPoint, WorldObject.Object/GetTerrain | — |
| GetInstanceData | method | — | ChatHandler.MiscCommands/HandleInstanceGetDataCommand, ChatHandler.MiscCommands/HandleInstanceSaveDataCommand, ChatHandler.MiscCommands/HandleInstanceSetDataCommand, instance_blackwing_lair/AreaTrigger_at_enter_vael_room, Map.ScriptCommands/ScriptCommand_SetData, Map.ScriptCommands/ScriptCommand_SetData64, MapPersistentStateMgr/SaveToDB, ScriptMgr/GetTargetByType, Spell.Effects/EffectScriptEffect, Totem/Create, WorldObject.Object/GetInstanceData, WorldObject.Object/SetZoneScript | — |
| Add#6 | method | — | Transport/TeleportTransport, TransportMgr/CreateTransport | — |
| GetInstanceData#2 | method | — | Conditions/Evaluate | — |
| GetScriptId | method | — | ScriptMgr/CreateInstanceData | — |
| Add#4 | method | — | — | — |
| LoadElevatorTransports | method | GameObject/LoadFromDB, Log.Main/Out, ObjectMgr/GetGameObjectTemplate, ObjectMgr/GetGOData, TransportMgr/GetElevatorTransportsForMap | — | — |
| Balance | method | — | — | — |
| IsUnloading | method | — | ObjectGridLoader/LoadHelper | — |
| MarkAsCrashed | method | — | — | — |
| IsCrashed | method | — | MapManager/Update | — |
| IsUpdateFinished | method | — | MapManager/Update | — |
| MarkNotUpdated | method | — | MapManager/Update | — |
| SetUpdateDiffMod | method | — | — | — |
| GetUpdateDiffMod | method | — | — | — |
| GetCurrentClockTime | method | — | GameObject/Update, Unit.Main/Update | — |
| GetWeatherSystem | method | — | Player.Main/UpdateZone | — |
| GetCreatureLinkingHolder | method | — | Creature.Main/Create, Creature.Main/LoadFromDB, Creature.Main/RemoveCorpse, Creature.Main/Update, Creature.MotionMaster/MoveTargetedHome, Unit.Main/SelectHostileTarget, Unit.Main/SetInCombatState, Unit.Main/TauntFadeOut, WorldObject.Object/SummonCreature, WorldObject.Object/SummonCreature#2 | — |
| MessageBroadcast | method | Cell/Cell#2, Cell/SetNoCreate, GridDefines/ComputeCellPair, Log.Main/Out, MessageDeliverer/MessageDeliverer, Object/GetGUIDLow, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY | Player.Main/SendMessageToSet | — |
| SetTimer | method | — | — | — |
| MessageBroadcast#2 | method | Cell/Cell#2, Cell/SetNoCreate, GridDefines/ComputeCellPair, Log.Main/Out, Object/GetGUIDLow, Object/GetTypeId, ObjectMessageDeliverer/ObjectMessageDeliverer, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY | WorldObject.Object/SendMessageToSet | — |
| buildNGridLinkage | method | — | — | — |
| getNGrid | method | — | ObjectGridLoader/Visit#2, ObjectGridLoader/Visit#4, ObjectGridLoader/Visit#8 | — |
| MessageDistBroadcast | method | Cell/Cell#2, Cell/SetNoCreate, GridDefines/ComputeCellPair, Log.Main/Out, MessageDistDeliverer/MessageDistDeliverer, Object/GetGUIDLow, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY | Player.Main/SendMessageToSetInRange, Player.Main/SendMessageToSetInRange#2 | — |
| MessageDistBroadcast#2 | method | Cell/Cell#2, Cell/SetNoCreate, GridDefines/ComputeCellPair, Log.Main/Out, Object/GetGUIDLow, Object/GetTypeId, ObjectMessageDistDeliverer/ObjectMessageDistDeliverer, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY | WorldObject.Object/SendMessageToSetInRange | — |
| loaded | method | — | — | — |
| UpdateSync | method | Object/IsInWorld, WorldObject.Object/Update | MapManager/Update | — |
| UpdateCellsAroundObject | method | Cell/CalculateCellArea, Cell/Cell#2, Cell/SetNoCreate, Object/IsInWorld, ObjectUpdater/ObjectUpdater, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/IsPositionValid | — | — |
| MarkCellsAroundObject | method | Cell/CalculateCellArea, Object/IsInWorld, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/IsPositionValid | — | — |
| UpdateActiveCellsCallback | method | Cell/Cell#2, Cell/SetNoCreate, ObjectUpdater/ObjectUpdater, World/getConfig#4 | — | — |
| UpdateActiveCellsAsynch | method | MapRefManager/begin, MapRefManager/end, ThreadPool/processWorkload, ThreadPool/size | — | — |
| UpdateActiveCellsSynch | method | MapRefManager/begin, MapRefManager/end | — | — |
| UpdateCells | method | Creature.MotionMaster/UpdateMotionAsync, Object/IsInWorld, shared_Util/getMSTime, ThreadPool/processWorkload, ThreadPool/status, Unit.Main/GetMotionMaster, World/getConfig#4, WorldTimer/getMSTimeDiff | — | — |
| ProcessSessionPackets | method | Log.Main/Out, MapRefManager/begin, MapRefManager/end, MapSessionFilter/MapSessionFilter, Object/IsInWorld, PacketFilter/SetProcessType, Player.Main/GetSession, SpellCaster/UpdateCooldowns, World/getConfig#4, WorldSession.Main/ProcessPackets | — | — |
| UpdateSessionsMovementAndSpellsIfNeeded | method | shared_Util/getMSTime, World/getConfig#4, WorldTimer/getMSTimeDiff | — | — |
| UpdatePlayers | method | MapRefManager/begin, MapRefManager/end, Object/IsInWorld, Player.Main/AddSkippedUpdateTime, Player.Main/GetSession, Player.Main/GetSkippedUpdateTime, Player.Main/HasScheduledEvent, Player.Main/ResetSkippedUpdateTime, shared_Util/getMSTime, Unit.Main/IsInCombat, UpdateHelper/UpdateHelper, UpdateHelper/UpdateRealTime, World/getConfig#4, WorldSession.Main/HasRecentPacket, WorldTimer/getMSTimeDiff | — | — |
| DoUpdate | method | shared_Util/getMSTime, WorldTimer/getMSTimeDiff | MapManager/Update | — |
| Update#3 | method | BoundsTrait.DynamicTree/update#2, Errors/PrintStacktraceAndThrow, InstanceData/Update, Log.Main/Out, MapManager/MarkContinentUpdateFinished, MapManager/UpdateGridState, MapManager/waitContinentUpdateFinishedUntil, MapRefManager/begin, MapRefManager/end, MapSessionFilter/MapSessionFilter, MovementBroadcaster/IsMapSlow, Object/IsInWorld, Player.Main/GetSession, shared_Util/getMSTime, Weather/UpdateWeathers, World/GetBroadcaster, World/getConfig#4, World/GetMaxVisibleDistanceOnContinents, WorldSession.Main/Update, WorldTimer/getMSTimeDiffToNow | — | — |
| GetLastPlayerLeftTime | method | — | — | — |
| UpdateScriptedEvents | method | — | — | — |
| StartScriptedEvent | method | Object/GetObjectGuid, ObjectGuid/ObjectGuid, World/GetGameTime | Map.ScriptCommands/ScriptCommand_StartMapEvent | — |
| UpdateEvent | method | Conditions/IsConditionSatisfied, Object/IsInWorld, World/GetGameTime | — | — |
| EndEvent | method | Object/IsInWorld | Map.ScriptCommands/ScriptCommand_EndMapEvent | — |
| GetSourceObject | method | — | ScriptMgr/GetTargetByType | — |
| GetTargetObject | method | — | ScriptMgr/GetTargetByType | — |
| SendEventToMainTargets | method | Creature.Main/AI, Creature.Main/ToCreature, CreatureAI/OnScriptEventHappened | Map.ScriptCommands/ScriptCommand_SendMapEvent | — |
| SendEventToAdditionalTargets | method | Creature.Main/AI, Creature.Main/ToCreature, CreatureAI/OnScriptEventHappened | Map.ScriptCommands/ScriptCommand_SendMapEvent | — |
| SendEventToAllTargets | method | — | Map.ScriptCommands/ScriptCommand_SendMapEvent | — |
| Remove#3 | method | Cell/Cell#2, Cell/GridX, Cell/GridY, Errors/PrintStacktraceAndThrow, GridDefines/ComputeCellPair, LinkedListElement/nocheck_prev, Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out, Object/GetGUID, Player.Main/CleanupsBeforeDelete, Player.Main/GetMapRef, Player.Main/GetName, Player.Main/RemoveFromWorld, PlayerBroadcaster/RemoveListener, World/IsStopped, WorldObject.Object/ClearUpdateMask, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/ResetMap, ZoneScript/OnPlayerLeave#2 | Player.Main/ExecuteTeleportFar, Player.Main/SwitchInstance, WorldSession.Main/LogoutPlayer | — |
| Remove#5 | method | GameObject/RemoveFromWorld, GameObject/SaveRespawnTime, Transport/CleanupsBeforeDelete, Transport/SendOutOfRangeUpdateToMap, World/getConfig, WorldObject.Object/IsActiveObject, WorldObject.Object/ResetMap | — | — |
| Remove#6 | method | — | Transport/TeleportTransport | — |
| Remove#4 | method | — | — | — |
| PlayerRelocation | method | Camera/UpdateVisibilityForOwner, Cell/Cell#2, Cell/CellX, Cell/CellY, Cell/DiffCell, Cell/DiffGrid, Cell/GridX, Cell/GridY, Cell/operator==, Errors/PrintStacktraceAndThrow, GridDefines/ComputeCellPair, Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out, MovementInfo/HasMovementFlag, Player.Main/GetCamera, Player.Main/GetLongSight, Player.Main/GetName, Player.Main/UpdateLongSight, Unit.Main/OnRelocated, ViewPoint/Event_GridChanged, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetViewPoint, WorldObject.Object/Relocate#2 | Player.Main/SetPosition, Transport/UpdatePassengerPosition | — |
| DoPlayerGridRelocation | method | Cell/Cell#2, Cell/CellX, Cell/CellY, Cell/DiffCell, Cell/DiffGrid, Cell/GridX, Cell/GridY, Cell/operator==, Errors/PrintStacktraceAndThrow, GridDefines/ComputeCellPair, Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out, Player.Main/GetName, ViewPoint/Event_GridChanged, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetViewPoint | Player.Main/RelocateToLastClientPosition | — |
| CreatureRelocation | method | Cell/Cell#2, Errors/PrintStacktraceAndThrow, GridDefines/ComputeCellPair, Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out, Object/GetEntry, Object/GetGUIDLow, Unit.Main/OnRelocated, WorldObject.Object/Relocate#2 | Creature.Main/RemoveCorpse, stratholme/Deplacement, stratholme/ReceiveEmote, Transport/UpdatePassengerPosition, Unit.Main/NearTeleportTo, Unit.Main/TeleportPositionRelocation, Unit.Main/UpdateSplineMovement, WorldSession.MovementHandler/HandleMoverRelocation | — |
| CreatureCellRelocation | method | Cell/CellX, Cell/CellY, Cell/DiffGrid, Cell/gridPair, Cell/GridX, Cell/GridY, Cell/operator!=, Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out, Object/GetEntry, Object/GetGUIDLow, ViewPoint/Event_GridChanged, WorldObject.Object/GetCurrentCell, WorldObject.Object/GetViewPoint, WorldObject.Object/IsActiveObject | — | — |
| CreatureRespawnRelocation | method | Cell/Cell#2, Cell/CellX, Cell/CellY, Cell/GridX, Cell/GridY, Creature.Main/AI, Creature.Main/GetRespawnCoord, Creature.MotionMaster/Initialize, CreatureAI/EnterEvadeMode, GridDefines/ComputeCellPair, Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out, MotionMaster/Clear, Object/GetEntry, Object/GetGUIDLow, Unit.Main/GetMotionMaster, Unit.Main/OnRelocated, WorldObject.Object/GetCurrentCell, WorldObject.Object/Relocate#2 | ObjectGridLoader/Visit#5 | — |
| UnloadGrid | method | Errors/PrintStacktraceAndThrow, GridMap/Unload, Log.Main/Out, ObjectGridLoader/MoveToRespawnN, ObjectGridUnloader/ObjectGridUnloader, ObjectGridUnloader/UnloadN | GridStates/Update#4 | — |
| UnloadAll#3 | method | Log.Main/Out | MapManager/DeleteInstance, MapManager/UnloadAll, MapManager/Update | — |
| CheckGridIntegrity | method | Cell/Cell#2, Cell/CellX, Cell/CellY, Cell/GridX, Cell/GridY, Cell/operator!=, GridDefines/ComputeCellPair, Log.Main/Out, Object/GetGUIDLow, WorldObject.Object/GetCurrentCell, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY | — | — |
| GetMapName | method | — | ChatHandler.ServerCommands/HandleListMapsCommand, instance_blackfathom_deeps/Load, instance_blackfathom_deeps/SetData, instance_blackrock_depths/Load, instance_blackrock_depths/SetData, instance_blackrock_spire/Load, instance_blackrock_spire/SetData, instance_blackwing_lair/Load, instance_blackwing_lair/SetData, instance_dire_maul/Load, instance_dire_maul/SetData, instance_gnomeregan/Load, instance_gnomeregan/SetData, instance_maraudon/SetData, instance_molten_core/Load, instance_molten_core/SetData, instance_naxxramas.Main/Load, instance_naxxramas.Main/SetData, instance_razorfen_downs/Load, instance_razorfen_downs/SetData, instance_razorfen_kraul/Load, instance_razorfen_kraul/SetData, instance_ruins_of_ahnqiraj/Load, instance_ruins_of_ahnqiraj/SetData, instance_scarlet_monastery/Load, instance_scarlet_monastery/SetData, instance_scholomance/Load, instance_scholomance/SetData, instance_shadowfang_keep/Load, instance_shadowfang_keep/SetData, instance_sunken_temple/Load, instance_sunken_temple/SetData, instance_temple_of_ahnqiraj/Load, instance_temple_of_ahnqiraj/SetData, instance_uldaman/Load, instance_uldaman/SetData, instance_wailing_caverns/Load, instance_wailing_caverns/SetData, instance_zulfarrak/Load, instance_zulfarrak/SetData, instance_zulgurub/Load, instance_zulgurub/SetData | — |
| UpdateObjectVisibility | method | Cell/SetNoCreate, GridNotifiers/VisibleChangesNotifier, Object/ToPlayer | WorldObject.Object/UpdateObjectVisibility | — |
| UpdateActiveObjectVisibility | method | Player.Main/GetSession, UpdateData/HasData, UpdateData/Send, UpdateData/UpdateData | — | — |
| UpdateActiveObjectVisibility#2 | method | Camera/GetBody, Object/GetObjectGuid, Object/IsInWorld, Player.Main/GetCamera, Player.Main/UpdateVisibilityOf | — | — |
| UpdateActiveObjectVisibility#3 | method | Camera/GetBody, Object/GetObjectGuid, Object/IsInWorld, Player.Main/GetCamera | GridNotifiers/Notify | — |
| SendInitSelf | method | GenericTransport/GetPassengers, Log.Main/Out, Object/GetGUIDLow, Player.Main/BuildCreateUpdateBlockForPlayer, Player.Main/GetSession, Player.Main/IsInVisibleList, UpdateData/Send, UpdateData/UpdateData, WorldObject.Object/BuildCreateUpdateBlockForPlayer, WorldObject.Object/GetTransport | — | — |
| SendInitTransports | method | Player.Main/GetSession, UpdateData/Send, UpdateData/UpdateData, WorldObject.Object/BuildCreateUpdateBlockForPlayer, WorldObject.Object/GetTransport | — | — |
| SendRemoveTransports | method | Player.Main/GetSession, UpdateData/Send, UpdateData/UpdateData, WorldObject.Object/BuildOutOfRangeUpdateBlock, WorldObject.Object/GetTransport | — | — |
| setNGrid | method | Errors/PrintStacktraceAndThrow, Log.Main/Out | — | — |
| AddObjectToRemoveList | method | Errors/PrintStacktraceAndThrow, WorldObject.Object/CleanupsBeforeDelete, WorldObject.Object/GetInstanceId, WorldObject.Object/GetMapId | WorldObject.Object/AddObjectToRemoveList | — |
| RemoveAllObjectsInRemoveList | method | GameObject/GetGoType, GameObject/ToTransport, Log.Main/Out, Object/GetGUIDLow, Object/GetObjectGuid, Object/GetTypeId, Object/ToGameObject | instance_molten_core/Update, instance_sunken_temple/Update, MapManager/RemoveAllObjectsInRemoveList | — |
| HaveRealPlayers | method | Player.Main/IsBot | BattleBotAI.Main/UpdateAI | — |
| GetPlayersCountExceptGMs | method | Player.Main/IsGameMaster | boss_skeram/Aggro, ChatHandler.ServerCommands/HandleListMapsCommand | — |
| SendToPlayers | method | Player.Main/GetSession, Player.Main/GetTeam, WorldSession.Main/SendPacket | Creature.Main/SendZoneUnderAttackMessage | — |
| SendToPlayersInZone | method | Player.Main/GetSession, WorldObject.Object/GetZoneId, WorldSession.Main/SendPacket | Weather/SendWeatherForPlayersInZone | — |
| SendDefenseMessage | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#3, ObjectMgr/GetBroadcastText, ObjectMgr/GetMangosString, Player.Main/GetSession, Unit.Main/GetGender, WorldPacket/WorldPacket#4, WorldSession.Main/GetSessionDbLocaleIndex, WorldSession.Main/SendPacket | OutdoorPvPEP/ChangeState, OutdoorPvPEP/ChangeState#2, OutdoorPvPEP/ChangeState#3, OutdoorPvPEP/ChangeState#4, OutdoorPvPEP/Update | — |
| ActiveObjectsNearGrid | method | Errors/PrintStacktraceAndThrow, GridDefines/ComputeCellPair, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY | GridStates/Update | — |
| AddToActive | method | Creature.Main/GetRespawnCoord, Creature.Main/HasStaticDBSpawnData, Creature.Main/IsPet, GridDefines/ComputeGridPair, Log.Main/Out, Object/GetEntry, Object/GetGUIDLow, Object/GetTypeId, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY | Camera/SetView, ObjectGridLoader/LoadHelper | — |
| RemoveFromActive | method | Creature.Main/GetRespawnCoord, Creature.Main/HasStaticDBSpawnData, Creature.Main/IsPet, GridDefines/ComputeGridPair, Log.Main/Out, Object/GetEntry, Object/GetGUIDLow, Object/GetTypeId, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY | Camera/SetView | — |
| CreateInstanceData | method | Database/PExecute#2, Database/PQuery, Field/GetString, InstanceData/Create, InstanceData/Initialize, InstanceData/Load, Log.Main/Out, QueryResult/Fetch, ScriptMgr/CreateInstanceData, ScriptMgr/GetScriptName#2 | MapManager/CreateBattleGroundMap, MapManager/CreateDungeonMap, MapManager/CreateMap, MapManager/CreateTestMap | instance, world |
| SetWeather | method | Weather/FindOrCreateWeather, Weather/SetWeather | boss_ossirian/Aggro, boss_ossirian/Reset, ChatHandler.ServerCommands/HandleChangeWeatherCommand, scourge_invasion/OnScriptEventHappened | — |
| TeleportAllPlayersTo | method | Errors/PrintStacktraceAndThrow, MapRefManager/getFirst, Player.Main/GetHomeBindMap, Player.Main/GetMapRef, Player.Main/TeleportToBGEntryPoint, Player.Main/TeleportToHomebind | MapPersistentStateMgr/operator() | — |
| GetPersistanceState#3 | method | — | — | — |
| DungeonMap | ctor | Errors/PrintStacktraceAndThrow, MapEntry/IsDungeon, World/getConfig#4 | MapManager/CreateDungeonMap, MapManager/CreateTestMap | — |
| ~DungeonMap | dtor | — | — | — |
| InitVisibilityDistance#2 | method | World/GetMaxVisibleDistanceInInstances | — | — |
| CanEnter#2 | method | Errors/PrintStacktraceAndThrow, game_Group_Group/InCombatToInstance, InstanceData/IsEncounterInProgress, Log.Main/Out, Object/GetGUIDLow, Player.Main/GetGroup, Player.Main/GetMapRef, Player.Main/GetName, Player.Main/IsGameMaster, Player.Main/SendTransferAborted, Unit.Main/IsAlive | MapManager/CreateNewInstancesForPlayers | — |
| Add#2 | method | Log.Main/Out, Player.Main/AddInstanceEnterTime, Player.Main/GetName | — | — |
| BindPlayerOrGroupOnEnter | method | ByteBuffer/operator<<#10, DungeonPersistentState/CanReset, DungeonPersistentState/GetGroupCount, DungeonPersistentState/GetPlayerCount, Errors/PrintStacktraceAndThrow, game_Group_Group/BindToInstance, game_Group_Group/GetBoundInstance, Group/GetFirstMember, Group/GetId, GroupReference/next, Log.Main/Out, MapPersistentStateMgr/GetInstanceId, MapPersistentStateMgr/GetMapId, Object/GetGUIDLow, Object/GetObjectGuid, ObjectGuid/GetString, Player.Main/BindToInstance, Player.Main/GetBoundInstance, Player.Main/GetGroup, Player.Main/GetName, Player.Main/GetSession, Player.Main/UnbindInstance#2, WorldObject.Object/GetMapId, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | MapManager/CreateNewInstancesForPlayers | — |
| Update#2 | method | — | — | — |
| Remove#2 | method | LinkedListHead/getSize, Log.Main/Out, Player.Main/GetName, World/getConfig#4 | — | — |
| Reset | method | LinkedListHead/isEmpty, Player.Main/SendResetFailedNotify | game_Group_Group/ResetInstances, MapPersistentStateMgr/operator(), MapPersistentStateMgr/_ResetInstance, Player.Main/ResetInstance | — |
| PermBindAllPlayers | method | ByteBuffer/operator<<#10, game_Group_Group/BindToInstance, Group/GetLeaderGuid, Object/GetObjectGuid, ObjectGuid/operator==, Player.Main/BindToInstance, Player.Main/GetBoundInstance, Player.Main/GetGroup, Player.Main/GetSession, Player.Main/Player, Player.Main/TeleportToHomebind, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| UnloadAll#2 | method | MapPersistentStateMgr/DeleteRespawnTimesAndData | — | — |
| SendResetWarnings | method | Player.Main/SendInstanceResetWarning | MapPersistentStateMgr/operator()#2 | — |
| SetResetSchedule | method | DungeonPersistentState/GetResetTime, DungeonResetEvent/DungeonResetEvent#2, Errors/PrintStacktraceAndThrow, MapPersistentStateManager/GetScheduler, MapPersistentStateMgr/GetInstanceId, MapPersistentStateMgr/GetMapId, MapPersistentStateMgr/ScheduleReset | — | — |
| GetMaxPlayers | method | — | — | — |
| GetPersistanceState#2 | method | — | ChatHandler.TeleportCommands/HandleGonameCommand | — |
| BattleGroundMap | ctor | — | MapManager/CreateBattleGroundMap | — |
| ~BattleGroundMap | dtor | — | — | — |
| Update | method | BattleGroundMap/GetBG, game_Battlegrounds_BattleGround/Update | — | — |
| GetPersistanceState | method | — | — | — |
| InitVisibilityDistance | method | World/GetMaxVisibleDistanceInBG | — | — |
| CanEnter | method | Errors/PrintStacktraceAndThrow, Log.Main/Out, Object/GetGUIDLow, Player.Main/GetBattleGroundId, Player.Main/GetMapRef | — | — |
| Add | method | — | — | — |
| Remove | method | Log.Main/Out, Player.Main/GetName | — | — |
| SetUnload | method | — | game_Battlegrounds_BattleGround/~BattleGround | — |
| UnloadAll | method | — | — | — |
| ScriptsStart | method | ScriptMgr/IncreaseScheduledScriptsCount, World/GetGameTime | CreatureAI/DoSpellsListCasts, GameObject/Use, instance_ruins_of_ahnqiraj/OnCreatureEnterCombat, Map.ScriptCommands/ScriptCommand_StartScript, Map.ScriptCommands/ScriptCommand_StartScriptForAll, Map.ScriptCommands/ScriptCommand_StartScriptOnGroup, Map.ScriptCommands/ScriptCommand_StartScriptOnZone, Map.ScriptCommands/ScriptCommand_SummonCreature, Player.Main/AddQuest, Player.Main/GetGossipTextId#2, Player.Main/OnGossipSelect, Player.Main/RewardQuest, Spell.Effects/EffectScriptEffect, Spell.Effects/EffectSendEvent, WaypointMovementGenerator/OnArrived, WorldSession.LootHandler/DoLootRelease | — |
| ScriptCommandStart | method | ScriptMgr/IncreaseScheduledScriptsCount, World/GetGameTime | Spell.Effects/EffectDummy, ThreatListCopier.battleground_alterac/UpdateEscortAI#4 | — |
| ScriptCommandStartDirect | method | Conditions/IsConditionSatisfied | CreatureEventAI/ProcessAction, eastern_plaguelands/EffectDummyGameObj_go_mark_of_detonation, Unit.SpellAuras/HandleAuraDummy | — |
| FindScriptInitialTargets | method | Object/IsInWorld, ObjectGuid/IsEmpty | — | — |
| FindScriptFinalTargets | method | Log.Main/Out, ScriptMgr/GetTargetByType, SpellMgr/GetSpellEntry, SpellMgr/Instance | — | — |
| TerminateScript | method | ScriptAction/IsSameScript, ScriptMgr/DecreaseScheduledScriptCount | — | — |
| ScriptsProcess | method | Conditions/IsConditionSatisfied, ScriptMgr/DecreaseScheduledScriptCount, World/GetGameTime | — | — |
| StartAreaTriggerScript | method | Conditions/IsConditionSatisfied, Object/GetObjectGuid, ScriptMgr/OnAreaTrigger, World/GetGameTime, WorldObject.Object/GetMap | WorldSession.MiscHandler/HandleAreaTriggerOpcode | — |
| GetPlayer | method | ObjectAccessor/FindPlayer, WorldObject.Object/GetMap | BattleGroundAV/CheckSpellCast, BattleGroundWS/ForceFlagAreaTrigger, blackrock_depths/SummonRingBoss, boss_ayamiss/UpdateAI, boss_buru/UpdateAI, boss_chromaggus/Reset, boss_chromaggus/UpdateAI, boss_cthun/UpdateAI#4, boss_cthun/UpdateAI#7, boss_cthun/UpdateInvulnerablePhase, boss_cthun/UpdateStomachGrab, boss_dathrohan_balnazzar/UpdateAI, boss_hakkar/UpdateAI, boss_jindo/UpdateAI, boss_jindo/UpdateAI#2, boss_maexxna/JustDied#2, boss_maexxna/UpdateAI#2, boss_maexxna/UpdateWraps, boss_maleki_the_pallid/UpdateAI, boss_mandokir/MovementInform, boss_mandokir/UpdateAI, boss_mandokir/UpdateAI#2, boss_nerubenkan/UpdateAI, boss_skeram/CancelFulfillment, boss_twinemperors/OnEndTeleport, boss_vaelastrasz/UpdateAI, boss_victor_nefarius/SummonedCreatureJustDied, boss_victor_nefarius/UpdateAI, darkshore/GetPlayer, desolace/FailEscort, desolace/WaypointReached, dreadsteed_ritual/UpdateAI#4, durotar/UpdateAI, duskwood/UpdateAI#3, eastern_plaguelands/GetPlayer, GameObject/FinishRitual, GameObject/Update, GameObject/Use, game_Battlegrounds_BattleGround/ReturnPlayersToHomeGY, gnomeregan/UpdateEscortAI, GridNotifiers/Notify, instance_scarlet_monastery/Update, instance_temple_of_ahnqiraj/KillPlayersInStomach, instance_temple_of_ahnqiraj/UpdateStomachOfCthun, npcs_special/EndEvent, npcs_special/UpdateAI#10, npcs_special/UpdateAI#13, npcs_special/UpdateAI#3, npc_j_eevee/MovementInform, npc_j_eevee/npc_j_eevee_scholomanceAI, npc_j_eevee/UpdateAI#2, OutdoorPvPEP/BuffTeams, Player.Main/GetSelectedPlayer, Player.Main/RewardHonorOnDeath, Player.Main/SendDestroyGroupMembers, quest_stormwind_rendezvous/GetPlayer, ScriptedEscortAI/GetPlayerForEscort, ScriptedFollowerAI/GetLeaderForFollower, searing_gorge/UpdateAI, silithus/OnActivateBySpell, silithus/UpdateAI#7, Spell.Effects/EffectDummy, stonetalon_mountains/JustDied, stonetalon_mountains/UpdateAI, stormwind_city/Reset#2, stormwind_city/UpdateAI, stratholme/UpdateAI#2, the_barrens/UpdateAI#2, thousand_needles/JustSummoned, thousand_needles/UpdateAI, ungoro_crater/Transform, wailing_caverns/UpdateEscortAI, WorldSession.QuestHandler/HandleQuestgiverAcceptQuestOpcode, WorldSession.TradeHandler/HandleInitiateTradeOpcode, ZoneScript/BroadcastPacket, ZoneScript/SendUpdateWorldState, ZoneScript/SendUpdateWorldState#2, ZoneScript/TeamCastSpell, ZoneScript/Update | — |
| GetCorpse | method | ObjectAccessor/GetCorpseInMap, WorldObject.Object/GetInstanceId | Corpse/DeleteBonesFromWorld, Player.Main/SendLoot, Spell.Main/CheckCast, Spell.Main/handle_immediate, Spell.Main/SetTargetMap, WorldSession.LootHandler/DoLootRelease, WorldSession.LootHandler/HandleAutostoreLootItemOpcode, WorldSession.LootHandler/HandleLootMoneyOpcode | — |
| GetAnyTypeCreature | method | ObjectGuid/GetHigh | ChatHandler.Chat/GetSelectedCreature, ChatHandler.CreatureCommands/HandleWpAddCommand, ChatHandler.CreatureCommands/HandleWpExportCommand, ChatHandler.CreatureCommands/HandleWpModifyCommand, ChatHandler.CreatureCommands/HandleWpShowCommand, Creature.Main/Execute, ObjectAccessor/GetUnit, Player.Main/GetNextQuest, Player.Main/GetNPCIfCanInteractWith, Player.Main/PrepareQuestMenu, Player.Main/SendPreparedQuest, WorldSession.PetHandler/HandlePetCastSpellOpcode, WorldSession.PetHandler/HandlePetSetAction, WorldSession.PetHandler/HandlePetSpellAutocastOpcode, WorldSession.PetHandler/SendPetNameQuery, WorldSession.SpellHandler/HandlePetCancelAuraOpcode | — |
| GetUnit | method | ObjectGuid/IsPlayer | blackrock_depths/JustDied, blackrock_depths/UpdateEscortAI#4, boss_arlokk/DoSummonPhanters, boss_arlokk/JustSummoned, boss_arlokk/UpdateAI, boss_arlokk/UpdateAI#2, boss_baroness_anastari/JustDied, boss_baroness_anastari/UpdateAI, boss_gordok_king/UpdateAIMage, boss_gordok_king/UpdateAIPrist, boss_gordok_king/UpdateAIShaman, boss_interrogator_vishas/JustDied, boss_jindo/JustSummoned, boss_jindo/UpdateAI, boss_mandokir/CheckWatchedPlayer, boss_mandokir/UpdateAI, boss_moam/UpdateAI, boss_ossirian/UpdateAI, boss_ouro/UpdateAI#2, boss_razorgore/PhaseSwitch, boss_razorgore/UpdateAI#2, boss_vectus/JustDied, Creature.Main/Execute, Creature.Main/Execute#4, CreatureGroups/Respawn, custom_creatures/UpdateAI#2, DynamicObject/Delay, instance_blackwing_lair/UpdateAI#3, instance_dire_maul/Reset#8, instance_dire_maul/UpdateAI#7, instance_zulgurub/Thekal_GetUnitCastingRez, instance_zulgurub/Thekal_GetUnitThatCanRez, instance_zulgurub/Thekal_GetUnitThatNeedsRez, PartyBotAI/GetMarkedTarget, PartyBotAI/SelectAttackTarget, PetAI/UpdateAI, Player.Main/GetSelectedUnit, Player.Main/SetCheatDebugTargetInfo, PlayerAI/FindController, ruins_of_ahnqiraj/UpdateAI#10, ruins_of_ahnqiraj/UpdateAI#7, ruins_of_ahnqiraj/UpdateAI#9, silithus/UpdateAI#2, silverpine_forest/WaypointReached, Spell.Main/CheckCast, Spell.Main/FillTargetMap, SpellCaster/ProcDamageAndSpell_delayed, spell_item/OnAfterApply#3, stratholme/JustSummoned, the_barrens/UpdateAI#2, TotemAI/UpdateAI, Unit.AuraProcHandler/HandleProcTriggerSpellAuraProc, Unit.Main/AddSpellAuraHolder, Unit.Main/GetTauntTarget, Unit.Main/GetUnit, Unit.Main/ModConfuseSpell, Unit.Main/RemoveNotOwnSingleTargetAuras, WorldSession.ChatHandler/HandleTextEmoteOpcode, WorldSession.CombatHandler/HandleAttackSwingOpcode, WorldSession.MovementHandler/GetMoverFromGuid, WorldSession.MovementHandler/HandleMoveNotActiveMoverOpcode, WorldSession.PetHandler/HandlePetAbandon, WorldSession.PetHandler/HandlePetAction, WorldSession.PetHandler/HandlePetStopAttack | — |
| GetWorldObject | method | Object/IsInWorld, ObjectGuid/GetHigh | Conditions/Evaluate, Map.ScriptCommands/ScriptCommand_RemoveMapEventTarget, ScriptMgr/GetTargetByType, TemporarySummon/InformSummonerOfDespawn, Unit.SpellAuras/TriggerSpell, WorldSession.MiscHandler/HandleFarSightOpcode | — |
| GetWorldObjectOrPlayer | method | ObjectGuid/GetHigh | — | — |
| AddUpdateObject | method | — | game_Objects_Item/AddToClientUpdateList, WorldObject.Object/AddToClientUpdateList#2 | — |
| RemoveUpdateObject | method | Errors/PrintStacktraceAndThrow | game_Objects_Item/RemoveFromClientUpdateList, WorldObject.Object/RemoveFromClientUpdateList#2 | — |
| AddRelocatedUnit | method | — | Unit.Main/Update | — |
| RemoveRelocatedUnit | method | Errors/PrintStacktraceAndThrow | Unit.Main/RemoveFromWorld | — |
| AddUnitToMovementUpdate | method | — | Unit.Main/Update | — |
| RemoveUnitFromMovementUpdate | method | — | — | — |
| SendObjectUpdates | method | Errors/PrintStacktraceAndThrow, Player.Main/GetSession, shared_Util/getMSTime, ThreadPool/processWorkload, ThreadPool/size, UpdateData/Send, World/getConfig#4, WorldObject.Object/BuildUpdateData, WorldTimer/getMSTimeDiffToNow | — | — |
| UpdateVisibilityForRelocations | method | Errors/PrintStacktraceAndThrow, shared_Util/getMSTime, ThreadPool/processWorkload, ThreadPool/size, Unit.Main/ProcessRelocationVisibilityUpdates, World/getConfig#4, WorldTimer/getMSTimeDiffToNow | — | — |
| GenerateLocalLowGuid | method | Errors/PrintStacktraceAndThrow | ChatHandler.CreatureCommands/HandleEscortShowWpCommand, ChatHandler.CreatureCommands/Helper_CreateWaypointFor, GameObject/SummonLinkedTrapIfAny, game_Battlegrounds_BattleGround/AddObject, ObjectMgr/AddCreData, ObjectMgr/AddGOData, Pet.Main/CreateBaseAtCreature, Pet.Main/LoadPetFromDB, Player.Main/SetLongSight, Player.Main/SummonPossessedMinion, Spell.Effects/EffectAddFarsight, Spell.Effects/EffectDuel, Spell.Effects/EffectDummy, Spell.Effects/EffectPersistentAA, Spell.Effects/EffectSummon, Spell.Effects/EffectSummonCritter, Spell.Effects/EffectSummonGuardian, Spell.Effects/EffectSummonObject, Spell.Effects/EffectSummonObjectWild, Spell.Effects/EffectSummonPet#2, Spell.Effects/EffectSummonTotem, Spell.Effects/EffectTransmitted, WorldObject.Object/SummonCreature, WorldObject.Object/SummonCreature#2, WorldObject.Object/SummonGameObject | — |
| SendMonsterTextToMap | method | Log.Main/Out, MonsterChatBuilder/MonsterChatBuilder, ObjectMgr/GetCreatureTemplate, StaticMonsterChatBuilder/StaticMonsterChatBuilder | ScriptMgr/DoOrSimulateScriptTextForMap | — |
| PlayDirectSoundToMap | method | ByteBuffer/operator<<#10, Player.Main/SendDirectMessage, WorldObject.Object/GetZoneId, WorldPacket/WorldPacket#4 | Map.ScriptCommands/ScriptCommand_PlaySound, ScriptMgr/DoOrSimulateScriptTextForMap, ScriptMgr/DoScriptText | — |
| isInLineOfSight | method | Errors/PrintStacktraceAndThrow, GridDefines/IsValidMapCoord#3, IVMapManager/isInLineOfSight, VMapFactory/createOrGetVMapManager | boss_bug_trio/JustDied#4, Spell.Effects/EffectTransmitted, Unit.Main/ExtrapolateMovement, WorldObject.Object/IsWithinLOSAtPosition | — |
| GetLosHitPosition | method | Errors/PrintStacktraceAndThrow, GridDefines/IsValidMapCoord#3, IVMapManager/getObjectHitPos, Log.Main/Out, VMapFactory/createOrGetVMapManager | Spell.Effects/EffectTransmitted, Spell.Main/SetTargetMap, Unit.Main/GetRandomAttackPoint, WorldObject.Object/GetFirstCollision, WorldObject.Object/GetRandomPoint, WorldObject.Object/MovePositionToFirstCollision | — |
| GetWalkHitPosition | method | GameObject/GetDisplayId, GenericTransport/CalculatePassengerOffset, GenericTransport/CalculatePassengerPosition, GridDefines/IsValidMapCoord#3, Log.Main/Out, MoveMap/createOrGetMMapManager, MoveMap/GetModelNavMeshQuery, MoveMap/GetNavMeshQuery, WorldObject.PathFinder/FindWalkPoly | PlayerBotAI/UpdateAI, Spell.Main/SetTargetMap, Unit.Main/GetRandomAttackPoint, WaypointMovementGenerator/GetResetPosition#2, WaypointMovementGenerator/StartMove | — |
| GetSwimRandomPosition | method | shared_Util/rand_norm_f | WorldObject.Object/GetRandomPoint | — |
| GetWalkRandomPosition | method | Errors/PrintStacktraceAndThrow, GameObject/GetDisplayId, GenericTransport/CalculatePassengerOffset, GenericTransport/CalculatePassengerPosition, Geometry/GetNearPoint2DAroundPosition, GridDefines/IsValidMapCoord#3, MoveMap/createOrGetMMapManager, MoveMap/GetModelNavMeshQuery, MoveMap/GetNavMeshQuery, shared_Util/frand, shared_Util/rand_norm_f, WorldObject.PathFinder/FindWalkPoly | desolace/SummonAmbusher, elemental_invasions/DoSpawn, PlayerBotAI/BeforeAddToMap#2, PlayerBotAI/UpdateAI, ScriptedAI/DoSpawnCreature#2, WorldObject.Object/GetRandomPoint | — |
| GetHeight | method | Errors/PrintStacktraceAndThrow, GridDefines/IsValidMapCoord#3, GridMap/GetHeightStatic | boss_cthun/FixPortalPosition, boss_four_horsemen/UpdateAI#3, ChatHandler.UnitCommands/HandleGPSCommand, Creature.Main/FallGround, Creature.Main/LoadFromDB, Player.Main/FallGround, Player.Main/OnDisconnected, Spell.Main/CheckCast, Spell.Main/SetTargetMap, Unit.Main/ExtrapolateMovement, WorldObject.Object/GetFirstCollision, WorldObject.Object/UpdateAllowedPositionZ, WorldObject.Object/UpdateGroundPositionZ, WorldObject.PathFinder/BuildPathStep, WorldSession.MovementHandler/HandleMoverRelocation, world_event_wareffort/MoveToWaveBattlePosition#2, world_event_wareffort/MoveToWaveBattlePosition#3, world_event_wareffort/SetRespawnNearSaurfang, world_event_wareffort/UpdateAI#2 | — |
| FindCollisionModel | method | Errors/PrintStacktraceAndThrow, GridDefines/IsValidMapCoord#3, IVMapManager/FindCollisionModel, VMapFactory/createOrGetVMapManager | ChatHandler.DebugCommands/HandleDebugLoSAllowCommand, ChatHandler.DebugCommands/HandleDebugLoSCommand, WorldObject.PathFinder/BuildPolyPath | — |
| FindDynamicObjectCollisionModel | method | BoundsTrait.DynamicTree/getObjectHit, Errors/PrintStacktraceAndThrow, GridDefines/IsValidMapCoord#3 | ChatHandler.DebugCommands/HandleDebugLoSCommand | — |
| RemoveGameObjectModel | method | BoundsTrait.DynamicTree/balance#2, BoundsTrait.DynamicTree/remove#2 | GameObject/RemoveFromWorld, GameObject/UpdateModel, GameObject/UpdateModelPosition | — |
| InsertGameObjectModel | method | BoundsTrait.DynamicTree/balance#2, BoundsTrait.DynamicTree/insert#2 | GameObject/AddToWorld, GameObject/UpdateModel, GameObject/UpdateModelPosition | — |
| ContainsGameObjectModel | method | BoundsTrait.DynamicTree/contains | GameObject/RemoveFromWorld, GameObject/UpdateModel, GameObject/UpdateModelPosition | — |
| GetDynamicObjectHitPos | method | BoundsTrait.DynamicTree/getObjectHitPos | WorldObject.PathFinder/CutPathWithDynamicLoS | — |
| GetDynamicTreeHeight | method | BoundsTrait.DynamicTree/getHeight | MovementAnticheat/CheckWallClimb | — |
| CheckDynamicTreeLoS | method | BoundsTrait.DynamicTree/isInLineOfSight | — | — |
| CrashUnload | method | GridMap/UnloadTerrain, InstanceData/SaveToDB, Log.Main/Out, MapPersistentStateMgr/SetUsedByMapState, MapRefManager/begin, MapRefManager/end, MasterPlayer.Main/SetSocial, Object/GetGUIDLow, ObjectAccessor/RemoveObject#3, Player.Main/GetSession, Player.Main/GetSocial, Player.Main/Player#2, Player.Main/SaveInventoryAndGoldToDB, Player.Main/UninviteFromGroup, ScriptMgr/DecreaseScheduledScriptCount#2, SocialMgr/RemovePlayerSocial, TerrainInfo/GetMapId, WorldPacket/WorldPacket#4, WorldSession.Main/GetMasterPlayer, WorldSession.Main/LogoutPlayer, WorldSession.Main/SendPacket, WorldSession.Main/SetPlayer | MapManager/Update | — |
| BindToInstanceOrRaid | method | DungeonPersistentState/GetResetTime, DungeonPersistentState/SetResetTime | boss_skeram/JustDied, boss_vaelastrasz/QuestAccept_vaelastrasz, Player.Main/SendLoot, Unit.Main/Kill | — |
| PrintInfos | method | ChatHandler.Chat/PSendSysMessage | ChatHandler.MiscCommands/HandleInstancePerfInfosCommand | — |
| ShouldUpdateMap | method | WorldTimer/getMSTimeDiff | MapManager/Update | — |
| AddCorpseToRemove | method | — | ObjectAccessor/ConvertCorpseForPlayer | — |
| RemoveBones | method | — | Corpse/~Corpse | — |
| RemoveCorpses | method | ByteBuffer/operator<<#7, Corpse/Corpse, Corpse/Create, Corpse/DeleteFromDB, Corpse/GetGrid, Corpse/GetOwnerGuid, Corpse/SetFactionTemplate, Corpse/SetGrid, Object/GetGUIDLow, Object/GetObjectGuid, Object/GetUInt32Value, Object/SetGuidValue, Player.Main/GetSession, Player.Main/SaveToDB, Player.Main/SendLoot, World/getConfig, WorldObject.Object/GetFactionTemplateEntry, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/Relocate#2, WorldObject.Object/SetFlag, WorldObject.Object/SetUInt32Value, WorldPacket/WorldPacket#4, WorldSession.Main/PlayerLogoutWithSave, WorldSession.Main/SendPacket | — | — |
| RemoveOldBones | method | Corpse/IsExpired, World/GetWorldUpdateTimerInterval | — | — |
| SummonGameObject | method | GameObject/Create, GameObject/CreateGameObject, GameObject/SetRespawnTime, GameObject/SetSpawnedByDefault, Log.Main/Out, ObjectMgr/GetGameObjectTemplate, WorldObject.Object/SetWorldMask | instance_ruins_of_ahnqiraj/SpawnNewCrystals, instance_ruins_of_ahnqiraj/Update, instance_uldaman/SetData, ZoneScript/AddObject, ZoneScript/SetCapturePointData | — |
| LoadCreatureSpawn | method | Creature.Main/Creature, Creature.Main/GetRespawnDelay, Creature.Main/IsWorldBoss, Creature.Main/LoadFromDB, Creature.Main/SaveRespawnTime, Creature.Main/SetRespawnTime, CreatureData/GetObjectGuid, Log.Main/Out, ObjectMgr/GetCreatureData, World/getConfig | Map.ScriptCommands/ScriptCommand_LoadCreatureSpawn | — |
| LoadCreatureSpawnWithGroup | method | Creature.Main/GetCreatureGroup, CreatureGroups/GetMembers, CreatureGroups/HasGroupFlag, CreatureGroups/RespawnAll, ObjectGuid/GetCounter, Unit.Main/IsAlive | instance_ruins_of_ahnqiraj/Update, Map.ScriptCommands/ScriptCommand_LoadCreatureSpawn | — |
| LoadGameObjectSpawn | method | GameObject/CreateGameObject, GameObject/GetRespawnDelay, GameObject/LoadFromDB, GameObject/SaveRespawnTime, GameObject/SetRespawnTime, Log.Main/Out, ObjectGuid/ObjectGuid#3, ObjectMgr/GetGOData, World/getConfig | Map.ScriptCommands/ScriptCommand_LoadGameObject, OutdoorPvPSI/SpawnDustBags | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `instance`: id int(11) unsigned PK, map int(11) unsigned, reset_time bigint(40), data longtext?
- `world`: map int(11) unsigned PK, data longtext?

*`?` = nullable, `PK` = primary key column.*

