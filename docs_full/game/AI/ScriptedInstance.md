# ScriptedInstance

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ScriptedInstance

**Purpose & Responsibilities**

`ScriptedInstance` is the foundational base class for scripted dungeon and raid instances in the `wowvmangos` server. It extends `InstanceData` to provide a standardized API for common instance management tasks, abstracting away low-level object lookups and state updates. Its primary responsibilities include:

1.  **GameObject Manipulation:** Providing safe, type-checked wrappers to open, close, reset, and respawn doors, buttons, and other interactive objects (`DoUseDoorOrButton`, `DoOpenDoor`, `DoResetDoor`, `DoRespawnGameObject`).
2.  **Entity Lookup:** Offering efficient retrieval of specific Creatures and GameObjects by their entry ID, relying on pre-populated storage maps (`GetSingleCreatureFromStorage`, `GetSingleGameObjectFromStorage`).
3.  **Instance State Communication:** Broadcasting world state updates to all players in the instance (`DoUpdateWorldState`) and retrieving player pointers for targeting or logic checks (`GetPlayerInMap`).
4.  **Persistence Helpers:** Serializing and deserializing encounter states (boss kills, event progress) into/from string formats for database storage (`GenSaveData`, `LoadSaveData`).
5.  **Dialogue Management:** Hosting the `DialogueHelper` class, which manages timed, sequential NPC dialogues, supporting both single-speaker and two-sided conversation flows.

Additionally, the derived class `ScriptedInstance_PTR` provides specialized logic for the Public Test Realm (PTR) environment, specifically handling the despawning of world bosses after a timeout period.

## Member-by-Member Behavior

### Instance Object Manipulation

These methods interact with `GameObject` entities within the instance map. They all begin by retrieving the object via `Map.Main/GetGameObject` using a provided GUID.

*   **DoUseDoorOrButton**: Toggles the state of a door or button. If the object is `GO_READY`, it activates it (`UseDoorOrButton`), optionally setting a restore time. If it is already `GO_ACTIVATED`, it resets it (`ResetDoorOrButton`). It validates that the object type is indeed a door or button; otherwise, it logs an error via `Log.Main/Out`. This is the most versatile door handler, used extensively across dungeons like Blackrock Depths, Gnomeregan, and Stratholme.
*   **DoOpenDoor**: Specifically opens a door or button. It checks if the state is `GO_READY` and calls `UseDoorOrButton` with a 0 restore time (immediate/permanent open). Like `DoUseDoorOrButton`, it enforces type checking and logs errors for invalid types.
*   **DoResetDoor**: Resets a door or button to its initial state. It verifies the type and calls `ResetDoorOrButton`. Used for resetting gates or doors upon aggro or reset events (e.g., Twin Emperors, Uldaman).
*   **DoRespawnGameObject**: Respawn a GameObject that is not currently spawned. It explicitly excludes fishing nodes, doors, buttons, and traps from this logic. If the object is already spawned, it returns early. Otherwise, it sets the respawn time and calls `Refresh` on the object. This is used for respawning statues, chests, or other interactive items after they have been used or despawned.

### Entity Lookup & Storage

These methods rely on internal maps (`m_mGoEntryGuidStore` and `m_mNpcEntryGuidStore`) that are populated by instance scripts during initialization (typically in `Load` or `SetData` methods of derived classes).

*   **GetSingleGameObjectFromStorage**: Looks up a `GameObject` by its entry ID. It searches `m_mGoEntryGuidStore` for the GUID associated with the entry, then retrieves the object from the map via `Map.Main/GetGameObject`. If not found, it logs an error indicating the object was either not created or not stored.
*   **GetSingleCreatureFromStorage**: Looks up a `Creature` by its entry ID. It searches `m_mNpcEntryGuidStore` for the GUID, then retrieves the creature via `Map.Main/GetCreature`. An optional `bSkipDebugLog` parameter suppresses the error log if the creature is not found. This is heavily used by boss AI scripts to locate targets, triggers, or other bosses.

### Instance State & Players

*   **DoUpdateWorldState**: Sends a world state update packet to all players currently in the instance. It iterates over `Map.Main/GetPlayers`. If no players are present, it logs a debug message. This is used for updating UI elements like timers, health bars, or event indicators.
*   **GetPlayerInMap**: Retrieves a pointer to a `Player` in the instance. It iterates through `Map.Main/GetPlayers` and applies filters: `bOnlyAlive` (checks `Unit.Main/IsAlive`) and `bCanBeGamemaster` (checks `Player.Main/IsGameMaster`). It returns the first matching player or `nullptr`. This is useful for targeting random players for whispers, buffs, or mechanics (e.g., C'Thun, Naxxramas).

### Persistence

*   **GenSaveData**: Serializes an array of encounter states (`uint32* encounters`) into a space-separated string. It iterates from index 0 to `maxIndex`, appending each value to an `ostringstream`. It logs the resulting string for debugging. Used by Zul'Gurub to save instance progress.
*   **LoadSaveData**: Deserializes a space-separated string back into an array of encounter states. It uses an `istringstream` to parse the values. It logs the input string and each parsed value for debugging. Used by Zul'Gurub to restore instance progress.

### Dialogue System

The `DialogueHelper` class (defined in the same header) manages timed NPC dialogues.

*   **DialogueHelper (ctor)**: Initializes the helper with a pointer to a static array of dialogue entries (`SIDialogueEntry` or `SIDialogueEntryTwoSide`). It sets internal pointers and timers to zero.
*   **StartNextDialogueText**: Starts a dialogue sequence from a specific text entry. It scans the dialogue array for the matching `iTextEntry`. If found, it sets the current entry pointer and calls `DoNextDialogueStep`. If not found, it logs an error.
*   **DoNextDialogueStep**: The core logic for executing a dialogue step. It checks if the current entry is valid (non-zero text entry). It determines the speaker entry and text entry, handling two-sided dialogues by selecting the appropriate side based on `m_bIsFirstSide`. It attempts to find the speaker creature via `GetSpeakerByEntry` (virtual) or `ScriptedInstance/GetSingleCreatureFromStorage`. If a speaker is found, it calls `ScriptMgr/DoScriptText` to broadcast the speech. Finally, it calls the virtual `JustDidDialogueStep` hook and increments the entry pointer.
*   **DialogueUpdate**: Called periodically (e.g., from `Update` methods). It decrements the timer (`m_uiTimer`) by the elapsed time (`uiDiff`). If the timer expires, it calls `DoNextDialogueStep` to proceed to the next line.

### PTR Specific Logic (`ScriptedInstance_PTR`)

*   **OnCreatureEnterCombat**: Overrides the base method to handle world bosses. If the creature is a world boss (`Creature.Main/IsWorldBoss`), it records the current time in `boss_expirations` and makes the creature announce a 30-minute despawn timer. It then calls the base `ScriptedInstance::OnCreatureEnterCombat`.
*   **Update**: Overrides the base update loop. It calculates an expiration threshold (current time minus 30 minutes). It iterates through `boss_expirations`, identifying bosses that have exceeded the timeout. For expired bosses, it retrieves the creature, kills it if alive (`Unit.Main/DoKillUnit`), saves its respawn time (`Creature.Main/SaveRespawnTime`), and adds it to the removal list (`WorldObject.Object/AddObjectToRemoveList`). It then calls the base `ScriptedInstance::Update`.

## Cross-Unit Boundaries

`ScriptedInstance` acts as a central hub for instance scripts, calling into core engine components and being called by numerous specific instance implementations.

**Calls Out:**
*   **GameObject**: `GetGoType`, `getLootState`, `ResetDoorOrButton`, `UseDoorOrButton`, `isSpawned`, `Refresh`, `SetRespawnTime`. Used for manipulating doors, buttons, and other interactive objects.
*   **Map.Main**: `GetGameObject`, `GetCreature`, `GetPlayers`, `GetId`. Used for retrieving entities and player lists from the map.
*   **Object**: `GetEntry`. Used for logging error messages when object types are unexpected.
*   **Log.Main**: `Out`. Used for debugging and error reporting.
*   **Player.Main**: `SendUpdateWorldState`, `IsGameMaster`. Used for UI updates and player filtering.
*   **Unit.Main**: `IsAlive`, `DoKillUnit`. Used for player filtering and killing expired world bosses.
*   **Creature.Main**: `IsWorldBoss`, `SaveRespawnTime`, `MonsterSay`. Used for world boss logic.
*   **WorldObject.Object**: `AddObjectToRemoveList`. Used for despawning expired world bosses.
*   **ObjectGuid**: `ObjectGuid#5`. Used for constructing GUIDs.
*   **LinkedListHead**: `isEmpty`. Used to check if the player list is empty.
*   **ScriptMgr**: `DoScriptText`. Used by `DialogueHelper` to broadcast NPC speech.
*   **ZoneScript**: `OnCreatureEnterCombat`, `GetMap#2`. Used for chaining combat events and map access.

**Called By:**
*   **Numerous Instance Scripts**: `instance_blackrock_depths`, `instance_gnomeregan`, `instance_naxxramas`, `instance_stratholme`, etc. These scripts inherit from `ScriptedInstance` and use its methods to manage doors, creatures, and state.
*   **Boss AI Scripts**: `boss_cannon_master_willey`, `boss_twinemperors`, `boss_cthun`, etc. These scripts often call `GetSingleCreatureFromStorage` or `GetSingleGameObjectFromStorage` to interact with instance-specific entities.
*   **Waypoint/Escort Scripts**: `blackrock_depths/WaypointReached`, `gnomeregan/UpdateEscortAI`. These scripts use `DoUseDoorOrButton` to trigger events along paths.

## Data Model

This unit does not directly query or modify database tables. It provides helper functions (`GenSaveData`, `LoadSaveData`) that serialize/deserialize data into strings, which are presumably stored in a database table by higher-level persistence logic (likely in `InstanceData` or the map manager). No SQL queries are present in this source file.

## Notable Implementation Details

1.  **Storage Maps**: `GetSingleCreatureFromStorage` and `GetSingleGameObjectFromStorage` rely on `m_mNpcEntryGuidStore` and `m_mGoEntryGuidStore`. These maps must be populated by the derived instance script (usually in `Load` or `SetData`) by calling `m_mNpcEntryGuidStore[entry] = creature->GetObjectGuid()`. If this step is missed, lookups will fail and log errors.
2.  **DialogueHelper Two-Sided Logic**: `DialogueHelper` supports two-sided dialogues (e.g., Kel'Thuzad vs. Arthas). The `SIDialogueEntryTwoSide` struct contains alternate text and speaker entries. `DoNextDialogueStep` selects the appropriate side based on `m_bIsFirstSide`. If the alternate fields are zero, it falls back to the primary fields.
3.  **PTR World Boss Despawn**: `ScriptedInstance_PTR` implements a simple timeout mechanism for world bosses. It records the combat start time and despawns the boss after 30 minutes. This is a PTR-specific feature and may not reflect retail behavior accurately.
4.  **Error Logging**: Many methods log errors if objects are not found or have unexpected types. This is crucial for debugging instance scripts but can clutter logs if not handled carefully.
5.  **No Thread Safety**: The methods assume single-threaded access to the instance data, consistent with the server's general design. Concurrent modifications to storage maps or dialogue state are not guarded.

## Member Reference

**DoUseDoorOrButton**: Toggles a door/button state. Checks type, then activates if ready or resets if activated. Logs errors for invalid types.

**DoRespawnGameObject**: Respawns a non-spawned GameObject (excluding doors/buttons/traps/fishing). Sets respawn time and refreshes.

**ScriptedInstance**: Constructor. Initializes the instance data.

**~ScriptedInstance**: Destructor. Cleans up instance data.

**DoOpenDoor**: Opens a door/button. Checks type, then activates with 0 restore time if ready. Logs errors for invalid types.

**DoOrSimulateScriptTextForThisInstance**: Wrapper to simulate map-wide text. Uses `GetSingleCreatureFromStorage` to find the speaker.

**DoResetDoor**: Resets a door/button. Checks type, then resets. Logs errors for invalid types.

**DoUpdateWorldState**: Sends world state update to all players in the instance. Logs if no players are present.

**GenSaveData**: Serializes encounter states into a space-separated string. Logs the result.

**LoadSaveData**: Deserializes a space-separated string into encounter states. Logs the input and parsed values.

**GetPlayerInMap**: Retrieves a player from the instance, filtering by alive status and GM status.

**GetSingleGameObjectFromStorage**: Looks up a GameObject by entry ID using the internal storage map. Logs errors if not found.

**GetSingleCreatureFromStorage**: Looks up a Creature by entry ID using the internal storage map. Logs errors if not found (unless suppressed).

**OnCreatureEnterCombat**: (ScriptedInstance_PTR) Handles world boss combat start. Records expiration time and announces timer. Calls base method.

**Update**: (ScriptedInstance_PTR) Checks for expired world bosses. Kills, saves respawn time, and removes them. Calls base method.

**DialogueHelper**: (ctor) Initializes dialogue helper with a single-sided dialogue array.

**DialogueHelper#2**: (ctor) Initializes dialogue helper with a two-sided dialogue array.

**StartNextDialogueText**: Starts a dialogue sequence from a specific text entry. Finds the entry and calls `DoNextDialogueStep`.

**DoNextDialogueStep**: Executes the current dialogue step. Finds speaker, broadcasts text, calls hook, and increments entry.

**DialogueUpdate**: Updates dialogue timer. Calls `DoNextDialogueStep` if timer expires.

---

<!-- machine-true, projected from graph.json -->

## Map — ScriptedInstance

*Source:* ScriptedInstance.cpp, ScriptedInstance.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| DoUseDoorOrButton | method | GameObject/GetGoType, GameObject/getLootState, GameObject/ResetDoorOrButton, GameObject/UseDoorOrButton, Log.Main/Out, Map.Main/GetGameObject, Object/GetEntry, ObjectGuid/ObjectGuid#5 | blackrock_depths/WaypointReached#4, boss_cannon_master_willey/ToggleGate, gnomeregan/JustDied, gnomeregan/UpdateEscortAI, gnomeregan/WaypointStart, instance_blackfathom_deeps/SetData, instance_blackrock_depths/HandleBarPatrol, instance_blackrock_depths/SetData, instance_blackrock_spire/DoSendNextStadiumWave, instance_blackrock_spire/SetData, instance_blackrock_spire/SetData64, instance_blackrock_spire/Update, instance_blackwing_lair/SetData, instance_deadmines/OnCreatureDeath, instance_dire_maul/SetData, instance_dire_maul/SetData64, instance_gnomeregan/DoActivateBombFace, instance_gnomeregan/DoDeactivateBombFace, instance_naxxramas.boss_kelthuzad/StartEncounter, instance_razorfen_kraul/SetData, instance_scholomance/OnCreatureDeath, instance_scholomance/SetData, instance_shadowfang_keep/SetData, instance_stratholme/DoGateTrap, instance_stratholme/SetData, instance_stratholme/Update, instance_sunken_temple/SetData, zulfarrak/MovementInform | — |
| DoRespawnGameObject | method | GameObject/GetGoType, GameObject/isSpawned, GameObject/Refresh, GameObject/SetRespawnTime, Map.Main/GetGameObject, ObjectGuid/ObjectGuid#5 | instance_blackrock_depths/SetData, instance_dire_maul/SetData, instance_gnomeregan/SetData, instance_molten_core/SetData, instance_naxxramas.Main/SetData, instance_sunken_temple/HandleStatueEventDone, instance_sunken_temple/ProcessStatueUsed | — |
| ScriptedInstance | ctor | — | instance_blackfathom_deeps/instance_blackfathom_deeps, instance_blackrock_depths/instance_blackrock_depths, instance_blackrock_spire/instance_blackrock_spire, instance_blackwing_lair/instance_blackwing_lair, instance_deadmines/instance_deadmines, instance_dire_maul/instance_dire_maul, instance_gnomeregan/instance_gnomeregan, instance_maraudon/instance_maraudon, instance_molten_core/instance_molten_core, instance_naxxramas.Main/instance_naxxramas, instance_onyxia_lair/instance_onyxia_lair, instance_razorfen_downs/instance_razorfen_downs, instance_razorfen_kraul/instance_razorfen_kraul, instance_ruins_of_ahnqiraj/instance_ruins_of_ahnqiraj, instance_scarlet_monastery/instance_scarlet_monastery, instance_scholomance/instance_scholomance, instance_shadowfang_keep/instance_shadowfang_keep, instance_stratholme/instance_stratholme, instance_sunken_temple/instance_sunken_temple, instance_temple_of_ahnqiraj/instance_temple_of_ahnqiraj, instance_uldaman/instance_uldaman, instance_wailing_caverns/instance_wailing_caverns, instance_zulfarrak/instance_zulfarrak | — |
| ~ScriptedInstance | dtor | — | — | — |
| DoOpenDoor | method | GameObject/GetGoType, GameObject/getLootState, GameObject/UseDoorOrButton, Log.Main/Out, Map.Main/GetGameObject, Object/GetEntry, ObjectGuid/ObjectGuid#5 | instance_blackrock_depths/SetData, instance_temple_of_ahnqiraj/SetData, instance_uldaman/SetData, instance_uldaman/Update | — |
| DoOrSimulateScriptTextForThisInstance | method | — | instance_naxxramas.boss_kelthuzad/UpdateP2P3, instance_naxxramas.Main/Update | — |
| DoResetDoor | method | GameObject/GetGoType, GameObject/ResetDoorOrButton, Log.Main/Out, Map.Main/GetGameObject, Object/GetEntry, ObjectGuid/ObjectGuid#5 | boss_twinemperors/Aggro, instance_blackrock_depths/SetData, instance_uldaman/SetData | — |
| DoUpdateWorldState | method | LinkedListHead/isEmpty, Log.Main/Out, Map.Main/GetPlayers, Player.Main/SendUpdateWorldState | — | — |
| GenSaveData | method | Log.Main/Out | instance_zulgurub/Create, instance_zulgurub/SetData | — |
| LoadSaveData | method | Log.Main/Out | instance_zulgurub/Load | — |
| GetPlayerInMap | method | Map.Main/GetPlayers, Player.Main/IsGameMaster, Unit.Main/IsAlive | boss_cthun/UpdateAI#2, boss_cthun/UpdateCthunTentacle, instance_naxxramas.Main/Update, instance_stratholme/Update | — |
| GetSingleGameObjectFromStorage | method | Log.Main/Out, Map.Main/GetGameObject, Map.Main/GetId | boss_gothik/HasLessPlayersPerSide, boss_gothik/OpenTheGate, boss_thaddius/HandleCheckSpawnAdd, boss_thaddius/HandleUnsummonCoil, boss_twinemperors/Aggro, instance_naxxramas.Main/IsInRightSideGothArea, instance_naxxramas.Main/SetData, instance_naxxramas.Main/ToggleKelThuzadWindows, instance_naxxramas.Main/UpdateAutomaticBossEntranceDoor, instance_naxxramas.Main/UpdateBossGate, instance_naxxramas.Main/UpdateManualDoor, instance_naxxramas.Main/UpdateTeleporters, instance_temple_of_ahnqiraj/SetData | — |
| GetSingleCreatureFromStorage | method | Log.Main/Out, Map.Main/GetCreature, Map.Main/GetId | boss_anubrekhan/Aggro#2, boss_anubrekhan/OnUse, boss_bug_trio/JustDied, boss_bug_trio/LeashEncounter, boss_cthun/Aggro#2, boss_four_horsemen/Aggro, boss_four_horsemen/Reset#2, boss_four_horsemen/Reset#3, boss_four_horsemen/Reset#4, boss_four_horsemen/Reset#5, boss_garr/JustDied#2, boss_gluth/ChaseGluth, boss_gothik/EffectDummyCreature_spell_anchor, boss_sapphiron/OnUse, boss_sapphiron/PickNewTarget, boss_sartura/LeashEncounter#2, boss_twinemperors/GetOtherBoss, instance_naxxramas.boss_kelthuzad/OnKTAreaTrigger, instance_naxxramas.boss_kelthuzad/SpellHit#2, instance_naxxramas.Main/HandleEvadeOutOfHome, instance_naxxramas.Main/onNaxxramasAreaTrigger, instance_naxxramas.Main/OnPlayerDeath, instance_naxxramas.Main/SetData, instance_naxxramas.Main/SetGothTriggers, instance_naxxramas.Main/Update, instance_temple_of_ahnqiraj/AddPlayerToStomach, instance_temple_of_ahnqiraj/DoHandleTempleAreaTrigger, instance_temple_of_ahnqiraj/JustDidDialogueStep, instance_temple_of_ahnqiraj/RestoreOuroSpawnTrigger, instance_temple_of_ahnqiraj/SetData, instance_temple_of_ahnqiraj/Start, instance_temple_of_ahnqiraj/UpdateCThunWhisper, instance_temple_of_ahnqiraj/UpdateStomachOfCthun | — |
| OnCreatureEnterCombat | method | Creature.Main/IsWorldBoss, Object/GetObjectGuid, WorldObject.Object/MonsterSay, ZoneScript/OnCreatureEnterCombat | — | — |
| Update | method | Creature.Main/SaveRespawnTime, InstanceData/Update, Map.Main/GetCreature, Unit.Main/DoKillUnit, Unit.Main/IsAlive, WorldObject.Object/AddObjectToRemoveList, ZoneScript/GetMap#2 | — | — |
| DialogueHelper | ctor | — | instance_blackrock_spire/instance_blackrock_spire, instance_temple_of_ahnqiraj/instance_temple_of_ahnqiraj, instance_temple_of_ahnqiraj/TwinsIntroDialogue | — |
| DialogueHelper#2 | ctor | — | — | — |
| StartNextDialogueText | method | Log.Main/Out, Map.Main/GetId | instance_blackrock_spire/DoSendNextStadiumWave, instance_blackrock_spire/OnCreatureDeath, instance_blackrock_spire/SetData, instance_temple_of_ahnqiraj/SetData, instance_temple_of_ahnqiraj/Start | — |
| DoNextDialogueStep | method | DialogueHelper/GetSpeakerByEntry, DialogueHelper/JustDidDialogueStep, ScriptMgr/DoScriptText | — | — |
| DialogueUpdate | method | — | instance_blackrock_spire/Update, instance_temple_of_ahnqiraj/Update | — |
