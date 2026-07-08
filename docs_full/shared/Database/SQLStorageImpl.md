# SQLStorageImpl

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# SQLStorageImpl

**Purpose & Responsibilities**

`SQLStorageImpl.h` defines the template implementation for `SQLStorageLoaderBase`, a utility class responsible for loading configuration and static data from MySQL database tables into in-memory C++ structures (`StorageClass`). This unit provides the core logic for:

1.  **Type Conversion:** Converting raw SQL field values (integers, floats, strings, booleans) into the specific C++ types expected by the target storage structure. It handles alignment issues on ARM architectures and provides default values for missing or "Not Applicable" (NA) fields.
2.  **Memory Layout Management:** Calculating the byte offset for each field within a contiguous memory block representing a single database row. It supports a flexible mapping between source SQL columns and destination C++ fields, allowing for skipped fields (NA types) and different data representations.
3.  **Data Loading Strategies:**
    *   **Standard Load (`Load`):** Loads all rows from a table, assuming the latest version of the data is always correct.
    *   **Progressive Load (`LoadProgressive`):** Loads data based on a "patch" column, selecting only the most recent version of each record that is compatible with a specified World of Warcraft patch version. This allows the server to maintain historical data versions in the database and load only what is relevant for the current game version.

The unit is designed to be generic via templates (`DerivedLoader`, `StorageClass`) but tightly coupled to the specific SQL query patterns and field type enums (`FT_*`) used by the WoWVMaNGOS project. It does not define the storage structures themselves but populates them.

**Member-by-Member Behavior**

The members of `SQLStorageLoaderBase` are grouped by their role in the data loading pipeline.

### Type Conversion Helpers

These functions handle the low-level conversion of data from SQL result sets to C++ variables. They are called by `storeValue` during the row processing loop.

*   **`convert`**: Converts a source value `src` of type `S` to a destination reference `dst` of type `D`. On ARM architectures, it checks for memory alignment. If `dst` is not aligned to its natural boundary, it uses `memcpy` to avoid potential hardware traps. Otherwise, it performs a direct cast assignment.
*   **`convert_to_bool`**: Converts any numeric source type `S` to a boolean `dst`. The result is `true` if `src` is non-zero, `false` otherwise.
*   **`convert_str_to_str`**: Allocates a new C-string (`char*`) for `dst` and copies the content from the source C-string `src`. If `src` is `nullptr`, it allocates a single-byte string containing only the null terminator. This ensures `dst` always points to valid, null-terminated memory.
*   **`convert_to_str`**: Sets `dst` to a newly allocated single-byte null-terminated string. This is used when a source value cannot be meaningfully converted to a string (e.g., converting a number to a string field where the number is irrelevant or missing).
*   **`convert_from_str`**: Sets the destination `dst` of type `D` to zero. On ARM, it uses `memcpy` for alignment safety. This is used when a source string cannot be meaningfully converted to a numeric type (e.g., parsing an empty string into an integer).
*   **`convert_str_to_bool`**: Sets `dst` to `false`. This is the fallback when a string source needs to be interpreted as a boolean but contains no meaningful truthy value.
*   **`default_fill`**: Similar to `convert`, but explicitly used for filling fields with default values (typically zero) when the source data is marked as "Not Applicable" (NA). It respects ARM alignment rules.
*   **`default_fill_to_str`**: Allocates a single-byte null-terminated string for `dst`. Used to provide a safe default for string fields that have no corresponding source data.

### Value Storage & Offset Management

*   **`storeValue` (Template Overload)**: Takes a generic value `V`, the target `store`, a pointer to the current record's memory buffer `p`, the destination field index `x`, and a reference to the current byte `offset`. It determines the destination field's format (`FT_*`) from the `store` and calls the appropriate conversion helper (`convert`, `convert_to_bool`, etc.) to write the value into the buffer at the current `offset`. It then advances `offset` by the size of the destination type.
*   **`storeValue` (String Overload)**: Specifically handles `char const*` source values. It follows the same pattern as the template overload but calls string-specific conversion helpers (`convert_str_to_str`, `convert_from_str`, `convert_str_to_bool`, `default_fill_to_str`).

### Data Loading Entry Points

*   **`Load`**: The primary function for loading a standard table.
    1.  Queries the maximum entry ID to size the lookup array.
    2.  Queries the total row count for progress reporting.
    3.  Validates that the number of columns in the SQL result matches the expected source field count defined in the `StorageClass`.
    4.  Calculates the total byte size of one record based on the destination field formats.
    5.  Prepares the `store` object with these metrics.
    6.  Iterates through each row:
        *   Creates a new record buffer in the `store`.
        *   Iterates through destination fields (`x`) and source columns (`y`).
        *   If a destination field is `FT_NA*`, it fills it with a default value and skips incrementing the source column index `y`.
        *   Otherwise, it reads the value from the source column `y`, converts it according to the source and destination formats, and stores it in the record buffer using `storeValue`.
        *   Advances both `x` and `y` appropriately.
    7.  Uses `BarGoLink` to display a progress bar during loading.
    8.  Exits the server with an error if critical tables are missing, empty (when `error_at_empty` is true), or have incompatible schemas.

*   **`LoadProgressive`**: Loads data with patch-version awareness.
    1.  Executes complex SQL queries to find the maximum entry ID and count rows, filtering for records where the `patch` column equals the maximum patch value less than or equal to the specified `wow_patch` for each entry. This effectively selects the "latest valid version" of each record.
    2.  Validates the schema, noting that the source field count is expected to be one less than the result column count because the `patch` column itself is excluded from the data mapping.
    3.  Calculates record size and prepares the `store`.
    4.  Iterates through rows similarly to `Load`, but with a key difference:
        *   It maintains a `patchoffset` variable.
        *   When processing the second destination field (`x == 1`) and the second source column (`y == 1`), it sets `patchoffset = 1`. This accounts for the fact that the SQL query returns the `patch` column as the first column after the entry ID, but the `StorageClass` definition likely does not include a field for the patch value itself. Therefore, subsequent source column accesses must be offset by 1 to skip the patch column in the result set.
        *   It uses `fields[y + patchoffset]` to read source data, ensuring the patch column is ignored during data extraction.
    5.  Also uses `BarGoLink` for progress and exits on critical errors.

**Cross-Unit Boundaries**

*   **Calls Out:**
    *   `WorldDatabase.PQuery`: Called extensively in `Load` and `LoadProgressive` to execute SQL queries against the world database. This is the primary interface for retrieving data.
    *   `sLog.Out`: Called to log errors and informational messages (e.g., table missing, empty, schema mismatch).
    *   `Log::WaitBeforeContinueIfNeed`: Called before exiting on critical errors, likely to allow log flushing or debugging pauses.
    *   `exit`: Called to terminate the server process if critical data tables are missing, inaccessible, or have incompatible schemas.
    *   `BarGoLink`: Instantiated in `Load` and `LoadProgressive` to display a progress bar during the potentially lengthy data loading process.
    *   `assert`: Used to catch programming errors such as unsupported field types (`FT_IND`, `FT_SORT`) or schema mismatches (too few columns). These will cause the program to abort if assertions are enabled.
    *   `strlen`, `memcpy`, `new`: Standard C library functions used for string handling and memory allocation.

*   **Called By:**
    *   The MAP indicates no external units call these members directly. This is consistent with the design: `SQLStorageLoaderBase` is a base class intended to be inherited by specific loader classes (defined elsewhere, e.g., `CreatureLoader`, `ItemLoader`). Those derived classes will instantiate `SQLStorageLoaderBase` functionality via their own `Load` methods, which in turn call the template implementations defined here. The `DerivedLoader` template parameter allows the base class to call back into the derived class if necessary (though in this implementation, it primarily casts `this` to `DerivedLoader*` to call virtual conversion methods, suggesting the derived class might override some conversion behavior).

**Data Model**

This unit interacts with arbitrary database tables defined by the `StorageClass` it is instantiated with. It does not hardcode specific table names. However, it relies on certain conventions:

*   **Entry Field:** Each table must have a unique identifier column, referred to as the "entry field". The name of this column is obtained via `store.EntryFieldName()`. This field is used to index records in the in-memory storage and is assumed to be an unsigned 32-bit integer.
*   **Patch Column (for Progressive Load):** When using `LoadProgressive`, the table must contain a column named according to the `column_name` parameter (defaulting to `"patch"`). This column stores the version/patch number associated with each record. The logic assumes that for a given entry ID, multiple rows may exist with different patch values, and only the row with the highest patch value less than or equal to the target `wow_patch` should be loaded.
*   **Field Types:** The unit operates on a set of predefined field types (`FT_LOGIC`, `FT_BYTE`, `FT_INT`, `FT_FLOAT`, `FT_STRING`, `FT_NA`, `FT_NA_BYTE`, `FT_NA_FLOAT`, `FT_NA_POINTER`, `FT_64BITINT`). These types dictate how data is read from the SQL result set and written to the in-memory buffer. The `StorageClass` must define the sequence and types of both source (SQL) and destination (C++) fields.

No specific table schemas are provided in the input, so column names, types, and constraints beyond the general conventions described above cannot be detailed.

**Notable Implementation Details**

1.  **ARM Alignment Handling:** The code explicitly checks for memory alignment on ARM architectures (`__arm__` or `_M_ARM`) in `convert`, `default_fill`, and `convert_from_str`. If the destination address is not naturally aligned, it uses `memcpy` to prevent potential hardware exceptions. This is a crucial detail for portability and stability on embedded or ARM-based servers.
2.  **Dynamic Memory Allocation for Strings:** String fields are handled by allocating new `char[]` buffers using `new`. This means the `StorageClass` is responsible for managing the lifetime of these strings, likely freeing them when the storage is cleared or destroyed. Failure to do so would result in memory leaks.
3.  **Strict Schema Validation:** Both `Load` and `LoadProgressive` perform strict validation of the SQL result set column count against the expected source field count defined in the `StorageClass`. If there's a mismatch, the server exits immediately. This prevents silent data corruption due to outdated or incorrect SQL schema files.
4.  **Critical Error Handling:** Missing tables, inaccessible tables, or empty tables (when `error_at_empty` is true) are treated as fatal errors, causing the server to exit. This reflects the importance of these configuration tables for server operation.
5.  **Progressive Load Complexity:** The SQL queries in `LoadProgressive` are complex correlated subqueries. They select the maximum patch value for each entry ID that is less than or equal to the target patch, then select the full row corresponding to that maximum patch. This ensures that only the most recent, compatible version of each record is loaded. The `patchoffset` logic in the C++ loop is essential to correctly map the SQL result columns (which include the patch column) to the C++ structure fields (which typically exclude it).
6.  **Unsupported Field Types:** The code asserts if it encounters `FT_IND` or `FT_SORT` field types. These types are likely used in other contexts (e.g., DBC file loading) but are not supported by the SQL storage mechanism. Attempting to use them will cause the program to abort.
7.  **Template Specialization via `DerivedLoader`:** The `storeValue` functions cast `this` to `DerivedLoader*` before calling conversion methods. This suggests that while the base class provides the default conversion logic, derived classes can override these methods to provide custom conversion behavior for specific data types or fields. This adds flexibility but also complexity to the inheritance hierarchy.

## Member Reference

*   **`convert_str_to_str`**: Allocates a new C-string and copies the source string `src` into it, assigning the pointer to `dst`. Handles `nullptr` source by allocating a single null-terminator.
*   **`convert_str_to_bool`**: Sets the boolean destination `dst` to `false`, used when a string source cannot be meaningfully converted to a boolean.
*   **`default_fill_to_str`**: Allocates a single-byte null-terminated string and assigns its pointer to `dst`, providing a default value for string fields.
*   **`storeValue`**: Determines the destination field format from the `store`, calls the appropriate conversion helper to write the value into the record buffer at the current `offset`, and advances the `offset`. Exists in two overloads: one for generic types `V` and one specifically for `char const*` sources.
*   **`Load`**: Loads all rows from a database table into the `store`. Validates table existence, schema compatibility, and emptiness. Iterates through rows, mapping source columns to destination fields, handling NA fields with defaults, and displaying progress. Exits on critical errors.
*   **`LoadProgressive`**: Loads rows from a database table, selecting only the most recent version (based on a `patch` column) compatible with a specified `wow_patch`. Uses complex SQL subqueries for selection and adjusts source column indexing (`patchoffset`) to skip the patch column during data extraction. Follows similar validation and error-handling procedures as `Load`.

---

<!-- machine-true, projected from graph.json -->

## Map — SQLStorageImpl

*Source:* SQLStorageImpl.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| convert_str_to_str | function | — | — | — |
| convert_str_to_bool | function | — | — | — |
| default_fill_to_str | function | — | — | — |
| storeValue | function | — | — | — |
| Load | function | — | — | — |
| LoadProgressive | function | — | — | — |
