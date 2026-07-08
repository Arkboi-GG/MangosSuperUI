<!-- provenance: verbose -->
# boss_tendris_warpwood

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_tendris_warpwood

## Purpose & Responsibilities

`boss_tendris_warpwood.cpp` implements the AI for **Tendris Warpwood**, a boss in the Dire Maul instance. The `boss_tendris_warpwoodAI` class manages combat abilities (Trample, Uppercut, Grasping Vines, Enrage), a unique "Invocation" mechanic that teleports distant targets, and patch-specific behavior for summoning allies. It coordinates with the `instance_dire_maul` script to track encounter state and summons an **Ancient Equine Spirit** upon death.

## Member-by-Member Behavior

### Initialization and Lifecycle

*   **`boss_tendris_warpwoodAI` (Constructor)**: Casts the creature’s instance data to `instance_dire_maul*`, initializes `m_uiAggroProtector` to `false`, and calls `Reset()`.
*   **`Reset`**: Randomizes timers for Trample (5–9s), Uppercut (2–4s), Grasping Vines (9–12s), and Invocation (0s) using `urand`. Resets `m_uiAggroProtector` to `false`.

### Combat Mechanics

*   **`UpdateAI`**: The main tick loop. It returns early if no target/victim exists or a non-melee spell is casting. It then:
    1.  Checks `ManageTimer` for **Trample**, **Uppercut**, and **Grasping Vines**, casting them via `DoCastSpellIfCan` with randomized cooldowns (9–14s, 12–15s, 17–22s respectively).
    2.  Casts **Enrage** (`SPELL_ENRAGE`) if health drops below 30% and the aura is absent.
    3.  Handles the **Invocation** mechanic: if `m_uiInvocation_Timer` expires and the victim is >7.0 units away, it sends a spell visual (`SendSpellGo` ID 25681), teleports the victim to Tendris’s location (`NearTeleportTo`), casts **Entangle** (`SPELL_ENCHEVETREMENT`), and resets the timer (10–15s).
    4.  Executes melee attacks via `DoMeleeAttackIfReady`.
*   **`ManageTimer`**: Helper that decrements a timer by `diff`. If the timer expires (< `diff`), it resets to `cooldown` and returns `true`; otherwise, it returns `false`.

### Event Handlers

*   **`Aggro`**: If `m_uiAggroProtector` is false:
    *   On patch 1.10.0+, it finds all **Ironbark Protectors** within 1800.0 units via `GetCreatureListWithEntryInGrid` and forces alive ones into combat with `SetInCombatWithZone`.
    *   Sets `m_uiAggroProtector` to `true` and plays aggro text (`SAY_TENDRIS_AGGRO`).
*   **`AttackStart`**: Calls parent `AttackStart` and notifies the instance script via `m_pInstance->SetData(DATA_TENDRIS_AGGRO, IN_PROGRESS)`.
*   **`JustDied`**: Summons an **Ancient Equine Spirit** (`NPC_ANCIENT_EQUINE_SPIRIT`) at the killer’s coordinates with a 60-second despawn timer.

### Registration

*   **`GetAI_boss_tendris_warpwood`**: Factory function returning a new `boss_tendris_warpwoodAI`.
*   **`AddSC_boss_tendris_warpwood`**: Registers the script with `ScriptMgr` via `Script::RegisterSelf`.

## Cross-Unit Boundaries

*   **`ScriptedAI`**: Base class providing `DoCastSpellIfCan`, `DoMeleeAttackIfReady`, and `AttackStart`.
*   **`WorldObject`**: Provides `GetInstanceData` (constructor), positional/orientation getters (`GetPositionX/Y/Z`, `GetOrientation`, `GetDistance`) in `JustDied` and `UpdateAI`.
*   **`Creature`**: Target for `SetInCombatWithZone` in `Aggro`.
*   **`GridSearchers`**: `GetCreatureListWithEntryInGrid` used in `Aggro` to locate protectors.
*   **`ScriptMgr`**: `DoScriptText` for aggro sounds; `RegisterSelf` for script registration.
*   **`Unit`**: Methods like `IsAlive`, `GetHealthPercent`, `GetVictim`, `HasAura`, `NearTeleportTo`, `SelectHostileTarget`, `SendSpellGo`, and `CastSpell` are used throughout `UpdateAI` and `Aggro`.
*   **`World`**: `GetWowPatch` determines if patch 1.10.0+ protector logic applies.
*   **`instance_dire_maul`**: `SetData` updates encounter state in `AttackStart`.
*   **`shared_Util`**: `urand` randomizes timers.
*   **`SpellCaster`**: `CastSpell` and `IsNonMeleeSpellCasted` manage spell states.
*   **`Script` / `ScriptLoader`**: Infrastructure for script registration.

## Data Model

This unit does not interact with any database tables.

## Notable Implementation Details

*   **Patch-Specific Aggro**: `Aggro` checks `sWorld.GetWowPatch() >= WOW_PATCH_110` before aggroing Ironbark Protectors, reflecting a historical change in WoW patch 1.10.0.
*   **Invocation Teleport**: The `UpdateAI` method manually handles a pull mechanic: it sends a visual spell effect, teleports the target, and then casts Entangle. This bypasses standard spell targeting for the teleport itself.
*   **Death Location**: The Ancient Equine Spirit spawns at the *killer’s* position, not the boss’s corpse.

## Member Reference

*   **`boss_tendris_warpwoodAI`**: Constructor initializing instance data, protector flag, and calling `Reset()`.
*   **`ManageTimer`**: Helper decrementing a timer; resets to cooldown and returns true if expired.
*   **`JustDied`**: Summons Ancient Equine Spirit at killer’s location with 60s despawn.
*   **`Aggro`**: Plays aggro text; on patch 1.10.0+, aggroes nearby Ironbark Protectors. Sets protector flag.
*   **`Reset`**: Randomizes spell timers and resets protector flag.
*   **`AttackStart`**: Calls parent `AttackStart` and updates instance data to `IN_PROGRESS`.
*   **`UpdateAI`**: Manages spell rotation, Enrage phase, Invocation teleport mechanic, and melee attacks.
*   **`GetAI_boss_tendris_warpwood`**: Factory function creating `boss_tendris_warpwoodAI`.
*   **`AddSC_boss_tendris_warpwood`**: Registers the script with `ScriptMgr`.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_tendris_warpwood

*Source:* boss_tendris_warpwood.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_tendris_warpwoodAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| ManageTimer | method | — | — | — |
| JustDied | method | WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SummonCreature#2 | — | — |
| Aggro | method | Creature.Main/SetInCombatWithZone, GridSearchers/GetCreatureListWithEntryInGrid#2, ScriptMgr/DoScriptText, Unit.Main/IsAlive, World/GetWowPatch | — | — |
| Reset | method | shared_Util/urand | — | — |
| AttackStart | method | CreatureAI/AttackStart, instance_dire_maul/SetData | — | — |
| UpdateAI | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, SpellCaster/CastSpell#2, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/GetHealthPercent, Unit.Main/GetVictim, Unit.Main/HasAura#2, Unit.Main/NearTeleportTo, Unit.Main/SelectHostileTarget, Unit.Main/SendSpellGo, WorldObject.Object/GetDistance#3, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | — | — |
| GetAI_boss_tendris_warpwood | function | — | — | — |
| AddSC_boss_tendris_warpwood | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
