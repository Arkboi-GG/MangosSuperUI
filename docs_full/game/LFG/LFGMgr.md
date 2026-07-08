# LFGMgr

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# LFGMgr

**Purpose & Responsibilities**

`LFGMgr` (Looking For Group Manager) is the singleton entry point for the server-side logic governing the "Meeting Stone" or Dungeon Finder system. Its primary responsibility is to mediate between player actions (joining a queue, changing groups) and the underlying `LFGQueue` system that performs matchmaking.

It handles two distinct workflows:
1.  **Queue Entry:** Validating whether a player is joining solo or as a group leader, calculating their potential roles (Tank, Healer, Damage) based on class and talent specialization, and submitting them to the queue via asynchronous messaging.
2.  **Group State Updates:** Reacting to players joining or leaving existing LFG groups to recalculate the group's role composition and update the queue's internal state.

Additionally, `LFGMgr` provides static utility functions for determining player roles based on class/talents, assigning priority weights for matchmaking, and constructing the network packets required to communicate queue status changes to clients. It does not store persistent state itself; it relies on `LFGQueue` for queue management and `Group`/`Player` objects for entity state.

## Member-by-Member Behavior

### Queue Management and Entry

**`AddToQueue`**
This is the main interface for entering the LFG system. It accepts a `Player*` (expected to be the initiator) and a `queueAreaID`.
*   **Group Join:** If the player is in a `Group` and is the leader (`Group/IsLeader`), the entire group is queued. The method calculates the group's collective roles using `game_Group_Group/CalculateLFGRoles`, sets the group's LFG area ID, and prepares an `LFGGroupQueueInfo` structure. It then uses `LFGQueue/GetMessager` to asynchronously dispatch a command to `LFGQueue/AddGroup`.
*   **Solo Join:** If the player is not in a group, they are queued individually. The method determines the player's role using either `CalculateTalentRoles` or `CalculateRoles` depending on the `CONFIG_BOOL_LFG_MATCHMAKING` world configuration. It prepares an `LFGPlayerQueueInfo` structure and asynchronously dispatches a command to `LFGQueue/AddPlayer`.
*   **Edge Case:** If the player is in a group but *not* the leader, the method silently returns without action. Only leaders can queue groups.

**`UpdateGroup`**
Triggered when a player joins or leaves an existing LFG group. It checks if the group is full (`Group/IsFull`). If not full, it recalculates the group's roles via `game_Group_Group/CalculateLFGRoles`. It then asynchronously dispatches an update to `LFGQueue/UpdateGroup` with the new role info, the join/leave status, and the player's GUID.

### Role Calculation Logic

These static methods determine how a player or group contributes to the matchmaking algorithm.

**`CalculateRoles`**
A static fallback method that assigns roles based strictly on character class, ignoring talents. For example, Druids are always Tank/Damage/Healer, while Hunters are always Damage. This is used when detailed talent-based matchmaking is disabled.

**`CalculateTalentRoles`**
A static method that assigns roles based on the player's highest-invested talent tree. It calls `GetHighestTalentTree` to determine specialization.
*   **Priest:** Holy (Tab 2) = Healer; Shadow (Tab 1) = Damage.
*   **Shaman:** Restoration (Tab 2) = Healer; Elemental/Enhancement (Tabs 0/1) = Damage.
*   **Warrior:** Protection (Tab 2) = Tank; Arms/Fury (Tabs 0/1) = Damage.
*   **Paladin:** Holy (Tab 0) = Healer; Protection (Tab 1) = Tank; Retribution (Tab 2) = Damage.
*   **Druid:** Balance (Tab 0) = Damage; Feral (Tab 1) = Tank/Damage; Restoration (Tab 2) = Healer.
*   **Others:** Default to Damage.

**`GetHighestTalentTree`**
Determines which talent tab a player has invested the most points in.
*   It first checks if the player is level 10 or higher.
*   It calls `GetTalentTrees` to get a map of tab IDs to point counts.
*   It iterates through the three possible tabs (0, 1, 2) to find the one with the maximum points.
*   **Fallback:** If the player has no talents (or is below level 10), it returns a hardcoded default tab based on class (e.g., Shaman defaults to Tab 2, Priest to Tab 1).

**`GetTalentTrees`**
Iterates through all talent entries in the database (`sTalentStore`). For each talent belonging to the player's class mask, it checks the player's spells (`Player.Main/HasSpell`) to determine the highest rank learned. It aggregates these ranks into a `std::map<uint32, int32>` where keys are tab IDs and values are total points in that tab.

**`GetPriority`**
Assigns a matchmaking priority weight (`LFG_PRIORITY_HIGH`, `NORMAL`, `LOW`, or `NONE`) to a specific class/role combination. This influences how quickly a player is matched.
*   **High Priority:** Warriors (Tank), Druids/Paladins/Priests/Shamans (Healer), Hunters/Mages/Rogues/Warlocks (Damage).
*   **Normal Priority:** Druids/Paladins/Warriors (Tank), Druids/Paladins/Shamans/Warriors (Damage).
*   **Low Priority:** Priests (Damage).

**`GetMaximumDPSSlots`**
Returns a constant value of `3`. This likely defines the maximum number of Damage Per Second (DPS) slots allowed in a standard LFG group composition calculation.

### Network Packet Construction

These static methods construct `WorldPacket` objects to send status updates to clients. They are called by `LFGQueue` when queue states change.

**`BuildSetQueuePacket`**
Constructs `SMSG_MEETINGSTONE_SETQUEUE`. Contains the area ID and a status byte. Used to notify players of queue entry/removal.

**`BuildMemberAddedPacket`**
Constructs `SMSG_MEETINGSTONE_MEMBER_ADDED`. Contains the GUID of the player added to the group. Used when a solo player is matched into a group or a group is formed.

**`BuildInProgressPacket`**
Constructs `SMSG_MEETINGSTONE_IN_PROGRESS`. An empty packet indicating the matchmaking process has started or is ongoing.

**`BuildCompletePacket`**
Constructs `SMSG_MEETINGSTONE_COMPLETE`. An empty packet indicating the queue process has finished (likely upon successful match or timeout).

## Cross-Unit Boundaries

*   **`LFGQueue`**: `LFGMgr` acts as a facade for `LFGQueue`. All actual queue manipulation (`AddGroup`, `AddPlayer`, `UpdateGroup`) is performed by `LFGQueue`. `LFGMgr` uses `LFGQueue/GetMessager` to send commands asynchronously, ensuring thread safety or separation of concerns between the manager logic and the queue engine.
*   **`Group`**: `LFGMgr` queries `Group` for leadership status, member count, and role calculations. It also sets the LFG Area ID on the group object.
*   **`Player` / `Unit`**: `LFGMgr` reads player attributes (Name, Team, Class, Level, Spells) to calculate roles and priorities. It sets the LFG Area ID on solo players.
*   **`World`**: `LFGMgr` accesses `World/GetLFGQueue` to obtain the queue instance and `World/getConfig` to check if talent-based matchmaking is enabled.
*   **`Map.ScriptCommands`**: `ScriptCommand_MeetingStone` calls `AddToQueue`, indicating that interacting with a Meeting Stone NPC triggers this logic.
*   **`WorldSession.LFGHandler`**: `HandleMeetingStoneJoinOpcode` calls `AddToQueue`, handling the client opcode for joining the queue.

## Data Model

`LFGMgr` does not directly interact with any database tables. All data operations are performed in-memory via `Player`, `Group`, and `LFGQueue` objects. The `LFGQueue` unit likely persists queue state to the database, but `LFGMgr` is agnostic to this storage layer.

## Notable Implementation Details

*   **Asynchronous Queue Updates:** `AddToQueue` and `UpdateGroup` do not call `LFGQueue` methods directly. Instead, they use `LFGQueue/GetMessager().AddMessage()` with lambdas. This suggests that `LFGQueue` operates on a separate thread or event loop, and direct calls would cause race conditions.
*   **Leader-Only Group Queuing:** In `AddToQueue`, if a player is in a group but not the leader, the function returns immediately. This enforces that only group leaders can initiate group queues. Solo players are handled in the `else if (!grp)` branch.
*   **Talent Matchmaking Config:** The behavior of `AddToQueue` changes based on `CONFIG_BOOL_LFG_MATCHMAKING`. If true, it uses `CalculateTalentRoles`; otherwise, it uses the simpler `CalculateRoles`. This allows server operators to toggle between strict role requirements and class-based defaults.
*   **Hardcoded Defaults:** `GetHighestTalentTree` has hardcoded fallback tabs for classes if no talents are learned. This ensures that low-level or un-specialized players still get assigned a role rather than failing to queue.
*   **Static Utilities:** Most role calculation and packet building logic is static, meaning `LFGMgr` instances are not required to perform these calculations. This allows other parts of the codebase to use these utilities without needing access to the singleton instance.

## Member Reference

**`AddToQueue`**: Entry point for joining the LFG queue. Handles both solo players and group leaders. Calculates roles, sets LFG area IDs, and asynchronously dispatches add commands to `LFGQueue`. Returns immediately if the player is in a group but not the leader.

**`LFGMgr`**: Constructor. Empty.

**`~LFGMgr`**: Destructor. Empty.

**`GetMaximumDPSSlots`**: Static method returning the constant `3`, representing the max DPS slots in a group.

**`CalculateRoles`**: Static method assigning roles (Tank/Healer/Damage) based solely on character class, ignoring talents.

**`GetPriority`**: Static method assigning matchmaking priority weights (High/Normal/Low/None) based on class and role.

**`GetTalentTrees`**: Static method iterating through all talents to count points per tab for a player, returning a map of tab ID to point count.

**`GetHighestTalentTree`**: Static method determining the player's primary talent specialization by finding the tab with the most points. Falls back to hardcoded defaults if no talents are learned.

**`CalculateTalentRoles`**: Static method assigning roles based on the player's highest-invested talent tree. Uses `GetHighestTalentTree` to determine specialization.

**`UpdateGroup`**: Updates the LFG queue when a player joins or leaves a group. Recalculates group roles if not full and asynchronously dispatches an update to `LFGQueue`.

**`BuildSetQueuePacket`**: Static method constructing `SMSG_MEETINGSTONE_SETQUEUE` packet with area ID and status.

**`BuildMemberAddedPacket`**: Static method constructing `SMSG_MEETINGSTONE_MEMBER_ADDED` packet with player GUID.

**`BuildInProgressPacket`**: Static method constructing empty `SMSG_MEETINGSTONE_IN_PROGRESS` packet.

**`BuildCompletePacket`**: Static method constructing empty `SMSG_MEETINGSTONE_COMPLETE` packet.

---

<!-- machine-true, projected from graph.json -->

## Map — LFGMgr

*Source:* LFGMgr.cpp, LFGMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AddToQueue | method | game_Group_Group/CalculateLFGRoles, Group/GetMembersCount, Group/IsLeader, Group/SetLFGAreaId, LFGQueue/AddGroup, LFGQueue/AddPlayer, LFGQueue/CalculateRoles, LFGQueue/CalculateTalentRoles, LFGQueue/GetMessager, Object/GetObjectGuid, Player.Main/GetGroup, Player.Main/GetName, Player.Main/GetTeam, Player.Main/SetLFGAreaId, Unit.Main/GetClass, World/getConfig, World/GetLFGQueue | game_Group_Group/RemoveMember, LFGQueue/Update, Map.ScriptCommands/ScriptCommand_MeetingStone, WorldSession.LFGHandler/HandleMeetingStoneJoinOpcode | — |
| LFGMgr | ctor | — | — | — |
| ~LFGMgr | dtor | — | — | — |
| GetMaximumDPSSlots | method | — | game_Group_Group/FillPremadeLFG, LFGQueue/FindRoleToGroup | — |
| CalculateRoles | method | — | game_Group_Group/CalculateLFGRoles, LFGQueue/CalculateRoles | — |
| GetPriority | method | — | game_Group_Group/FillPremadeLFG, LFGQueue/CalculateRoles, LFGQueue/CalculateTalentRoles | — |
| GetTalentTrees | method | Player.Main/HasSpell, Unit.Main/GetClassMask | — | — |
| GetHighestTalentTree | method | Unit.Main/GetClass, Unit.Main/GetLevel | — | — |
| CalculateTalentRoles | method | Unit.Main/GetClass | game_Group_Group/CalculateLFGRoles, LFGQueue/CalculateTalentRoles | — |
| UpdateGroup | method | game_Group_Group/CalculateLFGRoles, Group/IsFull, LFGQueue/GetMessager, LFGQueue/UpdateGroup, World/GetLFGQueue | game_Group_Group/AddMember, game_Group_Group/RemoveMember | — |
| BuildSetQueuePacket | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#7, WorldPacket/Initialize | LFGQueue/AddGroup, LFGQueue/RemoveGroupFromQueue, LFGQueue/RemovePlayerFromQueue | — |
| BuildMemberAddedPacket | method | ByteBuffer/operator<<#11, WorldPacket/Initialize | LFGQueue/FindRoleToGroup, LFGQueue/Update | — |
| BuildInProgressPacket | method | WorldPacket/Initialize | LFGQueue/Update | — |
| BuildCompletePacket | method | WorldPacket/Initialize | LFGQueue/RemoveGroupFromQueue | — |
