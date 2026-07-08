# ChannelOwner

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ChannelOwner

**Purpose & Responsibilities**

`ChannelOwner` is a client-to-server packet structure within the `WorldPackets::Channel` namespace. It represents the `CMSG_CHANNEL_OWNER` message sent by a client to request ownership information for a specific chat channel. Its sole responsibility is to encapsulate the target `channelName` string and provide the mechanism to deserialize this data from the raw network buffer (`WorldPacket`) into the object's member variable. It contains no business logic, validation, or server-side processing; it is strictly a data carrier for the incoming request.

**Member-by-Member Behavior**

The unit consists of a single constructor and inherits the deserialization interface from `ClientPacket`.

*   **Construction**: The explicit constructor initializes the base `ClientPacket` with the opcode `CMSG_CHANNEL_OWNER`. This registers the packet type with the network handler so that incoming packets with this opcode are routed to an instance of `ChannelOwner` for reading.
*   **Data Storage**: The public member `std::string channelName` holds the name of the channel for which the owner is being requested. This field is populated during the deserialization process.

**Cross-Unit Boundaries**

*   **Calls Out**: None. The `ChannelOwner` unit does not invoke any functions in other units.
*   **Called By**: While the MAP indicates no external callers, in practice, this class is instantiated and its `ReadFromWorldPacket` method is invoked by the core network handling infrastructure (likely within `WorldSession` or a central packet dispatcher) when a `CMSG_CHANNEL_OWNER` packet arrives on the wire. The caller passes a `WorldPacket` reference containing the raw bytes.

**Data Model**

This unit interacts with no database tables. It operates entirely on transient network data.

**Notable Implementation Details**

*   **Inheritance**: Inherits from `ClientPacket`, implying it is part of a hierarchy distinguishing client-originated messages from server-originated ones.
*   **Deserialization**: The actual logic for extracting `channelName` from the `WorldPacket` resides in the overridden `ReadFromWorldPacket` method. Although the implementation of `ReadFromWorldPacket` is not shown in the provided source snippet (it is likely defined in a corresponding `.cpp` file or inline elsewhere), the signature confirms it overrides the virtual method from `ClientPacket`. The standard pattern for such strings in this codebase typically involves reading a null-terminated string or a fixed-length string depending on the game version's protocol.

## Member Reference

**ChannelOwner**
Constructor for the `ChannelOwner` packet. Initializes the base `ClientPacket` with the opcode `CMSG_CHANNEL_OWNER`. It prepares the object to receive and store the `channelName` parameter from the incoming network stream.

---

<!-- machine-true, projected from graph.json -->

## Map — ChannelOwner

*Source:* Channel.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ChannelOwner | ctor | — | — | — |
