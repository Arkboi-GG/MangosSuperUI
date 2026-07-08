# RaidTargetUpdate

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# RaidTargetUpdate

**Purpose & Responsibilities**

`RaidTargetUpdate` is a client-side packet structure within the `WorldPackets::Group` namespace, responsible for handling incoming network data related to raid target icons. Specifically, it processes the `MSG_RAID_TARGET_UPDATE` message sent by the World of Warcraft client to the server. This packet allows players to assign or remove raid target markers (such as stars, moons, squares, etc.) on specific game entities (players, NPCs, or objects) identified by their `ObjectGuid`.

The class is conditionally compiled only for client builds newer than `CLIENT_BUILD_1_10_2`, indicating that this specific packet format and functionality were introduced or standardized in later versions of the game client. It serves as a data container that parses the raw binary stream from the network socket into structured fields (`iconId` and `guid`) that higher-level game logic can interpret.

**Member-by-Member Behavior**

The unit consists of a single constructor and relies on inherited methods for packet processing.

*   **`RaidTargetUpdate()`**: The default constructor initializes the packet instance. It sets the internal packet ID to `MSG_RAID_TARGET_UPDATE` via the base class `ClientPacket` constructor. It also initializes the member variables `iconId` to `0` and `guid` to its default state (an invalid/empty GUID). This preparation ensures the object is in a known state before any network data is read into it.

*   **`ReadFromWorldPacket(WorldPacket& recv_data)`**: Although declared in the shared header `Group.h`, this virtual method is implemented in the corresponding `.cpp` file (not provided in the source snippet but implied by the interface). Its responsibility is to deserialize the incoming `WorldPacket` buffer. Based on the member variables, it extracts a `uint8` value for `iconId` and an `ObjectGuid` for `guid`. The comment in the header notes that `guid` is "only valid when iconId != 0xFF". This implies that an `iconId` of `0xFF` likely represents a request or a special case (such as clearing all targets or querying current targets) where no specific entity GUID is attached, whereas other values represent an assignment of a specific icon to a specific entity.

**Cross-Unit Boundaries**

*   **Calls Out**: The MAP indicates no outgoing calls to other units from this specific translation unit/partial. The constructor and data members are self-contained. The actual deserialization logic in `ReadFromWorldPacket` would internally call methods on `WorldPacket` and `ObjectGuid`, but these are standard library/utility interactions rather than cross-unit architectural dependencies listed in the MAP.
*   **Called By**: The MAP shows no external callers. In practice, this class is instantiated and populated by the network layer when a `MSG_RAID_TARGET_UPDATE` packet is received from a client. The resulting object is then passed to the game world handler (likely in a `WorldSession` or `Player` handler) to execute the actual raid target assignment logic.

**Data Model**

This unit does not interact directly with any database tables. It operates purely on runtime network data and in-memory object states.

**Notable Implementation Details**

1.  **Conditional Compilation**: The entire class definition is wrapped in `#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_10_2`. This means the class does not exist for older client versions. Code handling raid targets must account for this version difference, potentially using different packet structures or logic for clients prior to build 1.10.2.
2.  **Icon ID Semantics**: The comment `// only valid when iconId != 0xFF (icon update, not request)` is critical. It defines the protocol semantics:
    *   `iconId != 0xFF`: A command to set a specific icon on a specific `guid`.
    *   `iconId == 0xFF`: Likely a query or a broadcast request where the `guid` field is ignored or unused. Maintainers must ensure that downstream logic checks `iconId` before attempting to use `guid` to avoid operating on an invalid or uninitialized GUID.
3.  **Default Initialization**: `iconId` defaults to `0`. In many WoW protocols, `0` might represent "no icon" or "clear icon," but this depends on the specific client version's enumeration. The explicit initialization prevents garbage data from being interpreted as a valid icon assignment.

## Member Reference

**RaidTargetUpdate**
Constructor that initializes the packet type to `MSG_RAID_TARGET_UPDATE` and sets default values for `iconId` (0) and `guid`. It is only available for client builds newer than 1.10.2.

---

<!-- machine-true, projected from graph.json -->

## Map — RaidTargetUpdate

*Source:* Group.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| RaidTargetUpdate | ctor | — | — | — |
