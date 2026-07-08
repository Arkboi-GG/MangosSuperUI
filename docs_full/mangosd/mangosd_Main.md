# mangosd_Main

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# mangosd_Main (`Main.cpp`)

## Purpose & Responsibilities

`Main.cpp` serves as the entry point for the `mangosd` executable, the world server daemon. Its primary responsibility is process initialization: parsing command-line arguments, loading the configuration file, initializing global logging facilities, managing platform-specific service states (Windows services or POSIX daemons), and finally delegating control to the core server loop via the `Master` singleton. It contains no game logic or network handling itself.

## Member-by-Member Behavior

The unit defines global database connection handles and a single `main` function.

### Global State
Four `DatabaseType` globals are declared to hold connections:
*   `WorldDatabase`: World/game data.
*   `CharacterDatabase`: Player data.
*   `LoginDatabase`: Realm/login data.
*   `LogsDatabase`: Logging data.

`realmID` and `realmName` are global placeholders for realm identification. `g_mainLogFileName` defaults to `"Server.log"`.

### The `main` Function
1.  **Argument Parsing**: Calls `ParseServerStartupArguments` (from `ArgparserForServer`). Exits with an error code if parsing fails or help is requested. Defaults config path to `_MANGOSD_CONFIG` if unspecified.
2.  **Configuration**: Calls `sConfig.LoadFromFile` (from `Config`). Must succeed before service init on Linux. Exits with `EXIT_FAILURE` if it fails, after logging and optional wait.
3.  **Logging**: Calls `sLog.OpenWorldLogFiles()` (from `Log`).
4.  **Service Management**:
    *   **Windows**: Processes `Install`, `Uninstall`, or `Start` commands via `WinServiceInstall`, `WinServiceUninstall`, or `WinServiceRun` (from `ServiceWin32`) *before* further initialization. Exits immediately after.
    *   **POSIX**: Processes `Start` or `Stop` commands via `startDaemon` or `stopDaemon` (from `PosixDaemon`) *after* config load.
5.  **Initialization Checks**: Logs core revision, ASCII banner, and OpenSSL version. Warns if OpenSSL is older than 0.9.8k. Sets progress bar visibility via `BarGoLink::SetOutputState` (from `ProgressBar`) based on config.
6.  **Execution Handoff**: Calls `sMaster.Run()` (from `Master`). Returns the result of `sMaster.Run()` as the exit code (0: normal, 1: error, 2: restart).

## Cross-Unit Boundaries

*   **`ArgparserForServer`**: Called by `main` to parse CLI args.
*   **`Config`**: Called by `main` to load settings.
*   **`Log`**: Called by `main` for logging and file setup.
*   **`ServiceWin32` / `PosixDaemon`**: Called by `main` for OS-specific service management.
*   **`Master`**: Called by `main` to run the server loop.
*   **`ProgressBar`**: Called by `main` to configure UI output.

## Data Model

This unit does not interact directly with any database tables. It declares global `DatabaseType` objects, but all SQL queries are performed by other units.

## Notable Implementation Details

*   **Service Init Order**: On Windows, service commands execute before config load; on POSIX, they execute after.
*   **OpenSSL Check**: Explicitly checks for OpenSSL >= 0.9.8k, warning if older, due to historical auth protocol requirements.
*   **Exit Codes**: Relies on `sMaster.Run()` return values to signal normal shutdown, error, or restart to external monitors.

## Member Reference

*(No members listed in the MAP)*

---

<!-- machine-true, projected from graph.json -->

## Map — mangosd_Main

*Source:* Main.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
