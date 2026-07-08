# SellItem

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# SellItem

## Purpose & Responsibilities

`SellItem` is a client-side packet structure within the `WorldPackets::Item` namespace, responsible for representing the `CMSG_SELL_ITEM` message sent from the game client to the server. Its sole responsibility is to define the binary layout and provide the interface for deserializing the data required to initiate a transaction where a player sells an item to a vendor. It acts as a passive data container; it contains no business logic, validation, or state management.

## Member-by-Member Behavior

The unit consists of a single constructor and a set of public data members that mirror the expected network payload.

### Data Members
*   **`vendorGuid`**: An `ObjectGuid` identifying the specific vendor NPC (Non-Player Character) or object with which the transaction is taking place. This allows the server to locate the correct entity to process the sale.
*   **`itemGuid`**: An `ObjectGuid` identifying the specific instance of the item the player wishes to sell. This distinguishes between multiple identical items in the player's inventory.
*   **`count`**: A `uint8` specifying the quantity of the item to sell. This supports selling partial stacks of stackable items.

### Constructor
*   **`SellItem()`**: The default constructor initializes the packet with the opcode `CMSG_SELL_ITEM`. It sets the `count` member to `0` via in-class initialization. The `vendorGuid` and `itemGuid` members are default-initialized by their respective type constructors. This constructor prepares the object to receive data from an incoming network stream.

## Cross-Unit Boundaries

*   **Called By**: The MAP indicates no external callers. In practice, instances of `SellItem` are typically created by the network layer (e.g., `WorldSession` or a packet handler dispatcher) when a raw `CMSG_SELL_ITEM` packet is received. The network layer will invoke `ReadFromWorldPacket` (declared in the base class `ClientPacket`, not implemented in this unit) to populate the members.
*   **Calls Out**: The MAP indicates no outgoing calls. The unit does not interact with other classes, databases, or services. It is a pure data structure.

## Data Model

This unit does not interact directly with any database tables. It operates entirely on in-memory objects derived from network packets. The `vendorGuid` and `itemGuid` eventually resolve to database records (e.g., `creature` or `item_instance` tables) via other units (such as `GameObject`, `Creature`, or `Item` managers), but `SellItem` itself performs no SQL queries or schema interactions.

## Notable Implementation Details

*   **Passive Structure**: `SellItem` inherits from `ClientPacket` but does not implement `ReadFromWorldPacket`. This method is likely implemented in a separate partial or base class handling the common serialization logic for all client packets. This unit only defines the *shape* of the data.
*   **Guid Usage**: The use of `ObjectGuid` for both the vendor and the item ensures that the server can uniquely identify entities even if their numeric IDs are recycled or ambiguous. This is critical for preventing race conditions or spoofing attempts where a client might try to sell an item to a different vendor than intended.
*   **Count Initialization**: The `count` field is initialized to `0` in the class definition. If the network packet fails to deserialize correctly or if the client sends malformed data, this default value provides a safe baseline, though validation would occur in the processing logic outside this unit.

## Member Reference

**SellItem**
The default constructor for the `SellItem` packet. It initializes the packet opcode to `CMSG_SELL_ITEM` and sets the `count` member to `0`. It prepares the object for deserialization of vendor and item identifiers from the network stream.

---

<!-- machine-true, projected from graph.json -->

## Map — SellItem

*Source:* Item.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| SellItem | ctor | — | — | — |
