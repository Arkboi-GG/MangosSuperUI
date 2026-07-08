# Emote

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Emote (WorldPackets::Misc::Emote)

## Purpose & Responsibilities

`WorldPackets::Misc::Emote` is a lightweight data structure representing a client-to-server network message (`CMSG_EMOTE`). Its sole responsibility is to encapsulate the raw integer value of an emote command sent by the game client. It acts as the transport layer object that carries the emote ID from the network packet buffer into the server's game logic handlers.

This unit contains no business logic, validation, or state management beyond basic memory initialization. It is part of the `WorldPackets::Misc` namespace, indicating it handles miscellaneous client commands that do not fit into more specific categories like combat, chat, or movement.

## Member-by-Member Behavior

### **Emote** (Constructor)
The constructor initializes the `Emote` object. It performs two actions:
1.  Calls the base class constructor `ClientPacket(CMSG_EMOTE)`, registering this object with the opcode `CMSG_EMOTE`. This allows the server's packet dispatcher to identify incoming packets of this type.
2.  Initializes the member variable `emote` to `0`. This provides a default safe value before the packet data is parsed.

## Cross-Unit Boundaries

*   **Calls Out:** None. The constructor does not invoke any other units.
*   **Called By:** None listed in the MAP. In practice, this constructor is invoked by the server's packet parsing infrastructure (likely within a factory pattern or switch statement in a packet handler unit) when a `CMSG_EMOTE` opcode is detected on the wire.

## Data Model

This unit does not interact with any database tables. It operates entirely in memory during the network packet parsing phase.

## Notable Implementation Details

*   **Final Class:** The class is marked `final`, preventing inheritance. This is consistent with its role as a simple data carrier; there is no need for polymorphic behavior.
*   **Public Member Variable:** The `emote` field is `public`. This design choice prioritizes simplicity and direct access for the parsing logic and subsequent handlers, avoiding the overhead of getter/setter methods for a single integer.
*   **Default Initialization:** The `emote` variable is explicitly initialized to `0` in the class definition. While the constructor also relies on the base class setup, this ensures the member has a defined state even if `ReadFromWorldPacket` were to fail or be skipped (though in normal operation, `ReadFromWorldPacket` will overwrite this value).
*   **Parsing Logic Absent:** The declaration of `ReadFromWorldPacket` is present but its implementation is not in this unit. The actual deserialization of the `uint32` emote ID from the `WorldPacket` buffer occurs in the corresponding `.cpp` file (not provided here, but implied by the interface).

## Member Reference

**Emote**
Constructor for the `Emote` packet class. Initializes the base `ClientPacket` with the `CMSG_EMOTE` opcode and sets the `emote` member to `0`.

---

<!-- machine-true, projected from graph.json -->

## Map — Emote

*Source:* Misc.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Emote | ctor | — | — | — |
