# game_Guild_Guild

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Guild

The `Guild` class and its associated `MemberSlot` struct constitute the core server-side representation of a player guild within the WoWVMaNGOS emulator. This unit manages the lifecycle of a guild entity, including creation, membership management, rank hierarchy, communication broadcasting, and persistence to the database. It acts as the central authority for guild state, coordinating with `WorldSession` handlers for user input, `GuildMgr` for global registry operations, and `Player` objects for real-time state synchronization.

## Purpose & Responsibilities

The primary responsibility of `Guild` is to maintain the logical and persistent state of a single guild. Key responsibilities include:

1.  **Lifecycle Management:** Handling guild creation (via petition or direct command), renaming, and disbanding.
2.  **Membership Control:** Adding and removing players, tracking their online status, level, class, and zone. It ensures data integrity by validating player existence and handling edge cases like deleted characters or broken database records during load.
3.  **Rank Hierarchy:** Managing a strict hierarchy of ranks (0–9), where lower IDs denote higher authority. It enforces rules such as having exactly one Guild Master and maintaining a minimum of five ranks.
4.  **Communication:** Broadcasting chat messages, roster updates, and system events (like promotions or disbands) to relevant subsets of members (all, officers, or specific ranks).
5.  **Persistence:** Synchronizing in-memory state with the `guild`, `guild_member`, `guild_rank`, and `guild_eventlog` database tables. It performs automatic repairs on corrupted database structures during load.

## Member-by-Member Behavior

### Lifecycle and Initialization

**`Guild` (Constructor)** initializes the guild object with default values. Emblem colors and styles are set to `-1` (indicating no emblem), and internal counters for accounts and event logs are zeroed.

**`~Guild` (Destructor)** is empty, relying on the owning `GuildMgr` or caller to handle cleanup if necessary.

**`Create` (Overload 1)** creates a guild from a `Petition`. It delegates to the second `Create` overload to establish the base guild structure, then iterates through the petition's signature list to add each signer as a member using the lowest available rank.

**`Create` (Overload 2)** establishes a new guild from scratch. It performs several critical checks:
1.  Verifies the guild name is unique via `GuildMgr`.
2.  Checks the guild name against the `Anticheat` library's spam filter.
3.  Generates a new unique `guild_id` via `ObjectMgr`.
4.  Inserts the guild record into the `guild` table and deletes any stale member records for that ID (though a new ID should have none).
5.  Calls `CreateDefaultGuildRanks` to populate the initial five ranks.
6.  Adds the leader as a member with the `GR_GUILDMASTER` rank.

**`CreateDefaultGuildRanks`** inserts five standard ranks into the `guild_rank` table: Guild Master, Officer, Veteran, Member, and Initiate. It assigns appropriate rights (e.g., only Masters and Officers have full rights; others have chat permissions).

**`Disband`** dissolves the guild. It broadcasts a disband event, iteratively removes all members (calling `DelMember` with `isDisbanding=true` to skip individual DB updates for efficiency), and then executes a transaction to delete all records from `guild`, `guild_rank`, and `guild_eventlog`. Finally, it notifies `GuildMgr` to remove the guild from the global cache.

### Membership Management

**`AddMember`** adds a player to the guild. It first checks if the player is already in a guild (either online via `ObjectAccessor` or offline via the in-memory `members` map). It clears any pending petition signatures for the player to prevent duplicate joins. It populates a `MemberSlot` with player data, fetching from the live `Player` object if online, or from `ObjectMgr`'s cached player data if offline. It validates the player's level and class against known valid ranges. If valid, it inserts the member into the `guild_member` table, updates the in-memory map, and synchronizes the online player's state if they are logged in.

**`DelMember`** removes a player from the guild. If the player being removed is the current leader and this is not part of a disband operation, it automatically promotes the highest-ranking remaining member (lowest `RankId`) to leader. It removes the member from the in-memory map, deletes their record from `guild_member`, and updates the online player's state if applicable. It returns `true` if the guild becomes empty, signaling the caller to potentially disband the guild.

**`ChangeRank`** updates a member's rank. It updates the in-memory `MemberSlot` and the `guild_member` table. If the player is online, it synchronizes the change to their `Player` object.

**`SetLeader`** explicitly sets a new leader. It updates the `guild` table's `leader_guid` and changes the specified member's rank to `GR_GUILDMASTER`.

### Rank Management

**`CreateRank`** adds a new rank to the guild. It appends the rank to the in-memory list and inserts it into the `guild_rank` table with the next sequential ID. It enforces the maximum rank count (`GUILD_RANKS_MAX_COUNT`).

**`DelRank`** removes the lowest rank (highest ID). It deletes the rank from the `guild_rank` table and removes it from the in-memory list. It prevents deletion if the guild would fall below the minimum rank count (`GUILD_RANKS_MIN_COUNT`).

**`SetRankName`** and **`SetRankRights`** update the name or permissions of an existing rank, persisting changes to the `guild_rank` table.

**`GetRankName`** and **`GetRankRights`** retrieve metadata for a specific rank ID from the in-memory list.

**`AddRank`** is a protected helper that simply pushes a `RankInfo` struct onto the in-memory `m_Ranks` vector.

### Communication and Broadcasting

**`BroadcastToGuild`** sends a chat message to all online members who have the `GR_RIGHT_GCHATLISTEN` permission. It constructs a chat packet using `ChatHandler` and filters recipients based on ignore lists and rank rights.

**`BroadcastToOfficers`** functions similarly but targets members with `GR_RIGHT_OFFCHATLISTEN` permission.

**`BroadcastPacket`** sends a raw `WorldPacket` to all online members.

**`BroadcastPacketToRank`** sends a raw `WorldPacket` only to online members holding a specific `rankId`.

**`BroadcastEvent`** constructs and sends a `SMSG_GUILD_EVENT` packet to all online members. This is used for system notifications like promotions, demotions, and logins/logouts.

**`Roster`** generates the `SMSG_GUILD_ROSTER` packet. It serializes the MOTD, guild info, rank rights, and detailed member information (GUID, name, level, class, zone, notes). It handles online vs. offline members differently: online members get fresh data from their `Player` object, while offline members use cached data from their `MemberSlot`. It respects packet size limits and hides officer notes from players lacking the `GR_RIGHT_VIEWOFFNOTE` permission.

**`Query`** generates the `SMSG_GUILD_QUERY_RESPONSE` packet, providing basic guild info (ID, name, ranks, emblem) to a requesting session.

### Notes and Metadata

**`SetPNOTE`** and **`SetOFFNOTE`** (on `MemberSlot`) update the public or officer note for a member. They escape the string and execute an `UPDATE` on the `guild_member` table.

**`SetMOTD`** and **`SetGINFO`** update the guild's Message of the Day and general info text, persisting them to the `guild` table.

**`SetEmblem`** updates the guild's visual emblem settings (style, colors) in the `guild` table.

**`Rename`** changes the guild's name in memory and persists it to the `guild` table.

### Database Loading and Integrity

**`LoadGuildFromDB`** populates the guild's basic attributes (ID, name, leader, emblem, MOTD, info, creation date) from a query result.

**`LoadRanksFromDB`** loads ranks from the `guild_rank` table. It performs significant integrity checking:
1.  It expects ranks to be sequential (0, 1, 2...). If gaps or duplicates are found, it marks the data as broken.
2.  If the rank list is empty or fewer than 5 ranks exist, it regenerates default ranks.
3.  If the sequence is broken, it deletes all existing ranks and re-inserts them with corrected sequential IDs.

**`LoadMembersFromDB`** loads members from the `guild_member` table. It validates each member's data:
1.  It checks for invalid levels or classes, deleting the member record if data is corrupted.
2.  It attempts to recover missing zone IDs from the `characters` table if the stored zone is zero.
3.  It deletes records for members with empty names (likely deleted characters).
4.  It ensures members are assigned to a valid rank, falling back to the lowest rank if the stored rank ID is out of bounds.

**`CheckGuildStructure`** is called after loading to ensure leadership consistency. If the stored leader GUID is not a member or lacks the Guild Master rank, it attempts to fix this by promoting the highest-ranking member. It also ensures only one Guild Master exists by demoting any other members with that rank to Officer.

### Event Logging

**`LogGuildEvent`** records a guild event (invite, join, promote, etc.) to the `guild_eventlog` table. It maintains a circular buffer of events in memory (`m_GuildEventLog`) limited by `GUILD_EVENTLOG_MAX_RECORDS`. It uses a `REPLACE INTO` statement to manage the log entries, cycling through `log_guid` values.

**`LoadGuildEventLogFromDB`** loads the most recent events from the database into the in-memory list.

**`GetGuildInviter`** searches the in-memory event log to find who invited a specific player.

**`DisplayGuildEventLog`** is a stub function marked as "Inexistant packet" in comments, indicating it is not currently implemented for client display.

**`GuildEventLogTypeToString`** converts numeric event types to human-readable strings for debugging or admin commands.

### Utility and State Helpers

**`GetAccountsNumber`** calculates the number of unique accounts in the guild. It uses lazy evaluation, caching the result in `m_accountsNumber` until invalidated by `UpdateAccountsNumber`.

**`UpdateAccountsNumber`** resets the cached account count to zero, forcing a recalculation on the next call to `GetAccountsNumber`.

**`GetGuildRosterFlagsForPlayer`** determines the online status flags (Online, AFK, DND) for a player for inclusion in the roster packet.

## Cross-Unit Boundaries

### Collaboration with `WorldSession`
*   **Called By:** Various `WorldSession` handlers (e.g., `HandleGuildCreateOpcode`, `HandleGuildInviteOpcode`) invoke `Guild` methods to process player requests.
*   **Direction:** `WorldSession` -> `Guild`.
*   **Why:** `WorldSession` handles network I/O and opcode dispatching, delegating the business logic of guild manipulation to the `Guild` class.

### Collaboration with `GuildMgr`
*   **Calls Out:** `Guild` calls `GuildMgr::GetGuildByName` to check for name uniqueness, `GuildMgr::GenerateGuildId` to get a new ID, `GuildMgr::GuildMemberAdded/Removed` to notify the manager of membership changes, and `GuildMgr::RemoveGuild` upon disbanding.
*   **Called By:** `GuildMgr::LoadGuilds` invokes `LoadGuildFromDB`, `LoadRanksFromDB`, `LoadMembersFromDB`, and `CheckGuildStructure` during server startup.
*   **Why:** `GuildMgr` maintains the global map of all guilds. `Guild` relies on it for global lookups and registration, while `GuildMgr` relies on `Guild` for detailed loading and validation.

### Collaboration with `Player` and `ObjectAccessor`
*   **Calls Out:** `Guild` frequently calls `Player` methods (e.g., `GetName`, `GetLevel`, `SetRank`) to synchronize state with online players. It uses `ObjectAccessor::FindPlayer` to locate online players by GUID.
*   **Called By:** `Player::LogoutPlayer` calls `SetMemberStats` and `UpdateLogoutTime` on the member's slot. `Player::DeleteFromDB` calls `DelMember` to clean up guild membership when a character is deleted.
*   **Why:** Ensures that the guild's view of a player matches the player's actual state, and vice versa.

### Collaboration with `Database`
*   **Calls Out:** Extensive use of `CharacterDatabase::PExecute`, `PQuery`, `BeginTransaction`, `CommitTransaction`, and `escape_string`.
*   **Why:** All persistent guild data is stored in the database. `Guild` is responsible for all CRUD operations related to guild tables.

### Collaboration with `Anticheat`
*   **Calls Out:** `Guild::Create` calls `Anticheat::GetAntispam` and `AntispamInterface::filterMessage`.
*   **Why:** To prevent the creation of guilds with spammy or inappropriate names.

## Data Model

The `Guild` unit interacts with four primary database tables:

1.  **`guild`**: Stores core guild metadata.
    *   Used by: `Create`, `Rename`, `SetMOTD`, `SetGINFO`, `SetEmblem`, `SetLeader`, `Disband`, `LoadGuildFromDB`.
    *   Columns accessed: `guild_id`, `name`, `leader_guid`, `info`, `motd`, `create_date`, `emblem_style`, `emblem_color`, `border_style`, `border_color`, `background_color`.

2.  **`guild_member`**: Stores individual member data.
    *   Used by: `AddMember`, `DelMember`, `ChangeRank`, `SetPNOTE`, `SetOFFNOTE`, `LoadMembersFromDB`, `Disband`.
    *   Columns accessed: `guild_id`, `guid`, `rank`, `player_note`, `officer_note`. Note: The code also reads/writes `logout_time`, `name`, `level`, `class`, `zone_id`, and `account_id` in `LoadMembersFromDB` and `AddMember`, although these columns are not explicitly listed in the provided SCHEMA snippet for `guild_member`. The source code implies their existence in the live database.

3.  **`guild_rank`**: Stores rank definitions.
    *   Used by: `CreateDefaultGuildRanks`, `CreateRank`, `DelRank`, `SetRankName`, `SetRankRights`, `LoadRanksFromDB`, `Disband`.
    *   Columns accessed: `guild_id`, `id`, `name`, `rights`.

4.  **`guild_eventlog`**: Stores historical events.
    *   Used by: `LogGuildEvent`, `LoadGuildEventLogFromDB`, `Disband`.
    *   Columns accessed: `guild_id`, `log_guid`, `event_type`, `player_guid1`, `player_guid2`, `new_rank`, `timestamp`.

## Notable Implementation Details

*   **Rank Sequence Enforcement:** The `LoadRanksFromDB` method strictly enforces that rank IDs are sequential starting from 0. If the database contains gaps (e.g., 0, 1, 3), it considers the data broken, deletes all ranks, and re-inserts them with corrected IDs (0, 1, 2). This is a self-healing mechanism for corrupted data.
*   **Leader Succession:** In `DelMember`, if the guild leader is removed, the system automatically promotes the member with the lowest `RankId` (highest privilege) to leader. This ensures the guild remains functional unless it is disbanded.
*   **Lazy Account Counting:** `GetAccountsNumber` uses a lazy evaluation pattern. The count is calculated only once and cached until `UpdateAccountsNumber` is called (which happens on membership changes). This optimizes performance for frequent queries.
*   **Offline Member Data Validation:** During `LoadMembersFromDB`, the code performs rigorous validation of offline member data. It deletes members with invalid levels, classes, or empty names. It also attempts to recover missing zone IDs from the `characters` table. This prevents crashes or inconsistencies caused by deleted characters or corrupted save files.
*   **Spam Filtering:** Guild creation is subject to antispam checks via the `Anticheat` module, preventing abuse of guild names.
*   **Packet Size Limits:** The `Roster` method carefully tracks `spaceLeft` to ensure the generated packet does not exceed `GUILD_ROSTER_MAX_LENGTH`. If the roster is too large, it truncates the list of members sent to the client.
*   **Circular Event Log:** The event log uses a circular buffer approach with `log_guid` cycling modulo `CONFIG_UINT32_GUILD_EVENT_LOG_COUNT`. This allows efficient storage of recent history without unbounded growth.

## Member Reference

**`SetMemberStats`**: Updates the `MemberSlot` with the current player's name, level, class, and zone ID. Called during logout to cache state.

**`UpdateLogoutTime`**: Sets the `LogoutTime` in the `MemberSlot` to the current system time. Called during logout.

**`SetPNOTE`**: Updates the public note for a member in memory and persists it to the `guild_member` table.

**`SetOFFNOTE`**: Updates the officer note for a member in memory and persists it to the `guild_member` table.

**`ChangeRank`**: Changes a member's rank in memory and persists it to the `guild_member` table. Synchronizes with the online player if present.

**`Guild`**: Constructor initializing the guild object with default values.

**`~Guild`**: Destructor, currently empty.

**`Create`**: Overload that creates a guild from a `Petition`, adding all signers as members.

**`Create#2`**: Overload that creates a new guild from scratch, validating name uniqueness and spam, generating an ID, inserting DB records, creating default ranks, and adding the leader.

**`GuildEventLogTypeToString`**: Converts numeric event log types to human-readable strings.

**`CreateDefaultGuildRanks`**: Inserts five standard ranks (Master, Officer, Veteran, Member, Initiate) into the `guild_rank` table.

**`Rename`**: Updates the guild's name in memory and persists it to the `guild` table.

**`AddMember`**: Adds a player to the guild, validating their status, populating their `MemberSlot`, inserting into `guild_member`, and synchronizing online state.

**`SetMOTD`**: Updates the guild's Message of the Day in memory and persists it to the `guild` table.

**`SetGINFO`**: Updates the guild's general info text in memory and persists it to the `guild` table.

**`LoadGuildFromDB`**: Populates the guild's basic attributes from a database query result.

**`CheckGuildStructure`**: Validates and repairs guild leadership consistency after loading, ensuring a valid leader exists and only one Guild Master rank is held.

**`LoadRanksFromDB`**: Loads ranks from the database, enforcing sequential IDs and regenerating defaults if data is corrupted or insufficient.

**`LoadMembersFromDB`**: Loads members from the database, validating data integrity, recovering missing zone IDs, and deleting records for invalid or deleted characters.

**`SetLeader`**: Explicitly sets a new leader, updating the `guild` table and the member's rank.

**`DelMember`**: Removes a player from the guild, handling leader succession if necessary, and cleaning up DB records.

**`BroadcastToGuild`**: Sends a chat message to all online members with listen permissions.

**`BroadcastToOfficers`**: Sends a chat message to all online officers with listen permissions.

**`BroadcastPacket`**: Sends a raw packet to all online members.

**`BroadcastPacketToRank`**: Sends a raw packet to online members of a specific rank.

**`CreateRank`**: Adds a new rank to the guild, appending to the in-memory list and inserting into the `guild_rank` table.

**`AddRank`**: Protected helper to push a `RankInfo` to the in-memory rank list.

**`DelRank`**: Removes the lowest rank from the guild, deleting from the `guild_rank` table and in-memory list.

**`GetRankName`**: Retrieves the name of a rank by ID.

**`GetRankRights`**: Retrieves the rights bitmask of a rank by ID.

**`SetRankName`**: Updates the name of a rank in memory and persists it to the `guild_rank` table.

**`SetRankRights`**: Updates the rights of a rank in memory and persists it to the `guild_rank` table.

**`Disband`**: Dissolves the guild, removing all members, deleting all related DB records, and notifying `GuildMgr`.

**`GetGuildRosterFlagsForPlayer`**: Determines online status flags (Online, AFK, DND) for a player.

**`Roster`**: Generates and sends the `SMSG_GUILD_ROSTER` packet, serializing guild and member data.

**`Query`**: Generates and sends the `SMSG_GUILD_QUERY_RESPONSE` packet with basic guild info.

**`SetEmblem`**: Updates the guild's emblem settings in memory and persists them to the `guild` table.

**`GetAccountsNumber`**: Calculates and caches the number of unique accounts in the guild.

**`DisplayGuildEventLog`**: Stub function, not implemented.

**`LoadGuildEventLogFromDB`**: Loads recent events from the `guild_eventlog` table into the in-memory list.

**`LogGuildEvent`**: Records a guild event to the in-memory list and persists it to the `guild_eventlog` table.

**`GetGuildInviter`**: Searches the in-memory event log to find who invited a specific player.

**`BroadcastEvent`**: Constructs and sends a `SMSG_GUILD_EVENT` packet to all online members.

---

<!-- machine-true, projected from graph.json -->

## Map — game_Guild_Guild

*Source:* Guild.cpp, Guild.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| SetMemberStats | method | Player.Main/GetCachedZoneId, Player.Main/GetName, Unit.Main/GetClass, Unit.Main/GetLevel | WorldSession.Main/LogoutPlayer | — |
| UpdateLogoutTime | method | — | WorldSession.Main/LogoutPlayer | — |
| SetPNOTE | method | Database/escape_string, Database/PExecute#2, ObjectGuid/GetCounter | WorldSession.GuildHandler/HandleGuildSetPublicNoteOpcode | guild_member |
| SetOFFNOTE | method | Database/escape_string, Database/PExecute#2, ObjectGuid/GetCounter | WorldSession.GuildHandler/HandleGuildSetOfficerNoteOpcode | guild_member |
| ChangeRank | method | Database/PExecute#2, ObjectGuid/GetCounter, ObjectMgr/GetPlayer, Player.Main/SetRank | ChatHandler.MiscCommands/HandleGuildRankCommand, WorldSession.GuildHandler/HandleGuildDemoteOpcode, WorldSession.GuildHandler/HandleGuildLeaderOpcode, WorldSession.GuildHandler/HandleGuildPromoteOpcode | guild_member |
| Guild | ctor | — | ChatHandler.MiscCommands/HandleGuildCreateCommand, GuildMgr/LoadGuilds, WorldSession.GuildHandler/HandleGuildCreateOpcode, WorldSession.PetitionsHandler/HandleTurnInPetitionOpcode | — |
| ~Guild | dtor | — | — | — |
| Create | method | Guild/GetLowestRank, ObjectGuid/IsEmpty, Petition/GetId, Petition/GetName, Petition/GetSignatureList, PetitionSignature/GetSignatureGuid | WorldSession.PetitionsHandler/HandleTurnInPetitionOpcode | — |
| Create#2 | method | Anticheat/GetAnticheatLib, Anticheat/GetAntispam, AntispamInterface/filterMessage, Database/BeginTransaction, Database/CommitTransaction, Database/escape_string, Database/PExecute#2, GuildMgr/GetGuildByName, Log.Main/Out, Object/GetObjectGuid, ObjectGuid/GetCounter, ObjectGuid/GetString, ObjectMgr/GenerateGuildId, Player.Main/GetSession, World/LogChat, WorldSession.Main/GetSessionDbLocaleIndex | ChatHandler.MiscCommands/HandleGuildCreateCommand, WorldSession.GuildHandler/HandleGuildCreateOpcode | guild, guild_member |
| GuildEventLogTypeToString | function | — | ChatHandler.MiscCommands/HandleGuildShowLogCommand | — |
| CreateDefaultGuildRanks | method | Database/PExecute#2, ObjectMgr/GetMangosString | — | guild_rank |
| Rename | method | Database/escape_string, Database/PExecute#2 | ChatHandler.MiscCommands/HandleGuildRenameCommand | guild |
| AddMember | method | Database/escape_string, Database/PExecute#2, Guild/GetId, Guild/UpdateAccountsNumber, GuildMgr/GuildMemberAdded, Log.Main/Out, ObjectAccessor/FindPlayerNotInWorld, ObjectGuid/GetCounter, ObjectGuid/GetString, ObjectMgr/GetPlayerDataByGUID, Player.Main/GetCachedZoneId, Player.Main/GetGuildId, Player.Main/GetName, Player.Main/GetSession, Player.Main/RemovePetitionsAndSigns, Player.Main/SetGuildIdInvited, Player.Main/SetInGuild, Player.Main/SetRank, Unit.Main/GetClass, Unit.Main/GetLevel, WorldSession.Main/GetAccountId | ChatHandler.MiscCommands/HandleGuildInviteCommand, WorldSession.GuildHandler/HandleGuildAcceptOpcode | guild_member |
| SetMOTD | method | Database/escape_string, Database/PExecute#2 | WorldSession.GuildHandler/HandleGuildMOTDOpcode | guild |
| SetGINFO | method | Database/escape_string, Database/PExecute#2 | WorldSession.GuildHandler/HandleGuildChangeInfoTextOpcode | guild |
| LoadGuildFromDB | method | Field/GetCppString, Field/GetInt32, Field/GetUInt32, Field/GetUInt64, ObjectGuid/ObjectGuid#2, QueryResult/Fetch | GuildMgr/LoadGuilds | — |
| CheckGuildStructure | method | Guild/GetRank, ObjectGuid/operator!= | GuildMgr/LoadGuilds | — |
| LoadRanksFromDB | method | Database/BeginTransaction, Database/CommitTransaction, Database/escape_string, Database/PExecute#2, Field/GetCppString, Field/GetUInt32, Log.Main/Out, QueryResult/Fetch, QueryResult/NextRow | GuildMgr/LoadGuilds | guild_rank |
| LoadMembersFromDB | method | Database/PExecute#2, Field/GetCppString, Field/GetInt32, Field/GetUInt32, Field/GetUInt64, Field/GetUInt8, Guild/GetId, Guild/GetLowestRank, Guild/UpdateAccountsNumber, GuildMgr/GuildMemberAdded, Log.Main/Out, ObjectGuid/GetString, ObjectGuid/ObjectGuid#2, Player.Main/GetZoneIdFromDB, QueryResult/Fetch, QueryResult/NextRow | GuildMgr/LoadGuilds | guild_member |
| SetLeader | method | Database/PExecute#2, Guild/GetMemberSlot, ObjectGuid/GetCounter | WorldSession.GuildHandler/HandleGuildLeaderOpcode | guild |
| DelMember | method | Database/PExecute#2, Guild/BroadcastEvent, Guild/UpdateAccountsNumber, GuildMgr/GuildMemberRemoved, ObjectGuid/GetCounter, ObjectGuid/ObjectGuid, ObjectGuid/ObjectGuid#2, ObjectGuid/operator==, ObjectMgr/GetPlayer, Player.Main/SetInGuild, Player.Main/SetRank | ChatHandler.MiscCommands/HandleGuildUninviteCommand, Player.Main/DeleteFromDB, WorldSession.GuildHandler/HandleGuildLeaveOpcode, WorldSession.GuildHandler/HandleGuildRemoveOpcode | guild_member |
| BroadcastToGuild | method | ChatHandler.Chat/BuildChatPacket, Guild/HasRankRight, MasterPlayer.Main/GetChatTag, MasterPlayer.Main/GetName, MasterPlayer.Main/GetObjectGuid, MasterPlayer.Main/GetRank, MasterPlayer.Main/GetSession, MasterPlayer.Main/GetSocial, ObjectAccessor/FindMasterPlayer, ObjectGuid/ObjectGuid#2, SocialMgr/HasIgnore, WorldPacket/WorldPacket, WorldSession.Main/GetMasterPlayer, WorldSession.Main/SendPacket | WorldSession.ChatHandler/HandleChatMessageOpcode | — |
| BroadcastToOfficers | method | ChatHandler.Chat/BuildChatPacket, Guild/HasRankRight, MasterPlayer.Main/GetChatTag, MasterPlayer.Main/GetName, MasterPlayer.Main/GetObjectGuid, MasterPlayer.Main/GetRank, MasterPlayer.Main/GetSession, MasterPlayer.Main/GetSocial, ObjectAccessor/FindMasterPlayer, ObjectGuid/ObjectGuid#2, SocialMgr/HasIgnore, WorldPacket/WorldPacket, WorldSession.Main/GetMasterPlayer, WorldSession.Main/SendPacket | WorldSession.ChatHandler/HandleChatMessageOpcode | — |
| BroadcastPacket | method | ObjectAccessor/FindPlayer, ObjectGuid/ObjectGuid#2, Player.Main/GetSession, WorldSession.Main/SendPacket | — | — |
| BroadcastPacketToRank | method | ObjectAccessor/FindPlayer, ObjectGuid/ObjectGuid#2, Player.Main/GetSession, WorldSession.Main/SendPacket | — | — |
| CreateRank | method | Database/escape_string, Database/PExecute#2 | WorldSession.GuildHandler/HandleGuildAddRankOpcode | guild_rank |
| AddRank | method | RankInfo/RankInfo | — | — |
| DelRank | method | Database/PExecute#2, Guild/GetLowestRank | WorldSession.GuildHandler/HandleGuildDelRankOpcode | guild_rank |
| GetRankName | method | — | WorldSession.GuildHandler/HandleGuildDemoteOpcode, WorldSession.GuildHandler/HandleGuildPromoteOpcode | — |
| GetRankRights | method | — | — | — |
| SetRankName | method | Database/escape_string, Database/PExecute#2 | WorldSession.GuildHandler/HandleGuildRankOpcode | guild_rank |
| SetRankRights | method | Database/PExecute#2 | WorldSession.GuildHandler/HandleGuildRankOpcode | guild_rank |
| Disband | method | Database/BeginTransaction, Database/CommitTransaction, Database/PExecute#2, Guild/BroadcastEvent, GuildMgr/RemoveGuild, ObjectGuid/ObjectGuid#2 | ChatHandler.MiscCommands/HandleGuildDeleteCommand, ChatHandler.MiscCommands/HandleGuildUninviteCommand, GuildMgr/LoadGuilds, Player.Main/DeleteFromDB, WorldSession.GuildHandler/HandleGuildDisbandOpcode, WorldSession.GuildHandler/HandleGuildLeaveOpcode, WorldSession.GuildHandler/HandleGuildRemoveOpcode | guild, guild_eventlog, guild_rank |
| GetGuildRosterFlagsForPlayer | function | Player.Main/IsAFK, Player.Main/IsDND | — | — |
| Roster | method | ByteBuffer/operator<<, ByteBuffer/operator<<#10, ByteBuffer/operator<<#7, ByteBuffer/operator<<#9, ByteBuffer/wpos, Guild/HasRankRight, Object/GetObjectGuid, ObjectAccessor/FindPlayer, ObjectGuid/ObjectGuid#2, ObjectGuid/operator<<, Player.Main/GetCachedZoneId, Player.Main/GetRank, Unit.Main/GetClass, Unit.Main/GetLevel, WorldPacket/WorldPacket#4, WorldSession.Main/GetPlayer, WorldSession.Main/SendPacket | WorldSession.GuildHandler/HandleGuildAddRankOpcode, WorldSession.GuildHandler/HandleGuildDelRankOpcode, WorldSession.GuildHandler/HandleGuildRankOpcode, WorldSession.GuildHandler/HandleGuildRosterOpcode, WorldSession.GuildHandler/HandleGuildSetOfficerNoteOpcode, WorldSession.GuildHandler/HandleGuildSetPublicNoteOpcode | — |
| Query | method | ByteBuffer/operator<<, ByteBuffer/operator<<#10, ByteBuffer/operator<<#4, ByteBuffer/operator<<#7, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | WorldSession.GuildHandler/HandleGuildAddRankOpcode, WorldSession.GuildHandler/HandleGuildDelRankOpcode, WorldSession.GuildHandler/HandleGuildQueryOpcode, WorldSession.GuildHandler/HandleGuildRankOpcode, WorldSession.GuildHandler/HandleSaveGuildEmblemOpcode | — |
| SetEmblem | method | Database/PExecute#2 | WorldSession.GuildHandler/HandleSaveGuildEmblemOpcode | guild |
| GetAccountsNumber | method | — | ChatHandler.LookupCommands/HandleLookupGuildCommand, WorldSession.GuildHandler/HandleGuildInfoOpcode | — |
| DisplayGuildEventLog | method | — | — | — |
| LoadGuildEventLogFromDB | method | Database/PQuery, Field/GetUInt32, Field/GetUInt64, Field/GetUInt8, QueryResult/Fetch, QueryResult/NextRow | GuildMgr/LoadGuilds | guild_eventlog |
| LogGuildEvent | method | Database/PExecute#2, ObjectGuid/GetCounter, ObjectGuid/ObjectGuid, World/getConfig#4 | WorldSession.GuildHandler/HandleGuildAcceptOpcode, WorldSession.GuildHandler/HandleGuildDemoteOpcode, WorldSession.GuildHandler/HandleGuildInviteOpcode, WorldSession.GuildHandler/HandleGuildLeaveOpcode, WorldSession.GuildHandler/HandleGuildPromoteOpcode, WorldSession.GuildHandler/HandleGuildRemoveOpcode | — |
| GetGuildInviter | method | ObjectGuid/ObjectGuid, ObjectGuid/ObjectGuid#5 | WorldSession.GuildHandler/HandleGuildDeclineOpcode | — |
| BroadcastEvent | method | ByteBuffer/operator<<#3, ByteBuffer/operator<<#7, ObjectGuid/IsEmpty, ObjectGuid/operator<<, WorldPacket/WorldPacket#4 | WorldSession.CharacterHandler/HandlePlayerLogin, WorldSession.GuildHandler/HandleGuildAcceptOpcode, WorldSession.GuildHandler/HandleGuildLeaveOpcode, WorldSession.Main/LogoutPlayer | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `guild`: guild_id int(6) unsigned PK, name varchar(255), leader_guid int(6) unsigned, emblem_style int(5), emblem_color int(5), border_style int(5), border_color int(5), background_color int(5), info text, motd varchar(255), create_date bigint(20)
- `guild_eventlog`: guild_id int(11) PK, log_guid int(11) PK, event_type tinyint(1), player_guid1 int(11), player_guid2 int(11), new_rank tinyint(2), timestamp bigint(20)
- `guild_member`: guild_id int(6) unsigned, guid int(11) unsigned PK, rank tinyint(2) unsigned, player_note varchar(255), officer_note varchar(255)
- `guild_rank`: guild_id int(6) unsigned PK, id int(11) unsigned PK, name varchar(255), rights int(3) unsigned

*`?` = nullable, `PK` = primary key column.*

