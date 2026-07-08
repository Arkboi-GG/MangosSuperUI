<!-- provenance: boundary-bleed -->
# WorldSession.ChannelHandler

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# WorldSession.ChannelHandler

## Purpose & Responsibilities

The `WorldSession.ChannelHandler` unit implements the server-side logic for processing client-initiated channel management opcodes in the WoWVMaNGOS emulation environment. It acts as the entry point for all player interactions with the in-game chat channel system, including joining, leaving, moderating, and configuring channels.

Its primary responsibilities are:
1.  **Validation:** Checking basic input constraints (e.g., channel name validity, player level requirements for inviting).
2.  **Routing:** Identifying the correct `ChannelMgr` instance based on the player's faction (Alliance or Horde) and retrieving the specific `Channel` object.
3.  **Delegation:** Forwarding the validated request to the appropriate method on the `Channel` class (`game_Chat_Channel`) to perform the actual state change.
4.  **Cross-Faction Support:** Implementing special logic allowing Game Masters (GMs) to join and interact with channels on the opposing faction, controlled by a world configuration flag.

This unit does not store channel state itself; it relies entirely on the `ChannelMgr` and `Channel` classes for persistence and state management. It does not interact directly with any database tables.

## Member-by-Member Behavior

The members are grouped by their functional role within the channel system.

### Channel Membership Management

These methods handle players entering or exiting channels.

**HandleJoinChannelOpcode**
Processes a request to join a channel. It first validates that the channel name is not empty and starts with an alphabetic character (ASCII check). If invalid, it sends a `CHAT_INVALID_NAME_NOTICE` to the client.
If valid, it retrieves the player's team (faction) and fetches the corresponding `ChannelMgr`. It attempts to get the channel via `ChannelMgr::GetJoinChannel` and calls `Channel::Join`.
Crucially, it includes a secondary block for GMs: if the player's security level is higher than `SEC_PLAYER` and the world config `CONFIG_BOOL_GM_JOIN_OPPOSITE_FACTION_CHANNELS` is enabled, it also attempts to join the same-named channel on the *opposite* faction, but only if that channel has a security level of 0 (indicating it is a general/cross-faction channel).

**HandleLeaveChannelOpcode**
Processes a request to leave a channel. It ignores empty channel names. It retrieves the player's faction-specific `ChannelMgr`, finds the channel, and calls `Channel::Leave`. It then calls `ChannelMgr::LeftChannel` to clean up local session state.
Like `HandleJoinChannelOpcode`, it contains a duplicate block for GMs to leave the opposite faction's channel if the relevant config flag is set.

### Channel Information Retrieval

**HandleChannelListOpcode**
Requests a list of members in a specific channel. It retrieves the channel from the player's faction manager and calls `Channel::List`, passing the player pointer to trigger the response generation.

**HandleChannelOwnerOpcode**
Requests information about who owns a specific channel. It retrieves the channel and calls `Channel::SendWhoOwner`, passing the requester's GUID.

### Channel Configuration & Moderation

These methods modify channel settings or member permissions. They share a common pattern: retrieve the channel, then delegate to a specific `Channel` method. Most require normalizing the target player's name using `ObjectMgr::normalizePlayerName`.

**HandleChannelPasswordOpcode**
Changes the password for a channel. Delegates to `Channel::Password`.

**HandleChannelSetOwnerOpcode**
Transfers ownership of a channel to another player. Validates the target name, then delegates to `Channel::SetOwner`.

**HandleChannelModeratorOpcode**
Adds a moderator to a channel. Validates the target name, then delegates to `Channel::SetModerator`.

**HandleChannelUnmoderatorOpcode**
Removes moderator status from a player. Validates the target name, then delegates to `Channel::UnsetModerator`.

**HandleChannelMuteOpcode**
Mutes a player in a channel. Validates the target name, then delegates to `Channel::SetMute`.

**HandleChannelUnmuteOpcode**
Unmutes a player in a channel. Validates the target name, then delegates to `Channel::UnsetMute`.

**HandleChannelAnnouncementsOpcode**
Toggles whether channel messages are announced in the default chat window. Delegates to `Channel::Announce`.

**HandleChannelModerateOpcode**
Toggles the "moderate" flag, which requires moderator approval for new members to join. Delegates to `Channel::Moderate`.

### Member Management (Invite, Kick, Ban)

These methods manage individual players within a channel.

**HandleChannelInviteOpcode**
Invites a player to a channel. It performs two checks before delegating:
1. Normalizes the target player's name.
2. Checks if the inviting player's level meets the minimum requirement defined by `CONFIG_UINT32_CHANNEL_INVITE_MIN_LEVEL` via `World::getConfig`.
If checks pass, it delegates to `Channel::Invite`.

**HandleChannelKickOpcode**
Kicks a player from a channel. Validates the target name, then delegates to `Channel::Kick`.

**HandleChannelBanOpcode**
Bans a player from a channel. Validates the target name, then delegates to `Channel::Ban`.

**HandleChannelUnbanOpcode**
Removes a ban on a player. Validates the target name, then delegates to `Channel::UnBan`.

## Cross-Unit Boundaries

### Calls Out

*   **`ChannelMgr`**: Every handler retrieves a `ChannelMgr` instance via the global `channelMgr()` function, passing the player's team ID. It then calls `GetChannel` or `GetJoinChannel` to locate the specific channel object. In `HandleLeaveChannelOpcode`, it also calls `LeftChannel` to update the manager's internal tracking.
*   **`game_Chat_Channel` (`Channel`)**: The core logic resides here. Each handler delegates the actual operation (Join, Leave, Mute, Kick, etc.) to the corresponding method on the `Channel` object returned by `ChannelMgr`.
*   **`AbstractPlayer`**: Handlers frequently call `GetTeam()` to determine the player's faction, `GetObjectGuid()` to identify the player in channel operations, and `GetSession()` (in `HandleJoinChannelOpcode`) to check security levels. `HandleChannelInviteOpcode` also calls `GetLevel()`.
*   **`ObjectMgr`**: Several moderation handlers (`SetOwner`, `Moderator`, `Mute`, etc.) call `normalizePlayerName` to ensure the target player name is in the correct format before passing it to the channel logic.
*   **`World`**: `HandleJoinChannelOpcode` and `HandleChannelInviteOpcode` call `getConfig` to check runtime configuration flags (`CONFIG_BOOL_GM_JOIN_OPPOSITE_FACTION_CHANNELS` and `CONFIG_UINT32_CHANNEL_INVITE_MIN_LEVEL`).
*   **`WorldSession.Main`**: All handlers call `GetPlayerPointer()` to obtain the current player object. `HandleJoinChannelOpcode` and `HandleLeaveChannelOpcode` also call `GetSecurity()` to enforce GM restrictions. `HandleJoinChannelOpcode` calls `SendPacket` to notify the client of errors.
*   **`ByteBuffer` / `WorldPacket`**: `HandleJoinChannelOpcode` constructs a `WorldPacket` manually to send the `SMSG_CHANNEL_NOTIFY` error message, using `operator<<` to serialize the error code and channel name.

### Called By

*   **`AiBotAI.Main/UpdateAI`**: Indicates that bot AI logic may programmatically trigger channel joins, likely for automated testing or bot behavior simulation.
*   **`ChatHandler.CharacterCommands/HandleChannelJoinCommand`** and **`ChatHandler.CharacterCommands/HandleChannelLeaveCommand`**: These chat command handlers wrap the opcode handlers, allowing players to join/leave channels via text commands (e.g., `/join`) in addition to the UI-generated opcodes.

## Data Model

This unit does not access any database tables directly. All channel state is managed in-memory by the `ChannelMgr` and `Channel` classes.

## Notable Implementation Details

1.  **Duplicate GM Logic**: `HandleJoinChannelOpcode` and `HandleLeaveChannelOpcode` contain nearly identical blocks of code to handle GM cross-faction channel access. This duplication exists because the standard `ChannelMgr` lookup is faction-specific. If the GM flag is active, the code manually looks up the *opposite* faction's `ChannelMgr` and attempts to join/leave there. This logic is gated by `sWorld.getConfig(CONFIG_BOOL_GM_JOIN_OPPOSITE_FACTION_CHANNELS)` and a check that the target channel has `GetSecurityLevel() == 0`.
2.  **Name Normalization Side Effects**: Several handlers (`SetOwner`, `Moderator`, `Mute`, etc.) call `ObjectMgr::normalizePlayerName` on the `packet.playerName` string. Note that this function takes a non-const `std::string&` (cast via `const_cast`), meaning it modifies the packet data in place. If normalization fails (returns false), the handler returns early, preventing invalid names from reaching the channel logic.
3.  **Level Gate for Invites**: `HandleChannelInviteOpcode` is the only handler that checks the player's level against a configurable minimum (`CONFIG_UINT32_CHANNEL_INVITE_MIN_LEVEL`). This prevents low-level characters from spamming invites.
4.  **Manual Packet Construction**: Unlike most handlers which rely on the `Channel` class to send responses, `HandleJoinChannelOpcode` manually constructs a `WorldPacket` for the `CHAT_INVALID_NAME_NOTICE` error. This suggests that invalid name validation is considered a session-layer concern rather than a channel-layer concern.
5.  **Empty Name Handling**: `HandleLeaveChannelOpcode` silently returns if the channel name is empty. `HandleJoinChannelOpcode` treats an empty name as invalid and sends an error notice. This asymmetry implies that the client is expected to provide a name for joins, but leaves might be triggered by other means or the empty check is a defensive guard.

## Member Reference

**HandleJoinChannelOpcode**: Validates channel name (must start with alpha), sends error if invalid. Joins channel on player's faction. If player is GM and config allows, also joins same-named channel on opposite faction (if security level 0).

**HandleLeaveChannelOpcode**: Leaves channel on player's faction. Updates `ChannelMgr` state. If player is GM and config allows, also leaves same-named channel on opposite faction.

**HandleChannelListOpcode**: Retrieves channel and requests member list via `Channel::List`.

**HandleChannelPasswordOpcode**: Changes channel password via `Channel::Password`.

**HandleChannelSetOwnerOpcode**: Normalizes target name, then transfers ownership via `Channel::SetOwner`.

**HandleChannelOwnerOpcode**: Requests owner info via `Channel::SendWhoOwner`.

**HandleChannelModeratorOpcode**: Normalizes target name, adds moderator via `Channel::SetModerator`.

**HandleChannelUnmoderatorOpcode**: Normalizes target name, removes moderator via `Channel::UnsetModerator`.

**HandleChannelMuteOpcode**: Normalizes target name, mutes player via `Channel::SetMute`.

**HandleChannelUnmuteOpcode**: Normalizes target name, unmutes player via `Channel::UnsetMute`.

**HandleChannelInviteOpcode**: Normalizes target name, checks player level against min-config, then invites via `Channel::Invite`.

**HandleChannelKickOpcode**: Normalizes target name, kicks player via `Channel::Kick`.

**HandleChannelBanOpcode**: Normalizes target name, bans player via `Channel::Ban`.

**HandleChannelUnbanOpcode**: Normalizes target name, unbans player via `Channel::UnBan`.

**HandleChannelAnnouncementsOpcode**: Toggles announcement setting via `Channel::Announce`.

**HandleChannelModerateOpcode**: Toggles moderate setting via `Channel::Moderate`.

---

<!-- machine-true, projected from graph.json -->

## Map — WorldSession.ChannelHandler

*Source:* ChannelHandler.cpp, WorldSession.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| HandleJoinChannelOpcode | method | AbstractPlayer/GetObjectGuid#2, AbstractPlayer/GetSession#2, AbstractPlayer/GetTeam#2, ByteBuffer/operator<<, ByteBuffer/operator<<#7, ChannelMgr/channelMgr, ChannelMgr/GetJoinChannel, game_Chat_Channel/GetSecurityLevel, game_Chat_Channel/Join, Player.Main/GetTeam, World/getConfig, WorldPacket/WorldPacket#4, WorldSession.Main/GetPlayerPointer, WorldSession.Main/GetSecurity, WorldSession.Main/SendPacket | AiBotAI.Main/UpdateAI, ChatHandler.CharacterCommands/HandleChannelJoinCommand | — |
| HandleLeaveChannelOpcode | method | AbstractPlayer/GetObjectGuid#2, AbstractPlayer/GetSession#2, AbstractPlayer/GetTeam#2, ChannelMgr/channelMgr, ChannelMgr/GetChannel, ChannelMgr/LeftChannel, game_Chat_Channel/Leave, World/getConfig, WorldSession.Main/GetPlayerPointer, WorldSession.Main/GetSecurity | ChatHandler.CharacterCommands/HandleChannelLeaveCommand | — |
| HandleChannelListOpcode | method | AbstractPlayer/GetTeam#2, ChannelMgr/channelMgr, ChannelMgr/GetChannel, game_Chat_Channel/List, WorldSession.Main/GetPlayerPointer | — | — |
| HandleChannelPasswordOpcode | method | AbstractPlayer/GetObjectGuid#2, AbstractPlayer/GetTeam#2, ChannelMgr/channelMgr, ChannelMgr/GetChannel, game_Chat_Channel/Password, WorldSession.Main/GetPlayerPointer | — | — |
| HandleChannelSetOwnerOpcode | method | AbstractPlayer/GetObjectGuid#2, AbstractPlayer/GetTeam#2, ChannelMgr/channelMgr, ChannelMgr/GetChannel, game_Chat_Channel/SetOwner, ObjectMgr/normalizePlayerName, WorldSession.Main/GetPlayerPointer | — | — |
| HandleChannelOwnerOpcode | method | AbstractPlayer/GetObjectGuid#2, AbstractPlayer/GetTeam#2, ChannelMgr/channelMgr, ChannelMgr/GetChannel, game_Chat_Channel/SendWhoOwner, WorldSession.Main/GetPlayerPointer | — | — |
| HandleChannelModeratorOpcode | method | AbstractPlayer/GetObjectGuid#2, AbstractPlayer/GetTeam#2, ChannelMgr/channelMgr, ChannelMgr/GetChannel, game_Chat_Channel/SetModerator, ObjectMgr/normalizePlayerName, WorldSession.Main/GetPlayerPointer | — | — |
| HandleChannelUnmoderatorOpcode | method | AbstractPlayer/GetObjectGuid#2, AbstractPlayer/GetTeam#2, ChannelMgr/channelMgr, ChannelMgr/GetChannel, game_Chat_Channel/UnsetModerator, ObjectMgr/normalizePlayerName, WorldSession.Main/GetPlayerPointer | — | — |
| HandleChannelMuteOpcode | method | AbstractPlayer/GetObjectGuid#2, AbstractPlayer/GetTeam#2, ChannelMgr/channelMgr, ChannelMgr/GetChannel, game_Chat_Channel/SetMute, ObjectMgr/normalizePlayerName, WorldSession.Main/GetPlayerPointer | — | — |
| HandleChannelUnmuteOpcode | method | AbstractPlayer/GetObjectGuid#2, AbstractPlayer/GetTeam#2, ChannelMgr/channelMgr, ChannelMgr/GetChannel, game_Chat_Channel/UnsetMute, ObjectMgr/normalizePlayerName, WorldSession.Main/GetPlayerPointer | — | — |
| HandleChannelInviteOpcode | method | AbstractPlayer/GetLevel#2, AbstractPlayer/GetObjectGuid#2, AbstractPlayer/GetTeam#2, ChannelMgr/channelMgr, ChannelMgr/GetChannel, game_Chat_Channel/Invite, ObjectMgr/normalizePlayerName, World/getConfig#4, WorldSession.Main/GetPlayerPointer | — | — |
| HandleChannelKickOpcode | method | AbstractPlayer/GetObjectGuid#2, AbstractPlayer/GetTeam#2, ChannelMgr/channelMgr, ChannelMgr/GetChannel, game_Chat_Channel/Kick, ObjectMgr/normalizePlayerName, WorldSession.Main/GetPlayerPointer | — | — |
| HandleChannelBanOpcode | method | AbstractPlayer/GetObjectGuid#2, AbstractPlayer/GetTeam#2, ChannelMgr/channelMgr, ChannelMgr/GetChannel, game_Chat_Channel/Ban, ObjectMgr/normalizePlayerName, WorldSession.Main/GetPlayerPointer | — | — |
| HandleChannelUnbanOpcode | method | AbstractPlayer/GetObjectGuid#2, AbstractPlayer/GetTeam#2, ChannelMgr/channelMgr, ChannelMgr/GetChannel, game_Chat_Channel/UnBan, ObjectMgr/normalizePlayerName, WorldSession.Main/GetPlayerPointer | — | — |
| HandleChannelAnnouncementsOpcode | method | AbstractPlayer/GetObjectGuid#2, AbstractPlayer/GetTeam#2, ChannelMgr/channelMgr, ChannelMgr/GetChannel, game_Chat_Channel/Announce, WorldSession.Main/GetPlayerPointer | — | — |
| HandleChannelModerateOpcode | method | AbstractPlayer/GetObjectGuid#2, AbstractPlayer/GetTeam#2, ChannelMgr/channelMgr, ChannelMgr/GetChannel, game_Chat_Channel/Moderate, WorldSession.Main/GetPlayerPointer | — | — |

---

<!-- verify: boundary-bleed | foreign: update, WorldSession -->
