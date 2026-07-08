# PlayerBotStats

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# PlayerBotStats

**Purpose & Responsibilities**

`PlayerBotStats` is a lightweight aggregate struct within the `PlayerBotMgr` subsystem that tracks real-time operational metrics and configuration thresholds for the player bot system. It serves two distinct roles:

1.  **Runtime Monitoring:** It maintains counters for the current state of bots in the world (`onlineCount`, `loadingCount`, `totalBots`, `onlineChat`). These values allow the manager to determine if the server population meets minimum requirements or exceeds maximum limits.
2.  **Configuration Storage:** It holds cached copies of configuration parameters (`confMaxOnline`, `confMinOnline`, `confRandomBotsRefresh`, `confUpdateDiff`) that dictate how the bot manager behaves during updates (e.g., how often to refresh random bots, what the target population range is).

This struct is embedded directly within the `PlayerBotMgr` singleton as the member `m_stats`. It does not perform any logic itself; it is purely data storage.

## Member-by-Member Behavior

The struct contains eight `uint32` members, initialized to zero by its default constructor.

### Runtime Counters
*   **`onlineCount`**: Tracks the number of bots currently in the `PB_STATE_ONLINE` state. This represents bots that are fully loaded and active in the game world.
*   **`loadingCount`**: Tracks the number of bots currently in the `PB_STATE_LOADING` state. These are bots that have been spawned but are not yet fully interactive or ready.
*   **`totalBots`**: Represents the total number of bot entries currently managed by the system, regardless of their state (online, loading, or offline/in-memory).
*   **`onlineChat`**: Tracks the number of bots that are both online and flagged as chat bots (`isChatBot`). These are typically bots designed to interact via text channels rather than participate in combat or movement.

### Configuration Thresholds
*   **`confMaxOnline`**: The upper limit for the number of bots allowed to be online simultaneously. The manager uses this to prevent over-population.
*   **`confMinOnline`**: The lower limit for the number of bots required to be online. If the count drops below this, the manager may spawn additional bots.
*   **`confRandomBotsRefresh`**: A time-based threshold (likely in milliseconds) indicating how frequently the pool of random bots should be refreshed or rotated.
*   **`confUpdateDiff`**: A time-based threshold related to the update cycle of the bot manager, potentially dictating the interval between major management ticks.

## Cross-Unit Boundaries

`PlayerBotStats` has no direct cross-unit calls or incoming calls from other units because it is a passive data structure. However, it is accessed indirectly through the `PlayerBotMgr` interface:

*   **Accessed by External Units via `PlayerBotMgr.GetStats()`**: Other parts of the codebase (not detailed in this specific map but implied by the `GetStats()` accessor in `PlayerBotMgr`) retrieve a reference to `m_stats` to read current bot counts or configuration values. For example, a UI module or a logging system might query `onlineCount` to display server population statistics.
*   **Modified by `PlayerBotMgr` Internal Logic**: While not shown in the `PlayerBotStats` map, the `PlayerBotMgr` methods (such as `AddBot`, `DeleteBot`, `OnBotLogin`, `OnBotLogout`) are responsible for incrementing or decrementing these counters. The `PlayerBotStats` struct itself does not contain the logic to modify its own fields; that responsibility lies entirely within the `PlayerBotMgr` class methods.

## Data Model

`PlayerBotStats` does not interact directly with any database tables. It stores in-memory state derived from the bot management process. The underlying bot data (such as GUIDs, accounts, and states) is stored in database tables managed by `PlayerBotMgr`'s `Load()` and `Save()` operations (not part of this unit's scope), but `PlayerBotStats` itself is purely volatile memory.

## Notable Implementation Details

*   **Zero Initialization**: The constructor explicitly initializes all members to `0`. This ensures that upon server startup, before any bots are loaded, the stats reflect an empty state.
*   **No Encapsulation**: All members are public. There are no getters or setters within `PlayerBotStats` itself. This design choice allows `PlayerBotMgr` to update these values directly and efficiently without function call overhead, which is critical for performance in a high-frequency update loop.
*   **Configuration vs. State**: The struct mixes runtime state (counts) with static configuration (limits). This coupling simplifies access for the manager but means that changes to configuration require reloading the entire stats object or updating individual fields manually by the manager.
*   **Type Consistency**: All members are `uint32`. This implies that the bot system is not expected to handle more than ~4 billion bots, which is a reasonable constraint for any server instance.

## Member Reference

**PlayerBotStats**  
Constructor. Initializes all eight `uint32` members (`onlineCount`, `loadingCount`, `totalBots`, `onlineChat`, `confMaxOnline`, `confMinOnline`, `confRandomBotsRefresh`, `confUpdateDiff`) to `0`. This ensures a clean state when the `PlayerBotMgr` singleton is instantiated.

---

<!-- machine-true, projected from graph.json -->

## Map — PlayerBotStats

*Source:* PlayerBotMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| PlayerBotStats | ctor | — | — | — |
