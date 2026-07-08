<!-- provenance: verbose -->
# QueryNamedResult

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# QueryNamedResult

**QueryNamedResult** is a thin wrapper around the abstract `QueryResult` interface that enables accessing database result fields by name rather than by integer index. It holds ownership of a `QueryResult` instance via `std::unique_ptr` and maintains a parallel vector of field names (`QueryFieldNames`). This allows callers to retrieve `Field` objects using string keys (e.g., `result["id"]`) while delegating row iteration and memory management to the wrapped `QueryResult`.

The class provides a convenience layer for query handling code where field positions are less meaningful than semantic names. It does not perform I/O; all data retrieval is proxied to the owned `QueryResult`.

## Member-by-Member Behavior

### Construction and Ownership
*   **`QueryNamedResult`**: Takes ownership of a `QueryResult` pointer and a `QueryFieldNames` vector, moving them into internal members `mQuery` and `mFieldNames`. It assumes the name vector size matches the result's field count.

### Row Iteration and Access
*   **`NextRow`**: Delegates to `mQuery->NextRow()` to advance the cursor.
*   **`Fetch`**: Delegates to `mQuery->Fetch()`, returning a pointer to the current row's `Field` array.
*   **`operator[](int)`**: Provides backward compatibility with `QueryResult` by allowing integer-indexed access via the underlying query.

### Named Field Access
*   **`operator[](std::string const&)`**: Retrieves the current row via `Fetch()`, determines the integer index of the requested field name using `GetField_idx()`, and returns a reference to the corresponding `Field`. Throws `std::runtime_error` if the name is not found.
*   **`GetField_idx`**: Performs a linear search through `mFieldNames` to find the index matching the provided name. Throws `std::runtime_error("unknown field name")` if no match is found. Contains unreachable dead code (`return uint32(-1)`).
*   **`GetFieldNames`**: Returns a constant reference to the internal `mFieldNames` vector.

### Metadata
*   **`GetFieldCount`**: Delegates to `mQuery->GetFieldCount()`.
*   **`GetRowCount`**: Delegates to `mQuery->GetRowCount()`.

## Cross-Unit Boundaries

*   **Calls into `QueryResult`**: Every public method except `GetFieldNames` and `GetField_idx` delegates to the owned `QueryResult` instance (`mQuery`). This includes `NextRow`, `Fetch`, `GetFieldCount`, `GetRowCount`, and `operator[]`. The collaboration is strictly proxy-based: `QueryNamedResult` adds the naming layer but relies entirely on `QueryResult` for data storage and row navigation.
*   **Called by other units**: Typically instantiated by database abstraction layers that execute SQL queries and populate field names. Callers use this object to parse results safely by name.

## Data Model

This unit does not interact with database tables directly. It operates on in-memory result sets provided by the `QueryResult` interface. The `QueryFieldNames` vector contains strings corresponding to column names from whatever table was queried upstream, but `QueryNamedResult` is agnostic to the schema.

## Notable Implementation Details

1.  **Linear Search Overhead**: `GetField_idx` performs an `O(N)` linear scan of field names for every named access. For queries with many columns or frequent named accesses, this is slower than hash-map lookup.
2.  **Exception Safety**: `operator[](std::string const&)` throws `std::runtime_error` on invalid field names. Callers must handle this or ensure correctness.
3.  **Dead Code**: `GetField_idx` contains unreachable `return uint32(-1)` after the `throw` statement.
4.  **No Validation**: The constructor assumes the `names` vector size matches `mQuery->GetFieldCount()`. Mismatches lead to undefined behavior in accessors.

## Member Reference

**QueryNamedResult**  
Constructor taking ownership of a `QueryResult` and field names vector. Initializes `mQuery` and `mFieldNames` via move semantics.

**NextRow**  
Delegates to `mQuery->NextRow()` to advance to the next row. Returns `true` if successful.

**Fetch**  
Delegates to `mQuery->Fetch()` to return a pointer to the current row's `Field` array.

**GetFieldCount**  
Delegates to `mQuery->GetFieldCount()` to return the number of columns.

**GetRowCount**  
Delegates to `mQuery->GetRowCount()` to return the total number of rows.

**operator[]#2**  
Integer-indexed access. Dereferences `mQuery` and applies the index operator to retrieve the `Field` at the specified position.

**operator[]**  
String-keyed access. Retrieves the current row via `Fetch()`, finds the index of the field name using `GetField_idx()`, and returns a const reference to the corresponding `Field`. Throws `std::runtime_error` if the name is not found.

**GetFieldNames**  
Returns a const reference to the internal `mFieldNames` vector, exposing the list of available field names.

**GetField_idx**  
Performs a linear search through `mFieldNames` to find the index of the given field name. Throws `std::runtime_error` if not found. Contains unreachable dead code.

---

<!-- machine-true, projected from graph.json -->

## Map — QueryNamedResult

*Source:* QueryResult.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| QueryNamedResult | ctor | — | — | — |
| NextRow | method | — | — | — |
| Fetch | method | — | — | — |
| GetFieldCount | method | — | — | — |
| GetRowCount | method | — | — | — |
| operator[]#2 | method | — | — | — |
| operator[] | method | — | — | — |
| GetFieldNames | method | — | — | — |
| GetField_idx | method | — | — | — |
