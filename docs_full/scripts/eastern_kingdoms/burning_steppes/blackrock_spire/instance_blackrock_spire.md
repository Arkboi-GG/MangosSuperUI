# instance_blackrock_spire

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# instance_blackrock_spire

## Purpose & Responsibilities

`instance_blackrock_spire` is the `ScriptedInstance` handler for the Upper Blackrock Spire (UBRS) wing of the Blackrock Spire dungeon in the WoW server emulation. It manages the state, logic, and synchronization for eight distinct encounter types or events within the instance:

1.  **Room Event:** A trash pack sorting mechanic where mobs are assigned to specific rooms based on proximity to runes.
2.  **Emberseer:** Boss encounter state tracking.
3.  **Flamewreath:** Boss encounter state tracking.
4.  **Stadium Event:** A complex multi-stage event involving dialogue, spectator spawning, wave-based mob summons (Chromatic Whelps/Dragons), and a final boss fight against Gyth.
5.  **Valthalak:** Boss encounter state tracking.
6.  **UBRS Door Event:** A timed sequence activating braziers and opening the door to Upper Blackrock Spire.
7.  **Solakar/Rookery Event:** A timed wave-spawning event triggered by interacting with the Father Flame, culminating in the spawn of Solakar.
8.  **Drakkisath:** Boss encounter state tracking and door management.

The class also handles miscellaneous instance-wide mechanics, such as the random replacement of a Firebrand Grunt with Bannok Grimaxe, area triggers for entering UBRS or triggering the Stadium event, and specific spell/game object interactions (Freezing Rookery Eggs, The Beast aggro).

## Member-by-Member Behavior

### Initialization and Lifecycle

**`instance_blackrock_spire`**
Constructs the instance data object. It initializes all GUID member variables to 0, timers to 0, counters to 0, and boolean flags to false. It calls `Initialize()` to zero out the encounter status array (`m_auiEncounter`) and rune GUID arrays. It inherits from `ScriptedInstance` and privately inherits from `DialogueHelper`, passing the static `aStadiumDialogue` array to manage the Stadium event's scripted conversation.

**`~instance_blackrock_spire`**
Default destructor. No custom cleanup logic is implemented.

**`Initialize`**
Resets the internal state of the instance. It uses `memset` to clear the `m_auiEncounter` array (tracking boss/event states) and `m_auiRoomRuneGUID` array. In debug builds, it logs the initialization. This is called during construction and potentially upon instance reset.

**`Save`**
Returns the string representation of the instance data (`strInstData`). This string is populated by `SetData` when an encounter reaches the `DONE` state. It contains space-separated integers representing the state of each of the 8 encounter types.

**`Load`**
Restores instance state from the database string. It parses the space-separated integers into `m_auiEncounter`. Crucially, it converts any `IN_PROGRESS` states back to `NOT_STARTED`, ensuring that partially completed events do not persist incorrectly across server restarts. It logs success or failure.

### State Management

**`SetData`**
Updates the state of a specific encounter type (`uiType`) to a new value (`uiData`).
-   **TYPE_ROOM_EVENT:** If done, opens the Emberseer entry door.
-   **TYPE_EMBERSEER / TYPE_FLAMEWREATH / TYPE_VALTHALAK / TYPE_DRAKKISATH:** Simply records the state. For Drakkisath, if done, it opens both Drakkisath doors.
-   **TYPE_STADIUM:** Handles complex transitions.
    -   If starting (`IN_PROGRESS`), it initiates the dialogue sequence.
    -   If completed (`DONE`), it despawns spectators and opens the exit door.
    -   If failed (`FAIL`), it despawns key NPCs (Nefarius, Rend, Gyth) and spectators, resets timers and counters, effectively canceling the event.
-   **TYPE_EVENT_DOOR_UBRS:** If done, starts a 2-second timer (`m_uiUBRSDoor_Timer`) to begin the brazier sequence.
-   **TYPE_SOLAKAR:** If failed, resets the Father Flame timer and wave count. If started, sets the Father Flame timer to 5 seconds to begin the first wave.
-   **Persistence:** If the new state is `DONE`, it serializes the entire `m_auiEncounter` array into `strInstData` and calls `SaveToDB()`.

**`SetData64`**
Handles 64-bit data updates, primarily used for the Room Event.
-   **TYPE_ROOM_EVENT:** When a mob dies (passed as `uiData` GUID), it removes that GUID from the sorted list for the room it was assigned to. If a room's list becomes empty, it activates the corresponding Rune GameObject. If all rooms are empty, it marks the Room Event as `DONE`.

**`GetData`**
Returns the current state (`NOT_STARTED`, `IN_PROGRESS`, `DONE`, `FAIL`) of a specified encounter type from `m_auiEncounter`.

**`GetData64`**
Returns the stored GUID for a specific NPC or GameObject (e.g., Nefarius, Drakkisath, Blackrock Altar).

### Creature and Object Tracking

**`OnObjectCreate`**
Called when a GameObject spawns in the instance. It switches on the GameObject entry ID to store its GUID in the appropriate member variable.
-   It stores GUIDs for doors (Emberseer, Gyth, Drakkisath, UBRS), runes (Room Event), braziers (UBRS Door), and special objects (Father Flame, Blackrock Altar, Rookery Eggs).
-   **State Restoration:** For several doors (Emberseer Out, Gyth Exit, Drakkisath Doors, UBRS Door/Braziers), it checks the current instance state via `GetData`. If the associated event is already `DONE`, it immediately sets the GameObject state to `ACTIVE` (open), ensuring visual consistency after a reload.

**`OnCreatureCreate`**
Called when a Creature spawns. It stores GUIDs for key bosses (Nefarius, Rend, Gyth, Infiltrator, Drakkisath, The Beast).
-   **Trash Lists:** It adds GUIDs of Blackhand Summoners/Veterans to `m_lRoomEventMobGUIDList` and Incarcerators to `m_lIncanceratorGUIDList`.
-   **Random Spawn Logic:** For `NPC_FIREBRAND_GRUNT`, it checks the low GUID. If it matches one of three specific IDs (placeholders), it rolls a 5% chance (`urand(0,99) < 5`). If successful and Bannok hasn't spawned yet (`!m_bBannokSpawned`), it replaces the grunt with `NPC_BANNOK_GRIMAXE` using `UpdateEntry` and sets the flag.

**`OnCreatureDeath`**
Handles logic triggered by creature deaths.
-   **Stadium Mobs (Whelps, Dragons, Handlers):** Checks if the creature is a temporary summon. If so, it decrements `m_uiStadiumMobsAlive`. It then rolls a 5% chance for Nefarius and a separate 5% chance for Rend to say a random taunt line. If `m_uiStadiumMobsAlive` hits 0 and all waves have been sent, it triggers the next phase (Gyth intro).
-   **Gyth/Rend:** Decrements `m_uiStadiumMobsAlive`. If 0, it triggers the victory dialogue.

**`OnCreatureEvade`**
Handles logic when a creature loses aggro and runs away.
-   **Stadium Mobs:** If a temporary stadium mob evades, it marks the Stadium event as `FAIL` and despawns the creature. This prevents stuck events.

### Stadium Event Logic

**`GetSpeakerByEntry`**
Helper function returning the `Creature` pointer for Nefarius or Rend based on entry ID, used by the dialogue system.

**`JustDidDialogueStep`**
Callback from `DialogueHelper` when a dialogue step completes.
-   **NPC_BLACKHAND_HANDLER:** Sets a 1-second timer. Moves Rend and Nefarius to positions. Spawns 12 spectators at specific locations, moves them to balconies, and stores their GUIDs.
-   **SAY_NEFARIUS_WARCHIEF:** Prepares for Gyth. Despawns Rend after 5 seconds, moves him to a position. Sets a 30-second timer.
-   **SAY_NEFARIUS_PACING:** Makes Nefarius patrol via waypoints.
-   **SAY_NEFARIUS_VICTORY:** Marks Stadium event as `DONE`.
-   **NPC_REND_BLACKHAND:** Despawns Nefarius after 5 seconds, moves him.

**`DoSendNextStadiumWave`**
Manages the wave spawning for the Stadium event.
-   **Waves 0-6:** Iterates through `aStadiumEventNpcs` for the current wave index. Spawns mobs around Nefarius. Groups them together. Increments `m_uiStadiumMobsAlive`. Sends the group on a waypoint path. Opens the combat door.
-   **Wave 7+ (Gyth):** If all previous waves are cleared, it stops Nefarius, moves him back, and summons Gyth. Sets `m_uiStadiumMobsAlive` to 2 (Gyth + Rend). Opens combat door.
-   **Timer:** Resets the event timer to 60 seconds for subsequent waves, or 0 if finished.

**`DespawnStadiumSpectators`**
Iterates through `m_lStadiumSpectatorsGUIDList` and despawns each spectator, then clears the list.

### Timed Events (Update Loop)

**`Update`**
Called periodically with time difference (`uiDiff`).
-   **Dialogue:** Calls `DialogueUpdate`.
-   **Stadium Timer:** If `m_uiStadiumEventTimer` is active, it decrements it. If it expires, calls `DoSendNextStadiumWave`.
-   **UBRS Door Timer:** If `m_uiUBRSDoor_Timer` is active, it decrements it. If expired, it executes a state machine (`m_uiUBRSDoor_Step`) to activate braziers in pairs (01/02, 03/04, 05/06) with 3-second delays, finally opening the UBRS door.
-   **Solakar/Rookery Event:** If `TYPE_SOLAKAR` is `IN_PROGRESS`:
    -   Decrements `m_uiFatherFlame_timer`.
    -   If expired:
        -   **Wave 0:** Spawns two Rookery Hatchers. One says a line. Sets timer to 30-40s.
        -   **Waves 1-4:** Randomly spawns pairs of Guardians/Hatchers. Sets timer to 30-40s.
        -   **Wave 5+:** Spawns Solakar. Marks event `DONE`. Resets timer.

### Room Event Logic

**`DoSortRoomEventMobs`**
Assigns trash mobs to rooms for the Room Event.
-   Checks if event is `NOT_STARTED`.
-   Iterates through 7 rooms. For each room, finds the Rune GameObject.
-   Iterates through `m_lRoomEventMobGUIDList`. If a mob is alive and within 10 yards of the Rune, adds its GUID to that room's sorted list.
-   Marks the event as `IN_PROGRESS`.

### External Interfaces and Scripts

**`GetIncanceratorGUIDList` / `GetRookeryEggGUIDList`**
Simple getters that copy internal GUID lists to an output parameter. Used by other scripts (likely boss AI) to target these objects.

**`GetInstanceData_instance_blackrock_spire`**
Factory function creating a new `instance_blackrock_spire` instance.

**`AreaTrigger_at_blackrock_spire`**
Handles area trigger interactions for players.
-   **AREATRIGGER_ENTER_UBRS:** Calls `DoSortRoomEventMobs`. If the player has item 12344 ("Seal of Ascension"), it triggers the UBRS Door event (`TYPE_EVENT_DOOR_UBRS`).
-   **AREATRIGGER_STADIUM:** If the Stadium event isn't already active/done, it respawns Nefarius and Rend if dead, and starts the Stadium event (`IN_PROGRESS`).

**`go_father_flameAI` / `GetAIgo_father_flame`**
AI for the Father Flame GameObject.
-   **`OnUse`:** Checks if Solakar event is not started/done and Drakkisath is alive. If so, starts the Solakar event (`IN_PROGRESS`).

**`AreaTrigger_at_ubrs_the_beast`**
Aggro trigger for The Beast. If the player enters the area and The Beast is alive and not in combat, it forces The Beast to attack the player.

**`UBRSFreezeRookeryEggScript` / `GetScript_UBRSFreezeRookeryEgg`**
Spell script for "Freeze Rookery Egg".
-   **`OnEffectExecute`:** If the spell targets a GameObject that is ready for loot/interaction, it activates the GameObject (likely freezing it visually/mechanically). Returns `false` to prevent default behavior.

**`AddSC_instance_blackrock_spire`**
Registers all scripts defined in this file with the `ScriptMgr`: the instance data, area triggers, GameObject AI, and spell script.

## Cross-Unit Boundaries

*   **ScriptedInstance:** Inherits core functionality. Calls `DoUseDoorOrButton` for door operations, `StartNextDialogueText` for dialogue, and `SaveToDB` for persistence.
*   **DialogueHelper:** Manages the timed dialogue sequences for the Stadium event. `instance_blackrock_spire` implements `JustDidDialogueStep` and `GetSpeakerByEntry` callbacks.
*   **Creature/Main:** Uses `DespawnOrUnsummon`, `IsTemporarySummon`, `UpdateEntry`, `Respawn`, `ForcedDespawn`, `SetDetectionDistance`, `SetHomePosition`, `SetNoCallAssistance`, `SetWalk`, `JoinCreatureGroup`.
*   **Creature/MotionMaster:** Controls movement via `MovePoint`, `MoveWaypoint`, `MoveIdle`.
*   **Unit/Main:** Uses `GetMotionMaster`, `SetFacingTo`, `IsAlive`, `IsDead`, `IsInCombat`.
*   **WorldObject/Object:** Uses `SummonCreature`, `GetRandomPoint`, `GetDistance`, `GetInstanceData`.
*   **ZoneScript:** Uses `GetCreature`, `GetMap`.
*   **ObjectGuid:** Constructs GUIDs.
*   **shared_Util:** Uses `urand` for random numbers, `frand` for floating-point randomness.
*   **ScriptMgr:** Uses `DoScriptText` for NPC speech.
*   **Log/Main:** Uses `Out` for debug logging.
*   **Map/Main:** Uses `GetId`, `GetInstanceId`, `GetMapName`, `GetCreature`, `GetGameObject`, `SummonCreature`.
*   **GameObject:** Uses `SetGoState`, `getLootState`, `UseDoorOrButton`.
*   **Spell/Main:** Uses `GetGOTarget`.
*   **Player/Main:** Uses `HasItemCount`, `IsGameMaster`.
*   **CreatureAI:** Uses `AttackStart`.
*   **Script:** Uses `RegisterSelf`.

## Data Model

This unit does not directly query or modify database tables via SQL strings. It relies on the `ScriptedInstance` base class methods (`SaveToDB`, `Load`) to persist the serialized `strInstData` string to the instance data table managed by the core engine. The schema of that table is not exposed here, but the data format is a space-separated string of 8 integers representing the state of each encounter type.

## Notable Implementation Details

*   **Stadium Event Complexity:** The Stadium event is the most complex logic in this file. It involves a dialogue tree, spectator spawning/movement, wave-based mob spawning with grouping, and a final boss phase. Failure handling is explicit, cleaning up all summoned entities.
*   **Room Event Sorting:** The `DoSortRoomEventMobs` function assigns mobs to rooms based on proximity to runes at the moment the player enters UBRS. This assignment is static for the duration of the event. Mob deaths update the room lists, and empty rooms open their runes.
*   **Bannok Grimaxe Random Spawn:** The logic to replace a Firebrand Grunt with Bannok Grimaxe is tied to specific low GUIDs and a global flag (`m_bBannokSpawned`) to ensure only one Bannok spawns per instance.
*   **UBRS Door Sequence:** The door opening is not instant. It uses a stepped timer in `Update` to light braziers in sequence before opening the main door, adding cinematic flavor.
*   **Solakar Wave Spawning:** The Solakar event uses a timer-based wave spawner in `Update`. The first wave is fixed (Hatchers), subsequent waves are randomized (Guardians/Hatchers), and the final wave spawns Solakar.
*   **State Persistence on Reload:** `OnObjectCreate` checks instance state to restore door/open states correctly after a server reload, preventing locked doors for completed encounters.
*   **Debug Logging:** Conditional compilation (`#ifdef DEBUG_ON`) allows for verbose logging of instance state changes and mob sorting, useful for debugging.

## Member Reference

**~instance_blackrock_spire**: Default destructor.
**Save**: Returns the serialized instance data string for database persistence.
**GetIncanceratorGUIDList**: Copies the list of Incarcerator GUIDs to an output parameter.
**GetRookeryEggGUIDList**: Copies the list of Rookery Egg GUIDs to an output parameter.
**instance_blackrock_spire**: Constructor initializing member variables and calling `Initialize`.
**Initialize**: Resets encounter states and rune GUIDs to zero.
**OnObjectCreate**: Stores GameObject GUIDs and restores door states based on instance progress.
**OnCreatureCreate**: Stores Creature GUIDs, populates trash lists, and handles random Bannok Grimaxe spawn.
**SetData**: Updates encounter state, triggers door/dialogue actions, and saves to DB if done.
**OnCreatureDeath**: Handles stadium mob deaths, taunts, and phase transitions.
**OnCreatureEvade**: Marks stadium event as failed if a summoned mob evades.
**GetSpeakerByEntry**: Returns Creature pointer for Nefarius or Rend for dialogue.
**JustDidDialogueStep**: Executes actions triggered by dialogue steps (spawning spectators, moving NPCs).
**DespawnStadiumSpectators**: Despawns all summoned spectators and clears the list.
**DoSendNextStadiumWave**: Spawns the next wave of stadium mobs or Gyth, managing timers and groups.
**Update**: Processes timers for stadium waves, UBRS door sequence, and Solakar wave spawning.
**SetData64**: Removes dead mobs from room event lists and opens runes when rooms are clear.
**Load**: Parses saved instance data from DB, resetting IN_PROGRESS states to NOT_STARTED.
**GetData**: Returns the state of a specific encounter type.
**GetData64**: Returns the GUID of a specific NPC or GameObject.
**DoSortRoomEventMobs**: Assigns trash mobs to rooms based on proximity to runes.
**GetInstanceData_instance_blackrock_spire**: Factory function to create the instance data object.
**AreaTrigger_at_blackrock_spire**: Handles player entry into UBRS (sorting mobs, door event) and Stadium event start.
**go_father_flameAI**: GameObject AI for Father Flame.
**OnUse**: Starts the Solakar event if conditions are met.
**GetAIgo_father_flame**: Factory function for Father Flame AI.
**AreaTrigger_at_ubrs_the_beast**: Forces The Beast to aggro the player upon entry.
**OnEffectExecute**: Activates the targeted Rookery Egg GameObject when frozen.
**GetScript_UBRSFreezeRookeryEgg**: Factory function for the freeze egg spell script.
**AddSC_instance_blackrock_spire**: Registers all scripts in this file with the ScriptMgr.

---

<!-- machine-true, projected from graph.json -->

## Map — instance_blackrock_spire

*Source:* instance_blackrock_spire.cpp, blackrock_spire.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ~instance_blackrock_spire | dtor | — | — | — |
| Save | method | — | — | — |
| GetIncanceratorGUIDList | method | — | — | — |
| GetRookeryEggGUIDList | method | — | — | — |
| instance_blackrock_spire | ctor | ScriptedInstance/DialogueHelper, ScriptedInstance/ScriptedInstance | — | — |
| Initialize | method | — | — | — |
| OnObjectCreate | method | GameObject/SetGoState, Object/GetEntry, Object/GetGUID | — | — |
| OnCreatureCreate | method | Creature.Main/UpdateEntry, Object/GetEntry, Object/GetGUID, Object/GetGUIDLow, shared_Util/urand | — | — |
| SetData | method | Creature.Main/DespawnOrUnsummon, InstanceData/SaveToDB, Log.Main/Out, Map.Main/GetId, Map.Main/GetInstanceId, Map.Main/GetMapName, ObjectGuid/ObjectGuid#5, ScriptedInstance/DoUseDoorOrButton, ScriptedInstance/StartNextDialogueText, ZoneScript/GetCreature | — | — |
| OnCreatureDeath | method | Creature.Main/IsTemporarySummon, Object/GetEntry, ObjectGuid/ObjectGuid#5, ScriptedInstance/StartNextDialogueText, ScriptMgr/DoScriptText, shared_Util/urand, ZoneScript/GetCreature | — | — |
| OnCreatureEvade | method | Creature.Main/DespawnOrUnsummon, Creature.Main/IsTemporarySummon, Object/GetEntry | — | — |
| GetSpeakerByEntry | method | ObjectGuid/ObjectGuid#5, ZoneScript/GetCreature | — | — |
| JustDidDialogueStep | method | Creature.Main/ForcedDespawn, Creature.Main/SetDetectionDistance, Creature.Main/SetHomePosition, Creature.Main/SetNoCallAssistance, Creature.MotionMaster/MovePoint, Creature.MotionMaster/MoveWaypoint, Object/GetObjectGuid, ObjectGuid/ObjectGuid#5, Unit.Main/GetMotionMaster, Unit.Main/SetFacingTo, Unit.Main/SetWalk, WorldObject.Object/SummonCreature#2, ZoneScript/GetCreature | — | — |
| DespawnStadiumSpectators | method | Creature.Main/DespawnOrUnsummon, Map.Main/GetCreature | — | — |
| DoSendNextStadiumWave | method | Creature.Main/JoinCreatureGroup, Creature.MotionMaster/MoveIdle, Creature.MotionMaster/MovePoint, Creature.MotionMaster/MoveWaypoint, ObjectGuid/ObjectGuid#5, ScriptedInstance/DoUseDoorOrButton, ScriptedInstance/StartNextDialogueText, shared_Util/frand, Unit.Main/GetMotionMaster, Unit.Main/SetWalk, WorldObject.Object/GetRandomPoint, WorldObject.Object/SummonCreature#2, ZoneScript/GetCreature | — | — |
| Update | method | ScriptedInstance/DialogueUpdate, ScriptedInstance/DoUseDoorOrButton, ScriptMgr/DoScriptText, shared_Util/urand, WorldObject.Object/SummonCreature, ZoneScript/GetMap#2 | — | — |
| SetData64 | method | ScriptedInstance/DoUseDoorOrButton | ubrs_trash/JustDied | — |
| Load | method | Log.Main/Out, Map.Main/GetId, Map.Main/GetInstanceId, Map.Main/GetMapName | — | — |
| GetData | method | — | — | — |
| GetData64 | method | — | — | — |
| DoSortRoomEventMobs | method | Map.Main/GetCreature, Map.Main/GetGameObject, ObjectGuid/ObjectGuid#5, Unit.Main/IsAlive, WorldObject.Object/GetDistance#3 | — | — |
| GetInstanceData_instance_blackrock_spire | function | — | — | — |
| AreaTrigger_at_blackrock_spire | function | Creature.Main/Respawn, ObjectGuid/ObjectGuid#5, Player.Main/HasItemCount, Player.Main/IsGameMaster, Unit.Main/IsAlive, Unit.Main/IsDead, WorldObject.Object/GetInstanceData, ZoneScript/GetCreature | — | — |
| go_father_flameAI | ctor | GameObjectAI/GameObjectAI | — | — |
| OnUse | method | InstanceData/GetData, InstanceData/GetData64, InstanceData/SetData, Map.Main/GetCreature, ObjectGuid/ObjectGuid#5, Unit.Main/IsAlive, WorldObject.Object/GetInstanceData | — | — |
| GetAIgo_father_flame | function | — | — | — |
| AreaTrigger_at_ubrs_the_beast | function | Creature.Main/AI, CreatureAI/AttackStart, Map.Main/GetCreature, ObjectGuid/ObjectGuid#5, Unit.Main/IsAlive, Unit.Main/IsDead, Unit.Main/IsInCombat, WorldObject.Object/GetInstanceData | — | — |
| OnEffectExecute | method | GameObject/getLootState, GameObject/UseDoorOrButton, Spell.Main/GetGOTarget | — | — |
| GetScript_UBRSFreezeRookeryEgg | function | — | — | — |
| AddSC_instance_blackrock_spire | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
