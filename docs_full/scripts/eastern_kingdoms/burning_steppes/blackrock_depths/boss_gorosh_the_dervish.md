<!-- provenance: verbose -->
# boss_gorosh_the_dervish

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_gorosh_the_dervish

**Purpose & Responsibilities**  
Implements the combat AI for **Gorosh the Dervish** (Blackrock Depths). It manages three timed abilities—Whirlwind, Mortal Strike, and Bloodlust—and standard melee attacks using hardcoded timers and spell IDs. No database interaction occurs.

## Member-by-Member Behavior

### Initialization & Lifecycle

**`boss_gorosh_the_dervishAI` (constructor)**  
Inherits from `ScriptedAI` and immediately calls `Reset()` to initialize timer states.

**`Reset`**  
Sets initial timer values:
- `WhirlWind_Timer`: 12,000 ms (first cast delay).
- `MortalStrike_Timer`: 22,000 ms (first cast delay).
- `Bloodlust_Timer`: 0 ms (disabled until health threshold is met).

### Combat Logic

**`UpdateAI`**  
Executes the main AI loop:
1. **Target Check**: Returns early if no hostile target or victim exists (`SelectHostileTarget`/`GetVictim`).
2. **Whirlwind**: Casts `SPELL_WHIRLWIND` (15589) on self if `WhirlWind_Timer` expires. Resets timer to 15,000 ms.
3. **Mortal Strike**: Casts `SPELL_MORTALSTRIKE` (15708) on the current victim if `MortalStrike_Timer` expires. Resets timer to 15,000 ms.
4. **Bloodlust**: If health is below 51% (`GetHealthPercent`), checks `Bloodlust_Timer`. If expired, casts `SPELL_BLOODLUST` (21049) on self and resets timer to 45,000 ms. *Note: The timer continues ticking even if health recovers above 51%, allowing repeated casts.*
5. **Melee**: Calls `DoMeleeAttackIfReady()` for standard attacks.

### Registration & Factory

**`GetAI_boss_gorosh_the_dervish`**  
Factory function returning a new `boss_gorosh_the_dervishAI` instance for a given `Creature`.

**`AddSC_boss_gorosh_the_dervish`**  
Registers the script with `ScriptMgr`. Creates a `Script` object named `"boss_gorosh_the_dervish"`, assigns the `GetAI` factory, and calls `RegisterSelf()`. Called by `ScriptLoader::AddScripts`.

## Cross-Unit Boundaries

- **Calls `ScriptedAI`**: Inherits base AI functionality.
- **Calls `CreatureAI`**: Uses `DoCastSpellIfCan` and `DoMeleeAttackIfReady` for safe spell/melee execution.
- **Calls `Unit.Main`**: Uses `GetHealthPercent`, `GetVictim`, and `SelectHostileTarget` for state queries.
- **Called by `ScriptLoader`**: `AddSC_boss_gorosh_the_dervish` is invoked during server startup.

## Data Model

No database tables are accessed.

## Notable Implementation Details

1. **Bloodlust Persistence**: Once `Bloodlust_Timer` starts (health < 51%), it ticks independently of health. Healing the boss above 51% does not stop future Bloodlust casts.
2. **Staggered Starts**: Initial timers (12s/22s) differ from recurring intervals (15s/15s) to prevent simultaneous ability casts at combat start.
3. **Hardcoded Spells**: Spell IDs are defined as macros; changes require recompilation.

## Member Reference

**`boss_gorosh_the_dervishAI`**  
Constructor initializing timers via `Reset()`.

**`Reset`**  
Sets `WhirlWind_Timer` to 12,000 ms, `MortalStrike_Timer` to 22,000 ms, and `Bloodlust_Timer` to 0 ms.

**`UpdateAI`**  
Manages combat loop: validates targets, handles Whirlwind/Mortal Strike/Bloodlust timers, and executes melee attacks.

**`GetAI_boss_gorosh_the_dervish`**  
Factory function creating the AI instance.

**`AddSC_boss_gorosh_the_dervish`**  
Registers the script with the engine.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_gorosh_the_dervish

*Source:* boss_gorosh_the_dervish.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_gorosh_the_dervishAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | method | — | — | — |
| UpdateAI | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Unit.Main/GetHealthPercent, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_boss_gorosh_the_dervish | function | — | — | — |
| AddSC_boss_gorosh_the_dervish | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
