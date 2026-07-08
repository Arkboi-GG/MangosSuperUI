<!-- provenance: boundary-bleed -->
# MasterPlayer.Chat

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# MasterPlayer.Chat

**Purpose & Responsibilities**

`MasterPlayer.Chat` implements the server-side logic for private messaging (whispers), status indicators (AFK/DND), and channel membership management within the `MasterPlayer` entity. This unit handles the construction of chat packets sent to clients, enforces anti-spam rate limiting for non-game-master players, manages the allow-list for incoming whispers when global acceptance is disabled, and ensures clean disconnection from all joined channels upon player destruction. It operates entirely in memory, with no direct database interaction.

## Member-by-Member Behavior

### Anti-Spam Protection
**UpdateSpeakTime**
This method enforces a rate-limiting mechanism to prevent chat flooding. It first checks the player's security level via `MasterPlayer.Main/GetSession` and `WorldSession.Main/GetSecurity`; if the player is a Game Master (`SEC_PLAYER` or higher), the check is bypassed entirely. For regular players, it compares the current time against `m_speakTime`. If the player attempts to speak before the delay expires, it increments `m_speakCount`. If this count exceeds the configured limit (`CONFIG_UINT32_CHATFLOOD_MESSAGE_COUNT` retrieved via `World/getConfig#4`), the player is muted for a duration defined by `CONFIG_UINT32_CHATFLOOD_MUTE_TIME`. The mute timer on the session is updated only if the new mute duration extends beyond any existing mute. Finally, `m_speakTime` is reset to the current time plus the configured delay (`CONFIG_UINT32_CHATFLOOD_MESSAGE_DELAY`).

### Private Messaging (Whispers)
**Whisper**
This method constructs and sends a whisper message to a specific `receiver`. It normalizes the language to `LANG_UNIVERSAL` unless the message is an addon command (`LANG_ADDON`). It builds a `CHAT_MSG_WHISPER` packet using `ChatHandler.Chat/BuildChatPacket`, incorporating the sender's tag, GUID, and name (retrieved via `MasterPlayer.Main` methods), and sends it to the receiver's session via `WorldSession.Main/SendPacket`.

If the message is not an addon command, it also sends a confirmation packet (`CHAT_MSG_WHISPER_INFORM`) back to the sender.

The method then checks the receiver's status. If the receiver is in Do Not Disturb mode (`receiver->IsDND()`), it sends a `CHAT_MSG_DND` packet containing the receiver's custom DND message. If the receiver is Away From Keyboard (`receiver->IsAFK()`), it sends a `CHAT_MSG_AFK` packet with the AFK message. These status notifications are sent to the *sender*, informing them why the receiver might not respond.

Finally, if the sender has disabled global whisper acceptance (`!IsAcceptWhispers()`), the receiver's GUID is added to the sender's allowed whisperer list via `MasterPlayer.Main/AddAllowedWhisperer`, ensuring future whispers from this receiver will be accepted despite the global setting.

### Status Indicators
**ToggleDND**
Toggles the player's "Do Not Disturb" status. It flips `m_chatTag` between `2` (DND) and `0` (Normal).

**ToggleAFK**
Toggles the player's "Away From Keyboard" status. It flips `m_chatTag` between `1` (AFK) and `0` (Normal).

Note: Both methods rely solely on the internal `m_chatTag` variable. They do not broadcast the status change to other players or update the database; those responsibilities likely lie in other units or are triggered by separate events.

### Channel Management
**JoinedChannel**
Adds a `Channel` pointer to the player's internal list of joined channels (`m_channels`). This is a simple bookkeeping operation.

**LeftChannel**
Removes a specific `Channel` pointer from the player's internal list of joined channels.

**CleanupChannels**
Called during player destruction (by `MasterPlayer.Main/~MasterPlayer`), this method iterates through all channels the player is still a member of. For each channel, it removes the player from the local list, calls `game_Chat_Channel/Leave` to remove the player from the channel's internal roster (without sending a packet to the client, as the player is disconnecting), and then asks the `ChannelMgr` (retrieved via `channelMgr` and `MasterPlayer.Main/GetTeam`) to delete the channel if it becomes empty (`ChannelMgr/LeftChannel`). This ensures no orphaned channel entries remain for disconnected players.

## Cross-Unit Boundaries

*   **WorldSession.ChatHandler**: Calls `UpdateSpeakTime` and `Whisper` when processing chat opcodes. This indicates that chat input validation and dispatch happen in the session handler, which delegates to the player object for state updates and packet construction.
*   **ChatHandler.DebugCommands**: Calls `Whisper` directly, likely for testing or debugging purposes, bypassing normal chat opcode handling.
*   **MasterPlayer.Main**: Provides essential identity and state data (`GetSession`, `GetName`, `GetObjectGuid`, `GetChatTag`, `IsAFK`, `IsDND`, `IsAcceptWhispers`, `AddAllowedWhisperer`, `GetTeam`) required for chat operations. It also triggers `CleanupChannels` during destruction.
*   **ChatHandler.Chat**: Used by `Whisper` to build the binary packet structures for whispers and status notifications.
*   **World**: Provides configuration values for chat flood protection limits and delays.
*   **ChannelMgr**: Used by `CleanupChannels` to manage the lifecycle of channels, specifically deleting empty ones.
*   **game_Chat_Channel**: Used by `CleanupChannels` to remove the player from the channel's internal state.

## Data Model

This unit does not interact with any database tables. All state (chat tags, speak timers, channel lists, allowed whisperers) is held in memory within the `MasterPlayer` object.

## Notable Implementation Details

*   **GM Exemption**: `UpdateSpeakTime` explicitly exempts Game Masters from chat flood protection. This is a critical privilege distinction.
*   **Whisper Allow-List Logic**: In `Whisper`, the code adds the *receiver* to the *sender's* allowed list if the sender has disabled whispers. This seems counter-intuitive at first glance. Typically, one would expect the *sender* to be added to the *receiver's* allowed list if the receiver has disabled whispers. However, reading the code carefully: `if (!IsAcceptWhispers()) AddAllowedWhisperer(receiver->GetObjectGuid());`. `IsAcceptWhispers()` is a method on `this` (the sender). So, if the *sender* has disabled whispers, they add the *receiver* to their allow list? This allows the sender to receive replies from the person they just whispered to, even though they've blocked general whispers. This is a subtle but important detail for maintaining conversation flow when one party has restricted access.
*   **Status Notification Direction**: In `Whisper`, the AFK/DND notifications are sent to the *sender* (`GetSession()->SendPacket`), not the receiver. This informs the sender that the recipient is unavailable.
*   **Channel Cleanup Safety**: `CleanupChannels` uses a `while` loop with `erase` on the front of the list to safely iterate and modify the container simultaneously. It also ensures that the channel manager is retrieved based on the player's team, implying separate channel managers for different factions.
*   **No Database Persistence**: Chat-related state like `m_chatTag`, `m_speakTime`, `m_speakCount`, and `m_allowedWhispers` are not saved to the database. This means these states are lost upon server restart or player logout/login cycles, which is typical for transient chat states.

## Member Reference

**UpdateSpeakTime**
Enforces anti-spam rate limiting for non-GM players by tracking message frequency and applying temporary mutes based on world configuration settings.

**Whisper**
Constructs and sends whisper packets between players, handles addon vs. universal language, sends status notifications (AFK/DND) to the sender, and manages the sender's whisper allow-list.

**ToggleDND**
Toggles the player's Do Not Disturb status by flipping the `m_chatTag` value.

**ToggleAFK**
Toggles the player's Away From Keyboard status by flipping the `m_chatTag` value.

**JoinedChannel**
Adds a channel pointer to the player's internal list of joined channels.

**LeftChannel**
Removes a channel pointer from the player's internal list of joined channels.

**CleanupChannels**
Iterates through all joined channels upon player destruction, removing the player from each channel's roster and deleting empty channels via the Channel Manager.

---

<!-- machine-true, projected from graph.json -->

## Map — MasterPlayer.Chat

*Source:* MasterPlayerChat.cpp, MasterPlayer.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| UpdateSpeakTime | method | MasterPlayer.Main/GetSession, World/getConfig#4, WorldSession.Main/GetSecurity | WorldSession.ChatHandler/HandleChatMessageOpcode | — |
| Whisper | method | ByteBuffer/clear, ChatHandler.Chat/BuildChatPacket, MasterPlayer.Main/AddAllowedWhisperer, MasterPlayer.Main/GetChatTag, MasterPlayer.Main/GetName, MasterPlayer.Main/GetObjectGuid, MasterPlayer.Main/GetSession, MasterPlayer.Main/IsAcceptWhispers, MasterPlayer.Main/IsAFK, MasterPlayer.Main/IsDND, WorldPacket/WorldPacket, WorldSession.Main/SendPacket | ChatHandler.DebugCommands/HandleDebugChatFreezeCommand, WorldSession.ChatHandler/HandleChatMessageOpcode | — |
| ToggleDND | method | — | — | — |
| ToggleAFK | method | — | — | — |
| JoinedChannel | method | — | — | — |
| LeftChannel | method | — | — | — |
| CleanupChannels | method | ChannelMgr/channelMgr, ChannelMgr/LeftChannel, game_Chat_Channel/GetName, game_Chat_Channel/Leave, MasterPlayer.Main/GetObjectGuid, MasterPlayer.Main/GetTeam | MasterPlayer.Main/~MasterPlayer | — |

---

<!-- verify: boundary-bleed | foreign: MasterPlayer, update -->
