# boss_ossirian

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_ossirian

**Purpose & Responsibilities**
This translation unit implements the encounter logic for **Ossirian the Unscarred**, a boss in the *Ruins of Ahn'Qiraj* instance. It defines three distinct AI behaviors:
1.  **`boss_ossirianAI`**: The primary boss AI, handling combat mechanics including threat manipulation, spell casting, summoning environmental effects (tornadoes), and interacting with the instance script to manage crystal spawns and weather changes.
2.  **`ossirian_crystalAI`**: The AI for the interactive crystals players must use to weaken the boss. It validates usage, triggers the boss's vulnerability, and coordinates with the instance script to spawn new crystals.
3.  **`generic_random_moveAI`**: A utility AI for non-combat creatures that move randomly around the map, occasionally targeting nearby players.

The unit relies heavily on `instance_ruins_of_ahnqiraj` for state management (crystal spawning, encounter status) and uses standard core APIs for threat, movement, and spell casting.

## Member-by-Member Behavior

### Boss Ossirian Mechanics (`boss_ossirianAI`)

**`boss_ossirianAI` (Constructor)**
Initializes the AI by retrieving the instance data via `GetInstanceData`. If the instance data is invalid (null), it immediately marks the creature for removal (`AddObjectToRemoveList`) to prevent crashes. It then calls `Reset()` to initialize timers and states.

**`Reset`**
Resets the boss's state for a new encounter or respawn:
-   Sets `m_bAggro` to false.
-   Casts `SPELL_STRENGTH_OF_OSSIRIAN` (buff) on itself.
-   Resets speed rates to normal (1.0x run/walk).
-   Initializes timers for spells: Curse of Tongues (30s), Strength of Ossirian (25s), War Stomp (25s), Enveloping Winds (20s).
-   Clears temporary threat lists and tornado GUIDs.
-   Sets `m_bIsEnraged` to true (indicating the boss starts with its buff).
-   Despawns any existing tornadoes by iterating through `TornadoGUIDs`, finding the creatures, and adding them to the remove list.
-   Resets weather to fine (`WEATHER_TYPE_FINE`).
-   Updates the instance data to mark the encounter as failed (`FAIL`).

**`SpellHitTarget`**
Triggered when a spell hits a target. Specifically handles `SPELL_ENVELOPING_WINDS`:
-   Records the caster's current threat value and GUID in temporary vectors (`TmpThreatVal`, `TmpThreatList`).
-   Reduces the caster's threat by 100% (`modifyThreatPercent(..., -100)`).
-   *Note*: This effectively removes the player from the threat list temporarily. The `UpdateAI` method later restores this threat if the aura wears off.

**`SpellHit`**
Triggered when the boss is hit by a spell. Checks if the spell ID matches any in `SpellWeakness` (Fire, Frost, Nature, Shadow, Arcane weaknesses):
-   If hit by a weakness spell, sets the `StrengthOfOssirian` timer to 45 seconds (delaying the next buff recast).
-   If the boss is enraged (`m_bIsEnraged`) or has the `SPELL_STRENGTH_OF_OSSIRIAN` aura, it removes the aura and sets `m_bIsEnraged` to false.
-   This mechanic allows players to strip the boss's damage reduction buff by using the correct elemental crystals.

**`Aggro`**
Triggered when the boss enters combat:
-   Plays aggro sound/text.
-   Marks the zone as in combat.
-   Casts `SPELL_STRENGTH_OF_OSSIRIAN`.
-   Summons two tornadoes (`NPC_TORNADO`) at predefined locations (`TornadoSpawn`). These tornadoes cast `SPELL_SANDSTORM` and are set to non-selectable. Their GUIDs are stored in `TornadoGUIDs`.
-   If this is the first aggro (`!m_bAggro`), it sets `m_bAggro` to true, resets the speed timer, and tells the instance to spawn new crystals (`SpawnNewCrystals`).
-   Changes weather to storm (`WEATHER_TYPE_STORM`).
-   Updates instance data to `IN_PROGRESS`.

**`JustDied`**
Triggered upon death:
-   Plays death sound/text.
-   Iterates through `TornadoGUIDs` and forces the despawn of any remaining tornadoes.
-   Updates instance data to `DONE`.

**`KilledUnit`**
Triggered when the boss kills a unit. If the victim is a player, it plays a slay sound/text.

**`UpdateAI`**
The main update loop, executed every tick:
-   **Target Check**: Returns early if no hostile target exists.
-   **Curse of Tongues**: If timer expires, casts on the victim. Timer resets to 10-20 seconds.
-   **Speed Ramp**: If `m_uiSpeed_Timer` is active, it gradually increases the boss's run speed from 1.0x up to 2.0x over 10 seconds. This creates a "rage" effect where the boss gets faster over time.
-   **Strength of Ossirian**: If not enraged and timer expires, recasts the buff and sets `m_bIsEnraged` to true. Timer resets to 25 seconds (or 45 if weakened).
-   **War Stomp**: If timer expires, casts on self. Timer resets to 25-35 seconds.
-   **Enveloping Winds**: If timer expires, casts on the victim. Timer resets to 15 seconds.
-   **Threat Restoration**: Iterates through `TmpThreatList`. For each entry, it checks if the unit is alive and no longer has the `SPELL_ENVELOPING_WINDS` aura. If so, it restores the previously saved threat value. It then removes the entry from the temporary lists.
-   **Melee**: Performs melee attacks if ready.

### Crystal Mechanics (`ossirian_crystalAI`)

**`ossirian_crystalAI` (Constructor)**
Standard initialization inheriting from `GameObjectAI`.

**`OnUse`**
Triggered when a player interacts with the crystal:
-   Retrieves instance data. If null, logs an error and returns false.
-   Checks if a `CRYSTAL_TRIGGER` creature already exists nearby (within 5 yards). If so, the crystal is considered "used" and returns true (preventing reuse).
-   Calls `pInstance->SpawnNewCrystals(me->GetObjectGuid())` to schedule the next set of crystals.
-   Finds the boss (`NPC_OSSIRIAN`) within 300 yards. If not found, logs an error and returns false.
-   Checks if the boss is in combat. If not, returns true (no action needed).
-   Summons a `CRYSTAL_TRIGGER` creature at the crystal's location. This creature casts a random weakness spell from `SpellWeakness` on the boss.
-   Returns false (indicating the object was used/consumed).

### Utility AI (`generic_random_moveAI`)

**`generic_random_moveAI` (Constructor)**
Initializes the AI and calls `Reset()`.

**`Reset`**
Sets the initial timer to 5 seconds and disables combat movement (`SetCombatMovement(false)`), ensuring these creatures do not chase targets.

**`UpdateAI`**
Executes random movement logic:
-   If timer expires:
    -   **Player Targeting (1/3 chance)**: Finds all players within `MAX_VISIBILITY_DISTANCE`. If any exist, picks one randomly and moves towards them. Timer resets to 5-20 seconds.
    -   **Random Point (2/3 chance)**: Picks a random point within 50 yards of the current position and moves there. Timer resets to 3-10 seconds.
    -   *Note*: There is a logical bug here. After executing either branch, the timer is unconditionally set to 2000ms at the end of the block, overriding the randomized timers set inside the branches. This results in a fixed 2-second delay between movements regardless of the intended randomization.

## Cross-Unit Boundaries

### `boss_ossirianAI`
-   **Calls `instance_ruins_of_ahnqiraj::SetData`**: Updates the encounter state (`FAIL`, `IN_PROGRESS`, `DONE`) in the instance script.
-   **Calls `instance_ruins_of_ahnqiraj::SpawnNewCrystals`**: Triggers the spawning of new interactive crystals during aggro.
-   **Calls `Map::GetCreature` / `Map::GetUnit`**: Retrieves creature/unit objects by GUID for tornado management and threat restoration.
-   **Calls `Map::SetWeather`**: Changes the visual weather effect in the zone.
-   **Calls `WorldObject::SummonCreature`**: Spawns tornadoes during aggro.
-   **Calls `ScriptMgr::DoScriptText`**: Plays sound/text events.
-   **Calls `CreatureAI::DoCast` / `DoCastSpellIfCan`**: Handles spell casting.
-   **Calls `ThreatManager` methods**: Manipulates threat values for `Enveloping Winds`.

### `ossirian_crystalAI`
-   **Calls `instance_ruins_of_ahnqiraj::SpawnNewCrystals`**: Ensures the cycle of crystals continues.
-   **Calls `GridSearchers::GetClosestCreatureWithEntry`**: Finds the boss and checks for existing triggers.
-   **Calls `WorldObject::SummonCreature`**: Spawns the trigger creature that applies the weakness.
-   **Calls `SpellCaster::CastSpell`**: Applies the weakness spell to the boss.

### `generic_random_moveAI`
-   **Calls `Map::GetPlayers`**: Retrieves a list of players for potential targeting.
-   **Calls `Unit::MonsterMove`**: Executes movement commands.

## Data Model

This unit does not directly query or modify database tables. It interacts with the `instance_ruins_of_ahnqiraj` script, which manages instance-specific data in memory. The `instance_ruins_of_ahnqiraj` script likely persists data to the `instance` table, but `boss_ossirian.cpp` itself contains no SQL queries or direct table access.

## Notable Implementation Details

1.  **Threat Manipulation for Enveloping Winds**:
    The `SpellHitTarget` method reduces threat by 100% when `Enveloping Winds` is cast. The `UpdateAI` method restores this threat only if the aura is gone. This creates a mechanic where players hit by this spell are removed from the threat list until the spell ends, potentially causing tank swaps or aggro issues if not managed.

2.  **Speed Ramp Bug/Feature**:
    In `UpdateAI`, the speed calculation `(2.0f - m_uiSpeed_Timer*1.0f/10000)` assumes `m_uiSpeed_Timer` counts down from 10000. However, the timer is only reset in `Aggro` and `Reset`. If the boss stays in combat longer than 10 seconds without resetting, the speed will cap at 2.0x and stay there because the condition `m_uiSpeed_Timer >= uiDiff` will eventually fail, stopping the decrement. The speed will not decrease unless the boss resets.

3.  **Generic Random Move Timer Override**:
    As noted in the `UpdateAI` description for `generic_random_moveAI`, the final line `m_uiTimer = 2000;` overrides the randomized timers set in the conditional blocks. This means the creature always waits exactly 2 seconds between movements, ignoring the intended 3-20 second ranges.

4.  **Tornado Management**:
    Tornadoes are summoned with `TEMPSUMMON_MANUAL_DESPAWN`. They are tracked by GUID in `TornadoGUIDs`. On reset or death, the AI iterates through this list to manually despawn them. This ensures they don't linger after the encounter ends.

5.  **Crystal Trigger Logic**:
    The `OnUse` method checks for an existing `CRYSTAL_TRIGGER` creature within 5 yards. This prevents multiple players from using the same crystal simultaneously or in quick succession. The trigger creature is summoned with `TEMPSUMMON_TIMED_DESPAWN` for 8 seconds, allowing it to cast the weakness spell before disappearing.

## Member Reference

**`boss_ossirianAI`**
Constructor for the boss AI. Initializes instance data, checks for validity, and calls `Reset()`.

**`Reset`**
Resets boss state, timers, speed, weather, and despawns tornadoes. Marks encounter as failed.

**`SpellHitTarget`**
Handles `Enveloping Winds` by reducing threat and storing original threat values for later restoration.

**`SpellHit`**
Checks for weakness spells. If hit, delays buff recast and removes the `Strength of Ossirian` aura.

**`Aggro`**
Initiates combat: plays text, summons tornadoes, changes weather, spawns crystals, and updates instance state.

**`JustDied`**
Handles death: plays text, despawns tornadoes, and updates instance state to done.

**`KilledUnit`**
Plays slay text if the victim is a player.

**`UpdateAI`**
Main loop: manages spell timers, speed ramp, threat restoration, and melee attacks.

**`GetAI_boss_ossirian`**
Factory function returning a new `boss_ossirianAI` instance.

**`generic_random_moveAI`**
Constructor for the utility AI. Calls `Reset()`.

**`Reset#2`**
Resets the generic AI timer and disables combat movement.

**`UpdateAI#2`**
Executes random movement logic, with a bug causing fixed 2-second intervals.

**`ossirian_crystalAI`**
Constructor for the crystal AI.

**`OnUse`**
Handles player interaction: validates usage, spawns new crystals, finds boss, and summons a trigger to apply weakness.

**`GetAI_ossirian_crystal`**
Factory function returning a new `ossirian_crystalAI` instance.

**`GetAI_generic_random_move`**
Factory function returning a new `generic_random_moveAI` instance.

**`AddSC_boss_ossirian`**
Registers the scripts for `boss_ossirian` and `ossirian_crystal` with the script manager.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_ossirian

*Source:* boss_ossirian.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_ossirianAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/AddObjectToRemoveList, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | CreatureAI/DoCast, instance_ruins_of_ahnqiraj/SetData, Map.Main/GetCreature, Map.Main/SetWeather, ObjectGuid/ObjectGuid#5, Unit.Main/SetSpeedRate, WorldObject.Object/AddObjectToRemoveList, WorldObject.Object/GetMap, WorldObject.Object/GetZoneId | — | — |
| SpellHitTarget | method | Object/GetObjectGuid, ThreatManager/getThreat, ThreatManager/modifyThreatPercent#2, Unit.Main/GetThreatManager | — | — |
| SpellHit | method | Unit.Main/HasAura#2, Unit.Main/RemoveAurasDueToSpell | — | — |
| Aggro | method | Creature.Main/AI, Creature.Main/SetInCombatWithZone, CreatureAI/AttackStart, CreatureAI/DoCast, instance_ruins_of_ahnqiraj/SetData, instance_ruins_of_ahnqiraj/SpawnNewCrystals, Map.Main/SetWeather, Object/GetGUID, ObjectGuid/ObjectGuid, ScriptMgr/DoScriptText, SpellCaster/CastSpell#2, Unit.Main/GetVictim, WorldObject.Object/GetMap, WorldObject.Object/GetZoneId, WorldObject.Object/SetFlag, WorldObject.Object/SummonCreature#2 | — | — |
| JustDied | method | Creature.Main/ForcedDespawn, instance_ruins_of_ahnqiraj/SetData, Map.Main/GetCreature, ObjectGuid/ObjectGuid#5, ScriptMgr/DoScriptText, WorldObject.Object/GetMap | — | — |
| KilledUnit | method | Object/GetTypeId, ScriptMgr/DoScriptText | — | — |
| UpdateAI | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Map.Main/GetUnit, ThreatManager/addThreat#3, Unit.Main/GetThreatManager, Unit.Main/GetVictim, Unit.Main/HasAura#2, Unit.Main/IsAlive, Unit.Main/SelectHostileTarget, Unit.Main/SetSpeedRate, WorldObject.Object/GetMap | — | — |
| GetAI_boss_ossirian | function | — | — | — |
| generic_random_moveAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#2 | method | CreatureAI/SetCombatMovement | — | — |
| UpdateAI#2 | method | Map.Main/GetPlayers, shared_Util/urand, Unit.Main/MonsterMove, WorldObject.Object/GetDistance2d#3, WorldObject.Object/GetMap, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/GetRandomPoint | — | — |
| ossirian_crystalAI | ctor | GameObjectAI/GameObjectAI | — | — |
| OnUse | method | GridSearchers/GetClosestCreatureWithEntry, instance_ruins_of_ahnqiraj/GetData64, instance_ruins_of_ahnqiraj/SpawnNewCrystals, Log.Main/Out, Object/GetObjectGuid, shared_Util/urand, SpellCaster/CastSpell#2, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, WorldObject.Object/GetInstanceData, WorldObject.Object/GetInstanceId, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SummonCreature#2 | — | — |
| GetAI_ossirian_crystal | function | — | — | — |
| GetAI_generic_random_move | function | — | — | — |
| AddSC_boss_ossirian | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
