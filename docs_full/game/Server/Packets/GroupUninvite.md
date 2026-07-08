# GroupUninvite

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# GroupUninvite

## Purpose & Responsibilities

`GroupUninvite` is a client-side packet structure within the `WorldPackets::Group` namespace, responsible for representing the `CMSG_GROUP_UNINVITE` message sent from the game client to the server. Its sole responsibility is to define the data layout for a request to remove a specific player from a group, identified by their character name.

As part of the `ClientPacket` hierarchy, it serves as a container for deserialization logic, allowing the network layer to extract the `memberName` field from the raw binary stream received over the wire. It does not contain business logic for validating the uninvite, checking permissions, or modifying group state; those responsibilities lie in the handler units that consume this packet after it has been constructed.

## Member-by-Member Behavior

The unit contains a single member: the constructor.

**GroupUninvite** initializes the packet object by invoking the base class `ClientPacket` constructor with the opcode `CMSG_GROUP_UNINVITE`. This registers the packet type with the network framework, ensuring that incoming data streams matching this opcode are routed to instances of this class for parsing. The `memberName` string member is default-initialized to an empty string by the compiler, awaiting population via the `ReadFromWorldPacket` method (which is declared in the header but implemented in the corresponding `.cpp` file, not shown here, though its signature is part of the class interface).

## Cross-Unit Boundaries

*   **Calls Out:** None. The constructor performs no external calls.
*   **Called By:** The MAP indicates no external callers for the constructor itself. However, in the broader system context, this class is instantiated by the network packet dispatcher when a `CMSG_GROUP_UNINVITE` opcode is detected. The resulting object is then passed to a handler (likely in a unit such as `GroupHandler` or similar, depending on the project structure) which reads the `memberName` and executes the uninvite logic.

## Data Model

This unit does not interact directly with any database tables. It operates purely on in-memory network packet data.

## Notable Implementation Details

*   **String-Based Identification:** Unlike some newer packet structures in the same header (e.g., `GroupUninviteGuid` or `GroupSetLeader` in builds > 1.11.2), `GroupUninvite` relies on a `std::string` (`memberName`) to identify the target player. This reflects older client protocols where names were used instead of GUIDs for group management commands.
*   **Final Class:** The class is marked `final`, preventing inheritance. This is consistent with its role as a leaf node in the packet hierarchy, representing a specific, fixed protocol message format.
*   **Namespace Isolation:** It resides in `WorldPackets::Group`, clearly segregating group-related network messages from other world packet types.

## Member Reference

**GroupUninvite**  
Constructor that initializes the `ClientPacket` base class with the opcode `CMSG_GROUP_UNINVITE`. It sets up the packet instance for subsequent deserialization of the `memberName` field from the incoming network stream.

---

<!-- machine-true, projected from graph.json -->

## Map — GroupUninvite

*Source:* Group.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GroupUninvite | ctor | — | — | — |
