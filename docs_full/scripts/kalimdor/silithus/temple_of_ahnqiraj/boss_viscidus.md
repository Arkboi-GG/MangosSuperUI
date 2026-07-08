# boss_viscidus

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_viscidus

**Purpose & Responsibilities**

`boss_viscidus.cpp` implements the artificial intelligence and encounter logic for **Viscidus**, a boss creature in the *Temple of Ahn'Qiraj* instance. The unit manages three distinct AI behaviors:
1.  **The Boss (`boss_viscidusAI`)**: Handles Viscidus's primary combat mechanics, including poison attacks, toxin cloud summons, and a complex "freeze/shatter" mechanic where sustained frost damage causes the boss to explode into smaller globules. It also manages the boss's size scaling based on health and the reformation process when globules return.
2.  **The Globules (`mob_viscidus_globAI`)**: Controls the behavior of `NPC_GLOB_OF_VISCIDUS`, small entities spawned when Viscidus explodes. They move toward players and accelerate over time.
3.  **The Trigger (`mob_viscidus_triggerAI`)**: Controls `NPC_VISCIDUS_TRIGGER`, a temporary entity summoned to create a stationary Toxin Cloud area-of-effect hazard.

The unit relies heavily on spell triggers, timers, and health thresholds to transition between phases (Normal, Frozen, Exploded). It interacts with the instance data system to report encounter status (In Progress, Fail, Done).

## Member-by-Member Behavior

### Global Helpers & Registration

*   **`AddSC_boss_viscidus`**: Registers the three scripts (`boss_viscidus`, `mob_viscidus_glob`, `mob_viscidus_trigger`) with the server's `ScriptMgr`. It links the `GetAI` functions and the `EffectAuraDummy` handler for the freeze spell.
*   **`EffectAuraDummy_spell_aura_dummy_viscidus_freeze`**: A global spell effect handler. When the `SPELL_VISCIDUS_FREEZE` aura is removed from the boss (indicating the freeze duration ended), it calls `ResetFrozenPhase` on the `boss_viscidusAI` instance to return the boss to normal combat behavior.

### Globule AI (`mob_viscidus_globAI`)

*   **`mob_viscidus_globAI` (ctor)**: Initializes the globule AI with a 4-second delay timer before acceleration begins.
*   **`Reset#2`**, **`AttackStart`**, **`MoveInLineOfSight#2`**: Dummy overrides that perform no action, ensuring globules do not engage in standard combat aggro loops.
*   **`UpdateAI#2`**: Manages the acceleration mechanic. After the initial 4-second delay, it casts `SPELL_GLOB_SPEED` once. This spell applies an aura that doubles the globule's speed every second, simulating rapid acceleration toward targets.
*   **`GetAI_mob_viscidus_glob`**: Factory function returning a new `mob_viscidus_globAI` instance.

### Trigger AI (`mob_viscidus_triggerAI`)

*   **`mob_viscidus_triggerAI` (ctor)**: Initializes the trigger AI with a 3-second delay timer.
*   **`Reset#3`**, **`AttackStart#2`**, **`MoveInLineOfSight#3`**: Dummy overrides preventing standard combat behavior.
*   **`UpdateAI#3`**: After a 3-second delay, it sets the trigger's faction to hostile (to allow visual targeting/effects if needed, though it is flagged non-attackable) and casts `SPELL_TOXIN_CLOUD` instantly. It then applies `SPELL_TOXIN`, an aura that continuously repeats the cloud spell, creating a persistent hazardous zone.
*   **`GetAI_mob_viscidus_trigger`**: Factory function returning a new `mob_viscidus_triggerAI` instance.

### Boss AI (`boss_viscidusAI`)

#### Initialization & State Management

*   **`boss_viscidusAI` (ctor)**: Retrieves the instance data, stores the initial object scale, and calls `Reset`.
*   **`Reset`**: Resets all timers (Toxin, Poison Shock, Poison Bolt Volley) to random intervals. Resets phase to Normal, hit count to 0, and restores the boss's scale to its initial value. Applies defensive auras (`SPELL_MEMBRANE_VISCIDUS`, `SPELL_VISCIDUS_WEAKNESS`). Calls `ResetViscidusState` to ensure visibility and combat readiness.
*   **`ResetViscidusState`**: Ensures the boss is visible, removes death/spawning flags, clears feign death state, resets threat lists, and enables combat movement. If the boss was invisible (post-explosion), it teleports them back to the center.
*   **`Aggro`**: Notifies the instance that the encounter has started (`IN_PROGRESS`).
*   **`JustReachedHome`**: Notifies the instance of failure (`FAIL`) and casts `SPELL_DESPAWN_GLOBS` to clean up any remaining globules.
*   **`JustDied`**: Notifies the instance of success (`DONE`).

#### Combat & Targeting

*   **`MoveInLineOfSight`**: Overrides default behavior to aggressively pull players within 95 yards if the boss has no current victim.
*   **`UpdateAI`**: The main loop.
    *   **Scaling/Growth**: Handles `HackyScaleUpdate` to shrink the boss as health drops. Manages `m_uiGrowTimer` to gradually increase scale when globules return (`SPELL_VISCIDUS_GROWS`).
    *   **Explosion Delay**: If in the explosion sequence, waits 2.5 seconds before hiding the boss and reducing scale based on remaining globules.
    *   **Target Restoration**: After casting Toxin Cloud, temporarily faces the trigger, then restores focus to the original victim after 800ms.
    *   **Abilities**: In Normal phase, casts `SPELL_POISON_SHOCK` and `SPELL_POISONBOLT_VOLLEY` on random timers. Summons `NPC_VISCIDUS_TRIGGER` to create Toxin Clouds on random targets every 30–40 seconds.
    *   **Melee**: Performs melee attacks if ready.
    *   **Evade**: Checks if out of combat area.

#### Phase & Mechanic Logic

*   **`SpellHit`**: Critical mechanic handler.
    *   **Explosion**: If hit by `SPELL_VISCIDUS_EXPLODE`, checks health. If below 5%, commits suicide via `SPELL_VISCIDUS_SUICIDE` and direct damage. Otherwise, enters `PHASE_EXPLODED`, clears existing globules, and summons new globules based on current health percentage (1 glob per 5% HP). Starts the explosion delay timer.
    *   **Freeze/Slow**: Tracks frost damage hits (`m_uiHitCount`).
        *   At 100 hits: Applies `SPELL_VISCIDUS_SLOWED` and emotes.
        *   At 150 hits: Removes slow, applies `SPELL_VISCIDUS_SLOWED_MORE`, and emotes.
        *   At 200 hits: Enters `PHASE_FROZEN`, applies `SPELL_VISCIDUS_FREEZE`, and emotes.
    *   **Wand Handling**: Specifically checks if a wand shot is frost-based by inspecting the player's equipped item prototype, ensuring wands contribute to the freeze counter.
*   **`ResetFrozenPhase`**: Called by the aura dummy handler when the freeze ends. Returns the boss to `PHASE_NORMAL` and resets the hit counter, unless the boss is already exploded.
*   **`HackyScaleUpdate`**: Shrinks the boss visually (`SPELL_VISCIDUS_SHRINKS`) every time health drops by 5%, using a bitset to prevent redundant casts.

#### Summon Management

*   **`JustSummoned`**: Moves newly spawned globules to the center of the room and adds their GUIDs to the tracking list.
*   **`SummonedCreatureJustDied`**: When a globule dies, reduces the boss's health by 5% (via `SPELL_VISCIDUS_SHRINKS_HP` and manual calculation). If no globules remain, resets the boss state.
*   **`SummonedMovementInform`**: When a globule reaches its destination (rejoining the boss), it casts `SPELL_REJOIN_VISCIDUS`, despawns, and increments the growth counter. If no globules remain, resets the boss state.

#### Factory

*   **`GetAI_boss_viscidus`**: Factory function returning a new `boss_viscidusAI` instance.

## Cross-Unit Boundaries

*   **`ScriptedAI`**: All three AI classes inherit from `ScriptedAI` (in `ScriptedAI/ScriptedAI`), providing base functionality for timers, threat management, and spell casting helpers like `DoCastSpellIfCan`.
*   **`SpellCaster`**: Used extensively via `CastSpell#2` and `DoCastSpellIfCan` to trigger abilities.
*   **`Unit` / `WorldObject`**: Used for state manipulation (`SetFactionTemplateId`, `SetFlag`, `SetObjectScale`, `SetVisibility`, `GetHealth`, etc.) and spatial queries (`IsWithinDistInMap`, `GetPositionX/Y/Z`).
*   **`CreatureAI`**: Base class methods like `DoMeleeAttackIfReady`, `DoCast`, and `MoveInLineOfSight` are overridden or called.
*   **`InstanceData`**: `SetData` is called in `Aggro`, `JustReachedHome`, and `JustDied` to update the raid instance's progress state.
*   **`ScriptMgr`**: `DoScriptText` is used to broadcast emotes. `RegisterSelf` is used in `AddSC_boss_viscidus`.
*   **`Player` / `game_Objects_Item`**: In `SpellHit`, the code casts `pCaster` to `Player` and accesses inventory items to determine if a wand shot is frost-based.

## Data Model

This unit does not directly query or modify database tables. It interacts with runtime memory structures (Creature, Unit, Aura, InstanceData). The `SCHEMA` section is empty/not applicable.

## Notable Implementation Details

1.  **Wand Frost Detection**: The `SpellHit` method contains specific logic to detect frost damage from wands. Since wand shots register as physical damage in the spell school, the code inspects the player's equipped ranged weapon prototype to check if its damage type is frost. This ensures wands correctly contribute to the freeze mechanic.
2.  **Health-Based Scaling**: The boss's visual scale is tied to its health. `HackyScaleUpdate` uses a `std::bitset<20>` to track which 5% health thresholds have been passed, casting a shrink spell only once per threshold. Conversely, `UpdateAI` increases scale when globules return (`m_uiGrowCount`).
3.  **Explosion Suicide Check**: If Viscidus explodes while below 5% health, he does not reform into globules. Instead, `SPELL_VISCIDUS_SUICIDE` is cast, and direct damage equal to his current health is dealt to himself, killing him immediately.
4.  **Toxin Cloud Visuals**: The Toxin Cloud mechanic involves summoning a trigger creature, making it temporarily selectable for visual effects, casting a spell on it that is resisted (for visuals), facing the boss toward it, and then restoring the boss's target after 800ms. This is a "hacky" way to simulate a targeted AoE cast animation.
5.  **Globule Acceleration**: Globules do not have constant speed. They start slow, wait 4 seconds, then cast a spell that doubles their speed every second via an aura tick. This creates a smooth acceleration curve.
6.  **Phase Locking**: During `PHASE_FROZEN` and `PHASE_EXPLODED`, most offensive abilities (Poison Shock, Poison Bolt Volley, Toxin Cloud) are disabled in `UpdateAI` until the phase resets.

## Member Reference

**mob_viscidus_globAI** (ctor): Initializes the globule AI with a 4-second acceleration delay timer and a flag to prevent repeated spell casts. Inherits from `ScriptedAI`.

**Reset#2**: Dummy override for `mob_viscidus_globAI`; performs no action.

**AttackStart**: Dummy override for `mob_viscidus_globAI`; prevents standard aggro.

**MoveInLineOfSight#2**: Dummy override for `mob_viscidus_globAI`; prevents standard aggro.

**UpdateAI#2**: Manages globule acceleration. After a 4-second delay, casts `SPELL_GLOB_SPEED` once, which applies an aura that doubles speed every second.

**GetAI_mob_viscidus_glob**: Factory function that returns a new `mob_viscidus_globAI` instance for a given creature.

**mob_viscidus_triggerAI** (ctor): Initializes the trigger AI with a 3-second delay timer for toxin cloud generation. Inherits from `ScriptedAI`.

**Reset#3**: Dummy override for `mob_viscidus_triggerAI`; performs no action.

**AttackStart#2**: Dummy override for `mob_viscidus_triggerAI`; prevents standard aggro.

**MoveInLineOfSight#3**: Dummy override for `mob_viscidus_triggerAI`; prevents standard aggro.

**UpdateAI#3**: After a 3-second delay, sets the trigger to a hostile faction but non-attackable, then casts `SPELL_TOXIN_CLOUD` and applies `SPELL_TOXIN` to maintain the cloud.

**GetAI_mob_viscidus_trigger**: Factory function that returns a new `mob_viscidus_triggerAI` instance for a given creature.

**boss_viscidusAI** (ctor): Initializes the boss AI, retrieves instance data, stores initial scale, and calls `Reset`. Inherits from `ScriptedAI`.

**Reset**: Resets all combat timers, phase, hit counts, and scale. Applies defensive auras and calls `ResetViscidusState`.

**MoveInLineOfSight**: Aggressively pulls players within 95 yards if the boss has no victim. Calls parent `MoveInLineOfSight`.

**Aggro**: Sets the instance data state to `IN_PROGRESS`.

**JustReachedHome**: Sets the instance data state to `FAIL` and casts `SPELL_DESPAWN_GLOBS` to clean up.

**JustDied**: Sets the instance data state to `DONE`.

**JustSummoned**: Moves summoned globules to the room center and adds their GUIDs to the tracking list.

**ResetViscidusState**: Restores boss visibility, removes death/spawning flags, clears threat, enables combat movement, and teleports if necessary.

**SummonedCreatureJustDied**: Reduces boss health by 5% when a globule dies. Resets boss state if no globules remain.

**SummonedMovementInform**: Handles globules rejoining the boss. Casts rejoin spell, despawns globule, increments growth counter, and resets state if no globules remain.

**SpellHit**: Handles frost damage accumulation for freeze/slow mechanics. Detects wand frost damage via item prototype. Handles explosion logic: if below 5% HP, suicides; otherwise, spawns globules based on health and enters exploded phase.

**ResetFrozenPhase**: Resets the boss to Normal phase and clears hit count when the freeze aura expires, unless already exploded.

**HackyScaleUpdate**: Shrinks the boss visually every 5% health drop using a bitset to track thresholds.

**UpdateAI**: Main combat loop. Manages scaling, explosion delays, target restoration, and ability timers (Poison Shock, Poison Bolt Volley, Toxin Cloud). Disables abilities during Frozen/Exploded phases. Performs melee attacks.

**GetAI_boss_viscidus**: Factory function that returns a new `boss_viscidusAI` instance for a given creature.

**EffectAuraDummy_spell_aura_dummy_viscidus_freeze**: Global handler that calls `ResetFrozenPhase` on the boss when the freeze aura is removed.

**AddSC_boss_viscidus**: Registers the three scripts (boss, glob, trigger) with the `ScriptMgr`.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_viscidus

*Source:* boss_viscidus.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| mob_viscidus_globAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#2 | method | — | — | — |
| AttackStart | method | — | — | — |
| MoveInLineOfSight#2 | method | — | — | — |
| UpdateAI#2 | method | SpellCaster/CastSpell#2 | — | — |
| GetAI_mob_viscidus_glob | function | — | — | — |
| mob_viscidus_triggerAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#3 | method | — | — | — |
| AttackStart#2 | method | — | — | — |
| MoveInLineOfSight#3 | method | — | — | — |
| UpdateAI#3 | method | SpellCaster/CastSpell#2, Unit.Main/SetFactionTemplateId, WorldObject.Object/SetFlag | — | — |
| GetAI_mob_viscidus_trigger | function | — | — | — |
| boss_viscidusAI | ctor | Object/GetObjectScale, ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | CreatureAI/DoCastSpellIfCan, shared_Util/urand, WorldObject.Object/SetObjectScale | — | — |
| MoveInLineOfSight | method | BasicAI/MoveInLineOfSight, CreatureAI/AttackStart, Object/GetTypeId, Unit.Main/GetVictim, WorldObject.Object/IsWithinDistInMap | — | — |
| Aggro | method | InstanceData/SetData | — | — |
| JustReachedHome | method | CreatureAI/DoCastSpellIfCan, InstanceData/SetData | — | — |
| JustDied | method | InstanceData/SetData | — | — |
| JustSummoned | method | Creature.MotionMaster/MovePoint, Object/GetEntry, Object/GetObjectGuid, Unit.Main/GetMotionMaster | — | — |
| ResetViscidusState | method | CreatureAI/DoCast, CreatureAI/SetCombatMovement, ScriptedAI/DoResetThreat, Unit.Main/ClearUnitState, Unit.Main/GetVisibility, Unit.Main/SetVisibility, WorldObject.Object/RemoveFlag | — | — |
| SummonedCreatureJustDied | method | CreatureAI/DoCastSpellIfCan, Object/GetEntry, Object/GetObjectGuid, Unit.Main/GetHealth, Unit.Main/GetMaxHealth, Unit.Main/SetHealth, Unit.Main/SetHealthPercent | — | — |
| SummonedMovementInform | method | Creature.Main/ForcedDespawn, Object/GetEntry, Object/GetObjectGuid, SpellCaster/CastSpell#2 | — | — |
| SpellHit | method | CreatureAI/DoCastSpellIfCan, game_Objects_Item/GetProto, Player.Main/GetItemByPos, ScriptMgr/DoScriptText, Unit.Main/DealDamage, Unit.Main/GetHealth, Unit.Main/GetHealthPercent, Unit.Main/RemoveAurasDueToSpell, Unit.Main/SetInvincibilityHpThreshold, Unit.Main/SetVisibility | — | — |
| ResetFrozenPhase | method | — | — | — |
| HackyScaleUpdate | method | SpellCaster/CastSpell#2, Unit.Main/GetHealthPercent | — | — |
| UpdateAI | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Object/GetObjectGuid, Object/GetObjectScale, ScriptedAI/EnterEvadeIfOutOfCombatArea, shared_Util/urand, SpellCaster/CastSpell#2, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, Unit.Main/SetInFront, Unit.Main/SetTargetGuid, Unit.Main/SetVisibility, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag, WorldObject.Object/SetObjectScale, WorldObject.Object/SummonCreature#2 | — | — |
| GetAI_boss_viscidus | function | — | — | — |
| EffectAuraDummy_spell_aura_dummy_viscidus_freeze | function | Aura/GetEffIndex, Aura/GetId, Aura/GetTarget, Creature.Main/AI | — | — |
| AddSC_boss_viscidus | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
