# ObjectGridLoader

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ObjectGridLoader

**ObjectGridLoader** implements the visitor-pattern classes responsible for managing the lifecycle of game objects within a specific grid of a `Map`. It handles the loading, unloading, and stopping of `Creature`, `GameObject`, `Corpse`, and `DynamicObject` instances as grids are activated or deactivated in the world simulation.

The unit defines three primary visitor classes for grid-level operations:
1.  **`ObjectGridLoader`**: Loads persistent objects (Creatures, Game Objects) and transient world objects (Corpses) from memory caches into the active world state.
2.  **`ObjectGridUnloader`**: Removes objects from the world, saves their state if necessary, and deletes them from memory.
3.  **`ObjectGridStoper`**: Halts AI and combat states for objects in a grid being deactivated, without necessarily deleting them immediately.

Additionally, it defines helper visitors for specific sub-tasks:
*   **`ObjectGridRespawnMover`**: Relocates creatures whose respawn coordinates lie outside the current grid to prevent them from being lost during grid unloading.
*   **`ObjectWorldLoader`**: Specifically handles the loading of `Corpse` objects, which are treated differently from persistent entities.

## Member-by-Member Behavior

### Grid Loading (`ObjectGridLoader`)

The `ObjectGridLoader` is instantiated for a specific `Cell` within a `Map`. Its primary responsibility is to populate the grid's internal containers with object instances.

*   **`ObjectGridLoader` (ctor)**: Initializes the loader with references to the target `NGridType` (`i_grid`), the parent `Map` (`i_map`), and the specific `Cell` (`i_cell`) being processed. It resets counters for loaded objects to zero.
*   **`Load`**: Orchestrates the loading process for a single `GridType`. It uses a `TypeContainerVisitor` to dispatch calls to the appropriate `Visit` methods for different object types (GameObjects, Creatures). It then instantiates an `ObjectWorldLoader` to handle `Corpse` loading separately, copying the corpse count back to the main loader.
*   **`Visit` (GameObjectMapType)**: Loads `GameObject` instances. It calculates the global cell ID from the local cell coordinates. It acquires shared locks on both the global `ObjectMgr`'s cell loading mutex and the map's persistent state mutex. It retrieves GUID sets for game objects from both sources and passes them to the `LoadHelper` function.
*   **`Visit` (CreatureMapType)**: Loads `Creature` instances. Similar to the GameObject visitor, it calculates the cell ID, acquires locks, retrieves creature GUID sets from `ObjectMgr` and `MapPersistentStateMgr`, and delegates to `LoadHelper`.
*   **`Visit` (CorpseMapType)**: Defined in the header as an empty stub. The actual corpse loading is handled by the `ObjectWorldLoader` helper class invoked in `Load`.
*   **`Visit` (DynamicObjectMapType)**: Defined in the header as an empty stub. Dynamic objects are typically not loaded from disk/grid persistence in this manner.
*   **`LoadN`**: Iterates over all cells (`MAX_NUMBER_OF_CELLS` x `MAX_NUMBER_OF_CELLS`) within the grid. For each cell, it creates a temporary `ObjectGridLoader` (implicitly via the loop structure in the caller or self-recursion pattern, though here it loops internally) and calls `Load` on each sub-grid. Finally, it logs the total counts of loaded GameObjects, Creatures, and Corpses.

### Grid Unloading (`ObjectGridUnloader`)

The `ObjectGridUnloader` removes objects from the world and frees memory.

*   **`ObjectGridUnloader` (ctor)**: Stores a reference to the `NGridType` being unloaded.
*   **`Unload`**: Uses a `TypeContainerVisitor` to dispatch to `Visit` methods for all object types in the grid.
*   **`Visit` (GridRefManager<T>)**: A template method that handles the removal of any object type stored in a `GridRefManager`.
    1.  It iterates through all objects, calling `CleanupsBeforeDelete()` on each to remove cross-references.
    2.  It enters a loop to delete objects one by one.
    3.  If the server configuration `SAVE_RESPAWN_TIME_IMMEDIATELY` is disabled, it calls `SaveRespawnTime()` on the object.
    4.  It calls `RemoveFromWorld()` to detach the object from the world state.
    5.  It checks if the object is already marked for deletion (`IsDeleted()`). If so, it invalidates the link manually to prevent double deletion (common with `DynamicObject`). Otherwise, it deletes the object pointer.
*   **`MoveToRespawnN`**: Iterates over all cells in the grid and invokes `ObjectGridRespawnMover::Move` on each. This ensures creatures are moved to their respawn points before the grid is fully unloaded, preventing loss of respawn data if the respawn location is in a different grid.

### Grid Stopping (`ObjectGridStoper`)

The `ObjectGridStoper` halts active processes in a grid, typically used when a grid is becoming inactive but not yet unloaded.

*   **`ObjectGridStoper` (ctor)**: Stores a reference to the `NGridType`.
*   **`Stop`**: Uses a `TypeContainerVisitor` to dispatch to `Visit` methods.
*   **`Visit` (CreatureMapType)**: Iterates through all creatures in the grid. For each creature:
    1.  Calls `AI()->EnterEvadeMode()` to stop combat and AI routines.
    2.  Calls `DeleteThreatList()` to clear aggro.
    3.  Calls `RemoveAllDynObjects()` to clean up spell effects.
*   **`Visit` (GameObjectMapType)**: Iterates through all game objects and calls `RemoveAllDynObjects()` to clean up spell effects.
*   **`Visit` (GridRefManager<NONACTIVE>)**: An empty template stub for non-active object types.

### Helper Visitors

*   **`ObjectGridRespawnMover`**:
    *   **`ObjectGridRespawnMover` (ctor)**: Default constructor.
    *   **`Move`**: Dispatches to `Visit` methods for the grid.
    *   **`Visit` (CreatureMapType)**: Iterates through creatures. It asserts that the creature is not a pet. It compares the creature's current cell with its respawn cell. If they differ, it calls `CreatureRespawnRelocation` on the map to move the creature to its respawn coordinates immediately. This prevents the creature from being stuck in an unloaded grid when it needs to respawn.
    *   **`Visit` (GridRefManager<T>)**: Empty template stub.

*   **`ObjectWorldLoader`**:
    *   **`ObjectWorldLoader` (ctor)**: Initializes with references to the parent `ObjectGridLoader`'s cell, map, and resets the corpse counter.
    *   **`Visit` (CorpseMapType)**: Calculates the cell ID and acquires a shared lock on the `ObjectMgr`'s cell loading mutex. It retrieves corpse GUIDs and delegates to the `LoadHelper` function for corpses.
    *   **`Visit` (GridRefManager<T>)**: Empty template stub.

### Free Functions & Templates

*   **`AddUnitState`**: Template function specialized for `GameObject` and `Creature`. It constructs a `Cell` from a `CellPair` and calls `SetCurrentCell` on the object. The default template does nothing.
*   **`IsEnabledOnMap`**: Template function checking if an object should be loaded on a specific map instance.
    *   Default template returns `true`.
    *   Specialization for `GameObject`: Checks if the map is a continent instance. If so, it verifies that the object's `instanciatedContinentInstanceId` matches the map's instance ID.
    *   Specialization for `Creature`: Same logic as GameObject.
*   **`LoadHelper` (Generic)**: Template function for loading `GameObject` or `Creature`.
    1.  Checks if the object is enabled on the map via `IsEnabledOnMap`.
    2.  Creates the object instance (using `CreateGameObject` for GOs, `new` for others).
    3.  Calls `LoadFromDB` on the object. If it fails, the object is deleted.
    4.  Adds the object to the grid, sets its current cell, sets its map, adds it to the world, and adds it to the active list if applicable.
    5.  Triggers `Event_AddedToWorld` and notifies the BattleGround if present.
*   **`LoadHelper` (Corpse)**: Overloaded function for `Corpse`.
    1.  Skips if the corpse set is empty.
    2.  Iterates through corpse GUIDs, skipping those not matching the map's instance ID.
    3.  Retrieves the `Corpse` object from `ObjectAccessor`.
    4.  Adds the corpse to the grid, sets its cell/map/world state, and increments the count.

## Cross-Unit Boundaries

*   **`Map.Main`**:
    *   `ObjectGridLoader` calls `Map::getNGrid`, `Map::GetId`, `Map::GetInstanceId`, `Map::IsContinent`, `Map::IsUnloading`, `Map::AddToActive`, and `Map::GetPersistentState`.
    *   `ObjectGridUnloader` calls `Map::CreatureRespawnRelocation`.
    *   `ObjectGridStoper` does not directly call Map methods in its `Visit` implementations but relies on the `Creature` and `GameObject` objects to interact with the map.
*   **`ObjectMgr`**:
    *   `ObjectGridLoader` calls `ObjectMgr::GetCellObjectGuids` and `ObjectMgr::GetCellLoadingObjectsMutex` to retrieve persistent object lists.
    *   `IsEnabledOnMap` calls `ObjectMgr::GetGOData` and `ObjectMgr::GetCreatureData` to check instance validity.
*   **`MapPersistentStateMgr`**:
    *   `ObjectGridLoader` calls `MapPersistentStateMgr::GetCellObjectGuids` and `MapPersistentStateMgr::GetCellObjectGuidsMutex` to retrieve dynamically spawned objects.
*   **`Cell` / `GridDefines`**:
    *   `ObjectGridLoader` and helpers use `Cell::GridX`, `Cell::GridY`, `Cell::CellX`, `Cell::CellY`, `Cell::DiffGrid`, and `MaNGOS::ComputeCellPair` to manage spatial coordinates.
*   **`WorldObject.Object`**:
    *   Helpers call `WorldObject::SetCurrentCell`, `WorldObject::SetMap`, `WorldObject::AddToWorld`, `WorldObject::IsActiveObject`, `WorldObject::GetCurrentCell`, and `WorldObject::GetMap`.
*   **`Creature.Main`**:
    *   `ObjectGridRespawnMover` calls `Creature::IsPet`, `Creature::GetRespawnCoord`, and `Creature::AI`.
    *   `ObjectGridStoper` calls `Creature::DeleteThreatList`.
*   **`SpellCaster`**:
    *   `ObjectGridStoper` calls `SpellCaster::RemoveAllDynObjects` on both Creatures and GameObjects.
*   **`Corpse`**:
    *   `LoadHelper` (Corpse) calls `Corpse::AddToWorld`.
*   **`ObjectAccessor`**:
    *   `LoadHelper` (Corpse) calls `ObjectAccessor::GetCorpseForPlayerGUID`.
*   **`Errors`**:
    *   `IsEnabledOnMap` calls `Errors::PrintStacktraceAndThrow` (via `ASSERT` macro expansion in debug builds, though the map lists it explicitly).

## Data Model

This unit does not directly query database tables using SQL strings. Instead, it relies on cached data structures populated by other systems (`ObjectMgr`, `MapPersistentStateMgr`). The `LoadFromDB` method called on individual objects (`Creature`, `GameObject`) performs the actual database queries, but those implementations reside in the respective object classes. Therefore, no direct table interactions occur in this translation unit.

## Notable Implementation Details

*   **Thread Safety**: The loading process uses `std::shared_lock<std::shared_timed_mutex>` to protect access to `ObjectMgr`'s cell GUID lists and `MapPersistentStateMgr`'s cell GUID lists. This allows multiple readers (grid loaders) to access the data concurrently while preventing writers from modifying the lists during loading.
*   **Respawn Relocation**: The `ObjectGridRespawnMover` is critical for maintaining game consistency. If a creature dies in Grid A but its respawn point is in Grid B, unloading Grid A without moving the creature would cause it to fail to respawn correctly until Grid A is reloaded. By moving it to its respawn coordinates immediately, the system ensures it appears in the correct grid upon respawn.
*   **Double Deletion Prevention**: In `ObjectGridUnloader::Visit`, the check `if (obj->IsDeleted())` handles cases where an object might have been scheduled for deletion elsewhere (e.g., by a spell effect or player action) before the grid unload process reaches it. Invalidating the link manually prevents the destructor from running twice.
*   **Instance Filtering**: The `IsEnabledOnMap` template specializations ensure that objects spawned in specific instance IDs are not loaded onto maps with different instance IDs, preventing visual glitches or logic errors in instanced content.
*   **Corpse Handling**: Corpses are handled separately from persistent entities because they are tied to player GUIDs and are retrieved via `ObjectAccessor` rather than being constructed from static data. They are also filtered by instance ID.

## Member Reference

**ObjectGridRespawnMover** (ctor): Default constructor for the respawn mover visitor.

**ObjectGridLoader** (ctor): Initializes the loader with grid, map, and cell references, resetting object counters.

**Move**: Dispatches the `ObjectGridRespawnMover` to visit all objects in the grid.

**Visit** (ObjectGridRespawnMover, GridRefManager<T>): Empty template stub for non-creature types.

**Visit#3** (method, ObjectGridRespawnMover, CreatureMapType): Iterates creatures, asserting they are not pets, and relocates them to their respawn coordinates if their current cell differs from their respawn cell.

**Visit#5** (method, ObjectGridLoader, GameObjectMapType): Loads GameObjects for the current cell by acquiring locks, retrieving GUIDs from ObjectMgr and PersistentStateMgr, and delegating to LoadHelper.

**ObjectWorldLoader** (ctor): Initializes the corpse loader with references to the parent loader's context.

**AddUnitState#2** (function): Template specialization for `Creature`; sets the creature's current cell.

**AddUnitState** (function): Template specialization for `GameObject`; sets the game object's current cell.

**IsEnabledOnMap#2** (function): Template specialization for `GameObject`; checks if the object belongs to the current map instance.

**IsEnabledOnMap** (function): Template specialization for `Creature`; checks if the object belongs to the current map instance.

**LoadHelper** (function, Generic): Template function that creates, loads from DB, and adds GameObjects or Creatures to the world.

**Visit#4** (method, ObjectGridLoader, GameObjectMapType): *See Visit#5*.

**Visit#2** (method, ObjectGridLoader, CreatureMapType): Loads Creatures for the current cell by acquiring locks, retrieving GUIDs from ObjectMgr and PersistentStateMgr, and delegating to LoadHelper.

**Visit#8** (method, ObjectWorldLoader, CorpseMapType): Calculates the cell ID and acquires a shared lock on the ObjectMgr's cell loading mutex. It retrieves corpse GUIDs and delegates to the LoadHelper function for corpses.

**Load**: Orchestrates loading of GameObjects, Creatures, and Corpses for a single grid.

**LoadN**: Iterates all cells in the grid, loading each one, and logs the results.

**MoveToRespawnN**: Iterates all cells in the grid, invoking the respawn mover for each.

**Unload**: Dispatches the unloader visitor to all objects in the grid.

**Stop**: Dispatches the stopper visitor to all objects in the grid.

**Visit#6** (method, ObjectGridStoper, CreatureMapType): Stops AI, clears threat lists, and removes dynamic objects for all creatures in the grid.

**Visit#7** (method, ObjectGridStoper, GameObjectMapType): Removes dynamic objects for all game objects in the grid.

---

<!-- machine-true, projected from graph.json -->

## Map — ObjectGridLoader

*Source:* ObjectGridLoader.cpp, ObjectGridLoader.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ObjectGridRespawnMover | ctor | — | — | — |
| ObjectGridLoader | ctor | — | Map.Main/EnsureGridLoaded | — |
| Move | method | — | — | — |
| Visit | method | — | — | — |
| Visit#3 | method | — | — | — |
| Visit#5 | method | Cell/Cell#2, Cell/DiffGrid, Creature.Main/GetRespawnCoord, Creature.Main/IsPet, Errors/PrintStacktraceAndThrow, GridDefines/ComputeCellPair, Map.Main/CreatureRespawnRelocation, WorldObject.Object/GetCurrentCell, WorldObject.Object/GetMap | — | — |
| ObjectWorldLoader | ctor | — | — | — |
| AddUnitState#2 | function | Cell/Cell#2, WorldObject.Object/SetCurrentCell | — | — |
| AddUnitState | function | Cell/Cell#2, WorldObject.Object/SetCurrentCell | — | — |
| IsEnabledOnMap#2 | function | Errors/PrintStacktraceAndThrow, Map.Main/GetInstanceId, Map.Main/IsContinent, ObjectMgr/GetGOData | — | — |
| IsEnabledOnMap | function | Errors/PrintStacktraceAndThrow, Map.Main/GetInstanceId, Map.Main/IsContinent, ObjectMgr/GetCreatureData | — | — |
| LoadHelper | function | Corpse/AddToWorld, Map.Main/AddToActive, Map.Main/GetInstanceId, Map.Main/IsUnloading, ObjectAccessor/GetCorpseForPlayerGUID, ObjectGuid/ObjectGuid#2, WorldObject.Object/IsActiveObject, WorldObject.Object/SetMap | — | — |
| Visit#4 | method | Cell/CellX, Cell/CellY, Cell/GridX, Cell/GridY, Map.Main/GetId, Map.Main/getNGrid, Map.Main/GetPersistentState, MapPersistentStateMgr/GetCellObjectGuids, MapPersistentStateMgr/GetCellObjectGuidsMutex, ObjectMgr/GetCellLoadingObjectsMutex, ObjectMgr/GetCellObjectGuids | — | — |
| Visit#2 | method | Cell/CellX, Cell/CellY, Cell/GridX, Cell/GridY, Map.Main/GetId, Map.Main/getNGrid, Map.Main/GetPersistentState, MapPersistentStateMgr/GetCellObjectGuids, MapPersistentStateMgr/GetCellObjectGuidsMutex, ObjectMgr/GetCellLoadingObjectsMutex, ObjectMgr/GetCellObjectGuids | — | — |
| Visit#8 | method | Cell/CellX, Cell/CellY, Cell/GridX, Cell/GridY, Map.Main/GetId, Map.Main/getNGrid, ObjectMgr/GetCellLoadingObjectsMutex, ObjectMgr/GetCellObjectGuids | — | — |
| Load | method | — | — | — |
| LoadN | method | Log.Main/Out, Map.Main/GetId | Map.Main/EnsureGridLoaded | — |
| MoveToRespawnN | method | — | Map.Main/UnloadGrid | — |
| Unload | method | — | — | — |
| Stop | method | — | — | — |
| Visit#6 | method | Creature.Main/AI, CreatureAI/EnterEvadeMode, SpellCaster/RemoveAllDynObjects, Unit.Main/DeleteThreatList | — | — |
| Visit#7 | method | SpellCaster/RemoveAllDynObjects | — | — |
