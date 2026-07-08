<!-- provenance: verbose -->
# boss_firemaw

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_firemaw

## Purpose & Responsibilities

`boss_firemaw` implements the AI for the **Firemaw** boss in the Blackwing Lair instance. The `boss_firemawAI` class manages combat rotations, threat manipulation, and instance state reporting. It defines four timed abilities: `Shadow Flame`, `Wing Buffet`, `Flame Buffet`, and a conditional melee `Thrash`.

## Member-by-Member Behavior

### Initialization and State Management

**`boss_firemawAI`**
Constructs the AI, retrieves the `ScriptedInstance` pointer from the creature, and initializes timers via `Reset()`.

**`Reset`**
Sets initial cooldowns: `Shadow Flame` (16s), `Wing Buffet` (30s), and `Flame Buffet` (2s).

**`Aggro`**
Marks the instance event `TYPE_FIREMAW` as `IN_PROGRESS` and flags the creature as in combat with the zone.

**`JustDied`**
Marks the instance event `TYPE_FIREMAW` as `DONE`.

**`JustReachedHome`**
Marks the instance event `TYPE_FIREMAW` as `FAIL` (e.g., on timeout or disconnect).

### Combat Logic

**`UpdateAI`**
Executes the main combat loop:
1.  **Shadow Flame**: Casts on self every 16s.
2.  **Wing Buffet**: Casts on the current victim every 30s.
3.  **Flame Buffet**: Casts on self every 1.8–3.0s (randomized).
4.  **Thrash**: If melee-ready and not casting non-melee spells, has a ~33% chance (`!urand(0, 2)`) to cast `Thrash` on self.
5.  **Melee**: Performs standard melee attacks via `DoMeleeAttackIfReady()`.

**`SpellHitTarget`**
Triggers when a spell hits a target. If the spell is `Wing Buffet` and the affected unit (`pCaster` parameter, representing the target in this hook context) is a player, it reduces that player's threat by 50%. This mitigates aggro spikes from the knockback effect.

### Registration

**`GetAI_boss_firemaw`**
Factory function returning a new `boss_firemawAI` instance.

**`AddSC_boss_firemaw`**
Registers the script with `ScriptMgr` under the name `"boss_firemaw"`. Called by `ScriptLoader::AddScripts`.

## Cross-Unit Boundaries

*   **`ScriptedAI` / `CreatureAI`**: Inherits base AI functionality; uses `DoCastSpellIfCan` and `DoMeleeAttackIfReady`.
*   **`ScriptedInstance`**: Reports encounter status (`IN_PROGRESS`, `DONE`, `FAIL`) via `SetData(TYPE_FIREMAW, ...)`.
*   **`ThreatManager`**: Modifies player threat percentages in `SpellHitTarget`.
*   **`Unit` / `Creature`**: Accesses combat state, targets, and readiness flags.
*   **`ScriptMgr`**: Handles script registration at server startup.

## Data Model

No database tables are accessed. State is managed entirely in-memory via the instance script interface.

## Notable Implementation Details

*   **Parameter Naming in `SpellHitTarget`**: The parameter `pCaster` in `SpellHitTarget(Unit* pCaster, ...)` actually represents the **target** of the spell in this engine version's hook signature. The code checks `pCaster->GetTypeId() != TYPEID_PLAYER` to ensure the victim is a player before reducing their threat.
*   **Thrash Probability**: `Thrash` is not guaranteed on every melee swing; it relies on a 1-in-3 random check.
*   **Flame Buffet Randomization**: Unlike the fixed timers for other spells, `Flame Buffet` resets to a random interval between 1800ms and 3000ms.

## Member Reference

**`boss_firemawAI`**
Constructor initializing instance data and timers.

**`Reset`**
Resets ability timers to defaults (16s, 30s, 2s).

**`Aggro`**
Sets instance state to `IN_PROGRESS` and marks creature in combat.

**`JustDied`**
Sets instance state to `DONE`.

**`JustReachedHome`**
Sets instance state to `FAIL`.

**`SpellHitTarget`**
Reduces threat by 50% for players hit by `Wing Buffet`.

**`UpdateAI`**
Manages spell timers and melee attacks, including conditional `Thrash`.

**`GetAI_boss_firemaw`**
Factory function for `boss_firemawAI`.

**`AddSC_boss_firemaw`**
Registers the script with the server.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_firemaw

*Source:* boss_firemaw.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_firemawAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | — | — | — |
| Aggro | method | Creature.Main/SetInCombatWithZone, InstanceData/SetData | — | — |
| JustDied | method | InstanceData/SetData | — | — |
| JustReachedHome | method | InstanceData/SetData | — | — |
| SpellHitTarget | method | Object/GetTypeId, ThreatManager/getThreat, ThreatManager/modifyThreatPercent#2, Unit.Main/GetThreatManager | — | — |
| UpdateAI | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/GetVictim, Unit.Main/IsAttackReady, Unit.Main/SelectHostileTarget | — | — |
| GetAI_boss_firemaw | function | — | — | — |
| AddSC_boss_firemaw | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
