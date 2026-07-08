# SetAmmo

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# SetAmmo

## Purpose & Responsibilities

`SetAmmo` is a client-to-server network packet structure within the `WorldPackets::Item` namespace. It represents the `CMSG_SET_AMMO` message sent by the game client to inform the server that the player has selected a specific item to serve as their current ammunition. This packet is part of the broader item management subsystem, handling discrete actions related to inventory manipulation, equipment, and vendor interactions.

The class itself is a data carrier; it contains no business logic. Its sole responsibility is to define the binary layout of the incoming network message and provide a mechanism (`ReadFromWorldPacket`) to deserialize raw network bytes into structured C++ fields. Specifically, it extracts a single `uint32` value representing the item entry ID of the ammo being equipped.

## Member-by-Member Behavior

### **SetAmmo** (Constructor)
The constructor initializes the `SetAmmo` object. It performs two key actions:
1.  **Base Initialization**: It calls the base class constructor `ClientPacket(CMSG_SET_AMMO)`, registering this packet instance with the opcode `CMSG_SET_AMMO`. This opcode allows the server's network dispatcher to identify the packet type and route it to the appropriate handler.
2.  **Member Initialization**: It initializes the public member `item` to `0`. This ensures that if the deserialization process fails or is skipped, the field holds a known default state rather than garbage memory.

## Cross-Unit Boundaries

*   **Calls Out**: None. The `SetAmmo` class does not invoke functions in other units. It is a passive data structure.
*   **Called By**: The packet is instantiated and processed by the server's network layer. Specifically, the `WorldSession` or equivalent network handler (not shown in this unit) will create an instance of `SetAmmo`, call `ReadFromWorldPacket` to populate it, and then pass it to the game logic handler responsible for equipping ammo. The handler will likely access the `item` member to validate the item ID and update the player's character data.

## Data Model

This unit does not interact directly with any database tables. It operates entirely on transient network data. The `item` field corresponds to an item entry ID found in the game's static item database (typically `item_template`), but `SetAmmo` itself performs no SQL queries or table lookups.

## Notable Implementation Details

*   **Minimal Payload**: The packet carries only a single `uint32` (`item`). This reflects the simplicity of the "set ammo" action: the client simply tells the server "use item ID X as ammo." The server is expected to verify that the player possesses this item, that it is a valid ammo type, and that it fits the requirements of the currently equipped ranged weapon.
*   **Default Value Safety**: Initializing `item` to `0` in the constructor is a defensive measure. An item ID of `0` is invalid, so if the packet parsing fails, subsequent logic can easily detect an error state rather than attempting to equip a non-existent or random item.
*   **Namespace Isolation**: The class resides in `WorldPackets::Item`, clearly segregating item-related network messages from other subsystems (like combat, chat, or movement). This aids in maintainability and logical grouping of network handlers.
*   **Final Class**: The class is marked `final`, indicating it cannot be inherited. This is appropriate for a leaf-node packet structure where polymorphism is unnecessary.

## Member Reference

**SetAmmo**
Constructor for the `SetAmmo` packet. Initializes the base `ClientPacket` with the opcode `CMSG_SET_AMMO` and sets the `item` member to `0`.

---

<!-- machine-true, projected from graph.json -->

## Map — SetAmmo

*Source:* Item.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| SetAmmo | ctor | — | — | — |
