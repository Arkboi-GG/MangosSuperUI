# ScriptInfo

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ScriptInfo

**ScriptInfo** is a Plain Old Data Structure (POD) that defines the schema and memory layout for individual steps within the server’s generic scripting system. It acts as the bridge between database-defined script actions (stored in tables like `scripted_areatrigger`, `gameobject_scripts`, etc.) and the runtime execution engine.

Each `ScriptInfo` instance represents a single command to be executed by a `WorldObject` (creature, game object, player, or map). The structure encapsulates:
1.  **Control Flow:** Execution delay, conditional requirements, and the specific command ID.
2.  **Command Parameters:** A large union (`union`) that holds the specific arguments for the command identified by `command`. Because different commands require different data (e.g., a "Talk" command needs text IDs, while a "Teleport" command needs coordinates), the union allows efficient storage of only the relevant fields for the active command.
3.  **Targeting Logic:** Fields (`target_type`, `target_param1`, `target_param2`) that define how the runtime resolves the source and target objects for the command.
4.  **Spatial Data:** Coordinates (`x`, `y`, `z`, `o`) used by movement, teleportation, and summoning commands.

This unit is purely declarative; it contains no logic for executing the scripts. Its sole responsibility is to hold the configuration data loaded from the database and provide a helper method to extract GameObject GUIDs for validation purposes.

## Member-by-Member Behavior

### Construction and Initialization
**`ScriptInfo()`**
The default constructor initializes all members to zero. Crucially, it uses `memset` to zero out the `raw.data` array within the union. This ensures that regardless of which union member is accessed later, the underlying memory is clean, preventing garbage values from leaking into script parameters if a command type is misconfigured or changed dynamically.

### Helper Methods
**`GetGOGuid()`**
This method provides a unified way to retrieve the `db_guid` of a GameObject involved in the script step. It inspects the `command` field and returns the corresponding GUID from the specific union member:
*   `SCRIPT_COMMAND_RESPAWN_GAMEOBJECT`: Returns `respawnGo.goGuid`.
*   `SCRIPT_COMMAND_DESPAWN_GAMEOBJECT`: Returns `despawnGo.goGuid`.
*   `SCRIPT_COMMAND_LOAD_GAMEOBJECT_SPAWN`: Returns `loadGo.goGuid`.
*   `SCRIPT_COMMAND_OPEN_DOOR`: Returns `openDoor.goGuid`.
*   `SCRIPT_COMMAND_CLOSE_DOOR`: Returns `closeDoor.goGuid`.
*   All other commands return `0`.

This abstraction simplifies validation logic in the calling units, allowing them to check if a valid GameObject GUID is present for commands that manipulate GameObjects, without needing to know the internal layout of every union variant.

## Cross-Unit Boundaries

### Called By
*   **`ScriptMgr/LoadScripts`**: This is the primary consumer. During server startup or reload, `ScriptMgr` reads script rows from various database tables. For each row, it constructs a `ScriptInfo` object, populating the fields based on the column values. `ScriptMgr` calls `GetGOGuid()` to validate that commands requiring a GameObject GUID actually have one specified in the database.
*   **`eastern_plaguelands/EffectDummyGameObj_go_mark_of_detonation`**: This specific script handler likely creates or accesses `ScriptInfo` instances to trigger scripted events related to the "Mark of Detonation" game object in the Eastern Plaguelands zone.
*   **`Spell.Effects/EffectDummy`**: When a spell with the `SPELL_EFFECT_DUMMY` effect is cast, the spell system may invoke scripts. This unit interacts with `ScriptInfo` to execute the associated script steps.
*   **`Unit.SpellAuras/HandleAuraDummy`**: Similar to `EffectDummy`, when an aura with the `SPELL_AURA_DUMMY` type is applied or ticked, this handler uses `ScriptInfo` to run the configured script actions.
*   **`ThreatListCopier.battleground_alterac/UpdateEscortAI#4`**: This appears to be a specific AI update routine for the Alterac Valley battleground escort mechanic. It likely triggers or checks script conditions using `ScriptInfo` structures.

### Calls Out
*   **None**: `ScriptInfo` is a data-only structure. It does not call any other units.

## Data Model

`ScriptInfo` itself does not interact with the database directly. However, its fields correspond directly to columns in the various `*_scripts` tables in the `wowvmangos` database (e.g., `gameobject_scripts`, `creature_scripts`, `scripted_areatrigger`).

While no specific table schema was provided in the input, the structure implies the following common columns found in these tables:
*   `id`: Unique identifier for the script step.
*   `delay`: Time in milliseconds before execution.
*   `command`: The integer ID corresponding to `eScriptCommand`.
*   `datalong` through `datalong4`: Mapped to the union members (e.g., `chatType`, `spellId`, `goGuid`).
*   `dataint` through `dataint4`: Mapped to secondary union members (e.g., `textId`, `flags`).
*   `target_type`: Corresponds to `ScriptTarget` enum.
*   `target_param1`, `target_param2`: Additional targeting parameters.
*   `x`, `y`, `z`, `o`: Spatial coordinates.

## Notable Implementation Details

1.  **Union Memory Layout**: The `union` inside `ScriptInfo` is carefully structured to align with the database columns. Each struct within the union corresponds to a specific `eScriptCommand`. For example, `SCRIPT_COMMAND_TALK` uses the `talk` struct, which maps `datalong` to `chatType` and `dataint`–`dataint4` to `textId`. This design allows the loader to simply cast the raw database integers into the appropriate struct fields based on the `command` ID.
2.  **Raw Data Access**: The union includes a `raw` struct containing `uint32 data[9]`. This allows low-level access to the parameter block if needed, or serves as a fallback for commands that don't fit neatly into the predefined structs. The constructor zeroes this out to ensure safety.
3.  **Targeting Flexibility**: The `ScriptTarget` enum defines a rich set of targeting modes, ranging from simple ("Provided Target") to complex ("Nearest Friendly Missing Buff"). The `target_param1` and `target_param2` fields allow these modes to accept dynamic arguments like creature entries, spell IDs, or search radii.
4.  **Validation Helper**: The `GetGOGuid()` method highlights a common validation pattern in the codebase: ensuring that commands manipulating GameObjects have a valid GUID. This prevents runtime errors where a script tries to open a door but no door GUID was specified.
5.  **No Dynamic Allocation**: `ScriptInfo` is designed to be stack-allocated or embedded within larger containers (like `std::vector<ScriptInfo>` in `ScriptMgr`). It has no pointers or virtual functions, making it cheap to copy and move.

## Member Reference

**`ScriptInfo`**
Constructor that initializes all members to zero, including the union's raw data buffer via `memset`. Ensures a clean state for newly created script steps.

**`GetGOGuid`**
Method that returns the `db_guid` of the GameObject associated with the current command, if applicable. Checks the `command` field and returns the GUID from the relevant union member (`respawnGo`, `despawnGo`, `loadGo`, `openDoor`, or `closeDoor`). Returns `0` for all other commands. Used by `ScriptMgr` for validation.

---

<!-- machine-true, projected from graph.json -->

## Map — ScriptInfo

*Source:* ScriptCommands.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ScriptInfo | ctor | — | eastern_plaguelands/EffectDummyGameObj_go_mark_of_detonation, ScriptMgr/LoadScripts, Spell.Effects/EffectDummy, ThreatListCopier.battleground_alterac/UpdateEscortAI#4, Unit.SpellAuras/HandleAuraDummy | — |
| GetGOGuid | method | — | ScriptMgr/LoadScripts | — |
