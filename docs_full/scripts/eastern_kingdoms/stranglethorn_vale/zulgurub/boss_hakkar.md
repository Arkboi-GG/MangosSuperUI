<!-- provenance: verbose -->
# boss_hakkar

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_hakkar

**Purpose & Responsibilities**
`boss_hakkar.cpp` implements the AI for Hakkar the Soulflayer, the final boss of the Zul'Gurub raid. It manages combat mechanics including periodic spells (Blood Siphon, Corrupted Blood, Cause Insanity), threat manipulation for the Cause Insanity ability, and conditional "Aspect" spells that depend on whether preceding High Priest bosses remain alive. It also enforces a spatial anti-exploit check to prevent the boss from being pulled outside its intended arena bounds.

## Member-by-Member Behavior

### Initialization and State Management

**`boss_hakkarAI` (Constructor)**
Initializes the AI by retrieving the `ScriptedInstance` pointer from the creature’s instance data via `WorldObject.Object/GetInstanceData` and calling `Reset()` to initialize timers and state.

**`Reset`**
Resets all internal timers to their default intervals (e.g., Blood Siphon: 90s, Corrupted Blood: 15s, Berserk: 600s) and clears state variables (`InsanePlayerGuid`, `Enraged`). Updates instance data via `InstanceData/SetData` to mark the encounter as `NOT_STARTED`.

**`Aggro`**
Triggered when Hakkar gains a hostile target. Updates instance data to `IN_PROGRESS` via `InstanceData/SetData`, plays the aggro emote (`SAY_AGGRO`) via `ScriptMgr/DoScriptText`, and delegates to `ScriptedAI/Aggro`.

**`JustDied`**
Triggered upon Hakkar’s death. Updates instance data to `DONE` via `InstanceData/SetData`.

### Combat Logic (`UpdateAI`)

**`UpdateAI`**
The main execution loop, called periodically with a time difference (`diff`). It performs the following checks in order:

1.  **Validity Check:** Returns early if there is no instance data, no hostile targets (`Creature.Main/SelectHostileTarget`), or no current victim (`Unit.Main/GetVictim`).
2.  **Anti-Exploit Spatial Check:**
    *   Retrieves Hakkar’s Z-coordinate via `WorldObject.Object/GetPositionZ`.
    *   If the Z-position is outside the range `[45.8f, 57.28f]`, the AI assumes an exploit and calls `ScriptedAI/EnterEvadeMode` to flee.
3.  **Cause Insanity Threat Restoration:**
    *   Manages `CCDelayInsanity_Timer`. If expired and `InsanePlayerGuid` is set:
        *   Retrieves the player via `Map.Main/GetPlayer` using the stored GUID (`ObjectGuid/ObjectGuid#5`).
        *   If the player lacks the `SPELL_CAUSEINSANITY` aura (`Unit.Main/HasAura#2`), it restores their threat by reducing their threat percentage by 100% (`ThreatManager/modifyThreatPercent#2`) and adding the stored `InsanePlayerAggro` value (`ThreatManager/addThreatDirectly`).
        *   Clears the GUID and aggro storage.
4.  **Spell Casting Gate:**
    *   Returns early if Hakkar is currently casting a non-melee spell (`SpellCaster/IsNonMeleeSpellCasted`).
5.  **Blood Siphon:**
    *   If `BloodSiphon_Timer` expires, casts `SPELL_BLOODSIPHON_STUN` on Hakkar via `CreatureAI/DoCastSpellIfCan`. Resets timer to 90,000 ms.
6.  **Corrupted Blood:**
    *   If `CorruptedBlood_Timer` expires, selects a random target via `Creature.Main/SelectAttackingTarget` and casts `SPELL_CORRUPTEDBLOOD`. Resets timer to a random value (14–16s) using `shared_Util/urand`.
7.  **Cause Insanity:**
    *   If `CauseInsanity_Timer` expires and Hakkar has a victim:
        *   Stores the victim’s GUID (`Object/GetGUID`) and current threat (`ThreatManager/getThreat` via `Unit.Main/GetThreatManager`).
        *   Casts `SPELL_CAUSEINSANITY` on the victim.
        *   Starts `CCDelayInsanity_Timer` (4,000 ms) to delay threat restoration.
        *   Resets `CauseInsanity_Timer` to a random value (20–25s).
8.  **Berserk:**
    *   If `Berserk_Timer` expires, casts `SPELL_BERSERK` on Hakkar if the aura is not present. Resets timer to 2,000 ms.
9.  **High Priest Aspects:**
    *   For each High Priest (Jeklik, Venoxis, Marli, Thekal, Arlokk), checks if the boss is marked as `DONE` via `InstanceData/GetData`.
    *   If **not** done, casts the corresponding "Aspect" spell on the victim (or Hakkar for Thekal/Arlokk) when its specific timer expires. Timers reset to fixed or random intervals.
10. **Melee Attack:**
    *   Calls `CreatureAI/DoMeleeAttackIfReady` to perform physical attacks.

### Registration

**`GetAI_boss_hakkar`**
Factory function that creates and returns a new `boss_hakkarAI` instance.

**`AddSC_boss_hakkar`**
Registers the script with the core. Creates a `Script` object, sets its name to `"boss_hakkar"`, assigns `GetAI_boss_hakkar` as the AI getter, and registers it via `ScriptMgr/RegisterSelf`. Called by `ScriptLoader/AddScripts`.

## Cross-Unit Boundaries

*   **`InstanceData` (via `m_pInstance`):**
    *   *Direction:* Outbound.
    *   *Usage:* `Reset`, `Aggro`, `JustDied`, and `UpdateAI` update encounter state (`NOT_STARTED`, `IN_PROGRESS`, `DONE`) and query the status of other bosses (`TYPE_JEKLIK`, etc.) to determine if Aspect spells should be cast.
*   **`ScriptedAI`:**
    *   *Direction:* Inheritance/Outbound.
    *   *Usage:* Constructor initializes base class. `Aggro` calls base `Aggro`. `UpdateAI` calls `EnterEvadeMode` for anti-exploit checks.
*   **`ScriptMgr`:**
    *   *Direction:* Outbound.
    *   *Usage:* `Aggro` uses `DoScriptText` for emotes. `AddSC_boss_hakkar` uses `RegisterSelf` for registration.
*   **`Creature` / `Unit` / `WorldObject`:**
    *   *Direction:* Outbound.
    *   *Usage:* `UpdateAI` accesses position (`GetPositionZ`), targets (`SelectHostileTarget`, `SelectAttackingTarget`, `GetVictim`), threat (`GetThreatManager`), and auras (`HasAura`).
*   **`Map`:**
    *   *Direction:* Outbound.
    *   *Usage:* `UpdateAI` uses `GetMap()->GetPlayer` to retrieve player objects by GUID for the Cause Insanity mechanic.
*   **`ThreatManager`:**
    *   *Direction:* Outbound.
    *   *Usage:* `UpdateAI` manipulates threat values (`addThreatDirectly`, `modifyThreatPercent`, `getThreat`) to implement the Cause Insanity mechanic.
*   **`ScriptLoader`:**
    *   *Direction:* Inbound.
    *   *Usage:* Calls `AddSC_boss_hakkar` during server startup.

## Data Model

This unit does not directly query or modify database tables. It interacts with runtime instance data structures provided by the `ScriptedInstance` interface. No SQL queries are present in this file.

## Notable Implementation Details

*   **Anti-Exploit Z-Check:** The hard-coded Z-coordinate range (`45.8f` to `57.28f`) in `UpdateAI` prevents players from pulling Hakkar out of the arena. If violated, the boss evades immediately.
*   **Cause Insanity Threat Logic:** The AI stores the victim’s GUID and threat value before casting `SPELL_CAUSEINSANITY`. After a 4-second delay, if the aura is gone, it restores threat by first reducing the player’s threat percentage by 100% and then adding the stored absolute threat value. This sequence attempts to restore the player to their previous threat level relative to others.
*   **Conditional Aspects:** The AI checks the status of five High Priests every tick. If any are not `DONE`, Hakkar casts their respective Aspect spell, tying final boss difficulty to prior encounter progress.
*   **Berserk Mechanic:** Berserk is triggered solely by a 600-second timer. Commented-out code suggests a health-based trigger (`< 5.0f`) was considered but not implemented.

## Member Reference

**`boss_hakkarAI`**
Constructor that initializes the AI, retrieves the instance data pointer, and calls `Reset()`.

**`Reset`**
Resets all internal timers, clears state variables, and updates the instance data to `NOT_STARTED`.

**`Aggro`**
Sets instance data to `IN_PROGRESS`, plays the aggro sound, and delegates to the base class.

**`JustDied`**
Sets instance data to `DONE`.

**`UpdateAI`**
Main AI loop handling spell rotations, threat manipulation for Cause Insanity, anti-exploit spatial checks, and conditional casting of High Priest Aspects based on instance data.

**`GetAI_boss_hakkar`**
Factory function returning a new `boss_hakkarAI` instance.

**`AddSC_boss_hakkar`**
Registers the script with the core’s script manager.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_hakkar

*Source:* boss_hakkar.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_hakkarAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | InstanceData/SetData | — | — |
| Aggro | method | InstanceData/SetData, ScriptedAI/Aggro, ScriptMgr/DoScriptText | — | — |
| JustDied | method | InstanceData/SetData | — | — |
| UpdateAI | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, InstanceData/GetData, Map.Main/GetPlayer, Object/GetGUID, ObjectGuid/ObjectGuid#5, ScriptedAI/EnterEvadeMode, shared_Util/urand, SpellCaster/IsNonMeleeSpellCasted, ThreatManager/addThreatDirectly, ThreatManager/getThreat, ThreatManager/modifyThreatPercent#2, Unit.Main/GetThreatManager, Unit.Main/GetVictim, Unit.Main/HasAura#2, Unit.Main/SelectHostileTarget, WorldObject.Object/GetMap, WorldObject.Object/GetPositionZ | — | — |
| GetAI_boss_hakkar | function | — | — | — |
| AddSC_boss_hakkar | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
