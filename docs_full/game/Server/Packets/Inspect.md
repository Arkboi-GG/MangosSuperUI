# Inspect

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Unit Documentation: `WorldPackets::Misc::Inspect`

## Purpose & Responsibilities

The `WorldPackets::Misc::Inspect` class is a data structure representing a specific client-to-server network message: `CMSG_INSPECT`. Its sole responsibility is to define the binary layout and parsing logic for a request where a player character attempts to inspect another entity (typically another player) in the game world.

As part of the `WorldPackets::Misc` namespace within the Mangos server architecture, this class serves as a contract between the network layer and the game logic layer. It ensures that the raw bytes received from the client are correctly interpreted into a structured object containing the unique identifier (`ObjectGuid`) of the target entity. The class itself contains no business logic; it strictly handles serialization/deserialization concerns.

## Member-by-Member Behavior

This unit defines a single class, `Inspect`, with one primary component relevant to its construction and initialization.

### Construction
The **`Inspect`** constructor initializes the packet object. It inherits from `ClientPacket`, passing the opcode `CMSG_INSPECT` to identify the message type in the network protocol. This initialization prepares the object to receive data from an incoming network stream. Note that while the class declares a `ReadFromWorldPacket` method, this method is not listed in the unit's MAP and thus its implementation details are outside the scope of this specific unit's behavioral documentation, though it is implicitly required by the `ClientPacket` interface.

## Cross-Unit Boundaries

The `Inspect` class operates at the boundary between the network transport layer and the application logic layer.

*   **Calls Out:**
    *   **`ClientPacket`**: The constructor calls the base class constructor to register the packet opcode.
    *   **`ObjectGuid`**: The class holds an `ObjectGuid` member, relying on the `ObjectGuid` unit for the definition of the identifier type.

*   **Called By:**
    *   **Network Handler / Opcode Dispatcher**: Although not explicitly listed in the "Called by" column of the map, the `Inspect` class is instantiated by the server's main packet processing loop when a `CMSG_INSPECT` opcode is detected.
    *   **Game Logic Handlers**: Once populated, an instance of `Inspect` is passed to the appropriate handler function (likely in a unit such as `PlayerHandler.cpp` or `MiscHandler.cpp`) which uses the `guid` member to locate the target player and execute the inspection logic.

## Data Model

This unit does not interact directly with any database tables. It processes transient network data. The `guid` extracted is used to look up entities in memory (via the server's object manager), but no SQL queries are executed within this class.

## Notable Implementation Details

*   **Minimalist Design**: The class follows a strict separation of concerns. It does not validate whether the `guid` is valid, whether the target exists, or whether the inspecting player has permission to inspect the target. These checks are performed by the calling logic after the packet has been successfully parsed.
*   **Public Member Access**: The `guid` member is declared `public`. This allows the calling handler to access the target identifier directly without needing getter methods, reducing boilerplate but exposing the internal state.
*   **Opcode Dependency**: The class is tightly coupled to the specific opcode `CMSG_INSPECT`. Any change in the client protocol version that alters this opcode or the packet structure would require updating this class.

## Member Reference

**Inspect**
Constructor for the `Inspect` packet class. Initializes the base `ClientPacket` with the opcode `CMSG_INSPECT`. Prepares the object to hold the target's `ObjectGuid`.

---

<!-- machine-true, projected from graph.json -->

## Map — Inspect

*Source:* Misc.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Inspect | ctor | — | — | — |
