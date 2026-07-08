# LootMasterGive

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# LootMasterGive

**Purpose & Responsibilities**

`LootMasterGive` is a client-to-server packet structure within the `WorldPackets::Loot` namespace. It represents the `CMSG_LOOT_MASTER_GIVE` message, which is sent by the client when a player requests to give a specific item from a loot window to another player in a Master Loot mode group. The class is responsible for defining the data layout of this request—specifically identifying the loot source, the item slot, and the recipient—and providing the mechanism to deserialize this data from the raw network buffer.

As a `ClientPacket`, it serves as the input interface for the server's loot distribution logic, ensuring that the necessary identifiers are extracted before the server validates permissions and executes the transfer.

## Member-by-Member Behavior

### Construction and Initialization
The **`LootMasterGive`** constructor initializes the packet object. It sets the packet opcode to `CMSG_LOOT_MASTER_GIVE` via the base `ClientPacket` constructor and initializes the member variables `slotId` to `0`. The `lootGuid` and `playerGuid` are default-constructed `ObjectGuid` instances. This ensures that all fields have defined initial states before deserialization occurs.

## Cross-Unit Boundaries

*   **Calls Out:** None. The `LootMasterGive` class itself does not call into other units; it is a data carrier and parser. The logic that *uses* this packet (likely in a handler such as `WorldSession` or a dedicated loot handler) will call into other units, but those interactions are outside the scope of this class.
*   **Called By:** Other units (not listed in the MAP as they are not part of this specific translation unit's definition) will instantiate `LootMasterGive` and invoke `ReadFromWorldPacket` when the server receives a `CMSG_LOOT_MASTER_GIVE` packet from the client. Typically, this happens in the network layer's packet dispatching mechanism.

## Data Model

This unit does not interact directly with any database tables. It operates entirely on in-memory network packet data.

## Notable Implementation Details

*   **Inheritance from `ClientPacket`:** As a subclass of `ClientPacket`, `LootMasterGive` inherits the contract for handling incoming client messages. This implies that the server is the consumer of this data.
*   **GUID Usage:** The use of `ObjectGuid` for both `lootGuid` and `playerGuid` indicates that the server relies on unique object identifiers to resolve the loot source and the recipient. This allows the server to look up the correct `Creature` or `GameObject` and the target `Player` objects in its world state.
*   **Slot ID:** The `slotId` is a `uint8`, suggesting that the loot list size is expected to fit within 8 bits (0-255). This is a reasonable constraint for typical loot windows.
*   **Final Class:** The class is marked `final`, preventing further inheritance. This enforces a strict, leaf-node design for this specific packet type, ensuring no derived classes alter its behavior.

## Member Reference

**LootMasterGive**
Constructor for the `LootMasterGive` packet. Initializes the base `ClientPacket` with the opcode `CMSG_LOOT_MASTER_GIVE` and sets `slotId` to `0`. Default constructs `lootGuid` and `playerGuid`.

---

<!-- machine-true, projected from graph.json -->

## Map — LootMasterGive

*Source:* Loot.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| LootMasterGive | ctor | — | — | — |
