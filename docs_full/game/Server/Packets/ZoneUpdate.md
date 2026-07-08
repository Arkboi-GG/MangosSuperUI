# ZoneUpdate

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ZoneUpdate

**Purpose & Responsibilities**

`ZoneUpdate` is a client-side packet structure within the `WorldPackets::Misc` namespace, defined in `Misc.h`. It represents the `CMSG_ZONEUPDATE` message sent by the game client to the server. Its sole responsibility is to carry the identifier of the new zone or area the player character has entered, allowing the server to process zone-change logic (such as updating faction reputation, triggering zone-specific scripts, or adjusting environmental effects).

As a `ClientPacket`, `ZoneUpdate` acts as a data container. It does not contain business logic itself; rather, it provides the interface for deserializing the raw binary data received from the network into a structured format (`newZone`) that higher-level server handlers can consume.

## Member-by-Member Behavior

### **ZoneUpdate** (Constructor)

The constructor initializes the `ZoneUpdate` instance. It performs two critical setup steps:
1.  **Base Class Initialization**: It calls the `ClientPacket` constructor with the opcode `CMSG_ZONEUPDATE`. This registers the packet type with the server's packet dispatching system, ensuring that incoming messages with this specific opcode are routed to handlers expecting a `ZoneUpdate` object.
2.  **Member Initialization**: It initializes the public member `newZone` to `0`. This default value ensures that if the packet reading fails or is incomplete, the zone ID remains in a known, invalid state rather than containing garbage memory.

## Cross-Unit Boundaries

*   **Calls Out**: None. The constructor is self-contained and does not invoke methods in other units.
*   **Called By**: None listed in the map. In practice, instances of `ZoneUpdate` are typically created by the server's packet reading infrastructure (likely within `Packet.cpp` or similar networking layers) when a `CMSG_ZONEUPDATE` opcode is detected on the wire. The `ReadFromWorldPacket` method (declared in the base class but implemented elsewhere or in the corresponding `.cpp` file) is responsible for populating the `newZone` field from the raw `WorldPacket` buffer.

## Data Model

This unit does not interact directly with any database tables. It operates entirely in memory as part of the network I/O layer. The `newZone` integer corresponds to zone IDs defined in the game's static data (likely `area_table` or similar in the database), but `ZoneUpdate` itself performs no SQL queries or table lookups.

## Notable Implementation Details

*   **Inheritance**: `ZoneUpdate` inherits from `ClientPacket`. This implies it shares common functionality for packet validation, opcode management, and potentially logging with other client-to-server packets.
*   **Public Data Member**: The `newZone` field is declared `public`. This design choice simplifies access for the handler that processes the packet, avoiding the need for getter/setter methods. However, it also means the integrity of the data relies on the correct implementation of `ReadFromWorldPacket` (which is not part of this unit's definition but is a virtual override required by the base class).
*   **Default Value Safety**: Initializing `newZone` to `0` is a defensive measure. Zone ID `0` is typically invalid or reserved, making it easy for downstream logic to detect if the packet was malformed or if the reading step failed to populate the field.

## Member Reference

**ZoneUpdate**
The constructor for the `ZoneUpdate` packet class. It initializes the base `ClientPacket` with the `CMSG_ZONEUPDATE` opcode and sets the `newZone` member to `0`. This prepares the object to receive and hold the zone change data from the client.

---

<!-- machine-true, projected from graph.json -->

## Map — ZoneUpdate

*Source:* Misc.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ZoneUpdate | ctor | — | — | — |
