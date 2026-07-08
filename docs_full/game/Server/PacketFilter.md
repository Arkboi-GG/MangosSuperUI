# PacketFilter

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# PacketFilter

## Purpose & Responsibilities

`PacketFilter` is a lightweight base class within the `wowvmangos` codebase designed to manage the state and policy of incoming network packet processing for a specific `WorldSession`. It acts as a gatekeeper, determining whether a `WorldSession` is currently allowed to process certain types of packets (e.g., movement vs. world-state changes) and whether it is in a special state such as logging out.

The class itself contains minimal logic; its primary role is to define an interface (`Process`, `PacketProcessType`, `SetProcessType`) and hold state variables (`m_processLogout`, `m_processType`) that derived classes specialize. It enables the server to separate packet processing contexts—specifically distinguishing between packets that can be processed safely on the map update thread versus those that require the main world thread—by providing a polymorphic handle that the session’s update loop can query.

## Member-by-Member Behavior

### Construction and Destruction

*   **`PacketFilter`**: The constructor initializes the filter with a pointer to the owning `WorldSession`. It sets the internal `m_processLogout` flag to `false` and initializes `m_processType` to `PACKET_PROCESS_MAX_TYPE`. This default type likely represents an invalid or catch-all state, ensuring that derived classes must explicitly set their intended processing mode.
*   **`~PacketFilter`**: A virtual destructor allowing proper cleanup of derived instances.

### State Inspection and Modification

*   **`Process`**: A virtual method intended to be overridden by derived classes. In the base `PacketFilter`, it accepts a `std::unique_ptr<ClientPacket const>` and unconditionally returns `true`. This indicates that the base class imposes no filtering logic; it allows all packets through by default. Derived classes like `MapSessionFilter` override this to implement actual filtering rules.
*   **`ProcessLogout`**: An inline accessor returning the boolean `m_processLogout`. This flag indicates whether the session is currently undergoing a logout procedure. Callers use this to determine if they should ignore non-critical packets or prioritize logout-related cleanup.
*   **`PacketProcessType`**: An inline accessor returning the current `PacketProcessing` enum value stored in `m_processType`. This tells the caller which category of packets this filter is currently configured to handle (e.g., map-safe vs. world-thread-only).
*   **`SetProcessType`**: An inline mutator that updates `m_processType`. This allows external systems (such as the Map or World update loops) to dynamically change the filtering context if necessary, though typically this is set during construction in the derived classes.

## Cross-Unit Boundaries

`PacketFilter` interacts primarily with `WorldSession` and the broader packet processing infrastructure.

*   **Called by `WorldSession.Main/Update`**: The `WorldSession` class uses `PacketFilter` instances during its update cycle. Specifically, `ProcessLogout` is called to check if the session is logging out, which affects how packets are queued or discarded.
*   **Called by `WorldSession.Main/ProcessPackets`**: The `PacketProcessType` method is queried to determine the current processing context. This helps `WorldSession` decide which queue of packets to drain or how to validate incoming data.
*   **Called by `Map.Main/ProcessSessionPackets` and `World/ProcessAsyncPackets`**: These external units call `SetProcessType` to configure the filter before passing it to the session’s processing logic. This establishes the contract that the filter is being used in a specific threading context (Map thread vs. World thread).

## Data Model

`PacketFilter` does not interact with any database tables. It operates entirely on in-memory state related to the active network session.

## Notable Implementation Details

*   **Virtual Base for Strategy Pattern**: `PacketFilter` implements a simple strategy pattern. By defining `Process` as virtual, it allows `MapSessionFilter` and `WorldSessionFilter` (defined in the same header) to inject specific filtering logic without modifying the core `WorldSession` packet handling loop.
*   **Default Permissive Behavior**: The base `Process` method always returns `true`. This is a safety default; if a derived class fails to override `Process`, packets will still be processed, preventing accidental deadlocks or dropped inputs due to missing overrides. However, the derived classes shown in the header (`MapSessionFilter`, `WorldSessionFilter`) do override this behavior.
*   **Thread-Safety Implications**: The existence of distinct filters for "Map" and "World" processing suggests that `wowvmangos` employs a multi-threaded architecture where packet processing is split between threads to improve performance. `PacketFilter` provides the mechanism to ensure that only thread-safe operations are performed on the Map thread, while potentially unsafe operations are deferred to the World thread.
*   **Logout State Tracking**: The `m_processLogout` flag is critical for clean session termination. By exposing this via `ProcessLogout`, the system ensures that no new gameplay actions are processed while the character is being saved and removed from the world state.

## Member Reference

**PacketFilter**
Constructor that initializes the filter with a `WorldSession` pointer, sets `m_processLogout` to `false`, and `m_processType` to `PACKET_PROCESS_MAX_TYPE`.

**~PacketFilter**
Virtual destructor for the base class.

**Process**
Virtual method that takes a `std::unique_ptr<ClientPacket const>` and returns `bool`. In the base class, it always returns `true`, indicating no filtering. Derived classes override this to implement specific packet acceptance logic.

**ProcessLogout**
Inline getter that returns the `m_processLogout` boolean flag, indicating if the session is currently logging out.

**PacketProcessType**
Inline getter that returns the current `PacketProcessing` enum value stored in `m_processType`.

**SetProcessType**
Inline setter that updates the `m_processType` member variable, allowing the caller to change the filtering context.

---

<!-- machine-true, projected from graph.json -->

## Map — PacketFilter

*Source:* WorldSession.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| PacketFilter | ctor | — | — | — |
| ~PacketFilter | dtor | — | — | — |
| Process | method | — | — | — |
| ProcessLogout | method | — | WorldSession.Main/Update | — |
| PacketProcessType | method | — | WorldSession.Main/ProcessPackets | — |
| SetProcessType | method | — | Map.Main/ProcessSessionPackets, World/ProcessAsyncPackets | — |
