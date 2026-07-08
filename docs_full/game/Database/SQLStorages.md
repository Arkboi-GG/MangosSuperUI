<!-- provenance: verbose, failed-members -->
# SQLStorages

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# SQLStorages

## Purpose & Responsibilities

`SQLStorages` defines global `SQLStorage` instances that cache static configuration data from the database into memory at server startup. The unit contains no executable logic; its behavior is entirely driven by the construction of these global variables. It declares format strings that dictate how database rows are parsed and binds them to specific tables and key columns. By exposing these instances via `extern` declarations in `SQLStorages.h`, the unit allows other parts of the codebase to query static game data efficiently without performing runtime database queries.

## Member-by-Member Behavior

This unit consists solely of global variable definitions. Each variable is an instance of `SQLStorage` (from `Database/SQLStorage.h`). The constructor arguments determine the behavior:
1.  **Format String(s):** Character literals defining column types (`i`=int, `f`=float, `b`=bool, `s`=string).
2.  **Key Column:** The database column used for lookups.
3.  **Table Name:** The target database table.

Upon program startup, these constructors execute, connecting to the database (via `DatabaseEnv`) and populating internal caches.

*   **`sCreatureDataAddonStorage`**: Loads `creature_addon` keyed by `guid`. Format: `iiiibbis`.
*   **`sCreatureDisplayInfoAddonStorage`**: Loads `creature_display_info_addon` keyed by `display_id`. Format: `iffffbi`.
*   **`sGameObjectDisplayInfoAddonStorage`**: Loads `gameobject_display_info_addon` keyed by `display_id`. Format: `iffffff`.
*   **`sPageTextStore`**: Loads `page_text` keyed by `entry`. Format: `isi`.
*   **`sMapStorage`**: Loads `map_template` keyed by `entry`. Uses dual formats: `MapEntrysrcfmt` (`iiiiiiiffss`) and `MapEntrydstfmt` (`iiiiiiiffsi`).
*   **`sConditionStorage`**: Loads `conditions` keyed by `condition_entry`. Uses dual formats: `ConditionsSrcFmt` and `ConditionsDstFmt` (both `iiiiiii`).
*   **`sAreaStorage`**: Loads `area_template` keyed by `entry`. Format: `iiiiiisii`.
*   **`sMailTemplateStorage`**: Loads `mail_text_template` keyed by `entry`. Format: `issssssss`.
*   **`sCreatureSpellDataStorage`**: Loads `pet_spell_data` keyed by `entry`. Format: `iiiii`.

## Cross-Unit Boundaries

*   **Calls Out:**
    *   **`Database/SQLStorage.h` / `Database/SQLStorageImpl.h`**: Provides the `SQLStorage` class definition and implementation. All database interaction and parsing logic resides here.
    *   **`Database/DatabaseEnv.h`**: Included to access the global database environment singleton required by `SQLStorage` constructors.
    *   **`Common.h`**: Included for fundamental type definitions and macros.

*   **Called By:**
    *   Other units include `SQLStorages.h` to access the global `SQLStorage` instances directly. There are no function calls into this unit; it exposes data objects.

## Data Model

The unit interacts with nine database tables. No SCHEMA section was provided; column details are inferred from format strings and table names in the source.

| Table Name | Key Column | Format String(s) | Inferred Structure |
| :--- | :--- | :--- | :--- |
| `creature_addon` | `guid` | `iiiibbis` | 4 ints, 1 bool, 1 int, 1 string |
| `creature_display_info_addon` | `display_id` | `iffffbi` | 1 int, 4 floats, 1 bool, 1 int |
| `gameobject_display_info_addon` | `display_id` | `iffffff` | 1 int, 6 floats |
| `page_text` | `entry` | `isi` | 1 int, 1 string, 1 int |
| `map_template` | `entry` | `iiiiiiiffss` / `iiiiiiiffsi` | 6 ints, 2 floats, 2 strings (src) OR 6 ints, 2 floats, 1 string, 1 int (dst) |
| `conditions` | `condition_entry` | `iiiiiii` / `iiiiiii` | 7 ints (identical src/dst formats) |
| `area_template` | `entry` | `iiiiiisii` | 6 ints, 1 string, 2 ints |
| `mail_text_template` | `entry` | `issssssss` | 1 int, 8 strings |
| `pet_spell_data` | `entry` | `iiiii` | 5 ints |

## Notable Implementation Details

1.  **Global Initialization Order:** Correct operation depends on `DatabaseEnv` being initialized before these global `SQLStorage` objects construct. If the database environment is not ready, loading may fail or crash.
2.  **Dual Format Strings:** `sMapStorage` and `sConditionStorage` accept two format strings. This design likely supports backward compatibility with older schemas or distinguishes between source and destination data structures. The `SQLStorage` implementation determines which format applies.
3.  **No Local Error Handling:** The unit performs no error checking. Failures due to missing tables or schema mismatches are handled internally by `SQLStorage` constructors.

## Member Reference

**sCreatureDataAddonStorage** Global `SQLStorage` instance for `creature_addon` table, keyed by `guid`.
**sCreatureDisplayInfoAddonStorage** Global `SQLStorage` instance for `creature_display_info_addon` table, keyed by `display_id`.
**sGameObjectDisplayInfoAddonStorage** Global `SQLStorage` instance for `gameobject_display_info_addon` table, keyed by `display_id`.
**sPageTextStore** Global `SQLStorage` instance for `page_text` table, keyed by `entry`.
**sMapStorage** Global `SQLStorage` instance for `map_template` table, keyed by `entry`, supporting dual format strings.
**sConditionStorage** Global `SQLStorage` instance for `conditions` table, keyed by `condition_entry`, supporting dual format strings.
**sAreaStorage** Global `SQLStorage` instance for `area_template` table, keyed by `entry`.
**sMailTemplateStorage** Global `SQLStorage` instance for `mail_text_template` table, keyed by `entry`.
**sCreatureSpellDataStorage** Global `SQLStorage` instance for `pet_spell_data` table, keyed by `entry`.

---

<!-- machine-true, projected from graph.json -->

## Map — SQLStorages

*Source:* SQLStorages.cpp, SQLStorages.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|

---

<!-- verify: failed-members | invented: sAreaStorage, sConditionStorage, sCreatureDataAddonStorage, sCreatureDisplayInfoAddonStorage, sCreatureSpellDataStorage, sGameObjectDisplayInfoAddonStorage, sMailTemplateStorage, sMapStorage, sPageTextStore -->
