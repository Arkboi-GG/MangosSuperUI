# LeaveChannel

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# LeaveChannel

`LeaveChannel` is a client-side packet structure within the `WorldPackets::Channel` namespace, responsible for representing the `CMSG_LEAVE_CHANNEL` message sent by the game client to the server. Its sole responsibility is to deserialize the raw binary data from the incoming network packet into a structured object containing the name of the channel the player wishes to leave. It contains no business logic, validation, or side effects; it is purely a data carrier for the network layer.

## Member-by-Member Behavior

The unit consists of a single constructor and relies on the inherited `ReadFromWorldPacket` method (defined in `ClientPacket` or a related base class, though not shown in this specific header snippet, it is declared in the class interface) to perform the actual deserialization.

### Construction and Initialization
The **LeaveChannel** constructor initializes the packet object. It explicitly calls the base class `ClientPacket` constructor, passing the opcode `CMSG_LEAVE_CHANNEL`. This associates the packet instance with the correct network protocol identifier, ensuring that when the packet is processed downstream, the system recognizes it as a request to leave a channel. The member variable `channelName` is default-initialized as an empty string at this stage; it will be populated later during the reading phase.

### Deserialization
While the `ReadFromWorldPacket` method is declared in the class, its implementation is not present in the provided source files. However, based on the class structure and standard patterns in this codebase, this method is responsible for extracting the `channelName` string from the `WorldPacket` buffer. The `channelName` member holds the target channel identifier provided by the client.

## Cross-Unit Boundaries

### Incoming Calls
*   **ChatHandler.CharacterCommands/HandleChannelLeaveCommand**: This unit creates an instance of `LeaveChannel`. The chat handler receives the raw network packet, constructs a `LeaveChannel` object to parse the data, and then uses the parsed `channelName` to execute the logic for removing the player from the specified channel. The direction of data flow is from the network layer (via the chat handler) into this packet structure for parsing, and then back out to the handler for processing.

### Outgoing Calls
*   None. The `LeaveChannel` class does not call into any other units. It is a passive data structure.

## Data Model

This unit does not interact directly with any database tables. It operates entirely in memory, handling transient network data.

## Notable Implementation Details

*   **Namespace Organization**: The class is nested within `WorldPackets::Channel`, indicating it is part of a modular packet handling system where channel-related messages are grouped together.
*   **Final Class**: The class is marked `final`, preventing inheritance. This enforces a strict, leaf-node design for this specific packet type, ensuring no further specialization occurs.
*   **String Storage**: The `channelName` is stored as a `std::string`. This implies that the deserialization process (in `ReadFromWorldPacket`) handles the conversion from the network byte format (likely a null-terminated string or length-prefixed string) into a C++ string object.
*   **No Validation**: The class itself performs no validation on the `channelName`. It simply stores whatever is provided by the client. Any checks regarding whether the channel exists, whether the player is currently in it, or whether the name is valid must be performed by the caller (`ChatHandler.CharacterCommands/HandleChannelLeaveCommand`).

## Member Reference

**LeaveChannel**
Constructor for the `LeaveChannel` packet. Initializes the base `ClientPacket` with the opcode `CMSG_LEAVE_CHANNEL`. Prepares the object to receive and store the `channelName` from the incoming network stream.

---

<!-- machine-true, projected from graph.json -->

## Map — LeaveChannel

*Source:* Channel.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| LeaveChannel | ctor | — | ChatHandler.CharacterCommands/HandleChannelLeaveCommand | — |
