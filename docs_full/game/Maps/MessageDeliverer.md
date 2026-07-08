# MessageDeliverer

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# MessageDeliverer

**Purpose & Responsibilities**

`MessageDeliverer` is a lightweight functor (visitor) struct within the `MaNGOS` namespace, defined in `GridNotifiers.h`. Its sole responsibility is to deliver a specific network packet (`WorldPacket`) to all players currently viewing a particular area of the game world, represented by a `CameraMapType`.

It implements the Visitor pattern required by the MaNGOS grid system. When the grid system iterates over visible entities, it calls the `Visit` method on registered notifiers. `MessageDeliverer` uses this callback to send data to clients. It supports two modes of delivery:
1.  **Broadcast:** Sending the message to all viewers in the camera's view.
2.  **Self-Delivery:** Optionally including the originating player (`i_player`) in the recipient list, controlled by the `i_toSelf` flag.

This unit does not perform any database operations, nor does it contain complex business logic; it acts strictly as a bridge between the spatial grid system and the network layer for targeted message broadcasting.

## Member-by-Member Behavior

### Construction
The constructor initializes the functor with the necessary context for delivery:
*   `i_player`: A constant reference to the `Player` who originated the message or is the center of the broadcast.
*   `i_message`: A pointer to the `WorldPacket` containing the data to be sent.
*   `i_toSelf`: A boolean flag indicating whether the `i_player` should also receive the packet.

### Visiting Cameras
The core behavior resides in the `Visit(CameraMapType& m)` method. Although the implementation of this method is not explicitly shown in the provided header snippet (it is likely defined in a corresponding `.cpp` file or implemented inline elsewhere in the full build), the signature and member variables define its contract:
1.  It accepts a `CameraMapType`, which represents a collection of cameras (viewpoints) associated with a specific grid cell or area.
2.  It iterates through these cameras.
3.  For each camera, it identifies the owning `Player`.
4.  It checks if the owner is the `i_player` and respects the `i_toSelf` flag.
5.  It sends `i_message` to the eligible players.

The templated `Visit(GridRefManager<SKIP>&)` method is a no-op (empty body), ensuring that `MessageDeliverer` ignores non-camera entities (like creatures or game objects) during grid traversal. This optimization ensures the functor only processes relevant viewer entities.

## Cross-Unit Boundaries

### Called By: `Map.Main` / `MessageBroadcast`
*   **Direction:** Inbound (Other units call `MessageDeliverer`).
*   **Collaboration:** The `Map` class (specifically its main broadcast mechanisms) instantiates `MessageDeliverer` when it needs to send a packet to all players in a specific area. The `Map` unit handles the spatial indexing and grid iteration, passing the relevant `CameraMapType` to the `MessageDeliverer`'s `Visit` method. This decouples the spatial lookup logic from the network sending logic.

### Calls Out: None
*   **Observation:** The MAP indicates no outbound calls to other units. However, logically, the `Visit` method must interact with the `Player` class (to send the packet) and potentially the `Camera` class (to retrieve the owner). These interactions are internal to the MaNGOS core object model and are not listed as cross-unit dependencies in the provided MAP, likely because they are considered part of the same logical subsystem or are handled via direct member access rather than distinct service calls.

## Data Model

This unit does not interact with any database tables. It operates entirely on in-memory objects (`Player`, `WorldPacket`, `Camera`).

## Notable Implementation Details

1.  **Visitor Pattern Optimization:** The struct uses a templated `Visit` method with an empty implementation for `GridRefManager<SKIP>`. This allows the grid traversal engine to call `Visit` on all entity types uniformly, while `MessageDeliverer` efficiently ignores irrelevant types (creatures, game objects, etc.) at compile time, reducing runtime overhead.
2.  **Self-Delivery Control:** The `i_toSelf` flag is critical for correctness in many game mechanics. For example, if a player casts a spell that triggers a visual effect visible to others, the server might need to send the effect packet to everyone *except* the caster (who already knows they cast it), or *including* the caster if the client needs confirmation. This flag provides that flexibility.
3.  **Pointer Ownership:** The `i_message` is stored as a raw pointer (`WorldPacket*`). The caller is responsible for ensuring the packet remains valid during the duration of the grid visit. The `MessageDeliverer` does not take ownership or copy the packet, prioritizing performance for high-frequency broadcasts.
4.  **Const Correctness:** The `i_player` is held as a `const&`, preventing accidental modification of the originating player's state during the broadcast process.

## Member Reference

**MessageDeliverer**
Constructor that initializes the functor with a reference to the originating `Player`, a pointer to the `WorldPacket` to be delivered, and a boolean flag (`i_toSelf`) determining if the originator should also receive the message. It sets up the context for the subsequent `Visit` operation.

---

<!-- machine-true, projected from graph.json -->

## Map — MessageDeliverer

*Source:* GridNotifiers.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| MessageDeliverer | ctor | — | Map.Main/MessageBroadcast | — |
