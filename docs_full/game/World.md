<!-- provenance: failed-members -->
# World

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# World

## Purpose & Responsibilities

The `World` class is the central singleton instance (`sWorld`) representing the entire game server environment in the MaNGOS/WowVMaNGOS emulator. It acts as the global state manager and coordinator for the server's lifecycle, configuration, session management, and periodic updates.

Key responsibilities include:
1.  **Server Lifecycle Management:** Handling initialization (`SetInitialWorldSettings`), graceful shutdowns (`Shutdown`, `ShutdownServ`), and emergency stops (`StopNow`).
2.  **Session Management:** Tracking all connected `WorldSession` objects, managing the login queue (`AddQueuedSession`, `RemoveQueuedSession`), and handling session creation/destruction.
3.  **Configuration Hub:** Loading, validating, and providing access to thousands of server-side configuration options (rates, limits, paths, anti-cheat settings) via `getConfig`/`setConfig` methods.
4.  **Global State & Time:** Maintaining the server's internal clock (`GetGameTime`, `GetGameDay`), uptime statistics, and patch version information.
5.  **Broadcasting & Messaging:** Providing mechanisms to send system messages, announcements, and GM notifications to all players or specific subsets (zones, battlegrounds, security levels).
6.  **Anti-Cheat & Moderation:** Coordinating with the Warden anti-cheat system, managing bans/warnings (`BanAccount`, `WarnAccount`), and logging transactions/chat.
7.  **Periodic Updates:** Driving the main server loop (`Update`) which triggers updates for maps, auctions, corpses, game events, and async tasks.

## Member-by-Member Behavior

### Initialization & Lifecycle

*   **`World` (ctor)**: Initializes member variables, resets configuration arrays to zero/false, and sets default values for visibility distances, time rates, and patch versions.
*   **`~World` (dtor)**: Cleans up resources by deleting all active sessions, joining worker threads (character DB, LFG queue, async packets), and clearing VMap memory via `VMapFactory/clear`.
*   **`SetInitialWorldSettings`**: The massive startup routine. It loads configuration, verifies map files, initializes databases, loads all game data (spells, items, creatures, quests, scripts, etc.), starts worker threads, and inserts the initial uptime record into the `uptime` table. It touches `corpse`, `ip_banned`, `realmlist`, and `uptime` tables.
*   **`Shutdown`**: Initiates a graceful shutdown. It deletes all player bots via `ChatHandler.PlayerBotMgr/DeleteAll`, kicks all players, updates sessions to ensure saves, joins worker threads, and stops the Warden update thread via `Anticheat/StopWardenUpdateThread`. Called by `WorldRunnable/operator()`.
*   **`StopNow`**: Immediately sets the global `m_stopEvent` flag to true, signaling the main loop to terminate. Sets the exit code. Called by various handlers like `ChatHandler.ServerCommands/HandleServerExitCommand` and signal handlers.
*   **`IsStopped`**: Returns the status of `m_stopEvent`. Used extensively by update loops and background threads to know when to terminate.
*   **`GetExitCode`**: Returns the static `m_ExitCode` set during shutdown.

### Session Management

*   **`FindSession`**: Looks up a `WorldSession` by `accountId` in the `m_sessions` map. Returns `nullptr` if not found. Called by many modules needing to interact with a specific player's session.
*   **`AddSession`**: Adds a new session to the asynchronous `addSessQueue`. Called by `WorldSocket/_HandleAuthSession` upon successful authentication.
*   **`AddSession_`**: The internal method that actually processes the session addition. It checks for duplicate logins (kicking the old one), handles queueing if the server is full, sends the `AUTH_OK` packet, initializes Warden, and updates the `realmlist` population statistic.
*   **`RemoveSession`**: Finds a session by ID and kicks the player. It returns `false` if the player is currently loading (to prevent iterator invalidation issues). Called by `WorldSession/Main/KickPlayer` and `WorldSession/Main/PlayerLoading`.
*   **`AddSessionToSessionsMap`**: Directly inserts a session into the `m_sessions` map. Called by `WorldSession/Main/GetAccountId` contextually (likely for re-authentication flows).
*   **`GetAllSessions`**: Returns a copy of the `m_sessions` map. Called by `MovementBroadcaster/UpdateConfiguration`.
*   **`GetActiveSessionCount`**: Returns the number of non-queued sessions. Used for population calculations and spawn logic.
*   **`GetQueuedSessionCount`**: Returns the number of sessions waiting in the login queue.
*   **`GetMaxActiveSessionCount`** / **`GetMaxQueuedSessionCount`**: Return the peak counts recorded since server start.
*   **`GetPlayerAmountLimit`**: Returns the configured soft player limit.
*   **`GetPlayerSecurityLimit`**: Returns the security level required to bypass the player limit (if negative limit is set).
*   **`SetSessionDisconnected`**: Moves a session from the active `m_sessions` map to the `m_disconnectedSessions` set, recording play history. This is called by `WorldSession.Main/SetDisconnectedSession` when a network disconnect occurs but the session object remains temporarily valid.
*   **`UpdateMaxSessionCounters`**: Updates the internal peak counters (`m_maxActiveSessionCount` and `m_maxQueuedSessionCount`) based on the current number of active and queued sessions. Called internally by `AddSession_` and `RemoveQueuedSession` to track historical highs.

### Login Queue

*   **`AddQueuedSession`**: Adds a session to the `m_QueuedSessions` list, sets its queue status, and sends an `AUTH_WAIT_QUEUE` packet with the current position.
*   **`RemoveQueuedSession`**: Removes a session from the queue. If the queue is not empty and the server has capacity, it promotes the next session in line, sending them an `AUTH_OK` packet and initializing Warden.
*   **`GetQueuedSessionPos`**: Calculates the 1-based index of a session in the queue.
*   **`CanSkipQueue`**: Determines if a high-security account or an account within the grace period after logout can bypass the queue.

### Configuration

*   **`LoadConfigSettings`**: Reads the `mangosd.conf` file. It validates the config version, loads hundreds of settings into internal arrays (`m_configUint32Values`, etc.), and configures subsystems like VMaps, MMaps, and Anti-Cheat. It supports hot-reloading (`reload=true`). Called by `ChatHandler.ServerCommands/HandleReloadConfigCommand`.
*   **`getConfig` / `setConfig`**: Generic getters/setters for configuration values by enum index. There are overloads for `uint32`, `int32`, `float`, and `bool`.
    *   **`getConfig`**: Retrieves boolean config values.
    *   **`setConfig#2`**: Sets boolean config values.
    *   **`getConfig#2`**: Retrieves `uint32` config values.
    *   **`setConfig#4`**: Sets `uint32` config values.
    *   **`getConfig#3`**: Retrieves `float` config values.
    *   **`setConfig#6`**: Sets `float` config values.
    *   **`getConfig#4`**: Retrieves `int32` config values.
    *   **`setConfig#8`**: Sets `int32` config values.
*   **`setConfigPos`**, **`setConfigPos#2`**: Helper methods that read a config value, validate it is positive, log errors if invalid, and clamp the value. `setConfigPos` handles `uint32`, `setConfigPos#2` handles `float`.
*   **`setConfigMin`**, **`setConfigMin#2`**, **`setConfigMin#3`**: Helper methods that validate a config value against a minimum constraint. `setConfigMin` handles `uint32`, `setConfigMin#2` handles `int32`, and `setConfigMin#3` handles `float`.
*   **`setConfigMinMax`**, **`setConfigMinMax#2`**, **`setConfigMinMax#3`**: Helper methods that validate a config value against both minimum and maximum constraints. `setConfigMinMax` handles `uint32`, `setConfigMinMax#2` handles `int32`, and `setConfigMinMax#3` handles `float`.
*   **`configNoReload`**, **`configNoReload#2`**, **`configNoReload#3`**, **`configNoReload#4`**: Checks if a config value has changed during a reload. If it has, it logs an error stating the value cannot be changed dynamically and reverts to the old value. `configNoReload` handles `bool`, `configNoReload#2` handles `float`, `configNoReload#3` handles `int32`, and `configNoReload#4` handles `uint32`.
*   **`setConfig#3`**: Private helper that reads a `float` config value from the file using `Config/GetFloatDefault` and stores it in the internal array.
*   **`setConfig#5`**: Private helper that reads a `uint32` config value from the file using `Config/GetIntDefault` and stores it in the internal array.
*   **`setConfig#7`**: Private helper that reads a `uint32` config value from the file using `Config/GetIntDefault` and stores it in the internal array.

### Time & State

*   **`GetGameTime`**: Returns the current server time in seconds since epoch. Used for respawn timers, cooldowns, and event scheduling.
*   **`GetGameDay`**: Returns the current day index (0-6) based on server time and timezone offset. Used for honor maintenance and daily resets.
*   **`GetStartTime`**: Returns the timestamp when the server started.
*   **`GetUptime`**: Returns the difference between current game time and start time.
*   **`GetLastMaintenanceDay`**: Calculates the most recent day of the week that matched the configured maintenance day.
*   **`GetCurrentMSTime`** / **`GetCurrentClockTime`** / **`GetCurrentDiff`**: Static accessors for the current millisecond time, high-resolution clock time, and the delta time since the last update tick. Used heavily by movement and anti-cheat systems.
*   **`GetWowPatch`**: Returns the configured WoW patch version (e.g., 1.12). Used by numerous systems to adjust mechanics (loot, spells, visibility) based on the era.
*   **`GetPatchName`**: Returns a human-readable string for the current patch.
*   **`GetDelayUntilNextSpellBatchingInterval`**: Calculates the remaining milliseconds until the next spell batching interval, used by `Spell.Main/GetSpellBatchingEffectDelay` and `Unit.Main/DelayAutoAttacks` to synchronize spell effects.

### Visibility & Distance

*   **`GetMaxVisibleDistanceOnContinents`** / **`InInstances`** / **`InBG`** / **`InFlight`**: Return the configured maximum distance at which objects are visible in different contexts.
*   **`GetVisibleUnitGreyDistance`** / **`GetVisibleObjectGreyDistance`**: Return the distance at which units/objects turn grey (non-interactable but visible).
*   **`GetRelocationLowerLimitSq`** / **`GetRelocationAINotifyDelay`**: Return thresholds for AI notification on player relocation.

### Broadcasting & Messaging

*   **`SendGlobalMessage`**: Sends a raw `WorldPacket` to all players in the world (optionally filtered by team or excluding a specific session).
*   **`SendWorldText`**: Sends a localized system message (from `mangos_string` table) to all players. Uses `WorldWorldTextBuilder` to handle localization and formatting.
*   **`SendBroadcastTextToWorld`**: Sends a broadcast text (from `broadcast_text` table) to all players.
*   **`SendWorldTextToBGAndQueue`**: Sends a message only to players in a specific Battleground or its queue, plus all GMs.
*   **`SendGMTicketText`**: Sends a message only to GMs who have ticket notifications enabled.
*   **`SendGMTicketText#2`**: Overload for sending formatted GMTicket text using string IDs.
*   **`SendGMText`**: Sends a message to all GMs.
*   **`SendGlobalText`**: Sends a raw text string as a system message to all players (deprecated/debug).
*   **`SendZoneMessage`** / **`SendZoneText`**: Sends packets or text only to players in a specific zone.
*   **`SendServerMessage`**: Sends a standard server message (shutdown warning, restart notice) to all players or a specific player.
*   **`InvalidatePlayerDataToAllClient`**: Sends a packet to all clients forcing them to refresh data for a specific player GUID. Called by `Player.Main/ChangeRace` and `WorldSession.CharacterHandler/HandleChangePlayerNameOpcodeCallBack`.
*   **`WorldWorldTextBuilder`**: Helper class constructor for building localized world text packets.
*   **`operator()#2`**: The functor operator for `WorldWorldTextBuilder`, which retrieves the string from `ObjectMgr/GetMangosString` and formats it.
*   **`lineFromMessage`**: Helper method in `WorldWorldTextBuilder` that splits a message string by newlines.
*   **`do_helper`**: Helper method in `WorldWorldTextBuilder` that builds `WorldPacket`s for each line of the message using `ChatHandler.Chat/BuildChatPacket`.
*   **`WorldBroadcastTextBuilder`**: Helper class constructor for building broadcast text packets.
*   **`operator()`**: The functor operator for `WorldBroadcastTextBuilder`, which retrieves the text from `ObjectMgr/GetBroadcastText` and builds the packet.
*   **`operator()#3`**: The functor operator for the `SessionPacketSendTask` struct. It looks up the session by account ID and sends the stored packet via `WorldSession.Main/SendPacket`.

### Moderation & Bans

*   **`WarnAccount`**: Inserts a temporary, inactive ban record into `account_banned` to serve as a warning log.
*   **`BanAccount#2`**: Bans a specific account ID. Inserts into `account_banned`, updates `AccountMgr` memory state, and kicks the player if online.
*   **`BanAccount`**: Bans by name, IP, or character. Uses an async query holder (`BanQueryHolder`) to resolve the ID, then applies the ban. Touches `account`, `characters`, `ip_banned`, and `account_banned` tables.
*   **`RemoveBanAccount`**: Removes a ban from `account_banned` or `ip_banned` and updates memory state.
*   **`KickAll`**: Kicks all connected players, saving their data.
*   **`KickAllLess`**: Kicks all players with a security level below the specified threshold.
*   **`BanQueryHolder`**: A helper class inheriting from `SqlQueryHolder` used to pass ban parameters asynchronously.
*   **`GetBanMode`**: Getter in `BanQueryHolder` returning the ban mode (IP, Account, Character).
*   **`GetDuration`**: Getter in `BanQueryHolder` returning the ban duration.
*   **`GetReason`**: Getter in `BanQueryHolder` returning the ban reason string.
*   **`GetRealmId`**: Getter in `BanQueryHolder` returning the realm ID.
*   **`GetAuthor`**: Getter in `BanQueryHolder` returning the author of the ban.
*   **`GetBanTarget`**: Getter in `BanQueryHolder` returning the target name/IP.
*   **`GetAuthorAccountId`**: Getter in `BanQueryHolder` returning the account ID of the author.
*   **`HandleAccountSelectResult`**: Callback method in the global `banHandler` instance. It processes the result of the async ban query, inserts the ban into the database, updates memory, and kicks affected players.

### Logging

*   **`LogMoneyTrade`**: Logs money transactions to the `logs_trade` table if enabled.
*   **`LogTransaction`**: Logs complex transactions (auctions, trades, mail) to the `logs_transactions` table.
*   **`LogChat`**: Logs chat messages to the smartlog system (not directly to DB in this method, but prepares the log entry).
*   **`InsertLog`**: Stores a message in the internal `m_logMessages` map with a security level, returning a unique key. Called by `WorldSession.MailHandler/HandleSendMailCallback`.
*   **`GetLog`**: Retrieves a previously inserted log message by key, enforcing security level checks. Called by `ChatHandler.ServerCommands/HandleViewLogCommand`.

### Async Tasks & Threads

*   **`AddAsyncTask`**: Adds a lambda/function to the `_asyncTasks` queue. These tasks are executed in parallel during the map update phase, allowing safe access to sessions without blocking the main thread.
*   **`ProcessAsyncPackets`**: Runs in a dedicated thread. Processes pending packets for sessions when the main session update is not running, improving latency.
*   **`UpdateResultQueue`**: Processes results from asynchronous database queries.
*   **`UpdateRealmCharCount`**: Asynchronously updates the character count for an account in the `realmcharacters` table (implied by `_UpdateRealmCharCount` callback).
*   **`CharactersDatabaseWorkerThread`**: A standalone function that runs a background thread to periodically clean up old characters and mails.
*   **`ProcessCliCommands`**: Processes queued CLI commands from the `cliCmdQueue`. It creates a `CliHandler` for each command and executes it. Called by the main `Update` loop.

### Timer Management

*   **`SetWorldUpdateTimer`**: Manually overrides the current value of a specific world timer (e.g., auctions, corpses). Called by zone scripts like `silithus/HandleWarStage`.
*   **`GetWorldUpdateTimer`**: Retrieves the current value of a specific world timer.
*   **`GetWorldUpdateTimerInterval`**: Retrieves the configured interval for a specific world timer.

### Shutdown Control

*   **`ShutdownServ`**: Schedules a shutdown or restart after a specified time. Sets `m_ShutdownTimer` and `m_ShutdownMask`.
*   **`ShutdownMsg`**: Displays countdown messages to players at specific intervals (every 5s, 1m, 5m, 1h, 12h).
*   **`ShutdownCancel`**: Cancels a scheduled shutdown.
*   **`IsShutdowning`**: Returns true if a shutdown is scheduled.

## Cross-Unit Boundaries

*   **Calls Out:**
    *   **`VMapFactory/clear`**: Called in destructor to release collision data.
    *   **`Anticheat/GetAnticheatLib`, `Anticheat/StopWardenUpdateThread`**: Integrates with the Warden anti-cheat module.
    *   **`ChatHandler.PlayerBotMgr/DeleteAll`**: Cleans up bot entities during shutdown.
    *   **`WorldSession.Main/*`**: Extensively interacts with sessions to kick, send packets, get account IDs, and initialize Warden.
    *   **`Database/*`**: Executes SQL statements for bans, uptime, and realm list updates.
    *   **`Log.Main/Out`**: Logs server events.
    *   **`ObjectMgr/*`**: Retrieves game data (strings, broadcast texts) for broadcasting.
    *   **`AccountMgr/*`**: Updates in-memory ban states.
    *   **`MapManager/*`**: Updates map state and grid cleanup delays.
    *   **`MovementBroadcaster/*`**: Updates packet broadcasting configuration.
    *   **`ChatHandler.Chat/*`**: Used for building chat packets and parsing CLI commands.
    *   **`CliHandler/*`**: Used to execute queued CLI commands.

*   **Called By:**
    *   **`WorldRunnable/operator()`**: The main server loop calls `Update`, `Shutdown`, and `InitResultQueue`.
    *   **`ChatHandler.*`**: Various command handlers call `FindSession`, `SetPlayerLimit`, `ShutdownServ`, `LoadConfigSettings`, etc.
    *   **`WorldSocket/_HandleAuthSession`**: Calls `AddSession` and `GetPlayerSecurityLimit`.
    *   **`Master/*`**: Signal handlers and run loops call `StopNow`, `IsStopped`, `GetExitCode`.
    *   **`AsyncCommandHandlers/*`**: Async DB callbacks call `FindSession`.
    *   **`MovementAnticheat/*`**: Calls `GetCurrentDiff`, `GetCurrentMSTime`, and various config getters.
    *   **`Player.Main/*`**: Calls `GetGameTime`, `GetWowPatch`, `getConfig` for rates and limits.

## Data Model

The `World` unit interacts with several database tables:

*   **`realmlist`**:
    *   Updated via `AddSession_` to reflect `population` (ratio of active sessions to limit).
    *   Updated via `SetPlayerLimit` to reflect `allowedSecurityLevel`.
    *   Updated via `SetInitialWorldSettings` to reflect `icon` and `timezone`.
*   **`uptime`**:
    *   Inserted into during `SetInitialWorldSettings` to record server start.
    *   Updated periodically in `Update` to record `uptime`, `onlineplayers`, and `maxplayers`.
*   **`account_banned`**:
    *   Inserted into by `WarnAccount`, `BanAccount`, and `HandleAccountSelectResult` (via async handler).
    *   Updated by `RemoveBanAccount` to deactivate bans.
*   **`ip_banned`**:
    *   Inserted into by `BanAccount` (IP mode).
    *   Deleted from by `RemoveBanAccount` (IP mode).
    *   Cleaned up in `SetInitialWorldSettings` to remove expired bans.
*   **`corpse`**:
    *   Cleaned up in `SetInitialWorldSettings` to remove old/invalid corpses.
*   **`logs_trade`**:
    *   Inserted into by `LogMoneyTrade`.
*   **`logs_transactions`**:
    *   Inserted into by `LogTransaction`.
*   **`characters`**:
    *   Queried indirectly via `UpdateRealmCharCount` callback to count characters per account.

## Notable Implementation Details

*   **Singleton Pattern**: `World` is instantiated as a singleton using `INSTANTIATE_SINGLETON_1(World)` and accessed via `sWorld`.
*   **Thread Safety**:
    *   `m_sessions` is accessed primarily from the main thread, but `AddSession` uses a `LockedQueue` (`addSessQueue`) to safely pass sessions from the socket thread to the main thread.
    *   `m_QueuedSessions` is modified in `UpdateSessions` and `RemoveQueuedSession`. Care is taken to avoid iterator invalidation.
    *   `m_asyncPacketsThread` runs independently, guarded by `m_asyncPacketsMutex` and `m_canProcessAsyncPackets` flag to prevent concurrent access to session packet buffers during the main update.
*   **Configuration Validation**: The `setConfig*` helpers enforce constraints at load time. If a config value is out of bounds, it is clamped, and an error is logged. This prevents runtime crashes due to invalid rates or limits.
*   **Graceful Shutdown**: The shutdown process is multi-stage. `ShutdownServ` sets a timer. `_UpdateGameTime` decrements it. When it hits zero, `m_stopEvent` is set. The main loop detects this and calls `Shutdown`, which ensures all players are saved and kicked before terminating threads.
*   **Anti-Cheat Integration**: `World` holds the configuration for extensive movement and Warden anti-cheat checks. It provides the thresholds and penalties, but the actual checking is done by `MovementAnticheat` and `Anticheat` units.
*   **Visibility Distances**: Visibility distances are static members, allowing fast access without locking. They are calculated once during config load based on aggro ranges and max visibility limits.
*   **Patch Versioning**: `GetWowPatch` is critical for backward compatibility. Many systems (loot, spells, visibility) branch logic based on whether the server is running 1.12, 1.10, etc.

## Member Reference

*   **GetSWorld**: Function returning the singleton `World` instance.
*   **World**: Constructor initializing member variables and config arrays.
*   **~World**: Destructor cleaning up sessions, threads, and VMaps.
*   **Shutdown**: Method initiating graceful shutdown, kicking players, and stopping threads.
*   **FindSession**: Method looking up a session by account ID.
*   **RemoveSession**: Method kicking a player and removing their session.
*   **AddSession**: Method adding a session to the async queue.
*   **AddSessionToSessionsMap**: Method inserting a session into the session map.
*   **AddSession_**: Internal method processing session addition, queueing, and auth response.
*   **GetQueuedSessionPos**: Method calculating a session's position in the login queue.
*   **AddQueuedSession**: Method adding a session to the queue and notifying the client.
*   **RemoveQueuedSession**: Method removing a session from the queue and promoting the next one.
*   **LoadConfigSettings**: Method loading and validating server configuration from file.
*   **GetAllSessions**: Method returning a copy of all active sessions.
*   **GetActiveAndQueuedSessionCount**: Method returning total session count.
*   **GetActiveSessionCount**: Method returning non-queued session count.
*   **GetQueuedSessionCount**: Method returning queued session count.
*   **GetMaxQueuedSessionCount**: Method returning peak queued session count.
*   **GetMaxActiveSessionCount**: Method returning peak active session count.
*   **GetPlayerAmountLimit**: Method returning the soft player limit.
*   **GetPlayerSecurityLimit**: Method returning the security level to bypass limits.
*   **SetMotd**: Method setting the Message of the Day.
*   **GetMotd**: Method retrieving the Message of the Day.
*   **GetWowPatch**: Method retrieving the configured WoW patch version.
*   **GetDefaultDbcLocale**: Method retrieving the default DBC locale.
*   **GetDataPath**: Method retrieving the path to game data files.
*   **GetHonorPath**: Method retrieving the path to honor logs.
*   **GetStartTime**: Method retrieving the server start timestamp.
*   **GetGameTime**: Method retrieving the current server time.
*   **GetGameDay**: Method retrieving the current server day index.
*   **GetUptime**: Method retrieving the server uptime in seconds.

---

<!-- machine-true, projected from graph.json -->

## Map — World

*Source:* World.cpp, World.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GetSWorld | function | — | — | — |
| World | ctor | — | — | — |
| ~World | dtor | VMapFactory/clear | — | — |
| Shutdown | method | Anticheat/GetAnticheatLib, Anticheat/StopWardenUpdateThread, ChatHandler.PlayerBotMgr/DeleteAll | WorldRunnable/operator() | — |
| FindSession | method | — | AsyncCommandHandlers/HandleAccountInfoResult, AsyncCommandHandlers/HandleGoldLookupResult, AsyncCommandHandlers/operator(), AsyncCommandHandlers/operator()#2, AsyncCommandHandlers/operator()#3, AsyncCommandHandlers/ShowAccountListHelper, ChatHandler.AccountCommands/HandleMuteCommand, ChatHandler.AccountCommands/HandleUnmuteCommand, ChatHandler.Chat/ParseCommands, ChatHandler.PlayerBotMgr/AddBot#2, ChatHandler.PlayerBotMgr/Update, game_Group_Group/UpdateOfflineLeader, Log.Warden/KickSession, Log.Warden/SendPacket, WardenMac/SetCharEnumPacket, WardenMac/Update, WardenWin/SetCharEnumPacket, WardenWin/Update, WorldSession.AuctionHouseHandler/operator(), WorldSession.CharacterHandler/HandleChangePlayerNameOpcodeCallBack, WorldSession.CharacterHandler/HandleCharEnumCallback, WorldSession.CharacterHandler/HandlePlayerLoginCallback, WorldSession.MailHandler/Callback, WorldSession.MiscHandler/operator(), WorldSession.QueryHandler/SendNameQueryOpcodeFromDBCallBack | — |
| RemoveSession | method | WorldSession.Main/KickPlayer, WorldSession.Main/PlayerLoading | — | — |
| AddSession | method | — | ChatHandler.PlayerBotMgr/AddBot#2, WorldSocket/_HandleAuthSession | — |
| AddSessionToSessionsMap | method | WorldSession.Main/GetAccountId | — | — |
| AddSession_ | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#7, Database/CreateStatement, Errors/PrintStacktraceAndThrow, Log.Main/Out, SqlStatementID/SqlStatementID, WorldPacket/WorldPacket#4, WorldSession.Main/ForcePlayerLogoutDelay, WorldSession.Main/GetAccountId, WorldSession.Main/GetConsecutivePlayTime, WorldSession.Main/HasTrialRestrictions, WorldSession.Main/InitWarden, WorldSession.Main/KickPlayer, WorldSession.Main/SendPacket, WorldSession.Main/SetPreviousPlayedTime | — | realmlist |
| GetQueuedSessionPos | method | — | — | — |
| AddQueuedSession | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#7, WorldPacket/WorldPacket#4, WorldSession.Main/HasTrialRestrictions, WorldSession.Main/SendPacket, WorldSession.Main/SetInQueue | — | — |
| RemoveQueuedSession | method | shared_Util/getMSTime, WorldSession.Main/InitWarden, WorldSession.Main/SendAuthWaitQue, WorldSession.Main/SetInQueue | — | — |
| LoadConfigSettings | method | ChatHandler.PlayerBotMgr/LoadConfig, Config/GetBoolDefault, Config/GetFilename, Config/GetFloatDefault, Config/GetIntDefault, Config/GetStringDefault, Config/Reload, IntervalTimer/Reset, IntervalTimer/SetInterval, IVMapManager/setEnableHeightCalc, IVMapManager/setEnableLineOfSightCalc, IVMapManager/setUseManagedPtrs, Log.Main/InitSmartlogEntries, Log.Main/InitSmartlogGuids, Log.Main/Out, Log.Main/WaitBeforeContinueIfNeed, MapManager/SetGridCleanUpDelay, MapManager/SetMapUpdateInterval, MovementAnticheat/InitWallClimbLimits, MovementBroadcaster/UpdateConfiguration, VMapFactory/createOrGetVMapManager | ChatHandler.ServerCommands/HandleReloadConfigCommand | — |
| GetAllSessions | method | — | MovementBroadcaster/UpdateConfiguration | — |
| GetActiveAndQueuedSessionCount | method | — | — | — |
| GetActiveSessionCount | method | — | burning_steppes/JustDied, ChatHandler.PlayerBotMgr/HandleBotInfoCommand, ChatHandler.ServerCommands/HandleServerInfoCommand, Creature.Main/SetDeathState, Creature.Main/UpdateVendorItemCurrentCount, GameObject/ComputeRespawnDelay#2, PoolManager/CanBeSpawned, PoolManager/GetSpawnCount, silithus/JustDied#4, ungoro_crater/JustDied, winterspring/JustDied | — |
| GetQueuedSessionCount | method | — | ChatHandler.ServerCommands/HandleServerInfoCommand | — |
| GetMaxQueuedSessionCount | method | — | ChatHandler.ServerCommands/HandleServerInfoCommand | — |
| GetMaxActiveSessionCount | method | — | ChatHandler.ServerCommands/HandleServerInfoCommand | — |
| GetPlayerAmountLimit | method | — | ChatHandler.ServerCommands/HandleServerPLimitCommand | — |
| GetPlayerSecurityLimit | method | — | ChatHandler.ServerCommands/HandleServerPLimitCommand, WorldSocket/_HandleAuthSession | — |
| SetMotd | method | — | ChatHandler.ServerCommands/HandleServerSetMotdCommand | — |
| GetMotd | method | — | ChatHandler.ServerCommands/HandleServerMotdCommand, RASocket/Start, WorldSession.CharacterHandler/HandlePlayerLogin | — |
| GetWowPatch | method | — | AuctionHouseMgr/LoadAuctionHouses, BattleGroundAB/Reset, BattleGroundAV/EndBattleGround, BattleGroundAV/initializeChallengeInvocationGoals, BattleGroundMgr/CreateInitialBattleGrounds, BattleGroundWS/Reset, boss_fankriss/UpdateAI#2, boss_jandice_barov/JustDied, boss_majordomo_executus/DomoEvent, boss_marli/Reset, boss_ouro/SandBlastTimerMax, boss_ouro/SandBlastTimerMin, boss_ouro/SubmergeTimer, boss_razorgore/UpdateAI, boss_skeram/Aggro, boss_tendris_warpwood/Aggro, boss_vaelastrasz/Aggro, Conditions/Evaluate, GameEventMgr.Main/LoadFromDB, game_Group_Group/RewardGroupAtKill_helper, GuardMgr/GuardMgr, HonorMgr/GenerateScores, instance_ruins_of_ahnqiraj/GiveRepAfterRajaxxDeath, instance_ruins_of_ahnqiraj/OnCreatureEnterCombat, instance_ruins_of_ahnqiraj/SetData, instance_ruins_of_ahnqiraj/Update, ItemEnchantmentMgr/LoadRandomEnchantmentsTable, LootMgr/LoadLootTable, ObjectMgr/CorrectCreatureDisplayIds, ObjectMgr/CorrectItemDisplayIds, ObjectMgr/CorrectItemEffects, ObjectMgr/LoadAreaTriggerTeleports, ObjectMgr/LoadCreatureAddons#2, ObjectMgr/LoadCreatures, ObjectMgr/LoadCreatureTemplate, ObjectMgr/LoadCreatureTemplates, ObjectMgr/LoadEquipmentTemplates, ObjectMgr/LoadGameobjects, ObjectMgr/LoadGameObjectTemplate, ObjectMgr/LoadGameObjectTemplates, ObjectMgr/LoadGraveyardZones, ObjectMgr/LoadItemPrototypes, ObjectMgr/LoadMapTemplate, ObjectMgr/LoadPetCreateSpells, ObjectMgr/LoadQuestRelationsHelper, ObjectMgr/LoadQuests, ObjectMgr/LoadReputationOnKill, ObjectMgr/LoadTavernAreaTriggers, ObjectMgr/LoadTrainerTemplates, ObjectMgr/LoadVendors#2, ObjectMgr/LoadVendorTemplates, Pet.Main/InitStatsForLevel, Player.Main/BuyItemFromVendor, Player.Main/CanUseItem#2, Player.Main/GetResetTalentsCost, Player.Main/LoadFromDB, Player.Main/RewardHonor, Player.Main/RewardHonorOnDeath, Player.Main/RewardReputation#2, Player.Main/SatisfyItemRequirements, PoolManager/LoadFromDB, QuestDef/GetRewMoneyMaxLevelAtComplete, quest_stormwind_rendezvous/AreaTrigger_at_stormwind_gates, Register/RegisterZoneScripts, Spell.Effects/EffectSanctuary, Unit.Main/UpdateSpeed, WorldSession.MiscHandler/HandleAreaTriggerOpcode, zulfarrak/OnTrigger_at_antusul | — |
| GetDefaultDbcLocale | method | — | ChatHandler.Chat/GetSessionDbcLocale#2, ChatHandler.Chat/GetSessionDbcLocale#3, ChatHandler.UnitCommands/HandleGPSCommand, custom_creatures/LearnAllRecipesInProfession, ObjectMgr/GeneratePetName, Pet.Main/InitializeDefaultName | — |
| GetDataPath | method | — | GameObjectModel/initialize, GameObjectModel/LoadGameObjectModelList, GridMap/ExistMap, GridMap/ExistVMap, GridMap/LoadMapAndVMap, MoveMap/loadGameObject, MoveMap/loadMap, MoveMap/loadMapData | — |
| GetHonorPath | method | — | HonorMgr/CreateCalculationReport | — |
| GetStartTime | method | — | — | — |
| GetGameTime | method | — | AuctionHouseMgr/Update#2, ChatHandler.PlayerBotMgr/Update, GameObject/Use, instance_zulgurub/CheckConditionCriteriaMeet, instance_zulgurub/SetData, Map.Main/ScriptCommandStart, Map.Main/ScriptsProcess, Map.Main/ScriptsStart, Map.Main/StartAreaTriggerScript, Map.Main/StartScriptedEvent, Map.Main/UpdateEvent, MapPersistentStateMgr/SaveCreatureRespawnTime, MapPersistentStateMgr/SaveGORespawnTime, MapPersistentStateMgr/SetCreatureRespawnTime, MapPersistentStateMgr/SetGORespawnTime, Pet.Main/GetResetTalentsCost, Player.Main/SendInitialPacketsBeforeAddToMap, Player.Main/UpdateResetTalentsMultiplier, Player.Main/_LoadQuestStatus, Player.Main/_SaveQuestStatus, quest_stormwind_rendezvous/AreaTrigger_at_stormwind_gates, SpellCaster/ProcSystemArguments, Weather/ReGenerate | — |
| GetGameDay | method | — | HonorMgr/Add, HonorMgr/CalculateTotalKills, HonorMgr/CheckMaintenanceDay, HonorMgr/DoMaintenance, HonorMgr/Update, instance_zulgurub/GenerateRandomBoss | — |
| GetUptime | method | — | ChatHandler.ServerCommands/HandleServerInfoCommand | — |
| GetLocalTimeByTime | method | — | — | — |
| GetLastMaintenanceDay | method | — | HonorMgr/Initialize | — |
| GetConfigMaxSkillValue | method | — | Conditions/IsValid, ObjectMgr/LoadQuests, Player.Main/UpdateSkillsForLevel, Spell.Main/CheckCast | — |
| IsShutdowning | method | — | WorldSession.CharacterHandler/HandlePlayerLogin | — |
| GetExitCode | method | — | Master/Run | — |
| StopNow | method | — | ChatHandler.ServerCommands/HandleServerExitCommand, CliRunnable/operator(), Master/Run, Master/_OnSignal, ObjectGuid/Generate, ObjectGuid/GenerateRange, ObjectMgr/Generate | — |
| IsStopped | method | — | Anticheat/UpdateWardenSessions, CliRunnable/operator(), LFGQueue/Update, MaNGOSsoap/SoapThreadBody, Map.Main/Remove#3, Master/freezeDetector, Player.Main/LeaveBattleground, WorldRunnable/operator(), WorldSession.Main/ForcePlayerLogoutDelay, WorldSession.Main/Update | — |
| setConfig#4 | method | — | — | — |
| getConfig#2 | method | — | AuctionHouseMgr/GetAuctionCut, AuctionHouseMgr/GetAuctionDeposit, ChatHandler.CharacterCommands/HandleModifyXpRateCommand, Creature.Main/AllLootRemovedFromCorpse, Creature.Main/ApplyDynamicRespawnDelay, Creature.Main/CallAssistance, Creature.Main/DoFleeToGetAssistance, Creature.Main/GetAttackDistance, Creature.Main/IsOutOfThreatArea, Creature.Main/IsVisibleInGridForPlayer, Creature.Main/RegenerateHealth, Creature.Main/_GetDamageMod, Creature.Main/_GetHealthMod, Creature.Main/_GetSpellDamageMod, HonorMgr/CalculateRpDecay, LootMgr/GenerateMoneyLoot, LootMgr/Roll, MovementAnticheat/InitWallClimbLimits, MovementAnticheat/IsTeleportAllowed3D, ObjectMgr/LoadMapTemplate, Pet.Main/ModifyLoyalty, Pet.Main/RegenerateFocus, Player.Main/CalculateReputationGain, Player.Main/CalculateTalentsPoints, Player.Main/CheckAreaExploreAndOutdoor, Player.Main/ComputeRest, Player.Main/GetYellRange, Player.Main/HandleFall, Player.Main/HandleStealthedUnitsDetection, Player.Main/Regenerate, Player.Main/RegenerateHealth, Player.Main/RewardQuest, Player.Main/RewardRage, Player.Main/Say, Player.Main/SendLoot, Player.Main/TextEmote, Player.StatSystem/UpdateManaRegen, QuestDef/GetRewMoneyMaxLevelAtComplete, QuestDef/GetRewOrReqMoney, Unit.Main/CanDetectStealthOf, Unit.Main/DealDamage, Unit.Main/Execute, Unit.Main/UpdateSpeed, WorldObject.Object/IsWithinLootXPDist, WorldObject.Object/MonsterSay, WorldObject.Object/MonsterSay#2, WorldObject.Object/MonsterTextEmote, WorldObject.Object/MonsterTextEmote#2, WorldObject.Object/MonsterYell, WorldObject.Object/MonsterYell#2, WorldObject.Object/PMonsterSay#2, WorldObject.Object/PMonsterYell#2, WorldSession.AuctionHouseHandler/HandleAuctionSellItem, WorldSession.ChatHandler/HandleTextEmoteOpcode, WorldSession.LootHandler/DoLootRelease, world_event_wareffort/AutoCompleteWarEffortProgress | — |
| setConfig#8 | method | — | — | — |
| getConfig#4 | method | — | AccountMgr/CanMail, AccountMgr/Update, AuctionHouseMgr/GetAuctionDeposit, AutoBroadCastMgr/AutoBroadCastMgr, BattleBotAI.Main/UpdateAI, BattleGroundMgr/AddGroup, BattleGroundMgr/CheckCreateNewBg, BattleGroundMgr/CheckNormalMatch, BattleGroundMgr/CheckPremadeMatch, BattleGroundMgr/FillPlayersToBg, BattleGroundMgr/GetPrematureFinishTime, ChatHandler.CharacterCommands/HandleCharacterDeletedOldCommand, ChatHandler.CharacterCommands/HandleResetLevelCommand, ChatHandler.CharacterCommands/HandleSaveCommand, ChatHandler.Chat/isValidChatMessage, ChatHandler.HardcodedEvents/HandleWarEffortInfoCommand, ChatHandler.HardcodedEvents/UpdateWarEffortCollection, ChatHandler.MiscCommands/HandleGMListIngameCommand, ChatHandler.PlayerBotMgr/HandleBattleBotAddCommand, ChatHandler.PlayerBotMgr/PartyBotAddRequirementCheck, ChatHandler.TicketCommands/HandleGMTicketAssignToCommand, CombatBotBaseAI/EquipRandomGearInEmptySlots, Conditions/IsValid, Corpse/IsExpired, Creature.Main/ApplyDynamicRespawnDelay, Creature.Main/CallAssistance, Creature.Main/Create, Creature.Main/DoFlee, Creature.Main/DoFleeToGetAssistance, Creature.Main/DynamicRespawnRatesChecker, Creature.Main/GetCombatTime, Creature.Main/Update, GameObject/AddToWorld, game_Chat_Channel/List, game_Chat_Channel/Say, game_Guild_Guild/LogGuildEvent, game_Mail_Mail/SendReturnToSender, GuildMgr/LoadGuilds, HonorMgr/GenerateScores, HonorMgr/LoadStandingLists, LFGQueue/Update, Log.Warden/ApplyPenalty, Log.Warden/BeginScanClock, Log.Warden/BeginTimeoutClock, Log.Warden/RequestScans, Map.Main/DungeonMap, Map.Main/Map, Map.Main/ProcessSessionPackets, Map.Main/Remove#2, Map.Main/SendObjectUpdates, Map.Main/Update#3, Map.Main/UpdateActiveCellsCallback, Map.Main/UpdateCells, Map.Main/UpdatePlayers, Map.Main/UpdateSessionsMovementAndSpellsIfNeeded, Map.Main/UpdateVisibilityForRelocations, MapManager/MapManager, MapManager/Update, MapPersistentStateMgr/CalculateNextResetTime, MapPersistentStateMgr/LoadResetTimes, MapPersistentStateMgr/ScheduleAllDungeonResets, MassMailMgr/GetStatistic, MassMailMgr/Update, Master/Run, Master/_OnSignal, MasterPlayer.Chat/UpdateSpeakTime, MovementAnticheat/AddCheats, MovementAnticheat/ComputeCheatAction, MovementAnticheat/HasEnoughBottingData, MovementAnticheat/LogMovementPacket, MovementBroadcaster/Work, ObjectGuid/LoadFromDB, ObjectMgr/BuildPlayerLevelInfo, ObjectMgr/CheckPetName, ObjectMgr/CheckPlayerName, ObjectMgr/GeneratePlayerName, ObjectMgr/GetPetLevelInfo, ObjectMgr/GetPlayerClassLevelInfo, ObjectMgr/GetPlayerLevelInfo, ObjectMgr/GetRealmLanguageType, ObjectMgr/IsValidCharterName, ObjectMgr/LoadPetLevelInfo, ObjectMgr/LoadPlayerInfo, ObjectMgr/ReturnOrDeleteOldMails, ObjectMgr/SetHighestGuids, packet_builder/WriteLinearPath, PartyBotAI/UpdateAI, Pet.Main/GivePetXP, Player.Main/AddGCD, Player.Main/CheckAreaExploreAndOutdoor, Player.Main/CheckInstanceCount, Player.Main/Create, Player.Main/DeleteFromDB, Player.Main/DeleteOldCharacters, Player.Main/GetMirrorTimerMaxDuration, Player.Main/GetResetTalentsCost, Player.Main/GetWaterBreathingInterval, Player.Main/GetYellRange, Player.Main/GiveXP, Player.Main/HasFreeBattleGroundQueueId, Player.Main/InitPrimaryProfessions, Player.Main/IsGroupVisibleFor, Player.Main/LoadFromDB, Player.Main/LogModifyMoney, Player.Main/OnDisconnected, Player.Main/OnMirrorTimerExpirationPulse, Player.Main/OnReceivedItem, Player.Main/Player#5, Player.Main/RemoveSpell, Player.Main/ResetTalents, Player.Main/RewardQuest, Player.Main/SaveNewPlayer, Player.Main/SaveToDB, Player.Main/SendQuestReward, Player.Main/SetPosition, Player.Main/SetRestBonus, Player.Main/SkillGainChance, Player.Main/Update, Player.Main/UpdateCombatSkills, Player.Main/UpdateCraftSkill, Player.Main/UpdateFishingSkill, Player.Main/UpdateGatherSkill, Player.Main/UpdateResetTalentsMultiplier, Player.Main/_SaveStats, PointMovementGenerator/Finalize, SocialMgr/BroadcastToFriendListers, SocialMgr/GetFriendInfo, Spell.Effects/EffectTameCreature, Spell.Main/GetSpellBatchingEffectDelay, SpellCaster/CheckAndIncreaseCastCounter, SpellCaster/GetLevelForTarget, SpellCaster/ProcDamageAndSpell, SpellCaster/UpdatePendingProcs, SpellMgr/AssignInternalSpellFlags, Unit.AuraProcHandler/HandleProcTriggerSpellAuraProc, Unit.Main/AddSpellAuraHolder, Unit.Main/AddToWorld, Unit.Main/CheckPendingMovementChanges, Unit.Main/DealDamage, Unit.Main/Update, Unit.SpellAuras/_AddSpellAuraHolder, UpdateData/BuildPacket#2, UpdateData/Compress, WardenScan/Scan, WardenScanMgr/GetRandomScans, WardenScanMgr/LoadFromDB, Weather/Weather, WorldObject.Object/GetCreatureSummonLimit, WorldObject.Object/GetSummonLimitForObject, WorldRunnable/operator(), WorldSession.AuctionHouseHandler/HandleAuctionSellItem, WorldSession.BattleGroundHandler/RequestBgJoinQueue, WorldSession.ChannelHandler/HandleChannelInviteOpcode, WorldSession.CharacterHandler/HandleCharCreateOpcode, WorldSession.ChatHandler/ChatCooldown, WorldSession.ChatHandler/HandleChatMessageOpcode, WorldSession.ChatHandler/SanitizeChatMessage, WorldSession.GMTicketHandler/HandleGMTicketCreateOpcode, WorldSession.MailHandler/HandleSendMail, WorldSession.MailHandler/HandleSendMailCallback, WorldSession.Main/AllowPacket, WorldSession.Main/CharacterScreenIdleKick, WorldSession.Main/ProcessPackets, WorldSession.Main/SendMovementPacket, WorldSession.Main/Update, WorldSession.MiscHandler/HandleLogoutRequestOpcode, WorldSession.MiscHandler/operator(), WorldSocket/_HandlePing | — |
| setConfig#6 | method | — | — | — |
| getConfig#3 | method | — | MovementAnticheat/CheckSpeedHack, Player.Main/CanSeeStartQuest, Player.Main/ResurrectPlayer, WorldSession.QuestHandler/GetDialogStatus | — |
| setConfig#2 | method | — | ChatHandler.DebugCommands/HandleMmap | — |
| getConfig | method | — | AccountMgr/HasTrialRestrictions, Anticheat/CreateWardenForInternal, AuctionHouseMgr/GetAuctionHouseEntry, AuctionHouseMgr/LoadAuctionHouses, AuctionHouseMgr/SendAuctionWonMail, BattleGroundMgr/CheckFreeSlots, BattleGroundWS/Reset, ChannelMgr/AnnounceBothFactionsChannel, ChannelMgr/channelMgr, CharacterDatabaseCleaner/CleanDatabase, ChatHandler.Chat/HasLowerSecurityAccount, ChatHandler.Chat/ParseCommands, ChatHandler.DebugCommands/HandleMmap, ChatHandler.DebugCommands/HandleMmapStatsCommand, ChatHandler.PlayerBotMgr/HandlePartyBotAddCommand, ChatHandler.PlayerBotMgr/LoadConfig, ChatHandler.PlayerBotMgr/PartyBotAddRequirementCheck, ChatHandler.ServerCommands/HandleChangeWeatherCommand, ChatHandler.UnitCommands/HandleDieHelper, Creature.Main/LogDeath, Creature.Main/LogLongCombat, Creature.Main/SetDeathState, Creature.Main/UpdateEntry, GameEventMgr.Main/ApplyNewEvent, GameObject/Create, GameObject/Update, GameObject/Use, game_Battlegrounds_BattleGround/EndBattleGround, game_Battlegrounds_BattleGround/Update, game_Chat_Channel/Invite, game_Chat_Channel/Join, game_Chat_Channel/Leave, game_Chat_Channel/Say, game_Chat_Channel/SetMode, game_Chat_Channel/SetOwner, game_Group_Group/CalculateLFGRoles, game_Group_Group/CanJoinBattleGroundQueue, GMTicketMgr/Initialize, GridMap/UnloadTerrain, HonorMgr/CheckMaintenanceDay, HonorMgr/DoMaintenance, LFGMgr/AddToQueue, LFGQueue/Update, Map.Main/EnsureGridCreated, Map.Main/LoadCreatureSpawn, Map.Main/LoadGameObjectSpawn, Map.Main/Remove#5, Map.Main/RemoveCorpses, MapManager/CanPlayerEnter, MapManager/GetContinentInstanceId, MapManager/Initialize, MapPersistentStateMgr/LoadCreatureRespawnTimes, MoveMap/loadMapData, MovementAnticheat/AddCheats, MovementAnticheat/CheckBotting, MovementAnticheat/CheckFakeTransport, MovementAnticheat/CheckFallReset, MovementAnticheat/CheckFallStop, MovementAnticheat/CheckForbiddenArea, MovementAnticheat/CheckMoveStart, MovementAnticheat/CheckMultiJump, MovementAnticheat/CheckNoFallTime, MovementAnticheat/CheckSpeedHack, MovementAnticheat/CheckTeleport, MovementAnticheat/CheckTeleportToTransport, MovementAnticheat/CheckWallClimb, MovementAnticheat/ComputeCheatAction, MovementAnticheat/Finalize, MovementAnticheat/HandleFlagTests, MovementAnticheat/HandlePositionTests, MovementAnticheat/OnExplore, MovementAnticheat/OnFailedToAckChange, MovementAnticheat/OnUnreachable, MovementAnticheat/OnWrongAckData, MovementAnticheat/ShouldRejectMovement, MovementAnticheat/Update, ObjectMgr/LoadItemPrototypes, Pet.Main/InitStatsForLevel, Player.Main/BuyItemFromVendor, Player.Main/CanUseItem#2, Player.Main/CheckAreaExploreAndOutdoor, Player.Main/DurabilityPointsLoss, Player.Main/GetCorpseReclaimDelay, Player.Main/GetResetTalentsCost, Player.Main/IsPetNeedBeTemporaryUnsummoned, Player.Main/IsPlayerLoggingEnabledToDB, Player.Main/LeaveBattleground, Player.Main/LoadFromDB, Player.Main/Mount, Player.Main/RewardHonor, Player.Main/RewardHonorOnDeath, Player.Main/SatisfyItemRequirements, Player.Main/SaveToDB, Player.Main/SendCorpseReclaimDelay, Player.Main/TextEmote, Player.Main/UpdateCorpseReclaimDelay, Player.Main/UpdateSkillsForLevel, Player.Main/UpdateSpellTrainedSkills, Player.Main/UpdateZone, PointMovementGenerator/ComputePath, PoolManager/LoadFromDB, PoolManager/Spawn1Object, PoolManager/Spawn1Object#2, QuestDef/GetRewMoneyMaxLevelAtComplete, QuestDef/IsAllowedInRaid, quest_stormwind_rendezvous/AreaTrigger_at_stormwind_gates, Register/RegisterZoneScripts, SocialMgr/BroadcastToFriendListers, SocialMgr/GetFriendInfo, Spell.Effects/EffectEnchantItemPerm, Spell.Effects/EffectEnchantItemTmp, Spell.Effects/EffectStuck, Spell.Main/CheckCast, TargetedMovementGenerator/_setTargetLocation, TargetedMovementGenerator/_setTargetLocation#2, Unit.Main/UpdateSpeed, WardenModuleMgr/WardenModuleMgr, WorldObject.Object/BuildValuesUpdate, WorldObject.Object/GetNearPointAroundPosition, WorldSession.AuctionHouseHandler/HandleAuctionPlaceBid, WorldSession.AuctionHouseHandler/HandleAuctionSellItem, WorldSession.ChannelHandler/HandleJoinChannelOpcode, WorldSession.ChannelHandler/HandleLeaveChannelOpcode, WorldSession.CharacterHandler/HandleCharCreateOpcode, WorldSession.CharacterHandler/HandlePlayerLogin, WorldSession.CharacterHandler/HandlePlayerLoginOpcode, WorldSession.ChatHandler/HandleChatMessageOpcode, WorldSession.ChatHandler/SanitizeChatMessage, WorldSession.GroupHandler/HandleGroupInviteOpcode, WorldSession.GuildHandler/HandleGuildAcceptOpcode, WorldSession.GuildHandler/HandleGuildInviteOpcode, WorldSession.MailHandler/HandleMailTakeItem, WorldSession.MailHandler/HandleSendMail, WorldSession.MailHandler/HandleSendMailCallback, WorldSession.Main/ForcePlayerLogoutDelay, WorldSession.Main/HasTrialRestrictions, WorldSession.Main/ProcessPackets, WorldSession.Main/Update, WorldSession.MiscHandler/HandleAddFriendOpcode, WorldSession.MiscHandler/HandleAreaTriggerOpcode, WorldSession.MiscHandler/operator(), WorldSession.MovementHandler/ExecuteTeleportNear, WorldSession.PetitionsHandler/HandleOfferPetitionOpcode, WorldSession.PetitionsHandler/HandlePetitionSignOpcode, WorldSession.TradeHandler/HandleAcceptTradeOpcode, WorldSession.TradeHandler/HandleInitiateTradeOpcode, WorldSession.TradeHandler/MoveItems, WorldSocket/_HandleCompleteReceivedPacket | — |
| IsPvPRealm | method | — | Player.Main/IsOutdoorPvPActive, Player.Main/UpdateZone, WorldSession.CharacterHandler/HandleCharCreateOpcode | — |
| IsFFAPvPRealm | method | — | Player.Main/SetGameMaster, Player.Main/SetRestType, Player.Main/UpdateArea, Player.Main/UpdateZone | — |
| GetMaxVisibleDistanceOnContinents | method | — | Map.Main/InitVisibilityDistance#3, Map.Main/Update#3 | — |
| GetMaxVisibleDistanceInInstances | method | — | Map.Main/InitVisibilityDistance#2 | — |
| GetMaxVisibleDistanceInBG | method | — | Map.Main/InitVisibilityDistance | — |
| GetMaxVisibleDistanceInFlight | method | — | WorldObject.Object/IsWithinVisibilityDistanceOf | — |
| GetVisibleUnitGreyDistance | method | — | WorldObject.Object/IsWithinVisibilityDistanceOf | — |
| GetVisibleObjectGreyDistance | method | — | Corpse/IsVisibleForInState, DynamicObject/IsVisibleForInState, GameObject/IsVisibleForInState, WorldObject.Object/IsWithinVisibilityDistanceOf | — |
| GetRelocationLowerLimitSq | method | — | Unit.Main/OnRelocated | — |
| GetRelocationAINotifyDelay | method | — | Unit.Main/OnRelocated | — |
| GetWardenModuleDirectory | method | — | WardenModuleMgr/WardenModuleMgr | — |
| QueueCliCommand | method | — | CliRunnable/operator(), MaNGOSsoap/ns1__executeCommand, RASocket/HandleInput_Authenticated | — |
| GetAvailableDbcLocale | method | — | WorldSession.Main/WorldSession | — |
| GetBroadcaster | method | — | ChatHandler.ChatCommands/HandlePBCastSetThreadsCommand, ChatHandler.ChatCommands/HandlePBCastStatsCommand, Map.Main/Update#3, Player.Main/CreatePacketBroadcaster, Player.Main/DeletePacketBroadcaster, WorldObject.Object/SendMovementMessageToSet | — |
| GetTimeRate | method | — | Creature.Main/Update | — |
| SetTimeRate | method | — | ChatHandler.DebugCommands/HandleDebugTimeCommand | — |
| SetAnticrashRearmTimer | method | — | Master/_OnSignal, WorldRunnable/operator() | — |
| GetAnticrashRearmTimer | method | — | WorldRunnable/operator() | — |
| GetCurrentMSTime | method | — | Transport/Create, Transport/Create#2, WorldObject.Object/BuildMovementUpdate, WorldSession.MovementHandler/HandleForceSpeedChangeAckOpcodes, WorldSession.MovementHandler/HandleMoveKnockBackAck, WorldSession.MovementHandler/HandleMovementFlagChangeToggleAck, WorldSession.MovementHandler/HandleMovementOpcodes, WorldSession.MovementHandler/HandleMoveNotActiveMoverOpcode, WorldSession.MovementHandler/HandleMoveRootAck, WorldSession.MovementHandler/HandleMoveSplineDoneOpcode | — |
| GetCurrentClockTime | method | — | Creature.Main/AddCooldown, Pet.Main/_LoadSpellCooldowns, Pet.Main/_SaveSpellCooldowns, Player.Main/AddCooldown, Player.Main/LockOutSpells, Player.Main/SendInitialSpells, Player.Main/_LoadSpellCooldowns, SpellCaster/AddCooldown, SpellCaster/AddGCD, SpellCaster/IsSpellOnPermanentCooldown, SpellCaster/LockOutSpells, SpellCaster/PrintCooldownList, Unit.Main/WritePetSpellsCooldown | — |
| GetCurrentDiff | method | — | MovementAnticheat/HandleFlagTests, MovementAnticheat/HandlePositionTests | — |
| GetMessager | method | — | ChatHandler.Chat/ParseCommands, LFGQueue/AddGroup, LFGQueue/AddPlayer, LFGQueue/FindRoleToGroup, LFGQueue/RemoveGroupFromQueue, LFGQueue/RemovePlayerFromQueue, LFGQueue/RestoreOfflinePlayer, LFGQueue/Update, Log.Warden/ApplyPenalty, Log.Warden/KickSession, Log.Warden/SendPacket, WardenMac/SetCharEnumPacket, WardenMac/Update, WardenWin/SetCharEnumPacket, WardenWin/Update | — |
| GetLFGQueue | method | — | game_Group_Group/Disband, game_Group_Group/RemoveMember, LFGMgr/AddToQueue, LFGMgr/UpdateGroup, WorldSession.LFGHandler/HandleMeetingStoneInfoOpcode, WorldSession.LFGHandler/HandleMeetingStoneLeaveOpcode, WorldSession.Main/LogoutPlayer | — |
| CharactersDatabaseWorkerThread | function | Database/HasAsyncQuery, DatabaseMysql/ThreadEnd, DatabaseMysql/ThreadStart, ObjectMgr/ReturnOrDeleteOldMails, Player.Main/DeleteOldCharacters | — | — |
| GetPatchName | method | — | — | — |
| SetInitialWorldSettings | method | AccountMgr/Load, Anticheat/GetAnticheatLib, Anticheat/LoadAnticheatData, Anticheat/StartWardenUpdateThread, AuctionHouseMgr/LoadAuctionHouses, AuctionHouseMgr/LoadAuctionItems, AuctionHouseMgr/LoadAuctions, AuraRemovalMgr/LoadFromDB, AutoBroadCastMgr/Load, BattleGroundMgr/CreateInitialBattleGrounds, BattleGroundMgr/LoadBattleEventIndexes, BattleGroundMgr/LoadBattleMastersEntry, CharacterDatabaseCache/instance, CharacterDatabaseCache/LoadAll, CharacterDatabaseCleaner/CleanDatabase, ChatHandler.AuctionHouseBotMgr/Load, ChatHandler.Chat/LoadRbacPermissions, ChatHandler.PlayerBotMgr/Load, CreateThread/CreateThreadPtr, CreatureAIRegistry/Initialize, CreatureEventAIMgr/LoadCreatureEventAI_Events, CreatureGroups/Load, CreatureGroupsManager/instance, CreatureLinkingMgr/LoadFromDB, Database/Execute#2, Database/PExecute#2, DBCStores/LoadDBCStores, GameEventMgr.Main/Initialize, GameEventMgr.Main/LoadFromDB, GameObjectModel/LoadGameObjectModelList, GMTicketMgr/Initialize, GMTicketMgr/LoadSurveys, GMTicketMgr/LoadTickets, GuildMgr/LoadGuilds, GuildMgr/LoadPetitions, HonorMgr/DoMaintenance, HonorMgr/Initialize, InstanceStatistics/LoadFromDB, IntervalTimer/SetInterval, ItemEnchantmentMgr/LoadRandomEnchantmentsTable, LFGQueue/Update, Log.Main/Out, Log.Main/WaitBeforeContinueIfNeed, LootMgr/CheckLootTemplates_Reference, LootMgr/LoadLootTables, MapManager/ExistMapAndVMap, MapManager/Initialize, MapPersistentStateMgr/CleanupInstances, MapPersistentStateMgr/LoadCreatureRespawnTimes, MapPersistentStateMgr/LoadGameobjectRespawnTimes, MapPersistentStateMgr/PackInstances, MapPersistentStateMgr/ScheduleInstanceResets, MoveMap/createOrGetMMapManager, MoveMap/loadAllGameObjectModels, ObjectMgr/GetTransportDisplayIds, ObjectMgr/LoadAllIdentifiers, ObjectMgr/LoadAreaLocales, ObjectMgr/LoadAreaTemplate, ObjectMgr/LoadAreaTriggerLocales, ObjectMgr/LoadAreaTriggers, ObjectMgr/LoadAreaTriggerTeleports, ObjectMgr/LoadBattlegroundEntranceTriggers, ObjectMgr/LoadBroadcastTextLocales, ObjectMgr/LoadBroadcastTexts, ObjectMgr/LoadCinematicsWaypoints, ObjectMgr/LoadConditions, ObjectMgr/LoadCorpses, ObjectMgr/LoadCreatureAddons, ObjectMgr/LoadCreatureClassLevelStats, ObjectMgr/LoadCreatureDisplayInfoAddon, ObjectMgr/LoadCreatureLocales, ObjectMgr/LoadCreatures, ObjectMgr/LoadCreatureSpells, ObjectMgr/LoadCreatureTemplates, ObjectMgr/LoadEquipmentTemplates, ObjectMgr/LoadExplorationBaseXP, ObjectMgr/LoadFactionChangeItems, ObjectMgr/LoadFactionChangeMounts, ObjectMgr/LoadFactionChangeQuests, ObjectMgr/LoadFactionChangeReputations, ObjectMgr/LoadFactionChangeSpells, ObjectMgr/LoadFactions, ObjectMgr/LoadFishingBaseSkillLevel, ObjectMgr/LoadGameObjectDisplayInfoAddon, ObjectMgr/LoadGameObjectForQuests, ObjectMgr/LoadGameObjectLocales, ObjectMgr/LoadGameobjects, ObjectMgr/LoadGameobjectsRequirements, ObjectMgr/LoadGameObjectTemplates, ObjectMgr/LoadGameTele, ObjectMgr/LoadGossipMenuItemsLocales, ObjectMgr/LoadGossipMenus, ObjectMgr/LoadGraveyardZones, ObjectMgr/LoadGroups, ObjectMgr/LoadItemLocales, ObjectMgr/LoadItemPrototypes, ObjectMgr/LoadItemRequiredTarget, ObjectMgr/LoadItemTexts, ObjectMgr/LoadMailTemplate, ObjectMgr/LoadMangosStrings, ObjectMgr/LoadMapLootDisabled, ObjectMgr/LoadMapTemplate, ObjectMgr/LoadNpcGossips, ObjectMgr/LoadNPCText, ObjectMgr/LoadPageTextLocales, ObjectMgr/LoadPageTexts, ObjectMgr/LoadPetCreateSpells, ObjectMgr/LoadPetLevelInfo, ObjectMgr/LoadPetNames, ObjectMgr/LoadPetNumber, ObjectMgr/LoadPetSpellData, ObjectMgr/LoadPlayerCacheData, ObjectMgr/LoadPlayerInfo, ObjectMgr/LoadPlayerPhaseFromDb, ObjectMgr/LoadPlayerPremadeTemplates, ObjectMgr/LoadPointOfInterestLocales, ObjectMgr/LoadPointsOfInterest, ObjectMgr/LoadQuestAreaTriggers, ObjectMgr/LoadQuestGreetings, ObjectMgr/LoadQuestLocales, ObjectMgr/LoadQuestRelations, ObjectMgr/LoadQuests, ObjectMgr/LoadReputationOnKill, ObjectMgr/LoadReputationRewardRate, ObjectMgr/LoadReputationSpilloverTemplate, ObjectMgr/LoadReservedPlayersNames, ObjectMgr/LoadSavedVariable, ObjectMgr/LoadSkillLineAbility, ObjectMgr/LoadSoundEntries, ObjectMgr/LoadSpellDisabledEntrys, ObjectMgr/LoadTavernAreaTriggers, ObjectMgr/LoadTaxiNodes, ObjectMgr/LoadTaxiPathTransitions, ObjectMgr/LoadTrainerGreetings, ObjectMgr/LoadTrainers, ObjectMgr/LoadTrainerTemplates, ObjectMgr/LoadVendors, ObjectMgr/LoadVendorTemplates, ObjectMgr/LoadWorldSafeLocsFacing, ObjectMgr/PackGroupIds, ObjectMgr/RestoreDeletedItems, ObjectMgr/ReturnOrDeleteOldMails, ObjectMgr/SetDBCLocaleIndex, ObjectMgr/SetHighestGuids, PoolManager/LoadFromDB, ScriptMgr/CheckAllScriptTexts, ScriptMgr/Initialize, ScriptMgr/LoadAreaTriggerScripts, ScriptMgr/LoadCreatureEventAIScripts, ScriptMgr/LoadCreatureMovementScripts, ScriptMgr/LoadCreatureSpellScripts, ScriptMgr/LoadEventIdScripts, ScriptMgr/LoadEventScripts, ScriptMgr/LoadGameObjectScripts, ScriptMgr/LoadGenericScripts, ScriptMgr/LoadGossipScripts, ScriptMgr/LoadQuestEndScripts, ScriptMgr/LoadQuestStartScripts, ScriptMgr/LoadScriptNames, ScriptMgr/LoadSpellScripts, shared_Util/getMSTime, SpellMgr/AssignInternalSpellFlags, SpellMgr/Instance, SpellMgr/LoadSkillLineAbilityMaps, SpellMgr/LoadSkillRaceClassInfoMap, SpellMgr/LoadSpellAreas, SpellMgr/LoadSpellChains, SpellMgr/LoadSpellCones, SpellMgr/LoadSpellElixirs, SpellMgr/LoadSpellEnchantCharges, SpellMgr/LoadSpellGroups, SpellMgr/LoadSpellGroupStackRules, SpellMgr/LoadSpellLearnSkills, SpellMgr/LoadSpellLearnSpells, SpellMgr/LoadSpellPetAuras, SpellMgr/LoadSpellProcEvents, SpellMgr/LoadSpellProcItemEnchant, SpellMgr/LoadSpells, SpellMgr/LoadSpellScriptTarget, SpellMgr/LoadSpellTargetPositions, SpellMgr/LoadSpellThreats, SpellModMgr/LoadSpellMods, TicketMgr/instance, TransportMgr/LoadTransportAnimationAndRotation, TransportMgr/LoadTransportTemplates, WaypointManager/Load, Weather/LoadWeatherZoneChances, WorldTimer/getMSTimeDiff, ZoneScriptMgr/InitZoneScripts | Master/Run | corpse, ip_banned, realmlist, uptime |
| DetectDBCLang | method | Config/GetIntDefault, Errors/PrintStacktraceAndThrow, Log.Main/Out, Log.Main/WaitBeforeContinueIfNeed | — | — |
| ProcessAsyncPackets | method | MapSessionFilter/MapSessionFilter, PacketFilter/SetProcessType, WorldSession.Main/ProcessPackets | — | — |
| Update | method | AccountMgr/Update, AuctionHouseMgr/Update, AutoBroadCastMgr/Update, BattleGroundMgr/Update, ChatHandler.AuctionHouseBotMgr/Update, ChatHandler.PlayerBotMgr/Update, Database/PExecute#2, GameEventMgr.Main/Update, game_Group_Group/UpdateOfflineLeader, GridMap/Update, GuardMgr/Update, HonorMgr/CheckMaintenanceDay, IntervalTimer/GetCurrent, IntervalTimer/Passed, IntervalTimer/Reset, IntervalTimer/SetCurrent, IntervalTimer/SetInterval, IntervalTimer/Update, Log.Main/Out, MapManager/RemoveAllObjectsInRemoveList, MapManager/Update, MapPersistentStateManager/Update, MassMailMgr/Update, ObjectAccessor/RemoveOldCorpses, ObjectMgr/GetGroupMapBegin, ObjectMgr/GetGroupMapEnd, ObjectMgr/SaveVariables, shared_Util/getMSTime, ThreadPool/processWorkload#2, ThreadPool/ThreadPool, WorldTimer/getMSTimeDiffToNow, ZoneScriptMgr/Update | WorldRunnable/operator() | uptime |
| SendGlobalMessage | method | Object/IsInWorld, Player.Main/GetTeam, WorldSession.Main/GetPlayer, WorldSession.Main/SendPacket | ChatHandler.Chat/SendGlobalSysMessage, ChatHandler.ServerCommands/HandleNotifyCommand | — |
| WorldWorldTextBuilder | ctor | — | — | — |
| operator()#2 | method | ObjectMgr/GetMangosString | — | — |
| lineFromMessage | method | — | — | — |
| do_helper | method | ChatHandler.Chat/BuildChatPacket, WorldPacket/WorldPacket | — | — |
| WorldBroadcastTextBuilder | ctor | ObjectGuid/ObjectGuid | — | — |
| operator() | method | ChatHandler.Chat/BuildChatPacket, ObjectMgr/GetBroadcastText, WorldPacket/WorldPacket | — | — |
| SendWorldText | method | Object/IsInWorld, WorldSession.Main/GetPlayer | AutoBroadCastMgr/Update, BattleGroundMgr/AddGroup, BattleGroundMgr/ToggleTesting, ChatHandler.CharacterCommands/HandleResetAllCommand, ChatHandler.ServerCommands/HandleAnnounceCommand, GameEventMgr.Main/ApplyNewEvent, game_Battlegrounds_BattleGround/Update, Master/_OnSignal | — |
| SendWorldTextToBGAndQueue | method | BattleGroundMgr/BgTemplateId, Object/IsInWorld, Player.Main/GetBattleGroundBracketIdFromLevel, Player.Main/GetBattleGroundBracketIdFromLevel#2, Player.Main/GetBattleGroundTypeId, Player.Main/InBattleGround, Player.Main/InBattleGroundQueueForBattleGroundQueueType, WorldSession.Main/GetPlayer, WorldSession.Main/GetSecurity | ChatHandler.PlayerBotMgr/AddBattleBot | — |
| SendBroadcastTextToWorld | method | Object/IsInWorld, ObjectGuid/ObjectGuid, WorldSession.Main/GetPlayer | azshara/JustDied, ChatHandler.HardcodedEvents/Update#7, moonglade/UpdateAI, silithus/BeginAQOpeningEvent, world_event_wareffort/UpdateAI#3 | — |
| SendGMTicketText | method | Object/IsInWorld, Player.Main/IsAcceptTickets, Player.Main/SendSysMessage, WorldSession.Main/GetPlayer, WorldSession.Main/GetSecurity | ChatHandler.TicketCommands/HandleGMTicketAssignToCommand, ChatHandler.TicketCommands/HandleGMTicketCloseByIdCommand, ChatHandler.TicketCommands/HandleGMTicketCommentCommand, ChatHandler.TicketCommands/HandleGMTicketCompleteCommand, ChatHandler.TicketCommands/HandleGMTicketDeleteByIdCommand, ChatHandler.TicketCommands/HandleGMTicketUnAssignCommand, GMTicketMgr/ReloadTicketCallback | — |
| SendGMTicketText#2 | method | Object/IsInWorld, Player.Main/IsAcceptTickets, WorldSession.Main/GetPlayer, WorldSession.Main/GetSecurity | GMTicketMgr/ReloadTicketCallback, WorldSession.GMTicketHandler/HandleGMTicketCreateOpcode, WorldSession.GMTicketHandler/HandleGMTicketDeleteTicketOpcode, WorldSession.GMTicketHandler/HandleGMTicketUpdateTextOpcode | — |
| SendGMText | method | Object/IsInWorld, WorldSession.Main/GetPlayer, WorldSession.Main/GetSecurity | Log.Warden/ApplyPenalty, Player.Main/GiveLevel, WorldObject.Object/Update, WorldSession.Main/ProcessAnticheatAction | — |
| SendGlobalText | method | ChatHandler.Chat/BuildChatPacket, ChatHandler.Chat/LineFromMessage, Common/mangos_strdup, WorldPacket/WorldPacket | GameEventMgr.Main/UpdateSilithusPVP, WorldSession.Main/ProcessAnticheatAction | — |
| SendZoneMessage | method | Object/IsInWorld, Player.Main/GetTeam, WorldObject.Object/GetZoneId, WorldSession.Main/GetPlayer, WorldSession.Main/SendPacket | — | — |
| SendZoneText | method | ChatHandler.Chat/BuildChatPacket, WorldPacket/WorldPacket | OutdoorPvPSI/HandleAreaTrigger | — |
| KickAll | method | WorldSession.Main/KickPlayer | — | — |
| KickAllLess | method | WorldSession.Main/GetSecurity, WorldSession.Main/KickPlayer | ChatHandler.ServerCommands/HandleServerPLimitCommand | — |
| WarnAccount | method | Database/escape_string, Database/PExecute#2 | ChatHandler.AccountCommands/HandleAddCharacterNoteCommand, ChatHandler.AccountCommands/HandleMuteCommand, ChatHandler.AccountCommands/HandleWarnCharacterCommand | account_banned |
| BanAccount#2 | method | AccountMgr/BanAccount, Database/escape_string, Database/PExecute#2, WorldSession.Main/GetPlayerName, WorldSession.Main/KickPlayer, WorldSession.Main/LogoutPlayer | — | account_banned |
| BanQueryHolder | ctor | — | — | — |
| GetBanMode | method | — | — | — |
| GetDuration | method | — | — | — |
| GetReason | method | — | — | — |
| GetRealmId | method | — | — | — |
| GetAuthor | method | — | — | — |
| GetBanTarget | method | — | — | — |
| GetAuthorAccountId | method | — | — | — |
| HandleAccountSelectResult | method | AccountMgr/BanAccount, ChatHandler.AccountCommands/SendBanResult, ChatHandler.Chat/ChatHandler#3, Database/PExecute#2, Field/GetUInt32, QueryResult/Fetch, QueryResult/NextRow, SqlOperations/DeleteAllResults, SqlOperations/TakeResult, WorldSession.Main/KickPlayer, WorldSession.Main/LogoutPlayer | — | account_banned |
| BanAccount | method | AccountMgr/BanIP, Database/escape_string, Database/PExecute#2, ObjectMgr/GetPlayerDataByName, ObjectMgr/GetPlayerGuidByName, SqlOperations/SetPQuery, SqlOperations/SetSize | ChatHandler.AccountCommands/HandleBanAllIPCommand, ChatHandler.AccountCommands/HandleBanHelper, Log.Warden/ApplyPenalty, WorldSession.Main/ProcessAnticheatAction | account, characters, ip_banned |
| RemoveBanAccount | method | AccountMgr/GetId, AccountMgr/UnbanAccount, AccountMgr/UnbanIP, Database/escape_string, Database/PExecute#2, ObjectMgr/GetPlayerAccountIdByPlayerName | ChatHandler.AccountCommands/HandleUnBanHelper | account_banned, ip_banned |
| _UpdateGameTime | method | — | — | — |
| ShutdownServ | method | — | ChatHandler.ServerCommands/HandleServerIdleRestartCommand, ChatHandler.ServerCommands/HandleServerIdleShutDownCommand, ChatHandler.ServerCommands/HandleServerRestartCommand, ChatHandler.ServerCommands/HandleServerShutDownCommand, HonorMgr/CheckMaintenanceDay | — |
| ShutdownMsg | method | Log.Main/Out, shared_Util/secsToTimeString | WorldSession.CharacterHandler/HandlePlayerLogin | — |
| ShutdownCancel | method | Log.Main/Out | ChatHandler.ServerCommands/HandleServerShutDownCancelCommand | — |
| SendServerMessage | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#3, Player.Main/GetSession, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| UpdateSessions | method | Log.Main/Out, shared_Util/getMSTime, WorldSession.Main/GetAccountId, WorldSession.Main/GetCreateTime, WorldSession.Main/InitWarden, WorldSession.Main/PlayerLoading, WorldSession.Main/SendAuthWaitQue, WorldSession.Main/SetInQueue, WorldSession.Main/Update, WorldSession.Main/UpdateDisconnected, WorldSessionFilter/WorldSessionFilter | — | — |
| ProcessCliCommands | method | ChatHandler.Chat/HasSentErrorMessage, ChatHandler.Chat/ParseCommands, CliHandler/CliHandler, Log.Main/Out | — | — |
| InitResultQueue | method | — | WorldRunnable/operator() | — |
| UpdateResultQueue | method | Database/ProcessResultQueue | — | — |
| UpdateRealmCharCount | method | — | Player.Main/DeleteFromDB | characters |
| _UpdateRealmCharCount | method | Database/PExecute#2, Field/GetUInt32, QueryResult/Fetch | — | — |
| SetPlayerLimit | method | Database/PExecute#2 | ChatHandler.ServerCommands/HandleServerPLimitCommand | realmlist |
| UpdateMaxSessionCounters | method | — | — | — |
| setConfig#7 | method | Config/GetIntDefault | — | — |
| setConfig#5 | method | Config/GetIntDefault | — | — |
| setConfig#3 | method | Config/GetFloatDefault | — | — |
| setConfig | method | Config/GetBoolDefault | — | — |
| setConfigPos#2 | method | Log.Main/Out | — | — |
| setConfigPos | method | Log.Main/Out | — | — |
| setConfigMin#3 | method | Log.Main/Out | — | — |
| setConfigMin#2 | method | Log.Main/Out | — | — |
| setConfigMin | method | Log.Main/Out | — | — |
| setConfigMinMax#3 | method | Log.Main/Out | — | — |
| setConfigMinMax#2 | method | Log.Main/Out | — | — |
| setConfigMinMax | method | Log.Main/Out | — | — |
| configNoReload#4 | method | Config/GetIntDefault, Log.Main/Out | — | — |
| configNoReload#3 | method | Config/GetIntDefault, Log.Main/Out | — | — |
| configNoReload#2 | method | Config/GetFloatDefault, Log.Main/Out | — | — |
| configNoReload | method | Config/GetBoolDefault, Log.Main/Out | — | — |
| InvalidatePlayerDataToAllClient | method | ObjectGuid/operator<<, WorldPacket/WorldPacket#4 | Player.Main/ChangeRace, WorldSession.CharacterHandler/HandleChangePlayerNameOpcodeCallBack | — |
| SetSessionDisconnected | method | Errors/PrintStacktraceAndThrow, WorldSession.Main/GetAccountId, WorldSession.Main/GetCreateTime | WorldSession.Main/SetDisconnectedSession | — |
| AddAsyncTask | method | — | AsyncCommandHandlers/HandleAccountLookupResult, AsyncCommandHandlers/HandlePlayerAccountSearchResult, AsyncCommandHandlers/HandlePlayerCharacterLookupResult, WorldSession.AuctionHouseHandler/HandleAuctionListBidderItems, WorldSession.AuctionHouseHandler/HandleAuctionListItems, WorldSession.AuctionHouseHandler/HandleAuctionListOwnerItems, WorldSession.MiscHandler/HandleWhoOpcode | — |
| LogMoneyTrade | method | Database/CreateStatement, ObjectGuid/GetCounter, ObjectGuid/GetEntry, ObjectGuid/GetHigh, SqlPreparedStatement/Execute#2, SqlStatement/addString#3, SqlStatement/addUInt32, SqlStatementID/SqlStatementID | Player.Main/LogModifyMoney | logs_trade |
| LogChat | method | AbstractPlayer/GetName#2, AbstractPlayer/GetObjectGuid#2, Errors/PrintStacktraceAndThrow, ObjectGuid/GetCounter, Player.Main/Player, WorldSession.Main/GetPlayerPointer | game_Guild_Guild/Create#2, WorldSession.ChatHandler/HandleChatMessageOpcode, WorldSession.PetitionsHandler/HandlePetitionBuyOpcode | — |
| LogTransaction | method | Database/CreateStatement, SqlPreparedStatement/Execute#2, SqlStatement/addString#2, SqlStatement/addString#3, SqlStatement/addUInt32, SqlStatementID/SqlStatementID | AuctionHouseMgr/Update#2, WorldSession.AuctionHouseHandler/HandleAuctionPlaceBid, WorldSession.AuctionHouseHandler/HandleAuctionSellItem, WorldSession.MailHandler/HandleMailTakeItem, WorldSession.MailHandler/HandleSendMailCallback, WorldSession.TradeHandler/HandleAcceptTradeOpcode | logs_transactions |
| CanSkipQueue | method | WorldSession.Main/GetAccountId, WorldSession.Main/GetSecurity | — | — |
| InsertLog | method | — | WorldSession.MailHandler/HandleSendMailCallback | — |
| GetLog | method | — | ChatHandler.ServerCommands/HandleViewLogCommand | — |
| SetWorldUpdateTimer | method | IntervalTimer/SetCurrent | silithus/HandleWarStage, silithus/JustDied#3 | — |
| GetWorldUpdateTimer | method | IntervalTimer/GetCurrent | — | — |
| GetWorldUpdateTimerInterval | method | IntervalTimer/GetInterval | Map.Main/RemoveOldBones, silithus/HandleWarStage, silithus/JustDied#3 | — |
| GetDelayUntilNextSpellBatchingInterval | method | shared_Util/getMSTime | Spell.Main/GetSpellBatchingEffectDelay, Unit.Main/DelayAutoAttacks | — |
| operator()#3 | method | WorldSession.Main/SendPacket | — | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `account`: id int(11) unsigned PK, username varchar(32), gmlevel tinyint(3) unsigned, sessionkey longtext?, v longtext?, s longtext?, token_key varchar(100), email text?, joindate timestamp, last_ip varchar(30), failed_logins int(11) unsigned, locked tinyint(3) unsigned, lock_country varchar(2), last_login timestamp, online tinyint(4), expansion tinyint(3) unsigned, mutetime bigint(40), locale tinyint(3) unsigned, os varchar(4), platform varchar(4), current_realm tinyint(3) unsigned, flags int(10) unsigned, security varchar(255)?, email_verif tinyint(1), geolock_pin int(11)?
- `account_banned`: banid bigint(20), id bigint(20) PK, bandate bigint(40) PK, unbandate bigint(40), bannedby varchar(50), banreason varchar(255), active tinyint(4), realm tinyint(4), gmlevel tinyint(4) unsigned
- `characters`: guid int(11) unsigned PK, account int(11) unsigned, name varchar(12), race tinyint(3) unsigned, class tinyint(3) unsigned, gender tinyint(3) unsigned, skin tinyint(3) unsigned, face tinyint(3) unsigned, hair_style tinyint(3) unsigned, hair_color tinyint(3) unsigned, facial_hair tinyint(3) unsigned, level tinyint(3) unsigned, xp int(10) unsigned, money int(10) unsigned, character_flags int(10) unsigned, zone int(11) unsigned, map int(11) unsigned, instance int(11) unsigned, position_x float, position_y float, position_z float, orientation float, transport_guid bigint(20) unsigned, transport_x float, transport_y float, transport_z float, transport_o float, known_taxi_mask longtext?, current_taxi_path text?, online tinyint(3) unsigned, played_time_total int(11) unsigned, played_time_level int(11) unsigned, create_time bigint(20) unsigned, logout_time bigint(20) unsigned, rest_bonus float, reset_talents_multiplier int(11) unsigned, reset_talents_time bigint(20) unsigned, death_expire_time bigint(20) unsigned, stable_slots tinyint(1) unsigned, bank_bag_slots tinyint(1) unsigned, extra_flags int(11) unsigned, honor_rank_points float, honor_highest_rank int(11) unsigned, honor_standing int(11) unsigned, honor_last_week_hk int(11) unsigned, honor_last_week_cp float, honor_stored_hk int(11), honor_stored_dk int(11), watched_faction int(11), drunk smallint(5) unsigned, health int(10) unsigned, power1 int(10) unsigned, power2 int(10) unsigned, power3 int(10) unsigned, power4 int(10) unsigned, power5 int(10) unsigned, explored_zones longtext?, equipment_cache longtext?, ammo_id int(10) unsigned, action_bars tinyint(3) unsigned, deleted_account int(11) unsigned?, deleted_name varchar(12)?, deleted_time bigint(20)?, world_phase_mask int(11)?
- `corpse`: guid int(11) unsigned PK, player_guid int(11) unsigned, position_x float, position_y float, position_z float, orientation float, map int(11) unsigned, time bigint(20) unsigned, corpse_type tinyint(3) unsigned, instance int(11) unsigned
- `ip_banned`: ip varchar(32) PK, bandate int(11), unbandate int(11), bannedby varchar(50), banreason varchar(50)
- `logs_trade`: time timestamp, type enum('AuctionBid','AuctionBuyout','BuyItem','SellItem','GM','Mail','QuestMaxLevel','Quest','Loot','Trade',''), sender int(11) unsigned, senderType int(11) unsigned, senderEntry int(11) unsigned, receiver int(11) unsigned, amount int(11), data int(11)
- `logs_transactions`: time timestamp, type enum('Bid','Buyout','PlaceAuction','Trade','Mail','MailCOD')?, guid1 int(11) unsigned, money1 int(11) unsigned, spell1 int(11) unsigned, items1 varchar(255), guid2 int(11) unsigned, money2 int(11) unsigned, spell2 int(11) unsigned, items2 varchar(255)
- `realmlist`: id int(11) unsigned PK, name varchar(32), address varchar(32), localAddress varchar(255), localSubnetMask varchar(255), port int(11), icon tinyint(3) unsigned, realmflags tinyint(3) unsigned, timezone tinyint(3) unsigned, allowedSecurityLevel tinyint(3) unsigned, population float unsigned, gamebuild_min int(11) unsigned, gamebuild_max int(11) unsigned, flag tinyint(3) unsigned, realmbuilds varchar(64)
- `uptime`: realmid int(11) unsigned PK, starttime bigint(20) unsigned PK, startstring varchar(64), uptime bigint(20) unsigned, onlineplayers smallint(5) unsigned, maxplayers smallint(5) unsigned, revision varchar(255)

*`?` = nullable, `PK` = primary key column.*


---

<!-- verify: failed-members | missing: DetectDBCLang, GetAnticrashRearmTimer, GetAvailableDbcLocale, GetBroadcaster, GetConfigMaxSkillValue, GetLFGQueue, GetLocalTimeByTime, GetMaxVisibleDistanceInBG, GetMaxVisibleDistanceInFlight, GetMaxVisibleDistanceInInstances, GetMessager, GetTimeRate, GetWardenModuleDirectory, IsFFAPvPRealm, IsPvPRealm, QueueCliCommand, SetAnticrashRearmTimer, SetTimeRate -->
