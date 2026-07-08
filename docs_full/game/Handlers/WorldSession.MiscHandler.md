<!-- provenance: failed-members, boundary-bleed -->
# WorldSession.MiscHandler

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# WorldSession.MiscHandler

## Purpose & Responsibilities

`WorldSession.MiscHandler` (implemented in `MiscHandler.cpp`) implements the packet handling logic for miscellaneous client-to-server opcodes within the `WorldSession` class. It manages a diverse set of player interactions that do not fall into dedicated subsystems like combat, questing, or trading. Key responsibilities include:

*   **Player Lifecycle Management:** Handling requests to respawn (`HandleRepopRequestOpcode`), logout (`HandleLogoutRequestOpcode`, `HandleLogoutCancelOpcode`), and reclaim corpses (`HandleReclaimCorpseOpcode`).
*   **Social Interactions:** Managing the Who List (`HandleWhoOpcode`), Friend/Ignore lists (`HandleAddFriendOpcode`, `HandleDelFriendOpcode`, etc.), and player inspection (`HandleInspectOpcode`, `HandleInspectHonorStatsOpcode`).
*   **World Interaction:** Processing area triggers for teleports, battleground entrances, and taverns (`HandleAreaTriggerOpcode`), zone updates (`HandleZoneUpdateOpcode`), and selection changes (`HandleSetSelectionOpcode`).
*   **Account & Configuration Data:** Synchronizing client-side account data (macros, talent trees, etc.) via compression/decompression (`HandleUpdateAccountData`, `HandleRequestAccountData`).
*   **Administrative & Debug Tools:** Providing handlers for admin-level commands like world teleportation (`HandleWorldTeleportOpcode`), raw position setting (`HandleMoveSetRawPosition`), and player lookups (`HandleWhoisOpcode`).
*   **UI State Synchronization:** Handling action button assignments (`HandleSetActionButtonOpcode`), cinematic completion (`HandleCompleteCinematic`), and stand state changes (`HandleStandStateChangeOpcode`).

This unit acts as a central dispatcher for these varied interactions, validating input, enforcing security checks (e.g., GM levels, combat states), and delegating complex logic to specialized managers (e.g., `SocialMgr`, `BattleGroundMgr`, `ObjectMgr`).

## Member-by-Member Behavior

### Player Lifecycle and Movement

**HandleRepopRequestOpcode**
Processes the client's request to respawn after death. It first validates that the player is dead and not already a ghost. A specific edge case handles race conditions where the server processes the repop request before the death state update propagates; if the player is in `JUST_DIED` state, it forces `KillPlayer()` to ensure consistency. It then builds the repop packet and schedules the graveyard resurrection.

**HandleLogoutRequestOpcode**
Initiates the logout sequence. It performs several checks to deny immediate logout:
1.  If the player is looting, it releases the loot via `WorldSession.LootHandler/DoLootRelease`.
2.  If in combat, jumping/falling, or frozen by a GM, it sends a rejection response with a specific reason code.
3.  If eligible for instant logout (resting, taxi flying, or high security level), it proceeds immediately.
4.  Otherwise, it roots the player, sets them to sit (if standing and not swimming/mounted), applies the "logging out" flag, and starts a 20-second countdown timer via `WorldSession.Main/LogoutRequest`.

**HandleLogoutCancelOpcode**
Cancels an ongoing logout request. It resets the logout timer via `WorldSession.Main/LogoutRequest`, unroots the player, restores their stand state if they were sitting, removes the stunned flag, and clears the "logging out" byte flag.

**HandleReclaimCorpseOpcode**
Allows a ghost player to reclaim their corpse. It verifies the player is a ghost, has a corpse, and is within `CORPSE_RECLAIM_RADIUS`. It enforces a delay based on `Player.Main/GetCorpseReclaimDelay` and prevents reclamation if the player is in a battleground that is not `STATUS_IN_PROGRESS` (preventing exploits where players die during preparation and resurrect inside the instance). Upon success, it resurrects the player via `Player.Main/ResurrectPlayer` and spawns the corpse bones via `Player.Main/SpawnCorpseBones`.

**HandleResurrectResponseOpcode**
Handles the player's acceptance or denial of a resurrection offer from another player. If accepted, it verifies the resurrector's GUID matches the pending request via `Player.Main/IsRessurectRequestedBy` and executes the resurrection via `Player.Main/ResurrectUsingRequestData`. If denied, it clears the resurrection request data via `Player.Main/ClearResurrectRequestData`.

### Social Systems

**HandleWhoOpcode**
Processes the `/who` command. Due to the computational cost of iterating all online players, this handler offloads the work to an asynchronous task (`WhoListClientQueryTask`). It validates input limits (max 10 zones, 4 search strings) and converts search terms to wide strings for case-insensitive comparison. It sets the received flag via `WorldSession.Main/SetReceivedWhoRequest` and dispatches the filtering logic to an async task via `World/AddAsyncTask`.

**operator()**
This is the execution body of the `WhoListClientQueryTask` functor, invoked asynchronously. It retrieves the session via `World/FindSession`, clears the received flag via `WorldSession.Main/SetReceivedWhoRequest`, and iterates through all online players obtained from `ObjectAccessor/GetPlayers`. It filters players based on team, security level, bot visibility settings, level range, class/race masks, name/guild substring matches, and zone proximity. It constructs the `SMSG_WHO` packet and sends it back to the session via `WorldSession.Main/SendPacket`.

**HandleAddFriendOpcode / HandleDelFriendOpcode**
Manages adding and removing friends. `HandleAddFriendOpcode` normalizes the name via `ObjectMgr/normalizePlayerName`, looks up the player's GUID via `ObjectMgr/GetPlayerDataByName`, checks for self-adds, cross-faction restrictions (unless configured otherwise or GM), and duplicate entries via `SocialMgr/HasFriend`. It determines if the friend is online/offline and adds them to the social list via `SocialMgr/AddToSocialList`. `HandleDelFriendOpcode` simply removes the GUID from the social list via `SocialMgr/RemoveFromSocialList`. Both send status updates via `SocialMgr/SendFriendStatus`.

**HandleAddIgnoreOpcode / HandleDelIgnoreOpcode**
Similar to friend management but for the ignore list. `HandleAddIgnoreOpcode` checks for self-ignores and duplicates via `SocialMgr/HasIgnore`. `HandleDelIgnoreOpcode` removes the entry via `SocialMgr/RemoveFromSocialList`.

**HandleFriendListOpcode**
Requests the current friend list from the `SocialMgr` associated with the `MasterPlayer` via `SocialMgr/SendFriendList`.

### World Interaction and Triggers

**HandleAreaTriggerOpcode**
The most complex handler in this unit, managing invisible zones that trigger events. It performs extensive validation:
1.  Ignores triggers if the player has the `IGNORE_TRIGGERS` cheat or is taxi flying.
2.  Verifies the trigger exists in DBC via `ObjectMgr/GetAreaTrigger` and the player is within range via `ObjectMgr/IsPointInAreaTriggerZone`.
3.  Executes scripts if defined via `Map.Main/StartAreaTriggerScript`.
4.  Handles quest-related triggers via `ObjectMgr/GetQuestForAreaTrigger`.
5.  Sets rest type for taverns via `ObjectMgr/IsTavernAreaTrigger`.
6.  Manages Battleground entrances via `ObjectMgr/GetBattlegroundEntranceTrigger`, checking level/team requirements and queuing the player via `WorldSession.BattleGroundHandler/SendBattleGroundList`.
7.  Delegates to active Battlegrounds via `BattleGround/HandleAreaTrigger` or ZoneScripts via `ZoneScript/HandleAreaTrigger#2` if applicable.
8.  Handles teleportation triggers via `ObjectMgr/GetAreaTriggerTeleport`, including dungeon entrances. It includes special legacy logic for Molten Core (Patch 1.2) to resurrect ghosts attempting to enter. It checks level requirements and conditions via `Conditions/IsConditionSatisfied`, displaying localized messages if access is denied.

**HandleZoneUpdateOpcode**
Updates the player's internal zone/area IDs based on server-side coordinates via `Player.Main/UpdateZone`. It also sends a stand state update packet to Mac clients if they are on a transport, to fix camera issues.

**HandleSetSelectionOpcode**
Updates the player's selected unit via `Player.Main/SetSelectionGuid`. It updates reputation visibility via `ReputationMgr/SetVisible#3` if the new target is friendly. It clears combo points for Rogues and Druids via `Player.Main/ClearComboPoints` if the target changes. It also updates the target of any active auto-repeat spells (like auto-shot) via `SpellCaster/GetCurrentSpell` and `SpellCastTargetsInfo/setUnitTarget`, canceling them via `Spell.Main/cancel` if the new target is invalid or out of range.

**HandleStandStateChangeOpcode**
Changes the player's animation state (stand, sit, sleep, kneel). It interrupts spells with channel flags that are canceled by animation changes via `SpellCaster/InterruptSpellsWithChannelFlags` and removes auras with similar interrupt flags via `Unit.Main/RemoveAurasWithInterruptFlags`.

### Account Data and UI

**HandleUpdateAccountData**
Receives compressed account data (macros, talents, etc.) from the client. It decompresses the data using `ZLib/Decompress`, validates the data type, and stores it via `WorldSession.Main/SetAccountData`. It logs anticheat actions via `WorldSession.Main/ProcessAnticheatAction` if invalid data types are received.

**HandleRequestAccountData**
Responds to client requests for account data. It retrieves the stored data via `WorldSession.Main/GetAccountData`, compresses it using `ZLib/Compress#2`, and sends it back via `WorldSession.Main/SendPacket`. If no data exists, it sends an empty packet.

**HandleSetActionButtonOpcode**
Assigns or removes actions (spells, items, macros) from action bars. It validates the action type and ensures the data is valid for the player via `Player.Main/IsActionButtonDataValid` before updating the `MasterPlayer`'s action buttons via `MasterPlayer.Main/addActionButton` or `MasterPlayer.Main/removeActionButton`.

**HandleCompleteCinematic**
Signals the end of a cinematic sequence, calling `Player.Main/CinematicEnd` on the player.

**HandleSetActionBarTogglesOpcode**
Updates the byte value controlling which action bars are visible/toggled on the client via `WorldObject.Object/SetByteValue`.

### Administrative and Debug

**HandleWhoisOpcode**
An admin-only command to look up account information for a player. It queries the `account` table via `Database/PQuery` for the username, email, and last IP address associated with the player's account ID and sends the result to the requesting GM via `WorldSession.Main/SendPacket`.

**HandleWorldTeleportOpcode**
Admin-only command to teleport to specific coordinates. It checks if the player is taxi flying (denying if so) and verifies admin security via `WorldSession.Main/GetSecurity`.

**HandleMoveSetRawPosition**
Admin-only command to set the player's raw position on the current map. Similar to world teleport, it denies execution if the player is not in the world or is taxi flying.

**HandleTeleportToUnitOpcode**
Delegates to the chat handler via `WorldSession.ChatHandler/SanitizeChatMessageAndProcessCommand` to execute the `.goname` command, effectively teleporting the GM to the specified player.

**HandleBugOpcode**
Logs bug reports or suggestions submitted by players to the debug log via `Log.Main/Out`.

**HandlePlayedTime**
Responds to the client's request for total and level-specific playtime statistics via `Player.Main/GetTotalPlayedTime` and `Player.Main/GetLevelPlayedTime`.

**HandleInspectOpcode / HandleInspectHonorStatsOpcode**
Handles player inspection. `HandleInspectOpcode` checks distance and hostility before allowing the inspect. `HandleInspectHonorStatsOpcode` retrieves detailed honor statistics (kills, contribution, rank) from the target player's `HonorMgr` via `HonorMgr/GetHighestRank` and sends them to the inspector.

**HandleFarSightOpcode**
Controls the "Far Sight" camera effect, allowing the player to view the world from another object's perspective via `Camera/SetView` and `Camera/ResetView`.

**HandleResetInstancesOpcode**
Resets all instances for the player or their group (if they are the leader) via `game_Group_Group/ResetInstances` or `Player.Main/ResetInstances`.

**HandleRequestPetInfoOpcode**
Initializes pet or charm spells via `Player.Main/PetSpellInitialize` or `Player.Main/CharmSpellInitialize` when the client requests pet info.

**HandleWardenDataOpcode**
Queues Warden (anticheat) data packets for processing.

**HandleLFGOpcode**
A stub handler that sends an empty `MSG_LOOKING_FOR_GROUP` packet, likely for compatibility with older client features.

**HandlePlayerLogoutOpcode / HandleNextCinematicCamera**
Empty stub handlers, likely reserved for future implementation or unused by the supported client versions.

## Cross-Unit Boundaries

*   **Player.Main:** Heavily relied upon for state management (`IsAlive`, `IsInCombat`, `GetCorpse`, `ResurrectPlayer`), data retrieval (`GetName`, `GetLevel`, `GetTeam`), and action execution (`KillPlayer`, `ScheduleRepopAtGraveyard`, `TeleportTo`).
*   **SocialMgr:** Used for all friend/ignore list operations (`AddToSocialList`, `RemoveFromSocialList`, `SendFriendStatus`).
*   **ObjectMgr:** Accessed for DBC lookups (`GetAreaTrigger`, `GetQuestTemplate`, `GetPlayerDataByName`), normalization of names, and retrieving player objects by GUID/name.
*   **World:** Used for configuration settings (`getConfig`) such as allowing two-sided who lists, showing bots, and instant logout thresholds. Also used to add async tasks (`AddAsyncTask`) for the Who list.
*   **BattleGroundMgr / BattleGround:** Used for battleground entrance logic, checking status, and handling triggers within active battlegrounds.
*   **GuildMgr:** Used to retrieve guild names for the Who list via `GuildMgr/GetGuildNameById`.
*   **AccountData:** Used for storing and retrieving compressed account data.
*   **ZLib:** Used for compression and decompression of account data packets.
*   **Log.Main:** Used extensively for debugging and error reporting.
*   **Errors:** Used for throwing exceptions in social list handlers if `MasterPlayer` is unexpectedly null.
*   **CombatBotBaseAI:** Called via `SendAreaTriggerPacket` in `HandleAreaTriggerOpcode`, indicating integration with a bot system for area trigger notifications.

## Data Model

This unit interacts with the following database table:

*   **`account`**: Accessed by `HandleWhoisOpcode` to retrieve `username`, `email`, and `last_ip` for a given account ID. This is an admin-only lookup.

No other tables are directly queried or modified by this unit. Most data (friends, ignores, account data) is managed in-memory by respective managers (`SocialMgr`, `WorldSession` members) and persisted elsewhere.

## Notable Implementation Details

*   **Async Who List:** The `HandleWhoOpcode` uses a custom functor `WhoListClientQueryTask` executed asynchronously via `sWorld.AddAsyncTask`. This prevents the main thread from blocking while iterating through all online players, which is critical for performance on populated servers. The task captures necessary state (accountId, filters) and accesses the global player map.
*   **Race Condition in Respawn:** `HandleRepopRequestOpcode` explicitly checks for `JUST_DIED` state to handle a race condition where the client sends a repop request before the server has fully processed the death event. This ensures the player is properly killed and scheduled for respawn.
*   **Battleground Exploit Prevention:** `HandleReclaimCorpseOpcode` checks `BattleGround::GetStatus()` to prevent players from dying during the preparation phase of a battleground and then reclaiming their corpse inside the instance once it starts.
*   **Legacy Dungeon Logic:** `HandleAreaTriggerOpcode` contains specific logic for Molten Core (Map ID 10, Trigger ID 1466) for Patch 1.2 and earlier, allowing ghosts to resurrect and teleport to Blackrock Depths instead of entering Molten Core. This reflects historical game mechanics.
*   **Compression:** Account data is compressed using ZLib before transmission and storage to reduce bandwidth and memory usage. The code handles both old and new account data types for backward compatibility.
*   **Security Checks:** Many handlers enforce security levels (e.g., `SEC_ADMINISTRATOR` for teleport commands, `SEC_MODERATOR` for cross-faction friend adds).
*   **Mac Client Workaround:** `HandleZoneUpdateOpcode` includes a specific check for Mac clients on transports to send a stand state update, fixing a known camera bug.

## Member Reference

**HandleRepopRequestOpcode**: Processes respawn requests, handling race conditions where the player is `JUST_DIED` by forcing `KillPlayer()`, then scheduling graveyard respawn.
**operator()**: (Internal functor `WhoListClientQueryTask`) Iterates online players, applying filters (team, level, class, race, name, guild, zone) to build the Who list packet, then sends it to the session.
**HandleWhoOpcode**: Validates Who request parameters, converts search terms, and dispatches the filtering logic to an async task.
**HandleLFGOpcode**: Sends an empty Looking For Group packet, likely for client compatibility.
**HandleLogoutRequestOpcode**: Initiates logout, checking for combat/jumping/frozen states to reject, or proceeding instantly if resting/taxi/admin, otherwise rooting the player and starting a countdown.
**HandlePlayerLogoutOpcode**: Empty stub handler.
**HandleLogoutCancelOpcode**: Cancels logout, unrooting the player and restoring their stand state and flags.
**HandleTogglePvP**: Toggles or sets the PvP desired flag and updates the player's PvP state.
**HandleZoneUpdateOpcode**: Updates player zone/area IDs and sends a stand state update to Mac clients on transports.
**HandleSetSelectionOpcode**: Updates selected unit, reputation visibility, combo points (for Rogues/Druids), and auto-shot targets.
**HandleStandStateChangeOpcode**: Changes player animation state, interrupting relevant spells and auras.
**HandleFriendListOpcode**: Requests the friend list from the SocialMgr.
**HandleAddFriendOpcode**: Adds a friend, checking for self-adds, cross-faction rules, and duplicates, then notifying the client.
**HandleDelFriendOpcode**: Removes a friend from the social list.
**HandleAddIgnoreOpcode**: Adds a player to the ignore list, checking for self-ignores and duplicates.
**HandleDelIgnoreOpcode**: Removes a player from the ignore list.
**HandleBugOpcode**: Logs player-submitted bug reports or suggestions.
**HandleReclaimCorpseOpcode**: Allows ghost players to reclaim their corpse, enforcing distance, delay, and battleground status checks.
**HandleResurrectResponseOpcode**: Accepts or denies a resurrection offer from another player.
**HandleAreaTriggerOpcode**: Handles area triggers for quests, taverns, battleground entrances, and teleports, including legacy Molten Core logic and level/condition checks.
**HandleUpdateAccountData**: Decompresses and stores account data received from the client.
**HandleRequestAccountData**: Compresses and sends requested account data to the client.
**HandleSetActionButtonOpcode**: Assigns or removes actions from action bars, validating the action type.
**HandleCompleteCinematic**: Signals the end of a cinematic sequence.
**HandleNextCinematicCamera**: Empty stub handler.
**HandleSetActionBarTogglesOpcode**: Updates the byte value controlling visible action bars.
**HandlePlayedTime**: Sends total and level-specific playtime stats to the client.
**HandleInspectOpcode**: Allows inspecting a nearby, non-hostile player.
**HandleInspectHonorStatsOpcode**: Sends detailed honor statistics of a nearby, non-hostile player.
**HandleTeleportToUnitOpcode**: Delegates to the chat handler to execute the `.goname` command.
**HandleWorldTeleportOpcode**: Admin-only teleport to specific coordinates, denying if taxi flying.
**HandleMoveSetRawPosition**: Admin-only setting of raw position on the current map, denying if not in world or taxi flying.
**HandleWhoisOpcode**: Admin-only lookup of account username, email, and last IP for a given player name.
**HandleFarSightOpcode**: Controls the Far Sight camera effect, attaching or detaching the view from an object.
**HandleResetInstancesOpcode**: Resets all instances for the player or their group.
**HandleRequestPetInfoOpcode**: Initializes pet or charm spells.
**HandleWardenDataOpcode**: Queues Warden anticheat data packets.

---

<!-- machine-true, projected from graph.json -->

## Map — WorldSession.MiscHandler

*Source:* MiscHandler.cpp, WorldSession.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| HandleRepopRequestOpcode | method | Log.Main/Out, Object/GetGUIDLow, Object/HasFlag, Player.Main/BuildPlayerRepop, Player.Main/GetName, Player.Main/KillPlayer, Player.Main/ScheduleRepopAtGraveyard, Unit.Main/GetDeathState, Unit.Main/IsAlive | — | — |
| operator() | method | AreaEntry/GetById, ByteBuffer/operator<<, ByteBuffer/operator<<#10, GuildMgr/GetGuildNameById, Object/IsInWorld, ObjectAccessor/GetPlayers, ObjectMgr/GetAreaLocaleString, Player.Main/GetCachedZoneId, Player.Main/GetGuildId, Player.Main/GetName, Player.Main/GetSession, Player.Main/GetTeam, Player.Main/IsBot, Player.Main/IsVisibleGloballyFor, shared_Util/Utf8FitTo, shared_Util/Utf8toWStr, shared_Util/wstrToLower, Unit.Main/GetClass, Unit.Main/GetLevel, Unit.Main/GetRace, World/FindSession, World/getConfig, World/getConfig#4, WorldObject.Object/GetInstanceId, WorldPacket/WorldPacket#4, WorldSession.Main/GetPlayer, WorldSession.Main/GetSecurity, WorldSession.Main/GetSessionDbLocaleIndex, WorldSession.Main/SendPacket, WorldSession.Main/SetReceivedWhoRequest | — | — |
| HandleWhoOpcode | method | Log.Main/Out, shared_Util/Utf8toWStr, shared_Util/wstrToLower, World/AddAsyncTask, WorldSession.Main/GetAccountId, WorldSession.Main/ReceivedWhoRequest, WorldSession.Main/SetReceivedWhoRequest | — | — |
| HandleLFGOpcode | method | ByteBuffer/operator<<#10, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| HandleLogoutRequestOpcode | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#7, MovementInfo/HasMovementFlag, Object/ApplyModByteFlag, Object/HasFlag, Player.Main/GetLootGuid, Unit.Main/CanFreeMove, Unit.Main/GetStandState, Unit.Main/HasAura#2, Unit.Main/IsInCombat, Unit.Main/IsMounted, Unit.Main/IsTaxiFlying, Unit.Main/SetRooted, Unit.Main/SetStandState, World/getConfig#4, WorldObject.Object/GetUnitMovementFlags, WorldObject.Object/SetFlag, WorldPacket/WorldPacket#4, WorldSession.LootHandler/DoLootRelease, WorldSession.Main/GetPlayer, WorldSession.Main/GetSecurity, WorldSession.Main/LogoutPlayer, WorldSession.Main/LogoutRequest, WorldSession.Main/SendPacket | — | — |
| HandlePlayerLogoutOpcode | method | — | — | — |
| HandleLogoutCancelOpcode | method | Object/ApplyModByteFlag, Unit.Main/CanFreeMove, Unit.Main/GetStandState, Unit.Main/SetRooted, Unit.Main/SetStandState, WorldObject.Object/RemoveFlag, WorldPacket/WorldPacket#4, WorldSession.Main/GetPlayer, WorldSession.Main/LogoutRequest, WorldSession.Main/SendPacket | — | — |
| HandleTogglePvP | method | Object/ApplyModFlag, Object/HasFlag, Object/ToggleFlag, Player.Main/UpdatePvP, WorldSession.Main/GetPlayer | — | — |
| HandleZoneUpdateOpcode | method | ByteBuffer/operator<<#7, MovementInfo/HasMovementFlag, Player.Main/GetSession, Player.Main/UpdateZone, Unit.Main/GetStandState, WorldObject.Object/GetZoneAndAreaId, WorldPacket/WorldPacket#4, WorldSession.Main/GetPlayer, WorldSession.Main/SendPacket | — | — |
| HandleSetSelectionOpcode | method | FactionTemplateEntry/IsHostileToPlayerTeam, Object/IsInWorld, ObjectAccessor/GetUnit, ObjectGuid/operator!=, Player.Main/ClearComboPoints, Player.Main/GetComboTargetGuid, Player.Main/GetReputationMgr, Player.Main/SetSelectionGuid, ReputationMgr/SetVisible#3, Spell.Main/cancel, SpellCaster/GetCurrentSpell, SpellCastTargetsInfo/setUnitTarget, Unit.Main/GetClass, WorldObject.Object/GetFactionTemplateEntry, WorldObject.Object/GetMap, WorldObject.Object/IsValidAttackTarget | — | — |
| HandleStandStateChangeOpcode | method | Object/HasFlag, SpellCaster/InterruptSpellsWithChannelFlags, Unit.Main/RemoveAurasWithInterruptFlags, Unit.Main/SetStandState | — | — |
| HandleFriendListOpcode | method | Errors/PrintStacktraceAndThrow, MasterPlayer.Main/GetSocial, SocialMgr/SendFriendList, WorldSession.Main/GetMasterPlayer | — | — |
| HandleAddFriendOpcode | method | Errors/PrintStacktraceAndThrow, Log.Main/Out, MasterPlayer.Main/GetName, MasterPlayer.Main/GetObjectGuid, MasterPlayer.Main/GetSocial, MasterPlayer.Main/GetTeam, MasterPlayer.Main/IsVisibleGloballyFor, ObjectAccessor/FindMasterPlayer, ObjectGuid/ObjectGuid#2, ObjectGuid/operator==, ObjectMgr/GetPlayerDataByName, ObjectMgr/normalizePlayerName, Player.Main/TeamForRace, SocialMgr/AddToSocialList, SocialMgr/HasFriend, SocialMgr/SendFriendStatus, World/getConfig, WorldSession.Main/GetMasterPlayer, WorldSession.Main/GetSecurity | — | — |
| HandleDelFriendOpcode | method | Errors/PrintStacktraceAndThrow, MasterPlayer.Main/GetSocial, SocialMgr/RemoveFromSocialList, SocialMgr/SendFriendStatus, WorldSession.Main/GetMasterPlayer | — | — |
| HandleAddIgnoreOpcode | method | Errors/PrintStacktraceAndThrow, Log.Main/Out, MasterPlayer.Main/GetName, MasterPlayer.Main/GetObjectGuid, MasterPlayer.Main/GetSocial, ObjectGuid/ObjectGuid#2, ObjectGuid/operator==, ObjectMgr/GetPlayerDataByName, ObjectMgr/normalizePlayerName, SocialMgr/AddToSocialList, SocialMgr/HasIgnore, SocialMgr/SendFriendStatus, WorldSession.Main/GetMasterPlayer | — | — |
| HandleDelIgnoreOpcode | method | Errors/PrintStacktraceAndThrow, MasterPlayer.Main/GetSocial, SocialMgr/RemoveFromSocialList, SocialMgr/SendFriendStatus, WorldSession.Main/GetMasterPlayer | — | — |
| HandleBugOpcode | method | Log.Main/Out, Player.Main/GetName, WorldSession.Main/GetAccountId, WorldSession.Main/GetPlayer | — | — |
| HandleReclaimCorpseOpcode | method | BattleGround/GetStatus, Corpse/GetGhostTime, Corpse/GetType, Object/HasFlag, Player.Main/GetBattleGround, Player.Main/GetCorpse, Player.Main/GetCorpseReclaimDelay, Player.Main/InBattleGround, Player.Main/ResurrectPlayer, Player.Main/SpawnCorpseBones, Unit.Main/IsAlive, WorldObject.Object/IsWithinDistInMap, WorldSession.Main/GetPlayer | — | — |
| HandleResurrectResponseOpcode | method | Player.Main/ClearResurrectRequestData, Player.Main/IsRessurectRequestedBy, Player.Main/ResurrectUsingRequestData, Unit.Main/IsAlive, WorldSession.Main/GetPlayer | — | — |
| HandleAreaTriggerOpcode | method | BattleGround/GetMaxLevel, BattleGround/GetMinLevel, BattleGround/HandleAreaTrigger, BattleGroundMgr/GetBattleGroundTemplate, Conditions/IsConditionSatisfied, Log.Main/Out, Map.Main/StartAreaTriggerScript, MapEntry/IsDungeon, Object/GetGUIDLow, Object/GetObjectGuid, ObjectMgr/GetAreaTrigger, ObjectMgr/GetAreaTriggerLocale, ObjectMgr/GetAreaTriggerTeleport, ObjectMgr/GetBattlegroundEntranceTrigger, ObjectMgr/GetMapEntranceTrigger, ObjectMgr/GetQuestForAreaTrigger, ObjectMgr/GetQuestTemplate, ObjectMgr/IsPointInAreaTriggerZone, ObjectMgr/IsTavernAreaTrigger, Player.Main/AreaExploredOrEventHappens, Player.Main/GetBattleGround, Player.Main/GetCorpse, Player.Main/GetName, Player.Main/GetQuestStatus, Player.Main/GetRestType, Player.Main/GetSession, Player.Main/GetTeam, Player.Main/GetZoneScript, Player.Main/HasCheatOption, Player.Main/InBattleGround, Player.Main/IsActiveQuest, Player.Main/IsGameMaster, Player.Main/ResurrectPlayer, Player.Main/SetBattleGroundEntryPoint#2, Player.Main/SetRestType, Player.Main/SpawnCorpseBones, Player.Main/TeleportTo, Unit.Main/GetLevel, Unit.Main/IsAlive, Unit.Main/IsTaxiFlying, World/getConfig, World/GetWowPatch, WorldObject.Object/GetMap, WorldObject.Object/GetMapId, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldSession.BattleGroundHandler/SendBattleGroundList, WorldSession.Main/GetMangosString, WorldSession.Main/GetPlayer, WorldSession.Main/GetSessionDbLocaleIndex, WorldSession.Main/SendAreaTriggerMessage, ZoneScript/HandleAreaTrigger#2 | CombatBotBaseAI/SendAreaTriggerPacket | — |
| HandleUpdateAccountData | method | AccountData/ConvertOldAccountDataToNew, Log.Main/Out, WorldSession.Main/GetGameBuild, WorldSession.Main/ProcessAnticheatAction, WorldSession.Main/SetAccountData, ZLib/Decompress | — | — |
| HandleRequestAccountData | method | AccountData/ConvertOldAccountDataToNew, ByteBuffer/append#2, ByteBuffer/operator<<#10, Log.Main/Out, WorldPacket/WorldPacket#4, WorldSession.Main/GetAccountData, WorldSession.Main/GetGameBuild, WorldSession.Main/ProcessAnticheatAction, WorldSession.Main/SendPacket, ZLib/Compress#2 | — | — |
| HandleSetActionButtonOpcode | method | Log.Main/Out, MasterPlayer.Main/addActionButton, MasterPlayer.Main/removeActionButton, Player.Main/IsActionButtonDataValid, WorldSession.Main/GetMasterPlayer, WorldSession.Main/GetPlayer | — | — |
| HandleCompleteCinematic | method | Player.Main/CinematicEnd, WorldSession.Main/GetPlayer | — | — |
| HandleNextCinematicCamera | method | — | — | — |
| HandleSetActionBarTogglesOpcode | method | Log.Main/Out, WorldObject.Object/SetByteValue, WorldSession.Main/GetPlayer | — | — |
| HandlePlayedTime | method | ByteBuffer/operator<<#10, Player.Main/GetLevelPlayedTime, Player.Main/GetTotalPlayedTime, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| HandleInspectOpcode | method | ObjectGuid/operator<<, ObjectMgr/GetPlayer, Player.Main/SetSelectionGuid, WorldObject.Object/GetDistance3dToCenter#3, WorldObject.Object/IsValidAttackTarget, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| HandleInspectHonorStatsOpcode | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#13, ByteBuffer/operator<<#7, HonorMgr/GetHighestRank, Object/GetByteValue, Object/GetUInt16Value, Object/GetUInt32Value, ObjectGuid/operator<<, ObjectMgr/GetPlayer, Player.Main/GetHonorMgr, WorldObject.Object/GetDistance3dToCenter#3, WorldObject.Object/IsValidAttackTarget, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| HandleTeleportToUnitOpcode | method | WorldSession.ChatHandler/SanitizeChatMessageAndProcessCommand | — | — |
| HandleWorldTeleportOpcode | method | Log.Main/Out, Object/GetGUIDLow, Player.Main/GetName, Unit.Main/IsTaxiFlying, WorldSession.Main/GetPlayer, WorldSession.Main/GetSecurity, WorldSession.Main/SendNotification#2 | — | — |
| HandleMoveSetRawPosition | method | Log.Main/Out, Object/GetGUIDLow, Object/IsInWorld, Player.Main/GetName, Unit.Main/IsTaxiFlying, WorldSession.Main/GetPlayer, WorldSession.Main/GetSecurity, WorldSession.Main/SendNotification#2 | — | — |
| HandleWhoisOpcode | method | ByteBuffer/operator<<, Database/PQuery, Field/GetCppString, ObjectMgr/GetPlayer#2, ObjectMgr/normalizePlayerName, Player.Main/GetSession, QueryResult/Fetch, WorldPacket/WorldPacket#4, WorldSession.Main/GetAccountId, WorldSession.Main/GetSecurity, WorldSession.Main/SendNotification#2, WorldSession.Main/SendPacket | — | account |
| HandleFarSightOpcode | method | Camera/ResetView, Camera/SetView, Log.Main/Out, Map.Main/GetWorldObject, Object/GetGuidStr, ObjectGuid/GetString, Player.Main/GetCamera, Player.Main/GetFarSightGuid, WorldObject.Object/GetMap | — | — |
| HandleResetInstancesOpcode | method | game_Group_Group/ResetInstances, Group/IsLeader, Object/GetObjectGuid, Player.Main/GetGroup, Player.Main/ResetInstances | — | — |
| HandleRequestPetInfoOpcode | method | Player.Main/CharmSpellInitialize, Player.Main/PetSpellInitialize, Unit.Main/GetCharm, Unit.Main/GetPet | — | — |
| HandleWardenDataOpcode | method | Player.Main/Player#3, WorldSession.Main/GetAccountId | — | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `account`: id int(11) unsigned PK, username varchar(32), gmlevel tinyint(3) unsigned, sessionkey longtext?, v longtext?, s longtext?, token_key varchar(100), email text?, joindate timestamp, last_ip varchar(30), failed_logins int(11) unsigned, locked tinyint(3) unsigned, lock_country varchar(2), last_login timestamp, online tinyint(4), expansion tinyint(3) unsigned, mutetime bigint(40), locale tinyint(3) unsigned, os varchar(4), platform varchar(4), current_realm tinyint(3) unsigned, flags int(10) unsigned, security varchar(255)?, email_verif tinyint(1), geolock_pin int(11)?

*`?` = nullable, `PK` = primary key column.*


---

<!-- verify: failed-members | invented: operator -->

---

<!-- verify: boundary-bleed | foreign: update, WorldSession -->
