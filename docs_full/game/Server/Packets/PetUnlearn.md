# PetUnlearn

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# PetUnlearn

**Purpose & Responsibilities**

`PetUnlearn` is a client-side network packet structure within the `WorldPackets::Pet` namespace. Its sole responsibility is to represent the `CMSG_PET_UNLEARN` message sent by the game client to the server. This message indicates that a player has instructed their pet to remove a specific spell or ability from its known spells list. The class encapsulates the raw binary data of this request—specifically, the identifier of the pet involved—and provides the interface for deserializing that data from the incoming network stream.

This unit is part of the Mangos server's packet parsing layer. It does not contain logic for executing the unlearn action, validating permissions, or modifying game state; it strictly defines the data contract for the request.

## Member-by-Member Behavior

### Constructor: `PetUnlearn()`
The constructor initializes the packet object. It performs two key setup tasks:
1.  **Protocol Identification**: It calls the base class `ClientPacket` constructor, passing `CMSG_PET_UNLEARN`. This registers the packet type with the network handler, ensuring that when the server receives a packet with this opcode, it instantiates a `PetUnlearn` object to handle it.
2.  **State Initialization**: It leaves the `guid` member uninitialized (default-initialized to an empty `ObjectGuid`). The actual value is populated later during deserialization.

### Deserialization: `ReadFromWorldPacket(WorldPacket& recv_data)`
Although declared in the header, the implementation of `ReadFromWorldPacket` is not shown in the provided source snippet. However, based on the class definition and standard patterns in this codebase:
*   It reads the `guid` field from the `recv_data` buffer.
*   The `guid` corresponds to the `ObjectGuid` of the pet that should unlearn the spell.
*   Note: In many WoW protocol versions, the spell ID to be unlearned is either implicit (e.g., the last learned spell) or handled via a separate mechanism not captured in this simple struct, or potentially the `guid` refers to the spell/target depending on the specific client build's protocol definition. Given the member is named `guid` and is of type `ObjectGuid`, it likely identifies the pet entity itself, while the specific spell might be determined by context or a different packet variant not shown here. *Correction*: Looking at similar packets like `PetCancelAura`, which has both `guid` and `spellId`, `PetUnlearn` only has `guid`. This suggests the protocol for `CMSG_PET_UNLEARN` in this specific client build (`> 1.6.1`) relies solely on the pet's GUID, implying the server determines which spell to remove (likely the most recently learned one, or requiring additional context not present in this packet alone).

## Cross-Unit Boundaries

*   **Calls Out**: None. The constructor and data members do not invoke other units.
*   **Called By**: This unit is instantiated by the network packet dispatcher (not shown in this unit) when a `CMSG_PET_UNLEARN` opcode is detected on the wire. The resulting object is then passed to the command handler or AI logic responsible for processing pet commands (likely in `PetHandler.cpp` or similar, though not listed in the MAP).

## Data Model

This unit does not interact directly with any database tables. It operates entirely on in-memory network data structures.

## Notable Implementation Details

*   **Client Build Dependency**: The class is wrapped in `#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_6_1`. This means `PetUnlearn` is only compiled and available for clients newer than version 1.6.1. For older clients, this packet type does not exist, and the server must handle pet unlearning differently (or not at all via this specific opcode).
*   **Minimal Payload**: Unlike `PetCancelAura` (which includes a `spellId`) or `PetSpellAutocast` (which includes `spellId` and `state`), `PetUnlearn` contains only a `guid`. This implies the "what to unlearn" information is either:
    1.  Implicitly the last learned spell.
    2.  Handled by a different packet structure not shown here.
    3.  Determined by the server based on the pet's current state upon receiving this GUID.
*   **Final Class**: The class is marked `final`, preventing inheritance. This is appropriate for a leaf-node packet structure.

## Member Reference

**PetUnlearn**
Constructor. Initializes the packet with the opcode `CMSG_PET_UNLEARN` and default-initializes the `guid` member. Only compiled for client builds greater than 1.6.1.

---

<!-- machine-true, projected from graph.json -->

## Map — PetUnlearn

*Source:* Pet.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| PetUnlearn | ctor | — | — | — |
