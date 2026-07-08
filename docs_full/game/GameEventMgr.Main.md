<!-- provenance: failed-members -->
# GameEventMgr.Main

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# GameEventMgr

**Purpose & Responsibilities**

`GameEventMgr` is the singleton manager responsible for the lifecycle, scheduling, and state enforcement of "Game Events" within the WoWVMaNGOS server. Game Events are time-based or manually triggered occurrences that alter the world state by spawning/despawning creatures and game objects, modifying creature appearances/spells, activating/deactivating quests, and sending mass emails to players.

The manager handles two distinct categories of events:
1.  **Database-Driven Events:** Defined in the `game_event` table with start/end times, recurrence intervals, and durations. These are automatically scheduled and executed by the `Update` loop.
2.  **Hardcoded Events:** Defined in C++ code (via the `WorldEvent` base class) for complex logic that cannot be easily expressed in static database rows (e.g., War Effort stages, specific boss mechanics). These are updated via the `mGameEventHardcodedList`.

Key responsibilities include:
*   Loading event definitions and associated entity lists (creatures, game objects, quests, mails) from the database during initialization.
*   Calculating when the next event transition (start/stop) will occur to optimize the server's update loop.
*   Applying event effects: spawning entities, changing creature data (entry/display/equipment/spells), toggling quest availability, and sending mail.
*   Persisting the active event state across server restarts using the `game_event_status` table.
*   Providing an API for other systems (Chat commands, Scripts, AI) to query event status, manually start/stop events, or check if specific entities are part of an event.

## Member-by-Member Behavior

### Initialization and Lifecycle

**`GameEventMgr` (ctor)**
Initializes the singleton instance. Sets `m_IsGameEventsInit` to `false` and `m_IsSilithusEventCompleted` to `false`.

**`~GameEventMgr` (dtor)**
Default destructor. No cleanup logic is implemented in this unit.

**`LoadFromDB`**
The primary initialization routine that populates all internal data structures from the database. It performs the following steps:
1.  **Event Definitions:** Queries `game_event` to determine the maximum event ID and resizes `mGameEvent`. It then loads each event's timing (`start`, `end`, `occurence`, `length`), metadata (`holiday_id`, `description`, `hardcoded`, `disabled`), and patch restrictions. It calculates `leapDays` for yearly events to ensure accurate recurrence over years. Events with invalid patch ranges or zero length are skipped or disabled.
2.  **Hardcoded Events:** Calls `ChatHandler.HardcodedEvents/LoadHardcodedEvents` to populate `mGameEventHardcodedList`.
3.  **Creature Spawns:** Joins `creature` and `game_event_creature` to build `mGameEventCreatureGuids`. It distinguishes between direct spawns and pool-based spawns. If a creature is part of a pool, it registers the pool ID in `mGameEventSpawnPoolIds` and removes the creature from the event's direct GUID list to avoid double-spawning. It validates that pools do not contain entities from conflicting events.
4.  **GameObject Spawns:** Similar to creatures, joins `gameobject` and `game_event_gameobject` to build `mGameEventGameobjectGuids` and handles pool associations.
5.  **Creature Data Modifications:** Queries `game_event_creature_data` (filtered by the highest valid `patch` for the current server patch) to load `mGameEventCreatureData`. This maps creature GUIDs to their event-specific `entry_id`, `display_id`, `equipment_id`, and spells (`spell_start`, `spell_end`). It validates references to templates and spells, logging errors and zeroing out invalid IDs.
6.  **Quest Activations:** Queries `game_event_quest` to build `mGameEventQuests`. It disables these quests initially (`SetQuestActiveState(false)`) so they are only available when their associated event is active.
7.  **Mail Notifications:** Queries `game_event_mail` to build `mGameEventMails`. It validates race masks, quest requirements, mail templates, and sender entries.

**`Initialize`**
Called once at server startup.
1.  Reads `game_event_status` from the `characters` database to identify which events were active when the server last shut down (`activeAtShutdown`).
2.  Truncates `game_event_status` to prepare for fresh state tracking.
3.  Calls `Update` with the `activeAtShutdown` set. This ensures that events that were active at shutdown are resumed immediately rather than waiting for their next scheduled start time.
4.  Sets `m_IsGameEventsInit` to `true`.

**`Initialize#2`**
Called when a new `MapPersistentState` is created (e.g., for a newly loaded map instance). It iterates through currently active events and initializes the spawn pools associated with those events in the new map state via `PoolManager/InitSpawnPool`.

**`Update`**
The core tick function, called periodically by `World/Update`.
1.  **Hardcoded Events:** Iterates through `mGameEventHardcodedList`. For each enabled hardcoded event, it calls `WorldEvent/Update` and tracks the minimum delay until the next update via `WorldEvent/GetNextUpdateDelay`.
2.  **Database Events:** Iterates through all non-hardcoded, non-disabled events in `mGameEvent`.
    *   Uses `CheckOneGameEvent` to determine if the event should be active at the current time.
    *   If active but not currently tracked in `m_ActiveEvents`, it calls `StartEvent`. If the event was active at shutdown (passed via `activeAtShutdown`), it passes `resume=true` to suppress startup mails.
    *   If inactive but currently tracked, it calls `StopEvent`.
    *   If inactive and the system is still initializing (`!m_IsGameEventsInit`), it spawns negative-event entities (entities that exist when the event is *not* active) via `GameEventSpawn(-itr)`.
    *   Calculates the delay until the next state change via `NextCheck` and updates the global `nextEventDelay`.
3.  Returns the calculated delay in milliseconds for the next `Update` call.

### Event State Management

**`StartEvent`**
Manually starts an event.
1.  Validates the event ID.
2.  Calls `ApplyNewEvent` to handle the side effects (spawning, data changes, mails).
3.  If the event is hardcoded and not disabled, it finds the corresponding `WorldEvent` in `mGameEventHardcodedList` and calls `WorldEvent/Enable`.
4.  If `overwrite` is true, it resets the event's start time to now and adjusts the end time, effectively forcing the event to run immediately regardless of its schedule.

**`StopEvent`**
Manually stops an event.
1.  Validates the event ID.
2.  Calls `UnApplyEvent` to handle side effects (despawning, restoring data).
3.  If `overwrite` is true, it shifts the event's start time back by its length, ensuring it won't trigger again immediately based on its schedule.

**`EnableEvent`**
Toggles the `disabled` flag for an event in memory and the database.
1.  Validates the event ID.
2.  Updates `mGameEvent[event_id].disabled` and executes `UPDATE game_event SET disabled = ...` via `Database/PExecute#2`.
3.  If the event is currently active (`IsActiveEvent`):
    *   If it's a hardcoded event, it calls `WorldEvent/Disable` or `WorldEvent/Enable` accordingly.
    *   Otherwise, it calls `StopEvent` with `overwrite=true` to immediately stop the running event if it was disabled.

**`IsEnabled`**
Returns whether an event is not disabled. Logs an error if the event ID is invalid.

**`IsActiveEvent`**
Checks if the event ID exists in `m_ActiveEvents`. This is the authoritative source for whether an event's effects are currently applied.

**`IsValidEvent`**
Checks if an event ID is within bounds and has a valid definition (specifically, `length > 0`).

**`GetActiveEventList`**
Returns a const reference to `m_ActiveEvents`. Note: The header comments warn this is not thread-safe for use outside the world update loop.

**`GetEventMap`**
Returns a const reference to `mGameEvent`, allowing inspection of all event definitions.

### Effect Application (Internal Helpers)

**`ApplyNewEvent`**
Internal helper called by `StartEvent`.
1.  Adds the event ID to `m_ActiveEvents`.
2.  Inserts the event ID into `game_event_status` in the `characters` database.
3.  Sends a world text announcement if configured (`CONFIG_BOOL_EVENT_ANNOUNCE`).
4.  Calls `GameEventSpawn(event_id)` to spawn positive-event entities.
5.  Calls `GameEventUnspawn(-event_id)` to despawn negative-event entities (entities that should disappear when the event starts).
6.  Calls `UpdateCreatureData(event_id, true)` to apply visual/spell changes to existing creatures.
7.  Calls `UpdateEventQuests(event_id, true)` to activate event-specific quests.
8.  Calls `SendEventMails(event_id)` unless `resume` is true (to avoid spamming mails on server restart).

**`UnApplyEvent`**
Internal helper called by `StopEvent`.
1.  Removes the event ID from `m_ActiveEvents`.
2.  Deletes the event ID from `game_event_status` in the `characters` database.
3.  Calls `GameEventUnspawn(event_id)` to despawn positive-event entities.
4.  Calls `GameEventSpawn(-event_id)` to spawn negative-event entities.
5.  Calls `UpdateCreatureData(event_id, false)` to restore original creature data.
6.  Calls `UpdateEventQuests(event_id, false)` to deactivate event-specific quests.
7.  Calls `SendEventMails(-event_id)` to send end-of-event mails.

**`GameEventSpawn`**
Spawns creatures and game objects associated with an event ID.
*   Handles both positive IDs (spawn when event starts) and negative IDs (spawn when event ends).
*   For creatures: Retrieves `CreatureData` from `ObjectMgr`. If the entity is part of a pool and the event ID is negative, it excludes the object from the pool (`PoolManager/SetExcludeObject`) and updates the pool. Otherwise, it adds the creature to the grid and spawns it in maps.
*   For game objects: Similar logic to creatures.
*   For positive event IDs, it also triggers `PoolManager/SpawnPoolInMaps` for any pools registered to this event.

**`GameEventUnspawn`**
Despawns creatures and game objects associated with an event ID.
*   For creatures: Retrieves `CreatureData`. If part of a pool and event ID is negative, it excludes the object and updates the pool. Otherwise, it removes the creature from the grid and adds it to the removal list.
*   For game objects: Similar logic.
*   For positive event IDs, it triggers `PoolManager/DespawnPoolInMaps` for associated pools.

**`UpdateCreatureData`**
Iterates through `mGameEventCreatureData` for the given event ID. For each creature, it creates a `GameEventUpdateCreatureDataInMapsWorker` functor and applies it to all maps containing the creature via `Map.Main/GetCreature`.
*   If `activate` is true, it calls `Creature.Main/UpdateEntry` with the event data to change entry/display/equipment/spells.
*   If `activate` is false, it calls `UpdateEntry` with `nullptr` to restore original state, and then calls `Creature.Main/ApplyGameEventSpells` with `false` to remove event-specific spells.

**`UpdateEventQuests`**
Iterates through `mGameEventQuests` for the event ID. It retrieves the `Quest` template and calls `QuestDef/SetQuestActiveState` with the `Activate` flag.

**`SendEventMails`**
Iterates through `mGameEventMails` for the event ID.
*   If a `questId` is specified, it constructs a SQL query to find characters who have completed that quest and match the `raceMask`. It uses `MassMailMgr/AddMassMailTask#2` to send mail to these specific players.
*   If no `questId` is specified, it uses `MassMailMgr/AddMassMailTask#3` to send mail to all online characters matching the `raceMask`.

### Scheduling Logic

**`CheckOneGameEvent`**
Determines if an event is active at a specific `currenttime`.
*   Checks if `currenttime` is within `[start, end)`.
*   Calculates the offset from the start time, adjusted for leap days.
*   Checks if the offset modulo `occurence` is less than `length`. This implements the recurring window logic.

**`NextCheck`**
Calculates the number of seconds until the next state change for an event.
*   If the event has ended (`currenttime > end`), returns `max_ge_check_delay` (1 day).
*   If the event hasn't started (`start > currenttime`), returns the time until start.
*   If the event is currently active, returns the time remaining until the current window ends.
*   If the event is inactive, returns the time until the next window starts.
*   Ensures the returned delay doesn't exceed the event's absolute end time.

### Entity Lookup

**`GetCreatureUpdateDataForActiveEvent`**
Given a creature's low GUID, finds the `GameEventCreatureData` for the first *active* event that modifies this creature. It searches `mGameEventCreatureDataPerGuid` for all events associated with the GUID, checks if each is active via `IsActiveEvent`, and returns the data for the first match.

**`GetGameEventId`**
Template specialization for `Creature`. Searches `mGameEventCreatureGuids` to reverse-lookup the event ID associated with a given creature GUID. Returns 0 if not found.

**`GetGameEventId#2`**
Template specialization for `GameObject`. Searches `mGameEventGameobjectGuids` to reverse-lookup the event ID associated with a given game object GUID. Returns 0 if not found.

**`GetGameEventId#3`**
Template specialization for `Pool`. Searches `mGameEventSpawnPoolIds` to reverse-lookup the event ID associated with a given pool ID. Returns 0 if not found.

### Silithus PvP Event

**`UpdateSilithusPVP`**
A specialized, hardcoded-like event handler for the Silithus PvP zone.
*   Checks the current hour. The event runs for 2 hours every 6 hours (starting at hours divisible by 6).
*   Uses `m_IsSilithusEventCompleted` to track if the 2-hour window has passed.
*   If the event should be ON and isn't active, it calls `StartEvent(SILITHUS_PVP_EVENT_ON)` and sends a French global text message.
*   If the event should be OFF and is active, it calls `StopEvent(SILITHUS_PVP_EVENT_ON)` and sends a different French global text message.
*   *Note:* The messages are hardcoded in French, suggesting this might be legacy code from a French server or a specific localization test.

**`GetSilithusPVPEventCompleted` / `SetSilithusPVPEventCompleted`**
Accessors for the `m_IsSilithusEventCompleted` flag.

**`IsActiveHoliday`**
Checks if any currently active event has a `holiday_id` matching the provided `HolidayIds`.

## Cross-Unit Boundaries

*   **`ChatHandler` Units:** `GameEventMgr` is extensively called by various `ChatHandler` methods (`HandleEventStartCommand`, `HandleEventStopCommand`, `HandleEventInfoCommand`, etc.) to allow GMs to manually control events. It also calls `ChatHandler.HardcodedEvents/LoadHardcodedEvents` during initialization.
*   **`WorldEvent` Unit:** `GameEventMgr` maintains a list of `WorldEvent` pointers. It calls `WorldEvent/Enable`, `WorldEvent/Disable`, `WorldEvent/Update`, and `WorldEvent/GetNextUpdateDelay` to manage hardcoded events.
*   **`Log.Main` Unit:** Used throughout for logging errors, warnings, and informational messages (e.g., "GameEvent X started").
*   **`Database` Unit:** Used for reading event definitions (`Query`, `PQuery`) and persisting state (`PExecute`, `Execute`).
*   **`ObjectMgr` Unit:** Used to validate creature/game object GUIDs, retrieve template data, and add/remove entities from grids (`AddCreatureToGrid`, `RemoveCreatureFromGrid`, etc.).
*   **`PoolManager` Unit:** Crucial for handling pooled spawns. `GameEventMgr` calls `IsPartOfAPool`, `SetExcludeObject`, `SpawnPoolInMaps`, `DespawnPoolInMaps`, and `InitSpawnPool` to integrate with the pooling system.
*   **`Creature.Main` / `GameObject` Units:** Called to actually spawn/despawn entities in the world (`SpawnInMaps`, `AddToRemoveListInMaps`) and to update creature data (`UpdateEntry`, `ApplyGameEventSpells`).
*   **`Map.Main` Unit:** Used via `DoForAllMapsWithMapId` to apply creature data changes across all instances of a map.
*   **`MassMailMgr` Unit:** Called by `SendEventMails` to queue mass email tasks.
*   **`QuestDef` Unit:** Called by `UpdateEventQuests` to toggle quest availability.
*   **`Conditions` / `ScriptMgr` / `AI` Units:** Various scripts and condition evaluators call `IsActiveEvent`, `IsValidEvent`, and `IsEnabled` to make decisions based on event state.

## Data Model

`GameEventMgr` interacts with the following database tables:

*   **`game_event`**: Core definition of events. Columns used: `entry`, `start_time`, `end_time`, `occurence`, `length`, `holiday`, `description`, `hardcoded`, `disabled`, `patch_min`, `patch_max`.
*   **`game_event_creature`**: Links creature GUIDs to events. Columns used: `guid`, `event`.
*   **`game_event_gameobject`**: Links game object GUIDs to events. Columns used: `guid`, `event`.
*   **`game_event_creature_data`**: Defines visual/spell changes for creatures during events. Columns used: `guid`, `event`, `display_id`, `equipment_id`, `entry_id`, `spell_start`, `spell_end`, `patch`.
*   **`game_event_quest`**: Links quests to events. Columns used: `quest`, `event`, `patch_min`.
*   **`game_event_mail`**: Defines mail notifications for events. Columns used: `event`, `raceMask`, `quest`, `mailTemplateId`, `senderEntry`.
*   **`game_event_status`**: Persists active event state across restarts. Column used: `event`.
*   **`creature`**: Joined with `game_event_creature` to validate GUIDs.
*   **`gameobject`**: Joined with `game_event_gameobject` to validate GUIDs.
*   **`characters`**: Joined with `character_queststatus` (implicitly via SQL string in `SendEventMails`) to filter mail recipients by quest completion.

## Notable Implementation Details

1.  **Negative Event IDs:** The system supports "negative" events. An entity linked to event `-X` is spawned when event `X` is *inactive* and despawned when `X` becomes *active*. This allows for seamless swapping of entities (e.g., replacing a normal mob with an elite version during an event). The internal indexing for GUID lists uses `mGameEvent.size() + event_id - 1` to map both positive and negative IDs into a contiguous vector.
2.  **Pool Integration:** Creatures and game objects in events can be part of pools. If an entity is in a pool, `GameEventMgr` does not spawn it directly. Instead, it marks the pool as associated with the event and lets `PoolManager` handle the spawning. This prevents conflicts where a pool might try to spawn an entity that `GameEventMgr` also tries to spawn.
3.  **Leap Year Handling:** For yearly events (`occurence == default_year_length`), `LoadFromDB` calculates the number of leap days between the event's start year and the current year. This adjustment is applied in `CheckOneGameEvent` and `NextCheck` to ensure the event recurs accurately over years.
4.  **Resume Logic:** When the server restarts, `Initialize` reads `game_event_status` to identify events that were active. These events are started with `resume=true`, which suppresses the startup mail notification to avoid spamming players who were already aware of the event.
5.  **Silithus PvP Hardcoding:** The `UpdateSilithusPVP` function contains hardcoded French strings for global announcements. This is a notable artifact that may need localization or removal depending on the server's target audience.
6.  **Thread Safety Warning:** The header explicitly warns that `GetActiveEventList` is not thread-safe for use outside the world update loop. Maintainers should be cautious when accessing this list from other threads.

## Member Reference

**`CheckOneGameEvent`**: Determines if a database-driven event is active at a given time, accounting for recurrence and leap days.

**`NextCheck`**: Calculates the delay in seconds until the next state change (start/stop) for a database-driven event.

**`StartEvent`**: Manually starts an event, applying its effects and enabling any associated hardcoded `WorldEvent`.

**`StopEvent`**: Manually stops an event, reversing its effects.

**`~GameEventMgr`**: Default destructor.

**`GetActiveEventList`**: Returns the set of currently active event IDs.

**`GetEventMap`**: Returns the vector of all loaded event definitions.

**`EnableEvent`**: Toggles the disabled state of an event in memory and the database, stopping it immediately if it was active and disabled.

**`IsValidEvent`**: Checks if an event ID is valid and defined.

**`IsActiveEvent`**: Checks if an event is currently active (effects applied).

**`IsEnabled`**: Checks if an event is not disabled.

**`LoadFromDB`**: Loads all event data from the database, including definitions, entity links, creature data, quests, and mails.

**`Initialize`**: Initializes the event system at server startup, resuming events that were active at shutdown.

**`Initialize#2`**: Initializes spawn pools for active events in a new map persistent state.

**`Update`**: The main tick function that checks and updates the state of all events, returning the delay until the next check.

**`UnApplyEvent`**: Internal helper to reverse the effects of an event (despawn, restore data, deactivate quests, send end mails).

**`ApplyNewEvent`**: Internal helper to apply the effects of an event (spawn, modify data, activate quests, send start mails).

**`GameEventSpawn`**: Spawns creatures and game objects associated with an event ID, handling pool exclusions.

**`GameEventUnspawn`**: Despawns creatures and game objects associated with an event ID, handling pool exclusions.

**`GetCreatureUpdateDataForActiveEvent`**: Finds the creature modification data for the first active event affecting a given creature GUID.

**`GameEventUpdateCreatureDataInMapsWorker`**: Functor used to apply creature data changes across all maps.

**`operator()`**: The functor's execution method, updating the creature's entry and spells.

**`UpdateCreatureData`**: Applies or reverses visual/spell changes to creatures involved in an event.

**`UpdateEventQuests`**: Activates or deactivates quests associated with an event.

**`SendEventMails`**: Sends mass emails to players based on event start/end conditions and optional quest requirements.

**`GetGameEventId`**: Template specialization for `Creature`. Searches `mGameEventCreatureGuids` to reverse-lookup the event ID associated with a given creature GUID.

**`GetGameEventId#2`**: Template specialization for `GameObject`. Searches `mGameEventGameobjectGuids` to reverse-lookup the event ID associated with a given game object GUID.

**`GetGameEventId#3`**: Template specialization for `Pool`. Searches `mGameEventSpawnPoolIds` to reverse-lookup the event ID associated with a given pool ID.

**`GameEventMgr`**: Constructor, initializing flags.

**`IsActiveHoliday`**: Checks if any active event corresponds to a specific holiday ID.

**`GetSilithusPVPEventCompleted`**: Accessor for the Silithus PvP event completion flag.

**`SetSilithusPVPEventCompleted`**: Mutator for the Silithus PvP event completion flag.

**`UpdateSilithusPVP`**: Manages the hardcoded Silithus PvP event schedule and announcements.

---

<!-- machine-true, projected from graph.json -->

## Map — GameEventMgr.Main

*Source:* GameEventMgr.cpp, GameEventMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| CheckOneGameEvent | method | — | — | — |
| NextCheck | method | — | ChatHandler.ServerCommands/HandleEventInfoCommand | — |
| StartEvent | method | Log.Main/Out, WorldEvent/Enable | boss_omen/OnFireworkLaunch, ChatHandler.HardcodedEvents/Disable#4, ChatHandler.HardcodedEvents/EnableAndStartEvent, ChatHandler.HardcodedEvents/EnableAndStartEvent#2, ChatHandler.HardcodedEvents/StartLocalBoss, ChatHandler.HardcodedEvents/StartLocalInvasion, ChatHandler.HardcodedEvents/Update, ChatHandler.HardcodedEvents/Update#2, ChatHandler.HardcodedEvents/Update#3, ChatHandler.HardcodedEvents/Update#4, ChatHandler.HardcodedEvents/Update#5, ChatHandler.HardcodedEvents/Update#6, ChatHandler.HardcodedEvents/Update#7, ChatHandler.HardcodedEvents/UpdateHiveColossusEvents, ChatHandler.ServerCommands/HandleEventStartCommand, Map.ScriptCommands/ScriptCommand_GameEvent, scourge_invasion/ChangeZoneEventStatus | — |
| StopEvent | method | Log.Main/Out | boss_omen/OnRemoveFromWorld, ChatHandler.HardcodedEvents/Disable#2, ChatHandler.HardcodedEvents/Disable#3, ChatHandler.HardcodedEvents/Disable#4, ChatHandler.HardcodedEvents/Disable#5, ChatHandler.HardcodedEvents/Disable#6, ChatHandler.HardcodedEvents/DisableAndStopEvent, ChatHandler.HardcodedEvents/DisableAndStopEvent#2, ChatHandler.HardcodedEvents/StopLocalInvasion, ChatHandler.HardcodedEvents/Update, ChatHandler.HardcodedEvents/Update#2, ChatHandler.HardcodedEvents/Update#3, ChatHandler.HardcodedEvents/Update#4, ChatHandler.HardcodedEvents/Update#6, ChatHandler.HardcodedEvents/Update#7, ChatHandler.ServerCommands/HandleEventStopCommand, Map.ScriptCommands/ScriptCommand_GameEvent, scourge_invasion/ChangeZoneEventStatus | — |
| ~GameEventMgr | dtor | — | — | — |
| GetActiveEventList | method | — | ChatHandler.ObjectCommands/HandleGameObjectTargetCommand | — |
| GetEventMap | method | — | ChatHandler.LookupCommands/HandleLookupEventCommand, ChatHandler.ServerCommands/HandleEventDisableCommand, ChatHandler.ServerCommands/HandleEventEnableCommand, ChatHandler.ServerCommands/HandleEventInfoCommand, ChatHandler.ServerCommands/HandleEventListCommand, ChatHandler.ServerCommands/HandleEventStartCommand, ChatHandler.ServerCommands/HandleEventStopCommand | — |
| EnableEvent | method | Database/PExecute#2, Log.Main/Out, WorldEvent/Disable, WorldEvent/Enable | ChatHandler.HardcodedEvents/DisableAndStopEvent, ChatHandler.HardcodedEvents/DisableAndStopEvent#2, ChatHandler.HardcodedEvents/EnableAndStartEvent, ChatHandler.HardcodedEvents/EnableAndStartEvent#2, ChatHandler.ServerCommands/HandleEventDisableCommand, ChatHandler.ServerCommands/HandleEventEnableCommand, npcs_special/OnRemoveFromWorld, npcs_special/ResetVariablesAndDisableWinnerEvents | game_event |
| IsValidEvent | method | — | ChatHandler.HardcodedEvents/Update, ChatHandler.LookupCommands/HandleLookupEventCommand, ChatHandler.ServerCommands/HandleEventDisableCommand, ChatHandler.ServerCommands/HandleEventEnableCommand, ChatHandler.ServerCommands/HandleEventInfoCommand, ChatHandler.ServerCommands/HandleEventListCommand, ChatHandler.ServerCommands/HandleEventStartCommand, ChatHandler.ServerCommands/HandleEventStopCommand, Conditions/IsValid, ScriptMgr/LoadScripts | — |
| IsActiveEvent | method | — | boss_omen/OnFireworkLaunch, boss_omen/OnRemoveFromWorld, ChatHandler.HardcodedEvents/Disable#2, ChatHandler.HardcodedEvents/Disable#3, ChatHandler.HardcodedEvents/Disable#4, ChatHandler.HardcodedEvents/Disable#6, ChatHandler.HardcodedEvents/DisableAndStopEvent, ChatHandler.HardcodedEvents/DisableAndStopEvent#2, ChatHandler.HardcodedEvents/EnableAndStartEvent, ChatHandler.HardcodedEvents/EnableAndStartEvent#2, ChatHandler.HardcodedEvents/ShouldEnable, ChatHandler.HardcodedEvents/StartLocalBoss, ChatHandler.HardcodedEvents/StopLocalInvasion, ChatHandler.HardcodedEvents/Update, ChatHandler.HardcodedEvents/Update#2, ChatHandler.HardcodedEvents/Update#3, ChatHandler.HardcodedEvents/Update#4, ChatHandler.HardcodedEvents/Update#5, ChatHandler.HardcodedEvents/Update#6, ChatHandler.HardcodedEvents/Update#7, ChatHandler.HardcodedEvents/UpdateHiveColossusEvents, ChatHandler.HardcodedEvents/UpdateStageEvents, ChatHandler.LookupCommands/HandleLookupEventCommand, ChatHandler.ServerCommands/HandleEventInfoCommand, ChatHandler.ServerCommands/HandleEventListCommand, ChatHandler.ServerCommands/HandleEventStartCommand, ChatHandler.ServerCommands/HandleEventStopCommand, Conditions/Evaluate, elemental_invasions/JustDied, elemental_invasions/UpdateAI, fireworks_show/UpdateAI, go_scripts/UpdateAI, go_scripts/UpdateAI#3, instance_zulgurub/OnGossipHello_go_table_madness, instance_zulgurub/SetData, npcs_special/CheckTournamentState, npcs_special/GossipHello_npc_kwee_peddlefeet, npcs_special/npc_kwee_peddlefeetAI, Player.Main/SendInitWorldStates, scourge_invasion/ChangeZoneEventStatus, Spell.Effects/EffectScriptEffect, world_event_wareffort/GetActiveTransportEvent, world_event_wareffort/UpdateAI#3, world_event_wareffort/UpdateAI#4 | — |
| IsEnabled | method | Log.Main/Out | ChatHandler.HardcodedEvents/DisableAndStopEvent, ChatHandler.HardcodedEvents/DisableAndStopEvent#2, ChatHandler.HardcodedEvents/EnableAndStartEvent, ChatHandler.HardcodedEvents/EnableAndStartEvent#2, ChatHandler.ServerCommands/HandleEventDisableCommand, ChatHandler.ServerCommands/HandleEventEnableCommand, ChatHandler.ServerCommands/HandleEventStartCommand | — |
| LoadFromDB | method | ChatHandler.HardcodedEvents/LoadHardcodedEvents, Database/PQuery, Database/Query, Field/GetCppString, Field/GetInt16, Field/GetUInt16, Field/GetUInt32, Field/GetUInt64, Field/GetUInt8, GameEventMail/GameEventMail, Log.Main/Out, ObjectMgr/GetCreatureTemplate, ObjectMgr/GetEquipmentTemplate, ObjectMgr/GetQuestTemplate, ObjectMgr/IsExistingCreatureGuid, ObjectMgr/IsExistingCreatureId, PoolManager/CheckEventLinkAndReport, PoolManager/RemoveAutoSpawnForPool, ProgressBar/BarGoLink, ProgressBar/BarGoLink#3, ProgressBar/step, QueryResult/Fetch, QueryResult/GetRowCount, QueryResult/NextRow, QuestDef/SetQuestActiveState, shared_Util/isLeapYear, SpellMgr/GetSpellEntry, SpellMgr/Instance, SpellMgr/IsExistingSpellId, World/GetWowPatch | World/SetInitialWorldSettings | creature, gameobject, game_event, game_event_creature, game_event_creature_data, game_event_gameobject, game_event_mail, game_event_quest |
| Initialize | method | Database/Execute#2, Database/Query, Field/GetUInt16, Log.Main/Out, QueryResult/Fetch, QueryResult/NextRow | World/SetInitialWorldSettings | game_event_status |
| Initialize#2 | method | PoolManager/InitSpawnPool | MapPersistentStateMgr/InitPools | — |
| Update | method | Log.Main/Out, WorldEvent/GetNextUpdateDelay, WorldEvent/Update | ChatHandler.HardcodedEvents/HandleWarEffortInfoCommand, ChatHandler.HardcodedEvents/HandleWarEffortSetGongTimeCommand, ChatHandler.HardcodedEvents/HandleWarEffortSetStageCommand, World/Update | — |
| UnApplyEvent | method | Database/PExecute#2, Log.Main/Out | — | game_event_status |
| ApplyNewEvent | method | Database/PExecute#2, Log.Main/Out, World/getConfig, World/SendWorldText | — | game_event_status |
| GameEventSpawn | method | Creature.Main/SpawnInMaps, GameObject/SpawnInMaps, Log.Main/Out, ObjectMgr/AddCreatureToGrid, ObjectMgr/AddGameobjectToGrid, ObjectMgr/GetCreatureData, ObjectMgr/GetGOData, PoolManager/IsPartOfAPool, PoolManager/IsPartOfAPool#2, PoolManager/SetExcludeObject, PoolManager/SetExcludeObject#2, PoolManager/SpawnPoolInMaps | — | — |
| GameEventUnspawn | method | Creature.Main/AddToRemoveListInMaps, GameObject/AddToRemoveListInMaps, Log.Main/Out, ObjectMgr/GetCreatureData, ObjectMgr/GetGOData, ObjectMgr/RemoveCreatureFromGrid, ObjectMgr/RemoveGameobjectFromGrid, PoolManager/DespawnPoolInMaps, PoolManager/IsPartOfAPool, PoolManager/IsPartOfAPool#2, PoolManager/SetExcludeObject, PoolManager/SetExcludeObject#2 | — | — |
| GetCreatureUpdateDataForActiveEvent | method | — | Creature.Main/LoadFromDB, Creature.Main/Update | — |
| GameEventUpdateCreatureDataInMapsWorker | ctor | — | — | — |
| operator() | method | Creature.Main/ApplyGameEventSpells, Creature.Main/GetOriginalEntry, Creature.Main/UpdateEntry, Map.Main/GetCreature | — | — |
| UpdateCreatureData | method | CreatureData/GetObjectGuid, ObjectMgr/GetCreatureData | — | — |
| UpdateEventQuests | method | ObjectMgr/GetQuestTemplate, QuestDef/SetQuestActiveState | — | — |
| SendEventMails | method | game_Mail_Mail/MailDraft#2, game_Mail_Mail/MailSender#2, MassMailMgr/AddMassMailTask#2, MassMailMgr/AddMassMailTask#3 | — | characters |
| GetGameEventId | method | — | — | — |
| GetGameEventId#2 | method | — | — | — |
| GetGameEventId#3 | method | — | — | — |
| GameEventMgr | ctor | — | — | — |
| IsActiveHoliday | method | — | BattleGroundMgr/IsBgWeekend, Conditions/Evaluate | — |
| GetSilithusPVPEventCompleted | method | — | — | — |
| SetSilithusPVPEventCompleted | method | — | — | — |
| UpdateSilithusPVP | method | Log.Main/Out, World/SendGlobalText | — | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `characters`: guid int(11) unsigned PK, account int(11) unsigned, name varchar(12), race tinyint(3) unsigned, class tinyint(3) unsigned, gender tinyint(3) unsigned, skin tinyint(3) unsigned, face tinyint(3) unsigned, hair_style tinyint(3) unsigned, hair_color tinyint(3) unsigned, facial_hair tinyint(3) unsigned, level tinyint(3) unsigned, xp int(10) unsigned, money int(10) unsigned, character_flags int(10) unsigned, zone int(11) unsigned, map int(11) unsigned, instance int(11) unsigned, position_x float, position_y float, position_z float, orientation float, transport_guid bigint(20) unsigned, transport_x float, transport_y float, transport_z float, transport_o float, known_taxi_mask longtext?, current_taxi_path text?, online tinyint(3) unsigned, played_time_total int(11) unsigned, played_time_level int(11) unsigned, create_time bigint(20) unsigned, logout_time bigint(20) unsigned, rest_bonus float, reset_talents_multiplier int(11) unsigned, reset_talents_time bigint(20) unsigned, death_expire_time bigint(20) unsigned, stable_slots tinyint(1) unsigned, bank_bag_slots tinyint(1) unsigned, extra_flags int(11) unsigned, honor_rank_points float, honor_highest_rank int(11) unsigned, honor_standing int(11) unsigned, honor_last_week_hk int(11) unsigned, honor_last_week_cp float, honor_stored_hk int(11), honor_stored_dk int(11), watched_faction int(11), drunk smallint(5) unsigned, health int(10) unsigned, power1 int(10) unsigned, power2 int(10) unsigned, power3 int(10) unsigned, power4 int(10) unsigned, power5 int(10) unsigned, explored_zones longtext?, equipment_cache longtext?, ammo_id int(10) unsigned, action_bars tinyint(3) unsigned, deleted_account int(11) unsigned?, deleted_name varchar(12)?, deleted_time bigint(20)?, world_phase_mask int(11)?
- `creature`: guid int(10) unsigned PK, id mediumint(8) unsigned, id2 mediumint(8) unsigned, id3 mediumint(8) unsigned, id4 mediumint(8) unsigned, id5 mediumint(8) unsigned, map smallint(5) unsigned, position_x float, position_y float, position_z float, orientation float, spawntimesecsmin int(10) unsigned, spawntimesecsmax int(10) unsigned, wander_distance float, health_percent float, mana_percent float unsigned, movement_type tinyint(3) unsigned, spawn_flags int(10) unsigned, visibility_mod float?, patch_min tinyint(3) unsigned, patch_max tinyint(3) unsigned
- `game_event`: entry mediumint(8) unsigned PK, start_time timestamp, end_time timestamp, occurence bigint(20) unsigned, length bigint(20) unsigned, holiday mediumint(8) unsigned, description varchar(255)?, hardcoded tinyint(3), disabled tinyint(3), patch_min tinyint(3) unsigned, patch_max tinyint(3) unsigned
- `game_event_creature`: guid int(10) unsigned PK, event smallint(6) PK
- `game_event_creature_data`: guid int(10) unsigned PK, patch tinyint(3) unsigned PK, entry_id mediumint(8) unsigned, display_id mediumint(8) unsigned, equipment_id mediumint(8) unsigned, spell_start smallint(5) unsigned, spell_end smallint(5) unsigned, event smallint(5) unsigned PK
- `game_event_gameobject`: guid int(10) unsigned PK, event smallint(6) PK
- `game_event_mail`: event smallint(6) PK, raceMask mediumint(8) unsigned PK, quest mediumint(8) unsigned PK, mailTemplateId mediumint(8) unsigned, senderEntry mediumint(8) unsigned
- `game_event_quest`: quest mediumint(8) unsigned PK, event smallint(5) unsigned PK, patch_min tinyint(3) unsigned
- `game_event_status`: event smallint(6) unsigned PK
- `gameobject`: guid int(10) unsigned PK, id mediumint(8) unsigned, map smallint(5) unsigned, position_x float, position_y float, position_z float, orientation float, rotation0 float, rotation1 float, rotation2 float, rotation3 float, spawntimesecsmin int(11), spawntimesecsmax int(11), animprogress tinyint(3) unsigned, state tinyint(3) unsigned, spawn_flags int(10) unsigned, visibility_mod float?, patch_min tinyint(3) unsigned, patch_max tinyint(3) unsigned

*`?` = nullable, `PK` = primary key column.*


---

<!-- verify: failed-members | invented: operator -->
