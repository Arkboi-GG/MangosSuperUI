# SetFactionInactive

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# SetFactionInactive

**Purpose & Responsibilities**

`SetFactionInactive` is a client-side packet structure within the `WorldPackets::Misc` namespace, defined in `Misc.h`. It represents the `CMSG_SET_FACTION_INACTIVE` message sent by the game client to the server. Its sole responsibility is to encapsulate the raw data payload of this specific network message: a faction reputation list identifier (`replistid`) and a boolean flag (`inactive`) indicating whether the client wishes to mark that faction as inactive (hidden from the reputation window).

This class is part of a larger family of `ClientPacket` subclasses that handle various miscellaneous interactions (teleports, friends, emotes, etc.). Like its siblings, it inherits the base networking contract from `ClientPacket`, providing a default constructor that registers the correct opcode and a pure virtual interface for deserialization (`ReadFromWorldPacket`). The actual deserialization logic is implemented in the corresponding `.cpp` file (not provided here, but implied by the `override` keyword), while this header defines the data layout.

**Member-by-Member Behavior**

The unit contains a single member: the constructor.

*   **`SetFactionInactive()`**: This default constructor initializes the object. It calls the base `ClientPacket` constructor with the constant `CMSG_SET_FACTION_INACTIVE`, ensuring the packet is correctly identified by the server's message dispatcher. It also initializes the two public data members, `replistid` and `inactive`, to zero. This zero-initialization provides a safe default state before the packet is populated via `ReadFromWorldPacket`.

**Cross-Unit Boundaries**

*   **Calls Out**: None. The constructor only invokes the base class constructor.
*   **Called By**: None listed in the map. In practice, instances of this class are typically created by the server's packet handling infrastructure when a `CMSG_SET_FACTION_INACTIVE` message is received from the network layer. The server would then instantiate this object, call `ReadFromWorldPacket` to populate `replistid` and `inactive`, and pass the object to the relevant game logic handler (likely within a `Player` or `Reputation` manager class, though those units are not part of this documentation scope).

**Data Model**

This unit does not interact directly with any database tables. It operates purely on in-memory network packet data. The `replistid` field corresponds to an internal ID used by the server's reputation system, which may eventually map to rows in a `faction` or `reputation` table during later processing stages, but `SetFactionInactive` itself performs no SQL operations.

**Notable Implementation Details**

*   **Conditional Compilation**: The entire class definition is wrapped in `#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_9_4`. This indicates that the `CMSG_SET_FACTION_INACTIVE` opcode and the associated "inactive faction" feature were introduced in client versions newer than 1.9.4. For older clients, this class does not exist, preventing compilation errors or invalid packet handling for unsupported features.
*   **Public Data Members**: Unlike some encapsulated designs, `replistid` and `inactive` are public. This allows the calling code (the packet handler) to access these fields directly after deserialization without needing getter methods, favoring simplicity and performance in the hot path of network processing.
*   **Zero Initialization**: Both `replistid` and `inactive` are explicitly initialized to `0` in the constructor. This is a defensive measure to ensure that if `ReadFromWorldPacket` fails or is not called, the fields hold predictable values rather than garbage memory.

## Member Reference

**SetFactionInactive**
Constructor for the `SetFactionInactive` packet. Initializes the base `ClientPacket` with the `CMSG_SET_FACTION_INACTIVE` opcode and sets the `replistid` and `inactive` data members to zero. This class is only compiled for client builds newer than 1.9.4.

---

<!-- machine-true, projected from graph.json -->

## Map — SetFactionInactive

*Source:* Misc.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| SetFactionInactive | ctor | — | — | — |
