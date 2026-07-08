# ClientPacket

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ClientPacket Architecture and Reference Documentation

## Purpose & Responsibilities

`ClientPacket` is an abstract base class within the `wowvmangos` network layer, designed to represent incoming data packets from clients. It serves as the foundational interface for deserializing raw binary network data (`WorldPacket`) into structured C++ objects.

Its primary responsibilities are:
1.  **Encapsulation of Opcode**: Storing the network opcode associated with the packet.
2.  **Deserialization Interface**: Defining the contract (`ReadFromWorldPacket`) that all specific client packet implementations must fulfill to parse their specific data fields.
3.  **Inheritance Hierarchy**: Acting as the bridge between the generic `Packet` base class and concrete packet implementations (such as `NullClientPacket` or game-specific command packets).

This unit contains no database interactions. It operates purely in memory during the network receive cycle.

## Member-by-Member Behavior

### Construction and Initialization

**`ClientPacket`**
The constructor initializes the `opcode` member variable inherited from the `Packet` base class. It takes a single `uint16` argument representing the network opcode for this packet type. This ensures that every instance of a client packet knows its identity before any data is parsed.

### Deserialization Logic

**`ReadFromWorldPacket`**
This is a pure virtual function, making `ClientPacket` an abstract class. It defines the mandatory interface for parsing. Implementations in derived classes are responsible for extracting bytes from the provided `WorldPacket` reference (`recv_data`) and populating the member variables of the derived class. The `WorldPacket` unit (not detailed here but referenced in the map) provides the low-level byte extraction utilities.

## Cross-Unit Boundaries

### Incoming Dependencies (Calls Out)

*   **`WorldPacket`**: Although `ReadFromWorldPacket` is declared in this unit, its implementation in derived classes (and the `NullClientPacket` override shown in the source) interacts directly with `WorldPacket`. Specifically, `NullClientPacket::ReadFromWorldPacket` calls `recv_data.GetOpcode()` to retrieve the opcode from the raw packet buffer. This establishes a dependency where `ClientPacket` derivatives rely on `WorldPacket` for low-level data access.

### Outgoing Dependencies (Called By)

*   **Network Handler Units**: While not explicitly listed in the "Called by" column of the map, the design implies that network reception handlers (likely in a `Socket` or `Session` unit) will instantiate these packets and invoke `ReadFromWorldPacket`. The map indicates no specific callers, suggesting this is a library-style definition used by higher-level network management code.

## Data Model

This unit does not interact with any database tables. It handles transient network data structures.

## Notable Implementation Details

1.  **Abstract Nature**: `ClientPacket` cannot be instantiated directly. It forces all client-side packet handlers to implement a consistent parsing interface.
2.  **Opcode Management**: The `opcode` is stored in the protected section of the base `Packet` class. `ClientPacket` exposes it via the public `GetOpcode()` method inherited from `Packet`.
3.  **NullClientPacket Pattern**: The source includes `NullClientPacket`, a concrete implementation used for packets that carry no additional data beyond the opcode itself. Its `ReadFromWorldPacket` implementation is notable because it updates the internal `opcode` field using `recv_data.GetOpcode()`. This suggests that for some packets, the opcode might be determined dynamically from the stream rather than being known at construction time, or it serves as a fallback mechanism.
4.  **Constant Definition**: The header defines `OPCODE_WILL_BE_SET_IN_READ_FUNCTION` as `0xFFFF`. This constant is used by `NullClientPacket` to indicate that the opcode is not known until the packet is read.

## Member Reference

**ClientPacket**
Constructor for the abstract base class. Initializes the `opcode` member from the `Packet` base class with the provided `uint16` value.

**ReadFromWorldPacket**
Pure virtual function declaring the interface for deserializing data from a `WorldPacket` into the packet object. Derived classes must implement this to extract specific fields.

---

<!-- machine-true, projected from graph.json -->

## Map — ClientPacket

*Source:* Packet.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ClientPacket | ctor | — | — | — |
| ReadFromWorldPacket | decl | — | — | — |
