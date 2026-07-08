# ChannelMute

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ChannelMute

**Purpose & Responsibilities**

`ChannelMute` is a client-side packet structure within the `WorldPackets::Channel` namespace, responsible for representing the `CMSG_CHANNEL_MUTE` message sent by the game client to the server. Its sole responsibility is to define the data layout for a request to mute a specific player within a specific channel. It holds two string fields: `channelName`, identifying the target channel, and `playerName`, identifying the player to be muted. As a `ClientPacket`, it serves as the input container for the network layer to deserialize incoming binary data into a structured object that higher-level game logic can process.

**Member-by-Member Behavior**

The unit contains only one member: the constructor.

*   **`ChannelMute`**: This is the default constructor for the `ChannelMute` packet. It initializes the base class `ClientPacket` with the constant `CMSG_CHANNEL_MUTE`, which identifies this specific packet type in the network protocol. It does not initialize the `channelName` or `playerName` members; these are populated later by the `ReadFromWorldPacket` method (which is declared in the header but implemented elsewhere, likely in a corresponding `.cpp` file or via inline definition not shown in the provided source snippet, though the MAP indicates no other members for this unit). The constructor ensures the packet is correctly typed before any data is read.

**Cross-Unit Boundaries**

*   **Calls Out**: None. The constructor does not invoke any other units.
*   **Called By**: None listed in the MAP. However, conceptually, this constructor is called by the network deserialization framework when a `CMSG_CHANNEL_MUTE` packet is received from the client. The framework instantiates this object and then calls its `ReadFromWorldPacket` method to populate the strings.

**Data Model**

This unit does not interact directly with any database tables. It operates purely on in-memory data structures derived from network packets.

**Notable Implementation Details**

*   **Inheritance**: `ChannelMute` inherits from `ClientPacket`, indicating it is part of the client-to-server communication flow.
*   **Final Class**: The class is marked `final`, preventing further inheritance. This is consistent with leaf-node packet structures that have a fixed format.
*   **String Storage**: The `channelName` and `playerName` are stored as `std::string`. This implies that the `ReadFromWorldPacket` implementation (not shown in this unit's source but referenced by the class interface) will handle the extraction of these strings from the raw `WorldPacket` buffer. The lack of validation in the constructor means that empty or invalid strings might be passed to higher-level logic if the packet parsing fails or sends malformed data, relying on downstream handlers to validate.

## Member Reference

**ChannelMute**
Constructor for the `ChannelMute` packet. Initializes the base `ClientPacket` with the opcode `CMSG_CHANNEL_MUTE`. Does not initialize the `channelName` or `playerName` members; these are filled during packet deserialization.

---

<!-- machine-true, projected from graph.json -->

## Map — ChannelMute

*Source:* Channel.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ChannelMute | ctor | — | — | — |
