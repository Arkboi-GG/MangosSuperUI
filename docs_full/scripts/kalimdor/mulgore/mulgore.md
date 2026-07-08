# mulgore

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# mulgore.cpp

## Purpose & Responsibilities

`mulgore.cpp` implements the artificial intelligence for a specific creature, **Plain Vision** (`npc_plains_vision`), located in the Mulgore zone. The unit defines a custom AI class, `plainVisionAI`, which inherits from `npc_escortAI`. Despite inheriting from an escort base class, this specific implementation does not perform complex escort logic; instead, it functions as a combat-capable entity that immediately initiates its escort path upon engagement and then proceeds to attack hostile targets using melee attacks.

The unit also provides the necessary registration hooks (`AddSC_mulgore`) to integrate this script into the server's script manager, ensuring the AI is loaded and associated with the correct creature entry when the server starts.

## Member-by-Member Behavior

### AI Logic (`plainVisionAI`)

The core of this unit is the `plainVisionAI` struct, which manages the behavior of the Plain Vision creature.

*   **Construction (`plainVisionAI`)**: The constructor initializes the base `npc_escortAI` class with the creature pointer. It explicitly calls `Reset()` to initialize internal states and sets a local boolean flag `isEngaged` to `false`. This flag is critical for controlling the initialization of the escort path.
*   **State Management (`Reset`)**: The `Reset` method is overridden but contains an empty body. This implies that resetting the creature (e.g., after death or despawn) does not require clearing any custom state variables beyond what the base class handles, as the `isEngaged` flag is effectively reset by the object's lifecycle or re-initialization.
*   **Waypoint Handling (`WaypointReached`)**: The `WaypointReached` method is overridden but is also empty. This indicates that while the creature follows a path (triggered by `Start`), no specific actions (such as casting spells, playing sounds, or changing behavior) are required when it reaches individual waypoints along that path.
*   **Update Loop (`UpdateEscortAI`)**: This is the primary logic driver, executed periodically.
    1.  **Path Initialization**: It checks the `isEngaged` flag. If `false`, it sets the flag to `true` and calls `Start(false, 0, nullptr, false)` on the base `npc_escortAI`. This triggers the creature to begin moving along its predefined escort path. The parameters suggest it starts immediately (`0` delay), without a specific target for the escort start, and likely without requiring a player to be present (`nullptr`).
    2.  **Combat Check**: It attempts to select a hostile target using `SelectHostileTarget()` and verifies if a victim exists via `GetVictim()`. If no valid target is found, the update returns early, preventing unnecessary processing.
    3.  **Melee Attack**: If a target is present, it calls `DoMeleeAttackIfReady()`, allowing the creature to perform melee attacks according to its attack timer.

### Registration (`AddSC_mulgore` & `GetAI_plainVision`)

*   **`GetAI_plainVision`**: A factory function that instantiates and returns a new `plainVisionAI` object for a given `Creature`. This decouples the AI creation from the registration process.
*   **`AddSC_mulgore`**: This function registers the script with the server. It creates a `Script` object, assigns the name `"npc_plains_vision"` (which links the script to the creature entry in the database), sets the `GetAI` pointer to `GetAI_plainVision`, and registers itself with the `ScriptMgr`. This ensures that when the server encounters a creature with the entry ID corresponding to `npc_plains_vision`, it loads this specific AI.

## Cross-Unit Boundaries

*   **`npc_escortAI` / `ScriptedEscortAI`**: `plainVisionAI` inherits from `npc_escortAI`. It relies on the base class for pathfinding, waypoint management, and the `Start()` method to initiate movement. The empty overrides for `Reset` and `WaypointReached` indicate minimal customization of the base escort behavior.
*   **`CreatureAI`**: The `UpdateEscortAI` method calls `DoMeleeAttackIfReady()`, which is a member of the `CreatureAI` base class (or a mixin). This integrates standard combat mechanics into the escort AI.
*   **`Unit.Main`**: The AI uses `SelectHostileTarget()` and `GetVictim()` from the `Unit` class to manage threat and target selection. These calls determine whether the creature should engage in combat during its patrol.
*   **`Script` / `ScriptMgr`**: `AddSC_mulgore` interacts with the `Script` structure and `ScriptMgr` to register the AI. `ScriptLoader::AddScripts` (called by the server startup sequence) invokes `AddSC_mulgore`, integrating this unit into the global script system.

## Data Model

This unit does not directly query or modify any database tables. It relies on the creature's entry ID (linked via the script name `"npc_plains_vision"`) to associate with the correct `creature_template` and potentially `creature_addon` or `waypoints` data stored in the database, but these interactions are handled by the core engine and base classes, not by explicit SQL queries in this file.

## Notable Implementation Details

*   **Immediate Path Start**: The `isEngaged` flag ensures that `Start()` is called exactly once when the AI becomes active. This means the creature begins its patrol path immediately upon spawning or resetting, regardless of whether a player is nearby. This differs from some escort NPCs that wait for a player to interact before starting.
*   **Empty Overrides**: The empty `Reset` and `WaypointReached` methods are significant. They indicate that the creature's behavior is purely "move along path and fight if threatened." There are no special events tied to waypoints or resets.
*   **Combat Priority**: The `UpdateEscortAI` loop prioritizes combat checks after initiating the path. If a target is selected, it will attack. The interaction between pathing and combat is managed by the base `npc_escortAI` class, which typically pauses pathing during combat and resumes afterward. This unit does not customize that behavior.
*   **Script Name Mapping**: The script name `"npc_plains_vision"` must match the `ScriptName` field in the `creature_template` table for the creature entry intended to use this AI. If this name is incorrect or missing in the database, the AI will not load.

## Member Reference

**plainVisionAI** (ctor): Constructs the AI, initializing the base `npc_escortAI` and setting `isEngaged` to `false`. Calls `Reset()`.

**Reset**: Overrides the base reset method with an empty body, indicating no custom state cleanup is needed.

**WaypointReached**: Overrides the base waypoint handler with an empty body, indicating no actions are taken when waypoints are reached.

**UpdateEscortAI**: The main update loop. Initializes the escort path via `Start()` if not already engaged. Checks for hostile targets using `SelectHostileTarget()` and `GetVictim()`. Performs melee attacks via `DoMeleeAttackIfReady()` if a target is present.

**GetAI_plainVision**: Factory function that creates and returns a new `plainVisionAI` instance for a given `Creature`.

**AddSC_mulgore**: Registers the script with the `ScriptMgr`. Creates a `Script` object named `"npc_plains_vision"`, links it to `GetAI_plainVision`, and registers it. Called by `ScriptLoader::AddScripts`.

---

<!-- machine-true, projected from graph.json -->

## Map — mulgore

*Source:* mulgore.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| plainVisionAI | ctor | ScriptedEscortAI/npc_escortAI | — | — |
| Reset | method | — | — | — |
| WaypointReached | method | — | — | — |
| UpdateEscortAI | method | CreatureAI/DoMeleeAttackIfReady, ScriptedEscortAI/Start, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_plainVision | function | — | — | — |
| AddSC_mulgore | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
