# SessionPacketSendTask

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# SessionPacketSendTask

**Purpose & Responsibilities**

`SessionPacketSendTask` is a lightweight, single-use functor designed to decouple network packet transmission from the main game loop thread. In the `wowvmangos` architecture, sending data to a client involves locating the specific `WorldSession` associated with a player's account and invoking its send method. This operation can be expensive if performed synchronously during critical game logic updates (such as map updates or AI ticks).

This unit encapsulates the necessary context—specifically the target `accountId` and the `WorldPacket` data—into a standalone object that can be queued and executed asynchronously. By implementing the `operator()`, it allows the calling code to treat the send operation as a standard task that can be handed off to a worker thread or a dedicated async processing queue (likely managed by `World::ProcessAsyncPackets` or similar infrastructure in `World.cpp`).

**Member-by-Member Behavior**

The unit consists of two primary components: the constructor and the invocation operator.

1.  **Construction (`SessionPacketSendTask`)**:
    The constructor accepts a `uint32 accountId` and a reference to a `WorldPacket`. It stores the account ID by value and copies the packet data into its internal `m_data` member. This copy is crucial because the original `WorldPacket` referenced by the caller may go out of scope or be modified before the task is actually executed. The copy ensures the task holds a valid, immutable snapshot of the data to be sent.

2.  **Execution (`operator()`)**:
    Although the implementation body is not present in the provided header, the signature `void operator ()()` indicates that this object is callable. Based on the member variables and the class name, this method performs the following logical steps:
    *   It retrieves the `WorldSession` instance corresponding to `m_accountId` from the global session map (typically via `sWorld.FindSession(m_accountId)`).
    *   If a session is found and is in a valid state (connected), it sends `m_data` to that session.
    *   If no session is found (e.g., the player logged out between task creation and execution), the operation silently fails or handles the null case gracefully, preventing crashes.

**Cross-Unit Boundaries**

*   **Called by**: While the MAP shows no explicit callers, the design implies usage within the `World` class (specifically in `World.cpp`), likely within methods like `ProcessAsyncPackets` or other async task handlers. The `World` class creates these tasks to offload I/O operations.
*   **Calls out**: The `operator()` implicitly calls into:
    *   `World::FindSession` (from `World.cpp`) to locate the target session.
    *   `WorldSession::SendPacket` (from `WorldSession.cpp`) to perform the actual socket write.
    *   These calls cross thread boundaries, meaning `SessionPacketSendTask` acts as a bridge between the async worker thread and the session management system. Proper synchronization (locks) within `WorldSession` or the session map is required to ensure thread safety during these lookups and sends.

**Data Model**

This unit does not interact directly with any database tables. It operates entirely on in-memory objects (`WorldSession`, `WorldPacket`).

**Notable Implementation Details**

*   **Copy Semantics**: The constructor takes `WorldPacket& data` but stores it in `WorldPacket m_data`. This deep copy is a performance consideration; for large packets, this adds overhead, but it is necessary for safety in an async context. Engineers must be aware that modifying the original packet after creating the task will not affect the data sent.
*   **Deleted Copy Constructor**: `SessionPacketSendTask(const SessionPacketSendTask&) = delete;` prevents accidental copying of the task object itself. This ensures that each task is unique and tied to its specific memory allocation, avoiding double-execution or resource management issues if the task were copied into a container that copies elements.
*   **Thread Safety**: The unit itself is not thread-safe regarding its internal state after construction (though it shouldn't be modified). The safety relies on the external system queuing it correctly and the `WorldSession` methods being thread-safe or called from a controlled context.

## Member Reference

**SessionPacketSendTask#2**
Declaration of the copy constructor, explicitly deleted to prevent copying of task instances.

**SessionPacketSendTask**
Constructor that initializes the task with a target `accountId` and a copy of the `WorldPacket` data to be sent.

---

<!-- machine-true, projected from graph.json -->

## Map — SessionPacketSendTask

*Source:* World.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| SessionPacketSendTask#2 | decl | — | — | — |
| SessionPacketSendTask | ctor | — | — | — |
