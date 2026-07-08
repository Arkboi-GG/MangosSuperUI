# boss_bug_trio

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_bug_trio

**Purpose & Responsibilities**  
`boss_bug_trio.cpp` implements the artificial intelligence for the **Bug Trio** encounter in the Temple of Ahn'Qiraj (AQ40). This is a coordinated three-boss fight featuring Lord Kri, Princess Yauj, and Vem. The core design pattern is a shared base class (`boss_bug_trioAI`) that handles mechanics common to all three bosses—specifically the "devour" mechanic where surviving bugs sprint to and consume the corpse of a fallen ally to fully heal—and individual derived classes (`boss_kriAI`, `boss_yaujAI`, `boss_vemAI`) that implement each boss's unique spell rotations and death effects. The unit also manages encounter-wide leash checks to force a reset if any boss leaves the designated arena bounds.

**Cross-Unit Boundaries**  
The AI relies heavily on `ScriptedInstance` (via `m_pInstance`) to track encounter state (`TYPE_BUG_TRIO`) and locate the other two bugs via GUID storage (`NPC_KRI`, `NPC_PRINCESS_YAUJ`, `NPC_VEM`). It uses `ScriptedAI` for standard threat and movement utilities. `WorldObject` and `Unit` methods are used for spatial calculations (line-of-sight, distance) and state management (health, speed, targets). No database tables are accessed; all logic is runtime-driven.

**Data Model**  
This unit does not interact with any database tables. All state is held in memory via the `ScriptedInstance` object and local member variables.

**Notable Implementation Details**  
*   **Devour Mechanic:** When a bug dies, `JustDied` in the base class triggers `TriggerDevour` on the other two living bugs. This sets a flag (`m_bIsEating`), boosts run speed to 2.7x, and moves the bug to the corpse's location. Upon arrival (`MovementInform`), it waits 4 seconds (`m_uiDevourTimer`), then resets threat, heals to full health, and resumes combat.
*   **Leash Check:** Every 2.5 seconds, `UpdateAI` checks if any bug has moved beyond specific coordinate thresholds (`Y < 2060` and `X > -8600`). If so, `LeashEncounter` forces all three bugs to evade and respawn, preventing players from kiting them out of the room.
*   **Yauj's Fear Spell Workaround:** A comment notes that the original fear spell (25807) only fears one player. The script uses Magmadar's panic spell (19408) instead, which has similar range/duration but affects multiple targets.
*   **Yauj's Immunity Hack:** Yauj is supposed to be immune to casting slow debuffs. Since Vanilla WoW lacks a specific immunity mask for these, `SpellHit` manually removes any `SPELL_AURA_MOD_CASTING_SPEED_NOT_STACK` aura applied to her.
*   **Vem's Threat Manipulation:** When Vem's knockback spell hits a player, `SpellHitTarget` reduces the victim's threat by 80% relative to the current target, likely to prevent aggro swaps or encourage re-engagement.
*   **Yauj Brood Spawning:** On death, Yauj summons 10 broods. The code uses a line-of-sight check against a central point in the room to ensure they don't spawn inside walls or underground, re-rolling coordinates if necessary.

## Member Reference

**boss_bug_trioAI** (ctor): Initializes the base AI, retrieves the instance data pointer, and calls `Reset`.

**Reset**: Resets the eating flag and initializes the evade check timer to 2500ms.

**JustReachedHome**: Marks the encounter as failed in the instance data if the bug returns to its home position.

**EnterEvadeMode**: Restores normal run speed (removing the devour speed boost) before calling the parent evasion routine.

**Aggro**: Sets the encounter state to `IN_PROGRESS` in the instance data.

**MoveInLineOfSight**: Extends the aggro radius to 60 yards for players who are not feigning death, in line of sight, and not already in combat.

**JustDied**: Prevents looting, schedules a forced despawn, and triggers the `TriggerDevour` mechanic on the other two living bugs if the encounter isn't already complete.

**CorpseRemoved**: Plays the devour emote when the corpse is removed.

**MovementInform**: Stops movement when the bug reaches the destination point (used for the devour sprint).

**TriggerDevour**: Sets the eating flag, boosts run speed, and moves the bug to the specified unit's (corpse's) location.

**LeashEncounter**: Forces all three bugs to evade and respawn if they are dead, ensuring the encounter resets cleanly if leashed.

**UpdateBugAI**: A virtual placeholder for subclass-specific AI logic; returns true by default.

**UpdateAI**: Handles the devour timer (healing and resuming combat), calls the subclass `UpdateBugAI`, performs melee attacks, and checks for leash violations.

**boss_kriAI** (ctor): Initializes Lord Kri's AI, inheriting from `boss_bug_trioAI`.

**Reset#2**: Initializes timers for Cleave, Thrash, and Toxic Volley with random delays.

**JustDied#2**: Summons a poison cloud trigger on death, then calls the base class death handler.

**UpdateBugAI#2**: Manages Kri's spell rotation: Cleave (on victim), Toxic Volley (self), and Thrash (self) with randomized cooldowns.

**boss_yaujAI** (ctor): Initializes Princess Yauj's AI, inheriting from `boss_bug_trioAI`.

**Reset#4**: Initializes timers for Heal, Fear, and Ravage with random delays.

**SpellHit**: Removes any casting speed reduction auras from Yauj to simulate immunity.

**JustDied#4**: Summons 10 Yauj Broods around her corpse, using line-of-sight checks to avoid invalid spawns, then calls the base class death handler.

**JustSummoned**: Puts summoned Yauj Broods into combat with the zone.

**UpdateBugAI#4**: Manages Yauj's spell rotation: Fear (self, resets threat), Heal (self if below 93% HP, otherwise lowest HP friendly), and Ravage (on victim).

**boss_vemAI** (ctor): Initializes Vem's AI, inheriting from `boss_bug_trioAI`.

**Reset#3**: Initializes timers for Charge, Knockback, and Knockdown with random delays.

**JustDied#3**: Casts Vengeance (enrage) on self on death, then calls the base class death handler.

**SpellHitTarget**: Reduces the victim's threat by 80% if hit by Knockback, to manage aggro dynamics.

**UpdateBugAI#3**: Manages Vem's spell rotation: Charge (random target outside melee), Knockback (victim if in melee and not stunned), and Knockdown (random target in melee).

**GetAI_boss_kri**: Factory function to create a `boss_kriAI` instance.

**GetAI_boss_yauj**: Factory function to create a `boss_yaujAI` instance.

**GetAI_boss_vem**: Factory function to create a `boss_vemAI` instance.

**AddSC_bug_trio**: Registers the three boss scripts with the script manager.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_bug_trio

*Source:* boss_bug_trio.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_bug_trioAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | — | — | — |
| JustReachedHome | method | InstanceData/SetData | — | — |
| EnterEvadeMode | method | ScriptedAI/EnterEvadeMode, Unit.Main/UpdateSpeed | — | — |
| Aggro | method | InstanceData/SetData | — | — |
| MoveInLineOfSight | method | BasicAI/MoveInLineOfSight, CreatureAI/AttackStart, Object/GetTypeId, Unit.Main/HasAuraType, Unit.Main/IsInCombat, WorldObject.Object/IsWithinDistInMap, WorldObject.Object/IsWithinLOSInMap | — | — |
| JustDied | method | Creature.Main/AI, Creature.Main/ForcedDespawn, Creature.Main/SetLootRecipient, InstanceData/GetData, InstanceData/SetData, ScriptedInstance/GetSingleCreatureFromStorage | — | — |
| CorpseRemoved | method | ScriptMgr/DoScriptText | — | — |
| MovementInform | method | Creature.MotionMaster/MoveIdle, Unit.Main/GetMotionMaster | — | — |
| TriggerDevour | method | Creature.MotionMaster/MovePoint, ObjectGuid/ObjectGuid, Unit.Main/GetMotionMaster, Unit.Main/IsAlive, Unit.Main/SetTargetGuid, Unit.Main/UpdateSpeed, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | — | — |
| LeashEncounter | method | Creature.Main/AI, Creature.Main/Respawn, CreatureAI/EnterEvadeMode, ScriptedInstance/GetSingleCreatureFromStorage, Unit.Main/IsDead | — | — |
| UpdateBugAI | method | — | — | — |
| UpdateAI | method | CreatureAI/DoMeleeAttackIfReady, Object/GetObjectGuid, ScriptedAI/DoResetThreat, ScriptedAI/DoStartMovement, Unit.Main/GetMaxHealth, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, Unit.Main/SetHealth, Unit.Main/SetTargetGuid, Unit.Main/UpdateSpeed, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY | — | — |
| boss_kriAI | ctor | — | — | — |
| Reset#2 | method | shared_Util/urand | — | — |
| JustDied#2 | method | CreatureAI/DoCastSpellIfCan | — | — |
| UpdateBugAI#2 | method | CreatureAI/DoCastSpellIfCan, shared_Util/urand, Unit.Main/GetVictim | — | — |
| boss_yaujAI | ctor | — | — | — |
| Reset#4 | method | shared_Util/urand | — | — |
| SpellHit | method | Unit.Main/HasAuraType, Unit.Main/RemoveSpellsCausingAura | — | — |
| JustDied#4 | method | Map.Main/isInLineOfSight, WorldObject.Object/GetMap, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/GetRandomPoint, WorldObject.Object/SummonCreature#2 | — | — |
| JustSummoned | method | Creature.Main/SetInCombatWithZone, Object/GetEntry | — | — |
| UpdateBugAI#4 | method | CreatureAI/DoCastSpellIfCan, ScriptedAI/DoResetThreat, shared_Util/urand, Unit.Main/FindLowestHpFriendlyUnit, Unit.Main/GetHealthPercent, Unit.Main/GetVictim | — | — |
| boss_vemAI | ctor | — | — | — |
| Reset#3 | method | shared_Util/urand | — | — |
| JustDied#3 | method | CreatureAI/DoCastSpellIfCan | — | — |
| SpellHitTarget | method | Object/GetTypeId, ThreatManager/getThreat, ThreatManager/modifyThreatPercent#2, Unit.Main/GetThreatManager, Unit.Main/GetVictim | — | — |
| UpdateBugAI#3 | method | Creature.Main/SelectAttackingTarget#2, CreatureAI/DoCastSpellIfCan, shared_Util/urand, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/GetVictim, Unit.Main/HasUnitState | — | — |
| GetAI_boss_kri | function | — | — | — |
| GetAI_boss_yauj | function | — | — | — |
| GetAI_boss_vem | function | — | — | — |
| AddSC_bug_trio | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
