# LootMethod

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Group.LootMethod

**Purpose & Responsibilities**

`LootMethod` is a client-side packet structure within the `WorldPackets::Group` namespace, responsible for deserializing the `CMSG_LOOT_METHOD` message sent by the game client. Its primary responsibility is to capture the player's requested changes to group loot distribution settings, specifically the loot method type, the designated loot master, and the item quality threshold for automatic looting. As a `ClientPacket`, it serves as the data carrier for incoming network traffic, holding the raw values extracted from the binary stream before they are processed by higher-level game logic.

**Member-by-Member Behavior**

The unit contains a single member: the constructor.

*   **`LootMethod` (Constructor):** This default constructor initializes the `LootMethod` instance. It sets the base class `ClientPacket` identifier to `CMSG_LOOT_METHOD`, ensuring the packet is correctly routed during the server's dispatch phase. It also initializes the three public data members (`lootMethod`, `lootMaster`, and `lootThreshold`) to their default states (zero/null) via in-class initializers defined in the header. The constructor itself performs no I/O or complex logic; it merely prepares the object for subsequent population by the `ReadFromWorldPacket` method (which is declared in the header but implemented in the corresponding `.cpp` file, not part of this specific partial's visible behavior in the provided source snippet, though the header defines the interface).

**Cross-Unit Boundaries**

*   **Calls Out:** None. The constructor does not invoke any external functions or classes.
*   **Called By:** None listed in the map. In practice, this constructor is called by the packet factory or dispatcher system when a `CMSG_LOOT_METHOD` opcode is detected on the wire, but these callers are outside the scope of the provided map.

**Data Model**

This unit does not interact with any database tables. It operates entirely in memory as part of the network packet handling layer.

**Notable Implementation Details**

*   **Inheritance:** `LootMethod` inherits from `ClientPacket`, indicating it represents data flowing from the client to the server.
*   **Default Initialization:** The members `lootMethod` and `lootThreshold` are initialized to `0`, and `lootMaster` to a default-constructed `ObjectGuid`. This ensures that if the packet reading process fails or is incomplete, the fields hold safe, neutral values rather than garbage data.
*   **Opcode Association:** The constructor explicitly binds this class to `CMSG_LOOT_METHOD`, which is the specific network opcode used by the World of Warcraft client to communicate loot settings changes.

## Member Reference

**LootMethod** (ctor): Default constructor that initializes the packet with the `CMSG_LOOT_METHOD` opcode and sets default values for `lootMethod` (0), `lootMaster` (empty Guid), and `lootThreshold` (0).

---

<!-- machine-true, projected from graph.json -->

## Map — LootMethod

*Source:* Group.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| LootMethod | ctor | — | — | — |
