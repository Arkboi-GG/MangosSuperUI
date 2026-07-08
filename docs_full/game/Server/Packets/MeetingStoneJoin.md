# MeetingStoneJoin

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# MeetingStoneJoin

**Purpose & Responsibilities**
`MeetingStoneJoin` is a client-side packet structure within the `WorldPackets::Misc` namespace, responsible for encapsulating the data sent by the game client when a player attempts to join a battleground queue via a Meeting Stone object. It inherits from `ClientPacket`, marking it as an incoming message from the client identified by the opcode `CMSG_MEETINGSTONE_JOIN`. The class holds a single piece of state: the `ObjectGuid` of the Meeting Stone object that initiated the request.

**Member-by-Member Behavior**
The unit contains only one member, the constructor, which initializes the packet metadata.

*   **`MeetingStoneJoin`**: This constructor sets up the packet instance. It explicitly calls the base `ClientPacket` constructor, passing `CMSG_MEETINGSTONE_JOIN` to identify the packet type for the server's dispatch system. It leaves the `guid` member uninitialized (default-constructed `ObjectGuid`) until the packet data is read from the network stream by the inherited `ReadFromWorldPacket` method (which is declared in the base class hierarchy but implemented elsewhere, likely in a corresponding `.cpp` file or template specialization not included in this specific partial view, though the declaration exists in the header).

**Cross-Unit Boundaries**
*   **Calls Out**: None. The constructor performs no external calls.
*   **Called By**: None listed in the map. In practice, this class is instantiated by the packet parsing infrastructure when a `CMSG_MEETINGSTONE_JOIN` opcode is received from the client.

**Data Model**
This unit does not interact with any database tables. It operates purely on in-memory packet data.

**Notable Implementation Details**
*   **Final Class**: The class is marked `final`, preventing further inheritance.
*   **Explicit Constructor**: The constructor is marked `explicit` to prevent implicit conversions from `CMSG_MEETINGSTONE_JOIN` or similar types.
*   **Guid Storage**: The `guid` field stores the identifier of the specific Meeting Stone object. This allows the server handler to verify that the player is interacting with a valid, nearby Meeting Stone and to determine which battleground queue or specific instance context is being requested based on that object's definition.

## Member Reference

**MeetingStoneJoin**
Constructor for the `MeetingStoneJoin` packet. Initializes the base `ClientPacket` with the opcode `CMSG_MEETINGSTONE_JOIN`. Does not initialize the `guid` member; that occurs during packet deserialization.

---

<!-- machine-true, projected from graph.json -->

## Map — MeetingStoneJoin

*Source:* Misc.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| MeetingStoneJoin | ctor | — | — | — |
