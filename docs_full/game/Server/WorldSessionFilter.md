# WorldSessionFilter

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# WorldSessionFilter

**Purpose & Responsibilities**

`WorldSessionFilter` is a specialized packet processing filter used within the `wowvmangos` server architecture to manage incoming client network traffic during the main world update loop. It inherits from `PacketFilter`, establishing a contract for determining whether specific network packets should be processed by the `WorldSession`.

Its primary responsibility is to **enable the processing of logout-related packets** while restricting general packet processing to the "World" context (`PACKET_PROCESS_WORLD`). This distingu it from `MapSessionFilter`, which is used during map updates and explicitly disables logout processing to ensure thread safety and logical separation between map-level updates and world-level session management.

It is instantiated and utilized by the `World` unit's `UpdateSessions` routine to safely drain and process packets queued for a player session during the global world tick.

## Member-by-Member Behavior

### Construction and Destruction

*   **`WorldSessionFilter`**: The constructor initializes the filter for a specific `WorldSession`. It sets two critical internal flags inherited from `PacketFilter`:
    1.  `m_processLogout` is set to `true`. This allows the session to process logout requests and related state changes during this phase.
    2.  `m_processType` is set to `PACKET_PROCESS_WORLD`. This categorizes the packets handled by this filter instance, ensuring they are drawn from the correct queue segment associated with world-level operations.
*   **`~WorldSessionFilter`**: The destructor is virtual and empty. It ensures proper cleanup of the object hierarchy when the filter goes out of scope after the `UpdateSessions` pass completes.

## Cross-Unit Boundaries

*   **Called by `World/UpdateSessions`**:
    *   **Direction**: `World` creates an instance of `WorldSessionFilter` and passes it to the `WorldSession::Update` method (or similar processing loop).
    *   **Collaboration**: The `World` unit orchestrates the global game loop. During the session update phase, it needs to process packets that affect the player's connection state (like logging out) but must do so in a controlled manner distinct from map-specific updates. By using `WorldSessionFilter`, `World` signals to the `WorldSession` that it is safe to process logout logic and world-context packets. The filter acts as a policy object passed into the session's processing logic.

## Data Model

This unit does not interact directly with any database tables. It operates entirely on in-memory network packet queues and session state flags.

## Notable Implementation Details

*   **Thread Safety Context**: The distinction between `WorldSessionFilter` and `MapSessionFilter` is crucial for the server's multithreaded architecture. `MapSessionFilter` sets `m_processLogout = false` because logout logic often involves saving player data and modifying global session maps, which are not safe to perform during concurrent map updates. `WorldSessionFilter` enables this by setting `m_processLogout = true`, allowing the main world thread to handle these heavier, non-map-specific operations.
*   **Packet Processing Type**: By setting `m_processType` to `PACKET_PROCESS_WORLD`, this filter ensures that only packets tagged for world-level processing are considered. This prevents accidental processing of map-specific movement or interaction packets that should have been handled during the map update phase.

## Member Reference

**WorldSessionFilter**
Constructor that initializes the filter for a `WorldSession`. It configures the parent `PacketFilter` to allow logout processing (`m_processLogout = true`) and restricts the processing scope to world-level packets (`m_processType = PACKET_PROCESS_WORLD`). It is instantiated by `World/UpdateSessions` to handle session updates during the main world loop.

**~WorldSessionFilter**
Virtual destructor. Performs no custom cleanup actions beyond invoking the base class destructor.

---

<!-- machine-true, projected from graph.json -->

## Map — WorldSessionFilter

*Source:* WorldSession.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| WorldSessionFilter | ctor | — | World/UpdateSessions | — |
| ~WorldSessionFilter | dtor | — | — | — |
