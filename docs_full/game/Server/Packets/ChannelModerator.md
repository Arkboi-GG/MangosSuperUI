# ChannelModerator

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ChannelModerator

**Purpose & Responsibilities**

`ChannelModerator` is a client-to-server packet structure within the `WorldPackets::Channel` namespace. Its sole responsibility is to represent the raw data received from a client when a player attempts to grant moderator privileges to another player within a specific chat channel. It acts as a data carrier, holding the target channel name and the name of the player to be promoted, until downstream logic processes the request.

**Member-by-Member Behavior**

The unit contains a single member: the constructor.

*   **ChannelModerator**: This default constructor initializes the `ClientPacket` base class with the opcode `CMSG_CHANNEL_MODERATOR`. This opcode identifies the packet type to the network handler, ensuring the incoming byte stream is routed to the correct deserialization logic. The constructor does not initialize the `channelName` or `playerName` members; these remain empty strings until populated by the `ReadFromWorldPacket` method (defined in the base class hierarchy or implemented elsewhere, but not part of this unit's direct behavior).

**Cross-Unit Boundaries**

*   **Calls Out**: None. The constructor performs no external calls.
*   **Called By**: None listed in the map. In practice, this object is instantiated by the network layer when a packet with opcode `CMSG_CHANNEL_MODERATOR` is detected, but this instantiation logic resides outside this unit.

**Data Model**

This unit interacts with no database tables. It operates entirely on transient network data.

**Notable Implementation Details**

*   **Final Class**: The class is marked `final`, preventing inheritance. This enforces a strict contract for this specific packet type.
*   **Public Data Members**: Unlike typical encapsulated C++ classes, `channelName` and `playerName` are public `std::string` members. This design choice prioritizes simplicity and direct access for the deserialization routine and subsequent business logic, avoiding the overhead of getter/setter methods for simple data transfer objects.
*   **Opcode Binding**: The constructor explicitly binds the packet to `CMSG_CHANNEL_MODERATOR`. Any change to this opcode constant would require updating this constructor to maintain protocol synchronization.

## Member Reference

**ChannelModerator**
Default constructor that initializes the base `ClientPacket` with the `CMSG_CHANNEL_MODERATOR` opcode. It prepares the object to receive and hold the channel name and target player name for the moderator assignment command.

---

<!-- machine-true, projected from graph.json -->

## Map — ChannelModerator

*Source:* Channel.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ChannelModerator | ctor | — | — | — |
