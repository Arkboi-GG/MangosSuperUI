<!-- provenance: verbose -->
# UpdateData

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# UpdateData

## Purpose & Responsibilities

`UpdateData` aggregates object state changes (values, movement, creation/destruction) and visibility updates into batches before transmitting them to a client via `WorldSession`. It reduces network overhead by serializing multiple "update blocks" into single `WorldPacket` structures, applying zlib compression if the payload exceeds a configured threshold.

The unit also manages `m_outOfRangeGUIDs`, a set of object identifiers that have moved out of the player's visibility range. These GUIDs are serialized into a specific update type (`UPDATETYPE_OUT_OF_RANGE_OBJECTS`) to instruct the client to remove those objects from its local scene.

Two supporting structures are defined in this unit:
1.  **`PacketCompressor`**: A static utility wrapper around the zlib library for compressing raw byte buffers.
2.  **`MovementData`** (conditional on client build > 1.7.1): A specialized buffer for batching movement packets (`SMSG_COMPRESSED_MOVES`), which have a distinct structure from general object updates.

This unit does not interact with any database tables.

## Member-by-Member Behavior

### Aggregation and Buffer Management

**`UpdateData` (ctor)**
Initializes an empty `UpdateData` instance. Internal containers (`m_datas`, `m_outOfRangeGUIDs`) are default-initialized.

**`~UpdateData` (dtor)**
Calls `Clear()` to release resources held by the internal `std::list<UpdatePacket>` and `ObjectGuidSet`.

**`AddOutOfRangeGUID` (overload 1)**
Inserts all GUIDs from an `ObjectGuidSet` into `m_outOfRangeGUIDs`. Called by `GridNotifiers/Notify`.

**`AddOutOfRangeGUID#2` (overload 2)**
Inserts a single `ObjectGuid` into `m_outOfRangeGUIDs`. Called by `WorldObject.Object/BuildOutOfRangeUpdateBlock`.

**`AddUpdateBlockAndGetBuffer`**
Returns a reference to a `ByteBuffer` where callers (e.g., `WorldObject.Object/BuildCreateUpdateBlockForPlayer`) write serialized update fields.
*   **Logic:** If `m_datas` is empty, it creates a new `UpdatePacket`. If the last packet's write position (`wpos()`) exceeds `MAX_UNCOMPRESSED_PACKET_SIZE` (32KB), it creates a new `UpdatePacket` to prevent oversized uncompressed chunks.
*   **Side Effect:** Increments `blockCount` on the active `UpdatePacket`, which the client uses to parse the number of update blocks.

**`Clear`**
Empties both `m_datas` and `m_outOfRangeGUIDs`, resetting the object for reuse or destruction.

**`HasData`**
Returns `true` if `m_datas` is not empty or `m_outOfRangeGUIDs` is not empty. Used by callers like `Map.Main/UpdateActiveObjectVisibility` to determine if a packet needs construction.

**`GetOutOfRangeGUIDs`**
Provides read-only access to `m_outOfRangeGUIDs`. Used by `GridNotifiers/Notify`.

### Packet Construction and Compression

**`BuildPacket#3` (overload 1)**
Convenience overload forwarding to `BuildPacket#2`. If `m_datas` is empty, it passes `nullptr` for the update packet pointer; otherwise, it passes a pointer to the first element of `m_datas`.

**`BuildPacket#2` (overload 2)**
Constructs the final `WorldPacket` from aggregated data.
1.  **Header:** Calculates size for block count, transport flag, and out-of-range GUIDs.
2.  **Out-of-Range Block:** If `m_outOfRangeGUIDs` is not empty, writes `UPDATETYPE_OUT_OF_RANGE_OBJECTS`, the GUID count, and serializes each GUID. For clients > 1.8.4, GUIDs use `WriteAsPacked()`; older clients use standard serialization.
3.  **Update Blocks:** If an `UpdatePacket` pointer is provided, appends its raw bytes.
4.  **Compression:** If total size exceeds `CONFIG_UINT32_COMPRESSION_UPDATE_SIZE`, it invokes `PacketCompressor::Compress`, sets opcode to `SMSG_COMPRESSED_UPDATE_OBJECT`, and prepends uncompressed size. Otherwise, it appends raw data and sets opcode to `SMSG_UPDATE_OBJECT`.
5.  **Safety:** Logs a `[CRASH-CLIENT]` warning if uncompressed size >= 900,000 bytes.

**`Compress` (static, `PacketCompressor`)**
Wraps zlib `deflate`. Initializes a `z_stream` with compression level from `sWorld.getConfig(CONFIG_UINT32_COMPRESSION_LEVEL)`. Performs `Z_NO_FLUSH` followed by `Z_FINISH`. Validates input consumption and stream termination. On error, logs the zlib error code and sets destination size to 0.

**`Send`**
Iterates through `m_datas` and sends each chunk as a separate packet via `WorldSession.Main/SendPacket`.
*   **Special Case:** If `m_datas` is empty but `m_outOfRangeGUIDs` is not, it builds and sends a single packet containing only out-of-range info.
*   **Loop:** For each `UpdatePacket`, it calls `BuildPacket#2`. Crucially, `m_outOfRangeGUIDs.clear()` is called inside the loop after the first packet is built. This means out-of-range GUIDs are only included in the *first* packet of a multi-chunk send. Subsequent chunks in the same `Send` call will not contain out-of-range notifications.

### Movement Data (Client Build > 1.7.1)

**`CanAddPacket`**
Checks if a new movement packet fits in the `MovementData` buffer. Fails if the packet size + 2 bytes exceeds 255 (`uint8` limit) or if the total buffer exceeds 900,000 bytes.

**`AddPacket`**
Appends a movement packet to `m_buffer`, prefixed with its size (`uint8`) and opcode (`uint16`). Asserts that packet size + 2 <= 255 to prevent client crashes.

**`BuildPacket` (MovementData)**
Compresses the accumulated `m_buffer` using `PacketCompressor::Compress`, prepends uncompressed size, and sets opcode to `SMSG_COMPRESSED_MOVES`. Logs a crash warning if size >= 900,000 bytes.

## Cross-Unit Boundaries

### Incoming Calls
*   **`Map.Main`**: Constructs `UpdateData` for initial self updates (`SendInitSelf`), transport updates (`SendInitTransports`, `SendRemoveTransports`), and visibility updates (`UpdateActiveObjectVisibility`).
*   **`WorldObject.Object`**: Populates `UpdateData` via `BuildUpdateDataForPlayer`, `SendCreateUpdateToPlayer`, etc., by calling `AddUpdateBlockAndGetBuffer`.
*   **`Player.Main`**: Uses `UpdateData` for quest world object updates (`UpdateForQuestWorldObjects`) and bit refreshes (`RefreshBitsForVisibleUnits`).
*   **`game_Group_Group`**: Triggers updates when adding members (`AddMember`).
*   **`Unit.SpellAuras`**: Spells like Empathy, Charm, and Possession use `UpdateData` to notify players of state changes.
*   **`Transport`**: Sends create/out-of-range updates for vehicles.
*   **`GridNotifiers`**: Calls `AddOutOfRangeGUID` and `GetOutOfRangeGUIDs` to manage visibility.

### Outgoing Calls
*   **`ByteBuffer`**: Core storage for serialization (`wpos`, `append`, `resize`, `contents`, `clear`).
*   **`WorldPacket`**: Final output format; `UpdateData` sets opcodes and prepares packets for transmission.
*   **`WorldSession`**: `Send` calls `SendPacket` to transmit data.
*   **`World`**: Queries `getConfig` for compression thresholds and levels.
*   **`Log`**: Logs compression errors and oversized packet warnings.
*   **`ObjectGuid`**: Serializes GUIDs into buffers.
*   **`Errors`**: Throws exceptions via `PrintStacktraceAndThrow` on assertion failures.

## Data Model

This unit does not access any database tables. All data is transient, residing in memory within `std::list<UpdatePacket>` and `ObjectGuidSet` until sent over the network.

## Notable Implementation Details

1.  **32KB Chunking:** `AddUpdateBlockAndGetBuffer` enforces a 32KB limit per `UpdatePacket` in `m_datas`. This prevents single uncompressed blocks from becoming excessively large, though the final compressed packet may aggregate multiple blocks.
2.  **Out-of-Range GUID Scope:** In `Send`, `m_outOfRangeGUIDs` is cleared after the first packet is built. If `m_datas` contains multiple chunks, only the first packet includes out-of-range GUIDs. Callers must ensure this behavior aligns with client expectations for visibility removal.
3.  **Compression Thresholds:** Compression is skipped for packets below `CONFIG_UINT32_COMPRESSION_UPDATE_SIZE` to avoid CPU overhead for small updates.
4.  **Client Build Compatibility:** GUID serialization in `BuildPacket#2` branches on `SUPPORTED_CLIENT_BUILD`. Clients > 1.8.4 require packed GUIDs; older clients use the full 8-byte format.
5.  **MovementData Separation:** `MovementData` is separate because `SMSG_COMPRESSED_MOVES` requires a different packet structure (sub-packet size/opcodes) than `SMSG_UPDATE_OBJECT`.
6.  **Crash Prevention:** Both `UpdateData::BuildPacket#2` and `MovementData::BuildPacket` log `[CRASH-CLIENT]` warnings for packets >= 900,000 bytes, defending against potential client buffer overflows.

## Member Reference

**UpdateData**
Constructor. Initializes an empty update data container.

**~UpdateData**
Destructor. Calls `Clear()` to free internal buffers.

**AddOutOfRangeGUID**
Overload 1: Inserts all GUIDs from a set into `m_outOfRangeGUIDs`.

**AddOutOfRangeGUID#2**
Overload 2: Inserts a single GUID into `m_outOfRangeGUIDs`.

**AddUpdateBlockAndGetBuffer**
Returns a reference to a `ByteBuffer` for writing update data. Creates a new `UpdatePacket` if the list is empty or if the current packet exceeds 32KB. Increments the block count.

**Compress**
Static method in `PacketCompressor`. Compresses a source buffer into a destination buffer using zlib. Returns the compressed size. Logs errors if compression fails.

**HasData**
Returns true if there are pending update blocks or out-of-range GUIDs.

**GetOutOfRangeGUIDs**
Returns a const reference to the set of out-of-range GUIDs.

**BuildPacket#3**
Convenience overload. Builds a packet from the first `UpdatePacket` in `m_datas` (or none if empty) and the current out-of-range GUIDs.

**BuildPacket#2**
Core packet builder. Serializes out-of-range GUIDs and update blocks into a `WorldPacket`. Applies zlib compression if the size exceeds the configured threshold. Sets the appropriate opcode (`SMSG_UPDATE_OBJECT` or `SMSG_COMPRESSED_UPDATE_OBJECT`).

**Send**
Sends all accumulated update data to the `WorldSession`. Handles the special case of only out-of-range GUIDs. Iterates through `m_datas`, building and sending a packet for each chunk. Clears out-of-range GUIDs after the first send.

**Clear**
Empties `m_datas` and `m_outOfRangeGUIDs`.

**CanAddPacket**
(MovementData) Checks if a movement packet can be added to the buffer without exceeding size limits (255 bytes per sub-packet, 900KB total).

**AddPacket**
(MovementData) Appends a movement packet to the internal buffer, prefixed with its size and opcode.

**BuildPacket**
(MovementData) Compresses the accumulated movement buffer and sets the opcode to `SMSG_COMPRESSED_MOVES`.

---

<!-- machine-true, projected from graph.json -->

## Map — UpdateData

*Source:* UpdateData.cpp, UpdateData.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| UpdateData | ctor | — | game_Group_Group/AddMember, Map.Main/SendInitSelf, Map.Main/SendInitTransports, Map.Main/SendRemoveTransports, Map.Main/UpdateActiveObjectVisibility, Player.Main/RefreshBitsForVisibleUnits, Player.Main/SetCheatDebugTargetInfo, Player.Main/UpdateForQuestWorldObjects, Transport/SendCreateUpdateToMap, Transport/SendOutOfRangeUpdateToMap, Unit.SpellAuras/HandleAuraEmpathy, Unit.SpellAuras/HandleModCharm, Unit.SpellAuras/HandleModPossess, WorldObject.Object/BuildUpdateDataForPlayer, WorldObject.Object/DirectSendPublicValueUpdate#2, WorldObject.Object/SendCreateUpdateToPlayer, WorldObject.Object/SendOutOfRangeUpdateToPlayer | — |
| ~UpdateData | dtor | — | — | — |
| AddOutOfRangeGUID | method | — | GridNotifiers/Notify | — |
| AddOutOfRangeGUID#2 | method | — | WorldObject.Object/BuildOutOfRangeUpdateBlock | — |
| AddUpdateBlockAndGetBuffer | method | ByteBuffer/wpos | Player.Main/RefreshBitsForVisibleUnits, WorldObject.Object/BuildCreateUpdateBlockForPlayer, WorldObject.Object/BuildMovementUpdateBlock, WorldObject.Object/BuildValuesUpdateBlockForPlayer, WorldObject.Object/DirectSendPublicValueUpdate#2 | — |
| Compress | method | Log.Main/Out, World/getConfig#4 | — | — |
| HasData | method | — | game_Group_Group/AddMember, GridNotifiers/Notify, Map.Main/UpdateActiveObjectVisibility, Player.Main/SetCheatDebugTargetInfo, Player.Main/UpdateForQuestWorldObjects, Unit.SpellAuras/HandleAuraEmpathy, Unit.SpellAuras/HandleModCharm, Unit.SpellAuras/HandleModPossess | — |
| GetOutOfRangeGUIDs | method | — | GridNotifiers/Notify | — |
| BuildPacket#3 | method | — | game_Group_Group/AddMember, Player.Main/SetCheatDebugTargetInfo, Transport/SendOutOfRangeUpdateToMap, Unit.SpellAuras/HandleAuraEmpathy, Unit.SpellAuras/HandleModCharm, Unit.SpellAuras/HandleModPossess, WorldObject.Object/DirectSendPublicValueUpdate#2, WorldObject.Object/SendOutOfRangeUpdateToPlayer | — |
| BuildPacket#2 | method | ByteBuffer/append#3, ByteBuffer/ByteBuffer#4, ByteBuffer/contents, ByteBuffer/empty, ByteBuffer/operator<<#10, ByteBuffer/operator<<#7, ByteBuffer/resize, ByteBuffer/wpos, Errors/PrintStacktraceAndThrow, Log.Main/Out, ObjectGuid/operator<<#2, ObjectGuid/WriteAsPacked, World/getConfig#4, WorldPacket/SetOpcode | — | — |
| Send | method | ByteBuffer/clear, WorldPacket/WorldPacket, WorldSession.Main/SendPacket | GridNotifiers/Notify, Map.Main/SendInitSelf, Map.Main/SendInitTransports, Map.Main/SendObjectUpdates, Map.Main/SendRemoveTransports, Map.Main/UpdateActiveObjectVisibility, Player.Main/RefreshBitsForVisibleUnits, Player.Main/UpdateForQuestWorldObjects, Transport/SendCreateUpdateToMap, WorldObject.Object/SendCreateUpdateToPlayer, WorldObject.Object/SendForcedObjectUpdate | — |
| Clear | method | — | — | — |
| CanAddPacket | method | ByteBuffer/wpos | WorldSession.Main/SendMovementPacket | — |
| AddPacket | method | ByteBuffer/append#5, ByteBuffer/contents, ByteBuffer/operator<<#13, ByteBuffer/operator<<#7, ByteBuffer/wpos, Errors/PrintStacktraceAndThrow, WorldPacket/GetOpcode | WorldSession.Main/SendMovementPacket | — |
| BuildPacket | method | ByteBuffer/contents, ByteBuffer/empty, ByteBuffer/resize, ByteBuffer/wpos, Errors/PrintStacktraceAndThrow, Log.Main/Out, WorldPacket/SetOpcode | WorldSession.Main/SendCompressedMovementPackets | — |
