# VisibleNotifier

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# VisibleNotifier

**Purpose & Responsibilities**

`MaNGOS::VisibleNotifier` is a visitor functor used within the MaNGOS grid-based spatial partitioning system to determine which `WorldObject`s are currently visible to a specific client (player). It is part of the visibility update pipeline, responsible for iterating over objects in a specific grid cell (`GridRefManager`) and generating the necessary network updates (`UpdateData`) to inform the client about these objects.

The notifier holds a reference to a `Camera` (representing the viewer's perspective and position), an `UpdateData` buffer to accumulate changes, and a reference to the client's set of known GUIDs (`i_clientGUIDs`). Its primary role is to inspect objects in a grid, decide if they should be sent to the client, and populate the update data accordingly.

**Member-by-Member Behavior**

*   **`VisibleNotifier` (Constructor)**: Initializes the notifier with a reference to a `Camera`. It stores the camera, initializes an empty `UpdateData` object, and binds `i_clientGUIDs` to the `m_visibleGUIDs` set of the camera's owner (typically a `Player`). This binding allows the notifier to track which objects the client already knows about, facilitating incremental updates rather than full resends.
*   **`Visit` (Method)**: This is the core execution point invoked by the grid traversal system. The provided source shows two overloads:
    *   `template<class T> void Visit(GridRefManager<T>& m)`: This template method is intended to process objects stored in a grid cell. However, the implementation body is **not present** in the provided `GridNotifiers.h` source snippet. Based on standard MaNGOS patterns and the presence of `i_data` and `i_camera`, this method would typically iterate through the objects in `m`, check their visibility against `i_camera`, and add them to `i_data` if they are new or updated. The absence of the body in this header suggests the implementation is likely in a corresponding `.cpp` file or defined elsewhere, but the interface is declared here.
    *   `void Visit(CameraMapType&)`: This overload is explicitly defined as an empty function `{}`. This indicates that `VisibleNotifier` does not perform any action when visiting a container of `Camera` objects. This is logical because a visibility update for a player does not need to process other cameras; it processes world objects relative to the player's camera.

**Cross-Unit Boundaries**

*   **Called by**: `Camera/UpdateVisibilityForOwner`.
    *   **Direction**: Inbound.
    *   **Collaboration**: The `Camera` class (specifically its `UpdateVisibilityForOwner` method) creates an instance of `VisibleNotifier` and passes it to the grid traversal mechanism. The grid system then calls `VisibleNotifier::Visit` for each relevant grid cell. After traversal, the `Camera` likely retrieves the populated `i_data` from the notifier and sends it to the client. This establishes `VisibleNotifier` as a passive worker object driven by the `Camera`'s visibility update cycle.
*   **Calls out**: None listed in the MAP.
    *   While the *implementation* of `Visit` (if it were visible) would likely call methods on `WorldObject`, `Camera`, and `UpdateData`, the MAP indicates no direct cross-unit dependencies are tracked for this specific unit's declared members in this context. The constructor accesses `Camera::GetOwner()` and `Player::m_visibleGUIDs`, implying dependencies on `Camera` and `Player` structures, but these are initialization steps rather than runtime calls tracked in the MAP.

**Data Model**

This unit does not interact directly with any database tables. It operates entirely on in-memory game state objects (`Camera`, `WorldObject`, `UpdateData`).

**Notable Implementation Details**

1.  **Empty `Visit(CameraMapType&)`**: The explicit empty definition for `Visit(CameraMapType&)` is a deliberate optimization. It prevents unnecessary processing of camera containers during visibility sweeps, reinforcing that visibility calculations are object-centric, not camera-centric (from the server's perspective, the camera is just the viewer).
2.  **Reference Binding in Constructor**: The constructor binds `i_clientGUIDs` directly to `c.GetOwner()->m_visibleGUIDs`. This avoids copying the GUID set and ensures that any modifications to the client's visible set during the visit (if the notifier were to modify it, though typically it just reads) would reflect immediately. More importantly, it allows the visibility logic to efficiently check if an object is already known to the client.
3.  **Missing Template Implementation**: The template `Visit(GridRefManager<T>&)` is declared but not defined in this header. In C++, templates usually require definition at the point of instantiation. This suggests that either:
    *   The implementation is in a separate `.cpp` file (which would require explicit template instantiations, hinted at by the `#ifndef WIN32` block at the bottom of the file for other notifiers).
    *   Or the implementation is in a different header included elsewhere.
    *   *Note*: The `#ifndef WIN32` block at the end of the file lists explicit instantiations for other notifiers (like `PlayerRelocationNotifier`), but **not** for `VisibleNotifier`. This might imply that `VisibleNotifier`'s template implementation is handled differently or is indeed missing from this specific snippet, relying on implicit instantiation or a different compilation unit. A maintainer must verify where `VisibleNotifier::Visit` is actually implemented to understand its logic.

## Member Reference

**VisibleNotifier**
Constructor that initializes the notifier with a `Camera` reference. It sets up the `i_camera` member, creates a new `UpdateData` object for accumulating updates, and binds `i_clientGUIDs` to the owner's visible GUID set to enable efficient visibility tracking.

**Visit**
Two overloads are declared:
1.  `template<class T> void Visit(GridRefManager<T>& m)`: Declared but not defined in this header. Intended to iterate over objects in a grid cell and update visibility data.
2.  `void Visit(CameraMapType&)`: Defined as an empty function. No action is taken when visiting camera containers, as they are irrelevant to object visibility updates.

---

<!-- machine-true, projected from graph.json -->

## Map — VisibleNotifier

*Source:* GridNotifiers.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| VisibleNotifier | ctor | — | Camera/UpdateVisibilityForOwner | — |
| Visit | method | — | — | — |
