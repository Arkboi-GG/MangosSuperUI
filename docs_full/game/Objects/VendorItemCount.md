# VendorItemCount

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# VendorItemCount

**Purpose & Responsibilities**

`VendorItemCount` is a lightweight data structure defined in `CreatureDefines.h` that tracks the dynamic inventory state of a single item sold by a vendor NPC. While the static definition of what items a vendor sells (including maximum stock limits and restock intervals) is stored in `VendorItem`, `VendorItemCount` holds the runtime mutable state: the current quantity available and the timestamp of the last stock update.

This struct enables the server to simulate limited-stock vendors where items deplete when purchased and regenerate over time. It is instantiated and managed by the `Creature` class (specifically via `Creature.Main` and `Creature.UpdateVendorItemCurrentCount`) to maintain accurate inventory counts for each vendor instance.

## Member-by-Member Behavior

### **VendorItemCount** (Constructor)

The constructor initializes the runtime state for a specific vendor item. It accepts three parameters:
1.  `_item`: The item ID (`uint32`) being tracked.
2.  `_count`: The initial quantity (`uint32`) of the item available.
3.  `_restockDelay`: The time interval (`uint32`, in seconds) required to replenish one unit of stock.

Upon construction, the member variables `itemId`, `count`, and `restockDelay` are assigned directly from these arguments. Crucially, `lastIncrementTime` is initialized to the current system time using `time(nullptr)`. This establishes the baseline timestamp from which future restock calculations will measure elapsed time.

## Cross-Unit Boundaries

*   **Called by `Creature.Main`**: The main logic of the `Creature` class instantiates `VendorItemCount` objects when loading or refreshing a vendor's inventory. This occurs during the initialization of the vendor's data structures, ensuring that each item starts with a defined count and a valid timestamp.
*   **Called by `Creature.UpdateVendorItemCurrentCount`**: This method in the `Creature` class interacts with `VendorItemCount` to manage stock depletion and regeneration. It likely reads `count` to determine if an item can be sold, decrements `count` upon sale, and checks `lastIncrementTime` against the current time to increment `count` if the `restockDelay` has passed.

## Data Model

This unit does not directly interact with database tables. It operates entirely on in-memory data structures derived from database queries performed by other units (such as `Creature`). The fields correspond logically to concepts found in vendor-related database tables (like `creature_vendor` or `npc_vendor`), but `VendorItemCount` itself contains no SQL logic or direct table references.

## Notable Implementation Details

*   **Time-Based Restocking**: The use of `time_t` for `lastIncrementTime` implies that restocking logic relies on wall-clock time differences. This allows for asynchronous restocking checks; the server does not need a periodic timer for every vendor item but can calculate availability on-demand when a player attempts to buy an item.
*   **No Bounds Checking in Constructor**: The constructor does not validate that `_count` is less than or equal to a maximum limit. It assumes the caller (`Creature`) has already validated the initial count against the static `maxcount` defined in `VendorItem`.
*   **Simple Value Semantics**: `VendorItemCount` is a plain old data structure (POD-like) with public members. It does not encapsulate its state, relying on the owning `Creature` object to enforce consistency between `count`, `restockDelay`, and `lastIncrementTime`.

## Member Reference

**VendorItemCount**
Constructor that initializes the item ID, current count, restock delay, and sets the last increment time to the current system time. Used to create runtime instances of vendor item stock tracking.

---

<!-- machine-true, projected from graph.json -->

## Map — VendorItemCount

*Source:* CreatureDefines.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| VendorItemCount | ctor | — | Creature.Main/UpdateVendorItemCurrentCount | — |
