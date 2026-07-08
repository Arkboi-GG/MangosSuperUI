# Group

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Group

## Purpose & Responsibilities

`Group` is the central data structure representing a player party or raid within the `wowvmangos` server. It manages the lifecycle, membership, roles, and state of a collection of players acting together. Its primary responsibilities include:

1.  **Membership Management:** Tracking invited players, active members, and their specific roles (leader, assistant, main tank, main assistant).
2.  **Group Type Handling:** Distinguishing between normal parties (max 5 members) and raids (max 40 members, subdivided into subgroups).
3.  **Loot Distribution:** Configuring and enforcing loot rules (Free-for-All, Round Robin, Master Loot, Group Loot, Need Before Greed) and managing the rolling system for contested items.
4.  **Instance Binding:** Managing persistent bindings to dungeon instances, ensuring group members remain linked to the same instance state.
5.  **Communication & Updates:** Broadcasting status updates (health, power, auras, position) to group members and handling chat/command routing within the group.
6.  **Integration Points:** Serving as the authoritative source for group-related queries by other subsystems, including Battlegrounds (`BattleGround`), Looking For Group (`LFGMgr`), AI bots (`AiBotAI`, `PartyBotAI`), and combat logic (`Unit`, `Creature`).

This unit is defined in `Group.h` and implemented primarily in `game_Group_Group.cpp` (referenced in the MAP as `game_Group_Group`). The header file contains inline implementations for simple getters/setters and helper functions, while complex logic resides in the corresponding `.cpp` file.

## Member-by-Member Behavior

The members of `Group` are categorized below by their functional domain.

### Group Identity and State

*   **`GetId`**: Returns the unique numeric ID of the group. This is heavily used across the codebase to identify the group in database operations, chat messages, and instance binding.
*   **`IsFull`**: Determines if the group has reached its maximum capacity. For normal groups, this is 5 (`MAX_GROUP_SIZE`); for raids, it is 40 (`MAX_RAID_SIZE`). Used by invitation handlers to reject new invites.
*   **`isRaidGroup`**: Returns `true` if the group type is `GROUPTYPE_RAID`. This flag dictates many behaviors, such as subgroup management, raid target icons, and specific loot rules.
*   **`isBGGroup`**: Returns `true` if the group is associated with a `BattleGround` instance. This affects loot permissions (e.g., allowing looting in PvP zones) and disbanding logic.
*   **`IsCreated`**: Returns `true` if the group has at least one member. This is a lightweight check to ensure the group object represents an active entity.

### Leadership and Roles

*   **`GetLeaderGuid`** / **`GetLeaderName`**: Retrieve the GUID and name of the current group leader. These are critical for permission checks (e.g., who can kick members, change loot method, or bind to instances).
*   **`IsLeader`**: Checks if a given GUID matches the leader's GUID.
*   **`IsAssistant`**: Checks if a member has the "assistant" flag set. Assistants have elevated permissions in raids (e.g., moving members between subgroups, ready checks).
*   **`SetAssistant`**: Sets the assistant flag for a member. Only valid for raid groups. Triggers a group update.
*   **`GetMainTankGuid`** / **`GetMainAssistantGuid`**: Retrieve the GUIDs of the designated Main Tank and Main Assistant. These roles are primarily for raid organization and UI display.
*   **`SetMainTank`** / **`SetMainAssistant`**: Assign the main tank or assistant role. Only valid for raid groups. Triggers a group update.

### Membership Management

*   **`IsMember`**: Checks if a GUID is present in the group's member list. Used extensively for permission checks, spell targeting, and event propagation.
*   **`GetMemberGuid`**: Looks up a member's GUID by their character name. Used when commands or interactions specify a name rather than a GUID.
*   **`GetMemberSlots`**: Returns the list of all member slots. Used for iterating over members for updates, XP distribution, or broadcasting.
*   **`GetMembersCount`**: Returns the total number of members. Used for capacity checks, XP calculation, and queue eligibility.
*   **`GetMembersMinCount`**: Returns the minimum number of members required for certain actions (e.g., 1 for BG groups, 2 otherwise).
*   **`GetMemberGroup`**: Returns the subgroup index (0-7) for a raid member. Used for subgroup-specific commands and battleground assignments.
*   **`HasFreeSlotSubGroup`**: Checks if a specific subgroup has space for more members. Used when moving members between subgroups.
*   **`GetFirstMember`**: Returns a pointer to the first member in the internal linked list (`GroupRefManager`). This is often used as a starting point for iteration or as a representative member for group-wide actions (e.g., determining group team, finding a target for spells).

### Loot Configuration

*   **`SetLootMethod`** / **`GetLootMethod`**: Sets/gets the current loot distribution rule (`FREE_FOR_ALL`, `ROUND_ROBIN`, `MASTER_LOOT`, `GROUP_LOOT`, `NEED_BEFORE_GREED`).
*   **`SetLooterGuid`** / **`GetLooterGuid`**: Sets/gets the GUID of the "looter" (the person who initiates the loot window). In Master Loot mode, this is the Loot Master.
*   **`SetLootThreshold`** / **`GetLootThreshold`**: Sets/gets the minimum item quality that triggers a roll or master loot decision. Lower-quality items may be automatically distributed.

### Battleground and LFG Integration

*   **`SetBattlegroundGroup`**: Associates the group with a `BattleGround` instance. This marks the group as a BG group, affecting loot and disbanding behavior.
*   **`SetLFGAreaId`** / **`GetLFGAreaId`**: Sets/gets the Area ID for the Looking For Group (LFG) system. Used to track which dungeon/raid the group is queuing for.
*   **`IsInLFG`**: Returns `true` if the group is currently queued for an LFG dungeon/raid. Prevents certain actions (like disbanding) while in queue.

### Instance Binding

*   **`GetBoundInstances`**: Returns a map of instance IDs to `InstanceGroupBind` structures. Used to list bound instances for a group.
*   **`GetTeam`**: Returns the faction team (Alliance/Horde) of the group. Used for loot generation and player-dependent loot logic.

### Internal Helpers and Subgroup Management

These methods are primarily called by other members within `Group` (specifically in `game_Group_Group`) to manage internal state efficiently.

*   **`_initRaidSubGroupsCounter`**: Initializes the array tracking the number of members in each raid subgroup. Called during group creation, conversion to raid, or loading from DB.
*   **`_getMemberCSlot`** / **`_getMemberWSlot`**: Internal iterators to find a member's slot in the `m_memberSlots` list. The `C` version is const-safe; the `W` version allows modification.
*   **`SubGroupCounterIncrease`** / **`SubGroupCounterDecrease`**: Adjusts the count of members in a specific subgroup. Called when members are added, removed, or moved between subgroups.
*   **`LinkMember`** / **`DelinkMember`**: Manages the bidirectional links between the `Group` and `GroupReference` objects. `LinkMember` adds a reference to the `GroupRefManager`; `DelinkMember` removes it. These are called by `GroupReference` during construction/destruction to maintain the integrity of the group's member list.

## Cross-Unit Boundaries

`Group` acts as a hub, interacting with numerous other units. Below are the key collaborations:

### With `Unit.Main` (Creature/Player Death and Loot)
*   **Called by `Unit.Main/Kill`**: When a unit dies, `Kill` calls `GetId`, `SetLooterGuid`, `SetLootThreshold`, `GetLootMethod`, `GetLooterGuid`, `GetMemberGroup`, and `GetMembersCount`. This determines who gets loot, how it's distributed, and whether the killer's group is eligible for rewards.
*   **Collaboration**: `Unit` relies on `Group` to resolve loot recipients and apply group-specific loot rules.

### With `WorldSession.GroupHandler` (Client Commands)
*   **Called by various `Handle...Opcode` methods**: Client commands for inviting, kicking, changing leaders, setting loot methods, etc., all invoke `Group` methods like `SetLootMethod`, `SetLooterGuid`, `IsFull`, `IsLeader`, `IsAssistant`, `GetMemberGuid`, etc.
*   **Collaboration**: `WorldSession` parses client packets and delegates the actual state changes to `Group`. `Group` validates permissions and updates internal state.

### With `game_Group_Group` (Internal Group Logic)
*   **Calls out**: None listed in MAP, but `Group` methods are extensively called by `game_Group_Group` functions (e.g., `AddMember`, `RemoveMember`, `SendUpdate`).
*   **Called by**: Almost all `Group` methods are called by `game_Group_Group` functions. This is the primary implementation unit for `Group`'s complex logic.
*   **Collaboration**: `game_Group_Group` implements the detailed algorithms for adding/removing members, sending network updates, and managing raid subgroups, using `Group`'s data accessors and mutators.

### With `AiBotAI` and `PartyBotAI` (Bot Behavior)
*   **Called by**: `AiBotAI.Bridge/BridgeHandleFormGroup`, `AiBotAI.Loot/DoAutoLoot`, `AiBotAI.Main/UpdateAI`, `PartyBotAI/GetPartyLeader`, `PartyBotAI/SelectAttackTarget`, etc.
*   **Collaboration**: Bots query `Group` to determine group composition, leadership, loot settings, and targets. They use this information to make decisions about attacking, healing, looting, and following orders.

### With `Creature.Main` and `GameObject` (Loot Recipients)
*   **Called by**: `Creature.Main/SetLootRecipient`, `Creature.Main/StartGroupLoot`, `GameObject/Use`.
*   **Collaboration**: When a creature or game object is killed/used, these units call `Group` methods to determine if a group should receive credit or loot, and who the recipient should be.

### With `Map.Main` and `MapManager` (Instance Binding)
*   **Called by**: `Map.Main/BindPlayerOrGroupOnEnter`, `MapManager/CanPlayerEnter`.
*   **Collaboration**: When players enter instances, `Map` units consult `Group` to check for existing group binds and enforce instance sharing rules.

### With `LFGMgr` and `LFGQueue` (Looking For Group)
*   **Called by**: `LFGMgr/UpdateGroup`, `LFGMgr/AddToQueue`, `LFGQueue/Update`, `LFGQueue/RemoveGroupFromQueue`.
*   **Collaboration**: The LFG system uses `Group` to track which groups are queued, their area IDs, and their composition. It updates `Group` state when groups join or leave queues.

### With `BattleGroundMgr` and `BattleGround` (Battlegrounds)
*   **Called by**: `BattleGroundMgr/AddGroup`, `game_Battlegrounds_BattleGround/SetBgRaid`, `game_Battlegrounds_BattleGround/AddOrSetPlayerToCorrectBgGroup`.
*   **Collaboration**: Battlegrounds assign groups to teams and manage their participation. `Group` provides membership and role information needed for this assignment.

### With `ChatHandler` (Console/Chat Commands)
*   **Called by**: Various `Handle...Command` methods in `ChatHandler.CharacterCommands`, `ChatHandler.TeleportCommands`, etc.
*   **Collaboration**: GM and player chat commands query `Group` for information (e.g., group info, member lists) or perform actions (e.g., summoning group members).

## Data Model

The `Group` class itself does not directly execute SQL queries in the provided source. However, it interacts with database tables through other units (primarily `ObjectMgr` and `game_Group_Group`). Based on the MAP and common WoW server architecture, the relevant tables are:

*   **`groups`**: Stores group metadata (ID, leader GUID, loot method, etc.). `ObjectMgr/LoadGroups` and `ObjectMgr/AddGroup` interact with this table.
*   **`group_member`**: Stores individual member details (group ID, member GUID, subgroup, assistant flag). `ObjectMgr/LoadGroups` and `game_Group_Group/LoadMemberFromDB` interact with this table.
*   **`instance_save`** / **`instance_boss_save`**: While not directly queried by `Group`, `Group`'s `InstanceGroupBind` structures relate to these tables, which store instance state and boss kill data. `Game_Group_Group/BindToInstance` likely interacts with these via `DungeonPersistentState`.

*Note: Specific column names and types are not provided in the SCHEMA section, so they are not detailed here.*

## Notable Implementation Details

1.  **Inline Getters/Setters**: Many simple accessors (e.g., `GetId`, `SetLootMethod`, `IsRaidGroup`) are implemented inline in `Group.h`. This improves performance for frequent calls but means changes to these methods require recompilation of any file including `Group.h`.
2.  **Subgroup Counter Array**: `m_subGroupsCounts` is a dynamically allocated array (`uint8*`) used only for raid groups. It is initialized in `_initRaidSubGroupsCounter` and updated via `SubGroupCounterIncrease`/`Decrease`. Care must be taken to ensure this array is properly allocated and freed to avoid memory leaks or corruption.
3.  **Member Slot List**: `m_memberSlots` is a `std::list<MemberSlot>`. Iterating over this list is done via `_getMemberCSlot`/`_getMemberWSlot`, which perform linear searches. For large raids, this could be a performance bottleneck, though 40 members is small enough that it's likely acceptable.
4.  **GroupReference Linking**: `LinkMember` and `DelinkMember` manage the connection between `Group` and `GroupReference` objects. `DelinkMember` is currently empty (`{ }`), suggesting that the actual unlinking might happen elsewhere (possibly in `GroupReference`'s destructor or `GroupRefManager`). This asymmetry is worth noting for maintainers.
5.  **Loot Threshold**: `m_lootThreshold` is an `ItemQualities` enum. This allows fine-grained control over which items trigger loot windows or rolls.
6.  **Target Icons**: `m_targetIcons` is an array of 8 `ObjectGuid`s, corresponding to the 8 raid target icons. `GetTargetWithIcon` retrieves the target for a given icon ID. This is used for raid marking and bot targeting.
7.  **BG Group Flag**: `isBGGroup()` checks if `m_bgGroup` is non-null. This flag is set by `SetBattlegroundGroup` and affects loot and disbanding logic. It's crucial for ensuring proper behavior in PvP environments.
8.  **LFG Area ID**: `m_LFGAreaId` tracks the LFG queue area. `IsInLFG()` simply checks if this ID is greater than 0. This is a simple but effective way to track LFG participation.

## Member Reference

**SetLootMethod**: Sets the loot distribution method (`m_lootMethod`). Called by `AiBotAI.Bridge/BridgeHandleFormGroup`, `Unit.Main/Kill`, and `WorldSession.GroupHandler/HandleLootMethodOpcode`.

**SetLooterGuid**: Sets the GUID of the looter (`m_looterGuid`). Called by `game_Group_Group/UpdateLooterGuid`, `Unit.Main/Kill`, and `WorldSession.GroupHandler/HandleLootMethodOpcode`.

**SetLootThreshold**: Sets the minimum item quality for loot windows (`m_lootThreshold`). Called by `Unit.Main/Kill` and `WorldSession.GroupHandler/HandleLootMethodOpcode`.

**GetId**: Returns the group's unique ID (`m_Id`). Called by numerous units for identification, including `ChatHandler`, `Creature`, `GameObject`, `Map`, `ObjectMgr`, `Spell`, and `WorldSession`.

**IsFull**: Returns `true` if the group has reached its maximum size (5 for parties, 40 for raids). Called by `ChatHandler`, `game_Group_Group`, `LFGMgr`, and `WorldSession` to prevent over-inviting.

**isRaidGroup**: Returns `true` if the group is a raid (`m_groupType == GROUPTYPE_RAID`). Called by `ChatHandler`, `game_Group_Group`, `MapManager`, `PetAI`, `Player`, and `WorldSession` to enable raid-specific features.

**isBGGroup**: Returns `true` if the group is associated with a Battleground (`m_bgGroup != nullptr`). Called by `Creature`, `game_Group_Group`, `Player`, and `WorldSession` to adjust loot and disbanding behavior.

**IsCreated**: Returns `true` if the group has at least one member. Called by `LFGQueue`, `Player`, and `WorldSession` to verify group existence.

**GetLeaderGuid**: Returns the leader's GUID (`m_leaderGuid`). Called by `ChatHandler`, `game_Group_Group`, `Map`, `PartyBotAI`, `Player`, `Unit`, and `WorldSession` for permission checks and leadership actions.

**GetLeaderName**: Returns the leader's name (`m_leaderName`). Called by `ChatHandler` and `WorldSession` for display purposes.

**GetLootMethod**: Returns the current loot method (`m_lootMethod`). Called by `AiBotAI`, `game_Group_Group`, `Player`, `Unit`, and `WorldSession` to determine loot distribution rules.

**GetLooterGuid**: Returns the looter's GUID (`m_looterGuid`). Called by `game_Group_Group`, `Unit`, and `WorldSession` to identify the loot initiator.

**GetLootThreshold**: Returns the loot quality threshold (`m_lootThreshold`). Called by `LootMgr` to filter items for loot windows.

**IsMember**: Checks if a GUID is in the group. Called by `AiBotAI`, `Corpse`, `GameObject`, `game_Battlegrounds`, `game_Group_Group`, `ObjectMgr`, `Player`, `Spell`, `Unit`, and `WorldSession` for membership verification.

**IsLeader**: Checks if a GUID is the group leader. Called by `ChatHandler`, `game_Battlegrounds`, `game_Group_Group`, `LFGMgr`, `Player`, and `WorldSession` for permission checks.

**GetMemberGuid**: Looks up a member's GUID by name. Called by `WorldSession.GroupHandler/HandleGroupUninviteOpcode`.

**IsAssistant**: Checks if a member is an assistant. Called by `game_Group_Group`, `Player`, and `WorldSession` for raid permission checks.

**HasFreeSlotSubGroup**: Checks if a subgroup has space. Called by `WorldSession.GroupHandler/HandleGroupChangeSubGroupOpcode`.

**GetMemberSlots**: Returns the list of member slots. Called by `game_Group_Group`, `Player`, and `Unit` for iteration and updates.

**GetFirstMember**: Returns the first member reference. Called by `AiBotAI`, `BattleBotAI`, `BattleGroundMgr`, `ChatHandler`, `Creature`, `game_Group_Group`, `instance_ruins_of_ahnqiraj`, `LootMgr`, `Map`, `PartyBotAI`, `PetAI`, `Player`, `quest_stormwind_rendezvous`, `ScriptedEscortAI`, `ScriptedFollowerAI`, `searing_gorge`, `Spell`, `Totem`, `Unit`, `WorldSession`, and `ZoneScript` for various group-wide actions.

**GetMembersCount**: Returns the number of members. Called by `BattleGroundMgr`, `ChatHandler`, `game_Group_Group`, `LFGMgr`, `ObjectMgr`, `PetAI`, `Player`, and `WorldSession` for capacity and reward calculations.

**GetMembersMinCount**: Returns the minimum member count (1 for BG, 2 otherwise). Called by `game_Group_Group` and `WorldSession` for validation.

**GetMemberGroup**: Returns the subgroup index for a member. Called by `game_Battlegrounds`, `game_Group_Group`, `Player`, `Unit`, and `WorldSession` for subgroup management.

**SetBattlegroundGroup**: Associates the group with a Battleground. Called by `game_Battlegrounds_BattleGround/SetBgRaid`.

**GetMainTankGuid**: Returns the main tank's GUID. No callers listed in MAP.

**GetMainAssistantGuid**: Returns the main assistant's GUID. No callers listed in MAP.

**SetAssistant**: Sets the assistant flag for a member. Called by `WorldSession.GroupHandler/HandleGroupAssistantLeaderOpcode`.

**SetMainTank**: Sets the main tank role. No callers listed in MAP.

**SetMainAssistant**: Sets the main assistant role. No callers listed in MAP.

**GetTargetWithIcon**: Returns the target GUID for a raid icon. Called by `PartyBotAI` for targeting marked enemies.

**SetLFGAreaId**: Sets the LFG area ID. Called by `LFGMgr` and `LFGQueue` for queue management.

**GetLFGAreaId**: Returns the LFG area ID. Called by `WorldSession.LFGHandler/HandleMeetingStoneInfoOpcode`.

**IsInLFG**: Returns `true` if the group is in an LFG queue. Called by `game_Group_Group` and `WorldSession` to restrict actions while queued.

**LinkMember**: Links a `GroupReference` to the group. Called by `GroupReference/targetObjectBuildLink`.

**DelinkMember**: Unlinks a `GroupReference` from the group. Called by `GroupReference/sourceObjectDestroyLink` and `GroupReference/targetObjectDestroyLink`.

**GetBoundInstances**: Returns the map of bound instances. Called by `ChatHandler.MiscCommands/HandleInstanceListBindsCommand`.

**GetTeam**: Returns the group's faction team. Called by `Creature` and `Player` for loot generation.

**_initRaidSubGroupsCounter**: Initializes the subgroup counter array. Called by `game_Group_Group` during creation/conversion/loading.

**_getMemberCSlot**: Finds a member's slot (const). Called by `game_Group_Group` for internal lookups.

**_getMemberWSlot**: Finds a member's slot (writable). Called by `game_Group_Group` for internal modifications.

**SubGroupCounterIncrease**: Increments a subgroup's member count. Called by `game_Group_Group` when members are added or moved.

**SubGroupCounterDecrease**: Decrements a subgroup's member count. Called by `game_Group_Group` when members are removed or moved.

---

<!-- machine-true, projected from graph.json -->

## Map — Group

*Source:* Group.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| SetLootMethod | method | — | AiBotAI.Bridge/BridgeHandleFormGroup, Unit.Main/Kill, WorldSession.GroupHandler/HandleLootMethodOpcode | — |
| SetLooterGuid | method | — | game_Group_Group/UpdateLooterGuid, Unit.Main/Kill, WorldSession.GroupHandler/HandleLootMethodOpcode | — |
| SetLootThreshold | method | — | Unit.Main/Kill, WorldSession.GroupHandler/HandleLootMethodOpcode | — |
| GetId | method | — | ChatHandler.CharacterCommands/HandleGroupInfoCommand, Creature.Main/SetLootRecipient, Creature.Main/StartGroupLoot, GameObject/Use, game_Group_Group/BindToInstance, Map.Main/BindPlayerOrGroupOnEnter, ObjectMgr/AddGroup, ObjectMgr/LoadGroups, ObjectMgr/RemoveGroup, Spell.Effects/EffectTransmitted, WorldSession.ChatHandler/HandleChatMessageOpcode | — |
| IsFull | method | — | ChatHandler.PlayerBotMgr/PartyBotAddRequirementCheck, game_Group_Group/_addMember#2, LFGMgr/UpdateGroup, WorldSession.GroupHandler/HandleGroupAcceptOpcode, WorldSession.GroupHandler/HandleGroupInviteOpcode, WorldSession.LFGHandler/HandleMeetingStoneJoinOpcode | — |
| isRaidGroup | method | — | ChatHandler.CharacterCommands/HandleGroupInfoCommand, game_Group_Group/AddMember, game_Group_Group/ChangeMembersGroup, game_Group_Group/ChangeMembersGroup#2, game_Group_Group/Create, game_Group_Group/Disband, game_Group_Group/RemoveMember, game_Group_Group/RewardGroupAtKill, game_Group_Group/SendUpdate, game_Group_Group/SwapMembersGroup, game_Group_Group/SwapMembersGroup#2, game_Group_Group/_chooseLeader, MapManager/CanPlayerEnter, PetAI/UpdateAllies, Player.Main/HasQuestForGO, Player.Main/HasQuestForItem, Player.Main/KilledMonsterCredit, WorldSession.ChatHandler/HandleChatMessageOpcode, WorldSession.LFGHandler/HandleMeetingStoneJoinOpcode | — |
| isBGGroup | method | — | Creature.Main/IsTappedBy, game_Group_Group/AddInvite, game_Group_Group/AddMember, game_Group_Group/BindToInstance, game_Group_Group/ConvertToRaid, game_Group_Group/Create, game_Group_Group/Disband, game_Group_Group/ResetInstances, game_Group_Group/UpdateOfflineLeader, game_Group_Group/_addMember#2, game_Group_Group/_removeMember, game_Group_Group/_setAssistantFlag, game_Group_Group/_setLeader, game_Group_Group/_setMainAssistant, game_Group_Group/_setMainTank, game_Group_Group/_setMembersGroup, game_Group_Group/_swapMembersGroup, Player.Main/IsAllowedToLoot, Player.Main/SendLoot, WorldSession.ChatHandler/HandleChatMessageOpcode, WorldSession.GroupHandler/HandleGroupInviteOpcode, WorldSession.GroupHandler/HandleLootMethodOpcode | — |
| IsCreated | method | — | LFGQueue/Update, Player.Main/UninviteFromGroup, WorldSession.GroupHandler/HandleGroupAcceptOpcode | — |
| GetLeaderGuid | method | — | ChatHandler.Chat/ExecuteCommand, ChatHandler.TeleportCommands/HandleGroupgoCommand, game_Group_Group/BindToInstance, game_Group_Group/Disband, game_Group_Group/UnbindInstance, Map.Main/PermBindAllPlayers, PartyBotAI/GetPartyLeader, Player.Main/CanUninviteFromGroup, Player.Main/UpdateGroupLeaderFlag, Unit.Main/Kill, WorldSession.GroupHandler/HandleGroupAcceptOpcode, WorldSession.GroupHandler/HandleGroupDeclineOpcode, WorldSession.GroupHandler/HandleRaidReadyCheckOpcode | — |
| GetLeaderName | method | — | ChatHandler.CharacterCommands/HandleGroupInfoCommand, WorldSession.GroupHandler/HandleGroupAcceptOpcode | — |
| GetLootMethod | method | — | AiBotAI.Loot/DoAutoLoot, game_Group_Group/SendUpdate, game_Group_Group/UpdateLooterGuid, Player.Main/IsAllowedToLoot, Player.Main/SendLoot, Unit.Main/Kill, WorldSession.LootHandler/DoLootRelease, WorldSession.LootHandler/HandleAutostoreLootItemOpcode, WorldSession.LootHandler/HandleLootMasterGiveOpcode | — |
| GetLooterGuid | method | — | game_Group_Group/UpdateLooterGuid, Unit.Main/Kill, WorldSession.LootHandler/HandleLootMasterGiveOpcode | — |
| GetLootThreshold | method | — | LootMgr/FillPlayerDependentLoot | — |
| IsMember | method | — | AiBotAI.Main/UpdateAI, Corpse/GetReactionTo, GameObject/Use, game_Battlegrounds_BattleGround/AddOrSetPlayerToCorrectBgGroup, game_Group_Group/UpdatePlayerOnlineStatus, ObjectMgr/GetGroupByMember, Player.Main/IsAllowedWhisperFrom, Spell.Main/SetTargetMap, Unit.SpellAuras/Update#2, WorldSession.GroupHandler/HandleGroupUninviteGuidOpcode | — |
| IsLeader | method | — | ChatHandler.TeleportCommands/HandleGonameCommand, game_Battlegrounds_BattleGround/AddOrSetPlayerToCorrectBgGroup, game_Group_Group/AddMember, game_Group_Group/BroadcastReadyCheck, game_Group_Group/UpdatePlayerOnlineStatus, LFGMgr/AddToQueue, Player.Main/CanUninviteFromGroup, WorldSession.ChatHandler/HandleChatMessageOpcode, WorldSession.GroupHandler/HandleGroupAssistantLeaderOpcode, WorldSession.GroupHandler/HandleGroupChangeSubGroupOpcode, WorldSession.GroupHandler/HandleGroupInviteOpcode, WorldSession.GroupHandler/HandleGroupRaidConvertOpcode, WorldSession.GroupHandler/HandleGroupSetLeaderOpcode, WorldSession.GroupHandler/HandleGroupSwapSubGroupOpcode, WorldSession.GroupHandler/HandleLootMethodOpcode, WorldSession.GroupHandler/HandleRaidReadyCheckOpcode, WorldSession.GroupHandler/HandleRaidTargetUpdateOpcode, WorldSession.LFGHandler/HandleMeetingStoneJoinOpcode, WorldSession.LFGHandler/HandleMeetingStoneLeaveOpcode, WorldSession.MiscHandler/HandleResetInstancesOpcode | — |
| GetMemberGuid | method | — | WorldSession.GroupHandler/HandleGroupUninviteOpcode | — |
| IsAssistant | method | — | game_Group_Group/BroadcastReadyCheck, Player.Main/CanUninviteFromGroup, WorldSession.ChatHandler/HandleChatMessageOpcode, WorldSession.GroupHandler/HandleGroupChangeSubGroupOpcode, WorldSession.GroupHandler/HandleGroupInviteOpcode, WorldSession.GroupHandler/HandleGroupSwapSubGroupOpcode, WorldSession.GroupHandler/HandleRaidReadyCheckOpcode, WorldSession.GroupHandler/HandleRaidTargetUpdateOpcode | — |
| HasFreeSlotSubGroup | method | — | WorldSession.GroupHandler/HandleGroupChangeSubGroupOpcode | — |
| GetMemberSlots | method | — | game_Group_Group/CalculateLFGRoles, game_Group_Group/FillPremadeLFG, Player.Main/RewardHonorOnDeath, Player.Main/SendDestroyGroupMembers, Unit.Main/Kill | — |
| GetFirstMember | method | — | AiBotAI.Combat/SelectAttackTarget, AiBotAI.Loot/DoAutoLoot, AiBotDoctrineTeam/ResolveFocus, BattleBotAI.Main/SelectAttackTarget, BattleGroundMgr/AddGroup, ChatHandler.CharacterCommands/HandleGroupAddItemCommand, ChatHandler.CharacterCommands/HandleGroupInfoCommand, ChatHandler.CharacterCommands/HandleGroupReplenishCommand, ChatHandler.CharacterCommands/HandleGroupReviveCommand, ChatHandler.CharacterCommands/HandleGroupSummonCommand, ChatHandler.MiscCommands/HandleInstanceGroupUnbindCommand, ChatHandler.PlayerBotMgr/HandlePartyBotAoECommand, ChatHandler.PlayerBotMgr/HandlePartyBotAttackStartCommand, ChatHandler.PlayerBotMgr/HandlePartyBotAttackStopCommand, ChatHandler.PlayerBotMgr/HandlePartyBotClearMarksCommand, ChatHandler.PlayerBotMgr/HandlePartyBotComeToMeCommand, ChatHandler.PlayerBotMgr/HandlePartyBotControlMarkCommand, ChatHandler.PlayerBotMgr/HandlePartyBotFocusMarkCommand, ChatHandler.PlayerBotMgr/HandlePartyBotPauseHelper, ChatHandler.PlayerBotMgr/HandlePartyBotPullCommand, ChatHandler.PlayerBotMgr/HandlePartyBotToggleCastingCommand, ChatHandler.PlayerBotMgr/HandlePartyBotUseGObjectCommand, ChatHandler.TeleportCommands/HandleGroupgoCommand, ChatHandler.TeleportCommands/HandleTeleGroupCommand, CombatBotBaseAI/AreOthersOnSameTarget, CombatBotBaseAI/FindAndPreHealTarget, CombatBotBaseAI/SelectBuffTarget, CombatBotBaseAI/SelectBuffTarget#2, CombatBotBaseAI/SelectDispelTarget, CombatBotBaseAI/SelectHealTarget, CombatBotBaseAI/SelectPeriodicHealTarget, Creature.Main/GetLootRecipient, game_Group_Group/AddMember, game_Group_Group/BroadcastPacket, game_Group_Group/BroadcastReadyCheck, game_Group_Group/CanJoinBattleGroundQueue, game_Group_Group/GetDataForXPAtKill, game_Group_Group/InCombatToInstance, game_Group_Group/MasterLoot, game_Group_Group/RewardGroupAtKill, game_Group_Group/StartLootRoll, game_Group_Group/UpdatePlayerOutOfRange, instance_ruins_of_ahnqiraj/GiveRepAfterRajaxxDeath, LootMgr/FillPlayerDependentLoot, Map.Main/BindPlayerOrGroupOnEnter, Map.ScriptCommands/ScriptCommand_QuestExplored, Map.ScriptCommands/ScriptCommand_StartScriptOnGroup, PartyBotAI/GetDistancingTarget, PartyBotAI/SelectPartyAttackTarget, PartyBotAI/SelectResurrectionTarget, PartyBotAI/SelectShieldTarget, PartyBotAI/ShouldAutoRevive, PetAI/UpdateAllies, Player.Main/GetNextRandomRaidMember, Player.Main/GiveLevel, Player.Main/GroupEventFailHappens, Player.Main/GroupEventHappens, Player.Main/RewardPlayerAndGroupAtCast, Player.Main/RewardPlayerAndGroupAtEvent, quest_stormwind_rendezvous/CompleteQuest, ScriptedEscortAI/IsPlayerOrGroupInRange, ScriptedFollowerAI/GetLeaderForFollower, ScriptedFollowerAI/JustDied, ScriptedFollowerAI/UpdateAI, searing_gorge/QuestAccept_npc_dying_archaeologist, Spell.Effects/EffectScriptEffect, Spell.Main/FillRaidOrPartyTargets, Spell.Main/SetTargetMap, Totem/UnSummon, Unit.SpellAuras/Update, WorldSession.BattleGroundHandler/RequestBgJoinQueue, WorldSession.LootHandler/HandleLootMoneyOpcode, WorldSession.QuestHandler/HandlePushQuestToParty, WorldSession.QuestHandler/HandleQuestgiverAcceptQuestOpcode, ZoneScript/HandleKill | — |
| GetMembersCount | method | — | BattleGroundMgr/AddGroup, ChatHandler.CharacterCommands/HandleGroupInfoCommand, ChatHandler.PlayerBotMgr/PartyBotAddRequirementCheck, game_Group_Group/CalculateLFGRoles, game_Group_Group/CanJoinBattleGroundQueue, game_Group_Group/RemoveInvite, game_Group_Group/RemoveMember, game_Group_Group/SendUpdate, game_Group_Group/_chooseLeader, LFGMgr/AddToQueue, ObjectMgr/LoadGroups, PetAI/UpdateAllies, Player.Main/GetNextRandomRaidMember, Player.Main/GiveLevel, Player.Main/UninviteFromGroup, WorldSession.BattleGroundHandler/RequestBgJoinQueue, WorldSession.GroupHandler/HandleGroupRaidConvertOpcode, WorldSession.LootHandler/HandleLootMoneyOpcode | — |
| GetMembersMinCount | method | — | game_Group_Group/RemoveMember, game_Group_Group/_chooseLeader, WorldSession.GroupHandler/HandleGroupRaidConvertOpcode | — |
| GetMemberGroup | method | — | game_Battlegrounds_BattleGround/AddOrSetPlayerToCorrectBgGroup, game_Group_Group/ChangeMembersGroup, game_Group_Group/ChangeMembersGroup#2, game_Group_Group/SwapMembersGroup, game_Group_Group/SwapMembersGroup#2, Player.Main/_LoadGroup, Unit.Main/Kill, WorldSession.ChatHandler/HandleChatMessageOpcode | — |
| SetBattlegroundGroup | method | — | game_Battlegrounds_BattleGround/SetBgRaid | — |
| GetMainTankGuid | method | — | — | — |
| GetMainAssistantGuid | method | — | — | — |
| SetAssistant | method | — | WorldSession.GroupHandler/HandleGroupAssistantLeaderOpcode | — |
| SetMainTank | method | — | — | — |
| SetMainAssistant | method | — | — | — |
| GetTargetWithIcon | method | — | PartyBotAI/GetMarkedTarget, PartyBotAI/SelectAttackTarget | — |
| SetLFGAreaId | method | — | LFGMgr/AddToQueue, LFGQueue/RemoveGroupFromQueue | — |
| GetLFGAreaId | method | — | WorldSession.LFGHandler/HandleMeetingStoneInfoOpcode | — |
| IsInLFG | method | — | game_Group_Group/AddMember, game_Group_Group/Disband, game_Group_Group/RemoveMember, WorldSession.LFGHandler/HandleMeetingStoneInfoOpcode, WorldSession.LFGHandler/HandleMeetingStoneLeaveOpcode | — |
| LinkMember | method | — | GroupReference/targetObjectBuildLink | — |
| DelinkMember | method | — | GroupReference/sourceObjectDestroyLink, GroupReference/targetObjectDestroyLink | — |
| GetBoundInstances | method | — | ChatHandler.MiscCommands/HandleInstanceListBindsCommand | — |
| GetTeam | method | — | Creature.Main/GenerateLootForBody, Creature.Main/GeneratePlayerDependentLoot, Player.Main/SendLoot | — |
| _initRaidSubGroupsCounter | method | — | game_Group_Group/ConvertToRaid, game_Group_Group/Create, game_Group_Group/LoadGroupFromDB | — |
| _getMemberCSlot | method | — | game_Group_Group/ChangeLeader, game_Group_Group/UpdateLooterGuid, game_Group_Group/_setLeader, game_Group_Group/_setMainTank | — |
| _getMemberWSlot | method | — | game_Group_Group/_removeMember, game_Group_Group/_setAssistantFlag, game_Group_Group/_setMainAssistant, game_Group_Group/_setMembersGroup, game_Group_Group/_swapMembersGroup | — |
| SubGroupCounterIncrease | method | — | game_Group_Group/LoadMemberFromDB, game_Group_Group/_addMember#2, game_Group_Group/_setMembersGroup | — |
| SubGroupCounterDecrease | method | — | game_Group_Group/ChangeMembersGroup, game_Group_Group/ChangeMembersGroup#2, game_Group_Group/_removeMember | — |
