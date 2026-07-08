<!-- provenance: boundary-bleed -->
# WorldSession.GroupHandler

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# WorldSession.GroupHandler

## Purpose & Responsibilities

`WorldSession.GroupHandler` implements the server-side logic for all group and raid management operations initiated by a client. It resides within the `WorldSession` class, acting as the primary interface between a specific player's network connection and the game's group state machine (`Group`).

Its responsibilities include:
1.  **Group Lifecycle Management:** Handling invitations, acceptance, decline, creation, disbanding, and conversion between party and raid modes.
2.  **Membership Administration:** Managing leader transfers, assistant assignments, subgroup movements, and uninviting/removing members.
3.  **Group State Synchronization:** Broadcasting updates to group members regarding loot methods, target icons, ready checks, minimap pings, and random rolls.
4.  **Statistical Reporting:** Constructing and sending detailed status packets (health, power, auras, pet info) for group members to keep the UI synchronized.

This unit does not store persistent group data itself; it delegates persistence and complex state validation to the `Group` class and `ObjectMgr`. It focuses on validating client requests, enforcing permissions (leader/assistant checks), and translating approved actions into network messages or state changes.

## Member-by-Member Behavior

### Group Invitation and Acceptance

**`HandleGroupInviteOpcode`** processes a request to invite another player to the sender's group. It performs extensive validation before proceeding:
1.  **Name Normalization:** Uses `ObjectMgr::normalizePlayerName` to ensure the target name is valid.
2.  **Target Existence:** Verifies the target player exists via `ObjectMgr::GetPlayer`.
3.  **Faction Restrictions:** Unless the sender is a Game Master or cross-faction grouping is enabled (`CONFIG_BOOL_ALLOW_TWO_SIDE_INTERACTION_GROUP`), it rejects invites across faction lines.
4.  **Ignore List:** Checks `SocialMgr::HasIgnore` to prevent inviting players who have ignored the sender.
5.  **Existing Group Status:** Determines if the sender or target is already in a group. Crucially, it distinguishes between temporary BattleGround groups (`isBGGroup`) and original groups (`GetOriginalGroup`). If a player is in a BG group, it looks at their original group for invitation purposes.
6.  **Permissions & Capacity:** If the sender is in a group, it verifies they are the Leader or Assistant and that the group is not full.
7.  **Group Creation/Update:**
    *   If no group exists, it creates a new `Group` object, adds the sender as the leader invitee, and adds the target as an invitee. If either addition fails, the temporary group is deleted.
    *   If a group exists, it simply adds the target to the invite list.
8.  **Notification:** Sends `SMSG_GROUP_INVITE` to the target and a success result to the sender via `SendPartyResult`.

**`HandleGroupAcceptOpcode`** handles a player accepting a group invitation.
1.  Retrieves the pending invite from `Player::GetGroupInvite`.
2.  Prevents a player from accepting an invite to their own group (logging an error if attempted).
3.  Removes the player from the invite list immediately.
4.  Checks if the group is full.
5.  **Group Finalization:** If the group was not yet "created" (meaning it was just formed by invites), it finalizes the group by calling `Group::Create` and registering it with `ObjectMgr::AddGroup`. This ensures groups are only persisted/saved once a second member joins.
6.  Adds the player as a formal member via `Group::AddMember` and broadcasts the update.

**`HandleGroupDeclineOpcode`** handles a player declining an invitation.
1.  Retrieves the pending invite.
2.  Calls `Player::UninviteFromGroup` to clean up the invite state.
3.  If the group leader is online, sends `SMSG_GROUP_DECLINE` to notify them.

### Membership Removal and Leadership

**`HandleGroupUninviteGuidOpcode`** and **`HandleGroupUninviteOpcode`** handle removing a player from a group or canceling an invite. They differ only in how the target is identified (by GUID vs. by Name).
1.  **Self-Uninvite Protection:** Both explicitly prevent a player from uninviting themselves, logging an error if attempted.
2.  **Permission Check:** Calls `Player::CanUninviteFromGroup` to verify the requester has authority (Leader/Assistant).
3.  **Target Resolution:**
    *   If the target is a current member, it calls `Player::RemoveFromGroup`.
    *   If the target is merely invited, it calls `Player::UninviteFromGroup`.
4.  Sends appropriate error results if the target is not found or permissions fail.

**`HandleGroupSetLeaderOpcode`** transfers leadership.
1.  Resolves the target player (by GUID or Name depending on client build).
2.  Validates that the requester is the current leader, the target is in the same group, and the target is not the requester themselves.
3.  Calls `Group::ChangeLeader`.

**`HandleGroupDisbandOpcode`** dissolves the entire group.
1.  Prevents disbanding if the player is in a BattleGround.
2.  Broadcasts the group update (which triggers removal for all members) and removes the requester from the group structure.

**`HandleGroupAssistantLeaderOpcode`** assigns or removes assistant status.
1.  Resolves the target player.
2.  Validates requester is the leader and target is in the group.
3.  Calls `Group::SetAssistant`.

### Raid and Subgroup Management

**`HandleGroupRaidConvertOpcode`** converts a party to a raid.
1.  Prevents conversion if in a BattleGround.
2.  Validates the requester is the leader and the group meets the minimum member count (`GetMembersMinCount`).
3.  Calls `Group::ConvertToRaid`.

**`HandleGroupChangeSubGroupOpcode`** moves a player to a different subgroup within a raid.
1.  Validates subgroup number bounds.
2.  Validates requester is Leader/Assistant.
3.  Checks if the target subgroup has a free slot (`HasFreeSlotSubGroup`).
4.  Resolves the target player by name. If online, uses the `Player` object; if offline, uses the GUID. Calls `Group::ChangeMembersGroup`.

**`HandleGroupSwapSubGroupOpcode`** swaps two players' subgroups.
1.  Validates requester is Leader/Assistant.
2.  Attempts to resolve both players by name.
3.  If both are online, swaps using `Player` objects. If either is offline, resolves by GUID and swaps using GUIDs. Calls `Group::SwapMembersGroup`.

### Loot and Roll Mechanics

**`HandleLootMethodOpcode`** sets the group's loot distribution rules.
1.  Validates the loot method enum range.
2.  Handles BG group fallback to original group.
3.  Validates requester is the leader.
4.  Updates `LootMethod`, `LooterGuid`, and `LootThreshold` on the `Group` object and sends an update.

**`HandleLootRoll`** processes a player's vote on a loot roll (Need/Greed/Pass).
1.  Validates the roll type.
2.  **Playtime Warning:** On newer clients, if the player has exceeded unhealthy playtime limits, it forces the roll to `PASS` and sends a warning via `WorldSession.Main/SendPlayTimeWarning`.
3.  Calls `Group::CountRollVote` to record the vote.

**`HandleRandomRollOpcode`** handles the `/random` command.
1.  Validates min/max bounds (max 10,000).
2.  Generates a random number using `shared_Util::urand`.
3.  Constructs a packet containing the range and result.
4.  Broadcasts to the group if the player is in one; otherwise, sends locally. Note: Pre-1.7.0 clients used a different broadcast mechanism (`SendObjectMessageToSet`).

### Communication and Coordination

**`HandleMinimapPingOpcode`** broadcasts a minimap ping to the group.
1.  Validates the player is in a group.
2.  Constructs a packet with the player's GUID and coordinates.
3.  Broadcasts to the group, excluding the sender.

**`HandleRaidTargetUpdateOpcode`** manages raid target icons (e.g., Star, Skull).
1.  Handles BG group fallback.
2.  If `iconId` is `0xFF`, it requests the current icon list from the group (`SendTargetIconList`).
3.  Otherwise, it validates the requester is Leader/Assistant and sets the icon on the target GUID via `Group::SetTargetIcon`.

**`HandleRaidReadyCheckOpcode`** initiates or responds to a ready check.
1.  **Request (No State):** Validates Leader/Assistant status. Broadcasts the ready check message and triggers `OfflineReadyCheck` for offline members.
2.  **Response (Has State):** Forwards the player's readiness state to the raid leader.

### Status Packets and Info Requests

**`BuildPartyMemberStatsPacket`** constructs a detailed status packet for a group member. This is a heavy serialization function.
1.  Takes a `Player` pointer, a `WorldPacket`, an update mask, and a flag for sending all auras.
2.  Serializes various fields based on the mask bits:
    *   Basic stats: Health, Power, Level, Zone, Position.
    *   Auras: Positive and Negative aura masks and IDs.
    *   Pet Stats: If the player has a pet, it serializes the pet's GUID, name, model ID, health, power, and auras.
3.  Uses conditional logic for client builds (e.g., packed GUIDs vs. standard GUIDs, aura mask sizes).

**`BuildPartyMemberStatsChangedPacket`** prepares a packet for incremental updates.
1.  Retrieves the dirty flags from `Player::GetGroupUpdateFlag`.
2.  Expands flags (e.g., if Power Type changed, also include Current/Max Power).
3.  Calculates the required buffer size.
4.  Initializes the packet and calls `BuildPartyMemberStatsPacket`.

**`HandleRequestPartyMemberStatsOpcode`** responds to a client requesting stats for a specific group member.
1.  Looks up the player by GUID.
2.  If the player is offline or not in the same raid, sends a minimal packet indicating `MEMBER_STATUS_OFFLINE`.
3.  If online and in the same raid, builds a full stats packet using `BuildPartyMemberStatsPacket` with `GROUP_UPDATE_FULL` and sends it.

**`HandleRequestRaidInfoOpcode`** is a simple proxy that calls `Player::SendRaidInfo`, likely triggered when opening the character sheet.

**`SendPartyResult`** is a utility method that constructs and sends `SMSG_PARTY_COMMAND_RESULT` packets to inform the client of the outcome of group operations (invite, leave, etc.).

## Cross-Unit Boundaries

*   **`Group` (game_Group_Group):** The central collaborator. `WorldSession.GroupHandler` acts as the input validator and dispatcher, while `Group` holds the state. Almost every handler calls methods like `AddInvite`, `AddMember`, `ChangeLeader`, `BroadcastGroupUpdate`, etc., on the `Group` object retrieved from the player.
*   **`Player` (Player.Main):** Provides access to the session's player object (`GetPlayer`), social data (`GetSocial`), group state (`GetGroup`, `GetGroupInvite`), and permissions (`IsGameMaster`, `CanUninviteFromGroup`). It also handles the actual removal of players from groups (`RemoveFromGroup`, `UninviteFromGroup`).
*   **`ObjectMgr` (ObjectMgr):** Used to resolve player names to `Player` pointers (`GetPlayer`) or GUIDs (`GetPlayerGuidByName`) and to normalize names (`normalizePlayerName`). It also registers new groups (`AddGroup`).
*   **`WorldPacket` / `ByteBuffer`:** Used for constructing outgoing network messages.
*   **`WorldSession` (WorldSession.Main):** Uses `SendPacket` to transmit data and `GetPlayer` to access the session context.
*   **`SocialMgr` (SocialMgr):** Checked via `Player::GetSocial()->HasIgnore` to enforce ignore lists during invitations.
*   **`Log` (Log.Main):** Used for debugging and error reporting (e.g., self-uninvite attempts, invalid accepts).
*   **`shared_Util` (shared_Util):** Provides `urand` for random roll generation.

## Data Model

This unit does not directly interact with database tables. It operates entirely on in-memory objects (`Group`, `Player`). Persistence of group data is handled by the `Group` class and `ObjectMgr`, which may interact with the `groups` table, but that interaction is abstracted away from this handler.

## Notable Implementation Details

1.  **BattleGround Group Handling:** Several handlers (`HandleGroupInviteOpcode`, `HandleLootMethodOpcode`, `HandleRaidTargetUpdateOpcode`) contain special logic to distinguish between temporary BattleGround groups and the player's "original" group. If a player is in a BG group, operations often fall back to the original group to preserve the player's persistent group state outside of combat scenarios.
2.  **Lazy Group Creation:** In `HandleGroupAcceptOpcode`, a group is not considered "created" (and thus not saved/registered globally) until a second player accepts an invite. The initial invite creates a temporary `Group` object held only in the invitees' memory. This prevents orphaned empty groups in the database.
3.  **Self-Uninvite Prevention:** Both `HandleGroupUninviteGuidOpcode` and `HandleGroupUninviteOpcode` explicitly block players from uninviting themselves, logging an error. This is a deviation from some vanilla behaviors noted in comments ("you can uninvite yourself - is is useful"), suggesting this specific codebase enforces stricter rules or has a bug/deviation here.
4.  **Playtime Enforcement:** `HandleLootRoll` forcibly changes a roll to `PASS` if the player has exceeded unhealthy playtime limits, demonstrating server-side enforcement of parental controls or server policies.
5.  **Client Build Compatibility:** Extensive use of `#if SUPPORTED_CLIENT_BUILD` directives indicates support for multiple WoW client versions (1.6.1 through 1.12.1+). This affects GUID packing, aura mask sizes, and opcode availability (e.g., Raid Target Icons were added in 1.10.2).
6.  **Offline Player Handling:** `HandleGroupChangeSubGroupOpcode` and `HandleGroupSwapSubGroupOpcode` attempt to resolve players by name first. If the player is offline, they fall back to resolving by GUID via `ObjectMgr::GetPlayerGuidByName` and perform the operation on the GUID level, allowing raid leaders to manage offline members.

## Member Reference

**SendPartyResult**: Constructs and sends an `SMSG_PARTY_COMMAND_RESULT` packet to the client, indicating the success or failure of a group operation (invite/leave) with a specific error code and optional member name.

**HandleGroupInviteOpcode**: Validates and processes a group invitation request. Checks name validity, faction restrictions, ignore lists, and group capacity. Creates a new group if necessary or adds to an existing one, then notifies the target player.

**HandleGroupAcceptOpcode**: Processes a player accepting a group invite. Finalizes the group creation if it was pending, adds the player as a member, and broadcasts the update. Prevents self-acceptance.

**HandleGroupDeclineOpcode**: Processes a player declining a group invite. Removes the invite state and notifies the group leader if they are online.

**HandleGroupUninviteGuidOpcode**: Removes a player from a group or cancels their invite based on their GUID. Validates permissions and prevents self-uninvite.

**HandleGroupUninviteOpcode**: Removes a player from a group or cancels their invite based on their name. Validates permissions and prevents self-uninvite.

**HandleGroupSetLeaderOpcode**: Transfers group leadership to another member. Validates that the requester is the current leader and the target is in the group.

**HandleGroupDisbandOpcode**: Dissolves the group. Prevents disbanding during BattleGrounds. Broadcasts the update and removes the requester.

**HandleLootMethodOpcode**: Sets the group's loot distribution method, looter GUID, and item quality threshold. Validates leader permissions and handles BG group fallback.

**HandleLootRoll**: Records a player's vote on a loot roll. Forces a "Pass" vote if the player has exceeded unhealthy playtime limits.

**HandleMinimapPingOpcode**: Broadcasts a minimap ping (coordinates and GUID) to all group members except the sender.

**HandleRandomRollOpcode**: Generates a random number within a specified range and broadcasts the result to the group or locally, depending on group membership and client version.

**HandleRaidTargetUpdateOpcode**: Sets or queries raid target icons. Validates leader/assistant permissions for setting icons. Handles BG group fallback.

**HandleGroupRaidConvertOpcode**: Converts a party to a raid. Validates leader permissions and minimum member count. Prevents conversion during BattleGrounds.

**HandleGroupChangeSubGroupOpcode**: Moves a player to a different subgroup within a raid. Validates leader/assistant permissions and subgroup capacity. Handles both online and offline targets.

**HandleGroupSwapSubGroupOpcode**: Swaps the subgroups of two players. Validates leader/assistant permissions. Handles both online and offline targets by resolving via Player objects or GUIDs.

**HandleGroupAssistantLeaderOpcode**: Assigns or removes assistant status from a player. Validates leader permissions and group membership.

**HandleRaidReadyCheckOpcode**: Initiates a raid ready check (broadcasting the request and checking offline members) or forwards a player's ready response to the raid leader. Validates leader/assistant permissions for initiation.

**BuildPartyMemberStatsPacket**: Serializes detailed status information (health, power, auras, pet stats) for a group member into a `WorldPacket` based on an update mask. Handles client-specific formatting for GUIDs and aura masks.

**BuildPartyMemberStatsChangedPacket**: Prepares a packet for incremental group member updates by retrieving dirty flags, expanding related flags, calculating buffer size, and calling `BuildPartyMemberStatsPacket`.

**HandleRequestPartyMemberStatsOpcode**: Responds to a client request for another player's stats. Sends offline status if the target is unavailable or not in the same raid; otherwise, sends full stats.

**HandleRequestRaidInfoOpcode**: Proxies the request to `Player::SendRaidInfo` to provide raid information to the client.

---

<!-- machine-true, projected from graph.json -->

## Map — WorldSession.GroupHandler

*Source:* GroupHandler.cpp, WorldSession.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| SendPartyResult | method | ByteBuffer/operator<<, ByteBuffer/operator<<#10, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| HandleGroupInviteOpcode | method | ByteBuffer/operator<<#3, game_Group_Group/AddInvite, game_Group_Group/AddLeaderInvite, game_Group_Group/Group, Group/IsAssistant, Group/isBGGroup, Group/IsFull, Group/IsLeader, Object/GetObjectGuid, ObjectMgr/GetPlayer#2, ObjectMgr/normalizePlayerName, Player.Main/GetGroup, Player.Main/GetGroupInvite, Player.Main/GetName, Player.Main/GetOriginalGroup, Player.Main/GetSession, Player.Main/GetSocial, Player.Main/GetTeam, Player.Main/IsGameMaster, SocialMgr/HasIgnore, World/getConfig, WorldPacket/WorldPacket#4, WorldSession.Main/GetPlayer, WorldSession.Main/SendPacket | — | — |
| HandleGroupAcceptOpcode | method | game_Group_Group/AddMember, game_Group_Group/BroadcastGroupUpdate, game_Group_Group/Create, game_Group_Group/RemoveInvite, Group/GetLeaderGuid, Group/GetLeaderName, Group/IsCreated, Group/IsFull, Log.Main/Out, Object/GetGuidStr, Object/GetObjectGuid, ObjectGuid/operator==, ObjectMgr/AddGroup, ObjectMgr/GetPlayer, Player.Main/GetGroupInvite, Player.Main/GetName, WorldSession.Main/GetPlayer | — | — |
| HandleGroupDeclineOpcode | method | ByteBuffer/operator<<#3, Group/GetLeaderGuid, ObjectMgr/GetPlayer, Player.Main/GetGroupInvite, Player.Main/GetName, Player.Main/GetSession, Player.Main/UninviteFromGroup, WorldPacket/WorldPacket#4, WorldSession.Main/GetPlayer, WorldSession.Main/SendPacket | — | — |
| HandleGroupUninviteGuidOpcode | method | game_Group_Group/GetInvited, Group/IsMember, Log.Main/Out, Object/GetGuidStr, Object/GetObjectGuid, ObjectGuid/operator==, Player.Main/CanUninviteFromGroup, Player.Main/GetGroup, Player.Main/RemoveFromGroup#2, Player.Main/UninviteFromGroup, WorldSession.Main/GetPlayer | — | — |
| HandleGroupUninviteOpcode | method | game_Group_Group/GetInvited#2, Group/GetMemberGuid, Log.Main/Out, Object/GetGuidStr, Object/GetObjectGuid, ObjectMgr/normalizePlayerName, Player.Main/CanUninviteFromGroup, Player.Main/GetGroup, Player.Main/GetName, Player.Main/RemoveFromGroup#2, Player.Main/UninviteFromGroup, WorldSession.Main/GetPlayer | — | — |
| HandleGroupSetLeaderOpcode | method | game_Group_Group/ChangeLeader, Group/IsLeader, Object/GetObjectGuid, ObjectMgr/GetPlayer, Player.Main/GetGroup, WorldSession.Main/GetPlayer | — | — |
| HandleGroupDisbandOpcode | method | game_Group_Group/BroadcastGroupUpdate, Player.Main/GetGroup, Player.Main/GetName, Player.Main/InBattleGround, Player.Main/RemoveFromGroup, WorldSession.Main/GetPlayer | — | — |
| HandleLootMethodOpcode | method | game_Group_Group/SendUpdate, Group/isBGGroup, Group/IsLeader, Group/SetLooterGuid, Group/SetLootMethod, Group/SetLootThreshold, Object/GetObjectGuid, Player.Main/GetGroup, Player.Main/GetOriginalGroup, WorldSession.Main/GetPlayer | — | — |
| HandleLootRoll | method | game_Group_Group/CountRollVote#2, Object/HasFlag, Player.Main/GetGroup, WorldSession.Main/GetPlayer, WorldSession.Main/SendPlayTimeWarning | — | — |
| HandleMinimapPingOpcode | method | ByteBuffer/operator<<#9, game_Group_Group/BroadcastPacket, Object/GetObjectGuid, ObjectGuid/operator<<, Player.Main/GetGroup, WorldPacket/WorldPacket#4, WorldSession.Main/GetPlayer | — | — |
| HandleRandomRollOpcode | method | ByteBuffer/operator<<#10, game_Group_Group/BroadcastPacket, Object/GetObjectGuid, ObjectGuid/operator<<, Player.Main/GetGroup, shared_Util/urand, WorldPacket/WorldPacket#4, WorldSession.Main/GetPlayer, WorldSession.Main/SendPacket | — | — |
| HandleRaidTargetUpdateOpcode | method | game_Group_Group/SendTargetIconList, game_Group_Group/SetTargetIcon, Group/IsAssistant, Group/IsLeader, Object/GetObjectGuid, Player.Main/GetGroup, Player.Main/GetOriginalGroup, WorldSession.Main/GetPlayer | — | — |
| HandleGroupRaidConvertOpcode | method | game_Group_Group/ConvertToRaid, Group/GetMembersCount, Group/GetMembersMinCount, Group/IsLeader, Object/GetObjectGuid, Player.Main/GetGroup, Player.Main/InBattleGround, WorldSession.Main/GetPlayer | — | — |
| HandleGroupChangeSubGroupOpcode | method | game_Group_Group/ChangeMembersGroup, game_Group_Group/ChangeMembersGroup#2, Group/HasFreeSlotSubGroup, Group/IsAssistant, Group/IsLeader, Object/GetObjectGuid, ObjectMgr/GetPlayer#2, ObjectMgr/GetPlayerGuidByName, Player.Main/GetGroup, WorldSession.Main/GetPlayer | — | — |
| HandleGroupSwapSubGroupOpcode | method | game_Group_Group/SwapMembersGroup, game_Group_Group/SwapMembersGroup#2, Group/IsAssistant, Group/IsLeader, Object/GetObjectGuid, ObjectGuid/operator!, ObjectMgr/GetPlayer#2, ObjectMgr/GetPlayerGuidByName, Player.Main/GetGroup, WorldSession.Main/GetPlayer | — | — |
| HandleGroupAssistantLeaderOpcode | method | Group/IsLeader, Group/SetAssistant, Object/GetObjectGuid, ObjectMgr/GetPlayer, Player.Main/GetGroup, WorldSession.Main/GetPlayer | — | — |
| HandleRaidReadyCheckOpcode | method | ByteBuffer/operator<<#7, game_Group_Group/BroadcastPacket, game_Group_Group/OfflineReadyCheck, Group/GetLeaderGuid, Group/IsAssistant, Group/IsLeader, Object/GetObjectGuid, ObjectGuid/operator<<, ObjectMgr/GetPlayer, Player.Main/GetGroup, Player.Main/GetSession, WorldPacket/WorldPacket#4, WorldSession.Main/GetPlayer, WorldSession.Main/SendPacket | — | — |
| BuildPartyMemberStatsPacket | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#13, ByteBuffer/operator<<#3, ByteBuffer/operator<<#6, ByteBuffer/operator<<#7, game_Group_Group/GetGroupMemberStatus, Object/GetObjectGuid, Object/GetPackGUID, Object/GetUInt32Value, ObjectGuid/ObjectGuid, ObjectGuid/operator<<, ObjectGuid/operator<<#2, Pet.Main/GetAuraUpdateMask, Pet.Main/GetName, Player.Main/GetAuraUpdateMask, Player.Main/GetCachedZoneId, Unit.Main/GetAuraApplicationMask, Unit.Main/GetDisplayId, Unit.Main/GetHealth, Unit.Main/GetLevel, Unit.Main/GetMaxHealth, Unit.Main/GetMaxPower, Unit.Main/GetNegativeAuraApplicationMask, Unit.Main/GetPet, Unit.Main/GetPower, Unit.Main/GetPowerType, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY | — | — |
| BuildPartyMemberStatsChangedPacket | method | Player.Main/GetGroupUpdateFlag, WorldPacket/Initialize | game_Group_Group/UpdatePlayerOutOfRange | — |
| HandleRequestPartyMemberStatsOpcode | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#7, ObjectGuid/operator<<#2, ObjectGuid/WriteAsPacked, Player.Main/IsInSameRaidWith, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| HandleRequestRaidInfoOpcode | method | Player.Main/SendRaidInfo | — | — |

---

<!-- verify: boundary-bleed | foreign: Update, WorldSession -->
