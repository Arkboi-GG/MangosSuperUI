# ThreatListCopier — Class Overview

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ThreatListCopier

**ThreatListCopier** is a lightweight utility class that synchronizes threat lists between units by iterating through a source unit’s aggro table and forcing a destination unit to attack each target. It implements the `ThreatListProcesser` interface, serving as a callback handler during `ProcessThreatList` operations. Its sole purpose is to ensure that summoned adds or linked mobs immediately engage the same players as their summoner or primary boss, preventing disjointed combat where the main threat is focused while minions remain passive.

The class is defined identically in two separate translation units—`battleground_alterac.cpp` and `boss_ragnaros.cpp`—to serve distinct encounter needs without introducing cross-module dependencies. It operates entirely on in-memory game state and touches no database tables.

## How the class is split

The `ThreatListCopier` class is duplicated across two partials, each tailored to its specific encounter context:

### ThreatListCopier.battleground_alterac
Defined in `battleground_alterac.cpp`, this partial supports the **Alterac Valley** battleground. It is primarily used by the `npc_alterac_bossHelper` class to synchronize aggro for major bosses like Vanndar and Drek’Thar. When these bosses engage players, `ThreatListCopier` ensures their linked minions (adds) instantly aggro the same targets. The partial includes the constructor and the `Process` method, which delegates to `CreatureAI::AttackStart` to initiate combat for the destination unit.

### ThreatListCopier.boss_ragnaros
Defined in `boss_ragnaros.cpp`, this partial supports the **Molten Core** raid encounter. It is used by `boss_ragnarosAI` during Phase 2 (Submerged/Banished) to synchronize the threat lists of Ragnaros with his summoned "Sons of Flame" adds. Like the Alterac Valley version, it implements the `ThreatListProcesser` interface, iterating through Ragnaros’s threat list and calling `AttackStart` on each Son of Flame to ensure they focus the same players.

## How the partials collaborate

Although defined in separate files, both partials share identical logic and structure. They do not interact with each other but instead collaborate with their respective AI controllers:

- **In Alterac Valley**: `npc_alterac_bossHelper::AggroLinkedMobsIfNeeded` creates a `ThreatListCopier` instance, passes it to `ProcessThreatList`, and then deletes it. The copier’s `Process` method is called for each unit on the boss’s threat list, forcing linked mobs to attack those units.
- **In Molten Core**: `boss_ragnarosAI::SummonSonsOfFlame` creates a `ThreatListCopier` for each Son of Flame, processes Ragnaros’s threat list, and then deletes the copier. The `Process` method ensures each Son of Flame attacks the same players Ragnaros is engaged with.

Both implementations rely on the `Unit::AI()` interface to access the destination unit’s AI and call `AttackStart`. The source unit’s threat list is read-only, while the destination unit’s threat list is modified by the side effects of `AttackStart`.

## Data model

`ThreatListCopier` does not interact with any database tables. It operates exclusively on in-memory objects (`Unit`, `Creature`, `ThreatList`) and relies on the engine’s threat management system to propagate aggro changes.

## Where to go deeper

- **ThreatListCopier.battleground_alterac**: Open this doc to understand how aggro synchronization works for Alterac Valley bosses, including the role of `npc_alterac_bossHelper` and the lifecycle of the copier instance.
- **ThreatListCopier.boss_ragnaros**: Open this doc to explore how Ragnaros’s Sons of Flame inherit his threat list during Phase 2, including the integration with `SummonSonsOfFlame` and the staggered emergence logic.

---

<!-- machine-true, projected from graph.json -->

## Map — ThreatListCopier

*Source:* battleground_alterac.cpp, boss_ragnaros.cpp

| Member | Partial | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|---|
| ThreatListCopier | ThreatListCopier.battleground_alterac | ctor | — | — | — |
| Process | ThreatListCopier.battleground_alterac | method | CreatureAI/AttackStart, Unit.Main/AI | — | — |
| npc_alterac_bossHelper | ThreatListCopier.battleground_alterac | ctor | — | — | — |
| AddLinkedMob | ThreatListCopier.battleground_alterac | method | — | — | — |
| AggroLinkedMobsIfNeeded | ThreatListCopier.battleground_alterac | method | Creature.Main/ProcessThreatList, GridSearchers/GetCreatureListWithEntryInGrid#2, Unit.Main/IsAlive, Unit.Main/IsInCombat | — | — |
| npc_VanndarAI | ThreatListCopier.battleground_alterac | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#19 | ThreatListCopier.battleground_alterac | method | shared_Util/urand, Unit.Main/ClearUnitState | — | — |
| EnterEvadeMode#3 | ThreatListCopier.battleground_alterac | method | ScriptedAI/EnterEvadeMode, ScriptMgr/DoScriptText | — | — |
| Aggro#13 | ThreatListCopier.battleground_alterac | method | ScriptMgr/DoScriptText | — | — |
| MoveInLineOfSight#5 | ThreatListCopier.battleground_alterac | method | CreatureAI/AttackStart, Unit.Main/GetVictim, Unit.Main/IsHostileTo, WorldObject.Object/GetDistance#3 | — | — |
| UpdateAI#15 | ThreatListCopier.battleground_alterac | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, ScriptMgr/DoScriptText, shared_Util/urand, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/ClearUnitState, Unit.Main/CombatStop, Unit.Main/GetHealthPercent, Unit.Main/GetMaxHealth, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, Unit.Main/SetHealth, WorldObject.Object/GetDistance2d#4, WorldObject.Object/GetMapId | — | — |
| GetAI_npc_Vanndar | ThreatListCopier.battleground_alterac | function | — | — | — |
| npc_DrekTharAI | ThreatListCopier.battleground_alterac | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#17 | ThreatListCopier.battleground_alterac | method | Creature.Main/Respawn, GridSearchers/GetCreatureListWithEntryInGrid#2, shared_Util/urand, Unit.Main/ClearUnitState, Unit.Main/IsAlive, Unit.Main/RemoveAurasDueToSpell | — | — |
| EnterEvadeMode#2 | ThreatListCopier.battleground_alterac | method | ScriptedAI/EnterEvadeMode, ScriptMgr/DoScriptText | — | — |
| Aggro#11 | ThreatListCopier.battleground_alterac | method | ScriptMgr/DoScriptText | — | — |
| MoveInLineOfSight#3 | ThreatListCopier.battleground_alterac | method | CreatureAI/AttackStart, Unit.Main/GetVictim, Unit.Main/IsHostileTo, WorldObject.Object/GetDistance#3 | — | — |
| UpdateAI#13 | ThreatListCopier.battleground_alterac | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, ScriptMgr/DoScriptText, shared_Util/urand, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/AddUnitState, Unit.Main/ClearUnitState, Unit.Main/CombatStop, Unit.Main/GetHealthPercent, Unit.Main/GetMaxHealth, Unit.Main/GetVictim, Unit.Main/HasUnitState, Unit.Main/SelectHostileTarget, Unit.Main/SetHealth, WorldObject.Object/GetDistance2d#4, WorldObject.Object/GetMapId | — | — |
| GetAI_npc_DrekThar | ThreatListCopier.battleground_alterac | function | — | — | — |
| npc_BalindaAI | ThreatListCopier.battleground_alterac | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#16 | ThreatListCopier.battleground_alterac | method | ScriptMgr/DoScriptText, Unit.Main/ClearUnitState | — | — |
| MoveInLineOfSight#2 | ThreatListCopier.battleground_alterac | method | CreatureAI/AttackStart, Unit.Main/GetVictim, Unit.Main/IsHostileTo, WorldObject.Object/GetDistance#3 | — | — |
| Aggro#10 | ThreatListCopier.battleground_alterac | method | ScriptMgr/DoScriptText | — | — |
| UpdateAI#12 | ThreatListCopier.battleground_alterac | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, ScriptedAI/EnterEvadeMode, ScriptMgr/DoScriptText, shared_Util/urand, SpellCaster/IsNonMeleeSpellCasted, ThreatManager/getThreatList, Unit.Main/AddUnitState, Unit.Main/ClearUnitState, Unit.Main/CombatStop, Unit.Main/GetHealthPercent, Unit.Main/GetMaxHealth, Unit.Main/GetThreatManager, Unit.Main/GetVictim, Unit.Main/HasUnitState, Unit.Main/SelectHostileTarget, Unit.Main/SetHealth, WorldObject.Object/GetDistance#3, WorldObject.Object/GetDistance2d#4, WorldObject.Object/GetMapId, WorldObject.Object/IsWithinDistInMap, WorldObject.Object/IsWithinLOSInMap | — | — |
| GetAI_npc_Balinda | ThreatListCopier.battleground_alterac | function | — | — | — |
| npc_GalvangarAI | ThreatListCopier.battleground_alterac | ctor | ScriptedAI/ScriptedAI | — | — |
| Aggro#12 | ThreatListCopier.battleground_alterac | method | ScriptMgr/DoScriptText | — | — |
| Reset#18 | ThreatListCopier.battleground_alterac | method | ScriptMgr/DoScriptText, shared_Util/urand, Unit.Main/ClearUnitState | — | — |
| MoveInLineOfSight#4 | ThreatListCopier.battleground_alterac | method | CreatureAI/AttackStart, Unit.Main/GetVictim, Unit.Main/IsHostileTo, WorldObject.Object/GetDistance#3 | — | — |
| UpdateAI#14 | ThreatListCopier.battleground_alterac | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, ScriptedAI/EnterEvadeMode, ScriptMgr/DoScriptText, shared_Util/urand, SpellCaster/IsNonMeleeSpellCasted, ThreatManager/getThreatList, Unit.Main/AddUnitState, Unit.Main/ClearUnitState, Unit.Main/CombatStop, Unit.Main/GetHealthPercent, Unit.Main/GetMaxHealth, Unit.Main/GetThreatManager, Unit.Main/GetVictim, Unit.Main/HasUnitState, Unit.Main/SelectHostileTarget, Unit.Main/SetHealth, WorldObject.Object/GetDistance2d#4, WorldObject.Object/GetMapId, WorldObject.Object/IsWithinDistInMap | — | — |
| GetAI_npc_Galvangar | ThreatListCopier.battleground_alterac | function | — | — | — |
| npc_WarMasterAI | ThreatListCopier.battleground_alterac | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#20 | ThreatListCopier.battleground_alterac | method | Unit.Main/ClearUnitState | — | — |
| MoveInLineOfSight#6 | ThreatListCopier.battleground_alterac | method | CreatureAI/AttackStart, CreatureAI/DoCastSpellIfCan, Unit.Main/GetVictim, WorldObject.Object/GetDistance#3, WorldObject.Object/IsValidAttackTarget | — | — |
| JustDied#4 | ThreatListCopier.battleground_alterac | method | — | — | — |
| UpdateAI#16 | ThreatListCopier.battleground_alterac | method | Creature.Main/GetVictimInRange, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/AddUnitState, Unit.Main/ClearUnitState, Unit.Main/GetHealthPercent, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, WorldObject.Object/DeleteLater | — | — |
| GetAI_npc_WarMaster | ThreatListCopier.battleground_alterac | function | — | — | — |
| npc_AlteracBowmanAI | ThreatListCopier.battleground_alterac | ctor | CreatureAI/SetCombatMovement, ScriptedAI/ScriptedAI, Unit.Main/AddAura | — | — |
| JustReachedHome#2 | ThreatListCopier.battleground_alterac | method | Unit.Main/AddAura | — | — |
| Reset#14 | ThreatListCopier.battleground_alterac | method | — | — | — |
| TargetWithinShootRange | ThreatListCopier.battleground_alterac | method | WorldObject.Object/GetDistance#3, WorldObject.Object/IsWithinLOSInMap | — | — |
| MoveInLineOfSight | ThreatListCopier.battleground_alterac | method | CreatureAI/AttackStart, Unit.Main/GetVictim, WorldObject.Object/IsValidAttackTarget | — | — |
| UpdateAI#10 | ThreatListCopier.battleground_alterac | method | Creature.Main/IsInEvadeMode, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, ScriptedAI/EnterEvadeMode, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAIInformation | ThreatListCopier.battleground_alterac | method | ChatHandler.Chat/PSendSysMessage, Creature.Main/IsInEvadeMode, CreatureAI/GetAIInformation, Unit.Main/GetVictim, WorldObject.Object/GetName | — | — |
| GetAI_npc_AlteracBowman | ThreatListCopier.battleground_alterac | function | — | — | — |
| npc_AlteracDardoshAI | ThreatListCopier.battleground_alterac | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#15 | ThreatListCopier.battleground_alterac | method | — | — | — |
| UpdateAI#11 | ThreatListCopier.battleground_alterac | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_npc_AlteracDardosh | ThreatListCopier.battleground_alterac | function | — | — | — |
| AV_NpcEventTroopsAI | ThreatListCopier.battleground_alterac | ctor | ScriptedEscortAI/npc_escortAI | — | — |
| Reset#5 | ThreatListCopier.battleground_alterac | method | Creature.Main/AI, Creature.Main/GetRespawnDelay, Object/GetEntry, ScriptedEscortAI/getCurrentWP, ScriptedEscortAI/HasEscortState, ScriptedEscortAI/setCurrentWP, ScriptedEscortAI/SetEscortPaused, ScriptedEscortAI/Start, Unit.Main/Mount | — | — |
| Aggro#3 | ThreatListCopier.battleground_alterac | method | Object/GetEntry, ScriptedEscortAI/SetEscortPaused, Unit.Main/Unmount | — | — |
| WaypointReached#2 | ThreatListCopier.battleground_alterac | method | — | — | — |
| UpdateEscortAI#2 | ThreatListCopier.battleground_alterac | method | Creature.Main/DisappearAndDie, CreatureAI/DoMeleeAttackIfReady, GridSearchers/GetCreatureListWithEntryInGrid#2, Object/GetEntry, Unit.Main/GetVictim, Unit.Main/IsDead, Unit.Main/SelectHostileTarget | — | — |
| npc_korrak_the_bloodragerAI | ThreatListCopier.battleground_alterac | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#22 | ThreatListCopier.battleground_alterac | method | — | — | — |
| UpdateAI#17 | ThreatListCopier.battleground_alterac | method | Creature.Main/DisappearAndDie, Creature.Main/SetRespawnTime, CreatureAI/DoMeleeAttackIfReady, ScriptMgr/DoScriptText, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| AV_NpcEventAI | ThreatListCopier.battleground_alterac | ctor | ScriptedEscortAI/npc_escortAI, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | — | — |
| checkTroopsStatus | ThreatListCopier.battleground_alterac | method | BattleGroundAV/getPlayerGoStatus, BattleGroundAV/getReinforcementLevelGroundUnit, BattleGroundAV/setPlayerGoStatus, BattleGroundMap/GetBG, WorldObject.Object/GetMap, WorldObject.Object/SummonCreature#2 | — | — |
| checkCavalryStatus | ThreatListCopier.battleground_alterac | method | BattleGroundAV/getPlayerGoStatus, BattleGroundAV/setPlayerGoStatus, BattleGroundMap/GetBG, WorldObject.Object/GetMap, WorldObject.Object/SummonCreature#2 | — | — |
| checkAerialStatus | ThreatListCopier.battleground_alterac | method | BattleGroundAV/getPlayerGoStatus, BattleGroundMap/GetBG, Creature.MotionMaster/MovePoint, CreatureAI/DoCastSpellIfCan, CreatureAI/IsCombatMovementEnabled, CreatureAI/SetCombatMovement, Unit.Main/AddAura, Unit.Main/GetMotionMaster, Unit.Main/SetDisplayId, Unit.Main/SetFly, Unit.Main/SetWalk, WorldObject.Object/GetMap, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/RemoveFlag, WorldObject.Object/SummonCreature#2 | — | — |
| JustRespawned | ThreatListCopier.battleground_alterac | method | Creature.Main/DisappearAndDie, Creature.Main/RemoveCorpse, Creature.Main/Respawn, Creature.Main/SetDeathState, Creature.Main/SetHomePosition, Creature.Main/SetRespawnDelay, Creature.MotionMaster/MovePoint, GridSearchers/GetCreatureListWithEntryInGrid#2, Object/GetEntry, ScriptedAI/DoTeleportTo#2, ScriptedEscortAI/JustRespawned, ScriptedEscortAI/Stop, Unit.Main/GetMotionMaster, Unit.Main/Unmount | — | — |
| Reset#4 | ThreatListCopier.battleground_alterac | method | Object/GetEntry, ScriptedEscortAI/SetEscortPaused, Unit.Main/Mount, Unit.Main/SetStandState, Unit.Main/SetWalk, WorldObject.Object/GetDistance#4 | — | — |
| Aggro#2 | ThreatListCopier.battleground_alterac | method | Object/GetEntry, ScriptedEscortAI/SetEscortPaused, SpellCaster/CastSpell#2, Unit.Main/Unmount | — | — |
| WaypointReached | ThreatListCopier.battleground_alterac | method | Creature.Main/SetHomePosition, GridSearchers/GetCreatureListWithEntryInGrid#2, Object/GetEntry, ScriptedEscortAI/SetEscortPaused, ScriptedEscortAI/Stop, ScriptMgr/DoScriptText, SpellCaster/CastSpell#2, Unit.Main/Mount, Unit.Main/SetWalk, Unit.Main/Unmount, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SetFlag, WorldObject.Object/SummonGameObject | — | — |
| JustDied | ThreatListCopier.battleground_alterac | method | BattleGroundAV/getReinforcementLevelGroundUnit, BattleGroundMap/GetBG, Creature.Main/AI, Creature.Main/SetRespawnDelay, GridSearchers/GetCreatureListWithEntryInGrid#2, Object/GetEntry, ScriptedEscortAI/getCurrentWP, ScriptedEscortAI/setCurrentWP, ScriptedEscortAI/Start, WorldObject.Object/GetMap | — | — |
| UpdateRenferalAI | ThreatListCopier.battleground_alterac | method | CreatureAI/DoCastSpellIfCan, shared_Util/urand, SpellCaster/CastSpell#2, Unit.Main/GetVictim | — | — |
| UpdateThurlogaAI | ThreatListCopier.battleground_alterac | method | CreatureAI/DoCastSpellIfCan, shared_Util/urand, SpellCaster/CastSpell#2, Unit.Main/GetHealthPercent, Unit.Main/GetVictim, WorldObject.Object/FindNearestCreature | — | — |
| UpdateEscortAI | ThreatListCopier.battleground_alterac | method | Creature.Main/DisappearAndDie, Creature.Main/JoinCreatureGroup, CreatureAI/DoMeleeAttackIfReady, GridSearchers/GetCreatureListWithEntryInGrid#2, Object/GetEntry, ScriptedEscortAI/SetEscortPaused, ScriptMgr/DoScriptText, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, Unit.Main/SetWalk, Unit.Main/Unmount, WorldObject.Object/FindNearestGameObject, WorldObject.Object/GetAngle, WorldObject.Object/GetDistance#3, WorldObject.Object/GetDistance#4, WorldObject.Object/GetOrientation | — | — |
| QuestComplete_npc_AVBlood_collector | ThreatListCopier.battleground_alterac | function | BattleGround/GetTypeID, BattleGroundAV/isWorldBossChallengeInvocationReady, BattleGroundAV/resetWorldBossChallengeInvocation, BattleGroundAV/setChallengeInvocationCounter, BattleGroundAV/setPlayerGoStatus, Creature.Main/AI, Log.Main/Out, Player.Main/GetBattleGround, Player.Main/GetTeam, QuestDef/GetQuestId, ScriptedEscortAI/Start, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag | — | — |
| GossipHello_npc_AVBlood_collector | ThreatListCopier.battleground_alterac | function | BattleGround/GetTypeID, BattleGroundAV/GetActualArmorRessources, BattleGroundAV/getChallengeInvocationCounter, BattleGroundAV/getChallengeInvocationGoals, BattleGroundAV/getMinReputationNeeded, BattleGroundAV/getReinforcementLevelGroundUnit, BattleGroundAV/isAerialChallengeInvocationReady, BattleGroundAV/isCavalryChallengeInvocationReady, BattleGroundAV/isGroundChallengeInvocationReady, BattleGroundMap/GetBG, Creature.Main/AI, GossipDef/AddMenuItem#5, GossipDef/SendGossipMenu, Object/GetEntry, Object/GetGUID, Object/GetObjectGuid, Object/HasFlag, ObjectGuid/ObjectGuid#5, ObjectMgr/GetCreatureQuestRelationsMapBounds, ObjectMgr/GetQuestTemplate, Player.Main/GetBattleGround, Player.Main/GetGossipTextId, Player.Main/GetReputationRank, Player.Main/GetTeam, Player.Main/PrepareQuestMenu, PlayerMenu/GetGossipMenu, QuestDef/GetQuestId, ScriptedEscortAI/Start, SpellCaster/CastSpell#2, Unit.Main/GetFactionTemplateId, Unit.Main/HandleEmote, Unit.Main/IsQuestGiver, Unit.Main/IsVendor, Unit.Main/SetFactionTemplateId, Unit.Main/SetWalk, WorldObject.Object/GetDistance#4, WorldObject.Object/GetMap, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag, WorldObject.Object/SetUInt32Value | — | — |
| AV_npc_troops_chief_EventAI | ThreatListCopier.battleground_alterac | ctor | ScriptedEscortAI/npc_escortAI | — | — |
| Reset#10 | ThreatListCopier.battleground_alterac | method | Creature.Main/GetCombatStartPosition, Creature.MotionMaster/MovePoint, ScriptedEscortAI/SetEscortPaused, Unit.Main/GetMotionMaster | — | — |
| Aggro#6 | ThreatListCopier.battleground_alterac | method | ScriptedEscortAI/SetEscortPaused | — | — |
| WaypointReached#5 | ThreatListCopier.battleground_alterac | method | Creature.Main/SetHomePosition, Object/GetEntry, ScriptedEscortAI/SetEscortPaused, ScriptedEscortAI/Stop, ScriptMgr/DoScriptText, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | — | — |
| JustDied#2 | ThreatListCopier.battleground_alterac | method | Creature.Main/AI, GridSearchers/GetCreatureListWithEntryInGrid#2, Object/GetEntry, ScriptedEscortAI/Start | — | — |
| UpdateEscortAI#5 | ThreatListCopier.battleground_alterac | method | Creature.Main/JoinCreatureGroup, CreatureAI/DoMeleeAttackIfReady, GridSearchers/GetCreatureListWithEntryInGrid#2, Object/GetEntry, ScriptedEscortAI/SetEscortPaused, ScriptMgr/DoScriptText, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, Unit.Main/SetWalk, WorldObject.Object/GetAngle, WorldObject.Object/GetDistance#3, WorldObject.Object/GetOrientation | — | — |
| QuestComplete_AV_npc_troops_chief | ThreatListCopier.battleground_alterac | function | BattleGround/GetTypeID, BattleGroundAV/resetGroundChallengeInvocation, BattleGroundAV/setPlayerGoStatus, Creature.Main/AI, Object/GetEntry, Player.Main/GetBattleGround, Player.Main/GetTeam, QuestDef/GetQuestId, ScriptedEscortAI/Start, Unit.Main/HandleEmote, Unit.Main/SetWalk, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag | — | — |
| GossipSelect_npc_AVBlood_collector | ThreatListCopier.battleground_alterac | function | BattleGround/GetTypeID, BattleGroundAV/resetAerialChallengeInvocation, BattleGroundAV/resetCavalryChallengeInvocation, BattleGroundAV/resetGroundChallengeInvocation, BattleGroundAV/setPlayerGoStatus, BattleGroundAV/UpgradeArmor, Creature.Main/AI, game_Objects_Item/GenerateItemRandomPropertyId, GossipDef/CloseGossip, GossipDef/SendGossipMenu, Object/GetEntry, Object/GetGUID, Object/HasFlag, ObjectGuid/ObjectGuid#5, Player.Main/CanStoreNewItem, Player.Main/GetBattleGround, Player.Main/GetSession, Player.Main/GetTeam, Player.Main/HasItemCount, Player.Main/SendNewItem, Player.Main/StoreNewItem, ScriptedEscortAI/Start, ScriptMgr/DoScriptText, Unit.Main/HandleEmote, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag, WorldSession.ItemHandler/SendListInventory | — | — |
| AV_WarRiderAI | ThreatListCopier.battleground_alterac | ctor | Creature.Main/SetWanderDistance, ScriptedAI/ScriptedAI, Unit.Main/SetCasterChaseDistance | — | — |
| Reset#8 | ThreatListCopier.battleground_alterac | method | Unit.Main/SetFly, Unit.Main/SetWalk | — | — |
| JustReachedHome | ThreatListCopier.battleground_alterac | method | Creature.MotionMaster/MoveRandom, Unit.Main/GetMotionMaster | — | — |
| UpdateAI#4 | ThreatListCopier.battleground_alterac | method | Creature.Main/SetHomePosition, Creature.MotionMaster/GetCurrentMovementGeneratorType, CreatureAI/AttackStart, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Object/GetEntry, ScriptedAI/EnterEvadeMode, Unit.Main/GetMotionMaster, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, Unit.Main/SelectNearestTarget, WorldObject.Object/IsWithinDistInMap | — | — |
| AV_BeaconInvocationObjectAI | ThreatListCopier.battleground_alterac | ctor | GameObject/SetOwnerGuid, GameObjectAI/GameObjectAI, Object/GetEntry, ObjectGuid/ObjectGuid, WorldObject.Object/SetUInt32Value | — | — |
| Reset | ThreatListCopier.battleground_alterac | method | — | — | — |
| UpdateAI | ThreatListCopier.battleground_alterac | method | WorldObject.Object/AddObjectToRemoveList, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SummonCreature#2 | — | — |
| OnUse | ThreatListCopier.battleground_alterac | method | WorldObject.Object/AddObjectToRemoveList | — | — |
| go_av_landmineAI | ThreatListCopier.battleground_alterac | ctor | GameObject/GetGOData, GameObjectAI/GameObjectAI, GameObjectData/GetRandomRespawnTime, Object/GetEntry | — | — |
| UpdateAI#9 | ThreatListCopier.battleground_alterac | method | BattleGround/IsActiveEvent, BattleGroundMap/GetBG, GameObject/isSpawned, GameObject/SetRespawnTime, Map.Main/IsBattleGround, shared_Util/urand, WorldObject.Object/GetMap | — | — |
| OnUse#2 | ThreatListCopier.battleground_alterac | method | GameObject/Despawn, GameObject/IsHostileTo | — | — |
| av_world_boss_baseai | ThreatListCopier.battleground_alterac | ctor | BattleGround/IsActiveEvent, BattleGroundMap/GetBG, game_Battlegrounds_BattleGround/SpawnEvent, Map.Main/IsBattleGround, ScriptedEscortAI/npc_escortAI, WorldObject.Object/DeleteLater, WorldObject.Object/GetMap | — | — |
| EnterEvadeMode | ThreatListCopier.battleground_alterac | method | Creature.Main/SetLootRecipient, ScriptedEscortAI/Reset, ScriptedEscortAI/ReturnToCombatStartPosition, Unit.Main/CombatStop, Unit.Main/DeleteThreatList, Unit.Main/GetSpellAuraHolderMap, Unit.Main/RemoveSpellAuraHolder, Unit.SpellAuras/IsPositive | — | — |
| JustDied#3 | ThreatListCopier.battleground_alterac | method | BattleGroundMap/GetBG, CreatureAI/JustDied, game_Battlegrounds_BattleGround/SpawnEvent, Map.Main/IsBattleGround, WorldObject.Object/GetMap | — | — |
| AV_NpcEventWorldBoss_H_AI | ThreatListCopier.battleground_alterac | ctor | GameObject/Delete, GridSearchers/GetCreatureListWithEntryInGrid#2, GridSearchers/GetGameObjectListWithEntryInGrid#2, Map.Main/GetPlayers, SpellCaster/InterruptNonMeleeSpells, Unit.Main/IsAlive, Unit.Main/RemoveAurasDueToSpell, WorldObject.Object/GetMap | — | — |
| Reset#7 | ThreatListCopier.battleground_alterac | method | Creature.Main/GetCombatStartPosition, Creature.MotionMaster/MovePoint, ScriptedEscortAI/SetEscortPaused, Unit.Main/GetMotionMaster, Unit.Main/SetWalk | — | — |
| Aggro#5 | ThreatListCopier.battleground_alterac | method | ScriptedEscortAI/SetEscortPaused | — | — |
| WaypointReached#4 | ThreatListCopier.battleground_alterac | method | Creature.Main/SetHomePosition, ScriptedEscortAI/Stop, ScriptMgr/DoScriptText, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | — | — |
| KilledUnit | ThreatListCopier.battleground_alterac | method | CreatureAI/DoCastSpellIfCan, Object/GetTypeId, ScriptMgr/DoScriptText | — | — |
| UpdateEscortAI#4 | ThreatListCopier.battleground_alterac | method | Creature.Main/SetDefaultMovementType, Creature.Main/SetHomePosition, Creature.Main/SetWanderDistance, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Map.Main/ScriptCommandStart, Object/GetObjectGuid, ScriptedEscortAI/Start, ScriptInfo/ScriptInfo, ScriptMgr/DoScriptText, shared_Util/urand, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, Unit.Main/SetWalk, WorldObject.Object/GetMap | — | — |
| AV_NpcEventWorldBoss_A_AI | ThreatListCopier.battleground_alterac | ctor | GameObject/Delete, GridSearchers/GetCreatureListWithEntryInGrid#2, GridSearchers/GetGameObjectListWithEntryInGrid#2, Map.Main/GetPlayers, SpellCaster/InterruptNonMeleeSpells, Unit.Main/IsAlive, Unit.Main/RemoveAurasDueToSpell, WorldObject.Object/GetMap | — | — |
| Reset#6 | ThreatListCopier.battleground_alterac | method | Creature.Main/GetCombatStartPosition, Creature.MotionMaster/MovePoint, ScriptedEscortAI/SetEscortPaused, Unit.Main/GetMotionMaster, Unit.Main/SetWalk | — | — |
| Aggro#4 | ThreatListCopier.battleground_alterac | method | ScriptedEscortAI/SetEscortPaused | — | — |
| WaypointReached#3 | ThreatListCopier.battleground_alterac | method | ScriptedEscortAI/Stop, ScriptMgr/DoScriptText | — | — |
| UpdateEscortAI#3 | ThreatListCopier.battleground_alterac | method | Creature.Main/SetDefaultMovementType, Creature.Main/SetHomePosition, Creature.Main/SetWanderDistance, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, ScriptedEscortAI/Start, ScriptMgr/DoScriptText, shared_Util/urand, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, Unit.Main/SetWalk | — | — |
| AV_CommanderAI | ThreatListCopier.battleground_alterac | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#2 | ThreatListCopier.battleground_alterac | method | — | — | — |
| UpdateAI#2 | ThreatListCopier.battleground_alterac | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Object/GetEntry, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| AV_DismountAI | ThreatListCopier.battleground_alterac | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#3 | ThreatListCopier.battleground_alterac | method | Object/GetEntry, Unit.Main/Mount | — | — |
| Aggro | ThreatListCopier.battleground_alterac | method | Unit.Main/Unmount | — | — |
| UpdateAI#3 | ThreatListCopier.battleground_alterac | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Object/GetEntry, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| FrostwolfShamanAI | ThreatListCopier.battleground_alterac | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#12 | ThreatListCopier.battleground_alterac | method | SpellCaster/CastSpell#2, Unit.Main/Mount | — | — |
| Aggro#8 | ThreatListCopier.battleground_alterac | method | SpellCaster/InterruptNonMeleeSpells, Unit.Main/HasAura#2, Unit.Main/IsMounted, Unit.Main/Unmount | — | — |
| UpdateAI#7 | ThreatListCopier.battleground_alterac | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, SpellCaster/CastSpell#2, Unit.Main/GetHealthPercent, Unit.Main/GetVictim, Unit.Main/HasAura#2, Unit.Main/SelectHostileTarget | — | — |
| DruidOfTheGroveAI | ThreatListCopier.battleground_alterac | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#11 | ThreatListCopier.battleground_alterac | method | SpellCaster/CastSpell#2, Unit.Main/Mount | — | — |
| Aggro#7 | ThreatListCopier.battleground_alterac | method | SpellCaster/InterruptNonMeleeSpells, Unit.Main/HasAura#2, Unit.Main/IsMounted, Unit.Main/Unmount | — | — |
| UpdateAI#6 | ThreatListCopier.battleground_alterac | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, SpellCaster/CastSpell#2, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_FrostwolfShamanAI | ThreatListCopier.battleground_alterac | function | — | — | — |
| GetAI_AV_DismountAI | ThreatListCopier.battleground_alterac | function | — | — | — |
| GetAI_AV_CommanderAI | ThreatListCopier.battleground_alterac | function | — | — | — |
| GetAI_npc_worldboss_A_AV | ThreatListCopier.battleground_alterac | function | — | — | — |
| GetAI_npc_worldboss_H_AV | ThreatListCopier.battleground_alterac | function | — | — | — |
| GetAI_npc_troops_chiefAV | ThreatListCopier.battleground_alterac | function | — | — | — |
| GetAI_npc_eventAV | ThreatListCopier.battleground_alterac | function | — | — | — |
| GetAI_npc_eventTroopsAV | ThreatListCopier.battleground_alterac | function | — | — | — |
| GetAI_npc_korrak_the_bloodrager | ThreatListCopier.battleground_alterac | function | — | — | — |
| GetAI_AV_WarRiderAI | ThreatListCopier.battleground_alterac | function | — | — | — |
| GetAI_AV_BeaconInvocationObjectAI | ThreatListCopier.battleground_alterac | function | — | — | — |
| GetAI_DruidOfTheGroveAI | ThreatListCopier.battleground_alterac | function | — | — | — |
| GetAI_go_av_landmine | ThreatListCopier.battleground_alterac | function | — | — | — |
| MineNPC_AI | ThreatListCopier.battleground_alterac | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#13 | ThreatListCopier.battleground_alterac | method | — | — | — |
| JustRespawned#3 | ThreatListCopier.battleground_alterac | method | BattleGround/ActivateEventWithoutSpawn, BattleGroundMap/GetBG, Map.Main/IsBattleGround, Object/GetEntry, WorldObject.Object/GetMap | — | — |
| Aggro#9 | ThreatListCopier.battleground_alterac | method | Creature.Main/SetInCombatWithZone | — | — |
| UpdateAI#8 | ThreatListCopier.battleground_alterac | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Map.Main/GetPlayers, MapRefManager/begin#2, MapRefManager/end#2, Unit.Main/GetPet, Unit.Main/GetVictim, Unit.Main/IsNoWeaponShapeShift, Unit.Main/SelectHostileTarget, WorldObject.Object/GetDistance2d#3, WorldObject.Object/GetMap | — | — |
| GetAI_AV_MineNPC_AI | ThreatListCopier.battleground_alterac | function | — | — | — |
| AV_mineNpcAI | ThreatListCopier.battleground_alterac | ctor | Object/GetEntry, ScriptedAI/ScriptedAI, WorldObject.Object/GetPosition#2 | — | — |
| Reset#9 | ThreatListCopier.battleground_alterac | method | — | — | — |
| JustRespawned#2 | ThreatListCopier.battleground_alterac | method | Creature.Main/UpdateEntry, Object/GetEntry | — | — |
| SelectCreatureEntry | ThreatListCopier.battleground_alterac | method | BattleGroundAV/getReinforcementLevelGroundUnit, BattleGroundMap/GetBG, Map.Main/IsBattleGround, Unit.Main/GetFactionTemplateId, WorldObject.Object/GetMap | — | — |
| UpdateAI#5 | ThreatListCopier.battleground_alterac | method | Creature.Main/UpdateEntry, CreatureAI/DoMeleeAttackIfReady, Object/GetEntry, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_AV_Mines_AI | ThreatListCopier.battleground_alterac | function | BasicAI/BasicAI, WorldObject.Object/GetMapId | — | — |
| npc_av_trigger_for_questAI | ThreatListCopier.battleground_alterac | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#21 | ThreatListCopier.battleground_alterac | method | Creature.Main/EnableMoveInLosEvent | — | — |
| MoveInLineOfSight#7 | ThreatListCopier.battleground_alterac | method | Object/GetEntry, Object/GetObjectGuid, Object/IsPlayer, Object/ToPlayer, Player.Main/KilledMonsterCredit, WorldObject.Object/IsWithinDist | — | — |
| GetAI_npc_av_trigger_for_quest | ThreatListCopier.battleground_alterac | function | — | — | — |
| OnSummon | ThreatListCopier.battleground_alterac | method | Object/GetObjectGuid, Unit.Main/SetCreatorGuid, WorldObject.Object/SetUInt32Value | — | — |
| GetScript_AVCreateShredder | ThreatListCopier.battleground_alterac | function | — | — | — |
| AddSC_bg_alterac | ThreatListCopier.battleground_alterac | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
| ThreatListCopier | ThreatListCopier.boss_ragnaros | ctor | — | — | — |
| Process | ThreatListCopier.boss_ragnaros | method | CreatureAI/AttackStart, Unit.Main/AI | — | — |
| boss_ragnarosAI | ThreatListCopier.boss_ragnaros | ctor | CreatureAI/SetCombatMovement, ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset | ThreatListCopier.boss_ragnaros | method | InstanceData/SetData, shared_Util/urand, Unit.Main/IsAlive | — | — |
| Aggro | ThreatListCopier.boss_ragnaros | method | Creature.Main/SetInCombatWithZone, CreatureAI/DoCastSpellIfCan, InstanceData/SetData, Object/GetEntry, Object/GetTypeId, WorldObject.Object/RemoveFlag | — | — |
| SpellHitTarget | ThreatListCopier.boss_ragnaros | method | Object/GetEntry, Object/GetTypeId | — | — |
| JustDied | ThreatListCopier.boss_ragnaros | method | InstanceData/SetData | — | — |
| KilledUnit | ThreatListCopier.boss_ragnaros | method | Object/GetEntry, ScriptMgr/DoScriptText | — | — |
| SummonSonsOfFlame | ThreatListCopier.boss_ragnaros | method | Creature.Main/AI, Creature.Main/ProcessThreatList, Creature.Main/SelectAttackingTarget, Creature.MotionMaster/MoveChase, CreatureAI/AttackStart, ThreatManager/modifyThreatPercent#2, Unit.Main/GetMotionMaster, Unit.Main/GetThreatManager, WorldObject.Object/SummonCreature#2 | — | — |
| UpdateLavaBurstAI | ThreatListCopier.boss_ragnaros | method | shared_Util/urand | — | — |
| DoLavaBurst | ThreatListCopier.boss_ragnaros | method | GameObject/Use, shared_Util/frand, shared_Util/urand, WorldObject.Object/SummonGameObject | — | — |
| UpdateAI | ThreatListCopier.boss_ragnaros | method | Creature.Main/ForcedDespawn, Creature.Main/SelectAttackingTarget, Creature.Main/SetInCombatWithZone, CreatureAI/DoCastSpellIfCan, GridSearchers/GetCreatureListWithEntryInGrid#2, Log.Main/Out, Object/GetObjectGuid, Object/HasFlag, Object/ToPlayer, Player.Main/IsGameMaster, ScriptedAI/DoResetThreat, ScriptMgr/DoScriptText, shared_Util/urand, ThreatManager/getThreatList, Unit.Main/AddAura, Unit.Main/GetPowerType, Unit.Main/GetThreatManager, Unit.Main/GetVictim, Unit.Main/HandleEmote, Unit.Main/HasAura#2, Unit.Main/HasUnitState, Unit.Main/IsAlive, Unit.Main/RemoveAurasDueToSpell, Unit.Main/SelectHostileTarget, Unit.Main/SetFacingToObject, Unit.Main/SetInFront, Unit.Main/SetStandState, Unit.Main/SetTargetGuid, WorldObject.Object/FindNearestCreature, WorldObject.Object/RemoveFlag | — | — |
| CheckForMelee | ThreatListCopier.boss_ragnaros | method | Creature.Main/SelectAttackingTarget, Object/IsPlayer, Object/ToPlayer, Player.Main/IsGameMaster, SpellCaster/IsNonMeleeSpellCasted, ThreatManager/modifyThreatPercent#2, Unit.Main/AttackerStateUpdate, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/GetThreatManager, Unit.Main/GetVictim, Unit.Main/IsAttackReady, Unit.Main/ResetAttackTimer, WorldObject.Object/IsWithinLOSInMap | — | — |
| GetAI_boss_ragnaros | ThreatListCopier.boss_ragnaros | function | — | — | — |
| AddSC_boss_ragnaros | ThreatListCopier.boss_ragnaros | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
