# QuestDef

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# QuestDef

**Purpose & Responsibilities**

The `Quest` class, defined in `QuestDef.h` and implemented in `QuestDef.cpp`, serves as the core data structure representing a single quest definition within the WowVMaNGOS server. It acts as a lightweight wrapper around static quest data loaded from the database (via `ObjectMgr`), providing efficient accessors for quest properties, rewards, requirements, and metadata.

Key responsibilities include:
1.  **Data Storage:** Holding all static fields associated with a quest (IDs, levels, text strings, item IDs, creature counts, etc.) as parsed from the database record.
2.  **Derived State Calculation:** Computing cached counts for required items, creatures/game objects, and reward choices during construction to avoid repeated iteration during gameplay logic.
3.  **Dynamic Reward Adjustment:** Implementing server-side logic to adjust experience points (`XPValue`) based on player level relative to quest level, and adjusting monetary rewards (`GetRewOrReqMoney`, `GetRewMoneyMaxLevelAtComplete`) based on server configuration rates and patch-specific rules (e.g., XP-to-Gold conversion at max level).
4.  **Flag Management:** Providing bit-mask checks for quest flags (`HasQuestFlag`, `HasSpecialFlag`) that dictate behavior such as shareability, raid eligibility, auto-completion, and repeatability.
5.  **Activity Control:** Managing whether a quest is currently active in the world via `IsActive` and `SetQuestActiveState`, allowing game events or scripts to enable/disable quests dynamically.

This unit does not handle quest state tracking (which is managed by `Player` and `QuestStatusData`); it strictly defines *what* a quest is.

## Member-by-Member Behavior

### Construction and Initialization
*   **`Quest` (Constructor):** Initializes the `Quest` object from a `Field*` pointer representing a row from the database query. It maps specific column indices to member variables. Notably, it handles the `QuestMethod` field to determine initial activity state: if `QUEST_METHOD_DISABLED` is set, `m_isActive` is set to `false`. It also iterates through fixed-size arrays (e.g., `ReqItemId`, `RewChoiceItemId`) to populate cached counters (`m_reqitemscount`, `m_rewchoiceitemscount`, etc.), ensuring these counts reflect only non-zero entries.

### Experience and Money Calculations
*   **`XPValue`:** Calculates the experience points awarded to a player for completing the quest. It applies a decay curve based on the difference between the player's level and the quest's level. Full XP is awarded if the player is within 5 levels of the quest level; XP decreases by 20% increments for each level above that threshold, down to 10% at 10+ levels higher. If `RewXP` is zero or negative, it returns 0.
*   **`GetRewOrReqMoney`:** Returns the monetary cost (if negative) or reward (if positive) associated with the quest. If the value is positive (a reward), it multiplies the base amount by the server configuration `CONFIG_FLOAT_RATE_DROP_MONEY` to apply global drop rate modifiers. Negative values (costs) are returned unchanged.
*   **`GetRewMoneyMaxLevelAtComplete`:** Determines the gold reward converted from experience for players at the maximum level. It checks two conditions: the server config `CONFIG_BOOL_NO_QUEST_XP_TO_GOLD` must be disabled, and the current WoW patch must be 1.10.0 or higher (referencing the historical introduction of this mechanic). If valid, it calculates the reward by multiplying `RewMoneyMaxLevel` by the `CONFIG_FLOAT_RATE_DROP_MONEY` rate.

### Flag and Status Accessors
*   **`GetQuestFlags`:** Returns the raw `m_QuestFlags` bitmask.
*   **`HasQuestFlag`:** Checks if a specific `QuestFlags` bit is set in `m_QuestFlags`.
*   **`HasSpecialFlag`:** Checks if a specific `QuestSpecialFlags` bit is set in `m_SpecialFlags`.
*   **`SetSpecialFlag`:** Sets a specific `QuestSpecialFlags` bit in `m_SpecialFlags`. This is typically used by scripts or event managers to mark quests as repeatable or exploration-based after loading.
*   **`IsRepeatable`:** Convenience accessor checking for `QUEST_SPECIAL_FLAG_REPEATABLE`.
*   **`IsAutoComplete`:** Returns `true` if `QuestMethod` is `QUEST_METHOD_AUTOCOMPLETE` (value 0). Auto-complete quests bypass the standard turn-in dialog and reward immediately upon objective completion.
*   **`IsAllowedInRaid`:** Determines if the quest can be completed while in a raid group. It returns `true` if the quest type is `QUEST_TYPE_RAID` or if the `QUEST_FLAGS_RAID` flag is set. Otherwise, it falls back to the server configuration `CONFIG_BOOL_QUEST_IGNORE_RAID`, which globally permits or denies raid completion for non-raid quests.
*   **`IsActive`:** Returns the boolean `m_isActive` state, indicating if the quest is currently enabled in the world.
*   **`SetQuestActiveState`:** Updates `m_isActive` to enable or disable the quest.

### Data Accessors (Getters)
The remaining methods are simple getters returning protected member variables populated during construction. They provide controlled access to quest properties:

*   **Identification & Hierarchy:** `GetQuestId`, `GetPrevQuestId`, `GetNextQuestId`, `GetExclusiveGroup`, `GetBreadcrumbForQuestId`, `GetNextQuestInChain`.
*   **Requirements:** `GetMinLevel`, `GetMaxLevel`, `GetQuestLevel`, `GetType`, `GetRequiredClasses`, `GetRequiredRaces`, `GetRequiredSkill`, `GetRequiredSkillValue`, `GetRequiredCondition`, `GetRequiredMinRepFaction`, `GetRequiredMinRepValue`, `GetRequiredMaxRepFaction`, `GetRequiredMaxRepValue`, `GetRepObjectiveFaction`, `GetRepObjectiveValue`.
*   **Source Items/Spells:** `GetSrcItemId`, `GetSrcItemCount`, `GetSrcSpell`.
*   **Text Content:** `GetTitle`, `GetDetails`, `GetObjectives`, `GetOfferRewardText`, `GetRequestItemsText`, `GetEndText`. Note that localized text vectors are stored in `QuestLocale` (not part of this class's direct members but associated via `ObjectMgr`).
*   **Rewards:** `GetRewMoneyMaxLevel`, `GetRewRepSpilloverMask`, `GetRewXP`, `GetRewSpell`, `GetRewSpellCast`, `GetRewMailTemplateId`, `GetRewMailDelaySecs`, `GetRewMailMoney`.
*   **Location & Emotes:** `GetPointMapId`, `GetPointX`, `GetPointY`, `GetPointOpt`, `GetIncompleteEmote`, `GetCompleteEmote`.
*   **Scripts:** `GetQuestStartScript`, `GetQuestCompleteScript`.
*   **Counts:** `GetReqItemsCount`, `GetReqCreatureOrGOcount`, `GetRewChoiceItemsCount`, `GetRewItemsCount`. These return the pre-calculated cached values from construction.

### Utility Functions
*   **`QuestStatusToString`:** A free function (declared in the header) that converts a `QuestStatus` enum value to a human-readable string constant (e.g., "COMPLETE", "INCOMPLETE"). Used primarily for debugging or chat commands.

## Cross-Unit Boundaries

*   **`ObjectMgr` (Calls `Quest` constructor, `SetSpecialFlag`, `HasSpecialFlag`, `GetQuestId`, etc.):**
    *   *Direction:* `ObjectMgr` creates `Quest` instances.
    *   *Collaboration:* `ObjectMgr::LoadQuests` iterates over database results, constructing `Quest` objects via the `Quest` constructor. It then populates linked lists (previous/next quests) and sets special flags using `SetSpecialFlag`. `ObjectMgr` relies on `Quest` getters to build lookup tables and resolve quest chains.
*   **`Player.Main` (Calls `XPValue`, `GetRewOrReqMoney`, `GetRewMoneyMaxLevelAtComplete`, `HasQuestFlag`, `HasSpecialFlag`, `IsRepeatable`, `IsAutoComplete`, `IsActive`, `Get...` accessors):**
    *   *Direction:* `Player` reads from `Quest`.
    *   *Collaboration:* During quest acceptance, completion, and reward processing, `Player` methods consult `Quest` to verify requirements (level, race, skill), calculate rewards (XP, money, items), check flags (auto-complete, raid allowed), and update quest status. `Player::RewardQuest` uses `XPValue` and `GetRewOrReqMoney` to grant rewards. `Player::CanCompleteQuest` uses various `GetRequired...` methods.
*   **`GossipDef` (Calls `GetTitle`, `GetDetails`, `GetObjectives`, `GetOfferRewardText`, `GetRequestItemsText`, `GetRewSpell`, `GetIncompleteEmote`, `GetCompleteEmote`, `GetReqItemsCount`, `GetRewChoiceItemsCount`, `GetRewItemsCount`, `HasQuestFlag`):**
    *   *Direction:* `GossipDef` reads from `Quest`.
    *   *Collaboration:* When displaying quest menus to players, `GossipDef` retrieves text strings and reward details from `Quest` to construct the appropriate network packets (`SMSG_QUESTGIVER_QUEST_DETAILS`, `SMSG_QUESTGIVER_OFFER_REWARD`, etc.).
*   **`WorldSession.QuestHandler` (Calls `GetQuestFlags`, `GetQuestMethod`, `GetZoneOrSort`, `GetQuestLevel`, `GetType`, `GetRepObjectiveFaction`, `GetRepObjectiveValue`, `GetNextQuestInChain`, `GetSrcItemId`, `GetTitle`, `GetDetails`, `GetObjectives`, `GetEndText`, `GetRewMoneyMaxLevel`, `GetRewSpell`, `GetPointMapId`, `GetPointX`, `GetPointY`, `GetPointOpt`, `IsRepeatable`, `IsAutoComplete`, `IsActive`):**
    *   *Direction:* `WorldSession.QuestHandler` reads from `Quest`.
    *   *Collaboration:* Handles incoming quest-related opcodes from clients. It queries `Quest` data to validate requests, send quest query responses, and manage quest log updates.
*   **`ChatHandler` (Calls `QuestStatusToString`, `GetQuestId`, `GetQuestLevel`, `GetPrevQuestId`, `GetNextQuestInChain`, `GetTitle`):**
    *   *Direction:* `ChatHandler` reads from `Quest`.
    *   *Collaboration:* Provides administrative commands to view quest information, status, and chains. Uses `QuestStatusToString` for display.
*   **`World` (Called by `GetRewOrReqMoney`, `GetRewMoneyMaxLevelAtComplete`, `IsAllowedInRaid`):**
    *   *Direction:* `Quest` reads from `World`.
    *   *Collaboration:* `Quest` methods access global server configurations (`getConfig`) and patch version (`GetWowPatch`) to dynamically adjust reward calculations and raid permissions.
*   **`GameEventMgr.Main` (Calls `SetQuestActiveState`):**
    *   *Direction:* `GameEventMgr` writes to `Quest`.
    *   *Collaboration:* Enables or disables quests based on active game events by calling `SetQuestActiveState`.
*   **`ScriptMgr` / `ScriptedEscortAI` / `ScriptedFollowerAI` (Calls `HasSpecialFlag`, `GetQuestId`, `GetQuestStartScript`, `GetQuestCompleteScript`):**
    *   *Direction:* Scripts read from `Quest`.
    *   *Collaboration:* Custom scripts check quest flags or retrieve script IDs to trigger custom behaviors on quest start/complete.

## Data Model

The `Quest` class does not directly interact with database tables. It consumes data passed to its constructor via a `Field*` pointer, which originates from a query executed by `ObjectMgr`. The underlying database table is implicitly `quest_template` (standard MaNGOS/WowVMaNGOS schema), but the `Quest` unit itself contains no SQL queries or table references. The constructor maps specific column indices (0–130+) to member variables, assuming a fixed schema order.

## Notable Implementation Details

1.  **Fixed Array Sizes:** The class uses compile-time constants (`QUEST_OBJECTIVES_COUNT`, `QUEST_REWARDS_COUNT`, etc.) to define fixed-size arrays for requirements and rewards. This reflects the static nature of the original World of Warcraft client/server protocol limits. Iterating these arrays in the constructor to count non-zero entries is a performance optimization, avoiding repeated scans during gameplay.
2.  **XP Decay Curve:** The `XPValue` method implements a specific, hard-coded decay curve for XP rewards based on level difference. This logic is critical for balancing and must match client expectations. It uses `ceilf` to round up fractional XP values.
3.  **Patch-Specific Logic:** `GetRewMoneyMaxLevelAtComplete` contains explicit logic tied to `WOW_PATCH_110`. This reflects a historical change in World of Warcraft where XP was converted to gold at max level. The code checks both the server config and the patch version to ensure backward compatibility or correct emulation for different expansions.
4.  **Disabled Quest Handling:** The constructor checks `QuestMethod` for `QUEST_METHOD_DISABLED`. If set, it marks the quest as inactive (`m_isActive = false`). However, it also includes a comment: "Leave invalid entries to be caught by ObjectMgr," suggesting that `ObjectMgr` performs additional validation beyond just the disabled flag.
5.  **Bitmask Flags:** `m_QuestFlags` and `m_SpecialFlags` are used extensively. `m_QuestFlags` are typically sent to the client and affect UI behavior (e.g., hidden rewards, party accept). `m_SpecialFlags` are server-side only (e.g., repeatable, exploration) and can be modified post-construction by scripts or event managers.
6.  **Money Rate Application:** `GetRewOrReqMoney` and `GetRewMoneyMaxLevelAtComplete` both apply `CONFIG_FLOAT_RATE_DROP_MONEY` to positive money values. This allows server administrators to scale quest gold rewards globally without modifying database entries. Negative values (costs) are exempt from this scaling.
7.  **Raid Eligibility Fallback:** `IsAllowedInRaid` first checks quest-specific flags/types. If neither indicates raid eligibility, it falls back to a global server config (`CONFIG_BOOL_QUEST_IGNORE_RAID`). This provides flexibility for servers that want to allow or disallow raid completion for all non-raid quests.

## Member Reference

**Quest** (Constructor): Initializes the `Quest` object from a database `Field*`. Maps column indices to member variables, sets initial activity state based on `QuestMethod`, and calculates cached counts for required/reward items and creatures.

**QuestStatusToString**: Converts a `QuestStatus` enum to a string literal for debugging/display purposes.

**XPValue**: Calculates experience points awarded based on player level vs. quest level, applying a decay curve for higher-level players.

**GetRewOrReqMoney**: Returns the quest's monetary cost or reward, applying the server's drop money rate multiplier to positive rewards.

**GetRewMoneyMaxLevelAtComplete**: Calculates gold converted from XP for max-level players, respecting patch version (1.10+) and server config settings.

**GetQuestFlags**: Returns the raw `m_QuestFlags` bitmask.

**HasQuestFlag**: Checks if a specific `QuestFlags` bit is set.

**HasSpecialFlag**: Checks if a specific `QuestSpecialFlags` bit is set.

**SetSpecialFlag**: Sets a specific `QuestSpecialFlags` bit.

**GetQuestId**: Returns the unique quest identifier.

**GetQuestMethod**: Returns the quest method (autocomplete, disabled, deliver).

**GetZoneOrSort**: Returns the zone or sort order ID.

**GetMinLevel**: Returns the minimum player level required.

**GetMaxLevel**: Returns the maximum player level allowed.

**GetQuestLevel**: Returns the quest's designated level.

**GetType**: Returns the quest type (elite, life, PVP, raid, etc.).

**IsAllowedInRaid**: Determines if the quest can be completed in a raid, checking quest flags/type first, then falling back to server config.

**GetRequiredClasses**: Returns bitmask of required classes.

**GetRequiredRaces**: Returns bitmask of required races.

**GetRequiredSkill**: Returns the required skill ID.

**GetRequiredSkillValue**: Returns the required skill level.

**GetRequiredCondition**: Returns the required condition ID.

**GetRepObjectiveFaction**: Returns the faction ID for reputation objectives.

**GetRepObjectiveValue**: Returns the required reputation value for objectives.

**GetRequiredMinRepFaction**: Returns the faction ID for minimum reputation requirement.

**GetRequiredMinRepValue**: Returns the minimum reputation value required.

**GetRequiredMaxRepFaction**: Returns the faction ID for maximum reputation requirement.

**GetRequiredMaxRepValue**: Returns the maximum reputation value allowed.

**GetSuggestedPlayers**: Returns the suggested number of players.

**GetLimitTime**: Returns the time limit for the quest (if timed).

**GetPrevQuestId**: Returns the ID of the previous quest in the chain.

**GetNextQuestId**: Returns the ID of the next quest in the chain.

**GetExclusiveGroup**: Returns the exclusive group ID.

**GetBreadcrumbForQuestId**: Returns the breadcrumb quest ID.

**GetNextQuestInChain**: Returns the next quest ID in the chain.

**GetSrcItemId**: Returns the source item ID.

**GetSrcItemCount**: Returns the source item count.

**GetSrcSpell**: Returns the source spell ID.

**GetTitle**: Returns the quest title string.

**GetDetails**: Returns the quest details string.

**GetObjectives**: Returns the quest objectives string.

**GetOfferRewardText**: Returns the offer reward text string.

**GetRequestItemsText**: Returns the request items text string.

**GetEndText**: Returns the end text string.

**GetRewMoneyMaxLevel**: Returns the maximum level money reward value.

**GetRewRepSpilloverMask**: Returns the reputation spillover mask.

**GetRewXP**: Returns the base experience reward value.

**GetRewSpell**: Returns the reward spell ID.

**GetRewSpellCast**: Returns the reward spell cast count.

**GetRewMailTemplateId**: Returns the mail template ID.

**GetRewMailDelaySecs**: Returns the mail delay in seconds.

**GetRewMailMoney**: Returns the money sent via mail.

**GetPointMapId**: Returns the map ID for the quest point.

**GetPointX**: Returns the X coordinate for the quest point.

**GetPointY**: Returns the Y coordinate for the quest point.

**GetPointOpt**: Returns the point option/flag.

**GetIncompleteEmote**: Returns the emote played when quest is incomplete.

**GetCompleteEmote**: Returns the emote played when quest is complete.

**GetQuestStartScript**: Returns the script ID for quest start.

**GetQuestCompleteScript**: Returns the script ID for quest completion.

**IsRepeatable**: Checks if the quest is marked as repeatable.

**IsAutoComplete**: Checks if the quest auto-completes upon objective fulfillment.

**SetQuestActiveState**: Enables or disables the quest globally.

**IsActive**: Returns whether the quest is currently active.

**GetReqItemsCount**: Returns the cached count of required items.

**GetReqCreatureOrGOcount**: Returns the cached count of required creatures/game objects.

**GetRewChoiceItemsCount**: Returns the cached count of reward choice items.

**GetRewItemsCount**: Returns the cached count of guaranteed reward items.

---

<!-- machine-true, projected from graph.json -->

## Map — QuestDef

*Source:* QuestDef.cpp, QuestDef.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Quest | ctor | Field/GetCppString, Field/GetFloat, Field/GetInt32, Field/GetUInt16, Field/GetUInt32, Field/GetUInt8 | — | — |
| QuestStatusToString | function | — | ChatHandler.CharacterCommands/HandleQuestStatusCommand | — |
| XPValue | method | — | Player.Main/RewardQuest | — |
| GetRewOrReqMoney | method | World/getConfig#2 | GossipDef/SendQuestGiverOfferReward, GossipDef/SendQuestGiverQuestDetails, GossipDef/SendQuestGiverRequestItems, Player.Main/CanCompleteQuest, Player.Main/CanRewardQuest, Player.Main/FullQuestComplete, Player.Main/MoneyChanged, Player.Main/RewardQuest, Player.Main/SendQuestReward, WorldSession.QuestHandler/HandleQuestQueryOpcode | — |
| GetRewMoneyMaxLevelAtComplete | method | World/getConfig, World/getConfig#2, World/GetWowPatch | Player.Main/RewardQuest, Player.Main/SendQuestReward | — |
| GetQuestFlags | method | — | WorldSession.QuestHandler/HandleQuestQueryOpcode | — |
| HasQuestFlag | method | — | GossipDef/SendQuestGiverQuestDetails, ObjectMgr/LoadQuests, Player.Main/CanCompleteQuest, Player.Main/CanShareQuest, Player.Main/CastedCreatureOrGO, Player.Main/CompleteQuest, WorldSession.QuestHandler/GetDialogStatus, WorldSession.QuestHandler/HandleQuestConfirmAccept, WorldSession.QuestHandler/HandleQuestgiverAcceptQuestOpcode, WorldSession.QuestHandler/HandleQuestQueryOpcode | — |
| HasSpecialFlag | method | — | ObjectMgr/LoadQuestAreaTriggers, ObjectMgr/LoadQuests, Player.Main/AddQuest, Player.Main/AdjustQuestReqItemCount, Player.Main/CanCompleteQuest, Player.Main/CanCompleteRepeatableQuest, Player.Main/CanRewardQuest, Player.Main/CastedCreatureOrGO, Player.Main/ChangeQuestsForRace, Player.Main/FailQuest, Player.Main/ItemAddedQuestCheck, Player.Main/ItemRemovedQuestCheck, Player.Main/KilledMonsterCredit, Player.Main/RemoveQuestAtSlot, Player.Main/SatisfyQuestTimed, Player.Main/TalkedToCreature, Player.Main/_LoadQuestStatus, ScriptMgr/LoadScripts | — |
| SetSpecialFlag | method | — | ObjectMgr/LoadQuestAreaTriggers, ObjectMgr/LoadQuests, ScriptMgr/LoadScripts | — |
| GetQuestId | method | — | AiBotAI.Loot/ChooseQuestReward, arathi_highlands/QuestAccept_npc_kinelory, arathi_highlands/QuestAccept_npc_professor_phizzlethorpe, arathi_highlands/QuestAccept_npc_shakes_o_breen, ashenvale/QuestAccept_npc_feero_ironhand, ashenvale/QuestAccept_npc_ruul_snowhoof, ashenvale/QuestAccept_npc_torek, blackrock_depths/QuestAccept_npc_marshal_windsor, blackrock_depths/QuestRewarded_npc_mistress_nagmara, blackrock_depths/QuestRewarded_npc_rocknot, boss_celebras_the_cursed/QuestAccept_celebras_spirit, boss_vaelastrasz/QuestAccept_vaelastrasz, burning_steppes/QuestAccept_npc_grark_lorkrub, ChatHandler.CharacterCommands/HandleQuestStatusCommand, ChatHandler.LookupCommands/HandleLookupQuestCommand, ChatHandler.LookupCommands/ShowQuestListHelper, darkshore/QuestAcceptGO_beached_sea, darkshore/QuestAccept_npc_kerlonian, darkshore/QuestAccept_npc_prospector_remtravel, darkshore/QuestAccept_npc_therylune, darkshore/QuestAccept_npc_volcor, darkshore/QuestComplete_npc_terenthis, darkshore/StartEscort, desolace/QuestAccept_npc_cork_gizelton, desolace/QuestAccept_npc_dalinda_malem, desolace/QuestAccept_npc_melizza_brimbuzzle, desolace/QuestAccept_npc_rigger_gizelton, duskwood/QuestRewarded_npc_sirra_vonindi, dustwallow_marsh/QuestAccept_npc_stinky_ignatz, dustwallow_marsh/QuestRewarded_npc_archmage_tervosh, eastern_plaguelands/QuestAccept_npc_eris_havenfire, felwood/QuestAccept_npc_arei, felwood/QuestAccept_npc_captured_arkonarin, feralas/QuestAccept_npc_kindal_moonweaver, feralas/QuestAccept_npc_shay_leafrunner, gnomeregan/QuestAccept_npc_kernobee, GossipDef/SendQuestGiverOfferReward, GossipDef/SendQuestGiverQuestDetails, GossipDef/SendQuestGiverRequestItems, hinterlands/QuestAccept_npc_rinji, instance_dire_maul/QuestRewarded_go_broken_trap, instance_dire_maul/QuestRewarded_npc_knot_thimblejack, loch_modan/QuestAccept_npc_miran, moonglade/QuestAccept_npc_keeper_remulos, npcs_special/QuestAccept_npc_doctor, npcs_special/QuestRewarded_npc_kwee_peddlefeet, npcs_special/QuestRewarded_npc_riggle_bassbait, ObjectMgr/LoadConditions, ObjectMgr/LoadQuests, Player.Main/AddQuest, Player.Main/CanGiveQuestSourceItemIfNeed, Player.Main/CanRewardQuest, Player.Main/CanRewardQuest#2, Player.Main/ChangeQuestsForRace, Player.Main/RewardQuest, Player.Main/SatisfyQuestExclusiveGroup, Player.Main/SatisfyQuestStatus, Player.Main/SendQuestConfirmAccept, Player.Main/SendQuestReward, Player.Main/SendQuestUpdateAddCreatureOrGo, Player.Main/SendQuestUpdateAddItem, quest_stormwind_rendezvous/QuestAccept_npc_reginald_windsor, razorfen_downs/QuestAccept_npc_belnistrasz, razorfen_kraul/QuestAccept_npc_willix_the_importer, redridge_mountains/QuestAccept_npc_corporal_keeshan, ScriptedEscortAI/JustDied, ScriptedFollowerAI/JustDied, ScriptedFollowerAI/UpdateAI, searing_gorge/QuestAccept_npc_dying_archaeologist, silithus/QuestAcceptGO_crystalline_tear, silithus/QuestComplete_npc_Geologist_Larksbane, silithus/QuestRewarded_scarab_gong, silverpine_forest/QuestAccept_npc_deathstalker_erland, stonetalon_mountains/QuestAccept_npc_piznik, stormwind_city/QuestAccept_npc_bartleby, stormwind_city/QuestAccept_npc_dashel_stonefist, stranglethorn_vale/QuestRewarded_npc_witch_doctor_unbagwa, swamp_of_sorrows/QuestAccept_npc_galen_goodward, tanaris/QuestAccept_npc_tooga, tanaris/QuestRewarded_npc_yehkinya, teldrassil/QuestAccept_npc_mist, teldrassil/QuestComplete_npc_treshala_fallowbrook, the_barrens/QuestAccept_npc_gilthares, the_barrens/QuestAccept_npc_wizzlecranks_shredder, thousand_needles/QuestAccept_npc_lakota_windsong, thousand_needles/QuestAccept_npc_paoka_swiftmountain, ThreatListCopier.battleground_alterac/GossipHello_npc_AVBlood_collector, ThreatListCopier.battleground_alterac/QuestComplete_AV_npc_troops_chief, ThreatListCopier.battleground_alterac/QuestComplete_npc_AVBlood_collector, ungoro_crater/QuestAccept_npc_ame01, ungoro_crater/QuestAccept_npc_ringo, westfall/QuestAccept_npc_daphne_stilwell, wetlands/QuestAccept_npc_mikhail, WorldSession.QuestHandler/HandlePushQuestToParty, WorldSession.QuestHandler/HandleQuestgiverAcceptQuestOpcode, WorldSession.QuestHandler/HandleQuestQueryOpcode | — |
| GetQuestMethod | method | — | ObjectMgr/LoadQuests, WorldSession.QuestHandler/HandleQuestQueryOpcode | — |
| GetZoneOrSort | method | — | WorldSession.QuestHandler/HandleQuestQueryOpcode | — |
| GetMinLevel | method | — | Player.Main/CanSeeStartQuest, Player.Main/SatisfyQuestLevel | — |
| GetMaxLevel | method | — | Player.Main/CanTakeQuest | — |
| GetQuestLevel | method | — | ChatHandler.LookupCommands/ShowQuestListHelper, GossipDef/SendGossipMenu, GossipDef/SendQuestGiverQuestList, WorldSession.QuestHandler/HandleQuestQueryOpcode | — |
| GetType | method | — | Player.Main/AddQuest, WorldSession.QuestHandler/HandleQuestQueryOpcode | — |
| IsAllowedInRaid | method | World/getConfig | Player.Main/HasQuestForGO, Player.Main/HasQuestForItem, Player.Main/KilledMonsterCredit, WorldSession.QuestHandler/HandleQuestConfirmAccept | — |
| GetRequiredClasses | method | — | Player.Main/SatisfyQuestClass | — |
| GetRequiredRaces | method | — | Player.Main/SatisfyQuestRace | — |
| GetRequiredSkill | method | — | Player.Main/SatisfyQuestSkill, Player.Main/SetSkill | — |
| GetRequiredSkillValue | method | — | Player.Main/SatisfyQuestSkill | — |
| GetRequiredCondition | method | — | Player.Main/SatisfyQuestCondition | — |
| GetRepObjectiveFaction | method | — | Player.Main/AddQuest, Player.Main/CanCompleteQuest, Player.Main/FullQuestComplete, Player.Main/ReputationChanged, WorldSession.QuestHandler/HandleQuestQueryOpcode | — |
| GetRepObjectiveValue | method | — | Player.Main/CanCompleteQuest, Player.Main/FullQuestComplete, Player.Main/ReputationChanged, WorldSession.QuestHandler/HandleQuestQueryOpcode | — |
| GetRequiredMinRepFaction | method | — | Player.Main/SatisfyQuestReputation | — |
| GetRequiredMinRepValue | method | — | Player.Main/SatisfyQuestReputation | — |
| GetRequiredMaxRepFaction | method | — | Player.Main/SatisfyQuestReputation | — |
| GetRequiredMaxRepValue | method | — | Player.Main/SatisfyQuestReputation | — |
| GetSuggestedPlayers | method | — | — | — |
| GetLimitTime | method | — | Player.Main/AddQuest | — |
| GetPrevQuestId | method | — | ChatHandler.CharacterCommands/HandleQuestStatusCommand, ObjectMgr/LoadQuests | — |
| GetNextQuestId | method | — | ObjectMgr/LoadQuests | — |
| GetExclusiveGroup | method | — | Player.Main/SatisfyQuestExclusiveGroup, Player.Main/SatisfyQuestPreviousQuest | — |
| GetBreadcrumbForQuestId | method | — | ObjectMgr/LoadQuests, Player.Main/SatisfyQuestBreadcrumbQuest | — |
| GetNextQuestInChain | method | — | ChatHandler.CharacterCommands/HandleQuestStatusCommand, Player.Main/GetNextQuest, Player.Main/SatisfyQuestNextChain, WorldSession.QuestHandler/HandleQuestQueryOpcode | — |
| GetSrcItemId | method | — | AiBotAI.Bridge/BridgeHandleSellItems, Player.Main/AddQuest, Player.Main/CanGiveQuestSourceItemIfNeed, Player.Main/GiveQuestSourceItemIfNeed, Player.Main/ItemAddedQuestCheck, Player.Main/TakeOrReplaceQuestStartItems, WorldSession.QuestHandler/HandleQuestQueryOpcode | — |
| GetSrcItemCount | method | — | Player.Main/CanGiveQuestSourceItemIfNeed, Player.Main/TakeOrReplaceQuestStartItems | — |
| GetSrcSpell | method | — | WorldSession.QuestHandler/HandleQuestgiverAcceptQuestOpcode | — |
| GetTitle | method | — | AiBotAI.Bridge/BridgeHandleQuestInteract, ChatHandler.LookupCommands/HandleLookupQuestCommand, ChatHandler.LookupCommands/ShowQuestListHelper, GossipDef/SendGossipMenu, GossipDef/SendQuestGiverOfferReward, GossipDef/SendQuestGiverQuestDetails, GossipDef/SendQuestGiverQuestList, GossipDef/SendQuestGiverRequestItems, Player.Main/SendQuestConfirmAccept, WorldSession.QuestHandler/HandleQuestQueryOpcode | — |
| GetDetails | method | — | GossipDef/SendQuestGiverQuestDetails, WorldSession.QuestHandler/HandleQuestQueryOpcode | — |
| GetObjectives | method | — | GossipDef/SendQuestGiverQuestDetails, WorldSession.QuestHandler/HandleQuestQueryOpcode | — |
| GetOfferRewardText | method | — | GossipDef/SendQuestGiverOfferReward | — |
| GetRequestItemsText | method | — | GossipDef/SendQuestGiverRequestItems | — |
| GetEndText | method | — | WorldSession.QuestHandler/HandleQuestQueryOpcode | — |
| GetRewMoneyMaxLevel | method | — | WorldSession.QuestHandler/HandleQuestQueryOpcode | — |
| GetRewRepSpilloverMask | method | — | Player.Main/RewardReputation | — |
| GetRewXP | method | — | — | — |
| GetRewSpell | method | — | GossipDef/SendQuestGiverOfferReward, GossipDef/SendQuestGiverQuestDetails, Player.Main/RewardQuest, WorldSession.QuestHandler/HandleQuestQueryOpcode | — |
| GetRewSpellCast | method | — | Player.Main/LearnQuestRewardedSpells#2, Player.Main/RewardQuest | — |
| GetRewMailTemplateId | method | — | Player.Main/RewardQuest | — |
| GetRewMailDelaySecs | method | — | Player.Main/RewardQuest | — |
| GetRewMailMoney | method | — | Player.Main/RewardQuest | — |
| GetPointMapId | method | — | WorldSession.QuestHandler/HandleQuestQueryOpcode | — |
| GetPointX | method | — | WorldSession.QuestHandler/HandleQuestQueryOpcode | — |
| GetPointY | method | — | WorldSession.QuestHandler/HandleQuestQueryOpcode | — |
| GetPointOpt | method | — | WorldSession.QuestHandler/HandleQuestQueryOpcode | — |
| GetIncompleteEmote | method | — | GossipDef/SendQuestGiverRequestItems | — |
| GetCompleteEmote | method | — | GossipDef/SendQuestGiverRequestItems | — |
| GetQuestStartScript | method | — | Player.Main/AddQuest | — |
| GetQuestCompleteScript | method | — | Player.Main/RewardQuest | — |
| IsRepeatable | method | — | LootMgr/AllowedForPlayer, Player.Main/GetQuestRewardStatus, Player.Main/RewardQuest, Player.Main/SendPreparedQuest, Player.Main/_LoadQuestStatus, WorldSession.QuestHandler/GetDialogStatus, WorldSession.QuestHandler/HandleQuestgiverCompleteQuest | — |
| IsAutoComplete | method | — | Player.Main/CanCompleteQuest, Player.Main/CanRewardQuest, Player.Main/PrepareQuestMenu, WorldSession.QuestHandler/GetDialogStatus | — |
| SetQuestActiveState | method | — | GameEventMgr.Main/LoadFromDB, GameEventMgr.Main/UpdateEventQuests | — |
| IsActive | method | — | AiBotAI.Bridge/BridgeHandleQuestInteract, Player.Main/CanSeeStartQuest, Player.Main/CanTakeQuest, Player.Main/ChangeQuestsForRace, Player.Main/PrepareQuestMenu, WorldSession.QuestHandler/GetDialogStatus | — |
| GetReqItemsCount | method | — | AiBotAI.Bridge/BridgeHandleQuestInteract, GossipDef/SendQuestGiverRequestItems | — |
| GetReqCreatureOrGOcount | method | — | AiBotAI.Bridge/BridgeHandleQuestInteract, Player.Main/SendQuestUpdateAddItem | — |
| GetRewChoiceItemsCount | method | — | AiBotAI.Loot/ChooseQuestReward, GossipDef/SendQuestGiverOfferReward, GossipDef/SendQuestGiverQuestDetails, Player.Main/CanRewardQuest#2, Player.Main/RewardQuest | — |
| GetRewItemsCount | method | — | GossipDef/SendQuestGiverOfferReward, GossipDef/SendQuestGiverQuestDetails, Player.Main/CanRewardQuest#2, Player.Main/RewardQuest, Player.Main/SendQuestReward | — |
