<!-- provenance: verbose, failed-members -->
# CliRunnable

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# CliRunnable

**CliRunnable** implements the main loop for the server’s command-line interface (CLI). Running in its own thread, it reads user input from `stdin`, converts it to UTF-8, and queues it for execution by the core world server via `World::QueueCliCommand`. It manages the lifecycle of a dedicated MySQL thread context for the world database, ensuring CLI commands have a valid connection handle.

## Purpose & Responsibilities

1.  **Manage CLI Thread Lifecycle:** Initializes and cleans up a MySQL thread context (`WorldDatabase`) for safe database interaction during command execution.
2.  **Poll for Input:** Uses platform-specific mechanisms (`select` on POSIX/Linux, blocking `fgets` on Windows) to wait for user input without blocking the server process.
3.  **Normalize Input:** Strips whitespace and converts raw console encoding to UTF-8.
4.  **Dispatch Commands:** Wraps processed commands in `CliCommandHolder` objects and queues them to the `World` singleton.
5.  **Provide Feedback:** Supplies callbacks (`utf8print`, `commandFinished`) for displaying output and prompts.

## Member-by-Member Behavior

### **operator()**

The core entry point for the CLI thread.

1.  **Initialization:**
    *   Calls `DatabaseMysql::ThreadStart` on `WorldDatabase` to establish a thread-local MySQL connection.
    *   Checks `Config::GetBoolDefault("BeepAtStart", true)`; if true, prints an alert character (`\a`).
    *   Prints the initial prompt `mangos>`.

2.  **Main Loop:**
    *   Loops while `World::IsStopped()` is false.
    *   **Linux Polling:** Uses local helper `kb_hit_return()` with `select` and `usleep(100)` to detect keystrokes without busy-waiting.
    *   **POSIX Waiting:** On non-Windows/Non-Linux POSIX, uses `select` with a 1-second timeout on `stdin`. If `select` fails (-1), calls `World::StopNow(SHUTDOWN_EXIT_CODE)`.
    *   **Reading:** Uses `fgets` to read up to 255 characters into `commandbuf`.
    *   **Processing:**
        *   If `fgets` returns `nullptr` due to EOF, calls `World::StopNow(SHUTDOWN_EXIT_CODE)`.
        *   Strips `\r` and `\n` from the buffer.
        *   If the string is empty, prints the prompt and continues.
        *   Calls `shared_Util::consoleToUtf8` to convert input to UTF-8. If conversion fails, prints the prompt and continues.
    *   **Queuing:** Creates a `CliCommandHolder` with the command, `SEC_CONSOLE` security level, and pointers to `utf8print` and `commandFinished`. Passes it to `World::QueueCliCommand`.

3.  **Cleanup:**
    *   Calls `DatabaseMysql::ThreadEnd` on `WorldDatabase` to release resources.

### **utf8print**

Static callback for displaying command output.

*   **Windows:** Converts UTF-8 to `std::wstring` via `Utf8toWStr`, then to OEM code page via `CharToOemBuffW`, and prints via `printf`. Returns early if UTF-8 to wide conversion fails.
*   **Non-Windows:** Prints the UTF-8 string directly via `printf`.

### **commandFinished**

Static callback invoked after command execution. Prints `mangos>` and flushes `stdout`.

## Cross-Unit Boundaries

*   **`CliCommandHolder` (CliCommandHolder.cpp):** `operator()` creates instances to wrap user input for execution.
*   **`Config` (Config.cpp):** `operator()` calls `GetBoolDefault` to check the startup beep setting.
*   **`DatabaseMysql` (DatabaseMysql.cpp):** `operator()` calls `ThreadStart` and `ThreadEnd` on `WorldDatabase` to manage the MySQL connection lifecycle.
*   **`shared_Util` (Util.cpp):** `operator()` calls `consoleToUtf8` to normalize input encoding.
*   **`World` (World.cpp):**
    *   `operator()` calls `IsStopped` to check server state.
    *   `operator()` calls `QueueCliCommand` to submit commands.
    *   `operator()` calls `StopNow` to initiate shutdown on I/O errors or EOF.

## Data Model

This unit does not directly query or modify database tables. It initializes a database thread context (`WorldDatabase`) so that queued commands can access the database, but `CliRunnable` itself performs no SQL operations.

## Notable Implementation Details

*   **Platform-Specific Input:** Linux uses a custom `kb_hit_return` helper with `usleep` to avoid busy-waiting. Other POSIX systems use `select` with a 1-second timeout. Windows relies on blocking `fgets`.
*   **Encoding Conversion:** On Windows, `utf8print` performs UTF-8 -> Wide String -> OEM conversion. Failure to convert UTF-8 to Wide String results in silent failure.
*   **Buffer Limit:** `commandbuf` is fixed at 256 bytes; commands >255 chars are truncated.
*   **Shutdown Triggers:** The thread triggers `World::StopNow` if `select` fails on POSIX or if `fgets` hits EOF.
*   **Unused Callback Args:** `utf8print` and `commandFinished` accept `void*` args that are unused, fitting a generic callback signature.

## Member Reference

**utf8print**: Static callback that prints a UTF-8 string to the console. On Windows, it converts the string to the OEM code page for correct display; on other platforms, it prints directly.

**commandFinished**: Static callback that prints the `mangos>` prompt and flushes stdout after a command completes.

**operator()**: The main thread function. Initializes the MySQL thread context, enters a loop to read and process user input from stdin, converts input to UTF-8, queues commands via `World::QueueCliCommand`, and cleans up the MySQL thread context upon exit. Handles platform-specific input polling and shutdown triggers.

---

<!-- machine-true, projected from graph.json -->

## Map — CliRunnable

*Source:* CliRunnable.cpp, CliRunnable.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| utf8print | function | — | — | — |
| commandFinished | function | — | — | — |
| operator() | method | CliCommandHolder/CliCommandHolder, Config/GetBoolDefault, DatabaseMysql/ThreadEnd, DatabaseMysql/ThreadStart, shared_Util/consoleToUtf8, World/IsStopped, World/QueueCliCommand, World/StopNow | — | — |

---

<!-- verify: failed-members | invented: operator -->
