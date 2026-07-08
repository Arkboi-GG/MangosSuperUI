# BattleGroundMap

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# BattleGroundMap

**Purpose & Responsibilities**

`BattleGroundMap` is a specialized subclass of `Map` designed to manage the spatial, object, and player state for World of Warcraft battleground instances (e.g., Warsong Gulch, Arathi Basin). While the base `Map` class handles general world geometry, grid loading, and object tracking, `BattleGroundMap` provides the critical integration layer between the spatial map system and the high-level `BattleGround` game logic.

Its primary responsibilities are:
1.  **Context Provision:** It holds a pointer to the active `BattleGround` instance (`m_bg`). This allows any code running within the map's context (such as creature AI, spell effects, or grid updates) to access the specific rules, teams, and state of the ongoing battleground match via the `GetBG()` method.
2.  **Lifecycle Management:** It overrides standard map entry/exit and update behaviors to enforce battleground-specific constraints, such as preventing unauthorized entry (`CanEnter`) and handling the unique cleanup required when a battleground ends (`UnloadAll`, `SetUnload`).
3.  **Visibility Tuning:** It overrides `InitVisibilityDistance` to configure the range at which players and objects become visible to each other, ensuring performance and gameplay consistency appropriate for large-scale PvP zones.

Unlike dungeons or raids, battlegrounds are typically temporary, instanced arenas where the map's existence is tightly coupled to the lifecycle of the `BattleGround` object itself.

## Member-by-Member Behavior

The `BattleGroundMap` class defines two public accessor methods and several overridden virtual functions inherited from `Map`.

### Context Accessors

*   **`GetBG`**: Returns the `BattleGround*` pointer stored in `m_bg`. This is the primary interface for retrieving the logical battleground state from within the spatial map context. It is called extensively by various subsystems (as detailed in the Cross-Unit Boundaries section) to determine team affiliations, score states, and event triggers.
*   **`SetBG`**: Assigns a `BattleGround*` pointer to the internal `m_bg` member. This establishes the link between the map instance and the game logic instance. It is called during map creation and destruction to bind and unbind the relationship.

### Lifecycle Overrides

*   **`Update`**: Overrides the base `Map::Update` method. While the base class handles grid updates and object movement, this override likely ensures that battleground-specific timers or state checks are integrated into the map's tick cycle.
*   **`Add`**: Overrides `Map::Add`. Handles the addition of a `Player` to the battleground map. This likely involves initializing the player's team state relative to the `BattleGround` instance and ensuring they are properly registered with the battleground's player list.
*   **`Remove`**: Overrides `Map::Remove`. Handles the removal of a `Player` from the battleground map. This ensures that when a player leaves (via disconnect, death, or manual exit), their state is cleaned up from both the spatial map and the logical `BattleGround` instance.
*   **`CanEnter`**: Overrides `Map::CanEnter`. Determines whether a `Player` is allowed to enter this specific battleground map. This check likely validates if the player is queued for, or currently participating in, the specific `BattleGround` instance associated with this map.
*   **`SetUnload`**: A battleground-specific method to signal that the map should begin its unload process. This is likely called when the battleground ends or times out.
*   **`UnloadAll`**: Overrides `Map::UnloadAll`. Forces the complete unloading of the map's grids and objects. In the context of a battleground, this ensures all temporary creatures, game objects, and players are removed and the map memory is freed after the match concludes.
*   **`InitVisibilityDistance`**: Overrides `Map::InitVisibilityDistance`. Configures the `m_visibilityDistance` and `m_gridActivationDistance` for the battleground. Battlegrounds often require larger visibility ranges than standard zones to accommodate large-scale combat, but may also need optimization to prevent excessive network traffic.

### Persistence

*   **`GetPersistanceState`**: Returns a `BattleGroundPersistentState*`. This provides access to data that must survive map reloads or crashes, such as the current state of the battleground match, though battlegrounds are typically ephemeral.

## Cross-Unit Boundaries

`BattleGroundMap` acts as a bridge between the low-level spatial engine (`Map`) and the high-level game logic (`BattleGround`). The `GetBG` method is the central point of interaction, allowing numerous other units to retrieve the `BattleGround` instance.

### Callers of `GetBG`

The following units call `BattleGroundMap::GetBG` to access the battleground context:

1.  **`CreatureGroups/Respawn`**: Likely uses the battleground context to determine respawn locations or team-specific spawn points for creature groups within the battleground.
2.  **`GridNotifiers/operator()#2` and `operator()#3`**: These grid notification handlers likely use the battleground context to filter or process events occurring in specific grids, possibly related to team-based visibility or event triggering.
3.  **`Map.Main/Update`**: The main map update loop calls `GetBG` to ensure the battleground logic is synchronized with the spatial updates.
4.  **`ThreatListCopier.battleground_alterac`**: This unit contains multiple methods interacting with `GetBG`:
    *   `battleground_alterac/av_world_boss_baseai`: Uses the battleground context for world boss AI logic.
    *   `checkAerialStatus`, `checkCavalryStatus`, `checkTroopsStatus`: These methods likely query the battleground state to determine the status of specific resources or objectives (e.g., aerial superiority, cavalry control, troop levels) in Alterac Valley.
    *   `GossipHello_npc_AVBlood_collector`: Uses the context to handle gossip interactions with the blood collector NPC, likely providing information about the current state of the battle.
    *   `JustDied` and `JustDied#3`: Handle creature death events, likely updating scores or triggering events based on the battleground state.
    *   `JustRespawned#3`: Handles creature respawn events, ensuring they respawn correctly according to battleground rules.
    *   `SelectCreatureEntry`: Likely selects appropriate creature entries for spawning based on the current battleground phase or team needs.
    *   `UpdateAI#9`: Updates the AI of creatures, potentially adjusting behavior based on the overall battleground situation.

### Callers of `SetBG`

1.  **`game_Battlegrounds_BattleGround/~BattleGround`**: The destructor of the `BattleGround` class calls `SetBG` (likely passing `nullptr` or cleaning up the reference) to sever the link between the battleground logic and the map instance when the battleground is destroyed.
2.  **`MapManager/CreateBattleGroundMap`**: The map manager calls `SetBG` during the creation of a new battleground map to establish the initial link between the newly created `BattleGroundMap` instance and the corresponding `BattleGround` logic instance.

## Data Model

This unit does not directly interact with any database tables. It operates entirely on in-memory objects (`BattleGround`, `Player`, `Creature`, etc.) and spatial data structures. The `BattleGroundPersistentState` may involve database interactions, but those are handled by the persistence layer, not directly by `BattleGroundMap`.

## Notable Implementation Details

*   **Pointer Ownership**: `BattleGroundMap` holds a raw pointer (`BattleGround* m_bg`) to the `BattleGround` instance. It does not own the `BattleGround` object; ownership remains with the `BattleGroundMgr` or similar higher-level manager. Care must be taken to ensure the `BattleGround` object outlives the `BattleGroundMap` or that the pointer is nulled appropriately during destruction to avoid dangling pointers.
*   **Virtual Overrides**: All lifecycle methods (`Update`, `Add`, `Remove`, `CanEnter`, `UnloadAll`) are overridden. This indicates that battlegrounds have distinct requirements for player management and map updates compared to standard world maps or dungeons. For example, `CanEnter` is crucial for preventing players from entering a battleground they are not assigned to.
*   **Visibility Configuration**: The override of `InitVisibilityDistance` suggests that battlegrounds have specific tuning for how far players can see. This is critical for performance in large-scale PvP zones where too many visible objects can cause lag.
*   **Alterac Valley Specifics**: The heavy usage of `GetBG` by `ThreatListCopier.battleground_alterac` methods indicates that Alterac Valley has complex, dynamic logic tied closely to the map's spatial context. This includes checking statuses of various military units (aerial, cavalry, troops) and handling specific NPC interactions.

## Member Reference

**GetBG**
Returns the `BattleGround*` pointer associated with this map. Used by various subsystems to access the logical state of the battleground.

**SetBG**
Sets the `BattleGround*` pointer associated with this map. Called during map creation and destruction to bind/unbind the battleground logic.

---

<!-- machine-true, projected from graph.json -->

## Map — BattleGroundMap

*Source:* Map.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GetBG | method | — | CreatureGroups/Respawn, GridNotifiers/operator()#2, GridNotifiers/operator()#3, Map.Main/Update, ThreatListCopier.battleground_alterac/av_world_boss_baseai, ThreatListCopier.battleground_alterac/checkAerialStatus, ThreatListCopier.battleground_alterac/checkCavalryStatus, ThreatListCopier.battleground_alterac/checkTroopsStatus, ThreatListCopier.battleground_alterac/GossipHello_npc_AVBlood_collector, ThreatListCopier.battleground_alterac/JustDied, ThreatListCopier.battleground_alterac/JustDied#3, ThreatListCopier.battleground_alterac/JustRespawned#3, ThreatListCopier.battleground_alterac/SelectCreatureEntry, ThreatListCopier.battleground_alterac/UpdateAI#9 | — |
| SetBG | method | — | game_Battlegrounds_BattleGround/~BattleGround, MapManager/CreateBattleGroundMap | — |
