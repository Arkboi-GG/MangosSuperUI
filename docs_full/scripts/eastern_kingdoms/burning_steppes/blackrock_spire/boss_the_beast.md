# boss_the_beast

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_the_beast

## Purpose & Responsibilities

`boss_the_beast` implements the AI for **The Beast** in Blackrock Spire. The `boss_thebeastAI` class manages a timer-driven rotation of fire-based spells, self-buffs, and a reactive summon mechanic triggered by player interactions. It inherits from `ScriptedAI` and registers itself via `AddSC_boss_thebeast`.

## Member-by-Member Behavior

### Initialization
*   **`boss_thebeastAI`**: Calls `ScriptedAI` constructor and immediately invokes `Reset()` to initialize timers.
*   **`Reset`**: Sets initial timer values. `m_uiFlamebreakTimer` and `m_uiFireBlastTimer` use `urand` for randomness; `m_uiTerrifyingRoarTimer` and `m_uiFireballTimer` are fixed; `m_uiBeserkerChargeTimer` is set to 0 to force an immediate cast on the first valid update cycle.

### Combat Loop (`UpdateAI`)
Executed every tick. If no hostile target exists, it returns early. Otherwise:
1.  **Immolate Maintenance**: If `AURA_IMMOLATE` (15506) is missing, it casts it on self.
2.  **Flamebreak**: Casts `SPELL_FLAMEBREAK` (16785) on self when timer expires (14–20s).
3.  **Terrifying Roar**: Casts `SPELL_TERRIFYINGROAR` (14100) on self when timer expires (16–18s).
4.  **Berserker Charge**: When timer expires (or is 0), selects a target. If timer was 0, it targets `GetVictim`; otherwise, it picks a random hostile target. Casts `SPELL_BERSERKER_CHARGE` (16636) and resets timer (15–20s).
5.  **Fireball**: Casts `SPELL_FIREBALL` (16788) on a random hostile target when timer expires (10–12s).
6.  **Fireblast**: Casts `SPELL_FIREBLAST` (14144) on `GetVictim` when timer expires (14–20s).
7.  **Melee**: Calls `DoMeleeAttackIfReady()`.

### Reactive Mechanics
*   **`SpellHit`**: If hit by a spell with `SPELL_EFFECT_SKINNING`, casts `SPELL_SUMMON_FINKLE` (16710) on the caster.

### Registration
*   **`GetAI_boss_thebeast`**: Factory function returning a new `boss_thebeastAI` instance.
*   **`AddSC_boss_thebeast`**: Creates a `Script` named `"boss_the_beast"`, assigns `GetAI_boss_thebeast`, and registers it via `ScriptMgr`. Called by `ScriptLoader/AddScripts`.

## Cross-Unit Boundaries

*   **`ScriptedAI`**: Base class providing `DoCastSpellIfCan` and `DoMeleeAttackIfReady`.
*   **`shared_Util`**: Provides `urand` for randomized timer intervals.
*   **`SpellCaster`**: `SpellHit` uses `CastSpell` to summon Finkle.
*   **`Creature`/`CreatureAI`**:
    *   `SelectHostileTarget`/`GetVictim`: Validate combat state and retrieve primary target.
    *   `HasAura`: Checks for `AURA_IMMOLATE`.
    *   `SelectAttackingTarget`: Picks random targets for Berserker Charge and Fireball.
*   **`Script`/`ScriptMgr`**: `AddSC_boss_thebeast` registers the script; `ScriptLoader/AddScripts` invokes it.

## Data Model

No database tables are accessed. All spell IDs and timers are hardcoded.

## Notable Implementation Details

*   **Immediate Berserker Charge**: `m_uiBeserkerChargeTimer` starts at 0. The logic `if (m_uiBeserkerChargeTimer == 0) pTarget = m_creature->GetVictim();` ensures the first charge targets the current victim, while subsequent charges target randomly.
*   **Immolate Spam Prevention**: The check `!m_creature->HasAura(AURA_IMMOLATE)` runs every tick. It relies on the spell’s internal cooldown or aura duration to prevent infinite recasting.
*   **Commented Code**: Logic for `SPELL_IMMOLATE` (20294) is commented out; the active code uses `AURA_IMMOLATE` (15506) directly.

## Member Reference

*   **`boss_thebeastAI`**: Constructor initializing `ScriptedAI` and calling `Reset()`.
*   **`Reset`**: Initializes timers; `m_uiBeserkerChargeTimer` is set to 0 for immediate first cast.
*   **`SpellHit`**: Triggers `SPELL_SUMMON_FINKLE` on the caster if hit by a skinning spell.
*   **`UpdateAI`**: Manages combat loop: maintains Immolate aura, casts Flamebreak, Terrifying Roar, Berserker Charge, Fireball, and Fireblast on timers, and handles melee attacks.
*   **`GetAI_boss_thebeast`**: Factory function creating `boss_thebeastAI` instances.
*   **`AddSC_boss_thebeast`**: Registers the script with `ScriptMgr`.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_the_beast

*Source:* boss_the_beast.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_thebeastAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | method | shared_Util/urand | — | — |
| SpellHit | method | SpellCaster/CastSpell#2 | — | — |
| UpdateAI | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, SpellCaster/CastSpell#2, Unit.Main/GetVictim, Unit.Main/HasAura#2, Unit.Main/SelectHostileTarget | — | — |
| GetAI_boss_thebeast | function | — | — | — |
| AddSC_boss_thebeast | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
