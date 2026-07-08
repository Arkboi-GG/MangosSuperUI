# ActionButton

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ActionButton

The `ActionButton` struct, defined in `Player.h`, represents a single slot on a player's action bar within the World of Warcraft emulator (MaNGOS/WowVMaNGOS). It encapsulates the data required to define what happens when a specific keybind or button is pressed—whether it casts a spell, uses an item, executes a macro, or performs a click action—and tracks whether that button's state has changed since the last synchronization with the client.

This unit is a lightweight data container with no external dependencies, database interactions, or complex logic. Its primary responsibility is to pack the action ID and type into a single 32-bit integer (`packedData`) for efficient storage and transmission, while maintaining a separate state flag (`uState`) to optimize network updates by only sending changes when necessary.

## Purpose & Responsibilities

The `ActionButton` struct serves two main purposes:
1.  **Data Packing:** It combines the specific action identifier (e.g., spell ID, item entry, macro ID) and the action type (Spell, Item, Macro, Click) into a single `uint32` field. This reduces memory footprint and simplifies serialization for network packets.
2.  **Change Tracking:** It maintains an `ActionButtonUpdateState` enum (`uState`) to indicate whether the button is new, unchanged, changed, or deleted. This allows the broader `Player` class to efficiently determine which action buttons need to be sent to the client during updates, avoiding unnecessary network traffic for static buttons.

## Member-by-Member Behavior

The struct contains three methods, all of which operate solely on the internal `packedData` and `uState` members.

### **GetType**
Returns the `ActionButtonType` associated with the current button. It extracts the upper 8 bits of `packedData` using the `ACTION_BUTTON_TYPE` macro and casts the result to the `ActionButtonType` enum. This allows callers to determine if the button triggers a spell, item use, macro, or click event.

### **GetAction**
Returns the raw action identifier (e.g., spell ID, item entry) as a `uint32`. It extracts the lower 24 bits of `packedData` using the `ACTION_BUTTON_ACTION` macro. This value is interpreted differently depending on the type returned by `GetType`.

### **SetActionAndType**
Updates the button's action and type. It takes a `uint32 action` and an `ActionButtonType type`, packs them into a new `uint32` using bitwise operations (shifting the type to the upper 8 bits and OR-ing with the action), and compares it to the existing `packedData`.
*   If the new data differs from the current `packedData`, or if the current state is `ACTIONBUTTON_DELETED`, it updates `packedData`.
*   It then updates the `uState` flag:
    *   If the previous state was not `ACTIONBUTTON_NEW`, it sets `uState` to `ACTIONBUTTON_CHANGED`.
    *   If the previous state was `ACTIONBUTTON_NEW`, it remains `ACTIONBUTTON_NEW` (indicating a fresh addition that hasn't been synced yet).
*   This logic ensures that the `Player` class knows precisely when a button has been modified and needs to be communicated to the client.

## Cross-Unit Boundaries

The `ActionButton` struct itself has no outgoing calls. However, it is heavily utilized by other units within the `Player` class hierarchy to manage action bars:

*   **Called by `MasterPlayer.Main/SaveActions`:** The master server component reads the `packedData` and `uState` via `GetType` and `GetAction` to serialize the player's action bar configuration to the database.
*   **Called by `Player.Main/ConvertSpell`:** During spell conversion (e.g., when changing races or classes), this method uses `GetType` and `GetAction` to identify spells on the action bar that need to be replaced or removed, and `SetActionAndType` to update them with new spell IDs.
*   **Called by `MasterPlayer.Main/addActionButton`:** When a player adds a new button via the client, this method uses `SetActionAndType` to populate the `ActionButton` struct with the new action and type, marking it as new or changed for subsequent synchronization.

These interactions highlight that `ActionButton` is a passive data structure manipulated by higher-level player management logic.

## Data Model

The `ActionButton` struct does not directly interact with any database tables. It is a transient in-memory representation of a player's action bar state. Persistence is handled by the `Player` class methods (like `SaveActions` in `MasterPlayer.Main`), which serialize the `packedData` and `uState` into appropriate database columns (likely in a table such as `character_action` or similar, though the specific table schema is not provided in the source). The struct itself contains no SQL queries or table references.

## Notable Implementation Details

*   **Bitwise Packing:** The use of `ACTION_BUTTON_TYPE(X)` and `ACTION_BUTTON_ACTION(X)` macros demonstrates a deliberate design choice to minimize memory usage. The type occupies the most significant byte (bits 24-31), while the action ID occupies the least significant 24 bits (bits 0-23). This allows for up to 16 million unique action IDs, which is sufficient for spell, item, and macro entries in WoW.
*   **State Transition Logic:** The `SetActionAndType` method carefully manages the `uState` transition. It avoids marking a button as `ACTIONBUTTON_CHANGED` if it is still `ACTIONBUTTON_NEW`. This is crucial for optimization: a newly added button should be sent once as "new," and subsequent modifications should be sent as "changed." If a button is deleted (`ACTIONBUTTON_DELETED`) and then re-added, the condition `uState == ACTIONBUTTON_DELETED` ensures it is treated as a fresh change, resetting the state appropriately.
*   **Const Correctness:** `GetType` and `GetAction` are marked `const`, reflecting that they do not modify the object's state. `SetActionAndType` is non-const, as it modifies both `packedData` and `uState`.
*   **No Validation:** The struct does not validate the input `action` or `type` in `SetActionAndType`. It assumes the caller has already verified that the action ID is valid for the given type. This keeps the struct lightweight and fast, pushing validation logic to the higher-level `Player` methods.

## Member Reference

**GetType**: Returns the `ActionButtonType` by extracting the upper 8 bits of `packedData` using the `ACTION_BUTTON_TYPE` macro. Used by `MasterPlayer.Main/SaveActions` and `Player.Main/ConvertSpell` to identify the nature of the action.

**GetAction**: Returns the action ID (e.g., spell ID, item entry) by extracting the lower 24 bits of `packedData` using the `ACTION_BUTTON_ACTION` macro. Used by `MasterPlayer.Main/SaveActions` and `Player.Main/ConvertSpell` to retrieve the specific target of the action.

**SetActionAndType**: Packs the given `action` and `type` into `packedData` using bitwise operations. Updates `uState` to `ACTIONBUTTON_CHANGED` if the data differs from the previous value or if the previous state was `ACTIONBUTTON_DELETED`; otherwise, it retains `ACTIONBUTTON_NEW` if it was already new. Used by `MasterPlayer.Main/addActionButton` and `Player.Main/ConvertSpell` to modify button configurations.

---

<!-- machine-true, projected from graph.json -->

## Map — ActionButton

*Source:* Player.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GetType | method | — | MasterPlayer.Main/SaveActions, Player.Main/ConvertSpell | — |
| GetAction | method | — | MasterPlayer.Main/SaveActions, Player.Main/ConvertSpell | — |
| SetActionAndType | method | — | MasterPlayer.Main/addActionButton, Player.Main/ConvertSpell | — |
