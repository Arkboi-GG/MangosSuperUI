# LeaveBattlefield

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# LeaveBattlefield

## Purpose & Responsibilities

`LeaveBattlefield` is a client-to-server network packet definition within the `WorldPackets::Battleground` namespace. Its sole responsibility is to represent the `CMSG_LEAVE_BATTLEFIELD` message sent by a World of Warcraft client when a player attempts to leave an active battleground or arena instance.

This class serves as a data structure that encapsulates the raw bytes received from the client, providing a typed interface for the server-side handler to extract relevant information—specifically, the map ID of the battleground being left (in supported client builds newer than 1.8.4). It inherits from `ClientPacket`, integrating into the server's packet handling framework, but contains no business logic itself; all parsing and subsequent game-state changes are handled by external units that instantiate this packet and invoke its `ReadFromWorldPacket` method.

## Member-by-Member Behavior

### Constructor: `LeaveBattlefield()`

The explicit constructor initializes the packet object. It calls the base class constructor `ClientPacket` with the constant `CMSG_LEAVE_BATTLEFIELD`, identifying the packet type for the server's dispatch system. It also initializes the `mapId` member to `0` if the client build supports it (`SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_8_4`). This ensures that the packet is ready to receive data immediately upon instantiation.

## Cross-Unit Boundaries

*   **Calls Out:** The `LeaveBattlefield` constructor calls `ClientPacket::ClientPacket(uint32 opcode)` from the `ClientPacket` unit (likely defined in `Packet.h` or similar). This establishes the packet's opcode identity.
*   **Called By:** External server-side handlers (not shown in this unit's map, but implied by the `ClientPacket` inheritance) will instantiate `LeaveBattlefield` and call `ReadFromWorldPacket`. These handlers typically reside in the world server logic (e.g., `WorldSession` or specific battleground handlers) and use the extracted `mapId` to process the player's departure request.

## Data Model

This unit does not interact directly with any database tables. It operates purely on in-memory network packet data.

## Notable Implementation Details

*   **Version Conditional Compilation:** The `mapId` member is guarded by `#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_8_4`. This indicates that older client versions (1.8.4 and below) did not send the map ID in the `CMSG_LEAVE_BATTLEFIELD` packet, or the server did not expect it. Maintainers must ensure that any logic consuming this packet checks the client build or handles missing `mapId` data appropriately for older clients.
*   **Default Initialization:** The `mapId` is explicitly initialized to `0` in the class declaration. This provides a safe default value if the packet reading logic fails or if the field is not populated for certain client versions.
*   **Final Class:** The class is marked `final`, preventing further inheritance. This is consistent with its role as a leaf node in the packet hierarchy, representing a specific, immutable message format.

## Member Reference

**LeaveBattlefield**
Constructor for the `LeaveBattlefield` packet. Initializes the base `ClientPacket` with the opcode `CMSG_LEAVE_BATTLEFIELD` and sets `mapId` to `0` for client builds newer than 1.8.4.

---

<!-- machine-true, projected from graph.json -->

## Map — LeaveBattlefield

*Source:* Battleground.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| LeaveBattlefield | ctor | — | — | — |
