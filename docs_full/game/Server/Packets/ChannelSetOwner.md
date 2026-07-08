# ChannelSetOwner

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ChannelSetOwner

**Purpose & Responsibilities**

`ChannelSetOwner` is a client-side packet structure within the `WorldPackets::Channel` namespace, defined in `Channel.h`. Its sole responsibility is to represent the `CMSG_CHANNEL_SET_OWNER` message sent from the game client to the server. This message requests that ownership of a specific chat channel be transferred to a designated player. The class encapsulates the raw data payload—specifically the target channel name and the name of the player who should become the new owner—and provides the mechanism to deserialize this data from the incoming network buffer.

As a `ClientPacket`, it serves as the input contract for the server’s channel management logic. It does not contain business logic, validation, or side effects; it is purely a data carrier and deserialization wrapper.

## Member-by-Member Behavior

The unit contains a single member: the constructor.

### Construction and Initialization

The **`ChannelSetOwner`** constructor initializes the packet object. It explicitly invokes the base class `ClientPacket` constructor, passing the constant `CMSG_CHANNEL_SET_OWNER`. This associates the instance with the specific opcode expected by the network layer for routing and identification purposes. The constructor takes no arguments, meaning the `channelName` and `playerName` member variables are default-initialized (empty strings) upon construction. These fields are populated later via the `ReadFromWorldPacket` method (defined in the base class hierarchy or implemented elsewhere, but declared in this header).

## Cross-Unit Boundaries

*   **Calls Out:** None. The constructor performs only local initialization and base class invocation.
*   **Called By:** None listed in the map. In practice, instances of this class are typically constructed by the network handler when a packet with opcode `CMSG_CHANNEL_SET_OWNER` is received, though the specific factory or handler logic resides outside this unit.

## Data Model

This unit does not interact directly with any database tables. It operates entirely on in-memory data structures (`std::string`) derived from the network packet stream.

## Notable Implementation Details

*   **Final Class:** The class is marked `final`, preventing inheritance. This enforces a strict, flat hierarchy for this specific packet type, ensuring no derived classes can alter its memory layout or behavior.
*   **Public Data Members:** The fields `channelName` and `playerName` are public. This design choice allows direct access by the calling code (likely the command handler or channel manager) after deserialization, avoiding the overhead of getter/setter methods for simple data transfer objects.
*   **Deserialization Contract:** While `ReadFromWorldPacket` is declared in this header, its implementation is not shown here. However, the presence of `channelName` and `playerName` implies that the deserialization logic will extract two string values from the `WorldPacket` buffer in a specific order (typically channel name first, then player name, or vice versa, depending on the client protocol specification).

## Member Reference

**ChannelSetOwner**
Constructor for the `ChannelSetOwner` packet. Initializes the base `ClientPacket` with the `CMSG_CHANNEL_SET_OWNER` opcode. Default-initializes the `channelName` and `playerName` string members.

---

<!-- machine-true, projected from graph.json -->

## Map — ChannelSetOwner

*Source:* Channel.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ChannelSetOwner | ctor | — | — | — |
