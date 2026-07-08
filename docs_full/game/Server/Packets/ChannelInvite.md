# ChannelInvite

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ChannelInvite

**Purpose & Responsibilities**

`ChannelInvite` is a client-side packet structure within the `WorldPackets::Channel` namespace, responsible for representing the `CMSG_CHANNEL_INVITE` message sent from the game client to the server. Its sole responsibility is to carry two pieces of data required to process a channel invitation request: the name of the target channel and the name of the player being invited. As a `ClientPacket`, it serves as the deserialization target for incoming network data related to this specific action. It does not contain logic for validation, permission checking, or state mutation; those concerns reside in the server-side handler that consumes this packet.

**Member-by-Member Behavior**

The unit consists of a single constructor and two public data members.

*   **Constructor (`ChannelInvite`)**: The explicit constructor initializes the base `ClientPacket` class with the opcode `CMSG_CHANNEL_INVITE`. This registration ensures that when the network layer receives a packet with this opcode, it instantiates this specific struct to hold the payload.
*   **Data Members**:
    *   `channelName`: A `std::string` holding the identifier of the channel to which the user wishes to invite someone.
    *   `playerName`: A `std::string` holding the identifier of the player who is the recipient of the invitation.

**Cross-Unit Boundaries**

*   **Calls Out**: None. The `ChannelInvite` class itself performs no outbound calls. The actual parsing of the binary data into the `channelName` and `playerName` fields is handled by the `ReadFromWorldPacket` method, which is declared in this header but implemented elsewhere (likely in a corresponding `.cpp` file not included in this unit's scope). However, since `ReadFromWorldPacket` is not listed in the MAP for this specific partial/unit definition, we treat the class as a pure data carrier in this context.
*   **Called By**: The MAP indicates no external callers for the constructor. In practice, this object is instantiated by the packet dispatching system (e.g., `WorldSession` or a central packet router) when a `CMSG_CHANNEL_INVITE` opcode is detected on the wire. The handler for this packet will then access the `channelName` and `playerName` members to execute the business logic (checking permissions, finding the target player, etc.).

**Data Model**

This unit interacts with no database tables. It is purely a network protocol buffer structure.

**Notable Implementation Details**

*   **Final Class**: The class is marked `final`, preventing inheritance. This is appropriate for a leaf packet structure.
*   **String Storage**: Both `channelName` and `playerName` are stored as `std::string`. This implies that the `ReadFromWorldPacket` implementation (not shown here) must correctly parse the string length and content from the raw `WorldPacket` buffer. Any mismatch between the client's encoding and the server's parsing logic here would result in corrupted data, but the risk is contained within the parsing method.
*   **No Validation**: The struct imposes no constraints on the strings (e.g., max length, valid characters). Validation must occur downstream in the handler that processes this packet.

## Member Reference

**ChannelInvite**
The explicit constructor for the `ChannelInvite` packet. It initializes the base `ClientPacket` with the opcode `CMSG_CHANNEL_INVITE`, registering this structure as the handler for incoming channel invitation requests from clients. It takes no arguments other than the implicit base class initialization.

---

<!-- machine-true, projected from graph.json -->

## Map — ChannelInvite

*Source:* Channel.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ChannelInvite | ctor | — | — | — |
