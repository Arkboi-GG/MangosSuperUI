<!-- provenance: verbose -->
# ChannelMgr

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ChannelMgr

**Purpose & Responsibilities**

`ChannelMgr` manages the in-memory registry of chat channels for a specific faction. Two singleton subclasses, `AllianceChannelMgr` and `HordeChannelMgr`, isolate channel state per faction unless the `CONFIG_BOOL_ALLOW_TWO_SIDE_INTERACTION_CHANNEL` server setting enables cross-faction sharing (in which case both factions use the `AllianceChannelMgr` instance).

Responsibilities:
1.  **Registry:** Stores `Channel` objects in a `std::map<std::wstring, Channel*>` keyed by lowercase wide-string names.
2.  **Lifecycle:** Lazily creates channels via `GetJoinChannel`, retrieves them via `GetChannel`, and destroys empty, non-constant, public channels via `LeftChannel`.
3.  **System Channels:** Initializes hidden, high-security anti-cheat/moderation channels during construction.
4.  **Broadcasting:** Provides `AnnounceBothFactionsChannel` to send messages to both faction instances.

No database tables are used; all state is volatile.

## Member-by-Member Behavior

### Singleton Access

**`channelMgr`**
Global accessor returning the `ChannelMgr` singleton for the given `Team`.
*   If `CONFIG_BOOL_ALLOW_TWO_SIDE_INTERACTION_CHANNEL` is true, returns `AllianceChannelMgr` regardless of team.
*   Otherwise, returns `AllianceChannelMgr` for `ALLIANCE` and `HordeChannelMgr` for `HORDE`.
*   Returns `nullptr` for invalid teams.
*   **Calls:** `World/getConfig`.
*   **Called by:** `AiBotAI.Bridge/BridgeHandleSayText`, `WorldSession.ChannelHandler` opcodes, `WorldSession.ChatHandler/HandleChatMessageOpcode`.

### Channel Lifecycle

**`GetJoinChannel`**
Retrieves an existing channel or creates a new one.
1.  Converts `name` to lowercase wide string.
2.  If not in map:
    *   Checks `DBCStores/GetChannelEntryFor`. If `allowAreaDependantChans` is false and the DBC entry has `CHANNEL_DBC_FLAG_ZONE_DEP`, returns `nullptr`.
    *   Otherwise, creates a new `Channel`, inserts it into the map, and returns it.
3.  If found, returns the existing pointer.
*   **Calls:** `DBCStores/GetChannelEntryFor`, `game_Chat_Channel/Channel`, `shared_Util/Utf8toWStr`, `shared_Util/wstrToLower`.
*   **Called by:** `AiBotAI.Bridge/BridgeHandleSayText`, `WorldSession.ChannelHandler/HandleJoinChannelOpcode`.

**`GetChannel`**
Retrieves an existing channel without creating one.
1.  Converts `name` to lowercase wide string.
2.  If not found:
    *   If `pkt` is true, sends a "Not On Channel" packet to `p` via `WorldSession.Main/SendPacket`.
    *   Returns `nullptr`.
3.  If found, returns the pointer.
*   **Calls:** `AbstractPlayer/GetSession#2`, `game_Chat_Channel/MakeNotOnPacket`, `shared_Util/Utf8toWStr`, `shared_Util/wstrToLower`, `WorldPacket/WorldPacket`, `WorldSession.Main/SendPacket`.
*   **Called by:** Most `WorldSession.ChannelHandler` opcodes and `WorldSession.ChatHandler/HandleChatMessageOpcode`.

**`LeftChannel`**
Attempts to destroy a channel if it is no longer needed.
1.  Finds the channel by lowercase name.
2.  If the channel has 0 players, is not constant, and has no security level, it erases it from the map and deletes the object.
*   **Calls:** `game_Chat_Channel/GetNumPlayers`, `game_Chat_Channel/GetSecurityLevel`, `game_Chat_Channel/IsConstant`, `shared_Util/Utf8toWStr`, `shared_Util/wstrToLower`.
*   **Called by:** `MasterPlayer.Chat/CleanupChannels`, `Player.Main/CleanupChannels`, `WorldSession.ChannelHandler/HandleLeaveChannelOpcode`.

### Initialization & Utilities

**`ChannelMgr#2`**
Constructor. Calls `CreateDefaultChannels()` to initialize system channels.

**`~ChannelMgr`**
Destructor. Deletes all `Channel` pointers in the map and clears it.

**`CreateDefaultChannels`**
Creates hidden system channels with high security levels:
*   `SEC_GAMEMASTER`: "Warden", "Anticrash", "Antiflood", "ItemsCheck", "GoldDupe", "SAC", "MailsAC", "BotsDetector", "LowLevelBots".
*   `SEC_MODERATOR`: "ChatSpam".
Sets `SetAnnounce(false)` for all channels in the map to suppress join/leave broadcasts.
*   **Calls:** `game_Chat_Channel/SetAnnounce`, `game_Chat_Channel/SetSecurityLevel`.

**`AnnounceBothFactionsChannel`**
Static method. Sends `message` to `channelName` on the Horde instance. If cross-faction interaction is disabled, also sends it to the Alliance instance.
*   **Calls:** `game_Chat_Channel/Say`, `World/getConfig`.

## Data Model

This unit uses no database tables.

## Notable Implementation Details

*   **Case Insensitivity:** All channel names are converted to lowercase wide strings (`Utf8toWStr` + `wstrToLower`) for storage and lookup.
*   **Memory Ownership:** `ChannelMgr` owns `Channel` objects via raw pointers. `LeftChannel` is the sole cleanup mechanism for dynamic channels; failure to call it on disconnect leads to leaks.
*   **Zone Dependency:** `GetJoinChannel` blocks creation of zone-dependent DBC channels if `allowAreaDependantChans` is false, preventing invalid joins.
*   **Cross-Faction Logic:** `AnnounceBothFactionsChannel` explicitly checks the config to avoid duplicate sends when factions share a manager.

## Member Reference

**`channelMgr`**: Global accessor returning the faction-specific `ChannelMgr` singleton based on `Team` and cross-faction config. Calls `World/getConfig`. Called by AI and session handlers.

**`ChannelMgr#2`**: Constructor initializing system channels via `CreateDefaultChannels()`.

**`~ChannelMgr`**: Destructor deleting all managed `Channel` objects.

**`GetJoinChannel`**: Retrieves or creates a channel, respecting zone-dependency flags. Calls `DBCStores/GetChannelEntryFor`, `game_Chat_Channel/Channel`, `shared_Util/Utf8toWStr`, `shared_Util/wstrToLower`. Called by `AiBotAI.Bridge/BridgeHandleSayText`, `WorldSession.ChannelHandler/HandleJoinChannelOpcode`.

**`GetChannel`**: Retrieves an existing channel, sending an error packet if missing. Calls `AbstractPlayer/GetSession#2`, `game_Chat_Channel/MakeNotOnPacket`, `shared_Util/Utf8toWStr`, `shared_Util/wstrToLower`, `WorldPacket/WorldPacket`, `WorldSession.Main/SendPacket`. Called by `WorldSession.ChannelHandler` opcodes and `WorldSession.ChatHandler/HandleChatMessageOpcode`.

**`LeftChannel`**: Destroys empty, non-constant, public channels. Calls `game_Chat_Channel/GetNumPlayers`, `game_Chat_Channel/GetSecurityLevel`, `game_Chat_Channel/IsConstant`, `shared_Util/Utf8toWStr`, `shared_Util/wstrToLower`. Called by `MasterPlayer.Chat/CleanupChannels`, `Player.Main/CleanupChannels`, `WorldSession.ChannelHandler/HandleLeaveChannelOpcode`.

**`CreateDefaultChannels`**: Initializes hidden system channels with high security and disabled announcements. Calls `game_Chat_Channel/SetAnnounce`, `game_Chat_Channel/SetSecurityLevel`.

**`AnnounceBothFactionsChannel`**: Static method broadcasting a message to both faction channel instances. Calls `game_Chat_Channel/Say`, `World/getConfig`.

---

<!-- machine-true, projected from graph.json -->

## Map — ChannelMgr

*Source:* ChannelMgr.cpp, ChannelMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| channelMgr | function | World/getConfig | AiBotAI.Bridge/BridgeHandleSayText, MasterPlayer.Chat/CleanupChannels, Player.Main/CleanupChannels, WorldSession.ChannelHandler/HandleChannelAnnouncementsOpcode, WorldSession.ChannelHandler/HandleChannelBanOpcode, WorldSession.ChannelHandler/HandleChannelInviteOpcode, WorldSession.ChannelHandler/HandleChannelKickOpcode, WorldSession.ChannelHandler/HandleChannelListOpcode, WorldSession.ChannelHandler/HandleChannelModerateOpcode, WorldSession.ChannelHandler/HandleChannelModeratorOpcode, WorldSession.ChannelHandler/HandleChannelMuteOpcode, WorldSession.ChannelHandler/HandleChannelOwnerOpcode, WorldSession.ChannelHandler/HandleChannelPasswordOpcode, WorldSession.ChannelHandler/HandleChannelSetOwnerOpcode, WorldSession.ChannelHandler/HandleChannelUnbanOpcode, WorldSession.ChannelHandler/HandleChannelUnmoderatorOpcode, WorldSession.ChannelHandler/HandleChannelUnmuteOpcode, WorldSession.ChannelHandler/HandleJoinChannelOpcode, WorldSession.ChannelHandler/HandleLeaveChannelOpcode, WorldSession.ChatHandler/HandleChatMessageOpcode | — |
| ChannelMgr#2 | ctor | — | — | — |
| ~ChannelMgr | dtor | — | — | — |
| GetJoinChannel | method | DBCStores/GetChannelEntryFor, game_Chat_Channel/Channel, shared_Util/Utf8toWStr, shared_Util/wstrToLower | AiBotAI.Bridge/BridgeHandleSayText, WorldSession.ChannelHandler/HandleJoinChannelOpcode | — |
| GetChannel | method | AbstractPlayer/GetSession#2, game_Chat_Channel/MakeNotOnPacket, shared_Util/Utf8toWStr, shared_Util/wstrToLower, WorldPacket/WorldPacket, WorldSession.Main/SendPacket | WorldSession.ChannelHandler/HandleChannelAnnouncementsOpcode, WorldSession.ChannelHandler/HandleChannelBanOpcode, WorldSession.ChannelHandler/HandleChannelInviteOpcode, WorldSession.ChannelHandler/HandleChannelKickOpcode, WorldSession.ChannelHandler/HandleChannelListOpcode, WorldSession.ChannelHandler/HandleChannelModerateOpcode, WorldSession.ChannelHandler/HandleChannelModeratorOpcode, WorldSession.ChannelHandler/HandleChannelMuteOpcode, WorldSession.ChannelHandler/HandleChannelOwnerOpcode, WorldSession.ChannelHandler/HandleChannelPasswordOpcode, WorldSession.ChannelHandler/HandleChannelSetOwnerOpcode, WorldSession.ChannelHandler/HandleChannelUnbanOpcode, WorldSession.ChannelHandler/HandleChannelUnmoderatorOpcode, WorldSession.ChannelHandler/HandleChannelUnmuteOpcode, WorldSession.ChannelHandler/HandleLeaveChannelOpcode, WorldSession.ChatHandler/HandleChatMessageOpcode | — |
| LeftChannel | method | game_Chat_Channel/GetNumPlayers, game_Chat_Channel/GetSecurityLevel, game_Chat_Channel/IsConstant, shared_Util/Utf8toWStr, shared_Util/wstrToLower | MasterPlayer.Chat/CleanupChannels, Player.Main/CleanupChannels, WorldSession.ChannelHandler/HandleLeaveChannelOpcode | — |
| CreateDefaultChannels | method | game_Chat_Channel/SetAnnounce, game_Chat_Channel/SetSecurityLevel | — | — |
| AnnounceBothFactionsChannel | method | game_Chat_Channel/Say, World/getConfig | — | — |
