<!-- provenance: verbose -->
# PosixDaemon

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# PosixDaemon

**Purpose & Responsibilities**

`PosixDaemon` implements POSIX-specific daemonization logic for the `realmd` process. It manages the fork-and-detach sequence, I/O redirection, and signal-based coordination between the launching parent process and the backgrounded daemon child. It also provides a mechanism to terminate a running daemon by reading its PID from a configuration-defined file.

## Member-by-Member Behavior

### Daemon Lifecycle

**startDaemon**
Initiates daemonization. It records the current PID as `parent_pid`, registers `daemonSignal` for `SIGUSR1`, `SIGINT`, `SIGTERM`, and `SIGALRM`, and forks.
- **Parent (`pid > 0`)**: Sets an alarm for `timeout` seconds and blocks in `pause()`. It waits for `SIGUSR1` from the child to indicate success; otherwise, it exits with failure upon timeout or other signals.
- **Child (`pid == 0`)**: Resets umask, creates a new session (`setsid`), changes directory to `/`, and redirects `stdin`, `stdout`, and `stderr` to `/dev/null`. If any step fails, it exits with failure.

**stopDaemon**
Terminates a running daemon. It retrieves the PID file path via `Config/GetStringDefault` ("PidFile"). If present, it reads the PID from the file and sends `SIGINT` to that process. If the PID file is missing or `kill` fails, it prints an error and exits.

**detachDaemon**
Signals successful daemonization to the parent. It sends `SIGUSR1` to `parent_pid`, waking the parent from `pause()` in `startDaemon` so it can exit successfully.

**exitDaemon**
Notifies the parent of daemon termination. If the current process is the child (not `parent_pid`), it sends `SIGTERM` to `parent_pid`.

### Signal Handling & Cleanup

**daemonSignal**
Global signal handler for `SIGUSR1`, `SIGINT`, `SIGTERM`, and `SIGALRM`.
- **Parent**: Exits with success on `SIGUSR1`; otherwise, forwards the signal to the session leader (`sid`) and exits with failure.
- **Daemon**: Forwards the signal to `sid` and exits with failure.

**~WatchDog**
Destructor for the global `WatchDog` instance `dog`. It calls `exitDaemon()` to ensure the parent is notified of the daemon's termination during program shutdown, even if explicit cleanup is missed.

## Cross-Unit Boundaries

| Member | Direction | Other Unit | Interaction Details |
| :--- | :--- | :--- | :--- |
| `stopDaemon` | Calls Out | `Config/GetStringDefault` | Retrieves the `"PidFile"` configuration value. |
| `startDaemon` | Called By | `realmd_Main/main` | Starts daemonization during realm server initialization. |
| `stopDaemon` | Called By | `realmd_Main/main` | Stops the daemon via CLI arguments. |
| `detachDaemon` | Called By | `Master/Run` | Signals successful startup to the parent. |
| `detachDaemon` | Called By | `realmd_Main/main` | Alternative path for signaling startup completion. |

## Data Model

This unit does not interact with any database tables. It operates on process IDs, session IDs, and file descriptors.

## Notable Implementation Details

- **Global State**: Relies on global `parent_pid` and `sid` to track process relationships.
- **Watchdog Idiom**: The global `WatchDog` object ensures `exitDaemon` is called during C++ runtime shutdown, notifying the parent of abnormal or normal exits.
- **Signal Forwarding**: `daemonSignal` forwards signals to the session leader (`sid`) before exiting, potentially coordinating shutdown across the session group.
- **Parent Wait Logic**: The parent process in `startDaemon` blocks in `pause()` until it receives `SIGUSR1` from the child or times out via `alarm`.

## Member Reference

**daemonSignal**: Signal handler for `SIGUSR1`, `SIGINT`, `SIGTERM`, `SIGALRM`. Parent exits on `SIGUSR1` or forwards other signals to session leader and fails. Daemon forwards signals to session leader and fails.

**startDaemon**: Forks process. Parent waits for `SIGUSR1` with timeout. Child sets session, changes dir to `/`, redirects I/O to `/dev/null`.

**stopDaemon**: Reads PID from config-specified file, sends `SIGINT` to that PID.

**detachDaemon**: Sends `SIGUSR1` to `parent_pid` to signal successful daemonization.

**exitDaemon**: Sends `SIGTERM` to `parent_pid` if current process is the daemon child.

**~WatchDog**: Destructor for global `WatchDog` instance; calls `exitDaemon` to notify parent on shutdown.

---

<!-- machine-true, projected from graph.json -->

## Map — PosixDaemon

*Source:* PosixDaemon.cpp, PosixDaemon.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| daemonSignal | function | — | — | — |
| startDaemon | function | — | realmd_Main/main | — |
| stopDaemon | function | Config/GetStringDefault | realmd_Main/main | — |
| detachDaemon | function | — | Master/Run, realmd_Main/main | — |
| exitDaemon | function | — | — | — |
| ~WatchDog | dtor | — | — | — |
