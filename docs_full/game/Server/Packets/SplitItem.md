# SplitItem

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# SplitItem

**SplitItem** is a client-side network packet structure within the `WorldPackets::Item` namespace, responsible for representing the `CMSG_SPLIT_ITEM` message sent from the game client to the server. Its sole responsibility is to deserialize raw binary data from a `WorldPacket` into structured fields that describe an item-splitting operation. This operation typically involves moving a specific quantity of stackable items from a source inventory slot to a destination inventory slot.

The class contains no executable logic beyond its constructor and the declaration of the `ReadFromWorldPacket` method. The actual implementation of `ReadFromWorldPacket` is not present in this unit (`Item.h`) and is presumably defined in a corresponding `.cpp` file or another partial not included in the provided source. Consequently, **SplitItem** serves purely as a data contract and interface definition for the item splitting protocol.

## Member-by-Member Behavior

### Constructor
The **SplitItem** constructor initializes the packet object. It sets the packet opcode to `CMSG_SPLIT_ITEM` via the base class `ClientPacket` constructor and initializes all member variables (`srcbag`, `srcslot`, `dstbag`, `dstslot`, `count`) to zero. This ensures a clean state before deserialization occurs.

### Data Fields
The class exposes five public `uint8` fields that define the parameters of the split request:
*   **srcbag**: The index of the source bag containing the items to be split.
*   **srcslot**: The index of the source slot within the source bag.
*   **dstbag**: The index of the destination bag where the split items will be placed.
*   **dstslot**: The index of the destination slot within the destination bag.
*   **count**: The number of items to move from the source to the destination.

These fields are populated by the `ReadFromWorldPacket` method (declared but not implemented in this unit) when the server receives the raw packet data from the client.

## Cross-Unit Boundaries

**SplitItem** interacts with the following external units:

*   **ClientPacket (Base Class)**: Inherits from `ClientPacket` to gain access to packet handling infrastructure, including the opcode management and the pure virtual `ReadFromWorldPacket` interface. The constructor calls `ClientPacket`'s constructor to register the `CMSG_SPLIT_ITEM` opcode.
*   **WorldPacket (Parameter)**: The `ReadFromWorldPacket` method accepts a `WorldPacket` reference, which provides the raw byte stream from the network connection. This unit relies on `WorldPacket` to provide the underlying data extraction mechanisms, though the specific extraction logic is not visible in this header.

No other units call into **SplitItem** directly according to the provided MAP. It is instantiated and processed by higher-level packet routing or handler logic (not shown in this unit) that dispatches `CMSG_SPLIT_ITEM` messages to this specific packet type for deserialization.

## Data Model

This unit does not interact with any database tables. It operates entirely on transient network data received from the client.

## Notable Implementation Details

*   **Zero Initialization**: All member variables are explicitly initialized to `0` in the class definition. This is a defensive measure to ensure that if `ReadFromWorldPacket` fails or is not called, the fields hold predictable default values rather than garbage data.
*   **Opcode Binding**: The constructor binds this class to the specific network opcode `CMSG_SPLIT_ITEM`. This allows the server's packet dispatcher to identify incoming split-item requests and route them to an instance of this class for parsing.
*   **Missing Implementation**: The critical logic for parsing the packet data resides in the `ReadFromWorldPacket` method, which is declared here but not defined. Engineers maintaining this code must look to the corresponding `.cpp` file (likely `Item.cpp` or similar) to understand how the binary data is mapped to the `srcbag`, `srcslot`, etc., fields.

## Member Reference

**SplitItem**
Constructor for the `SplitItem` packet. Initializes the base `ClientPacket` with the opcode `CMSG_SPLIT_ITEM` and sets all member variables (`srcbag`, `srcslot`, `dstbag`, `dstslot`, `count`) to zero.

---

<!-- machine-true, projected from graph.json -->

## Map — SplitItem

*Source:* Item.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| SplitItem | ctor | — | — | — |
