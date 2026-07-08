# FindCreatureData

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# FindCreatureData

**Purpose & Responsibilities**

`FindCreatureData` is a functor class defined in `ObjectMgr.h` that serves as a search worker for locating specific creature spawn records within the global `CreatureDataMap`. It is designed to be passed to `ObjectMgr::DoCreatureData` (defined in `ObjectMgr`), which iterates over all loaded creature data and invokes this functor for each entry.

The class implements a prioritized search strategy to find the "best" match for a given creature entry ID relative to a specific `Player`. It categorizes potential matches into three tiers based on relevance and proximity:
1.  **Spawned Data:** Creatures that are currently active/spawned in the world (identified by specific flags or states in the `CreatureData` structure).
2.  **Map Data:** Static spawn points located on the same map as the requesting player.
3.  **Any Data:** Any spawn point for the creature ID, regardless of map or activation state, serving as a final fallback.

For each category, the functor tracks the closest instance to the player using Euclidean distance. This allows administrative tools to resolve a creature ID into a concrete location (coordinates and map ID) for actions like teleportation or information retrieval, preferring active or nearby spawns over distant or inactive ones.

## Member-by-Member Behavior

### Construction and Initialization
**`FindCreatureData`**
The constructor initializes the search criteria. It accepts a target creature entry ID (`uint32 id`) and a pointer to the requesting `Player` (`Player* player`). It stores these in private members `i_id` and `i_player`. It also initializes three result pointers (`i_anyData`, `i_mapData`, `i_spawnedData`) to `nullptr` and two distance trackers (`i_mapDist`, `i_spawnedDist`) to `0.0f`. Note that `i_anyData` does not have a corresponding distance tracker, implying it is updated unconditionally or serves as a simple fallback without proximity optimization.

### Search Execution and Result Retrieval
The actual logic for filtering and selecting matches resides in the `operator()` and `GetResult()` methods. While their declarations are in `ObjectMgr.h`, their implementations are in `ObjectMgr.cpp` (not part of this unit). Based on the member variables and standard usage:
*   **`operator()`**: Invoked for each `CreatureDataPair`. It checks if the creature's entry ID matches `i_id`. If it matches, it calculates the distance to `i_player` and updates the appropriate result pointer (`i_spawnedData`, `i_mapData`, or `i_anyData`) if the new instance is closer than the previously recorded best in that category. It returns `true` to stop iteration if a high-priority match (e.g., spawned) is found, or `false` to continue searching for a closer match.
*   **`GetResult()`**: Returns a pointer to the best matching `CreatureDataPair` found. The priority order is typically: `i_spawnedData` > `i_mapData` > `i_anyData`. If no match is found, it returns `nullptr`.

## Cross-Unit Boundaries

### Called By
*   **`ChatHandler.CharacterCommands::HandleLearnAllMyTaxisCommand`**: Uses `FindCreatureData` to locate taxi nodes (represented as creatures) to learn flight paths. It requires the coordinates of the taxi node associated with the creature entry.
*   **`ChatHandler.Chat::ExtractLocationFromLink`**: When a user pastes a creature link, this handler uses `FindCreatureData` to resolve the link to a physical location in the world for debugging or information display.
*   **`ChatHandler.TeleportCommands::HandleGoCreatureCommand`**: Allows an administrator to teleport to a creature. It uses `FindCreatureData` to find the nearest spawn point of the specified creature entry relative to the admin's current position.

### Calls Out
*   **None**: The class itself does not call other units. It operates purely on the data passed to it via `operator()` and the `Player` object stored in its constructor. Distance calculations rely on `Player` methods, but these are internal to the core entity system and not considered cross-unit calls in this architectural context.

## Data Model

**Tables:** None.
`FindCreatureData` operates entirely on in-memory data structures (`CreatureDataMap`) populated by `ObjectMgr` from the database at startup or reload. It does not execute SQL queries or touch database tables directly.

## Notable Implementation Details

1.  **Functor Pattern**: `FindCreatureData` is a classic STL-style functor. It encapsulates search state and logic, allowing `ObjectMgr::DoCreatureData` to iterate over `m_CreatureDataMap` without exposing the internal map structure to callers like `ChatHandler`.
2.  **Priority Logic**: The separation of `i_spawnedData`, `i_mapData`, and `i_anyData` ensures that administrators are directed to the most relevant instance. For example, if a creature is spawned nearby, they are teleported there. If not, but there is a static spawn on the same map, they are directed there. If neither exists, they may be sent to a spawn on a different map.
3.  **Distance Tracking**: The use of `float` members for distance implies Euclidean distance calculations. This ensures accurate proximity sorting among multiple spawn points.
4.  **Thread Safety**: Since `DoCreatureData` is typically called from the main game thread, and `CreatureDataMap` is accessed via const iterators, this functor is generally safe. However, `i_player` is a raw pointer; if the player disconnects during the search (unlikely given the short duration), it could theoretically be a dangling pointer. In practice, the search is fast enough that this is rarely an issue.

## Member Reference

**FindCreatureData**
Constructor that initializes the search functor with a target creature entry ID and a reference `Player` object. Sets up internal pointers for storing the best matches in three categories (spawned, map-local, any) and initializes distance trackers to zero.

---

<!-- machine-true, projected from graph.json -->

## Map — FindCreatureData

*Source:* ObjectMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| FindCreatureData | ctor | — | ChatHandler.CharacterCommands/HandleLearnAllMyTaxisCommand, ChatHandler.Chat/ExtractLocationFromLink, ChatHandler.TeleportCommands/HandleGoCreatureCommand | — |
