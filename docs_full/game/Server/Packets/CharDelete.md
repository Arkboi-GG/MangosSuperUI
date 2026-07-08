# CharDelete

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# CharDelete

**Purpose & Responsibilities**

`CharDelete` is a client-side packet structure within the `WorldPackets::Character` namespace, designed to represent the `CMSG_CHAR_DELETE` message sent from a game client to the server. Its sole responsibility is to encapsulate the data required to identify a character for deletion: specifically, the `ObjectGuid` of the target character. It inherits from `ClientPacket`, indicating it is part of the network layer responsible for deserializing incoming binary data from clients into structured C++ objects.

**Member-by-Member Behavior**

The unit contains a single member: the constructor.

*   **`CharDelete()`**: This default constructor initializes the `CharDelete` object. It invokes the base class constructor `ClientPacket(CMSG_CHAR_DELETE)`, registering the packet type identifier `CMSG_CHAR_DELETE` with the packet handling system. No additional initialization is performed on the `guid` member in this constructor; the `guid` value is populated later via the `ReadFromWorldPacket` method (which is declared in this header but implemented elsewhere, likely in a corresponding `.cpp` file not provided in the source snippet, or implicitly handled by the framework if `ObjectGuid` has a default state suitable for reading).

**Cross-Unit Boundaries**

*   **Calls Out**: The constructor calls `ClientPacket(CMSG_CHAR_DELETE)` from the base class `ClientPacket`. This establishes the packet's identity within the network protocol handler.
*   **Called By**: According to the MAP, no other units explicitly call this constructor in the provided context. In practice, the packet framework likely instantiates this class when it receives a raw network packet with the opcode `CMSG_CHAR_DELETE`.

**Data Model**

This unit does not interact directly with any database tables. It operates purely within the network layer, handling in-memory data structures (`ObjectGuid`) derived from client input.

**Notable Implementation Details**

*   **Inheritance**: `CharDelete` inherits from `ClientPacket`, implying it participates in a polymorphic packet handling system.
*   **Final Class**: The class is marked `final`, preventing further inheritance.
*   **Namespace**: It resides in `WorldPackets::Character`, grouping it with other character-related network messages like `CharCreate`, `PlayerLogin`, and `CharRename`.
*   **Missing Implementation**: The `ReadFromWorldPacket` method is declared but not defined in the provided header. This suggests the actual deserialization logic (extracting the `ObjectGuid` from the `WorldPacket` buffer) is located in a separate source file (e.g., `Character.cpp` or similar), which is not part of this specific unit definition.

## Member Reference

**CharDelete**
Constructor for the `CharDelete` packet. Initializes the base `ClientPacket` with the opcode `CMSG_CHAR_DELETE`. Prepares the object to receive an `ObjectGuid` via subsequent deserialization steps.

---

<!-- machine-true, projected from graph.json -->

## Map — CharDelete

*Source:* Character.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| CharDelete | ctor | — | — | — |
