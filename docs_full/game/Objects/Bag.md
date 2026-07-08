# Bag

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

The `Bag` class represents a container item within the `wowvmangos` engine. It inherits from `Item`, extending the base item functionality to manage a collection of other items stored within it. Its primary responsibility is to maintain the state of its internal slots (`m_bagslot`), ensuring that items placed inside are correctly linked to the bag, and that the bag's representation in the world (via update blocks) and the database reflects its contents.

Key responsibilities include:
1.  **Slot Management:** Tracking which `Item` pointers occupy which of the `MAX_BAG_SIZE` slots.
2.  **State Synchronization:** Ensuring that when an item is added or removed, both the in-memory pointer array and the underlying object fields (used for network updates) are updated consistently.
3.  **Lifecycle Propagation:** When the bag is added to or removed from the world, or saved/deleted from the database, these actions must be propagated to all items contained within the bag.
4.  **Querying:** Providing methods to check capacity, find items by entry ID or GUID, and count specific items.

## Member-by-Member Behavior

### Construction and Destruction
*   **`Bag` (ctor):** Initializes the object as both an `ITEM` and a `CONTAINER` type. It sets the object type ID to `TYPEID_CONTAINER` and clears the `m_bagslot` array using `memset`.
*   **`~Bag` (dtor):** Iterates through `m_bagslot` and deletes any `Item` pointers found. This implies that `Bag` takes ownership of the items stored within it.

### World Lifecycle
*   **`AddToWorld`:** Calls the parent `Item::AddToWorld()` first, then iterates through all occupied slots and calls `AddToWorld()` on each contained item. This ensures nested items are registered in the world state.
*   **`RemoveFromWorld`:** Iterates through all occupied slots and calls `RemoveFromWorld()` on each contained item, then calls the parent `Item::RemoveFromWorld()`. The order is reversed compared to addition to ensure children are cleaned up before the parent.

### Creation and Initialization
*   **`Create`:** Validates the item prototype. If the prototype specifies more container slots than `MAX_BAG_SIZE`, creation fails. It initializes the bag's core fields:
    *   Sets durability to maximum.
    *   Sets stack count to 1.
    *   Sets the number of slots (`CONTAINER_FIELD_NUM_SLOTS`) from the prototype.
    *   Clears all slot GUIDs in the object fields and nullifies the `m_bagslot` pointers.

### Storage Operations
*   **`StoreItem`:** Places an `Item` into a specific slot. It updates the `m_bagslot` array, sets the slot's GUID field in the bag's object data, and updates the item's own fields (`ITEM_FIELD_CONTAINED`, `ITEM_FIELD_OWNER`, `SetContainer`, `SetSlot`) to link it back to the bag.
*   **`RemoveItem`:** Removes an item from a specific slot. It clears the item's container reference (`SetContainer(nullptr)`), nullifies the `m_bagslot` entry, and clears the slot's GUID field in the bag's object data. Note: It does **not** delete the item; it only detaches it.
*   **`Clear`:** Declared in the header but not defined in the provided source. Based on the destructor and `RemoveItem` logic, it likely iterates slots to remove and potentially delete items, but its exact behavior is not visible in `Bag.cpp`.

### Querying and Inspection
*   **`GetBagSize`:** Returns the number of slots defined by the item prototype, retrieved via `GetUInt32Value(CONTAINER_FIELD_NUM_SLOTS)`.
*   **`GetFreeSlots`:** Counts the number of `nullptr` entries in `m_bagslot` up to `GetBagSize()`.
*   **`IsEmpty`:** Returns `true` if all slots up to `GetBagSize()` are `nullptr`.
*   **`GetItemByPos`:** Returns the item at a specific slot index, if valid.
*   **`GetItemByEntry`:** Iterates through slots to find the first item matching a specific item entry ID.
*   **`GetItemCount`:** Sums the stack counts of all items in the bag matching a specific entry ID, optionally excluding a specific item instance (`eItem`).
*   **`GetSlotByItemGUID`:** Finds the slot index containing an item with a specific GUID.

### Persistence (Database)
*   **`SaveToDB`:** Delegates entirely to `Item::SaveToDB()`. The bag's contents are presumably saved as separate item records linked to the bag's GUID, handled by the item persistence logic.
*   **`LoadFromDB`:** Calls `Item::LoadFromDB()` first. Then, it resets the bag's slot fields:
    *   Sets `CONTAINER_FIELD_NUM_SLOTS` from the prototype.
    *   Iterates through all possible slots, clearing the GUID fields in the object data.
    *   Deletes any existing `Item` pointers in `m_bagslot` (cleaning up stale memory).
    *   Crucially, it does **not** load the items themselves here. The items are loaded separately (likely via `NewItemOrBag` and `Item::LoadFromDB` called by the player/inventory loading system) and then attached via `StoreItem`.
*   **`DeleteFromDB`:** Iterates through `m_bagslot` and calls `DeleteFromDB()` on each contained item, then calls `Item::DeleteFromDB()`. This ensures nested items are removed from the database before the bag itself.

### Network Updates
*   **`BuildCreateUpdateBlockForPlayer`:** Builds the update block for the bag itself (via parent), then iterates through all occupied slots and builds update blocks for each contained item. This ensures clients receive the state of items inside bags when the bag is created or updated.

## Cross-Unit Boundaries

### Calls Out
*   **`game_Objects_Item/Item`:** The `Bag` constructor and destructor rely on `Item`'s lifecycle. `AddToWorld`, `RemoveFromWorld`, `SaveToDB`, `LoadFromDB`, and `DeleteFromDB` all delegate to or wrap `Item` methods. `StoreItem` and `RemoveItem` call `Item` methods like `SetContainer`, `SetSlot`, `SetGuidValue`, etc.
*   **`Object`:** `Create` calls `Object::_Create`, `SetEntry`, `SetGuidValue`, `SetUInt32Value`. `StoreItem` and `RemoveItem` use `SetGuidValue`. `GetItemByEntry` uses `GetEntry`.
*   **`ObjectGuid`:** Used extensively for setting and comparing GUIDs in slots and item fields.
*   **`ObjectMgr`:** `Create` calls `GetItemPrototype` to validate and configure the bag.
*   **`WorldObject.Object`:** `Create` calls `SetObjectScale` and `SetUInt32Value`. `BuildCreateUpdateBlockForPlayer` calls the parent implementation.
*   **`Errors`:** `StoreItem` and `RemoveItem` call `PrintStacktraceAndThrow` if assertions fail (though the actual throw logic is likely in the assertion macro, the dependency is listed).

### Called By
*   **`AiBotAI`:** Various AI modules (`Bridge`, `Loot`) call `GetBagSize`, `IsEmpty`, and `GetItemByPos` to manage bot inventory and loot decisions.
*   **`ChatHandler`:** Debug and character commands use `GetBagSize`, `IsEmpty`, and `GetItemByPos` to inspect or manipulate player inventory.
*   **`Player.Main`:** The `Player` class heavily relies on `Bag` for inventory management. Methods like `CanStoreItems`, `SwapItem`, `_StoreItem`, `DestroyItem`, and `_LoadInventory` interact with `Bag` to place, remove, and query items. `GetItemCount` is used by `Player::GetItemCount`.
*   **`AuctionHouseMgr` & `MasterPlayer.Main`:** These units call `NewItemOrBag` to instantiate either a `Bag` or an `Item` depending on the prototype, demonstrating the factory pattern usage.

## Data Model

The `Bag` class interacts with the database primarily through its inheritance from `Item`. The specific tables touched are those managed by `Item::SaveToDB`, `Item::LoadFromDB`, and `Item::DeleteFromDB`. Typically, this involves the `character_inventory` table, where each item (including bags) has a record. The bag's contents are stored as separate records in the same table, linked to the bag via the `bagguid` or similar foreign key mechanism (handled by the `Item` persistence layer). The `Bag` class itself does not execute direct SQL queries; it delegates to `Item`.

## Notable Implementation Details

1.  **Ownership Semantics:** The destructor deletes all items in `m_bagslot`. This means `Bag` owns the `Item` objects stored within it. However, `RemoveItem` does *not* delete the item; it only detaches it. The caller of `RemoveItem` is responsible for deleting the item if it is no longer needed. This is a critical distinction to avoid double-deletion or memory leaks.
2.  **Lazy Loading of Contents:** `LoadFromDB` clears the `m_bagslot` array and deletes any existing pointers, but it does *not* populate the slots. The items are loaded separately by the higher-level inventory loading system (e.g., `Player::_LoadInventory`), which creates the `Item`/`Bag` objects and then calls `StoreItem` to attach them. This decouples the bag's structure from its contents during loading.
3.  **Fixed Slot Array:** `m_bagslot` is a fixed-size array of `MAX_BAG_SIZE` (36) pointers. Even if a bag has fewer slots (e.g., a 6-slot bag), the array remains size 36, but only the first `N` slots are considered valid based on `GetBagSize()`. Methods like `GetFreeSlots` and `IsEmpty` iterate only up to `GetBagSize()`, ignoring the rest of the array.
4.  **Assertion Safety:** `StoreItem` and `RemoveItem` use `MANGOS_ASSERT(slot < MAX_BAG_SIZE)` to prevent out-of-bounds access. This is a runtime check that will abort the server if violated in debug builds.
5.  **Update Block Propagation:** `BuildCreateUpdateBlockForPlayer` recursively builds update blocks for contained items. This ensures that when a bag is sent to a client, the client receives the state of all items inside it, maintaining consistency.

## Member Reference

**Bag**: Constructor that initializes the object as a container, sets type masks, and clears the slot array.
**~Bag**: Destructor that deletes all items stored in the bag's slots.
**AddToWorld**: Adds the bag and all contained items to the world state.
**Clear**: Declared in header, definition not present in source; likely clears all slots.
**RemoveFromWorld**: Removes all contained items and then the bag itself from the world state.
**GetBagSize**: Returns the number of slots defined by the item prototype.
**Create**: Initializes the bag with a given GUID, item ID, and owner, validating slot limits and setting initial fields.
**NewItemOrBag**: Factory function that creates a `Bag` if the prototype is a bag, otherwise an `Item`.
**SaveToDB**: Delegates to `Item::SaveToDB`.
**LoadFromDB**: Loads the bag's base data, resets slot fields, and cleans up old item pointers; does not load contents.
**DeleteFromDB**: Deletes all contained items from the DB, then the bag itself.
**GetFreeSlots**: Counts empty slots up to the bag's defined size.
**RemoveItem**: Detaches an item from a slot, updating links but not deleting the item.
**StoreItem**: Attaches an item to a slot, updating all relevant links and fields.
**BuildCreateUpdateBlockForPlayer**: Builds network update data for the bag and all contained items.
**IsEmpty**: Checks if all slots up to the bag's size are empty.
**GetItemByEntry**: Finds the first item in the bag with a matching entry ID.
**GetItemCount**: Sums the stack counts of items with a matching entry ID, optionally excluding one.
**GetSlotByItemGUID**: Finds the slot index of an item with a specific GUID.
**GetItemByPos**: Returns the item at a specific slot index.

---

<!-- machine-true, projected from graph.json -->

## Map — Bag

*Source:* Bag.cpp, Bag.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Bag | ctor | game_Objects_Item/Item | — | — |
| ~Bag | dtor | — | — | — |
| AddToWorld | method | Object/AddToWorld | — | — |
| Clear | decl | — | — | — |
| RemoveFromWorld | method | game_Objects_Item/RemoveFromWorld | — | — |
| GetBagSize | method | — | AiBotAI.Bridge/BridgeHandleSellItems, AiBotAI.Bridge/BridgeSendState, AiBotAI.Loot/TryAutoEquip, AiBotAI.Loot/TryAutoEquipBags, ChatHandler.CharacterCommands/HandleResetItemsCommand, ChatHandler.DebugCommands/HandleDebugGetItemStateCommand, Player.Main/CanStoreItems, Player.Main/CanUnequipItems, Player.Main/CountFreeInventorySlots, Player.Main/DestroyConjuredItems, Player.Main/DestroyItemCount#2, Player.Main/DestroyZoneLimitedItem, Player.Main/DurabilityLossAll, Player.Main/DurabilityPointsLossAll, Player.Main/GetItemByGuid, Player.Main/HasItemCount, Player.Main/IsValidPos, Player.Main/RemoveAllEnchantments, Player.Main/SwapItem, Player.Main/_CanStoreItem_InBag, Player.Main/_LoadInventory | — |
| Create | method | Object/SetEntry, Object/SetGuidValue, ObjectGuid/ObjectGuid, ObjectMgr/GetItemPrototype, WorldObject.Object/SetObjectScale, WorldObject.Object/SetUInt32Value, WorldObject.Object/_Create | — | — |
| NewItemOrBag | function | — | AuctionHouseMgr/LoadAuctionItems, game_Objects_Item/CreateItem, MasterPlayer.Main/LoadMailedItems, Player.Main/DeleteFromDB, Player.Main/_LoadInventory | — |
| SaveToDB | method | game_Objects_Item/SaveToDB | — | — |
| LoadFromDB | method | game_Objects_Item/GetProto, game_Objects_Item/LoadFromDB, Object/SetGuidValue, ObjectGuid/ObjectGuid, WorldObject.Object/SetUInt32Value | — | — |
| DeleteFromDB | method | game_Objects_Item/DeleteFromDB | — | — |
| GetFreeSlots | method | — | — | — |
| RemoveItem | method | Errors/PrintStacktraceAndThrow, game_Objects_Item/SetContainer, Object/SetGuidValue, ObjectGuid/ObjectGuid | Player.Main/DestroyItem, Player.Main/RemoveItem, Player.Main/SwapItem | — |
| StoreItem | method | Errors/PrintStacktraceAndThrow, game_Objects_Item/GetOwnerGuid, game_Objects_Item/SetContainer, game_Objects_Item/SetSlot, Object/GetObjectGuid, Object/SetGuidValue | Player.Main/SwapItem, Player.Main/_StoreItem | — |
| BuildCreateUpdateBlockForPlayer | method | WorldObject.Object/BuildCreateUpdateBlockForPlayer | — | — |
| IsEmpty | method | — | AiBotAI.Loot/TryAutoEquipBags, game_Objects_Item/CanBeTraded, Player.Main/CanBankItem, Player.Main/CanStoreItems, Player.Main/CanUnequipItem, Player.Main/DestroyItem, Player.Main/SwapItem, Player.Main/_CanStoreItem, Player.Main/_CanStoreItem_InBag, Player.Main/_CanStoreItem_InInventorySlots, Player.Main/_CanStoreItem_InSpecificSlot, WorldSession.ItemHandler/HandleSellItemOpcode | — |
| GetItemByEntry | method | Object/GetEntry | — | — |
| GetItemCount | method | game_Objects_Item/GetCount, Object/GetEntry | Player.Main/GetItemCount | — |
| GetSlotByItemGUID | method | Object/GetObjectGuid, ObjectGuid/operator== | — | — |
| GetItemByPos | method | — | ChatHandler.CharacterCommands/HandleResetItemsCommand, ChatHandler.DebugCommands/HandleDebugGetItemStateCommand, Player.Main/DestroyConjuredItems, Player.Main/DestroyItemCount#2, Player.Main/DestroyZoneLimitedItem, Player.Main/GetItemByGuid, Player.Main/GetItemByPos, Player.Main/RemoveAllEnchantments, Player.Main/SwapItem | — |
