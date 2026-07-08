# ScriptMgr

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ScriptMgr

**Purpose & Responsibilities**

`ScriptMgr` is the central registry and execution engine for the server's scripting system. It bridges the gap between static database-defined behaviors (SQL scripts) and dynamic C++-implemented logic (compiled scripts). Its primary responsibilities are:

1.  **Registration:** It maintains a global lookup table mapping string-based `script_name` identifiers (found in creature, gameobject, spell, and map templates) to specific C++ `Script` objects. This allows the rest of the server to request an AI, gossip handler, or instance data object by name.
2.  **Database Script Loading:** It loads, validates, and caches complex behavioral sequences defined in various `*_scripts` database tables (e.g., `creature_ai_scripts`, `spell_scripts`). These are stored in memory as `ScriptMapMap` structures for fast runtime access.
3.  **Event Dispatching:** It provides hook methods (e.g., `OnGossipHello`, `OnQuestAccept`, `OnEffectDummy`) that are called by the core game logic (Creatures, GameObjects, Spells) when specific events occur. `ScriptMgr` looks up the appropriate C++ script for the involved entity and invokes the relevant callback.
4.  **Utility Services:** It manages auxiliary data such as script texts (dialogue), waypoints, and escort quest data, providing helper functions like `DoScriptText` to broadcast dialogue to players.

`ScriptMgr` is implemented as a Singleton (`sScriptMgr`).

## Member-by-Member Behavior

### Initialization and Lifecycle

*   **`ScriptMgr` (ctor)**: Initializes the atomic counter `m_scheduledScripts` to zero. This counter tracks how many database-driven scripts are currently executing to prevent recursive loading or conflicts during reloads.
*   **`~ScriptMgr` (dtor)**: Cleans up all dynamically allocated `Script` objects stored in the global `m_scripts` vector. This ensures proper memory deallocation when the server shuts down or libraries are unloaded.
*   **`Initialize`**: The main entry point for setting up the scripting system. It calls `LoadDatabase` to load text and waypoint data, then resizes the `m_scripts` vector based on the number of unique script names found in the database. It invokes `AddScripts()` (defined in `ScriptLoader.cpp`) to register all compiled C++ scripts. Finally, it verifies that every script name referenced in the database has a corresponding C++ registration, logging errors for any mismatches.
*   **`LoadDatabase`**: A coordinator method that triggers the loading of static text data (`LoadScriptTexts`, `LoadScriptTextsCustom`), waypoint paths (`LoadScriptWaypoints`), and escort quest metadata (`LoadEscortData`).

### Database Script Loading and Validation

These methods load specific categories of database-defined scripts. They all rely on the internal `LoadScripts` helper to parse the SQL rows into `ScriptInfo` structures.

*   **`LoadScripts`**: The core parser for database scripts. It queries a specified table (e.g., `creature_ai_scripts`), iterates through the results, and populates a `ScriptMapMap`. For each row, it performs extensive validation:
    *   Checks if referenced conditions exist.
    *   Validates target parameters against the target type (via `CheckScriptTargets`).
    *   Verifies that spell IDs, creature entries, gameobject GUIDs, and quest IDs referenced in the script arguments actually exist in the database.
    *   Ensures logical consistency (e.g., a "Kill Credit" command references a valid creature; a "Teleport" command uses valid coordinates).
    *   If a script row contains invalid data, it logs an error and either skips the row or disables the specific action within the script using `DisableScriptAction`.
*   **`LoadAreaTriggerScripts`**: Loads scripts triggered when players enter specific areas. It cross-references loaded scripts with `areatrigger_template` to ensure all referenced script IDs exist and vice-versa.
*   **`LoadGameObjectScripts`**: Loads scripts tied to specific GameObject GUIDs. It validates that the GUIDs referenced in the script table actually exist in the world data.
*   **`LoadQuestEndScripts` / `LoadQuestStartScripts`**: Loads scripts executed when a quest is completed or accepted. It cross-references these with the `CompleteScript` and `StartScript` columns in `quest_template`.
*   **`LoadSpellScripts`**: Loads scripts for spells. It specifically checks that the spell has a `SPELL_EFFECT_SCRIPT_EFFECT` (effect ID 77) or is referenced by other mechanisms, ensuring the script is attached to a valid spell effect.
*   **`LoadGenericScripts`**: Loads scripts that are started by other scripts (via `SCRIPT_COMMAND_START_SCRIPT`). It uses `CollectPossibleGenericIds` to find all potential callers and ensures no orphaned scripts exist.
*   **`LoadEventScripts`**: Loads scripts triggered by `SPELL_EFFECT_SEND_EVENT` or GameObject events. It uses `CollectPossibleEventIds` to validate that every loaded event script ID is actually referenced by a spell or gameobject.
*   **`LoadCreatureSpellScripts`**: Loads scripts for creature-specific spell overrides. Validation is deferred to `ObjectMgr::LoadCreatureSpells`.
*   **`LoadGossipScripts`**: Loads gossip menu scripts. Validation is deferred to the gossip menu loader.
*   **`LoadCreatureMovementScripts`**: Loads movement scripts. Validation is deferred to `WaypointManager`.
*   **`LoadCreatureEventAIScripts`**: Loads the EventAI system scripts. It specifically checks that `creature_ai_scripts` do not have delays (which are unsupported in this context) and cross-references them with `creature_ai_events` to ensure all scripts are triggered by some event.

### Text and Waypoint Management

*   **`LoadScriptTexts`**: Loads dialogue strings from `script_texts`. It separates the text content (loaded by `ObjectMgr::LoadMangosStrings`) from the metadata (sound, type, language, emote) which is stored in `m_mTextDataMap`. It validates that sound IDs and languages exist.
*   **`LoadScriptTextsCustom`**: Similar to `LoadScriptTexts`, but loads from `custom_texts`.
*   **`LoadScriptWaypoints`**: Loads waypoint paths from `script_waypoint`. It groups points by creature entry and stores them in `m_mPointMoveMap`. It validates that the creature entry exists and has a script name defined.
*   **`LoadEscortData`**: Loads escort quest metadata from `script_escort_data`. It links creatures to quests and factions, and calculates the last waypoint ID for each escort. It relies on `GetPointMoveList` to verify waypoints exist.
*   **`GetTextData`**: Retrieves metadata (sound, type, etc.) for a given text ID from `m_mTextDataMap`.
*   **`GetPointMoveList`**: Returns the vector of waypoints for a specific creature entry.
*   **`GetEscortData`**: Returns the escort metadata for a specific creature.

### Script Registration and Lookup

*   **`RegisterSelf`**: Called by individual C++ `Script` subclasses during initialization. It looks up the script's name in `m_scriptNames` to get an integer ID, then places the `Script` object pointer into the global `m_scripts` vector at that index. If the name isn't found in the database, it deletes the script object and logs an error (unless suppressed).
*   **`GetScriptId`**: Converts a script name string to its integer ID using binary search on the sorted `m_scriptNames` vector.
*   **`GetScriptName`**: Converts an integer ID back to the script name string.
*   **`GetScriptIdsCount`**: Returns the total number of registered script names.
*   **`GetCreatureAI`**: Given a `Creature`, it retrieves its script ID, looks up the corresponding `Script` object, and calls its `GetAI` factory method to create and return a `CreatureAI` instance.
*   **`GetGameObjectAI`**: Similar to `GetCreatureAI`, but for `GameObject`s, returning a `GameObjectAI` instance.
*   **`CreateInstanceData`**: Given a `Map`, it retrieves the map's script ID and calls the corresponding `Script`'s `GetInstanceData` factory method to create an `InstanceData` object for dungeon/raid management.
*   **`GetSpellScript` / `GetAuraScript`**: Retrieves the C++ script object associated with a spell entry, allowing the spell system to invoke custom logic during casting or aura application.

### Event Hooks (Dispatchers)

These methods are called by the core game objects when specific interactions occur. They act as dispatchers, finding the correct C++ script and invoking the appropriate callback.

*   **`OnGossipHello` (Creature/GameObject)**: Called when a player opens a gossip menu. It clears the current menus and invokes the script's `pGossipHello` or `pGOGossipHello` callback.
*   **`OnGossipSelect` (Creature/GameObject)**: Called when a player selects a gossip option. It handles both standard selections and those with input codes, invoking `pGossipSelect` or `pGossipSelectWithCode`.
*   **`OnQuestAccept` (Creature/GameObject)**: Called when a player accepts a quest. Invokes `pQuestAcceptNPC` or `pGOQuestAccept`.
*   **`OnQuestRewarded` (Creature/GameObject)**: Called when a player completes a quest. Invokes `pQuestRewardedNPC` or `pQuestRewardedGO`.
*   **`GetDialogStatus` (Creature/GameObject)**: Called to determine if a quest giver is available. Invokes `pNPCDialogStatus` or `pGODialogStatus`.
*   **`OnGameObjectUse` / `OnGameObjectOpen`**: Called when a player interacts with or opens a GameObject. Invokes `pGOHello` or `pGOOpen`.
*   **`OnAreaTrigger`**: Called when a player enters an area trigger. Looks up the script by the trigger's name and invokes `pAreaTrigger`.
*   **`OnProcessEvent`**: Called when a generic event ID is triggered (e.g., by a spell or gameobject). It resolves the event ID to a script ID via `GetEventIdScriptId` and invokes `pProcessEventId`.
*   **`OnEffectDummy` (Creature/GameObject)**: Called when a spell with a "Dummy" effect hits a target. It looks up the target's script and invokes `pEffectDummyCreature` or `pEffectDummyGameObj`.
*   **`OnAuraDummy`**: Called when a Dummy aura is applied or removed. It casts the aura's target to a `Creature` and invokes `pEffectAuraDummy`.

### Utility Functions

*   **`DoScriptText`**: A global helper function that broadcasts text to players. It handles both positive IDs (standard broadcast text) and negative IDs (script-specific text from `script_texts`). It plays sounds, triggers emotes, and sends chat messages (Say, Yell, Whisper, etc.) based on the text data.
*   **`DoOrSimulateScriptTextForMap`**: Similar to `DoScriptText`, but designed for map-wide broadcasts (e.g., raid boss announcements). It sends text to all players in a specific zone/map.
*   **`GetTargetByType`**: A global helper function used by database scripts to resolve target parameters. Given a target type (e.g., "Nearest Hostile Player", "Creature with GUID") and parameters, it searches the map/unit threat lists to return the appropriate `WorldObject`.
*   **`CheckScriptTargets`**: Validates that the parameters provided for a specific target type in a database script are logically consistent and reference existing entities.
*   **`IsCreatureGuidReferencedInScripts` / `IsGameObjectGuidReferencedInScripts`**: Checks if a specific GUID is referenced in any loaded database script. Used by admin commands to prevent deleting NPCs/Objects that are critical to scripted events.
*   **`IncreaseScheduledScriptsCount` / `DecreaseScheduledScriptCount`**: Manages the atomic counter of active scripts. Used to prevent reloading scripts while they are executing.
*   **`IsScriptScheduled`**: Returns true if any scripts are currently running.

## Cross-Unit Boundaries

*   **`ObjectMgr`**: `ScriptMgr` heavily relies on `ObjectMgr` to validate data integrity. It calls `GetCreatureTemplate`, `GetGameObjectTemplate`, `GetQuestTemplate`, `GetSpellEntry`, etc., to ensure that IDs referenced in database scripts correspond to existing entries. It also uses `ObjectMgr` to load string data (`LoadMangosStrings`).
*   **`SpellMgr`**: Used to validate spell IDs and retrieve spell entries for validation purposes (e.g., checking if a spell applies an aura).
*   **`Database` / `QueryResult` / `Field`**: `ScriptMgr` directly queries the `WorldDatabase` to load all script definitions, texts, and waypoints. It parses the resulting rows into internal data structures.
*   **`Log`**: Extensive logging is used throughout `ScriptMgr` to report errors in database scripts (missing IDs, invalid parameters) and to confirm successful loading counts.
*   **`ProgressBar`**: Used to provide visual feedback during the loading of large script tables.
*   **`Creature` / `GameObject` / `Map` / `Player`**: These classes call into `ScriptMgr`'s hook methods (`OnGossipHello`, `GetCreatureAI`, etc.) to delegate behavior to scripts. Conversely, `ScriptMgr` calls back into these objects (via function pointers in the `Script` struct) to execute the actual scripted logic.
*   **`ScriptLoader`**: The `Initialize` method calls `AddScripts()` from `ScriptLoader.cpp`, which is responsible for instantiating all compiled C++ scripts.
*   **`ChatHandler`**: Various admin commands (e.g., `reload all_scripts`) call `ScriptMgr` methods to reload specific script categories or check if scripts are scheduled.

## Data Model

`ScriptMgr` interacts with numerous database tables to define behavior. Key tables include:

*   **`script_texts`**: Stores dialogue strings with metadata (sound, type, language, emote). Used by `LoadScriptTexts`.
*   **`custom_texts`**: Similar to `script_texts` for custom content. Used by `LoadScriptTextsCustom`.
*   **`script_waypoint`**: Defines movement paths for creatures. Used by `LoadScriptWaypoints`.
*   **`script_escort_data`**: Links creatures to escort quests and factions. Used by `LoadEscortData`.
*   **`creature_ai_scripts`**: Defines actions taken by creatures in response to events. Used by `LoadCreatureEventAIScripts`.
*   **`creature_ai_events`**: Defines the events that trigger `creature_ai_scripts`. Cross-referenced during loading.
*   **`spell_scripts`**: Defines custom behavior for spells. Used by `LoadSpellScripts`.
*   **`gameobject_scripts`**: Defines behavior for specific GameObjects. Used by `LoadGameObjectScripts`.
*   **`quest_start_scripts` / `quest_end_scripts`**: Defines behavior for quest acceptance/completion. Used by respective loaders.
*   **`event_scripts`**: Defines behavior for generic event IDs. Used by `LoadEventScripts`.
*   **`generic_scripts`**: Defines scripts that can be started by other scripts. Used by `LoadGenericScripts`.
*   **`areatrigger_scripts`**: Defines behavior for area triggers. Used by `LoadAreaTriggerScripts`.
*   **`scripted_event_id`**: Maps event IDs to script names. Used by `LoadEventIdScripts`.
*   **`areatrigger_template`**, **`creature_template`**, **`gameobject_template`**, **`spell_template`**, **`quest_template`**: These core template tables contain `script_name` or `script_id` columns that link entities to their scripts. `ScriptMgr` reads these to build its lookup maps and validate references.

## Notable Implementation Details

*   **Script ID Mapping**: `ScriptMgr` converts human-readable `script_name` strings into integer IDs. This ID is used as an index into the `m_scripts` vector, allowing O(1) access to the C++ script object. The `m_scriptNames` vector is kept sorted to enable binary search during ID lookup.
*   **Database Script Validation**: The `LoadScripts` method performs rigorous validation of database scripts at load time. This prevents runtime errors caused by invalid data (e.g., referencing a non-existent spell). Invalid scripts are either skipped or disabled, with detailed error messages logged.
*   **Atomic Script Counter**: The `m_scheduledScripts` counter is atomic and used to prevent race conditions during script reloading. If scripts are currently executing, reload operations are blocked.
*   **Global Helper Functions**: Functions like `DoScriptText` and `GetTargetByType` are declared as global functions in the header but implemented in the `.cpp` file. They are widely used by both C++ scripts and database script executors to perform common tasks.
*   **Memory Management**: The destructor explicitly deletes all `Script` objects. This is crucial because `Script` objects are dynamically allocated by `ScriptLoader` and stored in a raw pointer vector. Failure to delete them would cause memory leaks.
*   **Fallback Behavior**: Many hook methods (e.g., `OnGossipHello`) return `false` if no script is found or if the script doesn't implement the specific callback. This allows the core game logic to proceed with default behavior if no custom script is present.

## Member Reference

**ScriptMgr**
Constructor. Initializes the scheduled scripts counter to zero.

**~ScriptMgr**
Destructor. Deletes all registered `Script` objects to free memory.

**DisableScriptAction**
Static helper function. Marks a `ScriptInfo` action as disabled by setting its command to `SCRIPT_COMMAND_DISABLED`. Used when a script row contains invalid data.

**LoadScripts**
Internal method. Parses a database table of scripts, validates each row, and populates a `ScriptMapMap`. Performs extensive checks on spell IDs, creature entries, coordinates, and logical consistency.

**GetScriptName#2**
Returns the string name of a script given its integer ID.

**GetScriptIdsCount#2**
Returns the total number of registered script names.

**GetTextData**
Retrieves metadata (sound, type, language, emote) for a given script text ID.

**GetEscortData**
Retrieves escort quest metadata for a given creature entry.

**GetPointMoveList**
Returns the list of waypoints for a given creature entry.

**IsCreatureGuidReferencedInScripts**
Checks if a creature GUID is referenced in any loaded database script.

**IsGameObjectGuidReferencedInScripts**
Checks if a gameobject GUID is referenced in any loaded database script.

**IncreaseScheduledScriptsCount**
Increments the atomic counter of active scripts.

**DecreaseScheduledScriptCount**
Decrements the atomic counter of active scripts.

**DecreaseScheduledScriptCount#2**
Decrements the atomic counter by a specified amount.

**IsScriptScheduled**
Returns true if any scripts are currently executing.

**CheckScriptTargets**
Validates that the parameters for a specific target type in a database script are consistent and reference existing entities.

**LoadAreaTriggerScripts**
Loads area trigger scripts and cross-references them with `areatrigger_template`.

**LoadGameObjectScripts**
Loads gameobject scripts and validates referenced GUIDs.

**LoadQuestEndScripts**
Loads quest completion scripts and cross-references them with `quest_template`.

**LoadQuestStartScripts**
Loads quest acceptance scripts and cross-references them with `quest_template`.

**LoadSpellScripts**
Loads spell scripts and validates that the spells have appropriate effects.

**LoadGenericScripts**
Loads generic scripts and ensures they are referenced by other scripts.

**LoadEventScripts**
Loads event scripts and ensures they are referenced by spells or gameobjects.

**LoadCreatureSpellScripts**
Loads creature spell scripts. Validation is deferred.

**LoadGossipScripts**
Loads gossip scripts. Validation is deferred.

**LoadCreatureMovementScripts**
Loads movement scripts. Validation is deferred.

**LoadCreatureEventAIScripts**
Loads EventAI scripts, validating delays and cross-referencing with events.

**CheckAllScriptTexts**
Iterates through all loaded script maps and checks for missing broadcast text IDs.

**CheckScriptTexts**
Helper method for `CheckAllScriptTexts`. Checks a single script map for missing text IDs.

**LoadEventIdScripts**
Loads the mapping of event IDs to script names from `scripted_event_id`.

**LoadScriptNames**
Scans all template tables for unique `script_name` values and builds the `m_scriptNames` lookup vector.

**GetScriptId#2**
Converts a script name string to its integer ID using binary search.

**GetEventIdScriptId#2**
Returns the script ID associated with a given event ID.

**GetCreatureAI**
Creates and returns a `CreatureAI` instance for a given creature based on its script name.

**GetGameObjectAI**
Creates and returns a `GameObjectAI` instance for a given gameobject based on its script name.

**CreateInstanceData**
Creates and returns an `InstanceData` instance for a given map based on its script name.

**GetSpellScript**
Retrieves the C++ script object for a given spell entry.

**GetAuraScript**
Retrieves the C++ aura script object for a given spell entry.

**OnGossipHello**
Dispatches gossip hello events to the appropriate creature script.

**OnGossipHello#2**
Dispatches gossip hello events to the appropriate gameobject script.

**OnGossipSelect**
Dispatches gossip selection events to the appropriate creature script.

**OnGossipSelect#2**
Dispatches gossip selection events to the appropriate gameobject script.

**OnQuestAccept**
Dispatches quest acceptance events to the appropriate creature script.

**OnQuestAccept#2**
Dispatches quest acceptance events to the appropriate gameobject script.

**OnQuestRewarded**
Dispatches quest reward events to the appropriate creature script.

**OnQuestRewarded#2**
Dispatches quest reward events to the appropriate gameobject script.

**GetDialogStatus**
Dispatches dialog status queries to the appropriate creature script.

**GetDialogStatus#2**
Dispatches dialog status queries to the appropriate gameobject script.

**OnGameObjectOpen**
Dispatches gameobject open events to the appropriate script.

**OnGameObjectUse**
Dispatches gameobject use events to the appropriate script.

**OnAreaTrigger**
Dispatches area trigger events to the appropriate script.

**OnProcessEvent**
Dispatches generic event ID events to the appropriate script.

**OnEffectDummy**
Dispatches dummy spell effect events to the appropriate creature script.

**OnEffectDummy#2**
Dispatches dummy spell effect events to the appropriate gameobject script.

**OnAuraDummy**
Dispatches dummy aura events to the appropriate creature script.

**GetEventIdScriptId**
Global wrapper for `ScriptMgr::GetEventIdScriptId`.

**GetScriptId**
Global wrapper for `ScriptMgr::GetScriptId`.

**GetScriptName**
Global wrapper for `ScriptMgr::GetScriptName`.

**GetScriptIdsCount**
Global wrapper for `ScriptMgr::GetScriptIdsCount`.

**Initialize**
Main setup method. Loads database data, registers C++ scripts, and validates mappings.

**LoadDatabase**
Coordinator method for loading static text and waypoint data.

**LoadScriptTexts**
Loads dialogue strings and metadata from `script_texts`.

**LoadScriptTextsCustom**
Loads custom dialogue strings and metadata from `custom_texts`.

**LoadScriptWaypoints**
Loads waypoint paths from `script_waypoint`.

**LoadEscortData**
Loads escort quest metadata from `script_escort_data`.

**CollectPossibleGenericIds**
Helper method. Scans all script tables to find IDs of scripts that are started by other scripts.

**CollectPossibleEventIds**
Helper method. Scans gameobject and spell templates to find all referenced event IDs.

**DoScriptText**
Global helper. Broadcasts text, plays sounds, and triggers emotes based on a text ID.

**DoOrSimulateScriptTextForMap**
Global helper. Broadcasts text to all players in a specific map/zone.

**RegisterSelf**
Called by C++ scripts to register themselves with the manager.

**GetTargetByType**
Global helper. Resolves a target type and parameters to a specific `WorldObject`.

---

<!-- machine-true, projected from graph.json -->

## Map — ScriptMgr

*Source:* ScriptMgr.cpp, ScriptMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ScriptMgr | ctor | — | — | — |
| ~ScriptMgr | dtor | — | — | — |
| DisableScriptAction | function | — | — | — |
| LoadScripts | method | Database/PQuery, Field/GetFloat, Field/GetInt32, Field/GetUInt32, Field/GetUInt8, GameEventMgr.Main/IsValidEvent, GridDefines/IsValidMapCoord#4, Log.Main/Out, ObjectMgr/GetCreatureData, ObjectMgr/GetCreatureSpellsList, ObjectMgr/GetCreatureTemplate, ObjectMgr/GetFactionTemplateEntry, ObjectMgr/GetGameObjectTemplate, ObjectMgr/GetGOData, ObjectMgr/GetItemPrototype, ObjectMgr/GetQuestTemplate, ObjectMgr/IsExistingCreatureGuid, ObjectMgr/IsExistingCreatureId, ObjectMgr/IsExistingCreatureSpellsId, ObjectMgr/IsExistingGameObjectGuid, ObjectMgr/IsExistingGameObjectId, ObjectMgr/IsExistingGossipMenuId, ObjectMgr/IsExistingItemId, ObjectMgr/IsExistingQuestId, ProgressBar/BarGoLink, ProgressBar/BarGoLink#3, ProgressBar/step, QueryResult/Fetch, QueryResult/GetRowCount, QueryResult/NextRow, QuestDef/HasSpecialFlag, QuestDef/SetSpecialFlag, ScriptInfo/GetGOGuid, ScriptInfo/ScriptInfo, SpellEntry/HasEffect, SpellEntry/IsSpellAppliesAura#2, SpellMgr/GetSpellEntry, SpellMgr/Instance, SpellMgr/IsExistingSpellId, UpdateFields/GetIndexOfUpdateFieldForCurrentBuild | — | — |
| GetScriptName#2 | method | — | Creature.Main/GetScriptName, Map.Main/CreateInstanceData | — |
| GetScriptIdsCount#2 | method | — | — | — |
| GetTextData | method | — | — | — |
| GetEscortData | method | — | — | — |
| GetPointMoveList | method | — | ChatHandler.CreatureCommands/HandleEscortShowWpCommand, ScriptedEscortAI/FillPointMovementListForCreature | — |
| IsCreatureGuidReferencedInScripts | method | — | ChatHandler.CreatureCommands/HandleNpcDeleteCommand | — |
| IsGameObjectGuidReferencedInScripts | method | — | ChatHandler.ObjectCommands/HandleGameObjectDeleteCommand | — |
| IncreaseScheduledScriptsCount | method | — | Map.Main/ScriptCommandStart, Map.Main/ScriptsStart | — |
| DecreaseScheduledScriptCount | method | — | Map.Main/ScriptsProcess, Map.Main/TerminateScript | — |
| DecreaseScheduledScriptCount#2 | method | — | Map.Main/CrashUnload, Map.Main/~Map | — |
| IsScriptScheduled | method | — | ChatHandler.ServerCommands/HandleReloadAllScriptsCommand, ChatHandler.ServerCommands/HandleReloadCreatureSpellScriptsCommand, ChatHandler.ServerCommands/HandleReloadEventScriptsCommand, ChatHandler.ServerCommands/HandleReloadGameObjectScriptsCommand, ChatHandler.ServerCommands/HandleReloadGenericScriptsCommand, ChatHandler.ServerCommands/HandleReloadGossipScriptsCommand, ChatHandler.ServerCommands/HandleReloadQuestEndScriptsCommand, ChatHandler.ServerCommands/HandleReloadQuestStartScriptsCommand, ChatHandler.ServerCommands/HandleReloadSpellScriptsCommand | — |
| CheckScriptTargets | method | Log.Main/Out, ObjectMgr/GetCreatureData, ObjectMgr/GetCreatureTemplate, ObjectMgr/GetGameObjectTemplate, ObjectMgr/GetGOData, ObjectMgr/IsExistingCreatureGuid, ObjectMgr/IsExistingCreatureId, ObjectMgr/IsExistingGameObjectGuid, ObjectMgr/IsExistingGameObjectId, SpellMgr/GetSpellEntry, SpellMgr/Instance, SpellMgr/IsExistingSpellId | ObjectMgr/LoadCreatureSpells | — |
| LoadAreaTriggerScripts | method | Database/Query, Field/GetUInt32, Log.Main/Out, QueryResult/Fetch, QueryResult/NextRow | World/SetInitialWorldSettings | areatrigger_template |
| LoadGameObjectScripts | method | Log.Main/Out, ObjectMgr/GetGOData, ObjectMgr/IsExistingGameObjectGuid | ChatHandler.ServerCommands/HandleReloadGameObjectScriptsCommand, World/SetInitialWorldSettings | — |
| LoadQuestEndScripts | method | Database/Query, Field/GetUInt32, Log.Main/Out, QueryResult/Fetch, QueryResult/NextRow | ChatHandler.ServerCommands/HandleReloadQuestEndScriptsCommand, World/SetInitialWorldSettings | quest_template |
| LoadQuestStartScripts | method | Database/Query, Field/GetUInt32, Log.Main/Out, QueryResult/Fetch, QueryResult/NextRow | ChatHandler.ServerCommands/HandleReloadQuestStartScriptsCommand, World/SetInitialWorldSettings | quest_template |
| LoadSpellScripts | method | Database/Query, Field/GetUInt32, Log.Main/Out, QueryResult/Fetch, QueryResult/NextRow, SpellMgr/GetSpellEntry, SpellMgr/Instance, SpellMgr/IsExistingSpellId | ChatHandler.ServerCommands/HandleReloadSpellScriptsCommand, World/SetInitialWorldSettings | spell_template |
| LoadGenericScripts | method | Log.Main/Out | ChatHandler.ServerCommands/HandleReloadGenericScriptsCommand, World/SetInitialWorldSettings | — |
| LoadEventScripts | method | Log.Main/Out | ChatHandler.ServerCommands/HandleReloadEventScriptsCommand, World/SetInitialWorldSettings | — |
| LoadCreatureSpellScripts | method | — | ChatHandler.ServerCommands/HandleReloadCreatureSpellScriptsCommand, World/SetInitialWorldSettings | — |
| LoadGossipScripts | method | — | ChatHandler.ServerCommands/HandleReloadGossipScriptsCommand, World/SetInitialWorldSettings | — |
| LoadCreatureMovementScripts | method | — | World/SetInitialWorldSettings | — |
| LoadCreatureEventAIScripts | method | Database/PQuery, Database/Query, Field/GetUInt32, Log.Main/Out, QueryResult/Fetch, QueryResult/NextRow | ChatHandler.ServerCommands/HandleReloadEventAIEventsCommand, World/SetInitialWorldSettings | creature_ai_events, creature_ai_scripts |
| CheckAllScriptTexts | method | — | ChatHandler.ServerCommands/HandleReloadAllScriptsCommand, World/SetInitialWorldSettings | — |
| CheckScriptTexts | method | Log.Main/Out, ObjectMgr/GetBroadcastTextLocale | — | — |
| LoadEventIdScripts | method | Database/Query, Field/GetString, Field/GetUInt32, Log.Main/Out, ProgressBar/BarGoLink, ProgressBar/BarGoLink#3, ProgressBar/step, QueryResult/Fetch, QueryResult/GetRowCount, QueryResult/NextRow | World/SetInitialWorldSettings | scripted_event_id |
| LoadScriptNames | method | Database/PQuery, Field/GetString, Log.Main/Out, ProgressBar/BarGoLink, ProgressBar/step, QueryResult/NextRow, QueryResult/operator[] | World/SetInitialWorldSettings | — |
| GetScriptId#2 | method | — | ObjectMgr/LoadCreatureInfo, ObjectMgr/LoadGameObjectInfo, SpellMgr/LoadSpell | — |
| GetEventIdScriptId#2 | method | — | — | — |
| GetCreatureAI | method | Creature.Main/GetScriptId | CreatureAISelector/selectAI | — |
| GetGameObjectAI | method | GameObject/GetGOInfo | GameObject/AIM_Initialize | — |
| CreateInstanceData | method | Map.Main/GetScriptId | Map.Main/CreateInstanceData | — |
| GetSpellScript | method | — | Spell.Main/Spell, Spell.Main/Spell#2 | — |
| GetAuraScript | method | — | Unit.SpellAuras/SpellAuraHolder | — |
| OnGossipHello | method | Creature.Main/GetScriptId, GossipDef/ClearMenus | WorldSession.BattleGroundHandler/HandleAreaSpiritHealerQueueOpcode, WorldSession.NPCHandler/HandleGossipHelloOpcode, WorldSession.QuestHandler/HandleQuestgiverHelloOpcode | — |
| OnGossipHello#2 | method | GameObject/GetGOInfo, GossipDef/ClearMenus | GameObject/Use | — |
| OnGossipSelect | method | Creature.Main/GetScriptId, GossipDef/ClearMenus, Log.Main/Out | WorldSession.NPCHandler/HandleGossipSelectOptionOpcode | — |
| OnGossipSelect#2 | method | GameObject/GetGOInfo, GossipDef/ClearMenus, Log.Main/Out | WorldSession.NPCHandler/HandleGossipSelectOptionOpcode | — |
| OnQuestAccept | method | Creature.Main/GetScriptId, GossipDef/ClearMenus | Player.Main/AddQuest | — |
| OnQuestAccept#2 | method | GameObject/GetGOInfo, GossipDef/ClearMenus | Player.Main/AddQuest | — |
| OnQuestRewarded | method | Creature.Main/GetScriptId, GossipDef/ClearMenus | Player.Main/RewardQuest | — |
| OnQuestRewarded#2 | method | GameObject/GetGOInfo, GossipDef/ClearMenus | Player.Main/RewardQuest | — |
| GetDialogStatus | method | Creature.Main/GetScriptId, GossipDef/ClearMenus | WorldSession.QuestHandler/HandleQuestgiverStatusQueryOpcode | — |
| GetDialogStatus#2 | method | GameObject/GetGOInfo, GossipDef/ClearMenus | WorldSession.QuestHandler/HandleQuestgiverStatusQueryOpcode | — |
| OnGameObjectOpen | method | GameObject/GetGOInfo | Spell.Effects/EffectOpenLock | — |
| OnGameObjectUse | method | GameObject/GetGOInfo, GossipDef/ClearMenus | GameObject/Use | — |
| OnAreaTrigger | method | — | Map.Main/StartAreaTriggerScript | — |
| OnProcessEvent | method | — | GameObject/Use, Spell.Effects/EffectSendEvent, WorldSession.LootHandler/DoLootRelease | — |
| OnEffectDummy | method | Creature.Main/GetScriptId | Spell.Effects/EffectDummy, Unit.SpellAuras/TriggerSpell | — |
| OnEffectDummy#2 | method | GameObject/GetGOInfo | Spell.Effects/EffectDummy | — |
| OnAuraDummy | method | Aura/GetTarget, Creature.Main/GetScriptId | Unit.SpellAuras/HandleAuraDummy | — |
| GetEventIdScriptId | function | — | — | — |
| GetScriptId | function | — | ObjectMgr/LoadAreaTriggers | — |
| GetScriptName | function | — | — | — |
| GetScriptIdsCount | function | — | — | — |
| Initialize | method | Log.Main/Out, ProgressBar/BarGoLink, ProgressBar/step, ScriptLoader/AddScripts | World/SetInitialWorldSettings | — |
| LoadDatabase | method | — | — | — |
| LoadScriptTexts | method | Database/PQuery, Field/GetInt32, Field/GetUInt32, Log.Main/Out, ObjectMgr/GetLanguageDescByID, ObjectMgr/GetSoundEntry, ObjectMgr/LoadMangosStrings#2, ProgressBar/BarGoLink, ProgressBar/BarGoLink#3, ProgressBar/step, QueryResult/Fetch, QueryResult/GetRowCount, QueryResult/NextRow | — | script_texts |
| LoadScriptTextsCustom | method | Database/PQuery, Field/GetInt32, Field/GetUInt32, Log.Main/Out, ObjectMgr/GetLanguageDescByID, ObjectMgr/GetSoundEntry, ObjectMgr/LoadMangosStrings#2, ProgressBar/BarGoLink, ProgressBar/BarGoLink#3, ProgressBar/step, QueryResult/Fetch, QueryResult/GetRowCount, QueryResult/NextRow | — | custom_texts |
| LoadScriptWaypoints | method | Database/PQuery, Field/GetFloat, Field/GetUInt32, Log.Main/Out, ObjectMgr/GetCreatureTemplate, ObjectMgr/IsExistingCreatureId, ProgressBar/BarGoLink, ProgressBar/BarGoLink#3, ProgressBar/step, QueryResult/Fetch, QueryResult/GetRowCount, QueryResult/NextRow | — | script_waypoint |
| LoadEscortData | method | Database/PQuery, Field/GetUInt32, Log.Main/Out, ObjectMgr/GetCreatureTemplate, ProgressBar/BarGoLink, ProgressBar/BarGoLink#3, ProgressBar/step, QueryResult/Fetch, QueryResult/GetRowCount, QueryResult/NextRow | — | script_escort_data |
| CollectPossibleGenericIds | method | Database/PQuery, Field/GetInt32, Field/GetUInt32, QueryResult/Fetch, QueryResult/NextRow | — | — |
| CollectPossibleEventIds | method | Database/PQuery, Database/Query, Field/GetUInt32, QueryResult/Fetch, QueryResult/NextRow, SpellMgr/GetMaxSpellId, SpellMgr/GetSpellEntry, SpellMgr/Instance | — | gameobject_template, spell_template |
| DoScriptText | function | Log.Main/Out, Map.Main/IsContinent, Map.Main/PlayDirectSoundToMap, Object/GetEntry, Object/GetGUIDLow, Object/GetTypeId, ObjectMgr/GetBroadcastTextLocale, ObjectMgr/GetSoundEntry, Unit.Main/HandleEmoteCommand, WorldObject.Object/GetMap, WorldObject.Object/GetZoneId, WorldObject.Object/MonsterSay#2, WorldObject.Object/MonsterScriptToZone, WorldObject.Object/MonsterTextEmote#2, WorldObject.Object/MonsterWhisper#2, WorldObject.Object/MonsterYell#2, WorldObject.Object/MonsterYellToZone, WorldObject.Object/PlayDirectSound | arathi_highlands/Aggro, arathi_highlands/Aggro#2, arathi_highlands/QuestAccept_npc_kinelory, arathi_highlands/QuestAccept_npc_professor_phizzlethorpe, arathi_highlands/WaypointReached, arathi_highlands/WaypointReached#2, ashenvale/AttackedBy, ashenvale/HitBanner, ashenvale/JustSummoned, ashenvale/QuestAccept_npc_feero_ironhand, ashenvale/QuestAccept_npc_torek, ashenvale/SummonedCreatureJustDied, ashenvale/WaypointReached, ashenvale/WaypointReached#2, ashenvale/WaypointReached#3, blackrock_depths/Activate, blackrock_depths/Aggro#3, blackrock_depths/AreaTrigger_at_shadowforge_bridge, blackrock_depths/AttackThief, blackrock_depths/EnterCombat, blackrock_depths/OnUse, blackrock_depths/QuestRewarded_npc_rocknot, blackrock_depths/UpdateAI, blackrock_depths/UpdateAI#4, blackrock_depths/UpdateAI#5, blackrock_depths/UpdateEscortAI#2, blackrock_depths/UpdateEscortAI#3, blackrock_depths/UpdateEscortAI#4, blackrock_depths/WarnThief, blackrock_depths/WaypointReached, blackrock_depths/WaypointReached#2, blackrock_depths/WaypointReached#3, blackrock_depths/WaypointReached#4, blackrock_depths/WaypointReached#5, blackrock_depths/WaypointReached#6, boss_anubrekhan/Aggro, boss_anubrekhan/KilledUnit, boss_anubrekhan/OnUse, boss_anubrekhan/UpdateAI#2, boss_arcanist_doan/Aggro, boss_arcanist_doan/UpdateAI, boss_archaedas/KilledUnit, boss_archaedas/SpellHit, boss_archaedas/UpdateAI, boss_arlokk/Aggro, boss_arlokk/JustDied, boss_arlokk/UpdateAI, boss_ayamiss/UpdateAI, boss_baron_geddon/UpdateAI, boss_broodlord_lashlayer/Aggro, boss_broodlord_lashlayer/UpdateAI, boss_bug_trio/CorpseRemoved, boss_buru/UpdateAI, boss_celebras_the_cursed/QuestAccepted, boss_celebras_the_cursed/UpdateEscortAI, boss_celebras_the_cursed/WaypointReached, boss_chromaggus/UpdateAI, boss_cthun/UpdateInvulnerablePhase, boss_dathrohan_balnazzar/Aggro, boss_dathrohan_balnazzar/JustDied, boss_dathrohan_balnazzar/UpdateAI, boss_doctor_theolen_krastinov/UpdateAI, boss_emeriss/Aggro, boss_emeriss/DoSpecialAbility, boss_emperor_dagran_thaurissan/Aggro, boss_emperor_dagran_thaurissan/JustDied, boss_emperor_dagran_thaurissan/KilledUnit, boss_faerlina/Aggro, boss_faerlina/JustDied, boss_faerlina/KilledUnit, boss_faerlina/UpdateAI, boss_flamegor/UpdateAI, boss_four_horsemen/Aggro#3, boss_four_horsemen/JustDied#2, boss_four_horsemen/JustDied#3, boss_four_horsemen/JustDied#4, boss_four_horsemen/JustDied#5, boss_four_horsemen/KilledUnit, boss_four_horsemen/KilledUnit#2, boss_four_horsemen/KilledUnit#3, boss_four_horsemen/KilledUnit#4, boss_four_horsemen/SpellHitTarget#2, boss_four_horsemen/UpdateAI#2, boss_four_horsemen/UpdateAI#3, boss_four_horsemen/UpdateAI#4, boss_four_horsemen/UpdateAI#5, boss_garr/UpdateEvents, boss_general_angerforge/UpdateAI, boss_gluth/UpdateAI, boss_golemagg/DamageTaken#2, boss_gordok_king/Aggro, boss_gothik/Aggro, boss_gothik/JustDied, boss_gothik/KilledUnit, boss_gothik/OpenTheGate, boss_gothik/UpdateAI, boss_grizzle/UpdateAI, boss_hakkar/Aggro, boss_heigan/Aggro, boss_heigan/EventStartDance, boss_heigan/EventTaunt, boss_heigan/JustDied, boss_heigan/KilledUnit, boss_herod/Aggro, boss_herod/JustSummoned, boss_herod/KilledUnit, boss_herod/UpdateAI, boss_houndmaster_loksey/Aggro, boss_huhuran/UpdateAI, boss_interrogator_vishas/Aggro, boss_interrogator_vishas/JustDied, boss_interrogator_vishas/KilledUnit, boss_interrogator_vishas/UpdateAI, boss_ironaya/Aggro, boss_jeklik/Aggro, boss_jeklik/JustDied, boss_jeklik/UpdateAI, boss_jindo/Aggro, boss_kurinnaxx/UpdateAI, boss_lethon/Aggro, boss_lethon/DoSpecialAbility, boss_maexxna/UpdateAI, boss_majordomo_executus/Aggro, boss_majordomo_executus/DomoEvent, boss_majordomo_executus/KilledUnit, boss_majordomo_executus/UpdateAI, boss_mandokir/Aggro, boss_mandokir/CheckRaptor, boss_mandokir/KilledUnit, boss_mandokir/UpdateAI, boss_marli/Aggro, boss_marli/JustDied, boss_marli/UpdateAI, boss_moam/Aggro, boss_moam/UpdateAI, boss_mr_smite/UpdateAI, boss_nefarian/JustDied, boss_nefarian/KilledUnit, boss_nefarian/UpdateAI, boss_noth/Aggro, boss_noth/JustDied, boss_noth/KilledUnit, boss_noth/SpawnBalcAdds, boss_noth/SpawnWarriorsAndRepeatEvent, boss_onyxia/Aggro#2, boss_onyxia/DoMovement, boss_onyxia/KilledUnit, boss_onyxia/PhaseTransition, boss_ossirian/Aggro, boss_ossirian/JustDied, boss_ossirian/KilledUnit, boss_patchwerk/Aggro, boss_patchwerk/JustDied, boss_patchwerk/KilledUnit, boss_patchwerk/UpdateAI, boss_postmaster_malown/Aggro, boss_postmaster_malown/KilledUnit, boss_razorgore/JustReachedHome, boss_razuvious/Aggro, boss_razuvious/JustDied, boss_razuvious/KilledUnit, boss_razuvious/UpdateAI, boss_sapphiron/UpdateAI, boss_sartura/Aggro, boss_sartura/JustDied, boss_sartura/KilledUnit, boss_sartura/UpdateAI, boss_sartura/UpdateAI#3, boss_skeram/Aggro, boss_skeram/JustDied, boss_skeram/KilledUnit, boss_skeram/UpdateAI, boss_taerar/Aggro, boss_taerar/DoSpecialAbility, boss_tendris_warpwood/Aggro, boss_thaddius/Aggro, boss_thaddius/Aggro#2, boss_thaddius/DamageTaken, boss_thaddius/DoPolarityShift, boss_thaddius/JustDied#3, boss_thaddius/KilledUnit, boss_thaddius/KilledUnit#2, boss_thaddius/KilledUnit#3, boss_thaddius/UpdateAI#3, boss_thaddius/UpdateTransitionPhase, boss_thermaplugg/Aggro, boss_thermaplugg/KilledUnit, boss_thermaplugg/UpdateAI, boss_twinemperors/JustReachedHome#2, boss_twinemperors/JustReachedHome#3, boss_twinemperors/KilledUnit, boss_twinemperors/KilledUnit#2, boss_twinemperors/UpdateEmperor, boss_twinemperors/UpdateEmperor#2, boss_vaelastrasz/BeginSpeech, boss_vaelastrasz/KilledUnit, boss_vaelastrasz/UpdateAI, boss_vaelastrasz/UpdateAI#3, boss_venoxis/JustDied, boss_venoxis/UpdateAI, boss_victor_nefarius/FailScepterRun, boss_victor_nefarius/HandleScepterRun, boss_victor_nefarius/StartScepterRun, boss_victor_nefarius/UpdateAI, boss_viscidus/SpellHit, boss_ysondre/Aggro, boss_ysondre/DoSpecialAbility, burning_steppes/Aggro, burning_steppes/DialogueUpdate, burning_steppes/EffectDummyCreature_spell_capture_grark, burning_steppes/SummonedCreatureJustDied, burning_steppes/UpdateAI, burning_steppes/WaypointReached, ChatHandler.DebugCommands/HandleDebugPlayScriptText, darkshore/Aggro, darkshore/Aggro#2, darkshore/ClearSleeping, darkshore/GossipSelect_npc_threshwackonator, darkshore/MoveInLineOfSight, darkshore/MoveInLineOfSight#2, darkshore/QuestAccept_npc_kerlonian, darkshore/QuestAccept_npc_prospector_remtravel, darkshore/QuestAccept_npc_therylune, darkshore/SetSleeping, darkshore/UpdateAI#2, darkshore/WaypointReached, darkshore/WaypointReached#2, darkshore/WaypointReached#3, desolace/Dialogue, desolace/WaypointReached#3, dreadsteed_ritual/EventEndedFail, dreadsteed_ritual/PhaseTwoEndedSuccess, dreadsteed_ritual/WaveSpawn, dun_morogh/SpellHit, durotar/UpdateAI, duskwood/Aggro#2, dustwallow_marsh/Aggro, dustwallow_marsh/QuestRewarded_npc_archmage_tervosh, dustwallow_marsh/UpdateAI#4, dustwallow_marsh/UpdateAI#6, dustwallow_marsh/WaypointReached, eastern_plaguelands/CompleteEvent, eastern_plaguelands/FailEvent, eastern_plaguelands/MovementInform#3, eastern_plaguelands/NewWave, eastern_plaguelands/SummonedCreatureJustDied, eastern_plaguelands/SummonedMovementInform#2, eastern_plaguelands/UpdateAI, eastern_plaguelands/UpdateAI#3, eastern_plaguelands/UpdateAI#4, eastern_plaguelands/UpdateAI#5, elwynn_forest/SpellHit, felwood/Aggro, felwood/Aggro#2, felwood/Dialogue, felwood/JustSummoned, felwood/JustSummoned#2, felwood/QuestAccept_npc_arei, felwood/WaypointReached, felwood/WaypointReached#2, feralas/EnterCombat, feralas/MoveInLineOfSight, feralas/OnEscortFailed, feralas/QuestAccept_npc_kindal_moonweaver, feralas/QuestAccept_npc_shay_leafrunner, feralas/SpriteDied, feralas/SpriteSaved, feralas/UpdateFollowerAI, gnomeregan/AttackedBy, gnomeregan/StartQuest, gnomeregan/UpdateEscortAI, gnomeregan/UpdateFollowerAI, gnomeregan/WaypointReached, gnomeregan/WaypointStart, GuardMgr/SummonGuard, hinterlands/Aggro, hinterlands/UpdateEscortAI, hinterlands/WaypointReached, instance_blackrock_depths/HandleBarPatrol, instance_blackrock_depths/HandleBarPatrons, instance_blackrock_depths/OnCreatureDeath, instance_blackrock_depths/SetData, instance_blackrock_spire/OnCreatureDeath, instance_blackrock_spire/Update, instance_blackwing_lair/OnCreatureDeath, instance_blackwing_lair/OnUse, instance_deadmines/Update, instance_dire_maul/Aggro#2, instance_dire_maul/goToFengus, instance_dire_maul/JustReachedHome, instance_dire_maul/MovementInform#2, instance_dire_maul/npc_mizzle_the_craftyAI, instance_dire_maul/OnCreatureDeath, instance_dire_maul/SetData, instance_dire_maul/UpdateAI#4, instance_dire_maul/UpdateAI#5, instance_molten_core/GOHello_go_rune_MC, instance_naxxramas.boss_kelthuzad/DoChains, instance_naxxramas.boss_kelthuzad/JustDied, instance_naxxramas.boss_kelthuzad/KilledUnit, instance_naxxramas.boss_kelthuzad/SpellHit#2, instance_naxxramas.boss_kelthuzad/StartEncounter, instance_naxxramas.boss_kelthuzad/UpdateP1, instance_naxxramas.boss_kelthuzad/UpdateP2P3, instance_naxxramas.Main/onNaxxramasAreaTrigger, instance_naxxramas.Main/UpdateAI#2, instance_scarlet_monastery/AreaTrigger_at_cathedral_entrance, instance_scarlet_monastery/SetData, instance_scarlet_monastery/Update, instance_stratholme/OnCreatureDeath, instance_stratholme/SetData, instance_stratholme/Update, instance_sunken_temple/DoSpawnAtalarionIfCan, instance_sunken_temple/OnCreatureEnterCombat, instance_sunken_temple/SetData, instance_temple_of_ahnqiraj/UpdateCThunWhisper, instance_wailing_caverns/SetData, loch_modan/JustSummoned, loch_modan/SummonedCreatureJustDied, loch_modan/WaypointReached, Map.ScriptCommands/ScriptCommand_Talk, mob_anubisath_sentinel/JustDied, mob_anubisath_sentinel/UpdateAI, moonglade/KilledUnit, moonglade/MovementInform, moonglade/SummonedMovementInform, moonglade/SummonedMovementInform#2, moonglade/UpdateAI, moonglade/UpdateAI#2, moonglade/UpdateEscortAI, moonglade/WaypointReached, npcs_special/Aggro#3, npcs_special/ReceiveEmote, npcs_special/SpellHit, npc_j_eevee/MovementInform, npc_j_eevee/ShoutFreedom, npc_j_eevee/UpdateAI, npc_j_eevee/UpdateAI#2, OutdoorPvPSI/DoSilithystYell, PetAI/DoAttack, quest_stormwind_rendezvous/GossipSelect_npc_reginald_windsor, quest_stormwind_rendezvous/MovementInform, quest_stormwind_rendezvous/UpdateAI, razorfen_downs/AttackedBy, razorfen_downs/QuestAccept_npc_belnistrasz, razorfen_downs/UpdateEscortAI, razorfen_downs/WaypointReached, razorfen_kraul/Aggro, razorfen_kraul/DoFindNewTuber, razorfen_kraul/EffectDummyCreature_npc_snufflenose_gopher, razorfen_kraul/npc_snufflenose_gopherAI, razorfen_kraul/QuestAccept_npc_willix_the_importer, razorfen_kraul/WaypointReached, redridge_mountains/QuestAccept_npc_corporal_keeshan, redridge_mountains/WaypointReached, redridge_mountains/WaypointStart, ruins_of_ahnqiraj/DamageTaken#2, ruins_of_ahnqiraj/UpdateAI#10, scourge_invasion/JustDied#3, scourge_invasion/OnScriptEventHappened, scourge_invasion/UpdateAI#2, scourge_invasion/UpdateAI#8, ScriptedInstance/DoNextDialogueStep, searing_gorge/UpdateAI, silithus/MovementInform, silithus/npc_colossusAI, silithus/OnActivateBySpell, silithus/SpellHit, silithus/StartEvent, silithus/SummonedMovementInform, silithus/UpdateAI#4, silithus/UpdateAI#7, silverpine_forest/Aggro, silverpine_forest/QuestAccept_npc_deathstalker_erland, silverpine_forest/WaypointReached, stormwind_city/DamageTaken#2, stormwind_city/QuestAccept_npc_dashel_stonefist, stormwind_city/UpdateAI, stratholme/Reset#2, sunken_temple/npc_malfurionAI, sunken_temple/UpdateAI, swamp_of_sorrows/Aggro, swamp_of_sorrows/QuestAccept_npc_galen_goodward, swamp_of_sorrows/UpdateEscortAI, swamp_of_sorrows/WaypointReached, swamp_of_sorrows/WaypointStart, tanaris/QuestRewarded_npc_yehkinya, tanaris/UpdateEscortAI, tanaris/UpdateFollowerAI, teldrassil/DoComplete, teldrassil/MoveInLineOfSight, the_barrens/Aggro, the_barrens/Aggro#2, the_barrens/CanStartEvent, the_barrens/QuestAccept_npc_gilthares, the_barrens/QuestAccept_npc_wizzlecranks_shredder, the_barrens/UpdateAI#2, the_barrens/UpdateEscortAI, the_barrens/WaypointReached, the_barrens/WaypointReached#2, the_barrens/WaypointStart, thousand_needles/QuestAccept_npc_lakota_windsong, thousand_needles/QuestAccept_npc_paoka_swiftmountain, thousand_needles/WaypointReached, thousand_needles/WaypointReached#2, ThreatListCopier.battleground_alterac/Aggro#10, ThreatListCopier.battleground_alterac/Aggro#11, ThreatListCopier.battleground_alterac/Aggro#12, ThreatListCopier.battleground_alterac/Aggro#13, ThreatListCopier.battleground_alterac/EnterEvadeMode#2, ThreatListCopier.battleground_alterac/EnterEvadeMode#3, ThreatListCopier.battleground_alterac/GossipSelect_npc_AVBlood_collector, ThreatListCopier.battleground_alterac/KilledUnit, ThreatListCopier.battleground_alterac/Reset#16, ThreatListCopier.battleground_alterac/Reset#18, ThreatListCopier.battleground_alterac/UpdateAI#12, ThreatListCopier.battleground_alterac/UpdateAI#13, ThreatListCopier.battleground_alterac/UpdateAI#14, ThreatListCopier.battleground_alterac/UpdateAI#15, ThreatListCopier.battleground_alterac/UpdateAI#17, ThreatListCopier.battleground_alterac/UpdateEscortAI, ThreatListCopier.battleground_alterac/UpdateEscortAI#3, ThreatListCopier.battleground_alterac/UpdateEscortAI#4, ThreatListCopier.battleground_alterac/UpdateEscortAI#5, ThreatListCopier.battleground_alterac/WaypointReached, ThreatListCopier.battleground_alterac/WaypointReached#3, ThreatListCopier.battleground_alterac/WaypointReached#4, ThreatListCopier.battleground_alterac/WaypointReached#5, ThreatListCopier.boss_ragnaros/KilledUnit, ThreatListCopier.boss_ragnaros/UpdateAI, ungoro_crater/Aggro, ungoro_crater/ClearFaint, ungoro_crater/SetFaint, ungoro_crater/SpellHit#2, ungoro_crater/UpdateFollowerAI, ungoro_crater/WaypointReached, Unit.SpellAuras/HandleAuraDummy, wailing_caverns/AttackedBy, wailing_caverns/UpdateEscortAI, wailing_caverns/WaypointReached, westfall/QuestAccept_npc_daphne_stilwell, westfall/Reset, westfall/WaypointReached, wetlands/Aggro, wetlands/DamageTaken, wetlands/JustRespawned, wetlands/UpdateEscortAI, winterspring/SpellHit, world_event_wareffort/EnterCombat, world_event_wareffort/EnterEvadeMode, world_event_wareffort/JustDied, world_event_wareffort/KilledUnit, world_event_wareffort/UpdateAI#2, world_event_wareffort/UpdateAI#3, zulfarrak/DestroyDoor, zulfarrak/MovementInform, zulfarrak/OnTrigger_at_antusul, zulfarrak/OnTrigger_at_zumrah, zulfarrak/UpdateAI | — |
| DoOrSimulateScriptTextForMap | function | Log.Main/Out, Map.Main/GetId, Map.Main/PlayDirectSoundToMap, Map.Main/SendMonsterTextToMap, ObjectMgr/GetBroadcastTextLocale, ObjectMgr/GetMangosStringLocale, Unit.Main/HandleEmote | boss_kurinnaxx/JustDied, instance_naxxramas.Main/onNaxxramasAreaTrigger, instance_naxxramas.Main/Update | — |
| RegisterSelf | method | Log.Main/Out | arathi_highlands/AddSC_arathi_highlands, areatrigger_scripts/AddSC_areatrigger_scripts, arena_challenge_ai/AddSC_blackrock_depths_arena_challenge, ashenvale/AddSC_ashenvale, azshara/AddSC_azshara, blackrock_depths/AddSC_blackrock_depths, blasted_lands/AddSC_blasted_lands, boss_anubrekhan/AddSC_boss_anubrekhan, boss_anubshiah/AddSC_boss_anubshiah, boss_arcanist_doan/AddSC_boss_arcanist_doan, boss_archaedas/AddSC_boss_archaedas, boss_arlokk/AddSC_boss_arlokk, boss_ayamiss/AddSC_boss_ayamiss, boss_baroness_anastari/AddSC_boss_baroness_anastari, boss_baron_geddon/AddSC_boss_baron_geddon, boss_broodlord_lashlayer/AddSC_boss_broodlord, boss_bug_trio/AddSC_bug_trio, boss_buru/AddSC_boss_buru, boss_cannon_master_willey/AddSC_boss_cannon_master_willey, boss_celebras_the_cursed/AddSC_boss_celebras_the_cursed, boss_chromaggus/AddSC_boss_chromaggus, boss_cthun/AddSC_boss_cthun, boss_dathrohan_balnazzar/AddSC_boss_dathrohan_balnazzar, boss_doctor_theolen_krastinov/AddSC_boss_theolenkrastinov, boss_dragon_of_nightmare/AddSC_dragons_of_nightmare, boss_ebonroc/AddSC_boss_ebonroc, boss_emperor_dagran_thaurissan/AddSC_boss_draganthaurissan, boss_faerlina/AddSC_boss_faerlina, boss_fankriss/AddSC_boss_fankriss, boss_firemaw/AddSC_boss_firemaw, boss_flamegor/AddSC_boss_flamegor, boss_four_horsemen/AddSC_boss_four_horsemen, boss_gahzranka/AddSC_boss_gahzranka, boss_garr/AddSC_boss_garr, boss_gehennas/AddSC_boss_gehennas, boss_general_angerforge/AddSC_boss_general_angerforge, boss_gluth/AddSC_boss_gluth, boss_golemagg/AddSC_boss_golemagg, boss_gordok_king/AddSC_npc_king_gordok, boss_gorosh_the_dervish/AddSC_boss_gorosh_the_dervish, boss_gothik/AddSC_boss_gothik, boss_grizzle/AddSC_boss_grizzle, boss_grobbulus/AddSC_boss_grobbulus, boss_hakkar/AddSC_boss_hakkar, boss_halycon/AddSC_boss_halycon, boss_heigan/AddSC_boss_heigan, boss_herod/AddSC_boss_herod, boss_highlord_omokk/AddSC_boss_highlordomokk, boss_high_inquisitor_fairbanks/AddSC_boss_high_inquisitor_fairbanks, boss_high_interrogator_gerstahn/AddSC_boss_high_interrogator_gerstahn, boss_houndmaster_loksey/AddSC_boss_houndmaster_loksey, boss_huhuran/AddSC_boss_huhuran, boss_illucia_barov/AddSC_boss_illuciabarov, boss_immol_thar/AddSC_boss_immol_thar, boss_instructor_malicia/AddSC_boss_instructormalicia, boss_interrogator_vishas/AddSC_boss_interrogator_vishas, boss_ironaya/AddSC_boss_ironaya, boss_jandice_barov/AddSC_boss_jandicebarov, boss_jeklik/AddSC_boss_jeklik, boss_jindo/AddSC_boss_jindo, boss_kurinnaxx/AddSC_boss_kurinnaxx, boss_landslide/AddSC_boss_landslide, boss_loatheb/AddSC_boss_loatheb, boss_lord_alexei_barov/AddSC_boss_lordalexeibarov, boss_lorekeeper_polkelt/AddSC_boss_lorekeeperpolkelt, boss_lucifron/AddSC_boss_lucifron, boss_maexxna/AddSC_boss_maexxna, boss_magistrate_barthilas/AddSC_boss_magistrate_barthilas, boss_magmus/AddSC_boss_magmus, boss_majordomo_executus/AddSC_boss_majordomo, boss_maleki_the_pallid/AddSC_boss_maleki_the_pallid, boss_mandokir/AddSC_boss_mandokir, boss_marli/AddSC_boss_marli, boss_moam/AddSC_boss_moam, boss_mr_smite/AddSC_boss_mr_smite, boss_nefarian/AddSC_boss_nefarian, boss_nerubenkan/AddSC_boss_nerubenkan, boss_noth/AddSC_boss_noth, boss_noxxion/AddSC_boss_noxxion, boss_omen/AddSC_boss_omen, boss_onyxia/AddSC_boss_onyxia, boss_order_of_silver_hand/AddSC_boss_order_of_silver_hand, boss_ossirian/AddSC_boss_ossirian, boss_ouro/AddSC_boss_ouro, boss_overlord_wyrmthalak/AddSC_boss_overlordwyrmthalak, boss_patchwerk/AddSC_boss_patchwerk, boss_postmaster_malown/AddSC_boss_postmaster_malown, boss_ramstein_the_gorger/AddSC_boss_ramstein_the_gorger, boss_ras_frostwhisper/AddSC_boss_rasfrost, boss_razorgore/AddSC_boss_razorgore, boss_razuvious/AddSC_boss_razuvious, boss_renataki/AddSC_boss_renataki, boss_sapphiron/AddSC_boss_sapphiron, boss_sartura/AddSC_boss_sartura, boss_shadow_hunter_voshgajin/AddSC_boss_shadowvosh, boss_shazzrah/AddSC_boss_shazzrah, boss_skeram/AddSC_boss_skeram, boss_sulfuron_harbinger/AddSC_boss_sulfuron, boss_tendris_warpwood/AddSC_boss_tendris_warpwood, boss_thaddius/AddSC_boss_thaddius, boss_thermaplugg/AddSC_boss_thermaplugg, boss_the_beast/AddSC_boss_thebeast, boss_the_ravenian/AddSC_boss_theravenian, boss_timmy_the_cruel/AddSC_boss_timmy_the_cruel, boss_tomb_of_seven/AddSC_boss_tomb_of_seven, boss_twinemperors/AddSC_boss_twinemperors, boss_urok/AddSC_boss_urok, boss_vaelastrasz/AddSC_boss_vael, boss_vectus/AddSC_boss_vectus, boss_venoxis/AddSC_boss_venoxis, boss_victor_nefarius/AddSC_boss_victor_nefarius, boss_viscidus/AddSC_boss_viscidus, boss_warmaster_voone/AddSC_boss_warmastervoone, boss_zevrim/AddSC_boss_zevrim, burning_steppes/AddSC_burning_steppes, custom_creatures/AddSC_custom_creatures, darkshore/AddSC_darkshore, deadmines/AddSC_deadmines, desolace/AddSC_desolace, dreadsteed_ritual/AddSC_dreadsteed_ritual, dun_morogh/AddSC_dun_morogh, durotar/AddSC_durotar, duskwood/AddSC_duskwood, dustwallow_marsh/AddSC_dustwallow_marsh, eastern_plaguelands/AddSC_eastern_plaguelands, elemental_invasions/AddSC_elemental_invasions, elwynn_forest/AddSC_elwynn_forest, felwood/AddSC_felwood, feralas/AddSC_feralas, fireworks_show/AddSC_event_fireworks, gnomeregan/AddSC_gnomeregan, go_scripts/AddSC_go_scripts, hillsbrad_foothills/AddSC_hillsbrad_foothills, hinterlands/AddSC_hinterlands, instance_blackfathom_deeps/AddSC_instance_blackfathom_deeps, instance_blackrock_depths/AddSC_instance_blackrock_depths, instance_blackrock_spire/AddSC_instance_blackrock_spire, instance_blackwing_lair/AddSC_instance_blackwing_lair, instance_deadmines/AddSC_instance_deadmines, instance_dire_maul/AddSC_instance_dire_maul, instance_gnomeregan/AddSC_instance_gnomeregan, instance_maraudon/AddSC_instance_maraudon, instance_molten_core/AddSC_instance_molten_core, instance_naxxramas.boss_kelthuzad/AddSC_boss_kelthuzad, instance_naxxramas.Main/AddSC_instance_naxxramas, instance_onyxia_lair/AddSC_instance_onyxia_lair, instance_razorfen_downs/AddSC_instance_razorfen_downs, instance_razorfen_kraul/AddSC_instance_razorfen_kraul, instance_ruins_of_ahnqiraj/AddSC_instance_ruins_of_ahnqiraj, instance_scarlet_monastery/AddSC_instance_scarlet_monastery, instance_scholomance/AddSC_instance_scholomance, instance_shadowfang_keep/AddSC_instance_shadowfang_keep, instance_stratholme/AddSC_instance_stratholme, instance_sunken_temple/AddSC_instance_sunken_temple, instance_temple_of_ahnqiraj/AddSC_instance_temple_of_ahnqiraj, instance_uldaman/AddSC_instance_uldaman, instance_wailing_caverns/AddSC_instance_wailing_caverns, instance_zulfarrak/AddSC_instance_zulfarrak, instance_zulgurub/AddSC_instance_zulgurub, loch_modan/AddSC_loch_modan, mob_anubisath_sentinel/AddSC_mob_anubisath_sentinel, molten_core/AddSC_molten_core, moonglade/AddSC_moonglade, mulgore/AddSC_mulgore, npcs_special/AddSC_npcs_special, npc_j_eevee/AddSC_npc_j_eevee, npc_sandstalker/AddSC_npc_sandstalker, quest_stormwind_rendezvous/AddSC_quest_stormwind_rendezvous, razorfen_downs/AddSC_razorfen_downs, razorfen_kraul/AddSC_razorfen_kraul, redridge_mountains/AddSC_redridge_mountains, ruins_of_ahnqiraj/AddSC_ruins_of_ahnqiraj, scholo_trash/AddSC_scholo_trash, scourge_invasion/AddSC_scourge_invasion, scripts_battlegrounds_battleground/AddSC_battleground, searing_gorge/AddSC_searing_gorge, silithus/AddSC_silithus, silverpine_forest/AddSC_silverpine_forest, spell_druid/AddSC_druid_spell_scripts, spell_hunter/AddSC_hunter_spell_scripts, spell_item/AddSC_item_spell_scripts, spell_mage/AddSC_mage_spell_scripts, spell_paladin/AddSC_paladin_spell_scripts, spell_priest/AddSC_priest_spell_scripts, spell_rogue/AddSC_rogue_spell_scripts, spell_shaman/AddSC_shaman_spell_scripts, spell_special/AddSC_special_spell_scripts, spell_warlock/AddSC_warlock_spell_scripts, spell_warrior/AddSC_warrior_spell_scripts, stonetalon_mountains/AddSC_stonetalon_mountains, stormwind_city/AddSC_stormwind_city, stranglethorn_vale/AddSC_stranglethorn_vale, stratholme/AddSC_stratholme, sunken_temple/AddSC_sunken_temple, swamp_of_sorrows/AddSC_swamp_of_sorrows, tanaris/AddSC_tanaris, teldrassil/AddSC_teldrassil, the_barrens/AddSC_the_barrens, thousand_needles/AddSC_thousand_needles, ThreatListCopier.battleground_alterac/AddSC_bg_alterac, ThreatListCopier.boss_ragnaros/AddSC_boss_ragnaros, totems/AddSC_Totems, ubrs_trash/AddSC_ubrs_trash, uldaman/AddSC_uldaman, undercity/AddSC_undercity, ungoro_crater/AddSC_ungoro_crater, wailing_caverns/AddSC_wailing_caverns, western_plaguelands/AddSC_western_plaguelands, westfall/AddSC_westfall, wetlands/AddSC_wetlands, winterspring/AddSC_winterspring, world_event_wareffort/AddSC_war_effort, zulfarrak/AddSC_zulfarrak, zulgurub_trash/AddSC_zg_trash | — |
| GetTargetByType | function | Creature.Main/SelectAttackingTarget, Creature.Main/ToCreature, InstanceData/GetData64, Map.Main/GetCreature, Map.Main/GetGameObject, Map.Main/GetInstanceData, Map.Main/GetScriptedMapEvent, Map.Main/GetSourceObject, Map.Main/GetTargetObject, Map.Main/GetWorldObject, Object/GetEntry, Object/ToCreature, ObjectGuid/ObjectGuid#3, ObjectGuid/ObjectGuid#5, ObjectMgr/GetCreatureData, ObjectMgr/GetGOData, Unit.Main/FindFriendlyUnitCC, Unit.Main/FindFriendlyUnitMissingBuff, Unit.Main/FindLowestHpFriendlyUnit, Unit.Main/GetCharmerOrOwnerOrSelf, Unit.Main/GetOwner, Unit.Main/GetVictim, Unit.Main/SelectRandomFriendlyTarget, Unit.Main/ToUnit, WorldObject.Object/FindNearestCreature, WorldObject.Object/FindNearestFriendlyPlayer, WorldObject.Object/FindNearestGameObject, WorldObject.Object/FindNearestHostilePlayer, WorldObject.Object/FindNearestPlayer, WorldObject.Object/FindRandomCreature, WorldObject.Object/FindRandomGameObject, WorldObject.Object/GetMap, ZoneScript/GetCreature, ZoneScript/GetGameObject | CreatureAI/DoSpellsListCasts, Map.Main/FindScriptFinalTargets, Map.ScriptCommands/ScriptCommand_ModifyThreat, Map.ScriptCommands/ScriptCommand_SummonCreature | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `areatrigger_template`: id smallint(4) unsigned PK, build smallint(4) unsigned PK, name varchar(128)?, map_id smallint(3) unsigned, x float, y float, z float, radius float, box_x float, box_y float, box_z float, box_orientation float, cooldown int(10) unsigned, condition_id int(10) unsigned, script_id int(10) unsigned, script_name varchar(64)
- `creature_ai_events`: id int(11) unsigned PK, creature_id int(11) unsigned, condition_id mediumint(8) unsigned, event_type tinyint(5) unsigned, event_inverse_phase_mask int(11), event_chance tinyint(3) unsigned, event_flags int(3) unsigned, event_param1 int(11), event_param2 int(11), event_param3 int(11), event_param4 int(11), action1_script int(11) unsigned, action2_script int(11) unsigned, action3_script int(11) unsigned, comment varchar(255)
- `creature_ai_scripts`: id int(10) unsigned, delay int(10) unsigned, priority tinyint(3) unsigned, command tinyint(3) unsigned, datalong int(10) unsigned, datalong2 int(10) unsigned, datalong3 int(10) unsigned, datalong4 int(10) unsigned, target_param1 int(10) unsigned, target_param2 int(10) unsigned, target_type tinyint(3) unsigned, data_flags tinyint(3) unsigned, dataint int(11), dataint2 int(11), dataint3 int(11), dataint4 int(11), x float, y float, z float, o float, condition_id mediumint(8) unsigned, comments varchar(255)
- `custom_texts`: entry mediumint(8) PK, content_default text, content_loc1 text?, content_loc2 text?, content_loc3 text?, content_loc4 text?, content_loc5 text?, content_loc6 text?, content_loc7 text?, content_loc8 text?, sound mediumint(8) unsigned, type tinyint(3) unsigned, language tinyint(3) unsigned, emote smallint(5) unsigned, comment text?
- `gameobject_template`: entry mediumint(8) unsigned PK, patch tinyint(3) unsigned PK, type tinyint(3) unsigned, displayId mediumint(8) unsigned, name varchar(100), icon varchar(100), faction smallint(5) unsigned, flags int(10) unsigned, size float, data0 int(10), data1 int(11), data2 int(10), data3 int(10), data4 int(10), data5 int(10), data6 int(11), data7 int(10), data8 int(10), data9 int(10), data10 int(10), data11 int(10), data12 int(10), data13 int(10), data14 int(10), data15 int(10), data16 int(10), data17 int(10), data18 int(10), data19 int(10), data20 int(10), data21 int(10), data22 int(10), data23 int(10), mingold mediumint(8) unsigned, maxgold mediumint(8) unsigned, script_name varchar(64)
- `quest_template`: entry mediumint(8) unsigned PK, patch tinyint(3) unsigned PK, Method tinyint(3) unsigned, ZoneOrSort smallint(6), MinLevel tinyint(3) unsigned, MaxLevel tinyint(3) unsigned, QuestLevel tinyint(3) unsigned, Type smallint(5) unsigned, RequiredClasses smallint(5) unsigned, RequiredRaces smallint(5) unsigned, RequiredSkill smallint(5) unsigned, RequiredSkillValue smallint(5) unsigned, RequiredCondition mediumint(8) unsigned, RepObjectiveFaction smallint(5) unsigned, RepObjectiveValue mediumint(9), RequiredMinRepFaction smallint(5) unsigned, RequiredMinRepValue mediumint(9), RequiredMaxRepFaction smallint(5) unsigned, RequiredMaxRepValue mediumint(9), SuggestedPlayers tinyint(3) unsigned, LimitTime int(10) unsigned, QuestFlags smallint(5) unsigned, SpecialFlags tinyint(3) unsigned, PrevQuestId mediumint(9), NextQuestId mediumint(9), ExclusiveGroup mediumint(9), BreadcrumbForQuestId mediumint(9) unsigned, NextQuestInChain mediumint(8) unsigned, SrcItemId mediumint(8) unsigned, SrcItemCount tinyint(3) unsigned, SrcSpell smallint(5) unsigned, Title text?, Details text?, Objectives text?, OfferRewardText text?, RequestItemsText text?, EndText text?, ObjectiveText1 text?, ObjectiveText2 text?, ObjectiveText3 text?, ObjectiveText4 text?, ReqItemId1 mediumint(8) unsigned, ReqItemId2 mediumint(8) unsigned, ReqItemId3 mediumint(8) unsigned, ReqItemId4 mediumint(8) unsigned, ReqItemCount1 smallint(5) unsigned, ReqItemCount2 smallint(5) unsigned, ReqItemCount3 smallint(5) unsigned, ReqItemCount4 smallint(5) unsigned, ReqSourceId1 mediumint(8) unsigned, ReqSourceId2 mediumint(8) unsigned, ReqSourceId3 mediumint(8) unsigned, ReqSourceId4 mediumint(8) unsigned, ReqSourceCount1 mediumint(8) unsigned, ReqSourceCount2 mediumint(8) unsigned, ReqSourceCount3 mediumint(8) unsigned, ReqSourceCount4 mediumint(8) unsigned, ReqCreatureOrGOId1 mediumint(9), ReqCreatureOrGOId2 mediumint(9), ReqCreatureOrGOId3 mediumint(9), ReqCreatureOrGOId4 mediumint(9), ReqCreatureOrGOCount1 smallint(5) unsigned, ReqCreatureOrGOCount2 smallint(5) unsigned, ReqCreatureOrGOCount3 smallint(5) unsigned, ReqCreatureOrGOCount4 smallint(5) unsigned, ReqSpellCast1 smallint(5) unsigned, ReqSpellCast2 smallint(5) unsigned, ReqSpellCast3 smallint(5) unsigned, ReqSpellCast4 smallint(5) unsigned, RewChoiceItemId1 mediumint(8) unsigned, RewChoiceItemId2 mediumint(8) unsigned, RewChoiceItemId3 mediumint(8) unsigned, RewChoiceItemId4 mediumint(8) unsigned, RewChoiceItemId5 mediumint(8) unsigned, RewChoiceItemId6 mediumint(8) unsigned, RewChoiceItemCount1 smallint(5) unsigned, RewChoiceItemCount2 smallint(5) unsigned, RewChoiceItemCount3 smallint(5) unsigned, RewChoiceItemCount4 smallint(5) unsigned, RewChoiceItemCount5 smallint(5) unsigned, RewChoiceItemCount6 smallint(5) unsigned, RewItemId1 mediumint(8) unsigned, RewItemId2 mediumint(8) unsigned, RewItemId3 mediumint(8) unsigned, RewItemId4 mediumint(8) unsigned, RewItemCount1 smallint(5) unsigned, RewItemCount2 smallint(5) unsigned, RewItemCount3 smallint(5) unsigned, RewItemCount4 smallint(5) unsigned, RewRepFaction1 smallint(5) unsigned, RewRepFaction2 smallint(5) unsigned, RewRepFaction3 smallint(5) unsigned, RewRepFaction4 smallint(5) unsigned, RewRepFaction5 smallint(5) unsigned, RewRepValue1 mediumint(9), RewRepValue2 mediumint(9), RewRepValue3 mediumint(9), RewRepValue4 mediumint(9), RewRepValue5 mediumint(9), RewRepSpilloverMask tinyint(3) unsigned, RewXP mediumint(9) unsigned, RewOrReqMoney int(11), RewMoneyMaxLevel int(10) unsigned, RewSpell smallint(5) unsigned, RewSpellCast smallint(5) unsigned, RewMailTemplateId mediumint(8), RewMailDelaySecs int(11) unsigned, RewMailMoney int(10) unsigned, PointMapId smallint(5) unsigned, PointX float, PointY float, PointOpt mediumint(8) unsigned, DetailsEmote1 smallint(5) unsigned, DetailsEmote2 smallint(5) unsigned, DetailsEmote3 smallint(5) unsigned, DetailsEmote4 smallint(5) unsigned, DetailsEmoteDelay1 int(11) unsigned, DetailsEmoteDelay2 int(11) unsigned, DetailsEmoteDelay3 int(11) unsigned, DetailsEmoteDelay4 int(11) unsigned, IncompleteEmote smallint(5) unsigned, CompleteEmote smallint(5) unsigned, OfferRewardEmote1 smallint(5) unsigned, OfferRewardEmote2 smallint(5) unsigned, OfferRewardEmote3 smallint(5) unsigned, OfferRewardEmote4 smallint(5) unsigned, OfferRewardEmoteDelay1 int(11) unsigned, OfferRewardEmoteDelay2 int(11) unsigned, OfferRewardEmoteDelay3 int(11) unsigned, OfferRewardEmoteDelay4 int(11) unsigned, StartScript mediumint(8) unsigned, CompleteScript mediumint(8) unsigned
- `script_escort_data`: creature_id int(11)?, quest int(11)?, escort_faction int(11)?
- `script_texts`: entry mediumint(8) PK, content_default text, content_loc1 text?, content_loc2 text?, content_loc3 text?, content_loc4 text?, content_loc5 text?, content_loc6 text?, content_loc7 text?, content_loc8 text?, sound mediumint(8) unsigned, type tinyint(3) unsigned, language tinyint(3) unsigned, emote smallint(5) unsigned, comment text?
- `script_waypoint`: entry mediumint(8) unsigned PK, pointid mediumint(8) unsigned PK, location_x float, location_y float, location_z float, waittime int(10) unsigned, point_comment text?
- `scripted_event_id`: id mediumint(8) PK, script_name char(64)
- `spell_template`: entry mediumint(8) unsigned PK, build smallint(4) unsigned PK, school int(4) unsigned, category int(4) unsigned, castUI int(4) unsigned, dispel int(4) unsigned, mechanic int(4) unsigned, attributes int(4) unsigned, attributesEx int(4) unsigned, attributesEx2 int(4) unsigned, attributesEx3 int(4) unsigned, attributesEx4 int(4) unsigned, stances int(4) unsigned, stancesNot int(4) unsigned, targets int(4) unsigned, targetCreatureType int(4) unsigned, requiresSpellFocus int(4) unsigned, casterAuraState int(4) unsigned, targetAuraState int(4) unsigned, castingTimeIndex int(4) unsigned, recoveryTime int(4) unsigned, categoryRecoveryTime int(4) unsigned, interruptFlags int(4) unsigned, auraInterruptFlags int(4) unsigned, channelInterruptFlags int(4) unsigned, procFlags int(4) unsigned, procChance int(4) unsigned, procCharges int(4) unsigned, maxLevel int(4) unsigned, baseLevel int(4) unsigned, spellLevel int(4) unsigned, durationIndex int(4) unsigned, powerType int(4) unsigned, manaCost int(4) unsigned, manCostPerLevel int(4) unsigned, manaPerSecond int(4) unsigned, manaPerSecondPerLevel int(4) unsigned, rangeIndex int(4) unsigned, speed float, modelNextSpell int(4) unsigned, stackAmount int(4) unsigned, totem1 int(4) unsigned, totem2 int(4) unsigned, reagent1 int(4), reagent2 int(4), reagent3 int(4), reagent4 int(4), reagent5 int(4), reagent6 int(4), reagent7 int(4), reagent8 int(4), reagentCount1 int(4) unsigned, reagentCount2 int(4) unsigned, reagentCount3 int(4) unsigned, reagentCount4 int(4) unsigned, reagentCount5 int(4) unsigned, reagentCount6 int(4) unsigned, reagentCount7 int(4) unsigned, reagentCount8 int(4) unsigned, equippedItemClass int(4), equippedItemSubClassMask int(4), equippedItemInventoryTypeMask int(4), effect1 int(4) unsigned, effect2 int(4) unsigned, effect3 int(4) unsigned, effectDieSides1 int(4), effectDieSides2 int(4), effectDieSides3 int(4), effectBaseDice1 int(4) unsigned, effectBaseDice2 int(4) unsigned, effectBaseDice3 int(4) unsigned, effectDicePerLevel1 float, effectDicePerLevel2 float, effectDicePerLevel3 float, effectRealPointsPerLevel1 float, effectRealPointsPerLevel2 float, effectRealPointsPerLevel3 float, effectBasePoints1 int(4), effectBasePoints2 int(4), effectBasePoints3 int(4), effectBonusCoefficient1 float, effectBonusCoefficient2 float, effectBonusCoefficient3 float, effectMechanic1 int(4) unsigned, effectMechanic2 int(4) unsigned, effectMechanic3 int(4) unsigned, effectImplicitTargetA1 int(4) unsigned, effectImplicitTargetA2 int(4) unsigned, effectImplicitTargetA3 int(4) unsigned, effectImplicitTargetB1 int(4) unsigned, effectImplicitTargetB2 int(4) unsigned, effectImplicitTargetB3 int(4) unsigned, effectRadiusIndex1 int(4) unsigned, effectRadiusIndex2 int(4) unsigned, effectRadiusIndex3 int(4) unsigned, effectApplyAuraName1 int(4) unsigned, effectApplyAuraName2 int(4) unsigned, effectApplyAuraName3 int(4) unsigned, effectAmplitude1 int(4) unsigned, effectAmplitude2 int(4) unsigned, effectAmplitude3 int(4) unsigned, effectMultipleValue1 float, effectMultipleValue2 float, effectMultipleValue3 float, effectChainTarget1 int(4) unsigned, effectChainTarget2 int(4) unsigned, effectChainTarget3 int(4) unsigned, effectItemType1 bigint(20) unsigned, effectItemType2 bigint(20) unsigned, effectItemType3 bigint(20) unsigned, effectMiscValue1 int(4), effectMiscValue2 int(4), effectMiscValue3 int(4), effectTriggerSpell1 int(4) unsigned, effectTriggerSpell2 int(4) unsigned, effectTriggerSpell3 int(4) unsigned, effectPointsPerComboPoint1 float, effectPointsPerComboPoint2 float, effectPointsPerComboPoint3 float, spellVisual1 int(4) unsigned, spellVisual2 int(4) unsigned, spellIconId int(4) unsigned, activeIconId int(4) unsigned, spellPriority int(4) unsigned, name varchar(256), nameFlags int(4) unsigned, nameSubtext varchar(256), nameSubtextFlags int(4) unsigned, description varchar(1024), descriptionFlags int(4) unsigned, auraDescription varchar(512), auraDescriptionFlags int(4) unsigned, manaCostPercentage int(4) unsigned, startRecoveryCategory int(4) unsigned, startRecoveryTime int(4) unsigned, minTargetLevel int(4) unsigned, maxTargetLevel int(4) unsigned, spellFamilyName int(4) unsigned, spellFamilyFlags bigint(20) unsigned, maxAffectedTargets int(4) unsigned, dmgClass int(4) unsigned, preventionType int(4) unsigned, stanceBarOrder int(4), dmgMultiplier1 float, dmgMultiplier2 float, dmgMultiplier3 float, minFactionId int(4) unsigned, minReputation int(4) unsigned, requiredAuraVision int(4) unsigned, customFlags int(10) unsigned, script_name varchar(64)

*`?` = nullable, `PK` = primary key column.*

