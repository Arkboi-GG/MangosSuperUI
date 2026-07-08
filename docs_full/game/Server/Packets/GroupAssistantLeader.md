# GroupAssistantLeader

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# GroupAssistantLeader

**Purpose & Responsibilities**

The `GroupAssistantLeader` class is a client-side packet handler within the `WorldPackets::Group` namespace. Its sole responsibility is to deserialize the `CMSG_GROUP_ASSISTANT_LEADER` message received from the game client. This message allows a player (typically the group leader) to assign or remove the "Assistant Leader" role from another group member. The Assistant Leader role grants specific administrative privileges, such as inviting/uninviting players or changing loot methods, depending on the server configuration and client version.

This unit is part of the network layer's packet parsing infrastructure. It does not contain business logic for granting permissions or modifying group state; it strictly extracts the target identifier and the action flag from the binary stream into accessible member variables for downstream processing by the game world server logic.

**Member-by-Member Behavior**

The unit consists of a single constructor and relies on the inherited `ReadFromWorldPacket` method (defined in the base `ClientPacket` class, not shown in this source file but implied by the interface) to perform the actual deserialization.

*   **Constructor (`GroupAssistantLeader`)**: Initializes the packet object with the opcode `CMSG_GROUP_ASSISTANT_LEADER`. It sets the default value of the `flag` member to `0`. The initialization of the target identifier (`guid` or `name`) depends on the compiled client build version, handled via preprocessor directives.

**Cross-Unit Boundaries**

*   **Calls Out**: None. This unit does not invoke functions in other classes or modules.
*   **Called By**: The map indicates no external callers. In practice, this packet class is instantiated and populated by the network subsystem when a `CMSG_GROUP_ASSISTANT_LEADER` message arrives. The resulting object is then passed to the group management system (likely in `Group.cpp` or similar) to execute the role change.

**Data Model**

This unit does not interact with any database tables. It operates entirely on transient network data.

**Notable Implementation Details**

*   **Version-Dependent Serialization**: The structure of the packet changes based on the `SUPPORTED_CLIENT_BUILD` macro.
    *   For clients newer than `CLIENT_BUILD_1_11_2`, the target is identified by an `ObjectGuid`.
    *   For older clients, the target is identified by a `std::string` name.
    *   This conditional compilation ensures compatibility across different World of Warcraft client versions supported by the emulator.
*   **Flag Interpretation**: The `uint8 flag` member determines the action. While the exact semantic meaning of the flag values (e.g., 0 for remove, 1 for set) is not defined in this header, it is standard for such flags to indicate the desired state of the assistant leader role. The default initialization to `0` suggests a "remove" or "unset" operation might be the baseline if the field is omitted or zeroed, though the client typically sends the explicit intent.

## Member Reference

**GroupAssistantLeader**
Constructor that initializes the `ClientPacket` base class with the opcode `CMSG_GROUP_ASSISTANT_LEADER`. It conditionally defines the target identifier as either an `ObjectGuid` (for builds > 1.11.2) or a `std::string` (for older builds) and initializes the `flag` member to `0`.

---

<!-- machine-true, projected from graph.json -->

## Map — GroupAssistantLeader

*Source:* Group.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GroupAssistantLeader | ctor | — | — | — |
