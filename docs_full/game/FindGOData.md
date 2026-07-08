# FindGOData

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# FindGOData

**Purpose & Responsibilities**

`FindGOData` is a functor class defined in `ObjectMgr.h` that encapsulates the logic for locating specific `GameObject` spawn data within the `ObjectMgr`'s internal storage. It is designed to be used with the `ObjectMgr::DoGOData` template method, which iterates over the `m_GameObjectDataMap`.

The class does not perform the search independently; instead, it holds the search criteria (a target GameObject entry ID and a reference `Player`) and accumulates the best match found during iteration. It distinguishes between different types of data matches (any data, map-specific data, and spawned data) and tracks distances to prioritize the closest instance relative to the player. This allows command handlers to resolve a generic GameObject entry ID into a specific, spatially relevant spawn record for actions like teleportation or inspection.

**Member-by-Member Behavior**

### Construction and State Initialization
*   **`FindGOData(uint32 id, Player* player)`**: The constructor initializes the functor's state. It stores the target GameObject entry ID in `i_id` and the pointer to the requesting `Player` in `i_player`. It initializes four internal result pointers (`i_anyData`, `i_mapData`, `i_spawnedData`) to `nullptr` and two floating-point distance trackers (`i_mapDist`, `i_spawnedDist`) to `0.0f`. These members serve as the output buffer for the search results.

### Search Logic
*   **`operator()(GameObjectDataPair const& dataPair)`**: This function is invoked by `ObjectMgr::DoGOData` for each entry in the `m_GameObjectDataMap`. Although the implementation body is not present in this header file, the member variables indicate its behavior:
    1.  It compares the `dataPair`'s GameObject entry against the stored `i_id`.
    2.  If there is a match, it calculates the distance between the `Player`'s current position and the GameObject's position.
    3.  It updates the internal pointers (`i_anyData`, `i_mapData`, `i_spawnedData`) if the current match is closer than previously recorded distances or fits a higher-priority category (e.g., spawned vs. static).
    4.  It returns a boolean value indicating whether the iteration should continue. Typically, this returns `false` to allow the full map to be scanned for the absolute closest match, ensuring accuracy for commands like `.go object`.

### Result Retrieval
*   **`GetResult() const`**: After the iteration completes, this method returns a constant pointer to the best `GameObjectDataPair` found. The caller uses this pointer to access the detailed spawn information (coordinates, orientation, phase, etc.) of the located GameObject.

**Cross-Unit Boundaries**

*   **Called By**:
    *   `ChatHandler.Chat/ExtractLocationFromLink` (`ChatHandler.cpp`): Uses `FindGOData` to resolve a GameObject hyperlink (containing an entry ID) into a physical location for display or processing in chat commands.
    *   `ChatHandler.TeleportCommands/HandleGoObjectCommand` (`ChatHandler.cpp`): Uses `FindGOData` to find the nearest instance of a specified GameObject entry ID to teleport the player to.
*   **Calls Out**: None. `FindGOData` is a stateful functor. It accesses public members of the `Player` class (passed in the constructor) and the `GameObjectData` struct (from the `dataPair` argument) but does not invoke methods on other units.

**Data Model**

This unit does not interact directly with database tables. It operates entirely on the in-memory `m_GameObjectDataMap` maintained by `ObjectMgr`. This map is populated from database tables such as `gameobject` and `gameobject_respawn` during server initialization or dynamic reloads.

**Notable Implementation Details**

1.  **Functor Pattern Consistency**: `FindGOData` mirrors the design of `FindCreatureData` (also in `ObjectMgr.h`). This parallel structure allows `ObjectMgr` to use generic template methods (`DoCreatureData` and `DoGOData`) for searching different entity types without code duplication.
2.  **Distance-Based Prioritization**: The presence of `i_mapDist` and `i_spawnedDist` indicates that the search logic prioritizes proximity. This is essential for user-facing commands where multiple instances of the same GameObject entry may exist in the world, and the "nearest" one is the intended target.
3.  **Full Iteration**: The functor likely iterates through the entire `m_GameObjectDataMap` rather than stopping at the first match. This ensures that if a closer instance exists later in the unordered map, it will be selected.
4.  **Thread Safety Assumptions**: As a functor used in iteration, it assumes the underlying `m_GameObjectDataMap` is stable during the search. Callers must ensure appropriate locking or synchronization if concurrent modifications are possible, though `ObjectMgr` typically manages this via its own internal locks for grid operations.

## Member Reference

**FindGOData**
Constructor that initializes the search context with a target GameObject entry ID (`i_id`) and a `Player` pointer (`i_player`). Sets internal result pointers (`i_anyData`, `i_mapData`, `i_spawnedData`) to `nullptr` and distance trackers (`i_mapDist`, `i_spawnedDist`) to `0.0f`.

---

<!-- machine-true, projected from graph.json -->

## Map — FindGOData

*Source:* ObjectMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| FindGOData | ctor | — | ChatHandler.Chat/ExtractLocationFromLink, ChatHandler.TeleportCommands/HandleGoObjectCommand | — |
