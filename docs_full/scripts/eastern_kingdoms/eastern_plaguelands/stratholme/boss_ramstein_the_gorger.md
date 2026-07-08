<!-- provenance: verbose -->
# boss_ramstein_the_gorger

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`boss_ramstein_the_gorger.cpp` implements the AI for **Ramstein the Gorger**, a boss in the **Stratholme** dungeon. The unit defines `boss_ramstein_the_gorgerAI`, inheriting from `ScriptedAI`, to manage combat abilities (**Trample**, **Knockout**) and melee attacks. It integrates with the `ScriptedInstance` system to report encounter states (`IN_PROGRESS`, `DONE`, `FAIL`) and manipulates threat to support the **Knockout** mechanic.

## Member-by-Member Behavior

### Initialization and State

**`boss_ramstein_the_gorgerAI` (Constructor)**
Initializes the AI for a `Creature`. It retrieves the instance data via `WorldObject::GetInstanceData()`, casting it to `ScriptedInstance*` and storing it in `m_pInstance`. It sets `Engaged` to `false` and calls `Reset()` to initialize timers.

**`Reset`**
Resets combat timers: `Trample_Timer` to 3000 ms, `Knockout_Timer` to 12000 ms. If `Engaged` is `true` (indicating a wipe or despawn while alive), it reports `FAIL` to the instance via `m_pInstance->SetData(TYPE_RAMSTEIN, FAIL)`. Finally, it sets `Engaged` to `false`.

**`Aggro`**
Triggered on first hostilities. Sets `Engaged` to `true` and reports `IN_PROGRESS` to the instance if `m_pInstance` is valid.

**`JustDied`**
Triggered on death. Reports `DONE` to the instance if `m_pInstance` is valid.

### Combat Loop

**`UpdateAI`**
Executes every tick. Returns early if no hostile target or victim exists.
1.  **Trample:** If `Trample_Timer` expires, casts `SPELL_TRAMPLE` (5568) on self via `DoCastSpellIfCan`. Resets timer to 7000 ms. Otherwise, decrements timer.
2.  **Knockout:** If `Knockout_Timer` expires, attempts to cast `SPELL_KNOCKOUT` (17307) on the victim. On success (`CAST_OK`), it reduces the victim's threat by 100% using `Unit::GetThreatManager().modifyThreatPercent(...)`. Resets timer to 10000 ms. Otherwise, decrements timer.
3.  **Melee:** Calls `DoMeleeAttackIfReady()`.

### Registration

**`GetAI_boss_ramstein_the_gorger`**
Factory function returning a new `boss_ramstein_the_gorgerAI` instance for a `Creature`.

**`AddSC_boss_ramstein_the_gorger`**
Registers the script with `ScriptMgr`. It creates a `Script` object, sets the name to `"boss_ramstein_the_gorger"`, assigns `GetAI_boss_ramstein_the_gorger` as the AI provider, and calls `Script::RegisterSelf()`. Called by `ScriptLoader::AddScripts`.

## Cross-Unit Boundaries

### Calls Out
*   **`ScriptedAI`**: Base class providing AI hooks and utilities.
*   **`WorldObject::GetInstanceData`**: Retrieves instance context in the constructor.
*   **`InstanceData::SetData`**: Updates dungeon progress (`TYPE_RAMSTEIN`) in `Reset`, `Aggro`, and `JustDied`.
*   **`CreatureAI::DoCastSpellIfCan`**: Checks cast conditions and executes spells in `UpdateAI`.
*   **`CreatureAI::DoMeleeAttackIfReady`**: Executes melee attacks in `UpdateAI`.
*   **`Unit::GetThreatManager` / `ThreatManager::modifyThreatPercent`**: Reduces victim threat by 100% after `Knockout` in `UpdateAI`.
*   **`Unit::GetVictim` / `Unit::SelectHostileTarget`**: Validates targets in `UpdateAI`.
*   **`Script::Script` / `ScriptMgr::RegisterSelf`**: Registers the script in `AddSC_boss_ramstein_the_gorger`.

### Called By
*   **`ScriptLoader::AddScripts`**: Invokes `AddSC_boss_ramstein_the_gorger` during startup.

## Data Model

This unit does not interact with any database tables. State is managed in-memory via `ScriptedInstance` and local variables.

## Notable Implementation Details

1.  **Threat Reset on Knockout:** `UpdateAI` explicitly reduces the victim's threat by 100% upon successful `Knockout` cast. This forces aggro transfer or protects the target, implying a tank-swap or survival mechanic.
2.  **Timer Asymmetry:** Initial timers differ from cooldowns. `Trample` starts at 3s (cooldown 7s), appearing early. `Knockout` starts at 12s (cooldown 10s), appearing later.
3.  **Wipe Detection:** `Reset` checks `Engaged` to report `FAIL` if the boss despawns while alive, ensuring accurate instance state.
4.  **Null Safety:** `m_pInstance` is checked before `SetData` calls to prevent crashes if instance data is missing.

## Member Reference

**`boss_ramstein_the_gorgerAI`** (ctor): Initializes AI, retrieves `ScriptedInstance*` via `WorldObject::GetInstanceData`, sets `Engaged` to false, and calls `Reset()`.

**`Reset`**: Sets `Trample_Timer` to 3000 ms and `Knockout_Timer` to 12000 ms. If `Engaged` is true, reports `FAIL` to instance. Sets `Engaged` to false.

**`Aggro`**: Sets `Engaged` to true. If `m_pInstance` is valid, reports `IN_PROGRESS` to instance.

**`JustDied`**: If `m_pInstance` is valid, reports `DONE` to instance.

**`UpdateAI`**: Manages combat. Returns if no target/victim. Casts `SPELL_TRAMPLE` on self every 7s (init 3s). Casts `SPELL_KNOCKOUT` on victim every 10s (init 12s); on success, reduces victim threat by 100%. Performs melee attacks.

**`GetAI_boss_ramstein_the_gorger`**: Factory function creating and returning a new `boss_ramstein_the_gorgerAI` instance.

**`AddSC_boss_ramstein_the_gorger`**: Registers script with `ScriptMgr` by creating a `Script` object, setting name/AI getter, and calling `RegisterSelf()`.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_ramstein_the_gorger

*Source:* boss_ramstein_the_gorger.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_ramstein_the_gorgerAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | InstanceData/SetData | — | — |
| Aggro | method | InstanceData/SetData | — | — |
| JustDied | method | InstanceData/SetData | — | — |
| UpdateAI | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, ThreatManager/modifyThreatPercent#2, Unit.Main/GetThreatManager, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_boss_ramstein_the_gorger | function | — | — | — |
| AddSC_boss_ramstein_the_gorger | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
