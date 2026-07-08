# MovementData

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# MovementData

`MovementData` is a conditional compilation unit (`#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_7_1`) within `UpdateData.h` that provides a lightweight buffering mechanism for movement-related network packets. It aggregates multiple small `WorldPacket` instances into a single internal `ByteBuffer`, allowing the server to batch movement updates before transmitting them to the client. This batching reduces network overhead by minimizing the number of individual TCP segments sent for frequent, small movement events.

The class is designed as a simple container with no complex state management beyond the underlying buffer. It does not perform compression itself but prepares data for potential compression or direct transmission by higher-level session handlers.

## Member-by-Member Behavior

### Construction and Destruction
*   **`MovementData`**: The constructor initializes the internal `m_buffer` with a pre-allocated capacity of 1024 bytes. This avoids immediate heap allocations for small amounts of data.
*   **`~MovementData`**: The destructor is empty. Since `m_buffer` is a stack-allocated object (not a pointer), its resources are automatically reclaimed when the `MovementData` instance goes out of scope.

### Buffer Management
*   **`HasData`**: Returns `true` if the internal `m_buffer` has written any data (i.e., its write position `wpos()` is non-zero). This allows callers to check if there is pending movement data to send before attempting to build or transmit a packet.
*   **`ClearBuffer`**: Resets the internal `m_buffer` by calling `clear()`. This discards all accumulated movement packets, preparing the buffer for a new cycle of aggregation.

### Packet Aggregation (Defined in other partials/files)
While declared in this header, the methods `CanAddPacket`, `AddPacket`, and `BuildPacket` are implemented elsewhere (likely in a corresponding `.cpp` file or another partial). Their roles are:
*   **`CanAddPacket`**: Likely checks if adding a new packet would exceed a size limit or cause fragmentation issues.
*   **`AddPacket`**: Appends a `WorldPacket` to the internal buffer.
*   **`BuildPacket`**: Constructs a final `WorldPacket` from the buffered data, likely wrapping it in a specific opcode structure for the client.

## Cross-Unit Boundaries

`MovementData` interacts primarily with `WorldSession` methods, acting as a data provider for network transmission.

*   **Called by `WorldSession.Main/SendCompressedMovementPackets`**:
    *   **Direction**: `WorldSession` calls `MovementData`.
    *   **Collaboration**: `SendCompressedMovementPackets` uses `HasData()` to determine if there is movement data to process. If data exists, it likely triggers the building and sending of the aggregated movement packet. After sending, it calls `ClearBuffer()` to reset the state for the next frame or update cycle.
*   **Called by `WorldSession.Main/LogoutPlayer`**:
    *   **Direction**: `WorldSession` calls `MovementData`.
    *   **Collaboration**: During player logout, `ClearBuffer()` is called to ensure any pending, unsent movement data is discarded. This prevents stale or irrelevant movement updates from being processed after the player has disconnected.

## Data Model

This unit does not interact with any database tables. It operates entirely in memory, managing transient network packet data.

## Notable Implementation Details

1.  **Conditional Compilation**: The entire `MovementData` class is wrapped in `#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_7_1`. This indicates that movement data batching was introduced or became necessary for client builds newer than 1.7.1. Engineers working with older client versions must ensure they do not reference this class.
2.  **Pre-allocation**: The buffer is initialized with 1024 bytes. This is a heuristic choice to balance memory usage against allocation frequency. If movement updates consistently exceed this size, the `ByteBuffer` will resize dynamically, but the initial allocation covers typical small movement deltas.
3.  **No Compression Logic**: Despite the name `MovementData` and its use in `SendCompressedMovementPackets`, this class does not perform compression. It only buffers raw packet data. The actual compression (if any) is handled by `PacketCompressor` (also defined in `UpdateData.h`) or within the `WorldSession` layer.
4.  **Thread Safety**: There is no explicit locking in `MovementData`. It is assumed that access to a specific `MovementData` instance is confined to a single thread (typically the thread handling the specific `WorldSession`). Concurrent access from multiple threads would lead to race conditions on `m_buffer`.

## Member Reference

**MovementData**
Constructor. Initializes the internal `ByteBuffer` with a capacity of 1024 bytes.

**~MovementData**
Destructor. Empty; relies on automatic cleanup of the `m_buffer` member.

**HasData**
Method. Returns `true` if the internal buffer's write position is non-zero, indicating pending movement data. Called by `WorldSession.Main/SendCompressedMovementPackets`.

**ClearBuffer**
Method. Clears the internal buffer, discarding all accumulated data. Called by `WorldSession.Main/LogoutPlayer` and `WorldSession.Main/SendCompressedMovementPackets`.

---

<!-- machine-true, projected from graph.json -->

## Map — MovementData

*Source:* UpdateData.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| MovementData | ctor | — | — | — |
| ~MovementData | dtor | — | — | — |
| HasData | method | — | WorldSession.Main/SendCompressedMovementPackets | — |
| ClearBuffer | method | — | WorldSession.Main/LogoutPlayer, WorldSession.Main/SendCompressedMovementPackets | — |
