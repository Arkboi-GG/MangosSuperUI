<!-- provenance: verbose -->
# Guild

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Guild

The `Guild` class represents a single player guild entity within the server. It serves as the central data structure for guild identity (ID, name, leader, creation date), visual appearance (tabard/emblem styles and colors), administrative hierarchy (ranks and permissions), and membership roster. It also maintains the Message of the Day (MOTD), general information text, and an in-memory event log for auditing significant actions such as promotions, demotions, and membership changes.

This unit acts primarily as a data container and coordinator. It does not perform direct network I/O or database queries itself; instead, it exposes accessor methods and state management functions consumed by `WorldSession.GuildHandler` for opcode processing, `ChatHandler` for administrative commands, `GuildMgr` for global registry management, and `game_Guild_Guild` for structural logic. It relies on `ObjectAccessor` to resolve online players from GUIDs during broadcast operations.

## Member-by-Member Behavior

### Identity and Metadata Accessors
These methods provide read-only access to the core identifying attributes of the guild, returning private member variables.

*   **GetId**: Returns the unique numeric identifier (`m_Id`). Used by `ChatHandler.LookupCommands`, `game_Guild_Guild`, and `GuildMgr` for global identification.
*   **GetName**: Returns the guild's name (`m_Name`). Used by `AsyncCommandHandlers`, `ChatHandler`, `GuildMgr`, and `WorldSession.GuildHandler` for display and validation.
*   **GetLeaderGuid**: Returns the `ObjectGuid` of the current guild master (`m_LeaderGuid`). Critical for permission checks in `WorldSession.GuildHandler` operations (accepting invites, changing ranks, disbanding, transferring leadership) and `GuildMgr` lookups.
*   **GetMOTD**: Returns the Message of the Day string (`MOTD`). Displayed to players upon login via `WorldSession.CharacterHandler` and during lookup commands.
*   **GetGINFO**: Returns the general information text (`GINFO`). Used in lookup commands.
*   **GetCreatedYear**, **GetCreatedMonth**, **GetCreatedDay**: Return the respective components of the guild's creation date. Used for display in lookup commands and guild info packets.

### Emblem and Tabard Accessors
These methods retrieve the visual style settings for the guild tabard. They are simple getters for the corresponding `m_*` member variables.

*   **GetEmblemStyle**: Returns the emblem icon style.
*   **GetEmblemColor**: Returns the emblem color.
*   **GetBorderStyle**: Returns the border style.
*   **GetBorderColor**: Returns the border color.
*   **GetBackgroundColor**: Returns the background color.

*Note: These methods are not listed as being called by other units in the provided MAP, but they form the standard interface for retrieving tabard data, likely used internally by broadcast methods or other parts of the class not detailed in the cross-unit calls.*

### Membership and Rank Structure
These methods manage the guild's roster and rank hierarchy.

*   **GetLowestRank**: Returns the highest numerical rank ID, which corresponds to the lowest privilege level (e.g., Initiate). Calculated as `m_Ranks.size() - 1`. Used by `ChatHandler`, `game_Guild_Guild`, and `WorldSession.GuildHandler` to determine default ranks for new members or validate demotion limits.
*   **GetMemberSize**: Returns the total number of members (`members.size()`). Used for display in guild info and leave commands.
*   **GetRanksSize**: Returns the total number of defined ranks (`m_Ranks.size()`). Used when adding new ranks to ensure limits are respected.
*   **GetRank**: Retrieves the rank ID for a specific player GUID. Delegates to `GetMemberSlot(ObjectGuid)` to find the member and returns their `RankId`. Returns `-1` if not found. Used by `game_Guild_Guild` to check guild structure integrity.
*   **GetMemberSlot** (ObjectGuid): Looks up a `MemberSlot` by `ObjectGuid`. Searches the `members` unordered map using the GUID's counter. Used by `ChatHandler`, `game_Guild_Guild`, `Player.Main`, and `WorldSession.GuildHandler` to access detailed member data.
*   **GetMemberSlot#2** (std::string): Looks up a `MemberSlot` by player name. Iterates through the `members` map comparing names. Less efficient than GUID lookup; used by `WorldSession.GuildHandler` for operations where name is the primary input (e.g., setting notes, promoting/demoting by name).
*   **HasRankRight**: Checks if a specific rank ID possesses a specific permission bit. Retrieves rights via `GetRankRights` (defined in another partial) and performs a bitwise AND. Returns `true` if the result is not equal to `GR_RIGHT_EMPTY`. Heavily used by `game_Guild_Guild` and `WorldSession.GuildHandler` to enforce permissions for chatting, inviting, removing, promoting, demoting, and editing notes.
*   **UpdateAccountsNumber**: Resets the cached account count (`m_accountsNumber`) to `0`. Acts as a marker to trigger lazy recalculation the next time the account count is requested. Called whenever membership changes (`AddMember`, `DelMember`) or when loading from DB.

### Event Broadcasting and Logging
These methods handle notifying online members of guild events and maintaining an audit trail.

*   **BroadcastEvent**: Sends a guild event packet to all online members. Takes an event type (`GuildEvents`), an optional GUID, and optional strings. Called by `game_Guild_Guild` and `WorldSession.GuildHandler` for events like promotions, demotions, MOTD changes, leadership transfers, and disbanding.
*   **GetGuildEventLog**: Returns a constant reference to the internal list of `GuildEventLogEntry` objects (`m_GuildEventLog`). Used by `ChatHandler` to display the event history.

## Cross-Unit Boundaries

### Collaboration with `WorldSession.GuildHandler`
`WorldSession.GuildHandler` is the primary consumer of `Guild` methods for processing client requests.
*   **Permission Checks**: Calls `GetLeaderGuid`, `HasRankRight`, and `GetLowestRank` to verify authority for inviting, removing, promoting, or demoting.
*   **Data Retrieval**: Uses `GetName`, `GetMOTD`, `GetCreatedYear/Month/Day`, `GetMemberSize`, and `GetRanksSize` to construct response packets.
*   **Member Management**: Uses `GetMemberSlot` (both overloads) to locate members for note updates, rank changes, and removals.
*   **Event Notification**: Calls `BroadcastEvent` after successful operations.

### Collaboration with `game_Guild_Guild`
`game_Guild_Guild` appears to be a helper class or namespace for guild-related game logic.
*   **Structural Integrity**: Calls `GetRank` to verify internal consistency.
*   **Membership Operations**: Calls `AddMember` and `DelMember` (methods defined in another partial) and subsequently calls `UpdateAccountsNumber` to invalidate the cache. Also calls `BroadcastEvent` for major events like disbanding or member removal.
*   **Data Loading**: Calls `LoadMembersFromDB` (another partial) and `GetId` during initialization.

### Collaboration with `ChatHandler`
`ChatHandler` uses `Guild` methods for administrative commands.
*   **Lookup**: `HandleLookupGuildCommand` calls `GetId`, `GetName`, `GetLeaderGuid`, `GetMOTD`, `GetGINFO`, `GetCreatedYear/Month/Day`, and `GetMemberSize` to display guild details.
*   **Logging**: `HandleGuildShowLogCommand` calls `GetGuildEventLog` to display the event history.
*   **Management**: `HandleGuildInviteCommand` and `HandleGuildRankCommand` use `GetLowestRank` and `GetMemberSlot` to assist in manual management tasks.

### Collaboration with `GuildMgr`
`GuildMgr` manages the global collection of guilds.
*   **Identification**: Calls `GetId` and `GetName` to index and retrieve guilds.
*   **Leader Lookup**: `GetGuildByLeader` calls `GetLeaderGuid` to find a guild by its leader's GUID.

### Collaboration with `Player.Main`
*   **Communication Permissions**: `IsAllowedWhisperFrom` calls `GetMemberSlot` to check if a whisperer is in the same guild, potentially affecting whisper permissions or notifications.

### Collaboration with `AsyncCommandHandlers`
*   **Response Handling**: `HandleResponse` calls `GetName` to display results from asynchronous queries.

## Data Model

The `Guild` class interacts with several database tables, although the specific SQL queries are located in other partials (e.g., `LoadGuildFromDB`, `LoadMembersFromDB`). Based on the member variables and standard WoW emulator schemas, the relevant tables are:

*   **guild**: Stores basic guild information (`guildid`, `name`, `leaderguid`, `createdate`, `motd`, `info`, `emblemstyle`, `emblemcolor`, `borderstyle`, `bordercolor`, `bgcolor`).
*   **guild_member**: Stores membership details (`guid`, `rank`, `pnote`, `offnote`).
*   **guild_rank**: Stores rank definitions (`guildid`, `rid`, `rname`, `rights`).
*   **guild_eventlog**: Stores historical events (`guildid`, `eventtime`, `eventtype`, `player1`, `player2`, `newrank`).

The `Guild` class itself does not execute SQL; it provides the structures (`MemberSlot`, `RankInfo`, `GuildEventLogEntry`) to hold the data loaded by other parts of the class.

## Notable Implementation Details

*   **Lazy Account Count Calculation**: The `m_accountsNumber` variable is initialized to `0` and reset to `0` by `UpdateAccountsNumber`. This indicates that the actual count is calculated on-demand in `GetAccountsNumber` (defined in another partial) only when needed, optimizing performance by avoiding unnecessary iterations over the member list after every change.
*   **Rank Indexing**: The comment `//lowest rank is the count of ranks - 1` in `GetLowestRank` clarifies that rank IDs are zero-indexed, with `0` being the highest privilege (Guild Master) and higher numbers representing lower privileges. This aligns with the `GuildDefaultRanks` enum where `GR_GUILDMASTER` is `0` and `GR_INITIATE` is `4`.
*   **Member Lookup Efficiency**: `GetMemberSlot(ObjectGuid)` uses an `unordered_map` for O(1) lookup, while `GetMemberSlot(std::string)` uses a linear scan. Code should prefer the GUID-based lookup whenever possible for performance.
*   **Event Log Ordering**: The comment `/** These are actually ordered lists. The first element is the oldest entry.*/` for `m_GuildEventLog` indicates that the list maintains chronological order, with new entries appended to the end.
*   **Broadcast Worker Template**: The `BroadcastWorker` template method allows executing a functor on all online guild members. It uses `ObjectAccessor::FindPlayer` to resolve online players, ensuring that only currently logged-in members receive broadcasts. This is a flexible mechanism for custom broadcast logic.
*   **Permission Bitmask Logic**: `HasRankRight` uses a specific bitmask check `!= GR_RIGHT_EMPTY`. This implies that `GR_RIGHT_EMPTY` (0x00000040) is a base value present in all valid rights masks, and the check ensures that the specific right bit is set *in addition* to this base. This is a subtle detail that must be respected when defining new rights or checking permissions.

## Member Reference

**GetId**: Returns the guild's unique numeric ID (`m_Id`).

**GetLeaderGuid**: Returns the `ObjectGuid` of the guild leader (`m_LeaderGuid`).

**GetName**: Returns the guild's name (`m_Name`).

**GetMOTD**: Returns the Message of the Day string (`MOTD`).

**GetGINFO**: Returns the general information text (`GINFO`).

**GetCreatedYear**: Returns the year the guild was created (`m_CreatedYear`).

**GetCreatedMonth**: Returns the month the guild was created (`m_CreatedMonth`).

**GetCreatedDay**: Returns the day the guild was created (`m_CreatedDay`).

**GetEmblemStyle**: Returns the emblem style (`m_EmblemStyle`).

**GetEmblemColor**: Returns the emblem color (`m_EmblemColor`).

**GetBorderStyle**: Returns the border style (`m_BorderStyle`).

**GetBorderColor**: Returns the border color (`m_BorderColor`).

**GetBackgroundColor**: Returns the background color (`m_BackgroundColor`).

**GetLowestRank**: Returns the highest rank ID (lowest privilege), calculated as `m_Ranks.size() - 1`.

**GetMemberSize**: Returns the number of members in the guild (`members.size()`).

**BroadcastEvent**: Sends a guild event packet to all online members.

**GetRanksSize**: Returns the number of defined ranks (`m_Ranks.size()`).

**HasRankRight**: Checks if a rank has a specific permission bit, returning `true` if the bitwise AND of the rank's rights and the requested right is not equal to `GR_RIGHT_EMPTY`.

**GetRank**: Returns the rank ID for a player GUID, or `-1` if not found.

**GetMemberSlot**: Returns a pointer to the `MemberSlot` for a player GUID, or `nullptr` if not found.

**GetMemberSlot#2**: Returns a pointer to the `MemberSlot` for a player name, or `nullptr` if not found.

**GetGuildEventLog**: Returns a constant reference to the list of guild event log entries.

**UpdateAccountsNumber**: Resets the cached account count to `0` to trigger lazy recalculation.

---

<!-- machine-true, projected from graph.json -->

## Map — Guild

*Source:* Guild.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GetId | method | — | ChatHandler.LookupCommands/HandleLookupGuildCommand, game_Guild_Guild/AddMember, game_Guild_Guild/LoadMembersFromDB, GuildMgr/AddGuild | — |
| GetLeaderGuid | method | — | ChatHandler.LookupCommands/HandleLookupGuildCommand, GuildMgr/GetGuildByLeader, WorldSession.GuildHandler/HandleGuildAcceptOpcode, WorldSession.GuildHandler/HandleGuildAddRankOpcode, WorldSession.GuildHandler/HandleGuildDelRankOpcode, WorldSession.GuildHandler/HandleGuildDisbandOpcode, WorldSession.GuildHandler/HandleGuildLeaderOpcode, WorldSession.GuildHandler/HandleGuildLeaveOpcode, WorldSession.GuildHandler/HandleGuildRankOpcode, WorldSession.GuildHandler/HandleSaveGuildEmblemOpcode | — |
| GetName | method | — | AsyncCommandHandlers/HandleResponse, ChatHandler.LookupCommands/HandleLookupGuildCommand, GuildMgr/GetGuildByName, GuildMgr/GetGuildNameById, WorldSession.GuildHandler/HandleGuildInfoOpcode, WorldSession.GuildHandler/HandleGuildInviteOpcode, WorldSession.GuildHandler/HandleGuildLeaveOpcode | — |
| GetMOTD | method | — | ChatHandler.LookupCommands/HandleLookupGuildCommand, WorldSession.CharacterHandler/HandlePlayerLogin | — |
| GetGINFO | method | — | ChatHandler.LookupCommands/HandleLookupGuildCommand | — |
| GetCreatedYear | method | — | ChatHandler.LookupCommands/HandleLookupGuildCommand, WorldSession.GuildHandler/HandleGuildInfoOpcode | — |
| GetCreatedMonth | method | — | ChatHandler.LookupCommands/HandleLookupGuildCommand, WorldSession.GuildHandler/HandleGuildInfoOpcode | — |
| GetCreatedDay | method | — | ChatHandler.LookupCommands/HandleLookupGuildCommand, WorldSession.GuildHandler/HandleGuildInfoOpcode | — |
| GetEmblemStyle | method | — | — | — |
| GetEmblemColor | method | — | — | — |
| GetBorderStyle | method | — | — | — |
| GetBorderColor | method | — | — | — |
| GetBackgroundColor | method | — | — | — |
| GetLowestRank | method | — | ChatHandler.MiscCommands/HandleGuildInviteCommand, ChatHandler.MiscCommands/HandleGuildRankCommand, game_Guild_Guild/Create, game_Guild_Guild/DelRank, game_Guild_Guild/LoadMembersFromDB, WorldSession.GuildHandler/HandleGuildAcceptOpcode, WorldSession.GuildHandler/HandleGuildDemoteOpcode | — |
| GetMemberSize | method | — | ChatHandler.LookupCommands/HandleLookupGuildCommand, WorldSession.GuildHandler/HandleGuildInfoOpcode, WorldSession.GuildHandler/HandleGuildLeaveOpcode | — |
| BroadcastEvent | method | — | game_Guild_Guild/DelMember, game_Guild_Guild/Disband, WorldSession.GuildHandler/HandleGuildDemoteOpcode, WorldSession.GuildHandler/HandleGuildLeaderOpcode, WorldSession.GuildHandler/HandleGuildMOTDOpcode, WorldSession.GuildHandler/HandleGuildPromoteOpcode, WorldSession.GuildHandler/HandleGuildRemoveOpcode | — |
| GetRanksSize | method | — | WorldSession.GuildHandler/HandleGuildAddRankOpcode | — |
| HasRankRight | method | — | game_Guild_Guild/BroadcastToGuild, game_Guild_Guild/BroadcastToOfficers, game_Guild_Guild/Roster, WorldSession.GuildHandler/HandleGuildChangeInfoTextOpcode, WorldSession.GuildHandler/HandleGuildDemoteOpcode, WorldSession.GuildHandler/HandleGuildInviteOpcode, WorldSession.GuildHandler/HandleGuildMOTDOpcode, WorldSession.GuildHandler/HandleGuildPromoteOpcode, WorldSession.GuildHandler/HandleGuildRemoveOpcode, WorldSession.GuildHandler/HandleGuildSetOfficerNoteOpcode, WorldSession.GuildHandler/HandleGuildSetPublicNoteOpcode | — |
| GetRank | method | — | game_Guild_Guild/CheckGuildStructure | — |
| GetMemberSlot | method | — | ChatHandler.MiscCommands/HandleGuildRankCommand, game_Guild_Guild/SetLeader, Player.Main/IsAllowedWhisperFrom, WorldSession.GuildHandler/HandleGuildLeaderOpcode, WorldSession.Main/LogoutPlayer | — |
| GetMemberSlot#2 | method | — | WorldSession.GuildHandler/HandleGuildDemoteOpcode, WorldSession.GuildHandler/HandleGuildLeaderOpcode, WorldSession.GuildHandler/HandleGuildPromoteOpcode, WorldSession.GuildHandler/HandleGuildRemoveOpcode, WorldSession.GuildHandler/HandleGuildSetOfficerNoteOpcode, WorldSession.GuildHandler/HandleGuildSetPublicNoteOpcode | — |
| GetGuildEventLog | method | — | ChatHandler.MiscCommands/HandleGuildShowLogCommand | — |
| UpdateAccountsNumber | method | — | game_Guild_Guild/AddMember, game_Guild_Guild/DelMember, game_Guild_Guild/LoadMembersFromDB | — |
