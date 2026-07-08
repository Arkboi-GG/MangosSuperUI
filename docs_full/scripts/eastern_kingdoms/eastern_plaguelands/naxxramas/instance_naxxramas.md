# instance_naxxramas — Class Overview

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# instance_naxxramas

`instance_naxxramas` is the `ScriptedInstance` implementation for the Naxxramas raid, managing the persistent state of the instance, boss encounter progression, and environmental mechanics such as doors, gates, and teleporters. It serves as the central authority for the raid's logic, coordinating interactions between individual boss AIs, trash mobs, and the physical layout of the instance.

## How the class is split

The class is implemented across two primary partials, each handling distinct aspects of the raid's functionality:

*   **`instance_naxxramas.Main`**: This partial contains the core `ScriptedInstance` logic. It manages the `m_auiEncounter` array that tracks the state of every boss (NOT_STARTED, IN_PROGRESS, DONE, FAIL, SPECIAL). It handles the initialization of the instance, loading/saving state, and the complex logic for doors, gates, and teleporters. It also includes AI implementations for several trash mobs (Spirit of Naxxramas, Gargoyles, Plague Slimes, Toxic Tunnels, Dark Touched Warriors), gossip scripts for Master Craftsman Omarion, and specific helper methods for bosses like Gothik and Kel'Thuzad.
*   **`instance_naxxramas.boss_kelthuzad`**: This partial is dedicated entirely to the final boss, Kel'Thuzad. It implements the AI for Kel'Thuzad himself, his summoned minions (Frozen Soldiers, Unstoppable Abominations, Soul Weavers, Guardians of Icecrown, and Shadow Fissures), and specific spell behaviors associated with the encounter. It also contains a few helper methods for the instance script related to Kel'Thuzad, such as `OnKTAreaTrigger` and `GetChamberCenterCoords`.

## How the partials collaborate

The collaboration between the partials is centered around the `instance_naxxramas` object, which acts as the shared state container.

*   **State Management**: The `instance_naxxramas.Main` partial provides `SetData` and `GetData` methods, which are called by the `boss_kelthuzad` partial (and other boss scripts) to update and query the encounter state. For example, when Kel'Thuzad dies, his AI calls `SetData` to mark the encounter as DONE.
*   **Environmental Interaction**: The `boss_kelthuzad` partial calls methods like `ToggleKelThuzadWindows` and `DoUseDoorOrButton` on the `instance_naxxramas` object to control visual effects and physical barriers during the encounter.
*   **Encounter Initiation**: The `instance_naxxramas.Main` partial handles the area trigger for Kel'Thuzad (`onNaxxramasAreaTrigger`), which delegates to `OnKTAreaTrigger` in the `boss_kelthuzad` partial to start the encounter if it hasn't already begun.
*   **Shared Utilities**: Both partials utilize common utilities like `EventMap` for timed events, `ScriptMgr` for speech and script registration, and `shared_Util` for random number generation.

## Data model

This class does not directly query or modify database tables. It relies on the core's `ScriptedInstance` infrastructure to save and load instance data via the `Save()` and `Load()` methods. These methods serialize the `m_auiEncounter` array and other member variables to/from a string stored in the core's instance data system, which is typically backed by the `instance` table in the database. The AI logic and trash mob behaviors are entirely runtime-based.

## Where to go deeper

*   **`instance_naxxramas.Main`**: Open this doc to understand the overall instance state management, door/gate/teleporter logic, trash mob AIs, gossip scripts, and helper methods for various bosses.
*   **`instance_naxxramas.boss_kelthuzad`**: Open this doc for detailed information on Kel'Thuzad's multi-phase encounter, his minion AIs, and specific spell mechanics like Chains of Kel'Thuzad and Void Blast.

---

<!-- machine-true, projected from graph.json -->

## Map — instance_naxxramas

*Source:* boss_kelthuzad.cpp, naxxramas.h, instance_naxxramas.cpp

| Member | Partial | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|---|
| kt_p1AddAI | instance_naxxramas.boss_kelthuzad | ctor | Creature.Main/SetNoSearchAssistance, ScriptedAI/ScriptedAI | — | — |
| ActualAttack | instance_naxxramas.boss_kelthuzad | method | CreatureAI/AttackStart, Unit.Main/AddThreat | — | — |
| Aggro#2 | instance_naxxramas.boss_kelthuzad | method | — | — | — |
| AttackStart#2 | instance_naxxramas.boss_kelthuzad | method | CreatureAI/AttackStart, WorldObject.Object/GetDistance2d#3 | — | — |
| MoveInLineOfSight#2 | instance_naxxramas.boss_kelthuzad | method | BasicAI/MoveInLineOfSight, Unit.Main/IsHostileTo, WorldObject.Object/GetDistance2d#3 | — | — |
| SpellHit | instance_naxxramas.boss_kelthuzad | method | Object/ToUnit | — | — |
| boss_kelthuzadAI | instance_naxxramas.boss_kelthuzad | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData, WorldObject.Object/SetCreatureSummonLimit | — | — |
| Reset | instance_naxxramas.boss_kelthuzad | method | EventMap/Reset, Object/GetObjectGuid, ObjectGuid/operator!, Unit.Main/GetMaxHealth, Unit.Main/RemoveAurasDueToSpell, Unit.Main/SetHealth, WorldObject.Object/SetFlag, WorldObject.Object/SetObjectScale, WorldObject.Object/SummonGameObject, ZoneScript/GetMap#2 | — | — |
| ~instance_naxxramas | instance_naxxramas.boss_kelthuzad | dtor | — | — | — |
| Save | instance_naxxramas.boss_kelthuzad | method | — | — | — |
| GetChamberCenterCoords | instance_naxxramas.boss_kelthuzad | method | — | — | — |
| KilledUnit | instance_naxxramas.boss_kelthuzad | method | ScriptMgr/DoScriptText, shared_Util/urand | — | — |
| JustDied | instance_naxxramas.boss_kelthuzad | method | instance_naxxramas.Main/SetData, ScriptMgr/DoScriptText | — | — |
| MoveInLineOfSight | instance_naxxramas.boss_kelthuzad | method | — | — | — |
| AttackStart | instance_naxxramas.boss_kelthuzad | method | CreatureAI/AttackStart, Object/HasFlag | — | — |
| Aggro | instance_naxxramas.boss_kelthuzad | method | Creature.Main/SetInCombatWithZone, Object/HasFlag | — | — |
| CheckForEnemyPlayers | instance_naxxramas.boss_kelthuzad | method | Player.Main/IsGameMaster, Unit.Main/AddThreat, Unit.Main/SetInCombatWith, WorldObject.Object/GetAlivePlayerListInRange | — | — |
| JustReachedHome | instance_naxxramas.boss_kelthuzad | method | instance_naxxramas.Main/SetData, instance_naxxramas.Main/ToggleKelThuzadWindows | — | — |
| EvadeAllGuardians | instance_naxxramas.boss_kelthuzad | method | Creature.Main/AI, CreatureAI/EnterEvadeMode, ZoneScript/GetCreature | — | — |
| DespawnAllIntroCreatures | instance_naxxramas.boss_kelthuzad | method | Map.Main/GetCreature, TemporarySummon/UnSummon | — | — |
| StartEncounter | instance_naxxramas.boss_kelthuzad | method | Creature.Main/SetHomePosition, Creature.Main/SetWanderDistance, CreatureAI/DoCastAOE, EventMap/ScheduleEvent#2, EventMap/ScheduleEvent#3, instance_naxxramas.Main/SetData, instance_naxxramas.Main/ToggleKelThuzadWindows, Object/GetObjectGuid, ScriptedInstance/DoUseDoorOrButton, ScriptMgr/DoScriptText, shared_Util/frand, shared_Util/rand_norm, Unit.Main/GetMaxHealth, Unit.Main/SetHealth, WorldObject.Object/GetOrientation, WorldObject.Object/SummonCreature#2 | — | — |
| SpawnAndSendP1Creature | instance_naxxramas.boss_kelthuzad | method | Creature.Main/AI, Creature.Main/SelectAttackingTarget, Creature.Main/SetInCombatWithZone, shared_Util/urand, WorldObject.Object/GetAngle#2, WorldObject.Object/SummonCreature#2 | — | — |
| UpdateP1 | instance_naxxramas.boss_kelthuzad | method | Creature.Main/SelectAttackingTarget, Creature.Main/SetInCombatWithZone, EventMap/ExecuteEvent, EventMap/Repeat#3, EventMap/Reset, EventMap/ScheduleEvent#2, GameObject/Delete, instance_naxxramas.Main/GetData, Log.Main/Out, ObjectGuid/ObjectGuid#5, ScriptedAI/DoResetThreat, ScriptMgr/DoScriptText, shared_Util/urand, SpellCaster/InterruptNonMeleeSpells, Unit.Main/RemoveAurasDueToSpell, Unit.Main/SetInCombatState, WorldObject.Object/RemoveFlag, ZoneScript/GetGameObject | — | — |
| DoChains | instance_naxxramas.boss_kelthuzad | method | CreatureAI/DoCastSpellIfCan, EventMap/Repeat, ScriptedAI/DoResetThreat, ScriptMgr/DoScriptText, shared_Util/urand | — | — |
| UpdateP2P3 | instance_naxxramas.boss_kelthuzad | method | Creature.Main/AI, Creature.Main/SelectAttackingTarget, Creature.Main/SelectAttackingTarget#2, Creature.Main/SetInCombatWithZone, CreatureAI/AttackStart, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, EventMap/ExecuteEvent, EventMap/Repeat, EventMap/Repeat#3, EventMap/ScheduleEvent#2, instance_naxxramas.Main/ToggleKelThuzadWindows, Object/GetObjectGuid, ScriptedInstance/DoOrSimulateScriptTextForThisInstance, ScriptMgr/DoScriptText, shared_Util/urand, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/GetHealthPercent, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, WorldObject.Object/SummonCreature#2 | — | — |
| UpdateAI | instance_naxxramas.boss_kelthuzad | method | EventMap/Update, instance_naxxramas.Main/GetData, instance_naxxramas.Main/HandleEvadeOutOfHome, Object/HasFlag, SpellCaster/CastSpell#2, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| mob_abomAI | instance_naxxramas.boss_kelthuzad | ctor | — | — | — |
| Reset#2 | instance_naxxramas.boss_kelthuzad | method | Unit.Main/SetHealth, Unit.Main/SetMaxHealth | — | — |
| UpdateAI#2 | instance_naxxramas.boss_kelthuzad | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| mob_soldierAI | instance_naxxramas.boss_kelthuzad | ctor | — | — | — |
| Reset#5 | instance_naxxramas.boss_kelthuzad | method | Unit.Main/SetHealth, Unit.Main/SetMaxHealth | — | — |
| UpdateAI#5 | instance_naxxramas.boss_kelthuzad | method | Creature.Main/SelectAttackingTarget, CreatureAI/AttackStart, CreatureAI/DoMeleeAttackIfReady, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| mob_soulweaverAI | instance_naxxramas.boss_kelthuzad | ctor | — | — | — |
| Reset#6 | instance_naxxramas.boss_kelthuzad | method | Unit.Main/SetHealth, Unit.Main/SetMaxHealth | — | — |
| UpdateAI#6 | instance_naxxramas.boss_kelthuzad | method | Creature.Main/SelectAttackingTarget, CreatureAI/AttackStart, CreatureAI/DoMeleeAttackIfReady, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| mob_guardian_icecrownAI | instance_naxxramas.boss_kelthuzad | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset#3 | instance_naxxramas.boss_kelthuzad | method | — | — | — |
| JustReachedHome#2 | instance_naxxramas.boss_kelthuzad | method | WorldObject.Object/DeleteLater | — | — |
| DispellShackle | instance_naxxramas.boss_kelthuzad | method | Unit.Main/HasAura#2, Unit.Main/RemoveAurasDueToSpell | — | — |
| SpellHit#2 | instance_naxxramas.boss_kelthuzad | method | GridSearchers/GetCreatureListWithEntryInGrid#2, ScriptedInstance/GetSingleCreatureFromStorage, ScriptMgr/DoScriptText, Unit.Main/HasAura#2 | — | — |
| UpdateAI#3 | instance_naxxramas.boss_kelthuzad | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| mob_shadow_fissureAI | instance_naxxramas.boss_kelthuzad | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#4 | instance_naxxramas.boss_kelthuzad | method | — | — | — |
| Aggro#3 | instance_naxxramas.boss_kelthuzad | method | — | — | — |
| AttackStart#3 | instance_naxxramas.boss_kelthuzad | method | — | — | — |
| MoveInLineOfSight#3 | instance_naxxramas.boss_kelthuzad | method | — | — | — |
| UpdateAI#4 | instance_naxxramas.boss_kelthuzad | method | Creature.Main/ForcedDespawn, SpellCaster/CastSpell#2 | — | — |
| GetAI_boss_kelthuzad | instance_naxxramas.boss_kelthuzad | function | — | — | — |
| GetAI_mob_abom | instance_naxxramas.boss_kelthuzad | function | — | — | — |
| GetAI_mob_soldier | instance_naxxramas.boss_kelthuzad | function | — | — | — |
| GetAI_mob_soulweaver | instance_naxxramas.boss_kelthuzad | function | — | — | — |
| GetAI_mob_guardian_icecrown | instance_naxxramas.boss_kelthuzad | function | — | — | — |
| GetAI_mob_shadow_fissure | instance_naxxramas.boss_kelthuzad | function | — | — | — |
| OnKTAreaTrigger | instance_naxxramas.boss_kelthuzad | method | Creature.Main/AI, instance_naxxramas.Main/GetData, ScriptedInstance/GetSingleCreatureFromStorage | instance_naxxramas.Main/onNaxxramasAreaTrigger | — |
| OnEffectExecute | instance_naxxramas.boss_kelthuzad | method | Spell.Main/GetUnitTarget, Unit.Main/HasAura#2 | — | — |
| GetScript_KelThuzadVoidBlast | instance_naxxramas.boss_kelthuzad | function | — | — | — |
| OnAfterApply | instance_naxxramas.boss_kelthuzad | method | Aura/GetEffIndex, Aura/GetTarget, Object/ToPlayer | — | — |
| GetScript_ChainsOfKelThuzad | instance_naxxramas.boss_kelthuzad | function | — | — | — |
| AddSC_boss_kelthuzad | instance_naxxramas.boss_kelthuzad | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
| instance_naxxramas | instance_naxxramas.Main | ctor | ScriptedInstance/ScriptedInstance | — | — |
| Initialize | instance_naxxramas.Main | method | EventMap/Reset, EventMap/ScheduleEvent#2, EventMap/ScheduleEvent#3, shared_Util/urand | — | — |
| SetTeleporterVisualState | instance_naxxramas.Main | method | GameObject/SetGoState | — | — |
| SetTeleporterState | instance_naxxramas.Main | method | WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag | — | — |
| GetNumEndbossDead | instance_naxxramas.Main | method | — | — | — |
| HandleEvadeOutOfHome | instance_naxxramas.Main | method | Creature.Main/AI, Creature.Main/GetHomePosition#2, Creature.Main/IsInEvadeMode, CreatureAI/EnterEvadeMode, Log.Main/Out, Object/GetEntry, ScriptedInstance/GetSingleCreatureFromStorage, Unit.Main/IsAlive, WorldObject.Object/GetDistance2d, WorldObject.Object/GetPosition#3, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | boss_anubrekhan/UpdateAI, boss_faerlina/UpdateAI, boss_four_horsemen/UpdateAI#2, boss_four_horsemen/UpdateAI#3, boss_four_horsemen/UpdateAI#4, boss_four_horsemen/UpdateAI#5, boss_gothik/UpdateAI, boss_grobbulus/UpdateAI, boss_heigan/UpdateAI, boss_loatheb/UpdateAI, boss_noth/UpdateAI, boss_razuvious/UpdateAI, instance_naxxramas.boss_kelthuzad/UpdateAI | — |
| OnCreatureEnterCombat | instance_naxxramas.Main | method | GridSearchers/GetCreatureListWithEntryInGrid#2, Object/GetEntry, SpellCaster/CastSpell#2, Unit.Main/IsInCombat | — | — |
| WingsAreCleared | instance_naxxramas.Main | method | — | — | — |
| UpdateAutomaticBossEntranceDoor | instance_naxxramas.Main | method | ScriptedInstance/GetSingleGameObjectFromStorage | boss_heigan/JustDied, boss_heigan/JustReachedHome, boss_heigan/UpdateAI | — |
| UpdateAutomaticBossEntranceDoor#2 | instance_naxxramas.Main | method | GameObject/SetGoState, Log.Main/Out, WorldObject.Object/SetFlag | — | — |
| UpdateManualDoor | instance_naxxramas.Main | method | ScriptedInstance/GetSingleGameObjectFromStorage | — | — |
| UpdateManualDoor#2 | instance_naxxramas.Main | method | WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag | — | — |
| UpdateBossGate | instance_naxxramas.Main | method | ScriptedInstance/GetSingleGameObjectFromStorage | — | — |
| UpdateBossGate#2 | instance_naxxramas.Main | method | GameObject/SetGoState, Log.Main/Out | — | — |
| UpdateTeleporters | instance_naxxramas.Main | method | GameObject/SetGoState, Log.Main/Out, ScriptedInstance/GetSingleGameObjectFromStorage | — | — |
| OnCreatureCreate | instance_naxxramas.Main | method | Creature.Main/ForcedDespawn, Creature.Main/Respawn, Creature.Main/SetWanderDistance, GridSearchers/GetCreatureListWithEntryInGrid#2, Object/GetEntry, Object/GetGUID, Object/GetObjectGuid, Unit.Main/IsDead | — | — |
| OnObjectCreate | instance_naxxramas.Main | method | GameObject/GetDBTableGUIDLow, GameObject/GetGoType, GameObject/SetGoState, Object/GetEntry, Object/GetGUID, Object/GetObjectGuid, WorldObject.Object/DeleteLater, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag, WorldObject.Object/SummonCreature#2 | — | — |
| OnCreatureRespawn | instance_naxxramas.Main | method | Creature.Main/GetDBTableGUIDLow, Object/GetEntry, WorldObject.Object/AddObjectToRemoveList | — | — |
| IsEncounterInProgress | instance_naxxramas.Main | method | — | — | — |
| SetData | instance_naxxramas.Main | method | Creature.Main/GetCombatTime, Creature.Main/Respawn, Errors/PrintStacktraceAndThrow, EventMap/ScheduleEvent#2, EventMap/ScheduleEvent#3, GameObject/SetGoState, GridSearchers/GetCreatureListWithEntryInGrid, InstanceData/SaveToDB, InstanceStatistics/IncrementWipeCounter, LinkedListHead/isEmpty, Log.Main/Out, Map.Main/GetId, Map.Main/GetInstanceId, Map.Main/GetMapName, Map.Main/GetPlayers, ObjectMgr/GetFactionEntry, Player.Main/GetReputationMgr, ReputationMgr/ModifyReputation, ScriptedInstance/DoRespawnGameObject, ScriptedInstance/GetSingleCreatureFromStorage, ScriptedInstance/GetSingleGameObjectFromStorage, Unit.Main/IsDead, WorldObject.Object/DeleteLater, WorldObject.Object/IsWithinDist2d, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag, ZoneScript/GetMap#2 | boss_anubrekhan/Aggro, boss_anubrekhan/JustDied, boss_anubrekhan/JustReachedHome, boss_faerlina/Aggro, boss_faerlina/JustDied, boss_faerlina/JustReachedHome, boss_four_horsemen/Aggro, boss_four_horsemen/JustDied, boss_four_horsemen/JustReachedHome, boss_gluth/Aggro, boss_gluth/JustDied, boss_gluth/JustReachedHome, boss_gothik/Aggro, boss_gothik/JustDied, boss_gothik/JustReachedHome, boss_grobbulus/Aggro, boss_grobbulus/JustDied, boss_grobbulus/JustReachedHome, boss_heigan/Aggro, boss_heigan/JustDied, boss_heigan/JustReachedHome, boss_loatheb/Aggro, boss_loatheb/JustDied, boss_loatheb/JustReachedHome, boss_maexxna/Aggro, boss_maexxna/JustDied, boss_maexxna/JustReachedHome, boss_noth/Aggro, boss_noth/JustDied, boss_noth/JustReachedHome, boss_patchwerk/Aggro, boss_patchwerk/JustDied, boss_patchwerk/JustReachedHome, boss_razuvious/Aggro, boss_razuvious/JustDied, boss_razuvious/JustReachedHome, boss_sapphiron/Aggro, boss_sapphiron/JustDied, boss_sapphiron/OnUse, boss_sapphiron/Reset, boss_thaddius/Aggro#4, boss_thaddius/DamageTaken, boss_thaddius/JustDied, boss_thaddius/JustDied#2, boss_thaddius/JustDied#3, boss_thaddius/JustReachedHome, boss_thaddius/JustReachedHome#2, instance_naxxramas.boss_kelthuzad/JustDied, instance_naxxramas.boss_kelthuzad/JustReachedHome, instance_naxxramas.boss_kelthuzad/StartEncounter | — |
| Load | instance_naxxramas.Main | method | Log.Main/Out, Map.Main/GetId, Map.Main/GetInstanceId, Map.Main/GetMapName | — | — |
| GetData | instance_naxxramas.Main | method | Log.Main/Out | boss_anubrekhan/CheckSpawnInitialCryptGuards, boss_four_horsemen/Aggro, boss_four_horsemen/AggroRadius, boss_razuvious/RespawnAdds, boss_sapphiron/OnUse, boss_sapphiron/Reset, boss_thaddius/CheckSpawnAdds, boss_thaddius/JustDied, boss_thaddius/JustDied#2, boss_thaddius/UpdateAI, instance_naxxramas.boss_kelthuzad/OnKTAreaTrigger, instance_naxxramas.boss_kelthuzad/UpdateAI, instance_naxxramas.boss_kelthuzad/UpdateP1 | — |
| GetData64 | instance_naxxramas.Main | method | Log.Main/Out | — | — |
| GetGOUuid | instance_naxxramas.Main | method | Log.Main/Out | — | — |
| SetGothTriggers | instance_naxxramas.Main | method | Map.Main/GetCreature, Object/GetGUID, ObjectGuid/ObjectGuid#5, ScriptedInstance/GetSingleCreatureFromStorage, WorldObject.Object/GetPositionZ | boss_gothik/Aggro | — |
| GetClosestAnchorForGoth | instance_naxxramas.Main | method | Map.Main/GetCreature, ObjectDistanceOrder/ObjectDistanceOrder, ObjectGuid/ObjectGuid#5 | boss_gothik/EffectDummyCreature_spell_anchor, boss_gothik/SummonedCreatureJustDied | — |
| GetGothSummonPointCreatures | instance_naxxramas.Main | method | Map.Main/GetCreature, ObjectGuid/ObjectGuid#5 | boss_gothik/EffectDummyCreature_spell_anchor, boss_gothik/SummonAdds | — |
| IsInRightSideGothArea | instance_naxxramas.Main | method | Log.Main/Out, ScriptedInstance/GetSingleGameObjectFromStorage, WorldObject.Object/GetPositionY | boss_gothik/SummonAdd, boss_gothik/UpdateAI | — |
| SetChamberCenterCoords | instance_naxxramas.Main | method | — | — | — |
| ToggleKelThuzadWindows | instance_naxxramas.Main | method | GameObject/SetGoState, ScriptedInstance/GetSingleGameObjectFromStorage | instance_naxxramas.boss_kelthuzad/JustReachedHome, instance_naxxramas.boss_kelthuzad/StartEncounter, instance_naxxramas.boss_kelthuzad/UpdateP2P3 | — |
| OnPlayerDeath | instance_naxxramas.Main | method | Creature.Main/AI, Creature.Main/SelectAttackingTarget, Creature.Main/SetInCombatWithZone, CreatureAI/AttackStart, ScriptedInstance/GetSingleCreatureFromStorage, Unit.Main/AddThreat, Unit.Main/SendSpellGo, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SummonCreature#2 | — | — |
| OnCreatureDeath | instance_naxxramas.Main | method | Creature.Main/ForcedDespawn, EventMap/ScheduleEvent#3, InstanceStatistics/IncrementCustomCounter, Object/GetEntry, WorldObject.Object/DeleteLater | — | — |
| Update | instance_naxxramas.Main | method | Creature.MotionMaster/MovePoint, EventMap/ExecuteEvent, EventMap/Repeat, EventMap/ScheduleEvent#2, EventMap/ScheduleEvent#3, EventMap/Update, ScriptedInstance/DoOrSimulateScriptTextForThisInstance, ScriptedInstance/GetPlayerInMap, ScriptedInstance/GetSingleCreatureFromStorage, ScriptMgr/DoOrSimulateScriptTextForMap, shared_Util/urand, Unit.Main/GetMotionMaster, WorldObject.Object/SummonCreature, WorldObject.Object/SummonCreature#2, ZoneScript/GetMap#2 | — | — |
| GetInstanceData_instance_naxxramas | instance_naxxramas.Main | function | — | — | — |
| onNaxxramasAreaTrigger | instance_naxxramas.Main | method | EventMap/ScheduleEvent#3, instance_naxxramas.boss_kelthuzad/OnKTAreaTrigger, Player.Main/IsGameMaster, ScriptedInstance/GetSingleCreatureFromStorage, ScriptMgr/DoOrSimulateScriptTextForMap, ScriptMgr/DoScriptText, Unit.Main/IsAlive, ZoneScript/GetMap#2 | — | — |
| AreaTrigger_at_naxxramas | instance_naxxramas.Main | function | Player.Main/IsGameMaster, Unit.Main/IsAlive, WorldObject.Object/GetInstanceData | — | — |
| mob_spiritOfNaxxramasAI | instance_naxxramas.Main | ctor | ScriptedAI/ScriptedAI, SpellCaster/CastSpell#2 | — | — |
| DespawnPortal | instance_naxxramas.Main | method | Map.Main/GetCreature, ObjectGuid/ObjectGuid#5, ObjectGuid/operator!, TemporarySummon/UnSummon, WorldObject.Object/GetMap | — | — |
| Reset#4 | instance_naxxramas.Main | method | — | — | — |
| JustDied | instance_naxxramas.Main | method | — | — | — |
| UpdateAI#4 | instance_naxxramas.Main | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Object/GetObjectGuid, SpellCaster/CastSpell#2, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, Unit.Main/SendSpellGo, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SummonCreature#2 | — | — |
| mob_naxxramasGarboyleAI | instance_naxxramas.Main | ctor | Creature.Main/GetDefaultMovementType, Object/GetEntry, ScriptedAI/ScriptedAI, SpellCaster/CastSpell#2 | — | — |
| EnterStoneform | instance_naxxramas.Main | method | Creature.Main/GetDefaultMovementType, Object/GetEntry, SpellCaster/CastSpell#2 | — | — |
| Reset#2 | instance_naxxramas.Main | method | shared_Util/urand | — | — |
| JustReachedHome | instance_naxxramas.Main | method | — | — | — |
| MoveInLineOfSight | instance_naxxramas.Main | method | BasicAI/MoveInLineOfSight, CreatureAI/AttackStart, Object/GetTypeId, Unit.Main/HasAura#2, Unit.Main/HasAuraType, Unit.Main/IsInCombat, WorldObject.Object/IsWithinDistInMap, WorldObject.Object/IsWithinLOSInMap | — | — |
| Aggro | instance_naxxramas.Main | method | Unit.Main/HasAura#2, Unit.Main/RemoveAurasDueToSpellByCancel | — | — |
| UpdateAI#2 | instance_naxxramas.Main | method | Creature.Main/GetDBTableGUIDLow, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, ScriptMgr/DoScriptText, SpellCaster/CastSpell#2, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/GetHealthPercent, Unit.Main/GetVictim, Unit.Main/HasAura#2, Unit.Main/SelectHostileTarget | — | — |
| mob_naxxramasPlagueSlimeAI | instance_naxxramas.Main | ctor | ScriptedAI/ScriptedAI | — | — |
| ChangeColor | instance_naxxramas.Main | method | Creature.Main/UpdateEntry, CreatureAI/DoCastSpellIfCan, shared_Util/urand, SpellMgr/GetSpellEntry, SpellMgr/Instance, Unit.Main/RemoveAurasDueToSpell, WorldObject.Object/SetObjectScale | — | — |
| Reset#3 | instance_naxxramas.Main | method | — | — | — |
| Aggro#2 | instance_naxxramas.Main | method | Creature.Main/CallForHelp | — | — |
| UpdateAI#3 | instance_naxxramas.Main | method | CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| mob_toxic_tunnelAI | instance_naxxramas.Main | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#5 | instance_naxxramas.Main | method | — | — | — |
| AttackStart | instance_naxxramas.Main | method | — | — | — |
| MoveInLineOfSight#2 | instance_naxxramas.Main | method | — | — | — |
| EnterCombat | instance_naxxramas.Main | method | — | — | — |
| UpdateAI#5 | instance_naxxramas.Main | method | ScriptedAI/EnterEvadeMode, SpellCaster/CastSpell#2, Unit.Main/HasAura#2 | — | — |
| mob_dark_touched_warriorAI | instance_naxxramas.Main | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | instance_naxxramas.Main | method | — | — | — |
| FleeToHorse | instance_naxxramas.Main | method | Creature.MotionMaster/MoveSeekAssistance, NearestCreatureEntryWithLiveStateInObjectRangeCheck/NearestCreatureEntryWithLiveStateInObjectRangeCheck, ObjectGuid/ObjectGuid, SpellCaster/InterruptSpellsWithInterruptFlags, Unit.Main/GetMotionMaster, Unit.Main/GetVictim, Unit.Main/HasAuraType, Unit.Main/SetTargetGuid, Unit.Main/UpdateSpeed, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | — | — |
| UpdateAI | instance_naxxramas.Main | method | CreatureAI/DoMeleeAttackIfReady, Unit.Main/GetHealthPercent, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_mob_spiritOfNaxxramas | instance_naxxramas.Main | function | — | — | — |
| GetAI_mob_naxxramasGargoyle | instance_naxxramas.Main | function | — | — | — |
| GetAI_mob_plagueSlimeAI | instance_naxxramas.Main | function | — | — | — |
| GetAI_toxic_tunnel | instance_naxxramas.Main | function | — | — | — |
| GetAI_dark_touched_warrior | instance_naxxramas.Main | function | — | — | — |
| LearnCraftIfCan | instance_naxxramas.Main | function | Player.Main/GetReputationRank, Player.Main/HasSpell, SpellCaster/CastSpell#2 | — | — |
| GossipSelect_npc_MasterCraftsmanOmarion | instance_naxxramas.Main | function | GossipDef/AddMenuItem#4, GossipDef/AddMenuItem#5, GossipDef/CloseGossip, GossipDef/SendGossipMenu, Object/GetGUID, ObjectGuid/ObjectGuid#5, Player.Main/AddItem, Player.Main/GetReputationRank, Player.Main/GetSkillValue, Player.Main/HasItemCount, PlayerMenu/GetGossipMenu | — | — |
| GossipHello_npc_MasterCraftsmanOmarion | instance_naxxramas.Main | function | GossipDef/AddMenuItem#5, GossipDef/SendGossipMenu, Object/GetGUID, ObjectGuid/ObjectGuid#5, Player.Main/GetSkillValue, PlayerMenu/GetGossipMenu, Unit.Main/HandleEmote | — | — |
| OnBeforeApply | instance_naxxramas.Main | method | Aura/GetTarget, Unit.Main/SetStandState, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag | — | — |
| GetScript_GargoyleStoneform | instance_naxxramas.Main | function | — | — | — |
| OnCheckTarget | instance_naxxramas.Main | method | Unit.Main/HasAura#2 | — | — |
| GetScript_UnrelentingRiderShadowBoltVolley | instance_naxxramas.Main | function | — | — | — |
| AddSC_instance_naxxramas | instance_naxxramas.Main | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
