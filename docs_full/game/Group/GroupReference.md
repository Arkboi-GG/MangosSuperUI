<!-- provenance: verbose -->
# GroupReference

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`GroupReference` is a node in the doubly-linked list managing group membership, inheriting from `Reference<Group, Player>`. It links a `Group` (source) to a `Player` (target). Its responsibilities are:
1.  **Subgroup Tracking:** Stores the `iSubGroup` index for raid subgroup assignment.
2.  **Lifecycle Notification:** Overrides base class hooks to notify the `Group` via `LinkMember` and `DelinkMember` when the reference is created or destroyed, synchronizing the `Group`'s internal state.
3.  **Typed Traversal:** Provides `next()` for iterating over group members.

## Member-by-Member Behavior

### Lifecycle and Link Management

*   **`GroupReference` (Constructor):** Initializes the base `Reference` and sets `iSubGroup` to `0`.
*   **`~GroupReference` (Destructor):** Calls `unlink()`, triggering `sourceObjectDestroyLink` and `targetObjectDestroyLink` to notify the `Group` of removal before destruction.
*   **`targetObjectBuildLink`:** Invoked by the base class when linking to the target `Player`. Calls `getTarget()->LinkMember(this)` to register with the `Group`.
*   **`targetObjectDestroyLink`:** Invoked by the base class when unlinking from the target `Player`. Calls `getTarget()->DelinkMember(this)` to unregister.
*   **`sourceObjectDestroyLink`:** Invoked by the base class when the source `Group` invalidates the link. Calls `getTarget()->DelinkMember(this)` for symmetric cleanup.

### Data Access and Traversal

*   **`next`:** Returns the next `GroupReference` in the list via `static_cast`, enabling typed iteration.
*   **`getSubGroup`:** Returns `iSubGroup`.
*   **`setSubGroup`:** Sets `iSubGroup`.

## Cross-Unit Boundaries

### Outgoing Calls
*   **`Group/LinkMember`**: Called by `targetObjectBuildLink` to register the member.
*   **`Group/DelinkMember`**: Called by `targetObjectDestroyLink` and `sourceObjectDestroyLink` to unregister the member.

### Incoming Calls
`next()` is called by numerous units for iteration:
*   **AI & Combat:** `AiBotAI.Combat/SelectAttackTarget`, `CombatBotBaseAI/SelectHealTarget`, `PartyBotAI/SelectPartyAttackTarget`, etc.
*   **Group Management:** `ChatHandler.CharacterCommands/HandleGroupAddItemCommand`, `HandleGroupInfoCommand`, etc.
*   **Game Logic:** `game_Group_Group/RewardGroupAtKill`, `Spell.Main/FillRaidOrPartyTargets`, `Creature.Main/GetLootRecipient`, etc.

`getSubGroup()` and `setSubGroup()` are called by `game_Group_Group` and `Player.Main` for raid subgroup management.

## Data Model

This unit does not interact with any database tables.

## Notable Implementation Details

*   **Destructor Safety:** The explicit `unlink()` in the destructor prevents dangling pointers in the `Group` if a `GroupReference` is deleted while linked.
*   **Static Cast:** `next()` uses `static_cast` to `GroupReference*`, relying on the `Group` exclusively managing this list.

## Member Reference

**targetObjectBuildLink**  
Invoked by the base class when linking to the target `Player`. Calls `getTarget()->LinkMember(this)` to register with the `Group`.

**targetObjectDestroyLink**  
Invoked by the base class when unlinking from the target `Player`. Calls `getTarget()->DelinkMember(this)` to unregister.

**GroupReference**  
Constructor initializing the base `Reference` and setting `iSubGroup` to `0`.

**sourceObjectDestroyLink**  
Invoked by the base class when the source `Group` invalidates the link. Calls `getTarget()->DelinkMember(this)` for cleanup.

**~GroupReference**  
Destructor calling `unlink()` to sever links and notify the `Group` before destruction.

**next**  
Returns the next `GroupReference` in the list via `static_cast`, enabling typed iteration.

**getSubGroup**  
Returns the `iSubGroup` index.

**setSubGroup**  
Sets the `iSubGroup` index.

---

<!-- machine-true, projected from graph.json -->

## Map — GroupReference

*Source:* GroupReference.cpp, GroupReference.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| targetObjectBuildLink | method | Group/LinkMember | — | — |
| targetObjectDestroyLink | method | Group/DelinkMember | — | — |
| GroupReference | ctor | — | — | — |
| sourceObjectDestroyLink | method | Group/DelinkMember | — | — |
| ~GroupReference | dtor | — | — | — |
| next | method | — | AiBotAI.Combat/SelectAttackTarget, AiBotAI.Loot/DoAutoLoot, AiBotDoctrineTeam/ResolveFocus, BattleBotAI.Main/SelectAttackTarget, BattleGroundMgr/AddGroup, ChatHandler.CharacterCommands/HandleGroupAddItemCommand, ChatHandler.CharacterCommands/HandleGroupInfoCommand, ChatHandler.CharacterCommands/HandleGroupReplenishCommand, ChatHandler.CharacterCommands/HandleGroupReviveCommand, ChatHandler.CharacterCommands/HandleGroupSummonCommand, ChatHandler.MiscCommands/HandleInstanceGroupUnbindCommand, ChatHandler.PlayerBotMgr/HandlePartyBotAoECommand, ChatHandler.PlayerBotMgr/HandlePartyBotAttackStartCommand, ChatHandler.PlayerBotMgr/HandlePartyBotAttackStopCommand, ChatHandler.PlayerBotMgr/HandlePartyBotClearMarksCommand, ChatHandler.PlayerBotMgr/HandlePartyBotComeToMeCommand, ChatHandler.PlayerBotMgr/HandlePartyBotControlMarkCommand, ChatHandler.PlayerBotMgr/HandlePartyBotFocusMarkCommand, ChatHandler.PlayerBotMgr/HandlePartyBotPauseHelper, ChatHandler.PlayerBotMgr/HandlePartyBotPullCommand, ChatHandler.PlayerBotMgr/HandlePartyBotToggleCastingCommand, ChatHandler.PlayerBotMgr/HandlePartyBotUseGObjectCommand, ChatHandler.TeleportCommands/HandleGroupgoCommand, ChatHandler.TeleportCommands/HandleTeleGroupCommand, CombatBotBaseAI/AreOthersOnSameTarget, CombatBotBaseAI/FindAndPreHealTarget, CombatBotBaseAI/SelectBuffTarget, CombatBotBaseAI/SelectBuffTarget#2, CombatBotBaseAI/SelectDispelTarget, CombatBotBaseAI/SelectHealTarget, CombatBotBaseAI/SelectPeriodicHealTarget, Creature.Main/GetLootRecipient, game_Group_Group/AddMember, game_Group_Group/BroadcastPacket, game_Group_Group/BroadcastReadyCheck, game_Group_Group/CanJoinBattleGroundQueue, game_Group_Group/GetDataForXPAtKill, game_Group_Group/InCombatToInstance, game_Group_Group/MasterLoot, game_Group_Group/RewardGroupAtKill, game_Group_Group/StartLootRoll, game_Group_Group/UpdatePlayerOutOfRange, instance_ruins_of_ahnqiraj/GiveRepAfterRajaxxDeath, LootMgr/FillPlayerDependentLoot, Map.Main/BindPlayerOrGroupOnEnter, Map.ScriptCommands/ScriptCommand_QuestExplored, Map.ScriptCommands/ScriptCommand_StartScriptOnGroup, PartyBotAI/GetDistancingTarget, PartyBotAI/SelectPartyAttackTarget, PartyBotAI/SelectResurrectionTarget, PartyBotAI/SelectShieldTarget, PartyBotAI/ShouldAutoRevive, PetAI/UpdateAllies, Player.Main/GetNextRandomRaidMember, Player.Main/GiveLevel, Player.Main/GroupEventFailHappens, Player.Main/GroupEventHappens, Player.Main/RewardPlayerAndGroupAtCast, Player.Main/RewardPlayerAndGroupAtEvent, quest_stormwind_rendezvous/CompleteQuest, ScriptedEscortAI/IsPlayerOrGroupInRange, ScriptedFollowerAI/GetLeaderForFollower, ScriptedFollowerAI/JustDied, ScriptedFollowerAI/UpdateAI, searing_gorge/QuestAccept_npc_dying_archaeologist, Spell.Effects/EffectScriptEffect, Spell.Main/FillRaidOrPartyTargets, Spell.Main/SetTargetMap, Totem/UnSummon, Unit.SpellAuras/Update, WorldSession.BattleGroundHandler/RequestBgJoinQueue, WorldSession.LootHandler/HandleLootMoneyOpcode, WorldSession.QuestHandler/HandlePushQuestToParty, WorldSession.QuestHandler/HandleQuestgiverAcceptQuestOpcode, ZoneScript/HandleKill | — |
| getSubGroup | method | — | game_Group_Group/BroadcastPacket | — |
| setSubGroup | method | — | game_Group_Group/ChangeMembersGroup#2, game_Group_Group/SwapMembersGroup, game_Group_Group/SwapMembersGroup#2, Player.Main/RemoveFromBattleGroundRaid, Player.Main/SetBattleGroundRaid, Player.Main/SetGroup, Player.Main/SetOriginalGroup | — |
