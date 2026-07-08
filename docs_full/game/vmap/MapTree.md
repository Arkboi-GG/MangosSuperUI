<!-- provenance: failed-members -->
# MapTree

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# MapTree

`MapTree` implements `StaticMapTree`, the core spatial acceleration structure for static collision geometry in the VMAP (Virtual Map) system. It manages a Binary Interval Hierarchy (`BIH`) tree of `ModelInstance` objects, enabling high-performance queries for line-of-sight checks, height determination, area identification, and collision detection within a specific game map.

The unit handles two distinct loading strategies:
1.  **Non-Tiled Maps:** The entire map's collision data is loaded into memory at once via `InitMap`.
2.  **Tiled Maps:** Collision data is split into grid tiles (`.vmtile` files). `StaticMapTree` dynamically loads and unloads these tiles via `LoadMapTile` and `UnloadMapTile` to manage memory usage, tracking reference counts for individual model spawns to ensure safe cleanup.

It does not interact with any database tables; all data is read from binary `.vmap` and `.vmtile` files on disk.

## Member-by-Member Behavior

### Callback Functors
Several local classes act as functors (callbacks) passed to the `BIH` tree's intersection algorithms. These define how the tree traverses nodes and interacts with `ModelInstance` objects during queries.

*   **`MapRayCallback`**: Used for simple ray-casting queries (e.g., Line of Sight). Its `operator()#4` calls `ModelInstance::intersectRay` to check if a ray hits a model. It tracks whether *any* hit occurred via the `didHit` flag, which is exposed by the `didHit` method.
*   **`MapIntersectionFinderCallback`**: Similar to `MapRayCallback` but designed to find the *closest* or most relevant collision model. Its `operator()#3` calls `ModelInstance::intersectRay` and updates a `result` pointer if a hit is found, prioritizing models that do not break line-of-sight (`MOD_NO_BREAK_LOS`).
*   **`AreaInfoCallback`**: Used for point-in-volume queries to determine area metadata. Its `operator()` calls `ModelInstance::intersectPoint` to populate an `AreaInfo` struct with flags, ADT IDs, and ground height.
*   **`LocationInfoCallback`**: Used to retrieve detailed location context. Its `operator()#2` calls `ModelInstance::GetLocationInfo` to populate a `LocationInfo` struct with the specific `ModelInstance` and `GroupModel` hit, along with ground height and root ID.
*   **`UnderModelCallback`**: Used to determine if a point is enclosed within a model. Its `operator()#5` calls `ModelInstance::isUnderModel` to calculate distances to the nearest interior and exterior surfaces. The `UnderModel` method then evaluates these distances to determine enclosure status.

### Spatial Queries
These methods perform geometric queries against the loaded `BIH` tree.

*   **`getIntersectionTime`**: A private helper that casts a `G3D::Ray` through the tree. It uses `MapRayCallback` to find the first intersection within `pMaxDist`. If a hit occurs, it updates `pMaxDist` to the hit distance and returns `true`.
*   **`isInLineOfSight`**: Determines if two points (`pos1`, `pos2`) have a clear line of sight. It calculates the distance between points, constructs a ray, and calls `getIntersectionTime`. If no intersection is found within the distance, LOS is clear. It guards against NaN values caused by zero-distance inputs, which could cause the `BIH` intersection algorithm to infinite loop. On assertion failure, it may trigger `Errors::PrintStacktraceAndThrow`.
*   **`getObjectHitPos`**: Calculates the exact position where a movement from `pos1` to `pos2` would collide with an object. It uses `getIntersectionTime` to find the hit distance. If a hit occurs, it computes the hit position and applies an optional `pModifyDist` offset (pushing the entity back or forward relative to the hit normal/direction). Like `isInLineOfSight`, it guards against zero-distance inputs and may trigger `Errors::PrintStacktraceAndThrow` on assertion failure.
*   **`FindCollisionModel`**: Finds the specific `ModelInstance` that blocks the path between `pos1` and `pos2`. It uses `MapIntersectionFinderCallback` to traverse the tree and return the pointer to the colliding model.
*   **`getHeight`**: Determines the height of the terrain or object at a given `pPos`. It casts a vertical ray (up or down, determined by the sign of `maxSearchDist`) using `getIntersectionTime`. If a hit is found, it returns the Z-coordinate of the intersection; otherwise, it returns infinity.
*   **`getAreaInfo`**: Retrieves area metadata for a specific point. It uses `AreaInfoCallback` to query the tree. If a model contains the point, it populates output parameters (`flags`, `adtId`, `rootId`, `groupId`) and updates the point's Z to the ground height.
*   **`isUnderModel`**: Checks if a point is inside a model. It uses `UnderModelCallback` to query the tree. It returns `true` if the point is enclosed, optionally outputting the distance to the nearest interior (`inDist`) and exterior (`outDist`) surfaces.
*   **`GetLocationInfo`**: Retrieves detailed location context for a point. It uses `LocationInfoCallback` to query the tree, populating the `LocationInfo` struct with the hit instance, model, ground height, and root ID.

### File I/O and Loading Management
These methods handle the lifecycle of map data, reading from binary files and managing memory.

*   **`getTileFileName`**: Generates the filename for a specific tile based on `mapID`, `tileX`, and `tileY`. The format is `XXX_XX_XX.vmtile` (e.g., `001_05_03.vmtile`).
*   **`CanLoadMap`**: Pre-checks if a map or tile can be loaded. It opens the main `.vmap` file and verifies the magic number. If the map is tiled, it also attempts to open the specific `.vmtile` file to verify its existence and magic number. It calls `BoundsTrait::TileAssembler::readChunk` to parse file headers.
*   **`InitMap`**: Initializes the `StaticMapTree` for a non-tiled map. It opens the `.vmap` file, reads the `BIH` tree structure via `BIH::readFromFile`, and allocates the `iTreeValues` array. It then reads global model spawns, acquiring `WorldModel` pointers from `VMapManager2::acquireModelInstance` and setting their flags via `WorldModel::setModelFlags`. Each spawn is linked to a tree index in `iTreeValues`.
*   **`LoadMapTile`**: Loads a specific tile for a tiled map. If the map is not tiled, it registers a "fake" load to track tile coverage. For tiled maps, it opens the `.vmtile` file, reads the spawns, acquires `WorldModel` instances from `VMapManager2`, and updates `iTreeValues` with the new `ModelInstance`s. It tracks loaded tiles in `iLoadedTiles` and spawn reference counts in `iLoadedSpawns`.
*   **`UnloadMapTile`**: Unloads a specific tile. It re-reads the `.vmtile` file to identify which spawns belong to it. It decrements the reference count for each spawn in `iLoadedSpawns`. If a count reaches zero, it marks the corresponding `ModelInstance` as unloaded via `ModelInstance::setUnloaded` and removes it from the spawn map. Finally, it removes the tile from `iLoadedTiles`.
*   **`UnloadMap`**: Clears all internal tracking structures (`iLoadedSpawns`, `iLoadedTiles`) when the entire map is being unloaded. Note: It does not explicitly delete `ModelInstance` objects or release `WorldModel` references here; that is handled by the reference counting in `UnloadMapTile` or the destructor of `VMapManager2`.
*   **`StaticMapTree` (ctor)**: Initializes the map ID, base path, and ensures the base path ends with a directory separator.
*   **`~StaticMapTree` (dtor)**: Frees the `iTreeValues` array. It assumes that all model references have been properly released via `UnloadMapTile` or `UnloadMap` prior to destruction.

## Cross-Unit Boundaries

*   **`VMapManager2`**:
    *   **Called By**: `InitMap`, `LoadMapTile` call `VMapManager2::acquireModelInstance` to get shared pointers to `WorldModel` objects. `VMapManager2` calls `StaticMapTree::getAreaInfo`, `isUnderModel`, `GetLocationInfo`, `isInLineOfSight`, `getObjectHitPos`, `FindCollisionModel`, `getHeight`, `CanLoadMap`, `InitMap`, `UnloadMap`, `LoadMapTile`, and `UnloadMapTile`.
    *   **Collaboration**: `VMapManager2` acts as the central manager, delegating spatial queries to `StaticMapTree`. `StaticMapTree` relies on `VMapManager2` to provide and manage the underlying `WorldModel` geometry data.
*   **`ModelInstance`**:
    *   **Calls Out**: All callback functors (`MapRayCallback`, etc.) call various methods on `ModelInstance` (`intersectRay`, `intersectPoint`, `GetLocationInfo`, `isUnderModel`, `setUnloaded`). `InitMap` and `LoadMapTile` construct `ModelInstance` objects.
    *   **Collaboration**: `ModelInstance` represents a single placed object in the world. `StaticMapTree` organizes these instances into a spatial hierarchy and queries them for geometric properties.
*   **`WorldModel`**:
    *   **Calls Out**: `InitMap` and `LoadMapTile` call `WorldModel::setModelFlags` after acquiring a model instance.
    *   **Collaboration**: `WorldModel` holds the raw mesh data. `StaticMapTree` sets flags on these models to indicate their collision properties.
*   **`BIH`**:
    *   **Calls Out**: `InitMap` calls `BIH::readFromFile` and `BIH::primCount`. The query methods (`getIntersectionTime`, etc.) implicitly use the `BIH` tree structure (`iTree`) for traversal.
    *   **Collaboration**: `BIH` provides the spatial indexing algorithm. `StaticMapTree` wraps this tree with higher-level semantic queries.
*   **`BoundsTrait::TileAssembler`**:
    *   **Calls Out**: `CanLoadMap`, `InitMap`, `LoadMapTile`, and `UnloadMapTile` call `readChunk` to parse binary file headers.
    *   **Collaboration**: Provides low-level file parsing utilities for the VMAP file format.
*   **`Errors`**:
    *   **Calls Out**: `isInLineOfSight` and `getObjectHitPos` call `Errors::PrintStacktraceAndThrow` (indirectly via assertions or error paths if invalid coordinates are detected, though the primary guard is the `1e-10f` check).
    *   **Collaboration**: Handles critical error reporting for invalid geometric states.
*   **`Log.Main`**:
    *   **Calls Out**: `InitMap`, `LoadMapTile`, `UnloadMapTile` call `sLog.Out` for debug and error logging.
    *   **Collaboration**: Provides logging infrastructure for development and runtime diagnostics.

## Data Model

This unit does not interact with any database tables. All data is sourced from binary files (`.vmap`, `.vmtile`) on the filesystem.

## Notable Implementation Details

*   **Reference Counting for Spawns**: In tiled maps, a single `ModelInstance` (identified by its tree index) might be referenced by multiple tiles or multiple times within the same tile. `StaticMapTree` uses `iLoadedSpawns` (an `unordered_map<uint32, uint32>`) to track reference counts. A `ModelInstance` is only marked as unloaded (`setUnloaded`) when its reference count drops to zero. This prevents premature deletion of models shared across tile boundaries.
*   **Fake Tile Loads**: For non-tiled maps, `LoadMapTile` still registers the tile in `iLoadedTiles` with a `false` value (indicating no file was loaded). This allows the system to track which parts of the map are "active" for unloading purposes, ensuring the map isn't unloaded while any tile is considered loaded.
*   **NaN Protection**: Geometric queries like `isInLineOfSight` and `getObjectHitPos` explicitly check if the distance between points is less than `1e-10f`. If so, they return early to avoid creating rays with zero length, which would result in NaN directions and potentially cause the `BIH` intersection algorithm to enter an infinite loop.
*   **File Re-reading on Unload**: `UnloadMapTile` re-opens and re-reads the `.vmtile` file to determine which spawns to remove. This is inefficient but ensures consistency with the file state. It does not store the spawn list in memory for quick lookup during unload.
*   **Magic Number Verification**: `CanLoadMap` and `InitMap` verify the `VMAP_MAGIC` header in files. However, `CanLoadMap` has a `TODO` comment indicating that full magic number checking might not be fully implemented or robust in all paths.
*   **Thread Safety**: The code does not appear to use mutexes or atomic operations. `StaticMapTree` is likely accessed from a single thread (e.g., the map update thread) or requires external synchronization by `VMapManager2`. Concurrent access to `iTreeValues` or `iLoadedSpawns` from multiple threads would be unsafe.
*   **Memory Leak Risk**: The destructor `~StaticMapTree` deletes `iTreeValues` but does not explicitly release `WorldModel` references held by the `ModelInstance`s. It relies on `UnloadMap` or `UnloadMapTile` having been called to decrement reference counts and allow `VMapManager2` to clean up. If `StaticMapTree` is destroyed without proper unloading, `WorldModel` objects may leak.

## Member Reference

**MapRayCallback** (ctor): Constructs the functor, storing a pointer to the `ModelInstance` array and initializing the `hit` flag to false.

**operator()#4** (method): Invoked by the BIH tree traversal for ray casting. Calls `ModelInstance::intersectRay` on the specified entry. If a hit occurs, sets the internal `hit` flag to true and returns the result.

**didHit** (method): Returns the internal `hit` boolean flag, indicating whether any intersection occurred during the previous traversal.

**MapIntersectionFinderCallback** (ctor): Constructs the functor, storing a pointer to the `ModelInstance` array and initializing the `result` pointer to nullptr.

**operator()#3** (method): Invoked by the BIH tree traversal for finding collision models. Calls `ModelInstance::intersectRay`. If a hit occurs and the current result is null or the new model has the `MOD_NO_BREAK_LOS` flag, updates the `result` pointer to the current model.

**AreaInfoCallback** (ctor): Constructs the functor, storing a pointer to the `ModelInstance` array.

**operator()** (method): Invoked by the BIH tree traversal for area info queries. Calls `ModelInstance::intersectPoint` on the specified entry to populate the `aInfo` struct. Logs debug information if `VMAP_DEBUG` is enabled.

**LocationInfoCallback** (ctor): Constructs the functor, storing a pointer to the `ModelInstance` array and a reference to the `LocationInfo` struct to be populated.

**operator()#2** (method): Invoked by the BIH tree traversal for location info queries. Calls `ModelInstance::GetLocationInfo` on the specified entry. If successful, sets the internal `result` flag to true. Logs debug information if `VMAP_DEBUG` is enabled.

**getTileFileName** (method): Static utility that generates the string filename for a VMAP tile based on map ID and tile coordinates, formatting them as `XXX_XX_XX.vmtile`.

**getAreaInfo** (method): Queries the BIH tree using `AreaInfoCallback` to determine area metadata (flags, ADT ID, root ID, group ID) and ground height for a given position. Updates the input position's Z coordinate if a hit is found.

**UnderModelCallback** (ctor): Constructs the functor, storing a pointer to the `ModelInstance` array and initializing `outDist` and `inDist` to -1.

**operator()#5** (method): Invoked by the BIH tree traversal for "under model" checks. Calls `ModelInstance::isUnderModel` to get interior and exterior distances. Updates the functor's `outDist` and `inDist` if the new distances are closer than previously recorded ones.

**UnderModel** (method): Evaluates the stored `outDist` and `inDist` to determine if the point is strictly inside a model (interior distance is valid and less than exterior distance, or exterior is invalid while interior is valid).

**isUnderModel** (method): Queries the BIH tree using `UnderModelCallback` to determine if a position is inside a model. Optionally outputs the distances to the nearest interior and exterior surfaces.

**GetLocationInfo** (method): Queries the BIH tree using `LocationInfoCallback` to populate a `LocationInfo` struct with details about the model and ground at a specific position.

**StaticMapTree** (ctor): Initializes the map ID, base path (ensuring it ends with a separator), and resets internal state variables.

**~StaticMapTree** (dtor): Deletes the `iTreeValues` array. Relies on prior calls to `UnloadMap` or `UnloadMapTile` to release `WorldModel` references.

**getIntersectionTime** (method): Private helper that casts a ray through the BIH tree using `MapRayCallback`. Updates the max distance parameter if an intersection is found and returns true.

**isInLineOfSight** (method): Checks if a direct line exists between two points by casting a ray and verifying no intersections occur within the distance. Guards against zero-length rays. May trigger `Errors::PrintStacktraceAndThrow` on assertion failure.

**getObjectHitPos** (method): Calculates the collision point between two positions. If a hit occurs, adjusts the result position by an optional modification distance. Guards against zero-length rays. May trigger `Errors::PrintStacktraceAndThrow` on assertion failure.

**FindCollisionModel** (method): Traverses the BIH tree using `MapIntersectionFinderCallback` to return the `ModelInstance` that intersects the ray between two points.

**getHeight** (method): Casts a vertical ray (up or down) from a position to find the nearest surface height within a search distance.

**CanLoadMap** (method): Verifies the existence and validity of a VMAP file and its associated tile file (if tiled) by checking magic numbers and file accessibility. Uses `BoundsTrait::TileAssembler::readChunk` and `VMapManager2::getMapFileName`.

**InitMap** (method): Loads the main VMAP file, initializes the BIH tree, allocates the model instance array, and loads global model spawns by acquiring `WorldModel` instances from `VMapManager2`. Uses `BIH::readFromFile`, `BIH::primCount`, `BoundsTrait::TileAssembler::readChunk`, `ModelInstance` constructors, `ModelInstance::readFromFile`, `VMapManager2::acquireModelInstance`, and `WorldModel::setModelFlags`. Logs errors via `Log.Main::Out`.

**UnloadMap** (method): Clears the internal maps tracking loaded tiles and spawn reference counts.

**LoadMapTile** (method): Loads a specific tile file for a tiled map. Reads spawns, acquires `WorldModel` instances from `VMapManager2`, and updates the model instance array. Tracks loaded tiles and spawn references. Uses `BoundsTrait::TileAssembler::readChunk`, `ModelInstance` constructors, `ModelInstance::readFromFile`, `StaticMapTree::packTileID`, `VMapManager2::acquireModelInstance`, and `WorldModel::setModelFlags`. Logs errors via `Log.Main::Out`.

**UnloadMapTile** (method): Unloads a specific tile by re-reading its file to identify spawns, decrementing reference counts, and marking unused `ModelInstance`s as unloaded via `ModelInstance::setUnloaded`. Uses `BoundsTrait::TileAssembler::readChunk`, `ModelInstance::readFromFile`, `ModelInstance::setUnloaded`, and `StaticMapTree::packTileID`. Logs errors via `Log.Main::Out`.

---

<!-- machine-true, projected from graph.json -->

## Map — MapTree

*Source:* MapTree.cpp, MapTree.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| MapRayCallback | ctor | — | — | — |
| operator()#4 | method | ModelInstance/intersectRay | — | — |
| didHit | method | — | — | — |
| MapIntersectionFinderCallback | ctor | — | — | — |
| operator()#3 | method | ModelInstance/intersectRay | — | — |
| AreaInfoCallback | ctor | — | — | — |
| operator() | method | ModelInstance/intersectPoint | — | — |
| LocationInfoCallback | ctor | — | — | — |
| operator()#2 | method | ModelInstance/GetLocationInfo | — | — |
| getTileFileName | method | — | — | — |
| getAreaInfo | method | — | VMapManager2/getAreaInfo | — |
| UnderModelCallback | ctor | — | — | — |
| operator()#5 | method | ModelInstance/isUnderModel | — | — |
| UnderModel | method | — | — | — |
| isUnderModel | method | — | VMapManager2/isUnderModel | — |
| GetLocationInfo | method | — | VMapManager2/GetLiquidLevel | — |
| StaticMapTree | ctor | — | VMapManager2/_loadMap | — |
| ~StaticMapTree | dtor | — | — | — |
| getIntersectionTime | method | — | — | — |
| isInLineOfSight | method | Errors/PrintStacktraceAndThrow | VMapManager2/isInLineOfSight | — |
| getObjectHitPos | method | Errors/PrintStacktraceAndThrow | VMapManager2/getObjectHitPos | — |
| FindCollisionModel | method | — | VMapManager2/FindCollisionModel | — |
| getHeight | method | — | VMapManager2/getHeight | — |
| CanLoadMap | method | BoundsTrait.TileAssembler/readChunk, VMapManager2/getMapFileName | VMapManager2/existsMap | — |
| InitMap | method | BIH/primCount, BIH/readFromFile, BoundsTrait.TileAssembler/readChunk, Log.Main/Out, ModelInstance/ModelInstance, ModelInstance/ModelInstance#2, ModelInstance/readFromFile, VMapManager2/acquireModelInstance, WorldModel/setModelFlags | VMapManager2/_loadMap | — |
| UnloadMap | method | — | VMapManager2/unloadMap | — |
| LoadMapTile | method | BoundsTrait.TileAssembler/readChunk, Log.Main/Out, ModelInstance/ModelInstance#2, ModelInstance/readFromFile, StaticMapTree/packTileID, VMapManager2/acquireModelInstance, WorldModel/setModelFlags | VMapManager2/_loadMap | — |
| UnloadMapTile | method | BoundsTrait.TileAssembler/readChunk, Log.Main/Out, ModelInstance/readFromFile, ModelInstance/setUnloaded, StaticMapTree/packTileID | VMapManager2/unloadMap#2 | — |

---

<!-- verify: failed-members | invented: operator -->
