<!-- provenance: verbose -->
# boss_kurinnaxx

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_kurinnaxx

## Purpose & Responsibilities

`boss_kurinnaxx.cpp` implements the combat AI for Kurinnaxx, a boss in the Ruins of Ahn'Qiraj instance. The `boss_kurinnaxxAI` class handles spell rotations (Mortal Wound, Wide Slash, Trash), environmental hazards (summoning and cleaning up sand traps), and phase transitions (enrage at 30% health). It reports encounter state changes to the instance script and triggers specific dialogue upon death.

## Member-by-Member Behavior

### Lifecycle and State

**`boss_kurinnaxxAI` (Constructor)**
Retrieves the `ScriptedInstance` pointer from the creature and initializes timers via `Reset()`.

**`Reset`**
Resets all ability timers (`m_uiMortalWound_Timer`, `m_uiSandTrap_Timer`, `m_uiTrash_Timer`, `m_uiWideSlash_Timer`) to their base intervals and clears the `m_bHasEnraged` flag.

**`JustRespawned`**
Notifies the instance (`m_pInstance`) that the encounter status for `TYPE_KURINNAXX` is `NOT_STARTED`.

**`Aggro`**
Marks the creature as in combat with the zone and updates the instance status for `TYPE_KURINNAXX` to `IN_PROGRESS`.

**`JustDied`**
Updates the instance status for `TYPE_KURINNAXX` to `DONE` and triggers the scripted text `SAY_BREACHED` (11720) for NPC Ossirian on the current map.

### Combat Logic (`UpdateAI`)

The main loop manages five timers and melee attacks, returning early if no hostile target exists.

1.  **Sand Trap Cleanup**: If `m_uiCleanSandTrap_Timer` expires, it locates the nearest `GameObject` with entry `GO_TRAP` (180647), plays a despawn animation, and deletes it.
2.  **Enrage**: If health drops to ≤ 30% and `m_bHasEnraged` is false, it casts `SPELL_ENRAGE` (26527), emits `EMOTE_FRENZY` (10645), and sets the flag to prevent re-casting.
3.  **Mortal Wound**: On timer expiry, casts `SPELL_MORTALWOUND` (25646) on the victim and resets the timer to 9000 ms.
4.  **Sand Trap Summon**: On timer expiry, selects a random attacking target, summons `GO_TRAP` (180647) at the target's coordinates, sets the creature as the owner, resets `m_uiSandTrap_Timer` to `urand(5100, 7000)`, and starts a 5000 ms cleanup timer.
5.  **Wide Slash**: On timer expiry, casts `SPELL_WIDE_SLASH` (25814) on the victim and resets the timer to `10000 + (rand() % 10000)`.
6.  **Trash**: On timer expiry, casts `SPELL_TRASH` (3391) on the victim and resets the timer to `10000 + (rand() % 10000)`.
7.  **Melee**: Executes `DoMeleeAttackIfReady()`.

### Script Registration

**`GetAI_boss_kurinnaxx`**
Factory function returning a new `boss_kurinnaxxAI` instance.

**`AddSC_boss_kurinnaxx`**
Creates a `Script` object named `"boss_kurinnaxx"`, assigns `GetAI_boss_kurinnaxx` as the AI getter, and registers it with `ScriptMgr`.

## Cross-Unit Boundaries

- **Instance Data**: Calls `InstanceData/SetData` in `JustRespawned`, `Aggro`, and `JustDied` to update encounter progress. Uses `WorldObject.Object/GetInstanceData` in the constructor.
- **Combat**: Calls `Creature.Main/SetInCombatWithZone` in `Aggro`. Uses `Creature.Main/SelectAttackingTarget`, `Unit.Main/SelectHostileTarget`, `Unit.Main/GetVictim`, and `Unit.Main/GetHealthPercent` in `UpdateAI` for targeting and phase checks.
- **Spells & Actions**: Calls `CreatureAI/DoCastSpellIfCan` and `CreatureAI/DoMeleeAttackIfReady` in `UpdateAI`.
- **Scripting**: Calls `ScriptMgr/DoScriptText` for emotes and `ScriptMgr/DoOrSimulateScriptTextForMap` for death dialogue in `JustDied`.
- **Game Objects**: Calls `GridSearchers/GetClosestGameObjectWithEntry`, `WorldObject.Object/SummonGameObject`, `GameObject/SetOwnerGuid`, `GameObject/Delete`, and `WorldObject.Object/SendObjectDeSpawnAnim` in `UpdateAI` to manage sand traps.
- **Utilities**: Calls `shared_Util/urand` for random timers, and `WorldObject.Object/GetPositionX/Y/Z` and `Object/GetObjectGuid` for trap placement and ownership.

## Data Model

This unit does not access database tables directly. State is managed in-memory via `ScriptedInstance`.

## Notable Implementation Details

- **Manual Trap Cleanup**: Sand traps are explicitly deleted after 5 seconds via `m_uiCleanSandTrap_Timer` rather than relying solely on database despawn times.
- **Mixed Randomization**: `Sand Trap` uses `urand(5100, 7000)`, while `Wide Slash` and `Trash` use `rand() % 10000`.
- **TODO Comment**: Code notes `// TODO: Should use 26524 instead` regarding `SPELL_SUMMON_SANDTRAP`, but currently summons the game object directly via `SummonGameObject`.
- **Single Enrage**: `m_bHasEnraged` prevents the enrage spell from casting multiple times.

## Member Reference

**`boss_kurinnaxxAI`**
Constructor; retrieves `ScriptedInstance` and calls `Reset()`.

**`Reset`**
Resets timers and `m_bHasEnraged` flag.

**`JustRespawned`**
Sets instance data for `TYPE_KURINNAXX` to `NOT_STARTED`.

**`Aggro`**
Sets creature in combat with zone and instance data to `IN_PROGRESS`.

**`JustDied`**
Triggers `SAY_BREACHED` for `NPC_OSSIRIAN` and sets instance data to `DONE`.

**`UpdateAI`**
Manages sand trap cleanup, enrage, and timers for Mortal Wound, Sand Trap, Wide Slash, and Trash; executes melee attacks.

**`GetAI_boss_kurinnaxx`**
Factory function creating `boss_kurinnaxxAI`.

**`AddSC_boss_kurinnaxx`**
Registers the script with `ScriptMgr`.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_kurinnaxx

*Source:* boss_kurinnaxx.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_kurinnaxxAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | — | — | — |
| JustRespawned | method | InstanceData/SetData | — | — |
| Aggro | method | Creature.Main/SetInCombatWithZone, InstanceData/SetData | — | — |
| JustDied | method | InstanceData/SetData, ScriptMgr/DoOrSimulateScriptTextForMap, WorldObject.Object/GetMap | — | — |
| UpdateAI | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, GameObject/Delete, GameObject/SetOwnerGuid, GridSearchers/GetClosestGameObjectWithEntry, Object/GetObjectGuid, ScriptMgr/DoScriptText, shared_Util/urand, Unit.Main/GetHealthPercent, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SendObjectDeSpawnAnim, WorldObject.Object/SummonGameObject | — | — |
| GetAI_boss_kurinnaxx | function | — | — | — |
| AddSC_boss_kurinnaxx | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
