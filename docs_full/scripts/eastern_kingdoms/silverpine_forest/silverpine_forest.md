# silverpine_forest

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# silverpine_forest

**Purpose & Responsibilities**  
`silverpine_forest.cpp` implements the scripted behavior for **Deathstalker Erland** (creature entry associated with `npc_deathstalker_erland`), handling Quest 435 ("The Deathstalkers"). It manages a waypoint-based escort route, dialogue triggers, and interactions with allied NPCs Rane (entry 1950) and Quinn (entry 1951). The unit also defines an unused utility `IsWorgenTime` and registers the script with the core.

## Member-by-Member Behavior

### Utility
- **`IsWorgenTime`** — Returns `true` if the server’s local hour is ≥ 21 or < 9. Defined in `SilverpineUtils` but never called within this unit.

### AI Class (`npc_deathstalker_erlandAI`)
- **`npc_deathstalker_erlandAI` (ctor)** — Initializes cached GUIDs (`uiRaneGUID`, `uiQuinnGUID`) to 0 and calls `Reset()`. Inherits from `npc_escortAI`.
- **`MoveInLineOfSight`** — If escorting, checks if Rane or Quinn are within 30 yards. If found and not yet cached, stores their GUIDs. Delegates to `npc_escortAI::MoveInLineOfSight`.
- **`WaypointReached`** — Triggers dialogue at specific waypoints. At waypoint 13, completes the quest for the player’s group. At waypoints 14 and 25, retrieves Rane and Quinn by cached GUID from the map to play their reply lines.
- **`Reset`** — Clears cached GUIDs only if the escort is not active (`STATE_ESCORT_ESCORTING`), preserving context during temporary interruptions.
- **`Aggro`** — Plays a random aggro line (from 3 options) toward the attacker.

### Quest & Registration
- **`QuestAccept_npc_deathstalker_erland`** — On accepting Quest 435, plays intro dialogue, sets the creature’s faction to `FACTION_ESCORTEE` (232) until respawn, and starts the escort AI.
- **`GetAI_npc_deathstalker_erland`** — Factory function returning a new `npc_deathstalker_erlandAI` instance.
- **`AddSC_silverpine_forest`** — Registers the script with `ScriptMgr`, binding the AI factory and quest accept handler. Called by `ScriptLoader/AddScripts`.

## Cross-Unit Boundaries

| Member | Direction | Collaborating Unit | Purpose |
|---|---|---|---|
| `npc_deathstalker_erlandAI` (ctor) | Calls | `ScriptedEscortAI/npc_escortAI` | Inherits escort logic. |
| `MoveInLineOfSight` | Calls | `Object/GetEntry`, `Object/GetGUID`, `ScriptedEscortAI/HasEscortState`, `ScriptedEscortAI/MoveInLineOfSight`, `WorldObject.Object/IsWithinDistInMap` | Identifies Rane/Quinn, checks distance, delegates to base. |
| `WaypointReached` | Calls | `Map.Main/GetUnit`, `ObjectGuid/ObjectGuid#5`, `Player.Main/GroupEventHappens`, `ScriptedEscortAI/GetPlayerForEscort`, `ScriptMgr/DoScriptText`, `WorldObject.Object/GetMap` | Retrieves player, plays dialogue, completes quest, fetches allied NPCs. |
| `Reset` | Calls | `ScriptedEscortAI/HasEscortState` | Guards GUID clearing. |
| `Aggro` | Calls | `ScriptMgr/DoScriptText`, `shared_Util/urand` | Randomizes and plays aggro text. |
| `QuestAccept_npc_deathstalker_erland` | Calls | `Creature.Main/AI`, `Creature.Main/SetFactionTemporary`, `Object/GetGUID`, `QuestDef/GetQuestId`, `ScriptedEscortAI/Start`, `ScriptMgr/DoScriptText` | Validates quest, changes faction, starts escort. |
| `AddSC_silverpine_forest` | Called by | `ScriptLoader/AddScripts` | Registers the script at load time. |
| `AddSC_silverpine_forest` | Calls | `Script/Script`, `ScriptMgr/RegisterSelf` | Creates and registers the script descriptor. |

## Data Model

This unit does **not** query or modify any database tables directly. All data (quest IDs, NPC entries, faction IDs, dialogue text IDs) are hardcoded constants or resolved at runtime via the core’s object management systems.

## Notable Implementation Details

- **GUID Caching Heuristic**: `MoveInLineOfSight` caches Rane and Quinn’s GUIDs only once. If these NPCs despawn and respawn with new GUIDs during the escort, `WaypointReached` will fail to locate them at waypoints 14 and 25.
- **Escort State Guard**: `Reset` preserves cached GUIDs while the escort is active, allowing recovery from temporary resets. However, this risks stale GUIDs if the escort restarts after a long downtime where NPCs may have respawned.
- **Unreferenced Utility**: `IsWorgenTime` is defined but unused.
- **Faction Change**: `SetFactionTemporary` with `TEMPFACTION_RESTORE_RESPAWN` ensures the creature reverts to its default faction upon respawn.

## Member Reference

**IsWorgenTime** — Returns `true` if the current local hour is between 21:00 and 09:00. Uses `localtime`. Not called by any other member in this unit.

**npc_deathstalker_erlandAI** (ctor) — Initializes `uiRaneGUID` and `uiQuinnGUID` to 0, calls `Reset()`. Inherits from `npc_escortAI`.

**MoveInLineOfSight** — During an active escort, scans for NPCs with entries 1950 (Rane) and 1951 (Quinn). If within 30 yards, caches their GUIDs. Delegates to `npc_escortAI::MoveInLineOfSight`.

**WaypointReached** — At waypoint 0, plays start dialogue. At 13, plays end dialogue and triggers quest completion for the player’s group. At 14 and 25, retrieves Rane and Quinn by cached GUID and plays their reply lines. At 15, 16, 24, and 26, plays additional dialogue.

**Reset** — Clears `uiRaneGUID` and `uiQuinnGUID` only if the escort is not in the `STATE_ESCORT_ESCORTING` state.

**Aggro** — Selects a random aggro line (0–2) using `urand` and plays it toward the attacker via `DoScriptText`.

**QuestAccept_npc_deathstalker_erland** — On acceptance of quest 435, plays introductory dialogue, sets the creature’s faction to 232 temporarily, and starts the escort AI with the player as the escortee.

**GetAI_npc_deathstalker_erland** — Factory function that returns a new `npc_deathstalker_erlandAI` instance for the given creature.

**AddSC_silverpine_forest** — Creates a `Script` object, assigns the AI factory (`GetAI_npc_deathstalker_erland`) and quest accept handler (`QuestAccept_npc_deathstalker_erland`), and registers it with the `ScriptMgr`. Called by `ScriptLoader/AddScripts` at server startup.

---

<!-- machine-true, projected from graph.json -->

## Map — silverpine_forest

*Source:* silverpine_forest.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| IsWorgenTime | function | — | — | — |
| npc_deathstalker_erlandAI | ctor | ScriptedEscortAI/npc_escortAI | — | — |
| MoveInLineOfSight | method | Object/GetEntry, Object/GetGUID, ScriptedEscortAI/HasEscortState, ScriptedEscortAI/MoveInLineOfSight, WorldObject.Object/IsWithinDistInMap | — | — |
| WaypointReached | method | Map.Main/GetUnit, ObjectGuid/ObjectGuid#5, Player.Main/GroupEventHappens, ScriptedEscortAI/GetPlayerForEscort, ScriptMgr/DoScriptText, WorldObject.Object/GetMap | — | — |
| Reset | method | ScriptedEscortAI/HasEscortState | — | — |
| Aggro | method | ScriptMgr/DoScriptText, shared_Util/urand | — | — |
| QuestAccept_npc_deathstalker_erland | function | Creature.Main/AI, Creature.Main/SetFactionTemporary, Object/GetGUID, QuestDef/GetQuestId, ScriptedEscortAI/Start, ScriptMgr/DoScriptText | — | — |
| GetAI_npc_deathstalker_erland | function | — | — | — |
| AddSC_silverpine_forest | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
