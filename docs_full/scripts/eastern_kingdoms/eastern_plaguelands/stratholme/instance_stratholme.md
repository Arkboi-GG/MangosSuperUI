# instance_stratholme

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# instance_stratholme

**Purpose & Responsibilities**

`instance_stratholme` is the `ScriptedInstance` data handler for the Stratholme dungeon in the WoWVMaNGOS server. It manages the state, progression, and environmental interactions for the entire instance, coordinating between multiple distinct encounter phases: the initial "Slaughter Square" events (crystals, abominations, Ramstein), the "Baron Run" escort mission involving Ysida, and various side quests (The Unforgiven, Silver Hand, Postmaster).

Its primary responsibilities include:
1.  **State Persistence:** Saving and loading encounter statuses (`m_auiEncounter`) to allow instance resets or reloads without losing progress.
2.  **Environmental Control:** Managing the opening/closing of gates, doors, and portcullises (e.g., Slaughter Square gates, Ziggurat doors) based on encounter completion.
3.  **Event Coordination:** Triggering specific sequences, such as summoning Ramstein after abominations die, initiating the Baron's run timer, and handling the "Unforgiven" spawn trigger.
4.  **Trap Management:** Implementing the "Rat Trap" mechanics in the Gauntlet, detecting player proximity, closing gates, and spawning plagued critters.
5.  **NPC State Management:** Tracking the death of specific NPCs (Black Guards, Silver Hand members) to trigger dialogue or quest credits.

It does not contain AI logic for individual mobs (which resides in their respective `CreatureAI` classes) but reacts to their deaths and state changes via callbacks like `OnCreatureDeath` and `SetData`.

## Member-by-Member Behavior

### Initialization and Lifecycle
*   **`instance_stratholme`**: The constructor initializes the instance by calling `Initialize()`. It inherits from `ScriptedInstance`, providing the base framework for map-specific scripting.
*   **`Initialize`**: Resets all internal state variables. It clears GUID lists (`crystalsGUID`, `abomnationGUID`, etc.), sets encounter states to zero, resets timers, and ensures flags like `m_summoningRammstein` are false. This is crucial for clean instance starts or reloads.
*   **`IsEncounterInProgress`**: Checks if any encounter in `m_auiEncounter` is marked as `IN_PROGRESS`. This prevents certain actions (like resetting the instance) while an event is active.
*   **`Save` / `Load`**: Handles persistence. `Save` serializes `m_auiEncounter` into a space-separated string. `Load` parses this string back into the array. Notably, `Load` converts any `IN_PROGRESS` states to `NOT_STARTED` to prevent stuck instances, and re-summons Ramstein if his event was completed but he wasn't killed.

### Encounter Progression & Data Access
*   **`GetData` / `GetData64`**: Provide read-only access to instance state. `GetData` returns encounter status or specific quest flags (e.g., `TYPE_SH_QUEST` checks if all Silver Hand members are dead). `GetData64` returns GUIDs for key NPCs (Baron, Ysida Trigger, Dathrohan) needed by other scripts.
*   **`SetData`**: The central hub for updating instance state. It handles complex logic for different encounter types:
    *   **Baron Run (`TYPE_BARON_RUN`)**: Starts the 45-minute timer, summons Ysida, and handles success/failure states. On success, it frees Ysida, gives her gossip/questgiver flags, and moves her to a specific point.
    *   **Ziggurat Encounters (`TYPE_BARONESS`, `TYPE_NERUB`, `TYPE_PALLID`)**: Opens corresponding Ziggurat doors upon completion.
    *   **Ramstein (`TYPE_RAMSTEIN`)**: Manages the sequence where abominations must die before Ramstein spawns. It prevents recursive calls using `m_summoningRammstein`. On Ramstein's death, it spawns 34 mindless undead minions and opens the Slaughter Square gate.
    *   **Baron (`TYPE_BARON`)**: Closes/opens gates based on progress. On completion, it removes ultimatum auras from players and grants quest credit for "Dead Man's Plea".
    *   **Crystals (`TYPE_CRISTAL_DIE`)**: Calls `StartSlaugtherSquare()` to check if all crystals are dead, potentially opening the port to Slaughter Square.
    *   **Side Quests**: Tracks Silver Hand deaths (`TYPE_SH_*`) and Postmaster progress (`TYPE_POSTMASTER`).

### Environmental & Trap Mechanics
*   **`StartSlaugtherSquare`**: Checks if all crystal GUIDs are dead. If so, it activates the ports to Slaughter Square and marks the crystal encounter as done.
*   **`UpdateGoState`**: A helper to change the state of a `GameObject` (door/button). It supports a `withRestoreTime` flag to temporarily activate a door (using `UseDoorOrButton`) or permanently set its state.
*   **`OnCreatureCreate` / `OnGameObjectCreate`**: Callbacks triggered when objects spawn. They store GUIDs for important NPCs (Baron, Ysida, Crystals, Abominations) and GameObjects (Gates, Ziggurats, Cages). They also apply initial flags (e.g., making the Baron unselectable until the run starts).
*   **`OnCreatureDeath`**: Currently only tracks `NPC_BLACK_GUARD` deaths. When all 5 are dead, it triggers Rivendare's "Ready" yell.

### Timed Events & Updates
*   **`Update`**: The main tick function, called periodically. It handles:
    *   **Unforgiven Spawn**: Checks if any player is near the trigger spot. If so, it summons The Unforgiven and phantoms.
    *   **Rat Traps**: Manages three timers per trap (cooldown, gate reopen, critter spawn). It checks player/pet proximity to activate traps.
    *   **Baron Run Timer**: Counts down the 45-minute limit. At specific intervals (45, 10, 5, 1 min), it casts ultimatum spells on players and triggers dialogue. If time runs out, it fails the encounter, kills Ysida, and closes the cage.
    *   **Abomination Movement**: Periodically moves random abominations to specific points to simulate activity.
    *   **Slaughter Square Timer**: Delays Ramstein's spawn or the Baron's gate opening.
    *   **Ysida Reward Timer**: Delays Ysida's reward dialogue after being freed.

### Helper Functions
*   **`JoueurDansPiegeRat1` / `JoueurDansPiegeRat2`**: Check if any alive, non-GM player is within specific coordinate bounds (parallelograms/rectangles) for the rat traps. Note: These functions are defined but **not called** anywhere in the current source code, suggesting they might be legacy or unused logic.
*   **`MoveAbomnationMob`**: Selects a random abomination from `slaugtherAboGUID` and moves it to a specific point, removing it from the list to avoid repetition.
*   **`SummonRamstein`**: Spawns Ramstein at a fixed location, sets his home position, and marks the Ramstein event as done.
*   **`DoGateTrap`**: Activates a rat trap by closing gates and setting timers for reopening and spawning critters.
*   **`DoSpawnPlaguedCritters`**: Spawns 30 random plagued critters (rat, maggot, insect) around a player near the trap.

## Cross-Unit Boundaries

*   **Calls Out:**
    *   **`ScriptedInstance`**: Inherits base functionality. Uses `DoUseDoorOrButton` for standard door operations.
    *   **`Map.Main`**: Retrieves creatures, game objects, and players from the map instance (`instance->GetCreature`, `instance->GetGameObject`, `instance->GetPlayers`).
    *   **`Creature` / `GameObject` / `Player` / `Unit`**: Interacts with object states (alive, flags, motion master, spells, positions).
    *   **`ScriptMgr`**: Plays sounds/dialogue via `DoScriptText`.
    *   **`Log.Main`**: Outputs debug information for script events.
    *   **`shared_Util`**: Uses `urand` and `frand` for randomization.
    *   **`WorldObject.Object`**: Uses `SummonCreature`, `GetRandomPoint`, `SetFlag`, `GetPositionX/Y/Z`.

*   **Called By:**
    *   **`ScriptLoader/AddScripts`**: Registers the instance script during server startup.
    *   **Other Scripts**: Likely called by creature AI scripts (e.g., `npc_baron`, `npc_ramstein`) via `instance->SetData()` or `instance->GetData()` to update or query state. The MAP shows no explicit "Called by" entries, implying these calls happen through the `InstanceData` interface exposed to other scripts.

## Data Model

This unit does not directly interact with database tables for persistent storage beyond the standard instance save/load mechanism handled by the core engine. The `Save` and `Load` methods serialize/deserialize the `m_auiEncounter` array into a string stored in the `instance` table's `data` field (managed by the core, not explicitly queried here). No custom SQL queries or direct table accesses are present in this source file.

## Notable Implementation Details

1.  **Unused Trap Detection Logic**: The methods `JoueurDansPiegeRat1` and `JoueurDansPiegeRat2` implement complex geometric checks for player positions but are never invoked. The actual trap activation in `Update` uses simple distance checks (`IsWithinDist2d`) against `aGateTrap` positions. This suggests the detailed geometric checks were either replaced or abandoned.
2.  **Recursive Call Prevention**: In `SetData` for `TYPE_RAMSTEIN`, a boolean flag `m_summoningRammstein` is used to prevent infinite recursion if `SummonRamstein` triggers another `SetData` call.
3.  **Hardcoded Coordinates**: Many positions (spawn points, movement targets, trap zones) are hardcoded floats. This makes maintenance difficult if map geometry changes.
4.  **Timer Management**: The `Update` function manually decrements multiple timers (`m_uiBaronRun_Timer`, `m_uiSlaugtherSquare_Timer`, etc.) and checks conditions. This is a common pattern in MaNGOS but can become brittle if many timers are added.
5.  **French Comments**: Some comments and variable names (e.g., `JoueurDansPiegeRat`, `npc_placeEcarlateGUID`) are in French, indicating the original author's language. This doesn't affect functionality but impacts readability for non-French speakers.
6.  **Baron Run Ultimatum Spells**: The code casts specific spells (`SPELL_BARON_ULTIMATUM_*`) on players at timed intervals. If these spell IDs are incorrect or missing, the visual/audio feedback for the Baron run will fail.
7.  **Ysida Freedom**: Upon successful Baron run, Ysida is flagged as a gossip/questgiver and moved. However, the actual gossip menu and quest rewards are likely handled elsewhere (e.g., in `npc_yvida` or gossip scripts). This unit only prepares her state.

## Member Reference

*   **`instance_stratholme`**: Constructor that initializes the instance state by calling `Initialize()`.
*   **`Initialize`**: Resets all member variables, GUID lists, timers, and encounter states to their default values.
*   **`IsEncounterInProgress`**: Returns true if any encounter is currently in progress.
*   **`StartSlaugtherSquare`**: Checks if all crystals are dead; if so, opens ports to Slaughter Square and updates state.
*   **`UpdateGoState`**: Helper to change a GameObject's state, optionally with a temporary restore time.
*   **`OnCreatureCreate`**: Stores GUIDs for key NPCs and applies initial flags (e.g., unselectable Baron).
*   **`OnGameObjectCreate`**: Stores GUIDs for key GameObjects and applies initial states (e.g., locked gates).
*   **`OnCreatureDeath`**: Tracks Black Guard deaths to trigger Baron's dialogue.
*   **`GetData`**: Returns encounter status or specific quest flags (e.g., Silver Hand deaths).
*   **`GetData64`**: Returns GUIDs for key NPCs (Baron, Ysida Trigger, Dathrohan).
*   **`SetData`**: Central method for updating instance state, handling complex logic for Baron Run, Ramstein, Ziggurats, and side quests.
*   **`Save`**: Serializes encounter states to a string for persistence.
*   **`Load`**: Deserializes encounter states from a string, resetting in-progress states and re-summoning Ramstein if needed.
*   **`JoueurDansPiegeRat1`**: Unused function checking if players are in a specific parallelogram zone for Rat Trap 1.
*   **`JoueurDansPiegeRat2`**: Unused function checking if players are in a specific rectangular zone for Rat Trap 2.
*   **`MoveAbomnationMob`**: Moves a random abomination to a specific point and removes it from the tracking list.
*   **`Update`**: Main tick function managing timers for Baron Run, Rat Traps, Abomination movement, and Unforgiven spawn.
*   **`SummonRamstein`**: Spawns Ramstein, sets his home position, and marks the event as done.
*   **`DoGateTrap`**: Activates a rat trap by closing gates and setting timers for reopening and critter spawning.
*   **`DoSpawnPlaguedCritters`**: Spawns 30 random plagued critters around a player near a trap.
*   **`GetInstanceData_instance_stratholme`**: Factory function to create an instance of `instance_stratholme`.
*   **`AddSC_instance_stratholme`**: Registers the instance script with the server's script manager.

---

<!-- machine-true, projected from graph.json -->

## Map — instance_stratholme

*Source:* instance_stratholme.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| instance_stratholme | ctor | ScriptedInstance/ScriptedInstance | — | — |
| Initialize | method | — | — | — |
| IsEncounterInProgress | method | — | — | — |
| StartSlaugtherSquare | method | Log.Main/Out, Map.Main/GetCreature, ObjectGuid/ObjectGuid#5, Unit.Main/IsAlive | — | — |
| UpdateGoState | method | GameObject/SetGoState, GameObject/UseDoorOrButton, Map.Main/GetGameObject, ObjectGuid/ObjectGuid#5 | — | — |
| OnCreatureCreate | method | Object/GetEntry, Object/GetGUID, WorldObject.Object/SetFlag | — | — |
| OnGameObjectCreate | method | GameObject/UseDoorOrButton, Object/GetEntry, Object/GetGUID, WorldObject.Object/SetFlag | — | — |
| OnCreatureDeath | method | Map.Main/GetCreature, Object/GetEntry, ObjectGuid/ObjectGuid#5, ScriptMgr/DoScriptText | — | — |
| GetData | method | — | — | — |
| GetData64 | method | — | — | — |
| SetData | method | Creature.Main/SetHomePosition, Creature.MotionMaster/MovePoint, Creature.MotionMaster/MoveRandom, GameObject/GetGoState, GameObject/SetGoState, InstanceData/SaveToDB, LinkedListHead/isEmpty, Log.Main/Out, Map.Main/GetCreature, Map.Main/GetGameObject, Map.Main/GetPlayers, MotionMaster/Clear, ObjectGuid/ObjectGuid#5, Player.Main/GetQuestStatus, Player.Main/KilledMonsterCredit, ScriptedInstance/DoUseDoorOrButton, ScriptMgr/DoScriptText, shared_Util/frand, Unit.Main/GetMotionMaster, Unit.Main/HasAura#2, Unit.Main/IsAlive, Unit.Main/RemoveAurasDueToSpell, Unit.Main/SetWalk, WorldObject.Object/GetRandomPoint, WorldObject.Object/SetFlag, WorldObject.Object/SummonCreature, WorldObject.Object/SummonCreature#2 | — | — |
| Save | method | — | — | — |
| Load | method | — | — | — |
| JoueurDansPiegeRat1 | method | Map.Main/GetPlayers, Player.Main/IsGameMaster, Player.Main/IsGMVisible, Unit.Main/IsAlive, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | — | — |
| JoueurDansPiegeRat2 | method | Map.Main/GetPlayers, Player.Main/IsGameMaster, Player.Main/IsGMVisible, Unit.Main/IsAlive, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | — | — |
| MoveAbomnationMob | method | Creature.MotionMaster/MovePoint, Map.Main/GetCreature, Object/GetEntry, ObjectGuid/ObjectGuid#5, shared_Util/urand, Unit.Main/GetMotionMaster, Unit.Main/IsAlive, Unit.Main/IsInCombat | — | — |
| Update | method | Creature.Main/SetHomePosition, Creature.MotionMaster/MovePoint, GameObject/SetGoState, LinkedListHead/isEmpty, Log.Main/Out, Map.Main/GetCreature, Map.Main/GetGameObject, Map.Main/GetPlayers, ObjectGuid/ObjectGuid#5, Player.Main/IsGameMaster, ScriptedInstance/DoUseDoorOrButton, ScriptedInstance/GetPlayerInMap, ScriptMgr/DoScriptText, shared_Util/urand, SpellCaster/CastSpell#2, Unit.Main/GetMotionMaster, Unit.Main/GetPet, Unit.Main/HasAura, Unit.Main/IsTargetableBy, WorldObject.Object/GetDistance3dToCenter, WorldObject.Object/IsWithinDist2d, WorldObject.Object/RemoveFlag, WorldObject.Object/SummonCreature, WorldObject.Object/SummonCreature#2 | — | — |
| SummonRamstein | method | Creature.Main/SetHomePosition, Creature.MotionMaster/MovePoint, Log.Main/Out, Unit.Main/GetMotionMaster, WorldObject.Object/SummonCreature | — | — |
| DoGateTrap | method | Log.Main/Out, ScriptedInstance/DoUseDoorOrButton | — | — |
| DoSpawnPlaguedCritters | method | shared_Util/urand, WorldObject.Object/GetRandomPoint, WorldObject.Object/SummonCreature#2 | — | — |
| GetInstanceData_instance_stratholme | function | — | — | — |
| AddSC_instance_stratholme | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
