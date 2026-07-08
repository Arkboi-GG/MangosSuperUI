# TeleportToUnit

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# TeleportToUnit

**Purpose & Responsibilities**

`TeleportToUnit` is a client-side packet structure within the `WorldPackets::Misc` namespace, responsible for representing the `CMSG_TELEPORT_TO_UNIT` message sent from the game client to the server. Its sole responsibility is to define the data layout for a request where a player attempts to teleport to another specific player character. It acts as a data container, holding the target player's name (`playerName`) and inheriting the base packet identification and reading logic from `ClientPacket`. This unit contains no business logic, validation, or network transmission code; it strictly defines the interface for deserializing this specific command from the raw network stream.

**Member-by-Member Behavior**

The unit consists of a single class, `TeleportToUnit`, which inherits from `ClientPacket`.

*   **`TeleportToUnit()`**: The constructor initializes the packet object. It explicitly calls the `ClientPacket` constructor with the opcode `CMSG_TELEPORT_TO_UNIT`, ensuring the packet is correctly identified by the server's dispatch system. It does not initialize the `playerName` member, leaving it as an empty string until `ReadFromWorldPacket` is invoked.
*   **`playerName`**: A public `std::string` member that stores the name of the target player unit to whom the sender wishes to teleport. This field is populated during the packet reading phase.
*   **`ReadFromWorldPacket(WorldPacket& recv_data)`**: Declared as an override of the virtual function in `ClientPacket`. While the declaration is present in this header, the implementation resides in the corresponding `.cpp` file (not provided in the source snippet, but implied by the `override` keyword and standard Mangos architecture). This method is responsible for extracting the `playerName` string from the incoming `WorldPacket` buffer.

**Cross-Unit Boundaries**

*   **Inheritance from `ClientPacket`**: `TeleportToUnit` derives from `ClientPacket` (defined in `Packet.h`). This establishes the contract that this object represents a message originating from the client. The base class provides the mechanism for associating the packet with its opcode (`CMSG_TELEPORT_TO_UNIT`) and likely manages memory or lifecycle aspects common to all client packets.
*   **Dependency on `WorldPacket`**: The `ReadFromWorldPacket` method accepts a reference to `WorldPacket` (defined in `Packet.h` or similar). This indicates that the deserialization logic relies on the `WorldPacket` utility class to parse binary data from the network socket into the structured fields of `TeleportToUnit`.
*   **No Outgoing Calls**: As a pure data structure with a constructor and a virtual read method, this unit does not call into other business logic units (such as `Player`, `MapManager`, or `TeleportHandler`) directly. The handling of the teleport request occurs in a separate handler unit that receives an instance of `TeleportToUnit` after it has been constructed and read.

**Data Model**

This unit does not interact directly with any database tables. It operates entirely on runtime memory structures derived from network input. The `playerName` field is a transient string used for identifying the target player in memory; it is not persisted by this class itself.

**Notable Implementation Details**

*   **Final Class**: The class is marked `final`, preventing further inheritance. This is consistent with the design pattern for packet classes in this codebase, where each packet type is a leaf node in the hierarchy.
*   **Explicit Constructor**: The use of `explicit` prevents implicit conversions from other types to `TeleportToUnit`, ensuring type safety during packet construction.
*   **Public Member Data**: Unlike typical encapsulated C++ classes, `playerName` is public. This suggests that the packet objects are treated as lightweight data transfer objects (DTOs) or structs, where direct access is preferred over getter/setter methods for performance and simplicity in the parsing/handling pipeline.
*   **Opcode Dependency**: The correctness of this packet depends on the constant `CMSG_TELEPORT_TO_UNIT` being defined correctly in `SharedDefines.h` (included via `Packet.h` or indirectly). If this opcode value mismatches the client's expectation, the packet will not be dispatched to the correct handler.

## Member Reference

**TeleportToUnit**
Constructor for the `TeleportToUnit` packet class. Initializes the base `ClientPacket` with the opcode `CMSG_TELEPORT_TO_UNIT`. Does not initialize the `playerName` member.

---

<!-- machine-true, projected from graph.json -->

## Map — TeleportToUnit

*Source:* Misc.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| TeleportToUnit | ctor | — | — | — |
