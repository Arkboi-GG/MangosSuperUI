<!-- provenance: failed-members -->
# SQLStorage

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# SQLStorage

## Purpose & Responsibilities

`SQLStorage` provides a generic, high-performance framework for loading static configuration data from the World of Warcraft database into contiguous memory blocks. It acts as the bridge between the relational database layer and the in-memory game state, allowing managers (such as `ObjectMgr`, `LootMgr`, etc.) to define custom data structures that are populated directly from SQL result sets.

The core design principle is **zero-copy iteration** and **direct memory mapping**. Instead of storing individual objects in a heap-allocated container (like `std::vector<std::unique_ptr<T>>`), `SQLStorage` allocates a single large `char` buffer (`m_data`). Records are packed sequentially into this buffer according to a format string. This allows for cache-friendly iteration and minimal memory overhead.

The system supports three distinct indexing strategies via inheritance:
1.  **`SQLStorage`**: Uses a dense array (`char**`) for O(1) lookup by ID. Suitable for tables with contiguous, low-range IDs (e.g., creature display info).
2.  **`SQLHashStorage`**: Uses a hash map (`std::unordered_map`) for O(1) average lookup. Suitable for sparse or high-range IDs.
3.  **`SQLMultiStorage`**: Uses a multimap (`std::multimap`) to support multiple records per ID. Suitable for one-to-many relationships (e.g., a creature having multiple loot templates).

This unit does not execute SQL queries itself. It relies on derived loader classes (`SQLStorageLoader`, `SQLHashStorageLoader`, `SQLMultiStorageLoader`) to handle the actual database interaction and type conversion, passing the raw data back to the storage base for packing.

## Member-by-Member Behavior

### Initialization and Configuration

*   **`SQLStorageBase` (ctor)**: Initializes all metadata pointers and counters to null/zero.
*   **`Initialize`**: Sets the table name, entry field name, and source/destination format strings. It calculates the number of fields by taking the length of the format strings. This method is called by the constructors of the derived storage classes.
*   **`GetTableName`**, **`EntryFieldName`**, **`GetDstFormat`**, **`GetSrcFormat`**, **`GetDstFieldCount`**, **`GetSrcFieldCount`**: Accessors for the metadata set during initialization. These allow the loader to understand how to parse the SQL result set and how to pack the data into the memory buffer.
*   **`GetMaxEntry`**, **`GetRecordCount`**, **`GetRecordSize`**: Accessors for runtime statistics. `GetMaxEntry` is critical for bounds-checking lookups in `SQLStorage`.

### Memory Management and Loading Preparation

*   **`prepareToLoad`** (in `SQLStorageBase`): Allocates the main data buffer (`m_data`) based on the expected record count and size. It zeros out the memory.
*   **`prepareToLoad`** (in `SQLStorage`): Overrides the base method to also allocate the index array (`m_Index`) based on `maxRecordId`. It calls the base `prepareToLoad` after clearing old data.
*   **`prepareToLoad`** (in `SQLHashStorage` / `SQLMultiStorage`): Clears the respective maps (`m_indexMap` or `m_indexMultiMap`) and calls the base `prepareToLoad`.
*   **`createRecord`**: Allocates space for a single new record within the contiguous `m_data` buffer. It increments the internal record counter and returns a pointer to the start of the new record. It then calls the virtual `JustCreatedRecord` hook.
*   **`JustCreatedRecord`** (virtual): Implemented differently by each derived class to update its specific index structure:
    *   `SQLStorage`: Stores the record pointer in `m_Index[recordId]`.
    *   `SQLHashStorage`: Inserts the record pointer into `m_indexMap[recordId]`.
    *   `SQLMultiStorage`: Inserts the record pointer into `m_indexMultiMap` using `recordId` as the key.

### Data Loading Execution

*   **`Load`** (in `SQLStorage`): Instantiates a `SQLStorageLoader` and delegates the loading process. The loader executes the SQL query, iterates over results, converts types, and calls `storeValue` to pack data into the buffer managed by `SQLStorageBase`.
*   **`LoadProgressive`** (in `SQLStorage`): Similar to `Load`, but filters results by a `wow_patch` column, allowing for incremental loading of data relevant to specific game patches.
*   **`Load`** (in `SQLHashStorage` / `SQLMultiStorage`): Delegates to their respective loader types (`SQLHashStorageLoader`, `SQLMultiStorageLoader`).

### Lookup and Iteration

*   **`LookupEntry`** (in `SQLStorage`): Performs a bounds check against `GetMaxEntry()` and returns the record pointer from `m_Index`. Returns `nullptr` if the ID is out of bounds or not present.
*   **`LookupEntry`** (in `SQLHashStorage`): Looks up the ID in `m_indexMap` and returns the record pointer.
*   **`getBounds`** (in `SQLMultiStorage`): Returns a pair of iterators (`SQLMSIteratorBounds`) defining the range of records associated with a specific key in the multimap.
*   **`begin` / `end`** (in `SQLStorageBase`): Return `SQLSIterator` objects pointing to the start and end of the contiguous `m_data` buffer. This enables standard range-based for loops over the stored records.
*   **`SQLSIterator`**: A simple pointer-based iterator that advances by `m_recordSize` bytes. It provides `getValue()` to cast the raw char pointer to the user-defined struct type `T`.
*   **`SQLMultiSIterator`**: An iterator wrapping a `std::multimap::const_iterator`. It provides `getKey()` to retrieve the ID and `getValue()` to retrieve the record data.

### Cleanup and Modification

*   **`Free`** (in `SQLStorageBase`): Iterates through the format string to identify `FT_STRING` fields. For each string field, it deletes the dynamically allocated string buffers for all records. Finally, it deletes the main `m_data` buffer. **Note:** There is a known potential memory leak for `FT_NA_POINTER` fields, as indicated by a comment in the source.
*   **`Free`** (in `SQLStorage`): Calls base `Free()` and then deletes the `m_Index` array.
*   **`Free`** (in `SQLHashStorage` / `SQLMultiStorage`): Calls base `Free()` and clears the respective maps.
*   **`EraseEntry`** (in `SQLStorage`): Sets the corresponding entry in `m_Index` to `nullptr`. It does **not** free the memory occupied by the record in `m_data`, nor does it remove the record from the contiguous block. This is a logical removal only.
*   **`EraseEntry`** (in `SQLHashStorage`): Sets the mapped value to `nullptr`. Like `SQLStorage`, it does not free the underlying record memory.
*   **`EraseEntry`** (in `SQLMultiStorage`): Removes all entries with the given ID from the multimap. Again, the underlying memory in `m_data` is not freed.

## Cross-Unit Boundaries

### Called By: ObjectMgr, LootMgr, MapPersistentStateMgr

Various manager classes use `SQLStorage` derivatives to load their specific configuration tables.

*   **`ObjectMgr/LoadCreatureAddons`**, **`ObjectMgr/LoadConditions`**, **`ObjectMgr/LoadPageTexts`**, etc.: These functions instantiate specific storage classes (e.g., `SQLStorage<CreatureAddon>`), call `prepareToLoad()`, and then invoke `Load()` or `LoadProgressive()`. They rely on `GetMaxEntry()` and `GetRecordCount()` to validate the loaded data.
*   **`ObjectMgr/LoadCreatureAddons#2`**: Calls `GetTableName()` to log or verify the source table.

### Calls Out: SQLStorageLoader, SQLHashStorageLoader, SQLMultiStorageLoader

The `Load` methods in the storage classes delegate to these loader classes. The loaders are responsible for:
1.  Executing the SQL query.
2.  Iterating over the result set.
3.  Converting SQL field types (via `convert_*` methods) to C++ types.
4.  Calling `storeValue` on the storage object to pack the converted data into the `m_data` buffer.

This separation allows the storage mechanism to remain agnostic of the specific SQL dialect or connection details, while the loaders handle the database-specific logic.

## Data Model

This unit does not define database tables. It operates on the result sets returned by queries executed by the loader classes. The schema of the target tables is defined externally in the database and reflected in the format strings passed to `Initialize()`. The format strings (e.g., `"i"`, `"s"`, `"b"`) dictate how the binary data is packed into the `m_data` buffer.

Common tables loaded via this mechanism include:
*   `creature_addon`
*   `conditions`
*   `page_text`
*   `mail_template`
*   `areatrigger_tavern`
*   `gameobject_display_info`

## Notable Implementation Details

1.  **Contiguous Memory Layout**: The most significant performance characteristic is the use of a single `char*` buffer for all records. This improves cache locality during iteration compared to pointer-chasing through individually allocated objects.
2.  **Format Strings**: The `src_format` and `dst_format` strings control the parsing and packing. Characters like `'i'` (int), `'s'` (string), `'b'` (bool) determine the size and type of each field. The `Free()` method uses `dst_format` to correctly deallocate string buffers.
3.  **Memory Leak Warning**: In `SQLStorageBase::Free()`, there is a comment `// TODO- possible (and small) memleak here possible` next to the `FT_NA_POINTER` case. This indicates that pointers stored with this format type are not explicitly deleted, potentially leaking memory if they point to dynamically allocated resources.
4.  **Logical vs. Physical Deletion**: `EraseEntry` methods only nullify the index entry. The actual memory in `m_data` remains allocated until `Free()` is called. This means `EraseEntry` is not suitable for freeing memory; it is only for hiding records from lookup.
5.  **Index Bounds Checking**: `SQLStorage::LookupEntry` checks `id >= GetMaxEntry()` before accessing `m_Index`. This prevents out-of-bounds access if an invalid ID is queried. `SQLHashStorage` and `SQLMultiStorage` do not perform this check, relying on the map's `find` or `equal_range` methods.
6.  **Progressive Loading**: `LoadProgressive` allows loading data filtered by a `patch` column. This is useful for maintaining compatibility across different WoW versions or for incremental updates.
7.  **Iterator Safety**: The `SQLSIterator` assumes that the `m_data` buffer remains valid and unchanged during iteration. Modifying the storage (e.g., calling `Load` again) while iterating will invalidate iterators.

## Member Reference

**SQLStorageBase** (ctor): Initializes metadata pointers and counters to null/zero.
**GetTableName**: Returns the name of the SQL table associated with this storage.
**EntryFieldName**: Returns the name of the field used as the primary key/ID.
**GetDstFormat#2**: Returns the destination format string as a `char const*`.
**GetDstFormat**: Returns the `FieldFormat` enum for a specific field index.
**GetSrcFormat#2**: Returns the source format string as a `char const*`.
**Initialize**: Sets table name, entry field, and format strings; calculates field counts.
**GetSrcFormat**: Returns the `FieldFormat` enum for a specific field index.
**GetMaxEntry**: Returns the maximum entry ID expected (used for bounds checking).
**GetRecordCount**: Returns the number of records currently loaded.
**createRecord**: Allocates space in `m_data` for a new record and calls `JustCreatedRecord`.
**getValue#2**: (In `SQLSIterator`) Casts the internal pointer to `T const*`.
**operator++#2**: (In `SQLSIterator`) Advances the pointer by `recordSize`.
**operator*#2**: (In `SQLSIterator`) Dereferences the iterator to return the value.
**operator->#2**: (In `SQLSIterator`) Returns a pointer to the value.
**operator<**: (In `SQLSIterator`) Compares pointers for ordering.
**operator==#2**: (In `SQLSIterator`) Checks pointer equality.
**operator!=#2**: (In `SQLSIterator`) Checks pointer inequality.
**operator=**: (In `SQLSIterator`) Copies pointer and record size from another iterator.
**prepareToLoad#4**: (In `SQLStorageBase`) Allocates and zeros the `m_data` buffer.
**SQLSIterator<T>** (ctor): Initializes the iterator with a pointer and record size.
**Free#4**: (In `SQLStorageBase`) Deallocates string buffers and the `m_data` array.
**~SQLStorageBase** (dtor): Calls `Free()` to clean up resources.
**GetDstFieldCount**: Returns the number of fields in the destination format.
**GetSrcFieldCount**: Returns the number of fields in the source format.
**GetRecordSize**: Returns the size in bytes of a single record.
**JustCreatedRecord#2**: (Virtual) Hook called after a record is created; implemented by derived classes.
**~SQLStorage** (dtor): Calls `Free()` to clean up resources.
**EraseEntry#3**: (In `SQLStorage`) Sets the index entry to `nullptr`; does not free memory.
**JustCreatedRecord**: (In `SQLStorage`) Stores the record pointer in `m_Index`.
**Free#3**: (In `SQLStorage`) Calls base `Free()` and deletes `m_Index`.
**Load#3**: (In `SQLStorage`) Delegates to `SQLStorageLoader` to load data.
**LoadProgressive**: (In `SQLStorage`) Loads data filtered by patch version.
**SQLStorage** (ctor): Initializes with identical source/destination formats.
**SQLStorage#2** (ctor): Initializes with separate source/destination formats.
**prepareToLoad#3**: (In `SQLStorage`) Allocates `m_Index` and calls base `prepareToLoad`.
**Load**: (In `SQLStorage`) Delegates to `SQLStorageLoader`.
**Free**: (In `SQLStorage`) Cleans up index and base data.
**prepareToLoad**: (In `SQLStorage`) Prepares index and base data.
**EraseEntry**: (In `SQLStorage`) Nullifies index entry.
**SQLHashStorage** (ctor): Initializes with identical source/destination formats.
**getValue**: (In `SQLMultiSIterator`) Casts the map value to `T const*`.
**getKey**: (In `SQLMultiSIterator`) Returns the key from the map iterator.
**operator++**: (In `SQLMultiSIterator`) Advances the map iterator.
**operator***: (In `SQLMultiSIterator`) Dereferences to return the value.
**SQLHashStorage#2** (ctor): Initializes with separate source/destination formats.
**operator->**: (In `SQLMultiSIterator`) Returns a pointer to the value.
**operator!=**: (In `SQLMultiSIterator`) Checks iterator inequality.
**operator==**: (In `SQLMultiSIterator`) Checks iterator equality.
**Load#2**: (In `SQLHashStorage`) Delegates to `SQLHashStorageLoader`.
**SQLMultiSIterator<T>** (ctor): Initializes with a multimap iterator.
**Free#2**: (In `SQLHashStorage`) Calls base `Free()` and clears the map.
**prepareToLoad#2**: (In `SQLHashStorage`) Clears map and calls base `prepareToLoad`.
**SQLMSIteratorBounds<T>** (ctor): Initializes with a pair of iterators.
**EraseEntry#2**: (In `SQLHashStorage`) Sets map value to `nullptr`.
**SQLMultiStorage** (ctor): Initializes with identical source/destination formats.
**SQLMultiStorage#2** (ctor): Initializes with separate source/destination formats.
**convert_str_to_str**: (Decl) Trap function in `SQLStorageLoaderBase` to prevent incorrect usage.
**storeValue**: (Decl) Template function to pack a value into the record buffer.

---

<!-- machine-true, projected from graph.json -->

## Map — SQLStorage

*Source:* SQLStorage.cpp, SQLStorage.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| SQLStorageBase | ctor | — | — | — |
| GetTableName | method | — | ObjectMgr/LoadCreatureAddons#2 | — |
| EntryFieldName | method | — | — | — |
| GetDstFormat#2 | method | — | — | — |
| GetDstFormat | method | — | — | — |
| GetSrcFormat#2 | method | — | — | — |
| Initialize | method | — | — | — |
| GetSrcFormat | method | — | — | — |
| GetMaxEntry | method | — | LootMgr/LoadLootTemplates_Mail, MapPersistentStateMgr/LoadResetTimes, ObjectMgr/LoadConditions, ObjectMgr/LoadCreatureAddons, ObjectMgr/LoadCreatureAddons#2, ObjectMgr/LoadCreatureDisplayInfoAddon, ObjectMgr/LoadPageTexts | — |
| GetRecordCount | method | — | ObjectMgr/LoadConditions, ObjectMgr/LoadCreatureAddons#2, ObjectMgr/LoadCreatureDisplayInfoAddon, ObjectMgr/LoadGameObjectDisplayInfoAddon, ObjectMgr/LoadMailTemplate, ObjectMgr/LoadMapTemplate, ObjectMgr/LoadPageTexts, ObjectMgr/LoadPetSpellData | — |
| createRecord | method | — | — | — |
| getValue#2 | function | — | — | — |
| operator++#2 | function | — | — | — |
| operator*#2 | function | — | — | — |
| operator->#2 | function | — | — | — |
| operator< | function | — | — | — |
| operator==#2 | function | — | — | — |
| operator!=#2 | function | — | — | — |
| operator= | function | — | — | — |
| prepareToLoad#4 | method | — | — | — |
| SQLSIterator<T> | ctor | — | — | — |
| Free#4 | method | — | — | — |
| ~SQLStorageBase | dtor | — | — | — |
| GetDstFieldCount | method | — | — | — |
| GetSrcFieldCount | method | — | — | — |
| GetRecordSize | method | — | — | — |
| JustCreatedRecord#2 | decl | — | — | — |
| ~SQLStorage | dtor | — | — | — |
| EraseEntry#3 | method | — | ObjectMgr/LoadConditions, ObjectMgr/LoadMapTemplate | — |
| JustCreatedRecord | method | — | — | — |
| Free#3 | method | — | — | — |
| Load#3 | method | — | ObjectMgr/LoadAreaTemplate, ObjectMgr/LoadGameObjectDisplayInfoAddon, ObjectMgr/LoadMailTemplate, ObjectMgr/LoadPageTexts | — |
| LoadProgressive | method | — | ObjectMgr/LoadCreatureAddons#2, ObjectMgr/LoadCreatureDisplayInfoAddon, ObjectMgr/LoadPetSpellData | — |
| SQLStorage | ctor | — | — | — |
| SQLStorage#2 | ctor | — | — | — |
| prepareToLoad#3 | method | — | — | — |
| Load | method | — | — | — |
| Free | method | — | — | — |
| prepareToLoad | method | — | — | — |
| EraseEntry | method | — | — | — |
| SQLHashStorage | ctor | — | — | — |
| getValue | function | — | — | — |
| getKey | function | — | — | — |
| operator++ | function | — | — | — |
| operator* | function | — | — | — |
| SQLHashStorage#2 | ctor | — | — | — |
| operator-> | function | — | — | — |
| operator!= | function | — | — | — |
| operator== | function | — | — | — |
| Load#2 | method | — | — | — |
| SQLMultiSIterator<T> | ctor | — | — | — |
| Free#2 | method | — | — | — |
| prepareToLoad#2 | method | — | — | — |
| SQLMSIteratorBounds<T> | ctor | — | — | — |
| EraseEntry#2 | method | — | — | — |
| SQLMultiStorage | ctor | — | — | — |
| SQLMultiStorage#2 | ctor | — | — | — |
| convert_str_to_str | decl | — | — | — |
| storeValue | decl | — | — | — |

---

<!-- verify: failed-members | invented: operator -->
