# instance_uldaman

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# instance_uldaman

**Purpose & Responsibilities**

`instance_uldaman` is the `ScriptedInstance` implementation for the Uldaman dungeon in World of Warcraft. It acts as the central state machine and coordinator for the instance's specific mechanics, managing encounter progress, creature states (awakening/freezing), and game object interactions (doors, altars).

Its primary responsibilities include:
1.  **Encounter Tracking:** Maintaining the completion status (`NOT_STARTED`, `IN_PROGRESS`, `DONE`, `FAIL`) for three main encounters: The Stone Keepers, Ironaya (via the Keystone mechanic), and Archaedas.
2.  **Minion Management:** Handling the lifecycle of "minion" creatures (Stone Keepers, Vault Warders, Earthen Guardians, etc.) by freezing them upon instance reset/load and awakening them during specific encounter phases.
3.  **State Persistence:** Saving and loading encounter data to the database to ensure state consistency across server restarts or instance resets.
4.  **Mechanic Coordination:** Implementing specific logic for the Stone Keeper altar puzzle (waking one keeper at a time) and the Archaedas fight (awakening wall minions, despawning furniture/minions on victory).

This unit does not handle general mob AI or player combat logic; it strictly manages the *instance-level* consequences of those events.

## Member-by-Member Behavior

### Initialization and State Management

**`instance_uldaman` (Constructor)**
Initializes the instance script by calling `Initialize()`. It inherits from `ScriptedInstance`, linking itself to the provided `Map*`.

**`Initialize`**
Resets all internal state variables to their default "fresh instance" values:
-   Sets all encounter statuses in `m_auiEncounter` to 0 (`NOT_STARTED`).
-   Clears all GUID references for bosses (Archaedas, Ironaya) and key objects (Altars, Doors).
-   Resets the `uiIronayaSealDoorTimer` to 27,000 ms (27 seconds).
-   Sets `bKeystoneCheck` to `false`.
-   Pre-allocates memory for vectors tracking minion GUIDs (`vVaultWarder`, `vEarthenGuardian`, etc.).

**`IsEncounterInProgress`**
Iterates through the `m_auiEncounter` array. Returns `true` if any encounter is marked as `IN_PROGRESS`. This is used by the engine to prevent certain actions (like resetting the instance) while a boss fight is active.

**`GetData` / `GetData64`**
Read-only accessors for external scripts (e.g., boss AI scripts) to query instance state.
-   `GetData`: Returns the integer status (`NOT_STARTED`, `DONE`, etc.) for the three main encounters.
-   `GetData64`: Returns specific GUIDs requested by index. For example, index `0` returns the GUID of the player who woke Ironaya, indices `1-2` return Vault Warder GUIDs, and indices `5-10` return Earthen Guardian GUIDs. This allows boss AIs to target specific minions or objects without hardcoding GUIDs.

**`SetData` / `SetData64`**
The primary interface for updating instance state from other scripts.
-   `SetData`: Handles complex logic based on `uiType` (encounter ID) and `uiData` (new status).
    -   **Stone Keepers (`ULDAMAN_ENCOUNTER_STONE_KEEPERS`):**
        -   `DONE`: Opens the Altar of the Keeper door.
        -   `IN_PROGRESS`: Implements the "wake one keeper" mechanic. It scans `vStoneKeeper` for a frozen (immune) keeper. If found, it unfreezes it, sets its faction to hostile (470), and commands it to attack the nearest player. If no frozen keeper is found, it marks the encounter as `FAIL`. If all keepers are dead/unfrozen, it marks `DONE`.
        -   `FAIL`: Respawns all Stone Keepers and re-freezes them.
    -   **Archaedas (`ULDAMAN_ENCOUNTER_ARCHAEDAS`):**
        -   `IN_PROGRESS`: If starting the fight, it unfreezes Archaedas and casts `SPELL_ARCHAEDAS_AWAKEN`. If already in progress, it finds a frozen wall minion, casts `SPELL_AWAKEN_EARTHEN_DWARF` on it, and removes its immunity flags.
        -   `NOT_STARTED`: Respawns all Archaedas-related minions (Wall Minions, Vault Warders, Earthen Guardians) and furniture. Re-freezes Archaedas.
        -   `FAIL`: Resets the Ancient Door state.
        -   `DONE`: Despawns all Archaedas minions. Opens the Ancient Vault Door. Summons the `GO_ANCIENT_TREASURE` game object at specific coordinates.
    -   **Ironaya Door (`ULDAMAN_ENCOUNTER_IRONAYA_DOOR`):** Marks the encounter as `DONE` and enables the keystone check timer.
    -   **Ancient Door (`DATA_ANCIENT_DOOR`):** Controls the state of Archaedas' entrance and the vault door based on fight progress/failure/completion.
    -   **Altars (`DATA_KEEPERS_ALTAR`, `DATA_ARCHAEDAS_ALTAR`):** Toggles the visual state (`GO_STATE_ACTIVE` vs `GO_STATE_READY`) of the respective altar objects.
    -   **Persistence:** If any encounter is set to `DONE`, it serializes the `m_auiEncounter` array into `strInstData` and calls `SaveToDB()` (from `ScriptedInstance`).

-   `SetData64`: Handles 64-bit data transfers.
    -   Type `0`: Stores the GUID of the player who interacted with the Keystone (`uiWhoWokeIronayaGUID`).
    -   Type `1`: Unfreezes a specific creature by GUID.
    -   Type `2`: Freezes a specific creature by GUID.

**`Save` / `Load`**
Handles persistence of the encounter state string.
-   `Save`: Returns the `strInstData` string containing space-separated encounter statuses.
-   `Load`: Parses the input string back into `m_auiEncounter`. Any encounter not explicitly marked `DONE` is forced to `NOT_STARTED` to ensure a clean state for non-completed fights.

**`Update`**
Called periodically by the game loop.
-   Checks `bKeystoneCheck`. If true, it decrements `uiIronayaSealDoorTimer`.
-   When the timer expires, it unfreezes Ironaya, sets her faction template to 415 (hostile), opens the Ironaya Seal Door, and disables further checks. This implements the delayed awakening of Ironaya after the keystone is used.

### Creature and Object Lifecycle

**`OnCreatureCreate`**
Triggered when a creature spawns in the instance. It categorizes creatures by entry ID:
-   **Stone Keepers:** Added to `vStoneKeeper`. If the encounter is not done, they are immediately respawns/frozen via `RespawnMinion`.
-   **Archaedas Minions (Custodians, Hallshapers, Guardians):** Added to `vArchaedasWallMinions` or `vEarthenGuardian`. Frozen if Archaedas encounter is not done.
-   **Ironaya:** GUID stored. Frozen if her door encounter is not done.
-   **Vault Warders:** Distinguished by location. Those near coordinates (104, 272) are "inside" and added to `vVaultWarder`; others are "furniture" added to `vVaultWarderFurniture`. Both groups are managed for respawning/freezing.
-   **Archaedas:** GUID stored.

**`OnObjectCreate`**
Triggered when a game object spawns. It stores GUIDs for key objects:
-   **Altars:** `uiAltarOfArchaedas`, `uiAltarOfTheKeeper`.
-   **Doors:** `uiAltarOfTheKeeperTempleDoor`, `uiArchaedasTempleDoor`, `uiAncientVaultDoor`, `uiIronayaSealDoor`.
-   **Keystone:** `uiKeystoneGUID`.
-   It applies initial states: Doors are opened if their associated encounter is `DONE`. The Ancient Vault Door is set to `GO_STATE_READY` with specific flags. The Keystone has interaction conditions removed if the Ironaya door is done.

### Helper Methods

**`SetFrozenState` / `SetUnFrozenState`**
Utility methods to toggle a creature's "frozen" status.
-   **Frozen:** Adds immunity flags (`UNIT_FLAG_IMMUNE_TO_PLAYER`, `UNIT_FLAG_IMMUNE_TO_NPC`, `UNIT_FLAG_NOT_SELECTABLE`), removes all auras, and applies `SPELL_STONED`.
-   **Unfrozen:** Removes immunity flags and removes `SPELL_STONED`.

**`RespawnMinion` / `DespawnMinion`**
Utility methods for managing minion lifecycles.
-   **RespawnMinion:** Ensures a creature is alive and frozen. If it's alive but not frozen, it kills it, removes the corpse, respawns it, and then freezes it. This ensures minions are always in a "ready to wake" state.
-   **DespawnMinion:** Kills a creature and removes its corpse. Used to clean up minions after Archaedas is defeated.

### Registration

**`GetInstanceData_instance_uldaman`**
Factory function that creates and returns a new `instance_uldaman` object for a given `Map*`.

**`AddSC_instance_uldaman`**
Registers the script with the `ScriptMgr`. It creates a `Script` object, assigns the name `"instance_uldaman"`, links the `GetInstanceData` factory function, and registers it. This function is called by `ScriptLoader/AddScripts` during server startup.

## Cross-Unit Boundaries

*   **Calls `ScriptedInstance` methods:**
    *   `SaveToDB()`: Called in `SetData` to persist encounter data.
    *   `DoOpenDoor()`, `DoResetDoor()`: Called in `SetData` and `Update` to control door animations/states.
    *   Inherits `instance` pointer and map context from `ScriptedInstance`.

*   **Calls `Object` / `WorldObject` methods:**
    *   `GetEntry()`, `GetGUID()`: Used in `OnCreatureCreate` and `OnObjectCreate` to identify entities.
    *   `IsWithinDist2d()`: Used in `OnCreatureCreate` to distinguish Vault Warders by location.
    *   `SetFlag()`, `RemoveFlag()`: Used extensively in `SetFrozenState`, `SetUnFrozenState`, `RespawnMinion`, and `SetData` to manage immunity and interaction flags.
    *   `SetUInt32Value()`: Used in `OnObjectCreate` to set game object flags.

*   **Calls `Creature` / `Unit` methods:**
    *   `GetCreature()`: Called from `Map.Main` (via `instance->GetCreature` or `GetMap()->GetCreature`) to retrieve creature pointers from GUIDs.
    *   `IsAlive()`, `IsDead()`, `IsDespawned()`: Used to check creature states before manipulating them.
    *   `Respawn()`, `RemoveCorpse()`, `SetDeathState()`: Used in `RespawnMinion`, `DespawnMinion`, and `SetData` to manage creature lifecycles.
    *   `CastSpell()`: Used in `SetFrozenState` (`SPELL_STONED`), `SetData` (`SPELL_ARCHAEDAS_AWAKEN`, `SPELL_AWAKEN_EARTHEN_DWARF`).
    *   `HasAura()`, `RemoveAllAuras()`, `RemoveAurasDueToSpell()`: Used in `SetFrozenState` and `SetUnFrozenState` to manage spell effects.
    *   `SetFactionTemporary()`, `SetFactionTemplateId()`: Used in `SetData` and `Update` to change creature hostility.
    *   `AI()->AttackStart()`: Called in `SetData` (Stone Keepers) to initiate combat.
    *   `SelectNearestTarget()`: Called in `SetData` to find a target for the awakened Stone Keeper.

*   **Calls `GameObject` methods:**
    *   `GetGameObject()`: Called from `Map.Main` to retrieve GO pointers.
    *   `SetGoState()`: Used in `OnObjectCreate` and `SetData` to change visual state.
    *   `UseDoorOrButton()`: Used in `OnObjectCreate` to open doors if encounters are complete.

*   **Calls `Map` methods:**
    *   `GetCreature()`, `GetGameObject()`: Used to fetch entities by GUID.
    *   `SummonGameObject()`: Used in `SetData` (Archaedas DONE) to spawn the treasure.
    *   `GetId()`, `GetInstanceId()`, `GetMapName()`: Used in `Load` for logging.

*   **Called by `ScriptLoader/AddScripts`:**
    *   `AddSC_instance_uldaman` is registered to be called during script initialization.

## Data Model

This unit does not directly query or modify database tables via SQL. It relies on the `ScriptedInstance` base class to handle persistence. The `Save()` method returns a string representation of the encounter states, which is stored in the `instance` table's `data` column (managed by the core engine). The `Load()` method reads this string. No custom tables are used.

## Notable Implementation Details

1.  **Stone Keeper Logic:** The `IN_PROGRESS` case for `ULDAMAN_ENCOUNTER_STONE_KEEPERS` contains a specific algorithm to wake *one* keeper. It iterates through `vStoneKeeper` to find a creature that is alive AND has immunity flags (frozen). If multiple are frozen, it picks the first one. If none are frozen, it assumes the encounter is effectively over or failed. If the awakened keeper cannot find a target within 80 yards, the encounter fails.
2.  **Archaedas Minion Awakening:** During `ULDAMAN_ENCOUNTER_ARCHAEDAS` `IN_PROGRESS`, the script iterates through `vArchaedasWallMinions` to find a frozen minion to awaken. It uses `break` after finding the first valid target, ensuring only one minion is awakened per trigger event.
3.  **Vault Warder Distinction:** `OnCreatureCreate` uses `IsWithinDist2d(104, 272, 35.0f)` to separate Vault Warders into "inside" (fight participants) and "furniture" (decorative). This distinction is crucial because "furniture" warders are respawned but not necessarily engaged in the same way, and their GUIDs are stored separately.
4.  **Ironaya Delay:** The `Update` method implements a 27-second delay (`uiIronayaSealDoorTimer`) between the keystone being used (`bKeystoneCheck = true`) and Ironaya actually waking up. This timer is decremented every update tick.
5.  **State Resetting:** In `Load`, any encounter state that is not `DONE` is forcibly set to `NOT_STARTED`. This prevents partial states from persisting incorrectly across server restarts.
6.  **Flag Management:** The code heavily relies on `UNIT_FLAG_IMMUNE_TO_PLAYER` and `UNIT_FLAG_IMMUNE_TO_NPC` to simulate "frozen" states. Creatures are not truly dead or despawned; they are just unselectable and immune to damage/interaction until awakened.

## Member Reference

**`instance_uldaman`**: Constructor that initializes the instance script by calling `Initialize()`.

**`Initialize`**: Resets all internal state variables, GUIDs, timers, and vectors to their default values for a fresh instance.

**`IsEncounterInProgress`**: Returns `true` if any encounter in `m_auiEncounter` is marked as `IN_PROGRESS`.

**`OnCreatureCreate`**: Categorizes spawning creatures by entry ID, stores their GUIDs in appropriate vectors, and freezes them if their associated encounter is not complete.

**`OnObjectCreate`**: Stores GUIDs for key game objects (altars, doors, keystone) and applies initial states (opening doors if encounters are done).

**`SetFrozenState`**: Applies immunity flags, removes auras, and casts `SPELL_STONED` on a creature to simulate freezing.

**`SetUnFrozenState`**: Removes immunity flags and `SPELL_STONED` aura from a creature to awaken it.

**`RespawnMinion`**: Ensures a creature is alive and frozen by killing, removing corpse, respawning, and applying freeze flags if necessary.

**`DespawnMinion`**: Kills a creature and removes its corpse.

**`SetData64`**: Handles 64-bit data updates: storing the keystone user's GUID, or freezing/unfreezing a specific creature by GUID.

**`GetData64`**: Returns specific GUIDs (bosses, minions, furniture) based on an integer index.

**`SetData`**: Core logic handler for encounter state changes. Manages Stone Keeper awakening, Archaedas minion management, door states, altar visuals, and persists `DONE` states to the database.

**`Save`**: Returns the serialized encounter state string for persistence.

**`Load`**: Parses the saved encounter state string and restores `m_auiEncounter`, forcing non-DONE states to `NOT_STARTED`.

**`GetData`**: Returns the integer status (`NOT_STARTED`, `DONE`, etc.) for the three main encounters.

**`Update`**: Manages the 27-second timer for Ironaya's awakening after the keystone is used.

**`GetInstanceData_instance_uldaman`**: Factory function that creates a new `instance_uldaman` object for a given map.

**`AddSC_instance_uldaman`**: Registers the `instance_uldaman` script with the `ScriptMgr` during server startup.

---

<!-- machine-true, projected from graph.json -->

## Map — instance_uldaman

*Source:* instance_uldaman.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| instance_uldaman | ctor | ScriptedInstance/ScriptedInstance | — | — |
| Initialize | method | — | — | — |
| IsEncounterInProgress | method | — | — | — |
| OnCreatureCreate | method | Object/GetEntry, Object/GetGUID, WorldObject.Object/IsWithinDist2d | — | — |
| OnObjectCreate | method | GameObject/SetGoState, GameObject/UseDoorOrButton, Object/GetEntry, Object/GetGUID, WorldObject.Object/SetUInt32Value | — | — |
| SetFrozenState | method | SpellCaster/CastSpell#2, Unit.Main/HasAura#2, Unit.Main/RemoveAllAuras, WorldObject.Object/SetFlag | — | — |
| SetUnFrozenState | method | Unit.Main/HasAura#2, Unit.Main/RemoveAurasDueToSpell, WorldObject.Object/RemoveFlag | — | — |
| RespawnMinion | method | Creature.Main/RemoveCorpse, Creature.Main/Respawn, Creature.Main/SetDeathState, Map.Main/GetCreature, Object/HasFlag, ObjectGuid/ObjectGuid#5, Unit.Main/IsAlive, WorldObject.Object/SetFlag | — | — |
| DespawnMinion | method | Creature.Main/RemoveCorpse, Creature.Main/SetDeathState, Map.Main/GetCreature, ObjectGuid/ObjectGuid#5, Unit.Main/IsDead | — | — |
| SetData64 | method | Map.Main/GetCreature, ObjectGuid/ObjectGuid#5, ZoneScript/GetMap#2 | — | — |
| GetData64 | method | — | — | — |
| SetData | method | Creature.Main/AI, Creature.Main/IsDespawned, Creature.Main/RemoveCorpse, Creature.Main/Respawn, Creature.Main/SetDeathState, Creature.Main/SetFactionTemporary, CreatureAI/AttackStart, GameObject/SetGoState, InstanceData/SaveToDB, Log.Main/Out, Map.Main/GetCreature, Map.Main/GetGameObject, Map.Main/GetId, Map.Main/GetInstanceId, Map.Main/GetMapName, Map.Main/SummonGameObject, Object/HasFlag, ObjectGuid/ObjectGuid#5, ScriptedInstance/DoOpenDoor, ScriptedInstance/DoResetDoor, SpellCaster/CastSpell#2, Unit.Main/IsAlive, Unit.Main/IsDead, Unit.Main/SelectNearestTarget, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag, ZoneScript/GetGameObject | — | — |
| Save | method | — | — | — |
| Load | method | Log.Main/Out, Map.Main/GetId, Map.Main/GetInstanceId, Map.Main/GetMapName | — | — |
| GetData | method | — | — | — |
| Update | method | Map.Main/GetCreature, ObjectGuid/ObjectGuid#5, ScriptedInstance/DoOpenDoor, Unit.Main/SetFactionTemplateId | — | — |
| GetInstanceData_instance_uldaman | function | — | — | — |
| AddSC_instance_uldaman | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
