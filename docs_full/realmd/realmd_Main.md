# realmd_Main

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# realmd_Main (`Main.cpp`)

## Purpose & Responsibilities

`Main.cpp` constitutes the entry point and lifecycle manager for **realmd**, the authentication and realm-listing daemon in the WoWVMaNGOS server suite. Its primary responsibilities are:

1.  **Process Initialization**: Parsing command-line arguments, loading configuration (`realmd.conf`), and handling platform-specific daemonization (POSIX `daemon()` or Windows Service installation/execution).
2.  **Database Connectivity**: Establishing a secure connection to the login database, verifying schema migrations, and performing initial data cleanup (expiring bans).
3.  **Network Listening**: Binding to a configured IP/port to accept incoming TCP connections from game clients or proxies.
4.  **Connection Handling**: Dispatching accepted sockets to `AuthSocket` instances for authentication, with support for Proxy Protocol v2 to identify real client IPs behind load balancers.
5.  **Lifecycle Management**: Maintaining a main event loop that keeps the database connection alive via periodic pings, handles termination signals (SIGINT/SIGTERM), and orchestrates graceful shutdown of I/O contexts, timers, and database threads.

This unit does not implement authentication logic itself; it delegates that to `AuthSocket`. It acts as the infrastructure glue between the operating system, the database, and the networking layer.

## Member-by-Member Behavior

### `main`
The central execution flow. It performs the following stages:
1.  **Argument Parsing**: Uses `ArgparserForServer/ParseServerStartupArguments` to handle CLI flags. If parsing fails or help is requested, it exits immediately.
2.  **Configuration Loading**: Loads `realmd.conf` via `Config/LoadFromFile`. If missing, it aborts. It validates the config file version against `_REALMDCONFVERSION`, warning if outdated.
3.  **Service/Daemon Mode**:
    *   On **Windows**, it handles service install/uninstall/start commands via `ServiceWin32` functions (not in MAP, but implied by code structure, though the MAP lists `PosixDaemon` calls for POSIX). The MAP explicitly lists `PosixDaemon/startDaemon`, `stopDaemon`, `detachDaemon` for POSIX paths.
    *   On **POSIX**, it calls `PosixDaemon/startDaemon` or `stopDaemon` based on arguments.
4.  **Environment Setup**:
    *   Logs core revision and OpenSSL version. Warns if OpenSSL is older than 0.9.8k.
    *   Initializes the mailer service if `ENABLE_MAILSENDER` is defined.
    *   Creates a PID file using `shared_Util/CreatePIDFile` if configured.
5.  **Database Initialization**: Calls `StartDB` to connect to the login database. If this fails, the process exits.
6.  **Geolocking Validation**: If `GeoLocking` is enabled in config, it queries the `geoip` table to ensure it is not empty. An empty table causes an immediate exit.
7.  **Realm List Initialization**: Calls `RealmList/Initialize` to load realm data. Exits if no realms are found or if no valid client builds are defined.
8.  **Ban Cleanup**: Executes a transaction to deactivate expired bans in `account_banned` and delete expired entries in `ip_banned`. This uses `Database/BeginTransaction`, `Database/Execute`, and `Database/CommitTransaction`.
9.  **Network Stack Initialization**:
    *   Creates an `IO::IoContext` via `IoContext_linux/CreateIoContext`.
    *   Binds a server socket using `AsyncSocketAcceptor_posix/CreateAndBindServer`.
    *   Sets up an accept callback via `AsyncSocketAcceptor_posix/AutoAcceptSocketsUntilClose`.
10. **Signal Handling**: Hooks `SIGINT`, `SIGTERM`, and `SIGBREAK` (Windows) using `HookSignals`.
11. **Main Loop**:
    *   Enables async database transactions via `Database/AllowAsyncTransactions`.
    *   Spawns an I/O thread (`CreateThread/CreateThread`) to run `IoContext_linux/RunUntilShutdown`.
    *   Detaches the daemon on POSIX (`PosixDaemon/detachDaemon`).
    *   Enters a `while(!stopEvent)` loop, sleeping for 1 second per iteration.
    *   Periodically pings the database (`Database/Ping`) based on `MaxPingTime` config.
    *   Checks for Windows service status changes.
12. **Graceful Shutdown**:
    *   Stops accepting new connections (`AsyncSocketAcceptor_posix/ClosePortAndStopAcceptingNewConnections`).
    *   Stops the system timer thread (`AsyncSystemTimer/RemoveAllTimersAndStopThread`).
    *   Shuts down the I/O context (`IoContext_linux/Shutdown`).
    *   Joins the I/O thread.
    *   Halts the database delay thread (`Database/HaltDelayThread`).
    *   Unhooks signals (`UnhookSignals`).
    *   Exits with code 0.

### `OnSignal`
A static signal handler function. It sets the global `stopEvent` flag to `true` upon receiving `SIGINT`, `SIGTERM`, or `SIGBREAK` (Windows). It re-registers itself for the signal using `::signal(s, OnSignal)` to ensure subsequent signals are caught (though standard practice often suggests `SA_RESTART` or similar, this implementation manually re-hooks).

### `StartDB`
Initializes the login database connection.
1.  Retrieves the connection string from config (`Config/GetStringDefault`).
2.  Sanitizes the connection string for logging by replacing the password field (4th semicolon-delimited token) with `*`.
3.  Calls `Database/Initialize` with the raw connection string.
4.  Verifies required schema migrations using `Database/CheckRequiredMigrations`. If migrations fail, it halts the delay thread (`Database/HaltDelayThread`) and returns `false`.
5.  Returns `true` on success.

### `HookSignals`
Registers `OnSignal` as the handler for `SIGINT`, `SIGTERM`, and `SIGBREAK` (Windows) using `::signal`.

### `UnhookSignals`
Resets the handlers for `SIGINT`, `SIGTERM`, and `SIGBREAK` to `nullptr` (default behavior) using `::signal`.

## Cross-Unit Boundaries

*   **ArgparserForServer**: `main` calls `ParseServerStartupArguments` to interpret CLI inputs.
*   **Config**: `main` and `StartDB` extensively use `Config` methods (`LoadFromFile`, `GetStringDefault`, `GetIntDefault`, `GetBoolDefault`) to drive behavior.
*   **Database**: `main` and `StartDB` interact with the `Database` unit for connection management, transaction control, and query execution. `main` enables async transactions after startup.
*   **RealmList**: `main` calls `RealmList/Initialize` and `RealmList/size` to verify realm availability.
*   **AuthSocket**: `main` creates `AuthSocket` instances for each accepted connection and calls `AuthSocket/Start` to begin authentication. It also accesses `AuthSocket/GetRemoteIpString` for logging.
*   **AsyncSocket / AsyncSocketAcceptor**: `main` uses these units to manage the low-level I/O context, bind the server port, and accept connections.
*   **Log.Main**: Used throughout for status reporting, errors, and warnings.
*   **ProxyV2Reader**: `main` calls `ReadProxyV2Handshake` if the connecting IP is in the trusted proxy list, to extract the real client IP.
*   **IpAddress**: Used to convert IP objects to strings for logging.
*   **shared_Util**: `main` uses `CreatePIDFile` and `SplitStringByDelimiter` (for trusted proxy IPs).
*   **PosixDaemon / ServiceWin32**: Handles OS-level daemonization/service management.

## Data Model

This unit interacts with three database tables, primarily for validation and cleanup during startup:

1.  **`account_banned`**:
    *   **Usage**: `main` executes an `UPDATE` statement to set `active = 0` for records where `unbandate <= UNIX_TIMESTAMP()` and `unbandate <> bandate`. This deactivates temporary bans that have expired.
    *   **Columns Involved**: `active`, `unbandate`, `bandate`.

2.  **`ip_banned`**:
    *   **Usage**: `main` executes a `DELETE` statement to remove records where `unbandate <= UNIX_TIMESTAMP()` and `unbandate <> bandate`. This cleans up expired IP bans.
    *   **Columns Involved**: `unbandate`, `bandate`.

3.  **`geoip`**:
    *   **Usage**: If `GeoLocking` is enabled in the config, `main` runs a `SELECT 1 FROM geoip LIMIT 1` query. If no rows are returned, the server refuses to start. This ensures the geolocation database is populated before enforcing location-based restrictions.
    *   **Columns Involved**: None specific (just existence check).

## Notable Implementation Details

*   **Password Masking in Logs**: `StartDB` manually parses the semicolon-separated database connection string to mask the password before logging. It assumes the format `host;port;user;pass;db`. If the format doesn't match exactly 4 semicolons, it logs an error and fails to start.
*   **Proxy Protocol Support**: The accept callback in `main` checks if the remote IP is in the `TrustedProxyServers` list. If so, it invokes `ProxyV2Reader/ReadProxyV2Handshake` to parse the PROXY protocol header. The real client IP is stored in `authSocket->m_remoteIpAddressStringAfterProxy`. If parsing fails, the connection is closed.
*   **Ban Cleanup Transaction**: The cleanup of `account_banned` and `ip_banned` is wrapped in a single transaction (`BeginTransaction` ... `CommitTransaction`). This ensures atomicity of the cleanup operation.
*   **OpenSSL Version Check**: The code explicitly checks `SSLeay()` against `0x009080bfL` (OpenSSL 0.9.8k). While it only logs a warning, older versions may cause authentication failures due to deprecated cipher suites or hash algorithms.
*   **Main Loop Ping Mechanism**: The main thread sleeps for 1 second per iteration. It maintains a `loopCounter` and calls `LoginDatabase.Ping()` every `numLoops` iterations, where `numLoops` is derived from `MaxPingTime` config. This prevents the database connection from timing out due to inactivity.
*   **Singleton Initialization**: The code contains `(void)sRealmdPatchCache;` and `(void)sAsyncSystemTimer;` to force initialization of these singletons before the main loop starts.
*   **Signal Re-registration**: `OnSignal` calls `::signal(s, OnSignal)` at the end. This is necessary because some Unix implementations reset the signal handler to default after delivery. However, this approach is not reentrant-safe and can lead to issues if signals arrive rapidly.

## Member Reference

**main**
The entry point for the realmd process. It parses arguments, loads configuration, initializes the database and network stack, handles daemonization, and runs the main event loop until a termination signal is received. It coordinates with `ArgparserForServer`, `Config`, `Database`, `RealmList`, `AuthSocket`, `AsyncSocketAcceptor`, `Log`, `ProxyV2Reader`, `IpAddress`, `shared_Util`, `PosixDaemon`, and `AsyncSystemTimer`.

**OnSignal**
A signal handler that sets the global `stopEvent` flag to `true` when `SIGINT`, `SIGTERM`, or `SIGBREAK` is received, triggering the main loop to exit.

**StartDB**
Initializes the connection to the login database. It retrieves the connection string from config, masks the password for logging, connects via `Database/Initialize`, and verifies schema migrations via `Database/CheckRequiredMigrations`.

**HookSignals**
Registers the `OnSignal` handler for `SIGINT`, `SIGTERM`, and `SIGBREAK` (Windows).

**UnhookSignals**
Resets the signal handlers for `SIGINT`, `SIGTERM`, and `SIGBREAK` to their default behavior.

---

<!-- machine-true, projected from graph.json -->

## Map — realmd_Main

*Source:* Main.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| main | function | ArgparserForServer/ParseServerStartupArguments, AsyncSocket.Main/GetRemoteIpString, AsyncSocket._posix/AsyncSocket, AsyncSocket._posix/InitializeAndFixateMemoryLocation, AsyncSocketAcceptor_posix/AutoAcceptSocketsUntilClose, AsyncSocketAcceptor_posix/ClosePortAndStopAcceptingNewConnections, AsyncSocketAcceptor_posix/CreateAndBindServer, AsyncSystemTimer/RemoveAllTimersAndStopThread, AuthSocket/GetRemoteIpString, AuthSocket/Start, Config/GetBoolDefault, Config/GetFilename, Config/GetIntDefault, Config/GetStringDefault, Config/LoadFromFile, CreateThread/CreateThread, CreateThread/RenameCurrentThread, Database/AllowAsyncTransactions, Database/BeginTransaction, Database/CommitTransaction, Database/Execute#2, Database/HaltDelayThread, Database/Ping, Database/Query, IoContext_linux/CreateIoContext, IoContext_linux/RunUntilShutdown, IoContext_linux/Shutdown, IpAddress/ToString, Log.Main/OpenWorldLogFiles, Log.Main/Out, Log.Main/WaitBeforeContinueIfNeed, Master/Run, NetworkError/ToString, PosixDaemon/detachDaemon, PosixDaemon/startDaemon, PosixDaemon/stopDaemon, ProgressBar/SetOutputState, ProxyV2Reader/ReadProxyV2Handshake, RealmList/Initialize, RealmList/Instance, RealmList/size, shared_Util/CreatePIDFile, shared_Util/SplitStringByDelimiter, SocketDescriptor/SocketDescriptor | — | account_banned, geoip, ip_banned |
| OnSignal | function | — | — | — |
| StartDB | function | Config/GetStringDefault, Database/CheckRequiredMigrations, Database/HaltDelayThread, Database/Initialize, Log.Main/Out | — | — |
| HookSignals | function | — | — | — |
| UnhookSignals | function | — | — | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `account_banned`: banid bigint(20), id bigint(20) PK, bandate bigint(40) PK, unbandate bigint(40), bannedby varchar(50), banreason varchar(255), active tinyint(4), realm tinyint(4), gmlevel tinyint(4) unsigned
- `geoip`: network_start_integer int(11)?, network_last_integer int(11)?, geoname_id text?, registered_country_geoname_id text?, represented_country_geoname_id text?, is_anonymous_proxy int(11)?, is_satellite_provider int(11)?, postal_code text?, latitude double?, longitude double?, accuracy_radius int(11)?
- `ip_banned`: ip varchar(32) PK, bandate int(11), unbandate int(11), bannedby varchar(50), banreason varchar(50)

*`?` = nullable, `PK` = primary key column.*

