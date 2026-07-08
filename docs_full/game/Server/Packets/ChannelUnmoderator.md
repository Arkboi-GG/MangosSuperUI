# ChannelUnmoderator

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ChannelUnmoderator

**Purpose & Responsibilities**

`ChannelUnmoderator` is a client-side packet definition within the `WorldPackets::Channel` namespace. It represents the `CMSG_CHANNEL_UNMODERATOR` message sent by the game client to the server, requesting that a specific player be removed from the moderator list of a specific chat channel. As a `ClientPacket`, its sole responsibility is to define the data structure required to deserialize this request from the raw network stream. It holds two fields: the name of the target channel and the name of the player to be unmoderated.

This unit is part of a larger family of channel management packets defined in `Channel.h`. It does not contain logic for processing the request, validating permissions, or modifying channel state; those responsibilities lie elsewhere in the codebase. Its role is strictly to provide a typed interface for reading the incoming binary data.

**Member-by-Member Behavior**

The unit contains a single member: the constructor.

*   **Constructor (`ChannelUnmoderator`)**: Initializes the packet object. It explicitly calls the base class `ClientPacket` constructor, passing the opcode `CMSG_CHANNEL_UNMODERATOR`. This associates the packet instance with the correct network message type, ensuring that the server's packet dispatcher can route incoming data with this opcode to the correct handler. The constructor does not initialize the member variables `channelName` or `playerName`; these are populated later by the `ReadFromWorldPacket` method (inherited from `ClientPacket` but implemented in the corresponding `.cpp` file, though not shown in the provided source snippet, the declaration implies its existence via the override).

**Cross-Unit Boundaries**

*   **Calls Out**: None. The constructor only invokes the base class constructor.
*   **Called By**: None listed in the MAP. In practice, this class is instantiated by the network layer when a packet with opcode `CMSG_CHANNEL_UNMODERATOR` is received. The network layer will then call `ReadFromWorldPacket` to populate the fields.

**Data Model**

This unit does not interact with any database tables. It operates entirely on in-memory data structures representing network packet payloads.

**Notable Implementation Details**

*   **Final Class**: The class is marked `final`, preventing further inheritance. This is consistent with the design of packet classes in this codebase, which are leaf nodes in the class hierarchy.
*   **Public Members**: The fields `channelName` and `playerName` are public. This allows direct access after deserialization, simplifying the code that processes the packet. However, it also means there is no encapsulation or validation at the packet level.
*   **String Storage**: Both channel and player names are stored as `std::string`. This implies that the deserialization logic (in `ReadFromWorldPacket`) must handle string extraction correctly, likely using null-terminated strings or length-prefixed strings depending on the protocol version.
*   **No Logic**: The header file contains no implementation logic. All behavior related to reading the packet data is deferred to the `ReadFromWorldPacket` method, which is declared but not defined in this header. The actual parsing logic resides in the corresponding `.cpp` file (not provided in the source snippet, but implied by the class structure).

## Member Reference

**ChannelUnmoderator**
Constructor for the `ChannelUnmoderator` packet. Initializes the base `ClientPacket` with the opcode `CMSG_CHANNEL_UNMODERATOR`. Does not initialize member variables.

---

<!-- machine-true, projected from graph.json -->

## Map — ChannelUnmoderator

*Source:* Channel.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ChannelUnmoderator | ctor | — | — | — |
