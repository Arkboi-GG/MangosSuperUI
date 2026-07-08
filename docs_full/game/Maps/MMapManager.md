# MMapManager

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# MMapManager

**Purpose & Responsibilities**

`MMapManager` is the singleton controller for the server’s navigation mesh (NavMesh) system, interfacing with the Detour library. It manages the lifecycle of navigation data for game maps and static game object (GO) models, handling the loading, caching, and unloading of `dtNavMesh` structures. It provides thread-safe access to these meshes while managing thread-local `dtNavMeshQuery` instances, ensuring concurrent pathfinding requests do not corrupt shared state. It does not perform pathfinding calculations directly but supplies the necessary objects to other units.

## Member-by-Member Behavior

### Initialization and Lifecycle

**`MMapManager()`**
Initializes the `loadedTiles` counter to zero. The manager starts empty, relying on explicit load calls to populate caches.

**`~MMapManager()`**
Cleans up all allocated Detour resources by iterating through `loadedMMaps` and `loadedModels` and deleting `MMapData` pointers. The `MMapData` destructor frees associated `dtNavMeshQuery` objects and the `dtNavMesh` itself.

### Data Accessors (Statistics)

**`getLoadedTilesCount()`**
Returns the total number of navigation tiles currently held in memory across all loaded maps.

**`getLoadedMapsCount()`**
Returns the number of distinct map IDs for which navigation data is currently loaded, reflecting the size of `loadedMMaps`.

### Cross-Unit Collaboration

**Called by: `ChatHandler.DebugCommands/HandleMmapStatsCommand`**
The debug command handler in `ChatHandler` calls `getLoadedTilesCount()` and `getLoadedMapsCount()` to provide administrators with real-time statistics about the navigation mesh system's memory footprint.

**Called by: `MoveMap/createOrGetMMapManager`**
The `MMapFactory::createOrGetMMapManager()` function (in `MoveMap.cpp`) is the sole entry point for obtaining an instance of `MMapManager`, implementing the singleton pattern to ensure all pathfinding components share the same navigation data cache.

## Data Model

This unit does not interact directly with any database tables. All navigation data is loaded from binary files and managed entirely in RAM.

## Notable Implementation Details

### Thread-Safety Strategy
Detour’s `dtNavMeshQuery` objects are not thread-safe. `MMapManager` employs a hybrid locking strategy:
1.  **Mesh Access:** `dtNavMesh` objects are read-only after construction. Access to `loadedMMaps` is protected by a `std::shared_timed_mutex` (`loadedMMaps_lock`), allowing concurrent readers while blocking writers.
2.  **Query Isolation:** Each thread gets its own dedicated `dtNavMeshQuery` instance, stored in `navMeshQueries` within `MMapData` keyed by `std::thread::id`. This eliminates contention on query objects. The `navMeshQueries_lock` protects creation and retrieval of these per-thread objects.

### Memory Management
Custom Detour allocators (`dtCustomAlloc` and `dtCustomFree`) wrap standard `new[]` and `delete[]` operations, integrating Detour’s memory management with the C++ heap.

### Tile Identification
Navigation data is organized by map ID and grid coordinates (x, y). The private static helper `packTileID` combines x and y coordinates into a single `uint32` key for efficient storage in `MMapTileSet`.

## Member Reference

**`MMapManager`**
Constructor that initializes the `loadedTiles` counter to zero. It sets up the empty state of the manager, ready to accept load requests. No data is loaded at this stage.

**`getLoadedTilesCount`**
Returns the current value of the `loadedTiles` member variable, representing the total count of individual navigation tiles loaded into memory. Used by `ChatHandler.DebugCommands/HandleMmapStatsCommand` for diagnostic reporting.

**`getLoadedMapsCount`**
Returns the size of the `loadedMMaps` unordered map, indicating how many unique map IDs have active navigation data. Used by `ChatHandler.DebugCommands/HandleMmapStatsCommand` for diagnostic reporting.

---

<!-- machine-true, projected from graph.json -->

## Map — MMapManager

*Source:* MoveMap.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| MMapManager | ctor | — | MoveMap/createOrGetMMapManager | — |
| getLoadedTilesCount | method | — | ChatHandler.DebugCommands/HandleMmapStatsCommand | — |
| getLoadedMapsCount | method | — | ChatHandler.DebugCommands/HandleMmapStatsCommand | — |
