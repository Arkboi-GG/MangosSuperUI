# GuildQuery

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`GuildQuery` is a client-to-server packet structure within the `WorldPackets::Guild` namespace, responsible for encapsulating the data required to request guild information from the server. Specifically, it represents the `CMSG_GUILD_QUERY` message type. Its sole responsibility is to hold the `guildId` associated with the query request, which is populated when the raw network packet is deserialized. This unit does not perform any business logic, database access, or network transmission; it is purely a data container for the incoming request payload.

## Member-by-Member Behavior

The unit contains a single member: the constructor.

**`GuildQuery` (Constructor)**
The default constructor initializes the packet instance. It sets the internal packet opcode to `CMSG_GUILD_QUERY` via the base class `ClientPacket` constructor. It also initializes the public member variable `guildId` to `0`. This ensures that if the packet is instantiated but not yet populated from network data, the `guildId` holds a safe, neutral value.

## Cross-Unit Boundaries

*   **Calls Out:** None. The constructor does not invoke any functions in other units.
*   **Called By:** None listed in the map. In practice, this constructor is likely called by the network layer or packet factory when a `CMSG_GUILD_QUERY` opcode is detected on the wire, but these callers are outside the scope of the provided map.

## Data Model

This unit does not interact directly with any database tables. It operates solely on in-memory data structures derived from network packets.

## Notable Implementation Details

*   **Inheritance:** `GuildQuery` inherits from `ClientPacket`, indicating it is part of the client-to-server communication protocol.
*   **Opcode Association:** The constructor explicitly binds this class to the `CMSG_GUILD_QUERY` opcode, ensuring correct routing within the server's packet handling system.
*   **Default Initialization:** The `guildId` member is initialized to `0` in the class definition. This is a defensive measure, though the actual value will be overwritten by `ReadFromWorldPacket` (defined in the base class or implemented elsewhere, but not part of this specific unit's exposed interface in the map) when the packet is received.

## Member Reference

**GuildQuery**
The default constructor for the `GuildQuery` packet. It initializes the base `ClientPacket` with the opcode `CMSG_GUILD_QUERY` and sets the `guildId` member to `0`.

---

<!-- machine-true, projected from graph.json -->

## Map — GuildQuery

*Source:* Guild.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GuildQuery | ctor | — | — | — |
