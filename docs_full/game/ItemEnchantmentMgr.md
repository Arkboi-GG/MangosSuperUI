<!-- provenance: verbose -->
# ItemEnchantmentMgr

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ItemEnchantmentMgr

**ItemEnchantmentMgr** resolves random item enchantments by loading weighted definitions from `item_enchantment_template` into a global cache and selecting specific enchantment IDs based on those weights.

## Purpose & Responsibilities

This unit handles two distinct tasks:
1.  **Loading**: Reads `item_enchantment_template` from the database, filtering rows by the server's current WoW patch version (`patch_min`/`patch_max`), and caches valid entries in a static global map.
2.  **Resolution**: Selects a specific enchantment ID for a given item random property entry using a cumulative weighted random roll.

## Member-by-Member Behavior

### Configuration Loading

**`LoadRandomEnchantmentsTable`**
Populates the global `RandomItemEnch` cache.
1.  Clears existing state to support hot-reloads.
2.  Queries `item_enchantment_template` for rows where the current patch falls within `patch_min` and `patch_max`.
3.  Iterates results, extracting `entry`, `ench`, and `chance`.
4.  Validates `chance`: must be `> 0.000001f` and `<= 100.0f`. Invalid rows are skipped with a `LOG_DBERROR`.
5.  Inserts valid entries into `RandomItemEnch` keyed by `entry`.
6.  Logs the count of loaded definitions or an error if the table is empty/query fails. Uses `ProgressBar` for console feedback.

### Enchantment Resolution

**`GetItemEnchantMod`**
Selects an enchantment ID for a given `entry`.
1.  Returns `0` if `entry` is `0`.
2.  Looks up `entry` in `RandomItemEnch`. If missing, logs `LOG_DBERROR` (indicating a mismatch with `item_template`) and returns `0`.
3.  Calculates `total_chance` by summing all `chance` values for the entry.
4.  Rolls a random float via `rand_chance_f()` scaled by `total_chance / 100.0`.
5.  Iterates the enchantment list, accumulating `chance`. Returns the `ench` of the first entry where cumulative chance `>= roll`.
6.  Returns `0` if no enchantment is selected (fallback).

## Cross-Unit Boundaries

### Outbound Calls
*   **`Database/PQuery`**, **`QueryResult`**, **`Field`**: Used by `LoadRandomEnchantmentsTable` to fetch and parse database rows.
*   **`World/GetWowPatch`**: Called by `LoadRandomEnchantmentsTable` to filter rows by patch version.
*   **`Log.Main/Out`**: Called by both functions for status and error logging.
*   **`ProgressBar`**: Used by `LoadRandomEnchantmentsTable` for console progress bars.
*   **`shared_Util/rand_chance_f`**: Called by `GetItemEnchantMod` for weighted random selection.

### Inbound Calls
*   **`World/SetInitialWorldSettings`**: Calls `LoadRandomEnchantmentsTable` at startup.
*   **`ChatHandler.ServerCommands/HandleReloadItemEnchantementsCommand`**: Calls `LoadRandomEnchantmentsTable` for admin reloads.
*   **`game_Objects_Item/GenerateItemRandomPropertyId`**: Calls `GetItemEnchantMod` when creating items with random properties.
*   **`ObjectMgr/LoadItemPrototypes`**: Calls `GetItemEnchantMod` during prototype loading.
*   **`ChatHandler.DebugCommands/HandleDebugItemEnchantCommand`**: Calls `GetItemEnchantMod` for debugging.

## Data Model

Interacts with `item_enchantment_template`:

| Column | Type | Constraints | Description |
| :--- | :--- | :--- | :--- |
| `entry` | `mediumint(8) unsigned` | `PK` | Item Random Property ID. |
| `ench` | `mediumint(8) unsigned` | `PK` | Enchantment ID. |
| `chance` | `float unsigned` | | Probability weight (0–100). |
| `patch_min` | `tinyint(3) unsigned` | `PK` | Min valid patch version. |
| `patch_max` | `tinyint(3) unsigned` | `PK` | Max valid patch version. |

The composite PK allows multiple enchantments per entry with different patch ranges. Chances are not required to sum to 100 in the DB; the code sums them dynamically for weighting.

## Notable Implementation Details

*   **Patch Filtering**: SQL filters rows using `sWorld.GetWowPatch()`, ensuring only relevant enchantments are loaded for the current server version.
*   **Chance Validation**: Rows with `chance <= 0.000001f` or `> 100.0f` are rejected. This prevents negligible weights and invalid percentages.
*   **Weighted Roll**: `GetItemEnchantMod` uses a cumulative distribution approach. If the sum of chances is less than 100, there is an implicit chance of returning `0` (no enchantment).
*   **Global State**: `RandomItemEnch` is a static global map. Reloading clears it entirely. Concurrent access during reload is not protected internally; callers must ensure thread safety.
*   **Missing Entry Error**: If `GetItemEnchantMod` finds no data for an `entry`, it logs a `LOG_DBERROR`, highlighting potential database inconsistencies.

## Member Reference

**`EnchStoreItem`**
Default constructor for the internal struct, initializing `ench` and `chance` to 0.

**`EnchStoreItem#2`**
Parameterized constructor for `EnchStoreItem`, initializing `ench` and `chance` with provided values.

**`LoadRandomEnchantmentsTable`**
Loads and validates enchantment data from `item_enchantment_template` into the global cache, filtering by patch version.

**`GetItemEnchantMod`**
Resolves a specific enchantment ID for a given item entry using weighted random selection from the cached data.

---

<!-- machine-true, projected from graph.json -->

## Map — ItemEnchantmentMgr

*Source:* ItemEnchantmentMgr.cpp, ItemEnchantmentMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| EnchStoreItem | ctor | — | — | — |
| EnchStoreItem#2 | ctor | — | — | — |
| LoadRandomEnchantmentsTable | function | Database/PQuery, Field/GetFloat, Field/GetUInt32, Log.Main/Out, ProgressBar/BarGoLink#3, ProgressBar/step, QueryResult/Fetch, QueryResult/GetRowCount, QueryResult/NextRow, World/GetWowPatch | ChatHandler.ServerCommands/HandleReloadItemEnchantementsCommand, World/SetInitialWorldSettings | item_enchantment_template |
| GetItemEnchantMod | function | Log.Main/Out, shared_Util/rand_chance_f | ChatHandler.DebugCommands/HandleDebugItemEnchantCommand, game_Objects_Item/GenerateItemRandomPropertyId, ObjectMgr/LoadItemPrototypes | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `item_enchantment_template`: entry mediumint(8) unsigned PK, ench mediumint(8) unsigned PK, chance float unsigned, patch_min tinyint(3) unsigned PK, patch_max tinyint(3) unsigned PK

*`?` = nullable, `PK` = primary key column.*

