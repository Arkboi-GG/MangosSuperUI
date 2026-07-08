# ChannelList

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ChannelList

**Purpose & Responsibilities**

`ChannelList` is a client-to-server packet definition within the `WorldPackets::Channel` namespace. It represents the `CMSG_CHANNEL_LIST` message sent by a client to request information about channels. As a `ClientPacket`, its sole responsibility is to define the data structure expected from the wire and provide the mechanism to deserialize that data from a raw `WorldPacket`. It contains a single field, `channelName`, which likely specifies the target channel for the list request or acts as a filter, though the specific semantic intent of this field is determined by the server-side handler that consumes this packet, not by the packet definition itself.

This unit is part of a larger family of channel-related packets defined in `Channel.h`, including join, leave, moderation, and administrative commands. However, `ChannelList` is distinct in that it only carries a channel name, unlike many other channel packets that also carry player names or passwords.

**Member-by-Member Behavior**

The unit consists of a single member: the constructor.

*   **Constructor (`ChannelList()`)**: This explicit constructor initializes the base class `ClientPacket` with the constant `CMSG_CHANNEL_LIST`. This associates the packet instance with the specific opcode used to identify this message type on the network. The constructor does not initialize the `channelName` member; that occurs later during deserialization via `ReadFromWorldPacket`.

**Cross-Unit Boundaries**

*   **Calls Out**: None. The constructor only interacts with its base class `ClientPacket`.
*   **Called By**: None listed in the map. In practice, this packet is instantiated by the network layer when a `CMSG_CHANNEL_LIST` opcode is received, and then passed to a handler function (likely in a different unit, such as a channel command handler or world session manager) for processing. The `ReadFromWorldPacket` method is called by the network infrastructure to populate the `channelName` field.

**Data Model**

This unit does not interact directly with any database tables. It operates purely on in-memory data structures derived from network packets.

**Notable Implementation Details**

*   **Inheritance**: Inherits from `ClientPacket`, implying it is part of a standardized packet handling framework where all client-bound messages share common traits (like opcode management and serialization hooks).
*   **Final Class**: The class is marked `final`, preventing further inheritance. This suggests the packet structure is considered complete and stable.
*   **String Storage**: Uses `std::string` for `channelName`. The deserialization logic (in `ReadFromWorldPacket`, defined elsewhere but declared here) will handle reading the string from the binary packet data. No validation or sanitization is performed at the packet level; that is the responsibility of the consuming logic.

## Member Reference

**ChannelList**
The default constructor for the `ChannelList` packet. It explicitly initializes the base `ClientPacket` with the opcode `CMSG_CHANNEL_LIST`. It does not perform any additional initialization of the `channelName` member variable.

---

<!-- machine-true, projected from graph.json -->

## Map — ChannelList

*Source:* Channel.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ChannelList | ctor | — | — | — |
