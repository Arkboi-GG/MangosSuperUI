# ChannelAnnouncements

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ChannelAnnouncements

**Purpose & Responsibilities**

`ChannelAnnouncements` is a client-side packet structure within the `WorldPackets::Channel` namespace, responsible for representing the `CMSG_CHANNEL_ANNOUNCEMENTS` message sent from the game client to the server. Its sole responsibility is to carry the name of a channel (`channelName`) associated with a request to toggle or query the announcement status for that channel. As a `ClientPacket`, it serves as the data container for deserializing raw network bytes into a structured object that higher-level server logic can process. It contains no business logic, state management, or side effects; it is a pure data transfer object (DTO) for this specific client-to-server command.

**Member-by-Member Behavior**

The unit consists of a single constructor and inherits standard packet handling interfaces.

*   **Construction**: The explicit constructor `ChannelAnnouncements()` initializes the base `ClientPacket` class with the opcode `CMSG_CHANNEL_ANNOUNCEMENTS`. This registration ensures that when the server receives a packet with this opcode, it instantiates this specific class to handle the payload.
*   **Data Storage**: The public member `channelName` stores the string identifier of the channel targeted by the announcement request. This field is populated during the deserialization phase via the inherited `ReadFromWorldPacket` method (implementation not shown in this unit but declared in the base class interface).

**Cross-Unit Boundaries**

*   **Inheritance**: Inherits from `WorldPackets::ClientPacket`. This establishes the contract for packet identification (via opcode) and provides the interface for reading data from the `WorldPacket` buffer.
*   **Namespace Context**: Resides in `WorldPackets::Channel`, grouping it logically with other channel-related client commands such as `JoinChannel`, `LeaveChannel`, and `ChannelModerate`.
*   **No Outbound Calls**: The `ChannelAnnouncements` class itself makes no calls to other units. It is a leaf node in the call graph, serving only as a recipient of data during deserialization.
*   **No Inbound Calls from Other Units**: According to the provided MAP, no other units explicitly call into `ChannelAnnouncements`. In practice, the server's packet dispatcher (not detailed here) would instantiate this class upon receiving the `CMSG_CHANNEL_ANNOUNCEMENTS` opcode, but this interaction is implicit in the framework design rather than an explicit dependency listed in the MAP.

**Data Model**

This unit does not interact directly with any database tables. It operates entirely in memory as part of the network I/O layer. The `channelName` string is transient data derived from the client's network packet and is not persisted by this class.

**Notable Implementation Details**

*   **Final Class**: The class is marked `final`, preventing further inheritance. This enforces a strict hierarchy where `ChannelAnnouncements` is a terminal type in the packet class tree.
*   **Public Data Member**: The `channelName` is a public `std::string`. This design choice allows direct access to the channel name by any code holding an instance of this packet, bypassing getter/setter methods. This is typical for simple DTOs in performance-sensitive game servers to reduce overhead.
*   **Explicit Constructor**: The constructor is marked `explicit` to prevent implicit conversions from `WorldPacket` or other types, ensuring that instantiation is intentional and tied to the specific opcode registration.
*   **No Validation**: The class performs no validation on the `channelName` (e.g., length checks, character restrictions). Such validation is expected to occur in the server-side handler that processes this packet after deserialization.

## Member Reference

**ChannelAnnouncements**
Constructor for the `ChannelAnnouncements` packet. Initializes the base `ClientPacket` with the opcode `CMSG_CHANNEL_ANNOUNCEMENTS`. Does not perform any additional setup or validation.

---

<!-- machine-true, projected from graph.json -->

## Map — ChannelAnnouncements

*Source:* Channel.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ChannelAnnouncements | ctor | — | — | — |
