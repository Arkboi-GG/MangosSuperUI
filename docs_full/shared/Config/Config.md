# Config

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Config

`Config` is a thread-safe singleton that parses flat-text configuration files into an in-memory key-value store. It provides typed accessors for strings, booleans, integers, and floats, returning user-specified defaults when keys are missing. The class uses a `std::shared_timed_mutex` to allow concurrent reads while serializing writes during file reloads.

## Purpose & Responsibilities

*   **Parsing:** Reads `Key=Value` pairs from a text file, handling whitespace, comments (`#`), and quoted values.
*   **Storage:** Maintains a `std::unordered_map<std::string, std::string>` of configuration entries.
*   **Access:** Provides type-safe retrieval methods with default fallbacks.
*   **Concurrency:** Protects the map with a shared mutex; `Reload` acquires an exclusive lock, while getters acquire shared locks.

## Member-by-Member Behavior

### Loading and Parsing

*   **`LoadFromFile`**: Sets the target filename and delegates to `Reload`. Called by `realmd_Main/main` at startup.
*   **`Reload`**: Opens the file specified by `m_fileName`, clears the existing map, and parses each line via `ProcessLine`. It acquires an exclusive lock on `m_configLock` to prevent race conditions. Returns `false` if the file cannot be opened or if the resulting map is empty. Called by `World/LoadConfigSettings` for hot-reloading.
*   **`ProcessLine`**: A state-machine parser that extracts a key and value from a single line. It ignores lines starting with `#` or `[` (sections are unsupported). It strips surrounding double quotes from values. If a key is duplicated, it prints a warning to stdout and retains the first occurrence.
*   **`IsLineEndChar`**: Static helper identifying line terminators (`\0`, `\n`, `\r`). Used internally by `ProcessLine`.

### Retrieval

*   **`GetValueHelper`**: Private helper that looks up a key in `m_configMap` under a shared lock. Returns `true` and populates the result string if found.
*   **`IsSet`**: Checks for key existence by calling `GetValueHelper` and discarding the value. Called by `RASocket/RASocket`.
*   **`GetStringDefault`**: Returns the string value for a key, or the provided default if missing. Widely used by `AuthSocket`, `ChatHandler`, `Database`, `Log.Main`, `Master`, `RASocket`, and `World`.
*   **`GetBoolDefault`**: Parses boolean values. Strings `"true"`, `"TRUE"`, `"yes"`, `"YES"`, and `"1"` evaluate to `true`; all others fall back to the default. Used by `AuthSocket`, `ChatHandler`, `Database`, `Log.Main`, `Master`, `RASocket`, and `World`.
*   **`GetIntDefault`**: Converts the value to `int32` using `std::stoi`. Throws `std::invalid_argument` or `std::out_of_range` if the value is not a valid integer. Used by `AuthSocket`, `ChatHandler`, `Database`, `Log.Main`, `Master`, `RASocket`, and `World`.
*   **`GetFloatDefault`**: Converts the value to `float` using `std::stof`. Throws on invalid input. Used by `World`.

### Utility

*   **`GetFilename`**: Returns the path of the currently loaded configuration file. Called by `realmd_Main/main` and `World/LoadConfigSettings`.

## Cross-Unit Boundaries

*   **`realmd_Main/main`**: Calls `LoadFromFile` to initialize the realm daemon config, and `GetFilename`/`Get...Default` methods to retrieve startup parameters.
*   **`World/LoadConfigSettings`**: Calls `Reload` to apply configuration changes at runtime, and `GetFilename`/`Get...Default` methods to populate world server settings.
*   **`AuthSocket`**: Uses `Get...Default` methods to configure authentication logic (version checks, geographical locks, logon proofs).
*   **`ChatHandler`**: Subsystems like `AuctionHouseBotMgr` and `PlayerBotMgr` load their specific configurations via `Get...Default`.
*   **`Database/Initialize`**: Retrieves database connection parameters and initialization flags.
*   **`Log.Main`**: Determines logging levels, file paths, and formatting options.
*   **`Master`**: Configures remote access servers and database start procedures.
*   **`RASocket`**: Uses `IsSet` and `Get...Default` to configure remote administration sockets.

## Data Model

This unit does not interact with any database tables. It operates exclusively on a flat-text configuration file.

## Notable Implementation Details

*   **Duplicate Keys:** If a key appears twice, the first occurrence is kept. A warning is printed to stdout: `"Config setting '%s' appear twice in config! Ignoring second occurrence."`
*   **Quote Stripping:** Double quotes around values are removed during parsing. `Key="Value"` stores `Value`.
*   **Section Ignorance:** Lines starting with `[` are rejected by `ProcessLine`, meaning INI-style sections are not supported.
*   **Boolean Strictness:** Only `"true"`, `"TRUE"`, `"yes"`, `"YES"`, and `"1"` are considered true. `"on"`, `"t"`, or `"2"` result in the default value.
*   **Exception Risk:** `GetIntDefault` and `GetFloatDefault` use `std::stoi`/`std::stof` without try-catch blocks. Malformed numeric values will throw exceptions, potentially crashing the caller if not handled upstream.
*   **Thread Safety:** `std::shared_timed_mutex` enables high-concurrency reads. `Reload` blocks all readers until the new map is fully populated.

## Member Reference

**IsLineEndChar**
Static helper that checks if a character is a newline, carriage return, or null terminator. Used internally by `ProcessLine`.

**LoadFromFile**
Sets the configuration filename and triggers a `Reload`. Called by `realmd_Main/main`.

**Reload**
Re-reads the configuration file, clearing the old map and parsing new content under an exclusive lock. Called by `World/LoadConfigSettings`.

**ProcessLine**
Private parser that extracts key-value pairs from a line, handling comments, quotes, and duplicates.

**GetFilename**
Returns the path of the currently loaded configuration file. Called by `realmd_Main/main` and `World/LoadConfigSettings`.

**GetValueHelper**
Private helper that looks up a key in the configuration map under a shared lock.

**IsSet**
Checks if a configuration key exists. Called by `RASocket/RASocket`.

**GetStringDefault**
Retrieves a string configuration value, returning a default if not found. Called by `AuthSocket`, `ChatHandler`, `ClientPatchCache`, `Database`, `Log.Main`, `Master`, `PosixDaemon`, `realmd_Main`, and `World`.

**GetBoolDefault**
Retrieves a boolean configuration value, interpreting specific strings as true. Called by `AuthSocket`, `ChatHandler`, `CliRunnable`, `Database`, `Log.Main`, `Master`, `RASocket`, `realmd_Main`, and `World`.

**GetIntDefault**
Retrieves an integer configuration value using `std::stoi`. Called by `AuthSocket`, `ChatHandler`, `Database`, `Log.Main`, `Master`, `RASocket`, `realmd_Main`, `World`, and `WorldSocket`.

**GetFloatDefault**
Retrieves a floating-point configuration value using `std::stof`. Called by `World`.

---

<!-- machine-true, projected from graph.json -->

## Map — Config

*Source:* Config.cpp, Config.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| IsLineEndChar | function | — | — | — |
| LoadFromFile | method | — | realmd_Main/main | — |
| Reload | method | — | World/LoadConfigSettings | — |
| ProcessLine | method | — | — | — |
| GetFilename | method | — | realmd_Main/main, World/LoadConfigSettings | — |
| GetValueHelper | method | — | — | — |
| IsSet | method | — | RASocket/RASocket | — |
| GetStringDefault | method | — | AuthSocket/_HandleLogonProof__PostRecv_HandleInvalidVersion, ChatHandler.CharacterCommands/HandleServiceDeleteCharacters, ClientPatchCache/LoadPatchesInfo, Database/Initialize, Log.Main/Log, Log.Main/OpenLogFile, Master/Run, Master/SetupRemoteAccessServer, Master/StartDB, PosixDaemon/stopDaemon, realmd_Main/main, realmd_Main/StartDB, World/LoadConfigSettings | — |
| GetBoolDefault | method | — | AuthSocket/GeographicalLockCheck, AuthSocket/VerifyVersion, AuthSocket/_HandleLogonChallenge, AuthSocket/_HandleLogonProof__PostRecv, ChatHandler.AuctionHouseBotMgr/Load, ChatHandler.PlayerBotMgr/LoadConfig, CliRunnable/operator(), Database/Initialize, Log.Main/Log, Log.Main/OpenWorldLogFiles, Master/Run, RASocket/RASocket, realmd_Main/main, World/configNoReload, World/LoadConfigSettings, World/setConfig | — |
| GetIntDefault | method | — | AuthSocket/Start, AuthSocket/_HandleLogonChallenge, AuthSocket/_HandleLogonProof__PostRecv, AuthSocket/_HandleRealmList, ChatHandler.AuctionHouseBotMgr/Load, ChatHandler.PlayerBotMgr/LoadConfig, ChatHandler.ServerCommands/HandleServerPLimitCommand, Database/Initialize, Log.Main/Log, Log.Main/WaitBeforeContinueIfNeed, Master/Run, Master/SetupRemoteAccessServer, Master/StartDB, Master/_StartDB, RASocket/HandleInput_GotUsername, realmd_Main/main, World/configNoReload#3, World/configNoReload#4, World/DetectDBCLang, World/LoadConfigSettings, World/setConfig#5, World/setConfig#7, WorldSocket/Start | — |
| GetFloatDefault | method | — | World/configNoReload#2, World/LoadConfigSettings, World/setConfig#3 | — |
