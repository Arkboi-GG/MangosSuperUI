<!-- provenance: verbose -->
# AuraRemovalMgr

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AuraRemovalMgr

**Purpose & Responsibilities**

`AuraRemovalMgr` manages the automatic removal of specific buffs (auras) from players upon entering designated maps, typically to enforce instance-specific balance rules. Implemented as a singleton (`sAuraRemovalMgr`), it caches configuration from the `instance_buff_removal` database table and reacts to map-entry events via `PlayerEnterMap`. It supports team-based exclusions via bitmask flags, allowing distinct behaviors for Alliance and Horde players.

## Member-by-Member Behavior

### Initialization

**`LoadFromDB`**
Populates the internal `m_data` map from the `instance_buff_removal` table. It clears existing data, executes a `SELECT` query, and iterates through results. For each row where `enabled` is true, it constructs an `AuraRemovalEntry` (containing `auraId` and `flags`) and appends it to the vector for the corresponding `mapId`. Progress is reported via `ProgressBar` and `Log.Main`. If the table is empty, it logs a specific message.

### Runtime Logic

**`PlayerEnterMap`**
Triggered when a player enters a map. It validates the player pointer and looks up the `mapId` in `m_data`. If entries exist, it iterates through them, checking team exclusion flags:
- Skips removal if the player is Horde and `AURA_REM_FLAG_EXCLUDE_HORDE` is set.
- Skips removal if the player is Alliance and `AURA_REM_FLAG_EXCLUDE_ALLIANCE` is set.
If not excluded, it checks for the aura's presence using `Unit.Main/HasAura#2` and removes it via `Unit.Main/RemoveAurasDueToSpellByCancel` if present.

**`AuraRemovalManager`**
Default constructor for the singleton. Contains no custom logic.

## Cross-Unit Boundaries

*   **Called By:**
    *   `ChatHandler.ServerCommands/HandleReloadInstanceBuffRemoval` and `World/SetInitialWorldSettings` invoke `LoadFromDB` to load or reload configuration.
    *   `Map.Main/Add#3` invokes `PlayerEnterMap` when a player joins a map context.
*   **Calls Out:**
    *   `PlayerEnterMap` calls `Player.Main/GetTeam` to determine faction for flag evaluation.
    *   `PlayerEnterMap` calls `Unit.Main/HasAura#2` to verify aura presence and `Unit.Main/RemoveAurasDueToSpellByCancel` to execute removal.
    *   `LoadFromDB` uses `Database/Query`, `QueryResult`, `Field`, `Log.Main`, and `ProgressBar` for data retrieval and reporting.

## Data Model

The unit interacts with one table:

**`instance_buff_removal`**
*   `map_id` (int(10) unsigned, PK): Map ID where the rule applies.
*   `spell_id` (smallint(5) unsigned, PK): Spell ID of the aura to remove.
*   `enabled` (tinyint(1)): Boolean; only `true` rows are loaded.
*   `flags` (int(10)): Bitmask for exclusions (`1` = Exclude Horde, `2` = Exclude Alliance).
*   `comment` (varchar(256)): Ignored by C++ logic.

## Notable Implementation Details

*   **Exclusion Flags:** Default `flags=0` removes auras for both teams. Setting bits excludes specific factions.
*   **Removal Method:** Uses `RemoveAurasDueToSpellByCancel`, which may trigger specific cancellation visuals or cooldown resets compared to simple deletion.
*   **Data Structure:** `m_data` is a `std::map<uint32, std::vector<AuraRemovalEntry>>`, supporting multiple auras per map.
*   **Empty Table Logging:** Explicitly logs "Table instance_buff_removal is empty." if no rows are returned, stepping a progress bar once for consistency.

## Member Reference

**`LoadFromDB`**
Clears `m_data`, queries `instance_buff_removal`, and populates the map with `AuraRemovalEntry` structs for enabled rows. Logs progress and count using `ProgressBar` and `Log.Main`.

**`AuraRemovalManager`**
Default constructor for the singleton instance. No initialization logic.

**`PlayerEnterMap`**
Checks `m_data` for the given `mapId`. For each entry, skips removal if team flags exclude the player's faction. Otherwise, verifies aura presence via `Unit.Main/HasAura#2` and removes it via `Unit.Main/RemoveAurasDueToSpellByCancel`.

---

<!-- machine-true, projected from graph.json -->

## Map — AuraRemovalMgr

*Source:* AuraRemovalMgr.cpp, AuraRemovalMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| LoadFromDB | method | Database/Query, Field/GetUInt32, Field/GetUInt8, Log.Main/Out, ProgressBar/BarGoLink, ProgressBar/BarGoLink#3, ProgressBar/step, QueryResult/Fetch, QueryResult/GetRowCount, QueryResult/NextRow | ChatHandler.ServerCommands/HandleReloadInstanceBuffRemoval, World/SetInitialWorldSettings | instance_buff_removal |
| AuraRemovalManager | ctor | — | — | — |
| PlayerEnterMap | method | Player.Main/GetTeam, Unit.Main/HasAura#2, Unit.Main/RemoveAurasDueToSpellByCancel | Map.Main/Add#3 | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `instance_buff_removal`: map_id int(10) unsigned PK, spell_id smallint(5) unsigned PK, enabled tinyint(1), flags int(10), comment varchar(256)

*`?` = nullable, `PK` = primary key column.*

