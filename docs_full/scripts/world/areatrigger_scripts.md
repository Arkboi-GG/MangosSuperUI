# areatrigger_scripts

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# areatrigger_scripts

**Purpose & Responsibilities**
This unit implements logic for two specific area triggers: one for Children's Week mini-pet discovery events and one for granting quest credit for Manor Ravenholdt. It also registers these scripts with the engine.

**Member-by-Member Behavior**

### Children's Week Spot Logic
`AreaTrigger_at_childrens_week_spot` handles discovery events for players with active mini-pets. It uses a static array `TriggerOrphanSpell` mapping area trigger IDs to mini-pet entries and event IDs. When triggered, it iterates the array; if the area trigger ID matches `i[0]` and the player's active mini-pet entry (retrieved via `Player.Main::GetMiniPet` and `Object::GetEntry`) matches `i[1]`, it calls `Player.Main::AreaExploredOrEventHappens` with `i[2]` and returns `true`. Otherwise, it returns `false`.

### Manor Ravenholdt Logic
`AreaTrigger_at_ravenholdt` grants quest credit for Quest 6681. If `Player.Main::GetQuestStatus` indicates the quest is incomplete, it calls `Player.Main::KilledMonsterCredit` for NPC 13936. It always returns `false`.

### Script Registration
`AddSC_areatrigger_scripts` creates `Script` objects for both triggers, assigns their handlers, and calls `Script::RegisterSelf`. It is invoked by `ScriptLoader::AddScripts` during initialization.

**Cross-Unit Boundaries**
*   **`AreaTrigger_at_childrens_week_spot`**: Calls `Object::GetEntry` (via `Player.Main::GetMiniPet`) to identify the mini-pet, and `Player.Main::AreaExploredOrEventHappens` to trigger the client event.
*   **`AreaTrigger_at_ravenholdt`**: Calls `Player.Main::GetQuestStatus` to check quest progress and `Player.Main::KilledMonsterCredit` to award credit.
*   **`AddSC_areatrigger_scripts`**: Calls `Script::Script` and `Script::RegisterSelf`. Called by `ScriptLoader::AddScripts`.

**Data Model**
No database tables are accessed. All IDs are hardcoded.

**Notable Implementation Details**
*   **Static Configuration**: The `TriggerOrphanSpell` array is hardcoded; adding new spots requires recompilation.
*   **Null Safety**: `AreaTrigger_at_childrens_week_spot` checks `GetMiniPet()` for null before accessing its entry.
*   **Quest State Guard**: `AreaTrigger_at_ravenholdt` only acts if the quest status is `INCOMPLETE`, preventing duplicate credits.

## Member Reference

**AreaTrigger_at_childrens_week_spot**: Iterates `TriggerOrphanSpell` to match the area trigger ID and the player's active mini-pet entry (via `Player.Main::GetMiniPet` and `Object::GetEntry`). On match, calls `Player.Main::AreaExploredOrEventHappens` with the corresponding event ID and returns `true`; otherwise returns `false`.

**AreaTrigger_at_ravenholdt**: Checks if Quest 6681 is incomplete via `Player.Main::GetQuestStatus`. If so, grants kill credit for NPC 13936 via `Player.Main::KilledMonsterCredit`. Always returns `false`.

**AddSC_areatrigger_scripts**: Registers the `at_ravenholdt` and `at_childrens_week_spot` scripts by creating `Script` objects and calling `Script::RegisterSelf`. Called by `ScriptLoader::AddScripts`.

---

<!-- machine-true, projected from graph.json -->

## Map — areatrigger_scripts

*Source:* areatrigger_scripts.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AreaTrigger_at_childrens_week_spot | function | Object/GetEntry, Player.Main/AreaExploredOrEventHappens, Player.Main/GetMiniPet | — | — |
| AreaTrigger_at_ravenholdt | function | Player.Main/GetQuestStatus, Player.Main/KilledMonsterCredit | — | — |
| AddSC_areatrigger_scripts | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
