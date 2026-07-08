<!-- provenance: verbose -->
# boss_immol_thar

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`boss_immol_thar.cpp` implements the AI for **Immortal Thar**, a boss in the **Dire Maul** dungeon. The `boss_immol_tharAI` class manages combat mechanics including spell rotations (Trample, Infected Bite), summoning a temporary minion (Eye of Immol Thar), teleporting players (Portal of Immol Thar), and an enrage timer. It also includes a heuristic to recover from pathfinding failures by forcing an evade if the boss loses its target for 5 seconds.

## Member-by-Member Behavior

### Initialization and State

**`boss_immol_tharAI` (Constructor)**
Retrieves the `instance_dire_maul` script data from the creature and initializes timers via `Reset()`.

**`Reset`**
Randomizes cooldowns for all abilities using `urand` to prevent simultaneous casts. Resets the pathfinding bug timer (`CheckBug_Timer`) to 0 and sets `m_bEngage` to `false`.

**`EnterEvadeMode`**
Removes summoned guardians via `Unit.Main/RemoveGuardians` before delegating to `ScriptedAI::EnterEvadeMode`.

**`JustDied`**
Notifies the `instance_dire_maul` script that the boss is defeated by calling `SetData(TYPE_IMMOL_THAR, DONE)`.

### Combat Logic

**`Aggro`**
Sets `m_bEngage` to `true`, enabling the enrage mechanic.

**`UpdateAI`**
The main combat loop:
1.  **Pathfinding Check**: If no target/victim exists, increments `CheckBug_Timer`. If it exceeds 5000ms, forces evade, stops combat, and restores full health. Resets timer if a target exists.
2.  **Spell Guard**: Skips ability checks if a non-melee spell is currently casting.
3.  **Abilities**: Uses `ManageTimer` to trigger:
    *   **Trample**: Casts `SPELL_TRAMPLE` on self.
    *   **Infected Bite**: Casts `SPELL_INFECTED_BITE` on the victim.
    *   **Eye of Immol Thar**: Summons creature `14396` at the boss's location. Sends visual spell `25681` and orders the minion to attack the victim.
    *   **Portal of Immol Thar**: Selects a random target, sends visual spell `25681`, teleports them to the boss via `NearTeleportTo`, and reduces their threat by 100%.
4.  **Enrage**: If `m_uiEnrageTimer` expires and `m_bEngage` is true, casts `SPELL_ENRAGE` if not already present.
5.  **Melee**: Calls `DoMeleeAttackIfReady`.

**`ManageTimer`**
Helper that decrements a timer by `diff`. Returns `true` and resets the timer if it expires; otherwise returns `false`.

### Registration

**`GetAI_boss_immol_thar`**
Factory function returning a new `boss_immol_tharAI` instance.

**`AddSC_boss_immol_thar`**
Registers the script with `ScriptMgr` under the name `"boss_immol_thar"`.

## Cross-Unit Boundaries

*   **`ScriptedAI`**: Inherits base AI functionality; calls `EnterEvadeMode`, `DoCastSpellIfCan`, and `DoMeleeAttackIfReady`.
*   **`instance_dire_maul`**: Updates dungeon state in `JustDied`.
*   **`shared_Util`**: Provides `urand` for randomizing timers.
*   **`Unit.Main`**: Handles combat state (`SelectHostileTarget`, `GetVictim`, `IsNonMeleeSpellCasted`), summoning (`SummonCreature`), visuals (`SendSpellGo`), movement (`NearTeleportTo`), threat manipulation (`GetThreatManager`, `modifyThreatPercent`), and recovery actions (`CombatStop`, `SetHealth`, `GetMaxHealth`).
*   **`WorldObject.Object`**: Retrieves instance data and spatial coordinates/orientation.
*   **`Script` / `ScriptMgr`**: Used in `AddSC_boss_immol_thar` for registration.

## Data Model

No database tables are accessed. All configuration is hardcoded.

## Notable Implementation Details

*   **Pathfinding Recovery**: `UpdateAI` detects stuck bosses by checking for missing targets over 5 seconds, then forces a reset. This prevents soft-locks.
*   **Threat Manipulation**: `Portal of Immol Thar` reduces threat by 100%, effectively removing the player from the aggro table.
*   **Visual Decoupling**: Spells `25681` are sent manually via `SendSpellGo` for summons and teleports, separating visuals from mechanical effects.
*   **Enrage Guard**: Enrage only casts if `m_bEngage` is true, preventing pre-combat triggers.

## Member Reference

**`boss_immol_tharAI`**
Constructor initializing instance data and timers.

**`ManageTimer`**
Helper to decrement and reset timers, returning true on expiry.

**`JustDied`**
Marks the boss as done in the `instance_dire_maul` script.

**`Reset`**
Randomizes ability timers and resets state flags.

**`EnterEvadeMode`**
Removes guardians and calls parent evade logic.

**`Aggro`**
Sets the engage flag to enable enrage.

**`UpdateAI`**
Main loop handling pathfinding checks, ability rotations, enrage, and melee.

**`GetAI_boss_immol_thar`**
Factory function for the AI class.

**`AddSC_boss_immol_thar`**
Registers the script with the server.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_immol_thar

*Source:* boss_immol_thar.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_immol_tharAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| ManageTimer | method | — | — | — |
| JustDied | method | instance_dire_maul/SetData | — | — |
| Reset | method | shared_Util/urand | — | — |
| EnterEvadeMode | method | ScriptedAI/EnterEvadeMode, Unit.Main/RemoveGuardians | — | — |
| Aggro | method | — | — | — |
| UpdateAI | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, SpellCaster/CastSpell#2, SpellCaster/IsNonMeleeSpellCasted, ThreatManager/modifyThreatPercent#2, Unit.Main/Attack, Unit.Main/CombatStop, Unit.Main/GetMaxHealth, Unit.Main/GetThreatManager, Unit.Main/GetVictim, Unit.Main/HasAura#2, Unit.Main/NearTeleportTo, Unit.Main/SelectHostileTarget, Unit.Main/SendSpellGo, Unit.Main/SetHealth, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SummonCreature#2 | — | — |
| GetAI_boss_immol_thar | function | — | — | — |
| AddSC_boss_immol_thar | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
