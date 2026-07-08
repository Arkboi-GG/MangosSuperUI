# AutoEquipItem

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AutoEquipItem

**AutoEquipItem** is a client-side packet definition within the `WorldPackets::Item` namespace, responsible for representing the `CMSG_AUTOEQUIP_ITEM` message sent from the game client to the server. Its sole responsibility is to declare the data structure required to deserialize this specific command, which instructs the server to automatically equip an item located in a specific bag and slot.

This unit is part of the broader packet handling infrastructure (`Packet.h`, `ObjectGuid.h`) and serves as a lightweight data carrier. It contains no business logic, validation, or database interactions. Its behavior is limited to initialization and providing storage for the source bag and slot indices.

## Member-by-Member Behavior

The unit defines a single class, `AutoEquipItem`, which inherits from `ClientPacket`.

*   **Data Members**: The class exposes two public members, `srcbag` and `srcslot`, both of type `uint8`. These fields store the bag index and slot index of the item the player wishes to equip. They are initialized to `0` by default.
*   **Constructor**: The explicit constructor initializes the base `ClientPacket` with the opcode `CMSG_AUTOEQUIP_ITEM`. This registers the packet type with the network layer so that incoming data streams with this opcode are routed to an instance of this class.
*   **Deserialization Interface**: The class overrides the pure virtual function `ReadFromWorldPacket` from its base class. While the declaration is present here, the actual implementation of reading the binary data into `srcbag` and `srcslot` resides in the corresponding `.cpp` file (not provided in the source snippet, but implied by the interface). This method is called by the network handler after the packet is received.

## Cross-Unit Boundaries

*   **Calls Out**: None. The header file contains no function calls to other units. The constructor calls the base class constructor `ClientPacket(...)`, which is part of the `Packet` infrastructure, but this is standard inheritance behavior rather than a logical dependency on external business logic.
*   **Called By**: The MAP indicates no external callers. In practice, instances of `AutoEquipItem` are created by the server's network dispatcher when a `CMSG_AUTOEQUIP_ITEM` packet is received from a client. The dispatcher will then invoke `ReadFromWorldPacket` to populate the fields. After deserialization, the populated object is typically passed to a handler function (likely in a `Player` or `ChatHandler` class, outside this unit) that performs the actual equipping logic.

## Data Model

This unit does not interact with any database tables. It operates entirely on transient network data.

## Notable Implementation Details

*   **Minimalist Design**: As with all classes in the `WorldPackets::Item` namespace shown in `Item.h`, `AutoEquipItem` is a pure data structure. It does not validate whether the bag/slot exists, whether the item is equippable, or whether the target equipment slot is free. All such validation occurs downstream in the server logic that consumes this packet.
*   **Opcode Specificity**: The class is tightly coupled to the `CMSG_AUTOEQUIP_ITEM` opcode. Changing the protocol version or opcode would require updating this constructor.
*   **Default Initialization**: The members `srcbag` and `srcslot` are explicitly initialized to `0` in the class definition. This ensures that even if `ReadFromWorldPacket` fails or is not called, the fields hold a known safe value, preventing undefined behavior if accessed prematurely.

## Member Reference

**AutoEquipItem**
Constructor for the `AutoEquipItem` packet class. Initializes the base `ClientPacket` with the `CMSG_AUTOEQUIP_ITEM` opcode. Sets up the object to receive and store the source bag and slot indices for an auto-equip request.

---

<!-- machine-true, projected from graph.json -->

## Map — AutoEquipItem

*Source:* Item.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AutoEquipItem | ctor | — | — | — |
