# RecordedMovementPacket

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# RecordedMovementPacket

**RecordedMovementPacket** is a lightweight data structure defined in `BattleBotWaypoints.h`. It serves as a container for a single step in a recorded movement sequence, storing the network opcode, time delta, movement flags, and spatial position required to reconstruct a movement event.

This unit contains no executable logic, database interactions, or cross-unit dependencies. It is purely a definition of data structures and global constants related to battleground waiting positions and path definitions.

## Purpose & Responsibilities

1.  **Movement Packet Definition**: Defines the atomic unit of a movement log via the `RecordedMovementPacket` struct.
2.  **Static Coordinate Constants**: Provides hardcoded `Position` constants for fixed points of interest in Warsong Gulch (WS), Arathi Basin (AB), and Alterac Valley (AV), including waiting and flag positions.
3.  **Path Structure Definitions**: Defines `BattleBotWaypoint` (coordinates + function pointer) and `BattleBotPath` (vector of waypoints) for AI navigation.

## Member-by-Member Behavior

### RecordedMovementPacket

**RecordedMovementPacket** is the constructor for the `RecordedMovementPacket` struct. It initializes `opcode`, `timeDiff`, `moveFlags`, and `position` from the provided arguments. It performs direct assignment with no validation or side effects, relying on the external `Position` struct to aggregate x, y, z, and orientation.

## Cross-Unit Boundaries

This unit has **no outgoing calls**. It is referenced by other units to construct movement logs, access static coordinates, or define navigation paths for `BattleBotAI`. The `BattleBotWaypointFunc` typedef indicates a dependency on `BattleBotAI`, but the functions themselves are defined elsewhere.

## Data Model

This unit does **not** interact with any database tables. All data is hardcoded or passed at runtime.

## Notable Implementation Details

1.  **Function Pointers**: `BattleBotWaypoint` uses a raw function pointer (`BattleBotWaypointFunc`). Assignees must match the signature `void(BattleBotAI*)`.
2.  **External Vectors**: Global vectors like `vAllianceGraveyardJumpPath` are declared `extern`; their definition and population occur in other units.
3.  **Hardcoded Coordinates**: Positions are hardcoded floats. Changes to map geometry require manual updates.
4.  **No Default Constructor**: `RecordedMovementPacket` requires all six parameters, preventing uninitialized instances.

## Member Reference

**RecordedMovementPacket**
Constructor for the `RecordedMovementPacket` struct. Initializes `opcode`, `timeDiff`, `moveFlags`, and `position` from the provided arguments. No validation or side effects occur.

---

<!-- machine-true, projected from graph.json -->

## Map — RecordedMovementPacket

*Source:* BattleBotWaypoints.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| RecordedMovementPacket | ctor | — | — | — |
