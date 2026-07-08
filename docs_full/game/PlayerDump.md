# PlayerDump

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# PlayerDump

**PlayerDump** provides the infrastructure for exporting a player’s complete database state to a SQL dump file and importing that dump to recreate the character on the same or different server. It handles the complex dependency graph of character data—items, pets, mail, and associated metadata—by tracking GUIDs and IDs during export and remapping them to new, unique identifiers during import to prevent primary key collisions.

The unit is split into two distinct operational modes:
1.  **Export (`PlayerDumpWriter`)**: Reads rows from the `characters` database for a specific character GUID, collects dependent GUIDs (items, pets, mail), and generates a series of `INSERT` statements.
2.  **Import (`PlayerDumpReader`)**: Parses a SQL dump file line-by-line, validates table structures, remaps old GUIDs/IDs to new ones using internal maps, and executes the modified `INSERT` statements within a transaction.

This unit does not interact with live game objects or network packets; it operates entirely on the persistent storage layer.

## Member-by-Member Behavior

### Export Logic (`PlayerDumpWriter`)

The export process is driven by `GetDump`, which iterates through a static array of known tables (`dumpTables`). For each table, it calls `DumpTableContent` to query the database and generate SQL.

*   **GetDump**: Orchestrates the entire export. It initializes a string buffer with warning comments and then loops through `dumpTables`. For each entry, it calls `DumpTableContent`. It relies on the order of `dumpTables` because some tables depend on GUIDs collected from previous tables (e.g., `character_inventory` collects item GUIDs needed by `item_instance`).
*   **DumpTableContent**: The core export engine. It determines how to query the table based on `DumpTableType`:
    *   For simple character tables (`DTT_CHAR_TABLE`), it queries by the main character `guid`.
    *   For dependent tables (items, pets, mail), it queries by a set of collected GUIDs (e.g., `items`, `pets`, `mails`).
    *   It uses `GenerateWhereStr` to build the `WHERE` clause.
    *   It executes the query via `Database/PQuery`.
    *   For each row returned, it calls `CreateDumpString` to format the row into an `INSERT` statement.
    *   Crucially, it also **collects** dependent GUIDs during this pass. For example, when processing `character_inventory`, it extracts item GUIDs and adds them to the `items` set so that `item_instance` can be queried later.
*   **GenerateWhereStr**: Two overloads exist. One builds a simple equality check (`field = 'guid'`). The other builds an `IN (...)` clause for multiple GUIDs, splitting the query if the string length approaches `MAX_QUERY_LEN` to avoid database limits.
*   **CreateDumpString**: Formats a single `QueryResult` row into a valid SQL `INSERT` statement. It escapes string values using `Database/escape_string` and handles `NULL` fields explicitly.
*   **WriteDump**: A convenience wrapper that calls `GetDump` and writes the resulting string to a file on disk. It returns `DUMP_FILE_OPEN_ERROR` if the file cannot be opened.

### Import Logic (`PlayerDumpReader`)

The import process is significantly more complex due to the need to remap identifiers. `LoadDump` reads the file line-by-line, parses the SQL, modifies it, and executes it.

*   **LoadDump**: The main import routine.
    1.  **Validation**: Checks if the target account has too many characters (`AccountMgr/GetCharactersCount`).
    2.  **GUID/Name Preparation**: Determines the new character GUID (either user-provided or auto-generated) and normalizes/checks the character name (`ObjectMgr/CheckPlayerName`, `ObjectMgr/normalizePlayerName`).
    3.  **Transaction**: Begins a database transaction (`Database/BeginTransaction`).
    4.  **Line Processing**: Loops through the file using `fgets`. Skips empty lines and comment headers.
    5.  **Table Identification**: Uses `gettablename` to identify which table the current `INSERT` statement targets.
    6.  **Remapping**: Based on the `DumpTableType`, it modifies the SQL string in-place:
        *   Replaces the old character GUID with the new one.
        *   Replaces old item/pet/mail GUIDs with new ones generated via `registerNewGuid`.
        *   Updates the character name and account ID.
        *   Handles special cases like pet ID remapping (since `character_pet.id` is not a global GUID but a local sequence).
    7.  **Execution**: Executes the modified SQL string via `Database/Execute`. If any step fails, it rolls back the transaction (`Database/RollbackTransaction`) and returns an error.
    8.  **Finalization**: Commits the transaction (`Database/CommitTransaction`) and updates the global GUID generators (`sObjectMgr`) to reflect the newly consumed IDs.

### String Parsing Utilities

These static functions handle the fragile task of parsing raw SQL strings without a full SQL parser. They rely on fixed positions and delimiters.

*   **findtoknth / gettoknth**: Finds the $n$-th space-delimited token in a string. Used primarily for parsing simple value lists or headers.
*   **findnth / getnth**: Finds the $n$-th quoted value (`'...'`) within an `INSERT ... VALUES (...)` statement. It carefully handles escaped quotes (`\'`). This is the primary mechanism for extracting and replacing specific column values in the dump.
*   **changenth / changetoknth**: Replaces the $n$-th quoted value or token in the string with a new value. Supports an `insert` mode (prepend) and a `nonzero` check (skip replacement if the original value was "0").
*   **gettablename**: Extracts the table name from an `INSERT INTO \`table_name\` ...` statement.
*   **StoreGUID**: Helper functions used during export to extract GUIDs from query results and add them to sets (`items`, `pets`, etc.). One overload reads a direct integer field; the other parses a space-separated string field.

### GUID Management

*   **registerNewGuid**: Manages the mapping from old GUIDs to new GUIDs. If an old GUID has already been seen, it returns the previously assigned new GUID. Otherwise, it generates a new one based on a high-water mark (`hiGuid`) and the size of the map.
*   **changeGuid / changetokGuid**: Combines extraction (`getnth`/`gettoknth`), registration (`registerNewGuid`), and replacement (`changenth`/`changetoknth`) into a single step for GUID columns.

## Cross-Unit Boundaries

*   **Database Layer**:
    *   `CreateDumpString` calls `Database/escape_string` to sanitize strings for SQL injection prevention.
    *   `DumpTableContent` and `LoadDump` call `Database/PQuery` to execute queries and retrieve results.
    *   `LoadDump` calls `Database/Execute` to run the modified `INSERT` statements.
    *   `LoadDump` manages transactions via `Database/BeginTransaction`, `Database/CommitTransaction`, and `Database/RollbackTransaction`.
*   **Object Manager (`ObjectMgr`)**:
    *   `LoadDump` calls `ObjectMgr/CheckPlayerName` and `ObjectMgr/normalizePlayerName` to validate the target character name.
    *   `LoadDump` calls `ObjectMgr/GeneratePetNumber` to generate new pet IDs.
    *   `LoadDump` calls `ObjectMgr/AddItemText` to cache item text data.
    *   `LoadDump` accesses `sObjectMgr.m_CharGuids`, `m_ItemGuids`, `m_MailIds`, and `m_ItemTextIds` to determine the next available GUIDs and to update the global counters after import.
*   **Account Manager (`AccountMgr`)**:
    *   `LoadDump` calls `AccountMgr/GetCharactersCount` to enforce the character limit per account.
*   **Chat Handler**:
    *   `WriteDump` is called by `ChatHandler.CharacterCommands/HandlePDumpWriteCommand`.
    *   `LoadDump` is called by `ChatHandler.CharacterCommands/HandlePDumpLoadCommand`.

## Data Model

The unit interacts with the following tables in the `characters` database. The schema is derived from the provided SCHEMA section and the `dumpTables` array in the source.

*   **`characters`**: The primary table. Contains the character's basic info (GUID, name, account, race, class, level, position, etc.).
*   **`character_action`**: Action bar entries.
*   **`character_aura`**: Active auras/buffs.
*   **`character_homebind`**: Home bind location.
*   **`character_honor_cp`**: Honor points data.
*   **`character_inventory`**: Inventory slots, linking character GUID to item GUIDs.
*   **`character_queststatus`**: Quest progress.
*   **`character_pet`**: Pet information. Links pet ID to owner GUID.
*   **`character_reputation`**: Faction reputation.
*   **`character_skills`**: Skill levels.
*   **`character_spell`**: Known spells.
*   **`character_spell_cooldown`**: Spell cooldowns.
*   **`mail`**: Mail messages. Links receiver GUID to mail ID.
*   **`mail_items`**: Items attached to mail. Links mail ID to item GUID.
*   **`pet_aura`**: Pet auras. Linked to pet ID.
*   **`pet_spell`**: Pet spells. Linked to pet ID.
*   **`pet_spell_cooldown`**: Pet spell cooldowns. Linked to pet ID.
*   **`character_gifts`**: Gifted items. Links character GUID to item GUID.
*   **`item_instance`**: Detailed item data. Links item GUID to owner GUID and item text ID.
*   **`item_loot`**: Loot data. Links item GUID to owner GUID.
*   **`item_text`**: Item description text. Linked by ID.

## Notable Implementation Details

1.  **Fragile SQL Parsing**: The import logic relies on `findnth` and `changenth` to parse and modify raw SQL strings. These functions assume a strict format: `INSERT INTO \`table\` VALUES ('val1', 'val2', ...)`. They handle escaped quotes (`\'`) but may fail if the SQL format deviates (e.g., different quoting styles, comments within values, or non-standard whitespace). This makes the dump format tightly coupled to the export format.
2.  **Pet ID Remapping**: Unlike global GUIDs, pet IDs (`character_pet.id`) are local sequences. The import logic maintains a `petids` map to track old-to-new pet ID mappings. It generates new pet IDs using `ObjectMgr/GeneratePetNumber` and ensures that dependent tables (`pet_aura`, `pet_spell`, etc.) use the new IDs. This is a critical complexity that distinguishes pets from items/mail.
3.  **GUID Collision Avoidance**: During import, `registerNewGuid` ensures that every old GUID is mapped to a unique new GUID. The new GUIDs are generated sequentially starting from the current maximum used GUID for that type (item, mail, etc.). After import, the global GUID generators are updated to reflect these new maxima, preventing future collisions.
4.  **Transaction Safety**: The entire import process is wrapped in a database transaction. If any line fails to parse or execute, the transaction is rolled back, ensuring atomicity. However, the rollback also closes the file handle, which is a minor resource management detail.
5.  **Name Handling**: If a new name is provided, it is normalized and checked for existence. If it exists, the name is cleared, and the original name from the dump is used. If the original name exists, the character is flagged for rename on login (`character_flags` bit 14, value 16384).
6.  **Item Text Caching**: During import, item text entries are added to the `ObjectMgr` cache via `AddItemText`. This ensures that the text is available immediately after import, although it is also persisted in the `item_text` table.
7.  **Hardcoded Column Indices**: The `changenth` calls in `LoadDump` use hardcoded indices (e.g., `changenth(line, 1, newguid)` for GUID). These indices correspond to the column order in the `INSERT` statements generated by `CreateDumpString`. Any change to the table schema or the order of columns in the dump would break the import logic.

## Member Reference

**isValid**
Static helper method in `DumpTable` struct. Returns `true` if the table name is not null, used to terminate iteration over the `dumpTables` array.

**findtoknth**
Static function. Locates the start and end indices of the $n$-th space-delimited token in a string. Returns `false` if the token is not found.

**PlayerDump**
Base class constructor. Protected, does nothing. Serves as a common base for `PlayerDumpWriter` and `PlayerDumpReader`.

**gettoknth**
Static function. Returns the $n$-th space-delimited token as a string. Uses `findtoknth` internally.

**findnth**
Static function. Locates the start and end indices of the $n$-th quoted value (`'...'`) in an SQL `INSERT` statement. Handles escaped quotes. Returns `false` if not found.

**gettablename**
Static function. Extracts the table name from an `INSERT INTO \`table\` ...` statement.

**changenth**
Static function. Replaces the $n$-th quoted value in a string with a new value. Supports insertion and skipping zero values.

**getnth**
Static function. Returns the $n$-th quoted value as a string. Uses `findnth` internally.

**changetoknth**
Static function. Replaces the $n$-th space-delimited token in a string with a new value. Supports insertion and skipping zero values.

**registerNewGuid**
Static function. Maps an old GUID to a new unique GUID. If the old GUID is already mapped, returns the existing new GUID. Otherwise, generates a new one based on a high-water mark and stores the mapping.

**changeGuid**
Static function. Extracts the $n$-th quoted value as a GUID, registers a new GUID for it, and replaces the old value in the string.

**changetokGuid**
Static function. Extracts the $n$-th space-delimited token as a GUID, registers a new GUID for it, and replaces the old value in the string.

**CreateDumpString**
Static function. Converts a `QueryResult` row into a formatted SQL `INSERT` statement. Escapes strings and handles NULLs.

**GenerateWhereStr#2**
Method of `PlayerDumpWriter`. Generates a SQL `WHERE` clause with an `IN (...)` list for multiple GUIDs. Splits the list if it exceeds `MAX_QUERY_LEN`.

**GenerateWhereStr**
Method of `PlayerDumpWriter`. Generates a simple SQL `WHERE` clause with an equality check for a single GUID.

**StoreGUID**
Static function (overload 1). Extracts a GUID from a specific field index in a `QueryResult` and adds it to a set if non-zero.

**StoreGUID#2**
Static function (overload 2). Extracts a GUID from a space-separated string field in a `QueryResult` and adds it to a set if non-zero.

**DumpTableContent**
Method of `PlayerDumpWriter`. Queries a specific table for rows related to the character (directly or via dependent GUIDs), generates `INSERT` statements for each row, and collects dependent GUIDs for subsequent tables.

**GetDump**
Method of `PlayerDumpWriter`. Orchestrates the export of all character-related tables into a single SQL dump string.

**WriteDump**
Method of `PlayerDumpWriter`. Writes the SQL dump string to a file on disk.

**LoadDump**
Method of `PlayerDumpReader`. Imports a character from a SQL dump file. Validates the account, remaps GUIDs/IDs, executes the modified SQL statements within a transaction, and updates global GUID counters.

---

<!-- machine-true, projected from graph.json -->

## Map — PlayerDump

*Source:* PlayerDump.cpp, PlayerDump.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| isValid | method | — | — | — |
| findtoknth | function | — | — | — |
| PlayerDump | ctor | — | — | — |
| gettoknth | function | — | — | — |
| findnth | function | — | — | — |
| gettablename | function | — | — | — |
| changenth | function | — | — | — |
| getnth | function | — | — | — |
| changetoknth | function | — | — | — |
| registerNewGuid | function | — | — | — |
| changeGuid | function | — | — | — |
| changetokGuid | function | — | — | — |
| CreateDumpString | function | Database/escape_string, Field/GetCppString, Field/IsNULL, QueryResult/Fetch, QueryResult/GetFieldCount | — | — |
| GenerateWhereStr#2 | method | — | — | — |
| GenerateWhereStr | method | — | — | — |
| StoreGUID | function | Field/GetUInt32, QueryResult/Fetch | — | — |
| StoreGUID#2 | function | Field/GetCppString, QueryResult/Fetch | — | — |
| DumpTableContent | method | Database/PQuery, QueryResult/NextRow | — | — |
| GetDump | method | — | — | — |
| WriteDump | method | — | ChatHandler.CharacterCommands/HandlePDumpWriteCommand | — |
| LoadDump | method | AccountMgr/GetCharactersCount, Database/BeginTransaction, Database/CommitTransaction, Database/escape_string, Database/Execute#2, Database/PQuery, Database/RollbackTransaction, Errors/PrintStacktraceAndThrow, Log.Main/Out, ObjectMgr/AddItemText, ObjectMgr/CheckPlayerName, ObjectMgr/GeneratePetNumber, ObjectMgr/normalizePlayerName | ChatHandler.CharacterCommands/HandlePDumpLoadCommand | characters |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `characters`: guid int(11) unsigned PK, account int(11) unsigned, name varchar(12), race tinyint(3) unsigned, class tinyint(3) unsigned, gender tinyint(3) unsigned, skin tinyint(3) unsigned, face tinyint(3) unsigned, hair_style tinyint(3) unsigned, hair_color tinyint(3) unsigned, facial_hair tinyint(3) unsigned, level tinyint(3) unsigned, xp int(10) unsigned, money int(10) unsigned, character_flags int(10) unsigned, zone int(11) unsigned, map int(11) unsigned, instance int(11) unsigned, position_x float, position_y float, position_z float, orientation float, transport_guid bigint(20) unsigned, transport_x float, transport_y float, transport_z float, transport_o float, known_taxi_mask longtext?, current_taxi_path text?, online tinyint(3) unsigned, played_time_total int(11) unsigned, played_time_level int(11) unsigned, create_time bigint(20) unsigned, logout_time bigint(20) unsigned, rest_bonus float, reset_talents_multiplier int(11) unsigned, reset_talents_time bigint(20) unsigned, death_expire_time bigint(20) unsigned, stable_slots tinyint(1) unsigned, bank_bag_slots tinyint(1) unsigned, extra_flags int(11) unsigned, honor_rank_points float, honor_highest_rank int(11) unsigned, honor_standing int(11) unsigned, honor_last_week_hk int(11) unsigned, honor_last_week_cp float, honor_stored_hk int(11), honor_stored_dk int(11), watched_faction int(11), drunk smallint(5) unsigned, health int(10) unsigned, power1 int(10) unsigned, power2 int(10) unsigned, power3 int(10) unsigned, power4 int(10) unsigned, power5 int(10) unsigned, explored_zones longtext?, equipment_cache longtext?, ammo_id int(10) unsigned, action_bars tinyint(3) unsigned, deleted_account int(11) unsigned?, deleted_name varchar(12)?, deleted_time bigint(20)?, world_phase_mask int(11)?

*`?` = nullable, `PK` = primary key column.*

