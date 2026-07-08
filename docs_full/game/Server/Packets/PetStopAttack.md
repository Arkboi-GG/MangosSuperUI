# PetStopAttack

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# PetStopAttack

## Purpose & Responsibilities

`PetStopAttack` is a client-side network packet structure within the `WorldPackets::Pet` namespace, defined in `Pet.h`. Its sole responsibility is to represent the `CMSG_PET_STOP_ATTACK` message sent from the game client to the server. This packet informs the server that a specific pet, identified by its global unique identifier (`petGuid`), should cease its current attack action.

This class is part of the Mangos emulator's packet parsing infrastructure. It inherits from `ClientPacket`, indicating it is deserialized from incoming network data. The class is conditionally compiled only for client builds newer than `CLIENT_BUILD_1_6_1`, reflecting changes in the World of Warcraft protocol where this specific command was introduced or modified.

## Member-by-Member Behavior

The unit contains a single member: the constructor.

### **PetStopAttack**
The default constructor initializes the `PetStopAttack` object. It performs two critical setup tasks:
1.  **Base Class Initialization**: It calls the base class `ClientPacket` constructor, passing the opcode `CMSG_PET_STOP_ATTACK`. This registers the packet type with the network handler, ensuring that incoming data with this opcode is routed to this specific parser.
2.  **Member Initialization**: It leaves the `petGuid` member uninitialized (default constructed `ObjectGuid`). The actual value for `petGuid` is populated later by the `ReadFromWorldPacket` method (which is declared in this header but implemented elsewhere, likely in a corresponding `.cpp` file or via inline definition not shown in the provided source snippet, though the declaration is present).

## Cross-Unit Boundaries

*   **Calls Out**: None. The constructor does not invoke any other units.
*   **Called By**: None listed in the MAP. In practice, this constructor is invoked by the network layer's packet factory when a `CMSG_PET_STOP_ATTACK` opcode is detected on the wire. The resulting object is then passed to the game world logic (likely a `Player` or `Pet` handler) to execute the stop-attack command.

## Data Model

This unit does not interact with any database tables. It operates purely on network packet data.

## Notable Implementation Details

*   **Conditional Compilation**: The entire class is wrapped in `#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_6_1`. This indicates that the `CMSG_PET_STOP_ATTACK` packet format or existence is specific to clients newer than version 1.6.1. Engineers maintaining backward compatibility for older clients must ensure this packet is not expected or processed for those builds.
*   **Minimal State**: The class holds only one piece of data: `ObjectGuid petGuid`. This simplicity reflects the nature of the command: the server needs to know *which* pet to stop, but no additional parameters (like target or spell ID) are required for this specific action.
*   **Inheritance**: As a `final` class inheriting from `ClientPacket`, it cannot be subclassed further. This enforces a strict, flat hierarchy for packet types, aiding in performance and clarity during dispatch.

## Member Reference

**PetStopAttack**
Constructor for the `PetStopAttack` packet. Initializes the base `ClientPacket` with the `CMSG_PET_STOP_ATTACK` opcode. Leaves `petGuid` for subsequent population by the packet reading logic. Conditionally compiled for client builds greater than 1.6.1.

---

<!-- machine-true, projected from graph.json -->

## Map — PetStopAttack

*Source:* Pet.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| PetStopAttack | ctor | — | — | — |
