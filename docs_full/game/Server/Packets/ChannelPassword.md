# ChannelPassword

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ChannelPassword

**Purpose & Responsibilities**

`ChannelPassword` is a client-side packet structure within the `WorldPackets::Channel` namespace, responsible for representing the `CMSG_CHANNEL_PASSWORD` message sent from the game client to the server. Its sole responsibility is to define the data layout for a request to change or set the password of a specific chat channel. It acts as a passive data carrier, holding two string fields: the target channel's name and the new password value. It inherits from `ClientPacket`, indicating it originates from the client side of the network protocol.

**Member-by-Member Behavior**

This unit contains a single member: the constructor.

*   **ChannelPassword**: The default constructor initializes the base `ClientPacket` class with the opcode `CMSG_CHANNEL_PASSWORD`. This registration ensures that when the networking layer receives a packet with this specific opcode, it can instantiate this class to deserialize the payload. The constructor does not perform any validation or initialization of the member variables (`channelName`, `password`); these are populated later by the `ReadFromWorldPacket` method (which is declared in the shared header but implemented elsewhere, likely in a corresponding `.cpp` file not included in this unit's scope).

**Cross-Unit Boundaries**

*   **Calls Out**: None. The constructor only invokes the base class constructor.
*   **Called By**: None listed in the map. In practice, this class is instantiated by the network packet dispatcher (likely in `WorldSession` or a packet handler registry) when a raw network packet matching `CMSG_CHANNEL_PASSWORD` arrives. The dispatcher will then call `ReadFromWorldPacket` to populate the strings.

**Data Model**

This unit does not interact directly with any database tables. It operates purely on in-memory network packet data. Any persistence of channel passwords would occur in downstream handlers that process this packet after deserialization.

**Notable Implementation Details**

*   **Final Class**: The class is marked `final`, preventing inheritance. This is consistent with its role as a leaf node in the packet hierarchy.
*   **Public Members**: The fields `channelName` and `password` are public. This design choice allows direct access by the packet reading logic and subsequent business logic handlers without needing getter/setter methods, prioritizing simplicity and performance in the hot path of network processing.
*   **String Storage**: Both fields use `std::string`. The actual deserialization logic (not shown here) must handle the binary format of these strings as defined by the World of Warcraft protocol (typically length-prefixed UTF-8 strings).

## Member Reference

**ChannelPassword**
The default constructor for the `ChannelPassword` packet class. It initializes the base `ClientPacket` with the opcode `CMSG_CHANNEL_PASSWORD`, registering this class as the handler for incoming password-change requests from clients. It does not initialize the `channelName` or `password` members; those are filled during packet deserialization.

---

<!-- machine-true, projected from graph.json -->

## Map — ChannelPassword

*Source:* Channel.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ChannelPassword | ctor | — | — | — |
