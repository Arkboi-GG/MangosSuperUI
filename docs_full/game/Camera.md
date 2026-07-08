<!-- provenance: verbose -->
# Camera

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Camera

The `Camera` class manages the visual perspective of a `Player` (`m_owner`) within the game world. By default, a player views the world from their own character's position. Mechanics such as Far Sight, Bind Sight, possession, or cinematics temporarily shift this perspective to another `WorldObject` (the "viewpoint," stored in `m_source`). `Camera` acts as a proxy, ensuring the player receives correct visibility updates for objects near the new viewpoint rather than their own location.

`ViewPoint` is a lightweight observer embedded in `WorldObject` instances that can serve as camera targets. It maintains a list of attached `Camera` instances. When the `WorldObject` undergoes state changes (added, moved, removed, or visibility changed), `ViewPoint` notifies all attached `Camera` instances via callback methods, allowing the camera to adjust its grid registration and visibility calculations accordingly.

## Member-by-Member Behavior

### Construction and Destruction
*   **Camera**: Initializes the camera with a `Player`, setting both owner and initial source to the player. It attaches the `Camera` to the player's `ViewPoint` via `ViewPoint::Attach`, establishing the default "self-view" state.
*   **~Camera**: Asserts that the current source is the owner (ensuring the view was reset before destruction). It detaches the camera from the owner's `ViewPoint` using `ViewPoint::Detach`.

### View Management
*   **SetView**: Changes the player's perspective to another `WorldObject`. It validates that the object is in the same map and is a valid type (Unit or DynamicObject). It detaches from the old source's `ViewPoint`, manages the "active object" status of both old and new sources (adding/removing from `Map` active lists as needed), and attaches to the new source's `ViewPoint`. If `update_far_sight_field` is true, it schedules a camera update packet and updates visibility of the new source. Finally, it calls `UpdateForCurrentViewPoint`.
*   **ResetView**: Resets the camera's perspective back to the player (`m_owner`) by calling `SetView` with the owner.
*   **GetBody**: Returns the current `WorldObject*` serving as the viewpoint (`m_source`).
*   **GetOwner**: Returns a pointer to the `Player` who owns this camera.

### Visibility and Grid Integration
*   **UpdateForCurrentViewPoint**: Re-registers the `Camera` in the grid system based on the current source's grid. It unlinks the camera from its previous grid reference and adds it to the grid associated with `m_source->GetViewPoint()`. It then triggers `UpdateVisibilityForOwner`.
*   **UpdateVisibilityForOwner**: Calculates which objects are visible to the player from the current viewpoint. It uses a `VisibleNotifier` to visit all objects within the map's visibility distance of the current source. It acquires a shared lock on the owner's visible GUIDs list during notifier setup, then unlocks before the grid visitation to minimize contention.
*   **UpdateVisibilityOf**: Delegates visibility updates for specific targets to the `Player`'s `UpdateVisibilityOf` method, passing the current source (`m_source`) as the observer context.

### Event Handlers (Callbacks from ViewPoint)
These methods are invoked by `ViewPoint::CameraCall` when the observed `WorldObject` changes state.
*   **Event_AddedToWorld**: Triggered when the viewpoint object enters the world. It registers the camera in the viewpoint's grid and updates visibility.
*   **Event_RemovedFromWorld**: Triggered when the viewpoint object leaves the world. If the source is not the owner, it resets the view to the owner. If it is the owner, it just unlinks the grid reference.
*   **Event_Moved**: Triggered when the viewpoint object moves to a new grid cell. It updates the camera's grid reference to match the new location.
*   **Event_ViewPointVisibilityChanged**: Triggered when the viewpoint object's visibility state changes. If the owner can no longer see the source, it resets the view to the owner.

### Packet Handling
*   **ReceivePacket**: Forwards incoming network packets directly to the owner `Player` via `SendDirectMessage`.

### Utility and State
*   **GetGridRef**: Provides access to the internal `GridReference` used for spatial indexing.
*   **IsActiveObject**: Always returns `false`. Cameras are not considered active objects in the map's update loop.

## Cross-Unit Boundaries

### Collaboration with Player.Main
*   **Direction**: Camera -> Player
*   **Purpose**: `Camera` relies on `Player` for network communication and high-level visibility management.
    *   `ReceivePacket` calls `Player::SendDirectMessage`.
    *   `SetView` calls `Player::ScheduleCameraUpdate` and `Player::UpdateVisibilityOf`.
    *   `Event_ViewPointVisibilityChanged` calls `Player::IsInVisibleList`.

### Collaboration with WorldObject.Object
*   **Direction**: Camera <-> WorldObject
*   **Purpose**: `Camera` interacts with `WorldObject` to manage the viewpoint's lifecycle and spatial context.
    *   `Camera` calls `WorldObject::GetViewPoint`, `WorldObject::GetMap`, `WorldObject::IsActiveObject`, and `WorldObject::IsInMap`.
    *   `WorldObject` (via `ViewPoint`) calls back into `Camera` methods (`Event_*`) when the object's state changes.

### Collaboration with Map.Main
*   **Direction**: Camera -> Map
*   **Purpose**: `Camera` manages the "active" status of viewpoints to optimize server performance.
    *   `SetView` calls `Map::AddToActive` and `Map::RemoveFromActive` to ensure viewed objects are processed by the server's update loop.

### Collaboration with GridNotifiers
*   **Direction**: GridNotifiers -> Camera
*   **Purpose**: The grid system uses `Camera` to determine spatial context and ownership for visibility queries.
    *   `GridNotifiers` call `Camera::GetBody` for position/range checks.
    *   `GridNotifiers` call `Camera::GetOwner` to identify the player receiving updates.
    *   `GridNotifiers` call `Camera::UpdateVisibilityOf` to trigger visibility updates for specific objects.

### Collaboration with ViewPoint
*   **Direction**: Camera <-> ViewPoint
*   **Purpose**: Core observer pattern implementation.
    *   `Camera` calls `ViewPoint::Attach` and `ViewPoint::Detach` to subscribe/unsubscribe from viewpoint events.
    *   `ViewPoint` calls `Camera` event handlers (`Event_*`) to notify the camera of changes to the observed object.

## Data Model

This unit does not interact directly with any database tables. All operations are performed in-memory using object references and grid structures.

## Notable Implementation Details

1.  **Active Object Management**: `SetView` manages the `IsActiveObject` status of the viewpoint. Non-player objects being viewed are added to the map's active list to ensure their state is updated, preventing desynchronization. When switching away, the object is removed from the active list if no other cameras are viewing it.
2.  **Thread Safety in Visibility Updates**: `UpdateVisibilityForOwner` uses a `std::shared_lock` on `m_owner->m_visibleGUIDs_lock`. It locks briefly to initialize the `VisibleNotifier` (copying visible GUIDs), then unlocks before the expensive `Cell::VisitAllObjects` operation.
3.  **Default Self-View**: The `Camera` is always initialized to view the `Player` themselves. `ResetView` re-establishes this default. Special views are temporary deviations from this norm.
4.  **ViewPoint Observer Pattern**: `ViewPoint` holds a list of `Camera*` pointers. Its `CameraCall` method iterates through this list and invokes a specified member function on each camera, decoupling `WorldObject` from `Camera` logic.
5.  **Error Handling in SetView**: `SetView` includes assertions and log errors for invalid states (e.g., viewpoint not in map, invalid object type). It returns early in these cases. The destructor assertion `MANGOS_ASSERT(m_source == &m_owner)` ensures developers catch cases where the camera is destroyed while still viewing an external object.
6.  **Grid Reference Unlinking**: Methods like `UpdateForCurrentViewPoint`, `Event_AddedToWorld`, and `Event_Moved` explicitly unlink the camera's `GridReference` before re-adding it to a new grid, preventing double-linking errors in the intrusive list structure.

## Member Reference

**Camera**
Constructor that initializes the camera with a `Player`, setting the owner and initial source to the player, and attaching to the player's `ViewPoint`.

**~Camera**
Destructor that asserts the source is the owner and detaches from the `ViewPoint`.

**ReceivePacket**
Forwards a `WorldPacket` to the owner `Player` via `SendDirectMessage`.

**GetBody**
Returns the current `WorldObject*` serving as the viewpoint (`m_source`).

**GetOwner**
Returns a pointer to the `Player` who owns this camera.

**UpdateForCurrentViewPoint**
Re-registers the camera in the grid based on the current source's grid and triggers `UpdateVisibilityForOwner`.

**SetView**
Changes the camera's perspective to a new `WorldObject`, managing active object status, viewpoint attachment, and visibility updates.

**GetGridRef**
Returns the internal `GridReference` for spatial indexing.

**IsActiveObject**
Always returns `false`; cameras are not active objects in the map update loop.

**Event_ViewPointVisibilityChanged**
Callback triggered when the viewpoint's visibility changes; resets view if the source is no longer visible to the owner.

**ResetView**
Resets the camera's perspective back to the player by calling `SetView` with the owner.

**Event_AddedToWorld**
Callback triggered when the viewpoint object enters the world; registers camera in grid and updates visibility.

**Event_RemovedFromWorld**
Callback triggered when the viewpoint object leaves the world; resets view if source is not owner.

**Event_Moved**
Callback triggered when the viewpoint object moves; updates camera's grid reference.

**UpdateVisibilityOf**
Delegates visibility updates for a target to the owner `Player`, using the current source as the observer context.

**UpdateVisibilityForOwner**
Calculates and updates visibility of objects around the current viewpoint for the owner, using a `VisibleNotifier` and grid visitation.

**~ViewPoint**
Destructor for `ViewPoint` that logs an error if any cameras are still attached, indicating a potential memory leak or logic error.

---

<!-- machine-true, projected from graph.json -->

## Map — Camera

*Source:* Camera.cpp, Camera.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Camera | ctor | ViewPoint/Attach, WorldObject.Object/GetViewPoint | Player.Main/Player#5 | — |
| ~Camera | dtor | Errors/PrintStacktraceAndThrow, ViewPoint/Detach, WorldObject.Object/GetViewPoint | — | — |
| ReceivePacket | method | Player.Main/SendDirectMessage | — | — |
| GetBody | method | — | GridNotifiers/Notify, GridNotifiers/Visit#3, GridNotifiers/Visit#5, Map.Main/UpdateActiveObjectVisibility#2, Map.Main/UpdateActiveObjectVisibility#3, Player.Main/HandleStealthedUnitsDetection | — |
| GetOwner | method | — | GridNotifiers/Notify, GridNotifiers/Visit, GridNotifiers/Visit#2, GridNotifiers/Visit#3, GridNotifiers/Visit#4, GridNotifiers/Visit#5, WorldObject.Object/Visit, WorldObject.Object/Visit#2, WorldObject.Object/Visit#3 | — |
| UpdateForCurrentViewPoint | method | WorldObject.Object/GetViewPoint | — | — |
| SetView | method | Errors/PrintStacktraceAndThrow, Log.Main/Out, Map.Main/AddToActive, Map.Main/RemoveFromActive, Object/GetObjectGuid, Object/IsType, ObjectGuid/ObjectGuid, Player.Main/ScheduleCameraUpdate, Player.Main/UpdateVisibilityOf, ViewPoint/Attach, ViewPoint/Detach, WorldObject.Object/GetMap, WorldObject.Object/GetViewPoint, WorldObject.Object/IsActiveObject, WorldObject.Object/IsInMap | ChatHandler.MiscCommands/HandleSetViewCommand, Player.Main/SetLongSight, Player.Main/SummonPossessedMinion, Player.Main/UpdateCinematic, Spell.Effects/EffectAddFarsight, Unit.SpellAuras/HandleAuraDummy, Unit.SpellAuras/HandleBindSight, Unit.SpellAuras/ModPossess, Unit.SpellAuras/ModPossessPet, WorldSession.MiscHandler/HandleFarSightOpcode | — |
| GetGridRef | method | — | — | — |
| IsActiveObject | method | — | — | — |
| Event_ViewPointVisibilityChanged | method | Player.Main/IsInVisibleList | — | — |
| ResetView | method | — | Player.Main/CinematicEnd, Player.Main/RemoveFromWorld, Player.Main/SetLongSight, Player.Main/UnsummonPossessedMinion, Player.Main/UpdateCinematic, Spell.Main/SendChannelUpdate, Unit.SpellAuras/HandleAuraDummy, Unit.SpellAuras/HandleBindSight, Unit.SpellAuras/ModPossess, Unit.SpellAuras/ModPossessPet, WorldSession.MiscHandler/HandleFarSightOpcode | — |
| Event_AddedToWorld | method | Errors/PrintStacktraceAndThrow, WorldObject.Object/GetViewPoint | — | — |
| Event_RemovedFromWorld | method | — | — | — |
| Event_Moved | method | WorldObject.Object/GetViewPoint | — | — |
| UpdateVisibilityOf | method | Player.Main/UpdateVisibilityOf | GridNotifiers/Visit#6 | — |
| UpdateVisibilityForOwner | method | GridNotifiers/Notify, Map.Main/GetVisibilityDistance, VisibleNotifier/VisibleNotifier, WorldObject.Object/FindMap, WorldObject.Object/GetMap | Map.Main/PlayerRelocation, Player.Main/ResurrectPlayer, Player.Main/SetGameMaster, Unit.SpellAuras/HandleInvisibilityDetect, WorldSession.NPCHandler/SendSpiritResurrect | — |
| ~ViewPoint | dtor | Log.Main/Out | — | — |
