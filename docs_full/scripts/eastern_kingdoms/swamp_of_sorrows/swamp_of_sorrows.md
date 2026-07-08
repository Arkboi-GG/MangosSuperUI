<!-- provenance: verbose -->
# swamp_of_sorrows

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Unit Documentation: `swamp_of_sorrows`

## Purpose & Responsibilities

This unit implements the scripted AI for **Galen Goodward** (`npc_galen_goodward`) in the Swamp of Sorrows, specifically supporting **Quest 1393 ("Galen's Escape")**. It handles the escort sequence initiated by quest acceptance, including opening/closing Galen’s cage, managing dialogue states, and triggering quest completion upon reaching the final waypoint.

## Member-by-Member Behavior

### `npc_galen_goodwardAI` Class

Inherits from `npc_escortAI` to manage the escort path.

*   **`npc_galen_goodwardAI` (ctor)**: Initializes `m_uiGalensCageGUID` to 0 and calls `Reset()`.
*   **`Reset`**: Sets the periodic speech timer `m_uiPeriodicSay` to 6000 ms.
*   **`Aggro`**: If the escort is active (`STATE_ESCORT_ESCORTING`), plays a random attack line (`SAY_ATTACKED_1` or `SAY_ATTACKED_2`) via `ScriptMgr::DoScriptText`.
*   **`WaypointStart`**:
    *   **WP 0**: Locates Galen’s cage (`GO_GALENS_CAGE`). If `m_uiGalensCageGUID` is set, retrieves it via `Map::GetGameObject`; otherwise, searches nearby via `GridSearchers::GetClosestGameObjectWithEntry`. Opens the cage via `GameObject::UseDoorOrButton` and caches the GUID.
    *   **WP 21**: Plays `EMOTE_DISAPPEAR` via `ScriptMgr::DoScriptText`.
*   **`WaypointReached`**:
    *   **WP 0**: Closes the cage via `GameObject::ResetDoorOrButton` using the cached GUID.
    *   **WP 20**: Faces the escorted player (`Unit::SetFacingToObject`), plays completion dialogue (`SAY_QUEST_COMPLETE`) and whisper emote (`EMOTE_WHISPER`) via `ScriptMgr::DoScriptText`, triggers quest completion via `Player::GroupEventHappens`, and sets run mode via `ScriptedEscortAI::SetRun`.
*   **`UpdateEscortAI`**:
    *   Decrements `m_uiPeriodicSay`. If expired and escort state is `STATE_ESCORT_NONE`, plays `SAY_PERIODIC` via `ScriptMgr::DoScriptText` and resets the timer.
    *   Checks for a hostile target (`Unit::SelectHostileTarget`) and victim (`Unit::GetVictim`). If both exist, initiates melee attacks via `CreatureAI::DoMeleeAttackIfReady`.

### Script Hooks

*   **`QuestAccept_npc_galen_goodward`**: Triggered on quest acceptance. If the quest is `QUEST_GALENS_ESCAPE` (1393), it casts the creature’s AI to `npc_galen_goodwardAI`, starts the escort via `ScriptedEscortAI::Start`, sets a temporary friendly faction (495) via `Creature::SetFactionTemporary`, and plays `SAY_QUEST_ACCEPTED` via `ScriptMgr::DoScriptText`.
*   **`GetAI_npc_galen_goodward`**: Factory function returning a new `npc_galen_goodwardAI` instance.
*   **`AddSC_swamp_of_sorrows`**: Registers the script with the server. Creates a `Script` object named `"npc_galen_goodward"`, assigns the AI and quest accept handlers, and calls `Script::RegisterSelf`. Called by `ScriptLoader::AddScripts`.

## Cross-Unit Boundaries

*   **`ScriptedEscortAI` / `npc_escortAI`**: Base class providing `HasEscortState`, `GetPlayerForEscort`, `SetRun`, and `Start`.
*   **`ScriptMgr`**: `DoScriptText` triggers all dialogue and emotes.
*   **`shared_Util`**: `urand` randomizes attack lines in `Aggro`.
*   **`GameObject`**: `UseDoorOrButton` and `ResetDoorOrButton` control the cage; `GetGUID` retrieves the identifier.
*   **`Map.Main`**: `GetMap` and `GetGameObject` retrieve the cage object by GUID.
*   **`GridSearchers`**: `GetClosestGameObjectWithEntry` locates the cage if the GUID is unknown.
*   **`Player.Main`**: `GroupEventHappens` signals quest completion.
*   **`Unit.Main`**: `SelectHostileTarget`, `GetVictim`, and `SetFacingToObject` manage combat and positioning.
*   **`Creature.Main`**: `AI` retrieves the AI instance; `SetFactionTemporary` adjusts faction during the quest.

## Data Model

This unit does not directly query or modify any database tables. Configuration is hardcoded in the `GalenGoodwardData` enum.

## Notable Implementation Details

1.  **Cage GUID Caching**: The AI caches the cage’s GUID (`m_uiGalensCageGUID`) after finding it at Waypoint 0 start. This avoids re-searching the grid when closing the cage at Waypoint 0 arrival.
2.  **Temporary Faction**: Galen’s faction is set to 495 temporarily upon quest acceptance to ensure friendliness during the escort, reverting on respawn.
3.  **Waypoint Coupling**: Logic depends on specific waypoint IDs (0, 20, 21). Changes to the escort path in the database must preserve these IDs.

## Member Reference

**npc_galen_goodwardAI** (ctor): Initializes `m_uiGalensCageGUID` to 0 and calls `Reset()`. Inherits from `npc_escortAI`.

**Reset**: Sets `m_uiPeriodicSay` to 6000 ms.

**Aggro**: Plays a random attack line if escorting, using `urand` and `ScriptMgr::DoScriptText`.

**WaypointStart**: At WP 0, finds and opens Galen’s cage, caching its GUID. At WP 21, plays `EMOTE_DISAPPEAR`.

**WaypointReached**: At WP 0, closes the cage. At WP 20, faces the player, plays completion dialogue, triggers `GroupEventHappens`, and sets run mode.

**UpdateEscortAI**: Manages periodic speech timer and initiates melee attacks if a hostile target exists.

**QuestAccept_npc_galen_goodward**: Validates quest ID, starts escort, sets temporary faction, and plays acceptance dialogue.

**GetAI_npc_galen_goodward**: Factory function creating a new `npc_galen_goodwardAI` instance.

**AddSC_swamp_of_sorrows**: Registers the script with the server, linking AI and quest accept handlers.

---

<!-- machine-true, projected from graph.json -->

## Map — swamp_of_sorrows

*Source:* swamp_of_sorrows.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| npc_galen_goodwardAI | ctor | ScriptedEscortAI/npc_escortAI | — | — |
| Reset | method | — | — | — |
| Aggro | method | ScriptedEscortAI/HasEscortState, ScriptMgr/DoScriptText, shared_Util/urand | — | — |
| WaypointStart | method | GameObject/UseDoorOrButton, GridSearchers/GetClosestGameObjectWithEntry, Map.Main/GetGameObject, Object/GetGUID, ObjectGuid/ObjectGuid#5, ScriptMgr/DoScriptText, WorldObject.Object/GetMap | — | — |
| WaypointReached | method | GameObject/ResetDoorOrButton, Map.Main/GetGameObject, ObjectGuid/ObjectGuid#5, Player.Main/GroupEventHappens, ScriptedEscortAI/GetPlayerForEscort, ScriptedEscortAI/SetRun, ScriptMgr/DoScriptText, Unit.Main/SetFacingToObject, WorldObject.Object/GetMap | — | — |
| UpdateEscortAI | method | CreatureAI/DoMeleeAttackIfReady, ScriptedEscortAI/HasEscortState, ScriptMgr/DoScriptText, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| QuestAccept_npc_galen_goodward | function | Creature.Main/AI, Creature.Main/SetFactionTemporary, Object/GetGUID, QuestDef/GetQuestId, ScriptedEscortAI/Start, ScriptMgr/DoScriptText | — | — |
| GetAI_npc_galen_goodward | function | — | — | — |
| AddSC_swamp_of_sorrows | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
