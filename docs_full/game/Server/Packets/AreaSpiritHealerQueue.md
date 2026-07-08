# AreaSpiritHealerQueue

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AreaSpiritHealerQueue

**AreaSpiritHealerQueue** is a `ClientPacket` subclass within the `WorldPackets::Battleground` namespace, representing the `CMSG_AREA_SPIRIT_HEALER_QUEUE` message sent from the game client to the server. Its sole responsibility is to encapsulate the network data for a player's request to queue for resurrection via an Area Spirit Healer (ASH). The class holds the `ObjectGuid` of the target spirit healer NPC and provides the mechanism to deserialize this identifier from the incoming world packet buffer. It contains no business logic, validation, or side effects; it is purely a data container for the network layer.

## Member-by-Member Behavior

The unit consists of a single constructor and inherits standard packet behavior.

*   **Construction**: The explicit constructor initializes the base `ClientPacket` with the opcode `CMSG_AREA_SPIRIT_HEALER_QUEUE`. This registers the packet type with the networking subsystem, ensuring that incoming packets with this opcode are routed to handlers expecting this specific structure.
*   **Data Storage**: The public member `guid` stores the unique identifier of the Area Spirit Healer NPC. This value is populated during deserialization and is intended to be read by higher-level game logic (outside this unit) to determine which NPC the player is interacting with.

## Cross-Unit Boundaries

*   **Calls Out**: None. The `AreaSpiritHealerQueue` class does not invoke methods in other units.
*   **Called By**: None listed in the map. However, by design, instances of this class are created by the network handler when a `CMSG_AREA_SPIRIT_HEALER_QUEUE` packet is received. The handler will then call `ReadFromWorldPacket` (inherited from `ClientPacket`) to populate the `guid` field. Subsequently, game logic (likely in a `Player` or `Battleground` handler unit, though not detailed in this map) will read the `guid` member to process the queue request.

## Data Model

This unit does not interact with any database tables. It operates entirely on transient network data.

## Notable Implementation Details

*   **Minimalist Design**: Like other classes in this header (`BattlefieldListRequest`, `BattlemasterHello`, etc.), `AreaSpiritHealerQueue` is a thin wrapper around network data. It relies on the `ClientPacket` base class for serialization/deserialization infrastructure.
*   **Opcode Specificity**: The class is tightly coupled to the `CMSG_AREA_SPIRIT_HEALER_QUEUE` opcode. Any change in the client protocol for this message would require updating this class's `ReadFromWorldPacket` implementation (if overridden) or the base class handling. In this specific case, the `ReadFromWorldPacket` method is declared but not defined in this header, implying it is either implemented in the base class or in a corresponding `.cpp` file not provided here. Given the simplicity (just a GUID), it likely uses standard GUID reading utilities.
*   **Namespace Context**: It resides in `WorldPackets::Battleground`, indicating it is part of the structured packet system introduced to replace raw `WorldPacket` manipulation, providing type safety and clearer intent for battleground-related communications.

## Member Reference

**AreaSpiritHealerQueue**  
Constructor. Initializes the base `ClientPacket` with the opcode `CMSG_AREA_SPIRIT_HEALER_QUEUE`. No other initialization occurs.

---

<!-- machine-true, projected from graph.json -->

## Map — AreaSpiritHealerQueue

*Source:* Battleground.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AreaSpiritHealerQueue | ctor | — | — | — |
