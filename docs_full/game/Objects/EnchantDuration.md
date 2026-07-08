# EnchantDuration

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# EnchantDuration

## Purpose & Responsibilities

`EnchantDuration` is a lightweight aggregate struct defined in `Player.h` within the `wowvmangos` codebase. Its sole responsibility is to hold transient runtime state regarding temporary item enchantments that have a limited lifespan. Specifically, it tracks:
1.  The `Item` pointer associated with the enchantment.
2.  The specific `EnchantmentSlot` on that item (e.g., temporary socket bonus, temporary weapon enchant).
3.  The remaining `leftduration` (time left until the enchantment expires).

This struct is designed to be stored in a `std::list` (`EnchantDurationList`) within the `Player` class. It allows the game server to efficiently iterate through all active temporary enchantments on a player's items during tick updates to decrement their durations and remove them when they expire. It contains no logic itself; it is purely a data carrier.

## Member-by-Member Behavior

The `EnchantDuration` struct exposes two constructors and three public data members.

### Constructors

**`EnchantDuration()`** (Default Constructor)
*   **Behavior:** Initializes an empty `EnchantDuration` instance.
*   **Details:** Sets `item` to `nullptr`, `slot` to `MAX_ENCHANTMENT_SLOT` (an invalid/sentinel value indicating no slot), and `leftduration` to `0`. This constructor is typically used when declaring the struct in containers before assignment or for default initialization.

**`EnchantDuration(Item* _item, EnchantmentSlot _slot, uint32 _leftduration)`** (Parameterized Constructor)
*   **Behavior:** Initializes the struct with specific enchantment data.
*   **Details:**
    *   Assigns `_item` to the `item` member.
    *   Assigns `_slot` to the `slot` member.
    *   Assigns `_leftduration` to the `leftduration` member.
    *   **Assertion:** Executes `MANGOS_ASSERT(item)`. This enforces a strict invariant that an `EnchantDuration` record must always point to a valid `Item` object. If `_item` is `nullptr`, the server will crash in debug builds, preventing silent corruption of the enchantment tracking system.

### Data Members

*   **`Item* item`**: Pointer to the `Item` object holding the enchantment. Initialized to `nullptr` by default.
*   **`EnchantmentSlot slot`**: Enum value representing the slot on the item (e.g., `TEMP_ENCHANTMENT_SLOT`). Initialized to `MAX_ENCHANTMENT_SLOT` by default.
*   **`uint32 leftduration`**: The remaining time (in milliseconds) before the enchantment expires. Initialized to `0` by default.

## Cross-Unit Boundaries

`EnchantDuration` is a passive data structure. It does not call into other units. However, it is tightly coupled with the `Player` class, specifically the storage and management subsystems.

*   **Called by `Player.Main/AddEnchantmentDuration`**:
    *   **Direction:** Incoming.
    *   **Context:** The `Player` class (specifically the `AddEnchantmentDuration` method declared in `Player.h`) constructs `EnchantDuration` objects.
    *   **Collaboration:** When a player receives a temporary enchantment (via spell, item use, or trainer), the `Player` logic calculates the duration and creates a new `EnchantDuration` instance using the parameterized constructor. This instance is then inserted into the `Player`'s internal `m_enchantDuration` list (`EnchantDurationList`). This allows the `Player`'s update loop to later process these entries to handle expiration.

## Data Model

`EnchantDuration` does not interact directly with any database tables. It represents volatile, in-memory state that exists only while the player is online or while the `Player` object is resident in memory. Temporary enchantments are generally not persisted to the database in a way that involves this specific struct; rather, the enchantment data is part of the `Item`'s state, which may be saved via the `Item`'s own persistence mechanisms. There are no SQL queries or table references associated with `EnchantDuration`.

## Notable Implementation Details

1.  **Strict Null Pointer Assertion:** The parameterized constructor includes `MANGOS_ASSERT(item)`. This is a critical safety check. Since `EnchantDuration` is used in lists that are iterated over during game ticks, having a dangling or null pointer would cause undefined behavior or crashes when the system attempts to remove the expired enchantment from the item. The assertion ensures that any attempt to create an invalid `EnchantDuration` record fails loudly during development.
2.  **Aggregate Structure:** `EnchantDuration` is a simple aggregate with public members. It does not encapsulate data with getters/setters. This design choice prioritizes performance and simplicity, as the struct is frequently constructed, copied, and accessed in tight loops within the `Player` update cycle.
3.  **Sentinel Value for Slot:** The default value for `slot` is `MAX_ENCHANTMENT_SLOT`. This is likely an enum value representing an invalid or out-of-bounds slot index. This allows systems checking the validity of an `EnchantDuration` entry to quickly identify uninitialized or cleared entries by comparing the slot against this sentinel.
4.  **Memory Management:** The struct holds a raw pointer (`Item*`). It does not manage the lifetime of the `Item`. The `Player` class is responsible for ensuring that the `Item` remains valid as long as the `EnchantDuration` entry exists in the list, and for removing the entry if the `Item` is destroyed.

## Member Reference

**EnchantDuration#2**
Defines the `EnchantDuration` struct. It aggregates an `Item*` pointer, an `EnchantmentSlot` enum, and a `uint32` duration counter. It provides a default constructor initializing members to null/zero/sentinel values and a parameterized constructor that asserts the item pointer is non-null. It is used to track temporary enchantments on player items.

**EnchantDuration**
Constructs an `EnchantDuration` instance with the specified item, slot, and remaining duration. It assigns the input parameters to the respective members (`item`, `slot`, `leftduration`). Crucially, it executes `MANGOS_ASSERT(item)` to ensure the item pointer is valid, preventing null-pointer dereferences in subsequent processing. This constructor is called by `Player.AddEnchantmentDuration` when adding a new temporary enchantment to the player's tracking list.

---

<!-- machine-true, projected from graph.json -->

## Map — EnchantDuration

*Source:* Player.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| EnchantDuration#2 | decl | — | — | — |
| EnchantDuration | ctor | — | Player.Main/AddEnchantmentDuration | — |
