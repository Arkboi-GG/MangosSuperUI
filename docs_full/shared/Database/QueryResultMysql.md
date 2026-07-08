<!-- provenance: verbose -->
# QueryResultMysql

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# QueryResultMysql

`QueryResultMysql` implements the MySQL-specific backend for `QueryResult`, wrapping the native `MYSQL_RES` handle. It manages the lifecycle of the result set and provides iteration over rows, translating raw MySQL data into the application’s `Field` abstraction. The unit is compiled only when PostgreSQL support is disabled (`#ifndef DO_POSTGRESQL`).

## Purpose & Responsibilities

1.  **Result Wrapping:** Holds the `MYSQL_RES*` pointer and ensures `mysql_free_result` is called upon completion or destruction.
2.  **Type Mapping:** During construction, inspects `MYSQL_FIELD` metadata to map native MySQL types to `Field::DataTypes` via `ConvertNativeType`.
3.  **Row Iteration:** Implements `NextRow()` to fetch subsequent rows, populating an internal array of `Field` objects with raw string data from `MYSQL_ROW`.
4.  **Resource Cleanup:** Centralizes deallocation of the `Field` array and the MySQL result set in `EndQuery()`, invoked by the destructor or when iteration ends.

## Member-by-Member Behavior

### Construction and Initialization
**`QueryResultMysql`**
Initializes the wrapper with a `MYSQL_RES*`, `MYSQL_FIELD*` array, row count, and field count.
-   Calls the base `QueryResult/QueryResult` constructor.
-   Allocates a dynamic array of `Field` objects (`mCurrentRow`) sized to `mFieldCount`. An assertion (`MANGOS_ASSERT`) verifies allocation success.
-   Iterates through the `MYSQL_FIELD` array, converting each native type via `ConvertNativeType` and setting it on the corresponding `Field` object using `Field/SetType`.

### Iteration
**`NextRow`**
Fetches the next row from the MySQL result set.
-   Returns `false` immediately if `mResult` is null.
-   Calls `mysql_fetch_row`. If it returns `NULL` (end of results or error), it calls `EndQuery` to release resources and returns `false`.
-   On success, iterates through all fields, calling `Field/SetValue` on each `mCurrentRow` element with the raw string value from the `MYSQL_ROW`.
-   Returns `true` on success.

### Cleanup
**`EndQuery`**
Releases all resources associated with the result set.
-   Deletes the `mCurrentRow` array and nullifies the pointer.
-   Calls `mysql_free_result` on `mResult` if non-null, then nullifies the pointer.
-   Safe to call multiple times due to null checks.

**`~QueryResultMysql`**
Calls `EndQuery` to ensure resources are freed.

### Type Conversion
**`ConvertNativeType`**
Maps MySQL `enum_field_types` to `Field::DataTypes`:
-   **String:** Timestamps, dates, times, blobs, sets, nulls, and strings map to `Field::DB_TYPE_STRING`.
-   **Integer:** Tiny, short, long, int24, longlong, and enums map to `Field::DB_TYPE_INTEGER`.
-   **Float:** Decimal, float, and double map to `Field::DB_TYPE_FLOAT`.
-   **Unknown:** Defaults to `Field::DB_TYPE_UNKNOWN`.

## Cross-Unit Boundaries

### Calls Out
-   **`QueryResult/QueryResult`**: Base class constructor called during initialization.
-   **`Field/Field`**: Default constructor called implicitly when allocating `mCurrentRow`.
-   **`Field/SetType`**: Called in the constructor to assign data types to fields.
-   **`Field/SetValue`**: Called in `NextRow` to populate field data.
-   **`Errors/PrintStacktraceAndThrow`**: Listed in the MAP; while not explicitly called in the visible source, the `MANGOS_ASSERT` macro may trigger error handling mechanisms defined in the `Errors` unit depending on build configuration.

### Called By
-   **`DatabaseMysql/Query`**: Instantiates `QueryResultMysql` for standard SQL queries.
-   **`DatabaseMysql/QueryNamed`**: Instantiates `QueryResultMysql` for named parameterized queries.

## Data Model

This unit does not interact with specific database tables. It operates on generic result sets from any query.

## Notable Implementation Details

-   **Conditional Compilation:** Excluded from builds with PostgreSQL support (`#ifndef DO_POSTGRESQL`).
-   **Manual Memory Management:** Uses raw pointers and `new[]`/`delete[]`. `EndQuery` ensures both the C++ array and MySQL resource are freed.
-   **Date/Time as Strings:** All temporal types are mapped to `DB_TYPE_STRING`, implying parsing occurs at the `Field` level or by the caller.
-   **Early Cleanup:** `NextRow` calls `EndQuery` immediately upon reaching the end of results, freeing memory promptly rather than waiting for destruction.
-   **Fatal Allocation Failure:** The constructor asserts on `mCurrentRow` allocation failure, treating it as a critical error.

## Member Reference

**QueryResultMysql**
Constructor initializing the MySQL result wrapper. Allocates the `Field` array and sets types via `ConvertNativeType`. Calls `QueryResult/QueryResult`, `Field/Field`, and `Field/SetType`.

**~QueryResultMysql**
Destructor that calls `EndQuery` to clean up resources.

**NextRow**
Advances to the next row. Fetches via `mysql_fetch_row`, updates fields via `Field/SetValue`, and returns `true`. Calls `EndQuery` and returns `false` if no more rows exist.

**EndQuery**
Private method freeing the `Field` array and MySQL result set via `mysql_free_result`. Nullifies pointers to prevent double-freeing.

**ConvertNativeType**
Private helper mapping MySQL `enum_field_types` to `Field::DataTypes`. Groups temporals as strings, integers as integers, and decimals/floats as floats.

---

<!-- machine-true, projected from graph.json -->

## Map — QueryResultMysql

*Source:* QueryResultMysql.cpp, QueryResultMysql.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| QueryResultMysql | ctor | Errors/PrintStacktraceAndThrow, Field/Field, Field/SetType, QueryResult/QueryResult | DatabaseMysql/Query, DatabaseMysql/QueryNamed | — |
| ~QueryResultMysql | dtor | — | — | — |
| NextRow | method | Field/SetValue | DatabaseMysql/Query, DatabaseMysql/QueryNamed | — |
| EndQuery | method | — | — | — |
| ConvertNativeType | method | — | — | — |
