<!-- provenance: boundary-bleed -->
# BoundsTrait.TileAssembler

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# BoundsTrait.TileAssembler

## Purpose & Responsibilities

`TileAssembler` is a batch-processing utility within the `VMAP` namespace responsible for converting raw, extracted game world geometry data into optimized, binary spatial acceleration structures (Binary Indexed Hierarchies) and tiled map files. It acts as the bridge between raw asset extraction (processed by a prior extractor into binary intermediate formats) and the runtime collision detection system.

Its primary responsibilities are:
1.  **Parsing Raw Spawns:** Reading a binary directory file (`dir_bin`) that lists all model placements (spawns) in the world, including their positions, rotations, scales, and bounding boxes.
2.  **Coordinate Transformation:** Adjusting coordinates for specific model types (WMO vs. M2) to align with the game's internal coordinate system.
3.  **Spatial Indexing:** Building Binary Indexed Hierarchies (BIH) for efficient ray-casting and point-in-volume checks.
4.  **File Generation:** Writing three types of output files:
    *   `.vmtree`: Global map trees containing non-tiled (global) objects and the root BIH structure.
    *   `.vmtile`: Tiled map files containing local objects specific to a grid cell.
    *   `.vmo`: Converted model files containing mesh data, liquid data, and per-model BIHs.
5.  **Game Object Handling:** Processing a special list of game object models to pre-calculate their bounds for runtime use.

This unit does not perform runtime collision detection; it prepares the static data that the runtime engine (via `MapTree` and `WorldModel`) will load and query.

## Member-by-Member Behavior

### Initialization and Lifecycle

**`TileAssembler`**
The constructor initializes the source directory (`iSrcDir`) and destination directory (`iDestDir`) paths. These paths define where the raw extracted data resides and where the optimized `.vmtree`, `.vmtile`, and `.vmo` files will be written.

**`~TileAssembler`**
The destructor is currently empty. The comment `// delete iCoordModelMapping;` suggests that memory management for a mapping structure was previously handled here or considered, but is currently commented out. The `mapData` members are manually deleted in `convertWorld2`.

### Core Conversion Pipeline

**`convertWorld2`**
This is the main entry point for the conversion process. It orchestrates the entire pipeline:
1.  Calls `readMapSpawns()` to load all model placements into memory.
2.  Iterates through each map ID in `mapData`.
3.  For each map, it processes unique model entries:
    *   If the model is an M2 model (`MOD_M2`), it calls `calculateTransformedBound` to compute its bounding box from raw vertex data, as M2 models often lack pre-calculated bounds in the placement data.
    *   If the model is a World Spawn (`MOD_WORLDSPAWN`), it applies a hardcoded coordinate offset (`533.33333f * 32`) to adjust for differences between WMO/Terrain origins.
4.  Builds a `BIH` (Binary Indexed Hierarchy) tree using `BIH/build` and the `BoundsTrait<ModelSpawn*>::getBounds` functor.
5.  Writes the global map tree (`.vmtree`) file:
    *   Writes magic numbers and metadata.
    *   Writes the BIH nodes via `BIH/writeToFile`.
    *   Writes global spawns (those not associated with a specific tile) via `ModelInstance/writeToFile`.
6.  Writes individual tile files (`.vmtile`) for each grid cell, containing the spawns relevant to that tile.
7.  Calls `exportGameobjectModels()` to handle special game object models.
8.  Iterates through all unique model filenames encountered and calls `convertRawFile` to generate the final `.vmo` model files.
9.  Cleans up dynamically allocated `MapSpawns` objects.

**`readMapSpawns`**
Reads the `dir_bin` file from the source directory. This file contains a stream of binary records representing model spawns. For each record, it:
1.  Reads the map ID, tile X/Y coordinates, and spawn details using `ModelInstance/readFromFile`.
2.  Organizes the spawns into `mapData`, a map of Map IDs to `MapSpawns` structures.
3.  Stores unique spawns in `UniqueEntries` and maps tile IDs to spawn IDs in `TileEntries` using `StaticMapTree/packTileID`.

**`calculateTransformedBound`**
Specifically handles M2 models that lack bounding box data in the spawn record. It:
1.  Loads the raw model file using `WorldModel_Raw/Read`.
2.  Creates a `ModelPosition` object to handle rotation and scale transformations.
3.  Iterates through the model's vertex array, transforming each vertex using `ModelPosition/transform`.
4.  Computes the Axis-Aligned Bounding Box (AABB) from the transformed vertices.
5.  Updates the `ModelSpawn`'s bound and sets the `MOD_HAS_BOUND` flag.

**`exportGameobjectModels`**
Processes a file named `GAMEOBJECT_MODELS` (defined elsewhere) which lists special game object models. It:
1.  Reads the list of models from the source directory.
2.  For each model, loads the raw data using `WorldModel_Raw/Read`.
3.  Calculates the AABB from the raw vertices.
4.  Writes a new binary file to the destination directory containing the display ID, name, and calculated bounds. This allows the runtime to quickly check if a game object is within a certain area without loading the full model.

**`convertRawFile`**
Converts a single raw model file into the optimized `.vmo` format. It:
1.  Loads the raw model using `WorldModel_Raw/Read`.
2.  Creates a `WorldModel` object.
3.  Sets the root WMO ID.
4.  Iterates through the raw groups, creating `GroupModel` objects.
5.  Populates each `GroupModel` with mesh data (`setMeshData`) and liquid data (`setLiquidData`).
6.  Sets the group models on the `WorldModel` using `setGroupModels`, which triggers the building of the group-level BIH.
7.  Writes the final model to disk using `WorldModel/writeFile`.

### Helper Structures and Methods

**`transform`**
A method of `ModelPosition` that applies scaling and rotation to a 3D vector. It scales the input vector by `iScale`, then rotates it by the matrix `iRotation` (which is initialized from Euler angles in `init`).

**`getBounds`**
A static template specialization for `BoundsTrait<VMAP::ModelSpawn*>`. It retrieves the bounding box from a `ModelSpawn` object. This functor is passed to `BIH/build` to allow the BIH builder to access bounds for any spawn type.

**`readChunk`**
A utility function that reads a fixed number of bytes from a file pointer and compares them to an expected string. Used extensively in parsing binary files to verify chunk headers (e.g., "NODE", "GOBJ", "VERT").

**`Read`**
A method of `GroupModel_Raw` that parses a raw group model chunk from a file. It reads flags, bounds, liquid data, branches, indices, vertices, and liquid height/flag grids. It constructs `MeshTriangle` objects from index arrays and stores vertices in a `Vector3` array. It uses macros `READ_OR_RETURN` and `CMP_OR_RETURN` for error handling.

**`Read#2`**
A method of `WorldModel_Raw` that parses a raw world model file. It verifies the magic number, skips a temporary vector count, reads the number of groups and root WMO ID, and then iteratively calls `GroupModel_Raw/Read` for each group.

**`~GroupModel_Raw`**
Destructor for `GroupModel_Raw`. It deletes the dynamically allocated `WmoLiquid` object if present.

## Cross-Unit Boundaries

### Calls Out

*   **`BIH/BIH`, `BIH/writeToFile`**: `convertWorld2` uses the `BIH` class to build and serialize the spatial acceleration structure for the map. The BIH allows for efficient ray casting and point queries.
*   **`ModelInstance/writeToFile`**: Used in `convertWorld2` to write spawn data to both `.vmtree` and `.vmtile` files.
*   **`StaticMapTree/packTileID`, `StaticMapTree/unpackTileID`**: Used to encode/decode tile coordinates (X, Y) into/from a single 32-bit integer for storage and lookup in `TileEntries`.
*   **`ModelSpawn/readFromFile`**: Called by `readMapSpawns` to deserialize spawn data from the `dir_bin` file.
*   **`ModelPosition/init`**: Called by `calculateTransformedBound` to initialize the rotation matrix from Euler angles.
*   **`BoundsTrait.WorldModel/setGroupModels`, `BoundsTrait.WorldModel/setMeshData`, `BoundsTrait.WorldModel/writeFile`**: Called by `convertRawFile` to populate and serialize the final `WorldModel` object. Note: The MAP lists these as `BoundsTrait.WorldModel`, but the source shows they are methods of `WorldModel` itself. The MAP likely refers to the `BoundsTrait` specialization used elsewhere, but the calls in `convertRawFile` are direct method calls on `WorldModel`.
*   **`GroupModel/GroupModel#2`, `GroupModel/setLiquidData`**: Called by `convertRawFile` to construct `GroupModel` objects from raw data.
*   **`WorldModel/setRootWmoID`, `WorldModel/WorldModel`**: Called by `convertRawFile` to initialize the `WorldModel` object.
*   **`BoundsTrait.WorldModel/WmoLiquid#2`, `MeshTriangle/MeshTriangle#2`, `WmoLiquid/GetFlagsStorage`, `WmoLiquid/GetHeightStorage`**: Called by `GroupModel_Raw/Read` to parse liquid data chunks.

### Called By

*   **`BoundsTrait.WorldModel/readFile`, `BoundsTrait.WorldModel/readFromFile`**: These likely refer to `WorldModel/readFile` and `GroupModel/readFromFile` which use `readChunk` to parse binary data.
*   **`MapTree/CanLoadMap`, `MapTree/InitMap`, `MapTree/LoadMapTile`, `MapTree/UnloadMapTile`**: The `MapTree` unit uses `readChunk` to validate and parse the `.vmtree` and `.vmtile` files generated by `TileAssembler`. This confirms that `TileAssembler` produces the input format for `MapTree`.

## Data Model

This unit does not interact with any SQL database tables. It operates entirely on binary files extracted from game assets and generates new binary files for runtime consumption.

## Notable Implementation Details

1.  **Hardcoded Coordinate Offset**: In `convertWorld2`, there is a hardcoded offset `Vector3(533.33333f * 32, 533.33333f * 32, 0.f)` applied to `MOD_WORLDSPAWN` models. The comment indicates this is due to different origins for WMO maps and terrain maps. This is a fragile workaround ("TODO: remove extractor hack") that assumes a specific coordinate system alignment.

2.  **M2 Model Bounds Calculation**: M2 models are treated specially because they often lack bounding box data in the placement records. `calculateTransformedBound` loads the entire raw model file just to compute the bounds. This is computationally expensive but necessary for accurate collision detection. The code warns if an M2 model has multiple groups, suggesting it expects only one.

3.  **Liquid Data Parsing**: `GroupModel_Raw/Read` parses complex liquid data structures, including height grids and flag grids. It uses a custom `WMOLiquidHeader` struct to interpret the binary layout. The liquid data is stored in `WmoLiquid` objects, which are later serialized into the `.vmo` files.

4.  **Error Handling Macros**: The `READ_OR_RETURN` and `CMP_OR_RETURN` macros simplify error handling in the binary parsing code. They close the file and return `false` if a read fails or a chunk identifier doesn't match. This ensures that partial or corrupted files are rejected early.

5.  **Memory Management**: `convertWorld2` manually deletes `MapSpawns` objects after processing. This is unusual for a class that manages its own data members, suggesting that `mapData` is populated temporarily and then discarded. The destructor does not clean up `mapData`, relying on this manual cleanup.

6.  **Tile ID Packing**: The use of `StaticMapTree/packTileID` and `unpackTileID` allows for efficient storage of tile coordinates in a single integer. This is crucial for the `TileMap` multimap, which uses the packed ID as the key.

7.  **Game Object Model Export**: `exportGameobjectModels` creates a separate binary file for game object models. This file contains pre-calculated bounds, allowing the runtime to quickly determine if a game object is within a certain area without loading the full model geometry. This is an optimization for performance.

## Member Reference

**`getBounds`**: Static template specialization for `BoundsTrait<VMAP::ModelSpawn*>`. Retrieves the bounding box from a `ModelSpawn` object. Used by `BIH/build` to construct the spatial index.

**`readChunk`**: Utility function that reads a fixed number of bytes from a file pointer and compares them to an expected string. Used to verify chunk headers in binary files.

**`transform`**: Method of `ModelPosition`. Applies scaling and rotation to a 3D vector. Used to transform vertices from model space to world space.

**`TileAssembler`**: Constructor. Initializes source and destination directory paths.

**`~TileAssembler`**: Destructor. Currently empty. Manual cleanup of `mapData` occurs in `convertWorld2`.

**`convertWorld2`**: Main entry point. Orchestrates the conversion of raw spawn data into `.vmtree`, `.vmtile`, and `.vmo` files. Handles coordinate adjustments, BIH construction, and file serialization.

**`readMapSpawns`**: Reads the `dir_bin` file and populates `mapData` with model spawns organized by map ID and tile coordinates.

**`calculateTransformedBound`**: Computes the bounding box for M2 models by loading the raw model file, transforming vertices, and calculating the AABB.

**`convertRawFile`**: Converts a single raw model file into the optimized `.vmo` format. Populates `WorldModel` and `GroupModel` objects with mesh and liquid data, then serializes them.

**`exportGameobjectModels`**: Processes a list of game object models, calculates their bounds, and writes a binary file containing display IDs, names, and bounds for runtime optimization.

**`Read`**: Method of `GroupModel_Raw`. Parses a raw group model chunk from a file. Reads flags, bounds, liquid data, branches, indices, vertices, and liquid height/flag grids.

**`~GroupModel_Raw`**: Destructor. Deletes the dynamically allocated `WmoLiquid` object.

**`Read#2`**: Method of `WorldModel_Raw`. Parses a raw world model file. Verifies magic number, reads group count and root WMO ID, and calls `GroupModel_Raw/Read` for each group.

---

<!-- machine-true, projected from graph.json -->

## Map — BoundsTrait.TileAssembler

*Source:* TileAssembler.cpp, TileAssembler.h, WorldModel.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| getBounds | method | ModelSpawn/getBounds | — | — |
| readChunk | function | — | BoundsTrait.WorldModel/readFile, BoundsTrait.WorldModel/readFromFile, MapTree/CanLoadMap, MapTree/InitMap, MapTree/LoadMapTile, MapTree/UnloadMapTile | — |
| transform | method | — | — | — |
| TileAssembler | ctor | — | — | — |
| ~TileAssembler | dtor | — | — | — |
| convertWorld2 | method | BIH/BIH, BIH/writeToFile, ModelInstance/writeToFile, StaticMapTree/packTileID, StaticMapTree/unpackTileID | — | — |
| readMapSpawns | method | ModelInstance/readFromFile, StaticMapTree/packTileID | — | — |
| calculateTransformedBound | method | ModelPosition/init | — | — |
| convertRawFile | method | BoundsTrait.WorldModel/setGroupModels, BoundsTrait.WorldModel/setMeshData, BoundsTrait.WorldModel/writeFile, GroupModel/GroupModel#2, GroupModel/setLiquidData, WorldModel/setRootWmoID, WorldModel/WorldModel | — | — |
| exportGameobjectModels | method | — | — | — |
| Read | method | BoundsTrait.WorldModel/WmoLiquid#2, MeshTriangle/MeshTriangle#2, WmoLiquid/GetFlagsStorage, WmoLiquid/GetHeightStorage | — | — |
| ~GroupModel_Raw | dtor | — | — | — |
| Read#2 | method | — | — | — |

---

<!-- verify: boundary-bleed | foreign: contains, GroupModel, remove, WmoLiquid -->
