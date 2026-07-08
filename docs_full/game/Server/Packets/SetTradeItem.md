# SetTradeItem

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# SetTradeItem

## Purpose & Responsibilities

`SetTradeItem` is a client-side packet structure within the `WorldPackets::Trade` namespace, responsible for deserializing the `CMSG_SET_TRADE_ITEM` message sent by the client. This message indicates that a player intends to place a specific item from their inventory into a designated slot in the trade window. The class acts as a data carrier, holding the raw indices (`tradeSlot`, `bag`, `slot`) extracted from the network stream until higher-level game logic processes the request.

## Member-by-Member Behavior

The unit consists of a single constructor and three public data members. Its primary behavioral responsibility lies in the implicit contract of being a `ClientPacket`: it must correctly parse incoming binary data into its member variables via the `ReadFromWorldPacket` method (declared in the shared header but implemented elsewhere).

*   **Data Members**:
    *   `tradeSlot`: An 8-bit unsigned integer representing the target slot index within the trade interface where the item should be placed. Initialized to `0`.
    *   `bag`: An 8-bit unsigned integer representing the source bag index in the player's inventory. Initialized to `0`.
    *   `slot`: An 8-bit unsigned integer representing the source slot index within the specified bag. Initialized to `0`.

*   **Constructor**:
    *   `SetTradeItem()`: A default constructor that initializes the base `ClientPacket` with the opcode `CMSG_SET_TRADE_ITEM`. It does not perform any additional initialization logic, relying on the in-class initializers for the data members.

## Cross-Unit Boundaries

As a leaf node in the call graph for this specific unit (it has no outgoing calls to other units and is not called by other units according to the MAP), `SetTradeItem` operates primarily as a passive data structure during construction. However, its lifecycle involves interaction with the broader packet handling system:

1.  **Inbound Data Flow**: The `WorldPacket` infrastructure (external to this unit) instantiates `SetTradeItem` and invokes its `ReadFromWorldPacket` method. This method reads bytes from the `WorldPacket` object and populates `tradeSlot`, `bag`, and `slot`.
2.  **Outbound Usage**: After parsing, the populated `SetTradeItem` instance is typically passed to the game world handler (e.g., `Player::HandleSetTradeItem` or similar, located in other units like `Player.cpp` or `TradeHandler.cpp`). That handler will validate the `bag` and `slot` indices against the player's inventory, check if the `tradeSlot` is valid, and execute the trade logic.

## Data Model

This unit does not interact directly with any database tables. It processes transient network data related to the immediate state of the trade UI.

## Notable Implementation Details

*   **Default Initialization**: All data members (`tradeSlot`, `bag`, `slot`) are explicitly initialized to `0` in the class definition. This ensures that even if `ReadFromWorldPacket` fails or is not called, the object remains in a known, safe state.
*   **Type Constraints**: The use of `uint8` for all indices implies that the trade window supports a maximum of 256 slots/bags, which is consistent with typical World of Warcraft client constraints (usually much smaller, e.g., 4-5 bags, 6-10 trade slots).
*   **Final Class**: The class is marked `final`, preventing inheritance. This is appropriate for a packet structure, as its binary layout and behavior are fixed by the protocol specification.

## Member Reference

**SetTradeItem**
Constructor for the `SetTradeItem` packet. Initializes the base `ClientPacket` with the opcode `CMSG_SET_TRADE_ITEM`. Relies on in-class initializers for `tradeSlot`, `bag`, and `slot` (all set to `0`).

---

<!-- machine-true, projected from graph.json -->

## Map — SetTradeItem

*Source:* Trade.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| SetTradeItem | ctor | — | — | — |
