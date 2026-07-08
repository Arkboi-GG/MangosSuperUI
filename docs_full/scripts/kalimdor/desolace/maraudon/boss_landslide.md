# boss_landslide

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`boss_landslide.cpp` implements the combat AI for the **Landslide** boss in the **Maraudon** dungeon. The `boss_landslideAI` class manages a timed rotation of three abilities: **Knock Away** (targeted), **Trample** (self-cast), and **Landslide** (self-cast, triggered below 50% health). It relies on the `ScriptedAI` framework for lifecycle management and timer handling.

## Member-by-Member Behavior

### Initialization
*   **`boss_landslideAI`**: Constructs the AI, initializing the base `ScriptedAI` and immediately calling `Reset()` to prime timers.
*   **`Reset`**: Sets `KnockAway_Timer` to 8000 ms, `Trample_Timer` to 2000 ms, and `Landslide_Timer` to 0 ms.

### Combat Loop (`UpdateAI`)
Called periodically with elapsed time `diff`. Returns early if no hostile target exists.
1.  **Knock Away**: If `KnockAway_Timer` expires, casts `SPELL_KNOCKAWAY` on the victim. Resets timer to 15000 ms.
2.  **Trample**: If `Trample_Timer` expires, casts `SPELL_TRAMPLE` on self. Resets timer to 8000 ms.
3.  **Landslide**: If health is < 50%, checks `Landslide_Timer`. On expiry, interrupts non-melee spells and casts `SPELL_LANDSLIDE` on self. Resets timer to 60000 ms. The timer decrements continuously regardless of health, but the cast only occurs below the threshold.
4.  **Melee**: Calls `DoMeleeAttackIfReady()` to perform physical attacks.

### Registration
*   **`GetAI_boss_landslide`**: Factory function returning a new `boss_landslideAI` instance.
*   **`AddSC_boss_landslide`**: Registers the script with `ScriptMgr` under the name `"boss_landslide"`.

## Cross-Unit Boundaries

*   **Outbound**:
    *   `ScriptedAI`: Base class initialization.
    *   `CreatureAI`: `DoCastSpellIfCan` (casting), `DoMeleeAttackIfReady` (melee).
    *   `SpellCaster`: `InterruptNonMeleeSpells` (pre-Landslide cast).
    *   `Unit.Main`: `GetHealthPercent`, `GetVictim`, `SelectHostileTarget` (state queries).
    *   `Script`/`ScriptMgr`: `AddSC_boss_landslide` registers the script via `Script::RegisterSelf`.
*   **Inbound**:
    *   `ScriptLoader/AddScripts`: Calls `AddSC_boss_landslide` during server startup.

## Data Model

No database tables are accessed. All spell IDs and timers are hardcoded.

## Notable Implementation Details

*   **Timer Logic**: Uses manual subtraction (`Timer -= diff`). If `diff` exceeds the timer, the timer becomes negative, triggering the ability immediately on the next check. This prevents missed ticks during lag spikes.
*   **Landslide Interrupt**: `InterruptNonMeleeSpells(false)` is called before casting Landslide to ensure no other spell channels interfere. The `false` argument suppresses visual interrupt effects.
*   **Health Threshold**: Landslide activates strictly when health < 50%. If health were to rise above 50% (e.g., via healing), the ability would pause until health drops again, though the timer continues counting down.

## Member Reference

*   **`boss_landslideAI`**: Constructor; initializes `ScriptedAI` and calls `Reset()`.
*   **`Reset`**: Sets `KnockAway_Timer` (8000 ms), `Trample_Timer` (2000 ms), and `Landslide_Timer` (0 ms).
*   **`UpdateAI`**: Manages combat rotation: casts Knock Away (15 s CD, victim), Trample (8 s CD, self), and Landslide (60 s CD, self, < 50% HP, interrupts non-melee spells). Performs melee attacks.
*   **`GetAI_boss_landslide`**: Factory function creating a `boss_landslideAI` instance.
*   **`AddSC_boss_landslide`**: Registers the script with `ScriptMgr` as `"boss_landslide"`.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_landslide

*Source:* boss_landslide.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_landslideAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | method | — | — | — |
| UpdateAI | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, SpellCaster/InterruptNonMeleeSpells, Unit.Main/GetHealthPercent, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_boss_landslide | function | — | — | — |
| AddSC_boss_landslide | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
