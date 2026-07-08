<!-- provenance: verbose -->
# boss_baron_geddon

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`boss_baron_geddon.cpp` implements the AI for **Baron Geddon**, a boss in the **Molten Core** instance. The `boss_baron_geddonAI` class manages combat phases, spell rotations, and instance state reporting. Key behaviors include:
1.  **Standard Rotation:** Casting `Living Bomb` on random players, `Ignite Mana` on self, and `Inferno` (a self-rooting, escalating damage sequence).
2.  **Armageddon Phase:** At <5% health, the boss interrupts actions, stops moving, and casts `Armageddon`, halting all other logic until death.
3.  **Instance Integration:** Reports status (`NOT_STARTED`, `IN_PROGRESS`, `DONE`) to the `ScriptedInstance`.

No database tables are accessed.

## Member-by-Member Behavior

### Initialization & Lifecycle

**`boss_baron_geddonAI` (Constructor)**
Retrieves the `ScriptedInstance` pointer from the creature and calls `Reset()` to initialize timers and state flags.

**`Reset`**
Resets timers for `Ignite Mana` (10–15s), `Living Bomb` (15–20s), and `Inferno` (18–24s) using `urand`. Clears `UNIT_STATE_ROOT`, enables combat movement, and reports `NOT_STARTED` to the instance if the creature is alive.

**`Aggro`**
Reports `IN_PROGRESS` to the instance and marks the creature in combat with the zone.

**`JustDied`**
Reports `DONE` to the instance.

### Combat Logic (`UpdateAI`)

The main loop exits early if no hostile target exists or if `m_bArmageddon` is true.

**Armageddon Trigger**
If health drops below 5% and `m_bArmageddon` is false:
1.  Interrupts non-melee spells and disables combat movement.
2.  Casts `SPELL_ARMAGEDDOM` (20478) on self.
3.  Plays emote `EMOTE_SERVICE` (8253).
4.  Sets `m_bArmageddon = true` and returns, freezing further logic.

**Living Bomb**
When `m_uiLivingBombTimer` expires:
1.  Selects a random player target.
2.  If cast succeeds (`SPELL_LIVINGBOMB`, 20475), faces the target, sets its GUID as the current target, and resets the timer (12–15s).
3.  Sets `m_uiRestoreTargetTimer` to 800ms.

**Target Restoration**
If `m_uiRestoreTargetTimer` is active:
1.  After 800ms, re-faces the primary victim (`GetVictim`) and restores its GUID as the target.
2.  Prevents the boss from remaining focused on the Living Bomb target.

**Ignite Mana**
When `m_uiIgniteManaTimer` expires, casts `SPELL_IGNITEMANA` (19659) on self and resets the timer (20–30s).

**Inferno Sequence**
When `m_uiInfernoTimer` expires:
1.  Casts `SPELL_INFERNO` (19695) on self.
2.  Roots the creature (`UNIT_STATE_ROOT`), sets `m_bInferno = true`, and initializes `InfCount` (0) and `Tick` (1000ms).
3.  Resets the main Inferno timer (18–24s).

While `m_bInferno` is true:
1.  Waits for `Tick` to reach 1000ms.
2.  Calculates damage based on `InfCount`: 500 (ticks 0–1), 1000 (2–3), 2000 (4–5), 3000 (6), 5000 (7).
3.  Casts spell `19698` on self with the calculated damage via `CastCustomSpell`.
4.  Increments `InfCount`. On tick 7, sets `m_bInferno = false` and clears the root state.
5.  Returns early, skipping melee attacks during the sequence.

**Melee Attacks**
Calls `DoMeleeAttackIfReady()` if no special abilities are active or completing.

### Registration

**`GetAI_boss_baron_geddon`**
Factory function returning a new `boss_baron_geddonAI` instance.

**`AddSC_boss_baron_geddon`**
Creates a `Script` object named `"boss_baron_geddon"`, links the `GetAI` factory, and registers it with `ScriptMgr`. Called by `ScriptLoader::AddScripts`.

## Cross-Unit Boundaries

| Member | Direction | External Unit | Interaction Details |
| :--- | :--- | :--- | :--- |
| `boss_baron_geddonAI` | Calls | `ScriptedAI` | Inherits base AI functionality. |
| `boss_baron_geddonAI` | Calls | `WorldObject` | Retrieves instance data via `GetInstanceData()`. |
| `Reset` | Calls | `CreatureAI` | Enables combat movement. |
| `Reset` | Calls | `InstanceData` | Updates instance state to `NOT_STARTED`. |
| `Reset` | Calls | `shared_Util` | Generates random timer values. |
| `Reset` | Calls | `Unit.Main` | Clears root state and checks alive status. |
| `Aggro` | Calls | `Creature.Main` | Marks creature in combat with zone. |
| `Aggro` | Calls | `InstanceData` | Updates instance state to `IN_PROGRESS`. |
| `JustDied` | Calls | `InstanceData` | Updates instance state to `DONE`. |
| `UpdateAI` | Calls | `Creature.Main` | Selects targets, checks health, sets facing/GUIDs. |
| `UpdateAI` | Calls | `CreatureAI` | Handles spell casting, melee attacks, movement. |
| `UpdateAI` | Calls | `Object` | Gets GUIDs for targeting. |
| `UpdateAI` | Calls | `ScriptMgr` | Triggers emote text. |
| `UpdateAI` | Calls | `shared_Util` | Generates random cooldowns. |
| `UpdateAI` | Calls | `SpellCaster` | Casts custom spells (Inferno ticks) and standard spells. |
| `UpdateAI` | Calls | `Unit.Main` | Manages unit states (root), interrupts spells, selects victims. |
| `AddSC...` | Calls | `Script` / `ScriptMgr` | Registers the script definition. |
| `AddSC...` | Called By | `ScriptLoader` | Invoked during server startup. |

## Data Model

This unit does not interact with any database tables.

## Notable Implementation Details

1.  **Inferno Self-Damage:** The Inferno tick logic casts spell `19698` on `m_creature` (self). This deals damage to the boss rather than nearby players, which may be a specific design choice or a bug depending on the intended game behavior.
2.  **Hardcoded Spell ID:** The Inferno tick uses hardcoded spell ID `19698` in `CastCustomSpell`, distinct from the trigger spell `SPELL_INFERNO` (19695).
3.  **Armageddon Freeze:** Setting `m_bArmageddon = true` causes `UpdateAI` to return immediately, halting all other timers and logic. The boss remains rooted and immobile until death.
4.  **Target Restoration:** The 800ms `m_uiRestoreTargetTimer` ensures the boss briefly faces the Living Bomb target for visual consistency before snapping back to the primary victim.

## Member Reference

**`boss_baron_geddonAI`**
Constructor. Initializes instance data pointer and calls `Reset()`.

**`Reset`**
Resets timers, clears root state, enables movement, and reports `NOT_STARTED` to the instance.

**`Aggro`**
Reports `IN_PROGRESS` to the instance and marks the creature in combat.

**`JustDied`**
Reports `DONE` to the instance.

**`UpdateAI`**
Main loop. Handles Armageddon trigger (<5% HP), Living Bomb (random player target, 800ms restore), Ignite Mana (self), Inferno (self-root, escalating self-damage ticks), and melee attacks.

**`GetAI_boss_baron_geddon`**
Factory function returning a new `boss_baron_geddonAI` instance.

**`AddSC_boss_baron_geddon`**
Registers the script with `ScriptMgr` under the name "boss_baron_geddon".

---

<!-- machine-true, projected from graph.json -->

## Map — boss_baron_geddon

*Source:* boss_baron_geddon.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_baron_geddonAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | CreatureAI/SetCombatMovement, InstanceData/SetData, shared_Util/urand, Unit.Main/ClearUnitState, Unit.Main/IsAlive | — | — |
| Aggro | method | Creature.Main/SetInCombatWithZone, InstanceData/SetData | — | — |
| JustDied | method | InstanceData/SetData | — | — |
| UpdateAI | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, CreatureAI/SetCombatMovement, Object/GetObjectGuid, ScriptMgr/DoScriptText, shared_Util/urand, SpellCaster/CastCustomSpell#2, SpellCaster/CastSpell#2, SpellCaster/InterruptNonMeleeSpells, Unit.Main/AddUnitState, Unit.Main/ClearUnitState, Unit.Main/GetHealthPercent, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, Unit.Main/SetInFront, Unit.Main/SetTargetGuid | — | — |
| GetAI_boss_baron_geddon | function | — | — | — |
| AddSC_boss_baron_geddon | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
