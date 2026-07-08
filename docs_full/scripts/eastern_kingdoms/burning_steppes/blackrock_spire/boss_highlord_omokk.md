<!-- provenance: verbose, failed-members -->
# boss_highlord_omokk

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`boss_highlord_omokk.cpp` implements the combat AI for **Highlord Omokk**, a boss in the *Blackrock Spire* dungeon. The unit defines `boss_highlordomokkAI`, a subclass of `ScriptedAI`, which manages a timer-based rotation of six offensive spells alongside standard melee attacks. It provides no complex phase logic or movement paths; its role is strictly to execute a predictable sequence of abilities against the creature's current target. The unit also exports the registration function `AddSC_boss_highlord_omokk` to integrate this AI into the server's script manager.

## Member-by-Member Behavior

### Initialization and Lifecycle

**`boss_highlordomokkAI`**  
Constructs the AI object for a `Creature`. It delegates to the `ScriptedAI` base constructor and immediately calls `Reset()` to initialize spell timers, ensuring no ability fires instantly upon engagement.

**`Reset`**  
Configures the initial cooldowns for all six spells. Notably, `m_uiSunderArmorTimer` starts at 2,000 ms (2 seconds) to prioritize early armor reduction, while other timers range from 10,000 ms to 24,000 ms.

### Combat Logic

**`UpdateAI`**  
The main update loop, called periodically with the time delta `uiDiff`. It performs three steps:
1.  **Target Check**: Returns immediately if `SelectHostileTarget()` or `GetVictim()` indicates no valid target.
2.  **Spell Rotation**: Iterates through six timers. If a timer expires (`timer < uiDiff`), it casts the associated spell via `DoCastSpellIfCan` and resets the timer to its post-cast cooldown. Otherwise, it decrements the timer by `uiDiff`.
    *   **War Stomp** (`SPELL_WARSTOMP`): Cast on self (AoE). Initial: 15s, Reset: 14s.
    *   **Strike** (`SPELL_STRIKE`): Cast on victim. Initial: 10s, Reset: 10s.
    *   **Rend** (`SPELL_REND`): Cast on victim. Initial: 14s, Reset: 18s.
    *   **Sunder Armor** (`SPELL_SUNDERARMOR`): Cast on victim. Initial: 2s, Reset: 25s.
    *   **Knock Away** (`SPELL_KNOCKAWAY`): Cast on self (AoE push). Initial: 18s, Reset: 12s.
    *   **Slow** (`SPELL_SLOW`): Cast on self (AoE slow). Initial: 24s, Reset: 18s.
3.  **Melee**: Calls `DoMeleeAttackIfReady()` to handle physical attacks.

### Registration

**`GetAI_boss_highlordomokk`**  
Factory function that allocates and returns a new `boss_highlordomokkAI` instance for a given `Creature`.

**`AddSC_boss_highlord_omokk`**  
Registers the script with the `ScriptMgr`. It creates a `Script` object, sets the name to `"boss_highlord_omokk"`, assigns `GetAI_boss_highlordomokk` as the AI provider, and calls `RegisterSelf()`. This function is invoked by `ScriptLoader::AddScripts` during server startup.

## Cross-Unit Boundaries

*   **`ScriptedAI`**: Base class for `boss_highlordomokkAI`. Provides foundational AI infrastructure.
*   **`CreatureAI`**: `UpdateAI` calls `DoCastSpellIfCan` and `DoMeleeAttackIfReady` from this unit to handle spell casting mechanics and melee attacks.
*   **`Unit.Main`**: `UpdateAI` uses `GetVictim` and `SelectHostileTarget` from this unit to validate combat targets.
*   **`Script` / `ScriptMgr`**: `AddSC_boss_highlord_omokk` constructs a `Script` object and calls `RegisterSelf()` to register with `ScriptMgr`.
*   **`ScriptLoader`**: Calls `AddSC_boss_highlord_omokk` to load the script.

## Data Model

This unit does not interact with any database tables. All spell IDs and cooldown values are hardcoded.

## Notable Implementation Details

*   **Variable Cooldowns**: Several spells have different initial and subsequent cooldowns (e.g., Sunder Armor: 2s → 25s). This tunes the opening phase differently from sustained combat.
*   **Self-Cast AoEs**: War Stomp, Knock Away, and Slow are cast on `m_creature`. The AI assumes the base spell logic handles area-of-effect targeting and validity checks.
*   **No Custom Targeting**: The AI relies entirely on the base `CreatureAI` target selection. It does not implement custom logic for target switching.

## Member Reference

**boss_highlordomokkAI**  
Constructor for the AI class. Initializes the `ScriptedAI` base class and immediately calls `Reset()` to set initial spell timers.

**Reset**  
Sets the initial cooldown values for all six spells: `m_uiWarStompTimer` (15000), `m_uiStrikeTimer` (10000), `m_uiRendTimer` (14000), `m_uiSunderArmorTimer` (2000), `m_uiKnockAwayTimer` (18000), `m_uiSlowTimer` (24000).

**UpdateAI**  
Main AI loop. Validates target existence. Checks and updates six spell timers. Casts spells via `DoCastSpellIfCan` if timers expire. Resets timers to new values after casting. Decrements timers otherwise. Calls `DoMeleeAttackIfReady` for physical attacks.

**GetAI_boss_highlordomokk**  
Factory function that returns a new `boss_highlordomokkAI` instance for a given `Creature`. Used by the script registration system.

**AddSC_boss_highlord_omokk**  
Registers the script with the `ScriptMgr`. Creates a `Script` object, sets the name to `"boss_highlord_omokk"`, assigns `GetAI_boss_highlordomokk` as the AI getter, and calls `RegisterSelf()`. Called by `ScriptLoader::AddScripts`.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_highlord_omokk

*Source:* boss_highlord_omokk.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_highlordomokkAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | method | — | — | — |
| UpdateAI | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_boss_highlordomokk | function | — | — | — |
| AddSC_boss_highlordomokk | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |

---

<!-- verify: failed-members | missing: AddSC_boss_highlordomokk | invented: AddSC_boss_highlord_omokk -->
