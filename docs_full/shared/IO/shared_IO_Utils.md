# shared_IO_Utils

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# shared_IO_Utils

## Purpose & Responsibilities

`shared_IO_Utils` is a minimal utility unit providing a single cross-platform abstraction for retrieving the current process identifier (PID). It exposes `IO::Utils::GetCurrentProcessId`, which returns the PID as a 64-bit unsigned integer (`uint64_t`). The implementation delegates to the native operating system API: `::GetCurrentProcessId()` on Windows and `::getpid()` on POSIX-compliant systems. This unit exists solely to shield callers from platform-specific headers and function signatures when a PID is required.

## Member-by-Member Behavior

The unit contains only one member:

*   **`GetCurrentProcessId`**: Returns the unique identifier of the calling process. On Windows, it invokes the Win32 API `GetCurrentProcessId`. On non-Windows platforms, it invokes the POSIX `getpid` function. The result is cast implicitly to `uint64_t` to satisfy the declared return type, ensuring consistent width regardless of the underlying OS type (which is typically `DWORD` on Windows and `pid_t` on POSIX).

## Cross-Unit Boundaries

*   **Called by `shared_Util/CreatePIDFile`**: The function `CreatePIDFile` in the `shared_Util` unit calls `GetCurrentProcessId` to obtain the PID to write into a process ID file. This allows external monitoring tools or scripts to identify the running instance of the application. No data flows back from `shared_Util` to this unit; the interaction is strictly a request for the PID value.

## Data Model

This unit does not interact with any database tables. It relies entirely on operating system APIs.

## Notable Implementation Details

*   **Platform Conditional Compilation**: The implementation uses preprocessor directives (`#ifdef WIN32`) to include `<Windows.h>` or `<unistd.h>` and select the appropriate system call. This ensures the code compiles cleanly on both major supported platforms without exposing platform-specific headers to consumers of `Utils.h`.
*   **Return Type Consistency**: The function returns `uint64_t`. While `getpid()` typically returns a signed integer (`pid_t`) and `GetCurrentProcessId()` returns an unsigned 32-bit integer (`DWORD`), the implicit conversion to `uint64_t` guarantees a uniform interface for callers, avoiding potential sign-extension issues or width mismatches in higher-level logic.

## Member Reference

**GetCurrentProcessId**: A free function in the `IO::Utils` namespace that returns the current process ID as a `uint64_t`. It uses `::GetCurrentProcessId()` on Windows and `::getpid()` on other platforms.

---

<!-- machine-true, projected from graph.json -->

## Map — shared_IO_Utils

*Source:* Utils.cpp, Utils.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GetCurrentProcessId | function | — | shared_Util/CreatePIDFile | — |
