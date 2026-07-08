<!-- provenance: verbose -->
# IVMapManager

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`IVMapManager` is the abstract interface for the Virtual Map (VMap) system. It defines the contract for querying 3D spatial data—collision, line-of-sight (LOS), and height—from static world models and terrain. By separating the interface from the implementation, it allows the core engine (`Map`, `Spell`, `GridMap`) to request spatial services without depending on specific loading or parsing logic.

The class manages three global runtime toggles stored in private members, all defaulting to `true` in the constructor:
1.  `iEnableLineOfSightCalc`: Enables/disables LOS checks.
2.  `iEnableHeightCalc`: Enables/disables height queries against models.
3.  `m_useManagedPtrs`: Controls whether model instances use smart pointers.

## Member-by-Member Behavior

### Configuration & State
Inline methods manage the global toggles. They are configured at startup by `World/LoadConfigSettings` and checked by consumers to skip expensive calculations if disabled.

*   **`setEnableLineOfSightCalc`** / **`isLineOfSightCalcEnabled`**: Control LOS calculation.
*   **`setEnableHeightCalc`** / **`isHeightCalcEnabled`**: Control height calculation.
*   **`isMapLoadingEnabled`**: Returns `true` if *either* LOS or Height calculation is enabled (`iEnableLineOfSightCalc || iEnableHeightCalc`). Used by loaders to decide if map data needs to be loaded.
*   **`setUseManagedPtrs`** / **`getUseManagedPtrs`**: Control pointer management strategy for model instances.

### Spatial Queries
Pure virtual methods defining the spatial API. Implementations must handle the actual geometry intersection tests.

*   **`isInLineOfSight`**: Checks if a direct line exists between two 3D points on a map. Accepts `ignoreM2Model` to exclude specific model types.
*   **`getHeight`**: Finds the height of the ground or nearest surface at `(x, y)`, searching down from `z` up to `maxSearchDist`.
*   **`getObjectHitPos`**: Raycasts from start to end point. Returns `true` and the hit position if an object is intersected. `pModifyDist` adjusts the result position toward the origin.
*   **`FindCollisionModel`**: Identifies the specific `ModelInstance` intersecting a ray or point.
*   **`getAreaInfo`**: Retrieves area metadata (flags, ADT/root/group IDs) at a location, adjusting the `z` parameter to the valid ground height.
*   **`isUnderModel`**: Checks if a point is inside or beneath a 3D model. Optionally outputs distances to nearest surfaces.
*   **`GetLiquidLevel`**: Queries liquid data (level, floor, type) at a location for a specific liquid type.

### Lifecycle & Utilities
Pure virtual methods for managing map data in memory and debugging.

*   **`loadMap`**: Loads VMap data for a map ID and grid `(x, y)` from `pBasePath`. Returns `VMAPLoadResult`.
*   **`existsMap`**: Checks if VMap data is already loaded for a map ID and grid.
*   **`unloadMap` (overloads)**: Unloads data for a specific grid `(x, y)` or an entire map ID.
*   **`getDirFileName`**: Generates the file path string for a map/grid combination.
*   **`processCommand`**: Interface for debug commands.

## Cross-Unit Boundaries

*   **`GridMap`**: Primary consumer for lifecycle and static queries.
    *   Calls **`loadMap`** (`LoadMapAndVMap`), **`existsMap`** and **`getDirFileName`** (`ExistVMap`), **`getHeight`** and **`isHeightCalcEnabled`** (`GetHeightStatic`), **`unloadMap#2`** (`CleanUpGrids`), **`unloadMap`** (`~TerrainInfo`), **`getAreaInfo`** (`GetAreaInfo`), and **`GetLiquidLevel`** (`getLiquidStatus#2`).
*   **`Map.Main`**: Consumer for dynamic entity queries.
    *   Calls **`isInLineOfSight`** (`isInLineOfSight`), **`getObjectHitPos`** (`GetLosHitPosition`), and **`FindCollisionModel`** (`FindCollisionModel`).
*   **`VMapManager2`**: The concrete implementation. It calls back into the interface methods to respect global configuration flags before performing work.
    *   Calls **`isLineOfSightCalcEnabled`** (`FindCollisionModel`, `getObjectHitPos`, `isInLineOfSight`), **`isHeightCalcEnabled`** (`getHeight`), **`isMapLoadingEnabled`** (`loadMap`), and **`getUseManagedPtrs`** (`acquireModelInstance`).
*   **`World`**: Configures the system at startup.
    *   Calls **`setEnableLineOfSightCalc`**, **`setEnableHeightCalc`**, and **`setUseManagedPtrs`** (`LoadConfigSettings`).
*   **`Spell.Main`**: Checks LOS requirements.
    *   Calls **`isLineOfSightCalcEnabled`** (`CheckCast`).
*   **`MoveMap`**: Checks for existing maps.
    *   Calls **`existsMap`** (`loadMap`).

## Data Model

This unit does not interact with any database tables. It operates on in-memory representations of binary world assets (M2 models, ADT terrain).

## Notable Implementation Details

*   **Derived Loading State**: `isMapLoadingEnabled()` computes `iEnableLineOfSightCalc || iEnableHeightCalc`. If both are false, map loading is skipped entirely, saving I/O and memory.
*   **Inline Performance**: Configuration getters/setters are inline to minimize overhead in hot paths (e.g., every LOS check).
*   **Invalid Height Constants**: `VMAP_INVALID_HEIGHT` (-100000.0f) and `VMAP_INVALID_HEIGHT_VALUE` (-200000.0f) are defined for consumers to detect failed height queries.
*   **Abstract Base**: Cannot be instantiated directly; enforces dependency inversion.

## Member Reference

**IVMapManager**
Constructor. Initializes `iEnableLineOfSightCalc`, `iEnableHeightCalc`, and `m_useManagedPtrs` to `true`.

**~IVMapManager**
Virtual destructor.

**loadMap**
Pure virtual. Loads VMap data for a map/grid. Called by `GridMap/LoadMapAndVMap`.

**existsMap**
Pure virtual. Checks if VMap data exists for a map/grid. Called by `GridMap/ExistVMap` and `MoveMap/loadMap`.

**unloadMap#2**
Pure virtual overload. Unloads VMap data for a specific grid. Called by `GridMap/CleanUpGrids`.

**unloadMap**
Pure virtual overload. Unloads all VMap data for a map ID. Called by `GridMap/~TerrainInfo`.

**isInLineOfSight**
Pure virtual. Checks LOS between two points. Called by `Map.Main/isInLineOfSight`.

**getHeight**
Pure virtual. Gets height at a location. Called by `GridMap/GetHeightStatic`.

**getObjectHitPos**
Pure virtual. Raycasts for object hit position. Called by `Map.Main/GetLosHitPosition`.

**FindCollisionModel**
Pure virtual. Finds intersecting model instance. Called by `Map.Main/FindCollisionModel`.

**processCommand**
Pure virtual. Processes debug commands. No external callers.

**setEnableLineOfSightCalc**
Inline. Sets LOS enable flag. Called by `World/LoadConfigSettings`.

**setEnableHeightCalc**
Inline. Sets height enable flag. Called by `World/LoadConfigSettings`.

**isLineOfSightCalcEnabled**
Inline. Returns LOS enable flag. Called by `Spell.Main/CheckCast`, `VMapManager2/FindCollisionModel`, `VMapManager2/getObjectHitPos`, `VMapManager2/isInLineOfSight`.

**isHeightCalcEnabled**
Inline. Returns height enable flag. Called by `GridMap/GetHeightStatic`, `VMapManager2/getHeight`.

**isMapLoadingEnabled**
Inline. Returns true if LOS or Height is enabled. Called by `GridMap/ExistVMap`, `VMapManager2/loadMap`.

**getDirFileName**
Pure virtual. Generates file path for map/grid. Called by `GridMap/ExistVMap`.

**getAreaInfo**
Pure virtual. Gets area metadata. Called by `GridMap/GetAreaInfo`.

**isUnderModel**
Pure virtual. Checks if point is under model. No external callers.

**GetLiquidLevel**
Pure virtual. Queries liquid level. Called by `GridMap/getLiquidStatus#2`.

**getUseManagedPtrs**
Inline. Returns managed ptrs flag. Called by `VMapManager2/acquireModelInstance`.

**setUseManagedPtrs**
Inline. Sets managed ptrs flag. Called by `World/LoadConfigSettings`.

---

<!-- machine-true, projected from graph.json -->

## Map — IVMapManager

*Source:* IVMapManager.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| IVMapManager | ctor | — | — | — |
| ~IVMapManager | dtor | — | — | — |
| loadMap | decl | — | GridMap/LoadMapAndVMap | — |
| existsMap | decl | — | GridMap/ExistVMap, MoveMap/loadMap | — |
| unloadMap#2 | decl | — | GridMap/CleanUpGrids | — |
| unloadMap | decl | — | GridMap/~TerrainInfo | — |
| isInLineOfSight | decl | — | Map.Main/isInLineOfSight | — |
| getHeight | decl | — | GridMap/GetHeightStatic | — |
| getObjectHitPos | decl | — | Map.Main/GetLosHitPosition | — |
| FindCollisionModel | decl | — | Map.Main/FindCollisionModel | — |
| processCommand | decl | — | — | — |
| setEnableLineOfSightCalc | method | — | World/LoadConfigSettings | — |
| setEnableHeightCalc | method | — | World/LoadConfigSettings | — |
| isLineOfSightCalcEnabled | method | — | Spell.Main/CheckCast, VMapManager2/FindCollisionModel, VMapManager2/getObjectHitPos, VMapManager2/isInLineOfSight | — |
| isHeightCalcEnabled | method | — | GridMap/GetHeightStatic, VMapManager2/getHeight | — |
| isMapLoadingEnabled | method | — | GridMap/ExistVMap, VMapManager2/loadMap | — |
| getDirFileName | decl | — | GridMap/ExistVMap | — |
| getAreaInfo | decl | — | GridMap/GetAreaInfo | — |
| isUnderModel | decl | — | — | — |
| GetLiquidLevel | decl | — | GridMap/getLiquidStatus#2 | — |
| getUseManagedPtrs | method | — | VMapManager2/acquireModelInstance | — |
| setUseManagedPtrs | method | — | World/LoadConfigSettings | — |
