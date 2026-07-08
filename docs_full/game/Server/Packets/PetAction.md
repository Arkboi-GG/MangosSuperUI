# PetAction

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# PetAction Packet Definition

## Purpose & Responsibilities

The `PetAction` class, defined within the `WorldPackets::Pet` namespace in `Pet.h`, serves as the C++ representation of the `CMSG_PET_ACTION` network message. Its sole responsibility is to define the data structure required to deserialize client-to-server packets that instruct a player's pet to perform a specific action.

This unit is part of the broader packet parsing infrastructure. It does not contain logic for executing the action, validating permissions, or interacting with game entities. Instead, it provides the raw fields (`petGuid`, `data`, `targetGuid`) that higher-level handlers (not included in this unit) will consume to determine whether the pet should attack, follow, stay, or perform another command. The class inherits from `ClientPacket`, indicating it is exclusively used for incoming traffic from the game client.

## Member-by-Member Behavior

### **PetAction** (Constructor)
The constructor initializes the packet object. It performs two critical setup tasks:
1.  **Protocol Identification**: It calls the base class constructor `ClientPacket(CMSG_PET_ACTION)`, registering this instance as handling the specific opcode `CMSG_PET_ACTION`. This allows the network layer to route incoming bytes to this parser.
2.  **Field Initialization**: It explicitly initializes the `data` member to `0`. While `petGuid` and `targetGuid` are default-initialized by their respective constructors (typically to empty/null GUIDs), the `uint32 data` field is explicitly zeroed to ensure a clean state before deserialization begins.

### **ReadFromWorldPacket** (Declaration)
Although the implementation is not present in this header, the declaration specifies that this class overrides the virtual `ReadFromWorldPacket` method from `ClientPacket`. This method is responsible for extracting the binary payload from the incoming `WorldPacket` buffer and populating the public members:
*   `petGuid`: The unique identifier of the pet issuing or receiving the action.
*   `data`: A bitmask or integer value encoding the specific action type (e.g., attack, stop, follow) and potentially the action bar slot index.
*   `targetGuid`: The unique identifier of the entity targeted by the action (if applicable, such as the unit to attack).

## Cross-Unit Boundaries

*   **Calls Out**: None. The `PetAction` constructor and declaration do not invoke functions in other units.
*   **Called By**: The network input handler (external to this unit) instantiates `PetAction` objects and calls `ReadFromWorldPacket` when a packet with opcode `CMSG_PET_ACTION` is received. Subsequently, game logic handlers (external) will access the public members (`petGuid`, `data`, `targetGuid`) to process the request.

## Data Model

This unit does not interact with any database tables. It operates entirely on transient network data.

## Notable Implementation Details

*   **Explicit Zeroing of `data`**: The constructor explicitly sets `data = 0`. This suggests that `data` might be partially populated or that the deserialization logic relies on a known initial state, though typically `ReadFromWorldPacket` overwrites all fields. This explicit initialization acts as a safety measure against uninitialized memory reads if `ReadFromWorldPacket` fails or is skipped.
*   **Public Members**: All data fields (`petGuid`, `data`, `targetGuid`) are public. This design choice prioritizes simplicity and direct access for the handlers that parse these packets, avoiding the overhead of getter/setter methods for simple data transfer objects.
*   **Inheritance**: Inherits from `ClientPacket`, which implies it shares common functionality for packet validation, size checking, and opcode management with other client-bound messages.

## Member Reference

**PetAction**
Constructor that initializes the packet with opcode `CMSG_PET_ACTION` and sets the `data` field to 0. Prepares the object for deserialization of pet action commands.

---

<!-- machine-true, projected from graph.json -->

## Map — PetAction

*Source:* Pet.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| PetAction | ctor | — | — | — |
