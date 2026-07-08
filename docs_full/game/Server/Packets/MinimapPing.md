# MinimapPing

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

The `MinimapPing` class is a lightweight data structure within the `WorldPackets::Group` namespace, designed to represent a specific client-to-server network message: `MSG_MINIMAP_PING`. Its sole responsibility is to encapsulate the raw coordinate data sent by a client when a player pings a location on their minimap. As a `ClientPacket`, it serves as the input side of the communication channel, providing a typed interface for deserializing binary network data into accessible floating-point coordinates (`x` and `y`). It contains no business logic, validation, or persistence mechanisms; it is purely a transport container.

## Member-by-Member Behavior

### Construction and Initialization

**MinimapPing**
The constructor initializes the packet object for use. It performs two critical setup tasks:
1.  **Base Class Initialization**: It calls the base class `ClientPacket` constructor, passing the opcode `MSG_MINIMAP_PING`. This registers the packet type with the networking layer, ensuring that incoming binary streams with this specific opcode are routed to this class for processing.
2.  **Member Initialization**: It explicitly initializes the public member variables `x` and `y` to `0.0f`. This ensures that if the deserialization process fails or is incomplete, the coordinates default to a known neutral state rather than containing uninitialized garbage values.

### Deserialization Interface

Although not listed in the MAP as a "call out" because it is a virtual override defined in the base class hierarchy, the `ReadFromWorldPacket` method is the functional core of this unit's lifecycle. When the networking layer receives a `MSG_MINIMAP_PING` packet, it invokes this method. The implementation (defined in the corresponding `.cpp` file, not shown here but implied by the `override` keyword) reads two `float` values from the `WorldPacket` buffer and assigns them to `x` and `y`.

## Cross-Unit Boundaries

*   **Calls Out**: None. The `MinimapPing` constructor and its data members do not invoke functions in other units. It is a passive data holder.
*   **Called By**: The MAP indicates no external callers. In practice, instances of `MinimapPing` are typically created by the network dispatcher (part of the `WorldSession` or `Network` subsystem) when a packet with opcode `MSG_MINIMAP_PING` arrives. The dispatcher constructs the object, calls `ReadFromWorldPacket`, and then passes the populated object to a handler function (likely in `GroupHandler.cpp` or similar) to broadcast the ping to other group members. Since these interactions occur outside the scope of the `MinimapPing` class definition itself, they are not reflected in the "Called by" column of the MAP.

## Data Model

This unit does not interact with any database tables. It operates entirely in memory as part of the real-time network communication stack. No SQL queries, table references, or schema dependencies exist in this code.

## Notable Implementation Details

1.  **Public Data Members**: Unlike many C++ classes that enforce encapsulation via getters/setters, `MinimapPing` exposes `x` and `y` as public `float` members. This design choice prioritizes simplicity and performance for a high-frequency network packet, allowing handlers to access the coordinates directly without method overhead.
2.  **Default Values**: The initialization of `x` and `y` to `0.0f` in the constructor is a defensive programming measure. While `ReadFromWorldPacket` should always populate these fields for a valid packet, having safe defaults prevents undefined behavior if the packet parsing logic encounters an error or truncation.
3.  **Namespace Context**: Being nested in `WorldPackets::Group` suggests that minimap pings are logically categorized under group-related communications in this codebase. This implies that pings might be broadcast primarily to group members rather than all nearby players, though the class itself does not enforce this routing logic.
4.  **Final Class**: The class is marked `final`, preventing inheritance. This is appropriate for a leaf-node packet structure that has no need for polymorphic extension.

## Member Reference

**MinimapPing**
Constructor for the `MinimapPing` packet. Initializes the base `ClientPacket` with the opcode `MSG_MINIMAP_PING` and sets the coordinate members `x` and `y` to `0.0f`.

---

<!-- machine-true, projected from graph.json -->

## Map — MinimapPing

*Source:* Group.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| MinimapPing | ctor | — | — | — |
