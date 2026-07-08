# SetSheathed

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`SetSheathed` is a packet structure within the `WorldPackets::Combat` namespace, designed to represent the incoming client message `CMSG_SETSHEATHED`. Its sole responsibility is to encapsulate the data payload associated with a player changing their weapon sheath state (e.g., equipping or unequipping a weapon) as received from the game client. It inherits from `ClientPacket`, marking it as a message originating from the client side of the network connection.

## Member-by-Member Behavior

The unit contains a single member: the constructor.

*   **`SetSheathed()`**: This explicit constructor initializes the packet instance. It sets the default value of the `sheathed` member variable to `0` and invokes the base class `ClientPacket` constructor, passing the opcode `CMSG_SETSHEATHED`. This registration ensures that the packet handler system can correctly identify and route this specific message type when it arrives on the network socket.

## Cross-Unit Boundaries

As defined in the provided MAP, `SetSheathed` has no outgoing calls to other units and is not called by other units in the context of this specific translation unit's dependency graph. However, structurally, it relies on:
*   **`ClientPacket`**: The base class provides the infrastructure for packet identification and deserialization. The constructor delegates to `ClientPacket` to register the opcode.
*   **`WorldPacket`**: Although not explicitly listed in the "Calls out" column of the MAP for the constructor, the class declares an override for `ReadFromWorldPacket`, which implies a future interaction with the `WorldPacket` unit (likely defined elsewhere) to deserialize the `sheathed` field from the raw binary stream.

## Data Model

This unit does not interact with any database tables. It operates entirely in memory as part of the network packet processing layer.

## Notable Implementation Details

*   **Default State**: The `sheathed` field is initialized to `0` in the class definition. This suggests that if the deserialization process fails or is skipped, the packet defaults to a "unsheathed" or "default" state, though the exact semantic meaning of `0` vs other values depends on the protocol specification handled by the `ReadFromWorldPacket` implementation (which is not present in this source file).
*   **Explicit Constructor**: The use of `explicit` prevents implicit conversions from integers or other types into a `SetSheathed` object, ensuring type safety during packet creation.
*   **Final Class**: The class is marked `final`, indicating that it cannot be subclassed. This is appropriate for a leaf-node packet structure that represents a specific, fixed protocol message.

## Member Reference

**SetSheathed**
Constructor for the `SetSheathed` packet. Initializes the `sheathed` member to `0` and registers the packet with the base `ClientPacket` class using the opcode `CMSG_SETSHEATHED`.

---

<!-- machine-true, projected from graph.json -->

## Map — SetSheathed

*Source:* Combat.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| SetSheathed | ctor | — | — | — |
