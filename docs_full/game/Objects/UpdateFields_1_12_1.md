# UpdateFields_1_12_1

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`UpdateFields_1_12_1.cpp` is a data-only translation unit that defines the static metadata required for serializing and deserializing object state updates for World of Warcraft patch 1.12.1 (Build 5875). It contains no executable logic, classes, or functions. Its sole purpose is to provide the `g_updateFieldsData` array, which acts as a canonical lookup table mapping every possible object field to its network transmission properties.

This metadata is essential for the server's networking layer to correctly encode object updates (such as health changes, equipment swaps, or position updates) into packets that the client expects. Each entry in the array specifies:
1.  **Object Type Mask:** Identifies the base class or derived class owning the field (e.g., `TYPEMASK_UNIT`, `TYPEMASK_PLAYER`).
2.  **Field Name:** A human-readable identifier (e.g., `UNIT_FIELD_HEALTH`).
3.  **Offset:** The starting index of the field within the object's update block.
4.  **Size:** The number of values (elements) contained in the field.
5.  **Data Type:** The serialization format (GUID, INT, FLOAT, BYTES, TWO_SHORT).
6.  **Visibility Flags:** Rules governing which clients receive the data (Public, Owner Only, Private, Group Only, Dynamic).

## Member-by-Member Behavior

This unit contains no callable members. It consists entirely of a single static constant array initialization.

**`g_updateFieldsData`**
This is a `static const std::array<UpdateFieldData, 324>` that defines the complete schema for object field updates. The entries are ordered sequentially by offset and grouped by object type enums:
*   **EObjectFields:** Base fields for all objects (GUID, Type, Entry, Scale).
*   **EItemFields:** Item-specific data (Owner, Stack Count, Durability, Enchantments, Random Properties).
*   **EContainerFields:** Bag data (Slot count, Item GUIDs for slots).
*   **EUnitFields:** Creature/Player base stats (Health, Power, Level, Faction, Auras, Attack Power, Display ID).
*   **EPlayerFields:** Player-specific data (Quest Log, Visible Items, Inventory/Bank/Keyring slots, Experience, Skills, Combat Ratings, Honor).
*   **EGameObjectFields:** Static world object data (Display ID, Rotation, Position, State, Faction).
*   **EDynamicObjectFields:** Spell effect visuals (Caster, Spell ID, Radius, Position).
*   **ECorpseFields:** Dead body data (Owner, Position, Display ID, Loot Items).

Each entry uses flags to control visibility and transmission behavior:
*   `UF_FLAG_PUBLIC`: Sent to all observers.
*   `UF_FLAG_OWNER_ONLY`: Sent only to the object's owner.
*   `UF_FLAG_PRIVATE`: Sent only to the owner (and potentially debug/GM tools).
*   `UF_FLAG_GROUP_ONLY`: Sent to group members.
*   `UF_FLAG_DYNAMIC`: Included in updates only when the value changes (optimization).
*   `UF_FLAG_NONE`: Internal padding or alignment fields, not transmitted.

## Cross-Unit Boundaries

As a pure data definition file, `UpdateFields_1_12_1.cpp` does not call any other units. It is a leaf node in the dependency graph.

*   **Called By:** Core networking and object management units (such as `Object::BuildValuesUpdateBlock` or similar serialization routines in `Object.cpp` or `WorldPacket.cpp`) iterate over `g_updateFieldsData`. They use the metadata to determine how to read raw memory from an object and format it into a network packet.
*   **Direction:** Data flows *from* this unit *to* the serialization logic. The serialization logic consumes the offsets, sizes, types, and flags to construct valid protocol messages.

## Data Model

This unit does not interact with any database tables. It defines in-memory metadata for network protocol serialization.

## Notable Implementation Details

1.  **Patch Specificity:** The file header explicitly states `Patch: 1.12.1` and `Build: 5875`. The offsets, sizes, and field counts are hard-coded for this specific client version. Using this data with a different patch would cause desynchronization, leading to crashes or visual glitches.
2.  **Padding and Alignment:** Several entries are marked with `UF_FLAG_NONE` and `UF_TYPE_NONE` or `UF_TYPE_INT` with size 1, labeled as `PADDING` or `ALIGN_PAD` (e.g., `OBJECT_FIELD_PADDING`, `CONTAINER_ALIGN_PAD`, `UNIT_FIELD_PADDING`). These fields exist solely to maintain memory alignment or fill gaps in the client's expected structure; they carry no meaningful game data and are not transmitted.
3.  **End Markers:** Each object type section ends with an `_END` marker (e.g., `OBJECT_END`, `UNIT_END`) with size 0 and `UF_TYPE_NONE`. These serve as delimiters for iteration loops, allowing serializers to identify the boundary of a specific object type's field range.
4.  **Complex Visibility Rules:** Player fields often have split visibility. For instance, `PLAYER_QUEST_LOG_*` fields use `UF_FLAG_GROUP_ONLY` for the quest ID portion and `UF_FLAG_PRIVATE` for the progress/details portion. This allows group members to see *which* quest a player is undertaking, while hiding detailed progress from non-owners.
5.  **Large Contiguous Blocks:** Some fields span many indices. `PLAYER_SKILL_INFO_1_1` spans 384 indices of `UF_TYPE_TWO_SHORT`, and `PLAYER_FIELD_INV_SLOT_HEAD` spans 46 GUIDs. These reflect fixed-size arrays in the client's memory layout for skills and inventory.
6.  **Dynamic Optimization:** Fields like `UNIT_FIELD_HEALTH` and `UNIT_FIELD_MAXHEALTH` are marked `UF_FLAG_DYNAMIC`. This indicates they are only included in update packets when their value changes, reducing bandwidth usage compared to `UF_FLAG_PUBLIC` fields like `UNIT_FIELD_LEVEL`, which are always sent to observers.

## Member Reference

This unit contains no members listed in the MAP. The MAP is empty for this translation unit.

---

<!-- machine-true, projected from graph.json -->

## Map — UpdateFields_1_12_1

*Source:* UpdateFields_1_12_1.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
