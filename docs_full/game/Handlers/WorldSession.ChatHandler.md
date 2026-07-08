<!-- provenance: failed-members, boundary-bleed -->
# WorldSession.ChatHandler

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# WorldSession.ChatHandler

## Purpose & Responsibilities

The `WorldSession.ChatHandler` partial implements the server-side logic for processing all player-initiated communication events within the WoWVMaNGOS emulator. It serves as the primary gateway for chat messages, emotes, and related social interactions, enforcing game rules, security policies, and anti-abuse measures before broadcasting content to other players or systems.

Its core responsibilities include:
1.  **Input Validation & Sanitization:** Stripping invisible characters, validating links, checking language permissions, and ensuring messages comply with server-wide configuration settings (e.g., strict Latin character requirements).
2.  **Anti-Spam & Abuse Prevention:** Implementing cooldowns for public channels, integrating with the `Anticheat` system for spam detection, enforcing mute timers, and restricting usage based on player level, trial status, or game master privileges.
3.  **Message Routing:** Determining the correct audience for a message (Say, Yell, Whisper, Party, Raid, Guild, Channel, etc.) and constructing the appropriate network packets to broadcast them.
4.  **Emote Processing:** Handling both simple client-side emotes (wave) and complex text-emotes that require server-side packet construction and spatial broadcasting.
5.  **Social State Management:** Updating AFK/DND states and handling ignore lists.

This unit does not store data in the database; it operates entirely on in-memory objects (`Player`, `Group`, `Guild`, `Channel`) and configuration values.

## Member-by-Member Behavior

### Message Sanitization & Pre-processing

**`SanitizeChatMessage`**
This method performs initial validation on a chat message string.
1.  It rejects empty messages.
2.  It bypasses validation for `LANG_ADDON` messages, assuming they are trusted internal communications.
3.  If `CONFIG_BOOL_CHAT_FAKE_MESSAGE_PREVENTING` is enabled, it calls `shared_Util::stripLineInvisibleChars` to remove hidden formatting codes that could be used for spoofing.
4.  If `CONFIG_UINT32_CHAT_STRICT_LINK_CHECKING_SEVERITY` is set, it invokes `ChatHandler.Chat/isValidChatMessage`. If the message contains invalid links, it logs an error via `Log.Main/Out`. Depending on `CONFIG_UINT32_CHAT_STRICT_LINK_CHECKING_KICK`, it may immediately disconnect the player via `WorldSession.Main/KickPlayer`.

**`SanitizeChatMessageAndProcessCommand`**
This is a convenience wrapper that chains sanitization with command parsing.
1.  It first calls `SanitizeChatMessage`. If sanitization fails, it returns `false`.
2.  It creates a temporary `ChatHandler` instance and calls `ChatHandler.Chat/ParseCommands`.
3.  If a valid console command is detected and handled (returning `CommandDetectedAndHandled`), it returns `false` to indicate the input was consumed as a command, not a chat message. Otherwise, it returns `true`.

**`IsLanguageAllowedForChatType`**
A static helper that enforces language restrictions based on the chat context.
1.  **Addon Language (`LANG_ADDON`):** Allowed only in specific group/guild/channel contexts (Party, Guild, Officer, Raid, Channel, and BG variants depending on client build). It is explicitly forbidden in general Say/Yell/Whisper contexts.
2.  **Universal Language (`LANG_UNIVERSAL`):** Allowed only for AFK and DND status updates.
3.  **Other Languages:** Generally allowed, returning `true`.

**`ChatCooldown`**
Calculates the remaining cooldown time for sending messages in public channels.
1.  It retrieves configuration values for cooldown duration, minimum level, maximum level for scaling, and whether to use account max level instead of character level.
2.  If the player's level (or account max level) is below the configured threshold, it calculates the time elapsed since the last public channel message (`WorldSession.Main/GetLastPubChanMsgTime`).
3.  If `CONFIG_UINT32_WORLD_CHAN_CD_SCALING` is enabled, it scales the cooldown linearly based on how far below the max level the player is.
4.  If the elapsed time is less than the calculated cooldown, it returns the remaining seconds. Otherwise, it returns `0`.

### Opcode Handlers: Chat & Emotes

**`HandleChatMessageOpcode`**
The central dispatcher for all chat-related opcodes. It processes a `ChatMessage` packet through a rigorous pipeline:

1.  **Basic Validation:**
    *   Checks if the message type is valid.
    *   Verifies language permission via `IsLanguageAllowedForChatType`.
    *   Blocks `LANG_ADDON` if `CONFIG_BOOL_ADDON_CHANNEL` is disabled.

2.  **Language Resolution & Overrides:**
    *   If not `LANG_ADDON`, it checks if the player knows the language (`Player.Main/KnowsLanguage`). If not, it sends a notification and aborts.
    *   **GM Override:** Game Masters always speak in `LANG_UNIVERSAL`.
    *   **Cross-Faction Chat:** If enabled, Common/Orcish are converted to Universal. Specific group/guild configs can also force Universal language for mixed-faction groups.
    *   **Spell Auras:** Checks for `SPELL_AURA_MOD_LANGUAGE` auras on the player. If present, the aura's modifier overrides the chosen language.

3.  **Anti-Spam & Mute Checks:**
    *   Skips these checks for AFK/DND updates.
    *   Checks the session's `m_muteTime`. If muted, it notifies the player of the remaining time and aborts.
    *   Updates the master player's speak time for flood protection (`MasterPlayer.Chat/UpdateSpeakTime`).
    *   Runs `SanitizeChatMessageAndProcessCommand`. If it returns `false` (invalid or command), it aborts.
    *   Queries the `Anticheat` system (`AntispamInterface/isMuted`) for additional spam-based muting.

4.  **Routing by Message Type:**
    *   **`CHAT_MSG_CHANNEL`:**
        *   Retrieves the channel object.
        *   Enforces level restrictions for restricted channels.
        *   For public channels: Checks trial restrictions, GM restrictions, and strict Latin character requirements (using `AntispamInterface/normalizeMessage` and `shared_Util/isBasicLatinString`).
        *   Applies `ChatCooldown`.
        *   Broadcasts via `game_Chat_Channel/Say`.
        *   Logs the chat and adds to antispam history if applicable.
    *   **`CHAT_MSG_SAY` / `CHAT_MSG_YELL` / `CHAT_MSG_EMOTE`:**
        *   Enforces minimum level requirements.
        *   Checks if the player is alive.
        *   Broadcasts via `Player.Main/Say`, `Player.Main/Yell`, or `Player.Main/TextEmote`.
        *   Logs and adds to antispam history.
    *   **`CHAT_MSG_WHISPER`:**
        *   Resolves the target player by name. If not found, sends `SendPlayerNotFoundNotice`.
        *   Checks security levels: Players cannot whisper GMs unless the GM accepts whispers from them.
        *   Checks mute status for whispering to non-GMs.
        *   Enforces cross-faction whisper restrictions.
        *   Enforces zone-level restrictions for low-level players.
        *   Checks whisper restrictions (friends-only for trials, global config).
        *   Sends the whisper via `MasterPlayer.Chat/Whisper`.
        *   Logs and adds to antispam history.
    *   **`CHAT_MSG_PARTY` / `CHAT_MSG_RAID` / `CHAT_MSG_RAID_LEADER` / `CHAT_MSG_RAID_WARNING`:**
        *   Retrieves the player's group. Handles Battleground group exceptions.
        *   Validates leadership/assistant status for Leader/Warning messages.
        *   Constructs a chat packet using `ChatHandler.Chat/BuildChatPacket`.
        *   Broadcasts to the group/subgroup via `game_Group_Group/BroadcastPacket`.
        *   Logs the chat.
    *   **`CHAT_MSG_GUILD` / `CHAT_MSG_OFFICER`:**
        *   Retrieves the guild object.
        *   Broadcasts via `game_Guild_Guild/BroadcastToGuild` or `BroadcastToOfficers`.
        *   Logs the chat.
    *   **`CHAT_MSG_BATTLEGROUND` / `CHAT_MSG_BATTLEGROUND_LEADER`:**
        *   Similar to Raid, but targets the BG group.
    *   **`CHAT_MSG_AFK` / `CHAT_MSG_DND`:**
        *   Prevents toggling during combat.
        *   Updates the AFK/DND message strings on the `MasterPlayer`.
        *   Toggles the AFK/DND flags on the `Player`. Note the mutual exclusivity logic: setting DND turns off AFK, and vice versa.

**`HandleEmoteOpcode`**
Handles simple, predefined emotes (like waving).
1.  Checks if the player is alive and not prevented from animating.
2.  Checks if the player can speak (not muted).
3.  Restricts allowed emotes to `EMOTE_ONESHOT_NONE` and `EMOTE_ONESHOT_WAVE`.
4.  Interrupts spells with animation-canceling flags.
5.  Executes the emote via `Unit.Main/HandleEmoteCommand`.

**`HandleTextEmoteOpcode`**
Handles complex text emotes (e.g., "Player X bows to Player Y").
1.  Performs similar alive/speak checks as `HandleEmoteOpcode`.
2.  Looks up the emote definition in `sEmotesTextStore`.
3.  For non-passive emotes (Sleep, Sit, Kneel), it interrupts conflicting spells and executes the emote via `Unit.Main/HandleEmote`.
4.  Identifies the target unit from the packet GUID.
5.  Constructs a localized chat packet using the `EmoteChatBuilder` functor.
6.  Broadcasts the packet to nearby players within the configured listen range using `Cell::VisitWorldObjects`.
7.  If the target is a creature with AI, it triggers `CreatureAI/ReceiveEmote`.

**`HandleChatIgnoredOpcode`**
Handles the client request to notify a player that they are being ignored.
1.  Finds the target player by GUID.
2.  Constructs a `CHAT_MSG_IGNORED` packet.
3.  Sends it directly to the target player's session.

### Notification Helpers

**`SendPlayerNotFoundNotice`**
Constructs and sends an `SMSG_CHAT_PLAYER_NOT_FOUND` packet containing the searched name.

**`SendWrongFactionNotice`**
Constructs and sends an `SMSG_CHAT_WRONG_FACTION` packet.

**`SendChatRestrictedNotice`**
Constructs and sends an `SMSG_CHAT_RESTRICTED` packet (client-build dependent).

### Internal Functor

**`EmoteChatBuilder`**
A functor class used by `HandleTextEmoteOpcode` to construct the `SMSG_TEXT_EMOTE` packet.
1.  **Constructor:** Stores references to the player, emote IDs, and target unit.
2.  **`operator()`**: Called for each locale index. It initializes the packet, appends the player's GUID, emote IDs, and the target's name (localized if available). It handles the variable-length name field carefully.

## Cross-Unit Boundaries

*   **ChatHandler.Chat:**
    *   `SanitizeChatMessage` calls `isValidChatMessage` to check for malicious links.
    *   `SanitizeChatMessageAndProcessCommand` calls `ParseCommands` to intercept console commands.
    *   `HandleChatMessageOpcode` uses `BuildChatPacket` to format group/raid messages and `SendSysMessage`/`PSendSysMessage` for error notifications.
*   **Anticheat / AntispamInterface:**
    *   `HandleChatMessageOpcode` integrates deeply with the anticheat system. It calls `isMuted` to block spammers, `normalizeMessage` to strip formatting for Latin-checking, and `addMessage` to feed chat history into the spam detection algorithm.
*   **Player.Main / MasterPlayer:**
    *   Extensive interaction with `Player` and `MasterPlayer` classes to retrieve state (level, team, guild ID, group, AFK/DND status), modify state (toggle AFK/DND, update speak time), and perform actions (Say, Yell, Whisper, TextEmote).
*   **Group / Guild / Channel:**
    *   `HandleChatMessageOpcode` delegates broadcasting to `Group::BroadcastPacket`, `Guild::BroadcastToGuild`, and `Channel::Say`. It also queries these objects for membership and leadership status.
*   **World:**
    *   Reads configuration flags (`getConfig`) extensively to determine server behavior (cross-faction chat, strict Latin, cooldowns, etc.).
    *   Calls `World/LogChat` to record chat history for moderation/logging purposes.
*   **ObjectAccessor / ObjectMgr:**
    *   Uses `FindMasterPlayer` to resolve whisper targets by name.
    *   Uses `GetPlayer` in `HandleChatIgnoredOpcode` to resolve GUIDs.
*   **Log.Main:**
    *   Logs errors for invalid message types, languages, and invalid chat links.

## Data Model

This unit does not interact directly with database tables. All data operations are performed against in-memory objects (`Player`, `Group`, `Guild`, `Channel`) and server configuration variables. Chat logging is delegated to `World/LogChat`, which may persist data, but the schema and mechanism are outside this unit's scope.

## Notable Implementation Details

1.  **Language Overriding Logic:** The language resolution in `HandleChatMessageOpcode` is complex and layered. It starts with the client's choice, but can be overridden by GM status, cross-faction configurations, and finally by active spell auras (`SPELL_AURA_MOD_LANGUAGE`). The final language used for broadcasting is determined after all these checks.
2.  **Strict Latin Checking:** For public channels, if `CONFIG_BOOL_STRICT_LATIN_IN_GENERAL_CHANNELS` is enabled, the code normalizes the message (removing colors/punctuation) and converts it to wide string to check if it contains only basic Latin characters. This is a performance-heavy operation guarded by the config flag.
3.  **AFK/DND Mutual Exclusivity:** In `HandleChatMessageOpcode`, the logic for `CHAT_MSG_AFK` and `CHAT_MSG_DND` ensures that a player cannot be both AFK and DND simultaneously. Setting one automatically toggles off the other.
4.  **Battleground Group Handling:** For Party/Raid/BG chat types, the code distinguishes between `GetOriginalGroup()` and `GetGroup()`. In Battlegrounds, players are often in a temporary BG group, and the code ensures chat is routed correctly or blocked if the player is not in a valid group context.
5.  **Emote Broadcasting:** `HandleTextEmoteOpcode` uses a custom functor (`EmoteChatBuilder`) and `Cell::VisitWorldObjects` to broadcast emotes only to players within a specific range (`CONFIG_FLOAT_LISTEN_RANGE_TEXTEMOTE`). This is more efficient than global broadcasting and respects spatial awareness.
6.  **Const-Correctness Workaround:** In `HandleChatMessageOpcode`, `packet.lang` is declared `const`, but the code needs to modify it based on GM status or cross-faction rules. It uses `const_cast<uint32&>(packet.lang)` to achieve this, which is a notable deviation from strict const-correctness but necessary due to the packet structure.
7.  **Antispam Integration:** The integration with `Anticheat` is pervasive. Messages are added to the antispam buffer *after* successful broadcasting, ensuring only valid, delivered messages contribute to the spam score. However, mute checks happen *before* processing.

## Member Reference

**SanitizeChatMessage**
Validates and cleans a chat message string. Strips invisible characters if configured, checks for invalid links, and potentially kicks the player if strict link checking is enabled and violated. Returns `false` if the message is invalid or rejected.

**SanitizeChatMessageAndProcessCommand**
Chains `SanitizeChatMessage` with command parsing. If the message is a valid console command, it handles it and returns `false`. Otherwise, it returns `true` indicating the message should be treated as chat.

**IsLanguageAllowedForChatType**
Static helper that determines if a specific language (e.g., Addon, Universal) is permitted for a given chat message type (e.g., Party, Whisper, AFK).

**ChatCooldown**
Calculates the remaining cooldown time for public channel messages based on player level, account max level, and server configuration. Returns `0` if no cooldown is active.

**HandleChatMessageOpcode**
Main dispatcher for all chat messages. Validates language, resolves overrides (GM, cross-faction, auras), checks mutes/spam, and routes the message to the appropriate audience (Say, Yell, Whisper, Group, Guild, Channel, etc.). Logs chat and updates antispam history.

**HandleEmoteOpcode**
Processes simple emotes (wave). Checks for alive/speak status, restricts to allowed emotes, interrupts conflicting spells, and executes the emote.

**EmoteChatBuilder**
Functor class used to construct `SMSG_TEXT_EMOTE` packets for localized broadcasting.

**operator()**
Implementation of `EmoteChatBuilder`'s call operator. Initializes the packet and appends player GUID, emote IDs, and target name for a specific locale.

**HandleTextEmoteOpcode**
Processes complex text emotes. Looks up emote data, executes the emote action, identifies the target, and broadcasts the emote to nearby players using `EmoteChatBuilder` and spatial queries. Triggers AI responses if the target is a creature.

**HandleChatIgnoredOpcode**
Handles the "ignored" notification. Finds the target player by GUID and sends them a packet indicating the sender is ignoring them.

**SendPlayerNotFoundNotice**
Sends a `SMSG_CHAT_PLAYER_NOT_FOUND` packet to the client with the specified name.

**SendWrongFactionNotice**
Sends a `SMSG_CHAT_WRONG_FACTION` packet to the client.

**SendChatRestrictedNotice**
Sends a `SMSG_CHAT_RESTRICTED` packet to the client (client-build dependent).

---

<!-- machine-true, projected from graph.json -->

## Map — WorldSession.ChatHandler

*Source:* ChatHandler.cpp, WorldSession.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| SanitizeChatMessage | method | ChatHandler.Chat/ChatHandler#3, ChatHandler.Chat/isValidChatMessage, Log.Main/Out, Object/GetGUIDLow, Player.Main/GetName, shared_Util/stripLineInvisibleChars, World/getConfig, World/getConfig#4, WorldSession.Main/GetPlayer, WorldSession.Main/KickPlayer | — | — |
| SanitizeChatMessageAndProcessCommand | method | ChatHandler.Chat/ChatHandler#3, ChatHandler.Chat/ParseCommands | WorldSession.MiscHandler/HandleTeleportToUnitOpcode | — |
| IsLanguageAllowedForChatType | method | — | — | — |
| ChatCooldown | method | Errors/PrintStacktraceAndThrow, Unit.Main/GetLevel, World/getConfig#4, WorldSession.Main/GetAccountMaxLevel, WorldSession.Main/GetLastPubChanMsgTime, WorldSession.Main/GetPlayer | — | — |
| HandleChatMessageOpcode | method | AbstractPlayer/GetLevel#2, AbstractPlayer/GetObjectGuid#2, AbstractPlayer/GetTeam#2, Anticheat/GetAnticheatLib, Anticheat/GetAntispam, AntispamInterface/addMessage, AntispamInterface/isMuted, AntispamInterface/normalizeMessage, Aura/GetModifier, ChannelMgr/channelMgr, ChannelMgr/GetChannel, ChatHandler.Chat/BuildChatPacket, ChatHandler.Chat/ChatHandler#3, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, Errors/PrintStacktraceAndThrow, game_Chat_Channel/HasFlag, game_Chat_Channel/IsLevelRestricted, game_Chat_Channel/Say, game_Group_Group/BroadcastPacket, game_Guild_Guild/BroadcastToGuild, game_Guild_Guild/BroadcastToOfficers, Group/GetId, Group/GetMemberGroup, Group/IsAssistant, Group/isBGGroup, Group/IsLeader, Group/isRaidGroup, GuildMgr/GetGuildById, Log.Main/Out, MasterPlayer.Chat/UpdateSpeakTime, MasterPlayer.Chat/Whisper, MasterPlayer.Main/AcceptsWhispersFrom, MasterPlayer.Main/GetGuildId, MasterPlayer.Main/GetLevel, MasterPlayer.Main/GetObjectGuid, MasterPlayer.Main/GetSession, MasterPlayer.Main/GetTeam, MasterPlayer.Main/IsGameMaster, Object/GetObjectGuid, ObjectAccessor/FindMasterPlayer#2, ObjectMgr/normalizePlayerName, Player.Main/GetChatTag, Player.Main/GetGroup, Player.Main/GetName, Player.Main/GetOriginalGroup, Player.Main/GetTeam, Player.Main/IsAFK, Player.Main/IsAllowedWhisperFrom, Player.Main/IsDND, Player.Main/IsEnabledWhisperRestriction, Player.Main/IsGameMaster, Player.Main/KnowsLanguage, Player.Main/Say, Player.Main/TextEmote, Player.Main/ToggleAFK, Player.Main/ToggleDND, Player.Main/Yell, shared_Util/isBasicLatinString, shared_Util/secsToTimeString, shared_Util/Utf8toWStr, Unit.Main/GetAurasByType, Unit.Main/GetLevel, Unit.Main/IsAlive, Unit.Main/IsInCombat, World/getConfig, World/getConfig#4, World/LogChat, WorldPacket/WorldPacket, WorldSession.Main/GetAccountId, WorldSession.Main/GetAccountMaxLevel, WorldSession.Main/GetMangosString, WorldSession.Main/GetMasterPlayer, WorldSession.Main/GetPlayer, WorldSession.Main/GetPlayerPointer, WorldSession.Main/GetSecurity, WorldSession.Main/HasTrialRestrictions, WorldSession.Main/SendNotification, WorldSession.Main/SendNotification#2, WorldSession.Main/SetLastPubChanMsgTime | — | — |
| HandleEmoteOpcode | method | Object/HasFlag, Player.Main/CanSpeak, shared_Util/secsToTimeString, SpellCaster/InterruptSpellsWithChannelFlags, Unit.Main/HandleEmoteCommand, Unit.Main/IsAlive, Unit.Main/RemoveAurasWithInterruptFlags, WorldSession.Main/GetMangosString, WorldSession.Main/GetPlayer, WorldSession.Main/SendNotification | — | — |
| EmoteChatBuilder | ctor | — | — | — |
| operator() | method | ByteBuffer/append#4, ByteBuffer/operator<<#10, ByteBuffer/operator<<#7, Object/GetObjectGuid, ObjectGuid/operator<<, WorldObject.Object/GetNameForLocaleIdx, WorldPacket/Initialize | — | — |
| HandleTextEmoteOpcode | method | Creature.Main/AI, CreatureAI/ReceiveEmote, Map.Main/GetUnit, Object/HasFlag, Object/IsCreature, Player.Main/CanSpeak, shared_Util/secsToTimeString, SpellCaster/InterruptSpellsWithChannelFlags, Unit.Main/HandleEmote, Unit.Main/IsAlive, Unit.Main/RemoveAurasWithInterruptFlags, World/getConfig#2, WorldObject.Object/GetMap, WorldSession.Main/GetMangosString, WorldSession.Main/GetPlayer, WorldSession.Main/SendNotification | — | — |
| HandleChatIgnoredOpcode | method | ChatHandler.Chat/BuildChatPacket, Object/GetObjectGuid, ObjectMgr/GetPlayer, Player.Main/GetName, Player.Main/GetSession, WorldPacket/WorldPacket, WorldSession.Main/SendPacket | — | — |
| SendPlayerNotFoundNotice | method | ByteBuffer/operator<<, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| SendWrongFactionNotice | method | WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| SendChatRestrictedNotice | method | WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |

---

<!-- verify: failed-members | invented: operator -->

---

<!-- verify: boundary-bleed | foreign: update, WorldSession -->
