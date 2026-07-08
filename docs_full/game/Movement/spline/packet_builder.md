<!-- provenance: verbose -->
# packet_builder

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# packet_builder

## Purpose & Responsibilities

The `packet_builder` unit (`packet_builder.cpp`, `packet_builder.h`) serializes internal movement spline data (`MoveSpline`) into binary network packets for the World of Warcraft client. It resides in the `Movement` namespace and provides free helper functions for vector/path encoding and static methods in the `PacketBuilder` class for assembling complete movement update and creation packets.

It handles two distinct serialization contexts:
1.  **Movement Updates** (`WriteMonsterMove`): Incremental updates sent during movement, supporting path fragmentation and dynamic duration patching.
2.  **Object Creation** (`WriteCreate`): Full state transmission when an object spawns or enters view.

The unit implements specific client-side workarounds, such as perturbing near-zero offsets to prevent client freezes and injecting artificial flags (`Runmode`, `Enter_Cycle`) to ensure correct client behavior. It does not perform movement calculations; it strictly encodes existing `MoveSpline` state.

## Member-by-Member Behavior

### Vector Serialization Helpers

*   **`operator<<`**: Inline function serializing a `Vector3` into a `ByteBuffer` by writing `x`, `y`, and `z` components.
*   **`operator>>`**: Inline function deserializing a `Vector3` from a `ByteBuffer` by reading `x`, `y`, and `z` components.

### Path Encoding Functions

These free functions encode the geometric waypoints of a spline into a `ByteBuffer`.

*   **`WriteLinearPath`**: Encodes a linear spline segment.
    *   Retrieves `CONFIG_UINT32_MAX_POINTS_PER_MVT_PACKET` from `World` to limit points per packet.
    *   Writes the count of points and the final destination vector.
    *   Iterates through intermediate points, calculating the offset from the destination.
    *   **Client Workaround**: If an offset is near-zero (all components `< 0.25`), it perturbs the `z` component (`+0.51f` or `+0.26f`) to prevent client freezing.
    *   Returns the index of the last written point, enabling chunked transmission.
*   **`WriteCatmullRomPath`**: Encodes a non-cyclic Catmull-Rom spline. Writes the point count (excluding first 3 control points) and appends the raw `Vector3` array starting from index 2.
*   **`WriteCatmullRomCyclicPath`**: Encodes a cyclic Catmull-Rom spline. Prepends a "fake" point (index 1) to the output, which the client discards after the first cycle. Appends the remaining points starting from index 1.

### PacketBuilder Class Methods

*   **`WriteCommonMonsterMovePart`**: Private helper writing the common header for movement updates.
    *   Writes the current position (`spline.first()`) and movement ID.
    *   Determines facing type (`Target`, `Angle`, `Point`, or `Normal`) based on `MoveSplineFlag` masks and writes the corresponding data.
    *   Injects artificial flags: sets `enter_cycle` if cyclic, and forces `Runmode` to avoid client issues.
    *   Writes the masked spline flags and a placeholder duration.
*   **`WriteMonsterMove`**: Orchestrates serialization for movement updates.
    *   Calls `WriteCommonMonsterMovePart`.
    *   Records the buffer position of the duration placeholder.
    *   Branches by spline type:
        *   **Catmull-Rom**: Calls `WriteCatmullRomPath` or `WriteCatmullRomCyclicPath`. Returns `-1`.
        *   **Linear**: Calls `WriteLinearPath`. Recalculates duration for the written segment and patches the placeholder with `duration - time_passed`. Returns the last written node index.
*   **`WriteCreate`**: Serializes full movement state for object creation.
    *   Checks `Initialized()` status.
    *   Writes raw spline flags, facing data (if applicable), `timePassed`, and `Duration`.
    *   Conditionally writes the movement ID for clients newer than `CLIENT_BUILD_1_7_1`.
    *   Writes the total node count and appends the entire path array.
    *   Writes the final destination (or zero vector if cyclic).

## Cross-Unit Boundaries

*   **Calls Out To**:
    *   **`ByteBuffer` / `WorldPacket`**: Core serialization targets (`operator<<`, `appendPackXYZ`, `wpos`, `put`, `append`).
    *   **`MoveSpline`**: Source of truth for movement state (`Duration`, `GetId`, `isCyclic`, `FinalDestination`, `getPath`, `Initialized`, `timePassed`, `CountSplinePoints`).
    *   **`MoveSplineFlag`**: Bitwise flag manipulation (`raw`, `operator&`, constructors).
    *   **`Spline`**: Geometric data access (`getPoint`, `getPointCount`, `first`).
    *   **`World`**: Configuration retrieval (`getConfig` for `CONFIG_UINT32_MAX_POINTS_PER_MVT_PACKET`).
*   **Called By**:
    *   **`MoveSplineInit/Launch`**: Invokes `WriteMonsterMove` when launching movement.
    *   **`Unit.Main/UpdateSplineMovement`**: Invokes `WriteMonsterMove` during periodic updates.
    *   **`WorldObject.Object/BuildMovementUpdate`**: Invokes `WriteCreate` for initial object state.

## Data Model

This unit does not interact with any database tables. It operates exclusively on in-memory movement structures and network buffers.

## Notable Implementation Details

1.  **Zero-Offset Perturbation**: `WriteLinearPath` modifies `z` offsets if `x`, `y`, and `z` differences are all `< 0.25`. This prevents a known client freeze bug.
2.  **Artificial Flags**: `WriteCommonMonsterMovePart` forces `Runmode` and `enter_cycle` flags regardless of their original state to satisfy client expectations.
3.  **Duration Patching**: `WriteMonsterMove` writes a dummy duration, then overwrites it after calculating the precise time for the serialized segment (linear only).
4.  **Version Compatibility**: `WriteCreate` omits the movement ID for clients `<= CLIENT_BUILD_1_7_1`.
5.  **Catmull-Rom Control Points**: `WriteCatmullRomPath` skips the first two points of the spline array, as they serve as control points for curvature rather than direct path vertices in the packet format.

## Member Reference

*   **`operator<<`**: Inline function serializing a `Vector3` into a `ByteBuffer` by writing its x, y, and z components.
*   **`operator>>`**: Inline function deserializing a `Vector3` from a `ByteBuffer` by reading its x, y, and z components.
*   **`WriteCommonMonsterMovePart`**: Private static method of `PacketBuilder` that writes the common header for monster movement packets, including position, ID, facing data, and manipulated spline flags (adding fake `Enter_Cycle` and `Runmode` flags).
*   **`WriteLinearPath`**: Free function that encodes a linear spline path into a `ByteBuffer`, handling packet size limits and applying a workaround to prevent client freezes on near-zero offsets.
*   **`WriteCatmullRomPath`**: Free function that encodes a non-cyclic Catmull-Rom spline path, skipping the first two control points.
*   **`WriteCatmullRomCyclicPath`**: Free function that encodes a cyclic Catmull-Rom spline path, prepending a fake point for client-side cycle initialization.
*   **`WriteMonsterMove`**: Public static method of `PacketBuilder` that orchestrates the serialization of a movement update, choosing between linear or Catmull-Rom path encoding and patching the duration field.
*   **`WriteCreate`**: Public static method of `PacketBuilder` that serializes the full movement state for object creation, including flags, facing, timing, and the complete path array.

---

<!-- machine-true, projected from graph.json -->

## Map — packet_builder

*Source:* packet_builder.cpp, packet_builder.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| operator<< | function | ByteBuffer/operator<<#9 | — | — |
| operator>> | function | ByteBuffer/operator>>#8 | — | — |
| WriteCommonMonsterMovePart | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#11, ByteBuffer/operator<<#4, ByteBuffer/operator<<#7, ByteBuffer/operator<<#9, MoveSpline/Duration, MoveSpline/GetId, MoveSpline/isCyclic, MoveSplineFlag/MoveSplineFlag#2, MoveSplineFlag/operator&, spline/first, spline/getPoint | — | — |
| WriteLinearPath | function | ByteBuffer/appendPackXYZ, ByteBuffer/operator<<#10, spline/getPoint, spline/getPointCount, World/getConfig#4 | — | — |
| WriteCatmullRomPath | function | ByteBuffer/operator<<#10, spline/getPoint, spline/getPointCount | — | — |
| WriteCatmullRomCyclicPath | function | ByteBuffer/operator<<#10, spline/getPoint, spline/getPointCount | — | — |
| WriteMonsterMove | method | ByteBuffer/wpos, MoveSpline/CountSplinePoints, MoveSpline/Duration#2, MoveSplineFlag/MoveSplineFlag#2, MoveSplineFlag/operator& | MoveSplineInit/Launch, Unit.Main/UpdateSplineMovement | — |
| WriteCreate | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#11, ByteBuffer/operator<<#4, ByteBuffer/operator<<#9, MoveSpline/Duration, MoveSpline/FinalDestination, MoveSpline/GetId, MoveSpline/getPath, MoveSpline/Initialized, MoveSpline/isCyclic, MoveSpline/timePassed, MoveSplineFlag/MoveSplineFlag#2, MoveSplineFlag/raw | WorldObject.Object/BuildMovementUpdate | — |
