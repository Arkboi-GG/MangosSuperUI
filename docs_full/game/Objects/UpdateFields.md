# UpdateFields

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# UpdateFields

## Purpose & Responsibilities

The `UpdateFields` unit provides the static metadata and lookup utilities required to manage **Update Fields** for game objects in the WoWVMaNGOS server. In the context of this emulator, "Update Fields" are the specific memory offsets within a network packet that convey the state of an entity (Player, Creature, Item, GameObject, etc.) to the client. Because the layout, size, and visibility rules of these fields change significantly between different World of Warcraft client versions (from 1.2.4 through 1.12.1), this unit acts as a version-agnostic abstraction layer.

Its primary responsibilities are:
1.  **Version-Specific Data Inclusion:** It conditionally includes the correct set of field definitions (`g_updateFieldsData`) and constants based on the compiled `SUPPORTED_CLIENT_BUILD`.
2.  **Visibility Flag Resolution:** It pre-computes and exposes arrays of visibility flags for each object type, allowing the network serialization code to quickly determine which fields should be sent to which players (e.g., public vs. private vs. owner-only).
3.  **Field Lookup Utilities:** It provides functions to resolve field metadata by name or by offset/type mask, primarily supporting debugging tools and script validation.
4.  **Index Translation:** It offers a helper to translate hardcoded field indices from older database schemas or scripts into the current build's valid indices, ensuring backward compatibility for scripted behaviors.

This unit does not perform network I/O itself; rather, it supplies the data structures and lookups that `WorldObject` and related classes use to construct update packets.

## Member-by-Member Behavior

### Visibility Flag Management

**`GetUpdateFieldFlagsArray`**
This function returns a pointer to a pre-computed array of `uint16` flags corresponding to the update fields of a specific object type. The flags determine visibility rules (e.g., `UF_FLAG_PUBLIC`, `UF_FLAG_PRIVATE`).

*   **Logic:** It uses a `switch` statement on the `objectTypeId` (e.g., `TYPEID_ITEM`, `TYPEID_PLAYER`).
*   **Pre-computation:** The arrays returned are static globals (`g_containerUpdateFieldFlags`, `g_playerUpdateFieldFlags`, etc.) initialized at load time by the helper `SetupUpdateFieldFlagsArray`. This helper iterates over the global `g_updateFieldsData` (defined in the included version-specific `.cpp` file) and populates the flag array for each offset.
*   **Error Handling:** If an unsupported `objectTypeId` is passed, it logs an error via `Log.Main/Out` and returns `nullptr`.

### Field Metadata Lookups

**`GetUpdateFieldDataByName`**
Performs a linear search through the global `g_updateFieldsData` array to find an `UpdateFieldData` struct matching the provided C-string `name`. Returns a pointer to the found struct or `nullptr`. This is used by debug commands to allow administrators to query field values by human-readable names.

**`GetUpdateFieldDataByTypeMaskAndOffset`**
Finds an `UpdateFieldData` struct that matches both a specific `objectTypeMask` and a numeric `offset`.
*   **Logic:** It iterates through `g_updateFieldsData`. For each entry, it checks if the entry's `objectTypeMask` intersects with the requested mask. Then, it checks if the requested `offset` falls within the range `[itr.offset, itr.offset + itr.size)`.
*   **Usage:** This allows the system to identify the metadata (type, flags, name) of a field given only its raw position in the update packet structure.

### Index Translation

**`GetIndexOfUpdateFieldForCurrentBuild`**
Defined in the header as an inline function, this translates a "database index" (likely from a legacy schema or script definition) into the actual field index used by the current client build.
*   **Logic:** It uses a `switch` statement on common field names (e.g., `FIELD_GAMEOBJECT_FLAGS`, `UNIT_FIELD_FLAGS`). It maps these symbolic constants to the build-specific macros (e.g., `GAMEOBJECT_FLAGS`, `UNIT_FIELD_FLAGS`) defined in the included version-specific header.
*   **Fallback:** If the input index does not match any known legacy constant, it returns the input unchanged, assuming it is already a valid index for the current build.

## Cross-Unit Boundaries

### Calls Out

*   **`Log.Main/Out`**: Called by `GetUpdateFieldFlagsArray` when an invalid `objectTypeId` is encountered. This ensures that unexpected object types during update generation are logged for debugging.

### Called By

*   **`WorldObject.Object/GetUpdateFieldFlagsForTarget`**: Calls `GetUpdateFieldFlagsArray` to retrieve the visibility flags for an object's fields. This is critical for determining which parts of an object's state are visible to a specific player.
*   **`WorldObject.Object/MarkUpdateFieldsWithFlagForUpdate`**: Also calls `GetUpdateFieldFlagsArray` to iterate over fields and mark them for update based on their visibility flags.
*   **`Conditions/IsValid`**: Calls `GetIndexOfUpdateFieldForCurrentBuild` to validate conditions that rely on specific field indices. This ensures that conditional logic in scripts or databases remains valid across different client builds.
*   **`ScriptMgr/LoadScripts`**: Calls `GetIndexOfUpdateFieldForCurrentBuild` during script loading to normalize field indices referenced in script data.
*   **`ChatHandler.DebugCommands/HandleDebugGetValueByNameCommand`**: Calls `GetUpdateFieldDataByName` to resolve a field name to its metadata, allowing a GM to inspect a specific field's value on an object.
*   **`ChatHandler.DebugCommands/HandleDebugSetValueByNameCommand`**: Calls `GetUpdateFieldDataByName` to resolve a field name before modifying its value.
*   **`ChatHandler.DebugCommands/ShowUpdateFieldHelper`**: Calls `GetUpdateFieldDataByTypeMaskAndOffset` to display detailed information about a specific field offset for debugging purposes.

## Data Model

This unit does not interact directly with any database tables. It operates entirely on in-memory static data structures (`g_updateFieldsData`) generated from version-specific source files. The `GetIndexOfUpdateFieldForCurrentBuild` function references symbolic constants that may originate from database schemas, but the unit itself performs no SQL queries or table access.

## Notable Implementation Details

### Version-Specific Compilation
The core of this unit's design is the heavy use of preprocessor directives (`#if SUPPORTED_CLIENT_BUILD >= ...`). Both `UpdateFields.cpp` and `UpdateFields.h` include different `.cpp` and `.h` files depending on the target client version. This means:
*   The `g_updateFieldsData` array, which contains the actual field definitions, is defined in the included version-specific file (e.g., `UpdateFields_1_12_1.cpp`).
*   The constants like `CONTAINER_END`, `PLAYER_END`, and `GAMEOBJECT_END` used to size the flag arrays are also defined in the version-specific headers.
*   Maintainers must ensure that any changes to field layouts in a specific version's source file are consistent with the expectations of the generic code in `UpdateFields.cpp`.

### Pre-computed Flag Arrays
The visibility flags are not computed on-the-fly for every update. Instead, static arrays (`g_containerUpdateFieldFlags`, etc.) are initialized once at startup using `SetupUpdateFieldFlagsArray`. This optimization reduces runtime overhead during the frequent update packet generation process. The initialization iterates over `g_updateFieldsData` and fills the flag array for each offset. Note that if multiple entries in `g_updateFieldsData` overlap in offset (which shouldn't happen in a well-formed definition), the last one processed would overwrite previous flags, though the code assumes non-overlapping ranges.

### Linear Search Performance
Both `GetUpdateFieldDataByName` and `GetUpdateFieldDataByTypeMaskAndOffset` perform linear searches over `g_updateFieldsData`. Since these functions are primarily used by debug commands (`ChatHandler.DebugCommands`) and script loading/validation (which happens infrequently compared to runtime updates), the performance impact is negligible. However, they are not suitable for high-frequency runtime lookups.

### Index Translation Fallback
In `GetIndexOfUpdateFieldForCurrentBuild`, if a `db_index` is not recognized as a legacy constant, the function returns the index unchanged. This implies that scripts or database entries using raw numeric indices are assumed to be compatible with the current build, or that the developer is responsible for ensuring correctness. There is no validation or error logging for unrecognized indices.

### Error Logging in Flag Retrieval
`GetUpdateFieldFlagsArray` logs an error if it receives an `objectTypeId` it doesn't handle. This is a safety net for unexpected object types. Returning `nullptr` in this case will likely cause a crash in the calling code (`WorldObject.Object/...`) if not handled, serving as a fail-fast mechanism for development/debugging.

## Member Reference

**GetUpdateFieldFlagsArray**
Returns a pointer to a pre-computed array of visibility flags for the given `objectTypeId`. Uses a switch statement to select the appropriate static array (e.g., `g_playerUpdateFieldFlags`). Logs an error via `Log.Main/Out` if the type is unhandled and returns `nullptr`. Called by `WorldObject.Object/GetUpdateFieldFlagsForTarget` and `WorldObject.Object/MarkUpdateFieldsWithFlagForUpdate`.

**GetIndexOfUpdateFieldForCurrentBuild**
Inline function that translates a legacy database/script field index into the current build's field index. Maps symbolic constants (e.g., `FIELD_GAMEOBJECT_FLAGS`) to build-specific macros. Returns the input unchanged if no mapping is found. Called by `Conditions/IsValid` and `ScriptMgr/LoadScripts`.

**GetUpdateFieldDataByName**
Linearly searches the global `g_updateFieldsData` array for an entry with a matching name. Returns a pointer to the `UpdateFieldData` struct or `nullptr`. Used by `ChatHandler.DebugCommands/HandleDebugGetValueByNameCommand` and `ChatHandler.DebugCommands/HandleDebugSetValueByNameCommand`.

**GetUpdateFieldDataByTypeMaskAndOffset**
Linearly searches `g_updateFieldsData` for an entry whose `objectTypeMask` intersects with the input mask and whose offset range contains the input offset. Returns a pointer to the `UpdateFieldData` struct or `nullptr`. Used by `ChatHandler.DebugCommands/ShowUpdateFieldHelper`.

---

<!-- machine-true, projected from graph.json -->

## Map — UpdateFields

*Source:* UpdateFields.cpp, UpdateFields.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GetUpdateFieldFlagsArray | function | Log.Main/Out | WorldObject.Object/GetUpdateFieldFlagsForTarget, WorldObject.Object/MarkUpdateFieldsWithFlagForUpdate | — |
| GetIndexOfUpdateFieldForCurrentBuild | function | — | Conditions/IsValid, ScriptMgr/LoadScripts | — |
| GetUpdateFieldDataByName | function | — | ChatHandler.DebugCommands/HandleDebugGetValueByNameCommand, ChatHandler.DebugCommands/HandleDebugSetValueByNameCommand | — |
| GetUpdateFieldDataByTypeMaskAndOffset | function | — | ChatHandler.DebugCommands/ShowUpdateFieldHelper | — |
