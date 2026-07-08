# LootStoreItem

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# LootStoreItem

## Purpose & Responsibilities

`LootStoreItem` is a lightweight data structure defined in `LootMgr.h` that represents a single potential drop entry within a loot table template. It serves as the bridge between static database configuration and dynamic runtime loot generation. Specifically, it stores the raw parameters for an item drop—such as the item ID, drop chance, quantity limits, grouping constraints, and conditional requirements—as they are loaded from the database.

Its primary responsibility is to hold these configuration values in a format optimized for the `LootTemplate` processing logic. It does not manage the state of an actual dropped item (that is the role of `LootItem`, defined in the same header but distinct in purpose); rather, `LootStoreItem` defines the *rules* for whether an item *can* drop and how it behaves during the roll phase. It handles the conversion of database-specific representations (such as negative chances indicating quest items) into normalized internal flags (`needs_quest`) and absolute probabilities (`chance`).

## Member-by-Member Behavior

The unit consists of a single constructor. There are no methods or additional members defined exclusively within this unit's scope in the provided source, though the struct contains several data members that are initialized by the constructor.

### Data Members
While not functions, the following data members constitute the state of `LootStoreItem` and are critical to understanding its behavior:
*   **`itemid`**: The unique identifier of the item that may drop.
*   **`chance`**: A `float` representing the probability of the item dropping. This value is always positive, derived from the absolute value of the input parameter.
*   **`mincountOrRef`**: An `int32` that serves a dual purpose. If positive, it indicates the minimum quantity of the item to drop. If negative, it acts as a reference ID to another loot template (specifically, the absolute value is the referenced template ID).
*   **`group`**: A 7-bit unsigned integer indicating the loot group. Items in the same group compete against each other; typically, only one item from a group drops per loot event.
*   **`needs_quest`**: A 1-bit boolean flag. If true, the item is a quest reward and requires the player to have the relevant quest active to see or loot it.
*   **`maxcount`**: An 8-bit unsigned integer. If `mincountOrRef` is positive, this is the maximum quantity. If `mincountOrRef` is negative (reference), this acts as a multiplier for the referenced template.
*   **`conditionId`**: A 16-bit unsigned integer representing an additional conditional requirement (e.g., faction, race, or skill level) that must be met for the item to be eligible.

### Constructor: `LootStoreItem`

**Signature:**
```cpp
LootStoreItem(uint32 _itemid, float _chanceOrQuestChance, int8 _group, uint16 _conditionId, int32 _mincountOrRef, uint8 _maxcount)
```

**Behavior:**
This constructor initializes the `LootStoreItem` instance using parameters passed from the database loading routine. Its key logical contribution is the normalization of the `_chanceOrQuestChance` parameter:

1.  **Probability Normalization**: It assigns `fabs(_chanceOrQuestChance)` to the `chance` member. This ensures that the internal `chance` value is always positive, regardless of how it was stored in the database.
2.  **Quest Flag Derivation**: It sets the `needs_quest` bit to `true` if `_chanceOrQuestChance` is less than zero. In the database schema for loot tables, a negative chance value is the standard convention for marking an item as a quest drop. By checking `< 0`, the constructor decouples the sign of the chance from its magnitude, storing the intent (quest vs. non-quest) separately from the probability.
3.  **Direct Assignment**: All other parameters (`_itemid`, `_group`, `_conditionId`, `_mincountOrRef`, `_maxcount`) are assigned directly to their corresponding members without transformation.

**Note on `displayid`**: The comment in the header mentions that `displayid` is filled in `IsValid()`. However, `displayid` is not a member of `LootStoreItem` in the provided source code. This suggests that `IsValid()` (a member of `LootStoreItem` declared in the header but implemented elsewhere, likely in `LootMgr.cpp`) may interact with external item data or that the comment refers to legacy code or a different struct context. Based strictly on the provided `LootMgr.h`, `LootStoreItem` does not contain a `displayid` member.

## Cross-Unit Boundaries

### Called By: `LootMgr/LoadLootTable`
*   **Direction**: Inbound (Construction)
*   **Context**: The `LootStoreItem` constructor is invoked by the `LoadLootTable` function within the `LootMgr` unit (likely `LootMgr.cpp`).
*   **Collaboration**: During server startup or reload, `LootMgr` reads loot table data from the database. For each row in the loot table, it extracts the raw columns (item ID, chance, group, etc.) and constructs a `LootStoreItem` object. This object is then added to a `LootTemplate`'s internal list of entries. This boundary represents the transition from persistent storage (database rows) to in-memory configuration objects.

### Calls Out: None
*   The `LootStoreItem` constructor does not call any other units. It performs only local initialization and basic arithmetic (`fabs`).

## Data Model

`LootStoreItem` itself does not directly query the database. However, it is populated by `LootMgr/LoadLootTable`, which reads from the game's loot table schemas (typically `creature_loot_template`, `gameobject_loot_template`, `pickpocketing_loot_template`, etc.).

Based on the constructor parameters, the expected database columns mapped to `LootStoreItem` are:
*   **`Entry` / `ItemId`**: Maps to `itemid`.
*   **`ChanceOrQuestChance`**: Maps to `_chanceOrQuestChance`. Negative values indicate quest items.
*   **`Group`**: Maps to `_group`.
*   **`ConditionId`**: Maps to `_conditionId`.
*   **`MinCountOrRef`**: Maps to `_mincountOrRef`. Negative values indicate a reference to another loot template.
*   **`MaxCount`**: Maps to `_maxcount`.

No specific SQL schema is provided in the input, so column types and constraints are inferred solely from the C++ parameter types (`uint32`, `float`, `int8`, `uint16`, `int32`, `uint8`).

## Notable Implementation Details

1.  **Bit-Packing for Efficiency**: The struct uses bit-fields for `group` (7 bits), `needs_quest` (1 bit), `maxcount` (8 bits), and `conditionId` (16 bits). This reduces the memory footprint of each loot entry, which is significant because loot templates can contain hundreds of entries, and thousands of templates exist in memory.
2.  **Dual-Purpose `mincountOrRef`**: The use of a signed integer (`int32`) for `mincountOrRef` allows the system to distinguish between a simple quantity constraint (positive) and a template reference (negative) without needing a separate boolean flag or union. This is a common pattern in MaNGOS/TrinityCore to save space and simplify parsing logic.
3.  **Quest Item Encoding**: The reliance on the sign of the chance value to determine quest status is a historical artifact of the database design. The constructor abstracts this away, ensuring that downstream code (like `Roll()` or `IsValid()`) can treat `chance` as a pure probability and `needs_quest` as a pure boolean, reducing the risk of errors related to negative probabilities.
4.  **Const-Correctness**: Although not visible in the constructor, the `LootStoreItem` is typically treated as immutable after construction. Methods like `Roll()` and `IsValid()` are declared `const` in the header, reinforcing that this struct is a configuration snapshot, not a mutable state object.

## Member Reference

**LootStoreItem**
Constructor that initializes a loot entry from database parameters. It normalizes the drop chance to a positive float and derives the `needs_quest` flag from the sign of the input chance. It directly assigns item ID, group, condition ID, min/max counts, and reference IDs. Called by `LootMgr/LoadLootTable` during loot table loading.

---

<!-- machine-true, projected from graph.json -->

## Map — LootStoreItem

*Source:* LootMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| LootStoreItem | ctor | — | LootMgr/LoadLootTable | — |
