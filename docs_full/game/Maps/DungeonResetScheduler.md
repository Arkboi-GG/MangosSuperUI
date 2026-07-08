# DungeonResetScheduler

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# DungeonResetScheduler

**Purpose & Responsibilities**

`DungeonResetScheduler` is a helper class within `MapPersistentStateMgr.h` responsible for managing the timing and execution of instance resets in the WoW server emulation. It maintains two primary data structures:
1.  **`m_resetTimeByMapId`**: A vector mapping map IDs to their next scheduled global reset time (`time_t`). This allows for O(1) lookups when querying when a specific map type (e.g., Molten Core, Onyxia's Lair) will reset.
2.  **`m_resetTimeQueue`**: A multimap ordered by time, containing `DungeonResetEvent` objects. This acts as a priority queue for scheduling future reset events, including both actual resets and informational warnings (e.g., "Raid resets in 1 hour").

The scheduler does not perform the reset logic itself; instead, it coordinates with `MapPersistentStateManager` (via the `m_InstanceSaves` reference) to trigger resets or send warnings when the current time reaches or passes the scheduled event time.

**Member-by-Member Behavior**

### Construction
*   **`DungeonResetScheduler`**: The constructor initializes the scheduler with a reference to the `MapPersistentStateManager`. This reference is stored in `m_InstanceSaves` and is used throughout the scheduler's lifetime to delegate actual reset operations and state modifications back to the manager.

### Accessors & Calculators
*   **`GetResetTimeFor`**: Returns the next scheduled reset time for a given `mapId` from `m_resetTimeByMapId`. If no entry exists for the map ID, it returns the default-initialized value (0). This is a fast lookup used by various parts of the server to inform players or check reset status.
*   **`GetMaxResetTimeFor`**: A static utility function that calculates the maximum possible reset duration for a given `MapEntry`. This is likely used to determine the longest potential wait time for a reset based on map difficulty/type.
*   **`CalculateNextResetTime`**: A static utility function that computes the next reset timestamp given a `MapEntry` and a previous reset time. It encapsulates the logic for determining reset intervals (e.g., daily for raids, weekly for some dungeons, or dynamic based on activity).

### Modifiers & Scheduling
*   **`SetResetTimeFor`**: Updates the next reset time for a specific `mapId` in `m_resetTimeByMapId`. This is typically called after a reset occurs or when loading initial reset times from the database.
*   **`ScheduleReset`**: Adds a `DungeonResetEvent` to `m_resetTimeQueue` at a specified time. The `add` parameter likely controls whether this is a new event or an update to an existing one. This function populates the priority queue that drives the `Update` loop.
*   **`Update`**: The core tick function called periodically by `MapPersistentStateManager::Update`. It processes `m_resetTimeQueue`, checking if the current time has reached or passed any scheduled events. For each expired event, it triggers the appropriate action via `m_InstanceSaves` (either resetting an instance or sending a warning). It also cleans up processed events from the queue.
*   **`ResetAllRaid`**: Forces an immediate reset of all raid instances. This is likely an administrative command handler that bypasses normal scheduling and triggers resets for all raid-type maps immediately.
*   **`ScheduleAllDungeonResets`**: Initializes or re-schedules all pending dungeon and raid resets. This is likely called during server startup or after a major configuration change to populate `m_resetTimeQueue` and `m_resetTimeByMapId` with correct future events.
*   **`LoadResetTimes`**: Loads the current reset times for all maps from the database into `m_resetTimeByMapId`. This ensures that reset schedules persist across server restarts.

**Cross-Unit Boundaries**

*   **Called by `MapPersistentStateMgr/MapPersistentStateManager`**:
    *   `DungeonResetScheduler` is constructed by `MapPersistentStateManager` and stored as a member (`m_Scheduler`).
    *   `MapPersistentStateManager::Update` calls `DungeonResetScheduler::Update` to process scheduled reset events.
    *   `MapPersistentStateManager::LoadResetTimes` calls `DungeonResetScheduler::LoadResetTimes` to initialize reset times from the DB.
    *   `MapPersistentStateManager::ScheduleAllDungeonResets` calls `DungeonResetScheduler::ScheduleAllDungeonResets` to set up the initial schedule.
    *   `MapPersistentStateManager::ResetAllRaid` calls `DungeonResetScheduler::ResetAllRaid` to force raid resets.
    *   `MapPersistentStateManager::SetResetTimeFor` (via delegation or direct access) calls `DungeonResetScheduler::SetResetTimeFor` to update reset times.

*   **Called by `ChatHandler.MiscCommands/HandleInstanceListBindsCommand`**:
    *   This command handler queries `DungeonResetScheduler::GetResetTimeFor` to display the next reset time for instances to administrators or players.

*   **Called by `Player.Main/SendRaidInfo`**:
    *   When sending raid information to a player, the server uses `DungeonResetScheduler::GetResetTimeFor` to include the next reset time for relevant raids.

*   **Called by `WorldSession.MovementHandler/HandleMoveWorldportAck`**:
    *   During world port acknowledgments, the server may check `DungeonResetScheduler::GetResetTimeFor` to ensure consistency or handle edge cases related to instance transitions near reset times.

**Data Model**

The `DungeonResetScheduler` interacts with the database indirectly through `MapPersistentStateManager`. The specific tables involved in storing and retrieving reset times are not explicitly detailed in the provided source code for this unit, but `LoadResetTimes` implies reading from a table that stores map IDs and their last reset times (likely `instance_reset` or similar in the WoW database schema). The scheduler itself does not perform direct SQL queries; it relies on `MapPersistentStateManager` to handle database interactions.

**Notable Implementation Details**

*   **Static Utility Functions**: `GetMaxResetTimeFor` and `CalculateNextResetTime` are static, meaning they do not depend on the scheduler's internal state. They operate purely on `MapEntry` data and timestamps, making them reusable and testable in isolation.
*   **Priority Queue for Events**: The use of `std::multimap<time_t, DungeonResetEvent>` for `m_resetTimeQueue` allows efficient retrieval of the next upcoming event. Since `multimap` keeps keys sorted, iterating from the beginning yields events in chronological order.
*   **Decoupling Logic**: The scheduler separates the *when* (timing and scheduling) from the *what* (actual reset logic). It delegates the heavy lifting of resetting instances and managing persistent states to `MapPersistentStateManager`, adhering to the Single Responsibility Principle.
*   **Thread Safety Considerations**: While the scheduler itself does not appear to have explicit locking mechanisms in the provided snippet, it operates within the context of `MapPersistentStateManager`, which is marked as `MaNGOS::ClassLevelLockable`. This suggests that access to the scheduler's methods is protected by the manager's mutex, ensuring thread safety in a multi-threaded server environment.

## Member Reference

*   **DungeonResetScheduler**: Constructor that initializes the scheduler with a reference to `MapPersistentStateManager`.
*   **GetResetTimeFor**: Method that returns the next scheduled reset time for a given map ID from `m_resetTimeByMapId`.
*   **SetResetTimeFor**: Method that updates the next reset time for a specific map ID in `m_resetTimeByMapId`.

---

<!-- machine-true, projected from graph.json -->

## Map — DungeonResetScheduler

*Source:* MapPersistentStateMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| DungeonResetScheduler | ctor | — | MapPersistentStateMgr/MapPersistentStateManager | — |
| GetResetTimeFor | method | — | ChatHandler.MiscCommands/HandleInstanceListBindsCommand, MapPersistentStateMgr/ScheduleAllDungeonResets, MapPersistentStateMgr/Update, Player.Main/SendRaidInfo, WorldSession.MovementHandler/HandleMoveWorldportAck | — |
| SetResetTimeFor | method | — | MapPersistentStateMgr/LoadResetTimes, MapPersistentStateMgr/ResetAllRaid, MapPersistentStateMgr/ScheduleAllDungeonResets, MapPersistentStateMgr/Update | — |
