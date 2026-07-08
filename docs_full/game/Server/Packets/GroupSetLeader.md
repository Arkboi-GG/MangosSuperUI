# GroupSetLeader

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# GroupSetLeader

## Purpose & Responsibilities

`GroupSetLeader` is a client-side packet structure within the `WorldPackets::Group` namespace, responsible for encapsulating the data sent by the client when a player attempts to change the leader of their group or raid. It serves as the data carrier for the `CMSG_GROUP_SET_LEADER` message, translating raw network bytes into a structured C++ object that the server can process.

Its primary responsibility is to hold the identifier of the player who is being designated as the new leader. Crucially, the specific type of this identifier depends on the supported client version, reflecting changes in how World of Warcraft clients identify players over time.

## Member-by-Member Behavior

The unit consists of a single constructor and associated data members defined in the header.

### Construction and Initialization

**`GroupSetLeader()`**
This default constructor initializes the packet structure. Its key behavior is registering the packet with the underlying `ClientPacket` base class using the opcode `CMSG_GROUP_SET_LEADER`. This ensures that when the server receives a packet with this opcode, it can correctly instantiate and deserialize a `GroupSetLeader` object.

### Data Members

The class contains conditional data members based on the `SUPPORTED_CLIENT_BUILD` macro:

*   **For clients newer than 1.11.2 (`CLIENT_BUILD_1_11_2`):**
    *   `ObjectGuid guid`: Stores the unique global identifier (GUID) of the player being set as leader. Modern clients use GUIDs for robust player identification, avoiding ambiguity caused by name changes or duplicates.

*   **For clients 1.11.2 and older:**
    *   `std::string name`: Stores the character name of the player being set as leader. Older clients relied on character names for group management operations.

This conditional compilation ensures backward compatibility with older client versions while supporting modern identification methods for newer ones.

## Cross-Unit Boundaries

*   **Calls Out:** None. The constructor only interacts with its base class `ClientPacket`.
*   **Called By:** None listed in the map. In practice, this packet is instantiated by the network layer when a `CMSG_GROUP_SET_LEADER` message is received from a client. The deserialization logic (implemented in `ReadFromWorldPacket`, which is declared here but defined elsewhere) will populate either `guid` or `name` depending on the client build. Subsequent server-side logic (not part of this unit) will use this data to validate permissions and execute the leadership change.

## Data Model

This unit does not interact directly with any database tables. It operates purely on in-memory packet data received from the client. Any persistence related to group leadership would occur in higher-level server logic after this packet has been processed.

## Notable Implementation Details

*   **Version-Specific Identification:** The most significant detail is the `#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_11_2` directive. This means the structure of the `GroupSetLeader` packet in memory changes entirely based on the compile-time configuration of the server. Code handling this packet must account for which member (`guid` or `name`) is present and valid.
*   **No Default Values:** Unlike some other packets in the same header (e.g., `LootMethod` which initializes `lootMethod = 0`), `GroupSetLeader` does not initialize its data members in the constructor. They are expected to be populated exclusively by the `ReadFromWorldPacket` method during deserialization. Accessing these members before deserialization results in undefined behavior (uninitialized string or GUID).
*   **Final Class:** The class is marked `final`, indicating it cannot be inherited. This is appropriate for a leaf packet structure.

## Member Reference

**GroupSetLeader**
Default constructor that initializes the `ClientPacket` base with the `CMSG_GROUP_SET_LEADER` opcode. It prepares the object to receive data for changing the group leader. The actual data storage (`guid` or `name`) is determined by the `SUPPORTED_CLIENT_BUILD` preprocessor macro and is populated later by `ReadFromWorldPacket`.

---

<!-- machine-true, projected from graph.json -->

## Map — GroupSetLeader

*Source:* Group.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GroupSetLeader | ctor | — | — | — |
