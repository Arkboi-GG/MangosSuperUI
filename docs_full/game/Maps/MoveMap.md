# MoveMap

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# MoveMap

**Purpose & Responsibilities**

The `MoveMap` unit implements the singleton-based management of navigation meshes (MMaps) for the server. It acts as the central authority for loading, caching, querying, and unloading Detour (`dtNavMesh`) navigation data derived from the game world's geometry.

Its primary responsibilities are:
1.  **Map Navigation Mesh Management:** Loading map-level navigation data (`*.mmap` headers and `*.mmtile` chunks) into memory, organized by map ID and grid coordinates.
2.  **Thread-Safe Query Access:** Providing thread-local `dtNavMeshQuery` objects. Since Detour queries are not thread-safe, `MoveMap` maintains a pool of query objects keyed by `std::thread::id`, ensuring each thread gets its own isolated query context for a specific map or model.
3.  **Game Object Model Navigation:** Loading standalone navigation meshes for specific Game Object models (`goXXXX.mmtile`), allowing pathfinding around complex static objects that are not part of the main terrain mesh.
4.  **Resource Lifecycle:** Handling the allocation and deallocation of Detour memory structures, including custom allocators (`dtCustomAlloc`/`dtCustomFree`) to integrate Detour's memory management with standard C++ operators.

This unit does not perform pathfinding itself; it provides the underlying mesh data and query handles to other units (like `WorldObject.PathFinder` and `AiBotAI.Movement`) that execute the actual pathfinding algorithms.

## Member-by-Member Behavior

### Singleton Factory & Lifecycle

*   **`createOrGetMMapManager`**: Implements the singleton pattern for `MMapManager`. It checks the global pointer `g_MMapManager`; if null, it instantiates a new `MMapManager`. This ensures a single global instance manages all navigation data. It is called by various subsystems during initialization and runtime to access the manager.
*   **`clear`**: Destroys the global `MMapManager` instance and resets the pointer to null. This is typically called during server shutdown to ensure proper cleanup.
*   **`~MMapManager`**: The destructor iterates through `loadedMMaps` and deletes each `MMapData` object. It relies on the `MMapData` destructor to handle the deletion of associated `dtNavMesh` and `dtNavMeshQuery` objects. Note that the comment in the source warns that if tiles were still loaded in `MMapData->mmapLoadedTiles`, their data might be lost if not properly unloaded beforehand, though the `MMapData` destructor handles the mesh deletion.

### Map Data Loading & Unloading

*   **`loadMapData`**: Loads the base navigation mesh structure for a specific `mapId`.
    1.  Checks if the map is already loaded in `loadedMMaps`.
    2.  Verifies that MMaps are enabled via `sWorld.getConfig(CONFIG_BOOL_MMAP_ENABLED)`.
    3.  Constructs the file path `mmaps/%03i.mmap` using `sWorld.GetDataPath()`.
    4.  Reads the `dtNavMeshParams` from the file.
    5.  Initializes a new `dtNavMesh` using these parameters.
    6.  Wraps the mesh in an `MMapData` object and inserts it into `loadedMMaps` under a write lock.
    7.  If the map was already loaded by another thread between the check and the insert, it deletes the newly created data to avoid duplication.

*   **`loadMap`**: Loads a specific grid tile (`x`, `y`) for a given `mapId`.
    1.  Ensures the base map data is loaded via `loadMapData`.
    2.  Checks if the tile is already loaded in `MMapData::mmapLoadedTiles` using a packed ID from `packTileID`.
    3.  Constructs the tile file path `mmaps/%03i%02i%02i.mmtile`.
    4.  If the file doesn't exist, it logs a debug message if a corresponding VMap exists (indicating potential missing MMap generation).
    5.  Reads the `MmapTileHeader` to verify magic number and version.
    6.  Allocates memory using `dtAlloc` (which uses `dtCustomAlloc`) and reads the tile data.
    7.  Adds the tile to the `dtNavMesh` via `addTile`. The `DT_TILE_FREE_DATA` flag tells Detour to manage the memory lifetime.
    8.  Stores the tile reference in `mmapLoadedTiles` and increments the global `loadedTiles` counter.

*   **`unloadMap` (tile)**: Unloads a specific grid tile.
    1.  Verifies the map and tile are loaded.
    2.  Retrieves the `dtTileRef` and removes the tile from the `dtNavMesh`.
    3.  Removes the tile from `mmapLoadedTiles` and decrements `loadedTiles`.
    4.  If removal fails, it asserts, as this indicates a potential memory leak or state inconsistency.

*   **`unloadMap` (map)**: Unloads an entire map.
    1.  Iterates through all loaded tiles for the map, removing them from the `dtNavMesh`.
    2.  Deletes the `MMapData` object (which frees the `dtNavMesh` and any associated queries).
    3.  Removes the map from `loadedMMaps`.

*   **`unloadMapInstance`**: Unloads a specific `dtNavMeshQuery` instance associated with a thread ID for a given map. This is used to clean up per-thread resources when a thread finishes or changes context. It frees the query object and removes it from `MMapData::navMeshQueries`.

### Game Object Model Management

*   **`loadGameObject`**: Loads a navigation mesh for a specific Game Object display ID.
    1.  Checks if the model is already loaded in `loadedModels`.
    2.  Reads the file `mmaps/go%04i.mmtile`.
    3.  Validates the header.
    4.  Initializes a new `dtNavMesh` directly from the tile data (unlike map loading, which loads a header then tiles).
    5.  Wraps it in `MMapData` and stores it in `loadedModels`.

*   **`loadAllGameObjectModels`**: Iterates through a set of display IDs and calls `loadGameObject` for each. This is typically called during server startup to preload common object models.

### Query Accessors

*   **`GetNavMesh`**: Returns a raw pointer to the `dtNavMesh` for a given map ID. Used for direct mesh inspection or operations that don't require a query object.

*   **`GetGONavMesh`**: Returns a raw pointer to the `dtNavMesh` for a given Game Object display ID.

*   **`GetNavMeshQuery`**: Provides thread-safe access to a `dtNavMeshQuery` for a specific map.
    1.  Checks if the map is loaded.
    2.  Gets the current thread ID.
    3.  Acquires a shared lock on `navMeshQueries_lock`.
    4.  If a query for this thread doesn't exist, it upgrades to a unique lock, creates a new `dtNavMeshQuery`, initializes it with the map's mesh, and stores it.
    5.  Returns the query pointer. This ensures each thread has its own query object, preventing race conditions in Detour.

*   **`GetModelNavMeshQuery`**: Similar to `GetNavMeshQuery`, but for Game Object models. It uses a separate mutex `lockForModels` for synchronization, as model loading/querying is distinct from map navigation.

### Utility Functions

*   **`packTileID`**: Combines grid coordinates `x` and `y` into a single `uint32` key (`x << 16 | y`) for efficient storage in unordered maps.
*   **`dtCustomAlloc` / `dtCustomFree`**: Inline functions that bridge Detour's allocation interface with standard C++ `new[]` and `delete[]`. They are registered with Detour to ensure memory compatibility.

## Cross-Unit Boundaries

*   **`MMapManager/MMapManager`**: Called by `createOrGetMMapManager` to instantiate the singleton.
*   **`Errors/PrintStacktraceAndThrow`**: Called by `loadMapData`, `loadMap`, `unloadMap`, `loadGameObject`, `GetNavMeshQuery`, and `GetModelNavMeshQuery` in case of critical failures (e.g., assertion failures or severe initialization errors).
*   **`Log.Main/Out`**: Extensively used by almost all methods for debugging, error reporting, and status logging (e.g., successful loads, missing files, version mismatches).
*   **`MMapData/MMapData`**: Constructed by `loadMapData` and `loadGameObject` to wrap `dtNavMesh` instances and manage their lifecycle.
*   **`World/getConfig`**: Called by `loadMapData` to check if MMaps are enabled globally.
*   **`World/GetDataPath`**: Called by `loadMapData`, `loadMap`, and `loadGameObject` to construct file paths for MMap assets.
*   **`IVMapManager/existsMap`**: Called by `loadMap` to check if a VMap exists for a tile where an MMap is missing, aiding in debugging missing navigation data.
*   **`VMapFactory/createOrGetVMapManager`**: Called by `loadMap` to access the VMap manager for the existence check.
*   **`MmapTileHeader/MmapTileHeader`**: Used implicitly in `loadMap` and `loadGameObject` to read and validate file headers.

**Called By:**

*   **`AiBotAI.Movement/FindNearestNavmeshPointNear`**: Calls `createOrGetMMapManager` and `GetNavMeshQuery` to find valid movement points for AI bots.
*   **`ChatHandler.DebugCommands/*`**: Various debug commands (`HandleMmapLoad`, `HandleMmapUnload`, etc.) call `createOrGetMMapManager`, `loadMap`, `unloadMap`, `GetNavMesh`, `GetNavMeshQuery`, and `GetModelNavMeshQuery` to allow administrators to inspect and manipulate MMap state.
*   **`GridMap/CleanUpGrids`**: Calls `unloadMap` (tile version) to free memory when grids are cleaned up.
*   **`GridMap/LoadMapAndVMap`**: Calls `loadMap` to load navigation tiles when grids are initialized.
*   **`GridMap/~TerrainInfo`**: Calls `unloadMap` (map version) to clean up all MMap data when terrain info is destroyed.
*   **`Map.Main/GetWalkHitPosition`** and **`Map.Main/GetWalkRandomPosition`**: Call `GetNavMeshQuery` and `GetModelNavMeshQuery` to perform walkable position calculations.
*   **`World/SetInitialWorldSettings`**: Calls `createOrGetMMapManager` and `loadAllGameObjectModels` during server startup.
*   **`WorldObject.PathFinder/calculate`** and **`WorldObject.PathFinder/HasMMapsForCurrentMap`**: Call `GetNavMeshQuery` and `GetModelNavMeshQuery` to execute pathfinding algorithms.

## Data Model

This unit does not interact with any database tables. It operates entirely on file-based assets (`*.mmap`, `*.mmtile`) located in the `mmaps/` directory relative to the data path.

## Notable Implementation Details

1.  **Thread-Local Queries:** Detour's `dtNavMeshQuery` is not thread-safe. `MoveMap` solves this by maintaining a `std::unordered_map<std::thread::id, dtNavMeshQuery*>` within each `MMapData` object. `GetNavMeshQuery` and `GetModelNavMeshQuery` lazily create and cache a query object for each unique thread accessing a specific map or model. This requires careful locking (`shared_timed_mutex` for maps, `mutex` for models) to prevent race conditions during creation.
2.  **Memory Management Integration:** Detour uses its own allocator. `MoveMap` registers `dtCustomAlloc` and `dtCustomFree` to use standard C++ `new[]`/`delete[]`. This ensures that memory allocated by Detour for tile data (when `DT_TILE_FREE_DATA` is used) is correctly freed when the tile is removed or the mesh is destroyed.
3.  **Tile Packing:** Grid coordinates `(x, y)` are packed into a single `uint32` using `packTileID` (`x << 16 | y`). This assumes `x` and `y` fit within 16 bits, which is sufficient for typical grid sizes. This packed ID is used as the key in `MMapTileSet` for O(1) lookup.
4.  **Lazy Loading:** Navigation data is loaded on-demand. `loadMap` is called only when a specific tile is needed, and `loadMapData` is called only when the first tile for a map is requested. This reduces initial memory footprint and startup time.
5.  **Error Handling & Assertions:** The code uses `MANGOS_ASSERT` for critical failures (e.g., failed mesh initialization) and returns `false` for non-critical failures (e.g., missing files). In `unloadMap`, a failed tile removal triggers an assertion, as it implies a state inconsistency that could lead to memory leaks.
6.  **Version Checking:** Both `loadMap` and `loadGameObject` strictly check the `mmapVersion` in the file header against `MMAP_VERSION`. Mismatches result in an error log and failure to load, preventing corruption from incompatible file formats.
7.  **Duplicate Prevention:** `loadMapData` and `loadMap` check for existing entries before loading. `loadMapData` uses a double-check locking pattern (check under shared lock, then acquire unique lock to insert) to handle concurrent requests for the same map.

## Member Reference

*   **`createOrGetMMapManager`**: Static factory method that returns the singleton `MMapManager` instance, creating it if necessary.
*   **`dtCustomAlloc`**: Inline function that allocates memory using `new unsigned char[size]` for Detour integration.
*   **`clear`**: Static method that deletes the global `MMapManager` instance and resets the pointer.
*   **`dtCustomFree`**: Inline function that frees memory using `delete []` for Detour integration.
*   **`~MMapManager`**: Destructor that cleans up all loaded map data by deleting `MMapData` objects.
*   **`loadMapData`**: Loads the base `dtNavMesh` structure for a map ID from `mmaps/%03i.mmap`.
*   **`packTileID`**: Packs grid coordinates `x` and `y` into a single `uint32` key.
*   **`loadMap`**: Loads a specific grid tile (`x`, `y`) for a map ID from `mmaps/%03i%02i%02i.mmtile`.
*   **`unloadMap#2`**: Overload of `unloadMap` that unloads a specific grid tile (`x`, `y`) for a map ID.
*   **`unloadMap`**: Overload of `unloadMap` that unloads all tiles and the base mesh for a map ID.
*   **`unloadMapInstance`**: Unloads a specific `dtNavMeshQuery` instance associated with a thread ID for a map.
*   **`GetNavMesh`**: Returns a raw pointer to the `dtNavMesh` for a given map ID.
*   **`GetGONavMesh`**: Returns a raw pointer to the `dtNavMesh` for a given Game Object display ID.
*   **`GetNavMeshQuery`**: Returns a thread-local `dtNavMeshQuery` for a given map ID, creating it if necessary.
*   **`loadAllGameObjectModels`**: Loads navigation meshes for a set of Game Object display IDs.
*   **`loadGameObject`**: Loads a navigation mesh for a specific Game Object display ID from `mmaps/go%04i.mmtile`.
*   **`GetModelNavMeshQuery`**: Returns a thread-local `dtNavMeshQuery` for a given Game Object display ID, creating it if necessary.

---

<!-- machine-true, projected from graph.json -->

## Map — MoveMap

*Source:* MoveMap.cpp, MoveMap.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| createOrGetMMapManager | method | MMapManager/MMapManager | AiBotAI.Movement/FindNearestNavmeshPointNear, ChatHandler.DebugCommands/HandleMmapLoad, ChatHandler.DebugCommands/HandleMmapLoadedTilesCommand, ChatHandler.DebugCommands/HandleMmapLocCommand, ChatHandler.DebugCommands/HandleMmapPathCommand, ChatHandler.DebugCommands/HandleMmapStatsCommand, ChatHandler.DebugCommands/HandleMmapUnload, GridMap/CleanUpGrids, GridMap/LoadMapAndVMap, GridMap/~TerrainInfo, Map.Main/GetWalkHitPosition, Map.Main/GetWalkRandomPosition, World/SetInitialWorldSettings, WorldObject.PathFinder/calculate, WorldObject.PathFinder/HasMMapsForCurrentMap | — |
| dtCustomAlloc | function | — | — | — |
| clear | method | — | — | — |
| dtCustomFree | function | — | — | — |
| ~MMapManager | dtor | — | — | — |
| loadMapData | method | Errors/PrintStacktraceAndThrow, Log.Main/Out, MMapData/MMapData, World/getConfig, World/GetDataPath | — | — |
| packTileID | method | — | — | — |
| loadMap | method | Errors/PrintStacktraceAndThrow, IVMapManager/existsMap, Log.Main/Out, MmapTileHeader/MmapTileHeader, VMapFactory/createOrGetVMapManager, World/GetDataPath | ChatHandler.DebugCommands/HandleMmapLoad, GridMap/LoadMapAndVMap | — |
| unloadMap#2 | method | Errors/PrintStacktraceAndThrow, Log.Main/Out | GridMap/CleanUpGrids | — |
| unloadMap | method | Log.Main/Out | ChatHandler.DebugCommands/HandleMmapUnload, GridMap/~TerrainInfo | — |
| unloadMapInstance | method | Log.Main/Out | — | — |
| GetNavMesh | method | — | ChatHandler.DebugCommands/HandleMmapLoadedTilesCommand, ChatHandler.DebugCommands/HandleMmapLocCommand, ChatHandler.DebugCommands/HandleMmapPathCommand, ChatHandler.DebugCommands/HandleMmapStatsCommand | — |
| GetGONavMesh | method | — | ChatHandler.DebugCommands/HandleMmapPathCommand | — |
| GetNavMeshQuery | method | Errors/PrintStacktraceAndThrow, Log.Main/Out | AiBotAI.Movement/FindNearestNavmeshPointNear, ChatHandler.DebugCommands/HandleMmapLoadedTilesCommand, ChatHandler.DebugCommands/HandleMmapLocCommand, Map.Main/GetWalkHitPosition, Map.Main/GetWalkRandomPosition, WorldObject.PathFinder/calculate, WorldObject.PathFinder/HasMMapsForCurrentMap | — |
| loadAllGameObjectModels | method | — | World/SetInitialWorldSettings | — |
| loadGameObject | method | Errors/PrintStacktraceAndThrow, Log.Main/Out, MMapData/MMapData, MmapTileHeader/MmapTileHeader, World/GetDataPath | — | — |
| GetModelNavMeshQuery | method | Errors/PrintStacktraceAndThrow, Log.Main/Out | ChatHandler.DebugCommands/HandleMmapLocCommand, ChatHandler.DebugCommands/HandleMmapStatsCommand, Map.Main/GetWalkHitPosition, Map.Main/GetWalkRandomPosition, WorldObject.PathFinder/calculate, WorldObject.PathFinder/HasMMapsForCurrentMap | — |
