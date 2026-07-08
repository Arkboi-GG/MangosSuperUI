<!-- provenance: verbose -->
# boss_flamegor

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`boss_flamegor.cpp` implements the AI for **Flamegor**, a boss in the **Blackwing Lair** instance. The `boss_flamegorAI` class manages combat rotation, threat modification, and instance state reporting. It relies on three timed spells (**Shadow Flame**, **Wing Buffet**, **Frenzy**) and a probabilistic melee enhancement (**Thrash**). The unit also provides the registration hooks required by the server’s script loader.

## Member-by-Member Behavior

### Initialization and State

*   **`boss_flamegorAI` (Constructor)**: Retrieves `ScriptedInstance` data from the creature and initializes timers via `Reset()`.
*   **`Reset`**: Sets timer defaults: `m_uiShadowFlameTimer` (16s), `m_uiWingBuffetTimer` (30s), `m_uiFrenzyTimer` (10s). A source comment notes these values are likely inaccurate.

### Combat Lifecycle

*   **`Aggro`**: Updates instance data to `IN_PROGRESS` and marks the creature as in combat with the zone.
*   **`JustDied`**: Updates instance data to `DONE`.
*   **`JustReachedHome`**: Updates instance data to `FAIL` (e.g., on despawn/timeout).

### Abilities and Threat

*   **`SpellHitTarget`**: If **Wing Buffet** hits a player who has threat, reduces that player’s threat by 50%.
*   **`UpdateAI`**: The main loop. It casts **Shadow Flame** (self, 16s), **Wing Buffet** (victim, 30s), and **Frenzy** (self, 10s, with emote). On melee readiness, it has a ~66% chance (`!urand(0, 2)`) to cast **Thrash**. Finally, it executes standard melee attacks.

### Registration

*   **`GetAI_boss_flamegor`**: Factory function returning a new `boss_flamegorAI`.
*   **`AddSC_boss_flamegor`**: Registers the script with `ScriptMgr`; called by `ScriptLoader::AddScripts`.

## Cross-Unit Boundaries

*   **`ScriptedAI`**: Base class providing AI infrastructure.
*   **`WorldObject`/`Creature`**: Used for instance data retrieval (`GetInstanceData`) and combat state queries (`SelectHostileTarget`, `GetVictim`, `IsAttackReady`).
*   **`InstanceData`**: Receives state updates (`IN_PROGRESS`, `DONE`, `FAIL`) via `SetData`.
*   **`ThreatManager`**: Accessed in `SpellHitTarget` to check and modify player threat percentages.
*   **`ScriptMgr`**: Used for broadcasting emotes (`DoScriptText`) and registering the script (`RegisterSelf`).
*   **`shared_Util`**: Provides `urand` for Thrash probability.

## Data Model

This unit does not interact with any database tables. It operates entirely on in-memory instance state.

## Notable Implementation Details

*   **Unverified Timers**: The `Reset()` method explicitly comments that timer values are "probably wrong." Maintenance requires verification against official data.
*   **Thrash Logic**: Thrash is not timer-based; it triggers on melee readiness with a 2-in-3 probability, tying its frequency to attack speed.
*   **Threat Mitigation**: Wing Buffet reduces threat by 50% but does not remove the player from the threat list, serving as a mitigation rather than a taunt break.
*   **Null Safety**: All instance data calls are guarded by `if (m_pInstance)` checks.

## Member Reference

**boss_flamegorAI** (ctor): Initializes the AI, retrieves `ScriptedInstance` from the creature, and calls `Reset()`.

**Reset**: Sets `m_uiShadowFlameTimer` to 16000ms, `m_uiWingBuffetTimer` to 30000ms, and `m_uiFrenzyTimer` to 10000ms. Source notes these may be incorrect.

**Aggro**: Sets instance data to `IN_PROGRESS` for `TYPE_FLAMEGOR` and calls `SetInCombatWithZone`.

**JustDied**: Sets instance data to `DONE` for `TYPE_FLAMEGOR`.

**JustReachedHome**: Sets instance data to `FAIL` for `TYPE_FLAMEGOR`.

**SpellHitTarget**: If `SPELL_WING_BUFFET` hits a player with threat, reduces their threat by 50%.

**UpdateAI**: Manages timers for Shadow Flame (16s), Wing Buffet (30s, on victim), and Frenzy (10s, with emote). On melee readiness, casts Thrash with ~66% probability. Executes melee attacks.

**GetAI_boss_flamegor**: Factory function creating a new `boss_flamegorAI` instance.

**AddSC_boss_flamegor**: Creates a `Script` object named "boss_flamegor", links `GetAI_boss_flamegor`, and registers it with `ScriptMgr`. Called by `ScriptLoader::AddScripts`.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_flamegor

*Source:* boss_flamegor.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_flamegorAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | — | — | — |
| Aggro | method | Creature.Main/SetInCombatWithZone, InstanceData/SetData | — | — |
| JustDied | method | InstanceData/SetData | — | — |
| JustReachedHome | method | InstanceData/SetData | — | — |
| SpellHitTarget | method | Object/GetTypeId, ThreatManager/getThreat, ThreatManager/modifyThreatPercent#2, Unit.Main/GetThreatManager | — | — |
| UpdateAI | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, ScriptMgr/DoScriptText, shared_Util/urand, Unit.Main/GetVictim, Unit.Main/IsAttackReady, Unit.Main/SelectHostileTarget | — | — |
| GetAI_boss_flamegor | function | — | — | — |
| AddSC_boss_flamegor | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
