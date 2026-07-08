# SetFactionAtWar

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# SetFactionAtWar

**Purpose & Responsibilities**

`SetFactionAtWar` is a client-side packet structure within the `WorldPackets::Misc` namespace. Its sole responsibility is to represent the raw data payload of the `CMSG_SET_FACTION_ATWAR` network message sent by the game client to the server. This packet conveys a player's intent to change their "at war" status with a specific faction, identified by a reputation list ID. It acts as a data container, holding the faction identifier (`repListId`) and the desired state flag (`flag`) until the packet is processed by higher-level game logic handlers (not defined in this unit).

**Member-by-Member Behavior**

The unit consists of a single class, `SetFactionAtWar`, which inherits from `ClientPacket`.

*   **Data Members**:
    *   `repListId` (`uint32`): Stores the unique identifier for the faction's reputation list. Initialized to `0` in the constructor.
    *   `flag` (`uint8`): Stores the boolean-like state indicating whether the player wishes to be "at war" with the faction. Initialized to `0` in the constructor.

*   **Constructor**:
    *   `explicit SetFactionAtWar()`: Initializes the base `ClientPacket` with the opcode `CMSG_SET_FACTION_ATWAR`. It also initializes the data members `repListId` and `flag` to `0`. The `explicit` keyword prevents implicit conversions from other types.

*   **Virtual Method**:
    *   `ReadFromWorldPacket(WorldPacket& recv_data)`: Declared as an override of the base class virtual function. While the declaration is present in this header, the implementation (which parses the binary stream from `recv_data` into `repListId` and `flag`) is located in the corresponding `.cpp` file (not provided in the source snippet, but implied by the `override` specifier and standard packet handling patterns). This method is responsible for deserializing the incoming network data.

**Cross-Unit Boundaries**

*   **Calls Out**: None. The `SetFactionAtWar` class itself does not call any other units. Its constructor calls the base class `ClientPacket` constructor, but this is internal inheritance initialization.
*   **Called By**: None listed in the MAP. In practice, instances of this class are typically created by a central packet dispatcher or router (outside this unit) when a `CMSG_SET_FACTION_ATWAR` opcode is detected on the network socket. The dispatcher would then invoke `ReadFromWorldPacket` to populate the data members before passing the object to a handler function (also outside this unit) that processes the faction war status change.

**Data Model**

This unit does not interact directly with any database tables. It operates purely on network packet data. Any persistence of faction war status would occur in higher-level game logic handlers that consume this packet, potentially updating tables such as `character_faction_at_war` or similar, but such interactions are not visible in this translation unit.

**Notable Implementation Details**

*   **Final Class**: The class is marked `final`, preventing further inheritance. This is appropriate for a leaf-node packet structure.
*   **Initialization**: Both data members are explicitly initialized to `0` in the constructor. This ensures a known default state even if `ReadFromWorldPacket` fails or is not called (though in normal operation, it should always be called before use).
*   **Opcode Association**: The constructor binds this structure to the specific network opcode `CMSG_SET_FACTION_ATWAR`, ensuring type safety and correct routing within the packet handling system.
*   **Namespace**: Located in `WorldPackets::Misc`, grouping it with other miscellaneous client-to-server messages that do not fit into more specific categories like combat, movement, or chat.

## Member Reference

**SetFactionAtWar**
Constructor for the `SetFactionAtWar` packet. Initializes the base `ClientPacket` with the opcode `CMSG_SET_FACTION_ATWAR` and sets the `repListId` and `flag` data members to `0`. It is declared `explicit` to prevent implicit conversions. This member prepares the object to receive and hold data from an incoming network packet regarding a player's desire to change their war status with a specific faction.

---

<!-- machine-true, projected from graph.json -->

## Map — SetFactionAtWar

*Source:* Misc.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| SetFactionAtWar | ctor | — | — | — |
