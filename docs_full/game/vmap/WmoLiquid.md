# WmoLiquid

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`WmoLiquid` is a data structure within the `VMAP` namespace that represents the liquid surface geometry for a specific World Model Object (WMO) group. It stores liquid data as a regular 2D grid of height values and flags, mapped to 3D space via a corner position and tile dimensions. The class acts as a container for raw memory buffers (`iHeight` and `iFlags`), providing accessor methods for these buffers and the liquid type. This enables the `BoundsTrait` subsystem to query liquid properties during map assembly and type identification.

## Member-by-Member Behavior

### Construction

**`WmoLiquid`**
Initializes the liquid grid metadata. It accepts grid dimensions (`width`, `height`), the world-space coordinate of the grid's lower corner (`corner`), and the liquid type identifier (`type`).
*   Assigns `width` to `iTilesX` and `height` to `iTilesY`.
*   Stores `corner` in `iCorner` and `type` in `iType`.
*   Allocates memory for `iHeight` (`float*`) and `iFlags` (`uint8*`). The size is `(tilesX + 1) * (tilesY + 1)`, indicating storage for grid vertices rather than tile centers, ensuring continuous coverage.

### Accessors

**`GetType`**
Returns the `iType` member, identifying the liquid kind (e.g., water, lava).

**`GetHeightStorage`**
Returns a pointer to the `iHeight` array, exposing raw height data for direct access by consumers.

**`GetFlagsStorage`**
Returns a pointer to the `iFlags` array, exposing raw flag data indicating liquid validity or presence.

## Cross-Unit Boundaries

`WmoLiquid` does not call out to other units. It is consumed by the `BoundsTrait` subsystem:

*   **Called by `BoundsTrait.WorldModel/GetLiquidType`**: Retrieves the liquid type ID via `GetType()` to determine semantic properties for the virtual map system.
*   **Called by `BoundsTrait.TileAssembler/Read`**: Obtains pointers to raw height and flag arrays via `GetHeightStorage()` and `GetFlagsStorage()` to sample heights and check validity during map tile assembly.
*   **Called by `BoundsTrait.WorldModel/readFromFile#2`**: Instantiates `WmoLiquid` during deserialization of WMO data from disk, passing parsed width, height, corner, and type arguments.

## Data Model

`WmoLiquid` does not interact with any database tables. It manages transient runtime geometry derived from game assets or pre-compiled virtual map files.

## Notable Implementation Details

1.  **Vertex-Based Grid**: The allocation size `(tilesX + 1) * (tilesY + 1)` confirms that `iHeight` and `iFlags` store data for grid vertices. Callers must handle indexing and interpolation (e.g., bilinear) themselves, as `WmoLiquid` provides no high-level sampling methods.
2.  **Manual Memory Management**: The class manually manages `float*` and `uint8*` pointers. The destructor frees this memory. Copy semantics (constructor/assignment) perform deep copies to ensure value safety when instances are moved into `GroupModel` objects.
3.  **Ownership Transfer**: `GroupModel` takes ownership of `WmoLiquid` instances via `setLiquidData`, which nullifies the source pointer. `WmoLiquid` is deleted when its owning `GroupModel` is destroyed.
4.  **Generator-Specific API**: The `#ifdef MMAP_GENERATOR` block exposes `getPosInfo` for tooling that needs to inspect grid parameters during map generation. This is hidden in runtime builds.

## Member Reference

**`GetType`**
Returns the `uint32` liquid type identifier (`iType`). Called by `BoundsTrait.WorldModel/GetLiquidType`.

**`GetHeightStorage`**
Returns a pointer to the `float` array `iHeight`. Called by `BoundsTrait.TileAssembler/Read`.

**`GetFlagsStorage`**
Returns a pointer to the `uint8` array `iFlags`. Called by `BoundsTrait.TileAssembler/Read`.

**`WmoLiquid`**
Constructor initializing grid dimensions, corner position, and type, and allocating height/flags arrays. Called by `BoundsTrait.WorldModel/readFromFile#2`.

---

<!-- machine-true, projected from graph.json -->

## Map — WmoLiquid

*Source:* WorldModel.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GetType | method | — | BoundsTrait.WorldModel/GetLiquidType | — |
| GetHeightStorage | method | — | BoundsTrait.TileAssembler/Read | — |
| GetFlagsStorage | method | — | BoundsTrait.TileAssembler/Read | — |
| WmoLiquid | ctor | — | BoundsTrait.WorldModel/readFromFile#2 | — |
