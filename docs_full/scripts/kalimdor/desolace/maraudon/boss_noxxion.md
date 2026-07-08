<!-- provenance: verbose -->
# boss_noxxion

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`boss_noxxion.cpp` implements the combat AI for **Noxxion**, a boss in the Maraudon dungeon. The unit defines `boss_noxxionAI`, a `ScriptedAI` derivative that manages Noxxion’s spell rotation, melee attacks, and a periodic "vanish" mechanic. During this mechanic, Noxxion becomes unselectable, changes model, summons five adds, and remains inactive for 15 seconds before reappearing. The unit also handles corpse cleanup on death and registers the script with the server. It contains no database interactions.

## Member-by-Member Behavior

### Initialization and State

**`boss_noxxionAI`**  
Constructs the AI, invoking the `ScriptedAI` base constructor and immediately calling `Reset()` to initialize timers and the `Invisible` flag.

**`Reset`**  
Sets initial timer values: `ToxicVolley_Timer` (7s), `Uppercut_Timer` (16s), `Adds_Timer` (19s), and `Invisible_Timer` (15s). Sets `Invisible` to `false`.

### Combat and Phase Logic

**`UpdateAI`**  
The main tick handler. It processes three behaviors:
1.  **Invisibility Check:** If `Invisible` is true, it waits for `Invisible_Timer` to expire. Upon expiry, it restores Noxxion to faction `14`, removes the `UNIT_FLAG_NOT_SELECTABLE` flag, sets the display ID to `11172` (normal model), and sets `Invisible` to `false`. If the timer has not expired, it decrements the timer and returns early, skipping all combat actions.
2.  **Target Validation:** Returns early if no hostile target is selected.
3.  **Active Combat:** If visible, it manages three timers:
    *   **Toxic Volley:** Casts spell `21687` on the victim when `ToxicVolley_Timer` expires, resetting the timer to 9s on success.
    *   **Uppercut:** Casts spell `22916` on the victim when `Uppercut_Timer` expires, resetting the timer to 12s on success.
    *   **Add Summoning:** When `Adds_Timer` expires, it interrupts non-melee spells, sets faction to `35`, applies `UNIT_FLAG_NOT_SELECTABLE`, changes display ID to `11686` (invisible model), and calls `SummonAdds` five times. It then sets `Invisible` to `true`, starts the `Invisible_Timer` (15s), and resets `Adds_Timer` to 40s.
Finally, it attempts a melee attack via `DoMeleeAttackIfReady`.

**`SummonAdds`**  
Spawns a single add (entry `13456`) with a 90-second lifetime. If the spawn succeeds and the add has an AI, it commands the add to attack the current victim.

### Death and Cleanup

**`JustDied`**  
Restores Noxxion’s corpse to a normal state: calls `DeMorph()`, resets the faction template ID to the value from `GetCreatureInfo()`, and removes the `UNIT_FLAG_NOT_SELECTABLE` flag.

### Script Integration

**`GetAI_boss_noxxion`**  
Factory function that allocates and returns a new `boss_noxxionAI` instance for a `Creature`.

**`AddSC_boss_noxxion`**  
Creates a `Script` object named `"boss_noxxion"`, links it to `GetAI_boss_noxxion`, and registers it with `ScriptMgr`. Called by `ScriptLoader::AddScripts`.

## Cross-Unit Boundaries

*   **`ScriptedAI` / `CreatureAI`:** Inherits from `ScriptedAI` to use helpers like `DoCastSpellIfCan`, `DoMeleeAttackIfReady`, and `DoSpawnCreature`. Uses `CreatureAI::AttackStart` on spawned adds.
*   **`Creature` / `Unit` / `WorldObject`:** Modifies the creature’s state via `SetDisplayId`, `SetFactionTemplateId`, `DeMorph`, `SetFlag`/`RemoveFlag` (for `UNIT_FLAG_NOT_SELECTABLE`), `GetVictim`, `SelectHostileTarget`, and `InterruptNonMeleeSpells`.
*   **`ScriptMgr` / `ScriptLoader`:** `AddSC_boss_noxxion` registers the script globally during server startup.

## Data Model

This unit does not access any database tables. All configuration is hardcoded.

## Notable Implementation Details

1.  **Faction Switching:** Noxxion switches to faction `35` during the vanish transition and back to `14` upon reappearance. This likely prevents aggro or targeting issues while unselectable.
2.  **Early Return:** `UpdateAI` returns immediately if `Invisible` is true and the timer is active, ensuring no spells or attacks occur while the boss is visually hidden and untargetable.
3.  **Sequential Spawning:** `SummonAdds` is called in a loop five times. Each spawn is independent; failures in one iteration do not stop the others.
4.  **Timer Retry Logic:** Spell timers only reset on `CAST_OK`. If a spell fails, the timer continues to decrement, causing immediate retry attempts in subsequent ticks until the cast succeeds or the timer wraps.
5.  **Commented Movement Control:** A commented-out `m_creature->m_canMove = true` suggests movement was previously manually controlled but is now likely handled by base AI or flags.

## Member Reference

**`boss_noxxionAI`**  
Constructor for the Noxxion AI class. Initializes the base `ScriptedAI` and calls `Reset()` to set initial timer values and state flags.

**`Reset`**  
Resets internal timers (`ToxicVolley`, `Uppercut`, `Adds`, `Invisible`) to their default durations and sets the `Invisible` flag to `false`. Used on spawn or reset.

**`JustDied`**  
Handles post-death cleanup: removes morph effects, restores the original faction template ID from creature data, and removes the `NOT_SELECTABLE` flag to allow corpse interaction.

**`SummonAdds`**  
Spawns a single add (entry `13456`) near Noxxion. If successful, commands the add to attack the current victim. Used internally by `UpdateAI`.

**`UpdateAI`**  
Main combat loop. Manages the invisibility phase (pausing combat if invisible), casts `Toxic Volley` and `Uppercut` on timers, and triggers a periodic event where Noxxion becomes invisible, unselectable, changes model, and summons 5 adds. Handles melee attacks when visible.

**`GetAI_boss_noxxion`**  
Factory function that creates and returns a new `boss_noxxionAI` instance for a given `Creature`. Required by the scripting system.

**`AddSC_boss_noxxion`**  
Registers the "boss_noxxion" script with the server’s `ScriptMgr`. Links the script name to the `GetAI` function pointer.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_noxxion

*Source:* boss_noxxion.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_noxxionAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | method | — | — | — |
| JustDied | method | Creature.Main/GetCreatureInfo, Unit.Main/DeMorph, Unit.Main/SetFactionTemplateId, WorldObject.Object/RemoveFlag | — | — |
| SummonAdds | method | Creature.Main/AI, CreatureAI/AttackStart, ScriptedAI/DoSpawnCreature#2 | — | — |
| UpdateAI | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, SpellCaster/InterruptNonMeleeSpells, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, Unit.Main/SetDisplayId, Unit.Main/SetFactionTemplateId, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag | — | — |
| GetAI_boss_noxxion | function | — | — | — |
| AddSC_boss_noxxion | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
