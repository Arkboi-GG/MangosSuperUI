<!-- provenance: boundary-bleed -->
# WorldSession.LFGHandler

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# WorldSession.LFGHandler

## Purpose & Responsibilities

The `WorldSession.LFGHandler` partial implements the server-side logic for **Meeting Stone** interactions within the `wowvmangos` core. Meeting Stones are specific `GameObject`s (`GAMEOBJECT_TYPE_MEETINGSTONE`) that allow players or groups to join a random Dungeon Finder (LFG) queue for a specific zone or dungeon area.

This unit handles three primary client opcodes:
1.  **Join**: A player interacts with a Meeting Stone to enter the LFG queue.
2.  **Leave**: A player or group leader explicitly leaves the LFG queue.
3.  **Info**: The client requests the current LFG status (typically on login or zone change) to synchronize UI state.

It acts as the bridge between the `WorldSession` (representing the connected player) and the global `LFGMgr`/`LFGQueue` systems. It enforces basic eligibility rules (e.g., only party leaders can queue groups, raids cannot use Meeting Stones) before delegating the actual queue management to the `LFGMgr` subsystem. It does not perform database operations directly; all persistence and complex queue logic are handled by downstream units.

## Member-by-Member Behavior

### Joining the Queue
**`HandleMeetingStoneJoinOpcode`** processes the `CMSG_MEETINGSTONE_JOIN` packet. It performs a series of validation checks before adding the player or group to the queue:
1.  **Remote Control Check**: Ignores the request if the player is currently controlling another entity (`Player.Main/IsSelfMover` returns false).
2.  **Object Validation**: Retrieves the `GameObject` referenced by the packet GUID using `Player.Main/GetGameObjectIfCanInteractWith`. If the object is invalid or the player cannot interact with it, the request is dropped.
3.  **Type Verification**: Ensures the object is strictly a `GAMEOBJECT_TYPE_MEETINGSTONE` via `GameObject/GetGoType`. If not, it logs an error via `Log.Main/Out` and aborts. This guards against malformed packets or unexpected object types.
4.  **Group Eligibility**: If the player is in a group (retrieved via `Player.Main/GetGroup`):
    *   Only the **group leader** can initiate the join. Non-leaders receive a `MEETINGSTONE_FAIL_PARTYLEADER` failure message. Checked via `Group/IsLeader`.
    *   **Raids** are explicitly forbidden from using Meeting Stones (`MEETINGSTONE_FAIL_RAID_GROUP`). Checked via `Group/isRaidGroup`.
    *   **Full groups** cannot queue (`MEETINGSTONE_FAIL_FULL_GROUP`). Checked via `Group/IsFull`.
5.  **Queue Submission**: If all checks pass, it retrieves the `areaID` from the Meeting Stone's template via `ObjectMgr/GetGameObjectTemplate` and calls `LFGMgr/AddToQueue` to register the player/group.

### Leaving the Queue
**`HandleMeetingStoneLeaveOpcode`** processes the `CMSG_MEETINGSTONE_LEAVE` packet. It distinguishes between solo players and group leaders:
1.  **Group Leader**: If the player is in a group, is the leader (`Group/IsLeader`), and the group is currently in an LFG queue (`Group/IsInLFG`), it schedules a removal of the entire group from the queue. It accesses the global queue via `World/GetLFGQueue`, gets the messenger via `LFGQueue/GetMessager`, and submits a lambda that calls `LFGQueue/RemoveGroupFromQueue`. This ensures thread-safe execution within the LFG subsystem.
2.  **Non-Leader in Group**: If the player is in a group but not the leader (or the group isn't in LFG), it simply sends a status update to the client indicating they are no longer queued (`SendMeetingstoneSetqueue` with `MEETINGSTONE_STATUS_NONE`). It does *not* attempt to remove the group from the queue.
3.  **Solo Player**: If the player is not in a group, it schedules the removal of the individual player from the queue. It accesses the global queue via `World/GetLFGQueue`, gets the messenger via `LFGQueue/GetMessager`, and submits a lambda that calls `LFGQueue/RemovePlayerFromQueue`.

### Status Synchronization
**`HandleMeetingStoneInfoOpcode`** processes `CMSG_MEETINGSTONE_INFO`. This is typically called by the client to refresh its UI state.
1.  **Group Context**: If the player is in a group (via `Player.Main/GetGroup`):
    *   If the group is in an LFG queue (`Group/IsInLFG`), it sends the current `LFGAreaId` (via `Group/GetLFGAreaId`) and `MEETINGSTONE_STATUS_JOINED_QUEUE` to the client using `SendMeetingstoneSetqueue`.
    *   Otherwise, it sends `MEETINGSTONE_STATUS_NONE`.
2.  **Solo Context**: If the player is solo, it attempts to restore offline player state. It includes a defensive null-check for `_player` and `_player->GetSession()` (via `Player.Main/GetSession`). If valid, it submits a task to `LFGQueue/RestoreOfflinePlayer` via the messenger obtained from `World/GetLFGQueue` and `LFGQueue/GetMessager`. This likely checks if the player had an active queue entry that persisted through a disconnect/reconnect cycle.

### Client Communication
**`SendMeetingstoneFailed`** constructs and sends an `SMSG_MEETINGSTONE_JOINFAILED` packet containing a single `uint8` status code explaining why the join failed (e.g., not leader, raid group). It uses `WorldPacket/WorldPacket#4` for construction, `ByteBuffer/operator<<#7` for serialization, and `WorldSession.Main/SendPacket` for transmission.

**`SendMeetingstoneSetqueue`** constructs and sends an `SMSG_MEETINGSTONE_SETQUEUE` packet containing a `uint32` area ID and a `uint8` status. It uses `WorldPacket/WorldPacket#4` for construction, `ByteBuffer/operator<<#10` and `ByteBuffer/operator<<#7` for serialization, and `WorldSession.Main/SendPacket` for transmission. This updates the client's visual representation of the LFG queue state.

## Cross-Unit Boundaries

### Collaboration with `Group`
*   **Direction**: `WorldSession.LFGHandler` → `Group`
*   **Purpose**: Validation and State Retrieval.
*   **Details**:
    *   `HandleMeetingStoneJoinOpcode` calls `Group/IsLeader`, `Group/isRaidGroup`, and `Group/IsFull` to enforce queuing rules.
    *   `HandleMeetingStoneLeaveOpcode` and `HandleMeetingStoneInfoOpcode` call `Group/IsInLFG` to determine if the group is actively queued.
    *   `HandleMeetingStoneInfoOpcode` calls `Group/GetLFGAreaId` to report the correct zone back to the client.
    *   `Player.Main/GetGroup` is used throughout to retrieve the current group context.

### Collaboration with `LFGMgr` / `LFGQueue`
*   **Direction**: `WorldSession.LFGHandler` → `LFGMgr` / `LFGQueue`
*   **Purpose**: Queue Management.
*   **Details**:
    *   `HandleMeetingStoneJoinOpcode` delegates the actual addition to `LFGMgr/AddToQueue`.
    *   `HandleMeetingStoneLeaveOpcode` uses `World/GetLFGQueue` to access the global queue instance. It then uses `LFGQueue/GetMessager` to submit lambdas that call `LFGQueue/RemoveGroupFromQueue` or `LFGQueue/RemovePlayerFromQueue`. This indirection suggests the LFG queue operates on a separate thread or requires synchronization, hence the messenger pattern.
    *   `HandleMeetingStoneInfoOpcode` similarly uses the messenger to call `LFGQueue/RestoreOfflinePlayer`.

### Collaboration with `ObjectMgr`
*   **Direction**: `WorldSession.LFGHandler` → `ObjectMgr`
*   **Purpose**: Data Lookup.
*   **Details**: `HandleMeetingStoneJoinOpcode` calls `ObjectMgr/GetGameObjectTemplate` to fetch the `meetingstone.areaID` associated with the specific Meeting Stone entry.

### Collaboration with `Player`
*   **Direction**: `WorldSession.LFGHandler` → `Player`
*   **Purpose**: Context Access.
*   **Details**:
    *   `Player.Main/GetGameObjectIfCanInteractWith` validates the target object.
    *   `Player.Main/IsSelfMover` checks remote control state.
    *   `Player.Main/GetGroup` retrieves the group pointer.
    *   `Player.Main/GetSession` is used defensively in `HandleMeetingStoneInfoOpcode`.

### Collaboration with `Log`
*   **Direction**: `WorldSession.LFGHandler` → `Log`
*   **Purpose**: Error Reporting.
*   **Details**: `HandleMeetingStoneJoinOpcode` calls `Log.Main/Out` if a non-MeetingStone object triggers the join opcode, aiding in debugging potential exploits or bugs.

### Collaboration with `WorldSession` (Other Partials)
*   **Direction**: `WorldSession.LFGHandler` → `WorldSession`
*   **Purpose**: Network Transmission.
*   **Details**:
    *   `SendMeetingstoneFailed` and `SendMeetingstoneSetqueue` call `WorldSession.Main/SendPacket` to transmit responses to the client. Note that `WorldSession.Main/SendPacket` is implemented in another partial of the `WorldSession` class, not in this unit.

## Data Model

This unit does not directly access any database tables. All data interactions are mediated through the `LFGMgr` and `Group` subsystems, which handle persistence. The `Tables` column in the MAP is empty, confirming no direct SQL queries or table references exist in this source file.

## Notable Implementation Details

1.  **Thread Safety via Messenger**: The `LFGQueue` interactions in `HandleMeetingStoneLeaveOpcode` and `HandleMeetingStoneInfoOpcode` use `GetMessager().AddMessage()`. This indicates that the `LFGQueue` is not thread-safe for direct access from the `WorldSession` thread (which handles network I/O). The messenger likely queues these operations for execution in a dedicated LFG update loop. Maintainers must ensure that any new LFG-related logic added here follows this pattern if it modifies queue state.

2.  **Strict Type Checking**: `HandleMeetingStoneJoinOpcode` explicitly checks `obj->GetGoType() != GAMEOBJECT_TYPE_MEETINGSTONE`. This is a critical security/stability check. Without it, a malicious client could potentially trigger LFG queue logic using arbitrary GameObjects, leading to undefined behavior or exploits. The error log helps identify such anomalies.

3.  **Leader-Only Group Queuing**: The logic enforces that only the group leader can queue a group via Meeting Stone. Non-leaders who attempt to join are rejected with `MEETINGSTONE_FAIL_PARTYLEADER`. This prevents conflicting queue states within a group.

4.  **Raid Exclusion**: Raids (`isRaidGroup()`) are explicitly blocked from using Meeting Stones. This aligns with typical WoW mechanics where Raids use different queue mechanisms (e.g., Raid Finder or manual grouping) rather than the standard LFG Meeting Stone interface.

5.  **Defensive Null Checks**: In `HandleMeetingStoneInfoOpcode`, there is a check `if (!_player || !_player->GetSession()) return;`. While `_player` is a member of `WorldSession` and usually valid during packet handling, this check protects against edge cases where the session might be in a transitional state (e.g., logout in progress).

6.  **No Direct Queue Removal for Non-Leaders**: In `HandleMeetingStoneLeaveOpcode`, if a non-leader in a group sends the leave opcode, the server only updates the client's UI (`SendMeetingstoneSetqueue(0, MEETINGSTONE_STATUS_NONE)`). It does *not* remove the group from the queue. This implies that only the leader can disband the group's queue entry, or that the client is expected to handle the UI state locally for non-leaders. This could be a point of confusion if the client expects the group to leave the queue when any member clicks "leave".

## Member Reference

**HandleMeetingStoneJoinOpcode**: Validates the player's interaction with a Meeting Stone GameObject. Checks for remote control, object validity, and type. Enforces that only group leaders can queue groups, and that raids/full groups are excluded. Retrieves the area ID from the object template and adds the player/group to the LFG queue via `LFGMgr`. Logs errors for invalid object types.

**HandleMeetingStoneLeaveOpcode**: Handles the client request to leave the LFG queue. If the player is a group leader and the group is queued, it schedules the group's removal from the queue via the LFG messenger. If the player is solo, it schedules individual removal. If the player is a non-leader in a group, it only updates the client UI to show they are no longer queued, without affecting the group's queue status.

**HandleMeetingStoneInfoOpcode**: Synchronizes the client's LFG UI state. For groups, it reports the current LFG area ID and joined status. For solo players, it triggers a restoration of offline player queue state via the LFG messenger. Includes defensive null checks for the player and session.

**SendMeetingstoneFailed**: Constructs and sends an `SMSG_MEETINGSTONE_JOINFAILED` packet to the client, containing a single byte status code indicating the reason for the join failure.

**SendMeetingstoneSetqueue**: Constructs and sends an `SMSG_MEETINGSTONE_SETQUEUE` packet to the client, containing the area ID and queue status. Used to update the client's visual representation of the LFG queue state after joining, leaving, or syncing.

---

<!-- machine-true, projected from graph.json -->

## Map — WorldSession.LFGHandler

*Source:* LFGHandler.cpp, WorldSession.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| HandleMeetingStoneJoinOpcode | method | GameObject/GetGoType, Group/IsFull, Group/IsLeader, Group/isRaidGroup, LFGMgr/AddToQueue, Log.Main/Out, Object/GetEntry, Object/GetObjectGuid, ObjectMgr/GetGameObjectTemplate, Player.Main/GetGameObjectIfCanInteractWith, Player.Main/GetGroup, Player.Main/IsSelfMover | — | — |
| HandleMeetingStoneLeaveOpcode | method | Group/IsInLFG, Group/IsLeader, LFGQueue/GetMessager, LFGQueue/RemoveGroupFromQueue, LFGQueue/RemovePlayerFromQueue, Object/GetObjectGuid, Player.Main/GetGroup, World/GetLFGQueue | — | — |
| HandleMeetingStoneInfoOpcode | method | Group/GetLFGAreaId, Group/IsInLFG, LFGQueue/GetMessager, LFGQueue/RestoreOfflinePlayer, Object/GetObjectGuid, Player.Main/GetGroup, Player.Main/GetSession, World/GetLFGQueue | — | — |
| SendMeetingstoneFailed | method | ByteBuffer/operator<<#7, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| SendMeetingstoneSetqueue | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#7, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | game_Group_Group/Disband, game_Group_Group/RemoveMember, LFGQueue/AddPlayer, LFGQueue/RestoreOfflinePlayer | — |

---

<!-- verify: boundary-bleed | foreign: update, WorldSession -->
