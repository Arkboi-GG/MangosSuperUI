# Record

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# DBCFileLoader::Record

## Purpose & Responsibilities

`DBCFileLoader::Record` is a lightweight, non-owning view object that provides typed access to a single row within a loaded World of Warcraft DBC (Data Block Chunk) file. It acts as a cursor or handle, holding a pointer to the raw memory offset of a specific record and a reference to the parent `DBCFileLoader` instance, which manages the global file metadata (such as field offsets and the string table).

The primary responsibility of `Record` is to abstract the binary layout of DBC files. Instead of requiring callers to manually calculate byte offsets and handle endianness conversion, `Record` exposes methods like `getFloat`, `getUInt`, and `getString` that perform these low-level operations safely and consistently. It ensures that data is interpreted according to the specific type expected by the caller, while relying on the parent loader for structural information.

## Member-by-Member Behavior

The members of `Record` are grouped by their role in data extraction:

### Construction
*   **`Record`**: The constructor is private, meaning `Record` instances cannot be created directly by external code. It is initialized with a reference to the owning `DBCFileLoader` and a pointer (`unsigned char*`) to the start of the record's data in memory. This design enforces that all records are derived from a valid, loaded DBC file context.

### Data Accessors
These methods retrieve values from specific fields within the record. Each takes a `size_t` field index, asserts that the index is within bounds, calculates the absolute memory address using the parent loader's offset table, reads the raw bytes, and returns the value in the appropriate native type.

*   **`getFloat`**: Retrieves a 32-bit floating-point value. It casts the memory at the calculated offset to a `float*`, dereferences it, performs endianness conversion via `EndianConvert` (from `Utilities/ByteConverter.h`), and returns the result. This is critical because DBC files are typically big-endian, while many host systems are little-endian.
*   **`getUInt`**: Retrieves a 32-bit unsigned integer. Similar to `getFloat`, it casts to `uint32*`, dereferences, applies `EndianConvert`, and returns the value.
*   **`getUInt8`**: Retrieves an 8-bit unsigned integer. Since single bytes do not have endianness issues, it simply casts to `uint8*` and dereferences the value. No conversion is performed.
*   **`getString`**: Retrieves a null-terminated C-string. DBC strings are stored as offsets into a separate string table at the end of the file. This method first calls `getUInt(field)` to read the offset value. It then asserts that this offset is within the bounds of the file's string table size. Finally, it returns a pointer to the character data located at `file.stringTable + stringOffset`. Note that this returns a `char const*` pointing directly into the loaded file memory; the caller must not free this pointer.

## Cross-Unit Boundaries

`Record` is tightly coupled with `DBCFileLoader` and relies on utility functions for byte manipulation.

*   **Called by `DBCFileLoader/AutoProduceData`**: The `AutoProduceData` method in `DBCFileLoader.cpp` iterates through records and uses `getFloat`, `getUInt`, and `getUInt8` to extract structured data based on a format string. This allows the loader to automatically populate complex data structures without manual field-by-field parsing for every DBC type.
*   **Called by `DBCFileLoader/AutoProduceStrings`**: Similarly, `AutoProduceStrings` uses `getString` to extract text fields from records, populating string-based data structures.
*   **Created by `DBCFileLoader/getRecord`**: The `getRecord` method in `DBCFileLoader.cpp` constructs `Record` objects. It calculates the memory offset for a given record ID and returns a `Record` instance initialized with that offset and a reference to the loader. This is the sole entry point for creating `Record` views.
*   **Depends on `Utilities/ByteConverter.h`**: The `getFloat` and `getUInt` methods call `EndianConvert`. This utility handles the byte-swapping required to interpret big-endian DBC data on little-endian architectures.

## Data Model

`DBCFileLoader::Record` does not interact with any database tables. It operates exclusively on in-memory binary data loaded from `.dbc` files.

## Notable Implementation Details

*   **Non-Owning Semantics**: `Record` holds a raw pointer (`offset`) and a reference (`file`). It does not own the memory it points to. If the `DBCFileLoader` is destroyed or its internal data buffer is freed, all `Record` instances become dangling pointers. Callers must ensure the lifetime of the `DBCFileLoader` exceeds that of any `Record` derived from it.
*   **Endianness Handling**: The explicit use of `EndianConvert` in `getFloat` and `getUInt` highlights that DBC files are platform-independent binary formats. The loader assumes the file is big-endian and converts to the host's native endianness. `getUInt8` skips this step, which is correct since single-byte values are endian-neutral.
*   **String Offset Indirection**: `getString` demonstrates the two-step process for accessing strings in DBC files: first reading an integer offset, then using that offset to index into the global string table. This indirection is a hallmark of the DBC format.
*   **Assertions for Safety**: All accessor methods use `assert` to check bounds (field index vs. `fieldCount`, string offset vs. `stringSize`). These checks protect against out-of-bounds memory access but are disabled in release builds (`NDEBUG`). In production, invalid indices could lead to undefined behavior or crashes.
*   **Private Constructor**: By making the constructor private and friending `DBCFileLoader`, the class enforces that records are always created through the loader's controlled interface (`getRecord`). This prevents accidental creation of invalid record views.

## Member Reference

**getFloat**  
Retrieves a 32-bit float from the specified field index. Asserts the field index is valid, casts the memory at the calculated offset to `float*`, dereferences it, applies endianness conversion via `EndianConvert`, and returns the value. Called by `DBCFileLoader/AutoProduceData`.

**getUInt**  
Retrieves a 32-bit unsigned integer from the specified field index. Asserts the field index is valid, casts the memory at the calculated offset to `uint32*`, dereferences it, applies endianness conversion via `EndianConvert`, and returns the value. Called by `DBCFileLoader/AutoProduceData` and internally by `getString`.

**getUInt8**  
Retrieves an 8-bit unsigned integer from the specified field index. Asserts the field index is valid, casts the memory at the calculated offset to `uint8*`, and dereferences it. No endianness conversion is performed. Called by `DBCFileLoader/AutoProduceData`.

**getString**  
Retrieves a null-terminated C-string from the specified field index. First calls `getUInt(field)` to obtain the string offset. Asserts the offset is within the bounds of the file's string table. Returns a pointer to the string data located at `file.stringTable + offset`. The returned pointer is valid only as long as the `DBCFileLoader` remains loaded. Called by `DBCFileLoader/AutoProduceStrings`.

**Record**  
Private constructor that initializes the record with a reference to the owning `DBCFileLoader` and a pointer to the start of the record's data in memory. Can only be called by `DBCFileLoader` (via friendship). Created by `DBCFileLoader/getRecord`.

---

<!-- machine-true, projected from graph.json -->

## Map — Record

*Source:* DBCFileLoader.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| getFloat | method | — | DBCFileLoader/AutoProduceData | — |
| getUInt | method | — | DBCFileLoader/AutoProduceData | — |
| getUInt8 | method | — | DBCFileLoader/AutoProduceData | — |
| getString | method | — | DBCFileLoader/AutoProduceStrings | — |
| Record | ctor | — | DBCFileLoader/getRecord | — |
