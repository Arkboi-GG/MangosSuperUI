# MessageDelivererExcept

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# MessageDelivererExcept

`MessageDelivererExcept` is a visitor struct within the `MaNGOS` namespace, defined in `GridNotifiers.h`. It serves as a specialized functor for iterating over the grid-based spatial partitioning system (specifically `CameraMapType`) to deliver a network message (`WorldPacket`) to all visible players, with the explicit exception of one specific `Player`.

It is part of a family of notifier structs (`MessageDeliverer`, `ObjectMessageDeliverer`, etc.) that implement the Visitor pattern to traverse the game world's object containers. Unlike `MessageDeliverer`, which sends messages to a set including or excluding the sender based on a boolean flag, `MessageDelivererExcept` is designed for scenarios where a specific recipient must be excluded from a broadcast, regardless of whether they are the originator of the action.

## Purpose & Responsibilities

The primary responsibility of `MessageDelivererExcept` is to facilitate targeted network communication in a multiplayer environment. It allows the server to send a packet to all players currently viewing a specific area (represented by `CameraMapType`) while skipping a designated player.

This is commonly used in scenarios such as:
- Sending a visual effect or chat message to everyone *except* the caster (who might already have local feedback).
- Broadcasting an event to nearby players while hiding it from a specific stealthed or invisible entity.
- Implementing "whisper" or "party-only" logic where the global broadcast needs to exclude one participant.

## Member-by-Member Behavior

### **MessageDelivererExcept** (Constructor)

*   **Kind:** Constructor
*   **Signature:** `MessageDelivererExcept(WorldPacket* msg, Player const* skipped)`
*   **Behavior:**
    Initializes the struct with two critical pieces of data:
    1.  `i_message`: A pointer to the `WorldPacket` containing the binary data to be sent to clients.
    2.  `i_skipped_receiver`: A pointer to the `Player` object who must **not** receive this packet.

    The constructor stores these references directly. It does not perform validation (e.g., checking if `msg` is null or if `skipped` is valid), assuming the caller ensures validity.

## Cross-Unit Boundaries

### Called By: `WorldObject.Object/SendMessageToSetExcept`

*   **Direction:** Inbound (Other units call this unit)
*   **Collaboration:**
    The `WorldObject` class (specifically the `SendMessageToSetExcept` method) creates an instance of `MessageDelivererExcept` to handle the distribution of a packet.
    1.  `WorldObject` prepares the `WorldPacket`.
    2.  `WorldObject` identifies the `Player` to exclude.
    3.  `WorldObject` instantiates `MessageDelivererExcept` with the packet and the excluded player.
    4.  `WorldObject` passes this instance to the grid traversal system (likely via a `Visit` call on the relevant map container).
    5.  The grid system invokes the `Visit(CameraMapType&)` method of `MessageDelivererExcept` (defined elsewhere, likely in `GridNotifiers.cpp` or similar implementation file, though the declaration is here).

*   **Why:** This separation allows the high-level `WorldObject` logic to remain decoupled from the low-level iteration mechanics of the grid/camera system. The `MessageDelivererExcept` encapsulates the "what" (send this packet) and the "constraint" (skip this player), while the grid system handles the "how" (iterating through visible cameras).

### Calls Out: None

*   The `MessageDelivererExcept` struct itself, as defined in this header, does not contain any methods that call out to other units. Its `Visit` methods are declared but not defined in this header. The actual logic for sending the packet (which would involve calling `Player::SendDirectMessage` or similar) resides in the implementation of the `Visit` methods, which are not part of this specific translation unit's definition. However, based on the pattern of `MessageDeliverer`, the `Visit` implementation will likely iterate through the `CameraMapType` and invoke methods on `Player` objects.

## Data Model

This unit does not interact with any database tables. It operates entirely on in-memory objects (`WorldPacket`, `Player`, `Camera`).

## Notable Implementation Details

1.  **Visitor Pattern Integration:**
    `MessageDelivererExcept` is designed to work with the MaNGOS grid system's visitor interface. It provides two `Visit` overloads:
    -   `void Visit(CameraMapType& m)`: This is the active method. It iterates over the cameras (representing player viewpoints) in the specified map type.
    -   `template<class SKIP> void Visit(GridRefManager<SKIP>&) {}`: This is a no-op template specialization. It ensures that when the visitor is applied to other types of grid managers (e.g., `CreatureMapType`, `GameObjectMapType`), nothing happens. This restricts the message delivery strictly to players (via their cameras).

2.  **Exclusion Logic:**
    The exclusion logic is not visible in the header file because the `Visit` method body is not defined here. However, the presence of `i_skipped_receiver` implies that the implementation of `Visit(CameraMapType&)` will compare each player's GUID or pointer against `i_skipped_receiver` before sending the packet.

3.  **Const Correctness:**
    -   `i_message` is a non-const pointer to `WorldPacket`. This allows the packet to be modified if necessary during transmission (though typically packets are sent as-is).
    -   `i_skipped_receiver` is a const pointer to `Player`. This ensures the struct cannot accidentally modify the excluded player's state.

4.  **Memory Management:**
    The struct holds raw pointers. It does not take ownership of the `WorldPacket` or the `Player`. The caller is responsible for ensuring these objects remain valid for the duration of the visit operation. This is typical for short-lived visitor functors in MaNGOS.

5.  **Comparison with `MessageDeliverer`:**
    -   `MessageDeliverer` takes a `Player const& i_player` (the sender) and a `bool i_toSelf`. It decides whether to include the sender based on the boolean.
    -   `MessageDelivererExcept` takes a `Player const* i_skipped_receiver`. It explicitly excludes one specific player, regardless of who the sender is. This offers more flexibility for complex exclusion rules (e.g., exclude a player who is not the sender).

## Member Reference

**MessageDelivererExcept**
Constructor that initializes the struct with a `WorldPacket*` to be delivered and a `Player const*` to be excluded from receiving it. Used by `WorldObject.Object/SendMessageToSetExcept` to broadcast messages to all visible players except one.

---

<!-- machine-true, projected from graph.json -->

## Map — MessageDelivererExcept

*Source:* GridNotifiers.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| MessageDelivererExcept | ctor | — | WorldObject.Object/SendMessageToSetExcept | — |
