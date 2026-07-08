<!-- provenance: verbose -->
# boss_zevrim

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`boss_zevrim.cpp` implements the AI for **Zevrim**, a boss in the **Dire Maul** dungeon. The `boss_zevrimAI` class manages two timed abilities—**Intensive Pain** (self-targeted) and **Sacrifice** (random hostile target, excluding pets)—alongside standard melee attacks. It integrates with the `instance_dire_maul` script to report death status and registers itself with the server’s script manager.

## Member-by-Member Behavior

### Initialization & Lifecycle

**`boss_zevrimAI`**  
Constructs the AI, casting the creature’s instance data to `instance_dire_maul*` and storing it in `m_pInstance`. Immediately calls `Reset()` to initialize timers with random offsets.

**`Reset`**  
Sets `m_uiIntensePainTimer` to 5–9 seconds and `m_uiSacrificeTimer` to 9–12 seconds using `urand` from `shared_Util`, preventing synchronized casts.

### Combat Logic

**`UpdateAI`**  
Executes on each tick. Returns early if no hostile target, no victim, or a non-melee spell is casting.  
- **Intensive Pain**: When `m_uiIntensePainTimer` expires, casts `SPELL_INTENSIVE_PAIN` (22478) on self via `DoCastSpellIfCan`, then resets the timer to 20–26 seconds.  
- **Sacrifice**: When `m_uiSacrificeTimer` expires, selects a random attacking target (excluding totems) via `SelectAttackingTarget`. If the target is not a pet (`!IsPet()`), casts `SPELL_SACRIFICE` (22651) on them and resets the timer to 15–18 seconds. If the target is a pet, the cast is skipped and the timer is **not** reset, forcing a retry on the next tick.  
- **Melee**: Calls `DoMeleeAttackIfReady()` for physical attacks.

### Death & Registration

**`JustDied`**  
Notifies the `instance_dire_maul` script via `SetData(TYPE_BOSS_ZEVRIM, DONE)` if `m_pInstance` is valid.

**`GetAI_boss_zevrim`**  
Factory function returning a new `boss_zevrimAI` instance for a `Creature`.

**`AddSC_boss_zevrim`**  
Registers the script with `ScriptMgr` by creating a `Script` object named `"boss_zevrim"`, assigning `GetAI_boss_zevrim` as the AI getter, and calling `RegisterSelf()`. Called by `ScriptLoader::AddScripts`.

## Cross-Unit Boundaries

- **`instance_dire_maul`**: Called by `JustDied` to update instance state upon boss death.
- **`ScriptedAI`**: Base class providing `DoCastSpellIfCan`, `DoMeleeAttackIfReady`, and timer infrastructure.
- **`Creature`/`Unit`**: Used in `UpdateAI` for target selection (`SelectHostileTarget`, `GetVictim`, `SelectAttackingTarget`) and state checks (`IsPet`, `IsNonMeleeSpellCasted`).
- **`shared_Util`**: Provides `urand` for randomized timer intervals.
- **`ScriptMgr`/`Script`**: Used in `AddSC_boss_zevrim` for global script registration.

## Data Model

This unit does not interact with any database tables.

## Notable Implementation Details

- **Pet Exclusion Retry**: In `UpdateAI`, if `Sacrifice` selects a pet, the spell is not cast and the timer is not reset. This ensures the ability is never wasted on pets, but may delay the next cast if only pets remain on the threat list.
- **Instance Cast Safety**: The constructor casts `GetInstanceData()` directly to `instance_dire_maul*`. This assumes Zevrim only spawns in Dire Maul; moving the creature elsewhere without code changes risks undefined behavior.

## Member Reference

**`boss_zevrimAI`**  
Constructor initializing `m_pInstance` from creature data and calling `Reset()`.

**`JustDied`**  
Calls `m_pInstance->SetData(TYPE_BOSS_ZEVRIM, DONE)` to mark the boss as defeated.

**`Reset`**  
Randomizes `m_uiIntensePainTimer` (5–9s) and `m_uiSacrificeTimer` (9–12s) using `urand`.

**`UpdateAI`**  
Manages combat ticks: casts `SPELL_INTENSIVE_PAIN` on self periodically; casts `SPELL_SACRIFICE` on a random non-pet, non-totem target periodically (retrying if target is a pet); performs melee attacks.

**`GetAI_boss_zevrim`**  
Returns a new `boss_zevrimAI` instance for a `Creature`.

**`AddSC_boss_zevrim`**  
Registers the "boss_zevrim" script with `ScriptMgr` using `GetAI_boss_zevrim` as the AI provider.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_zevrim

*Source:* boss_zevrim.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_zevrimAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| JustDied | method | instance_dire_maul/SetData | — | — |
| Reset | method | shared_Util/urand | — | — |
| UpdateAI | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Object/IsPet, shared_Util/urand, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_boss_zevrim | function | — | — | — |
| AddSC_boss_zevrim | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
