# GroupSwapSubGroup

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# GroupSwapSubGroup

## Purpose & Responsibilities

`GroupSwapSubGroup` is a client-side packet structure within the `WorldPackets::Group` namespace, responsible for representing the `CMSG_GROUP_SWAP_SUB_GROUP` message sent by the game client to the server. Its sole responsibility is to define the data layout for a request to swap two players between subgroups within a raid or party context. It holds two string fields: the name of the player initiating or being moved (`name`) and the name of the player they are swapping with (`nameSwapWith`). As a `ClientPacket`, it serves as the deserialization target for incoming network traffic related to subgroup management.

## Member-by-Member Behavior

The unit consists of a single constructor and two public data members.

*   **Constructor**: Initializes the base `ClientPacket` with the specific opcode `CMSG_GROUP_SWAP_SUB_GROUP`. This ensures that when the packet is processed by the server's packet handler, it is routed to the correct logic for handling subgroup swaps.
*   **Data Members**: The class exposes `name` and `nameSwapWith` as public `std::string` objects. These are populated by the `ReadFromWorldPacket` method (inherited from `ClientPacket` but implemented in the corresponding `.cpp` file, though not shown in the provided source snippet, its existence is implied by the class hierarchy). The constructor does not initialize these strings, leaving them empty until deserialization occurs.

## Cross-Unit Boundaries

*   **Calls Out**: None. The constructor only calls the base class constructor.
*   **Called By**: None listed in the map. In practice, this class is instantiated by the server's packet parsing infrastructure when a `CMSG_GROUP_SWAP_SUB_GROUP` message is received. The parsed instance is then passed to higher-level group management logic (likely in `Group.cpp` or similar) to execute the swap. However, per the provided map, there are no explicit cross-unit callouts defined for this specific unit entry.

## Data Model

This unit does not interact directly with any database tables. It operates purely on in-memory network packet data.

## Notable Implementation Details

*   **String-Based Identification**: Unlike some other group packets in the same header (e.g., `GroupUninviteGuid` or `GroupSetLeader` which use `ObjectGuid` for newer client builds), `GroupSwapSubGroup` relies exclusively on character names (`std::string`). This implies that the server-side handler must resolve these names to player GUIDs before performing any database or memory operations. This approach is consistent with older World of Warcraft client protocols where name-based identification was common for group modifications.
*   **No Default Values**: The strings `name` and `nameSwapWith` are not initialized in the constructor. They rely on the `ReadFromWorldPacket` implementation to populate them correctly from the raw binary data. If the packet is malformed or truncated, these strings may remain empty or contain garbage, requiring validation in the calling server logic.
*   **Inheritance**: It inherits from `ClientPacket`, which provides the framework for reading/writing binary data and managing the packet opcode. The `override` keyword on `ReadFromWorldPacket` in the base class declaration indicates that this class implements the specific deserialization logic for its fields.

## Member Reference

**GroupSwapSubGroup**  
Constructor for the `GroupSwapSubGroup` packet. Initializes the base `ClientPacket` with the opcode `CMSG_GROUP_SWAP_SUB_GROUP`. Does not initialize the member strings; they are populated during deserialization.

---

<!-- machine-true, projected from graph.json -->

## Map — GroupSwapSubGroup

*Source:* Group.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GroupSwapSubGroup | ctor | — | — | — |
