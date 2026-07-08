# DelFriend

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# DelFriend

**DelFriend** is a client-to-server packet structure within the `WorldPackets::Misc` namespace, representing the request to remove a player from the sender's friends list. It encapsulates the unique identifier (`ObjectGuid`) of the friend being removed and provides the mechanism to deserialize this data from the raw network stream upon receipt by the server.

## Purpose & Responsibilities

The primary responsibility of `DelFriend` is to act as a data container for the `CMSG_DEL_FRIEND` opcode. When a client sends a request to remove a contact from their friends list, the server receives the raw byte stream, instantiates a `DelFriend` object, and populates its `friendGuid` member via the `ReadFromWorldPacket` method. This object then serves as the interface through which higher-level game logic (such as social manager handlers) can access the target of the deletion request.

As a `final` class inheriting from `ClientPacket`, it adheres to the standard packet lifecycle: construction with a specific opcode, reading from the incoming world packet, and exposing parsed fields for downstream processing.

## Member-by-Member Behavior

### **DelFriend** (Constructor)
The default constructor initializes the packet with the opcode `CMSG_DEL_FRIEND`. It ensures that the packet is correctly identified by the server's packet dispatcher before any data is read. No additional state is initialized here, as the `friendGuid` will be populated during the read phase.

## Cross-Unit Boundaries

*   **Calls Out:** None. The `DelFriend` class itself does not initiate calls to other units. Its role is purely passive data holding and parsing.
*   **Called By:** The packet dispatcher system (not explicitly listed in the map but implied by the `ClientPacket` inheritance) creates instances of `DelFriend` when a `CMSG_DEL_FRIEND` packet is received. Subsequently, the social system handler (likely in a unit such as `SocialMgr` or `PlayerSocial`) will call `ReadFromWorldPacket` (implicitly via the dispatcher) and then access `friendGuid` to perform the actual removal logic.

## Data Model

This unit does not interact directly with any database tables. It operates entirely in memory, handling transient network data. The persistence of the friends list is managed by higher-level components that use the `friendGuid` provided by this packet to update the relevant database records.

## Notable Implementation Details

*   **GUID vs. Name:** A notable distinction in `Misc.h` is that `AddFriend` uses a `std::string friendName`, while `DelFriend` uses an `ObjectGuid friendGuid`. This suggests that adding a friend is initiated by name lookup, whereas removing a friend is done by referencing the already-resolved GUID, which is more robust against name collisions or changes.
*   **Final Class:** The class is marked `final`, preventing further derivation. This enforces a strict contract for this specific packet type, ensuring no subclassing alters its behavior.
*   **Namespace:** It resides in `WorldPackets::Misc`, grouping it with other miscellaneous client commands that do not fit into larger subsystems like combat, movement, or chat.

## Member Reference

**DelFriend**
The constructor for the `DelFriend` packet. It initializes the base `ClientPacket` with the opcode `CMSG_DEL_FRIEND`. It does not take any arguments and prepares the object to receive data from the network stream.

---

<!-- machine-true, projected from graph.json -->

## Map — DelFriend

*Source:* Misc.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| DelFriend | ctor | — | — | — |
