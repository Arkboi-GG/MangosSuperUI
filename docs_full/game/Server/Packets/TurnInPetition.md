# TurnInPetition

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# TurnInPetition

## Purpose & Responsibilities

`TurnInPetition` is a packet structure within the `WorldPackets::Petition` namespace, defined in `Petition.h`. It represents the client-to-server message `CMSG_TURN_IN_PETITION`. Its sole responsibility is to encapsulate the data required for a player to submit a signed petition to an NPC, thereby initiating the creation of a guild or similar faction entity. The structure holds the `ObjectGuid` of the petition item being turned in. As a `ClientPacket`, it is designed to be deserialized from the network stream via its `ReadFromWorldPacket` method.

## Member-by-Member Behavior

The unit contains a single member: the constructor.

*   **`TurnInPetition`**: This is the default constructor for the `TurnInPetition` class. It initializes the base class `ClientPacket` with the opcode `CMSG_TURN_IN_PETITION`. This registration ensures that when the server receives a packet with this specific opcode, it can correctly instantiate this structure to process the request. The constructor does not perform any additional initialization of the `itemGuid` member, leaving it to be populated during the deserialization phase (`ReadFromWorldPacket`).

## Cross-Unit Boundaries

*   **Calls Out**: None. The constructor only invokes the base class constructor.
*   **Called By**: None listed in the map. In practice, this packet is instantiated by the network layer when a `CMSG_TURN_IN_PETITION` message is received from a client. The processing logic that consumes this packet resides in other units (likely a handler such as `WorldSession` or a dedicated petition handler), but those interactions are outside the scope of this unit's definition.

## Data Model

This unit does not directly interact with database tables. It operates purely on in-memory packet data. The `itemGuid` field corresponds to a game object or item instance, but the mapping of this GUID to persistent storage (e.g., `petitions` or `guilds` tables) occurs in downstream processing units, not within this packet structure.

## Notable Implementation Details

*   **Final Class**: The class is marked `final`, indicating it cannot be inherited. This is consistent with its role as a leaf node in the packet hierarchy.
*   **Base Class Initialization**: The constructor explicitly passes `CMSG_TURN_IN_PETITION` to the `ClientPacket` base. This opcode is critical for the server's packet dispatching mechanism to route the incoming data to the correct handler.
*   **Member Initialization**: The `itemGuid` member is declared but not initialized in the constructor. It relies on the `ReadFromWorldPacket` method (inherited from `ClientPacket` and implemented elsewhere, likely in a corresponding `.cpp` file not shown here but implied by the interface) to populate this value from the raw network bytes.

## Member Reference

**TurnInPetition**  
Constructor for the `TurnInPetition` packet. Initializes the base `ClientPacket` with the opcode `CMSG_TURN_IN_PETITION`. No other members are initialized here; `itemGuid` is populated during deserialization.

---

<!-- machine-true, projected from graph.json -->

## Map — TurnInPetition

*Source:* Petition.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| TurnInPetition | ctor | — | — | — |
