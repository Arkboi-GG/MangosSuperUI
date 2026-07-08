# ChannelUnban

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ChannelUnban

**Purpose & Responsibilities**

`ChannelUnban` is a lightweight data structure within the `WorldPackets::Channel` namespace, defined in `Channel.h`. It represents a specific client-to-server network message (`CMSG_CHANNEL_UNBAN`) used in the WoWVMaNGOS server architecture. Its sole responsibility is to encapsulate the raw payload of an "unban" request sent by a client, holding the target channel name and the player name to be unbanned. It acts as a passive container; all parsing logic is delegated to its inherited `ReadFromWorldPacket` method, while its own constructor simply registers the packet type.

**Member-by-Member Behavior**

The unit contains only one member: the default constructor.

*   **`ChannelUnban()`**: This explicit constructor initializes the `ClientPacket` base class with the opcode `CMSG_CHANNEL_UNBAN`. It does not perform any validation, memory allocation, or field initialization beyond what the base class provides. The public member variables `channelName` and `playerName` are left uninitialized until `ReadFromWorldPacket` is invoked by the networking layer.

**Cross-Unit Boundaries**

*   **Calls Out**: None. The constructor does not invoke any external functions or classes.
*   **Called By**: None listed in the map. In practice, this class is instantiated by the network packet dispatcher (likely in `WorldSession` or a similar handler) when a packet with opcode `CMSG_CHANNEL_UNBAN` is received. The dispatcher then calls `ReadFromWorldPacket` (defined in the base class or implemented elsewhere) to populate the `channelName` and `playerName` fields.

**Data Model**

This unit does not interact directly with any database tables. It operates entirely in memory as part of the network I/O layer. The `channelName` and `playerName` strings are transient data extracted from the network stream. Any persistence of ban/unban states would occur in downstream handlers that process this packet, not within this class itself.

**Notable Implementation Details**

*   **Passive Structure**: Like all other `ClientPacket` derivatives in `Channel.h`, `ChannelUnban` follows a consistent pattern: it defines public string members for the payload and relies on the base class infrastructure for packet identification and reading. There is no custom logic in this partial.
*   **Explicit Constructor**: The use of `explicit` prevents implicit conversions from `CMSG_CHANNEL_UNBAN` (if it were convertible) or other types, ensuring type safety during instantiation.
*   **Namespace**: It resides in `WorldPackets::Channel`, indicating it is part of the world server's packet handling subsystem, distinct from authentication or other server modules.

## Member Reference

**ChannelUnban**
Constructor for the `ChannelUnban` packet class. Initializes the base `ClientPacket` with the opcode `CMSG_CHANNEL_UNBAN`. Does not initialize the public string members `channelName` or `playerName`; these are populated later via `ReadFromWorldPacket`.

---

<!-- machine-true, projected from graph.json -->

## Map — ChannelUnban

*Source:* Channel.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ChannelUnban | ctor | — | — | — |
