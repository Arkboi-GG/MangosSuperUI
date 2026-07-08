# BankerActivate

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# BankerActivate

## Purpose & Responsibilities

`BankerActivate` is a client-to-server packet structure within the `WorldPackets::Npc` namespace. It represents the network message sent by a client when a player interacts with an NPC designated as a banker to open the bank interface. The class encapsulates the unique identifier (`guid`) of the target NPC and provides the mechanism to deserialize this data from the raw network stream. As a leaf class in the packet hierarchy, it contains no behavioral logic beyond construction and deserialization; its sole responsibility is to faithfully represent the binary protocol for the `CMSG_BANKER_ACTIVATE` opcode.

## Member-by-Member Behavior

The unit consists of a single member: the constructor.

*   **Construction**: The default constructor initializes the base `ClientPacket` with the specific opcode `CMSG_BANKER_ACTIVATE`. This registration ensures that when the server receives a packet with this opcode, it can correctly instantiate a `BankerActivate` object for processing. The `guid` member is left uninitialized by the constructor, as it is populated exclusively during the deserialization phase via `ReadFromWorldPacket`.

## Cross-Unit Boundaries

*   **Calls Out**: None. The constructor performs only base-class initialization.
*   **Called By**: None listed in the map. In practice, this class is instantiated by the server's packet dispatching infrastructure when a `CMSG_BANKER_ACTIVATE` message arrives on the wire. The `ReadFromWorldPacket` method (declared in the header but not detailed in the map as a distinct "member" for this documentation scope, though part of the class definition) is called by the networking layer to populate the `guid` field.

## Data Model

This unit does not interact directly with any database tables. It operates entirely on in-memory network data structures.

## Notable Implementation Details

*   **Inheritance**: `BankerActivate` inherits from `ClientPacket`, implying it follows the standard pattern for incoming client messages in the Mangos architecture.
*   **Final Class**: The class is marked `final`, preventing further inheritance. This is consistent with its role as a concrete data carrier for a specific protocol message.
*   **Guid Storage**: The `guid` is stored as an `ObjectGuid` type, which is the standard representation for entity identifiers in this codebase. This allows the server to quickly resolve which NPC was targeted by the client.

## Member Reference

**BankerActivate**
Constructor for the `BankerActivate` packet. Initializes the base `ClientPacket` with the opcode `CMSG_BANKER_ACTIVATE`. Does not initialize the `guid` member, which is filled later during packet reading.

---

<!-- machine-true, projected from graph.json -->

## Map — BankerActivate

*Source:* Npc.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| BankerActivate | ctor | — | — | — |
