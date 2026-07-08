# boss_shazzrah

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`boss_shazzrah.cpp` implements the AI for **Shazzrah** (Molten Core) and a supporting spell script for her "Gate" ability. The unit manages Shazzrah’s combat rotation—casting Arcane Explosion, Curse, Deaden Magic, and Counterspell—and executes a "Blink" mechanic that teleports her to a random player, resets threat, and initiates a new attack. It also provides `ShazzrahGateScript` to constrain the Gate spell to a single target, ensuring the teleport logic remains deterministic. Encounter state (Not Started, In Progress, Done) is synchronized with the instance manager via `ScriptedInstance`.

## Member-by-Member Behavior

### Boss AI Lifecycle
*   **`boss_shazzrahAI`**: Constructor. Retrieves `ScriptedInstance` from the creature and initializes timers via `Reset()`.
*   **`Reset`**: Sets initial timer values. If the boss is alive, marks the encounter as `NOT_STARTED` in the instance data.
*   **`Aggro`**: Marks the encounter as `IN_PROGRESS` in the instance data.
*   **`JustDied`**: Marks the encounter as `DONE` in the instance data.

### Combat Loop (`UpdateAI`)
The main update loop checks for a valid victim and processes five timers:
1.  **Arcane Explosion**: Cast on victim every 3–5s (randomized post-cast).
2.  **Shazzrah’s Curse**: Cast on victim every 20s, only if the victim lacks the aura (`CF_AURA_NOT_PRESENT`).
3.  **Deaden Magic**: Cast on self every 7–14s (randomized).
4.  **Counterspell**: Cast on victim every 16–18s (randomized).
5.  **Blink**: Every 25–35s (randomized), casts `SPELL_GATE_DUMMY`. If successful, selects a random player, calls `DoResetThreat()` to clear aggro, teleports to the player via `NearTeleportTo`, and attacks them. This forces a new primary target.
Finally, `DoMeleeAttackIfReady()` handles standard melee.

### Spell Script
*   **`OnSetTargetMap`**: Hook for `ShazzrahGateScript`. Forces `unMaxTargets` to 1, ensuring the Gate spell affects only one entity, which aligns with the AI’s single-target teleport logic.

### Registration
*   **`GetAI_boss_shazzrah`** / **`GetScript_ShazzrahGate`**: Factory functions returning new AI and spell script instances.
*   **`AddSC_boss_shazzrah`**: Registers both scripts with the `ScriptMgr`.

## Cross-Unit Boundaries

*   **Instance Data**: `Reset`, `Aggro`, and `JustDied` call `InstanceData/SetData` to update encounter state.
*   **Combat Engine**: `UpdateAI` calls `CreatureAI/DoCastSpellIfCan` for spells, `CreatureAI/DoMeleeAttackIfReady` for melee, and `ScriptedAI/DoResetThreat` during Blink.
*   **Targeting & Movement**: `UpdateAI` uses `Creature.Main/SelectAttackingTarget` to pick a random player for Blink, `WorldObject.Object/GetPosition` to get coordinates for `NearTeleportTo`, and `Unit.Main/Attack` to engage the new target.
*   **Utilities**: `Reset` and `UpdateAI` use `shared_Util/urand` for timer randomization.

## Data Model

No database tables are accessed. State is managed in-memory via `ScriptedInstance` and member variables.

## Notable Implementation Details

*   **Manual Teleportation**: The Blink mechanic manually handles teleportation (`NearTeleportTo`) and threat reset (`DoResetThreat`) rather than relying solely on spell effects. This ensures reliability if the dummy spell fails.
*   **Threat Reset**: `DoResetThreat()` clears the threat table during Blink, forcing players to re-aggro. The boss immediately attacks the teleported-to player, making them the new primary target.
*   **Aura Check**: `ShazzrahCurse` uses `CF_AURA_NOT_PRESENT` to prevent stacking.
*   **Single-Target Constraint**: `ShazzrahGateScript` limits the Gate spell to one target to match the AI’s expectation of a single teleport destination.

## Member Reference

*   **`boss_shazzrahAI`**: Constructor; retrieves instance data and calls `Reset()`.
*   **`Reset`**: Initializes timers; sets instance state to `NOT_STARTED` if alive.
*   **`Aggro`**: Sets instance state to `IN_PROGRESS`.
*   **`JustDied`**: Sets instance state to `DONE`.
*   **`UpdateAI`**: Main loop; manages spell timers, executes Blink (teleport/threat reset/attack), and handles melee.
*   **`GetAI_boss_shazzrah`**: Factory function for `boss_shazzrahAI`.
*   **`OnSetTargetMap`**: Limits Gate spell to one target.
*   **`GetScript_ShazzrahGate`**: Factory function for `ShazzrahGateScript`.
*   **`AddSC_boss_shazzrah`**: Registers boss AI and spell scripts.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_shazzrah

*Source:* boss_shazzrah.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_shazzrahAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | InstanceData/SetData, shared_Util/urand, Unit.Main/IsAlive | — | — |
| Aggro | method | InstanceData/SetData | — | — |
| JustDied | method | InstanceData/SetData | — | — |
| UpdateAI | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, ScriptedAI/DoResetThreat, shared_Util/urand, Unit.Main/Attack, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, WorldObject.Object/GetPosition#3 | — | — |
| GetAI_boss_shazzrah | function | — | — | — |
| OnSetTargetMap | method | — | — | — |
| GetScript_ShazzrahGate | function | — | — | — |
| AddSC_boss_shazzrah | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
