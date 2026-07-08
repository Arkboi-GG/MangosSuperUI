<!-- provenance: no-member-reference-section -->
# game_Group_Group

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Group

The `Group` class (defined in `Group.h` and implemented in `Group.cpp`) represents a player party or raid group within the server. It manages the lifecycle of groups, including creation, disbanding, membership changes, leadership transfers, and role assignments (assistants, main tank, main assistant). It also handles the distribution of loot via various methods (Free-for-All, Round Robin, Master Loot, Group Loot, Need Before Greed) and the associated rolling mechanics. Additionally, it manages group bindings to dungeon instances, ensuring that group members share instance states, and facilitates communication between group members through packet broadcasting and status updates. The class supports both normal groups (up to 5 members) and raid groups (up to 40 members, subdivided into subgroups).

## Member-by-Member Behavior

### Group Lifecycle and Membership Management

*   **`Group` (ctor)**: Initializes the group object with default values, such as setting the group type to normal, loot method to Free-for-All, and clearing member lists. It is called by various systems to create new group objects before they are fully initialized via `Create`.
*   **`~Group` (dtor)**: Cleans up resources associated with the group. It removes the group from any associated battleground, clears pending invites, deletes any active loot rolls, and removes the group from any bound dungeon persistent states. It also frees memory allocated for subgroup counters.
*   **`Create`**: Fully initializes a new group. It sets the leader, determines if it's a raid or normal group, initializes raid subgroup counters if necessary, sets default loot settings, generates a unique group ID, and persists the group data to the `groups` and `group_member` database tables. It then adds the leader as the first member.
*   **`LoadGroupFromDB`**: Loads group metadata from the `groups` table using a `Field` array. It reconstructs the group's ID, leader information, raid status, main tank/assistant GUIDs, loot settings, and target icons. It validates the leader's existence by name lookup.
*   **`LoadMemberFromDB`**: Adds a member to the group during server startup or reload, using data from the `group_member` table. It checks for duplicates, creates a `MemberSlot`, updates subgroup counters, and determines the group's faction composition.
*   **`AddInvite`**: Places a player on the group's invite list. It prevents inviting players who are already invited, already in a group (unless it's a battleground group), or in an original group. It clears any existing invite for the player before adding them to this group's list.
*   **`AddLeaderInvite`**: Similar to `AddInvite`, but also designates the invited player as the future leader upon acceptance.
*   **`RemoveInvite`**: Removes a player from the invite list and clears their invite reference. Returns the current member count.
*   **`RemoveAllInvites`**: Clears the entire invite list, updating each player's invite reference to null.
*   **`GetInvited` (overloads)**: Retrieves a pointer to an invited player by either their `ObjectGuid` or name. The overload `GetInvited#2` specifically takes a string name.
*   **`AddMember`**: Adds a player to the group. It first calls `_addMember` to handle internal state and database persistence. Then, it performs extensive synchronization: updating the group's faction status, resetting instances for the new member if appropriate, setting update flags, handling aura masks for the player and their pet, scheduling quest-related world object updates for raid members, validating instance binds, and broadcasting field updates to all group members. It also notifies the LFG manager if the group is in an LFG queue.
*   **`RemoveMember`**: Removes a player from the group. If the group will still have enough members, it calls `_removeMember`. It handles quest-related updates for raid members, sends appropriate packets (kick or leave), manages LFG queue status (removing the group or re-queuing the kicked player), and updates the group leader if the removed player was the leader. If the group falls below the minimum member count, it disbans the group.
*   **`ChangeLeader`**: Changes the group leader to a specified member. It updates internal state via `_setLeader`, broadcasts the new leader's name to the group, and sends a full group update.
*   **`Disband`**: Dissolves the group. It iterates through all members, removing them from the group (handling battleground and original group references), sending destruction packets, managing LFG queue removal, and initiating homebind timers for players in instances. It processes any pending loot rolls, clears member slots and invites, and deletes the group and its members from the database. It also transfers instance saves to a remaining player if applicable and resets instances.

### Leadership and Role Management

*   **`_setLeader`**: Internally updates the group leader. It handles database updates for `group_instance` and `groups` tables, transferring instance binds from the old leader to the new one and updating the leader GUID in the database. It also updates internal state and flags.
*   **`_chooseLeader`**: Selects a new leader, prioritizing online players, then assistants (in raids), then any available member. If called due to offline leader timeout and no suitable candidate is found, it may not change the leader. It triggers `ChangeLeader` or `_setLeader` depending on the context.
*   **`_updateLeaderFlag`**: Updates the leader flag on the current leader's `Player` object, notifying them of their status change.
*   **`_setMainTank`**: Sets the main tank for the group. It ensures the main assistant is cleared if the same player is set as main tank. It updates the `groups` table.
*   **`_setMainAssistant`**: Sets the main assistant for the group. It ensures the main tank is cleared if the same player is set as main assistant. It updates the `groups` table.
*   **`_setAssistantFlag`**: Sets the assistant flag for a specific member. It updates the `group_member` table.

### Subgroup Management (Raids)

*   **`_initRaidSubGroupsCounter`**: Initializes the array tracking the number of members in each raid subgroup.
*   **`SubGroupCounterIncrease` / `SubGroupCounterDecrease`**: Helper methods to update the subgroup member counts.
*   **`_setMembersGroup`**: Changes a member's subgroup. It updates the internal slot, subgroup counter, and the `group_member` table.
*   **`_swapMembersGroup`**: Swaps the subgroups of two members. It updates the internal slots and the `group_member` table without changing subgroup counters.
*   **`ChangeMembersGroup` (overloads)**: Public interface for changing a member's subgroup. The overload `ChangeMembersGroup#2` handles online `Player` pointers directly, updating their group references and subgroup counters. The other overload handles offline members by GUID. Both check for subgroup capacity and update references accordingly.
*   **`SwapMembersGroup` (overloads)**: Public interface for swapping two members' subgroups. The overload `SwapMembersGroup#2` handles online `Player` pointers, updating their group references. The other overload handles offline members by GUID. Both update references and send updates.
*   **`SameSubGroup`**: Checks if two players are in the same subgroup within this group.

### Loot System

*   **`Roll` (ctor/dtor)**: Constructs a loot roll object, linking it to the loot item. The destructor `~Roll` is empty, relying on the parent class or manual deletion.
*   **`targetObjectBuildLink`**: Called when the roll is linked to the loot object, adding a validator reference.
*   **`setLoot` / `getLoot`**: Accessors for the loot object associated with a roll.
*   **`StartLootRoll`**: Initiates a loot roll for an item. It identifies eligible players based on proximity, loot permissions, and item usability. If only one eligible player exists, it immediately awards the item via `CountSingleLooterRoll`. Otherwise, it starts a timer, notifies the creature, and sends start roll packets to eligible players.
*   **`CountRollVote` (overloads)**: Records a player's vote (Need, Greed, Pass) for a roll. It updates roll statistics and sends individual roll packets. If all votes are collected, it triggers `CountTheRoll`.
*   **`CountTheRoll`**: Processes the final outcome of a roll. It determines the winner based on Need or Greed votes (using random numbers for ties), awards the item to the winner, handles inventory errors, and sends appropriate packets. If no one wins, it sends an "all passed" packet. It then cleans up the roll object.
*   **`CountSingleLooterRoll`**: Awards an item to the sole eligible looter without a formal roll process.
*   **`EndRoll`**: Called when a roll timer expires. It processes any incomplete rolls by treating non-voters as passes.
*   **`SendLootStartRoll` / `SendLootRoll` / `SendLootRollWon` / `SendLootAllPassed`**: Static methods that construct and send the respective loot roll packets to all participating players in a roll.
*   **`SendLootStartRollsForPlayer`**: Sends any pending loot roll start packets to a specific player, typically used when a player logs in or joins a group mid-roll.
*   **`GroupLoot` / `NeedBeforeGreed` / `MasterLoot`**: Methods that process loot according to the specific loot method. They iterate through loot items, check thresholds, and initiate rolls or distribute items accordingly. `MasterLoot` also sends a list of eligible looters to the master looter.
*   **`UpdateLooterGuid`**: Manages the round-robin looter for low-quality items. It cycles through group members to find the next eligible looter within range.
*   **`SendLooter`**: Placeholder/static method intended to notify the group of the designated looter for a creature (currently commented out).
*   **`_removeRolls`**: Internal helper that removes a specific player's votes from all active rolls in the `RollId` list, adjusting vote counts and player totals accordingly.

### Instance Binding and Reset

*   **`BindToInstance`**: Binds the group to a specific dungeon instance state. It updates the `group_instance` table and links the group to the `DungeonPersistentState`.
*   **`UnbindInstance`**: Unbinds the group from a dungeon instance, removing the entry from the `group_instance` table and unlinking from the state.
*   **`GetBoundInstance`**: Retrieves the binding information for a specific map ID.
*   **`ResetInstances`**: Resets all instances the group is bound to. It checks reset eligibility, notifies players, resets the map if loaded, deletes instance data from the database if appropriate, and unbinds the group.
*   **`_homebindIfInstance`**: Helper to start the homebind timer for a player leaving an instance if they are not permanently bound.

### Communication and Status Updates

*   **`SendUpdate`**: Sends a full group list update packet to all online members. It includes group type, member details, leader info, loot settings, and raid target icons (for non-raids).
*   **`BroadcastPacket`**: Sends a packet to all online group members, with options to ignore players in battleground raids, target specific subgroups, or ignore a specific player.
*   **`BroadcastReadyCheck`**: Sends a ready check packet to the leader and assistants.
*   **`OfflineReadyCheck`**: Placeholder for handling ready checks for offline members (currently empty).
*   **`UpdatePlayerOutOfRange`**: Sends updated stats for a player to group members who cannot see them directly (e.g., across different maps).
*   **`UpdatePlayerOnlineStatus`**: Updates the group when a player logs in or out, triggering full updates and setting update flags.
*   **`UpdateOfflineLeader`**: Periodically checks if the group leader has been offline too long and initiates a leader change if necessary.
*   **`BroadcastGroupUpdate`**: Forces updates for specific unit fields (flags, faction, health) for all group members and their controlled units. It uses a helper functor `BroadcastGroupUpdateHelper` whose `operator()` method forces value updates on the unit and its controlled entities.

### Target Icons

*   **`SetTargetIcon`**: Assigns a raid target icon to a specific GUID. It clears the icon from any other target if it was previously assigned elsewhere. It sends a delta update packet.
*   **`ClearTargetIcon`**: Removes a raid target icon from a specific GUID.
*   **`SendTargetIconList`**: Sends the complete list of current raid target icons to a specific session.

### Experience and Rewards

*   **`GetDataForXPAtKill`**: Calculates data needed for XP distribution among group members after a kill, including member count, sum of levels, and identifying members with maximum levels (considering gray level penalties).
*   **`RewardGroupAtKill`**: Distributes XP, honor, reputation, and quest progress to group members after a unit kill. It uses helper functions to calculate individual rewards based on levels, proximity, and group composition.

### LFG (Looking For Group) Integration

*   **`CalculateLFGRoles`**: Determines the available roles (Tank, Healer, DPS) for the group in the LFG system, considering member classes and talents.
*   **`FillPremadeLFG`**: Helper for `CalculateLFGRoles` to assign roles to members based on priority and availability.

### Battleground Queue Validation

*   **`CanJoinBattleGroundQueue`**: Validates whether the group can join a specific battleground queue, checking member counts, online status, faction consistency, level brackets, existing queue status, deserter debuffs, and queue limits.
*   **`InCombatToInstance`**: Checks if any group member is in combat within a specific instance.

## Cross-Unit Boundaries

*   **`GetGroupMemberStatus`**: Called by `WorldSession.GroupHandler/BuildPartyMemberStatsPacket` to determine the status flags (online, dead, AFK, etc.) for a group member.
*   **`Group` (ctor)**: Called by `AiBotAI.Bridge/BridgeHandleFormGroup`, `game_Battlegrounds_BattleGround/AddOrSetPlayerToCorrectBgGroup`, `LFGQueue/Update`, `ObjectMgr/LoadGroups`, `PartyBotAI/AddToPlayerGroup`, and `WorldSession.GroupHandler/HandleGroupInviteOpcode` to instantiate new group objects.
*   **`~Group` (dtor)**: Interacts with `BattleGround/GetBgRaid`, `DungeonPersistentState/RemoveGroup`, `game_Battlegrounds_BattleGround/SetBgRaid`, and `Log.Main/Out` during cleanup.
*   **`Create`**: Called by `AiBotAI.Bridge/BridgeHandleFormGroup`, `game_Battlegrounds_BattleGround/AddOrSetPlayerToCorrectBgGroup`, `LFGQueue/Update`, `PartyBotAI/AddToPlayerGroup`, and `WorldSession.GroupHandler/HandleGroupAcceptOpcode` to finalize group creation. It calls into `Database` for persistence, `ObjectMgr` for ID generation and player lookup, and `Player.Main` for instance conversion.
*   **`LoadGroupFromDB` / `LoadMemberFromDB`**: Called by `ObjectMgr/LoadGroups` to reconstruct groups from the database on server startup. They use `Field` accessors and `ObjectMgr` for name/data lookups.
*   **`ConvertToRaid`**: Called by `WorldSession.GroupHandler/HandleGroupRaidConvertOpcode`. It updates the database and schedules quest object updates for members via `Player.Main`.
*   **`AddInvite` / `AddLeaderInvite` / `RemoveInvite` / `RemoveAllInvites` / `GetInvited`**: Manage the invite list, interacting with `Player.Main` methods for invite state and `Errors` for assertions. Called by `WorldSession.GroupHandler` opcodes and `Player.Main/UninviteFromGroup`.
*   **`AddMember`**: Called by `AiBotAI.Bridge/BridgeHandleFormGroup`, `game_Battlegrounds_BattleGround/AddOrSetPlayerToCorrectBgGroup`, `LFGQueue/FindRoleToGroup`, `LFGQueue/Update`, `PartyBotAI/AddToPlayerGroup`, and `WorldSession.GroupHandler/HandleGroupAcceptOpcode`. It extensively interacts with `Player.Main`, `Map.Main`, `LFGMgr`, `ObjectMgr`, and various update/packet building utilities.
*   **`RemoveMember`**: Called by `AiBotAI.Bridge/BridgeHandleFormGroup`, `game_Battlegrounds_BattleGround/RemovePlayerAtLeave`, and `Player.Main/RemoveFromGroup#2`. It interacts with `LFGMgr`, `LFGQueue`, `ObjectMgr`, `Player.Main`, and `World` for LFG and packet handling.
*   **`ChangeLeader`**: Called by `game_Battlegrounds_BattleGround/AddOrSetPlayerToCorrectBgGroup` and `WorldSession.GroupHandler/HandleGroupSetLeaderOpcode`.
*   **`Disband`**: Called by `AiBotAI.Bridge/BridgeHandleDisbandGroup`, `AiBotAI.Bridge/BridgeHandleFormGroup`, `ChatHandler.MiscCommands/HandleInstanceGroupUnbindCommand`, `ObjectMgr/LoadGroups`, and `Player.Main/UninviteFromGroup`. It interacts with `Database`, `DungeonPersistentState`, `LFGQueue`, `MapPersistentStateMgr`, `ObjectMgr`, `Player.Main`, and `World` for cleanup, persistence, and notifications.
*   **`CalculateLFGRoles`**: Called by `LFGMgr/AddToQueue` and `LFGMgr/UpdateGroup`. It uses `LFGMgr` for role calculation and `ObjectMgr` for player/class data.
*   **`SendLootStartRoll` / `SendLootRoll` / `SendLootRollWon` / `SendLootAllPassed`**: Static methods called internally by the loot system to send packets via `Player.Main/GetSession` and `WorldSession.Main/SendPacket`.
*   **`GroupLoot` / `NeedBeforeGreed` / `MasterLoot`**: Called by `AiBotAI.Loot/DoAutoLoot` and `Player.Main/SendLoot`. They use `ObjectMgr` for item prototypes and `LootMgr` for looter validation.
*   **`CountRollVote#2`**: Called by `WorldSession.GroupHandler/HandleLootRoll`.
*   **`StartLootRoll`**: Called by `Creature.Main/StartGroupLoot`. It uses `LootMgr` and `ObjectMgr` for validation.
*   **`SendLootStartRollsForPlayer`**: Called by `WorldSession.CharacterHandler/HandlePlayerLogin`.
*   **`EndRoll`**: Called by `Creature.Main/StopGroupLoot`.
*   **`CountSingleLooterRoll` / `CountTheRoll`**: Internal loot processing methods using `LootMgr` and `Player.Main` for item storage and notifications.
*   **`SetTargetIcon` / `ClearTargetIcon` / `SendTargetIconList`**: Manage raid icons, interacting with `WorldSession` for packet sending and `CreatureAI/ClearTargetIcon`.
*   **`GetDataForXPAtKill` / `RewardGroupAtKill`**: Handle XP distribution, using `Formulas`, `Unit.Main`, `Player.Main`, and `MapEntry` for calculations and rewards.
*   **`SendUpdate`**: Called by `Unit.Main/Kill` and `WorldSession.GroupHandler/HandleLootMethodOpcode`. Sends packets to all members.
*   **`UpdatePlayerOutOfRange`**: Called by `Player.Main/SendUpdateToOutOfRangeGroupMembers`. Uses `WorldSession.GroupHandler/BuildPartyMemberStatsChangedPacket`.
*   **`UpdatePlayerOnlineStatus`**: Called by `WorldSession.CharacterHandler/HandlePlayerLogin` and `WorldSession.Main/LogoutPlayer`.
*   **`UpdateOfflineLeader`**: Called by `World/Update`. Uses `ObjectMgr` and `WorldSession.Main/PlayerLoading`.
*   **`BroadcastPacket`**: Called by numerous systems (`LFGQueue`, `Player.Main`, `Unit.Main`, `WorldSession.ChatHandler`, `WorldSession.GroupHandler`) to send messages to the group.
*   **`BroadcastReadyCheck` / `OfflineReadyCheck`**: Related to ready checks, called by `WorldSession.GroupHandler/HandleRaidReadyCheckOpcode`.
*   **`_addMember#2` / `_removeMember`**: Internal helpers for membership changes, interacting with `Database`, `MapPersistentStateMgr`, `ObjectMgr`, and `Player.Main`.
*   **`_chooseLeader` / `_setLeader` / `_updateLeaderFlag`**: Internal leadership management, interacting with `Database`, `DungeonPersistentState`, `MapPersistentStateMgr`, `ObjectMgr`, and `Player.Main`.
*   **`_swapMembersGroup` / `_setMembersGroup` / `_setAssistantFlag` / `_setMainTank` / `_setMainAssistant`**: Internal subgroup and role management, interacting with `Database`.
*   **`SameSubGroup`**: Called by `PetAI/UpdateAllies`, `Player.Main/IsInSameGroupWith`, `Totem/UnSummon`, and `Unit.SpellAuras/Update`.
*   **`ChangeMembersGroup` / `SwapMembersGroup`**: Called by `WorldSession.GroupHandler` opcodes.
*   **`CanJoinBattleGroundQueue`**: Called by `WorldSession.BattleGroundHandler/RequestBgJoinQueue`. Uses `Player.Main` and `World` config.
*   **`InCombatToInstance`**: Called by `Map.Main/CanEnter#2`.
*   **`ResetInstances`**: Called by `WorldSession.MiscHandler/HandleResetInstancesOpcode`. Interacts with `DungeonPersistentState`, `Map.Main`, `MapManager`, `MapPersistentStateMgr`, `ObjectMgr`, and `Player.Main`.
*   **`GetBoundInstance` / `BindToInstance` / `UnbindInstance`**: Manage instance bindings, interacting with `ChatHandler.TeleportCommands`, `Map.Main`, `ObjectMgr`, `Player.Main`, and `MapPersistentStateMgr`.
*   **`BroadcastGroupUpdate`**: Called by `Unit.SpellAuras/HandleModCharm` and `WorldSession.GroupHandler` opcodes.
*   **`SendLooter`**: Called by `Unit.Main/Kill` and `WorldSession.LootHandler/DoLootRelease`.
*   **`UpdateLooterGuid`**: Called by `Player.Main/SendLoot` and `Unit.Main/Kill`. Uses `ObjectAccessor` and `WorldObject.Object`.

## Data Model

The `Group` class interacts with the following database tables:

*   **`groups`**: Stores core group metadata.
    *   `group_id` (PK): Unique identifier for the group.
    *   `leader_guid`: GUID of the group leader.
    *   `main_tank_guid`: GUID of the main tank (raids).
    *   `main_assistant_guid`: GUID of the main assistant (raids).
    *   `loot_method`: Enum value for the loot distribution method.
    *   `loot_threshold`: Item quality threshold for rolling.
    *   `looter_guid`: GUID of the designated looter (Round Robin/Master Loot).
    *   `icon1` - `icon8`: GUIDs of targets assigned to raid icons.
    *   `is_raid`: Boolean indicating if the group is a raid.
*   **`group_member`**: Stores individual member details within a group.
    *   `group_id` (PK): Foreign key to `groups.group_id`.
    *   `member_guid` (PK): GUID of the player.
    *   `assistant`: Boolean indicating if the member is an assistant (raids).
    *   `subgroup`: Subgroup index for the member (raids).
*   **`group_instance`**: Tracks group bindings to dungeon instances.
    *   `leader_guid` (PK): GUID of the group leader at the time of binding.
    *   `instance` (PK): Instance ID.
    *   `permanent`: Boolean indicating if the bind is permanent.
*   **`character_instance`**: Referenced indirectly during leader changes to manage personal instance binds.
    *   `guid` (PK): Player GUID.
    *   `instance` (PK): Instance ID.
    *   `permanent`: Boolean indicating if the bind is permanent.

## Notable Implementation Details

*   **Battleground Groups**: The `isBGGroup()` check is pervasive. Battleground groups often bypass standard database persistence and some instance binding rules, as they are managed separately by the `BattleGround` system. The `m_bgGroup` pointer links the `Group` to its `BattleGround` instance.
*   **Instance Binding Complexity**: Instance binding involves coordinating between `Group`, `Player`, `DungeonPersistentState`, and multiple database tables (`group_instance`, `character_instance`). Leader changes trigger complex logic to transfer instance binds from the old leader to the new one, potentially converting personal binds to group binds or vice-versa. The `_setLeader` method contains significant logic for this transfer.
*   **Loot Roll Mechanics**: Loot rolls are managed via `Roll` objects stored in the `RollId` vector. The system handles timeouts, player votes, and winner determination. Single-looter cases are optimized by skipping the roll process entirely. The `CountTheRoll` method uses `urand(1, 100)` for tie-breaking in Need/Greed rolls.
*   **Subgroup Management**: Raid subgroups are tracked via an array `m_subGroupsCounts`. Adding/removing members or changing subgroups requires careful management of these counters to prevent overflow. The `_addMember` overload automatically assigns a member to the first non-full subgroup.
*   **Offline Leader Timeout**: The `UpdateOfflineLeader` method, called periodically by `World/Update`, checks if the leader has been offline beyond a configured delay. If so, it triggers `_chooseLeader` to appoint a new leader, prioritizing online assistants.
*   **Cross-Faction Groups**: The `m_groupTeam` variable tracks the group's faction composition. It starts as `TEAM_NONE` and updates as members join, becoming `TEAM_CROSSFACTION` if members from both factions are present. This affects certain functionalities like battleground queue joining.
*   **LFG Integration**: The group maintains an `m_LFGAreaId` to track its LFG queue status. Various methods (`AddMember`, `RemoveMember`, `Disband`) interact with `LFGMgr` and `LFGQueue` to update the group's status in the LFG system.
*   **Packet Broadcasting**: `BroadcastPacket` is a central utility for sending messages to all group members. It handles filtering based on subgroup, battleground raid status, and specific player ignores.
*   **Memory Management**: The destructor carefully cleans up dynamically allocated resources, including the `m_subGroupsCounts` array and `Roll` objects. It also ensures proper unlinking from `DungeonPersistentState` objects.
*   **Database Transactions**: Most database modifications are wrapped in `CharacterDatabase.BeginTransaction` and `CommitTransaction` blocks to ensure atomicity.
*   **Helper Functions**: Several static helper functions (`GetDataForXPAtKill_helper`, `RewardGroupAtKill_helper`, `Broadcast

---

<!-- machine-true, projected from graph.json -->

## Map — game_Group_Group

*Source:* Group.cpp, Group.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GetGroupMemberStatus | function | Object/HasFlag, Player.Main/GetSession, Player.Main/IsAFK, Player.Main/IsDND, Player.Main/IsFFAPvP, Unit.Main/IsDead, Unit.Main/IsPvP, WorldSession.Main/PlayerLogout | WorldSession.GroupHandler/BuildPartyMemberStatsPacket | — |
| targetObjectBuildLink | method | Loot/addLootValidatorRef | — | — |
| Group | ctor | — | AiBotAI.Bridge/BridgeHandleFormGroup, game_Battlegrounds_BattleGround/AddOrSetPlayerToCorrectBgGroup, LFGQueue/Update, ObjectMgr/LoadGroups, PartyBotAI/AddToPlayerGroup, WorldSession.GroupHandler/HandleGroupInviteOpcode | — |
| ~Group | dtor | BattleGround/GetBgRaid, DungeonPersistentState/RemoveGroup, game_Battlegrounds_BattleGround/SetBgRaid, Log.Main/Out | — | — |
| Create | method | Database/BeginTransaction, Database/CommitTransaction, Database/PExecute#2, Group/isBGGroup, Group/isRaidGroup, Group/_initRaidSubGroupsCounter, ObjectGuid/GetCounter, ObjectGuid/GetRawValue, ObjectMgr/GenerateGroupId, ObjectMgr/GetPlayer, Player.Main/ConvertInstancesToGroup | AiBotAI.Bridge/BridgeHandleFormGroup, game_Battlegrounds_BattleGround/AddOrSetPlayerToCorrectBgGroup, LFGQueue/Update, PartyBotAI/AddToPlayerGroup, WorldSession.GroupHandler/HandleGroupAcceptOpcode | groups, group_member |
| Roll | ctor | — | — | — |
| ~Roll | dtor | — | — | — |
| setLoot | method | — | — | — |
| getLoot | method | — | — | — |
| LoadGroupFromDB | method | Field/GetBool, Field/GetUInt16, Field/GetUInt32, Field/GetUInt64, Field/GetUInt8, Group/_initRaidSubGroupsCounter, ObjectGuid/ObjectGuid#2, ObjectGuid/ObjectGuid#5, ObjectMgr/GetPlayerNameByGUID | ObjectMgr/LoadGroups | — |
| LoadMemberFromDB | method | Group/SubGroupCounterIncrease, Log.Main/Out, ObjectGuid/ObjectGuid#2, ObjectGuid/operator==, ObjectMgr/GetPlayerDataByGUID, Player.Main/TeamForRace | ObjectMgr/LoadGroups | — |
| ConvertToRaid | method | Database/PExecute#2, Group/isBGGroup, Group/_initRaidSubGroupsCounter, ObjectMgr/GetPlayer, Player.Main/UpdateForQuestWorldObjects | WorldSession.GroupHandler/HandleGroupRaidConvertOpcode | groups |
| AddInvite | method | Group/isBGGroup, Player.Main/GetGroup, Player.Main/GetGroupInvite, Player.Main/GetOriginalGroup, Player.Main/SetGroupInvite | WorldSession.GroupHandler/HandleGroupInviteOpcode | — |
| AddLeaderInvite | method | Object/GetObjectGuid, Player.Main/GetName | WorldSession.GroupHandler/HandleGroupInviteOpcode | — |
| RemoveInvite | method | Errors/PrintStacktraceAndThrow, Group/GetMembersCount, Player.Main/GetGroupInvite, Player.Main/SetGroupInvite | Player.Main/UninviteFromGroup, WorldSession.GroupHandler/HandleGroupAcceptOpcode | — |
| RemoveAllInvites | method | Errors/PrintStacktraceAndThrow, Player.Main/GetGroupInvite, Player.Main/SetGroupInvite | Player.Main/UninviteFromGroup | — |
| GetInvited | method | Object/GetObjectGuid, ObjectGuid/operator== | WorldSession.GroupHandler/HandleGroupUninviteGuidOpcode | — |
| GetInvited#2 | method | Player.Main/GetName | WorldSession.GroupHandler/HandleGroupUninviteOpcode | — |
| AddMember | method | DungeonMap/IsUnloadingBeforeReset, Group/GetFirstMember, Group/isBGGroup, Group/IsInLFG, Group/IsLeader, Group/isRaidGroup, GroupReference/next, LFGMgr/UpdateGroup, Map.Main/GetId, Map.Main/IsDungeon, Object/GetObjectGuid, Object/GetValuesCount, ObjectMgr/GetPlayer, Pet.Main/SetAuraUpdateMask, Player.Main/GetBoundInstance, Player.Main/GetTeam, Player.Main/IsInVisibleList, Player.Main/ResetInstances, Player.Main/SendDirectMessage, Player.Main/SetAuraUpdateMask, Player.Main/SetGroupUpdateFlag, Player.Main/UpdateForQuestWorldObjects, Unit.Main/GetAuraApplicationMask, Unit.Main/GetPet, UpdateData/BuildPacket#3, UpdateData/HasData, UpdateData/UpdateData, UpdateMask/HasData, UpdateMask/SetCount, UpdateMask/UpdateMask, WorldObject.Object/BuildValuesUpdateBlockForPlayer, WorldObject.Object/GetMap, WorldObject.Object/MarkUpdateFieldsWithFlagForUpdate, WorldPacket/WorldPacket | AiBotAI.Bridge/BridgeHandleFormGroup, game_Battlegrounds_BattleGround/AddOrSetPlayerToCorrectBgGroup, LFGQueue/FindRoleToGroup, LFGQueue/Update, PartyBotAI/AddToPlayerGroup, WorldSession.GroupHandler/HandleGroupAcceptOpcode | — |
| RemoveMember | method | ByteBuffer/operator<<, ByteBuffer/operator<<#10, ByteBuffer/operator<<#11, ByteBuffer/operator<<#4, ByteBuffer/operator<<#7, Group/GetMembersCount, Group/GetMembersMinCount, Group/IsInLFG, Group/isRaidGroup, LFGMgr/AddToQueue, LFGMgr/UpdateGroup, LFGQueue/GetMessager, LFGQueue/RemoveGroupFromQueue, ObjectMgr/GetPlayer, Player.Main/GetGroup, Player.Main/GetSession, Player.Main/UpdateForQuestWorldObjects, World/GetLFGQueue, WorldPacket/Initialize, WorldPacket/WorldPacket, WorldPacket/WorldPacket#4, WorldSession.LFGHandler/SendMeetingstoneSetqueue, WorldSession.Main/SendPacket | AiBotAI.Bridge/BridgeHandleFormGroup, game_Battlegrounds_BattleGround/RemovePlayerAtLeave, Player.Main/RemoveFromGroup#2 | — |
| ChangeLeader | method | ByteBuffer/operator<<, Group/_getMemberCSlot, WorldPacket/WorldPacket#4 | game_Battlegrounds_BattleGround/AddOrSetPlayerToCorrectBgGroup, WorldSession.GroupHandler/HandleGroupSetLeaderOpcode | — |
| Disband | method | ByteBuffer/operator<<#11, Database/BeginTransaction, Database/CommitTransaction, Database/PExecute#2, DungeonPersistentState/RemoveGroup, Group/GetLeaderGuid, Group/isBGGroup, Group/IsInLFG, Group/isRaidGroup, LFGQueue/GetMessager, LFGQueue/RemoveGroupFromQueue, MapPersistentStateMgr/GetInstanceId, Object/GetObjectGuid, ObjectGuid/Clear, ObjectGuid/GetCounter, ObjectGuid/IsEmpty, ObjectGuid/ObjectGuid, ObjectGuid/operator==, ObjectMgr/GetPlayer, Player.Main/BindToInstance, Player.Main/GetBoundInstance, Player.Main/GetGroup, Player.Main/GetOriginalGroup, Player.Main/GetSession, Player.Main/RemoveFromBattleGroundRaid, Player.Main/SetGroup, Player.Main/SetOriginalGroup, Player.Main/UpdateForQuestWorldObjects, World/GetLFGQueue, WorldObject.Object/GetMapId, WorldPacket/Initialize, WorldPacket/WorldPacket, WorldSession.LFGHandler/SendMeetingstoneSetqueue, WorldSession.Main/SendPacket | AiBotAI.Bridge/BridgeHandleDisbandGroup, AiBotAI.Bridge/BridgeHandleFormGroup, ChatHandler.MiscCommands/HandleInstanceGroupUnbindCommand, ObjectMgr/LoadGroups, Player.Main/UninviteFromGroup | groups, group_instance, group_member |
| CalculateLFGRoles | method | Group/GetMembersCount, Group/GetMemberSlots, LFGMgr/CalculateRoles, LFGMgr/CalculateTalentRoles, ObjectMgr/GetPlayer, ObjectMgr/GetPlayerClassByGUID, World/getConfig | LFGMgr/AddToQueue, LFGMgr/UpdateGroup | — |
| FillPremadeLFG | method | Group/GetMemberSlots, LFGMgr/GetMaximumDPSSlots, LFGMgr/GetPriority, ObjectGuid/operator==, ObjectMgr/GetPlayerClassByGUID | — | — |
| SendLootStartRoll | method | ByteBuffer/operator<<#10, ObjectGuid/operator<<, ObjectMgr/GetPlayer, Player.Main/GetSession, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| SendLootRoll | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#7, ObjectGuid/operator<<, ObjectMgr/GetPlayer, Player.Main/GetSession, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| SendLootRollWon | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#7, ObjectGuid/operator<<, ObjectMgr/GetPlayer, Player.Main/GetSession, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| SendLootAllPassed | method | ByteBuffer/operator<<#10, ObjectGuid/operator<<, ObjectMgr/GetPlayer, Player.Main/GetSession, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| GroupLoot | method | Log.Main/Out, ObjectMgr/GetItemPrototype | AiBotAI.Loot/DoAutoLoot, Player.Main/SendLoot | — |
| NeedBeforeGreed | method | Log.Main/Out, ObjectMgr/GetItemPrototype | AiBotAI.Loot/DoAutoLoot, Player.Main/SendLoot | — |
| MasterLoot | method | ByteBuffer/operator<<#7, Group/GetFirstMember, GroupReference/next, LootMgr/IsAllowedLooter, Object/GetObjectGuid, Object/IsInWorld, ObjectGuid/operator<<, ObjectMgr/GetItemPrototype, Player.Main/GetSession, WorldObject.Object/IsWithinLootXPDist, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | Player.Main/SendLoot | — |
| CountRollVote#2 | method | Object/GetObjectGuid, ObjectGuid/operator== | WorldSession.GroupHandler/HandleLootRoll | — |
| CountRollVote | method | — | — | — |
| StartLootRoll | method | Creature.Main/StartGroupLoot, Group/GetFirstMember, GroupReference/next, LootMgr/AllowedForPlayer, LootMgr/IsAllowedLooter, Object/GetObjectGuid, Object/IsInWorld, ObjectMgr/GetItemPrototype, Player.Main/CanUseItem#2, WorldObject.Object/IsWithinLootXPDist | — | — |
| SendLootStartRollsForPlayer | method | ByteBuffer/operator<<#10, Creature.Main/GetGroupLootTimer, Map.Main/GetCreature, Object/GetObjectGuid, ObjectGuid/operator!=, ObjectGuid/operator<<, Player.Main/GetSession, WorldObject.Object/GetMap, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | WorldSession.CharacterHandler/HandlePlayerLogin | — |
| EndRoll | method | — | Creature.Main/StopGroupLoot | — |
| CountSingleLooterRoll | method | LootMgr/NotifyItemRemoved, ObjectGuid/GetString, ObjectMgr/GetPlayer, Player.Main/CanStoreNewItem, Player.Main/GetSession, Player.Main/GetShortDescription, Player.Main/OnReceivedItem, Player.Main/Player, Player.Main/SendEquipError, Player.Main/StoreNewItem | — | — |
| CountTheRoll | method | LootMgr/NotifyItemRemoved, ObjectGuid/GetString, ObjectMgr/GetPlayer, Player.Main/CanStoreNewItem, Player.Main/GetSession, Player.Main/GetShortDescription, Player.Main/OnReceivedItem, Player.Main/Player, Player.Main/SendEquipError, Player.Main/StoreNewItem, shared_Util/urand | — | — |
| SetTargetIcon | method | ByteBuffer/operator<<#7, ObjectGuid/ObjectGuid, ObjectGuid/operator<<, ObjectGuid/operator==, WorldPacket/WorldPacket#4 | WorldSession.GroupHandler/HandleRaidTargetUpdateOpcode | — |
| ClearTargetIcon | method | ObjectGuid/ObjectGuid, ObjectGuid/operator== | CreatureAI/ClearTargetIcon | — |
| GetDataForXPAtKill_helper | function | Formulas/GetGrayLevel, Unit.Main/GetLevel | — | — |
| GetDataForXPAtKill | method | Group/GetFirstMember, GroupReference/next, Object/IsInWorld, Player.Main/IsAtGroupRewardDistance, Unit.Main/IsAlive | — | — |
| SendTargetIconList | method | ByteBuffer/operator<<#7, ObjectGuid/operator!, ObjectGuid/operator<<, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | WorldSession.GroupHandler/HandleRaidTargetUpdateOpcode | — |
| SendUpdate | method | ByteBuffer/operator<<, ByteBuffer/operator<<#10, ByteBuffer/operator<<#11, ByteBuffer/operator<<#7, ByteBuffer/wpos, Group/GetLootMethod, Group/GetMembersCount, Group/isRaidGroup, ObjectGuid/operator!, ObjectGuid/operator<<, ObjectGuid/operator==, ObjectMgr/GetPlayer, Player.Main/GetGroup, Player.Main/GetSession, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | Unit.Main/Kill, WorldSession.GroupHandler/HandleLootMethodOpcode | — |
| UpdatePlayerOutOfRange | method | Group/GetFirstMember, GroupReference/next, Object/IsInWorld, Player.Main/GetGroupUpdateFlag, Player.Main/GetSession, Player.Main/IsInVisibleList, WorldPacket/WorldPacket, WorldSession.GroupHandler/BuildPartyMemberStatsChangedPacket, WorldSession.Main/SendPacket | Player.Main/SendUpdateToOutOfRangeGroupMembers | — |
| UpdatePlayerOnlineStatus | method | Group/IsLeader, Group/IsMember, Object/GetObjectGuid, Player.Main/SetGroupUpdateFlag | WorldSession.CharacterHandler/HandlePlayerLogin, WorldSession.Main/LogoutPlayer | — |
| UpdateOfflineLeader | method | Group/isBGGroup, ObjectMgr/GetPlayer, ObjectMgr/GetPlayerAccountIdByGUID, World/FindSession, WorldSession.Main/PlayerLoading | World/Update | — |
| BroadcastPacket | method | Group/GetFirstMember, GroupReference/getSubGroup, GroupReference/next, Object/GetObjectGuid, ObjectGuid/ObjectGuid, ObjectGuid/operator==, Player.Main/GetGroup, Player.Main/GetSession, WorldSession.Main/SendPacket | LFGQueue/AddGroup, LFGQueue/FindRoleToGroup, LFGQueue/RemoveGroupFromQueue, LFGQueue/Update, Player.Main/SendNewItem, Unit.Main/Kill, WorldSession.ChatHandler/HandleChatMessageOpcode, WorldSession.GroupHandler/HandleMinimapPingOpcode, WorldSession.GroupHandler/HandleRaidReadyCheckOpcode, WorldSession.GroupHandler/HandleRandomRollOpcode | — |
| BroadcastReadyCheck | method | Group/GetFirstMember, Group/IsAssistant, Group/IsLeader, GroupReference/next, Object/GetObjectGuid, Player.Main/GetSession, WorldSession.Main/SendPacket | — | — |
| OfflineReadyCheck | method | — | WorldSession.GroupHandler/HandleRaidReadyCheckOpcode | — |
| _addMember | method | — | — | — |
| _addMember#2 | method | Database/BeginTransaction, Database/CommitTransaction, Database/PExecute#2, Group/isBGGroup, Group/IsFull, Group/SubGroupCounterIncrease, Log.Main/Out, MapPersistentStateMgr/GetInstanceId, ObjectGuid/GetCounter, ObjectGuid/operator!, ObjectGuid/operator==, ObjectMgr/GetPlayer, Player.Main/GetGroup, Player.Main/GetGroupInvite, Player.Main/IsSavingDisabled, Player.Main/SetBattleGroundRaid, Player.Main/SetGroup, Player.Main/SetGroupInvite, Player.Main/SetOriginalGroup, WorldObject.Object/GetInstanceId, WorldObject.Object/GetMapId | — | group_member |
| _removeMember | method | Database/BeginTransaction, Database/CommitTransaction, Database/PExecute#2, Group/isBGGroup, Group/SubGroupCounterDecrease, Group/_getMemberWSlot, ObjectGuid/GetCounter, ObjectGuid/operator==, ObjectMgr/GetPlayer, Player.Main/GetOriginalGroup, Player.Main/RemoveFromBattleGroundRaid, Player.Main/SetGroup, Player.Main/SetOriginalGroup | — | group_member |
| _chooseLeader | method | Group/GetMembersCount, Group/GetMembersMinCount, Group/isRaidGroup, ObjectGuid/IsEmpty, ObjectGuid/ObjectGuid, ObjectGuid/operator==, ObjectMgr/GetPlayer, Player.Main/GetGroup | — | — |
| _setLeader | method | Database/BeginTransaction, Database/CommitTransaction, Database/PExecute#2, DungeonPersistentState/RemoveGroup, Errors/PrintStacktraceAndThrow, Group/isBGGroup, Group/_getMemberCSlot, MapPersistentStateMgr/GetInstanceId, ObjectGuid/GetCounter, ObjectGuid/operator!=, ObjectMgr/GetPlayer, Player.Main/BindToInstance, Player.Main/ConvertInstancesToGroup, Player.Main/GetBoundInstance | — | character_instance, groups, group_instance |
| _updateLeaderFlag | method | ObjectMgr/GetPlayer, Player.Main/UpdateGroupLeaderFlag | — | — |
| _removeRolls | method | — | — | — |
| _swapMembersGroup | method | Database/BeginTransaction, Database/CommitTransaction, Database/PExecute#2, Group/isBGGroup, Group/_getMemberWSlot, ObjectGuid/GetCounter | — | group_member |
| _setMembersGroup | method | Database/BeginTransaction, Database/CommitTransaction, Database/PExecute#2, Group/isBGGroup, Group/SubGroupCounterIncrease, Group/_getMemberWSlot, ObjectGuid/GetCounter | — | group_member |
| _setAssistantFlag | method | Database/BeginTransaction, Database/CommitTransaction, Database/PExecute#2, Group/isBGGroup, Group/_getMemberWSlot, ObjectGuid/GetCounter | — | group_member |
| _setMainTank | method | Database/BeginTransaction, Database/CommitTransaction, Database/PExecute#2, Group/isBGGroup, Group/_getMemberCSlot, ObjectGuid/GetCounter, ObjectGuid/ObjectGuid, ObjectGuid/operator== | — | groups |
| _setMainAssistant | method | Database/BeginTransaction, Database/CommitTransaction, Database/PExecute#2, Group/isBGGroup, Group/_getMemberWSlot, ObjectGuid/GetCounter, ObjectGuid/ObjectGuid, ObjectGuid/operator== | — | groups |
| SameSubGroup | method | Player.Main/GetGroup#2, Player.Main/GetSubGroup | PetAI/UpdateAllies, Player.Main/IsInSameGroupWith, Totem/UnSummon, Unit.SpellAuras/Update | — |
| ChangeMembersGroup | method | Group/GetMemberGroup, Group/isRaidGroup, Group/SubGroupCounterDecrease, ObjectMgr/GetPlayer | WorldSession.GroupHandler/HandleGroupChangeSubGroupOpcode | — |
| ChangeMembersGroup#2 | method | Group/GetMemberGroup, Group/isRaidGroup, Group/SubGroupCounterDecrease, GroupReference/setSubGroup, Object/GetObjectGuid, Player.Main/GetGroup, Player.Main/GetGroupRef, Player.Main/GetOriginalGroupRef, Player.Main/GetOriginalSubGroup | WorldSession.GroupHandler/HandleGroupChangeSubGroupOpcode | — |
| SwapMembersGroup | method | Group/GetMemberGroup, Group/isRaidGroup, GroupReference/setSubGroup, ObjectMgr/GetPlayer, Player.Main/GetGroup, Player.Main/GetGroupRef, Player.Main/GetOriginalGroupRef | WorldSession.GroupHandler/HandleGroupSwapSubGroupOpcode | — |
| SwapMembersGroup#2 | method | Group/GetMemberGroup, Group/isRaidGroup, GroupReference/setSubGroup, Object/GetObjectGuid, Player.Main/GetGroup, Player.Main/GetGroupRef, Player.Main/GetOriginalGroupRef | WorldSession.GroupHandler/HandleGroupSwapSubGroupOpcode | — |
| CanJoinBattleGroundQueue | method | Group/GetFirstMember, Group/GetMembersCount, GroupReference/next, Object/GetGUIDLow, Object/IsInWorld, Player.Main/CanJoinToBattleground, Player.Main/GetBattleGroundBracketIdFromLevel, Player.Main/GetTeam, Player.Main/HasFreeBattleGroundQueueId, Player.Main/InBattleGround, Player.Main/InBattleGroundQueueForBattleGroundQueueType, World/getConfig | WorldSession.BattleGroundHandler/RequestBgJoinQueue | — |
| InCombatToInstance | method | Group/GetFirstMember, GroupReference/next, Unit.Main/IsInCombat, WorldObject.Object/GetInstanceId | Map.Main/CanEnter#2 | — |
| ResetInstances | method | Database/PExecute#2, DungeonPersistentState/CanReset, DungeonPersistentState/RemoveGroup, Group/isBGGroup, Map.Main/IsDungeon, Map.Main/Reset, MapManager/FindMap, MapPersistentStateMgr/DeleteFromDB, MapPersistentStateMgr/GetInstanceId, MapPersistentStateMgr/GetMapId, ObjectMgr/GetPlayer, Player.Main/SendResetInstanceFailed, Player.Main/SendResetInstanceSuccess | WorldSession.MiscHandler/HandleResetInstancesOpcode | group_instance |
| GetBoundInstance | method | — | ChatHandler.TeleportCommands/HandleGonameCommand, Map.Main/BindPlayerOrGroupOnEnter, Player.Main/GetBoundInstanceSaveForSelfOrGroup, Player.Main/ResetPersonalInstanceOnLeaveDungeon | — |
| BindToInstance | method | Database/PExecute#2, DungeonPersistentState/AddGroup, DungeonPersistentState/RemoveGroup, Errors/PrintStacktraceAndThrow, Group/GetId, Group/GetLeaderGuid, Group/isBGGroup, Log.Main/Out, MapPersistentStateMgr/GetInstanceId, MapPersistentStateMgr/GetMapId, ObjectGuid/GetCounter | ChatHandler.TeleportCommands/HandleGonameCommand, Map.Main/BindPlayerOrGroupOnEnter, Map.Main/PermBindAllPlayers, ObjectMgr/LoadGroups, Player.Main/ConvertInstancesToGroup | group_instance |
| UnbindInstance | method | Database/PExecute#2, DungeonPersistentState/RemoveGroup, Group/GetLeaderGuid, MapPersistentStateMgr/GetInstanceId, ObjectGuid/GetCounter | MapPersistentStateMgr/UnbindThisState | group_instance |
| _homebindIfInstance | method | Map.Main/GetId, Map.Main/IsDungeon, Player.Main/GetBoundInstance, Player.Main/IsGameMaster, WorldObject.Object/GetMap | — | — |
| RewardGroupAtKill_helper | function | BattleGround/GetTypeID, Creature.Main/GetCreatureInfo, Object/GetObjectGuid, Object/GetTypeId, Object/HasFlag, ObjectMgr/GetFactionEntry, Pet.Main/GivePetXP, Player.Main/GetBattleGround, Player.Main/GetReputationMgr, Player.Main/GetTeam, Player.Main/GiveXP, Player.Main/KilledMonster, Player.Main/RewardHonor, Player.Main/RewardReputation#2, Player.Main/ToPlayer, ReputationMgr/ModifyReputation, Unit.Main/GetLevel, Unit.Main/GetPet, Unit.Main/IsAlive, World/GetWowPatch | — | — |
| RewardGroupAtKill | method | Formulas/Gain, Formulas/xp_in_group_rate, Group/GetFirstMember, Group/isRaidGroup, GroupReference/next, MapEntry/IsDungeon, MapEntry/IsRaid, Object/IsInWorld, Player.Main/IsAtGroupRewardDistance, Unit.Main/IsCharmerOrOwnerPlayerOrPlayerItself, WorldObject.Object/GetMapId | Unit.Main/Kill | — |
| BroadcastGroupUpdateHelper | ctor | — | — | — |
| operator() | method | Object/GetTypeId, WorldObject.Object/ForceValuesUpdateAtIndex | — | — |
| BroadcastGroupUpdate | method | Object/IsInWorld, ObjectMgr/GetPlayer, WorldObject.Object/ForceValuesUpdateAtIndex | Unit.SpellAuras/HandleModCharm, WorldSession.GroupHandler/HandleGroupAcceptOpcode, WorldSession.GroupHandler/HandleGroupDisbandOpcode | — |
| SendLooter | method | Errors/PrintStacktraceAndThrow | Unit.Main/Kill, WorldSession.LootHandler/DoLootRelease | — |
| UpdateLooterGuid | method | Group/GetLooterGuid, Group/GetLootMethod, Group/SetLooterGuid, Group/_getMemberCSlot, Object/GetObjectGuid, ObjectAccessor/FindPlayer, ObjectGuid/ObjectGuid#5, ObjectGuid/operator!=, WorldObject.Object/IsWithinLootXPDist | Player.Main/SendLoot, Unit.Main/Kill | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `character_instance`: guid int(11) unsigned PK, instance int(11) unsigned PK, permanent tinyint(1) unsigned
- `group_instance`: leader_guid int(11) unsigned PK, instance int(11) unsigned PK, permanent tinyint(1) unsigned
- `group_member`: group_id int(11) unsigned PK, member_guid int(11) unsigned PK, assistant tinyint(1) unsigned, subgroup smallint(6) unsigned
- `groups`: group_id int(11) unsigned PK, leader_guid int(11) unsigned, main_tank_guid int(11) unsigned, main_assistant_guid int(11) unsigned, loot_method tinyint(4) unsigned, loot_threshold tinyint(4) unsigned, looter_guid int(11) unsigned, icon1 int(11) unsigned, icon2 int(11) unsigned, icon3 int(11) unsigned, icon4 int(11) unsigned, icon5 int(11) unsigned, icon6 int(11) unsigned, icon7 int(11) unsigned, icon8 int(11) unsigned, is_raid tinyint(1) unsigned

*`?` = nullable, `PK` = primary key column.*

