# Loot & Gold Drop Rates

<!-- aliases: drop rates, loot rates, more loot, drop chance, item drop rate, more gold, gold drops, money drops, increase drops, gold rate -->
<!-- documentation: model-written from source via the local LLM; review before trusting -->

When a creature dies, the server determines who receives the loot, calculates the specific items and gold dropped, and distributes them. This process relies on the **tapping system** to identify the recipient, the **loot templates** defined in the database to determine *what* drops, and **configuration rates** to scale the probability of those drops and the amount of gold.

The flow begins when a creature's health reaches zero. `Creature.Main/SetDeathState` transitions the creature to a corpse state. If the creature has a valid loot recipient (set via `Creature.Main/SetLootRecipient` during combat tapping), the loot generation proceeds. The recipient is determined by `Creature.Main/GetLootRecipient`, which checks for a group recipient (`Creature.Main/GetGroupLootRecipient`) or an original player recipient (`Creature.Main/GetOriginalLootRecipient`). If no recipient is set, loot is generally not generated for players, though bots may force themselves as recipients via `AiBotAI.Loot/DoAutoLoot`.

Once the recipient is established, `Creature.Main/GenerateLootForBody` is called. This method clears any previous loot and calls `LootMgr/FillLoot` with the creature's `loot_id` from `creature_template`. `LootMgr/FillLoot` retrieves the corresponding `LootTemplate` from the `LootStore` (loaded at startup by `LootMgr/LoadLootTable` from `creature_loot_template` and `reference_loot_template`). During the processing of this template, the server rolls for each item. The base chance defined in the database is multiplied by a quality-specific rate factor retrieved from the configuration (e.g., `Rate.Drop.Item.Rare`). If the roll succeeds, the item is added to the loot object.

Simultaneously, `Creature.Main/GenerateLootForBody` calls `loot.GenerateMoneyLoot`, which uses the `gold_min` and `gold_max` values from `creature_template`. The resulting gold amount is multiplied by the `Rate.Drop.Money` configuration key.

For SuperUiBots, `AiBotAI.Loot/DoAutoLoot` automates this entire sequence. It ensures the bot is set as the recipient if necessary, generates the loot, handles group splits for gold, stores items in the bot's inventory, and signals the external bridge service. Finally, when all items are taken, `Creature.Main/AllLootRemovedFromCorpse` is triggered, starting the timer for corpse decay.

Operators can verify drop rates using `ChatHandler.DebugCommands/HandleDebugLootTableCommand`, which simulates thousands of loot rolls for a given loot ID and reports the empirical drop percentages.

## How to Modify

### Config
The following keys in `mangosd.conf` directly scale drop probabilities and gold amounts. Changes take effect immediately upon reload (`reload config` command) or server restart.

*   **`Rate.Drop.Item.Poor`** (default 1): Multiplier for white (poor quality) item drop chances.
*   **`Rate.Drop.Item.Normal`** (default 1): Multiplier for green (normal quality) item drop chances.
*   **`Rate.Drop.Item.Uncommon`** (default 1): Multiplier for blue (uncommon quality) item drop chances.
*   **`Rate.Drop.Item.Rare`** (default 1): Multiplier for purple (rare quality) item drop chances.
*   **`Rate.Drop.Item.Epic`** (default 1): Multiplier for orange (epic quality) item drop chances.
*   **`Rate.Drop.Item.Legendary`** (default 1): Multiplier for legendary item drop chances.
*   **`Rate.Drop.Item.Artifact`** (default 1): Multiplier for artifact item drop chances.
*   **`Rate.Drop.Item.Referenced`** (default 1): Multiplier for drop chances of items defined in `reference_loot_template`.
*   **`Rate.Drop.Money`** (default 1): Multiplier for the final gold amount dropped by creatures.

Setting `Rate.Drop.Money` to `2` doubles the gold dropped by all creatures. Setting `Rate.Drop.Item.Rare` to `5` makes rare items five times more likely to drop.

### Database
Drop definitions and gold ranges are controlled by database rows. Changes require a server restart or a loot reload command (if supported by your build).

*   **`creature_template`**:
    *   `loot_id`: Links the creature to a row in `creature_loot_template`. Setting this to `0` disables item loot.
    *   `gold_min` / `gold_max`: Defines the range of copper dropped. These values are multiplied by `Rate.Drop.Money`.
*   **`creature_loot_template`**:
    *   `entry`: Must match the `loot_id` in `creature_template`.
    *   `item`: The item ID to drop.
    *   `ChanceOrQuestChance`: The base probability (0–100) for the item to drop. This value is multiplied by the corresponding `Rate.Drop.Item.*` config key based on the item's quality.
    *   `groupid`: If non-zero, items in the same group compete; only one item from the group can drop.
    *   `mincountOrRef`: Number of items to drop if the chance succeeds. If negative, it acts as a reference to `reference_loot_template`.
*   **`reference_loot_template`**:
    *   Used when `mincountOrRef` is negative in `creature_loot_template`. Allows complex loot chains. The drop chance here is also scaled by `Rate.Drop.Item.Referenced`.

To increase gold drops for a specific mob, edit `gold_min` and `gold_max` in `creature_template`. To add a new item drop, insert a row into `creature_loot_template` with the correct `entry` and `item`.

### Code
If you need to change the logic of *how* rates are applied or *who* gets loot, you must edit the source code and rebuild.

*   **`Creature.Main/GenerateLootForBody`** (`Creature.cpp`): Controls the order of loot generation and calls `GenerateMoneyLoot`. You can modify this to change how gold is calculated or to force specific loot behaviors.
*   **`LootMgr/FillLoot`** (`LootMgr.cpp`): Orchestrates the template processing. The actual multiplication of chances by config rates happens inside the `LootTemplate::Process` method (not shown in slices but called here). To change the formula for rate application, look for where `sWorld.getConfig(CONFIG_FLOAT_RATE_DROP_ITEM_*)` is used within the loot processing loop.
*   **`AiBotAI.Loot/DoAutoLoot`** (`AiBotAILoot.cpp`): If you want bots to behave differently regarding loot (e.g., ignore certain items, split gold differently), modify this method. Currently, it splits gold evenly among nearby group members.
*   **`Creature.Main/SetLootRecipient`** (`Creature.cpp`): Modifies who is eligible to loot. Changing the logic here affects tapping rules.

## Path Reference

**GetLootRecipientGuid** (Creature.h) Returns the GUID of the player designated to receive loot.
**GetLootGroupRecipientId** (Creature.h) Returns the ID of the group designated to receive loot.
**HasLootRecipient** (Creature.h) Checks if a loot recipient (player or group) has been set.
**IsGroupLootRecipient** (Creature.h) Checks if the loot recipient is a group.
**IsLootAllowedDueToDamageOrigin** (Creature.h) Determines if loot is allowed based on damage dealt by players vs. non-players.
**GetGroupLootTimer** (Creature.h) Returns the remaining time for group loot distribution.
**StartGroupLoot** (Creature.cpp) Initiates the group loot timer and associates the group ID.
**StopGroupLoot** (Creature.cpp) Ends the group loot timer and cleans up group associations.
**GetOriginalLootRecipient** (Creature.cpp) Retrieves the specific player who originally tapped the creature.
**GetGroupLootRecipient** (Creature.cpp) Retrieves the group object associated with the loot recipient.
**GetLootRecipient** (Creature.cpp) Resolves the final loot recipient, prioritizing the group leader or a member if the original recipient is offline.
**SetLootRecipient** (Creature.cpp) Assigns a player or their group as the loot recipient when they deal damage.
**GenerateLootForBody** (Creature.cpp) Generates the actual loot items and gold for the corpse, calling `FillLoot` and `GenerateMoneyLoot`.
**GeneratePlayerDependentLoot** (Creature.cpp) Generates loot specific to the looter's conditions (e.g., quest items).
**AllLootRemovedFromCorpse** (Creature.cpp) Handles corpse decay timers after all loot has been taken.
**LootStore** (LootMgr.h) Constructs a loot store with a name and rate allowance flag.
**~LootStore** (LootMgr.h) Destroys the loot store and clears templates.
**HaveLootFor** (LootMgr.h) Checks if a loot ID exists in the store.
**GetName** (LootMgr.h) Returns the name of the loot store for logging.
**GetEntryName** (LootMgr.h) Returns the entry name for database identification.
**IsRatesAllowed** (LootMgr.h) Indicates if config rates should be applied to this store's loot.
**LoadAllIdentifiers** (ObjectMgr.cpp) Loads distinct IDs from various tables, including `creature_template`.
**LoadCreatureTemplates** (ObjectMgr.cpp) Queries and loads all creature template data, including `loot_id` and `gold_min/max`.
**LoadCreatureTemplate** (ObjectMgr.cpp) Loads a single creature template by entry.
**CheckCreatureTemplate** (ObjectMgr.cpp) Validates creature template data, warning if gold/loot is set on despawn-instantly creatures.
**LoadCreatureClassLevelStats** (ObjectMgr.cpp) Loads creature stats, not directly related to loot but part of template loading.
**LoadTrainers#2** (ObjectMgr.cpp) Loads trainer spells, unrelated to loot.
**LoadTrainerTemplates** (ObjectMgr.cpp) Loads trainer templates, unrelated to loot.
**LoadVendorTemplates** (ObjectMgr.cpp) Loads vendor templates, unrelated to loot.
**LoadConfigSettings** (World.cpp) Reads `Rate.Drop.Item.*` and `Rate.Drop.Money` from `mangosd.conf`.
**DoAutoLoot** (AiBotAILoot.cpp) Automates the looting process for AI bots, including gold splitting and item storage.
**HandleDebugLootTableCommand** (DebugCommands.cpp) Simulates loot rolls to report drop rates for debugging.
**SetDeathState** (Creature.cpp) Triggers loot generation setup when a creature dies.
**LoadLootTable** (LootMgr.cpp) Loads loot definitions from `creature_loot_template` and `reference_loot_template` into memory.
**ReportUnusedIds** (LootMgr.cpp) Logs loot IDs defined in DB but not used by any creature.
**ReportNotExistedId** (LootMgr.cpp) Logs errors when a creature references a non-existent loot ID.
**IsValid** (LootMgr.cpp) Validates loot table entries for consistency (e.g., chance ranges).
**FillLoot** (LootMgr.cpp) Processes the loot template, applying config rates to determine which items drop.
**Verify#3** (LootMgr.cpp) Verifies loot group chances do not exceed 100%.
**ExistsRefLootTemplate** (LootMgr.cpp) Checks if a reference loot template exists.

---

<!-- machine-true, projected from graph.json -->

## Map — Loot & Gold Drop Rates

*Source:* Creature.h, Creature.cpp, LootMgr.h, ObjectMgr.cpp, World.cpp, AiBotAILoot.cpp, DebugCommands.cpp, LootMgr.cpp
*Config keys:* Rate.Drop.Item.Poor (default 1), Rate.Drop.Item.Normal (default 1), Rate.Drop.Item.Uncommon (default 1), Rate.Drop.Item.Rare (default 1), Rate.Drop.Item.Epic (default 1), Rate.Drop.Item.Legendary (default 1), Rate.Drop.Item.Artifact (default 1), Rate.Drop.Item.Referenced (default 1), Rate.Drop.Money (default 1)
*Tables:* creature_loot_template, reference_loot_template, creature_template

| Member | Kind | Source | Role |
|---|---|---|---|
| Creature.Main/GetLootRecipientGuid | method | Creature.h:299-299 | seed — Creature.*/*Loot* |
| Creature.Main/GetLootGroupRecipientId | method | Creature.h:300-300 | seed — Creature.*/*Loot* |
| Creature.Main/HasLootRecipient | method | Creature.h:303-303 | seed — Creature.*/*Loot* |
| Creature.Main/IsGroupLootRecipient | method | Creature.h:304-304 | seed — Creature.*/*Loot* |
| Creature.Main/IsLootAllowedDueToDamageOrigin | method | Creature.h:548-554 | seed — Creature.*/*Loot* |
| Creature.Main/GetGroupLootTimer | method | Creature.h:587-587 | seed — Creature.*/*Loot* |
| Creature.Main/StartGroupLoot | method | Creature.cpp:995-999 | seed — Creature.*/*Loot* |
| Creature.Main/StopGroupLoot | method | Creature.cpp:1001-1011 | seed — Creature.*/*Loot* |
| Creature.Main/GetOriginalLootRecipient | method | Creature.cpp:1462-1465 | seed — Creature.*/*Loot* |
| Creature.Main/GetGroupLootRecipient | method | Creature.cpp:1470-1474 | seed — Creature.*/*Loot* |
| Creature.Main/GetLootRecipient | method | Creature.cpp:1483-1507 | seed — Creature.*/*Loot* |
| Creature.Main/SetLootRecipient | method | Creature.cpp:1512-1541 | seed — Creature.*/*Loot* |
| Creature.Main/GenerateLootForBody | method | Creature.cpp:1564-1580 | seed — Creature.*/*Loot* |
| Creature.Main/GeneratePlayerDependentLoot | method | Creature.cpp:1582-1589 | seed — Creature.*/*Loot* |
| Creature.Main/AllLootRemovedFromCorpse | method | Creature.cpp:3286-3332 | seed — Creature.*/*Loot* |
| LootStore/LootStore | ctor | LootMgr.h:182-183 | seed — LootStore/* |
| LootStore/~LootStore | dtor | LootMgr.h:184-184 | seed — LootStore/* |
| LootStore/HaveLootFor | method | LootMgr.h:193-193 | seed — LootStore/* |
| LootStore/GetName | method | LootMgr.h:199-199 | seed — LootStore/* |
| LootStore/GetEntryName | method | LootMgr.h:200-200 | seed — LootStore/* |
| LootStore/IsRatesAllowed | method | LootMgr.h:201-201 | seed — LootStore/* |
| ObjectMgr/LoadAllIdentifiers | method | ObjectMgr.cpp:180-328 | seed — queries creature_template |
| ObjectMgr/LoadCreatureTemplates | method | ObjectMgr.cpp:1188-1205 | seed — queries creature_template |
| ObjectMgr/LoadCreatureTemplate | method | ObjectMgr.cpp:1207-1224 | seed — queries creature_template |
| ObjectMgr/CheckCreatureTemplate | method | ObjectMgr.cpp:1419-1629 | seed — queries creature_template |
| ObjectMgr/LoadCreatureClassLevelStats | method | ObjectMgr.cpp:2064-2276 | seed — queries creature_template |
| ObjectMgr/LoadTrainers#2 | method | ObjectMgr.cpp:10313-10457 | seed — queries creature_template |
| ObjectMgr/LoadTrainerTemplates | method | ObjectMgr.cpp:10459-10491 | seed — queries creature_template |
| ObjectMgr/LoadVendorTemplates | method | ObjectMgr.cpp:10544-10576 | seed — queries creature_template |
| World/LoadConfigSettings | method | World.cpp:440-1245 | seed — reads config Rate.Drop.Item.Poor |
| AiBotAI.Loot/DoAutoLoot | method | AiBotAILoot.cpp:110-254 | related — 1 hop from a seed |
| ChatHandler.DebugCommands/HandleDebugLootTableCommand | method | DebugCommands.cpp:1806-1917 | related — 1 hop from a seed |
| Creature.Main/SetDeathState | method | Creature.cpp:2171-2257 | related — 1 hop from a seed |
| LootMgr/LoadLootTable | method | LootMgr.cpp:93-196 | related — 1 hop from a seed |
| LootMgr/ReportUnusedIds | method | LootMgr.cpp:242-250 | related — 1 hop from a seed |
| LootMgr/ReportNotExistedId | method | LootMgr.cpp:252-255 | related — 1 hop from a seed |
| LootMgr/IsValid | method | LootMgr.cpp:279-333 | related — 1 hop from a seed |
| LootMgr/FillLoot | method | LootMgr.cpp:496-517 | related — 1 hop from a seed |
| LootMgr/Verify#3 | method | LootMgr.cpp:1192-1203 | related — 1 hop from a seed |
| LootMgr/ExistsRefLootTemplate | function | LootMgr.cpp:1580-1583 | related — 1 hop from a seed |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `areatrigger_template`: id smallint(4) unsigned PK, build smallint(4) unsigned PK, name varchar(128)?, map_id smallint(3) unsigned, x float, y float, z float, radius float, box_x float, box_y float, box_z float, box_orientation float, cooldown int(10) unsigned, condition_id int(10) unsigned, script_id int(10) unsigned, script_name varchar(64)
- `conditions`: condition_entry mediumint(8) unsigned PK, type tinyint(3), value1 int(11), value2 int(11), value3 int(11), value4 int(11), flags tinyint(3) unsigned
- `creature`: guid int(10) unsigned PK, id mediumint(8) unsigned, id2 mediumint(8) unsigned, id3 mediumint(8) unsigned, id4 mediumint(8) unsigned, id5 mediumint(8) unsigned, map smallint(5) unsigned, position_x float, position_y float, position_z float, orientation float, spawntimesecsmin int(10) unsigned, spawntimesecsmax int(10) unsigned, wander_distance float, health_percent float, mana_percent float unsigned, movement_type tinyint(3) unsigned, spawn_flags int(10) unsigned, visibility_mod float?, patch_min tinyint(3) unsigned, patch_max tinyint(3) unsigned
- `creature_classlevelstats`: class tinyint(3) unsigned PK, level tinyint(3) unsigned PK, melee_damage float, ranged_damage float, attack_power int(11), ranged_attack_power int(11), health int(11), base_health int(11), mana int(11), base_mana int(11), strength int(11), agility int(11), stamina int(11), intellect int(11), spirit int(11), armor int(11)
- `creature_loot_template`: entry mediumint(8) unsigned PK, item mediumint(8) unsigned PK, ChanceOrQuestChance float, groupid tinyint(3) unsigned PK, mincountOrRef mediumint(9), maxcount tinyint(3) unsigned, condition_id mediumint(8) unsigned, patch_min tinyint(3) unsigned PK, patch_max tinyint(3) unsigned PK
- `creature_spells`: entry int(11) unsigned PK, name varchar(255), spellId_1 smallint(5) unsigned, probability_1 tinyint(3) unsigned, castTarget_1 tinyint(2) unsigned, targetParam1_1 smallint(5) unsigned, targetParam2_1 smallint(5) unsigned, castFlags_1 smallint(5) unsigned, delayInitialMin_1 smallint(5) unsigned, delayInitialMax_1 smallint(5) unsigned, delayRepeatMin_1 smallint(5) unsigned, delayRepeatMax_1 smallint(5) unsigned, scriptId_1 mediumint(8) unsigned, spellId_2 smallint(5) unsigned, probability_2 tinyint(3) unsigned, castTarget_2 tinyint(2) unsigned, targetParam1_2 smallint(5) unsigned, targetParam2_2 smallint(5) unsigned, castFlags_2 smallint(5) unsigned, delayInitialMin_2 smallint(5) unsigned, delayInitialMax_2 smallint(5) unsigned, delayRepeatMin_2 smallint(5) unsigned, delayRepeatMax_2 smallint(5) unsigned, scriptId_2 mediumint(8) unsigned, spellId_3 smallint(5) unsigned, probability_3 tinyint(3) unsigned, castTarget_3 tinyint(2) unsigned, targetParam1_3 smallint(5) unsigned, targetParam2_3 smallint(5) unsigned, castFlags_3 smallint(5) unsigned, delayInitialMin_3 smallint(5) unsigned, delayInitialMax_3 smallint(5) unsigned, delayRepeatMin_3 smallint(5) unsigned, delayRepeatMax_3 smallint(5) unsigned, scriptId_3 mediumint(8) unsigned, spellId_4 smallint(5) unsigned, probability_4 tinyint(3) unsigned, castTarget_4 tinyint(2) unsigned, targetParam1_4 smallint(5) unsigned, targetParam2_4 smallint(5) unsigned, castFlags_4 smallint(5) unsigned, delayInitialMin_4 smallint(5) unsigned, delayInitialMax_4 smallint(5) unsigned, delayRepeatMin_4 smallint(5) unsigned, delayRepeatMax_4 smallint(5) unsigned, scriptId_4 mediumint(8) unsigned, spellId_5 smallint(5) unsigned, probability_5 tinyint(3) unsigned, castTarget_5 tinyint(2) unsigned, targetParam1_5 smallint(5) unsigned, targetParam2_5 smallint(5) unsigned, castFlags_5 smallint(5) unsigned, delayInitialMin_5 smallint(5) unsigned, delayInitialMax_5 smallint(5) unsigned, delayRepeatMin_5 smallint(5) unsigned, delayRepeatMax_5 smallint(5) unsigned, scriptId_5 mediumint(8) unsigned, spellId_6 smallint(5) unsigned, probability_6 tinyint(3) unsigned, castTarget_6 tinyint(2) unsigned, targetParam1_6 smallint(5) unsigned, targetParam2_6 smallint(5) unsigned, castFlags_6 smallint(5) unsigned, delayInitialMin_6 smallint(5) unsigned, delayInitialMax_6 smallint(5) unsigned, delayRepeatMin_6 smallint(5) unsigned, delayRepeatMax_6 smallint(5) unsigned, scriptId_6 mediumint(8) unsigned, spellId_7 smallint(5) unsigned, probability_7 tinyint(3) unsigned, castTarget_7 tinyint(2) unsigned, targetParam1_7 smallint(5) unsigned, targetParam2_7 smallint(5) unsigned, castFlags_7 smallint(5) unsigned, delayInitialMin_7 smallint(5) unsigned, delayInitialMax_7 smallint(5) unsigned, delayRepeatMin_7 smallint(5) unsigned, delayRepeatMax_7 smallint(5) unsigned, scriptId_7 mediumint(8) unsigned, spellId_8 smallint(5) unsigned, probability_8 tinyint(3) unsigned, castTarget_8 tinyint(2) unsigned, targetParam1_8 smallint(5) unsigned, targetParam2_8 smallint(5) unsigned, castFlags_8 smallint(5) unsigned, delayInitialMin_8 smallint(5) unsigned, delayInitialMax_8 smallint(5) unsigned, delayRepeatMin_8 smallint(5) unsigned, delayRepeatMax_8 smallint(5) unsigned, scriptId_8 mediumint(8) unsigned
- `creature_template`: entry mediumint(8) unsigned PK, patch tinyint(3) unsigned PK, name char(100), subname char(100)?, level_min tinyint(3) unsigned, level_max tinyint(3) unsigned, faction smallint(5) unsigned, npc_flags int(10) unsigned, gossip_menu_id mediumint(8) unsigned, display_id1 mediumint(8) unsigned, display_id2 mediumint(8) unsigned, display_id3 mediumint(8) unsigned, display_id4 mediumint(8) unsigned, display_scale1 float, display_scale2 float, display_scale3 float, display_scale4 float, display_probability1 smallint(5) unsigned, display_probability2 smallint(5) unsigned, display_probability3 smallint(5) unsigned, display_probability4 smallint(5) unsigned, display_total_probability smallint(5) unsigned, mount_display_id smallint(5) unsigned, speed_walk float, speed_run float, detection_range float, call_for_help_range float, leash_range float, type tinyint(3) unsigned, pet_family tinyint(4) unsigned, rank tinyint(3) unsigned, unit_class tinyint(3) unsigned, xp_multiplier float, health_multiplier float, mana_multiplier float, armor_multiplier float, damage_multiplier float, damage_variance float, damage_school tinyint(4) unsigned, base_attack_time int(10) unsigned, ranged_attack_time int(10) unsigned, holy_res smallint(5), fire_res smallint(5), nature_res smallint(5), frost_res smallint(5), shadow_res smallint(5), arcane_res smallint(5), trainer_type tinyint(4) unsigned, trainer_spell smallint(5) unsigned, trainer_class tinyint(3) unsigned, trainer_race tinyint(3) unsigned, loot_id mediumint(8) unsigned, pickpocket_loot_id mediumint(8) unsigned, skinning_loot_id mediumint(8) unsigned, gold_min mediumint(8) unsigned, gold_max mediumint(8) unsigned, spell_id1 smallint(5) unsigned, spell_id2 smallint(5) unsigned, spell_id3 smallint(5) unsigned, spell_id4 smallint(5) unsigned, spell_list_id int(11) unsigned, pet_spell_list_id mediumint(8) unsigned, spawn_spell_id smallint(5) unsigned, auras text?, ai_name char(64), movement_type tinyint(3) unsigned, inhabit_type tinyint(3) unsigned, civilian tinyint(3) unsigned, racial_leader tinyint(3) unsigned, equipment_id mediumint(8) unsigned, trainer_id mediumint(8) unsigned, vendor_id mediumint(8) unsigned, mechanic_immune_mask int(10) unsigned, school_immune_mask int(10) unsigned, immunity_flags int(10) unsigned, static_flags1 int(10) unsigned, static_flags2 int(10) unsigned, flags_extra int(10) unsigned, script_name char(64)
- `gameobject`: guid int(10) unsigned PK, id mediumint(8) unsigned, map smallint(5) unsigned, position_x float, position_y float, position_z float, orientation float, rotation0 float, rotation1 float, rotation2 float, rotation3 float, spawntimesecsmin int(11), spawntimesecsmax int(11), animprogress tinyint(3) unsigned, state tinyint(3) unsigned, spawn_flags int(10) unsigned, visibility_mod float?, patch_min tinyint(3) unsigned, patch_max tinyint(3) unsigned
- `gameobject_template`: entry mediumint(8) unsigned PK, patch tinyint(3) unsigned PK, type tinyint(3) unsigned, displayId mediumint(8) unsigned, name varchar(100), icon varchar(100), faction smallint(5) unsigned, flags int(10) unsigned, size float, data0 int(10), data1 int(11), data2 int(10), data3 int(10), data4 int(10), data5 int(10), data6 int(11), data7 int(10), data8 int(10), data9 int(10), data10 int(10), data11 int(10), data12 int(10), data13 int(10), data14 int(10), data15 int(10), data16 int(10), data17 int(10), data18 int(10), data19 int(10), data20 int(10), data21 int(10), data22 int(10), data23 int(10), mingold mediumint(8) unsigned, maxgold mediumint(8) unsigned, script_name varchar(64)
- `gossip_menu`: entry smallint(6) unsigned PK, text_id mediumint(8) unsigned PK, script_id mediumint(8) unsigned, condition_id mediumint(8) unsigned
- `item_template`: entry mediumint(8) unsigned PK, patch tinyint(3) unsigned PK, class tinyint(3) unsigned, subclass tinyint(3) unsigned, name varchar(255), description varchar(255), display_id mediumint(8) unsigned, quality tinyint(3) unsigned, flags int(10) unsigned, buy_count tinyint(3) unsigned, buy_price int(10) unsigned, sell_price int(10) unsigned, inventory_type tinyint(3) unsigned, allowable_class mediumint(9), allowable_race mediumint(9), item_level tinyint(3) unsigned, required_level tinyint(3) unsigned, required_skill smallint(5) unsigned, required_skill_rank smallint(5) unsigned, required_spell smallint(5) unsigned, required_honor_rank mediumint(8) unsigned, required_city_rank mediumint(8) unsigned, required_reputation_faction smallint(5) unsigned, required_reputation_rank smallint(5) unsigned, max_count smallint(5) unsigned, stackable smallint(5) unsigned, container_slots tinyint(3) unsigned, stat_type1 tinyint(3) unsigned, stat_value1 smallint(6), stat_type2 tinyint(3) unsigned, stat_value2 smallint(6), stat_type3 tinyint(3) unsigned, stat_value3 smallint(6), stat_type4 tinyint(3) unsigned, stat_value4 smallint(6), stat_type5 tinyint(3) unsigned, stat_value5 smallint(6), stat_type6 tinyint(3) unsigned, stat_value6 smallint(6), stat_type7 tinyint(3) unsigned, stat_value7 smallint(6), stat_type8 tinyint(3) unsigned, stat_value8 smallint(6), stat_type9 tinyint(3) unsigned, stat_value9 smallint(6), stat_type10 tinyint(3) unsigned, stat_value10 smallint(6), delay smallint(5) unsigned, range_mod float, ammo_type tinyint(3) unsigned, dmg_min1 float, dmg_max1 float, dmg_type1 tinyint(3) unsigned, dmg_min2 float, dmg_max2 float, dmg_type2 tinyint(3) unsigned, dmg_min3 float, dmg_max3 float, dmg_type3 tinyint(3) unsigned, dmg_min4 float, dmg_max4 float, dmg_type4 tinyint(3) unsigned, dmg_min5 float, dmg_max5 float, dmg_type5 tinyint(3) unsigned, block mediumint(8) unsigned, armor smallint(5), holy_res smallint(5), fire_res smallint(5), nature_res smallint(5), frost_res smallint(5), shadow_res smallint(5), arcane_res smallint(5), spellid_1 smallint(5) unsigned, spelltrigger_1 tinyint(3) unsigned, spellcharges_1 tinyint(4), spellppmrate_1 float, spellcooldown_1 int(11), spellcategory_1 smallint(5) unsigned, spellcategorycooldown_1 int(11), spellid_2 smallint(5) unsigned, spelltrigger_2 tinyint(3) unsigned, spellcharges_2 tinyint(4), spellppmrate_2 float, spellcooldown_2 int(11), spellcategory_2 smallint(5) unsigned, spellcategorycooldown_2 int(11), spellid_3 smallint(5) unsigned, spelltrigger_3 tinyint(3) unsigned, spellcharges_3 tinyint(4), spellppmrate_3 float, spellcooldown_3 int(11), spellcategory_3 smallint(5) unsigned, spellcategorycooldown_3 int(11), spellid_4 smallint(5) unsigned, spelltrigger_4 tinyint(3) unsigned, spellcharges_4 tinyint(4), spellppmrate_4 float, spellcooldown_4 int(11), spellcategory_4 smallint(5) unsigned, spellcategorycooldown_4 int(11), spellid_5 smallint(5) unsigned, spelltrigger_5 tinyint(3) unsigned, spellcharges_5 tinyint(4), spellppmrate_5 float, spellcooldown_5 int(11), spellcategory_5 smallint(5) unsigned, spellcategorycooldown_5 int(11), bonding tinyint(3) unsigned, page_text mediumint(8) unsigned, page_language tinyint(3) unsigned, page_material tinyint(3) unsigned, start_quest mediumint(8) unsigned, lock_id mediumint(8) unsigned, material tinyint(4), sheath tinyint(3) unsigned, random_property mediumint(8) unsigned, set_id mediumint(8) unsigned, max_durability smallint(5) unsigned, area_bound mediumint(8) unsigned, map_bound smallint(6), duration int(11) unsigned, bag_family mediumint(9), disenchant_id mediumint(8) unsigned, food_type tinyint(3) unsigned, min_money_loot int(10) unsigned, max_money_loot int(10) unsigned, wrapped_gift mediumint(8) unsigned, extra_flags tinyint(1) unsigned, other_team_entry int(11) unsigned?
- `npc_vendor_template`: entry mediumint(8) unsigned PK, slot smallint(5) unsigned, item mediumint(8) unsigned PK, maxcount tinyint(3) unsigned, incrtime int(10) unsigned, itemflags int(10) unsigned, condition_id mediumint(8) unsigned
- `quest_template`: entry mediumint(8) unsigned PK, patch tinyint(3) unsigned PK, Method tinyint(3) unsigned, ZoneOrSort smallint(6), MinLevel tinyint(3) unsigned, MaxLevel tinyint(3) unsigned, QuestLevel tinyint(3) unsigned, Type smallint(5) unsigned, RequiredClasses smallint(5) unsigned, RequiredRaces smallint(5) unsigned, RequiredSkill smallint(5) unsigned, RequiredSkillValue smallint(5) unsigned, RequiredCondition mediumint(8) unsigned, RepObjectiveFaction smallint(5) unsigned, RepObjectiveValue mediumint(9), RequiredMinRepFaction smallint(5) unsigned, RequiredMinRepValue mediumint(9), RequiredMaxRepFaction smallint(5) unsigned, RequiredMaxRepValue mediumint(9), SuggestedPlayers tinyint(3) unsigned, LimitTime int(10) unsigned, QuestFlags smallint(5) unsigned, SpecialFlags tinyint(3) unsigned, PrevQuestId mediumint(9), NextQuestId mediumint(9), ExclusiveGroup mediumint(9), BreadcrumbForQuestId mediumint(9) unsigned, NextQuestInChain mediumint(8) unsigned, SrcItemId mediumint(8) unsigned, SrcItemCount tinyint(3) unsigned, SrcSpell smallint(5) unsigned, Title text?, Details text?, Objectives text?, OfferRewardText text?, RequestItemsText text?, EndText text?, ObjectiveText1 text?, ObjectiveText2 text?, ObjectiveText3 text?, ObjectiveText4 text?, ReqItemId1 mediumint(8) unsigned, ReqItemId2 mediumint(8) unsigned, ReqItemId3 mediumint(8) unsigned, ReqItemId4 mediumint(8) unsigned, ReqItemCount1 smallint(5) unsigned, ReqItemCount2 smallint(5) unsigned, ReqItemCount3 smallint(5) unsigned, ReqItemCount4 smallint(5) unsigned, ReqSourceId1 mediumint(8) unsigned, ReqSourceId2 mediumint(8) unsigned, ReqSourceId3 mediumint(8) unsigned, ReqSourceId4 mediumint(8) unsigned, ReqSourceCount1 mediumint(8) unsigned, ReqSourceCount2 mediumint(8) unsigned, ReqSourceCount3 mediumint(8) unsigned, ReqSourceCount4 mediumint(8) unsigned, ReqCreatureOrGOId1 mediumint(9), ReqCreatureOrGOId2 mediumint(9), ReqCreatureOrGOId3 mediumint(9), ReqCreatureOrGOId4 mediumint(9), ReqCreatureOrGOCount1 smallint(5) unsigned, ReqCreatureOrGOCount2 smallint(5) unsigned, ReqCreatureOrGOCount3 smallint(5) unsigned, ReqCreatureOrGOCount4 smallint(5) unsigned, ReqSpellCast1 smallint(5) unsigned, ReqSpellCast2 smallint(5) unsigned, ReqSpellCast3 smallint(5) unsigned, ReqSpellCast4 smallint(5) unsigned, RewChoiceItemId1 mediumint(8) unsigned, RewChoiceItemId2 mediumint(8) unsigned, RewChoiceItemId3 mediumint(8) unsigned, RewChoiceItemId4 mediumint(8) unsigned, RewChoiceItemId5 mediumint(8) unsigned, RewChoiceItemId6 mediumint(8) unsigned, RewChoiceItemCount1 smallint(5) unsigned, RewChoiceItemCount2 smallint(5) unsigned, RewChoiceItemCount3 smallint(5) unsigned, RewChoiceItemCount4 smallint(5) unsigned, RewChoiceItemCount5 smallint(5) unsigned, RewChoiceItemCount6 smallint(5) unsigned, RewItemId1 mediumint(8) unsigned, RewItemId2 mediumint(8) unsigned, RewItemId3 mediumint(8) unsigned, RewItemId4 mediumint(8) unsigned, RewItemCount1 smallint(5) unsigned, RewItemCount2 smallint(5) unsigned, RewItemCount3 smallint(5) unsigned, RewItemCount4 smallint(5) unsigned, RewRepFaction1 smallint(5) unsigned, RewRepFaction2 smallint(5) unsigned, RewRepFaction3 smallint(5) unsigned, RewRepFaction4 smallint(5) unsigned, RewRepFaction5 smallint(5) unsigned, RewRepValue1 mediumint(9), RewRepValue2 mediumint(9), RewRepValue3 mediumint(9), RewRepValue4 mediumint(9), RewRepValue5 mediumint(9), RewRepSpilloverMask tinyint(3) unsigned, RewXP mediumint(9) unsigned, RewOrReqMoney int(11), RewMoneyMaxLevel int(10) unsigned, RewSpell smallint(5) unsigned, RewSpellCast smallint(5) unsigned, RewMailTemplateId mediumint(8), RewMailDelaySecs int(11) unsigned, RewMailMoney int(10) unsigned, PointMapId smallint(5) unsigned, PointX float, PointY float, PointOpt mediumint(8) unsigned, DetailsEmote1 smallint(5) unsigned, DetailsEmote2 smallint(5) unsigned, DetailsEmote3 smallint(5) unsigned, DetailsEmote4 smallint(5) unsigned, DetailsEmoteDelay1 int(11) unsigned, DetailsEmoteDelay2 int(11) unsigned, DetailsEmoteDelay3 int(11) unsigned, DetailsEmoteDelay4 int(11) unsigned, IncompleteEmote smallint(5) unsigned, CompleteEmote smallint(5) unsigned, OfferRewardEmote1 smallint(5) unsigned, OfferRewardEmote2 smallint(5) unsigned, OfferRewardEmote3 smallint(5) unsigned, OfferRewardEmote4 smallint(5) unsigned, OfferRewardEmoteDelay1 int(11) unsigned, OfferRewardEmoteDelay2 int(11) unsigned, OfferRewardEmoteDelay3 int(11) unsigned, OfferRewardEmoteDelay4 int(11) unsigned, StartScript mediumint(8) unsigned, CompleteScript mediumint(8) unsigned
- `reference_loot_template`: entry mediumint(8) unsigned PK, item mediumint(8) unsigned PK, ChanceOrQuestChance float, groupid tinyint(3) unsigned, mincountOrRef mediumint(9), maxcount tinyint(3) unsigned, condition_id mediumint(8) unsigned, patch_min tinyint(3) unsigned PK, patch_max tinyint(3) unsigned PK

*`?` = nullable, `PK` = primary key column.*

