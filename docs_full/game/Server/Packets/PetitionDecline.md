# PetitionDecline

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# PetitionDecline

**PetitionDecline** is a client-side network packet class within the `WorldPackets::Petition` namespace, responsible for representing the `MSG_PETITION_DECLINE` message sent from the game client to the server. It encapsulates the data required for a player to decline a petition signature request, specifically identifying the petition item involved via its global unique identifier (`ObjectGuid`). As a `ClientPacket`, it serves as the structured container for incoming network data related to this specific petition action, providing a mechanism to deserialize the raw binary stream into accessible member variables for further processing by the server's game logic.

## Purpose & Responsibilities

The primary responsibility of `PetitionDecline` is to define the structure and deserialization logic for the `MSG_PETITION_DECLINE` opcode. It acts as a lightweight data holder that bridges the gap between the raw network layer and the higher-level game mechanics handling petition interactions. By inheriting from `ClientPacket`, it integrates into the broader packet handling framework, ensuring that the server can correctly identify and parse this specific type of client request. The class does not contain business logic for processing the decline; rather, it strictly manages the extraction of the `itemGuid` from the incoming `WorldPacket`.

## Member-by-Member Behavior

### **PetitionDecline** (Constructor)
The constructor initializes the packet object by calling the base `ClientPacket` constructor with the opcode `MSG_PETITION_DECLINE`. This registration ensures that the packet handler recognizes this instance as belonging to the petition decline protocol. No additional initialization is performed on the member variables at this stage, as they are populated during the reading phase.

### **ReadFromWorldPacket**
Although not explicitly listed in the MAP as a separate callable member due to its virtual nature and internal usage, this method is critical to the class's function. It overrides the base class method to define how the `itemGuid` is extracted from the `WorldPacket` buffer. The implementation reads the `ObjectGuid` from the stream, assigning it to the public member variable. This step is essential for the server to identify which specific petition item the player is declining.

## Cross-Unit Boundaries

*   **Calls out:** None. The `PetitionDecline` class does not invoke methods in other units. Its scope is limited to packet construction and data extraction.
*   **Called by:** The packet handling infrastructure (likely within the network module or session management unit) calls the `ReadFromWorldPacket` method when a `MSG_PETITION_DECLINE` message is received from the client. Subsequently, the game logic unit handling petitions will access the `itemGuid` member to process the decline action.

## Data Model

This unit does not interact directly with any database tables. It operates solely on in-memory network packet data. The `itemGuid` it carries corresponds to an item instance in the game world, which may have associated records in the database (e.g., `item_instance` or similar), but `PetitionDecline` itself performs no SQL operations.

## Notable Implementation Details

*   **Final Class:** The class is marked as `final`, indicating that it cannot be inherited. This enforces a strict hierarchy and prevents unintended subclassing, which is appropriate for a leaf-node packet definition.
*   **Public Member Variable:** The `itemGuid` is declared as a public member variable. This design choice simplifies access for the consuming code, allowing direct reading of the GUID after the packet has been parsed, without needing getter methods.
*   **Opcode Specificity:** The constructor hardcodes the opcode `MSG_PETITION_DECLINE`. This tight coupling ensures that each packet class is exclusively tied to its corresponding network message, reducing ambiguity in the packet dispatch system.

## Member Reference

**PetitionDecline**: The constructor for the `PetitionDecline` class. It initializes the packet with the `MSG_PETITION_DECLINE` opcode by invoking the base `ClientPacket` constructor. It prepares the object to receive and parse incoming network data for petition decline requests.

---

<!-- machine-true, projected from graph.json -->

## Map — PetitionDecline

*Source:* Petition.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| PetitionDecline | ctor | — | — | — |
