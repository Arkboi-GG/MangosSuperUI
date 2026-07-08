# PetitionSign

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# PetitionSign

**PetitionSign** is a single-method implementation unit within the `WorldPackets::Petition` namespace, responsible for deserializing the `CMSG_PETITION_SIGN` network packet. This packet is sent by the client when a player attempts to sign a guild or faction petition item. The unit’s sole responsibility is to extract the GUID of the petition item from the binary stream and store it in the object’s public member variable, making it available for subsequent game logic processing (handled by other units).

As a `ClientPacket`, `PetitionSign` inherits the standard packet infrastructure but contains no custom logic beyond the default constructor initialization and the mandatory override of `ReadFromWorldPacket`. It does not perform validation, authorization checks, or database interactions itself; those concerns reside in the handlers that consume this packet object.

## Member-by-Member Behavior

The unit consists of a single member: the constructor and the associated packet reading logic defined in the header.

### **PetitionSign**
This is the constructor for the `PetitionSign` class. It initializes the base `ClientPacket` with the opcode `CMSG_PETITION_SIGN`. This opcode identifies the packet type to the network layer, ensuring it is routed to the correct handler. The constructor does not take arguments and does not interact with any external systems or database tables.

The class also declares `ReadFromWorldPacket`, which is the mechanism for parsing the incoming binary data. Although the implementation of `ReadFromWorldPacket` is not shown in the provided source snippet (it is likely defined in a corresponding `.cpp` file or inherited/templated in a way not visible here), its purpose is strictly to read the `itemGuid` field from the `WorldPacket` buffer. Based on the class definition, the expected payload structure is a single `ObjectGuid` representing the unique identifier of the petition item being signed.

## Cross-Unit Boundaries

*   **Calls out:** None. The `PetitionSign` unit does not call into other units during construction or packet reading.
*   **Called by:** Other units (not listed in the MAP, but implied by the architecture) will instantiate `PetitionSign` when the network layer receives a packet with opcode `CMSG_PETITION_SIGN`. After instantiation, the network framework calls `ReadFromWorldPacket` to populate the `itemGuid`. Subsequently, a game logic handler (likely in a different translation unit, such as a session or world handler) will access the `itemGuid` member to process the signing request.

## Data Model

This unit does not directly interact with any database tables. It operates purely on in-memory network packet data. The `itemGuid` extracted from the packet will eventually be used by downstream logic to query or update tables related to petitions (e.g., `petition_signlist` or `guild` tables), but `PetitionSign` itself performs no SQL operations.

## Notable Implementation Details

*   **Minimalist Design:** The class follows the standard pattern for `WorldPackets` in this codebase: a lightweight data holder with a specific opcode. All complex logic is deferred to the handler that consumes this object.
*   **Public Member Access:** The `itemGuid` is a public member variable. This design choice allows the consuming handler to access the parsed data directly without needing getter methods, adhering to the simple struct-of-data pattern common in packet definitions.
*   **No Validation:** The unit does not validate whether the `itemGuid` is valid, whether the item exists, or whether the player is allowed to sign it. These checks are the responsibility of the business logic layer that processes the packet after it has been deserialized.

## Member Reference

**PetitionSign**
Constructor for the `PetitionSign` packet class. Initializes the base `ClientPacket` with the opcode `CMSG_PETITION_SIGN`. Does not perform any I/O or database operations. The associated `ReadFromWorldPacket` method (declared in the header) is responsible for extracting the `itemGuid` from the incoming network packet.

---

<!-- machine-true, projected from graph.json -->

## Map — PetitionSign

*Source:* Petition.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| PetitionSign | ctor | — | — | — |
