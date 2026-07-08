# boss_arlokk

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_arlokk

**Purpose & Responsibilities**  
This unit implements the AI and script hooks for the Zul’Gurub boss **Arlokk** and her summoned **Zulian Prowlers**. It handles:
- The Gong of Bethekk interaction that starts the encounter.
- Arlokk’s two-phase combat: a troll phase with marking and shadow magic, followed by a panther phase with enhanced melee and threat manipulation.
- Summoning and targeting logic for Zulian Prowlers, which focus the marked player.
- State synchronization with the instance data (`TYPE_ARLOKK`) and environmental objects (force field door, gong respawn).

No database tables are accessed by this unit.

---

## Member-by-Member Behavior

### Encounter Initiation
- **`GOHello_go_gong_of_bethekk`**: Triggered when a player interacts with the Gong of Bethekk. It checks the instance state via `InstanceData/GetData`. If Arlokk is not already done or in progress, it sets the state to `IN_PROGRESS` via `InstanceData/SetData`. Returns `false` to allow normal interaction feedback.

### Arlokk’s AI (`boss_arlokkAI`)
- **`boss_arlokkAI`**: Initializes the AI, retrieves the instance data pointer, and calls `Reset`.
- **`Reset`**: Restores all timers to their initial values, resets phase flags (`m_bIsPhaseTwo`, `m_bIsVanished`), clears the marked GUID, removes the `NOT_SELECTABLE` flag, resets stats, and sets scale to 1.0.
- **`Aggro`**: Plays the aggro speech, marks the creature as in combat, and opens the force field door (`GO_ARLOKK_FORCE_FIELD`) using `GameObject/UseDoorOrButton`.
- **`JustReachedHome`**: If the instance state is not `DONE`, it resets it to `NOT_STARTED`. Resets the force field door, respawns the gong, and forces the creature to despawn (since Arlokk is summoned).
- **`JustDied`**: Plays death speech, removes the panther transform spell, leaves vanish mode, resets scale, sets instance state to `DONE`, resets the force field door, and casts `SPELL_HAKKAR_POWER_DOWN` on herself to remove a Hakkar Power stack.
- **`DoSummonSinglePhanter`**: Summons a Zulian Prowler at specified coordinates. If a target is provided and the prowler has an AI, it commands the prowler to attack that target.
- **`DoSummonPhanters`**: Retrieves the marked unit from the map and summons two prowlers at fixed coordinates, targeting the marked unit.
- **`JustSummoned`**: Determines the target for the newly summoned prowler. If the marked unit is alive, the prowler attacks them. Otherwise, it selects a random hostile target. If no targets exist, the prowler is removed and Arlokk evades. Increments the summon count.
- **`UpdateAI`**: The main combat loop. It manages:
  - **Phase 1 (Troll)**: Casts Shadow Word: Pain, Backstab, and Mark. Mark selects a random player, removes the old mark, applies the new mark, and records the GUID. Logs an error if no valid player is found.
  - **Phase 2 (Panther)**: Casts Thrash, Ravage, Gouge (which reduces victim threat by 80%), and Tourbillon.
  - **Summoning**: Continues summoning prowlers every 5 seconds until the max count (30) is reached.
  - **Phase Transitions**: 
    - After 35–50 seconds, Arlokk vanishes (invisible model).
    - After 35–50 seconds of vanish, she reappears as a panther, scales up to 1.7, increases melee damage by 35%, and ambushes a random target with Backstab.
    - After 45 seconds in panther form, she reverts to troll form.
  - **Melee**: Performs melee attacks when not in vanish or phase transition.

### Zulian Prowler AI (`mob_prowlerAI`)
- **`mob_prowlerAI`**: Initializes timers and the Arlokk GUID cache.
- **`GetArlokkAI`**: Finds the nearest Arlokk creature, caches its GUID, and returns its AI pointer if alive. Used to access the marked target GUID.
- **`Reset#2`**: Applies a spell (22766), initializes thrash and update target timers.
- **`JustDied#2`**: Decrements Arlokk’s summon count if Arlokk’s AI is accessible.
- **`UpdateAI#2`**: 
  - Every 2 seconds, it checks for the marked target via Arlokk’s AI. If Arlokk is dead or inaccessible, the prowler removes itself. Otherwise, it attacks the marked target and reapplies spell 22766. It also reduces threat against its current victim by 100%.
  - Casts Thrash on a timer.
  - Performs melee attacks.

### Script Registration
- **`GetAI_boss_arlokk`** and **`GetAI_mob_prowler`**: Factory functions that instantiate the respective AI classes.
- **`AddSC_boss_arlokk`**: Registers the gong script, Arlokk’s AI, and the prowler’s AI with the script manager. Called by `ScriptLoader/AddScripts`.

---

## Cross-Unit Boundaries

- **Instance Data**: `boss_arlokkAI` reads and writes `TYPE_ARLOKK` via `InstanceData/GetData` and `InstanceData/SetData` to synchronize encounter state.
- **Game Objects**: `boss_arlokkAI` interacts with `GO_ARLOKK_FORCE_FIELD` (door) and `GO_ARLOKK_GONG` (gong) via `GameObject/UseDoorOrButton`, `GameObject/ResetDoorOrButton`, and `GameObject/Respawn`.
- **Script Manager**: Uses `ScriptMgr/DoScriptText` for speech events.
- **Logging**: `boss_arlokkAI::UpdateAI` logs errors via `Log/Main/Out` if it cannot acquire a target for the Mark spell.
- **Threat Management**: `boss_arlokkAI` modifies threat via `ThreatManager/getThreat` and `ThreatManager/modifyThreatPercent#2`. `mob_prowlerAI` uses `ScriptedAI/DoGetThreat` and `ScriptedAI/DoModifyThreatPercent`.
- **Summoning**: `boss_arlokkAI` summons prowlers via `WorldObject/Object/SummonCreature#2` and manages their AI via `CreatureAI/AttackStart`. `mob_prowlerAI` accesses Arlokk’s AI via `Map/Main/GetCreature` and `Creature/Main/AI`.

---

## Data Model

This unit does not access any database tables. All state is managed in-memory via instance data and creature/game object interactions.

---

## Notable Implementation Details

- **Phase Logic**: Arlokk’s phases are managed by timers and boolean flags. The transition from troll to panther involves a vanish period, during which she is invisible. Upon reappearing, she gains increased damage and size.
- **Mark Mechanic**: The Mark spell targets a random player. The old mark is removed before applying the new one. If no valid player is found, an error is logged.
- **Prowler Targeting**: Prowlers always attack the marked player. If the marked player dies or is invalid, they select a random target. If no targets exist, they are removed.
- **Threat Manipulation**: Arlokk’s Gouge spell reduces victim threat by 80%. Prowlers reduce threat against their current victim by 100% to encourage switching to the marked target.
- **Summon Limit**: Arlokk stops summoning prowlers after 30 are active. Each prowler decrementing the count on death ensures the limit is respected.
- **Error Handling**: If Arlokk cannot find a valid player to mark, it logs an error. If prowlers cannot find Arlokk, they remove themselves.

---

## Member Reference

- **GOHello_go_gong_of_bethekk**: Handles gong interaction, sets instance state to `IN_PROGRESS` if not already started.
- **boss_arlokkAI**: Initializes AI, retrieves instance data, calls `Reset`.
- **Reset**: Resets timers, phase flags, marked GUID, flags, stats, and scale.
- **Aggro**: Plays aggro speech, sets combat state, opens force field door.
- **JustReachedHome**: Resets instance state, door, gong, and despawns Arlokk.
- **JustDied**: Plays death speech, removes transforms, resets scale, sets instance state to `DONE`, resets door, casts Hakkar Power Down.
- **DoSummonSinglePhanter**: Summons a prowler and commands it to attack a target.
- **DoSummonPhanters**: Summons two prowlers targeting the marked unit.
- **JustSummoned**: Assigns target to summoned prowler (marked unit or random), increments count, or removes if no targets.
- **UpdateAI**: Main combat loop managing spells, phases, summoning, and transitions.
- **mob_prowlerAI**: Initializes prowler AI timers and GUID cache.
- **GetArlokkAI**: Finds and returns Arlokk’s AI pointer.
- **Reset#2**: Applies spell 22766, initializes prowler timers.
- **JustDied#2**: Decrements Arlokk’s summon count.
- **UpdateAI#2**: Manages prowler targeting, threat reduction, thrash casting, and melee.
- **GetAI_boss_arlokk**: Factory function for Arlokk’s AI.
- **GetAI_mob_prowler**: Factory function for prowler’s AI.
- **AddSC_boss_arlokk**: Registers scripts for gong, Arlokk, and prowlers.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_arlokk

*Source:* boss_arlokk.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GOHello_go_gong_of_bethekk | function | InstanceData/GetData, InstanceData/SetData, WorldObject.Object/GetInstanceData | — | — |
| boss_arlokkAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | Creature.Main/ResetStats, shared_Util/urand, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFloatValue | — | — |
| Aggro | method | Creature.Main/SetInCombatWithZone, GameObject/UseDoorOrButton, ScriptMgr/DoScriptText, WorldObject.Object/FindNearestGameObject | — | — |
| JustReachedHome | method | Creature.Main/ForcedDespawn, GameObject/ResetDoorOrButton, GameObject/Respawn, InstanceData/GetData, InstanceData/SetData, WorldObject.Object/FindNearestGameObject | — | — |
| JustDied | method | GameObject/ResetDoorOrButton, InstanceData/SetData, ScriptedAI/LeaveVanish, ScriptMgr/DoScriptText, SpellCaster/CastSpell#2, Unit.Main/RemoveAurasDueToSpell, WorldObject.Object/FindNearestGameObject, WorldObject.Object/SetFloatValue | — | — |
| DoSummonSinglePhanter | method | Creature.Main/AI, CreatureAI/AttackStart, WorldObject.Object/SummonCreature#2 | — | — |
| DoSummonPhanters | method | Map.Main/GetUnit, ObjectGuid/ObjectGuid#5, WorldObject.Object/GetMap | — | — |
| JustSummoned | method | Creature.Main/AI, Creature.Main/SelectAttackingTarget, CreatureAI/AttackStart, Map.Main/GetUnit, ObjectGuid/ObjectGuid#5, ScriptedAI/EnterEvadeMode, Unit.Main/IsAlive, WorldObject.Object/AddObjectToRemoveList, WorldObject.Object/GetMap | — | — |
| UpdateAI | method | Creature.Main/GetDefaultDamageRange, Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Log.Main/Out, Map.Main/GetUnit, Object/GetGUID, ObjectGuid/ObjectGuid#5, Player.StatSystem/UpdateDamagePhysical, ScriptedAI/Ambush, ScriptedAI/EnterVanish, ScriptedAI/LeaveVanish, ScriptMgr/DoScriptText, shared_Util/urand, SpellCaster/CastSpell#2, ThreatManager/getThreat, ThreatManager/modifyThreatPercent#2, Unit.Main/GetCharmerOrOwnerPlayerOrPlayerItself, Unit.Main/GetThreatManager, Unit.Main/GetVictim, Unit.Main/RemoveAurasDueToSpell, Unit.Main/SelectHostileTarget, Unit.Main/SetBaseWeaponDamage, WorldObject.Object/GetMap, WorldObject.Object/SetFloatValue | — | — |
| mob_prowlerAI | ctor | ScriptedAI/ScriptedAI | — | — |
| GetArlokkAI | method | Creature.Main/AI, Map.Main/GetCreature, Object/GetGUID, ObjectGuid/ObjectGuid#5, Unit.Main/IsAlive, WorldObject.Object/FindNearestCreature, WorldObject.Object/GetMap | — | — |
| Reset#2 | method | CreatureAI/DoCast, shared_Util/urand | — | — |
| JustDied#2 | method | — | — | — |
| UpdateAI#2 | method | Creature.Main/AI, CreatureAI/AttackStart, CreatureAI/DoCast, CreatureAI/DoMeleeAttackIfReady, Map.Main/GetUnit, ObjectGuid/ObjectGuid#5, ScriptedAI/DoGetThreat, ScriptedAI/DoModifyThreatPercent, shared_Util/urand, Unit.Main/GetVictim, WorldObject.Object/AddObjectToRemoveList, WorldObject.Object/GetMap | — | — |
| GetAI_boss_arlokk | function | — | — | — |
| GetAI_mob_prowler | function | — | — | — |
| AddSC_boss_arlokk | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
