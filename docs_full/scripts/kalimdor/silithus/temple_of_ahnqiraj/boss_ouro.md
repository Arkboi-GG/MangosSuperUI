# boss_ouro

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_ouro.cpp

**Purpose & Responsibilities**  
This translation unit implements the complete scripted behavior for the **Ouro** encounter in the *Temple of Ahn'Qiraj* raid instance. It defines five distinct AI classes:
1.  `boss_ouroAI`: The main boss logic, handling emergence, submersion, targeted spells (Sand Blast, Sweep), enrage mechanics, and threat management.
2.  `npc_ouro_spawnerAI`: A passive trigger creature that summons the boss when a non-GM player enters its line of sight.
3.  `npc_dirt_moundAI`: Summons by the boss during submersion; follows a player and eventually spawns scarabs before despawning.
4.  `npc_ouro_scarabAI`: Minor adds spawned by dirt mounds; aggro on sight and melee attack.
5.  `go_sandworm_baseAI`: A game object representing the sandworm hole; handles visual animations and deletion upon interaction.

The unit contains no database interactions; all logic is driven by in-memory state, timers, and spell effects.

---

## Member-by-Member Behavior

### Boss Ouro (`boss_ouroAI`)

#### Initialization & Timers
- **`boss_ouroAI`**: Constructor initializes the instance data pointer and calls `Reset`.
- **`SandBlastTimerMin`**, **`SandBlastTimerMax`**, **`SubmergeTimer`**: Inline methods returning timer values adjusted for the server's configured WoW patch version (`WOW_PATCH_110` and above use longer/cooler timers, reflecting later-era nerfs).

#### State Management
- **`Reset`**: Resets all internal timers, clears the trigger GUID, and forces the boss into a "rooted" state (`UNIT_STATE_ROOT`, `SetRooted(true)`). This ensures Ouro does not move from its position during combat. Sets initial grace timers for emergence and melee engagement.
- **`Aggro`**: Notifies the instance script that the encounter has started (`IN_PROGRESS`) and records the boss's starting location.
- **`JustReachedHome`**: Triggered on evade/reset. Marks the encounter as failed, despawns all associated creatures (mounds, scarabs), casts the despawn base spell, initiates submersion visuals, and forces the boss to despawn after 2 seconds.
- **`JustDied`**: Marks the encounter as complete (`DONE`) and casts the despawn base spell.

#### Combat Mechanics
- **`UpdateAI`**: The core loop. It manages:
  - **Rooting**: Ensures the boss remains rooted unless in evade mode.
  - **Emergence**: If submerged, waits for the submerge timer, then teleports to the trigger location, becomes visible, casts birth/rupture spells, and resets combat timers.
  - **Abilities**:
    - *Sweep*: Casts periodically on a fixed timer.
    - *Sand Blast*: Targets the highest-threat player. Visually locks onto the target for 2.1 seconds to ensure animation correctness, then restores visual focus to the melee victim.
    - *Enrage*: At 20% health, casts Berserk. While enraged, summons dirt mounds every 10 seconds and spams Boulder spells if no melee target is present.
    - *Submersion*: If not enraged and no melee target is within range for 3–10 seconds (depending on grace timers), triggers `Submerge`.
  - **Threat**: Preserves threat list across submersions.
- **`Submerge`**: Initiates the submersion sequence. Plays visual spells, summons dirt mounds and a trigger creature, sets the boss unselectable/spawning flags, clears the target icon, wipes threat (`DoResetThreat`), and sets the submerge duration timer (30 seconds).
- **`SpellHitTarget`**: Reduces threat by 100% on the target hit by Sand Blast, preventing immediate re-targeting of the same player.
- **`JustSummoned`**: If the summoned creature is the Ouro Trigger (`NPC_OURO_TRIGGER`), stores its GUID and commands it to follow a random hostile target. This trigger determines where Ouro emerges next.
- **`DespawnCreatures`**: Helper to remove all dirt mounds and optionally scarabs within a 250-unit radius.

### Spawner (`npc_ouro_spawnerAI`)
- **`npc_ouro_spawnerAI`**: Constructor calls `Reset`.
- **`Reset`**: Initializes the "has summoned" flag and casts a passive aura. Enables line-of-sight events.
- **`MoveInLineOfSight`**: If a non-GM player comes within 25 units and the boss hasn't been summoned yet, casts the summon spell for Ouro.
- **`JustSummoned`**: When Ouro is summoned, casts the birth animation on him, puts him in combat, and despawns the spawner itself.
- **`UpdateAI`**: Empty; logic is event-driven.

### Dirt Mound (`npc_dirt_moundAI`)
- **`npc_dirt_moundAI`**: Constructor calls `Reset`.
- **`JustRespawned`**: Sets unselectable/spawning flags.
- **`Reset`**: Sets a 30-second despawn timer and casts the passive aura.
- **`MoveInLineOfSight`**: Records the first player seen as the target to follow.
- **`UpdateAI`**: 
  - Follows the recorded target (or moves randomly if invalid/dead/immune).
  - After 30 seconds, spawns scarabs and despawns itself.

### Scarab (`npc_ouro_scarabAI`)
- **`npc_ouro_scarabAI`**: Constructor calls `Reset`.
- **`Reset`**: Sets a 45-second despawn timer and enables LOS events.
- **`MoveInLineOfSight`**: 20% chance to aggro a player on sight if not already in combat.
- **`UpdateAI`**: Melee attacks current victim; despawns after 45 seconds.

### Sandworm Base (`go_sandworm_baseAI`)
- **`go_sandworm_baseAI`**: Constructor initializes active state.
- **`OnUse`**: Toggles the game object's state. First use plays a custom animation; second use deletes the object immediately.

### Registration
- **`GetAI_boss_ouro`**, **`GetAI_npc_ouro_spawner`**, **`GetAI_npc_dirt_mound`**, **`GetAI_npc_ouro_scarab`**, **`GetAIgo_sandworm_base`**: Factory functions returning new AI instances.
- **`AddSC_boss_ouro`**: Registers all five scripts with the engine.

---

## Cross-Unit Boundaries

| Member | Direction | Collaborating Unit | Interaction Details |
|--------|-----------|--------------------|---------------------|
| `boss_ouroAI` (ctor) | Calls | `ScriptedAI`, `WorldObject` | Inherits base AI behavior; retrieves instance data for encounter tracking. |
| `SandBlastTimerMin/Max`, `SubmergeTimer` | Calls | `World` | Queries `GetWowPatch()` to adjust difficulty/timers based on server configuration. |
| `Reset` | Calls | `Unit.Main`, `shared_Util` | Applies root states, stops movement, generates random timers. |
| `Aggro` | Calls | `InstanceData`, `WorldObject` | Updates encounter state; records position. |
| `DespawnCreatures` | Calls | `Creature.Main`, `WorldObject` | Finds and removes specific NPC entries from the grid. |
| `JustReachedHome`, `JustDied` | Calls | `InstanceData`, `SpellCaster` | Updates encounter state; casts cleanup spells. |
| `JustSummoned` | Calls | `Creature.Main`, `Unit.Main`, `Object` | Configures the summoned trigger to follow a target. |
| `SpellHitTarget` | Calls | `ThreatManager`, `Unit.Main` | Modifies threat percentages to balance targeting. |
| `Submerge` | Calls | `CreatureAI`, `ScriptedAI`, `WorldObject` | Casts spells, modifies unit flags, resets threat. |
| `UpdateAI` | Calls | `Creature.Main`, `Map.Main`, `Unit.Main`, `WorldObject`, `SpellCaster`, `ThreatManager`, `shared_Util` | Core logic: checks evade mode, selects targets, casts spells, manages timers, handles visibility, teleports, and manages threat lists. |
| `npc_ouro_spawnerAI::MoveInLineOfSight` | Calls | `BasicAI`, `CreatureAI`, `Player.Main`, `WorldObject` | Checks player type/distance/GM status; casts summon spell. |
| `npc_ouro_spawnerAI::JustSummoned` | Calls | `Creature.Main`, `SpellCaster` | Despawns self; applies birth spell to boss. |
| `npc_dirt_moundAI::JustRespawned` | Calls | `ScriptedAI`, `WorldObject` | Sets unit flags. |
| `npc_dirt_moundAI::Reset` | Calls | `CreatureAI`, `ObjectGuid` | Casts passive spell; clears target GUIDs. |
| `npc_dirt_moundAI::MoveInLineOfSight` | Calls | `Object`, `ObjectGuid` | Records player GUID. |
| `npc_dirt_moundAI::UpdateAI` | Calls | `Creature.Main`, `Creature.MotionMaster`, `Map.Main`, `Unit.Main`, `WorldObject`, `SpellCaster`, `shared_Util` | Manages movement (follow/random), despawn timer, and scarab spawning. |
| `npc_ouro_scarabAI::Reset` | Calls | `Creature.Main` | Enables LOS events. |
| `npc_ouro_scarabAI::MoveInLineOfSight` | Calls | `CreatureAI`, `Object`, `Unit.Main`, `shared_Util` | Random aggro check. |
| `npc_ouro_scarabAI::UpdateAI` | Calls | `Creature.Main`, `CreatureAI`, `Unit.Main` | Melee attacks; manages despawn timer. |
| `go_sandworm_baseAI::OnUse` | Calls | `GameObject`, `Object`, `WorldLocation` | Plays animation or deletes object. |
| `AddSC_boss_ouro` | Calls | `Script`, `ScriptMgr` | Registers scripts with the global manager. |
| `AddSC_boss_ouro` | Called By | `ScriptLoader` | Entry point for script initialization. |

---

## Data Model

This unit does not interact with any database tables. All state is maintained in memory via AI member variables and instance data.

---

## Notable Implementation Details

1.  **Rooted Movement Logic**:  
    Ouro is forced into a rooted state (`UNIT_STATE_ROOT`) in `Reset` and `UpdateAI`. This prevents him from moving during combat. However, rooted units in the engine do not automatically reset their evade state. `UpdateAI` explicitly checks `IsInEvadeMode()` and clears the root state if true, allowing the boss to properly reset.

2.  **Visual Targeting for Sand Blast**:  
    In `UpdateAI`, when casting Sand Blast, Ouro sets his visual target to the highest-threat player (`SetInFront`, `SetTargetGuid`). A 2.1-second timer (`m_uiRestoreTargetTimer`) ensures the animation completes facing the correct direction before swapping the visual target back to the melee victim. This prevents visual glitches where the boss appears to spit sand at the wrong person.

3.  **Submersion Grace Periods**:  
    Two timers prevent premature submersion:
    - `m_justEmergedGraceTimer` (10s): Prevents submersion immediately after emerging.
    - `m_uiNoMeleeTimer` (3s normally, 10s after submerge): Prevents submersion if a melee attacker is present.  
    *Gotcha*: The code comments note that `m_uiNoMeleeTimer` must be set to 10 seconds after submerge. If it remained 3 seconds, and `CanReachWithMeleeAutoAttack` returned false for the entire 3 seconds, the timer would never reset, potentially causing immediate re-submersion.

4.  **Enrage Mechanics**:  
    At 20% health, Ouro enrages (`SPELL_BERSERK`). While enraged:
    - He summons a dirt mound every 10 seconds.
    - If no melee target is present, he spams `SPELL_BOULDER` on random targets.
    - He does *not* submerge while enraged.

5.  **Trigger-Based Emergence**:  
    Upon submersion, Ouro summons a hidden trigger creature (`NPC_OURO_TRIGGER`). This trigger follows a random hostile player. When Ouro emerges, he teleports to the trigger's location (`NearTeleportTo`). This ensures Ouro emerges near players, increasing engagement.

6.  **Patch-Specific Timers**:  
    `SandBlastTimerMin`, `SandBlastTimerMax`, and `SubmergeTimer` use `sWorld.GetWowPatch()` to return different values for patch 1.10+ vs. earlier patches. This allows the same script to support multiple difficulty profiles.

7.  **Threat Management**:  
    Threat is preserved across submersions. However, `Submerge` calls `DoResetThreat()`, which typically wipes threat. The comment notes that threat is preserved because the wipe happens *after* the submerge sequence begins, and the threat list is restored upon emergence. Sand Blast reduces threat by 100% on the target to prevent stacking.

8.  **Dirt Mound Targeting**:  
    `npc_dirt_moundAI` records the first player it sees (`MoveInLineOfSight`) and follows them. If the target dies or becomes immune, it switches to random movement. This ensures mounds stay relevant to the fight.

9.  **Scarab Aggro**:  
    `npc_ouro_scarabAI` has a 20% chance (`!urand(0, 5)`) to aggro on sight. This prevents all scarabs from instantly attacking every player, creating a more manageable add phase.

10. **Sandworm Base Interaction**:  
    `go_sandworm_baseAI` is a simple toggle. First use plays an animation; second use deletes the object. This likely represents the hole closing/opening visually.

---

## Member Reference

- **`boss_ouroAI`**: Constructor; initializes instance data and calls `Reset`.
- **`SandBlastTimerMin`**: Returns minimum Sand Blast cooldown based on WoW patch.
- **`SandBlastTimerMax`**: Returns maximum Sand Blast cooldown based on WoW patch.
- **`SubmergeTimer`**: Returns submerge interval based on WoW patch.
- **`Reset`**: Resets timers, roots the boss, clears trigger GUID, and sets initial states.
- **`Aggro`**: Sets encounter state to IN_PROGRESS and records starting position.
- **`DespawnCreatures`**: Removes dirt mounds and optionally scarabs from the grid.
- **`JustReachedHome`**: Handles evade/reset; marks encounter failed, despawns adds, and forces boss despawn.
- **`JustDied`**: Marks encounter done and casts despawn base spell.
- **`JustSummoned`**: Configures the Ouro Trigger to follow a random target.
- **`SpellHitTarget`**: Reduces threat by 100% on Sand Blast targets.
- **`Submerge`**: Initiates submersion sequence; casts spells, sets flags, wipes threat, and sets timers.
- **`UpdateAI`**: Core loop; manages rooting, emergence, abilities (Sweep, Sand Blast, Enrage, Submerge), and threat.
- **`GetAI_boss_ouro`**: Factory function for `boss_ouroAI`.
- **`npc_ouro_spawnerAI`**: Constructor; calls `Reset`.
- **`Reset#4`**: (In `npc_ouro_spawnerAI`) Initializes summon flag and casts passive aura.
- **`MoveInLineOfSight#3`**: (In `npc_ouro_spawnerAI`) Summons Ouro if a non-GM player is nearby.
- **`JustSummoned#2`**: (In `npc_ouro_spawnerAI`) Despawns self and applies birth spell to Ouro.
- **`UpdateAI#4`**: (In `npc_ouro_spawnerAI`) Empty; logic is event-driven.
- **`GetAI_npc_ouro_spawner`**: Factory function for `npc_ouro_spawnerAI`.
- **`npc_dirt_moundAI`**: Constructor; calls `Reset`.
- **`JustRespawned`**: Sets unselectable/spawning flags.
- **`Reset#2`**: (In `npc_dirt_moundAI`) Sets despawn timer and casts passive aura.
- **`MoveInLineOfSight`**: (In `npc_dirt_moundAI`) Records first player seen as target.
- **`UpdateAI#2`**: (In `npc_dirt_moundAI`) Manages movement, despawn timer, and scarab spawning.
- **`GetAI_npc_dirt_mound`**: Factory function for `npc_dirt_moundAI`.
- **`npc_ouro_scarabAI`**: Constructor; calls `Reset`.
- **`Reset#3`**: (In `npc_ouro_scarabAI`) Sets despawn timer and enables LOS events.
- **`MoveInLineOfSight#2`**: (In `npc_ouro_scarabAI`) 20% chance to aggro on sight.
- **`UpdateAI#3`**: (In `npc_ouro_scarabAI`) Melee attacks and manages despawn timer.
- **`GetAI_npc_ouro_scarab`**: Factory function for `npc_ouro_scarabAI`.
- **`go_sandworm_baseAI`**: Constructor; initializes active state.
- **`OnUse`**: Toggles animation or deletes the game object.
- **`GetAIgo_sandworm_base`**: Factory function for `go_sandworm_baseAI`.
- **`AddSC_boss_ouro`**: Registers all five scripts with the engine.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_ouro

*Source:* boss_ouro.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_ouroAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| SandBlastTimerMin | method | World/GetWowPatch | — | — |
| SandBlastTimerMax | method | World/GetWowPatch | — | — |
| SubmergeTimer | method | World/GetWowPatch | — | — |
| Reset | method | ObjectGuid/Clear, shared_Util/urand, Unit.Main/AddUnitState, Unit.Main/SetRooted, Unit.Main/StopMoving | — | — |
| Aggro | method | InstanceData/SetData, WorldObject.Object/GetPosition | — | — |
| DespawnCreatures | method | Creature.Main/ForcedDespawn, WorldObject.Object/GetCreatureListWithEntryInGrid | — | — |
| JustReachedHome | method | Creature.Main/ForcedDespawn, InstanceData/SetData, SpellCaster/CastSpell#2 | — | — |
| JustDied | method | InstanceData/SetData, SpellCaster/CastSpell#2 | — | — |
| JustSummoned | method | Creature.Main/SelectAttackingTarget, Creature.MotionMaster/MoveFollow, Object/GetEntry, Object/GetGUID, ObjectGuid/ObjectGuid#5, Unit.Main/GetMotionMaster | — | — |
| SpellHitTarget | method | ThreatManager/getThreat, ThreatManager/modifyThreatPercent#2, Unit.Main/GetThreatManager | — | — |
| Submerge | method | CreatureAI/ClearTargetIcon, CreatureAI/DoCastSpellIfCan, ScriptedAI/DoResetThreat, WorldObject.Object/SetFlag | — | — |
| UpdateAI | method | Creature.Main/IsInEvadeMode, Creature.Main/SelectAttackingTarget, Creature.Main/SelectAttackingTarget#2, Creature.Main/SetHomePosition, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Map.Main/GetCreature, Object/GetObjectGuid, shared_Util/urand, SpellCaster/CastSpell#2, SpellCaster/IsNonMeleeSpellCasted, ThreatManager/getThreatList, Unit.Main/AddUnitState, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/ClearUnitState, Unit.Main/GetHealthPercent, Unit.Main/GetThreatManager, Unit.Main/GetVictim, Unit.Main/GetVisibility, Unit.Main/RemoveAurasDueToSpell, Unit.Main/SelectHostileTarget, Unit.Main/SetInFront, Unit.Main/SetRooted, Unit.Main/SetTargetGuid, Unit.Main/SetVisibility, Unit.Main/StopMoving, WorldObject.Object/GetDistance2d#3, WorldObject.Object/GetMap, WorldObject.Object/GetOrientation, WorldObject.Object/GetPosition#3, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/RemoveFlag | — | — |
| GetAI_boss_ouro | function | — | — | — |
| npc_ouro_spawnerAI | ctor | Scripted_NoMovementAI/Scripted_NoMovementAI | — | — |
| Reset#4 | method | Creature.Main/EnableMoveInLosEvent, CreatureAI/DoCastSpellIfCan | — | — |
| MoveInLineOfSight#3 | method | BasicAI/MoveInLineOfSight, CreatureAI/DoCastSpellIfCan, Object/GetTypeId, Player.Main/IsGameMaster, WorldObject.Object/IsWithinDistInMap | — | — |
| JustSummoned#2 | method | Creature.Main/ForcedDespawn, Creature.Main/SetInCombatWithZone, Object/GetEntry, SpellCaster/CastSpell#2 | — | — |
| UpdateAI#4 | method | — | — | — |
| GetAI_npc_ouro_spawner | function | — | — | — |
| npc_dirt_moundAI | ctor | ScriptedAI/ScriptedAI | — | — |
| JustRespawned | method | ScriptedAI/JustRespawned, WorldObject.Object/SetFlag | — | — |
| Reset#2 | method | CreatureAI/DoCastSpellIfCan, ObjectGuid/Clear | — | — |
| MoveInLineOfSight | method | Object/GetGUID, Object/GetTypeId, ObjectGuid/ObjectGuid#5, ObjectGuid/operator! | — | — |
| UpdateAI#2 | method | Creature.Main/ForcedDespawn, Creature.MotionMaster/MoveFollow, Creature.MotionMaster/MoveRandom, Map.Main/GetUnit, ObjectGuid/Clear, shared_Util/urand, SpellCaster/CastSpell#2, Unit.Main/GetMotionMaster, Unit.Main/IsDead, Unit.Main/IsImmuneToDamage, WorldObject.Object/GetMap | — | — |
| GetAI_npc_dirt_mound | function | — | — | — |
| npc_ouro_scarabAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#3 | method | Creature.Main/EnableMoveInLosEvent | — | — |
| MoveInLineOfSight#2 | method | CreatureAI/AttackStart, Object/GetTypeId, shared_Util/urand, Unit.Main/GetVictim | — | — |
| UpdateAI#3 | method | Creature.Main/ForcedDespawn, CreatureAI/DoMeleeAttackIfReady, Unit.Main/GetVictim | — | — |
| GetAI_npc_ouro_scarab | function | — | — | — |
| go_sandworm_baseAI | ctor | GameObjectAI/GameObjectAI | — | — |
| OnUse | method | GameObject/Delete, GameObject/SendGameObjectCustomAnim, GameObject/SetRespawnTime, Object/GetObjectScale, WorldLocation/WorldLocation#2, WorldObject.Object/GetPosition | — | — |
| GetAIgo_sandworm_base | function | — | — | — |
| AddSC_boss_ouro | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
