# PlayerCreateInfoAction

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# PlayerCreateInfoAction

## Purpose & Responsibilities

`PlayerCreateInfoAction` is a lightweight Plain Old Data (POD) struct defined in `Player.h`. It represents a single entry in the initial action bar configuration for a newly created character.

When a player creates a new character, the game server must populate the character's starting equipment, spells, and action bars based on the character's race and class. The `PlayerCreateInfoAction` struct holds the data for one specific button on that action bar:
1.  **Which button slot** it occupies (`button`).
2.  **What action** is assigned to it (`action`, typically a spell ID or item ID).
3.  **The type** of that action (`type`, indicating whether it is a spell, item, macro, etc.).

This struct is part of the `PlayerCreateInfoActions` vector (defined as `std::vector<PlayerCreateInfoAction>`), which aggregates all starting actions for a specific race/class combination. It is populated during the server's initialization phase by reading data from the database via the `ObjectMgr` unit and is consumed during character creation to set up the new `Player` object's action bars.

## Member-by-Member Behavior

### `PlayerCreateInfoAction#2` (Declaration)
This is the struct definition itself. It contains three public member variables:
*   `uint8 button`: The index of the action bar slot (0–119, corresponding to `MAX_ACTION_BUTTONS`).
*   `uint8 type`: The type of the action, derived from the `ActionButtonType` enum (e.g., `ACTION_BUTTON_SPELL`, `ACTION_BUTTON_ITEM`).
*   `uint32 action`: The identifier for the action, such as a Spell Entry ID or Item Entry ID.

The struct provides two constructors:
1.  **Default Constructor**: Initializes all members to zero.
2.  **Parameterized Constructor**: Accepts `_button`, `_action`, and `_type` to initialize the members directly.

### `PlayerCreateInfoAction` (Constructor Implementation)
While the declaration shows the constructor signature, the implementation is trivial (member-wise initialization). Its role is to allow the `ObjectMgr` unit to construct these objects efficiently when loading the `player_create_info_action` table from the database.

## Cross-Unit Boundaries

### Called By: `ObjectMgr/LoadPlayerInfo`
*   **Direction:** Inbound (Data Population)
*   **Collaboration:** The `ObjectMgr` unit (specifically the `LoadPlayerInfo` method) reads rows from the `player_create_info_action` database table. For each row, it constructs a `PlayerCreateInfoAction` object using the parameterized constructor, passing the `button`, `action`, and `type` columns from the database result. These objects are then pushed into a `PlayerCreateInfoActions` vector associated with a specific `PlayerInfo` struct (which groups data by Race and Class).
*   **Why:** This populates the static lookup tables used during character creation. When a user clicks "Create Character," the server looks up the pre-loaded `PlayerCreateInfoActions` for that race/class and applies them to the new `Player` instance.

### Calls Out: None
This unit is a pure data container. It does not invoke any other units, perform I/O, or contain business logic beyond basic initialization.

## Data Model

This unit interacts indirectly with the database table **`player_create_info_action`**. Although the schema is not explicitly provided in the input, the code structure and standard MaNGOS/WowVMangos conventions indicate the following usage:

*   **Table:** `player_create_info_action`
*   **Columns Used:**
    *   `race_mask`: Used by `ObjectMgr` to filter which races this action applies to.
    *   `class_mask`: Used by `ObjectMgr` to filter which classes this action applies to.
    *   `button`: Maps to `PlayerCreateInfoAction::button`.
    *   `action`: Maps to `PlayerCreateInfoAction::action`.
    *   `type`: Maps to `PlayerCreateInfoAction::type`.

The `PlayerCreateInfoAction` struct itself does not perform queries; it merely holds the deserialized values from this table.

## Notable Implementation Details

1.  **Bit-Packing Context:** The `type` field in `PlayerCreateInfoAction` corresponds to the high byte of the `packedData` in the `ActionButton` struct (also defined in `Player.h`). The `ActionButton` struct uses macros like `ACTION_BUTTON_TYPE(X)` to extract the type from a packed 32-bit integer. `PlayerCreateInfoAction` stores them separately for clarity during the loading phase, but they are conceptually equivalent to the components of the runtime `ActionButton` state.
2.  **Memory Efficiency:** As a POD struct with no virtual functions or dynamic allocations, it is cheap to copy and store in vectors. This allows the `ObjectMgr` to hold multiple copies of action sets for different race/class combinations in memory without significant overhead.
3.  **Default Initialization:** The default constructor sets all values to 0. This is important because `button` 0 is a valid slot, but `action` 0 is generally invalid (no spell/item ID 0). The loading logic in `ObjectMgr` ensures that only valid, non-zero actions are processed, or the default constructor is used as a placeholder before assignment.

## Member Reference

**PlayerCreateInfoAction#2**
The struct definition for `PlayerCreateInfoAction`. It defines the data layout for a single starting action bar entry, comprising `button` (slot index), `type` (action category), and `action` (ID). It includes a default constructor and a parameterized constructor.

**PlayerCreateInfoAction**
The constructor implementation for the struct. It initializes the `button`, `type`, and `action` members with the provided arguments. This is primarily called by `ObjectMgr/LoadPlayerInfo` when populating the starting action bar data from the database.

---

<!-- machine-true, projected from graph.json -->

## Map — PlayerCreateInfoAction

*Source:* Player.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| PlayerCreateInfoAction#2 | decl | — | — | — |
| PlayerCreateInfoAction | ctor | — | ObjectMgr/LoadPlayerInfo | — |
