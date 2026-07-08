<!-- provenance: verbose -->
# AutoBroadCastMgr

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AutoBroadCastMgr

**Purpose & Responsibilities**

`AutoBroadCastMgr` is a singleton that periodically broadcasts random text messages to all connected players. It loads message identifiers from the `autobroadcast` database table, stores them in memory, and triggers a broadcast at fixed intervals defined by the server configuration.

**Member-by-Member Behavior**

### Initialization and Lifecycle

*   **`AutoBroadCastMgr` (Constructor)**: Retrieves the broadcast interval from `World/getConfig#4` (`CONFIG_UINT32_AUTOBROADCAST_INTERVAL`) and stores it in `_constInterval`. Resets the internal timer `_current` to 0.
*   **`~AutoBroadCastMgr` (Destructor)**: Clears the `entries` vector.

### Data Loading

*   **`Load`**: Repopulates `entries` from the `autobroadcast` table.
    *   Executes `SELECT \`string_id\` FROM \`autobroadcast\`` via `Database/Query`.
    *   If the query fails or returns no results, logs "Loaded 0 AutoBroadCast message" via `Log.Main/Out` and returns.
    *   Otherwise, iterates through `QueryResult` using `QueryResult/NextRow` and `QueryResult/Fetch`. For each row, it extracts `string_id` via `Field/GetInt32`, creates an `AutoBroadCastEntry`, and appends it to `entries`.
    *   Uses `ProgressBar/BarGoLink` and `ProgressBar/step` for console progress feedback.
    *   Logs the final count of loaded messages via `Log.Main/Out`.
    *   Called by `World/SetInitialWorldSettings` at startup and `ChatHandler.ServerCommands/HandleReloadAutoBroadcastCommand` for runtime reloads.

### Periodic Execution

*   **`Update`**: Driven by `World/Update` with a time delta `diff`.
    *   Returns immediately if `entries` is empty.
    *   Accumulates `diff` into `_current`.
    *   If `_current >= _constInterval`, selects a random entry from `entries` using `SelectRandomContainerElement`, broadcasts its `stringId` via `World/SendWorldText`, and resets `_current` to 0.

**Cross-Unit Boundaries**

*   **`World`**:
    *   *Calls Out*: Constructor reads config via `World/getConfig#4`. `Update` sends messages via `World/SendWorldText`.
    *   *Called By*: `World/SetInitialWorldSettings` calls `Load`. `World/Update` calls `Update`.
*   **`ChatHandler.ServerCommands`**:
    *   *Called By*: `HandleReloadAutoBroadcastCommand` calls `Load`.
*   **`Database`**:
    *   *Calls Out*: `Load` executes queries via `Database/Query`.
*   **`Log`**:
    *   *Calls Out*: `Load` reports status via `Log.Main/Out`.
*   **`ProgressBar`**:
    *   *Calls Out*: `Load` displays progress via `ProgressBar/BarGoLink` and `ProgressBar/step`.

**Data Model**

*   **`autobroadcast`**: Stores message identifiers.
    *   `string_id` (int(11), nullable): Identifier for a localized string. The manager assumes validity; no validation is performed.

**Notable Implementation Details**

*   **Singleton**: Instantiated via `INSTANTIATE_SINGLETON_1` and accessed via `sAutoBroadCastMgr`.
*   **Timer Logic**: `Update` accumulates `diff` rather than tracking absolute time. Large `diff` values (e.g., server lag) cause immediate broadcast upon crossing the threshold, potentially skipping intervals without multiple broadcasts.
*   **Config Reload**: The interval `_constInterval` is read only in the constructor. Changing `CONFIG_UINT32_AUTOBROADCAST_INTERVAL` requires a server restart to take effect; `Load` does not re-read it.
*   **Empty Guard**: `Update` checks `entries.empty()` before calling `SelectRandomContainerElement` to prevent undefined behavior.

## Member Reference

**AutoBroadCastMgr** (ctor): Initializes `_constInterval` from `World/getConfig#4` and resets `_current` to 0.

**~AutoBroadCastMgr** (dtor): Clears `entries`.

**Load** (method): Queries `autobroadcast` for `string_id` via `Database/Query`, populates `entries` using `Field/GetInt32`, `QueryResult/Fetch`, `QueryResult/NextRow`, and `QueryResult/GetRowCount`. Displays progress via `ProgressBar/BarGoLink` and `ProgressBar/step`. Logs results via `Log.Main/Out`. Called by `World/SetInitialWorldSettings` and `ChatHandler.ServerCommands/HandleReloadAutoBroadcastCommand`.

**Update** (method): Accumulates `diff` into `_current`. If `_current >= _constInterval`, selects a random entry from `entries` and broadcasts via `World/SendWorldText`, then resets `_current`. Called by `World/Update`.

---

<!-- machine-true, projected from graph.json -->

## Map — AutoBroadCastMgr

*Source:* AutoBroadCastMgr.cpp, AutoBroadCastMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AutoBroadCastMgr | ctor | World/getConfig#4 | — | — |
| ~AutoBroadCastMgr | dtor | — | — | — |
| Load | method | Database/Query, Field/GetInt32, Log.Main/Out, ProgressBar/BarGoLink, ProgressBar/BarGoLink#3, ProgressBar/step, QueryResult/Fetch, QueryResult/GetRowCount, QueryResult/NextRow | ChatHandler.ServerCommands/HandleReloadAutoBroadcastCommand, World/SetInitialWorldSettings | autobroadcast |
| Update | method | World/SendWorldText | World/Update | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `autobroadcast`: string_id int(11)?

*`?` = nullable, `PK` = primary key column.*

