# MessageDistDeliverer

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# MessageDistDeliverer

**Purpose & Responsibilities**

`MessageDistDeliverer` is a visitor functor within the `MaNGOS` namespace, defined in `GridNotifiers.h`. Its specific responsibility is to deliver a `WorldPacket` to all players visible to a source `Player`, subject to strict spatial and social constraints. Unlike general broadcast mechanisms, `MessageDistDeliverer` filters recipients based on:
1.  **Proximity:** The recipient must be within a specified distance (`i_dist`) of the source player.
2.  **Team/Faction Alignment:** If `i_ownTeamOnly` is true, the recipient must be on the same team as the source player.
3.  **Self-Inclusion:** Whether the source player receives the message themselves (`i_toSelf`).

It is designed for scenarios such as local chat channels, proximity-based emotes, or team-specific announcements where visibility alone is insufficient to determine the audience. It operates as part of the grid notification system, iterating over camera maps to identify valid recipients.

**Member-by-Member Behavior**

The unit consists of a single constructor and relies on the `Visit` methods declared in the header (implemented elsewhere, likely in `GridNotifiers.cpp` or similar, though not provided in the source snippet, the behavior is inferred from the data members and standard MaNGOS patterns).

*   **Constructor (`MessageDistDeliverer`)**: Initializes the functor with the necessary context for delivery. It stores references and pointers to the source player, the packet to send, and the filtering criteria.
*   **`Visit(CameraMapType& m)`**: Although the implementation is not in the provided header, the signature indicates this method iterates through the `CameraMapType` (which represents players/cameras in the current grid cell). For each camera/player, it applies the filters defined by the data members (`i_dist`, `i_ownTeamOnly`, `i_toSelf`) and sends `i_message` if the conditions are met. The `template<class SKIP> void Visit(GridRefManager<SKIP>&)` overload is empty, ensuring this functor ignores non-player entities (creatures, game objects, etc.) during grid traversal.

**Cross-Unit Boundaries**

*   **Called By**:
    *   `Map.Main/MessageDistBroadcast`: This indicates that the primary caller is a method within the `Map` class (likely `MessageDistBroadcast` or similar). The `Map` unit orchestrates the grid iteration and instantiates `MessageDistDeliverer` to handle the actual packet sending logic for a specific broadcast request. The `Map` passes the player, packet, distance, and team flags to the constructor.
*   **Calls Out**:
    *   The MAP lists no direct "Calls out" to other units for the constructor. However, the `Visit` method (implied by the class structure) will inevitably call methods on `Player` objects (to check team, distance, and send packets) and potentially `Camera` objects. These interactions are internal to the game object hierarchy and not listed as cross-unit dependencies in the MAP because they are standard object method calls rather than distinct architectural unit boundaries like `Map` or `Database`.

**Data Model**

This unit does not interact with any database tables. It operates entirely on in-memory game objects (`Player`, `WorldPacket`) and spatial data structures (`CameraMapType`).

**Notable Implementation Details**

*   **Const Correctness**: The source player `i_player` is stored as a `const&`, ensuring the functor cannot modify the sender's state during delivery.
*   **Filtering Logic**: The combination of `i_ownTeamOnly` and `i_dist` allows for flexible messaging. For example, a raid leader might send a message to their entire raid group regardless of distance (if `i_ownTeamOnly` is true and distance is large), or a player might shout to nearby enemies (if `i_ownTeamOnly` is false).
*   **Empty Visits for Non-Cameras**: The template specialization `template<class SKIP> void Visit(GridRefManager<SKIP>&) {}` ensures that the functor is lightweight when traversing grids containing creatures or game objects, as it immediately returns without processing them. This is crucial for performance in dense areas.
*   **Dependency on External Implementation**: The actual logic for checking distance and team membership resides in the `Visit(CameraMapType&)` implementation, which is not present in the header. The header only defines the data structure and the interface. Maintainers must look to the corresponding `.cpp` file (likely `GridNotifiers.cpp`) to see how `i_dist` and `i_ownTeamOnly` are evaluated against each `Camera`/`Player` in the map.

## Member Reference

**MessageDistDeliverer**
Constructor that initializes the functor with the source player (`i_player`), the packet to send (`i_message`), a boolean indicating if the sender receives the message (`i_toSelf`), a boolean restricting delivery to the sender's team (`i_ownTeamOnly`), and the maximum distance for delivery (`i_dist`). It is instantiated by `Map.Main/MessageDistBroadcast` to perform filtered packet distribution.

---

<!-- machine-true, projected from graph.json -->

## Map — MessageDistDeliverer

*Source:* GridNotifiers.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| MessageDistDeliverer | ctor | — | Map.Main/MessageDistBroadcast | — |
