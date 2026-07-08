# StablePet

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# StablePet Packet Class

## Purpose & Responsibilities

The `StablePet` class is a lightweight data structure within the `WorldPackets::Npc` namespace, designed to represent a specific client-to-server network message: `CMSG_STABLE_PET`. Its sole responsibility is to encapsulate the raw data received from a client when a player attempts to stable a pet. It inherits from `ClientPacket`, indicating it is part of the incoming message processing pipeline. The class does not contain logic for handling the request; it only provides the mechanism to parse the binary stream into structured fields (`npcGuid`) and exposes the packet opcode for identification.

## Member-by-Member Behavior

### **StablePet** (Constructor)
The default constructor initializes the `StablePet` object. It explicitly calls the base class constructor `ClientPacket(CMSG_STABLE_PET)`, registering this instance as representing the `CMSG_STABLE_PET` opcode. This ensures that when the packet router processes incoming data, it can correctly identify and instantiate this specific packet type. No other initialization occurs; the `npcGuid` member remains in its default-initialized state until `ReadFromWorldPacket` is invoked.

## Cross-Unit Boundaries

*   **Calls Out:** None. The constructor performs no external calls.
*   **Called By:** None listed in the map. In practice, this constructor is likely invoked by the central packet parsing infrastructure (e.g., a factory method in `Packet.cpp` or similar) when the server receives a byte stream starting with the `CMSG_STABLE_PET` opcode. However, since these callers are not in the provided MAP, they are not detailed here.

## Data Model

This unit does not interact directly with any database tables. It operates purely on in-memory network packet data.

## Notable Implementation Details

*   **Minimalist Design:** The class contains only one public member variable, `npcGuid` (of type `ObjectGuid`). This reflects the minimal information required by the client to identify which NPC the player is interacting with for the stabling action.
*   **Parsing Logic Delegation:** The actual extraction of `npcGuid` from the raw `WorldPacket` buffer is handled by the `ReadFromWorldPacket` method. While `ReadFromWorldPacket` is declared in the header, it is not listed in the MAP for this specific unit analysis (likely because it is implemented in a corresponding `.cpp` file not included in the "SOURCE" block provided, or considered part of the general `ClientPacket` interface implementation pattern). The MAP only lists the constructor, so documentation focuses on initialization.
*   **Final Class:** The class is marked `final`, preventing inheritance. This is consistent with the design of packet classes, which are leaf nodes in the packet hierarchy.

## Member Reference

**StablePet**: The default constructor for the `StablePet` packet class. It initializes the base `ClientPacket` with the opcode `CMSG_STABLE_PET`, identifying the packet type for the network layer. It does not initialize the `npcGuid` member; that occurs during the reading phase via `ReadFromWorldPacket`.

---

<!-- machine-true, projected from graph.json -->

## Map — StablePet

*Source:* Npc.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| StablePet | ctor | — | — | — |
