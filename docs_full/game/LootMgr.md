# LootMgr

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# LootMgr

## Purpose & Responsibilities

`LootMgr` is the central subsystem responsible for defining, loading, validating, and generating loot tables in the WoW server emulation. It manages the entire lifecycle of loot data: from parsing SQL tables (`*_loot_template`) at startup or reload, to resolving complex dependencies like reference templates and conditional drops, to the runtime generation of specific items for a player or group based on probabilities, quest status, and group permissions.

The unit defines three primary layers of abstraction:
1.  **Data Storage (`LootStore`)**: Manages the in-memory representation of specific loot table types (e.g., Creature, Fishing, Disenchant). It handles loading from the database, verifying integrity, and providing lookup interfaces.
2.  **Template Logic (`LootTemplate` & `LootGroup`)**: Encapsulates the rules for how loot is generated. It handles grouping (mutually exclusive items), references (nested loot tables), and chance calculations.
3.  **Runtime Loot Instance (`Loot`)**: Represents the actual loot generated for a specific kill or event. It tracks which items were rolled, who is allowed to loot them, handles group distribution logic (Need/Greed, Master Loot, Personal Loot), and serializes the loot window data to the client.

Key responsibilities include:
*   **Database Integration**: Loading loot definitions from multiple specialized tables, filtering by game patch, and excluding forbidden items.
*   **Validation**: Detecting circular references, unused templates, invalid chances, and missing item prototypes during load time.
*   **Probability Resolution**: Calculating drop chances based on item quality, global rate modifiers, and specific group weights.
*   **Permission Enforcement**: Determining visibility and accessibility of items based on player quests, group roles, and item flags (e.g., Free-for-All, Party Loot).
*   **Client Communication**: Serializing loot windows into `ByteBuffer` packets, respecting client limits (16 standard items, 32 quest items) and permission levels.

## Member-by-Member Behavior

### Data Loading and Validation

The system loads loot data from distinct database tables into separate `LootStore` instances. Each store type corresponds to a specific game mechanic (e.g., killing a creature, disenchanting an item).

*   **`LoadLootTable`**: The core method for populating a `LootStore`. It executes a SQL query against the store's specific table (e.g., `creature_loot_template`). The query filters entries based on the current `World` patch version and excludes items listed in the `forbidden_items` table. For each row, it constructs a `LootStoreItem`, validates its constraints (e.g., `maxcount` limits, condition existence), and inserts it into the appropriate `LootTemplate`. After loading, it calls `Verify()` to check for logical errors like total group chances exceeding 100%.
*   **`LoadAndCollectLootIds`**: A convenience wrapper that calls `LoadLootTable` and then collects all unique loot IDs (entries) from the loaded templates into a provided `LootIdSet`. This is used by higher-level loaders to track which IDs are defined.
*   **`Clear`**: Destroys all `LootTemplate` objects in the store and clears the internal map. This is essential for hot-reloading loot tables without restarting the server.
*   **`Verify`**: Iterates through all templates in the store and calls their individual `Verify` methods. This ensures that every template adheres to logical constraints (e.g., group chances summing correctly).
*   **`CheckLootRefs`**: Validates reference integrity. It scans all templates for entries that point to other templates via `mincountOrRef < 0`. It checks if the referenced template exists in `LootTemplates_Reference`. If a reference is found, it removes the ID from the provided `ref_set` (used to track used references). If a reference is missing, it logs an error.
*   **`ReportUnusedIds`**: Logs warnings for loot IDs that were loaded from the database but are not referenced by any game object, creature, or other loot template. This helps maintain database cleanliness.
*   **`ReportNotExistedId`**: Logs an error when a game entity (like a creature) references a loot ID that does not exist in the corresponding loot table.

### Template Processing and Generation

Once loaded, loot templates are processed to generate actual loot instances.

*   **`AddEntry`**: Called during the loading phase to insert a `LootStoreItem` into a `LootTemplate`. If the item belongs to a group (`group > 0`), it is added to the corresponding `LootGroup`. Otherwise, it is added to the main `Entries` list.
*   **`Process`**: The main entry point for generating loot from a template. It iterates through non-grouped entries, rolling for each one. If an entry is a reference (`mincountOrRef < 0`), it recursively processes the referenced template. Finally, it processes all `LootGroups`.
*   **`HasQuestDrop` / `HasQuestDropForPlayer`**: Utility methods to check if a template contains any quest-related items. `HasQuestDropForPlayer` further filters this by checking if the player has an active quest for any of the items. These are used to optimize loot generation and UI display.
*   **`Verify` (LootTemplate)**: Checks the integrity of the template's groups, ensuring that the sum of chances in any group does not exceed reasonable limits.
*   **`CheckLootRefs` (LootTemplate)**: Delegates reference checking to its contained `LootGroups` and direct entries.

### LootGroup Logic

`LootGroup` handles mutually exclusive items within a template.

*   **`AddEntry`**: Adds an item to the group. Items with explicit chances go to `ExplicitlyChanced`; items with 0 chance go to `EqualChanced`.
*   **`Roll`**: Selects an item from the group. It first attempts to roll against explicitly chanced items. If no item is selected, it picks randomly from the equal-chanced items, filtering out those that don't meet team conditions if necessary.
*   **`Process`**: Calls `Roll` and, if an item is selected, adds it to the `Loot` instance via `Loot::AddItem`.
*   **`RawTotalChance` / `TotalChance`**: Calculates the aggregate chance of the group. `TotalChance` caps at 100% if equal-chanced items are present.
*   **`Verify`**: Ensures the group's total chance is valid.

### Runtime Loot Management (`Loot` Class)

The `Loot` struct represents the actual loot generated for a specific event.

*   **`FillLoot`**: Initializes the loot instance. It retrieves the appropriate `LootTemplate` from the store, calls `Process` to generate base items, and then calls `FillPlayerDependentLoot` to handle player-specific items (quests, FFA).
*   **`FillPlayerDependentLoot`**: Handles the distribution of loot for groups. It identifies allowed looters, sets the round-robin player, and determines which items are under the group's loot threshold. It then calls `FillNotNormalLootFor` for each eligible player.
*   **`FillNotNormalLootFor`**: Dispatches to specific fillers for Quest, FFA (Free-For-All), and Conditional items for a specific player.
*   **`FillQuestLoot` / `FillFFALoot` / `FillNonQuestNonFFAConditionalLoot`**: These methods iterate through the generated items and populate player-specific maps (`m_playerQuestItems`, etc.) if the player meets the criteria (e.g., has the quest, is in the group). They also update the `unlootedCount` and block items if necessary.
*   **`AddItem`**: Called by template processors to add an item to the loot. It distinguishes between quest items and normal items, enforcing slot limits (`MAX_NR_QUEST_ITEMS`, `MAX_NR_LOOT_ITEMS`).
*   **`LootItemInSlot`**: Resolves a slot index to a specific `LootItem` for a player. This is complex because slot indices differ between players due to personalized quest/FFA items. It checks the player's specific maps to find the correct item.
*   **`GetMaxSlotInLootFor`**: Returns the total number of slots visible to a specific player, accounting for their unique quest/FFA items.
*   **`NotifyItemRemoved` / `NotifyMoneyRemoved` / `NotifyQuestItemRemoved`**: Sends notifications to all players currently looting the object when an item or money is taken. This keeps the loot windows synchronized across the group.
*   **`GenerateMoneyLoot`**: Calculates the gold amount based on min/max values and the global money drop rate configuration.
*   **`AllowedForPlayer`**: Checks if a specific `LootItem` is visible and accessible to a player. It verifies conditions, quest status, and ownership.
*   **`AllowedForTeam`**: Checks if an item is compatible with the group's faction/team composition, primarily for conditional drops.
*   **`GetSlotTypeForSharedLoot`**: Determines the visual state of a loot slot (e.g., Allow Loot, Roll Ongoing, Master Only) based on the player's permission and the item's state.
*   **`operator<<` (LootView)**: Serializes the loot data into a `ByteBuffer` for transmission to the client. It carefully constructs the packet based on the viewer's permission level, hiding items they shouldn't see and marking slots appropriately.

### Global Loader Functions

These functions coordinate the loading of all loot tables and perform final validation.

*   **`LoadLootTables`**: Orchestrates the loading of all specific loot stores (Creature, Fishing, etc.).
*   **`LoadLootTemplates_*`**: Specific loaders for each loot type. They load the data, collect IDs, and then cross-reference these IDs with the relevant game entities (e.g., `CreatureInfo`, `ItemPrototype`) to identify unused or missing templates.
*   **`CheckLootTemplates_Reference`**: Performs a final pass to ensure all reference templates are used and valid.
*   **`ExistsRefLootTemplate`**: Quick check for the existence of a reference template.

## Cross-Unit Boundaries

### Database Interaction
*   **`LoadLootTable`** queries the World Database using `Database/PQuery`. It reads from tables like `creature_loot_template` and filters against `forbidden_items`.
*   **`LoadLootTable`** uses `Field/Get...` methods to parse the result set.
*   **`LoadLootTable`** uses `ProgressBar/BarGoLink` to display loading progress.

### Object Manager Integration
*   **`LoadLootTemplates_Creature`**, **`LoadLootTemplates_Pickpocketing`**, **`LoadLootTemplates_Skinning`** call `ObjectMgr/GetCreatureInfoMap` to verify that loaded loot IDs correspond to existing creatures.
*   **`LoadLootTemplates_Disenchant`**, **`LoadLootTemplates_Item`** call `ObjectMgr/GetItemPrototypeMap` to verify item-based loot IDs.
*   **`LoadLootTemplates_Gameobject`** calls `ObjectMgr/GetGameObjectInfoMap` and `GameObjectInfo/GetLootId` to verify game object loot IDs.
*   **`LootStoreItem::Roll`**, **`LootStoreItem::IsValid`**, **`LootItem::AllowedForPlayer`**, **`Loot::AddItem`**, **`Loot::FillPlayerDependentLoot`**, **`Loot::LootItemInSlot`**, **`operator<<`** all call `ObjectMgr/GetItemPrototype` to retrieve item details (quality, flags, display info) for chance calculation, validation, and serialization.
*   **`LootItem::AllowedForPlayer`** calls `ObjectMgr/GetQuestTemplate` to check quest properties.

### Player and Group Interaction
*   **`LootItem::AllowedForPlayer`** calls `Player.Main/HasQuestForItem`, `Player.Main/GetQuestStatus`, and `Player.Main/GetGroup` to determine player eligibility.
*   **`Loot::FillPlayerDependentLoot`** calls `Player.Main/GetGroup`, `Group/GetFirstMember`, `Group/GetLootThreshold`, and `Player.Main/IsAtGroupRewardDistance` to manage group loot distribution.
*   **`Loot::NotifyItemRemoved`**, **`Loot::NotifyMoneyRemoved`**, **`Loot::NotifyQuestItemRemoved`** call `ObjectAccessor/FindPlayer` and `Player.Main/SendNotifyLootItemRemoved`/`SendNotifyLootMoneyRemoved` to update clients.
*   **`Loot::hasItemFor`**, **`Loot::hasOverThresholdItem`** are called by `Player.Main/IsAllowedToLoot` to determine if a player can initiate looting.
*   **`LootItem::AllowedForPlayer`** is called by `Player.Main/AutoStoreLoot` and `WorldSession.LootHandler/HandleAutostoreLootItemOpcode` to validate automatic looting actions.
*   **`Loot::IsAllowedLooter`** is called by `game_Group_Group/MasterLoot`, `game_Group_Group/StartLootRoll`, and `Player.Main/IsAllowedToLoot` to enforce group permissions.

### Condition System
*   **`LoadLootTable`** calls `Conditions/CanBeUsedWithoutPlayer` to validate condition types during loading.
*   **`LootItem::AllowedForPlayer`** calls `Conditions/IsConditionSatisfied` to check if a player meets the requirements for a conditional drop.
*   **`LootStoreItem::AllowedForTeam`** calls `Conditions/GetTeam`, `Conditions/Meets`, and `Conditions/CanBeUsedWithoutPlayer` to check team-based conditions.

### Logging
*   **`LoadLootTable`**, **`LootStoreItem::IsValid`**, **`LootTemplate::LootGroup::Verify`**, **`LootStore::ReportUnusedIds`**, **`LootStore::ReportNotExistedId`**, **`Loot::FillLoot`** all use `Log.Main/Out` to report errors, warnings, and informational messages during loading and runtime.

## Data Model

The `LootMgr` interacts with several database tables, though only `forbidden_items` is explicitly detailed in the schema. The other tables are inferred from the SQL queries in `LoadLootTable`.

### `forbidden_items`
This table lists items that are banned from dropping in certain game patches.
*   **`entry`** (mediumint unsigned, PK): The item entry ID.
*   **`patch`** (tinyint unsigned, PK): The game patch version.
*   **`after_or_before`** (tinyint unsigned, PK): Flag indicating if the ban applies to patches after (1) or before (0) the specified patch.

### Inferred Loot Tables
The following tables are queried by `LoadLootTable`. Their structure is consistent across types, differing only in the `entry` column's semantic meaning.
*   **Columns**: `entry`, `item`, `ChanceOrQuestChance`, `groupid`, `mincountOrRef`, `maxcount`, `condition_id`.
*   **Tables**:
    *   `creature_loot_template`: Loot dropped by creatures.
    *   `disenchant_loot_template`: Loot from disenchanting items.
    *   `fishing_loot_template`: Loot from fishing.
    *   `gameobject_loot_template`: Loot from game objects.
    *   `item_loot_template`: Loot from opening items (e.g., bags, chests).
    *   `mail_loot_template`: Loot attached to mail templates.
    *   `pickpocketing_loot_template`: Loot from pickpocketing.
    *   `skinning_loot_template`: Loot from skinning.
    *   `reference_loot_template`: Nested loot templates referenced by other tables.

## Notable Implementation Details

### Patch-Based Filtering
`LoadLootTable` filters loot entries based on the current `World` patch version. It uses `patch_min` and `patch_max` columns (implied in the query, though not in the provided schema snippet for `forbidden_items`) to ensure only relevant loot is loaded. Additionally, it actively excludes items listed in `forbidden_items` based on the `after_or_before` flag.

### Reference Templates
Loot templates can reference other templates via a negative `mincountOrRef` value. This allows for modular loot design (e.g., a common "trash loot" template referenced by many creatures). `LootTemplate::Process` handles this recursion. `CheckLootRefs` ensures these references are valid and not circular or dangling.

### Grouped Loot
Items can be grouped (`groupid > 0`). Within a group, only one item is rolled. `LootGroup::Roll` implements this logic, prioritizing explicitly chanced items over equal-chanced ones. This is crucial for implementing "one of X" drop mechanics.

### Conditional Drops
Items can have associated `condition_id`s. `LootItem::AllowedForPlayer` and `LootStoreItem::AllowedForTeam` evaluate these conditions at runtime. This allows for dynamic loot based on player quests, faction, or other game states.

### Client Slot Limits
The client has strict limits on the number of items it can display: 16 for standard loot and 32 for quest loot. `Loot::AddItem` enforces these limits. If a player has many quest items, they might not see all of them if the limit is reached. `Loot::GetMaxSlotInLootFor` calculates the effective slot count for a specific player.

### Personal vs. Group Loot
`Loot::FillPlayerDependentLoot` distinguishes between personal loot (where each player sees their own potential drops) and group loot (where items are shared). In group mode, it identifies the "round-robin" player and marks items below the group's loot threshold as `is_underthreshold`.

### Serialization Complexity
The `operator<<` for `LootView` is complex because it must serialize different views of the same loot for different players. It iterates through standard items, quest items, FFA items, and conditional items, applying permission checks and slot type markers for each. This ensures that players only see what they are allowed to see and that the UI reflects the correct interaction states (e.g., "Need/Greed" roll vs. "Take").

## Member Reference

**LootGroup**
Constructor for the `LootGroup` class, initializing the `hasConditionalEqualChancedItem` flag.

**Clear**
Destroys all `LootTemplate` objects in the `LootStore` and clears the internal map, preparing for a reload.

**Verify**
Iterates through all templates in the `LootStore` and calls their individual `Verify` methods to ensure logical consistency.

**LoadLootTable**
Loads loot definitions from the database table associated with the `LootStore`. It filters by patch and forbidden items, validates entries, and populates the `LootTemplate` structures.

**HaveQuestLootFor**
Checks if a specific loot ID contains any quest-related items.

**HaveQuestLootForPlayer**
Checks if a specific loot ID contains any quest-related items for which the given player has an active quest.

**GetLootFor**
Retrieves the `LootTemplate` pointer for a given loot ID.

**LoadAndCollectLootIds**
Loads the loot table and collects all unique loot IDs into a provided set.

**CheckLootRefs**
Validates reference integrity by checking if referenced templates exist and removing used IDs from a tracking set.

**ReportUnusedIds**
Logs warnings for loot IDs that are defined in the database but not referenced by any game entity.

**ReportNotExistedId**
Logs an error when a game entity references a loot ID that does not exist in the database.

**Roll**
Determines if a `LootStoreItem` drops based on its chance, item quality, and global rate modifiers.

**IsValid**
Validates the constraints of a `LootStoreItem`, such as group size, min/max counts, and item prototype existence.

**LootItem**
Constructor that creates a runtime `LootItem` from a `LootStoreItem`, generating random count and property IDs.

**LootItem#2**
Constructor that creates a `LootItem` with explicit item ID, count, and property ID.

**AllowedForPlayer**
Checks if a `LootItem` is visible and accessible to a specific player, considering conditions, quests, and ownership.

**AllowedForTeam**
Checks if a `LootStoreItem` is compatible with the group's faction/team composition.

**LoadLootTables**
Orchestrates the loading of all specific loot stores.

**GetSlotTypeForSharedLoot**
Determines the visual state of a loot slot for a player based on permissions and item state.

**AddItem**
Adds a `LootStoreItem` to the `Loot` instance, distinguishing between quest and normal items.

**FillLoot**
Initializes the `Loot` instance by retrieving the template, processing it, and filling player-dependent items.

**FillPlayerDependentLoot**
Handles the distribution of loot for groups, identifying allowed looters and setting thresholds.

**IsAllowedLooter**
Checks if a player is allowed to loot the current `Loot` instance.

**FillNotNormalLootFor**
Dispatches to specific fillers for Quest, FFA, and Conditional items for a specific player.

**FillFFALoot**
Populates the FFA item map for a player.

**FillQuestLoot**
Populates the quest item map for a player.

**FillNonQuestNonFFAConditionalLoot**
Populates the conditional item map for a player.

**NotifyItemRemoved**
Sends notifications to all looting players when an item is removed.

**NotifyMoneyRemoved**
Sends notifications to all looting players when money is removed.

**NotifyQuestItemRemoved**
Sends notifications to all looting players when a quest item is removed.

**GenerateMoneyLoot**
Calculates the gold amount based on min/max values and global rates.

**LootItemInSlot**
Resolves a slot index to a specific `LootItem` for a player, handling personalized slots.

**GetMaxSlotInLootFor**
Returns the total number of slots visible to a specific player.

**GetLootTarget**
Returns the world object being looted, resolving corpses to their owners if applicable.

**operator<<**
Serializes a `LootItem` into a `ByteBuffer`.

**operator<<#2**
Serializes a `LootView` into a `ByteBuffer`, constructing the loot window packet for the client.

**hasOverThresholdItem**
Checks if there are any unlooted items above the group's loot threshold.

**hasItemFor**
Checks if a player has any unlooted FFA, quest, or conditional items.

**AddEntry#2**
Adds an entry to a `LootGroup` during the loading stage.

**Roll#2**
Selects an item from a `LootGroup` based on chances and team conditions.

**HasQuestDrop#2**
Checks if a `LootGroup` contains any quest drops.

**HasQuestDropForPlayer#2**
Checks if a `LootGroup` contains any quest drops for a specific player.

**Process#2**
Rolls an item from a `LootGroup` and adds it to the `Loot` instance.

**RawTotalChance**
Calculates the aggregate chance of a `LootGroup` without equal-chanced items.

**TotalChance**
Calculates the aggregate chance of a `LootGroup`, capping at 100% if equal-chanced items are present.

**Verify#3**
Verifies the integrity of a `LootGroup`, checking for excessive chances.

**CheckLootRefs#3**
Checks reference integrity for a `LootGroup`.

**AddEntry**
Adds a `LootStoreItem` to a `LootTemplate`, placing it in a group or the main entry list.

**Process**
Generates loot from a `LootTemplate`, handling references and groups.

**HasQuestDrop**
Checks if a `LootTemplate` contains any quest drops.

**HasQuestDropForPlayer**
Checks if a `LootTemplate` contains any quest drops for a specific player.

**Verify#2**
Verifies the integrity of a `LootTemplate`.

**CheckLootRefs#2**
Checks reference integrity for a `LootTemplate`.

**LoadLootTemplates_Creature**
Loads and validates creature loot templates.

**LoadLootTemplates_Disenchant**
Loads and validates disenchant loot templates.

**LoadLootTemplates_Fishing**
Loads and validates fishing loot templates.

**LoadLootTemplates_Gameobject**
Loads and validates game object loot templates.

**LoadLootTemplates_Item**
Loads and validates item loot templates.

**LoadLootTemplates_Pickpocketing**
Loads and validates pickpocketing loot templates.

**LoadLootTemplates_Mail**
Loads and validates mail loot templates.

**LoadLootTemplates_Skinning**
Loads and validates skinning loot templates.

**LoadLootTemplates_Reference**
Loads reference loot templates and collects their IDs.

**CheckLootTemplates_Reference**
Validates reference templates, ensuring they are used and not circular.

**ExistsRefLootTemplate**
Checks if a reference loot template exists.

---

<!-- machine-true, projected from graph.json -->

## Map — LootMgr

*Source:* LootMgr.cpp, LootMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| LootGroup | ctor | — | — | — |
| Clear | method | — | — | — |
| Verify | method | — | — | — |
| LoadLootTable | method | Conditions/CanBeUsedWithoutPlayer, Database/PQuery, Field/GetFloat, Field/GetInt32, Field/GetUInt16, Field/GetUInt32, Field/GetUInt8, Log.Main/Out, LootStore/GetName, LootStoreItem/LootStoreItem, ProgressBar/BarGoLink#3, ProgressBar/step, QueryResult/Fetch, QueryResult/GetRowCount, QueryResult/NextRow, World/GetWowPatch | — | forbidden_items |
| HaveQuestLootFor | method | — | ObjectMgr/LoadGameObjectForQuests | — |
| HaveQuestLootForPlayer | method | — | GameObject/ActivateToQuest | — |
| GetLootFor | method | — | ChatHandler.DebugCommands/HandleDebugLootTableCommand | — |
| LoadAndCollectLootIds | method | — | — | — |
| CheckLootRefs | method | — | ChatHandler.ServerCommands/HandleReloadLootTemplatesCreatureCommand, ChatHandler.ServerCommands/HandleReloadLootTemplatesDisenchantCommand, ChatHandler.ServerCommands/HandleReloadLootTemplatesFishingCommand, ChatHandler.ServerCommands/HandleReloadLootTemplatesGameobjectCommand, ChatHandler.ServerCommands/HandleReloadLootTemplatesItemCommand, ChatHandler.ServerCommands/HandleReloadLootTemplatesMailCommand, ChatHandler.ServerCommands/HandleReloadLootTemplatesPickpocketingCommand, ChatHandler.ServerCommands/HandleReloadLootTemplatesSkinningCommand | — |
| ReportUnusedIds | method | Log.Main/Out, LootStore/GetEntryName, LootStore/GetName | — | — |
| ReportNotExistedId | method | Log.Main/Out, LootStore/GetEntryName, LootStore/GetName | — | — |
| Roll | method | ObjectMgr/GetItemPrototype, shared_Util/roll_chance_f, World/getConfig#2 | — | — |
| IsValid | method | Log.Main/Out, LootStore/GetName, ObjectMgr/GetItemPrototype | — | — |
| LootItem | ctor | game_Objects_Item/GenerateItemRandomPropertyId, ObjectMgr/GetItemPrototype, shared_Util/urand | — | — |
| LootItem#2 | ctor | ObjectMgr/GetItemPrototype | game_Objects_Item/LoadLootFromDB | — |
| AllowedForPlayer | method | Conditions/IsConditionSatisfied, ItemPrototype/HasExtraFlag, Object/GetObjectGuid, ObjectGuid/IsEmpty, ObjectGuid/operator==, ObjectMgr/GetItemPrototype, ObjectMgr/GetQuestTemplate, Player.Main/GetQuestStatus, Player.Main/HasQuestForItem, QuestDef/IsRepeatable, WorldObject.Object/GetMap | game_Group_Group/StartLootRoll, Player.Main/AutoStoreLoot, WorldSession.LootHandler/HandleAutostoreLootItemOpcode | — |
| AllowedForTeam | method | Conditions/CanBeUsedWithoutPlayer, Conditions/GetTeam, Conditions/Meets, Loot/GetTeam, WorldObject.Object/FindMap | — | — |
| LoadLootTables | function | — | ChatHandler.ServerCommands/HandleReloadAllLootCommand, World/SetInitialWorldSettings | — |
| GetSlotTypeForSharedLoot | method | — | — | — |
| AddItem | method | ObjectMgr/GetItemPrototype | — | — |
| FillLoot | method | Log.Main/Out, LootStore/GetName, LootStore/IsRatesAllowed | AiBotAI.Bridge/BridgeHandleUseGameObject, Creature.Main/GenerateLootForBody, GameObject/getFishLoot, game_Mail_Mail/prepareItems, game_Mail_Mail/prepareTemplateItems, Player.Main/SendLoot | — |
| FillPlayerDependentLoot | method | Group/GetFirstMember, Group/GetLootThreshold, GroupReference/next, Object/GetGUID, Object/GetObjectGuid, Object/IsInWorld, ObjectMgr/GetItemPrototype, Player.Main/GetGroup, Player.Main/IsAtGroupRewardDistance | Creature.Main/GeneratePlayerDependentLoot | — |
| IsAllowedLooter | method | ObjectGuid/operator== | game_Group_Group/MasterLoot, game_Group_Group/StartLootRoll, Player.Main/IsAllowedToLoot, WorldSession.LootHandler/HandleLootMasterGiveOpcode | — |
| FillNotNormalLootFor | method | Object/GetGUIDLow, Object/GetObjectGuid, Object/IsInWorld | ChatHandler.DebugCommands/HandleDebugLootTableCommand, Player.Main/SendLoot | — |
| FillFFALoot | method | Object/GetGUIDLow, Object/IsInWorld | — | — |
| FillQuestLoot | method | Object/GetGUIDLow, Object/IsInWorld | — | — |
| FillNonQuestNonFFAConditionalLoot | method | Object/GetGUIDLow, Object/IsInWorld | — | — |
| NotifyItemRemoved | method | ObjectAccessor/FindPlayer, Player.Main/SendNotifyLootItemRemoved | game_Group_Group/CountSingleLooterRoll, game_Group_Group/CountTheRoll, WorldSession.LootHandler/HandleAutostoreLootItemOpcode, WorldSession.LootHandler/HandleLootMasterGiveOpcode | — |
| NotifyMoneyRemoved | method | ObjectAccessor/FindPlayer, Player.Main/SendNotifyLootMoneyRemoved | WorldSession.LootHandler/HandleLootMoneyOpcode | — |
| NotifyQuestItemRemoved | method | Object/GetGUIDLow, ObjectAccessor/FindPlayer, Player.Main/SendNotifyLootItemRemoved | WorldSession.LootHandler/HandleAutostoreLootItemOpcode | — |
| GenerateMoneyLoot | method | shared_Util/urand, World/getConfig#2 | Creature.Main/GenerateLootForBody, Player.Main/SendLoot | — |
| LootItemInSlot | method | — | game_Mail_Mail/prepareItems, game_Mail_Mail/prepareTemplateItems, game_Objects_Item/SaveToDB, Player.Main/AutoStoreLoot, WorldSession.LootHandler/HandleAutostoreLootItemOpcode | — |
| GetMaxSlotInLootFor | method | — | game_Mail_Mail/prepareItems, game_Mail_Mail/prepareTemplateItems, game_Objects_Item/SaveToDB, Player.Main/AutoStoreLoot | — |
| GetLootTarget | method | Corpse/GetOwnerGuid, Object/ToCorpse#2, ObjectAccessor/FindPlayer | Player.Main/AutoStoreLoot, Player.Main/LootMoney, WorldSession.LootHandler/HandleAutostoreLootItemOpcode | — |
| operator<< | function | ByteBuffer/operator<<#10, ObjectMgr/GetItemPrototype | — | — |
| operator<<#2 | function | ByteBuffer/operator<<#10, ByteBuffer/operator<<#7, ByteBuffer/wpos, Loot/GetPlayerFFAItems, Loot/GetPlayerNonQuestNonFFAConditionalItems, Loot/GetPlayerQuestItems, Object/GetGUID, Object/GetGUIDLow | Player.Main/SendLoot | — |
| hasOverThresholdItem | method | — | Player.Main/IsAllowedToLoot | — |
| hasItemFor | method | Loot/GetPlayerFFAItems, Loot/GetPlayerNonQuestNonFFAConditionalItems, Loot/GetPlayerQuestItems, Object/GetGUIDLow | Player.Main/IsAllowedToLoot | — |
| AddEntry#2 | method | — | — | — |
| Roll#2 | method | Loot/GetTeam, shared_Util/irand, shared_Util/rand_chance_f, shared_Util/urand | — | — |
| HasQuestDrop#2 | method | — | — | — |
| HasQuestDropForPlayer#2 | method | Player.Main/HasQuestForItem | — | — |
| Process#2 | method | — | — | — |
| RawTotalChance | method | — | — | — |
| TotalChance | method | — | — | — |
| Verify#3 | method | Log.Main/Out, LootStore/GetName | — | — |
| CheckLootRefs#3 | method | — | — | — |
| AddEntry | method | — | — | — |
| Process | method | — | ChatHandler.DebugCommands/HandleDebugLootTableCommand | — |
| HasQuestDrop | method | — | — | — |
| HasQuestDropForPlayer | method | Player.Main/HasQuestForItem | — | — |
| Verify#2 | method | — | — | — |
| CheckLootRefs#2 | method | — | — | — |
| LoadLootTemplates_Creature | function | ObjectMgr/GetCreatureInfoMap | ChatHandler.ServerCommands/HandleReloadLootTemplatesCreatureCommand | — |
| LoadLootTemplates_Disenchant | function | ObjectMgr/GetItemPrototypeMap | ChatHandler.ServerCommands/HandleReloadLootTemplatesDisenchantCommand | — |
| LoadLootTemplates_Fishing | function | — | ChatHandler.ServerCommands/HandleReloadLootTemplatesFishingCommand | — |
| LoadLootTemplates_Gameobject | function | GameObjectInfo/GetLootId, ObjectMgr/GetGameObjectInfoMap | ChatHandler.ServerCommands/HandleReloadLootTemplatesGameobjectCommand | — |
| LoadLootTemplates_Item | function | Log.Main/HasLogFilter, ObjectMgr/GetItemPrototypeMap | ChatHandler.ServerCommands/HandleReloadLootTemplatesItemCommand | — |
| LoadLootTemplates_Pickpocketing | function | ObjectMgr/GetCreatureInfoMap | ChatHandler.ServerCommands/HandleReloadLootTemplatesPickpocketingCommand | — |
| LoadLootTemplates_Mail | function | SQLStorage/GetMaxEntry | ChatHandler.ServerCommands/HandleReloadLootTemplatesMailCommand | — |
| LoadLootTemplates_Skinning | function | ObjectMgr/GetCreatureInfoMap | ChatHandler.ServerCommands/HandleReloadLootTemplatesSkinningCommand | — |
| LoadLootTemplates_Reference | function | — | ChatHandler.ServerCommands/HandleReloadLootTemplatesReferenceCommand | — |
| CheckLootTemplates_Reference | function | BattleGroundMgr/GetUsedRefLootIds | ChatHandler.ServerCommands/HandleReloadLootTemplatesReferenceCommand, World/SetInitialWorldSettings | — |
| ExistsRefLootTemplate | function | LootStore/HaveLootFor | BattleGroundMgr/CreateInitialBattleGrounds | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `forbidden_items`: entry mediumint(8) unsigned PK, patch tinyint(3) unsigned, after_or_before tinyint(3) unsigned PK

*`?` = nullable, `PK` = primary key column.*

