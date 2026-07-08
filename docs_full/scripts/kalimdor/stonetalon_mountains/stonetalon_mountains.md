<!-- provenance: verbose -->
# stonetalon_mountains

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# stonetalon_mountains

## Purpose & Responsibilities

`stonetalon_mountains.cpp` implements the AI and quest logic for **Piznik** (`npc_piznik`), supporting **Quest 1090 ("Gerenos' Orders")**. The unit manages a timed survival event triggered when a player accepts the quest. During the event, Piznik becomes attackable by players (`UNIT_FLAG_PVP`) and summons three waves of hostile creatures over 180 seconds. If Piznik dies during the event, the quest fails. If the timer expires, the quest progresses via `GroupEventHappens`. Outside the event, Piznik is immune to NPCs and ignores combat initiation.

## Member-by-Member Behavior

### Event Lifecycle
*   **`npc_piznikAI` (ctor):** Initializes `InEvent` to `false`, `EventTimer` to `0`, and calls `Reset()`.
*   **`Reset`:** Empty override; performs no custom cleanup.
*   **`JustRespawned`:** Sets `UNIT_FLAG_IMMUNE_TO_NPC` to prevent NPC aggression and calls `ScriptedAI::JustRespawned()`.
*   **`StartEvent`:** Triggered by `QuestAccept_npc_piznik`. If not already active, it sets `InEvent` to `true`, resets phase/timer, stores the player's GUID, applies `UNIT_FLAG_PVP`, removes immunity, and sets a temporary faction (`FACTION_ESCORT_N_FRIEND_ACTIVE`).
*   **`JustDied`:** If `InEvent` is true, it retrieves the tracked player and calls `FailQuest(QUEST_GERENOS_ORDERS)`, then resets `InEvent`.

### Combat & Summoning
*   **`AttackStart`:** Prevents Piznik from initiating combat if `InEvent` is true; otherwise delegates to `ScriptedAI::AttackStart`.
*   **`JustSummoned`:** Directs summoned creatures to a specific coordinate via `MovePoint` and sets their home position.
*   **`UpdateAI`:** Handles melee combat if a victim exists. If `InEvent` is true, it advances through four phases based on `EventTimer`:
    1.  **INIT (0s):** Summons two creatures (IDs 3998, 4001).
    2.  **SECOND_WAVE (60s):** Summons three creatures (two 3998, one 4001).
    3.  **THIRD_WAVE (120s):** Summons three creatures (two 3998, one 4003).
    4.  **END (180s):** Triggers `GroupEventHappens` for the tracked player, resets `InEvent`, removes `UNIT_FLAG_PVP`, and restores faction.
    The timer increments by `uiDiff` each tick.

### Script Registration
*   **`GetAI_npc_piznik`:** Factory function returning a new `npc_piznikAI`.
*   **`QuestAccept_npc_piznik`:** Validates quest ID 1090 and invokes `StartEvent` on the creature's AI.
*   **`AddSC_stonetalon_mountains`:** Registers the script "npc_piznik" with the engine, linking the AI getter and quest accept hook.

## Cross-Unit Boundaries

*   **`ScriptedAI`**: Base class for `npc_piznikAI`; called for `JustRespawned` and `AttackStart`.
*   **`Creature` / `Unit` / `WorldObject`**:
    *   `SetFlag`/`RemoveFlag`: Manage PvP and immunity states.
    *   `SummonCreature`: Spawns enemy waves in `UpdateAI`.
    *   `GetMotionMaster`/`MovePoint`/`SetHomePosition`: Position summoned creatures in `JustSummoned`.
    *   `GetMap`/`GetPlayer`: Retrieve the tracked player in `JustDied` and `UpdateAI`.
    *   `SelectHostileTarget`/`GetVictim`/`DoMeleeAttackIfReady`: Handle combat in `UpdateAI`.
    *   `SetFactionTemporary`/`RestoreFaction`: Manage faction state in `StartEvent` and `UpdateAI`.
*   **`Player`**:
    *   `FailQuest`: Called in `JustDied` on failure.
    *   `GroupEventHappens`: Called in `UpdateAI` on success.
*   **`QuestDef`**: `GetQuestId` used in `QuestAccept_npc_piznik` to validate the quest.
*   **`Script` / `ScriptMgr`**: `RegisterSelf` used in `AddSC_stonetalon_mountains` to load the script.

## Data Model

This unit does not interact with any database tables. All logic relies on hardcoded constants (quest ID, creature IDs, coordinates, timers).

## Notable Implementation Details

*   **Single-Player Tracking:** The event tracks only one `ObjectGuid` (`pGuid`). If multiple players are present, only the one who accepted the quest triggers `GroupEventHappens` or `FailQuest`.
*   **PvP Flag:** `UNIT_FLAG_PVP` makes Piznik attackable by players during the event. Combined with `AttackStart` blocking initiation, Piznik only fights back if attacked.
*   **No Explicit Cleanup:** Summoned creatures despawn via `TEMPSUMMON_TIMED_OR_CORPSE_DESPAWN` (120s) or death. They are not manually removed if the event fails early.
*   **Hardcoded Timers:** Wave intervals (60s, 120s, 180s) and coordinates are fixed in source.

## Member Reference

**npc_piznikAI** (ctor): Initializes `InEvent` to `false`, `EventTimer` to `0`, and calls `Reset()`. Inherits from `ScriptedAI`.

**Reset**: Empty override of the base class method.

**JustRespawned**: Sets `UNIT_FLAG_IMMUNE_TO_NPC` on the creature and calls `ScriptedAI::JustRespawned()`.

**JustSummoned**: Moves the summoned creature to a specific coordinate via `MovePoint` and sets its home position.

**JustDied**: If `InEvent` is true, retrieves the tracked player and calls `FailQuest(QUEST_GERENOS_ORDERS)`, then resets `InEvent`.

**StartEvent**: Activates the event if not already running. Sets `InEvent=true`, resets timer/phase, stores player GUID, applies `UNIT_FLAG_PVP`, removes immunity, and sets a temporary faction.

**AttackStart**: Prevents automatic attack initiation if `InEvent` is true; otherwise delegates to `ScriptedAI::AttackStart`.

**UpdateAI**: Handles melee combat if a victim exists. If `InEvent` is true, manages wave phases based on `EventTimer`: spawns creatures at 0s, 60s, 120s, and completes the event at 180s by calling `GroupEventHappens` and resetting state. Increments `EventTimer` by `uiDiff`.

**GetAI_npc_piznik**: Factory function returning a new `npc_piznikAI` instance.

**QuestAccept_npc_piznik**: Validates quest ID 1090 and invokes `StartEvent` on the creature's AI. Returns `true`.

**AddSC_stonetalon_mountains**: Registers the script "npc_piznik" with the engine, linking the AI getter and quest accept hook.

---

<!-- machine-true, projected from graph.json -->

## Map — stonetalon_mountains

*Source:* stonetalon_mountains.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| npc_piznikAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | method | — | — | — |
| JustRespawned | method | ScriptedAI/JustRespawned, WorldObject.Object/SetFlag | — | — |
| JustSummoned | method | Creature.Main/SetHomePosition, Creature.MotionMaster/MovePoint, Unit.Main/GetMotionMaster | — | — |
| JustDied | method | Map.Main/GetPlayer, Player.Main/FailQuest, WorldObject.Object/GetMap | — | — |
| StartEvent | method | Creature.Main/SetFactionTemporary, Object/GetObjectGuid, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag | — | — |
| AttackStart | method | CreatureAI/AttackStart | — | — |
| UpdateAI | method | CreatureAI/DoMeleeAttackIfReady, Map.Main/GetPlayer, Player.Main/GroupEventHappens, Unit.Main/GetVictim, Unit.Main/RestoreFaction, Unit.Main/SelectHostileTarget, WorldObject.Object/GetMap, WorldObject.Object/RemoveFlag, WorldObject.Object/SummonCreature#2 | — | — |
| GetAI_npc_piznik | function | — | — | — |
| QuestAccept_npc_piznik | function | Creature.Main/AI, QuestDef/GetQuestId | — | — |
| AddSC_stonetalon_mountains | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
