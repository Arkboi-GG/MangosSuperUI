# ChannelUnmute

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ChannelUnmute

**Purpose & Responsibilities**

`ChannelUnmute` is a client-side packet structure within the `WorldPackets::Channel` namespace, designed to represent the `CMSG_CHANNEL_UNMUTE` message sent from the game client to the server. Its sole responsibility is to define the data layout for a request to remove a mute status from a specific player within a specific chat channel. It acts as a passive data container, holding the channel name and the target player's name until the packet is processed by the server's network handler.

**Member-by-Member Behavior**

The unit contains a single member: the constructor.

*   **`ChannelUnmute`**: This explicit constructor initializes the packet object. It sets the internal packet opcode to `CMSG_CHANNEL_UNMUTE` by calling the base class `ClientPacket` constructor. It leaves the `channelName` and `playerName` member variables in their default-initialized state (empty strings), awaiting population via the `ReadFromWorldPacket` method (which is declared in this header but implemented elsewhere, likely in a corresponding `.cpp` file or via template instantiation not shown here, though typically these are inline or in a separate implementation file for the `WorldPackets` module).

**Cross-Unit Boundaries**

*   **Calls Out**: The constructor calls the `ClientPacket` constructor (from the `Packet.h` unit, implied by the `#include "Packet.h"` directive). This establishes the packet's identity within the network protocol stack.
*   **Called By**: According to the provided MAP, this unit is not called by any other external units. In practice, instances of `ChannelUnmute` are created by the network reception layer when a `CMSG_CHANNEL_UNMUTE` opcode is detected on the wire. The `ReadFromWorldPacket` method (declared here) will be invoked by that network layer to deserialize the binary data into the `channelName` and `playerName` fields.

**Data Model**

This unit does not interact directly with any database tables. It operates purely on transient network data. The `channelName` and `playerName` strings are temporary payloads used for immediate processing of the unmute request.

**Notable Implementation Details**

*   **Final Class**: The class is marked `final`, preventing inheritance. This enforces a strict, flat hierarchy for packet types, ensuring that `ChannelUnmute` cannot be subclassed to alter its behavior or memory layout.
*   **Explicit Constructor**: The use of `explicit` prevents implicit conversions from `CMSG_CHANNEL_UNMUTE` (or other types) to `ChannelUnmute`, reducing the risk of accidental object creation.
*   **String Storage**: The class uses `std::string` for both `channelName` and `playerName`. This implies dynamic memory allocation for the string contents during deserialization. Maintainers should be aware that repeated creation and destruction of these packets in high-frequency scenarios could lead to heap fragmentation, though this is generally managed by the standard library's small-string optimization (SSO) for short names.
*   **Missing Implementation**: The `ReadFromWorldPacket` method is declared but not defined in this header. The actual logic for parsing the binary stream into the `std::string` members resides outside this unit. The correctness of the unmute request depends entirely on that external implementation correctly handling encoding (likely UTF-8) and null-termination.

## Member Reference

**ChannelUnmute**
Constructor for the `ChannelUnmute` packet. Initializes the base `ClientPacket` with the opcode `CMSG_CHANNEL_UNMUTE`. Leaves `channelName` and `playerName` empty until populated by `ReadFromWorldPacket`.

---

<!-- machine-true, projected from graph.json -->

## Map — ChannelUnmute

*Source:* Channel.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ChannelUnmute | ctor | — | — | — |
