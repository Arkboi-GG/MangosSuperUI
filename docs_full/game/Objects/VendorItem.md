# VendorItem

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# VendorItem

**Purpose & Responsibilities**

`VendorItem` is a lightweight Plain Old Data (POD) struct defined in `CreatureDefines.h`. It represents a single entry in a Non-Player Character's (NPC) vendor inventory. Its sole responsibility is to hold the configuration data required to define what an item is, how many are available, how quickly they restock, and under what conditions the item is visible to the player.

It acts as the atomic data unit for the `VendorItemData` container, which manages the full list of items an NPC sells. `VendorItem` contains no logic beyond its constructor; all manipulation, iteration, and conditional checking are performed by the owning `VendorItemData` struct or higher-level game logic.

## Member-by-Member Behavior

The unit consists of a single member: the constructor.

### Constructor (`VendorItem`)

The constructor initializes the five data members of the struct. It takes five `uint32` arguments:
1.  `_item`: The unique identifier of the item being sold.
2.  `_maxcount`: The maximum number of this item the vendor can hold at once. A value of `0` indicates an infinite supply.
3.  `_incrtime`: The time interval (in seconds) between restocks. This is only relevant if `maxcount` is non-zero.
4.  `_itemflags`: Bitmask flags controlling vendor-specific behaviors, such as whether the item restocks randomly or dynamically.
5.  `_conditionId`: An identifier for a server-side condition that must be met for the item to be visible/purchasable by the player.

The constructor assigns these values directly to the corresponding member variables using an initializer list.

## Cross-Unit Boundaries

As a simple data structure, `VendorItem` has no outgoing calls to other units. It is purely passive data.

**Incoming Collaborations:**
While the MAP does not explicitly list callers for this specific partial, the source code reveals that `VendorItem` instances are created and managed by the `VendorItemData` struct (also defined in `CreatureDefines.h`). Specifically:
*   **`VendorItemData::AddItem`**: This method allocates new `VendorItem` objects on the heap and pushes them into the internal `std::vector<VendorItem*>` (`m_items`).
*   **`VendorItemData::Clear`**: This method iterates over the vector and deletes the `VendorItem` pointers, managing their lifetime.

Higher-level systems (such as the Creature AI or WorldSession handlers) interact with `VendorItem` indirectly through `VendorItemData` methods like `FindItem`, `GetItem`, and `Empty`.

## Data Model

`VendorItem` itself does not query the database. However, it mirrors the structure of the `creature_vendor` table in the WoW database. The fields correspond roughly to:
*   `item`: The item ID.
*   `maxcount`: The stock limit.
*   `incrtime`: The restock timer.
*   `itemflags`: Flags associated with the vendor entry.
*   `conditionId`: The condition ID linked to the vendor entry.

The actual loading of this data from the database is handled by other units (likely `CreatureTemplateLoader` or similar), which parse the SQL results and populate `VendorItemData` structures using `VendorItem` instances.

## Notable Implementation Details

1.  **Heap Allocation**: `VendorItem` objects are typically allocated on the heap via `new` within `VendorItemData::AddItem`. This means `VendorItemData` is responsible for memory management. The `Clear()` method in `VendorItemData` explicitly deletes each pointer before clearing the vector, preventing memory leaks.
2.  **Infinite Stock Logic**: The `maxcount` field uses `0` to represent infinity. This is a common pattern in game servers to distinguish between "out of stock" (count == 0) and "unlimited stock" (maxcount == 0). Logic consuming this struct must check `maxcount == 0` to determine if stock limits apply.
3.  **Condition System Integration**: The `conditionId` field allows vendors to hide or show items based on complex server-side conditions (e.g., quest completion, reputation, class). The `VendorItem` struct stores the ID, but the evaluation of the condition is performed elsewhere in the engine.
4.  **No Copy/Move Semantics Defined**: As a POD struct with a user-defined constructor, it relies on compiler-generated copy/move semantics. Since it contains only primitive types (`uint32`), shallow copies are safe and efficient.

## Member Reference

**VendorItem**
Constructor that initializes the `item`, `maxcount`, `incrtime`, `itemflags`, and `conditionId` members from the provided arguments. It is the only way to create a valid `VendorItem` instance, ensuring all fields are set at creation time.

---

<!-- machine-true, projected from graph.json -->

## Map — VendorItem

*Source:* CreatureDefines.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| VendorItem | ctor | — | — | — |
