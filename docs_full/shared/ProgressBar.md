<!-- provenance: verbose -->
# ProgressBar

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`BarGoLink` is a console utility that renders a text-based progress bar to `stdout` during server startup. It provides visual feedback for long-running database loading operations performed by managers such as `ObjectMgr`, `SpellMgr`, and `GuildMgr`. The class tracks progress against a total expected count (`row_count`) passed at construction and updates the display in-place using carriage returns (`\r`) when `step()` is called. It supports platform-specific characters for the bar graphics and allows global output suppression via `SetOutputState`.

## Member-by-Member Behavior

### Construction and Initialization

The class offers four overloaded constructors, all delegating to the private `init()` method. They accept different integer types (`int`, `uint32`, `uint64`, and `size_t` on Apple) to accommodate various API return types.

- **`BarGoLink(int)`**: Directly passes the count to `init()`.
- **`BarGoLink(uint32)`**, **`BarGoLink(uint64)`**, **`BarGoLink(size_t)`**: Assert that the input fits within a signed 32-bit integer (`int32`) to prevent overflow in internal counters. If the assertion fails, `Errors/PrintStacktraceAndThrow` is triggered.

**`init(int row_count)`** resets internal state (`rec_no`, `rec_pos`) and sets the indicator length to 50 characters. If output is enabled (`m_showOutput` is true), it prints the initial empty bar frame to `stdout`. On Windows, it uses `=` (`\x3D`) for borders; on other platforms, it uses `[` and `]`. The output ends with a carriage return to position the cursor for subsequent updates.

### Progress Updates

**`step()`** is called by the caller for each processed item. It increments the internal counter `rec_no` and calculates the current fill level `n`. If `n` differs from the previously rendered position `rec_pos`, it redraws the bar:
1. Prints a carriage return (`\r`) to move the cursor to the line start.
2. Prints the opening border, followed by `n` fill characters (`*` or `=`), then spaces to fill the remaining width.
3. Prints the closing border and the current percentage.
4. Ends with a carriage return and flushes `stdout`.

This ensures the terminal output updates only when the visual representation changes, minimizing I/O overhead.

### Output Control and Cleanup

**`SetOutputState(bool on)`** is a static method that toggles the global `m_showOutput` flag. It is called by `realmd_Main/main` to disable progress bars, typically when running as a daemon or logging to a file where in-place updates would corrupt the log. The header warns against changing this state while an active bar exists, as it may leave incomplete visual artifacts.

**`~BarGoLink()`** handles cleanup. If output is enabled, it prints a newline (`\n`) and flushes `stdout`, moving the cursor to the next line to prevent subsequent log messages from overwriting the final progress line.

## Cross-Unit Boundaries

`BarGoLink` is a passive utility with no outgoing calls to business logic, except for the assertion failure path.

### Called By (Consumers)

Nearly every manager responsible for loading data from the database instantiates `BarGoLink` during its `Load...` methods. Key consumers include:
- **`ObjectMgr`**: Loads creatures, game objects, quests, items, spells, factions, etc.
- **`SpellMgr`**: Loads spell definitions, areas, chains, and modifications.
- **`GuildMgr`**: Loads guild data and petitions.
- **`AuctionHouseMgr`**: Loads auction house items.
- **`BattleGroundMgr`**: Loads battleground templates and masters.
- **`ScriptMgr`**: Loads custom scripts and waypoints.
- **`AccountMgr`**: Loads ban lists.
- **`CharacterDatabaseCleaner`**: Checks for unique constraints.
- **`realmd_Main/main`**: Calls `SetOutputState` to configure global output behavior.

The usage pattern is consistent: construct `BarGoLink` with the total row count, call `step()` inside the processing loop, and let the destructor finalize the output.

### Calls Out (Dependencies)

- **`Errors/PrintStacktraceAndThrow`**: Called by the `uint32`, `uint64`, and `size_t` constructors via `MANGOS_ASSERT` if the `row_count` exceeds `int32` limits.
- **Standard Library**: Uses `<cstdio>` for `printf`/`fflush` and `<limits>` for numeric bounds.

## Data Model

`BarGoLink` does not interact with any database tables. It operates solely on in-memory integers provided by its callers.

## Notable Implementation Details

1.  **Integer Overflow Protection**: Internal counters are `int`. Constructors for larger types assert that the input fits in `int32` to prevent silent overflow or incorrect percentage calculations.
2.  **Platform-Specific Graphics**: Windows uses `=` for borders and fill; Unix-like systems use `[`/`]` and `*`. This addresses historical differences in console character rendering.
3.  **In-Place Updates**: Relies on `\r` (carriage return) to overwrite the current line. This requires a terminal that supports carriage returns; redirecting to a plain text file without handling `\r` will result in overlapping, unreadable lines.
4.  **Static State**: `m_showOutput` is static. Disabling it affects all future instances globally. Changing it mid-sequence is discouraged due to potential formatting inconsistencies.
5.  **Apple-Specific Constructor**: The `size_t` overload is guarded by `#ifdef __APPLE__`, likely to resolve ambiguity or implicit conversion warnings specific to macOS compilers or APIs.

## Member Reference

**BarGoLink** (ctor, `int`)
Constructs a progress bar with a signed integer count. Delegates to `init()`.

**BarGoLink#2** (ctor, `uint32`)
Constructs a progress bar with an unsigned 32-bit count. Asserts the value fits in `int32` to prevent overflow in internal counters. Calls `Errors/PrintStacktraceAndThrow` via `MANGOS_ASSERT` if the assertion fails. Delegates to `init()`.

**BarGoLink#3** (ctor, `uint64`)
Constructs a progress bar with an unsigned 64-bit count. Asserts the value fits in `int32` to prevent overflow in internal counters. Calls `Errors/PrintStacktraceAndThrow` via `MANGOS_ASSERT` if the assertion fails. Delegates to `init()`.

**~BarGoLink** (dtor)
Destroys the progress bar. If output is enabled, prints a newline and flushes stdout to clean up the terminal display.

**init** (method)
Private helper that initializes internal counters (`rec_no`, `rec_pos`, `num_rec`, `indic_len`). If output is enabled, prints the initial empty progress bar frame to stdout and positions the cursor for updates.

**step** (method)
Increments the progress counter. Calculates the current percentage and redraws the progress bar on the current line if the visual fill level has changed. Uses carriage returns to overwrite the previous output. Flushes stdout after updates.

**SetOutputState** (method)
Static method to enable or disable progress bar output globally. Used by `realmd_Main/main` to control verbosity during startup.

---

<!-- machine-true, projected from graph.json -->

## Map — ProgressBar

*Source:* ProgressBar.cpp, ProgressBar.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| BarGoLink | ctor | — | AccountMgr/LoadAccountBanList, AccountMgr/LoadIPBanList, AuctionHouseMgr/LoadAuctionItems, AuctionHouseMgr/LoadAuctions, AuraRemovalMgr/LoadFromDB, AutoBroadCastMgr/Load, BattleGroundMgr/CreateInitialBattleGrounds, BattleGroundMgr/LoadBattleEventIndexes, BattleGroundMgr/LoadBattleMastersEntry, ChatHandler.AuctionHouseBotMgr/Load, CreatureEventAIMgr/LoadCreatureEventAI_Events, CreatureGroups/Load, CreatureLinkingMgr/LoadFromDB, GameEventMgr.Main/LoadFromDB, GuildMgr/LoadGuilds, GuildMgr/LoadPetitions, InstanceStatistics/LoadFromDB, Log.Main/WaitBeforeContinueIfNeed, MapPersistentStateMgr/CleanupInstances, MapPersistentStateMgr/LoadCreatureRespawnTimes, MapPersistentStateMgr/LoadGameobjectRespawnTimes, ObjectMgr/LoadAreaLocales, ObjectMgr/LoadAreaTriggerLocales, ObjectMgr/LoadAreaTriggers, ObjectMgr/LoadAreaTriggerTeleports, ObjectMgr/LoadBattlegroundEntranceTriggers, ObjectMgr/LoadBroadcastTextLocales, ObjectMgr/LoadBroadcastTexts, ObjectMgr/LoadCinematicsWaypoints, ObjectMgr/LoadCorpses, ObjectMgr/LoadCreatureLocales, ObjectMgr/LoadCreatures, ObjectMgr/LoadCreatureSpells, ObjectMgr/LoadEquipmentTemplates, ObjectMgr/LoadExplorationBaseXP, ObjectMgr/LoadFactionChangeItems, ObjectMgr/LoadFactionChangeMounts, ObjectMgr/LoadFactionChangeQuests, ObjectMgr/LoadFactionChangeReputations, ObjectMgr/LoadFactionChangeSpells, ObjectMgr/LoadFactions, ObjectMgr/LoadFishingBaseSkillLevel, ObjectMgr/LoadGameObjectForQuests, ObjectMgr/LoadGameObjectLocales, ObjectMgr/LoadGameobjects, ObjectMgr/LoadGameobjectsRequirements, ObjectMgr/LoadGameTele, ObjectMgr/LoadGossipMenu, ObjectMgr/LoadGossipMenuItems, ObjectMgr/LoadGossipMenuItemsLocales, ObjectMgr/LoadGraveyardZones, ObjectMgr/LoadGroups, ObjectMgr/LoadItemLocales, ObjectMgr/LoadItemPrototypes, ObjectMgr/LoadItemRequiredTarget, ObjectMgr/LoadItemTexts, ObjectMgr/LoadMangosStrings#2, ObjectMgr/LoadMapLootDisabled, ObjectMgr/LoadNpcGossips, ObjectMgr/LoadNPCText, ObjectMgr/LoadPageTextLocales, ObjectMgr/LoadPetCreateSpells, ObjectMgr/LoadPetLevelInfo, ObjectMgr/LoadPetNames, ObjectMgr/LoadPlayerCacheData, ObjectMgr/LoadPlayerInfo, ObjectMgr/LoadPlayerPhaseFromDb, ObjectMgr/LoadPlayerPremadeTemplates, ObjectMgr/LoadPointOfInterestLocales, ObjectMgr/LoadPointsOfInterest, ObjectMgr/LoadQuestAreaTriggers, ObjectMgr/LoadQuestGreetings, ObjectMgr/LoadQuestLocales, ObjectMgr/LoadQuestRelationsHelper, ObjectMgr/LoadQuests, ObjectMgr/LoadReputationOnKill, ObjectMgr/LoadReputationRewardRate, ObjectMgr/LoadReputationSpilloverTemplate, ObjectMgr/LoadReservedPlayersNames, ObjectMgr/LoadSavedVariable, ObjectMgr/LoadSkillLineAbility, ObjectMgr/LoadSoundEntries, ObjectMgr/LoadSpellDisabledEntrys, ObjectMgr/LoadTavernAreaTriggers, ObjectMgr/LoadTaxiNodes, ObjectMgr/LoadTaxiPathTransitions, ObjectMgr/LoadTrainerGreetings, ObjectMgr/LoadTrainers#2, ObjectMgr/LoadVendors#2, ObjectMgr/LoadWorldSafeLocsFacing, ObjectMgr/RestoreDeletedItems, PoolManager/LoadFromDB, ScriptMgr/Initialize, ScriptMgr/LoadEscortData, ScriptMgr/LoadEventIdScripts, ScriptMgr/LoadScriptNames, ScriptMgr/LoadScripts, ScriptMgr/LoadScriptTexts, ScriptMgr/LoadScriptTextsCustom, ScriptMgr/LoadScriptWaypoints, SpellMgr/CheckUsedSpells, SpellMgr/LoadSpellAreas, SpellMgr/LoadSpellChains, SpellMgr/LoadSpellCones, SpellMgr/LoadSpellElixirs, SpellMgr/LoadSpellEnchantCharges, SpellMgr/LoadSpellGroups, SpellMgr/LoadSpellGroupStackRules, SpellMgr/LoadSpellLearnSpells, SpellMgr/LoadSpellPetAuras, SpellMgr/LoadSpellProcEvents, SpellMgr/LoadSpellProcItemEnchant, SpellMgr/LoadSpells, SpellMgr/LoadSpellScriptTarget, SpellMgr/LoadSpellTargetPositions, SpellMgr/LoadSpellThreats, SpellModMgr/LoadSpellMods, WaypointManager/Load, Weather/LoadWeatherZoneChances | — |
| BarGoLink#2 | ctor | Errors/PrintStacktraceAndThrow | DBCStores/LoadDBCStores, SpellMgr/LoadSkillLineAbilityMaps, SpellMgr/LoadSkillRaceClassInfoMap, SpellMgr/LoadSpellLearnSkills, WaypointManager/Load | — |
| BarGoLink#3 | ctor | Errors/PrintStacktraceAndThrow | AuctionHouseMgr/LoadAuctionItems, AuctionHouseMgr/LoadAuctions, AuraRemovalMgr/LoadFromDB, AutoBroadCastMgr/Load, BattleGroundMgr/CreateInitialBattleGrounds, BattleGroundMgr/LoadBattleEventIndexes, BattleGroundMgr/LoadBattleMastersEntry, CharacterDatabaseCleaner/CheckUnique, ChatHandler.AuctionHouseBotMgr/Load, CreatureEventAIMgr/LoadCreatureEventAI_Events, CreatureGroups/Load, CreatureLinkingMgr/LoadFromDB, GameEventMgr.Main/LoadFromDB, GuildMgr/LoadGuilds, GuildMgr/LoadPetitions, InstanceStatistics/LoadFromDB, ItemEnchantmentMgr/LoadRandomEnchantmentsTable, LootMgr/LoadLootTable, MapPersistentStateMgr/LoadCreatureRespawnTimes, MapPersistentStateMgr/LoadGameobjectRespawnTimes, MapPersistentStateMgr/PackInstances, ObjectMgr/LoadAreaLocales, ObjectMgr/LoadAreaTriggerLocales, ObjectMgr/LoadAreaTriggers, ObjectMgr/LoadAreaTriggerTeleports, ObjectMgr/LoadBattlegroundEntranceTriggers, ObjectMgr/LoadBroadcastTextLocales, ObjectMgr/LoadBroadcastTexts, ObjectMgr/LoadCinematicsWaypoints, ObjectMgr/LoadCorpses, ObjectMgr/LoadCreatureLocales, ObjectMgr/LoadCreatures, ObjectMgr/LoadCreatureSpells, ObjectMgr/LoadEquipmentTemplates, ObjectMgr/LoadExplorationBaseXP, ObjectMgr/LoadFactionChangeItems, ObjectMgr/LoadFactionChangeMounts, ObjectMgr/LoadFactionChangeQuests, ObjectMgr/LoadFactionChangeReputations, ObjectMgr/LoadFactionChangeSpells, ObjectMgr/LoadFactions, ObjectMgr/LoadFishingBaseSkillLevel, ObjectMgr/LoadGameObjectForQuests, ObjectMgr/LoadGameObjectLocales, ObjectMgr/LoadGameobjects, ObjectMgr/LoadGameobjectsRequirements, ObjectMgr/LoadGameTele, ObjectMgr/LoadGossipMenu, ObjectMgr/LoadGossipMenuItems, ObjectMgr/LoadGossipMenuItemsLocales, ObjectMgr/LoadGraveyardZones, ObjectMgr/LoadGroups, ObjectMgr/LoadItemLocales, ObjectMgr/LoadItemPrototypes, ObjectMgr/LoadItemRequiredTarget, ObjectMgr/LoadItemTexts, ObjectMgr/LoadMangosStrings#2, ObjectMgr/LoadMapLootDisabled, ObjectMgr/LoadNpcGossips, ObjectMgr/LoadNPCText, ObjectMgr/LoadPageTextLocales, ObjectMgr/LoadPetCreateSpells, ObjectMgr/LoadPetLevelInfo, ObjectMgr/LoadPetNames, ObjectMgr/LoadPlayerCacheData, ObjectMgr/LoadPlayerInfo, ObjectMgr/LoadPlayerPhaseFromDb, ObjectMgr/LoadPlayerPremadeTemplates, ObjectMgr/LoadPointOfInterestLocales, ObjectMgr/LoadPointsOfInterest, ObjectMgr/LoadQuestAreaTriggers, ObjectMgr/LoadQuestGreetings, ObjectMgr/LoadQuestLocales, ObjectMgr/LoadQuestRelationsHelper, ObjectMgr/LoadQuests, ObjectMgr/LoadReputationOnKill, ObjectMgr/LoadReputationRewardRate, ObjectMgr/LoadReputationSpilloverTemplate, ObjectMgr/LoadReservedPlayersNames, ObjectMgr/LoadSavedVariable, ObjectMgr/LoadSkillLineAbility, ObjectMgr/LoadSoundEntries, ObjectMgr/LoadSpellDisabledEntrys, ObjectMgr/LoadTavernAreaTriggers, ObjectMgr/LoadTaxiNodes, ObjectMgr/LoadTaxiPathTransitions, ObjectMgr/LoadTrainerGreetings, ObjectMgr/LoadTrainers#2, ObjectMgr/LoadVendors#2, ObjectMgr/LoadWorldSafeLocsFacing, ObjectMgr/PackGroupIds, ObjectMgr/RestoreDeletedItems, PoolManager/LoadFromDB, ScriptMgr/LoadEscortData, ScriptMgr/LoadEventIdScripts, ScriptMgr/LoadScripts, ScriptMgr/LoadScriptTexts, ScriptMgr/LoadScriptTextsCustom, ScriptMgr/LoadScriptWaypoints, SpellMgr/CheckUsedSpells, SpellMgr/LoadSpellAreas, SpellMgr/LoadSpellChains, SpellMgr/LoadSpellCones, SpellMgr/LoadSpellElixirs, SpellMgr/LoadSpellEnchantCharges, SpellMgr/LoadSpellGroups, SpellMgr/LoadSpellGroupStackRules, SpellMgr/LoadSpellLearnSpells, SpellMgr/LoadSpellPetAuras, SpellMgr/LoadSpellProcEvents, SpellMgr/LoadSpellProcItemEnchant, SpellMgr/LoadSpells, SpellMgr/LoadSpellScriptTarget, SpellMgr/LoadSpellTargetPositions, SpellMgr/LoadSpellThreats, SpellModMgr/LoadSpellMods, WaypointManager/Load, Weather/LoadWeatherZoneChances | — |
| ~BarGoLink | dtor | — | — | — |
| init | method | — | — | — |
| step | method | — | AccountMgr/LoadAccountBanList, AccountMgr/LoadIPBanList, AuctionHouseMgr/LoadAuctionItems, AuctionHouseMgr/LoadAuctions, AuraRemovalMgr/LoadFromDB, AutoBroadCastMgr/Load, BattleGroundMgr/CreateInitialBattleGrounds, BattleGroundMgr/LoadBattleEventIndexes, BattleGroundMgr/LoadBattleMastersEntry, CharacterDatabaseCleaner/CheckUnique, ChatHandler.AuctionHouseBotMgr/Load, CreatureEventAIMgr/LoadCreatureEventAI_Events, CreatureGroups/Load, CreatureLinkingMgr/LoadFromDB, GameEventMgr.Main/LoadFromDB, GuildMgr/LoadGuilds, GuildMgr/LoadPetitions, InstanceStatistics/LoadFromDB, ItemEnchantmentMgr/LoadRandomEnchantmentsTable, Log.Main/WaitBeforeContinueIfNeed, LootMgr/LoadLootTable, MapPersistentStateMgr/CleanupInstances, MapPersistentStateMgr/LoadCreatureRespawnTimes, MapPersistentStateMgr/LoadGameobjectRespawnTimes, MapPersistentStateMgr/PackInstances, ObjectMgr/LoadAreaLocales, ObjectMgr/LoadAreaTriggerLocales, ObjectMgr/LoadAreaTriggers, ObjectMgr/LoadAreaTriggerTeleports, ObjectMgr/LoadBattlegroundEntranceTriggers, ObjectMgr/LoadBroadcastTextLocales, ObjectMgr/LoadBroadcastTexts, ObjectMgr/LoadCinematicsWaypoints, ObjectMgr/LoadCorpses, ObjectMgr/LoadCreatureLocales, ObjectMgr/LoadCreatures, ObjectMgr/LoadCreatureSpells, ObjectMgr/LoadEquipmentTemplates, ObjectMgr/LoadExplorationBaseXP, ObjectMgr/LoadFactionChangeItems, ObjectMgr/LoadFactionChangeMounts, ObjectMgr/LoadFactionChangeQuests, ObjectMgr/LoadFactionChangeReputations, ObjectMgr/LoadFactionChangeSpells, ObjectMgr/LoadFactions, ObjectMgr/LoadFishingBaseSkillLevel, ObjectMgr/LoadGameObjectForQuests, ObjectMgr/LoadGameObjectLocales, ObjectMgr/LoadGameobjects, ObjectMgr/LoadGameobjectsRequirements, ObjectMgr/LoadGameTele, ObjectMgr/LoadGossipMenu, ObjectMgr/LoadGossipMenuItems, ObjectMgr/LoadGossipMenuItemsLocales, ObjectMgr/LoadGraveyardZones, ObjectMgr/LoadGroups, ObjectMgr/LoadItemLocales, ObjectMgr/LoadItemPrototypes, ObjectMgr/LoadItemRequiredTarget, ObjectMgr/LoadItemTexts, ObjectMgr/LoadMangosStrings#2, ObjectMgr/LoadMapLootDisabled, ObjectMgr/LoadNpcGossips, ObjectMgr/LoadNPCText, ObjectMgr/LoadPageTextLocales, ObjectMgr/LoadPetCreateSpells, ObjectMgr/LoadPetLevelInfo, ObjectMgr/LoadPetNames, ObjectMgr/LoadPlayerCacheData, ObjectMgr/LoadPlayerInfo, ObjectMgr/LoadPlayerPhaseFromDb, ObjectMgr/LoadPlayerPremadeTemplates, ObjectMgr/LoadPointOfInterestLocales, ObjectMgr/LoadPointsOfInterest, ObjectMgr/LoadQuestAreaTriggers, ObjectMgr/LoadQuestGreetings, ObjectMgr/LoadQuestLocales, ObjectMgr/LoadQuestRelationsHelper, ObjectMgr/LoadQuests, ObjectMgr/LoadReputationOnKill, ObjectMgr/LoadReputationRewardRate, ObjectMgr/LoadReputationSpilloverTemplate, ObjectMgr/LoadReservedPlayersNames, ObjectMgr/LoadSavedVariable, ObjectMgr/LoadSkillLineAbility, ObjectMgr/LoadSoundEntries, ObjectMgr/LoadSpellDisabledEntrys, ObjectMgr/LoadTavernAreaTriggers, ObjectMgr/LoadTaxiNodes, ObjectMgr/LoadTaxiPathTransitions, ObjectMgr/LoadTrainerGreetings, ObjectMgr/LoadTrainers#2, ObjectMgr/LoadVendors#2, ObjectMgr/LoadWorldSafeLocsFacing, ObjectMgr/PackGroupIds, ObjectMgr/RestoreDeletedItems, PoolManager/LoadFromDB, ScriptMgr/Initialize, ScriptMgr/LoadEscortData, ScriptMgr/LoadEventIdScripts, ScriptMgr/LoadScriptNames, ScriptMgr/LoadScripts, ScriptMgr/LoadScriptTexts, ScriptMgr/LoadScriptTextsCustom, ScriptMgr/LoadScriptWaypoints, SpellMgr/CheckUsedSpells, SpellMgr/LoadSkillLineAbilityMaps, SpellMgr/LoadSkillRaceClassInfoMap, SpellMgr/LoadSpellAreas, SpellMgr/LoadSpellChains, SpellMgr/LoadSpellCones, SpellMgr/LoadSpellElixirs, SpellMgr/LoadSpellEnchantCharges, SpellMgr/LoadSpellGroups, SpellMgr/LoadSpellGroupStackRules, SpellMgr/LoadSpellLearnSkills, SpellMgr/LoadSpellLearnSpells, SpellMgr/LoadSpellPetAuras, SpellMgr/LoadSpellProcEvents, SpellMgr/LoadSpellProcItemEnchant, SpellMgr/LoadSpells, SpellMgr/LoadSpellScriptTarget, SpellMgr/LoadSpellTargetPositions, SpellMgr/LoadSpellThreats, SpellModMgr/LoadSpellMods, WaypointManager/Load, Weather/LoadWeatherZoneChances | — |
| SetOutputState | method | — | realmd_Main/main | — |
