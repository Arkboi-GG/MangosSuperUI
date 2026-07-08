# DuelCancelled

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# DuelCancelled

**Purpose & Responsibilities**

`DuelCancelled` is a client-to-server packet handler within the `WorldPackets::Duel` namespace. Its sole responsibility is to deserialize the `CMSG_DUEL_CANCELLED` message received from a client. This message indicates that a player has explicitly cancelled a duel request or an ongoing duel. The class extracts the `ObjectGuid` of the player involved in the duel cancellation from the raw network data, making it available for subsequent game logic processing.

As a `ClientPacket`, it serves as the entry point for this specific command from the network layer into the application logic. It does not contain business logic for handling the cancellation itself; it strictly handles the parsing of the binary protocol.

## Member-by-Member Behavior

The unit consists of a single constructor and inherits standard packet reading behavior.

### Initialization and Construction

The **DuelCancelled** constructor initializes the base `ClientPacket` with the opcode `CMSG_DUEL_CANCELLED`. This registration ensures that when the network layer receives a packet with this specific opcode, an instance of `DuelCancelled` is created to handle the deserialization. The constructor is marked `explicit` to prevent implicit conversions.

### Data Deserialization

While the `ReadFromWorldPacket` method is declared in the header, its implementation is not provided in the source snippet. However, based on the class structure and the presence of the `playerGuid` member, the inherited or implemented `ReadFromWorldPacket` logic is responsible for extracting a 64-bit GUID from the incoming `WorldPacket` buffer and storing it in the `playerGuid` public member variable. This GUID typically represents the target player whose duel is being cancelled or the player initiating the cancellation, depending on the specific protocol definition for `CMSG_DUEL_CANCELLED`.

## Cross-Unit Boundaries

*   **Calls Out:** None. This unit does not invoke methods in other classes during its construction or declaration.
*   **Called By:** None listed in the map. In practice, this class is instantiated by the packet dispatching system (likely within the `WorldSession` or a central packet router) when a `CMSG_DUEL_CANCELLED` packet is detected on the wire. The dispatcher will then pass this object to the appropriate handler (e.g., a `HandleDuelCancelled` method in `Player` or `WorldSession`) which is not part of this unit.

## Data Model

This unit does not interact directly with any database tables. It operates entirely on runtime memory structures (`ObjectGuid`) derived from network packets.

## Notable Implementation Details

*   **Namespace Organization:** The class is nested within `WorldPackets::Duel`, grouping all duel-related network messages together for clarity and maintainability.
*   **Public Member Access:** The `playerGuid` member is public, allowing the calling handler direct access to the parsed GUID without needing getter methods. This is a common pattern in packet classes to minimize boilerplate.
*   **Final Class:** The class is marked `final`, indicating it cannot be subclassed. This enforces a strict interface for this specific packet type.

## Member Reference

**DuelCancelled**
Constructor that initializes the `ClientPacket` base class with the opcode `CMSG_DUEL_CANCELLED`. It prepares the object to receive and parse the duel cancellation message from the client.

---

<!-- machine-true, projected from graph.json -->

## Map — DuelCancelled

*Source:* Duel.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| DuelCancelled | ctor | — | — | — |
