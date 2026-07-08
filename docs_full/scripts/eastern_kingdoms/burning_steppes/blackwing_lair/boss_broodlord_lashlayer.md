<!-- provenance: verbose -->
# boss_broodlord_lashlayer

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`boss_broodlord_lashlayer.cpp` implements the AI for **Broodlord Lashlayer**, a raid boss in the Blackwing Lair instance. The unit defines `boss_broodlordAI`, a `ScriptedAI` subclass that manages the boss's spell rotation (Cleave, Blast Wave, Mortal Strike, Knock Away), suppresses nearby neutral NPCs during combat, and reports encounter state to the `ScriptedInstance`.

## Member-by-Member Behavior

### Initialization and Lifecycle

**`boss_broodlordAI`**
Constructs the AI. Retrieves `ScriptedInstance` via `WorldObject.Object/GetInstanceData`, sets `m_bMobsDesactives` to `false`, and calls `Reset()`.

**`Reset`**
Initializes timers: `m_uiCleaveTimer` (8000ms), `m_uiBlastWaveTimer` (20000ms), `m_uiMortalStrikeTimer` (25000ms), `m_uiInCombatTimer` (2000ms). `m_uiKnockAwayTimer` is randomized (20000–25000ms) via `shared_Util/urand`. A comment notes these values may be inaccurate.

**`GetAI_boss_broodlord`**
Factory function allocating and returning a new `boss_broodlordAI` instance.

**`AddSC_boss_broodlord`**
Registers the script. Creates a `Script` named `"boss_broodlord"`, assigns `GetAI_boss_broodlord`, and calls `ScriptMgr/RegisterSelf`. Called by `ScriptLoader/AddScripts`.

### Combat State Management

**`Aggro`**
Sets instance state to `IN_PROGRESS` via `InstanceData/SetData`. If alive, disables nearby mobs via `SetMobsDesactivated(true)`, plays `SAY_AGGRO` via `ScriptMgr/DoScriptText`, and forces combat via `Creature.Main/SetInCombatWithZone`.

**`JustDied`**
Sets instance state to `DONE` via `InstanceData/SetData` and re-enables mobs via `SetMobsDesactivated(false)`.

**`JustReachedHome`**
Sets instance state to `FAIL` via `InstanceData/SetData` and re-enables mobs via `SetMobsDesactivated(false)`.

**`MoveInLineOfSight`**
Triggers combat via `Creature.Main/SetInCombatWithZone` if the target is a player, within 40.0f yards (`WorldObject.Object/IsWithinDistInMap`), in LOS (`WorldObject.Object/IsWithinLOSInMap`), accessible (`Unit.Main/IsInAccessablePlaceFor`), not stealthed (`Unit.Main/HasStealthAura`), and the boss is not already in combat (`Unit.Main/IsInCombat`).

**`UpdateAI`**
Main loop. Maintains combat zone status every 2000ms. Manages four spell timers using `CreatureAI/DoCastSpellIfCan`:
1. **Cleave:** On victim (13–20s random).
2. **Blast Wave:** On self (20–35s random).
3. **Mortal Strike:** On victim (20–30s random). ID depends on client build.
4. **Knock Away:** On victim (12–25s random).
Handles melee via `CreatureAI/DoMeleeAttackIfReady`. If out of combat area, evades via `ScriptedAI/EnterEvadeIfOutOfCombatArea` and plays `SAY_LEASH` via `ScriptMgr/DoScriptText`.

### Environmental and Threat Mechanics

**`SetMobsDesactivated`**
Toggles flags on nearby neutral NPCs (Warlock, Technician, Spellbinder, Overseer). Uses `GridSearchers/GetCreatureListWithEntryInGrid` (300.0f radius). Sets `UNIT_FLAG_NOT_SELECTABLE | UNIT_FLAG_SPAWNING | UNIT_FLAG_IMMUNE_TO_NPC` via `WorldObject.Object/SetFlag` if `on` is true, otherwise removes them via `WorldObject.Object/RemoveFlag`. Updates `m_bMobsDesactives`.

**`SpellHitTarget`**
If `SPELL_KNOCK_AWAY` hits, reduces `pCaster`'s threat by 50% via `Unit.Main/GetThreatManager` and `ThreatManager/modifyThreatPercent`. Note: `pCaster` is the boss, so this reduces the boss's threat toward itself, which is likely a logic error.

## Cross-Unit Boundaries

-   **Instance Data:** `Aggro`, `JustDied`, `JustReachedHome` call `InstanceData/SetData` to report `IN_PROGRESS`, `DONE`, or `FAIL`.
-   **Script Manager:** `Aggro`, `UpdateAI` call `ScriptMgr/DoScriptText` for audio cues.
-   **Grid Search:** `SetMobsDesactivated` calls `GridSearchers/GetCreatureListWithEntryInGrid` to find NPCs.
-   **Threat System:** `SpellHitTarget` calls `Unit.Main/GetThreatManager` and `ThreatManager/modifyThreatPercent`.
-   **Core Unit/Creature:** Members call `Creature.Main/SetInCombatWithZone`, `Unit.Main/IsAlive`, `Unit.Main/IsInCombat`, `Unit.Main/GetVictim`, `Unit.Main/SelectHostileTarget`, `Unit.Main/HasStealthAura`, `Unit.Main/IsInAccessablePlaceFor`.
-   **AI Framework:** `UpdateAI` calls `ScriptedAI/EnterEvadeIfOutOfCombatArea`, `CreatureAI/DoCastSpellIfCan`, `CreatureAI/DoMeleeAttackIfReady`.
-   **Utilities:** `Reset`, `UpdateAI` call `shared_Util/urand`.

## Data Model

No database tables are accessed. Configuration is hardcoded or derived from DBC files.

## Notable Implementation Details

-   **Timer Accuracy:** `Reset` comments suggest timer values may be incorrect.
-   **Client Build Dependency:** `SPELL_MORTAL_STRIKE` ID varies by `SUPPORTED_CLIENT_BUILD` to handle a historical damage bug fix.
-   **Threat Logic Error:** `SpellHitTarget` reduces the boss's own threat, likely intending to reduce the victim's.
-   **Redundant Cleanup:** `SetMobsDesactivated` clears a local vector unnecessarily.
-   **Summoned Mobs:** Commented-out GUID check implies summoned creatures are now included in flag toggles.

## Member Reference

**boss_broodlordAI**: Constructor initializing AI, retrieving instance data, and calling `Reset()`.
**Reset**: Resets spell and utility timers to base or randomized values.
**Aggro**: Sets instance state to `IN_PROGRESS`, disables nearby mobs, plays aggro text, and forces combat.
**JustDied**: Sets instance state to `DONE` and re-enables nearby mobs.
**JustReachedHome**: Sets instance state to `FAIL` and re-enables nearby mobs.
**MoveInLineOfSight**: Checks distance, LOS, stealth, and accessibility to trigger combat with players.
**SpellHitTarget**: Reduces `pCaster`'s threat by 50% if `SPELL_KNOCK_AWAY` hits.
**SetMobsDesactivated**: Toggles `NOT_SELECTABLE`, `SPAWNING`, and `IMMUNE_TO_NPC` flags on nearby neutral NPCs.
**UpdateAI**: Manages spell timers, melee attacks, combat zone maintenance, and leash evasion.
**GetAI_boss_broodlord**: Factory function returning a new `boss_broodlordAI` instance.
**AddSC_boss_broodlord**: Registers the script with the engine via `ScriptMgr/RegisterSelf`.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_broodlord_lashlayer

*Source:* boss_broodlord_lashlayer.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_broodlordAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | shared_Util/urand | — | — |
| Aggro | method | Creature.Main/SetInCombatWithZone, InstanceData/SetData, ScriptMgr/DoScriptText, Unit.Main/IsAlive | — | — |
| JustDied | method | InstanceData/SetData | — | — |
| JustReachedHome | method | InstanceData/SetData | — | — |
| MoveInLineOfSight | method | Creature.Main/SetInCombatWithZone, Object/GetTypeId, Unit.Main/HasStealthAura, Unit.Main/IsInAccessablePlaceFor, Unit.Main/IsInCombat, WorldObject.Object/IsWithinDistInMap, WorldObject.Object/IsWithinLOSInMap | — | — |
| SpellHitTarget | method | ThreatManager/modifyThreatPercent#2, Unit.Main/GetThreatManager | — | — |
| SetMobsDesactivated | method | GridSearchers/GetCreatureListWithEntryInGrid#2, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag | — | — |
| UpdateAI | method | Creature.Main/SetInCombatWithZone, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, ScriptedAI/EnterEvadeIfOutOfCombatArea, ScriptMgr/DoScriptText, shared_Util/urand, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_boss_broodlord | function | — | — | — |
| AddSC_boss_broodlord | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
