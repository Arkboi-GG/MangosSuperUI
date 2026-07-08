<!-- provenance: failed-members, boundary-bleed -->
# BoundsTrait.WorldModel

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# BoundsTrait.WorldModel

**Purpose & Responsibilities**

This unit implements the core geometric representation and collision detection logic for static world models in the VMAP (Virtual Map) system. It defines three primary classes—`WmoLiquid`, `GroupModel`, and `WorldModel`—that collectively represent a hierarchical structure of 3D geometry used for line-of-sight checks, position validation, and liquid height queries.

The hierarchy mirrors the structure of World of Warcraft model files:
1.  **`WorldModel`**: Represents a complete static model instance (e.g., a building or terrain chunk). It contains multiple `GroupModel`s and manages a Bounding Interval Hierarchy (BIH) for efficient spatial queries across groups.
2.  **`GroupModel`**: Represents a distinct group of triangles within a model (often corresponding to a specific material or logical section). It holds the vertex/triangle mesh, its own BIH for fast ray-triangle intersection, and optional liquid data.
3.  **`WmoLiquid`**: Represents a grid-based liquid surface associated with a `GroupModel`. It stores height values and flags for a tiled area, allowing interpolation of liquid levels at arbitrary points.

Additionally, the unit provides low-level geometric primitives (`IntersectTriangle`, `IsGoingOut`) and callback structures (`GModelRayCallback`, `WModelRayCallBack`, etc.) required by the BIH traversal algorithms. These callbacks bridge the gap between the generic BIH intersection routines and the specific triangle-mesh data stored in these classes.

**Cross-Unit Boundaries**

*   **`BoundsTrait.TileAssembler`**: Calls `WmoLiquid` constructors and `GroupModel::setMeshData`/`setGroupModels` during the conversion of raw model files into VMAP format. It also calls `GroupModel::readFromFile` and `WorldModel::readFile` to deserialize pre-compiled maps.
*   **`BIH`**: Called by `GroupModel::writeToFile`/`readFromFile` and `WorldModel::writeFile`/`readFile` to serialize/deserialize the bounding interval hierarchies. Also called by `GroupModel::setMeshData` and `WorldModel::setGroupModels` to build the trees.
*   **`ModelInstance`**: Calls `WorldModel::IntersectRay`, `WorldModel::IntersectPoint`, `WorldModel::GetLocationInfo`, and `WorldModel::IsUnderObject` to perform high-level collision and containment checks. It also calls `GroupModel::GetLiquidLevel` and `GroupModel::GetLiquidType` for liquid queries.
*   **`GameObjectModel`**: Calls `WorldModel::IntersectRay` for raycasting against game objects represented as models.
*   **`VMapManager2`**: Calls `WorldModel::GetLiquidType` and `WorldModel::readFile` to manage model instances and liquid data globally.

**Data Model**

This unit does not interact with any database tables. All data is loaded from binary model files (`.vmap`) or generated in-memory during map compilation.

**Notable Implementation Details**

*   **Manual Memory Management**: `WmoLiquid` and `GroupModel` manage raw pointers (`float*`, `uint8*`, `WmoLiquid*`) manually. They implement custom copy constructors, assignment operators, and destructors to ensure deep copies and proper cleanup. This is critical because standard STL containers would not correctly handle the raw arrays.
*   **BIH Integration**: Both `GroupModel` and `WorldModel` use the `BIH` class (from `BIH.h`) to accelerate spatial queries. `GroupModel` builds a BIH over its triangles, while `WorldModel` builds a BIH over its child `GroupModel`s. The `BoundsTrait` template specialization allows the BIH to query bounding boxes from `GroupModel` objects.
*   **Liquid Interpolation**: `WmoLiquid::GetLiquidHeight` uses bilinear interpolation over a grid of height values. It splits each square tile into two triangles to determine which interpolation coefficients to apply, ensuring smooth transitions across the liquid surface.
*   **M2 vs. WMO Orientation**: The code explicitly handles differences between M2 (dynamic/object) and WMO (static/world) models regarding triangle winding order. In `GModelRayOrientedCallback`, the vertex order passed to `IsGoingOut` is reversed for M2 models (`idx2, idx1, idx0`) compared to WMO models (`idx0, idx1, idx2`). This ensures correct determination of whether a ray is entering or exiting a volume.
*   **Single-Group Optimization**: `WorldModel::IntersectRay` checks if there is only one `GroupModel`. If so, it bypasses the `WorldModel`'s BIH and queries the single group directly, avoiding unnecessary overhead.

## Member Reference

**getBounds**
A static method specializing the `BoundsTrait` template for `GroupModel`. It retrieves the axis-aligned bounding box (`G3D::AABox`) from a `GroupModel` by calling `GroupModel::GetBound` (defined in `WorldModel.h`). This enables the BIH system to access bounding volumes for spatial partitioning.

**IntersectTriangle**
A standalone function implementing the Möller–Trumbore algorithm for ray-triangle intersection. It takes a triangle definition, a vertex buffer, a ray, and a reference to the current closest distance. It returns `true` if the ray intersects the triangle closer than the current best distance, updating the distance accordingly. It includes an epsilon check for degenerate cases.

**TriBoundFunc**
A functor class used during BIH construction. Its constructor accepts a vertex buffer. Its `operator()` calculates the bounding box of a specific triangle by finding the min/max coordinates of its three vertices. This allows the BIH builder to compute bounds for each leaf node (triangle).

**operator()#3**
The call operator of `TriBoundFunc`. It computes the bounding box for a given `MeshTriangle` using the stored vertex iterator and assigns it to the output `G3D::AABox`.

**WmoLiquid#2**
The default constructor for `WmoLiquid`, marked private in `WorldModel.h`. It initializes all members to zero/null. It is primarily used internally by `readFromFile` to create an empty object before populating it with data.

**WmoLiquid**
The main constructor for `WmoLiquid`. It allocates memory for the height grid (`iHeight`) and flags grid (`iFlags`) based on the provided width and height. The height grid is sized `(width + 1) * (height + 1)` to support interpolation at grid edges, while the flags grid is `width * height`.

**~WmoLiquid**
The destructor for `WmoLiquid`. It frees the dynamically allocated `iHeight` and `iFlags` arrays.

**operator=**
The copy assignment operator for `WmoLiquid`. It performs a deep copy of the liquid data. It first deletes existing resources, then allocates new memory and copies the contents from the source object. It handles self-assignment safely.

**GetLiquidHeight**
Calculates the liquid height at a specific 3D position. It determines which tile the position falls into, checks if the tile is valid (flags not `0x0F`), and then performs bilinear interpolation using the four surrounding height values. It returns `false` if the position is outside the liquid grid or if the tile is invalid.

**GetFileSize**
Returns the total number of bytes required to serialize the `WmoLiquid` object. This includes the dimensions, corner position, type, height array, and flags array.

**writeToFile#2**
Serializes the `WmoLiquid` data to a file stream. It writes the dimensions, corner, type, height array, and flags array sequentially. It returns `false` if any write operation fails.

**readFromFile#2**
A static factory method that deserializes a `WmoLiquid` object from a file stream. It reads the header fields, allocates memory for the arrays, reads the data, and assigns the resulting pointer to the `out` parameter. It cleans up partially constructed objects if reading fails.

**GroupModel**
The copy constructor for `GroupModel`. It copies scalar members and performs a deep copy of the `iLiquid` pointer if it exists. It relies on STL vector copy constructors for `vertices` and `triangles`.

**setMeshData**
Initializes the mesh data for a `GroupModel`. It swaps the provided vertex and triangle vectors into the object and then builds the internal BIH (`meshTree`) using the `TriBoundFunc` functor to calculate triangle bounds. This prepares the model for fast ray intersection queries.

**writeToFile**
Serializes the `GroupModel` to a file. It writes the bounding box, MOGP flags, WMO ID, vertices, triangles, the mesh BIH, and the liquid data (if present). Each section is prefixed with a chunk identifier (e.g., "VERT", "TRIM") and size.

**readFromFile**
Deserializes a `GroupModel` from a file. It reads the header fields, then iterates through chunks ("VERT", "TRIM", "MBIH", "LIQU") to reconstruct the vertices, triangles, BIH, and liquid data. It clears existing data before reading.

**GModelRayCallback**
A functor used during BIH ray intersection for `GroupModel`. It stores iterators to the triangle and vertex buffers. Its `operator()` calls `IntersectTriangle` for a specific triangle index. It tracks the number of hits and returns `true` to continue traversal unless `stopAtFirstHit` is triggered by the BIH logic.

**operator()**
The call operator of `GModelRayCallback`. It performs the actual ray-triangle intersection for the specified entry and updates the hit count.

**IntersectRay**
Queries the `GroupModel`'s BIH for intersections with a given ray. It uses `GModelRayCallback` to process individual triangles. It returns the number of intersections found. If `stopAtFirstHit` is true, it stops after the first hit.

**IsInsideObject**
Determines if a point is inside the `GroupModel`'s volume. It casts a ray upwards from the point and counts intersections. If there is at least one intersection, it considers the point inside and returns the distance to the first hit. It first checks if the point is within the bounding box.

**GetLiquidLevel**
Delegates to `WmoLiquid::GetLiquidHeight` if liquid data is present. Returns `false` if no liquid data exists.

**GetLiquidType**
Returns the liquid type stored in the associated `WmoLiquid` object, or 0 if no liquid data is present.

**setGroupModels**
Initializes the `WorldModel` with a list of `GroupModel`s. It swaps the provided vector into the object and builds the `WorldModel`'s BIH (`groupTree`) using the `BoundsTrait<GroupModel>::getBounds` function to determine group bounds.

**WModelRayCallBack**
A functor used during BIH ray intersection for `WorldModel`. It stores an iterator to the `GroupModel` list. Its `operator()` calls `IntersectRay` on the specific `GroupModel` for the given entry. It sets a `hit` flag if any intersection occurs.

**operator()#6**
The call operator of `WModelRayCallBack`. It delegates the ray intersection to the appropriate `GroupModel` and updates the hit status.

**IntersectRay#2**
Queries the `WorldModel`'s BIH for intersections with a given ray. It first checks if M2 models should be ignored. If there is only one group, it queries that group directly. Otherwise, it uses `WModelRayCallBack` to traverse the BIH and query individual groups.

**WModelAreaCallback**
A functor used for point-in-volume queries. It stores the `GroupModel` list and a downward direction vector. Its `operator()` checks if a point is inside a specific `GroupModel` using `IsInsideObject`. If so, it tracks the closest hit (smallest Z distance) to determine the "ceiling" or nearest enclosing volume.

**operator()#5**
The call operator of `WModelAreaCallback`. It evaluates whether the current point is inside the candidate `GroupModel` and updates the minimum Z distance if a closer interior point is found.

**IntersectPoint**
Determines if a point is inside any `GroupModel` within the `WorldModel`. It uses `WModelAreaCallback` to traverse the BIH. If a hit is found, it populates the `AreaInfo` structure with the root WMO ID, group WMO ID, MOGP flags, and the distance to the ceiling.

**GetLocationInfo**
Similar to `IntersectPoint`, but returns detailed location information including a pointer to the hit `GroupModel`. It uses the same `WModelAreaCallback` mechanism to find the enclosing volume.

**UnderObjectCheckerCallback**
A functor used to determine if a point is under a model (i.e., inside a volume). It tracks the minimum distance to exit (`outDist`) and enter (`inDist`) the volume along an upward ray. Its `UnderModel` method determines if the point is inside based on these distances.

**operator()#4**
The call operator of `UnderObjectCheckerCallback`. It calls `GroupModel::IsUnderObject` for the candidate group and updates the global minimum `outDist` and `inDist` if closer intersections are found.

**UnderModel#2**
A helper method in `UnderObjectCheckerCallback` that determines if the point is considered "under" the model based on the tracked entry and exit distances.

**IsUnderObject#2**
Queries the `WorldModel` to determine if a point is under any of its `GroupModel`s. It uses `UnderObjectCheckerCallback` to traverse the BIH. It returns `true` if the point is inside any volume and optionally outputs the distances to the nearest exit and entry points.

**writeFile**
Serializes the entire `WorldModel` to a binary file. It writes a magic header, the root WMO ID, the list of `GroupModel`s (each serialized via `GroupModel::writeToFile`), and the `WorldModel`'s BIH.

**readFile**
Deserializes a `WorldModel` from a binary file. It verifies the magic header, reads the root WMO ID, reconstructs the `GroupModel` list, and rebuilds the BIH.

**IsGoingOut**
A standalone function that determines if a ray is exiting a triangle face. It calculates the normal of the triangle (defined by three points) and checks if the dot product with the ray direction is negative. This indicates the ray is moving opposite to the normal, i.e., exiting the volume.

**GModelRayOrientedCallback**
A functor used for oriented ray intersection (entering/exiting). It tracks the minimum distance to enter (`minInDist`) and exit (`minOutDist`) the volume. Its `operator()` uses `IsGoingOut` to classify each intersection as an entry or exit event.

**operator()#2**
The call operator of `GModelRayOrientedCallback`. It processes each triangle intersection, determining if it is an entry or exit based on triangle winding and ray direction, and updates the minimum distances accordingly.

**UnderModel**
A helper method in `GModelRayOrientedCallback` that determines if the ray origin is inside the volume based on the relative values of `minInDist` and `minOutDist`.

**IsUnderObject**
Determines if a point is inside the `GroupModel`'s volume using oriented ray casting. It casts a ray upwards and uses `GModelRayOrientedCallback` to find the nearest entry and exit points. It returns `true` if the point is inside and optionally outputs the distances. It handles M2 vs. WMO winding differences.

---

<!-- machine-true, projected from graph.json -->

## Map — BoundsTrait.WorldModel

*Source:* WorldModel.cpp, WorldModel.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| getBounds | method | GroupModel/GetBound | — | — |
| IntersectTriangle | function | — | — | — |
| TriBoundFunc | ctor | — | — | — |
| operator()#3 | method | — | — | — |
| WmoLiquid#2 | ctor | — | BoundsTrait.TileAssembler/Read | — |
| WmoLiquid | ctor | — | — | — |
| ~WmoLiquid | dtor | — | — | — |
| operator= | method | — | — | — |
| GetLiquidHeight | method | — | — | — |
| GetFileSize | method | — | — | — |
| writeToFile#2 | method | — | — | — |
| readFromFile#2 | method | WmoLiquid/WmoLiquid | — | — |
| GroupModel | ctor | — | — | — |
| setMeshData | method | — | BoundsTrait.TileAssembler/convertRawFile | — |
| writeToFile | method | BIH/writeToFile | — | — |
| readFromFile | method | BIH/readFromFile, BoundsTrait.TileAssembler/readChunk | — | — |
| GModelRayCallback | ctor | — | — | — |
| operator() | method | — | — | — |
| IntersectRay | method | — | — | — |
| IsInsideObject | method | — | — | — |
| GetLiquidLevel | method | — | ModelInstance/GetLiquidLevel | — |
| GetLiquidType | method | WmoLiquid/GetType | VMapManager2/GetLiquidLevel | — |
| setGroupModels | method | — | BoundsTrait.TileAssembler/convertRawFile | — |
| WModelRayCallBack | ctor | — | — | — |
| operator()#6 | method | — | — | — |
| IntersectRay#2 | method | — | GameObjectModel/intersectRay, ModelInstance/intersectRay | — |
| WModelAreaCallback | ctor | — | — | — |
| operator()#5 | method | — | — | — |
| IntersectPoint | method | GroupModel/GetMogpFlags, GroupModel/GetWmoID | ModelInstance/intersectPoint | — |
| GetLocationInfo | method | — | ModelInstance/GetLocationInfo | — |
| UnderObjectCheckerCallback | ctor | — | — | — |
| operator()#4 | method | — | — | — |
| UnderModel#2 | method | — | — | — |
| IsUnderObject#2 | method | — | ModelInstance/isUnderModel | — |
| writeFile | method | BIH/writeToFile | BoundsTrait.TileAssembler/convertRawFile | — |
| readFile | method | BIH/readFromFile, BoundsTrait.TileAssembler/readChunk | VMapManager2/acquireModelInstance | — |
| IsGoingOut | function | — | — | — |
| GModelRayOrientedCallback | ctor | — | — | — |
| operator()#2 | method | — | — | — |
| UnderModel | method | — | — | — |
| IsUnderObject | method | — | — | — |

---

<!-- verify: failed-members | invented: operator -->

---

<!-- verify: boundary-bleed | foreign: contains, getBounds, size -->
