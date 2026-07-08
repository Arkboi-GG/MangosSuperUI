# instance_molten_core

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# instance_molten_core

`instance_molten_core` is the instance script for the Molten Core raid dungeon. It manages the state of all boss encounters, tracks the completion of seven specific runes required to summon the final boss sequence, handles creature spawning and despawning logic tied to boss progress, and orchestrates the transition between the rune collection phase and the Majordomo/Ragnaros finale.

The class inherits from `ScriptedInstance`, providing the standard interface for saving/loading instance data, tracking encounter states, and reacting to world events like creature creation or combat entry. It does not interact with any database tables directly; all persistence is handled via the base class's string-based serialization mechanism (`Save`/`Load`).

## Member-by-Member Behavior

### Initialization and State Management

**`instance_molten_core`**
The constructor initializes the instance by calling `Initialize`. It sets up the parent `ScriptedInstance` with the provided `Map` pointer.

**`Initialize`**
Resets all internal state variables to their default values. This includes zeroing out GUIDs for bosses and runes, resetting encounter statuses to `NOT_STARTED`, clearing the rune usage list, and setting the object removal timer to 5 seconds. This ensures a clean slate when a new instance is created or loaded.

**`IsEncounterInProgress`**
Checks if any of the tracked encounters are currently in the `IN_PROGRESS` or `SPECIAL` state. This is used by the engine to determine if the instance is considered "active" for purposes like preventing certain actions or displaying status indicators.

**`SetData`**
The primary entry point for updating instance state from other scripts (e.g., boss AI scripts). It accepts a type identifier and a data value.
- For boss encounters (Sulfuron through Lucifron), it updates the corresponding index in `m_auiEncounter`.
- For Majordomo, it updates the encounter state and, if marked `DONE`, respawns the Firelord Cache game object after one hour.
- For Ragnaros, it updates index 9 of the encounter array.
- For rune states (`DATA_RUNE_ACTIVE_0` through `_6`), it updates the `m_RuneSates` array.
- For `DATA_DOMO_SPAWNED`, it updates the flag indicating whether Majordomo has been summoned.
- Crucially, if the data value is `DONE`, it triggers a save to the database via `SaveToDB` and logs the action.

**`GetData`**
Retrieves the current state of an encounter or rune. It returns the value from `m_auiEncounter` for bosses, `m_RuneSates` for runes, or `m_dataDomoSpawned` for the Majordomo spawn flag. Returns 0 if the type is unrecognized.

**`GetData64`**
Retrieves the GUID of a specific boss creature. It supports Sulfuron, Golemagg, Garr, and Majordomo. Other bosses' GUIDs are stored internally but not exposed via this method in this partial.

**`Save`**
Serializes the instance state into a space-separated string. It writes all 10 encounter states followed by the 7 rune states. This string is returned to the base class for storage.

**`Load`**
Deserializes the instance state from a string. It parses the encounter and rune states. It explicitly resets any encounter marked `IN_PROGRESS` to `NOT_STARTED` to prevent stuck states after server restarts. It then calls `SetData` for each value to ensure side effects (like logging) are triggered correctly, although the save-to-db logic in `SetData` is guarded by the `DONE` check which might not trigger on load if the state was already done. Note: The loop for runes uses a hardcoded offset `DATA_RUNE_ACTIVE_0 + 16` which appears to be a bug or specific constant mapping not immediately obvious from the enum names, likely intended to map to the correct `SetData` case.

### World Event Handlers

**`OnObjectCreate`**
Triggered when a game object spawns in the instance. It captures GUIDs for the seven runes, the Hot Coals portal, and the Firelord Cache.
- For runes, if the corresponding rune state is already `DONE`, it adds the GUID to `m_GOUseGuidList`. This list is used later to automatically "use" the rune visual effect if a player enters the instance after the rune is already collected.
- For the Hot Coals portal, it sets the `GO_FLAG_IN_USE` flag, likely to indicate it's active or locked.

**`OnCreatureCreate`**
Triggered when a creature spawns. It captures GUIDs for major bosses. It also implements several conditional despawn mechanics:
- **Flamewaker Priest:** Despawns if Sulfuron is done.
- **Core Rager:** Despawns if Golemagg is done.
- **Flamewaker:** Despawns if Gehennas is done.
- **Flamewaker Protector:** Despawns if Lucifron is done.
- **Core Hounds:** Despawns if Magmadar is done.
- **Firesworn/Lava Surger:** Despawns if Garr is done.
- **Lava Annihilator/Firelord:** Randomly swaps their entry ID upon spawn to vary mob types, then reinitializes their AI.
- **Lava Spawn:** Checks for existing Lava Spawns within 100 yards. If more than `MAX_LAVA_SPAWNS` exist, the new spawn is forcibly despawned to prevent exponential population growth due to evade bugs.
- **Garr:** Stores its GUID in both `m_uiGarrGUID` and the generic `m_mNpcEntryGuidStore` map.

**`OnCreatureRespawn`**
Similar to `OnCreatureCreate`, this handles creatures respawning naturally. It applies the same conditional despawn logic for adds based on boss completion. It also handles the random entry swapping for Lava Annihilators and Firelords.
- **Majordomo/Ragnaros:** If these bosses respawn (which shouldn't happen normally unless manually triggered or bugged), it marks their encounter as `DONE` and removes them from the world.

**`OnCreatureEnterCombat`**
Implements aggro linking for the Majordomo encounter. If Majordomo, a Flamewaker Healer, or a Flamewaker Elite enters combat, it searches for all such creatures within 150 yards. Any alive, non-combatting creatures found are forced into combat with the same victim. This ensures the entire pack engages together, preventing players from isolating individual mobs.

### Utility and Update Logic

**`RemoveRuneFire`**
A helper function that finds a specific fire animation game object near a rune, deletes it, and "uses" the rune game object. This is used to visually clear the rune effect when a player interacts with it or when the instance loads with a completed rune.

**`Update`**
Called periodically by the engine.
- Manages the `m_ObjectRemoveTimer`. When the timer expires, it calls `RemoveAllObjectsInRemoveList` to actually delete objects marked for removal (via `AddObjectToRemoveList` in other handlers). This batches deletions for performance.
- Iterates through `m_GOUseGuidList`. For each GUID, it retrieves the game object. If valid, it calls `RemoveRuneFire` using the first player in the instance as the user. This automates the visual cleanup of runes that were completed before the current players entered the instance.

### Global Functions

**`UpdateRune`**
A standalone function used by the rune interaction script. It checks if the associated boss is `DONE`. If so, it marks the rune as `DONE` in the instance data and deletes the nearby fire animation game object. It returns true if the rune was updated.

**`GOHello_go_rune_MC`**
The interaction handler for the rune game objects.
1. Retrieves the instance data.
2. Calls `UpdateRune` for the specific rune. If the boss isn't done, it returns early (true, meaning handled).
3. Checks if Ragnaros is done or Majordomo is already spawned. If so, it does nothing further.
4. Checks if all 7 runes are `DONE`. If not, it returns early.
5. If all runes are done, it sets `DATA_DOMO_SPAWNED` to `DONE`.
6. Summons Majordomo:
   - If Majordomo's encounter isn't done, he is summoned at the rune room coordinates with a manual despawn timer. He says a line of dialogue.
   - If Majordomo's encounter is already done (indicating a server crash during the fight), he is summoned in the Ragnaros chamber with specific flags (immune, in combat, pet rename) and gossip enabled to allow resummoning Ragnaros.

**`GetInstance_instance_molten_core`**
Factory function that creates and returns a new `instance_molten_core` object.

**`AddSC_instance_molten_core`**
Registers the instance script and the rune interaction script with the script manager.

## Cross-Unit Boundaries

- **`ScriptedInstance`**: The base class provides the framework for instance management. `instance_molten_core` overrides methods like `Initialize`, `SetData`, `GetData`, `Save`, `Load`, and event handlers. It calls `SaveToDB` and `DoRespawnGameObject` from the base class.
- **`Creature`/`GameObject`/`WorldObject`**: These classes represent entities in the world. The instance script calls methods on them to get GUIDs, set entries, add/remove flags, and delete them. It also uses `FindNearestGameObject` to locate related objects.
- **`Unit`**: Used in `OnCreatureEnterCombat` to get the victim and force other units into combat.
- **`GridSearchers`**: Used to find lists of creatures within a grid area for aggro linking and lava spawn limiting.
- **`shared_Util`**: Uses `urand` for random number generation in mob type swapping.
- **`Log.Main`**: Uses `OUT_SAVE_INST_DATA` and `OUT_LOAD_INST_DATA` macros for logging save/load operations.
- **`Map.Main`**: Uses `GetPlayers`, `GetGameObject`, and `RemoveAllObjectsInRemoveList` to manage instance-wide state.
- **`ScriptMgr`**: Uses `DoScriptText` to play dialogue for Majordomo.
- **`ScriptLoader`**: Calls `AddSC_instance_molten_core` to register the scripts.

## Data Model

This unit does not interact with any database tables directly. All state is persisted via the `Save`/`Load` methods, which serialize data into a string format managed by the `ScriptedInstance` base class. The string contains space-separated integers representing encounter states and rune states.

## Notable Implementation Details

- **Rune Visual Cleanup**: The `Update` method actively cleans up rune fire effects for completed runes when players are present. This is necessary because the visual effect is a separate game object that doesn't automatically disappear when the rune is logically "used".
- **Aggro Linking**: The `OnCreatureEnterCombat` handler ensures that Majordomo's adds engage as a group. This is critical for the encounter design, as isolating adds would trivialize the fight.
- **Lava Spawn Limiting**: The `OnCreatureCreate` handler for `NPC_LAVA_SPAWN` prevents exponential growth by checking for existing spawns and despawning new ones if the limit is exceeded. This is a safeguard against bugs where spawns might trigger repeatedly.
- **Server Crash Recovery**: The `GOHello_go_rune_MC` function handles the case where the server crashes during the Ragnaros encounter. It summons Majordomo in the Ragnaros chamber with special flags to allow players to resummon Ragnaros via gossip.
- **Random Mob Types**: Lava Annihilators and Firelords randomly swap their entry IDs upon spawn/respawn. This adds variety to the trash mobs but requires reinitializing their AI to ensure they behave correctly for their new type.
- **Hardcoded Offset Bug?**: In `Load`, the loop for runes uses `SetData((DATA_RUNE_ACTIVE_0 + 16), i)`. This suggests that the `SetData` cases for runes might be offset by 16 from the `DATA_RUNE_ACTIVE_0` enum value, or that `DATA_RUNE_ACTIVE_0` itself is part of a larger enum block. Without seeing the enum definition, this looks suspicious but is consistent with the code.
- **Majordomo Respawn**: When Majordomo is defeated, the Firelord Cache game object is respawned after one hour. This allows players to potentially restart the rune process if needed, though typically the instance would reset entirely.

## Member Reference

**`instance_molten_core`**
Constructor that initializes the instance by calling `Initialize`.

**`Initialize`**
Resets all internal state variables, including encounter states, GUIDs, and timers, to their default values.

**`IsEncounterInProgress`**
Returns true if any encounter is in `IN_PROGRESS` or `SPECIAL` state.

**`OnObjectCreate`**
Captures GUIDs for runes and other key game objects. Marks runes for automatic visual cleanup if they are already completed.

**`OnCreatureRespawn`**
Handles creature respawns by applying conditional despawn logic for adds based on boss completion and randomizing mob types for Lava Annihilators/Firelords.

**`OnCreatureEnterCombat`**
Links aggro for Majordomo and his adds, forcing nearby allies into combat with the same victim.

**`OnCreatureCreate`**
Captures boss GUIDs, applies conditional despawn logic for adds, randomizes mob types, and limits Lava Spawn population.

**`SetData`**
Updates instance state for encounters and runes. Triggers a database save if the state is set to `DONE`.

**`Save`**
Serializes encounter and rune states into a space-separated string for persistence.

**`GetData`**
Retrieves the current state of an encounter or rune.

**`GetData64`**
Retrieves the GUID of a specific boss creature.

**`RemoveRuneFire`**
Helper function to delete the fire animation game object near a rune and use the rune itself.

**`Update`**
Manages the object removal timer and automatically cleans up visual effects for completed runes when players are present.

**`Load`**
Deserializes instance state from a string, resetting any `IN_PROGRESS` encounters to `NOT_STARTED`.

**`UpdateRune`**
Standalone function to update a rune's state and delete its fire animation if the associated boss is done.

**`GOHello_go_rune_MC`**
Handles player interaction with runes. Checks if all runes are done and summons Majordomo accordingly, handling server crash recovery.

**`GetInstance_instance_molten_core`**
Factory function to create a new `instance_molten_core` object.

**`AddSC_instance_molten_core`**
Registers the instance and rune interaction scripts with the script manager.

---

<!-- machine-true, projected from graph.json -->

## Map — instance_molten_core

*Source:* instance_molten_core.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| instance_molten_core | ctor | ScriptedInstance/ScriptedInstance | — | — |
| Initialize | method | — | — | — |
| IsEncounterInProgress | method | — | — | — |
| OnObjectCreate | method | Object/GetEntry, Object/GetGUID, WorldObject.Object/SetFlag | — | — |
| OnCreatureRespawn | method | Creature.Main/AIM_Initialize, Creature.Main/UpdateEntry, Object/GetEntry, Object/SetEntry, shared_Util/urand, WorldObject.Object/AddObjectToRemoveList | — | — |
| OnCreatureEnterCombat | method | GridSearchers/GetCreatureListWithEntryInGrid#2, Object/GetEntry, Unit.Main/GetVictim, Unit.Main/IsAlive, Unit.Main/IsInCombat, Unit.Main/SetInCombatWith | — | — |
| OnCreatureCreate | method | Creature.Main/AIM_Initialize, Creature.Main/ForcedDespawn, Creature.Main/UpdateEntry, GridSearchers/GetCreatureListWithEntryInGrid#2, Object/GetEntry, Object/GetGUID, Object/GetObjectGuid, Object/SetEntry, shared_Util/urand, WorldObject.Object/AddObjectToRemoveList | — | — |
| SetData | method | InstanceData/SaveToDB, Log.Main/Out, Map.Main/GetId, Map.Main/GetInstanceId, Map.Main/GetMapName, ScriptedInstance/DoRespawnGameObject | — | — |
| Save | method | — | — | — |
| GetData | method | — | — | — |
| GetData64 | method | — | — | — |
| RemoveRuneFire | method | GameObject/Delete, GameObject/Use, WorldObject.Object/FindNearestGameObject | — | — |
| Update | method | Map.Main/GetGameObject, Map.Main/GetPlayers, Map.Main/RemoveAllObjectsInRemoveList, MapRefManager/getFirst#2, Object/GetEntry, ObjectGuid/ObjectGuid#5 | — | — |
| Load | method | Log.Main/Out, Map.Main/GetId, Map.Main/GetInstanceId, Map.Main/GetMapName | — | — |
| UpdateRune | function | GameObject/Delete, InstanceData/GetData, InstanceData/SetData, WorldObject.Object/FindNearestGameObject | — | — |
| GOHello_go_rune_MC | function | InstanceData/GetData, InstanceData/SetData, Object/GetEntry, ScriptMgr/DoScriptText, Unit.Main/CombatStop, Unit.Main/SetFactionTemplateId, WorldObject.Object/GetInstanceData, WorldObject.Object/SetUInt32Value, WorldObject.Object/SummonCreature#2 | — | — |
| GetInstance_instance_molten_core | function | — | — | — |
| AddSC_instance_molten_core | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
