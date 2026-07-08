<!-- provenance: verbose -->
# PlayerBroadcaster

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# PlayerBroadcaster

`PlayerBroadcaster` manages the outbound network packet queue and listener visibility graph for a single `Player`. It buffers `WorldPacket`s to batch network I/O and maintains a map of other players (`m_listeners`) who must receive these packets. This allows the server to broadcast movement or state updates to nearby clients efficiently. The class is thread-safe, using mutexes to protect the queue and listener map from concurrent access by game logic threads and the network processing thread.

## Member-by-Member Behavior

### Lifecycle and Configuration

**`PlayerBroadcaster` (Constructor)**
Initializes the broadcaster with a `WorldSocket`, the owner’s `ObjectGuid` (`m_self`), and a maximum queue size (`MAX_QUEUE_SIZE`, default 500). It reserves memory for `m_queue` and increments the static creation counter `num_bcaster_created`.

**`~PlayerBroadcaster` (Destructor)**
Nullifies `m_socket` and increments the static deletion counter `num_bcaster_deleted`. It does not clear the queue or listeners; `FreeAtLogout` must be called prior to destruction to ensure clean state.

**`FreeAtLogout`**
Prepares the broadcaster for player logout by setting `m_socket` to `nullptr` and clearing both `m_queue` and `m_listeners` under lock. Called by `Player.Main/DeletePacketBroadcaster`.

**`ChangeSocket`**
Replaces the internal `m_socket` with a new `WorldSocket`. Called by `WorldSession.CharacterHandler/HandlePlayerLogin` during login or reconnection.

**`SetInstanceId`**
Sets the `instanceId` member. Called by `Map.Main/Add#3` to tag the broadcaster with its map instance.

**`GetGUID`**
Returns `m_self`, the GUID of the owning player. Called by `MovementBroadcaster` to identify the player.

### Listener Management

Listeners are other players who need to receive packets from this broadcaster (e.g., nearby players seeing movement).

**`AddListener`**
Adds a `Player` to `m_listeners` keyed by their GUID, storing a `shared_ptr` to their broadcaster. It asserts the player is valid and returns early if the player is the broadcaster’s owner (`m_self`), preventing self-listening. Protected by `m_listeners_lock`. Called by `Player.Main` methods handling visibility changes.

**`RemoveListener`**
Removes a `Player` from `m_listeners` by GUID. Protected by `m_listeners_lock`. Called by `Player.Main`, `GridNotifiers`, and `Map.Main` when visibility ends or players are removed.

**`ClearListeners`**
Empties `m_listeners` under lock.

### Packet Queuing and Dispatch

**`QueuePacket`**
Adds a `WorldPacket` to `m_queue` with flags for `sendToSelf` and an exclusion GUID (`except`). If the queue is full (`>= MAX_QUEUE_SIZE`), it checks if both the last queued packet and the new packet are "skippable" via `CanSkipPacket`. If so, it replaces the last packet with the new one to preserve the latest state without growing the queue. Otherwise, it appends the packet. Protected by `m_queue_lock`. Called by `WorldObject.Object/SendMovementMessageToSet`.

**`CanSkipPacket`**
Static helper returning `true` if an opcode represents a transient movement update that can be dropped when the queue is full. Skippable opcodes are those `< MSG_MOVE_SET_RUN_SPEED_CHEAT` OR (`> MSG_MOVE_SET_TURN_RATE` AND `!= MSG_MOVE_HEARTBEAT`). This preserves heartbeats and non-movement packets while allowing redundant movement updates to be overwritten.

**`ProcessQueue`**
Drains `m_queue` and sends packets to recipients. It acquires both `m_queue_lock` and `m_listeners_lock`, moves the queue to a local variable, and unlocks the queue lock to allow new packets to be queued. It calculates `lastUpdatePackets` (queue size × listener count) and adds it to the `num_packets` reference. For each packet, it sends to self (if `sendToSelf` and not excluded) and iterates `m_listeners`, sending to each listener’s broadcaster unless their GUID matches `except`. Called by `MovementBroadcaster/BroadcastPackets`.

**`SendPacket`**
Sends a `WorldPacket` to `m_socket` if valid. Used internally by `ProcessQueue`.

## Cross-Unit Boundaries

*   **`WorldSession.CharacterHandler/HandlePlayerLogin` → `ChangeSocket`**: Updates the socket on login.
*   **`Player.Main` → `AddListener` / `RemoveListener`**: Manages visibility listeners.
*   **`GridNotifiers/Notify` → `RemoveListener`**: Removes listeners on grid events.
*   **`Map.Main` → `SetInstanceId`**: Sets instance ID.
*   **`WorldObject.Object/SendMovementMessageToSet` → `QueuePacket`**: Queues movement packets.
*   **`MovementBroadcaster/BroadcastPackets` → `ProcessQueue`**: Flushes the queue.
*   **`MovementBroadcaster` → `GetGUID`**: Identifies the player.
*   **`Player.Main/DeletePacketBroadcaster` → `FreeAtLogout`**: Cleans up on logout.

## Data Model

No database tables are touched.

## Notable Implementation Details

*   **Queue Replacement:** `QueuePacket` replaces the tail packet if the queue is full and both packets are skippable movement updates, optimizing bandwidth for high-frequency movement data.
*   **Locking Strategy:** `ProcessQueue` holds `m_listeners_lock` while sending to all listeners, which can cause contention if many players are listening. It releases `m_queue_lock` early to allow concurrent queuing.
*   **Self-Exclusion:** `AddListener` prevents a player from listening to themselves, avoiding redundant sends.

## Member Reference

**`PlayerBroadcaster`**: Constructor initializing socket, self-GUID, and max queue size; reserves queue memory and increments creation counter.

**`ChangeSocket`**: Replaces the internal `WorldSocket` pointer.

**`AddListener`**: Adds a player’s broadcaster to `m_listeners` if not self, protected by `m_listeners_lock`.

**`RemoveListener`**: Removes a player’s broadcaster from `m_listeners` by GUID, protected by `m_listeners_lock`.

**`CanSkipPacket`**: Static helper returning true if an opcode is a skippable transient movement update.

**`ClearListeners`**: Empties `m_listeners` under lock.

**`SendPacket`**: Sends a packet to `m_socket` if valid.

**`ProcessQueue`**: Drains `m_queue`, sends packets to self and listeners (excluding specified GUIDs), and updates packet count statistics.

**`SetInstanceId`**: Sets the `instanceId` member.

**`QueuePacket`**: Adds a packet to `m_queue`, replacing the last packet if full and both are skippable.

**`GetGUID`**: Returns the owner’s `ObjectGuid`.

**`FreeAtLogout`**: Nullifies socket and clears queue and listeners under lock.

**`~PlayerBroadcaster`**: Destructor nullifying socket and incrementing deletion counter.

---

<!-- machine-true, projected from graph.json -->

## Map — PlayerBroadcaster

*Source:* PlayerBroadcaster.cpp, PlayerBroadcaster.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| PlayerBroadcaster | ctor | — | — | — |
| ChangeSocket | method | — | WorldSession.CharacterHandler/HandlePlayerLogin | — |
| AddListener | method | Errors/PrintStacktraceAndThrow, Object/GetObjectGuid, ObjectGuid/operator== | Player.Main/AddBroadcastListener, Player.Main/HandleStealthedUnitsDetection, Player.Main/UpdateVisibilityOf | — |
| RemoveListener | method | Errors/PrintStacktraceAndThrow, Object/GetObjectGuid | GridNotifiers/Notify, Map.Main/ExistingPlayerLogin, Map.Main/Remove#3, Player.Main/HandleStealthedUnitsDetection, Player.Main/RemoveBroadcastListener, Player.Main/SendDestroyGroupMembers, Player.Main/UpdateVisibilityOf, WorldObject.Object/DestroyForNearbyPlayers | — |
| CanSkipPacket | method | — | — | — |
| ClearListeners | method | — | — | — |
| SendPacket | method | WorldPacket/WorldPacket#3, WorldSocket/SendPacket | — | — |
| ProcessQueue | method | ObjectGuid/operator!=, ObjectGuid/operator== | MovementBroadcaster/BroadcastPackets | — |
| SetInstanceId | method | — | Map.Main/Add#3 | — |
| QueuePacket | method | WorldPacket/GetOpcode, WorldPacket/operator= | WorldObject.Object/SendMovementMessageToSet | — |
| GetGUID | method | — | MovementBroadcaster/RegisterPlayer, MovementBroadcaster/RemovePlayer | — |
| FreeAtLogout | method | — | Player.Main/DeletePacketBroadcaster | — |
| ~PlayerBroadcaster | dtor | — | — | — |
