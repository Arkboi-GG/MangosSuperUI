<!-- provenance: verbose -->
# boss_doctor_theolen_krastinov

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`boss_doctor_theolen_krastinov.cpp` implements the combat AI for **Doctor Theolen Krastinov**, a boss in the Scholomance dungeon. The unit defines `boss_theolenkrastinovAI`, which manages timed spell casts (**Rend**, **Backhand**), threat manipulation, and a low-health enrage mechanic (**Frenzy**). It also provides the factory and registration functions required to integrate this AI into the server’s script system.

## Member-by-Member Behavior

### Initialization and Lifecycle

**`boss_theolenkrastinovAI`**
Constructs the AI object for a specific `Creature`. It initializes the parent `ScriptedAI` and immediately calls `Reset()` to establish initial timer states.

**`Reset`**
Sets the internal timers to their starting values:
*   `m_uiRend_Timer`: 8000 ms.
*   `m_uiBackhand_Timer`: 9000 ms.
*   `m_uiFrenzy_Timer`: 1000 ms.

**`JustDied`**
Called when the boss dies. It retrieves the `ScriptedInstance` via `m_creature->GetInstanceData()` and calls `SetData(TYPE_THEOLEN, DONE)` to record the boss defeat in the instance state.

### Combat Logic

**`UpdateAI`**
The primary update loop executed during combat. It performs the following steps:
1.  **Target Check:** Returns early if no hostile target or victim exists.
2.  **Rend:** If `m_uiRend_Timer` expires, casts `SPELL_REND` on the victim and resets the timer to 10000 ms. Otherwise, decrements the timer.
3.  **Backhand:** If `m_uiBackhand_Timer` expires, casts `SPELL_BACKHAND` on the victim, reduces the victim’s threat by 100% via `DoModifyThreatPercent`, and resets the timer to 10000 ms. Otherwise, decrements the timer.
4.  **Frenzy:** If health is below 26%, checks `m_uiFrenzy_Timer`. If expired, casts `SPELL_FRENZY` on self and broadcasts emote `EMOTE_GENERIC_FRENZY_KILL`. Resets the timer to 120000 ms. If health is above 26%, the Frenzy logic is skipped, but the timer continues to decrement if it was previously active.
5.  **Melee:** Calls `DoMeleeAttackIfReady()` to handle standard attacks.

### Registration

**`GetAI_boss_theolen_krastinov`**
Factory function that allocates and returns a new `boss_theolenkrastinovAI` instance for a given `Creature`.

**`AddSC_boss_theolen_krastinov`**
Registers the script with the server. It creates a `Script` object, sets the name to `"boss_doctor_theolen_krastinov"`, assigns `GetAI_boss_theolen_krastinov` as the AI getter, and calls `RegisterSelf()`. This function is invoked by `ScriptLoader::AddScripts` during server startup.

## Cross-Unit Boundaries

*   **`ScriptedAI`**: Base class providing AI infrastructure. Used for `DoModifyThreatPercent` and `DoScriptText`.
*   **`CreatureAI`**: Used for `DoCastSpellIfCan` and `DoMeleeAttackIfReady` to handle spell casting and melee attacks.
*   **`Unit.Main`**: Used for `GetHealthPercent`, `GetVictim`, and `SelectHostileTarget` to query combat state.
*   **`WorldObject.Object`**: Used in `JustDied` via `GetInstanceData` to access instance data.
*   **`InstanceData`**: Used in `JustDied` via `SetData` to update dungeon progress.
*   **`ScriptMgr`**: Used in `AddSC_boss_theolen_krastinov` via `RegisterSelf` to register the script.
*   **`Script`**: Used in `AddSC_boss_theolen_krastinov` to define script metadata.

## Data Model

This unit does not interact with any database tables. State is managed in memory via the `Creature` object, `ScriptedInstance`, and internal timers.

## Notable Implementation Details

*   **Threat Reduction:** After casting Backhand, `DoModifyThreatPercent` reduces the victim’s threat by 100%. This prevents the high-damage spell from causing tank displacement.
*   **Frenzy Trigger:** Frenzy only casts if health is below 26%. The timer is initialized to 1000 ms, allowing immediate activation if the boss spawns below this threshold. Once cast, the 120-second cooldown prevents re-application.
*   **Timer Logic:** Timers are decremented by `uiDiff` each update. Actions trigger when the timer is less than `uiDiff`, indicating expiration.

## Member Reference

**boss_theolenkrastinovAI**
Constructor that initializes the AI and calls `Reset()`.

**Reset**
Resets `m_uiRend_Timer` to 8000 ms, `m_uiBackhand_Timer` to 9000 ms, and `m_uiFrenzy_Timer` to 1000 ms.

**JustDied**
Marks the boss as defeated in the instance data via `SetData(TYPE_THEOLEN, DONE)`.

**UpdateAI**
Manages combat logic: casts Rend and Backhand on timers, reduces threat after Backhand, triggers Frenzy if health < 26%, and handles melee attacks.

**GetAI_boss_theolenkrastinov**
Factory function returning a new `boss_theolenkrastinovAI` instance.

**AddSC_boss_theolenkrastinov**
Registers the script with `ScriptMgr` by creating a `Script` object and calling `RegisterSelf()`.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_doctor_theolen_krastinov

*Source:* boss_doctor_theolen_krastinov.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_theolenkrastinovAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | method | — | — | — |
| JustDied | method | InstanceData/SetData, WorldObject.Object/GetInstanceData | — | — |
| UpdateAI | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, ScriptedAI/DoModifyThreatPercent, ScriptMgr/DoScriptText, Unit.Main/GetHealthPercent, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_boss_theolenkrastinov | function | — | — | — |
| AddSC_boss_theolenkrastinov | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
