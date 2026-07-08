# CancelCast

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# CancelCast

## Purpose & Responsibilities

`CancelCast` is a lightweight data structure within the `WorldPackets::Spell` namespace, representing the `CMSG_CANCEL_CAST` network message sent by a client to request the cancellation of an active spell cast. As a subclass of `ClientPacket`, its primary responsibility is to define the binary layout and initialization state for this specific command. It holds a single field, `spellId`, which identifies the spell the client wishes to cancel.

This unit contains no business logic, validation, or processing code. It serves strictly as a type definition and constructor for the packet structure. The actual parsing of the incoming network data (`ReadFromWorldPacket`) and the server-side handling of the cancellation request are implemented in other units (likely the corresponding `.cpp` file for this header or a separate handler module), but those members are not part of this specific translation unit's scope.

## Member-by-Member Behavior

The unit defines one member: the constructor.

*   **Constructor Initialization**: The `CancelCast` constructor initializes the base class `ClientPacket` with the opcode `CMSG_CANCEL_CAST`. This associates the packet structure with the correct network message identifier expected by the server's packet dispatcher. It also initializes the `spellId` member to `0` via the in-class initializer defined in the header.

## Cross-Unit Boundaries

*   **Base Class Dependency**: `CancelCast` inherits from `ClientPacket` (defined in `Packet.h`). The constructor calls `ClientPacket`'s constructor to set the opcode. This establishes the contract that `CancelCast` is an inbound message from the client.
*   **No Outbound Calls**: This unit does not call any other functions or classes.
*   **No Inbound Calls from Other Units**: According to the provided MAP, no other units explicitly call into `CancelCast` members in this context. However, in practice, the server's packet reading infrastructure will instantiate this class and call its `ReadFromWorldPacket` method (which is declared here but defined elsewhere) when a `CMSG_CANCEL_CAST` packet is received.

## Data Model

This unit does not interact with any database tables. It operates entirely on in-memory network packet structures.

## Notable Implementation Details

*   **Minimal State**: The class contains only one data member, `uint32 spellId`. This reflects the simplicity of the client's request: "Cancel the spell with ID X."
*   **Default Initialization**: The `spellId` is default-initialized to `0`. While the `ReadFromWorldPacket` method (defined outside this unit) will overwrite this value with the actual data from the network stream, the default ensures the object is in a valid state immediately upon construction.
*   **Final Class**: The class is marked `final`, preventing further inheritance. This is appropriate for a leaf-node packet structure that represents a specific, fixed protocol message.
*   **Namespace Organization**: It resides in `WorldPackets::Spell`, grouping it logically with other spell-related network messages like `CastSpell`, `UseItem`, and `CancelAura`.

## Member Reference

**CancelCast**
Constructor for the `CancelCast` packet. Initializes the base `ClientPacket` with the opcode `CMSG_CANCEL_CAST` and sets the `spellId` member to its default value of `0`. This prepares the object to receive and hold the spell ID from an incoming network packet.

---

<!-- machine-true, projected from graph.json -->

## Map — CancelCast

*Source:* Spell.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| CancelCast | ctor | — | — | — |
