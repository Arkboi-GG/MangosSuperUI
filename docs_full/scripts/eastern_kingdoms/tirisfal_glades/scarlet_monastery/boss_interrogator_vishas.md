<!-- provenance: verbose -->
# boss_interrogator_vishas

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`boss_interrogator_vishas.cpp` implements the AI for **Interrogator Vishas**, a boss in the **Scarlet Monastery** instance. The `boss_interrogator_vishasAI` class manages combat mechanics (melee and **Shadow Word: Pain**), health-based dialogue triggers, and a specific death event that causes another NPC, **Vorrel**, to speak.

## Member-by-Member Behavior

### Initialization and State

**`boss_interrogator_vishasAI`**  
Constructs the AI, casting the creature’s instance data to `ScriptedInstance*` and storing it in `m_pInstance`. Immediately calls `Reset()` to initialize timers and flags.

**`Reset`**  
Resets `Yell30` and `Yell60` to `false` and sets `ShadowWordPain_Timer` to 5000 ms.

### Combat Events

**`Aggro`**  
Broadcasts `SAY_AGGRO` ("Tell me... tell me everything!") via `ScriptMgr::DoScriptText`.

**`KilledUnit`**  
Broadcasts `SAY_KILL` ("Purged by pain!") when a player dies.

**`JustDied`**  
Retrieves Vorrel’s GUID from `m_pInstance->GetData64(DATA_VORREL)`. If Vorrel is found in the map, triggers `SAY_TRIGGER_VORREL` ("Finally. The bastard got what he deserved.") from Vorrel’s unit.

### Main Loop

**`UpdateAI`**  
Executes every tick:
1. Returns if no hostile target exists.
2. Triggers `SAY_HEALTH1` ("Naughty secrets!") if health ≤ 60% and `Yell60` is false.
3. Triggers `SAY_HEALTH2` ("I'll rip the secrets from your flesh!") if health ≤ 30% and `Yell30` is false.
4. Casts `SPELL_SHADOWWORDPAIN` on the victim if `ShadowWordPain_Timer` expires, resetting the timer to a random 5–15 second interval.
5. Performs melee attacks via `DoMeleeAttackIfReady`.

### Registration

**`GetAI_boss_interrogator_vishas`**  
Factory function returning a new `boss_interrogator_vishasAI` instance.

**`AddSC_boss_interrogator_vishas`**  
Registers the script with `ScriptMgr` under the name `"boss_interrogator_vishas"`. Called by `ScriptLoader::AddScripts`.

## Cross-Unit Boundaries

*   **`ScriptedAI`**: Base class providing `DoCastSpellIfCan`, `DoMeleeAttackIfReady`, and `DoScriptText`.
*   **`ScriptedInstance`**: Accessed via `m_pInstance` to retrieve `DATA_VORREL` (Vorrel’s GUID).
*   **`Map` / `WorldObject`**: `GetMap()` and `GetUnit()` locate Vorrel in the world for the death dialogue.
*   **`Unit`**: `GetHealthPercent()`, `GetVictim()`, and `SelectHostileTarget()` drive combat logic.
*   **`shared_Util`**: `urand()` generates random spell cooldowns.
*   **`ScriptMgr`**: `DoScriptText()` broadcasts dialogue; `RegisterSelf()` registers the script.
*   **`ScriptLoader`**: Calls `AddSC_boss_interrogator_vishas` during startup.

## Data Model

No database tables are accessed. All data (spell IDs, dialogue IDs, instance keys) is hardcoded or held in instance memory.

## Notable Implementation Details

*   **Cross-NPC Dialogue**: Vishas’s death triggers Vorrel’s speech. This relies on `m_pInstance` correctly storing Vorrel’s GUID. If `m_pInstance` is null or Vorrel is not in the map, the dialogue fails silently.
*   **One-Time Taunts**: `Yell30` and `Yell60` ensure health taunts fire only once per encounter.
*   **Randomized Casting**: `Shadow Word: Pain` uses a random 5–15 second cooldown, preventing predictable patterns.

## Member Reference

**`boss_interrogator_vishasAI`**: Constructor initializing instance data and calling `Reset()`.

**`Reset`**: Resets dialogue flags and spell timer.

**`Aggro`**: Broadcasts aggro dialogue.

**`KilledUnit`**: Broadcasts kill dialogue.

**`JustDied`**: Triggers Vorrel’s dialogue upon Vishas’s death.

**`UpdateAI`**: Manages health taunts, spell casting, and melee attacks.

**`GetAI_boss_interrogator_vishas`**: Factory function for the AI.

**`AddSC_boss_interrogator_vishas`**: Registers the script with the server.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_interrogator_vishas

*Source:* boss_interrogator_vishas.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_interrogator_vishasAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | — | — | — |
| Aggro | method | ScriptMgr/DoScriptText | — | — |
| KilledUnit | method | ScriptMgr/DoScriptText | — | — |
| JustDied | method | InstanceData/GetData64, Map.Main/GetUnit, ObjectGuid/ObjectGuid#5, ScriptMgr/DoScriptText, WorldObject.Object/GetMap | — | — |
| UpdateAI | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, ScriptMgr/DoScriptText, shared_Util/urand, Unit.Main/GetHealthPercent, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_boss_interrogator_vishas | function | — | — | — |
| AddSC_boss_interrogator_vishas | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
