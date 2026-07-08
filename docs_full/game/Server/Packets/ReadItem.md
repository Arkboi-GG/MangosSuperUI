# ReadItem

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ReadItem

## Purpose & Responsibilities

`ReadItem` is a client-side packet handler within the `WorldPackets::Item` namespace, responsible for deserializing the `CMSG_READ_ITEM` message sent by the game client. This packet represents a user action where the player attempts to inspect or "read" an item located in a specific inventory slot. The class extracts the bag index and slot index from the incoming binary data, making these coordinates available to the server-side logic that processes the request. It is part of the broader item management subsystem, handling low-level input parsing rather than high-level game logic or database persistence.

## Member-by-Member Behavior

### Construction and Initialization
The **ReadItem** constructor initializes the packet object. It sets the default values for the `bag` and `slot` members to `0`. Crucially, it invokes the base class constructor `ClientPacket` with the constant `CMSG_READ_ITEM`, registering this object as the handler for that specific message type. This ensures that when the network layer receives a packet with this opcode, an instance of `ReadItem` is created to process it.

### Data Extraction
Although not explicitly listed in the MAP as a separate member due to its virtual nature inherited from `ClientPacket`, the `ReadFromWorldPacket` method (declared in the header) is the core functional component. It overrides the base class method to define how the binary payload is interpreted. For `ReadItem`, this involves reading two unsigned 8-bit integers (`uint8`) from the `WorldPacket` stream:
1.  The first byte corresponds to the `bag` index.
2.  The second byte corresponds to the `slot` index.

These values are stored in the public members `bag` and `slot`, allowing subsequent processing logic (in other units) to identify exactly which item the client is referencing.

## Cross-Unit Boundaries

*   **Calls Out:** None. The `ReadItem` class is purely a data structure and parser. It does not invoke methods in other units during construction or parsing.
*   **Called By:** The MAP indicates no external callers, which is consistent with its role as a leaf-node packet handler. In practice, instances of `ReadItem` are typically instantiated by the network dispatch system (e.g., `WorldSession` or a packet router) when a `CMSG_READ_ITEM` opcode is detected. The extracted data (`bag`, `slot`) is then consumed by higher-level session handlers or item managers (not shown in this unit) to validate the request and send back item details.

## Data Model

This unit does not interact directly with any database tables. It operates solely on in-memory network packet data. The `bag` and `slot` indices it extracts are transient references to the player's current inventory state, which may be persisted elsewhere in the system, but `ReadItem` itself performs no SQL queries or table accesses.

## Notable Implementation Details

*   **Minimalist Design:** The class contains only two data members (`bag`, `slot`) and relies entirely on the base class `ClientPacket` for lifecycle management and network I/O infrastructure.
*   **Type Safety:** The use of `uint8` for bag and slot indices aligns with typical World of Warcraft protocol specifications for inventory slots, ensuring efficient memory usage and correct interpretation of the binary stream.
*   **Default Initialization:** The members are initialized to `0` in the class definition. While the `ReadFromWorldPacket` method will overwrite these with actual packet data, explicit initialization prevents undefined behavior if the parsing step fails or is skipped.

## Member Reference

**ReadItem**
Constructor for the `ReadItem` packet handler. Initializes the `bag` and `slot` members to `0` and registers the packet with the `CMSG_READ_ITEM` opcode via the base `ClientPacket` constructor.

---

<!-- machine-true, projected from graph.json -->

## Map — ReadItem

*Source:* Item.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ReadItem | ctor | — | — | — |
