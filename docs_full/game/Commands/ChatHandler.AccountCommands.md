<!-- provenance: boundary-bleed -->
# ChatHandler.AccountCommands

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ChatHandler.AccountCommands

## Purpose & Responsibilities

This unit implements the administrative account management and moderation commands for the `ChatHandler` class in the WoWVMaNGOS server. It provides Game Masters (GMs) and console operators with tools to manage user accounts, including creation, deletion, security level modification, password resets, and locking mechanisms.

Beyond basic account administration, this unit handles critical moderation features:
*   **Banning System:** Comprehensive support for banning accounts, characters, and IP addresses, including bulk IP bans, unbanning, and viewing ban history/lists.
*   **Moderation Actions:** Warning players, adding notes to accounts, kicking players from the world, and muting/unmuting chat capabilities.
*   **Anti-Cheat Integration:** Interfaces with the anti-cheat and anti-spam modules to mute spammers and view cheat reports.
*   **Debugging Tools:** Enables packet sniffing for specific players to diagnose network issues.

The unit operates primarily through direct database queries (`LoginDatabase`, `CharacterDatabase`) and interactions with the `AccountMgr` and `World` singletons. It enforces security hierarchies, ensuring that lower-level GMs cannot modify higher-level accounts.

## Member-by-Member Behavior

### Account Lifecycle & Configuration

These members handle the creation, deletion, and configuration of user accounts.

*   **HandleAccountCommand**: Displays the current access level (GM level) of the invoking handler. It relies on `ChatHandler.Chat/GetAccessLevel` to determine the level and `ChatHandler.Chat/PSendSysMessage` to display it.
*   **HandleAccountCreateCommand**: Creates a new account. It extracts the username and password, validates them, and delegates the actual creation to `AccountMgr/CreateAccount`. It handles various error states (name too long, already exists, DB error) via `AccountOpResult`.
*   **HandleAccountDeleteCommand**: Deletes an account. It verifies the target account has lower security than the invoker using `ChatHandler.Chat/HasLowerSecurityAccount`. It delegates deletion to `AccountMgr/DeleteAccount`. Note: The source contains a TODO comment indicating this function should ideally handle realm-specific character deletion more robustly, but currently relies on `AccountMgr` to handle the cleanup.
*   **HandleAccountSetPasswordCommand**: Changes a target account's password. It requires two matching password arguments. It checks security levels via `ChatHandler.Chat/HasLowerSecurityAccount` and delegates the change to `AccountMgr/ChangePassword`.
*   **HandleAccountPasswordCommand**: Allows a user to change their *own* password. It requires the old password for verification via `AccountMgr/CheckPassword` and the new password (confirmed twice). It uses `WorldSession.Main/GetUsername` to identify the current user.
*   **HandleAccountSetGmLevelCommand**: Modifies the security level (GM level) of a target account. It enforces strict hierarchy: the invoker must have a higher security level than the target, and the new level must be lower than the invoker's level. It updates the session security via `WorldSession.Main/SetSecurity` if the target is online, and persists the change via `AccountMgr/SetSecurity`.
*   **HandleAccountSetAddonCommand**: Sets the expansion level (addon) for an account. It writes directly to the `account` table in the `LoginDatabase` using `Database/PExecute`. It restricts usage to accounts with lower security or the self-account.
*   **HandleAccountSetLockedCommand**: Sets the locked status of an account. It writes directly to the `account` table in the `LoginDatabase`. It restricts usage to accounts with lower security or the self-account.
*   **HandleAccountLockCommand**: Locks or unlocks the *invoker's* own account. It checks if the command is coming from a Remote Admin (RA) session (not console) via `ChatHandler.Chat/GetAccountId`. It writes directly to the `account` table.
*   **HandleAccountClearDataCommand**: Clears saved account data (variables) for the invoker's account. It executes `DELETE` statements on `account_data` and `character_account_data` tables in the `CharacterDatabase`.

### Account Information & Listing

*   **HandleAccountCharactersCommand**: Lists characters associated with an account. It triggers an asynchronous query on the `CharacterDatabase` to select `guid`, `name`, `race`, `class`, and `level` from the `characters` table. The results are handled by `PlayerSearchHandler::HandlePlayerCharacterLookupResult` (external).
*   **HandleAccountOnlineListCommand**: Lists accounts currently logged into the realm. It queries the `account` table in `LoginDatabase` for records where `current_realm` matches the server ID. It delegates the formatting and display of results to `AsyncCommandHandlers/ShowAccountListHelper`.

### Moderation: Warnings, Notes, and Kicks

*   **HandleWarnCharacterCommand**: Issues a warning to a player's account. It identifies the player via `ChatHandler.Chat/ExtractPlayerTarget`, retrieves cached player data via `ObjectMgr/GetPlayerDataByGUID`, and logs the warning via `World/WarnAccount` and `AccountMgr/WarnAccount`. If the player is online, it sends a system message via `Player.Main/PSendSysMessage`.
*   **HandleAddCharacterNoteCommand**: Adds a note to a player's account. Similar to warnings, it uses `ObjectMgr/GetPlayerDataByGUID` to find the account ID and logs the note via `World/WarnAccount` with the type "NOTE".
*   **HandleKickPlayerCommand**: Disconnects a player from the world. It prevents self-kick. It checks security hierarchy via `ChatHandler.Chat/HasLowerSecurity`. Depending on whether the target is connected, it calls `WorldSession.Main/KickPlayer` or `WorldSession.Main/KickDisconnectedFromWorld`.

### Banning System

This subsystem is extensive, handling bans for accounts, characters, and IPs.

*   **HandleBanAccountCommand**, **HandleBanCharacterCommand**, **HandleBanIPCommand**: Thin wrappers that delegate to `HandleBanHelper` with the appropriate `BanMode`.
*   **HandleBanHelper**: The core logic for issuing bans. It parses the target (account name, character name, or IP), duration, and reason. It normalizes strings using `AccountMgr/normalizeString` or `ObjectMgr/normalizePlayerName` and validates IPs using `shared_Util/IsIPAddress`. It delegates the actual ban execution to `World/BanAccount`.
*   **HandleBanAllIPCommand**: A specialized command to ban all accounts associated with a specific IP address, excluding those with high-level characters (above level 10). It queries `account` and `characters` tables. It iterates through results, checking if the account is already banned via `AccountMgr/IsAccountBanned`, and bans them via `World/BanAccount`.
*   **HandleUnBanAccountCommand**, **HandleUnBanCharacterCommand**, **HandleUnBanIPCommand**: Thin wrappers that delegate to `HandleUnBanHelper`.
*   **HandleUnBanHelper**: Core logic for removing bans. It parses the target and an optional message. It normalizes/validates the target and delegates removal to `World/RemoveBanAccount`.
*   **SendBanResult**: Formats and sends the result of a ban operation to the invoker. It converts duration seconds to a time string using `shared_Util/secsToTimeString`. This method is called by `World/HandleAccountSelectResult` (external).
*   **HandleBanInfoAccountCommand**, **HandleBanInfoCharacterCommand**: Retrieve ban history for an account. `HandleBanInfoCharacterCommand` resolves the character name to an account ID using `ObjectMgr/GetPlayerAccountIdByGUID` or `WorldSession.Main/GetAccountId`. Both delegate to `HandleBanInfoHelper`.
*   **HandleBanInfoHelper**: Queries the `account_banned` table joined with `realmlist` to display ban history. It calculates active status and permanent status based on `bandate` and `unbandate`. It hides ban reasons from GMs with insufficient security levels.
*   **HandleBanInfoIPCommand**: Retrieves ban information for a specific IP address from the `ip_banned` table.
*   **HandleBanListAccountCommand**, **HandleBanListCharacterCommand**, **HandleBanListIPCommand**: List currently active bans. They first clean up expired IP bans from the `ip_banned` table. They query the respective tables (`account_banned`, `characters`, `ip_banned`) and delegate the display formatting to `HandleBanListHelper` or inline logic for IPs.
*   **HandleBanListHelper**: Formats the list of banned accounts. It distinguishes between chat output (short) and console output (wide table format). It performs additional queries to fetch ban details (`bandate`, `unbandate`, etc.) for each account in the list.

### Anti-Cheat & Spam Control

*   **HandleAnticheatCommand**: Generates a cheat report for a selected player. It retrieves cheat data via `Player.Main/GetCheatData` and delegates command handling to `MovementAnticheat/HandleCommand`.
*   **HandleSpamerMute**, **HandleSpamerUnmute**, **HandleSpamerList**: Interface with the anti-spam module. They locate the player via `ObjectAccessor/FindPlayerByName`, retrieve the `AntispamInterface` via `Anticheat/GetAntispam`, and call `mute`, `unmute`, or `showMuted`.

### Chat Muting

*   **HandleMuteCommand**: Mutes a player for a specified duration. It updates the `mutetime` column in the `account` table. If the player is online, it applies a visual aura (`SPELL_PLAYER_MUTED_VISUAL`) via `Unit.Main/AddAura` and sets the session mute time. It logs the action via `World/WarnAccount`.
*   **HandleUnmuteCommand**: Removes a mute. It resets `mutetime` in the `account` table. If the player is online, it removes the visual aura via `Unit.Main/RemoveAurasDueToSpell` and resets the session mute time.

### Debugging

*   **HandleSniffCommand**: Enables or disables packet sniffing for a selected player. It checks if the player is connected via `WorldSession.Main/IsConnected` and calls `WorldSession.Main/StartSniffing` or `WorldSession.Main/StopSniffing`.

## Cross-Unit Boundaries

*   **ChatHandler.Chat**: Heavily utilized for argument parsing (`ExtractAccountId`, `ExtractQuotedOrLiteralArg`, etc.), security checks (`HasLowerSecurityAccount`, `GetAccessLevel`), and messaging (`PSendSysMessage`, `SendSysMessage`).
*   **AccountMgr**: Central authority for account operations. Called for `CreateAccount`, `DeleteAccount`, `ChangePassword`, `CheckPassword`, `SetSecurity`, `WarnAccount`, `IsAccountBanned`, and `GetName`.
*   **World**: Used for global actions like `BanAccount`, `RemoveBanAccount`, `WarnAccount`, and `FindSession`.
*   **Database**: Direct SQL execution via `LoginDatabase` and `CharacterDatabase` for updates (`PExecute`) and queries (`PQuery`, `Query`). Tables accessed include `account`, `account_banned`, `ip_banned`, `characters`, `account_data`, `character_account_data`, and `realmlist`.
*   **WorldSession.Main**: Interacts with online sessions to get/set security, kick players, start/stop sniffing, and retrieve player/account IDs.
*   **Player.Main**: Used to send messages to online players, get cheat data, and manage auras.
*   **ObjectMgr**: Used to resolve player GUIDs to account IDs and retrieve cached player data.
*   **AsyncCommandHandlers**: Used for asynchronous display of online account lists.
*   **Anticheat/AntispamInterface**: Used for spam control features.
*   **MovementAnticheat**: Used for generating cheat reports.

## Data Model

This unit interacts with the following database tables:

*   **`account`**: Primary table for user accounts. Columns used: `id`, `username`, `gmlevel`, `expansion`, `locked`, `mutetime`, `current_realm`, `last_ip`.
*   **`account_banned`**: Stores ban records. Columns used: `id`, `bandate`, `unbandate`, `active`, `banreason`, `bannedby`, `realm`, `gmlevel`.
*   **`ip_banned`**: Stores IP ban records. Columns used: `ip`, `bandate`, `unbandate`, `bannedby`, `banreason`.
*   **`characters`**: Used to list characters per account and resolve character names to account IDs. Columns used: `guid`, `name`, `account`, `level`, `race`, `class`.
*   **`account_data`**: Stores saved account variables. Cleared by `HandleAccountClearDataCommand`.
*   **`character_account_data`**: Stores character-specific account data. Cleared by `HandleAccountClearDataCommand`.
*   **`realmlist`**: Joined with `account_banned` to display realm names in ban history. Column used: `name`.

## Notable Implementation Details

*   **Security Hierarchy Enforcement**: Most account modification commands strictly enforce that the invoker must have a higher security level than the target. `HandleAccountSetGmLevelCommand` adds an extra check that the new level must be lower than the invoker's level, preventing a GM from promoting themselves or peers to their own level.
*   **Direct Database Updates**: Several commands (`HandleAccountSetAddonCommand`, `HandleAccountSetLockedCommand`, `HandleAccountLockCommand`, `HandleMuteCommand`, `HandleUnmuteCommand`) perform direct `UPDATE` queries on the `account` table rather than using `AccountMgr` methods. This bypasses potential caching or validation layers in `AccountMgr`.
*   **Bulk IP Ban Logic**: `HandleBanAllIPCommand` implements a heuristic to avoid banning legitimate players. It excludes accounts that have any character above level 10. This logic is hardcoded and queries the `characters` table to filter out high-level accounts from the ban list derived from the `account` table.
*   **Ban History Visibility**: In `HandleBanInfoHelper`, ban reasons are hidden from GMs whose security level is lower than the `gmlevel` recorded in the `account_banned` table for that specific ban entry. This allows for sensitive ban reasons to be restricted to higher authorities.
*   **Mute Aura**: Muting a player involves both a database update (`mutetime`) and a runtime aura (`SPELL_PLAYER_MUTED_VISUAL`). The aura duration is calculated in milliseconds (`notspeaktime * MINUTE * IN_MILLISECONDS`). Unmuting removes this aura.
*   **Expired Ban Cleanup**: The ban list commands (`HandleBanListAccountCommand`, etc.) proactively delete expired entries from the `ip_banned` table before querying. This keeps the table clean but means the cleanup happens lazily upon listing, not automatically.
*   **Self-Modification Restrictions**: `HandleAccountLockCommand` and `HandleAccountPasswordCommand` explicitly check if the command is invoked from a console (where `GetAccountId()` returns 0/null) and reject it, forcing these actions to be performed via an in-game session.

## Member Reference

*   **HandleAccountCommand**: Displays the invoker's current access level.
*   **HandleAccountSetAddonCommand**: Sets the expansion level for a target account via direct DB update.
*   **HandleAccountSetGmLevelCommand**: Changes the security level of a target account, enforcing strict hierarchy.
*   **HandleAccountSetPasswordCommand**: Changes a target account's password after verifying security and matching inputs.
*   **HandleAccountSetLockedCommand**: Sets the locked status of a target account via direct DB update.
*   **HandleAccountCharactersCommand**: Triggers an async query to list characters for an account.
*   **HandleAccountClearDataCommand**: Deletes saved account data for the invoker from `account_data` and `character_account_data`.
*   **HandleAccountCreateCommand**: Creates a new account via `AccountMgr`.
*   **HandleAccountDeleteCommand**: Deletes an account via `AccountMgr`, enforcing security hierarchy.
*   **HandleAccountOnlineListCommand**: Lists online accounts by querying `account` table and delegating display.
*   **HandleAccountLockCommand**: Locks/unlocks the invoker's own account via direct DB update.
*   **HandleAccountPasswordCommand**: Allows the invoker to change their own password after verifying the old one.
*   **HandleAddCharacterNoteCommand**: Adds a note to a player's account via `World/WarnAccount`.
*   **HandleWarnCharacterCommand**: Issues a warning to a player's account via `World/WarnAccount` and `AccountMgr/WarnAccount`.
*   **HandleKickPlayerCommand**: Disconnects a target player from the world.
*   **HandleBanAccountCommand**: Wrapper for `HandleBanHelper` with `BAN_ACCOUNT` mode.
*   **HandleBanCharacterCommand**: Wrapper for `HandleBanHelper` with `BAN_CHARACTER` mode.
*   **HandleBanIPCommand**: Wrapper for `HandleBanHelper` with `BAN_IP` mode.
*   **HandleBanAllIPCommand**: Bans all accounts associated with an IP, excluding those with high-level characters.
*   **HandleBanHelper**: Core logic for issuing bans, validating targets, and delegating to `World/BanAccount`.
*   **SendBanResult**: Formats and displays the result of a ban operation.
*   **HandleUnBanAccountCommand**: Wrapper for `HandleUnBanHelper` with `BAN_ACCOUNT` mode.
*   **HandleUnBanCharacterCommand**: Wrapper for `HandleUnBanHelper` with `BAN_CHARACTER` mode.
*   **HandleUnBanIPCommand**: Wrapper for `HandleUnBanHelper` with `BAN_IP` mode.
*   **HandleUnBanHelper**: Core logic for removing bans, delegating to `World/RemoveBanAccount`.
*   **HandleBanInfoAccountCommand**: Retrieves ban history for an account by ID/name.
*   **HandleBanInfoCharacterCommand**: Resolves character to account and retrieves ban history.
*   **HandleBanInfoHelper**: Queries `account_banned` and `realmlist` to display detailed ban history.
*   **HandleBanInfoIPCommand**: Retrieves ban information for a specific IP from `ip_banned`.
*   **HandleBanListCharacterCommand**: Lists banned characters, cleaning expired IP bans first.
*   **HandleBanListAccountCommand**: Lists banned accounts, cleaning expired IP bans first.
*   **HandleBanListHelper**: Formats and displays the list of banned accounts.
*   **HandleBanListIPCommand**: Lists banned IPs, cleaning expired bans first.
*   **HandleAnticheatCommand**: Generates a cheat report for a selected player.
*   **HandleSpamerMute**: Mutes a player via the anti-spam interface.
*   **HandleSpamerUnmute**: Unmutes a player via the anti-spam interface.
*   **HandleSpamerList**: Lists muted players via the anti-spam interface.
*   **HandleMuteCommand**: Mutes a player by updating DB and applying a visual aura.
*   **HandleUnmuteCommand**: Unmutes a player by updating DB and removing the visual aura.
*   **HandleSniffCommand**: Enables/disables packet sniffing for a selected player.

---

<!-- machine-true, projected from graph.json -->

## Map — ChatHandler.AccountCommands

*Source:* AccountCommands.cpp, Chat.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| HandleAccountCommand | method | ChatHandler.Chat/GetAccessLevel, ChatHandler.Chat/PSendSysMessage#2 | — | — |
| HandleAccountSetAddonCommand | method | ChatHandler.Chat/ExtractAccountId, ChatHandler.Chat/ExtractOptNotLastArg, ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/GetAccountId, ChatHandler.Chat/HasLowerSecurityAccount, ChatHandler.Chat/PSendSysMessage#2, Database/PExecute#2 | — | account |
| HandleAccountSetGmLevelCommand | method | AccountMgr/SetSecurity, ChatHandler.Chat/ExtractAccountId, ChatHandler.Chat/ExtractInt32, ChatHandler.Chat/ExtractOptNotLastArg, ChatHandler.Chat/GetAccessLevel, ChatHandler.Chat/GetAccountId, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/HasLowerSecurityAccount, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/GetSession, Player.Main/PSendSysMessage#2, WorldSession.Main/SetSecurity | — | — |
| HandleAccountSetPasswordCommand | method | AccountMgr/ChangePassword, ChatHandler.Chat/ExtractAccountId, ChatHandler.Chat/ExtractQuotedOrLiteralArg, ChatHandler.Chat/HasLowerSecurityAccount, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage | — | — |
| HandleAccountSetLockedCommand | method | ChatHandler.Chat/ExtractAccountId, ChatHandler.Chat/ExtractOptNotLastArg, ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/GetAccountId, ChatHandler.Chat/HasLowerSecurityAccount, ChatHandler.Chat/PSendSysMessage#2, Database/PExecute#2 | — | account |
| HandleAccountCharactersCommand | method | ChatHandler.Chat/ExtractAccountId, ChatHandler.Chat/GetAccountId | — | characters |
| HandleAccountClearDataCommand | method | ChatHandler.Chat/GetAccountId, ChatHandler.Chat/SendSysMessage, Database/PExecute#2 | — | account_data, characters, character_account_data |
| HandleAccountCreateCommand | method | AccountMgr/CreateAccount, ChatHandler.Chat/ExtractQuotedOrLiteralArg, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage | — | — |
| HandleAccountDeleteCommand | method | AccountMgr/DeleteAccount, ChatHandler.Chat/ExtractAccountId, ChatHandler.Chat/HasLowerSecurityAccount, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage | — | — |
| HandleAccountOnlineListCommand | method | AsyncCommandHandlers/ShowAccountListHelper, ChatHandler.Chat/ExtractOptUInt32, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Database/PQuery | — | account |
| HandleAccountLockCommand | method | ChatHandler.Chat/ExtractOnOff, ChatHandler.Chat/GetAccountId, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Database/PExecute#2 | — | account |
| HandleAccountPasswordCommand | method | AccountMgr/ChangePassword, AccountMgr/CheckPassword, ChatHandler.Chat/ExtractQuotedOrLiteralArg, ChatHandler.Chat/GetAccountId, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, WorldSession.Main/GetUsername | — | — |
| HandleAddCharacterNoteCommand | method | ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/ExtractQuotedOrLiteralArg, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, ObjectGuid/GetCounter, ObjectGuid/ObjectGuid, ObjectMgr/GetPlayerDataByGUID, World/WarnAccount, WorldSession.Main/GetPlayerName | — | — |
| HandleWarnCharacterCommand | method | AccountMgr/WarnAccount, ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/ExtractQuotedOrLiteralArg, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, ObjectGuid/GetCounter, ObjectGuid/ObjectGuid, ObjectMgr/GetPlayerDataByGUID, Player.Main/PSendSysMessage#2, World/WarnAccount, WorldSession.Main/GetPlayerName | — | — |
| HandleKickPlayerCommand | method | ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/GetNameLink, ChatHandler.Chat/HasLowerSecurity, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/GetSession, WorldSession.Main/GetPlayer, WorldSession.Main/IsConnected, WorldSession.Main/KickDisconnectedFromWorld, WorldSession.Main/KickPlayer | — | — |
| HandleBanAccountCommand | method | — | — | — |
| HandleBanCharacterCommand | method | — | — | — |
| HandleBanIPCommand | method | — | — | — |
| HandleBanAllIPCommand | method | AccountMgr/IsAccountBanned, ChatHandler.Chat/ExtractQuotedOrLiteralArg, ChatHandler.Chat/GetAccessLevel, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SetSentErrorMessage, Database/escape_string, Database/PQuery, Field/GetCppString, Field/GetUInt32, QueryResult/Fetch, QueryResult/NextRow, World/BanAccount, WorldSession.Main/GetPlayerName | — | account, characters |
| HandleBanHelper | method | AccountMgr/normalizeString, ChatHandler.Chat/ExtractArg, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, ObjectMgr/normalizePlayerName, shared_Util/IsIPAddress, shared_Util/TimeStringToSecs, World/BanAccount, WorldSession.Main/GetPlayerName | — | — |
| SendBanResult | method | ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, shared_Util/secsToTimeString | World/HandleAccountSelectResult | — |
| HandleUnBanAccountCommand | method | — | — | — |
| HandleUnBanCharacterCommand | method | — | — | — |
| HandleUnBanIPCommand | method | — | — | — |
| HandleUnBanHelper | method | AccountMgr/normalizeString, ChatHandler.Chat/ExtractArg, ChatHandler.Chat/ExtractQuotedOrLiteralArg, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, ObjectMgr/normalizePlayerName, shared_Util/IsIPAddress, World/RemoveBanAccount, WorldSession.Main/GetPlayerName | — | — |
| HandleBanInfoAccountCommand | method | ChatHandler.Chat/ExtractAccountId | — | — |
| HandleBanInfoCharacterCommand | method | AccountMgr/GetName, ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/PSendSysMessage#2, ObjectGuid/ObjectGuid, ObjectMgr/GetPlayerAccountIdByGUID, Player.Main/GetSession, WorldSession.Main/GetAccountId | — | — |
| HandleBanInfoHelper | method | ChatHandler.Chat/GetAccessLevel, ChatHandler.Chat/GetMangosString, ChatHandler.Chat/PSendSysMessage#2, Database/PQuery, Field/GetBool, Field/GetCppString, Field/GetString, Field/GetUInt64, Field/GetUInt8, QueryResult/Fetch, QueryResult/NextRow, shared_Util/secsToTimeString | — | account_banned, realmlist |
| HandleBanInfoIPCommand | method | ChatHandler.Chat/ExtractQuotedOrLiteralArg, ChatHandler.Chat/GetMangosString, ChatHandler.Chat/PSendSysMessage#2, Database/escape_string, Database/PQuery, Field/GetString, Field/GetUInt64, QueryResult/Fetch, shared_Util/IsIPAddress, shared_Util/secsToTimeString | — | ip_banned |
| HandleBanListCharacterCommand | method | ChatHandler.Chat/ExtractLiteralArg, ChatHandler.Chat/PSendSysMessage#2, Database/escape_string, Database/Execute#2, Database/PQuery | — | characters, ip_banned |
| HandleBanListAccountCommand | method | ChatHandler.Chat/ExtractLiteralArg, ChatHandler.Chat/PSendSysMessage#2, Database/escape_string, Database/Execute#2, Database/PQuery, Database/Query | — | account, ip_banned |
| HandleBanListHelper | method | AccountMgr/GetName, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SendSysMessage#2, Database/PQuery, Field/GetCppString, Field/GetString, Field/GetUInt32, Field/GetUInt64, QueryResult/Fetch, QueryResult/GetFieldCount, QueryResult/NextRow | — | account, account_banned |
| HandleBanListIPCommand | method | ChatHandler.Chat/ExtractLiteralArg, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SendSysMessage#2, Database/escape_string, Database/Execute#2, Database/PQuery, Database/Query, Field/GetString, Field/GetUInt64, QueryResult/Fetch, QueryResult/NextRow | — | ip_banned |
| HandleAnticheatCommand | method | ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/PSendSysMessage, MovementAnticheat/HandleCommand, Object/GetGUIDLow, Player.Main/GetCheatData, Player.Main/GetName, WorldSession.Main/GetPlayer | — | — |
| HandleSpamerMute | method | Anticheat/GetAnticheatLib, Anticheat/GetAntispam, AntispamInterface/mute, ChatHandler.Chat/ExtractArg, ChatHandler.Chat/PSendSysMessage, ObjectAccessor/FindPlayerByName, Player.Main/GetSession, WorldSession.Main/GetAccountId | — | — |
| HandleSpamerUnmute | method | Anticheat/GetAnticheatLib, Anticheat/GetAntispam, AntispamInterface/unmute, ChatHandler.Chat/ExtractArg, ChatHandler.Chat/PSendSysMessage, ObjectAccessor/FindPlayerByName, Player.Main/GetSession, WorldSession.Main/GetAccountId | — | — |
| HandleSpamerList | method | Anticheat/GetAnticheatLib, Anticheat/GetAntispam, AntispamInterface/showMuted, ChatHandler.Chat/GetSession | — | — |
| HandleMuteCommand | method | ChatHandler.Chat/ExtractOptNotLastArg, ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/ExtractQuotedOrLiteralArg, ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/HasLowerSecurity, ChatHandler.Chat/playerLink, ChatHandler.Chat/PSendSysMessage#2, Database/PExecute#2, Errors/PrintStacktraceAndThrow, ObjectGuid/ObjectGuid, ObjectMgr/GetPlayerAccountIdByGUID, ObjectMgr/GetPlayerDataByGUID, Player.Main/GetSession, Player.Main/PSendSysMessage#2, SpellAuraHolder/SetAuraDuration, Unit.Main/AddAura, Unit.SpellAuras/RefreshHolder, Unit.SpellAuras/SetAuraMaxDuration, World/FindSession, World/WarnAccount, WorldSession.Main/GetAccountId, WorldSession.Main/GetPlayer, WorldSession.Main/GetPlayerName | — | account |
| HandleUnmuteCommand | method | ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/HasLowerSecurity, ChatHandler.Chat/playerLink, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Database/PExecute#2, ObjectGuid/ObjectGuid, ObjectMgr/GetPlayerAccountIdByGUID, Player.Main/CanSpeak, Player.Main/GetSession, Player.Main/PSendSysMessage#2, Unit.Main/RemoveAurasDueToSpell, World/FindSession, WorldSession.Main/GetAccountId, WorldSession.Main/GetPlayer | — | account |
| HandleSniffCommand | method | ChatHandler.Chat/ExtractOnOff, ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/GetName, Player.Main/GetSession, WorldSession.Main/IsConnected, WorldSession.Main/StartSniffing, WorldSession.Main/StopSniffing | — | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `account`: id int(11) unsigned PK, username varchar(32), gmlevel tinyint(3) unsigned, sessionkey longtext?, v longtext?, s longtext?, token_key varchar(100), email text?, joindate timestamp, last_ip varchar(30), failed_logins int(11) unsigned, locked tinyint(3) unsigned, lock_country varchar(2), last_login timestamp, online tinyint(4), expansion tinyint(3) unsigned, mutetime bigint(40), locale tinyint(3) unsigned, os varchar(4), platform varchar(4), current_realm tinyint(3) unsigned, flags int(10) unsigned, security varchar(255)?, email_verif tinyint(1), geolock_pin int(11)?
- `account_banned`: banid bigint(20), id bigint(20) PK, bandate bigint(40) PK, unbandate bigint(40), bannedby varchar(50), banreason varchar(255), active tinyint(4), realm tinyint(4), gmlevel tinyint(4) unsigned
- `account_data`: account int(11) unsigned PK, type int(11) unsigned PK, time bigint(11) unsigned, data longblob
- `character_account_data`: guid int(11) unsigned PK, type int(11) unsigned PK, time bigint(11) unsigned, data longblob
- `characters`: guid int(11) unsigned PK, account int(11) unsigned, name varchar(12), race tinyint(3) unsigned, class tinyint(3) unsigned, gender tinyint(3) unsigned, skin tinyint(3) unsigned, face tinyint(3) unsigned, hair_style tinyint(3) unsigned, hair_color tinyint(3) unsigned, facial_hair tinyint(3) unsigned, level tinyint(3) unsigned, xp int(10) unsigned, money int(10) unsigned, character_flags int(10) unsigned, zone int(11) unsigned, map int(11) unsigned, instance int(11) unsigned, position_x float, position_y float, position_z float, orientation float, transport_guid bigint(20) unsigned, transport_x float, transport_y float, transport_z float, transport_o float, known_taxi_mask longtext?, current_taxi_path text?, online tinyint(3) unsigned, played_time_total int(11) unsigned, played_time_level int(11) unsigned, create_time bigint(20) unsigned, logout_time bigint(20) unsigned, rest_bonus float, reset_talents_multiplier int(11) unsigned, reset_talents_time bigint(20) unsigned, death_expire_time bigint(20) unsigned, stable_slots tinyint(1) unsigned, bank_bag_slots tinyint(1) unsigned, extra_flags int(11) unsigned, honor_rank_points float, honor_highest_rank int(11) unsigned, honor_standing int(11) unsigned, honor_last_week_hk int(11) unsigned, honor_last_week_cp float, honor_stored_hk int(11), honor_stored_dk int(11), watched_faction int(11), drunk smallint(5) unsigned, health int(10) unsigned, power1 int(10) unsigned, power2 int(10) unsigned, power3 int(10) unsigned, power4 int(10) unsigned, power5 int(10) unsigned, explored_zones longtext?, equipment_cache longtext?, ammo_id int(10) unsigned, action_bars tinyint(3) unsigned, deleted_account int(11) unsigned?, deleted_name varchar(12)?, deleted_time bigint(20)?, world_phase_mask int(11)?
- `ip_banned`: ip varchar(32) PK, bandate int(11), unbandate int(11), bannedby varchar(50), banreason varchar(50)
- `realmlist`: id int(11) unsigned PK, name varchar(32), address varchar(32), localAddress varchar(255), localSubnetMask varchar(255), port int(11), icon tinyint(3) unsigned, realmflags tinyint(3) unsigned, timezone tinyint(3) unsigned, allowedSecurityLevel tinyint(3) unsigned, population float unsigned, gamebuild_min int(11) unsigned, gamebuild_max int(11) unsigned, flag tinyint(3) unsigned, realmbuilds varchar(64)

*`?` = nullable, `PK` = primary key column.*


---

<!-- verify: boundary-bleed | foreign: ChatHandler, UPDATE -->
