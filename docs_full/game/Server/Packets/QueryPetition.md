# QueryPetition

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# QueryPetition (`WorldPackets::Petition::QueryPetition`)

## Purpose & Responsibilities

`QueryPetition` is a lightweight data structure representing a specific client-to-server network message: `CMSG_PETITION_QUERY`. It resides within the `WorldPackets::Petition` namespace, which groups all packet structures related to the game's faction/guild petition system.

The class serves two primary roles:
1.  **Data Container:** It holds the raw data extracted from the incoming network packet, specifically the `petitionGuid` (a 32-bit unsigned integer) and the `itemGuid` (an `ObjectGuid` representing the physical petition item in the player's inventory).
2.  **Packet Deserialization Interface:** It inherits from `ClientPacket`, providing the mechanism (`ReadFromWorldPacket`) to parse binary data from the network stream into these structured fields.

This unit does not contain business logic, validation, or database interaction. It is purely a transport layer object. The actual handling of the petition query (validating ownership, checking signature counts, etc.) occurs in other units that receive an instance of this class after deserialization.

## Member-by-Member Behavior

### Construction and Initialization

**`QueryPetition()`**
The constructor initializes the packet structure. It sets the internal packet opcode to `CMSG_PETITION_QUERY` by calling the base class `ClientPacket` constructor. It also initializes the member variables:
*   `petitionGuid` is set to `0`.
*   `itemGuid` is default-initialized (typically an invalid/empty GUID).

This ensures that if the packet reading fails or is incomplete, the members hold safe default values rather than uninitialized memory.

### Data Parsing

**`ReadFromWorldPacket(WorldPacket& recv_data)`**
*(Note: While declared in the header, the implementation is not provided in the source snippet. However, based on standard patterns in this codebase and the member variables, its behavior is inferred as follows)*

This virtual method overrides the base class interface to define how binary data is extracted from the `recv_data` stream. It typically performs the following operations in order:
1.  Reads a `uint32` value from the packet stream and assigns it to `petitionGuid`.
2.  Reads an `ObjectGuid` structure from the packet stream and assigns it to `itemGuid`.

If the packet is malformed or too short, this method may throw an exception or return early, depending on the base class implementation details not shown here.

## Cross-Unit Boundaries

### Called By (Other Units)

Although the MAP indicates no explicit "Called by" entries for this specific unit, in practice, instances of `QueryPetition` are created and populated by the network handler subsystem. Specifically:
*   **Network Handler / Packet Dispatcher:** When the server receives a raw byte stream with the opcode `CMSG_PETITION_QUERY`, the network layer instantiates `QueryPetition`, calls `ReadFromWorldPacket`, and then passes the populated object to the game logic handler (likely in a unit like `PetitionHandler.cpp` or similar, though not listed in the MAP).

### Calls Out (Other Units)

*   **`ClientPacket`:** The constructor calls `ClientPacket(CMSG_PETITION_QUERY)` to register the packet type.
*   **`ObjectGuid`:** The member `itemGuid` relies on the `ObjectGuid` class for storage and parsing logic during `ReadFromWorldPacket`.

## Data Model

This unit does not interact directly with any database tables. It operates solely on in-memory data received from the client. The `petitionGuid` and `itemGuid` fields correspond to identifiers that *will* be used by downstream handlers to query tables such as `character_petition` or `item_instance`, but `QueryPetition` itself performs no SQL operations.

## Notable Implementation Details

*   **Final Class:** The class is marked `final`, indicating it cannot be subclassed. This enforces a strict contract for this specific packet type.
*   **Default Initialization:** The explicit initialization of `petitionGuid = 0` in the class definition is a safety measure. Since `ReadFromWorldPacket` is called after construction, ensuring a zero-value default prevents undefined behavior if the parsing step is skipped or fails silently.
*   **Namespace Organization:** Being part of `WorldPackets::Petition` clearly segregates petition-related network traffic from other game systems, aiding in maintainability and preventing opcode collisions.

## Member Reference

**`QueryPetition`**
Constructor for the `QueryPetition` packet. Initializes the base `ClientPacket` with opcode `CMSG_PETITION_QUERY` and sets `petitionGuid` to 0. Default-initializes `itemGuid`.

---

<!-- machine-true, projected from graph.json -->

## Map — QueryPetition

*Source:* Petition.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| QueryPetition | ctor | — | — | — |
