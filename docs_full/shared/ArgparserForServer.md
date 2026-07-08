# ArgparserForServer

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ArgparserForServer

## Purpose & Responsibilities

`ArgparserForServer` parses command-line arguments for the WoWVMaNGOS server executables. It validates flags for help, version, configuration file paths, and platform-specific service/daemon management actions. It returns a structured `ServerStartupArguments` object or an exit code via `nonstd::expected`, enabling the caller (`realmd_Main/main`) to handle startup logic or termination cleanly.

## Member-by-Member Behavior

### Argument Parsing

**`ParseServerStartupArguments`**
Iterates through `argc` and `argv` to populate a `ServerStartupArguments` struct.
1.  **Setup:** Skips `argv[0]` (executable name), initializes `args` with `ServiceDaemonAction::NotSet` and an empty config path.
2.  **Loop:** Processes each argument:
    *   `-h` / `--help`: Calls `printUsage`, returns `EXIT_SUCCESS`.
    *   `-v` / `--version`: Prints `_FULLVERSION`, returns `EXIT_SUCCESS`.
    *   `-c` / `--config`: Requires a next argument. If missing, errors with `EXIT_FAILURE`. Otherwise, stores the next argument in `args.configFilePath`.
    *   `-s`: Requires a next argument. If missing, errors with `EXIT_FAILURE`. Validates the sub-argument:
        *   `run`: Sets `args.inputServiceMode` to `ServiceDaemonAction::Start`.
        *   `install` / `uninstall` (Windows only): Sets mode to `Install` or `Uninstall`.
        *   `stop` (Unix only): Sets mode to `Stop`.
        *   Invalid sub-argument: Errors with `EXIT_FAILURE`.
    *   Unknown argument: Prints error, calls `printUsage`, returns `EXIT_FAILURE`.
3.  **Result:** Returns the populated `args` on success.

### Usage Display

**`printUsage`**
Prints a formatted help string to `stdout`. Content varies by platform:
*   **Windows:** Lists service actions (`run`, `install`, `uninstall`).
*   **Unix:** Lists daemon actions (`run`, `stop`).
*   **Common:** Lists `-v/--version` and `-c/--config`. Note: Help text contains a typo ("exist" instead of "exit").

## Cross-Unit Boundaries

*   **Called by `realmd_Main/main`:** Receives raw `argc`/`argv`. Uses the returned `nonstd::expected` to either exit (on error/help/version) or proceed with server initialization using the parsed `ServerStartupArguments`.
*   **Includes `SystemConfig.h`:** Included for macro definitions like `_FULLVERSION`, though no runtime functions from `SystemConfig` are called in this unit.

## Data Model

This unit does not interact with any database tables.

## Notable Implementation Details

*   **Argument Mutability:** The function modifies local copies of `argc` and `argv` (incrementing/decrementing) to iterate. This is safe as they are passed by value/pointer copy.
*   **Strict Syntax:** Flags requiring arguments (`-c`, `-s`) must be immediately followed by their value. Combined flags (e.g., `-cv`) or `--key=value` syntax are not supported.
*   **Platform Specificity:** `ServiceDaemonAction` enum and `-s` parsing logic depend on the `WIN32` macro. Windows supports install/uninstall; Unix supports stop. `run` is common to both.
*   **Exit Codes:** Distinguishes between informational exits (`EXIT_SUCCESS` for help/version) and errors (`EXIT_FAILURE`), allowing the caller to differentiate user intent from mistakes.

## Member Reference

**`printUsage`**
Prints a platform-specific usage string to `stdout`, detailing valid command-line options. Uses preprocessor directives to include Windows-specific service options (`install`, `uninstall`) or Unix-specific daemon options (`stop`).

**`ParseServerStartupArguments`**
Parses `argc`/`argv` into `ServerStartupArguments`. Handles `-h` (help), `-v` (version), `-c` (config file), and `-s` (service/daemon mode). Returns `nonstd::expected<ServerStartupArguments, int>`, yielding the arguments on success or an exit code (`EXIT_SUCCESS`/`EXIT_FAILURE`) on informational output or error.

---

<!-- machine-true, projected from graph.json -->

## Map — ArgparserForServer

*Source:* ArgparserForServer.cpp, ArgparserForServer.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| printUsage | function | — | — | — |
| ParseServerStartupArguments | function | — | realmd_Main/main | — |
