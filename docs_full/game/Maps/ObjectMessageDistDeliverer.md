# ObjectMessageDistDeliverer

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ObjectMessageDistDeliverer

**Purpose & Responsibilities**

`ObjectMessageDistDeliverer` is a lightweight visitor struct within the `MaNGOS` namespace, defined in `GridNotifiers.h`. Its sole responsibility is to facilitate the delivery of a network message (`WorldPacket`) to all players currently visible to a specific `WorldObject`, but only if those players are within a specified physical distance from that object.

It implements the Visitor pattern required by the MaNGOS grid system. The grid system iterates over spatial partitions (grids/cells) containing objects. To perform an action on objects within a specific grid type (in this case, cameras representing players), the grid infrastructure calls a `Visit` method on a provided visitor object. `ObjectMessageDistDeliverer` provides this interface, allowing the caller to pass a message and a distance threshold, after which the grid system handles the iteration and filtering logic.

Unlike `MessageDistDeliverer`, which is tied to a specific `Player` sender and includes team-based filtering, `ObjectMessageDistDeliverer` is generic: it originates from any `WorldObject` (which could be a Creature, GameObject, or Player) and sends to any visible player within range, regardless of team affiliation or sender identity.

## Member-by-Member Behavior

### Constructor: `ObjectMessageDistDeliverer`

The constructor initializes the visitor with the necessary context for message delivery.

*   **Parameters:**
    *   `WorldObject const& obj`: The source object from which the distance is measured. This object acts as the anchor for the visibility and range check.
    *   `WorldPacket* msg`: The network packet to be sent to eligible receivers. The visitor holds a raw pointer, implying the caller manages the packet's lifetime during the visitation process.
    *   `float dist`: The maximum distance (in game units) from `obj` within which a player must reside to receive the message.

*   **Initialization:**
    *   Stores references/pointers to `obj`, `msg`, and `dist` in the corresponding member variables (`i_object`, `i_message`, `i_dist`).
    *   No complex logic or validation occurs in the constructor; it is purely data aggregation for the subsequent `Visit` call.

### Method: `Visit(CameraMapType& m)`

This method is the core implementation of the Visitor pattern for this unit. It is invoked by the grid system when iterating over the `CameraMapType`, which represents the set of active player cameras (viewpoints) within a specific grid cell.

*   **Logic:**
    1.  The method iterates through the provided `CameraMapType` container `m`.
    2.  For each entry, it retrieves the `Camera` object.
    3.  It obtains the `Player` associated with that camera via `camera->GetOwner()`.
    4.  It performs a distance check: `i_object.IsWithinDist(camera->GetOwner(), i_dist)`.
        *   Note: The code uses `IsWithinDist`, which typically calculates the 2D distance (X/Y plane) unless otherwise specified by the overload resolution in the base class. However, looking at similar structures in `GridNotifiers.h` like `CameraDistWorker`, it explicitly uses `IsWithinDist`. In MaNGOS, `IsWithinDist` usually defaults to 2D distance for many gameplay mechanics, but `IsWithinDistInMap` is often preferred for strict spatial checks. The specific behavior depends on the `WorldObject::IsWithinDist` implementation, but the intent is clearly a proximity filter.
    5.  If the player is within the specified distance, the message is sent: `player->SendDirectMessage(i_message)`.
    6.  If the player is outside the range, no action is taken.

*   **Note on Visibility:** The fact that this visitor is applied to `CameraMapType` implies that the players represented here are already considered "visible" or "relevant" to the grid cell being processed. The grid system typically ensures that only objects within a certain visibility radius are included in these maps. Therefore, `ObjectMessageDistDeliverer` adds a secondary, stricter distance filter on top of the grid's coarse visibility culling.

### Method: `Visit(GridRefManager<SKIP>&)`

This is a template method that serves as a no-op for all other grid manager types (e.g., `CreatureMapType`, `GameObjectMapType`, etc.).

*   **Behavior:** It does nothing.
*   **Purpose:** This satisfies the Visitor interface requirement that a visitor must be callable on all grid types. Since `ObjectMessageDistDeliverer` is only interested in sending messages to players (represented by Cameras), it ignores all other object types.

## Cross-Unit Boundaries

### Called By: `Map.Main/MessageDistBroadcast#2`

*   **Direction:** Incoming call.
*   **Collaboration:** The `Map` unit (specifically the `MessageDistBroadcast` functionality, likely part of a broader broadcast mechanism) creates an instance of `ObjectMessageDistDeliverer`.
*   **Context:** When a `WorldObject` needs to broadcast a message to nearby players, the `Map` unit determines which grid cells contain relevant players. It then instantiates `ObjectMessageDistDeliverer` with the source object, the packet, and the desired range. The `Map` unit then invokes the grid iteration logic, which in turn calls `ObjectMessageDistDeliverer::Visit(CameraMapType&)` for each relevant grid cell.
*   **Data Crossing Boundary:**
    *   **In:** The `WorldObject` reference, the `WorldPacket` pointer, and the `float` distance are passed from `Map` to `ObjectMessageDistDeliverer`.
    *   **Out:** None directly. The side effect is the transmission of the packet to players, which is handled internally by the `Player` unit via `SendDirectMessage`.

### Calls Out: `Player.SendDirectMessage`

*   **Direction:** Outgoing call.
*   **Collaboration:** Inside `Visit(CameraMapType&)`, after verifying the distance, the visitor calls `SendDirectMessage` on the `Player` object obtained from the camera.
*   **Purpose:** To actually transmit the network data to the client.
*   **Data Crossing Boundary:** The `WorldPacket*` is passed to the `Player` unit.

### Calls Out: `WorldObject.IsWithinDist`

*   **Direction:** Outgoing call.
*   **Collaboration:** The visitor calls `IsWithinDist` on the stored `i_object` reference, passing the target `Player` and the stored `i_dist`.
*   **Purpose:** To determine if the target player is close enough to receive the message.
*   **Data Crossing Boundary:** The target `Player` pointer and the distance threshold are passed to the `WorldObject` unit for calculation.

## Data Model

This unit does not interact with any database tables. It operates entirely on in-memory game state objects (`WorldObject`, `Player`, `Camera`) and network packets.

## Notable Implementation Details

1.  **Raw Pointer Usage:** The `i_message` member is a raw `WorldPacket*`. The `ObjectMessageDistDeliverer` does not take ownership of the packet. The caller (likely in `Map`) must ensure the packet remains valid for the duration of the grid iteration. If the packet were deleted prematurely, this would result in undefined behavior. This is a common pattern in MaNGOS for performance, avoiding deep copies of large packet buffers.

2.  **Distance Calculation Nuance:** The use of `IsWithinDist` rather than `IsWithinDistInMap` or `IsWithinDist3d` is significant. In many MaNGOS versions, `IsWithinDist` calculates the 2D Euclidean distance on the X-Y plane, ignoring Z (height). This means a player directly above or below the source object might receive the message if they are horizontally close, even if vertically distant. If vertical separation is intended to block the message, `IsWithinDist3d` should be used. Maintainers should verify if this 2D-only check is the desired behavior for the specific broadcast use case.

3.  **Visibility Pre-filtering:** This visitor relies on the grid system to pre-filter players into `CameraMapType`. It assumes that any player in this map is potentially visible. The distance check is a secondary filter. This is efficient because it avoids checking distance against players who are already known to be far away (outside the grid cell's visibility radius). However, it also means that if the grid's visibility radius is larger than `i_dist`, the distance check is necessary. If `i_dist` is larger than the grid's visibility radius, some players might be missed if they are in adjacent grids not yet processed or not included in the current `CameraMapType` iteration scope. The `Map` unit is responsible for ensuring the correct grid cells are visited.

4.  **No Team/Faction Filtering:** Unlike `MessageDistDeliverer`, this struct does not check for team alignment (`i_ownTeamOnly`). It sends to anyone within range. This makes it suitable for neutral broadcasts (e.g., environmental sounds, global announcements from a specific point, or non-combat notifications) but inappropriate for team-specific communications.

5.  **Const Correctness:** The `i_object` is stored as a `const` reference, ensuring the source object cannot be modified during the visitation. The `i_message` is a mutable pointer, allowing the packet to be sent multiple times (once per eligible player).

## Member Reference

**ObjectMessageDistDeliverer**
Constructor that initializes the visitor with a source `WorldObject`, a `WorldPacket` to send, and a maximum distance `float`. It stores these values in member variables for use during the `Visit` method.

---

<!-- machine-true, projected from graph.json -->

## Map — ObjectMessageDistDeliverer

*Source:* GridNotifiers.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ObjectMessageDistDeliverer | ctor | — | Map.Main/MessageDistBroadcast#2 | — |
