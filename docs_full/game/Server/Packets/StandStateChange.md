# StandStateChange

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# StandStateChange

**Purpose & Responsibilities**

`StandStateChange` is a client-side packet handler within the `WorldPackets::Misc` namespace, responsible for deserializing the `CMSG_STANDSTATECHANGE` message sent by the game client. Its sole responsibility is to extract the requested animation state identifier from the incoming network buffer and store it in the `animState` member variable. This packet represents a user action to change their character's posture (e.g., standing up, sitting down, kneeling) or specific animation triggers.

As a `ClientPacket`, it serves as a data structure that bridges the raw binary network stream and the server's game logic. It contains no business logic itself; it strictly defines the contract for how the server interprets this specific client command.

## Member-by-Member Behavior

### **StandStateChange** (Constructor)
The constructor initializes the packet object. It performs two critical setup tasks:
1.  **Base Initialization**: It calls the base class constructor `ClientPacket(CMSG_STANDSTATECHANGE)`, registering this object with the server's packet dispatch system under the opcode `CMSG_STANDSTATECHANGE`. This ensures that when the server receives a packet with this opcode, it instantiates a `StandStateChange` object to handle it.
2.  **Member Initialization**: It initializes the `animState` member to `0`. This default value acts as a fallback if the reading process fails or if the packet format is malformed, though the `ReadFromWorldPacket` method typically overwrites this.

### **ReadFromWorldPacket** (Implicitly Declared, Defined Elsewhere)
While the declaration is present in `Misc.h`, the implementation resides in the corresponding `.cpp` file (not provided in the source snippet, but implied by the `override` keyword and standard pattern). Based on the class structure:
*   It accepts a `WorldPacket&` reference containing the raw binary data.
*   It reads a `uint32` value from the packet stream.
*   It assigns this value to the `animState` member.
*   This method is called by the server's packet processing loop after the packet has been identified by its opcode.

## Cross-Unit Boundaries

*   **Calls Out**: None. The `StandStateChange` class does not call any other units. It is a passive data holder.
*   **Called By**: The server's main packet handling infrastructure (likely within `WorldSession` or a similar packet dispatcher unit, though not explicitly listed in the MAP). The dispatcher creates an instance of `StandStateChange` and invokes `ReadFromWorldPacket` when a `CMSG_STANDSTATECHANGE` opcode is detected on the wire. After reading, the dispatcher likely passes the populated `animState` value to a game object handler (e.g., `Player::HandleStandStateChange`) to apply the visual change to the character.

## Data Model

This unit does not interact with any database tables. It operates entirely on transient network data.

## Notable Implementation Details

*   **Default State**: The `animState` is initialized to `0` in the constructor. In many game engines, `0` often corresponds to the "standing" state. This default ensures that if the packet reading fails silently or the data is missing, the system defaults to a neutral standing posture rather than an undefined animation.
*   **Type Safety**: The use of `uint32` for `animState` aligns with typical World of Warcraft protocol specifications for animation states, which are integer identifiers defined in the client's data files.
*   **Final Class**: The class is marked `final`, preventing inheritance. This is appropriate for a leaf-node packet handler that has no need for polymorphic extension.

## Member Reference

**StandStateChange**
Constructor for the `StandStateChange` packet. Initializes the base `ClientPacket` with the opcode `CMSG_STANDSTATECHANGE` and sets the `animState` member to `0`. This registration allows the server's packet dispatcher to route incoming `CMSG_STANDSTATECHANGE` messages to this specific handler.

---

<!-- machine-true, projected from graph.json -->

## Map — StandStateChange

*Source:* Misc.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| StandStateChange | ctor | — | — | — |
