# ScheduledTeleportData

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ScheduledTeleportData

**Purpose & Responsibilities**

`ScheduledTeleportData` is a lightweight aggregate struct defined in `Player.h` that encapsulates the parameters required to perform a deferred or asynchronous teleportation of a `Player`. It serves as a data carrier for teleport operations that cannot be executed immediately or require coordination between different execution contexts (e.g., initiating a teleport in one thread or logic path, but executing the actual map transition in another, such as during the main game loop update).

The struct holds the destination coordinates (`targetMapId`, `x`, `y`, `z`, `orientation`), configuration flags (`options`), and a recovery callback (`recover`). The recovery callback allows the caller to specify cleanup or state-restoration logic that must run after the teleport completes or fails, ensuring that transient states (like combat locks or movement restrictions) are properly handled post-transition.

**Member-by-Member Behavior**

The unit consists of two members: the struct declaration itself and its constructor.

1.  **`ScheduledTeleportData` (Declaration)**
    *   **Kind:** Struct Definition
    *   **Behavior:** Defines the memory layout and default initialization for teleport parameters.
        *   `targetMapId`: The ID of the map to teleport to. Defaults to `0`.
        *   `x`, `y`, `z`: The spatial coordinates within the target map. Default to `0.0f`.
        *   `orientation`: The facing angle of the player upon arrival. Defaults to `0.0f`.
        *   `options`: A bitmask of flags controlling teleport behavior (e.g., whether to leave combat, unsummon pets, etc.). Defaults to `0`.
        *   `recover`: A `std::function<void()>` callback. Defaults to an empty function. This is invoked by the executor of the teleport to handle post-teleport logic.

2.  **`ScheduledTeleportData` (Constructor)**
    *   **Kind:** Constructor
    *   **Signature:** `ScheduledTeleportData(uint32 mapid, float x, float y, float z, float o, uint32 options, std::function<void()> recover_)`
    *   **Behavior:** Initializes all members with the provided arguments. It uses `std::move` for the `recover_` callback to efficiently transfer ownership of the lambda/function object into the struct. This constructor is used to create a fully populated teleport request in a single expression.

**Cross-Unit Boundaries**

*   **Called By: `Player.Main/TeleportTo`**
    *   **Direction:** Outbound (from `Player` to `ScheduledTeleportData`)
    *   **Collaboration:** The `Player` class (specifically the `TeleportTo` method family) constructs `ScheduledTeleportData` instances to package teleport requests. When a player initiates a teleport (via command, quest reward, or death respawn), `Player::TeleportTo` gathers the destination coordinates and options, wraps them in a `ScheduledTeleportData` object, and likely passes this object to a scheduler or queue (such as `ExecuteTeleportFar` or a delayed operation handler) to ensure the teleport happens safely relative to the game loop and network synchronization. The `recover` callback typically captures `this` (the Player pointer) to restore player state after the teleport.

**Data Model**

This unit does not interact directly with any database tables. It is a pure in-memory data structure used for runtime logic.

**Notable Implementation Details**

*   **Callback Ownership:** The `recover` member is a `std::function<void()>`. This design allows for flexible, context-specific cleanup logic without requiring the teleport execution engine to know the specifics of *why* the teleport occurred. For example, a teleport triggered by a spell might need to remove the spell aura, while a teleport triggered by a death might need to reset combat flags.
*   **Default Initialization:** The struct provides a default constructor (`ScheduledTeleportData() = default;`) which initializes all numeric fields to zero and the callback to an empty function. This allows for stack allocation and zero-initialization if needed, though the parameterized constructor is preferred for valid teleport requests.
*   **Thread Safety Implications:** While the struct itself is not thread-safe, its usage pattern (passing data from a high-level request to a lower-level executor) implies that the data contained within must be stable for the duration of the teleport process. The `recover` callback must be careful not to access invalid memory if the `Player` object is destroyed during the teleport (though typically the teleport prevents destruction until completion).

## Member Reference

**ScheduledTeleportData#2**
Declares the `ScheduledTeleportData` struct, defining the fields `targetMapId`, `x`, `y`, `z`, `orientation`, `options`, and `recover`. It provides a default constructor that initializes all fields to zero/empty.

**ScheduledTeleportData**
Constructs a `ScheduledTeleportData` instance with specific destination coordinates (`mapid`, `x`, `y`, `z`, `o`), configuration `options`, and a `recover` callback function. It moves the callback into the struct to avoid copying.

---

<!-- machine-true, projected from graph.json -->

## Map — ScheduledTeleportData

*Source:* Player.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ScheduledTeleportData#2 | decl | — | — | — |
| ScheduledTeleportData | ctor | — | Player.Main/TeleportTo | — |
