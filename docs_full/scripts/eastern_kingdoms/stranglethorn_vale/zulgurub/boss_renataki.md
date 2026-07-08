<!-- provenance: verbose -->
# boss_renataki

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`boss_renataki.cpp` implements the AI for **Renataki**, a boss in the Zul'Gurub dungeon. The `boss_renatakiAI` class manages her combat rotation, alternating between visible spellcasting/melee and a stealth phase (`EnterVanish`). Key mechanics include:
*   **Stealth Cycle:** Periodically vanishes for ~20s, then reappears to `Ambush` a random target.
*   **Spell Rotation:** Casts *Suriner Zone*, *Thousand Blades*, and *Red Lightning* (once at start).
*   **Enrage:** Applies *Enrage* below 30% health.
*   **Threat Management:** Periodically reduces current victim's threat by 50% and switches targets to distribute damage.
*   **Visuals:** Hardcodes weapon display IDs in `Reset` to ensure correct appearance.

No database tables are accessed.

## Member-by-Member Behavior

### Initialization & Lifecycle

**`boss_renatakiAI`**
Constructs the AI, calls `Reset()`, and initializes `Light` to `false` (tracks whether *Red Lightning* has been cast).

**`Reset`**
Resets timers to random intervals:
*   `Invisible_Timer`: 28–32s (time until vanish).
*   `Suriner_Timer`: 9–11s (*Suriner Zone* cooldown).
*   `Visible_Timer`: 20s (duration of vanish).
*   `Aggro_Timer`: 15–25s (threat switch interval).
*   `ThousandBlades_Timer`: 4–8s (*Thousand Blades* cooldown).
*   `TickTimer`: 1000ms (unused).

Sets `Invisible` to `false`. Configures visual appearance via `SetUInt32Value` on `UNIT_VIRTUAL_ITEM_SLOT_DISPLAY` and `UNIT_VIRTUAL_ITEM_INFO` with hardcoded values (31818, 218171138, 3) to workaround missing item display IDs.

**`JustDied`**
Calls `LeaveVanish()` from `ScriptedAI` to ensure visibility upon death.

### Combat Logic

**`UpdateAI`**
Executes the main loop:
1.  **Red Lightning:** If `Light` is false, casts *Red Lightning* on self and sets `Light` to true.
2.  **Victim Check:** Returns early if no valid victim exists.
3.  **Enrage:** If health < 30%, casts *Enrage* if not already present.
4.  **Invisible Phase:** If `Invisible` is true, decrements `Visible_Timer`. On expiry, selects a random target, calls `LeaveVanish()`, executes `Ambush()` on the target, sets `Invisible` to false, and resets `Visible_Timer`. Returns early, skipping other abilities.
5.  **Suriner Zone:** On `Suriner_Timer` expiry, casts *Suriner Zone* and resets timer (9–11s).
6.  **Vanish Trigger:** On `Invisible_Timer` expiry, calls `EnterVanish()`, sets `Invisible` to true, and resets timer (30–42s).
7.  **Aggro Switch:** On `Aggro_Timer` expiry, selects a random target (excluding current victim), reduces current victim's threat by 50% via `modifyThreatPercent`, and starts attacking the new target. Resets timer (7–20s).
8.  **Thousand Blades:** On `ThousandBlades_Timer` expiry, casts *Thousand Blades* on victim and resets timer (7–12s).
9.  **Melee:** If attack ready, casts *Trash* on self with 33% probability (`!urand(0, 2)`), then performs melee attack.

### Registration

**`GetAI_boss_renataki`**
Factory function returning a new `boss_renatakiAI` instance.

**`AddSC_boss_renataki`**
Registers the script with `ScriptMgr` by creating a `Script` object named `"boss_renataki"` and calling `RegisterSelf()`.

## Cross-Unit Boundaries

*   **`ScriptedAI`**: Inherits from `ScriptedAI`. Uses `EnterVanish`, `LeaveVanish`, `Ambush`, `DoCastSpellIfCan`, `DoMeleeAttackIfReady`, and `AttackStart` for core combat actions.
*   **`shared_Util`**: Uses `urand` for randomized timer intervals.
*   **`Creature` / `WorldObject`**: Uses `m_creature` to set visual values (`SetUInt32Value`), check health (`GetHealthPercent`), select targets (`SelectHostileTarget`, `SelectAttackingTarget`), and access threat management.
*   **`ThreatManager`**: Directly manipulates threat via `getThreat` and `modifyThreatPercent` to force target switching.
*   **`ScriptMgr`**: `AddSC_boss_renataki` registers the script globally.

## Data Model

No database tables are accessed.

## Notable Implementation Details

*   **Unused Timer:** `TickTimer` is initialized in `Reset` but never used in `UpdateAI`.
*   **Hardcoded Visuals:** `Reset` uses hardcoded integers for item display because the original ID (31818) is missing from the database.
*   **Threat Reduction:** The aggro rotation explicitly cuts current victim threat by 50% before switching, preventing tank lock-on.
*   **State Machine:** The `Invisible` flag gates all other abilities; while vanished, only the vanish-exit logic runs.
*   **Randomized Cooldowns:** Wide random ranges (e.g., 30–42s for vanish) make patterns unpredictable.

## Member Reference

**`boss_renatakiAI`**
Constructor. Initializes `ScriptedAI`, calls `Reset()`, and sets `Light` to `false`.

**`Reset`**
Resets timers to random intervals. Sets `Invisible` to `false`. Hardcodes virtual item display values for visual correctness.

**`JustDied`**
Calls `LeaveVanish()` from `ScriptedAI` to reveal the creature on death.

**`UpdateAI`**
Main loop. Handles *Red Lightning* (once), *Enrage* (<30% HP), vanish cycle (`EnterVanish`/`LeaveVanish`/`Ambush`), *Suriner Zone*, *Thousand Blades*, threat reduction/target switching, and melee with 33% chance for *Trash*.

**`GetAI_boss_renataki`**
Factory function creating a new `boss_renatakiAI` instance.

**`AddSC_boss_renataki`**
Registers the script with `ScriptMgr` via `Script::RegisterSelf()`.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_renataki

*Source:* boss_renataki.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_renatakiAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | method | shared_Util/urand, WorldObject.Object/SetUInt32Value | — | — |
| JustDied | method | ScriptedAI/LeaveVanish | — | — |
| UpdateAI | method | Creature.Main/SelectAttackingTarget, CreatureAI/AttackStart, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Errors/PrintStacktraceAndThrow, ScriptedAI/Ambush, ScriptedAI/EnterVanish, ScriptedAI/LeaveVanish, shared_Util/urand, SpellCaster/CastSpell#2, ThreatManager/getThreat, ThreatManager/modifyThreatPercent#2, Unit.Main/GetHealthPercent, Unit.Main/GetThreatManager, Unit.Main/GetVictim, Unit.Main/IsAttackReady, Unit.Main/SelectHostileTarget | — | — |
| GetAI_boss_renataki | function | — | — | — |
| AddSC_boss_renataki | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
