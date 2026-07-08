# WardenScanMgr

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`WardenScanMgr` is the central registry and loader for Warden anti-cheat scans within the WowVMaNGOS server. It maintains a global collection of `Scan` objects (`m_scans`) that define specific integrity checks the server can request from connected clients. These scans cover various domains, including memory inspection, file hashing, Lua variable verification, and driver detection.

The manager has two primary responsibilities:
1.  **Persistence:** Loading scan definitions from the `warden_scans` database table during server startup via `LoadFromDB`.
2.  **Selection:** Providing a filtered, randomized subset of scans suitable for a specific client context (OS, game build, scan type) via `GetRandomScans`, ensuring the resulting payload fits within network buffer limits.

It acts as a bridge between the static configuration in the database, dynamic scans added by platform-specific modules (`WardenWin`, `WardenMac`), and the runtime selection logic used by the Warden system to challenge players.

## Member-by-Member Behavior

### Initialization and Database Loading

**`LoadFromDB`**
This method populates the internal `m_scans` vector from the `warden_scans` table.
1.  **Preservation of Dynamic Scans:** Before loading, it iterates through existing scans in `m_scans`. Any scan *not* marked with `ScanFlags::FromDatabase` is preserved in a temporary vector. This allows scans added programmatically (e.g., by `WardenWin::LoadScriptedScans`) to survive a database reload or re-initialization.
2.  **Query Execution:** It executes a `SELECT` query on `warden_scans`. If the result set is empty, it logs an error and returns.
3.  **Row Processing:** For each row:
    *   It validates the `type` field against `MAX_SCAN_TYPE`. Invalid types are skipped with a log warning.
    *   It extracts common fields: `id`, `offset`, `length`, `flags`, `penalty`, `build_min`, `build_max`, and `comment`.
    *   **Penalty Handling:** If the `penalty` from the DB is outside the valid range (`WARDEN_ACTION_LOG` to `WARDEN_ACTION_MAX`), it defaults to the server configuration value `CONFIG_UINT32_AC_WARDEN_DEFAULT_PENALTY`.
    *   **Scan Construction:** Based on `scanType`, it constructs a specific `Scan` subclass:
        *   `READ_MEMORY`: Uses `BuildRawData` to parse the `result` column (hex string) into a byte vector. If the parsed size doesn't match the `length` column, the scan is skipped. It creates a `WindowsMemoryScan`, optionally using the `str` column as a module name base.
        *   `FIND_MODULE_BY_NAME`: Creates a `WindowsModuleScan` using `str` as the module name and `result` as a boolean indicator of whether the module is expected.
        *   `FIND_MEM_IMAGE_CODE_BY_HASH` / `FIND_CODE_BY_HASH`: Parses the `data` column via `BuildRawData` to create a `WindowsCodeScan`.
        *   `HASH_CLIENT_FILE`: Parses the `result` column as a SHA1 hash. If `result` is empty, it indicates a "bad file" check (hash must *not* match). If present, it's a "good file" check. Creates a `WindowsFileHashScan`.
        *   `GET_LUA_VARIABLE`: Creates a `WindowsLuaScan`. If `data` is empty, it uses the boolean `result` to indicate existence checks. Otherwise, it compares against the string in `data`.
        *   `API_CHECK`: Parses `result` as a SHA1 hash for a `WindowsHookScan`, checking specific API hooks in `str` (module) and `data` (procedure).
        *   `FIND_DRIVER_BY_NAME`: Creates a `WindowsDriverScan` using `str` (driver name) and `data` (path).
    *   **Finalization:** Sets the `checkId` and `penalty` on the created scan object and adds it to `m_scans` wrapped in a `shared_ptr`.
4.  **Logging:** Logs the total number of scans loaded.

**`Count`**
Returns the current size of the `m_scans` vector. Used by `Log.Warden` and `LoadScriptedScans` to report status.

### Adding Dynamic Scans

These methods allow other units to inject scans into the manager without touching the database. They are primarily used by platform-specific loaders.

**`AddMacScan` (two overloads)**
Adds a `MacScan` to the registry. One overload takes a raw pointer (taking ownership via `shared_ptr` construction), the other takes a `shared_ptr`. Called by `WardenMac::LoadScriptedScans`.

**`AddWindowsScan` (two overloads)**
Adds a `WindowsScan` to the registry. Similar to `AddMacScan`, handles both raw pointers and `shared_ptr`s. Called by `WardenWin::LoadScriptedScans`.

### Scan Selection

**`GetRandomScans`**
Selects a subset of scans appropriate for a specific client request.
1.  **Filtering:** Iterates through all scans in `m_scans` and keeps those that match:
    *   **OS Mask:** The scan's OS flags (`Windows` or `Mac`) must overlap with the requested `flags`.
    *   **Build Range:** The client's `build` must be within `[scan->buildMin, scan->buildMax]`.
    *   **Context Flags:** Exact matches required for `InitialLogin` and `Maiev` flags. If the scan requires `OffsetsInitialized`, the request flags must also include it.
2.  **Randomization:** Shuffles the matching scans using `std::shuffle` with a Mersenne Twister engine seeded by `std::random_device`. This prevents predictable scan patterns.
3.  **Limiting:**
    *   First, it caps the number of scans to `CONFIG_UINT32_AC_WARDEN_NUM_SCANS` if the match count exceeds this config value.
    *   Second, it iterates through the shuffled list, accumulating `requestSize` and `replySize`. If adding the next scan would exceed `Warden::MaxRequest` or `Warden::MaxReply`, it stops and resizes the result vector to exclude that scan and any subsequent ones.
4.  **Return:** Returns the vector of selected `shared_ptr<Scan const>`.

### Internal Helpers

**`BuildRawData`**
A static helper function (defined in the anonymous namespace of `WardenScanMgr.cpp`) that converts a hexadecimal string into a `std::vector<uint8>`.
*   Validates that the input string length is even.
*   Processes pairs of characters, converting '0'-'9' and 'A'-'F' to their numeric values.
*   Returns `false` if any character is invalid or the length is odd.
*   Used extensively in `LoadFromDB` to parse binary data stored as text in the database.

## Cross-Unit Boundaries

*   **`LoadFromDB`**:
    *   Calls `WorldDatabase.Query` to fetch scan definitions.
    *   Calls `sLog.Out` to log errors and completion status.
    *   Calls `sWorld.getConfig` to retrieve the default penalty value if the database value is invalid.
    *   Constructs objects from `WardenScan` subclasses (`WindowsMemoryScan`, `WindowsModuleScan`, etc.).
    *   Called by `Anticheat::LoadAnticheatData` during server initialization.

*   **`GetRandomScans`**:
    *   Calls `sWorld.getConfig` to get the maximum number of scans allowed per request.
    *   Accesses `Warden::MaxRequest` and `Warden::MaxReply` constants to enforce buffer limits.
    *   Called by `Log.Warden::SelectScans` to determine which scans to send to a player.

*   **`AddMacScan`**:
    *   Called by `WardenMac::LoadScriptedScans` to register Mac-specific scans.

*   **`AddWindowsScan`**:
    *   Called by `WardenWin::LoadScriptedScans` to register Windows-specific scans.

*   **`Count`**:
    *   Called by `Log.Warden` and `LoadScriptedScans` for reporting.

## Data Model

The unit interacts with one database table:

**`warden_scans`**
Stores the definitions for all database-driven Warden scans.

| Column | Type | Description |
| :--- | :--- | :--- |
| `id` | `smallint(5) unsigned PK` | Unique identifier for the scan. |
| `type` | `int(11)?` | Enumerated type of the scan (e.g., `READ_MEMORY`, `HASH_CLIENT_FILE`). |
| `str` | `text?` | Variable string data, often a module name, filename, or variable name. |
| `data` | `text?` | Variable data, often a procedure name, path, or expected value string. |
| `address` | `int(11)?` | Memory offset for memory/code scans. |
| `length` | `int(11)?` | Length of data to read/check. |
| `result` | `tinytext` | Expected result, often a hex-encoded byte sequence or boolean flag. |
| `flags` | `mediumint(8) unsigned` | Bitmask of `ScanFlags` (e.g., OS, context, initialization requirements). |
| `penalty` | `tinyint(4)` | Action to take if the scan fails (e.g., log, kick, ban). |
| `build_min` | `smallint(5) unsigned` | Minimum client build version for this scan. |
| `build_max` | `smallint(5) unsigned` | Maximum client build version for this scan. |
| `comment` | `tinytext` | Human-readable description of the scan. |

## Notable Implementation Details

*   **Hex Parsing Robustness:** `BuildRawData` manually parses hex strings. It only accepts uppercase 'A'-'F'. Lowercase hex digits will cause the function to return `false`, potentially skipping valid scans if the database contains lowercase hex. This is a strict constraint.
*   **Dynamic Scan Persistence:** `LoadFromDB` carefully preserves scans not marked `FromDatabase`. This design allows hybrid configurations where some scans are hardcoded/scripted in C++ (via `AddWindowsScan`/`AddMacScan`) and others are loaded from the DB, without one overwriting the other.
*   **Buffer Limit Enforcement:** `GetRandomScans` enforces hard limits on the size of the Warden request and reply packets. It does this by iterating through the shuffled list and stopping when the cumulative size exceeds `Warden::MaxRequest` or `Warden::MaxReply`. This prevents network fragmentation or packet rejection due to oversized payloads.
*   **Default Penalty Fallback:** If a scan's `penalty` in the database is invalid, `LoadFromDB` falls back to `CONFIG_UINT32_AC_WARDEN_DEFAULT_PENALTY`. This ensures scans don't silently fail to apply penalties due to bad data, but instead use a server-wide safe default.
*   **SHA1 Hash Handling:** For `HASH_CLIENT_FILE` and `API_CHECK`, the code expects the `result` column to contain a hex-encoded SHA1 digest. It uses `Crypto::Hash::SHA1::CreateZero()` and `std::copy_n` to populate the digest structure. If the hex string length doesn't match `SHA1::Digest::size`, the scan is skipped.
*   **Singleton Pattern:** `WardenScanMgr` is instantiated as a singleton (`INSTANTIATE_SINGLETON_1`) and accessed via `sWardenScanMgr`. This ensures a single global registry of scans.

## Member Reference

**`BuildRawData`**
Static helper function that converts a hexadecimal string into a `std::vector<uint8>`. Validates even length and uppercase hex characters. Returns `false` on failure.

**`Count`**
Returns the number of scans currently registered in `m_scans`.

**`LoadFromDB`**
Loads scan definitions from the `warden_scans` table. Preserves non-database scans. Parses each row into the appropriate `Scan` subclass based on `type`. Handles hex decoding, penalty defaults, and build range validation. Logs errors for invalid rows.

**`AddMacScan#2`**
Overload of `AddMacScan` that accepts a `std::shared_ptr<MacScan>`. Adds the scan to `m_scans`.

**`AddMacScan`**
Overload of `AddMacScan` that accepts a raw `MacScan const*`. Wraps it in a `shared_ptr` and adds it to `m_scans`.

**`AddWindowsScan#2`**
Overload of `AddWindowsScan` that accepts a `std::shared_ptr<WindowsScan>`. Adds the scan to `m_scans`.

**`AddWindowsScan`**
Overload of `AddWindowsScan` that accepts a raw `WindowsScan const*`. Wraps it in a `shared_ptr` and adds it to `m_scans`.

**`GetRandomScans`**
Filters `m_scans` based on OS, build range, and context flags. Shuffles the results. Limits the count to `CONFIG_UINT32_AC_WARDEN_NUM_SCANS` and ensures the total request/reply size fits within `Warden::MaxRequest`/`Warden::MaxReply`. Returns the selected scans.

---

<!-- machine-true, projected from graph.json -->

## Map — WardenScanMgr

*Source:* WardenScanMgr.cpp, WardenScanMgr.hpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| BuildRawData | function | — | — | — |
| Count | method | — | Log.Warden/LoadScriptedScans | — |
| LoadFromDB | method | Database/Query, Digest/size#2, Field/GetBool, Field/GetCppString, Field/GetUInt16, Field/GetUInt32, Field/GetUInt8, Generator.SHA1/CreateZero, Log.Main/Out, QueryResult/Fetch, QueryResult/NextRow, WardenScan/operator!, WardenScan/operator&, WardenScan/operator|, WardenScan/WindowsCodeScan, WardenScan/WindowsDriverScan, WardenScan/WindowsFileHashScan, WardenScan/WindowsHookScan, WardenScan/WindowsLuaScan#2, WardenScan/WindowsLuaScan#3, WardenScan/WindowsMemoryScan, WardenScan/WindowsMemoryScan#3, WardenScan/WindowsModuleScan#2, World/getConfig#4 | Anticheat/LoadAnticheatData | warden_scans |
| AddMacScan#2 | method | — | — | — |
| AddMacScan | method | — | WardenMac/LoadScriptedScans | — |
| AddWindowsScan#2 | method | — | — | — |
| AddWindowsScan | method | — | WardenWin/LoadScriptedScans | — |
| GetRandomScans | method | WardenScan/operator!, WardenScan/operator&, WardenScan/operator|, World/getConfig#4 | Log.Warden/SelectScans | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `warden_scans`: id smallint(5) unsigned PK, type int(11)?, str text?, data text?, address int(11)?, length int(11)?, result tinytext, flags mediumint(8) unsigned, penalty tinyint(4), build_min smallint(5) unsigned, build_max smallint(5) unsigned, comment tinytext

*`?` = nullable, `PK` = primary key column.*

