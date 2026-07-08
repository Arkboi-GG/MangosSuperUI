# ViewPoint

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ViewPoint

**ViewPoint** is a lightweight observer-list manager defined in `Camera.h`. It maintains a list of `Camera` instances observing a specific world object. Its responsibility is to propagate lifecycle and visibility events from the observed object to all attached `Camera` instances via the `CameraCall` mechanism.

## Member-by-Member Behavior

### Observer Registration
*   **`Attach`**: Adds a `Camera` to `m_cameras`. Called by `Camera::SetView` when a player starts viewing an object.
*   **`Detach`**: Removes a `Camera` from `m_cameras`. Called by `Camera::SetView` when switching views or by `Camera::~Camera` during destruction.

### Event Propagation
Each event method updates internal state (specifically `m_grid`) and delegates to `CameraCall` to notify attached cameras.

*   **`Event_AddedToWorld`**: Sets `m_grid` and notifies cameras via `Camera::Event_AddedToWorld`. Triggered when the observed object enters the world.
*   **`Event_RemovedFromWorld`**: Clears `m_grid` and notifies cameras via `Camera::Event_RemovedFromWorld`. Triggered when the object leaves the world.
*   **`Event_GridChanged`**: Updates `m_grid` and notifies cameras via `Camera::Event_Moved`. Triggered when the object moves between grids.
*   **`Event_ViewPointVisibilityChanged`**: Notifies cameras via `Camera::Event_ViewPointVisibilityChanged`. Triggered by visibility state changes.
*   **`Call_UpdateVisibilityForOwner`**: Notifies cameras via `Camera::UpdateVisibilityForOwner`. Forces visibility updates for the camera's owner.

### Internal Dispatch
*   **`CameraCall`**: Iterates `m_cameras` and invokes the specified `Camera` member function. It increments the iterator before dereferencing (`*(itr++)`) to safely handle cases where a callback modifies the list (e.g., self-detachment).
*   **`hasViewers`**: Returns `true` if `m_cameras` is not empty.
*   **`ViewPoint`**: Constructor initializing `m_grid` to `nullptr`.

## Cross-Unit Boundaries

### Calls Out
*   **`Camera`**: `ViewPoint` calls `Camera` methods via `CameraCall`: `Event_AddedToWorld`, `Event_RemovedFromWorld`, `Event_Moved` (via `Event_GridChanged`), `Event_ViewPointVisibilityChanged`, and `UpdateVisibilityForOwner`.

### Called By
*   **`Map.Main`**: `Add#3` and `ExistingPlayerLogin` call `Event_AddedToWorld`. `CreatureCellRelocation`, `DoPlayerGridRelocation`, and `PlayerRelocation` call `Event_GridChanged`.
*   **`DynamicObject` / `Unit.Main`**: `RemoveFromWorld` calls `Event_RemovedFromWorld`.
*   **`Unit.Main` / `WorldObject.Object`**: `UpdateVisibilityAndView` calls `Event_ViewPointVisibilityChanged`. `ProcessRelocationVisibilityUpdates` and `UpdateVisibilityAndView` call `Call_UpdateVisibilityForOwner`.

## Data Model
`ViewPoint` does not interact with any database tables.

## Notable Implementation Details
1.  **Iterator Safety**: `CameraCall` uses `*(itr++)` to advance the iterator before processing the element. This prevents crashes if a callback removes the current element from `m_cameras`, as the iterator remains valid for the next iteration.
2.  **Grid Tracking**: `m_grid` stores the current `GridType*` of the view point, updated on world entry and grid changes, enabling spatial queries.

## Member Reference

*   **`Attach`**: Adds a `Camera` to `m_cameras`. Called by `Camera::SetView`.
*   **`Detach`**: Removes a `Camera` from `m_cameras`. Called by `Camera::SetView` and `Camera::~Camera`.
*   **`CameraCall`**: Iterates `m_cameras` and invokes the specified `Camera` member function. Uses `itr++` for safe iteration during potential list modifications.
*   **`ViewPoint`**: Constructor. Initializes `m_grid` to `nullptr`.
*   **`hasViewers`**: Returns `true` if `m_cameras` is not empty.
*   **`Event_AddedToWorld`**: Sets `m_grid` and notifies cameras via `Camera::Event_AddedToWorld`. Called by `Map.Main::Add#3` and `Map.Main::ExistingPlayerLogin`.
*   **`Event_RemovedFromWorld`**: Clears `m_grid` and notifies cameras via `Camera::Event_RemovedFromWorld`. Called by `DynamicObject::RemoveFromWorld` and `Unit.Main::RemoveFromWorld`.
*   **`Event_GridChanged`**: Updates `m_grid` and notifies cameras via `Camera::Event_Moved`. Called by `Map.Main::CreatureCellRelocation`, `Map.Main::DoPlayerGridRelocation`, and `Map.Main::PlayerRelocation`.
*   **`Event_ViewPointVisibilityChanged`**: Notifies cameras via `Camera::Event_ViewPointVisibilityChanged`. Called by `Unit.Main::UpdateVisibilityAndView` and `WorldObject.Object::UpdateVisibilityAndView`.
*   **`Call_UpdateVisibilityForOwner`**: Notifies cameras via `Camera::UpdateVisibilityForOwner`. Called by `Unit.Main::ProcessRelocationVisibilityUpdates`, `Unit.Main::UpdateVisibilityAndView`, and `WorldObject.Object::UpdateVisibilityAndView`.

---

<!-- machine-true, projected from graph.json -->

## Map — ViewPoint

*Source:* Camera.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Attach | method | — | Camera/Camera, Camera/SetView | — |
| Detach | method | — | Camera/SetView, Camera/~Camera | — |
| CameraCall | method | — | — | — |
| ViewPoint | ctor | — | — | — |
| hasViewers | method | — | — | — |
| Event_AddedToWorld | method | — | Map.Main/Add#3, Map.Main/ExistingPlayerLogin | — |
| Event_RemovedFromWorld | method | — | DynamicObject/RemoveFromWorld, Unit.Main/RemoveFromWorld | — |
| Event_GridChanged | method | — | Map.Main/CreatureCellRelocation, Map.Main/DoPlayerGridRelocation, Map.Main/PlayerRelocation | — |
| Event_ViewPointVisibilityChanged | method | — | Unit.Main/UpdateVisibilityAndView, WorldObject.Object/UpdateVisibilityAndView | — |
| Call_UpdateVisibilityForOwner | method | — | Unit.Main/ProcessRelocationVisibilityUpdates, Unit.Main/UpdateVisibilityAndView, WorldObject.Object/UpdateVisibilityAndView | — |
