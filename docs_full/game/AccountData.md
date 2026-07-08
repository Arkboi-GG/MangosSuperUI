<!-- provenance: verbose -->
# AccountData

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AccountData

**Purpose & Responsibilities**

The `AccountData` unit defines the data structures and conversion logic required to manage client-side account data caches within the WoWVMaNGOS server. It addresses a breaking change in the client-server protocol between World of Warcraft patch 1.8 (and earlier) and patch 1.9 (and later).

In older clients (1.8), account data was categorized into five types. In newer clients (1.9+), this was expanded to eight types, splitting global/per-character distinctions for bindings, macros, and configurations more granularly. This unit provides:
1.  **Enum Definitions**: Distinct enumerations for `OldAccountData` (1.8) and `NewAccountData` (1.9+) types, including bitmask constants for global vs. per-character data.
2.  **Conversion Functions**: Inline functions to translate indices between the old and new schemas, ensuring backward compatibility for legacy clients or internal consistency during migration.
3.  **Data Structure**: A simple `AccountData` struct to hold the binary cache payload and its associated timestamp.

This unit contains no database interactions, network I/O, or complex state management. It is purely a definition and utility layer for protocol compatibility.

## Member-by-Member Behavior

### Conversion Logic

The core responsibility of this unit is bridging the gap between two different client versions' expectations of account data types.

**`ConvertOldAccountDataToNew`**
This inline function takes a `uint32` index representing an `OldAccountData::AccountDataType` and returns the corresponding `NewAccountData::AccountDataType`.
*   **Mapping**:
    *   `GLOBAL_CONFIG_CACHE` (0) maps to `GLOBAL_CONFIG_CACHE` (0).
    *   `GLOBAL_BINDINGS_CACHE` (1) maps to `GLOBAL_BINDINGS_CACHE` (2).
    *   `GLOBAL_MACROS_CACHE` (2) maps to `GLOBAL_MACROS_CACHE` (4).
    *   `PER_CHARACTER_LAYOUT_CACHE` (3) maps to `PER_CHARACTER_LAYOUT_CACHE` (6).
    *   `PER_CHARACTER_CHAT_CACHE` (4) maps to `PER_CHARACTER_CHAT_CACHE` (7).
*   **Fallback**: If the input index does not match any of the known old types, it returns `NewAccountData::NUM_ACCOUNT_DATA_TYPES` (8), effectively signaling an invalid or unmapped type.

**`ConvertNewAccountDataToOld`**
This inline function performs the reverse operation, taking a `uint32` index representing a `NewAccountData::AccountDataType` and returning the corresponding `OldAccountData::AccountDataType`.
*   **Mapping**:
    *   `GLOBAL_CONFIG_CACHE` (0) maps to `GLOBAL_CONFIG_CACHE` (0).
    *   `GLOBAL_BINDINGS_CACHE` (2) maps to `GLOBAL_BINDINGS_CACHE` (1).
    *   `GLOBAL_MACROS_CACHE` (4) maps to `GLOBAL_MACROS_CACHE` (2).
    *   `PER_CHARACTER_LAYOUT_CACHE` (6) maps to `PER_CHARACTER_LAYOUT_CACHE` (3).
    *   `PER_CHARACTER_CHAT_CACHE` (7) maps to `PER_CHARACTER_CHAT_CACHE` (4).
*   **Fallback**: If the input index does not match any of the known new types (e.g., `PER_CHARACTER_CONFIG_CACHE`, `PER_CHARACTER_BINDINGS_CACHE`, `PER_CHARACTER_MACROS_CACHE`), it returns `OldAccountData::NUM_ACCOUNT_DATA_TYPES` (5). This indicates that these newer, more granular types have no direct equivalent in the old schema and are treated as unmapped.

### Data Structure

**`AccountData`**
A simple aggregate struct used to store cached account data.
*   **Members**:
    *   `timestamp`: A `time_t` value indicating when the data was last updated or created. Initialized to `0` in the constructor.
    *   `data`: A `std::string` holding the raw binary or serialized content of the account data cache. Initialized to an empty string in the constructor.
*   **Constructor**: The default constructor initializes both fields to safe default states (`0` and `""`).

## Cross-Unit Boundaries

This unit is passive; it does not initiate calls to other units. It is consumed by the `WorldSession` class to handle account data requests and updates.

*   **Called by `WorldSession.MiscHandler/HandleRequestAccountData`**:
    When a client requests account data, `WorldSession` uses `ConvertOldAccountDataToNew` to ensure that internal storage or processing logic (which likely uses the newer schema) can correctly interpret requests coming from older clients or to standardize the data type before retrieval.

*   **Called by `WorldSession.MiscHandler/HandleUpdateAccountData`**:
    When a client sends updated account data, `WorldSession` uses `ConvertOldAccountDataToNew` to translate the incoming data type index into the modern schema before storing or processing the update.

*   **Called by `WorldSession.Main/SendAccountDataTimes`**:
    When sending timestamps for account data to the client, `WorldSession` uses `ConvertNewAccountDataToOld` to translate the internal modern schema indices back into the format expected by the specific client version connected to that session. This ensures that a 1.8 client receives valid type indices, while a 1.9+ client receives the correct modern indices.

*   **Called by `WorldSession.Main/LoadAccountData`**:
    This member constructs `AccountData` objects, likely populating them with data retrieved from the database or memory cache, to be sent to the client or stored in the session state.

## Data Model

This unit does not interact directly with any database tables. It operates entirely in memory, providing structures and conversion logic for data that is persisted elsewhere (likely in the `account_data` table or similar, managed by `WorldSession` or a dedicated data manager). No SQL queries or schema references are present in this source file.

## Notable Implementation Details

1.  **Asymmetric Mapping**: The conversion between old and new schemas is not bijective. The new schema has 8 types, while the old has 5. The three additional types in the new schema (`PER_CHARACTER_CONFIG_CACHE`, `PER_CHARACTER_BINDINGS_CACHE`, `PER_CHARACTER_MACROS_CACHE`) have no counterparts in the old schema. Consequently, `ConvertNewAccountDataToOld` will return an invalid index (`NUM_ACCOUNT_DATA_TYPES`) for these three types. This implies that if a server stores data in these newer categories, it cannot be meaningfully represented to an old client via this conversion logic. The calling code in `WorldSession` must handle this mismatch appropriately (e.g., by ignoring these types for old clients or merging them into broader categories).

2.  **Inline Functions**: Both conversion functions are defined as `inline` in the header. This suggests they are performance-critical or simply small enough to warrant inlining to avoid call overhead, though given the simple switch-case logic, the compiler would likely inline them automatically anyway.

3.  **Bitmask Constants**: The `GLOBAL_CACHE_MASK` and `PER_CHARACTER_CACHE_MASK` constants are defined for both old and new schemas. These masks likely correspond to bit flags used in the client-server protocol to indicate which types of data are being requested or updated in bulk. The values differ between old and new schemas (`0xD` vs `0x15` for global, `0x30` vs `0xEA` for per-character), reflecting the changed bit positions of the data types in the protocol.

4.  **No Validation**: The conversion functions do not validate the input range beyond the explicit switch cases. An out-of-range input simply falls through to the default return value. Callers must ensure they pass valid indices.

## Member Reference

**`ConvertOldAccountDataToNew`**
An inline function that converts an `OldAccountData::AccountDataType` index to its corresponding `NewAccountData::AccountDataType` index. It handles the five types present in the old schema, mapping them to their equivalents in the new schema (with index shifts). Returns `NewAccountData::NUM_ACCOUNT_DATA_TYPES` for unmapped inputs. Called by `WorldSession.MiscHandler/HandleRequestAccountData` and `WorldSession.MiscHandler/HandleUpdateAccountData`.

**`ConvertNewAccountDataToOld`**
An inline function that converts a `NewAccountData::AccountDataType` index to its corresponding `OldAccountData::AccountDataType` index. It handles the five types from the new schema that have direct equivalents in the old schema. Returns `OldAccountData::NUM_ACCOUNT_DATA_TYPES` for the three new-specific types (`PER_CHARACTER_CONFIG_CACHE`, `PER_CHARACTER_BINDINGS_CACHE`, `PER_CHARACTER_MACROS_CACHE`) and any other unmapped inputs. Called by `WorldSession.Main/SendAccountDataTimes`.

**`AccountData`**
A struct containing a `time_t timestamp` and a `std::string data`. Used to hold cached account data payloads. The default constructor initializes `timestamp` to 0 and `data` to an empty string. Constructed by `WorldSession.Main/LoadAccountData`.

---

<!-- machine-true, projected from graph.json -->

## Map — AccountData

*Source:* AccountData.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ConvertOldAccountDataToNew | function | — | WorldSession.MiscHandler/HandleRequestAccountData, WorldSession.MiscHandler/HandleUpdateAccountData | — |
| ConvertNewAccountDataToOld | function | — | WorldSession.Main/SendAccountDataTimes | — |
| AccountData | ctor | — | WorldSession.Main/LoadAccountData | — |
