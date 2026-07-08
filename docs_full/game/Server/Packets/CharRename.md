# CharRename

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# CharRename

**Purpose & Responsibilities**

`CharRename` is a client-side packet structure within the `WorldPackets::Character` namespace, responsible for representing the `CMSG_CHAR_RENAME` message sent from a client to the server. Its sole responsibility is to define the data layout for a character rename request, specifically capturing the target character's unique identifier (`guid`) and the desired new name (`newname`). As a `ClientPacket`, it serves as the input container for the server's packet parsing logic, ensuring that the binary data received over the network is correctly deserialized into accessible C++ fields.

**Member-by-Member Behavior**

The unit contains a single member: the constructor.

*   **Constructor (`CharRename()`)**: This explicit constructor initializes the packet object. It invokes the base class `ClientPacket` constructor, passing the constant `CMSG_CHAR_RENAME` to identify the packet type. This registration allows the server's packet dispatcher to route incoming binary streams with this opcode to the appropriate handler. The constructor does not initialize the `guid` or `newname` members; these are populated later by the `ReadFromWorldPacket` method (defined in the shared header but implemented elsewhere, likely in a corresponding `.cpp` file or inline in a different partial, though the MAP indicates no other members belong to this specific unit).

**Cross-Unit Boundaries**

*   **Calls Out**: None. The constructor only interacts with its base class `ClientPacket`.
*   **Called By**: None listed in the MAP. In practice, instances of `CharRename` are typically created by the server's packet reading infrastructure when a `CMSG_CHAR_RENAME` opcode is detected, but this interaction occurs outside the scope of this unit's defined members.

**Data Model**

This unit does not interact directly with any database tables. It operates purely on in-memory data structures derived from network packets.

**Notable Implementation Details**

*   **Inheritance**: Inherits from `ClientPacket`, implying it follows the standard pattern for incoming client messages in the Mangos framework.
*   **Explicit Constructor**: The use of `explicit` prevents implicit conversions from other types, ensuring type safety during packet instantiation.
*   **Uninitialized Members**: The `guid` and `newname` members are default-initialized (empty string and zeroed Guid) by their respective constructors but remain empty until `ReadFromWorldPacket` is called. Since `ReadFromWorldPacket` is not part of this unit's MAP, the actual deserialization logic is external to this documentation's scope.

## Member Reference

**CharRename**
The default constructor for the `CharRename` packet. It explicitly initializes the base `ClientPacket` with the `CMSG_CHAR_RENAME` opcode, marking this object as a rename request from the client. It does not populate the `guid` or `newname` fields; those are handled by the separate `ReadFromWorldPacket` method.

---

<!-- machine-true, projected from graph.json -->

## Map — CharRename

*Source:* Character.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| CharRename | ctor | — | — | — |
