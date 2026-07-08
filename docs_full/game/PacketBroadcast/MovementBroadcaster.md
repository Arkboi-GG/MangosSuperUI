# MovementBroadcaster

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# MovementBroadcaster

**Purpose & Responsibilities**

`MovementBroadcaster` is a multi-threaded subsystem that distributes network packets for player movement updates. It offloads this high-frequency, computationally expensive work from the main game loop by maintaining a pool of worker threads.

The class manages a partitioned set of `PlayerBroadcaster` objects (one per connected player). Players are distributed across worker threads based on a hash of their GUID (`GetRawValue() % num_threads`). Each worker thread periodically wakes up, acquires a shared lock on its assigned subset of players, copies the set, and invokes `ProcessQueue` on each `PlayerBroadcaster` to send pending movement packets.

It supports dynamic reconfiguration of thread count and sleep interval via `UpdateConfiguration`, and includes performance monitoring to log slow broadcast cycles and identify specific map instances contributing disproportionately to packet volume.

**Data Model**

This unit does not interact with any database tables. All state is held in memory.

**Cross-Unit Boundaries**

*   **PlayerBroadcaster**: Core dependency. `MovementBroadcaster` holds `std::shared_ptr<PlayerBroadcaster>` objects. It calls `PlayerBroadcaster::GetGUID` for thread affinity hashing during registration/removal, and `PlayerBroadcaster::ProcessQueue` during the broadcast cycle to send packets.
*   **World**: Used for configuration retrieval (`getConfig`) and session management (`GetAllSessions`, `WorldSession::GetPlayer`) during reconfiguration.
*   **Log**: Used for informational and performance logging (`sLog.Out`).
*   **WorldTimer**: Used for timing operations (`getMSTime`, `getMSTimeDiffToNow`) to measure broadcast latency.
*   **ChatHandler**: Consumes statistics from `MovementBroadcaster` (`GetStats`, `GetSleepTimer`, `GetNumThreads`) for administrative monitoring and tuning.
*   **Map**: Calls `IsMapSlow` to check if a specific instance is flagged as a performance bottleneck.
*   **WorldObject**: Calls `IsEnabled` to determine if movement messages should be routed through this broadcaster.
*   **Player**: Calls `RegisterPlayer` and `RemovePlayer` during packet broadcaster creation/deletion.

**Notable Implementation Details**

1.  **Thread Affinity via Hashing**: Players are assigned to threads using `player->GetGUID().GetRawValue() % m_num_threads`. This ensures consistent thread ownership per player. Changing the thread count requires stopping all threads, recreating the pool, and re-registering every online player via `UpdateConfiguration`.
2.  **Locking Strategy**: Uses `std::shared_timed_mutex` per thread. Registration/removal takes an exclusive lock (`std::lock_guard`). Broadcasting takes a shared lock (`std::shared_lock`) only to copy the player set (`my_players = m_thread_players[index]`), then releases the lock before iterating and calling `ProcessQueue`. This minimizes lock hold time during potentially long packet processing.
3.  **Slow Map Identification**: If a broadcast cycle exceeds `CONFIG_UINT32_PBCAST_DIFF_LOWER_VISIBILITY_DISTANCE`, `IdentifySlowMap` aggregates `lastUpdatePackets` per `instanceId` for players on that thread. The instance with the highest packet count is marked as `slow_instance` in the thread's stats. `IsMapSlow` checks and resets this flag.
4.  **Disabled State**: If `m_num_threads` is 0, the broadcaster is disabled. `IsEnabled()` returns false, and `RegisterPlayer`/`RemovePlayer` return immediately.

## Member Reference

**MovementBroadcaster**
Constructor. Initializes `m_num_threads` and `m_sleep_timer`. Logs configuration if threads are enabled. Calls `StartThreads`.

**~MovementBroadcaster**
Destructor. Calls `Stop` if not already stopped, ensuring clean thread shutdown.

**IsEnabled**
Returns `true` if `m_num_threads` is non-zero.

**StartThreads**
Asserts `m_threads` is empty. Creates vectors for locks, player sets, and stats sized to `m_num_threads`. Resets `m_stop` to false. Spawns `m_num_threads` worker threads running `Work`.

**RegisterPlayer**
If disabled, returns. Calculates thread index via GUID hash. Acquires exclusive lock on that thread's mutex. Inserts `player` into `m_thread_players[index]`.

**GetStats**
Returns constant reference to `m_thread_update_stats`.

**GetSleepTimer**
Returns `m_sleep_timer`.

**GetNumThreads**
Returns `m_num_threads`.

**RemovePlayer**
If disabled, returns. Calculates thread index via GUID hash. Acquires exclusive lock. Finds and erases `player` from `m_thread_players[index]` if present.

**Work**
Worker thread loop. While `!m_stop`: records start time, calls `BroadcastPackets` to get packet count, updates `ThreadUpdateStats` with time/packets. Logs if time exceeds `CONFIG_UINT32_PERFLOG_SLOW_PACKET_BCAST`. If time exceeds `CONFIG_UINT32_PBCAST_DIFF_LOWER_VISIBILITY_DISTANCE`, calls `IdentifySlowMap`; else sets `slow_instance` to -1. Sleeps for `m_sleep_timer`.

**IdentifySlowMap**
Acquires shared lock on `thread_id`'s mutex. Aggregates `lastUpdatePackets` per `instanceId` for players in `m_thread_players[thread_id]`. Returns the `instanceId` with the highest total packets.

**BroadcastPackets**
Acquires shared lock on `index`'s mutex. Copies `m_thread_players[index]` to local `my_players`. Releases lock. Iterates `my_players`, calling `ProcessQueue` on each, accumulating `num_packets`.

**Stop**
Logs stop message. Sets `m_stop` to true. Joins all threads in `m_threads`. Clears `m_threads`.

**UpdateConfiguration**
Updates `m_sleep_timer`. If `new_threads_count` equals `m_num_threads`, returns. Otherwise: calls `Stop`, updates `m_num_threads`, calls `StartThreads`. Iterates `sWorld.GetAllSessions()`, retrieves each player's `PlayerBroadcaster`, and calls `RegisterPlayer`. Logs reconfiguration time.

**IsMapSlow**
Iterates `m_thread_update_stats`. If any stat's `slow_instance` matches `instanceId`, resets that stat's `slow_instance` to -1 and returns `true`. Returns `false` if not found.

---

<!-- machine-true, projected from graph.json -->

## Map — MovementBroadcaster

*Source:* MovementBroadcaster.cpp, MovementBroadcaster.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| MovementBroadcaster | ctor | Log.Main/Out | — | — |
| IsEnabled | method | — | WorldObject.Object/SendMovementMessageToSet | — |
| StartThreads | method | Errors/PrintStacktraceAndThrow | — | — |
| RegisterPlayer | method | ObjectGuid/GetRawValue, PlayerBroadcaster/GetGUID | Player.Main/CreatePacketBroadcaster | — |
| GetStats | method | — | ChatHandler.ChatCommands/HandlePBCastStatsCommand | — |
| GetSleepTimer | method | — | ChatHandler.ChatCommands/HandlePBCastSetThreadsCommand | — |
| GetNumThreads | method | — | ChatHandler.ChatCommands/HandlePBCastSetThreadsCommand | — |
| RemovePlayer | method | ObjectGuid/GetRawValue, PlayerBroadcaster/GetGUID | Player.Main/DeletePacketBroadcaster | — |
| Work | method | Log.Main/Out, shared_Util/getMSTime, World/getConfig#4, WorldTimer/getMSTimeDiffToNow | — | — |
| IdentifySlowMap | method | — | — | — |
| BroadcastPackets | method | PlayerBroadcaster/ProcessQueue | — | — |
| Stop | method | Log.Main/Out | — | — |
| UpdateConfiguration | method | Log.Main/Out, Player.Main/GetPacketBroadcaster, shared_Util/getMSTime, World/GetAllSessions, WorldSession.Main/GetPlayer, WorldTimer/getMSTimeDiffToNow | ChatHandler.ChatCommands/HandlePBCastSetThreadsCommand, World/LoadConfigSettings | — |
| ~MovementBroadcaster | dtor | — | — | — |
| IsMapSlow | method | — | Map.Main/Update#3 | — |
