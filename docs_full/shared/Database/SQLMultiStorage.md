# SQLMultiStorage

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# SQLMultiStorage

## Purpose & Responsibilities

`SQLMultiStorage` is a specialized in-memory data container within the `wowvmangos` server framework, designed to hold database records where multiple rows share the same unique identifier (key). It inherits from `SQLStorageBase`, providing a common interface for loading, storing, and iterating over structured data retrieved from SQL tables.

Unlike its sibling classes `SQLStorage` (which assumes a 1:1 mapping between ID and record) and `SQLHashStorage` (which uses a hash map for sparse IDs), `SQLMultiStorage` utilizes a `std::multimap` to store records. This allows the server to efficiently retrieve all records associated with a specific key, such as multiple spell effects for a single spell ID or multiple loot entries for a single creature. The class manages raw memory buffers (`char*`) for the actual data, while the multimap stores pointers to these buffers indexed by their record ID.

## Member-by-Member Behavior

The `SQLMultiStorage` class exposes two primary behaviors relevant to external consumers and internal lifecycle management: destruction and record insertion during loading.

### Lifecycle Management
*   **~SQLMultiStorage**: The destructor overrides the base class destructor to ensure proper cleanup. It calls `Free()` (defined in `SQLStorageBase` and overridden in `SQLMultiStorage`) to release the allocated memory for the data buffer and clear the internal multimap index. This prevents memory leaks when the storage object goes out of scope or is explicitly deleted.

### Data Loading Integration
*   **JustCreatedRecord**: This protected virtual method is a callback invoked by the loader infrastructure (`SQLStorageLoaderBase`) immediately after a new record's memory buffer has been allocated and populated with data from the database. In `SQLMultiStorage`, this method inserts the newly created record pointer into the `m_indexMultiMap` using the provided `recordId` as the key. Because the underlying container is a `std::multimap`, this operation supports inserting multiple records with identical keys without overwriting previous entries.

## Cross-Unit Boundaries

`SQLMultiStorage` interacts primarily with the storage loading infrastructure and the base storage class.

*   **Calls Out**:
    *   **None**: The members listed in the MAP (`~SQLMultiStorage` and `JustCreatedRecord`) do not directly call functions in other units. They rely on standard library containers (`std::multimap`) and base class methods (`Free`).
    *   *Note*: The `Load()` and `EraseEntry()` methods (not in the MAP for this specific partial but present in the class definition) would interact with `SQLStorageLoaderBase` and `SQLStorageBase`, but these are not part of the documented members for this unit.

*   **Called By**:
    *   **SQLStorageLoaderBase**: Specifically, the `storeValue` and related conversion methods within `SQLStorageLoaderBase` (defined in `SQLStorageImpl.h`, included via `#include "SQLStorageImpl.h"`) invoke `JustCreatedRecord`. The loader allocates a chunk of memory for a record, fills it with converted field data, and then calls `JustCreatedRecord` to register the record in the storage's index. This decouples the parsing/conversion logic from the indexing strategy.
    *   **Derived Classes**: While not shown in the MAP, any class inheriting from `SQLMultiStorage` might override `JustCreatedRecord` to perform additional post-processing, though the default implementation is typically sufficient for simple indexing.

## Data Model

`SQLMultiStorage` itself does not define specific database tables. It is a generic container used by various parts of the server to load different tables. The actual table structure is determined by the format strings (`src_fmt`, `dst_fmt`) passed to the constructor and the SQL query executed by the loader.

However, the design implies that the tables it loads have:
1.  A primary key or identifier column (referenced by `_entry_field` in the constructor).
2.  Multiple rows can share the same value in this identifier column.
3.  Fixed-width fields, as the storage uses raw byte offsets (`recordSize`) to navigate the data buffer.

Examples of such tables in a World of Warcraft emulator context might include:
*   `spell_linked_spell`: Where one spell ID can trigger multiple other spells.
*   `creature_loot_template`: Where one creature ID can drop multiple items.
*   `item_enchantment_template`: Where one item ID can have multiple possible enchantments.

The class does not enforce any specific schema; it relies on the caller to provide the correct format strings and SQL queries.

## Notable Implementation Details

1.  **Raw Memory Management**: `SQLMultiStorage` stores data in a contiguous block of `char*` memory (`m_data`, inherited from `SQLStorageBase`). Records are accessed via pointers into this block. The `m_indexMultiMap` stores `char*` pointers, not copies of the data. This makes lookups fast but requires careful lifetime management. The `Free()` method must delete the `m_data` block and clear the map to avoid dangling pointers.

2.  **Multimap Iteration**: The class provides custom iterator types (`SQLMultiSIterator` and `SQLMSIteratorBounds`) to allow range-based iteration over records with the same key. `getBounds(key)` returns a pair of iterators defining the range `[first, second)` of all records with the specified `key`. This is crucial for efficiently processing all entries associated with a single ID without scanning the entire map.

3.  **Template Friendships**: The class grants friendship to `SQLMultiSIterator` and `SQLMSIteratorBounds` templates. This allows these iterator classes to access the private `m_indexMultiMap` member, enabling them to traverse the multimap correctly. This is a common pattern for implementing custom iterators that need access to internal container structures.

4.  **No Dynamic Resizing**: The storage size is fixed at load time. The `prepareToLoad` method (inherited from `SQLStorageBase`) allocates the `m_data` buffer based on the expected number of records and record size. If the actual number of records exceeds this estimate, it could lead to buffer overflows, although the loader typically counts records before allocation.

5.  **Key Type**: The key type is hardcoded to `uint32`. This limits the storage to identifiers that fit within a 32-bit unsigned integer, which is consistent with most World of Warcraft database IDs.

## Member Reference

**~SQLMultiStorage**
The destructor for `SQLMultiStorage`. It overrides the base class destructor to ensure that the `Free()` method is called, releasing the allocated memory for the data buffer and clearing the internal multimap index. This prevents memory leaks when the storage object is destroyed.

**JustCreatedRecord**
A protected virtual method called by the loader infrastructure (`SQLStorageLoaderBase`) after a new record's memory buffer has been allocated and populated. It inserts the record pointer into the `m_indexMultiMap` using the provided `recordId` as the key. This allows multiple records with the same ID to be stored and retrieved efficiently.

---

<!-- machine-true, projected from graph.json -->

## Map — SQLMultiStorage

*Source:* SQLStorage.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ~SQLMultiStorage | dtor | — | — | — |
| JustCreatedRecord | method | — | — | — |
