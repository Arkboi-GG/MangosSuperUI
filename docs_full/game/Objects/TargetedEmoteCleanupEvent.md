# TargetedEmoteCleanupEvent

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# TargetedEmoteCleanupEvent

**Purpose & Responsibilities**

`TargetedEmoteCleanupEvent` is a transient event handler class defined within `Creature.h`. Its sole responsibility is to restore a `Creature`'s orientation to a previously saved value after a targeted emote sequence has concluded. It acts as the cleanup phase for the `TargetedEmoteEvent` workflow, ensuring that visual state changes (specifically rotation toward a target) are temporary and reversible.

This class inherits from `BasicEvent`, integrating it into the server's event scheduling system. It is designed to execute once, typically scheduled to run shortly after a `TargetedEmoteEvent` completes or fails, resetting the creature's facing direction to prevent it from remaining permanently rotated toward a target that may no longer be relevant or visible.

## Member-by-Member Behavior

### **TargetedEmoteCleanupEvent** (Constructor)
The constructor initializes the event with two critical pieces of state:
1.  **`owner`**: A reference to the `Creature` instance that performed the emote. This allows the event to modify the creature's orientation upon execution.
2.  **`orientation`**: A `float` representing the creature's original orientation (rotation angle) before the targeted emote began. This value is stored in the private member `m_orientation` to be restored later.

The constructor explicitly calls the base `BasicEvent` constructor, registering the event in the scheduler context.

### **Execute** (Method)
Although the implementation of `Execute` is not visible in the provided header snippet (it is likely defined in `Creature.cpp` or a related source file), its signature and the class's design dictate its behavior:
1.  It receives timing parameters (`e_time`, `p_time`) from the event scheduler.
2.  It checks if the `owner` creature is still valid (alive and in the world).
3.  If valid, it sets the `owner`'s orientation back to `m_orientation`.
4.  It returns `true` to indicate successful execution, allowing the event to be removed from the scheduler.

If the creature has despawned or died before the event triggers, the method likely performs a safe no-op or early exit to prevent accessing invalid memory.

## Cross-Unit Boundaries

*   **Called by:** `Map.ScriptCommands/ScriptCommand_Emote`
    *   **Direction:** Outbound (from `Map` to `TargetedEmoteCleanupEvent`)
    *   **Collaboration:** The `Map` unit, specifically through its script command handling subsystem (`ScriptCommand_Emote`), creates instances of `TargetedEmoteCleanupEvent`. This occurs when a script commands a creature to perform a targeted emote. The `Map` unit schedules this cleanup event to ensure the creature's orientation is reset after the emote duration expires or the target becomes invalid. This decouples the immediate emote action from its cleanup, allowing the server to handle other tasks while waiting for the emote to finish.

*   **Calls out:** None
    *   The class itself does not initiate calls to other units during construction. Its interaction with the `Creature` unit happens internally via the stored reference during the `Execute` phase, which is part of the event loop managed by the core engine, not an explicit cross-unit call in the traditional sense of invoking another module's API.

## Data Model

This unit does not interact with any database tables. It operates entirely in memory, managing transient state associated with a specific `Creature` instance during its lifetime.

## Notable Implementation Details

*   **Reference Semantics:** The class stores a reference (`Creature& m_owner`) rather than a pointer or GUID. This implies that the event is expected to execute quickly relative to the creature's lifetime. If the creature is destroyed before the event fires, dereferencing `m_owner` would result in undefined behavior. Therefore, the `Execute` method (though not shown) must robustly check the validity of the `owner` reference, likely by verifying if the creature still exists in the world map or by using a smart pointer pattern if the underlying `BasicEvent` framework supports it. Given the typical MaNGOS/WowVM architecture, it's highly probable that `Execute` checks `m_owner.IsInWorld()` or similar validity flags before proceeding.
*   **Orientation Restoration:** The class captures the orientation *before* the emote starts. This ensures that if the creature was already facing a specific direction (e.g., patrolling, looking at a player), it returns to that exact state, maintaining visual continuity.
*   **Event Scheduling:** As a `BasicEvent`, it relies on the global event scheduler. The timing of its execution is determined by when it was scheduled by `ScriptCommand_Emote`. This allows for precise control over how long the creature remains oriented toward the target.
*   **No State Persistence:** The class holds no persistent state beyond the single execution. Once `Execute` runs, the event object is discarded.

## Member Reference

**TargetedEmoteCleanupEvent**
Constructor that initializes the event with a reference to the owning `Creature` and the original orientation value to be restored. It prepares the event for scheduling by the `Map` unit's script command system.

---

<!-- machine-true, projected from graph.json -->

## Map — TargetedEmoteCleanupEvent

*Source:* Creature.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| TargetedEmoteCleanupEvent | ctor | — | Map.ScriptCommands/ScriptCommand_Emote | — |
