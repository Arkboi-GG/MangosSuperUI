# boss_the_ravenian

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`boss_the_ravenian.cpp` implements the combat AI for **The Ravenian**, a boss in the **Scholomance** dungeon. The unit defines `boss_theravenianAI`, a `ScriptedAI` subclass that manages four periodic spells (**Trample**, **Cleave**, **Sundering Cleave**, **Knock Away**) and standard melee attacks. It also provides the factory and registration functions required to load this AI into the server’s script manager.

## Member-by-Member Behavior

### Initialization and State

*   **`boss_theravenianAI`**: Constructs the AI, calling the base `ScriptedAI` constructor and immediately invoking `Reset()` to initialize timers.
*   **`Reset`**: Sets initial cooldowns for all four spells and initializes the unused `HasYelled` flag to `false`. Initial timers are significantly longer than subsequent intervals (see *Notable Implementation Details*).

### Combat Loop

*   **`UpdateAI`**: Executed periodically. It first verifies a valid hostile target exists via `SelectHostileTarget()` and `GetVictim()` (from `Unit.Main`). If no target, it returns early. Otherwise, it checks four internal timers:
    *   If a timer expires (`< diff`), it casts the corresponding spell on the victim using `DoCastSpellIfCan()` (from `CreatureAI`) and resets the timer to its shorter, recurring interval.
    *   If not expired, it decrements the timer by `diff`.
    *   Finally, it calls `DoMeleeAttackIfReady()` (from `CreatureAI`) to handle physical attacks.

### Lifecycle and Registration

*   **`JustDied`**: Called upon death. It retrieves the instance data via `GetInstanceData()` (from `WorldObject.Object`) and calls `SetData(TYPE_RAVENIAN, DONE)` on the `ScriptedInstance` (via `InstanceData`) to mark the encounter complete.
*   **`GetAI_boss_theravenian`**: Factory function returning a new `boss_theravenianAI` instance for a given `Creature`.
*   **`AddSC_boss_theravenian`**: Creates a `Script` object, assigns `GetAI_boss_theravenian` as the AI provider, and registers it with the `ScriptMgr` via `RegisterSelf()`. This function is called by `ScriptLoader::AddScripts` during server startup.

## Cross-Unit Boundaries

*   **`ScriptedAI`**: Base class providing `DoCastSpellIfCan`, `DoMeleeAttackIfReady`, and common AI infrastructure.
*   **`Unit.Main`**: `UpdateAI` uses `SelectHostileTarget` and `GetVictim` to validate targets.
*   **`WorldObject.Object`**: `JustDied` uses `GetInstanceData` to access the dungeon instance context.
*   **`InstanceData`**: `JustDied` calls `SetData` to update dungeon progress.
*   **`Script` / `ScriptMgr`**: `AddSC_boss_theravenian` constructs a `Script` and registers it via `ScriptMgr::RegisterSelf`.
*   **`ScriptLoader`**: Calls `AddSC_boss_theravenian` to load the script.

## Data Model

This unit does not interact with any database tables. All spell IDs and timer values are hardcoded. Dungeon state is managed in-memory via the `InstanceData` interface.

## Notable Implementation Details

1.  **Staggered Initial Timers**: `Reset()` sets long initial delays (24s, 15s, 40s, 32s), but `UpdateAI()` resets them to much shorter intervals (10s, 7s, 20s, 12s) after the first cast. This creates a delayed start for abilities, likely to stagger initial damage output.
2.  **Unused `HasYelled`**: The `HasYelled` member is declared and reset but never read or written in this unit, indicating dead code or incomplete implementation.
3.  **Manual Timer Management**: The AI manually tracks spell cooldowns using `uint32` timers rather than relying on engine-side spell cooldowns, ensuring strict adherence to the scripted rotation regardless of spell failure conditions (though `DoCastSpellIfCan` still checks LOS/range).

## Member Reference

*   **`boss_theravenianAI`**: Constructor initializing the AI and calling `Reset()`.
*   **`Reset`**: Resets spell timers to initial (longer) values and clears `HasYelled`.
*   **`JustDied`**: Marks the boss as defeated in the instance data.
*   **`UpdateAI`**: Main loop processing spell timers, casting spells, and handling melee attacks.
*   **`GetAI_boss_theravenian`**: Factory function creating the AI instance.
*   **`AddSC_boss_theravenian`**: Registers the script with the server's script manager.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_the_ravenian

*Source:* boss_the_ravenian.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_theravenianAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | method | — | — | — |
| JustDied | method | InstanceData/SetData, WorldObject.Object/GetInstanceData | — | — |
| UpdateAI | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_boss_theravenian | function | — | — | — |
| AddSC_boss_theravenian | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
