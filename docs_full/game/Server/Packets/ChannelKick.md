# ChannelKick

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ChannelKick

**Purpose & Responsibilities**

`ChannelKick` is a client-side packet structure within the `WorldPackets::Channel` namespace, responsible for representing the `CMSG_CHANNEL_KICK` message sent from the game client to the server. Its sole responsibility is to define the data layout for a request to remove a specific player from a specific chat channel. It acts as a data container, holding the target channel name and the name of the player to be kicked, ready to be populated by deserialization logic.

As a `final` class inheriting from `ClientPacket`, it is part of the network layer's abstraction, ensuring type-safe handling of incoming client requests related to channel management. It does not contain business logic, validation, or server-side processing; those concerns reside in the handlers that consume this packet object.

**Member-by-Member Behavior**

The unit consists of a single member: the constructor.

*   **Constructor (`ChannelKick`)**: Initializes the packet object. It explicitly invokes the base class constructor `ClientPacket(CMSG_CHANNEL_KICK)`, registering this instance with the specific opcode `CMSG_CHANNEL_KICK`. This association allows the network dispatcher to correctly route incoming raw bytes to this specific packet type for parsing. The constructor does not initialize the member variables `channelName` or `playerName`; these are left empty until `ReadFromWorldPacket` (defined in the corresponding `.cpp` file, though not shown in the provided source snippet, its declaration exists in the header) populates them from the network stream.

**Cross-Unit Boundaries**

*   **Calls Out**: None. The constructor only calls the base class constructor.
*   **Called By**: None listed in the MAP. In practice, this class is instantiated by the network input handler when a `CMSG_CHANNEL_KICK` opcode is detected, but such interactions occur outside this unit's scope.

**Data Model**

This unit does not interact with any database tables. It operates entirely on in-memory network packet data.

**Notable Implementation Details**

*   **Final Class**: The class is marked `final`, preventing further inheritance. This enforces a flat hierarchy for this specific packet type, simplifying polymorphic dispatch and ensuring no derived classes alter its memory layout or behavior.
*   **Public Members**: The fields `channelName` and `playerName` are public. This design choice allows direct access by the deserialization routine (`ReadFromWorldPacket`) and potentially by the handler that processes the kick request, avoiding the overhead of getter/setter methods for simple data transfer objects.
*   **Explicit Constructor**: The use of `explicit` prevents implicit conversions from `CMSG_CHANNEL_KICK` (if it were convertible) or other types, ensuring that `ChannelKick` objects are only created intentionally.

## Member Reference

**ChannelKick**
Constructor for the `ChannelKick` packet. Initializes the base `ClientPacket` with the opcode `CMSG_CHANNEL_KICK`. Does not initialize the string members `channelName` or `playerName`.

---

<!-- machine-true, projected from graph.json -->

## Map — ChannelKick

*Source:* Channel.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ChannelKick | ctor | — | — | — |
