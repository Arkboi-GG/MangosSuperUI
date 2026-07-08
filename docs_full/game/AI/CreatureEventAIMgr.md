# CreatureEventAIMgr

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# CreatureEventAIMgr

**CreatureEventAIMgr** is the singleton manager responsible for loading, validating, and storing the configuration data for the scripted AI system used by creatures in the world. It acts as the bridge between the static database definition of creature behaviors (`creature_ai_events`) and the runtime AI engine (`CreatureEventAI`).

Its primary responsibility is to parse the `creature_ai_events` table during server startup or reload commands, perform extensive sanity checks on the event parameters (such as ensuring timer ranges are valid, spells exist, and creature templates are defined), and populate an in-memory map (`m_CreatureEventAI_Event_Map`) keyed by creature ID. This map is then queried by individual creature AI instances to determine how they should react to specific game events (e.g., reaching low health, being hit by a spell, or spawning).

The manager enforces strict validation rules. If an event configuration contains logical errors—such as a minimum timer value exceeding the maximum, a missing spell ID, or a non-existent creature template—the manager logs an error and typically skips loading that specific event row to prevent runtime crashes or undefined behavior. It also automatically corrects certain minor configuration issues, such as capping event chances at 100% or forcing the `REPEATABLE` flag on events that logically require it (like receiving an emote).

## Member-by-Member Behavior

### Initialization and Lifecycle
*   **`CreatureEventAIMgr` / `~CreatureEventAIMgr`**: The constructor and destructor are trivial. The class relies on the `INSTANTIATE_SINGLETON_1` macro to ensure a single global instance exists. No complex initialization or cleanup logic is performed here; the heavy lifting occurs in `LoadCreatureEventAI_Events`.

### Data Management
*   **`ClearEventData`**: Clears the internal `m_CreatureEventAI_Event_Map`. This is primarily used during hot-reload operations to discard the old configuration before loading new data from the database.
*   **`GetCreatureEventAIMap`**: Returns a constant reference to the internal map of creature event configurations. This allows the `CreatureEventAI` class to look up the list of events associated with a specific creature ID without copying the data.

### Loading and Validation Logic
*   **`LoadCreatureEventAI_Events`**: This is the core method of the unit. It performs the following steps:
    1.  **Clears existing data**: Ensures no stale data remains from previous loads.
    2.  **Queries the database**: Executes a `SELECT` on `creature_ai_events` to retrieve all event rows.
    3.  **Iterates and Parses**: For each row, it constructs a temporary `CreatureEventAI_Event` structure. It maps raw integer fields from the database into typed structures (e.g., `timer`, `percent_range`, `hit_by_spell`) depending on the `event_type`.
    4.  **Validates Creature Existence**: Checks if the `creature_id` exists in the `creature_template` table via `ObjectMgr`. If not, the event is skipped.
    5.  **Validates Chance**: Ensures `event_chance` is between 1 and 100. Values > 100 are clamped to 100; 0 is logged as an error but the event is still loaded (though it will never trigger).
    6.  **Validates Conditions**: If a `condition_id` is present, it verifies the condition exists in the `conditions` storage. If missing, the condition ID is cleared to 0.
    7.  **Event-Specific Validation**: A large `switch` statement validates parameters based on `event_type`:
        *   **Timers (`EVENT_T_TIMER_IN_COMBAT`, etc.)**: Checks that `initialMin <= initialMax` and `repeatMin <= repeatMax`.
        *   **Percentages (`EVENT_T_HP`, etc.)**: Checks that `percentMin <= percentMax <= 100`. If `REPEATABLE` is set but repeat timers are zero, the flag is removed.
        *   **Spells (`EVENT_T_HIT_BY_SPELL`, `EVENT_T_AURA`, etc.)**: Verifies that referenced `spellId`s exist in `SpellMgr`. For hit-by-spell events, it also checks that the `schoolMask` matches the spell's school if specified.
        *   **Ranges (`EVENT_T_RANGE`)**: Checks `minDist <= maxDist`.
        *   **Summoned Units (`EVENT_T_SUMMONED_UNIT`)**: Verifies the summoned creature template exists.
        *   **Emotes (`EVENT_T_RECEIVE_EMOTE`)**: Verifies the emote text ID exists and forces the `REPEATABLE` flag.
        *   **Unimplemented Events**: Logs warnings for `EVENT_T_QUEST_ACCEPT` and `EVENT_T_QUEST_COMPLETE` and skips them.
    8.  **Action Script Resolution**: For each of the three possible actions (`action1_script`, `action2_script`, `action3_script`), it looks up the script in the global `sCreatureAIScripts` map. If a script ID is present but not found, the action is set to `nullptr` and an error is logged.
    9.  **Storage**: Validated events are appended to the vector corresponding to the `creature_id` in `m_CreatureEventAI_Event_Map`.
    10. **Logging**: Reports the total number of loaded events or notes if the table was empty.

## Cross-Unit Boundaries

*   **`ChatHandler.ServerCommands/HandleReloadEventAIEventsCommand`**: Calls `ClearEventData` and `LoadCreatureEventAI_Events` to allow administrators to reload AI configurations without restarting the server.
*   **`World/SetInitialWorldSettings`**: Calls `LoadCreatureEventAI_Events` during server startup to initialize the AI configuration.
*   **`CreatureEventAI/CreatureEventAI`**: Calls `GetCreatureEventAIMap` to retrieve the list of events for its associated creature. This is the primary consumer of the data managed by this unit.
*   **`ObjectMgr/GetCreatureTemplate` & `ObjectMgr/IsExistingCreatureId`**: Called by `LoadCreatureEventAI_Events` to verify that the `creature_id` in the event row corresponds to a valid creature template.
*   **`SpellMgr/GetSpellEntry`**: Called by `LoadCreatureEventAI_Events` to validate that spell IDs referenced in events (e.g., `EVENT_T_HIT_BY_SPELL`, `EVENT_T_AURA`) exist in the spell database.
*   **`SpellDefines/GetSchoolMask`**: Used to compare the school mask defined in the event against the actual spell's school mask.
*   **`Database/Query`**: Executes the SQL query to fetch event data.
*   **`Log.Main/Out`**: Logs errors and informational messages regarding invalid configurations, missing dependencies, or successful loading counts.
*   **`ProgressBar/BarGoLink`**: Provides visual feedback during the loading process.

## Data Model

The unit interacts exclusively with the `creature_ai_events` table.

**Table:** `creature_ai_events`
*   **Purpose**: Defines the reactive behaviors for creatures. Each row represents a single event trigger and its associated actions.
*   **Key Columns Used**:
    *   `id`: Unique identifier for the event row.
    *   `creature_id`: Links the event to a specific creature template.
    *   `condition_id`: Optional link to a condition that must be met for the event to trigger.
    *   `event_type`: Determines the nature of the trigger (e.g., timer, HP threshold, spell hit).
    *   `event_inverse_phase_mask`, `event_chance`, `event_flags`: Control when and how often the event triggers.
    *   `event_param1` through `event_param4`: Raw parameters interpreted differently based on `event_type` (e.g., min/max timers, spell IDs, distances).
    *   `action1_script`, `action2_script`, `action3_script`: Pointers to script functions that execute when the event triggers.

## Notable Implementation Details

*   **Silent Correction of Flags**: In `LoadCreatureEventAI_Events`, if an event like `EVENT_T_RECEIVE_EMOTE` is not marked as `REPEATABLE`, the manager automatically sets the flag because the event logically must be repeatable to function correctly. Similarly, if `EVENT_T_HP` is marked repeatable but has no repeat timers, the `REPEATABLE` flag is stripped.
*   **Strict Spell School Matching**: For `EVENT_T_HIT_BY_SPELL`, if a `schoolMask` is provided in `event_param2`, the code verifies that it matches the spell's actual school mask. If it doesn't match (and isn't -1, which implies "any"), the event is considered invalid and skipped.
*   **Unimplemented Quest Events**: `EVENT_T_QUEST_ACCEPT` and `EVENT_T_QUEST_COMPLETE` are explicitly logged as unimplemented and skipped. This indicates a gap in the current AI system's capabilities regarding quest-driven triggers.
*   **Action Script Null Handling**: If an action script ID is specified in the database but the script is not registered in `sCreatureAIScripts`, the action slot is set to `nullptr`. The event itself is still loaded, meaning the trigger might fire but produce no effect for that specific action slot.
*   **Condition Fallback**: If a `condition_id` is invalid, it is silently reset to 0, effectively removing the condition constraint rather than skipping the entire event.

## Member Reference

**CreatureEventAIMgr**
Constructor for the singleton manager. Trivial implementation.

**~CreatureEventAIMgr**
Destructor for the singleton manager. Trivial implementation.

**ClearEventData**
Clears the internal `m_CreatureEventAI_Event_Map`. Called by `ChatHandler.ServerCommands/HandleReloadEventAIEventsCommand` before reloading data.

**GetCreatureEventAIMap**
Returns a constant reference to `m_CreatureEventAI_Event_Map`. Called by `CreatureEventAI/CreatureEventAI` to access event configurations.

**LoadCreatureEventAI_Events**
Loads and validates all creature AI events from the `creature_ai_events` table. Performs extensive checks on creature existence, spell validity, parameter ranges, and script registration. Populates `m_CreatureEventAI_Event_Map`. Called by `World/SetInitialWorldSettings` and `ChatHandler.ServerCommands/HandleReloadEventAIEventsCommand`.

---

<!-- machine-true, projected from graph.json -->

## Map — CreatureEventAIMgr

*Source:* CreatureEventAIMgr.cpp, CreatureEventAIMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| CreatureEventAIMgr | ctor | — | — | — |
| ~CreatureEventAIMgr | dtor | — | — | — |
| ClearEventData | method | — | ChatHandler.ServerCommands/HandleReloadEventAIEventsCommand | — |
| GetCreatureEventAIMap | method | — | CreatureEventAI/CreatureEventAI | — |
| LoadCreatureEventAI_Events | method | Database/Query, Field/GetUInt32, Field/GetUInt8, Log.Main/Out, ObjectMgr/GetCreatureTemplate, ObjectMgr/IsExistingCreatureId, ProgressBar/BarGoLink, ProgressBar/BarGoLink#3, ProgressBar/step, QueryResult/Fetch, QueryResult/GetRowCount, QueryResult/NextRow, SpellDefines/GetSchoolMask, SpellMgr/GetSpellEntry, SpellMgr/Instance | ChatHandler.ServerCommands/HandleReloadEventAIEventsCommand, World/SetInitialWorldSettings | creature_ai_events |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `creature_ai_events`: id int(11) unsigned PK, creature_id int(11) unsigned, condition_id mediumint(8) unsigned, event_type tinyint(5) unsigned, event_inverse_phase_mask int(11), event_chance tinyint(3) unsigned, event_flags int(3) unsigned, event_param1 int(11), event_param2 int(11), event_param3 int(11), event_param4 int(11), action1_script int(11) unsigned, action2_script int(11) unsigned, action3_script int(11) unsigned, comment varchar(255)

*`?` = nullable, `PK` = primary key column.*

