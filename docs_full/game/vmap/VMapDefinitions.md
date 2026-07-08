<!-- provenance: failed-members -->
# VMapDefinitions

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# VMapDefinitions

**VMapDefinitions** is a header-only unit that defines the fundamental constants, file format signatures, and logging macros for the Virtual Map (VMap) subsystem. It contains no executable logic of its own but establishes the shared context required by the VMap loader and generator tools.

## Purpose & Responsibilities

The unit serves three primary purposes:
1.  **File Format Identification:** It defines the magic byte sequences (`VMAP_MAGIC`, `RAW_VMAP_MAGIC`) used to validate VMap binary files.
2.  **Spatial Constants:** It provides `LIQUID_TILE_SIZE`, a constant for calculating liquid heightmap tile dimensions.
3.  **Build-Specific Logging:** It implements conditional macros (`DEBUG_FILTER_LOG`, `MANGOS_ASSERT`) that adapt logging and assertion behavior based on whether the code is compiled for the core server, the map generator, or a standalone environment.

## Member-by-Member Behavior

This unit defines no functions or methods. Its members are constants and macros exposed via the `VMAP` namespace or globally.

*   **Constants:** `VMAP_MAGIC` ("VMAP_7.0") and `RAW_VMAP_MAGIC` ("VMAPs05") identify compiled and raw VMap files, respectively. `GAMEOBJECT_MODELS` ("temp_gameobject_models") is a string identifier for temporary game object model data. `LIQUID_TILE_SIZE` calculates the size of a liquid tile as `533.333f / 128.f`.
*   **Macros:** `MANGOS_ASSERT` wraps `assert()`. `DEBUG_FILTER_LOG` conditionally outputs debug messages, routing to `sLog.Out` in the core server or `printf` in generator/standalone builds, controlled by a filter flag.
*   **Declarations:** `readChunk` is declared in the `VMAP` namespace but implemented in `TileAssembler.cpp`. It reads and validates file chunks.

## Cross-Unit Boundaries

*   **Calls Out:**
    *   **`TileAssembler.cpp`**: The declaration of `readChunk` links to this unit for implementation.
    *   **`Log.h`**: In core server builds, `DEBUG_FILTER_LOG` calls `sLog.Out`.
    *   **Standard Library**: In non-core builds, macros use `printf` and `assert`.
*   **Called By:**
    *   No other units are listed as callers in the MAP.

## Data Model

This unit does not interact with database tables.

## Notable Implementation Details

*   **Conditional Compilation:** The header uses `#ifndef NO_CORE_FUNCS`, `#elif defined MMAP_GENERATOR`, and `#else` to define logging macros differently for the core server, map generator, and other builds. Maintainers must preserve these branches to ensure debug output works in all environments.
*   **Forward Declaration:** `readChunk` is declared here to reduce include dependencies, relying on linking with `TileAssembler.cpp`.

## Member Reference

*   **LIQUID_TILE_SIZE**: A float constant defining the size of a liquid heightmap tile, calculated as `533.333f / 128.f`.
*   **VMAP_MAGIC**: A char array "VMAP_7.0" serving as the file signature for compiled VMap files.
*   **RAW_VMAP_MAGIC**: A char array "VMAPs05" serving as the file signature for raw VMap data files.
*   **GAMEOBJECT_MODELS**: A char array "temp_gameobject_models" likely used as a directory or identifier for temporary game object models.
*   **readChunk**: A declared function (implemented in TileAssembler.cpp) that reads and validates a chunk of data from a file.
*   **MANGOS_ASSERT**: A macro wrapping assert() for consistent assertion handling across builds.
*   **DEBUG_FILTER_LOG**: A macro that conditionally logs debug messages using sLog.Out (core) or printf (generator/standalone), respecting a filter flag.

---

<!-- machine-true, projected from graph.json -->

## Map — VMapDefinitions

*Source:* VMapDefinitions.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|

---

<!-- verify: failed-members | invented: DEBUG_FILTER_LOG, GAMEOBJECT_MODELS, LIQUID_TILE_SIZE, MANGOS_ASSERT, RAW_VMAP_MAGIC, readChunk, VMAP_MAGIC -->
