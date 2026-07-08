# WorldMap

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# WorldMap

**Purpose & Responsibilities**

`WorldMap` is a specialized subclass of `Map` that represents the persistent, non-instanced continents of the game world (e.g., Eastern Kingdoms, Kalimdor). Its primary responsibility is to manage the state and lifecycle of these open-world zones, which differ significantly from dungeons, raids, or battlegrounds in that they do not have instance-specific resets, player limits, or temporary existence.

While the base `Map` class handles the heavy lifting of grid management, object storage, scripting, and network updates, `WorldMap` provides the specific interface for retrieving its persistent state (`WorldPersistentState`). It acts as the root container for all objects and players existing in the open world, coordinating with `MapManager` for creation and destruction.

**Member-by-Member Behavior**

The `WorldMap` class is minimal, delegating most functionality to its base class `Map`. Its members are focused on construction, destruction, and state retrieval.

*   **Construction and Destruction**: The constructor initializes the base `Map` with the provided map ID, expiry timer, and instance ID (typically 0 for world maps). The destructor cleans up resources, relying on the base class destructor for the bulk of the cleanup.
*   **State Management**: Although not listed in the MAP for this partial, the class declares `GetPersistanceState` to override the base class method, returning a `WorldPersistentState` pointer. This allows the rest of the system to access world-specific persistent data (such as global creature spawns or game object states that persist across server restarts) without needing to downcast or check map types explicitly.

**Cross-Unit Boundaries**

*   **`MapManager`**: `WorldMap` instances are created by `MapManager::CreateMap` and `MapManager::CreateTestMap`. The `MapManager` is responsible for determining when a world map needs to be loaded (usually at server startup or when the first player enters a previously unloaded grid) and passing the necessary initialization parameters (ID, expiry) to the `WorldMap` constructor.
*   **`WorldPersistentState`**: The `GetPersistanceState` method returns a pointer to a `WorldPersistentState` object. This object is owned and managed externally (likely by `MapManager` or a dedicated persistence manager) and contains the long-term state for the world map. The `WorldMap` itself does not create or destroy this state; it merely provides access to it.

**Data Model**

`WorldMap` does not directly interact with any database tables. It relies on the `Map` base class for any indirect data interactions (such as loading creature/gameobject spawns via `LoadCreatureSpawn` or `LoadGameObjectSpawn`, which query the database through other units). The `WorldMap` class itself contains no SQL queries or direct table references.

**Notable Implementation Details**

*   **Inheritance Hierarchy**: `WorldMap` inherits from `Map`, which inherits from `GridRefManager<NGridType>`. This hierarchy places `WorldMap` at the top of the map type specialization chain, alongside `DungeonMap` and `BattleGroundMap`. Each subclass overrides specific behaviors relevant to its type (e.g., `DungeonMap` handles resets and player limits, `BattleGroundMap` handles battle ground logic), while `WorldMap` remains largely unchanged from the base `Map` behavior because the open world is the "default" case.
*   **Persistent State Override**: The `using Map::GetPersistentState;` declaration in `WorldMap` is crucial. It hides the base class `GetPersistentState` method, forcing callers to use the overridden `GetPersistanceState` (note the spelling difference: `Persistance` vs `Persistent` in the base class, though both return `MapPersistentState*` or derived types). This ensures type safety when accessing world-specific state.
*   **No Instance Logic**: Unlike `DungeonMap` or `BattleGroundMap`, `WorldMap` does not override `CanEnter`, `Add`, `Remove`, or `Update`. This confirms that world maps do not have entry restrictions, special player addition/removal logic, or custom update cycles beyond what the base `Map` provides.

## Member Reference

**WorldMap**
Constructor that initializes the base `Map` class with the given map ID, expiry timer, and instance ID (usually 0). It is called by `MapManager::CreateMap` and `MapManager::CreateTestMap` to instantiate world maps.

**~WorldMap**
Destructor that cleans up the `WorldMap` object. It relies on the base `Map` destructor to handle the majority of resource cleanup, such as unloading grids and removing objects.

---

<!-- machine-true, projected from graph.json -->

## Map — WorldMap

*Source:* Map.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| WorldMap | ctor | — | MapManager/CreateMap, MapManager/CreateTestMap | — |
| ~WorldMap | dtor | — | — | — |
