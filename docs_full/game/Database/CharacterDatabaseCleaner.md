<!-- provenance: verbose -->
# CharacterDatabaseCleaner

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`CharacterDatabaseCleaner` is a namespace-scoped maintenance module that purges invalid references from the character database during server startup. It targets the `character_skills` and `character_spell` tables, removing rows where the referenced skill or spell ID no longer exists in the server’s loaded DBC stores.

The module operates conditionally based on a bitmask (`cleaning_flags`) stored in the `saved_variables` table. It is invoked by `World.SetInitialWorldSettings` if the `CONFIG_BOOL_CLEAN_CHARACTER_DB` configuration option is enabled. After processing, it resets the flags to zero to prevent redundant scans on subsequent restarts.

## Member-by-Member Behavior

### Orchestration

**`CleanDatabase`** is the primary entry point. It first checks `World.getConfig` for `CONFIG_BOOL_CLEAN_CHARACTER_DB`; if disabled, it returns immediately. Otherwise, it logs the start of the process and queries `saved_variables` for `cleaning_flags`. Based on the bitmask, it conditionally invokes `CleanCharacterSkills` (if `CLEANING_FLAG_SKILLS` is set) and `CleanCharacterSpell` (if `CLEANING_FLAG_SPELLS` is set). Finally, it executes an `UPDATE` statement to reset `cleaning_flags` to 0 in `saved_variables`, regardless of whether any deletions occurred.

### Validation Helpers

**`SkillCheck`** validates a single skill ID by calling `sSkillLineStore.LookupEntry`. It returns `true` if the skill exists in the DBC store, `false` otherwise.

**`SpellCheck`** validates a single spell ID by calling `SpellMgr.GetSpellEntry`. It returns `true` if the spell entry exists, `false` otherwise.

### Cleanup Drivers

**`CleanCharacterSkills`** initiates the skill cleanup by calling `CheckUnique` with the table `"character_skills"`, column `"skill"`, and the `SkillCheck` validator.

**`CleanCharacterSpell`** initiates the spell cleanup by calling `CheckUnique` with the table `"character_spell"`, column `"spell"`, and the `SpellCheck` validator.

### Generic Deletion Logic

**`CheckUnique`** is a reusable utility that removes rows from a specified table where a column’s value fails a provided validation function. It executes `SELECT DISTINCT <column> FROM <table>` to retrieve unique IDs. It iterates through the results, stepping a `ProgressBar` for each row. If the validation function returns `false` for an ID, the ID is appended to an `std::ostringstream`. Once iteration completes, if any invalid IDs were found, it constructs and executes a single `DELETE FROM <table> WHERE <column> IN (...)` statement. If the table is empty, it logs a message and returns early.

## Cross-Unit Boundaries

*   **`World`**: `CleanDatabase` calls `World.getConfig` to check the enable flag. `CleanDatabase` is called by `World.SetInitialWorldSettings`.
*   **`Database`**: `CleanDatabase` and `CheckUnique` use `Database.PQuery` for reads and `Database.Execute` for writes.
*   **`Log.Main`**: `CleanDatabase` and `CheckUnique` call `Log.Main.Out` for status logging.
*   **`ProgressBar`**: `CheckUnique` uses `ProgressBar.BarGoLink` and `ProgressBar.step` to visualize progress during ID iteration.
*   **`SpellMgr`**: `SpellCheck` calls `SpellMgr.GetSpellEntry` to verify spell validity.
*   **`DBCStores`**: `SkillCheck` calls `sSkillLineStore.LookupEntry` (implicit via global singleton) to verify skill validity.

## Data Model

The unit interacts with three tables:

*   **`saved_variables`**: Used to read and reset the `cleaning_flags` bitmask. The schema defines `cleaning_flags` as `int(11) unsigned`. Other columns like `honor_last_maintenance_day` are ignored.
*   **`character_skills`**: Scanned for invalid `skill` IDs. Rows with invalid IDs are deleted.
*   **`character_spell`**: Scanned for invalid `spell` IDs. Rows with invalid IDs are deleted.

## Notable Implementation Details

*   **Batch Deletion**: `CheckUnique` accumulates all invalid IDs into a single `IN (...)` clause. This minimizes round-trips but can generate large SQL strings if many invalid IDs exist.
*   **Distinct Optimization**: Using `SELECT DISTINCT` reduces iteration overhead when many characters share the same invalid ID, but requires holding all distinct IDs in memory during the scan.
*   **Flag Reset Timing**: `CleanDatabase` resets `cleaning_flags` only after all cleanup functions complete. If the server crashes during cleanup, the flags remain set, triggering a retry on the next startup.
*   **Static Inputs**: `CheckUnique` accepts table and column names as strings, which poses a theoretical SQL injection risk. However, callers pass only hardcoded literals, making it safe in practice.

## Member Reference

**`CleanDatabase`**: Entry point that checks config, reads `cleaning_flags` from `saved_variables`, invokes specific cleaners based on flags, and resets flags to 0. Called by `World.SetInitialWorldSettings`.

**`CheckUnique`**: Generic helper that queries distinct values from a table, validates them via a function pointer, and deletes invalid rows in a single batch `DELETE` statement. Uses `ProgressBar` for feedback.

**`SkillCheck`**: Validator returning `true` if the skill ID exists in `sSkillLineStore`.

**`CleanCharacterSkills`**: Calls `CheckUnique` for the `character_skills` table using `SkillCheck`.

**`SpellCheck`**: Validator returning `true` if the spell ID exists in `SpellMgr`.

**`CleanCharacterSpell`**: Calls `CheckUnique` for the `character_spell` table using `SpellCheck`.

---

<!-- machine-true, projected from graph.json -->

## Map — CharacterDatabaseCleaner

*Source:* CharacterDatabaseCleaner.cpp, CharacterDatabaseCleaner.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| CleanDatabase | function | Database/Execute#2, Database/PQuery, Field/GetUInt32, Log.Main/Out, QueryResult/operator[], World/getConfig | World/SetInitialWorldSettings | saved_variables |
| CheckUnique | function | Database/Execute#2, Database/PQuery, Field/GetUInt32, Log.Main/Out, ProgressBar/BarGoLink#3, ProgressBar/step, QueryResult/Fetch, QueryResult/GetRowCount, QueryResult/NextRow | — | — |
| SkillCheck | function | — | — | — |
| CleanCharacterSkills | function | — | — | — |
| SpellCheck | function | SpellMgr/GetSpellEntry, SpellMgr/Instance | — | — |
| CleanCharacterSpell | function | — | — | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `saved_variables`: key tinyint(1) unsigned PK, cleaning_flags int(11) unsigned, honor_last_maintenance_day int(11) unsigned, honor_next_maintenance_day int(11) unsigned, honor_maintenance_marker tinyint(1) unsigned

*`?` = nullable, `PK` = primary key column.*

