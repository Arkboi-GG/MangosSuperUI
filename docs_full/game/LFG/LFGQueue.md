# LFGQueue

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# LFGQueue

**Purpose & Responsibilities**

`LFGQueue` implements the core matchmaking logic for the Looking For Group (LFG) system, specifically handling the "Meeting Stone" or dungeon queue mechanics. It maintains two primary data structures: `m_queuedPlayers`, tracking individual players waiting to join a group, and `m_queuedGroups`, tracking existing groups seeking additional members.

The unit operates as a continuous background task via its `Update()` method, which runs in a loop until the server stops. Its responsibilities include:
1.  **Queue Management:** Adding and removing players and groups from the queue, updating their status timers, and notifying clients of state changes (joined, left, completed).
2.  **Matchmaking Logic:** Matching individual players to existing groups based on role compatibility (Tank, Healer, Damage), faction (team), and dungeon area. It also forms new groups from scratch when sufficient solo players are available.
3.  **Priority Handling:** Calculating and applying queue priority based on time spent in the queue and class-specific role suitability.
4.  **Thread Safety:** Using a `Messager` pattern to defer all interactions with `Player` and `Group` objects to the main world thread, ensuring that the matchmaking loop (which may run on a separate thread or context) does not cause race conditions with game object state.

This unit does not interact with any database tables; all state is held in memory within the `LFGQueue` instance.

## Member-by-Member Behavior

### Matchmaking Core (`Update`)

The heart of the unit is `Update()`, which executes a continuous loop. In each iteration:
1.  It calculates the time delta since the last iteration.
2.  It processes pending messages via `GetMessager().Execute(this)`.
3.  It updates `m_queuedPlayers`:
    *   Increments `timeInLFG` for each player.
    *   Grants `hasQueuePriority` if a player has waited 30 minutes.
    *   If matchmaking is enabled and a player has waited longer than the configured `CONFIG_UINT32_LFG_MATCHMAKING_TIMER` (default 5 minutes), it clears their talent-based role priorities and recalculates roles based solely on class (`CalculateRoles`). This prevents players from being stuck due to strict talent requirements after a long wait.
4.  It attempts to fill `m_queuedGroups`:
    *   Iterates through queued groups. For each group, it searches `m_queuedPlayers` for candidates matching the group's `team` and `areaId`.
    *   For each candidate, it checks if the player's `roleMask` overlaps with the group's `availableRoles`.
    *   If a match is found, it calls `FindRoleToGroup` to verify priority and execute the join.
    *   If a group reaches 5 members, it is removed from the queue via `RemoveGroupFromQueue`.
    *   Groups that haven't been filled for 5 minutes receive a broadcast packet indicating they are still waiting (`BuildInProgressPacket`).
5.  It attempts to form new groups from solo players:
    *   If there are at least `m_groupSize` (5) players in the queue, it picks the first player as a leader.
    *   It finds other players in the same area/team using `FindInArea`.
    *   If enough players are found, it removes the leader and one member from the player queue, creates a new `Group` object, adds the member to it, and adds the new group to the queue via `LFGMgr::AddToQueue`. Note: The code snippet shows adding only one member explicitly in the lambda, implying the rest might be handled by subsequent iterations or a simplified logic path for initial formation.

### Role Calculation (`LFGPlayerQueueInfo` methods)

These methods belong to the `LFGPlayerQueueInfo` struct, which holds state for individual players in the queue.

*   **`CalculateRoles`**: Sets the `roleMask` based on the player's class using `LFGMgr::CalculateRoles`. It then populates `rolePriority` by querying `LFGMgr::GetPriority` for Tank, Healer, and Damage roles.
*   **`CalculateTalentRoles`**: Similar to `CalculateRoles`, but uses `LFGMgr::CalculateTalentRoles` to determine the `roleMask` based on the player's current talents. It still uses class-based priority for the `rolePriority` vector.
*   **`GetRolePriority`**: Returns the priority value for a specific role from the `rolePriority` vector, or `LFG_PRIORITY_NONE` if not found.

### Queue Entry & Exit

*   **`AddPlayer`**: Inserts a player into `m_queuedPlayers`. It schedules a message to send a `MEETINGSTONE_STATUS_JOINED_QUEUE` packet to the player's session.
*   **`AddGroup`**: Inserts a group into `m_queuedGroups`. It schedules a message to broadcast a `MEETINGSTONE_STATUS_JOINED_QUEUE` packet to all group members.
*   **`RemovePlayerFromQueue`**: Removes a player from `m_queuedPlayers`. If the leave method is `PLAYER_CLIENT_LEAVE`, it schedules a message to send a `MEETINGSTONE_STATUS_LEAVE_QUEUE` packet and reset the player's LFG area ID.
*   **`RemoveGroupFromQueue`**: Removes a group from `m_queuedGroups`. Depending on the leave method, it broadcasts either a `LEAVE_QUEUE` packet or a `COMPLETE` packet followed by a `NONE` status reset. It also resets the group's LFG area ID.

### Matchmaking Helpers

*   **`FindInArea`**: Iterates through `m_queuedPlayers` to find all players in a specific `area` and `team`, excluding a specified `exclude` GUID. Used to find potential group mates for new group formation.
*   **`FindRoleToGroup`**: The critical decision point for adding a player to a group.
    1.  Validates that both the player and group are still in their respective queues.
    2.  Checks priority against other players in the queue who can fill the same role. It considers:
        *   Class-based role priority (`GetRolePriority`).
        *   Queue time priority (`hasQueuePriority`).
        *   Total time in queue (`timeInLFG`).
    3.  If the player wins the priority check, it updates the group's `availableRoles` mask and `dpsCount` (for Damage dealers).
    4.  It removes the player from the player queue.
    5.  It increments the group's `playerCount`.
    6.  It schedules a message to add the player to the actual `Group` object and broadcast a `MEMBER_ADDED` packet.

### State Maintenance

*   **`IsPlayerInQueue` / `IsGroupInQueue`**: Simple lookup functions to check if a player or group is currently in the queue maps.
*   **`UpdateGroup`**: Updates the info for an existing group in the queue. If the player count reaches 5, it triggers removal from the queue. This is called when players manually invite others into a queued group.
*   **`RestoreOfflinePlayer`**: Handles the re-entry of a player who was offline. It moves the player from `m_offlinePlayers` back to `m_queuedPlayers` and notifies the client. If the player is not found in offline storage, it notifies the client that they are not in a queue.

## Cross-Unit Boundaries

*   **`LFGMgr`**: `LFGQueue` relies heavily on `LFGMgr` for:
    *   Calculating roles and priorities (`CalculateRoles`, `CalculateTalentRoles`, `GetPriority`).
    *   Building network packets (`BuildInProgressPacket`, `BuildMemberAddedPacket`, `BuildSetQueuePacket`, `BuildCompletePacket`).
    *   Getting configuration values indirectly via `World`.
    *   Adding groups to the higher-level LFG manager (`AddToQueue`).
*   **`World`**: Used to access global configuration (`getConfig`), the messager system (`GetMessager`), and to check if the server is stopping (`IsStopped`).
*   **`ObjectMgr`**: Used to retrieve `Player` and `Group` objects by GUID or ID (`GetPlayer`, `GetGroupById`, `AddGroup`).
*   **`Player` / `Group`**: Interacted with primarily via the `Messager` to ensure thread safety. Actions include sending packets, modifying LFG state (`SetLFGAreaId`), and managing group membership (`AddMember`, `BroadcastPacket`).
*   **`WorldSession`**: Accessed via `Player::GetSession()` to send packets directly to clients.

## Data Model

This unit does not interact with any database tables. All queue state is maintained in-memory within `m_queuedPlayers`, `m_offlinePlayers`, and `m_queuedGroups`.

## Notable Implementation Details

*   **Thread Safety via Messager**: The `Update()` loop likely runs on a dedicated thread or context separate from the main game loop. To safely modify `Player` and `Group` objects, which are accessed by the main thread, `LFGQueue` uses `sWorld.GetMessager().AddMessage(...)`. This defers the actual modification to the main thread. This is a critical design pattern to avoid race conditions.
*   **Role Priority Decay**: After a configurable timeout (default 5 minutes), players lose their talent-based role restrictions and fall back to class-based roles. This prevents players from being indefinitely stuck in the queue because no group needs their specific talent specialization.
*   **Queue Priority**: Players gain a "queue priority" flag after 30 minutes. This flag influences the matchmaking algorithm in `FindRoleToGroup`, giving older queue entries precedence over newer ones with equal class priority.
*   **Group Formation Logic**: The logic for forming new groups from solo players in `Update()` is somewhat simplistic. It picks the first player as a leader and finds others in the same area. It then removes the leader and *one* member from the queue, creates a group, adds the member, and adds the group to the queue. The remaining players are presumably picked up in subsequent iterations. This suggests a step-wise group formation process.
*   **Iterator Invalidation**: The `Update()` loop carefully handles iterator invalidation when removing players or groups from the maps. For example, it pre-increments iterators before calling `FindRoleToGroup`, which may erase elements.
*   **Hardcoded Group Size**: `m_groupSize` is hardcoded to 5, reflecting the standard dungeon group size in World of Warcraft.

## Member Reference

**CalculateRoles**: Method in `LFGPlayerQueueInfo`. Calculates the player's role mask based on class and populates role priorities by calling `LFGMgr::CalculateRoles` and `LFGMgr::GetPriority`. Called by `LFGMgr::AddToQueue`.

**CalculateTalentRoles**: Method in `LFGPlayerQueueInfo`. Calculates the player's role mask based on talents and populates role priorities by calling `LFGMgr::CalculateTalentRoles`, `LFGMgr::GetPriority`, and `Unit.Main::GetClass`. Called by `LFGMgr::AddToQueue`.

**GetRolePriority**: Method in `LFGPlayerQueueInfo`. Returns the priority value for a given role from the internal `rolePriority` vector. No external calls or callers.

**Update**: Method in `LFGQueue`. The main matchmaking loop. Updates player timers, grants queue priority, decays talent roles, matches players to groups, forms new groups, and broadcasts status updates. Calls `Errors::PrintStacktraceAndThrow`, `game_Group_Group::AddMember`, `game_Group_Group::BroadcastPacket`, `game_Group_Group::Create`, `game_Group_Group::Group`, `Group::IsCreated`, `LFGMgr::AddToQueue`, `LFGMgr::BuildInProgressPacket`, `LFGMgr::BuildMemberAddedPacket`, `Object::GetObjectGuid`, `ObjectMgr::AddGroup`, `ObjectMgr::GetGroupById`, `ObjectMgr::GetPlayer`, `Player.Main::GetName`, `Player.Main::GetSession`, `World::getConfig`, `World::GetMessager`, `World::IsStopped`, `WorldPacket::WorldPacket`, and `WorldSession.Main::SendPacket`. Called by `World::SetInitialWorldSettings`.

**GetMessager**: Method in `LFGQueue`. Returns the internal `Messager` instance. Called by `game_Group_Group::Disband`, `game_Group_Group::RemoveMember`, `LFGMgr::AddToQueue`, `LFGMgr::UpdateGroup`, `WorldSession.LFGHandler::HandleMeetingStoneInfoOpcode`, `WorldSession.LFGHandler::HandleMeetingStoneLeaveOpcode`, and `WorldSession.Main::LogoutPlayer`.

**IsPlayerInQueue**: Method in `LFGQueue`. Checks if a player GUID exists in `m_queuedPlayers`. No external calls or callers.

**IsGroupInQueue**: Method in `LFGQueue`. Checks if a group ID exists in `m_queuedGroups`. No external calls or callers.

**UpdateGroup**: Method in `LFGQueue`. Updates the state of a group in the queue and removes it if full. Called by `LFGMgr::UpdateGroup`.

**AddGroup**: Method in `LFGQueue`. Adds a group to `m_queuedGroups` and schedules a notification packet. Calls `game_Group_Group::BroadcastPacket`, `LFGMgr::BuildSetQueuePacket`, `ObjectMgr::GetGroupById`, `World::GetMessager`, and `WorldPacket::WorldPacket`. Called by `LFGMgr::AddToQueue`.

**AddPlayer**: Method in `LFGQueue`. Adds a player to `m_queuedPlayers` and schedules a notification packet. Calls `ObjectMgr::GetPlayer`, `Player.Main::GetSession`, `World::GetMessager`, and `WorldSession.LFGHandler::SendMeetingstoneSetqueue`. Called by `LFGMgr::AddToQueue`.

**FindInArea**: Method in `LFGQueue`. Finds all players in a specific area and team, excluding one. Calls `ObjectGuid::operator==`. No external callers.

**FindRoleToGroup**: Method in `LFGQueue`. Matches a player to a group based on role and priority, then executes the join. Calls `game_Group_Group::AddMember`, `game_Group_Group::BroadcastPacket`, `LFGMgr::BuildMemberAddedPacket`, `LFGMgr::GetMaximumDPSSlots`, `ObjectGuid::operator==`, `ObjectMgr::GetGroupById`, `ObjectMgr::GetPlayer`, `Player.Main::GetName`, `World::GetMessager`, and `WorldPacket::WorldPacket`. No external callers.

**RemovePlayerFromQueue**: Method in `LFGQueue`. Removes a player from the queue and notifies the client if applicable. Calls `LFGMgr::BuildSetQueuePacket`, `ObjectMgr::GetPlayer`, `Player.Main::GetSession`, `Player.Main::SetLFGAreaId`, `World::GetMessager`, `WorldPacket::WorldPacket`, and `WorldSession.Main::SendPacket`. Called by `WorldSession.LFGHandler::HandleMeetingStoneLeaveOpcode` and `WorldSession.Main::LogoutPlayer`.

**RemoveGroupFromQueue**: Method in `LFGQueue`. Removes a group from the queue and notifies members. Calls `game_Group_Group::BroadcastPacket`, `Group::SetLFGAreaId`, `LFGMgr::BuildCompletePacket`, `LFGMgr::BuildSetQueuePacket`, `ObjectMgr::GetGroupById`, `World::GetMessager`, and `WorldPacket::WorldPacket`. Called by `game_Group_Group::Disband`, `game_Group_Group::RemoveMember`, and `WorldSession.LFGHandler::HandleMeetingStoneLeaveOpcode`.

**RestoreOfflinePlayer**: Method in `LFGQueue`. Restores a player from offline storage to the active queue. Calls `ObjectMgr::GetPlayer`, `Player.Main::GetSession`, `World::GetMessager`, and `WorldSession.LFGHandler::SendMeetingstoneSetqueue`. Called by `WorldSession.LFGHandler::HandleMeetingStoneInfoOpcode`.

---

<!-- machine-true, projected from graph.json -->

## Map — LFGQueue

*Source:* LFGQueue.cpp, LFGQueue.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| CalculateRoles | method | LFGMgr/CalculateRoles, LFGMgr/GetPriority | LFGMgr/AddToQueue | — |
| CalculateTalentRoles | method | LFGMgr/CalculateTalentRoles, LFGMgr/GetPriority, Unit.Main/GetClass | LFGMgr/AddToQueue | — |
| GetRolePriority | method | — | — | — |
| Update | method | Errors/PrintStacktraceAndThrow, game_Group_Group/AddMember, game_Group_Group/BroadcastPacket, game_Group_Group/Create, game_Group_Group/Group, Group/IsCreated, LFGMgr/AddToQueue, LFGMgr/BuildInProgressPacket, LFGMgr/BuildMemberAddedPacket, Object/GetObjectGuid, ObjectMgr/AddGroup, ObjectMgr/GetGroupById, ObjectMgr/GetPlayer, Player.Main/GetName, Player.Main/GetSession, World/getConfig, World/getConfig#4, World/GetMessager, World/IsStopped, WorldPacket/WorldPacket, WorldSession.Main/SendPacket | World/SetInitialWorldSettings | — |
| GetMessager | method | — | game_Group_Group/Disband, game_Group_Group/RemoveMember, LFGMgr/AddToQueue, LFGMgr/UpdateGroup, WorldSession.LFGHandler/HandleMeetingStoneInfoOpcode, WorldSession.LFGHandler/HandleMeetingStoneLeaveOpcode, WorldSession.Main/LogoutPlayer | — |
| IsPlayerInQueue | method | — | — | — |
| IsGroupInQueue | method | — | — | — |
| UpdateGroup | method | — | LFGMgr/UpdateGroup | — |
| AddGroup | method | game_Group_Group/BroadcastPacket, LFGMgr/BuildSetQueuePacket, ObjectMgr/GetGroupById, World/GetMessager, WorldPacket/WorldPacket | LFGMgr/AddToQueue | — |
| AddPlayer | method | ObjectMgr/GetPlayer, Player.Main/GetSession, World/GetMessager, WorldSession.LFGHandler/SendMeetingstoneSetqueue | LFGMgr/AddToQueue | — |
| FindInArea | method | ObjectGuid/operator== | — | — |
| FindRoleToGroup | method | game_Group_Group/AddMember, game_Group_Group/BroadcastPacket, LFGMgr/BuildMemberAddedPacket, LFGMgr/GetMaximumDPSSlots, ObjectGuid/operator==, ObjectMgr/GetGroupById, ObjectMgr/GetPlayer, Player.Main/GetName, World/GetMessager, WorldPacket/WorldPacket | — | — |
| RemovePlayerFromQueue | method | LFGMgr/BuildSetQueuePacket, ObjectMgr/GetPlayer, Player.Main/GetSession, Player.Main/SetLFGAreaId, World/GetMessager, WorldPacket/WorldPacket, WorldSession.Main/SendPacket | WorldSession.LFGHandler/HandleMeetingStoneLeaveOpcode, WorldSession.Main/LogoutPlayer | — |
| RemoveGroupFromQueue | method | game_Group_Group/BroadcastPacket, Group/SetLFGAreaId, LFGMgr/BuildCompletePacket, LFGMgr/BuildSetQueuePacket, ObjectMgr/GetGroupById, World/GetMessager, WorldPacket/WorldPacket | game_Group_Group/Disband, game_Group_Group/RemoveMember, WorldSession.LFGHandler/HandleMeetingStoneLeaveOpcode | — |
| RestoreOfflinePlayer | method | ObjectMgr/GetPlayer, Player.Main/GetSession, World/GetMessager, WorldSession.LFGHandler/SendMeetingstoneSetqueue | WorldSession.LFGHandler/HandleMeetingStoneInfoOpcode | — |
