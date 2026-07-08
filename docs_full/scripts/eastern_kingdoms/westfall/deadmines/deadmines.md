<!-- provenance: verbose -->
# deadmines

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# deadmines

## Purpose & Responsibilities

The `deadmines` translation unit implements scripted interactions for three Game Objects (GOs) in the Deadmines instance: the exit door lever, the Defias cannon, and a Defias gunpowder barrel. It manages the state gating for the instance exit, triggers a one-time summoning event for a Defias Overseer, and provides the AI logic to guide that summoned creature along a fixed path. The unit registers these scripts with the core engine via `AddSC_deadmines`.

## Member-by-Member Behavior

### Exit Door Lever Logic
**`GOHello_go_door_lever_dm`** determines if a player can interact with the exit door lever. It retrieves the instance data manager and uses `DATA_DEFIAS_DOOR` to find the GUID of the actual exit door. If the door exists and its state is `1` (closed/locked), the function returns `false`, blocking interaction. Otherwise, it returns `true`, allowing the lever to be used.

### Cannon Event Trigger
**`GOHello_go_defias_cannon`** gates the cannon interaction. It checks the `TYPE_DEFIAS_ENDDOOR` encounter state. If the state is `DONE` or `IN_PROGRESS`, it returns `false`, ignoring further inputs. If the event has not started, it sets the state to `IN_PROGRESS` and returns `false`.

### Gunpowder Barrel Event
**`GOHello_go_defias_gunpowder`** manages a one-time event triggered by interacting with a gunpowder barrel. It checks if the `GUN_POWDER_EVENT` flag is `0`. If so, it summons a Defias Overseer (entry ID 634) at fixed coordinates, configures it to despawn after 310 seconds if out of combat, and sets its respawn delay to 350 seconds. It immediately commands the creature to move to the first waypoint. The instance flag is then set to `1` to prevent re-triggering. The function returns `true`.

### Gunpowder AI and Movement Tracking
**`go_defias_gunpowderAI`** is a minimal AI class derived from `GameObjectAI` that tracks the movement of creatures summoned by the gunpowder barrel.

**`SummonedMovementInform`** overrides the base method to handle the Defias Overseer's pathfinding. When the summoned creature reaches `point_id` 0, the AI commands it to move to `point_id` 1 at specific coordinates. Upon reaching `point_id` 1, the AI sets the creature's home position to those coordinates, anchoring it there.

**`GetAIgo_defias_gunpowder`** is a factory function that instantiates and returns a new `go_defias_gunpowderAI` object for a given game object.

### Script Registration
**`AddSC_deadmines`** registers the three game object scripts with the core script manager. It creates `Script` objects for `go_door_lever_dm`, `go_defias_cannon`, and `go_defias_gunpowder`, assigning the appropriate handler functions (`pGOHello`) and the AI getter (`GOGetAI`) for the gunpowder barrel. Each script is then registered with the `ScriptMgr`.

## Cross-Unit Boundaries

*   **`GOHello_go_door_lever_dm`**:
    *   Calls `WorldObject.Object/GetInstanceData` to retrieve the instance script manager.
    *   Calls `InstanceData/GetData64` to fetch the GUID of the exit door.
    *   Calls `Map.Main/GetGameObject` to obtain the actual door object from the map.
    *   Calls `GameObject/GetGoState` to check if the door is open/closed.
    *   Uses `ObjectGuid/ObjectGuid#5` implicitly via the GUID retrieval.

*   **`GOHello_go_defias_cannon`**:
    *   Calls `WorldObject.Object/GetInstanceData` to get the instance manager.
    *   Calls `InstanceData/GetData` to check the current state of the end door event.
    *   Calls `InstanceData/SetData` to update the event state to `IN_PROGRESS`.

*   **`GOHello_go_defias_gunpowder`**:
    *   Calls `WorldObject.Object/GetInstanceData` to get the instance manager.
    *   Calls `InstanceData/GetData` to check if the gunpowder event has occurred.
    *   Calls `WorldObject.Object/SummonCreature#2` to spawn the Defias Overseer.
    *   Calls `Unit.Main/GetMotionMaster` to access the summoned creature's movement controller.
    *   Calls `Creature.MotionMaster/MovePoint` to initiate the first movement step.
    *   Calls `Creature.Main/SetRespawnDelay` to configure the summoned creature's respawn timer.
    *   Calls `InstanceData/SetData` to mark the event as completed.

*   **`go_defias_gunpowderAI::SummonedMovementInform`**:
    *   Calls `Unit.Main/GetMotionMaster` to access the summoned creature's movement controller.
    *   Calls `Creature.MotionMaster/MovePoint` to initiate the second movement step.
    *   Calls `Creature.Main/SetHomePosition` to anchor the creature at the final destination.

*   **`AddSC_deadmines`**:
    *   Calls `Script/Script` constructor to create script descriptors.
    *   Calls `ScriptMgr/RegisterSelf` to register each script with the global manager.
    *   Is called by `ScriptLoader/AddScripts` during server startup.

## Data Model

This unit does not interact directly with any database tables. All state management is handled through the in-memory `ScriptedInstance` system using integer data keys defined in `deadmines.h` (e.g., `TYPE_DEFIAS_ENDDOOR`, `DATA_DEFIAS_DOOR`, `GUN_POWDER_EVENT`).

## Notable Implementation Details

*   **Hardcoded Coordinates:** The summoning and movement points for the Defias Overseer in `GOHello_go_defias_gunpowder` and `SummonedMovementInform` use hardcoded floating-point coordinates. Any changes to the map geometry or object placement would require updating these values manually.
*   **Single-Use Event:** The `GUN_POWDER_EVENT` flag is set to `1` upon first trigger and never reset. This means the event can only happen once per instance load. If the instance resets, the flag would need to be cleared by the instance reset logic (not shown here).
*   **Return Value Semantics:** The `GOHello_*` functions return boolean values. In `GOHello_go_door_lever_dm`, `true` allows interaction. In `GOHello_go_defias_cannon` and `GOHello_go_defias_gunpowder`, `false` and `true` respectively seem to indicate whether the script handled the event exclusively or if the default behavior should proceed. Specifically, `GOHello_go_defias_cannon` returns `false` after setting the state, which might prevent the default "use" animation or sound if the framework interprets `false` as "do not proceed with default action." Conversely, `GOHello_go_defias_gunpowder` returns `true`, potentially signaling success or allowing the default action. The exact interpretation depends on the `ScriptMgr`'s handling of `pGOHello` return values.
*   **Motion Master Chaining:** The `SummonedMovementInform` method chains movement commands. It relies on the `motion_type` being `POINT_MOTION_TYPE` and the `point_id` matching the expected sequence (0 then 1). If the creature arrives at a different point ID or motion type, no action is taken.

## Member Reference

*   **`GOHello_go_door_lever_dm`**: Checks if the exit door is closed; prevents lever interaction if so.
*   **`GOHello_go_defias_cannon`**: Gates the cannon event; sets state to `IN_PROGRESS` if not already active.
*   **`GOHello_go_defias_gunpowder`**: Triggers a one-time event summoning a Defias Overseer who moves to a specific location.
*   **`go_defias_gunpowderAI`**: AI class for the gunpowder barrel, tracking summoned creature movement.
*   **`SummonedMovementInform`**: Handles the two-step movement path of the summoned Defias Overseer.
*   **`GetAIgo_defias_gunpowder`**: Factory function to create the `go_defias_gunpowderAI` instance.
*   **`AddSC_deadmines`**: Registers all Deadmines GO scripts with the core engine.

---

<!-- machine-true, projected from graph.json -->

## Map — deadmines

*Source:* deadmines.cpp, deadmines.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GOHello_go_door_lever_dm | function | GameObject/GetGoState, InstanceData/GetData64, Map.Main/GetGameObject, ObjectGuid/ObjectGuid#5, WorldObject.Object/GetInstanceData | — | — |
| GOHello_go_defias_cannon | function | InstanceData/GetData, InstanceData/SetData, WorldObject.Object/GetInstanceData | — | — |
| GOHello_go_defias_gunpowder | function | Creature.Main/SetRespawnDelay, Creature.MotionMaster/MovePoint, InstanceData/GetData, InstanceData/SetData, Unit.Main/GetMotionMaster, WorldObject.Object/GetInstanceData, WorldObject.Object/SummonCreature#2 | — | — |
| go_defias_gunpowderAI | ctor | GameObjectAI/GameObjectAI | — | — |
| SummonedMovementInform | method | Creature.Main/SetHomePosition, Creature.MotionMaster/MovePoint, Unit.Main/GetMotionMaster | — | — |
| GetAIgo_defias_gunpowder | function | — | — | — |
| AddSC_deadmines | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
