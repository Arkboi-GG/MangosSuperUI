<!-- provenance: failed-members, boundary-bleed -->
# WorldSession.CharacterHandler

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# WorldSession.CharacterHandler

## Purpose & Responsibilities

The `WorldSession.CharacterHandler` unit implements the core lifecycle management for player characters within the `wowvmangos` server. It handles the entire sequence of events from a client requesting a list of characters, through character creation and deletion, to the complex asynchronous loading of a character into the game world.

Its primary responsibilities include:
1.  **Character Enumeration:** Retrieving and formatting character data for the login screen.
2.  **Character Creation & Deletion:** Validating inputs, checking realm restrictions, and persisting changes to the database.
3.  **Asynchronous Login:** Managing the heavy I/O operations required to load a player's full state (inventory, spells, quests, social lists, etc.) from multiple database tables without blocking the main server thread.
4.  **Session State Transition:** Moving a `WorldSession` from a "logged in but no character" state to a "fully loaded player in the world" state, handling edge cases like re-logins (ALT-F4) and concurrent login attempts.
5.  **Minor Gameplay Opcodes:** Handling specific client requests related to faction reputation, tutorial flags, cosmetic visibility (helm/cloak), and character renaming.

## Member-by-Member Behavior

### Character Enumeration

**`HandleCharEnumOpcode`**
Initiates the retrieval of characters for the current account. It constructs a SQL query joining `characters`, `character_pet`, and `guild_member` tables to fetch essential display data (name, race, class, level, pet info, guild ID). This query is executed asynchronously via `CharacterDatabase.AsyncPQuery`, with results routed to `CharacterHandler.HandleCharEnumCallback`.

**`HandleCharEnumCallback`**
Acts as the bridge between the asynchronous database result and the session. It locates the `WorldSession` associated with the account ID using `World.FindSession`. If the session still exists, it delegates the actual packet construction to `WorldSession.HandleCharEnum` (implemented in the `WorldSession` unit).

**`HandleCharEnum`**
Iterates through the `QueryResult` provided by the enumeration query. For each character row, it updates the session's `m_characterMaxLevel` tracker and calls `Player.BuildEnumData` (from the `Player` unit) to serialize the character's summary data into the `SMSG_CHAR_ENUM` packet. Finally, it sends the populated packet to the client.

### Character Creation

**`HandleCharCreateOpcode`**
Processes the request to create a new character. It performs extensive validation:
1.  Checks if character creation is disabled for the selected race's faction (Alliance/Horde) based on realm configuration.
2.  Validates that the requested Race and Class entries exist in the DBC stores and that the race is playable.
3.  Validates appearance attributes (skin, hair, etc.) via `Player.ValidateAppearance`.
4.  Normalizes and checks the character name for validity, reserved status, and uniqueness.
5.  Enforces realm-specific rules, such as preventing two-sided accounts on PvP realms unless configured otherwise.
6.  Generates a new GUID and calls `Player.SaveNewPlayer` to insert the character into the `characters` table.
7.  Updates the `realmcharacters` table with the new character count.
8.  Sends a success or failure response (`SMSG_CHAR_CREATE`) to the client.

### Character Deletion

**`HandleCharDeleteOpcode`**
Handles the deletion of a character. It first ensures the character is not currently online (loaded in memory). It verifies the requester is the account owner and not a guild leader (preventing accidental loss of guild leadership). If the character is online (e.g., due to a crash/ALT-F4), it forces a logout via `WorldSession.LogoutPlayer`. It then calls `Player.DeleteFromDB` to remove the character data and sends a success response.

### Character Login (Asynchronous Loading)

**`LoginQueryHolder` (ctor)**
Constructs a holder object for asynchronous login queries. It initializes the base `SqlQueryHolder` with the character's GUID counter and stores the account ID and full `ObjectGuid`.

**`~LoginQueryHolder`**
Destructor that cleans up any pending query results by calling `SqlOperations.DeleteAllResults`.

**`GetGuid`**
Simple accessor returning the stored character GUID.

**`GetAccountId`**
Simple accessor returning the stored account ID.

**`Initialize`**
Prepares the asynchronous login by defining a series of SQL queries (`SetPQuery`) required to load the player's full state. These queries target numerous tables including `characters`, `character_inventory`, `character_spell`, `character_queststatus`, `character_social`, `mail`, `guild_member`, etc. This allows the database engine to prepare these statements efficiently before execution.

**`HandlePlayerLoginOpcode`**
The entry point for logging in a specific character. It validates that the world is available, the session isn't already loading a player, and the GUID is valid. It creates a `LoginQueryHolder`, initializes its queries, sets the session's `m_playerLoading` flag, and submits the holder to the database for asynchronous execution via `CharacterDatabase.DelayQueryHolderUnsafe`.

**`LoginPlayer`**
An internal method (called by other systems like bots) that performs the same asynchronous login setup as `HandlePlayerLoginOpcode` but takes the GUID directly rather than parsing a packet. It is called by `AiBotAI.OnSessionLoaded`, `PartyBotAI.OnSessionLoaded`, and `PlayerBotAI.OnSessionLoaded`.

**`HandlePlayerLoginCallback`**
The callback triggered when the asynchronous login queries complete. It retrieves the `WorldSession` using the account ID from the `LoginQueryHolder` via `World.FindSession`. If the session is valid, it passes the holder to `WorldSession.HandlePlayerLogin`.

**`HandlePlayerLogin`**
The most complex method in this unit, responsible for constructing the `Player` object and integrating it into the world.
1.  **Safety Checks:** Ensures the session hasn't been invalidated or already assigned a player during the async wait.
2.  **Online Conflict Resolution:** Checks if the character is already online (e.g., ALT-F4 logout). If so, it transfers the existing `Player` object to the new session, updates the socket/broadcaster, and clears stun flags. If not, it creates a new `Player` object.
3.  **Data Loading:** Calls `Player.LoadFromDB` (from the `Player` unit) passing the `LoginQueryHolder`. This method consumes the pre-fetched query results to populate the player's inventory, spells, quests, etc.
4.  **MasterPlayer Integration:** Creates or updates the `MasterPlayer` object (used for clustering/social/mail) and loads actions, social lists, and mails from the holder.
5.  **World Integration:**
    *   Sends `SMSG_LOGIN_VERIFY_WORLD` with position data.
    *   Loads account data and sends times.
    *   Sends friend/ignore lists.
    *   Displays MOTD (Message of the Day) and Guild MOTD.
    *   Handles initial cinematic sequences for new characters.
    *   Adds the player to the `Map` object. If the map fails to add the player (invalid position), it teleports them to a fallback location (homebind or specific coordinates for Naxxramas).
    *   Updates online status in `characters` and `account` tables.
    *   Notifies groups and friends of the login.
    *   Loads corpse, pet, and handles taxi flights if applicable.
    *   Resets talents if flagged.
    *   Sends final notifications (shutdown warnings, GM invisibility, etc.).
6.  **Cleanup:** Clears the `m_playerLoading` flag and deletes the `LoginQueryHolder`.

### Faction & Reputation

**`HandleSetFactionAtWarOpcode`**
Allows a player to set a faction as "at war" (hostile). It checks if the player is in combat (disallowed) and then calls `ReputationMgr.SetAtWar`.

**`HandleSetWatchedFactionOpcode`**
Updates the faction index currently watched by the player's UI. It directly sets the `PLAYER_FIELD_WATCHED_FACTION_INDEX` field on the player object via `WorldObject.SetInt32Value`.

**`HandleSetFactionInactiveOpcode`**
Marks a faction as inactive in the player's reputation manager, hiding it from the reputation window unless explicitly shown.

### Cosmetics & Tutorials

**`HandleShowingHelmOpcode`**
Toggles the `PLAYER_FLAGS_HIDE_HELM` flag on the player object, controlling whether the character wears their equipped helm.

**`HandleShowingCloakOpcode`**
Toggles the `PLAYER_FLAGS_HIDE_CLOAK` flag on the player object, controlling whether the character wears their equipped cloak.

**`HandleTutorialFlagOpcode`**
Sets a specific bit in the tutorial flags array. It calculates the word index and bit offset from the provided flag ID and updates the session's tutorial state via `WorldSession.SetTutorialInt`.

**`HandleTutorialClearOpcode`**
Sets all tutorial flag words to `0xFFFFFFFF`, effectively marking all tutorials as completed/seen.

**`HandleTutorialResetOpcode`**
Resets all tutorial flags to `0x00000000`, forcing the client to show all tutorials again.

### Character Renaming

**`HandleCharRenameOpcode`**
Validates the new name (normalization, reserved names, availability). It then initiates an asynchronous database check to ensure the character belongs to the account, has the rename flag set, and the new name is not taken.

**`HandleChangePlayerNameOpcodeCallBack`**
Executes after the rename validation query completes. If successful, it begins a transaction, updates the `characters` table with the new name and sets the "needs GM review" flag, commits the transaction, logs the change, sends a success packet to the client, updates the name cache, and invalidates the player's data to all connected clients.

## Cross-Unit Boundaries

*   **`Player` (Player.cpp/h):** Heavily relied upon for data serialization (`BuildEnumData`), persistence (`SaveNewPlayer`, `DeleteFromDB`, `LoadFromDB`), and validation (`ValidateAppearance`, `TeamForRace`). `HandlePlayerLogin` creates the `Player` object and delegates the bulk of state initialization to it.
*   **`ObjectMgr` (ObjectMgr.cpp/h):** Used for name validation (`CheckPlayerName`, `IsReservedName`, `normalizePlayerName`), GUID generation (`GeneratePlayerLowGuid`), and caching (`GetPlayerDataForAccount`, `ChangePlayerNameInCache`).
*   **`World` (World.cpp/h):** Accessed for global configuration (`getConfig`), realm settings (`IsPvPRealm`), and session management (`FindSession`).
*   **`GuildMgr` / `Guild` (GuildMgr.cpp/h, Guild.cpp/h):** Checked during deletion (`GetGuildByLeader`) and login (`GetGuildById` for MOTD).
*   **`ObjectAccessor` (ObjectAccessor.cpp/h):** Used to find existing `Player` objects in memory (`FindPlayer`, `FindPlayerNotInWorld`, `FindMasterPlayer`) to handle re-logins and online conflicts.
*   **`Map` / `MapManager` (Map.cpp/h, MapManager.cpp/h):** `HandlePlayerLogin` adds the player to the map (`Map.Add`) and handles teleportation failures via `MapManager.ExecuteSingleDelayedTeleport`.
*   **`MasterPlayer` (MasterPlayer.cpp/h):** Created and populated in `HandlePlayerLogin` to manage social/mail data separately from the core player entity, likely for clustering support.
*   **`SocialMgr` (SocialMgr.cpp/h):** Sends friend/ignore lists and status updates during login.
*   **`ReputationMgr` (ReputationMgr.cpp/h):** Called by faction opcodes to update war/inactive states.
*   **`SqlQueryHolder` / `SqlOperations` (Database/SqlQueryHolder.cpp/h):** The base class for `LoginQueryHolder` and the mechanism for managing asynchronous query results.

## Data Model

This unit interacts with a wide range of database tables to load and persist character state. Key tables include:

*   **`characters`**: Core character data (GUID, name, race, class, position, level, money, flags, etc.). Read during enum and login; updated during creation, deletion, rename, and login status.
*   **`character_account_data`**: Cached account-specific data (e.g., camera settings, UI preferences). Loaded during login.
*   **`character_action`**: Action bar bindings. Loaded during login.
*   **`character_aura`**: Persistent auras (buffs/debuffs). Loaded during login.
*   **`character_battleground_data`**: Battleground join positions/teams. Loaded during login.
*   **`character_forgotten_skills`**: Skills intentionally removed. Loaded during login.
*   **`character_homebind`**: Home bind location. Loaded during login.
*   **`character_honor_cp`**: Honor kill credit data. Loaded during login.
*   **`character_instance`**: Instance reset data. Loaded during login.
*   **`character_inventory`**: Bag/slot mappings for items. Joined with `item_instance` during login.
*   **`character_queststatus`**: Quest progress. Loaded during login.
*   **`character_reputation`**: Faction standings. Loaded during login.
*   **`character_skills`**: Skill levels. Loaded during login.
*   **`character_social`**: Friends/ignores. Loaded during login.
*   **`character_spell`**: Known spells. Loaded during login.
*   **`character_spell_cooldown`**: Active cooldowns. Loaded during login.
*   **`group_member`**: Group membership. Checked during login.
*   **`guild_member`**: Guild membership/rank. Checked during enum, login, and deletion.
*   **`instance`**: Instance metadata. Joined with `character_instance` during login.
*   **`item_instance`**: Detailed item data (enchantments, durability, etc.). Joined with `character_inventory` and `mail_items` during login.
*   **`item_loot`**: Loot stored in bags. Loaded during login.
*   **`mail`**: Mail headers. Loaded during login.
*   **`mail_items`**: Items attached to mail. Joined with `item_instance` during login.
*   **`account`**: Account online status and current realm. Updated during login.
*   **`realmcharacters`**: Character count per account/realm. Updated during creation.

## Notable Implementation Details

1.  **Asynchronous Login Pattern:** The login process is split between `HandlePlayerLoginOpcode` (setup), `LoginQueryHolder` (query definition), and `HandlePlayerLogin` (execution). This prevents the main thread from blocking during heavy database reads. The `LoginQueryHolder` persists across the async gap, carrying the query results.
2.  **Re-login Handling:** `HandlePlayerLogin` explicitly checks if the character is already online (`sObjectAccessor.FindPlayer`). If so, it doesn't create a new `Player` object but instead transfers the existing one to the new session. This handles cases where a client crashes (ALT-F4) and reconnects quickly. It also resets movement flags (stun/root) that might have been left over from the logout sequence.
3.  **Map Addition Failure Fallback:** If `Map.Add` fails (e.g., invalid coordinates), the code attempts to teleport the player to a "Go Back Trigger" location, or hardcoded coordinates for Naxxramas, or finally to their homebind. This prevents players from getting stuck in void.
4.  **Name Validation Rigor:** Both creation and renaming involve multiple steps: normalization, DBC lookup, reserved name check, uniqueness check, and account ownership verification. Renaming is asynchronous to ensure consistency.
5.  **Tutorial Flag Bitmasking:** Tutorial flags are stored as an array of 8 integers. `HandleTutorialFlagOpcode` calculates the word index (`flag / 32`) and bit offset (`flag % 32`) to manipulate individual bits within these words.
6.  **Crash Prevention in Callbacks:** `HandlePlayerLoginCallback` and `HandleCharEnumCallback` check if the `WorldSession` still exists before proceeding. This prevents crashes if the session was closed (e.g., network drop) while the database query was pending.
7.  **MasterPlayer Separation:** The `MasterPlayer` object is created/updated separately from the `Player` object during login. This suggests a design where social/mail data might be handled independently, possibly for performance or clustering reasons.

## Member Reference

**`LoginQueryHolder` (ctor)**
Constructs the asynchronous login query holder, initializing the base class with the character's GUID counter and storing the account ID and full GUID.

**`~LoginQueryHolder`**
Destructor that cleans up any pending query results by calling `SqlOperations.DeleteAllResults`.

**`GetGuid`**
Returns the stored character `ObjectGuid`.

**`GetAccountId`**
Returns the stored account ID.

**`Initialize`**
Defines the SQL queries required to load the player's full state (inventory, spells, quests, etc.) from various database tables, preparing them for asynchronous execution.

**`HandleCharEnumCallback`**
Bridge callback that locates the `WorldSession` for the account and delegates character enumeration data processing to `WorldSession.HandleCharEnum`.

**`HandlePlayerLoginCallback`**
Bridge callback that locates the `WorldSession` for the account and delegates the final login processing to `WorldSession.HandlePlayerLogin`, passing the `LoginQueryHolder`.

**`HandleCharEnum`**
Iterates through the character enumeration query results, builds the `SMSG_CHAR_ENUM` packet using `Player.BuildEnumData`, and sends it to the client.

**`HandleCharEnumOpcode`**
Initiates the asynchronous query to retrieve character list data for the current account, joining `characters`, `character_pet`, and `guild_member` tables.

**`HandleCharCreateOpcode`**
Validates character creation inputs (race, class, name, appearance), enforces realm restrictions, generates a new GUID, saves the character to the database, and sends a success/failure response.

**`HandleCharDeleteOpcode`**
Validates character deletion (ownership, guild leadership, online status), forces logout if online, deletes the character from the database, and sends a success response.

**`HandlePlayerLoginOpcode`**
Initiates the asynchronous login process for a specific character by creating a `LoginQueryHolder`, initializing its queries, and submitting it to the database.

**`LoginPlayer`**
Internal method to initiate asynchronous login for a given GUID, used by systems like bots, mirroring the logic of `HandlePlayerLoginOpcode`.

**`HandlePlayerLogin`**
Completes the login process: resolves online conflicts, creates/updates the `Player` and `MasterPlayer` objects, loads data from the `LoginQueryHolder`, integrates the player into the world map, updates database online status, and sends initial world packets.

**`HandleSetFactionAtWarOpcode`**
Sets a faction as "at war" for the player, if not in combat.

**`HandleTutorialFlagOpcode`**
Sets a specific bit in the player's tutorial flags array.

**`HandleTutorialClearOpcode`**
Marks all tutorial flags as seen by setting all words to `0xFFFFFFFF`.

**`HandleTutorialResetOpcode`**
Resets all tutorial flags to unseen by setting all words to `0x00000000`.

**`HandleSetWatchedFactionOpcode`**
Updates the faction index currently watched by the player's UI.

**`HandleSetFactionInactiveOpcode`**
Marks a faction as inactive in the player's reputation manager.

**`HandleShowingHelmOpcode`**
Toggles the visibility of the player's equipped helm.

**`HandleShowingCloakOpcode`**
Toggles the visibility of the player's equipped cloak.

**`HandleCharRenameOpcode`**
Validates the new character name and initiates an asynchronous database check to ensure the rename is allowed and the name is available.

**`HandleChangePlayerNameOpcodeCallBack`**
Executes the character rename transaction, updates the database, notifies clients, and sends a success response if the validation passed.

---

<!-- machine-true, projected from graph.json -->

## Map — WorldSession.CharacterHandler

*Source:* CharacterHandler.cpp, WorldSession.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| LoginQueryHolder | ctor | ObjectGuid/GetCounter, SqlQueryHolder/SqlQueryHolder#2 | — | — |
| ~LoginQueryHolder | dtor | SqlOperations/DeleteAllResults | — | — |
| GetGuid | method | — | — | — |
| GetAccountId | method | — | — | — |
| Initialize | method | ObjectGuid/GetCounter, SqlOperations/SetPQuery, SqlOperations/SetSize | — | characters, character_account_data, character_action, character_aura, character_battleground_data, character_forgotten_skills, character_homebind, character_honor_cp, character_instance, character_inventory, character_queststatus, character_reputation, character_skills, character_social, character_spell, character_spell_cooldown, group_member, guild_member, instance, item_instance, item_loot, mail, mail_items |
| HandleCharEnumCallback | method | World/FindSession | — | — |
| HandlePlayerLoginCallback | method | World/FindSession | — | — |
| HandleCharEnum | method | ByteBuffer/operator<<#7, Field/GetUInt32, Log.Main/Out, Player.Main/BuildEnumData, QueryResult/NextRow, QueryResult/operator[], WorldPacket/WorldPacket#4, WorldSession.Main/GetAccountId, WorldSession.Main/SendPacket | — | — |
| HandleCharEnumOpcode | method | WorldSession.Main/GetAccountId | — | characters, character_pet, guild_member |
| HandleCharCreateOpcode | method | ByteBuffer/operator<<#7, ChrRacesEntry/HasFlag, Database/PExecute#2, ObjectMgr/CheckPlayerName, ObjectMgr/GeneratePlayerLowGuid, ObjectMgr/GetPlayerDataForAccount, ObjectMgr/GetPlayerGuidByName, ObjectMgr/IsReservedName, ObjectMgr/normalizePlayerName, Player.Main/SaveNewPlayer, Player.Main/TeamForRace, Player.Main/ValidateAppearance, World/getConfig, World/getConfig#4, World/IsPvPRealm, WorldPacket/WorldPacket#4, WorldSession.Main/GetAccountId, WorldSession.Main/GetSecurity, WorldSession.Main/ProcessAnticheatAction, WorldSession.Main/SendPacket | — | — |
| HandleCharDeleteOpcode | method | ByteBuffer/operator<<#7, GuildMgr/GetGuildByLeader, ObjectAccessor/FindPlayer, ObjectAccessor/FindPlayerNotInWorld, ObjectGuid/GetCounter, ObjectMgr/GetPlayerDataByGUID, Player.Main/DeleteFromDB, Player.Main/GetSession, Player.Main/Player#2, WorldPacket/WorldPacket#4, WorldSession.Main/GetAccountId, WorldSession.Main/LogoutPlayer, WorldSession.Main/SendPacket | — | — |
| HandlePlayerLoginOpcode | method | ByteBuffer/operator<<#7, ObjectGuid/IsPlayer, World/getConfig, WorldPacket/WorldPacket#4, WorldSession.Main/GetAccountId, WorldSession.Main/GetPlayer, WorldSession.Main/GetSecurity, WorldSession.Main/PlayerLoading, WorldSession.Main/SendPacket | — | — |
| LoginPlayer | method | Errors/PrintStacktraceAndThrow, ObjectGuid/IsPlayer, WorldSession.Main/GetAccountId | AiBotAI.Main/OnSessionLoaded, PartyBotAI/OnSessionLoaded, PlayerBotAI/OnSessionLoaded#2 | — |
| HandlePlayerLogin | method | AccountMgr/GetWarningText, ByteBuffer/operator<<, ByteBuffer/operator<<#10, ByteBuffer/operator<<#7, ByteBuffer/operator<<#9, Creature.MotionMaster/Initialize, Database/CreateStatement, Errors/PrintStacktraceAndThrow, game_Group_Group/SendLootStartRollsForPlayer, game_Group_Group/UpdatePlayerOnlineStatus, game_Guild_Guild/BroadcastEvent, Guild/GetMOTD, GuildMgr/GetGuildById, Log.Main/Out, Map.Main/Add#3, Map.Main/ExistingPlayerLogin, MapManager/ExecuteSingleDelayedTeleport#2, MapManager/FindMap, MasterPlayer.Main/GetObjectGuid, MasterPlayer.Main/GetSession, MasterPlayer.Main/LoadActions, MasterPlayer.Main/LoadMailedItems, MasterPlayer.Main/LoadMails, MasterPlayer.Main/LoadPlayer, MasterPlayer.Main/LoadSocial, MasterPlayer.Main/MasterPlayer, MasterPlayer.Main/SendInitialActionButtons, MasterPlayer.Main/SetSession, MasterPlayer.Main/UpdateNextMailTimeAndUnreads, MovementInfo/GetTransportPos, Object/GetGUIDLow, Object/GetObjectGuid, Object/HasFlag, ObjectAccessor/AddObject#2, ObjectAccessor/AddObject#3, ObjectAccessor/FindMasterPlayer, ObjectAccessor/FindPlayer, ObjectGuid/GetCounter, ObjectGuid/IsPlayer, ObjectMgr/GetGoBackTrigger, ObjectMgr/UpdatePlayerCachedPosition, Player.Main/ApplyGhostForm, Player.Main/ContinueTaxiFlight, Player.Main/CreatePacketBroadcaster, Player.Main/GetCachedAreaId, Player.Main/GetCachedZoneId, Player.Main/GetGMInvisibilityLevel, Player.Main/GetGroup, Player.Main/GetGuildId, Player.Main/GetName, Player.Main/GetSession, Player.Main/GetSocial, Player.Main/HasCharacterFlag, Player.Main/IsGameMaster, Player.Main/IsGMVisible, Player.Main/KillPlayer, Player.Main/LoadCorpse, Player.Main/LoadFromDB, Player.Main/LoadPet, Player.Main/PetSpellInitialize, Player.Main/Player#2, Player.Main/Player#5, Player.Main/PSendSysMessage, Player.Main/PSendSysMessage#2, Player.Main/ResetTalents, Player.Main/RestorePendingTeleport, Player.Main/SendCinematicStart, Player.Main/SendCorpseReclaimDelay, Player.Main/SendInitialPacketsAfterAddToMap, Player.Main/SendInitialPacketsBeforeAddToMap, Player.Main/SendInitWorldStates, Player.Main/SendMirrorTimers, Player.Main/SendPacketsAtRelogin, Player.Main/SetInGameTime, Player.Main/SetSession, Player.Main/SetTaxiCheater, Player.Main/TeleportTo, Player.Main/TeleportToHomebind, Player.Main/UpdatePvPContested, PlayerBotMgr/IsSavingAllowed, PlayerBroadcaster/ChangeSocket, shared_Util/getMSTime, SocialMgr/SendFriendList, SocialMgr/SendFriendStatus, SocialMgr/SendIgnoreList, SqlOperations/TakeResult, SqlPreparedStatement/operator=, SqlStatementID/SqlStatementID, Unit.Main/CanFreeMove, Unit.Main/GetDeathState, Unit.Main/GetMotionMaster, Unit.Main/GetRace, Unit.Main/HasUnitState, Unit.Main/IsAlive, Unit.Main/IsStandingUp, Unit.Main/SetRootedReal, Unit.Main/SetStandState, Unit.Main/UpdateControl, World/getConfig, World/GetMotd, World/IsShutdowning, World/ShutdownMsg, WorldObject.Object/FindMap, WorldObject.Object/GetInstanceId, WorldObject.Object/GetMap, WorldObject.Object/GetMapId, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/GetTransport, WorldObject.Object/RemoveFlag, WorldPacket/WorldPacket#4, WorldSession.Main/GetAccountId, WorldSession.Main/GetBot, WorldSession.Main/GetMasterPlayer, WorldSession.Main/GetPlayer, WorldSession.Main/GetPlayerName, WorldSession.Main/GetRemoteAddress, WorldSession.Main/GetSocket, WorldSession.Main/KickPlayer, WorldSession.Main/LoadAccountData, WorldSession.Main/ProcessAnticheatAction, WorldSession.Main/SendAccountDataTimes, WorldSession.Main/SendNotification, WorldSession.Main/SendNotification#2, WorldSession.Main/SendPacket, WorldSession.Main/SetMasterPlayer, WorldSession.Main/SetPlayer | — | account, characters |
| HandleSetFactionAtWarOpcode | method | Player.Main/GetReputationMgr, ReputationMgr/SetAtWar#2, Unit.Main/IsInCombat, WorldSession.Main/GetPlayer | — | — |
| HandleTutorialFlagOpcode | method | WorldSession.Main/GetTutorialInt, WorldSession.Main/SetTutorialInt | — | — |
| HandleTutorialClearOpcode | method | WorldSession.Main/SetTutorialInt | — | — |
| HandleTutorialResetOpcode | method | WorldSession.Main/SetTutorialInt | — | — |
| HandleSetWatchedFactionOpcode | method | WorldObject.Object/SetInt32Value, WorldSession.Main/GetPlayer | — | — |
| HandleSetFactionInactiveOpcode | method | Player.Main/GetReputationMgr, ReputationMgr/SetInactive#2 | — | — |
| HandleShowingHelmOpcode | method | Object/ToggleFlag | — | — |
| HandleShowingCloakOpcode | method | Object/ToggleFlag | — | — |
| HandleCharRenameOpcode | method | ByteBuffer/operator<<#7, Database/escape_string, ObjectGuid/GetCounter, ObjectMgr/CheckPlayerName, ObjectMgr/IsReservedName, ObjectMgr/normalizePlayerName, WorldPacket/WorldPacket#4, WorldSession.Main/GetAccountId, WorldSession.Main/GetSecurity, WorldSession.Main/SendPacket | — | characters |
| HandleChangePlayerNameOpcodeCallBack | method | ByteBuffer/operator<<, ByteBuffer/operator<<#7, Database/BeginTransaction, Database/CommitTransaction, Database/PExecute#2, Field/GetCppString, Field/GetUInt32, ObjectGuid/ObjectGuid#2, ObjectGuid/operator<<, ObjectMgr/ChangePlayerNameInCache, Player.Main/Player#3, QueryResult/Fetch, World/FindSession, World/InvalidatePlayerDataToAllClient, WorldPacket/WorldPacket#4, WorldSession.Main/GetAccountId, WorldSession.Main/GetRemoteAddress, WorldSession.Main/SendPacket | — | characters |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `account`: id int(11) unsigned PK, username varchar(32), gmlevel tinyint(3) unsigned, sessionkey longtext?, v longtext?, s longtext?, token_key varchar(100), email text?, joindate timestamp, last_ip varchar(30), failed_logins int(11) unsigned, locked tinyint(3) unsigned, lock_country varchar(2), last_login timestamp, online tinyint(4), expansion tinyint(3) unsigned, mutetime bigint(40), locale tinyint(3) unsigned, os varchar(4), platform varchar(4), current_realm tinyint(3) unsigned, flags int(10) unsigned, security varchar(255)?, email_verif tinyint(1), geolock_pin int(11)?
- `character_account_data`: guid int(11) unsigned PK, type int(11) unsigned PK, time bigint(11) unsigned, data longblob
- `character_action`: guid int(11) unsigned PK, button tinyint(3) unsigned PK, action int(11) unsigned, type tinyint(3) unsigned
- `character_aura`: guid int(11) unsigned PK, caster_guid bigint(20) unsigned PK, item_guid int(11) unsigned PK, spell int(11) unsigned PK, stacks int(11) unsigned, charges int(11) unsigned, base_points0 float, base_points1 float, base_points2 float, periodic_time0 int(11) unsigned, periodic_time1 int(11) unsigned, periodic_time2 int(11) unsigned, max_duration int(11), duration int(11), effect_index_mask tinyint(3) unsigned
- `character_battleground_data`: guid int(11) unsigned PK, instance_id int(11) unsigned, team int(11) unsigned, join_x float, join_y float, join_z float, join_o float, join_map int(11)
- `character_forgotten_skills`: guid int(11) unsigned PK, skill mediumint(9) unsigned PK, value mediumint(9) unsigned
- `character_homebind`: guid int(11) unsigned PK, map int(11) unsigned, zone int(11) unsigned, position_x float, position_y float, position_z float
- `character_honor_cp`: guid int(11) unsigned, victim_type tinyint(3) unsigned, victim_id int(11) unsigned, cp float, date int(11) unsigned, type tinyint(3) unsigned
- `character_instance`: guid int(11) unsigned PK, instance int(11) unsigned PK, permanent tinyint(1) unsigned
- `character_inventory`: guid int(11) unsigned, bag int(11) unsigned, slot tinyint(3) unsigned, item_guid int(11) unsigned PK, item_id int(11) unsigned
- `character_pet`: id int(11) unsigned PK, entry int(11) unsigned, owner_guid int(11) unsigned, display_id int(11) unsigned?, created_by_spell int(11) unsigned, pet_type tinyint(3) unsigned, level int(11) unsigned, xp int(11) unsigned, react_state tinyint(1) unsigned, loyalty_points int(11), loyalty int(11) unsigned, training_points int(11), name varchar(100)?, renamed tinyint(1) unsigned, slot int(11) unsigned, current_health int(11) unsigned, current_mana int(11) unsigned, current_happiness int(11) unsigned, save_time bigint(20) unsigned, reset_talents_cost int(11) unsigned, reset_talents_time bigint(20) unsigned, action_bar_data longtext?, teach_spell_data longtext?
- `character_queststatus`: guid int(11) unsigned PK, quest int(11) unsigned PK, status int(11) unsigned, rewarded tinyint(1) unsigned, explored tinyint(1) unsigned, timer bigint(20) unsigned, mob_count1 int(11) unsigned, mob_count2 int(11) unsigned, mob_count3 int(11) unsigned, mob_count4 int(11) unsigned, item_count1 int(11) unsigned, item_count2 int(11) unsigned, item_count3 int(11) unsigned, item_count4 int(11) unsigned, reward_choice int(11) unsigned
- `character_reputation`: guid int(11) unsigned PK, faction int(11) unsigned PK, standing int(11), flags int(11)
- `character_skills`: guid int(11) unsigned PK, skill mediumint(9) unsigned PK, value mediumint(9) unsigned, max mediumint(9) unsigned
- `character_social`: guid int(11) unsigned PK, friend int(11) unsigned PK, flags tinyint(1) unsigned PK
- `character_spell`: guid int(11) unsigned PK, spell int(11) unsigned PK, active tinyint(3) unsigned, disabled tinyint(3) unsigned
- `character_spell_cooldown`: guid int(11) unsigned PK, spell int(11) unsigned PK, spell_expire_time bigint(20) unsigned, category int(11) unsigned, category_expire_time bigint(20) unsigned, item_id int(11) unsigned
- `characters`: guid int(11) unsigned PK, account int(11) unsigned, name varchar(12), race tinyint(3) unsigned, class tinyint(3) unsigned, gender tinyint(3) unsigned, skin tinyint(3) unsigned, face tinyint(3) unsigned, hair_style tinyint(3) unsigned, hair_color tinyint(3) unsigned, facial_hair tinyint(3) unsigned, level tinyint(3) unsigned, xp int(10) unsigned, money int(10) unsigned, character_flags int(10) unsigned, zone int(11) unsigned, map int(11) unsigned, instance int(11) unsigned, position_x float, position_y float, position_z float, orientation float, transport_guid bigint(20) unsigned, transport_x float, transport_y float, transport_z float, transport_o float, known_taxi_mask longtext?, current_taxi_path text?, online tinyint(3) unsigned, played_time_total int(11) unsigned, played_time_level int(11) unsigned, create_time bigint(20) unsigned, logout_time bigint(20) unsigned, rest_bonus float, reset_talents_multiplier int(11) unsigned, reset_talents_time bigint(20) unsigned, death_expire_time bigint(20) unsigned, stable_slots tinyint(1) unsigned, bank_bag_slots tinyint(1) unsigned, extra_flags int(11) unsigned, honor_rank_points float, honor_highest_rank int(11) unsigned, honor_standing int(11) unsigned, honor_last_week_hk int(11) unsigned, honor_last_week_cp float, honor_stored_hk int(11), honor_stored_dk int(11), watched_faction int(11), drunk smallint(5) unsigned, health int(10) unsigned, power1 int(10) unsigned, power2 int(10) unsigned, power3 int(10) unsigned, power4 int(10) unsigned, power5 int(10) unsigned, explored_zones longtext?, equipment_cache longtext?, ammo_id int(10) unsigned, action_bars tinyint(3) unsigned, deleted_account int(11) unsigned?, deleted_name varchar(12)?, deleted_time bigint(20)?, world_phase_mask int(11)?
- `group_member`: group_id int(11) unsigned PK, member_guid int(11) unsigned PK, assistant tinyint(1) unsigned, subgroup smallint(6) unsigned
- `guild_member`: guild_id int(6) unsigned, guid int(11) unsigned PK, rank tinyint(2) unsigned, player_note varchar(255), officer_note varchar(255)
- `instance`: id int(11) unsigned PK, map int(11) unsigned, reset_time bigint(40), data longtext?
- `item_instance`: guid int(10) unsigned PK, item_id mediumint(8) unsigned, owner_guid int(10) unsigned, creator_guid int(10) unsigned, gift_creator_guid int(10) unsigned, count int(10) unsigned, duration int(10), charges tinytext?, flags mediumint(8) unsigned, enchantments text, random_property_id smallint(5), durability smallint(5) unsigned, text int(10) unsigned, generated_loot tinyint(4)?
- `item_loot`: guid int(11) unsigned PK, owner_guid int(11) unsigned, item_id int(11) unsigned PK, amount int(11) unsigned, property int(11)
- `mail`: id int(11) unsigned PK, message_type tinyint(3) unsigned, stationery tinyint(3), mail_template_id mediumint(8) unsigned, sender_guid int(11) unsigned, receiver_guid int(11) unsigned, subject longtext?, item_text_id int(11) unsigned, has_items tinyint(3) unsigned, expire_time bigint(40), deliver_time bigint(40), money int(11) unsigned, cod int(11) unsigned, checked tinyint(3) unsigned
- `mail_items`: mail_id int(11) unsigned PK, item_guid int(11) unsigned PK, item_id int(11) unsigned, receiver_guid int(11) unsigned

*`?` = nullable, `PK` = primary key column.*


---

<!-- verify: failed-members | invented: LoginQueryHolder` -->

---

<!-- verify: boundary-bleed | foreign: callback, GetGuid, process, update, WorldSession -->
