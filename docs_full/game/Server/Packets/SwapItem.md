# SwapItem

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# SwapItem

## Purpose & Responsibilities

`SwapItem` is a lightweight data structure within the `WorldPackets::Item` namespace, designed to represent the client-side request for swapping two items between inventory slots. It inherits from `ClientPacket`, marking it as a message originating from the game client and destined for the server. Its sole responsibility is to hold the raw parameters—source and destination bag and slot indices—extracted from the network packet identified by `CMSG_SWAP_ITEM`.

This unit does not contain business logic, validation, or database interactions. It is purely a transport container for the swap operation's coordinates.

## Member-by-Member Behavior

The unit contains only one member: the constructor.

*   **Construction**: The default constructor initializes the base `ClientPacket` with the opcode `CMSG_SWAP_ITEM`. It also zero-initializes all four member variables (`dstbag`, `dstslot`, `srcbag`, `srcslot`) via in-class initializers. This ensures that if the packet reading process fails or is incomplete, the fields remain in a known safe state (zero) rather than containing garbage memory.

## Cross-Unit Boundaries

*   **Called By**: As indicated by the empty "Called by" column in the MAP, this specific constructor is not directly invoked by other documented units in the provided scope. In practice, instances of `SwapItem` are typically created by the packet dispatching system (likely within `WorldSession` or a similar handler) when a `CMSG_SWAP_ITEM` packet is received from the network. The dispatcher constructs this object, populates it via `ReadFromWorldPacket` (defined in the shared header but implemented elsewhere), and then passes it to the appropriate handler logic.
*   **Calls Out**: The constructor calls the base class constructor `ClientPacket(CMSG_SWAP_ITEM)`. This establishes the packet's identity within the server's networking layer.

## Data Model

This unit does not interact with any database tables. It operates entirely on transient network data.

## Notable Implementation Details

*   **Zero-Initialization**: All `uint8` fields (`dstbag`, `dstslot`, `srcbag`, `srcslot`) are initialized to `0` in the class definition. This is a defensive coding practice common in packet structures to prevent undefined behavior if the deserialization step (`ReadFromWorldPacket`) is skipped or fails to read all expected bytes.
*   **Final Class**: The class is marked `final`, indicating it is not intended to be subclassed. This aligns with its role as a simple data holder.
*   **Namespace**: It resides in `WorldPackets::Item`, grouping it logically with other item-related network messages like `BuyItem`, `SellItem`, and `SplitItem`.

## Member Reference

**SwapItem**
The default constructor for the `SwapItem` packet. It initializes the base `ClientPacket` with the opcode `CMSG_SWAP_ITEM` and sets all bag and slot indices to zero.

---

<!-- machine-true, projected from graph.json -->

## Map — SwapItem

*Source:* Item.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| SwapItem | ctor | — | — | — |
