# SpawnedPoolData

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# SpawnedPoolData

**Purpose & Responsibilities**

`SpawnedPoolData` is a lightweight data structure within the `PoolManager` subsystem responsible for tracking the runtime state of spawned entities for a specific map instance. It maintains three distinct sets of identifiers:
1.  **Spawned Creatures:** The database GUIDs of creatures currently active in the world that belong to pools.
2.  **Spawned Gameobjects:** The database GUIDs of game objects currently active in the world that belong to pools.
3.  **Spawned Pools:** The IDs of sub-pools that have been instantiated within a parent pool context.

Additionally, it holds a boolean flag (`m_isInitialized`) to indicate whether the pool system for this specific map instance has completed its initial loading and spawning phase. This structure allows the server to quickly determine if a specific entity is already alive without iterating through the entire map's entity list, and it provides the necessary state for the pool manager to enforce limits (e.g., preventing duplicate spawns or exceeding maximum counts).

It does not perform any spawning or despawning logic itself; it merely records the results of actions performed by `PoolGroup` and `PoolManager`.

## Member-by-Member Behavior

### Initialization State Management

*   **`SpawnedPoolData` (Constructor)**
    Initializes the object by setting `m_isInitialized` to `false`. The internal containers (`mSpawnedCreatures`, `mSpawnedGameobjects`, `mSpawnedPools`) are default-initialized to empty states.

*   **`IsInitialized`**
    Returns the current value of `m_isInitialized`. This is used by `MapPersistentStateMgr::InitPools` to check if the pool system for a map has already been set up, preventing redundant initialization work.

*   **`SetInitialized`**
    Sets `m_isInitialized` to `true`. Called by `MapPersistentStateMgr::InitPools` after the pool templates and initial spawns have been processed for the map.

### Accessing Spawned Entities

*   **`GetSpawnedCreatures`**
    Returns a constant reference to `mSpawnedCreatures`, a `std::set<uint32>` containing the database GUIDs of all creatures currently spawned via pools on this map. This is primarily consumed by chat commands (`ChatHandler::HandlePoolInfoCommand`, `ChatHandler::HandlePoolSpawnsCommand`) to report status to administrators.

*   **`GetSpawnedGameobjects`**
    Returns a constant reference to `mSpawnedGameobjects`, a `std::set<uint32>` containing the database GUIDs of all game objects currently spawned via pools on this map. Like creature data, this is exposed for administrative inspection via `ChatHandler` commands.

*   **`GetSpawnedPools`**
    Returns a constant reference to `mSpawnedPools`, a `std::map<uint32, uint32>` mapping sub-pool IDs to their parent pool IDs (or potentially just tracking active sub-pool IDs depending on usage context in `PoolManager`). This is used by `ChatHandler::HandlePoolInfoCommand` to display hierarchical pool information.

## Cross-Unit Boundaries

### Called By

*   **`MapPersistentStateMgr::InitPools`**
    *   **Interaction:** Calls `IsInitialized` and `SetInitialized`.
    *   **Reason:** During map loading or instance creation, the persistent state manager needs to ensure that the pool system is initialized exactly once. It checks the flag to skip re-initialization if the state is already valid, and sets the flag upon completion.

*   **`ChatHandler::HandlePoolInfoCommand`**
    *   **Interaction:** Calls `GetSpawnedCreatures`, `GetSpawnedGameobjects`, and `GetSpawnedPools`.
    *   **Reason:** When an administrator queries pool information for a specific map or pool, the handler retrieves these sets to print a summary of what is currently active.

*   **`ChatHandler::HandlePoolSpawnsCommand`**
    *   **Interaction:** Calls `GetSpawnedCreatures` and `GetSpawnedGameobjects`.
    *   **Reason:** Similar to `HandlePoolInfoCommand`, this command likely provides a detailed list of spawned entities, requiring direct access to the GUID sets stored in `SpawnedPoolData`.

### Calls Out

*   **None.** `SpawnedPoolData` is a pure data holder. It does not call into other units. All modification of its internal state (adding/removing spawns) is done via template methods (`AddSpawn`, `RemoveSpawn`) defined in the header but implemented inline or elsewhere in the `PoolManager` logic, which are not listed in the MAP as "Calls out" because they operate on the local state or are part of the same logical unit's internal mechanics. The MAP explicitly lists no outgoing calls for the members tracked here.

## Data Model

`SpawnedPoolData` does not interact directly with any database tables. It operates entirely on in-memory data structures populated by the `PoolManager` during runtime. The database tables involved in the broader pool system (such as `pool_template`, `pool_creature`, `pool_gameobject`) are accessed by `PoolManager::LoadFromDB` and related functions, but `SpawnedPoolData` itself is transient runtime state.

## Notable Implementation Details

1.  **Thread Safety:** The class contains no mutexes or atomic operations. Access to `SpawnedPoolData` must be synchronized externally, typically by the map lock or the `PoolManager`'s internal synchronization mechanisms. Since it is accessed by chat commands (which run on the main thread) and pool updates (also main thread in most WoW server architectures), this is generally safe, but concurrent modification from different threads would cause data races.

2.  **Memory Efficiency:** It uses `std::set` for creatures and gameobjects. This provides $O(\log N)$ lookup and insertion times, which is efficient enough for typical pool sizes. Using `std::unordered_set` might offer faster average-case lookups but would require hash functions for `uint32` (trivial) and could have higher memory overhead. The choice of `std::set` suggests an emphasis on ordered iteration (useful for debugging/reporting) and predictable performance.

3.  **No Ownership:** `SpawnedPoolData` stores only GUIDs (identifiers), not pointers to `Creature` or `GameObject` objects. This decouples the pool system from the lifetime of the entities themselves. If a creature dies and despawns, the pool manager must explicitly remove its GUID from this set (via `RemoveSpawn`), ensuring consistency between the physical world state and the pool's logical state.

4.  **Initialization Flag:** The `m_isInitialized` flag is critical for preventing double-spawning. If `MapPersistentStateMgr::InitPools` were to run twice without this check, the pool system might attempt to spawn entities that are already present, leading to duplicates or errors.

## Member Reference

**SpawnedPoolData**
Constructor that initializes `m_isInitialized` to `false`.

**IsInitialized**
Returns the boolean `m_isInitialized`, indicating if the pool system for this map instance has been fully initialized. Called by `MapPersistentStateMgr::InitPools`.

**SetInitialized**
Sets `m_isInitialized` to `true`. Called by `MapPersistentStateMgr::InitPools` after initialization completes.

**GetSpawnedCreatures**
Returns a const reference to the `std::set<uint32>` of spawned creature GUIDs. Used by `ChatHandler::HandlePoolInfoCommand` and `ChatHandler::HandlePoolSpawnsCommand` for reporting.

**GetSpawnedGameobjects**
Returns a const reference to the `std::set<uint32>` of spawned gameobject GUIDs. Used by `ChatHandler::HandlePoolInfoCommand` and `ChatHandler::HandlePoolSpawnsCommand` for reporting.

**GetSpawnedPools**
Returns a const reference to the `std::map<uint32, uint32>` of spawned sub-pools. Used by `ChatHandler::HandlePoolInfoCommand` for reporting hierarchical pool data.

---

<!-- machine-true, projected from graph.json -->

## Map — SpawnedPoolData

*Source:* PoolManager.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| SpawnedPoolData | ctor | — | — | — |
| IsInitialized | method | — | MapPersistentStateMgr/InitPools | — |
| SetInitialized | method | — | MapPersistentStateMgr/InitPools | — |
| GetSpawnedCreatures | method | — | ChatHandler.MiscCommands/HandlePoolInfoCommand, ChatHandler.MiscCommands/HandlePoolSpawnsCommand | — |
| GetSpawnedGameobjects | method | — | ChatHandler.MiscCommands/HandlePoolInfoCommand, ChatHandler.MiscCommands/HandlePoolSpawnsCommand | — |
| GetSpawnedPools | method | — | ChatHandler.MiscCommands/HandlePoolInfoCommand | — |
