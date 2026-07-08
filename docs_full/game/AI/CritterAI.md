# CritterAI

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# CritterAI

**CritterAI** implements the passive behavior for creatures classified as `CREATURE_TYPE_CRITTER`. Its sole responsibility is to ensure these entities never engage in combat: they ignore threats, flee when damaged or affected by harmful spells, and automatically evade combat mode after a fixed duration.

## Purpose & Responsibilities

1.  **Assignment Restriction**: `Permissible` ensures only critters can use this AI.
2.  **Threat Ignorance**: `MoveInLineOfSight` and `AttackStart` are empty overrides, preventing aggro generation.
3.  **Flight Response**: `DamageTaken` and `SpellHit` trigger a 30-second fleeing movement (`MoveFleeing`) if the creature survives the hit.
4.  **Combat Cleanup**: `UpdateAI` monitors the `m_uiCombatTimer`. If the creature remains in combat after the timer expires, it forces `EnterEvadeMode` to clear combat state.

## Member-by-Member Behavior

### Initialization & Validation
*   **`CritterAI`**: Constructor initializing the base `CreatureAI`. Called by `npcs_special/npc_chicken_cluckAI` and `npcs_special/npc_sickly_critterAI`.
*   **`Permissible`**: Static check returning `PERMIT_BASE_SPECIAL` if `Creature.Main/GetCreatureInfo` reports `CREATURE_TYPE_CRITTER`; otherwise `PERMIT_BASE_NO`.

### Passive Behavior
*   **`MoveInLineOfSight`**: Empty override; ignores units entering line of sight.
*   **`AttackStart`**: Empty override; prevents attack initiation.

### Reaction to Harm
*   **`DamageTaken`**: If damage is non-lethal (`uiDamage < Unit.Main/GetHealth`), checks if already fleeing via `Creature.MotionMaster/GetCurrentMovementGeneratorType`. If not, starts `Creature.MotionMaster/MoveFleeing` from the attacker for 30s (`ESCAPE_TIMER`) and resets `m_uiCombatTimer`.
*   **`SpellHit`**: Reacts to non-positive, non-direct-damage spells (e.g., debuffs). If `Unit.Main/IsAlive` and not already fleeing, casts `SpellCaster` to `Unit` via `Object/ToUnit`, starts `Creature.MotionMaster/MoveFleeing` for 30s, and resets `m_uiCombatTimer`. Filters using `SpellEntry/IsPositiveSpell` and `SpellEntry/IsDirectDamageSpell`.

### State Management
*   **`UpdateAI`**: If `Unit.Main/IsInCombat` is true, decrements `m_uiCombatTimer`. On expiry, calls `CreatureAI/EnterEvadeMode` to exit combat and resets the timer.

## Cross-Unit Boundaries

*   **`Creature.Main`**: `Permissible` uses `GetCreatureInfo`; `DamageTaken` uses `GetHealth`.
*   **`Creature.MotionMaster`**: `DamageTaken`/`SpellHit` use `GetCurrentMovementGeneratorType` and `MoveFleeing`.
*   **`Unit.Main`**: `SpellHit` uses `IsAlive`; `UpdateAI` uses `IsInCombat`.
*   **`CreatureAI`**: `UpdateAI` calls `EnterEvadeMode`.
*   **`SpellEntry`**: `SpellHit` uses `IsPositiveSpell` and `IsDirectDamageSpell`.
*   **`Object`**: `SpellHit` uses `ToUnit`.

## Data Model

No database tables are accessed.

## Notable Implementation Details

*   **Lethal Hit Optimization**: `DamageTaken` skips fleeing if damage equals or exceeds health, avoiding unnecessary movement for dying creatures.
*   **Spell Filtering**: `SpellHit` explicitly excludes direct damage spells (`!IsDirectDamageSpell`), assuming those trigger `DamageTaken` instead. It targets harmful indirect effects.
*   **Timer Reset**: Repeated hits reset `m_uiCombatTimer`, extending the evasion window.
*   **Hardcoded Duration**: `ESCAPE_TIMER` is fixed at 30,000 ms.

## Member Reference

*   **`CritterAI`**: Constructor initializing `CreatureAI`. Called by `npcs_special/npc_chicken_cluckAI`, `npcs_special/npc_sickly_critterAI`.
*   **`Permissible`**: Static method checking `Creature.Main/GetCreatureInfo` for `CREATURE_TYPE_CRITTER`. Returns `PERMIT_BASE_SPECIAL` or `PERMIT_BASE_NO`.
*   **`MoveInLineOfSight`**: Empty override ignoring line-of-sight events.
*   **`AttackStart`**: Empty override preventing attack initiation.
*   **`DamageTaken`**: If non-lethal (`Unit.Main/GetHealth`), checks `Creature.MotionMaster/GetCurrentMovementGeneratorType`; if not fleeing, calls `Creature.MotionMaster/MoveFleeing` for 30s and resets `m_uiCombatTimer`.
*   **`SpellHit`**: If non-positive/non-direct-damage (`SpellEntry/IsPositiveSpell`, `SpellEntry/IsDirectDamageSpell`) and `Unit.Main/IsAlive`, checks movement type; if not fleeing, casts `Object/ToUnit` and calls `Creature.MotionMaster/MoveFleeing` for 30s, resetting `m_uiCombatTimer`.
*   **`UpdateAI`**: If `Unit.Main/IsInCombat`, decrements `m_uiCombatTimer`. On expiry, calls `CreatureAI/EnterEvadeMode` and resets timer.

---

<!-- machine-true, projected from graph.json -->

## Map — CritterAI

*Source:* CritterAI.cpp, CritterAI.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Permissible | method | Creature.Main/GetCreatureInfo | — | — |
| CritterAI | ctor | — | npcs_special/npc_chicken_cluckAI, npcs_special/npc_sickly_critterAI | — |
| MoveInLineOfSight | method | — | — | — |
| AttackStart | method | — | — | — |
| DamageTaken | method | Creature.MotionMaster/GetCurrentMovementGeneratorType, Creature.MotionMaster/MoveFleeing, Unit.Main/GetHealth, Unit.Main/GetMotionMaster | — | — |
| SpellHit | method | Creature.MotionMaster/GetCurrentMovementGeneratorType, Creature.MotionMaster/MoveFleeing, Object/ToUnit, SpellEntry/IsDirectDamageSpell, SpellEntry/IsPositiveSpell#4, Unit.Main/GetMotionMaster, Unit.Main/IsAlive | npcs_special/SpellHit#3 | — |
| UpdateAI | method | CreatureAI/EnterEvadeMode, Unit.Main/IsInCombat | npcs_special/UpdateAI#13, npcs_special/UpdateAI#2 | — |
