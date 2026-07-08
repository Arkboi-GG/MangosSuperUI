# GuildMgr

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# GuildMgr

**Purpose & Responsibilities**

`GuildMgr` is the singleton manager responsible for the lifecycle, persistence, and lookup of **Guilds** and **Guild Petitions** (charters) within the server. It acts as the central authority for two distinct but related subsystems:

1.  **Guild Management:** It maintains an in-memory cache of all active `Guild` objects, indexed by ID. It handles loading guilds from the database at startup, adding/removing them during runtime, and providing fast lookups by ID, name, or leader GUID. It also maintains a secondary index mapping player GUIDs to their guild IDs for rapid membership checks.
2.  **Petition Management:** It manages the creation, signing, and deletion of guild charters (`Petition`). Before a guild can be formally created, players must sign a petition. `GuildMgr` tracks these petitions, their signatures (`PetitionSignature`), and ensures data consistency between the petition object and the database.

The class uses `std::shared_timed_mutex` to protect its internal maps (`m_GuildMap`, `m_petitionMap`, `m_guid2guild`) from concurrent access, allowing multiple readers (lookups) but exclusive access for writers (add/remove/load).

## Member-by-Member Behavior

### Guild Lifecycle and Lookup

*   **`GuildMgr` / `~GuildMgr`**: The constructor initializes the singleton. The destructor cleans up all dynamically allocated `Guild` objects in `m_GuildMap` and calls `CleanUpPetitions()` to free all `Petition` and `PetitionSignature` objects.
*   **`AddGuild`**: Inserts a newly created `Guild` object into `m_GuildMap` keyed by its ID. It acquires an exclusive lock on `m_guildMutex`. This is called after a guild is successfully loaded from DB or created via petition turn-in.
*   **`RemoveGuild`**: Removes a `Guild` from `m_GuildMap` by ID. It acquires an exclusive lock. This is called when a guild is disbanded. Note: It does *not* delete the `Guild` object itself; the caller is responsible for deletion (see `game_Guild_Guild/Disband`).
*   **`GetGuildById`**: Retrieves a `Guild` pointer by its numeric ID. Uses a shared lock. Returns `nullptr` if not found. This is the most frequently called lookup method, used by nearly all guild-related opcodes.
*   **`GetGuildByName`**: Iterates through `m_GuildMap` to find a guild matching the given name string. Uses a shared lock. Linear scan performance ($O(N)$). Used for commands like `/guild rename` or `/guild invite` where the user provides a name.
*   **`GetGuildByLeader`**: Iterates through `m_GuildMap` to find a guild where the leader's GUID matches the provided GUID. Used primarily during character deletion to check if the character being deleted is a guild leader.
*   **`GetGuildNameById`**: A convenience wrapper that retrieves the guild by ID and returns its name string. Returns an empty string if the guild doesn't exist.

### Player-Guild Association Index

*   **`GuildMemberAdded`**: Updates the `m_guid2guild` map, associating a player's low GUID with their guild ID. Acquires an exclusive lock on `m_guid2GuildMutex`. Called when a player joins a guild.
*   **`GuildMemberRemoved`**: Removes the association from `m_guid2guild`. Called when a player leaves or is removed from a guild.
*   **`GetPlayerGuild`**: Looks up a player's guild ID in `m_guid2guild` and then calls `GetGuildById` to return the actual `Guild` object. This provides a fast path to determine if a player is in a guild and which one, without iterating all guilds.

### Petition Lifecycle

*   **`CreatePetition`**: Creates a new `Petition` object, sets its team (Alliance/Horde) based on the creating player, saves it to the database, and inserts it into `m_petitionMap`. Acquires an exclusive lock on `m_petitionsMutex`.
*   **`DeletePetition`**: Removes the petition from `m_petitionMap` and calls `Petition::Delete()` to remove associated records from the database. Then deletes the C++ object.
*   **`GetPetitionById`**: Retrieves a `Petition` by its unique ID. Shared lock.
*   **`GetPetitionByCharterGuid`**: Finds a petition by the GUID of the physical charter item. Shared lock. Used when a player interacts with the charter item.
*   **`GetPetitionByOwnerGuid`**: Finds a petition owned by a specific player. Shared lock. Used to prevent a player from owning multiple open petitions.
*   **`DeletePetitionSignaturesByPlayer`**: Iterates through all petitions (except one optionally excluded by ID) and removes any signatures belonging to the specified player GUID. This is typically called when a player is deleted or logs out to clean up stale signatures.

### Database Loading

*   **`LoadGuilds`**: Called at server startup.
    1.  Queries the `guild` table for basic guild info.
    2.  Queries `guild_rank` and `guild_member` tables (joined with `characters`) for ranks and member details.
    3.  Iterates through each guild row:
        *   Creates a new `Guild` object.
        *   Calls `Guild::LoadGuildFromDB`, `LoadRanksFromDB`, `LoadMembersFromDB`, and `CheckGuildStructure`.
        *   If any step fails, the guild is disbanded (deleted) and skipped.
        *   Loads the event log via `Guild::LoadGuildEventLogFromDB`.
        *   Adds the valid guild to the manager via `AddGuild`.
    4.  Cleans up old event log entries exceeding the configured limit.
*   **`LoadPetitions`**: Called at server startup or via reload command.
    1.  Clears existing petitions via `CleanUpPetitions`.
    2.  Queries `petition` table.
    3.  Queries `petition_sign` table.
    4.  Iterates through petitions, creating `Petition` objects and loading them via `Petition::LoadFromDB`.
    5.  Iterates through signatures, linking them to their respective `Petition` objects.
    6.  **Data Integrity Checks**:
        *   If a signature references a non-existent petition, it logs an error and deletes the orphaned signature from the DB.
        *   If a signature's `owner_guid` mismatches the petition's `owner_guid`, it logs an error and updates the DB to fix the mismatch.

### Petition Class Methods (Internal Helpers)

*   **`Petition::~Petition`**: Destructor that cleans up all `PetitionSignature` objects in `m_signatures`.
*   **`Petition::LoadFromDB`**: Populates the `Petition` object's fields from a query result row. Sets the team based on the owner's GUID.
*   **`Petition::Delete`**: Deletes the petition record and all its signatures from the database using a transaction.
*   **`Petition::SaveToDB`**: Inserts a new petition record into the database.
*   **`Petition::Rename`**: Updates the petition name in the database and memory. Escapes the string to prevent SQL injection.
*   **`Petition::BuildSignatureData`**: Serializes the list of signature GUIDs into a `WorldPacket` for sending to clients.
*   **`Petition::GetSignatureForPlayer`**: Checks if a player has already signed. It first checks by Account ID (to prevent alt-signing), then by Player GUID.
*   **`Petition::GetSignatureForAccount` / `GetSignatureForPlayerGuid`**: Helper methods to search the signature list by account or GUID.
*   **`Petition::AddSignature`**: Adds a signature object to the internal list.
*   **`Petition::AddNewSignature`**: Validates that the petition isn't already complete, creates a new `PetitionSignature`, saves it to DB, and adds it to the list.
*   **`Petition::DeleteSignatureByPlayer`**: Removes a signature from the list and deletes the object.
*   **`PetitionSignature::PetitionSignature`**: Constructor storing the petition pointer, signer's GUID, and account ID.
*   **`PetitionSignature::SaveToDB`**: Inserts the signature record into the `petition_sign` table.

## Cross-Unit Boundaries

### Guild Operations
*   **`game_Guild_Guild`**:
    *   `GuildMgr::AddGuild` is called by `game_Guild_Guild/AddMember` (indirectly, after creation) and `game_Guild_Guild/LoadMembersFromDB` (during load).
    *   `GuildMgr::RemoveGuild` is called by `game_Guild_Guild/Disband`.
    *   `GuildMgr::LoadGuilds` calls various `game_Guild_Guild` methods (`LoadGuildFromDB`, `LoadRanksFromDB`, etc.) to populate the guild object.
*   **`ChatHandler` / `WorldSession`**:
    *   Almost all guild-related chat commands and opcodes (e.g., `HandleGuildCreateCommand`, `HandleGuildInfoOpcode`, `HandleGuildInviteCommand`) call `GuildMgr::GetGuildById` or `GetGuildByName` to retrieve the target guild object.
    *   `WorldSession.PetitionsHandler` methods interact heavily with `GuildMgr` for petition creation, signing, and turning in.

### Petition Operations
*   **`WorldSession.PetitionsHandler`**:
    *   `HandlePetitionBuyOpcode` calls `GuildMgr::CreatePetition` and `GetPetitionByOwnerGuid`.
    *   `HandlePetitionSignOpcode` calls `GetPetitionByCharterGuid` and `Petition::AddNewSignature`.
    *   `HandleTurnInPetitionOpcode` calls `GetPetitionById`, `DeletePetition`, and eventually triggers guild creation (which calls `AddGuild`).
    *   `HandlePetitionRenameOpcode` calls `GetPetitionByCharterGuid` and `Petition::Rename`.
*   **`Player.Main`**:
    *   `Player::DestroyItem` calls `GetPetitionById` and `GetPetitionByCharterGuid` to handle charter items.
    *   `Player::RemovePetitionsAndSigns` calls `DeletePetitionSignaturesByPlayer`.

## Data Model

`GuildMgr` interacts with the following database tables:

*   **`guild`**: Stores core guild information (ID, name, leader GUID, emblem colors, MOTD, creation date).
*   **`guild_rank`**: Stores rank definitions (ID, name, rights) for each guild.
*   **`guild_member`**: Stores membership data (player GUID, rank, notes) linked to a guild ID.
*   **`guild_eventlog`**: Stores historical events (promotions, demotions, etc.). `LoadGuilds` cleans up old entries.
*   **`petition`**: Stores active guild charters (owner GUID, petition GUID, charter item GUID, name).
*   **`petition_sign`**: Stores signatures on petitions (owner GUID, petition GUID, player GUID, player account).
*   **`characters`**: Joined with `guild_member` during `LoadGuilds` to fetch player names, levels, classes, and zones for display in the guild roster.

## Notable Implementation Details

1.  **Mutex Granularity**: `GuildMgr` uses three separate mutexes:
    *   `m_guildMutex`: Protects `m_GuildMap`.
    *   `m_guid2GuildMutex`: Protects `m_guid2guild`.
    *   `m_petitionsMutex`: Protects `m_petitionMap`.
    This allows concurrent operations on guilds and petitions, and even concurrent reads/writes between the guild map and the player-index map, reducing contention.

2.  **Petition Owner Mismatch Repair**: In `LoadPetitions`, if a signature in `petition_sign` has an `owner_guid` that differs from the `owner_guid` in the `petition` table for the same `petition_guid`, the code **automatically updates the database** to fix the mismatch. This is a self-healing mechanism for data corruption.

3.  **Orphaned Signature Cleanup**: Similarly, `LoadPetitions` detects signatures pointing to non-existent petitions and **deletes them from the database**.

4.  **Linear Scans**: `GetGuildByName` and `GetGuildByLeader` perform linear scans over `m_GuildMap`. For servers with thousands of guilds, this could become a bottleneck if called frequently. However, these are typically used for administrative commands or rare lookups, whereas `GetGuildById` (hash map lookup) is used for frequent gameplay interactions.

5.  **Petition Completion Check**: `Petition::IsComplete()` checks if the number of signatures equals `CONFIG_UINT32_MIN_PETITION_SIGNS`. This value is configurable, allowing server operators to adjust the difficulty of creating a guild.

6.  **SQL Injection Prevention**: `Petition::Rename` and `Petition::SaveToDB` use `CharacterDatabase.escape_string()` before inserting names into SQL queries. However, note that `LoadGuilds` and `LoadPetitions` use raw string formatting for DELETE/UPDATE statements with integer IDs, which is safe, but the INSERT/UPDATE for names relies on proper escaping.

7.  **Memory Management**: `GuildMgr` takes ownership of `Guild` and `Petition` objects. Callers must not delete these objects manually unless they have been removed from the manager's maps. The destructor ensures cleanup.

## Member Reference

*   **`GuildMgr`**: Default constructor for the singleton.
*   **`~GuildMgr`**: Destructor that deletes all cached `Guild` and `Petition` objects.
*   **`CleanUpPetitions`**: Private helper that deletes all `Petition` objects in `m_petitionMap` and clears the map.
*   **`GuildMemberAdded`**: Adds a mapping from player GUID to guild ID in `m_guid2guild`.
*   **`AddGuild`**: Inserts a `Guild` object into `m_GuildMap` under its ID.
*   **`GuildMemberRemoved`**: Removes the player GUID entry from `m_guid2guild`.
*   **`RemoveGuild`**: Removes a `Guild` from `m_GuildMap` by ID.
*   **`GetPlayerGuild`**: Looks up a player's guild via `m_guid2guild` and returns the `Guild` object.
*   **`GetGuildById`**: Returns the `Guild` object for a given ID from `m_GuildMap`.
*   **`GetGuildByName`**: Linear scan of `m_GuildMap` to find a guild by name.
*   **`GetGuildByLeader`**: Linear scan of `m_GuildMap` to find a guild by leader GUID.
*   **`GetGuildNameById`**: Returns the name string of a guild by ID.
*   **`LoadGuilds`**: Loads all guilds, ranks, members, and event logs from the database at startup.
*   **`LoadPetitions`**: Loads all petitions and signatures from the database, repairing data inconsistencies.
*   **`~Petition`**: Destructor for `Petition`, cleaning up signatures.
*   **`CreatePetition`**: Creates a new `Petition`, saves it to DB, and adds it to `m_petitionMap`.
*   **`DeletePetition`**: Removes a `Petition` from memory and database.
*   **`GetPetitionById`**: Retrieves a `Petition` by its ID.
*   **`GetPetitionByCharterGuid`**: Retrieves a `Petition` by the GUID of its charter item.
*   **`GetPetitionByOwnerGuid`**: Retrieves a `Petition` by the owner's player GUID.
*   **`DeletePetitionSignaturesByPlayer`**: Removes all signatures by a specific player from all petitions.
*   **`LoadFromDB`**: (`Petition`) Populates a `Petition` object from a database query result.
*   **`Delete`**: (`Petition`) Deletes the petition and its signatures from the database.
*   **`BuildSignatureData`**: (`Petition`) Serializes signature GUIDs into a network packet.
*   **`Rename`**: (`Petition`) Updates the petition name in the database and memory.
*   **`SaveToDB`**: (`Petition`) Inserts a new petition record into the database.
*   **`GetSignatureForPlayer`**: (`Petition`) Checks if a player has signed, checking account first, then GUID.
*   **`GetSignatureForAccount`**: (`Petition`) Finds a signature by account ID.
*   **`GetSignatureForPlayerGuid`**: (`Petition`) Finds a signature by player GUID.
*   **`AddSignature`**: (`Petition`) Adds a signature object to the internal list.
*   **`AddNewSignature`**: (`Petition`) Creates, saves, and adds a new signature if the petition is not complete.
*   **`DeleteSignatureByPlayer`**: (`Petition`) Removes a signature by player GUID from the list.
*   **`PetitionSignature`**: Constructor for `PetitionSignature`, storing petition, player GUID, and account ID.
*   **`SaveToDB#2`**: (`PetitionSignature`) Inserts the signature record into the `petition_sign` table.

---

<!-- machine-true, projected from graph.json -->

## Map — GuildMgr

*Source:* GuildMgr.cpp, GuildMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GuildMgr | ctor | — | — | — |
| ~GuildMgr | dtor | — | — | — |
| CleanUpPetitions | method | — | — | — |
| GuildMemberAdded | method | — | game_Guild_Guild/AddMember, game_Guild_Guild/LoadMembersFromDB | — |
| AddGuild | method | Guild/GetId | ChatHandler.MiscCommands/HandleGuildCreateCommand, WorldSession.GuildHandler/HandleGuildCreateOpcode, WorldSession.PetitionsHandler/HandleTurnInPetitionOpcode | — |
| GuildMemberRemoved | method | — | game_Guild_Guild/DelMember | — |
| RemoveGuild | method | — | game_Guild_Guild/Disband | — |
| GetPlayerGuild | method | — | AsyncCommandHandlers/HandleResponse, Player.Main/DeleteFromDB | — |
| GetGuildById | method | — | ChatHandler.MiscCommands/HandleGuildRankCommand, ChatHandler.MiscCommands/HandleGuildUninviteCommand, Player.Main/IsAllowedWhisperFrom, Player.Main/_LoadGuild, WorldSession.CharacterHandler/HandlePlayerLogin, WorldSession.ChatHandler/HandleChatMessageOpcode, WorldSession.GuildHandler/HandleGuildAcceptOpcode, WorldSession.GuildHandler/HandleGuildAddRankOpcode, WorldSession.GuildHandler/HandleGuildChangeInfoTextOpcode, WorldSession.GuildHandler/HandleGuildDeclineOpcode, WorldSession.GuildHandler/HandleGuildDelRankOpcode, WorldSession.GuildHandler/HandleGuildDemoteOpcode, WorldSession.GuildHandler/HandleGuildDisbandOpcode, WorldSession.GuildHandler/HandleGuildInfoOpcode, WorldSession.GuildHandler/HandleGuildInviteOpcode, WorldSession.GuildHandler/HandleGuildLeaderOpcode, WorldSession.GuildHandler/HandleGuildLeaveOpcode, WorldSession.GuildHandler/HandleGuildMOTDOpcode, WorldSession.GuildHandler/HandleGuildPromoteOpcode, WorldSession.GuildHandler/HandleGuildQueryOpcode, WorldSession.GuildHandler/HandleGuildRankOpcode, WorldSession.GuildHandler/HandleGuildRemoveOpcode, WorldSession.GuildHandler/HandleGuildRosterOpcode, WorldSession.GuildHandler/HandleGuildSetOfficerNoteOpcode, WorldSession.GuildHandler/HandleGuildSetPublicNoteOpcode, WorldSession.GuildHandler/HandleSaveGuildEmblemOpcode, WorldSession.Main/LogoutPlayer | — |
| GetGuildByName | method | Guild/GetName | ChatHandler.LookupCommands/HandleLookupGuildCommand, ChatHandler.MiscCommands/HandleGuildDeleteCommand, ChatHandler.MiscCommands/HandleGuildInviteCommand, ChatHandler.MiscCommands/HandleGuildRenameCommand, ChatHandler.MiscCommands/HandleGuildShowLogCommand, game_Guild_Guild/Create#2, WorldSession.PetitionsHandler/HandlePetitionBuyOpcode, WorldSession.PetitionsHandler/HandlePetitionRenameOpcode, WorldSession.PetitionsHandler/HandleTurnInPetitionOpcode | — |
| GetGuildByLeader | method | Guild/GetLeaderGuid, ObjectGuid/operator== | WorldSession.CharacterHandler/HandleCharDeleteOpcode | — |
| GetGuildNameById | method | Guild/GetName | WorldSession.MiscHandler/operator() | — |
| LoadGuilds | method | Database/PExecute#2, Database/Query, game_Guild_Guild/CheckGuildStructure, game_Guild_Guild/Disband, game_Guild_Guild/Guild, game_Guild_Guild/LoadGuildEventLogFromDB, game_Guild_Guild/LoadGuildFromDB, game_Guild_Guild/LoadMembersFromDB, game_Guild_Guild/LoadRanksFromDB, Log.Main/Out, ProgressBar/BarGoLink, ProgressBar/BarGoLink#3, ProgressBar/step, QueryResult/GetRowCount, QueryResult/NextRow, World/getConfig#4 | World/SetInitialWorldSettings | characters, guild, guild_eventlog, guild_member, guild_rank |
| LoadPetitions | method | Database/PExecute#2, Database/Query, Field/GetUInt32, Log.Main/Out, ObjectGuid/GetCounter, ObjectGuid/ObjectGuid#2, ObjectGuid/operator!=, Petition/GetId, Petition/GetOwnerGuid, Petition/Petition, PetitionSignature/PetitionSignature, ProgressBar/BarGoLink, ProgressBar/BarGoLink#3, ProgressBar/step, QueryResult/Fetch, QueryResult/GetRowCount, QueryResult/NextRow | ChatHandler.ServerCommands/HandleReloadPetitions, World/SetInitialWorldSettings | petition, petition_sign |
| ~Petition | dtor | — | — | — |
| CreatePetition | method | Object/GetObjectGuid, Petition/GetId, Petition/Petition#2, Petition/SetTeam, Player.Main/GetTeam | WorldSession.PetitionsHandler/HandlePetitionBuyOpcode | — |
| DeletePetition | method | Petition/GetId | game_Objects_Item/DeleteAllFromDB#2, Player.Main/DestroyItem, WorldSession.PetitionsHandler/HandleTurnInPetitionOpcode | — |
| GetPetitionById | method | — | Player.Main/DestroyItem, WorldSession.PetitionsHandler/HandleOfferPetitionOpcode, WorldSession.PetitionsHandler/HandlePetitionQueryOpcode, WorldSession.PetitionsHandler/HandlePetitionRenameOpcode, WorldSession.PetitionsHandler/HandlePetitionShowSignOpcode, WorldSession.PetitionsHandler/HandleTurnInPetitionOpcode | — |
| GetPetitionByCharterGuid | method | ObjectGuid/operator==, Petition/GetCharterGuid | game_Objects_Item/DeleteAllFromDB#2, WorldSession.PetitionsHandler/HandlePetitionDeclineOpcode, WorldSession.PetitionsHandler/HandlePetitionSignOpcode | — |
| GetPetitionByOwnerGuid | method | ObjectGuid/operator==, Petition/GetOwnerGuid | WorldSession.PetitionsHandler/HandlePetitionBuyOpcode | — |
| DeletePetitionSignaturesByPlayer | method | — | Player.Main/RemovePetitionsAndSigns | — |
| LoadFromDB | method | Field/GetString, Field/GetUInt32, Log.Main/Out, ObjectGuid/ObjectGuid#2, ObjectMgr/GetPlayerTeamByGUID, QueryResult/Fetch | — | — |
| Delete | method | Database/BeginTransaction, Database/CommitTransaction, Database/PExecute#2 | — | petition, petition_sign |
| BuildSignatureData | method | ByteBuffer/operator<<#4, ObjectGuid/operator<<, PetitionSignature/GetSignatureGuid | WorldSession.PetitionsHandler/HandleOfferPetitionOpcode, WorldSession.PetitionsHandler/HandlePetitionShowSignOpcode | — |
| Rename | method | Database/escape_string, Database/PExecute#2, Log.Main/Out | WorldSession.PetitionsHandler/HandlePetitionRenameOpcode | petition |
| SaveToDB | method | Database/escape_string, Database/PExecute#2, ObjectGuid/GetCounter | — | petition |
| GetSignatureForPlayer | method | Object/GetObjectGuid, Player.Main/GetSession, WorldSession.Main/GetAccountId | WorldSession.PetitionsHandler/HandlePetitionSignOpcode | — |
| GetSignatureForAccount | method | PetitionSignature/GetSignatureAccountId | — | — |
| GetSignatureForPlayerGuid | method | ObjectGuid/operator==, PetitionSignature/GetSignatureGuid | — | — |
| AddSignature | method | — | — | — |
| AddNewSignature | method | Petition/IsComplete | WorldSession.PetitionsHandler/HandlePetitionSignOpcode | — |
| DeleteSignatureByPlayer | method | ObjectGuid/operator==, PetitionSignature/GetSignatureGuid | — | — |
| PetitionSignature | ctor | Object/GetObjectGuid, Player.Main/GetSession, WorldSession.Main/GetAccountId | — | — |
| SaveToDB#2 | method | Database/PExecute#2, ObjectGuid/GetCounter, Petition/GetId, Petition/GetOwnerGuid | — | petition_sign |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `characters`: guid int(11) unsigned PK, account int(11) unsigned, name varchar(12), race tinyint(3) unsigned, class tinyint(3) unsigned, gender tinyint(3) unsigned, skin tinyint(3) unsigned, face tinyint(3) unsigned, hair_style tinyint(3) unsigned, hair_color tinyint(3) unsigned, facial_hair tinyint(3) unsigned, level tinyint(3) unsigned, xp int(10) unsigned, money int(10) unsigned, character_flags int(10) unsigned, zone int(11) unsigned, map int(11) unsigned, instance int(11) unsigned, position_x float, position_y float, position_z float, orientation float, transport_guid bigint(20) unsigned, transport_x float, transport_y float, transport_z float, transport_o float, known_taxi_mask longtext?, current_taxi_path text?, online tinyint(3) unsigned, played_time_total int(11) unsigned, played_time_level int(11) unsigned, create_time bigint(20) unsigned, logout_time bigint(20) unsigned, rest_bonus float, reset_talents_multiplier int(11) unsigned, reset_talents_time bigint(20) unsigned, death_expire_time bigint(20) unsigned, stable_slots tinyint(1) unsigned, bank_bag_slots tinyint(1) unsigned, extra_flags int(11) unsigned, honor_rank_points float, honor_highest_rank int(11) unsigned, honor_standing int(11) unsigned, honor_last_week_hk int(11) unsigned, honor_last_week_cp float, honor_stored_hk int(11), honor_stored_dk int(11), watched_faction int(11), drunk smallint(5) unsigned, health int(10) unsigned, power1 int(10) unsigned, power2 int(10) unsigned, power3 int(10) unsigned, power4 int(10) unsigned, power5 int(10) unsigned, explored_zones longtext?, equipment_cache longtext?, ammo_id int(10) unsigned, action_bars tinyint(3) unsigned, deleted_account int(11) unsigned?, deleted_name varchar(12)?, deleted_time bigint(20)?, world_phase_mask int(11)?
- `guild`: guild_id int(6) unsigned PK, name varchar(255), leader_guid int(6) unsigned, emblem_style int(5), emblem_color int(5), border_style int(5), border_color int(5), background_color int(5), info text, motd varchar(255), create_date bigint(20)
- `guild_eventlog`: guild_id int(11) PK, log_guid int(11) PK, event_type tinyint(1), player_guid1 int(11), player_guid2 int(11), new_rank tinyint(2), timestamp bigint(20)
- `guild_member`: guild_id int(6) unsigned, guid int(11) unsigned PK, rank tinyint(2) unsigned, player_note varchar(255), officer_note varchar(255)
- `guild_rank`: guild_id int(6) unsigned PK, id int(11) unsigned PK, name varchar(255), rights int(3) unsigned
- `petition`: owner_guid int(10) unsigned PK, petition_guid int(10) unsigned?, charter_guid int(10) unsigned?, name varchar(255)
- `petition_sign`: owner_guid int(10) unsigned, petition_guid int(11) unsigned PK, player_guid int(11) unsigned PK, player_account int(11) unsigned

*`?` = nullable, `PK` = primary key column.*

