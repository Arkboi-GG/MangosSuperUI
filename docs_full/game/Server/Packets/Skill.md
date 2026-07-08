# Skill

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Skill Packet Handlers

The `Skill` unit defines three client-to-server packet structures within the `WorldPackets::Skill` namespace. These classes represent incoming network messages related to character progression mechanics: learning talents, unlearning skills, and confirming talent resets. Each class inherits from `ClientPacket`, indicating these are packets received from the game client. The primary responsibility of this unit is to deserialize raw binary data from `WorldPacket` objects into structured C++ fields (`talent_id`, `skillId`, `guid`, etc.) so that higher-level game logic can process the player's intent.

This unit contains no database interactions, no outgoing packet generation, and no complex validation logic. It serves strictly as a deserialization layer for specific `CMSG_*` and `MSG_*` opcodes.

## Member-by-Member Behavior

### Talent Learning
**`LearnTalent`** represents the `CMSG_LEARN_TALENT` opcode. When a client sends this packet, it intends to learn a specific talent at a specific rank.
*   **Construction**: Initializes the packet type to `CMSG_LEARN_TALENT` and sets `talent_id` and `requested_rank` to 0.
*   **Deserialization**: The `ReadFromWorldPacket` method extracts two 32-bit unsigned integers from the incoming buffer: first the `talent_id`, then the `requested_rank`.

### Skill Unlearning
**`UnlearnSkill`** represents the `CMSG_UNLEARN_SKILL` opcode. This is used when a player wishes to remove a learned skill, often to free up space or reset progress.
*   **Construction**: Initializes the packet type to `CMSG_UNLEARN_SKILL` and sets `skillId` to 0.
*   **Deserialization**: The `ReadFromWorldPacket` method extracts a single 32-bit unsigned integer representing the `skillId`.

### Talent Reset Confirmation
**`TalentWipeConfirm`** represents the `MSG_TALENT_WIPE_CONFIRM` opcode. This packet is typically sent by the client in response to a server request to confirm that the player wants to reset their talents (often involving a cost or penalty).
*   **Construction**: Initializes the packet type to `MSG_TALENT_WIPE_CONFIRM`.
*   **Deserialization**: The `ReadFromWorldPacket` method extracts an `ObjectGuid` from the incoming buffer. This GUID likely identifies the player or the specific transaction/request being confirmed.

## Cross-Unit Boundaries

All three classes rely on the `ByteBuffer` infrastructure for deserialization. Specifically:
*   **`LearnTalent::ReadFromWorldPacket`** and **`UnlearnSkill::ReadFromWorldPacket`** call `ByteBuffer/operator>>#9` (the overload for extracting primitive types like `uint32`) to parse integer fields.
*   **`TalentWipeConfirm::ReadFromWorldPacket`** calls `ObjectGuid/operator>>` to parse the complex GUID structure from the byte stream.

These methods are not called by any other units in the provided map; they are leaf nodes in the call graph, invoked by the central packet dispatching system (not shown in this unit) when the corresponding opcode is detected on the wire.

## Data Model

This unit does not interact with any database tables. All data is transient, existing only within the scope of the network packet processing.

## Notable Implementation Details

*   **Minimal Validation**: The deserialization methods perform no validation. They blindly extract bytes assuming the client sent a well-formed packet. If the packet is truncated, undefined behavior may occur depending on the `operator>>` implementations in `ByteBuffer` and `ObjectGuid`.
*   **Default Initialization**: All constructors explicitly initialize member variables to 0 or default states. This ensures that even if `ReadFromWorldPacket` fails or is not called, the object remains in a known state.
*   **Namespace Structure**: The classes are nested under `WorldPackets::Skill`, providing a clean separation of concerns for different packet categories.

## Member Reference

**ReadFromWorldPacket** (in `LearnTalent`): Deserializes `talent_id` and `requested_rank` as `uint32` values from the `WorldPacket` buffer using `ByteBuffer/operator>>#9`.

**ReadFromWorldPacket#3** (in `UnlearnSkill`): Deserializes `skillId` as a `uint32` value from the `WorldPacket` buffer using `ByteBuffer/operator>>#9`.

**ReadFromWorldPacket#2** (in `TalentWipeConfirm`): Deserializes `guid` as an `ObjectGuid` from the `WorldPacket` buffer using `ObjectGuid/operator>>`.

**LearnTalent**: Constructor for the `LearnTalent` packet class. Sets the opcode to `CMSG_LEARN_TALENT` and initializes `talent_id` and `requested_rank` to 0.

---

<!-- machine-true, projected from graph.json -->

## Map — Skill

*Source:* Skill.cpp, Skill.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ReadFromWorldPacket | method | ByteBuffer/operator>>#9 | — | — |
| ReadFromWorldPacket#3 | method | ByteBuffer/operator>>#9 | — | — |
| ReadFromWorldPacket#2 | method | ObjectGuid/operator>> | — | — |
| LearnTalent | ctor | — | — | — |
