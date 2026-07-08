# boss_grobbulus

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_grobbulus

**Purpose & Responsibilities**
This unit implements the AI and spell mechanics for **Grobbulus**, a boss in the Naxxramas raid instance (`naxxramas.h`). It defines four script components:
1.  **`boss_grobbulusAI`**: Controls combat phases, ability rotation (Mutating Injection, Poison Cloud, Slime Spray, Berserk), and instance state reporting.
2.  **`GrobbulusMutatingInjectionScript`**: Handles the removal of the Mutating Injection debuff, triggering Mutagen Explosion and Poison Cloud.
3.  **`GrobbulusCloudPoisonScript`**: Dynamically scales the radius of the Poison Cloud spell based on the duration of a passive aura.
4.  **`GrobbulusMutagenExplosionScript`**: Adjusts Mutagen Explosion damage based on whether the preceding injection was dispelled or expired.

The unit uses `EventMap` for scheduling and interacts with `instance_naxxramas` for state management. It accesses no database tables.

## Member-by-Member Behavior

### Boss AI (`boss_grobbulusAI`)

*   **Initialization & State**:
    *   **`boss_grobbulusAI` (ctor)**: Retrieves `instance_naxxramas` data via `WorldObject.Object/GetInstanceData` and calls `Reset`.
    *   **`Reset`**: Clears `EventMap/Reset` and sets `m_uiSlimeStreamTimer` to 5000ms.
    *   **`Aggro`**: Sets instance data to `IN_PROGRESS` via `instance_naxxramas.Main/SetData`. Schedules initial events for Mutating Injection, Poison Cloud, Slime Spray, and Berserk using `EventMap/ScheduleEvent`.
    *   **`JustDied`**: Sets instance data to `DONE`.
    *   **`JustReachedHome`**: Sets instance data to `FAIL`.

*   **Ability Logic**:
    *   **`INJECTION_CD`**: Calculates cooldown for Mutating Injection. Returns 12s for the initial cast. Subsequently, uses `Unit.Main/GetHealthPercent`: if >30%, returns `urand(7000, 13000)`; if ≤30%, returns `urand(3000, 7000)`.
    *   **`DoCastMutagenInjection`**: Selects a target from the threat list (`ThreatManager/getThreatList`) who is a player (`Object/ToPlayer`) and lacks `SPELL_MUTATING_INJECTION` (`Unit.Main/HasAura`). Prevents casting if `SpellCaster/IsNonMeleeSpellCasted` is true. Casts via `CreatureAI/DoCastSpellIfCan`.
    *   **`SpellHitTarget`**: If `SPELL_SLIME_SPRAY` hits a player (`Object/GetTypeId`), summons `NPC_FALLOUT_SLIME` at the target's position (`WorldObject.Object/GetPositionX/Y/Z`) with `TEMPSUMMON_TIMED_DESPAWN_OUT_OF_COMBAT`. Forces combat via `Creature.Main/SetInCombatWithZone`.
    *   **`UpdateSlimeStream`**: If the victim (`Unit.Main/GetVictim`) is in melee range (`Unit.Main/CanReachWithMeleeAutoAttack`), resets timer. Otherwise, decrements timer; if expired, casts `SPELL_SLIME_STREAM` via `CreatureAI/DoCastSpellIfCan` and resets timer to 1500ms.
    *   **`UpdateAI`**: Validates target via `Unit.Main/SelectHostileTarget` and home bounds via `instance_naxxramas.Main/HandleEvadeOutOfHome`. Updates `UpdateSlimeStream` and `EventMap/Update`. Executes events:
        *   `EVENT_MUTATING_INJECTION`: Calls `DoCastMutagenInjection`. Repeats with `INJECTION_CD(false)` on success, or 100ms on failure.
        *   `EVENT_POISON_CLOUD`: Casts `SPELL_POISON_CLOUD`. Repeats with `POISONCLOUD_CD()` on success, or 100ms on failure.
        *   `EVENT_SLIME_SPRAY`: Casts `SPELL_SLIME_SPRAY` on victim. Repeats with `SLIMESPRAY_CD(false)` on success, or 100ms on failure.
        *   `EVENT_BERSERK`: Casts `SPELL_BERSERK`. Retries in 100ms on failure.
    *   Calls `CreatureAI/DoMeleeAttackIfReady`.

*   **Helpers**:
    *   **`POISONCLOUD_CD`**: Returns fixed 15000ms.
    *   **`SLIMESPRAY_CD`**: Returns `urand(20000, 30000)` if initial, else `urand(30000, 35000)`.
    *   **`GetAI_boss_grobbulus`**: Factory function for `boss_grobbulusAI`.

### Spell & Aura Scripts

*   **`GrobbulusMutatingInjectionScript`**:
    *   **`OnBeforeApply`**: On removal (`apply` false), checks `Aura/GetRemoveMode`. If dispelled, casts `SPELL_MUTAGEN_EXPLOSION` via `SpellCaster/CastSpell` without trigger info. Otherwise, casts with trigger info. Always casts `SPELL_POISON_CLOUD` on the target.
    *   **`GetScript_GrobbulusMutatingInjection`**: Factory function.

*   **`GrobbulusCloudPoisonScript`**:
    *   **`OnSetTargetMap`**: Retrieves passive aura `28158` via `Unit.Main/GetSpellAuraHolder`. Calculates radius: `18.0f / maxDur * currTick + 2`, where `currTick` is derived from `SpellAuraHolder/GetAuraMaxDuration` and `SpellAuraHolder/GetAuraDuration`.
    *   **`GetScript_GrobbulusCloudPoison`**: Factory function.

*   **`GrobbulusMutagenExplosionScript`**:
    *   **`OnEffectExecute`**: If `spell->m_triggeredBySpellInfo` is true (expired), multiplies damage by 1.5. Else (dispelled), divides by 1.5.
    *   **`GetScript_GrobbulusMutagenExplosion`**: Factory function.

*   **`AddSC_boss_grobbulus`**: Registers all scripts via `Script/Script` and `ScriptMgr/RegisterSelf`. Called by `ScriptLoader/AddScripts`.

## Cross-Unit Boundaries

*   **`instance_naxxramas`**: `boss_grobbulusAI` calls `SetData` to report `IN_PROGRESS`, `DONE`, or `FAIL`. Calls `HandleEvadeOutOfHome` to validate position.
*   **`ScriptedAI`**: Base class for `boss_grobbulusAI`, providing `DoCastSpellIfCan`, `DoMeleeAttackIfReady`, and standard hooks.
*   **`EventMap`**: Used by `boss_grobbulusAI` for scheduling (`ScheduleEvent`, `Repeat`) and execution (`Update`, `ExecuteEvent`, `Reset`).
*   **`Unit`/`Creature`/`Player`**: `boss_grobbulusAI` accesses health, threat lists, auras, positions, and summoning capabilities. Aura/Spell scripts access caster/target info and cast spells.
*   **`shared_Util`**: `urand` used for randomizing cooldowns and target selection.

## Data Model

No database tables are accessed.

## Notable Implementation Details

*   **Health-Based Injection Frequency**: `INJECTION_CD` reduces cooldown below 30% health, increasing late-fight pressure.
*   **Debuff Spreading**: `DoCastMutagenInjection` explicitly avoids targets with the existing aura, forcing distribution.
*   **Dynamic Cloud Radius**: `GrobbulusCloudPoisonScript` grows the cloud from 2 to ~20 yards based on passive aura duration, requiring aura `28158` to be present.
*   **Explosion Damage Hack**: `GrobbulusMutagenExplosionScript` uses `m_triggeredBySpellInfo` to differentiate dispel (lower damage) from expiration (higher damage), as noted in comments.
*   **Slime Stream Gap-Closer**: `UpdateSlimeStream` only casts when the victim is out of melee range, prioritizing melee attacks otherwise.
*   **Fallback Retries**: Failed casts in `UpdateAI` retry after 100ms to handle transient failures.

## Member Reference

*   **`POISONCLOUD_CD`**: Static function returning fixed 15000ms cooldown.
*   **`SLIMESPRAY_CD`**: Static function returning randomized cooldown (20–30s initial, 30–35s subsequent) via `shared_Util/urand`.
*   **`boss_grobbulusAI`**: Constructor initializing instance data and calling `Reset`.
*   **`INJECTION_CD`**: Method calculating cooldown based on initial cast status and health percentage (>30% vs ≤30%).
*   **`Reset`**: Method clearing event map and setting Slime Stream timer to 5000ms.
*   **`Aggro`**: Method scheduling initial abilities and setting instance state to `IN_PROGRESS`.
*   **`JustDied`**: Method setting instance state to `DONE`.
*   **`JustReachedHome`**: Method setting instance state to `FAIL`.
*   **`DoCastMutagenInjection`**: Method selecting a player without the injection aura from the threat list and casting the spell.
*   **`SpellHitTarget`**: Method summoning a Fallout Slime at the target's position when Slime Spray hits a player.
*   **`UpdateSlimeStream`**: Method managing Slime Stream casting based on melee range and timer.
*   **`UpdateAI`**: Main loop processing events, updating Slime Stream, and performing melee attacks.
*   **`GetAI_boss_grobbulus`**: Factory function for `boss_grobbulusAI`.
*   **`OnBeforeApply`**: Aura script method triggering Mutagen Explosion and Poison Cloud on injection removal.
*   **`GetScript_GrobbulusMutatingInjection`**: Factory function for the injection aura script.
*   **`OnSetTargetMap`**: Spell script method calculating dynamic Poison Cloud radius based on passive aura duration.
*   **`GetScript_GrobbulusCloudPoison`**: Factory function for the cloud poison spell script.
*   **`OnEffectExecute`**: Spell script method adjusting Mutagen Explosion damage based on trigger source.
*   **`GetScript_GrobbulusMutagenExplosion`**: Factory function for the explosion spell script.
*   **`AddSC_boss_grobbulus`**: Function registering all Grobbulus scripts with the script manager.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_grobbulus

*Source:* boss_grobbulus.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| POISONCLOUD_CD | function | — | — | — |
| SLIMESPRAY_CD | function | shared_Util/urand | — | — |
| boss_grobbulusAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| INJECTION_CD | method | shared_Util/urand, Unit.Main/GetHealthPercent | — | — |
| Reset | method | EventMap/Reset | — | — |
| Aggro | method | EventMap/ScheduleEvent#3, instance_naxxramas.Main/SetData | — | — |
| JustDied | method | instance_naxxramas.Main/SetData | — | — |
| JustReachedHome | method | instance_naxxramas.Main/SetData | — | — |
| DoCastMutagenInjection | method | CreatureAI/DoCastSpellIfCan, Object/ToPlayer, shared_Util/urand, SpellCaster/IsNonMeleeSpellCasted, ThreatManager/getThreatList, Unit.Main/GetThreatManager, Unit.Main/HasAura#2 | — | — |
| SpellHitTarget | method | Creature.Main/SetInCombatWithZone, Object/GetTypeId, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SummonCreature#2 | — | — |
| UpdateSlimeStream | method | CreatureAI/DoCastSpellIfCan, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/GetVictim | — | — |
| UpdateAI | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, EventMap/ExecuteEvent, EventMap/Repeat#3, EventMap/Update, instance_naxxramas.Main/HandleEvadeOutOfHome, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_boss_grobbulus | function | — | — | — |
| OnBeforeApply | method | Aura/GetCaster, Aura/GetRemoveMode, Aura/GetTarget, SpellCaster/CastSpell#2 | — | — |
| GetScript_GrobbulusMutatingInjection | function | — | — | — |
| OnSetTargetMap | method | SpellAuraHolder/GetAuraDuration, SpellAuraHolder/GetAuraMaxDuration, Unit.Main/GetSpellAuraHolder#2 | — | — |
| GetScript_GrobbulusCloudPoison | function | — | — | — |
| OnEffectExecute | method | Spell.Main/GetUnitTarget | — | — |
| GetScript_GrobbulusMutagenExplosion | function | — | — | — |
| AddSC_boss_grobbulus | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
