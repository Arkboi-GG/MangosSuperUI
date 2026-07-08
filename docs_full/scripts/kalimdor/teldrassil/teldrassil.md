# teldrassil

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`teldrassil.cpp` implements scripted behaviors for two NPCs in the Teldrassil zone. Its primary role is managing **Quest 938 ("The Mist")**, where `npc_mist` acts as a follower that completes the quest upon reaching a specific location near `NPC_ARYNIA` (ID 3519). Secondarily, it adds a visual emote trigger for **Quest 1142** via `npc_treshala_fallowbrook`. The unit contains no direct database queries; all logic is driven by engine APIs and hardcoded constants.

## Member-by-Member Behavior

### `npc_mist` Follower AI

The `npc_mistAI` class extends `FollowerAI` to handle the specific completion conditions for Quest 938.

*   **`npc_mistAI` (Constructor)**: Initializes the AI and calls `Reset()`.
*   **`Reset`**: Empty override; relies entirely on `FollowerAI` base behavior.
*   **`JustRespawned`**: Sets `UNIT_FLAG_IMMUNE_TO_NPC` to prevent NPC aggression upon respawn, then calls `FollowerAI::JustRespawned()`.
*   **`MoveInLineOfSight`**: Core completion logic. After calling the base implementation, it checks if the creature is not in combat, not already completed, and sees `NPC_ARYNIA` within 10.0 units. If true, it speaks `SAY_AT_HOME` and calls `DoComplete()`.
*   **`DoComplete`**: Plays `EMOTE_AT_HOME`, retrieves the leader player, and if `QUEST_MIST` is incomplete, triggers `GroupEventHappens` to award the quest. Finally, it calls `SetFollowComplete()` to stop the follower state.

### Global Script Hooks

*   **`GetAI_npc_mist`**: Factory function returning a new `npc_mistAI` instance for a creature.
*   **`QuestAccept_npc_mist`**: Triggered on quest accept for `QUEST_MIST`. It casts the creature's AI to `npc_mistAI`, sets temporary faction to `FACTION_DARNASSUS` (79), removes `UNIT_FLAG_IMMUNE_TO_NPC`, and starts the follow sequence via `StartFollow`.
*   **`QuestComplete_npc_treshala_fallowbrook`**: Triggered on quest completion for `QUEST_MORTALITY_WANES` (1142). It makes the quest giver perform `EMOTE_ONESHOT_CRY` and returns `false` to allow default database reward processing.

### Registration

*   **`AddSC_teldrassil`**: Registers `npc_mist` (with AI and quest accept hooks) and `npc_treshala_fallowbrook` (with quest reward hook) with the `ScriptMgr`.

## Cross-Unit Boundaries

*   **`npc_mistAI` → `FollowerAI`**: Inherits follower mechanics. Calls `JustRespawned`, `MoveInLineOfSight`, `HasFollowState`, `GetLeaderForFollower`, `SetFollowComplete`, and `StartFollow` to manage the follow state and lifecycle.
*   **`npc_mistAI` → `WorldObject.Object`**: Calls `SetFlag` (immunity) and `IsWithinDistInMap` (proximity check).
*   **`npc_mistAI` → `ScriptMgr`**: Calls `DoScriptText` to broadcast speech/emotes.
*   **`npc_mistAI` → `Unit.Main` / `Object`**: Calls `GetVictim` (combat check) and `GetEntry` (NPC identification).
*   **`QuestAccept_npc_mist` → `Creature.Main` / `ScriptedFollowerAI`**: Calls `SetFactionTemporary`, `RemoveFlag`, and `StartFollow`.
*   **`QuestComplete_npc_treshala_fallowbrook` → `Unit.Main`**: Calls `HandleEmoteCommand`.
*   **`AddSC_teldrassil` → `ScriptMgr` / `Script`**: Allocates and registers script objects. Called by `ScriptLoader/AddScripts`.

## Data Model

No direct database tables are accessed. Constants (`QUEST_MIST`, `NPC_ARYNIA`, etc.) correspond to rows in `creature_template`, `quest_template`, and `creature_text`, accessed via engine APIs.

## Notable Implementation Details

1.  **Immunity Toggle**: `npc_mist` is immune to NPCs by default (`JustRespawned`). This flag is removed in `QuestAccept_npc_mist` to allow normal interaction/combat during the quest.
2.  **Proximity Completion**: Quest completion is triggered automatically by `MoveInLineOfSight` when within 10.0 units of `NPC_ARYNIA`, requiring no player input beyond leading the follower there.
3.  **Safety Check**: `MoveInLineOfSight` checks `!HasFollowState(STATE_FOLLOW_COMPLETE)` to prevent duplicate completions if the creature lingers in range.
4.  **Hardcoded Faction**: `FACTION_DARNASSUS` is forced in `QuestAccept_npc_mist` to ensure friendliness for Night Elf players.
5.  **Empty Reset**: `Reset` is intentionally empty, relying on `FollowerAI` base logic.

## Member Reference

*   **`npc_mistAI`**: Constructor for the Mist creature's AI. Initializes the object and calls `Reset`. Inherits from `FollowerAI`.
*   **`Reset`**: Empty override method. Delegates all reset logic to the base `FollowerAI` class.
*   **`JustRespawned`**: Sets `UNIT_FLAG_IMMUNE_TO_NPC` on the creature to prevent NPC aggression upon respawn, then calls the base `FollowerAI::JustRespawned`.
*   **`MoveInLineOfSight`**: Checks if the creature sees `NPC_ARYNIA` within 10.0 units while not in combat and not already completed. If so, triggers dialogue and calls `DoComplete`.
*   **`DoComplete`**: Plays an emote, checks if the leader player has the quest incomplete, awards the quest via `GroupEventHappens`, and marks the follower state as complete.
*   **`GetAI_npc_mist`**: Factory function that instantiates and returns a `npc_mistAI` object for a given creature.
*   **`QuestAccept_npc_mist`**: Intercepts quest acceptance for `QUEST_MIST`. Sets temporary faction to Darnassus, removes NPC immunity, and starts the follower AI.
*   **`QuestComplete_npc_treshala_fallowbrook`**: Intercepts quest completion for `QUEST_MORTALITY_WANES`. Triggers a cry emote on the quest giver and returns false to allow default reward processing.
*   **`AddSC_teldrassil`**: Registers the `npc_mist` and `npc_treshala_fallowbrook` scripts with the `ScriptMgr` during server load.

---

<!-- machine-true, projected from graph.json -->

## Map — teldrassil

*Source:* teldrassil.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| npc_mistAI | ctor | ScriptedFollowerAI/FollowerAI | — | — |
| Reset | method | — | — | — |
| JustRespawned | method | ScriptedFollowerAI/JustRespawned, WorldObject.Object/SetFlag | — | — |
| MoveInLineOfSight | method | Object/GetEntry, ScriptedFollowerAI/HasFollowState, ScriptedFollowerAI/MoveInLineOfSight, ScriptMgr/DoScriptText, Unit.Main/GetVictim, WorldObject.Object/IsWithinDistInMap | — | — |
| DoComplete | method | Player.Main/GetQuestStatus, Player.Main/GroupEventHappens, ScriptedFollowerAI/GetLeaderForFollower, ScriptedFollowerAI/SetFollowComplete, ScriptMgr/DoScriptText | — | — |
| GetAI_npc_mist | function | — | — | — |
| QuestAccept_npc_mist | function | Creature.Main/AI, Creature.Main/SetFactionTemporary, QuestDef/GetQuestId, ScriptedFollowerAI/StartFollow, WorldObject.Object/RemoveFlag | — | — |
| QuestComplete_npc_treshala_fallowbrook | function | QuestDef/GetQuestId, Unit.Main/HandleEmoteCommand | — | — |
| AddSC_teldrassil | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
