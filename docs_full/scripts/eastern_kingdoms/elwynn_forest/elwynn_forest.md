# elwynn_forest

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`elwynn_forest.cpp` implements the scripted behavior for **Henze Faulk** (`npc_henze_faulk`), supporting Quest 1786. The NPC spawns in a dead state (lying down, flagged dead). When hit by Spell ID 8593, he stands up, removes the dead flag, and speaks dialogue ID 2283. A 120-second timer then counts down; if it expires while he is standing, he enters evade mode. The unit maintains no database state.

## Member-by-Member Behavior

### State Initialization
*   **`npc_henze_faulkAI` (ctor)**: Inherits from `ScriptedAI` and immediately calls `Reset()` to ensure the NPC spawns correctly.
*   **`Reset`**: Sets `lifeTimer` to 120,000 ms. Applies `UNIT_DYNFLAG_DEAD` via `WorldObject::SetUInt32Value` and `UNIT_STAND_STATE_DEAD` via `Unit::SetStandState`. Resets `spellHit` to `false`.

### AI Loop & Triggers
*   **`MoveInLineOfSight`**: Empty override to suppress default aggro/detection reactions.
*   **`UpdateAI`**: If `Unit::IsStandingUp()` is true, decrements `lifeTimer` by `diff`. If `lifeTimer` <= 0, calls `CreatureAI::EnterEvadeMode`. If not standing, the timer is ignored.
*   **`SpellHit`**: If hit by Spell ID 8593 and `spellHit` is `false`: sets stand state to `UNIT_STAND_STATE_STAND`, clears `UNIT_DYNAMIC_FLAGS`, calls `ScriptMgr::DoScriptText` with ID 2283 targeting the caster (`Object::ToUnit`), and sets `spellHit` to `true`.

### Registration
*   **`GetAI_npc_henze_faulk`**: Factory function returning a new `npc_henze_faulkAI` instance.
*   **`AddSC_elwynn_forest`**: Creates a `Script` named `"npc_henze_faulk"`, assigns `GetAI_npc_henze_faulk`, and calls `Script::RegisterSelf`. Called by `ScriptLoader::AddScripts`.

## Cross-Unit Boundaries

*   **`ScriptedAI`**: Base class providing AI hooks.
*   **`Unit` / `WorldObject`**: Manipulated for state (`SetStandState`, `SetUInt32Value`, `IsStandingUp`).
*   **`CreatureAI`**: `EnterEvadeMode` called to end the interaction.
*   **`ScriptMgr`**: `DoScriptText` broadcasts dialogue.
*   **`Object`**: `ToUnit` casts the spell caster for dialogue targeting.
*   **`Script` / `ScriptLoader`**: Integration into the server's script registry.

## Data Model

No database tables are accessed.

## Notable Implementation Details

*   **Single-Use Guard**: `spellHit` prevents duplicate triggers from the same healing event.
*   **Conditional Timer**: The 120s countdown only runs while standing. If the NPC is reset while standing, `Reset()` forces him back to the dead state and resets the timer.
*   **Hardcoded Spell**: Relies strictly on Spell ID 8593.

## Member Reference

**npc_henze_faulkAI** (ctor): Inherits from `ScriptedAI`; calls `Reset()` to initialize dead state.

**Reset**: Sets `lifeTimer` to 120,000 ms, applies `UNIT_DYNFLAG_DEAD` and `UNIT_STAND_STATE_DEAD`, and resets `spellHit` to `false`.

**MoveInLineOfSight**: Empty override to suppress default reactions.

**UpdateAI**: If standing, decrements `lifeTimer`; if expired, calls `EnterEvadeMode`. Ignores timer if not standing.

**SpellHit**: On Spell ID 8593 (if `!spellHit`): stands up, clears dead flags, speaks ID 2283 to caster, sets `spellHit` to `true`.

**GetAI_npc_henze_faulk**: Factory function returning a new `npc_henze_faulkAI` instance.

**AddSC_elwynn_forest**: Registers `"npc_henze_faulk"` script with `ScriptMgr`. Called by `ScriptLoader`.

---

<!-- machine-true, projected from graph.json -->

## Map — elwynn_forest

*Source:* elwynn_forest.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| npc_henze_faulkAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | method | Unit.Main/SetStandState, WorldObject.Object/SetUInt32Value | — | — |
| MoveInLineOfSight | method | — | — | — |
| UpdateAI | method | Creature.Main/AI, CreatureAI/EnterEvadeMode, Unit.Main/IsStandingUp | — | — |
| SpellHit | method | Object/ToUnit, ScriptMgr/DoScriptText, Unit.Main/SetStandState, WorldObject.Object/SetUInt32Value | — | — |
| GetAI_npc_henze_faulk | function | — | — | — |
| AddSC_elwynn_forest | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
