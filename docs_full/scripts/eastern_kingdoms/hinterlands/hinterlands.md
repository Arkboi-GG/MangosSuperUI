# hinterlands

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# hinterlands.cpp

## Purpose & Responsibilities

`hinterlands.cpp` provides scripted behaviors for two entities in the Hinterlands zone:
1.  **Rinji (`npc_rinji`)**: An escort NPC for Quest 2742 ("Rinji Trapped"). The script manages the escort lifecycle, including spawning ambush enemies at specific waypoints, handling combat dialogue, and triggering quest completion.
2.  **Lard's Picnic Basket (`go_lards_picnic_basket`)**: A game object trap. Interaction spawns hostile "Kidnappeur Vilebranch" creatures near the player, subject to a 5-minute cooldown.

No database tables are accessed; all configuration (coordinates, IDs, dialogue) is hardcoded.

## Member-by-Member Behavior

### Rinji Escort (`npc_rinjiAI`)

Inherits from `npc_escortAI`.

*   **`npc_rinjiAI` (ctor)**: Initializes state (`m_bIsByOutrunner = false`, `m_iSpawnId = 0`) and calls `Reset`.
*   **`Reset`**: Resets `m_uiPostEventCount` to 0 and `m_uiPostEventTimer` to 3000ms.
*   **`JustRespawned`**: Resets outrunner/spawn flags. Sets `UNIT_FLAG_IMMUNE_TO_NPC` to prevent ambient aggro. Calls parent `JustRespawned`.
*   **`Aggro`**: If escorting:
    *   If attacker is `NPC_OUTRUNNER` and not yet flagged, plays `SAY_RIN_BY_OUTRUNNER` and sets `m_bIsByOutrunner = true`.
    *   With 25% probability (`urand(0,3) == 0`), plays a random help line (`SAY_RIN_HELP_1` or `SAY_RIN_HELP_2`).
*   **`DoSpawnAmbush`**: Spawns one `NPC_RANGER` and two `NPC_OUTRUNNER` mobs at coordinates from `m_afAmbushSpawn[m_iSpawnId]`. If `bFirst` is false, sets `m_iSpawnId = 1` for subsequent calls. Mobs despawn after 60s or death.
*   **`JustSummoned`**: Sets summoned mobs to run (`SetWalk(false)`) and moves them to `m_afAmbushMoveTo[m_iSpawnId]`.
*   **`WaypointReached`**:
    *   WP 1: Plays `SAY_RIN_FREE`.
    *   WP 7: Calls `DoSpawnAmbush(true)`.
    *   WP 13: Calls `DoSpawnAmbush(false)`.
    *   WP 17: Plays `SAY_RIN_COMPLETE`, triggers `GroupEventHappens` for Quest 2742, sets Rinji to run, and starts post-event dialogue (`m_uiPostEventCount = 1`).
*   **`UpdateEscortAI`**:
    *   If no target: Manages post-event dialogue timer. If `m_uiPostEventCount` > 0, plays `SAY_RIN_PROGRESS_1` then `SAY_RIN_PROGRESS_2` sequentially. If the escort player is missing during this phase, forces despawn.
    *   If target exists: Calls `DoMeleeAttackIfReady`.

### Quest Hook

*   **`QuestAccept_npc_rinji`**: For Quest 2742:
    *   Opens nearby `GO_RINJI_CAGE`.
    *   Sets Rinji's faction to `FACTION_ESCORTEE` (temporary).
    *   Removes `UNIT_FLAG_IMMUNE_TO_NPC`.
    *   Starts the escort via `npc_rinjiAI::Start`.

### Picnic Basket Trap (`go_lards_picnic_basketAI`)

Inherits from `GameObjectAI`.

*   **`go_lards_picnic_basketAI` (ctor)**: Initializes `timer = 0`, `state = 0` (ready).
*   **`UpdateAI`**: If `state == 1` (in use), decrements `timer`. On expiry, resets `state = 0`, sets `GO_STATE_READY`, and removes `GO_FLAG_IN_USE`.
*   **`CheckCanStartEvent`**: Returns `!state`.
*   **`SetInUse`**: Sets `GO_STATE_ACTIVE`, adds `GO_FLAG_IN_USE`, sets `state = 1`, and starts a 300,000ms (5 min) timer.

### Trap Hook

*   **`GOHello_go_lards_picnic_basket`**: If GO is `GAMEOBJECT_TYPE_GOOBER` and `CheckCanStartEvent()` is true:
    *   Calls `SetInUse()`.
    *   Spawns 3 `NPC_KIDNAPPEUR_VILEBRANCH` at player position (despawn on death/30s).

### Registration

*   **`GetAI_npc_rinji` / `GetAIgo_lards_picnic_basket`**: Factory functions for their respective AIs.
*   **`AddSC_hinterlands`**: Registers both scripts with `ScriptMgr`, binding AI factories and event hooks (`QuestAccept_npc_rinji`, `GOHello_go_lards_picnic_basket`).

## Cross-Unit Boundaries

*   **`npc_escortAI`**: Base class for Rinji’s movement and player tracking.
*   **`ScriptMgr`**: Used for `DoScriptText` (dialogue) and `RegisterSelf` (script registration).
*   **`WorldObject/Object`**: Used for `SetFlag`/`RemoveFlag` (immunity/in-use states) and `SummonCreature` (mob spawning).
*   **`Creature/Unit`**: Used for `GetEntry` (attacker ID), `GetVictim`/`SelectHostileTarget` (combat), `DoMeleeAttackIfReady`, `ForcedDespawn`, `GetMotionMaster`/`MovePoint` (summoned mob movement), and `SetWalk`.
*   **`Player`**: Used for `GroupEventHappens` (quest completion) and `GetGUID` (escort start).
*   **`GameObject`**: Used for `UseDoorOrButton` (cage open), `SetGoState`, `GetGoType`, and `AI` (retrieving basket AI).
*   **`ScriptLoader`**: Calls `AddSC_hinterlands` to register scripts.

## Data Model

No database tables are accessed.

## Notable Implementation Details

*   **Ambush Coordinates**: Two sets of spawn/move coordinates are defined in `m_afAmbushSpawn` and `m_afAmbushMoveTo`. `m_iSpawnId` switches between them after the first ambush.
*   **Post-Escort Despawn**: If the player disconnects during the post-escort dialogue sequence (after WP 17), `UpdateEscortAI` forces Rinji to despawn.
*   **Immunity Toggle**: Rinji is immune to NPCs on respawn. Immunity is removed only on quest acceptance, preventing premature aggro.
*   **Trap Cooldown**: The picnic basket enforces a 5-minute cooldown via `UpdateAI` to prevent spam.

## Member Reference

**npc_rinjiAI** (ctor): Initializes Rinji AI state and calls `Reset`.
**Reset**: Resets post-event counters and timers.
**JustRespawned**: Resets flags, sets NPC immunity, calls parent.
**Aggro**: Plays outrunner-specific or random help dialogue if escorting.
**DoSpawnAmbush**: Spawns Ranger/Outrunners at indexed coordinates.
**JustSummoned**: Moves summoned mobs to attack points.
**WaypointReached**: Triggers dialogue, ambushes, and quest completion at waypoints.
**UpdateEscortAI**: Handles melee combat and post-escort dialogue timer/despawn.
**QuestAccept_npc_rinji**: Opens cage, adjusts faction/flags, starts escort.
**GetAI_npc_rinji**: Factory for `npc_rinjiAI`.
**go_lards_picnic_basketAI** (ctor): Initializes basket AI state/timer.
**UpdateAI**: Manages trap cooldown timer and state reset.
**CheckCanStartEvent**: Returns true if not on cooldown.
**SetInUse**: Activates trap, sets flags, starts cooldown.
**GetAIgo_lards_picnic_basket**: Factory for `go_lards_picnic_basketAI`.
**GOHello_go_lards_picnic_basket**: Triggers trap, spawns mobs if ready.
**AddSC_hinterlands**: Registers scripts with `ScriptMgr`.

---

<!-- machine-true, projected from graph.json -->

## Map — hinterlands

*Source:* hinterlands.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| npc_rinjiAI | ctor | ScriptedEscortAI/npc_escortAI | — | — |
| Reset | method | — | — | — |
| JustRespawned | method | ScriptedEscortAI/JustRespawned, WorldObject.Object/SetFlag | — | — |
| Aggro | method | Object/GetEntry, ScriptedEscortAI/HasEscortState, ScriptMgr/DoScriptText, shared_Util/urand | — | — |
| DoSpawnAmbush | method | WorldObject.Object/SummonCreature#2 | — | — |
| JustSummoned | method | Creature.MotionMaster/MovePoint, Unit.Main/GetMotionMaster, Unit.Main/SetWalk | — | — |
| WaypointReached | method | Player.Main/GroupEventHappens, ScriptedEscortAI/GetPlayerForEscort, ScriptedEscortAI/SetRun, ScriptMgr/DoScriptText | — | — |
| UpdateEscortAI | method | Creature.Main/ForcedDespawn, CreatureAI/DoMeleeAttackIfReady, ScriptedEscortAI/GetPlayerForEscort, ScriptedEscortAI/HasEscortState, ScriptMgr/DoScriptText, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| QuestAccept_npc_rinji | function | Creature.Main/AI, Creature.Main/SetFactionTemporary, GameObject/UseDoorOrButton, GridSearchers/GetClosestGameObjectWithEntry, Object/GetGUID, QuestDef/GetQuestId, ScriptedEscortAI/Start, WorldObject.Object/RemoveFlag | — | — |
| GetAI_npc_rinji | function | — | — | — |
| go_lards_picnic_basketAI | ctor | GameObjectAI/GameObjectAI | — | — |
| UpdateAI | method | GameObject/SetGoState, WorldObject.Object/RemoveFlag | — | — |
| CheckCanStartEvent | method | — | — | — |
| SetInUse | method | GameObject/SetGoState, WorldObject.Object/SetFlag | — | — |
| GetAIgo_lards_picnic_basket | function | — | — | — |
| GOHello_go_lards_picnic_basket | function | GameObject/AI, GameObject/GetGoType, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SummonCreature#2 | — | — |
| AddSC_hinterlands | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
