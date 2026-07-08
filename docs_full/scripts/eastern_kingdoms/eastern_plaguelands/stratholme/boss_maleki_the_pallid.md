<!-- provenance: verbose -->
# boss_maleki_the_pallid

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`boss_maleki_the_pallid.cpp` implements the combat AI for **Maleki the Pallid**, a boss in the Stratholme instance. The `boss_maleki_the_pallidAI` class manages a rotation of Frostbolt, Ice Tomb, and conditional Drain Life/Mana spells. Key mechanics include manipulating threat to remove targets from aggro lists during Ice Tomb and restoring it upon expiration, and dynamically adjusting movement thresholds based on health and mana levels to prioritize closing distance for draining spells.

## Member-by-Member Behavior

### Initialization and Lifecycle

**`boss_maleki_the_pallidAI` (Constructor)**
Retrieves the `ScriptedInstance` from the creature’s map data via `WorldObject::GetInstanceData`, casting it to `ScriptedInstance*` and storing it in `m_pInstance`. Immediately calls `Reset()` to initialize timers and state.

**`Reset`**
Initializes internal state:
- `Frostbolt_Timer`: 1000 ms.
- `IceTomb_Timer`: 12000 ms.
- `Drain_Timer`: 4000 ms.
- `IcedPlayerGuid` and `IcedPlayerAggro`: Cleared.
- `NeedMoveCloser`: `false`.

**`JustDied`**
If `m_pInstance` is valid, calls `InstanceData::SetData(TYPE_PALLID, DONE)` to signal boss defeat to the instance script.

### Utility

**`GetManaPercent`**
Calculates current mana as a percentage of maximum mana using `Unit::GetPower` and `Unit::GetMaxPower`: `(CurrentMana / MaxMana) * 100`.

### Combat Logic (`UpdateAI`)

**`UpdateAI`**
Executes the main combat loop:

1.  **Target Validation**: Returns early if no hostile target or victim exists (`Creature::SelectHostileTarget`, `Unit::GetVictim`).
2.  **Ice Tomb Aggro Restoration**: If `IcedPlayerGuid` is set, retrieves the player from the map (`Map::GetPlayer`). If the player lacks the `SPELL_ICETOMB` aura (`Unit::HasAura`), restores their previous threat level (`IcedPlayerAggro`) via `ThreatManager::addThreatDirectly` and clears the tracking variables. If the player is missing, clears variables without restoring threat.
3.  **Frostbolt**: If `Frostbolt_Timer` expires, casts `SPELL_FROSTBOLT` on the victim (`CreatureAI::DoCastSpellIfCan`) and resets the timer to 3500–4500 ms (`shared_Util::urand`).
4.  **Ice Tomb**: If `IceTomb_Timer` expires, casts `SPELL_ICETOMB` on the victim. Stores the victim’s GUID (`Object::GetGUID`) and current threat (`ThreatManager::getThreat`) in `IcedPlayerGuid`/`IcedPlayerAggro`. Reduces the victim’s threat by 100% (`ThreatManager::modifyThreatPercent`) to remove them from the threat list. Resets timer to 20000–25000 ms.
5.  **Drain Life/Mana**: Activates if Health < 60% (`Unit::GetHealthPercent`) or Mana < 50% (`GetManaPercent`). Sets `NeedMoveCloser` to `true`. If `Drain_Timer` expires, checks if the victim has mana (`Unit::GetPower`). Casts `SPELL_DRAIN_MANA` if yes, otherwise `SPELL_DRAIN_LIFE`. Resets timer to 12000–18000 ms. If conditions are no longer met, sets `NeedMoveCloser` to `false`.
6.  **Movement**: If a victim exists:
    - Determines distance threshold: 20.0 yards if `NeedMoveCloser` is true, otherwise 40.0 yards.
    - Forces chase if distance exceeds threshold (`WorldObject::GetDistance2d`) OR if Mana < 10%.
    - Enables combat movement (`CreatureAI::SetCombatMovement`) and chases (`Creature::MotionMaster::MoveChase`) if chasing; otherwise disables combat movement and idles (`Creature::MotionMaster::MoveIdle`).
    - If no victim, disables combat movement.
7.  **Melee**: Calls `CreatureAI::DoMeleeAttackIfReady()`.

### Script Registration

**`GetAI_boss_maleki_the_pallid`**
Factory function returning a new `boss_maleki_the_pallidAI` instance.

**`AddSC_boss_maleki_the_pallid`**
Registers the script with name `"boss_maleki_the_pallid"` and assigns `GetAI_boss_maleki_the_pallid` as the AI getter via `ScriptMgr::RegisterSelf`. Called by `ScriptLoader::AddScripts`.

## Cross-Unit Boundaries

- **`ScriptedAI`**: Base class providing AI framework and `m_creature` access.
- **`WorldObject::GetInstanceData`**: Retrieves instance data in the constructor.
- **`InstanceData::SetData`**: Reports boss death in `JustDied`.
- **`Unit::GetPower` / `Unit::GetMaxPower`**: Checks mana levels in `GetManaPercent` and `UpdateAI`.
- **`Creature::SelectHostileTarget` / `Creature::GetVictim`**: Validates combat targets in `UpdateAI`.
- **`CreatureAI::DoCastSpellIfCan`**: Attempts spell casts in `UpdateAI`.
- **`CreatureAI::DoMeleeAttackIfReady`**: Handles melee attacks in `UpdateAI`.
- **`CreatureAI::SetCombatMovement`**: Toggles movement mode in `UpdateAI`.
- **`Map::GetPlayer`**: Retrieves player objects for aggro restoration in `UpdateAI`.
- **`shared_Util::urand`**: Generates random timer intervals in `UpdateAI`.
- **`ThreatManager::addThreatDirectly` / `getThreat` / `modifyThreatPercent`**: Manages threat for Ice Tomb mechanics in `UpdateAI`.
- **`Unit::GetHealthPercent`**: Checks health for drain activation in `UpdateAI`.
- **`Unit::GetMotionMaster`**: Controls movement (chase/idle) in `UpdateAI`.
- **`Unit::GetThreatManager`**: Accesses threat methods in `UpdateAI`.
- **`Unit::HasAura`**: Checks for Ice Tomb aura in `UpdateAI`.
- **`WorldObject::GetDistance2d`**: Calculates distance for movement logic in `UpdateAI`.
- **`WorldObject::GetMap`**: Accesses map for player lookup in `UpdateAI`.
- **`Script::Script` / `ScriptMgr::RegisterSelf`**: Registers script in `AddSC_boss_maleki_the_pallid`.
- **`ScriptLoader::AddScripts`**: Calls `AddSC_boss_maleki_the_pallid` at startup.

## Data Model

This unit does not interact directly with any database tables.

## Notable Implementation Details

- **Ice Tomb Threat Handling**: The boss removes the target from the threat list by reducing threat by 100% when casting Ice Tomb. It stores the target’s GUID and threat level. Upon aura expiration, it restores the exact threat amount, ensuring the target re-enters the threat list at their prior position.
- **Dynamic Movement**: The boss reduces its idle distance threshold from 40 to 20 yards when Health < 60% or Mana < 50%, encouraging it to close distance for draining spells. If Mana < 10%, it always chases regardless of distance.
- **Drain Spell Selection**: The boss casts `SPELL_DRAIN_MANA` if the victim has mana, otherwise `SPELL_DRAIN_LIFE`, optimizing resource recovery.
- **Null Safety**: `JustDied` checks for a valid `m_pInstance` before calling `SetData`, preventing crashes if the creature is spawned outside a valid instance context.

## Member Reference

**`boss_maleki_the_pallidAI`**: Constructor initializing AI, retrieving instance data, and calling `Reset()`.
**`Reset`**: Method resetting internal timers and state variables.
**`JustDied`**: Method notifying the instance script of boss death.
**`GetManaPercent`**: Helper calculating current mana percentage.
**`UpdateAI`**: Main combat loop managing spells, threat, movement, and melee.
**`GetAI_boss_maleki_the_pallid`**: Factory function creating the AI instance.
**`AddSC_boss_maleki_the_pallid`**: Function registering the script with the server.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_maleki_the_pallid

*Source:* boss_maleki_the_pallid.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_maleki_the_pallidAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | — | — | — |
| JustDied | method | InstanceData/SetData | — | — |
| GetManaPercent | method | Unit.Main/GetMaxPower, Unit.Main/GetPower | — | — |
| UpdateAI | method | Creature.MotionMaster/MoveChase, Creature.MotionMaster/MoveIdle, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, CreatureAI/SetCombatMovement, Map.Main/GetPlayer, Object/GetGUID, ObjectGuid/ObjectGuid#5, shared_Util/urand, ThreatManager/addThreatDirectly, ThreatManager/getThreat, ThreatManager/modifyThreatPercent#2, Unit.Main/GetHealthPercent, Unit.Main/GetMotionMaster, Unit.Main/GetPower, Unit.Main/GetThreatManager, Unit.Main/GetVictim, Unit.Main/HasAura#2, Unit.Main/SelectHostileTarget, WorldObject.Object/GetDistance2d#3, WorldObject.Object/GetMap | — | — |
| GetAI_boss_maleki_the_pallid | function | — | — | — |
| AddSC_boss_maleki_the_pallid | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
