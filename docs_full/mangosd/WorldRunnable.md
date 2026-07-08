<!-- provenance: verbose, failed-members -->
# WorldRunnable

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`WorldRunnable` implements the primary execution loop for the `mangosd` game server process. Its single method, `operator()`, serves as the entry point for the main server thread (the "heartbeat"). It is responsible for initializing critical subsystems (database threads, anti-crash watchdogs), maintaining a continuous update cycle at a target frequency of 20 Hz (50 ms per tick), and coordinating the orderly shutdown of the world state, including battle grounds, maps, and database connections. It does not contain game logic itself but orchestrates the invocation of global managers like `World`, `MapManager`, and `BattleGroundMgr`.

## Member-by-Member Behavior

### `operator()`

This method manages the entire lifecycle of the server thread in three phases: initialization, the main update loop, and shutdown.

**1. Initialization**
*   **Database Thread**: Starts the asynchronous MySQL thread for the world database via `DatabaseMysql/ThreadStart` (on `WorldDatabase`) and initializes the result queue via `World/InitResultQueue`.
*   **Watchdog**: Arms the system-level anti-crash mechanism via `Master::ArmAnticrash`.
*   **Timer Resolution**: Sets the OS thread timer period to 1 ms via `shared_Util/getMSTime` (wrapped in `set_time_period`) to ensure precise sleep and timing calculations.
*   **Baseline Time**: Records the initial timestamp using `WorldTimer/getMSTime` to calculate the first delta time.

**2. Main Update Loop**
The loop runs while `World/IsStopped` returns false. Each iteration:
*   **Tick Tracking**: Increments `World::m_worldLoopCounter`.
*   **Delta Calculation**: Computes `diff` (time since last tick) using `WorldTimer/getMSTimeDiff`.
*   **Performance Logging**: If `diff` exceeds the configured `CONFIG_UINT32_PERFLOG_SLOW_WORLD_UPDATE` threshold (checked via `World/getConfig`), it logs a "Slow world update" warning via `Log.Main/Out`.
*   **Anti-Crash Rearming**:
    *   Checks `World/GetAnticrashRearmTimer`. If non-zero, it captures the value and resets the internal timer via `World/SetAnticrashRearmTimer`.
    *   If a rearm timer is active, it decrements it by `diff`. Upon expiration, it calls `Master::ArmAnticrash` to reset the watchdog and logs "Anticrash rearmed". This prevents the watchdog from terminating the server during expected heavy loads.
*   **World Update**: Delegates game state advancement to `World/Update(diff)`.
*   **Frame Rate Control**:
    *   Calculates `updateTime` (actual time spent in the loop body) using `WorldTimer/getMSTimeDiffToNow`.
    *   If `updateTime` is less than `WORLD_SLEEP_CONST` (50 ms), it sleeps for the remainder using `std::this_thread::sleep_for`.
    *   If `updateTime` exceeds 50 ms, it skips the sleep, allowing the server to run as fast as possible to catch up (noting in comments that this does not smooth spikes).
*   **Windows Service Handling**: On Windows, it checks `m_ServiceStatus`. If stopping (`0`), it calls `World::StopNow`. If paused (`2`), it sleeps in 1-second increments.

**3. Shutdown**
When `World/IsStopped` becomes true, the loop exits and cleanup proceeds in strict order:
1.  Logs "Shutting down world..." via `Log.Main/Out`.
2.  Calls `World/Shutdown` for internal world cleanup.
3.  Calls `BattleGroundMgr/DeleteAllBattleGrounds` to clean up active battle grounds (required before other singletons are destroyed).
4.  Logs "Unloading all maps..." via `Log.Main/Out`.
5.  Calls `MapManager/UnloadAll` to free all grid data.
6.  Calls `DatabaseMysql/ThreadEnd` (on `WorldDatabase`) to terminate the database thread.

## Cross-Unit Boundaries

`WorldRunnable` acts as a central coordinator, calling into various subsystems. It is not called by other units in the provided map, implying instantiation by the application entry point.

*   **DatabaseMysql (`WorldDatabase`)**: Calls `ThreadStart` and `ThreadEnd` to manage the lifecycle of the asynchronous database connection thread.
*   **World (`sWorld`)**: Calls `InitResultQueue`, `IsStopped`, `getConfig`, `GetAnticrashRearmTimer`, `SetAnticrashRearmTimer`, `Update`, `Shutdown`, and `StopNow` (indirectly via static context in Win32 block). `World` holds the central state; `WorldRunnable` queries it for configuration, stop signals, and anti-crash timers, and delegates game tick processing to `Update`.
*   **Master**: Calls `ArmAnticrash` to signal liveness to the OS or parent process, preventing termination due to unresponsiveness.
*   **Log.Main (`sLog`)**: Calls `Out` to record lifecycle events (start, slow updates, anti-crash rearming, shutdown steps).
*   **MapManager (`sMapMgr`)**: Calls `UnloadAll` during shutdown to free spatial data (grids/maps) from memory.
*   **BattleGroundMgr (`sBattleGroundMgr`)**: Calls `DeleteAllBattleGrounds` during shutdown to clean up persistent battle ground instances, ordered before map unloading to avoid dangling pointers.
*   **TimePeriod / shared_Util**: Calls `set_time_period` (using `getMSTime` internally) to optimize OS timer resolution for accurate sleeping and delta-time calculations.
*   **WorldTimer**: Calls `getMSTime`, `getMSTimeDiff`, and `getMSTimeDiffToNow` to provide high-resolution, monotonic time sources for frame deltas and tick rate enforcement.

## Data Model

This unit does not directly query or modify any database tables. It manages the lifecycle of the database thread (`WorldDatabase`), but all SQL interactions are delegated to other components (primarily `World` and its sub-managers) which execute queries asynchronously. Therefore, no table schemas are relevant to the direct logic of `WorldRunnable`.

## Notable Implementation Details

1.  **Catch-up Behavior**: The server targets a fixed 20 Hz tick rate (50 ms). If an update takes longer than 50 ms, the sleep is skipped, and the next iteration runs immediately. This causes the server to consume 100% CPU under heavy load to catch up, rather than dropping frames. The comments acknowledge this limitation regarding spike smoothing.
2.  **Anti-Crash Rearm Logic**: The anti-crash mechanism supports a "rearm timer" stored in `World`. If `World` sets this timer (likely during known heavy operations), `WorldRunnable` delays re-arming the OS watchdog until the timer expires. This prevents false positives where the server is legitimately busy but not hung.
3.  **Strict Shutdown Order**: The shutdown sequence is critical: `World::Shutdown` → `BattleGroundMgr::DeleteAllBattleGrounds` → `MapManager::UnloadAll` → `WorldDatabase::ThreadEnd`. Deviating from this order risks crashes due to dangling references between battle grounds, maps, and world objects, or access violations if the DB thread accesses destroyed objects.
4.  **Windows Service Integration**: The `#ifdef WIN32` blocks handle `m_ServiceStatus`, allowing the server to respond to pause/stop commands from the Service Control Manager. The `Sleep(1000)` loop during pause prevents CPU burning.
5.  **Timer Precision**: The call to `set_time_period(std::chrono::milliseconds(1))` reduces OS timer resolution from default (often 15 ms) to 1 ms, significantly improving the precision of `sleep_for` and `getMSTimeDiff` calls, reducing jitter in the tick rate.

## Member Reference

**operator()**
The main entry point for the server's heartbeat thread. Initializes the database thread and anti-crash watchdog, enters a loop that updates the world state at a target rate of 20 Hz (50 ms per tick), handles performance logging and anti-crash rearming, and finally executes an ordered shutdown sequence involving battle grounds, maps, and the database thread.

---

<!-- machine-true, projected from graph.json -->

## Map — WorldRunnable

*Source:* WorldRunnable.cpp, WorldRunnable.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| operator() | method | BattleGroundMgr/DeleteAllBattleGrounds, DatabaseMysql/ThreadEnd, DatabaseMysql/ThreadStart, Log.Main/Out, MapManager/UnloadAll, Master/ArmAnticrash, shared_Util/getMSTime, TimePeriod/set_time_period#2, World/GetAnticrashRearmTimer, World/getConfig#4, World/InitResultQueue, World/IsStopped, World/SetAnticrashRearmTimer, World/Shutdown, World/Update, WorldTimer/getMSTimeDiff, WorldTimer/getMSTimeDiffToNow | — | — |

---

<!-- verify: failed-members | invented: operator -->
