# AutoEquipItemSlot

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AutoEquipItemSlot

## Purpose & Responsibilities

`AutoEquipItemSlot` is a client-to-server network packet handler within the `WorldPackets::Item` namespace. Its sole responsibility is to deserialize the binary data of the `CMSG_AUTOEQUIP_ITEM_SLOT` message received from a game client. This message represents a user action where a specific item, identified by its unique global identifier (`ObjectGuid`), is requested to be moved into a specific equipment slot (`dstslot`) on the character.

As a `ClientPacket`, this class does not contain business logic for validating the move, checking inventory space, or updating the database. It strictly serves as a data structure that extracts the raw parameters from the incoming network stream so that higher-level server systems (such as the player inventory manager or command processor) can interpret the request.

## Member-by-Member Behavior

The unit contains only one member: the constructor.

### **AutoEquipItemSlot**
This is the explicit default constructor for the `AutoEquipItemSlot` class. It performs two initialization tasks:
1.  It invokes the base class constructor `ClientPacket(CMSG_AUTOEQUIP_ITEM_SLOT)`, registering this packet instance with the specific opcode `CMSG_AUTOEQUIP_ITEM_SLOT`. This allows the network layer to route incoming packets with this opcode to the correct deserialization handler.
2.  It initializes the member variables `itemGuid` and `dstslot`. While `dstslot` is explicitly initialized to `0` in the class definition, `itemGuid` relies on the default constructor of `ObjectGuid`.

The actual deserialization logic is handled by the virtual method `ReadFromWorldPacket`, which is declared in the header but implemented elsewhere (not part of this unit's source). The constructor ensures the object is in a valid state before that method is called.

## Cross-Unit Boundaries

*   **Calls out:** None. The constructor does not invoke any functions in other units.
*   **Called by:** None listed in the MAP. In practice, this constructor is called by the network packet dispatching system (likely within `WorldSession` or a central packet router) when a `CMSG_AUTOEQUIP_ITEM_SLOT` packet arrives. The dispatcher creates an instance of this class to hold the parsed data.

## Data Model

This unit does not interact directly with any database tables. It operates entirely on in-memory network packet data. The `itemGuid` field corresponds to the unique identifier of an item instance, which ultimately maps to rows in tables like `character_inventory` or `item_instance` in the database, but this mapping occurs in higher-level logic outside this unit.

## Notable Implementation Details

*   **Namespace Structure:** The class is nested within `WorldPackets::Item`, indicating it is part of a modular packet handling system where item-related messages are grouped together.
*   **Final Class:** The class is marked `final`, preventing inheritance. This is appropriate for a leaf-node data structure in a packet hierarchy.
*   **Default Initialization:** The `dstslot` member is initialized to `0` in the declaration. This provides a safe default value if the packet reading fails or if the slot index is not explicitly set during construction (though `ReadFromWorldPacket` will overwrite it).
*   **ObjectGuid Usage:** The use of `ObjectGuid` for `itemGuid` suggests that the item being equipped is already known to the server (i.e., it exists in the player's inventory or bank). This differs from packets that might use an item entry ID (`uint32`) for items not yet instantiated.

## Member Reference

**AutoEquipItemSlot**
Constructor for the `AutoEquipItemSlot` packet. Initializes the base `ClientPacket` with the opcode `CMSG_AUTOEQUIP_ITEM_SLOT` and prepares the object to receive deserialized data for `itemGuid` and `dstslot`.

---

<!-- machine-true, projected from graph.json -->

## Map — AutoEquipItemSlot

*Source:* Item.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AutoEquipItemSlot | ctor | — | — | — |
