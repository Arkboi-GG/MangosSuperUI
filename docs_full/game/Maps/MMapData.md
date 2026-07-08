# MMapData

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# MMapData

**Purpose & Responsibilities**

`MMapData` is a lightweight aggregate structure within the `MMAP` namespace that encapsulates the runtime state required for navigation mesh (NavMesh) operations on a specific game map or object model. It serves as the container for a single `dtNavMesh` instance (provided by the Detour library) and manages the associated thread-local query objects (`dtNavMeshQuery`) necessary to perform pathfinding and collision checks against that mesh.

Because the underlying Detour library’s `dtNavMeshQuery` objects are not thread-safe, `MMapData` implements a per-thread caching mechanism. It stores a mapping from `std::thread::id` to `dtNavMeshQuery*`, protected by a `std::shared_timed_mutex`. This allows multiple threads to concurrently read from their respective cached queries while ensuring exclusive access when creating or destroying these query objects. Additionally, it tracks which map tiles have been loaded into the mesh via `mmapLoadedTiles`, protected by a separate mutex to allow concurrent tile loading operations.

The structure is designed to be owned and managed by the `MMapManager` singleton. Its lifetime is tied to the existence of the `dtNavMesh` pointer passed to its constructor; upon destruction, it automatically frees all associated Detour resources (both the mesh itself and any cached query objects) to prevent memory leaks.

## Member-by-Member Behavior

### Construction and Destruction

*   **`MMapData` (Constructor)**: Initializes the `MMapData` instance with a pre-existing `dtNavMesh*` pointer. This pointer is stored in the `navMesh` member. The constructor does not allocate the mesh itself; it assumes ownership of the resource passed to it. All other members (`navMeshQueries`, `mmapLoadedTiles`, and locks) are default-initialized by the compiler.
*   **`~MMapData` (Destructor)**: Performs critical cleanup of Detour resources. It iterates through the `navMeshQueries` map and calls `dtFreeNavMeshQuery` on each cached query object. Finally, it checks if `navMesh` is non-null and calls `dtFreeNavMesh` to release the navigation mesh memory. This ensures that all Detour-managed memory is properly freed when the `MMapData` object goes out of scope or is deleted by `MMapManager`.

### State Management

Although `MMapData` does not expose public methods for modifying its state, its members are accessed directly by `MMapManager` (specifically in `MoveMap.cpp`, though the implementation details of `MMapManager` are outside this unit's scope, the MAP indicates `MMapManager` interacts with `MMapData`).

*   **`navMesh`**: Holds the primary navigation mesh object. This is the core data structure used for spatial queries.
*   **`navMeshQueries`**: An unordered map linking thread IDs to `dtNavMeshQuery` pointers. This cache avoids the overhead of creating a new query object for every pathfinding request on a given thread.
*   **`navMeshQueries_lock`**: A `std::shared_timed_mutex` protecting access to `navMeshQueries`. It allows multiple threads to read their cached queries simultaneously (shared lock) but requires exclusive access (unique lock) when inserting or removing entries.
*   **`mmapLoadedTiles`**: An unordered map tracking which grid coordinates (keys) correspond to which Detour tile references (values) currently loaded in the `navMesh`. This is used to avoid reloading tiles that are already in memory.
*   **`tilesLoading_lock`**: A `std::mutex` protecting `mmapLoadedTiles` during modification, ensuring thread-safe updates to the tile loading state.

## Cross-Unit Boundaries

*   **Called by `MMapManager::loadMapData`**: The `MMapManager` creates instances of `MMapData` when loading map data. It passes a newly allocated `dtNavMesh*` to the `MMapData` constructor. The `MMapManager` then populates the `navMesh` with tiles and updates `mmapLoadedTiles`.
*   **Called by `MMapManager::loadGameObject`**: Similarly, when loading a game object's model for navigation purposes, `MMapManager` instantiates `MMapData` to hold the object-specific NavMesh.
*   **Resource Ownership**: `MMapData` takes ownership of the `dtNavMesh*` and the `dtNavMeshQuery*` objects. It is responsible for freeing them via Detour API calls (`dtFreeNavMesh`, `dtFreeNavMeshQuery`). `MMapManager` relies on `MMapData`'s destructor to clean up these resources when a map or model is unloaded.

## Data Model

This unit does not interact directly with any database tables. It operates entirely on in-memory data structures provided by the Detour navigation library and standard C++ containers.

## Notable Implementation Details

*   **Thread-Safety Strategy**: The use of `std::shared_timed_mutex` for `navMeshQueries` is a deliberate optimization. Since pathfinding queries are frequent and typically read-only once the query object is created, allowing concurrent reads reduces contention compared to a simple `std::mutex`. However, note that the `dtNavMeshQuery` objects themselves are *not* thread-safe; the cache ensures that each thread uses its *own* dedicated query object, preventing race conditions within the Detour library.
*   **Memory Management**: The custom allocators `dtCustomAlloc` and `dtCustomFree` are defined in the header but are not members of `MMapData`. They are likely registered globally with the Detour library to ensure consistent memory allocation strategies across the engine. `MMapData` relies on the standard Detour free functions (`dtFreeNavMesh`, `dtFreeNavMeshQuery`) which internally use these custom allocators if configured.
*   **No Copy/Move Semantics**: `MMapData` is a struct with raw pointers and mutexes. It implicitly deletes copy and move constructors/assignment operators due to the presence of `std::mutex` and `std::shared_timed_mutex` (which are non-copyable and non-movable). This enforces unique ownership, which is correct for this resource-management pattern.
*   **Direct Member Access**: The members of `MMapData` are public. This design choice simplifies access for `MMapManager` but exposes the internal structure. Maintainers must ensure that only `MMapManager` (and potentially other tightly coupled parts of the MMAP system) modifies these members, respecting the locking protocols.

## Member Reference

**MMapData**  
Constructor. Initializes the `navMesh` member with the provided `dtNavMesh*` pointer. Other members are default-initialized. Takes ownership of the mesh resource.

**~MMapData**  
Destructor. Iterates over `navMeshQueries` and frees each `dtNavMeshQuery` using `dtFreeNavMeshQuery`. Then, if `navMesh` is not null, frees the navigation mesh using `dtFreeNavMesh`. Ensures all Detour resources are released.

---

<!-- machine-true, projected from graph.json -->

## Map — MMapData

*Source:* MoveMap.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| MMapData | ctor | — | MoveMap/loadGameObject, MoveMap/loadMapData | — |
| ~MMapData | dtor | — | — | — |
