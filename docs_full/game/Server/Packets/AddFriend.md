# AddFriend

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AddFriend

## Purpose & Responsibilities

`AddFriend` is a lightweight data structure within the `WorldPackets::Misc` namespace, designed to represent a specific client-to-server network message: `CMSG_ADD_FRIEND`. Its sole responsibility is to define the payload schema for this request, specifically holding the name of the character the client wishes to add to their friends list.

As a subclass of `ClientPacket`, it inherits the machinery required to identify the packet opcode and manage the underlying binary stream, but it contributes no logic for parsing, validation, or persistence. It serves strictly as a container for the `friendName` string, decoupling the definition of the message format from the logic that processes it.

## Member-by-Member Behavior

The unit contains a single member: the constructor.

### Constructor (`AddFriend`)

The constructor initializes the `AddFriend` object. It performs two critical setup tasks inherited from its base class `ClientPacket`:
1.  **Opcode Assignment**: It passes `CMSG_ADD_FRIEND` to the `ClientPacket` base constructor. This registers the packet with the server's network dispatcher, ensuring that incoming binary streams matching this opcode are routed to handlers expecting an `AddFriend` instance.
2.  **Default Initialization**: It relies on the default initialization of the `friendName` member (an empty `std::string`). No explicit value is set in the constructor body.

The constructor is marked `explicit` to prevent implicit conversions from other types, enforcing strict instantiation syntax.

## Cross-Unit Boundaries

`AddFriend` exists at the boundary between the network transport layer and the game logic layer.

*   **Calls Out**: None. The constructor does not invoke any external functions or services.
*   **Called By**: While the MAP indicates no external callers for the constructor itself, instances of `AddFriend` are typically constructed by the network subsystem (e.g., `WorldSession` or a packet factory) when a raw `CMSG_ADD_FRIEND` packet is received from a client. After construction, the `ReadFromWorldPacket` method (declared in the header but implemented elsewhere, likely in a corresponding `.cpp` file not included in this unit's scope) will be called by the network handler to populate `friendName` from the binary stream.

## Data Model

This unit does not interact directly with any database tables. It operates entirely in memory as part of the network packet processing pipeline. The `friendName` string it holds may eventually be used by downstream logic to query the `characters` table or similar structures to resolve the GUID of the target player, but `AddFriend` itself performs no SQL operations.

## Notable Implementation Details

*   **Final Class**: The class is declared `final`, preventing further inheritance. This is a design choice to ensure the packet structure remains stable and cannot be extended by subclasses, which simplifies polymorphic handling in the network stack.
*   **Namespace Organization**: It resides in `WorldPackets::Misc`, indicating it is part of a broader collection of miscellaneous client messages that do not fit into more specific categories like combat, movement, or chat.
*   **Dependency on External Parsing**: The header declares `ReadFromWorldPacket` as an override, but the implementation is not present in this file. This suggests a separation of concerns where the header defines the data layout, and the implementation file handles the byte-ordering and string extraction logic. Maintainers must look to the corresponding `.cpp` file to understand how `friendName` is extracted from the raw packet data.

## Member Reference

**AddFriend**
Constructor for the `AddFriend` packet. Initializes the base `ClientPacket` with the opcode `CMSG_ADD_FRIEND`. Does not initialize `friendName` explicitly, leaving it as an empty string until populated by `ReadFromWorldPacket`.

---

<!-- machine-true, projected from graph.json -->

## Map — AddFriend

*Source:* Misc.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AddFriend | ctor | — | — | — |
