# PetRename

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# PetRename

**Purpose & Responsibilities**

`PetRename` is a client-side network packet structure within the `WorldPackets::Pet` namespace, responsible for deserializing the `CMSG_PET_RENAME` message sent by the game client. Its sole responsibility is to extract the target pet's unique identifier (`ObjectGuid`) and the desired new name (`std::string`) from the raw binary data received over the network. This unit acts as a data carrier, preparing these fields for consumption by higher-level game logic handlers that validate and execute the rename operation. It does not perform validation, database updates, or server-to-client responses itself.

**Member-by-Member Behavior**

The unit contains a single member: the constructor.

*   **`PetRename`**: The default constructor initializes the `ClientPacket` base class with the opcode `CMSG_PET_RENAME`. This registration ensures that when the network layer receives a packet with this specific opcode, it instantiates a `PetRename` object to handle the deserialization. The member variables `petGuid` and `name` are default-initialized (empty GUID and empty string) until `ReadFromWorldPacket` is invoked by the network framework.

**Cross-Unit Boundaries**

*   **Calls Out**: None. The constructor performs no external calls.
*   **Called By**: None explicitly listed in the map. However, implicitly, the network dispatch system (likely within `WorldSession` or a central packet handler) will instantiate this class and call its `ReadFromWorldPacket` method when a `CMSG_PET_RENAME` packet arrives. The resulting object is then passed to a handler function (not part of this unit) that processes the rename request.

**Data Model**

This unit does not interact directly with any database tables. It operates purely on in-memory network packet data. Any persistence of the pet's name would occur in downstream handlers using tables such as `character_pet`, but `PetRename` itself is agnostic to storage mechanisms.

**Notable Implementation Details**

*   **Inheritance**: Inherits from `ClientPacket`, indicating it represents data flowing from the client to the server.
*   **Deserialization Dependency**: The actual extraction of `petGuid` and `name` from the `WorldPacket` buffer is handled by the `ReadFromWorldPacket` method, which is declared in this header but implemented elsewhere (likely in a corresponding `.cpp` file not included in this partial view, or potentially inline in a separate compilation unit). The header only declares the interface.
*   **Type Safety**: Uses `ObjectGuid` for the pet identifier, ensuring type-safe handling of entity references compared to raw integers.
*   **String Handling**: Uses `std::string` for the name, implying variable-length encoding in the network packet, typically preceded by a length prefix or terminated by a null character depending on the specific `WorldPacket` reading implementation.

## Member Reference

**PetRename**  
Constructor for the `PetRename` packet. Initializes the base `ClientPacket` with the opcode `CMSG_PET_RENAME`. Prepares the object to receive deserialized data for a pet rename request.

---

<!-- machine-true, projected from graph.json -->

## Map — PetRename

*Source:* Pet.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| PetRename | ctor | — | — | — |
