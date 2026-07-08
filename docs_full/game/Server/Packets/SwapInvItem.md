# SwapInvItem

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# SwapInvItem

**Purpose & Responsibilities**

`SwapInvItem` is a client-side packet structure within the `WorldPackets::Item` namespace, defined in `Item.h`. It represents the `CMSG_SWAP_INV_ITEM` message sent by the game client to the server. Its sole responsibility is to encapsulate the raw data required to request a swap of two items within the player's primary inventory slots (excluding bags). The structure holds two 8-bit unsigned integers: `srcslot`, identifying the source inventory slot, and `dstslot`, identifying the destination inventory slot. As a `ClientPacket` derivative, it provides the mechanism to deserialize this binary data from the network stream via its `ReadFromWorldPacket` method.

**Member-by-Member Behavior**

The unit consists of a single constructor and two public data members, alongside an inherited virtual method for deserialization.

*   **Constructor (`SwapInvItem`)**: Initializes the packet object. It invokes the base class `ClientPacket` constructor, registering the packet type as `CMSG_SWAP_INV_ITEM`. It also initializes the `srcslot` and `dstslot` members to zero. This default initialization ensures that if the packet is instantiated but not fully populated from a network stream, the slot indices remain in a known safe state.
*   **Data Members (`srcslot`, `dstslot`)**: These `uint8` fields store the slot indices for the item swap operation. In the context of World of Warcraft's inventory system, these typically refer to the fixed equipment slots (head, neck, shoulders, etc.) or the main backpack slots, depending on how the client distinguishes between "inventory" and "bags" in this specific opcode. The use of `uint8` implies a maximum of 256 slots, which is sufficient for standard inventory limits.
*   **Deserialization (`ReadFromWorldPacket`)**: Although declared in the base class hierarchy, the implementation of `ReadFromWorldPacket` (not shown in the provided source snippet but implied by the class definition) is responsible for extracting the `srcslot` and `dstslot` values from the incoming `WorldPacket` buffer. This method is overridden to handle the specific binary layout of `CMSG_SWAP_INV_ITEM`.

**Cross-Unit Boundaries**

*   **Calls Out**: The `SwapInvItem` constructor calls into the `ClientPacket` base class constructor. This establishes the packet's identity within the world server's message handling framework.
*   **Called By**: The MAP indicates no external callers. However, in practice, instances of `SwapInvItem` are created by the network layer when a `CMSG_SWAP_INV_ITEM` message is received. The network handler will then pass this object to the appropriate game logic handler (likely in a unit such as `PlayerHandler` or `ItemHandler`, though not listed in the MAP) to execute the actual inventory swap. The `ReadFromWorldPacket` method is called by the network infrastructure to populate the object's fields.

**Data Model**

This unit does not interact directly with any database tables. It operates purely on transient network data representing a user action. Any persistence resulting from the swap operation would be handled by downstream logic in other units, potentially updating tables like `character_inventory` or similar, but `SwapInvItem` itself contains no SQL queries or table references.

**Notable Implementation Details**

*   **Minimalist Design**: The class is a simple data carrier with no business logic. All validation (e.g., checking if slots are valid, if items exist, if the swap is allowed) occurs in the handler that processes this packet, not within the packet class itself.
*   **Default Initialization**: Both `srcslot` and `dstslot` are explicitly initialized to `0` in the class definition. This is a defensive coding practice to prevent uninitialized memory reads if the deserialization step fails or is skipped.
*   **Opcode Specificity**: The packet is tied strictly to `CMSG_SWAP_INV_ITEM`. This distinguishes it from `SwapItem` (which involves bags) or `AutoEquipItem` (which moves items to equipment slots automatically). The separation suggests distinct handling paths or validation rules for direct inventory swaps versus bag-related operations.

## Member Reference

**SwapInvItem**
The default constructor for the `SwapInvItem` packet. It initializes the base `ClientPacket` with the opcode `CMSG_SWAP_INV_ITEM` and sets both `srcslot` and `dstslot` to `0`. This prepares the object to receive data from the network stream.

---

<!-- machine-true, projected from graph.json -->

## Map — SwapInvItem

*Source:* Item.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| SwapInvItem | ctor | — | — | — |
