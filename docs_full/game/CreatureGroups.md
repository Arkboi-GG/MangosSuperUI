# CreatureGroups

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# CreatureGroups

**Purpose & Responsibilities**

The `CreatureGroup` system manages coordinated behavior for clusters of non-player characters (NPCs) in the world. It allows multiple creatures to act as a single tactical unit, sharing states such as combat aggression, evasion, and respawning. Additionally, it supports "formation" movement, where member creatures maintain specific relative positions (distance and angle) to a leader, typically following a waypoint path.

The system is driven by configuration stored in the `creature_groups` and `creature_groups_entry_limit` database tables. At runtime, `CreatureGroupsManager` loads these configurations into `CreatureGroup` instances. Individual `Creature` objects link to a `CreatureGroup` via `Creature.Main/JoinCreatureGroup`. The group then intercepts lifecycle events (death, combat start/end, respawn) from its members to trigger synchronized actions across the entire group, such as aggroing all members when one attacks (`OnMemberAttackStart`) or respawning the whole group when the leader evades (`OnLeaveCombat`).

## Member-by-Member Behavior

### Group Lifecycle and Membership Management

These methods handle the creation, modification, and destruction of group membership data structures.

*   **`CreatureGroup` (Constructor)**: Initializes a new group with a specified leader GUID. It sets both the current leader and the original leader to this GUID, initializes options to zero, and resets internal guards and waypoints.
*   **`AddMember`**: Adds a creature GUID to the group's internal map (`m_members`). It stores the member's follow distance, angle, and flags. Crucially, it merges the member's flags into the group's global `m_options` bitmask. If the added GUID is the leader itself, it returns early to avoid duplication.
*   **`RemoveMember`**: Removes a specific GUID from the `m_members` map. It performs no side effects on the creature object itself (e.g., it does not call `LeaveCreatureGroup`); it only updates the local data structure.
*   **`DisbandGroup`**: Permanently destroys the group. It marks the group as deleted (`m_deleted = true`), removes the group from the global manager (`CreatureGroupsManager/EraseCreatureGroup`) if it was loaded from the database, and iterates through all members to detach them from the group (`Creature.Main/SetCreatureGroup`). If the group was a formation, it reinitializes the motion masters of alive members to stop them from following the now-defunct leader.
*   **`RemoveTemporaryLeader`**: Used when a temporary leader (assigned during combat or formation breaks) needs to be reverted. It resets the current leader GUID back to the original leader, removes the temporary leader from the member map, and reinitializes patrol movements for remaining members.

### Combat and State Synchronization

These methods are called by `Creature.Main` when a member enters or leaves significant states, propagating those states to other members based on group flags.

*   **`OnMemberAttackStart`**: Triggered when a member starts attacking. If the `OPTION_AGGRO_TOGETHER` flag is set, it calls `MemberAssist` on all other living members in the group, causing them to attack the same target. It also ensures the original leader assists if they are not the attacker.
*   **`OnMemberDied`**: Triggered when a member dies. It performs two main checks:
    1.  **Notification**: If `OPTION_INFORM_LEADER_ON_MEMBER_DIED` or `OPTION_INFORM_MEMBERS_ON_ANY_DIED` is set, it calls `CreatureAI/GroupMemberJustDied` on the leader or all members, respectively, passing context about who died.
    2.  **Leader Election**: If the deceased member was the current leader and the group uses formation movement (`IsFormation`), it searches for a new living member to become the leader. If found, it assigns the new leader the waypoint movement of the original leader (`Creature.MotionMaster/MoveWaypointAsDefault`) and reinitializes patrols for others. If no living members remain, it resets the leader to the original GUID.
*   **`OnLeaveCombat`**: Triggered when a member leaves combat (evades). If `OPTION_EVADE_TOGETHER` is set, it forces all other living members to evade (`CreatureAI/EnterEvadeMode`). It determines if the "master" (original leader) evaded. Based on `OPTION_RESPAWN_ALL_ON_MASTER_EVADE` or `OPTION_RESPAWN_ALL_ON_ANY_EVADE`, it may trigger `RespawnAll` to bring the entire group back online.
*   **`OnRespawn`**: Triggered when a member respawns. If the original leader respawns and a temporary leader was active, it restores the original leader as the current leader and reinitializes motion masters. If `OPTION_RESPAWN_TOGETHER` is set, it triggers `RespawnAll` to ensure the whole group is present.

### Movement and Formation Logic

*   **`ComputeRelativePosition`**: A helper method on the `CreatureGroupMember` struct. It calculates the X/Y offset for a member based on the leader's orientation, the member's configured `followAngle`, and `followDistance`. This is used by `WaypointMovementGenerator` to position followers correctly.
*   **`SetLastReachedWaypoint`**: Updates the internal tracker for the last waypoint reached by the group's leader. This is used to resume formation movement if the leader changes.
*   **`IsFormation`**: Returns true if the `OPTION_FORMATION_MOVE` flag is set in the group's options.
*   **`HasGroupFlag`**: Checks if a specific option flag is enabled for the group.

### Respawning Mechanics

*   **`RespawnAll`**: Iterates through all members (except the one triggering the call) and calls `Respawn` on them. It also ensures the original leader is respawned if they weren't the trigger.
*   **`Respawn`**: Handles the actual resurrection of a single creature. It uses a guard (`m_respawnGuard`) to prevent infinite recursion if `Creature.Main/Respawn` triggers group callbacks. It checks if the creature is eligible to spawn (respawn timer expired, battleground permissions). If the member is part of a formation, it calculates the correct position relative to the leader (using `ComputeRelativePosition`) and teleports the creature there before calling `Creature.Main/Respawn`.

### Database Persistence

*   **`SaveToDb`**: Persists the current group state to the `creature_groups` table. It first deletes existing entries for the leader (`DeleteFromDb`), then inserts rows for each member with their GUID, distance, angle, and flags.
*   **`DeleteFromDb`**: Removes all rows associated with the group's original leader from the `creature_groups` table.

### Utility and Accessors

*   **`GetLeaderGuid`**: Returns the current leader's GUID (which may change during combat/formation).
*   **`GetOriginalLeaderGuid`**: Returns the GUID of the creature designated as the leader in the database configuration.
*   **`GetMembers`**: Returns a constant reference to the internal map of members.
*   **`ContainsGuid`**: Checks if a specific GUID is a member of this group.
*   **`DoForAllMembers`**: Iterates over all members and executes a provided lambda function on each valid `Creature` object found in the world.
*   **`ChooseCreatureId`**: Determines which creature ID (from a spawn pool) a specific member should take. It respects limits defined in `creature_groups_entry_limit` (min/max counts per creature ID). It ensures minimum counts are met by checking unspawned members and available slots. If constraints cannot be met, it falls back to the default random selection.

## Cross-Unit Boundaries

### Collaboration with `Creature.Main`
*   **Direction**: Bidirectional.
*   **Details**: `Creature.Main` acts as the event source. It calls `CreatureGroup::OnMemberAttackStart`, `OnMemberDied`, `OnLeaveCombat`, and `OnRespawn` when the creature's state changes. Conversely, `CreatureGroup` methods often call back into `Creature.Main` to modify state, such as `Creature.Main/Respawn`, `Creature.Main/SetCreatureGroup`, or `Creature.Main/JoinCreatureGroup`.

### Collaboration with `CreatureAI`
*   **Direction**: Outbound from `CreatureGroup`.
*   **Details**: `CreatureGroup` uses `CreatureAI` to enforce behavioral synchronization. It calls `CreatureAI/AttackedBy` to force aggro, `CreatureAI/EnterEvadeMode` to force evasion, and `CreatureAI/GroupMemberJustDied` to notify AI scripts of group member deaths.

### Collaboration with `Map.Main`
*   **Direction**: Outbound from `CreatureGroup`.
*   **Details**: `CreatureGroup` frequently calls `Map.Main/GetCreature` to resolve `ObjectGuid`s into live `Creature` pointers. This is necessary because the group only stores GUIDs, and members may despawn or leave the world.

### Collaboration with `Creature.MotionMaster`
*   **Direction**: Outbound from `CreatureGroup`.
*   **Details**: For formation groups, `CreatureGroup` manipulates movement directly. It calls `Creature.MotionMaster/MoveWaypointAsDefault` to assign the leader's path to a new leader, `Creature.MotionMaster/ReInitializePatrolMovement` to reset followers, and `Creature.MotionMaster/Initialize` to clear movement generators when disbanded.

### Collaboration with `CreatureGroupsManager`
*   **Direction**: Outbound from `CreatureGroup` (for persistence) and Inbound to `CreatureGroup` (for loading).
*   **Details**: `CreatureGroup::DisbandGroup` calls `CreatureGroupsManager/EraseCreatureGroup` to remove itself from the global registry if it was DB-loaded. `CreatureGroupsManager::Load` creates `CreatureGroup` instances and populates them.

### Collaboration with `Database`
*   **Direction**: Outbound from `CreatureGroup`.
*   **Details**: `SaveToDb` and `DeleteFromDb` execute SQL statements to persist or remove group data from the `creature_groups` table.

## Data Model

The unit interacts with two database tables:

1.  **`creature_groups`**:
    *   **Usage**: Stores the definition of creature groups. Each row represents a member of a group.
    *   **Columns**:
        *   `leader_guid`: Identifies the group by its leader's GUID.
        *   `member_guid`: The GUID of the member creature. Primary Key.
        *   `dist`: Distance from the leader for formation positioning.
        *   `angle`: Angle relative to the leader's orientation for formation positioning.
        *   `flags`: Bitmask of options (e.g., formation move, aggro together) applied to this member/group.

2.  **`creature_groups_entry_limit`**:
    *   **Usage**: Defines constraints on which creature IDs can spawn within a group, ensuring variety or specific compositions.
    *   **Columns**:
        *   `leader_guid`: Links the limit rule to a specific group leader. Primary Key.
        *   `creature_id`: The creature template ID being constrained. Primary Key.
        *   `min_count`: Minimum number of this creature ID required in the group.
        *   `max_count`: Maximum number of this creature ID allowed in the group.

## Notable Implementation Details

*   **Recursion Guards**: The `Respawn` method uses a `m_respawnGuard` boolean to prevent infinite loops. Since `Creature.Main/Respawn` might trigger `OnRespawn` again, the guard ensures `CreatureGroup::Respawn` doesn't re-enter itself. Similarly, `MemberAssist` uses `m_assistGuard` to prevent recursive aggro chains.
*   **Leader Election Logic**: In `OnMemberDied`, if the leader dies in a formation group, the code picks the *first* living member found in the `m_members` map iteration as the new leader. This is deterministic based on map ordering but arbitrary regarding gameplay balance. The new leader inherits the original leader's waypoint progress.
*   **Memory Leak Acknowledgement**: The comment in `CreatureGroupsManager::Load` explicitly notes a memory leak: "Memory leak, but we cannot delete the loaded groups, since pointer may be present at loaded creatures." Groups created during `Load` are never freed unless explicitly erased via `DisbandGroup` (which only happens if the group is DB-persisted and disbanded). This implies that dynamically created groups or groups that aren't properly cleaned up will persist in memory until server restart.
*   **Formation Positioning**: `ComputeRelativePosition` assumes standard trigonometric orientation. The `Respawn` method adds `M_PI` (180 degrees) to the leader's angle when teleporting followers, likely to face them towards the leader or align them correctly depending on the coordinate system conventions.
*   **Entry Limit Enforcement**: `ChooseCreatureId` implements a complex algorithm to respect `min_count` and `max_count`. It prioritizes satisfying `min_count` constraints by checking unspawned members. If a minimum constraint cannot be satisfied by remaining spawns, it forces that creature ID. Otherwise, it randomly selects from available IDs that haven't hit their `max_count`.

## Member Reference

**AddMember**: Adds a GUID to the group's member map, storing follow distance, angle, and flags, and merging flags into the group's global options.

**OnMemberAttackStart**: If `OPTION_AGGRO_TOGETHER` is set, triggers `MemberAssist` on all other living members and the original leader to attack the same target.

**OnMemberDied**: Notifies leader/members via AI if configured. If the leader dies in a formation group, elects a new living leader, assigns them the waypoint path, and reinitializes followers.

**CreatureGroup**: Constructor initializes the group with a leader GUID, setting current and original leaders, and resetting internal state.

**GetLeaderGuid**: Returns the GUID of the current leader, which may differ from the original leader during dynamic leadership changes.

**GetOriginalLeaderGuid**: Returns the GUID of the leader defined in the database configuration, which remains constant.

**GetMembers**: Returns a constant reference to the map of all group members.

**ContainsGuid**: Checks if a given GUID exists in the group's member map.

**IsFormation**: Returns true if the `OPTION_FORMATION_MOVE` flag is active for the group.

**HasGroupFlag**: Checks if a specific option flag is set in the group's global options.

**SetLastReachedWaypoint**: Updates the internal record of the last waypoint reached by the group leader.

**OnLeaveCombat**: If `OPTION_EVADE_TOGETHER` is set, forces all members to evade. May trigger `RespawnAll` based on evade-related respawn flags.

**OnRespawn**: Restores the original leader if they respawned while a temporary leader was active. Triggers `RespawnAll` if `OPTION_RESPAWN_TOGETHER` is set.

**RespawnAll**: Iterates through all members and calls `Respawn` on each, excluding the triggerer, and ensuring the original leader is also respawned.

**Respawn**: Resurrects a single creature, handling formation positioning relative to the leader and using a guard to prevent recursive calls.

**MemberAssist**: Forces a member to attack a target via `CreatureAI/AttackedBy`, using a guard to prevent recursion and setting leash extension times.

**RemoveTemporaryLeader**: Resets the leader to the original, removes the temporary leader from the map, and reinitializes patrol movements for remaining members.

**RemoveMember**: Removes a GUID from the internal member map without affecting the creature object's state.

**DisbandGroup**: Marks the group as deleted, erases it from the global manager if DB-loaded, detaches all members from the group, and clears the member map.

**DoForAllMembers**: Executes a provided lambda function on every living creature member found in the world.

**DeleteFromDb**: Deletes all rows for the group's leader from the `creature_groups` table.

**SaveToDb**: Deletes existing group data and inserts new rows for each member into the `creature_groups` table.

**ChooseCreatureId**: Selects a creature ID for a member based on spawn pool limits (`creature_groups_entry_limit`), ensuring min/max counts are respected.

**ComputeRelativePosition**: Calculates X/Y offsets for a member based on leader orientation, follow angle, and distance.

**ConvertDBGuid**: Converts a low GUID integer to a full `ObjectGuid` using creature data from `ObjectMgr`.

**Load**: Loads group definitions and entry limits from `creature_groups` and `creature_groups_entry_limit` tables into memory, creating `CreatureGroup` instances.

**LoadCreatureGroup**: Looks up a `CreatureGroup` by a member GUID or leader GUID in the global manager's map.

---

<!-- machine-true, projected from graph.json -->

## Map — CreatureGroups

*Source:* CreatureGroups.cpp, CreatureGroups.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AddMember | method | ObjectGuid/operator== | ChatHandler.CreatureCommands/HandleNpcGroupAddCommand, ChatHandler.CreatureCommands/HandleNpcGroupAddRelCommand, Creature.Main/JoinCreatureGroup | — |
| OnMemberAttackStart | method | Map.Main/GetCreature, Object/GetObjectGuid, ObjectGuid/operator!=, WorldObject.Object/GetMap | Creature.Main/OnEnterCombat | — |
| OnMemberDied | method | Creature.Main/AI, Creature.Main/GetDefaultMovementType, Creature.MotionMaster/MoveWaypointAsDefault, Creature.MotionMaster/ReInitializePatrolMovement, CreatureAI/GroupMemberJustDied, Map.Main/GetCreature, Object/GetEntry, Object/GetGUIDLow, Object/GetObjectGuid, Object/IsInWorld, ObjectGuid/operator!=, ObjectGuid/operator==, Unit.Main/GetMotionMaster, Unit.Main/IsAlive, WorldObject.Object/GetMap | Unit.Main/Kill | — |
| CreatureGroup | ctor | — | ChatHandler.CreatureCommands/HandleNpcGroupAddCommand, ChatHandler.CreatureCommands/HandleNpcGroupAddRelCommand, Creature.Main/JoinCreatureGroup | — |
| GetLeaderGuid | method | — | Conditions/Evaluate, Creature.Main/IsInEvadeMode, Creature.Main/LeaveCreatureGroup, CreatureAISelector/selectMovementGenerator, Map.ScriptCommands/ScriptCommand_StartScriptOnGroup, WaypointMovementGenerator/InitPatrol, WaypointMovementGenerator/OnArrived | — |
| GetOriginalLeaderGuid | method | — | ChatHandler.CreatureCommands/HandleNpcGroupDelCommand, Conditions/Evaluate, Creature.Main/LeaveCreatureGroup, CreatureAISelector/selectMovementGenerator, instance_ruins_of_ahnqiraj/OnCreatureEnterCombat, ruins_of_ahnqiraj/UpdateAI#7, ruins_of_ahnqiraj/UpdateAI#9 | — |
| GetMembers | method | — | Conditions/Evaluate, Map.Main/LoadCreatureSpawnWithGroup, Map.ScriptCommands/ScriptCommand_StartScriptOnGroup, WaypointMovementGenerator/InitPatrol | — |
| ContainsGuid | method | — | — | — |
| IsFormation | method | — | Creature.Main/AddToWorld, Creature.Main/IsInEvadeMode, Creature.Main/JoinCreatureGroup, CreatureAISelector/selectMovementGenerator, WaypointMovementGenerator/InitPatrol | — |
| HasGroupFlag | method | — | Map.Main/LoadCreatureSpawnWithGroup | — |
| SetLastReachedWaypoint | method | — | WaypointMovementGenerator/OnArrived | — |
| OnLeaveCombat | method | Creature.Main/AI, CreatureAI/EnterEvadeMode, Map.Main/GetCreature, Object/GetObjectGuid, Object/IsInWorld, ObjectGuid/operator!=, ObjectGuid/operator==, Unit.Main/IsAlive, WorldObject.Object/GetMap | Creature.Main/OnLeaveCombat | — |
| OnRespawn | method | Creature.MotionMaster/Initialize, Map.Main/GetCreature, Object/GetObjectGuid, Object/IsInWorld, ObjectGuid/operator!=, ObjectGuid/operator==, Unit.Main/GetMotionMaster, Unit.Main/IsAlive, WorldObject.Object/GetMap | Creature.Main/AddToWorld | — |
| RespawnAll | method | Map.Main/GetCreature, Object/GetObjectGuid, ObjectGuid/operator!=, WorldObject.Object/GetMap | Map.Main/LoadCreatureSpawnWithGroup | — |
| Respawn | method | BattleGroundMap/GetBG, Creature.Main/GetRespawnCoord, Creature.Main/GetRespawnTime, Creature.Main/Respawn, game_Battlegrounds_BattleGround/CanBeSpawned, Map.Main/GetUnit, Map.Main/IsBattleGround, Object/GetTypeId, Object/IsInWorld, Object/ToCreature, Unit.Main/IsAlive, Unit.Main/NearTeleportTo, WorldObject.Object/GetAngle#2, WorldObject.Object/GetMap, WorldObject.Object/GetOrientation, WorldObject.Object/GetPosition#2, WorldObject.Object/UpdateGroundPositionZ | — | — |
| MemberAssist | method | Creature.Main/AI, Creature.Main/GetLastLeashExtensionTimePtr, Creature.Main/SetLastLeashExtensionTimePtr, Creature.Main/SetNoCallAssistance, CreatureAI/AttackedBy, Object/IsInWorld, Unit.Main/GetVictim, Unit.Main/IsAlive | — | — |
| RemoveTemporaryLeader | method | Creature.MotionMaster/ReInitializePatrolMovement, Map.Main/GetCreature, Object/GetObjectGuid, Object/IsInWorld, ObjectGuid/operator!=, Unit.Main/GetMotionMaster, Unit.Main/IsAlive, WorldObject.Object/GetMap | Creature.Main/LeaveCreatureGroup | — |
| RemoveMember | method | — | boss_vectus/JustDied, boss_vectus/UpdateAI, Creature.Main/LeaveCreatureGroup | — |
| DisbandGroup | method | Creature.Main/HasStaticDBSpawnData, Creature.Main/SetCreatureGroup, Creature.MotionMaster/Initialize, CreatureGroupsManager/EraseCreatureGroup, CreatureGroupsManager/instance, Errors/PrintStacktraceAndThrow, Map.Main/GetCreature, Object/GetObjectGuid, ObjectGuid/operator==, Unit.Main/GetMotionMaster, Unit.Main/IsAlive, WorldObject.Object/GetMap | Creature.Main/LeaveCreatureGroup | — |
| DoForAllMembers | method | Map.Main/GetCreature | eastern_plaguelands/EnableCombat, eastern_plaguelands/EnableCombat#2, eastern_plaguelands/JustDied#2, eastern_plaguelands/JustReachedHome | — |
| DeleteFromDb | method | Database/PExecute#2, ObjectGuid/GetCounter | ChatHandler.CreatureCommands/HandleNpcGroupDelCommand | creature_groups |
| SaveToDb | method | Database/PExecute#2, ObjectGuid/GetCounter | ChatHandler.CreatureCommands/HandleNpcGroupAddCommand, ChatHandler.CreatureCommands/HandleNpcGroupAddRelCommand, ChatHandler.CreatureCommands/HandleNpcGroupDelCommand | creature_groups |
| ChooseCreatureId | method | CreatureData/ChooseCreatureId, CreatureData/HasCreatureId, Log.Main/Out, Map.Main/GetCreature, Object/GetEntry, ObjectGuid/GetCounter, ObjectGuid/GetString, ObjectGuid/operator!=, ObjectGuid/operator==, ObjectMgr/GetCreatureData, shared_Util/urand | Creature.Main/LoadFromDB, Creature.Main/Update | — |
| ComputeRelativePosition | method | — | WaypointMovementGenerator/GetResetPosition#2, WaypointMovementGenerator/StartMove | — |
| ConvertDBGuid | method | CreatureData/GetObjectGuid, ObjectGuid/ObjectGuid, ObjectMgr/GetCreatureData | ChatHandler.CreatureCommands/HandleNpcGroupAddCommand, ChatHandler.CreatureCommands/HandleNpcGroupAddRelCommand, ChatHandler.CreatureCommands/HandleNpcGroupLinkCommand | — |
| Load | method | CreatureGroupsManager/RegisterNewGroup, Database/Query, Field/GetFloat, Field/GetInt32, Field/GetUInt32, Log.Main/Out, ObjectGuid/IsEmpty, ObjectGuid/operator!=, ObjectGuid/operator==, ObjectMgr/GetCreatureTemplate, ObjectMgr/IsExistingCreatureGuid, ObjectMgr/IsExistingCreatureId, ProgressBar/BarGoLink, ProgressBar/BarGoLink#3, ProgressBar/step, QueryResult/Fetch, QueryResult/GetRowCount, QueryResult/NextRow, shared_Util/getMSTime | ChatHandler.ServerCommands/HandleReloadCreatureGroupsCommand, World/SetInitialWorldSettings | creature_groups, creature_groups_entry_limit |
| LoadCreatureGroup | method | ObjectGuid/operator== | Creature.Main/AddToWorld, Creature.Main/LoadFromDB | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `creature_groups`: leader_guid int(11) unsigned, member_guid int(11) unsigned PK, dist float unsigned, angle float unsigned, flags int(11) unsigned
- `creature_groups_entry_limit`: leader_guid int(11) unsigned PK, creature_id int(11) unsigned PK, min_count int(11) unsigned, max_count int(11) unsigned

*`?` = nullable, `PK` = primary key column.*

