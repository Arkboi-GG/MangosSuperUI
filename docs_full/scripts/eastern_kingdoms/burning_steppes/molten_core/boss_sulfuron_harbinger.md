<!-- provenance: verbose -->
# boss_sulfuron_harbinger

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_sulfuron_harbinger

## Purpose & Responsibilities

`boss_sulfuron_harbinger.cpp` implements the artificial intelligence for **Sulfuron Harbinger**, a boss in the **Molten Core** instance. The unit defines `boss_sulfuronAI`, which manages combat timers, spell casting, and instance state reporting. It handles five primary abilities: `Demoralizing Shout`, `Inspire`, `Knockdown`, `Flamespear`, and `Darkstrike`.

## Member-by-Member Behavior

### Lifecycle & State
*   **`boss_sulfuronAI`**: Constructor initializes `m_pInstance` from the creature’s instance data and calls `Reset()`.
*   **`Reset`**: Sets base timer values (noted in comments as "probably wrong"). If the creature is alive, it reports `NOT_STARTED` to the instance.
*   **`Aggro`**: Reports `IN_PROGRESS` to the instance and marks the creature in combat with the zone.
*   **`JustDied`**: Reports `DONE` to the instance.

### Combat Loop (`UpdateAI`)
Executes per tick. Returns early if no hostile target exists. Processes five timers sequentially:
1.  **Demoralizing Shout**: Casts on victim. Reshuffles timer (15–20s) on success.
2.  **Inspire**: Finds friendly creatures within 45 yards missing the buff. Picks one randomly via `std::list` iteration. Attempts to cast on the target, then on self. **Note**: If the self-cast fails, the function returns immediately, skipping subsequent abilities (`Knockdown`, `Flamespear`, `Darkstrike`) for that tick. Timer reshuffles (20–26s) only if self-cast succeeds.
3.  **Knockdown**: Casts on victim. Reshuffles timer (12–15s) on success.
4.  **Flamespear**: Selects a random hostile target. Casts spell. Reshuffles timer (12–16s) on success. *Note: Source comments indicate intended movement logic is missing.*
5.  **Darkstrike**: Casts on victim. Reshuffles timer (15–18s) on success.

Finally, calls `DoMeleeAttackIfReady()`.

### Registration
*   **`GetAI_boss_sulfuron`**: Factory function returning a new `boss_sulfuronAI`.
*   **`AddSC_boss_sulfuron`**: Registers the script with `ScriptMgr`. Called by `ScriptLoader::AddScripts`.

## Cross-Unit Boundaries

*   **`ScriptedAI`**: Inherits helper methods (`DoCastSpellIfCan`, `DoFindFriendlyMissingBuff`, `DoMeleeAttackIfReady`).
*   **`ScriptedInstance`**: Accessed via `m_pInstance` to update encounter state (`TYPE_SULFURON`).
*   **`Unit`/`Creature`**: Used for targeting (`SelectHostileTarget`, `GetVictim`, `SelectAttackingTarget`) and combat state (`SetInCombatWithZone`, `IsAlive`).
*   **`shared_Util`**: Uses `urand` for timer randomization.
*   **`ScriptMgr`**: Integration point for loading the AI.

## Data Model

No database tables are accessed directly. State is managed in-memory via `ScriptedInstance`.

## Notable Implementation Details

1.  **Early Return Bug**: In `UpdateAI`, if `Inspire` fails to cast on Sulfuron himself, the function returns immediately. This prevents `Knockdown`, `Flamespear`, and `Darkstrike` from ticking in that frame.
2.  **Incomplete Feature**: A comment notes Sulfuron should move before casting `Flamespear`, but no movement logic is implemented.
3.  **Timer Values**: Comments indicate initial timer values in `Reset` are approximate.
4.  **Inefficient Random Selection**: `Inspire` uses `std::list::advance` with `rand()`, resulting in O(N) complexity for target selection.

## Member Reference

**boss_sulfuronAI**  
Constructor. Retrieves `ScriptedInstance` and calls `Reset()`.

**Reset**  
Initializes timers. Reports `NOT_STARTED` to instance if alive.

**Aggro**  
Reports `IN_PROGRESS` to instance. Sets zone combat state.

**JustDied**  
Reports `DONE` to instance.

**UpdateAI**  
Main loop. Processes 5 spell timers. Contains early-return bug on failed `Inspire` self-cast. Calls melee attack handler.

**GetAI_boss_sulfuron**  
Factory function creating `boss_sulfuronAI`.

**AddSC_boss_sulfuron**  
Registers script with `ScriptMgr`.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_sulfuron_harbinger

*Source:* boss_sulfuron_harbinger.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_sulfuronAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | InstanceData/SetData, Unit.Main/IsAlive | — | — |
| Aggro | method | Creature.Main/SetInCombatWithZone, InstanceData/SetData | — | — |
| JustDied | method | InstanceData/SetData | — | — |
| UpdateAI | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, ScriptedAI/DoFindFriendlyMissingBuff, shared_Util/urand, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_boss_sulfuron | function | — | — | — |
| AddSC_boss_sulfuron | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
