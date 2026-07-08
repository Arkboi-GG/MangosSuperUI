<!-- provenance: verbose -->
# boss_instructor_malicia

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_instructor_malicia

**Purpose & Responsibilities**  
This unit implements the combat AI for **Instructor Malicia**, a boss in the Scholomance dungeon. It manages timed offensive and healing spell rotations and reports defeat status to the instance script.

**Member-by-Member Behavior**  

### Initialization & State
- **`boss_instructormaliciaAI` (ctor)**: Inherits from `ScriptedAI` and calls `Reset()` to initialize timers.
- **`Reset`**: Sets initial cooldowns:
  - `CallOfGraves`: 4s initial, 65s cycle.
  - `Corruption`: 8s initial, 24s cycle.
  - `FlashHeal`: 22s initial, variable cycle.
  - `Renew`: 15s initial, 10s cycle.
  - `HealingTouch`: 25s initial, variable cycle.
  - Counters (`FlashCounter`, `TouchCounter`) start at 0.
- **`JustDied`**: Retrieves `ScriptedInstance` via `m_creature->GetInstanceData()` and calls `SetData(TYPE_MALICIA, DONE)` to mark the boss defeated.

### Combat Logic (`UpdateAI`)
The main loop decrements timers and casts spells when thresholds are met. All spells use `DoCastSpellIfCan`.

1. **Call of Graves** (`SPELL_CALLOFGRAVES`): Cast on the current victim every ~65s.
2. **Corruption** (`SPELL_CORRUPTION`): Cast on a random hostile target (`SelectAttackingTarget`) every ~24s. Checks for `nullptr` before casting.
3. **Renew** (`SPELL_RENEW`): Self-cast every ~10s.
4. **Flash Heal Burst** (`SPELL_FLASHHEAL`): 
   - Initial delay: 22s.
   - If `FlashCounter < 2`, casts again in 5s (incrementing counter).
   - Else, resets counter and sets next cast to 30s.
   - Result: Two rapid heals every ~35s.
5. **Healing Touch Burst** (`SPELL_HEALINGTOUCH`): 
   - Intended to mirror Flash Heal, but contains a **bug**: `if (HealingTouch_Timer < 2)` compares a millisecond timer to 2, which is always false after the first cast.
   - Result: The burst logic fails; it casts once at 25s, then every 30s indefinitely. `TouchCounter` is incremented but never triggers the reset branch correctly.
6. **Melee**: `DoMeleeAttackIfReady()` is called every tick.

**Cross-Unit Boundaries**  
- **`ScriptedAI`**: Base class for `DoCastSpellIfCan`, `DoMeleeAttackIfReady`, and `m_creature` access.
- **`Creature`**: Used for `SelectHostileTarget`, `GetVictim`, `SelectAttackingTarget`, and `GetInstanceData`.
- **`ScriptedInstance`**: Accessed in `JustDied` to update instance state.
- **`ScriptMgr`**: `AddSC_boss_instructormalicia` registers the script.

**Data Model**  
No database tables are accessed.

**Notable Implementation Details**  
1. **Healing Touch Bug**: The condition `if (HealingTouch_Timer < 2)` is logically incorrect. It should compare `TouchCounter < 2`. This breaks the intended burst mechanic.
2. **Timer Arithmetic**: Manual timer management (`timer -= diff`) is used.
3. **Target Validation**: `SelectAttackingTarget` may return `nullptr`; the code checks for this before casting Corruption. Other spells assume `GetVictim()` is valid (guarded by early return).

## Member Reference

**boss_instructormaliciaAI**  
Constructor that initializes the AI by calling `Reset()`. Inherits from `ScriptedAI`.

**Reset**  
Initializes all spell timers and counters to their starting values. Called on spawn/reset.

**JustDied**  
Updates the instance script to mark Malicia as defeated (`TYPE_MALICIA = DONE`). Uses `m_creature->GetInstanceData()` to retrieve the `ScriptedInstance` object.

**UpdateAI**  
Main combat loop. Manages five spell timers:
- `CallOfGraves`: Single-target damage on victim every ~65s.
- `Corruption`: Random-target debuff/damage every ~24s.
- `Renew`: Self-heal over time every ~10s.
- `FlashHeal`: Burst mechanic (two casts 5s apart, then 30s cooldown).
- `HealingTouch`: Intended burst mechanic, but broken due to timer comparison bug.
Also handles melee attacks via `DoMeleeAttackIfReady()`.

**GetAI_boss_instructormalicia**  
Factory function that creates and returns a new `boss_instructormaliciaAI` instance for a given `Creature`.

**AddSC_boss_instructormalicia**  
Registration function. Creates a `Script` object, assigns the name `"boss_instructor_malicia"` and the `GetAI` factory, then registers it with the `ScriptMgr`. Called by `ScriptLoader::AddScripts`.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_instructor_malicia

*Source:* boss_instructor_malicia.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_instructormaliciaAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | method | — | — | — |
| JustDied | method | InstanceData/SetData, WorldObject.Object/GetInstanceData | — | — |
| UpdateAI | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_boss_instructormalicia | function | — | — | — |
| AddSC_boss_instructormalicia | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
