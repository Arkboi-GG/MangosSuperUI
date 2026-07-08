# ItemPosCount

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Documentation: `ItemPosCount`

## Purpose & Responsibilities

`ItemPosCount` is a lightweight aggregate struct defined in `Player.h`. It serves as a data carrier for inventory management operations within the `Player` class, specifically representing a single destination slot (or "position") and the quantity of items intended for that slot.

Its primary responsibility is to decouple the *calculation* of where items should go from the *execution* of moving or storing them. When the `Player` class determines that an item can be stored (e.g., via `_CanStoreItem_InBag`), it populates a vector of `ItemPosCount` objects (`ItemPosCountVec`). This vector is then passed to storage functions (like `StoreItem`) to perform the actual insertion. This two-phase approach allows the system to validate space availability and calculate split quantities (e.g., stacking 5 items in one slot and 3 in another) before modifying the player's inventory state.

## Member-by-Member Behavior

The `ItemPosCount` struct contains two data members and one constructor.

### Constructor: `ItemPosCount(uint16 _pos, uint8 _count)`
*   **Behavior:** Initializes the `pos` member with `_pos` and the `count` member with `_count`.
*   **Context:** This is the standard way to create an instance. It is typically invoked by the `Player` class methods (specifically `Player.Main._CanStoreItem_InBag`, `Player.Main._CanStoreItem_InInventorySlots`, and `Player.Main._CanStoreItem_InSpecificSlot`) when they identify a valid slot for an item.

### Data Members
*   **`uint16 pos`:** Represents the inventory slot index. In the WoW protocol and MaNGOS implementation, this is often a packed value where the high byte represents the bag ID and the low byte represents the slot index within that bag (e.g., `bag << 8 | slot`).
*   **`uint8 count`:** The number of items to place in the specified `pos`. This is capped by `uint8` (max 255), which aligns with the maximum stack size for most items in World of Warcraft Classic/TBC era.

## Cross-Unit Boundaries

`ItemPosCount` acts as a bridge between the validation logic and the execution logic within the `Player` class.

### Called By
*   **`Player.Main._CanStoreItem_InBag`**: This private method in `Player.cpp` (referenced as `Player.Main` in the map) calculates potential slots in a specific bag. When it finds a valid slot, it constructs an `ItemPosCount` and appends it to the `dest` vector.
*   **`Player.Main._CanStoreItem_InInventorySlots`**: Similar to above, but scans specific inventory ranges (e.g., equipment slots or backpack slots). It creates `ItemPosCount` instances for valid slots found.
*   **`Player.Main._CanStoreItem_InSpecificSlot`**: Validates a single specific slot. If valid, it creates an `ItemPosCount` for that slot.

### Calls Out
*   **None:** `ItemPosCount` itself does not call into other units. It is a passive data structure. However, the vectors containing these structs (`ItemPosCountVec`) are passed *from* the `Player` validation methods *to* the `Player` storage methods (e.g., `StoreItem`, `EquipItem`).

## Data Model

`ItemPosCount` does not interact directly with any database tables. It is an in-memory transient object used solely during the runtime processing of inventory transactions. The underlying inventory data is persisted in the `character_inventory` table (and related tables like `character_equipmentsets`), but `ItemPosCount` is not mapped to any specific row or column; it merely describes a delta operation against that persistent state.

## Notable Implementation Details

1.  **Packed Position Encoding:** The `pos` member is a `uint16`. In the context of `Player.h`, helper functions like `IsInventoryPos(uint16 pos)` exist which decode this value using bitwise operations (`pos >> 8` for bag, `pos & 255` for slot). Maintainers must ensure that any code constructing `ItemPosCount` uses this packed format consistently.
2.  **Stack Size Limitation:** The `count` field is `uint8`. This imposes a hard limit of 255 items per slot in this data structure. While this matches the game's typical stack limits, any logic attempting to store more than 255 items in a single `ItemPosCount` entry will overflow. The calling code in `Player` must handle splitting large quantities into multiple `ItemPosCount` entries if necessary.
3.  **Vector Ownership:** `ItemPosCount` objects are almost always managed within a `std::vector<ItemPosCount>` (typedef'd as `ItemPosCountVec`). The `Player` class methods take a reference to this vector (`ItemPosCountVec& dest`) and populate it. The caller is responsible for the lifetime of the vector.

## Member Reference

**ItemPosCount**  
Constructor that initializes the `pos` and `count` members. Used by `Player.Main._CanStoreItem_InBag`, `Player.Main._CanStoreItem_InInventorySlots`, and `Player.Main._CanStoreItem_InSpecificSlot` to create destination descriptors for items.

---

<!-- machine-true, projected from graph.json -->

## Map — ItemPosCount

*Source:* Player.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ItemPosCount | ctor | — | Player.Main/_CanStoreItem_InBag, Player.Main/_CanStoreItem_InInventorySlots, Player.Main/_CanStoreItem_InSpecificSlot | — |
