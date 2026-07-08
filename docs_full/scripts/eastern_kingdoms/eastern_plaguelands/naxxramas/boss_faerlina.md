# boss_faerlina

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`boss_faerlina.cpp` implements the AI and combat mechanics for the boss **Faerlina** in the Naxxramas raid. It manages her primary abilities—**Poison Bolt Volley**, **Rain of Fire**, and **Enrage**—and handles the interaction with the **Widow’s Embrace** debuff (applied by summoned worshippers), which suppresses her casting and delays her enrage. The unit also includes a custom spell script to limit Poison Bolt Volley to 10 targets and registers these components with the server’s script manager.

## Member-by-Member Behavior

### Initialization and Lifecycle
*   **`boss_faerlinaAI` (Constructor)**: Retrieves the `instance_naxxramas` data pointer. Logs an error if the cast fails. Calls `Reset()` to initialize timers.
*   **`Reset`**: Sets `m_uiPoisonBoltVolleyTimer` to 8s, `m_uiRainOfFireTimer` to 16s, and `m_uiEnrageTimer` to 60s.
*   **`JustReachedHome`**: Signals `FAIL` to the instance script upon evasion.
*   **`JustDied`**: Plays death text and signals `DONE` to the instance script.
*   **`Aggro`**: Plays pull text and signals `IN_PROGRESS` to the instance script.

### Combat Mechanics
*   **`UpdateAI`**: The main loop. It manages three timers:
    1.  **Poison Bolt Volley**: If `SPELL_WIDOWS_EMBRACE` is active, it delays casting by 2.5s (simulating silence). Otherwise, it casts on the victim and resets the timer via `POSIONBOLT_VOLLEY_CD()`.
    2.  **Rain of Fire**: Casts on a random hostile target and resets the timer via `RAINOFFIRE_CD()`.
    3.  **Enrage**: If the timer expires and `SPELL_WIDOWS_EMBRACE` is *not* active, it casts Enrage, plays a taunt, and resets the timer to 60s.
    *   Also checks `HandleEvadeOutOfHome`; if true, forces evasion.
*   **`SpellHit`**: Intercepts `SPELL_WIDOWS_EMBRACE`. It sets the Enrage timer to `max(current, 30000)` and removes any existing Enrage auras, implementing the mechanic where sacrificing worshippers delays/prevents enrage.
*   **`KilledUnit`**: Plays a random kill taunt if the victim is a player.
*   **`MoveInLineOfSight`**: Contains a placeholder check for 60-yard distance (currently inert) and delegates to the parent class.

### Helpers and Registration
*   **`POSIONBOLT_VOLLEY_CD` / `RAINOFFIRE_CD`**: Return random cooldowns (10–12s and 8–12s respectively).
*   **`OnSetTargetMap`**: Limits Poison Bolt Volley to 10 targets.
*   **`GetAI_boss_faerlina` / `GetScript_FaerlinaPoisonBoltVolley`**: Factory functions for the AI and spell script.
*   **`AddSC_boss_faerlina`**: Registers both scripts with `ScriptMgr`.

## Cross-Unit Boundaries

*   **`instance_naxxramas.Main`**: Called by `ctor`, `JustReachedHome`, `Aggro`, `JustDied`, and `UpdateAI` to update encounter state (`IN_PROGRESS`, `DONE`, `FAIL`) and check bounds (`HandleEvadeOutOfHome`).
*   **`ScriptMgr`**: Called by `Aggro`, `KilledUnit`, `JustDied`, `UpdateAI`, and `AddSC_boss_faerlina` for text/sound events (`DoScriptText`) and registration (`RegisterSelf`).
*   **`shared_Util`**: Called by `POSIONBOLT_VOLLEY_CD`, `RAINOFFIRE_CD`, `KilledUnit`, and `UpdateAI` for randomization (`urand`).
*   **`ScriptedAI` / `BasicAI`**: Base classes. `ctor` calls `ScriptedAI` constructor; `MoveInLineOfSight` calls `BasicAI::MoveInLineOfSight`.
*   **`Creature.Main` / `CreatureAI`**: Called by `UpdateAI` for targeting (`SelectHostileTarget`, `SelectAttackingTarget`), casting (`DoCastSpellIfCan`), melee (`DoMeleeAttackIfReady`), and evasion (`EnterEvadeMode`).
*   **`Unit.Main`**: Called by `SpellHit` (`RemoveAurasDueToSpell`) and `UpdateAI` (`HasAura`, `GetVictim`).
*   **`WorldObject.Object`**: Called by `ctor` (`GetInstanceData`) and `MoveInLineOfSight` (`IsWithinDistInMap`).
*   **`Object`**: Called by `KilledUnit` (`GetTypeId`) to verify player victims.
*   **`ScriptLoader`**: Calls `AddSC_boss_faerlina` at startup.

## Data Model

No database tables are accessed. State is managed in-memory via `instance_naxxramas` and AI timers.

## Notable Implementation Details

*   **Widow’s Embrace Interaction**: `SpellHit` ensures the Enrage timer never drops below 30s when hit by Widow’s Embrace and removes active Enrage auras. `UpdateAI` suppresses Enrage casting entirely if the aura is present.
*   **Silence Simulation**: `UpdateAI` checks for `SPELL_WIDOWS_EMBRACE` before casting Poison Bolt Volley. If present, it sets a 2.5s retry timer instead of casting, simulating a silence effect.
*   **Hardcoded Target Limit**: `OnSetTargetMap` forces Poison Bolt Volley to affect exactly 10 targets, overriding default spell behavior.
*   **Incomplete Aggro Range**: `MoveInLineOfSight` contains a `todo` comment and an empty conditional block for 60-yard checks, indicating unfinished aggro range logic.

## Member Reference

**POSIONBOLT_VOLLEY_CD**: Returns a random cooldown (10,000–12,000 ms) for Poison Bolt Volley. Calls `shared_Util/urand`.

**RAINOFFIRE_CD**: Returns a random cooldown (8,000–12,000 ms) for Rain of Fire. Calls `shared_Util/urand`.

**boss_faerlinaAI**: Constructor. Retrieves instance data, logs errors on failure, and calls `Reset()`. Calls `Log.Main/Out`, `ScriptedAI/ScriptedAI`, `WorldObject.Object/GetInstanceData`.

**Reset**: Initializes spell and enrage timers to their starting values. No external calls.

**SpellHit**: Intercepts `SPELL_WIDOWS_EMBRACE` to boost Enrage timer to ≥30s and remove Enrage auras. Calls `Unit.Main/RemoveAurasDueToSpell`.

**JustReachedHome**: Signals encounter failure to the instance script. Calls `instance_naxxramas.Main/SetData`.

**Aggro**: Plays pull text and signals encounter start. Calls `instance_naxxramas.Main/SetData`, `ScriptMgr/DoScriptText`.

**MoveInLineOfSight**: Placeholder distance check; delegates to parent. Calls `BasicAI/MoveInLineOfSight`, `WorldObject.Object/IsWithinDistInMap`.

**KilledUnit**: Plays random kill taunt for player victims. Calls `Object/GetTypeId`, `ScriptMgr/DoScriptText`, `shared_Util/urand`.

**JustDied**: Plays death text and signals encounter completion. Calls `instance_naxxramas.Main/SetData`, `ScriptMgr/DoScriptText`.

**UpdateAI**: Main loop managing Poison Bolt Volley, Rain of Fire, and Enrage timers; checks for Widow’s Embrace suppression; handles evasion. Calls `Creature.Main/AI`, `Creature.Main/SelectAttackingTarget`, `CreatureAI/DoCastSpellIfCan`, `CreatureAI/DoMeleeAttackIfReady`, `CreatureAI/EnterEvadeMode`, `instance_naxxramas.Main/HandleEvadeOutOfHome`, `ScriptMgr/DoScriptText`, `shared_Util/urand`, `Unit.Main/GetVictim`, `Unit.Main/HasAura#2`, `Unit.Main/SelectHostileTarget`.

**GetAI_boss_faerlina**: Factory function creating `boss_faerlinaAI`. No external calls.

**OnSetTargetMap**: Limits Poison Bolt Volley to 10 targets. No external calls.

**GetScript_FaerlinaPoisonBoltVolley**: Factory function creating `FaerlinaPoisonBoltVolleyScript`. No external calls.

**AddSC_boss_faerlina**: Registers AI and spell scripts. Calls `Script/Script`, `ScriptMgr/RegisterSelf`. Called by `ScriptLoader/AddScripts`.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_faerlina

*Source:* boss_faerlina.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| POSIONBOLT_VOLLEY_CD | function | shared_Util/urand | — | — |
| RAINOFFIRE_CD | function | shared_Util/urand | — | — |
| boss_faerlinaAI | ctor | Log.Main/Out, ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | — | — | — |
| SpellHit | method | Unit.Main/RemoveAurasDueToSpell | — | — |
| JustReachedHome | method | instance_naxxramas.Main/SetData | — | — |
| Aggro | method | instance_naxxramas.Main/SetData, ScriptMgr/DoScriptText | — | — |
| MoveInLineOfSight | method | BasicAI/MoveInLineOfSight, WorldObject.Object/IsWithinDistInMap | — | — |
| KilledUnit | method | Object/GetTypeId, ScriptMgr/DoScriptText, shared_Util/urand | — | — |
| JustDied | method | instance_naxxramas.Main/SetData, ScriptMgr/DoScriptText | — | — |
| UpdateAI | method | Creature.Main/AI, Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, CreatureAI/EnterEvadeMode, instance_naxxramas.Main/HandleEvadeOutOfHome, ScriptMgr/DoScriptText, shared_Util/urand, Unit.Main/GetVictim, Unit.Main/HasAura#2, Unit.Main/SelectHostileTarget | — | — |
| GetAI_boss_faerlina | function | — | — | — |
| OnSetTargetMap | method | — | — | — |
| GetScript_FaerlinaPoisonBoltVolley | function | — | — | — |
| AddSC_boss_faerlina | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
