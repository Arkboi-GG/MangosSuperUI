<!-- provenance: failed-members -->
# AsyncCommandHandlers

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AsyncCommandHandlers

**Purpose & Responsibilities**

`AsyncCommandHandlers` implements the backend logic for asynchronous administrative commands in the WoWVMaNGOS server. Its primary responsibility is to gather and display detailed information about players, accounts, and characters without blocking the main game loop (the "world" thread). It achieves this by dispatching database queries asynchronously and processing results in callbacks, often chaining multiple queries (e.g., character data → mail data → account data). Final output is deferred to the main thread via `World::AddAsyncTask` to ensure thread safety when accessing `WorldSession` objects.

The unit handles four main subsystems:
1.  **Player Information (`/pinfo`)**: Aggregates multi-source data (runtime object state, character database, mail history, account details, Warden anti-cheat data) for a target player.
2.  **Player Search**: Looks up characters by name or account ID and displays lists of matching players.
3.  **Account Search**: Looks up accounts by username and displays lists of associated characters and account metadata.
4.  **Gold Removal**: Safely deducts gold from a player’s character record using database transactions to prevent race conditions.

---

## Member-by-Member Behavior

### Player Information (`PInfoHandler`)

This subsystem implements the `/pinfo` command. It follows a strict chain of asynchronous callbacks to aggregate data from different sources.

*   **`HandlePInfoCommand`**: The entry point, called by `ChatHandler.CharacterCommands/HandlePInfoCommand`. It allocates a `PInfoData` struct to hold aggregated results.
    *   If the target `Player` is online, it immediately populates `PInfoData` with runtime data (race, class, level, money, latency, Warden info) from the `Player` object and its `WorldSession`. It then calls `HandleDataAfterPlayerLookup` to fetch mail gold data.
    *   If the target is offline, it stores the target GUID and name, then dispatches an asynchronous query to the `characters` table to fetch basic stats (`played_time_total`, `level`, `money`, `account`, `race`, `class`). The callback for this query is `HandlePlayerLookupResult`.

*   **`HandlePlayerLookupResult`**: Callback for the offline character lookup. It extracts fields from the `QueryResult` into the `PInfoData` struct. After populating the basic stats, it calls `HandleDataAfterPlayerLookup` to proceed to the next stage (mail gold lookup). If the result is empty (player not found), it deletes the data struct and aborts.

*   **`HandleDataAfterPlayerLookup`**: Prepares for the mail gold lookup. It creates a `SqlQueryHolder` to batch two queries:
    1.  Sum of `money` sent by the target (`sender_guid`).
    2.  Sum of `money` received by the target (`receiver_guid`).
    These queries target the `mail` table. The holder is passed to `CharacterDatabase.DelayQueryHolder` with `HandleDelayedMoneyQuery` as the callback.

*   **`HandleDelayedMoneyQuery`**: Processes the mail gold results. It retrieves the sums for sent/received gold from the `SqlQueryHolder` and stores them in `PInfoData`. It cleans up the holder. Crucially, it then dispatches an **unsafe** asynchronous query to the `account` table to fetch username, IP, login time, locale, and security flags. The callback is `HandleAccountInfoResult`. Note: This uses `AsyncPQueryUnsafe` because the subsequent step requires accessing the caller's session, which is not thread-safe.

*   **`HandleAccountInfoResult`**: Callback for the account lookup. It first verifies that the original GM's session still exists (they might have logged out during the async chain). If valid, it extracts account details from the `QueryResult`. It applies security checks: if the GM's security level is lower than the target's, or if the GM is not an Administrator trying to view a GM's IP, the IP and last login are masked with "-". It marks `m_hasAccount = true` and calls `HandleResponse`.

*   **`HandleResponse`**: The final formatting and output stage. It runs in the world thread (via the unsafe callback mechanism). It resolves race/class names using DBC stores, formats currency (gold/silver/copper), and constructs the final chat messages using `ChatHandler`. It displays:
    *   Account info (username, security, ban status, IP, locale, 2FA status).
    *   Character info (level, playtime, current money, mail gold in/out).
    *   Guild membership (if applicable).
    *   Warden anti-cheat data (clock, fingerprint, hypervisor, renderer, proxifier, click-to-move usage).
    Finally, it deletes the `PInfoData` struct.

### Player Search (`PlayerSearchHandler` & Tasks)

This subsystem handles looking up players by name or account.

*   **`HandlePlayerAccountSearchResult`**: Callback for account-based player searches. It wraps the `SqlQueryHolder` in a `PlayerAccountSearchDisplayTask` and adds it to the world task queue via `World::AddAsyncTask`.

*   **`PlayerAccountSearchDisplayTask::operator()`**: Executes in the world thread. It retrieves the GM's session. If the session is gone, it cleans up and returns. Otherwise, it iterates through the query results in the holder. For each result, it checks security permissions (GM cannot see accounts with higher security, non-admins cannot see GM accounts). It appends "[BANNED]" if applicable and calls `ShowPlayerListHelper` to format the output. It respects a limit on the number of displayed results.

*   **`HandlePlayerCharacterLookupResult`**: Callback for character-based lookups. It wraps the `QueryResult` in a `PlayerCharacterLookupDisplayTask` and adds it to the world task queue.

*   **`PlayerCharacterLookupDisplayTask::operator()`**: Executes in the world thread. It retrieves the GM's session and calls `ShowPlayerListHelper` to display the results.

*   **`ShowPlayerListHelper`**: A utility method that formats the player list output. It prints headers if requested. It iterates through the `QueryResult`, extracting GUID, name, race, class, and level. It looks up race/class names from DBC stores. It checks if the player is online (using `ObjectAccessor::FindPlayerNotInWorld`) and prefixes the name with "*" if online. It sends the formatted line to the chat handler.

### Account Search (`AccountSearchHandler` & Tasks)

This subsystem handles looking up accounts by username.

*   **`HandleAccountLookupResult`**: Callback for account lookups. It wraps the `QueryResult` in an `AccountSearchDisplayTask` and adds it to the world task queue.

*   **`AccountSearchDisplayTask::operator()`**: Executes in the world thread. It retrieves the GM's session and calls `ShowAccountListHelper`.

*   **`ShowAccountListHelper`**: Formats the account list output. It iterates through the `QueryResult`, extracting account ID, username, last IP, and security. It performs security checks similar to player search (masking IPs if the GM lacks sufficient privileges). It checks if the account is currently online and retrieves the connected player's name. It appends "[BANNED]" if the account is banned. It sends the formatted line to the chat handler.

### Gold Removal (`PlayerGoldRemovalHandler`)

*   **`HandleGoldLookupResult`**: Callback for gold removal operations. It is marked as not thread-safe and must be called from an unsafe callback. It retrieves the GM's session. If the session is invalid, it returns. It extracts the previous money amount and GUID from the `QueryResult`. It calculates the new money amount (clamping to 0 if the removal exceeds current funds). It executes a transaction on the `characters` table to update the `money` field. It then sends confirmation messages to the GM showing the previous, removed, and new amounts.

### Helper Classes

*   **`PlayerSearchQueryHolder`**: Extends `SqlQueryHolder` to store additional account information (ID and name) mapped by query index.
    *   **`AddAccountInfo`**: Stores account ID and name for a specific query index. Called by `ChatHandler.LookupCommands/LookupPlayerSearchCommand`.
    *   **`GetAccountInfo`**: Retrieves stored account info for a query index.
    *   **`GetAccountId`** / **`GetLimit`**: Accessors for the holder's metadata.

---

## Cross-Unit Boundaries

*   **`ChatHandler.CharacterCommands/HandlePInfoCommand`** calls **`PInfoHandler::HandlePInfoCommand`** to initiate the player info gathering process.
*   **`ChatHandler.LookupCommands/LookupPlayerSearchCommand`** calls **`PlayerSearchQueryHolder::AddAccountInfo`** to attach account metadata to the query holder before dispatching.
*   **`ChatHandler.AccountCommands/HandleAccountOnlineListCommand`** calls **`AccountSearchHandler::ShowAccountListHelper`** directly for online account listings (bypassing the async task wrapper used for offline lookups).
*   **`World::AddAsyncTask`** is called by various handlers (`HandlePlayerAccountSearchResult`, `HandlePlayerCharacterLookupResult`, `HandleAccountLookupResult`) to schedule display tasks on the main world thread.
*   **`AccountMgr`** methods (`GetSecurity`, `IsAccountBanned`, `IsIPBanned`) are called extensively to enforce permission checks and display ban status.
*   **`ChatHandler`** methods (`PSendSysMessage`, `SendSysMessage`, `playerLink`, `GetMangosString`) are used for all final output formatting and sending.
*   **`DBCStores`** (`GetUnitRaceName`, `GetUnitClassName`) are used to resolve numeric IDs to localized strings.
*   **`GuildMgr::GetPlayerGuild`** is used to retrieve guild information for the target player.
*   **`ObjectAccessor::FindPlayerNotInWorld`** is used to determine online status for listed players.
*   **`Database`** methods (`AsyncPQuery`, `DelayQueryHolder`, `BeginTransaction`, `CommitTransaction`, `PExecute`) are used for all database interactions.

---

## Data Model

The unit interacts with three database tables:

1.  **`characters`**:
    *   Used by `PInfoHandler` to fetch `played_time_total`, `level`, `money`, `account`, `race`, `class` for offline targets.
    *   Used by `PlayerGoldRemovalHandler` to update the `money` field.
    *   Columns accessed: `guid`, `played_time_total`, `level`, `money`, `account`, `race`, `class`.

2.  **`mail`**:
    *   Used by `PInfoHandler` to calculate total gold sent and received by a player.
    *   Queries: `SELECT SUM(money) FROM mail WHERE sender_guid = ...` and `SELECT SUM(money) FROM mail WHERE receiver_guid = ...`.
    *   Columns accessed: `money`, `sender_guid`, `receiver_guid`.

3.  **`account`**:
    *   Used by `PInfoHandler` to fetch `username`, `last_ip`, `last_login`, `locale`, `locked` (security flag) for the target's account.
    *   Columns accessed: `id`, `username`, `last_ip`, `last_login`, `locale`, `locked`.

---

## Notable Implementation Details

*   **Thread Safety & Unsafe Callbacks**: The `PInfoHandler` chain uses `AsyncPQueryUnsafe` for the final account lookup. This is necessary because `HandleAccountInfoResult` needs to access the GM's `WorldSession` to send the response, which is not thread-safe. The comment explicitly warns: "Not threadsafe, executed in unsafe callback." The same applies to `HandleGoldLookupResult`.
*   **Memory Management**: `PInfoData` is allocated with `new` and manually deleted in `HandleResponse` or early exit paths. `SqlQueryHolder` is also manually managed in some paths (e.g., `HandleDelayedMoneyQuery` deletes it after use). Care must be taken to ensure all paths delete these structs to avoid leaks.
*   **Security Checks**: IP addresses are masked if the viewing GM has lower security than the target, or if the viewer is not an Administrator and the target is a GM. This logic is duplicated in `HandleAccountInfoResult` and `ShowAccountListHelper`.
*   **Race Condition Prevention in Gold Removal**: `PlayerGoldRemovalHandler::HandleGoldLookupResult` uses `BeginTransaction` and `CommitTransaction` around the `UPDATE` query to ensure atomicity, preventing issues if the player logs in during the operation.
*   **Warden Data**: The `PInfoHandler` collects detailed anti-cheat data from the `Warden` module if the target is online. This includes clock skew, fingerprint, hypervisor detection, renderer info, and proxifier detection.
*   **Limit Handling**: Both player and account search helpers respect a `limit` parameter, breaking out of the loop once the limit is reached.
*   **Locale Handling**: Race and class names are resolved using the GM's session locale. If the locale is invalid (> `LOCALE_esMX`), it defaults to `LOCALE_enUS`.
*   **Shared Pointer Workaround**: `PlayerCharacterLookupDisplayTask` and `AccountSearchDisplayTask` use `std::shared_ptr<std::unique_ptr<QueryResult>>` to manage the query result. The comment notes this is a workaround because the class is not movable when cast to `std::function<void()>`.

---

## Member Reference

*   **HandlePInfoCommand**: Entry point for `/pinfo`. Allocates `PInfoData`, populates with online data or dispatches async query for offline data.
*   **HandlePlayerLookupResult**: Callback for offline character lookup. Populates `PInfoData` with basic stats and chains to mail lookup.
*   **HandleDataAfterPlayerLookup**: Prepares and dispatches async queries for mail gold sent/received.
*   **HandleDelayedMoneyQuery**: Processes mail gold results and dispatches unsafe async query for account info.
*   **HandleAccountInfoResult**: Callback for account lookup. Applies security masks, populates account data, and chains to response handler.
*   **HandleResponse**: Finalizes `/pinfo` output. Formats and sends all gathered data to the GM. Deletes `PInfoData`.
*   **HandlePlayerAccountSearchResult**: Wraps account search results in a display task for the world thread.
*   **operator()#2**: Executes account search display on the world thread. Iterates results, checks security, and calls `ShowPlayerListHelper`.
*   **operator()#3**: Executes character lookup display on the world thread. Calls `ShowPlayerListHelper`.
*   **HandlePlayerCharacterLookupResult**: Wraps character lookup results in a display task for the world thread.
*   **ShowPlayerListHelper**: Utility to format and send player list lines. Handles online status prefix and locale resolution.
*   **AddAccountInfo**: Stores account ID/name in `PlayerSearchQueryHolder`.
*   **GetAccountInfo**: Retrieves stored account ID/name from `PlayerSearchQueryHolder`.
*   **operator()**: Executes account search display on the world thread. Calls `ShowAccountListHelper`.
*   **HandleAccountLookupResult**: Wraps account lookup results in a display task for the world thread.
*   **ShowAccountListHelper**: Utility to format and send account list lines. Handles IP masking and online status.
*   **HandleGoldLookupResult**: Processes gold removal. Updates database within a transaction and sends confirmation messages.

---

<!-- machine-true, projected from graph.json -->

## Map — AsyncCommandHandlers

*Source:* AsyncCommandHandlers.cpp, AsyncCommandHandlers.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| HandlePInfoCommand | method | Object/GetObjectGuid, ObjectGuid/GetCounter, Player.Main/GetMoney, Player.Main/GetSession, Player.Main/GetTotalPlayedTime, Unit.Main/GetClass, Unit.Main/GetLevel, Unit.Main/GetRace, Warden/GetPlayerInfo, Warden/HasUsedClickToMove, WorldSession.Main/GetAccountId, WorldSession.Main/GetLatency, WorldSession.Main/GetSessionDbcLocale, WorldSession.Main/GetWarden | ChatHandler.CharacterCommands/HandlePInfoCommand | characters |
| HandlePlayerLookupResult | method | Field/GetUInt32, Field/GetUInt8, QueryResult/Fetch | — | — |
| HandleDataAfterPlayerLookup | method | ObjectGuid/GetCounter, SqlOperations/SetPQuery, SqlOperations/SetSize, SqlQueryHolder/SqlQueryHolder | — | mail |
| HandleDelayedMoneyQuery | method | Field/GetUInt32, QueryResult/Fetch, SqlOperations/DeleteAllResults, SqlOperations/TakeResult | — | account |
| HandleAccountInfoResult | method | AccountMgr/GetSecurity, Field/GetCppString, Field/GetUInt8, QueryResult/Fetch, World/FindSession, WorldSession.Main/GetPlayer, WorldSession.Main/GetSecurity | — | — |
| HandleResponse | method | AccountMgr/IsAccountBanned, AccountMgr/IsIPBanned, ChatHandler.Chat/ChatHandler#3, ChatHandler.Chat/GetMangosString, ChatHandler.Chat/playerLink, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage, DBCStores/GetUnitClassName, DBCStores/GetUnitRaceName, Guild/GetName, GuildMgr/GetPlayerGuild, ObjectGuid/GetCounter, shared_Util/secsToTimeString, WorldSession.Main/GetSessionDbcLocale | — | — |
| HandlePlayerAccountSearchResult | method | PlayerAccountSearchDisplayTask/PlayerAccountSearchDisplayTask, World/AddAsyncTask | — | — |
| operator()#2 | method | AccountMgr/GetSecurity, AccountMgr/IsAccountBanned, ChatHandler.Chat/ChatHandler#3, ChatHandler.Chat/PSendSysMessage#2, PlayerSearchQueryHolder/GetAccountId, PlayerSearchQueryHolder/GetLimit, SqlOperations/DeleteAllResults, SqlOperations/TakeResult, SqlQueryHolder/GetSize, World/FindSession, WorldSession.Main/GetSecurity | — | — |
| operator()#3 | method | ChatHandler.Chat/ChatHandler#3, ChatHandler.Chat/PSendSysMessage#2, World/FindSession | — | — |
| HandlePlayerCharacterLookupResult | method | PlayerCharacterLookupDisplayTask/PlayerCharacterLookupDisplayTask, World/AddAsyncTask | — | — |
| ShowPlayerListHelper | method | ChatHandler.Chat/GetSession, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, Field/GetCppString, Field/GetUInt32, Field/GetUInt8, ObjectAccessor/FindPlayerNotInWorld, ObjectGuid/ObjectGuid#2, QueryResult/Fetch, QueryResult/NextRow, WorldSession.Main/GetSessionDbcLocale | — | — |
| AddAccountInfo | method | — | ChatHandler.LookupCommands/LookupPlayerSearchCommand | — |
| GetAccountInfo | method | — | — | — |
| operator() | method | ChatHandler.Chat/ChatHandler#3, ChatHandler.Chat/SendSysMessage#2, World/FindSession | — | — |
| HandleAccountLookupResult | method | AccountSearchDisplayTask/AccountSearchDisplayTask, World/AddAsyncTask | — | — |
| ShowAccountListHelper | method | AccountMgr/GetSecurity, AccountMgr/IsAccountBanned, ChatHandler.Chat/GetMangosString, ChatHandler.Chat/GetSession, ChatHandler.Chat/playerLink, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, Field/GetCppString, Field/GetUInt32, Player.Main/GetName, QueryResult/Fetch, QueryResult/NextRow, World/FindSession, WorldSession.Main/GetPlayer, WorldSession.Main/GetSecurity | ChatHandler.AccountCommands/HandleAccountOnlineListCommand | — |
| HandleGoldLookupResult | method | ChatHandler.Chat/ChatHandler#3, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/PSendSysMessage#2, Database/BeginTransaction, Database/CommitTransaction, Database/PExecute#2, Field/GetString, Field/GetUInt32, QueryResult/Fetch, World/FindSession | — | characters |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `account`: id int(11) unsigned PK, username varchar(32), gmlevel tinyint(3) unsigned, sessionkey longtext?, v longtext?, s longtext?, token_key varchar(100), email text?, joindate timestamp, last_ip varchar(30), failed_logins int(11) unsigned, locked tinyint(3) unsigned, lock_country varchar(2), last_login timestamp, online tinyint(4), expansion tinyint(3) unsigned, mutetime bigint(40), locale tinyint(3) unsigned, os varchar(4), platform varchar(4), current_realm tinyint(3) unsigned, flags int(10) unsigned, security varchar(255)?, email_verif tinyint(1), geolock_pin int(11)?
- `characters`: guid int(11) unsigned PK, account int(11) unsigned, name varchar(12), race tinyint(3) unsigned, class tinyint(3) unsigned, gender tinyint(3) unsigned, skin tinyint(3) unsigned, face tinyint(3) unsigned, hair_style tinyint(3) unsigned, hair_color tinyint(3) unsigned, facial_hair tinyint(3) unsigned, level tinyint(3) unsigned, xp int(10) unsigned, money int(10) unsigned, character_flags int(10) unsigned, zone int(11) unsigned, map int(11) unsigned, instance int(11) unsigned, position_x float, position_y float, position_z float, orientation float, transport_guid bigint(20) unsigned, transport_x float, transport_y float, transport_z float, transport_o float, known_taxi_mask longtext?, current_taxi_path text?, online tinyint(3) unsigned, played_time_total int(11) unsigned, played_time_level int(11) unsigned, create_time bigint(20) unsigned, logout_time bigint(20) unsigned, rest_bonus float, reset_talents_multiplier int(11) unsigned, reset_talents_time bigint(20) unsigned, death_expire_time bigint(20) unsigned, stable_slots tinyint(1) unsigned, bank_bag_slots tinyint(1) unsigned, extra_flags int(11) unsigned, honor_rank_points float, honor_highest_rank int(11) unsigned, honor_standing int(11) unsigned, honor_last_week_hk int(11) unsigned, honor_last_week_cp float, honor_stored_hk int(11), honor_stored_dk int(11), watched_faction int(11), drunk smallint(5) unsigned, health int(10) unsigned, power1 int(10) unsigned, power2 int(10) unsigned, power3 int(10) unsigned, power4 int(10) unsigned, power5 int(10) unsigned, explored_zones longtext?, equipment_cache longtext?, ammo_id int(10) unsigned, action_bars tinyint(3) unsigned, deleted_account int(11) unsigned?, deleted_name varchar(12)?, deleted_time bigint(20)?, world_phase_mask int(11)?
- `mail`: id int(11) unsigned PK, message_type tinyint(3) unsigned, stationery tinyint(3), mail_template_id mediumint(8) unsigned, sender_guid int(11) unsigned, receiver_guid int(11) unsigned, subject longtext?, item_text_id int(11) unsigned, has_items tinyint(3) unsigned, expire_time bigint(40), deliver_time bigint(40), money int(11) unsigned, cod int(11) unsigned, checked tinyint(3) unsigned

*`?` = nullable, `PK` = primary key column.*


---

<!-- verify: failed-members | invented: operator -->
