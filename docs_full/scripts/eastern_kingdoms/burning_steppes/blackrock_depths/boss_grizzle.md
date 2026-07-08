# boss_grizzle

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`boss_grizzle.cpp` implements the artificial intelligence (AI) for the boss creature **Grizzle** in the *Blackrock Depths* dungeon instance. The unit defines a custom AI class (`boss_grizzleAI`) that inherits from `ScriptedAI`, providing specific combat behaviors: periodic casting of **Ground Tremor**, entering a **Frenzy** state when health drops below 50%, and standard melee engagement. It also provides the factory function to instantiate this AI and the registration routine to integrate it into the server's script system.

## Member-by-Member Behavior

### AI Lifecycle and State Management

*   **`boss_grizzleAI` (Constructor)**: Initializes the AI instance by calling `Reset()` to establish initial timer states. It inherits initialization from `ScriptedAI`.
*   **`Reset`**: Resets the internal timers for the boss's abilities.
    *   `GroundTremor_Timer` is set to 12,000 ms (12 seconds), meaning the first cast occurs after a 12-second delay.
    *   `Frenzy_Timer` is set to 0, ensuring the frenzy check starts immediately upon engagement.

### Combat Logic

*   **`UpdateAI`**: The core game loop method, called periodically with a time difference (`diff`). It manages three primary behaviors:
    1.  **Target Validation**: Immediately returns if the creature has no hostile target or victim, preventing action execution during idle states.
    2.  **Ground Tremor**: Checks `GroundTremor_Timer`. If the timer expires (is less than `diff`), it attempts to cast `SPELL_GROUNDTREMOR` (ID 6524) using `DoCastSpellIfCan`. Upon successful attempt, the timer resets to 8,000 ms (8 seconds). If not expired, it decrements the timer by `diff`.
    3.  **Frenzy Mechanic**: Checks if the creature's health percentage is below 51%. If true, it checks `Frenzy_Timer`. If the timer expires, it attempts to cast `SPELL_FRENZY` (ID 8269). If the cast succeeds (`CAST_OK`), it triggers an emote via `DoScriptText` (ID 7797, `EMOTE_GENERIC_FRENZY_KILL`) and resets the timer to 15,000 ms (15 seconds). If the health is above 51%, the frenzy logic is skipped entirely, and the timer is not decremented (effectively pausing the frenzy cycle until the health threshold is met again).
    4.  **Melee Attack**: Calls `DoMeleeAttackIfReady` to handle standard physical attacks.

### Integration Functions

*   **`GetAI_boss_grizzle`**: A factory function that creates and returns a new instance of `boss_grizzleAI` for a given `Creature` pointer. This is the entry point used by the script manager to attach this AI to the creature entity.
*   **`AddSC_boss_grizzle`**: Registers the script with the server. It creates a `Script` object, assigns the name `"boss_grizzle"`, links the `GetAI` function to `GetAI_boss_grizzle`, and calls `RegisterSelf` to add it to the global script registry. This function is called by `ScriptLoader::AddScripts` during server startup.

## Cross-Unit Boundaries

*   **`ScriptedAI` / `CreatureAI`**: `boss_grizzleAI` inherits from `ScriptedAI`. It uses `DoCastSpellIfCan` and `DoMeleeAttackIfReady` from the base AI classes to handle spell casting and melee mechanics. These methods abstract the complex logic of checking line-of-sight, range, and cooldowns.
*   **`ScriptMgr`**: `UpdateAI` calls `DoScriptText` to broadcast the frenzy emote. `AddSC_boss_grizzle` calls `RegisterSelf` to register the script.
*   **`Unit.Main`**: `UpdateAI` uses `GetHealthPercent`, `GetVictim`, and `SelectHostileTarget` to determine the creature's state and targets. These are fundamental unit methods for accessing health data and threat lists.
*   **`Script` / `ScriptLoader`**: `AddSC_boss_grizzle` constructs a `Script` object and is itself called by `ScriptLoader::AddScripts` to ensure the AI is loaded into the engine.

## Data Model

This unit does not interact directly with any database tables. All configuration (spell IDs, emote IDs, timer values) is hardcoded in the source file.

## Notable Implementation Details

*   **Frenzy Timer Pause**: The `Frenzy_Timer` is only decremented when the creature's health is below 51%. If the creature is healed above 51% during combat, the timer stops counting down. This means the frenzy ability will not trigger again until the health drops below 51% *and* the accumulated time exceeds the 15-second interval. This is a subtle behavior: the timer does not reset on healing, it merely pauses.
*   **Initial Delay**: The first `GroundTremor` cast is delayed by 12 seconds, while subsequent casts occur every 8 seconds. This provides players with a slightly longer initial window before the first area-of-effect damage.
*   **Emote Trigger**: The frenzy emote (`EMOTE_GENERIC_FRENZY_KILL`) is only triggered if the `SPELL_FRENZY` cast is successful (`CAST_OK`). If the cast fails (e.g., due to silence or other interrupts), the emote is not played, and the timer is not reset, allowing the next tick to retry the cast.
*   **Hardcoded Values**: Spell IDs (6524, 8269) and emote ID (7797) are defined as macros at the top of the file. Timer values are hardcoded integers in milliseconds.

## Member Reference

*   **`boss_grizzleAI`**: Constructor for the AI class. Initializes the parent `ScriptedAI` and calls `Reset()` to set initial timer values.
*   **`Reset`**: Resets `GroundTremor_Timer` to 12,000 ms and `Frenzy_Timer` to 0.
*   **`UpdateAI`**: Main AI update loop. Handles target validation, casts `GroundTremor` on a timer, casts `Frenzy` if health < 51% and timer expires, and performs melee attacks.
*   **`GetAI_boss_grizzle`**: Factory function that instantiates and returns a `boss_grizzleAI` object for a given `Creature`.
*   **`AddSC_boss_grizzle`**: Registration function that creates a `Script` object, sets its name and AI getter, and registers it with the `ScriptMgr`.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_grizzle

*Source:* boss_grizzle.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_grizzleAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | method | — | — | — |
| UpdateAI | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, ScriptMgr/DoScriptText, Unit.Main/GetHealthPercent, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_boss_grizzle | function | — | — | — |
| AddSC_boss_grizzle | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
