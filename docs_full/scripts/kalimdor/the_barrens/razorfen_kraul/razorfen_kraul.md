# razorfen_kraul

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# razorfen_kraul

**Purpose & Responsibilities**

The `razorfen_kraul` translation unit implements scripted behaviors for two specific non-player characters (NPCs) within the Razorfen Kraul dungeon instance: **Willix the Importer** and **Snufflenose Gopher**. These scripts handle quest-specific escort mechanics, follower AI, and spell-triggered interactions required for quests 1144 ("Willix the Importer") and 1221 ("Snufflenose Gopher").

The unit does not implement the dungeon instance data itself (that logic resides in `instance_razorfen_kraul`, declared in `razorfen_kraul.h` but implemented elsewhere). Instead, this file provides:
1.  An escort AI for Willix, managing his movement path, dialogue, summoning of hostile boars, and quest completion triggers.
2.  A follower AI for Snufflenose Gopher, managing its ability to follow players, search for specific game objects (Blueleaf Tubers), and interact via a dummy spell effect.
3.  Registration hooks to attach these scripts to the respective creature entries in the server's script manager.

## Member-by-Member Behavior

### Willix the Importer Escort Logic

This subsystem manages the escort quest for Willix. It inherits from `npc_escortAI` (mapped as `ScriptedEscortAI` in the MAP) to leverage built-in waypoint navigation and pause/resume capabilities.

*   **Initialization & State Management**:
    *   **`npc_willix_the_importerAI` (ctor)**: Initializes the AI and immediately calls `Reset`.
    *   **`Reset#2`**: This is the `Reset` method of `npc_willix_the_importerAI`. It currently overrides the base class reset but performs no custom cleanup or state initialization.
    *   **`JustRespawned`**: Sets the `UNIT_FLAG_IMMUNE_TO_NPC` flag on Willix, preventing other NPCs from attacking him while he is idle or respawning. It then delegates to the parent `ScriptedEscortAI::JustRespawned`.

*   **Combat & Aggression**:
    *   **`Aggro`**: Triggered when Willix enters combat. It randomly selects one of four aggro lines (`SAY_WILLIX_AGGRO_1` through `_4`) using `urand`. Note that the random range is 0–6, meaning there is a ~33% chance (cases 4, 5, 6) that no line is spoken.
    *   **`JustSummoned`**: When Willix summons creatures (specifically Raging Agamar boars), this method forces the summoned creature to immediately attack Willix (`AttackStart`).

*   **Waypoint Navigation & Events**:
    *   **`WaypointReached`**: The core logic driver. As Willix reaches specific waypoints, it triggers dialogue, summons enemies, or completes the quest.
        *   Waypoints 2, 6, 9, 25, 33, 44: Trigger specific dialogue lines (`SAY_WILLIX_1` through `_7`).
        *   Waypoint 14: Summons two Raging Agamar boars at predefined coordinates (`aBoarSpawn[0]` and `[1]`). These despawn after 25 seconds out of combat.
        *   Waypoint 44: Summons two more boars at different coordinates (`aBoarSpawn[2]` and `[3]`).
        *   Waypoint 45: Marks the end of the escort. It plays the final dialogue, sets Willix as a quest giver (`UNIT_NPC_FLAG_QUESTGIVER`), pauses the escort permanently, and triggers the group event for Quest 1144 if a player is attached to the escort.

*   **Quest Acceptance Hook**:
    *   **`QuestAccept_npc_willix_the_importer`**: This global function is called when a player accepts Quest 1144 from Willix. It verifies the quest ID, casts the creature's AI to `npc_willix_the_importerAI`, and starts the escort process. It sets Willix's faction to neutral-passive temporarily, removes the immune-to-NPC flag (allowing him to be attacked by mobs during the escort), and plays the start dialogue.

*   **Registration**:
    *   **`GetAI_npc_willix_the_importer`**: Factory function returning a new instance of the AI.

### Snufflenose Gopher Follower Logic

This subsystem manages the pet-like behavior of Snufflenose Gopher, which follows players and digs up Blueleaf Tubers. It inherits from `FollowerAI` (mapped as `ScriptedFollowerAI`).

*   **Initialization & State**:
    *   **`npc_snufflenose_gopherAI` (ctor)**: Initializes the AI, plays spawn dialogue, and attempts to start following the creature's owner (if the owner is a player). It initially pauses the follow state.
    *   **`Reset`**: Resets internal timers and flags. Sets the creature's faction template to 35 (likely neutral/friendly). Initializes `m_followPausedTimer` to 3 seconds.

*   **Movement & Tuber Discovery**:
    *   **`MovementInform`**: Called when the gopher reaches a movement point. If the gopher was paused and reached a target tuber (identified by `m_targetTuberGuid`), it marks the tuber as "found":
        *   Sets the tuber's respawn time to 3 minutes.
        *   Refreshes the game object.
        *   Removes the `GO_FLAG_INTERACT_COND` flag, making it interactable.
        *   Adds the tuber's GUID to `m_foundTubers` to prevent re-digging.
        *   Pauses movement for 5 seconds before resuming follow/search.
    *   **`DoFindNewTuber`**: The search routine. It queries all `GO_BLUELEAF_TUBER` objects within 60 yards, sorts them by distance, and validates them using `IsValidTuber`. If a valid tuber is found:
        *   Plays "Found" dialogue.
        *   Moves the gopher to the tuber's contact point.
        *   Sets `m_bIsMovementActive` to true and pauses following.
    *   **`IsValidTuber`**: Checks if a tuber is a valid target:
        *   Must not be spawned (already dug up).
        *   Must have the `GO_FLAG_INTERACT_COND` flag (indicating it's hidden/unavailable).
        *   Must be within Line of Sight (LOS) from the owner (or the gopher if no owner).
        *   Must not be in `m_foundTubers`.
        *   Must be within 15 vertical yards of the viewer's Z-position.

*   **Update Loop**:
    *   **`UpdateAI`**: Manages the pause timer. If the gopher is actively moving to a tuber, it skips logic. Otherwise, it decrements `m_followPausedTimer`. If the timer expires, it unpauses the follow state. It then delegates to the parent `FollowerAI::UpdateAI`.

*   **Spell Interaction**:
    *   **`EffectDummyCreature_npc_snufflenose_gopher`**: Handles the `SPELL_SNUFFLENOSE_COMMAND` (8283).
        *   Verifies the caster is targeting the gopher. If not, it sends a "Bad Target" failure message.
        *   If the gopher is paused (e.g., just finished digging), it resumes following and clears the target tuber.
        *   If the gopher is following normally, it triggers `DoFindNewTuber` to start searching for a new tuber.

*   **Registration**:
    *   **`GetAI_npc_snufflenose_gopher`**: Factory function for the AI.
    *   **`AddSC_razorfen_kraul`**: Registers both scripts with the `ScriptMgr`. It binds the AI getters and the specific quest/spell hooks to their respective creature names.

## Cross-Unit Boundaries

*   **`npc_willix_the_importerAI` ↔ `ScriptedEscortAI`**:
    *   *Direction*: Calls out.
    *   *Collaboration*: Inherits navigation, pause/resume, and player attachment logic. `JustRespawned` and `WaypointReached` rely on the base class to manage the escort state machine.
*   **`npc_willix_the_importerAI` ↔ `WorldObject.Object`**:
    *   *Direction*: Calls out.
    *   *Collaboration*: Uses `SetFlag` to manage immunity and quest-giver status. Uses `SummonCreature` to spawn boars.
*   **`npc_willix_the_importerAI` ↔ `ScriptMgr`**:
    *   *Direction*: Calls out.
    *   *Collaboration*: `DoScriptText` broadcasts dialogue to nearby players.
*   **`npc_willix_the_importerAI` ↔ `shared_Util`**:
    *   *Direction*: Calls out.
    *   *Collaboration*: `urand` generates random numbers for aggro dialogue selection.
*   **`npc_willix_the_importerAI` ↔ `Creature.Main` / `CreatureAI`**:
    *   *Direction*: Calls out.
    *   *Collaboration*: `JustSummoned` accesses the summoned creature's AI to initiate combat.
*   **`npc_willix_the_importerAI` ↔ `Player.Main`**:
    *   *Direction*: Calls out.
    *   *Collaboration*: `GroupEventHappens` notifies the player's group that the quest objective is complete.
*   **`QuestAccept_npc_willix_the_importer` ↔ `ScriptedEscortAI`**:
    *   *Direction*: Calls out.
    *   *Collaboration*: `Start` initializes the escort path and attaches the player.
*   **`QuestAccept_npc_willix_the_importer` ↔ `Creature.Main`**:
    *   *Direction*: Calls out.
    *   *Collaboration*: `SetFactionTemporary` changes Willix's faction during the quest. `AI` retrieves the AI pointer.
*   **`QuestAccept_npc_willix_the_importer` ↔ `Object` / `QuestDef`**:
    *   *Direction*: Calls out.
    *   *Collaboration*: `GetGUID` and `GetQuestId` verify the context of the quest acceptance.
*   **`npc_snufflenose_gopherAI` ↔ `ScriptedFollowerAI`**:
    *   *Direction*: Calls out.
    *   *Collaboration*: Inherits follow logic. `StartFollow`, `SetFollowPaused`, and `HasFollowState` manage the follower state. `UpdateAI` delegates to the parent.
*   **`npc_snufflenose_gopherAI` ↔ `Unit.Main`**:
    *   *Direction*: Calls out.
    *   *Collaboration*: `GetOwner` identifies the player controlling the gopher. `SetFactionTemplateId` resets faction on reset. `GetMotionMaster` and `MovePoint` control movement.
*   **`npc_snufflenose_gopherAI` ↔ `GridSearchers`**:
    *   *Direction*: Calls out.
    *   *Collaboration*: `GetGameObjectListWithEntryInGrid` finds nearby tubers.
*   **`npc_snufflenose_gopherAI` ↔ `WorldObject.Object`**:
    *   *Direction*: Calls out.
    *   *Collaboration*: `GetMap`, `GetContactPoint`, `GetPositionZ`, `IsWithinLOSInMap`, `HasFlag`, `RemoveFlag` handle spatial and state checks for tubers.
*   **`npc_snufflenose_gopherAI` ↔ `GameObject`**:
    *   *Direction*: Calls out.
    *   *Collaboration*: `Refresh` and `SetRespawnTime` update the tuber state after being dug up.
*   **`npc_snufflenose_gopherAI` ↔ `ScriptMgr`**:
    *   *Direction*: Calls out.
    *   *Collaboration*: `DoScriptText` plays dialogue.
*   **`EffectDummyCreature_npc_snufflenose_gopher` ↔ `ScriptedFollowerAI`**:
    *   *Direction*: Calls out.
    *   *Collaboration*: Checks follow state and triggers search/pause logic.
*   **`EffectDummyCreature_npc_snufflenose_gopher` ↔ `Unit.Main`**:
    *   *Direction*: Calls out.
    *   *Collaboration*: `GetTargetGuid` verifies targeting. `SendPetCastFail` handles invalid targets.
*   **`EffectDummyCreature_npc_snufflenose_gopher` ↔ `Object`**:
    *   *Direction*: Calls out.
    *   *Collaboration*: `ToUnit`, `GetEntry`, `GetObjectGuid` validate the spell target.
*   **`AddSC_razorfen_kraul` ↔ `ScriptMgr`**:
    *   *Direction*: Calls out.
    *   *Collaboration*: `RegisterSelf` adds the scripts to the global registry.
*   **`AddSC_razorfen_kraul` ↔ `ScriptLoader`**:
    *   *Direction*: Called by.
    *   *Collaboration*: The script loader invokes `AddSC_razorfen_kraul` during server startup to register these scripts.

## Data Model

This unit does not directly query or modify database tables. It interacts with runtime entities (Creatures, Game Objects, Players) whose persistent data may reside in tables like `creature`, `gameobject`, or `quest_template`, but no SQL statements or direct table access occur in this source file.

## Notable Implementation Details

1.  **Random Aggro Silence**: In `npc_willix_the_importerAI::Aggro`, the random number generator uses `urand(0, 6)`. However, the `switch` statement only handles cases 0–3. This means there is a 33% probability that Willix will enter combat without speaking any aggro line. This appears intentional ("Not always said" comment), but the gap between 3 and 6 is large.
2.  **Hardcoded Spawn Coordinates**: The boar spawns for Willix are hardcoded in the `aBoarSpawn` array. Any map changes affecting these coordinates would require code updates.
3.  **Tuber Validation Logic**: `IsValidTuber` performs a strict LOS check from the *owner* (player) if present, otherwise from the gopher. This ensures the player can see the tuber being dug up. It also enforces a 15-yard vertical difference limit, preventing the gopher from digging up tubers on significantly different terrain levels.
4.  **Follow Pause Timer**: The gopher uses a manual `m_followPausedTimer` in `UpdateAI` rather than relying solely on the base class's pause mechanism. This allows for a specific delay (5 seconds after digging, 3 seconds on reset) before resuming follow or search behavior.
5.  **Spell Target Verification**: `EffectDummyCreature_npc_snufflenose_gopher` explicitly checks if the caster's target matches the gopher's GUID. If not, it sends a `SpellCastResult` failure code `0x0A` (likely `SPELL_FAILED_BAD_TARGETS`). This prevents accidental activation if the player casts the spell while targeting something else.
6.  **Empty Reset**: `npc_willix_the_importerAI::Reset#2` is empty. This relies entirely on the base `npc_escortAI` reset behavior. If custom state needs clearing, it must be added here.
7.  **Header vs. Source Discrepancy**: The header `razorfen_kraul.h` declares `instance_razorfen_kraul`, but this class is not implemented in `razorfen_kraul.cpp`. The `.cpp` file only contains the NPC scripts. Engineers should look elsewhere for the instance script implementation.

## Member Reference

**npc_willix_the_importerAI** (ctor): Initializes the escort AI and calls `Reset`. Inherits from `ScriptedEscortAI`.

**Reset#2**: Overrides base reset; currently performs no custom actions.

**JustRespawned**: Sets `UNIT_FLAG_IMMUNE_TO_NPC` on Willix to prevent NPC attacks, then calls parent `JustRespawned`.

**Aggro**: Randomly selects and plays one of four aggro lines (33% chance of silence due to `urand(0,6)` vs cases 0-3).

**JustSummoned**: Forces summoned creatures (boars) to immediately attack Willix.

**WaypointReached**: Triggers dialogue, summons boars at waypoints 14 and 44, and completes the quest at waypoint 45 by setting quest-giver flag and triggering group event.

**GetAI_npc_willix_the_importer**: Factory function returning a new `npc_willix_the_importerAI` instance.

**QuestAccept_npc_willix_the_importer**: Global hook for Quest 1144 acceptance. Starts the escort, sets temporary faction, removes immunity, and plays start dialogue.

**npc_snufflenose_gopherAI** (ctor): Initializes follower AI, plays spawn dialogue, starts following owner if present, and pauses follow state.

**Reset**: Resets faction, timers, and movement flags.

**MovementInform**: Handles arrival at tuber location. Marks tuber as found (refreshes, removes interaction condition, adds to found list), and pauses movement for 5 seconds.

**DoFindNewTuber**: Searches for valid tubers within 60 yards, moves to the nearest one, and pauses following.

**IsValidTuber**: Validates a tuber candidate by checking spawn state, interaction flag, LOS from owner/gopher, previous discovery, and vertical distance.

**UpdateAI**: Manages follow pause timer and delegates to parent `FollowerAI`.

**GetAI_npc_snufflenose_gopher**: Factory function returning a new `npc_snufflenose_gopherAI` instance.

**EffectDummyCreature_npc_snufflenose_gopher**: Handles `SPELL_SNUFFLENOSE_COMMAND`. Verifies target, resumes following if paused, or triggers tuber search if following.

**AddSC_razorfen_kraul**: Registers both NPC scripts with the `ScriptMgr`.

---

<!-- machine-true, projected from graph.json -->

## Map — razorfen_kraul

*Source:* razorfen_kraul.cpp, razorfen_kraul.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| npc_willix_the_importerAI | ctor | ScriptedEscortAI/npc_escortAI | — | — |
| Reset#2 | method | — | — | — |
| JustRespawned | method | ScriptedEscortAI/JustRespawned, WorldObject.Object/SetFlag | — | — |
| Aggro | method | ScriptMgr/DoScriptText, shared_Util/urand | — | — |
| JustSummoned | method | Creature.Main/AI, CreatureAI/AttackStart | — | — |
| WaypointReached | method | Player.Main/GroupEventHappens, ScriptedEscortAI/GetPlayerForEscort, ScriptedEscortAI/SetEscortPaused, ScriptMgr/DoScriptText, WorldObject.Object/SetFlag, WorldObject.Object/SummonCreature#2 | — | — |
| GetAI_npc_willix_the_importer | function | — | — | — |
| QuestAccept_npc_willix_the_importer | function | Creature.Main/AI, Creature.Main/SetFactionTemporary, Object/GetGUID, QuestDef/GetQuestId, ScriptedEscortAI/Start, ScriptMgr/DoScriptText, WorldObject.Object/RemoveFlag | — | — |
| npc_snufflenose_gopherAI | ctor | Object/ToPlayer, ScriptedFollowerAI/FollowerAI, ScriptedFollowerAI/SetFollowPaused, ScriptedFollowerAI/StartFollow, ScriptMgr/DoScriptText, Unit.Main/GetOwner | — | — |
| Reset | method | Unit.Main/SetFactionTemplateId | — | — |
| MovementInform | method | GameObject/Refresh, GameObject/SetRespawnTime, Map.Main/GetGameObject, ScriptedFollowerAI/HasFollowState, WorldObject.Object/GetMap, WorldObject.Object/RemoveFlag | — | — |
| DoFindNewTuber | method | Creature.MotionMaster/MovePoint, GridSearchers/GetGameObjectListWithEntryInGrid#2, Object/GetObjectGuid, ObjectDistanceOrder/ObjectDistanceOrder, ScriptedFollowerAI/SetFollowPaused, ScriptMgr/DoScriptText, Unit.Main/GetMotionMaster, WorldObject.Object/GetContactPoint | — | — |
| IsValidTuber | method | GameObject/isSpawned, Object/GetObjectGuid, Object/HasFlag, ObjectGuid/operator==, Unit.Main/GetOwner, WorldObject.Object/GetPositionZ, WorldObject.Object/IsWithinLOSInMap | — | — |
| UpdateAI | method | ScriptedFollowerAI/SetFollowPaused, ScriptedFollowerAI/UpdateAI | — | — |
| GetAI_npc_snufflenose_gopher | function | — | — | — |
| EffectDummyCreature_npc_snufflenose_gopher | function | Creature.Main/AI, Object/GetEntry, Object/GetObjectGuid, Object/ToUnit, ObjectGuid/ObjectGuid#5, ObjectGuid/operator!=, ScriptedFollowerAI/HasFollowState, ScriptedFollowerAI/SetFollowPaused, ScriptMgr/DoScriptText, Unit.Main/GetTargetGuid, Unit.Main/SendPetCastFail | — | — |
| AddSC_razorfen_kraul | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
