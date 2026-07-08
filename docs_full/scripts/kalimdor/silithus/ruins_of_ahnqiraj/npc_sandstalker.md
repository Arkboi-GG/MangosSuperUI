# npc_sandstalker

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# npc_sandstalker

**Purpose & Responsibilities**

`npc_sandstalker` implements the AI for the Sandstalker creature in the Ruins of Ahn'Qiraj. It models an ambush predator that cycles between a stealthed ("vanished") state and active combat. The creature remains invisible until it either provokes a player or its internal timer expires, at which point it reveals itself, casts `SPELL_BURROW` (ID 26381) on a random target, and engages in melee.

## Member-by-Member Behavior

### Initialization and Lifecycle
*   **`npc_sandstalkerAI` (Constructor):** Calls `Reset()` to initialize the creature in the vanished state with a 5-second burrow timer.
*   **`JustReachedHome`:** Calls `EnterVanish()` from `ScriptedAI` to ensure invisibility upon respawn.
*   **`JustDied`:** Calls `LeaveVanish()` from `ScriptedAI` to reveal the corpse.

### State Management and Combat
*   **`Reset`:** Calls `EnterVanish()` from `ScriptedAI` and sets `m_uiBurrow_Timer` to 5000 ms.
*   **`Aggro`:** Triggered when attacked. Selects a random target via `Creature.Main/SelectAttackingTarget`, calls `LeaveVanish()` from `ScriptedAI`, and executes `Ambush()` from `ScriptedAI` with `SPELL_BURROW`. Note: It does not reset `m_uiBurrow_Timer`, allowing the autonomous timer to continue running during combat.
*   **`UpdateAI`:** The main loop. If not in combat (`Unit.Main/IsInCombat`), it calls `EnterVanish()` from `ScriptedAI`. If `m_uiBurrow_Timer` expires, it calls `EnterVanish()` from `ScriptedAI` (ensuring state), selects a random target via `Creature.Main/SelectAttackingTarget`, calls `LeaveVanish()` from `ScriptedAI`, executes `Ambush()` from `ScriptedAI` with `SPELL_BURROW`, and resets the timer using `urand` from `shared_Util` (5–10 seconds). Otherwise, it decrements the timer. Finally, it calls `DoMeleeAttackIfReady` from `CreatureAI`.

### Registration
*   **`GetAI_npc_sandstalker`:** Factory function returning a new `npc_sandstalkerAI` instance.
*   **`AddSC_npc_sandstalker`:** Registers the script with `ScriptMgr/RegisterSelf` under the name `"npc_sandstalker"`.

## Cross-Unit Boundaries

*   **`ScriptedAI`**: Provides `EnterVanish()`, `LeaveVanish()`, and `Ambush()` for stealth management and special attacks.
*   **`Creature.Main`**: Provides `SelectAttackingTarget()` for random target selection.
*   **`CreatureAI`**: Provides `DoMeleeAttackIfReady()` for standard melee attacks.
*   **`Unit.Main`**: Provides `IsInCombat()` to check combat state.
*   **`shared_Util`**: Provides `urand()` for random timer intervals.
*   **`ScriptMgr`**: Provides `RegisterSelf()` for script registration.

## Data Model

This unit does not interact with any database tables.

## Notable Implementation Details

*   **Continuous Timer During Combat:** `Aggro` does not reset `m_uiBurrow_Timer`. Consequently, if the timer expires while the creature is already in combat, `UpdateAI` will trigger another `Ambush()` sequence. This results in repeated burrow attacks during prolonged fights.
*   **Redundant Vanish Call:** In `UpdateAI`, `EnterVanish()` is called before `LeaveVanish()` during the timer expiration block. While logically redundant if the creature was already vanished, it ensures the stealth state is explicitly applied before the ambush animation triggers.

## Member Reference

*   **`npc_sandstalkerAI`**: Constructor calling `Reset()`.
*   **`JustReachedHome`**: Calls `EnterVanish` from `ScriptedAI`.
*   **`Reset`**: Calls `EnterVanish` from `ScriptedAI` and sets `m_uiBurrow_Timer` to 5000.
*   **`Aggro`**: Selects target via `Creature.Main/SelectAttackingTarget`, calls `LeaveVanish` from `ScriptedAI`, and executes `Ambush` from `ScriptedAI` with `SPELL_BURROW`.
*   **`JustDied`**: Calls `LeaveVanish` from `ScriptedAI`.
*   **`UpdateAI`**: Checks `Unit.Main/IsInCombat`; if not in combat, calls `EnterVanish` from `ScriptedAI`. If `m_uiBurrow_Timer` expires, calls `EnterVanish` from `ScriptedAI`, selects target via `Creature.Main/SelectAttackingTarget`, calls `LeaveVanish` from `ScriptedAI`, executes `Ambush` from `ScriptedAI`, and resets timer via `urand` from `shared_Util`. Otherwise decrements timer. Calls `DoMeleeAttackIfReady` from `CreatureAI`.
*   **`GetAI_npc_sandstalker`**: Factory function returning `npc_sandstalkerAI`.
*   **`AddSC_npc_sandstalker`**: Registers script via `ScriptMgr/RegisterSelf`.

---

<!-- machine-true, projected from graph.json -->

## Map — npc_sandstalker

*Source:* npc_sandstalker.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| npc_sandstalkerAI | ctor | ScriptedAI/ScriptedAI | — | — |
| JustReachedHome | method | ScriptedAI/EnterVanish | — | — |
| Reset | method | ScriptedAI/EnterVanish | — | — |
| Aggro | method | Creature.Main/SelectAttackingTarget, ScriptedAI/Ambush, ScriptedAI/LeaveVanish | — | — |
| JustDied | method | ScriptedAI/LeaveVanish | — | — |
| UpdateAI | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoMeleeAttackIfReady, ScriptedAI/Ambush, ScriptedAI/EnterVanish, ScriptedAI/LeaveVanish, shared_Util/urand, Unit.Main/IsInCombat | — | — |
| GetAI_npc_sandstalker | function | — | — | — |
| AddSC_npc_sandstalker | function | Script/Script, ScriptMgr/RegisterSelf | — | — |
