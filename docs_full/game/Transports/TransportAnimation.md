# TransportAnimation

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# TransportAnimation

**Purpose & Responsibilities**

`TransportAnimation` is a lightweight data structure within the `TransportMgr` subsystem responsible for holding the static path definition of a vehicle or moving platform (a "transport") in the game world. It aggregates a sequence of animation nodes (`TransportAnimationEntry`) that define the spatial trajectory of the transport over time. Its primary responsibility is to provide random-access lookup capabilities to determine which segment of the path corresponds to a specific point in the transport's lifecycle (`time`). It does not manage the runtime state of the transport (such as current position or velocity); rather, it serves as the immutable blueprint from which the `TransportMgr` calculates positions during simulation.

**Member-by-Member Behavior**

The `TransportAnimation` struct contains only one member defined in this unit: its constructor.

*   **Constructor (`TransportAnimation()`)**: Initializes the instance by setting the `TotalTime` member to zero. The `Path` container (a `std::map`) is default-initialized to empty. This ensures that any newly created `TransportAnimation` object starts in a clean state before path data is populated by the `TransportMgr`.

**Cross-Unit Boundaries**

*   **Calls Out**: None. The constructor performs no external calls.
*   **Called By**: None listed in the map. However, based on the source code, instances of `TransportAnimation` are stored in `m_transportAnimations` within `TransportMgr` (specifically in `TransportMgr.cpp`, which is not part of this unit). The `TransportMgr` populates these structures via `LoadTransportAnimationAndRotation()` and retrieves them via `GetTransportAnimInfo()`.

**Data Model**

This unit does not directly interact with database tables. It consumes data loaded into memory by `TransportMgr` from DBC files (specifically `TransportAnimation.dbc` and potentially `TransportAnimationEntry` structures). The `TransportAnimation` struct itself holds pointers to `TransportAnimationEntry` objects, which are typically loaded from DBC stores, not SQL tables. Therefore, no SQL tables are touched by this specific struct.

**Notable Implementation Details**

*   **Time-Based Lookup**: The struct provides two methods, `GetPrevAnimNode` and `GetNextAnimNode`, which take a `uint32 time` parameter. These methods allow the system to interpolate the transport's position between two known animation nodes based on the elapsed time since the transport started its route. This implies that the `Path` map is keyed by time segments, allowing efficient binary search or iteration to find the relevant segment for a given timestamp.
*   **Commented-Out Rotation Support**: The header contains commented-out code for `TransportPathRotationContainer` and `GetAnimRotation`. This indicates that rotation handling was either moved to a different system (noted as "wotlk onwards") or deprecated. The current `TransportAnimation` struct only handles positional pathing via `TransportAnimationEntry`.
*   **Const-Correctness**: The lookup methods `GetPrevAnimNode` and `GetNextAnimNode` are marked `const`, ensuring that querying the path does not modify the animation data.

## Member Reference

**TransportAnimation**
Constructor for the `TransportAnimation` struct. Initializes `TotalTime` to 0 and leaves the `Path` map empty. This sets up a blank slate for the `TransportMgr` to populate with path nodes.

---

<!-- machine-true, projected from graph.json -->

## Map — TransportAnimation

*Source:* TransportMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| TransportAnimation | ctor | — | — | — |
