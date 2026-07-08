# ForcedDespawnDelayEvent

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ForcedDespawnDelayEvent

**Purpose & Responsibilities**

`ForcedDespawnDelayEvent` is a transient event handler class within the `Creature.h` unit of the wowvmangos codebase. Its sole responsibility is to manage the asynchronous despawning of a `Creature` instance after a specified delay. It acts as a bridge between the immediate request to remove a creature from the world (via `Creature.ForcedDespawn`) and the actual execution of that removal, allowing the server to schedule the despawn for a future tick rather than performing it instantly. This pattern ensures that game logic (such as visual effects or final animations) can complete before the entity is removed from memory and the world state.

The class inherits from `BasicEvent`, integrating into the server’s global event scheduler. It holds a reference to the `Creature` it manages and a configuration value for the respawn time, which it applies to the creature immediately upon execution.

**Member-by-Member Behavior**

The unit contains only one member: the constructor.

*   **`ForcedDespawnDelayEvent` (Constructor)**: Initializes the event object. It accepts a non-const reference to a `Creature` (`owner`) and an optional `uint32` parameter for the respawn time in seconds (`secsTimeToRespawn`, defaulting to 0). It initializes the base `BasicEvent` class, stores the reference to the creature in the private member `m_owner`, and stores the respawn time in `m_secsTimeToRespawn`. The constructor is marked `explicit` to prevent implicit conversions.

**Cross-Unit Boundaries**

*   **Called by `Creature.Main/ForcedDespawn`**: The `Creature` class (specifically the `ForcedDespawn` method, located in the `Creature` partial of this unit) creates instances of `ForcedDespawnDelayEvent`. When a creature needs to be despawned after a delay, `Creature.ForcedDespawn` instantiates this event and schedules it with the server’s event manager. The `Creature` passes itself (`*this`) and the desired respawn time to the event constructor. This establishes a dependency where the `Creature` relies on this event class to handle the deferred logic of its own removal.
*   **Calls out**: The MAP indicates no outgoing calls to other units from the constructor. However, the `Execute` method (declared in the header but implemented elsewhere, likely in `Creature.cpp` or a related event handling file) will interact with the `Creature` instance referenced by `m_owner`. Specifically, it will call methods on `m_owner` to set the respawn time and trigger the actual despawn/removal from the world. While the MAP does not list `Execute` as a separate member for this documentation scope (as it is not in the "Member" column of the provided MAP for this specific partial), the constructor sets up the state required for that future interaction. The primary boundary interaction defined in the MAP is the instantiation by `Creature`.

**Data Model**

This unit does not directly access any database tables. It operates entirely on in-memory objects (`Creature` instances). The `secsTimeToRespawn` parameter influences the `m_respawnTime` member of the `Creature` object, which may eventually be persisted to the database via `Creature.SaveToDB`, but `ForcedDespawnDelayEvent` itself performs no SQL operations.

**Notable Implementation Details**

*   **Reference Semantics**: The class stores a reference (`Creature& m_owner`) rather than a pointer or GUID. This implies that the `Creature` object must remain alive for the duration of the event's existence. If the `Creature` is destroyed before the event executes (e.g., due to a crash or unexpected deletion), the event would dereference a dangling reference, leading to undefined behavior. The safety of this design relies on the event scheduler ensuring that events are cancelled or handled appropriately if the associated object is deleted prematurely, or on the guarantee that the creature persists until the event fires.
*   **Minimal State**: The class is extremely lightweight, containing only two data members. This reflects its narrow, single-purpose design.
*   **Explicit Constructor**: The use of `explicit` prevents accidental creation of the event through implicit type conversions, ensuring that scheduling a forced despawn is an intentional action.

## Member Reference

**ForcedDespawnDelayEvent**
Constructor for the event. Takes a reference to the `Creature` to be despawned and an optional respawn time in seconds. Initializes the base `BasicEvent` and stores the creature reference and respawn time in private members. Called by `Creature.ForcedDespawn` to schedule the delayed removal of the creature.

---

<!-- machine-true, projected from graph.json -->

## Map — ForcedDespawnDelayEvent

*Source:* Creature.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ForcedDespawnDelayEvent | ctor | — | Creature.Main/ForcedDespawn | — |
