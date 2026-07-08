<!-- provenance: failed-members -->
# PoolManager

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# PoolManager

**Purpose & Responsibilities**

`PoolManager` is the singleton subsystem responsible for managing **spawn pools** in the world. In this game engine, a "pool" is a logical group of entities (Creatures, GameObjects, or other Pools) where only a limited number (`MaxLimit`) can exist simultaneously. This mechanism allows for dynamic population control, such as having multiple rare spawn locations where only one appears at a time, or scaling the number of mobs based on server population.

Key responsibilities include:
1.  **Loading & Validation:** Reading pool definitions from the database (`pool_template`, `pool_creature`, etc.) during startup, validating constraints (e.g., no circular dependencies between pools, valid chances, correct map assignments), and building in-memory lookup structures.
2.  **Spawn Management:** Determining which specific entity within a pool should spawn based on weighted chances (`chance` column) or equal probability, respecting the `MaxLimit` constraint.
3.  **Lifecycle Handling:** Spawning, despawning, and respawning entities within pools. It handles the complex logic of replacing a despawned entity with a new one from the same pool or refreshing an existing one.
4.  **Integration:** Collaborating with `GameEventMgr` to handle event-driven spawns/despawns, and with `MapPersistentStateMgr` to ensure pool states are correctly maintained across map instances and reloads.
5.  **Scaling:** Adjusting pool limits dynamically based on active player count if configured (`POOL_FLAG_MAXLIMIT_SCALING_LINEAR`).

---

## Member-by-Member Behavior

### Initialization & Database Loading

*   **`LoadFromDB`**: The core initialization routine. It queries `pool_template` to determine the maximum pool ID and loads template metadata (limits, flags, descriptions). It then unions data from `pool_creature`/`pool_creature_template` and `pool_gameobject`/`pool_gameobject_template` to populate `PoolGroup<Creature>` and `PoolGroup<GameObject>` structures. Finally, it loads `pool_pool` to establish parent-child relationships between pools. During this process, it validates:
    *   That referenced GUIDs exist in `creature` or `gameobject` tables.
    *   That chances are valid (0-100).
    *   That pools do not contain circular references (detected via DFS traversal).
    *   That all entities in a pool reside on compatible maps (specifically, they cannot mix instanceable and non-instanceable maps arbitrarily).
    *   It logs errors for invalid configurations and skips problematic entries.

*   **`Initialize`**: Called when a `MapPersistentState` is created. It iterates through all pool templates marked with `POOL_FLAG_AUTO_SPAWN` (top-level pools not part of events or other pools) and triggers their initial spawn via `InitSpawnPool`.

*   **`InitSpawnPool`**: Checks if a pool is relevant to the current map state (using `CanBeSpawnedAtMap`) and spawns it immediately.

### Core Spawn/Despawn Logic

*   **`SpawnPool`**: Orchestrates the spawning of a specific pool ID. It delegates to `SpawnPoolGroup` for Pools, GameObjects, and Creatures. This ensures that nested pools are handled recursively.

*   **`DespawnPool`**: Removes all active entities belonging to a specific pool ID from the world. It iterates through the respective `PoolGroup`s and calls `DespawnObject` for each.

*   **`UpdatePool`**: Called when an entity within a pool is ready to respawn (e.g., after death). It determines if the entity belongs to a sub-pool or a top-level pool and triggers a spawn attempt for that specific pool, potentially replacing the just-despawned entity with a new one from the pool.

*   **`SpawnPoolInMaps` / `DespawnPoolInMaps`**: Utility functions that apply spawn/despawn actions to all relevant map instances (handling continent instantiation) using worker functors (`SpawnPoolInMapsWorker`, `DespawnPoolInMapsWorker`) executed by `MapPersistentStateMgr`.

*   **`UpdatePoolInMaps`**: A template method that applies `UpdatePool` across all relevant map instances using the `UpdatePoolInMapsWorker<T>` functor. This is used when a specific entity needs to trigger a pool update globally (e.g., after a respawn timer expires on a continent instance).

### Pool Group & Object Management (Template Class `PoolGroup<T>`)

*   **`AddEntry`**: Adds a `PoolObject` to either the `ExplicitlyChanced` or `EqualChanced` list within a `PoolGroup`. If the pool has a `MaxLimit` of 1 and the object has a non-zero chance, it goes to `ExplicitlyChanced`; otherwise, it goes to `EqualChanced`.

*   **`RollOne`**: The heart of the selection algorithm. It attempts to select one entity from the pool to spawn.
    1.  It first checks `ExplicitlyChanced` objects, rolling a random number against their cumulative chances.
    2.  If no explicit match or if the explicit roll fails, it falls back to `EqualChanced` objects, selecting one randomly from those that are eligible (not excluded, not already spawned, unless it's the triggering respawn).
    3.  It respects the `triggerFrom` parameter, allowing the just-despawned entity to be re-selected if appropriate.

*   **`SpawnObject`**: Manages the spawning process up to the `limit` (derived from `MaxLimit`). It handles:
    *   Restoring entities that have pending respawn timers (from previous sessions).
    *   Rolling for new entities via `RollOne`.
    *   Despawning the `triggerFrom` entity if a new one is spawned in its place (swap logic).
    *   Updating the `SpawnedPoolData` cache.

*   **`DespawnObject`**: Iterates through spawned entities in the pool and removes them from the world and the cache. If a specific `guid` is provided, only that entity is removed; otherwise, all are removed.

*   **`Spawn1Object` / `Despawn1Object` / `ReSpawn1Object`**: Low-level implementations for specific types (`Creature`, `GameObject`, `Pool`).
    *   For `Creature`/`GameObject`, they interact with `MapPersistentState` to add/remove entities from grids and the world map. They handle loading data from DB (`LoadFromDB`) and setting respawn timers.
    *   For `Pool`, they delegate back to `PoolManager` methods (`SpawnPool`, `DespawnPool`).

### State Tracking & Helpers

*   **`IsPartOfAPool`**: Template method that checks if a given GUID (Creature, GameObject) or Pool ID is part of any pool. It uses pre-built search maps (`m_creatureSearchMap`, etc.) for O(log n) lookups.

*   **`GetSpawnCount`**: Calculates the effective spawn limit for a pool. If `POOL_FLAG_MAXLIMIT_SCALING_LINEAR` is set, it scales the `MaxLimit` based on the ratio of active sessions to a baseline population (`BLIZZLIKE_REALM_POPULATION`).

*   **`CanBeSpawned`**: Checks if a specific `PoolObject` is eligible to spawn. It considers exclusion flags and special spawn conditions like `FLAG_SPAWN_ENABLE_IF_WORLD_POP_OVER_BLIZZLIKE`.

*   **`SetExcludeObject`**: Marks a specific entity in a pool as excluded from future rolls, typically used during event transitions to prevent immediate re-spawning.

*   **`CheckEventLinkAndReport`**: Validates that all entities in a pool are correctly linked to a game event in the database (`game_event_creature`/`game_event_gameobject`). Logs errors if mismatches are found.

*   **`GetContinentInstanceIdForPool`**: Determines the correct instance ID for a pool if it resides on a continent that supports instantiation. It scans the pool's entities to find their coordinates and queries `MapManager`.

---

## Cross-Unit Boundaries

*   **`World`**:
    *   `GetActiveSessionCount`: Used by `CanBeSpawned` and `GetSpawnCount` to implement population-based scaling and conditional spawning.
    *   `getConfig`: Used to check configuration flags like `CONFIG_BOOL_SAVE_RESPAWN_TIME_IMMEDIATELY` and `CONFIG_BOOL_CONTINENTS_INSTANCIATE`.
    *   `GetWowPatch`: Used during `LoadFromDB` to filter pool entries by patch version.

*   **`MapPersistentStateMgr`**:
    *   `GetMap`, `GetMapId`, `GetSpawnedPoolData`: Accessed extensively by `PoolGroup` methods to manage entity state within a specific map context.
    *   `AddCreatureToGrid`, `RemoveCreatureFromGrid`, `AddGameobjectToGrid`, `RemoveGameobjectFromGrid`: Used to register/unregister entities with the map's spatial partitioning system.
    *   `SaveCreatureRespawnTime`, `SaveGORespawnTime`, `GetCreatureRespawnTime`, `GetGORespawnTime`: Persist and retrieve respawn timers for entities.
    *   `DoForAllStatesWithMapId`: Used by `SpawnPoolInMaps`, `DespawnPoolInMaps`, and `UpdatePoolInMaps` to apply changes across all relevant map instances.

*   **`ObjectMgr`**:
    *   `GetCreatureData`, `GetGOData`: Used to retrieve static spawn data (position, map ID) for validation and spawning.
    *   `operator()` / `operator()#2`: Likely used for general object management or lookup.

*   **`Map` / `MapManager`**:
    *   `GetCreature`, `GetGameObject`: Used to retrieve live object pointers from the map for manipulation (e.g., adding to remove list).
    *   `IsLoaded`: Checked before spawning to avoid processing unloaded grids.
    *   `GetContinentInstanceId`: Used by `GetContinentInstanceIdForPool`.

*   **`GameEventMgr`**:
    *   `LoadFromDB`: Calls `CheckEventLinkAndReport` and `RemoveAutoSpawnForPool` to integrate pools with the event system.
    *   `GameEventSpawn`, `GameEventUnspawn`: Call `SetExcludeObject`, `SpawnPoolInMaps`, and `DespawnPoolInMaps` to manage pool states during events.

*   **`ChatHandler`**:
    *   Various commands (`HandlePoolUpdateCommand`, `HandleLookupPoolCommand`, etc.) call `GetSpawnedObjects`, `GetPoolTemplate`, `GetPoolCreatures`, `GetPoolGameObjects`, `GetPoolPools`, and `IsPartOfAPool` to provide administrative tools for inspecting and manipulating pools.

*   **`Creature` / `GameObject`**:
    *   Directly instantiated and manipulated by `Spawn1Object` and `Despawn1Object`. Methods like `LoadFromDB`, `SetRespawnTime`, `AddObjectToRemoveList` are called on these objects.

*   **`Log`**:
    *   `Out`: Used extensively for logging errors, warnings, and informational messages during loading and validation.

*   **`shared_Util`**:
    *   `rand_chance`, `urand`: Used in `RollOne` for random selection.

---

## Data Model

`PoolManager` interacts with the following database tables:

*   **`pool_template`**: Defines the pool itself.
    *   `entry`: Primary key, unique pool ID.
    *   `max_limit`: Maximum number of entities from this pool that can be active simultaneously.
    *   `flags`: Bitmask for pool behavior (e.g., `POOL_FLAG_AUTO_SPAWN`, `POOL_FLAG_MAXLIMIT_SCALING_LINEAR`).
    *   `description`: Human-readable name.
    *   `instance`: Instance ID for continent instantiation.
    *   `patch_min`, `patch_max`: Patch version range for which this pool is active.

*   **`pool_creature`**: Links creature spawns to a pool.
    *   `guid`: Foreign key to `creature.guid`.
    *   `pool_entry`: Foreign key to `pool_template.entry`.
    *   `chance`: Weighted chance for this creature to be selected (0 for equal chance).
    *   `flags`: Spawn-specific flags.
    *   `patch_min`, `patch_max`: Patch version range.

*   **`pool_creature_template`**: Similar to `pool_creature`, but links by creature entry ID instead of GUID, allowing template-based pooling.
    *   `id`: Foreign key to `creature_template.entry`.
    *   Other columns similar to `pool_creature`.

*   **`pool_gameobject`**: Links gameobject spawns to a pool.
    *   `guid`: Foreign key to `gameobject.guid`.
    *   `pool_entry`: Foreign key to `pool_template.entry`.
    *   `chance`, `flags`, `patch_min`, `patch_max`: Similar to `pool_creature`.

*   **`pool_gameobject_template`**: Similar to `pool_gameobject`, but links by gameobject entry ID.
    *   `id`: Foreign key to `gameobject_template.entry`.
    *   Other columns similar to `pool_gameobject`.

*   **`pool_pool`**: Defines hierarchical relationships between pools.
    *   `pool_id`: Child pool ID.
    *   `mother_pool`: Parent pool ID.
    *   `chance`: Weighted chance for this child pool to be selected within the parent.
    *   `flags`: Flags for the relationship.

*   **`creature` / `gameobject`**: Referenced indirectly to validate GUIDs and retrieve spawn positions/map IDs.

---

## Notable Implementation Details

1.  **Circular Dependency Detection**: During `LoadFromDB`, after loading `pool_pool` entries, the code performs a depth-first search (DFS) using `m_poolSearchMap` to detect cycles. If a cycle is found, it breaks the last link in the chain and logs an error. This prevents infinite recursion during spawn/despawn operations.

2.  **Map Consistency Validation**: The `PoolMapChecker` helper ensures that all entities within a single pool reside on compatible maps. Specifically, it prevents mixing instanceable and non-instanceable maps within the same pool, which would cause state synchronization issues.

3.  **Dual Chance System**: `PoolGroup` maintains two lists: `ExplicitlyChanced` and `EqualChanced`. Entities with a non-zero chance and a pool limit of 1 go to `ExplicitlyChanced`, allowing for precise weighted probabilities. Others go to `EqualChanced`, where they have an equal probability of being selected. `RollOne` prioritizes `ExplicitlyChanced` rolls.

4.  **Respawn Timer Persistence**: When an entity is spawned but not "instantly" (e.g., during initial map load or event start), its respawn timer is saved to the database via `MapPersistentState`. This allows pools to resume their state correctly after a server restart.

5.  **Population Scaling**: If `POOL_FLAG_MAXLIMIT_SCALING_LINEAR` is set, `GetSpawnCount` dynamically adjusts the pool's `MaxLimit` based on the current number of active sessions relative to a baseline (`BLIZZLIKE_REALM_POPULATION`). This allows servers to scale mob density with player count.

6.  **Trigger-Based Replacement**: When an entity in a pool dies and respawns (`UpdatePool`), the system attempts to spawn a replacement. If the pool limit is reached, it may despawn another entity from the same pool to make room, or simply refresh the existing one. The `triggerFrom` parameter in `SpawnObject` helps manage this swap logic.

7.  **Exclusion Mechanism**: `SetExcludeObject` allows temporarily excluding an entity from pool rolls. This is primarily used by `GameEventMgr` to prevent entities from spawning immediately after an event ends or begins, ensuring smooth transitions.

8.  **Template Specialization**: Heavy use of template specialization for `Creature`, `GameObject`, and `Pool` types allows the same `PoolGroup` logic to handle different entity types with minimal code duplication, while delegating type-specific operations (like DB loading or grid management) to specialized methods.

---

## Member Reference

*   **`CanBeSpawned`**: Checks if a `PoolObject` is eligible to spawn, considering exclusion flags and population-based conditions.
*   **`GetSpawnCount`**: Calculates the effective spawn limit for a pool, applying linear scaling if configured.
*   **`GetSpawnedObjects`**: Returns the number of currently spawned entities for a given pool ID from the cache.
*   **`IsSpawnedObject`**: Checks if a specific entity (Creature/GameObject/Pool) is currently marked as spawned in the cache.
*   **`IsSpawnedObject#2`**: Overload for `GameObject`.
*   **`IsSpawnedObject#3`**: Overload for `Pool`.
*   **`PoolObject`**: Constructor for `PoolObject`, initializing GUID, chance, and flags.
*   **`AddSpawn`**: Adds a spawned entity to the cache and increments the pool's spawn count.
*   **`AddSpawn#2`**: Overload for `GameObject`.
*   **`AddSpawn#3`**: Overload for `Pool`.
*   **`RemoveSpawn`**: Removes a spawned entity from the cache and decrements the pool's spawn count.
*   **`RemoveSpawn#2`**: Overload for `GameObject`.
*   **`RemoveSpawn#3`**: Overload for `Pool`.
*   **`PoolGroup<T>`**: Constructor for the template class `PoolGroup`.
*   **`SetPoolId`**: Sets the pool ID for a `PoolGroup`.
*   **`~PoolGroup<T>`**: Destructor for `PoolGroup`.
*   **`isEmpty`**: Checks if a `PoolGroup` contains any entities.
*   **`Despawn1Object#4`**: Declaration for despawning a single entity (specialized in .cpp).
*   **`CheckEventLinkAndReport#2`**: Validates event linkage for a `PoolObject`, logging errors if mismatched.
*   **`Spawn1Object#4`**: Declaration for spawning a single entity (specialized in .cpp).
*   **`ReSpawn1Object#4`**: Declaration for respawning a single entity (specialized in .cpp).
*   **`RemoveOneRelation#2`**: Declaration for removing a circular dependency link (specialized in .cpp).
*   **`GetPoolObjectRespawnTime#4`**: Declaration for retrieving respawn time (specialized in .cpp).
*   **`CheckEventLinkAndReport#3`**: Overload for `GameObject`.
*   **`GetExplicitlyChanced`**: Returns the list of explicitly chanced entities in a `PoolGroup`.
*   **`GetEqualChanced`**: Returns the list of equally chanced entities in a `PoolGroup`.
*   **`size`**: Returns the total number of entities in a `PoolGroup`.
*   **`CheckEventLinkAndReport#4`**: Overload for `Pool`.
*   **`~PoolManager`**: Destructor for `PoolManager`.
*   **`AddEntry`**: Adds a `PoolObject` to a `PoolGroup`, categorizing it by chance.
*   **`GetMaxPoolId`**: Returns the highest pool ID loaded from the database.
*   **`CheckPool#2`**: Validates the integrity of chances within a `PoolGroup`.
*   **`CheckEventLinkAndReport#5`**: Validates event linkage for all entities in a `PoolGroup`.
*   **`SetExcludeObject#3`**: Excludes or includes a specific entity in a `PoolGroup` from future rolls.
*   **`RemoveAutoSpawnForPool`**: Removes the `POOL_FLAG_AUTO_SPAWN` flag from a pool, preventing automatic initialization.
*   **`GetPoolTemplate`**: Retrieves the template data for a specific pool ID.
*   **`GetPoolCreatures`**: Retrieves the `PoolGroup<Creature>` for a specific pool ID.
*   **`GetPoolGameObjects`**: Retrieves the `PoolGroup<GameObject>` for a specific pool ID.
*   **`GetPoolPools`**: Retrieves the `PoolGroup<Pool>` for a specific pool ID.
*   **`RollOne`**: Selects one entity from a `PoolGroup` based on chances and availability.
*   **`IsPartOfAPool`**: Checks if a Creature is part of any pool and returns the pool ID.
*   **`IsPartOfAPool#2`**: Checks if a GameObject is part of any pool and returns the pool ID.
*   **`DespawnObject`**: Despawns all or a specific entity from a `PoolGroup`.
*   **`IsPartOfAPool#3`**: Checks if a Pool is part of any parent pool and returns the parent pool ID.
*   **`Despawn1Object`**: Despawns a single Creature from the world and cache.
*   **`Despawn1Object#2`**: Despawns a single GameObject from the world and cache.
*   **`Despawn1Object#3`**: Despawns a single Pool (delegates to `PoolManager`).
*   **`RemoveOneRelation`**: Removes a specific child pool from a parent pool to break circular dependencies.
*   **`SpawnObject`**: Spawns entities in a `PoolGroup` up to the limit, handling replacements and timers.
*   **`GetPoolObjectRespawnTime`**: Retrieves the respawn time for a Creature from the map state.
*   **`GetPoolObjectRespawnTime#2`**: Retrieves the respawn time for a GameObject from the map state.
*   **`GetPoolObjectRespawnTime#3`**: Returns 0 for Pools (no direct respawn time).
*   **`Spawn1Object`**: Spawns a single Creature, loading data and adding to the map.
*   **`Spawn1Object#2`**: Spawns a single GameObject, loading data and adding to the map.
*   **`Spawn1Object#3`**: Spawns a single Pool (delegates to `PoolManager`).
*   **`ReSpawn1Object`**: Respawns a single Creature by adding it back to the map.
*   **`ReSpawn1Object#2`**: Respawns a single GameObject by adding it back to the map.
*   **`ReSpawn1Object#3`**: No-op for Pools.
*   **`PoolManager`**: Constructor for `PoolManager`.
*   **`PoolMapChecker`**: Helper struct for validating map consistency during loading.
*   **`CheckAndRemember`**: Validates and records the map entry for a pool entity.
*   **`CheckPoolAndChance`**: Validates pool ID range and chance values.
*   **`LoadFromDB`**: Loads all pool data from the database, validates, and builds in-memory structures.
*   **`GetContinentInstanceIdForPool`**: Determines the instance ID for a pool on a continent.
*   **`Initialize`**: Initializes auto-spawning pools for a new map state.
*   **`SpawnPoolGroup`**: Spawns a specific type of pool group (Creature/GameObject/Pool).
*   **`SpawnPoolGroup#2`**: Overload for `GameObject`.
*   **`SpawnPoolGroup#3`**: Overload for `Pool`.
*   **`SpawnPool`**: Spawns all entities in a pool.
*   **`DespawnPool`**: Despawns all entities in a pool.
*   **`CheckPool`**: Validates the integrity of a pool's chances.
*   **`CheckEventLinkAndReport`**: Validates event linkage for all entities in a pool.
*   **`SetExcludeObject`**: Excludes/includes a Creature from a pool.
*   **`SetExcludeObject#2`**: Excludes/includes a GameObject from a pool.
*   **`SpawnPoolInMapsWorker`**: Functor for spawning a pool across all map instances.
*   **`operator()#2`**: Executes the spawn action for `SpawnPoolInMapsWorker`.
*   **`SpawnPoolInMaps`**: Spawns a pool across all relevant map instances.
*   **`DespawnPoolInMapsWorker`**: Functor for despawning a pool across all map instances.
*   **`operator()`**: Executes the despawn action for `DespawnPoolInMapsWorker`.
*   **`DespawnPoolInMaps`**: Despawns a pool across all relevant map instances.
*   **`InitSpawnPool`**: Initializes and spawns a pool for a specific map state.
*   **`UpdatePoolInMapsWorker<T>`**: Functor for updating a pool across all map instances.
*   **`operator()#3`**: Executes the update action for `UpdatePoolInMapsWorker`.

---

<!-- machine-true, projected from graph.json -->

## Map — PoolManager

*Source:* PoolManager.cpp, PoolManager.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| CanBeSpawned | method | World/GetActiveSessionCount | — | — |
| GetSpawnCount | method | World/GetActiveSessionCount | — | — |
| GetSpawnedObjects | method | — | ChatHandler.MiscCommands/HandlePoolUpdateCommand | — |
| IsSpawnedObject | method | — | — | — |
| IsSpawnedObject#2 | method | — | — | — |
| PoolObject | ctor | — | — | — |
| IsSpawnedObject#3 | method | — | — | — |
| AddSpawn | method | — | — | — |
| AddSpawn#2 | method | — | — | — |
| AddSpawn#3 | method | — | — | — |
| RemoveSpawn | method | — | — | — |
| RemoveSpawn#2 | method | — | — | — |
| RemoveSpawn#3 | method | — | — | — |
| PoolGroup<T> | ctor | — | — | — |
| SetPoolId | function | — | — | — |
| ~PoolGroup<T> | dtor | — | — | — |
| isEmpty | function | — | — | — |
| Despawn1Object#4 | decl | — | — | — |
| CheckEventLinkAndReport#2 | method | Log.Main/Out | — | — |
| Spawn1Object#4 | decl | — | — | — |
| ReSpawn1Object#4 | decl | — | — | — |
| RemoveOneRelation#2 | decl | — | — | — |
| GetPoolObjectRespawnTime#4 | decl | — | — | — |
| CheckEventLinkAndReport#3 | method | Log.Main/Out | — | — |
| GetExplicitlyChanced | function | — | — | — |
| GetEqualChanced | function | — | — | — |
| size | function | — | — | — |
| CheckEventLinkAndReport#4 | method | — | — | — |
| ~PoolManager | dtor | — | — | — |
| AddEntry | function | — | — | — |
| GetMaxPoolId | method | — | ChatHandler.LookupCommands/HandleLookupPoolCommand, ChatHandler.LookupCommands/HandlePoolListCommand | — |
| CheckPool#2 | function | — | — | — |
| CheckEventLinkAndReport#5 | function | — | — | — |
| SetExcludeObject#3 | function | — | — | — |
| RemoveAutoSpawnForPool | method | — | GameEventMgr.Main/LoadFromDB | — |
| GetPoolTemplate | method | — | ChatHandler.LookupCommands/HandleLookupPoolCommand, ChatHandler.LookupCommands/HandlePoolListCommand, ChatHandler.LookupCommands/ShowPoolListHelper, ChatHandler.MiscCommands/HandlePoolInfoCommand, ChatHandler.MiscCommands/HandlePoolUpdateCommand | — |
| GetPoolCreatures | method | — | ChatHandler.LookupCommands/ShowPoolListHelper, ChatHandler.MiscCommands/HandlePoolInfoCommand | — |
| GetPoolGameObjects | method | — | ChatHandler.LookupCommands/ShowPoolListHelper, ChatHandler.MiscCommands/HandlePoolInfoCommand, GameObject/Delete, GameObject/JustDespawnedWaitingRespawn | — |
| GetPoolPools | method | — | ChatHandler.LookupCommands/ShowPoolListHelper, ChatHandler.MiscCommands/HandlePoolInfoCommand | — |
| RollOne | function | shared_Util/rand_chance, shared_Util/urand | — | — |
| IsPartOfAPool | method | — | ChatHandler.MiscCommands/HandlePoolSpawnsCommand, Creature.Main/Update, GameEventMgr.Main/GameEventSpawn, GameEventMgr.Main/GameEventUnspawn, ObjectMgr/operator() | — |
| IsPartOfAPool#2 | method | — | ChatHandler.MiscCommands/HandlePoolSpawnsCommand, ChatHandler.ObjectCommands/HandleGameObjectTargetCommand, GameEventMgr.Main/GameEventSpawn, GameEventMgr.Main/GameEventUnspawn, GameObject/Delete, GameObject/JustDespawnedWaitingRespawn, ObjectMgr/operator()#2 | — |
| DespawnObject | function | MapPersistentStateMgr/GetSpawnedPoolData | — | — |
| IsPartOfAPool#3 | method | — | ChatHandler.MiscCommands/HandlePoolInfoCommand | — |
| Despawn1Object | method | CreatureData/GetObjectGuid, Map.Main/GetCreature, MapPersistentStateMgr/GetMap, MapPersistentStateMgr/GetMapId, MapPersistentStateMgr/RemoveCreatureFromGrid, ObjectMgr/GetCreatureData, WorldObject.Object/AddObjectToRemoveList | — | — |
| Despawn1Object#2 | method | Map.Main/GetGameObject, MapPersistentStateMgr/GetMap, MapPersistentStateMgr/GetMapId, MapPersistentStateMgr/RemoveGameobjectFromGrid, ObjectGuid/ObjectGuid#3, ObjectMgr/GetGOData, WorldObject.Object/AddObjectToRemoveList | — | — |
| Despawn1Object#3 | method | — | — | — |
| RemoveOneRelation | method | — | — | — |
| SpawnObject | function | MapPersistentStateMgr/GetSpawnedPoolData | — | — |
| GetPoolObjectRespawnTime | method | MapPersistentStateMgr/GetCreatureRespawnTime | — | — |
| GetPoolObjectRespawnTime#2 | method | MapPersistentStateMgr/GetGORespawnTime | — | — |
| GetPoolObjectRespawnTime#3 | method | — | — | — |
| Spawn1Object | method | Creature.Main/Creature, Creature.Main/GetRespawnDelay, Creature.Main/IsWorldBoss, Creature.Main/LoadFromDB, Creature.Main/SaveRespawnTime, Creature.Main/SetRespawnTime, CreatureData/GetRandomRespawnTime, Map.Main/IsLoaded, MapPersistentStateMgr/AddCreatureToGrid, MapPersistentStateMgr/GetMap, MapPersistentStateMgr/GetMapId, MapPersistentStateMgr/SaveCreatureRespawnTime, ObjectMgr/GetCreatureData, World/getConfig | — | — |
| Spawn1Object#2 | method | Errors/PrintStacktraceAndThrow, GameObject/ComputeRespawnDelay#2, GameObject/CreateGameObject, GameObject/GetRespawnDelay, GameObject/isSpawnedByDefault, GameObject/LoadFromDB, GameObject/SaveRespawnTime, GameObject/SetRespawnDelay, GameObject/SetRespawnTime, GameObjectData/GetRandomRespawnTime, Map.Main/IsLoaded, MapPersistentStateMgr/AddGameobjectToGrid, MapPersistentStateMgr/GetMap, MapPersistentStateMgr/GetMapId, MapPersistentStateMgr/SaveGORespawnTime, ObjectMgr/GetGOData, World/getConfig | — | — |
| Spawn1Object#3 | method | — | — | — |
| ReSpawn1Object | method | CreatureData/GetObjectGuid, Map.Main/GetCreature, MapPersistentStateMgr/GetMap, MapPersistentStateMgr/GetMapId, ObjectMgr/GetCreatureData, WorldObject.Object/GetMap | — | — |
| ReSpawn1Object#2 | method | Map.Main/GetGameObject, MapPersistentStateMgr/GetMap, MapPersistentStateMgr/GetMapId, ObjectGuid/ObjectGuid#3, ObjectMgr/GetGOData, WorldObject.Object/GetMap | — | — |
| ReSpawn1Object#3 | method | — | — | — |
| PoolManager | ctor | — | — | — |
| PoolMapChecker | ctor | — | — | — |
| CheckAndRemember | method | Log.Main/Out, MapEntry/Instanceable | — | — |
| CheckPoolAndChance | function | Log.Main/Out | — | — |
| LoadFromDB | method | Database/PQuery, Database/Query, Field/GetCppString, Field/GetFloat, Field/GetUInt16, Field/GetUInt32, Field/GetUInt8, Log.Main/Out, MapEntry/IsContinent, ObjectMgr/GetCreatureData, ObjectMgr/GetGOData, PoolTemplateData/IsAutoSpawn, ProgressBar/BarGoLink, ProgressBar/BarGoLink#3, ProgressBar/step, QueryResult/Fetch, QueryResult/GetRowCount, QueryResult/NextRow, World/getConfig, World/GetWowPatch | World/SetInitialWorldSettings | creature, gameobject, pool_creature, pool_creature_template, pool_gameobject, pool_gameobject_template, pool_pool, pool_template |
| GetContinentInstanceIdForPool | method | MapManager/GetContinentInstanceId, ObjectMgr/GetCreatureData, ObjectMgr/GetGOData | — | — |
| Initialize | method | PoolTemplateData/IsAutoSpawn | MapPersistentStateMgr/InitPools | — |
| SpawnPoolGroup | method | — | — | — |
| SpawnPoolGroup#2 | method | — | — | — |
| SpawnPoolGroup#3 | method | — | — | — |
| SpawnPool | method | — | — | — |
| DespawnPool | method | — | — | — |
| CheckPool | method | — | — | — |
| CheckEventLinkAndReport | method | — | GameEventMgr.Main/LoadFromDB | — |
| SetExcludeObject | method | — | GameEventMgr.Main/GameEventSpawn, GameEventMgr.Main/GameEventUnspawn | — |
| SetExcludeObject#2 | method | — | GameEventMgr.Main/GameEventSpawn, GameEventMgr.Main/GameEventUnspawn | — |
| SpawnPoolInMapsWorker | ctor | — | — | — |
| operator()#2 | method | — | — | — |
| SpawnPoolInMaps | method | — | GameEventMgr.Main/GameEventSpawn | — |
| DespawnPoolInMapsWorker | ctor | — | — | — |
| operator() | method | — | — | — |
| DespawnPoolInMaps | method | — | GameEventMgr.Main/GameEventUnspawn | — |
| InitSpawnPool | method | MapPersistentStateMgr/GetMapEntry, PoolTemplateData/CanBeSpawnedAtMap | GameEventMgr.Main/Initialize#2 | — |
| UpdatePoolInMapsWorker<T> | ctor | — | — | — |
| operator()#3 | function | — | — | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `creature`: guid int(10) unsigned PK, id mediumint(8) unsigned, id2 mediumint(8) unsigned, id3 mediumint(8) unsigned, id4 mediumint(8) unsigned, id5 mediumint(8) unsigned, map smallint(5) unsigned, position_x float, position_y float, position_z float, orientation float, spawntimesecsmin int(10) unsigned, spawntimesecsmax int(10) unsigned, wander_distance float, health_percent float, mana_percent float unsigned, movement_type tinyint(3) unsigned, spawn_flags int(10) unsigned, visibility_mod float?, patch_min tinyint(3) unsigned, patch_max tinyint(3) unsigned
- `gameobject`: guid int(10) unsigned PK, id mediumint(8) unsigned, map smallint(5) unsigned, position_x float, position_y float, position_z float, orientation float, rotation0 float, rotation1 float, rotation2 float, rotation3 float, spawntimesecsmin int(11), spawntimesecsmax int(11), animprogress tinyint(3) unsigned, state tinyint(3) unsigned, spawn_flags int(10) unsigned, visibility_mod float?, patch_min tinyint(3) unsigned, patch_max tinyint(3) unsigned
- `pool_creature`: guid int(10) unsigned PK, pool_entry smallint(5) unsigned, chance float unsigned, description varchar(255), flags int(10) unsigned, patch_min tinyint(3) unsigned, patch_max tinyint(3) unsigned
- `pool_creature_template`: id int(10) unsigned PK, pool_entry smallint(5) unsigned, chance float unsigned, description varchar(255), flags int(10) unsigned, patch_min tinyint(3) unsigned, patch_max tinyint(3) unsigned
- `pool_gameobject`: guid int(10) unsigned PK, pool_entry smallint(5) unsigned, chance float unsigned, description varchar(255), flags int(10) unsigned, patch_min tinyint(3) unsigned, patch_max tinyint(3) unsigned
- `pool_gameobject_template`: id int(10) unsigned PK, pool_entry smallint(5) unsigned, chance float unsigned, description varchar(255), flags int(10) unsigned, patch_min tinyint(3) unsigned, patch_max tinyint(3) unsigned
- `pool_pool`: pool_id smallint(5) unsigned PK, mother_pool smallint(5) unsigned, chance float, description varchar(255), flags int(10) unsigned
- `pool_template`: entry smallint(5) unsigned PK, max_limit int(10) unsigned, description varchar(255), flags int(11) unsigned, instance mediumint(8), patch_min tinyint(3) unsigned PK, patch_max tinyint(3) unsigned PK

*`?` = nullable, `PK` = primary key column.*


---

<!-- verify: failed-members | invented: operator -->
