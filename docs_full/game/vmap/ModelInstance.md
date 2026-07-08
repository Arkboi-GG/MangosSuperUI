# ModelInstance

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ModelInstance

**Purpose & Responsibilities**

`ModelInstance` represents a single placed 3D model (building, prop, etc.) within the VMAP system. It bridges high-level spatial queries (line-of-sight, ground height, containment) with the low-level geometry in `WorldModel`. Its core responsibilities are:
1.  **Coordinate Transformation:** Converting world-space inputs to local model-space coordinates using precomputed inverse rotation (`iInvRot`) and inverse scale (`iInvScale`), and transforming results back to world space.
2.  **Query Delegation:** Performing fast bounding-box rejection tests, then delegating detailed geometric checks to `BoundsTrait.WorldModel`.
3.  **Lifecycle Management:** Tracking load state via `iModel` and supporting serialization of spawn data via `ModelSpawn`.

It inherits from `ModelSpawn`, which holds static placement data (position, rotation, scale, ID, name). `ModelInstance` adds runtime logic and the link to the geometry mesh.

## Member-by-Member Behavior

### Construction and State

*   **`ModelInstance()` (Default):** Initializes `iInvScale` to 0 and `iModel` to `nullptr`. Used for placeholders.
*   **`ModelInstance#2` (Parameterized):** The primary constructor. It initializes `ModelSpawn` data and precomputes `iInvRot` (from Euler angles ZYX) and `iInvScale` (reciprocal of `iScale`) to optimize repeated coordinate transforms. Called by `MapTree/InitMap` and `MapTree/LoadMapTile`.
*   **`setUnloaded`:** Sets `iModel` to `nullptr`, marking the instance as unloaded. Subsequent queries fail gracefully. Called by `MapTree/UnloadMapTile`.

### Spatial Queries

All query methods follow a pattern: check load state, reject if outside bounding box (`iBound`), transform input to model space, delegate to `WorldModel`, and transform results back to world space if needed.

*   **`intersectRay`:** Checks if a ray intersects the model. Performs a bounding-box test, transforms the ray to model space, and calls `BoundsTrait.WorldModel/IntersectRay#2`. On hit, it scales the distance back to world space and updates `pMaxDist`. Called by `MapTree/operator()#3` and `MapTree/operator()#4`.
*   **`intersectPoint`:** Updates `AreaInfo` with ground height if the point is on the model surface. Skips M2 models (no area info). Transforms point and downward vector to model space, calls `BoundsTrait.WorldModel/IntersectPoint`, and updates `info.ground_Z` and `info.adtId` if the result is higher than the current best. Called by `MapTree/operator()`.
*   **`isUnderModel`:** Checks if a point is inside the model volume. For M2 models, it currently has incomplete logic (falls through to `else` block, likely returning false due to missing bounds). For WMOs, it checks the bounding box, transforms the point and upward vector to model space, and calls `BoundsTrait.WorldModel/IsUnderObject#2`. Called by `MapTree/operator()#5`.
*   **`GetLocationInfo`:** Retrieves detailed location metadata (root ID, hit model, ground Z). Skips M2 models. Transforms point to model space, calls `BoundsTrait.WorldModel/GetLocationInfo`, and updates `info` if the resulting ground Z is higher than the current best. Called by `MapTree/operator()#2`.
*   **`GetLiquidLevel`:** Calculates liquid level at a point. Transforms point to model space, calls `BoundsTrait.WorldModel/GetLiquidLevel`, and transforms the resulting Z-distance back to world space. Called by `VMapManager2/GetLiquidLevel`.

### Accessors

*   **`getWorldModel`:** Returns the `std::shared_ptr<WorldModel>`.
*   **`getScale`:** Returns `iInvScale` (the *inverse* scale, not the actual scale).
*   **`getRot`:** Returns `iInvRot` (the *inverse* rotation matrix).

### Serialization

*   **`readFromFile`:** Static method to deserialize `ModelSpawn` from a binary file. Reads flags, ADT ID, Model ID, position, rotation, scale, optional bounding box (if `MOD_HAS_BOUND`), and name. Validates byte counts. Called by `BoundsTrait.TileAssembler/readMapSpawns`, `MapTree/InitMap`, `MapTree/LoadMapTile`, and `MapTree/UnloadMapTile`.
*   **`writeToFile`:** Static method to serialize `ModelSpawn` to a binary file. Writes flags, ADT ID, Model ID, position, rotation, scale, optional bounding box, and name. Called by `BoundsTrait.TileAssembler/convertWorld2`.

## Cross-Unit Boundaries

*   **Calls `BoundsTrait.WorldModel`:** All spatial query methods (`intersectRay`, `intersectPoint`, `isUnderModel`, `GetLocationInfo`, `GetLiquidLevel`) delegate detailed geometric calculations to `WorldModel` after coordinate transformation.
*   **Called by `MapTree`:** `MapTree` uses `intersectRay`, `intersectPoint`, `isUnderModel`, and `GetLocationInfo` for line-of-sight, ground height, containment, and area detection. It also constructs `ModelInstance`s (`InitMap`, `LoadMapTile`) and unloads them (`UnloadMapTile`).
*   **Called by `VMapManager2`:** Uses `GetLiquidLevel` for water level calculations.
*   **Called by `BoundsTrait.TileAssembler`:** Uses `readFromFile` and `writeToFile` for map tile assembly and conversion.

## Data Model

This unit does not interact with database tables. It operates on in-memory structures and binary files for persistence.

## Notable Implementation Details

*   **Inverse Transform Precomputation:** `iInvRot` and `iInvScale` are computed once in the constructor to avoid costly inverse operations during frequent spatial queries.
*   **M2 vs. WMO Handling:** M2 models (dynamic/creature-like) are skipped for area info (`intersectPoint`, `GetLocationInfo`) because they lack static area data. `isUnderModel` has incomplete logic for M2s.
*   **Bounding Box Rejection:** All queries first check `iBound.contains()` or ray intersection with `iBound` to quickly reject distant queries.
*   **Misleading Accessor Names:** `getScale()` returns the *inverse* scale, and `getRot()` returns the *inverse* rotation matrix. Users must account for this inversion.
*   **Binary Serialization:** `readFromFile` and `writeToFile` use a fixed binary layout. The presence of bounding box data depends on the `MOD_HAS_BOUND` flag. Name strings are length-prefixed.

## Member Reference

**ModelInstance#2**: Constructor taking `ModelSpawn` and `std::shared_ptr<WorldModel>`. Initializes base data and precomputes `iInvRot` and `iInvScale`. Called by `MapTree/InitMap` and `MapTree/LoadMapTile`.

**intersectRay**: Checks ray intersection. Performs bounding-box test, transforms ray to model space, delegates to `BoundsTrait.WorldModel/IntersectRay#2`, and updates `pMaxDist` on hit. Called by `MapTree/operator()#3` and `MapTree/operator()#4`.

**intersectPoint**: Updates `AreaInfo` with ground height if point is on surface. Skips M2s, checks bounding box, transforms point to model space, delegates to `BoundsTrait.WorldModel/IntersectPoint`, and updates `info` if result is higher. Called by `MapTree/operator()`.

**ModelInstance**: Default constructor initializing `iInvScale` to 0 and `iModel` to `nullptr`. Called by `MapTree/InitMap`.

**setUnloaded**: Sets `iModel` to `nullptr` to mark instance as unloaded. Called by `MapTree/UnloadMapTile`.

**getWorldModel**: Returns the `std::shared_ptr<WorldModel>`.

**getScale**: Returns `iInvScale` (inverse scale).

**getRot**: Returns `iInvRot` (inverse rotation matrix).

**isUnderModel**: Checks if point is inside model volume. Handles M2s with incomplete logic, checks bounding box for WMOs, transforms point to model space, and delegates to `BoundsTrait.WorldModel/IsUnderObject#2`. Called by `MapTree/operator()#5`.

**GetLocationInfo**: Retrieves location metadata (root ID, hit model, ground Z). Skips M2s, checks bounding box, transforms point to model space, delegates to `BoundsTrait.WorldModel/GetLocationInfo`, and updates `info` if result is higher. Called by `MapTree/operator()#2`.

**GetLiquidLevel**: Calculates liquid level at a point. Transforms point to model space, delegates to `BoundsTrait.WorldModel/GetLiquidLevel`, and transforms result back to world space. Called by `VMapManager2/GetLiquidLevel`.

**readFromFile**: Static method to deserialize `ModelSpawn` from binary file. Reads flags, IDs, position, rotation, scale, optional bounding box, and name. Called by `BoundsTrait.TileAssembler/readMapSpawns`, `MapTree/InitMap`, `MapTree/LoadMapTile`, and `MapTree/UnloadMapTile`.

**writeToFile**: Static method to serialize `ModelSpawn` to binary file. Writes flags, IDs, position, rotation, scale, optional bounding box, and name. Called by `BoundsTrait.TileAssembler/convertWorld2`.

---

<!-- machine-true, projected from graph.json -->

## Map — ModelInstance

*Source:* ModelInstance.cpp, ModelInstance.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ModelInstance#2 | ctor | — | MapTree/InitMap, MapTree/LoadMapTile | — |
| intersectRay | method | BoundsTrait.WorldModel/IntersectRay#2 | MapTree/operator()#3, MapTree/operator()#4 | — |
| intersectPoint | method | BoundsTrait.WorldModel/IntersectPoint | MapTree/operator() | — |
| ModelInstance | ctor | — | MapTree/InitMap | — |
| setUnloaded | method | — | MapTree/UnloadMapTile | — |
| getWorldModel | method | — | — | — |
| getScale | method | — | — | — |
| getRot | method | — | — | — |
| isUnderModel | method | BoundsTrait.WorldModel/IsUnderObject#2 | MapTree/operator()#5 | — |
| GetLocationInfo | method | BoundsTrait.WorldModel/GetLocationInfo | MapTree/operator()#2 | — |
| GetLiquidLevel | method | BoundsTrait.WorldModel/GetLiquidLevel | VMapManager2/GetLiquidLevel | — |
| readFromFile | method | — | BoundsTrait.TileAssembler/readMapSpawns, MapTree/InitMap, MapTree/LoadMapTile, MapTree/UnloadMapTile | — |
| writeToFile | method | — | BoundsTrait.TileAssembler/convertWorld2 | — |
