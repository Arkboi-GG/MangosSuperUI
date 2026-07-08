<!-- provenance: boundary-bleed -->
# ChatHandler.LookupCommands

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ChatHandler.LookupCommands

## Purpose & Responsibilities

`ChatHandler.LookupCommands` (implemented in `LookupCommands.cpp`) provides a comprehensive suite of administrative and debugging commands for searching, listing, and inspecting game entities, database records, and system states within the MaNGOS server framework. These commands are designed for Game Masters (GMs), administrators, and console operators.

The unit handles lookups for:
*   **World Entities:** Creatures, Game Objects, Items, Spells, Quests, Skills, Factions, Areas, Teleports, Taxi Nodes, Sounds, and Events.
*   **Player & Account Data:** Looking up accounts by email, IP, or name; looking up players by name, IP, or account; and retrieving character details.
*   **System Structures:** Guilds, Spawn Pools, and Click-to-Move usage statistics.

The commands support both in-game chat execution (where `m_session` is valid) and console execution (where `m_session` is null). They heavily utilize localization helpers to display names in the operator's preferred language and often provide contextual information relative to a selected target player (e.g., "Is this item usable by the target?", "Does the target know this spell?").

## Member-by-Member Behavior

### World Entity Lookups

These commands search through static data stores (DBC files, object managers) or dynamic world databases to find entities matching a user-provided string or ID.

*   **`HandleListObjectCommand`**: Lists spawned Game Objects by their Entry ID. It accepts an optional count limit. If executed in-game, it orders results by proximity to the issuing player. It queries the `gameobject` table to count total spawns and retrieve coordinates.
*   **`HandleListCreatureCommand`**: Lists spawned Creatures by their Entry ID. Similar to objects, it supports proximity ordering for in-game users and queries the `creature` table for spawn counts and coordinates.
*   **`HandleLookupItemCommand`**: Searches the item template store (`sObjectMgr.GetItemPrototypeMap`) for items whose names match the input string. It respects the session's locale index. For each match, it calls `ShowItemListHelper` to display the item, optionally indicating if a target player can use it.
*   **`HandleLookupItemSetCommand`**: Searches the `ItemSet.dbc` store for item sets matching the input string. It iterates through all locales if the primary locale doesn't match.
*   **`HandleLookupSkillCommand`**: Searches the `SkillLine.dbc` store for skills matching the input. If a target player is selected, it displays whether the target knows the skill and their current/max/bonus values.
*   **`HandleLookupSpellCommand`**: Searches the `Spell.dbc` store via `sSpellMgr`. For each match, it delegates to `ShowSpellListHelper` to display detailed spell information, including rank, type (talent/passive/learn), and whether the target player knows or has the aura active.
*   **`HandleLookupQuestCommand`**: Searches the quest template store (`sObjectMgr.GetQuestTemplates`). If a target player is selected, it shows the quest status (Active, Complete, Rewarded) for that player.
*   **`HandleLookupCreatureCommand`**: Searches the creature template store (`sObjectMgr.GetCreatureInfoMap`) for creatures matching the name.
*   **`HandleLookupCreatureModelCommand`**: A specialized lookup that can search for creatures by their model ID or model name. It can optionally export the results to a SQL file (`creature_export.sql`) if the `export` argument is provided. It iterates through `sCreatureModelDataStore` and `sCreatureDisplayInfoStore` to find matches.
*   **`HandleLookupObjectCommand`**: Searches the game object template store (`sObjectMgr.GetGameObjectInfoMap`) for objects matching the name.
*   **`HandleLookupTaxiNodeCommand`**: Searches the taxi node entries (`sObjectMgr.GetTaxiNodeEntry`) for nodes matching the name, displaying their coordinates.
*   **`HandleLookupAreaCommand`**: Searches the area storage (`sAreaStorage`) for areas matching the name, supporting locale-specific names.
*   **`HandleLookupTeleCommand`**: Searches the teleport map (`sObjectMgr.GetGameTeleMap`) for teleports matching the name.
*   **`HandleLookupSoundCommand`**: Searches the sound entries map (`sObjectMgr.GetSoundEntriesMap`) for sounds matching the name.
*   **`HandleLookupFactionCommand`**: Searches the faction map (`sObjectMgr.GetFactionMap`). If a target player is selected, it displays the target's reputation rank and state with that faction via `ShowFactionListHelper`.
*   **`HandleLookupEventCommand`**: Searches the game event manager (`sGameEventMgr.GetEventMap`) for events matching the description, indicating if they are currently active.

### Helper Methods for Display

These methods format and send the output for the lookup commands above.

*   **`ShowItemListHelper`**: Formats item information, creating a clickable link if in-game. It checks `Player.CanUseItem` if a target is provided.
*   **`ShowSpellListHelper`**: Formats spell information, determining if it's a talent, passive, or learn-spell. It checks `Player.HasSpell` and `Unit.HasAura` for the target.
*   **`ShowQuestListHelper`**: Formats quest information, checking `Player.GetQuestStatus` and `Player.GetQuestRewardStatus` for the target.
*   **`ShowFactionListHelper`**: Formats faction information, including reputation rank and flags (visible, at war, etc.) if a target and reputation state are provided.
*   **`ShowPoolListHelper`**: Displays details about a spawn pool, including its description, auto-spawn status, max limit, and counts of creatures/game objects/sub-pools.

### Account & Player Lookups

These commands interact with the `LoginDatabase` and `CharacterDatabase` to find user-related information.

*   **`HandleLookupAccountEmailCommand`**: Searches the `account` table for accounts with emails matching the input. Uses async query.
*   **`HandleLookupAccountIpCommand`**: Searches the `account` table for accounts with IPs matching the input. Delegates to `ShowAccountIpListHelper`.
*   **`HandleLookupAccountIponlineCommand`**: Searches the `account` table for *online* accounts with IPs matching the input. Delegates to `ShowAccountIpListHelper`.
*   **`HandleLookupAccountNameCommand`**: Searches the `account` table for usernames matching the input. Normalizes the string before querying.
*   **`HandleLookupPlayerIpCommand`**: Finds accounts by IP, then delegates to `LookupPlayerSearchCommand` to find characters on those accounts.
*   **`HandleLookupPlayerAccountCommand`**: Finds accounts by username, then delegates to `LookupPlayerSearchCommand`.
*   **`HandleLookupPlayerEmailCommand`**: Finds accounts by email, then delegates to `LookupPlayerSearchCommand`.
*   **`HandleLookupPlayerNameCommand`**: Searches the `characters` table directly for character names matching the input. Uses async query.
*   **`HandleLookupPlayerCharacterCommand`**: A complex lookup that first tries to resolve a character name to an account ID via `ObjectMgr.GetPlayerDataByName`. It performs a security check: if the issuer's access level is lower than the target account's security level (and the issuer isn't an admin), it returns no results. Otherwise, it finds the account by IP and delegates to `LookupPlayerSearchCommand`.
*   **`LookupPlayerSearchCommand`**: Takes a result set of accounts and constructs delayed queries to fetch character details (`guid`, `name`, `race`, `class`, `level`) from the `characters` table for each account. It uses `PlayerSearchQueryHolder` to manage the async flow.
*   **`ShowAccountIpListHelper`**: Helper for IP-based account lookups, constructing the SQL query with or without the `online = 1` filter.

### System & Guild Lookups

*   **`HandleLookupGuildCommand`**: Retrieves a guild by name from `sGuildMgr`. Displays guild ID, leader name, creation date, member count, MOTD, and info.
*   **`HandleLookupPoolCommand`**: Searches all pool templates (`sPoolMgr.GetPoolTemplate`) for pools whose description matches the input.
*   **`HandlePoolListCommand`**: Lists all pools that can be spawned on the map where the issuing player is currently located. It checks `MapPersistentState` and `PoolTemplateData.CanBeSpawnedAtMap`.
*   **`HandleListClickToMoveCommand`**: Iterates through all online players (`sObjectAccessor.GetPlayers`). Filters for those who have used the "Click to Move" feature (`WorldSession.HasUsedClickToMove`). Sorts them by level and displays their name, IP, and level.

## Cross-Unit Boundaries

*   **ChatHandler.Chat**: Most members call helper methods from the main `ChatHandler` class (defined in `Chat.cpp`/`Chat.h`), such as `ExtractOptUInt32`, `ExtractUint32KeyFromLink`, `PSendSysMessage`, `SetSentErrorMessage`, `GetSessionDbLocaleIndex`, `GetSessionDbcLocale`, `GetSelectedPlayer`, `GetAccountId`, and `GetAccessLevel`. These handle argument parsing, output formatting, and session context.
*   **ObjectMgr**: Extensively used to retrieve static data templates and maps: `GetGameObjectTemplate`, `GetCreatureTemplate`, `GetItemPrototype`, `GetItemLocale`, `GetQuestTemplate`, `GetQuestLocale`, `GetCreatureInfoMap`, `GetCreatureLocale`, `GetGameObjectInfoMap`, `GetGameObjectLocale`, `GetTaxiNodeEntry`, `GetMaxTaxiNodeId`, `GetAreaLocaleString`, `GetGameTeleMap`, `GetSoundEntriesMap`, `GetFactionMap`, `GetPlayerDataByName`, `normalizePlayerName`, `GetPlayerNameByGUID`.
*   **Database**: Used for dynamic queries against `WorldDatabase`, `LoginDatabase`, and `CharacterDatabase`. Methods like `PQuery`, `AsyncPQuery`, and `escape_string` are called.
*   **Player.Main**: Used to inspect the state of a target player: `CanUseItem`, `HasSkill`, `GetSkillValuePure`, `GetSkillMaxPure`, `GetSkillBonusPermanent`, `GetSkillBonusTemporary`, `HasSpell`, `HasAura`, `GetQuestStatus`, `GetQuestRewardStatus`, `GetReputationMgr`, `GetLevel`, `GetSession`.
*   **WorldSession.Main**: Used to get the player associated with the session (`GetPlayer`) and session-specific data like `GetRemoteAddress` and `HasUsedClickToMove`.
*   **SpellMgr**: Used for spell data: `GetSpellEntry`, `GetMaxSpellId`, `GetSpellRank`, `Instance`.
*   **DBCStores**: Used for DBC data: `GetTalentSpellCost`.
*   **PoolManager**: Used for pool data: `GetPoolTemplate`, `GetPoolCreatures`, `GetPoolGameObjects`, `GetPoolPools`, `GetMaxPoolId`.
*   **GuildMgr**: Used to retrieve guilds: `GetGuildByName`.
*   **Guild**: Used to access guild properties: `GetId`, `GetName`, `GetLeaderGuid`, `GetCreatedYear/Month/Day`, `GetMemberSize`, `GetAccountsNumber`, `GetMOTD`, `GetGINFO`.
*   **AccountMgr**: Used for account normalization: `normalizeString`, `GetSecurity`.
*   **GameEventMgr.Main**: Used for event data: `GetEventMap`, `IsActiveEvent`, `IsValidEvent`.
*   **Map.Main / MapPersistentStateMgr**: Used in `HandlePoolListCommand` to determine the current map and its instanceability.
*   **AsyncCommandHandlers / SqlOperations**: Used for managing asynchronous database queries and result holders (`AddAccountInfo`, `PlayerSearchQueryHolder`, `SetPQuery`, `SetSize`).
*   **shared_Util**: Used for string manipulation: `Utf8FitTo`, `Utf8toWStr`, `wstrToLower`, `strToLower`.
*   **Log.Main**: Used in `HandleLookupCreatureModelCommand` for logging errors during file export.

## Data Model

This unit interacts with the following database tables:

*   **`gameobject`**: Queried by `HandleListObjectCommand` to count spawns and retrieve coordinates (`guid`, `position_x`, `position_y`, `position_z`, `map`, `id`).
*   **`creature`**: Queried by `HandleListCreatureCommand` to count spawns and retrieve coordinates (`guid`, `position_x`, `position_y`, `position_z`, `map`, `id`).
*   **`account`**: Queried by account lookup commands (`HandleLookupAccountEmailCommand`, `HandleLookupAccountIpCommand`, `HandleLookupAccountIponlineCommand`, `HandleLookupAccountNameCommand`, `HandleLookupPlayerIpCommand`, `HandleLookupPlayerAccountCommand`, `HandleLookupPlayerEmailCommand`, `HandleLookupPlayerCharacterCommand`). Columns accessed include `id`, `username`, `last_ip`, `expansion`, `online`, `email`.
*   **`characters`**: Queried by `HandleLookupPlayerNameCommand` and `LookupPlayerSearchCommand` to find characters by name or account ID. Columns accessed include `guid`, `name`, `race`, `class`, `level`, `account`.

## Notable Implementation Details

*   **Proximity Ordering**: `HandleListObjectCommand` and `HandleListCreatureCommand` dynamically construct SQL queries. If `m_session` is present, they calculate the squared Euclidean distance between the player's position and the entity's position (`POW(x - px, 2) + ...`) to order results by proximity. This calculation is done in SQL to leverage database sorting efficiency.
*   **Locale Fallback**: Many lookup commands (Items, Spells, Quests, etc.) implement a fallback mechanism for localization. They first check the session's specific locale index. If the name is empty or doesn't match, they iterate through other available locales to find a match, ensuring broader search capability.
*   **Security Check in `HandleLookupPlayerCharacterCommand`**: This command enforces a security hierarchy. It retrieves the target account's security level via `AccountMgr.GetSecurity`. If the issuer's access level is lower than the target's (and the issuer is not an administrator), the command silently fails to return results, preventing lower-level GMs from investigating higher-level accounts.
*   **Async vs. Sync Queries**: Account and player lookups that might involve large datasets or multiple steps often use `AsyncPQuery` or `DelayQueryHolder` (e.g., `HandleLookupAccountEmailCommand`, `LookupPlayerSearchCommand`) to avoid blocking the server thread. Simpler lookups or those requiring immediate feedback may use synchronous `PQuery`.
*   **File Export in `HandleLookupCreatureModelCommand`**: This command has a unique feature to export results to a SQL file (`creature_export.sql`). It uses standard C++ file I/O (`fopen`, `fputs`, `fclose`) and logs the output to the server log as well.
*   **Click-to-Move Tracking**: `HandleListClickToMoveCommand` relies on a flag `HasUsedClickToMove` in the `WorldSession`. This suggests the server tracks whether a player has utilized a specific movement cheat or feature, allowing GMs to identify potential abusers.

## Member Reference

**HandleListObjectCommand**: Lists spawned Game Objects by Entry ID, ordered by proximity if in-game. Queries `gameobject` table.
**HandleListCreatureCommand**: Lists spawned Creatures by Entry ID, ordered by proximity if in-game. Queries `creature` table.
**ShowItemListHelper**: Formats and displays item info, checking usability for a target player.
**HandleLookupItemCommand**: Searches item templates by name, respecting locale.
**HandleLookupItemSetCommand**: Searches item set DBC by name.
**HandleLookupSkillCommand**: Searches skill line DBC by name, showing target player's skill values.
**ShowSpellListHelper**: Formats and displays spell info, including rank, type, and target player's knowledge/aura status.
**HandleLookupSpellCommand**: Searches spell DBC by name.
**ShowQuestListHelper**: Formats and displays quest info, including target player's quest status.
**HandleLookupQuestCommand**: Searches quest templates by name.
**HandleLookupCreatureCommand**: Searches creature templates by name.
**HandleLookupCreatureModelCommand**: Searches creatures by model ID/name, with optional SQL export.
**HandleLookupObjectCommand**: Searches game object templates by name.
**HandleLookupTaxiNodeCommand**: Searches taxi nodes by name.
**HandleLookupAccountEmailCommand**: Searches accounts by email (async).
**ShowAccountIpListHelper**: Helper for IP-based account searches, handling online/offline filters.
**HandleLookupAccountIpCommand**: Searches accounts by IP.
**HandleLookupAccountIponlineCommand**: Searches online accounts by IP.
**HandleLookupAccountNameCommand**: Searches accounts by username.
**HandleLookupPlayerIpCommand**: Finds accounts by IP, then characters.
**HandleLookupPlayerAccountCommand**: Finds accounts by username, then characters.
**HandleLookupPlayerEmailCommand**: Finds accounts by email, then characters.
**HandleLookupPlayerNameCommand**: Searches characters by name (async).
**HandleLookupPlayerCharacterCommand**: Resolves character name to account, checks security, then finds characters.
**LookupPlayerSearchCommand**: Processes account results to fetch character details asynchronously.
**ShowPoolListHelper**: Displays spawn pool details.
**HandleLookupPoolCommand**: Searches pools by description.
**HandlePoolListCommand**: Lists pools spawnable on the current map.
**HandleLookupAreaCommand**: Searches areas by name.
**HandleLookupTeleCommand**: Searches teleports by name.
**HandleLookupGuildCommand**: Retrieves and displays guild info.
**HandleLookupSoundCommand**: Searches sounds by name.
**ShowFactionListHelper**: Formats and displays faction info, including reputation.
**HandleLookupFactionCommand**: Searches factions by name.
**HandleLookupEventCommand**: Searches events by description.
**HandleListClickToMoveCommand**: Lists players who have used Click-to-Move, sorted by level.

---

<!-- machine-true, projected from graph.json -->

## Map — ChatHandler.LookupCommands

*Source:* LookupCommands.cpp, Chat.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| HandleListObjectCommand | method | ChatHandler.Chat/ExtractOptUInt32, ChatHandler.Chat/ExtractUint32KeyFromLink, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Database/PQuery, Field/GetFloat, Field/GetUInt16, Field/GetUInt32, ObjectMgr/GetGameObjectTemplate, QueryResult/Fetch, QueryResult/NextRow, QueryResult/operator[], WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldSession.Main/GetPlayer | — | gameobject |
| HandleListCreatureCommand | method | ChatHandler.Chat/ExtractOptUInt32, ChatHandler.Chat/ExtractUint32KeyFromLink, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Database/PQuery, Field/GetFloat, Field/GetUInt16, Field/GetUInt32, ObjectMgr/GetCreatureTemplate, QueryResult/Fetch, QueryResult/NextRow, QueryResult/operator[], WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldSession.Main/GetPlayer | — | creature |
| ShowItemListHelper | method | ChatHandler.Chat/GetMangosString, ChatHandler.Chat/PSendSysMessage#2, ObjectMgr/GetItemLocale, ObjectMgr/GetItemPrototype, Player.Main/CanUseItem#2 | — | — |
| HandleLookupItemCommand | method | ChatHandler.Chat/GetSessionDbLocaleIndex, ChatHandler.Chat/SendSysMessage#2, ObjectMgr/GetItemLocale, ObjectMgr/GetItemPrototypeMap, shared_Util/Utf8FitTo, shared_Util/Utf8toWStr, shared_Util/wstrToLower, WorldSession.Main/GetPlayer | — | — |
| HandleLookupItemSetCommand | method | ChatHandler.Chat/GetSessionDbcLocale, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, shared_Util/Utf8FitTo, shared_Util/Utf8toWStr, shared_Util/wstrToLower | — | — |
| HandleLookupSkillCommand | method | ChatHandler.Chat/GetMangosString, ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/GetSessionDbcLocale, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, Player.Main/GetSkillBonusPermanent, Player.Main/GetSkillBonusTemporary, Player.Main/GetSkillMaxPure, Player.Main/GetSkillValuePure, Player.Main/HasSkill, shared_Util/Utf8FitTo, shared_Util/Utf8toWStr, shared_Util/wstrToLower | — | — |
| ShowSpellListHelper | method | ChatHandler.Chat/GetMangosString, ChatHandler.Chat/SendSysMessage, DBCStores/GetTalentSpellCost#2, Player.Main/HasSpell, SpellEntry/IsPassiveSpell#2, SpellMgr/GetSpellRank, SpellMgr/Instance, Unit.Main/HasAura#2 | ChatHandler.CharacterCommands/HandleListTalentsCommand, ChatHandler.DebugCommands/HandleSpellEffectsCommand, ChatHandler.DebugCommands/HandleSpellInfosCommand, ChatHandler.DebugCommands/HandleSpellSearchCommand, ChatHandler.ServerCommands/HandleGroupAddSpellCommand | — |
| HandleLookupSpellCommand | method | ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/GetSessionDbcLocale, ChatHandler.Chat/SendSysMessage#2, shared_Util/Utf8FitTo, shared_Util/Utf8toWStr, shared_Util/wstrToLower, SpellMgr/GetMaxSpellId, SpellMgr/GetSpellEntry, SpellMgr/Instance | — | — |
| ShowQuestListHelper | method | ChatHandler.Chat/GetMangosString, ChatHandler.Chat/PSendSysMessage#2, ObjectMgr/GetQuestLocale, ObjectMgr/GetQuestTemplate, Player.Main/GetQuestRewardStatus, Player.Main/GetQuestStatus, QuestDef/GetQuestId, QuestDef/GetQuestLevel, QuestDef/GetTitle | ChatHandler.MiscCommands/HandleTriggerCommand | — |
| HandleLookupQuestCommand | method | ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/GetSessionDbLocaleIndex, ChatHandler.Chat/SendSysMessage#2, ObjectMgr/GetQuestLocale, ObjectMgr/GetQuestTemplates, QuestDef/GetQuestId, QuestDef/GetTitle, shared_Util/Utf8FitTo, shared_Util/Utf8toWStr, shared_Util/wstrToLower | — | — |
| HandleLookupCreatureCommand | method | ChatHandler.Chat/GetSessionDbLocaleIndex, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ObjectMgr/GetCreatureInfoMap, ObjectMgr/GetCreatureLocale, shared_Util/Utf8FitTo, shared_Util/Utf8toWStr, shared_Util/wstrToLower | — | — |
| HandleLookupCreatureModelCommand | method | ChatHandler.Chat/ExtractLiteralArg, ChatHandler.Chat/ExtractOptUInt32, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, Log.Main/Out, ObjectMgr/GetCreatureInfoMap, shared_Util/Utf8FitTo, shared_Util/Utf8toWStr, shared_Util/wstrToLower | — | — |
| HandleLookupObjectCommand | method | ChatHandler.Chat/GetSessionDbLocaleIndex, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ObjectMgr/GetGameObjectInfoMap, ObjectMgr/GetGameObjectLocale, shared_Util/Utf8FitTo, shared_Util/Utf8toWStr, shared_Util/wstrToLower | — | — |
| HandleLookupTaxiNodeCommand | method | ChatHandler.Chat/GetSessionDbcLocale, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ObjectMgr/GetMaxTaxiNodeId, ObjectMgr/GetTaxiNodeEntry, shared_Util/Utf8FitTo, shared_Util/Utf8toWStr, shared_Util/wstrToLower | — | — |
| HandleLookupAccountEmailCommand | method | ChatHandler.Chat/ExtractOptUInt32, ChatHandler.Chat/ExtractQuotedOrLiteralArg, ChatHandler.Chat/GetAccountId, Database/escape_string | — | account |
| ShowAccountIpListHelper | method | ChatHandler.Chat/ExtractOptUInt32, ChatHandler.Chat/ExtractQuotedOrLiteralArg, ChatHandler.Chat/GetAccountId, Database/escape_string | — | account |
| HandleLookupAccountIpCommand | method | — | — | — |
| HandleLookupAccountIponlineCommand | method | — | — | — |
| HandleLookupAccountNameCommand | method | AccountMgr/normalizeString, ChatHandler.Chat/ExtractOptUInt32, ChatHandler.Chat/ExtractQuotedOrLiteralArg, ChatHandler.Chat/GetAccountId, Database/escape_string | — | account |
| HandleLookupPlayerIpCommand | method | ChatHandler.Chat/ExtractOptUInt32, ChatHandler.Chat/ExtractQuotedOrLiteralArg, Database/escape_string, Database/PQuery | — | account |
| HandleLookupPlayerAccountCommand | method | AccountMgr/normalizeString, ChatHandler.Chat/ExtractOptUInt32, ChatHandler.Chat/ExtractQuotedOrLiteralArg, Database/escape_string, Database/PQuery | — | account |
| HandleLookupPlayerEmailCommand | method | ChatHandler.Chat/ExtractOptUInt32, ChatHandler.Chat/ExtractQuotedOrLiteralArg, Database/escape_string, Database/PQuery | — | account |
| HandleLookupPlayerNameCommand | method | ChatHandler.Chat/ExtractOptUInt32, ChatHandler.Chat/ExtractQuotedOrLiteralArg, ChatHandler.Chat/GetAccountId, Database/escape_string | — | characters |
| HandleLookupPlayerCharacterCommand | method | AccountMgr/GetSecurity, ChatHandler.Chat/ExtractOptUInt32, ChatHandler.Chat/ExtractQuotedOrLiteralArg, ChatHandler.Chat/GetAccessLevel, Database/escape_string, Database/PQuery, Field/GetCppString, Field/GetInt32, ObjectMgr/GetPlayerDataByName, ObjectMgr/normalizePlayerName, QueryResult/Fetch | — | account |
| LookupPlayerSearchCommand | method | AsyncCommandHandlers/AddAccountInfo, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Field/GetCppString, Field/GetUInt32, PlayerSearchQueryHolder/PlayerSearchQueryHolder, QueryResult/Fetch, QueryResult/GetRowCount, QueryResult/NextRow, SqlOperations/SetPQuery, SqlOperations/SetSize, WorldSession.Main/GetAccountId | — | characters |
| ShowPoolListHelper | method | ChatHandler.Chat/PSendSysMessage#2, PoolManager/GetPoolCreatures, PoolManager/GetPoolGameObjects, PoolManager/GetPoolPools, PoolManager/GetPoolTemplate, PoolTemplateData/IsAutoSpawn | — | — |
| HandleLookupPoolCommand | method | ChatHandler.Chat/SendSysMessage#2, PoolManager/GetMaxPoolId, PoolManager/GetPoolTemplate, shared_Util/strToLower | — | — |
| HandlePoolListCommand | method | ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Map.Main/GetPersistentState, MapEntry/Instanceable, MapPersistentStateMgr/GetMapEntry, MapPersistentStateMgr/GetMapId, PoolManager/GetMaxPoolId, PoolManager/GetPoolTemplate, PoolTemplateData/CanBeSpawnedAtMap, WorldObject.Object/GetMap, WorldSession.Main/GetPlayer | — | — |
| HandleLookupAreaCommand | method | ChatHandler.Chat/GetSessionDbLocaleIndex, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SendSysMessage#2, ObjectMgr/GetAreaLocaleString, shared_Util/Utf8FitTo, shared_Util/Utf8toWStr, shared_Util/wstrToLower | — | — |
| HandleLookupTeleCommand | method | ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, ObjectMgr/GetGameTeleMap, shared_Util/Utf8toWStr, shared_Util/wstrToLower | — | — |
| HandleLookupGuildCommand | method | ChatHandler.Chat/ExtractQuotedArg, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, game_Guild_Guild/GetAccountsNumber, Guild/GetCreatedDay, Guild/GetCreatedMonth, Guild/GetCreatedYear, Guild/GetGINFO, Guild/GetId, Guild/GetLeaderGuid, Guild/GetMemberSize, Guild/GetMOTD, Guild/GetName, GuildMgr/GetGuildByName, ObjectMgr/GetPlayerNameByGUID | — | — |
| HandleLookupSoundCommand | method | ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ObjectMgr/GetSoundEntriesMap, shared_Util/strToLower | — | — |
| ShowFactionListHelper | method | ChatHandler.Chat/GetMangosString, ChatHandler.Chat/SendSysMessage, Player.Main/GetReputationMgr, ReputationMgr/GetRank, ReputationMgr/GetReputation | ChatHandler.CharacterCommands/HandleCharacterReputationCommand | — |
| HandleLookupFactionCommand | method | ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/GetSessionDbcLocale, ChatHandler.Chat/SendSysMessage#2, ObjectMgr/GetFactionMap, Player.Main/GetReputationMgr, ReputationMgr/GetState, shared_Util/Utf8FitTo, shared_Util/Utf8toWStr, shared_Util/wstrToLower | — | — |
| HandleLookupEventCommand | method | ChatHandler.Chat/GetMangosString, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, GameEventMgr.Main/GetEventMap, GameEventMgr.Main/IsActiveEvent, GameEventMgr.Main/IsValidEvent, shared_Util/Utf8FitTo, shared_Util/Utf8toWStr, shared_Util/wstrToLower | — | — |
| HandleListClickToMoveCommand | method | ChatHandler.Chat/GetNameLink, ChatHandler.Chat/playerLink, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, Object/IsInWorld, ObjectAccessor/GetPlayers, Player.Main/GetSession, Unit.Main/GetLevel, WorldSession.Main/GetRemoteAddress, WorldSession.Main/HasUsedClickToMove | — | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `account`: id int(11) unsigned PK, username varchar(32), gmlevel tinyint(3) unsigned, sessionkey longtext?, v longtext?, s longtext?, token_key varchar(100), email text?, joindate timestamp, last_ip varchar(30), failed_logins int(11) unsigned, locked tinyint(3) unsigned, lock_country varchar(2), last_login timestamp, online tinyint(4), expansion tinyint(3) unsigned, mutetime bigint(40), locale tinyint(3) unsigned, os varchar(4), platform varchar(4), current_realm tinyint(3) unsigned, flags int(10) unsigned, security varchar(255)?, email_verif tinyint(1), geolock_pin int(11)?
- `characters`: guid int(11) unsigned PK, account int(11) unsigned, name varchar(12), race tinyint(3) unsigned, class tinyint(3) unsigned, gender tinyint(3) unsigned, skin tinyint(3) unsigned, face tinyint(3) unsigned, hair_style tinyint(3) unsigned, hair_color tinyint(3) unsigned, facial_hair tinyint(3) unsigned, level tinyint(3) unsigned, xp int(10) unsigned, money int(10) unsigned, character_flags int(10) unsigned, zone int(11) unsigned, map int(11) unsigned, instance int(11) unsigned, position_x float, position_y float, position_z float, orientation float, transport_guid bigint(20) unsigned, transport_x float, transport_y float, transport_z float, transport_o float, known_taxi_mask longtext?, current_taxi_path text?, online tinyint(3) unsigned, played_time_total int(11) unsigned, played_time_level int(11) unsigned, create_time bigint(20) unsigned, logout_time bigint(20) unsigned, rest_bonus float, reset_talents_multiplier int(11) unsigned, reset_talents_time bigint(20) unsigned, death_expire_time bigint(20) unsigned, stable_slots tinyint(1) unsigned, bank_bag_slots tinyint(1) unsigned, extra_flags int(11) unsigned, honor_rank_points float, honor_highest_rank int(11) unsigned, honor_standing int(11) unsigned, honor_last_week_hk int(11) unsigned, honor_last_week_cp float, honor_stored_hk int(11), honor_stored_dk int(11), watched_faction int(11), drunk smallint(5) unsigned, health int(10) unsigned, power1 int(10) unsigned, power2 int(10) unsigned, power3 int(10) unsigned, power4 int(10) unsigned, power5 int(10) unsigned, explored_zones longtext?, equipment_cache longtext?, ammo_id int(10) unsigned, action_bars tinyint(3) unsigned, deleted_account int(11) unsigned?, deleted_name varchar(12)?, deleted_time bigint(20)?, world_phase_mask int(11)?
- `creature`: guid int(10) unsigned PK, id mediumint(8) unsigned, id2 mediumint(8) unsigned, id3 mediumint(8) unsigned, id4 mediumint(8) unsigned, id5 mediumint(8) unsigned, map smallint(5) unsigned, position_x float, position_y float, position_z float, orientation float, spawntimesecsmin int(10) unsigned, spawntimesecsmax int(10) unsigned, wander_distance float, health_percent float, mana_percent float unsigned, movement_type tinyint(3) unsigned, spawn_flags int(10) unsigned, visibility_mod float?, patch_min tinyint(3) unsigned, patch_max tinyint(3) unsigned
- `gameobject`: guid int(10) unsigned PK, id mediumint(8) unsigned, map smallint(5) unsigned, position_x float, position_y float, position_z float, orientation float, rotation0 float, rotation1 float, rotation2 float, rotation3 float, spawntimesecsmin int(11), spawntimesecsmax int(11), animprogress tinyint(3) unsigned, state tinyint(3) unsigned, spawn_flags int(10) unsigned, visibility_mod float?, patch_min tinyint(3) unsigned, patch_max tinyint(3) unsigned

*`?` = nullable, `PK` = primary key column.*


---

<!-- verify: boundary-bleed | foreign: ChatHandler -->
