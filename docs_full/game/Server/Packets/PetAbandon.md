# PetAbandon

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`PetAbandon` is a client-to-server network packet structure within the `WorldPackets::Pet` namespace. Its sole responsibility is to represent the `CMSG_PET_ABANDON` message sent by the game client when a player attempts to abandon a pet. The class encapsulates the raw binary data received from the network, specifically holding the `ObjectGuid` of the pet targeted for abandonment, and provides the interface (`ReadFromWorldPacket`) to deserialize this data from the incoming network stream. It contains no business logic, validation, or side effects; it is purely a data carrier for the network layer.

## Member-by-Member Behavior

### Construction and Initialization
**`PetAbandon`** (Constructor)
The default constructor initializes the packet object. It calls the base class `ClientPacket` constructor, passing the constant `CMSG_PET_ABANDON` to identify the packet type. This ensures that when the packet is processed later in the server pipeline, it is correctly routed to the handler responsible for pet abandonment logic. The member variable `guid` is not explicitly initialized in the constructor definition shown, relying on default initialization or subsequent assignment during deserialization.

### Deserialization
**`ReadFromWorldPacket`**
This virtual method overrides the base class implementation to parse the binary content of the incoming `WorldPacket`. While the implementation body is not provided in the source snippet (it is likely defined in a corresponding `.cpp` file or inline elsewhere), its signature indicates it accepts a reference to `WorldPacket& recv_data`. Its role is to extract the `ObjectGuid` from the network buffer and store it in the public member `guid`. This prepares the packet object for consumption by the server's game logic handlers, which will use the `guid` to identify the specific pet entity to be removed or released.

## Cross-Unit Boundaries

*   **Calls Out:** None. The `PetAbandon` class itself does not invoke methods on other units. It is a passive data structure.
*   **Called By:** None listed in the MAP. However, in the broader system context, instances of `PetAbandon` are typically constructed by the network input parser (e.g., `WorldSession` or a packet factory) when a `CMSG_PET_ABANDON` opcode is detected. The parsed object is then passed to a handler function (likely in a unit such as `PetHandler.cpp` or similar) which executes the actual abandonment logic. The MAP confirms no direct cross-unit calls are attributed to this specific partial/unit in the provided scope.

## Data Model

This unit does not interact directly with any database tables. It operates entirely in memory as part of the network packet processing pipeline. Any persistence changes resulting from a pet abandonment (such as removing a pet from a player's saved pets table) would occur in downstream handler units after this packet has been successfully parsed and validated.

## Notable Implementation Details

*   **Inheritance:** `PetAbandon` inherits from `ClientPacket`, indicating it originates from the client. This distinguishes it from server-to-client packets.
*   **Public Member Access:** The `guid` member is public. This design choice allows the receiving handler to access the pet's GUID directly without needing getter methods, simplifying the interface for the packet consumer.
*   **No Validation:** The class performs no validation of the `guid` format or validity. It assumes the network layer has already verified the packet integrity and length. Invalid GUIDs would result in undefined behavior or errors in the subsequent handling stage.
*   **Minimalist Design:** The class contains only the essential data (`guid`) required to identify the target of the action. It does not include player information, timestamps, or other metadata, which are likely handled by the surrounding session or handler context.

## Member Reference

**PetAbandon**
The default constructor for the `PetAbandon` packet class. It initializes the base `ClientPacket` with the `CMSG_PET_ABANDON` opcode, preparing the object to receive and hold data related to a pet abandonment request from the client.

---

<!-- machine-true, projected from graph.json -->

## Map — PetAbandon

*Source:* Pet.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| PetAbandon | ctor | — | — | — |
