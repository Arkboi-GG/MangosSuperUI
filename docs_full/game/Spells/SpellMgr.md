<!-- provenance: failed-members -->
# SpellMgr

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# SpellMgr

**Purpose & Responsibilities**

`SpellMgr` is the central singleton manager responsible for loading, storing, and providing query access to all spell-related data in the `wowvmangos` server. It acts as the bridge between the raw database tables (custom server configuration) and the compiled DBC files (client data), resolving conflicts, validating integrity, and exposing high-level queries about spell behavior, rankings, areas, and interactions.

Its primary responsibilities include:
1.  **Loading Spell Definitions:** Reading `spell_template` and `locales_spell` to populate `SpellEntry` objects, applying version-specific patches for older client builds (e.g., converting old proc flags, fixing amplitude timers).
2.  **Managing Spell Chains:** Determining rank hierarchies (previous/next/first spell in a chain) by combining data from `spell_chain`, talent definitions, and skill line abilities. This is critical for determining if a player knows a higher rank of a spell or for trainer logic.
3.  **Handling Spell Areas & Restrictions:** Loading `spell_area` to determine where spells can be cast, what quests must be active/inactive, and what auras are required. It also handles hard-coded restrictions for Battlegrounds.
4.  **Defining Spell Interactions:** Managing stacking rules via `spell_group` and `spell_group_stack_rules`, and defining which spells displace others (e.g., "A more powerful spell is already active").
5.  **Proc & Threat Logic:** Loading custom proc conditions (`spell_proc_event`), item enchant proc rates (`spell_proc_item_enchant`), and threat modifiers (`spell_threat`).
6.  **Validation:** Providing utilities to check if a spell is valid for learning, casting, or existence, often used by chat commands and AI systems.
7.  **Internal Flag Assignment:** Pre-computing boolean properties (e.g., is heal, is AOE, is CC) into bitmasks on `SpellEntry` to optimize runtime checks during spell casting and aura application.

**Member-by-Member Behavior**

### Initialization & Singleton Access
*   **`SpellMgr` (ctor)**: Default constructor. Initializes the singleton instance.
*   **`~SpellMgr` (dtor)**: Default destructor.
*   **`Instance`**: Returns the global singleton instance of `SpellMgr`. Used extensively throughout the codebase via the `sSpellMgr` macro.

### Data Loading Methods
These methods are called during server startup (`World/SetInitialWorldSettings`) or via reload commands (`ChatHandler.ServerCommands`). They query the database, validate data against existing DBC/DB entries, and populate internal maps.

*   **`LoadSpells`**: Loads the core spell definitions from `spell_template`. It iterates through all rows, calling `LoadSpell` for each. It also loads localized strings from `locales_spell` for build 1.12.1.
*   **`LoadSpell`**: Parses a single row of fields into a `SpellEntry`. It applies several legacy patches:
    *   Sets periodic aura amplitudes to 5000ms if 0 (Vanilla behavior).
    *   Converts `SPELL_ATTR_EX2_ENABLE_AFTER_PARRY` to caster aura states.
    *   Adjusts speed reduction values for pre-1.11 clients.
    *   Converts old proc flags for pre-1.10 clients.
*   **`AssignInternalSpellFlags`**: Iterates through all loaded `SpellEntry` objects and sets internal bitmask flags (e.g., `SPELL_INTERNAL_HEAL`, `SPELL_INTERNAL_AOE`) based on helper functions in the `SpellInternal` namespace. This optimizes runtime checks.
*   **`LoadSpellChains`**: Constructs the spell rank hierarchy. It first populates chains from Talent DBC data and Skill Line Ability DBC data (using `LoadSpellChains_AbilityHelper` to resolve forward references). Then, it overlays data from the `spell_chain` database table, validating that DB data matches DBC-derived data unless it provides additional `req_spell` information.
*   **`LoadSpellAreas`**: Loads `spell_area` records. It validates area IDs, quest templates, and aura spells. It populates multiple maps (`mSpellAreaMap`, `mSpellAreaForQuestMap`, etc.) to allow efficient lookups by spell, area, quest, or aura. It prevents circular autocast dependencies.
*   **`LoadSpellGroups`**: Loads `spell_group` definitions. It validates that referenced spells and groups exist. It builds bidirectional maps: spell-to-group and group-to-spell.
*   **`LoadSpellGroupStackRules`**: Loads `spell_group_stack_rules`. It validates that the group exists and the stack rule is valid.
*   **`LoadSpellProcEvents`**: Loads `spell_proc_event`. Uses `SpellRankHelper` to handle rank inheritance. Validates that custom ranks match the first rank's school/family/flags, allowing only PPM/chance/cooldown differences.
*   **`LoadSpellProcItemEnchant`**: Loads `spell_proc_item_enchant`. Validates that the spell exists and is the first rank in its chain. Propagates the PPM rate to all higher ranks in the chain.
*   **`LoadSpellThreats`**: Loads `spell_threat`. Uses `SpellRankHelper` for rank inheritance. Validates that custom ranks have threat data. Warns if a spell with threat mods has mixed target types.
*   **`LoadSpellElixirs`**: Loads `spell_elixir`. Maps spell IDs to elixir masks (flask/well-fed/etc.).
*   **`LoadSpellEnchantCharges`**: Loads `spell_enchant_charges`. Maps spell IDs to charge counts.
*   **`LoadSpellCones`**: Loads `spell_cone`. Maps spell IDs to cone angles in radians. Validates angles are within -360 to 360 degrees.
*   **`LoadSpellTargetPositions`**: Loads `spell_target_position`. Validates that the target map exists, coordinates are non-zero, the spell exists, and the spell has a `TARGET_LOCATION_DATABASE` implicit target.
*   **`LoadSpellScriptTarget`**: Loads `spell_script_target`. Validates that the spell has a script-based implicit target type. Checks that referenced creature/gameobject templates and condition IDs exist.
*   **`LoadSpellPetAuras`**: Loads `spell_pet_auras`. Validates that the spell has a dummy effect/aura. Creates `PetAura` objects mapping pet entries to aura spells.
*   **`LoadSpellLearnSpells`**: Loads `spell_learn_spell`. Also scans all DBC spells for `SPELL_EFFECT_LEARN_SPELL` to auto-populate the map for spells not explicitly in the DB. Marks auto-learned spells appropriately.
*   **`LoadSpellLearnSkills`**: Scans all DBC spells for `SPELL_EFFECT_SKILL` to populate `mSpellLearnSkills`. Calculates skill steps and values.
*   **`LoadSkillLineAbilityMaps`**: Iterates through all `SkillLineAbility` DBC entries and populates maps indexed by spell ID and skill ID.
*   **`LoadSkillRaceClassInfoMap`**: Iterates through `SkillRaceClassInfo` DBC entries and populates a map indexed by skill ID.
*   **`LoadExistingSpellIds`**: Loads distinct spell IDs from `spell_template` into a set for quick existence checks.

### Query & Helper Methods
These methods provide read-only access to the loaded data or perform logical checks.

*   **`GetSpellEntry`**: Retrieves a `SpellEntry` pointer by ID. Returns `nullptr` if the ID is out of bounds or the entry doesn't exist.
*   **`GetMaxSpellId`**: Returns the size of the spell entry map.
*   **`IsExistingSpellId`**: Checks if a spell ID exists in the database (even if not loaded into memory yet, though typically used post-load).
*   **`OverwriteSpellEntry`**: Creates a new, empty `SpellEntry` for a given ID, marking it as custom. Used by `SpellModMgr` to override spells.

#### Spell Chain & Rank Queries
*   **`GetSpellChainNode`**: Returns the `SpellChainNode` for a spell, containing prev, first, req, and rank.
*   **`GetFirstSpellInChain`**: Returns the ID of the first spell in the chain.
*   **`GetPrevSpellInChain`**: Returns the ID of the previous rank.
*   **`GetSpellRank`**: Returns the rank number (1-based).
*   **`IsHighRankOfSpell`**: Checks if `spell1` is a higher rank than `spell2` in the same chain.
*   **`GetSpellBookSuccessorSpellId`**: Finds the next spell in the skill line ability chain (forward spell).
*   **`doForHighRanks`**: Template method that iterates through all higher ranks of a spell, invoking a worker functor.
*   **`GetSpellChainNext`**: Returns a reference to the internal `SpellChainMapNext` multimap, which stores mappings from a spell ID to its successor spell IDs in a chain. This is used internally by `doForHighRanks` and other chain traversal logic.

#### Spell Group & Stacking Queries
*   **`GetSpellSpellGroupMapBounds`**: Returns iterators for all groups a spell belongs to.
*   **`GetSpellGroupSpellMapBounds`**: Returns iterators for all spells in a group.
*   **`GetSetOfSpellsInSpellGroup`**: Public wrapper for collecting spells in a group.
*   **`GetSetOfSpellsInSpellGroup#2`**: Recursively collects all spells in a group (handling nested groups).
*   **`CheckSpellGroupStackRules`**: Determines the stacking rule between two spells based on shared groups.
*   **`ListMorePowerfulSpells`**: Lists spells in a "powerful chain" group that are stronger than the given spell.
*   **`ListLessPowerfulSpells`**: Lists spells in a "powerful chain" group that are weaker than the given spell.
*   **`IsMorePowerfulSpell`**: Checks if one spell is more powerful than another within a specific group.
*   **`IsNoStackSpellDueToSpell`**: Complex logic to determine if two spells should not stack. It checks:
    1.  DB-defined group stack rules.
    2.  Hard-coded exceptions for specific spell IDs/icons/families (e.g., Thunderfury, Moonkin Aura, Paladin Seals).
    3.  Generic rules: same icon, same family, same visual, positive/negative status, and effect types.
*   **`IsRankSpellDueToSpell`**: Checks if two spells are ranks of the same spell, using family, icon, visual, and effect comparisons.
*   **`IsSpellMemberOfSpellGroup`**: Checks if a specific spell ID is a member of a given `SpellGroup`. It retrieves the bounds for the spell's groups and iterates to see if the target group ID is present.

#### Area & Location Queries
*   **`GetSpellAllowedInLocationError`**: Checks if a spell can be cast in a specific location given a caster unit. It handles:
    *   Battleground-only spells (hard-coded and attribute-based).
    *   Area-specific spells from `spell_area`, checking zone/area, quest status, aura presence, race, and gender.
*   **`GetSpellAllowedInLocationError#2`**: Checks if a spell can be cast in a specific location given explicit zone and area IDs. It delegates to the first overload if no area restrictions are found in the database.
*   **`GetRequiredAreaForSpell`**: Returns the area ID required for a spell, if any.
*   **`GetSpellAreaMapBounds`**: Returns iterators for all area restrictions for a spell.
*   **`GetSpellAreaForQuestMapBounds`**: Returns iterators for spells restricted by a quest start.
*   **`GetSpellAreaForQuestEndMapBounds`**: Returns iterators for spells restricted by a quest end.
*   **`GetSpellAreaForAuraMapBounds`**: Returns iterators for spells restricted by an aura.
*   **`GetSpellAreaForAreaMapBounds`**: Returns iterators for spells restricted by an area.
*   **`IsFitToRequirements`**: A method on the `SpellArea` struct (defined in `SpellMgr.h`) that checks if a player meets the gender, race, area, quest, and aura requirements for a specific spell area entry.

#### Proc, Threat, & Misc Queries
*   **`GetSpellProcEvent`**: Returns custom proc conditions for a spell.
*   **`IsSpellProcEventCanTriggeredBy`**: Static helper to check if a proc event matches a specific trigger context (school, family, flags, etc.).
*   **`GetSpellThreatEntry`**: Returns threat modification data for a spell.
*   **`GetSpellThreatMultiplier`**: Returns the threat multiplier for a spell, defaulting to 1.0.
*   **`GetItemEnchantProcChance`**: Returns the PPM rate for an item enchant spell.
*   **`GetSpellElixirMask`**: Returns the elixir mask for a spell.
*   **`GetSpellElixirSpecific`**: Determines if a spell is a flask, well-fed, or normal elixir.
*   **`GetSpellElixirMap`**: Returns a constant reference to the internal `SpellElixirMap`, allowing direct iteration over all registered elixir spells and their masks.
*   **`GetSpellCone`**: Returns the cone angle for a spell, defaulting to 60 degrees.
*   **`GetSpellEnchantCharges`**: Returns the charge count for an enchant spell.
*   **`GetSpellTargetPosition`**: Returns the target coordinates for a spell.
*   **`GetSpellScriptTargetBounds`**: Returns iterators for script targets associated with a spell.
*   **`GetPetAura`**: Returns pet aura data for a spell.
*   **`GetSpellLearnSkill`**: Returns skill learning data for a spell.
*   **`IsSpellLearnSpell`**: Checks if a spell teaches another spell.
*   **`GetSpellLearnSpellMapBounds`**: Returns iterators for spells taught by a given spell.
*   **`IsSpellLearnToSpell`**: Checks if spell A teaches spell B.
*   **`GetSkillLineAbilityMapBoundsBySpellId`**: Returns iterators for skill line abilities associated with a spell.
*   **`GetSkillLineAbilityMapBoundsBySkillId`**: Returns iterators for skill line abilities associated with a skill.
*   **`GetSkillRaceClassInfoMapBounds`**: Returns iterators for race/class restrictions for a skill.
*   **`GetSpellAffectMask`**: Returns the `EffectItemType` bitmask for a specific effect index of a spell. This is used to determine which item types a spell affects (e.g., for enchantments or specific item-targeting spells).

#### Validation & Utility
*   **`IsSpellValid`**: Checks if a spell is valid for learning/casting. Verifies that crafted items and reagents exist, and that learned spells are themselves valid.
*   **`IsProfessionOrRidingSpell`**: Checks if a spell grants a profession or riding skill. It verifies the spell has a `SPELL_EFFECT_SKILL` on its second effect and delegates to `IsProfessionOrRidingSkill`.
*   **`IsProfessionSpell`**: Checks if a spell grants a profession skill.
*   **`IsPrimaryProfessionSpell`**: Checks if a spell grants a primary profession skill.
*   **`IsPrimaryProfessionFirstRankSpell`**: Checks if a spell is the first rank of a primary profession.
*   **`IsSkillBonusSpell`**: Checks if a spell is a bonus skill gain (e.g., from a talent).
*   **`SelectAuraRankForLevel`**: Selects the appropriate rank of an aura spell for a player's level, walking down the chain until the spell level fits.
*   **`CheckUsedSpells`**: Debug utility to verify that spells referenced in other tables (passed via `table` parameter) exist and match specified criteria (family, icon, effect, etc.).

#### Internal Spell Property Helpers
These functions are primarily used by `AssignInternalSpellFlags` to pre-compute bitmasks on `SpellEntry` objects, optimizing runtime checks.

*   **`IsSpellAppliesAura`**: Checks if a spell applies any aura effect.
*   **`IsSpellAppliesPeriodicAura`**: Checks if a spell applies only periodic auras (damage/heal over time), excluding direct damage/heal effects.
*   **`IsPassiveSpellStackableWithRanks`**: Checks if a passive spell can stack with its ranks (typically false if it applies auras).
*   **`IsHealSpell`**: Checks if a spell heals (direct heal, periodic heal, or specific Paladin holy spells).
*   **`IsDirectDamageSpell`**: Checks if a spell deals direct damage.
*   **`IsSpellWithCasterSourceTargetsOnly`**: Checks if all effects target only the caster or sources related to the caster.
*   **`IsAreaOfEffectSpell`**: Checks if any effect targets an area.
*   **`HasAreaAuraEffect`**: Checks if any effect applies an area aura.
*   **`IsDismountSpell`**: Checks if a spell applies immunity to mount mechanics (effectively dismounting).
*   **`IsCharmSpell`**: Checks if a spell charms or possesses a unit.
*   **`IsReflectableSpell`**: Checks if a spell can be reflected (magic, non-passive, non-positive, no reflection immunity).
*   **`IsSpellWithDelayableEffects`**: Checks if a spell's effects can be delayed (e.g., for batching). Includes CC spells and specific exceptions like Execute.
*   **`IsBinary`**: Checks if a spell is "binary" (non-damage magic effects like interrupts, roots, silences) which affects resistance calculations.
*   **`IsNonPeriodicDispel`**: Checks if a spell is a dispel that does not apply periodic effects.
*   **`IsPvEHeartBeat`**: Checks if a spell is a PvE heartbeat proc (excluding certain crowd control auras).
*   **`IsCCSpell`**: Checks if a spell is a crowd control spell (based on diminishing returns groups).
*   **`GetAllowedTargetMask`**: Computes the bitmask of allowed target types for a spell based on its effects and implicit targets.

### Internal Helpers & Structures
*   **`SpellRankHelper`**: Template struct used by loaders to handle rank inheritance. It ensures that if a higher rank is defined, the first rank is also present, and propagates data from the first rank to higher ranks if not explicitly defined.
*   **`DoSpellProcEvent`**: Functor struct used with `SpellRankHelper` to validate and insert spell proc event data.
    *   **`DoSpellProcEvent` (ctor)**: Constructor for the functor.
    *   **`operator()`**: Processes a single spell proc event entry, validating custom ranks against the first rank.
    *   **`TableName`**: Returns the name of the table being processed ("spell_proc_event").
    *   **`IsValidCustomRank`**: Validates that a custom rank has PPM rate and matches the first rank's school/family/flags.
    *   **`AddEntry`**: Adds a proc event entry to the map, logging warnings for redundant or invalid data.
    *   **`HasEntry`**: Checks if a spell ID exists in the proc event map.
    *   **`SetStateToEntry`**: Sets the iterator state to a specific spell ID in the proc event map.
*   **`DoSpellProcItemEnchant`**: Functor struct used to propagate item enchant proc rates.
    *   **`DoSpellProcItemEnchant` (ctor)**: Constructor for the functor.
    *   **`operator()#2`**: Processes a single item enchant proc entry, propagating PPM to higher ranks.
*   **`DoSpellThreat`**: Functor struct used with `SpellRankHelper` to validate and insert spell threat data.
    *   **`DoSpellThreat` (ctor)**: Constructor for the functor.
    *   **`operator()#3`**: Processes a single spell threat entry, validating custom ranks.
    *   **`TableName#2`**: Returns the name of the table being processed ("spell_threat").
    *   **`IsValidCustomRank#2`**: Validates that a custom threat rank has threat data.
    *   **`AddEntry#2`**: Adds a threat entry to the map, warning about mixed target types.
    *   **`HasEntry#2`**: Checks if a spell ID exists in the threat map.
    *   **`SetStateToEntry#2`**: Sets the iterator state to a specific spell ID in the threat map.
*   **`LoadSpellChains_AbilityHelper`**: Recursive helper to resolve spell chains from skill line abilities.

**Cross-Unit Boundaries**

*   **Called By**: `SpellMgr` is heavily depended upon. Almost every system involving spells calls into it:
    *   **AI Systems** (`AiBotAI`, `BattleBotAI`, `CombatBotBaseAI`, `PetAI`): Use `GetSpellEntry`, `IsPrimaryProfessionFirstRankSpell`, `ListMorePowerfulSpells`, etc., to decide which spells to cast.
    *   **Player/Creature Logic** (`Player.Main`, `Creature.Main`, `Unit.Main`): Use `GetSpellEntry`, `GetSpellChainNode`, `IsNoStackSpellDueToSpell`, `GetSpellAllowedInLocationError` for learning, casting, and aura management.
    *   **Chat Handlers** (`ChatHandler.*`): Use `GetSpellEntry`, `IsSpellValid`, `CheckUsedSpells` for GM commands and debugging.
    *   **Object Manager** (`ObjectMgr`): Uses `IsExistingSpellId`, `GetSpellEntry` during initial loading and validation.
    *   **Spell System** (`Spell.Main`, `Spell.Effects`, `Unit.SpellAuras`): Uses `GetSpellEntry`, `GetSpellThreatEntry`, `GetSpellProcEvent`, `GetSpellTargetPosition` during spell execution and aura application.
*   **Calls Out**:
    *   **Database**: All `Load*` methods query the world database.
    *   **DBCStores**: `LoadSpellChains` uses `GetTalentSpellPos` and `GetTalentSpellCost`. `LoadSpells` uses `sTalentStore`.
    *   **ObjectMgr**: `LoadSpellScriptTarget` and `LoadSpellAreas` call `GetCreatureTemplate`, `GetGameObjectTemplate`, `GetQuestTemplate`, `IsExistingCreatureId`, `IsExistingGameObjectId` to validate references.
    *   **ScriptMgr**: `LoadSpell` calls `GetScriptId` to associate scripts with spells.
    *   **Log/Main**: All loaders log progress and errors.
    *   **ProgressBar**: Used in loaders to display progress.
    *   **World**: `AssignInternalSpellFlags` reads config settings.

**Data Model**

`SpellMgr` interacts with numerous database tables to customize and extend spell behavior beyond the DBC files.

*   **`spell_template`**: Core spell definitions. Columns include `entry`, `build`, `school`, `attributes`, `effect*`, `name`, etc.
*   **`locales_spell`**: Localized names and descriptions for spells.
*   **`spell_chain`**: Defines spell rank hierarchies (`spell_id`, `prev_spell`, `first_spell`, `rank`, `req_spell`).
*   **`spell_area`**: Defines area restrictions for spells (`spell`, `area`, `quest_start`, `quest_end`, `aura_spell`, `racemask`, `gender`, `autocast`).
*   **`spell_group`**: Groups spells together (`group_id`, `spell_id`).
*   **`spell_group_stack_rules`**: Defines stacking behavior for groups (`group_id`, `stack_rule`).
*   **`spell_proc_event`**: Custom proc conditions (`entry`, `SchoolMask`, `SpellFamilyName`, `procFlags`, `ppmRate`, etc.).
*   **`spell_proc_item_enchant`**: PPM rates for item enchants (`entry`, `ppmRate`).
*   **`spell_threat`**: Threat modifications (`entry`, `Threat`, `multiplier`, `ap_bonus`).
*   **`spell_elixir`**: Elixir classification (`entry`, `mask`).
*   **`spell_enchant_charges`**: Charge counts for enchants (`entry`, `charges`).
*   **`spell_cone`**: Cone angles (`entry`, `cone_degrees`).
*   **`spell_target_position`**: Teleport/target coordinates (`id`, `target_map`, `target_position_x/y/z`, `target_orientation`).
*   **`spell_script_target`**: Script-based targeting (`entry`, `type`, `targetEntry`, `conditionId`, `inverseEffectMask`).
*   **`spell_pet_auras`**: Pet-specific auras (`spell`, `pet`, `aura`).
*   **`spell_learn_spell`**: Spells that teach other spells (`entry`, `SpellID`, `Active`).
*   **`conditions`**: Referenced by `spell_script_target` for conditional targeting.

**Notable Implementation Details**

*   **Rank Inheritance**: Many loaders (`LoadSpellProcEvents`, `LoadSpellThreats`) use `SpellRankHelper` to ensure that if a higher rank of a spell is defined in the DB, the first rank is also present. Data from the first rank is propagated to higher ranks if not explicitly overridden, reducing DB redundancy.
*   **Legacy Support**: `LoadSpell` contains extensive conditional compilation (`#if SUPPORTED_CLIENT_BUILD <= ...`) to patch spell data for older client versions. This includes converting proc flags, adjusting speed reduction values, and fixing periodic aura timers.
*   **Stacking Logic**: `IsNoStackSpellDueToSpell` is highly complex, containing many hard-coded exceptions for specific spells (e.g., Thunderfury, Moonkin Aura, Paladin Seals). This suggests that the generic stacking rules were insufficient for Vanilla-era behavior, requiring manual overrides.
*   **Chain Resolution**: `LoadSpellChains` combines data from Talents, Skill Line Abilities, and the `spell_chain` table. It validates that DB data matches DBC-derived data, ensuring consistency. The `LoadSpellChains_AbilityHelper` recursively resolves forward references in skill line abilities.
*   **Area Validation**: `LoadSpellAreas` performs rigorous validation, including checking for circular autocast dependencies and ensuring that aura spells referenced in `spell_area` have dummy/ghost auras.
*   **Internal Flags**: `AssignInternalSpellFlags` pre-computes various spell properties (heal, damage, AOE, etc.) into bitmasks on `SpellEntry`, optimizing runtime checks in the spell casting system.
*   **Singleton Pattern**: `SpellMgr` uses the Meyer's Singleton pattern (`static SpellMgr spellMgr;` inside `Instance()`), ensuring thread-safe initialization in C++11 and later.

## Member Reference

**SpellMgr** (ctor): Default constructor for the singleton.
**Instance**: Returns the global `SpellMgr` singleton instance.
**LoadSpellTargetPositions**:

---

<!-- machine-true, projected from graph.json -->

## Map — SpellMgr

*Source:* SpellMgr.cpp, SpellMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| SpellMgr | ctor | — | — | — |
| Instance | method | — | AiBotAI.Bridge/BridgeHandleTrain, AiBotAI.Combat/DrinkAndEat, AiBotAI.Loot/ScoreItem, AuctionHouseMgr/BuildListAuctionItems, BattleBotAI.Main/DrinkAndEat, blackrock_depths/OnPeriodicTrigger, boss_patchwerk/DoHatefulStrike, CharacterDatabaseCleaner/SpellCheck, ChatHandler.CharacterCommands/HandleCharacterPremadeSaveSpecCommand, ChatHandler.CharacterCommands/HandleLearnAllCommand, ChatHandler.CharacterCommands/HandleLearnAllGMCommand, ChatHandler.CharacterCommands/HandleLearnAllItemsCommand, ChatHandler.CharacterCommands/HandleLearnAllMySpellsCommand, ChatHandler.CharacterCommands/HandleLearnAllMyTalentsCommand, ChatHandler.CharacterCommands/HandleLearnCommand, ChatHandler.CharacterCommands/HandleLearnSkillRecipesHelper, ChatHandler.CharacterCommands/HandleLearnTrainerHelper, ChatHandler.CharacterCommands/HandleListTalentsCommand, ChatHandler.CharacterCommands/HandleUnLearnCommand, ChatHandler.Chat/isValidChatMessage, ChatHandler.CreatureCommands/HandleNpcSpawnSetAurasCommand, ChatHandler.DebugCommands/HandleDebugSendNextChannelSpellVisualCommand, ChatHandler.DebugCommands/HandleDebugSpellCheckCommand, ChatHandler.DebugCommands/HandleDebugSpellCoefsCommand, ChatHandler.DebugCommands/HandleSendSpellChannelVisualCommand, ChatHandler.DebugCommands/HandleSendSpellVisualCommand, ChatHandler.DebugCommands/HandleSpellEffectsCommand, ChatHandler.DebugCommands/HandleSpellInfosCommand, ChatHandler.DebugCommands/HandleSpellSearchCommand, ChatHandler.LookupCommands/HandleLookupSpellCommand, ChatHandler.LookupCommands/ShowSpellListHelper, ChatHandler.ServerCommands/HandleGroupAddSpellCommand, ChatHandler.ServerCommands/HandleReloadSpellAreaCommand, ChatHandler.ServerCommands/HandleReloadSpellChainCommand, ChatHandler.ServerCommands/HandleReloadSpellElixirCommand, ChatHandler.ServerCommands/HandleReloadSpellGroupCommand, ChatHandler.ServerCommands/HandleReloadSpellGroupStackRulesCommand, ChatHandler.ServerCommands/HandleReloadSpellLearnSpellCommand, ChatHandler.ServerCommands/HandleReloadSpellPetAurasCommand, ChatHandler.ServerCommands/HandleReloadSpellProcEventCommand, ChatHandler.ServerCommands/HandleReloadSpellProcItemEnchantCommand, ChatHandler.ServerCommands/HandleReloadSpellScriptTargetCommand, ChatHandler.ServerCommands/HandleReloadSpellTargetPositionCommand, ChatHandler.ServerCommands/HandleReloadSpellTemplateCommand, ChatHandler.ServerCommands/HandleReloadSpellThreatsCommand, ChatHandler.TeleportCommands/HandleUnstuckCommand, ChatHandler.UnitCommands/HandleAuraHelper, ChatHandler.UnitCommands/HandleCastBackCommand, ChatHandler.UnitCommands/HandleCastCommand, ChatHandler.UnitCommands/HandleCastDistCommand, ChatHandler.UnitCommands/HandleCastSelfCommand, ChatHandler.UnitCommands/HandleCastTargetCommand, ChatHandler.UnitCommands/HandleCooldownClearCommand, ChatHandler.UnitCommands/HandleFearCommand, ChatHandler.UnitCommands/HandleUnitInfoCommand, ChatHandler.UnitCommands/HandleUnitShowCreateSpellCommand, CombatBotBaseAI/IsValidBuffTarget, CombatBotBaseAI/PopulateSpellData, CombatBotBaseAI/UseItemEffect, Conditions/IsValid, Creature.Main/ApplyGameEventSpells, Creature.Main/CancelSummonPossessedCharm, Creature.Main/LoadDefaultAuras, Creature.Main/SelectAttackingTarget#2, Creature.Main/StartCooldownForSummoner, Creature.Main/TryToCast#2, CreatureAI/DoSpellsListCasts, CreatureEventAIMgr/LoadCreatureEventAI_Events, custom_creatures/LearnSkillRecipesHelper, DBCStores/LoadDBCStores, DynamicObject/Create, GameEventMgr.Main/LoadFromDB, GameObject/AddUniqueUse, GameObject/FinishRitual, GameObject/GetSpellForLock, GameObject/TriggerLinkedGameObject, GameObject/Use, game_Battlegrounds_BattleGround/RewardSpellCast, game_Objects_Item/AddItemsSetItem, instance_naxxramas.Main/ChangeColor, Map.Main/FindScriptFinalTargets, Map.ScriptCommands/ScriptCommand_AddSpellCooldown, ObjectMgr/ApplyPremadeSpecTemplateToPlayer, ObjectMgr/CheckCreatureTemplate, ObjectMgr/CheckGOSpellId, ObjectMgr/FillObtainedItemsList, ObjectMgr/LoadAllIdentifiers, ObjectMgr/LoadCreatureSpells, ObjectMgr/LoadFactionChangeSpells, ObjectMgr/LoadItemPrototypes, ObjectMgr/LoadItemRequiredTarget, ObjectMgr/LoadPetCreateSpells, ObjectMgr/LoadPlayerInfo, ObjectMgr/LoadPlayerPremadeTemplates, ObjectMgr/LoadQuests, ObjectMgr/LoadSpellDisabledEntrys, ObjectMgr/LoadTrainers#2, PartyBotAI/CloneFromPlayer, PartyBotAI/DrinkAndEat, Pet.Main/AddSpell, Pet.Main/CanLearnPetSpell, Pet.Main/CanTakeMoreActiveSpells, Pet.Main/GetTPForSpell, Pet.Main/InitPetCreateSpells, Pet.Main/LoadPetFromDB, Pet.Main/RemoveSpell, Pet.Main/Unsummon, Pet.Main/_LoadAuras, Pet.Main/_LoadSpellCooldowns, PetAI/UpdateAI, Player.Main/AddQuest, Player.Main/AddSpell, Player.Main/ApplyEquipCooldown, Player.Main/ApplyItemEquipSpell, Player.Main/AutoReSummonPet, Player.Main/CastHighestStealthRank, Player.Main/CastItemCombatSpell, Player.Main/CastItemUseSpell, Player.Main/CheckAreaExploreAndOutdoor, Player.Main/EquipItem, Player.Main/GetSpellRank, Player.Main/GetTrainerSpellState, Player.Main/IsActionButtonDataValid, Player.Main/IsSpellFitByClassAndRace, Player.Main/LearnQuestRewardedSpells#2, Player.Main/LearnSpell, Player.Main/LearnSpellHighRank, Player.Main/LoadAura, Player.Main/LockOutSpells, Player.Main/RemoveSpell, Player.Main/RemoveSpellLockout, Player.Main/ResetTalents, Player.Main/RewardQuest, Player.Main/TeleportToHomebind, Player.Main/UpdateAreaDependentAuras, Player.Main/UpdateCraftSkill, Player.Main/UpdateSkillTrainedSpells, Player.Main/UpdateSpellTrainedSkills, Player.Main/UpdateZoneDependentAuras, Player.Main/_LoadSpellCooldowns, PlayerAI/PlayerControlledAI, PlayerAI/UpdateAI#2, ScriptMgr/CheckScriptTargets, ScriptMgr/CollectPossibleEventIds, ScriptMgr/LoadScripts, ScriptMgr/LoadSpellScripts, Spell.Effects/EffectDummy, Spell.Effects/EffectEnchantHeldItem, Spell.Effects/EffectEnchantItemTmp, Spell.Effects/EffectLearnPetSpell, Spell.Effects/EffectScriptEffect, Spell.Effects/EffectTeleportUnits, Spell.Effects/EffectTriggerMissileSpell, Spell.Effects/EffectTriggerSpell, Spell.Main/CheckCast, Spell.Main/CheckScriptTargeting, Spell.Main/CheckTarget, Spell.Main/DoAllEffectOnTarget#3, Spell.Main/HandleAddTargetTriggerAuras, Spell.Main/HandleThreatSpells, Spell.Main/SendCastResult#2, Spell.Main/SetTargetMap, Spell.Main/Spell, Spell.Main/Spell#2, Spell.Main/SpellNotifierCreatureAndPlayer, SpellCaster/CastCustomSpell#2, SpellCaster/CastSpell#2, SpellCaster/CastSpell#4, SpellCaster/DealSpellDamage, SpellCaster/IsSpellReady#2, SpellCaster/RemoveSpellCooldown#2, SpellEntry/CompareAuraRanks, SpellEntry/GetSpellSpecific, SpellEntry/HasAuraOrTriggersAnotherSpellWithAura, SpellEntry/IsAutocastable, SpellEntry/IsPassiveSpell, SpellEntry/IsPositiveEffect, SpellEntry/IsPositiveSpell, SpellEntry/IsPositiveSpell#2, SpellEntry/IsTargetInRange, SpellModifier/IsAffectedOnSpell, SpellModifier/SpellModifier#2, SpellModifier/SpellModifier#3, SpellModMgr/LoadSpellMods, spell_paladin/OnEffectExecute#4, spell_priest/OnSuccessfulFinish, spell_special/OnSuccessfulStart, Totem/SetTypeBySummonSpell, TotemAI/TotemAI, TotemAI/UpdateAI, Unit.AuraProcHandler/HandleProcTriggerSpellAuraProc, Unit.AuraProcHandler/IsTriggeredAtSpellProcEvent, Unit.AuraProcHandler/TriggerProccedSpell#2, Unit.Main/AddAura, Unit.Main/AddGameObject, Unit.Main/AddSpellToActionBar, Unit.Main/DealDamage, Unit.Main/HasMorePowerfulSpellActive, Unit.Main/InitCharmCreateSpells, Unit.Main/InitPossessCreateSpells, Unit.Main/IsImmuneToDamage, Unit.Main/IsImmuneToSchool, Unit.Main/IsImmuneToSpell, Unit.Main/IsImmuneToSpellEffect, Unit.Main/IsSecondaryThreatTarget, Unit.Main/LoadPetActionBar, Unit.Main/ModifyAuraState, Unit.Main/RemoveGameObject, Unit.Main/RemoveNoStackAurasDueToAuraHolder, Unit.Main/RemoveSpellFromActionBar, Unit.SpellAuras/Aura, Unit.SpellAuras/CalculateForDebuffLimit, Unit.SpellAuras/CanProcFrom, Unit.SpellAuras/CleanupTriggeredSpells, Unit.SpellAuras/HandleAuraDummy, Unit.SpellAuras/HandleShapeshiftBoosts, Unit.SpellAuras/IsAffectedOnSpell, Unit.SpellAuras/PeriodicTick, Unit.SpellAuras/SpellAuraHolder, Unit.SpellAuras/TriggerSpell, Unit.SpellAuras/Update, World/SetInitialWorldSettings, WorldSession.ItemHandler/HandleItemQuerySingleOpcode, WorldSession.NPCHandler/HandleTrainerBuySpellOpcode, WorldSession.NPCHandler/SendTrainerList, WorldSession.NPCHandler/SendTrainerSpellHelper, WorldSession.PetHandler/HandlePetAction, WorldSession.PetHandler/HandlePetCastSpellOpcode, WorldSession.SpellHandler/HandleCancelAuraOpcode, WorldSession.SpellHandler/HandleCastSpellOpcode, WorldSession.SpellHandler/HandlePetCancelAuraOpcode, WorldSession.SpellHandler/HandleSelfResOpcode, WorldSession.SpellHandler/HandleUseItemOpcode, WorldSession.TradeHandler/HandleAcceptTradeOpcode | — |
| LoadSpellTargetPositions | method | Database/PQuery, Field/GetFloat, Field/GetUInt32, Log.Main/Out, ProgressBar/BarGoLink, ProgressBar/BarGoLink#3, ProgressBar/step, QueryResult/Fetch, QueryResult/GetRowCount, QueryResult/NextRow, WorldLocation/WorldLocation#2 | ChatHandler.ServerCommands/HandleReloadSpellTargetPositionCommand, World/SetInitialWorldSettings | spell_target_position |
| SpellRankHelper<EntryType, WorkerType, StorageType> | ctor | — | — | — |
| RecordRank | function | Log.Main/Out | — | — |
| FillHigherRanks | function | Log.Main/Out | — | — |
| DoSpellProcEvent | ctor | — | — | — |
| operator() | method | Log.Main/Out | — | — |
| TableName | method | — | — | — |
| IsValidCustomRank | method | Log.Main/Out | — | — |
| AddEntry | method | Log.Main/Out | — | — |
| IsPrimaryProfessionSkill | function | — | Player.Main/GetTrainerSpellState | — |
| IsProfessionSkill | function | — | Player.Main/UpdateSpellTrainedSkills | — |
| IsProfessionOrRidingSkill | function | — | Player.Main/UpdateSkillsToMaxSkillsForLevel | — |
| ~SpellMgr | dtor | — | — | — |
| GetSpellSpellGroupMapBounds | method | — | — | — |
| IsSpellMemberOfSpellGroup | method | — | — | — |
| HasEntry | method | — | — | — |
| SetStateToEntry | method | — | — | — |
| GetSpellGroupSpellMapBounds | method | — | — | — |
| GetSetOfSpellsInSpellGroup#2 | method | — | — | — |
| LoadSpellProcEvents | method | Database/PQuery, Field/GetFloat, Field/GetUInt32, Field/GetUInt64, Log.Main/Out, ProgressBar/BarGoLink, ProgressBar/BarGoLink#3, ProgressBar/step, QueryResult/Fetch, QueryResult/GetRowCount, QueryResult/NextRow | ChatHandler.ServerCommands/HandleReloadSpellProcEventCommand, World/SetInitialWorldSettings | spell_proc_event |
| GetSetOfSpellsInSpellGroup | method | — | — | — |
| CheckSpellGroupStackRules | method | — | Unit.Main/RemoveNoStackAurasDueToAuraHolder | — |
| DoSpellProcItemEnchant | ctor | — | — | — |
| operator()#2 | method | — | — | — |
| IsMorePowerfulSpell | method | — | Unit.Main/RemoveNoStackAurasDueToAuraHolder | — |
| LoadSpellProcItemEnchant | method | Database/Query, Field/GetFloat, Field/GetUInt32, Log.Main/Out, ProgressBar/BarGoLink, ProgressBar/BarGoLink#3, ProgressBar/step, QueryResult/Fetch, QueryResult/GetRowCount, QueryResult/NextRow | ChatHandler.ServerCommands/HandleReloadSpellProcItemEnchantCommand, World/SetInitialWorldSettings | spell_proc_item_enchant |
| GetSpellAffectMask | method | — | SpellModifier/SpellModifier#2, SpellModifier/SpellModifier#3, Unit.SpellAuras/CanProcFrom, Unit.SpellAuras/IsAffectedOnSpell | — |
| GetSpellElixirMap | method | — | — | — |
| GetSpellElixirMask | method | — | — | — |
| GetSpellElixirSpecific | method | — | SpellEntry/GetSpellSpecific | — |
| GetSpellCone | method | — | Spell.Main/SpellNotifierCreatureAndPlayer | — |
| GetSpellEnchantCharges | method | — | Spell.Effects/EffectEnchantHeldItem, Spell.Effects/EffectEnchantItemTmp | — |
| GetSpellThreatEntry | method | — | Spell.Main/HandleThreatSpells | — |
| IsSpellProcEventCanTriggeredBy | method | SpellDefines/GetSchoolMask, SpellEntry/IsSpellAppliesPeriodicAura | Unit.AuraProcHandler/IsTriggeredAtSpellProcEvent | — |
| GetSpellThreatMultiplier | method | — | Spell.Main/DoAllEffectOnTarget#3, Unit.Main/DealDamage, Unit.SpellAuras/PeriodicTick | — |
| GetSpellProcEvent | method | — | Unit.AuraProcHandler/IsTriggeredAtSpellProcEvent, Unit.SpellAuras/CanProcFrom | — |
| GetItemEnchantProcChance | method | — | Player.Main/CastItemCombatSpell | — |
| GetSpellTargetPosition | method | — | Spell.Effects/EffectTeleportUnits, Spell.Main/SetTargetMap | — |
| GetSpellChainNode | method | — | Player.Main/GetTrainerSpellState, WorldSession.NPCHandler/SendTrainerSpellHelper | — |
| GetFirstSpellInChain | method | — | ChatHandler.CharacterCommands/HandleLearnAllCommand, ChatHandler.CharacterCommands/HandleLearnAllMySpellsCommand, ChatHandler.CharacterCommands/HandleUnLearnCommand, ObjectMgr/ApplyPremadeSpecTemplateToPlayer, PartyBotAI/CloneFromPlayer, Pet.Main/CanTakeMoreActiveSpells, Pet.Main/GetTPForSpell, Player.Main/LearnQuestRewardedSpells#2, Player.Main/UpdateSpellTrainedSkills, Unit.Main/AddSpellToActionBar, Unit.Main/RemoveNoStackAurasDueToAuraHolder, Unit.Main/RemoveSpellFromActionBar, Unit.SpellAuras/CalculateForDebuffLimit | — |
| LoadSpellGroups | method | Database/PQuery, Field/GetInt32, Field/GetUInt32, Log.Main/Out, ProgressBar/BarGoLink, ProgressBar/BarGoLink#3, ProgressBar/step, QueryResult/Fetch, QueryResult/GetRowCount, QueryResult/NextRow | ChatHandler.ServerCommands/HandleReloadSpellGroupCommand, World/SetInitialWorldSettings | spell_group |
| GetPrevSpellInChain | method | — | Pet.Main/RemoveSpell, Player.Main/AddSpell, Player.Main/RemoveSpell, Player.Main/UpdateSpellTrainedSkills | — |
| GetSpellChainNext | method | — | Player.Main/LearnSpell, Player.Main/RemoveSpell | — |
| GetSpellRank | method | — | ChatHandler.LookupCommands/ShowSpellListHelper, Pet.Main/AddSpell, Player.Main/LearnQuestRewardedSpells#2, Spell.Main/HandleThreatSpells | — |
| IsHighRankOfSpell | method | — | Pet.Main/AddSpell, Player.Main/LearnQuestRewardedSpells#2 | — |
| GetSpellBookSuccessorSpellId | method | — | Player.Main/AddSpell, Player.Main/RemoveSpell | — |
| GetSpellLearnSkill | method | — | Player.Main/UpdateSpellTrainedSkills | — |
| IsSpellLearnSpell | method | — | — | — |
| LoadSpellGroupStackRules | method | Database/PQuery, Field/GetUInt32, Log.Main/Out, ProgressBar/BarGoLink, ProgressBar/BarGoLink#3, ProgressBar/step, QueryResult/Fetch, QueryResult/GetRowCount, QueryResult/NextRow | ChatHandler.ServerCommands/HandleReloadSpellGroupStackRulesCommand, World/SetInitialWorldSettings | spell_group_stack_rules |
| GetSpellLearnSpellMapBounds | method | — | Player.Main/AddSpell, Player.Main/RemoveSpell | — |
| IsSpellLearnToSpell | method | — | — | — |
| GetSpellScriptTargetBounds | method | — | ObjectMgr/LoadItemRequiredTarget, Spell.Main/CheckScriptTargeting, Spell.Main/SetTargetMap | — |
| GetSkillLineAbilityMapBoundsBySpellId | method | — | ChatHandler.CharacterCommands/HandleLearnAllItemsCommand, Pet.Main/CanLearnPetSpell, Pet.Main/GetTPForSpell, Pet.Main/InitPetCreateSpells, Player.Main/GetSpellRank, Player.Main/IsSpellFitByClassAndRace, Player.Main/UpdateCraftSkill, Player.Main/UpdateSpellTrainedSkills | — |
| GetSkillLineAbilityMapBoundsBySkillId | method | — | Player.Main/UpdateSkillTrainedSpells | — |
| GetSkillRaceClassInfoMapBounds | method | — | Player.Main/IsSpellFitByClassAndRace | — |
| GetPetAura | method | — | Player.Main/RemoveSpell, Spell.Effects/EffectDummy, Unit.SpellAuras/HandleAuraDummy | — |
| ListMorePowerfulSpells | method | Errors/PrintStacktraceAndThrow | CombatBotBaseAI/IsValidBuffTarget, Unit.Main/HasMorePowerfulSpellActive | — |
| GetSpellAreaMapBounds | method | — | — | — |
| GetSpellAreaForQuestMapBounds | method | — | Player.Main/AddQuest, Player.Main/RewardQuest | — |
| GetSpellAreaForQuestEndMapBounds | method | — | Player.Main/RewardQuest | — |
| GetSpellAreaForAuraMapBounds | method | — | Unit.SpellAuras/HandleAuraDummy | — |
| GetSpellAreaForAreaMapBounds | method | — | Player.Main/UpdateAreaDependentAuras, Player.Main/UpdateZoneDependentAuras | — |
| ListLessPowerfulSpells | method | — | Unit.Main/RemoveNoStackAurasDueToAuraHolder | — |
| GetSpellEntry | method | — | AiBotAI.Bridge/BridgeHandleTrain, AiBotAI.Combat/DrinkAndEat, AiBotAI.Loot/ScoreItem, AuctionHouseMgr/BuildListAuctionItems, BattleBotAI.Main/DrinkAndEat, blackrock_depths/OnPeriodicTrigger, boss_patchwerk/DoHatefulStrike, CharacterDatabaseCleaner/SpellCheck, ChatHandler.CharacterCommands/HandleCharacterPremadeSaveSpecCommand, ChatHandler.CharacterCommands/HandleLearnAllCommand, ChatHandler.CharacterCommands/HandleLearnAllGMCommand, ChatHandler.CharacterCommands/HandleLearnAllItemsCommand, ChatHandler.CharacterCommands/HandleLearnAllMySpellsCommand, ChatHandler.CharacterCommands/HandleLearnAllMyTalentsCommand, ChatHandler.CharacterCommands/HandleLearnCommand, ChatHandler.CharacterCommands/HandleLearnSkillRecipesHelper, ChatHandler.CharacterCommands/HandleLearnTrainerHelper, ChatHandler.CharacterCommands/HandleListTalentsCommand, ChatHandler.Chat/isValidChatMessage, ChatHandler.CreatureCommands/HandleNpcSpawnSetAurasCommand, ChatHandler.DebugCommands/HandleDebugSendNextChannelSpellVisualCommand, ChatHandler.DebugCommands/HandleDebugSpellCoefsCommand, ChatHandler.DebugCommands/HandleSendSpellChannelVisualCommand, ChatHandler.DebugCommands/HandleSendSpellVisualCommand, ChatHandler.DebugCommands/HandleSpellEffectsCommand, ChatHandler.DebugCommands/HandleSpellInfosCommand, ChatHandler.DebugCommands/HandleSpellSearchCommand, ChatHandler.LookupCommands/HandleLookupSpellCommand, ChatHandler.ServerCommands/HandleGroupAddSpellCommand, ChatHandler.TeleportCommands/HandleUnstuckCommand, ChatHandler.UnitCommands/HandleAuraHelper, ChatHandler.UnitCommands/HandleCastBackCommand, ChatHandler.UnitCommands/HandleCastCommand, ChatHandler.UnitCommands/HandleCastDistCommand, ChatHandler.UnitCommands/HandleCastSelfCommand, ChatHandler.UnitCommands/HandleCastTargetCommand, ChatHandler.UnitCommands/HandleCooldownClearCommand, ChatHandler.UnitCommands/HandleFearCommand, ChatHandler.UnitCommands/HandleUnitInfoCommand, ChatHandler.UnitCommands/HandleUnitShowCreateSpellCommand, CombatBotBaseAI/PopulateSpellData, CombatBotBaseAI/UseItemEffect, Conditions/IsValid, Creature.Main/ApplyGameEventSpells, Creature.Main/CancelSummonPossessedCharm, Creature.Main/LoadDefaultAuras, Creature.Main/SelectAttackingTarget#2, Creature.Main/StartCooldownForSummoner, Creature.Main/TryToCast#2, CreatureAI/DoSpellsListCasts, CreatureEventAIMgr/LoadCreatureEventAI_Events, custom_creatures/LearnSkillRecipesHelper, DBCStores/LoadDBCStores, DynamicObject/Create, GameEventMgr.Main/LoadFromDB, GameObject/AddUniqueUse, GameObject/FinishRitual, GameObject/GetSpellForLock, GameObject/TriggerLinkedGameObject, GameObject/Use, game_Battlegrounds_BattleGround/RewardSpellCast, game_Objects_Item/AddItemsSetItem, instance_naxxramas.Main/ChangeColor, Map.Main/FindScriptFinalTargets, Map.ScriptCommands/ScriptCommand_AddSpellCooldown, ObjectMgr/CheckCreatureTemplate, ObjectMgr/CheckGOSpellId, ObjectMgr/FillObtainedItemsList, ObjectMgr/LoadCreatureSpells, ObjectMgr/LoadFactionChangeSpells, ObjectMgr/LoadItemPrototypes, ObjectMgr/LoadItemRequiredTarget, ObjectMgr/LoadPetCreateSpells, ObjectMgr/LoadPlayerInfo, ObjectMgr/LoadPlayerPremadeTemplates, ObjectMgr/LoadQuests, ObjectMgr/LoadSpellDisabledEntrys, ObjectMgr/LoadTrainers#2, PartyBotAI/CloneFromPlayer, PartyBotAI/DrinkAndEat, Pet.Main/AddSpell, Pet.Main/InitPetCreateSpells, Pet.Main/LoadPetFromDB, Pet.Main/Unsummon, Pet.Main/_LoadAuras, Pet.Main/_LoadSpellCooldowns, PetAI/UpdateAI, Player.Main/AddSpell, Player.Main/ApplyEquipCooldown, Player.Main/ApplyItemEquipSpell, Player.Main/AutoReSummonPet, Player.Main/CastHighestStealthRank, Player.Main/CastItemCombatSpell, Player.Main/CastItemUseSpell, Player.Main/CheckAreaExploreAndOutdoor, Player.Main/EquipItem, Player.Main/GetTrainerSpellState, Player.Main/IsActionButtonDataValid, Player.Main/LearnQuestRewardedSpells#2, Player.Main/LoadAura, Player.Main/LockOutSpells, Player.Main/RemoveSpellLockout, Player.Main/ResetTalents, Player.Main/RewardQuest, Player.Main/TeleportToHomebind, Player.Main/_LoadSpellCooldowns, PlayerAI/PlayerControlledAI, PlayerAI/UpdateAI#2, ScriptMgr/CheckScriptTargets, ScriptMgr/CollectPossibleEventIds, ScriptMgr/LoadScripts, ScriptMgr/LoadSpellScripts, Spell.Effects/EffectDummy, Spell.Effects/EffectLearnPetSpell, Spell.Effects/EffectScriptEffect, Spell.Effects/EffectTriggerMissileSpell, Spell.Effects/EffectTriggerSpell, Spell.Main/CheckCast, Spell.Main/DoAllEffectOnTarget#3, Spell.Main/HandleAddTargetTriggerAuras, Spell.Main/Spell, Spell.Main/Spell#2, SpellCaster/CastCustomSpell#2, SpellCaster/CastSpell#2, SpellCaster/CastSpell#4, SpellCaster/DealSpellDamage, SpellCaster/IsSpellReady#2, SpellCaster/RemoveSpellCooldown#2, SpellEntry/CompareAuraRanks, SpellEntry/GetSpellSpecific, SpellEntry/HasAuraOrTriggersAnotherSpellWithAura, SpellEntry/IsAutocastable, SpellEntry/IsPassiveSpell, SpellEntry/IsPositiveEffect, SpellEntry/IsPositiveSpell, SpellEntry/IsPositiveSpell#2, SpellEntry/IsTargetInRange, SpellModifier/IsAffectedOnSpell, SpellModMgr/LoadSpellMods, spell_paladin/OnEffectExecute#4, spell_priest/OnSuccessfulFinish, spell_special/OnSuccessfulStart, Totem/SetTypeBySummonSpell, TotemAI/TotemAI, TotemAI/UpdateAI, Unit.AuraProcHandler/HandleProcTriggerSpellAuraProc, Unit.AuraProcHandler/TriggerProccedSpell#2, Unit.Main/AddAura, Unit.Main/AddGameObject, Unit.Main/InitCharmCreateSpells, Unit.Main/InitPossessCreateSpells, Unit.Main/IsImmuneToDamage, Unit.Main/IsImmuneToSchool, Unit.Main/IsImmuneToSpell, Unit.Main/IsImmuneToSpellEffect, Unit.Main/IsSecondaryThreatTarget, Unit.Main/LoadPetActionBar, Unit.Main/ModifyAuraState, Unit.Main/RemoveGameObject, Unit.SpellAuras/Aura, Unit.SpellAuras/CleanupTriggeredSpells, Unit.SpellAuras/HandleShapeshiftBoosts, Unit.SpellAuras/SpellAuraHolder, Unit.SpellAuras/TriggerSpell, WorldSession.ItemHandler/HandleItemQuerySingleOpcode, WorldSession.NPCHandler/HandleTrainerBuySpellOpcode, WorldSession.NPCHandler/SendTrainerList, WorldSession.NPCHandler/SendTrainerSpellHelper, WorldSession.PetHandler/HandlePetAction, WorldSession.PetHandler/HandlePetCastSpellOpcode, WorldSession.SpellHandler/HandleCancelAuraOpcode, WorldSession.SpellHandler/HandleCastSpellOpcode, WorldSession.SpellHandler/HandlePetCancelAuraOpcode, WorldSession.SpellHandler/HandleSelfResOpcode, WorldSession.SpellHandler/HandleUseItemOpcode, WorldSession.TradeHandler/HandleAcceptTradeOpcode | — |
| GetMaxSpellId | method | — | ChatHandler.CharacterCommands/HandleLearnAllCommand, ChatHandler.DebugCommands/HandleDebugSendNextChannelSpellVisualCommand, ChatHandler.DebugCommands/HandleSendSpellChannelVisualCommand, ChatHandler.DebugCommands/HandleSpellSearchCommand, ChatHandler.LookupCommands/HandleLookupSpellCommand, ChatHandler.UnitCommands/HandleFearCommand, CombatBotBaseAI/PopulateSpellData, DBCStores/LoadDBCStores, ObjectMgr/FillObtainedItemsList, ObjectMgr/LoadPetCreateSpells, ObjectMgr/LoadQuests, ObjectMgr/LoadTrainers#2, ScriptMgr/CollectPossibleEventIds | — |
| IsExistingSpellId | method | — | Conditions/IsValid, GameEventMgr.Main/LoadFromDB, ObjectMgr/LoadCreatureSpells, ObjectMgr/LoadSpellDisabledEntrys, ScriptMgr/CheckScriptTargets, ScriptMgr/LoadScripts, ScriptMgr/LoadSpellScripts, SpellModMgr/LoadSpellMods | — |
| OverwriteSpellEntry | method | — | SpellModMgr/LoadSpellMods | — |
| LoadSpellElixirs | method | Database/PQuery, Field/GetUInt32, Field/GetUInt8, Log.Main/Out, ProgressBar/BarGoLink, ProgressBar/BarGoLink#3, ProgressBar/step, QueryResult/Fetch, QueryResult/GetRowCount, QueryResult/NextRow | ChatHandler.ServerCommands/HandleReloadSpellElixirCommand, World/SetInitialWorldSettings | spell_elixir |
| DoSpellThreat | ctor | — | — | — |
| operator()#3 | method | Log.Main/Out | — | — |
| TableName#2 | method | — | — | — |
| IsValidCustomRank#2 | method | Log.Main/Out | — | — |
| AddEntry#2 | method | Log.Main/Out | — | — |
| HasEntry#2 | method | — | — | — |
| SetStateToEntry#2 | method | — | — | — |
| LoadSpellThreats | method | Database/PQuery, Field/GetFloat, Field/GetUInt16, Field/GetUInt32, Log.Main/Out, ProgressBar/BarGoLink, ProgressBar/BarGoLink#3, ProgressBar/step, QueryResult/Fetch, QueryResult/GetRowCount, QueryResult/NextRow | ChatHandler.ServerCommands/HandleReloadSpellThreatsCommand, World/SetInitialWorldSettings | spell_threat |
| IsRankSpellDueToSpell | method | — | CombatBotBaseAI/IsValidBuffTarget, Pet.Main/AddSpell, Unit.Main/RemoveNoStackAurasDueToAuraHolder | — |
| IsNoStackSpellDueToSpell | method | Log.Main/Out, SpellEntry/HasAttribute#3, SpellEntry/IsFitToFamilyMask, SpellEntry/IsPositiveSpell#4, SpellEntry/IsSealSpell | Unit.Main/RemoveNoStackAurasDueToAuraHolder | — |
| IsProfessionOrRidingSpell | method | — | — | — |
| IsProfessionSpell | method | — | ObjectMgr/LoadTrainers#2 | — |
| IsPrimaryProfessionSpell | method | — | — | — |
| IsPrimaryProfessionFirstRankSpell | method | — | AiBotAI.Bridge/BridgeHandleTrain, ChatHandler.CharacterCommands/HandleLearnTrainerHelper, Player.Main/AddSpell, Player.Main/GetTrainerSpellState, Player.Main/RemoveSpell, WorldSession.NPCHandler/SendTrainerSpellHelper | — |
| IsSkillBonusSpell | method | — | — | — |
| SelectAuraRankForLevel | method | SpellEntry/IsAreaEffectPossitiveTarget, SpellEntry/IsExplicitPositiveTarget, SpellEntry/IsPassiveSpell#2, SpellEntry/IsPositiveEffect | Spell.Main/CheckCast, Spell.Main/CheckTarget, Unit.SpellAuras/Update, WorldSession.SpellHandler/HandleCastSpellOpcode | — |
| LoadSpellChains_AbilityHelper | function | Errors/PrintStacktraceAndThrow | — | — |
| LoadSpellChains | method | Database/PQuery, DBCStores/GetTalentSpellPos, Errors/PrintStacktraceAndThrow, Field/GetUInt32, Field/GetUInt8, Log.Main/Out, ProgressBar/BarGoLink, ProgressBar/BarGoLink#3, ProgressBar/step, QueryResult/Fetch, QueryResult/GetRowCount, QueryResult/NextRow | ChatHandler.ServerCommands/HandleReloadSpellChainCommand, World/SetInitialWorldSettings | spell_chain |
| LoadSpellLearnSkills | method | Log.Main/Out, ProgressBar/BarGoLink#2, ProgressBar/step, SpellEntry/CalculateSimpleValue | World/SetInitialWorldSettings | — |
| LoadSpellEnchantCharges | method | Database/Query, Field/GetUInt32, Log.Main/Out, ProgressBar/BarGoLink, ProgressBar/BarGoLink#3, ProgressBar/step, QueryResult/Fetch, QueryResult/GetRowCount, QueryResult/NextRow | World/SetInitialWorldSettings | spell_enchant_charges |
| LoadSpellLearnSpells | method | Database/PQuery, DBCStores/GetTalentSpellCost#2, Field/GetBool, Field/GetUInt32, Log.Main/Out, ProgressBar/BarGoLink, ProgressBar/BarGoLink#3, ProgressBar/step, QueryResult/Fetch, QueryResult/GetRowCount, QueryResult/NextRow, SpellEntry/HasEffect, SpellEntry/IsPassiveSpell#2 | ChatHandler.ServerCommands/HandleReloadSpellLearnSpellCommand, World/SetInitialWorldSettings | spell_learn_spell |
| LoadSpellScriptTarget | method | Database/PQuery, Database/Query, Field/GetUInt32, Log.Main/Out, ObjectMgr/GetCreatureTemplate, ObjectMgr/GetGameObjectTemplate, ObjectMgr/IsExistingCreatureId, ObjectMgr/IsExistingGameObjectId, ProgressBar/BarGoLink, ProgressBar/BarGoLink#3, ProgressBar/step, QueryResult/Fetch, QueryResult/GetRowCount, QueryResult/NextRow, SpellTargetEntry/SpellTargetEntry | ChatHandler.ServerCommands/HandleReloadSpellScriptTargetCommand, World/SetInitialWorldSettings | conditions, spell_script_target |
| LoadSpellPetAuras | method | Database/Query, Field/GetUInt32, Log.Main/Out, PetAura/AddAura, PetAura/PetAura#2, ProgressBar/BarGoLink, ProgressBar/BarGoLink#3, ProgressBar/step, QueryResult/Fetch, QueryResult/GetRowCount, QueryResult/NextRow, SpellEntry/CalculateSimpleValue | ChatHandler.ServerCommands/HandleReloadSpellPetAurasCommand, World/SetInitialWorldSettings | spell_pet_auras |
| IsSpellValid | method | Log.Main/Out, ObjectMgr/GetItemPrototype, Player.Main/PSendSysMessage | ChatHandler.CharacterCommands/HandleLearnAllCommand, ChatHandler.CharacterCommands/HandleLearnAllGMCommand, ChatHandler.CharacterCommands/HandleLearnAllMySpellsCommand, ChatHandler.CharacterCommands/HandleLearnAllMyTalentsCommand, ChatHandler.CharacterCommands/HandleLearnCommand, ChatHandler.CharacterCommands/HandleLearnSkillRecipesHelper, ChatHandler.UnitCommands/HandleCastCommand, ChatHandler.UnitCommands/HandleCastDistCommand, ChatHandler.UnitCommands/HandleCastSelfCommand, custom_creatures/LearnSkillRecipesHelper, ObjectMgr/LoadQuests, ObjectMgr/LoadTrainers#2, Player.Main/AddSpell | — |
| LoadSpellCones | method | Database/Query, Field/GetInt16, Field/GetUInt32, Log.Main/Out, ProgressBar/BarGoLink, ProgressBar/BarGoLink#3, ProgressBar/step, QueryResult/Fetch, QueryResult/GetRowCount, QueryResult/NextRow | World/SetInitialWorldSettings | spell_cone |
| LoadSpellAreas | method | AreaEntry/GetById, Database/Query, Field/GetBool, Field/GetInt32, Field/GetUInt32, Field/GetUInt8, Log.Main/Out, ObjectMgr/GetQuestTemplate, ProgressBar/BarGoLink, ProgressBar/BarGoLink#3, ProgressBar/step, QueryResult/Fetch, QueryResult/GetRowCount, QueryResult/NextRow | ChatHandler.ServerCommands/HandleReloadSpellAreaCommand, World/SetInitialWorldSettings | spell_area |
| GetSpellAllowedInLocationError | method | BattleGround/GetStatus, MapEntry/IsBattleGround, Player.Main/GetBattleGround, Player.Main/InBattleGround, SpellEntry/HasAttribute#5, WorldObject.Object/GetMapId, WorldObject.Object/GetZoneAndAreaId | Spell.Main/CheckCast | — |
| GetRequiredAreaForSpell | method | — | Spell.Main/CheckCast, Spell.Main/SendCastResult#2 | — |
| GetSpellAllowedInLocationError#2 | method | — | Player.Main/UpdateAreaDependentAuras | — |
| LoadSkillLineAbilityMaps | method | Log.Main/Out, ObjectMgr/GetMaxSkillLineAbilityId, ObjectMgr/GetSkillLineAbility, ProgressBar/BarGoLink#2, ProgressBar/step | World/SetInitialWorldSettings | — |
| LoadSkillRaceClassInfoMap | method | Log.Main/Out, ProgressBar/BarGoLink#2, ProgressBar/step | World/SetInitialWorldSettings | — |
| CheckUsedSpells | method | Database/PQuery, Field/GetCppString, Field/GetInt32, Field/GetUInt32, Field/GetUInt64, Log.Main/Out, ProgressBar/BarGoLink, ProgressBar/BarGoLink#3, ProgressBar/step, QueryResult/Fetch, QueryResult/GetRowCount, QueryResult/NextRow, SpellEntry/HasAura, SpellEntry/HasEffect, SpellEntry/IsFitToFamilyMask | ChatHandler.DebugCommands/HandleDebugSpellCheckCommand | — |
| IsFitToRequirements | method | Player.Main/GetQuestRewardStatus, Player.Main/IsActiveQuest, Unit.Main/GetGender, Unit.Main/GetRaceMask, Unit.Main/HasAura#2 | Player.Main/AddQuest, Player.Main/RewardQuest, Player.Main/UpdateAreaDependentAuras, Player.Main/UpdateZoneDependentAuras, Unit.SpellAuras/HandleAuraDummy | — |
| LoadExistingSpellIds | method | Database/Query, Field/GetUInt32, QueryResult/Fetch, QueryResult/NextRow | ObjectMgr/LoadAllIdentifiers | spell_template |
| IsSpellAppliesAura | function | SpellEntry/IsEffectAppliesAura | — | — |
| IsSpellAppliesPeriodicAura | function | — | — | — |
| IsPassiveSpellStackableWithRanks | function | SpellEntry/IsPassiveSpell#2 | — | — |
| IsHealSpell | function | — | — | — |
| IsDirectDamageSpell | function | SpellEntry/IsDirectDamageEffect | — | — |
| IsSpellWithCasterSourceTargetsOnly | function | SpellEntry/IsCasterSourceTarget | — | — |
| IsAreaOfEffectSpell | function | SpellEntry/IsAreaEffectTarget | — | — |
| HasAreaAuraEffect | function | SpellEntry/IsAreaAuraEffect | — | — |
| IsDismountSpell | function | — | — | — |
| IsCharmSpell | function | SpellEntry/HasAura | — | — |
| IsReflectableSpell | function | SpellEntry/HasAttribute, SpellEntry/HasAttribute#3, SpellEntry/IsPositiveSpell#4 | — | — |
| IsSpellWithDelayableEffects | function | SpellEntry/IsCCSpell, SpellEntry/IsChanneledSpell, SpellEntry/IsDelayableEffect, SpellEntry/IsNextMeleeSwingSpell, SpellEntry/IsRangedSpell | — | — |
| IsBinary | function | — | — | — |
| IsNonPeriodicDispel | function | SpellEntry/HasEffect | — | — |
| IsPvEHeartBeat | function | SpellEntry/HasAttribute | — | — |
| IsCCSpell | function | SpellEntry/GetDiminishingReturnsGroup, SpellEntry/HasEffect, SpellEntry/IsChanneledSpell | — | — |
| GetAllowedTargetMask | function | SpellEntry/GetAllowedTargetMaskForTargetType | — | — |
| AssignInternalSpellFlags | method | SpellEntry/IsPositiveSpell#3, World/getConfig#4 | World/SetInitialWorldSettings | — |
| LoadSpells | method | Database/PQuery, Database/Query, Field/GetCppString, Field/GetUInt32, Log.Main/Out, ProgressBar/BarGoLink, ProgressBar/BarGoLink#3, ProgressBar/step, QueryResult/Fetch, QueryResult/GetRowCount, QueryResult/NextRow, shared_Util/getMSTime, WorldTimer/getMSTimeDiffToNow | ChatHandler.ServerCommands/HandleReloadSpellTemplateCommand, World/SetInitialWorldSettings | locales_spell, spell_template |
| LoadSpell | method | Field/GetCppString, Field/GetFloat, Field/GetInt32, Field/GetString, Field/GetUInt32, Field/GetUInt64, ScriptMgr/GetScriptId#2, SpellEntry/HasAttribute#4 | — | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `conditions`: condition_entry mediumint(8) unsigned PK, type tinyint(3), value1 int(11), value2 int(11), value3 int(11), value4 int(11), flags tinyint(3) unsigned
- `locales_spell`: entry smallint(5) unsigned PK, name_loc1 varchar(256), name_loc2 varchar(256), name_loc3 varchar(256), name_loc4 varchar(256), name_loc5 varchar(256), name_loc6 varchar(256), nameSubtext_loc1 varchar(256), nameSubtext_loc2 varchar(256), nameSubtext_loc3 varchar(256), nameSubtext_loc4 varchar(256), nameSubtext_loc5 varchar(256), nameSubtext_loc6 varchar(256), description_loc1 varchar(1024), description_loc2 varchar(1024), description_loc3 varchar(1024), description_loc4 varchar(1024), description_loc5 varchar(1024), description_loc6 varchar(1024), auraDescription_loc1 varchar(512), auraDescription_loc2 varchar(512), auraDescription_loc3 varchar(512), auraDescription_loc4 varchar(512), auraDescription_loc5 varchar(512), auraDescription_loc6 varchar(512)
- `spell_area`: spell smallint(5) unsigned PK, area mediumint(8) unsigned PK, quest_start mediumint(8) unsigned PK, quest_start_active tinyint(1) unsigned PK, quest_end mediumint(8) unsigned, aura_spell smallint(6) PK, racemask mediumint(8) unsigned PK, gender tinyint(1) unsigned PK, autocast tinyint(1) unsigned
- `spell_chain`: spell_id smallint(5) unsigned PK, prev_spell smallint(5) unsigned, first_spell smallint(5) unsigned, rank tinyint(4), req_spell smallint(5) unsigned, build_min smallint(4) PK, build_max smallint(4) PK
- `spell_cone`: entry smallint(5) unsigned PK, cone_degrees smallint(6)
- `spell_elixir`: entry smallint(5) unsigned PK, mask tinyint(1) unsigned, build_min smallint(4) unsigned, build_max smallint(4) unsigned
- `spell_enchant_charges`: entry smallint(5) unsigned PK, charges int(10) unsigned
- `spell_group`: group_id int(11) unsigned PK, group_spell_id int(11) unsigned PK, spell_id smallint(5) unsigned PK, build_min smallint(4) unsigned, build_max smallint(4) unsigned
- `spell_group_stack_rules`: group_id int(11) unsigned PK, build smallint(4) unsigned PK, stack_rule tinyint(3)
- `spell_learn_spell`: entry smallint(5) unsigned PK, SpellID smallint(5) unsigned PK, Active tinyint(3) unsigned, build_min smallint(4) unsigned, build_max smallint(4) unsigned
- `spell_pet_auras`: spell smallint(5) unsigned PK, pet mediumint(8) unsigned PK, aura mediumint(8) unsigned
- `spell_proc_event`: entry smallint(5) unsigned PK, SchoolMask tinyint(4) unsigned, SpellFamilyName smallint(5) unsigned, SpellFamilyMask0 bigint(40) unsigned, SpellFamilyMask1 bigint(40) unsigned, SpellFamilyMask2 bigint(40) unsigned, procFlags int(10) unsigned, procEx int(10) unsigned, ppmRate float, CustomChance float, Cooldown int(10) unsigned, build_min smallint(4) unsigned PK, build_max smallint(4) unsigned PK
- `spell_proc_item_enchant`: entry smallint(5) unsigned PK, ppmRate float
- `spell_script_target`: entry smallint(5) unsigned PK, type tinyint(3) unsigned PK, targetEntry mediumint(8) unsigned PK, conditionId mediumint(8) unsigned, inverseEffectMask mediumint(8) unsigned, build_min smallint(4) unsigned, build_max smallint(4) unsigned
- `spell_target_position`: id smallint(5) unsigned PK, target_map smallint(5) unsigned PK, target_position_x float, target_position_y float, target_position_z float, target_orientation float, build_min smallint(4) unsigned, build_max smallint(4) unsigned
- `spell_template`: entry mediumint(8) unsigned PK, build smallint(4) unsigned PK, school int(4) unsigned, category int(4) unsigned, castUI int(4) unsigned, dispel int(4) unsigned, mechanic int(4) unsigned, attributes int(4) unsigned, attributesEx int(4) unsigned, attributesEx2 int(4) unsigned, attributesEx3 int(4) unsigned, attributesEx4 int(4) unsigned, stances int(4) unsigned, stancesNot int(4) unsigned, targets int(4) unsigned, targetCreatureType int(4) unsigned, requiresSpellFocus int(4) unsigned, casterAuraState int(4) unsigned, targetAuraState int(4) unsigned, castingTimeIndex int(4) unsigned, recoveryTime int(4) unsigned, categoryRecoveryTime int(4) unsigned, interruptFlags int(4) unsigned, auraInterruptFlags int(4) unsigned, channelInterruptFlags int(4) unsigned, procFlags int(4) unsigned, procChance int(4) unsigned, procCharges int(4) unsigned, maxLevel int(4) unsigned, baseLevel int(4) unsigned, spellLevel int(4) unsigned, durationIndex int(4) unsigned, powerType int(4) unsigned, manaCost int(4) unsigned, manCostPerLevel int(4) unsigned, manaPerSecond int(4) unsigned, manaPerSecondPerLevel int(4) unsigned, rangeIndex int(4) unsigned, speed float, modelNextSpell int(4) unsigned, stackAmount int(4) unsigned, totem1 int(4) unsigned, totem2 int(4) unsigned, reagent1 int(4), reagent2 int(4), reagent3 int(4), reagent4 int(4), reagent5 int(4), reagent6 int(4), reagent7 int(4), reagent8 int(4), reagentCount1 int(4) unsigned, reagentCount2 int(4) unsigned, reagentCount3 int(4) unsigned, reagentCount4 int(4) unsigned, reagentCount5 int(4) unsigned, reagentCount6 int(4) unsigned, reagentCount7 int(4) unsigned, reagentCount8 int(4) unsigned, equippedItemClass int(4), equippedItemSubClassMask int(4), equippedItemInventoryTypeMask int(4), effect1 int(4) unsigned, effect2 int(4) unsigned, effect3 int(4) unsigned, effectDieSides1 int(4), effectDieSides2 int(4), effectDieSides3 int(4), effectBaseDice1 int(4) unsigned, effectBaseDice2 int(4) unsigned, effectBaseDice3 int(4) unsigned, effectDicePerLevel1 float, effectDicePerLevel2 float, effectDicePerLevel3 float, effectRealPointsPerLevel1 float, effectRealPointsPerLevel2 float, effectRealPointsPerLevel3 float, effectBasePoints1 int(4), effectBasePoints2 int(4), effectBasePoints3 int(4), effectBonusCoefficient1 float, effectBonusCoefficient2 float, effectBonusCoefficient3 float, effectMechanic1 int(4) unsigned, effectMechanic2 int(4) unsigned, effectMechanic3 int(4) unsigned, effectImplicitTargetA1 int(4) unsigned, effectImplicitTargetA2 int(4) unsigned, effectImplicitTargetA3 int(4) unsigned, effectImplicitTargetB1 int(4) unsigned, effectImplicitTargetB2 int(4) unsigned, effectImplicitTargetB3 int(4) unsigned, effectRadiusIndex1 int(4) unsigned, effectRadiusIndex2 int(4) unsigned, effectRadiusIndex3 int(4) unsigned, effectApplyAuraName1 int(4) unsigned, effectApplyAuraName2 int(4) unsigned, effectApplyAuraName3 int(4) unsigned, effectAmplitude1 int(4) unsigned, effectAmplitude2 int(4) unsigned, effectAmplitude3 int(4) unsigned, effectMultipleValue1 float, effectMultipleValue2 float, effectMultipleValue3 float, effectChainTarget1 int(4) unsigned, effectChainTarget2 int(4) unsigned, effectChainTarget3 int(4) unsigned, effectItemType1 bigint(20) unsigned, effectItemType2 bigint(20) unsigned, effectItemType3 bigint(20) unsigned, effectMiscValue1 int(4), effectMiscValue2 int(4), effectMiscValue3 int(4), effectTriggerSpell1 int(4) unsigned, effectTriggerSpell2 int(4) unsigned, effectTriggerSpell3 int(4) unsigned, effectPointsPerComboPoint1 float, effectPointsPerComboPoint2 float, effectPointsPerComboPoint3 float, spellVisual1 int(4) unsigned, spellVisual2 int(4) unsigned, spellIconId int(4) unsigned, activeIconId int(4) unsigned, spellPriority int(4) unsigned, name varchar(256), nameFlags int(4) unsigned, nameSubtext varchar(256), nameSubtextFlags int(4) unsigned, description varchar(1024), descriptionFlags int(4) unsigned, auraDescription varchar(512), auraDescriptionFlags int(4) unsigned, manaCostPercentage int(4) unsigned, startRecoveryCategory int(4) unsigned, startRecoveryTime int(4) unsigned, minTargetLevel int(4) unsigned, maxTargetLevel int(4) unsigned, spellFamilyName int(4) unsigned, spellFamilyFlags bigint(20) unsigned, maxAffectedTargets int(4) unsigned, dmgClass int(4) unsigned, preventionType int(4) unsigned, stanceBarOrder int(4), dmgMultiplier1 float, dmgMultiplier2 float, dmgMultiplier3 float, minFactionId int(4) unsigned, minReputation int(4) unsigned, requiredAuraVision int(4) unsigned, customFlags int(10) unsigned, script_name varchar(64)
- `spell_threat`: entry smallint(5) unsigned PK, Threat float, multiplier float, ap_bonus float, build_min smallint(4) unsigned PK, build_max smallint(4) unsigned PK

*`?` = nullable, `PK` = primary key column.*


---

<!-- verify: failed-members | missing: FillHigherRanks, IsPrimaryProfessionSkill, IsProfessionSkill, RecordRank, SpellRankHelper<EntryType, WorkerType, StorageType> -->
