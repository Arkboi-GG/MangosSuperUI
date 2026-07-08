# DestroyItem

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# DestroyItem

## Purpose & Responsibilities

`DestroyItem` is a client-side packet structure within the `WorldPackets::Item` namespace, defined in `Item.h`. Its sole responsibility is to represent the `CMSG_DESTROYITEM` message sent by the game client to the server when a player attempts to destroy an item.

As a `ClientPacket`, it serves as a data container that holds the raw parameters extracted from the network stream: the bag index, the slot index, and the quantity of items to destroy. It does not contain logic for validation, execution, or state modification; those responsibilities lie with the server-side handlers that consume this packet. The class is part of the Mangos emulator's networking layer, specifically handling item-related interactions.

## Member-by-Member Behavior

The unit contains a single member: the constructor.

### **DestroyItem**
This is the default constructor for the `DestroyItem` class. It performs two initialization tasks:
1.  **Base Class Initialization**: It invokes the base class constructor `ClientPacket(CMSG_DESTROYITEM)`, registering this packet instance with the specific opcode `CMSG_DESTROYITEM`. This opcode identifies the packet type to the server's network dispatcher.
2.  **Member Initialization**: It initializes the public data members `bag`, `slot`, and `count` to `0`. These members correspond to the fields expected in the `CMSG_DESTROYITEM` packet structure:
    *   `bag`: The index of the bag containing the item.
    *   `slot`: The slot index within that bag.
    *   `count`: The number of items to destroy (relevant for stackable items).

The constructor is marked `explicit` to prevent implicit conversions from other types.

## Cross-Unit Boundaries

*   **Calls Out**: None. The constructor only initializes local state and the base class.
*   **Called By**: None listed in the MAP. In practice, instances of `DestroyItem` are typically created by the network reading infrastructure (e.g., `WorldSession` or a packet factory) when a `CMSG_DESTROYITEM` opcode is received. The `ReadFromWorldPacket` method (declared in the class but not part of this unit's MAP) would be called subsequently to populate the `bag`, `slot`, and `count` fields from the raw byte stream.

## Data Model

This unit does not interact directly with any database tables. It operates purely on in-memory network packet data.

## Notable Implementation Details

*   **Default Values**: All data members (`bag`, `slot`, `count`) are initialized to `0` in the constructor. This ensures that if `ReadFromWorldPacket` fails or is not called, the object remains in a known, safe state rather than containing garbage values.
*   **Public Data Members**: Unlike typical C++ classes that use private members with getters/setters, `DestroyItem` exposes its data members (`bag`, `slot`, `count`) publicly. This is a common pattern in packet structures for performance and simplicity, allowing direct access by the parsing logic and the handler logic.
*   **Final Class**: The class is declared `final`, indicating it cannot be inherited. This is appropriate for a leaf-node packet structure.
*   **Namespace**: It resides in `WorldPackets::Item`, clearly segregating item-related network messages from other world packet types.

## Member Reference

**DestroyItem**
The default constructor for the `DestroyItem` packet. It initializes the base `ClientPacket` with the opcode `CMSG_DESTROYITEM` and sets the `bag`, `slot`, and `count` members to `0`. It is marked `explicit` to prevent implicit conversions.

---

<!-- machine-true, projected from graph.json -->

## Map — DestroyItem

*Source:* Item.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| DestroyItem | ctor | — | — | — |
