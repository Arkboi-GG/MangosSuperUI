# MapSessionFilter

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# MapSessionFilter

**Purpose & Responsibilities**

`MapSessionFilter` is a specialized packet processing filter used within the `wowvmangos` server architecture to enforce thread-safety constraints during the game world update loop. It inherits from `PacketFilter` and configures itself to allow only packets marked as safe for concurrent processing on the map update thread (`PACKET_PROCESS_MAP`). Crucially, it explicitly disables the processing of logout-related packets (`m_processLogout = false`) during this phase, ensuring that session teardown logic—which likely involves complex state changes or database interactions—is deferred to the main world thread (`WorldSessionFilter`) to prevent race conditions.

## Member-by-Member Behavior

### Construction and Destruction

*   **`MapSessionFilter` (Constructor)**
    Initializes the filter for use in the map update context. It calls the base `PacketFilter` constructor with the associated `WorldSession`. It then sets two critical configuration flags:
    1.  `m_processLogout` is set to `false`, preventing logout packets from being handled during the map update cycle.
    2.  `m_processType` is set to `PACKET_PROCESS_MAP`, indicating that only packets categorized as map-safe should pass through the filter's `Process` method.

*   **`~MapSessionFilter` (Destructor)**
    A trivial destructor that performs no custom cleanup, relying on the base class destructor.

## Cross-Unit Boundaries

*   **Called by `Map.Main/ProcessSessionPackets`**: The map update logic uses this filter to iterate over pending packets for a session. By using `MapSessionFilter`, the map thread ensures it only processes movement and other high-frequency, thread-safe updates, leaving heavy operations like logout to the world thread.
*   **Called by `Map.Main/Update#3`**: Similar to above, this indicates the filter is applied during specific phases of the map's update loop to gate packet processing.
*   **Called by `World/ProcessAsyncPackets`**: This suggests that async packet processing paths may also utilize this filter to ensure consistency in how map-safe packets are identified and handled, regardless of the originating thread context.

## Data Model

This unit does not interact directly with any database tables. It operates purely on in-memory packet structures and session state flags.

## Notable Implementation Details

*   **Thread Safety Enforcement**: The primary design goal is to partition packet processing based on thread safety. By setting `m_processType` to `PACKET_PROCESS_MAP`, the filter relies on the underlying `PacketFilter::Process` implementation (defined in the base class or elsewhere) to check the packet's type against this allowed category.
*   **Logout Deferral**: Setting `m_processLogout` to `false` is a deliberate architectural choice. Logout procedures often involve saving character data, removing the player from groups/guilds, and updating online status. These operations are likely not thread-safe with respect to the map update loop, so they are excluded from this filter's scope.

## Member Reference

**MapSessionFilter**
Constructor that initializes the filter for map-thread packet processing. Sets `m_processLogout` to `false` and `m_processType` to `PACKET_PROCESS_MAP` via the base `PacketFilter` constructor.

**~MapSessionFilter**
Trivial destructor that overrides the base class destructor. Performs no additional cleanup.

---

<!-- machine-true, projected from graph.json -->

## Map — MapSessionFilter

*Source:* WorldSession.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| MapSessionFilter | ctor | — | Map.Main/ProcessSessionPackets, Map.Main/Update#3, World/ProcessAsyncPackets | — |
| ~MapSessionFilter | dtor | — | — | — |
