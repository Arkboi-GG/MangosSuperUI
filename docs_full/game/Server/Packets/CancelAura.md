# CancelAura

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# CancelAura

## Purpose & Responsibilities

`CancelAura` is a minimal client-to-server packet structure within the `WorldPackets::Spell` namespace. Its sole responsibility is to represent the `CMSG_CANCEL_AURA` message sent by a client when a player attempts to remove a specific aura (spell effect) from themselves or a target.

The class acts as a data carrier, holding the `spellId` associated with the aura to be cancelled. It inherits from `ClientPacket`, establishing it as an inbound message from the game client. The class itself contains no business logic for processing the cancellation; it only defines the structure required to deserialize the incoming network data.

## Member-by-Member Behavior

The unit consists of a single member: the constructor.

### Constructor Initialization
The **CancelAura** constructor initializes the packet with the opcode `CMSG_CANCEL_AURA`. It sets the default value of the `spellId` member to `0`. This initialization ensures that if the packet is instantiated but not properly populated via `ReadFromWorldPacket`, the `spellId` remains in a known safe state.

## Cross-Unit Boundaries

As indicated in the MAP, the `CancelAura` constructor does not call out to other units, nor is it called by other units in the context of this specific translation unit's dependencies. However, in the broader system:

*   **Inheritance**: It relies on `ClientPacket` (defined in `Packet.h`) for base packet functionality, such as opcode management and serialization hooks.
*   **Deserialization**: The `ReadFromWorldPacket` method (declared here but implemented elsewhere, likely in a corresponding `.cpp` file or inline in a different partial) will be called by the network layer to populate the `spellId` from the raw `WorldPacket` data.
*   **Processing**: Once deserialized, an instance of `CancelAura` will typically be passed to a handler (e.g., in `WorldSession` or a spell handler module) which interprets the `spellId` and executes the actual aura removal logic on the player object.

## Data Model

This unit does not interact directly with any database tables. It operates entirely on runtime memory structures derived from network packets.

## Notable Implementation Details

*   **Minimalist Design**: The class contains only a single data member (`spellId`) and a constructor. All heavy lifting regarding packet parsing is delegated to the `ReadFromWorldPacket` method, which is declared but not defined in this header.
*   **Opcode Association**: The constructor explicitly binds this class to `CMSG_CANCEL_AURA`, ensuring type safety and correct routing within the packet dispatch system.
*   **Default State**: The `spellId` is initialized to `0`. In many game contexts, a spell ID of `0` is invalid, serving as a sentinel value to indicate an uninitialized or empty packet.

## Member Reference

**CancelAura**
Constructor for the `CancelAura` packet. Initializes the base `ClientPacket` with the opcode `CMSG_CANCEL_AURA` and sets the `spellId` member to `0`. It prepares the object to receive data via `ReadFromWorldPacket`.

---

<!-- machine-true, projected from graph.json -->

## Map — CancelAura

*Source:* Spell.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| CancelAura | ctor | — | — | — |
