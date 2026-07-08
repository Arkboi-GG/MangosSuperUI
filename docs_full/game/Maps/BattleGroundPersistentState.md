# BattleGroundPersistentState

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# BattleGroundPersistentState

**Purpose & Responsibilities**

`BattleGroundPersistentState` is a specialized subclass of `MapPersistentState` within the `wowvmangos` engine, designed to manage the persistent memory state for BattleGrounds and Arenas. Unlike standard dungeons (`DungeonPersistentState`) or open-world zones (`WorldPersistentState`), BattleGrounds are transient, competitive instances that typically do not involve long-term player binding, complex group management, or scheduled global resets.

The primary responsibility of `BattleGroundPersistentState` is to provide a lightweight container for instance-specific data—such as creature/gameobject respawn timers and pool spawn states—while explicitly disabling the unloading restrictions associated with player bindings. It ensures that the server can track the state of a BattleGround instance (e.g., who has spawned, when mobs respawn) without retaining references to `Player` or `Group` objects, allowing the instance to be unloaded immediately once the map itself is destroyed or becomes inactive.

## Member-by-Member Behavior

The `BattleGroundPersistentState` class is minimal, inheriting most of its functionality from `MapPersistentState`. Its specific members are:

### **BattleGroundPersistentState** (Constructor)
*   **Kind:** Constructor
*   **Behavior:** Initializes the persistent state object for a BattleGround or Arena. It accepts the `MapId` (identifying the zone, e.g., Warsong Gulch) and the `InstanceId` (the unique runtime ID for this specific match).
*   **Implementation:** It delegates initialization to the base class `MapPersistentState(MapId, InstanceId)`. This sets up internal maps for respawn times and grid object GUIDs. It does **not** initialize player or group lists, distinguishing it from `DungeonPersistentState`.

### **~BattleGroundPersistentState** (Destructor)
*   **Kind:** Destructor
*   **Behavior:** Cleans up the `BattleGroundPersistentState` object.
*   **Implementation:** The destructor is empty (`override {}`). All cleanup of internal data structures (respawn times, grid GUIDs, pool data) is handled by the base class `MapPersistentState::~MapPersistentState()`. This design ensures that regardless of the specific instance type (BG, Dungeon, World), the underlying memory resources are released consistently.

## Cross-Unit Boundaries

### **Calls Out**
*   **None.** The `BattleGroundPersistentState` class itself does not make direct calls to other units in its own members. It relies entirely on the base class `MapPersistentState` for data storage and management.

### **Called By**
*   **`MapPersistentStateMgr/AddPersistentState`:**
    *   **Direction:** Incoming.
    *   **Context:** The `MapPersistentStateManager` (specifically its `AddPersistentState` method) is responsible for factory-creating the correct type of persistent state based on the `MapEntry`. When a new BattleGround or Arena is generated, the manager identifies the map type and instantiates a `BattleGroundPersistentState`.
    *   **Why:** This centralizes instance lifecycle management. The manager ensures that the correct subclass is used so that the appropriate unloading rules (no player bindings for BGs) are enforced automatically.

## Data Model

**No Database Tables.**

`BattleGroundPersistentState` does not interact directly with any database tables. It is a purely in-memory structure.
*   **Respawn Times:** While `MapPersistentState` has methods like `SaveCreatureRespawnTime`, these typically update in-memory maps (`m_creatureRespawnTimes`). For BattleGrounds, these respawn times are usually transient and discarded when the instance is unloaded. They are not persisted to disk for BattleGrounds in the same way they might be for permanent dungeon saves.
*   **Persistence:** BattleGround instances are ephemeral. Their state exists only for the duration of the match. Once the map is unloaded, the `BattleGroundPersistentState` is destroyed, and no data is written to or read from the database for this specific instance type.

## Notable Implementation Details

1.  **Minimalist Design:** The class contains no private member variables. It relies entirely on inheritance. This is a deliberate design choice to keep BattleGround state lightweight. There is no need to track `m_playerList` or `m_groupList` because players are not "bound" to a BattleGround instance in the same way they are to a dungeon instance. Players enter and leave freely, and the instance resets completely upon conclusion.

2.  **Unloading Logic:** The key differentiator between `BattleGroundPersistentState` and `DungeonPersistentState` is the `CanBeUnload` logic.
    *   `DungeonPersistentState::CanBeUnload()` returns `false` if there are still players or groups bound to the instance, keeping it in memory until everyone leaves or the instance resets.
    *   `BattleGroundPersistentState::CanBeUnload()` inherits the base behavior, which primarily checks if the map object itself is still using the state (`m_usedByMap`). Since there are no player bindings to hold it, it can be unloaded as soon as the map is destroyed.

3.  **Inheritance Hierarchy:**
    *   `MapPersistentState` (Base): Handles generic map data (respawns, pools, grid objects).
    *   `WorldPersistentState`: For open world zones (non-instanceable).
    *   `DungeonPersistentState`: For dungeons/raids (handles player/group bindings, reset schedules).
    *   `BattleGroundPersistentState`: For BGs/Arenas (transient, no bindings).

4.  **Thread Safety:** The base class `MapPersistentState` uses a `std::shared_timed_mutex` (`m_cellObjectGuidsMutex`) to protect grid object data. `BattleGroundPersistentState` inherits this protection, ensuring that concurrent access to grid spawns (e.g., multiple players triggering spawns) is safe.

## Member Reference

**BattleGroundPersistentState**
Constructor that initializes the BattleGround persistent state. It takes the `MapId` and `InstanceId` and passes them to the base class `MapPersistentState`. It does not initialize any player or group lists, reflecting the transient nature of BattleGrounds.

**~BattleGroundPersistentState**
Destructor that cleans up the object. It is empty, relying on the base class `MapPersistentState::~MapPersistentState()` to handle the destruction of internal data structures like respawn time maps and grid object GUIDs.

---

<!-- machine-true, projected from graph.json -->

## Map — BattleGroundPersistentState

*Source:* MapPersistentStateMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| BattleGroundPersistentState | ctor | — | MapPersistentStateMgr/AddPersistentState | — |
| ~BattleGroundPersistentState | dtor | — | — | — |
