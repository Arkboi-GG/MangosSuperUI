# TutorialFlag

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# TutorialFlag

**Purpose & Responsibilities**

`TutorialFlag` is a client-side packet structure within the `WorldPackets::Misc` namespace, defined in `Misc.h`. It represents the `CMSG_TUTORIAL_FLAG` message sent from the game client to the server. Its sole responsibility is to carry a single 32-bit unsigned integer (`iFlag`) that indicates which tutorial step or flag the client is reporting as completed or triggered. This packet is part of the broader miscellaneous client-to-server communication layer, handling discrete, low-frequency interactions such as tutorial progress, emotes, and faction changes.

**Member-by-Member Behavior**

The unit contains only one member: the constructor.

*   **Constructor (`TutorialFlag`)**: Initializes the packet object. It sets the internal opcode to `CMSG_TUTORIAL_FLAG` via the base class `ClientPacket` constructor and initializes the public member `iFlag` to `0`. This default value ensures that if the packet is instantiated but not yet populated from network data, it holds a safe, neutral state. The actual population of `iFlag` occurs later via the inherited `ReadFromWorldPacket` method, which is implemented in a separate unit (not detailed in this partial).

**Cross-Unit Boundaries**

*   **Calls Out**: None. The constructor performs no external calls.
*   **Called By**: None listed in the MAP. In practice, instances of `TutorialFlag` are typically created by the packet dispatching system when the server receives a raw network buffer with the `CMSG_TUTORIAL_FLAG` opcode. The dispatcher will instantiate this class and call its `ReadFromWorldPacket` method (defined in another unit) to parse the binary data into the `iFlag` field.

**Data Model**

This unit does not interact directly with any database tables. It is a transient data structure representing a network message. Any persistence of tutorial flags would occur in higher-level handler logic (e.g., in a `Player` or `Tutorial` class) after the packet is processed, potentially updating a `characters` table or a dedicated `tutorial_flags` table, but such logic is outside the scope of this packet definition.

**Notable Implementation Details**

*   **Inheritance**: Inherits from `ClientPacket`, which provides the common interface for all client-to-server messages, including the `ReadFromWorldPacket` virtual method.
*   **Default Initialization**: The member `iFlag` is explicitly initialized to `0` in the class declaration. This is a defensive measure, ensuring that even if `ReadFromWorldPacket` fails or is not called, the flag value is well-defined.
*   **Opcode Association**: The constructor binds this class to the specific network opcode `CMSG_TUTORIAL_FLAG`, allowing the server's packet router to correctly identify and handle incoming tutorial flag updates.

## Member Reference

**TutorialFlag**  
Constructor for the `TutorialFlag` packet. Initializes the base `ClientPacket` with the opcode `CMSG_TUTORIAL_FLAG` and sets the `iFlag` member to `0`. No external calls are made.

---

<!-- machine-true, projected from graph.json -->

## Map — TutorialFlag

*Source:* Misc.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| TutorialFlag | ctor | — | — | — |
