<!-- provenance: boundary-bleed -->
# ChatHandler.HardcodedEvents

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ChatHandler.HardcodedEvents

## Purpose & Responsibilities

This unit implements the server-side logic for seven specific, persistent world events in the WoW server emulation: **Elemental Invasion**, **Dragons of Nightmare**, **Darkmoon Faire**, **Fireworks Show**, **Toasting Goblets**, **Scourge Invasion**, and **The Gates of Ahn'Qiraj War Effort**.

These events are termed "hardcoded" because their progression, timing, and state management are driven by C++ logic and saved variables rather than purely by the `game_event` database table structure. The unit provides:
1.  **Event Controllers:** Classes inheriting from `WorldEvent` that manage the lifecycle (start, stop, update) of each event.
2.  **State Persistence:** Heavy reliance on `ObjectMgr` saved variables to track timers, stages, and progress across server restarts.
3.  **Admin Commands:** A suite of `ChatHandler` commands (implemented in this file as part of the `ChatHandler` class partial) allowing Game Masters to inspect, debug, and manually manipulate the state of the War Effort event.
4.  **Integration Point:** A factory method (`LoadHardcodedEvents`) that instantiates these controllers and registers them with the `GameEventMgr`.

## Member-by-Member Behavior

### Elemental Invasion
Manages the periodic invasion of elemental rifts and bosses. It tracks four elements (Fire, Air, Earth, Water), each with a rift phase and a boss phase.

*   **Update#3**: The main loop. If the main invasion event is inactive, it checks if the global invasion timer has expired. If so, it starts the main event and initiates local invasions/bosses for all four elements. If the main event is active, it checks boss states. If all bosses are dead and their despawn delays have elapsed, it stops the main event, sets a new random invasion timer (2–4 days), and resets state.
*   **Enable#3**: Empty placeholder.
*   **Disable#3**: Stops all active rift and boss events for all elements and calls `ResetThings`.
*   **StartLocalInvasion**: Starts the rift event for a specific element if the current stage is before the boss phase.
*   **StartLocalBoss**: Starts the boss event for a specific element if the stage is "Boss Down" (with delay) or "Boss". This ensures the boss event remains active during the despawn delay so the boss doesn't respawn dead.
*   **StopLocalInvasion**: Handles the transition when a boss dies. It stops the rift event. If a despawn delay is active, it decrements the delay counter. If the delay reaches zero, it stops the boss event.
*   **ResetThings**: Resets all saved variables for delays, kills, and stages for all four elements. It also clears the `creature_respawn` table entries for the boss GUIDs to ensure clean respawns.

### Dragons of Nightmare
Manages the spawn, fight, and respawn of four nightmare dragons.

*   **Update#2**: Loads dragon GUIDs. If the event is active, it counts alive dragons. If all are dead, it manages a stop delay. Once the delay expires, it sets a new respawn timer (4–7 days), permutes the dragon order for the next spawn, and stops the event. If the event is inactive, it checks if the respawn timer has expired and starts the event if so.
*   **Enable#2**: Empty placeholder.
*   **Disable#2**: Stops the event if active and resets the request update variable.
*   **CheckSingleVariable**: Helper to ensure a saved variable exists; if not, it initializes it with a default value and logs an error.
*   **GetAliveCountAndUpdateRespawnTime**: Iterates through dragon GUIDs, finds their creature objects, and updates their respawn times in the persistent state if dead, or increments the alive count if alive.
*   **LoadDragons**: Retrieves the GUIDs for the four nightmare dragons from the object manager.
*   **PermutateDragons**: Shuffles the order of the four dragons and saves the new permutation to saved variables for the next spawn cycle.

### Darkmoon Faire
Manages the monthly rotation of the Darkmoon Faire, including installation periods and active faire weeks.

*   **Update**: Determines the current Darkmoon state (None, Installation, Active) and starts/stops the corresponding game events.
*   **Enable**: Empty placeholder.
*   **Disable**: Empty placeholder.
*   **FindMonthFirstMonday**: Calculates the day of the month for the first Monday of the current month. Also determines if the faire is Alliance or Horde based on the month parity.
*   **GetDarkmoonState**: Uses the current time to determine if the faire is in an installation period, active period, or not present.

### Fireworks Show
Manages fireworks displays during specific holidays (New Year, Lunar New Year, July 4th, September 30th).

*   **Update#4**: Checks if a holiday event is active. If so, it toggles the fireworks event on/off at the beginning of each hour (between 6 PM and 6 AM).
*   **Enable#4**: Empty placeholder.
*   **Disable#4**: Stops the fireworks event. If New Year/Lunar New Year is active, it may start the "Toasting Goblets" event.
*   **IsHourBeginning**: Checks if the current minute is within the specified threshold (default 10 mins) of the top of the hour, and if the time is between 6 PM and 6 AM.

### Toasting Goblets
Manages the "Toasting Goblets" event that follows the fireworks during New Year/Lunar New Year.

*   **Update#6**: Starts or stops the event based on whether it should be enabled.
*   **Enable#6**: Empty placeholder.
*   **Disable#6**: Stops the event.
*   **ShouldEnable**: Returns true if New Year/Lunar New Year is active, the time is between 6 PM and 6 AM, and the minute is between 10 and 20 past the hour.

### Scourge Invasion
Manages the complex Scourge Invasion event, involving zone attacks (Mouth of Kel'Thuzad) and city attacks (Pallid Horror).

*   **ScourgeInvasionEvent (ctor)**: Initializes saved variables for attack times, victory counts, and remaining necropolises. Sets up data structures for invasion zones (Winterspring, Tanaris, etc.) and city attacks (Undercity, Stormwind).
*   **Update#5**: The main loop. Ensures the main Scourge Invasion event is active. Handles city attacks for Undercity and Stormwind. Manages the loading state of invasion zones. Calls `HandleActiveZone` for each invasion point, `HandleDefendedZones` to manage milestone events, `UpdateWorldState` to broadcast status to players, and `LogNextZoneTime`.
*   **GetZoneTime**: Retrieves the next attack time for a specific zone from saved variables.
*   **LogNextZoneTime**: Logs the time until the next zone attack.
*   **EnableAndStartEvent**: Enables and starts a game event if it's not already active.
*   **DisableAndStopEvent**: Stops and disables a game event.
*   **HandleDefendedZones**: Manages milestone events (50, 100, 150 victories) based on the total victory count.
*   **Enable#5**: Initializes the loading state for all invasion zones and updates the world state.
*   **Disable#5**: Removes summoned creatures (Mouths and Pallids), resets all attack timers and remaining counts to zero, and stops all related game events.
*   **GetNextUpdateDelay**: Returns 20 seconds, the interval for the update loop.
*   **GetMap**: Helper to find the map object for a given map ID and position.
*   **HandleActiveZone**: Manages the state of a single invasion zone. If no Mouth is present, it checks if a new invasion should start. If a Mouth is present and all necropolises are destroyed, it increments the victory count, sets the next attack time, and triggers the zone stop script.
*   **HandleActiveCity**: Manages city attacks. If no Pallid is present and the timer has expired, it starts a new city attack.
*   **OnEnable**: Called when the event system enables the Scourge Invasion. It checks if an invasion was in progress and resumes it, or starts a new one.
*   **StartNewCityAttackIfTime**: Spawns a Pallid Horror in a city if the timer allows.
*   **StartNewInvasionIfTime**: Spawns a Mouth of Kel'Thuzad in a zone if the timer allows and conditions are met (not the same zone as last attack, not too many active zones).
*   **ResumeInvasion**: Attempts to resume an invasion in a zone that had remaining necropolises before a server restart.
*   **SummonPallid**: Spawns a Pallid Horror or Patchwork Terror at a specified location in a city and assigns it a waypoint path.
*   **SummonMouth**: Spawns a Mouth of Kel'Thuzad at a specified location in a zone and triggers the zone start script.
*   **isValidZoneId**: Checks if a zone ID is one of the valid Scourge Invasion zones.
*   **isActiveZone**: Checks if a Mouth of Kel'Thuzad is currently present in a zone.
*   **GetActiveZones**: Counts how many zones currently have an active Mouth of Kel'Thuzad.
*   **GetCityZone**: Retrieves the `CityAttack` data structure for a given zone ID.
*   **GetInvasionZone**: Retrieves the `InvasionZone` data structure for a given zone ID.
*   **UpdateWorldState**: Broadcasts the current state of the Scourge Invasion (remaining necropolises, victory count) to all online players via world state updates.

### War Effort (Gates of Ahn'Qiraj)
Manages the multi-stage War Effort event, from resource collection to the final battle.

*   **WarEffortEvent (ctor)**: Initializes the event and loads current variables.
*   **Update#7**: The main loop. Updates variables and stage-specific events. Handles transitions between stages (Collection, Ready, Move 1-5, Gong Wait, Gong Rung, Battle, CH Attack, Final Battle, Complete). It broadcasts messages and triggers sub-events as appropriate.
*   **UpdateWarEffortCollection**: Checks resource collection progress. If all objectives are complete, it transitions to the "Ready" stage. It also handles auto-completing progress periodically.
*   **UpdateStageTransitionTime**: Records the current time as the last stage transition time.
*   **IncrementWarEffortTransition**: Moves to the next stage (used for the "Move" stages).
*   **BeginWar**: Ensures war-related events are active.
*   **CompleteWarEffort**: Stops battle-related events and sets the stage to "Complete".
*   **UpdateStageEvents**: Activates/deactivates game events based on the current stage, using a predefined matrix of events per stage.
*   **Enable#7**: Empty placeholder.
*   **Disable#7**: Empty placeholder.
*   **GetNextUpdateDelay#2**: Returns a shorter delay (10s) during critical stages (Gong Wait/Rung) for faster response, otherwise uses the standard delay.
*   **EnableAndStartEvent#2**: Enables and starts a game event.
*   **DisableAndStopEvent#2**: Stops and disables a game event.
*   **UpdateHiveColossusEvents**: Starts events for Hive Colossus battles if the corresponding reward flags are set.
*   **UpdateVariables**: Loads the current stage, transition time, gong time, and auto-complete time from saved variables.

### Admin Commands (ChatHandler)
The following members are implemented in this file as part of the `ChatHandler` class partial. They provide commands for Game Masters to interact with the War Effort event.

*   **HandleWarEffortInfoCommand**: Displays detailed information about the current War Effort stage, timers, and resource collection progress.
*   **HandleWarEffortSetGongTimeCommand**: Sets the gong ring time manually.
*   **HandleWarEffortSetStageCommand**: Sets the War Effort stage manually.
*   **HandleWarEffortGetResource**: Retrieves the current count and required amount for a specific resource.
*   **HandleWarEffortSetResource**: Sets the current count for a specific resource.

### Utility & Factory

*   **WarEffortStageToString**: Converts a War Effort stage integer to a human-readable string.
*   **LoadHardcodedEvents**: Instantiates all hardcoded event controllers and adds them to the event list.

## Cross-Unit Boundaries

*   **GameEventMgr.Main**: Heavily used by all event controllers to start, stop, enable, disable, and check the status of game events. This is the primary interface for integrating with the server's event system.
*   **ObjectMgr**: Used extensively to get and set saved variables, which persist event state across server restarts. Also used to retrieve creature and item prototypes.
*   **MapManager / Map**: Used to find map objects and retrieve creature instances by GUID. Essential for checking if bosses/dragons/Mouths/Pallids are alive or present.
*   **Log.Main**: Used for logging errors and informational messages, particularly when creatures are not found or events fail to start.
*   **shared_Util**: Used for random number generation (`urand`) and time formatting utilities (`secsToTimeString`, `TimeToTimestampStr`).
*   **ChatHandler.Chat**: The admin commands are implemented as methods of `ChatHandler`, using its utilities for parsing arguments (`ExtractUInt32`) and sending messages (`PSendSysMessage`).
*   **World**: Used to send broadcast messages to all players and to read configuration values (e.g., auto-complete period).
*   **world_event_wareffort**: Specific helper functions for retrieving War Effort stock information and auto-completing progress.
*   **CreatureAI / Creature.MotionMaster**: Used to trigger scripts on bosses/Mouths and to assign waypoint paths to Pallids.
*   **ObjectAccessor / Player.Main**: Used in `UpdateWorldState` to iterate over all online players and send world state updates.

## Data Model

*   **creature_respawn**: Used by `ElementalInvasion::ResetThings` to delete respawn entries for boss GUIDs, ensuring they respawn correctly when the event restarts. Columns: `guid` (int, PK), `respawn_time` (bigint), `instance` (mediumint, PK), `map` (int, nullable).

Most other state is managed via `ObjectMgr` saved variables, which are not stored in standard SQL tables queried by this unit.

## Notable Implementation Details

*   **Saved Variable Dependency**: The entire logic relies on `ObjectMgr` saved variables. If these are corrupted or missing, events may behave unexpectedly. `DragonsOfNightmare::CheckSingleVariable` attempts to mitigate this by initializing missing variables.
*   **Respawn Time Manipulation**: `DragonsOfNightmare::GetAliveCountAndUpdateRespawnTime` directly manipulates the respawn time of dead dragons in the persistent state, setting it to `max` while the event is active and to a future time when the event ends.
*   **Stateful Transitions**: The War Effort event has a complex state machine. The `UpdateStageEvents` function uses a static matrix to determine which sub-events should be active for each stage, ensuring consistency.
*   **Manual Intervention**: The admin commands allow GMs to bypass normal progression, which is useful for testing or fixing stuck events. However, this can lead to inconsistent state if not used carefully.
*   **Time-Based Logic**: Many events rely on `time(nullptr)` for scheduling. This means event timing is tied to real-world time, not game time.
*   **Map Loading Assumptions**: `ScourgeInvasionEvent` assumes that maps for invasion zones are loaded. If a map is not loaded, it logs an error and retries on the next update.
*   **Hardcoded GUIDs and Positions**: Creature GUIDs and positions are hardcoded in the header and constructor. Any changes to the world database (e.g., moving a boss) would require updating this code.

## Member Reference

**Update#3** (ElementalInvasion): Main update loop for Elemental Invasion; checks timers, starts/stops rift and boss events, and resets state when all bosses are dead.
**Enable#3** (ElementalInvasion): Empty placeholder.
**Disable#3** (ElementalInvasion): Stops all active Elemental Invasion events and resets state.
**StartLocalInvasion** (ElementalInvasion): Starts the rift event for a specific element if the stage permits.
**StartLocalBoss** (ElementalInvasion): Starts the boss event for a specific element if the stage permits.
**StopLocalInvasion** (ElementalInvasion): Stops rift/boss events for an element, handling despawn delays.
**ResetThings** (ElementalInvasion): Resets saved variables and clears `creature_respawn` entries for bosses.
**Update#2** (DragonsOfNightmare): Main update loop for Dragons of Nightmare; checks dragon alive counts, manages respawn timers, and permutes dragon order.
**Enable#2** (DragonsOfNightmare): Empty placeholder.
**Disable#2** (DragonsOfNightmare): Stops the event if active and resets variables.
**CheckSingleVariable** (DragonsOfNightmare): Ensures a saved variable exists, initializing it if missing.
**GetAliveCountAndUpdateRespawnTime** (DragonsOfNightmare): Counts alive dragons and updates respawn times for dead ones.
**WarEffortStageToString** (Global): Converts War Effort stage integer to string.
**LoadDragons** (DragonsOfNightmare): Retrieves GUIDs for the four nightmare dragons.
**PermutateDragons** (DragonsOfNightmare): Shuffles dragon order and saves to variables.
**Update** (DarkmoonFaire): Main update loop for Darkmoon Faire; starts/stops events based on calendar state.
**Enable** (DarkmoonFaire): Empty placeholder.
**Disable** (DarkmoonFaire): Empty placeholder.
**FindMonthFirstMonday** (DarkmoonFaire): Calculates the first Monday of the month and faire faction.
**GetDarkmoonState** (DarkmoonFaire): Determines current Darkmoon Faire state (None, Install, Active).
**Update#4** (FireworksShow): Main update loop for Fireworks; toggles event based on hour and holiday status.
**Enable#4** (FireworksShow): Empty placeholder.
**Disable#4** (FireworksShow): Stops fireworks and potentially starts Toasting Goblets.
**IsHourBeginning** (FireworksShow): Checks if current time is near the top of the hour (6PM-6AM).
**Update#6** (ToastingGoblets): Main update loop for Toasting Goblets; starts/stops based on time conditions.
**Enable#6** (ToastingGoblets): Empty placeholder.
**Disable#6** (ToastingGoblets): Stops the Toasting Goblets event.
**ShouldEnable** (ToastingGoblets): Checks if Toasting Goblets should be active based on time and holiday.
**ScourgeInvasionEvent** (ctor): Initializes Scourge Invasion variables, zones, and city attack data.
**LogNextZoneTime** (ScourgeInvasionEvent): Logs time until next zone attack.
**GetZoneTime** (ScourgeInvasionEvent): Gets next attack time for a zone from variables.
**EnableAndStartEvent** (ScourgeInvasionEvent): Enables and starts a game event.
**DisableAndStopEvent** (ScourgeInvasionEvent): Stops and disables a game event.
**HandleDefendedZones** (ScourgeInvasionEvent): Manages milestone events based on victory count.
**Update#5** (ScourgeInvasionEvent): Main update loop for Scourge Invasion; handles zones, cities, and world state.
**GetNextUpdateDelay** (ScourgeInvasionEvent): Returns 20-second update interval.
**Enable#5** (ScourgeInvasionEvent): Initializes zone loading states and updates world state.
**Disable#5** (ScourgeInvasionEvent): Removes summoned creatures, resets variables, and stops events.
**GetMap** (ScourgeInvasionEvent): Finds map object for a given ID and position.
**HandleActiveZone** (ScourgeInvasionEvent): Manages state of a single invasion zone (Mouth of Kel'Thuzad).
**HandleActiveCity** (ScourgeInvasionEvent): Manages state of a city attack (Pallid Horror).
**OnEnable** (ScourgeInvasionEvent): Resumes or starts new invasions when event is enabled.
**StartNewCityAttackIfTime** (ScourgeInvasionEvent): Spawns Pallid Horror if timer allows.
**StartNewInvasionIfTime** (ScourgeInvasionEvent): Spawns Mouth of Kel'Thuzad if timer allows.
**ResumeInvasion** (ScourgeInvasionEvent): Resumes an invasion in a zone with remaining necropolises.
**SummonPallid** (ScourgeInvasionEvent): Spawns and waypoints a Pallid Horror.
**SummonMouth** (ScourgeInvasionEvent): Spawns and scripts a Mouth of Kel'Thuzad.
**isValidZoneId** (ScourgeInvasionEvent): Checks if zone ID is valid for Scourge Invasion.
**isActiveZone** (ScourgeInvasionEvent): Checks if a Mouth is present in a zone.
**GetActiveZones** (ScourgeInvasionEvent): Counts zones with active Mouths.
**GetCityZone** (ScourgeInvasionEvent): Gets CityAttack data for a zone ID.
**GetInvasionZone** (ScourgeInvasionEvent): Gets InvasionZone data for a zone ID.
**UpdateWorldState** (ScourgeInvasionEvent): Broadcasts invasion status to all players.
**WarEffortEvent** (ctor): Initializes War Effort event and loads variables.
**UpdateVariables** (WarEffortEvent): Loads current stage and timers from variables.
**Update#7** (WarEffortEvent): Main update loop for War Effort; handles stage transitions and events.
**UpdateWarEffortCollection** (WarEffortEvent): Checks resource progress and auto-completes if needed.
**UpdateStageTransitionTime** (WarEffortEvent): Records current time as last transition time.
**IncrementWarEffortTransition** (WarEffortEvent): Moves to next stage.
**BeginWar** (WarEffortEvent): Ensures war events are active.
**CompleteWarEffort** (WarEffortEvent): Stops battle events and marks event complete.
**UpdateStageEvents** (WarEffortEvent): Activates/deactivates events based on current stage matrix.
**Enable#7** (WarEffortEvent): Empty placeholder.
**Disable#7** (WarEffortEvent): Empty placeholder.
**GetNextUpdateDelay#2** (WarEffortEvent): Returns 10s delay for critical stages, standard otherwise.
**EnableAndStartEvent#2** (WarEffortEvent): Enables and starts a game event.
**DisableAndStopEvent#2** (WarEffortEvent): Stops and disables a game event.
**UpdateHiveColossusEvents** (WarEffortEvent): Starts Hive Colossus events if rewards are set.
**HandleWarEffortInfoCommand** (ChatHandler): Displays War Effort status and resource progress.
**HandleWarEffortSetGongTimeCommand** (ChatHandler): Sets gong ring time manually.
**HandleWarEffortSetStageCommand** (ChatHandler): Sets War Effort stage manually.
**HandleWarEffortGetResource** (ChatHandler): Gets resource count and requirement.
**HandleWarEffortSetResource** (ChatHandler): Sets resource count manually.
**LoadHardcodedEvents** (GameEventMgr): Instantiates all hardcoded event controllers.

---

<!-- machine-true, projected from graph.json -->

## Map — ChatHandler.HardcodedEvents

*Source:* HardcodedEvents.cpp, HardcodedEvents.h, Chat.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Update#3 | method | GameEventMgr.Main/IsActiveEvent, GameEventMgr.Main/StartEvent, GameEventMgr.Main/StopEvent, ObjectMgr/GetSavedVariable, ObjectMgr/SetSavedVariable, shared_Util/urand | — | — |
| Enable#3 | method | — | — | — |
| Disable#3 | method | GameEventMgr.Main/IsActiveEvent, GameEventMgr.Main/StopEvent | — | — |
| StartLocalInvasion | method | GameEventMgr.Main/StartEvent | — | — |
| StartLocalBoss | method | GameEventMgr.Main/IsActiveEvent, GameEventMgr.Main/StartEvent | — | — |
| StopLocalInvasion | method | GameEventMgr.Main/IsActiveEvent, GameEventMgr.Main/StopEvent, ObjectMgr/SetSavedVariable | — | — |
| ResetThings | method | Database/PExecute#2, ObjectMgr/SetSavedVariable | — | creature_respawn |
| Update#2 | method | GameEventMgr.Main/IsActiveEvent, GameEventMgr.Main/StartEvent, GameEventMgr.Main/StopEvent, Log.Main/Out, ObjectMgr/SetSavedVariable, shared_Util/urand | — | — |
| Enable#2 | method | — | — | — |
| Disable#2 | method | GameEventMgr.Main/IsActiveEvent, GameEventMgr.Main/StopEvent, ObjectMgr/SetSavedVariable | — | — |
| CheckSingleVariable | method | Log.Main/Out, ObjectMgr/GetSavedVariable, ObjectMgr/SetSavedVariable | — | — |
| GetAliveCountAndUpdateRespawnTime | method | Log.Main/Out, Map.Main/GetCreature, Map.Main/GetPersistentState, MapManager/FindMap, MapManager/GetContinentInstanceId, MapPersistentStateMgr/SaveCreatureRespawnTime, ObjectGuid/GetCounter, ObjectMgr/GetCreatureData, Unit.Main/IsDead | — | — |
| WarEffortStageToString | function | — | — | — |
| LoadDragons | method | Log.Main/Out, ObjectGuid/IsEmpty, ObjectMgr/GetOneCreatureByEntry | — | — |
| PermutateDragons | method | ObjectMgr/SetSavedVariable | — | — |
| Update | method | GameEventMgr.Main/IsActiveEvent, GameEventMgr.Main/IsValidEvent, GameEventMgr.Main/StartEvent, GameEventMgr.Main/StopEvent | — | — |
| Enable | method | — | — | — |
| Disable | method | — | — | — |
| FindMonthFirstMonday | method | — | — | — |
| GetDarkmoonState | method | — | — | — |
| Update#4 | method | GameEventMgr.Main/IsActiveEvent, GameEventMgr.Main/StartEvent, GameEventMgr.Main/StopEvent | — | — |
| Enable#4 | method | — | — | — |
| Disable#4 | method | GameEventMgr.Main/IsActiveEvent, GameEventMgr.Main/StartEvent, GameEventMgr.Main/StopEvent | — | — |
| IsHourBeginning | method | — | — | — |
| Update#6 | method | GameEventMgr.Main/IsActiveEvent, GameEventMgr.Main/StartEvent, GameEventMgr.Main/StopEvent | — | — |
| Enable#6 | method | — | — | — |
| Disable#6 | method | GameEventMgr.Main/IsActiveEvent, GameEventMgr.Main/StopEvent | — | — |
| ShouldEnable | method | GameEventMgr.Main/IsActiveEvent | — | — |
| ScourgeInvasionEvent | ctor | ObjectMgr/InitSavedVariable, Position/Position#2, WorldEvent/WorldEvent | — | — |
| LogNextZoneTime | method | Log.Main/Out, Map.Main/GetCreature, ObjectMgr/GetSavedVariable | — | — |
| GetZoneTime | method | ObjectMgr/GetSavedVariable | — | — |
| EnableAndStartEvent | method | GameEventMgr.Main/EnableEvent, GameEventMgr.Main/IsActiveEvent, GameEventMgr.Main/IsEnabled, GameEventMgr.Main/StartEvent | — | — |
| DisableAndStopEvent | method | GameEventMgr.Main/EnableEvent, GameEventMgr.Main/IsActiveEvent, GameEventMgr.Main/IsEnabled, GameEventMgr.Main/StopEvent | — | — |
| HandleDefendedZones | method | ObjectMgr/GetSavedVariable | — | — |
| Update#5 | method | GameEventMgr.Main/IsActiveEvent, GameEventMgr.Main/StartEvent, ObjectMgr/GetSavedVariable | — | — |
| GetNextUpdateDelay | method | — | — | — |
| Enable#5 | method | — | — | — |
| Disable#5 | method | Creature.Main/RemoveFromWorld, GameEventMgr.Main/StopEvent, Map.Main/GetCreature, ObjectGuid/operator!, ObjectMgr/SetSavedVariable, WorldObject.Object/DeleteLater | — | — |
| GetMap | method | Log.Main/Out, MapManager/FindMap, MapManager/GetContinentInstanceId | — | — |
| HandleActiveZone | method | Creature.Main/AI, CreatureAI/OnScriptEventHappened, Log.Main/Out, Map.Main/GetCreature, ObjectMgr/GetSavedVariable, ObjectMgr/SetSavedVariable, shared_Util/urand | — | — |
| HandleActiveCity | method | Map.Main/GetCreature, ObjectMgr/GetSavedVariable | — | — |
| OnEnable | method | Log.Main/Out, ObjectMgr/GetSavedVariable, ObjectMgr/SetSavedVariable | — | — |
| StartNewCityAttackIfTime | method | Log.Main/Out, ObjectMgr/GetSavedVariable, shared_Util/urand | — | — |
| StartNewInvasionIfTime | method | Log.Main/Out, ObjectMgr/GetSavedVariable, ObjectMgr/SetSavedVariable | — | — |
| ResumeInvasion | method | Log.Main/Out | — | — |
| SummonPallid | method | Creature.Main/RemoveFromWorld, Creature.MotionMaster/MoveWaypoint, Log.Main/Out, Map.Main/GetCreature, MotionMaster/Clear, Object/GetObjectGuid, Unit.Main/GetMotionMaster, WorldObject.Object/GetZoneId, WorldObject.Object/SummonCreature | — | — |
| SummonMouth | method | Creature.Main/AI, Creature.Main/RemoveFromWorld, CreatureAI/OnScriptEventHappened, Log.Main/Out, Map.Main/GetCreature, Object/GetObjectGuid, WorldObject.Object/SummonCreature | — | — |
| isValidZoneId | method | — | — | — |
| isActiveZone | method | Log.Main/Out, Map.Main/GetCreature | — | — |
| GetActiveZones | method | Log.Main/Out, Map.Main/GetCreature | — | — |
| GetCityZone | method | Log.Main/Out | — | — |
| GetInvasionZone | method | Log.Main/Out | — | — |
| UpdateWorldState | method | Object/IsInWorld, ObjectAccessor/GetPlayers, ObjectMgr/GetSavedVariable, Player.Main/SendUpdateWorldState | — | — |
| WarEffortEvent | ctor | WorldEvent/WorldEvent | — | — |
| UpdateVariables | method | ObjectMgr/GetSavedVariable | — | — |
| Update#7 | method | GameEventMgr.Main/IsActiveEvent, GameEventMgr.Main/StartEvent, GameEventMgr.Main/StopEvent, Log.Main/Out, ObjectMgr/SetSavedVariable, World/SendBroadcastTextToWorld | — | — |
| UpdateWarEffortCollection | method | ObjectMgr/SetSavedVariable, World/getConfig#4, world_event_wareffort/AutoCompleteWarEffortProgress, world_event_wareffort/GetWarEffortStockInfo | — | — |
| UpdateStageTransitionTime | method | ObjectMgr/SetSavedVariable | — | — |
| IncrementWarEffortTransition | method | — | — | — |
| BeginWar | method | — | — | — |
| CompleteWarEffort | method | ObjectMgr/SetSavedVariable | — | — |
| UpdateStageEvents | method | GameEventMgr.Main/IsActiveEvent, Log.Main/Out | — | — |
| Enable#7 | method | — | — | — |
| Disable#7 | method | — | — | — |
| GetNextUpdateDelay#2 | method | — | — | — |
| EnableAndStartEvent#2 | method | GameEventMgr.Main/EnableEvent, GameEventMgr.Main/IsActiveEvent, GameEventMgr.Main/IsEnabled, GameEventMgr.Main/StartEvent | — | — |
| DisableAndStopEvent#2 | method | GameEventMgr.Main/EnableEvent, GameEventMgr.Main/IsActiveEvent, GameEventMgr.Main/IsEnabled, GameEventMgr.Main/StopEvent | — | — |
| UpdateHiveColossusEvents | method | GameEventMgr.Main/IsActiveEvent, GameEventMgr.Main/StartEvent, ObjectMgr/GetSavedVariable | — | — |
| HandleWarEffortInfoCommand | method | ChatHandler.Chat/GetItemLink, ChatHandler.Chat/PSendSysMessage, GameEventMgr.Main/Update, ObjectMgr/GetItemPrototype, ObjectMgr/GetSavedVariable, shared_Util/secsToTimeString, shared_Util/TimeToTimestampStr, World/getConfig#4, world_event_wareffort/GetWarEffortStockInfo | — | — |
| HandleWarEffortSetGongTimeCommand | method | ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/PSendSysMessage, GameEventMgr.Main/Update, ObjectMgr/SetSavedVariable, shared_Util/TimeToTimestampStr | — | — |
| HandleWarEffortSetStageCommand | method | ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/PSendSysMessage, GameEventMgr.Main/Update, ObjectMgr/SetSavedVariable | — | — |
| HandleWarEffortGetResource | method | ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/PSendSysMessage, world_event_wareffort/GetWarEffortStockInfo | — | — |
| HandleWarEffortSetResource | method | ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/PSendSysMessage, ObjectMgr/SetSavedVariable, world_event_wareffort/GetWarEffortStockInfo | — | — |
| LoadHardcodedEvents | method | DarkmoonFaire/DarkmoonFaire, DragonsOfNightmare/DragonsOfNightmare, ElementalInvasion/ElementalInvasion, FireworksShow/FireworksShow, ToastingGoblets/ToastingGoblets | GameEventMgr.Main/LoadFromDB | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `creature_respawn`: guid int(10) unsigned PK, respawn_time bigint(20), instance mediumint(8) unsigned PK, map int(5) unsigned?

*`?` = nullable, `PK` = primary key column.*


---

<!-- verify: boundary-bleed | foreign: ChatHandler -->
