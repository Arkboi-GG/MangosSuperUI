# UpdateFieldData

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# UpdateFieldData

**Purpose & Responsibilities**

`UpdateFieldData` is a lightweight data structure within the `wowvmangos` codebase that defines the metadata for individual "update fields" used in the server-to-client network protocol. In World of Warcraft emulation, objects (units, game objects, items, etc.) maintain a set of dynamic properties—such as health, mana, flags, resistances, and attack power—that must be synchronized with connected clients. These properties are stored in contiguous memory blocks known as "update fields."

The `UpdateFieldData` struct encapsulates the static definition of a single such field. It specifies:
1.  **Which object types** possess this field (`objectTypeMask`).
2.  **Where** the field resides in the object's memory layout (`offset` and `size`).
3.  **What type** of data it holds (`valueType`, e.g., integer, float, GUID).
4.  **Visibility rules** governing who can see updates to this field (`flags`, e.g., public, private, owner-only).

This unit does not perform runtime synchronization itself. Instead, it provides the schema definition that other parts of the engine (such as the update packet generation system) consult to serialize object states correctly. The header `UpdateFields.h` also contains conditional compilation logic to include client-version-specific implementations (e.g., `UpdateFields_1_12_1.h`) and helper functions to map database-style indices to the correct runtime offsets for the compiled client version.

**Member-by-Member Behavior**

This unit consists of two members declared in the MAP: the default constructor and the parameterized constructor. Both are trivial inline implementations defined within the struct declaration in `UpdateFields.h`.

1.  **Default Construction**: Initializes all members to safe defaults (zero masks, empty string names, zero offsets/sizes, `UF_TYPE_NONE`, and `UF_FLAG_NONE`). This allows arrays of `UpdateFieldData` to be default-initialized before being populated by the version-specific headers.
2.  **Parameterized Construction**: Accepts six arguments to fully define a field's metadata. It assigns these directly to the corresponding member variables. This constructor is typically used by the macro-generated code in the included version-specific headers (e.g., `UpdateFields_1_12_1.h`) to populate static lookup tables of field definitions.

**Cross-Unit Boundaries**

According to the MAP, `UpdateFieldData` has no outgoing calls to other units and is not called by other units in the context of this specific translation unit's dependency graph. However, in the broader codebase:
*   **Called By**: The namespace `UpdateFields` (declared in this header but implemented elsewhere, likely in `UpdateFields.cpp` or the version-specific files) uses `UpdateFieldData` instances to build lookup tables. Functions like `GetUpdateFieldDataByName` and `GetUpdateFieldDataByTypeMaskAndOffset` return pointers to these structs.
*   **Calls Out**: None. The struct is pure data.

**Data Model**

This unit does not interact directly with database tables. The `Tables` column in the MAP is empty. The `UpdateFieldData` struct represents in-memory metadata derived from client reverse-engineering, not persistent database records. While the helper function `GetIndexOfUpdateFieldForCurrentBuild` references constants like `FIELD_GAMEOBJECT_FLAGS` which correspond to database column indices in scripts, the struct itself is purely a runtime protocol definition.

**Notable Implementation Details**

1.  **Version-Specific Conditional Compilation**: The top of `UpdateFields.h` contains a large `#if` block that includes one of many `UpdateFields_X_Y_Z.h` files based on `SUPPORTED_CLIENT_BUILD`. This ensures that the `UpdateFieldData` definitions match the exact memory layout and field offsets expected by the specific WoW client version the server is emulating. Field offsets change between patches, making this abstraction critical for compatibility.
2.  **Index Mapping Helper**: The inline function `GetIndexOfUpdateFieldForCurrentBuild` bridges the gap between static database/script indices (which are often hardcoded in SQL or Lua scripts based on a reference build) and the actual runtime offsets for the current build. For example, `FIELD_UNIT_FIELD_RESISTANCES` maps to `UNIT_FIELD_RESISTANCES`, while subsequent resistance fields map to `UNIT_FIELD_RESISTANCES + N`. This prevents scripts from breaking when the underlying field offsets shift between client versions.
3.  **Flag Semantics**: The `UpdateFieldFlags` enum defines visibility constraints. `UF_FLAG_PUBLIC` means all players see the field, `UF_FLAG_PRIVATE` means only the owner sees it, and `UF_FLAG_OWNER_ONLY` restricts visibility further. `UF_FLAG_DYNAMIC` likely indicates that the field changes frequently and may require special handling in update packets (e.g., delta compression).
4.  **Value Types**: The `UpdateFieldValueTypes` enum defines how the field's data is serialized. `UF_TYPE_TWO_SHORT` is notable as it packs two 16-bit integers into a single 32-bit slot, a common optimization in WoW protocols for coordinates or small paired values.

## Member Reference

**UpdateFieldData#2**
The default constructor for `UpdateFieldData`. It initializes all member variables to their default values: `objectTypeMask` to 0, `name` to an empty string literal, `offset` and `size` to 0, `valueType` to `UF_TYPE_NONE`, and `flags` to `UF_FLAG_NONE`. This allows for safe default initialization of arrays or instances before explicit configuration.

**UpdateFieldData**
The parameterized constructor for `UpdateFieldData`. It accepts six arguments: `objectTypeMask_` (uint8), `name_` (const char*), `offset_` (uint16), `size_` (uint16), `valueType_` (UpdateFieldValueTypes), and `flags_` (uint16). It assigns these values directly to the corresponding member variables, fully defining the metadata for a specific update field. This constructor is primarily used by the version-specific header files to populate static field definition tables.

---

<!-- machine-true, projected from graph.json -->

## Map — UpdateFieldData

*Source:* UpdateFields.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| UpdateFieldData#2 | decl | — | — | — |
| UpdateFieldData | ctor | — | — | — |
