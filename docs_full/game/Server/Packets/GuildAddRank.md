# GuildAddRank

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# GuildAddRank

**GuildAddRank** is a client-side network packet structure within the `WorldPackets::Guild` namespace. It represents the `CMSG_GUILD_ADD_RANK` message, which is transmitted by the game client when a user initiates the creation of a new guild rank. This unit serves as a data container and parser, responsible for extracting the desired rank name from the raw binary stream received over the network.

### Purpose & Responsibilities

The primary responsibility of `GuildAddRank` is to deserialize the `CMSG_GUILD_ADD_RANK` packet into a usable C++ object. When a guild officer or leader uses the client interface to add a new rank, the client sends this specific opcode. The class:

1.  Identifies itself as a `ClientPacket` associated with the opcode `CMSG_GUILD_ADD_RANK`.
2.  Provides a public member variable, `rankName`, to store the string payload contained in the packet.
3.  Declares the `ReadFromWorldPacket` method, which is overridden to implement the specific deserialization logic for this packet type.

It does not perform validation, database insertion, or permission checks. Those responsibilities belong to the server-side handler that processes this packet instance after it has been constructed and populated.

### Member-by-Member Behavior

#### Constructor: `GuildAddRank`
The default constructor initializes the packet object. It invokes the base class constructor `ClientPacket(CMSG_GUILD_ADD_RANK)` to register the correct opcode for this message type. No additional initialization is performed on the member variables.

### Cross-Unit Boundaries

*   **Calls Out:** None. This unit is a leaf node in the call graph regarding external dependencies; it only interacts with its base class `ClientPacket` and the `WorldPacket` utility class for reading data.
*   **Called By:** The MAP indicates no external callers are listed. In practice, this packet is instantiated by the network layer when a `CMSG_GUILD_ADD_RANK` opcode is detected on the socket. The resulting object is then passed to a server-side handler (likely in a `GuildHandler` or similar module) which will inspect `rankName` and proceed with business logic.

### Data Model

This unit does not directly interact with any database tables. It operates purely on in-memory network data. The `rankName` extracted here will eventually be persisted to the `guild_rank` table (or equivalent) by downstream handlers, but `GuildAddRank` itself performs no SQL operations.

### Notable Implementation Details

*   **Namespace:** The class resides in `WorldPackets::Guild`, indicating it is part of the structured packet handling system used in the Mangos core.
*   **String Handling:** The `rankName` is stored as a `std::string`. Downstream consumers must ensure the string is not empty or exceeds maximum length limits allowed by the database or client, as this unit performs no such validation.
*   **Final Class:** The class is marked `final`, preventing inheritance. This ensures the packet structure remains stable and predictable.

## Member Reference

**GuildAddRank**
Constructor that initializes the packet with the `CMSG_GUILD_ADD_RANK` opcode.

---

<!-- machine-true, projected from graph.json -->

## Map — GuildAddRank

*Source:* Guild.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GuildAddRank | ctor | — | — | — |
