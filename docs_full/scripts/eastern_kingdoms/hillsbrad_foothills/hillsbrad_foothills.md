<!-- provenance: verbose -->
# hillsbrad_foothills

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# hillsbrad_foothills

## Purpose & Responsibilities

`hillsbrad_foothills.cpp` provides scripted behaviors for two `GameObject`s in the Hillsbrad Foothills zone:

1.  **Helcular's Grave (`go_helcular_s_grave`)**: Spawns the creature Helcular (entry 2433) once when a related quest is rewarded, tracking the spawned creature's GUID to prevent duplicates.
2.  **The Dusty Rug (`go_dusty_rug`)**: Triggers a multi-stage cinematic event upon quest reward, moving captured farmers (entry 2284) to a tainted keg (entry 1729), playing animations, spawning smoke effects (entry 1730), and killing the farmers.

No database tables are accessed; all state is managed in memory.

## Member-by-Member Behavior

### Helcular's Grave

*   **`go_helcular_s_graveAI`**: Tracks Helcular's GUID.
    *   **`ctor`**: Initializes `GameObjectAI` and sets `guid_helcular` to 0.
    *   **`CheckHelcularSpawned`**: Returns `true` if `Map.Main/GetCreature` finds the stored GUID.
    *   **`SetHelcularGuid`**: Stores the result of `Object/GetGUID` from a summoned creature.
*   **`GetAIgo_helcular_s_grave`**: Factory returning a new `go_helcular_s_graveAI`.
*   **`QuestRewarded_go_helcular_s_grave`**: If `CheckHelcularSpawned` is false, summons Helcular at fixed coordinates via `WorldObject.Object/SummonCreature#2` and saves the GUID.

### Dusty Rug Event

*   **`go_dusty_rugAI`**: Manages a 4-step event timer.
    *   **`ctor`**: Initializes `GameObjectAI`, `timer` (0), and `step` (0).
    *   **`UpdateAI`**: Advances `step` when `timer` expires:
        *   **Step 1**: Finds nearest keg (`WorldObject.Object/FindNearestGameObject`). Collects alive farmers within 30 yards (`WorldObject.Object/GetCreatureListWithEntryInGrid`). Moves each to a contact point near the keg (`Creature.MotionMaster/MovePoint`). Timer: 4.5s.
        *   **Step 2**: Sets the first farmer to kneeling (`Unit.Main/SetStandState`). Timer: 2s.
        *   **Step 3**: Spawns smoke at keg position (`WorldObject.Object/SummonGameObject`). Sets first farmer to standing. Iterates `Farmers` list, killing each via `Unit.Main/DealDamage` (damage equals health). Clears list. Timer: 20s.
        *   **Step 4**: Resets `step` and `timer` to 0.
    *   **`StartEvent`**: If idle, sets `step`=1, `timer`=2s. Refreshes nearest keg (`GameObject/Refresh`) with 120s respawn (`GameObject/SetRespawnTime`).
*   **`GetAIgo_dusty_rug`**: Factory returning a new `go_dusty_rugAI`.
*   **`QuestRewarded_go_dusty_rug`**: Calls `StartEvent()` on the rug's AI.

### Registration

*   **`AddSC_hillsbrad_foothills`**: Registers both scripts via `ScriptMgr/RegisterSelf`. Called by `ScriptLoader/AddScripts`.

## Cross-Unit Boundaries

*   **Constructors**: Call `GameObjectAI/GameObjectAI`.
*   **`CheckHelcularSpawned`**: Calls `WorldObject.Object/GetMap` and `Map.Main/GetCreature`.
*   **`SetHelcularGuid`**: Calls `Object/GetGUID`.
*   **`QuestRewarded_go_helcular_s_grave`**: Calls `GameObject/AI` and `WorldObject.Object/SummonCreature#2`.
*   **`UpdateAI` (Dusty Rug)**: Calls `WorldObject.Object/FindNearestGameObject`, `WorldObject.Object/GetCreatureListWithEntryInGrid`, `Object/GetGUID`, `Creature.MotionMaster/MovePoint`, `Unit.Main/SetStandState`, `WorldObject.Object/GetPosition#2`, `WorldObject.Object/SummonGameObject`, `Map.Main/GetCreature`, `Unit.Main/IsAlive`, `Unit.Main/GetHealth`, `Unit.Main/DealDamage`, `Unit.Main/GetMotionMaster`, `WorldObject.Object/GetContactPoint`, `WorldObject.Object/GetMap`.
*   **`StartEvent`**: Calls `WorldObject.Object/FindNearestGameObject`, `GameObject/Refresh`, `GameObject/SetRespawnTime`.
*   **`AddSC_hillsbrad_foothills`**: Calls `Script/Script` and `ScriptMgr/RegisterSelf`.

## Data Model

No database tables are used.

## Notable Implementation Details

*   **Single-Farmer Animation**: Steps 2 and 3 only animate the *first* farmer in the `Farmers` list (`Farmers.front()`), though Step 1 collects all nearby farmers and Step 3 kills all of them.
*   **Transient GUID Tracking**: `CheckHelcularSpawned` only verifies if Helcular is loaded in memory. If he despawns or unloads, the grave will spawn him again on next quest reward.
*   **Hardcoded Coordinates**: Helcular spawns at fixed coordinates (-741.982, -621.186, 18.3853).
*   **Instant Kill**: Farmers are killed by dealing damage equal to their current health (`curr->GetHealth()`), bypassing resistances or immunities.

## Member Reference

**go_helcular_s_graveAI** (ctor): Initializes `GameObjectAI` and sets `guid_helcular` to 0. Calls `GameObjectAI/GameObjectAI`.

**CheckHelcularSpawned** (method): Returns `true` if `Map.Main/GetCreature` finds the stored GUID. Uses `WorldObject.Object/GetMap`.

**SetHelcularGuid** (method): Stores the creature's GUID via `Object/GetGUID`.

**GetAIgo_helcular_s_grave** (function): Factory returning a new `go_helcular_s_graveAI`.

**QuestRewarded_go_helcular_s_grave** (function): If Helcular isn't spawned, summons him via `WorldObject.Object/SummonCreature#2` and saves GUID. Uses `GameObject/AI`.

**go_dusty_rugAI** (ctor): Initializes `GameObjectAI`, `timer` (0), and `step` (0). Calls `GameObjectAI/GameObjectAI`.

**UpdateAI** (method): Advances 4-step event: (1) Move farmers to keg, (2) Kneel first farmer, (3) Spawn smoke, stand first farmer, kill all farmers, (4) Reset. Uses `Creature.MotionMaster/MovePoint`, `Map.Main/GetCreature`, `Object/GetGUID`, `ObjectGuid/ObjectGuid#5`, `Unit.Main/DealDamage`, `Unit.Main/GetHealth`, `Unit.Main/GetMotionMaster`, `Unit.Main/IsAlive`, `Unit.Main/SetStandState`, `WorldObject.Object/FindNearestGameObject`, `WorldObject.Object/GetContactPoint`, `WorldObject.Object/GetCreatureListWithEntryInGrid`, `WorldObject.Object/GetMap`, `WorldObject.Object/GetPosition#2`, `WorldObject.Object/SummonGameObject`.

**StartEvent** (method): Starts event, refreshes keg. Uses `GameObject/Refresh`, `GameObject/SetRespawnTime`, `WorldObject.Object/FindNearestGameObject`.

**GetAIgo_dusty_rug** (function): Factory returning a new `go_dusty_rugAI`.

**QuestRewarded_go_dusty_rug** (function): Calls `StartEvent()` on AI. Uses `GameObject/AI`.

**AddSC_hillsbrad_foothills** (function): Registers scripts. Calls `Script/Script`, `ScriptMgr/RegisterSelf`. Called by `ScriptLoader/AddScripts`.

---

<!-- machine-true, projected from graph.json -->

## Map — hillsbrad_foothills

*Source:* hillsbrad_foothills.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| go_helcular_s_graveAI | ctor | GameObjectAI/GameObjectAI | — | — |
| CheckHelcularSpawned | method | Map.Main/GetCreature, ObjectGuid/ObjectGuid#5, WorldObject.Object/GetMap | — | — |
| SetHelcularGuid | method | Object/GetGUID | — | — |
| GetAIgo_helcular_s_grave | function | — | — | — |
| QuestRewarded_go_helcular_s_grave | function | GameObject/AI, WorldObject.Object/SummonCreature#2 | — | — |
| go_dusty_rugAI | ctor | GameObjectAI/GameObjectAI | — | — |
| UpdateAI | method | Creature.MotionMaster/MovePoint, Map.Main/GetCreature, Object/GetGUID, ObjectGuid/ObjectGuid#5, Unit.Main/DealDamage, Unit.Main/GetHealth, Unit.Main/GetMotionMaster, Unit.Main/IsAlive, Unit.Main/SetStandState, WorldObject.Object/FindNearestGameObject, WorldObject.Object/GetContactPoint, WorldObject.Object/GetCreatureListWithEntryInGrid, WorldObject.Object/GetMap, WorldObject.Object/GetPosition#2, WorldObject.Object/SummonGameObject | — | — |
| StartEvent | method | GameObject/Refresh, GameObject/SetRespawnTime, WorldObject.Object/FindNearestGameObject | — | — |
| GetAIgo_dusty_rug | function | — | — | — |
| QuestRewarded_go_dusty_rug | function | GameObject/AI | — | — |
| AddSC_hillsbrad_foothills | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
