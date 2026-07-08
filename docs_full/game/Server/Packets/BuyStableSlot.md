# BuyStableSlot

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# BuyStableSlot

**Purpose & Responsibilities**

`BuyStableSlot` is a client-side packet structure within the `WorldPackets::Npc` namespace, responsible for representing the `CMSG_BUY_STABLE_SLOT` message sent from the game client to the server. Its sole responsibility is to carry the `ObjectGuid` of the Non-Player Character (NPC) that the player intends to interact with to purchase an additional stable slot for pets. As a `ClientPacket`, it serves as the data container for deserializing this specific network request. It contains no business logic, state management, or database interactions itself; those concerns are handled by the server-side handlers that receive instances of this class after deserialization.

**Member-by-Member Behavior**

The unit consists of a single class, `BuyStableSlot`, which inherits from `ClientPacket`.

*   **Data Member**: `npcGuid` (`ObjectGuid`)
    *   This member stores the unique identifier of the NPC vendor. It is populated during the deserialization process via the inherited `ReadFromWorldPacket` method (implemented in the base class or a related handler, though the declaration is in `ClientPacket`). The value represents the target of the transaction.

*   **Constructor**: `BuyStableSlot()`
    *   This explicit constructor initializes the packet object. It calls the base class constructor `ClientPacket(CMSG_BUY_STABLE_SLOT)`, registering the packet type constant `CMSG_BUY_STABLE_SLOT` with the base class infrastructure. This registration is essential for the server's packet routing system to identify incoming raw bytes as a "Buy Stable Slot" request and instantiate the correct `BuyStableSlot` object for processing.

**Cross-Unit Boundaries**

*   **Calls Out**: None. The `BuyStableSlot` class does not invoke methods in other units. It is a passive data structure.
*   **Called By**: None listed in the map. In practice, this class is instantiated by the server's network layer (likely within `WorldSession` or a packet factory) when a `CMSG_BUY_STABLE_SLOT` opcode is detected on the wire. After instantiation and deserialization, it is passed to a handler function (not part of this unit) that executes the actual logic of checking costs, verifying NPC type, and updating the player's stable slots.

**Data Model**

This unit does not interact with any database tables. It operates entirely in memory as a transient representation of a network message. No SQL queries are present in this source file.

**Notable Implementation Details**

*   **Inheritance**: The class inherits from `ClientPacket`, implying it shares common functionality for packet identification and potentially serialization/deserialization hooks with other client-to-server messages.
*   **Explicit Constructor**: The use of `explicit` prevents implicit conversions from other types, ensuring that `BuyStableSlot` objects are created intentionally.
*   **Namespace**: It resides in `WorldPackets::Npc`, grouping it logically with other NPC-related interactions such as `GossipHello`, `TrainerList`, and `BankerActivate`. This suggests a modular design where packet types are categorized by the type of game entity or interaction they represent.
*   **Minimal State**: The class holds only the `npcGuid`. It does not store the cost of the slot, the player's current slot count, or success/failure flags. These are either derived from the NPC's template/data on the server side or returned via a separate server-to-client packet.

## Member Reference

**BuyStableSlot**
Constructor for the `BuyStableSlot` packet. Initializes the base `ClientPacket` with the opcode `CMSG_BUY_STABLE_SLOT`. It prepares the object to receive and hold the `npcGuid` of the target NPC for stable slot purchases.

---

<!-- machine-true, projected from graph.json -->

## Map — BuyStableSlot

*Source:* Npc.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| BuyStableSlot | ctor | — | — | — |
