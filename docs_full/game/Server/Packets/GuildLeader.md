# GuildLeader

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# GuildLeader

**Purpose & Responsibilities**

`GuildLeader` is a client-side network packet structure within the `WorldPackets::Guild` namespace. Its sole responsibility is to represent the `CMSG_GUILD_LEADER` message sent from the game client to the server. This message indicates that a player has requested to transfer guild leadership to another specific player. The class encapsulates the raw data payload of this request—specifically, the name of the player designated to become the new leader—and provides the mechanism to deserialize this data from the incoming network stream.

As a `ClientPacket`, it serves as a data carrier. It does not contain business logic for validating the request, checking permissions, or updating the database. Those responsibilities lie with the handler that processes this packet after it has been constructed and populated.

## Member-by-Member Behavior

The unit contains a single member: the constructor.

### **GuildLeader**
This is the default constructor for the `GuildLeader` class. It performs two initialization tasks:
1.  It initializes the base class `ClientPacket` with the opcode `CMSG_GUILD_LEADER`. This opcode identifies the type of message to the network layer and the packet dispatcher.
2.  It leaves the `playerName` member uninitialized (default-initialized to an empty string), awaiting population via the `ReadFromWorldPacket` method.

## Cross-Unit Boundaries

*   **Calls Out:** None. The constructor does not invoke any other units.
*   **Called By:** None listed in the map. In practice, this constructor is invoked by the network input handling system (likely within the core session or packet parsing logic outside this unit) when a packet with the `CMSG_GUILD_LEADER` opcode is received from the client.

## Data Model

This unit does not interact directly with any database tables. It operates entirely on in-memory network data. The `playerName` field is a transient string extracted from the client packet. Any persistence of the leadership change occurs in downstream handlers, not within this class.

## Notable Implementation Details

*   **Inheritance:** `GuildLeader` inherits from `ClientPacket`. This implies it shares common functionality for packet identification and potentially logging or validation hooks defined in the base class.
*   **Data Field:** The `std::string playerName` is public, allowing direct access by the handler that processes the packet. This design choice simplifies the handler code but exposes the internal state of the packet object.
*   **Opcode Specificity:** The class is tightly coupled to the `CMSG_GUILD_LEADER` opcode. It cannot be reused for other guild-related messages, adhering to the one-class-per-packet-type pattern seen in the surrounding `Guild.h` header.

## Member Reference

**GuildLeader**
Constructor for the `GuildLeader` packet. Initializes the base `ClientPacket` with the `CMSG_GUILD_LEADER` opcode. Does not perform any deserialization; that is handled by `ReadFromWorldPacket`.

---

<!-- machine-true, projected from graph.json -->

## Map — GuildLeader

*Source:* Guild.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GuildLeader | ctor | — | — | — |
