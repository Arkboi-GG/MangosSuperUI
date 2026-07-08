<!-- provenance: verbose -->
# boss_tomb_of_seven

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`boss_tomb_of_seven.cpp` implements the AI for **Doomrel**, the final boss of the "Tomb of Seven" encounter in **Blackrock Depths**. The unit manages two distinct phases:

1.  **Tribute Phase:** A sequential engagement of seven dwarf NPCs (Angerrel through Haterel, then Doomrel). The AI coordinates with `InstanceData` to track progress, summon dwarfs via `CallToFight`, and detect wipes if a dwarf loses its target.
2.  **Combat Phase:** Standard boss mechanics including spell rotation (Shadow Volley, Immolate, Curse of Weakness, Demon Armor) and summoning Voidwalkers at 50% health.

The script acts as the encounter controller, updating instance state (`IN_PROGRESS`, `FAIL`, `DONE`, `NOT_STARTED`) to synchronize with other dungeon scripts.

## Member-by-Member Behavior

### Initialization and State Management

*   **`boss_doomrelAI` (Constructor):** Retrieves the `ScriptedInstance` pointer from the creature’s instance data and calls `Reset()` to initialize timers and flags.
*   **`Reset`:** Resets all internal timers (e.g., Shadow Volley at 10s, Immolate at 18s) and clears the dwarf round counter (`m_uiDwarfRound`) and summoning flag (`m_bHasSummoned`).
*   **`JustReachedHome`:** Triggered if the boss despawns/resets. Signals failure to the instance via `InstanceData/SetData` (`TYPE_TOMB_OF_SEVEN` = `FAIL`).
*   **`JustDied`:** Triggered on death. Signals success via `InstanceData/SetData` (`TYPE_TOMB_OF_SEVEN` = `DONE`). Contains commented-out code for spawning a game object.

### Dwarf Tribute Logic

*   **`GetDwarfForPhase`:** Maps `m_uiDwarfRound` (0–6) to a specific dwarf NPC. Queries `InstanceData/GetData64` for GUIDs and retrieves `Creature` pointers via `Map.Main/GetCreature`. Round 6 returns the boss itself.
*   **`CallToFight`:** Manages the current dwarf’s state.
    *   **Start Fight:** Removes immunity flags, sets faction to hostile (`FACTION_HOSTILE`), and forces combat (`Creature.Main/SetInCombatWithZone`).
    *   **Stop Fight:** Respawns dead dwarfs, sets faction to neutral (`FACTION_NEUTRAL`), and deactivates them.
*   **`JustSummoned`:** Assigns a random target from the boss’s threat list to newly summoned Voidwalkers via `CreatureAI/AttackStart`.

### Main AI Loop (`UpdateAI`)

*   **`UpdateAI`:** Executed periodically. Handles two logic blocks:
    1.  **Encounter Progression:**
        *   If `IN_PROGRESS`: Checks `m_uiCallToFight_Timer`. If expired, calls `CallToFight(true)` for the current dwarf, increments `m_uiDwarfRound`, and resets timers.
        *   **Wipe Check:** If `m_uiWipeCheck_Timer` expires, verifies if the *previous* dwarf is alive and has a hostile target. If alive but idle, triggers `FAIL` state.
        *   **Failure Cleanup:** If `FAIL`, iterates all dwarfs calling `CallToFight(false)` to reset them, then sets instance state to `NOT_STARTED`.
    2.  **Combat Mechanics:**
        *   Returns early if no victim.
        *   **Spells:** Casts Shadow Volley (12s), Immolate (25s, random target), Curse of Weakness (45s), and Demon Armor (300s, self).
        *   **Voidwalkers:** Summons once at ≤50% health using `m_bHasSummoned` flag to prevent re-triggering.
        *   **Melee:** Executes `DoMeleeAttackIfReady`.

### Registration

*   **`GetAI_boss_doomrel`:** Factory function returning a new `boss_doomrelAI` instance.
*   **`AddSC_boss_tomb_of_seven`:** Registers the script with `ScriptMgr/RegisterSelf`, linking the AI factory to the creature name "boss_doomrel". Called by `ScriptLoader/AddScripts`.

## Cross-Unit Boundaries

*   **`InstanceData` (via `m_pInstance`):**
    *   *Direction:* Bidirectional.
    *   *Usage:* Reads encounter state (`GetData`) to drive tribute phase logic. Writes state changes (`SetData`) to notify the instance manager of progress, failure, or completion.
*   **`Map.Main` / `Creature.Main`:**
    *   *Direction:* Outbound.
    *   *Usage:* `GetDwarfForPhase` uses `Map.Main/GetCreature` to fetch dwarf pointers. `CallToFight` uses `Creature.Main` methods to modify faction, flags, and combat state.
*   **`CreatureAI` / `Unit.Main`:**
    *   *Direction:* Outbound.
    *   *Usage:* Standard combat actions: target selection (`SelectAttackingTarget`, `SelectHostileTarget`), health checks, and spell/melee execution.
*   **`ScriptMgr` / `ScriptLoader`:**
    *   *Direction:* Inbound to `AddSC_boss_tomb_of_seven`.
    *   *Usage:* Engine loads this script at startup; registration ensures the AI attaches to the correct creature.

## Data Model

This unit does not interact directly with database tables. All encounter state (dwarf GUIDs, phase status) is managed in-memory via `ScriptedInstance`. Dwarf GUIDs (`DATA_ANGERREL`, etc.) are loaded by other scripts or the instance loader.

## Notable Implementation Details

*   **Wipe Detection Heuristic:** The `m_uiWipeCheck_Timer` detects wipes by checking if the active dwarf has lost its target. If a dwarf is alive but idle, it assumes players are dead/disconnected and triggers `FAIL`. This prevents indefinite stalling.
*   **Single-Summon Flag:** `m_bHasSummoned` prevents Voidwalker spam. Without it, the `≤50%` health check would trigger every tick.
*   **Sequential Engagement:** `m_uiDwarfRound` ensures dwarfs engage one-by-one. `CallToFight` handles both activation (hostile/combat) and deactivation (neutral/respawn).
*   **Hardcoded Factions:** Uses hardcoded faction IDs (`FACTION_NEUTRAL = 734`, `FACTION_HOSTILE = 54`).
*   **Timer Initialization:** `m_uiCallToFight_Timer` starts at 0 in `Reset`, causing the first dwarf to engage immediately on the first `UpdateAI` tick if `diff > 0`.

## Member Reference

**boss_doomrelAI** (ctor): Initializes the AI, retrieves the instance data pointer, and calls `Reset()` to initialize timers and state variables.

**Reset**: Resets all spell timers, the dwarf round counter, and the summoning flag to their initial values, preparing the boss for a fresh encounter.

**JustReachedHome**: Signals a failure to the instance data if the boss resets or despawns while active.

**JustDied**: Signals success to the instance data when the boss is killed; contains commented-out code for spawning a game object.

**JustSummoned**: Assigns a random target from the boss's threat list to newly summoned Voidwalkers.

**GetDwarfForPhase**: Maps the current dwarf round index (0-6) to the corresponding dwarf NPC's creature pointer using instance data GUIDs; returns the boss creature for round 6.

**CallToFight**: Activates or deactivates the current dwarf in the rotation; makes them hostile and engages combat if starting, or resets them to neutral and respawns if ending/cleaning up.

**UpdateAI**: The main AI loop; manages the sequential dwarf tribute phase (summoning, wipe detection, failure cleanup) and executes combat spells (Shadow Volley, Immolate, Curse of Weakness, Demon Armor) and Voidwalker summoning based on health thresholds.

**GetAI_boss_doomrel**: Factory function that creates and returns a new `boss_doomrelAI` instance for the designated creature.

**AddSC_boss_tomb_of_seven**: Registers the "boss_doomrel" script with the engine, linking the AI factory function so the boss behaves correctly when spawned.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_tomb_of_seven

*Source:* boss_tomb_of_seven.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_doomrelAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | — | — | — |
| JustReachedHome | method | InstanceData/SetData | — | — |
| JustDied | method | InstanceData/SetData | — | — |
| JustSummoned | method | Creature.Main/AI, Creature.Main/SelectAttackingTarget, CreatureAI/AttackStart | — | — |
| GetDwarfForPhase | method | InstanceData/GetData64, Map.Main/GetCreature, ObjectGuid/ObjectGuid#5 | — | — |
| CallToFight | method | Creature.Main/Respawn, Creature.Main/SetInCombatWithZone, Unit.Main/IsAlive, Unit.Main/IsDead, Unit.Main/SetFactionTemplateId, WorldObject.Object/RemoveFlag | — | — |
| UpdateAI | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, InstanceData/GetData, InstanceData/SetData, SpellCaster/CastSpell#2, Unit.Main/GetHealthPercent, Unit.Main/GetVictim, Unit.Main/IsAlive, Unit.Main/SelectHostileTarget | — | — |
| GetAI_boss_doomrel | function | — | — | — |
| AddSC_boss_tomb_of_seven | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
