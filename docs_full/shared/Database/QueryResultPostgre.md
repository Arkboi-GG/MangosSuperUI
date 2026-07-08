<!-- provenance: failed-members -->
# QueryResultPostgre

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# QueryResultPostgre

**Purpose & Responsibilities**

`QueryResultPostgre` is the PostgreSQL-specific implementation of the abstract `QueryResult` interface within the `wowvmangos` database abstraction layer. Its primary responsibility is to wrap a raw `PGresult` pointer (provided by the libpq client library) and expose it as a standardized, type-safe iterator over the rows returned by a SQL query.

This unit handles the translation between PostgreSQL’s native data formats and the application’s internal `Field` objects. It manages the lifecycle of the result set, including allocating a buffer for the current row, determining column types via PostgreSQL Object IDs (OIDs), and freeing resources when the iteration is complete or the object is destroyed. The entire unit is conditionally compiled only when `DO_POSTGRESQL` is defined, ensuring it is included only in builds targeting PostgreSQL databases.

## Member-by-Member Behavior

### Construction and Initialization
The constructor `QueryResultPostgre(PGresult*, uint64, uint32)` initializes the wrapper with a pointer to a completed PostgreSQL result set, the total row count, and the field (column) count. It performs two key setup steps:
1.  **Buffer Allocation:** It allocates a heap-based array of `Field` objects (`mCurrentRow`) sized to `mFieldCount`. This array holds the parsed data for the row currently being accessed by the iterator.
2.  **Type Resolution:** It iterates through each column index, calling `PQftype` to retrieve the PostgreSQL OID for that column. It then invokes `ConvertNativeType` to map the OID to an internal `Field::DataTypes` enum value (e.g., `DB_TYPE_STRING`, `DB_TYPE_INTEGER`). This ensures that subsequent data extraction uses the correct internal representation.

### Row Iteration
The public method `NextRow()` drives the iteration process:
1.  It validates that the underlying `PGresult` is present and that the internal index `mTableIndex` has not exceeded `mRowCount`.
2.  If valid, it loops through each column, retrieving the raw C-string value via `PQgetvalue`.
3.  **Null Handling:** It checks if the returned pointer is non-null but points to an empty string (`!(*pPQgetvalue)`). In this case, it converts the pointer to `nullptr` before passing it to `Field::SetValue`. This treats empty strings as NULL values for the purpose of storage.
4.  It increments `mTableIndex` and returns `true`.
5.  If the end of the result set is reached, it calls `EndQuery()` to clean up resources and returns `false`.

### Resource Cleanup
The private method `EndQuery()` releases all resources held by the object. It deletes the `mCurrentRow` array and calls `PQclear` on the `mResult` pointer to free the memory allocated by libpq. The destructor `~QueryResultPostgre()` delegates entirely to `EndQuery()`.

### Type Conversion Logic
The private method `ConvertNativeType(Oid)` maps PostgreSQL OIDs to `Field::DataTypes`. This mapping determines how the application interprets raw data:
*   **Strings:** Character types (`TEXTOID`, `VARCHAROID`, `BPCHAROID`, etc.) map to `DB_TYPE_STRING`.
*   **Floats:** Floating-point and numeric types (`FLOAT4OID`, `FLOAT8OID`, `NUMERICOID`, `CASHOID`) map to `DB_TYPE_FLOAT`.
*   **Integers:** Integer types (`INT2OID`, `INT4OID`, `INT8OID`, etc.) and date/time types (`DATEOID`, `TIMESTAMPOID`, `TIMETZOID`, etc.) map to `DB_TYPE_INTEGER`. This implies that date/time values are expected to be handled as numeric timestamps or epoch values internally.
*   **Bools:** `BOOLOID` maps to `DB_TYPE_BOOL`.
*   **Unknown:** Unmapped or complex geometric types default to `DB_TYPE_UNKNOWN`.

## Cross-Unit Boundaries

*   **Calls Out:**
    *   **`QueryResult` (Base Class):** The constructor calls the base class constructor `QueryResult(rowCount, fieldCount)` to establish the common interface.
    *   **`Field` (Class):** The constructor calls `Field::SetType` to define column types, and `NextRow` calls `Field::SetValue` to populate row data.
    *   **libpq (External Library):** Directly calls `PQftype` (column type metadata), `PQgetvalue` (cell data retrieval), and `PQclear` (resource deallocation).
    *   **`DatabaseEnv.h`:** Included for macro definitions such as `MANGOS_ASSERT`.

*   **Called By:**
    *   The MAP lists no specific callers. Logically, this class is instantiated by the PostgreSQL-specific database connection manager (e.g., `DatabasePostgre`) upon query execution. The rest of the application interacts with it via the `QueryResult` base interface.

## Data Model

This unit does not interact with specific database tables. It operates on transient result sets provided by the PostgreSQL client library. Schema information is derived dynamically from the `PGresult` metadata at runtime. No static table definitions or SQL queries are present in this unit.

## Notable Implementation Details

1.  **Empty String as NULL:** The logic `if(pPQgetvalue && !(*pPQgetvalue)) pPQgetvalue = nullptr;` in `NextRow` explicitly treats empty strings as NULL values. This means the application cannot distinguish between a database NULL and an empty string `''` for this backend.
2.  **Date/Time as Integers:** Date and time OIDs are mapped to `DB_TYPE_INTEGER`. Callers must interpret these integer values as timestamps or epoch seconds, consistent with the application's internal date handling strategy.
3.  **Conditional Compilation:** The entire source file is wrapped in `#ifdef DO_POSTGRESQL`, excluding it from non-PostgreSQL builds.
4.  **OID Definitions:** On non-Windows platforms, the header manually defines PostgreSQL OIDs to ensure compatibility when server headers (`pg_type.h`) are unavailable or inconsistent. On Windows, it relies on `<postgre/pg_type.h>`.
5.  **Fail-Fast Allocation:** The constructor uses `MANGOS_ASSERT(mCurrentRow)` after allocation to crash immediately if memory allocation fails, preventing undefined behavior from null pointer dereferences.

## Member Reference

**QueryResultPostgre(PGresult* result, uint64 rowCount, uint32 fieldCount)**
Constructor that initializes the result wrapper. It allocates a `Field` array for the current row and iterates through all columns to determine their data types by calling `PQftype` and `ConvertNativeType`. It passes `rowCount` and `fieldCount` to the base `QueryResult` class.

**~QueryResultPostgre()**
Destructor that calls `EndQuery()` to release the `PGresult` resource and the allocated `Field` array.

**NextRow()**
Advances the internal row index. If a valid row exists, it extracts each column's value using `PQgetvalue`, handles NULL/empty string conversion, and populates the `mCurrentRow` `Field` array via `Field::SetValue`. Returns `true` if successful, `false` if the end of the result set is reached (triggering cleanup via `EndQuery`).

**ConvertNativeType(Oid pOid)**
Private helper that maps a PostgreSQL OID to an internal `Field::DataTypes` enum value. It uses a switch statement to categorize OIDs into Strings, Floats, Integers (including dates/times), Bools, or Unknown.

**EndQuery()**
Private cleanup method. It deletes the `mCurrentRow` array and calls `PQclear` on the `mResult` pointer to free the PostgreSQL result memory. Sets pointers to zero/null to prevent double-free.

---

<!-- machine-true, projected from graph.json -->

## Map — QueryResultPostgre

*Source:* QueryResultPostgre.cpp, QueryResultPostgre.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|

---

<!-- verify: failed-members | invented: ConvertNativeType, EndQuery, NextRow, ~QueryResultPostgre -->
