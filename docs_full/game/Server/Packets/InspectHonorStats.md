# InspectHonorStats

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# InspectHonorStats

**Purpose & Responsibilities**

`InspectHonorStats` is a client-side packet structure within the `WorldPackets::Misc` namespace, defined in `Misc.h`. Its sole responsibility is to represent the network message sent by a game client when a player requests to inspect the honor statistics of another character. It encapsulates the target character's unique identifier (`ObjectGuid`) and associates this request with the specific opcode `MSG_INSPECT_HONOR_STATS`. As a `ClientPacket`, it serves as the data container for the incoming wire format, holding the raw data until it is processed by the server-side handler.

**Member-by-Member Behavior**

The unit consists of a single constructor and one public data member.

*   **Constructor (`InspectHonorStats`)**: Initializes the packet object. It sets the internal opcode to `MSG_INSPECT_HONOR_STATS`, identifying the message type for the network layer. It leaves the `guid` member uninitialized (default constructed), expecting it to be populated later via the `ReadFromWorldPacket` method (which is declared in the base class `ClientPacket` but implemented elsewhere, likely in a corresponding `.cpp` file not included in this unit's scope).
*   **`guid`**: A public member of type `ObjectGuid`. This field stores the unique identifier of the character whose honor stats are being requested. It is the primary payload of this packet.

**Cross-Unit Boundaries**

*   **Calls Out**: The constructor calls the base class constructor `ClientPacket(MSG_INSPECT_HONOR_STATS)` from the `ClientPacket` unit (defined in `Packet.h`). This establishes the packet's identity within the network protocol.
*   **Called By**: According to the provided MAP, no other units explicitly call into this unit's members. However, in the broader context of the engine, the server's network handler will instantiate this class and invoke its `ReadFromWorldPacket` method (inherited from `ClientPacket`) to deserialize the `guid` from the raw network buffer. Once deserialized, the server logic will use the `guid` to locate the target player and retrieve their honor data.

**Data Model**

This unit does not interact directly with any database tables. It operates purely on in-memory network packet data. The `guid` it carries refers to a player entity, which may subsequently lead to database queries in other units (e.g., `Player` or `HonorHandler`), but `InspectHonorStats` itself performs no I/O.

**Notable Implementation Details**

*   **Final Class**: The class is marked `final`, preventing inheritance. This is consistent with its role as a leaf node in the packet hierarchy, representing a specific, fixed-format message.
*   **Public Data Member**: The `guid` is public, allowing direct access by the deserialization logic and subsequent handlers without needing getter/setter methods. This is a common pattern in high-performance network packet structures to minimize overhead.
*   **Opcode Specificity**: Unlike many other packets in `Misc.h` that use standard `CMSG_*` opcodes, this packet uses `MSG_INSPECT_HONOR_STATS`. This suggests it might be part of a slightly different or older protocol variant, or simply a distinct message type not grouped under the standard client message prefix. Maintainers should ensure that the server's opcode table correctly maps `MSG_INSPECT_HONOR_STATS` to the appropriate handler.

## Member Reference

**InspectHonorStats**  
Constructor for the `InspectHonorStats` packet. Initializes the base `ClientPacket` with the opcode `MSG_INSPECT_HONOR_STATS`. It prepares the object to receive the target character's GUID during deserialization.

---

<!-- machine-true, projected from graph.json -->

## Map — InspectHonorStats

*Source:* Misc.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| InspectHonorStats | ctor | — | — | — |
