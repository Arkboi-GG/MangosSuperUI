# ListInventory

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ListInventory

**ListInventory** is a minimal client-side packet structure within the `WorldPackets::Item` namespace, defined in `Item.h`. It represents the `CMSG_LIST_INVENTORY` message sent by the game client to the server. Its sole responsibility is to carry the `ObjectGuid` of the entity (typically a corpse or container) whose inventory the client requests the server to list.

This unit contains no executable logic, no database interactions, and no cross-unit dependencies beyond its inheritance from `ClientPacket`. It serves purely as a data carrier for network deserialization.

## Purpose & Responsibilities

The `ListInventory` class encapsulates the data payload for the `CMSG_LIST_INVENTORY` opcode. When a player interacts with an object that has an inventory (such as a dead creature's corpse or an opened container), the client sends this packet to request the contents of that inventory. The server receives this packet, extracts the `guid`, and subsequently processes the request (logic handled in other units, such as `Corpse` or `Container` handlers) to send back the inventory details.

## Member-by-Member Behavior

### Constructor
*   **`ListInventory()`**: The default constructor initializes the packet with the opcode `CMSG_LIST_INVENTORY`. It inherits from `ClientPacket`, ensuring the base packet structures are set up correctly for incoming network data.

### Data Members
*   **`guid`**: An `ObjectGuid` representing the unique identifier of the target object whose inventory is being queried. This field is populated during deserialization via `ReadFromWorldPacket`.

### Deserialization
*   **`ReadFromWorldPacket(WorldPacket& recv_data)`**: Declared as an override of the base class method. While the declaration is present in this header, the implementation resides in the corresponding `.cpp` file (not provided in the source snippet, but implied by the interface). This method reads the `guid` from the raw network packet buffer.

## Cross-Unit Boundaries

*   **Calls Out**: None. The class itself performs no actions.
*   **Called By**: The MAP indicates no external callers for the constructor. In practice, this class is instantiated by the network layer (e.g., `WorldSession` or packet handler dispatchers) when a `CMSG_LIST_INVENTORY` opcode is detected on the wire. These callers are outside the scope of this specific translation unit's definition.

## Data Model

This unit does not interact with any database tables. It operates entirely on runtime network data.

## Notable Implementation Details

*   **Minimalist Design**: Like all packet classes in this namespace, `ListInventory` is a Plain Old Data (POD) structure with a virtual destructor (inherited) and a deserialization hook. It contains no business logic.
*   **Opcode Association**: It is strictly bound to `CMSG_LIST_INVENTORY`. Any change in the client protocol regarding how inventory lists are requested would require updating this class's fields and deserialization logic.
*   **Guid Usage**: The use of `ObjectGuid` ensures that the server can uniquely identify the target object across different contexts (players, creatures, items, etc.) without ambiguity.

## Member Reference

**ListInventory**  
Constructor for the `ListInventory` packet. Initializes the base `ClientPacket` with the `CMSG_LIST_INVENTORY` opcode. No arguments are taken.

---

<!-- machine-true, projected from graph.json -->

## Map — ListInventory

*Source:* Item.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ListInventory | ctor | — | — | — |
