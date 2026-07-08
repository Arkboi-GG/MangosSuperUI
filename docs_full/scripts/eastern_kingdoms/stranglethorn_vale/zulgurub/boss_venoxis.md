# boss_venoxis

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_venoxis

## Purpose & Responsibilities

`boss_venoxis` implements the AI for **High Priest Venoxis**, a Zul'Gurub raid boss. It manages a two-phase encounter: Phase 1 involves holy magic and self-healing; Phase 2 (triggered at 50% health) transforms the boss into a serpent, doubling its scale and switching to poison-based attacks and add summoning. The unit tracks summoned Razzashi Cobras for cleanup on reset/evasion, enforces Z-coordinate bounds to prevent pull exploits, and updates the instance state via `ScriptedInstance`.

## Member-by-Member Behavior

### Lifecycle and State

**`boss_venoxisAI` (Constructor)**
Initializes the AI by retrieving the `ScriptedInstance` pointer and storing the creature's default scale in `m_fDefaultSize`. Calls `Reset()` to initialize timers and state.

**`Reset`**
Resets all ability timers to base values, clears the `bFrenzy` flag, and restores the creature's scale to `m_fDefaultSize`. Calls `Creature.Main/ResetStats` to restore health/mana. Iterates `lAddsGUIDs` to find and remove any lingering summoned creatures via `WorldObject.Object/AddObjectToRemoveList`.

**`Aggro`**
Calls `InstanceData/SetData` to mark the encounter as `IN_PROGRESS`.

**`EnterEvadeMode`**
Searches for Razzashi Cobras (entry 11373) in the grid using `GridSearchers/GetCreatureListWithEntryInGrid#2` and forces their despawn via `Creature.Main/ForcedDespawn`. Delegates to `ScriptedAI/EnterEvadeMode`.

**`JustReachedHome`**
Searches for Razzashi Cobras in the grid. If any are found and dead (`Unit.Main/IsAlive` returns false), it calls `Creature.Main/Respawn`. Updates instance state to `NOT_STARTED` via `InstanceData/SetData`.

**`JustDied`**
Plays death text via `ScriptMgr/DoScriptText`. Casts `SPELL_POISON_CLOUD` and `SPELL_HAKKAR_POWER_DOWN` on itself. Restores scale to `m_fDefaultSize`. Updates instance state to `DONE` via `InstanceData/SetData`.

### Combat Logic

**`UpdateAI`**
The main update loop:
1.  **Validation:** Returns if no victim or if casting a non-melee spell (`SpellCaster/IsNonMeleeSpellCasted`).
2.  **Anti-Exploit:** Forces evasion if Z-position is outside [27.0, 43.0] (`WorldObject.Object/GetPositionZ`).
3.  **Phase Transition:** At <50% health, interrupts spells, casts `SPELL_SNAKE_FORM`, doubles scale, resets threat (`ScriptedAI/DoResetThreat`), and sets `m_bPhaseTwo = true`.
4.  **Phase 1:** Manages timers for `SPELL_HOLY_NOVA`, `SPELL_DISPELL`, `SPELL_HOLY_FIRE`, `SPELL_RENEW`, and `SPELL_HOLY_WRATH`. Targets are usually the victim or self.
5.  **Phase 2:** Manages timers for `SPELL_POISON_CLOUD`, `SPELL_TRASH`, `SPELL_VENOMSPIT` (random target), and `SPELL_PARASITIC`. `SPELL_PARASITIC` summons a cobra (entry 14884) via `Unit.Main/SummonCreatureAndAttack` and stores its GUID in `lAddsGUIDs`.
6.  **Frenzy:** At <20% health, casts `SPELL_FRENZY` if not present.
7.  **Melee:** Calls `CreatureAI/DoMeleeAttackIfReady`.

### Registration

**`GetAI_boss_venoxis`**
Factory function returning a new `boss_venoxisAI` instance.

**`AddSC_boss_venoxis`**
Creates a `Script` object, assigns `GetAI_boss_venoxis`, and registers it via `ScriptMgr/RegisterSelf`. Called by `ScriptLoader/AddScripts`.

## Cross-Unit Boundaries

*   **Instance State:** `Aggro`, `JustReachedHome`, and `JustDied` call `InstanceData/SetData` to update encounter progress.
*   **Entity Management:** `Reset` and `EnterEvadeMode` use `GridSearchers/GetCreatureListWithEntryInGrid#2` to find adds. `Reset` removes them via `WorldObject.Object/AddObjectToRemoveList`; `EnterEvadeMode` despawns them via `Creature.Main/ForcedDespawn`; `JustReachedHome` respawns dead ones via `Creature.Main/Respawn`.
*   **Combat Mechanics:** `UpdateAI` uses `CreatureAI/DoCastSpellIfCan` for spells, `Creature.Main/SelectAttackingTarget` for random targets, and `Unit.Main/SummonCreatureAndAttack` for adds. `SpellCaster/InterruptNonMeleeSpells` and `SpellCaster/IsNonMeleeSpellCasted` manage casting states.
*   **Visuals/Audio:** `ScriptMgr/DoScriptText` handles speech; `WorldObject.Object/SetFloatValue` handles scale changes.
*   **Core AI:** Inherits from `ScriptedAI`, using `ScriptedAI/EnterEvadeMode` and `ScriptedAI/DoResetThreat`.

## Data Model

No database tables are accessed directly. Creature entries (11373, 14884) and spell IDs are hardcoded constants.

## Notable Implementation Details

*   **Z-Bound Exploit Prevention:** Hardcoded Z-check (27.0–43.0) in `UpdateAI` forces evasion if the boss is pulled out of bounds.
*   **Add Tracking:** `lAddsGUIDs` stores GUIDs of summoned cobras to ensure cleanup on reset, preventing orphaned entities.
*   **Scale Persistence:** Default scale is saved in the constructor to correctly restore size on reset/death after Phase 2 doubling.
*   **Hakkar Power:** `JustDied` casts `SPELL_HAKKAR_POWER_DOWN`, linking this encounter to the raid-wide Hakkar mechanic.

## Member Reference

**`boss_venoxisAI`**
Constructor initializing instance pointer, default scale, and calling `Reset()`.

**`Reset`**
Resets timers, stats, and scale; removes lingering adds from `lAddsGUIDs`.

**`Aggro`**
Sets instance data to `IN_PROGRESS`.

**`EnterEvadeMode`**
Despawns all Razzashi Cobras in grid; calls parent evade.

**`JustReachedHome`**
Respawns dead Razzashi Cobras in grid; sets instance data to `NOT_STARTED`.

**`JustDied`**
Plays death text, casts poison/Hakkar spells, restores scale, sets instance data to `DONE`.

**`UpdateAI`**
Main loop: validates target/Z-bounds, handles phase transition at 50%, manages Phase 1/2 spell timers, summons adds, applies Frenzy at 20%, and performs melee attacks.

**`GetAI_boss_venoxis`**
Returns new `boss_venoxisAI` instance.

**`AddSC_boss_venoxis`**
Registers the script with the engine.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_venoxis

*Source:* boss_venoxis.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_venoxisAI | ctor | Object/GetFloatValue, ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | Creature.Main/ResetStats, Map.Main/GetCreature, WorldObject.Object/AddObjectToRemoveList, WorldObject.Object/GetMap, WorldObject.Object/SetFloatValue | — | — |
| Aggro | method | InstanceData/SetData | — | — |
| EnterEvadeMode | method | Creature.Main/ForcedDespawn, GridSearchers/GetCreatureListWithEntryInGrid#2, ScriptedAI/EnterEvadeMode | — | — |
| JustReachedHome | method | Creature.Main/Respawn, GridSearchers/GetCreatureListWithEntryInGrid#2, InstanceData/SetData, Unit.Main/IsAlive | — | — |
| JustDied | method | InstanceData/SetData, ScriptMgr/DoScriptText, SpellCaster/CastSpell#2, WorldObject.Object/SetFloatValue | — | — |
| UpdateAI | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Object/GetObjectGuid, ScriptedAI/DoResetThreat, ScriptMgr/DoScriptText, shared_Util/urand, SpellCaster/CastSpell#2, SpellCaster/InterruptNonMeleeSpells, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/GetHealthPercent, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, Unit.Main/SummonCreatureAndAttack, WorldObject.Object/GetPositionZ, WorldObject.Object/SetFloatValue | — | — |
| GetAI_boss_venoxis | function | — | — | — |
| AddSC_boss_venoxis | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
