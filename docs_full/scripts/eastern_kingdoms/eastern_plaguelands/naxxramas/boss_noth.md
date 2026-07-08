<!-- provenance: verbose -->
# boss_noth

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_noth

## Purpose & Responsibilities

`boss_noth.cpp` implements the AI for **Noth the Plaguebringer**, a raid boss in the Naxxramas instance. The unit manages a multi-phase encounter alternating between ground combat and balcony teleports. Key responsibilities include:

1.  **Phase Management:** Tracking `phaseCounter` to escalate difficulty (add types, balcony duration) and managing transitions via `isOnBalc`.
2.  **Event Scheduling:** Using `EventMap` to orchestrate abilities (Blink, Curse, Warrior summons, Teleports) with randomized intervals.
3.  **Add Spawning:** Complex logic to spawn Warriors, Champions, Guardians, and Constructs in specific patterns, particularly during balcony phases.
4.  **Immunity & Damage Control:** Enforcing damage immunity on the balcony via `DamageTaken` interception and spell auras, and removing it upon return.
5.  **Instance Integration:** Reporting state (`IN_PROGRESS`, `DONE`, `FAIL`) to `instance_naxxramas` and handling out-of-bounds evasion.

The unit also defines `NothCurseOfThePlaguebringerScript` to modify the targeting behavior of Noth’s curse spell, ensuring it hits targets closest to the primary victim.

## Member-by-Member Behavior

### Initialization and State

**`boss_nothAI` (ctor)**
Initializes the AI, casting `pCreature->GetInstanceData()` to `instance_naxxramas*` and storing it in `m_pInstance`. Calls `Reset()` to initialize state.

**`Reset`**
Resets `isOnBalc` to false, `phaseCounter` to 0, `killSayCooldown` to 5000ms, and clears `m_events`.

**`JustReachedHome`**
Reports `FAIL` to `instance_naxxramas`. Cleans up lingering adds (Guardians, Constructs, Champions, Warriors) within 150.0f units by calling `DeleteLater`.

**`JustDied`**
Plays death text via `ScriptMgr/DoScriptText` and reports `DONE` to `instance_naxxramas`.

### Ground Phase Mechanics

**`Aggro`**
Sets combat state. Schedules initial events: `EVENT_CURSE` (8–12s), `EVENT_BLINK` (30–40s), `EVENT_WARRIORS` (10s), `EVENT_TP_BALC` (90s). Plays random aggro text and reports `IN_PROGRESS` to the instance.

**`SpawnWarriorsAndRepeatEvent`**
Summons three Plagued Warriors (SW, NW, NE) using triggered spells. Repeats every 30 seconds. Plays summon text.

**`BlinkAndRepeatEvent`**
Casts `SPELL_CRIPPLE` on the origin location, then a random blink spell (`SPELL_BLINK_1`–`4`). Resets threat, selects a new random target, and starts attack. Repeats in 30–40s.

**`CurseAndRepeatEvent`**
Casts `SPELL_CURSE_PLAGUEBRINGER` on the current victim. Repeats in 50–60s if a victim exists; otherwise repeats in 100ms.

**`KilledUnit`**
If `killSayCooldown` is zero, plays a random slay text and sets cooldown to 5000ms.

**`AttackStart`**
Only initiates attack if `isOnBalc` is false.

**`DamageTaken`**
If `isOnBalc` is true, sets `uiDamage` to 0, enforcing immunity.

### Balcony Phase Mechanics

**`TeleportToBalc`**
Casts `SPELL_TP_BALC`. If failed, retries in 100ms. On success:
- Sets `isOnBalc` to true and resets events.
- Schedules `EVENT_TP_GROUND` (70s + 25s * `phaseCounter`).
- Schedules two `EVENT_BALC_ADDS` events with phase-dependent delays.
- Casts `SPELL_IMMUNE_ALL` on self.
- Stops movement and attacks.

**`TeleportFromBalc`**
Casts `SPELL_TP_CENTER`. If failed, retries in 100ms. On success, resets events and schedules `EVENT_RMV_INVULN` in 2s.

**`OnRemoveVulnerability`**
Triggers after returning from balcony. Sets `isOnBalc` to false, removes immunity aura, resets events, and schedules immediate ground-phase abilities (Blink, Curse, Warriors) with 2–10s delays. Resets threat and selects a new target. Increments `phaseCounter` and schedules the next `EVENT_TP_BALC` (110s for phase 1, 180s for phase 2+).

**`SpawnBalcAdds`**
Plays summon text. Based on `phaseCounter`:
- Phase 0: Summons 4 Champions.
- Phase 1: Summons 4 Champions + 2 Guardians.
- Phase 2+: Summons 4 Champions + 2 Guardians + 3 Constructs.

**`Summon4Champions`**
Selects 4 Champion spells from 10 options to ensure spread:
1. Picks one random spell from Group 1 (indices 0–3), Group 2 (4–7), and Group 3 (8–9).
2. Removes these from the list.
3. Casts the three selected spells.
4. Picks a fourth spell from the remaining 7 and casts it.

**`Summon2Guardians`**
Summons one Guardian from two SW locations and one from either NE or NW locations.

**`Summon3Constructs`**
Summons three Plagued Constructs at hardcoded coordinates with 25s despawn timers.

### Summoning and Cleanup

**`JustSummoned`**
Sets summoned creatures into combat with the zone.

**`SummonedCreatureJustDied`**
Forces the dead add to despawn after 3000ms.

### Main Loop

**`UpdateAI`**
- If not on balcony: Checks for hostile targets and calls `instance_naxxramas.HandleEvadeOutOfHome`.
- If on balcony: If threat list is empty and `EVENT_RMV_INVULN` is >2s away, forces `TeleportFromBalc` to prevent getting stuck.
- Decrements `killSayCooldown`.
- Executes pending events from `EventMap`.
- Performs melee attacks if not on balcony.

### Script Registration and Spell Hooks

**`GetAI_boss_noth`**
Factory function creating `boss_nothAI`.

**`OnSetTargetMap`**
Part of `NothCurseOfThePlaguebringerScript`. Sets `selectClosestTargets = true` for the curse spell.

**`GetScript_NothCurseOfThePlaguebringer`**
Factory function for the curse spell script.

**`AddSC_boss_noth`**
Registers `boss_noth` AI and `spell_noth_curse_of_the_plaguebringer` spell script with `ScriptMgr`.

## Cross-Unit Boundaries

### `instance_naxxramas`
- **Direction:** `boss_nothAI` calls into `instance_naxxramas`.
- **Collaboration:** Reports encounter state (`FAIL`, `IN_PROGRESS`, `DONE`) via `SetData`. Calls `HandleEvadeOutOfHome` to check if Noth has moved out of bounds.

### `ScriptedAI`
- **Direction:** `boss_nothAI` inherits from and calls into `ScriptedAI`.
- **Collaboration:** Uses `DoScriptText`, `DoCastSpellIfCan`, `DoResetThreat`, `DoStopAttack`, and `AttackStart` for standard AI behaviors.

### `EventMap`
- **Direction:** `boss_nothAI` uses `EventMap`.
- **Collaboration:** Manages timing via `ScheduleEvent`, `Repeat`, `Reset`, and `ExecuteEvent`.

### `WorldObject` / `Creature` / `Unit`
- **Direction:** `boss_nothAI` calls into these core classes.
- **Collaboration:**
    - `GetInstanceData`: Retrieves instance pointer.
    - `SetInCombatWithZone`: Engages combat.
    - `SelectAttackingTarget` / `SelectHostileTarget`: Finds targets.
    - `GetVictim`: Gets current target.
    - `GetMotionMaster` / `MoveIdle`: Controls movement.
    - `RemoveAurasDueToSpell`: Removes buffs/debuffs.
    - `SummonCreature`: Spawns adds.
    - `ForcedDespawn` / `DeleteLater`: Cleans up adds.

### `ScriptMgr`
- **Direction:** `boss_nothAI` and `AddSC_boss_noth` call into `ScriptMgr`.
- **Collaboration:** `DoScriptText` plays sounds. `RegisterSelf` registers scripts.

### `shared_Util`
- **Direction:** `boss_nothAI` calls into `shared_Util`.
- **Collaboration:** `urand` generates random integers for timing and spell selection.

### `GridSearchers`
- **Direction:** `boss_nothAI` calls into `GridSearchers`.
- **Collaboration:** `GetCreatureListWithEntryInGrid` finds nearby adds for cleanup.

### `ThreatManager`
- **Direction:** `boss_nothAI` calls into `ThreatManager`.
- **Collaboration:** `isThreatListEmpty` checks for aggro loss to trigger safe teleport.

### `SpellCaster`
- **Direction:** `boss_nothAI` calls into `SpellCaster`.
- **Collaboration:** `CastSpell` applies immunity directly in `TeleportToBalc`.

## Data Model

This unit does not interact with any database tables. All data is managed in-memory.

## Notable Implementation Details

1.  **Dual-Layer Immunity:** Noth is immune on the balcony via `DamageTaken` interception (setting damage to 0) and `SPELL_IMMUNE_ALL` aura. Immunity is removed in `OnRemoveVulnerability`.
2.  **Spread-Out Champion Spawns:** `Summon4Champions` ensures Champions spawn from different arena zones by picking one from each of three predefined groups, then a fourth from the remainder.
3.  **Wipe Safety:** `UpdateAI` checks if the threat list is empty while on the balcony. If so, it forces a teleport back to the ground to prevent Noth from getting stuck in the air.
4.  **Phase Escalation:** `phaseCounter` increments after each balcony return, increasing balcony duration (70s → 95s → 120s) and add complexity (Champions → +Guardians → +Constructs).
5.  **Curse Targeting:** `NothCurseOfThePlaguebringerScript` forces the curse to hit targets closest to the primary victim.
6.  **Hardcoded Construct Spawns:** `Summon3Constructs` uses hardcoded coordinates, unlike spell-based spawns for other adds.
7.  **Kill Say Cooldown:** `killSayCooldown` prevents spamming kill taunts, decrementing in `UpdateAI`.
8.  **Retry Logic:** `TeleportToBalc` and `TeleportFromBalc` retry casting in 100ms if the spell fails.

## Member Reference

**`boss_nothAI`**
Constructor initializing instance data and calling `Reset`.

**`Reset`**
Resets `isOnBalc`, `phaseCounter`, `killSayCooldown`, and `m_events`.

**`JustReachedHome`**
Reports `FAIL` to instance and deletes lingering adds.

**`Aggro`**
Starts combat, schedules initial events, plays aggro text, reports `IN_PROGRESS`.

**`SpawnWarriorsAndRepeatEvent`**
Summons three Warriors, repeats every 30s.

**`BlinkAndRepeatEvent`**
Casts Cripple and Blink, resets threat, selects new target, repeats in 30–40s.

**`CurseAndRepeatEvent`**
Casts Curse on victim, repeats in 50–60s.

**`TeleportToBalc`**
Teleports to balcony, sets immunity, schedules balcony events, stops attacks.

**`TeleportFromBalc`**
Teleports back to center, schedules invulnerability removal.

**`Summon4Champions`**
Spawns 4 Champions using weighted random selection from 10 locations.

**`Summon2Guardians`**
Spawns 2 Guardians at random SW and NE/NW locations.

**`Summon3Constructs`**
Spawns 3 Constructs at hardcoded coordinates.

**`SpawnBalcAdds`**
Calls appropriate summon functions based on phase counter.

**`OnRemoveVulnerability`**
Removes immunity, resets events for ground phase, increments phase counter, schedules next balcony teleport.

**`SummonedCreatureJustDied`**
Forces despawn of dead adds after 3s.

**`JustSummoned`**
Sets summoned adds into combat.

**`KilledUnit`**
Plays kill sound if cooldown allows.

**`JustDied`**
Plays death sound, reports success to instance.

**`AttackStart`**
Initiates attack only if not on balcony.

**`DamageTaken`**
Nullifies damage if on balcony.

**`UpdateAI`**
Main loop: handles evasion, threat checks, event execution, and melee attacks.

**`GetAI_boss_noth`**
Factory function for `boss_nothAI`.

**`OnSetTargetMap`**
Modifies curse spell targeting to select closest targets.

**`GetScript_NothCurseOfThePlaguebringer`**
Factory function for the curse spell script.

**`AddSC_boss_noth`**
Registers the boss AI and spell script with the server.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_noth

*Source:* boss_noth.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_nothAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | EventMap/Reset | — | — |
| JustReachedHome | method | GridSearchers/GetCreatureListWithEntryInGrid, instance_naxxramas.Main/SetData, WorldObject.Object/DeleteLater | — | — |
| Aggro | method | Creature.Main/SetInCombatWithZone, EventMap/ScheduleEvent#2, instance_naxxramas.Main/SetData, ScriptMgr/DoScriptText, shared_Util/urand | — | — |
| SpawnWarriorsAndRepeatEvent | method | CreatureAI/DoCastSpellIfCan, EventMap/Repeat, ScriptMgr/DoScriptText | — | — |
| BlinkAndRepeatEvent | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, EventMap/Repeat, ScriptedAI/DoResetThreat, shared_Util/urand | — | — |
| CurseAndRepeatEvent | method | CreatureAI/DoCastSpellIfCan, EventMap/Repeat, EventMap/Repeat#3, shared_Util/urand, Unit.Main/GetVictim | — | — |
| TeleportToBalc | method | Creature.MotionMaster/MoveIdle, CreatureAI/DoCastSpellIfCan, EventMap/Repeat#3, EventMap/Reset, EventMap/ScheduleEvent#2, ScriptedAI/DoStopAttack, shared_Util/urand, SpellCaster/CastSpell#2, Unit.Main/GetMotionMaster | — | — |
| TeleportFromBalc | method | CreatureAI/DoCastSpellIfCan, EventMap/Repeat#3, EventMap/Reset, EventMap/ScheduleEvent#2 | — | — |
| Summon4Champions | method | CreatureAI/DoCastSpellIfCan, shared_Util/urand | — | — |
| Summon2Guardians | method | CreatureAI/DoCastSpellIfCan, shared_Util/urand | — | — |
| Summon3Constructs | method | WorldObject.Object/SummonCreature#2 | — | — |
| SpawnBalcAdds | method | ScriptMgr/DoScriptText | — | — |
| OnRemoveVulnerability | method | Creature.Main/SelectAttackingTarget, EventMap/Reset, EventMap/ScheduleEvent#2, ScriptedAI/DoResetThreat, shared_Util/urand, Unit.Main/RemoveAurasDueToSpell | — | — |
| SummonedCreatureJustDied | method | Creature.Main/ForcedDespawn | — | — |
| JustSummoned | method | Creature.Main/SetInCombatWithZone | — | — |
| KilledUnit | method | ScriptMgr/DoScriptText, shared_Util/urand | — | — |
| JustDied | method | instance_naxxramas.Main/SetData, ScriptMgr/DoScriptText | — | — |
| AttackStart | method | CreatureAI/AttackStart | — | — |
| DamageTaken | method | — | — | — |
| UpdateAI | method | CreatureAI/DoMeleeAttackIfReady, EventMap/ExecuteEvent, EventMap/GetTimeUntilEvent, EventMap/Update, instance_naxxramas.Main/HandleEvadeOutOfHome, ThreatManager/isThreatListEmpty, Unit.Main/GetThreatManager, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_boss_noth | function | — | — | — |
| OnSetTargetMap | method | — | — | — |
| GetScript_NothCurseOfThePlaguebringer | function | — | — | — |
| AddSC_boss_noth | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
