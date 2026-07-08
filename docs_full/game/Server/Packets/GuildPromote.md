# GuildPromote

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# GuildPromote

## Purpose & Responsibilities

`GuildPromote` is a client-side packet structure within the `WorldPackets::Guild` namespace, defined in `Guild.h`. Its sole responsibility is to represent the `CMSG_GUILD_PROMOTE` message sent from the game client to the server. This message indicates that a player intends to promote another guild member to a higher rank.

The class acts as a data container, holding the name of the target player (`playerName`) who is to be promoted. It inherits from `ClientPacket`, establishing its role as an incoming network message that must be parsed from the raw binary stream provided by the world server.

## Member-by-Member Behavior

### Construction and Initialization
**GuildPromote**
The constructor initializes the packet object. It explicitly calls the base class constructor `ClientPacket(CMSG_GUILD_PROMOTE)`, binding this specific instance to the opcode `CMSG_GUILD_PROMOTE`. This ensures that when the packet is processed by the server's message handler, it is routed to the correct logic for handling guild promotions. The member variable `playerName` is default-initialized to an empty string by the compiler, pending population via deserialization.

### Deserialization
Although not listed as a separate callable member in the MAP (as it is a virtual override inherited from the interface), the class declares `void ReadFromWorldPacket(WorldPacket& recv_data) override`. This method is responsible for extracting the `playerName` string from the incoming `WorldPacket` buffer. The implementation of this method resides in the corresponding `.cpp` file (not provided in the source snippet but implied by the declaration), where it will read the string field according to the protocol definition for `CMSG_GUILD_PROMOTE`.

## Cross-Unit Boundaries

*   **Calls Out:** None. The MAP indicates no outgoing calls to other units. The class is purely a data structure with a declared parsing interface.
*   **Called By:** None. The MAP indicates no incoming calls from other units. In practice, instances of this class are typically constructed by the server's network layer upon receiving the `CMSG_GUILD_PROMOTE` opcode, after which the populated data is passed to guild management logic (likely in a `GuildMgr` or similar unit, though not shown in the MAP).

## Data Model

This unit does not interact directly with any database tables. It operates entirely in memory as part of the network packet processing pipeline. Any persistence of the promotion action would occur downstream in the guild management system, which is outside the scope of this unit.

## Notable Implementation Details

*   **Inheritance:** Inherits from `ClientPacket`, marking it as a message originating from the client.
*   **Opcode Binding:** The constructor hardcodes the association with `CMSG_GUILD_PROMOTE`. This opcode is critical for the server's dispatch mechanism to identify the intent of the message.
*   **Data Field:** Contains a single `std::string playerName`. This implies the protocol identifies the target of the promotion by name rather than by GUID or ID, requiring the server to resolve the name to a player object before executing the promotion.
*   **Final Class:** The class is marked `final`, preventing further inheritance. This is appropriate for a leaf-node packet structure.

## Member Reference

**GuildPromote**
Constructor that initializes the `GuildPromote` packet. It calls the base `ClientPacket` constructor with the opcode `CMSG_GUILD_PROMOTE`, registering this packet type with the network handler. It prepares the object to receive the `playerName` field during deserialization.

---

<!-- machine-true, projected from graph.json -->

## Map — GuildPromote

*Source:* Guild.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GuildPromote | ctor | — | — | — |
