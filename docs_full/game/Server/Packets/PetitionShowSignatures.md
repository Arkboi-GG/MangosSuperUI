# PetitionShowSignatures

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`PetitionShowSignatures` is a data structure within the `WorldPackets::Petition` namespace that represents a specific client-to-server network message: `CMSG_PETITION_SHOW_SIGNATURES`. Its sole responsibility is to encapsulate the payload associated with this opcode, specifically the `ObjectGuid` of the petition item whose signatures the client wishes to view. As a `ClientPacket`, it serves as the input container for the server's packet handling logic, providing a typed interface to extract the item identifier from the raw binary stream received from the client.

This unit is part of the larger petition system, which manages guild creation or similar administrative actions requiring player signatures. `PetitionShowSignatures` handles the specific step where a player requests to see who has already signed a particular petition item.

## Member-by-Member Behavior

The unit contains only one member: the constructor.

**PetitionShowSignatures**
The default constructor initializes the `PetitionShowSignatures` object. It explicitly invokes the base class constructor `ClientPacket`, passing the constant `CMSG_PETITION_SHOW_SIGNATURES` as the opcode. This registration ensures that when the server receives a packet with this opcode, it can correctly instantiate this type for processing. The member variable `itemGuid` is default-initialized to an empty/null `ObjectGuid` by the compiler, pending population via `ReadFromWorldPacket` (which is declared in the header but implemented elsewhere, likely in a corresponding `.cpp` file not included in this unit's scope, or potentially inline in a different partial).

## Cross-Unit Boundaries

*   **Calls Out:** None. The constructor only calls the base class constructor.
*   **Called By:** None listed in the map. However, by design, instances of this class are created by the packet parsing infrastructure (likely in `WorldSession` or a central packet router) when a client sends the `CMSG_PETITION_SHOW_SIGNATURES` opcode. The parser will then call `ReadFromWorldPacket` to populate `itemGuid`.

## Data Model

This unit does not interact directly with any database tables. It operates entirely on in-memory network packet data. The `itemGuid` it carries may later be used by higher-level game logic to query database tables related to petitions (such as `petition_sign` or `petition`), but `PetitionShowSignatures` itself performs no SQL operations.

## Notable Implementation Details

*   **Final Class:** The class is marked `final`, preventing inheritance. This is appropriate for a leaf-node packet structure.
*   **Namespace:** It resides in `WorldPackets::Petition`, indicating a modular organization of network packets by feature domain.
*   **Dependency:** It relies on `ObjectGuid` for identifying the petition item and `ClientPacket` for the base networking functionality.
*   **Missing Implementation Context:** While `ReadFromWorldPacket` is declared, its implementation is not present in the provided source. Typically, this method would read the `itemGuid` from the `WorldPacket` buffer. Engineers maintaining this unit should ensure that the implementation of `ReadFromWorldPacket` correctly deserializes the `ObjectGuid` format expected by the client version supported by this server build.

## Member Reference

**PetitionShowSignatures**
Constructor for the `PetitionShowSignatures` packet. Initializes the base `ClientPacket` with the opcode `CMSG_PETITION_SHOW_SIGNATURES`. Default-initializes the `itemGuid` member.

---

<!-- machine-true, projected from graph.json -->

## Map — PetitionShowSignatures

*Source:* Petition.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| PetitionShowSignatures | ctor | — | — | — |
