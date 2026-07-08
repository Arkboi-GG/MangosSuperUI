# WorldPersistentState

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# WorldPersistentState

**Purpose & Responsibilities**

`WorldPersistentState` is a specialized subclass of `MapPersistentState` within the `wowvmangos` codebase, designed to manage persistent data for **non-instanceable maps** (i.e., open-world zones such as Elwynn Forest or Durotar). Unlike dungeon or battleground instances, which are unique copies tied to specific groups or players, world maps are shared by all players. Consequently, `WorldPersistentState` holds global state for a specific map ID, primarily tracking creature and game object respawn timers and pool spawn states.

Its primary responsibility is to ensure that respawn data for world entities remains consistent and available even when the map itself is unloaded from memory. It acts as a lightweight container that prevents premature deletion of critical timing data while allowing the heavy `Map` object to be freed if no players are currently present in that zone.

**Member-by-Member Behavior**

The `WorldPersistentState` class is minimal, inheriting most of its functionality from `MapPersistentState`. Its two defined members in this unit are:

1.  **Constructor (`WorldPersistentState`)**: Initializes the base `MapPersistentState` with the provided `MapId` and `InstanceId`. For world maps, the `InstanceId` is typically irrelevant or set to a default value (often 0 or the map ID itself, depending on how the manager passes it), as these maps do not have unique instance identifiers in the same way dungeons do. The constructor establishes the link between this state object and the specific map definition.
2.  **Destructor (`~WorldPersistentState`)**: Cleans up the `WorldPersistentState` object. Since `WorldPersistentState` does not own any additional resources beyond those in `MapPersistentState`, this destructor simply invokes the base class destructor chain. It is overridden explicitly to satisfy the virtual destructor requirement of the base class, ensuring correct polymorphic deletion when managed via `MapPersistentState*` pointers.

**Cross-Unit Boundaries**

*   **Called by `MapPersistentStateMgr::AddPersistentState`**: The `MapPersistentStateMgr` (defined in `MapPersistentStateMgr.h`) is responsible for factory-creating the appropriate `MapPersistentState` subclass based on the map type. When `AddPersistentState` determines that a map is *not* instanceable (via `MapEntry::Instanceable()`), it instantiates a `WorldPersistentState`. This boundary crossing transfers ownership of the new state object to the manager, which stores it in `m_instanceSaveByMapId` for future retrieval.
*   **Inherits from `MapPersistentState`**: All other behaviors, including respawn time management (`SaveCreatureRespawnTime`, `GetCreatureRespawnTime`), pool initialization (`InitPools`), and grid object tracking (`AddCreatureToGrid`), are inherited from `MapPersistentState`. `WorldPersistentState` relies entirely on these base methods to perform its duties.

**Data Model**

`WorldPersistentState` does not directly interact with database tables in its own implementation. However, it manages data that is persisted to and loaded from the database by the `MapPersistentStateMgr` and the base `MapPersistentState` class. Specifically:
*   **Creature Respawns**: Managed via `m_creatureRespawnTimes` (inherited). These values correspond to entries in the `creature_respawn` table (implied by `LoadCreatureRespawnTimes` in the manager).
*   **Game Object Respawns**: Managed via `m_goRespawnTimes` (inherited). These correspond to the `gameobject_respawn` table.
*   **Pool Spawns**: Managed via `m_spawnedPoolData` (inherited). This relates to the `pool_template` and associated spawn tables, though the exact persistence mechanism for pool states is handled by the `PoolManager` and potentially custom save logic in the base class.

No direct SQL queries or table manipulations occur within `WorldPersistentState` itself.

**Notable Implementation Details**

*   **Minimal Override**: The class overrides `CanBeUnload()` from `MapPersistentState`. While the signature is present in the header, the implementation is not shown in the provided source snippet for `WorldPersistentState` specifically, implying it likely uses the default behavior from `MapPersistentState` or a simple variant. In `MapPersistentState`, `CanBeUnload()` returns `!m_usedByMap`. For world maps, this means the state can be unloaded if no `Map` object is currently holding a reference to it. However, because world maps are rarely "unloaded" in the same aggressive manner as unused dungeons, this check ensures that if the map *is* loaded, the state persists.
*   **Instance ID Handling**: The constructor takes a `uint16 instanceId`, but for world maps, this parameter is largely semantic. The `MapPersistentStateMgr` uses `m_instanceSaveByMapId` to store world states, keyed by `MapId`, ignoring the `InstanceId` for lookup purposes. This distinguishes it from `DungeonPersistentState`, which is keyed by `InstanceId`.
*   **Friend Class**: `MapPersistentStateManager` is declared as a friend, allowing it to access protected members of `MapPersistentState` (and by extension `WorldPersistentState`) for management tasks like unloading or resetting.

## Member Reference

**WorldPersistentState**
Constructor that initializes the base `MapPersistentState` with the given `MapId` and `InstanceId`. It is called by `MapPersistentStateMgr::AddPersistentState` when creating state for non-instanceable maps.

**~WorldPersistentState**
Virtual destructor that cleans up the `WorldPersistentState` object. It overrides the base class destructor to ensure proper polymorphic deletion. No additional cleanup is performed beyond the base class chain.

---

<!-- machine-true, projected from graph.json -->

## Map — WorldPersistentState

*Source:* MapPersistentStateMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| WorldPersistentState | ctor | — | MapPersistentStateMgr/AddPersistentState | — |
| ~WorldPersistentState | dtor | — | — | — |
