# LootRoll

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# LootRoll

**Purpose & Responsibilities**

`LootRoll` is a client-facing packet structure within the `WorldPackets::Loot` namespace, responsible for deserializing the `CMSG_LOOT_ROLL` message sent by the game client. Its sole responsibility is to extract three specific fields from the incoming binary stream: the GUID of the target being looted, the index of the item slot involved in the roll, and the type of roll requested (e.g., Need, Greed, or Pass). It acts as a data carrier, converting raw network bytes into strongly-typed C++ members for subsequent processing by the server’s loot management system.

This unit is a leaf node in the call graph; it performs no outbound calls to other units and is not called by any other units outside of its own construction and reading process (as indicated by the empty "Calls out" and "Called by" columns in the MAP). It does not interact with any database tables.

## Member-by-Member Behavior

### **LootRoll** (Constructor)
The constructor initializes the `LootRoll` object. It sets the base `ClientPacket` identifier to `CMSG_LOOT_ROLL`, ensuring the server correctly identifies the message type during dispatch. It also initializes the member variables:
- `lootedTarget`: An `ObjectGuid` representing the entity (creature, game object, etc.) from which the item was looted.
- `itemSlot`: A `uint32` initialized to `0`, representing the index of the item in the loot window.
- `rollType`: A `uint8` initialized to `0`, representing the player's choice in the roll (e.g., 0 for Need, 1 for Greed, 2 for Pass, though specific enum values depend on the broader codebase definitions not present here).

The constructor does not perform any validation or complex logic; it merely prepares the object for data ingestion via `ReadFromWorldPacket`.

## Cross-Unit Boundaries

As per the MAP, `LootRoll` has no explicit outbound calls to other units listed in the cross-reference. However, it inherits from `ClientPacket` (defined in `Packet.h`), implying reliance on that base class for packet identification and potentially logging or error handling infrastructure. The `ReadFromWorldPacket` method (declared in the header but implemented elsewhere, likely in a corresponding `.cpp` file not provided in the SOURCE snippet) will parse the `WorldPacket` argument. While the implementation of `ReadFromWorldPacket` is not visible in the provided SOURCE, its signature indicates it consumes a `WorldPacket&` to populate the `lootedTarget`, `itemSlot`, and `rollType` members.

The MAP indicates no "Called by" entries, suggesting that the instantiation and usage of `LootRoll` occur within the packet handling pipeline, likely triggered by the network layer when a `CMSG_LOOT_ROLL` opcode is detected. The actual business logic for processing the roll (validating eligibility, updating loot states, notifying players) resides in other units that consume this populated `LootRoll` object after deserialization.

## Data Model

This unit does not interact with any database tables. It operates entirely in memory, processing transient network data.

## Notable Implementation Details

1.  **Default Initialization**: The members `itemSlot` and `rollType` are explicitly initialized to `0` in the class definition. This ensures that even if `ReadFromWorldPacket` fails or is not called, these fields hold a known default state rather than garbage values.
2.  **GUID Handling**: The `lootedTarget` is an `ObjectGuid`. The server uses this GUID to locate the corresponding world object (e.g., a corpse or chest) to verify that the loot window is still open and that the item at `itemSlot` exists and is eligible for rolling.
3.  **Passive Data Structure**: `LootRoll` contains no business logic. It is a pure data structure. All validation, state changes, and side effects are deferred to the caller that receives the populated instance.
4.  **Namespace Isolation**: It resides in `WorldPackets::Loot`, clearly segregating loot-related network protocols from other game systems.

## Member Reference

**LootRoll**
Constructor for the `LootRoll` packet. Initializes the packet opcode to `CMSG_LOOT_ROLL` and sets default values for `itemSlot` (0) and `rollType` (0). Prepares the object to receive deserialized data from the client.

---

<!-- machine-true, projected from graph.json -->

## Map — LootRoll

*Source:* Loot.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| LootRoll | ctor | — | — | — |
