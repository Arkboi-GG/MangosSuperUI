<!-- provenance: verbose -->
# loch_modan

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# loch_modan

**Purpose & Responsibilities**
The `loch_modan` translation unit implements scripted quest events for the zone "Loch Modan", specifically handling two quests involving the NPC Miran:
1.  **Protecting the Shipment (Quest ID 309):** An escort quest where Miran travels along a waypoint path. The script manages Miran's AI (`npc_miranAI`) during this escort, triggering an ambush at waypoint 19, spawning enemies, and handling dialogue and quest completion at waypoint 23.
2.  **Resupplying the Excavation (Quest ID 273):** An area-triggered event. When a player triggers the associated area trigger, the script checks conditions and initiates an ambush scenario involving NPCs Miran, Huldar, and Saean, spawning Dark Iron Ambushers to attack them.

## Member-by-Member Behavior

### Escort Quest Logic (Protecting the Shipment)

*   **`npc_miranAI`**: The AI class for Miran during the escort quest, inheriting from `npc_escortAI`.
    *   **Constructor (`ctor`)**: Initializes the AI and calls `Reset()` to ensure initial state variables are correct.
    *   **`Reset`**: Resets the internal counter `m_uiDwarves` to 0 if the escort is not currently active (`!HasEscortState(STATE_ESCORT_ESCORTING)`). This counter tracks alive summoned Dark Iron Raiders.
    *   **`WaypointReached`**: Triggered when Miran reaches a specific waypoint.
        *   **Waypoint 19**: Miran speaks (`SAY_MIRAN_1`) and summons two Dark Iron Raiders (`NPC_DARK_IRON_RAIDER`) at predefined positions. Summons are temporary (`TEMPSUMMON_TIMED_OR_DEAD_DESPAWN`, 25s).
        *   **Waypoint 23**: Miran speaks (`SAY_MIRAN_3`). If a player is being escorted, `GroupEventHappens` is called to signal quest progress/completion for `QUEST_PROTECTING_THE_SHIPMENT`.
    *   **`SummonedCreatureJustDied`**: Called when a summoned creature dies. If it is a Dark Iron Raider, it decrements `m_uiDwarves`. If the counter reaches zero, Miran speaks (`SAY_MIRAN_2`).
    *   **`JustSummoned`**: Called after a creature is summoned. If it is a Dark Iron Raider:
        *   If `m_uiDwarves` was 0, the raider speaks (`SAY_DARK_IRON_DWARF`).
        *   Increments `m_uiDwarves`.
        *   Commands the raider to attack Miran (`AttackStart`).

*   **`QuestAccept_npc_miran`**: Global function hooked to the quest accept event for Miran.
    *   Checks if the accepted quest is `QUEST_PROTECTING_THE_SHIPMENT`.
    *   If so, retrieves the `npc_miranAI` instance and starts the escort sequence using `Start()`, passing the player's GUID and the quest object.
    *   Always returns `true`, allowing the quest to be accepted regardless of the escort start outcome.

*   **`GetAI_npc_miran`**: Factory function that creates and returns a new instance of `npc_miranAI` for a given creature.

### Area Trigger Event (Resupplying the Excavation)

*   **`AreaTrigger_at_huldar_miran`**: Global function handling the area trigger event for quest 273.
    *   **Initial Checks**: Returns `false` if the player is dead, is a Game Master, has already completed the quest, or doesn't have the quest.
    *   **Premature Completion**: Immediately marks the quest as complete for the player and sends the completion event. This safeguards against NPCs being unavailable or dead, preventing the quest from being stuck incomplete.
    *   **NPC Availability Checks**:
        *   Finds the closest Huldar (`NPC_HULDAR`) within 60 yards. Returns `false` if not found or dead.
        *   Finds the closest Miran (`NPC_MIRAN`) within 60 yards. Returns `false` if not found or dead.
        *   **Collision Check**: If Miran is found, checks if Miran's AI is `npc_miranAI` and if that AI is currently escorting someone (`HasEscortState(STATE_ESCORT_ESCORTING)`). If Miran is busy with the escort quest, the ambush event is aborted (`return false`) to prevent conflicts.
    *   **Combat Check**: If either Miran or Huldar is already in combat, the function returns `true` (event considered handled/skipped further setup).
    *   **Saean Setup & Ambush**:
        *   Finds the closest Saean (`NPC_SAEAN`) within 60 yards.
        *   If Saean is alive:
            *   Sets Saean's faction temporarily to hostile (`FACTION_HOSTILE`).
            *   Checks if Dark Iron Ambushers (`NPC_DARK_IRON_AMBUSHER`) are already nearby (within 100 yards) to prevent duplicate spawns.
            *   If no ambushers are nearby, summons two Dark Iron Ambushers at predefined positions near Saean. These are temporary summons.
            *   Commands Saean to attack Miran, initiating the conflict.
            *   If ambushers *are* already nearby, it returns `false`, assuming the event is already underway.
    *   Returns `true` if the event was successfully initiated or skipped due to prior conditions.

### Script Registration

*   **`AddSC_loch_modan`**: Registers the scripts defined in this unit with the server's script manager.
    *   Creates a script for "npc_miran", assigning `GetAI_npc_miran` as the AI provider and `QuestAccept_npc_miran` as the quest accept handler.
    *   Creates a script for "at_huldar_miran", assigning `AreaTrigger_at_huldar_miran` as the area trigger handler.
    *   Calls `RegisterSelf()` for both scripts to activate them.

## Cross-Unit Boundaries

*   **`npc_miranAI`**:
    *   Inherits from `npc_escortAI` (in `ScriptedEscortAI`), utilizing its framework for waypoint movement and escort state management.
    *   Calls `ScriptedEscortAI::HasEscortState` in `Reset` to check escort status.
    *   Calls `ScriptedEscortAI::GetPlayerForEscort` in `WaypointReached` to identify the escorted player.
    *   Calls `ScriptedEscortAI::Start` indirectly via `QuestAccept_npc_miran` to begin the escort.
*   **`WaypointReached`**:
    *   Calls `Player::GroupEventHappens` to signal quest progress/completion to the player.
    *   Calls `ScriptMgr::DoScriptText` to play dialogue lines.
    *   Calls `WorldObject::SummonCreature` (via `m_creature->SummonCreature`) to spawn enemies.
*   **`SummonedCreatureJustDied` / `JustSummoned`**:
    *   Calls `Object::GetEntry` to identify the type of summoned creature.
    *   Calls `ScriptMgr::DoScriptText` for dialogue.
    *   Calls `Creature::AI` and `CreatureAI::AttackStart` to control summoned creatures' behavior.
*   **`QuestAccept_npc_miran`**:
    *   Calls `Creature::AI` to get the AI instance.
    *   Calls `Object::GetGUID` to get the player's GUID.
    *   Calls `QuestDef::GetQuestId` to check the quest ID.
    *   Calls `ScriptedEscortAI::Start` to initiate the escort.
*   **`AreaTrigger_at_huldar_miran`**:
    *   Calls various `Player` methods (`IsAlive`, `IsGameMaster`, `GetQuestStatus`, `CompleteQuest`, `SendQuestCompleteEvent`) to manage player state and quest progression.
    *   Calls `GridSearchers::GetClosestCreatureWithEntry` and `WorldObject::FindNearestCreature` to locate NPCs.
    *   Calls `Creature` methods (`AI`, `SetFactionTemporary`, `SummonCreature`) and `CreatureAI::AttackStart` to manipulate NPCs and initiate combat.
    *   Calls `Unit::IsAlive` and `Unit::IsInCombat` to check NPC states.
    *   Calls `ScriptedEscortAI::HasEscortState` to check for conflicts with the escort quest.
*   **`AddSC_loch_modan`**:
    *   Calls `Script::Script` constructor and `ScriptMgr::RegisterSelf` to register the scripts.
    *   Is called by `ScriptLoader::AddScripts` (standard entry point).

## Data Model

This unit does not directly interact with any database tables. All data (quest IDs, NPC entries, dialogue text IDs, spawn coordinates) is hardcoded within the C++ source file.

## Notable Implementation Details

*   **Hardcoded Data:** Quest IDs, NPC entries, dialogue text IDs, and spawn coordinates are all defined as static constants or enums within the file. Changes to these require recompilation.
*   **Escort Counter (`m_uiDwarves`):** The `npc_miranAI` uses a simple integer counter to track alive summoned raiders. This counter is incremented in `JustSummoned` and decremented in `SummonedCreatureJustDied`. Dialogue is triggered based on this counter reaching zero or starting from zero. This assumes summons are only created and destroyed through these specific AI hooks.
*   **Area Trigger Safeguards:** The `AreaTrigger_at_huldar_miran` function includes several safeguards:
    *   Immediate quest completion if NPCs are unavailable, preventing soft-locks.
    *   Checking for existing combat to avoid redundant actions.
    *   Checking for nearby ambushers to prevent duplicate spawns.
    *   Checking if Miran is already escorting to avoid conflicting events.
*   **Temporary Summons:** Both the escort ambush and the area trigger ambush use `TEMPSUMMON_TIMED_OR_DEAD_DESPAWN` with a 25-second duration. This ensures spawned enemies don't persist indefinitely if the event fails or players leave.
*   **Faction Manipulation:** In the area trigger event, Saean's faction is temporarily changed to hostile to allow him to attack Miran. This change is set to restore upon respawn (`TEMPFACTION_RESTORE_RESPAWN`).
*   **Dynamic Casts:** The code uses `dynamic_cast` to safely check if a creature's AI is of the expected type (`npc_miranAI`) before accessing specific methods or state. This is crucial for the collision check between the two quests.
*   **Return Values:** `QuestAccept_npc_miran` always returns `true`, meaning the quest can be accepted even if the escort start fails (e.g., if the AI cast fails). `AreaTrigger_at_huldar_miran` returns `true` if the event was processed or skipped due to valid reasons, and `false` if it failed or was aborted due to conflicts/unavailability. The interpretation of these return values depends on the calling script system.

## Member Reference

*   **`npc_miranAI`**: Constructor for the escort AI, initializes state by calling `Reset()`. Inherits from `npc_escortAI`.
*   **`Reset`**: Resets the `m_uiDwarves` counter if the escort is not active. Calls `ScriptedEscortAI::HasEscortState`.
*   **`WaypointReached`**: Handles waypoint events for the escort. Spawns enemies and plays dialogue at waypoint 19. Signals quest progress at waypoint 23. Calls `Player::GroupEventHappens`, `ScriptedEscortAI::GetPlayerForEscort`, `ScriptMgr::DoScriptText`, `WorldObject::SummonCreature`.
*   **`SummonedCreatureJustDied`**: Decrements the raider counter when a summoned raider dies. Plays dialogue if all raiders are dead. Calls `Object::GetEntry`, `ScriptMgr::DoScriptText`.
*   **`JustSummoned`**: Increments the raider counter when a raider is summoned. Plays initial dialogue and commands attack. Calls `Creature::AI`, `CreatureAI::AttackStart`, `Object::GetEntry`, `ScriptMgr::DoScriptText`.
*   **`QuestAccept_npc_miran`**: Starts the escort quest when the specific quest is accepted. Calls `Creature::AI`, `Object::GetGUID`, `QuestDef::GetQuestId`, `ScriptedEscortAI::Start`.
*   **`GetAI_npc_miran`**: Factory function to create `npc_miranAI` instances.
*   **`AreaTrigger_at_huldar_miran`**: Handles the area trigger for the ambush event. Checks player/NPC states, prevents conflicts, spawns enemies, and initiates combat. Calls numerous `Player`, `Creature`, `Unit`, `WorldObject`, `GridSearchers`, and `ScriptedEscortAI` methods.
*   **`AddSC_loch_modan`**: Registers the NPC and area trigger scripts with the script manager. Calls `Script::Script`, `ScriptMgr::RegisterSelf`. Called by `ScriptLoader::AddScripts`.

---

<!-- machine-true, projected from graph.json -->

## Map — loch_modan

*Source:* loch_modan.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| npc_miranAI | ctor | ScriptedEscortAI/npc_escortAI | — | — |
| Reset | method | ScriptedEscortAI/HasEscortState | — | — |
| WaypointReached | method | Player.Main/GroupEventHappens, ScriptedEscortAI/GetPlayerForEscort, ScriptMgr/DoScriptText, WorldObject.Object/SummonCreature#2 | — | — |
| SummonedCreatureJustDied | method | Object/GetEntry, ScriptMgr/DoScriptText | — | — |
| JustSummoned | method | Creature.Main/AI, CreatureAI/AttackStart, Object/GetEntry, ScriptMgr/DoScriptText | — | — |
| QuestAccept_npc_miran | function | Creature.Main/AI, Object/GetGUID, QuestDef/GetQuestId, ScriptedEscortAI/Start | — | — |
| GetAI_npc_miran | function | — | — | — |
| AreaTrigger_at_huldar_miran | function | Creature.Main/AI, Creature.Main/SetFactionTemporary, CreatureAI/AttackStart, GridSearchers/GetClosestCreatureWithEntry, Player.Main/CompleteQuest, Player.Main/GetQuestStatus, Player.Main/IsGameMaster, Player.Main/SendQuestCompleteEvent, ScriptedEscortAI/HasEscortState, Unit.Main/IsAlive, Unit.Main/IsInCombat, WorldObject.Object/FindNearestCreature, WorldObject.Object/SummonCreature#2 | — | — |
| AddSC_loch_modan | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
