# game_Chat_Channel

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Channel

The `Channel` class represents a single chat channel within the game world. It manages the lifecycle of players joining and leaving, enforces permissions (ownership, moderation, muting, banning), handles message broadcasting, and generates the specific network packets required by the client to display channel notifications, errors, and chat messages.

`Channel` instances are created by `ChannelMgr` (in `ChannelMgr.cpp`) and manipulated primarily through opcode handlers in `WorldSession.ChannelHandler` (in `WorldSessionChannelHandler.cpp`). The class distinguishes between **built-in channels** (defined in DBC data, such as General, Trade, LFG) and **custom channels** (created by players). Built-in channels have fixed IDs and behaviors, while custom channels are dynamic, allowing player-appointed owners and moderators.

## Purpose & Responsibilities

1.  **Membership Management**: Tracks which players (`ObjectGuid`) are currently in the channel via `m_players` and which are banned via `m_banned`.
2.  **Permission Enforcement**: Validates actions (kick, ban, mute, set owner) against the requester's security level (GM status) and channel role (Owner/Moderator). It enforces faction restrictions unless configured otherwise.
3.  **Message Broadcasting**: Routes chat messages (`Say`) to all members, respecting ignore lists and mute statuses.
4.  **Packet Generation**: Constructs `WorldPacket` objects for various channel events (join, leave, kick, ban, mode changes) using a family of `Make...` methods. These packets use the `SMSG_CHANNEL_NOTIFY` opcode structure.
5.  **State Persistence (In-Memory)**: Maintains channel flags, passwords, security levels, and announcement/moderation states in memory. Note: `Channel` itself does not persist data to the database; persistence is handled by higher-level managers like `ChannelMgr`.

## Member-by-Member Behavior

### Initialization and Properties

*   **`Channel` (Constructor)**: Initializes the channel with a name. It checks `DBCStores/GetChannelEntryFor` to determine if it is a built-in channel. If so, it sets flags (General, Trade, LFG, etc.) and disables join/leave announcements. If custom, it normalizes the name using `ObjectMgr/normalizePlayerName`. Special handling exists for "World" and "China" channels.
*   **`GetName`, `GetChannelId`, `IsConstant`, `IsAnnounce`, `IsLevelRestricted`, `IsLFG`, `GetPassword`, `SetPassword`, `SetAnnounce`, `GetNumPlayers`, `GetFlags`, `HasFlag`, `SetSecurityLevel`, `GetSecurityLevel`**: Standard accessors and mutators for channel metadata. `IsConstant` returns true if the channel has a non-zero DBC ID (i.e., it is built-in).

### Membership Operations

*   **`Join`**: Handles a player joining the channel.
    *   Checks if the player is already a member (`IsOn`). If so, sends `MakePlayerAlreadyMember` unless it's a constant channel.
    *   Checks if the player is banned (`IsBanned`). If so, sends `MakeBanned`.
    *   Verifies the password if one is set.
    *   Checks security level requirements (`m_securityLevel`).
    *   Prevents guilded players from joining certain channels (specifically if `GetFlags() == 0x38`, which corresponds to `CHANNEL_FLAG_CITY | CHANNEL_FLAG_GENERAL | CHANNEL_FLAG_NOT_LFG`, likely Guild Recruitment).
    *   Updates the player's internal state via `Player.Main/JoinedChannel`.
    *   Sends join announcements (`MakeJoined`) if enabled and the player isn't a silent GM.
    *   Adds the player to `m_players`.
    *   If it's a custom, non-constant channel with no owner, the joining player becomes the owner and moderator.
*   **`Leave`**: Handles a player leaving.
    *   Sends `MakeYouLeft` to the leaver and `MakeLeft` to others if announcements are enabled.
    *   Removes the player from `m_players`.
    *   If the leaver was the owner, assigns ownership to the next player in the map (arbitrary order) or clears it if empty.
    *   Calls `Player.Main/LeftChannel` to update the player's state.
*   **`IsOn`, `IsBanned`**: Private helpers checking membership and ban status.

### Moderation and Administration

*   **`KickOrBan`**: Core logic for kicking or banning a target.
    *   Validates the kicker's authority (Moderator or GM).
    *   Validates the target is online and in the channel.
    *   Prevents non-GMs from kicking the owner unless they are the owner themselves.
    *   If banning, adds the target to `m_banned` and sends `MakePlayerBanned`. Otherwise, sends `MakePlayerKicked`.
    *   Removes the target from `m_players` and updates ownership if the target was the owner.
*   **`Kick`, `Ban`**: Thin wrappers around `KickOrBan`.
*   **`UnBan`**: Removes a player from the ban list. Validates authority similar to `KickOrBan`. Sends `MakePlayerUnbanned`.
*   **`SetMode`**: Handles setting/unsetting Moderator or Mute status for a target.
    *   Validates authority.
    *   Enforces faction restrictions: Non-GMs cannot moderate/mute players from the opposite faction unless `CONFIG_BOOL_ALLOW_TWO_SIDE_INTERACTION_CHANNEL` is true.
    *   Prevents changing the owner's moderator status if the requester isn't the owner.
    *   Calls private `SetModerator` or `SetMute` helpers which update flags and broadcast `MakeModeChange`.
*   **`SetModerator`, `UnsetModerator`, `SetMute`, `UnsetMute`**: Wrappers around `SetMode`.
*   **`SetOwner` (two overloads)**:
    *   `SetOwner(guid, bool exclaim)`: Internal use, often called when ownership transfers automatically (e.g., on leave). Updates `m_ownerGuid` and broadcasts changes.
    *   `SetOwner(guid, char const* targetName)`: External command handler. Validates the requester is the current owner or a GM. Validates the target is in the channel and on the same faction (unless config allows cross-faction). Sets the target as owner and moderator.
*   **`SendWhoOwner`**: Sends the current owner's name to the requesting player.

### Messaging and Communication

*   **`Say`**: Broadcasts a chat message.
    *   Checks if the sender is muted or lacks permission (e.g., Honor Rank requirement for World Defense channel).
    *   Checks moderation status: If `m_moderate` is true, only Moderators/GMs can speak.
    *   Builds the chat packet using `ChatHandler.Chat/BuildChatPacket`.
    *   Handles account-level mutes (`ACCOUNT_FLAG_MUTED_FROM_PUBLIC_CHANNELS`).
    *   Sends the packet to all members except the sender (unless the sender is a moderator, in which case it might be sent to all including sender depending on logic, though typically `SendToAll` excludes the specified `guid`).
*   **`Invite`**: Invites a player to the channel.
    *   Validates the inviter is in the channel.
    *   Checks if the target is already a member or banned.
    *   Enforces faction restrictions.
    *   Sends `MakeInvite` to the target (if not ignoring the inviter) and `MakePlayerInvited` to the inviter.
*   **`List`**: Generates a list of players in the channel.
    *   Iterates through `m_players`.
    *   Applies visibility rules: Players cannot see GMs above a certain security level unless they are GMs themselves. Uses `MasterPlayer.Main/IsVisibleGloballyFor` or `Player.Main/IsVisibleGloballyFor`.
    *   Sends the list to the requesting player.

### Packet Construction Helpers

The class contains numerous `Make...` methods that populate a `WorldPacket` with specific notification types (`ChatNotify` enum) and data. These are used internally by the logic methods above.

*   **`MakeNotifyPacket`**: Base helper that initializes the packet with `SMSG_CHANNEL_NOTIFY`, the notification type, and the channel name.
*   **`MakeJoined`, `MakeLeft`, `MakeYouJoined`, `MakeYouLeft`**: Join/leave notifications.
*   **`MakeWrongPassword`, `MakeNotMember`, `MakeNotModerator`, `MakeNotOwner`, `MakePlayerNotFound`**: Error notifications.
*   **`MakePasswordChanged`, `MakeOwnerChanged`, `MakeModeChange`**: State change notifications.
*   **`MakeAnnouncementsOn/Off`, `MakeModerationOn/Off`**: Toggle notifications.
*   **`MakeMuted`**: Mute notification.
*   **`MakePlayerKicked`, `MakeBanned`, `MakePlayerBanned`, `MakePlayerUnbanned`, `MakePlayerNotBanned`**: Ban/Kick notifications.
*   **`MakePlayerAlreadyMember`**: Duplicate join notification.
*   **`MakeInvite`, `MakeInviteWrongFaction`, `MakeWrongFaction`, `MakeInvalidName`, `MakeNotModerated`, `MakePlayerInvited`, `MakePlayerInviteBanned`, `MakeThrottled`**: Various other notifications.

### Utility and Internal Helpers

*   **`SendToAll`, `SendToOne`**: Network transmission helpers. `SendToAll` iterates `m_players` and sends the packet to each, skipping players who ignore the source `guid`.
*   **`GetPlayer` (two overloads)**: Retrieves a `PlayerPointer` (wrapper for `Player` or `MasterPlayer`) by GUID or Name. It uses `ObjectAccessor/FindMasterPlayer` or `ObjectAccessor/FindPlayerNotInWorld` depending on whether the channel is area-dependent (`m_area_dependant`).
*   **`Voice`, `DeVoice`, `JoinNotify`, `LeaveNotify`**: Currently empty stubs, likely reserved for future voice chat integration or specific server-side notifications.

## Cross-Unit Boundaries

*   **`ChannelMgr` (`ChannelMgr.cpp`)**:
    *   *Called by*: `ChannelMgr/GetJoinChannel` creates `Channel` instances.
    *   *Calls out*: `Channel/IsConstant`, `Channel/GetNumPlayers`, `Channel/GetSecurityLevel` are used by `ChannelMgr` during cleanup and validation. `Channel/MakeNotOnPacket` is used by `ChannelMgr/GetChannel`.
*   **`WorldSession.ChannelHandler` (`WorldSessionChannelHandler.cpp`)**:
    *   *Called by*: All major opcode handlers (`HandleJoinChannelOpcode`, `HandleLeaveChannelOpcode`, `HandleChannelKickOpcode`, etc.) invoke corresponding `Channel` methods.
    *   *Calls out*: None directly from `Channel` to this unit, but `Channel` methods generate packets that are sent via `WorldSession`.
*   **`Player` / `MasterPlayer` (`Player.cpp`, `MasterPlayer.cpp`)**:
    *   *Calls out*: `Channel` calls `Player.Main/JoinedChannel`, `Player.Main/LeftChannel`, `Player.Main/GetSession`, `Player.Main/GetTeam`, `Player.Main/IsVisibleGloballyFor`, etc., to update player state and retrieve information.
    *   *Called by*: `Player.Main/CleanupChannels` calls `Channel/Leave` and `Channel/GetName`. `Player.Main/LeaveLFGChannel` calls `Channel/IsLFG` and `Channel/Leave`.
*   **`ObjectMgr` (`ObjectMgr.cpp`)**:
    *   *Calls out*: `Channel` constructor calls `DBCStores/GetChannelEntryFor` and `ObjectMgr/normalizePlayerName`. `KickOrBan`, `UnBan`, `SetOwner`, `List`, `Invite` call `ObjectMgr/GetPlayer` or `ObjectMgr/GetPlayerNameByGUID`.
*   **`ObjectAccessor` (`ObjectAccessor.cpp`)**:
    *   *Calls out*: `GetPlayer` helpers call `ObjectAccessor/FindMasterPlayer` and `ObjectAccessor/FindPlayerNotInWorld`.
*   **`ChatHandler` (`Chat.cpp`)**:
    *   *Calls out*: `Say` calls `ChatHandler.Chat/BuildChatPacket` to format the chat message.
*   **`World` (`World.cpp`)**:
    *   *Calls out*: Various methods check `World/getConfig` for settings like `CONFIG_BOOL_SILENTLY_GM_JOIN_TO_CHANNEL`, `CONFIG_BOOL_ALLOW_TWO_SIDE_INTERACTION_CHANNEL`, and `CONFIG_UINT32_GM_LEVEL_IN_WHO_LIST`.
*   **`SocialMgr` (`SocialMgr.cpp`)**:
    *   *Calls out*: `Invite` and `SendToAll` call `SocialMgr/HasIgnore` to respect player ignore lists.
*   **`AiBotAI.Bridge` (`AiBotAI.cpp`)**:
    *   *Called by*: `AiBotAI.Bridge/BridgeHandleSayText` calls `Channel/Say` to inject bot messages into channels.

## Data Model

This unit does not interact directly with any database tables. All state (members, bans, flags, passwords) is held in memory within the `Channel` object. Persistence across server restarts is managed by `ChannelMgr` using separate database tables (not detailed in this unit's scope).

## Notable Implementation Details

1.  **Ownership Transfer Logic**: In `Leave` and `KickOrBan`, if the departing/kicked player was the owner, the new owner is selected arbitrarily from the remaining players (`m_players.begin()->second.player`). This can lead to unexpected ownership assignments if the map iteration order is relied upon for fairness.
2.  **Faction Restrictions**: Many moderation and invitation actions enforce faction alignment (`pPlayer->GetTeam() != pTarget->GetTeam()`). This can be bypassed by Game Masters or by enabling `CONFIG_BOOL_ALLOW_TWO_SIDE_INTERACTION_CHANNEL`.
3.  **Silent GM Joins**: The `Join` method checks `CONFIG_BOOL_SILENTLY_GM_JOIN_TO_CHANNEL`. If enabled, GMs joining do not trigger join announcements, reducing spam in public channels.
4.  **Guild Recruitment Restriction**: In `Join`, there is a specific check: `if (pPlayer->GetGuildId() && (GetFlags() == 0x38)) return;`. This prevents guilded players from joining channels with flags `0x38` (City, General, Not-LFG), which typically corresponds to the Guild Recruitment channel. This ensures only non-guilded players can join to find a guild.
5.  **Honor Rank Requirement**: In `Say`, there is a check for `CHANNEL_ID_WORLD_DEFENSE` requiring `honor_rank >= 15`. This restricts speaking in the World Defense channel to high-honor players.
6.  **Packet Size Guessing**: In `List`, the `WorldPacket` size is guessed: `(GetName().size() + 1) + 1 + 4 + m_players.size() * (8 + 1)`. This is an optimization to avoid reallocation, but relies on accurate size calculations for GUIDs (8 bytes) and flags (1 byte).
7.  **Area Dependence**: The `m_area_dependant` flag determines whether `GetPlayer` searches for `MasterPlayer` (global) or `Player` (local/node-specific). This is set based on DBC flags (`CHANNEL_DBC_FLAG_ZONE_DEP`) during construction.

## Member Reference

**Channel**: Constructor initializes channel properties based on name and DBC data. Sets flags for built-in channels or normalizes names for custom channels.
**Join**: Handles player entry, validates password/bans/security, updates player state, sends announcements, and assigns ownership if needed.
**Leave**: Handles player exit, sends announcements, removes player, and transfers ownership if necessary.
**GetName**: Returns the channel name.
**GetChannelId**: Returns the DBC channel ID.
**IsConstant**: Returns true if the channel is built-in (non-zero ID).
**IsAnnounce**: Returns whether join/leave announcements are enabled.
**IsLevelRestricted**: Returns whether the channel has level restrictions.
**IsLFG**: Returns true if the channel is a Looking For Group channel.
**GetPassword**: Returns the channel password.
**SetPassword**: Sets the channel password.
**SetAnnounce**: Enables or disables join/leave announcements.
**GetNumPlayers**: Returns the number of players in the channel.
**GetFlags**: Returns the channel flags.
**HasFlag**: Checks if a specific flag is set.
**SetSecurityLevel**: Sets the minimum security level required to join.
**GetSecurityLevel**: Returns the minimum security level required to join.
**Kick**: Wrapper for `KickOrBan` with `ban=false`.
**Ban**: Wrapper for `KickOrBan` with `ban=true`.
**SetModerator**: Wrapper for `SetMode` to set moderator status.
**UnsetModerator**: Wrapper for `SetMode` to unset moderator status.
**SetMute**: Wrapper for `SetMode` to set mute status.
**UnsetMute**: Wrapper for `SetMode` to unset mute status.
**KickOrBan**: Core logic for kicking or banning a player, validating authority and updating ownership if needed.
**IsOn**: Checks if a player is in the channel.
**IsBanned**: Checks if a player is banned from the channel.
**GetPlayerFlags**: Returns the flags for a specific player.
**SetModerator#2**: Private helper to update moderator flag and broadcast change.
**UnBan**: Removes a player from the ban list.
**SetMute#2**: Private helper to update mute flag and broadcast change.
**Password**: Changes the channel password.
**SetMode**: Sets or unsets moderator/mute status for a target, enforcing faction and authority rules.
**SetOwner**: Two overloads: one for internal ownership transfer, one for external command to change owner.
**SendWhoOwner**: Sends the owner's name to a player.
**List**: Generates and sends a list of visible players in the channel.
**Announce**: Toggles join/leave announcements.
**Moderate**: Toggles moderation mode (only mods/GMs can speak).
**Say**: Broadcasts a chat message, enforcing mute/moderation/honor rules.
**Invite**: Invites a player to the channel, enforcing faction and ignore rules.
**SetOwner#2**: Internal helper to update owner GUID and broadcast change.
**SendToAll**: Sends a packet to all channel members, respecting ignore lists.
**SendToOne**: Sends a packet to a specific player.
**Voice**: Empty stub.
**DeVoice**: Empty stub.
**MakeNotifyPacket**: Initializes a notification packet with type and channel name.
**MakeJoined**: Creates a join notification packet.
**MakeLeft**: Creates a leave notification packet.
**MakeYouJoined**: Creates a "you joined" notification packet.
**MakeYouLeft**: Creates a "you left" notification packet.
**MakeWrongPassword**: Creates a wrong password error packet.
**MakeNotMember**: Creates a "not member" error packet.
**MakeNotOnPacket**: Static helper to create a "not on channel" packet for a specific name.
**MakeNotModerator**: Creates a "not moderator" error packet.
**MakePasswordChanged**: Creates a password change notification packet.
**MakeOwnerChanged**: Creates an owner change notification packet.
**MakePlayerNotFound**: Creates a player not found error packet.
**MakeNotOwner**: Creates a "not owner" error packet.
**MakeChannelOwner**: Creates a packet showing the current owner.
**MakeModeChange**: Creates a mode change notification packet.
**MakeAnnouncementsOn**: Creates an announcements on notification packet.
**MakeAnnouncementsOff**: Creates an announcements off notification packet.
**MakeModerationOn**: Creates a moderation on notification packet.
**MakeModerationOff**: Creates a moderation off notification packet.
**MakeMuted**: Creates a muted notification packet.
**MakePlayerKicked**: Creates a player kicked notification packet.
**MakeBanned**: Creates a banned notification packet.
**MakePlayerBanned**: Creates a player banned notification packet.
**MakePlayerUnbanned**: Creates a player unbanned notification packet.
**MakePlayerNotBanned**: Creates a player not banned error packet.
**MakePlayerAlreadyMember**: Creates a player already member error packet.
**MakeInvite**: Creates an invite notification packet.
**MakeInviteWrongFaction**: Creates an invite wrong faction error packet.
**MakeWrongFaction**: Creates a wrong faction error packet.
**MakeInvalidName**: Creates an invalid name error packet.
**MakeNotModerated**: Creates a not moderated error packet.
**MakePlayerInvited**: Creates a player invited notification packet.
**MakePlayerInviteBanned**: Creates a player invite banned error packet.
**MakeThrottled**: Creates a throttled error packet.
**JoinNotify**: Empty stub.
**LeaveNotify**: Empty stub.
**GetPlayer**: Retrieves a player pointer by GUID, handling area dependence.
**GetPlayer#2**: Retrieves a player pointer by name, handling area dependence.

---

<!-- machine-true, projected from graph.json -->

## Map — game_Chat_Channel

*Source:* Channel.cpp, Channel.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Channel | ctor | DBCStores/GetChannelEntryFor, ObjectMgr/normalizePlayerName | ChannelMgr/GetJoinChannel | — |
| Join | method | AbstractPlayer/GetGuildId#2, AbstractPlayer/GetSession#2, AbstractPlayer/ToPlayer#3, ByteBuffer/clear, ObjectGuid/operator!, Player.Main/JoinedChannel, PlayerInfo/SetModerator, World/getConfig, WorldPacket/WorldPacket, WorldSession.Main/GetSecurity | WorldSession.ChannelHandler/HandleJoinChannelOpcode | — |
| Leave | method | AbstractPlayer/GetSession#2, AbstractPlayer/LeftChannel#2, ByteBuffer/clear, ObjectGuid/ObjectGuid, PlayerInfo/IsOwner, World/getConfig, WorldPacket/WorldPacket, WorldSession.Main/GetSecurity | MasterPlayer.Chat/CleanupChannels, Player.Main/CleanupChannels, Player.Main/LeaveLFGChannel, WorldSession.ChannelHandler/HandleLeaveChannelOpcode | — |
| GetName | method | — | MasterPlayer.Chat/CleanupChannels, Player.Main/CleanupChannels | — |
| GetChannelId | method | — | — | — |
| IsConstant | method | — | ChannelMgr/LeftChannel | — |
| IsAnnounce | method | — | — | — |
| IsLevelRestricted | method | — | WorldSession.ChatHandler/HandleChatMessageOpcode | — |
| IsLFG | method | — | Player.Main/LeaveLFGChannel | — |
| GetPassword | method | — | — | — |
| SetPassword | method | — | — | — |
| SetAnnounce | method | — | ChannelMgr/CreateDefaultChannels | — |
| GetNumPlayers | method | — | ChannelMgr/LeftChannel | — |
| GetFlags | method | — | — | — |
| HasFlag | method | — | WorldSession.ChatHandler/HandleChatMessageOpcode | — |
| SetSecurityLevel | method | — | ChannelMgr/CreateDefaultChannels | — |
| GetSecurityLevel | method | — | ChannelMgr/LeftChannel, WorldSession.ChannelHandler/HandleJoinChannelOpcode | — |
| Kick | method | — | WorldSession.ChannelHandler/HandleChannelKickOpcode | — |
| Ban | method | — | WorldSession.ChannelHandler/HandleChannelBanOpcode | — |
| SetModerator | method | — | WorldSession.ChannelHandler/HandleChannelModeratorOpcode | — |
| UnsetModerator | method | — | WorldSession.ChannelHandler/HandleChannelUnmoderatorOpcode | — |
| SetMute | method | — | WorldSession.ChannelHandler/HandleChannelMuteOpcode | — |
| UnsetMute | method | — | WorldSession.ChannelHandler/HandleChannelUnmuteOpcode | — |
| KickOrBan | method | AbstractPlayer/GetSession#2, Object/GetObjectGuid, ObjectGuid/ObjectGuid, ObjectGuid/operator!=, ObjectGuid/operator==, ObjectMgr/GetPlayer#2, Player.Main/LeftChannel, PlayerInfo/IsModerator, WorldPacket/WorldPacket, WorldSession.Main/GetSecurity | — | — |
| IsOn | method | — | — | — |
| IsBanned | method | — | — | — |
| GetPlayerFlags | method | — | — | — |
| SetModerator#2 | method | — | — | — |
| UnBan | method | AbstractPlayer/GetSession#2, Object/GetObjectGuid, ObjectMgr/GetPlayer#2, PlayerInfo/IsModerator, WorldPacket/WorldPacket, WorldSession.Main/GetSecurity | WorldSession.ChannelHandler/HandleChannelUnbanOpcode | — |
| SetMute#2 | method | — | — | — |
| Password | method | AbstractPlayer/GetSession#2, PlayerInfo/IsModerator, WorldPacket/WorldPacket, WorldSession.Main/GetSecurity | WorldSession.ChannelHandler/HandleChannelPasswordOpcode | — |
| SetMode | method | AbstractPlayer/GetObjectGuid#2, AbstractPlayer/GetSession#2, AbstractPlayer/GetTeam#2, ObjectGuid/operator!=, ObjectGuid/operator==, PlayerInfo/IsModerator, World/getConfig, WorldPacket/WorldPacket, WorldSession.Main/GetSecurity | — | — |
| SetOwner | method | AbstractPlayer/GetSession#2, AbstractPlayer/GetTeam#2, Object/GetObjectGuid, ObjectGuid/operator!=, ObjectMgr/GetPlayer#2, Player.Main/GetSession, Player.Main/GetTeam, PlayerInfo/SetModerator, World/getConfig, WorldPacket/WorldPacket, WorldSession.Main/GetSecurity | WorldSession.ChannelHandler/HandleChannelSetOwnerOpcode | — |
| SendWhoOwner | method | WorldPacket/WorldPacket | WorldSession.ChannelHandler/HandleChannelOwnerOpcode | — |
| List | method | AbstractPlayer/GetObjectGuid#2, AbstractPlayer/ToMasterPlayer#3, AbstractPlayer/ToPlayer#3, ByteBuffer/operator<<, ByteBuffer/operator<<#4, ByteBuffer/operator<<#7, ByteBuffer/wpos, MasterPlayer.Main/GetSession, MasterPlayer.Main/IsVisibleGloballyFor, ObjectAccessor/FindMasterPlayer, ObjectGuid/operator<<, ObjectMgr/GetPlayer, Player.Main/GetSession, Player.Main/IsVisibleGloballyFor, World/getConfig#4, WorldPacket/WorldPacket, WorldPacket/WorldPacket#4, WorldSession.Main/GetSecurity | WorldSession.ChannelHandler/HandleChannelListOpcode | — |
| Announce | method | AbstractPlayer/GetSession#2, PlayerInfo/IsModerator, WorldPacket/WorldPacket, WorldSession.Main/GetSecurity | WorldSession.ChannelHandler/HandleChannelAnnouncementsOpcode | — |
| Moderate | method | AbstractPlayer/GetSession#2, PlayerInfo/IsModerator, WorldPacket/WorldPacket, WorldSession.Main/GetSecurity | WorldSession.ChannelHandler/HandleChannelModerateOpcode | — |
| Say | method | AbstractPlayer/GetChatTag#2, AbstractPlayer/GetSession#2, AbstractPlayer/ToPlayer#3, ChatHandler.Chat/BuildChatPacket, HonorMgr/GetCurrentHonorRank, ObjectGuid/ObjectGuid, Player.Main/GetHonorMgr, PlayerInfo/IsModerator, PlayerInfo/IsMuted, World/getConfig, World/getConfig#4, WorldPacket/WorldPacket, WorldSession.Main/GetAccountFlags, WorldSession.Main/GetAccountMaxLevel, WorldSession.Main/GetSecurity, WorldSession.Main/SendPacket | AiBotAI.Bridge/BridgeHandleSayText, ChannelMgr/AnnounceBothFactionsChannel, WorldSession.ChatHandler/HandleChatMessageOpcode | — |
| Invite | method | AbstractPlayer/GetName#2, AbstractPlayer/GetObjectGuid#2, AbstractPlayer/GetSocial#2, AbstractPlayer/GetTeam#2, ByteBuffer/clear, SocialMgr/HasIgnore, World/getConfig, WorldPacket/WorldPacket | WorldSession.ChannelHandler/HandleChannelInviteOpcode | — |
| SetOwner#2 | method | AbstractPlayer/GetSession#2, PlayerInfo/SetOwner, WorldPacket/WorldPacket, WorldSession.Main/GetSecurity | — | — |
| SendToAll | method | AbstractPlayer/GetSession#2, AbstractPlayer/GetSocial#2, ObjectGuid/ObjectGuid, SocialMgr/HasIgnore, WorldSession.Main/SendPacket | — | — |
| SendToOne | method | AbstractPlayer/GetSession#2, WorldSession.Main/SendPacket | — | — |
| Voice | method | — | — | — |
| DeVoice | method | — | — | — |
| MakeNotifyPacket | method | ByteBuffer/operator<<, ByteBuffer/operator<<#7, WorldPacket/Initialize | — | — |
| MakeJoined | method | ObjectGuid/operator<< | — | — |
| MakeLeft | method | ObjectGuid/operator<< | — | — |
| MakeYouJoined | method | ByteBuffer/operator<<#10 | — | — |
| MakeYouLeft | method | — | — | — |
| MakeWrongPassword | method | — | — | — |
| MakeNotMember | method | — | — | — |
| MakeNotOnPacket | method | ByteBuffer/operator<<, ByteBuffer/operator<<#7, WorldPacket/Initialize | ChannelMgr/GetChannel | — |
| MakeNotModerator | method | — | — | — |
| MakePasswordChanged | method | ObjectGuid/operator<< | — | — |
| MakeOwnerChanged | method | ObjectGuid/operator<< | — | — |
| MakePlayerNotFound | method | ByteBuffer/operator<< | — | — |
| MakeNotOwner | method | — | — | — |
| MakeChannelOwner | method | ByteBuffer/operator<<, ObjectGuid/operator!, ObjectMgr/GetPlayerNameByGUID | — | — |
| MakeModeChange | method | ByteBuffer/operator<<#7, ObjectGuid/operator<< | — | — |
| MakeAnnouncementsOn | method | ObjectGuid/operator<< | — | — |
| MakeAnnouncementsOff | method | ObjectGuid/operator<< | — | — |
| MakeModerationOn | method | ObjectGuid/operator<< | — | — |
| MakeModerationOff | method | ObjectGuid/operator<< | — | — |
| MakeMuted | method | — | — | — |
| MakePlayerKicked | method | ObjectGuid/operator<< | — | — |
| MakeBanned | method | — | — | — |
| MakePlayerBanned | method | ObjectGuid/operator<< | — | — |
| MakePlayerUnbanned | method | ObjectGuid/operator<< | — | — |
| MakePlayerNotBanned | method | ByteBuffer/operator<< | — | — |
| MakePlayerAlreadyMember | method | ObjectGuid/operator<< | — | — |
| MakeInvite | method | ObjectGuid/operator<< | — | — |
| MakeInviteWrongFaction | method | — | — | — |
| MakeWrongFaction | method | — | — | — |
| MakeInvalidName | method | — | — | — |
| MakeNotModerated | method | — | — | — |
| MakePlayerInvited | method | ByteBuffer/operator<< | — | — |
| MakePlayerInviteBanned | method | ByteBuffer/operator<< | — | — |
| MakeThrottled | method | — | — | — |
| JoinNotify | method | — | — | — |
| LeaveNotify | method | — | — | — |
| GetPlayer | method | ObjectAccessor/FindMasterPlayer, ObjectAccessor/FindPlayerNotInWorld | — | — |
| GetPlayer#2 | method | ObjectAccessor/FindMasterPlayer#2, ObjectAccessor/FindPlayerByNameNotInWorld | — | — |
