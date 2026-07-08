# BuyBankSlot

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

The `BuyBankSlot` class is a lightweight data structure within the `WorldPackets::Item` namespace, designed to represent the `CMSG_BUY_BANK_SLOT` network message sent from the client to the server. Its sole responsibility is to encapsulate the raw data payload associated with a player's request to purchase an additional bank slot. As a subclass of `ClientPacket`, it serves as a bridge between the low-level binary network stream and the high-level game logic, providing a typed interface (`guid`) for the entity involved in the transaction.

This unit is part of a larger collection of item-related packet definitions in `Item.h`. It does not contain business logic, validation, or side effects; it strictly defines the memory layout and initialization required to deserialize the incoming network packet.

## Member-by-Member Behavior

### Construction and Initialization

**`BuyBankSlot()`**
This is the default constructor for the `BuyBankSlot` packet. It performs two critical initialization steps:
1.  **Base Class Initialization**: It invokes the `ClientPacket` constructor, passing the constant `CMSG_BUY_BANK_SLOT`. This registers the packet type with the network handler, ensuring that incoming packets with this opcode are routed to the correct deserialization routine.
2.  **Member Initialization**: The `guid` member is implicitly default-initialized. Since `ObjectGuid` is a value type representing a unique identifier, it starts in an empty/null state until populated by the `ReadFromWorldPacket` method (which is declared in the base class or elsewhere, but not defined in this specific partial).

## Cross-Unit Boundaries

*   **Calls Out**: None. The constructor does not invoke any other units.
*   **Called By**: The MAP indicates no external callers. In practice, instances of `BuyBankSlot` are typically created by the network layer's packet factory when a `CMSG_BUY_BANK_SLOT` opcode is detected on the wire. The factory allocates this object and then calls its `ReadFromWorldPacket` method to populate the `guid` field.

## Data Model

This unit does not interact directly with any database tables. It operates entirely in memory during the network I/O phase. The `guid` field likely corresponds to the GUID of the Banker NPC or the Player character, depending on the specific protocol version, but no SQL queries are executed within this class.

## Notable Implementation Details

*   **Final Class**: The class is marked `final`, preventing inheritance. This is appropriate for a leaf-node packet structure that should not be extended.
*   **Namespace Isolation**: It resides in `WorldPackets::Item`, clearly segregating item-related network traffic from other subsystems (e.g., combat, chat, movement).
*   **Minimal State**: The class contains only one member variable, `ObjectGuid guid`. This simplicity reflects the minimal data required for the client to identify the target of the bank slot purchase request.
*   **No Logic**: There is no validation logic (e.g., checking if the GUID is valid) within this unit. Such checks would occur in the handler that processes this packet after deserialization.

## Member Reference

**BuyBankSlot**
Default constructor for the `BuyBankSlot` packet. Initializes the base `ClientPacket` with the opcode `CMSG_BUY_BANK_SLOT`. The `guid` member is default-initialized to an empty state. This constructor prepares the object to receive data from the network stream via the inherited `ReadFromWorldPacket` method.

---

<!-- machine-true, projected from graph.json -->

## Map — BuyBankSlot

*Source:* Item.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| BuyBankSlot | ctor | — | — | — |
