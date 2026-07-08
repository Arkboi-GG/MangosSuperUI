<!-- provenance: verbose -->
# DBCStore

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`DBCStorage<T>` is a template class managing the lifecycle and access of World of Warcraft DBC (Data Block Chunk) binary files. It abstracts low-level parsing by delegating to `DBCFileLoader` while providing an interface for O(1) record lookup by ID.

The class maintains two primary structures:
1.  **`m_dataTable`**: A contiguous memory block of raw binary records, cast to template type `T`.
2.  **`indexTable`**: An array of pointers (`T**`) enabling direct access to records by numeric ID.

It also manages a `StringPoolList` (`m_stringPoolList`) holding extracted string data, supporting multiple locales by appending additional string buffers via `LoadStringsFrom`.

## Member-by-Member Behavior

### Initialization and Lifecycle
*   **`DBCStorage<T>` (Constructor)**: Initializes with format string `f`. Sets `nCount` and `fieldCount` to 0, and internal pointers to `nullptr`.
*   **`~DBCStorage<T>` (Destructor)**: Calls `Clear()` to free all allocated memory.

### Data Access
*   **`LookupEntry`**: Returns a pointer to the record with the given `id`. Performs a bounds check against `nCount`; returns `nullptr` if `id` is out of range or if the entry was erased.
*   **`InsertEntry`**: Manually inserts pointer `data` into `indexTable` at `id`. Returns `false` if `id >= nCount`; otherwise, updates the index and returns `true`.
*   **`EraseEntry`**: Sets `indexTable[id]` to `nullptr`, logically removing the entry from lookups without freeing the underlying memory in `m_dataTable`.

### Metadata
*   **`GetNumRows`**: Returns `nCount`, the total number of records.
*   **`GetFormat`**: Returns the format string `fmt`.
*   **`GetFieldCount`**: Returns `fieldCount`, the number of columns per record.

### Loading and Memory Management
*   **`Load`**: Populates storage from DBC file `fn`. Uses `DBCFileLoader` to parse the file, allocate `m_dataTable` and `indexTable` via `AutoProduceData`, and extract strings via `AutoProduceStrings`. Returns `false` if loading fails or `indexTable` is `nullptr`.
*   **`LoadStringsFrom`**: Loads string data from a locale-specific DBC file `fn` into the existing structure. Requires `Load` to have been called previously. Appends the new string buffer to `m_stringPoolList`. Returns `false` if `indexTable` is null or loading fails.
*   **`Clear`**: Frees `indexTable`, `m_dataTable`, and all buffers in `m_stringPoolList`. Resets `nCount` to 0.

## Cross-Unit Boundaries

*   **Calls `DBCFileLoader`** (`DBCFileLoader.cpp`):
    *   **Context**: Used in `Load` and `LoadStringsFrom`.
    *   **Collaboration**: `DBCStorage` delegates binary parsing to `DBCFileLoader`. It calls `DBCFileLoader::Load` to read the file, `DBCFileLoader::GetCols` to get the column count, `DBCFileLoader::AutoProduceData` to allocate and populate the data/index tables, and `DBCFileLoader::AutoProduceStrings` to extract string buffers.

## Data Model

This unit does not interact with SQL database tables. It operates exclusively on binary DBC files. The schema is defined by the format string passed to the constructor.

## Notable Implementation Details

1.  **Direct Memory Casting**: `Load` casts the raw data pointer returned by `DBCFileLoader::AutoProduceData` directly to `T*`. The template type `T` must exactly match the binary layout defined by the format string; mismatches cause undefined behavior.
2.  **String Pooling**: Strings are stored in separate buffers in `m_stringPoolList`, not inline in `T`. `LoadStringsFrom` appends new buffers for different locales. The logic for selecting the active locale buffer is external to this class.
3.  **Logical Deletion**: `EraseEntry` only nullifies the index pointer. The underlying memory in `m_dataTable` remains allocated until `Clear` is called.
4.  **No Thread Safety**: The class lacks synchronization primitives. It assumes single-threaded access during initialization and read-only access during runtime.

## Member Reference

**DBCStorage<T>**
Constructor initializing the storage with format string `f`. Sets `nCount` and `fieldCount` to 0, and pointers to `nullptr`.

**~DBCStorage<T>**
Destructor calling `Clear()` to release memory.

**LookupEntry**
Returns pointer to entry `id` if `id < nCount`; otherwise `nullptr`.

**InsertEntry**
Inserts `data` into `indexTable[id]`. Returns `false` if `id >= nCount`; else `true`.

**GetNumRows**
Returns `nCount`.

**GetFormat**
Returns `fmt`.

**GetFieldCount**
Returns `fieldCount`.

**Load**
Loads DBC file `fn` using `DBCFileLoader`. Allocates `m_dataTable`, `indexTable`, and string buffers. Returns `false` on failure or null index.

**LoadStringsFrom**
Loads strings from DBC file `fn` into existing storage. Appends buffer to `m_stringPoolList`. Returns `false` if `indexTable` is null or load fails.

**Clear**
Deletes `indexTable`, `m_dataTable`, and all string buffers. Resets `nCount` to 0.

**EraseEntry**
Sets `indexTable[id]` to `nullptr`.

---

<!-- machine-true, projected from graph.json -->

## Map — DBCStore

*Source:* DBCStore.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| DBCStorage<T> | ctor | — | — | — |
| ~DBCStorage<T> | dtor | — | — | — |
| LookupEntry | function | — | — | — |
| InsertEntry | function | — | — | — |
| GetNumRows | function | — | — | — |
| GetFormat | function | — | — | — |
| GetFieldCount | function | — | — | — |
| Load | function | — | — | — |
| LoadStringsFrom | function | — | — | — |
| Clear | function | — | — | — |
| EraseEntry | function | — | — | — |
