<!-- provenance: verbose -->
# GameObjectModel

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`GameObjectModel` represents the 3D geometry of a `GameObject` for collision detection and line-of-sight (LoS) calculations within the VMap system. It acts as a bridge between high-level game object state (position, orientation, scale) and the low-level `VMAP::WorldModel` intersection engine.

Key responsibilities:
1.  **Geometry Resolution:** Maps a `GameObject`'s display ID to a specific model file and its pre-computed bounding box using a global binary cache (`vmaps/gameobject_models`).
2.  **Spatial Transformation:** Computes the world-space Axis-Aligned Bounding Box (AABB) by applying the object's position, rotation (Euler ZYX), and scale to the model's local bounds.
3.  **Ray Intersection:** Handles ray queries by transforming world-space rays into the model's local object space before delegating to `WorldModel`.
4.  **State Management:** Toggles collision enablement and updates spatial transforms when the `GameObject` moves.

## Member-by-Member Behavior

### Initialization and Construction

**`GameobjectModelData` (ctor)**
Constructs the internal helper struct storing a model filename and its base AABB. Used exclusively by the global `model_list` map in `GameObjectModel.cpp`.

**`LoadGameObjectModelList` (function)**
Populates the global `model_list` (`std::unordered_map<uint32, GameobjectModelData>`) by parsing the binary file `vmaps/gameobject_models` from the world data path.
*   Reads entries containing a `displayId`, string length, model name, and two `Vector3` points defining the bounding box.
*   Aborts and logs via `Log.Main/Out` if the string length exceeds the 500-byte buffer, indicating corruption.
*   Called by `World/SetInitialWorldSettings` at server startup.

**`GameObjectModel` (ctor)**
Private default constructor. Initializes `collision_enabled` to `false`, scale factors to zero, and `iModel` to `nullptr`. Enforces creation via the static factory `construct`.

**`construct` (method)**
Static factory method for creating `GameObjectModel` instances.
1.  **Filtering:** Returns `nullptr` if the `GameObject` is a Button/Goober with `losOK` true, or if `GameObjectInfo/IsServerOnly` is true.
2.  **Lookup:** Retrieves `GameObjectDisplayInfoEntry` via `GameObject/GetDisplayId`. Returns `nullptr` if missing.
3.  **Initialization:** Creates a `GameObjectModel` and calls `initialize`. Deletes the instance and returns `nullptr` if initialization fails.
*   Called by `GameObject/UpdateModel`.

**`initialize` (method)**
Sets up the model instance geometry and transforms.
1.  **Cache Lookup:** Finds `GameobjectModelData` in `model_list` by display ID. Returns `false` if missing.
2.  **Validation:** Logs via `Log.Main/Out` and returns `false` if the model’s bounding box is zero-sized.
3.  **Model Acquisition:** Uses `VMapFactory/createOrGetVMapManager` and `VMapManager2/acquireModelInstance` to load the mesh from `World/GetDataPath` + "vmaps/".
4.  **Flag Setting:** If the model is an `.m2` file and `GameObjectInfo/CanAlwaysBreakLoS` is false, sets `VMAP::MOD_M2` on the `WorldModel` via `WorldModel/setModelFlags`.
5.  **Transform Calculation:**
    *   Stores position (`WorldObject.Object/GetPositionX/Y/Z`) in `iPos`.
    *   Computes inverse rotation matrix from orientation (`WorldObject.Object/GetOrientation`) using Euler ZYX.
    *   Stores scale (`Object/GetObjectScale`) and its inverse.
    *   Transforms the local bounding box: scales it, rotates the 8 corners, merges into a new AABB, and translates by `iPos` to produce world-space `iBound`.
6.  **Debug:** If `#ifdef SPAWN_CORNERS` is defined, spawns temporary creatures at the bounding box corners.

### Spatial Queries and Updates

**`getBounds` (method)**
Returns a const reference to `iBound` (world-space AABB).
*   Called by `BoundsTrait.DynamicTree/getBounds`, `BoundsTrait.DynamicTree/getBounds2`, and `GameObject/GetLosCheckPosition` for broad-phase culling and LoS positioning.

**`getPosition` (method)**
Returns a const reference to `iPos` (world-space position).
*   Called by `BoundsTrait.DynamicTree/getPosition` and `ChatHandler.DebugCommands/HandleDebugLoSCommand`.

**`disable` (method)**
Sets `collision_enabled` to `false`, disabling ray intersections.

**`enable` (method)**
Sets `collision_enabled` to the passed boolean.
*   Called by `GameObject/UpdateCollisionState` to toggle collision dynamically.

**`intersectRay` (method)**
Performs ray intersection checks.
1.  Returns `false` if `collision_enabled` is false.
2.  Checks if the ray intersects `iBound`. If not, returns `false`.
3.  Transforms the ray from world space to local object space: applies inverse rotation and scale to origin and direction, and scales `MaxDist`.
4.  Delegates to `BoundsTrait.WorldModel/IntersectRay#2` on `iModel`.
5.  If hit, scales the distance back to world space and updates `MaxDist`.
*   Called by `BoundsTrait.DynamicTree/operator()` variants.

**`Relocate` (method)**
Updates spatial data when the `GameObject` moves.
1.  Validates `iModel` exists and display ID is in `model_list`. Logs via `Log.Main/Out` and returns `false` if bounds are zero.
2.  Updates `iPos` from `GameObject` position.
3.  Recomputes rotation matrix and inverse from new orientation.
4.  Recomputes `iBound` by scaling, rotating, and translating the base model box.
5.  **Debug:** If `#ifdef SPAWN_CORNERS` is defined, spawns temporary flying creatures at the new bounding box corners.
*   Called by `GameObject/UpdateModelPosition`.

**`~GameObjectModel` (dtor)**
Empty destructor. `std::shared_ptr<VMAP::WorldModel> iModel` handles resource cleanup.

## Cross-Unit Boundaries

*   **`VMAP` System (`VMapManager2`, `WorldModel`, `VMapFactory`):**
    *   `initialize` calls `VMapFactory::createOrGetVMapManager` and `VMapManager2::acquireModelInstance` to load geometry.
    *   `intersectRay` delegates intersection math to `WorldModel::IntersectRay`.
    *   Separates game-specific transform logic from raw geometric intersection.

*   **`GameObject` and `WorldObject`:**
    *   Reads position, orientation, scale, and display ID from `GameObject` (via `WorldObject` base methods) during `initialize` and `Relocate`.
    *   Filters objects based on `GameObjectInfo` properties (`IsServerOnly`, `CanAlwaysBreakLoS`, type-specific `losOK`).

*   **`World` and `Log`:**
    *   Uses `World::GetDataPath` to locate the `vmaps` directory.
    *   Uses `Log::Out` for debug messages regarding missing models, zero bounds, or file corruption.

*   **`BoundsTrait` (Dynamic Tree):**
    *   Implements the interface for the spatial partitioning tree.
    *   The tree calls `getBounds`, `getPosition`, and `intersectRay` for efficient collision checks.

## Data Model

This unit does not interact with SQL database tables. It relies on:
1.  **DBC Files:** `GameObjectDisplayInfo.dbc` (via `sGameObjectDisplayInfoStore`) to map Display IDs to metadata.
2.  **Binary Cache:** `vmaps/gameobject_models`, a custom binary file containing pre-computed bounding boxes, loaded by `LoadGameObjectModelList`.
3.  **VMap Files:** `.vmd`/`.vmt` model files in `vmaps/`, loaded by `VMapManager2`.

## Notable Implementation Details

1.  **Global Model Cache:** `model_list` is a global static map. All instances share this lookup table for model names and base bounds. Changes to the VMap cache require a server restart.
2.  **M2 Model Flags:** `initialize` sets `VMAP::MOD_M2` on `.m2` models unless `CanAlwaysBreakLoS` is true. This likely alters how the intersection engine handles transparency or internal geometry for M2 files.
3.  **Inverse Transform Caching:** `iInvRot` and `iInvScale` are cached during initialization/relocation to avoid per-ray matrix inversions in `intersectRay`.
4.  **Rotated Bounding Boxes:** `iBound` is computed by rotating the 8 corners of the local box and merging them into a new AABB. This ensures tight fitting for rotated objects, improving broad-phase culling.
5.  **Debug Spawning:** `#ifdef SPAWN_CORNERS` spawns temporary creatures at bounding box corners in `initialize` and `Relocate` for visualization.
6.  **Private Constructor:** Enforces use of `construct` to ensure valid initialization.

## Member Reference

**GameobjectModelData** (ctor): Constructs the helper struct holding the model name and its base bounding box. Used internally by `LoadGameObjectModelList`.

**LoadGameObjectModelList** (function): Loads the global `model_list` map from the binary file `vmaps/gameobject_models`. Reads display IDs, names, and bounding boxes. Called by `World/SetInitialWorldSettings`.

**GameObjectModel** (ctor): Private default constructor. Initializes collision to disabled and pointers to null. Ensures instances are only created via `construct`.

**getBounds** (method): Returns the world-space bounding box (`iBound`). Called by `BoundsTrait.DynamicTree/getBounds`, `BoundsTrait.DynamicTree/getBounds2`, and `GameObject/GetLosCheckPosition`.

**getPosition** (method): Returns the world-space position (`iPos`). Called by `BoundsTrait.DynamicTree/getPosition` and `ChatHandler.DebugCommands/HandleDebugLoSCommand`.

**disable** (method): Sets `collision_enabled` to false. Disables ray intersection checks.

**enable** (method): Sets `collision_enabled` to the specified boolean. Called by `GameObject/UpdateCollisionState`.

**~GameObjectModel** (dtor): Empty destructor. Cleanup of `iModel` is handled by `std::shared_ptr`.

**initialize** (method): Core setup logic. Looks up model data in `model_list`, validates bounds, acquires the `WorldModel` instance via `VMapManager2`, sets M2 flags if applicable, and computes world-space transforms (position, rotation, scale, bounding box) from `GameObject` data.

**construct** (method): Static factory method. Filters out non-collidable `GameObject`s (Buttons/Goobers with `losOK`, ServerOnly objects), looks up display info, creates a `GameObjectModel`, and calls `initialize`. Called by `GameObject/UpdateModel`.

**intersectRay** (method): Performs ray intersection. Checks collision enable state, tests against world-space bounding box, transforms ray to local object space, and delegates to `WorldModel::IntersectRay`. Adjusts hit distance back to world space. Called by `BoundsTrait.DynamicTree/operator()` variants.

**Relocate** (method): Updates spatial data after `GameObject` movement. Recomputes position, rotation, and bounding box based on new `GameObject` coordinates. Called by `GameObject/UpdateModelPosition`.

---

<!-- machine-true, projected from graph.json -->

## Map — GameObjectModel

*Source:* GameObjectModel.cpp, GameObjectModel.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GameobjectModelData | ctor | — | — | — |
| LoadGameObjectModelList | function | Log.Main/Out, World/GetDataPath | World/SetInitialWorldSettings | — |
| GameObjectModel | ctor | — | — | — |
| getBounds | method | — | BoundsTrait.DynamicTree/getBounds, BoundsTrait.DynamicTree/getBounds2, GameObject/GetLosCheckPosition | — |
| getPosition | method | — | BoundsTrait.DynamicTree/getPosition, ChatHandler.DebugCommands/HandleDebugLoSCommand | — |
| disable | method | — | — | — |
| enable | method | — | GameObject/UpdateCollisionState | — |
| ~GameObjectModel | dtor | — | — | — |
| initialize | method | GameObject/GetGOInfo, GameObjectInfo/CanAlwaysBreakLoS, Log.Main/Out, Object/GetObjectScale, VMapFactory/createOrGetVMapManager, VMapManager2/acquireModelInstance, World/GetDataPath, WorldModel/setModelFlags, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | — | — |
| construct | method | GameObject/GetDisplayId, GameObject/GetGOInfo, GameObjectInfo/IsServerOnly | GameObject/UpdateModel | — |
| intersectRay | method | BoundsTrait.WorldModel/IntersectRay#2 | BoundsTrait.DynamicTree/operator(), BoundsTrait.DynamicTree/operator()#2, BoundsTrait.DynamicTree/operator()#3 | — |
| Relocate | method | GameObject/GetDisplayId, Log.Main/Out, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | GameObject/UpdateModelPosition | — |
