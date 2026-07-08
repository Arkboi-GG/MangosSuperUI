# UnlearnSkill

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# UnlearnSkill

**Purpose & Responsibilities**

`UnlearnSkill` is a data structure within the `WorldPackets::Skill` namespace that represents a client-to-server network message (`CMSG_UNLEARN_SKILL`). Its sole responsibility is to encapsulate the raw data received from a client when a player attempts to unlearn a specific skill. It acts as a passive container, holding the `skillId` until the packet processing pipeline invokes its deserialization logic.

This unit is part of the broader packet handling system in Mangos, where `ClientPacket` derivatives define the schema for incoming messages. `UnlearnSkill` does not perform validation, business logic, or database interactions itself; it strictly defines the memory layout and deserialization interface for this specific command.

## Member-by-Member Behavior

The unit contains a single member: the constructor.

### Construction and Initialization

The **UnlearnSkill** constructor initializes the packet object. It performs two key actions:
1.  **Base Class Initialization**: It calls the `ClientPacket` constructor with the constant `CMSG_UNLEARN_SKILL`. This registers the packet type with the network handler, ensuring that when the server receives a packet with this opcode, it instantiates an `UnlearnSkill` object to process it.
2.  **Member Initialization**: The member variable `skillId` is initialized to `0` via in-class initialization (`uint32 skillId = 0;`). This ensures the ID is zeroed out before deserialization occurs, preventing garbage values if the read operation fails or is skipped.

## Cross-Unit Boundaries

*   **Calls Out**: None. The constructor does not invoke any other units.
*   **Called By**: None explicitly listed in the map, but implicitly, this constructor is called by the packet dispatching mechanism (likely within `WorldSession` or a central packet router) when a `CMSG_UNLEARN_SKILL` opcode is detected on the wire.

## Data Model

This unit does not interact directly with any database tables. It operates purely on network packet data. The `skillId` it holds will eventually be used by downstream handlers (not part of this unit) to query or update character skill records, but `UnlearnSkill` itself has no SQL queries or table dependencies.

## Notable Implementation Details

*   **Inheritance**: It inherits from `ClientPacket`, implying it implements the virtual method `ReadFromWorldPacket` (declared in the header but defined elsewhere, likely in a corresponding `.cpp` file not provided here, or potentially inline in a different partial). The provided header shows the declaration `void ReadFromWorldPacket(WorldPacket& recv_data) override;`, indicating that the actual logic for extracting `skillId` from the binary stream resides outside this specific header definition or in the implementation file associated with this class.
*   **Final Class**: The class is marked `final`, preventing further inheritance. This is a common pattern for packet structures to ensure strict control over the serialization/deserialization contract.
*   **Namespace**: It resides in `WorldPackets::Skill`, grouping it logically with other skill-related network messages like `LearnTalent` and `TalentWipeConfirm`.

## Member Reference

**UnlearnSkill**
Constructor for the `UnlearnSkill` packet. Initializes the base `ClientPacket` with the opcode `CMSG_UNLEARN_SKILL` and sets the `skillId` member to `0`. This prepares the object to receive and store the skill identifier from the incoming network stream.

---

<!-- machine-true, projected from graph.json -->

## Map — UnlearnSkill

*Source:* Skill.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| UnlearnSkill | ctor | — | — | — |
