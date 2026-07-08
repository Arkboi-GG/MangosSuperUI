# GroupUninviteGuid

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`GroupUninviteGuid` is a client-side network packet structure within the `WorldPackets::Group` namespace. Its sole responsibility is to represent the `CMSG_GROUP_UNINVITE_GUID` message sent by the game client to the server. This packet carries the `ObjectGuid` of a player whom the sender wishes to remove from their current group or raid.

As a `ClientPacket`, it serves as the data container for deserialization logic. It does not contain business logic for performing the uninvite; it only defines the memory layout and initialization required to receive the raw bytes from the network and expose them as a strongly-typed C++ object for downstream handlers (typically in the world server logic, such as `ChatHandler` or `Group` management classes, though those callers are outside this unit's scope).

## Member-by-Member Behavior

This unit contains a single member: the constructor.

### Initialization
The **`GroupUninviteGuid`** constructor initializes the base class `ClientPacket` with the specific opcode `CMSG_GROUP_UNINVITE_GUID`. This registration ensures that when the network layer receives a packet with this opcode, it instantiates this specific class type for processing. The member variable `guid` is default-initialized to an empty/null `ObjectGuid` by its class definition, awaiting population by the `ReadFromWorldPacket` method (which is declared in the header but implemented elsewhere, likely in a corresponding `.cpp` file not included in this unit's source view, or potentially inline in a different context; however, based strictly on the provided source, only the declaration exists here).

## Cross-Unit Boundaries

*   **Calls Out:** None. The constructor only invokes the base class constructor.
*   **Called By:** None listed in the MAP. In practice, this class is instantiated by the network packet dispatcher when a `CMSG_GROUP_UNINVITE_GUID` packet arrives on the wire. The dispatcher is part of the core networking infrastructure (`WorldSession` or similar), which is outside this unit's direct dependency graph as defined by the MAP.

## Data Model

This unit does not interact with any database tables. It operates entirely in memory as part of the network I/O layer.

## Notable Implementation Details

*   **GUID vs. Name:** Unlike older packet structures (e.g., `GroupUninvite` which uses `std::string memberName`), `GroupUninviteGuid` uses `ObjectGuid`. This reflects a shift in the game protocol towards using unique identifiers rather than names for entity resolution, which is more robust against name changes or duplicates.
*   **Final Class:** The class is marked `final`, preventing inheritance. This is appropriate for a leaf-node packet structure that has no need for polymorphic behavior.
*   **Namespace:** It resides in `WorldPackets::Group`, indicating it is part of a modularized packet handling system where group-related messages are grouped together for organizational clarity.

## Member Reference

**GroupUninviteGuid**
Constructor for the `GroupUninviteGuid` packet. Initializes the base `ClientPacket` with the opcode `CMSG_GROUP_UNINVITE_GUID`. No additional initialization is performed in this unit; the `guid` member is left to its default state until populated by the reading logic.

---

<!-- machine-true, projected from graph.json -->

## Map — GroupUninviteGuid

*Source:* Group.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GroupUninviteGuid | ctor | — | — | — |
