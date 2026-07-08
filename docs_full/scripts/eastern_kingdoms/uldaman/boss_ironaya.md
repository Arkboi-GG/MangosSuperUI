# boss_ironaya

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`boss_ironaya.cpp` implements the AI for **Ironaya**, a boss in the **Uldaman** dungeon. The `boss_ironayaAI` class manages combat phases triggered by health thresholds and time, including an initial movement sequence, a one-time knockback at 50% health, a one-time stomp at 25% health, and periodic arcing smash casts.

## Member-by-Member Behavior

### Initialization and State

**`boss_ironayaAI` (Constructor)**
Initializes the AI for a `Creature`. It casts the creature’s instance data to `ScriptedInstance`, sets `hasMoved` to `false`, and calls `Reset()` to clear spell-cast flags.

**`Reset`**
Resets `hasCastedKnockaway` and `hasCastedWstomp` to `false`, enabling these one-time abilities for future encounters. Note that `hasMoved` is not reset here; it is controlled by the constructor and `UpdateAI`.

### Combat Logic

**`Aggro`**
Triggered on combat entry. It plays the aggro text (`SAY_AGGRO`) via `ScriptMgr` and marks the creature as in combat with the zone.

**`UpdateAI`**
The main update loop handles four logic blocks:
1.  **Initial Movement:** If the creature is not immune and `hasMoved` is false, it retrieves a target GUID from `instance->GetData64(0)`. If valid, it attacks that unit and sets `hasMoved` to `true`.
2.  **Target Check:** Returns early if no hostile target or victim exists.
3.  **50% Health Threshold:** If `hasCastedKnockaway` is false and health is below 50%, it casts `SPELL_KNOCKAWAY` on the victim, reduces the victim’s threat by 100%, and selects a new target (skipping the current victim if it remains top aggro). It sets `hasCastedKnockaway` to `true`.
4.  **Periodic & 25% Threshold:** Decrements `Arcing_Timer`; if expired, casts `SPELL_ARCINGSMASH` and resets the timer to 13,000 ms. If `hasCastedWstomp` is false and health is below 25%, it casts `SPELL_WSTOMP` and sets the flag. Finally, it attempts a melee attack.

### Registration

**`GetAI_boss_ironaya`**
Factory function returning a new `boss_ironayaAI` instance.

**`AddSC_boss_ironaya`**
Registers the script with `ScriptMgr` by creating a `Script` object named "boss_ironaya" and linking the `GetAI` factory. Called by `ScriptLoader`.

## Cross-Unit Boundaries

*   **`ScriptedAI` / `ScriptedInstance`:** Inherits base AI behavior. Uses `InstanceData/GetData64` to fetch the initial movement target GUID from the instance script.
*   **`Creature` / `Unit`:** Uses `Creature.Main` and `Unit.Main` methods for health checks, flag verification, victim/target selection, and spell casting.
*   **`ThreatManager`:** `UpdateAI` calls `modifyThreatPercent` to remove threat from the knocked-away player.
*   **`ScriptMgr`:** `Aggro` calls `DoScriptText` for audio/text; `AddSC_boss_ironaya` calls `RegisterSelf` for script registration.
*   **`ScriptLoader`:** Calls `AddSC_boss_ironaya` during server startup.

## Data Model

This unit does not access any database tables. It relies on runtime instance data and hardcoded spell/text IDs.

## Notable Implementation Details

*   **Asymmetric State Reset:** `hasMoved` is initialized in the constructor and set in `UpdateAI`, whereas spell flags are reset in `Reset()`. This ensures the initial movement only happens once per spawn, while spell cooldowns reset on despawn.
*   **Threat Manipulation:** The 50% health mechanic explicitly drops threat by 100% to force a target switch after the knockback. The code includes a fallback to select the second-highest threat target if the primary remains the same.
*   **Instance Dependency:** The initial attack depends on `instance->GetData64(0)`. If the instance script fails to populate this GUID, the initial movement logic fails silently.

## Member Reference

**`boss_ironayaAI`**: Constructor initializing instance data, flags, and calling `Reset`.
**`Reset`**: Resets `hasCastedKnockaway` and `hasCastedWstomp` flags.
**`Aggro`**: Plays aggro text and sets combat zone status.
**`UpdateAI`**: Handles initial movement, 50% knockback/threat drop, 25% stomp, periodic arcing smash, and melee attacks.
**`GetAI_boss_ironaya`**: Factory function creating a new `boss_ironayaAI` instance.
**`AddSC_boss_ironaya`**: Registers the script with `ScriptMgr`.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_ironaya

*Source:* boss_ironaya.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_ironayaAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | — | — | — |
| Aggro | method | Creature.Main/SetInCombatWithZone, ScriptMgr/DoScriptText | — | — |
| UpdateAI | method | Creature.Main/SelectAttackingTarget, CreatureAI/AttackStart, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, InstanceData/GetData64, Object/HasFlag, SpellCaster/CastSpell#2, ThreatManager/modifyThreatPercent#2, Unit.Main/GetHealthPercent, Unit.Main/GetThreatManager, Unit.Main/GetUnit, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_boss_ironaya | function | — | — | — |
| AddSC_boss_ironaya | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
