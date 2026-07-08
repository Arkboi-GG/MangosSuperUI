# DungeonResetEvent

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# DungeonResetEvent

**Purpose & Responsibilities**

`DungeonResetEvent` is a lightweight aggregate struct defined in `MapPersistentStateMgr.h` that encapsulates the metadata required to schedule and identify a specific dungeon or raid reset operation within the server. It serves as the value type for the reset scheduling system managed by `DungeonResetScheduler`.

The struct distinguishes between two primary modes of operation via its `type` field:
1.  **Global/Map-level Resets:** Triggered by specific days/times (e.g., Tuesday raids), affecting all instances of a given `mapId`. In this mode, `instanceId` is typically 0.
2.  **Instance-specific Resets:** Triggered by individual instance expiration (e.g., a normal dungeon resetting after 72 hours of inactivity). In this mode, `instanceId` identifies the specific instance.

It provides equality comparison (`operator==`) to allow the scheduling system to deduplicate or locate existing scheduled events for a specific map/instance combination.

## Member-by-Member Behavior

### Constructors

*   **`DungeonResetEvent()`**: The default constructor initializes the event to a neutral state. It sets `type` to `RESET_EVENT_NORMAL_DUNGEON`, and both `mapId` and `instanceId` to `0`. This creates an empty, invalid event object, likely used for initialization of containers or temporary variables before assignment.

*   **`DungeonResetEvent(ResetEventType t, uint32 _mapid, uint32 _instanceid)`**: The parameterized constructor creates a fully specified reset event. It assigns the provided `ResetEventType` to `type`, the map identifier to `mapId`, and the instance identifier to `instanceId`. This is the standard way to create an event for scheduling.

### Comparison

*   **`operator==(DungeonResetEvent const& e) const`**: Compares two `DungeonResetEvent` objects for equality. Crucially, it **only** compares `mapId` and `instanceId`. It ignores the `type` field. This implies that for the purposes of the scheduling system (specifically `DungeonResetScheduler`), an event is uniquely identified by the target map and instance, regardless of whether it is a warning, a forced reset, or a normal reset. This allows the system to check if a reset is already scheduled for a specific instance without worrying about the specific subtype of the reset event.

## Cross-Unit Boundaries

`DungeonResetEvent` is a passive data structure; it contains no methods that call out to other units. However, it is heavily consumed by the scheduling infrastructure:

*   **Called by `MapPersistentStateMgr/ScheduleReset`**: The `MapPersistentStateMgr` (via its internal `DungeonResetScheduler`) uses `DungeonResetEvent` as the key/value pair in its `m_resetTimeQueue` (a `std::multimap<time_t, DungeonResetEvent>`). When `ScheduleReset` is invoked, it constructs a `DungeonResetEvent` and inserts it into the queue. The `operator==` defined here is implicitly used by the map's internal logic or explicitly by callers checking for existing entries.
*   **Called by `MapPersistentStateMgr/ScheduleAllDungeonResets`**: During server startup or reload, this method iterates through maps and instances, constructing `DungeonResetEvent` objects to populate the initial schedule.
*   **Called by `MapPersistentStateMgr/AddPersistentState`**: When a new persistent state is added, if it requires a reset schedule (e.g., a new dungeon instance), this unit creates a `DungeonResetEvent` to register the future reset time.
*   **Called by `Map.Main/SetResetSchedule`**: The `Map` class (in `Map.cpp`) interacts with the scheduler to set or update reset times, passing `DungeonResetEvent` structures to define what needs to happen and when.

## Data Model

`DungeonResetEvent` itself does not interact directly with database tables. It is an in-memory representation of reset logic. The data it represents (reset times, map IDs, instance IDs) originates from:
1.  **`dbc` files**: Specifically `Map.dbc`, which defines map properties like instance type and reset day.
2.  **`instance_reset` / `instance` tables**: The `DungeonResetScheduler` loads historical reset times and current instance states from these tables to construct `DungeonResetEvent` objects for the schedule. However, `DungeonResetEvent` does not perform these queries; it is merely the payload passed around after the data is loaded by `DungeonResetScheduler::LoadResetTimes` and related methods in `MapPersistentStateMgr`.

## Notable Implementation Details

1.  **Bitfield Optimization**: The `type` member is declared as `uint8 type :8;`. While `uint8` is already 8 bits, the explicit bitfield notation suggests an intent to pack this struct tightly, although `mapId` (uint16) and `instanceId` (uint32) follow immediately. Given the alignment rules of most compilers, the struct size is likely dominated by the 4-byte `instanceId`. The bitfield doesn't save space here compared to a plain `uint8`, but it semantically marks `type` as a small enumeration.
2.  **Equality Semantics**: The `operator==` ignoring `type` is a critical design choice. It means that if a `RESET_EVENT_INFORM_1` (warning) is scheduled for a map, and later a `RESET_EVENT_NORMAL_DUNGEON` is attempted for the same map/instance, the system considers them "equal" in terms of identity. This prevents duplicate scheduling entries for the same logical reset target, ensuring that only one event exists in the `m_resetTimeQueue` for a given `mapId`/`instanceId` pair at a specific time (though `multimap` allows multiple entries with the same key, the equality check helps in management logic elsewhere).
3.  **No Virtual Functions**: As a simple struct with no inheritance, it has no vtable overhead, making it cheap to copy and store in large containers like `std::multimap`.

## Member Reference

**DungeonResetEvent**
Default constructor. Initializes `type` to `RESET_EVENT_NORMAL_DUNGEON`, `mapId` to `0`, and `instanceId` to `0`. Creates an empty event object.

**DungeonResetEvent#2**
Parameterized constructor. Accepts `ResetEventType`, `uint32` map ID, and `uint32` instance ID. Initializes the struct fields accordingly. Used to create valid, schedulable reset events.

**operator==**
Member function comparing two `DungeonResetEvent` instances. Returns `true` if `mapId` and `instanceId` match. Explicitly ignores the `type` field. Used by `MapPersistentStateMgr` and `DungeonResetScheduler` to identify unique reset targets.

---

<!-- machine-true, projected from graph.json -->

## Map — DungeonResetEvent

*Source:* MapPersistentStateMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| DungeonResetEvent | ctor | — | — | — |
| DungeonResetEvent#2 | ctor | — | Map.Main/SetResetSchedule, MapPersistentStateMgr/AddPersistentState, MapPersistentStateMgr/ScheduleAllDungeonResets | — |
| operator== | method | — | MapPersistentStateMgr/ScheduleReset | — |
