<!-- provenance: verbose -->
# TerrainManager

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`TerrainManager` is the global singleton facade for terrain and geographic data. It manages the lifecycle of `TerrainInfo` objects (which hold grid-based map data) and provides a unified interface for querying zone IDs, area IDs, and area flags for any coordinate. It also maintains a registry of `LiquidTypeEntry` data loaded from DBC files, allowing other systems to resolve liquid properties by ID.

## Member-by-Member Behavior

### Terrain Data Access & Lifecycle

**`LoadTerrain`**
Loads or retrieves the `TerrainInfo` instance for `mapId`. If not present in `i_TerrainMap`, it creates a new `TerrainInfo`, inserts it, and returns it. This is the primary entry point for accessing terrain data for a specific map.

**`UnloadTerrain`**
Removes and deletes the `TerrainInfo` for `mapId` from `i_TerrainMap`, freeing associated memory.

**`Update`**
Iterates over all loaded `TerrainInfo` objects and calls their `CleanUpGrids` method with the time difference `diff`. This triggers garbage collection of unused `GridMap` tiles to prevent memory bloat. Note that `TerrainInfo::CleanUpGrids` is not thread-safe, so this method must be called from a controlled context.

**`UnloadAll`**
Deletes all `TerrainInfo` objects in `i_TerrainMap` and clears the map, used during server shutdown.

### Geographic Query Facades

These methods delegate to `TerrainInfo` after ensuring the relevant terrain is loaded. They use `const_cast` to allow lazy loading within `const` methods.

**`GetAreaFlag`**
Returns the raw 16-bit area flag for `(x, y, z)` on `mapid`. Loads terrain if necessary, then calls `TerrainInfo::GetAreaFlag`.

**`GetAreaId`**
Returns the Area ID for `(x, y, z)` on `mapid`. Obtains the area flag via `GetAreaFlag` and resolves it using the static helper `GetAreaIdByAreaFlag`.

**`GetZoneId`**
Returns the Zone ID for `(x, y, z)` on `mapid`. Obtains the area flag via `GetAreaFlag` and resolves it using `GetZoneIdByAreaFlag`.

**`GetZoneAndAreaId`**
Populates `zoneid` and `areaid` references for `(x, y, z)` on `mapid`. Fetches the area flag once and resolves both IDs via `GetZoneAndAreaIdByAreaFlag`.

### Static Resolution Helpers

**`GetAreaIdByAreaFlag`**
Static helper converting a 16-bit `areaflag` and `map_id` into a 32-bit Area ID.

**`GetZoneIdByAreaFlag`**
Static helper converting a 16-bit `areaflag` and `map_id` into a 32-bit Zone ID.

**`GetZoneAndAreaIdByAreaFlag`**
Static helper converting a 16-bit `areaflag` and `map_id` into both Zone and Area IDs.

### Liquid Type Registry

**`GetLiquidType`**
Returns a pointer to the `LiquidTypeEntry` for a given `id`. Checks bounds against `GetMaxLiquidType()` and returns the managed object or `nullptr`.

**`GetMaxLiquidType`**
Returns the size of the `mLiquidTypes` vector, used for bounds checking.

### Constructors & Destructor

**`TerrainManager`**
Private constructor initializing the singleton and likely loading liquid type DBC data.

**`~TerrainManager`**
Destructor cleaning up terrain data and liquid entries.

**`operator=`**
Deleted assignment operator preventing singleton copying.

## Cross-Unit Boundaries

### Calls Out (Dependencies)

*   **`TerrainInfo`**: `TerrainManager` creates `TerrainInfo` objects in `LoadTerrain` and delegates all geographic queries (`GetAreaFlag`, `GetAreaId`, etc.) to them. `Update` calls `TerrainInfo::CleanUpGrids`.
*   **`LiquidTypeEntry`**: Stored in `mLiquidTypes` and returned by `GetLiquidType`.

### Called By (Consumers)

*   **`GridMap`**: `GridMap::getLiquidStatus` calls `GetLiquidType` to resolve liquid properties.
*   **`Player.Main`**: `UpdateTerainEnvironmentFlags` calls `GetLiquidType`; `GetZoneIdFromDB` calls `GetZoneId`.
*   **`ChatHandler.TeleportCommands`**: `HandleTeleNameCommand` calls `GetZoneId`.
*   **`ObjectMgr`**: `GetClosestGraveYard` calls `GetZoneAndAreaId`.

## Data Model

`TerrainManager` does not interact with database tables. It relies on:
1.  **DBC Files**: For `LiquidTypeEntry` data and potentially for resolving Area/Zone IDs from flags.
2.  **Asset Files**: Indirectly via `TerrainInfo` and `GridMap` for terrain geometry (WDT/M2/ADT).
3.  **No SQL Queries**: No direct database interaction occurs in this unit.

## Notable Implementation Details

*   **Singleton & Locking**: Inherits from `MaNGOS::Singleton` and `ClassLevelLockable`, providing global access via `sTerrainMgr` and mutex protection for public methods.
*   **Const-Cast Lazy Loading**: Methods like `GetAreaFlag` are `const` but call non-const `LoadTerrain` via `const_cast`. This allows callers to treat the manager as immutable while enabling internal caching.
*   **Thread Safety Caveat**: While `TerrainManager` is lockable, `TerrainInfo::CleanUpGrids` is explicitly **not thread-safe**. `Update` must be called from a single-threaded context (e.g., main loop) to avoid race conditions during grid cleanup.
*   **Memory Management**: Uses `std::unique_ptr` for `LiquidTypeEntry` storage but raw pointers for `TerrainInfo` in `i_TerrainMap`, requiring manual deletion in `UnloadTerrain` and `UnloadAll`.

## Member Reference

**`GetLiquidType`**: Returns `LiquidTypeEntry*` for `id`, checking bounds with `GetMaxLiquidType`. Called by `GridMap::getLiquidStatus` and `Player.Main::UpdateTerainEnvironmentFlags`.

**`GetMaxLiquidType`**: Returns `mLiquidTypes.size()`, used for bounds checking in `GetLiquidType`.

**`GetAreaFlag`**: Loads terrain for `mapid` if needed, delegates to `TerrainInfo::GetAreaFlag` for `(x, y, z)`.

**`GetAreaId`**: Gets `AreaFlag` for `(x, y, z)` on `mapid`, resolves to Area ID via `GetAreaIdByAreaFlag`.

**`GetZoneId`**: Gets `AreaFlag` for `(x, y, z)` on `mapid`, resolves to Zone ID via `GetZoneIdByAreaFlag`. Called by `ChatHandler::HandleTeleNameCommand` and `Player.Main::GetZoneIdFromDB`.

**`GetZoneAndAreaId`**: Gets `AreaFlag` for `(x, y, z)` on `mapid`, resolves both Zone and Area IDs via `GetZoneAndAreaIdByAreaFlag`. Called by `ObjectMgr::GetClosestGraveYard`.

**`TerrainManager`**: Private singleton constructor.

**`operator=`**: Deleted assignment operator.

---

<!-- machine-true, projected from graph.json -->

## Map — TerrainManager

*Source:* GridMap.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GetLiquidType | method | — | GridMap/getLiquidStatus, GridMap/getLiquidStatus#2, Player.Main/UpdateTerainEnvironmentFlags | — |
| GetMaxLiquidType | method | — | — | — |
| GetAreaFlag | method | — | — | — |
| GetAreaId | method | — | — | — |
| GetZoneId | method | — | ChatHandler.TeleportCommands/HandleTeleNameCommand, Player.Main/GetZoneIdFromDB | — |
| GetZoneAndAreaId | method | — | ObjectMgr/GetClosestGraveYard | — |
| TerrainManager | decl | — | — | — |
| operator= | decl | — | — | — |
