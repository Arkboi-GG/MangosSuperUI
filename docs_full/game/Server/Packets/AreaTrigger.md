# AreaTrigger

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AreaTrigger (WorldPackets::Misc)

**Purpose & Responsibilities**

`AreaTrigger` is a client-to-server packet structure within the `WorldPackets::Misc` namespace, defined in `Misc.h`. Its sole responsibility is to represent the `CMSG_AREATRIGGER` message sent by the game client to the server. This packet signals that the player character has entered or triggered a specific area trigger zone, identified by the `triggerId`. It acts as a data carrier, holding the raw identifier until the server-side handler processes the event.

**Member-by-Member Behavior**

The unit contains a single member: the constructor.

*   **`AreaTrigger()`**: The default constructor initializes the packet. It sets the internal packet opcode to `CMSG_AREATRIGGER` via the base class `ClientPacket` and initializes the `triggerId` member variable to `0`. This ensures the object is in a valid, empty state before any data is read from the network stream.

**Cross-Unit Boundaries**

*   **Called by `CombatBotBaseAI/SendAreaTriggerPacket`**: The `CombatBotBaseAI` unit (likely located in `CombatBotBaseAI.cpp`) instantiates this `AreaTrigger` packet when it needs to simulate a player triggering an area effect. The AI creates the packet, presumably populates the `triggerId`, and sends it to the server logic to mimic legitimate client behavior. This indicates that `AreaTrigger` is part of the standard protocol for area interactions, which bots must replicate to function correctly in zones with scripted triggers.

**Data Model**

This unit does not interact directly with any database tables. It is a transient network packet structure.

**Notable Implementation Details**

*   **Inheritance**: `AreaTrigger` inherits from `ClientPacket`, which implies it implements the interface required for deserialization from a `WorldPacket` buffer. Although the `ReadFromWorldPacket` method is declared in the header, its implementation is not shown in the provided source snippet. However, based on the pattern of other packets in `Misc.h` (e.g., `AddFriend`, `Inspect`), it is expected to extract the `triggerId` from the incoming binary data.
*   **Default Initialization**: The `triggerId` is explicitly initialized to `0` in the constructor. This is a defensive measure to ensure that if the packet is constructed but not properly populated or read, it holds a known invalid or neutral value rather than garbage memory.
*   **Final Class**: The class is marked `final`, preventing further inheritance. This enforces that `AreaTrigger` is a leaf node in the packet hierarchy, suitable for direct instantiation and use.

## Member Reference

**AreaTrigger**
The default constructor for the `AreaTrigger` packet. It initializes the base `ClientPacket` with the opcode `CMSG_AREATRIGGER` and sets the `triggerId` member to `0`. This prepares the object to receive data from the network or to be manually populated by callers such as `CombatBotBaseAI/SendAreaTriggerPacket`.

---

<!-- machine-true, projected from graph.json -->

## Map — AreaTrigger

*Source:* Misc.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AreaTrigger | ctor | — | CombatBotBaseAI/SendAreaTriggerPacket | — |
