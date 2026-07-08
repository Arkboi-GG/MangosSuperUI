# CooldownContainer

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# CooldownContainer

**Purpose & Responsibilities**

`CooldownContainer` is a lightweight, in-memory data structure responsible for tracking active spell and category cooldowns for a `SpellCaster`. It maintains two parallel indices: one keyed by specific `spellId` and another keyed by `spellCategory`. Its primary responsibilities are:

1.  **Storage:** Holding `CooldownData` objects that define when a specific spell or category becomes available again.
2.  **Expiration Management:** Providing an `Update` mechanism to identify and remove expired cooldowns, ensuring memory does not leak as time progresses.
3.  **Querying:** Allowing external systems (primarily `SpellCaster`, `Player`, and `Creature`) to check if a spell is ready, retrieve its expiration time, or determine if it is permanently locked.
4.  **Modification:** Supporting the addition, removal, and clearing of cooldowns via specific IDs or categories.

It does not handle Global Cooldowns (GCD) or school lockouts; those are managed by separate maps (`m_GCDCatMap` and `m_lockoutMap`) within the parent `SpellCaster` class. `CooldownContainer` strictly manages individual spell and category timers.

## Member-by-Member Behavior

The members of `CooldownContainer` can be grouped into lifecycle management, modification, querying, and iteration.

### Lifecycle and Maintenance

*   **`Update`**: This is the core maintenance routine. It iterates through all stored cooldowns. For each cooldown, it checks if both the specific spell cooldown and the associated category cooldown (if any) have expired relative to the provided `now` timestamp.
    *   If both are expired, the entry is removed from the container entirely.
    *   If the spell cooldown is still active but the category cooldown has expired, the category association is cleared from the `CooldownData` object, and the entry is removed from the internal `categoryMap` index. The spell entry itself remains in `spellIdMap` until its specific timer expires.
    *   Permanent cooldowns (`m_typePermanent == true`) are never removed by `Update`, regardless of time.

### Modification

*   **`AddCooldown`**: Adds or updates a cooldown for a specific spell.
    *   It first calls `RemoveBySpellId` to ensure no duplicate entries exist for the same spell ID.
    *   It creates a new `CooldownData` object and inserts it into `m_spellIdMap`.
    *   **Category Logic:** If a `spellCategory` and `categoryDuration` are provided, it attempts to register this spell in the `categoryMap`. However, it implements a specific rule to prevent overwriting permanent category cooldowns with temporary ones, or vice-versa, in a way that breaks client synchronization. If an existing entry already owns this category and is permanent, the new entry's category link is dropped (`m_category = 0`). If the existing entry is not permanent, its expiration time is updated to the new duration, and the new entry drops its category ownership to maintain a single "owner" record in the `categoryMap` for synchronization purposes.
    *   Returns `true` if a new entry was created (i.e., the spell wasn't already tracked), `false` otherwise.
*   **`RemoveBySpellId`**: Removes a cooldown entry by its spell ID. It also cleans up the corresponding entry in `m_categoryMap` if the removed spell was the owner of that category's index.
*   **`RemoveByCategory`**: Removes the index entry for a specific category from `m_categoryMap` and clears the `m_category` field on the associated `CooldownData` object. It does *not* remove the spell entry from `m_spellIdMap`; it only detaches it from the category tracking.
*   **`erase`**: An iterator-based removal helper. It removes the entry pointed to by the iterator from `m_spellIdMap` and cleans up the `categoryMap` if necessary. It returns the next valid iterator, facilitating safe iteration during removal (used by `Update`).
*   **`clear`**: Empties both `m_spellIdMap` and `m_categoryMap`, resetting the container to an empty state.

### Querying

*   **`FindBySpellId`**: Returns a constant iterator to the cooldown data for a given spell ID, or `end()` if not found.
*   **`FindByCategory`**: Returns a constant iterator to the `CooldownData` object that currently "owns" the index for a given category in `m_categoryMap`. If no spell is currently indexed for that category, it returns `end()`. Note that this finds the *index owner*, not necessarily all spells that belong to that category logically (only one is tracked in the map for sync purposes).
*   **`IsEmpty`**: Returns `true` if `m_spellIdMap` contains no entries.
*   **`size`**: Returns the number of active spell cooldowns tracked in `m_spellIdMap`.

### Iteration Support

*   **`begin`** / **`end`**: Provide standard STL-style const iterators for `m_spellIdMap`, allowing external code to traverse all active spell cooldowns.

## Cross-Unit Boundaries

`CooldownContainer` is a passive data structure; it does not initiate actions outside itself. It is exclusively manipulated and queried by other units.

*   **Called by `SpellCaster`**:
    *   `SpellCaster/UpdateCooldowns` calls `Update` to clean up expired entries periodically.
    *   `SpellCaster/AddCooldown` calls `AddCooldown` to register new cooldowns after a spell is cast.
    *   `SpellCaster/RemoveSpellCooldown` calls `RemoveBySpellId` to manually reset a specific spell's cooldown.
    *   `SpellCaster/RemoveSpellCategoryCooldown` calls `RemoveByCategory` to reset a category cooldown.
    *   `SpellCaster/GetExpireTime`, `SpellCaster/IsSpellOnPermanentCooldown`, and `SpellCaster/IsSpellReady` use `FindBySpellId` and `end` to inspect cooldown states.
    *   `SpellCaster/IsSpellReady` also uses `FindByCategory` to check category locks.

*   **Called by `Player.Main`**:
    *   `Player.Main/AddCooldown` calls `AddCooldown`, `FindBySpellId`, `FindByCategory`, `erase`, `begin`, and `end`. This suggests `Player` may perform more complex manual manipulations or validations than the base `SpellCaster`.
    *   `Player.Main/_LoadSpellCooldowns` calls `AddCooldown` to restore cooldowns from saved state (e.g., after login).
    *   `Player.Main/RemoveSpellCooldown` calls `RemoveBySpellId`.
    *   `Player.Main/RemoveSpellCategoryCooldown` calls `RemoveByCategory`.
    *   `Player.Main/RemoveAllCooldowns` calls `erase`, `begin`, and `end` to iterate and clear all entries.
    *   `Player.Main/SendInitialSpells` calls `size` to determine how many cooldowns to send to the client.

*   **Called by `Creature.Main`**:
    *   `Creature.Main/AddCooldown` calls `AddCooldown`.
    *   `Creature.Main/_LoadSpellCooldowns` calls `AddCooldown`.

*   **Called by `Pet.Main`**:
    *   `Pet.Main/_LoadSpellCooldowns` calls `AddCooldown`.

## Data Model

`CooldownContainer` operates entirely in memory. It does not interact with any database tables directly. The `Tables` column in the MAP is empty for all members. Persistence of cooldowns is handled by the calling units (`Player`, `Creature`, `Pet`) which serialize/deserialize the data using `AddCooldown` and `_LoadSpellCooldowns` methods, likely interacting with character save tables elsewhere in the codebase, but `CooldownContainer` itself is agnostic to storage.

## Notable Implementation Details

1.  **Single Owner Category Indexing**: The `categoryMap` does not store all spells belonging to a category. It stores only *one* iterator pointing to the `CooldownData` that is considered the "owner" for client synchronization purposes. In `AddCooldown`, if a category already exists in the map, the code updates the *existing* owner's expiration time rather than adding a new entry to the map. This implies that the client expects a single update packet for a category cooldown, and the server tracks which spell instance triggered that update.
2.  **Permanent Cooldown Handling**: `CooldownData` has a `m_typePermanent` flag. If set, `IsSpellCDExpired` and `IsCatCDExpired` always return `false`. Consequently, `Update` will never remove these entries. They must be explicitly removed via `RemoveBySpellId` or `clear`. This is used for items or effects that impose a permanent lock until manually reset or reloaded.
3.  **Iterator Invalidation Safety**: The `Update` method uses a `while` loop with an iterator returned by `erase`. This is the correct pattern for removing elements from a `std::map` during iteration. `erase` returns the next valid iterator, preventing undefined behavior.
4.  **Category Detachment on Expiry**: In `Update`, if a spell's specific cooldown is still active but its category cooldown has expired, the code sets `cd->m_category = 0` and removes the entry from `m_categoryMap`. This decouples the spell from the category lock. Future checks for `IsCatCDExpired` will return `true` (because `m_category` is 0), effectively treating the category as unlocked for this spell, even though the spell itself is still on cooldown.
5.  **Memory Management**: `CooldownData` objects are stored as `std::unique_ptr<CooldownData>` in the map. This ensures automatic cleanup when entries are erased. The `AddCooldown` method uses `std::move` to transfer ownership efficiently.

## Member Reference

**Update**
Iterates through `m_spellIdMap`. Removes entries where both spell and category cooldowns are expired. If only the category cooldown expires, it clears the category link from the data and removes the entry from `m_categoryMap`. Permanent cooldowns are ignored.

**AddCooldown**
Removes any existing entry for `spellId`. Creates a new `CooldownData` and inserts it into `m_spellIdMap`. If a category is specified, it updates `m_categoryMap` according to ownership rules: it avoids overwriting permanent category owners and updates the expiration time of the existing owner if the new entry is not permanent. Returns `true` if a new map entry was created.

**RemoveBySpellId**
Finds the entry for `spellId` in `m_spellIdMap`. If found, it removes the corresponding entry from `m_categoryMap` if the spell owned a category, then erases the spell entry from `m_spellIdMap`.

**RemoveByCategory**
Finds the entry for `category` in `m_categoryMap`. If found, it sets the `m_category` field of the associated `CooldownData` to 0 and erases the entry from `m_categoryMap`. The spell entry remains in `m_spellIdMap`.

**erase**
Takes a `ConstIterator` to a spell entry. Removes the associated category entry from `m_categoryMap` if applicable, then erases the spell entry from `m_spellIdMap`. Returns the next valid iterator.

**FindBySpellId**
Returns a `ConstIterator` to the `CooldownData` for the given `id` in `m_spellIdMap`, or `end()` if not found.

**FindByCategory**
Looks up `category` in `m_categoryMap`. If found, returns the iterator stored in the value (which points to the owning `CooldownData` in `m_spellIdMap`). Otherwise, returns `end()`.

**clear**
Calls `clear()` on both `m_spellIdMap` and `m_categoryMap`, destroying all `CooldownData` objects.

**begin**
Returns `m_spellIdMap.begin()`.

**end**
Returns `m_spellIdMap.end()`.

**IsEmpty**
Returns `m_spellIdMap.empty()`.

**size**
Returns `m_spellIdMap.size()`.

---

<!-- machine-true, projected from graph.json -->

## Map — CooldownContainer

*Source:* SpellCaster.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Update | method | — | SpellCaster/UpdateCooldowns | — |
| AddCooldown | method | — | Creature.Main/AddCooldown, Pet.Main/_LoadSpellCooldowns, Player.Main/AddCooldown, Player.Main/_LoadSpellCooldowns, SpellCaster/AddCooldown | — |
| RemoveBySpellId | method | — | Player.Main/RemoveSpellCooldown, SpellCaster/RemoveSpellCooldown | — |
| RemoveByCategory | method | — | SpellCaster/RemoveSpellCategoryCooldown | — |
| erase | method | — | Player.Main/AddCooldown, Player.Main/RemoveAllCooldowns, Player.Main/RemoveSpellCategoryCooldown | — |
| FindBySpellId | method | — | Player.Main/AddCooldown, SpellCaster/GetExpireTime, SpellCaster/IsSpellOnPermanentCooldown, SpellCaster/IsSpellReady | — |
| FindByCategory | method | — | Player.Main/AddCooldown, Player.Main/RemoveSpellCategoryCooldown, SpellCaster/IsSpellReady | — |
| clear | method | — | — | — |
| begin | method | — | Player.Main/RemoveAllCooldowns | — |
| end | method | — | Player.Main/AddCooldown, Player.Main/RemoveAllCooldowns, Player.Main/RemoveSpellCategoryCooldown, SpellCaster/GetExpireTime, SpellCaster/IsSpellOnPermanentCooldown, SpellCaster/IsSpellReady | — |
| IsEmpty | method | — | — | — |
| size | method | — | Player.Main/SendInitialSpells | — |
