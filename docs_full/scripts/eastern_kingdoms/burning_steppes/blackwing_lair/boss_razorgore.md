# boss_razorgore

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_razorgore

**Purpose & Responsibilities**  
This translation unit implements the artificial intelligence and encounter mechanics for **Razorgore the Untamed**, a raid boss in the *Blackwing Lair* instance, and its associated helper creature, the **Orb of Command**. The encounter is structured around two distinct phases managed by two separate AI classes:

1.  **`boss_razorgoreAI`**: Controls Razorgore during Phase 1 (pre-egg destruction) and Phase 2 (post-egg destruction). In Phase 1, Razorgore is possessed by a player via the Orb of Command, acting as a controlled mount/unit. In Phase 2, after the eggs are destroyed, Razorgore becomes hostile, summons adds, and uses offensive spells. It also handles failure states (player death or evasion).
2.  **`trigger_orb_of_commandAI`**: Controls the Orb of Command creature. It manages the spawning of adds (Legionnaires, Mages, Dragonspawn) during Phase 1, detects when the eggs are destroyed to trigger the phase transition (`PhaseSwitch`), and handles the possession mechanics (transferring threat from Razorgore to the possessing player).

The unit relies heavily on `ScriptedInstance` data to track GUIDs of key objects (Razorgore, the Orb, the Trigger creature) and encounter state (IN_PROGRESS, DONE, FAIL). It does not interact with any database tables directly; all state is held in memory via the instance script system.

---

## Member-by-Member Behavior

### `boss_razorgoreAI`

#### Initialization & State Management
*   **`boss_razorgoreAI` (ctor)**: Initializes the AI, sets `SetUseAiAtControl(true)` to allow control while possessed, retrieves the instance data, and calls `Reset()`.
*   **`Reset`**: Resets all spell timers to their base values (Cleave: 9s, Warstomp: 22s, Conflagration: 12s, Fireball Volley: 7s, Out-of-Reach: 10s, Init: 5s, Evade Troops: 5s) and enables combat movement.
*   **`SituationInitiale`**: Prepares the encounter for Phase 1. It deletes existing adds (Legionnaires, Mages, Dragonspawn) within 250 yards. It ensures the Orb of Domination trigger creature exists and is invisible/unselectable, channeling Razorgore. It respawns guards (Grethok) and removes the "No Interact" flag from the Orb GameObject. Finally, it teleports Razorgore to his respawn coordinates.
*   **`EvadeTroops`**: Used to clear adds. Finds Legionnaires, Mages, and Dragonspawn within 250 yards and forces them to evade (despawn/flee).

#### Combat & Spell Logic
*   **`UpdateAI`**: The main update loop.
    *   If Razorgore is possessed (`UNIT_STATE_POSSESSED`), it only performs melee attacks.
    *   If initialization isn't complete, it waits for the init timer.
    *   If no target is selected, it returns early.
    *   **Phase 2 Specifics**: If the eggs are done (`DATA_EGG == DONE`), it periodically calls `EvadeTroops()` to prevent adds from re-aggroing via fireballs.
    *   **Patch 1.8+ Feature**: If the victim is unreachable (`CantPathToVictim`), it casts `SPELL_SUMMON_PLAYER` after 10 seconds.
    *   **Spells**: Rotates through Cleave, Warstomp, Fireball Volley, and Conflagration using randomized timers.
*   **`SpellHitTarget`**: Reduces threat by 30% on the target hit by `SPELL_WARSTOMP`.
*   **`AttackStart`**: Handles command attacks (when possessed). If commanded, it attacks the specified victim; otherwise, it falls back to standard AI targeting.
*   **`EnterCombat`**: Sets the instance event to `IN_PROGRESS`. Clears channeling visuals on the trigger creature.

#### Death & Failure Handling
*   **`JustDied`**: Checks if the eggs are destroyed. If yes, marks the event as `DONE`. If no, calls `MortPhaseUn()` to handle failure.
*   **`MortPhaseUn`**: A failure handler. It casts an explosion spell on all alive players in the map, marks the instance as `FAIL`, respawns Razorgore, and resets the encounter via `SituationInitiale()`.
*   **`JustReachedHome`**: Triggered if Razorgore evades combat. Marks the instance as `FAIL`, resets the encounter, and plays a "freed" emote.

### `trigger_orb_of_commandAI`

#### Initialization & Spawning
*   **`trigger_orb_of_commandAI` (ctor)**: Initializes the AI, retrieves instance data, and calls `Reset()`.
*   **`Reset#2`**: Resets the pop timer (45s), sets phase flags, and clears the possessor GUID.
*   **`PopAdd`**: Spawns adds during Phase 1. It checks current add counts (max 12 drakes, 40 orcs/mages). It randomly selects an add type and spawn location (North/South/East/West, with primary/bis coordinates). It can spawn two adds at once. Adds are summoned with corpse despawn, set in combat, and attack Razorgore with minimal threat.

#### Phase Transition & Possession Mechanics
*   **`PhaseSwitch`**: Triggered when eggs are destroyed. It makes all nearby adds flee and become immune/pacified. It disables interaction with the Orb. It restores Razorgore: resets his threat, enables combat movement, and forces him to attack the last player who possessed him (adding massive threat). Finally, it schedules the Orb creature for deletion.
*   **`UpdateAI#2`**:
    *   **Combat Start**: When combat begins and eggs are not yet done, it sets `m_uiCombatStarted = true`, puts guards in combat, and clears Grethok's channel spell.
    *   **Possession Handling**: While Razorgore has the `SPELL_POSSESS` aura:
        *   It records the charmer's GUID.
        *   It stops Razorgore's movement.
        *   It sets Razorgore's max health to 225,000 (reduced from normal).
        *   If the charmer dies, it removes the possess aura and evades Razorgore if no threats remain.
        *   It transfers threat from Razorgore to the charmer for all nearby adds (so adds attack the player controlling him).
    *   **Post-Possession**: When the possess aura ends, it restores Razorgore's max health, resets his threat, enables movement, and forces him to attack the former possessor.
    *   **Spawning**: Periodically calls `PopAdd()` for all four directions.

---

## Cross-Unit Boundaries

*   **`boss_razorgoreAI` ↔ `ScriptedInstance`**:
    *   *Calls*: `GetData64` (to retrieve GUIDs of the Orb, Trigger, and Razorgore), `SetData` (to update encounter state: IN_PROGRESS, DONE, FAIL).
    *   *Why*: To coordinate state between the boss, the orb, and the instance script.
*   **`boss_razorgoreAI` ↔ `Map` / `Creature`**:
    *   *Calls*: `GetCreature`, `GetGameObject`, `GetPlayers`, `SummonCreature`, `GetCreatureListWithEntryInGrid`.
    *   *Why*: To manipulate other entities in the encounter (summoning adds, clearing adds, finding the Orb, exploding players on failure).
*   **`trigger_orb_of_commandAI` ↔ `boss_razorgoreAI`**:
    *   *Calls*: Casts `boss_razorgoreAI` methods via `(ScriptedAI*)pRazorgore->AI()`. Specifically calls `DoResetThreat`, `SetCombatMovement`, `AttackStart`, `EnterEvadeMode`.
    *   *Why*: The Orb AI controls Razorgore's behavior during possession and phase transitions, as Razorgore's own AI is passive or simplified during these states.
*   **`trigger_orb_of_commandAI` ↔ `ThreatManager`**:
    *   *Calls*: `modifyThreatPercent`, `getThreat`, `isThreatListEmpty`.
    *   *Why*: To transfer threat from Razorgore to the possessing player, ensuring adds attack the player instead of the boss.

---

## Data Model

This unit does not query or modify any database tables. All state is managed in-memory via the `ScriptedInstance` system and creature/gameobject properties.

---

## Notable Implementation Details

1.  **Possession Threat Transfer**: In `trigger_orb_of_commandAI::UpdateAI`, when Razorgore is possessed, the code iterates through nearby adds and manually transfers their threat from Razorgore to the charmer (`modifyThreatPercent(pChanneler, -100)` then `AddThreat`). This is critical because adds are programmed to attack Razorgore; without this, they would ignore the player controlling him.
2.  **Health Manipulation**: During possession, Razorgore's max health is hardcoded to 225,000 (`pRazorgore->SetMaxHealth(225000)`). Upon release, it is restored to `RAZORGORE_MAX_HEALTH_DURING_POSESSION` (likely a macro defined elsewhere, possibly the original max health). This suggests Razorgore has reduced health while possessed.
3.  **Failure Explosion**: In `boss_razorgoreAI::MortPhaseUn`, if the encounter fails (Razorgore dies before eggs are done), it casts `SPELL_EXPLOSION` on *every* alive player in the map. This is likely a visual effect or damage penalty for failure.
4.  **Patch-Specific Summon**: The `SPELL_SUMMON_PLAYER` mechanic is guarded by `sWorld.GetWowPatch() >= WOW_PATCH_108`. This indicates a retroactive addition to match WoW patch 1.8.0 behavior.
5.  **Hardcoded Coordinates**: Spawn locations for adds are hardcoded as `#define` macros (e.g., `SPAWN_X1`, `SPAWN_Y1`). There are primary and secondary ("BIS") coordinates for each direction, allowing for varied spawn positions.
6.  **Add Limits**: `PopAdd` enforces hard limits: max 12 Death Talon Dragonspawn and max 40 Blackwing Legionnaires/Mages combined. If limits are reached, it prioritizes spawning the type under the limit or skips spawning.
7.  **Evade Troops Backup**: In `boss_razorgoreAI::UpdateAI`, there is a comment noting that `EvadeTroops` is called as a "backup" in case mages re-aggro via in-flight fireballs. This suggests a known edge case where projectile aggro might persist after adds are supposed to be cleared.

---

## Member Reference

*   **boss_razorgoreAI**: Initializes the AI, sets control usage, retrieves instance data, and calls `Reset`.
*   **Reset**: Resets all spell and event timers to base values and enables combat movement.
*   **SpellHitTarget**: Reduces threat by 30% on targets hit by Warstomp.
*   **AttackStart**: Handles command attacks when possessed; otherwise uses default targeting.
*   **EnterCombat**: Sets instance state to IN_PROGRESS and clears channeling visuals on the trigger creature.
*   **MortPhaseUn**: Handles encounter failure: explodes all players, sets instance to FAIL, respawns Razorgore, and resets the scene.
*   **JustDied**: Checks if eggs are done; if so, marks DONE, otherwise calls `MortPhaseUn`.
*   **JustReachedHome**: Handles evasion: marks FAIL, resets scene, and plays freed emote.
*   **SituationInitiale**: Prepares Phase 1: clears adds, sets up Orb trigger, respawns guards, and teleports Razorgore.
*   **EvadeTroops**: Forces nearby adds (Legionnaires, Mages, Dragonspawn) to evade/despawn.
*   **UpdateAI**: Main loop: handles possession state, initialization, unreachable victim summoning (Patch 1.8+), spell rotation, and add evasion in Phase 2.
*   **GetAI_boss_razorgore**: Factory function returning a new `boss_razorgoreAI` instance.
*   **trigger_orb_of_commandAI**: Initializes the Orb AI, retrieves instance data, and calls `Reset`.
*   **Reset#2**: Resets Orb timers, phase flags, and possessor GUID.
*   **PhaseSwitch**: Transitions to Phase 2: makes adds flee, disables Orb interaction, restores Razorgore's threat/movement, and forces him to attack the former possessor.
*   **PopAdd**: Spawns adds based on type limits and random coordinates, setting them to attack Razorgore.
*   **UpdateAI#2**: Manages combat start, possession mechanics (threat transfer, health reduction, movement stop), post-possession restoration, and periodic add spawning.
*   **GetAI_trigger_orb_of_command**: Factory function returning a new `trigger_orb_of_commandAI` instance.
*   **AddSC_boss_razorgore**: Registers both AI scripts with the server.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_razorgore

*Source:* boss_razorgore.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_razorgoreAI | ctor | CreatureAI/SetUseAiAtControl, ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | CreatureAI/SetCombatMovement | — | — |
| SpellHitTarget | method | ThreatManager/modifyThreatPercent#2, Unit.Main/GetThreatManager | — | — |
| AttackStart | method | CreatureAI/AttackStart, Unit.Main/Attack, Unit.Main/GetCharmInfo, Unit.Main/IsCommandAttack, Unit.Main/SetIsCommandAttack, WorldObject.Object/IsValidAttackTarget | — | — |
| EnterCombat | method | Creature.Main/SetInCombatWithZone, InstanceData/GetData64, InstanceData/SetData, Map.Main/GetCreature, ObjectGuid/ObjectGuid#5, WorldObject.Object/GetMap, WorldObject.Object/SetUInt32Value, WorldObject.Object/SetUInt64Value | — | — |
| MortPhaseUn | method | Creature.Main/Respawn, InstanceData/SetData, Map.Main/GetPlayers, SpellCaster/CastSpell#2, Unit.Main/IsAlive, WorldObject.Object/GetMap | — | — |
| JustDied | method | InstanceData/GetData64, InstanceData/SetData | — | — |
| JustReachedHome | method | InstanceData/SetData, ScriptMgr/DoScriptText | — | — |
| SituationInitiale | method | Creature.Main/AI, Creature.Main/GetRespawnCoord, Creature.Main/Respawn, CreatureAI/EnterEvadeMode, GridSearchers/GetCreatureListWithEntryInGrid, InstanceData/GetData, InstanceData/GetData64, Map.Main/GetCreature, Map.Main/GetGameObject, Object/GetEntry, Object/GetObjectGuid, ObjectGuid/ObjectGuid#5, Unit.Main/IsAlive, Unit.Main/NearTeleportTo, Unit.Main/SetDisplayId, WorldObject.Object/DeleteLater, WorldObject.Object/GetMap, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag, WorldObject.Object/SetUInt32Value, WorldObject.Object/SetUInt64Value, WorldObject.Object/SummonCreature#2 | — | — |
| EvadeTroops | method | Creature.Main/AI, CreatureAI/EnterEvadeMode, GridSearchers/GetCreatureListWithEntryInGrid, Unit.Main/IsAlive | — | — |
| UpdateAI | method | Creature.Main/TryToCast#2, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, InstanceData/GetData64, shared_Util/urand, Unit.Main/CantPathToVictim, Unit.Main/GetVictim, Unit.Main/HasUnitState, Unit.Main/SelectHostileTarget, World/GetWowPatch | — | — |
| GetAI_boss_razorgore | function | — | — | — |
| trigger_orb_of_commandAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset#2 | method | ObjectGuid/Clear | — | — |
| PhaseSwitch | method | Creature.Main/AI, Creature.Main/SetHomePosition, Creature.Main/SetInCombatWithZone, Creature.MotionMaster/MoveChase, CreatureAI/AttackStart, CreatureAI/EnterEvadeMode, CreatureAI/SetCombatMovement, GridSearchers/GetCreatureListWithEntryInGrid, InstanceData/GetData64, Map.Main/GetCreature, Map.Main/GetGameObject, Map.Main/GetUnit, ObjectGuid/ObjectGuid#5, ScriptedAI/DoResetThreat, Unit.Main/AddThreat, Unit.Main/GetMotionMaster, Unit.Main/IsAlive, WorldObject.Object/DeleteLater, WorldObject.Object/GetMap, WorldObject.Object/MonsterTextEmote#2, WorldObject.Object/SetFlag, WorldObject.Object/SetUInt32Value, WorldObject.Object/SetUInt64Value | — | — |
| PopAdd | method | Creature.Main/AI, Creature.Main/SetInCombatWithZone, CreatureAI/AttackStart, GridSearchers/GetCreatureListWithEntryInGrid, InstanceData/GetData64, Map.Main/GetCreature, ObjectGuid/ObjectGuid#5, shared_Util/urand, Unit.Main/AddThreat, WorldObject.Object/GetMap, WorldObject.Object/SummonCreature#2 | — | — |
| UpdateAI#2 | method | Creature.Main/AI, Creature.Main/SetInCombatWithZone, Creature.MotionMaster/Initialize, Creature.MotionMaster/MoveChase, CreatureAI/AttackStart, CreatureAI/EnterEvadeMode, CreatureAI/SetCombatMovement, GridSearchers/GetCreatureListWithEntryInGrid, InstanceData/GetData, InstanceData/GetData64, Map.Main/GetCreature, Map.Main/GetUnit, Object/GetEntry, ObjectGuid/Clear, ObjectGuid/ObjectGuid#5, ObjectGuid/operator!, ScriptedAI/DoResetThreat, ThreatManager/getThreat, ThreatManager/isThreatListEmpty, ThreatManager/modifyThreatPercent#2, Unit.Main/AddThreat, Unit.Main/GetCharmerGuid, Unit.Main/GetMaxHealth, Unit.Main/GetMotionMaster, Unit.Main/GetThreatManager, Unit.Main/HasAura#2, Unit.Main/IsDead, Unit.Main/IsInCombat, Unit.Main/RemoveAurasDueToSpell, Unit.Main/SetMaxHealth, Unit.Main/StopMoving, WorldObject.Object/GetMap, WorldObject.Object/SetUInt32Value, WorldObject.Object/SetUInt64Value | — | — |
| GetAI_trigger_orb_of_command | function | — | — | — |
| AddSC_boss_razorgore | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
