# DBCFileLoader

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`DBCFileLoader` is a low-level utility class responsible for parsing and providing access to **WDBC** (World of Warcraft Database Client) files. These binary files contain static game data (such as item definitions, creature templates, or spell effects) used by the server emulation layer.

The class performs two distinct roles:
1.  **Raw File Parsing:** It reads the binary structure of a `.dbc` file, handling endianness conversion, header validation, and memory mapping of records and the associated string table.
2.  **Data Transformation:** It provides helper methods (`AutoProduceData`, `AutoProduceStrings`) to convert the raw binary layout into contiguous C-style structs or arrays, optionally building index tables for fast lookup by specific field values (e.g., ID).

This unit does not interact with the SQL database; it operates entirely on local binary files. It relies on the `Record` inner class to provide typed accessors for individual fields within a record.

## Member-by-Member Behavior

### Initialization and Lifecycle

*   **`DBCFileLoader` (Constructor):** Initializes internal pointers (`data`, `fieldsOffset`) to `nullptr`. It does not load any file.
*   **`~DBCFileLoader` (Destructor):** Frees the dynamically allocated memory for the raw data buffer (`data`) and the field offset array (`fieldsOffset`). This ensures that if `Load` was called multiple times, only the most recent allocation is freed (since `Load` deletes previous data before allocating new).

### File Loading

*   **`Load`:** The core ingestion method. It takes a filename and a format string (`fmt`).
    1.  **Cleanup:** If data is already loaded, it frees the existing buffer.
    2.  **Header Validation:** Opens the file in binary mode and reads the first 4 bytes. It expects the magic number `0x43424457` ('WDBC'). If this check fails, the file is rejected.
    3.  **Metadata Extraction:** Reads four 32-bit integers: `recordCount`, `fieldCount`, `recordSize`, and `stringSize`. Each is passed through `EndianConvert` to ensure native byte order.
    4.  **Offset Calculation:** Allocates an array `fieldsOffset` to track the byte position of each field within a record. It iterates through the `fmt` string:
        *   `'b'` (byte) or `'X'` (ignored byte) adds 1 byte to the offset.
        *   All other characters (representing 4-byte fields like floats, ints, or string pointers) add 4 bytes.
    5.  **Memory Allocation:** Allocates a single contiguous block for all records (`recordSize * recordCount`) followed immediately by the string table (`stringSize`).
    6.  **Data Read:** Reads the entire remaining file content into the allocated buffer.
    7.  **String Table Pointer:** Sets `stringTable` to point to the start of the string data section within the allocated buffer.

### Accessors and Metadata

*   **`IsLoaded`:** Returns `true` if the `data` pointer is non-null, indicating a successful prior `Load` call.
*   **`GetNumRows`:** Returns the `recordCount` parsed from the file header.
*   **`GetCols`:** Returns the `fieldCount` parsed from the file header.
*   **`GetOffset`:** Returns the pre-calculated byte offset for a specific field index. This is used internally by `Record` to locate fields. It safely returns 0 if the index is out of bounds or offsets haven't been calculated.

### Record Access

*   **`getRecord`:** Creates and returns a `Record` object for a specific row index (`id`). It calculates the base pointer for that record (`data + id * recordSize`) and passes it to the `Record` constructor. The `Record` object holds a reference to the loader and the specific byte offset, allowing typed access to fields.

### Data Transformation Helpers

These methods allow consumers to convert the raw binary data into more convenient C structures, often used for performance-critical lookups.

*   **`GetFormatRecordSize`:** A static utility that calculates the size of a C-struct based on a format string. It iterates through the format characters, summing the sizes of `float`, `uint32`, `char*`, and `uint8`. It also identifies the index of any `FT_IND` (indexed) or `FT_SORT` field, returning its position in `index_pos`. This is used by `AutoProduceData` to determine how much memory to allocate for the transformed data.

*   **`AutoProduceData`:** Converts raw DBC records into a contiguous block of memory (`dataTable`) structured according to the `format` string.
    1.  **Validation:** Ensures the format string length matches the number of fields in the DBC.
    2.  **Index Table Generation:** If the format contains an indexed field (`FT_IND` or `FT_SORT`), it scans all records to find the maximum index value. It allocates an `indexTable` of pointers sized to `max_index + 1`. If no index field exists, it creates an index table sized to `recordCount`, mapping linearly.
    3.  **Data Copying:** Iterates through every record in the DBC. For each record, it copies field values into the `dataTable` based on the format:
        *   `FT_FLOAT`, `FT_INT`, `FT_BYTE`: Copies the raw value.
        *   `FT_STRING`: Initializes the pointer to `nullptr` (to be resolved later by `AutoProduceStrings`).
        *   `FT_IND`/`FT_SORT`: Uses the value of this field to place the record's address in the `indexTable`.
    4.  **Return:** Returns the pointer to the `dataTable`. The caller receives the `count` (number of entries in the index table) and the `indexTable` itself via reference parameters.

*   **`AutoProduceStrings`:** Resolves string pointers in a previously generated `dataTable`.
    1.  **String Pool Copy:** Allocates a new buffer (`stringPool`) and copies the raw string table from the DBC file into it. This decouples the string data from the original file buffer, allowing the original `data` buffer to potentially be freed or reused (though typically the loader persists).
    2.  **Pointer Resolution:** Iterates through the `dataTable` again. For every field marked as `FT_STRING`:
        *   It retrieves the original string pointer from the DBC record.
        *   If the slot in `dataTable` is empty (null or pointing to null), it calculates the relative offset of the string within the new `stringPool` and updates the pointer in `dataTable` to point to the correct location in the new pool.
    3.  **Return:** Returns the pointer to the `stringPool`. The caller must manage the lifetime of both the `dataTable` and the `stringPool`.

## Cross-Unit Boundaries

*   **Calls `Record` methods:** `AutoProduceData` and `AutoProduceStrings` rely heavily on the inner class `Record` (defined in `DBCFileLoader.h`) to fetch typed values (`getFloat`, `getUInt`, `getUInt8`, `getString`) from the raw binary data. This encapsulates the endianness conversion and offset calculation logic.
*   **Called by External Units:** While the MAP shows no explicit callers, in the broader context of the wowvmangos codebase, this class is typically instantiated by specific DBC loader implementations (e.g., `ItemTemplateDBC`, `CreatureTemplateDBC`) which call `Load` and then use `AutoProduceData` to populate global lookup tables.

## Data Model

This unit does not interact with SQL database tables. It processes binary files on disk. The "schema" is defined by the WDBC file format:
*   **Header:** Magic number, record count, field count, record size, string table size.
*   **Records:** Fixed-size blocks containing raw binary data for each field.
*   **String Table:** A contiguous block of null-terminated strings referenced by offset from the start of the table.

## Notable Implementation Details

1.  **Endianness Handling:** The class assumes the host machine might differ from the file's endianness. It uses `EndianConvert` on all 32-bit integers and floats read from the file. However, `getUInt8` does not perform endianness conversion (correct, as bytes are invariant).
2.  **Format String Dependency:** The `Load` method requires a format string (`fmt`) to correctly calculate field offsets. This format string must match the actual structure of the DBC file. If the format string is incorrect, `fieldsOffset` will be wrong, leading to garbage data when accessing records.
3.  **Memory Management in `AutoProduceData`:**
    *   The method allocates `dataTable` and `indexTable`. The caller is responsible for deleting these.
    *   If an indexed field is present, `indexTable` is sparse (only valid indices are populated). The `records` output parameter reflects the size of the index table (max index + 1), not necessarily the number of records in the DBC.
4.  **String Pointer Resolution:** `AutoProduceStrings` assumes that `AutoProduceData` was called first with the same format string. It relies on the fact that `AutoProduceData` initialized string slots to `nullptr`. It then patches these pointers to point into the newly copied `stringPool`. This allows the original `data` buffer (which contained the original string table) to be independent of the transformed data.
5.  **Assertion Failures:** The code uses `assert` for format validation (e.g., unknown format characters, logic fields). In release builds, these checks are disabled, which could lead to undefined behavior if invalid format strings are provided.
6.  **No Error Handling for Malformed Files:** Beyond the magic number check, the class trusts the header counts (`recordCount`, `fieldCount`, etc.). If these values are corrupted, it may allocate excessive memory or read out of bounds.

## Member Reference

*   **`DBCFileLoader`**: Constructor; initializes `data` and `fieldsOffset` to `nullptr`.
*   **`Load`**: Loads a WDBC file; validates magic number, parses header, calculates field offsets based on format string, and allocates memory for records and string table.
*   **`GetNumRows`**: Returns the number of records in the loaded DBC file.
*   **`GetCols`**: Returns the number of fields in the loaded DBC file.
*   **`GetOffset`**: Returns the byte offset of a specific field within a record, used for raw pointer arithmetic.
*   **`IsLoaded`**: Returns `true` if data has been successfully loaded.
*   **`~DBCFileLoader`**: Destructor; frees allocated memory for data and field offsets.
*   **`getRecord`**: Returns a `Record` object for a given row index, enabling typed field access.
*   **`GetFormatRecordSize`**: Static method; calculates the size of a C-struct based on a format string and identifies the index of any indexed/sorted field.
*   **`AutoProduceData`**: Transforms raw DBC records into a contiguous C-style data table and an optional index table for fast lookup by a specific field value.
*   **`AutoProduceStrings`**: Resolves string pointers in a transformed data table by copying the string table to a new pool and updating pointers to reference the new locations.

---

<!-- machine-true, projected from graph.json -->

## Map — DBCFileLoader

*Source:* DBCFileLoader.cpp, DBCFileLoader.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| DBCFileLoader | ctor | — | — | — |
| Load | method | — | — | — |
| GetNumRows | method | — | — | — |
| GetCols | method | — | — | — |
| GetOffset | method | — | — | — |
| IsLoaded | method | — | — | — |
| ~DBCFileLoader | dtor | — | — | — |
| getRecord | method | Record/Record | — | — |
| GetFormatRecordSize | method | — | — | — |
| AutoProduceData | method | Record/getFloat, Record/getUInt, Record/getUInt8 | — | — |
| AutoProduceStrings | method | Record/getString | — | — |
