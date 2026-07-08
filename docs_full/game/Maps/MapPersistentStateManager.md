# MapPersistentStateManager

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# MapPersistentStateManager

## Purpose & Responsibilities

`MapPersistentStateManager` is the central singleton responsible for managing the lifecycle, persistence, and reset scheduling of **instance states** and **world map persistent data** in the WoW server. It acts as the bridge between the in-memory `Map` objects (which represent active zones/dungeons) and the database-backed persistent state that survives map unloads or server restarts.

Its core responsibilities are:
1.  **State Management:** Creating, retrieving, and destroying `MapPersistentState` objects (and its subclasses: `WorldPersistentState`, `DungeonPersistentState`, `BattleGroundPersistentState`) based on map IDs and instance IDs.
2.  **Respawn Tracking:** Loading and maintaining creature and game object respawn timers for both world maps and instances.
3.  **Reset Scheduling:** Via the internal `DungeonResetScheduler`, it calculates when raids and heroics should reset, schedules these events, and executes them (either warning players or forcibly resetting the instance).
4.  **Memory Optimization:** Implementing logic to unload persistent states from memory when they are no longer needed (e.g., when no players are bound to a dungeon and respawn timers have expired), while keeping critical data in memory to avoid excessive database hits.

This unit does not handle the actual rendering or simulation of entities; it handles the *metadata* and *lifecycle rules* that govern when those entities exist, respawn, or are wiped clean.

## Member-by-Member Behavior

The unit consists of two primary methods exposed on the `MapPersistentStateManager` singleton, along with a rich set of internal helper classes (`MapPersistentState`, `DungeonResetScheduler`, etc.) that implement the logic.

### `GetScheduler`
*   **Kind:** Method
*   **Purpose:** Returns a reference to the internal `DungeonResetScheduler` object.
*   **Behavior:** This is the primary interface for external units to interact with the reset scheduling system. It allows other parts of the server to query reset times, schedule manual resets, or trigger global raid resets.
*   **Cross-Unit Collaboration:**
    *   **Called by:**
        *   `ChatHandler.MiscCommands/HandleInstanceListBindsCommand`: Likely used to display current reset times or bound status to administrators.
        *   `ChatHandler.ServerCommands/HandleServerResetAllRaidCommand`: Used to manually force a reset of all raid instances.
        *   `Map.Main/SetResetSchedule`: Called when a map is initialized or modified to register its reset requirements with the scheduler.
        *   `Player.Main/SendRaidInfo`: Used to send the next reset time to a player joining a raid.
        *   `WorldSession.MovementHandler/HandleMoveWorldportAck`: Likely checks reset status or updates player bindings upon entering a world port/instance.

### `Update`
*   **Kind:** Method
*   **Purpose:** Performs periodic maintenance tasks related to instance resets and state cleanup.
*   **Behavior:** This method is called regularly by the server's main update loop. It delegates work to the internal `DungeonResetScheduler::Update()` method. The scheduler checks its queue of scheduled reset events. If the current time matches a scheduled event, it triggers the appropriate action (warning players or executing a reset). It also likely handles cleaning up expired instance states that are no longer needed in memory.
*   **Cross-Unit Collaboration:**
    *   **Called by:**
        *   `World/Update`: The main server tick loop calls this to ensure reset schedules are processed in real-time.

## Cross-Unit Boundaries

`MapPersistentStateManager` sits at the intersection of the **World Server Core**, **Player/Group Management**, and **Database Persistence**.

1.  **With `World` (`World/Update`):**
    *   **Direction:** `World` calls `MapPersistentStateManager::Update`.
    *   **Why:** The `World` object drives the server's heartbeat. By calling `Update`, it ensures that time-sensitive operations like raid resets happen at the correct moment without blocking the main thread for long periods.

2.  **With `ChatHandler` (`ChatHandler.MiscCommands`, `ChatHandler.ServerCommands`):**
    *   **Direction:** `ChatHandler` calls `MapPersistentStateManager::GetScheduler`.
    *   **Why:** Administrators need tools to inspect and manipulate instance states. The scheduler provides the data (reset times, bound players) and actions (force reset) required for these commands.

3.  **With `Map` (`Map.Main/SetResetSchedule`):**
    *   **Direction:** `Map` calls `MapPersistentStateManager::GetScheduler`.
    *   **Why:** When a `Map` object is created or its configuration changes, it needs to inform the global scheduler of its reset requirements (e.g., "I am a raid, reset every 7 days").

4.  **With `Player` (`Player.Main/SendRaidInfo`):**
    *   **Direction:** `Player` calls `MapPersistentStateManager::GetScheduler`.
    *   **Why:** When a player joins a raid, the client expects to know when the raid will reset. The player object queries the scheduler to get this timestamp and sends it to the client.

5.  **With `WorldSession` (`WorldSession.MovementHandler/HandleMoveWorldportAck`):**
    *   **Direction:** `WorldSession` calls `MapPersistentStateManager::GetScheduler`.
    *   **Why:** During movement handling, particularly when acknowledging world ports or instance transitions, the session may need to verify instance validity or update binding information, which relies on the scheduler's state.

## Data Model

While the provided source code for `MapPersistentStateManager` itself does not contain direct SQL queries (those are likely located in the implementation `.cpp` file or in the `DungeonPersistentState`/`WorldPersistentState` subclasses), the structure implies interaction with standard MaNGOS/WoWVMaNGOS instance tables. Based on the member names and typical usage in this codebase:

*   **`instance_reset`**: Stores the last reset time for each map/instance ID. Used by `LoadResetTimes` and `SetResetTimeFor`.
*   **`creature_respawn` / `gameobject_respawn`**: Stores respawn timestamps for killed creatures and destroyed game objects. Loaded by `LoadCreatureRespawnTimes` and `LoadGameobjectRespawnTimes`.
*   **`instance_bind`**: Tracks which players or groups are bound to specific instances. Managed by `DungeonPersistentState` methods like `AddPlayer`, `RemovePlayer`, `SaveToDB`, and `DeleteFromDB`.
*   **`pool_template` / `pool_creature` / `pool_gameobject`**: Related to the `SpawnedPoolData` and `InitPools` functionality, managing dynamic spawns within instances.

*Note: Specific column names and types are not explicitly defined in the provided header, so they are described conceptually based on standard WoW server architecture.*

## Notable Implementation Details

1.  **Singleton Pattern:** `MapPersistentStateManager` inherits from `MaNGOS::Singleton`, ensuring only one instance exists globally. It uses `ClassLevelLockable` with a `std::mutex` for thread safety, critical since multiple threads (network, update, AI) may access instance state concurrently.

2.  **Polymorphic State Management:** The base class `MapPersistentState` is abstracted into three specialized subclasses:
    *   `WorldPersistentState`: For non-instanceable maps (world zones). Primarily tracks respawns.
    *   `DungeonPersistentState`: For instanceable dungeons/raids. Tracks player/group bindings, reset times, and whether the instance can be reset.
    *   `BattleGroundPersistentState`: For battlegrounds/arenas.
    This design allows the manager to treat all maps uniformly while applying specific logic (like binding checks) only where relevant.

3.  **Memory Unloading Logic:** The `CanBeUnload()` method in `MapPersistentState` and its subclasses determines if a state can be removed from memory.
    *   `WorldPersistentState` can unload if not used by a map and no respawns are pending.
    *   `DungeonPersistentState` can unload if no players/groups are bound and no respawns are pending.
    This prevents memory leaks from abandoned instances while keeping active ones responsive.

4.  **Reset Scheduler Queue:** `DungeonResetScheduler` uses a `std::multimap<time_t, DungeonResetEvent>` (`m_resetTimeQueue`) to efficiently find and process resets due at the current time. This avoids iterating through all instances every tick.

5.  **Thread Safety for Grid Objects:** `MapPersistentState` uses a `std::shared_timed_mutex` (`m_cellObjectGuidsMutex`) to protect `m_gridObjectGuids`. This allows multiple readers (e.g., pathfinding, visibility checks) but exclusive writers (e.g., spawning/despawning), optimizing performance for high-contention areas.

6.  **Template-Based Iteration:** `DoForAllStatesWithMapId` is a template function that accepts a functor (`Do`). This allows callers to perform custom operations on all states for a given map ID without exposing the internal storage structures (`m_instanceSaveByInstanceId` vs `m_instanceSaveByMapId`).

## Member Reference

**GetScheduler**
Returns a reference to the internal `DungeonResetScheduler`. Used by external units to query reset times, schedule resets, or force global resets. Called by `ChatHandler`, `Map`, `Player`, and `WorldSession` units.

**Update**
Delegates to `DungeonResetScheduler::Update()` to process scheduled reset events and clean up expired states. Called periodically by the `World` unit's main update loop.

---

<!-- machine-true, projected from graph.json -->

## Map — MapPersistentStateManager

*Source:* MapPersistentStateMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GetScheduler | method | — | ChatHandler.MiscCommands/HandleInstanceListBindsCommand, ChatHandler.ServerCommands/HandleServerResetAllRaidCommand, Map.Main/SetResetSchedule, Player.Main/SendRaidInfo, WorldSession.MovementHandler/HandleMoveWorldportAck | — |
| Update | method | — | World/Update | — |
