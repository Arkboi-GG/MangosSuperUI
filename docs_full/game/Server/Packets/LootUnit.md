# LootUnit

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# LootUnit

**Purpose & Responsibilities**

`LootUnit` is a client-to-server packet structure within the `WorldPackets::Loot` namespace. Its sole responsibility is to deserialize the `CMSG_LOOT` message received from a game client. This message indicates that a player has initiated the looting process for a specific entity (such as a corpse or object). The class extracts the target entity's unique identifier (`ObjectGuid`) from the raw network data so that the server-side handler can locate the corresponding world object and generate the appropriate loot table.

As a `ClientPacket`, it inherits the base functionality required for network deserialization but contains no business logic, state management, or database interactions. It is a pure data carrier for the initial step of the loot interaction flow.

## Member-by-Member Behavior

### **LootUnit** (Constructor)
The constructor initializes the packet instance. It sets the internal packet opcode to `CMSG_LOOT`, identifying the message type to the network layer. It leaves the `guid` member uninitialized (default constructed `ObjectGuid`), expecting it to be populated during the deserialization phase.

## Cross-Unit Boundaries

*   **Called by:** This unit is instantiated and processed by the server's network handler. The handler creates an instance of `LootUnit`, calls the inherited `ReadFromWorldPacket` method to populate the `guid`, and then passes the populated object to the game logic handler responsible for processing loot requests.
*   **Calls out:** None. The unit does not invoke any other classes or functions directly in its own scope. It relies on the `WorldPacket` class (from the `Packet` unit) for stream reading operations, which are handled within the `ReadFromWorldPacket` implementation inherited from `ClientPacket`.

## Data Model

This unit does not interact with any database tables. It operates solely on runtime memory structures derived from network packets.

## Notable Implementation Details

*   **Minimalist Design:** The class contains only one data member (`guid`) and relies on the inherited `ReadFromWorldPacket` method. There is no validation logic within the packet itself; validity checks (e.g., whether the GUID exists, whether the player is close enough, whether the object is already looted) are performed by the server-side handler that consumes this packet.
*   **Public Member Access:** The `guid` member is public, allowing direct access by the consuming handler without needing getter methods. This is consistent with the lightweight nature of packet structures in this codebase.
*   **Inheritance:** Inherits from `ClientPacket`, which manages the opcode registration and basic packet lifecycle. The `explicit` constructor prevents implicit conversions from other types.

## Member Reference

**LootUnit**
Constructor that initializes the packet with the `CMSG_LOOT` opcode. It prepares the object for deserialization but does not populate the `guid` field.

---

<!-- machine-true, projected from graph.json -->

## Map — LootUnit

*Source:* Loot.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| LootUnit | ctor | — | — | — |
