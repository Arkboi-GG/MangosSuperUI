<!-- provenance: verbose -->
# boss_taerar

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`boss_taerar.cpp` implements the combat AI for the boss **Taerar** and his summoned minions, the **Shades of Taerar**. It defines two classes:

1.  **`boss_taerarAI`**: Inherits from `boss_dragon_of_nightmareAI` (`boss_dragon_of_nightmare`). It manages Taerar’s combat rotation (`Arcane Blast`, `Bellowing Roar`) and a special phase where he stuns himself, becomes unselectable, and summons three Shades. He resumes combat when all Shades die or a 120-second timeout expires.
2.  **`npc_shade_of_taerarAI`**: Inherits from `ScriptedPetAI`. It controls the Shades, which aggressively attack their victims using `Acid Breath` and `Poison Cloud`.

No database tables are accessed.

## Member-by-Member Behavior

### Taerar Boss AI (`boss_taerarAI`)

*   **`boss_taerarAI` (ctor)**: Calls the parent `boss_dragon_of_nightmareAI` constructor and invokes `Reset()`.
*   **`Reset`**: Calls parent `Reset`. Initializes random timers for `Arcane Blast` (11–13s) and `Bellowing Roar` (27–30s). Clears `m_uiShadesTimeoutTimer` and `m_uiShadesDead`.
*   **`Aggro`**: Calls parent `Aggro`. Plays `SAY_TAERAR_AGGRO` via `ScriptMgr::DoScriptText`.
*   **`EnterEvadeMode`**: Calls `DoUnbanish()` to clear stun/unselectable states, then calls parent `EnterEvadeMode`.
*   **`DoSpecialAbility`**: Casts `SPELL_SELF_STUN` on Taerar. If successful, casts three summoning spells (`SPELL_SHADE_OF_TAERAR_LEFT/RIGHT/FRONT`) with `CF_TRIGGERED`, sets `UNIT_FLAG_NOT_SELECTABLE`, plays `SAY_SUMMON_SHADE`, and sets a 120s timeout. Returns `true` on success.
*   **`SummonedCreatureJustDied`**: If the dead creature is `NPC_SHADE_OF_TAERAR`, increments `m_uiShadesDead`. If count reaches 3, calls `DoUnbanish()` and `Creature::RemoveGuardians`.
*   **`DoUnbanish`**: Removes `SPELL_SELF_STUN` aura and `UNIT_FLAG_NOT_SELECTABLE` flag. Resets `m_uiShadesTimeoutTimer` and `m_uiShadesDead` to 0.
*   **`UpdateDragonAI`**: Main update loop. If `m_uiShadesTimeoutTimer` is active, updates leash time, decrements timer, and calls `DoUnbanish()` on expiry; returns `false` to skip normal combat. Otherwise, manages `Arcane Blast` (random non-GM player target, 10–16s) and `Bellowing Roar` (self-cast, 25–28s) timers; returns `true`.

### Shade of Taerar AI (`npc_shade_of_taerarAI`)

*   **`npc_shade_of_taerarAI` (ctor)**: Calls `ScriptedPetAI` constructor, sets `REACT_AGGRESSIVE`, and invokes `Reset()`.
*   **`Reset#2`**: Initializes random timers for `Acid Breath` (10–12s) and `Poison Cloud` (8–15s).
*   **`UpdatePetAI`**: Manages `Acid Breath` (cast on victim, 10–15s) and `Poison Cloud` (cast on self, 15–20s) timers. Calls parent `ScriptedPetAI::UpdatePetAI`.

## Cross-Unit Boundaries

*   **`boss_dragon_of_nightmare`**: Parent class for `boss_taerarAI`. Provides base dragon behavior via `Reset`, `Aggro`, and `EnterEvadeMode`.
*   **`ScriptedPetAI`**: Parent class for `npc_shade_of_taerarAI`. Provides pet movement and targeting logic via `UpdatePetAI`.
*   **`ScriptMgr`**: Used by `boss_taerarAI` to broadcast text/sounds (`DoScriptText`).
*   **`CreatureAI` / `Unit.Main` / `WorldObject.Object`**: Core engine utilities for spell casting (`DoCastSpellIfCan`), flag manipulation (`SetFlag`/`RemoveFlag`), aura removal (`RemoveAurasDueToSpell`), target selection (`SelectAttackingTarget`), and guardian cleanup (`RemoveGuardians`).

## Data Model

This unit does not access any database tables.

## Notable Implementation Details

*   **Self-Stun & Unselectable**: Taerar uses `SPELL_SELF_STUN` and `UNIT_FLAG_NOT_SELECTABLE` to pause combat and prevent targeting during the Shade summoning phase. `DoUnbanish` reverses both.
*   **Timeout Safety**: A 120-second timer (`m_uiShadesTimeoutTimer`) ensures Taerar resumes combat even if Shades are not killed.
*   **Guardian Cleanup**: `Creature::RemoveGuardians` is called when all Shades die to clean up summon relationships.
*   **GM Exclusion**: `Arcane Blast` targets exclude Game Masters (`SELECT_FLAG_PLAYER_NOT_GM`).

## Member Reference

**boss_taerarAI** (ctor): Calls parent `boss_dragon_of_nightmareAI` constructor and `Reset()`.

**Reset**: Calls parent `Reset`; initializes `Arcane Blast` and `Bellowing Roar` timers; clears shade counters.

**Aggro**: Calls parent `Aggro`; plays aggro text via `ScriptMgr::DoScriptText`.

**EnterEvadeMode**: Calls `DoUnbanish()` then parent `EnterEvadeMode`.

**DoSpecialAbility**: Casts self-stun, summons three Shades, sets unselectable flag, plays text, starts 120s timeout.

**SummonedCreatureJustDied**: Increments dead shade count; if 3, calls `DoUnbanish()` and `RemoveGuardians`.

**DoUnbanish**: Removes self-stun aura and unselectable flag; resets shade timers/counters.

**UpdateDragonAI**: Handles special phase timeout or normal `Arcane Blast`/`Bellowing Roar` rotation.

**npc_shade_of_taerarAI** (ctor): Calls `ScriptedPetAI` constructor, sets aggressive react state, calls `Reset()`.

**Reset#2**: Initializes `Acid Breath` and `Poison Cloud` timers.

**UpdatePetAI**: Manages `Acid Breath` and `Poison Cloud` timers; calls parent `UpdatePetAI`.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_taerar

*Source:* boss_taerar.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_taerarAI | ctor | boss_dragon_of_nightmare/boss_dragon_of_nightmareAI | boss_dragon_of_nightmare/GetAI_boss_dragon_of_nightmare | — |
| Reset | method | boss_dragon_of_nightmare/Reset, shared_Util/urand | — | — |
| Aggro | method | boss_dragon_of_nightmare/Aggro, ScriptMgr/DoScriptText | — | — |
| EnterEvadeMode | method | boss_dragon_of_nightmare/EnterEvadeMode | — | — |
| DoSpecialAbility | method | CreatureAI/DoCastSpellIfCan, ScriptMgr/DoScriptText, WorldObject.Object/SetFlag | — | — |
| SummonedCreatureJustDied | method | Object/GetEntry, Unit.Main/RemoveGuardians | — | — |
| DoUnbanish | method | Unit.Main/RemoveAurasDueToSpell, WorldObject.Object/RemoveFlag | — | — |
| UpdateDragonAI | method | Creature.Main/SelectAttackingTarget, Creature.Main/UpdateLeashExtensionTime, CreatureAI/DoCastSpellIfCan, shared_Util/urand | — | — |
| npc_shade_of_taerarAI | ctor | ScriptedPetAI/ScriptedPetAI, Unit.Main/SetReactState | boss_dragon_of_nightmare/GetAI_npc_shade_of_taerar | — |
| Reset#2 | method | shared_Util/urand | — | — |
| UpdatePetAI | method | CreatureAI/DoCastSpellIfCan, ScriptedPetAI/UpdatePetAI, shared_Util/urand, Unit.Main/GetVictim | — | — |
