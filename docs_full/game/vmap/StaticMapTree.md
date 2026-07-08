# StaticMapTree

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# StaticMapTree

`StaticMapTree` manages the static geometric data for a single game map within the VMAP system. It encapsulates a `BIH` (Binary Interval Hierarchy) tree for efficient spatial queries and handles the lifecycle of map data, supporting both monolithic maps and tiled architectures. The class tracks loaded tiles via `iLoadedTiles` and manages reference counts for spawned objects in `iLoadedSpawns` to prevent premature unloading.

## Member-by-Member Behavior

### Tile Identification Utilities
These static methods facilitate the conversion between 2D tile coordinates and compact 32-bit identifiers used for hashing and storage.

*   **`packTileID`**: Combines `tileX` and `tileY` into a single `uint32` by shifting `tileX` left 16 bits and OR-ing with `tileY`.
*   **`unpackTileID`**: Reverses `packTileID`, extracting `tileX` via right-shift and `tileY` via bitwise AND with `0xFF`. Note: The mask `0xFF` restricts `tileY` to 8 bits, despite `tileX` using 16 bits; this may limit valid tile Y-coordinates to 0–255.
*   **`isTiled`**: Returns `iIsTiled`, indicating whether the map is divided into tiles.
*   **`numLoadedTiles`**: Returns the size of `iLoadedTiles`, providing the count of currently loaded tiles.

### Spatial Queries and Collision
Methods declared in the header but not listed in the MAP (e.g., `isInLineOfSight`, `getHeight`, `InitMap`) belong to other partials or are not covered by this unit’s scope. This unit’s documented members focus on tile management and identification.

## Cross-Unit Boundaries

*   **`BoundsTrait.TileAssembler`**:
    *   Calls `packTileID` and `unpackTileID` in `convertWorld2` and `readMapSpawns` to translate world coordinates to tile IDs for bounds assembly.
    *   Calls `LoadMapTile` and `UnloadMapTile` (members of `MapTree`, not this partial) to manage tile residency during assembly.
*   **`VMapManager2`**:
    *   Calls `numLoadedTiles` in `unloadMap` and `unloadMap#2` to verify that all tiles are released before removing the map from memory.

## Data Model

This unit does not interact with database tables. It operates on in-memory structures: `iLoadedTiles` (`std::unordered_map<uint32, bool>`) tracks tile presence, and `iLoadedSpawns` (`std::unordered_map<uint32, uint32>`) tracks reference counts for spawned objects.

## Notable Implementation Details

*   **Tile ID Masking**: `unpackTileID` uses `0xFF` for `tileY`, limiting it to 8 bits. If `tileY` exceeds 255, data loss occurs. This contrasts with `tileX`, which uses the upper 16 bits.
*   **Empty Tile Tracking**: `iLoadedTiles` uses a `bool` value rather than a `std::set` to explicitly mark tiles as loaded, even if they contain no geometry ("empty tiles"). This ensures consistency checks can distinguish between "not loaded" and "loaded but empty."
*   **Reference Counting**: `iLoadedSpawns` stores reference counts keyed by tree index. This prevents unloading geometry referenced by active spawns, though the increment/decrement logic resides in other units.

## Member Reference

**packTileID**: Static method packing `tileX` and `tileY` into a `uint32` ID. Called by `BoundsTrait.TileAssembler` and `MapTree`.

**unpackTileID**: Static method unpacking a `uint32` ID into `tileX` and `tileY`. Uses `0xFF` mask for `tileY`. Called by `BoundsTrait.TileAssembler`.

**isTiled**: Returns `iIsTiled` flag. No external callers.

**numLoadedTiles**: Returns `iLoadedTiles.size()`. Called by `VMapManager2`.

---

<!-- machine-true, projected from graph.json -->

## Map — StaticMapTree

*Source:* MapTree.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| packTileID | method | — | BoundsTrait.TileAssembler/convertWorld2, BoundsTrait.TileAssembler/readMapSpawns, MapTree/LoadMapTile, MapTree/UnloadMapTile | — |
| unpackTileID | method | — | BoundsTrait.TileAssembler/convertWorld2 | — |
| isTiled | method | — | — | — |
| numLoadedTiles | method | — | VMapManager2/unloadMap, VMapManager2/unloadMap#2 | — |
