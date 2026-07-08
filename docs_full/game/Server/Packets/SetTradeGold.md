# SetTradeGold

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# SetTradeGold

**SetTradeGold** is a client-side packet structure within the `WorldPackets::Trade` namespace, responsible for representing the `CMSG_SET_TRADE_GOLD` message sent from the game client to the server. Its sole purpose is to carry the amount of gold a player intends to offer in a trade session.

As a `ClientPacket`, it inherits the standard serialization and deserialization infrastructure required to parse incoming network data. The class contains a single public data member, `gold`, which stores the monetary value being proposed. It does not perform any business logic, validation, or database interaction itself; it is purely a data container for the network layer.

## Member Reference

**SetTradeGold**
Constructor for the `SetTradeGold` packet. It initializes the base `ClientPacket` class with the opcode `CMSG_SET_TRADE_GOLD`. This ensures that when the packet is processed by the server's network handler, it is routed to the appropriate trade-handling logic. The constructor also default-initializes the `gold` member to `0`.

---

<!-- machine-true, projected from graph.json -->

## Map — SetTradeGold

*Source:* Trade.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| SetTradeGold | ctor | — | — | — |
