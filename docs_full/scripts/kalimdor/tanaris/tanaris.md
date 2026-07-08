# tanaris

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Tanaris Zone Scripts (`tanaris.cpp`)

**Purpose & Responsibilities**
This translation unit implements scripted behaviors for three specific entities in the Tanaris zone of the game world:
1.  **Tooga (`npc_tooga`)**: A follower AI for Quest 1560. It handles the mechanics of Tooga following the player, detecting the arrival of a companion NPC (Torta), triggering a multi-stage dialogue event between Tooga and Torta, and finally moving Tooga to a specific coordinate to complete the follow sequence.
2.  **Inconspicuous Landmark (`go_inconspicuous_landmark`)**: A Game Object AI for Quest 2882 ("Cuergo's Gold"). It manages a cooldown system to prevent spamming, and upon interaction, summons a group of hostile pirates that attack the player.
3.  **Yeh'Kinya (`npc_yehkinya`)**: An escort AI triggered by the reward of Quest 8181. It handles a visual transformation event at a specific waypoint during the escort, including changing display ID, equipping/unequipping gear, enabling flight, and pausing/resuming the escort path.

The unit also registers these scripts with the global `ScriptMgr` via `AddSC_tanaris`. It does not interact with any database tables directly; all logic is driven by in-memory state, quest IDs, and entity interactions.

---

## Member-by-Member Behavior

### Tooga Follower Logic (`npc_toogaAI`)

This class inherits from `FollowerAI` and manages the lifecycle of the NPC Tooga during Quest 1560.

*   **`npc_toogaAI` (ctor)**: Initializes the AI and immediately calls `Reset()` to set initial timers and states.
*   **`Reset`**: Clears internal timers (`m_uiCheckSpeechTimer`, `m_uiPostEventTimer`) and phase counters. It clears the stored GUID of the companion NPC Torta (`m_tortaGuid`) and enables the `MoveInLineOfSight` event on the creature so it can detect nearby entities.
*   **`MoveInLineOfSight`**: Triggered when another unit enters Tooga's line of sight.
    *   It first delegates to the parent `FollowerAI::MoveInLineOfSight`.
    *   If Tooga has no victim (not in combat) and is not already in a completion/post-event state, it checks if the seen unit is **Torta** (entry `6015`).
    *   If Torta is within interaction distance, it retrieves the leader (player) for the follower. If the player has Quest 1560 in an incomplete status, it triggers the quest event `GroupEventHappens` for that player.
    *   It stores Torta's GUID in `m_tortaGuid` and marks the follow as complete (`SetFollowComplete(true)`), transitioning the AI into the post-event dialogue phase.
*   **`MovementInform`**: Called when Tooga reaches a movement point.
    *   Delegates to parent.
    *   If the motion type is `POINT_MOTION_TYPE` and the point ID is `POINT_ID_TO_WATER` (1), it calls `SetFollowComplete()` again. This acts as a final cleanup step after Tooga moves to the water location at the end of the dialogue.
*   **`UpdateFollowerAI`**: The main update loop, executed every tick.
    *   **Combat Check**: If Tooga has a hostile target, it performs melee attacks via `DoMeleeAttackIfReady` and skips other logic.
    *   **Post-Event Phase**: If in `STATE_FOLLOW_POSTEVENT`:
        *   It uses `m_uiPostEventTimer` (initially 1000ms, then 5000ms between steps) to pace a dialogue sequence.
        *   It verifies that Torta (`m_tortaGuid`) still exists and is alive. If Torta is missing or dead, it aborts the event and completes the follow.
        *   It executes a switch statement on `m_uiPhasePostEvent` (1–6):
            *   Phases 1, 3, 5: Tooga speaks (`SAY_TOOG_POST_*`).
            *   Phases 2, 4, 6: Torta speaks (`SAY_TORT_POST_*`).
            *   Phase 6 additionally commands Tooga to move to the hardcoded water coordinates (`m_afToWaterLoc`).
        *   After each phase, it increments `m_uiPhasePostEvent`.
    *   **In-Progress Phase**: If in `STATE_FOLLOW_INPROGRESS`:
        *   It uses `m_uiCheckSpeechTimer` (randomized 30–60 seconds) to occasionally trigger random chatter from Tooga directed at the player leader.
*   **`GetAI_npc_tooga`**: Factory function returning a new `npc_toogaAI` instance.
*   **`QuestAccept_npc_tooga`**: Triggered when a player accepts Quest 1560.
    *   It casts the creature's AI to `npc_toogaAI` and starts the follow sequence with `StartFollow`, setting the faction to passive friendly.

### Inconspicuous Landmark Logic (`go_inconspicuous_landmarkAI`)

This class inherits from `GameObjectAI` and manages the "Inconspicuous Landmark" object for Quest 2882.

*   **`go_inconspicuous_landmarkAI` (ctor)**: Initializes `timer` to 0 and `state` to 0 (ready).
*   **`UpdateAI`**: Handles the cooldown timer.
    *   If `state` is 1 (in use), it decrements `timer`.
    *   When `timer` expires, it resets `state` to 0, sets the GO state to `GO_STATE_READY`, and removes the `GO_FLAG_IN_USE` flag, making it interactive again.
*   **`CheckCanStartEvent`**: Returns `true` only if `state` is 0 (not currently in use/cooldown).
*   **`SetInUse`**: Sets `state` to 1, updates the GO state to `GO_STATE_ACTIVE`, adds the `GO_FLAG_IN_USE` flag, and sets the cooldown timer to 600,000 ms (10 minutes).
*   **`GetAIgo_inconspicuous_landmark`**: Factory function returning a new `go_inconspicuous_landmarkAI` instance.
*   **`GOHello_go_inconspicuous_landmark`**: Triggered when a player interacts with the landmark.
    *   It checks if the event can start via `CheckCanStartEvent`.
    *   If valid, it calls `SetInUse()` to lock the object.
    *   It verifies the GO type is `GAMEOBJECT_TYPE_GOOBER`.
    *   It checks if the player has Quest 2882 incomplete.
    *   If so, it summons 5 pirates:
        *   3 fixed types (`NPC_PIRATES_1`, `_2`, `_3`) at specific coordinates.
        *   2 random types selected from `extraPirateType` array using `urand`.
    *   All summoned pirates are set to attack the player immediately. They despawn after death or 310 seconds.

### Yeh'Kinya Escort Logic (`npc_yehkinyaAI`)

This class inherits from `npc_escortAI` and manages the escort behavior for Yeh'Kinya after Quest 8181 is rewarded.

*   **`npc_yehkinyaAI` (ctor)**: Initializes the AI and calls `Reset()`.
*   **`Reset`**: Resets event flags, loads equipment set 1315, sets Display ID to 7902, disables flying and walking animations.
*   **`WaypointReached`**: Triggered when Yeh'Kinya reaches a waypoint in the escort path.
    *   At Waypoint 1:
        *   Disables walking animation.
        *   Sets `m_isEventStarted` to true.
        *   Unloads current equipment (`LoadEquipment(0, true)`).
        *   Casts `SPELL_TRANSFORM_VISUAL` (24085) on self.
        *   Changes Display ID to 1336.
        *   Enables flying.
        *   Pauses the escort path (`SetEscortPaused(true)`).
        *   Sets a 3-second timer (`m_uiEventTimer`) before resuming.
*   **`UpdateEscortAI`**: Main update loop.
    *   If `m_uiEventTimer` expires:
        *   If `m_isEventStarted` is true, it unpauses the escort, plays dialogue `SAY_HAKKAR_EVENT_2`, ensures walking animation is off, and sets a 15-second timer (likely for future extensions or stability, though no further logic uses it in this snippet).
    *   Otherwise, it decrements the timer.
*   **`GetAI_npc_yehkinya`**: Factory function returning a new `npc_yehkinyaAI` instance.
*   **`QuestRewarded_npc_yehkinya`**: Triggered when a player turns in Quest 8181.
    *   Plays dialogue `SAY_HAKKAR_EVENT_1`.
    *   Starts the escort sequence via `Start(true, 0, nullptr, true)`.
    *   Ensures the creature is not walking (`SetWalk(false)`).

### Script Registration

*   **`AddSC_tanaris`**: Registers the three scripts (`npc_yehkinya`, `npc_tooga`, `go_inconspicuous_landmark`) with the `ScriptMgr`. It binds the appropriate AI getters and quest/hello handlers for each.

---

## Cross-Unit Boundaries

*   **`npc_toogaAI` ↔ `FollowerAI`**: Inherits core follow logic. Calls `GetLeaderForFollower`, `HasFollowState`, `SetFollowComplete`, and `MoveInLineOfSight` from the base class.
*   **`npc_toogaAI` ↔ `ScriptedFollowerAI`**: Uses `DoScriptText` for dialogue.
*   **`npc_toogaAI` ↔ `Creature`/`Unit`**: Interacts with the creature's motion master, victim status, and map data to find Torta.
*   **`go_inconspicuous_landmarkAI` ↔ `GameObject`**: Modifies GO state and flags to manage usability.
*   **`GOHello_go_inconspicuous_landmark` ↔ `WorldObject`**: Uses `SummonCreature` to spawn pirates.
*   **`npc_yehkinyaAI` ↔ `npc_escortAI`**: Inherits escort pathing logic. Calls `SetEscortPaused` and `Start`.
*   **`npc_yehkinyaAI` ↔ `ScriptMgr`**: Uses `DoScriptText` for dialogue.
*   **`AddSC_tanaris` ↔ `ScriptMgr`**: Registers scripts globally. Called by `ScriptLoader`.

---

## Data Model

This unit does not access any database tables directly. All configuration (quest IDs, NPC entries, spell IDs, coordinates) is hardcoded in enums and constants within the source file.

---

## Notable Implementation Details

1.  **Hardcoded Coordinates**: The destination for Tooga's final movement is hardcoded in `m_afToWaterLoc` (`{-7032.664551f, -4906.199219f, -1.606446f}`). Any map changes affecting this area would require code updates.
2.  **Torta Dependency**: The `npc_toogaAI` relies on finding a specific NPC entry (`6015`, Torta) within interaction distance to trigger the post-event. If Torta is missing, dead, or too far away when Tooga arrives, the quest event may fail to trigger correctly or abort early.
3.  **Cooldown Management**: The `go_inconspicuous_landmarkAI` uses a simple boolean `state` and `timer` variable for cooldowns. This is vulnerable to server restarts (cooldowns reset) but sufficient for runtime prevention of spam.
4.  **Random Pirate Spawns**: The landmark summons two additional pirates with randomized types from a predefined list. This adds slight variability to the encounter.
5.  **Visual Transformation**: Yeh'Kinya's escort involves a complex visual change (equipment removal, spell cast, display ID change, flight enable) at Waypoint 1. The escort is paused during this transition to ensure the animation/spell completes before movement resumes.
6.  **Timer Precision**: Timers in `UpdateFollowerAI` and `UpdateEscortAI` are decremented by `uiDiff`. If `uiDiff` is larger than the timer (e.g., due to lag or pause), the timer becomes negative, but the logic checks `< uiDiff` or `<= diff`, ensuring the event triggers on the next tick. However, large gaps could cause multiple phases to skip if not handled carefully (though the current logic increments phase once per tick where timer expires, so rapid ticks might be needed for smooth pacing).

---

## Member Reference

*   **`npc_toogaAI`**: Constructor for Tooga's AI, initializes timers and calls `Reset`.
*   **`Reset`**: Resets Tooga's internal state, timers, and enables LOS events.
*   **`MoveInLineOfSight`**: Detects Torta, triggers quest event, stores Torta's GUID, and starts post-event.
*   **`MovementInform`**: Handles arrival at movement points, specifically completing follow at the water location.
*   **`UpdateFollowerAI`**: Main loop for Tooga; handles combat, post-event dialogue pacing, and random chatter.
*   **`GetAI_npc_tooga`**: Factory function for `npc_toogaAI`.
*   **`QuestAccept_npc_tooga`**: Starts Tooga's follow sequence when Quest 1560 is accepted.
*   **`go_inconspicuous_landmarkAI`**: Constructor for the Landmark's AI, initializes state and timer.
*   **`UpdateAI`**: Manages the 10-minute cooldown for the Landmark.
*   **`CheckCanStartEvent`**: Returns true if the Landmark is not on cooldown.
*   **`SetInUse`**: Locks the Landmark and starts the cooldown timer.
*   **`GetAIgo_inconspicuous_landmark`**: Factory function for `go_inconspicuous_landmarkAI`.
*   **`GOHello_go_inconspicuous_landmark`**: Triggers pirate summoning when player interacts with the Landmark.
*   **`npc_yehkinyaAI`**: Constructor for Yeh'Kinya's AI, initializes state and calls `Reset`.
*   **`Reset#2`**: Resets Yeh'Kinya's appearance, equipment, and movement flags.
*   **`WaypointReached`**: Triggers visual transformation and pauses escort at Waypoint 1.
*   **`UpdateEscortAI`**: Resumes escort after transformation delay and plays dialogue.
*   **`GetAI_npc_yehkinya`**: Factory function for `npc_yehkinyaAI`.
*   **`QuestRewarded_npc_yehkinya`**: Starts Yeh'Kinya's escort sequence when Quest 8181 is rewarded.
*   **`AddSC_tanaris`**: Registers all Tanaris scripts with the script manager.

---

<!-- machine-true, projected from graph.json -->

## Map — tanaris

*Source:* tanaris.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| npc_toogaAI | ctor | ScriptedFollowerAI/FollowerAI | — | — |
| Reset | method | Creature.Main/EnableMoveInLosEvent, ObjectGuid/Clear, shared_Util/urand | — | — |
| MoveInLineOfSight | method | Object/GetEntry, Object/GetObjectGuid, Player.Main/GetQuestStatus, Player.Main/GroupEventHappens, ScriptedFollowerAI/GetLeaderForFollower, ScriptedFollowerAI/HasFollowState, ScriptedFollowerAI/MoveInLineOfSight, ScriptedFollowerAI/SetFollowComplete, Unit.Main/GetVictim, WorldObject.Object/IsWithinDistInMap | — | — |
| MovementInform | method | ScriptedFollowerAI/MovementInform, ScriptedFollowerAI/SetFollowComplete | — | — |
| UpdateFollowerAI | method | Creature.MotionMaster/MovePoint, CreatureAI/DoMeleeAttackIfReady, Map.Main/GetCreature, ScriptedFollowerAI/GetLeaderForFollower, ScriptedFollowerAI/HasFollowState, ScriptedFollowerAI/SetFollowComplete, ScriptMgr/DoScriptText, shared_Util/urand, Unit.Main/GetMotionMaster, Unit.Main/GetVictim, Unit.Main/IsAlive, Unit.Main/SelectHostileTarget, WorldObject.Object/GetMap | — | — |
| GetAI_npc_tooga | function | — | — | — |
| QuestAccept_npc_tooga | function | Creature.Main/AI, QuestDef/GetQuestId, ScriptedFollowerAI/StartFollow | — | — |
| go_inconspicuous_landmarkAI | ctor | GameObjectAI/GameObjectAI | — | — |
| UpdateAI | method | GameObject/SetGoState, WorldObject.Object/RemoveFlag | — | — |
| CheckCanStartEvent | method | — | — | — |
| SetInUse | method | GameObject/SetGoState, WorldObject.Object/SetFlag | — | — |
| GetAIgo_inconspicuous_landmark | function | — | — | — |
| GOHello_go_inconspicuous_landmark | function | Creature.Main/AI, CreatureAI/AttackStart, GameObject/AI, GameObject/GetGoType, Player.Main/GetQuestStatus, shared_Util/urand, WorldObject.Object/SummonCreature#2 | — | — |
| npc_yehkinyaAI | ctor | ScriptedEscortAI/npc_escortAI | — | — |
| Reset#2 | method | Creature.Main/LoadEquipment, Unit.Main/SetDisplayId, Unit.Main/SetFly, Unit.Main/SetWalk | — | — |
| WaypointReached | method | Creature.Main/LoadEquipment, CreatureAI/DoCastSpellIfCan, ScriptedEscortAI/SetEscortPaused, Unit.Main/SetDisplayId, Unit.Main/SetFly, Unit.Main/SetWalk | — | — |
| UpdateEscortAI | method | ScriptedEscortAI/SetEscortPaused, ScriptMgr/DoScriptText, Unit.Main/SetWalk | — | — |
| GetAI_npc_yehkinya | function | — | — | — |
| QuestRewarded_npc_yehkinya | function | Creature.Main/AI, QuestDef/GetQuestId, ScriptedEscortAI/Start, ScriptMgr/DoScriptText, Unit.Main/SetWalk | — | — |
| AddSC_tanaris | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
