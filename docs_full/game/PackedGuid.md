# PackedGuid

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# PackedGuid

**Purpose & Responsibilities**

The `PackedGuid` class serves as a thin wrapper around a `ByteBuffer` to hold a **packed** representation of a 64-bit object identifier (`ObjectGuid`). In the World of Warcraft protocol, GUIDs are often transmitted in a variable-length "packed" format to reduce bandwidth, particularly when the high-part of the GUID allows for shorter encoding. `PackedGuid` encapsulates this serialized byte sequence, providing methods to initialize it from a raw `uint64` or an `ObjectGuid`, update its content, and query its current byte size. It is primarily used during network serialization to prepare GUID data for transmission to clients.

Unlike `ObjectGuid`, which manages the logical structure and type-checking of the identifier, `PackedGuid` is concerned solely with the binary payload required by the network layer. It does not perform any parsing or validation of the GUID structure itself; it relies on `ByteBuffer::appendPackGUID` to handle the actual packing logic.

## Member-by-Member Behavior

### Construction and Initialization

The class provides three constructors, all of which initialize an internal `ByteBuffer` (`m_packedGuid`) with a minimum capacity of 9 bytes (`PACKED_GUID_MIN_BUFFER_SIZE`).

*   **`PackedGuid()`**: The default constructor creates a `PackedGuid` representing an empty or zero GUID. It initializes the buffer and immediately packs the value `0`.
*   **`PackedGuid(uint64 const& guid)`**: Constructs a `PackedGuid` from a raw 64-bit integer. It initializes the buffer and packs the provided `guid` value.
*   **`PackedGuid(ObjectGuid const& guid)`**: Constructs a `PackedGuid` from an `ObjectGuid` instance. It extracts the raw 64-bit value via `ObjectGuid::GetRawValue()` and packs it into the buffer.

### Modification

*   **`Set(uint64 const& guid)`**: Updates the internal packed representation to reflect a new raw 64-bit GUID value. It resets the write position of the internal `ByteBuffer` to 0 and repacks the new `guid`.
*   **`Set(ObjectGuid const& guid)`**: Overload that accepts an `ObjectGuid`. It resets the buffer's write position and repacks the raw value obtained from the `ObjectGuid`.

### Accessors

*   **`size()`**: Returns the current number of bytes occupied by the packed GUID in the internal `ByteBuffer`. This is critical for network senders to know how many bytes to transmit.

## Cross-Unit Boundaries

`PackedGuid` acts as a data carrier between the object management layer and the network serialization layer.

*   **Called by `WorldSession.CombatHandler/SendAttackStop`**: When a player stops attacking, the combat handler needs to send a message to the client containing the target's GUID. It constructs a `PackedGuid` to serialize the target's identifier for the outgoing packet.
*   **Called by `MovementPacketSender/SendMovementFlagChangeToAll`** and **`SendMovementFlagChangeToController`**: These movement-related functions need to broadcast changes in movement flags (e.g., falling, jumping) to observers. They use `PackedGuid::size()` to determine the length of the packed GUID field within the movement update packet, ensuring the packet structure is correctly formed before transmission.
*   **Called by `Player.Main/SetClientControl`**: When a player gains or loses control of a unit (such as a pet or vehicle), the server sends a control update packet. This packet includes the GUID of the controlled unit. The `Player` module uses `PackedGuid::size()` to calculate the packet size for this GUID field.

Note: While `PackedGuid` is constructed by these callers, the actual serialization into a network stream is handled by the friend operator `operator<<` defined in `ObjectGuid.h` (though the implementation of that operator is likely in a corresponding `.cpp` file, the declaration is here). The `PackedGuid` class itself does not call out to other units; it is a passive data holder.

## Data Model

`PackedGuid` does not interact with any database tables. It operates entirely in memory, handling transient network serialization data.

## Notable Implementation Details

*   **Buffer Management**: The internal `ByteBuffer` is initialized with a fixed minimum size of 9 bytes. This is because the maximum size of a packed GUID in the WoW protocol is 9 bytes (when the high part is non-zero and requires full expansion). The `Set` methods reset the write position (`wpos(0)`) before repacking, effectively overwriting the previous content. This implies `PackedGuid` is designed for single-use or overwrite scenarios, not for appending multiple GUIDs.
*   **Dependency on `ByteBuffer`**: The class delegates all actual packing logic to `ByteBuffer::appendPackGUID`. This means the efficiency and correctness of the packing depend entirely on that method's implementation. `PackedGuid` adds no logic of its own regarding the bit-shifting or variable-length encoding rules.
*   **Friend Operator**: The class grants friendship to `operator<<` for `ByteBuffer`, allowing direct access to the private `m_packedGuid` member for efficient streaming. This avoids copying the buffer contents, enabling zero-copy serialization where possible.
*   **No Validation**: `PackedGuid` does not validate whether the input `uint64` or `ObjectGuid` is valid or empty. It blindly packs whatever value is provided. An empty GUID (0) will result in a 1-byte packed representation (typically just `0x00`), while a full GUID might expand to 9 bytes.

## Member Reference

**PackedGuid** (ctor): Default constructor that initializes the internal buffer and packs a zero GUID.

**PackedGuid#3** (ctor): Constructor taking a raw `uint64` GUID value, initializing the buffer and packing the provided value.

**PackedGuid#2** (ctor): Constructor taking an `ObjectGuid`, extracting its raw value, and packing it into the internal buffer.

**Set#2** (method): Overload that sets the packed GUID from an `ObjectGuid` instance by resetting the buffer and repacking the raw value.

**Set** (method): Sets the packed GUID from a raw `uint64` value by resetting the buffer's write position and repacking the new value.

**size** (method): Returns the current byte size of the packed GUID stored in the internal `ByteBuffer`.

---

<!-- machine-true, projected from graph.json -->

## Map — PackedGuid

*Source:* ObjectGuid.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| PackedGuid | ctor | — | WorldSession.CombatHandler/SendAttackStop | — |
| PackedGuid#3 | ctor | — | — | — |
| PackedGuid#2 | ctor | — | — | — |
| Set#2 | method | — | — | — |
| Set | method | — | WorldObject.Object/_Create | — |
| size | method | — | MovementPacketSender/SendMovementFlagChangeToAll, MovementPacketSender/SendMovementFlagChangeToController, Player.Main/SetClientControl | — |
