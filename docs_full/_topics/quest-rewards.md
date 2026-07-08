# Quest Rewards (XP, Gold, Items)

<!-- aliases: quest rewards, quest xp, quest gold, quest money, increase quest rewards, quest reward items -->
<!-- documentation: model-written from source via the local LLM; review before trusting -->

Quest rewards in VMaNGOS are defined statically in the database and loaded into memory at server startup. The server does not calculate XP or gold dynamically based on player level or difficulty; instead, it reads fixed values from the `quest_template` table and applies them directly when a player completes a quest.

The lifecycle begins with **ObjectMgr/LoadAllIdentifiers**, which scans `quest_template` to build a set of valid quest IDs (`m_QuestIdSet`). This ensures the server knows which quests exist before loading their full definitions. Next, **ObjectMgr/LoadQuests** executes a complex SQL query against `quest_template` to fetch all quest data, including reward columns. It constructs `Quest` objects (defined in `QuestDef.h`) and populates internal maps. During this phase, the server validates reward data—for instance, checking that auto-rewarded quests do not have multiple choice items—and logs errors for invalid references.

The `QuestDef` class provides accessor methods for all quest properties. Reward-specific data is stored in member variables populated during loading:
- **XP**: Stored in `RewXP` (accessed via logic not shown in slices, but defined in schema).
- **Gold**: Stored in `RewOrReqMoney` and capped by `RewMoneyMaxLevel`.
- **Items**: Fixed rewards are in `RewItemId1`–`RewItemId4` with counts in `RewItemCount1`–`RewItemCount4`. Choice rewards are in `RewChoiceItemId1`–`RewChoiceItemId6` with counts in `RewChoiceItemCount1`–`RewChoiceItemCount6`.
- **Reputation**: Defined by `RewRepFaction1`–`RewRepFaction5` and `RewRepValue1`–`RewRepValue5`.
- **Spells**: Granted via `RewSpell` or `RewSpellCast`.

When a quest is completed, the server uses these stored values to grant rewards. If a quest has associated scripts, **ScriptMgr/LoadQuestEndScripts** and **ScriptMgr/LoadQuestStartScripts** ensure that C++ scripts linked via `CompleteScript` and `StartScript` columns are loaded and validated. These scripts can modify rewards programmatically, but the base values always originate from the database.

No configuration keys scale quest rewards globally. Any change to XP, gold, or item rewards requires editing the `quest_template` table directly.

## How to Modify

### Config
No dedicated configuration key exists for scaling quest XP, gold, or item rewards. The server uses the raw values from the database without applying multipliers from `mangosd.conf`.

### Database
All quest rewards are controlled by the `quest_template` table. Edit the following columns for the desired quest `entry`:

- **XP**: `RewXP` — The amount of experience points awarded. Set to `0` for no XP.
- **Gold**: 
  - `RewOrReqMoney` — The base gold amount. Note: This column is also used for required money costs if the quest requires payment.
  - `RewMoneyMaxLevel` — The player level above which the gold reward is reduced to zero. Set to `60` (or higher) to ensure gold is always awarded.
- **Fixed Item Rewards**: 
  - `RewItemId1`–`RewItemId4` — Item template IDs for guaranteed rewards.
  - `RewItemCount1`–`RewItemCount4` — Quantity of each fixed item.
- **Choice Item Rewards**: 
  - `RewChoiceItemId1`–`RewChoiceItemId6` — Item template IDs for optional rewards (player picks one).
  - `RewChoiceItemCount1`–`RewChoiceItemCount6` — Quantity of each choice item.
- **Reputation**: 
  - `RewRepFaction1`–`RewRepFaction5` — Faction IDs for reputation gains.
  - `RewRepValue1`–`RewRepValue5` — Amount of reputation gained per faction.
- **Spells**: 
  - `RewSpell` — Spell ID granted upon completion.
  - `RewSpellCast` — Spell ID cast on the player upon completion.

Changes take effect after reloading quests (`reload quest_template`) or restarting the server.

### Code
If you need to implement dynamic reward scaling (e.g., increasing XP based on player level or adding global multipliers), you must modify the C++ code. The reward granting logic is not in the provided slices, but it typically resides in `Player.cpp` or `Quest.cpp` within the `HandleQuestComplete` or similar completion handlers. You would need to locate where `RewXP` and `RewOrReqMoney` are applied to the player and insert multiplier logic there. Rebuilding the server is required.

## Path Reference

**ObjectMgr/LoadAllIdentifiers**  
*ObjectMgr.cpp*  
Scans `quest_template` to build a set of valid quest IDs, ensuring the server recognizes all defined quests before loading detailed data.

**ObjectMgr/LoadQuests**  
*ObjectMgr.cpp*  
Queries `quest_template` to load full quest definitions, including reward columns, into memory. Validates reward data and constructs `Quest` objects.

**QuestDef/GetQuestFlags**  
*QuestDef.h*  
Returns the raw `m_QuestFlags` bitmask, which includes flags like `QUEST_FLAGS_AUTO_REWARDED` that affect how rewards are distributed.

**QuestDef/GetQuestId**  
*QuestDef.h*  
Returns the unique quest identifier, used to look up the correct reward data.

**QuestDef/GetQuestMethod**  
*QuestDef.h*  
Returns the quest method (e.g., autocomplete, deliver), which can influence reward timing.

**QuestDef/GetMinLevel**  
*QuestDef.h*  
Returns the minimum player level required, indirectly affecting reward relevance.

**QuestDef/GetMaxLevel**  
*QuestDef.h*  
Returns the maximum player level allowed, indirectly affecting reward relevance.

**QuestDef/GetQuestLevel**  
*QuestDef.h*  
Returns the quest's designated level, used for UI and potential scaling logic.

**QuestDef/GetRequiredClasses**  
*QuestDef.h*  
Returns bitmask of required classes, restricting who can earn the rewards.

**QuestDef/GetRequiredRaces**  
*QuestDef.h*  
Returns bitmask of required races, restricting who can earn the rewards.

**QuestDef/GetRequiredCondition**  
*QuestDef.h*  
Returns the required condition ID, which can gate access to rewards.

**QuestDef/GetRepObjectiveFaction**  
*QuestDef.h*  
Returns the faction ID for reputation objectives, distinct from reward reputation.

**QuestDef/GetRepObjectiveValue**  
*QuestDef.h*  
Returns the required reputation value for objectives, distinct from reward reputation.

**QuestDef/GetRequiredMinRepFaction**  
*QuestDef.h*  
Returns the faction ID for minimum reputation requirement to accept the quest.

**QuestDef/GetRequiredMinRepValue**  
*QuestDef.h*  
Returns the minimum reputation value required to accept the quest.

**QuestDef/GetRequiredMaxRepFaction**  
*QuestDef.h*  
Returns the faction ID for maximum reputation allowed to accept the quest.

**QuestDef/GetRequiredMaxRepValue**  
*QuestDef.h*  
Returns the maximum reputation value allowed to accept the quest.

**QuestDef/GetLimitTime**  
*QuestDef.h*  
Returns the time limit for the quest, which can affect reward eligibility if expired.

**QuestDef/GetPrevQuestId**  
*QuestDef.h*  
Returns the ID of the previous quest in the chain, linking reward progression.

**QuestDef/GetNextQuestId**  
*QuestDef.h*  
Returns the ID of the next quest in the chain, linking reward progression.

**QuestDef/GetExclusiveGroup**  
*QuestDef.h*  
Returns the exclusive group ID, preventing duplicate reward claiming from conflicting quests.

**QuestDef/GetBreadcrumbForQuestId**  
*QuestDef.h*  
Returns the breadcrumb quest ID, guiding players to reward sources.

**QuestDef/GetNextQuestInChain**  
*QuestDef.h*  
Returns the next quest ID in the chain, linking reward progression.

**QuestDef/GetDetails**  
*QuestDef.h*  
Returns the quest details string, displayed to players before reward acceptance.

**QuestDef/GetObjectives**  
*QuestDef.h*  
Returns the quest objectives string, defining what must be done to earn rewards.

**QuestDef/GetOfferRewardText**  
*QuestDef.h*  
Returns the offer reward text string, displayed when rewards are presented.

**QuestDef/GetRequestItemsText**  
*QuestDef.h*  
Returns the request items text string, displayed when turning in items for rewards.

**QuestDef/GetEndText**  
*QuestDef.h*  
Returns the end text string, displayed after rewards are granted.

**QuestDef/GetPointMapId**  
*QuestDef.h*  
Returns the map ID for the quest point, locating reward sources.

**QuestDef/GetPointX**  
*QuestDef.h*  
Returns the X coordinate for the quest point, locating reward sources.

**QuestDef/GetPointY**  
*QuestDef.h*  
Returns the Y coordinate for the quest point, locating reward sources.

**QuestDef/GetPointOpt**  
*QuestDef.h*  
Returns the point option/flag, modifying how the quest point is displayed.

**QuestDef/GetIncompleteEmote**  
*QuestDef.h*  
Returns the emote played when quest is incomplete, signaling no rewards yet.

**QuestDef/GetCompleteEmote**  
*QuestDef.h*  
Returns the emote played when quest is complete, signaling rewards are ready.

**QuestDef/GetQuestStartScript**  
*QuestDef.h*  
Returns the script ID for quest start, which can pre-condition rewards.

**QuestDef/GetQuestCompleteScript**  
*QuestDef.h*  
Returns the script ID for quest completion, which can modify or add rewards.

**QuestDef/GetReqItemsCount**  
*QuestDef.h*  
Returns the cached count of required items, verifying completion for rewards.

**QuestDef/GetReqCreatureOrGOcount**  
*QuestDef.h*  
Returns the cached count of required creatures/game objects, verifying completion for rewards.

**ScriptMgr/LoadQuestEndScripts**  
*ScriptMgr.cpp*  
Loads quest completion scripts and cross-references them with `quest_template`, enabling custom reward logic.

**ScriptMgr/LoadQuestStartScripts**  
*ScriptMgr.cpp*  
Loads quest acceptance scripts and cross-references them with `quest_template`, enabling pre-reward conditions.

---

<!-- machine-true, projected from graph.json -->

## Map — Quest Rewards (XP, Gold, Items)

*Source:* ObjectMgr.cpp, QuestDef.h, ScriptMgr.cpp
*Config keys:* —
*Tables:* quest_template

| Member | Kind | Source | Role |
|---|---|---|---|
| ObjectMgr/LoadAllIdentifiers | method | ObjectMgr.cpp:180-328 | seed — queries quest_template |
| ObjectMgr/LoadQuests | method | ObjectMgr.cpp:5314-6030 | seed — queries quest_template |
| QuestDef/GetQuestFlags | method | QuestDef.h:211-211 | seed — QuestDef/* |
| QuestDef/GetQuestId | method | QuestDef.h:217-217 | seed — QuestDef/* |
| QuestDef/GetQuestMethod | method | QuestDef.h:218-218 | seed — QuestDef/* |
| QuestDef/GetMinLevel | method | QuestDef.h:220-220 | seed — QuestDef/* |
| QuestDef/GetMaxLevel | method | QuestDef.h:221-221 | seed — QuestDef/* |
| QuestDef/GetQuestLevel | method | QuestDef.h:222-222 | seed — QuestDef/* |
| QuestDef/GetRequiredClasses | method | QuestDef.h:224-224 | seed — QuestDef/* |
| QuestDef/GetRequiredRaces | method | QuestDef.h:225-225 | seed — QuestDef/* |
| QuestDef/GetRequiredCondition | method | QuestDef.h:228-228 | seed — QuestDef/* |
| QuestDef/GetRepObjectiveFaction | method | QuestDef.h:229-229 | seed — QuestDef/* |
| QuestDef/GetRepObjectiveValue | method | QuestDef.h:230-230 | seed — QuestDef/* |
| QuestDef/GetRequiredMinRepFaction | method | QuestDef.h:231-231 | seed — QuestDef/* |
| QuestDef/GetRequiredMinRepValue | method | QuestDef.h:232-232 | seed — QuestDef/* |
| QuestDef/GetRequiredMaxRepFaction | method | QuestDef.h:233-233 | seed — QuestDef/* |
| QuestDef/GetRequiredMaxRepValue | method | QuestDef.h:234-234 | seed — QuestDef/* |
| QuestDef/GetLimitTime | method | QuestDef.h:236-236 | seed — QuestDef/* |
| QuestDef/GetPrevQuestId | method | QuestDef.h:237-237 | seed — QuestDef/* |
| QuestDef/GetNextQuestId | method | QuestDef.h:238-238 | seed — QuestDef/* |
| QuestDef/GetExclusiveGroup | method | QuestDef.h:239-239 | seed — QuestDef/* |
| QuestDef/GetBreadcrumbForQuestId | method | QuestDef.h:240-240 | seed — QuestDef/* |
| QuestDef/GetNextQuestInChain | method | QuestDef.h:241-241 | seed — QuestDef/* |
| QuestDef/GetDetails | method | QuestDef.h:247-247 | seed — QuestDef/* |
| QuestDef/GetObjectives | method | QuestDef.h:248-248 | seed — QuestDef/* |
| QuestDef/GetOfferRewardText | method | QuestDef.h:249-249 | seed — QuestDef/* |
| QuestDef/GetRequestItemsText | method | QuestDef.h:250-250 | seed — QuestDef/* |
| QuestDef/GetEndText | method | QuestDef.h:251-251 | seed — QuestDef/* |
| QuestDef/GetPointMapId | method | QuestDef.h:263-263 | seed — QuestDef/* |
| QuestDef/GetPointX | method | QuestDef.h:264-264 | seed — QuestDef/* |
| QuestDef/GetPointY | method | QuestDef.h:265-265 | seed — QuestDef/* |
| QuestDef/GetPointOpt | method | QuestDef.h:266-266 | seed — QuestDef/* |
| QuestDef/GetIncompleteEmote | method | QuestDef.h:267-267 | seed — QuestDef/* |
| QuestDef/GetCompleteEmote | method | QuestDef.h:268-268 | seed — QuestDef/* |
| QuestDef/GetQuestStartScript | method | QuestDef.h:269-269 | seed — QuestDef/* |
| QuestDef/GetQuestCompleteScript | method | QuestDef.h:270-270 | seed — QuestDef/* |
| QuestDef/GetReqItemsCount | method | QuestDef.h:300-300 | seed — QuestDef/* |
| QuestDef/GetReqCreatureOrGOcount | method | QuestDef.h:301-301 | seed — QuestDef/* |
| ScriptMgr/LoadQuestEndScripts | method | ScriptMgr.cpp:1452-1474 | seed — queries quest_template |
| ScriptMgr/LoadQuestStartScripts | method | ScriptMgr.cpp:1476-1498 | seed — queries quest_template |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `areatrigger_template`: id smallint(4) unsigned PK, build smallint(4) unsigned PK, name varchar(128)?, map_id smallint(3) unsigned, x float, y float, z float, radius float, box_x float, box_y float, box_z float, box_orientation float, cooldown int(10) unsigned, condition_id int(10) unsigned, script_id int(10) unsigned, script_name varchar(64)
- `conditions`: condition_entry mediumint(8) unsigned PK, type tinyint(3), value1 int(11), value2 int(11), value3 int(11), value4 int(11), flags tinyint(3) unsigned
- `creature`: guid int(10) unsigned PK, id mediumint(8) unsigned, id2 mediumint(8) unsigned, id3 mediumint(8) unsigned, id4 mediumint(8) unsigned, id5 mediumint(8) unsigned, map smallint(5) unsigned, position_x float, position_y float, position_z float, orientation float, spawntimesecsmin int(10) unsigned, spawntimesecsmax int(10) unsigned, wander_distance float, health_percent float, mana_percent float unsigned, movement_type tinyint(3) unsigned, spawn_flags int(10) unsigned, visibility_mod float?, patch_min tinyint(3) unsigned, patch_max tinyint(3) unsigned
- `creature_spells`: entry int(11) unsigned PK, name varchar(255), spellId_1 smallint(5) unsigned, probability_1 tinyint(3) unsigned, castTarget_1 tinyint(2) unsigned, targetParam1_1 smallint(5) unsigned, targetParam2_1 smallint(5) unsigned, castFlags_1 smallint(5) unsigned, delayInitialMin_1 smallint(5) unsigned, delayInitialMax_1 smallint(5) unsigned, delayRepeatMin_1 smallint(5) unsigned, delayRepeatMax_1 smallint(5) unsigned, scriptId_1 mediumint(8) unsigned, spellId_2 smallint(5) unsigned, probability_2 tinyint(3) unsigned, castTarget_2 tinyint(2) unsigned, targetParam1_2 smallint(5) unsigned, targetParam2_2 smallint(5) unsigned, castFlags_2 smallint(5) unsigned, delayInitialMin_2 smallint(5) unsigned, delayInitialMax_2 smallint(5) unsigned, delayRepeatMin_2 smallint(5) unsigned, delayRepeatMax_2 smallint(5) unsigned, scriptId_2 mediumint(8) unsigned, spellId_3 smallint(5) unsigned, probability_3 tinyint(3) unsigned, castTarget_3 tinyint(2) unsigned, targetParam1_3 smallint(5) unsigned, targetParam2_3 smallint(5) unsigned, castFlags_3 smallint(5) unsigned, delayInitialMin_3 smallint(5) unsigned, delayInitialMax_3 smallint(5) unsigned, delayRepeatMin_3 smallint(5) unsigned, delayRepeatMax_3 smallint(5) unsigned, scriptId_3 mediumint(8) unsigned, spellId_4 smallint(5) unsigned, probability_4 tinyint(3) unsigned, castTarget_4 tinyint(2) unsigned, targetParam1_4 smallint(5) unsigned, targetParam2_4 smallint(5) unsigned, castFlags_4 smallint(5) unsigned, delayInitialMin_4 smallint(5) unsigned, delayInitialMax_4 smallint(5) unsigned, delayRepeatMin_4 smallint(5) unsigned, delayRepeatMax_4 smallint(5) unsigned, scriptId_4 mediumint(8) unsigned, spellId_5 smallint(5) unsigned, probability_5 tinyint(3) unsigned, castTarget_5 tinyint(2) unsigned, targetParam1_5 smallint(5) unsigned, targetParam2_5 smallint(5) unsigned, castFlags_5 smallint(5) unsigned, delayInitialMin_5 smallint(5) unsigned, delayInitialMax_5 smallint(5) unsigned, delayRepeatMin_5 smallint(5) unsigned, delayRepeatMax_5 smallint(5) unsigned, scriptId_5 mediumint(8) unsigned, spellId_6 smallint(5) unsigned, probability_6 tinyint(3) unsigned, castTarget_6 tinyint(2) unsigned, targetParam1_6 smallint(5) unsigned, targetParam2_6 smallint(5) unsigned, castFlags_6 smallint(5) unsigned, delayInitialMin_6 smallint(5) unsigned, delayInitialMax_6 smallint(5) unsigned, delayRepeatMin_6 smallint(5) unsigned, delayRepeatMax_6 smallint(5) unsigned, scriptId_6 mediumint(8) unsigned, spellId_7 smallint(5) unsigned, probability_7 tinyint(3) unsigned, castTarget_7 tinyint(2) unsigned, targetParam1_7 smallint(5) unsigned, targetParam2_7 smallint(5) unsigned, castFlags_7 smallint(5) unsigned, delayInitialMin_7 smallint(5) unsigned, delayInitialMax_7 smallint(5) unsigned, delayRepeatMin_7 smallint(5) unsigned, delayRepeatMax_7 smallint(5) unsigned, scriptId_7 mediumint(8) unsigned, spellId_8 smallint(5) unsigned, probability_8 tinyint(3) unsigned, castTarget_8 tinyint(2) unsigned, targetParam1_8 smallint(5) unsigned, targetParam2_8 smallint(5) unsigned, castFlags_8 smallint(5) unsigned, delayInitialMin_8 smallint(5) unsigned, delayInitialMax_8 smallint(5) unsigned, delayRepeatMin_8 smallint(5) unsigned, delayRepeatMax_8 smallint(5) unsigned, scriptId_8 mediumint(8) unsigned
- `creature_template`: entry mediumint(8) unsigned PK, patch tinyint(3) unsigned PK, name char(100), subname char(100)?, level_min tinyint(3) unsigned, level_max tinyint(3) unsigned, faction smallint(5) unsigned, npc_flags int(10) unsigned, gossip_menu_id mediumint(8) unsigned, display_id1 mediumint(8) unsigned, display_id2 mediumint(8) unsigned, display_id3 mediumint(8) unsigned, display_id4 mediumint(8) unsigned, display_scale1 float, display_scale2 float, display_scale3 float, display_scale4 float, display_probability1 smallint(5) unsigned, display_probability2 smallint(5) unsigned, display_probability3 smallint(5) unsigned, display_probability4 smallint(5) unsigned, display_total_probability smallint(5) unsigned, mount_display_id smallint(5) unsigned, speed_walk float, speed_run float, detection_range float, call_for_help_range float, leash_range float, type tinyint(3) unsigned, pet_family tinyint(4) unsigned, rank tinyint(3) unsigned, unit_class tinyint(3) unsigned, xp_multiplier float, health_multiplier float, mana_multiplier float, armor_multiplier float, damage_multiplier float, damage_variance float, damage_school tinyint(4) unsigned, base_attack_time int(10) unsigned, ranged_attack_time int(10) unsigned, holy_res smallint(5), fire_res smallint(5), nature_res smallint(5), frost_res smallint(5), shadow_res smallint(5), arcane_res smallint(5), trainer_type tinyint(4) unsigned, trainer_spell smallint(5) unsigned, trainer_class tinyint(3) unsigned, trainer_race tinyint(3) unsigned, loot_id mediumint(8) unsigned, pickpocket_loot_id mediumint(8) unsigned, skinning_loot_id mediumint(8) unsigned, gold_min mediumint(8) unsigned, gold_max mediumint(8) unsigned, spell_id1 smallint(5) unsigned, spell_id2 smallint(5) unsigned, spell_id3 smallint(5) unsigned, spell_id4 smallint(5) unsigned, spell_list_id int(11) unsigned, pet_spell_list_id mediumint(8) unsigned, spawn_spell_id smallint(5) unsigned, auras text?, ai_name char(64), movement_type tinyint(3) unsigned, inhabit_type tinyint(3) unsigned, civilian tinyint(3) unsigned, racial_leader tinyint(3) unsigned, equipment_id mediumint(8) unsigned, trainer_id mediumint(8) unsigned, vendor_id mediumint(8) unsigned, mechanic_immune_mask int(10) unsigned, school_immune_mask int(10) unsigned, immunity_flags int(10) unsigned, static_flags1 int(10) unsigned, static_flags2 int(10) unsigned, flags_extra int(10) unsigned, script_name char(64)
- `gameobject`: guid int(10) unsigned PK, id mediumint(8) unsigned, map smallint(5) unsigned, position_x float, position_y float, position_z float, orientation float, rotation0 float, rotation1 float, rotation2 float, rotation3 float, spawntimesecsmin int(11), spawntimesecsmax int(11), animprogress tinyint(3) unsigned, state tinyint(3) unsigned, spawn_flags int(10) unsigned, visibility_mod float?, patch_min tinyint(3) unsigned, patch_max tinyint(3) unsigned
- `gameobject_template`: entry mediumint(8) unsigned PK, patch tinyint(3) unsigned PK, type tinyint(3) unsigned, displayId mediumint(8) unsigned, name varchar(100), icon varchar(100), faction smallint(5) unsigned, flags int(10) unsigned, size float, data0 int(10), data1 int(11), data2 int(10), data3 int(10), data4 int(10), data5 int(10), data6 int(11), data7 int(10), data8 int(10), data9 int(10), data10 int(10), data11 int(10), data12 int(10), data13 int(10), data14 int(10), data15 int(10), data16 int(10), data17 int(10), data18 int(10), data19 int(10), data20 int(10), data21 int(10), data22 int(10), data23 int(10), mingold mediumint(8) unsigned, maxgold mediumint(8) unsigned, script_name varchar(64)
- `gossip_menu`: entry smallint(6) unsigned PK, text_id mediumint(8) unsigned PK, script_id mediumint(8) unsigned, condition_id mediumint(8) unsigned
- `item_template`: entry mediumint(8) unsigned PK, patch tinyint(3) unsigned PK, class tinyint(3) unsigned, subclass tinyint(3) unsigned, name varchar(255), description varchar(255), display_id mediumint(8) unsigned, quality tinyint(3) unsigned, flags int(10) unsigned, buy_count tinyint(3) unsigned, buy_price int(10) unsigned, sell_price int(10) unsigned, inventory_type tinyint(3) unsigned, allowable_class mediumint(9), allowable_race mediumint(9), item_level tinyint(3) unsigned, required_level tinyint(3) unsigned, required_skill smallint(5) unsigned, required_skill_rank smallint(5) unsigned, required_spell smallint(5) unsigned, required_honor_rank mediumint(8) unsigned, required_city_rank mediumint(8) unsigned, required_reputation_faction smallint(5) unsigned, required_reputation_rank smallint(5) unsigned, max_count smallint(5) unsigned, stackable smallint(5) unsigned, container_slots tinyint(3) unsigned, stat_type1 tinyint(3) unsigned, stat_value1 smallint(6), stat_type2 tinyint(3) unsigned, stat_value2 smallint(6), stat_type3 tinyint(3) unsigned, stat_value3 smallint(6), stat_type4 tinyint(3) unsigned, stat_value4 smallint(6), stat_type5 tinyint(3) unsigned, stat_value5 smallint(6), stat_type6 tinyint(3) unsigned, stat_value6 smallint(6), stat_type7 tinyint(3) unsigned, stat_value7 smallint(6), stat_type8 tinyint(3) unsigned, stat_value8 smallint(6), stat_type9 tinyint(3) unsigned, stat_value9 smallint(6), stat_type10 tinyint(3) unsigned, stat_value10 smallint(6), delay smallint(5) unsigned, range_mod float, ammo_type tinyint(3) unsigned, dmg_min1 float, dmg_max1 float, dmg_type1 tinyint(3) unsigned, dmg_min2 float, dmg_max2 float, dmg_type2 tinyint(3) unsigned, dmg_min3 float, dmg_max3 float, dmg_type3 tinyint(3) unsigned, dmg_min4 float, dmg_max4 float, dmg_type4 tinyint(3) unsigned, dmg_min5 float, dmg_max5 float, dmg_type5 tinyint(3) unsigned, block mediumint(8) unsigned, armor smallint(5), holy_res smallint(5), fire_res smallint(5), nature_res smallint(5), frost_res smallint(5), shadow_res smallint(5), arcane_res smallint(5), spellid_1 smallint(5) unsigned, spelltrigger_1 tinyint(3) unsigned, spellcharges_1 tinyint(4), spellppmrate_1 float, spellcooldown_1 int(11), spellcategory_1 smallint(5) unsigned, spellcategorycooldown_1 int(11), spellid_2 smallint(5) unsigned, spelltrigger_2 tinyint(3) unsigned, spellcharges_2 tinyint(4), spellppmrate_2 float, spellcooldown_2 int(11), spellcategory_2 smallint(5) unsigned, spellcategorycooldown_2 int(11), spellid_3 smallint(5) unsigned, spelltrigger_3 tinyint(3) unsigned, spellcharges_3 tinyint(4), spellppmrate_3 float, spellcooldown_3 int(11), spellcategory_3 smallint(5) unsigned, spellcategorycooldown_3 int(11), spellid_4 smallint(5) unsigned, spelltrigger_4 tinyint(3) unsigned, spellcharges_4 tinyint(4), spellppmrate_4 float, spellcooldown_4 int(11), spellcategory_4 smallint(5) unsigned, spellcategorycooldown_4 int(11), spellid_5 smallint(5) unsigned, spelltrigger_5 tinyint(3) unsigned, spellcharges_5 tinyint(4), spellppmrate_5 float, spellcooldown_5 int(11), spellcategory_5 smallint(5) unsigned, spellcategorycooldown_5 int(11), bonding tinyint(3) unsigned, page_text mediumint(8) unsigned, page_language tinyint(3) unsigned, page_material tinyint(3) unsigned, start_quest mediumint(8) unsigned, lock_id mediumint(8) unsigned, material tinyint(4), sheath tinyint(3) unsigned, random_property mediumint(8) unsigned, set_id mediumint(8) unsigned, max_durability smallint(5) unsigned, area_bound mediumint(8) unsigned, map_bound smallint(6), duration int(11) unsigned, bag_family mediumint(9), disenchant_id mediumint(8) unsigned, food_type tinyint(3) unsigned, min_money_loot int(10) unsigned, max_money_loot int(10) unsigned, wrapped_gift mediumint(8) unsigned, extra_flags tinyint(1) unsigned, other_team_entry int(11) unsigned?
- `npc_vendor_template`: entry mediumint(8) unsigned PK, slot smallint(5) unsigned, item mediumint(8) unsigned PK, maxcount tinyint(3) unsigned, incrtime int(10) unsigned, itemflags int(10) unsigned, condition_id mediumint(8) unsigned
- `quest_template`: entry mediumint(8) unsigned PK, patch tinyint(3) unsigned PK, Method tinyint(3) unsigned, ZoneOrSort smallint(6), MinLevel tinyint(3) unsigned, MaxLevel tinyint(3) unsigned, QuestLevel tinyint(3) unsigned, Type smallint(5) unsigned, RequiredClasses smallint(5) unsigned, RequiredRaces smallint(5) unsigned, RequiredSkill smallint(5) unsigned, RequiredSkillValue smallint(5) unsigned, RequiredCondition mediumint(8) unsigned, RepObjectiveFaction smallint(5) unsigned, RepObjectiveValue mediumint(9), RequiredMinRepFaction smallint(5) unsigned, RequiredMinRepValue mediumint(9), RequiredMaxRepFaction smallint(5) unsigned, RequiredMaxRepValue mediumint(9), SuggestedPlayers tinyint(3) unsigned, LimitTime int(10) unsigned, QuestFlags smallint(5) unsigned, SpecialFlags tinyint(3) unsigned, PrevQuestId mediumint(9), NextQuestId mediumint(9), ExclusiveGroup mediumint(9), BreadcrumbForQuestId mediumint(9) unsigned, NextQuestInChain mediumint(8) unsigned, SrcItemId mediumint(8) unsigned, SrcItemCount tinyint(3) unsigned, SrcSpell smallint(5) unsigned, Title text?, Details text?, Objectives text?, OfferRewardText text?, RequestItemsText text?, EndText text?, ObjectiveText1 text?, ObjectiveText2 text?, ObjectiveText3 text?, ObjectiveText4 text?, ReqItemId1 mediumint(8) unsigned, ReqItemId2 mediumint(8) unsigned, ReqItemId3 mediumint(8) unsigned, ReqItemId4 mediumint(8) unsigned, ReqItemCount1 smallint(5) unsigned, ReqItemCount2 smallint(5) unsigned, ReqItemCount3 smallint(5) unsigned, ReqItemCount4 smallint(5) unsigned, ReqSourceId1 mediumint(8) unsigned, ReqSourceId2 mediumint(8) unsigned, ReqSourceId3 mediumint(8) unsigned, ReqSourceId4 mediumint(8) unsigned, ReqSourceCount1 mediumint(8) unsigned, ReqSourceCount2 mediumint(8) unsigned, ReqSourceCount3 mediumint(8) unsigned, ReqSourceCount4 mediumint(8) unsigned, ReqCreatureOrGOId1 mediumint(9), ReqCreatureOrGOId2 mediumint(9), ReqCreatureOrGOId3 mediumint(9), ReqCreatureOrGOId4 mediumint(9), ReqCreatureOrGOCount1 smallint(5) unsigned, ReqCreatureOrGOCount2 smallint(5) unsigned, ReqCreatureOrGOCount3 smallint(5) unsigned, ReqCreatureOrGOCount4 smallint(5) unsigned, ReqSpellCast1 smallint(5) unsigned, ReqSpellCast2 smallint(5) unsigned, ReqSpellCast3 smallint(5) unsigned, ReqSpellCast4 smallint(5) unsigned, RewChoiceItemId1 mediumint(8) unsigned, RewChoiceItemId2 mediumint(8) unsigned, RewChoiceItemId3 mediumint(8) unsigned, RewChoiceItemId4 mediumint(8) unsigned, RewChoiceItemId5 mediumint(8) unsigned, RewChoiceItemId6 mediumint(8) unsigned, RewChoiceItemCount1 smallint(5) unsigned, RewChoiceItemCount2 smallint(5) unsigned, RewChoiceItemCount3 smallint(5) unsigned, RewChoiceItemCount4 smallint(5) unsigned, RewChoiceItemCount5 smallint(5) unsigned, RewChoiceItemCount6 smallint(5) unsigned, RewItemId1 mediumint(8) unsigned, RewItemId2 mediumint(8) unsigned, RewItemId3 mediumint(8) unsigned, RewItemId4 mediumint(8) unsigned, RewItemCount1 smallint(5) unsigned, RewItemCount2 smallint(5) unsigned, RewItemCount3 smallint(5) unsigned, RewItemCount4 smallint(5) unsigned, RewRepFaction1 smallint(5) unsigned, RewRepFaction2 smallint(5) unsigned, RewRepFaction3 smallint(5) unsigned, RewRepFaction4 smallint(5) unsigned, RewRepFaction5 smallint(5) unsigned, RewRepValue1 mediumint(9), RewRepValue2 mediumint(9), RewRepValue3 mediumint(9), RewRepValue4 mediumint(9), RewRepValue5 mediumint(9), RewRepSpilloverMask tinyint(3) unsigned, RewXP mediumint(9) unsigned, RewOrReqMoney int(11), RewMoneyMaxLevel int(10) unsigned, RewSpell smallint(5) unsigned, RewSpellCast smallint(5) unsigned, RewMailTemplateId mediumint(8), RewMailDelaySecs int(11) unsigned, RewMailMoney int(10) unsigned, PointMapId smallint(5) unsigned, PointX float, PointY float, PointOpt mediumint(8) unsigned, DetailsEmote1 smallint(5) unsigned, DetailsEmote2 smallint(5) unsigned, DetailsEmote3 smallint(5) unsigned, DetailsEmote4 smallint(5) unsigned, DetailsEmoteDelay1 int(11) unsigned, DetailsEmoteDelay2 int(11) unsigned, DetailsEmoteDelay3 int(11) unsigned, DetailsEmoteDelay4 int(11) unsigned, IncompleteEmote smallint(5) unsigned, CompleteEmote smallint(5) unsigned, OfferRewardEmote1 smallint(5) unsigned, OfferRewardEmote2 smallint(5) unsigned, OfferRewardEmote3 smallint(5) unsigned, OfferRewardEmote4 smallint(5) unsigned, OfferRewardEmoteDelay1 int(11) unsigned, OfferRewardEmoteDelay2 int(11) unsigned, OfferRewardEmoteDelay3 int(11) unsigned, OfferRewardEmoteDelay4 int(11) unsigned, StartScript mediumint(8) unsigned, CompleteScript mediumint(8) unsigned

*`?` = nullable, `PK` = primary key column.*

