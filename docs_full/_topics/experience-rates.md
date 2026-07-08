# Experience Gain & Rates

<!-- aliases: xp rates, experience rates, increase xp, xp multiplier, double xp, xp boost, leveling speed, level faster, rested xp, xp gain -->
<!-- documentation: model-written from source via the local LLM; review before trusting -->

Experience gain in VMaNGOS flows through two distinct pipelines: combat kills and quest completions. Both pipelines calculate a base value, apply specific multipliers (elite status, group sharing, level decay), scale by server-wide configuration rates, and finally pass the result through `Player::GiveXP` to handle rested bonuses and level-up logic.

### Combat Kill XP

The process begins when a creature dies. If the killer is solo, `Player::RewardSinglePlayerAtKill` is invoked. If in a group, `Group::RewardGroupAtKill` distributes the XP. Both paths ultimately rely on the `MaNGOS::XP::Gain` function defined in `Formulas.h`.

1.  **Eligibility**: `Formulas/Gain` first checks if the creature grants XP. Creatures flagged as critters, totems, or with `UNIT_STATE_NO_KILL_REWARD` or `CREATURE_STATIC_FLAG_NO_XP` yield 0 XP.
2.  **Base Calculation**: The core formula is `(owner_level * 5 + 45) * BaseGainLevelFactor`.
    *   `Formulas/BaseGainLevelFactor` adjusts this based on the level difference. If the victim is higher level, XP increases by 5% per level up to a cap of 4 levels (+20%). If the victim is lower level, XP decays linearly until the victim becomes "grey" (too low level to grant XP).
3.  **Modifiers**:
    *   **Elite Bonus**: Elite creatures grant 2x XP normally, or 2.5x in non-raid dungeons. This is multiplied by `Rate.XP.Kill.Elite`.
    *   **Pet Penalty**: Pets receive 75% of the calculated XP.
    *   **Template Multiplier**: The creature's `xp_multiplier` from `creature_template` is applied.
    *   **Global Rate**: The final value is multiplied by `Rate.XP.Kill`.
4.  **Distribution**:
    *   **Solo**: The full calculated XP is passed to `Player::GiveXP`.
    *   **Group**: `Group::RewardGroupAtKill` calculates a share based on group size and dungeon status. It calls `RewardGroupAtKill_helper`, which applies a level-based weight (`group_rate * player_level / sum_level`) before calling `GiveXP`.

### Quest XP

When a player completes a quest, `Player::RewardQuest` handles the transaction.

1.  **Base Value**: The raw XP is stored in `quest_template.RewXP`.
2.  **Level Decay**: `QuestDef/XPValue` applies a decay curve if the player is higher level than the quest.
    *   Player Level ≤ Quest Level + 5: 100% XP.
    *   Player Level = Quest Level + 6: 80% XP.
    *   Player Level = Quest Level + 7: 60% XP.
    *   Player Level = Quest Level + 8: 40% XP.
    *   Player Level = Quest Level + 9: 20% XP.
    *   Player Level ≥ Quest Level + 10: 10% XP.
3.  **Global Rate**: The decayed value is multiplied by `Rate.XP.Quest`.
4.  **Max Level Handling**: If the player is at the maximum level, XP is not awarded. Instead, `QuestDef/GetRewMoneyMaxLevelAtComplete` converts the XP to gold (scaled by `Rate.Drop.Money`), provided the server patch is 1.10+ or the specific config allows it.

### Final Application: Rested XP and Leveling

Both combat and quest XP converge at `Player::GiveXP`.

1.  **Trial Restrictions**: Trial accounts stop gaining XP at `TRIAL_MAX_LEVEL`.
2.  **Play Time Flags**: Accounts with `PLAYER_FLAGS_NO_PLAY_TIME` gain nothing. Those with `PLAYER_FLAGS_PARTIAL_PLAY_TIME` gain half XP.
3.  **Personal Rates**: `GetPersonalXpRate()` applies any personal buffs/debuffs (e.g., from items or spells).
4.  **Rested Bonus**: If the XP comes from a kill (`victim` is not null), `GetXPRestBonus` consumes rested XP to double the effective gain. Rested XP accumulation is governed by `Rate.Rest.InGame`, `Rate.Rest.Offline.InTavernOrCity`, and `Rate.Rest.Offline.InWilderness`.
5.  **Level Up**: The XP is added to the current total. If the threshold for the next level is met, `GiveLevel` is called, and the process repeats until the XP is exhausted or the player hits the max level.

## How to Modify

### Config

The following keys in `mangosd.conf` directly scale experience gain. Changes take effect immediately for new calculations (no restart required, though a reload command may be needed depending on server setup).

*   **`Rate.XP.Kill`** (default `1`): Multiplies XP gained from killing creatures. Set to `2` for double XP from mobs.
*   **`Rate.XP.Kill.Elite`** (default `1`): Additional multiplier specifically for elite creatures. Useful if you want elites to scale differently than normal mobs.
*   **`Rate.XP.Quest`** (default `1`): Multiplies XP gained from turning in quests.
*   **`Rate.XP.Explore`** (default `1`): Multiplies XP gained from discovering new zones (exploration).
*   **`Rate.Rest.InGame`** (default `1`): Speed at which rested XP accumulates while online.
*   **`Rate.Rest.Offline.InTavernOrCity`** (default `1`): Speed at which rested XP accumulates while offline in a city/tavern.
*   **`Rate.Rest.Offline.InWilderness`** (default `1`): Speed at which rested XP accumulates while offline in the wilderness.

### Database

*   **`quest_template`**:
    *   **`RewXP`**: The base experience value for the quest. Increasing this raises the pre-rate XP. Note that `QuestDef/XPValue` applies level decay to this value before the `Rate.XP.Quest` config is applied.
    *   **`RewMoneyMaxLevel`**: The gold amount awarded instead of XP if the player is max level. This is scaled by `Rate.Drop.Money`.
*   **`creature_template`**:
    *   **`xp_multiplier`**: A per-creature template multiplier. This is applied *after* the elite bonus but *before* the global `Rate.XP.Kill`. Setting this to `2.0` doubles XP for that specific creature type relative to others.

### Code

*   **Level Decay Curve**: Edit `QuestDef/XPValue` in `QuestDef.cpp` to change the percentages for over-leveled quest completion.
*   **Base Formula**: Edit `Formulas/BaseGain` and `Formulas/BaseGainLevelFactor` in `Formulas.h` to change the fundamental XP calculation (e.g., changing the `+45` constant or the 5% per level bonus).
*   **Elite Multipliers**: Edit `Formulas/Gain` in `Formulas.h` to change the hardcoded `2.5` (dungeon elite) and `2.0` (normal elite) multipliers.
*   **Pet XP**: Edit `Formulas/Gain` in `Formulas.h` to change the `0.75f` penalty for pets.
*   **Group Sharing Logic**: Edit `Group::RewardGroupAtKill_helper` in `Group.cpp` to change how XP is distributed among group members (currently weighted by level).

## Path Reference

**Formulas/BaseGainLevelFactor** (Formulas.h): Computes the level-difference multiplier for kill XP, capping high-level bonuses at +20% and scaling down low-level penalties.
**Formulas/BaseGain** (Formulas.h): Calculates the raw base XP amount using `(owner_level * 5 + 45)` multiplied by the level factor.
**Formulas/Gain** (Formulas.h): The main XP calculation function for kills, applying elite bonuses, pet penalties, template multipliers, and global rates.
**ObjectMgr/LoadAllIdentifiers** (ObjectMgr.cpp): Loads distinct IDs from various tables, including `quest_template`, into memory sets for quick lookup.
**ObjectMgr/LoadQuests** (ObjectMgr.cpp): Queries and parses `quest_template` rows into `Quest` objects, storing them in the quest template map.
**Player.Main/GiveXP** (Player.cpp): The final gatekeeper for XP gain; applies trial restrictions, play-time flags, personal rates, rested bonus consumption, and triggers level-ups.
**Player.Main/RewardQuest** (Player.cpp): Handles the entire quest turn-in transaction, including item rewards, reputation, XP calculation (via `XPValue`), and max-level gold conversion.
**ScriptMgr/LoadQuestEndScripts** (ScriptMgr.cpp): Loads quest completion scripts from the database and cross-references them with `quest_template` to ensure valid script IDs.
**ScriptMgr/LoadQuestStartScripts** (ScriptMgr.cpp): Loads quest start scripts from the database and cross-references them with `quest_template`.
**World/LoadConfigSettings** (World.cpp): Reads and validates all configuration keys, including `Rate.XP.Kill`, `Rate.XP.Quest`, and rest rates, storing them in the world config accessor.
**AiBotAI.Bridge/BridgeHandleQuestInteract** (AiBotAIBridge.cpp): Manages bot quest interactions, including accepting and completing quests, which triggers the standard XP reward pipeline via `RewardQuest`.
**BattleGroundAV/HandleQuestComplete** (BattleGroundAV.cpp): Processes quest turns-ins in Arathi Basin, updating team progress and reputation, but notably does not directly award XP (handled by the caller).
**game_Group_Group/RewardGroupAtKill_helper** (Group.cpp): Helper function that distributes XP to individual group members based on level weighting and distance checks.
**game_Group_Group/RewardGroupAtKill** (Group.cpp): Orchestrates group XP distribution, calculating the total XP pool and iterating through group members to call the helper.
**game_Mail_Mail/MailSender#4** (Mail.cpp): Constructor for `MailSender` that initializes sender details for mail sent during quest rewards (e.g., mailed items).
**game_Mail_Mail/MailDraft#2** (Mail.cpp): Constructor for `MailDraft` that prepares mail templates for quest rewards.
**game_Mail_Mail/MailSender#2** (Mail.h): Alternative constructor for `MailSender` used in various contexts including quest reward mail.
**game_Mail_Mail/SendMailTo** (Mail.cpp): Persists mail to the database and updates in-memory state for online players, used for delivering quest reward items/gold via mail.
**game_Objects_Item/GenerateItemRandomPropertyId** (Item.cpp): Generates random properties for items rewarded by quests, ensuring unique stats for random enchantments.
**Map.Main/ScriptsStart** (Map.cpp): Schedules script execution for quest completion scripts defined in `quest_template.CompleteScript`.
**Object/ToUnit** (Unit.h): Utility method to safely cast an `Object` pointer to a `Unit` pointer, used in quest interaction validation.
**Player.Main/RewardSinglePlayerAtKill** (Player.cpp): Handles XP and reputation rewards for solo kills, calling `MaNGOS::XP::Gain` and `GiveXP`.
**QuestDef/XPValue** (QuestDef.cpp): Calculates the final quest XP by applying the level decay curve to the base `RewXP` value.
**QuestDef/GetRewOrReqMoney** (QuestDef.cpp): Returns the monetary cost or reward for a quest, scaled by the drop money rate.
**QuestDef/GetRewMoneyMaxLevelAtComplete** (QuestDef.cpp): Calculates the gold conversion for max-level players completing quests, respecting patch version and config settings.
**ScriptMgr/OnQuestRewarded** (ScriptMgr.cpp): Dispatches quest reward events to creature scripts, allowing custom behavior upon quest completion.
**ScriptMgr/OnQuestRewarded#2** (ScriptMgr.cpp): Dispatches quest reward events to gameobject scripts.
**SpellMgr/GetSpellAreaForQuestMapBounds** (SpellMgr.h): Retrieves spell areas associated with active quests, used to apply/remove auras upon quest completion.
**SpellMgr/GetSpellAreaForQuestEndMapBounds** (SpellMgr.h): Retrieves spell areas associated with completed quests, used to remove auras upon quest completion.
**SpellMgr/IsFitToRequirements** (SpellMgr.cpp): Checks if a player meets the requirements (race, gender, zone, quest state, aura) for a spell area effect.
**Unit.Main/HasAura** (Unit.cpp): Checks if a unit has a specific aura, used in spell area requirement checks during quest rewards.
**WorldObject.Object/GetZoneAndAreaId** (Object.cpp): Retrieves the current zone and area ID of an object, used for spell area and quest validation.
**WorldSession.Main/HasTrialRestrictions** (WorldSession.cpp): Determines if an account has trial restrictions, which caps XP gain and money amounts.
**WorldSession.QuestHandler/HandleQuestgiverChooseRewardOpcode** (QuestHandler.cpp): Handles the client packet for choosing a quest reward, validating the choice and triggering `RewardQuest`.
**Player.Main/UpdateCraftSkill** (Player.cpp): Updates crafting skill levels, gated by trial restrictions and skill gain configs.
**Player.Main/UpdateGatherSkill** (Player.cpp): Updates gathering skill levels, applying red-level scaling and multipliers.
**Player.Main/UpdateFishingSkill** (Player.cpp): Updates fishing skill levels using a specific chance-based model.
**Player.Main/GetMaxMoney** (Player.cpp): Returns the maximum money cap, which is significantly lower for trial accounts.
**World/AddSession_** (World.cpp): Adds a new session to the world, handling trial restriction flags and billing information.
**World/AddQueuedSession** (World.cpp): Adds a session to the login queue, also setting trial restriction flags.

---

<!-- machine-true, projected from graph.json -->

## Map — Experience Gain & Rates

*Source:* Formulas.h, ObjectMgr.cpp, Player.cpp, ScriptMgr.cpp, World.cpp, AiBotAIBridge.cpp, BattleGroundAV.cpp, Group.cpp, Mail.cpp, Mail.h, Item.cpp, Map.cpp, Unit.h, QuestDef.cpp, SpellMgr.h, SpellMgr.cpp, Unit.cpp, Object.cpp, WorldSession.cpp, QuestHandler.cpp
*Config keys:* Rate.XP.Kill (default 1), Rate.XP.Kill.Elite (default 1), Rate.XP.Quest (default 1), Rate.XP.Explore (default 1), Rate.Rest.InGame (default 1), Rate.Rest.Offline.InTavernOrCity (default 1), Rate.Rest.Offline.InWilderness (default 1)
*Tables:* quest_template

| Member | Kind | Source | Role |
|---|---|---|---|
| Formulas/BaseGainLevelFactor | function | Formulas.h:76-95 | seed — Formulas/BaseGain* |
| Formulas/BaseGain | function | Formulas.h:97-101 | seed — Formulas/BaseGain* |
| Formulas/Gain | function | Formulas.h:103-159 | seed — Formulas/Gain |
| ObjectMgr/LoadAllIdentifiers | method | ObjectMgr.cpp:180-328 | seed — queries quest_template |
| ObjectMgr/LoadQuests | method | ObjectMgr.cpp:5314-6030 | seed — queries quest_template |
| Player.Main/GiveXP | method | Player.cpp:3061-3110 | seed — Player.*/GiveXP* |
| Player.Main/RewardQuest | method | Player.cpp:13205-13373 | seed — Player.*/RewardQuest* |
| ScriptMgr/LoadQuestEndScripts | method | ScriptMgr.cpp:1452-1474 | seed — queries quest_template |
| ScriptMgr/LoadQuestStartScripts | method | ScriptMgr.cpp:1476-1498 | seed — queries quest_template |
| World/LoadConfigSettings | method | World.cpp:440-1245 | seed — reads config Rate.XP.Kill |
| AiBotAI.Bridge/BridgeHandleQuestInteract | method | AiBotAIBridge.cpp:928-1163 | related — 1 hop from a seed |
| BattleGroundAV/HandleQuestComplete | method | BattleGroundAV.cpp:500-771 | related — 1 hop from a seed |
| game_Group_Group/RewardGroupAtKill_helper | function | Group.cpp:2281-2339 | related — 1 hop from a seed |
| game_Group_Group/RewardGroupAtKill | method | Group.cpp:2348-2395 | related — 1 hop from a seed |
| game_Mail_Mail/MailSender#4 | ctor | Mail.cpp:49-76 | related — 1 hop from a seed |
| game_Mail_Mail/MailDraft#2 | ctor | Mail.cpp:113-118 | related — 1 hop from a seed |
| game_Mail_Mail/MailSender#2 | ctor | Mail.h:129-132 | related — 1 hop from a seed |
| game_Mail_Mail/SendMailTo | method | Mail.cpp:300-386 | related — 1 hop from a seed |
| game_Objects_Item/GenerateItemRandomPropertyId | method | Item.cpp:785-807 | related — 1 hop from a seed |
| Map.Main/ScriptsStart | method | Map.cpp:2510-2535 | related — 1 hop from a seed |
| Object/ToUnit | method | Unit.h:1453-1456 | related — 1 hop from a seed |
| Player.Main/RewardSinglePlayerAtKill | method | Player.cpp:20004-20029 | related — 1 hop from a seed |
| QuestDef/XPValue | method | QuestDef.cpp:176-198 | related — 1 hop from a seed |
| QuestDef/GetRewOrReqMoney | method | QuestDef.cpp:200-206 | related — 1 hop from a seed |
| QuestDef/GetRewMoneyMaxLevelAtComplete | method | QuestDef.cpp:208-221 | related — 1 hop from a seed |
| ScriptMgr/OnQuestRewarded | method | ScriptMgr.cpp:1954-1964 | related — 1 hop from a seed |
| ScriptMgr/OnQuestRewarded#2 | method | ScriptMgr.cpp:1966-1976 | related — 1 hop from a seed |
| SpellMgr/GetSpellAreaForQuestMapBounds | method | SpellMgr.h:656-662 | related — 1 hop from a seed |
| SpellMgr/GetSpellAreaForQuestEndMapBounds | method | SpellMgr.h:664-667 | related — 1 hop from a seed |
| SpellMgr/IsFitToRequirements | method | SpellMgr.cpp:3048-3099 | related — 1 hop from a seed |
| Unit.Main/HasAura | method | Unit.cpp:4290-4298 | related — 1 hop from a seed |
| WorldObject.Object/GetZoneAndAreaId | method | Object.cpp:1563-1567 | related — 1 hop from a seed |
| WorldSession.Main/HasTrialRestrictions | method | WorldSession.cpp:342-345 | related — 1 hop from a seed |
| WorldSession.QuestHandler/HandleQuestgiverChooseRewardOpcode | method | QuestHandler.cpp:407-452 | related — 1 hop from a seed |
| Player.Main/UpdateCraftSkill | method | Player.cpp:5377-5408 | related — 2 hops from a seed |
| Player.Main/UpdateGatherSkill | method | Player.cpp:5410-5444 | related — 2 hops from a seed |
| Player.Main/UpdateFishingSkill | method | Player.cpp:5446-5459 | related — 2 hops from a seed |
| Player.Main/GetMaxMoney | method | Player.cpp:14326-14332 | related — 2 hops from a seed |
| World/AddSession_ | method | World.cpp:255-351 | related — 2 hops from a seed |
| World/AddQueuedSession | method | World.cpp:364-381 | related — 2 hops from a seed |

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

