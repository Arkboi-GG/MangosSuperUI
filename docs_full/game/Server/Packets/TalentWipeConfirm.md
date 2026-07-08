# TalentWipeConfirm

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# TalentWipeConfirm

**Purpose & Responsibilities**

`TalentWipeConfirm` is a lightweight data structure within the `WorldPackets::Skill` namespace, defined in `Skill.h`. It represents a specific client-to-server network message identified by the opcode `MSG_TALENT_WIPE_CONFIRM`. Its sole responsibility is to encapsulate the raw data received from a client requesting confirmation for a talent wipe operation. Specifically, it stores the `ObjectGuid` of the entity involved in this request. As a `ClientPacket`, it serves as the input payload for the server's network handling layer before the request is processed by higher-level game logic.

**Member-by-Member Behavior**

The unit consists of a single constructor and one public data member, alongside an inherited virtual method declaration.

*   **Construction**: The default constructor initializes the base `ClientPacket` with the opcode `MSG_TALENT_WIPE_CONFIRM`. This registration ensures that when the network layer receives a packet with this opcode, it instantiates this specific class to parse the contents.
*   **Data Storage**: The class holds a single `ObjectGuid` member named `guid`. This field is populated by the `ReadFromWorldPacket` method (inherited from `ClientPacket`) when the packet is deserialized from the network stream.
*   **Deserialization**: While the declaration `void ReadFromWorldPacket(WorldPacket& recv_data) override` is present in the class interface, the implementation is not provided in this source file. Based on standard patterns in this codebase, this method would extract the `guid` from the incoming `WorldPacket` buffer.

**Cross-Unit Boundaries**

*   **Inheritance**: `TalentWipeConfirm` inherits from `ClientPacket` (defined in `Packet.h`). This establishes the contract for network packet handling, including the opcode assignment and the requirement to implement `ReadFromWorldPacket`.
*   **Dependencies**: It utilizes `ObjectGuid` (defined in `ObjectGuid.h`) to represent the unique identifier of the game object or player associated with the talent wipe request.
*   **Isolation**: According to the provided MAP, this unit has no outgoing calls to other units and is not explicitly called by other units in the tracked cross-file dependencies. It acts as a passive data carrier instantiated by the network dispatcher.

**Data Model**

This unit does not interact directly with any database tables. It operates entirely in memory as part of the network packet processing pipeline.

**Notable Implementation Details**

*   **Final Class**: The class is marked `final`, preventing further inheritance. This indicates that `TalentWipeConfirm` is a leaf node in the packet hierarchy and should not be subclassed.
*   **Minimal State**: The class contains only one data member (`guid`). This suggests that the talent wipe confirmation protocol relies solely on identifying the target entity via its GUID, with no additional parameters (such as reason codes or secondary identifiers) transmitted in this specific packet.
*   **Default Initialization**: The constructor explicitly sets the base packet opcode but does not initialize the `guid` member. The `guid` is expected to be filled exclusively during the `ReadFromWorldPacket` phase. If the packet is malformed or truncated, the `guid` may remain in an undefined state until validation occurs downstream.

## Member Reference

**TalentWipeConfirm**
Constructor for the `TalentWipeConfirm` packet. Initializes the base `ClientPacket` with the opcode `MSG_TALENT_WIPE_CONFIRM`. It does not initialize the `guid` member, leaving that task to the deserialization process.

---

<!-- machine-true, projected from graph.json -->

## Map — TalentWipeConfirm

*Source:* Skill.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| TalentWipeConfirm | ctor | — | — | — |
