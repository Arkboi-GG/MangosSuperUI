# CooldownData

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# CooldownData

`CooldownData` is a lightweight, immutable-at-construction data structure that represents a single active cooldown entry within the `wowvmangos` spell-casting system. It encapsulates the timing and metadata for two distinct cooldown mechanisms: **Spell-specific cooldowns** and **Category-based cooldowns**.

In this engine, spells can belong to a "category" (e.g., all Fire spells might share a category). When a spell in a category is cast, it may trigger a cooldown on that entire category, preventing other spells in the same category from being cast until the timer expires. `CooldownData` tracks both timers independently, allowing the system to determine if a specific spell is ready, if its category is ready, or if the cooldown is "permanent" (a state used for client synchronization or special game mechanics where the cooldown never naturally expires via time).

This class is designed to be stored inside a `std::unique_ptr` within the `CooldownContainer` (defined in the same header, `SpellCaster.h`). It has no external dependencies, performs no I/O, and touches no database tables. Its sole responsibility is to provide accurate time-comparison logic for cooldown expiration.

## Member-by-Member Behavior

The members of `CooldownData` are grouped by their functional role: construction, time retrieval, expiration checks, and metadata access.

### Construction and Initialization

**`CooldownData`**
The constructor initializes the cooldown state based on the current time (`clockNow`) and configuration parameters.
*   **Spell Expiration:** If `duration` is non-zero, `m_expireTime` is set to `clockNow + duration`. Otherwise, it defaults to a zero-initialized `TimePoint`.
*   **Category Expiration:** If `spellCategory` is non-zero AND `categoryDuration` is non-zero, `m_catExpireTime` is set to `clockNow + categoryDuration`. Otherwise, it defaults to a zero-initialized `TimePoint`.
*   **Metadata:** Stores `spellId`, `spellCategory`, `itemId`, and the `isPermanent` flag directly into their respective member variables.
*   **Note:** The `isPermanent` flag dictates the behavior of all subsequent expiration checks. If true, the cooldown is considered "active forever" for the purposes of expiration logic, though it can still be manually removed by the container.

### Time Retrieval

These methods allow external units to query the absolute expiration times. They enforce the "permanent" constraint by returning `false` if the cooldown is marked permanent, indicating that no valid expiration time exists.

**`GetSpellCDExpireTime`**
Retrieves the absolute time point when the specific spell's cooldown ends.
*   If `m_typePermanent` is true, returns `false` and leaves the output parameter unchanged.
*   Otherwise, copies `m_expireTime` to the output parameter and returns `true`.
*   **Called by:** `Pet.Main/_SaveSpellCooldowns`, `Player.Main/SendInitialSpells`, `Player.Main/_SaveSpellCooldowns`, `SpellCaster/GetExpireTime`, `SpellCaster/PrintCooldownList`, `Unit.Main/WritePetSpellsCooldown`.

**`GetCatCDExpireTime`**
Retrieves the absolute time point when the category's cooldown ends.
*   If `m_typePermanent` is true, returns `false`.
*   Otherwise, copies `m_catExpireTime` to the output parameter and returns `true`.
*   **Called by:** `Player.Main/SendInitialSpells`, `Player.Main/_SaveSpellCooldowns`, `SpellCaster/PrintCooldownList`, `Unit.Main/WritePetSpellsCooldown`.

### Expiration Logic

These methods determine if a cooldown has elapsed relative to a given "now" timestamp. They are the core decision-making functions for whether a spell can be cast.

**`IsSpellCDExpired`**
Determines if the spell-specific cooldown has passed.
*   Returns `false` immediately if `m_typePermanent` is true (permanent cooldowns never expire).
*   Otherwise, returns `true` if `now >= m_expireTime`.
*   **Called by:** `Player.Main/AddCooldown`, `SpellCaster/IsSpellOnPermanentCooldown`.

**`IsCatCDExpired`**
Determines if the category-specific cooldown has passed.
*   Returns `false` immediately if `m_typePermanent` is true.
*   Returns `true` if `m_category` is 0 (no category assigned, so no category cooldown to block).
*   Returns `true` if `now >= m_catExpireTime`.
*   Returns `false` otherwise.
*   **Called by:** `Player.Main/AddCooldown`.

### Metadata Accessors

Simple getters for the stored identifiers and flags.

**`IsPermanent`**
Returns the `m_typePermanent` flag. Used to distinguish between temporary timers and persistent states.
*   **Called by:** `Pet.Main/_SaveSpellCooldowns`, `Player.Main/AddCooldown`, `Player.Main/RemoveAllCooldowns`, `Player.Main/SendInitialSpells`, `Player.Main/_SaveSpellCooldowns`, `SpellCaster/GetExpireTime`, `SpellCaster/IsSpellOnPermanentCooldown`, `SpellCaster/PrintCooldownList`, `Unit.Main/WritePetSpellsCooldown`.

**`GetItemId`**
Returns the ID of the item associated with this cooldown (if any).
*   **Called by:** `Player.Main/AddCooldown`, `Player.Main/SendInitialSpells`, `Player.Main/_SaveSpellCooldowns`.

**`GetSpellId`**
Returns the ID of the spell associated with this cooldown.
*   **Called by:** `Player.Main/AddCooldown`, `Player.Main/RemoveAllCooldowns`, `Player.Main/RemoveSpellCategoryCooldown`, `Player.Main/SendInitialSpells`, `Player.Main/_SaveSpellCooldowns`, `Unit.Main/WritePetSpellsCooldown`.

**`GetCategory`**
Returns the spell category ID.
*   **Called by:** `Player.Main/SendInitialSpells`, `Player.Main/_SaveSpellCooldowns`, `Unit.Main/WritePetSpellsCooldown`.

### Modification

**`SetCatCDExpireTime`**
Updates the category expiration time. This is notably mutable, unlike the other fields. It is used when a new spell in the same category extends the existing category cooldown.
*   **Called by:** No external units listed in the MAP, but logically used by `CooldownContainer` (which is in the same file/unit context) when managing category overlaps.

## Cross-Unit Boundaries

`CooldownData` is a pure data holder with no outgoing calls. It relies entirely on incoming calls from higher-level management classes to interpret its state.

*   **`Player.Main`**: The primary consumer. `Player.Main` uses `CooldownData` to manage player spell readiness, save/load cooldown states to/from the database (via `_SaveSpellCooldowns`), and synchronize the client UI (`SendInitialSpells`). It adds new cooldowns via `AddCooldown` and queries expiration via `IsSpellCDExpired` and `IsCatCDExpired`.
*   **`SpellCaster`**: Uses `CooldownData` for internal consistency checks. `SpellCaster/GetExpireTime` retrieves raw times for debugging or complex logic, while `SpellCaster/IsSpellOnPermanentCooldown` checks the permanent flag. `SpellCaster/PrintCooldownList` iterates over these objects to display debug information to administrators.
*   **`Pet.Main`**: Similar to `Player.Main`, pets have their own cooldown systems. `Pet.Main/_SaveSpellCooldowns` persists pet cooldowns, relying on `CooldownData` to provide the necessary timestamps and flags.
*   **`Unit.Main`**: Handles serialization for network packets. `Unit.Main/WritePetSpellsCooldown` extracts data from `CooldownData` to send cooldown updates to the client for pets.

## Data Model

`CooldownData` itself does not interact with any database tables. It is an in-memory representation. However, the data it holds is persisted by calling units (specifically `Player.Main` and `Pet.Main`) into the database. The MAP indicates no direct table usage by this unit.

## Notable Implementation Details

1.  **Permanent Cooldowns:** The concept of a "permanent" cooldown is central to this class. A permanent cooldown does not expire via time (`IsSpellCDExpired` and `IsCatCDExpired` always return `false`). This is likely used for spells that are permanently locked out due to game rules (e.g., class restrictions) or for synchronization purposes where the client needs to know a spell is unavailable, but the server doesn't want to track a ticking timer. The `IsPermanent` flag is the gatekeeper for all time-based logic.
2.  **Category vs. Spell Cooldowns:** The class maintains two independent timers (`m_expireTime` and `m_catExpireTime`). This allows for scenarios where a spell's individual cooldown has expired, but the category cooldown has not (preventing other spells in the group from casting), or vice versa. The `IsCatCDExpired` method explicitly checks if `m_category` is 0, treating un-categorized spells as having no category restriction.
3.  **Immutability (Except Category Time):** Once constructed, most fields are effectively immutable. The only mutable field exposed is `m_catExpireTime` via `SetCatCDExpireTime`. This design choice supports the `CooldownContainer`'s logic where a new spell cast in a category might extend the existing category cooldown without replacing the entire `CooldownData` object.
4.  **TimePoint Usage:** The class uses `std::chrono::time_point` (aliased as `TimePoint` in the project) for high-resolution time tracking. This ensures precise cooldown calculations, avoiding integer overflow issues associated with millisecond counters over long periods.
5.  **Friend Class:** `CooldownContainer` is declared as a friend, granting it access to private members. This is necessary for `CooldownContainer` to efficiently manage the lifecycle of `CooldownData` objects, including setting the category expiration time and accessing internal state for cleanup.

## Member Reference

**`CooldownData`**
Constructor that initializes spell and category expiration times based on `clockNow`, `duration`, and `categoryDuration`. Sets metadata fields (`m_spellId`, `m_category`, `m_itemId`, `m_typePermanent`). If durations are zero or categories are invalid, expiration times default to zero-initialized `TimePoint`s.

**`GetSpellCDExpireTime`**
Returns `false` if the cooldown is permanent. Otherwise, outputs the absolute `m_expireTime` to the reference parameter and returns `true`. Called by `Pet.Main/_SaveSpellCooldowns`, `Player.Main/SendInitialSpells`, `Player.Main/_SaveSpellCooldowns`, `SpellCaster/GetExpireTime`, `SpellCaster/PrintCooldownList`, `Unit.Main/WritePetSpellsCooldown`.

**`SetCatCDExpireTime`**
Sets the `m_catExpireTime` to the provided `expireTime`. Allows external modification of the category timer, typically used when extending a category cooldown.

**`GetCatCDExpireTime`**
Returns `false` if the cooldown is permanent. Otherwise, outputs the absolute `m_catExpireTime` to the reference parameter and returns `true`. Called by `Player.Main/SendInitialSpells`, `Player.Main/_SaveSpellCooldowns`, `SpellCaster/PrintCooldownList`, `Unit.Main/WritePetSpellsCooldown`.

**`IsSpellCDExpired`**
Returns `false` if permanent. Otherwise, returns `true` if the current time `now` is greater than or equal to `m_expireTime`. Called by `Player.Main/AddCooldown`, `SpellCaster/IsSpellOnPermanentCooldown`.

**`IsCatCDExpired`**
Returns `false` if permanent. Returns `true` if `m_category` is 0 (no category) or if `now` is greater than or equal to `m_catExpireTime`. Called by `Player.Main/AddCooldown`.

**`IsPermanent`**
Returns the boolean value of `m_typePermanent`. Called by `Pet.Main/_SaveSpellCooldowns`, `Player.Main/AddCooldown`, `Player.Main/RemoveAllCooldowns`, `Player.Main/SendInitialSpells`, `Player.Main/_SaveSpellCooldowns`, `SpellCaster/GetExpireTime`, `SpellCaster/IsSpellOnPermanentCooldown`, `SpellCaster/PrintCooldownList`, `Unit.Main/WritePetSpellsCooldown`.

**`GetItemId`**
Returns the `m_itemId` associated with the cooldown. Called by `Player.Main/AddCooldown`, `Player.Main/SendInitialSpells`, `Player.Main/_SaveSpellCooldowns`.

**`GetSpellId`**
Returns the `m_spellId` associated with the cooldown. Called by `Player.Main/AddCooldown`, `Player.Main/RemoveAllCooldowns`, `Player.Main/RemoveSpellCategoryCooldown`, `Player.Main/SendInitialSpells`, `Player.Main/_SaveSpellCooldowns`, `Unit.Main/WritePetSpellsCooldown`.

**`GetCategory`**
Returns the `m_category` ID associated with the cooldown. Called by `Player.Main/SendInitialSpells`, `Player.Main/_SaveSpellCooldowns`, `Unit.Main/WritePetSpellsCooldown`.

---

<!-- machine-true, projected from graph.json -->

## Map — CooldownData

*Source:* SpellCaster.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| CooldownData | ctor | — | — | — |
| GetSpellCDExpireTime | method | — | Pet.Main/_SaveSpellCooldowns, Player.Main/SendInitialSpells, Player.Main/_SaveSpellCooldowns, SpellCaster/GetExpireTime, SpellCaster/PrintCooldownList, Unit.Main/WritePetSpellsCooldown | — |
| SetCatCDExpireTime | method | — | — | — |
| GetCatCDExpireTime | method | — | Player.Main/SendInitialSpells, Player.Main/_SaveSpellCooldowns, SpellCaster/PrintCooldownList, Unit.Main/WritePetSpellsCooldown | — |
| IsSpellCDExpired | method | — | Player.Main/AddCooldown, SpellCaster/IsSpellOnPermanentCooldown | — |
| IsCatCDExpired | method | — | Player.Main/AddCooldown | — |
| IsPermanent | method | — | Pet.Main/_SaveSpellCooldowns, Player.Main/AddCooldown, Player.Main/RemoveAllCooldowns, Player.Main/SendInitialSpells, Player.Main/_SaveSpellCooldowns, SpellCaster/GetExpireTime, SpellCaster/IsSpellOnPermanentCooldown, SpellCaster/PrintCooldownList, Unit.Main/WritePetSpellsCooldown | — |
| GetItemId | method | — | Player.Main/AddCooldown, Player.Main/SendInitialSpells, Player.Main/_SaveSpellCooldowns | — |
| GetSpellId | method | — | Player.Main/AddCooldown, Player.Main/RemoveAllCooldowns, Player.Main/RemoveSpellCategoryCooldown, Player.Main/SendInitialSpells, Player.Main/_SaveSpellCooldowns, Unit.Main/WritePetSpellsCooldown | — |
| GetCategory | method | — | Player.Main/SendInitialSpells, Player.Main/_SaveSpellCooldowns, Unit.Main/WritePetSpellsCooldown | — |
