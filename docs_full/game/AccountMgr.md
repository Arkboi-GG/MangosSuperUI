# AccountMgr

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AccountMgr

**Purpose & Responsibilities**

`AccountMgr` is the singleton service responsible for the lifecycle, authentication, and administrative management of user accounts within the WoWVMaNGOS server. It acts as the central authority for:

1.  **Account CRUD Operations:** Creating, deleting, and modifying account credentials (username/password) and metadata (security levels, emails).
2.  **Authentication Verification:** Validating passwords using the SRP6 (Secure Remote Password) protocol and SHA1 hashing.
3.  **Access Control & Bans:** Managing IP and account-level bans, warnings, and security levels (GM permissions).
4.  **Anti-Spam/Abuse Tracking:** Monitoring whisper frequencies and mail sending rates to detect flooding behavior.
5.  **Instance Reset Tracking:** Enforcing limits on how frequently an account can reset instances.

It maintains in-memory caches for ban lists, account persistent data (security, email, username), and anti-spam metrics to avoid excessive database queries during runtime operations.

---

## Member-by-Member Behavior

### Account Lifecycle & Credentials

*   **`CreateAccount`**: Validates username length (max 16 chars UTF-8), normalizes the string, and checks for existence via `GetId`. It generates SRP6 salt and verifier values from the SHA1 hash of `username:password`. It inserts the new record into the `account` table and updates `realmcharacters` to ensure the new account has a character count entry. Returns `AOR_OK` on success or specific error codes (e.g., `AOR_NAME_TOO_LONG`, `AOR_NAME_ALREDY_EXIST`).
*   **`DeleteAccount`**: Verifies the account exists. It iterates through all characters associated with the account in the `characters` table, kicking any online players (`ObjectAccessor::KickPlayer`) and deleting their data from the database (`Player::DeleteFromDB`). It cleans up `character_tutorial` data and then deletes the account from `account` and `realmcharacters` tables within a transaction.
*   **`ChangeUsername`**: *(Deprecated)* Updates the username and password for an existing account. It recalculates SRP6 values and updates the `account` table. It also updates the in-memory `AccountPersistentData` cache.
*   **`ChangePassword`**: Updates the password for an account. If the username is not provided, it retrieves it via `GetName`. It recalculates SRP6 salt and verifier and updates the `account` table. This forces a re-authentication at the next login.
*   **`CheckPassword`**: Validates a provided password against the stored SRP6 verifier. It retrieves the stored salt (`s`) and verifier (`v`) from the `account` table, calculates the expected verifier from the input password, and uses `SRP6::ProofVerifier` to confirm validity.
*   **`GetId`**: Retrieves the numeric account ID for a given username by querying the `account` table. Returns 0 if not found.
*   **`GetName`**: Retrieves the username for a given account ID. It first checks the in-memory `m_accountPersistentData` cache. If not present or empty, it queries the `account` table. It normalizes the returned string before caching/storing it.
*   **`CalculateShaPassHash`**: Computes the SHA1 hash of the concatenated string `username:password` and returns it as a hex-encoded string. This is the input for the SRP6 calculation.

### Security & Access Control

*   **`GetSecurity`**: Returns the security level (GM level) of an account from the in-memory `AccountPersistentData` cache.
*   **`SetSecurity`**: Updates the security level in the `account_access` table using a `REPLACE` statement and updates the in-memory cache.
*   **`HasTrialRestrictions`**: Determines if an account is subject to trial restrictions. It returns `true` if the global config `CONFIG_BOOL_RESTRICT_UNVERIFIED_ACCOUNTS` is enabled, the account's email is not verified, and the security level is `SEC_PLAYER` or lower.
*   **`GetCharactersCount`**: Queries the `characters` table to count the number of characters associated with an account.

### Ban Management

*   **`BanIP` / `UnbanIP`**: Adds or removes an IP address from the in-memory `m_ipBanned` map. These are called by `World` methods to manage IP bans dynamically.
*   **`BanAccount` / `UnbanAccount`**: Adds or removes an account ID from the in-memory `m_accountBanned` map. Called by `World` methods.
*   **`IsIPBanned`**: Checks if an IP is in the `m_ipBanned` map and if the ban has expired (comparing `unbandate` with current time).
*   **`IsAccountBanned`**: Checks if an account is in the `m_accountBanned` map and if the ban has expired.
*   **`LoadIPBanList`**: Loads active IP bans from the `ip_banned` table into the `m_ipBanned` map. It handles both permanent bans (`unbandate == bandate`) and temporary ones. It supports silent loading (no progress bar/logs) for async reloads.
*   **`LoadAccountBanList`**: Loads active account bans from the `account_banned` table into the `m_accountBanned` map. Filters for `active = 1` and valid expiration dates.
*   **`WarnAccount`**: Stores a warning message for an account in the in-memory `m_accountWarnings` map.
*   **`GetWarningText`**: Retrieves the warning message for an account from the `m_accountWarnings` map.
*   **`LoadAccountWarnings`**: Loads inactive bans (`active = 0`) with reasons starting with "WARN:" from the `account_banned` table into the `m_accountWarnings` map. It strips the "WARN:" prefix.

### Anti-Spam & Abuse Prevention

*   **`WhisperedBy`**: Resets the whisper score for a specific target when the account receives a whisper from them. This is part of the anti-whisper-flood logic.
*   **`CountWhispersTo`**: Increments the whisper count for a target. If it's the first whisper to this target in the current cycle, it calculates a "score" based on relationship (guild/group/proximity). It returns the previous count.
*   **`CanWhisper`**: Delegates to `AnticheatMgr` to determine if a whisper is allowed based on the current anti-cheat rules.
*   **`GetWhisperScore`**: Calculates a risk score for whispering to a target. Score is 3 by default, reduced to 1 if the sender and receiver are in the same guild, same area, or same group.
*   **`JustMailed`**: Records the timestamp of a sent mail to a specific target account in the `m_mailsSent` map.
*   **`CanMail`**: Checks if an account can send mail to a target. If a mail was already sent to this target recently, it allows it. Otherwise, it counts recent mails sent to *any* target and compares against the configured limit (`CONFIG_UINT32_MAILSPAM_MAX_MAILS`) and expiration time (`CONFIG_UINT32_MAILSPAM_EXPIRE_SECS`).

### Instance Reset Tracking

*   **`CheckInstanceCount`**: Checks if an account has exceeded the maximum allowed instance resets (`maxCount`) for a specific instance ID within the last hour. It cleans up entries older than 3600 seconds. Returns `false` if the limit is exceeded.
*   **`AddInstanceEnterTime`**: Records the current time as the last entry time for a specific instance for an account.

### Initialization & Updates

*   **`Load`**: Called at startup. Initializes `AccountPersistentData`, loads ban lists, and loads account warnings.
*   **`LoadAccountData`**: Populates the `m_accountPersistentData` cache with username, email, verification status, and security level from the `account` and `account_access` tables.
*   **`GetAccountPersistentData`**: Thread-safe accessor for the `m_accountPersistentData` map. Creates a new entry if the account ID is not present.
*   **`UpdateAccountData`**: Updates the in-memory cache for an account's persistent data.
*   **`Update`**: Periodic timer handler. Reloads the IP ban list asynchronously if the configured timer expires.
*   **`normalizeString`**: Static utility to convert a UTF-8 string to wide string, uppercase Latin characters, and convert back to UTF-8. Ensures consistent casing for usernames/passwords.

---

## Cross-Unit Boundaries

*   **`ChatHandler.AccountCommands`**:
    *   Calls `CreateAccount`, `DeleteAccount`, `ChangePassword`, `CheckPassword`, `WarnAccount`, `GetWarningText`, `GetName`, `GetCharactersCount`, `normalizeString`.
    *   *Direction*: Chat commands trigger account management actions.
*   **`World`**:
    *   Calls `BanIP`, `UnbanIP`, `BanAccount`, `UnbanAccount`, `Load`, `Update`, `getConfig`.
    *   *Direction*: The World singleton manages global state and timers, delegating ban management and initialization to `AccountMgr`.
*   **`WorldSession`**:
    *   Calls `GetWarningText`, `HasTrialRestrictions`, `JustMailed`, `CanMail`, `GetAccountPersistentData`, `UpdateAccountData`.
    *   *Direction*: Active player sessions check restrictions, spam limits, and retrieve account data.
*   **`ObjectAccessor`**:
    *   Called by `DeleteAccount` via `KickPlayer`.
    *   *Direction*: `AccountMgr` requests the removal of online players during account deletion.
*   **`Player`**:
    *   Called by `DeleteAccount` via `DeleteFromDB`.
    *   *Direction*: `AccountMgr` delegates character data cleanup to the `Player` class.
*   **`SRP6`**:
    *   Called by `CreateAccount`, `ChangeUsername`, `ChangePassword`, `CheckPassword`.
    *   *Direction*: `AccountMgr` uses the SRP6 library for cryptographic password handling.
*   **`Database`**:
    *   Called extensively for queries and executions.
    *   *Direction*: `AccountMgr` persists all account data to the database.
*   **`Anticheat`**:
    *   Called by `CanWhisper`.
    *   *Direction*: `AccountMgr` delegates complex anti-cheat logic to the `AnticheatMgr`.
*   **`MasterPlayer`**:
    *   Called by `WhisperedBy`, `CountWhispersTo`, `GetWhisperScore`.
    *   *Direction*: `AccountMgr` accesses player GUIDs and sessions to evaluate whisper relationships.

---

## Data Model

`AccountMgr` interacts with the following database tables:

*   **`account`**:
    *   Used for: Storing core account info (ID, username, password verifier/salt, join date, last IP, security flags, email, etc.).
    *   Columns accessed: `id`, `username`, `v` (verifier), `s` (salt), `joindate`, `email`, `email_verif`, `gmlevel` (via join), `last_ip`, `locked`, `last_login`, `online`, `expansion`, `mutetime`, `locale`, `os`, `platform`, `current_realm`, `flags`, `security`, `geolock_pin`.
*   **`account_access`**:
    *   Used for: Storing GM/security levels per realm.
    *   Columns accessed: `id`, `gmlevel`, `RealmID`.
*   **`account_banned`**:
    *   Used for: Storing active and inactive bans/warnings.
    *   Columns accessed: `id`, `unbandate`, `bandate`, `active`, `banreason`, `bannedby`, `realm`, `gmlevel`.
*   **`ip_banned`**:
    *   Used for: Storing IP bans.
    *   Columns accessed: `ip`, `unbandate`, `bandate`, `bannedby`, `banreason`.
*   **`characters`**:
    *   Used for: Listing and deleting characters associated with an account.
    *   Columns accessed: `guid`, `account`.
*   **`character_tutorial`**:
    *   Used for: Cleaning up tutorial progress when an account is deleted.
    *   Columns accessed: `account`.
*   **`realmcharacters`**:
    *   Used for: Maintaining character counts per account per realm.
    *   Columns accessed: `realmlist.id` (joined), `account.id` (joined), `acctid`, `numchars`.

---

## Notable Implementation Details

1.  **SRP6 Authentication**: Passwords are never stored in plaintext. `AccountMgr` uses the SRP6 protocol. The `CalculateShaPassHash` function creates a SHA1 hash of `username:password`, which is then used by `SRP6::CalculateVerifier` to generate the salt (`s`) and verifier (`v`) stored in the database.
2.  **In-Memory Caching**:
    *   `m_accountPersistentData`: Caches username, email, verification status, and security level to avoid frequent DB reads. Updated via `LoadAccountData` and `UpdateAccountData`.
    *   `m_ipBanned` / `m_accountBanned`: Caches active bans for fast lookup. Reloaded periodically via `Update` or manually via `LoadIPBanList`/`LoadAccountBanList`.
    *   `m_accountWarnings`: Caches warning messages loaded from inactive bans.
3.  **Thread Safety**:
    *   `m_accountPersistentData` is protected by `m_accountPersistentDataMutex` (a `std::shared_timed_mutex`). Reads use `shared_lock`, writes use `lock_guard`.
    *   `m_ipBanned` is protected by `m_ipBannedMutex`.
4.  **Ban Expiration Logic**:
    *   In `LoadIPBanList` and `LoadAccountBanList`, if `unbandate == bandate`, the `unbandate` is set to `0xFFFFFFFF` (effectively infinite/permanent).
    *   `IsIPBanned` and `IsAccountBanned` check if `it->second < time(nullptr)` to determine if a ban has expired.
5.  **Anti-Spam Heuristics**:
    *   **Whispers**: `CountWhispersTo` tracks whispers per target. `GetWhisperScore` lowers the risk score if the players are socially connected (same guild/group/area).
    *   **Mail**: `CanMail` allows unlimited mail to a specific recipient once contacted, but limits the total number of *new* recipients contacted within a configurable time window.
6.  **Instance Reset Limit**:
    *   `CheckInstanceCount` enforces a limit on instance resets per hour. It prunes entries older than 3600 seconds. Note that this logic is purely in-memory and resets if the server restarts unless persisted elsewhere (not shown here).
7.  **Deprecated Method**: `ChangeUsername` is marked as deprecated in comments but still implemented. It updates both the database and the in-memory cache.
8.  **Normalization**: `normalizeString` converts strings to uppercase Latin characters. This ensures case-insensitive matching for usernames and passwords, preventing duplicate accounts with different casings.

---

## Member Reference

*   **`AccountMgr`**: Constructor. Initializes `m_banlistUpdateTimer` to 0.
*   **`~AccountMgr`**: Destructor. No special cleanup required.
*   **`CreateAccount`**: Creates a new account in the `account` table, generates SRP6 credentials, and initializes `realmcharacters`. Returns an `AccountOpResult`.
*   **`DeleteAccount`**: Deletes an account and all its characters. Kicks online players, removes character data, and deletes account records from `account`, `realmcharacters`, and `character_tutorial`.
*   **`ChangeUsername`**: *(Deprecated)* Updates username and password for an account. Recalculates SRP6 values and updates the database and cache.
*   **`BanIP`**: Adds an IP to the in-memory ban list.
*   **`UnbanIP`**: Removes an IP from the in-memory ban list.
*   **`BanAccount`**: Adds an account to the in-memory ban list.
*   **`UnbanAccount`**: Removes an account from the in-memory ban list.
*   **`WarnAccount`**: Stores a warning message for an account in memory.
*   **`GetWarningText`**: Retrieves the warning message for an account from memory.
*   **`ChangePassword`**: Updates an account's password. Recalculates SRP6 values and updates the database.
*   **`GetId`**: Retrieves the account ID for a given username from the database.
*   **`Load`**: Initializes the manager by loading account data, ban lists, and warnings.
*   **`GetSecurity`**: Returns the security level of an account from the in-memory cache.
*   **`SetSecurity`**: Updates the security level in the database and in-memory cache.
*   **`HasTrialRestrictions`**: Checks if an account is restricted due to being unverified and having low security.
*   **`GetName`**: Retrieves the username for an account ID, checking the cache first, then the database.
*   **`GetCharactersCount`**: Counts the number of characters for an account by querying the `characters` table.
*   **`CheckPassword`**: Validates a password against the stored SRP6 verifier in the database.
*   **`normalizeString`**: Static utility to uppercase Latin characters in a UTF-8 string.
*   **`CalculateShaPassHash`**: Computes the SHA1 hash of `username:password` as a hex string.
*   **`Update`**: Timer handler that asynchronously reloads the IP ban list if the timer expires.
*   **`LoadIPBanList`**: Loads active IP bans from the `ip_banned` table into the in-memory map.
*   **`LoadAccountBanList`**: Loads active account bans from the `account_banned` table into the in-memory map.
*   **`IsIPBanned`**: Checks if an IP is banned and if the ban is still active.
*   **`IsAccountBanned`**: Checks if an account is banned and if the ban is still active.
*   **`LoadAccountWarnings`**: Loads warning messages from inactive bans in the `account_banned` table.
*   **`CheckInstanceCount`**: Checks if an account has exceeded the instance reset limit for a specific instance in the last hour.
*   **`AddInstanceEnterTime`**: Records the current time as the last entry time for an instance for an account.
*   **`WhisperedBy`**: Resets the whisper score for a target when receiving a whisper.
*   **`CountWhispersTo`**: Increments the whisper count for a target and calculates a risk score.
*   **`CanWhisper`**: Delegates to `AnticheatMgr` to check if a whisper is allowed.
*   **`GetWhisperScore`**: Calculates a risk score for whispering based on social connections.
*   **`JustMailed`**: Records a mail sent to a target account.
*   **`CanMail`**: Checks if an account can send mail based on spam limits.
*   **`GetAccountPersistentData`**: Thread-safe accessor for the account data cache.
*   **`LoadAccountData`**: Populates the account data cache from the database.
*   **`UpdateAccountData`**: Updates the account data cache.

---

<!-- machine-true, projected from graph.json -->

## Map — AccountMgr

*Source:* AccountMgr.cpp, AccountMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AccountMgr | ctor | — | — | — |
| ~AccountMgr | dtor | — | — | — |
| CreateAccount | method | BigNumber/AsHexStr, Database/Execute#2, Database/PExecute#2, shared_Util/utf8length, SRP6/CalculateVerifier, SRP6/GetSalt, SRP6/GetVerifier, SRP6/SRP6 | ChatHandler.AccountCommands/HandleAccountCreateCommand | account |
| DeleteAccount | method | Database/BeginTransaction, Database/CommitTransaction, Database/PExecute#2, Database/PQuery, Field/GetUInt32, ObjectAccessor/KickPlayer, ObjectGuid/ObjectGuid#2, Player.Main/DeleteFromDB, QueryResult/Fetch, QueryResult/NextRow | ChatHandler.AccountCommands/HandleAccountDeleteCommand | account, characters, character_tutorial, realmcharacters |
| ChangeUsername | method | BigNumber/AsHexStr, Database/escape_string, Database/PExecute#2, Database/PQuery, shared_Util/utf8length, SRP6/CalculateVerifier, SRP6/GetSalt, SRP6/GetVerifier, SRP6/SRP6 | — | account |
| BanIP | method | — | World/BanAccount | — |
| UnbanIP | method | — | World/RemoveBanAccount | — |
| BanAccount | method | — | World/BanAccount#2, World/HandleAccountSelectResult | — |
| UnbanAccount | method | — | World/RemoveBanAccount | — |
| WarnAccount | method | — | ChatHandler.AccountCommands/HandleWarnCharacterCommand | — |
| GetWarningText | method | — | WorldSession.CharacterHandler/HandlePlayerLogin | — |
| ChangePassword | method | BigNumber/AsHexStr, Database/PExecute#2, shared_Util/utf8length, SRP6/CalculateVerifier, SRP6/GetSalt, SRP6/GetVerifier, SRP6/SRP6 | ChatHandler.AccountCommands/HandleAccountPasswordCommand, ChatHandler.AccountCommands/HandleAccountSetPasswordCommand | account |
| GetId | method | Database/escape_string, Database/PQuery, Field/GetUInt32, QueryResult/operator[] | ChatHandler.Chat/ExtractAccountId, MaNGOSsoap/ns1__executeCommand, RASocket/HandleInput_GotUsername, World/RemoveBanAccount | account |
| Load | method | Database/Query | World/SetInitialWorldSettings | ip_banned |
| GetSecurity | method | — | AsyncCommandHandlers/HandleAccountInfoResult, AsyncCommandHandlers/operator()#2, AsyncCommandHandlers/ShowAccountListHelper, AuctionHouseMgr/SendAuctionWonMail, ChatHandler.Chat/HasLowerSecurityAccount, ChatHandler.LookupCommands/HandleLookupPlayerCharacterCommand, ChatHandler.PlayerBotMgr/AddBot#2, ChatHandler.TicketCommands/HandleGMTicketAssignToCommand, ChatHandler.TicketCommands/HandleGMTicketUnAssignCommand, MaNGOSsoap/ns1__executeCommand, RASocket/HandleInput_GotUsername | — |
| SetSecurity | method | Database/PExecute#2 | ChatHandler.AccountCommands/HandleAccountSetGmLevelCommand | — |
| HasTrialRestrictions | method | World/getConfig | WorldSession.MailHandler/HandleSendMailCallback | — |
| GetName | method | Database/PQuery, Field/GetCppString, QueryResult/operator[] | ChatHandler.AccountCommands/HandleBanInfoCharacterCommand, ChatHandler.AccountCommands/HandleBanListHelper, ChatHandler.CharacterCommands/GetDeletedCharacterInfoList, ChatHandler.CharacterCommands/HandleCharacterDeletedRestoreCommand, ChatHandler.CharacterCommands/HandleCharacterEraseCommand, ChatHandler.Chat/ExtractAccountId | account |
| GetCharactersCount | method | Database/PQuery, Field/GetUInt32, QueryResult/Fetch | ChatHandler.CharacterCommands/HandleCharacterDeletedRestoreHelper, PlayerDump/LoadDump | characters |
| CheckPassword | method | Database/PQuery, Field/GetCppString, QueryResult/Fetch, SRP6/CalculateVerifier#2, SRP6/ProofVerifier, SRP6/SRP6 | ChatHandler.AccountCommands/HandleAccountPasswordCommand, MaNGOSsoap/ns1__executeCommand, RASocket/HandleInput_GotUsername | account |
| normalizeString | method | shared_Util/Utf8toWStr, shared_Util/WStrToUtf8 | ChatHandler.AccountCommands/HandleBanHelper, ChatHandler.AccountCommands/HandleUnBanHelper, ChatHandler.CharacterCommands/GetDeletedCharacterInfoList, ChatHandler.Chat/ExtractAccountId, ChatHandler.LookupCommands/HandleLookupAccountNameCommand, ChatHandler.LookupCommands/HandleLookupPlayerAccountCommand | — |
| CalculateShaPassHash | method | Digest/size#2, Generator.SHA1/ComputeFrom, shared_Util/hexEncodeByteArray | — | — |
| Update | method | World/getConfig#4 | World/Update | ip_banned |
| LoadIPBanList | method | Field/GetString, Field/GetUInt32, Log.Main/Out, ProgressBar/BarGoLink, ProgressBar/step, QueryResult/Fetch, QueryResult/GetRowCount, QueryResult/NextRow | ChatHandler.ServerCommands/HandleReloadIPBanList | — |
| LoadAccountBanList | method | Database/PQuery, Field/GetUInt32, Log.Main/Out, ProgressBar/BarGoLink, ProgressBar/step, QueryResult/Fetch, QueryResult/GetRowCount, QueryResult/NextRow | ChatHandler.ServerCommands/HandleReloadAccountBanList | account_banned |
| IsIPBanned | method | — | AsyncCommandHandlers/HandleResponse, WorldSocket/_HandleAuthSession | — |
| IsAccountBanned | method | — | AsyncCommandHandlers/HandleResponse, AsyncCommandHandlers/operator()#2, AsyncCommandHandlers/ShowAccountListHelper, ChatHandler.AccountCommands/HandleBanAllIPCommand | — |
| LoadAccountWarnings | method | Database/Query, Field/GetCppString, Field/GetUInt32, QueryResult/Fetch, QueryResult/NextRow | — | account_banned |
| CheckInstanceCount | method | — | Player.Main/CheckInstanceCount | — |
| AddInstanceEnterTime | method | — | Player.Main/AddInstanceEnterTime | — |
| WhisperedBy | method | MasterPlayer.Main/GetGUIDLow | — | — |
| CountWhispersTo | method | MasterPlayer.Main/GetGUIDLow, MasterPlayer.Main/GetSession, WorldSession.Main/GetAccountId | — | — |
| CanWhisper | method | Anticheat/CanWhisper, Anticheat/GetAnticheatLib | — | — |
| GetWhisperScore | method | MasterPlayer.Main/GetAreaId, MasterPlayer.Main/GetSession, Player.Main/GetGroup, Player.Main/GetGuildId, WorldSession.Main/GetPlayer | — | — |
| JustMailed | method | — | WorldSession.MailHandler/HandleSendMailCallback | — |
| CanMail | method | World/getConfig#4 | WorldSession.MailHandler/HandleSendMailCallback | — |
| GetAccountPersistentData | method | — | WorldSession.MailHandler/HandleSendMailCallback | — |
| LoadAccountData | method | Database/PQuery, Field/GetBool, Field/GetCppString, Field/GetString, Field/GetUInt32, QueryResult/Fetch, QueryResult/NextRow | — | account, account_access |
| UpdateAccountData | method | — | WorldSocket/_HandleAuthSession | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `account`: id int(11) unsigned PK, username varchar(32), gmlevel tinyint(3) unsigned, sessionkey longtext?, v longtext?, s longtext?, token_key varchar(100), email text?, joindate timestamp, last_ip varchar(30), failed_logins int(11) unsigned, locked tinyint(3) unsigned, lock_country varchar(2), last_login timestamp, online tinyint(4), expansion tinyint(3) unsigned, mutetime bigint(40), locale tinyint(3) unsigned, os varchar(4), platform varchar(4), current_realm tinyint(3) unsigned, flags int(10) unsigned, security varchar(255)?, email_verif tinyint(1), geolock_pin int(11)?
- `account_access`: id int(11) unsigned PK, gmlevel tinyint(3) unsigned, RealmID int(11) PK
- `account_banned`: banid bigint(20), id bigint(20) PK, bandate bigint(40) PK, unbandate bigint(40), bannedby varchar(50), banreason varchar(255), active tinyint(4), realm tinyint(4), gmlevel tinyint(4) unsigned
- `character_tutorial`: account bigint(20) unsigned PK, tut0 int(11) unsigned, tut1 int(11) unsigned, tut2 int(11) unsigned, tut3 int(11) unsigned, tut4 int(11) unsigned, tut5 int(11) unsigned, tut6 int(11) unsigned, tut7 int(11) unsigned
- `characters`: guid int(11) unsigned PK, account int(11) unsigned, name varchar(12), race tinyint(3) unsigned, class tinyint(3) unsigned, gender tinyint(3) unsigned, skin tinyint(3) unsigned, face tinyint(3) unsigned, hair_style tinyint(3) unsigned, hair_color tinyint(3) unsigned, facial_hair tinyint(3) unsigned, level tinyint(3) unsigned, xp int(10) unsigned, money int(10) unsigned, character_flags int(10) unsigned, zone int(11) unsigned, map int(11) unsigned, instance int(11) unsigned, position_x float, position_y float, position_z float, orientation float, transport_guid bigint(20) unsigned, transport_x float, transport_y float, transport_z float, transport_o float, known_taxi_mask longtext?, current_taxi_path text?, online tinyint(3) unsigned, played_time_total int(11) unsigned, played_time_level int(11) unsigned, create_time bigint(20) unsigned, logout_time bigint(20) unsigned, rest_bonus float, reset_talents_multiplier int(11) unsigned, reset_talents_time bigint(20) unsigned, death_expire_time bigint(20) unsigned, stable_slots tinyint(1) unsigned, bank_bag_slots tinyint(1) unsigned, extra_flags int(11) unsigned, honor_rank_points float, honor_highest_rank int(11) unsigned, honor_standing int(11) unsigned, honor_last_week_hk int(11) unsigned, honor_last_week_cp float, honor_stored_hk int(11), honor_stored_dk int(11), watched_faction int(11), drunk smallint(5) unsigned, health int(10) unsigned, power1 int(10) unsigned, power2 int(10) unsigned, power3 int(10) unsigned, power4 int(10) unsigned, power5 int(10) unsigned, explored_zones longtext?, equipment_cache longtext?, ammo_id int(10) unsigned, action_bars tinyint(3) unsigned, deleted_account int(11) unsigned?, deleted_name varchar(12)?, deleted_time bigint(20)?, world_phase_mask int(11)?
- `ip_banned`: ip varchar(32) PK, bandate int(11), unbandate int(11), bannedby varchar(50), banreason varchar(50)
- `realmcharacters`: realmid int(11) unsigned PK, acctid bigint(20) unsigned PK, numchars tinyint(3) unsigned

*`?` = nullable, `PK` = primary key column.*

