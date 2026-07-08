# GuildSetPublicNote

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# GuildSetPublicNote

## Purpose & Responsibilities

`GuildSetPublicNote` is a client-side packet structure within the `WorldPackets::Guild` namespace, responsible for representing the `CMSG_GUILD_SET_PUBLIC_NOTE` message sent from a game client to the server. Its sole responsibility is to deserialize the raw binary data of this specific network message into structured fields: the name of the target player and the text of the public note to be assigned to them. It acts as a data carrier, holding the payload until the server-side handler processes the request.

## Member-by-Member Behavior

The unit consists of a single constructor and two public data members.

### Construction and Initialization
The **`GuildSetPublicNote`** constructor initializes the packet object. It explicitly calls the base class `ClientPacket` constructor, passing the constant `CMSG_GUILD_SET_PUBLIC_NOTE`. This registers the packet type with the networking layer, ensuring that incoming messages with this opcode are routed to instances of this class for deserialization. The constructor does not initialize the string members `playerName` or `note`; these remain empty until populated by the `ReadFromWorldPacket` method (which is declared in the header but implemented elsewhere, likely in a corresponding `.cpp` file not included in this unit's scope, though the MAP indicates no external calls for the constructor itself).

### Data Members
*   **`playerName`**: A `std::string` that holds the name of the guild member to whom the public note will be applied.
*   **`note`**: A `std::string` that holds the content of the public note itself.

These members are public, allowing direct access by the server logic that consumes this packet after deserialization.

## Cross-Unit Boundaries

*   **Calls Out**: The constructor calls the base class `ClientPacket` constructor. This establishes the inheritance hierarchy and associates the packet with the correct network opcode (`CMSG_GUILD_SET_PUBLIC_NOTE`). No other external units are called by the members listed in the MAP.
*   **Called By**: According to the MAP, no other units explicitly call this constructor or its members in the context of cross-unit dependencies tracked here. In practice, the networking subsystem instantiates this class when it receives a packet with the matching opcode, and then invokes `ReadFromWorldPacket` (not listed in the MAP as a member of *this* unit's behavioral scope for cross-boundary analysis, but implied by the class definition) to fill the data members.

## Data Model

This unit does not interact directly with any database tables. It operates purely on network packet data. Any persistence of the public note to the database would occur in subsequent server-side logic that consumes this packet, not within this packet structure itself.

## Notable Implementation Details

*   **Opcode Association**: The class is tightly coupled to the specific opcode `CMSG_GUILD_SET_PUBLIC_NOTE`. Changing this opcode in the client-server protocol would require updating this constructor.
*   **String Handling**: The use of `std::string` for both `playerName` and `note` implies that the deserialization logic (in `ReadFromWorldPacket`) must handle variable-length string extraction correctly, including null-termination or length-prefix parsing, depending on the client version's protocol specifics.
*   **No Validation**: This packet structure performs no validation on the input data. It simply stores whatever strings are extracted from the network stream. Validation of whether the player exists, whether the sender has permission to set notes, and whether the note length is acceptable, must be performed by the server handler that processes this packet.

## Member Reference

**GuildSetPublicNote**
Constructor for the `GuildSetPublicNote` packet. Initializes the base `ClientPacket` with the opcode `CMSG_GUILD_SET_PUBLIC_NOTE`. Does not initialize the `playerName` or `note` members.

---

<!-- machine-true, projected from graph.json -->

## Map — GuildSetPublicNote

*Source:* Guild.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GuildSetPublicNote | ctor | — | — | — |
