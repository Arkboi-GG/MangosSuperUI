# TogglePvP

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# TogglePvP

**Purpose & Responsibilities**

`TogglePvP` is a client-side packet structure within the `WorldPackets::Misc` namespace, defined in `Misc.h`. Its sole responsibility is to represent the `CMSG_TOGGLE_PVP` message sent by the game client to the server when a player attempts to change their Player vs. Player (PvP) status. This typically occurs when a player clicks the PvP toggle button in the interface or enters/exits a PvP zone. The class acts as a data container, holding the desired target state (`true` for enabling PvP, `false` for disabling it) parsed from the raw network packet. It does not contain logic for processing the request, validating permissions, or updating game state; those responsibilities lie in the server-side handler that consumes this packet.

**Member-by-Member Behavior**

The unit consists of a single member: the constructor.

*   **Constructor (`TogglePvP`)**: Initializes the `ClientPacket` base class with the opcode `CMSG_TOGGLE_PVP`. This registration allows the server's packet dispatcher to identify incoming bytes as a PvP toggle request and route them to the appropriate handler. The constructor does not initialize the `targetState` member, leaving it empty until `ReadFromWorldPacket` is called by the server infrastructure.

**Cross-Unit Boundaries**

*   **Calls Out**: None. The constructor performs no external calls.
*   **Called By**: None listed in the map. In practice, this constructor is invoked by the server's packet deserialization framework (likely within `Packet.cpp` or a similar dispatcher) when a `CMSG_TOGGLE_PVP` opcode is detected on the wire. The framework creates an instance of `TogglePvP`, then calls its `ReadFromWorldPacket` method (defined in the corresponding `.cpp` file, not shown here but implied by the `override` keyword) to populate the `targetState` field.

**Data Model**

This unit interacts with no database tables. It is purely a transient network data structure.

**Notable Implementation Details**

*   **Optional State**: The `targetState` member is defined as `nonstd::optional<bool>`. This suggests that the packet format might allow for ambiguity or that the server needs to distinguish between "no data received" and an explicit boolean value during parsing. However, since `ReadFromWorldPacket` is not implemented in this header, the exact parsing logic is hidden.
*   **Final Class**: The class is marked `final`, preventing inheritance. This is consistent with its role as a leaf-node data structure in the packet hierarchy.
*   **Namespace**: It resides in `WorldPackets::Misc`, indicating it is part of the general miscellaneous client-to-server messages, distinct from combat, movement, or social-specific namespaces.

## Member Reference

**TogglePvP**
Constructor for the `TogglePvP` packet. Initializes the base `ClientPacket` with the `CMSG_TOGGLE_PVP` opcode. Does not initialize the `targetState` member.

---

<!-- machine-true, projected from graph.json -->

## Map — TogglePvP

*Source:* Misc.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| TogglePvP | ctor | — | — | — |
