# GroupChangeSubGroup

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# GroupChangeSubGroup

## Purpose & Responsibilities

`GroupChangeSubGroup` is a client-side packet structure within the `WorldPackets::Group` namespace. Its sole responsibility is to represent the `CMSG_GROUP_CHANGE_SUB_GROUP` message sent by the game client to the server. This packet conveys a player's intent to move themselves into a specific subgroup within a raid or party context. It carries the target subgroup number (`groupNr`) and the name of the entity involved in the change (`name`). As a `ClientPacket`, it serves as the data container for deserialization logic, holding the raw fields extracted from the network stream before higher-level game logic processes the request.

## Member-by-Member Behavior

The unit consists of a single constructor and relies on inherited behavior for packet identification and deserialization.

### Construction and Initialization

**`GroupChangeSubGroup`**
This default constructor initializes the packet instance. It performs two critical setup steps:
1.  **Base Class Initialization**: It invokes the `ClientPacket` base class constructor, passing the opcode `CMSG_GROUP_CHANGE_SUB_GROUP`. This registers the packet type with the network layer, ensuring that incoming packets with this opcode are routed to this specific class for handling.
2.  **Member Defaulting**: It initializes the `groupNr` member variable to `0`. The `name` member, being a `std::string`, is default-constructed to an empty string. These defaults ensure the object is in a valid state immediately upon creation, prior to any data being read from the network buffer.

### Data Fields

The class exposes two public data members that define the payload of the packet:
*   **`name`**: A `std::string` intended to hold the character name associated with the subgroup change request. In many World of Warcraft packet structures, this field may represent the player issuing the command or the target, depending on the specific client version and protocol semantics.
*   **`groupNr`**: A `uint8` representing the index of the target subgroup (e.g., Subgroup 1, Subgroup 2, etc.). It defaults to `0`.

### Deserialization

While not a member defined in this specific partial, the class inherits the virtual method `ReadFromWorldPacket` from `ClientPacket`. The actual implementation of this method (which populates `name` and `groupNr` from the `WorldPacket` buffer) resides in the corresponding `.cpp` file or is implemented elsewhere in the class hierarchy. This unit's header merely declares the interface contract.

## Cross-Unit Boundaries

*   **Inheritance**: The class inherits from `WorldPackets::ClientPacket`. This establishes the fundamental lifecycle of the object: it is created by the network layer when a packet with opcode `CMSG_GROUP_CHANGE_SUB_GROUP` is received.
*   **Dependencies**:
    *   `Packet.h`: Provides the base class definition.
    *   `ObjectGuid.h`: Included in the header, though `GroupChangeSubGroup` itself does not use `ObjectGuid` (unlike sibling classes like `GroupUninviteGuid`). This inclusion is likely part of the broader `Group` namespace header management.
    *   `nonstd/optional.hpp`: Included in the header, but not used by `GroupChangeSubGroup`. This dependency is required by sibling classes such as `RaidReadyCheck`.

There are no outgoing calls from `GroupChangeSubGroup` to other business logic units within this translation unit. It is a pure data structure.

## Data Model

This unit does not interact directly with any database tables. It operates entirely in memory as part of the network packet processing pipeline.

## Notable Implementation Details

1.  **Public Data Members**: Unlike typical C++ classes that enforce encapsulation via getters/setters, `GroupChangeSubGroup` exposes `name` and `groupNr` as public members. This is a common pattern in packet structures for performance and simplicity, allowing the deserialization routine (`ReadFromWorldPacket`) and the subsequent handler logic to access fields directly without overhead.
2.  **Default Value for `groupNr`**: The initialization of `groupNr` to `0` is significant. In zero-indexed systems, this might refer to the first subgroup. However, if the client sends an invalid or missing value, the default ensures the variable holds a predictable state. Handlers consuming this packet must verify if `0` is a valid subgroup index in the current context (e.g., raids often use 1-based indexing for display, but internal representations vary).
3.  **String Handling**: The `name` field is a `std::string`. The deserialization process (not shown here but implied) must handle potential encoding issues or null terminators correctly when reading from the binary `WorldPacket`.
4.  **Opcode Specificity**: The constructor strictly binds this class to `CMSG_GROUP_CHANGE_SUB_GROUP`. Any deviation in the opcode will result in this class not being instantiated for that packet, preventing misinterpretation of network data.

## Member Reference

**GroupChangeSubGroup**
Constructor for the `GroupChangeSubGroup` packet. Initializes the base `ClientPacket` with the opcode `CMSG_GROUP_CHANGE_SUB_GROUP` and sets the `groupNr` member to `0`. The `name` member is default-initialized to an empty string. This prepares the object to receive and store data from an incoming network packet requesting a subgroup change.

---

<!-- machine-true, projected from graph.json -->

## Map — GroupChangeSubGroup

*Source:* Group.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GroupChangeSubGroup | ctor | — | — | — |
