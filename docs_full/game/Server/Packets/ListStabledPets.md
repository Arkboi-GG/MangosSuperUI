# ListStabledPets

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ListStabledPets

**Purpose & Responsibilities**

`ListStabledPets` is a client-to-server network packet structure within the `WorldPackets::Npc` namespace. Its sole responsibility is to represent the incoming message `MSG_LIST_STABLED_PETS` sent by the game client when a player interacts with a stable master NPC to request a list of their currently stabled pets. As a `ClientPacket`, it serves as a data container that extracts the necessary context—specifically the GUID of the NPC being interacted with—from the raw binary stream received over the network. It contains no business logic, state management, or database interaction capabilities itself; it is purely a serialization target for the network layer.

**Member-by-Member Behavior**

The unit consists of a single member: the constructor.

*   **`ListStabledPets`**: This is an explicit default constructor. Its primary role is initialization. It initializes the base class `ClientPacket` with the specific opcode `MSG_LIST_STABLED_PETS`. This opcode identification is critical for the server's network dispatcher to route the incoming byte stream to the correct handler logic elsewhere in the codebase. The constructor does not initialize the `npcGuid` member variable; that field remains default-initialized (likely to an invalid or zeroed `ObjectGuid`) until the `ReadFromWorldPacket` method (declared in the base class interface but implemented elsewhere or implicitly via template mechanisms not shown in this header) populates it from the incoming data.

**Cross-Unit Boundaries**

*   **Calls Out**: None. The constructor performs only base class initialization.
*   **Called By**: None listed in the map. In practice, instances of `ListStabledPets` are typically created by the network reception layer when a packet with opcode `MSG_LIST_STABLED_PETS` is detected. The network layer will then invoke the `ReadFromWorldPacket` method to populate the `npcGuid` field before passing the object to the game logic handlers.

**Data Model**

This unit does not interact with any database tables. It operates entirely in memory as part of the network I/O subsystem.

**Notable Implementation Details**

*   **Explicit Constructor**: The use of `explicit` prevents implicit conversions from other types to `ListStabledPets`, ensuring type safety during packet creation.
*   **Base Class Dependency**: The class relies heavily on `ClientPacket` for its core functionality (opcode management and reading logic). The `ReadFromWorldPacket` method is declared as `override`, indicating it implements a virtual interface from `ClientPacket`. While the declaration is here, the actual parsing logic for `npcGuid` is not visible in this header, implying it is either implemented in a corresponding `.cpp` file or handled generically by the base class infrastructure.
*   **Namespace Structure**: The class is nested within `WorldPackets::Npc`, clearly categorizing it as a world-level packet related to Non-Player Character interactions.

## Member Reference

**ListStabledPets**
Constructor for the `ListStabledPets` packet. Initializes the base `ClientPacket` with the opcode `MSG_LIST_STABLED_PETS`. Does not initialize the `npcGuid` member, which is populated later during packet deserialization.

---

<!-- machine-true, projected from graph.json -->

## Map — ListStabledPets

*Source:* Npc.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ListStabledPets | ctor | — | — | — |
