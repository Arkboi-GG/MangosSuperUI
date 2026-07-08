<!-- provenance: verbose -->
# boss_anubshiah

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`boss_anubshiah.cpp` implements the combat AI for **Anubshiah**, a boss in the **Blackrock Depths** dungeon. The unit defines `boss_anubshiahAI`, derived from `ScriptedAI`, which manages timed spell casts and melee attacks. It registers itself with the server via `AddSC_boss_anubshiah`.

## Member-by-Member Behavior

### Initialization and Lifecycle

**`boss_anubshiahAI` (Constructor)**
Initializes the AI for a `Creature`, calling the parent `ScriptedAI` constructor and immediately invoking `Reset()` to set initial timer values.

**`Reset`**
Sets initial cooldowns for all abilities:
- `ShadowBolt_Timer`: 7,000 ms
- `CurseOfTongues_Timer`: 24,000 ms
- `CurseOfWeakness_Timer`: 12,000 ms
- `DemonArmor_Timer`: 3,000 ms
- `EnvelopingWeb_Timer`: 16,000 ms

### Combat Logic

**`UpdateAI`**
Executed periodically with a time delta (`diff`). It returns early if no hostile target exists. Otherwise, it processes five timers:
1.  **Shadow Bolt**: Casts `SPELL_SHADOWBOLT` on the victim every 7,000 ms.
2.  **Curse of Tongues**: Selects a random target and casts `SPELL_CURSEOFTONGUES` every 18,000 ms.
3.  **Curse of Weakness**: Casts `SPELL_CURSEOFWEAKNESS` on the victim every 45,000 ms.
4.  **Demon Armor**: Casts `SPELL_DEMONARMOR` on itself every 300,000 ms.
5.  **Enveloping Web**: Selects a random target and casts `SPELL_ENVELOPINGWEB` every 12,000 ms.

Finally, it calls `DoMeleeAttackIfReady()`.

**Notable Timer Discrepancy**: The values used to reset timers in `UpdateAI` differ from those in `Reset()`. For example, `CurseOfTongues` initializes at 24s but resets to 18s after the first cast; `DemonArmor` initializes at 3s but resets to 300s. This causes the first rotation to behave differently from subsequent ones.

### Script Registration

**`GetAI_boss_anubshiah`**
Factory function returning a new `boss_anubshiahAI` instance for a `Creature`.

**`AddSC_boss_anubshiah`**
Creates a `Script` object named `"boss_anubshiah"`, assigns `GetAI_boss_anubshiah` as the AI getter, and registers it via `RegisterSelf()`. Called by `ScriptLoader::AddScripts`.

## Cross-Unit Boundaries

### Calls Out
-   **`ScriptedAI/ScriptedAI`**: Base class initialization.
-   **`Creature.Main/SelectAttackingTarget`**: Selects random targets for curses and webs.
-   **`CreatureAI/DoCastSpellIfCan`**: Attempts spell casts with safety checks.
-   **`CreatureAI/DoMeleeAttackIfReady`**: Handles melee attacks.
-   **`Unit.Main/GetVictim`**: Retrieves current target.
-   **`Unit.Main/SelectHostileTarget`**: Checks for active combat.
-   **`Script/Script`**: Creates script metadata.
-   **`ScriptMgr/RegisterSelf`**: Registers the script.

### Called By
-   **`ScriptLoader/AddScripts`**: Loads the script during startup.

## Data Model

This unit does not interact with any database tables. All configuration is hardcoded.

## Member Reference

**`boss_anubshiahAI`**: Constructor initializing the AI and calling `Reset()`.

**`Reset`**: Sets initial timer values for all five abilities.

**`UpdateAI`**: Main loop managing timers for Shadow Bolt, Curse of Tongues, Curse of Weakness, Demon Armor, and Enveloping Web, plus melee attacks. Note timer discrepancies between initial and reset values.

**`GetAI_boss_anubshiah`**: Factory function creating the AI instance.

**`AddSC_boss_anubshiah`**: Registers the script with the server.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_anubshiah

*Source:* boss_anubshiah.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_anubshiahAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | method | — | — | — |
| UpdateAI | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_boss_anubshiah | function | — | — | — |
| AddSC_boss_anubshiah | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
