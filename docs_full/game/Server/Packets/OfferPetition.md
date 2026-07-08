# OfferPetition

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# OfferPetition

**OfferPetition** is a client-to-server network packet structure within the `WorldPackets::Petition` namespace, representing the `CMSG_OFFER_PETITION` message. It encapsulates the data required for a player to offer a petition item to another player, typically as part of the guild creation or signing workflow in the game. As a `ClientPacket`, its primary responsibility is to deserialize binary data received from the client into structured C++ fields (`itemGuid` and `playerGuid`) so that higher-level game logic can process the offer request.

This unit contains only the constructor and the declaration of the `ReadFromWorldPacket` method. The actual deserialization logic is implemented in the corresponding `.cpp` file (not provided in the source snippet but implied by the virtual override), while the constructor initializes the packet type identifier.

## Member-by-Member Behavior

### Initialization and Construction
The **OfferPetition** constructor is responsible for initializing the packet object. It explicitly sets the packet opcode to `CMSG_OFFER_PETITION` via the base class `ClientPacket` constructor. This ensures that when the packet is processed by the server's message dispatcher, it is correctly routed to the handler responsible for petition offers. The constructor takes no arguments, relying on default initialization for the member variables `itemGuid` and `playerGuid`.

### Data Deserialization
Although the implementation of `ReadFromWorldPacket` is not visible in the provided header, its signature indicates that it overrides the base class method to parse the incoming `WorldPacket`. Based on the member variables declared in the class, this method is expected to extract two `ObjectGuid` values from the raw binary stream:
1.  **itemGuid**: The unique identifier of the petition item being offered.
2.  **playerGuid**: The unique identifier of the target player who is receiving the offer.

These fields are populated during the read operation, making them available for subsequent validation and processing by the game server logic.

## Cross-Unit Boundaries

*   **Calls out**: None. The `OfferPetition` class itself does not call into other units. Its dependency is on the `WorldPacket` class (from the networking layer) for reading data, and on `ObjectGuid` for identity representation.
*   **Called by**: The MAP indicates no specific callers from other units. In practice, this packet is instantiated and populated by the network input handler when a client sends the `CMSG_OFFER_PETITION` opcode. The resulting object is then passed to the game logic handler (likely in a separate unit such as a Guild or Petition handler) which is not listed in the "Called by" column of this specific MAP.

## Data Model

This unit does not directly interact with any database tables. It operates purely on in-memory network data structures. The `itemGuid` and `playerGuid` fields correspond to entities that likely exist in database tables (such as `character_petitions` or `characters`), but the `OfferPetition` class itself performs no SQL queries or direct table access.

## Notable Implementation Details

*   **Final Class**: The class is marked as `final`, preventing further inheritance. This is consistent with its role as a leaf node in the packet hierarchy.
*   **Explicit Constructor**: The constructor is marked `explicit`, preventing implicit conversions from the `CMSG_OFFER_PETITION` opcode value to an `OfferPetition` object.
*   **Default Initialization**: The member variables `itemGuid` and `playerGuid` are default-initialized. Since `ObjectGuid` is a value type, this ensures they are zeroed out before `ReadFromWorldPacket` is called, providing a safe initial state.

## Member Reference

**OfferPetition**
Constructor for the `OfferPetition` packet. Initializes the base `ClientPacket` with the opcode `CMSG_OFFER_PETITION`. Does not take any arguments.

---

<!-- machine-true, projected from graph.json -->

## Map — OfferPetition

*Source:* Petition.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| OfferPetition | ctor | — | — | — |
