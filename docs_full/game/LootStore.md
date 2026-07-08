# LootStore

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# LootStore

## Purpose & Responsibilities

`LootStore` is a registry and lookup table for loot templates within the `wowvmangos` server. It acts as the authoritative source for determining whether a specific entity (creature, game object, item, etc.) has defined loot drops and provides access to those definitions.

The class is designed as a generic container for different types of loot sources. The codebase instantiates multiple global `LootStore` objects (e.g., `LootTemplates_Creature`, `LootTemplates_Fishing`) to segregate loot data by source type. Each instance holds a map of `LootTemplate` objects, keyed by a unique identifier (`loot_id`).

Key responsibilities include:
1.  **Existence Checking:** Quickly verifying if a given ID has associated loot via `HaveLootFor`.
2.  **Metadata Provision:** Providing human-readable names for debugging and logging via `GetName` and `GetEntryName`.
3.  **Rate Configuration:** Indicating whether loot rates (multipliers for drop chances) apply to this store via `IsRatesAllowed`.
4.  **Template Management:** Loading loot definitions from the database (via `LoadLootTable`, though the implementation is in the `.cpp` file not provided here, the declaration exists) and managing the lifecycle of these templates.

`LootStore` does not perform the actual randomization or distribution of items; that logic resides in `LootTemplate` and the `Loot` struct. `LootStore` is purely a data accessor and validator.

## Member-by-Member Behavior

### Construction and Destruction

*   **`LootStore` (Constructor):** Initializes the store with a descriptive name (`m_name`), an entry name for database identification (`m_entryName`), and a boolean flag (`m_ratesAllowed`) indicating if loot rates apply. It sets up the internal `m_LootTemplates` map.
*   **`~LootStore` (Destructor):** Calls `Clear()` to release memory held by the `LootTemplate` objects stored in `m_LootTemplates`. This prevents memory leaks when the server shuts down or reloads loot data.

### Lookup and Validation

*   **`HaveLootFor`:** A constant-time check to see if a `loot_id` exists in the `m_LootTemplates` map. It returns `true` if the ID is found, `false` otherwise. This is the primary gatekeeper before attempting to generate loot.
*   **`GetName`:** Returns the `m_name` string. This is used by `LootMgr` functions (`FillLoot`, `IsValid`, `LoadLootTable`, etc.) to identify the type of loot being processed in logs or error messages.
*   **`GetEntryName`:** Returns the `m_entryName` string. Used by `LootMgr` reporting functions (`ReportNotExistedId`, `ReportUnusedIds`) to provide context when logging errors about missing or unused loot IDs.
*   **`IsRatesAllowed`:** Returns the `m_ratesAllowed` boolean. This flag controls whether global or command-line loot rate multipliers are applied during the loot generation process. It is checked by `ChatHandler` debug commands and `LootMgr::FillLoot`.

### Internal Management (Protected/Private)

*   **`LoadLootTable`:** Declared as protected, this method is responsible for querying the database and populating `m_LootTemplates`. While the implementation is not in this header, its presence indicates that each `LootStore` instance loads its specific subset of loot data (e.g., creature loot vs. fishing loot) independently.
*   **`Clear`:** Declared as private, this method iterates through `m_LootTemplates` and deletes each `LootTemplate` pointer, then clears the map. It is called by the destructor and likely by reload commands (not shown in this unit but implied by typical server architecture).

## Cross-Unit Boundaries

`LootStore` interacts primarily with the `LootMgr` subsystem and high-level entity managers like `Creature`.

*   **Called by `Creature.Main/SetDeathState`:**
    *   **Direction:** Inbound call to `LootStore::HaveLootFor`.
    *   **Context:** When a creature dies, `Creature::SetDeathState` checks if the creature has loot defined. It calls `HaveLootFor` on the appropriate `LootStore` (likely `LootTemplates_Creature`) using the creature's entry ID or specific loot ID. If `HaveLootFor` returns `true`, the creature proceeds to generate loot; otherwise, it may skip loot generation entirely.

*   **Called by `LootMgr/ExistsRefLootTemplate`:**
    *   **Direction:** Inbound call to `LootStore::HaveLootFor`.
    *   **Context:** `LootMgr` uses this to validate reference loot templates. Some loot entries refer to other templates (references). `ExistsRefLootTemplate` ensures that a referenced ID actually exists in the `LootTemplates_Reference` store before attempting to use it.

*   **Called by `LootMgr/FillLoot`:**
    *   **Direction:** Inbound call to `LootStore::GetName` and `LootStore::IsRatesAllowed`.
    *   **Context:** When filling a `Loot` object, `LootMgr::FillLoot` retrieves the store's name for logging/debugging and checks `IsRatesAllowed` to determine if it should apply rate multipliers to the drop chances.

*   **Called by `LootMgr/IsValid`, `LootMgr/LoadLootTable`, `LootMgr/ReportNotExistedId`, `LootMgr/ReportUnusedIds`, `LootMgr/Verify#3`:**
    *   **Direction:** Inbound call to `LootStore::GetName`.
    *   **Context:** These `LootMgr` functions use `GetName` to tag log messages with the specific loot type (e.g., "Creature", "Fishing") when validating data integrity, loading tables, or reporting errors.

*   **Called by `LootMgr/ReportNotExistedId`, `LootMgr/ReportUnusedIds`:**
    *   **Direction:** Inbound call to `LootStore::GetEntryName`.
    *   **Context:** Similar to `GetName`, but `GetEntryName` provides the specific database column or entry name used for more precise error reporting regarding missing or unused IDs.

*   **Called by `ChatHandler.DebugCommands/HandleDebugLootTableCommand`:**
    *   **Direction:** Inbound call to `LootStore::IsRatesAllowed`.
    *   **Context:** Debug commands allow administrators to inspect loot tables. `IsRatesAllowed` is queried to inform the admin whether rates are active for that specific loot type.

## Data Model

`LootStore` itself does not directly execute SQL queries in the provided header. However, it manages data loaded from database tables corresponding to the various loot types. Based on the global instances declared (`LootTemplates_Creature`, `LootTemplates_Fishing`, etc.), the underlying tables typically follow a pattern such as:

*   `creature_loot_template`
*   `fishing_loot_template`
*   `gameobject_loot_template`
*   `item_loot_template`
*   `mail_loot_template`
*   `pickpocketing_loot_template`
*   `skinning_loot_template`
*   `disenchant_loot_template`
*   `reference_loot_template`

Each table generally contains columns for:
*   `Entry`: The ID linking to the creature/gameobject/item.
*   `Item`: The item ID to drop.
*   `ChanceOrQuestChance`: The probability of dropping.
*   `Group`: Loot group ID.
*   `ConditionId`: Conditional loot requirements.
*   `MinCountOrRef`: Minimum count or reference ID.
*   `MaxCount`: Maximum count.

`LootStore` maps these rows into `LootTemplate` objects, which contain `LootStoreItem` structs. The `LootStore` instance holds the mapping from `Entry` (or `loot_id`) to the `LootTemplate`.

## Notable Implementation Details

1.  **Global Instances:** The header declares several global `LootStore` instances (e.g., `LootTemplates_Creature`). This design allows different parts of the server to access loot data for specific contexts without needing to pass around store pointers. The `LoadLootTables` function initializes all these stores.

2.  **Separation of Concerns:** `LootStore` is strictly a registry. It does not contain logic for rolling dice, checking player permissions, or handling loot windows. That logic is delegated to `LootTemplate` (for processing templates) and `Loot` (for managing the actual loot window state). This separation makes the code modular and easier to maintain.

3.  **Memory Management:** The destructor explicitly calls `Clear()`, which deletes `LootTemplate` pointers. This indicates that `LootStore` owns the `LootTemplate` objects. Care must be taken to ensure that `LootTemplate` objects are not accessed after the `LootStore` is destroyed or cleared.

4.  **Rate Flag:** The `m_ratesAllowed` flag is crucial for gameplay balance. Some loot types (like fishing or disenchanting) might not respect global loot rate multipliers, while creature loot does. `IsRatesAllowed` provides this distinction.

5.  **Const Correctness:** Methods like `HaveLootFor`, `GetName`, `GetEntryName`, and `IsRatesAllowed` are marked `const`, ensuring they do not modify the state of the `LootStore`. This allows them to be called on const references, which is important for thread safety and API design.

6.  **Reference Handling:** The existence of `LootTemplates_Reference` and the `ExistsRefLootTemplate` function suggests that loot templates can reference other templates. `LootStore` plays a role in validating these references exist before they are used, preventing crashes or undefined behavior from dangling references.

## Member Reference

**LootStore**
Constructs a `LootStore` instance with a name, entry name, and rate allowance flag. Initializes the internal template map.

**~LootStore**
Destroys the `LootStore` instance. Calls `Clear()` to delete all managed `LootTemplate` objects and prevent memory leaks.

**HaveLootFor**
Checks if a specific `loot_id` exists in the store's template map. Returns `true` if found, `false` otherwise. Used by `Creature::SetDeathState` and `LootMgr::ExistsRefLootTemplate` to determine if loot generation should proceed.

**GetName**
Returns the descriptive name of the loot store (e.g., "Creature"). Used by `LootMgr` functions for logging, validation, and error reporting.

**GetEntryName**
Returns the entry name associated with the loot store, often used for database-specific identification. Used by `LootMgr` reporting functions to provide context in error messages.

**IsRatesAllowed**
Returns a boolean indicating whether loot rate multipliers apply to this store. Checked by `ChatHandler` debug commands and `LootMgr::FillLoot` to determine if rates should be applied during loot generation.

---

<!-- machine-true, projected from graph.json -->

## Map — LootStore

*Source:* LootMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| LootStore | ctor | — | — | — |
| ~LootStore | dtor | — | — | — |
| HaveLootFor | method | — | Creature.Main/SetDeathState, LootMgr/ExistsRefLootTemplate | — |
| GetName | method | — | LootMgr/FillLoot, LootMgr/IsValid, LootMgr/LoadLootTable, LootMgr/ReportNotExistedId, LootMgr/ReportUnusedIds, LootMgr/Verify#3 | — |
| GetEntryName | method | — | LootMgr/ReportNotExistedId, LootMgr/ReportUnusedIds | — |
| IsRatesAllowed | method | — | ChatHandler.DebugCommands/HandleDebugLootTableCommand, LootMgr/FillLoot | — |
