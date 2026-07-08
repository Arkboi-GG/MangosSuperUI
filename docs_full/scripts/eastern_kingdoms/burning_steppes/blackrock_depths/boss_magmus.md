# boss_magmus

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`boss_magmus.cpp` implements the artificial intelligence (AI) and script registration for **Magmus**, a boss creature located in the **Blackrock Depths** dungeon instance. The unit defines `boss_magmusAI`, a subclass of `ScriptedAI`, which governs Magmus's combat behavior, including spell casting, melee attacks, and health-based ability triggers.

The primary responsibilities of this unit are:
1.  **Combat Logic:** Managing timed abilities (`Fiery Burst`) and conditional abilities (`War Stomp`, triggered below 51% health).
2.  **Instance State Management:** Communicating with the dungeon's instance data system (`ScriptedInstance`) to track the progress of the "Iron Hall" encounter phase. It reports states such as `IN_PROGRESS`, `DONE`, and `FAIL`.
3.  **Script Registration:** Providing the factory function and registration hook to integrate this AI into the server's script manager.

## Member-by-Member Behavior

### Initialization and Lifecycle

*   **`boss_magmusAI` (Constructor):** Initializes the AI object. It retrieves the current instance data via `WorldObject::GetInstanceData()` and casts it to `ScriptedInstance*`. It initializes the `Engaged` flag to `false` and immediately calls `Reset()` to set initial timer values.
*   **`Reset`:** Resets the AI state for a new engagement. It sets `m_uiFieryBurst_Timer` to 5000 ms and `m_uiWarStomp_Timer` to 0. Crucially, if the boss was previously engaged (`Engaged == true`), it signals a failure to the instance data by calling `InstanceData::SetData` with `TYPE_IRON_HALL` and `FAIL`. It then clears the `Engaged` flag.
*   **`Aggro`:** Triggered when the creature enters combat. It sets `Engaged` to `true` and notifies the instance data that the `TYPE_IRON_HALL` event is `IN_PROGRESS`.
*   **`JustDied`:** Triggered upon the creature's death. It notifies the instance data that the `TYPE_IRON_HALL` event is `DONE`.

### Combat Loop

*   **`UpdateAI`:** The core tick function called periodically.
    1.  **Target Validation:** Returns early if the creature has no hostile target or victim.
    2.  **Fiery Burst:** Checks `m_uiFieryBurst_Timer`. If the timer expires, it casts `SPELL_FIERYBURST` (ID 13900) on the current victim and resets the timer to 6000 ms. Otherwise, it decrements the timer by the time difference (`uiDiff`).
    3.  **War Stomp:** Checks if the creature's health is below 51%. If so, it checks `m_uiWarStomp_Timer`. If expired, it casts `SPELL_WARSTOMP` (ID 24375) on the victim and resets the timer to 8000 ms. Otherwise, it decrements the timer. Note that `War Stomp` is only considered when health is low; otherwise, its timer is not updated or checked.
    4.  **Melee:** Calls `CreatureAI::DoMeleeAttackIfReady()` to handle standard physical attacks.

### Script Integration

*   **`GetAI_boss_magmus`:** A factory function that allocates and returns a new `boss_magmusAI` instance for a given `Creature`.
*   **`AddSC_boss_magmus`:** Registers the script with the server. It creates a `Script` object, assigns the name `"boss_magmus"` and the AI getter function, and calls `Script::RegisterSelf()` to add it to the global script registry. This function is called by `ScriptLoader::AddScripts` during server startup.

## Cross-Unit Boundaries

*   **`ScriptedAI` / `CreatureAI`:** `boss_magmusAI` inherits from `ScriptedAI`. It relies on base classes for fundamental AI mechanics like `DoCastSpellIfCan`, `DoMeleeAttackIfReady`, and target selection helpers.
*   **`WorldObject` / `InstanceData`:** The constructor accesses `WorldObject::GetInstanceData()` to obtain the dungeon instance context. Throughout the lifecycle (`Reset`, `Aggro`, `JustDied`), the AI calls `InstanceData::SetData` to update the state of the `TYPE_IRON_HALL` event. This allows other scripts or game objects in the dungeon to react to Magmus's status.
*   **`Unit.Main`:** `UpdateAI` uses `Unit::GetHealthPercent`, `Unit::GetVictim`, and `Unit::SelectHostileTarget` to determine combat conditions and targets.
*   **`ScriptMgr` / `ScriptLoader`:** `AddSC_boss_magmus` interacts with the script management system. `Script::RegisterSelf` adds the script to the internal map, and `ScriptLoader::AddScripts` invokes this registration function during initialization.

## Data Model

This unit does not interact directly with any database tables. All state is managed in-memory via the `ScriptedInstance` interface and local member variables.

## Notable Implementation Details

*   **Timer Initialization vs. Reset:** In the constructor, `Reset()` is called, setting `m_uiFieryBurst_Timer` to 5000 ms. However, in the `Reset()` method itself, the timer is also set to 5000 ms. This ensures consistent behavior whether the creature despawns and respawns or is manually reset.
*   **War Stomp Condition:** `War Stomp` is strictly gated by health percentage (`< 51.0f`). The timer for `War Stomp` is initialized to 0 in `Reset()`, meaning it is ready to cast immediately if the health condition is met upon aggro (though unlikely at 100% health). The timer is only decremented when the health condition is active; if health rises above 51%, the timer stops decrementing, preserving the remaining time until the next potential cast window.
*   **Failure State Reporting:** The `Reset()` method checks `Engaged`. If the boss resets while engaged (e.g., due to a timeout or manual reset command), it reports `FAIL` to the instance. This distinguishes between a natural death (`DONE` via `JustDied`) and an interrupted encounter.
*   **Missing Pre-Event:** The script header comment notes: *"Missing pre-event to open doors"*. This implies that external mechanisms (likely other scripts or game events) are responsible for opening doors leading to Magmus, and this AI does not handle that logic.

## Member Reference

*   **`boss_magmusAI`**: Constructor for the AI class. Retrieves instance data, initializes `Engaged` to false, and calls `Reset()`.
*   **`Reset`**: Resets timers (`FieryBurst` to 5000ms, `WarStomp` to 0). If `Engaged` is true, sets instance data `TYPE_IRON_HALL` to `FAIL`. Sets `Engaged` to false.
*   **`Aggro`**: Sets `Engaged` to true. Sets instance data `TYPE_IRON_HALL` to `IN_PROGRESS`.
*   **`JustDied`**: Sets instance data `TYPE_IRON_HALL` to `DONE`.
*   **`UpdateAI`**: Main combat loop. Validates target. Casts `SPELL_FIERYBURST` on 6s timer. If health < 51%, casts `SPELL_WARSTOMP` on 8s timer. Performs melee attacks.
*   **`GetAI_boss_magmus`**: Factory function returning a new `boss_magmusAI` instance.
*   **`AddSC_boss_magmus`**: Registers the script with the name "boss_magmus" and links the AI getter function.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_magmus

*Source:* boss_magmus.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_magmusAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | InstanceData/SetData | — | — |
| Aggro | method | InstanceData/SetData | — | — |
| JustDied | method | InstanceData/SetData | — | — |
| UpdateAI | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Unit.Main/GetHealthPercent, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_boss_magmus | function | — | — | — |
| AddSC_boss_magmus | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
