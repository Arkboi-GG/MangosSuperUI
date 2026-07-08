<!-- provenance: verbose -->
# PatrolMovementGenerator

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`PatrolMovementGenerator` implements patrol-based movement for `Creature` entities. It supports two initialization modes:
1.  **Independent Patrols:** Constructed with a `Creature` reference, asserting valid patrol data via `InitPatrol`.
2.  **Group Patrols:** Constructed with a leader `ObjectGuid` and `CreatureGroupMember` pointer, enabling creatures to follow a leader or maintain formation within a `CreatureGroup`.

Inheriting from `MovementGeneratorMedium`, it operates at a priority level that yields to high-priority actions (e.g., combat) but persists over idle states.

## Member-by-Member Behavior

### Initialization and Lifecycle

**`PatrolMovementGenerator` (Default Constructor)**
Initializes an independent patrol. It immediately asserts that `InitPatrol(c)` returns `true`. This enforces a strict contract: the generator cannot be constructed unless valid patrol data exists for the creature. Failure results in an assertion failure.

**`PatrolMovementGenerator#2` (Group Constructor)**
Initializes a group-bound patrol. It stores the leader's `ObjectGuid` in `m_leaderGuid` and copies the `CreatureGroupMember` data into `m_groupMember`. This bypasses `InitPatrol`, indicating that movement logic is derived from the group context rather than a standalone path.

**`Initialize`**
Prepares the generator for active movement. Typically resets internal timers and ensures the creature is ready to move.

**`Finalize`**
Cleans up resources when the generator is removed. Stops movement commands and clears state.

**`Interrupt`**
Handles external interruptions (e.g., combat). Pauses the patrol timer and preserves current progress for potential resumption.

**`Reset`**
Resets the patrol state, potentially returning the creature to the start of the path or reloading path data.

### Movement Logic

**`LoadPath`**
Loads patrol path data for the specified `Creature`. Unlike `WaypointMovementGenerator`, it derives identifiers from the creature itself, populating the internal path structure.

**`Update`**
The core tick function. Checks arrival at waypoints, handles wait times, and triggers movement to the next node. Returns `true` if active, `false` if complete or interrupted.

**`StartMove`**
Initiates movement toward the next waypoint, calculating direction/speed and issuing commands to the creature's movement handler.

**`GetResetPosition`**
Retrieves the fallback position for the creature if the patrol is aborted or reset, preventing invalid states.

### Configuration

**`GetMovementGeneratorType`**
Returns `PATROL_MOTION_TYPE`, identifying the generator to the engine for debugging and state management.

**`InitPatrol`**
Validates and sets up the patrol for an independent creature. Called by the default constructor; its success is mandatory for construction.

## Cross-Unit Boundaries

*   **Calls Out:**
    *   **`MovementGeneratorMedium`**: Provides the lifecycle framework (`Initialize`, `Finalize`, etc.) and integration with the creature's movement system.
    *   **`Creature`**: Reads state, sets positions, and receives movement commands.
    *   **`ObjectGuid` / `CreatureGroupMember`**: Used in the group constructor to link to leader/group state.

*   **Called By:**
    *   **`Creature` / `AI` Systems**: Attach this generator when patrol behavior is required.
    *   **`MovementHandler`**: Invokes `Update` and lifecycle methods during the game loop.

## Data Model

`PatrolMovementGenerator` does not directly query database tables in its interface. `LoadPath` implies data-driven paths, likely sourced from `waypoint_data` or similar tables via the creature's GUID/Entry, but this access is abstracted away from this unit.

## Notable Implementation Details

1.  **Constructor Assertion:** The default constructor asserts `InitPatrol(c)`. This is a hard requirement: invalid patrol data causes a crash in debug builds. Maintainers must ensure data integrity before instantiation.
2.  **Dual Initialization:** The two constructors reflect two distinct behaviors: autonomous patrols (validated by `InitPatrol`) and group-following patrols (derived from leader state).
3.  **No Repeating Flag:** Unlike `WaypointMovementGenerator`, there is no `m_repeating` flag. Repetition is likely handled externally by the AI or by restarting the generator.
4.  **Medium Priority:** Inheritance from `MovementGeneratorMedium` ensures patrols yield to combat/chase but override idle wandering.

## Member Reference

**`PatrolMovementGenerator#2`**
Constructor for group-based patrol. Initializes `m_leaderGuid` and `m_groupMember` from the provided `leader` GUID and `member` pointer. Bypasses `InitPatrol`, assuming group context provides necessary movement data.

**`PatrolMovementGenerator`**
Constructor for independent patrol. Takes a `Creature` reference and asserts that `InitPatrol(c)` succeeds. Ensures the creature has valid patrol data before the generator is considered valid.

**`LoadPath`**
Declares the method to load patrol path data for the given creature. Implementation likely queries the database or internal cache using the creature's GUID/Entry to populate the internal path structure.

**`GetMovementGeneratorType`**
Returns `PATROL_MOTION_TYPE`. Identifies the generator type to the engine's movement system, distinguishing it from waypoint, flight, or random movement generators.

---

<!-- machine-true, projected from graph.json -->

## Map — PatrolMovementGenerator

*Source:* WaypointMovementGenerator.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| PatrolMovementGenerator#2 | ctor | — | — | — |
| PatrolMovementGenerator | ctor | — | — | — |
| LoadPath | decl | — | — | — |
| GetMovementGeneratorType | method | — | — | — |
