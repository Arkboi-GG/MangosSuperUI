# QuestgiverStatusQuery

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# QuestgiverStatusQuery

**Purpose & Responsibilities**

`QuestgiverStatusQuery` is a lightweight data structure representing a specific client-to-server network message (`CMSG_QUESTGIVER_STATUS_QUERY`) within the `WorldPackets::Quest` namespace. Its sole responsibility is to encapsulate the raw data received from a client when that client requests status information regarding a specific quest giver entity. It holds the `ObjectGuid` of the target entity but contains no business logic, validation, or processing capabilities itself. It serves as a container for deserialization, allowing higher-level game logic handlers to access the target identifier after the packet has been parsed.

**Member-by-Member Behavior**

The unit consists of a single member: the constructor.

*   **Construction**: The default constructor initializes the base class `ClientPacket` with the opcode `CMSG_QUESTGIVER_STATUS_QUERY`. This registration allows the network layer to identify incoming packets of this type and instantiate the correct handler. The member variable `guid` is implicitly default-initialized (to an invalid/empty GUID) until populated by the `ReadFromWorldPacket` method (which is declared in the header but implemented elsewhere, likely in a corresponding `.cpp` file or via template specialization not shown in this unit).

**Cross-Unit Boundaries**

*   **Calls Out**: None. The constructor performs no external calls.
*   **Called By**: None listed in the map. In practice, this class is instantiated by the network packet parsing infrastructure when a client sends the `CMSG_QUESTGIVER_STATUS_QUERY` opcode. The resulting object is then passed to a handler function (not part of this unit) that interprets the `guid` to determine which NPC or creature the player is interacting with.

**Data Model**

This unit does not interact with any database tables. It operates entirely in memory as part of the network packet handling pipeline.

**Notable Implementation Details**

*   **Final Class**: The class is marked `final`, preventing inheritance. This ensures the packet structure remains stable and predictable for the network layer.
*   **Minimal State**: The class contains only one data member, `ObjectGuid guid`. This reflects the simplicity of the protocol: the client only needs to specify *who* it is asking about. All other context (player location, permissions, quest availability) is determined server-side using this GUID.
*   **Base Class Dependency**: It inherits from `ClientPacket`, implying it relies on the base class for common packet metadata (such as sequence numbers or source identification) and the interface for reading binary data (`ReadFromWorldPacket`).

## Member Reference

**QuestgiverStatusQuery**
Constructor for the `QuestgiverStatusQuery` packet. Initializes the base `ClientPacket` with the opcode `CMSG_QUESTGIVER_STATUS_QUERY`. Does not perform any external calls or database interactions.

---

<!-- machine-true, projected from graph.json -->

## Map — QuestgiverStatusQuery

*Source:* Quest.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| QuestgiverStatusQuery | ctor | — | — | — |
