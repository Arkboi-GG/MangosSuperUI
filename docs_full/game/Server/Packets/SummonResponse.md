# SummonResponse

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# SummonResponse

**Purpose & Responsibilities**

`SummonResponse` is a client-side packet structure within the `WorldPackets::Misc` namespace, defined in `Misc.h`. It represents the `CMSG_SUMMON_RESPONSE` message sent by the game client to the server when a player responds to a summon request. Its primary responsibility is to encapsulate the raw binary data of this specific network message into a structured C++ object, specifically identifying the GUID of the player who issued the summon (`summonerGuid`).

As a `ClientPacket`, it serves as the input interface for the server's handling logic. It does not contain business logic for processing the summon (such as checking validity, moving the player, or updating states); it strictly defines the data contract for receiving the response. The actual deserialization of the network stream into this struct is handled by the inherited `ReadFromWorldPacket` method, which is implemented elsewhere (likely in a corresponding `.cpp` file or via template specialization not shown in this header-only view, though the declaration is present here).

**Member-by-Member Behavior**

The unit contains a single member relevant to its definition in this header:

*   **Constructor (`SummonResponse`)**: Initializes the packet object. It sets the internal opcode to `CMSG_SUMMON_RESPONSE` via the base class `ClientPacket` constructor. It leaves the `summonerGuid` member uninitialized (default constructed `ObjectGuid`), expecting it to be populated during the reading phase.

**Cross-Unit Boundaries**

*   **Inheritance**: Inherits from `ClientPacket` (defined in `Packet.h`). This establishes the base functionality for network packet handling, including opcode management and the interface for reading data from a `WorldPacket`.
*   **Dependencies**:
    *   `ObjectGuid`: Used for the `summonerGuid` member. This type is defined in `ObjectGuid.h` and represents a unique identifier for game objects (players, NPCs, etc.).
    *   `SharedDefines.h`: Likely provides the definition for `CMSG_SUMMON_RESPONSE`, the numeric opcode constant used to identify this packet type on the wire.
*   **Collaboration**:
    *   **Called By**: While the MAP indicates no external callers for the constructor, the `SummonResponse` object itself will be instantiated by the server's packet dispatching system (e.g., in `WorldSession` or a central packet handler) when a `CMSG_SUMMON_RESPONSE` opcode is detected in the incoming data stream.
    *   **Calls Out**: The constructor calls the base `ClientPacket` constructor. The `ReadFromWorldPacket` method (declared here, implemented elsewhere) will call methods on `WorldPacket` (from `Packet.h`) to extract bytes and construct the `ObjectGuid`.

**Data Model**

This unit does not interact directly with any database tables. It operates entirely in memory as part of the network communication layer. The `summonerGuid` it carries may eventually be used by downstream handlers to look up player data in tables such as `characters` or `player_summons`, but `SummonResponse` itself performs no SQL operations.

**Notable Implementation Details**

*   **Final Class**: The class is marked `final`, preventing further inheritance. This is consistent with leaf-node packet structures that should not be subclassed.
*   **Public Member**: `summonerGuid` is declared as a public member variable. This allows the handler that processes this packet to directly access the GUID without needing getter methods, following a common pattern in high-performance game servers where minimal overhead is preferred.
*   **Opcode Binding**: The constructor explicitly binds the instance to `CMSG_SUMMON_RESPONSE`. This ensures that if the packet is serialized back or logged, it retains its identity.
*   **No Default Value for GUID**: Unlike some other packets in `Misc.h` that initialize integer fields to `0`, `summonerGuid` is not explicitly initialized in the constructor. It relies on the default constructor of `ObjectGuid` (which typically creates an invalid/empty GUID) until `ReadFromWorldPacket` populates it. Handlers must ensure `ReadFromWorldPacket` is called before accessing `summonerGuid`.

## Member Reference

**SummonResponse**
Constructor for the `SummonResponse` packet. Initializes the base `ClientPacket` with the opcode `CMSG_SUMMON_RESPONSE`. Leaves `summonerGuid` in its default state.

---

<!-- machine-true, projected from graph.json -->

## Map — SummonResponse

*Source:* Misc.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| SummonResponse | ctor | — | — | — |
