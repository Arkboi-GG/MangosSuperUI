<!-- provenance: verbose -->
# VMapFactory

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# VMapFactory

## Purpose & Responsibilities

`VMapFactory` is a static utility class in the `VMAP` namespace that manages the lifecycle of a single global `IVMapManager` instance. It provides a centralized access point (`createOrGetVMapManager`) for retrieving the virtual map manager singleton, ensuring that the heavy initialization of the `VMapManager2` object occurs only once. It also provides a cleanup mechanism (`clear`) to destroy this singleton during server shutdown.

The unit additionally defines two free functions, `chompAndTrim` and `getNextId`, which serve as string parsing utilities for processing configuration or data-loading strings.

## Member-by-Member Behavior

### Lifecycle Management

**`createOrGetVMapManager`**
This static method implements a lazy-initialization pattern for the global `IVMapManager` pointer `gVMapManager`.
1. It checks if `gVMapManager` is `nullptr`.
2. If null, it allocates a new `VMapManager2` object on the heap and assigns it to `gVMapManager`. The source code comments that this instantiation "should be taken from config," implying potential future configurability, but currently hardcodes `VMapManager2`.
3. It returns the pointer to the manager.

This method is the central hub for accessing virtual map data. As indicated by the MAP, it is called by numerous subsystems including `GameObjectModel`, `GridMap`, `Map.Main`, `MoveMap`, `Spell.Main`, and `World`. This confirms that `VMapFactory` acts as the gateway for collision detection, line-of-sight calculations, height queries, and liquid status checks throughout the server.

**`clear`**
This static method handles the destruction of the singleton.
1. It deletes the object pointed to by `gVMapManager`.
2. It sets `gVMapManager` back to `nullptr`.

This method is called by `World::~World` during server shutdown, ensuring that the memory allocated for the virtual map manager is released cleanly.

### String Utilities

**`chompAndTrim`**
A free function that modifies a `std::string` in-place by removing trailing and leading whitespace and quote characters.
1. **Trailing:** It iterates from the end of the string backwards. If the last character is `\r`, `\n`, space, `"`, or `'`, it removes it using `substr`. It stops when it encounters a character not in this set.
2. **Leading:** It iterates from the beginning of the string forwards. If the first character is space, `"`, or `'`, it removes it. It stops when it encounters a character not in this set.

Note that `\r` and `\n` are only trimmed from the *end*, not the beginning. Quotes are trimmed from both ends.

**`getNextId`**
A free function designed to parse comma-separated integer IDs from a string.
1. It searches for the next comma starting from `pStartPos`.
2. If a comma is found after `pStartPos`, it extracts the substring between `pStartPos` and the comma.
3. It updates `pStartPos` to the position after the comma.
4. It calls `chompAndTrim` on the extracted substring to clean it.
5. It converts the cleaned string to an integer using `atoi` and stores it in `pId`.
6. It returns `true` if an ID was successfully parsed, otherwise `false`.

This function is typically used in loops to process lists of IDs, such as map IDs or zone IDs, stored in configuration strings.

## Cross-Unit Boundaries

### Calls Out

**`VMapManager2`**
`createOrGetVMapManager` instantiates `VMapManager2` (defined in `VMapManager2.cpp`). This is the core implementation of the virtual map system. By creating this object, `VMapFactory` delegates all complex geometry processing, loading, and querying logic to `VMapManager2`.

### Called By

The `createOrGetVMapManager` method is heavily utilized across the codebase, indicating that almost any component requiring spatial awareness interacts with `VMapFactory`:

*   **`GridMap`**: Multiple methods (`CleanUpGrids`, `ExistVMap`, `GetAreaInfo`, `GetHeightStatic`, `getLiquidStatus#2`, `LoadMapAndVMap`, `~TerrainInfo`) call `createOrGetVMapManager`. This suggests that `GridMap` relies on the factory to ensure the VMap manager is available before performing grid-specific terrain queries.
*   **`Map.Main`**: Methods like `FindCollisionModel`, `GetLosHitPosition`, and `isInLineOfSight` use the factory to access the manager for raycasting and collision checks.
*   **`GameObjectModel`**: `initialize` calls the factory, likely to register game objects with the virtual map system.
*   **`MoveMap`**: `loadMap` uses the factory, suggesting that movement pathfinding depends on virtual map data.
*   **`Spell.Main`**: `CheckCast` uses the factory, implying that spell targeting and line-of-sight validation rely on virtual maps.
*   **`World`**: `LoadConfigSettings` calls the factory, possibly to initialize the manager early in the startup sequence or to verify its availability.

## Data Model

This unit does not interact with any database tables. It operates entirely in memory, managing a singleton object and providing string parsing utilities.

## Notable Implementation Details

1.  **Thread Safety**: The `createOrGetVMapManager` method is **not thread-safe**. It performs a check-then-act sequence (`if (!gVMapManager) ... new VMapManager2()`) without any locking mechanisms. In a multi-threaded environment, if two threads call this method simultaneously when `gVMapManager` is null, both may attempt to allocate a new `VMapManager2`, leading to a race condition where one allocation is leaked and the final value of `gVMapManager` is non-deterministic. However, since `World::LoadConfigSettings` likely calls this during single-threaded startup, and subsequent calls are reads, this may be acceptable in practice if the manager is initialized before worker threads start.
2.  **Hardcoded Type**: The comment in `createOrGetVMapManager` explicitly states that the type `VMapManager2` "should be taken from config." Currently, it is hardcoded. This limits flexibility if alternative VMap implementations were to be introduced.
3.  **String Parsing Efficiency**: `chompAndTrim` creates new string objects via `substr` for every character removed. For long strings with many leading/trailing spaces, this results in repeated memory allocations. A more efficient approach would use iterators or `erase` with indices. Similarly, `getNextId` uses `atoi`, which does not handle errors gracefully (e.g., non-numeric input results in 0), but this is consistent with typical legacy C++ parsing patterns.
4.  **Global State**: The use of a global variable `gVMapManager` makes testing difficult and couples the entire server to this specific singleton. The `clear` method ensures cleanup, but any code holding a raw pointer to the manager after `clear` is called will have a dangling pointer.

## Member Reference

**`chompAndTrim`**
Free function that trims leading and trailing whitespace, quotes (`"`, `'`), and newline/carriage return characters (only from the end) from a `std::string`. It modifies the string in-place.

**`getNextId`**
Free function that parses the next comma-separated integer ID from a string. It takes a reference to the string, a start position, and an output ID. It updates the start position to after the comma, cleans the substring using `chompAndTrim`, converts it to an integer via `atoi`, and returns `true` if successful.

**`createOrGetVMapManager`**
Static method of `VMapFactory` that returns a pointer to the global `IVMapManager` singleton. If the singleton does not exist, it creates a new `VMapManager2` instance. This is the primary entry point for accessing virtual map data across the server.

**`clear`**
Static method of `VMapFactory` that deletes the global `IVMapManager` singleton and sets the pointer to `nullptr`. Used during server shutdown to free resources.

---

<!-- machine-true, projected from graph.json -->

## Map — VMapFactory

*Source:* VMapFactory.cpp, VMapFactory.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| chompAndTrim | function | — | — | — |
| getNextId | function | — | — | — |
| createOrGetVMapManager | method | VMapManager2/VMapManager2 | GameObjectModel/initialize, GridMap/CleanUpGrids, GridMap/ExistVMap, GridMap/GetAreaInfo, GridMap/GetHeightStatic, GridMap/getLiquidStatus#2, GridMap/LoadMapAndVMap, GridMap/~TerrainInfo, Map.Main/FindCollisionModel, Map.Main/GetLosHitPosition, Map.Main/isInLineOfSight, MoveMap/loadMap, Spell.Main/CheckCast, World/LoadConfigSettings | — |
| clear | method | — | World/~World | — |
