# BattleGroundMgr

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# BattleGroundMgr

**Purpose & Responsibilities**

`BattleGroundMgr` is the singleton manager responsible for the lifecycle, queueing, and matchmaking of all Battlegrounds (BGs) in the server. It handles three primary domains:
1.  **Queue Management:** Maintains separate queues for Alliance and Horde, distinguishing between premade groups and normal (solo/small group) queues. It calculates average wait times and manages the transition of players from "queued" to "invited" to "in-game."
2.  **Matchmaking Logic:** Determines when to start a new Battleground instance or fill open slots in existing instances. It implements algorithms to balance team sizes, prioritizing balanced matches over speed, and handles special cases like premade group timeouts.
3.  **Instance Lifecycle:** Creates, updates, and destroys `BattleGround` objects. It loads configuration from the database (`battleground_template`, `battlemaster_entry`, `battleground_events`) and manages the mapping between client-visible instance IDs and internal server instances.

The class contains nested helper classes: `BattleGroundQueue` (manages the actual queue data structures), `SelectionPool` (used internally by the queue to select groups for matching), and event handlers `BgQueueInviteEvent` and `BGQueueRemoveEvent` (manage timed invitations).

## Member-by-Member Behavior

### Queue Initialization and Cleanup
*   **`BattleGroundQueue`** (ctor): Initializes wait time tracking arrays (`m_sumOfWaitTimes`, `m_waitTimes`) to zero for all teams and brackets.
*   **`~BattleGroundQueue`**: Cleans up memory by clearing the `m_queuedPlayers` map and deleting all `GroupQueueInfo` objects stored in `m_queuedGroups`.
*   **`Init`**: Resets the selection pool by clearing `selectedGroups` and setting `playerCount` to 0. Used before starting a new matchmaking attempt.

### Queue Population and Removal
*   **`AddGroup`**: Adds a group (or solo player) to the appropriate queue (Premade or Normal, Alliance or Horde).
    *   It creates a `GroupQueueInfo` object linking all members.
    *   If the group size exceeds the configured `CONFIG_UINT32_BATTLEGROUND_GROUP_LIMIT`, it splits the group, queuing members individually and sending them a system message.
    *   It updates the `m_queuedPlayers` map for each member.
    *   If configured, it announces the queue status (current queue counts vs. required players) to the player or the entire world.
*   **`AddGroup#2`**: An overload or alternative entry point for adding groups to the selection pool during matchmaking calculations, used internally by `SelectionPool` logic.
*   **`RemovePlayer`**: Removes a player from the queue.
    *   It locates the player's `GroupQueueInfo` by iterating through all brackets and queue types.
    *   If the player was invited to a specific BG instance and `decreaseInvitedCount` is true, it notifies the `BattleGround` object to decrement its invited count.
    *   If the `GroupQueueInfo` becomes empty after removal, the group object is deleted.
*   **`PlayerLoggedOut`** (in `BattleGroundQueue`): Marks the player as offline in `m_queuedPlayers` and records the timestamp. It does *not* immediately remove them, allowing for re-login within a grace period.
*   **`PlayerLoggedIn`** (in `BattleGroundQueue`): Marks the player as online. Returns true if the player was found in the queue (indicating they were queued while offline).
*   **`RemoveOfflinePlayer`**: Iterates through `m_queuedPlayers`. Removes players who have been offline longer than `OFFLINE_BG_QUEUE_TIME` (60 seconds) or who are invited to a BG that has ended (`STATUS_WAIT_LEAVE`).

### Matchmaking Algorithms
*   **`CheckPremadeMatch`**: Attempts to form a match using only premade groups.
    *   It fills `SelectionPool`s for both teams with premade groups until `minPlayersPerTeam` is reached.
    *   If a premade group has waited longer than `CONFIG_UINT32_BATTLEGROUND_PREMADE_GROUP_WAIT_FOR_MATCH` or shrinks below `minPlayersPerTeam`, it is moved to the Normal queue.
    *   Returns true if both teams have enough players in the selection pools.
*   **`CheckNormalMatch`**: Attempts to form a match using normal queues.
    *   Fills `SelectionPool`s with normal groups.
    *   If `CONFIG_UINT32_BATTLEGROUND_INVITATION_TYPE` is non-zero, it attempts to balance the teams further by adding extra groups to the smaller team, ensuring the difference doesn't exceed 2 players.
    *   Returns true if both teams meet the minimum player requirement.
*   **`FillPlayersToBg`**: Fills open slots in an *existing* running Battleground.
    *   It iterates through the normal queues, adding groups to `SelectionPool`s until free slots are filled.
    *   If `CONFIG_UINT32_BATTLEGROUND_INVITATION_TYPE` is non-zero, it runs a balancing loop (`KickGroup`) to ensure the final team sizes are as close as possible, kicking larger groups if necessary to achieve balance.
*   **`CheckFreeSlots`**: Iterates through the `m_bgFreeSlotQueue` (BGs with open slots). For each eligible BG, it calls `FillPlayersToBg` and invites the selected groups. If a BG becomes full, it is removed from the free slot queue.
*   **`CheckCreateNewBg`**: Determines if a new BG instance should be started.
    *   First attempts `CheckPremadeMatch`. If successful, it creates a new BG via `CreateNewBattleGround`, invites the selected groups, sets the level range, and starts the BG.
    *   If no premade match is found, it attempts `CheckNormalMatch`. If successful, it performs the same creation and invitation steps.
    *   Special handling for Alterac Valley (AV): Enforces a minimum queue depth (`CONFIG_UINT32_AV_MIN_PLAYERS_IN_QUEUE`) and potentially limits initial max players (`CONFIG_UINT32_AV_INITIAL_MAX_PLAYERS`). It also randomizes the queue order if configured.
*   **`Update#2`**: The main tick for a specific queue type/bracket.
    *   Calls `RemoveOfflinePlayer`.
    *   Checks if any players are in queue.
    *   Calls `CheckFreeSlots` to fill existing BGs.
    *   Calls `CheckCreateNewBg` to start new BGs.
    *   Calls `CheckFreeSlots` again to handle any remaining players who might fit into the newly created BG.

### Invitation Handling
*   **`InviteGroupToBG`**: Sends invitations to a group for a specific BG instance.
    *   Sets `isInvitedToBgInstanceGuid` and `removeInviteTime` on the `GroupQueueInfo`.
    *   Increments the BG's invited count.
    *   Schedules two events for each player:
        1.  `BgQueueInviteEvent`: Reminds the player after `INVITATION_REMIND_TIME`.
        2.  `BGQueueRemoveEvent`: Removes the player from the queue if they don't accept by `INVITE_ACCEPT_WAIT_TIME`.
    *   Sends `SMSG_BATTLEFIELD_STATUS` with `STATUS_WAIT_JOIN` to the player.
*   **`Execute#2`**: The execution handler for `BgQueueInviteEvent`. If the player is still online and still invited to the same BG, it resends the status packet with the remaining time.
*   **`Abort#2`**: The abort handler for `BgQueueInviteEvent`. Currently a no-op.
*   **`Execute`**: The execution handler for `BGQueueRemoveEvent`. If the player is still online and still invited (hasn't accepted or left manually), it removes them from the queue, sends a `STATUS_NONE` packet, and schedules a queue update.
*   **`Abort`**: The abort handler for `BGQueueRemoveEvent`. Currently a no-op.

### Wait Time Tracking
*   **`PlayerInvitedToBgUpdateAverageWaitTime`**: Updates the rolling average wait time for the player's team/bracket. It uses a circular buffer (`m_waitTimes`) of size `COUNT_OF_PLAYERS_TO_AVERAGE_WAIT_TIME` (10).
*   **`GetAverageQueueWaitTime`**: Returns the current average wait time. If the buffer isn't full, it returns 0.

### Packet Construction
*   **`BuildBattleGroundStatusPacket`**: Constructs `SMSG_BATTLEFIELD_STATUS`. Encodes queue slot, map ID, bracket, instance ID, status, and timing data depending on the status (queue, invite, in-progress).
*   **`BuildPvpLogDataPacket`**: Constructs `MSG_PVP_LOG_DATA`. Includes winner info and detailed scores for up to 80 players. Scores include standard stats (kills, deaths) and BG-specific stats (e.g., graveyards assaulted for AV, flag captures for WS).
*   **`BuildGroupJoinedBattlegroundPacket`**: Constructs `SMSG_GROUP_JOINED_BATTLEGROUND` with a status code.
*   **`BuildUpdateWorldStatePacket`**: Constructs `SMSG_UPDATE_WORLD_STATE` for UI elements.
*   **`BuildPlaySoundPacket`**: Constructs `SMSG_PLAY_SOUND`.
*   **`BuildPlayerLeftBattleGroundPacket`**: Constructs `SMSG_BATTLEGROUND_PLAYER_LEFT`.
*   **`BuildPlayerJoinedBattleGroundPacket`**: Constructs `SMSG_BATTLEGROUND_PLAYER_JOINED`.
*   **`BuildBattleGroundListPacket`**: Constructs `SMSG_BATTLEFIELD_LIST`. Lists all active client-visible instance IDs for a specific BG type and bracket.

### Instance Management
*   **`CreateInitialBattleGrounds`**: Loads BG templates from `battleground_template`. For each row, it validates start locations against `WorldSafeLocs.dbc` and loot templates. It calls `CreateBattleGround` to instantiate the template objects.
*   **`CreateBattleGround`**: Instantiates a specific `BattleGround` subclass (AV, WS, AB) or base `BattleGround`. Configures it with min/max players, spells, start locations, and level ranges. Adds it to `m_battleGrounds`.
*   **`CreateNewBattleGround`**: Creates a playable instance by copying a template. Assigns a new client-visible ID via `CreateClientVisibleInstanceId`. Sets status to `STATUS_WAIT_JOIN`. Calls `MapManager::CreateBgMap` to create the map instance.
*   **`CreateClientVisibleInstanceId`**: Generates a sequential, gap-free integer ID for client display purposes, distinct from the internal map instance ID.
*   **`GetBattleGround`**: Retrieves a BG by internal instance ID.
*   **`GetBattleGroundThroughClientInstance`**: Retrieves a BG by client-visible instance ID.
*   **`GetBattleGroundTemplate`**: Returns the template BG object for a type (always the first entry in `m_battleGrounds`).
*   **`SendToBattleGround`**: Teleports a player to their team's start location in a specific BG instance. Clears AFK status.
*   **`DeleteAllBattleGrounds`**: Destroys all BG objects in `m_battleGrounds`.
*   **`Update`** (in `BattleGroundMgr`): Processes the `m_queueUpdateScheduler`. For each scheduled item, it decodes the queue/type/bracket and calls `BattleGroundQueue::Update`.

### Configuration and Data Loading
*   **`LoadBattleMastersEntry`**: Loads `battlemaster_entry` table, mapping NPC entries to BG types.
*   **`LoadBattleEventIndexes`**: Loads `battleground_events`, `creature_battleground`, and `gameobject_battleground`. Maps creature/gameobject GUIDs to event indices (event1, event2) for specific maps. Validates that GUIDs exist in `creature`/`gameobject` tables and that events exist in `battleground_events`.
*   **`GetCreatureEventIndex`** / **`GetGameObjectEventIndex`**: Retrieves the primary event index for a GUID.
*   **`GetCreatureEventsVector`** / **`GetGameObjectEventsVector`**: Retrieves all event indices for a GUID (supporting multiple events per object).
*   **`GetUsedRefLootIds`**: Returns the set of loot template IDs used by BGs, used by `LootMgr` to validate references.

### Utility and State
*   **`BgQueueTypeId`** / **`BgTemplateId`**: Static converters between BG Type IDs and Queue Type IDs.
*   **`BgTypeToWeekendHolidayId`** / **`WeekendHolidayIdToBgType`**: Static converters for holiday logic.
*   **`IsBgWeekend`**: Checks if a BG type is currently active as a weekend holiday via `GameEventMgr`.
*   **`ToggleTesting`**: Enables/disables testing mode. In testing mode, BGs can start with 1 player per team. Broadcasts status to the world.
*   **`ScheduleQueueUpdate`**: Adds a BG type/bracket to the update scheduler if not already present.
*   **`GetPrematureFinishTime`**: Returns the configured timer for premature BG finishes.
*   **`PlayerLoggedIn`** / **`PlayerLoggedOut`** (in `BattleGroundMgr`): High-level hooks called on player login/logout. They iterate through all queue types to restore queue state or clean up.
*   **`PlayerLoggedIn#2`**: Marks a player as online in the `BattleGroundQueue`.
*   **`PlayerLoggedOut#2`**: Marks a player as offline in the `BattleGroundQueue`.

## Cross-Unit Boundaries

*   **`WorldSession.BattleGroundHandler`**:
    *   *Calls `AddGroup`*: When a player requests to join a queue.
    *   *Calls `GetAverageQueueWaitTime`*: To display estimated wait time.
    *   *Calls `BuildBattleGroundStatusPacket`*, `BuildPvpLogDataPacket`, etc.: To send status updates to the client.
    *   *Calls `GetBattleGround`*, `GetBattleGroundTemplate`: To resolve BG instances for porting or status checks.
    *   *Calls `SendToBattleGround`*: When a player accepts an invitation.
*   **`game_Battlegrounds_BattleGround`**:
    *   *Calls `AddBattleGround`*, `RemoveBattleGround`: To register/unregister instances.
    *   *Calls `DeleteClientVisibleInstanceId`*: When an instance ends.
    *   *Calls `ScheduleQueueUpdate`*: When a BG ends or a player leaves, triggering a re-evaluation of queues.
    *   *Calls `BuildBattleGroundStatusPacket`*, `BuildPvpLogDataPacket`, etc.: To broadcast events to players inside the BG.
    *   *Calls `GetCreatureEventsVector`*, `GetGameObjectEventsVector`: To determine event triggers for spawns and interactions.
*   **`Player.Main`**:
    *   *Calls `RemovePlayer`*, `GetPlayerGroupInfoData`: When a player levels up (potentially changing bracket) or ports.
    *   *Calls `PlayerLoggedIn`*, `PlayerLoggedOut`: To manage queue persistence across logins.
*   **`ChatHandler.MiscCommands`**:
    *   *Calls `GetBattleGroundsBegin`*, `GetBattleGroundsEnd`, `GetBattleGroundTemplate`, `BgQueueTypeId`: For `.bg status` command.
*   **`GridNotifiers`**:
    *   *Calls `GetCreatureEventIndex`*, `GetGameObjectEventIndex`: To trigger events when players interact with objects/creatures in BGs.
*   **`LootMgr`**:
    *   *Calls `GetUsedRefLootIds`*: To validate loot template references.

## Data Model

*   **`battleground_template`**: Defines the static properties of each BG type (min/max players, level ranges, win/loss spells, start locations, loot IDs). Loaded by `CreateInitialBattleGrounds`.
*   **`battlemaster_entry`**: Maps NPC entries to BG types. Loaded by `LoadBattleMastersEntry`.
*   **`battleground_events`**: Defines event pairs (event1, event2) for specific maps.
*   **`creature_battleground`** / **`gameobject_battleground`**: Links creature/gameobject GUIDs to event pairs.
*   **`creature`** / **`gameobject`**: Referenced during `LoadBattleEventIndexes` to validate that linked GUIDs exist.

## Notable Implementation Details

*   **Queue Splitting**: If a group exceeds `CONFIG_UINT32_BATTLEGROUND_GROUP_LIMIT`, `AddGroup` splits it into solo queues. This prevents large groups from dominating matchmaking.
*   **Balancing Algorithm**: `FillPlayersToBg` and `CheckNormalMatch` implement a heuristic to balance team sizes. It prefers balanced matches over filling slots quickly, potentially leaving slots open if it results in a more even fight.
*   **Premade Timeout**: Premade groups are moved to the normal queue if they wait too long or shrink below the minimum size. This prevents premade groups from blocking normal players indefinitely.
*   **Offline Queue Persistence**: Players remain in the queue for 60 seconds after logging out. If they log back in, their queue position and invitation state are restored.
*   **Testing Mode**: `ToggleTesting` allows BGs to start with 1 player per team, useful for debugging.
*   **Client-Visible IDs**: `CreateClientVisibleInstanceId` generates sequential IDs for the client, separate from the internal map instance IDs. This allows the server to manage instances internally while presenting a clean list to the client.
*   **Event Validation**: `LoadBattleEventIndexes` performs strict validation, logging errors if GUIDs or event definitions are missing or mismatched.

## Member Reference

**BattleGroundQueue**
Initializes wait time tracking arrays to zero.

**~BattleGroundQueue**
Clears queued players and deletes all `GroupQueueInfo` objects.

**Init**
Resets the selection pool's group list and player count.

**KickGroup**
Removes a group from the selection pool to balance team sizes. Returns true if more groups can be added.

**AddGroup#2**
An overload or alternative entry point for adding groups to the selection pool during matchmaking calculations.

**AddGroup**
Adds a group or solo player to the queue. Handles group splitting if size exceeds limits. Announces queue status if configured.

**GetBattleGroundsBegin**
Returns iterator to the beginning of the BG set for a type.

**GetBattleGroundsEnd**
Returns iterator to the end of the BG set for a type.

**AddBattleGround**
Registers a BG instance in the manager's map.

**RemoveBattleGround**
Unregisters a BG instance from the manager's map.

**DeleteClientVisibleInstanceId**
Removes a client-visible instance ID from the tracking set.

**GetBattleMasterBG**
Returns the BG type associated with a battlemaster NPC entry.

**PlayerInvitedToBgUpdateAverageWaitTime**
Updates the rolling average wait time for a team/bracket using a circular buffer.

**GetCreatureEventIndex**
Returns the primary event index for a creature GUID.

**GetGameObjectEventIndex**
Returns the primary event index for a gameobject GUID.

**GetCreatureEventsVector**
Returns all event indices for a creature GUID.

**GetAverageQueueWaitTime**
Returns the current average wait time for a team/bracket.

**GetGameObjectEventsVector**
Returns all event indices for a gameobject GUID.

**isTesting**
Returns whether testing mode is enabled.

**RemovePlayer**
Removes a player from the queue and their group info. Decrements BG invited count if applicable.

**GetUsedRefLootIds**
Returns the set of loot template IDs used by BGs.

**IsPlayerInvited**
Checks if a player is currently invited to a specific BG instance.

**GetPlayerGroupInfoData**
Retrieves the `GroupQueueInfo` for a player.

**InviteGroupToBG**
Sends invitations to a group for a BG instance. Schedules reminder and removal events.

**FillPlayersToBg**
Fills open slots in an existing BG, balancing team sizes if configured.

**CheckPremadeMatch**
Attempts to form a match using only premade groups. Moves timed-out groups to normal queue.

**CheckNormalMatch**
Attempts to form a match using normal queues, balancing team sizes if configured.

**RemoveOfflinePlayer**
Removes players who have been offline too long or whose invited BG has ended.

**HasPlayersInQueue**
Checks if any groups are in the queue for a bracket.

**CheckFreeSlots**
Iterates through BGs with free slots and attempts to fill them.

**CheckCreateNewBg**
Determines if a new BG should be started, attempting premade then normal matches.

**Update#2**
Main tick for a queue type/bracket. Removes offline players, fills slots, creates new BGs.

**Execute#2**
Resends invitation status if the player is still waiting.

**Abort#2**
No-op abort handler for invitation reminders.

**Execute**
Removes player from queue if they haven't accepted the invitation.

**Abort**
No-op abort handler for queue removal.

**BattleGroundMgr**
Initializes BG maps and testing flag.

**~BattleGroundMgr**
Deletes all BG instances.

**DeleteAllBattleGrounds**
Destroys all BG objects in the manager's maps.

**Update**
Processes scheduled queue updates.

**BuildBattleGroundStatusPacket**
Constructs `SMSG_BATTLEFIELD_STATUS` packet.

**BuildPvpLogDataPacket**
Constructs `MSG_PVP_LOG_DATA` packet with scores.

**BuildGroupJoinedBattlegroundPacket**
Constructs `SMSG_GROUP_JOINED_BATTLEGROUND` packet.

**BuildUpdateWorldStatePacket**
Constructs `SMSG_UPDATE_WORLD_STATE` packet.

**BuildPlaySoundPacket**
Constructs `SMSG_PLAY_SOUND` packet.

**BuildPlayerLeftBattleGroundPacket**
Constructs `SMSG_BATTLEGROUND_PLAYER_LEFT` packet.

**BuildPlayerJoinedBattleGroundPacket**
Constructs `SMSG_BATTLEGROUND_PLAYER_JOINED` packet.

**GetBattleGroundThroughClientInstance**
Finds a BG by its client-visible instance ID.

**GetBattleGround**
Finds a BG by its internal instance ID.

**GetBattleGroundTemplate**
Returns the template BG object for a type.

**CreateClientVisibleInstanceId**
Generates a sequential client-visible instance ID.

**CreateNewBattleGround**
Creates a playable BG instance from a template.

**CreateBattleGround**
Instantiates a BG template object with configuration.

**CreateInitialBattleGrounds**
Loads BG templates from the database.

**BuildBattleGroundListPacket**
Constructs `SMSG_BATTLEFIELD_LIST` packet.

**SendToBattleGround**
Teleports a player to a BG instance.

**BgQueueTypeId**
Converts BG Type ID to Queue Type ID.

**BgTemplateId**
Converts Queue Type ID to BG Type ID.

**ToggleTesting**
Enables/disables testing mode.

**ScheduleQueueUpdate**
Adds a queue type/bracket to the update scheduler.

**GetPrematureFinishTime**
Returns the configured premature finish timer.

**LoadBattleMastersEntry**
Loads battlemaster NPC mappings from the database.

**BgTypeToWeekendHolidayId**
Converts BG Type ID to Holiday ID.

**WeekendHolidayIdToBgType**
Converts Holiday ID to BG Type ID.

**IsBgWeekend**
Checks if a BG type is active as a weekend holiday.

**LoadBattleEventIndexes**
Loads and validates BG event mappings from the database.

**PlayerLoggedIn**
Restores queue state for a logging-in player.

**PlayerLoggedOut**
Marks a player as offline in the queue.

**PlayerLoggedOut#2**
Marks a player as offline in the `BattleGroundQueue`.

**PlayerLoggedIn#2**
Marks a player as online in the `BattleGroundQueue`.

---

<!-- machine-true, projected from graph.json -->

## Map — BattleGroundMgr

*Source:* BattleGroundMgr.cpp, BattleGroundMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| BattleGroundQueue | ctor | — | — | — |
| ~BattleGroundQueue | dtor | — | — | — |
| Init | method | — | — | — |
| KickGroup | method | SelectionPool/GetPlayerCount | — | — |
| AddGroup#2 | method | — | — | — |
| AddGroup | method | BattleGround/GetMinPlayersPerTeam, BattleGround/GetName, Group/GetFirstMember, Group/GetMembersCount, GroupReference/next, Log.Main/Out, Object/GetGUIDLow, Object/GetObjectGuid, Player.Main/GetMaxLevelForBattleGroundBracketId, Player.Main/GetMinLevelForBattleGroundBracketId, Player.Main/GetName, Player.Main/GetSession, Player.Main/GetTeam, Player.Main/PSendSysMessage, Player.Main/PSendSysMessage#2, shared_Util/getMSTime, World/getConfig#4, World/SendWorldText, WorldSession.Main/GetAccountId, WorldSession.Main/GetRemoteAddress | WorldSession.BattleGroundHandler/RequestBgJoinQueue | — |
| GetBattleGroundsBegin | method | — | ChatHandler.MiscCommands/HandleBGStatusCommand | — |
| GetBattleGroundsEnd | method | — | ChatHandler.MiscCommands/HandleBGStatusCommand | — |
| AddBattleGround | method | — | game_Battlegrounds_BattleGround/StartBattleGround | — |
| RemoveBattleGround | method | — | game_Battlegrounds_BattleGround/~BattleGround | — |
| DeleteClientVisibleInstanceId | method | — | game_Battlegrounds_BattleGround/~BattleGround | — |
| GetBattleMasterBG | method | — | Creature.Main/CanInteractWithBattleMaster, Player.Main/OnGossipSelect, WorldSession.BattleGroundHandler/HandleBattlemasterHelloOpcode | — |
| PlayerInvitedToBgUpdateAverageWaitTime | method | shared_Util/getMSTime, WorldTimer/getMSTimeDiff | — | — |
| GetCreatureEventIndex | method | — | BattleGroundAV/HandleKillUnit, GridNotifiers/operator()#2 | — |
| GetGameObjectEventIndex | method | — | BattleGroundAB/EventPlayerClickedOnFlag, BattleGroundAV/EventPlayerClickedOnFlag, BattleGroundWS/EventPlayerClickedOnFlag, GridNotifiers/operator()#3 | — |
| GetCreatureEventsVector | method | — | game_Battlegrounds_BattleGround/CanBeSpawned, game_Battlegrounds_BattleGround/OnObjectDBLoad, game_Battlegrounds_BattleGround/SetSpawnEventMode, game_Battlegrounds_BattleGround/SpawnEvent | — |
| GetAverageQueueWaitTime | method | — | WorldSession.BattleGroundHandler/HandleBattlefieldStatusOpcode, WorldSession.BattleGroundHandler/RequestBgJoinQueue | — |
| GetGameObjectEventsVector | method | — | game_Battlegrounds_BattleGround/OnObjectDBLoad#2 | — |
| isTesting | method | — | game_Battlegrounds_BattleGround/Update | — |
| RemovePlayer | method | BattleGround/GetTeamIndexByTeamId, game_Battlegrounds_BattleGround/DecreaseInvitedCount, Log.Main/Out, ObjectGuid/GetString | Player.Main/GiveLevel, WorldSession.BattleGroundHandler/HandleBattleFieldPortOpcode | — |
| GetUsedRefLootIds | method | — | LootMgr/CheckLootTemplates_Reference | — |
| IsPlayerInvited | method | — | — | — |
| GetPlayerGroupInfoData | method | — | Player.Main/GiveLevel, WorldSession.BattleGroundHandler/HandleBattleFieldPortOpcode, WorldSession.BattleGroundHandler/HandleBattlefieldStatusOpcode | — |
| InviteGroupToBG | method | BattleGround/GetBracketId, BattleGround/GetInstanceID, BattleGround/GetTypeID, BgQueueInviteEvent/BgQueueInviteEvent, BGQueueRemoveEvent/BGQueueRemoveEvent, EventProcessor/AddEvent, EventProcessor/CalculateTime, game_Battlegrounds_BattleGround/IncreaseInvitedCount, Log.Main/Out, Object/GetGuidStr, Object/GetObjectGuid, ObjectAccessor/FindPlayerNotInWorld, Player.Main/GetBattleGroundQueueIndex, Player.Main/GetSession, Player.Main/SetInviteForBattleGroundQueueType, shared_Util/getMSTime, WorldPacket/WorldPacket, WorldSession.Main/SendPacket | — | — |
| FillPlayersToBg | method | BattleGround/GetClientInstanceID, game_Battlegrounds_BattleGround/GetFreeSlotsForTeam, SelectionPool/GetPlayerCount, World/getConfig#4 | — | — |
| CheckPremadeMatch | method | SelectionPool/GetPlayerCount, shared_Util/getMSTime, World/getConfig#4 | — | — |
| CheckNormalMatch | method | SelectionPool/GetPlayerCount, World/getConfig#4 | — | — |
| RemoveOfflinePlayer | method | BattleGround/GetStatus, ObjectAccessor/FindPlayerNotInWorld, Player.Main/GetBattleGroundQueueIndex, Player.Main/GetSession, Player.Main/RemoveBattleGroundQueueId, WorldPacket/WorldPacket, WorldSession.Main/SendPacket, WorldTimer/getMSTimeDiffToNow | — | — |
| HasPlayersInQueue | method | — | — | — |
| CheckFreeSlots | method | BattleGround/GetBracketId, BattleGround/GetStatus, BattleGround/GetTypeID, game_Battlegrounds_BattleGround/HasFreeSlots, game_Battlegrounds_BattleGround/RemoveFromBGFreeSlotQueue, Log.Main/Out, World/getConfig | — | — |
| CheckCreateNewBg | method | BattleGround/GetMaxPlayersPerTeam, BattleGround/GetMinPlayersPerTeam, BattleGround/SetLevelRange, game_Battlegrounds_BattleGround/StartBattleGround, Log.Main/Out, Player.Main/GetMaxLevelForBattleGroundBracketId, Player.Main/GetMinLevelForBattleGroundBracketId, World/getConfig#4 | — | — |
| Update#2 | method | — | — | — |
| Execute#2 | method | BattleGround/GetTypeID, ObjectAccessor/FindPlayerNotInWorld, Player.Main/GetBattleGroundQueueIndex, Player.Main/GetSession, WorldPacket/WorldPacket, WorldSession.Main/SendPacket | — | — |
| Abort#2 | method | — | — | — |
| Execute | method | BattleGround/GetBracketId, BattleGround/GetStatus, Log.Main/Out, Object/GetGUIDLow, ObjectAccessor/FindPlayerNotInWorld, Player.Main/GetBattleGroundQueueIndex, Player.Main/GetSession, Player.Main/RemoveBattleGroundQueueId, WorldPacket/WorldPacket, WorldSession.Main/SendPacket | — | — |
| Abort | method | — | — | — |
| BattleGroundMgr | ctor | — | — | — |
| ~BattleGroundMgr | dtor | — | — | — |
| DeleteAllBattleGrounds | method | — | WorldRunnable/operator() | — |
| Update | method | — | World/Update | — |
| BuildBattleGroundStatusPacket | method | BattleGround/GetBracketId, BattleGround/GetClientInstanceID, BattleGround/GetMapId, ByteBuffer/operator<<#10, ByteBuffer/operator<<#7, Log.Main/Out, WorldPacket/Initialize | game_Battlegrounds_BattleGround/EndBattleGround, game_Battlegrounds_BattleGround/PlayerAddedToBGCheckIfBGIsRunning, game_Battlegrounds_BattleGround/RemovePlayerAtLeave, Player.Main/GiveLevel, WorldSession.BattleGroundHandler/HandleBattleFieldPortOpcode, WorldSession.BattleGroundHandler/HandleBattlefieldStatusOpcode, WorldSession.BattleGroundHandler/RequestBgJoinQueue | — |
| BuildPvpLogDataPacket | method | BattleGround/GetPlayerScoresBegin, BattleGround/GetPlayerScoresEnd, BattleGround/GetPlayerScoresSize, BattleGround/GetStatus, BattleGround/GetTypeID, BattleGround/GetWinner, ByteBuffer/operator<<#10, ByteBuffer/operator<<#7, HonorMgr/GetRank, Log.Main/Out, ObjectAccessor/FindPlayerNotInWorld, ObjectGuid/operator<<, Player.Main/GetHonorMgr, WorldPacket/Initialize | game_Battlegrounds_BattleGround/EndBattleGround, game_Battlegrounds_BattleGround/EndNow, game_Battlegrounds_BattleGround/PlayerAddedToBGCheckIfBGIsRunning, WorldSession.BattleGroundHandler/HandlePVPLogDataOpcode | — |
| BuildGroupJoinedBattlegroundPacket | method | ByteBuffer/operator<<#4, WorldPacket/Initialize | WorldSession.BattleGroundHandler/RequestBgJoinQueue | — |
| BuildUpdateWorldStatePacket | method | WorldPacket/Initialize, WorldStates/WriteUpdateWorldStatePair | game_Battlegrounds_BattleGround/UpdateWorldState, game_Battlegrounds_BattleGround/UpdateWorldStateForPlayer | — |
| BuildPlaySoundPacket | method | ByteBuffer/operator<<#10, WorldPacket/Initialize | game_Battlegrounds_BattleGround/PlaySoundToAll, game_Battlegrounds_BattleGround/PlaySoundToTeam | — |
| BuildPlayerLeftBattleGroundPacket | method | ObjectGuid/operator<<, WorldPacket/Initialize | game_Battlegrounds_BattleGround/RemovePlayerAtLeave | — |
| BuildPlayerJoinedBattleGroundPacket | method | Object/GetObjectGuid, ObjectGuid/operator<<, WorldPacket/Initialize | game_Battlegrounds_BattleGround/AddPlayer | — |
| GetBattleGroundThroughClientInstance | method | BattleGround/GetClientInstanceID | WorldSession.BattleGroundHandler/RequestBgJoinQueue | — |
| GetBattleGround | method | — | Player.Main/GetBattleGround, Player.Main/GiveLevel, WorldSession.BattleGroundHandler/HandleBattleFieldPortOpcode, WorldSession.BattleGroundHandler/HandleBattlefieldStatusOpcode | — |
| GetBattleGroundTemplate | method | — | ChatHandler.MiscCommands/HandleBGStatusCommand, ChatHandler.PlayerBotMgr/Update, Player.Main/GetBattleGroundBracketIdFromLevel#2, Player.Main/GetBGAccessByLevel, Player.Main/GetMinLevelForBattleGroundBracketId, Player.Main/GiveLevel, Spell.Effects/DoCreateItem, WorldSession.BattleGroundHandler/HandleBattleFieldPortOpcode, WorldSession.BattleGroundHandler/HandleBattlefieldStatusOpcode, WorldSession.BattleGroundHandler/RequestBgJoinQueue, WorldSession.MiscHandler/HandleAreaTriggerOpcode | — |
| CreateClientVisibleInstanceId | method | — | — | — |
| CreateNewBattleGround | method | BattleGround/GetMapId, BattleGround/SetBracketId, BattleGround/SetClientInstanceID, BattleGround/SetStatus, game_Battlegrounds_BattleGround/Reset, Log.Main/Out, MapManager/CreateBgMap | — | — |
| CreateBattleGround | method | BattleGround/GetInstanceID, BattleGround/GetTypeID, BattleGround/SetAllianceLoseSpell, BattleGround/SetAllianceWinSpell, BattleGround/SetHordeLoseSpell, BattleGround/SetHordeWinSpell, BattleGround/SetLevelRange, BattleGround/SetMapId, BattleGround/SetMaxPlayers, BattleGround/SetMaxPlayersPerTeam, BattleGround/SetMinPlayers, BattleGround/SetMinPlayersPerTeam, BattleGround/SetName, BattleGround/SetPlayerSkinRefLootId, BattleGround/SetTypeID, BattleGroundAB/BattleGroundAB, BattleGroundAV/BattleGroundAV, BattleGroundWS/BattleGroundWS, game_Battlegrounds_BattleGround/BattleGround, game_Battlegrounds_BattleGround/SetTeamStartLoc | — | — |
| CreateInitialBattleGrounds | method | Database/PQuery, Field/GetUInt32, Log.Main/Out, LootMgr/ExistsRefLootTemplate, ObjectMgr/GetWorldSafeLocFacing, ProgressBar/BarGoLink, ProgressBar/BarGoLink#3, ProgressBar/step, QueryResult/Fetch, QueryResult/GetRowCount, QueryResult/NextRow, SharedDefines/GetBattleGrounMapIdByTypeId, World/GetWowPatch | World/SetInitialWorldSettings | battleground_template |
| BuildBattleGroundListPacket | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#7, ByteBuffer/wpos, ObjectGuid/operator<<, Player.Main/GetBattleGroundBracketIdFromLevel, SharedDefines/GetBattleGrounMapIdByTypeId, WorldPacket/Initialize | WorldSession.BattleGroundHandler/HandleBattlefieldListOpcode, WorldSession.BattleGroundHandler/SendBattleGroundList | — |
| SendToBattleGround | method | BattleGround/GetMapId, BattleGround/GetTeamStartLoc, Log.Main/Out, Object/GetGUIDLow, Player.Main/GetBGTeam, Player.Main/GetName, Player.Main/GetTeam, Player.Main/IsAFK, Player.Main/TeleportTo, Player.Main/ToggleAFK | WorldSession.BattleGroundHandler/HandleBattleFieldPortOpcode | — |
| BgQueueTypeId | method | — | ChatHandler.MiscCommands/HandleBGStatusCommand, game_Battlegrounds_BattleGround/EndBattleGround, game_Battlegrounds_BattleGround/PlayerAddedToBGCheckIfBGIsRunning, game_Battlegrounds_BattleGround/RemovePlayerAtLeave, game_Battlegrounds_BattleGround/Update, WorldSession.BattleGroundHandler/HandleBattleFieldPortOpcode, WorldSession.BattleGroundHandler/RequestBgJoinQueue | — |
| BgTemplateId | method | — | ChatHandler.PlayerBotMgr/Update, Player.Main/GiveLevel, World/SendWorldTextToBGAndQueue, WorldSession.BattleGroundHandler/HandleBattlefieldStatusOpcode | — |
| ToggleTesting | method | World/SendWorldText | ChatHandler.DebugCommands/HandleDebugBattlegroundCommand | — |
| ScheduleQueueUpdate | method | — | game_Battlegrounds_BattleGround/EndBattleGround, game_Battlegrounds_BattleGround/RemovePlayerAtLeave, game_Battlegrounds_BattleGround/Update, Player.Main/GiveLevel, WorldSession.BattleGroundHandler/HandleBattleFieldPortOpcode, WorldSession.BattleGroundHandler/RequestBgJoinQueue | — |
| GetPrematureFinishTime | method | World/getConfig#4 | game_Battlegrounds_BattleGround/Update | — |
| LoadBattleMastersEntry | method | Database/Query, Field/GetUInt32, Log.Main/Out, ProgressBar/BarGoLink, ProgressBar/BarGoLink#3, ProgressBar/step, QueryResult/Fetch, QueryResult/GetRowCount, QueryResult/NextRow | World/SetInitialWorldSettings | battlemaster_entry |
| BgTypeToWeekendHolidayId | method | — | — | — |
| WeekendHolidayIdToBgType | method | — | — | — |
| IsBgWeekend | method | GameEventMgr.Main/IsActiveHoliday | BattleGroundAB/EndBattleGround, BattleGroundAB/Reset, BattleGroundAV/EndBattleGround, BattleGroundAV/Reset, BattleGroundWS/EndBattleGround, BattleGroundWS/Reset | — |
| LoadBattleEventIndexes | method | Database/Query, Field/GetString, Field/GetUInt32, Field/GetUInt8, Log.Main/Out, ProgressBar/BarGoLink, ProgressBar/BarGoLink#3, ProgressBar/step, QueryResult/Fetch, QueryResult/GetRowCount, QueryResult/NextRow | ChatHandler.ServerCommands/HandleReloadBattleEventCommand, World/SetInitialWorldSettings | battleground_events, creature, creature_battleground, gameobject, gameobject_battleground |
| PlayerLoggedIn | method | BattleGround/GetTypeID, BGQueueRemoveEvent/BGQueueRemoveEvent, EventProcessor/AddEvent, EventProcessor/CalculateTime, Object/GetObjectGuid, Player.Main/AddBattleGroundQueueId, Player.Main/GetBattleGroundBracketIdFromLevel, Player.Main/GetSession, Player.Main/SetInviteForBattleGroundQueueType, shared_Util/getMSTime, WorldPacket/WorldPacket, WorldSession.Main/SendPacket, WorldTimer/getMSTimeDiff | Player.Main/LoadFromDB | — |
| PlayerLoggedOut | method | Object/GetObjectGuid, Player.Main/GetBattleGroundQueueTypeId, Player.Main/RemoveBattleGroundQueueId | WorldSession.Main/LogoutPlayer | — |
| PlayerLoggedOut#2 | method | Log.Main/Out, ObjectGuid/GetString, shared_Util/getMSTime | — | — |
| PlayerLoggedIn#2 | method | Object/GetObjectGuid | — | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `battleground_events`: map smallint(5) unsigned PK, event1 tinyint(3) unsigned PK, event2 tinyint(3) unsigned PK, description varchar(255)
- `battleground_template`: id mediumint(8) unsigned PK, patch tinyint(3) unsigned PK, min_players_per_team smallint(5) unsigned, max_players_per_team smallint(5) unsigned, min_level tinyint(3) unsigned, max_level tinyint(3) unsigned, alliance_win_spell smallint(5) unsigned, alliance_lose_spell smallint(5) unsigned, horde_win_spell smallint(5) unsigned, horde_lose_spell smallint(5) unsigned, alliance_start_location mediumint(8) unsigned, horde_start_location mediumint(8) unsigned, player_loot_id mediumint(8) unsigned
- `battlemaster_entry`: entry mediumint(8) unsigned PK, bg_template mediumint(8) unsigned
- `creature`: guid int(10) unsigned PK, id mediumint(8) unsigned, id2 mediumint(8) unsigned, id3 mediumint(8) unsigned, id4 mediumint(8) unsigned, id5 mediumint(8) unsigned, map smallint(5) unsigned, position_x float, position_y float, position_z float, orientation float, spawntimesecsmin int(10) unsigned, spawntimesecsmax int(10) unsigned, wander_distance float, health_percent float, mana_percent float unsigned, movement_type tinyint(3) unsigned, spawn_flags int(10) unsigned, visibility_mod float?, patch_min tinyint(3) unsigned, patch_max tinyint(3) unsigned
- `creature_battleground`: guid int(10) unsigned PK, event1 tinyint(3) unsigned PK, event2 tinyint(3) unsigned
- `gameobject`: guid int(10) unsigned PK, id mediumint(8) unsigned, map smallint(5) unsigned, position_x float, position_y float, position_z float, orientation float, rotation0 float, rotation1 float, rotation2 float, rotation3 float, spawntimesecsmin int(11), spawntimesecsmax int(11), animprogress tinyint(3) unsigned, state tinyint(3) unsigned, spawn_flags int(10) unsigned, visibility_mod float?, patch_min tinyint(3) unsigned, patch_max tinyint(3) unsigned
- `gameobject_battleground`: guid int(10) unsigned PK, event1 tinyint(3) unsigned PK, event2 tinyint(3) unsigned PK

*`?` = nullable, `PK` = primary key column.*

