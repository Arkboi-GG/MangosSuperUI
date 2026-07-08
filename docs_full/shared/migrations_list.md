<!-- provenance: failed-members -->
# migrations_list

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# `migrations_list`

## Purpose & Responsibilities

The `migrations_list` unit (`migrations_list.h`) serves as the central registry of database migration identifiers for the WowVMaNGOS server. It defines four static, null-terminated arrays of C-style strings, each corresponding to a specific database instance managed by the server: **Characters**, **World**, **Logon**, and **Logs**.

These arrays provide the ordered list of migration version IDs (formatted as timestamps, e.g., `"20210515110157"`) that the database migration framework iterates over to determine which schema changes need to be applied. The unit contains no executable logic, classes, or functions; it is purely a data definition header included by the migration subsystem to know the complete set of expected migrations for each database.

## Member-by-Member Behavior

This unit exposes four global constant arrays. Each array is a `const char *` pointer array terminated by a `NULL` sentinel value.

### `MIGRATIONS_CHARACTERS`
An array of string literals representing the migration IDs for the **characters** database. This database typically stores player-specific data such as character profiles, inventory, guild memberships, and auction house entries. The array contains 7 migration IDs, ranging from `20220813085336` to `20250712023524`.

### `MIGRATIONS_WORLD`
An array of string literals representing the migration IDs for the **world** database. This is the largest database in the system, containing game world data such as creature templates, item definitions, quest data, map scripts, and zone configurations. The array contains over 1,000 migration IDs, spanning from `20210515110157` to `20260402075929`. This indicates a long history of incremental schema updates to the core game data structures.

### `MIGRATIONS_LOGON`
An array of string literals representing the migration IDs for the **logon** (or auth) database. This database handles account management, including user credentials, security tokens, and account bans. The array contains 6 migration IDs, ranging from `20210830151515` to `20260109115717`.

### `MIGRATIONS_LOGS`
An array of string literals representing the migration IDs for the **logs** database. This database stores server activity logs, error reports, and audit trails. The array contains 6 migration IDs, ranging from `20210731110900` to `20221008210304`.

## Cross-Unit Boundaries

This unit has no executable members, so it does not actively call into other units. However, it is **called by** (included by) the database migration subsystem. Specifically, the migration manager likely iterates through these arrays to:
1.  Check the current migration version stored in the database.
2.  Compare it against the list in these arrays.
3.  Apply any pending migrations (SQL scripts associated with these IDs) in order until the database schema matches the latest ID in the respective array.

The naming convention (`MIGRATIONS_<DB_NAME>`) suggests a direct mapping to the database connection pools or migration handlers for each specific database type within the WowVMaNGOS architecture.

## Data Model

This unit does not directly interact with database tables via SQL queries. Instead, it defines the **metadata** (migration IDs) that governs the evolution of the database schemas. The actual table structures are defined in the SQL migration files corresponding to these IDs. Therefore, no specific table columns or schemas are referenced in this unit.

## Notable Implementation Details

*   **Null-Termination:** All four arrays are explicitly terminated with `NULL`. This allows the consuming code to iterate through the arrays using standard C-style loops (e.g., `while (array[i] != NULL)`) without needing to hardcode the array size.
*   **Static Const Storage:** The arrays are declared as `static const`, meaning they are local to the translation unit that includes this header. This prevents symbol collisions if multiple files include `migrations_list.h`.
*   **Timestamp-Based Versioning:** The migration IDs follow a strict `YYYYMMDDHHmmss` format. This ensures that migrations are naturally sorted chronologically, which is critical for applying schema changes in the correct order.
*   **Future-Dated Migrations:** The `MIGRATIONS_WORLD` array contains IDs extending into 2026 (e.g., `20260402075929`). This suggests either:
    1.  The system clock during development was set ahead.
    2.  These are placeholder IDs for future migrations.
    3.  The codebase is being developed with a forward-looking timeline.
    Maintainers should verify that these future-dated migrations correspond to actual SQL files and are not accidental typos.
*   **No Logic:** There is no validation, sorting, or duplicate checking performed in this unit. The correctness of the migration order relies entirely on the alphabetical/chronological sorting of the string literals in the source code.

## Member Reference

**MIGRATIONS_CHARACTERS**
A static const array of `char *` pointers listing the migration IDs for the characters database. Contains 7 entries, terminated by `NULL`.

**MIGRATIONS_WORLD**
A static const array of `char *` pointers listing the migration IDs for the world database. Contains over 1,000 entries, terminated by `NULL`. This is the most extensive migration list, reflecting the complexity and frequent updates to the game world data.

**MIGRATIONS_LOGON**
A static const array of `char *` pointers listing the migration IDs for the logon/auth database. Contains 6 entries, terminated by `NULL`.

**MIGRATIONS_LOGS**
A static const array of `char *` pointers listing the migration IDs for the logs database. Contains 6 entries, terminated by `NULL`.

---

<!-- machine-true, projected from graph.json -->

## Map — migrations_list

*Source:* migrations_list.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|

---

<!-- verify: failed-members | invented: MIGRATIONS_CHARACTERS, MIGRATIONS_LOGON, MIGRATIONS_LOGS, MIGRATIONS_WORLD -->
