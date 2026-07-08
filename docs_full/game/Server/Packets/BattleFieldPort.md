# BattleFieldPort

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# BattleFieldPort

## Purpose & Responsibilities

`BattleFieldPort` is a client-side packet structure within the `WorldPackets::Battleground` namespace, defined in `Battleground.h`. Its sole responsibility is to represent the `CMSG_BATTLEFIELD_PORT` message sent by the game client to the server. This packet conveys a player's intent to teleport to a battlefield instance, carrying the specific map identifier and an action flag required for the server to process the request.

As a `ClientPacket`, it inherits the standard serialization and deserialization mechanisms for incoming network data. It does not contain business logic for handling the port request itself; rather, it serves as the data container that allows the server to extract the necessary parameters (`mapId` and `action`) once the packet is received.

## Member-by-Member Behavior

The unit consists of a single member: the constructor.

*   **Construction**: The default constructor initializes the packet with the opcode `CMSG_BATTLEFIELD_PORT`. It also initializes member variables based on the supported client build version.

## Cross-Unit Boundaries

*   **Called by `CombatBotBaseAI/SendBattlefieldPortPacket`**: The `BattleFieldPort` constructor is invoked by `SendBattlefieldPortPacket` in the `CombatBotBaseAI` unit. This indicates that the bot AI system uses this packet structure to simulate a player requesting a battlefield port, likely to test or automate battlefield entry scenarios. The AI constructs the packet to send the appropriate `mapId` and `action` to the server.

## Data Model

This unit does not interact directly with any database tables. It operates purely on network packet data.

## Notable Implementation Details

*   **Conditional Compilation**: The presence of the `mapId` member is guarded by `#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_8_4`. This reflects changes in the World of Warcraft protocol where older clients (1.8.4 and below) did not include the map ID in this specific packet, while newer clients do. The `action` member is always present.
*   **Default Initialization**: Both `mapId` (when compiled in) and `action` are explicitly initialized to `0` in the class definition. This ensures deterministic state before the packet is populated via `ReadFromWorldPacket`.
*   **Inheritance**: It inherits from `ClientPacket`, which provides the base functionality for reading binary data from the network stream into these member variables. The actual parsing logic resides in the overridden `ReadFromWorldPacket` method (defined elsewhere, likely in a corresponding `.cpp` file not included in this partial, but implied by the interface).

## Member Reference

**BattleFieldPort**
Constructor for the `BattleFieldPort` packet. Initializes the packet opcode to `CMSG_BATTLEFIELD_PORT`. Conditionally initializes `mapId` to `0` if the client build is greater than 1.8.4. Always initializes `action` to `0`. Called by `CombatBotBaseAI/SendBattlefieldPortPacket` to construct packets for bot simulation.

---

<!-- machine-true, projected from graph.json -->

## Map — BattleFieldPort

*Source:* Battleground.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| BattleFieldPort | ctor | — | CombatBotBaseAI/SendBattlefieldPortPacket | — |
