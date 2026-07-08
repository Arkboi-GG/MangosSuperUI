<!-- provenance: no-member-reference-section, boundary-bleed -->
# ChatHandler.CharacterCommands

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ChatHandler.CharacterCommands

## Purpose & Responsibilities

The `ChatHandler.CharacterCommands` partial of the `ChatHandler` class implements a comprehensive suite of administrative and debugging commands focused on **player character management**, **cheat toggles**, **stat modification**, and **database maintenance**. These commands are primarily used by Game Masters (GMs) and server administrators via in-game chat or console interfaces to manipulate player states, debug gameplay mechanics, manage deleted characters, and perform bulk operations on the character database.

Key responsibilities include:
1.  **Cheat & Debug Toggles:** Enabling/disabling specific gameplay cheats (fly, god mode, instant cast, etc.) for targeted players.
2.  **Character State Modification:** Directly altering levels, stats, reputation, honor, skills, spells, items, and appearance.
3.  **Character Lifecycle Management:** Handling the restoration, deletion, and cleanup of soft-deleted characters from the database.
4.  **Bulk Operations:** Mass-learning spells, resetting talents/stats for all players, and cleaning up orphaned database entries.
5.  **Information Retrieval:** Displaying detailed information about player AI, groups, quests, pets, and explored areas.

## Member-by-Member Behavior

### Cheat & Debug Toggles
These commands toggle specific boolean flags or movement states on a target player. Most follow a standard pattern: extract an on/off value, identify the target (defaulting to self if none selected), apply the change via `Player` or `Unit` methods, and notify both the issuer and the target.

*   **`HandleCheatFlyCommand`**: Enables/disables flight capability. Warns the player that jumping will disable the cheat.
*   **`HandleCheatFixedZCommand`**: Locks the player's Z-axis position, preventing vertical movement changes from terrain or gravity.
*   **`HandleCheatGodCommand`**: Grants invincibility (god mode).
*   **`HandleCheatCooldownCommand`**: Removes cooldowns from all abilities.
*   **`HandleCheatCastTimeCommand`**: Makes all casts instant.
*   **`HandleCheatPowerCommand`**: Removes resource costs (mana, energy, etc.) for abilities.
*   **`HandleCheatDebuffImmunityCommand`**: Prevents the player from receiving debuffs.
*   **`HandleCheatAlwaysCritCommand`**: Forces all attacks/spells to critically strike.
*   **`HandleCheatNoCastCheckCommand`**: Bypasses casting requirements (line of sight, range, etc.).
*   **`HandleCheatAlwaysProcCommand`**: Forces passive effects/procs to trigger on every eligible event.
*   **`HandleCheatTriggerPassCommand`**: Allows passing through area triggers.
*   **`HandleCheatIgnoreTriggersCommand`**: Ignores area triggers entirely.
*   **`HandleCheatBeastmasterCommand`**: Grants immunity to NPC attacks (sets `UNIT_FLAG_NOT_ATTACKABLE_1`).
*   **`HandleCheatImmuneToPlayersCommand`**: Sets `UNIT_FLAG_IMMUNE_TO_PLAYER`, making the player immune to other players' attacks.
*   **`HandleCheatImmuneToCreaturesCommand`**: Sets `UNIT_FLAG_IMMUNE_TO_NPC`, making the player immune to creature attacks.
*   **`HandleCheatUntargetableCommand`**: Sets `UNIT_FLAG_NOT_SELECTABLE`, making the player unselectable by others.
*   **`HandleCheatWaterwalkCommand`**: Enables water walking via `Unit::SetWaterWalking`. Requires security check.
*   **`HandleCheatWallclimbCommand`**: Enables wall climbing by setting `UNIT_FLAG_SERVER_CONTROLLED`. Requires security check.
*   **`HandleCheatDebugTargetInfoCommand`**: Enables debug info display for targeted units.
*   **`HandleCheatStatusCommand`**: Lists all currently active cheats on a target player by checking various `Player` and `Unit` flags.
*   **`HandleTaxiCheatCommand`**: Enables/disables access to all flight paths.
*   **`HandleExploreCheatCommand`**: Sets all explored zones bits to 0xFFFFFFFF (explored) or 0 (unexplored) for the *issuer's* player object, regardless of the selected target (notable bug/quirk: it modifies `m_session->GetPlayer()` instead of the target).
*   **`HandleHoverCommand`**: Enables/disables hovering for the issuer.
*   **`HandleWhisperRestrictionCommand`**: Toggles whisper restrictions for the issuer.
*   **`HandleWhispersCommand`**: Toggles whether the GM accepts whispers from players.

### Character State & Stat Modification
These commands directly alter numerical values or flags associated with a player's character data.

*   **`HandleModifyXpRateCommand`**: Sets the personal XP rate multiplier. Validates against configured min/max limits.
*   **`HandleModifyBlockCommand`**, **`HandleModifyDodgeCommand`**, **`HandleModifyParryCommand`**, **`HandleModifyMeleeCritCommand`**, **`HandleModifyRangedCritCommand`**: Set specific combat stat percentages. Validate range 0-100.
*   **`HandleModifySpellCritCommand`**: Sets spell critical chance for all schools.
*   **`HandleModifyGenderCommand`**: Changes the player's gender, updating display IDs and bytes.
*   **`HandleModifyDrunkCommand`**: Sets the player's drunkenness level.
*   **`HandleModifyExhaustionCommand`**: Sets exhaustion flags (partial/no play time).
*   **`HandleModifyRepCommand`**: Modifies reputation with a specific faction. Can take a raw amount or a rank string (e.g., "friendly").
*   **`HandleModifyMountCommand`**: Forces the player to mount a specific creature display ID.
*   **`HandleModifyMoneyCommand`**: Adds or removes gold. Logs the transaction.
*   **`HandleModifyTalentCommand`**: Sets the number of free talent points.
*   **`HandleModifySpeedCommand`**, **`HandleModifySwimCommand`**, **`HandleModifyBWalkCommand`**: Modify run, swim, and backward walk speeds. Caps speed at 4.0 for non-admins.
*   **`HandleModifyFlyCommand`**: Modifies flight path speed. Only works if the player is currently taxi flying.
*   **`HandleModifyEnergyCommand`**, **`HandleModifyRageCommand`**: Sets current and maximum energy/rage. Rage values are scaled by 10 internally.
*   **`HandleModifyHairStyleCommand`**, **`HandleModifyHairColorCommand`**, **`HandleModifySkinColorCommand`**, **`HandleModifyAccessoriesCommand`**: Updates cosmetic byte values and forces a display ID update using `DISPLAY_ID_BOX` (a placeholder ID) to ensure the client refreshes the model.

### Level, Skills, Spells, & Items
Commands for managing progression and inventory.

*   **`HandleLevelUpCommand`**: Increases the level of a player or creature. For players, it delegates to `HandleCharacterLevel`. For creatures, it updates stats directly.
*   **`HandleCharacterLevel`**: Helper that applies level changes. For online players, it calls `Player::GiveLevel` and resets XP. For offline players, it updates the `characters` table directly.
*   **`HandleCharacterLevelCommand`**: Parses arguments for `.character level` and delegates to `HandleCharacterLevel`.
*   **`HandleMaxSkillCommand`**: Sets all skills to their maximum value for the player's current level.
*   **`HandleSetSkillCommand`**: Sets a specific skill to a specific value.
*   **`HandleLearnCommand`**: Teaches a specific spell to a player. Supports "all" ranks.
*   **`HandleUnLearnCommand`**: Removes a specific spell. Supports "all" ranks.
*   **`HandleLearnAllCommand`**: Iterates through all spells in the database, teaching those that are learnable via spell effects, excluding talents and passives.
*   **`HandleLearnAllGMCommand`**: Teaches a hardcoded list of GM utility spells (`gmSpellList`).
*   **`HandleUnLearnAllGMCommand`**: Removes the hardcoded GM spells.
*   **`HandleLearnAllMyClassCommand`**: Delegates to `HandleLearnAllMySpellsCommand` and `HandleLearnAllMyTalentsCommand`.
*   **`HandleLearnAllMySpellsCommand`**: Teaches all spells fitting the player's class/race that are not talents.
*   **`HandleLearnAllMyTalentsCommand`**: Teaches the highest rank of all talents for the player's class.
*   **`HandleLearnAllTrainerCommand`**: Teaches all spells available from NPC trainers. Uses `HandleLearnTrainerHelper` to handle dependencies.
*   **`HandleLearnAllItemsCommand`**: Teaches all spells learned from items that the player can use.
*   **`HandleLearnAllMyTaxisCommand`**: Unlocks all flight nodes for the player's faction.
*   **`HandleLearnAllLangCommand`**: Teaches all languages.
*   **`HandleLearnAllDefaultCommand`**: Teaches default class/race spells and quest-rewarded spells.
*   **`HandleLearnAllCraftsCommand`**: Teaches all profession and secondary skill recipes. Uses `HandleLearnSkillRecipesHelper`.
*   **`HandleUnLearnAllCraftsCommand`**: Removes all profession/secondary skills, then re-teaches default spells.
*   **`HandleLearnAllRecipesCommand`**: Teaches all recipes for a specified profession and sets the skill to max.
*   **`HandleUnLearnAllRecipesCommand`**: Removes all recipes for a specified profession.
*   **`HandleAddItemCommand`**: Adds an item to a player's inventory. Handles negative counts as removal. Checks space and binds/unbinds appropriately.
*   **`HandleDeleteItemCommand`**: Removes items from a player's inventory. For offline players, it performs direct SQL deletions from `item_instance`, `character_inventory`, `character_gifts`, and `mail_items`.
*   **`HandleAddItemSetCommand`**: Adds all items belonging to a specific item set to the player's inventory.
*   **`HandleListItemCommand`**: Searches for an item across `character_inventory`, `mail_items`, and `auction` tables, reporting locations and owners.
*   **`HandleItemMoveCommand`**: Swaps items between two inventory slots for the issuer.

### Area Exploration
*   **`HandleShowAreaCommand`**: Marks a specific area ID as explored for the selected player by setting the corresponding bit in the `PLAYER_EXPLORED_ZONES` fields.
*   **`HandleHideAreaCommand`**: Unmarks a specific area ID as explored for the selected player by clearing the corresponding bit in the `PLAYER_EXPLORED_ZONES` fields.

### Quests, Pets, & Groups
Management of auxiliary character systems.

*   **`HandleQuestAddCommand`**: Adds a quest to a player. Checks if it starts from an item (and fails if so). Attempts to complete it immediately if criteria are met.
*   **`HandleQuestRemoveCommand`**: Removes a quest and resets its status.
*   **`HandleQuestStatusCommand`**: Displays the status of a quest and its chain predecessors. Uses `HandleQuestStatusCommandHelper` to fetch data from memory or DB.
*   **`HandleQuestCompleteCommand`**: Forces completion of a quest.
*   **`HandlePetLearnSpellCommand`**, **`HandlePetUnlearnSpellCommand`**: Manages pet spellbook.
*   **`HandlePetListCommand`**: Lists all pets for a character from the cache.
*   **`HandlePetRenameCommand`**: Renames a pet in the `character_pet` table and cache.
*   **`HandlePetDeleteCommand`**: Deletes a pet from the `character_pet` table and cache.
*   **`HandlePetLoyaltyCommand`**: Modifies hunter pet loyalty points.
*   **`HandlePetInfoCommand`**: Displays detailed pet statistics.
*   **`HandleGroupInfoCommand`**: Displays group composition, leader, and type.
*   **`HandleGroupAddItemCommand`**: Adds an item to all group members.
*   **`HandleGroupReviveCommand`**: Resurrects all dead group members.
*   **`HandleGroupReplenishCommand`**: Restores health and mana to all alive group members.
*   **`HandleGroupSummonCommand`**: Sends summon requests to all group members.

### Honor & Reputation
*   **`HandleHonorShow`**: Displays detailed honor statistics (ranks, kills, points) for a player.
*   **`HandleHonorAddCommand`**: Adds honor points.
*   **`HandleHonorAddKillCommand`**: Awards honor for killing a targeted unit.
*   **`HandleModifyHonorCommand`**: Modifies specific honor fields (points, rank, kill counts) by name abbreviation.
*   **`HandleHonorResetCommand`**: Resets honor manager state.
*   **`HandleHonorSetRPCommand`**: Sets raw rank points.
*   **`HandleCharacterReputationCommand`**: Lists all faction reputations for a player.
*   **`HandleResetHonorCommand`**: Resets the honor manager for a target player, clearing their honor points and rank.

### Talents & Skills Helpers
*   **`HandleListTalentsCommand`**: Lists all talents currently learned by a selected player, including their point cost.
*   **`FindSkillLineEntryFromProfessionName`**: Helper function that searches the DBC for a skill line entry matching a partial profession name string, handling locale conversions.
*   **`HandleUnLearnSkillRecipesHelper`**: Helper function that iterates through skill line abilities for a given skill ID and removes the associated spells from a player.

### Character Lifecycle & Database Maintenance
Commands for handling deleted characters and cleaning up the database.

*   **`GetDeletedCharacterInfoList`**: Queries the `characters` table for rows where `deleted_time` is not null. Supports filtering by GUID, name, account ID, or account name. Populates a `DeletedInfoList`.
*   **`GenerateDeletedCharacterGUIDsWhereStr`**: Helper to generate SQL `IN (...)` clauses for GUID lists, breaking them up to avoid query length limits.
*   **`HandleCharacterDeletedListHelper`**: Formats and prints the list of deleted characters.
*   **`HandleCharacterDeletedListNameCommand`**, **`HandleCharacterDeletedListAccountCommand`**, **`HandleCharacterDeletedListCommand`**: Entry points for listing deleted characters.
*   **`HandleCharacterDeletedRestoreHelper`**: Restores a single deleted character by updating the `characters` table (clearing delete flags) and reloading cache. Checks for account existence, character slot availability, and name conflicts.
*   **`HandleCharacterDeletedRestoreCommand`**: Restores one or more deleted characters based on search criteria. Supports renaming and reassigning accounts during restore.
*   **`HandleCharacterDeletedDeleteCommand`**: Permanently deletes characters marked as deleted from the database.
*   **`HandleCharacterDeletedOldCommand`**: Deletes characters marked as deleted older than a specified number of days.
*   **`HandleCharacterEraseCommand`**: Immediately deletes a character (online or offline), kicking them if online.
*   **`HandleCleanCharactersToDeleteCommand`**: Processes the `characters_guid_delete` queue, permanently deleting listed GUIDs.
*   **`HandleCleanCharactersItemsCommand`**: Processes the `characters_item_delete` queue. If run from console (`SEC_CONSOLE`), it deletes the items from `item_instance` and related tables. Otherwise, it only reports counts.
*   **`HandleServiceDeleteCharacters`**: Bulk deletes characters based on complex criteria (banned accounts, GMs, low level/money/items/playtime). Constructs dynamic SQL queries.
*   **`HandleGoldRemoval`**: Removes a specific amount of gold (g/s/c format) from a player. Uses regex for parsing.

### Appearance & Miscellaneous
*   **`HandleCharacterChangeRaceCommand`**: Changes a player's race.
*   **`HandleCharacterCopySkinCommand`**: Copies appearance bytes (skin, face, hair, etc.) from one character to another by querying the `characters` table.
*   **`HandleCharacterFillFlysCommand`**: Unlocks all flight paths for the player's faction by setting hardcoded taxi masks.
*   **`HandleCharacterCityTitleCommand`**: Toggles city titles.
*   **`HandleCharacterPremadeGearCommand`**, **`HandleCharacterPremadeSaveGearCommand`**: Applies or saves gear templates from `player_premade_item` and `player_premade_item_template` tables.
*   **`HandleCharacterPremadeSpecCommand`**, **`HandleCharacterPremadeSaveSpecCommand`**: Applies or saves talent/spec templates from `player_premade_spell` and `player_premade_spell_template` tables.
*   **`HandleCharacterRenameCommand`**: Sets the rename flag on a character, triggering a rename prompt on login.
*   **`HandleCharacterHasItemCommand`**: Checks if a player has a specific item, querying the DB for offline players.
*   **`HandlePDumpLoadCommand`**, **`HandlePDumpWriteCommand`**: Imports/exports character data to/from dump files.
*   **`HandleReviveCommand`**: Resurrects a player (online or offline). For offline players, it converts the corpse via `ObjectAccessor`.
*   **`HandleMountCommand`**, **`HandleDismountCommand`**: Mounts/dismounts the issuer. Mounting copies the display ID of a targeted creature.
*   **`HandleSaveCommand`**: Forces a player save to the database.
*   **`HandleRepairitemsCommand`**: Repairs all items for a target.
*   **`HandleCombatStopCommand`**: Stops combat and clears hostile references for a target.
*   **`HandleListExploredAreasCommand`**: Lists all areas explored by a player.
*   **`HandleListVisibleGuidsCommand`**: Lists all GUIDs visible to a player.
*   **`HandleChannelJoinCommand`**, **`HandleChannelLeaveCommand`**: Joins/leaves a chat channel by constructing packets and calling session handlers.
*   **`HandleResetLevelCommand`**, **`HandleResetStatsCommand`**, **`HandleResetSpellsCommand`**, **`HandleResetTalentsCommand`**, **`HandleResetItemsCommand`**: Various reset commands for player progression. `HandleResetStatsOrLevelHelper` is a static helper that resets base unit fields.
*   **`HandleResetAllCommand`**: Sets the talent reset flag for all online players and updates the DB for offline players.
*   **`EscapeString`**: Static helper function that escapes a string for safe insertion into MySQL queries using `mysql_escape_string`.
*   **`HandleCharacterAIInfoCommand`**: Displays the AI class name and current movement generator type for a selected player. It retrieves the AI object via `Player::AI` and uses `typeid` to get the class name, then queries `Unit::GetMotionMaster` for movement details.

## Cross-Unit Boundaries

*   **`ChatHandler.Chat`**: Extensively used for argument parsing (`Extract...`), messaging (`SendSysMessage`, `PSendSysMessage`), and target selection (`GetSelectedPlayer`, `ExtractPlayerTarget`).
*   **`Player.Main`**: The primary collaborator. Almost every command modifies player state via methods like `SetCheat...`, `LearnSpell`, `RemoveSpell`, `SetLevel`, `SetMoney`, `GetReputationMgr`, etc.
*   **`Unit.Main`**: Used for movement-related cheats (`SetWaterWalking`, `UpdateSpeed`, `GetMotionMaster`) and general unit flags.
*   **`Creature.Main`**: Used in `HandleLevelUpCommand` to modify creature levels and stats.
*   **`Pet.Main`**: Used for pet-specific commands (`LearnSpell`, `UnlearnSpell`, `ModifyLoyalty`).
*   **`Group`**: Used in group commands to iterate members (`GetFirstMember`, `next`) and get group info.
*   **`ObjectAccessor`**: Used in `HandleReviveCommand` to convert corpses for offline players.
*   **`ObjectMgr`**: Used for looking up data (names, factions, items, quests, trainer spells) and managing caches.
*   **`SpellMgr`**: Used for validating spells, getting spell entries, and finding spell chains.
*   **`Database`**: Direct SQL execution (`PQuery`, `PExecute`, `DirectPExecute`) is used extensively for offline character modifications, deleted character management, and item cleanup.
*   **`AccountMgr`**: Used for account name lookups and character counts.
*   **`World`**: Used for configuration values (`getConfig`) and sending world-wide messages.
*   **`WorldSession.Main`**: Used to get the issuing player/session and check security levels.
*   **`AsyncCommandHandlers`**: `HandlePInfoCommand` delegates to `AsyncCommandHandlers::HandlePInfoCommand`.
*   **`CombatBotBaseAI`**: `HandleLearnAllTrainerCommand` and `HandleLearnAllItemsCommand` are called by `CombatBotBaseAI::LearnPremadeSpecForClass` (as indicated in the MAP's "Called by" column).
*   **`Creature.MotionMaster`**: Used in `HandleCharacterAIInfoCommand` to retrieve the current movement generator type and its name.

## Data Model

This unit interacts with several database tables, primarily for offline character manipulation and maintenance tasks.

*   **`characters`**:
    *   Used for: Reading/writing character levels, XP, flags (rename, talent reset), deleted character info (`deleted_time`, `deleted_name`, `deleted_account`), and appearance bytes (via `HandleCharacterCopySkinCommand`).
    *   Columns accessed: `guid`, `level`, `xp`, `character_flags`, `deleted_time`, `deleted_name`, `deleted_account`, `skin`, `face`, `hair_style`, `hair_color`, `facial_hair`, `gender`, `name`, `account`, `money`, `played_time_total`, `logout_time`.
*   **`character_skills`**:
    *   Used for: Removing riding skills (`HandleRemoveRidingCommand`).
    *   Columns accessed: `skill`, `guid`, `value`, `max`.
*   **`character_spell`**:
    *   Used for: Removing riding spells (`HandleRemoveRidingCommand`) and saving premade specs (`HandleCharacterPremadeSaveSpecCommand`).
    *   Columns accessed: `spell`, `guid`, `active`, `disabled`.
*   **`character_queststatus`**:
    *   Used for: Checking quest status for offline players (`HandleQuestStatusCommandHelper`).
    *   Columns accessed: `status`, `rewarded`, `reward_choice`, `guid`, `quest`.
*   **`character_pet`**:
    *   Used for: Renaming and deleting pets (`HandlePetRenameCommand`, `HandlePetDeleteCommand`).
    *   Columns accessed: `id`, `owner_guid`, `name`.
*   **`item_instance`**:
    *   Used for: Deleting items for offline players (`HandleDeleteItemCommand`) and cleaning up items (`HandleCleanCharactersItemsCommand`).
    *   Columns accessed: `guid`, `item_id`, `owner_guid`, `count`.
*   **`character_inventory`**:
    *   Used for: Deleting items for offline players (`HandleDeleteItemCommand`) and listing items (`HandleListItemCommand`).
    *   Columns accessed: `item_guid`, `item_id`, `guid`, `bag`, `slot`.
*   **`character_gifts`**:
    *   Used for: Deleting gifted items for offline players (`HandleDeleteItemCommand`).
    *   Columns accessed: `item_guid`.
*   **`mail_items`**:
    *   Used for: Deleting mailed items for offline players (`HandleDeleteItemCommand`) and listing items (`HandleListItemCommand`).
    *   Columns accessed: `item_guid`, `item_id`, `mail_id`.
*   **`mail`**:
    *   Used for: Listing items in mail (`HandleListItemCommand`).
    *   Columns accessed: `id`, `sender_guid`, `receiver_guid`.
*   **`auction`**:
    *   Used for: Listing items in auctions (`HandleListItemCommand`).
    *   Columns accessed: `item_guid`, `item_id`, `seller_guid`.
*   **`characters_guid_delete`**:
    *   Used for: Processing the queue of characters to be permanently deleted (`HandleCleanCharactersToDeleteCommand`).
    *   Columns accessed: `guid`.
*   **`characters_item_delete`**:
    *   Used for: Processing the queue of items to be deleted (`HandleCleanCharactersItemsCommand`).
    *   Columns accessed: `entry`.
*   **`player_premade_item_template`**:
    *   Used for: Saving/loading premade gear templates (`HandleCharacterPremadeSaveGearCommand`, `HandleCharacterPremadeGearCommand`).
    *   Columns accessed: `entry`, `class`, `level`, `name`.
*   **`player_premade_item`**:
    *   Used for: Saving/loading premade gear templates.
    *   Columns accessed: `entry`, `item`, `enchant`.
*   **`player_premade_spell_template`**:
    *   Used for: Saving/loading premade spec templates (`HandleCharacterPremadeSaveSpecCommand`, `HandleCharacterPremadeSpecCommand`).
    *   Columns accessed: `entry`, `class`, `level`, `name`.
*   **`player_premade_spell`**:
    *   Used for: Saving/loading premade spec templates.
    *   Columns accessed: `entry`, `spell`.
*   **`account`**:
    *   Used for: Looking up account IDs from names (`GetDeletedCharacterInfoList`) and checking GM levels (`HandleServiceDeleteCharacters`).
    *   Columns accessed: `id`, `username`, `gmlevel`.
*   **`account_banned`**:
    *   Used for: Identifying banned accounts for deletion (`HandleServiceDeleteCharacters`).
    *   Columns accessed: `id`, `bandate`, `unbandate`, `active`.

## Notable Implementation Details

1.  **Offline vs. Online Handling**: Many commands distinguish between online and offline targets. Online targets are modified via C++ objects (`Player*`), while offline targets require direct SQL queries (`CharacterDatabase.PQuery`/`PExecute`). This is evident in `HandleCharacterLevel`, `HandleDeleteItemCommand`, `HandleQuestStatusCommand`, and `HandleCharacterHasItemCommand`.
2.  **Security Checks**: Commands that affect other players often call `HasLowerSecurity` to ensure the issuer has sufficient privileges. This is seen in `HandleCheatWaterwalkCommand`, `HandleCheatWallclimbCommand`, `HandleModifyMoneyCommand`, etc.
3.  **Notification Pattern**: Most cheat/modification commands notify both the issuer (`PSendSysMessage`) and the target (`target->PSendSysMessage`) if the target is online and different from the issuer (`needReportToTarget`).
4.  **Hardcoded Values**: Several commands rely on hardcoded values, such as `gmSpellList` in `HandleLearnAllGMCommand`, taxi masks in `HandleCharacterFillFlysCommand`, and riding skill IDs in `HandleRemoveRidingCommand`.
5.  **Bug/Quirk in `HandleExplore

---

<!-- machine-true, projected from graph.json -->

## Map — ChatHandler.CharacterCommands

*Source:* CharacterCommands.cpp, Chat.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| HandleCharacterAIInfoCommand | method | ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Creature.MotionMaster/GetCurrentMovementGeneratorType, Creature.MotionMaster/GetMovementGeneratorTypeName, Object/GetObjectGuid, ObjectGuid/GetString, Player.Main/AI, Unit.Main/GetMotionMaster | ChatHandler.UnitCommands/HandleUnitAIInfoCommand | — |
| HandleModifyXpRateCommand | method | ChatHandler.Chat/ExtractFloat, ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/GetName, Player.Main/SetPersonalXpRate, World/getConfig#2, WorldSession.Main/GetPlayer, WorldSession.Main/GetSecurity | — | — |
| HandleCheatFlyCommand | method | ChatHandler.Chat/ExtractOnOff, ChatHandler.Chat/GetNameLink, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/PSendSysMessage#2, Player.Main/SendSysMessage, Player.Main/SetCheatFly, WorldSession.Main/GetPlayer | — | — |
| HandleCheatFixedZCommand | method | ChatHandler.Chat/ExtractOnOff, ChatHandler.Chat/GetNameLink, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/PSendSysMessage#2, Player.Main/SetCheatFixedZ, WorldSession.Main/GetPlayer | — | — |
| HandleCheatGodCommand | method | ChatHandler.Chat/ExtractOnOff, ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/GetNameLink, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/PSendSysMessage#2, Player.Main/SetCheatGod | — | — |
| HandleCheatCooldownCommand | method | ChatHandler.Chat/ExtractOnOff, ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/GetNameLink, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/PSendSysMessage#2, Player.Main/SetCheatNoCooldown | — | — |
| HandleCheatCastTimeCommand | method | ChatHandler.Chat/ExtractOnOff, ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/GetNameLink, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/PSendSysMessage#2, Player.Main/SetCheatInstantCast | — | — |
| HandleCheatPowerCommand | method | ChatHandler.Chat/ExtractOnOff, ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/GetNameLink, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/PSendSysMessage#2, Player.Main/SetCheatNoPowerCost | — | — |
| HandleCheatDebuffImmunityCommand | method | ChatHandler.Chat/ExtractOnOff, ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/GetNameLink, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/PSendSysMessage#2, Player.Main/SetCheatDebuffImmunity | — | — |
| HandleCheatAlwaysCritCommand | method | ChatHandler.Chat/ExtractOnOff, ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/GetNameLink, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/PSendSysMessage#2, Player.Main/SetCheatAlwaysCrit | — | — |
| HandleCheatNoCastCheckCommand | method | ChatHandler.Chat/ExtractOnOff, ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/GetNameLink, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/PSendSysMessage#2, Player.Main/SetCheatNoCastCheck | — | — |
| HandleCheatAlwaysProcCommand | method | ChatHandler.Chat/ExtractOnOff, ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/GetNameLink, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/PSendSysMessage#2, Player.Main/SetCheatAlwaysProc | — | — |
| HandleCheatTriggerPassCommand | method | ChatHandler.Chat/ExtractOnOff, ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/GetNameLink, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/PSendSysMessage#2, Player.Main/SetCheatTriggerPass | — | — |
| HandleCheatIgnoreTriggersCommand | method | ChatHandler.Chat/ExtractOnOff, ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/GetNameLink, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/PSendSysMessage#2, Player.Main/SetCheatIgnoreTriggers | — | — |
| HandleCheatBeastmasterCommand | method | ChatHandler.Chat/ExtractOnOff, ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/GetNameLink, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/PSendSysMessage#2, Player.Main/SetCheatBeastmaster | — | — |
| HandleCheatImmuneToPlayersCommand | method | ChatHandler.Chat/ExtractOnOff, ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/GetNameLink, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/PSendSysMessage#2, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag | — | — |
| HandleCheatImmuneToCreaturesCommand | method | ChatHandler.Chat/ExtractOnOff, ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/GetNameLink, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/PSendSysMessage#2, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag | — | — |
| HandleCheatUntargetableCommand | method | ChatHandler.Chat/ExtractOnOff, ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/GetNameLink, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/PSendSysMessage#2, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag | — | — |
| HandleCheatWaterwalkCommand | method | ChatHandler.Chat/ExtractOnOff, ChatHandler.Chat/GetNameLink, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/HasLowerSecurity, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/PSendSysMessage#2, Unit.Main/SetWaterWalking | — | — |
| HandleCheatWallclimbCommand | method | ChatHandler.Chat/ExtractOnOff, ChatHandler.Chat/GetNameLink, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/HasLowerSecurity, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/PSendSysMessage#2, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag | — | — |
| HandleCheatDebugTargetInfoCommand | method | ChatHandler.Chat/ExtractOnOff, ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/GetNameLink, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/PSendSysMessage#2, Player.Main/SetCheatDebugTargetInfo | — | — |
| HandleCheatStatusCommand | method | ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, Object/HasFlag, Player.Main/GetName, Player.Main/HasCheatOption, Player.Main/HasMovementFlag, Unit.Main/GetInvincibilityHpThreshold, Unit.Main/HasAuraType | — | — |
| HandleReviveCommand | method | ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/playerLink, ChatHandler.Chat/PSendSysMessage#2, ObjectAccessor/ConvertCorpseForPlayer, ObjectGuid/ObjectGuid, ObjectMgr/GetPlayerNameByGUID, Player.Main/GetName, Player.Main/ResurrectPlayer, Player.Main/SpawnCorpseBones | — | — |
| HandleExploreCheatCommand | method | ChatHandler.Chat/GetNameLink, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/PSendSysMessage#2, WorldObject.Object/SetFlag, WorldSession.Main/GetPlayer | — | — |
| HandleHoverCommand | method | ChatHandler.Chat/ExtractOptUInt32, ChatHandler.Chat/SendSysMessage#2, Unit.Main/SetHover, WorldSession.Main/GetPlayer | — | — |
| HandleLevelUpCommand | method | ChatHandler.Chat/ExtractArg, ChatHandler.Chat/ExtractInt32, ChatHandler.Chat/ExtractOptNotLastArg, ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/GetSelectedCreature, ChatHandler.Chat/playerLink, ChatHandler.Chat/PSendSysMessage#2, Creature.Main/GetName, Creature.Main/InitStatsForLevel, Creature.Main/IsPet, ObjectGuid/ObjectGuid, Pet.Main/GivePetLevel, Player.Main/GetLevelFromDB, Player.StatSystem/UpdateAllStats, Unit.Main/GetLevel, Unit.Main/SetLevel, WorldSession.Main/GetPlayer | — | — |
| HandleShowAreaCommand | method | AreaEntry/GetFlagById, ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Object/GetUInt32Value, WorldObject.Object/SetUInt32Value | — | — |
| HandleHideAreaCommand | method | AreaEntry/GetFlagById, ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Object/GetUInt32Value, WorldObject.Object/SetUInt32Value | — | — |
| HandleMaxSkillCommand | method | ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/UpdateSkillsToMaxSkillsForLevel | — | — |
| HandleSetSkillCommand | method | ChatHandler.Chat/ExtractInt32, ChatHandler.Chat/ExtractKeyFromLink#2, ChatHandler.Chat/ExtractOptInt32, ChatHandler.Chat/GetNameLink, ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/GetSessionDbcLocale, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/GetSkillMaxPure, Player.Main/GetSkillValue, Player.Main/SetSkill | — | — |
| HandleRemoveRidingCommand | method | ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Database/PExecute#2, Database/PQuery, ObjectGuid/GetCounter, ObjectGuid/ObjectGuid, Player.Main/RemoveSpell, Player.Main/SaveToDB | — | character_skills, character_spell |
| HandleUnLearnCommand | method | ChatHandler.Chat/ExtractLiteralArg, ChatHandler.Chat/ExtractSpellIdFromLink, ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/HasSpell, Player.Main/RemoveSpell, SpellMgr/GetFirstSpellInChain, SpellMgr/Instance | — | — |
| HandleGroupInfoCommand | method | ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/GetNameLink, ChatHandler.Chat/playerLink, ChatHandler.Chat/PSendSysMessage#2, Group/GetFirstMember, Group/GetId, Group/GetLeaderName, Group/GetMembersCount, Group/isRaidGroup, GroupReference/next, ObjectGuid/ObjectGuid, Player.Main/GetGroup, Player.Main/GetName | — | — |
| HandlePInfoCommand | method | AsyncCommandHandlers/HandlePInfoCommand, ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/HasLowerSecurity, ObjectGuid/ObjectGuid | — | — |
| HandleMountCommand | method | ChatHandler.Chat/GetSelectedCreature, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Object/GetUInt32Value, Player.Main/Unmount, Unit.Main/IsTaxiFlying, Unit.Main/UpdateSpeed, WorldObject.Object/SetUInt32Value, WorldSession.Main/GetPlayer | — | — |
| HandleDismountCommand | method | ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/Unmount, Unit.Main/IsMounted, Unit.Main/IsTaxiFlying, Unit.Main/RemoveSpellsCausingAura, WorldSession.Main/GetPlayer | — | — |
| HandleSaveCommand | method | ChatHandler.Chat/GetAccessLevel, ChatHandler.Chat/SendSysMessage#2, Player.Main/GetSaveTimer, Player.Main/SaveToDB, World/getConfig#4, WorldSession.Main/GetPlayer | — | — |
| HandleWhisperRestrictionCommand | method | ChatHandler.Chat/ExtractOnOff, ChatHandler.Chat/GetSession, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/IsEnabledWhisperRestriction, Player.Main/SetWhisperRestriction, WorldSession.Main/GetPlayer | — | — |
| HandleWhispersCommand | method | ChatHandler.Chat/ExtractOnOff, ChatHandler.Chat/GetOnOffStr, ChatHandler.Chat/GetSession, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, MasterPlayer.Main/ClearAllowedWhisperers, MasterPlayer.Main/IsAcceptWhispers, Player.Main/SetAcceptWhispers, WorldSession.Main/GetMasterPlayer, WorldSession.Main/GetPlayer | — | — |
| HandleTaxiCheatCommand | method | ChatHandler.Chat/ExtractOnOff, ChatHandler.Chat/GetNameLink, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/HasLowerSecurity, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/PSendSysMessage#2, Player.Main/SetTaxiCheater, WorldSession.Main/GetPlayer | — | — |
| GetDeletedCharacterInfoList | method | AccountMgr/GetName, AccountMgr/normalizeString, Database/escape_string, Database/PQuery, Database/Query, Field/GetCppString, Field/GetUInt32, Field/GetUInt64, ObjectMgr/normalizePlayerName, QueryResult/Fetch, QueryResult/NextRow, shared_Util/isNumeric | — | account, characters |
| GenerateDeletedCharacterGUIDsWhereStr | method | — | — | — |
| HandleCharacterDeletedListHelper | method | ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, shared_Util/TimeToTimestampStr | — | — |
| HandleCharacterDeletedListNameCommand | method | — | — | — |
| HandleCharacterDeletedListAccountCommand | method | — | — | — |
| HandleCharacterDeletedListCommand | method | ChatHandler.Chat/SendSysMessage#2 | — | — |
| HandleCharacterDeletedRestoreHelper | method | AccountMgr/GetCharactersCount, ChatHandler.Chat/PSendSysMessage#2, Database/DirectPExecute, ObjectMgr/GetPlayerGuidByName, ObjectMgr/LoadPlayerCacheData | — | characters |
| HandleCharacterDeletedRestoreCommand | method | AccountMgr/GetName, ChatHandler.Chat/SendSysMessage#2, ObjectMgr/normalizePlayerName | — | — |
| HandleCharacterDeletedDeleteCommand | method | ChatHandler.Chat/SendSysMessage#2, ObjectGuid/ObjectGuid#2, Player.Main/DeleteFromDB | — | — |
| HandleCharacterDeletedOldCommand | method | ChatHandler.Chat/ExtractOptInt32, Player.Main/DeleteOldCharacters#2, World/getConfig#4 | — | — |
| HandleCharacterEraseCommand | method | AccountMgr/GetName, ChatHandler.Chat/ExtractLiteralArg, ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/PSendSysMessage#2, ObjectGuid/GetCounter, ObjectGuid/ObjectGuid, ObjectMgr/GetPlayerAccountIdByGUID, Player.Main/DeleteFromDB, Player.Main/GetSession, WorldSession.Main/GetAccountId, WorldSession.Main/KickPlayer | — | — |
| HandleCleanCharactersToDeleteCommand | method | ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SetSentErrorMessage, Database/Query, Field/GetUInt32, ObjectGuid/ObjectGuid#5, Player.Main/DeleteFromDB, QueryResult/Fetch, QueryResult/NextRow | — | characters_guid_delete |
| HandleCleanCharactersItemsCommand | method | ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SetSentErrorMessage, Database/BeginTransaction, Database/CommitTransaction, Database/Query, Field/GetUInt32, game_Objects_Item/DeleteAllFromDB#2, ObjectGuid/ObjectGuid#5, ObjectMgr/GetPlayer, Player.Main/DestroyItemCount#2, QueryResult/Fetch, QueryResult/NextRow, WorldSession.Main/GetSecurity | — | characters_item_delete, item_instance |
| HandleCharacterLevel | method | ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/needReportToTarget, Database/PExecute#2, ObjectGuid/GetCounter, Player.Main/GiveLevel, Player.Main/InitTalentForLevel, Player.Main/PSendSysMessage#2, WorldObject.Object/SetUInt32Value | — | characters |
| HandleCharacterLevelCommand | method | ChatHandler.Chat/ExtractArg, ChatHandler.Chat/ExtractInt32, ChatHandler.Chat/ExtractOptNotLastArg, ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/playerLink, ChatHandler.Chat/PSendSysMessage#2, ObjectGuid/ObjectGuid, Player.Main/GetLevelFromDB, Unit.Main/GetLevel, WorldSession.Main/GetPlayer | — | — |
| HandleCharacterRenameCommand | method | ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/GetNameLink, ChatHandler.Chat/HasLowerSecurity, ChatHandler.Chat/playerLink, ChatHandler.Chat/PSendSysMessage#2, Database/PExecute#2, Object/GetGUIDLow, ObjectGuid/GetCounter, ObjectGuid/ObjectGuid, Player.Main/SetCharacterFlag | — | characters |
| HandleCharacterReputationCommand | method | ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/GetSessionDbcLocale, ChatHandler.LookupCommands/ShowFactionListHelper, ObjectMgr/GetFactionEntry, Player.Main/GetReputationMgr, ReputationMgr/GetStateList | — | — |
| HandleCharacterHasItemCommand | method | ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/GetItemLink, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SetSentErrorMessage, Database/PQuery, Field/GetUInt32, ObjectGuid/GetCounter, ObjectGuid/ObjectGuid, ObjectMgr/GetItemPrototype, Player.Main/GetItemCount, QueryResult/Fetch | — | item_instance |
| HandleCharacterPremadeGearCommand | method | ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SendSysMessage#2, ObjectMgr/ApplyPremadeGearTemplateToPlayer, ObjectMgr/GetPlayerPremadeGearTemplates, Player.Main/GetName, Unit.Main/GetClass | — | — |
| EscapeString | function | — | — | — |
| HandleCharacterPremadeSaveGearCommand | method | ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, Database/DirectPExecute, Database/PExecute#2, Database/Query, Field/GetUInt32, game_Objects_Item/GetEnchantmentId, Object/GetEntry, ObjectMgr/LoadPlayerPremadeTemplates, Player.Main/GetItemByPos, QueryResult/Fetch, Unit.Main/GetClass, Unit.Main/GetLevel, WorldSession.Main/GetPlayer | — | player_premade_item, player_premade_item_template |
| HandleCharacterPremadeSpecCommand | method | ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SendSysMessage#2, ObjectMgr/ApplyPremadeSpecTemplateToPlayer, ObjectMgr/GetPlayerPremadeSpecTemplates, Player.Main/GetName, Unit.Main/GetClass | — | — |
| HandleCharacterPremadeSaveSpecCommand | method | ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, Database/DirectPExecute, Database/PExecute#2, Database/PQuery, Database/Query, Field/GetUInt32, Object/GetGUIDLow, ObjectMgr/GetPlayerInfo, ObjectMgr/LoadPlayerPremadeTemplates, QueryResult/Fetch, QueryResult/NextRow, SpellMgr/GetSpellEntry, SpellMgr/Instance, Unit.Main/GetClass, Unit.Main/GetLevel, Unit.Main/GetRace, WorldSession.Main/GetPlayer | — | character_spell, player_premade_spell, player_premade_spell_template |
| HandleCharacterChangeRaceCommand | method | ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/PSendSysMessage, Player.Main/ChangeRace | — | — |
| HandleCharacterCopySkinCommand | method | ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SetSentErrorMessage, Database/escape_string, Database/PQuery, Field/GetUInt32, Field/GetUInt8, QueryResult/Fetch, WorldObject.Object/SetByteValue | — | characters |
| HandleCharacterFillFlysCommand | method | ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/PSendSysMessage, Player.Main/GetName, Player.Main/GetTaxi, Player.Main/GetTeam, PlayerTaxi/LoadTaxiMask | — | — |
| HandleCharacterCityTitleCommand | method | ChatHandler.Chat/ExtractOnOff, ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage#2, Player.Main/RemoveCityTitle, Player.Main/SetCityTitle | — | — |
| HandleHonorShow | method | ChatHandler.Chat/GetMangosString, ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/PSendSysMessage#2, HonorMgr/GetHighestRank, HonorMgr/GetRank, HonorMgr/GetRankPoints, Object/GetUInt16Value, Object/GetUInt32Value, Player.Main/GetHonorMgr, Player.Main/GetName, Player.Main/GetTeam, WorldSession.Main/GetPlayer | — | — |
| HandleHonorAddCommand | method | ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/HasLowerSecurity, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, HonorMgr/Add, Player.Main/GetHonorMgr | — | — |
| HandleHonorAddKillCommand | method | ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/RewardHonor, WorldSession.Main/GetPlayer | — | — |
| HandleModifyHonorCommand | method | ChatHandler.Chat/ExtractInt32, ChatHandler.Chat/ExtractLiteralArg, ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/hasStringAbbr, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/GetName, WorldObject.Object/SetByteValue, WorldObject.Object/SetUInt16Value, WorldObject.Object/SetUInt32Value | — | — |
| HandleHonorResetCommand | method | ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, HonorMgr/Reset, Player.Main/GetHonorMgr | — | — |
| HandleHonorSetRPCommand | method | ChatHandler.Chat/ExtractFloat, ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, HonorMgr/SetRankPoints, HonorMgr/Update, Player.Main/GetHonorMgr, Player.Main/GetName | — | — |
| HandleLearnAllCommand | method | ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, DBCStores/GetTalentSpellCost#2, Player.Main/LearnSpell, SpellEntry/HasAttribute, SpellEntry/HasAttribute#4, SpellEntry/HasEffect, SpellMgr/GetFirstSpellInChain, SpellMgr/GetMaxSpellId, SpellMgr/GetSpellEntry, SpellMgr/Instance, SpellMgr/IsSpellValid | — | — |
| HandleLearnAllGMCommand | method | ChatHandler.Chat/SendSysMessage#2, Player.Main/LearnSpell, SpellMgr/GetSpellEntry, SpellMgr/Instance, SpellMgr/IsSpellValid, WorldSession.Main/GetPlayer | — | — |
| HandleUnLearnAllGMCommand | method | ChatHandler.Chat/SendSysMessage, Player.Main/RemoveSpell, WorldSession.Main/GetPlayer | — | — |
| HandleLearnAllMyClassCommand | method | — | — | — |
| HandleLearnAllMySpellsCommand | method | ChatHandler.Chat/SendSysMessage#2, DBCStores/GetTalentSpellCost#2, ObjectMgr/GetMaxSkillLineAbilityId, ObjectMgr/GetSkillLineAbility, Player.Main/IsSpellFitByClassAndRace, Player.Main/LearnSpell, SpellMgr/GetFirstSpellInChain, SpellMgr/GetSpellEntry, SpellMgr/Instance, SpellMgr/IsSpellValid, Unit.Main/GetClass, WorldSession.Main/GetPlayer | — | — |
| HandleLearnAllMyTalentsCommand | method | ChatHandler.Chat/SendSysMessage#2, Player.Main/LearnSpellHighRank, SpellMgr/GetSpellEntry, SpellMgr/Instance, SpellMgr/IsSpellValid, Unit.Main/GetClassMask, WorldSession.Main/GetPlayer | — | — |
| HandleLearnAllTrainerCommand | method | ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SetSentErrorMessage, ObjectMgr/GetCreatureInfoMap, ObjectMgr/GetNpcTrainerSpells, ObjectMgr/GetNpcTrainerTemplateSpells, Unit.Main/GetClass, WorldSession.Main/GetPlayer | CombatBotBaseAI/LearnPremadeSpecForClass | — |
| HandleLearnTrainerHelper | method | Player.Main/GetTrainerSpellState, Player.Main/IsSpellFitByClassAndRace, Player.Main/LearnSpell, SpellMgr/GetSpellEntry, SpellMgr/Instance, SpellMgr/IsPrimaryProfessionFirstRankSpell | — | — |
| HandleLearnAllItemsCommand | method | ChatHandler.Chat/SendSysMessage, ObjectMgr/GetItemPrototypeMap, Player.Main/CanUseItem#2, Player.Main/LearnSpell, SpellEntry/HasEffect, SpellMgr/GetSkillLineAbilityMapBoundsBySpellId, SpellMgr/GetSpellEntry, SpellMgr/Instance, WorldSession.Main/GetPlayer | CombatBotBaseAI/LearnPremadeSpecForClass | — |
| HandleLearnAllMyTaxisCommand | method | ChatHandler.Chat/GetSession, ChatHandler.Chat/SendSysMessage#2, FindCreatureData/FindCreatureData, ObjectMgr/GetCreatureInfoMap, ObjectMgr/GetNearestTaxiNode, ObjectMgr/GetResult, Player.Main/GetTaxi, Player.Main/GetTeam, PlayerTaxi/SetTaximaskNode, WorldPacket/WorldPacket#4, WorldSession.Main/GetPlayer, WorldSession.Main/SendPacket | — | — |
| HandleLearnAllLangCommand | method | ChatHandler.Chat/SendSysMessage#2, Player.Main/LearnSpell, Player.Main/SetSkill, WorldSession.Main/GetPlayer | — | — |
| HandleLearnAllDefaultCommand | method | ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/GetNameLink, ChatHandler.Chat/PSendSysMessage#2, Player.Main/LearnDefaultSpells, Player.Main/LearnQuestRewardedSpells | — | — |
| HandleLearnSkillRecipesHelper | method | ObjectMgr/GetMaxSkillLineAbilityId, ObjectMgr/GetSkillLineAbility, Player.Main/LearnSpell, SpellMgr/GetSpellEntry, SpellMgr/Instance, SpellMgr/IsSpellValid, Unit.Main/GetClassMask | — | — |
| HandleUnLearnSkillRecipesHelper | method | ObjectMgr/GetMaxSkillLineAbilityId, ObjectMgr/GetSkillLineAbility, Player.Main/RemoveSpell | — | — |
| HandleLearnAllCraftsCommand | method | ChatHandler.Chat/SendSysMessage#2, WorldSession.Main/GetPlayer | — | — |
| HandleUnLearnAllCraftsCommand | method | ChatHandler.Chat/SendSysMessage, Player.Main/LearnDefaultSpells, WorldSession.Main/GetPlayer | — | — |
| FindSkillLineEntryFromProfessionName | method | ChatHandler.Chat/GetSessionDbcLocale, shared_Util/Utf8FitTo, shared_Util/Utf8toWStr, shared_Util/wstrToLower | — | — |
| HandleLearnAllRecipesCommand | method | ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, Player.Main/GetSkillMaxPure, Player.Main/SetSkill | — | — |
| HandleUnLearnAllRecipesCommand | method | ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage#2, Player.Main/GetName | — | — |
| HandleLearnCommand | method | ChatHandler.Chat/ExtractLiteralArg, ChatHandler.Chat/ExtractSpellIdFromLink, ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/GetName, Player.Main/HasSpell, Player.Main/LearnSpell, Player.Main/LearnSpellHighRank, SpellMgr/GetSpellEntry, SpellMgr/Instance, SpellMgr/IsSpellValid, WorldSession.Main/GetPlayer | — | — |
| HandleItemMoveCommand | method | Player.Main/IsValidPos, Player.Main/SwapItem, WorldSession.Main/GetPlayer | — | — |
| HandleAddItemCommand | method | ChatHandler.Chat/ExtractKeyFromLink#2, ChatHandler.Chat/ExtractOptInt32, ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/GetMangosString, ChatHandler.Chat/GetNameLink, ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Database/escape_string, Database/PQuery, Field/GetUInt16, game_Objects_Item/GenerateItemRandomPropertyId, game_Objects_Item/SetBinding, Log.Main/Out, ObjectMgr/GetItemPrototype, Player.Main/CanStoreNewItem, Player.Main/DestroyItemCount#2, Player.Main/GetItemByPos#2, Player.Main/GetItemCount, Player.Main/HasItemCount, Player.Main/SendNewItem, Player.Main/StoreNewItem, QueryResult/Fetch, WorldSession.Main/GetPlayer | — | item_template |
| HandleDeleteItemCommand | method | ChatHandler.Chat/ExtractKeyFromLink#2, ChatHandler.Chat/ExtractOptUInt32, ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/GetMangosString, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SetSentErrorMessage, Database/DirectPExecute, Database/escape_string, Database/PExecute#2, Database/PQuery, Field/GetUInt16, Field/GetUInt32, Log.Main/Out, ObjectGuid/GetCounter, ObjectGuid/ObjectGuid, Player.Main/DestroyItemCount#2, Player.Main/GetItemCount, Player.Main/HasItemCount, Player.Main/SaveInventoryAndGoldToDB, QueryResult/Fetch | — | character_gifts, character_inventory, item_instance, item_template, mail_items |
| HandleAddItemSetCommand | method | ChatHandler.Chat/ExtractUint32KeyFromLink, ChatHandler.Chat/GetMangosString, ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, game_Objects_Item/SetBinding, Log.Main/Out, ObjectMgr/GetItemPrototypeMap, Player.Main/CanStoreNewItem, Player.Main/SendEquipError, Player.Main/SendNewItem, Player.Main/StoreNewItem, WorldSession.Main/GetPlayer | — | — |
| HandleListItemCommand | method | ChatHandler.Chat/ExtractOptUInt32, ChatHandler.Chat/ExtractUint32KeyFromLink, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Database/PQuery, Field/GetCppString, Field/GetUInt32, ObjectMgr/GetItemPrototype, Player.Main/IsBankPos, Player.Main/IsEquipmentPos, Player.Main/IsInventoryPos, QueryResult/Fetch, QueryResult/GetRowCount, QueryResult/NextRow, QueryResult/operator[] | — | auction, character_inventory, mail, mail_items |
| HandleListTalentsCommand | method | ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/GetSessionDbcLocale, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, ChatHandler.LookupCommands/ShowSpellListHelper, DBCStores/GetTalentSpellCost#2, Player.Main/GetSpellMap, SpellMgr/GetSpellEntry, SpellMgr/Instance | — | — |
| HandleResetHonorCommand | method | ChatHandler.Chat/ExtractPlayerTarget, HonorMgr/Reset, Player.Main/GetHonorMgr | — | — |
| HandleResetStatsOrLevelHelper | function | Log.Main/Out, Player.Main/SetFactionForRace, Unit.Main/GetClass, Unit.Main/GetRace, Unit.Main/GetShapeshiftForm, Unit.Main/HasAuraType, Unit.Main/InitPlayerDisplayIds, Unit.Main/SetShapeshiftForm, WorldObject.Object/SetByteValue, WorldObject.Object/SetFloatValue, WorldObject.Object/SetInt32Value, WorldObject.Object/SetUInt32Value | — | — |
| HandleResetLevelCommand | method | ChatHandler.Chat/ExtractPlayerTarget, Pet.Main/SynchronizeLevelWithOwner, Player.Main/InitStatsForLevel, Player.Main/InitTalentForLevel, Player.Main/InitTaxiNodes, Unit.Main/GetPet, Unit.Main/SetLevel, World/getConfig#4, WorldObject.Object/SetUInt32Value | — | — |
| HandleResetStatsCommand | method | ChatHandler.Chat/ExtractPlayerTarget, Player.Main/InitStatsForLevel, Player.Main/InitTalentForLevel | — | — |
| HandleResetSpellsCommand | method | ChatHandler.Chat/GetNameLink, ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/ResetSpells, Player.Main/SendSysMessage#2, WorldSession.Main/GetPlayer | — | — |
| HandleResetTalentsCommand | method | ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/GetNameLink, ChatHandler.Chat/playerLink, ChatHandler.Chat/PSendSysMessage#2, Database/PExecute#2, ObjectGuid/GetCounter, ObjectGuid/ObjectGuid, Player.Main/ResetTalents, Player.Main/SendSysMessage#2, WorldSession.Main/GetPlayer | — | characters |
| HandleResetItemsCommand | method | Bag/GetBagSize, Bag/GetItemByPos, ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, ObjectGuid/ObjectGuid, Player.Main/DestroyItem, Player.Main/GetItemByPos | — | — |
| HandleResetAllCommand | method | ChatHandler.Chat/SendSysMessage#2, Database/PExecute#2, ObjectAccessor/GetPlayers, Player.Main/SetCharacterFlag, World/SendWorldText | — | characters |
| HandleModifyBlockCommand | method | ChatHandler.Chat/ExtractFloat, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/GetName, Player.Main/PSendSysMessage#2, WorldObject.Object/SetStatFloatValue | — | — |
| HandleModifyDodgeCommand | method | ChatHandler.Chat/ExtractFloat, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/GetName, Player.Main/PSendSysMessage#2, WorldObject.Object/SetStatFloatValue | — | — |
| HandleModifyParryCommand | method | ChatHandler.Chat/ExtractFloat, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/GetName, Player.Main/PSendSysMessage#2, WorldObject.Object/SetStatFloatValue | — | — |
| HandleModifyMeleeCritCommand | method | ChatHandler.Chat/ExtractFloat, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/GetName, Player.Main/PSendSysMessage#2, WorldObject.Object/SetStatFloatValue | — | — |
| HandleModifyRangedCritCommand | method | ChatHandler.Chat/ExtractFloat, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/GetName, Player.Main/PSendSysMessage#2, WorldObject.Object/SetStatFloatValue | — | — |
| HandleModifySpellCritCommand | method | ChatHandler.Chat/ExtractFloat, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/GetName, Player.Main/PSendSysMessage#2, Unit.Main/SetSpellCritPercent | — | — |
| HandleModifyGenderCommand | method | ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, ObjectMgr/GetPlayerInfo, Player.Main/GetDrunkValue, Player.Main/GetName, Player.Main/PSendSysMessage#2, Unit.Main/GetClass, Unit.Main/GetGender, Unit.Main/GetRace, Unit.Main/InitPlayerDisplayIds, WorldObject.Object/SetByteValue, WorldObject.Object/SetUInt16Value | — | — |
| HandleModifyDrunkCommand | method | ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/SetDrunkValue | — | — |
| HandleModifyExhaustionCommand | method | ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag | — | — |
| HandleModifyRepCommand | method | ChatHandler.Chat/ExtractInt32, ChatHandler.Chat/ExtractLiteralArg, ChatHandler.Chat/ExtractOptInt32, ChatHandler.Chat/ExtractUint32KeyFromLink, ChatHandler.Chat/GetMangosString, ChatHandler.Chat/GetNameLink, ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/GetSessionDbcLocale, ChatHandler.Chat/HasLowerSecurity, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, ObjectMgr/GetFactionEntry, Player.Main/GetReputationMgr, ReputationMgr/GetReputation, ReputationMgr/SetReputation, shared_Util/Utf8toWStr, shared_Util/wstrToLower | — | — |
| HandleModifyMountCommand | method | ChatHandler.Chat/GetNameLink, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/HasLowerSecurity, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, ObjectMgr/GetCreatureDisplayInfoAddon, Player.Main/Mount, Player.Main/PSendSysMessage#2 | — | — |
| HandleModifyMoneyCommand | method | ChatHandler.Chat/ExtractInt32, ChatHandler.Chat/GetMangosString, ChatHandler.Chat/GetNameLink, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/HasLowerSecurity, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Log.Main/Out, Object/GetObjectGuid, Player.Main/GetMoney, Player.Main/LogModifyMoney, Player.Main/PSendSysMessage#2, Player.Main/SetMoney, WorldSession.Main/GetPlayer | — | — |
| HandleModifyTalentCommand | method | ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/HasLowerSecurity, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/SetFreeTalentPoints | — | — |
| HandleModifySpeedCommand | method | ChatHandler.Chat/GetAccessLevel, ChatHandler.Chat/GetNameLink, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/HasLowerSecurity, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/PSendSysMessage#2, Unit.Main/IsTaxiFlying, Unit.Main/UpdateSpeed | — | — |
| HandleModifySwimCommand | method | ChatHandler.Chat/GetAccessLevel, ChatHandler.Chat/GetNameLink, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/HasLowerSecurity, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/PSendSysMessage#2, Unit.Main/IsTaxiFlying, Unit.Main/UpdateSpeed | — | — |
| HandleModifyBWalkCommand | method | ChatHandler.Chat/GetAccessLevel, ChatHandler.Chat/GetNameLink, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/HasLowerSecurity, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/PSendSysMessage#2, Unit.Main/IsTaxiFlying, Unit.Main/UpdateSpeed | — | — |
| HandleModifyFlyCommand | method | ChatHandler.Chat/GetNameLink, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/HasLowerSecurity, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/PSendSysMessage#2, Unit.Main/GetMotionMaster, Unit.Main/IsTaxiFlying, WaypointMovementGenerator/Reset, WorldSession.Main/GetPlayer | — | — |
| HandleModifyEnergyCommand | method | ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/GetNameLink, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/HasLowerSecurity, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/PSendSysMessage#2, Unit.Main/GetMaxPower, Unit.Main/SetMaxPower, Unit.Main/SetPower | — | — |
| HandleModifyRageCommand | method | ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/GetNameLink, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/HasLowerSecurity, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/PSendSysMessage#2, Unit.Main/GetMaxPower, Unit.Main/SetMaxPower, Unit.Main/SetPower | — | — |
| HandleModifyHairStyleCommand | method | ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/PSendSysMessage, Unit.Main/DeMorph, Unit.Main/SetDisplayId, WorldObject.Object/DirectSendPublicValueUpdate#3, WorldObject.Object/SetByteValue, WorldSession.Main/GetPlayer | — | — |
| HandleModifyHairColorCommand | method | ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/PSendSysMessage, Unit.Main/DeMorph, Unit.Main/SetDisplayId, WorldObject.Object/DirectSendPublicValueUpdate#3, WorldObject.Object/SetByteValue, WorldSession.Main/GetPlayer | — | — |
| HandleModifySkinColorCommand | method | ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/PSendSysMessage, Unit.Main/DeMorph, Unit.Main/SetDisplayId, WorldObject.Object/DirectSendPublicValueUpdate#3, WorldObject.Object/SetByteValue, WorldSession.Main/GetPlayer | — | — |
| HandleModifyAccessoriesCommand | method | ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/PSendSysMessage, Unit.Main/DeMorph, Unit.Main/SetDisplayId, WorldObject.Object/DirectSendPublicValueUpdate#3, WorldObject.Object/SetByteValue, WorldSession.Main/GetPlayer | — | — |
| HandlePDumpLoadCommand | method | ChatHandler.Chat/ExtractAccountId, ChatHandler.Chat/ExtractLiteralArg, ChatHandler.Chat/ExtractQuotedOrLiteralArg, ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, ObjectGuid/ObjectGuid#2, ObjectMgr/CheckPlayerName, ObjectMgr/GetPlayerAccountIdByGUID, ObjectMgr/normalizePlayerName, PlayerDump/LoadDump, PlayerDumpReader/PlayerDumpReader | — | — |
| HandlePDumpWriteCommand | method | ChatHandler.Chat/ExtractLiteralArg, ChatHandler.Chat/ExtractPlayerNameFromLink, ChatHandler.Chat/ExtractQuotedOrLiteralArg, ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, ObjectGuid/GetCounter, ObjectGuid/ObjectGuid, ObjectGuid/ObjectGuid#2, ObjectGuid/operator!, ObjectMgr/GetPlayerAccountIdByGUID, ObjectMgr/GetPlayerGuidByName, PlayerDump/WriteDump, PlayerDumpWriter/PlayerDumpWriter | — | — |
| HandleQuestAddCommand | method | ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/ExtractUint32KeyFromLink, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, ObjectMgr/GetItemPrototypeMap, ObjectMgr/GetQuestTemplate, Player.Main/AddQuest, Player.Main/CanAddQuest, Player.Main/CanCompleteQuest, Player.Main/CompleteQuest, Player.Main/GetName | — | — |
| HandleQuestRemoveCommand | method | ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/ExtractUint32KeyFromLink, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, ObjectMgr/GetQuestTemplate, Player.Main/GetQuestStatusMap, Player.Main/RemoveQuest, Player.Main/SetQuestStatus | — | — |
| HandleQuestStatusCommandHelper | function | Database/PQuery, Field/GetBool, Field/GetUInt32, ObjectGuid/GetCounter, Player.Main/GetQuestStatusData, QueryResult/Fetch, QueryResult/GetRowCount, QuestStatusData/QuestStatusData | — | character_queststatus |
| HandleQuestStatusCommand | method | ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/ExtractUint32KeyFromLink, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, ObjectGuid/ObjectGuid, ObjectMgr/GetItemPrototype, ObjectMgr/GetQuestTemplate, QuestDef/GetNextQuestInChain, QuestDef/GetPrevQuestId, QuestDef/GetQuestId, QuestDef/QuestStatusToString | — | — |
| HandleQuestCompleteCommand | method | ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/ExtractUint32KeyFromLink, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, ObjectMgr/GetQuestTemplate, Player.Main/FullQuestComplete, Player.Main/GetName, Player.Main/GetQuestStatus | — | — |
| HandlePetLearnSpellCommand | method | ChatHandler.Chat/ExtractOptInt32, ChatHandler.Chat/GetSelectedPet, ChatHandler.Chat/PSendSysMessage, Pet.Main/GetName, Pet.Main/LearnSpell | — | — |
| HandlePetUnlearnSpellCommand | method | ChatHandler.Chat/ExtractOptInt32, ChatHandler.Chat/GetSelectedPet, ChatHandler.Chat/PSendSysMessage, Pet.Main/GetName, Pet.Main/UnlearnSpell | — | — |
| HandlePetListCommand | method | CharacterDatabaseCache/GetCharPetsMap, CharacterDatabaseCache/instance, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, ObjectGuid/GetCounter, ObjectGuid/operator!, ObjectMgr/GetPlayerGuidByName, ObjectMgr/normalizePlayerName | — | — |
| HandlePetRenameCommand | method | CharacterDatabaseCache/GetCharacterPetById, CharacterDatabaseCache/instance, ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SetSentErrorMessage, Database/escape_string, Database/PExecute#2, Database/PQuery, Field/GetString, Field/GetUInt32, ObjectMgr/CheckPetName, QueryResult/Fetch | — | character_pet |
| HandlePetDeleteCommand | method | CharacterDatabaseCache/DeleteCharacterPetById, CharacterDatabaseCache/GetCharacterPetById, CharacterDatabaseCache/instance, ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SetSentErrorMessage, Database/PExecute#2 | — | character_pet |
| HandlePetLoyaltyCommand | method | ChatHandler.Chat/ExtractOptInt32, ChatHandler.Chat/GetSelectedPet, Pet.Main/GetPetType, Pet.Main/ModifyLoyalty | — | — |
| HandlePetInfoCommand | method | ChatHandler.Chat/GetSelectedPet, ChatHandler.Chat/PSendSysMessage, Object/GetByteValue, Object/GetObjectGuid, Object/GetUInt32Value, ObjectGuid/GetString, Pet.Main/GetPetType, Unit.Main/GetOwnerGuid | — | — |
| HandleChannelJoinCommand | method | ChatHandler.Chat/PSendSysMessage, game_Server_Packets_Channel/JoinChannel, WorldSession.ChannelHandler/HandleJoinChannelOpcode | — | — |
| HandleChannelLeaveCommand | method | ChatHandler.Chat/PSendSysMessage, LeaveChannel/LeaveChannel, WorldSession.ChannelHandler/HandleLeaveChannelOpcode | — | — |
| HandleServiceDeleteCharacters | method | ChatHandler.Chat/ExtractUInt32, Config/GetStringDefault, Database/Query, Field/GetUInt32, Log.Main/Out, ObjectGuid/ObjectGuid#2, Player.Main/DeleteFromDB, QueryResult/Fetch, QueryResult/GetRowCount, QueryResult/NextRow, shared_Util/StrSplit | — | characters, character_inventory |
| HandleGoldRemoval | method | ChatHandler.Chat/GetAccountId, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SetSentErrorMessage, Database/escape_string, ObjectMgr/GetPlayer#2, Player.Main/GetMoney, Player.Main/ModifyMoney | — | characters |
| HandleRepairitemsCommand | method | ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/GetNameLink, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/HasLowerSecurity, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, Player.Main/DurabilityRepairAll, Player.Main/PSendSysMessage#2 | — | — |
| HandleCombatStopCommand | method | ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/HasLowerSecurity, HostileRefManager/deleteReferences, Unit.Main/CombatStop, Unit.Main/GetHostileRefManager | — | — |
| HandleGroupAddItemCommand | method | ChatHandler.Chat/ExtractOptUInt32, ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SetSentErrorMessage, Group/GetFirstMember, GroupReference/next, ObjectMgr/GetItemPrototype, Player.Main/GetGroup, Player.Main/SendNewItem, Player.Main/StoreNewItemInInventorySlot, WorldSession.Main/GetPlayer | — | — |
| HandleGroupReviveCommand | method | ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SetSentErrorMessage, Group/GetFirstMember, GroupReference/next, Player.Main/GetGroup, Player.Main/ResurrectPlayer, Player.Main/SpawnCorpseBones, Unit.Main/IsDead, WorldSession.Main/GetPlayer | — | — |
| HandleGroupReplenishCommand | method | ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SetSentErrorMessage, Group/GetFirstMember, GroupReference/next, Player.Main/GetGroup, Unit.Main/GetMaxHealth, Unit.Main/GetMaxPower, Unit.Main/GetPowerType, Unit.Main/IsAlive, Unit.Main/SetHealth, Unit.Main/SetPower, WorldSession.Main/GetPlayer | — | — |
| HandleGroupSummonCommand | method | ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SetSentErrorMessage, Group/GetFirstMember, GroupReference/next, Object/GetObjectGuid, Player.Main/GetGroup, Player.Main/SendSummonRequest, WorldObject.Object/GetMapId, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/GetZoneId, WorldSession.Main/GetPlayer | — | — |
| HandleListExploredAreasCommand | method | ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/GetSessionDbLocaleIndex, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Object/GetUInt32Value, ObjectMgr/GetAreaLocaleString, Player.Main/GetName | — | — |
| HandleListVisibleGuidsCommand | method | ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, ObjectGuid/GetString, Player.Main/GetName | — | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `account`: id int(11) unsigned PK, username varchar(32), gmlevel tinyint(3) unsigned, sessionkey longtext?, v longtext?, s longtext?, token_key varchar(100), email text?, joindate timestamp, last_ip varchar(30), failed_logins int(11) unsigned, locked tinyint(3) unsigned, lock_country varchar(2), last_login timestamp, online tinyint(4), expansion tinyint(3) unsigned, mutetime bigint(40), locale tinyint(3) unsigned, os varchar(4), platform varchar(4), current_realm tinyint(3) unsigned, flags int(10) unsigned, security varchar(255)?, email_verif tinyint(1), geolock_pin int(11)?
- `auction`: id int(11) unsigned PK, house_id int(11) unsigned, item_guid int(11) unsigned, item_id int(11) unsigned, seller_guid int(11) unsigned, buyout_price int(11), expire_time bigint(40), buyer_guid int(11) unsigned, last_bid int(11), start_bid int(11), deposit int(11)
- `character_gifts`: guid int(20) unsigned, item_guid int(11) unsigned PK, item_id int(20) unsigned, flags int(20) unsigned
- `character_inventory`: guid int(11) unsigned, bag int(11) unsigned, slot tinyint(3) unsigned, item_guid int(11) unsigned PK, item_id int(11) unsigned
- `character_pet`: id int(11) unsigned PK, entry int(11) unsigned, owner_guid int(11) unsigned, display_id int(11) unsigned?, created_by_spell int(11) unsigned, pet_type tinyint(3) unsigned, level int(11) unsigned, xp int(11) unsigned, react_state tinyint(1) unsigned, loyalty_points int(11), loyalty int(11) unsigned, training_points int(11), name varchar(100)?, renamed tinyint(1) unsigned, slot int(11) unsigned, current_health int(11) unsigned, current_mana int(11) unsigned, current_happiness int(11) unsigned, save_time bigint(20) unsigned, reset_talents_cost int(11) unsigned, reset_talents_time bigint(20) unsigned, action_bar_data longtext?, teach_spell_data longtext?
- `character_queststatus`: guid int(11) unsigned PK, quest int(11) unsigned PK, status int(11) unsigned, rewarded tinyint(1) unsigned, explored tinyint(1) unsigned, timer bigint(20) unsigned, mob_count1 int(11) unsigned, mob_count2 int(11) unsigned, mob_count3 int(11) unsigned, mob_count4 int(11) unsigned, item_count1 int(11) unsigned, item_count2 int(11) unsigned, item_count3 int(11) unsigned, item_count4 int(11) unsigned, reward_choice int(11) unsigned
- `character_skills`: guid int(11) unsigned PK, skill mediumint(9) unsigned PK, value mediumint(9) unsigned, max mediumint(9) unsigned
- `character_spell`: guid int(11) unsigned PK, spell int(11) unsigned PK, active tinyint(3) unsigned, disabled tinyint(3) unsigned
- `characters`: guid int(11) unsigned PK, account int(11) unsigned, name varchar(12), race tinyint(3) unsigned, class tinyint(3) unsigned, gender tinyint(3) unsigned, skin tinyint(3) unsigned, face tinyint(3) unsigned, hair_style tinyint(3) unsigned, hair_color tinyint(3) unsigned, facial_hair tinyint(3) unsigned, level tinyint(3) unsigned, xp int(10) unsigned, money int(10) unsigned, character_flags int(10) unsigned, zone int(11) unsigned, map int(11) unsigned, instance int(11) unsigned, position_x float, position_y float, position_z float, orientation float, transport_guid bigint(20) unsigned, transport_x float, transport_y float, transport_z float, transport_o float, known_taxi_mask longtext?, current_taxi_path text?, online tinyint(3) unsigned, played_time_total int(11) unsigned, played_time_level int(11) unsigned, create_time bigint(20) unsigned, logout_time bigint(20) unsigned, rest_bonus float, reset_talents_multiplier int(11) unsigned, reset_talents_time bigint(20) unsigned, death_expire_time bigint(20) unsigned, stable_slots tinyint(1) unsigned, bank_bag_slots tinyint(1) unsigned, extra_flags int(11) unsigned, honor_rank_points float, honor_highest_rank int(11) unsigned, honor_standing int(11) unsigned, honor_last_week_hk int(11) unsigned, honor_last_week_cp float, honor_stored_hk int(11), honor_stored_dk int(11), watched_faction int(11), drunk smallint(5) unsigned, health int(10) unsigned, power1 int(10) unsigned, power2 int(10) unsigned, power3 int(10) unsigned, power4 int(10) unsigned, power5 int(10) unsigned, explored_zones longtext?, equipment_cache longtext?, ammo_id int(10) unsigned, action_bars tinyint(3) unsigned, deleted_account int(11) unsigned?, deleted_name varchar(12)?, deleted_time bigint(20)?, world_phase_mask int(11)?
- `characters_guid_delete`: guid int(11)?
- `characters_item_delete`: entry int(11)?
- `item_instance`: guid int(10) unsigned PK, item_id mediumint(8) unsigned, owner_guid int(10) unsigned, creator_guid int(10) unsigned, gift_creator_guid int(10) unsigned, count int(10) unsigned, duration int(10), charges tinytext?, flags mediumint(8) unsigned, enchantments text, random_property_id smallint(5), durability smallint(5) unsigned, text int(10) unsigned, generated_loot tinyint(4)?
- `item_template`: entry mediumint(8) unsigned PK, patch tinyint(3) unsigned PK, class tinyint(3) unsigned, subclass tinyint(3) unsigned, name varchar(255), description varchar(255), display_id mediumint(8) unsigned, quality tinyint(3) unsigned, flags int(10) unsigned, buy_count tinyint(3) unsigned, buy_price int(10) unsigned, sell_price int(10) unsigned, inventory_type tinyint(3) unsigned, allowable_class mediumint(9), allowable_race mediumint(9), item_level tinyint(3) unsigned, required_level tinyint(3) unsigned, required_skill smallint(5) unsigned, required_skill_rank smallint(5) unsigned, required_spell smallint(5) unsigned, required_honor_rank mediumint(8) unsigned, required_city_rank mediumint(8) unsigned, required_reputation_faction smallint(5) unsigned, required_reputation_rank smallint(5) unsigned, max_count smallint(5) unsigned, stackable smallint(5) unsigned, container_slots tinyint(3) unsigned, stat_type1 tinyint(3) unsigned, stat_value1 smallint(6), stat_type2 tinyint(3) unsigned, stat_value2 smallint(6), stat_type3 tinyint(3) unsigned, stat_value3 smallint(6), stat_type4 tinyint(3) unsigned, stat_value4 smallint(6), stat_type5 tinyint(3) unsigned, stat_value5 smallint(6), stat_type6 tinyint(3) unsigned, stat_value6 smallint(6), stat_type7 tinyint(3) unsigned, stat_value7 smallint(6), stat_type8 tinyint(3) unsigned, stat_value8 smallint(6), stat_type9 tinyint(3) unsigned, stat_value9 smallint(6), stat_type10 tinyint(3) unsigned, stat_value10 smallint(6), delay smallint(5) unsigned, range_mod float, ammo_type tinyint(3) unsigned, dmg_min1 float, dmg_max1 float, dmg_type1 tinyint(3) unsigned, dmg_min2 float, dmg_max2 float, dmg_type2 tinyint(3) unsigned, dmg_min3 float, dmg_max3 float, dmg_type3 tinyint(3) unsigned, dmg_min4 float, dmg_max4 float, dmg_type4 tinyint(3) unsigned, dmg_min5 float, dmg_max5 float, dmg_type5 tinyint(3) unsigned, block mediumint(8) unsigned, armor smallint(5), holy_res smallint(5), fire_res smallint(5), nature_res smallint(5), frost_res smallint(5), shadow_res smallint(5), arcane_res smallint(5), spellid_1 smallint(5) unsigned, spelltrigger_1 tinyint(3) unsigned, spellcharges_1 tinyint(4), spellppmrate_1 float, spellcooldown_1 int(11), spellcategory_1 smallint(5) unsigned, spellcategorycooldown_1 int(11), spellid_2 smallint(5) unsigned, spelltrigger_2 tinyint(3) unsigned, spellcharges_2 tinyint(4), spellppmrate_2 float, spellcooldown_2 int(11), spellcategory_2 smallint(5) unsigned, spellcategorycooldown_2 int(11), spellid_3 smallint(5) unsigned, spelltrigger_3 tinyint(3) unsigned, spellcharges_3 tinyint(4), spellppmrate_3 float, spellcooldown_3 int(11), spellcategory_3 smallint(5) unsigned, spellcategorycooldown_3 int(11), spellid_4 smallint(5) unsigned, spelltrigger_4 tinyint(3) unsigned, spellcharges_4 tinyint(4), spellppmrate_4 float, spellcooldown_4 int(11), spellcategory_4 smallint(5) unsigned, spellcategorycooldown_4 int(11), spellid_5 smallint(5) unsigned, spelltrigger_5 tinyint(3) unsigned, spellcharges_5 tinyint(4), spellppmrate_5 float, spellcooldown_5 int(11), spellcategory_5 smallint(5) unsigned, spellcategorycooldown_5 int(11), bonding tinyint(3) unsigned, page_text mediumint(8) unsigned, page_language tinyint(3) unsigned, page_material tinyint(3) unsigned, start_quest mediumint(8) unsigned, lock_id mediumint(8) unsigned, material tinyint(4), sheath tinyint(3) unsigned, random_property mediumint(8) unsigned, set_id mediumint(8) unsigned, max_durability smallint(5) unsigned, area_bound mediumint(8) unsigned, map_bound smallint(6), duration int(11) unsigned, bag_family mediumint(9), disenchant_id mediumint(8) unsigned, food_type tinyint(3) unsigned, min_money_loot int(10) unsigned, max_money_loot int(10) unsigned, wrapped_gift mediumint(8) unsigned, extra_flags tinyint(1) unsigned, other_team_entry int(11) unsigned?
- `mail`: id int(11) unsigned PK, message_type tinyint(3) unsigned, stationery tinyint(3), mail_template_id mediumint(8) unsigned, sender_guid int(11) unsigned, receiver_guid int(11) unsigned, subject longtext?, item_text_id int(11) unsigned, has_items tinyint(3) unsigned, expire_time bigint(40), deliver_time bigint(40), money int(11) unsigned, cod int(11) unsigned, checked tinyint(3) unsigned
- `mail_items`: mail_id int(11) unsigned PK, item_guid int(11) unsigned PK, item_id int(11) unsigned, receiver_guid int(11) unsigned
- `player_premade_item`: entry int(10) unsigned, item int(10) unsigned, enchant int(10) unsigned, team int(10) unsigned
- `player_premade_item_template`: entry int(10) unsigned PK, class tinyint(3) unsigned, level tinyint(3) unsigned, role tinyint(3) unsigned, name varchar(50)?
- `player_premade_spell`: entry int(10) unsigned PK, spell smallint(5) unsigned PK
- `player_premade_spell_template`: entry int(10) unsigned PK, class tinyint(3) unsigned, level tinyint(3) unsigned, role tinyint(3) unsigned, name varchar(50)?

*`?` = nullable, `PK` = primary key column.*


---

<!-- verify: boundary-bleed | foreign: ChatHandler, disable -->
