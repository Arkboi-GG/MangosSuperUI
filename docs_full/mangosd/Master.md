# Master

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Master

## Purpose & Responsibilities

The `Master` class is the central orchestrator for the `mangosd` world server process. It implements the singleton pattern (`sMaster`) and is responsible for the complete lifecycle of the server application: initialization, runtime management, and graceful shutdown.

Its primary responsibilities include:
1.  **Configuration & Initialization**: Reading configuration values, establishing connections to the four core databases (World, Character, Login, Logs), and validating the realm identity.
2.  **Subsystem Bootstrapping**: Starting the asynchronous I/O context, network listeners (world sockets, remote access, SOAP), background worker threads (WorldRunnable, CLI, Freeze Detector), and signal handlers.
3.  **Runtime Monitoring**: Managing a "freeze detector" thread that monitors the main world loop for hangs and terminates the server if unresponsive.
4.  **Signal Handling**: Intercepting OS signals (`SIGINT`, `SIGTERM`, `SIGSEGV`) to trigger graceful shutdowns, save player data, generate crash dumps, or restart the server depending on the signal type and configuration.
5.  **Graceful Shutdown**: Coordinating the orderly cessation of network services, database connections, and background threads, ensuring that online status flags are cleared and pending operations (like mass mail) are completed before exit.

## Member-by-Member Behavior

### Lifecycle & Initialization

**`Master()` / `~Master()`**
Trivial constructor and destructor. The class relies on the singleton implementation for lifetime management.

**`Run()`**
The main entry point for the server logic, called by `realmd_Main/main`. It executes the following sequence:
1.  **PID File Creation**: If configured, creates a PID file using `shared_Util/CreatePIDFile`. Failure here aborts startup.
2.  **Database Startup**: Calls `_StartDB()` to initialize connections to World, Character, Login, and Logs databases.
3.  **Realm Validation**: Queries the `realmlist` table in the Login database to verify the configured `realmID` exists and retrieves the `realmName`.
4.  **I/O Context Setup**: Creates an `IO::IoContext` and spawns a configurable number of network worker threads (`Network.Threads`) that run `IoContext_linux/RunUntilShutdown`.
5.  **World Initialization**: Calls `World/SetInitialWorldSettings` to prepare the game world state.
6.  **Daemonization**: On non-Windows systems, calls `PosixDaemon/detachDaemon` to detach from the controlling terminal.
7.  **Async DB Enablement**: Enables asynchronous transactions for all databases via `Database/AllowAsyncTransactions`, which were disabled during startup to ensure consistency.
8.  **Signal Hooking**: Registers signal handlers via `_HookSignals`.
9.  **Thread Launch**:
    *   Starts the `WorldRunnable` thread (the main game loop).
    *   Updates the `realmlist` table to mark the realm as online (`REALM_FLAG_OFFLINE` cleared) and sets the `realmbuilds` string.
    *   Optionally starts the CLI thread (`CliRunnable`).
    *   Optionally starts the Remote Access Server via `SetupRemoteAccessServer`.
    *   On Windows, handles processor affinity and process priority settings.
    *   Optionally starts the SOAP server thread via `MaNGOSsoap/StartSoapThread`.
    *   Optionally starts the `freezeDetector` thread if `MaxCoreStuckTime` is configured.
10. **Network Start**: Initializes the world socket manager via `WorldSocketMgr/StartWorldNetworking`.
11. **Main Loop Block**: Joins the `world_thread`, blocking until the world loop exits (triggered by a stop signal).
12. **Shutdown Sequence**:
    *   Unhooks signals.
    *   Joins optional threads (freeze, SOAP).
    *   Marks the realm as offline in `realmlist`.
    *   Stops world networking.
    *   Closes the remote access server if active.
    *   Stops system timers.
    *   Shuts down the I/O context and joins network threads.
    *   Clears online account statuses via `clearOnlineAccounts`.
    *   Flushes queued mass mail via `MassMailMgr/Update`.
    *   Stops all database servers.
    *   Terminates the CLI thread (using platform-specific hacks to unblock stdin reading).
    *   Returns the exit code from `World/GetExitCode`.

**`_StartDB()`**
Initializes the four database connections. It reads the `RealmID` from config, then calls the static helper `StartDB` for each database type (World, Character, Login, Logs). If any fail, it halts the delay threads and returns false. After successful connection, it calls `clearOnlineAccounts()` to reset stale online states from previous crashes.

**`StartDB()`**
A static helper function that initializes a specific `DatabaseType`. It reads connection strings and thread counts from the config, sanitizes the password for logging, initializes the database object via `Database/Initialize`, and checks for required schema migrations via `Database/CheckRequiredMigrations`.

### Signal Handling & Crash Recovery

**`_HookSignals()`**
Registers `_OnSignal` as the handler for `SIGINT`, `SIGTERM`, `SIGSEGV`, and `SIGBREAK` (Windows). It then calls `ArmAnticrash()` to enable crash recovery logic.

**`_UnhookSignals()`**
Resets signal handlers to default (`nullptr`) and disables the anticrash flag. Called during shutdown to prevent interference with process termination.

**`_OnSignal(int s)`**
The central signal handler.
*   **`SIGINT`**: Triggers a restart via `World/StopNow(RESTART_EXIT_CODE)`.
*   **`SIGTERM` / `SIGBREAK`**: Triggers a clean shutdown via `World/StopNow(SHUTDOWN_EXIT_CODE)`.
*   **`SIGSEGV` (Crash)**:
    1.  Disarms the handler to prevent recursion.
    2.  Sets an anticrash rearm timer via `World/SetAnticrashRearmTimer`.
    3.  Prints a stack trace via `Errors/PrintStacktrace`.
    4.  If configured, generates a core dump via `CreateCrashDump`.
    5.  If configured, announces the crash to players via `World/SendWorldText`.
    6.  If configured to save all players, it starts the character database thread (`CharacterDatabase/ThreadStart`) and forces a save of all online players via `ObjectAccessor/SaveAllPlayers`. It waits 25 seconds for this to complete.
    7.  Re-throws the exception to crash the process for real.

**`ArmAnticrash()`**
Sets the static flag `m_handleSigvSignals` to `true`, enabling the crash recovery logic in `_OnSignal`. Called by `WorldRunnable/operator()` after the world loop is stable.

**`SigvSignalHandler()`**
A static wrapper that checks `m_handleSigvSignals` and calls `_OnSignal(SIGSEGV)` if enabled, otherwise exits immediately.

**`CreateCrashDump()`**
On non-Windows systems, forks a child process and calls `abort()` in the child to generate a core dump without terminating the parent immediately (though the parent will likely crash shortly after due to the re-thrown exception in `_OnSignal`).

### Runtime Monitoring

**`freezeDetector(uint32 _delaytime)`**
A standalone function (not a member) that runs in its own thread. It sleeps for 1 second intervals and compares its loop counter with `World::m_worldLoopCounter`. If the world loop counter hasn't changed for longer than `_delaytime` milliseconds, it logs an error and calls `std::terminate()` to kill the server. This prevents the server from hanging indefinitely.

### Network & Remote Access

**`SetupRemoteAccessServer(IO::IoContext* ioCtx)`**
Creates and binds an `AsyncSocketAcceptor` for the Remote Access (RA) protocol. It reads the IP and Port from config. If binding fails, it logs an error and returns null. Otherwise, it sets up an auto-accept callback that creates a new `RASocket` for each incoming connection and starts it.

### Data Cleanup

**`clearOnlineAccounts()`**
Resets online status flags in the database to handle unexpected server restarts.
1.  Updates `account` table: Sets `current_realm` to 0 for accounts currently marked as being on this realm.
2.  Updates `characters` table: Sets `online` to 0 for all characters marked as online.
3.  Updates `character_battleground_data` table: Resets `instance_id` to 0, clearing stale battleground instance references.

## Cross-Unit Boundaries

*   **`realmd_Main/main` -> `Master::Run`**: The application entry point instantiates the singleton and calls `Run()`.
*   **`Master::Run` -> `WorldRunnable/operator()`**: `Master` launches the `WorldRunnable` thread, which owns the main game loop. `Master` blocks on this thread until the world stops.
*   **`Master::Run` -> `WorldSocketMgr/StartWorldNetworking`**: Delegates the setup of the main TCP listener for client connections to the `WorldSocketMgr`.
*   **`Master::Run` -> `MaNGOSsoap/StartSoapThread`**: Delegates SOAP web service setup to the `MaNGOSsoap` unit.
*   **`Master::Run` -> `MassMailMgr/Update`**: During shutdown, ensures any queued mass emails are sent before DB connections close.
*   **`Master::_OnSignal` -> `ObjectAccessor/SaveAllPlayers`**: In the event of a crash, delegates the saving of all online player data to the `ObjectAccessor`.
*   **`Master::_OnSignal` -> `Errors/PrintStacktrace`**: Delegates stack trace generation to the `Errors` utility.
*   **`Master::freezeDetector` -> `World/IsStopped`**: Checks if the world loop has stopped to exit the detector thread gracefully.
*   **`WorldRunnable/operator()` -> `Master::ArmAnticrash`**: Once the world loop is stable, it signals the Master to enable crash recovery handlers.

## Data Model

The `Master` unit interacts with three tables in the **Login** and **Character** databases to manage realm status and online presence.

### `realmlist` (Login Database)
*   **Usage**:
    *   **Startup**: `Master::Run` queries `name` where `id` matches the configured `realmID` to validate the realm and get its display name.
    *   **Online Status**: `Master::Run` updates `realmflags` (clearing `REALM_FLAG_OFFLINE`), sets `population` to 0, and updates `realmbuilds` with the acceptable client build string.
    *   **Shutdown**: `Master::Run` updates `realmflags` to set `REALM_FLAG_OFFLINE`.
*   **Columns Involved**: `id`, `name`, `realmflags`, `population`, `realmbuilds`.

### `account` (Login Database)
*   **Usage**:
    *   **Cleanup**: `Master::clearOnlineAccounts` resets the `current_realm` field to 0 for any account that was logged into this realm, preventing stale "online" indicators if the server crashed.
*   **Columns Involved**: `current_realm`.

### `characters` (Character Database)
*   **Usage**:
    *   **Cleanup**: `Master::clearOnlineAccounts` sets the `online` field to 0 for all characters, ensuring no character appears online after a restart.
*   **Columns Involved**: `online`.

### `character_battleground_data` (Character Database)
*   **Usage**:
    *   **Cleanup**: `Master::clearOnlineAccounts` resets `instance_id` to 0. This clears any lingering references to battleground instances that no longer exist after a server restart.
*   **Columns Involved**: `instance_id`.

## Notable Implementation Details

1.  **Asynchronous Database Transactions**: `Master::Run` explicitly enables async transactions (`AllowAsyncTransactions`) *after* the initial world setup and database migrations are complete. This ensures that the critical startup phase (schema checks, initial loads) uses synchronous queries to guarantee consistency and order, while the runtime uses async queries for performance.
2.  **Freeze Detector Termination**: The `freezeDetector` function calls `std::terminate()` if the world loop hangs. This is a hard crash, not a graceful shutdown. It is intended to prevent the server from becoming completely unresponsive, forcing a restart via external supervision (e.g., systemd, monit).
3.  **Crash Recovery Logic**: The `_OnSignal` handler for `SIGSEGV` attempts to save all players before crashing. It manually starts the character database thread (`CharacterDatabase/ThreadStart`) because the database might be in an inconsistent state or blocked. It then waits 25 seconds for the save to complete. This is a best-effort mechanism; if the crash is severe, the save may fail.
4.  **CLI Thread Termination Hack**: On Windows, the CLI thread reads from stdin. To terminate it gracefully, `Master::Run` injects fake keyboard events ('X' and Enter) into the console input buffer. On Unix, it closes `stdin`. This is a workaround for the lack of a clean interrupt mechanism for the CLI reader.
5.  **Realm Build String**: The `realmbuilds` column in `realmlist` is updated with a string generated by `DBCStores/AcceptableClientBuildsListStr`. This allows the login server (`realmd`) to inform clients whether this realm supports their client version.
6.  **Signal Handler Re-arm**: The `ArmAnticrash` method is called by `WorldRunnable` after the world loop starts. This delays the activation of the crash recovery logic until the server is fully initialized, preventing false positives during startup.

## Member Reference

**`freezeDetector`**: Standalone function that runs in a separate thread. Monitors `World::m_worldLoopCounter` once per second. If the counter doesn't change within the configured `_delaytime`, it logs an error and calls `std::terminate()` to kill the server. Uses `Log.Main/Out`, `shared_Util/getMSTime`, `World/IsStopped`, and `WorldTimer/getMSTimeDiff`.

**`SetupRemoteAccessServer`**: Static function that creates an `AsyncSocketAcceptor` for the Remote Access protocol. Reads IP/Port from config, binds the socket, and sets up an auto-accept callback that creates `RASocket` instances for new connections. Returns a `unique_ptr` to the acceptor. Uses `AsyncSocket._posix/AsyncSocket`, `AsyncSocketAcceptor_posix/AutoAcceptSocketsUntilClose`, `AsyncSocketAcceptor_posix/CreateAndBindServer`, `Config/GetIntDefault`, `Config/GetStringDefault`, `Log.Main/Out`, `RASocket/Start`, and `SocketDescriptor/SocketDescriptor`.

**`Master`**: Default constructor. No initialization logic.

**`~Master`**: Destructor. No cleanup logic (cleanup is handled in `Run`).

**`Run`**: Main server lifecycle method. Initializes PID file, databases, I/O context, world settings, signal handlers, and various threads (World, CLI, SOAP, Freeze Detector). Starts network listeners. Blocks on the World thread. On exit, performs graceful shutdown: marks realm offline, stops networking, clears online accounts, flushes mail, stops DBs, and terminates CLI thread. Uses `AsyncSocketAcceptor_posix/ClosePortAndStopAcceptingNewConnections`, `AsyncSystemTimer/RemoveAllTimersAndStopThread`, `Config/GetBoolDefault`, `Config/GetIntDefault`, `Config/GetStringDefault`, `CreateThread/CreateThread`, `CreateThread/CreateThreadPtr`, `CreateThread/RenameCurrentThread`, `Database/AllowAsyncTransactions`, `Database/DirectPExecute`, `Database/escape_string`, `Database/PExecute#2`, `Database/PQuery`, `Database/StopServer`, `DBCStores/AcceptableClientBuildsListStr`, `Field/GetCppString`, `IoContext_linux/CreateIoContext`, `IoContext_linux/RunUntilShutdown`, `IoContext_linux/Shutdown`, `Log.Main/Out`, `Log.Main/WaitBeforeContinueIfNeed`, `MaNGOSsoap/StartSoapThread`, `MassMailMgr/Update`, `PosixDaemon/detachDaemon`, `QueryResult/operator[]`, `shared_Util/CreatePIDFile`, `shared_Util/SplitStringByDelimiter`, `World/getConfig#4`, `World/GetExitCode`, `World/SetInitialWorldSettings`, `World/StopNow`, and `WorldSocketMgr/StartWorldNetworking`, `WorldSocketMgr/StopWorldNetworking`. Touches `realmlist`.

**`StartDB`**: Static helper to initialize a specific database. Reads config, sanitizes password for logs, initializes the database object, and checks migrations. Uses `Config/GetIntDefault`, `Config/GetStringDefault`, `Database/CheckRequiredMigrations`, `Database/Initialize`, and `Log.Main/Out`.

**`_StartDB`**: Member method to initialize all four databases (World, Character, Login, Logs). Reads `RealmID`, calls `StartDB` for each, and clears online accounts. Uses `Config/GetIntDefault`, `Database/HaltDelayThread`, and `Log.Main/Out`.

**`clearOnlineAccounts`**: Resets online status in the database. Updates `account.current_realm`, `characters.online`, and `character_battleground_data.instance_id`. Uses `Database/Execute#2` and `Database/PExecute#2`. Touches `account`, `characters`, and `character_battleground_data`.

**`CreateCrashDump`**: Generates a core dump on non-Windows systems by forking and aborting the child process.

**`SigvSignalHandler`**: Static wrapper that calls `_OnSignal(SIGSEGV)` if anticrash is enabled, otherwise exits.

**`_OnSignal`**: Signal handler for `SIGINT`, `SIGTERM`, `SIGSEGV`, and `SIGBREAK`. Handles restart, shutdown, and crash recovery (stack trace, core dump, player save). Uses `DatabaseMysql/ThreadStart`, `Errors/PrintStacktrace`, `Log.Main/Out`, `ObjectAccessor/SaveAllPlayers`, `World/getConfig#4`, `World/SendWorldText`, `World/SetAnticrashRearmTimer`, and `World/StopNow`.

**`_HookSignals`**: Registers `_OnSignal` for relevant signals and calls `ArmAnticrash`.

**`ArmAnticrash`**: Sets `m_handleSigvSignals` to true. Called by `WorldRunnable/operator()`.

**`_UnhookSignals`**: Resets signal handlers to default and disables anticrash.

---

<!-- machine-true, projected from graph.json -->

## Map — Master

*Source:* Master.cpp, Master.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| freezeDetector | function | Log.Main/Out, shared_Util/getMSTime, World/IsStopped, WorldTimer/getMSTimeDiff | — | — |
| SetupRemoteAccessServer | function | AsyncSocket._posix/AsyncSocket, AsyncSocketAcceptor_posix/AutoAcceptSocketsUntilClose, AsyncSocketAcceptor_posix/CreateAndBindServer, Config/GetIntDefault, Config/GetStringDefault, Log.Main/Out, RASocket/Start, SocketDescriptor/SocketDescriptor | — | — |
| Master | ctor | — | — | — |
| ~Master | dtor | — | — | — |
| Run | method | AsyncSocketAcceptor_posix/ClosePortAndStopAcceptingNewConnections, AsyncSystemTimer/RemoveAllTimersAndStopThread, Config/GetBoolDefault, Config/GetIntDefault, Config/GetStringDefault, CreateThread/CreateThread, CreateThread/CreateThreadPtr, CreateThread/RenameCurrentThread, Database/AllowAsyncTransactions, Database/DirectPExecute, Database/escape_string, Database/PExecute#2, Database/PQuery, Database/StopServer, DBCStores/AcceptableClientBuildsListStr, Field/GetCppString, IoContext_linux/CreateIoContext, IoContext_linux/RunUntilShutdown, IoContext_linux/Shutdown, Log.Main/Out, Log.Main/WaitBeforeContinueIfNeed, MaNGOSsoap/StartSoapThread, MassMailMgr/Update, PosixDaemon/detachDaemon, QueryResult/operator[], shared_Util/CreatePIDFile, shared_Util/SplitStringByDelimiter, World/getConfig#4, World/GetExitCode, World/SetInitialWorldSettings, World/StopNow, WorldSocketMgr/StartWorldNetworking, WorldSocketMgr/StopWorldNetworking | realmd_Main/main | realmlist |
| StartDB | function | Config/GetIntDefault, Config/GetStringDefault, Database/CheckRequiredMigrations, Database/Initialize, Log.Main/Out | — | — |
| _StartDB | method | Config/GetIntDefault, Database/HaltDelayThread, Log.Main/Out | — | — |
| clearOnlineAccounts | method | Database/Execute#2, Database/PExecute#2 | — | account, characters, character_battleground_data |
| CreateCrashDump | function | — | — | — |
| SigvSignalHandler | method | — | — | — |
| _OnSignal | method | DatabaseMysql/ThreadStart, Errors/PrintStacktrace, Log.Main/Out, ObjectAccessor/SaveAllPlayers, World/getConfig#4, World/SendWorldText, World/SetAnticrashRearmTimer, World/StopNow | — | — |
| _HookSignals | method | — | — | — |
| ArmAnticrash | method | — | WorldRunnable/operator() | — |
| _UnhookSignals | method | — | — | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `account`: id int(11) unsigned PK, username varchar(32), gmlevel tinyint(3) unsigned, sessionkey longtext?, v longtext?, s longtext?, token_key varchar(100), email text?, joindate timestamp, last_ip varchar(30), failed_logins int(11) unsigned, locked tinyint(3) unsigned, lock_country varchar(2), last_login timestamp, online tinyint(4), expansion tinyint(3) unsigned, mutetime bigint(40), locale tinyint(3) unsigned, os varchar(4), platform varchar(4), current_realm tinyint(3) unsigned, flags int(10) unsigned, security varchar(255)?, email_verif tinyint(1), geolock_pin int(11)?
- `character_battleground_data`: guid int(11) unsigned PK, instance_id int(11) unsigned, team int(11) unsigned, join_x float, join_y float, join_z float, join_o float, join_map int(11)
- `characters`: guid int(11) unsigned PK, account int(11) unsigned, name varchar(12), race tinyint(3) unsigned, class tinyint(3) unsigned, gender tinyint(3) unsigned, skin tinyint(3) unsigned, face tinyint(3) unsigned, hair_style tinyint(3) unsigned, hair_color tinyint(3) unsigned, facial_hair tinyint(3) unsigned, level tinyint(3) unsigned, xp int(10) unsigned, money int(10) unsigned, character_flags int(10) unsigned, zone int(11) unsigned, map int(11) unsigned, instance int(11) unsigned, position_x float, position_y float, position_z float, orientation float, transport_guid bigint(20) unsigned, transport_x float, transport_y float, transport_z float, transport_o float, known_taxi_mask longtext?, current_taxi_path text?, online tinyint(3) unsigned, played_time_total int(11) unsigned, played_time_level int(11) unsigned, create_time bigint(20) unsigned, logout_time bigint(20) unsigned, rest_bonus float, reset_talents_multiplier int(11) unsigned, reset_talents_time bigint(20) unsigned, death_expire_time bigint(20) unsigned, stable_slots tinyint(1) unsigned, bank_bag_slots tinyint(1) unsigned, extra_flags int(11) unsigned, honor_rank_points float, honor_highest_rank int(11) unsigned, honor_standing int(11) unsigned, honor_last_week_hk int(11) unsigned, honor_last_week_cp float, honor_stored_hk int(11), honor_stored_dk int(11), watched_faction int(11), drunk smallint(5) unsigned, health int(10) unsigned, power1 int(10) unsigned, power2 int(10) unsigned, power3 int(10) unsigned, power4 int(10) unsigned, power5 int(10) unsigned, explored_zones longtext?, equipment_cache longtext?, ammo_id int(10) unsigned, action_bars tinyint(3) unsigned, deleted_account int(11) unsigned?, deleted_name varchar(12)?, deleted_time bigint(20)?, world_phase_mask int(11)?
- `realmlist`: id int(11) unsigned PK, name varchar(32), address varchar(32), localAddress varchar(255), localSubnetMask varchar(255), port int(11), icon tinyint(3) unsigned, realmflags tinyint(3) unsigned, timezone tinyint(3) unsigned, allowedSecurityLevel tinyint(3) unsigned, population float unsigned, gamebuild_min int(11) unsigned, gamebuild_max int(11) unsigned, flag tinyint(3) unsigned, realmbuilds varchar(64)

*`?` = nullable, `PK` = primary key column.*

