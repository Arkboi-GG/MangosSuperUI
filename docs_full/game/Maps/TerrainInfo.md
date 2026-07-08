# TerrainInfo

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# TerrainInfo — Grid-Level Terrain Data Management

**Purpose & Responsibilities**
`TerrainInfo` is the central class responsible for managing terrain data for a specific game map within the CMaNGOS server engine. It acts as a cache and coordinator for `GridMap` objects, which contain the raw height, area, liquid, and hole data for individual grid cells of the map.

Its primary responsibilities are:
1.  **Grid Lifecycle Management:** Loading `GridMap` instances on-demand when a coordinate falls within an unloaded grid, and unloading them when they are no longer referenced, to manage memory usage efficiently.
2.  **Reference Counting:** Using a reference-counting mechanism (`Referencable<AtomicLong>`) to track how many entities (likely `Map` instances or similar high-level objects) are currently using this terrain data, ensuring it isn't destroyed while in use.
3.  **Query Interface:** Providing methods to query terrain properties (height, water level, area flags, zone IDs) at specific coordinates `(x, y, z)`. These methods delegate to the appropriate `GridMap` instance after resolving the grid coordinates.
4.  **Garbage Collection:** Periodically cleaning up unreferenced grids via `CleanUpGrids`, triggered by the `TerrainManager`.

It is designed to be thread-safe for concurrent access to different grids, utilizing mutexes (`m_mutex` and `m_refMutex`) to protect internal state during load/unload operations and reference counting.

## Member-by-Member Behavior

### Construction and Destruction
*   **`TerrainInfo(uint32 mapid)`**: Initializes the terrain info for a specific map ID. It sets `m_mapId` and initializes the grid maps and reference arrays.
*   **`~TerrainInfo()`**: Destructor. It iterates through all loaded grids and deletes them. It relies on the `Referencable` base class for final cleanup.

### Grid Loading and Unloading
*   **`Load(uint32 const x, uint32 const y)`**: Loads a `GridMap` for the specified grid coordinates `(x, y)`. If the grid is already loaded, it returns the existing pointer. Otherwise, it creates a new `GridMap`, loads its data from disk (via `GridMap::loadData`), and stores it in `m_GridMaps`. It also increments the reference count for this grid. This method is protected and intended for internal use or by the `Map` class.
*   **`Unload(uint32 const x, uint32 const y)`**: Unloads a `GridMap` for the specified grid coordinates. It decrements the reference count. If the count reaches zero, it deletes the `GridMap` object and sets the slot in `m_GridMaps` to `nullptr`. This method is protected and intended for internal use or by the `Map` class.
*   **`RefGrid(uint32 const& x, uint32 const& y)`**: Increments the reference count for a specific grid. Returns the new reference count. Used internally to track usage.
*   **`UnrefGrid(uint32 const& x, uint32 const& y)`**: Decrements the reference count for a specific grid. Returns the new reference count. Used internally to track usage. If the count drops to zero, it signals that the grid can be unloaded (though `Unload` handles the actual deletion).

### Terrain Queries
These methods query terrain properties at specific coordinates. They typically involve:
1.  Converting world coordinates `(x, y)` to grid coordinates `(gx, gy)`.
2.  Ensuring the corresponding `GridMap` is loaded (calling `Load` if necessary).
3.  Delegating the query to the `GridMap` instance.

*   **`GetHeightStatic(float x, float y, float z, bool checkVMap, float maxSearchDist)`**: Retrieves the static ground height at `(x, y)`. It checks if Vertical Maps (VMaps) are available and should be checked. If so, it might perform a more complex search. Otherwise, it delegates to `GridMap::getHeight`.
*   **`GetWaterLevel(float x, float y, float z, float* pGround)`**: Retrieves the water level at `(x, y)`. Optionally fills `pGround` with the ground height. Delegates to `GridMap::getLiquidLevel`.
*   **`GetWaterOrGroundLevel(Position const& position, float* pGround, bool swim)` / `GetWaterOrGroundLevel(float x, float y, float z, float* pGround, bool swim)`**: Determines whether the entity is in water or on ground. If `swim` is true, it prioritizes water level; otherwise, it prioritizes ground height. Returns the relevant level and optionally fills `pGround`.
*   **`IsSwimmable(float x, float y, float z, float radius, GridMapLiquidData* data)`**: Checks if the location is swimmable, considering a radius around the point. Fills `data` if provided. Delegates to `GridMap::getLiquidStatus`.
*   **`IsInWater(float x, float y, float z, GridMapLiquidData* data)`**: Checks if the location is in water. Fills `data` if provided. Delegates to `GridMap::getLiquidStatus`.
*   **`IsUnderWater(float x, float y, float z)`**: Checks if the location is underwater. Delegates to `GridMap::getLiquidStatus`.
*   **`getLiquidStatus(float x, float y, float z, uint8 ReqLiquidType, GridMapLiquidData* data)`**: Retrieves detailed liquid status at the location. Delegates to `GridMap::getLiquidStatus`.
*   **`GetAreaFlag(float x, float y, float z, bool* isOutdoors)`**: Retrieves the area flag at the location. Optionally fills `isOutdoors`. Delegates to `GridMap::getArea`.
*   **`GetTerrainType(float x, float y)`**: Retrieves the terrain type at the location. Delegates to `GridMap::getTerrainType`.
*   **`GetAreaId(float x, float y, float z)`**: Retrieves the area ID at the location. Uses `GetAreaFlag` and then converts the flag to an ID.
*   **`GetZoneId(float x, float y, float z)`**: Retrieves the zone ID at the location. Uses `GetAreaFlag` and then converts the flag to an ID.
*   **`GetZoneAndAreaId(uint32& zoneid, uint32& areaid, float x, float y, float z)`**: Retrieves both zone and area IDs at the location. Uses `GetAreaFlag` and then converts the flag to IDs.
*   **`GetAreaInfo(float x, float y, float z, uint32& mogpflags, int32& adtId, int32& rootId, int32& groupId)`**: Retrieves detailed area information, including MOGP flags, ADT ID, root ID, and group ID. Delegates to `GridMap::getArea` and potentially other internal structures.
*   **`IsOutdoors(float x, float y, float z)`**: Checks if the location is outdoors. Uses `GetAreaFlag` and checks the result.

### Garbage Collection
*   **`LoadAll()`**: Loads all grids for the map. This is likely used during initial map loading or testing.
*   **`CleanUpGrids(uint32 const diff)`**: Iterates through all grids and unloads those with a reference count of zero. This method is explicitly marked as **NOT THREAD-SAFE** and should only be called by `TerrainManager` during its update cycle, presumably when no other threads are accessing terrain data for this map.

## Cross-Unit Boundaries

*   **Calls Out:**
    *   `GridMap`: `TerrainInfo` extensively uses `GridMap` for all terrain queries. It calls `GridMap::getHeight`, `GridMap::getLiquidLevel`, `GridMap::getLiquidStatus`, `GridMap::getArea`, `GridMap::getTerrainType`, and `GridMap::loadData`. It also manages the lifecycle of `GridMap` objects by creating and deleting them.
    *   `TerrainManager`: `TerrainInfo` is managed by `TerrainManager`. `TerrainManager::Update` calls `TerrainInfo::CleanUpGrids`. `TerrainManager::LoadTerrain` and `TerrainManager::UnloadTerrain` manage the creation and destruction of `TerrainInfo` objects.
    *   `Map`: The `Map` class (not shown in detail here, but referenced in comments and likely in `Map.Main`) calls `TerrainInfo::Load` and `TerrainInfo::Unload` to manage grid references. It also calls `TerrainInfo::GetMapId`.

*   **Called By:**
    *   `GridMap`: `GridMap::GetMapId` is called by `GridMap` methods like `GetAreaFlag`, `GetAreaInfo`, `GetHeightStatic`, `getLiquidStatus`, and by `Map.Main::CrashUnload` and `Map.Main::~Map`. This suggests `GridMap` needs to know its associated map ID, possibly for logging or validation.
    *   `Map.Main`: As mentioned, `Map.Main` calls `TerrainInfo::Load`, `TerrainInfo::Unload`, and `TerrainInfo::GetMapId`. It also calls `TerrainInfo::CleanUpGrids` indirectly via `TerrainManager`.

## Data Model

This unit does not directly interact with database tables. It loads terrain data from binary files on disk (handled by `GridMap::loadData`). The `TerrainManager` might load liquid type data from DBC files (as suggested by `mLiquidTypes` and `GetLiquidType`), but this is not detailed in the provided source.

## Notable Implementation Details

1.  **Reference Counting:** `TerrainInfo` inherits from `Referencable<AtomicLong>`, providing atomic reference counting. This allows multiple `Map` instances (or other entities) to share the same `TerrainInfo` object safely. The `AddRef` and `Release` methods manage the count.
2.  **Grid Reference Counting:** In addition to the overall `TerrainInfo` reference count, each `GridMap` within it has its own reference count (`m_GridRef`). This allows individual grids to be unloaded independently when no longer needed, optimizing memory usage.
3.  **Thread Safety:** `TerrainInfo` uses two mutexes: `m_mutex` for general access and `m_refMutex` for reference counting operations. However, `CleanUpGrids` is explicitly marked as **NOT THREAD-SAFE**. This implies that garbage collection must happen in a controlled manner, likely during a pause in gameplay or when no other threads are accessing terrain data.
4.  **Delegation:** Most terrain queries are delegated to `GridMap` instances. `TerrainInfo` acts primarily as a manager and dispatcher, handling grid loading/unloading and routing queries to the correct grid.
5.  **Memory Management:** `TerrainInfo` takes ownership of `GridMap` objects, creating and deleting them as needed. It also manages the `m_GridMaps` and `m_GridRef` arrays.
6.  **Constants:** Several constants are defined for height and water searches, such as `MAX_HEIGHT`, `INVALID_HEIGHT`, `DEFAULT_HEIGHT_SEARCH`, and `DEFAULT_WATER_SEARCH`. These influence the behavior of height and water level queries.

## Member Reference

*   **`GetMapId`**: Returns the map ID associated with this `TerrainInfo` instance. Called by `GridMap` methods and `Map.Main`.
*   **`TerrainInfo`**: Constructor. Initializes the terrain info for a specific map ID.
*   **`operator=`**: Deleted assignment operator. Prevents copying of `TerrainInfo` objects.

---

<!-- machine-true, projected from graph.json -->

## Map — TerrainInfo

*Source:* GridMap.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GetMapId | method | — | GridMap/GetAreaFlag, GridMap/GetAreaInfo, GridMap/GetHeightStatic, GridMap/getLiquidStatus#2, Map.Main/CrashUnload, Map.Main/~Map | — |
| TerrainInfo | decl | — | — | — |
| operator= | decl | — | — | — |
