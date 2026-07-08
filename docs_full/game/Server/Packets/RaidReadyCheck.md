# RaidReadyCheck

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

The `RaidReadyCheck` class is a packet structure within the `WorldPackets::Group` namespace, responsible for handling the `MSG_RAID_READY_CHECK` network message between the client and the server. It serves a dual purpose depending on the direction of communication and the presence of payload data:

1.  **Request:** When sent from the server to the client (or vice versa, depending on protocol specifics not fully detailed in this header alone, but typically initiated by the leader), it acts as a signal to initiate a ready check. In this mode, the `state` field is empty (`hasValue() == false`).
2.  **Response:** When sent as a reply to a ready check request, it carries the player's readiness status. In this mode, the `state` field contains a `uint8` value indicating whether the player is ready, not ready, or has timed out.

This class is part of the Mangos server's packet parsing and generation framework, specifically designed for clients newer than build 1.10.2, as indicated by the `#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_10_2` preprocessor directive.

## Member-by-Member Behavior

### Construction and Initialization

**`RaidReadyCheck`**
The constructor initializes the packet object. It inherits from `ClientPacket` and registers itself with the opcode `MSG_RAID_READY_CHECK`. This registration ensures that when the server receives a packet with this opcode, it can instantiate the correct handler class. The constructor does not take any arguments and performs no complex initialization beyond calling the base class constructor.

### Data Members

**`state`**
A `nonstd::optional<uint8>` member variable that holds the readiness state.
-   If `state.has_value()` is `false`, the packet represents a **request** to start a ready check.
-   If `state.has_value()` is `true`, the packet represents a **response** to a ready check, and `state.value()` contains the specific status code (e.g., ready, not ready, timeout).

## Cross-Unit Boundaries

According to the provided MAP, the `RaidReadyCheck` class has **no outgoing calls** to other units and is **not called by** any other units listed in the map. This indicates that its primary role is data representation and serialization/deserialization via the inherited `ReadFromWorldPacket` method (which is declared in the base class `ClientPacket` and implemented elsewhere, likely in a corresponding `.cpp` file not included in this source snippet). The interaction with other parts of the system occurs through the packet processing pipeline, where instances of this class are created, populated, and then passed to higher-level game logic handlers.

## Data Model

This unit does not interact directly with any database tables. It operates entirely in memory as part of the network packet handling layer.

## Notable Implementation Details

1.  **Client Build Dependency:** The entire `RaidReadyCheck` class definition is guarded by `#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_10_2`. This means the class only exists and is compiled for server builds targeting clients newer than version 1.10.2. Older clients do not support this specific packet format or feature set.
2.  **Dual-Purpose Packet:** The use of `nonstd::optional<uint8>` for the `state` member is a key design choice. It allows a single packet type to serve two distinct semantic purposes (request vs. response) based solely on whether the optional value is present. This reduces the number of opcodes needed and simplifies the packet structure.
3.  **Inheritance:** As a `ClientPacket`, it is expected to be used primarily for packets received *from* the client. However, the comment suggests it can be used for both requests and responses. Typically, in such frameworks, a separate `ServerPacket` might be used for outgoing messages, or the same structure is reused. The presence of `ReadFromWorldPacket` confirms it is designed to parse incoming data. If the server needs to send a ready check request, it might use a different mechanism or reuse this structure if the framework supports bidirectional packet types. Given the inheritance from `ClientPacket`, it is most likely used to process the player's response to a ready check initiated by the leader.

## Member Reference

**`RaidReadyCheck`**: Constructor for the `RaidReadyCheck` packet class. Initializes the object as a `ClientPacket` with the opcode `MSG_RAID_READY_CHECK`. It is only defined for client builds newer than 1.10.2.

---

<!-- machine-true, projected from graph.json -->

## Map — RaidReadyCheck

*Source:* Group.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| RaidReadyCheck | ctor | — | — | — |
