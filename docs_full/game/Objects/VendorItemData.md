# VendorItemData

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# VendorItemData

**VendorItemData** is a lightweight container struct defined in `CreatureDefines.h` that manages the inventory list for a vendor Non-Player Character (NPC). It holds a dynamic list of `VendorItem` pointers, representing the specific items a vendor sells, along with their stock limits, restock timers, and conditional requirements.

This struct serves as the in-memory representation of a vendor's catalog. It is populated during server startup or configuration changes by the `ObjectMgr` singleton and queried by player interaction handlers to validate purchases, check stock availability, and transmit the inventory list to the client.

## Purpose & Responsibilities

The primary responsibility of `VendorItemData` is to provide efficient access to the items a specific vendor offers. It abstracts the raw storage of `VendorItem` objects behind a simple interface that supports:
1.  **Iteration and Counting:** Determining how many items a vendor sells (`GetItemCount`) and checking if the vendor is empty (`Empty`).
2.  **Slot-Based Access:** Retrieving a specific item by its index in the vendor list (`GetItem`). This is critical for network packets where the client references items by their position in the list.
3.  **Modification:** Adding new items to the list (`AddItem`) and clearing the entire list (`Clear`) for memory management or reloading.

It does **not** handle the logic of selling, buying, or checking player conditions itself. Those responsibilities lie in the calling units (`Player`, `WorldSession`, `ObjectMgr`). `VendorItemData` is purely a data holder and accessor.

## Member-by-Member Behavior

### Inventory Accessors

*   **`GetItem`**: Retrieves a pointer to the `VendorItem` at a specific zero-based index (`slot`). If the slot index is greater than or equal to the number of items in the list, it returns `nullptr`. This method is used by `Player.Main/BuyItemFromVendor` to fetch the item details when a player attempts to buy a specific slot, and by `WorldSession.ItemHandler/SendListInventory` to serialize the inventory list for the client. It is also used by `ObjectMgr/IsVendorItemValid` to verify if a slot exists before further validation.
*   **`Empty`**: Returns `true` if the internal `m_items` vector is empty. This is checked by `Player.Main/BuyItemFromVendor` to quickly reject purchase attempts from vendors with no items, and by `Player.Main/PrepareGossipMenu` to determine if a vendor menu should be displayed or skipped.
*   **`GetItemCount`**: Returns the number of items in the vendor list as a `uint8`. This is used by `ObjectMgr/IsVendorItemValid` to validate slot indices, by `Player.Main/BuyItemFromVendor` to ensure the requested slot is within bounds, and by `WorldSession.ItemHandler/SendListInventory` to loop through all items when sending the inventory packet to the client.

### Inventory Modification

*   **`AddItem`**: Appends a new `VendorItem` to the end of the `m_items` list. It takes the item ID, maximum count, restock time, item flags, and condition ID as parameters, constructs a new `VendorItem` object on the heap, and pushes its pointer into the vector. This is called by `ObjectMgr/AddVendorItem` when manually adding items via console commands or scripts, and by `ObjectMgr/LoadVendors#2` when loading vendor data from the database.
*   **`Clear`**: Iterates through all pointers in `m_items`, deletes each `VendorItem` object to prevent memory leaks, and then clears the vector. This is called by `ObjectMgr/LoadVendors#2` when reloading vendor data to discard the old list, and by `ObjectMgr::~ObjectMgr` during server shutdown to clean up all vendor data.

## Cross-Unit Boundaries

`VendorItemData` interacts primarily with the `ObjectMgr` singleton and the `Player` class.

*   **`ObjectMgr`**:
    *   **`ObjectMgr/LoadVendors#2`**: Calls `Clear` to remove existing data and `AddItem` to populate the `VendorItemData` instance for each vendor loaded from the database. This establishes the initial state of all vendors.
    *   **`ObjectMgr/AddVendorItem`**: Calls `AddItem` to dynamically add an item to a vendor's list at runtime.
    *   **`ObjectMgr/IsVendorItemValid`**: Calls `GetItem` and `GetItemCount` to verify that a requested item slot exists and is valid before allowing further processing.
    *   **`ObjectMgr::~ObjectMgr`**: Calls `Clear` on all `VendorItemData` instances to free memory during destruction.

*   **`Player.Main`**:
    *   **`Player.Main/BuyItemFromVendor`**: Calls `Empty` to check if the vendor has any items. It calls `GetItemCount` to validate the slot index provided by the client. It calls `GetItem` to retrieve the `VendorItem` details (price, count, etc.) for the purchase transaction.
    *   **`Player.Main/PrepareGossipMenu`**: Calls `Empty` to decide whether to include vendor options in the gossip menu presented to the player.

*   **`WorldSession.ItemHandler`**:
    *   **`WorldSession.ItemHandler/SendListInventory`**: Calls `GetItemCount` to determine the size of the inventory list. It iterates using `GetItem` to fetch each `VendorItem` and serialize its data into the `SMSG_LIST_INVENTORY` packet sent to the client.

## Data Model

`VendorItemData` does not directly interact with database tables. It is populated by `ObjectMgr/LoadVendors#2`, which reads from the `creature_vendor` table (implied by the context of vendor loading in WoW-like servers, though not explicitly shown in the provided schema or code snippets). The `VendorItem` struct fields correspond to columns typically found in such a table:
*   `item`: The item entry ID.
*   `maxcount`: The maximum number of items available (0 for infinite).
*   `incrtime`: The time in seconds between restocks.
*   `itemflags`: Flags controlling restock behavior (e.g., random restock).
*   `conditionId`: An optional condition ID that must be met for the item to be visible/purchasable.

Since no schema was provided for `creature_vendor`, these column names are inferred from the `VendorItem` struct definition and common practice in this codebase.

## Notable Implementation Details

*   **Heap Allocation**: `AddItem` allocates `VendorItem` objects on the heap using `new`. This means `VendorItemData` is responsible for their lifetime. The `Clear` method correctly deletes these pointers, preventing memory leaks. However, if `Clear` is not called before a `VendorItemData` instance is destroyed (e.g., if it were part of a larger object that didn't call `Clear`), a leak would occur. In this codebase, `ObjectMgr` manages the lifecycle and calls `Clear` appropriately.
*   **No Copy Semantics**: `VendorItemData` does not define a copy constructor or assignment operator. Since it contains raw pointers (`std::vector<VendorItem*>`), copying it would result in shallow copies, leading to double-free errors if both copies are cleared. Users of this struct must be careful not to copy it. The `ObjectMgr` likely stores these instances by value in a map, but relies on the fact that they are not copied after initialization.
*   **Slot-Based Indexing**: The `GetItem` method uses direct index access. This implies that the order of items in the `m_items` vector is significant and corresponds to the slot index used by the client. Items added via `AddItem` are appended to the end, so their slot index depends on the order of addition. This order is determined by the database query order in `ObjectMgr/LoadVendors#2`.
*   **Const Correctness**: Accessor methods (`GetItem`, `Empty`, `GetItemCount`) are marked `const`, ensuring they do not modify the state of the `VendorItemData` instance. This allows them to be called on `const` references, which is good practice for read-only operations.
*   **Return Types**: `GetItem` returns a raw pointer (`VendorItem*`), while `FindItem` (not in the MAP, but present in the source) returns a `const` pointer. The MAP only includes `GetItem`, which returns a non-const pointer, allowing callers to potentially modify the `VendorItem` (though typically they don't). `GetItemCount` returns `uint8`, which is sufficient given the `MAX_VENDOR_ITEMS` limit of 128 defined elsewhere in the header.

## Member Reference

**GetItem**: Retrieves a pointer to the `VendorItem` at the specified zero-based index. Returns `nullptr` if the index is out of bounds. Used by `ObjectMgr/IsVendorItemValid`, `Player.Main/BuyItemFromVendor`, and `WorldSession.ItemHandler/SendListInventory`.

**Empty**: Returns `true` if the vendor has no items. Used by `Player.Main/BuyItemFromVendor` and `Player.Main/PrepareGossipMenu`.

**GetItemCount**: Returns the number of items in the vendor list as a `uint8`. Used by `ObjectMgr/IsVendorItemValid`, `Player.Main/BuyItemFromVendor`, and `WorldSession.ItemHandler/SendListInventory`.

**AddItem**: Creates a new `VendorItem` on the heap and appends it to the internal list. Used by `ObjectMgr/AddVendorItem` and `ObjectMgr/LoadVendors#2`.

**Clear**: Deletes all `VendorItem` objects in the list and clears the vector. Used by `ObjectMgr/LoadVendors#2` and `ObjectMgr::~ObjectMgr`.

---

<!-- machine-true, projected from graph.json -->

## Map — VendorItemData

*Source:* CreatureDefines.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GetItem | method | — | ObjectMgr/IsVendorItemValid, Player.Main/BuyItemFromVendor, WorldSession.ItemHandler/SendListInventory | — |
| Empty | method | — | Player.Main/BuyItemFromVendor, Player.Main/PrepareGossipMenu | — |
| GetItemCount | method | — | ObjectMgr/IsVendorItemValid, Player.Main/BuyItemFromVendor, WorldSession.ItemHandler/SendListInventory | — |
| AddItem | method | — | ObjectMgr/AddVendorItem, ObjectMgr/LoadVendors#2 | — |
| Clear | method | — | ObjectMgr/LoadVendors#2, ObjectMgr/~ObjectMgr | — |
