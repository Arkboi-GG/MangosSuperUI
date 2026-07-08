# instance_blackrock_depths

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# instance_blackrock_depths

**Purpose & Responsibilities**

`instance_blackrock_depths` is the `ScriptedInstance` implementation for the **Blackrock Depths** dungeon in World of Warcraft. It acts as the central state machine and coordinator for all scripted events, boss encounters, and interactive objects within this specific instance map.

Its primary responsibilities include:
1.  **State Management:** Tracking the completion status (`NOT_STARTED`, `IN_PROGRESS`, `DONE`, `FAIL`) of numerous sub-events (e.g., Ring of Law, Tomb of Seven, Lyceum, Iron Hall) via the `m_auiEncounter` array.
2.  **Entity Registration:** Capturing the GUIDs of critical NPCs (bosses like Emperor Dagran Thaurissan, Princess Moira) and GameObjects (doors, runes, kegs) during map population (`OnCreatureCreate`, `OnObjectCreate`) to allow efficient lookup and manipulation later.
3.  **Event Orchestration:** Handling complex multi-step sequences such as the "Ring of Law" arena challenge, the "Flamelash" rune-summoning mechanic, the "Bar Patron" hostility transitions, and the "Jail Break" quest progression.
4.  **Persistence:** Saving and loading the instance state to the database so that progress persists across server restarts or instance resets.

This unit does not contain AI logic for individual creatures; rather, it reacts to creature deaths, spell casts, and object interactions to trigger global changes (door openings, faction swaps, summons) via calls to core engine classes.

---

## Member-by-Member Behavior

### Initialization and Lifecycle

*   **`instance_blackrock_depths`**: The constructor initializes the parent `ScriptedInstance` and immediately calls `Initialize()` to reset all internal state variables, GUIDs, and timers to their default values.
*   **`Initialize`**: Resets the encounter array `m_auiEncounter` to zero, clears all stored GUIDs (bosses, doors, runes), resets timers (spirit summoning, patron emotes), and sets initial door states (all jail doors closed). It ensures a clean slate for a new instance.
*   **`Save`**: Returns the serialized string representation of the instance state (`strInstData`). This string is written to the database by the core engine.
*   **`Load`**: Parses the saved string data back into the `m_auiEncounter` array. Crucially, it converts any `IN_PROGRESS` states to `NOT_STARTED` to prevent stuck instances after a server crash/restart. It logs the load operation.
*   **`GetInstanceData_instance_blackrock_depths`**: Factory function that creates and returns a new `instance_blackrock_depths` object for a given `Map`.
*   **`AddSC_instance_blackrock_depths`**: Registers the script with the `ScriptMgr` so the engine knows to instantiate this class for the Blackrock Depths map.

### Entity Tracking (OnCreate Hooks)

*   **`OnCreatureCreate`**: Called by the engine when a creature spawns in the instance. It uses a switch statement on the creature's entry ID to:
    *   Store GUIDs for key bosses (Emperor, Princess, Phalanx, Haterel, etc.).
    *   Populate lists for groups of mobs (Arena Spectators, Ribbly's Cronies, Argelmach Protectors, Bar Patrons).
    *   Apply initial conditions: e.g., if the "Ring of Law" is already done, arena spectators are set to neutral faction. If "Plugger" is done, bar patrons are set to hostile Dark Iron faction.
    *   Validate spatial constraints: Arena spectators are only tracked if they are within a specific volume (Z-height and radius) around the arena center.
*   **`OnObjectCreate`**: Called when a GameObject spawns. It stores GUIDs for doors, runes, kegs, and chests. It also applies persistent state:
    *   If "Ring of Law" is done, Arena Door 3 is opened.
    *   If "Rocknot" or "Nagmara/Plugger" are done, the Bar Door is opened.
    *   If "Tomb of Seven" is done, the Tomb Exit door is used (opened).
    *   If "Lyceum" is done, Golem Room doors are opened.
    *   If "Iron Hall" is done, the Throne Room door is opened.

### Event Logic and State Transitions

*   **`SetData`**: The primary interface for updating instance state. It receives a type (event ID) and data (status).
    *   **Ring of Law**: Sets spectators to neutral if done.
    *   **Vault/Rocknot/Tomb/Lyceum/Iron Hall**: Updates encounter status and triggers side effects like opening doors (`DoOpenDoor`, `DoUseDoorOrButton`), respawning objects (`DoRespawnGameObject`), or changing mob factions.
    *   **Thunderbrew/Relic Coffer**: Counts progress (3 ales or 12 coffers) before marking as done.
    *   **Doomgrip**: Opens the secret door.
    *   **Ribbly**: Changes cronies to hostile faction and attacks their current victims.
    *   **Argelmach Aggro**: Summons nearby protectors to attack Argelmach's victim.
    *   **Patrol**: Triggers the bar patrol sequence.
    *   **Theldren**: Gives kill credit to all players and spawns the spoils chest.
    *   **Plugger**: Tracks stolen ales; marks as in-progress after 3 steals.
    *   **Jail Doors**: Updates boolean flags for specific jail cell doors.
    *   **Flamelash**: Manages the rune activation/deactivation and burning spirit summons.
    *   **Persistence**: If the new data is `DONE`, it serializes the entire `m_auiEncounter` array into `strInstData` and calls `SaveToDB()`.

*   **`GetData`**: Retrieves the current status of an event or the open/closed state of a jail door. Note: For `TYPE_ROCKNOT`, it dynamically returns `SPECIAL` if the event is in progress and 3 ales have been counted, allowing other scripts to check for this intermediate state.
*   **`GetData64`**: Retrieves the stored GUID for a specific entity (boss, door, rune, player) identified by a constant ID.

### Specific Event Handlers

*   **`OnCreatureDeath`**:
    *   **Burning Spirit**: Removes the spirit from the tracking list.
    *   **Shadowforge Senator**: Triggers a random yell from Emperor Dagran Thaurissan, throttled by a 45-second timer (`m_uiDagranTimer`) to prevent spam.

*   **`CustomSpellCasted`**:
    *   Logs spell casts for debugging.
    *   Detects spell ID `27517` (Invocation of Theldren). If cast by a player and not already invoked, it starts the Theldren event (`BeginTheldrenEvent`).

*   **`BeginTheldrenEvent`**: Marks the Theldren event as in-progress and stores the GUID of the player who initiated the challenge.

*   **`ReplacePrincessIfPossible`**: Checks if any Alliance player in the instance has *not* completed the quest "Fate of the Kingdom" or any Horde player has *not* completed "Royal Rescue". If *all* relevant players have completed their quests, it transforms the Princess Moira NPC into High Priestess (entry change), indicating the rescue was successful.

*   **`HandleBarPatrons`**: Manages the behavior of bar patrons based on event type:
    *   **PATRON_EMOTE**: Periodically (every ~1.25s), ~5% of patrons perform a random emote (laugh, cheer, etc.) if the Plugger event isn't done.
    *   **PATRON_PISSED**: When Rocknot breaks the keg, patrons near the keg trap yell angry lines.
    *   **PATRON_HOSTILE**: When Plugger is defeated, all patrons become hostile (Dark Iron faction), stand up, and wander randomly. Rocknot and Nagmara despawn (unless Rocknot is under a specific aura).

*   **`HandleBarPatrol`**: Executes the scripted patrol sequence for Plugger Spazzring:
    *   **Step 0**: Spawns Fireguard Destroyers and Anvilrage Officers behind the bar door, moves them into the bar.
    *   **Step 1**: An Anvilrage Officer yells the first line.
    *   **Step 2**: An Anvilrage Officer yells the second line, marking the patrol as done.

*   **`DoSummonCreatureAndAttack`**: Helper to summon a creature at one of four predefined positions near coordinates (586, -152, -52), put it in combat with a target, and set its faction template.

*   **`EnableCreature`**: Removes flags that make a creature unselectable, spawning, or immune to NPCs. Used to activate hidden or disabled mobs.

*   **`SetOpenedDoor` / `GetOpenedDoor`**: Internal helpers to manage the boolean state of six specific jail doors (Dughal, Tobias, Crest, Jaz, Shill, Supply).

### Periodic Updates

*   **`Update`**: Called periodically by the engine with the time difference (`uiDiff`).
    *   Decrements `m_uiDagranTimer` (throttle for Emperor yells).
    *   Decrements `m_uiPatronEmoteTimer`; triggers `HandleBarPatrons(PATRON_EMOTE)` when expired.
    *   Decrements `m_uiPatrolTimer`; triggers next step of `HandleBarPatrol` if in progress.
    *   **Flamelash Logic**: If Flamelash is active, it iterates through 7 runes. For each rune, if its timer expires, it summons a Burning Spirit at the rune's location (if max spirits not reached) and sets a new random timer (15-30s). Spirits follow Flamelash.

---

## Cross-Unit Boundaries

*   **`ScriptedInstance`**: Inherits from this base class. Uses methods like `DoUseDoorOrButton`, `DoOpenDoor`, `DoRespawnGameObject`, `SaveToDB`, and `OUT_SAVE_INST_DATA` macros.
*   **`WorldObject` / `Object`**: Uses `GetGUID`, `GetObjectGuid`, `GetEntry`, `GetName`, `GetPositionX/Y/Z`, `IsWithinDist2d`, `IsWithinDist`, `GetRandomPoint`, `SummonCreature`, `RemoveFlag`.
*   **`Creature`**: Uses `SetFactionTemporary`, `SetStandState`, `SetDefaultMovementType`, `SetWanderDistance`, `ForcedDespawn`, `UpdateEntry`, `SetRespawnDelay`, `SetInCombatWithZone`, `AI()`, `GetVictim`, `IsAlive`, `IsInCombat`, `DespawnOrUnsummon`.
*   **`CreatureAI`**: Calls `AttackStart` on AI objects to initiate combat.
*   **`Unit`**: Uses `GetMotionMaster`, `HandleEmote`, `HasAura`, `SetWalk`, `CastSpell`, `SetFactionTemplateId`.
*   **`Creature.MotionMaster`**: Uses `Initialize`, `MoveIdle`, `MovePoint`, `MoveFollow`.
*   **`GameObject`**: Uses `SetGoState`, `UseDoorOrButton`, `ResetDoorOrButton`, `GetGoState`.
*   **`Map`**: Uses `GetCreature`, `GetGameObject`, `GetPlayers`, `GetInstanceId`, `GetId`, `GetMapName`.
*   **`Player`**: Uses `GetQuestRewardStatus`, `GetTeam`, `KilledMonsterCredit`.
*   **`ScriptMgr`**: Uses `DoScriptText` to play sound/text emotes.
*   **`Log`**: Uses `Out` for debug logging.
*   **`shared_Util`**: Uses `urand` for random number generation.
*   **`ZoneScript`**: Uses `GetMap`, `GetCreature`, `GetGameObject` (via `instance` pointer which is derived from Map).
*   **`GridSearchers`**: Uses `GetCreatureListWithEntryInGrid` to find nearby mobs (used in Lyceum event).
*   **`LinkedListHead`**: Uses `isEmpty` to check if player list is empty.

---

## Data Model

This unit does not directly query or manipulate database tables via SQL strings. Instead, it relies on the `ScriptedInstance` base class methods (`SaveToDB`, `Load`) to persist its state. The state is serialized into a space-separated string of integers representing the `m_auiEncounter` array. This string is stored in the instance data table managed by the core engine (typically `instance` or `instance_data` depending on the specific Mangos/SD2 variant). No custom tables are touched by this unit.

---

## Notable Implementation Details

1.  **Dynamic Rocknot State**: In `GetData(TYPE_ROCKNOT)`, the function returns `SPECIAL` if the event is `IN_PROGRESS` and `m_uiBarAleCount == 3`. This allows other scripts to detect that Rocknot has stolen enough ale to trigger the next phase, even though the main encounter state hasn't changed to `DONE` yet.
2.  **Throttled Emperor Yells**: `OnCreatureDeath` for Shadowforge Senators checks `m_uiDagranTimer`. If a senator dies, the Emperor yells, but the timer prevents another yell for 45 seconds. This timer is decremented in `Update`.
3.  **Flamelash Spirit Cap**: The `Update` loop for Flamelash checks `m_burningSpirits.size() < BURNING_SPIRIT_MAX`. If the cap is reached, it sets the rune timer to 1ms instead of summoning a new spirit, effectively pausing summons for that rune until a slot opens.
4.  **Jail Door State Separation**: Jail door states are stored in separate boolean members (`m_bDoorDughalOpened`, etc.) and accessed via `SetOpenedDoor`/`GetOpenedDoor`. They are also saved in the `m_auiEncounter` array indices 17-19? No, looking closely at `SetData`, the jail doors are handled in the switch cases `GO_JAIL_DOOR_*` which update the booleans, but they are *not* explicitly added to the `saveStream` in `SetData`. Wait, let's re-read `SetData`.
    *   The `saveStream` only saves `m_auiEncounter[0]` through `m_auiEncounter[19]`.
    *   The jail door booleans are updated in `SetData` cases `GO_JAIL_DOOR_*`, but they are **not** included in the serialization stream. This means jail door states are **not persisted** across server restarts. They will reset to closed on reload. This is a potential bug or intentional design choice (jail breaks are usually quick).
5.  **Arena Spectator Filtering**: `OnCreatureCreate` filters arena spectators by Z-height and distance. This prevents spectators outside the arena volume (e.g., in the corridors) from being affected by the Ring of Law faction change.
6.  **Princess Replacement Logic**: `ReplacePrincessIfPossible` checks *all* players in the instance. If *any* player hasn't completed their respective quest, the princess is *not* replaced. This ensures the "rescue" is only considered successful if the whole group participated.
7.  **Hardcoded Coordinates**: `DoSummonCreatureAndAttack` uses hardcoded coordinates for summoning locations. This makes the script fragile to map changes.
8.  **French Comments**: Some comments and log messages are in French (e.g., "On invoque pas 2 fois ...", "caste par"), indicating the original author's language preference.

---

## Member Reference

*   **`instance_blackrock_depths`**: Constructor that initializes the parent `ScriptedInstance` and calls `Initialize()` to reset all state.
*   **`EnableCreature`**: Removes `UNIT_FLAG_NOT_SELECTABLE`, `UNIT_FLAG_SPAWNING`, and `UNIT_FLAG_IMMUNE_TO_NPC` from a creature, making it interactable.
*   **`Initialize`**: Resets all encounter statuses, GUIDs, timers, and door states to their default values for a new instance.
*   **`SetOpenedDoor`**: Updates the boolean state of a specific jail door (Dughal, Tobias, Crest, Jaz, Shill, Supply) based on its entry ID.
*   **`OnCreatureCreate`**: Registers GUIDs for key NPCs and groups, applies initial faction/movement states based on existing instance progress, and filters arena spectators by location.
*   **`OnObjectCreate`**: Registers GUIDs for GameObjects and applies persistent visual states (opening doors, activating objects) based on existing instance progress.
*   **`OnCreatureDeath`**: Handles death events for Burning Spirits (cleanup) and Shadowforge Senators (triggers throttled Emperor yell).
*   **`HandleBarPatrons`**: Manages bar patron behavior: periodic emotes, angry reactions to keg breaking, and transition to hostile state upon Plugger's defeat.
*   **`HandleBarPatrol`**: Executes the multi-step patrol sequence for Plugger Spazzring, spawning guards and triggering dialogue.
*   **`CustomSpellCasted`**: Logs spell casts and detects the invocation of Theldren (spell 27517) to start the arena challenge.
*   **`DoSummonCreatureAndAttack`**: Summons a creature at one of four hardcoded positions, puts it in combat with a target, and sets its faction.
*   **`BeginTheldrenEvent`**: Starts the Theldren challenge event and records the challenger's GUID.
*   **`ReplacePrincessIfPossible`**: Checks if all players have completed their rescue quests; if so, transforms Princess Moira into High Priestess.
*   **`SetData`**: Updates instance state for various events, triggers side effects (doors, factions, summons), and persists data to DB if the event is marked `DONE`.
*   **`GetOpenedDoor`**: Returns the boolean open/closed state of a specific jail door.
*   **`GetData`**: Returns the current status of an event or jail door, including dynamic logic for Rocknot's intermediate state.
*   **`GetData64`**: Returns the stored GUID for a specific entity (NPC, GameObject, Player) identified by a constant.
*   **`Update`**: Periodically updates timers for Emperor yells, patron emotes, bar patrols, and Flamelash spirit summoning.
*   **`Save`**: Returns the serialized string of encounter states for database persistence.
*   **`Load`**: Deserializes saved encounter states, resetting `IN_PROGRESS` to `NOT_STARTED` to prevent stuck instances.
*   **`GetInstanceData_instance_blackrock_depths`**: Factory function to create a new instance of this script for a map.
*   **`AddSC_instance_blackrock_depths`**: Registers the script with the engine's script manager.

---

<!-- machine-true, projected from graph.json -->

## Map — instance_blackrock_depths

*Source:* instance_blackrock_depths.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| instance_blackrock_depths | ctor | ScriptedInstance/ScriptedInstance | — | — |
| EnableCreature | method | WorldObject.Object/RemoveFlag | — | — |
| Initialize | method | ObjectGuid/ObjectGuid#5 | — | — |
| SetOpenedDoor | method | — | — | — |
| OnCreatureCreate | method | Creature.Main/SetFactionTemporary, Object/GetEntry, Object/GetGUID, Object/GetObjectGuid, Unit.Main/SetStandState, WorldObject.Object/GetPositionZ, WorldObject.Object/IsWithinDist2d | — | — |
| OnObjectCreate | method | GameObject/SetGoState, GameObject/UseDoorOrButton, Object/GetEntry, Object/GetGUID, Object/GetObjectGuid | — | — |
| OnCreatureDeath | method | Map.Main/GetCreature, Object/GetEntry, Object/GetObjectGuid, ObjectGuid/ObjectGuid#5, ScriptMgr/DoScriptText, shared_Util/urand, Unit.Main/IsAlive | — | — |
| HandleBarPatrons | method | Creature.Main/ForcedDespawn, Creature.Main/SetDefaultMovementType, Creature.Main/SetFactionTemporary, Creature.Main/SetWanderDistance, Creature.MotionMaster/Initialize, Map.Main/GetCreature, Map.Main/GetGameObject, ObjectGuid/ObjectGuid#5, ScriptMgr/DoScriptText, shared_Util/urand, SpellCaster/CastSpell#2, Unit.Main/GetMotionMaster, Unit.Main/HandleEmote, Unit.Main/HasAura#2, Unit.Main/SetStandState, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/IsWithinDist2d | — | — |
| HandleBarPatrol | method | Creature.MotionMaster/MoveIdle, Creature.MotionMaster/MovePoint, GameObject/GetGoState, Map.Main/GetCreature, Map.Main/GetGameObject, Object/GetEntry, Object/GetGUID, ObjectGuid/ObjectGuid#5, ScriptedInstance/DoUseDoorOrButton, ScriptMgr/DoScriptText, Unit.Main/GetMotionMaster, WorldObject.Object/GetRandomPoint, WorldObject.Object/SummonCreature#2 | — | — |
| CustomSpellCasted | method | Log.Main/Out, Object/ToPlayer, WorldObject.Object/GetName | — | — |
| DoSummonCreatureAndAttack | method | Creature.Main/AI, Creature.Main/SetInCombatWithZone, CreatureAI/AttackStart, Unit.Main/SetFactionTemplateId, WorldObject.Object/SummonCreature | — | — |
| BeginTheldrenEvent | method | Object/GetGUID | — | — |
| ReplacePrincessIfPossible | method | Creature.Main/UpdateEntry, LinkedListHead/isEmpty, Map.Main/GetCreature, Map.Main/GetPlayers, ObjectGuid/ObjectGuid#5, Player.Main/GetQuestRewardStatus, Player.Main/GetTeam | — | — |
| SetData | method | Creature.Main/AI, Creature.Main/DespawnOrUnsummon, Creature.Main/SetFactionTemporary, Creature.Main/SetRespawnDelay, CreatureAI/AttackStart, GameObject/ResetDoorOrButton, GameObject/UseDoorOrButton, GridSearchers/GetCreatureListWithEntryInGrid#2, InstanceData/SaveToDB, Log.Main/Out, Map.Main/GetCreature, Map.Main/GetId, Map.Main/GetInstanceId, Map.Main/GetMapName, Map.Main/GetPlayers, ObjectGuid/ObjectGuid#5, Player.Main/KilledMonsterCredit, ScriptedInstance/DoOpenDoor, ScriptedInstance/DoResetDoor, ScriptedInstance/DoRespawnGameObject, ScriptedInstance/DoUseDoorOrButton, ScriptMgr/DoScriptText, Unit.Main/GetVictim, Unit.Main/IsAlive, Unit.Main/IsInCombat, Unit.Main/SetFactionTemplateId, WorldObject.Object/IsWithinDist, ZoneScript/GetGameObject, ZoneScript/GetMap#2 | — | — |
| GetOpenedDoor | method | — | — | — |
| GetData | method | — | — | — |
| GetData64 | method | — | — | — |
| Update | method | Creature.MotionMaster/MoveFollow, Map.Main/GetGameObject, Object/GetObjectGuid, ObjectGuid/ObjectGuid#5, shared_Util/urand, Unit.Main/GetMotionMaster, Unit.Main/SetWalk, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SummonCreature, ZoneScript/GetCreature, ZoneScript/GetMap#2 | — | — |
| Save | method | — | — | — |
| Load | method | Log.Main/Out, Map.Main/GetId, Map.Main/GetInstanceId, Map.Main/GetMapName | — | — |
| GetInstanceData_instance_blackrock_depths | function | — | — | — |
| AddSC_instance_blackrock_depths | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
