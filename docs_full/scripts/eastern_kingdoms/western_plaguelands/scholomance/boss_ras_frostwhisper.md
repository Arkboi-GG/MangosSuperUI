<!-- provenance: verbose -->
# boss_ras_frostwhisper

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_ras_frostwhisper

## Purpose & Responsibilities

`boss_ras_frostwhisper` implements the combat AI for **Ras Frostwhisper**, a boss in the Scholomance dungeon. The unit defines `boss_rasfrostAI`, a subclass of `ScriptedAI`, managing timed spell casts and melee attacks. It provides `GetAI_boss_rasfrost` to instantiate the AI and `AddSC_boss_rasfrost` to register the script with the server's `ScriptMgr`.

## Member-by-Member Behavior

### Initialization

**`boss_rasfrostAI`**  
Constructs the AI instance for a `Creature`. It initializes the parent `ScriptedAI` and immediately calls `Reset()` to apply the initial `SPELL_ICEARMOR` buff and prime all combat timers.

**`Reset`**  
Sets initial cooldowns for all abilities:
- `IceArmor_Timer`: 2,000 ms (initial cast), resetting to 180,000 ms on subsequent ticks.
- `Frostbolt_Timer`: 8,000 ms.
- `ChillNova_Timer`: 12,000 ms.
- `Freeze_Timer`: 18,000 ms.
- `FrostVolley_Timer`: 24,000 ms.
- `Fear_Timer`: 45,000 ms.

It forces a cast of `SPELL_ICEARMOR` on the creature itself using `SpellCaster::CastSpell`.

### Combat Loop

**`UpdateAI`**  
Executes every update tick with a time delta `diff`. It first verifies the creature has a hostile target and a victim; if not, it returns early. It then processes six independent timers:
1.  **Ice Armor**: Recasts `SPELL_ICEARMOR` on self every 180 seconds.
2.  **Frostbolt**: Selects a random target via `Creature::SelectAttackingTarget` and casts `SPELL_FROSTBOLT` every 8 seconds.
3.  **Freeze**: Casts `SPELL_FREEZE` on the current victim every 24 seconds.
4.  **Fear**: Casts `SPELL_FEAR` on the current victim every 30 seconds.
5.  **Chill Nova**: Casts `SPELL_CHILLNOVA` on the current victim every 14 seconds.
6.  **Frost Volley**: Casts `SPELL_FROSTVOLLEY` on the current victim every 15 seconds.

After processing timers, it calls `CreatureAI::DoMeleeAttackIfReady` to handle physical attacks.

### Registration

**`GetAI_boss_rasfrost`**  
Factory function that allocates and returns a new `boss_rasfrostAI` instance for a given `Creature`.

**`AddSC_boss_rasfrost`**  
Registers the script with the global `ScriptMgr`. It creates a `Script` object named `"boss_boss_ras_frostwhisper"`, assigns `GetAI_boss_rasfrost` as the AI getter, and calls `Script::RegisterSelf`. This function is invoked by `ScriptLoader::AddScripts` during server startup.

## Cross-Unit Boundaries

-   **`ScriptedAI`**: Inherited base class providing the AI framework.
-   **`SpellCaster`**: `Reset` calls `CastSpell` to apply the initial buff.
-   **`Creature` / `Unit`**: `UpdateAI` uses `SelectHostileTarget`, `GetVictim`, and `SelectAttackingTarget` for target validation and selection.
-   **`CreatureAI`**: `UpdateAI` uses `DoCastSpellIfCan` for spell execution and `DoMeleeAttackIfReady` for melee attacks.
-   **`ScriptMgr`**: `AddSC_boss_rasfrost` registers the script via `Script::RegisterSelf`.

## Data Model

This unit does not access any database tables. All spell IDs and timers are hardcoded.

## Notable Implementation Details

-   **Targeting Strategy**: `Frostbolt` targets randomly among attackers, while `Freeze`, `Fear`, `ChillNova`, and `FrostVolley` always target the primary victim (typically the tank).
-   **Timer Reset Logic**: Timers are decremented by `diff` and reset to their cooldown values only when triggered. The `IceArmor_Timer` has a distinct initial delay (2s) versus its renewal interval (180s).
-   **No Health Phases**: The AI behavior is static regardless of the creature's health percentage.

## Member Reference

**boss_rasfrostAI**  
Constructor that initializes the parent `ScriptedAI` and calls `Reset()` to set timers and apply the initial Ice Armor buff.

**Reset**  
Sets initial cooldowns for all abilities and forces a cast of `SPELL_ICEARMOR` on the creature.

**UpdateAI**  
Main update loop that checks for valid targets, manages six spell timers (Ice Armor, Frostbolt, Freeze, Fear, Chill Nova, Frost Volley), casts spells upon expiration, and handles melee attacks.

**GetAI_boss_rasfrost**  
Factory function that returns a new `boss_rasfrostAI` instance for a given `Creature`.

**AddSC_boss_rasfrost**  
Registration function that creates a `Script` object named `"boss_boss_ras_frostwhisper"`, links the `GetAI` function, and registers it with `ScriptMgr`.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_ras_frostwhisper

*Source:* boss_ras_frostwhisper.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_rasfrostAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | method | SpellCaster/CastSpell#2 | — | — |
| UpdateAI | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_boss_rasfrost | function | — | — | — |
| AddSC_boss_rasfrost | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
