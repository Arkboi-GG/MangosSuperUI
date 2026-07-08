# JumpInfo

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# JumpInfo

**Purpose & Responsibilities**

`JumpInfo` is a nested structure within `MovementInfo` (defined in `MovementInfo.h`) that encapsulates the kinematic state required to simulate a character’s jump trajectory. It stores the initial velocity components, orientation angles, starting position, and the client-side timestamp at which the jump began. This data allows the server to reconstruct or validate the parabolic arc of a jump, ensuring synchronization between the client’s predicted movement and the server’s authoritative physics simulation.

The structure is purely data-bearing; it contains no methods other than its default constructor. Its fields are initialized to zero or neutral values upon construction, ensuring a clean state before being populated by movement packet parsing logic (handled by `MovementInfo::Read`, which is outside this unit’s scope but interacts with this structure).

## Member-by-Member Behavior

### **JumpInfo** (Constructor)
The default constructor initializes all members of the `JumpInfo` structure to safe, neutral defaults:
- `zspeed`, `sinAngle`, `cosAngle`, and `xyspeed` are set to `0.0f`.
- `start` (a `Position` object) is default-constructed (typically zeroed coordinates).
- `startClientTime` is set to `0`.

This ensures that if a `JumpInfo` instance is created but not yet populated with valid jump data, it represents a stationary, non-jumping state.

## Cross-Unit Boundaries

`JumpInfo` itself does not call into other units, nor is it directly called by other units in the provided MAP. However, it is a member of `MovementInfo`. The `MovementInfo` class exposes `GetJumpInfo()` to allow external units to access this data. Typically, movement processing logic (such as pathfinding or physics validation modules) will read these values to calculate expected positions during a jump. Conversely, network serialization code (like `MovementInfo::Read`) will populate these fields when a jump movement packet is received from the client.

## Data Model

`JumpInfo` does not interact with any database tables. It is a transient runtime structure used exclusively for in-memory movement state management.

## Notable Implementation Details

1. **Kinematic Decomposition**: The structure separates horizontal and vertical motion. `xyspeed` combined with `sinAngle` and `cosAngle` defines the horizontal velocity vector, while `zspeed` defines the initial vertical velocity. This decomposition is standard for projectile motion calculations in 3D space.
2. **Client Time Tracking**: The `startClientTime` field is crucial for latency compensation. By knowing when the client initiated the jump, the server can adjust the simulation timeline to account for network delay, preventing desynchronization where the server might otherwise think the jump started earlier or later than intended.
3. **Zero Initialization**: The explicit initialization in the constructor prevents undefined behavior from uninitialized memory, which is critical for floating-point physics calculations.

## Member Reference

**JumpInfo**  
Default constructor for the `JumpInfo` structure. Initializes `zspeed`, `sinAngle`, `cosAngle`, `xyspeed` to `0.0f`, `start` to a default `Position`, and `startClientTime` to `0`. Ensures a clean, neutral state for jump kinematics.

---

<!-- machine-true, projected from graph.json -->

## Map — JumpInfo

*Source:* MovementInfo.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| JumpInfo | ctor | — | — | — |
