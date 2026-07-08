# GuildDemote

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# GuildDemote

## Purpose & Responsibilities

`GuildDemote` is a client-side packet structure within the `WorldPackets::Guild` namespace, responsible for representing the `CMSG_GUILD_DEMOTE` message sent by a game client to the server. Its sole responsibility is to deserialize the raw network data into a structured format containing the name of the player being demoted from their current guild rank. It acts as a data carrier, holding the `playerName` string until the server-side handler processes the request.

This unit is part of the broader guild management subsystem, specifically handling rank adjustments. It does not contain logic for validation, permission checking, or database updates; those responsibilities lie outside this unit, in the server handlers that consume this packet.

## Member-by-Member Behavior

The unit consists of a single constructor and one implicit data member (`playerName`) declared in the header.

### Construction and Initialization

**`GuildDemote()`**
The default constructor initializes the packet object. It calls the base class `ClientPacket` constructor, passing the opcode `CMSG_GUILD_DEMOTE`. This registration ensures that when the server receives a packet with this specific opcode, it can instantiate the correct `GuildDemote` object to parse the payload. The `playerName` member is implicitly initialized to an empty string by default, though it will be overwritten during the deserialization process.

### Data Deserialization

While not explicitly listed as a separate "member" in the MAP due to its virtual nature inherited from `ClientPacket`, the behavior of `ReadFromWorldPacket` is critical to this unit's function. Although the implementation is not provided in the source snippet, the signature indicates that this method will extract the `playerName` string from the incoming `WorldPacket` buffer. Based on the pattern seen in sibling classes like `GuildPromote` and `GuildRemove`, this likely involves reading a fixed-length or null-terminated string from the network stream.

## Cross-Unit Boundaries

*   **Calls Out:** None. The `GuildDemote` constructor does not invoke any other units.
*   **Called By:** None are listed in the MAP. In practice, this class is instantiated by the packet dispatching system (likely within `WorldSession` or a similar network handler) when a `CMSG_GUILD_DEMOTE` opcode is detected. The server-side handler then accesses the `playerName` field to execute the demotion logic.

## Data Model

This unit does not interact directly with any database tables. It operates purely on in-memory network data. Any persistence of the demotion action (updating guild ranks in the database) is handled by downstream server logic after this packet has been parsed.

## Notable Implementation Details

*   **String Handling:** The `playerName` is stored as a `std::string`. Care must be taken in the corresponding `ReadFromWorldPacket` implementation (not shown but implied) to handle potential malformed input or excessive string lengths from malicious clients, although this is typically enforced by the base `WorldPacket` reading utilities.
*   **Opcode Specificity:** The class is tightly coupled to the `CMSG_GUILD_DEMOTE` opcode. Changing this opcode in the client-server protocol would require updating this constructor.
*   **Namespace Organization:** It resides in `WorldPackets::Guild`, indicating a modular design where all guild-related network messages are grouped together for maintainability.

## Member Reference

**GuildDemote**
Constructor that initializes the `GuildDemote` packet object. It invokes the base `ClientPacket` constructor with the `CMSG_GUILD_DEMOTE` opcode to register the packet type for network dispatching. The `playerName` member is ready to be populated via the `ReadFromWorldPacket` method during deserialization.

---

<!-- machine-true, projected from graph.json -->

## Map — GuildDemote

*Source:* Guild.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GuildDemote | ctor | — | — | — |
