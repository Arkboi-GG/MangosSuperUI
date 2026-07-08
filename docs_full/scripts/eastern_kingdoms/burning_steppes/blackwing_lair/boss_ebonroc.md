<!-- provenance: verbose -->
# boss_ebonroc

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`boss_ebonroc.cpp` implements the combat AI for **Ebonroc**, a boss in the **Blackwing Lair** instance. The `boss_ebonrocAI` class manages spell rotations, threat adjustments, and instance state notifications. It contains no database interactions.

## Member-by-Member Behavior

### Initialization and Timers
*   **`boss_ebonrocAI`**: Retrieves the `ScriptedInstance` pointer and initializes timers via `Reset()`.
*   **`Reset`**: Sets cooldowns: `Shadow Flame` (16s), `Wing Buffet` (30s), `Shadow of Ebonroc` (8s). A source comment notes these values are likely inaccurate.

### Combat Lifecycle
*   **`Aggro`**: Marks the instance phase as `IN_PROGRESS` and sets the creature in combat with the zone.
*   **`JustDied`**: Marks the instance phase as `DONE`.
*   **`JustReachedHome`**: Marks the instance phase as `FAIL` (e.g., if the boss escapes or despawns).

### Spell Logic
*   **`SpellHitTarget`**: Intended to reduce threat by 50% on players hit by `Wing Buffet`. However, the guard clause `pCaster->GetTypeId() != TYPEID_PLAYER` causes an early return because the caster is the boss (a Creature), not a player. Consequently, **this threat reduction never executes**.
*   **`UpdateAI`**:
    *   **Shadow Flame**: Casts on self every 16s.
    *   **Wing Buffet**: Casts on victim every 30s.
    *   **Shadow of Ebonroc**: Casts on victim every 8s, only if the victim lacks the aura (`CF_AURA_NOT_PRESENT`).
    *   **Thrash**: On melee readiness, casts with a ~66% chance (`!urand(0, 2)`).
    *   Performs standard melee attacks.

### Registration
*   **`GetAI_boss_ebonroc`**: Factory function creating the AI instance.
*   **`AddSC_boss_ebonroc`**: Registers the script with `ScriptMgr`.

## Cross-Unit Boundaries

*   **`ScriptedAI` / `ScriptedInstance`**: Inherits base AI behavior; reports fight status (`IN_PROGRESS`, `DONE`, `FAIL`) to the instance manager.
*   **`Creature` / `Unit` / `ThreatManager`**: Used for target selection, casting, melee readiness, and threat modification.
*   **`shared_Util`**: Provides `urand` for randomizing `Thrash` casts.
*   **`ScriptMgr` / `ScriptLoader`**: Handles script registration at server startup.

## Data Model

No database tables are accessed. All state is transient.

## Notable Implementation Details

1.  **Broken Threat Logic**: `SpellHitTarget` fails to reduce threat because it checks the *caster's* type instead of the *target's*. Since the boss casts the spell, the check fails, and the threat modifier is skipped.
2.  **Unverified Timers**: The `Reset()` comment explicitly warns that spell cooldowns are estimates.
3.  **Aura Prevention**: `Shadow of Ebonroc` uses `CF_AURA_NOT_PRESENT` to prevent redundant casts on the same target.

## Member Reference

**boss_ebonrocAI**
Constructor that initializes the AI, retrieves the instance data pointer, and calls `Reset()` to set initial spell timers.

**Reset**
Initializes `m_uiShadowFlameTimer` (16000ms), `m_uiWingBuffetTimer` (30000ms), and `m_uiShadowOfEbonrocTimer` (8000ms). Contains a warning comment that these values may be inaccurate.

**Aggro**
Sets the instance data state to `IN_PROGRESS` and marks the creature as in combat with the zone.

**JustDied**
Sets the instance data state to `DONE`.

**JustReachedHome**
Sets the instance data state to `FAIL`.

**SpellHitTarget**
Checks if the spell hit was `SPELL_WING_BUFFET`. Due to a type check on `pCaster` (which is the boss), the threat reduction logic currently exits early and does not execute. Intended to reduce player threat by 50%.

**UpdateAI**
Manages the main combat loop. Handles timers for `Shadow Flame`, `Wing Buffet`, and `Shadow of Ebonroc` (with aura check). Casts `Thrash` with a ~66% probability on melee readiness. Performs standard melee attacks.

**GetAI_boss_ebonroc**
Factory function returning a new `boss_ebonrocAI` instance.

**AddSC_boss_ebonroc**
Registers the "boss_ebonroc" script with the `ScriptMgr`.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_ebonroc

*Source:* boss_ebonroc.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_ebonrocAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | — | — | — |
| Aggro | method | Creature.Main/SetInCombatWithZone, InstanceData/SetData | — | — |
| JustDied | method | InstanceData/SetData | — | — |
| JustReachedHome | method | InstanceData/SetData | — | — |
| SpellHitTarget | method | Object/GetTypeId, ThreatManager/getThreat, ThreatManager/modifyThreatPercent#2, Unit.Main/GetThreatManager | — | — |
| UpdateAI | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, Unit.Main/GetVictim, Unit.Main/IsAttackReady, Unit.Main/SelectHostileTarget | — | — |
| GetAI_boss_ebonroc | function | — | — | — |
| AddSC_boss_ebonroc | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
