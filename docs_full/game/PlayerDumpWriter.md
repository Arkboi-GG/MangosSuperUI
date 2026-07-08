<!-- provenance: verbose -->
# PlayerDumpWriter

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# PlayerDumpWriter

**PlayerDumpWriter** is a utility class that generates a complete SQL dump of a single player character’s data. It serializes the character’s state—including inventory, mail, pets, and associated metadata—into a series of `INSERT` statements suitable for backup or migration. The class resolves foreign-key dependencies by collecting related GUIDs (items, mail, pets) before generating the final SQL output.

This unit is one half of the `PlayerDump` hierarchy; `PlayerDumpReader` (defined in the same header but implemented elsewhere) handles the reverse operation. `PlayerDumpWriter` focuses exclusively on export.

## Purpose & Responsibilities

The primary responsibility of `PlayerDumpWriter` is to query the live database for all records associated with a specific character GUID and format them into valid SQL. Key responsibilities include:

1.  **Dependency Resolution:** Identifying related entities (e.g., items in inventory, mail items, pet spells) by collecting their GUIDs into internal `std::set<uint32>` containers (`items`, `mails`, `pets`, `texts`). This ensures that dependent tables are dumped with the correct `WHERE` clauses.
2.  **SQL Generation:** Constructing `INSERT INTO ... VALUES (...)` statements for each relevant table, ordered to maintain referential integrity where possible.
3.  **File Output:** Writing the generated SQL string to a specified file path, reporting success or specific error codes via the `DumpReturn` enum.

## Member-by-Member Behavior

### Construction

**`PlayerDumpWriter()`**
The default constructor initializes the `PlayerDumpWriter` instance. It leaves the internal `GUIDs` sets (`pets`, `mails`, `items`, `texts`) empty. No I/O or database queries occur during construction.

### Core Operations

While the MAP lists only the constructor, the header defines two public methods that drive the class’s functionality. These are invoked by `ChatHandler.CharacterCommands/HandlePDumpWriteCommand`.

**`GetDump(uint32 guid)`**
Generates the full SQL dump string for the character identified by `guid`. It iterates through the `DumpTableType` enum, calling the private helper `DumpTableContent` for each table group. For tables requiring dependent GUIDs (e.g., `DTT_INVENTORY`), it first populates the internal sets by querying the database. The resulting SQL fragments are accumulated into a single `std::string` and returned.

**`WriteDump(std::string const& file, uint32 guid)`**
Writes the dump to disk. It calls `GetDump(guid)` to generate the content, opens the file specified by `file`, and writes the string. Returns `DUMP_FILE_OPEN_ERROR` if the file cannot be opened, or `DUMP_SUCCESS` on completion.

### Internal Helpers

**`DumpTableContent(std::string& dump, uint32 guid, char const* tableFrom, char const* tableTo, DumpTableType type)`**
Handles dumping for a specific `DumpTableType`. It constructs `SELECT` queries to fetch rows matching the character’s GUID or the collected GUIDs in the internal sets. Each row is formatted into an `INSERT` statement and appended to `dump`.

**`GenerateWhereStr(char const* field, GUIDs const& guids, GUIDs::const_iterator& itr)`**
Generates a SQL `WHERE` clause fragment for a set of GUIDs, producing a string like `"field IN (123, 456)"`. Used for tables depending on multiple IDs.

**`GenerateWhereStr(char const* field, uint32 guid)`**
Generates a `WHERE` clause for a single GUID, used for primary character tables.

## Cross-Unit Boundaries

**Called by: `ChatHandler.CharacterCommands/HandlePDumpWriteCommand`**
The chat command handler instantiates `PlayerDumpWriter` when a Game Master executes `.pdump write`. The handler passes the target character’s GUID and the output file path to `WriteDump`. The writer returns a `DumpReturn` status, which the handler reports to the user.

**Calls out: Database Infrastructure**
`PlayerDumpWriter` executes `SELECT` queries against the live database. It relies on the server’s database connection pool (managed externally) to retrieve data from the tables listed in the Data Model section.

## Data Model

`PlayerDumpWriter` queries the following tables, organized by `DumpTableType`:

*   **`DTT_CHARACTER`**: `characters` (basic character info).
*   **`DTT_CHAR_TABLE`**: `character_action`, `character_aura`, `character_homebind`, `character_queststatus`, `character_reputation`, `character_spell`, `character_spell_cooldown`, `character_tutorial`.
*   **`DTT_INVENTORY`**: `character_inventory` (links character to item instances).
*   **`DTT_MAIL`**: `mail` (mail headers).
*   **`DTT_MAIL_ITEM`**: `mail_items` (items attached to mail, depends on mail IDs).
*   **`DTT_ITEM`**: `item_instance` (detailed item data, depends on item GUIDs).
*   **`DTT_ITEM_GIFT`**: `character_gifts` (gifted items, depends on item GUIDs).
*   **`DTT_ITEM_LOOT`**: `item_loot` (loot in items, depends on item GUIDs).
*   **`DTT_PET`**: `character_pet` (pet records).
*   **`DTT_PET_TABLE`**: `pet_aura`, `pet_spell`, `pet_spell_cooldown` (depends on pet GUIDs).
*   **`DTT_ITEM_TEXT`**: `item_text` (text descriptions, depends on item text IDs).

The dump order ensures that parent records (e.g., `characters`) are dumped before or alongside their dependents, while GUID collections allow dependent tables (e.g., `item_instance`) to be filtered correctly.

## Notable Implementation Details

1.  **Two-Pass GUID Collection:** The class uses `std::set<uint32>` members to collect GUIDs for items, mail, pets, and texts before generating SQL. This ensures that tables like `item_loot` or `mail_items` are included even if they are not directly linked to the character’s main GUID.
2.  **Memory Usage:** The entire dump is built as a single `std::string`. For characters with large inventories or mailboxes, this may consume significant memory.
3.  **Schema Assumption:** The class assumes the database schema matches the structure implied by `DumpTableType`. It does not validate column existence or types at runtime.

## Member Reference

**`PlayerDumpWriter()`**
Default constructor. Initializes internal `GUIDs` sets (`pets`, `mails`, `items`, `texts`) to empty. Does not perform I/O. Called by `ChatHandler.CharacterCommands/HandlePDumpWriteCommand` to create an instance for dumping a character.

---

<!-- machine-true, projected from graph.json -->

## Map — PlayerDumpWriter

*Source:* PlayerDump.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| PlayerDumpWriter | ctor | — | ChatHandler.CharacterCommands/HandlePDumpWriteCommand | — |
