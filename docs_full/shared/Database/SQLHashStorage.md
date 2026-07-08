# SQLHashStorage

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# SQLHashStorage

**Purpose & Responsibilities**

`SQLHashStorage` is a specialized in-memory data container within the `wowvmangos` server framework, designed to hold records loaded from database tables where the primary key (entry ID) is sparse or non-contiguous. It inherits from `SQLStorageBase`, sharing the core memory management and iteration infrastructure, but distinguishes itself by using a hash map (`std::unordered_map`) for O(1) average-time complexity lookups by record ID.

Unlike its sibling `SQLStorage` (which uses a direct-access array suitable for dense, contiguous IDs like creature entries) or `SQLMultiStorage` (which supports multiple records per ID), `SQLHashStorage` assumes a one-to-one mapping between a unique `uint32` ID and a record structure. It is typically used for game data tables where IDs are large, irregular, or have significant gaps, making array-based indexing prohibitively memory-intensive.

The class manages the lifecycle of raw binary record data: allocating a contiguous block of memory for all records, parsing database results into this block via format strings, and maintaining an index map that allows rapid retrieval of any specific record by its ID.

## Member-by-Member Behavior

### Construction and Initialization

**`SQLHashStorage(char const* fmt, char const* _entry_field, char const* sqlname)`**
Constructs a storage instance using a single format string for both source (database) and destination (memory) layouts. It initializes the base class with the table name, the name of the entry field (used to identify the primary key in SQL queries), and the format string. This constructor is used when the database schema matches the in-memory struct layout exactly.

**`SQLHashStorage(char const* src_fmt, char const* dst_fmt, char const* _entry_field, char const* sqlname)`**
Constructs a storage instance with separate format strings for source and destination. This allows for data transformation during loading, such as skipping certain database columns or reordering fields in memory. The `src_fmt` describes the database result set, while `dst_fmt` describes the target C++ struct layout.

### Data Loading and Indexing

**`Load()`**
Initiates the loading process from the database. This method triggers the query execution and delegates the actual parsing and storage to the associated loader mechanism (`SQLHashStorageLoader`). It ensures that the internal memory buffer is prepared and that the `m_indexMap` is populated with pointers to the newly created records. If the table is empty, it may log a warning depending on configuration, though `SQLHashStorage` does not expose an `error_at_empty` parameter in its public interface like `SQLStorage` does.

**`JustCreatedRecord(uint32 recordId, char* record)`**
A protected virtual method overridden from `SQLStorageBase`. This hook is called by the loader immediately after a new record's memory space has been allocated within the contiguous data buffer. `SQLHashStorage` uses this opportunity to insert the record into its hash map: `m_indexMap[recordId] = record`. This establishes the link between the logical ID and the physical memory location, enabling fast lookups.

**`prepareToLoad(uint32 maxRecordId, uint32 recordCount, uint32 recordSize)`**
A protected virtual method overridden from `SQLStorageBase`. This method prepares the internal data structures before any records are parsed. For `SQLHashStorage`, this involves clearing any existing data (via `Free()`) and reserving space in the `m_indexMap` if necessary, although the primary allocation of the contiguous data buffer is handled by the base class or the loader's interaction with `createRecord`. It ensures the storage is in a clean state for the incoming data.

### Lookup and Access

**`LookupEntry(uint32 id)`**
A template method that retrieves a record by its ID. It performs a lookup in `m_indexMap`. If the ID exists, it returns a pointer to the record, cast to the template type `T` (the user-defined struct representing the row). If the ID is not found, it returns `nullptr`. This is the primary interface for accessing data stored in this container. The use of `reinterpret_cast` assumes the caller knows the correct struct type corresponding to the format string used during construction.

### Modification and Cleanup

**`EraseEntry(uint32 id)`**
Removes a specific record from the storage. It locates the record in `m_indexMap` and removes the entry. Note that this operation only removes the index entry; it does not necessarily shrink the contiguous memory buffer or shift subsequent records. The memory for the erased record remains allocated until the entire storage is freed or reloaded. This method is useful for dynamic updates or testing scenarios where specific entries need to be invalidated.

**`~SQLHashStorage()`**
The destructor. It calls `Free()` to release the contiguous memory buffer holding the record data and clears the `m_indexMap`. This ensures no memory leaks occur when the storage object goes out of scope.

**`Free()`**
A protected virtual method overridden from `SQLStorageBase`. It releases the raw memory block (`m_data`) allocated for the records and clears the `m_indexMap`. This method is called explicitly by the destructor and implicitly by `prepareToLoad` to reset the storage state.

## Cross-Unit Boundaries

`SQLHashStorage` is tightly coupled with the `SQLStorageLoaderBase` and its derived class `SQLHashStorageLoader`.

*   **Calls Out:** `SQLHashStorage` itself does not directly execute SQL queries. Instead, its `Load()` method relies on the `SQLHashStorageLoader` (instantiated elsewhere, typically in the specific table loader classes) to perform the database interaction. The loader calls back into `SQLHashStorage`'s protected methods (`prepareToLoad`, `JustCreatedRecord`) to manage the internal state.
*   **Called By:** Specific table loader classes (e.g., `CreatureTemplateLoader`, `ItemTemplateLoader`, etc., though these are not listed in the MAP as they are distinct units) instantiate `SQLHashStorage` objects and invoke their `Load()` method. These loaders also define the struct types `T` used with `LookupEntry`.
*   **Collaboration:** The `SQLStorageLoaderBase` template class handles the low-level parsing of database fields according to the format strings. It calls `storeValue` methods to write data into the raw `char*` buffer provided by `SQLHashStorage`. `SQLHashStorage` provides the memory layout and the indexing mechanism, while the loader provides the data extraction logic.

## Data Model

`SQLHashStorage` is agnostic to specific database tables. It operates on any table specified by the `sqlname` parameter during construction. The schema of the table is described indirectly by the `src_fmt` and `dst_fmt` strings, which define the column types and order. Common tables using this storage type include those with sparse IDs, such as `creature_template_addon`, `item_template_locale`, or various spell-related tables. The class does not enforce any specific column names or types beyond what is encoded in the format strings.

## Notable Implementation Details

*   **Memory Layout:** Records are stored in a single contiguous `char*` buffer (`m_data` inherited from `SQLStorageBase`). This improves cache locality for sequential iteration but means that erasing a single record (`EraseEntry`) does not compact the memory. The gap remains, and the ID is simply removed from the hash map.
*   **Type Safety:** The `LookupEntry` method uses `reinterpret_cast`, which bypasses C++ type safety. The correctness of the returned pointer depends entirely on the caller providing the correct template argument `T` that matches the `dst_fmt` used during construction. Mismatched types will lead to undefined behavior.
*   **Concurrency:** The class does not appear to implement any thread-safety mechanisms. Access to `m_indexMap` and `m_data` should be synchronized externally if accessed from multiple threads, particularly during loading or modification.
*   **Hash Map Choice:** The use of `std::unordered_map` provides average O(1) lookup time, which is efficient for random access. However, it has higher memory overhead per entry compared to the array-based `SQLStorage` and does not support ordered iteration by ID. Iteration over the storage (using `begin()`/`end()` from the base class) traverses the contiguous memory buffer in insertion order, not ID order.

## Member Reference

**~SQLHashStorage**
Destructor that calls `Free()` to release the contiguous memory buffer and clear the index map, ensuring proper cleanup of resources.

**JustCreatedRecord**
Protected virtual method overridden from `SQLStorageBase`. Called by the loader after a record's memory is allocated. It inserts the record pointer into `m_indexMap` keyed by `recordId`, establishing the lookup index.

---

<!-- machine-true, projected from graph.json -->

## Map — SQLHashStorage

*Source:* SQLStorage.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ~SQLHashStorage | dtor | — | — | — |
| JustCreatedRecord | method | — | — | — |
