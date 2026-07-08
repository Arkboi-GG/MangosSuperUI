<!-- provenance: no-member-reference-section, failed-members, boundary-bleed -->
# ChatHandler.ServerCommands

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ChatHandler.ServerCommands

## Purpose & Responsibilities

The `ChatHandler.ServerCommands` unit implements a comprehensive suite of administrative, maintenance, and debugging commands for the WoWVMaNGOS server core. These commands are accessible via the in-game chat interface (for Game Masters and Administrators) and the remote console (CLI). The unit serves as the primary interface for server operators to manage the live environment without requiring a full server restart. Its core responsibilities include:

1.  **Server Lifecycle Management:** Controlling server shutdown, restart, and idle behaviors, as well as retrieving real-time server status information (uptime, player counts, session queues).
2.  **Dynamic Data Reloading:** Allowing administrators to reload specific database tables (NPCs, items, spells, scripts, loot tables, locales, etc.) into memory. This is critical for live development, hot-fixing configuration errors, and updating content on a running server.
3.  **World State Manipulation:** Modifying environmental conditions (weather), managing "world masks" (visibility layers/groups), and resetting instance states (raids).
4.  **Anti-Spam & Moderation Tools:** Managing blacklists and replacement rules for chat spam filtering via direct database manipulation.
5.  **Event Management:** Listing, inspecting, starting, stopping, enabling, and disabling scheduled game events.
6.  **Logging Control:** Viewing archived logs and adjusting log filters and verbosity levels dynamically.
7.  **Debugging & Inspection:** Providing tools to inspect unit visibility, map states, and internal server variables.

This unit acts as a bridge between textual command input and the underlying subsystems (`World`, `ObjectMgr`, `SpellMgr`, `ScriptMgr`, `GameEventMgr`, etc.). It parses arguments, validates inputs, and delegates execution to the appropriate manager singletons. Note that permission checking, command routing, and helper methods like `SendSysMessage` or `ExtractUInt32` are handled by the broader `ChatHandler` class infrastructure (defined in `Chat.h` and other partials), not within this specific unit's logic.

## Member-by-Member Behavior

### Server Information & Lifecycle

These commands provide visibility into server health and control its termination.

*   **HandleServerInfoCommand**: Retrieves current active and queued session counts, maximum historical counts, and uptime from `World`. It formats the uptime using `shared_Util::secsToTimeString` and displays the core revision number.
*   **HandleServerMotdCommand**: Displays the current "Message of the Day" retrieved via `World::GetMotd`.
*   **HandleServerSetMotdCommand**: Updates the server's MOTD using `World::SetMotd` and confirms the change.
*   **HandleServerShutDownCommand**, **HandleServerRestartCommand**, **HandleServerIdleShutDownCommand**, **HandleServerIdleRestartCommand**: These four commands follow an identical pattern: they extract a delay (seconds) and an optional exit code (defaulting to `SHUTDOWN_EXIT_CODE` or `RESTART_EXIT_CODE`). They validate that the exit code is $\le$ 125 (to avoid shell interpretation conflicts). They then call `World::ShutdownServ` with specific flags:
    *   Shutdown: No flags.
    *   Restart: `SHUTDOWN_MASK_RESTART`.
    *   Idle Shutdown: `SHUTDOWN_MASK_IDLE`.
    *   Idle Restart: `SHUTDOWN_MASK_RESTART | SHUTDOWN_MASK_IDLE`.
*   **HandleServerShutDownCancelCommand**: Cancels a pending shutdown via `World::ShutdownCancel`.
*   **HandleServerExitCommand**: Immediately stops the server by calling `World::StopNow` with `SHUTDOWN_EXIT_CODE`.
*   **HandleQuitCommand**: A legacy command that sends an error message (`LANG_QUIT_WRONG_USE_ERROR`), noting that quit processing is handled elsewhere (RASocket).

### World State & Environment

*   **HandleChangeWeatherCommand**: Changes the weather for the issuing player's current zone.
    1.  Checks if weather is enabled globally via `World::getConfig(CONFIG_BOOL_WEATHER)`.
    2.  Extracts weather type and grade (float).
    3.  Validates the weather type using `Weather::IsValidWeatherType`.
    4.  Clamps the grade to `[0.0, 1.0]`.
    5.  Retrieves the player's zone ID and map, then calls `Map::SetWeather` to apply the change.
*   **HandleAnnounceCommand**: Sends a system-wide text message using `World::SendWorldText`.
*   **HandleNotifyCommand**: Constructs a `WorldPacket` of type `SMSG_NOTIFICATION` containing a localized string prefix (`LANG_GLOBAL_NOTIFY`) plus the user's argument, then broadcasts it via `World::SendGlobalMessage`.

### Player Limits & Session Management

*   **HandleServerPLimitCommand**: Manages the maximum number of players allowed on the server and the minimum security level required to connect.
    *   It accepts literals like "player", "moderator", "gamemaster", "administrator", or "reset".
    *   It accepts an integer value for the limit.
    *   It calls `World::SetPlayerLimit` to update the threshold.
    *   Crucially, if the new limit is higher than the current player count, it calls `World::KickAllLess` to remove players below the new minimum security level.
    *   Finally, it reports the current limit and security level name.
*   **HandleSaveAllCommand**: Forces a save of all online players to the database by calling `ObjectAccessor::SaveAllPlayers`.
*   **HandleServerCorpsesCommand**: Triggers the cleanup of old corpses by calling `ObjectAccessor::RemoveOldCorpses`.

### Instance & Raid Management

*   **HandleServerResetAllRaidCommand**: Resets all raid instances. It notifies users that players in raids will be teleported to their homebind, then calls `MapPersistentStateMgr::ResetAllRaid` via the scheduler.

### Logging

*   **HandleViewLogCommand**: Retrieves an archived log message by ID using `World::GetLog`. If found, it prints the message content; otherwise, it reports an error.
*   **HandleServerLogFilterCommand**: Manages log filters.
    *   With no arguments, it lists all known filters and their current on/off state using `Log::HasLogFilter`.
    *   With arguments, it extracts a filter name and an on/off boolean. It supports "all" to toggle everything. It matches partial names against the internal `logFilterData` array and applies changes via `Log::SetLogFilter`.
*   **HandleServerLogLevelCommand**: Displays or sets the console and file log levels. It reads current levels via `Log::GetConsoleLevel`/`Log::GetFileLevel` and sets the console level via `Log::SetConsoleLevel` using `atoi`.

### Anti-Spam Management

These commands manage two database tables: `antispam_blacklist` and `antispam_replacement`. They require the Anticheat module to be active (`sAnticheatMgr->GetAntispam()`).

*   **HandleAntiSpamAdd**: Inserts a word and a ban duration into `antispam_blacklist` using `LoginDatabase::PExecute`.
*   **HandleAntiSpamRemove**: Deletes a word from `antispam_blacklist`.
*   **HandleAntiSpamReplace**: Inserts a "from" and "to" string pair into `antispam_replacement`.
*   **HandleAntiSpamRemoveReplace**: Deletes a "from" entry from `antispam_replacement`.

*Note: These commands construct SQL strings directly using `sprintf`-style formatting within `PExecute`. While convenient, this pattern is susceptible to SQL injection if input validation is insufficient, though `ExtractQuotedArg` provides some protection.*

### World Masks (Visibility Groups)

These commands manipulate the `WorldMask` property of units, which determines which "worlds" or visibility layers a unit exists in.

*   **HandleWorldUpdateCommand**: Gets the selected unit. If no argument is provided, it prints the unit's current `WorldMask`. If an argument is provided, it parses an integer and sets the unit's mask via `WorldObject::SetWorldMask`.
*   **HandleWorldTestCommand**: Tests visibility between the issuing player and the selected target. It prints both masks and uses `WorldObject::CanSeeInWorld` to determine if they can see each other.
*   **HandleWorldDetailCommand**: Prints the selected unit's `WorldMask` and iterates through bits to identify which specific world IDs are active in the mask.

### Spell Groups

*   **HandleGroupAddSpellCommand**: Adds a spell to a spell group in the database.
    1.  Extracts spell ID and group ID.
    2.  Validates the spell exists via `SpellMgr::GetSpellEntry`.
    3.  Displays spell info using `ChatHandler::ShowSpellListHelper`.
    4.  Inserts the record into `spell_group` via `WorldDatabase::PExecute`.
*   **HandleGroupSetRuleCommand**: Sets the stacking rule for a spell group. It defaults to `SPELL_GROUP_STACK_RULE_EXCLUSIVE` if no rule is specified. It executes a `REPLACE INTO` on `spell_group_stack_rules`.
*   **HandleReloadSpellGroupCommand**: Reloads `spell_group` data via `SpellMgr::LoadSpellGroups`.
*   **HandleReloadSpellGroupStackRulesCommand**: Reloads `spell_group_stack_rules` via `SpellMgr::LoadSpellGroupStackRules`.

### Game Events

These commands interact with `GameEventMgr` to manage timed or manual game events.

*   **HandleEventListCommand**: Iterates through `GameEventMgr::GetEventMap`. It filters for valid events. If the argument is "all", it shows inactive events too. It prints event ID, description, and active/inactive status.
*   **HandleEventInfoCommand**: Provides detailed info for a specific event ID. It calculates the next check time using `GameEventMgr::NextCheck` and formats timestamps using `shared_Util::TimeToTimestampStr`.
*   **HandleEventStartCommand**: Starts an event. It validates the event exists, is valid, is not already active, and is enabled. Then calls `GameEventMgr::StartEvent`.
*   **HandleEventStopCommand**: Stops an active event. Validates existence and active status. Calls `GameEventMgr::StopEvent`.
*   **HandleEventEnableCommand**: Enables a disabled event. Validates existence and disabled status. Calls `GameEventMgr::EnableEvent(event_id, true)`.
*   **HandleEventDisableCommand**: Disables an enabled event. Validates existence and enabled status. Calls `GameEventMgr::EnableEvent(event_id, false)`.

### Saved Variables

*   **HandleVariableCommand**: Manages runtime saved variables stored in `ObjectMgr`.
    *   If a value is provided, it sets the variable using `ObjectMgr::SetSavedVariable`. It compares the new value against the old value (retrieved via `ObjectMgr::GetSavedVariable`) to report whether it was newly created or modified.
    *   If no value is provided, it retrieves and displays the current value of the variable.
    *   Uses `sscanf` for parsing index and value.

### Dynamic Reloading (The "Reload" Suite)

This is the largest section of the unit. Most reload commands follow a strict pattern:
1.  Log the action via `Log::Out`.
2.  Call the corresponding `Load...` method on a manager singleton (e.g., `ObjectMgr`, `SpellMgr`, `ScriptMgr`).
3.  Send a confirmation message to the user.

Some commands have additional logic or dependencies:

#### Meta-Reload Commands

*   **HandleReloadAllCommand**: A meta-command that calls a subset of other reload commands: `HandleReloadSkillFishingBaseLevelCommand`, `HandleReloadAllAreaCommand`, `HandleReloadEventAIEventsCommand`, `HandleReloadAllLootCommand`, `HandleReloadAllNpcCommand`, `HandleReloadAllQuestCommand`, `HandleReloadAllSpellCommand`, `HandleReloadAllItemCommand`, `HandleReloadAllGossipsCommand`, `HandleReloadAllLocalesCommand`, `HandleReloadCommandCommand`, `HandleReloadReservedNameCommand`, `HandleReloadMangosStringCommand`, `HandleReloadGameTeleCommand`, and `HandleReloadBattleEventCommand`.
*   **HandleReloadAllAreaCommand**: Calls `HandleReloadAreaTriggerTeleportCommand`, `HandleReloadAreaTriggerTavernCommand`, and `HandleReloadGameGraveyardZoneCommand`.
*   **HandleReloadAllLootCommand**: Logs the action, creates an empty `LootIdSet`, calls `LootMgr::LoadLootTables`, and sends a confirmation.
*   **HandleReloadAllNpcCommand**: Calls `HandleReloadNpcGossipCommand`, `HandleReloadNpcTrainerCommand`, `HandleReloadNpcVendorCommand`, and `HandleReloadPointsOfInterestCommand`.
*   **HandleReloadAllQuestCommand**: Calls `HandleReloadQuestAreaTriggersCommand`, `HandleReloadQuestTemplateCommand`, and explicitly calls `ObjectMgr::LoadQuestRelations`.
*   **HandleReloadAllScriptsCommand**: First checks `ScriptMgr::IsScriptScheduled()`. If scripts are running, it aborts. Otherwise, it calls reload commands for GameObject, Gossip, Generic, Event, Quest End, Quest Start, Spell, and Creature Spell scripts. Finally, it calls `ScriptMgr::CheckAllScriptTexts`.
*   **HandleReloadAllSpellCommand**: Calls reload commands for Spell Template, Area, Chain, Elixir, Learn Spell, Proc Event, Proc Item Enchant, Script Target, Target Position, Threats, and Pet Auras.
*   **HandleReloadAllGossipsCommand**: Calls reload commands for Gossip Menu, Gossip Menu Option, Gossip Scripts (if not already done), NPC Gossip, and Points of Interest.
*   **HandleReloadAllItemCommand**: Calls reload commands for Page Texts, Item Enchantments, and Item Required Targets.
*   **HandleReloadAllLocalesCommand**: Calls reload commands for Locales Creature, Gameobject, Gossip Menu Option, Item, Page Text, Points of Interest, and Quest.

#### Specific Reload Commands

*   **HandleReloadConfigCommand**: Reloads world configuration settings via `World::LoadConfigSettings(true)`.
*   **HandleReloadAreaTriggerTavernCommand**: Reloads tavern area triggers via `ObjectMgr::LoadTavernAreaTriggers`.
*   **HandleReloadAreaTriggerTeleportCommand**: Reloads teleport area triggers via `ObjectMgr::LoadAreaTriggerTeleports`.
*   **HandleReloadCommandCommand**: Sets a static flag `m_loadCommandTable = true`. The actual reload happens lazily at the next chat command usage, avoiding potential issues with reloading the command table while commands are being parsed.
*   **HandleReloadCreatureSpellsCommand**: Reloads creature spells via `ObjectMgr::LoadCreatureSpells`.
*   **HandleReloadCreatureQuestRelationsCommand**: Reloads creature quest giver relations via `ObjectMgr::LoadCreatureQuestRelations`.
*   **HandleReloadCreatureQuestInvRelationsCommand**: Reloads creature quest taker relations via `ObjectMgr::LoadCreatureInvolvedRelations`.
*   **HandleReloadGossipMenuCommand**: Builds a set of gossip script IDs from `sGossipScripts` and passes it to `ObjectMgr::LoadGossipMenu` to ensure consistency.
*   **HandleReloadGossipMenuOptionCommand**: Builds a set of gossip script IDs from `sGossipScripts` and passes it to `ObjectMgr::LoadGossipMenuItems`.
*   **HandleReloadGossipScriptsCommand**: Checks if scripts are scheduled. If not, calls `ScriptMgr::LoadGossipScripts`.
*   **HandleReloadGOQuestRelationsCommand**: Reloads gameobject quest giver relations via `ObjectMgr::LoadGameobjectQuestRelations`.
*   **HandleReloadGORequirementsCommand**: Reloads gameobject requirements via `ObjectMgr::LoadGameobjectsRequirements`.
*   **HandleReloadGOQuestInvRelationsCommand**: Reloads gameobject quest taker relations via `ObjectMgr::LoadGameobjectInvolvedRelations`.
*   **HandleReloadQuestAreaTriggersCommand**: Reloads quest area triggers via `ObjectMgr::LoadQuestAreaTriggers`.
*   **HandleReloadQuestTemplateCommand**: Reloads quest templates via `ObjectMgr::LoadQuests` and also reloads gameobjects for quests via `ObjectMgr::LoadGameObjectForQuests`.
*   **HandleReloadQuestGreetingCommand**: Reloads quest greetings via `ObjectMgr::LoadQuestGreetings`.
*   **HandleReloadTrainerGreetingCommand**: Reloads trainer greetings via `ObjectMgr::LoadTrainerGreetings`.
*   **HandleReloadLootTemplatesCreatureCommand**: Reloads creature loot templates via `LootMgr::LoadLootTemplates_Creature` and checks refs.
*   **HandleReloadLootTemplatesDisenchantCommand**: Reloads disenchant loot templates via `LootMgr::LoadLootTemplates_Disenchant` and checks refs.
*   **HandleReloadLootTemplatesFishingCommand**: Reloads fishing loot templates via `LootMgr::LoadLootTemplates_Fishing` and checks refs.
*   **HandleReloadLootTemplatesGameobjectCommand**: Reloads gameobject loot templates via `LootMgr::LoadLootTemplates_Gameobject` and checks refs.
*   **HandleReloadLootTemplatesItemCommand**: Reloads item loot templates via `LootMgr::LoadLootTemplates_Item` and checks refs.
*   **HandleReloadLootTemplatesPickpocketingCommand**: Reloads pickpocketing loot templates via `LootMgr::LoadLootTemplates_Pickpocketing` and checks refs.
*   **HandleReloadLootTemplatesMailCommand**: Reloads mail loot templates via `LootMgr::LoadLootTemplates_Mail` and checks refs.
*   **HandleReloadLootTemplatesReferenceCommand**: Reloads reference loot templates via `LootMgr::LoadLootTemplates_Reference` and checks refs using an ID set.
*   **HandleReloadLootTemplatesSkinningCommand**: Reloads skinning loot templates via `LootMgr::LoadLootTemplates_Skinning` and checks refs.
*   **HandleReloadMangosStringCommand**: Reloads mangos strings via `ObjectMgr::LoadMangosStrings`.
*   **HandleReloadNpcGossipCommand**: Reloads NPC gossip via `ObjectMgr::LoadNpcGossips`.
*   **HandleReloadNpcTextCommand**: Reloads NPC text via `ObjectMgr::LoadNPCText`.
*   **HandleReloadNpcTrainerCommand**: Reloads trainer templates via `ObjectMgr::LoadTrainerTemplates` and trainers via `ObjectMgr::LoadTrainers`.
*   **HandleReloadNpcVendorCommand**: Reloads vendor templates via `ObjectMgr::LoadVendorTemplates` and vendors via `ObjectMgr::LoadVendors`.
*   **HandleReloadPointsOfInterestCommand**: Reloads points of interest via `ObjectMgr::LoadPointsOfInterest`.
*   **HandleReloadReservedNameCommand**: Reloads reserved player names via `ObjectMgr::LoadReservedPlayersNames`.
*   **HandleReloadReputationRewardRateCommand**: Reloads reputation reward rates via `ObjectMgr::LoadReputationRewardRate`.
*   **HandleReloadReputationSpilloverTemplateCommand**: Reloads reputation spillover templates via `ObjectMgr::LoadReputationSpilloverTemplate`.
*   **HandleReloadSkillFishingBaseLevelCommand**: Reloads fishing base skill levels via `ObjectMgr::LoadFishingBaseSkillLevel`.
*   **HandleReloadSpellAreaCommand**: Reloads spell areas via `SpellMgr::LoadSpellAreas`.
*   **HandleReloadSpellChainCommand**: Reloads spell chains via `SpellMgr::LoadSpellChains`.
*   **HandleReloadSpellElixirCommand**: Reloads spell elixirs via `SpellMgr::LoadSpellElixirs`.
*   **HandleReloadSpellLearnSpellCommand**: Reloads spell learn spells via `SpellMgr::LoadSpellLearnSpells`.
*   **HandleReloadSpellProcEventCommand**: Reloads spell proc events via `SpellMgr::LoadSpellProcEvents`.
*   **HandleReloadSpellProcItemEnchantCommand**: Reloads spell proc item enchants via `SpellMgr::LoadSpellProcItemEnchant`.
*   **HandleReloadSpellScriptTargetCommand**: Reloads spell script targets via `SpellMgr::LoadSpellScriptTarget`.
*   **HandleReloadSpellTargetPositionCommand**: Reloads spell target positions via `SpellMgr::LoadSpellTargetPositions`.
*   **HandleReloadSpellTemplateCommand**: Reloads spell templates via `SpellMgr::LoadSpells` and spell mods via `SpellModMgr::LoadSpellMods`.
*   **HandleReloadSpellThreatsCommand**: Reloads spell threats via `SpellMgr::LoadSpellThreats`.
*   **HandleReloadSpellPetAurasCommand**: Reloads spell pet auras via `SpellMgr::LoadSpellPetAuras`.
*   **HandleReloadPageTextsCommand**: Reloads page texts via `ObjectMgr::LoadPageTexts`.
*   **HandleReloadItemEnchantementsCommand**: Reloads random enchantments via `ItemEnchantmentMgr::LoadRandomEnchantmentsTable`.
*   **HandleReloadItemRequiredTragetCommand**: Reloads item required targets via `ObjectMgr::LoadItemRequiredTarget`.
*   **HandleReloadBattleEventCommand**: Reloads battleground event indexes via `BattleGroundMgr::LoadBattleEventIndexes`.
*   **HandleReloadGameObjectScriptsCommand**: Checks if scripts are scheduled. If not, calls `ScriptMgr::LoadGameObjectScripts`.
*   **HandleReloadGenericScriptsCommand**: Checks if scripts are scheduled. If not, calls `ScriptMgr::LoadGenericScripts`.
*   **HandleReloadEventScriptsCommand**: Checks if scripts are scheduled. If not, calls `ScriptMgr::LoadEventScripts`.
*   **HandleReloadEventAIEventsCommand**: Clears event data via `CreatureEventAIMgr::ClearEventData`, loads creature AI scripts via `ScriptMgr::LoadCreatureEventAIScripts`, and loads creature AI events via `CreatureEventAIMgr::LoadCreatureEventAI_Events`.
*   **HandleReloadQuestEndScriptsCommand**: Checks if scripts are scheduled. If not, calls `ScriptMgr::LoadQuestEndScripts`.
*   **HandleReloadQuestStartScriptsCommand**: Checks if scripts are scheduled. If not, calls `ScriptMgr::LoadQuestStartScripts`.
*   **HandleReloadCreatureSpellScriptsCommand**: Checks if scripts are scheduled. If not, calls `ScriptMgr::LoadCreatureSpellScripts`.
*   **HandleReloadSpellScriptsCommand**: Checks if scripts are scheduled. If not, calls `ScriptMgr::LoadSpellScripts`.
*   **HandleReloadGameGraveyardZoneCommand**: Reloads graveyard zones via `ObjectMgr::LoadGraveyardZones`.
*   **HandleReloadGameTeleCommand**: Reloads game teleports via `ObjectMgr::LoadGameTele`.
*   **HandleReloadTaxiPathTransitionsCommand**: Reloads taxi path transitions via `ObjectMgr::LoadTaxiPathTransitions`.
*   **HandleReloadLocalesCreatureCommand**: Reloads creature locales via `ObjectMgr::LoadCreatureLocales`.
*   **HandleReloadLocalesGameobjectCommand**: Reloads gameobject locales via `ObjectMgr::LoadGameObjectLocales`.
*   **HandleReloadLocalesGossipMenuOptionCommand**: Reloads gossip menu option locales via `ObjectMgr::LoadGossipMenuItemsLocales`.
*   **HandleReloadLocalesItemCommand**: Reloads item locales via `ObjectMgr::LoadItemLocales`.
*   **HandleReloadLocalesPageTextCommand**: Reloads page text locales via `ObjectMgr::LoadPageTextLocales`.
*   **HandleReloadLocalesPointsOfInterestCommand**: Reloads point of interest locales via `ObjectMgr::LoadPointOfInterestLocales`.
*   **HandleReloadLocalesQuestCommand**: Reloads quest locales via `ObjectMgr::LoadQuestLocales`.
*   **HandleReloadCharacterPetCommand**: Takes a pet ID, validates it, and calls `CharacterDatabaseCache::LoadAll(petId)` to refresh that specific pet's data.
*   **HandleReloadCreatureCommand**: Reloads creature spawns via `ObjectMgr::LoadCreatures(true)`.
*   **HandleReloadGameObjectCommand**: Reloads gameobject spawns via `ObjectMgr::LoadGameobjects(true)`.
*   **HandleReloadItemTemplate**: Reloads item prototypes via `ObjectMgr::LoadItemPrototypes`.
*   **HandleReloadMapTemplate**: Reloads map templates via `ObjectMgr::LoadMapTemplate`.
*   **HandleReloadCreatureTemplatesCommand**: If an entry ID is provided, reloads only that template via `ObjectMgr::LoadCreatureTemplate(entry)`. Otherwise, reloads all via `ObjectMgr::LoadCreatureTemplates`.
*   **HandleReloadGameObjectTemplatesCommand**: If an entry ID is provided, reloads only that template via `ObjectMgr::LoadGameObjectTemplate(entry)`. Otherwise, reloads all via `ObjectMgr::LoadGameObjectTemplates`.
*   **HandleReloadExplorationBaseXp**: Reloads exploration base XP via `ObjectMgr::LoadExplorationBaseXP`.
*   **HandleReloadPetNameGeneration**: Reloads pet names via `ObjectMgr::LoadPetNames`.
*   **HandleReloadCreatureOnKillReputation**: Reloads reputation on kill via `ObjectMgr::LoadReputationOnKill`.
*   **HandleReloadGameWeather**: Reloads weather zone chances via `Weather::LoadWeatherZoneChances`.
*   **HandleReloadFactionChangeReputations**: Reloads faction change reputations via `ObjectMgr::LoadFactionChangeReputations`.
*   **HandleReloadFactionChangeSpells**: Reloads faction change spells via `ObjectMgr::LoadFactionChangeSpells`.
*   **HandleReloadFactionChangeItems**: Reloads faction change items via `ObjectMgr::LoadFactionChangeItems`.
*   **HandleReloadFactionChangeQuests**: Reloads faction change quests via `ObjectMgr::LoadFactionChangeQuests`.
*   **HandleReloadFactionChangeMounts**: Reloads faction change mounts via `ObjectMgr::LoadFactionChangeMounts`.
*   **HandleReloadCreatureDisplayInfoAddon**: Reloads creature display info addons via `ObjectMgr::LoadCreatureDisplayInfoAddon`.
*   **HandleReloadIPBanList**: Queries the database using `LOAD_IP_BANS_QUERY` and passes the result to `AccountMgr::LoadIPBanList`.
*   **HandleReloadAccountBanList**: Reloads account ban list via `AccountMgr::LoadAccountBanList`.
*   **HandleReloadInstanceBuffRemoval**: Reloads instance buff removal rules via `AuraRemovalMgr::LoadFromDB`.
*   **HandleReloadPetitions**: Reloads guild petitions via `GuildMgr::LoadPetitions`.
*   **HandleReloadVariablesCommand**: Reloads saved variables via `ObjectMgr::LoadSavedVariable`.
*   **HandleReloadCreatureGroupsCommand**: Reloads creature groups via `CreatureGroupsManager::Load`.
*   **HandleReloadCinematicWaypointsCommand**: Reloads cinematic waypoints via `ObjectMgr::LoadCinematicsWaypoints`.
*   **HandleReloadSpellDisabledCommand**: Reloads disabled spell entries via `ObjectMgr::LoadSpellDisabledEntrys`.
*   **HandleReloadAutoBroadcastCommand**: Reloads autobroadcast messages via `AutoBroadCastMgr::Load`.
*   **HandleReloadSpellModsCommand**: Reloads spell mods via `SpellModMgr::LoadSpellMods`.
*   **HandleReloadMapLootDisabledCommand**: Reloads map loot disabled flags via `ObjectMgr::LoadMapLootDisabled`.
*   **HandleReloadConditionsCommand**: Reloads conditions via `ObjectMgr::LoadConditions`.
*   **HandleReloadAnticheatCommand**: Reloads ant

---

<!-- machine-true, projected from graph.json -->

## Map — ChatHandler.ServerCommands

*Source:* ServerCommands.cpp, Chat.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| HandleAnnounceCommand | method | World/SendWorldText | — | — |
| HandleNotifyCommand | method | ByteBuffer/operator<<, ChatHandler.Chat/GetMangosString, World/SendGlobalMessage, WorldPacket/WorldPacket#4 | — | — |
| HandleVariableCommand | method | ChatHandler.Chat/PSendSysMessage, ObjectMgr/GetSavedVariable, ObjectMgr/SetSavedVariable | — | — |
| HandleChangeWeatherCommand | method | ChatHandler.Chat/ExtractFloat, ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Map.Main/SetWeather, Weather/IsValidWeatherType, World/getConfig, WorldObject.Object/GetMap, WorldObject.Object/GetZoneId, WorldSession.Main/GetPlayer | — | — |
| HandleSaveAllCommand | method | ChatHandler.Chat/SendSysMessage#2, ObjectAccessor/SaveAllPlayers | — | — |
| HandleAntiSpamAdd | method | Anticheat/GetAnticheatLib, Anticheat/GetAntispam, ChatHandler.Chat/ExtractQuotedArg, ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/PSendSysMessage, Database/PExecute#2 | — | antispam_blacklist |
| HandleAntiSpamRemove | method | Anticheat/GetAnticheatLib, Anticheat/GetAntispam, ChatHandler.Chat/ExtractQuotedArg, ChatHandler.Chat/PSendSysMessage, Database/PExecute#2 | — | antispam_blacklist |
| HandleAntiSpamReplace | method | Anticheat/GetAnticheatLib, Anticheat/GetAntispam, ChatHandler.Chat/ExtractQuotedArg, ChatHandler.Chat/PSendSysMessage, Database/PExecute#2 | — | antispam_replacement |
| HandleAntiSpamRemoveReplace | method | Anticheat/GetAnticheatLib, Anticheat/GetAntispam, ChatHandler.Chat/ExtractQuotedArg, ChatHandler.Chat/PSendSysMessage, Database/PExecute#2 | — | antispam_replacement |
| HandleWorldUpdateCommand | method | ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Object/GetGUIDLow, WorldObject.Object/GetWorldMask, WorldObject.Object/SetWorldMask | — | — |
| HandleWorldTestCommand | method | ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, WorldObject.Object/CanSeeInWorld, WorldObject.Object/GetWorldMask, WorldSession.Main/GetPlayer | — | — |
| HandleWorldDetailCommand | method | ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, WorldObject.Object/GetWorldMask | — | — |
| HandleServerInfoCommand | method | ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage, shared_Util/secsToTimeString, World/GetActiveSessionCount, World/GetMaxActiveSessionCount, World/GetMaxQueuedSessionCount, World/GetQueuedSessionCount, World/GetUptime | — | — |
| HandleServerMotdCommand | method | ChatHandler.Chat/PSendSysMessage#2, World/GetMotd | — | — |
| HandleServerSetMotdCommand | method | ChatHandler.Chat/PSendSysMessage#2, World/SetMotd | — | — |
| HandleServerPLimitCommand | method | ChatHandler.Chat/ExtractInt32, ChatHandler.Chat/ExtractLiteralArg, ChatHandler.Chat/PSendSysMessage, Config/GetIntDefault, World/GetPlayerAmountLimit, World/GetPlayerSecurityLimit, World/KickAllLess, World/SetPlayerLimit | — | — |
| HandleServerCorpsesCommand | method | ObjectAccessor/RemoveOldCorpses | — | — |
| HandleServerResetAllRaidCommand | method | ChatHandler.Chat/SendSysMessage, MapPersistentStateManager/GetScheduler, MapPersistentStateMgr/ResetAllRaid | — | — |
| HandleServerShutDownCancelCommand | method | World/ShutdownCancel | — | — |
| HandleServerShutDownCommand | method | ChatHandler.Chat/ExtractOptUInt32, ChatHandler.Chat/ExtractUInt32, World/ShutdownServ | — | — |
| HandleServerRestartCommand | method | ChatHandler.Chat/ExtractOptUInt32, ChatHandler.Chat/ExtractUInt32, World/ShutdownServ | — | — |
| HandleServerIdleRestartCommand | method | ChatHandler.Chat/ExtractOptUInt32, ChatHandler.Chat/ExtractUInt32, World/ShutdownServ | — | — |
| HandleServerIdleShutDownCommand | method | ChatHandler.Chat/ExtractOptUInt32, ChatHandler.Chat/ExtractUInt32, World/ShutdownServ | — | — |
| HandleQuitCommand | method | ChatHandler.Chat/SendSysMessage#2 | — | — |
| HandleServerExitCommand | method | ChatHandler.Chat/SendSysMessage#2, World/StopNow | — | — |
| HandleViewLogCommand | method | ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/GetAccessLevel, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SetSentErrorMessage, World/GetLog | — | — |
| HandleServerLogFilterCommand | method | ChatHandler.Chat/ExtractLiteralArg, ChatHandler.Chat/ExtractOnOff, ChatHandler.Chat/GetOnOffStr, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Log.Main/HasLogFilter, Log.Main/SetLogFilter | — | — |
| HandleServerLogLevelCommand | method | ChatHandler.Chat/PSendSysMessage, Log.Main/GetConsoleLevel, Log.Main/GetFileLevel, Log.Main/SetConsoleLevel | — | — |
| HandleGroupAddSpellCommand | method | ChatHandler.Chat/ExtractSpellIdFromLink, ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/GetSessionDbcLocale, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SetSentErrorMessage, ChatHandler.LookupCommands/ShowSpellListHelper, Database/PExecute#2, SpellMgr/GetSpellEntry, SpellMgr/Instance | — | spell_group |
| HandleGroupSetRuleCommand | method | ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/PSendSysMessage, Database/PExecute#2 | — | — |
| HandleReloadSpellGroupCommand | method | ChatHandler.Chat/SendSysMessage, SpellMgr/Instance, SpellMgr/LoadSpellGroups | — | — |
| HandleReloadSpellGroupStackRulesCommand | method | ChatHandler.Chat/SendSysMessage, SpellMgr/Instance, SpellMgr/LoadSpellGroupStackRules | — | — |
| HandleEventListCommand | method | ChatHandler.Chat/GetMangosString, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, GameEventMgr.Main/GetEventMap, GameEventMgr.Main/IsActiveEvent, GameEventMgr.Main/IsValidEvent | — | — |
| HandleEventInfoCommand | method | ChatHandler.Chat/ExtractUint32KeyFromLink, ChatHandler.Chat/GetMangosString, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, GameEventMgr.Main/GetEventMap, GameEventMgr.Main/IsActiveEvent, GameEventMgr.Main/IsValidEvent, GameEventMgr.Main/NextCheck, shared_Util/secsToTimeString, shared_Util/TimeToTimestampStr | — | — |
| HandleEventStartCommand | method | ChatHandler.Chat/ExtractUint32KeyFromLink, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, GameEventData/isValid, GameEventMgr.Main/GetEventMap, GameEventMgr.Main/IsActiveEvent, GameEventMgr.Main/IsEnabled, GameEventMgr.Main/IsValidEvent, GameEventMgr.Main/StartEvent | — | — |
| HandleEventStopCommand | method | ChatHandler.Chat/ExtractUint32KeyFromLink, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, GameEventData/isValid, GameEventMgr.Main/GetEventMap, GameEventMgr.Main/IsActiveEvent, GameEventMgr.Main/IsValidEvent, GameEventMgr.Main/StopEvent | — | — |
| HandleEventEnableCommand | method | ChatHandler.Chat/ExtractUint32KeyFromLink, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, GameEventData/isValid, GameEventMgr.Main/EnableEvent, GameEventMgr.Main/GetEventMap, GameEventMgr.Main/IsEnabled, GameEventMgr.Main/IsValidEvent | — | — |
| HandleEventDisableCommand | method | ChatHandler.Chat/ExtractUint32KeyFromLink, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, GameEventData/isValid, GameEventMgr.Main/EnableEvent, GameEventMgr.Main/GetEventMap, GameEventMgr.Main/IsEnabled, GameEventMgr.Main/IsValidEvent | — | — |
| HandleReloadAllCommand | method | — | — | — |
| HandleReloadAllAreaCommand | method | — | — | — |
| HandleReloadAllLootCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, LootMgr/LoadLootTables | — | — |
| HandleReloadAllNpcCommand | method | — | — | — |
| HandleReloadAllQuestCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, ObjectMgr/LoadQuestRelations | — | — |
| HandleReloadAllScriptsCommand | method | ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SetSentErrorMessage, Log.Main/Out, ScriptMgr/CheckAllScriptTexts, ScriptMgr/IsScriptScheduled | — | — |
| HandleReloadAllSpellCommand | method | — | — | — |
| HandleReloadAllGossipsCommand | method | — | — | — |
| HandleReloadAllItemCommand | method | — | — | — |
| HandleReloadAllLocalesCommand | method | — | — | — |
| HandleReloadConfigCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, World/LoadConfigSettings | — | — |
| HandleReloadAreaTriggerTavernCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, ObjectMgr/LoadTavernAreaTriggers | — | — |
| HandleReloadAreaTriggerTeleportCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, ObjectMgr/LoadAreaTriggerTeleports | — | — |
| HandleReloadCommandCommand | method | ChatHandler.Chat/SendSysMessage | — | — |
| HandleReloadCreatureSpellsCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, ObjectMgr/LoadCreatureSpells | — | — |
| HandleReloadCreatureQuestRelationsCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, ObjectMgr/LoadCreatureQuestRelations | — | — |
| HandleReloadCreatureQuestInvRelationsCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, ObjectMgr/LoadCreatureInvolvedRelations | — | — |
| HandleReloadGossipMenuCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, ObjectMgr/LoadGossipMenu | — | — |
| HandleReloadGossipMenuOptionCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, ObjectMgr/LoadGossipMenuItems | — | — |
| HandleReloadGossipScriptsCommand | method | ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SetSentErrorMessage, Log.Main/Out, ScriptMgr/IsScriptScheduled, ScriptMgr/LoadGossipScripts | — | — |
| HandleReloadGOQuestRelationsCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, ObjectMgr/LoadGameobjectQuestRelations | — | — |
| HandleReloadGORequirementsCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, ObjectMgr/LoadGameobjectsRequirements | — | — |
| HandleReloadGOQuestInvRelationsCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, ObjectMgr/LoadGameobjectInvolvedRelations | — | — |
| HandleReloadQuestAreaTriggersCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, ObjectMgr/LoadQuestAreaTriggers | — | — |
| HandleReloadQuestTemplateCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, ObjectMgr/LoadGameObjectForQuests, ObjectMgr/LoadQuests | — | — |
| HandleReloadQuestGreetingCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, ObjectMgr/LoadQuestGreetings | — | — |
| HandleReloadTrainerGreetingCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, ObjectMgr/LoadTrainerGreetings | — | — |
| HandleReloadLootTemplatesCreatureCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, LootMgr/CheckLootRefs, LootMgr/LoadLootTemplates_Creature | — | — |
| HandleReloadLootTemplatesDisenchantCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, LootMgr/CheckLootRefs, LootMgr/LoadLootTemplates_Disenchant | — | — |
| HandleReloadLootTemplatesFishingCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, LootMgr/CheckLootRefs, LootMgr/LoadLootTemplates_Fishing | — | — |
| HandleReloadLootTemplatesGameobjectCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, LootMgr/CheckLootRefs, LootMgr/LoadLootTemplates_Gameobject | — | — |
| HandleReloadLootTemplatesItemCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, LootMgr/CheckLootRefs, LootMgr/LoadLootTemplates_Item | — | — |
| HandleReloadLootTemplatesPickpocketingCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, LootMgr/CheckLootRefs, LootMgr/LoadLootTemplates_Pickpocketing | — | — |
| HandleReloadLootTemplatesMailCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, LootMgr/CheckLootRefs, LootMgr/LoadLootTemplates_Mail | — | — |
| HandleReloadLootTemplatesReferenceCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, LootMgr/CheckLootTemplates_Reference, LootMgr/LoadLootTemplates_Reference | — | — |
| HandleReloadLootTemplatesSkinningCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, LootMgr/CheckLootRefs, LootMgr/LoadLootTemplates_Skinning | — | — |
| HandleReloadMangosStringCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, ObjectMgr/LoadMangosStrings | — | — |
| HandleReloadNpcGossipCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, ObjectMgr/LoadNpcGossips | — | — |
| HandleReloadNpcTextCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, ObjectMgr/LoadNPCText | — | — |
| HandleReloadNpcTrainerCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, ObjectMgr/LoadTrainers, ObjectMgr/LoadTrainerTemplates | — | — |
| HandleReloadNpcVendorCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, ObjectMgr/LoadVendors, ObjectMgr/LoadVendorTemplates | — | — |
| HandleReloadPointsOfInterestCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, ObjectMgr/LoadPointsOfInterest | — | — |
| HandleReloadReservedNameCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, ObjectMgr/LoadReservedPlayersNames | — | — |
| HandleReloadReputationRewardRateCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, ObjectMgr/LoadReputationRewardRate | — | — |
| HandleReloadReputationSpilloverTemplateCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, ObjectMgr/LoadReputationSpilloverTemplate | — | — |
| HandleReloadSkillFishingBaseLevelCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, ObjectMgr/LoadFishingBaseSkillLevel | — | — |
| HandleReloadSpellAreaCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, SpellMgr/Instance, SpellMgr/LoadSpellAreas | — | — |
| HandleReloadSpellChainCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, SpellMgr/Instance, SpellMgr/LoadSpellChains | — | — |
| HandleReloadSpellElixirCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, SpellMgr/Instance, SpellMgr/LoadSpellElixirs | — | — |
| HandleReloadSpellLearnSpellCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, SpellMgr/Instance, SpellMgr/LoadSpellLearnSpells | — | — |
| HandleReloadSpellProcEventCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, SpellMgr/Instance, SpellMgr/LoadSpellProcEvents | — | — |
| HandleReloadSpellProcItemEnchantCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, SpellMgr/Instance, SpellMgr/LoadSpellProcItemEnchant | — | — |
| HandleReloadSpellScriptTargetCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, SpellMgr/Instance, SpellMgr/LoadSpellScriptTarget | — | — |
| HandleReloadSpellTargetPositionCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, SpellMgr/Instance, SpellMgr/LoadSpellTargetPositions | — | — |
| HandleReloadSpellTemplateCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, SpellMgr/Instance, SpellMgr/LoadSpells, SpellModMgr/LoadSpellMods | — | — |
| HandleReloadSpellThreatsCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, SpellMgr/Instance, SpellMgr/LoadSpellThreats | — | — |
| HandleReloadSpellPetAurasCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, SpellMgr/Instance, SpellMgr/LoadSpellPetAuras | — | — |
| HandleReloadPageTextsCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, ObjectMgr/LoadPageTexts | — | — |
| HandleReloadItemEnchantementsCommand | method | ChatHandler.Chat/SendSysMessage, ItemEnchantmentMgr/LoadRandomEnchantmentsTable, Log.Main/Out | — | — |
| HandleReloadItemRequiredTragetCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, ObjectMgr/LoadItemRequiredTarget | — | — |
| HandleReloadBattleEventCommand | method | BattleGroundMgr/LoadBattleEventIndexes, ChatHandler.Chat/SendSysMessage, Log.Main/Out | — | — |
| HandleReloadGameObjectScriptsCommand | method | ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SetSentErrorMessage, Log.Main/Out, ScriptMgr/IsScriptScheduled, ScriptMgr/LoadGameObjectScripts | — | — |
| HandleReloadGenericScriptsCommand | method | ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SetSentErrorMessage, Log.Main/Out, ScriptMgr/IsScriptScheduled, ScriptMgr/LoadGenericScripts | — | — |
| HandleReloadEventScriptsCommand | method | ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SetSentErrorMessage, Log.Main/Out, ScriptMgr/IsScriptScheduled, ScriptMgr/LoadEventScripts | — | — |
| HandleReloadEventAIEventsCommand | method | ChatHandler.Chat/SendSysMessage, CreatureEventAIMgr/ClearEventData, CreatureEventAIMgr/LoadCreatureEventAI_Events, Log.Main/Out, ScriptMgr/LoadCreatureEventAIScripts | — | — |
| HandleReloadQuestEndScriptsCommand | method | ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SetSentErrorMessage, Log.Main/Out, ScriptMgr/IsScriptScheduled, ScriptMgr/LoadQuestEndScripts | — | — |
| HandleReloadQuestStartScriptsCommand | method | ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SetSentErrorMessage, Log.Main/Out, ScriptMgr/IsScriptScheduled, ScriptMgr/LoadQuestStartScripts | — | — |
| HandleReloadCreatureSpellScriptsCommand | method | ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SetSentErrorMessage, Log.Main/Out, ScriptMgr/IsScriptScheduled, ScriptMgr/LoadCreatureSpellScripts | — | — |
| HandleReloadSpellScriptsCommand | method | ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SetSentErrorMessage, Log.Main/Out, ScriptMgr/IsScriptScheduled, ScriptMgr/LoadSpellScripts | — | — |
| HandleReloadGameGraveyardZoneCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, ObjectMgr/LoadGraveyardZones | — | — |
| HandleReloadGameTeleCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, ObjectMgr/LoadGameTele | — | — |
| HandleReloadTaxiPathTransitionsCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, ObjectMgr/LoadTaxiPathTransitions | — | — |
| HandleReloadLocalesCreatureCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, ObjectMgr/LoadCreatureLocales | — | — |
| HandleReloadLocalesGameobjectCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, ObjectMgr/LoadGameObjectLocales | — | — |
| HandleReloadLocalesGossipMenuOptionCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, ObjectMgr/LoadGossipMenuItemsLocales | — | — |
| HandleReloadLocalesItemCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, ObjectMgr/LoadItemLocales | — | — |
| HandleReloadLocalesPageTextCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, ObjectMgr/LoadPageTextLocales | — | — |
| HandleReloadLocalesPointsOfInterestCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, ObjectMgr/LoadPointOfInterestLocales | — | — |
| HandleReloadLocalesQuestCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, ObjectMgr/LoadQuestLocales | — | — |
| HandleReloadCharacterPetCommand | method | CharacterDatabaseCache/instance, CharacterDatabaseCache/LoadAll, ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/PSendSysMessage | — | — |
| HandleReloadCreatureCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, ObjectMgr/LoadCreatures | — | — |
| HandleReloadGameObjectCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, ObjectMgr/LoadGameobjects | — | — |
| HandleReloadItemTemplate | method | ChatHandler.Chat/SendSysMessage, ObjectMgr/LoadItemPrototypes | — | — |
| HandleReloadMapTemplate | method | ChatHandler.Chat/SendSysMessage, ObjectMgr/LoadMapTemplate | — | — |
| HandleReloadCreatureTemplatesCommand | method | ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, Log.Main/Out, ObjectMgr/LoadCreatureTemplate, ObjectMgr/LoadCreatureTemplates | — | — |
| HandleReloadGameObjectTemplatesCommand | method | ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, Log.Main/Out, ObjectMgr/LoadGameObjectTemplate, ObjectMgr/LoadGameObjectTemplates | — | — |
| HandleReloadExplorationBaseXp | method | ChatHandler.Chat/SendSysMessage, ObjectMgr/LoadExplorationBaseXP | — | — |
| HandleReloadPetNameGeneration | method | ChatHandler.Chat/SendSysMessage, ObjectMgr/LoadPetNames | — | — |
| HandleReloadCreatureOnKillReputation | method | ChatHandler.Chat/SendSysMessage, ObjectMgr/LoadReputationOnKill | — | — |
| HandleReloadGameWeather | method | ChatHandler.Chat/SendSysMessage, Weather/LoadWeatherZoneChances | — | — |
| HandleReloadFactionChangeReputations | method | ChatHandler.Chat/SendSysMessage, ObjectMgr/LoadFactionChangeReputations | — | — |
| HandleReloadFactionChangeSpells | method | ChatHandler.Chat/SendSysMessage, ObjectMgr/LoadFactionChangeSpells | — | — |
| HandleReloadFactionChangeItems | method | ChatHandler.Chat/SendSysMessage, ObjectMgr/LoadFactionChangeItems | — | — |
| HandleReloadFactionChangeQuests | method | ChatHandler.Chat/SendSysMessage, ObjectMgr/LoadFactionChangeQuests | — | — |
| HandleReloadFactionChangeMounts | method | ChatHandler.Chat/SendSysMessage, ObjectMgr/LoadFactionChangeMounts | — | — |
| HandleReloadCreatureDisplayInfoAddon | method | ChatHandler.Chat/SendSysMessage, ObjectMgr/LoadCreatureDisplayInfoAddon | — | — |
| HandleReloadIPBanList | method | AccountMgr/LoadIPBanList, ChatHandler.Chat/SendSysMessage, Database/Query | — | ip_banned |
| HandleReloadAccountBanList | method | AccountMgr/LoadAccountBanList, ChatHandler.Chat/SendSysMessage | — | — |
| HandleReloadInstanceBuffRemoval | method | AuraRemovalMgr/LoadFromDB, ChatHandler.Chat/SendSysMessage | — | — |
| HandleReloadPetitions | method | ChatHandler.Chat/SendSysMessage, GuildMgr/LoadPetitions | — | — |
| HandleReloadVariablesCommand | method | ChatHandler.Chat/SendSysMessage, ObjectMgr/LoadSavedVariable | — | — |
| HandleReloadCreatureGroupsCommand | method | ChatHandler.Chat/SendSysMessage, CreatureGroups/Load, CreatureGroupsManager/instance | — | — |
| HandleReloadCinematicWaypointsCommand | method | ChatHandler.Chat/SendSysMessage, ObjectMgr/LoadCinematicsWaypoints | — | — |
| HandleReloadSpellDisabledCommand | method | ChatHandler.Chat/SendSysMessage, ObjectMgr/LoadSpellDisabledEntrys | — | — |
| HandleReloadAutoBroadcastCommand | method | AutoBroadCastMgr/Load, ChatHandler.Chat/SendSysMessage | — | — |
| HandleReloadSpellModsCommand | method | ChatHandler.Chat/SendSysMessage, SpellModMgr/LoadSpellMods | — | — |
| HandleReloadMapLootDisabledCommand | method | ChatHandler.Chat/SendSysMessage, ObjectMgr/LoadMapLootDisabled | — | — |
| HandleReloadConditionsCommand | method | ChatHandler.Chat/SendSysMessage, Log.Main/Out, ObjectMgr/LoadConditions | — | — |
| HandleReloadAnticheatCommand | method | Anticheat/GetAnticheatLib, Anticheat/LoadAnticheatData, ChatHandler.Chat/SendSysMessage | — | — |
| HandleListMapsCommand | method | ChatHandler.Chat/playerLink, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, Map.Main/GetCreateTime, Map.Main/GetMapName, Map.Main/GetPlayersCountExceptGMs, MapManager/Maps, shared_Util/secsToTimeString | — | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `ip_banned`: ip varchar(32) PK, bandate int(11), unbandate int(11), bannedby varchar(50), banreason varchar(50)
- `spell_group`: group_id int(11) unsigned PK, group_spell_id int(11) unsigned PK, spell_id smallint(5) unsigned PK, build_min smallint(4) unsigned, build_max smallint(4) unsigned

*`?` = nullable, `PK` = primary key column.*

## Tables with NO verified schema — column names/types unknown, do not guess

- `antispam_blacklist`
- `antispam_replacement`


---

<!-- verify: failed-members | missing: HandleListMapsCommand -->

---

<!-- verify: boundary-bleed | foreign: ChatHandler, update -->
