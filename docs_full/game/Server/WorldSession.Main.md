<!-- provenance: boundary-bleed -->
# WorldSession.Main

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# WorldSession.Main

## Purpose & Responsibilities

`WorldSession` is the central abstraction representing a single authenticated connection between a client (or bot) and the WoW server. It manages the entire lifecycle of a user session, from initial authentication through gameplay to logout and cleanup.

Its primary responsibilities include:
1.  **Packet I/O:** Receiving raw binary packets from the `WorldSocket`, parsing them into structured `ClientPacket` objects, queuing them for processing based on thread safety requirements, and sending response packets back to the client.
2.  **State Management:** Tracking the session's current state (logged in, transferring, logging out, disconnected) to ensure packets are processed only when valid for that context.
3.  **Player Association:** Holding a pointer to the active `Player` object associated with the session. It coordinates the creation, loading, saving, and deletion of the `Player` entity.
4.  **Security & Anti-Cheat:** Integrating with the `Anticheat` system (Warden, MovementAnticheat) to detect cheating, enforce bans/kicks, and manage account flags.
5.  **Account Data Persistence:** Loading and saving persistent account-specific data (tutorials, global cache) from the database.
6.  **Bot Support:** Supporting non-human "bot" sessions that simulate players without a physical network socket, allowing AI-driven entities to interact with the world.

## Member-by-Member Behavior

### Session Lifecycle & Initialization

*   **`WorldSession` (ctor):** Initializes the session with a unique GUID, remote IP address, security level, and locale settings. It sets up internal timers and flags for connection state. If the socket is null (indicating a bot), it marks the remote IP as `<BOT>`.
*   **`~WorldSession` (dtor):** Cleans up resources. If a `Player` is still attached, it triggers `LogoutPlayer` to ensure proper cleanup. It finalizes the `WorldSocket`, removes the Warden session from the anticheat manager, and deletes the `MovementAnticheat` data structure.
*   **`SetDisconnectedSession`:** Marks the session as disconnected, stops packet sniffing, and notifies the `World` singleton that the session is no longer connected.
*   **`UpdateDisconnected`:** Handles the grace period for a disconnected player. If the disconnect timer expires, it returns false, signaling that the session object should be destroyed.
*   **`ForcePlayerLogoutDelay`:** Implements a delay mechanism for forced logouts (e.g., due to socket loss). It saves the player and keeps them in the world briefly to allow for reconnection or graceful cleanup.

### Packet Processing & Routing

*   **`MapSessionFilterHelper`:** A static helper function used by `MapSessionFilter` to determine if a packet can be processed in the map update thread. It checks if the player exists and is in the world.
*   **`Process`:** Method of `MapSessionFilter` (defined in this unit's scope via the filter class) that uses `MapSessionFilterHelper` to validate packet processing eligibility for the map thread.
*   **`QueueBinaryPacket`:** The entry point for incoming raw packets. It logs the packet if sniffing is enabled, checks for anticheat logging, parses the binary data into a `ClientPacket` using the opcode handler, verifies the parse integrity, and queues it.
*   **`QueuePacket`:** Routes the parsed `ClientPacket` into one of several `LockedQueue`s based on its processing strategy (World, Map, or Async). Chat packets are specially routed to async threads if possible.
*   **`ProcessPackets`:** The core loop that dequeues packets and executes their handlers. It respects the session's current state (e.g., rejecting logged-in packets if the player isn't loaded). It includes robust error handling for malformed packets, potentially kicking the player via `ProcessAnticheatAction`.
*   **`CanProcessPackets`:** Returns true if the session is capable of processing packets (socket open or bot active).
*   **`Update`:** Called periodically by the `World` update loop. It processes queued packets, checks for idle kicks, handles compressed movement packets, enforces play-time limits, and initiates logout procedures if necessary.
*   **`ClearIncomingPacketsByType`:** Empties a specific packet queue, used during state transitions to discard stale packets.

### Network Communication

*   **`SendPacket`:** Sends a `WorldPacket` to the client. It checks for oversized packets (rejecting them if > 32KB) and handles bot sessions by passing the packet to the bot's AI instead of the socket.
*   **`SendPacketImpl`:** The low-level implementation that writes to the `SniffFile` (if active) and sends the data via `WorldSocket`.
*   **`SendMovementPacket`:** Specialized sender for movement packets. For newer clients, it implements compression by batching multiple movement updates into a single packet if the count exceeds a configured threshold.
*   **`SendCompressedMovementPackets`:** Builds and sends the batched movement packet.
*   **`VerifyPacketWasCorrectlyRead`:** Debug helper to ensure the parser consumed the entire packet buffer.

### Player & Account Identity

*   **`GetPlayer` / `SetPlayer`:** Accessors for the active `Player` pointer.
*   **`GetPlayerName`:** Returns the name of the current player, or `<none>` if no player is loaded.
*   **`GetAccountId` / `GetUsername` / `SetUsername`:** Accessors for the account identity.
*   **`GetSecurity` / `SetSecurity`:** Accessors for the GM/security level.
*   **`GetRemoteAddress`:** Returns the client's IP address.
*   **`GetSocket`:** Returns the underlying `WorldSocket` pointer.
*   **`GetGUID`:** Returns the unique session ID. (Implemented in header only, declared here).

### Logout & Login Logic

*   **`LogoutPlayer`:** The complex routine for removing a player from the world. It handles combat states, battlegrounds, groups, guilds, pets, and inventory. It saves the player to the database, updates the `account` table's online status, and cleans up social links.
*   **`KickPlayer`:** Immediately closes the socket or marks the bot for removal.
*   **`PlayerLoading` / `PlayerLogout` / `PlayerLogoutWithSave`:** State flags indicating if the player is currently being loaded or logged out.
*   **`LogoutRequest`:** Initiates the logout countdown.
*   **`ShouldLogOut`:** Checks if the logout cooldown has expired.

### Anti-Cheat & Security

*   **`InitWarden`:** Initializes the Warden anticheat module for the session.
*   **`InitCheatData` / `GetCheatData`:** Manages the `MovementAnticheat` instance, initializing it when a player loads.
*   **`ProcessAnticheatAction`:** Executes penalties for detected cheating. It can mute, ban (account/IP), kick, or report to GMs. It updates the `account` table flags and logs the action.
*   **`HasTrialRestrictions`:** Determines if the account is restricted due to being unverified/trial.
*   **`CheckPlayedTimeLimit`:** Enforces play-time exhaustion rules, setting player flags and sending warnings.
*   **`AllowPacket`:** Implements flood protection by counting specific opcodes and triggering anticheat actions if thresholds are exceeded.

### Account Data & Tutorials

*   **`LoadGlobalAccountData` / `LoadAccountData`:** Fetches persistent account data from the `account_data` table.
*   **`SetAccountData`:** Saves account data to either `account_data` (global) or `character_account_data` (per-character) tables.
*   **`SendAccountDataTimes`:** Sends MD5 hashes of account data to the client to verify synchronization.
*   **`LoadTutorialsData` / `SaveTutorialsData` / `SetTutorialInt`:** Manages the 8 tutorial flags stored in the `character_tutorial` table.

### Utility & Helpers

*   **`GetMangosString`:** Retrieves localized strings from the object manager.
*   **`SendNotification`:** Sends system messages to the client.
*   **`SendAreaTriggerMessage`:** Sends messages triggered by area triggers.
*   **`StartSniffing` / `StopSniffing`:** Enables/disables packet logging to a file.
*   **`HasUsedClickToMove`:** Queries the Warden module for click-to-move usage.

## Cross-Unit Boundaries

*   **`WorldSocket`:** `WorldSession` holds a `shared_ptr` to `WorldSocket`. It calls `GetRemoteIpString`, `SendPacket`, `FinalizeSession`, `CloseSocket`, and `IsClosing`. The socket provides the raw network transport.
*   **`Player`:** `WorldSession` holds a raw pointer to `Player`. It calls numerous methods on `Player` for state management (e.g., `SaveToDB`, `RemoveFromWorld`, `GetName`). `Player` methods often call back into `WorldSession` to send packets (e.g., `SendPacket`, `SendSysMessage`).
*   **`World`:** `WorldSession` interacts with the `World` singleton for global configuration (`getConfig`), session management (`AddSession_`, `RemoveSession`, `SetSessionDisconnected`), and broadcasting (`SendGlobalMessage`).
*   **`Anticheat` / `Warden` / `MovementAnticheat`:** `WorldSession` delegates anticheat logic to these units. It creates Warden sessions, queries cheat data, and processes actions returned by the anticheat system.
*   **`ObjectMgr`:** Used for locale indexing (`GetIndexForLocale`) and string retrieval (`GetMangosString`).
*   **`Database`:** Directly queried for account data and tutorials. Uses `PQuery`, `PExecute`, and prepared statements.
*   **`ChatHandler` / `Command Handlers`:** Many command handlers call `WorldSession` methods to get player info, security levels, and to send responses.
*   **`BattleGroundMgr` / `Group` / `Guild`:** `LogoutPlayer` interacts with these managers to clean up the player's presence in groups, guilds, and battlegrounds.

## Data Model

`WorldSession` interacts with the following database tables:

*   **`account`**:
    *   Updated during logout (`current_realm`, `online`).
    *   Updated during anticheat actions (`flags` for muting).
    *   Read for account identity (though mostly cached in memory).
*   **`account_data`**:
    *   Stores global account data (e.g., macro lists, camera settings).
    *   Columns: `account` (PK), `type` (PK), `time`, `data`.
*   **`character_account_data`**:
    *   Stores character-specific account data.
    *   Columns: `guid` (PK), `type` (PK), `time`, `data`.
*   **`character_tutorial`**:
    *   Stores tutorial completion flags.
    *   Columns: `account` (PK), `tut0`-`tut7`.

## Notable Implementation Details

*   **Thread Safety:** Packet processing is split into three queues (`PACKET_PROCESS_WORLD`, `PACKET_PROCESS_MAP`, `PACKET_PROCESS_ASYNC`). `ProcessPackets` is called with a `PacketFilter` that dictates which queue to drain. This allows map-related packets to be processed on the map thread, reducing contention on the main world thread.
*   **Bot Support:** The code extensively checks for `m_bot`. If a bot is present, network operations (like `SendPacket`) are redirected to the bot's AI, and socket-related checks are bypassed. This allows headless players to function identically to human players in the game logic.
*   **Movement Compression:** For clients newer than 1.7.1, `SendMovementPacket` batches movement updates. It uses a `MovementData` compressor to combine multiple small movement packets into one larger packet if the frequency exceeds a threshold, reducing network overhead.
*   **Logout Complexity:** `LogoutPlayer` is a critical and complex function. It must handle edge cases like players dying during logout, being in combat, or having pending teleports. It ensures the player is properly removed from all social and game structures before deleting the `Player` object.
*   **Flood Protection:** `AllowPacket` tracks counts of specific opcodes. If a client sends too many "slow" or "very slow" opcodes (like character creation or mail sending) in a short period, the session is flagged for anticheat action (kick/ban).
*   **State Machine:** The session has implicit states (Authed, LoggedIn, Transferring, LoggingOut). `ProcessPackets` checks these states before executing opcode handlers to prevent invalid operations (e.g., processing a chat message before the player is loaded).

## Member Reference

**MapSessionFilterHelper**
Static helper function used by `MapSessionFilter` to determine if a packet can be processed in the map update thread. It checks if the player exists and is in the world.

**Process**
Method of `MapSessionFilter` (defined in this unit's scope via the filter class) that uses `MapSessionFilterHelper` to validate packet processing eligibility for the map thread.

**WorldSession**
Constructor that initializes the session with ID, socket, security, mute time, and locale. Sets up internal state variables and remote IP.

**~WorldSession**
Destructor that cleans up the player (via `LogoutPlayer`), finalizes the socket, removes Warden session, and deletes anticheat data.

**GetPlayerName**
Returns the name of the current player, or `<none>` if no player is loaded. Delegates to `Player::GetName`.

**SendPacket**
Sends a `WorldPacket` to the client. Checks for oversized packets and redirects to bot AI if applicable. Calls `SendPacketImpl`.

**SendPacketImpl**
Low-level implementation that writes to sniff file and sends via `WorldSocket`.

**VerifyPacketWasCorrectlyRead**
Debug helper that logs errors if the parsed packet size doesn't match the original buffer size.

**SendMovementPacket**
Sends movement packets. For newer clients, it batches packets using `MovementData` compression if the count exceeds a threshold.

**GetGUID**
Returns the unique session GUID. (Inline getter defined in header).

**GetSecurity**
Returns the account's security level. (Inline getter defined in header).

**GetAccountId**
Returns the account ID. (Inline getter defined in header).

**GetUsername**
Returns the account username. (Inline getter defined in header).

**SetUsername**
Sets the account username. (Inline setter defined in header).

**GetLatency**
Returns the client's latency. (Inline getter defined in header).

**SetLatency**
Sets the client's latency. (Inline setter defined in header).

**GetGameBuild**
Returns the client's game build number. (Inline getter defined in header).

**SetGameBuild**
Sets the client's game build number. (Inline setter defined in header).

**GetOS**
Returns the client's OS type. (Inline getter defined in header).

**SetOS**
Sets the client's client's OS type. (Inline setter defined in header).

**GetPlatform**
Returns the client's platform type. (Inline getter defined in header).

**SetPlatform**
Sets the client's platform type. (Inline setter defined in header).

**SendCompressedMovementPackets**
Builds and sends the batched movement packet from the compressor.

**GetAccountMaxLevel**
Returns the maximum level of characters on the account. (Inline getter defined in header).

**SetAccountFlags**
Sets the account flags. (Inline setter defined in header).

**GetAccountFlags**
Returns the account flags. (Inline getter defined in header).

**SetVerifiedEmail**
Sets whether the account has a verified email. (Inline setter defined in header).

**HasVerifiedEmail**
Returns whether the account has a verified email. (Inline getter defined in header).

**GetPlayer**
Returns the pointer to the active `Player`. (Inline getter defined in header).

**SetSecurity**
Sets the account's security level. (Inline setter defined in header).

**GetRemoteAddress**
Returns the client's remote IP address. (Inline getter defined in header).

**SetPlayer**
Sets the pointer to the active `Player`. (Inline setter defined in header).

**SetMasterPlayer**
Sets the pointer to the `MasterPlayer` (for clustering). (Inline setter defined in header).

**GetSocket**
Returns the `WorldSocket` pointer. (Inline getter defined in header).

**GetChatPacketProcessingType**
Static function that determines the processing queue for chat packets based on chat type.

**SetInQueue**
Sets whether the session is in the authentication queue. (Inline setter defined in header).

**IsConnected**
Returns whether the session is connected. (Inline getter defined in header).

**KickDisconnectedFromWorld**
Resets the disconnect timer, effectively keeping the session alive despite disconnection. (Inline setter defined in header).

**PlayerLoading**
Returns whether the player is currently being loaded. (Inline getter defined in header).

**PlayerLogout**
Returns whether the player is currently logging out. (Inline getter defined in header).

**PlayerLogoutWithSave**
Returns whether the player is logging out with a save. (Inline getter defined in header).

**GetCreateTime**
Returns the time the session was created. (Inline getter defined in header).

**GetConsecutivePlayTime**
Calculates the total consecutive play time including previous sessions. (Inline getter defined in header).

**GetPreviousPlayedTime**
Returns the play time from the previous session. (Inline getter defined in header).

**SetPreviousPlayedTime**
Sets the play time from the previous session. (Inline setter defined in header).

**IsLogingOut**
Returns whether the user is in the logout process. (Inline getter defined in header).

**LogoutRequest**
Initiates the logout countdown. (Inline method defined in header).

**QueuePacket**
Routes a parsed `ClientPacket` into the appropriate processing queue.

**ShouldLogOut**
Checks if the logout cooldown has expired. (Inline method defined in header).

**GetSessionDbcLocale**
Returns the session's DBC locale. (Inline getter defined in header).

**GetSessionDbLocaleIndex**
Returns the session's DB locale index. (Inline getter defined in header).

**GetLastPubChanMsgTime**
Returns the timestamp of the last public channel message. (Inline getter defined in header).

**SetLastPubChanMsgTime**
Sets the timestamp of the last public channel message. (Inline setter defined in header).

**QueueBinaryPacket**
Entry point for incoming raw packets. Parses and queues them.

**GetBot**
Returns the bot entry pointer. (Inline getter defined in header).

**SetBot**
Sets the bot entry pointer. (Inline setter defined in header).

**SetSessionKey**
Sets the session key for Warden. (Inline setter defined in header).

**GetWarden**
Returns the Warden pointer. (Inline getter defined in header).

**GetFingerprint**
Placeholder for fingerprinting (returns 0). (Inline method defined in header).

**CleanupFingerprintHistory**
Placeholder for fingerprint cleanup. (Inline method defined in header).

**GetClientMoverGuid**
Returns the GUID of the client-controlled mover. (Inline getter defined in header).

**HasClientMovementControl**
Returns whether the client controls movement. (Inline getter defined in header).

**SetReceivedWhoRequest**
Sets the flag for received WHO request. (Inline setter defined in header).

**ReceivedWhoRequest**
Returns the flag for received WHO request. (Inline getter defined in header).

**SetReceivedAHListRequest**
Sets the flag for received Auction House list request. (Inline setter defined in header).

**ReceivedAHListRequest**
Returns the flag for received Auction House list request. (Inline getter defined in header).

**LogUnexpectedOpcode**
Logs an unexpected opcode with a reason.

**HasRecentPacket**
Returns whether a packet of a specific type was recently received. (Inline getter defined in header).

**HasTrialRestrictions**
Determines if the account has trial restrictions.

**StartSniffing**
Starts packet logging to a file. (Inline method defined in header).

**CheckPlayedTimeLimit**
Enforces play-time exhaustion rules.

**StopSniffing**
Stops packet logging. (Inline method defined in header).

**SendTrainerList**
Declaration for sending trainer lists (implemented elsewhere).

**SendPlayTimeWarning**
Sends a play-time warning packet to the client.

**ForcePlayerLogoutDelay**
Implements a delay for forced logouts.

**GetAccountData**
Returns a pointer to specific account data. (Inline getter defined in header).

**SetTutorialInt**
Sets a tutorial flag and marks data as changed. (Inline method defined in header).

**Update**
Main update loop for the session. Processes packets, checks idle, handles compression, and manages logout.

**CanProcessPackets**
Checks if the session can process packets.

**ProcessPackets**
Dequeues and executes packet handlers.

**HandlePingOpcode**
Declaration for ping handler.

**HandleAuthSessionOpcode**
Declaration for auth session handler.

**ClearIncomingPacketsByType**
Clears a specific packet queue.

**SetDisconnectedSession**
Marks the session as disconnected.

**UpdateDisconnected**
Handles the disconnect grace period.

**LogoutPlayer**
Complex routine for removing a player from the world, saving data, and cleaning up associations.

**KickPlayer**
Immediately closes the socket or marks bot for removal.

**SendAreaTriggerMessage**
Sends an area trigger message to the client.

**SendNotification**
Sends a system notification to the client.

**SendNotification#2**
Overload of `SendNotification` that takes a string ID.

**GetMasterPlayer**
Returns the `MasterPlayer` pointer. (Inline getter defined in header).

**GetMangosString**
Retrieves a localized string from the object manager.

**GetPlayerPointer**
Returns a `PlayerPointer` wrapper for the active player or master player. (Inline method defined in header).

**Handle_NULL**
Handler for unimplemented opcodes.

**Handle_EarlyProccess**
Handler for opcodes that must be processed earlier.

**Handle_ServerSide**
Handler for server-side only opcodes.

**SendAuthWaitQue**
Sends the authentication wait queue response.

**LoadGlobalAccountData**
Loads global account data from the database.

**LoadAccountData**
Parses and stores account data from a query result.

**SetAccountData**
Saves account data to the database.

**SendAccountDataTimes**
Sends MD5 hashes of account data to the client.

**LoadTutorialsData**
Loads tutorial flags from the database.

**SendTutorialsData**
Sends tutorial flags to the client.

**SaveTutorialsData**
Saves tutorial flags to the database.

**GetTutorialInt**
Returns a specific tutorial flag.

**ExecuteOpcode**
Executes the handler for a parsed packet.

**InitWarden**
Initializes the Warden anticheat module.

**InitCheatData**
Initializes the movement anticheat data.

**GetCheatData**
Returns or creates the movement anticheat data.

**ProcessAnticheatAction**
Executes penalties for cheating (mute, ban, kick, report).

**HasUsedClickToMove**
Queries Warden for click-to-move usage.

**AllowPacket**
Implements flood protection by counting opcodes.

**CharacterScreenIdleKick**
Kicks sessions idle on the character screen.

---

<!-- machine-true, projected from graph.json -->

## Map — WorldSession.Main

*Source:* WorldSession.cpp, WorldSession.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| MapSessionFilterHelper | function | Object/IsInWorld | — | — |
| Process | method | Opcodes/LookupOpcodeHandler, Packet/GetOpcode | — | — |
| WorldSession | ctor | ObjectMgr/GetIndexForLocale, shared_Util/getMSTime, World/GetAvailableDbcLocale, WorldSocket/GetRemoteIpString | ChatHandler.PlayerBotMgr/AddBot#2, WorldSocket/_HandleAuthSession | — |
| ~WorldSession | dtor | Anticheat/GetAnticheatLib, Anticheat/RemoveWardenSession, PlayerBotMgr/IsSavingAllowed, WorldSocket/FinalizeSession | — | — |
| GetPlayerName | method | Player.Main/GetName | ChatHandler.AccountCommands/HandleAddCharacterNoteCommand, ChatHandler.AccountCommands/HandleBanAllIPCommand, ChatHandler.AccountCommands/HandleBanHelper, ChatHandler.AccountCommands/HandleMuteCommand, ChatHandler.AccountCommands/HandleUnBanHelper, ChatHandler.AccountCommands/HandleWarnCharacterCommand, ChatHandler.DebugCommands/HandleDebugSendChatMsgCommand, ChatHandler.TicketCommands/HandleGMTicketAssignToCommand, HonorMgr/Add, World/BanAccount#2, WorldSession.AuctionHouseHandler/HandleAuctionSellItem, WorldSession.CharacterHandler/HandlePlayerLogin, WorldSession.MailHandler/HandleMailTakeItem, WorldSession.MailHandler/HandleSendMailCallback | — |
| SendPacket | method | ByteBuffer/size, Log.Main/Out, Opcodes/LookupOpcodeName, PlayerBotAI/OnPacketReceived, WorldPacket/GetOpcode | AiBotAI.Bridge/BridgeHandleSayText, BattleGroundMgr/Execute, BattleGroundMgr/Execute#2, BattleGroundMgr/InviteGroupToBG, BattleGroundMgr/PlayerLoggedIn, BattleGroundMgr/RemoveOfflinePlayer, ChannelMgr/GetChannel, ChatHandler.CharacterCommands/HandleLearnAllMyTaxisCommand, ChatHandler.Chat/SendSysMessage, ChatHandler.DebugCommands/HandleDebugPvPCreditCommand, ChatHandler.DebugCommands/HandleDebugSendChannelNotifyCommand, ChatHandler.DebugCommands/HandleDebugSendChatMsgCommand, ChatHandler.DebugCommands/HandleDebugSendOpcodeCommand, ChatHandler.DebugCommands/HandleDebugSendSpellFailCommand, ChatHandler.DebugCommands/HandleDebugSpellModsCommand, ChatHandler.TicketCommands/HandleGMTicketCloseByIdCommand, ChatHandler.TicketCommands/HandleGMTicketDeleteByIdCommand, GameObject/Update, GameObject/Use, game_Battlegrounds_BattleGround/EndBattleGround, game_Battlegrounds_BattleGround/PlayerAddedToBGCheckIfBGIsRunning, game_Battlegrounds_BattleGround/PlaySoundToTeam, game_Battlegrounds_BattleGround/RemovePlayerAtLeave, game_Battlegrounds_BattleGround/SendPacketToAll, game_Battlegrounds_BattleGround/SendPacketToTeam, game_Battlegrounds_BattleGround/UpdateWorldStateForPlayer, game_Chat_Channel/Say, game_Chat_Channel/SendToAll, game_Chat_Channel/SendToOne, game_Group_Group/BroadcastPacket, game_Group_Group/BroadcastReadyCheck, game_Group_Group/Disband, game_Group_Group/MasterLoot, game_Group_Group/RemoveMember, game_Group_Group/SendLootAllPassed, game_Group_Group/SendLootRoll, game_Group_Group/SendLootRollWon, game_Group_Group/SendLootStartRoll, game_Group_Group/SendLootStartRollsForPlayer, game_Group_Group/SendTargetIconList, game_Group_Group/SendUpdate, game_Group_Group/UpdatePlayerOutOfRange, game_Guild_Guild/BroadcastPacket, game_Guild_Guild/BroadcastPacketToRank, game_Guild_Guild/BroadcastToGuild, game_Guild_Guild/BroadcastToOfficers, game_Guild_Guild/Query, game_Guild_Guild/Roster, game_Objects_Item/SendTimeUpdate, GMTicketMgr/SendResponse, GMTicketMgr/SendTicket, GossipDef/CloseGossip, GossipDef/SendGossipMenu, GossipDef/SendPointOfInterest, GossipDef/SendPointOfInterest#2, GossipDef/SendQuestGiverOfferReward, GossipDef/SendQuestGiverQuestDetails, GossipDef/SendQuestGiverQuestList, GossipDef/SendQuestGiverRequestItems, GossipDef/SendQuestGiverStatus, GossipDef/SendTalking, GossipDef/SendTalking#2, GridNotifiers/Visit, GridNotifiers/Visit#2, GridNotifiers/Visit#3, GridNotifiers/Visit#4, GridNotifiers/Visit#5, LFGQueue/RemovePlayerFromQueue, LFGQueue/Update, Log.Warden/SendPacket, Log.Warden/SendPacketDirect, Map.Main/BindPlayerOrGroupOnEnter, Map.Main/CrashUnload, Map.Main/PermBindAllPlayers, Map.Main/RemoveCorpses, Map.Main/SendDefenseMessage, Map.Main/SendToPlayers, Map.Main/SendToPlayersInZone, MasterPlayer.Chat/Whisper, MasterPlayer.Main/SendInitialActionButtons, MovementPacketSender/SendKnockBackToController, MovementPacketSender/SendMovementFlagChangeToController, MovementPacketSender/SendSpeedChangeToController, MovementPacketSender/SendTeleportToController, Pet.Main/ModifyLoyalty, Pet.Main/SetEnabled, Pet.Main/_LoadSpellCooldowns, Player.Main/ActivateTaxiPathTo, Player.Main/AddSpell, Player.Main/ApplyEquipCooldown, Player.Main/BuyItemFromVendor, Player.Main/CharmSpellInitialize, Player.Main/CheckDuelDistance, Player.Main/DuelComplete, Player.Main/EnvironmentalDamage, Player.Main/ExecuteTeleportFar, Player.Main/GiveLevel, Player.Main/LearnSpell, Player.Main/LockOutSpells, Player.Main/PetSpellInitialize, Player.Main/PossessSpellInitialize, Player.Main/RemovedInsignia, Player.Main/RemoveSpell, Player.Main/SatisfyQuestLog, Player.Main/SendAttackSwingBadFacingAttack, Player.Main/SendAttackSwingCancelAttack, Player.Main/SendAttackSwingCantAttack, Player.Main/SendAttackSwingDeadTarget, Player.Main/SendAttackSwingNotInRange, Player.Main/SendAttackSwingNotStanding, Player.Main/SendAutoRepeatCancel, Player.Main/SendBuyError, Player.Main/SendCanTakeQuestResponse, Player.Main/SendCorpseReclaimDelay, Player.Main/SendDestroyGroupMembers, Player.Main/SendDirectMessage, Player.Main/SendDismountResult, Player.Main/SendDuelCountdown, Player.Main/SendEnchantmentLog, Player.Main/SendEquipError, Player.Main/SendExplorationExperience, Player.Main/SendFactionAtWar, Player.Main/SendFeignDeathResisted, Player.Main/SendInitialPacketsBeforeAddToMap, Player.Main/SendInitialSpells, Player.Main/SendInitWorldStates, Player.Main/SendInstanceResetWarning, Player.Main/SendLogXPGain, Player.Main/SendLootMoneyNotify, Player.Main/SendMessageToSet, Player.Main/SendMessageToSetInRange, Player.Main/SendMessageToSetInRange#2, Player.Main/SendMirrorTimerPause, Player.Main/SendMirrorTimerStart, Player.Main/SendMirrorTimerStop, Player.Main/SendMountResult, Player.Main/SendNewItem, Player.Main/SendNewWorld, Player.Main/SendNotifyLootItemRemoved, Player.Main/SendNotifyLootMoneyRemoved, Player.Main/SendOpenContainer, Player.Main/SendPetSkillWipeConfirm, Player.Main/SendPetTameFailure, Player.Main/SendProficiency, Player.Main/SendPushToPartyResponse, Player.Main/SendQuestCompleteEvent, Player.Main/SendQuestConfirmAccept, Player.Main/SendQuestFailed, Player.Main/SendQuestFailedAtTaker, Player.Main/SendQuestReward, Player.Main/SendQuestTimerFailed, Player.Main/SendQuestUpdateAddCreatureOrGo, Player.Main/SendQuestUpdateAddItem, Player.Main/SendRaidGroupOnlyError, Player.Main/SendRaidInfo, Player.Main/SendResetInstanceFailed, Player.Main/SendResetInstanceSuccess, Player.Main/SendSavedInstances, Player.Main/SendSellError, Player.Main/SendSpellCooldown, Player.Main/SendSpellRemoved, Player.Main/SendSummonRequest, Player.Main/SendSysMessage, Player.Main/SendTalentWipeConfirm, Player.Main/SendTransferAborted, Player.Main/SendUpdateWorldState, Player.Main/SetBindPoint, Player.Main/SetClientControl, Player.Main/SwitchInstance, Player.Main/TaxiStepFinished, SocialMgr/BroadcastToFriendListers, SocialMgr/SendFriendList, SocialMgr/SendFriendStatus, SocialMgr/SendIgnoreList, Spell.Effects/EffectDuel, Spell.Effects/EffectDummy, Spell.Main/SendCastResult#2, Spell.Main/SendResurrectRequest, Unit.Main/Kill, Unit.Main/SendPetActionFeedback, Unit.Main/SendPetAIReaction, Unit.Main/SendPetCastFail, Unit.Main/SendPetTalk, Unit.Main/SetStandState, UpdateData/Send, WardenMac/SetCharEnumPacket, WardenMac/Update, WardenWin/SetCharEnumPacket, WardenWin/Update, Weather/SendWeatherUpdateToPlayer, World/AddQueuedSession, World/AddSession_, World/operator()#3, World/SendGlobalMessage, World/SendServerMessage, World/SendZoneMessage, WorldObject.Object/DestroyForPlayer, WorldObject.Object/MonsterWhisper, WorldObject.Object/MonsterWhisper#2, WorldObject.Object/Visit, WorldSession.AuctionHouseHandler/operator(), WorldSession.AuctionHouseHandler/SendAuctionBidderNotification, WorldSession.AuctionHouseHandler/SendAuctionCommandResult, WorldSession.AuctionHouseHandler/SendAuctionHello, WorldSession.AuctionHouseHandler/SendAuctionOwnerNotification, WorldSession.AuctionHouseHandler/SendAuctionRemovedNotification, WorldSession.BattleGroundHandler/HandleBattlefieldListOpcode, WorldSession.BattleGroundHandler/HandleBattleFieldPortOpcode, WorldSession.BattleGroundHandler/HandleBattlefieldStatusOpcode, WorldSession.BattleGroundHandler/HandleBattleGroundPlayerPositionsOpcode, WorldSession.BattleGroundHandler/HandlePVPLogDataOpcode, WorldSession.BattleGroundHandler/RequestBgJoinQueue, WorldSession.BattleGroundHandler/SendBattleGroundJoinError, WorldSession.BattleGroundHandler/SendBattleGroundList, WorldSession.ChannelHandler/HandleJoinChannelOpcode, WorldSession.CharacterHandler/HandleChangePlayerNameOpcodeCallBack, WorldSession.CharacterHandler/HandleCharCreateOpcode, WorldSession.CharacterHandler/HandleCharDeleteOpcode, WorldSession.CharacterHandler/HandleCharEnum, WorldSession.CharacterHandler/HandleCharRenameOpcode, WorldSession.CharacterHandler/HandlePlayerLogin, WorldSession.CharacterHandler/HandlePlayerLoginOpcode, WorldSession.ChatHandler/HandleChatIgnoredOpcode, WorldSession.ChatHandler/SendChatRestrictedNotice, WorldSession.ChatHandler/SendPlayerNotFoundNotice, WorldSession.ChatHandler/SendWrongFactionNotice, WorldSession.CombatHandler/SendAttackStop, WorldSession.GMTicketHandler/HandleGMTicketCreateOpcode, WorldSession.GMTicketHandler/HandleGMTicketDeleteTicketOpcode, WorldSession.GMTicketHandler/HandleGMTicketSystemStatusOpcode, WorldSession.GMTicketHandler/HandleGMTicketUpdateTextOpcode, WorldSession.GroupHandler/HandleGroupDeclineOpcode, WorldSession.GroupHandler/HandleGroupInviteOpcode, WorldSession.GroupHandler/HandleRaidReadyCheckOpcode, WorldSession.GroupHandler/HandleRandomRollOpcode, WorldSession.GroupHandler/HandleRequestPartyMemberStatsOpcode, WorldSession.GroupHandler/SendPartyResult, WorldSession.GuildHandler/HandleGuildDeclineOpcode, WorldSession.GuildHandler/HandleGuildInfoOpcode, WorldSession.GuildHandler/HandleGuildInviteOpcode, WorldSession.GuildHandler/SendGuildCommandResult, WorldSession.GuildHandler/SendSaveGuildEmblem, WorldSession.ItemHandler/HandleBuyBankSlotOpcode, WorldSession.ItemHandler/HandleItemNameQueryOpcode, WorldSession.ItemHandler/HandleItemQuerySingleOpcode, WorldSession.ItemHandler/HandleReadItemOpcode, WorldSession.ItemHandler/SendItemEnchantTimeUpdate, WorldSession.ItemHandler/SendListInventory, WorldSession.LFGHandler/SendMeetingstoneFailed, WorldSession.LFGHandler/SendMeetingstoneSetqueue, WorldSession.MailHandler/HandleGetMailList, WorldSession.MailHandler/HandleItemTextQuery, WorldSession.MailHandler/HandleQueryNextMailTime, WorldSession.MailHandler/SendMailResult, WorldSession.MailHandler/SendNewMail, WorldSession.MiscHandler/HandleInspectHonorStatsOpcode, WorldSession.MiscHandler/HandleInspectOpcode, WorldSession.MiscHandler/HandleLFGOpcode, WorldSession.MiscHandler/HandleLogoutCancelOpcode, WorldSession.MiscHandler/HandleLogoutRequestOpcode, WorldSession.MiscHandler/HandlePlayedTime, WorldSession.MiscHandler/HandleRequestAccountData, WorldSession.MiscHandler/HandleWhoisOpcode, WorldSession.MiscHandler/HandleZoneUpdateOpcode, WorldSession.MiscHandler/operator(), WorldSession.NPCHandler/SendShowBank, WorldSession.NPCHandler/SendStablePet, WorldSession.NPCHandler/SendStableResult, WorldSession.NPCHandler/SendTabardVendorActivate, WorldSession.NPCHandler/SendTrainerList, WorldSession.NPCHandler/SendTrainingFailure, WorldSession.NPCHandler/SendTrainingSuccess, WorldSession.PetHandler/SendPetNameInvalid, WorldSession.PetHandler/SendPetNameQuery, WorldSession.PetitionsHandler/HandleOfferPetitionOpcode, WorldSession.PetitionsHandler/HandlePetitionDeclineOpcode, WorldSession.PetitionsHandler/HandlePetitionQueryOpcode, WorldSession.PetitionsHandler/HandlePetitionRenameOpcode, WorldSession.PetitionsHandler/HandlePetitionShowSignOpcode, WorldSession.PetitionsHandler/HandlePetitionSignOpcode, WorldSession.PetitionsHandler/HandleTurnInPetitionOpcode, WorldSession.PetitionsHandler/SendPetitionShowList, WorldSession.QueryHandler/HandleCorpseQueryOpcode, WorldSession.QueryHandler/HandleCreatureQueryOpcode, WorldSession.QueryHandler/HandleGameObjectQueryOpcode, WorldSession.QueryHandler/HandleNpcTextQueryOpcode, WorldSession.QueryHandler/HandlePageTextQueryOpcode, WorldSession.QueryHandler/SendNameQueryOpcode, WorldSession.QueryHandler/SendNameQueryOpcodeFromDB, WorldSession.QueryHandler/SendNameQueryOpcodeFromDBCallBack, WorldSession.QueryHandler/SendQueryTimeResponse, WorldSession.QuestHandler/HandleQuestPushResult, WorldSession.QuestHandler/HandleQuestQueryOpcode, WorldSession.SkillHandler/HandleTalentWipeConfirmOpcode, WorldSession.SpellHandler/HandleCastSpellOpcode, WorldSession.TaxiHandler/SendLearnNewTaxiNode, WorldSession.TaxiHandler/SendTaxiMenu, WorldSession.TaxiHandler/SendTaxiStatus, WorldSession.TradeHandler/HandleInitiateTradeOpcode, WorldSession.TradeHandler/SendTradeStatus, WorldSession.TradeHandler/SendUpdateTrade, ZoneScript/BroadcastPacket | — |
| SendPacketImpl | method | SniffFile/WritePacket#2, WorldPacket/WorldPacket#3, WorldSocket/SendPacket | — | — |
| VerifyPacketWasCorrectlyRead | method | ByteBuffer/rpos, ByteBuffer/size, Log.Main/Out, Opcodes/LookupOpcodeName, Packet/GetOpcode, WorldPacket/GetOpcode | — | — |
| SendMovementPacket | method | ByteBuffer/size, Log.Main/Out, Opcodes/LookupOpcodeName, PlayerBotAI/OnPacketReceived, UpdateData/AddPacket, UpdateData/CanAddPacket, World/getConfig#4, WorldPacket/GetOpcode | WorldObject.Object/Visit#2 | — |
| GetGUID | method | — | ChatHandler.Chat/ParseCommands, Log.Warden/KickSession, Log.Warden/SendPacket, Log.Warden/Warden, MovementAnticheat/CheckFallReset, MovementAnticheat/CheckFallStop, MovementAnticheat/CheckMoveStart, MovementAnticheat/CheckNoFallTime, MovementAnticheat/CheckTimeDesync, WardenMac/SetCharEnumPacket, WardenMac/Update, WardenWin/SetCharEnumPacket, WardenWin/Update, WorldSession.MovementHandler/HandleMoverRelocation | — |
| GetSecurity | method | — | Anticheat/CreateWardenForInternal, AsyncCommandHandlers/HandleAccountInfoResult, AsyncCommandHandlers/operator()#2, AsyncCommandHandlers/ShowAccountListHelper, AuctionHouseMgr/SendAuctionWonMail, ChatHandler.CharacterCommands/HandleCleanCharactersItemsCommand, ChatHandler.CharacterCommands/HandleModifyXpRateCommand, ChatHandler.Chat/GetAccessLevel, ChatHandler.Chat/HasLowerSecurityAccount, ChatHandler.Chat/ParseCommands, ChatHandler.DebugCommands/HandleDebugChatFreezeCommand, ChatHandler.DebugCommands/HandleDebugPlayMusicCommand, ChatHandler.MiscCommands/HandleGMListIngameCommand, ChatHandler.PlayerBotMgr/HandlePartyBotAddCommand, ChatHandler.PlayerBotMgr/PartyBotAddRequirementCheck, ChatHandler.TicketCommands/HandleGMTicketEscalateCommand, ChatHandler.TicketCommands/HandleGMTicketUnAssignCommand, game_Chat_Channel/Announce, game_Chat_Channel/Join, game_Chat_Channel/KickOrBan, game_Chat_Channel/Leave, game_Chat_Channel/List, game_Chat_Channel/Moderate, game_Chat_Channel/Password, game_Chat_Channel/Say, game_Chat_Channel/SetMode, game_Chat_Channel/SetOwner, game_Chat_Channel/SetOwner#2, game_Chat_Channel/UnBan, MasterPlayer.Chat/UpdateSpeakTime, MasterPlayer.Main/IsVisibleGloballyFor, MovementAnticheat/HandleFlagTests, MovementAnticheat/HandlePositionTests, MovementAnticheat/OnExplore, MovementAnticheat/OnFailedToAckChange, MovementAnticheat/OnUnreachable, MovementAnticheat/OnWrongAckData, Player.Main/Create, Player.Main/IsVisibleGloballyFor, Player.Main/IsVisibleInGridForPlayer, Player.Main/LoadFromDB, Player.Main/LogModifyMoney, Player.Main/Player#5, Player.Main/SaveNewPlayer, Player.Main/UpdateFreeTalentPoints, SocialMgr/BroadcastToFriendListers, SocialMgr/GetFriendInfo, Spell.Effects/EffectEnchantItemPerm, Spell.Effects/EffectEnchantItemTmp, Unit.Main/IsVisibleForOrDetect, World/CanSkipQueue, World/KickAllLess, World/SendGMText, World/SendGMTicketText, World/SendGMTicketText#2, World/SendWorldTextToBGAndQueue, WorldSession.AuctionHouseHandler/HandleAuctionPlaceBid, WorldSession.AuctionHouseHandler/HandleAuctionSellItem, WorldSession.ChannelHandler/HandleJoinChannelOpcode, WorldSession.ChannelHandler/HandleLeaveChannelOpcode, WorldSession.CharacterHandler/HandleCharCreateOpcode, WorldSession.CharacterHandler/HandleCharRenameOpcode, WorldSession.CharacterHandler/HandlePlayerLoginOpcode, WorldSession.ChatHandler/HandleChatMessageOpcode, WorldSession.ItemHandler/HandleItemQuerySingleOpcode, WorldSession.MailHandler/HandleMailTakeItem, WorldSession.MailHandler/HandleSendMail, WorldSession.MailHandler/HandleSendMailCallback, WorldSession.MailHandler/HandleSendMailRequest, WorldSession.MiscHandler/HandleAddFriendOpcode, WorldSession.MiscHandler/HandleLogoutRequestOpcode, WorldSession.MiscHandler/HandleMoveSetRawPosition, WorldSession.MiscHandler/HandleWhoisOpcode, WorldSession.MiscHandler/HandleWorldTeleportOpcode, WorldSession.MiscHandler/operator(), WorldSession.TradeHandler/HandleAcceptTradeOpcode, WorldSession.TradeHandler/MoveItems, WorldSocket/_HandlePing | — |
| GetAccountId | method | — | AccountMgr/CountWhispersTo, AsyncCommandHandlers/HandlePInfoCommand, AuctionHouseMgr/BuildListOwnerItems, AuctionHouseMgr/SendAuctionWonMail, BattleGroundMgr/AddGroup, ChatHandler.AccountCommands/HandleBanInfoCharacterCommand, ChatHandler.AccountCommands/HandleMuteCommand, ChatHandler.AccountCommands/HandleSpamerMute, ChatHandler.AccountCommands/HandleSpamerUnmute, ChatHandler.AccountCommands/HandleUnmuteCommand, ChatHandler.CharacterCommands/HandleCharacterEraseCommand, ChatHandler.Chat/ExtractAccountId, ChatHandler.Chat/GetAccountId, ChatHandler.Chat/isAvailable, ChatHandler.LookupCommands/LookupPlayerSearchCommand, ChatHandler.PlayerBotMgr/ForceAccountConnection, game_Guild_Guild/AddMember, go_scripts/GOHello_go_silithyste, GuildMgr/GetSignatureForPlayer, GuildMgr/PetitionSignature, HonorMgr/Add, Log.Warden/Warden, ObjectMgr/GetPlayerAccountIdByGUID, ObjectMgr/InsertPlayerInCache, ObjectMgr/UpdatePlayerCache, OutdoorPvPSI/HandleAreaTrigger, OutdoorPvPSI/HandleDropFlag, Player.Main/AddInstanceEnterTime, Player.Main/CheckInstanceCount, Player.Main/GetShortDescription, Player.Main/LeaveBattleground, Player.Main/LoadFromDB, Player.Main/Player, Player.Main/Player#2, Player.Main/SaveNewPlayer, Player.Main/SaveToDB, Spell.Effects/EffectEnchantItemPerm, Spell.Effects/EffectEnchantItemTmp, World/AddSessionToSessionsMap, World/AddSession_, World/CanSkipQueue, World/SetSessionDisconnected, World/UpdateSessions, WorldSession.AuctionHouseHandler/HandleAuctionListBidderItems, WorldSession.AuctionHouseHandler/HandleAuctionListItems, WorldSession.AuctionHouseHandler/HandleAuctionListOwnerItems, WorldSession.AuctionHouseHandler/HandleAuctionPlaceBid, WorldSession.AuctionHouseHandler/HandleAuctionSellItem, WorldSession.BattleGroundHandler/HandleBattleFieldPortOpcode, WorldSession.CharacterHandler/HandleChangePlayerNameOpcodeCallBack, WorldSession.CharacterHandler/HandleCharCreateOpcode, WorldSession.CharacterHandler/HandleCharDeleteOpcode, WorldSession.CharacterHandler/HandleCharEnum, WorldSession.CharacterHandler/HandleCharEnumOpcode, WorldSession.CharacterHandler/HandleCharRenameOpcode, WorldSession.CharacterHandler/HandlePlayerLogin, WorldSession.CharacterHandler/HandlePlayerLoginOpcode, WorldSession.CharacterHandler/LoginPlayer, WorldSession.ChatHandler/HandleChatMessageOpcode, WorldSession.MailHandler/HandleMailReturnToSender, WorldSession.MailHandler/HandleMailTakeItem, WorldSession.MailHandler/HandleSendMail, WorldSession.MailHandler/HandleSendMailCallback, WorldSession.MiscHandler/HandleBugOpcode, WorldSession.MiscHandler/HandleWardenDataOpcode, WorldSession.MiscHandler/HandleWhoisOpcode, WorldSession.MiscHandler/HandleWhoOpcode, WorldSession.TradeHandler/HandleAcceptTradeOpcode, WorldSession.TradeHandler/MoveItems, WorldSocket/_HandleCompleteReceivedPacket | — |
| GetUsername | method | — | ChatHandler.AccountCommands/HandleAccountPasswordCommand, ChatHandler.Chat/ExecuteCommand, ChatHandler.DebugCommands/HandleDebugGetPrevPlayTimeCommand, ChatHandler.DebugCommands/HandleDebugSetPrevPlayTimeCommand, Log.Warden/Warden, MovementAnticheat/Finalize, Player.Main/GetShortDescription | — |
| SetUsername | method | — | WorldSocket/_HandleAuthSession | — |
| GetLatency | method | — | AsyncCommandHandlers/HandlePInfoCommand, MovementAnticheat/HandleFlagTests, MovementAnticheat/HandlePositionTests, PointMovementGenerator/ComputePath | — |
| SetLatency | method | — | WorldSocket/_HandlePing | — |
| GetGameBuild | method | — | Log.Warden/Warden, WorldSession.MiscHandler/HandleRequestAccountData, WorldSession.MiscHandler/HandleUpdateAccountData | — |
| SetGameBuild | method | — | WorldSocket/_HandleAuthSession | — |
| GetOS | method | — | Anticheat/CreateWardenForInternal, Log.Warden/Warden | — |
| SetOS | method | — | WorldSocket/_HandleAuthSession | — |
| GetPlatform | method | — | Log.Warden/Warden, WardenMac/WardenMac | — |
| SetPlatform | method | — | WorldSocket/_HandleAuthSession | — |
| SendCompressedMovementPackets | method | Log.Main/Out, MovementData/ClearBuffer, MovementData/HasData, UpdateData/BuildPacket, WorldPacket/WorldPacket | — | — |
| GetAccountMaxLevel | method | — | game_Chat_Channel/Say, WorldSession.ChatHandler/ChatCooldown, WorldSession.ChatHandler/HandleChatMessageOpcode, WorldSession.MailHandler/HandleSendMail | — |
| SetAccountFlags | method | — | WorldSocket/_HandleAuthSession | — |
| GetAccountFlags | method | — | game_Chat_Channel/Say | — |
| SetVerifiedEmail | method | — | WorldSocket/_HandleAuthSession | — |
| HasVerifiedEmail | method | — | — | — |
| GetPlayer | method | — | AccountMgr/GetWhisperScore, AsyncCommandHandlers/HandleAccountInfoResult, AsyncCommandHandlers/ShowAccountListHelper, ChatHandler.AccountCommands/HandleAnticheatCommand, ChatHandler.AccountCommands/HandleKickPlayerCommand, ChatHandler.AccountCommands/HandleMuteCommand, ChatHandler.AccountCommands/HandleUnmuteCommand, ChatHandler.CharacterCommands/HandleAddItemCommand, ChatHandler.CharacterCommands/HandleAddItemSetCommand, ChatHandler.CharacterCommands/HandleCharacterLevelCommand, ChatHandler.CharacterCommands/HandleCharacterPremadeSaveGearCommand, ChatHandler.CharacterCommands/HandleCharacterPremadeSaveSpecCommand, ChatHandler.CharacterCommands/HandleCheatFixedZCommand, ChatHandler.CharacterCommands/HandleCheatFlyCommand, ChatHandler.CharacterCommands/HandleDismountCommand, ChatHandler.CharacterCommands/HandleExploreCheatCommand, ChatHandler.CharacterCommands/HandleGroupAddItemCommand, ChatHandler.CharacterCommands/HandleGroupReplenishCommand, ChatHandler.CharacterCommands/HandleGroupReviveCommand, ChatHandler.CharacterCommands/HandleGroupSummonCommand, ChatHandler.CharacterCommands/HandleHonorAddKillCommand, ChatHandler.CharacterCommands/HandleHonorShow, ChatHandler.CharacterCommands/HandleHoverCommand, ChatHandler.CharacterCommands/HandleItemMoveCommand, ChatHandler.CharacterCommands/HandleLearnAllCraftsCommand, ChatHandler.CharacterCommands/HandleLearnAllGMCommand, ChatHandler.CharacterCommands/HandleLearnAllItemsCommand, ChatHandler.CharacterCommands/HandleLearnAllLangCommand, ChatHandler.CharacterCommands/HandleLearnAllMySpellsCommand, ChatHandler.CharacterCommands/HandleLearnAllMyTalentsCommand, ChatHandler.CharacterCommands/HandleLearnAllMyTaxisCommand, ChatHandler.CharacterCommands/HandleLearnAllTrainerCommand, ChatHandler.CharacterCommands/HandleLearnCommand, ChatHandler.CharacterCommands/HandleLevelUpCommand, ChatHandler.CharacterCommands/HandleModifyAccessoriesCommand, ChatHandler.CharacterCommands/HandleModifyFlyCommand, ChatHandler.CharacterCommands/HandleModifyHairColorCommand, ChatHandler.CharacterCommands/HandleModifyHairStyleCommand, ChatHandler.CharacterCommands/HandleModifyMoneyCommand, ChatHandler.CharacterCommands/HandleModifySkinColorCommand, ChatHandler.CharacterCommands/HandleModifyXpRateCommand, ChatHandler.CharacterCommands/HandleMountCommand, ChatHandler.CharacterCommands/HandleResetSpellsCommand, ChatHandler.CharacterCommands/HandleResetTalentsCommand, ChatHandler.CharacterCommands/HandleSaveCommand, ChatHandler.CharacterCommands/HandleTaxiCheatCommand, ChatHandler.CharacterCommands/HandleUnLearnAllCraftsCommand, ChatHandler.CharacterCommands/HandleUnLearnAllGMCommand, ChatHandler.CharacterCommands/HandleWhisperRestrictionCommand, ChatHandler.CharacterCommands/HandleWhispersCommand, ChatHandler.Chat/ExecuteCommand, ChatHandler.Chat/ExtractLocationFromLink, ChatHandler.Chat/GetGameObjectWithGuid, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/GetSelectedCreature, ChatHandler.Chat/GetSelectedPet, ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/ParseCommands, ChatHandler.CreatureCommands/HandleComeToMeCommand, ChatHandler.CreatureCommands/HandleEscortAddWpCommand, ChatHandler.CreatureCommands/HandleEscortHideWpCommand, ChatHandler.CreatureCommands/HandleEscortModifyWpCommand, ChatHandler.CreatureCommands/HandleEscortShowWpCommand, ChatHandler.CreatureCommands/HandleNpcAddCommand, ChatHandler.CreatureCommands/HandleNpcAddVendorItemCommand, ChatHandler.CreatureCommands/HandleNpcDeleteCommand, ChatHandler.CreatureCommands/HandleNpcFollowCommand, ChatHandler.CreatureCommands/HandleNpcGroupAddCommand, ChatHandler.CreatureCommands/HandleNpcMoveHelperCommand, ChatHandler.CreatureCommands/HandleNpcSummonCommand, ChatHandler.CreatureCommands/HandleNpcTameCommand, ChatHandler.CreatureCommands/HandleNpcUnFollowCommand, ChatHandler.CreatureCommands/HandleNpcWhisperCommand, ChatHandler.CreatureCommands/HandleRespawnCommand, ChatHandler.CreatureCommands/HandleWpAddCommand, ChatHandler.CreatureCommands/HandleWpExportCommand, ChatHandler.CreatureCommands/HandleWpModifyCommand, ChatHandler.CreatureCommands/HandleWpShowCommand, ChatHandler.DebugCommands/HandleDebugAnimCommand, ChatHandler.DebugCommands/HandleDebugConditionCommand, ChatHandler.DebugCommands/HandleDebugFaceMeCommand, ChatHandler.DebugCommands/HandleDebugGetItemStateCommand, ChatHandler.DebugCommands/HandleDebugGetItemValueCommand, ChatHandler.DebugCommands/HandleDebugLoSAllowCommand, ChatHandler.DebugCommands/HandleDebugLoSCommand, ChatHandler.DebugCommands/HandleDebugModItemValueCommand, ChatHandler.DebugCommands/HandleDebugMonsterChatCommand, ChatHandler.DebugCommands/HandleDebugMoveCommand, ChatHandler.DebugCommands/HandleDebugMoveDistanceCommand, ChatHandler.DebugCommands/HandleDebugMoveToCommand, ChatHandler.DebugCommands/HandleDebugPlayCinematicCommand, ChatHandler.DebugCommands/HandleDebugPlayScriptText, ChatHandler.DebugCommands/HandleDebugPlaySoundCommand, ChatHandler.DebugCommands/HandleDebugPvPCreditCommand, ChatHandler.DebugCommands/HandleDebugSendBuyErrorCommand, ChatHandler.DebugCommands/HandleDebugSendChatMsgCommand, ChatHandler.DebugCommands/HandleDebugSendEquipErrorCommand, ChatHandler.DebugCommands/HandleDebugSendNextChannelSpellVisualCommand, ChatHandler.DebugCommands/HandleDebugSendOpcodeCommand, ChatHandler.DebugCommands/HandleDebugSendPoiCommand, ChatHandler.DebugCommands/HandleDebugSendQuestInvalidMsgCommand, ChatHandler.DebugCommands/HandleDebugSendQuestPartyMsgCommand, ChatHandler.DebugCommands/HandleDebugSendSellErrorCommand, ChatHandler.DebugCommands/HandleDebugSendWorldStateCommand, ChatHandler.DebugCommands/HandleDebugSetItemValueCommand, ChatHandler.DebugCommands/HandleMmapConnection, ChatHandler.DebugCommands/HandleMmapLoad, ChatHandler.DebugCommands/HandleMmapLoadedTilesCommand, ChatHandler.DebugCommands/HandleMmapPathCommand, ChatHandler.DebugCommands/HandleMmapStatsCommand, ChatHandler.DebugCommands/HandleMmapTestArea, ChatHandler.DebugCommands/HandleMmapUnload, ChatHandler.DebugCommands/HandleSendSpellChannelVisualCommand, ChatHandler.DebugCommands/HandleSendSpellVisualCommand, ChatHandler.DebugCommands/HandleVideoTurn, ChatHandler.LookupCommands/HandleListCreatureCommand, ChatHandler.LookupCommands/HandleListObjectCommand, ChatHandler.LookupCommands/HandleLookupItemCommand, ChatHandler.LookupCommands/HandlePoolListCommand, ChatHandler.MiscCommands/HandleAuctionAllianceCommand, ChatHandler.MiscCommands/HandleAuctionCommand, ChatHandler.MiscCommands/HandleAuctionGoblinCommand, ChatHandler.MiscCommands/HandleAuctionHordeCommand, ChatHandler.MiscCommands/HandleBankCommand, ChatHandler.MiscCommands/HandleBGCustomCommand, ChatHandler.MiscCommands/HandleBGStartCommand, ChatHandler.MiscCommands/HandleBGStatusCommand, ChatHandler.MiscCommands/HandleBGStopCommand, ChatHandler.MiscCommands/HandleCinematicAddWpCommand, ChatHandler.MiscCommands/HandleCinematicGoTimeCommand, ChatHandler.MiscCommands/HandleGMChatCommand, ChatHandler.MiscCommands/HandleGMCommand, ChatHandler.MiscCommands/HandleGMListIngameCommand, ChatHandler.MiscCommands/HandleGMVisibleCommand, ChatHandler.MiscCommands/HandleInstanceBindingMode, ChatHandler.MiscCommands/HandleInstanceGetDataCommand, ChatHandler.MiscCommands/HandleInstanceListBindsCommand, ChatHandler.MiscCommands/HandleInstancePerfInfosCommand, ChatHandler.MiscCommands/HandleInstanceSaveDataCommand, ChatHandler.MiscCommands/HandleInstanceSetDataCommand, ChatHandler.MiscCommands/HandleInstanceUnbindCommand, ChatHandler.MiscCommands/HandleLinkGraveCommand, ChatHandler.MiscCommands/HandleNearGraveCommand, ChatHandler.MiscCommands/HandlePoolInfoCommand, ChatHandler.MiscCommands/HandlePoolSpawnsCommand, ChatHandler.MiscCommands/HandlePoolUpdateCommand, ChatHandler.MiscCommands/HandleSendItemsCommand, ChatHandler.MiscCommands/HandleSendItemsHelper, ChatHandler.MiscCommands/HandleSendMailCommand, ChatHandler.MiscCommands/HandleSendMassItemsCommand, ChatHandler.MiscCommands/HandleSendMassMailCommand, ChatHandler.MiscCommands/HandleSendMassMoneyCommand, ChatHandler.MiscCommands/HandleSendMoneyCommand, ChatHandler.MiscCommands/HandleSetViewCommand, ChatHandler.MiscCommands/HandleStableCommand, ChatHandler.MiscCommands/HandleTriggerActiveCommand, ChatHandler.MiscCommands/HandleTriggerCommand, ChatHandler.MiscCommands/HandleTriggerNearCommand, ChatHandler.MiscCommands/RegisterPlayerToBG, ChatHandler.MiscCommands/ShowTriggerListHelper, ChatHandler.MiscCommands/ShowTriggerTargetListHelper, ChatHandler.ObjectCommands/getSelectedGameObject, ChatHandler.ObjectCommands/HandleGameObjectAddCommand, ChatHandler.ObjectCommands/HandleGameObjectDeleteCommand, ChatHandler.ObjectCommands/HandleGameObjectMoveCommand, ChatHandler.ObjectCommands/HandleGameObjectNearCommand, ChatHandler.ObjectCommands/HandleGameObjectSelectCommand, ChatHandler.ObjectCommands/HandleGameObjectTargetCommand, ChatHandler.ObjectCommands/HandleGameObjectTempAddCommand, ChatHandler.ObjectCommands/HandleGameObjectTurnCommand, ChatHandler.ObjectCommands/HandleGameObjectUseCommand, ChatHandler.PlayerBotMgr/HandleBattleBotShowAllPathsCommand, ChatHandler.PlayerBotMgr/HandlePartyBotAddCommand, ChatHandler.PlayerBotMgr/HandlePartyBotAoECommand, ChatHandler.PlayerBotMgr/HandlePartyBotAttackStartCommand, ChatHandler.PlayerBotMgr/HandlePartyBotAttackStopCommand, ChatHandler.PlayerBotMgr/HandlePartyBotClearMarksCommand, ChatHandler.PlayerBotMgr/HandlePartyBotCloneCommand, ChatHandler.PlayerBotMgr/HandlePartyBotComeToMeCommand, ChatHandler.PlayerBotMgr/HandlePartyBotControlMarkCommand, ChatHandler.PlayerBotMgr/HandlePartyBotFocusMarkCommand, ChatHandler.PlayerBotMgr/HandlePartyBotLoadCommand, ChatHandler.PlayerBotMgr/HandlePartyBotPauseHelper, ChatHandler.PlayerBotMgr/HandlePartyBotPullCommand, ChatHandler.PlayerBotMgr/HandlePartyBotToggleCastingCommand, ChatHandler.PlayerBotMgr/HandlePartyBotUseGObjectCommand, ChatHandler.ServerCommands/HandleChangeWeatherCommand, ChatHandler.ServerCommands/HandleWorldTestCommand, ChatHandler.TeleportCommands/HandleGoCommand, ChatHandler.TeleportCommands/HandleGocorpseCommand, ChatHandler.TeleportCommands/HandleGoCreatureCommand, ChatHandler.TeleportCommands/HandleGoForwardCommand, ChatHandler.TeleportCommands/HandleGoGraveyardCommand, ChatHandler.TeleportCommands/HandleGoGridCommand, ChatHandler.TeleportCommands/HandleGonameCommand, ChatHandler.TeleportCommands/HandleGoObjectCommand, ChatHandler.TeleportCommands/HandleGoRelativeCommand, ChatHandler.TeleportCommands/HandleGoTargetCommand, ChatHandler.TeleportCommands/HandleGoTaxinodeCommand, ChatHandler.TeleportCommands/HandleGoTriggerCommand, ChatHandler.TeleportCommands/HandleGoUpCommand, ChatHandler.TeleportCommands/HandleGoXYCommand, ChatHandler.TeleportCommands/HandleGoXYZCommand, ChatHandler.TeleportCommands/HandleGoXYZOCommand, ChatHandler.TeleportCommands/HandleGoZoneXYCommand, ChatHandler.TeleportCommands/HandleGroupgoCommand, ChatHandler.TeleportCommands/HandleNamegoCommand, ChatHandler.TeleportCommands/HandleStartCommand, ChatHandler.TeleportCommands/HandleTeleAddCommand, ChatHandler.TeleportCommands/HandleTeleCommand, ChatHandler.TeleportCommands/HandleUnstuckCommand, ChatHandler.TicketCommands/HandleGMTicketAssignToCommand, ChatHandler.TicketCommands/HandleGMTicketCloseByIdCommand, ChatHandler.TicketCommands/HandleGMTicketCommentCommand, ChatHandler.TicketCommands/HandleGMTicketCompleteCommand, ChatHandler.TicketCommands/HandleGMTicketCounterCommand, ChatHandler.TicketCommands/HandleGMTicketDeleteByIdCommand, ChatHandler.TicketCommands/HandleGMTicketNextCommand, ChatHandler.TicketCommands/HandleGMTicketNotifyCommand, ChatHandler.TicketCommands/HandleGMTicketPreviousCommand, ChatHandler.TicketCommands/HandleGMTicketResponseResetCommand, ChatHandler.TicketCommands/HandleGMTicketUnAssignCommand, ChatHandler.TicketCommands/_HandleGMTicketResponseAppendCommand, ChatHandler.UnitCommands/HandleAoEDamageCommand, ChatHandler.UnitCommands/HandleAuraHelper, ChatHandler.UnitCommands/HandleCastBackCommand, ChatHandler.UnitCommands/HandleCastCommand, ChatHandler.UnitCommands/HandleCastDistCommand, ChatHandler.UnitCommands/HandleCastSelfCommand, ChatHandler.UnitCommands/HandleCastTargetCommand, ChatHandler.UnitCommands/HandleChargeCommand, ChatHandler.UnitCommands/HandleCooldownClearCommand, ChatHandler.UnitCommands/HandleDamageCommand, ChatHandler.UnitCommands/HandleDeMorphCommand, ChatHandler.UnitCommands/HandleDieHelper, ChatHandler.UnitCommands/HandleGetAngleCommand, ChatHandler.UnitCommands/HandleGetDistanceCommand, ChatHandler.UnitCommands/HandleGPSCommand, ChatHandler.UnitCommands/HandleGUIDCommand, ChatHandler.UnitCommands/HandleKnockBackCommand, ChatHandler.UnitCommands/HandleModifyMorphCommand, ChatHandler.UnitCommands/HandlePossessCommand, game_Guild_Guild/Roster, GossipDef/AddMenuItem#5, MasterPlayer.Main/LoadMails, MovementAnticheat/AddCheats, MovementBroadcaster/UpdateConfiguration, Player.Main/PlayerLogHeaderToConsole, Player.Main/PlayerLogHeaderToFile, Player.Main/PlayerLogToDB, World/SendBroadcastTextToWorld, World/SendGlobalMessage, World/SendGMText, World/SendGMTicketText, World/SendGMTicketText#2, World/SendWorldText, World/SendWorldTextToBGAndQueue, World/SendZoneMessage, WorldSession.AuctionHouseHandler/GetCheckedAuctionHouseForAuctioneer, WorldSession.AuctionHouseHandler/HandleAuctionHelloOpcode, WorldSession.AuctionHouseHandler/HandleAuctionListBidderItems, WorldSession.AuctionHouseHandler/HandleAuctionListItems, WorldSession.AuctionHouseHandler/HandleAuctionListOwnerItems, WorldSession.AuctionHouseHandler/HandleAuctionPlaceBid, WorldSession.AuctionHouseHandler/HandleAuctionRemoveItem, WorldSession.AuctionHouseHandler/HandleAuctionSellItem, WorldSession.AuctionHouseHandler/operator(), WorldSession.BattleGroundHandler/HandleAreaSpiritHealerQueryOpcode, WorldSession.BattleGroundHandler/HandleAreaSpiritHealerQueueOpcode, WorldSession.BattleGroundHandler/HandleBattlemasterHelloOpcode, WorldSession.BattleGroundHandler/RequestBgJoinQueue, WorldSession.CharacterHandler/HandlePlayerLogin, WorldSession.CharacterHandler/HandlePlayerLoginOpcode, WorldSession.CharacterHandler/HandleSetFactionAtWarOpcode, WorldSession.CharacterHandler/HandleSetWatchedFactionOpcode, WorldSession.ChatHandler/ChatCooldown, WorldSession.ChatHandler/HandleChatMessageOpcode, WorldSession.ChatHandler/HandleEmoteOpcode, WorldSession.ChatHandler/HandleTextEmoteOpcode, WorldSession.ChatHandler/SanitizeChatMessage, WorldSession.CombatHandler/HandleAttackStopOpcode, WorldSession.CombatHandler/HandleSetSheathedOpcode, WorldSession.CombatHandler/SendAttackStop, WorldSession.DuelHandler/HandleDuelAcceptedOpcode, WorldSession.DuelHandler/HandleDuelCancelledOpcode, WorldSession.GMTicketHandler/HandleGMSurveySubmitOpcode, WorldSession.GMTicketHandler/HandleGMTicketCreateOpcode, WorldSession.GMTicketHandler/HandleGMTicketDeleteTicketOpcode, WorldSession.GMTicketHandler/HandleGMTicketGetTicketOpcode, WorldSession.GMTicketHandler/HandleGMTicketUpdateTextOpcode, WorldSession.GroupHandler/HandleGroupAcceptOpcode, WorldSession.GroupHandler/HandleGroupAssistantLeaderOpcode, WorldSession.GroupHandler/HandleGroupChangeSubGroupOpcode, WorldSession.GroupHandler/HandleGroupDeclineOpcode, WorldSession.GroupHandler/HandleGroupDisbandOpcode, WorldSession.GroupHandler/HandleGroupInviteOpcode, WorldSession.GroupHandler/HandleGroupRaidConvertOpcode, WorldSession.GroupHandler/HandleGroupSetLeaderOpcode, WorldSession.GroupHandler/HandleGroupSwapSubGroupOpcode, WorldSession.GroupHandler/HandleGroupUninviteGuidOpcode, WorldSession.GroupHandler/HandleGroupUninviteOpcode, WorldSession.GroupHandler/HandleLootMethodOpcode, WorldSession.GroupHandler/HandleLootRoll, WorldSession.GroupHandler/HandleMinimapPingOpcode, WorldSession.GroupHandler/HandleRaidReadyCheckOpcode, WorldSession.GroupHandler/HandleRaidTargetUpdateOpcode, WorldSession.GroupHandler/HandleRandomRollOpcode, WorldSession.GuildHandler/HandleGuildAcceptOpcode, WorldSession.GuildHandler/HandleGuildAddRankOpcode, WorldSession.GuildHandler/HandleGuildChangeInfoTextOpcode, WorldSession.GuildHandler/HandleGuildCreateOpcode, WorldSession.GuildHandler/HandleGuildDelRankOpcode, WorldSession.GuildHandler/HandleGuildDemoteOpcode, WorldSession.GuildHandler/HandleGuildDisbandOpcode, WorldSession.GuildHandler/HandleGuildInfoOpcode, WorldSession.GuildHandler/HandleGuildInviteOpcode, WorldSession.GuildHandler/HandleGuildLeaderOpcode, WorldSession.GuildHandler/HandleGuildMOTDOpcode, WorldSession.GuildHandler/HandleGuildPromoteOpcode, WorldSession.GuildHandler/HandleGuildRankOpcode, WorldSession.GuildHandler/HandleGuildRemoveOpcode, WorldSession.GuildHandler/HandleGuildSetOfficerNoteOpcode, WorldSession.GuildHandler/HandleGuildSetPublicNoteOpcode, WorldSession.GuildHandler/HandleSaveGuildEmblemOpcode, WorldSession.ItemHandler/CheckBanker, WorldSession.ItemHandler/HandleBuybackItem, WorldSession.ItemHandler/HandleBuyItemInSlotOpcode, WorldSession.ItemHandler/HandleBuyItemOpcode, WorldSession.ItemHandler/HandleListInventoryOpcode, WorldSession.ItemHandler/HandleSellItemOpcode, WorldSession.ItemHandler/HandleSetAmmoOpcode, WorldSession.ItemHandler/SendListInventory, WorldSession.LootHandler/DoLootRelease, WorldSession.LootHandler/HandleAutostoreLootItemOpcode, WorldSession.LootHandler/HandleLootMasterGiveOpcode, WorldSession.LootHandler/HandleLootMoneyOpcode, WorldSession.LootHandler/HandleLootOpcode, WorldSession.LootHandler/HandleLootReleaseOpcode, WorldSession.MailHandler/Callback, WorldSession.MailHandler/CheckMailBox, WorldSession.MailHandler/HandleMailTakeItem, WorldSession.MailHandler/HandleMailTakeMoney, WorldSession.MailHandler/HandleSendMailCallback, WorldSession.MiscHandler/HandleAreaTriggerOpcode, WorldSession.MiscHandler/HandleBugOpcode, WorldSession.MiscHandler/HandleCompleteCinematic, WorldSession.MiscHandler/HandleLogoutCancelOpcode, WorldSession.MiscHandler/HandleLogoutRequestOpcode, WorldSession.MiscHandler/HandleMoveSetRawPosition, WorldSession.MiscHandler/HandleReclaimCorpseOpcode, WorldSession.MiscHandler/HandleResurrectResponseOpcode, WorldSession.MiscHandler/HandleSetActionBarTogglesOpcode, WorldSession.MiscHandler/HandleSetActionButtonOpcode, WorldSession.MiscHandler/HandleTogglePvP, WorldSession.MiscHandler/HandleWorldTeleportOpcode, WorldSession.MiscHandler/HandleZoneUpdateOpcode, WorldSession.MiscHandler/operator(), WorldSession.MovementHandler/HandleMountSpecialAnimOpcode, WorldSession.MovementHandler/HandleMoveWorldportAck, WorldSession.NPCHandler/CheckStableMaster, WorldSession.NPCHandler/HandleBankerActivateOpcode, WorldSession.NPCHandler/HandleBinderActivateOpcode, WorldSession.NPCHandler/HandleBuyStableSlot, WorldSession.NPCHandler/HandleGossipHelloOpcode, WorldSession.NPCHandler/HandleGossipSelectOptionOpcode, WorldSession.NPCHandler/HandleListStabledPetsOpcode, WorldSession.NPCHandler/HandleRepairItemOpcode, WorldSession.NPCHandler/HandleSpiritHealerActivateOpcode, WorldSession.NPCHandler/HandleStablePet, WorldSession.NPCHandler/HandleStableSwapPet, WorldSession.NPCHandler/HandleTabardVendorActivateOpcode, WorldSession.NPCHandler/HandleTrainerBuySpellOpcode, WorldSession.NPCHandler/HandleUnstablePet, WorldSession.NPCHandler/SendBindPoint, WorldSession.NPCHandler/SendShowBank, WorldSession.NPCHandler/SendStablePet, WorldSession.NPCHandler/SendTrainerList, WorldSession.PetHandler/HandlePetAction, WorldSession.PetHandler/HandlePetCastSpellOpcode, WorldSession.PetHandler/HandlePetSpellAutocastOpcode, WorldSession.PetHandler/HandlePetStopAttack, WorldSession.PetHandler/HandlePetUnlearnOpcode, WorldSession.PetitionsHandler/HandleOfferPetitionOpcode, WorldSession.PetitionsHandler/HandlePetitionBuyOpcode, WorldSession.PetitionsHandler/HandlePetitionSignOpcode, WorldSession.PetitionsHandler/SendPetitionShowList, WorldSession.QueryHandler/HandleCorpseQueryOpcode, WorldSession.QuestHandler/HandleQuestgiverAcceptQuestOpcode, WorldSession.QuestHandler/HandleQuestgiverChooseRewardOpcode, WorldSession.QuestHandler/HandleQuestgiverHelloOpcode, WorldSession.QuestHandler/HandleQuestgiverRequestRewardOpcode, WorldSession.QuestHandler/HandleQuestLogSwapQuest, WorldSession.SkillHandler/HandleTalentWipeConfirmOpcode, WorldSession.SkillHandler/HandleUnlearnSkillOpcode, WorldSession.SpellHandler/HandleCastSpellOpcode, WorldSession.SpellHandler/HandleGameObjectUseOpcode, WorldSession.SpellHandler/HandlePetCancelAuraOpcode, WorldSession.TaxiHandler/HandleActivateTaxiExpressOpcode, WorldSession.TaxiHandler/HandleActivateTaxiOpcode, WorldSession.TaxiHandler/HandleTaxiQueryAvailableNodes, WorldSession.TaxiHandler/SendDoFlight, WorldSession.TaxiHandler/SendLearnNewTaxiNode, WorldSession.TaxiHandler/SendTaxiMenu, WorldSession.TaxiHandler/SendTaxiStatus, WorldSession.TradeHandler/HandleInitiateTradeOpcode | — |
| SetSecurity | method | — | ChatHandler.AccountCommands/HandleAccountSetGmLevelCommand | — |
| GetRemoteAddress | method | — | AuctionHouseMgr/IsAvailableFor, BattleGroundMgr/AddGroup, ChatHandler.LookupCommands/HandleListClickToMoveCommand, go_scripts/GOHello_go_silithyste, HonorMgr/Add, Log.Warden/Warden, OutdoorPvPSI/HandleAreaTrigger, OutdoorPvPSI/HandleDropFlag, Player.Main/GetShortDescription, Player.Main/LeaveBattleground, Player.Main/PlayerLogHeaderToConsole, Player.Main/PlayerLogHeaderToFile, Player.Main/PlayerLogToDB, WorldSession.AuctionHouseHandler/HandleAuctionSellItem, WorldSession.BattleGroundHandler/HandleBattleFieldPortOpcode, WorldSession.CharacterHandler/HandleChangePlayerNameOpcodeCallBack, WorldSession.CharacterHandler/HandlePlayerLogin | — |
| SetPlayer | method | — | Map.Main/CrashUnload, PlayerBotAI/SpawnNewPlayer, WorldSession.CharacterHandler/HandlePlayerLogin | — |
| SetMasterPlayer | method | — | PlayerBotAI/SpawnNewPlayer, WorldSession.CharacterHandler/HandlePlayerLogin | — |
| GetSocket | method | — | Player.Main/CreatePacketBroadcaster, WorldSession.CharacterHandler/HandlePlayerLogin | — |
| GetChatPacketProcessingType | function | — | — | — |
| SetInQueue | method | — | World/AddQueuedSession, World/RemoveQueuedSession, World/UpdateSessions | — |
| IsConnected | method | — | ChatHandler.AccountCommands/HandleKickPlayerCommand, ChatHandler.AccountCommands/HandleSniffCommand, game_Objects_Item/AddToClientUpdateList, Player.Main/ScheduleRepopAtGraveyard, Player.Main/Update, Unit.Main/CheckPendingMovementChanges | — |
| KickDisconnectedFromWorld | method | — | ChatHandler.AccountCommands/HandleKickPlayerCommand | — |
| PlayerLoading | method | — | game_Group_Group/UpdateOfflineLeader, Map.Main/Add#3, ReputationMgr/SendVisible, Spell.Effects/EffectApplyAura, Spell.Main/SendCastResult, Unit.Main/AddSpellAuraHolder, Unit.SpellAuras/HandleManaShield, Unit.SpellAuras/HandlePeriodicDamage, Unit.SpellAuras/HandlePeriodicHeal, Unit.SpellAuras/HandlePeriodicHealthFunnel, Unit.SpellAuras/HandlePeriodicLeech, Unit.SpellAuras/HandleSchoolAbsorb, World/RemoveSession, World/UpdateSessions, WorldSession.CharacterHandler/HandlePlayerLoginOpcode | — |
| PlayerLogout | method | — | game_Group_Group/GetGroupMemberStatus, game_Objects_Item/AddToClientUpdateList, Player.Main/ExecuteTeleportFar, Player.Main/RemoveFromWorld, Player.Main/TeleportTo, ZoneScript/OnPlayerLeave#2 | — |
| PlayerLogoutWithSave | method | — | Map.Main/RemoveCorpses | — |
| GetCreateTime | method | — | World/SetSessionDisconnected, World/UpdateSessions | — |
| GetConsecutivePlayTime | method | — | Player.Main/LoadFromDB, World/AddSession_ | — |
| GetPreviousPlayedTime | method | — | ChatHandler.DebugCommands/HandleDebugGetPrevPlayTimeCommand | — |
| SetPreviousPlayedTime | method | — | ChatHandler.DebugCommands/HandleDebugSetPrevPlayTimeCommand, World/AddSession_ | — |
| IsLogingOut | method | — | ChatHandler.MiscCommands/HandleSendMessageCommand, Player.Main/ActivateTaxiPathTo, Player.Main/BuildPlayerRepop, Player.Main/CanEquipItem#2, Player.Main/CanUnequipItem, Player.Main/SaveToDB, WorldSession.TradeHandler/HandleInitiateTradeOpcode | — |
| LogoutRequest | method | — | MovementAnticheat/HandlePositionTests, WorldSession.MiscHandler/HandleLogoutCancelOpcode, WorldSession.MiscHandler/HandleLogoutRequestOpcode | — |
| QueuePacket | method | Errors/PrintStacktraceAndThrow, Log.Main/Out, Opcodes/LookupOpcodeHandler, Packet/GetOpcode | AiBotAI.Main/OnPacketReceived, BattleBotAI.Main/OnPacketReceived, CombatBotBaseAI/OnPacketReceived, PartyBotAI/OnPacketReceived | — |
| ShouldLogOut | method | — | — | — |
| GetSessionDbcLocale | method | — | AsyncCommandHandlers/HandlePInfoCommand, AsyncCommandHandlers/HandleResponse, AsyncCommandHandlers/ShowPlayerListHelper, AuctionHouseMgr/BuildListAuctionItems, ChatHandler.Chat/GetSessionDbcLocale, Player.Main/RewardQuest | — |
| GetSessionDbLocaleIndex | method | — | AuctionHouseMgr/BuildListAuctionItems, ChatHandler.Chat/GetSessionDbLocaleIndex, game_Battlegrounds_BattleGround/SendRewardMarkByMail, game_Guild_Guild/Create#2, GossipDef/AddMenuItem#5, GossipDef/SendGossipMenu, GossipDef/SendPointOfInterest#2, GossipDef/SendQuestGiverOfferReward, GossipDef/SendQuestGiverQuestDetails, GossipDef/SendQuestGiverQuestList, GossipDef/SendQuestGiverRequestItems, GossipDef/SendTalking#2, Map.Main/SendDefenseMessage, Player.Main/PrepareGossipMenu, Player.Main/SendPreparedQuest, Player.Main/SendQuestConfirmAccept, Spell.Main/SendResurrectRequest, WorldObject.Object/MonsterWhisper#2, WorldSession.ItemHandler/HandleItemNameQueryOpcode, WorldSession.ItemHandler/HandleItemQuerySingleOpcode, WorldSession.MiscHandler/HandleAreaTriggerOpcode, WorldSession.MiscHandler/operator(), WorldSession.NPCHandler/SendTrainerList, WorldSession.QueryHandler/HandleCreatureQueryOpcode, WorldSession.QueryHandler/HandleGameObjectQueryOpcode, WorldSession.QueryHandler/HandleNpcTextQueryOpcode, WorldSession.QueryHandler/HandlePageTextQueryOpcode, WorldSession.QuestHandler/HandleQuestQueryOpcode | — |
| GetLastPubChanMsgTime | method | — | WorldSession.ChatHandler/ChatCooldown | — |
| SetLastPubChanMsgTime | method | — | WorldSession.ChatHandler/HandleChatMessageOpcode | — |
| QueueBinaryPacket | method | Log.Main/Out, MovementAnticheat/IsLoggedOpcode, MovementAnticheat/LogMovementPacket, Opcodes/LookupOpcodeHandler, SniffFile/WritePacket#2, WorldPacket/GetOpcode, WorldSocket/GetRemoteIpString | WorldSocket/_HandleCompleteReceivedPacket | — |
| GetBot | method | — | ChatHandler.PlayerBotMgr/ForceAccountConnection, ChatHandler.PlayerBotMgr/OnPlayerInWorld, Player.Main/LoadFromDB, Player.Main/RemoveTemporaryAI, Player.Main/SetControlledBy, WorldSession.CharacterHandler/HandlePlayerLogin | — |
| SetBot | method | — | ChatHandler.PlayerBotMgr/AddBot#2 | — |
| SetSessionKey | method | — | WorldSocket/_HandleAuthSession | — |
| GetWarden | method | — | AsyncCommandHandlers/HandlePInfoCommand | — |
| GetFingerprint | method | — | — | — |
| CleanupFingerprintHistory | method | — | — | — |
| GetClientMoverGuid | method | — | Player.Main/GetConfirmedMover | — |
| HasClientMovementControl | method | — | — | — |
| SetReceivedWhoRequest | method | — | WorldSession.MiscHandler/HandleWhoOpcode, WorldSession.MiscHandler/operator() | — |
| ReceivedWhoRequest | method | — | WorldSession.MiscHandler/HandleWhoOpcode | — |
| SetReceivedAHListRequest | method | — | WorldSession.AuctionHouseHandler/HandleAuctionListBidderItems, WorldSession.AuctionHouseHandler/HandleAuctionListItems, WorldSession.AuctionHouseHandler/HandleAuctionListOwnerItems, WorldSession.AuctionHouseHandler/operator() | — |
| ReceivedAHListRequest | method | — | WorldSession.AuctionHouseHandler/HandleAuctionListBidderItems, WorldSession.AuctionHouseHandler/HandleAuctionListItems, WorldSession.AuctionHouseHandler/HandleAuctionListOwnerItems | — |
| LogUnexpectedOpcode | method | Log.Main/Out, Opcodes/LookupOpcodeName, Packet/GetOpcode | — | — |
| HasRecentPacket | method | — | Map.Main/UpdatePlayers | — |
| HasTrialRestrictions | method | World/getConfig | Player.Main/GetMaxMoney, Player.Main/GiveXP, Player.Main/UpdateCraftSkill, Player.Main/UpdateFishingSkill, Player.Main/UpdateGatherSkill, World/AddQueuedSession, World/AddSession_, WorldSession.AuctionHouseHandler/HandleAuctionPlaceBid, WorldSession.AuctionHouseHandler/HandleAuctionSellItem, WorldSession.ChatHandler/HandleChatMessageOpcode, WorldSession.GuildHandler/HandleGuildCreateOpcode, WorldSession.GuildHandler/HandleGuildInviteOpcode, WorldSession.MailHandler/HandleSendMail, WorldSession.MailHandler/HandleSendMailRequest, WorldSession.PetitionsHandler/HandleOfferPetitionOpcode, WorldSession.PetitionsHandler/HandlePetitionBuyOpcode, WorldSession.PetitionsHandler/HandlePetitionSignOpcode, WorldSession.PetitionsHandler/HandleTurnInPetitionOpcode, WorldSession.TradeHandler/HandleInitiateTradeOpcode | — |
| StartSniffing | method | — | ChatHandler.AccountCommands/HandleSniffCommand | — |
| CheckPlayedTimeLimit | method | WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag | — | — |
| StopSniffing | method | — | ChatHandler.AccountCommands/HandleSniffCommand | — |
| SendTrainerList | decl | — | — | — |
| SendPlayTimeWarning | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#4, WorldPacket/WorldPacket#4 | Player.Main/CanRewardQuest, WorldSession.GroupHandler/HandleLootRoll | — |
| ForcePlayerLogoutDelay | method | Object/IsInWorld, Player.Main/OnDisconnected, Player.Main/Player#2, Player.Main/RemoveFromGroup, Player.Main/SaveToDB, World/getConfig, World/IsStopped, WorldObject.Object/FindMap | World/AddSession_ | — |
| GetAccountData | method | — | WorldSession.MiscHandler/HandleRequestAccountData | — |
| SetTutorialInt | method | — | WorldSession.CharacterHandler/HandleTutorialClearOpcode, WorldSession.CharacterHandler/HandleTutorialFlagOpcode, WorldSession.CharacterHandler/HandleTutorialResetOpcode | — |
| Update | method | Anticheat/GetAnticheatLib, Anticheat/RemoveWardenSession, ChatHandler.PlayerBotMgr/ForceAccountConnection, Log.Main/Out, MasterPlayer.Main/LoadPlayer, MasterPlayer.Main/Update, Object/IsInWorld, PacketFilter/ProcessLogout, Player.Main/DeletePacketBroadcaster, PlayerBotMgr/IsSavingAllowed, shared_Util/getMSTime, World/getConfig, World/getConfig#4, World/IsStopped, WorldSocket/FinalizeSession, WorldSocket/IsClosing, WorldTimer/getMSTimeDiffToNow | Map.Main/Update#3, World/UpdateSessions | — |
| CanProcessPackets | method | ChatHandler.PlayerBotMgr/IsChatBot, Object/GetGUIDLow, WorldSocket/IsClosing | — | — |
| ProcessPackets | method | Errors/PrintStacktraceAndThrow, Log.Main/Out, Object/IsInWorld, Opcodes/LookupOpcodeHandler, Packet/GetOpcode, PacketFilter/PacketProcessType, shared_Util/getMSTime, World/getConfig, World/getConfig#4, WorldTimer/getMSTimeDiffToNow | Map.Main/ProcessSessionPackets, World/ProcessAsyncPackets | — |
| HandlePingOpcode | decl | — | — | — |
| HandleAuthSessionOpcode | decl | — | — | — |
| ClearIncomingPacketsByType | method | Errors/PrintStacktraceAndThrow | Map.Main/Add#3 | — |
| SetDisconnectedSession | method | World/SetSessionDisconnected | — | — |
| UpdateDisconnected | method | Errors/PrintStacktraceAndThrow, Object/IsInWorld, WorldObject.Object/FindMap | World/UpdateSessions | — |
| LogoutPlayer | method | BattleGroundMgr/PlayerLoggedOut, Database/CreateStatement, game_Group_Group/UpdatePlayerOnlineStatus, game_Guild_Guild/BroadcastEvent, game_Guild_Guild/SetMemberStats, game_Guild_Guild/UpdateLogoutTime, Guild/GetMemberSlot, GuildMgr/GetGuildById, HostileRefManager/deleteReferences, HostileRefManager/setOnlineOfflineState#2, LFGQueue/GetMessager, LFGQueue/RemovePlayerFromQueue, Log.Main/Out, Map.Main/DeleteFromWorld, Map.Main/GetId, Map.Main/IsNonRaidDungeon, Map.Main/Remove#3, MapManager/CancelInstanceCreationForPlayer, MapManager/ExecuteSingleDelayedTeleport#2, MasterPlayer.Main/GetGUIDLow, MasterPlayer.Main/GetObjectGuid, MasterPlayer.Main/GetSocial, MasterPlayer.Main/SaveToDB, MasterPlayer.Main/SetSocial, MovementData/ClearBuffer, Object/GetObjectGuid, Object/IsInWorld, ObjectGuid/Clear, ObjectMgr/GetGoBackTrigger, ObjectMgr/UpdatePlayerCache, Player.Main/BuildPlayerRepop, Player.Main/CleanupChannels, Player.Main/CleanupsBeforeDelete, Player.Main/GetBattleGround, Player.Main/GetBoundInstanceSaveForSelfOrGroup, Player.Main/GetDeathTimer, Player.Main/GetGroup, Player.Main/GetGuildId, Player.Main/GetLootGuid, Player.Main/GetName, Player.Main/IsBeingTeleportedFar, Player.Main/IsInLFG, Player.Main/KillPlayer, Player.Main/LeaveBattleground, Player.Main/Player#2, Player.Main/RemovePet, Player.Main/RepopAtGraveyard, Player.Main/SaveToDB, Player.Main/TeleportToHomebind, Player.Main/UninviteFromGroup, shared_Util/getMSTime, SocialMgr/RemovePlayerSocial, SocialMgr/SendFriendStatus, SqlStatementID/SqlStatementID, Unit.Main/CombatStop, Unit.Main/GetHostileRefManager, Unit.Main/HasAuraType, Unit.Main/IsInCombat, Unit.Main/RemoveAurasWithInterruptFlags, Unit.Main/RemoveSpellsCausingAura, World/GetLFGQueue, WorldObject.Object/FindMap, WorldObject.Object/GetMap, WorldPacket/WorldPacket#4, WorldSession.LootHandler/DoLootRelease, WorldSession.MovementHandler/HandleMoveWorldportAck | ChatHandler.PlayerBotMgr/Update, Map.Main/CrashUnload, ObjectAccessor/KickPlayer, Player.Main/ChangeRace, World/BanAccount#2, World/HandleAccountSelectResult, WorldSession.CharacterHandler/HandleCharDeleteOpcode, WorldSession.MiscHandler/HandleLogoutRequestOpcode | account |
| KickPlayer | method | WorldSocket/CloseSocket | ChatHandler.AccountCommands/HandleKickPlayerCommand, ChatHandler.CharacterCommands/HandleCharacterEraseCommand, Log.Warden/KickSession, ObjectAccessor/KickPlayer, World/AddSession_, World/BanAccount#2, World/HandleAccountSelectResult, World/KickAll, World/KickAllLess, World/RemoveSession, WorldSession.CharacterHandler/HandlePlayerLogin, WorldSession.ChatHandler/SanitizeChatMessage, WorldSession.MovementHandler/HandleMovementOpcodes, WorldSession.MovementHandler/HandleMoveRootAck | — |
| SendAreaTriggerMessage | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#3, WorldPacket/WorldPacket#4 | BattleGroundWS/HandleAreaTrigger, ChatHandler.MiscCommands/HandleSendMessageCommand, WorldSession.MiscHandler/HandleAreaTriggerOpcode | — |
| SendNotification | method | ByteBuffer/operator<<#3, WorldPacket/WorldPacket#4 | custom_creatures/CompleteLearnProfession, custom_creatures/Enchant, custom_creatures/GossipSelect_EnchantNPC, custom_creatures/LearnAllRecipesInProfession, WorldSession.CharacterHandler/HandlePlayerLogin, WorldSession.ChatHandler/HandleChatMessageOpcode, WorldSession.ChatHandler/HandleEmoteOpcode, WorldSession.ChatHandler/HandleTextEmoteOpcode | — |
| SendNotification#2 | method | ByteBuffer/operator<<#3, WorldPacket/WorldPacket#4 | ChatHandler.MiscCommands/HandleGMChatCommand, ChatHandler.MiscCommands/HandleGMCommand, Player.Main/SetCheatAlwaysCrit, Player.Main/SetCheatAlwaysProc, Player.Main/SetCheatBeastmaster, Player.Main/SetCheatDebuffImmunity, Player.Main/SetCheatDebugTargetInfo, Player.Main/SetCheatFixedZ, Player.Main/SetCheatFly, Player.Main/SetCheatGod, Player.Main/SetCheatIgnoreTriggers, Player.Main/SetCheatInstantCast, Player.Main/SetCheatNoCastCheck, Player.Main/SetCheatNoCooldown, Player.Main/SetCheatNoPowerCost, Player.Main/SetCheatTriggerPass, Player.Main/SetGameMaster, Player.Main/SetGMChat, Player.Main/SetGMVisible, WorldSession.BattleGroundHandler/HandleBattlemasterHelloOpcode, WorldSession.CharacterHandler/HandlePlayerLogin, WorldSession.ChatHandler/HandleChatMessageOpcode, WorldSession.GuildHandler/HandleGuildCreateOpcode, WorldSession.GuildHandler/HandleGuildInviteOpcode, WorldSession.MiscHandler/HandleMoveSetRawPosition, WorldSession.MiscHandler/HandleWhoisOpcode, WorldSession.MiscHandler/HandleWorldTeleportOpcode, WorldSession.PetitionsHandler/HandleOfferPetitionOpcode, WorldSession.PetitionsHandler/HandlePetitionBuyOpcode, WorldSession.PetitionsHandler/HandlePetitionSignOpcode, WorldSession.PetitionsHandler/HandleTurnInPetitionOpcode, WorldSession.TradeHandler/HandleAcceptTradeOpcode | — |
| GetMasterPlayer | method | — | ChatHandler.CharacterCommands/HandleWhispersCommand, ChatHandler.DebugCommands/HandleDebugChatFreezeCommand, game_Guild_Guild/BroadcastToGuild, game_Guild_Guild/BroadcastToOfficers, game_Mail_Mail/prepareTemplateItems, Map.Main/CrashUnload, Player.Main/ConvertSpell, Player.Main/FindSocial, Player.Main/GetSocial, Player.Main/SendInitialPacketsBeforeAddToMap, Player.Main/SetGMVisible, WorldSession.CharacterHandler/HandlePlayerLogin, WorldSession.ChatHandler/HandleChatMessageOpcode, WorldSession.MailHandler/HandleGetMailList, WorldSession.MailHandler/HandleMailCreateTextItem, WorldSession.MailHandler/HandleMailDelete, WorldSession.MailHandler/HandleMailMarkAsRead, WorldSession.MailHandler/HandleMailReturnToSender, WorldSession.MailHandler/HandleMailTakeItem, WorldSession.MailHandler/HandleMailTakeMoney, WorldSession.MailHandler/HandleQueryNextMailTime, WorldSession.MailHandler/HandleSendMail, WorldSession.MailHandler/HandleSendMailCallback, WorldSession.MailHandler/HandleSendMailRequest, WorldSession.MiscHandler/HandleAddFriendOpcode, WorldSession.MiscHandler/HandleAddIgnoreOpcode, WorldSession.MiscHandler/HandleDelFriendOpcode, WorldSession.MiscHandler/HandleDelIgnoreOpcode, WorldSession.MiscHandler/HandleFriendListOpcode, WorldSession.MiscHandler/HandleSetActionButtonOpcode | — |
| GetMangosString | method | ObjectMgr/GetMangosString | ChatHandler.Chat/GetMangosString, game_Battlegrounds_BattleGround/SendRewardMarkByMail, Player.Main/AutoUnequipItemFromSlot, Player.Main/PrepareGossipMenu, Player.Main/PSendSysMessage#2, Player.Main/SendSysMessage#2, Player.Main/_LoadInventory, WorldSession.BattleGroundHandler/SendBattleGroundJoinError, WorldSession.ChatHandler/HandleChatMessageOpcode, WorldSession.ChatHandler/HandleEmoteOpcode, WorldSession.ChatHandler/HandleTextEmoteOpcode, WorldSession.MiscHandler/HandleAreaTriggerOpcode, WorldSession.NPCHandler/SendTrainerList | — |
| GetPlayerPointer | method | — | World/LogChat, WorldSession.ChannelHandler/HandleChannelAnnouncementsOpcode, WorldSession.ChannelHandler/HandleChannelBanOpcode, WorldSession.ChannelHandler/HandleChannelInviteOpcode, WorldSession.ChannelHandler/HandleChannelKickOpcode, WorldSession.ChannelHandler/HandleChannelListOpcode, WorldSession.ChannelHandler/HandleChannelModerateOpcode, WorldSession.ChannelHandler/HandleChannelModeratorOpcode, WorldSession.ChannelHandler/HandleChannelMuteOpcode, WorldSession.ChannelHandler/HandleChannelOwnerOpcode, WorldSession.ChannelHandler/HandleChannelPasswordOpcode, WorldSession.ChannelHandler/HandleChannelSetOwnerOpcode, WorldSession.ChannelHandler/HandleChannelUnbanOpcode, WorldSession.ChannelHandler/HandleChannelUnmoderatorOpcode, WorldSession.ChannelHandler/HandleChannelUnmuteOpcode, WorldSession.ChannelHandler/HandleJoinChannelOpcode, WorldSession.ChannelHandler/HandleLeaveChannelOpcode, WorldSession.ChatHandler/HandleChatMessageOpcode | — |
| Handle_NULL | method | Log.Main/Out, Opcodes/LookupOpcodeName, WorldPacket/GetOpcode | — | — |
| Handle_EarlyProccess | method | Log.Main/Out, Opcodes/LookupOpcodeName, WorldPacket/GetOpcode | — | — |
| Handle_ServerSide | method | Log.Main/Out, Opcodes/LookupOpcodeName, WorldPacket/GetOpcode | — | — |
| SendAuthWaitQue | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#7, WorldPacket/WorldPacket#4 | World/RemoveQueuedSession, World/UpdateSessions | — |
| LoadGlobalAccountData | method | Database/PQuery | WorldSocket/_HandleAuthSession | account_data |
| LoadAccountData | method | AccountData/AccountData, Field/GetCppString, Field/GetUInt32, Field/GetUInt64, Log.Main/Out, QueryResult/Fetch, QueryResult/NextRow | WorldSession.CharacterHandler/HandlePlayerLogin | — |
| SetAccountData | method | Database/escape_string, Database/PExecute#2, ObjectGuid/GetCounter, ObjectGuid/operator! | WorldSession.MiscHandler/HandleUpdateAccountData | account_data, character_account_data |
| SendAccountDataTimes | method | AccountData/ConvertNewAccountDataToOld, ByteBuffer/append#5, Digest/size, Generator.MD5/ComputeFrom, Generator.MD5/CreateEmpty, WorldPacket/WorldPacket#4 | WorldSession.CharacterHandler/HandlePlayerLogin | — |
| LoadTutorialsData | method | Database/PQuery, Field/GetUInt32, QueryResult/Fetch, QueryResult/NextRow | WorldSocket/_HandleAuthSession | character_tutorial |
| SendTutorialsData | method | ByteBuffer/operator<<#10, WorldPacket/WorldPacket#4 | Player.Main/SendInitialPacketsBeforeAddToMap | — |
| SaveTutorialsData | method | Database/CreateStatement, SqlPreparedStatement/Execute#2, SqlStatement/addUInt32, SqlStatementID/SqlStatementID | Player.Main/SaveNewPlayer, Player.Main/SaveToDB | character_tutorial |
| GetTutorialInt | method | Errors/PrintStacktraceAndThrow | WorldSession.CharacterHandler/HandleTutorialFlagOpcode | — |
| ExecuteOpcode | method | Player.Main/IsHasDelayedTeleport, Player.Main/SetCanDelayTeleport | — | — |
| InitWarden | method | Anticheat/CreateWardenFor, Anticheat/GetAnticheatLib, Errors/PrintStacktraceAndThrow | World/AddSession_, World/RemoveQueuedSession, World/UpdateSessions | — |
| InitCheatData | method | Anticheat/CreateAnticheatFor, Anticheat/GetAnticheatLib, MovementAnticheat/InitNewPlayer | Player.Main/Player#5 | — |
| GetCheatData | method | Anticheat/CreateAnticheatFor, Anticheat/GetAnticheatLib | — | — |
| ProcessAnticheatAction | method | Database/PExecute#2, Player.Main/GetShortDescription, Player.Main/Player#2, World/BanAccount, World/SendGlobalText, World/SendGMText | Player.Main/ActivateTaxiPathTo, Player.Main/CanStoreItems, Player.Main/DestroyItem, Player.Main/OnDisconnected, Player.Main/SendLoot, Player.Main/SplitItem, Player.Main/SwapItem, Player.Main/Update, Player.Main/_CanStoreItem_InBag, Player.Main/_CanStoreItem_InInventorySlots, Player.Main/_CanStoreItem_InSpecificSlot, Player.Main/_LoadInventory, Player.Main/_SaveInventory, Spell.Main/ValidateExplicitTargetMask, WorldSession.AuctionHouseHandler/HandleAuctionSellItem, WorldSession.BattleGroundHandler/RequestBgJoinQueue, WorldSession.CharacterHandler/HandleCharCreateOpcode, WorldSession.CharacterHandler/HandlePlayerLogin, WorldSession.GuildHandler/HandleGuildAddRankOpcode, WorldSession.GuildHandler/HandleGuildChangeInfoTextOpcode, WorldSession.GuildHandler/HandleGuildCreateOpcode, WorldSession.GuildHandler/HandleGuildMOTDOpcode, WorldSession.GuildHandler/HandleGuildRankOpcode, WorldSession.GuildHandler/HandleGuildSetOfficerNoteOpcode, WorldSession.GuildHandler/HandleGuildSetPublicNoteOpcode, WorldSession.ItemHandler/HandleSwapInvItemOpcode, WorldSession.ItemHandler/HandleSwapItem, WorldSession.LootHandler/HandleLootOpcode, WorldSession.MailHandler/HandleMailMarkAsRead, WorldSession.MailHandler/HandleSendMail, WorldSession.MailHandler/HandleSendMailCallback, WorldSession.MiscHandler/HandleRequestAccountData, WorldSession.MiscHandler/HandleUpdateAccountData, WorldSession.SkillHandler/HandleUnlearnSkillOpcode | account |
| HasUsedClickToMove | method | Warden/HasUsedClickToMove | ChatHandler.LookupCommands/HandleListClickToMoveCommand | — |
| AllowPacket | method | Opcodes/LookupOpcodeName, World/getConfig#4 | — | — |
| CharacterScreenIdleKick | method | Log.Main/Out, World/getConfig#4 | — | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `account`: id int(11) unsigned PK, username varchar(32), gmlevel tinyint(3) unsigned, sessionkey longtext?, v longtext?, s longtext?, token_key varchar(100), email text?, joindate timestamp, last_ip varchar(30), failed_logins int(11) unsigned, locked tinyint(3) unsigned, lock_country varchar(2), last_login timestamp, online tinyint(4), expansion tinyint(3) unsigned, mutetime bigint(40), locale tinyint(3) unsigned, os varchar(4), platform varchar(4), current_realm tinyint(3) unsigned, flags int(10) unsigned, security varchar(255)?, email_verif tinyint(1), geolock_pin int(11)?
- `account_data`: account int(11) unsigned PK, type int(11) unsigned PK, time bigint(11) unsigned, data longblob
- `character_account_data`: guid int(11) unsigned PK, type int(11) unsigned PK, time bigint(11) unsigned, data longblob
- `character_tutorial`: account bigint(20) unsigned PK, tut0 int(11) unsigned, tut1 int(11) unsigned, tut2 int(11) unsigned, tut3 int(11) unsigned, tut4 int(11) unsigned, tut5 int(11) unsigned, tut6 int(11) unsigned, tut7 int(11) unsigned

*`?` = nullable, `PK` = primary key column.*


---

<!-- verify: boundary-bleed | foreign: GetGUID -->
