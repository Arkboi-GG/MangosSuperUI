<!-- provenance: boundary-bleed -->
# WorldSession.GuildHandler

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# WorldSession.GuildHandler

## Purpose & Responsibilities

`WorldSession.GuildHandler` (implemented in `GuildHandler.cpp`) constitutes the network-facing interface for all guild-related gameplay mechanics within the `wowvmangos` server. It acts as the dispatcher and validator for client-to-server guild opcodes. Its primary responsibilities are:

1.  **Input Validation:** Ensuring that incoming guild commands originate from valid players, respect permission hierarchies (ranks/rights), adhere to string length limits (with anticheat enforcement), and comply with faction restrictions.
2.  **State Coordination:** Translating validated client requests into actions on the `Guild` object (managed by `GuildMgr`) and updating the local `Player` state (e.g., setting invited guild IDs).
3.  **Network Communication:** Sending appropriate success/failure responses (`SMSG_GUILD_COMMAND_RESULT`, `SMSG_GUILD_INFO`, etc.) to the initiating player and broadcasting events to other guild members via `Guild::BroadcastEvent`.
4.  **Audit Logging:** Recording significant guild actions (invites, joins, promotions, disbands) into the guild log via `Guild::LogGuildEvent`.

This unit does not persist data directly to the database; it relies entirely on the `game_Guild_Guild` unit (and indirectly `GuildMgr`) for persistence and complex business logic.

## Member-by-Member Behavior

The members are grouped by functional subsystem.

### Guild Creation and Querying

**HandleGuildQueryOpcode**
Retrieves detailed information about a specific guild identified by `packet.guildId`. It attempts to fetch the `Guild` object via `GuildMgr::GetGuildById`. If successful, it delegates to `Guild::Query` (in `game_Guild_Guild`) to send the response. If the guild does not exist, it sends a generic "player not in guild" error using `SendGuildCommandResult`.

**HandleGuildCreateOpcode**
Processes a request to create a new guild. It performs several pre-checks:
1.  Verifies the player is not already in a guild (`Player::GetGuildId`).
2.  Checks for trial account restrictions (`WorldSession::HasTrialRestrictions`).
3.  Validates the desired guild name length. If the UTF-8 length exceeds `GUILD_NAME_MAX_LENGTH`, it triggers an anticheat action (`WorldSession::ProcessAnticheatAction`) for potential cheating/exploitation and aborts.
4.  Instantiates a new `Guild` object. If `Guild::Create` succeeds, it registers the guild with `GuildMgr::AddGuild`. If creation fails, the object is deleted.

### Membership Management (Invite, Accept, Decline, Leave, Remove)

**HandleGuildInviteOpcode**
Handles inviting another player to the sender's guild.
1.  Normalizes and looks up the target player by name.
2.  Validates the sender is in a guild and has the `GR_RIGHT_INVITE` permission.
3.  Checks various constraints:
    *   Target is not on a trial account.
    *   Target is not ignoring the sender (`SocialMgr::HasIgnore`).
    *   Faction compatibility (unless `CONFIG_BOOL_ALLOW_TWO_SIDE_INTERACTION_GUILD` is enabled).
    *   Target is not already in a guild or already invited.
4.  If valid, it sets the target's pending guild ID (`Player::SetGuildIdInvited`), logs the event, and sends an `SMSG_GUILD_INVITE` packet directly to the target player's session.

**HandleGuildAcceptOpcode**
Processes a player accepting a guild invitation.
1.  Retrieves the guild associated with the player's pending invite ID (`Player::GetGuildIdInvited`).
2.  Validates that the player is not already in a guild.
3.  Checks faction compatibility against the guild leader's team.
4.  Adds the player as a member with the lowest rank via `Guild::AddMember`.
5.  Logs the join event and broadcasts a "joined" message to the guild.

**HandleGuildDeclineOpcode**
Processes a player declining a guild invitation.
1.  Clears the player's pending guild ID (`Player::SetGuildIdInvited(0)`).
2.  Attempts to notify the original inviter by looking up the inviter's GUID from the guild object (`Guild::GetGuildInviter`) and sending them an `SMSG_GUILD_DECLINE` packet. Note: This uses `_player` directly rather than `GetPlayer()`, implying internal consistency assumptions.

**HandleGuildLeaveOpcode**
Handles a player voluntarily leaving their guild.
1.  Prevents the guild leader from leaving if there are other members (`ERR_GUILD_LEADER_LEAVE`).
2.  If the leader is the *only* member, it disbans the guild and deletes the object.
3.  Otherwise, it removes the member via `Guild::DelMember`. If this removal results in zero members, it disbans and deletes the guild.
4.  Logs the leave event and broadcasts a "left" message.

**HandleGuildRemoveOpcode**
Handles kicking a member from the guild.
1.  Validates the kicker has `GR_RIGHT_REMOVE` permission.
2.  Ensures the target is in the guild and is not the guild leader.
3.  Enforces hierarchy: a player cannot kick someone with a rank equal to or higher than their own.
4.  Removes the member via `Guild::DelMember`. If this results in zero members, it disbans and deletes the guild.
5.  Logs the uninvite event and broadcasts a "removed" message.

### Rank Management (Promote, Demote, Leader Change)

**HandleGuildPromoteOpcode**
Promotes a guild member to a higher rank (lower numerical ID).
1.  Validates the promoter has `GR_RIGHT_PROMOTE`.
2.  Ensures the target is in the guild and is not the promoter themselves.
3.  Enforces hierarchy: the promoter can only promote to a rank strictly lower than their own (numerically smaller). Specifically, `GetPlayer()->GetRank() + 1 >= slot->RankId` prevents promoting to one's own rank or higher.
4.  Calculates the new rank ID (`slot->RankId - 1`), applies it via `MemberSlot::ChangeRank`, logs the event, and broadcasts the promotion.

**HandleGuildDemoteOpcode**
Demotes a guild member to a lower rank (higher numerical ID).
1.  Validates the demoter has `GR_RIGHT_DEMOTE`.
2.  Ensures the target is in the guild and is not the demoter themselves.
3.  Enforces hierarchy: the demoter cannot demote someone with a rank equal to or higher than their own.
4.  Prevents demotion below the lowest existing rank (`guild->GetLowestRank()`).
5.  Calculates the new rank ID (`slot->RankId + 1`), applies it, logs the event, and broadcasts the demotion.

**HandleGuildLeaderOpcode**
Transfers guild leadership.
1.  Validates the current sender is the guild leader.
2.  Validates the target is in the guild.
3.  Sets the new leader via `Guild::SetLeader`.
4.  Changes the old leader's rank to Officer (`GR_OFFICER`).
5.  Broadcasts the leadership change.

### Information and Notes

**HandleGuildInfoOpcode**
Sends basic guild metadata to the requesting player.
1.  Retrieves the player's current guild.
2.  Constructs an `SMSG_GUILD_INFO` packet containing the guild name, creation date (day/month/year), member count, and account count.

**HandleGuildRosterOpcode**
Requests the full guild roster. Delegates entirely to `Guild::Roster`, which handles constructing and sending the complex roster packet.

**HandleGuildMOTDOpcode**
Sets the Message of the Day (MOTD).
1.  Validates MOTD length against `GUILD_MOTD_MAX_LENGTH`, triggering anticheat if exceeded.
2.  Checks for `GR_RIGHT_SETMOTD` permission.
3.  Updates the MOTD via `Guild::SetMOTD` and broadcasts it.

**HandleGuildSetPublicNoteOpcode**
Sets a public note for a specific member.
1.  Validates the setter has `GR_RIGHT_EPNOTE`.
2.  Validates note length, triggering anticheat if exceeded.
3.  Updates the note via `MemberSlot::SetPNOTE`.
4.  Refreshes the roster for the current session (`Guild::Roster(this)`).

**HandleGuildSetOfficerNoteOpcode**
Sets an officer-only note for a specific member.
1.  Validates the setter has `GR_RIGHT_EOFFNOTE`.
2.  Validates note length, triggering anticheat if exceeded.
3.  Updates the note via `MemberSlot::SetOFFNOTE`.
4.  Refreshes the roster for the current session.

**HandleGuildChangeInfoTextOpcode**
*(Conditional: `CLIENT_BUILD > 1.8.4`)*
Sets the guild's general info text.
1.  Validates info text length, triggering anticheat if exceeded.
2.  Checks for `GR_RIGHT_MODIFY_GUILD_INFO` permission.
3.  Updates the info text via `Guild::SetGINFO`.

### Rank Structure Management

**HandleGuildRankOpcode**
Modifies an existing rank's name and rights.
1.  Restricts access to the guild leader.
2.  Validates rank name length, triggering anticheat if exceeded.
3.  Updates the rank name and rights via `Guild::SetRankName` and `Guild::SetRankRights`.
4.  Special case: If modifying the Guild Master rank (`GR_GUILDMASTER`), it forces rights to `GR_RIGHT_ALL` to prevent accidental loss of leader privileges.
5.  Refreshes the query and roster for the current session.

**HandleGuildAddRankOpcode**
Creates a new rank.
1.  Restricts access to the guild leader.
2.  Validates rank name length, triggering anticheat if exceeded.
3.  Checks if the maximum number of ranks (`GUILD_RANKS_MAX_COUNT`) has been reached.
4.  Creates the rank with default chat listen/speak rights via `Guild::CreateRank`.
5.  Refreshes the query and roster.

**HandleGuildDelRankOpcode**
Deletes the lowest non-leader rank.
1.  Restricts access to the guild leader.
2.  Calls `Guild::DelRank`.
3.  Refreshes the query and roster.

### Disbanding

**HandleGuildDisbandOpcode**
Disbands the entire guild.
1.  Validates the sender is the guild leader.
2.  Calls `Guild::Disband` and deletes the `Guild` object. No explicit error message is sent if successful, implying the client handles the disappearance of the guild UI.

### Emblem Handling

**HandleSaveGuildEmblemOpcode**
Handles the purchase and saving of a guild emblem/tabard design.
1.  Validates interaction with a Tabard Designer NPC (`Player::GetNPCIfCanInteractWith`).
2.  Removes "feign death" state if active.
3.  Validates the player is in a guild and is the leader.
4.  Checks if the player has sufficient funds (10 Gold). If not, sends a failure message.
5.  Deducts the money (`Player::ModifyMoney`).
6.  Saves the emblem style/colors via `Guild::SetEmblem`.
7.  Sends a success message and refreshes the guild query.

**SendSaveGuildEmblem**
Helper method to send the `MSG_SAVE_GUILD_EMBLEM` packet with a specific status code (success/failure reasons).

### Utility

**SendGuildCommandResult**
Generic helper to send `SMSG_GUILD_COMMAND_RESULT` packets. It constructs the packet with a command type, a string parameter (often a player name or empty), and a result code. This is called by many other handlers to report errors or confirmations.

## Cross-Unit Boundaries

This unit acts as a thin controller layer between the network session and the domain logic.

*   **`GuildMgr`**: The central registry for guilds. `WorldSession.GuildHandler` calls `GetGuildById` frequently to retrieve the active `Guild` object for validation and manipulation. It calls `AddGuild` during creation.
*   **`game_Guild_Guild`**: Contains the heavy lifting. `WorldSession.GuildHandler` delegates persistence, complex state changes (adding/removing members, changing ranks), and packet construction for rosters/queries to methods like `Guild::Query`, `Guild::Create`, `Guild::AddMember`, `Guild::DelMember`, `Guild::Disband`, `Guild::LogGuildEvent`, `Guild::BroadcastEvent`, `Guild::SetMOTD`, `Guild::SetEmblem`, etc.
*   **`Player.Main`**: Used to retrieve the current player's context (`GetPlayer`), their current guild ID, invited guild ID, rank, name, and team. It is also used to modify the player's state, such as `SetGuildIdInvited` and `ModifyMoney`.
*   **`ObjectAccessor` / `ObjectMgr`**: Used to resolve player names to `Player` objects (`FindPlayerByName`) and normalize names (`normalizePlayerName`).
*   **`SocialMgr`**: Checked via `HasIgnore` to prevent inviting players who have ignored the inviter.
*   **`WorldSession.Main`**: Uses `GetPlayer`, `HasTrialRestrictions`, `ProcessAnticheatAction`, `SendNotification`, and `SendPacket` to manage the session's own state and communication channels.
*   **`World`**: Accessed via `getConfig` to check server-wide settings like `CONFIG_BOOL_ALLOW_TWO_SIDE_INTERACTION_GUILD`.
*   **`Log.Main`**: Used for debug logging in `HandleGuildInviteOpcode` and `HandleSaveGuildEmblemOpcode`.
*   **`ByteBuffer` / `WorldPacket`**: Used for constructing outgoing network packets.
*   **`shared_Util`**: `utf8length` is used for strict string length validation.

## Data Model

This unit does not interact directly with database tables. All persistence is handled by the `game_Guild_Guild` unit and `GuildMgr`. Therefore, no SQL queries or table references are present in this source file.

## Notable Implementation Details

1.  **Anticheat String Length Validation**: Several handlers (`HandleGuildCreateOpcode`, `HandleGuildMOTDOpcode`, `HandleGuildSetPublicNoteOpcode`, `HandleGuildSetOfficerNoteOpcode`, `HandleGuildRankOpcode`, `HandleGuildAddRankOpcode`, `HandleGuildChangeInfoTextOpcode`) use `utf8length` to validate input strings. If the length exceeds the defined max, they trigger `ProcessAnticheatAction` with `CHEAT_ACTION_LOG | CHEAT_ACTION_REPORT_GMS | CHEAT_ACTION_KICK`. This suggests that exceeding these limits is treated as a potential exploit or client modification, not just a user error.
2.  **Rank Hierarchy Logic**: In `HandleGuildPromoteOpcode` and `HandleGuildDemoteOpcode`, the logic for preventing self-promotion/demotion and enforcing hierarchy relies on comparing `GetPlayer()->GetRank()` with `slot->RankId`. Note that in this system, lower numerical IDs represent higher ranks (Guild Master is 0). Promoting decreases the ID; demoting increases it.
3.  **Guild Leader Constraints**:
    *   `HandleGuildLeaveOpcode`: The leader cannot leave if there are other members. If they are the only member, the guild is disbanded.
    *   `HandleGuildRemoveOpcode`: A leader cannot be kicked.
    *   `HandleGuildDisbandOpcode`: Only the leader can disband.
    *   `HandleGuildLeaderOpcode`: Only the leader can transfer leadership.
4.  **Silent Failures vs. Errors**:
    *   `HandleGuildDeclineOpcode` does not send an error if the inviter is offline or not found; it simply clears the invite.
    *   `HandleSaveGuildEmblemOpcode` sends specific error codes via `SendSaveGuildEmblem` for various failures (no guild, not leader, not enough money).
5.  **Direct Packet Sending**: `HandleGuildInviteOpcode` constructs an `SMSG_GUILD_INVITE` packet and sends it directly to the *target* player's session (`player->GetSession()->SendPacket`). This is a cross-session communication pattern managed within the handler.
6.  **Memory Management**: In `HandleGuildCreateOpcode`, if `Guild::Create` fails, the newly allocated `Guild` object is explicitly `delete`d. In `HandleGuildRemoveOpcode` and `HandleGuildLeaveOpcode`, if `DelMember` results in an empty guild, `Disband` is called and the `Guild` object is `delete`d. This indicates that `Guild` objects are heap-allocated and manually managed when no longer needed.
7.  **Conditional Compilation**: `HandleGuildChangeInfoTextOpcode` is wrapped in `#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1.8.4`, indicating it is only available for newer client versions.
8.  **Use of `_player` vs `GetPlayer()`**: Most methods use `GetPlayer()`. However, `HandleGuildDeclineOpcode` and `HandleGuildDemoteOpcode` (in the broadcast call) use `_player` directly. This is likely an optimization or legacy artifact, assuming `_player` is valid when these methods are called.

## Member Reference

**HandleGuildQueryOpcode**: Retrieves guild info by ID; delegates to `Guild::Query` if found, else sends error.
**HandleGuildCreateOpcode**: Validates player eligibility (not in guild, not trial, name length), creates `Guild` object, and registers it with `GuildMgr`. Triggers anticheat on long names.
**HandleGuildInviteOpcode**: Validates invite permissions, target existence, ignore list, faction, and duplicate invites. Sets target's pending guild ID, logs event, and sends invite packet to target.
**HandleGuildRemoveOpcode**: Validates kick permissions and hierarchy. Removes member; if guild becomes empty, disbans and deletes guild. Logs and broadcasts event.
**HandleGuildAcceptOpcode**: Validates pending invite and faction compatibility. Adds player to guild with lowest rank, logs event, and broadcasts join.
**HandleGuildDeclineOpcode**: Clears player's pending guild ID. Attempts to notify inviter via packet if inviter is online.
**HandleGuildInfoOpcode**: Sends basic guild metadata (name, creation date, counts) to the requesting player.
**HandleGuildRosterOpcode**: Delegates to `Guild::Roster` to send full roster data.
**HandleGuildPromoteOpcode**: Validates promote permissions and hierarchy. Decreases target's rank ID, logs event, and broadcasts promotion.
**HandleGuildDemoteOpcode**: Validates demote permissions and hierarchy. Increases target's rank ID, logs event, and broadcasts demotion.
**HandleGuildLeaveOpcode**: Handles voluntary departure. Prevents leader from leaving if others remain. Disbands guild if leader leaves alone. Logs and broadcasts event.
**HandleGuildDisbandOpcode**: Allows leader to disband guild, deleting the object.
**HandleGuildLeaderOpcode**: Transfers leadership to target member, demotes old leader to Officer, and broadcasts change.
**HandleGuildMOTDOpcode**: Validates MOTD length (anticheat) and permissions. Sets MOTD and broadcasts it.
**HandleGuildSetPublicNoteOpcode**: Validates permissions and note length (anticheat). Sets public note for member and refreshes roster.
**HandleGuildSetOfficerNoteOpcode**: Validates permissions and note length (anticheat). Sets officer note for member and refreshes roster.
**HandleGuildRankOpcode**: Allows leader to modify rank name/rights. Forces Guild Master rights to ALL. Refreshes query/roster.
**HandleGuildAddRankOpcode**: Allows leader to create new rank if under max limit. Validates name length (anticheat). Refreshes query/roster.
**HandleGuildDelRankOpcode**: Allows leader to delete lowest rank. Refreshes query/roster.
**SendGuildCommandResult**: Helper to construct and send `SMSG_GUILD_COMMAND_RESULT` packets.
**HandleGuildChangeInfoTextOpcode**: *(Client > 1.8.4)* Validates info text length (anticheat) and permissions. Sets guild info text.
**HandleSaveGuildEmblemOpcode**: Validates NPC interaction, guild membership, leadership, and funds. Deducts gold, saves emblem, and sends success/failure status.
**SendSaveGuildEmblem**: Helper to send `MSG_SAVE_GUILD_EMBLEM` packet with status code.

---

<!-- machine-true, projected from graph.json -->

## Map — WorldSession.GuildHandler

*Source:* GuildHandler.cpp, WorldSession.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| HandleGuildQueryOpcode | method | game_Guild_Guild/Query, GuildMgr/GetGuildById | — | — |
| HandleGuildCreateOpcode | method | game_Guild_Guild/Create#2, game_Guild_Guild/Guild, GuildMgr/AddGuild, Player.Main/GetGuildId, shared_Util/utf8length, WorldSession.Main/GetPlayer, WorldSession.Main/HasTrialRestrictions, WorldSession.Main/ProcessAnticheatAction, WorldSession.Main/SendNotification#2 | — | — |
| HandleGuildInviteOpcode | method | ByteBuffer/operator<<, ByteBuffer/operator<<#3, game_Guild_Guild/LogGuildEvent, Guild/GetName, Guild/HasRankRight, GuildMgr/GetGuildById, Log.Main/Out, Object/GetObjectGuid, ObjectAccessor/FindPlayerByName, ObjectMgr/normalizePlayerName, Player.Main/GetGuildId, Player.Main/GetGuildIdInvited, Player.Main/GetName, Player.Main/GetRank, Player.Main/GetSession, Player.Main/GetSocial, Player.Main/GetTeam, Player.Main/SetGuildIdInvited, SocialMgr/HasIgnore, World/getConfig, WorldPacket/WorldPacket#4, WorldSession.Main/GetPlayer, WorldSession.Main/HasTrialRestrictions, WorldSession.Main/SendNotification#2, WorldSession.Main/SendPacket | — | — |
| HandleGuildRemoveOpcode | method | game_Guild_Guild/DelMember, game_Guild_Guild/Disband, game_Guild_Guild/LogGuildEvent, Guild/BroadcastEvent, Guild/GetMemberSlot#2, Guild/HasRankRight, GuildMgr/GetGuildById, Object/GetObjectGuid, Object/IsInWorld, ObjectMgr/normalizePlayerName, Player.Main/GetGuildId, Player.Main/GetName, Player.Main/GetRank, WorldSession.Main/GetPlayer | — | — |
| HandleGuildAcceptOpcode | method | game_Guild_Guild/AddMember, game_Guild_Guild/BroadcastEvent, game_Guild_Guild/LogGuildEvent, Guild/GetLeaderGuid, Guild/GetLowestRank, GuildMgr/GetGuildById, Object/GetObjectGuid, ObjectMgr/GetPlayerTeamByGUID, Player.Main/GetGuildId, Player.Main/GetGuildIdInvited, Player.Main/GetName, Player.Main/GetTeam, World/getConfig, WorldSession.Main/GetPlayer | — | — |
| HandleGuildDeclineOpcode | method | ByteBuffer/operator<<#3, game_Guild_Guild/GetGuildInviter, GuildMgr/GetGuildById, Object/GetObjectGuid, ObjectAccessor/FindPlayer, Player.Main/GetGuildId, Player.Main/GetGuildIdInvited, Player.Main/GetName, Player.Main/GetSession, Player.Main/SetGuildIdInvited, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| HandleGuildInfoOpcode | method | ByteBuffer/operator<<, ByteBuffer/operator<<#10, game_Guild_Guild/GetAccountsNumber, Guild/GetCreatedDay, Guild/GetCreatedMonth, Guild/GetCreatedYear, Guild/GetMemberSize, Guild/GetName, GuildMgr/GetGuildById, Player.Main/GetGuildId, WorldPacket/WorldPacket#4, WorldSession.Main/GetPlayer, WorldSession.Main/SendPacket | — | — |
| HandleGuildRosterOpcode | method | game_Guild_Guild/Roster, GuildMgr/GetGuildById, Player.Main/GetGuildId | — | — |
| HandleGuildPromoteOpcode | method | game_Guild_Guild/ChangeRank, game_Guild_Guild/GetRankName, game_Guild_Guild/LogGuildEvent, Guild/BroadcastEvent, Guild/GetMemberSlot#2, Guild/HasRankRight, GuildMgr/GetGuildById, Object/GetObjectGuid, ObjectGuid/operator==, ObjectMgr/normalizePlayerName, Player.Main/GetGuildId, Player.Main/GetName, Player.Main/GetRank, WorldSession.Main/GetPlayer | — | — |
| HandleGuildDemoteOpcode | method | game_Guild_Guild/ChangeRank, game_Guild_Guild/GetRankName, game_Guild_Guild/LogGuildEvent, Guild/BroadcastEvent, Guild/GetLowestRank, Guild/GetMemberSlot#2, Guild/HasRankRight, GuildMgr/GetGuildById, Object/GetObjectGuid, ObjectGuid/operator==, ObjectMgr/normalizePlayerName, Player.Main/GetGuildId, Player.Main/GetName, Player.Main/GetRank, WorldSession.Main/GetPlayer | — | — |
| HandleGuildLeaveOpcode | method | game_Guild_Guild/BroadcastEvent, game_Guild_Guild/DelMember, game_Guild_Guild/Disband, game_Guild_Guild/LogGuildEvent, Guild/GetLeaderGuid, Guild/GetMemberSize, Guild/GetName, GuildMgr/GetGuildById, Object/GetObjectGuid, ObjectGuid/operator==, Player.Main/GetGuildId, Player.Main/GetName | — | — |
| HandleGuildDisbandOpcode | method | game_Guild_Guild/Disband, Guild/GetLeaderGuid, GuildMgr/GetGuildById, Object/GetObjectGuid, ObjectGuid/operator!=, Player.Main/GetGuildId, WorldSession.Main/GetPlayer | — | — |
| HandleGuildLeaderOpcode | method | game_Guild_Guild/ChangeRank, game_Guild_Guild/SetLeader, Guild/BroadcastEvent, Guild/GetLeaderGuid, Guild/GetMemberSlot, Guild/GetMemberSlot#2, GuildMgr/GetGuildById, Object/GetObjectGuid, ObjectGuid/operator!=, ObjectMgr/normalizePlayerName, Player.Main/GetGuildId, Player.Main/GetName, WorldSession.Main/GetPlayer | — | — |
| HandleGuildMOTDOpcode | method | game_Guild_Guild/SetMOTD, Guild/BroadcastEvent, Guild/HasRankRight, GuildMgr/GetGuildById, Player.Main/GetGuildId, Player.Main/GetRank, shared_Util/utf8length, WorldSession.Main/GetPlayer, WorldSession.Main/ProcessAnticheatAction | — | — |
| HandleGuildSetPublicNoteOpcode | method | game_Guild_Guild/Roster, game_Guild_Guild/SetPNOTE, Guild/GetMemberSlot#2, Guild/HasRankRight, GuildMgr/GetGuildById, ObjectMgr/normalizePlayerName, Player.Main/GetGuildId, Player.Main/GetRank, shared_Util/utf8length, WorldSession.Main/GetPlayer, WorldSession.Main/ProcessAnticheatAction | — | — |
| HandleGuildSetOfficerNoteOpcode | method | game_Guild_Guild/Roster, game_Guild_Guild/SetOFFNOTE, Guild/GetMemberSlot#2, Guild/HasRankRight, GuildMgr/GetGuildById, ObjectMgr/normalizePlayerName, Player.Main/GetGuildId, Player.Main/GetRank, shared_Util/utf8length, WorldSession.Main/GetPlayer, WorldSession.Main/ProcessAnticheatAction | — | — |
| HandleGuildRankOpcode | method | game_Guild_Guild/Query, game_Guild_Guild/Roster, game_Guild_Guild/SetRankName, game_Guild_Guild/SetRankRights, Guild/GetLeaderGuid, GuildMgr/GetGuildById, Object/GetObjectGuid, ObjectGuid/operator!=, Player.Main/GetGuildId, shared_Util/utf8length, WorldSession.Main/GetPlayer, WorldSession.Main/ProcessAnticheatAction | — | — |
| HandleGuildAddRankOpcode | method | game_Guild_Guild/CreateRank, game_Guild_Guild/Query, game_Guild_Guild/Roster, Guild/GetLeaderGuid, Guild/GetRanksSize, GuildMgr/GetGuildById, Object/GetObjectGuid, ObjectGuid/operator!=, Player.Main/GetGuildId, shared_Util/utf8length, WorldSession.Main/GetPlayer, WorldSession.Main/ProcessAnticheatAction | — | — |
| HandleGuildDelRankOpcode | method | game_Guild_Guild/DelRank, game_Guild_Guild/Query, game_Guild_Guild/Roster, Guild/GetLeaderGuid, GuildMgr/GetGuildById, Object/GetObjectGuid, ObjectGuid/operator!=, Player.Main/GetGuildId, WorldSession.Main/GetPlayer | — | — |
| SendGuildCommandResult | method | ByteBuffer/operator<<, ByteBuffer/operator<<#10, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | WorldSession.PetitionsHandler/HandleOfferPetitionOpcode, WorldSession.PetitionsHandler/HandlePetitionBuyOpcode, WorldSession.PetitionsHandler/HandlePetitionRenameOpcode, WorldSession.PetitionsHandler/HandlePetitionSignOpcode, WorldSession.PetitionsHandler/HandleTurnInPetitionOpcode | — |
| HandleGuildChangeInfoTextOpcode | method | game_Guild_Guild/SetGINFO, Guild/HasRankRight, GuildMgr/GetGuildById, Player.Main/GetGuildId, Player.Main/GetRank, shared_Util/utf8length, WorldSession.Main/GetPlayer, WorldSession.Main/ProcessAnticheatAction | — | — |
| HandleSaveGuildEmblemOpcode | method | game_Guild_Guild/Query, game_Guild_Guild/SetEmblem, Guild/GetLeaderGuid, GuildMgr/GetGuildById, Log.Main/Out, Object/GetObjectGuid, ObjectGuid/GetString, ObjectGuid/operator!=, Player.Main/GetGuildId, Player.Main/GetMoney, Player.Main/GetNPCIfCanInteractWith, Player.Main/ModifyMoney, Unit.Main/HasUnitState, Unit.Main/RemoveSpellsCausingAura, WorldSession.Main/GetPlayer | — | — |
| SendSaveGuildEmblem | method | ByteBuffer/operator<<#10, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |

---

<!-- verify: boundary-bleed | foreign: WorldSession -->
