# MmapTileHeader

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# MmapTileHeader

**Purpose & Responsibilities**

`MmapTileHeader` is a plain data structure (POD) defined in `MoveMapSharedDefines.h` that serves as the binary file header for MoveMap tile files. MoveMaps are precomputed navigation meshes used by the server to determine valid movement paths, collision detection, and terrain properties for entities in the world.

This structure defines the contract for reading and validating these binary tiles. It stores metadata required to verify file integrity, compatibility with the underlying navigation library (Recast/Detour), and specific feature flags (such as liquid presence) before the actual mesh data is parsed. It is not a class with behavior; it is a memory layout definition.

**Member-by-Member Behavior**

The unit contains a single member: the default constructor.

*   **`MmapTileHeader()`**: Initializes the header fields with known magic numbers and version constants. This ensures that when a new tile is created or when a header object is instantiated in memory, it starts with a valid signature (`MMAP_MAGIC`) and the current expected versions (`DT_NAVMESH_VERSION` and `MMAP_VERSION`). The `size` and `usesLiquids` fields are zeroed out, awaiting population during the file writing process or remaining zero if the file format does not support/use those features.

**Cross-Unit Boundaries**

*   **Called by `MoveMap/loadGameObject` and `MoveMap/loadMap`**: These functions in the `MoveMap` unit are responsible for loading navigation data from disk. They instantiate `MmapTileHeader` objects to read the first bytes of a tile file. By calling the constructor (implicitly via stack allocation or explicit initialization), they prepare a buffer to receive the header data. After reading, they likely check `mmapMagic` against `MMAP_MAGIC` and `dtVersion`/`mmapVersion` against the current constants to ensure the file is valid and compatible with the running server version.

**Data Model**

This unit does not interact with any database tables. It operates exclusively on binary file I/O structures.

**Notable Implementation Details**

*   **Magic Number Validation**: The constant `MMAP_MAGIC` is defined as `0x4d4d4150`, which corresponds to the ASCII string `'MMAP'`. This is a standard technique for identifying file types in binary formats. Any tile file not starting with these four bytes is invalid.
*   **Version Coupling**: The header stores two version numbers:
    1.  `dtVersion`: Tied to `DT_NAVMESH_VERSION` from the Recast/Detour library (`DetourNavMesh.h`). This ensures the binary mesh data is compatible with the specific version of the navigation library linked against the server.
    2.  `mmapVersion`: Tied to `MMAP_VERSION` (currently `6`). This allows the server code to evolve the MoveMap format independently of the underlying Detour library.
*   **Liquid Flag**: The `usesLiquids` field is a boolean-like integer (0 or 1). Its presence indicates whether the tile contains data relevant to liquid interactions (e.g., swimming vs. walking). This allows the loader to potentially skip parsing liquid-specific data if the client or server configuration does not require it, or to validate that the loader supports liquid data.
*   **Size Field**: The `size` field likely represents the total size of the tile data following the header, allowing the loader to allocate the correct amount of memory or seek past the tile efficiently.
*   **NavTerrain Enum**: Although not part of the `MmapTileHeader` struct itself, the `NavTerrain` enum is defined in the same header. It defines bitflags for terrain types (Ground, Magma, Slime, Water, Steep Slopes). These flags are likely stored within the mesh data referenced by the header, influencing how entities interact with the terrain (e.g., taking damage on magma, moving slower in slime).

## Member Reference

**MmapTileHeader**
Default constructor for the `MmapTileHeader` struct. Initializes `mmapMagic` to `MMAP_MAGIC` ('MMAP'), `dtVersion` to the current Detour navigation mesh version, `mmapVersion` to `MMAP_VERSION` (6), `size` to 0, and `usesLiquids` to 0. This provides a baseline valid header state for creating new tiles or preparing buffers for reading existing ones.

---

<!-- machine-true, projected from graph.json -->

## Map — MmapTileHeader

*Source:* MoveMapSharedDefines.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| MmapTileHeader | ctor | — | MoveMap/loadGameObject, MoveMap/loadMap | — |
