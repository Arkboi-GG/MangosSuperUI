<!-- provenance: boundary-bleed -->
# WorldSession.BattleGroundHandler

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# WorldSession.BattleGroundHandler

## Purpose & Responsibilities

`WorldSession.BattleGroundHandler` constitutes the network-facing interface for all Battleground (BG) interactions within the `wowvmangos` server. It resides in `BattleGroundHandler.cpp` and implements methods declared in the `WorldSession` class (`WorldSession.h`). Its primary responsibility is to parse incoming client packets related to PvP arenas and battlegrounds, validate the player's eligibility and state, and coordinate with the `BattleGroundMgr` singleton to manage queueing, teleportation, and status updates.

This unit acts as a gatekeeper: it ensures that players meet level requirements, are not currently in combat (when leaving), are not flagged as deserters, and are interacting with valid NPCs or portals before allowing them to join or leave queues. It handles both solo and group queueing logic, including complex validation for premade groups. Additionally, it manages specific battleground mechanics such as Warflag carrier position broadcasting (for Warsong Gulch) and spirit healer interactions within battleground instances.

The handler does not maintain persistent state itself; instead, it relies on `Player` objects for individual queue status and `BattleGroundMgr` for global queue management and instance creation.

## Member-by-Member Behavior

### Queueing and Joining Logic

**`HandleBattlemasterHelloOpcode`**
This method processes the initial interaction when a player clicks on a Battlemaster NPC. It retrieves the `Creature` object from the map using the provided GUID. It validates that the creature is indeed a Battlemaster (`Unit.Main/IsBattleMaster`). If valid, it pauses the NPC's movement if configured to do so. It determines the associated `BattleGroundTypeId` via `BattleGroundMgr/GetBattleMasterBG`. Before proceeding, it checks if the player meets the level requirement for that specific battleground type using `Player.Main/GetBGAccessByLevel`. If the player is ineligible, a notification is sent. Finally, it interrupts any channeling spells on the player and delegates the actual list generation to `SendBattleGroundList`.

**`SendBattleGroundList`**
A helper method that constructs the response packet for the Battlemaster hello. It calls `BattleGroundMgr/BuildBattleGroundListPacket` to populate a `WorldPacket` with the available battleground options for the player and sends it back to the client.

**`HandleBattlefieldJoinOpcode`**
Handles the generic "Join Battlefield" request, often triggered by clicking a portal or a general join button. It extracts the map ID from the packet and delegates the actual queueing logic to `RequestBgJoinQueue`, passing an empty `ObjectGuid` for the battlemaster, indicating this is likely a portal-based join.

**`HandleBattlemasterJoinOpcode`**
Specifically handles the join request initiated by interacting with a Battlemaster NPC. It extracts the Battlemaster's GUID, instance ID, map ID, and whether the player intends to join as a group. It then delegates to `RequestBgJoinQueue`. Note: This method is only compiled for client builds newer than 1.6.1.

**`RequestBgJoinQueue`**
This is the core logic engine for joining battleground queues. It performs extensive validation:
1.  **Anti-Cheat Checks:** It verifies the validity of the `BattleGroundTypeId`. It flags attempts to queue for invalid types or to queue for Arathi Basin (AV) as a group (which is typically disallowed or handled differently) as passive anti-cheat violations.
2.  **Location Validation:** It distinguishes between joining via a portal (`queuedAtBGPortal`) and via an NPC. For portals, it checks if the player is within 50 yards of the registered entry point. For NPCs, it verifies the player can interact with the specified Battlemaster.
3.  **State Checks:** It prevents players already in a battleground from joining a queue.
4.  **Instance Retrieval:** It attempts to find an existing battleground instance or falls back to the template.
5.  **Solo Queueing:** If joining alone, it checks for the "Deserter" debuff (`Player.Main/CanJoinToBattleground`), ensures the player isn't already in the queue, and verifies free queue slots. If valid, it adds the player to the `BattleGroundQueue` via `BattleGroundMgr/AddGroup`, calculates average wait time, and sends the status packet.
6.  **Group Queueing:** If joining as a group, it retrieves the player's `Group` object. It calls `game_Group_Group/CanJoinBattleGroundQueue` to validate all members (checking for deserters, mixed factions, etc.). It determines if the group qualifies as "premade" based on server configuration. It iterates through group members, adding each to the queue individually, handling exclusions (members who failed validation), and sending appropriate status packets to each member.
7.  **Queue Update:** Finally, it schedules a queue update via `BattleGroundMgr/ScheduleQueueUpdate`.

**`SendBattleGroundJoinError`**
A utility method that translates internal error codes (e.g., `BG_JOIN_ERR_GROUP_TOO_LARGE`) into localized chat messages using `ChatHandler.Chat/BuildChatPacket` and sends them to the client.

### In-Battleground Operations

**`HandleBattleGroundPlayerPositionsOpcode`**
Responds to the client's request for teammate positions. It retrieves the current `BattleGround` object. If the player is not in a raid group matching the BG's raid setup, it iterates through all players in the battleground, filtering by team, and appends their GUIDs and X/Y coordinates to the response packet. For Warsong Gulch (`BATTLEGROUND_WS`), it additionally identifies the flag carrier for the opposing team (using `BattleGroundWS/GetHordeFlagPickerGuid` or `GetAllianceFlagPickerGuid`) and includes their position if they exist.

**`HandlePVPLogDataOpcode`**
Requests PvP statistics. If the battleground is still in progress (`STATUS_WAIT_LEAVE` is false), it requests the standard PVP log packet from `BattleGroundMgr`. If the battleground has ended, it sends the final score packet directly from the `BattleGround` object.

**`HandleLeaveBattlefieldOpcode`**
Processes the request to leave a battleground. It first validates that the player is on the correct map (for newer clients). Crucially, it prevents leaving if the player is in combat, unless the battleground status is `STATUS_WAIT_LEAVE` (indicating the match has concluded). If valid, it calls `Player.Main/LeaveBattleground`.

**`HandleAreaSpiritHealerQueryOpcode`**
Handles the query for spirit healers within a battleground. It verifies the player is in a battleground, retrieves the target `Creature`, confirms it is a spirit service NPC (`Unit.Main/IsSpiritService`), and delegates the response to `Creature.Main/SendAreaSpiritHealerQueryOpcode`.

**`HandleAreaSpiritHealerQueueOpcode`**
Handles the interaction with a spirit healer. Similar to the query, it validates the context and the NPC type, then triggers the gossip script via `ScriptMgr/OnGossipHello`.

### Status and Teleportation

**`HandleBattlefieldListOpcode`**
Requests the list of available battlegrounds. It determines the `BattleGroundTypeId` either from the packet (newer clients) or the player's queued battleground (older clients). It validates the type and delegates packet construction to `BattleGroundMgr/BuildBattleGroundListPacket`.

**`HandleBattleFieldPortOpcode`**
Manages the transition between the queue and the battleground instance. The `action` field indicates whether the player wants to enter (1) or leave (0) the queue.
1.  **Validation:** It checks if the player is in a queue and retrieves their `GroupQueueInfo`.
2.  **Entering (Action 1):**
    *   Checks for the Deserter debuff. If present, it forces the action to "leave queue" (0) and notifies the player.
    *   Checks if the player's level exceeds the battleground's max level (can happen if leveling while queuing). If so, it forces "leave queue".
    *   Checks if the battleground has already ended (`STATUS_WAIT_LEAVE`). If so, it forces "leave queue".
    *   If valid, it resurrects the player if dead, cancels any taxi flight, removes the player from the queue manager, sets the player's battleground ID and team, and teleports them via `BattleGroundMgr/SendToBattleGround`.
3.  **Leaving (Action 0):**
    *   Removes the queue ID from the player.
    *   Sends a status packet indicating no active queue.
    *   Removes the player from the `BattleGroundQueue` and schedules a queue update.

**`HandleBattlefieldStatusOpcode`**
Periodically updates the client on queue status. It iterates through all possible queue slots for the player. For each active queue:
*   If the player is already in a battleground instance, it sends the in-progress status with end/start times.
*   If the player is invited to an instance, it calculates the remaining time until the invite expires and sends the "wait to join" status.
*   If the player is simply in the queue, it calculates the average wait time and time spent in queue, sending the "wait in queue" status.

## Cross-Unit Boundaries

This unit relies heavily on `BattleGroundMgr` for state management and packet construction. It interacts with `Player` for individual state checks and modifications. It uses `Creature` and `Map` for spatial and entity validation.

*   **`BattleGroundMgr`**: The central authority for battleground logic. `WorldSession.BattleGroundHandler` calls `BattleGroundMgr` to:
    *   Resolve Battlemaster NPCs to BG types (`GetBattleMasterBG`).
    *   Construct network packets (`BuildBattleGroundListPacket`, `BuildBattleGroundStatusPacket`, `BuildGroupJoinedBattlegroundPacket`, `BuildPvpLogDataPacket`).
    *   Manage queues (`AddGroup`, `GetPlayerGroupInfoData`, `RemovePlayer`, `ScheduleQueueUpdate`).
    *   Retrieve instances/templates (`GetBattleGround`, `GetBattleGroundTemplate`, `GetBattleGroundThroughClientInstance`).
    *   Teleport players (`SendToBattleGround`).
*   **`Player`**: Represents the local user. The handler calls `Player` methods to:
    *   Check eligibility (`GetBGAccessByLevel`, `CanJoinToBattleground`, `HasFreeBattleGroundQueueId`).
    *   Manage queue state (`AddBattleGroundQueueId`, `RemoveBattleGroundQueueId`, `GetBattleGroundQueueIndex`).
    *   Modify state (`SetBattleGroundEntryPoint`, `SetBattleGroundId`, `SetBGTeam`, `ResurrectPlayer`, `LeaveBattleground`).
    *   Retrieve context (`GetGroup`, `GetTeam`, `GetMapId`, `IsInCombat`).
*   **`Creature` / `Map`**: Used to validate NPC interactions. `HandleBattlemasterHelloOpcode` and `HandleAreaSpiritHealerQueryOpcode` retrieve `Creature` objects from the `Map` to verify flags (`IsBattleMaster`, `IsSpiritService`) and pause movement.
*   **`Group`**: Used in `RequestBgJoinQueue` to validate group composition (`CanJoinBattleGroundQueue`) and iterate members.
*   **`ChatHandler`**: Used in `SendBattleGroundJoinError` to format error messages into chat packets.
*   **`ScriptMgr`**: Used in `HandleAreaSpiritHealerQueueOpcode` to trigger gossip scripts.
*   **`CombatBotBaseAI`**: The MAP indicates `CombatBotBaseAI/SendBattlemasterJoinPacket` and `CombatBotBaseAI/SendBattlefieldPortPacket` call into this unit. This suggests bot logic mimics client packets to join/port, utilizing these handlers as the standard entry points.

## Data Model

This unit does not directly access database tables. All data operations are performed in-memory via the `BattleGroundMgr`, `Player`, and `Group` objects. Any persistence related to battlegrounds (such as queue states or instance data) is managed by those respective units or the `BattleGround` base class, not by this handler.

## Notable Implementation Details

1.  **Anti-Cheat Integration**: `RequestBgJoinQueue` contains explicit anti-cheat checks. It logs and reports attempts to queue for invalid BG types, queue for AV as a group, or queue from out-of-range portals/invalid NPCs. This is done via `WorldSession.Main/ProcessAnticheatAction`.
2.  **Deserter Debuff Handling**: The code explicitly checks for the "Deserter" debuff in multiple places (`RequestBgJoinQueue`, `HandleBattleFieldPortOpcode`). If a player has this debuff, they are prevented from joining or entering, and often forced out of the queue with a specific error packet (`SMSG_GROUP_JOINED_BATTLEGROUND` with value `0xFFFFFFFE`).
3.  **Level-Up Edge Case**: In `HandleBattleFieldPortOpcode`, there is a specific check for players who may have leveled up while waiting in the queue. If the player's current level exceeds the battleground's `GetMaxLevel()`, they are denied entry and removed from the queue. This prevents players from entering a bracket they no longer qualify for.
4.  **Warsong Gulch Specifics**: `HandleBattleGroundPlayerPositionsOpcode` has special-case logic for `BATTLEGROUND_WS`. It broadcasts the position of the opposing team's flag carrier, which is critical for the game mode's mechanics. Other battlegrounds do not include this data.
5.  **Client Version Compatibility**: Several methods (`HandleBattlemasterJoinOpcode`, parts of `RequestBgJoinQueue`, `HandleBattlefieldListOpcode`, `HandleBattleFieldPortOpcode`) contain `#if SUPPORTED_CLIENT_BUILD` directives. This indicates significant differences in packet structure and logic between older (1.6.1, 1.8.4) and newer client versions. For example, older clients rely on `_player->GetQueuedBattleground()` to determine the BG type, while newer clients pass the `mapId` explicitly.
6.  **Group Queue Exclusions**: When a group joins, `RequestBgJoinQueue` allows for partial success. If some members fail validation (e.g., deserters), they are added to an `excludedMembers` list. The loop iterating through group members skips these excluded players, sending them a failure packet while successfully queuing the rest of the group.
7.  **Queue Slot Management**: The code carefully manages `PLAYER_MAX_BATTLEGROUND_QUEUES`. It checks for free slots before adding and removes slots explicitly when leaving. The comment in `HandleBattleFieldPortOpcode` notes that `RemoveBattleGroundQueueId` must be called in a specific order to avoid bugs, highlighting a fragile dependency in the queue management logic.
8.  **Spirit Healer Gossip**: Instead of implementing the resurrection logic directly, `HandleAreaSpiritHealerQueueOpcode` delegates to `ScriptMgr/OnGossipHello`. This implies that the actual resurrection mechanics are handled by scripted gossip menus attached to the spirit healer NPCs, allowing for flexibility in scripting.

## Member Reference

**HandleBattlemasterHelloOpcode**: Processes the initial click on a Battlemaster NPC. Validates the NPC, checks player level eligibility, interrupts channeling spells, and triggers `SendBattleGroundList`.

**SendBattleGroundList**: Constructs and sends the battleground list packet to the client using `BattleGroundMgr/BuildBattleGroundListPacket`.

**HandleBattlefieldJoinOpcode**: Handles generic join requests (e.g., from portals). Delegates to `RequestBgJoinQueue` with an empty battlemaster GUID.

**HandleBattlemasterJoinOpcode**: Handles join requests from Battlemaster NPCs. Extracts NPC GUID and group intent, then delegates to `RequestBgJoinQueue`. (Client build > 1.6.1).

**RequestBgJoinQueue**: Core queueing logic. Validates anti-cheat rules, location, and player/group eligibility. Adds solo players or entire groups to the `BattleGroundQueue`, handling errors and exclusions, and schedules queue updates.

**HandleBattleGroundPlayerPositionsOpcode**: Broadcasts teammate positions. Includes special logic for Warsong Gulch to broadcast the opposing flag carrier's position.

**HandlePVPLogDataOpcode**: Requests PvP stats. Sends standard log data if the BG is active, or final scores if the BG has ended.

**HandleBattlefieldListOpcode**: Requests the available battleground list. Determines BG type from packet or player state and delegates packet construction to `BattleGroundMgr`.

**HandleBattleFieldPortOpcode**: Manages entering or leaving the queue. Validates deserter status, level caps, and BG status. Teleports players into instances or removes them from queues as requested.

**HandleLeaveBattlefieldOpcode**: Processes leave requests. Prevents leaving if in combat (unless BG ended) and calls `Player.LeaveBattleground`.

**HandleBattlefieldStatusOpcode**: Updates client on queue status. Iterates through all queue slots, sending appropriate status packets (in-progress, invited, or waiting) based on current state.

**HandleAreaSpiritHealerQueryOpcode**: Validates spirit healer NPC and delegates query response to the Creature object.

**HandleAreaSpiritHealerQueueOpcode**: Validates spirit healer NPC and triggers the gossip script via `ScriptMgr`.

**SendBattleGroundJoinError**: Translates internal error codes into localized chat messages and sends them to the client.

---

<!-- machine-true, projected from graph.json -->

## Map — WorldSession.BattleGroundHandler

*Source:* BattleGroundHandler.cpp, WorldSession.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| HandleBattlemasterHelloOpcode | method | BattleGroundMgr/GetBattleMasterBG, Creature.Main/HasExtraFlag, Creature.MotionMaster/PauseOutOfCombatMovement, Map.Main/GetCreature, Object/GetEntry, Player.Main/GetBGAccessByLevel, SpellCaster/InterruptSpellsWithChannelFlags, Unit.Main/IsBattleMaster, Unit.Main/RemoveAurasWithInterruptFlags, WorldObject.Object/GetMap, WorldSession.Main/GetPlayer, WorldSession.Main/SendNotification#2 | — | — |
| SendBattleGroundList | method | BattleGroundMgr/BuildBattleGroundListPacket, WorldPacket/WorldPacket, WorldSession.Main/SendPacket | ChatHandler.MiscCommands/RegisterPlayerToBG, Player.Main/OnGossipSelect, WorldSession.MiscHandler/HandleAreaTriggerOpcode | — |
| HandleBattlefieldJoinOpcode | method | ObjectGuid/ObjectGuid | — | — |
| HandleBattlemasterJoinOpcode | method | — | — | — |
| RequestBgJoinQueue | method | BattleGround/GetMapId, BattleGround/GetMaxPlayersPerTeam, BattleGroundMgr/AddGroup, BattleGroundMgr/BgQueueTypeId, BattleGroundMgr/BuildBattleGroundStatusPacket, BattleGroundMgr/BuildGroupJoinedBattlegroundPacket, BattleGroundMgr/GetAverageQueueWaitTime, BattleGroundMgr/GetBattleGroundTemplate, BattleGroundMgr/GetBattleGroundThroughClientInstance, BattleGroundMgr/ScheduleQueueUpdate, ByteBuffer/operator<<#10, game_Group_Group/CanJoinBattleGroundQueue, Group/GetFirstMember, Group/GetMembersCount, GroupReference/next, Log.Main/Out, Object/GetGUIDLow, Object/GetObjectGuid, ObjectGuid/operator==, Player.Main/AddBattleGroundQueueId, Player.Main/CanJoinToBattleground, Player.Main/GetBattleGroundBracketIdFromLevel, Player.Main/GetBattleGroundEntryPoint, Player.Main/GetBattleGroundQueueIndex, Player.Main/GetGroup, Player.Main/GetName, Player.Main/GetNPCIfCanInteractWith, Player.Main/GetSession, Player.Main/HasFreeBattleGroundQueueId, Player.Main/InBattleGround, Player.Main/SetBattleGroundEntryPoint, SharedDefines/GetBattleGroundTypeIdByMapId, World/getConfig#4, WorldObject.Object/GetMapId, WorldPacket/WorldPacket, WorldPacket/WorldPacket#4, WorldSession.Main/GetPlayer, WorldSession.Main/ProcessAnticheatAction, WorldSession.Main/SendPacket | CombatBotBaseAI/SendBattlemasterJoinPacket | — |
| HandleBattleGroundPlayerPositionsOpcode | method | BattleGround/GetBgRaid, BattleGround/GetPlayers, BattleGround/GetTypeID, BattleGroundWS/GetAllianceFlagPickerGuid, BattleGroundWS/GetHordeFlagPickerGuid, ByteBuffer/operator<<#10, ByteBuffer/operator<<#7, ByteBuffer/operator<<#9, ByteBuffer/wpos, Object/GetObjectGuid, ObjectGuid/operator<<, ObjectMgr/GetPlayer, Player.Main/GetBattleGround, Player.Main/GetGroup, Player.Main/GetTeam, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| HandlePVPLogDataOpcode | method | BattleGround/GetFinalScorePacket, BattleGround/GetStatus, BattleGroundMgr/BuildPvpLogDataPacket, Player.Main/GetBattleGround, WorldPacket/WorldPacket, WorldSession.Main/SendPacket | — | — |
| HandleBattlefieldListOpcode | method | BattleGroundMgr/BuildBattleGroundListPacket, Log.Main/Out, Object/GetObjectGuid, SharedDefines/GetBattleGroundTypeIdByMapId, WorldPacket/WorldPacket, WorldSession.Main/SendPacket | — | — |
| HandleBattleFieldPortOpcode | method | BattleGround/GetInstanceID, BattleGround/GetMapId, BattleGround/GetMaxLevel, BattleGround/GetStartTime, BattleGround/GetStatus, BattleGround/GetTypeID, BattleGroundMgr/BgQueueTypeId, BattleGroundMgr/BuildBattleGroundStatusPacket, BattleGroundMgr/GetBattleGround, BattleGroundMgr/GetBattleGroundTemplate, BattleGroundMgr/GetPlayerGroupInfoData, BattleGroundMgr/RemovePlayer, BattleGroundMgr/ScheduleQueueUpdate, BattleGroundMgr/SendToBattleGround, ByteBuffer/operator<<#10, game_Battlegrounds_BattleGround/RemovePlayerAtLeave, Log.Main/Out, MotionMaster/MovementExpired, Object/GetGUIDLow, Object/GetObjectGuid, Player.Main/CanJoinToBattleground, Player.Main/GetBattleGround, Player.Main/GetBattleGroundBracketIdFromLevel, Player.Main/GetBattleGroundQueueIndex, Player.Main/GetName, Player.Main/GetSession, Player.Main/InBattleGroundQueue, Player.Main/IsInvitedForBattleGroundQueueType, Player.Main/RemoveBattleGroundQueueId, Player.Main/ResurrectPlayer, Player.Main/SetBattleGroundId, Player.Main/SetBGTeam, Player.Main/SpawnCorpseBones, PlayerTaxi/ClearTaxiDestinations, SharedDefines/GetBattleGroundTypeIdByMapId, Unit.Main/GetLevel, Unit.Main/GetMotionMaster, Unit.Main/IsAlive, Unit.Main/IsTaxiFlying, WorldPacket/WorldPacket, WorldPacket/WorldPacket#4, WorldSession.Main/GetAccountId, WorldSession.Main/GetRemoteAddress, WorldSession.Main/SendPacket | CombatBotBaseAI/SendBattlefieldPortPacket | — |
| HandleLeaveBattlefieldOpcode | method | BattleGround/GetStatus, Player.Main/GetBattleGround, Player.Main/LeaveBattleground, Unit.Main/IsInCombat, WorldObject.Object/GetMapId | — | — |
| HandleBattlefieldStatusOpcode | method | BattleGround/GetEndTime, BattleGround/GetStartTime, BattleGroundMgr/BgTemplateId, BattleGroundMgr/BuildBattleGroundStatusPacket, BattleGroundMgr/GetAverageQueueWaitTime, BattleGroundMgr/GetBattleGround, BattleGroundMgr/GetBattleGroundTemplate, BattleGroundMgr/GetPlayerGroupInfoData, Object/GetObjectGuid, Player.Main/GetBattleGround, Player.Main/GetBattleGroundBracketIdFromLevel, Player.Main/GetBattleGroundQueueTypeId, Player.Main/GetBattleGroundTypeId, shared_Util/getMSTime, WorldPacket/WorldPacket, WorldSession.Main/SendPacket, WorldTimer/getMSTimeDiff | — | — |
| HandleAreaSpiritHealerQueryOpcode | method | Creature.Main/SendAreaSpiritHealerQueryOpcode, Map.Main/GetCreature, Player.Main/GetBattleGround, Unit.Main/IsSpiritService, WorldObject.Object/GetMap, WorldSession.Main/GetPlayer | — | — |
| HandleAreaSpiritHealerQueueOpcode | method | Map.Main/GetCreature, Player.Main/GetBattleGround, ScriptMgr/OnGossipHello, Unit.Main/IsSpiritService, WorldObject.Object/GetMap, WorldSession.Main/GetPlayer | — | — |
| SendBattleGroundJoinError | method | ChatHandler.Chat/BuildChatPacket, WorldPacket/WorldPacket, WorldSession.Main/GetMangosString, WorldSession.Main/SendPacket | — | — |

---

<!-- verify: boundary-bleed | foreign: Update, WorldSession -->
