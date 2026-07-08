# azshara

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Azshara Region Scripts (`azshara.cpp`)

This translation unit implements scripted behaviors for two entities in the Azshara zone: **Alita Maws** (creature) and the **Bay of Storms** (game object). It contains no database interactions; all logic relies on hardcoded constants, in-memory state, and engine API calls.

## Purpose & Responsibilities

1.  **Alita Maws AI (`mob_mawsAI`)**:
    *   **Patrol**: Executes a waypoint loop defined by the static `ronde` array when out of combat.
    *   **Combat**: Handles melee attacks and periodic spells (`Rampage`, `Frenzy`).
    *   **Enrage**: At 20% health, enters Phase Two, reducing spell cooldowns and adding `Dark Water`.
    *   **Anti-Kite**: Prevents threat-resetting via kiting by requiring 30 seconds of no damage to disengage.
    *   **Cleanup**: Despawns the linked `Bay of Storms` game object on death or removal.

2.  **Bay of Storms AI (`go_bay_of_stormsAI`)**:
    *   **Animation**: Cycles through three custom animation states at random intervals (3–8 seconds).

3.  **Registration (`AddSC_azshara`)**:
    *   Registers the AI factories with the `ScriptMgr` under names `"mob_maws"` and `"go_bay_of_storms"`.

## Member-by-Member Behavior

### Alita Maws Creature AI

*   **`mob_mawsAI`**: Constructor initializes `LastWayPoint` to 2 and calls `Reset()`.
*   **`Reset`**: Clears combat state, auras, and threat. Sets initial timers for spells. Restores movement to `LastWayPoint` using `ronde` coordinates. Sets walk mode based on current faction.
*   **`MovementInform`**: Triggered on waypoint arrival. If out of combat, queues the next point in `ronde`. At waypoint 14, it temporarily sets faction to `FACTION_MONSTER` (14) and loops back to waypoint 1. Updates `LastWayPoint` for reset tracking.
*   **`UpdateAI#2`**: Main tick.
    *   **Disengage**: If no victim or `LeaveCombatTimer` expires, calls `Reset()`.
    *   **Phase Two**: Triggers at <20% HP. Caps `Rampage` and `Frenzy` timers to 12s and 15s respectively.
    *   **Spells**: Casts `Rampage` (victim), `Frenzy` (self), and `Dark Water` (self, Phase Two only) based on timers.
    *   **Movement**: Chases victim if idle.
*   **`DamageTaken`**: Resets `LeaveCombatTimer` to 30s, preventing disengagement while taking damage.
*   **`JustDied`**: Calls `DespawnBayOfStorms()` and broadcasts `EMOTE_THE_BEAST_RETURNS` globally.
*   **`OnRemoveFromWorld`**: Calls `DespawnBayOfStorms()` to ensure cleanup on non-death removal.
*   **`DespawnBayOfStorms`**: Finds the nearest `GO_BAY_OF_STORMS` (180670) within `MAX_VISIBILITY_DISTANCE` and deletes it.
*   **`GetAI_mob_maws`**: Factory function returning a new `mob_mawsAI`.

### Bay of Storms Game Object AI

*   **`go_bay_of_stormsAI`**: Constructor sets `m_animId` to 0 and `m_playAnimTimer` to 1s.
*   **`UpdateAI`**: If timer expires, plays current `m_animId`, increments ID (wrapping at 2), and sets a new random timer (3–8s).
*   **`GetAI_go_bay_of_storms`**: Factory function returning a new `go_bay_of_stormsAI`.

### Registration

*   **`AddSC_azshara`**: Creates and registers `Script` objects for both entities with `ScriptMgr`.

## Cross-Unit Boundaries

*   **`mob_mawsAI` → `ScriptedAI`**: Inherits base AI utilities.
*   **`mob_mawsAI` → `Creature`**: Modifies faction, movement, auras, and threat.
*   **`mob_mawsAI` → `WorldObject`**: Locates and deletes the Bay of Storms GO.
*   **`mob_mawsAI` → `World`**: Broadcasts death message globally.
*   **`go_bay_of_stormsAI` → `GameObject`**: Plays custom animations.
*   **`AddSC_azshara` → `ScriptMgr`**: Registers scripts.

## Data Model

No database tables are accessed.

## Notable Implementation Details

*   **Patrol Indexing**: The `ronde` array has 15 entries, but index 0 is unused. Patrol uses indices 1–14.
*   **Faction Toggle**: Reaching waypoint 14 sets faction to `FACTION_MONSTER` temporarily, likely to trigger hostile visuals or behavior at the loop's end.
*   **Anti-Kite Logic**: `LeaveCombatTimer` resets on *any* damage, forcing players to stop attacking for 30s to reset the mob.
*   **Global Broadcast**: Death notifies the entire server, not just the zone.

## Member Reference

*   **`mob_mawsAI`**: Constructor for Alita Maws AI; initializes state and calls `Reset`.
*   **`DespawnBayOfStorms`**: Finds and deletes the nearest Bay of Storms game object.
*   **`OnRemoveFromWorld`**: Calls `DespawnBayOfStorms` on creature removal.
*   **`MovementInform`**: Handles waypoint progression, faction toggle at loop end, and `LastWayPoint` tracking.
*   **`UpdateAI#2`**: Manages combat state, phase transitions, spell timers, and movement.
*   **`DamageTaken`**: Resets anti-kite timer to prevent disengagement during active combat.
*   **`JustDied`**: Despawns linked GO and broadcasts global death message.
*   **`Reset`**: Resets AI state, timers, auras, and movement to last waypoint.
*   **`GetAI_mob_maws`**: Factory function for `mob_mawsAI`.
*   **`go_bay_of_stormsAI`**: Constructor for Bay of Storms GO AI; initializes animation state.
*   **`UpdateAI`**: Cycles through custom animations for the Bay of Storms GO at random intervals.
*   **`GetAI_go_bay_of_storms`**: Factory function for `go_bay_of_stormsAI`.
*   **`AddSC_azshara`**: Registers both AI scripts with the server's script manager.

---

<!-- machine-true, projected from graph.json -->

## Map — azshara

*Source:* azshara.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| mob_mawsAI | ctor | ScriptedAI/ScriptedAI | — | — |
| DespawnBayOfStorms | method | WorldObject.Object/DeleteLater, WorldObject.Object/FindNearestGameObject | — | — |
| OnRemoveFromWorld | method | — | — | — |
| MovementInform | method | Creature.Main/SetFactionTemporary, Creature.MotionMaster/MovePoint, Unit.Main/GetFactionTemplateId, Unit.Main/GetMotionMaster | — | — |
| UpdateAI#2 | method | Creature.MotionMaster/GetCurrentMovementGeneratorType, Creature.MotionMaster/MoveChase, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, Unit.Main/GetHealthPercent, Unit.Main/GetMotionMaster, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| DamageTaken | method | — | — | — |
| JustDied | method | World/SendBroadcastTextToWorld | — | — |
| Reset | method | Creature.Main/SetLootRecipient, Creature.MotionMaster/MovePoint, shared_Util/urand, Unit.Main/CombatStop, Unit.Main/DeleteThreatList, Unit.Main/GetFactionTemplateId, Unit.Main/GetMotionMaster, Unit.Main/RemoveAllAuras, Unit.Main/SetWalk | — | — |
| GetAI_mob_maws | function | — | — | — |
| go_bay_of_stormsAI | ctor | GameObjectAI/GameObjectAI | — | — |
| UpdateAI | method | GameObject/SendGameObjectCustomAnim, shared_Util/urand | — | — |
| GetAI_go_bay_of_storms | function | — | — | — |
| AddSC_azshara | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
