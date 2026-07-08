# DungeonPersistentState

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# DungeonPersistentState

**Purpose & Responsibilities**

`DungeonPersistentState` is a specialized subclass of `MapPersistentState` responsible for managing the runtime state of **instanceable dungeons** (as opposed to world maps or battlegrounds). Its primary role is to track which players and groups are currently bound to a specific dungeon instance, determine when that instance is eligible for resetting, and manage the lifecycle of the instance data in memory relative to player presence.

Unlike `WorldPersistentState` (which handles non-instanceable maps) or `BattleGroundPersistentState`, `DungeonPersistentState` maintains explicit lists of `Player` and `Group` pointers. These lists serve two critical functions:
1.  **Lifecycle Management:** They prevent the instance state from being unloaded from memory while players or groups are still bound to it, ensuring that respawn timers and instance-specific data persist even if the map itself is temporarily unloaded or if players are offline.
2.  **Reset Logic:** They provide the basis for determining whether an instance can be reset (`CanReset`). If players are permanently bound to an instance, it generally cannot be reset until those bindings are cleared or the global reset time arrives.

This class acts as the bridge between the high-level instance management system (`MapPersistentStateManager`) and the individual entities (`Player`, `Group`) that interact with instances. It does not handle creature/gameobject spawning directly (that is handled by the base `MapPersistentState` and `PoolManager`), but it tracks the *ownership* of the instance.

## Member-by-Member Behavior

The members of `DungeonPersistentState` are grouped by their functional role: tracking bindings, managing reset eligibility, and lifecycle control.

### Binding Tracking

These methods maintain the lists of players and groups associated with the instance.

*   **`AddPlayer`**: Adds a `Player*` to the internal `m_playerList`. This is called when a solo player binds to an instance. The list is a `std::list`, allowing for efficient removal later.
*   **`RemovePlayer`**: Removes a `Player*` from `m_playerList`. Crucially, after removing the player, it calls `UnloadIfEmpty()` (inherited from `MapPersistentState`). If both `m_playerList` and `m_groupList` are now empty, and the map is not currently loaded (`m_usedByMap` is null), the entire `DungeonPersistentState` object may be destroyed to free memory.
*   **`AddGroup`**: Adds a `Group*` to the internal `m_groupList`. This occurs when a group binds to an instance. Note that group members are not individually added to `m_playerList` unless they have separate permanent binds; the group binding is tracked at the group level.
*   **`RemoveGroup`**: Removes a `Group*` from `m_groupList`. Like `RemovePlayer`, it triggers `UnloadIfEmpty()` to check if the instance state can be discarded.
*   **`GetPlayerCount`**: Returns the size of `m_playerList`. Used by `MapPersistentStateMgr` for statistics and by `Map` classes to verify binding consistency.
*   **`GetGroupCount`**: Returns the size of `m_groupList`. Used similarly for statistics and validation.

### Reset Management

These methods handle the timing and eligibility of instance resets.

*   **`GetResetTime`**: Returns the `m_resetTime` value. For normal dungeons, this is typically calculated as the maximum creature respawn time plus a buffer. For raids, it often reflects the global raid reset schedule.
*   **`SetResetTime`**: Updates `m_resetTime`. Called by `Map` when binding a player/group to ensure the instance's reset time aligns with the current schedule or specific instance rules.
*   **`CanReset`**: Returns the boolean `m_canReset`. This flag indicates whether the instance is allowed to be reset immediately (e.g., via admin commands or automatic cleanup). It is `false` if there are players permanently bound to the instance who are offline, preventing accidental loss of progress.
*   **`SetCanReset`**: Updates the `m_canReset` flag. Called by `Player` when binding to an instance, likely to set it to `false` if the player is establishing a permanent bind that should protect the instance from immediate reset.

### Lifecycle & State Queries

*   **`HasBounds`**: Returns `true` if either `m_playerList` or `m_groupList` is non-empty. This is a quick check used by `MapPersistentStateMgr` to decide if an instance can be safely unloaded from memory. If `HasBounds` is true, the instance is considered "active" in terms of ownership, even if no players are currently logged in.

## Cross-Unit Boundaries

`DungeonPersistentState` interacts heavily with the core entity classes (`Player`, `Group`) and the instance management singleton (`MapPersistentStateMgr`).

### Collaboration with `Player` (`Player.Main`)

*   **Direction:** `Player` calls `DungeonPersistentState`.
*   **Context:** When a player enters, leaves, or resets an instance, the `Player` class updates the instance's binding state.
    *   `Player.Main/BindToInstance` calls `AddPlayer` to register the player with the instance. It also calls `SetCanReset` to potentially lock the instance from resetting while the player is bound.
    *   `Player.Main/ResetInstance`, `Player.Main/UnbindInstance`, and `Player.Main/~Player` (destructor) call `RemovePlayer` to detach the player from the instance. This ensures that if a player logs off or resets, the instance knows it no longer has that specific solo binding.

### Collaboration with `Group` (`game_Group_Group`)

*   **Direction:** `Group` calls `DungeonPersistentState`.
*   **Context:** Groups bind to instances collectively.
    *   `game_Group_Group/BindToInstance` calls `AddGroup` to register the group.
    *   `game_Group_Group/Disband`, `game_Group_Group/ResetInstances`, `game_Group_Group/UnbindInstance`, `game_Group_Group/_setLeader`, and `game_Group_Group/~Group` call `RemoveGroup`. This ensures that when a group disbands, resets, or changes leadership (which might invalidate the old bind), the instance state is updated.

### Collaboration with `Map` (`Map.Main`)

*   **Direction:** `Map` calls `DungeonPersistentState`.
*   **Context:** The `Map` class represents the loaded instance in memory. It needs to know how many players/groups are bound to validate entries and manage resets.
    *   `Map.Main/BindPlayerOrGroupOnEnter` calls `GetPlayerCount` and `GetGroupCount` to verify that the entering player/group is actually bound to this instance. It also calls `CanReset` to check if the instance is in a valid state for entry.
    *   `Map.Main/BindToInstanceOrRaid` calls `SetResetTime` to synchronize the instance's reset timer with the map's expectations.

### Collaboration with `MapPersistentStateMgr` (`MapPersistentStateMgr`)

*   **Direction:** Bidirectional, but primarily `Mgr` manages `State`.
*   **Context:** `MapPersistentStateMgr` is the factory and registry for all instance states.
    *   `MapPersistentStateMgr/AddPersistentState` creates `DungeonPersistentState` objects and calls `SetResetTime` during initialization.
    *   `MapPersistentStateMgr/GetStatistics` calls `GetPlayerCount` and `GetGroupCount` to report server-wide instance usage metrics.
    *   `MapPersistentStateMgr/CanBeUnload#2` calls `HasBounds` to determine if an instance state can be removed from the manager's internal maps.

### Collaboration with `ChatHandler` (`ChatHandler.MiscCommands`, `ChatHandler.TeleportCommands`)

*   **Direction:** `ChatHandler` calls `DungeonPersistentState`.
*   **Context:** Administrative commands often need to inspect or force-reset instances.
    *   `HandleInstanceListBindsCommand` and `HandleInstanceUnbindHelper` call `GetResetTime` and `CanReset` to display instance status and determine if unbinding/resetting is possible.
    *   `HandleGonameCommand` (teleport) calls `CanReset` to ensure the target instance is stable before teleporting a player into it.

## Data Model

`DungeonPersistentState` does not directly execute SQL queries in its member functions shown in the map. However, it holds data that is persisted to and loaded from the database by `MapPersistentStateMgr` and the base `MapPersistentState` class.

The relevant database tables (implied by the class structure and typical MaNGOS/WowVMangos design) include:
*   `instance`: Stores the instance ID, map ID, reset time, and can-reset flag.
*   `instance_playerbind`: Stores the bindings between players and instances.
*   `instance_groupbind`: Stores the bindings between groups and instances.

While `DungeonPersistentState` members like `SaveToDB` and `DeleteFromDB` exist in the header, they are not listed in the provided MAP for this specific unit analysis, implying they are either implemented in a different partial or handled by the manager. The members in this MAP focus on the *in-memory* representation of these bindings.

## Notable Implementation Details

1.  **Memory Management via `UnloadIfEmpty`**:
    The most critical logic in `DungeonPersistentState` is the call to `UnloadIfEmpty()` in `RemovePlayer` and `RemoveGroup`. This function checks if `m_playerList` and `m_groupList` are empty. If they are, and if the map is not currently loaded (`m_usedByMap` is null), the state object deletes itself. This prevents memory leaks from orphaned instance states when all players leave and log off. However, if the map *is* loaded (`m_usedByMap` is not null), the state persists even if empty, because the map object holds a reference to it.

2.  **`m_canReset` Flag Semantics**:
    The `m_canReset` flag is a boolean that acts as a safety latch. It is set to `false` when a player binds permanently (via `SetCanReset`). This prevents the instance from being automatically reset by the scheduler or admin commands while a player is "saved" to it. This is crucial for preserving progress for players who log off mid-dungeon. The flag is likely reset to `true` when the player unbinds or when the global reset time forces a reset.

3.  **Group vs. Player Bindings**:
    The class distinguishes between solo player binds (`m_playerList`) and group binds (`m_groupList`). A player in a group is *not* added to `m_playerList` unless they have a separate permanent bind. This distinction allows the system to handle group disbanding correctly: if a group disbands, `RemoveGroup` is called, but the individual players' solo binds (if any) remain in `m_playerList`, keeping the instance alive for them.

4.  **Thread Safety**:
    The class uses `std::list` for player and group storage. Access to these lists is assumed to be serialized by the caller (typically the main game thread or protected by higher-level locks in `Map` or `Player`). There are no mutexes within `DungeonPersistentState` itself for `m_playerList` or `m_groupList`, implying that concurrent modification is not expected or is handled externally.

5.  **Inheritance from `MapPersistentState`**:
    `DungeonPersistentState` inherits significant functionality from `MapPersistentState`, including respawn time management (`m_creatureRespawnTimes`, `m_goRespawnTimes`) and pool data (`m_spawnedPoolData`). While these are not direct members of `DungeonPersistentState`, they are part of the object's state. The `CanBeUnload` override in `DungeonPersistentState` (via `HasBounds`) adds an extra layer of protection: even if the map is unloaded, if there are bounds (players/groups), the state stays in memory.

## Member Reference

**GetPlayerCount**
Returns the number of solo players currently bound to this instance. Implemented as `m_playerList.size()`. Called by `Map` and `MapPersistentStateMgr` for validation and statistics.

**GetGroupCount**
Returns the number of groups currently bound to this instance. Implemented as `m_groupList.size()`. Called by `Map` and `MapPersistentStateMgr` for validation and statistics.

**AddPlayer**
Adds a `Player*` to the `m_playerList`. Called by `Player.Main/BindToInstance` when a solo player enters and binds to an instance.

**RemovePlayer**
Removes a `Player*` from `m_playerList`. After removal, calls `UnloadIfEmpty()` to potentially destroy the state object if no other bindings exist. Called by `Player.Main/BindToInstance` (on re-bind?), `Player.Main/ResetInstance`, `Player.Main/UnbindInstance`, and `Player.Main/~Player`.

**AddGroup**
Adds a `Group*` to the `m_groupList`. Called by `game_Group_Group/BindToInstance` when a group binds to an instance.

**RemoveGroup**
Removes a `Group*` from `m_groupList`. After removal, calls `UnloadIfEmpty()` to potentially destroy the state object if no other bindings exist. Called by `game_Group_Group/BindToInstance` (on re-bind?), `game_Group_Group/Disband`, `game_Group_Group/ResetInstances`, `game_Group_Group/UnbindInstance`, `game_Group_Group/_setLeader`, and `game_Group_Group/~Group`.

**GetResetTime**
Returns the `m_resetTime` value, indicating when the instance is scheduled to reset. Called by `ChatHandler` commands, `Map` binding logic, and `MapPersistentStateMgr` for DB synchronization.

**SetResetTime**
Sets the `m_resetTime` value. Called by `Map.Main/BindToInstanceOrRaid` and `MapPersistentStateMgr/AddPersistentState` to initialize or update the reset schedule.

**CanReset**
Returns the `m_canReset` boolean flag. Indicates if the instance can be reset immediately. Called by `ChatHandler` commands, `Map` entry logic, and `Player` reset logic.

**SetCanReset**
Sets the `m_canReset` boolean flag. Called by `Player.Main/BindToInstance` to lock/unlock reset eligibility based on player binding status.

**HasBounds**
Returns `true` if `m_playerList` or `m_groupList` is non-empty. Used by `MapPersistentStateMgr/CanBeUnload#2` to determine if the instance state should be kept in memory.

---

<!-- machine-true, projected from graph.json -->

## Map — DungeonPersistentState

*Source:* MapPersistentStateMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GetPlayerCount | method | — | Map.Main/BindPlayerOrGroupOnEnter, MapPersistentStateMgr/GetStatistics | — |
| GetGroupCount | method | — | Map.Main/BindPlayerOrGroupOnEnter, MapPersistentStateMgr/GetStatistics | — |
| AddPlayer | method | — | Player.Main/BindToInstance | — |
| RemovePlayer | method | — | Player.Main/BindToInstance, Player.Main/ResetInstance, Player.Main/UnbindInstance, Player.Main/~Player | — |
| AddGroup | method | — | game_Group_Group/BindToInstance | — |
| RemoveGroup | method | — | game_Group_Group/BindToInstance, game_Group_Group/Disband, game_Group_Group/ResetInstances, game_Group_Group/UnbindInstance, game_Group_Group/_setLeader, game_Group_Group/~Group | — |
| GetResetTime | method | — | ChatHandler.MiscCommands/HandleInstanceListBindsCommand, ChatHandler.MiscCommands/HandleInstanceUnbindHelper, Map.Main/BindToInstanceOrRaid, Map.Main/SetResetSchedule, MapPersistentStateMgr/GetResetTimeForDB | — |
| SetResetTime | method | — | Map.Main/BindToInstanceOrRaid, MapPersistentStateMgr/AddPersistentState | — |
| CanReset | method | — | ChatHandler.MiscCommands/HandleInstanceListBindsCommand, ChatHandler.MiscCommands/HandleInstanceUnbindHelper, ChatHandler.TeleportCommands/HandleGonameCommand, game_Group_Group/ResetInstances, Map.Main/BindPlayerOrGroupOnEnter, Player.Main/ResetInstances | — |
| SetCanReset | method | — | Player.Main/BindToInstance | — |
| HasBounds | method | — | MapPersistentStateMgr/CanBeUnload#2 | — |
