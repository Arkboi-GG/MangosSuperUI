<!-- provenance: verbose -->
# boss_arcanist_doan

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_arcanist_doan

**Purpose & Responsibilities**

`boss_arcanist_doan` implements the combat AI for the boss **Arcanist Doan** in the Scarlet Monastery instance. The unit defines `boss_arcanist_doanAI`, a `ScriptedAI` subclass that manages Doan’s spell rotation, melee attacks, and a health-triggered defensive mechanic. Key behaviors include periodic casting of Polymorph, AoE Silence, and Arcane Explosion, and a phase transition at ≤50% health where Doan casts an Arcane Bubble to shield himself, followed immediately by a Fire AOE detonation. The unit also provides the standard script registration functions to integrate the AI into the server.

**Member-by-Member Behavior**

### Initialization and State Management

*   **`boss_arcanist_doanAI` (Constructor)**: Inherits from `ScriptedAI` and immediately calls `Reset()` to initialize timers and state flags.
*   **`Reset`**: Sets initial values for all internal state:
    *   `Polymorph_Timer`: 20,000 ms.
    *   `AoESilence_Timer`: 15,000 ms.
    *   `ArcaneExplosion_Timer`: 3,000 ms.
    *   `bCanDetonate`: `false`.
    *   `bShielded`: `false`.

### Combat Logic

*   **`Aggro`**: Triggered on combat entry. Broadcasts the aggro text (`SAY_AGGRO`, ID 6199) via `ScriptMgr::DoScriptText`.
*   **`UpdateAI`**: The core update loop, executed every tick. It performs the following steps in order:
    1.  **Target Check**: Returns early if no hostile target or victim exists.
    2.  **Detonation**: If `bShielded` and `bCanDetonate` are true, broadcasts the burn text (`SAY_BURN_IN_FIRE`, ID 6200), casts `SPELL_FIREAOE` (ID 9435) on self, and sets `bCanDetonate` to `false`.
    3.  **Shield Lockout**: If Doan has the `SPELL_ARCANEBUBBLE` aura (ID 9438), the function returns immediately, suppressing all other abilities until the aura expires or is removed.
    4.  **Phase Transition**: If `bShielded` is false and health is ≤50%, Doan attempts to cast `SPELL_ARCANEBUBBLE` (ID 9438). It first checks `IsNonMeleeSpellCasted` to avoid interrupting an ongoing cast. On success, `bShielded` and `bCanDetonate` are set to `true`.
    5.  **Polymorph**: If `Polymorph_Timer` expires, selects a random hostile target and casts `SPELL_POLYMORPH` (ID 13323). Resets timer to 20,000 ms.
    6.  **AoE Silence**: If `AoESilence_Timer` expires, casts `SPELL_AOESILENCE` (ID 8988) on the current victim. Resets timer to a random value between 15,000 and 20,000 ms using `urand`.
    7.  **Arcane Explosion**: If `ArcaneExplosion_Timer` expires, casts `SPELL_ARCANEEXPLOSION` (ID 9433) on the current victim. Resets timer to 8,000 ms.
    8.  **Melee**: Calls `DoMeleeAttackIfReady()` to handle physical attacks.

### Registration

*   **`GetAI_boss_arcanist_doan`**: Factory function returning a new `boss_arcanist_doanAI` instance.
*   **`AddSC_boss_arcanist_doan`**: Registers the script with `ScriptMgr` by creating a `Script` object, assigning the name `"boss_arcanist_doan"` and the factory function, and calling `RegisterSelf`.

**Cross-Unit Boundaries**

*   **`ScriptedAI`**: Base class providing the AI framework.
*   **`ScriptMgr::DoScriptText`**: Called by `Aggro` and `UpdateAI` to output speech/sound events.
*   **`Creature::SelectAttackingTarget` / `Unit::SelectHostileTarget`**: Used to identify valid targets for spells and melee.
*   **`CreatureAI::DoCastSpellIfCan`**: Executes spell casts after checking validity (cooldowns, range, etc.).
*   **`CreatureAI::DoMeleeAttackIfReady`**: Manages melee attack timing.
*   **`shared_Util::urand`**: Randomizes the `AoESilence` cooldown.
*   **`SpellCaster::IsNonMeleeSpellCasted`**: Prevents spell interruption during the bubble cast.
*   **`Unit::GetHealthPercent` / `Unit::GetVictim` / `Unit::HasAura`**: Queries used for health thresholds, target validation, and aura checks.
*   **`Script::RegisterSelf` / `ScriptMgr::RegisterSelf`**: Integrates the script into the engine.
*   **`ScriptLoader::AddScripts`**: Invokes `AddSC_boss_arcanist_doan` at startup.

**Data Model**

This unit does not interact with any database tables. All spell IDs, text IDs, and timer values are hardcoded.

**Notable Implementation Details**

1.  **Immediate Detonation**: The Fire AOE detonation is not timed. It triggers on the very next `UpdateAI` tick after the Arcane Bubble is cast, provided the aura is present. The `bCanDetonate` flag ensures it fires only once per bubble cast.
2.  **Aura Lockout**: While `SPELL_ARCANEBUBBLE` is active, `UpdateAI` returns early, completely suppressing Polymorph, Silence, Arcane Explosion, and melee attacks. This creates a brief window where Doan is invulnerable and inactive.
3.  **Single Bubble**: The `bShielded` flag prevents the bubble mechanic from triggering more than once per encounter, as the health check is skipped when `bShielded` is true.
4.  **Cast Safety**: The bubble cast checks `IsNonMeleeSpellCasted` to avoid interrupting other spells, ensuring smooth phase transitions.

## Member Reference

**boss_arcanist_doanAI**  
Constructor for the AI class. Initializes the parent `ScriptedAI` and calls `Reset()` to set initial timers and state flags.

**Reset**  
Resets all internal timers (`Polymorph_Timer`, `AoESilence_Timer`, `ArcaneExplosion_Timer`) and boolean flags (`bCanDetonate`, `bShielded`) to their default values.

**Aggro**  
Called when the boss enters combat. Plays the aggro text/sound (`SAY_AGGRO`) using `ScriptMgr::DoScriptText`.

**UpdateAI**  
The main AI loop. Validates targets, checks for shield/detonation state, enforces a lockout while the Arcane Bubble aura is active, triggers the bubble at ≤50% health, and manages timed casts for Polymorph, AoE Silence, and Arcane Explosion. Ends with a melee attack check.

**GetAI_boss_arcanist_doan**  
Factory function that creates and returns a new `boss_arcanist_doanAI` instance for a given `Creature`.

**AddSC_boss_arcanist_doan**  
Registers the script with the server's `ScriptMgr`. Creates a `Script` object, sets its name and AI factory function, and registers it.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_arcanist_doan

*Source:* boss_arcanist_doan.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_arcanist_doanAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | method | — | — | — |
| Aggro | method | ScriptMgr/DoScriptText | — | — |
| UpdateAI | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, ScriptMgr/DoScriptText, shared_Util/urand, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/GetHealthPercent, Unit.Main/GetVictim, Unit.Main/HasAura#2, Unit.Main/SelectHostileTarget | — | — |
| GetAI_boss_arcanist_doan | function | — | — | — |
| AddSC_boss_arcanist_doan | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
