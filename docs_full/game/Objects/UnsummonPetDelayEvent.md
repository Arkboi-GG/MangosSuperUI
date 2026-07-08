# UnsummonPetDelayEvent

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# UnsummonPetDelayEvent

## Purpose & Responsibilities

`UnsummonPetDelayEvent` is a lightweight event handler class defined within `Pet.h`. Its sole responsibility is to encapsulate the deferred execution of a pet's unsummoning process. In the MaNGOS/WowVMaNGOS architecture, immediate actions that must occur after a delay (such as despawning a creature after a timeout) are often handled by scheduling events on a scheduler object. This class serves as the payload for such a scheduled event, holding the necessary context (`Pet` reference and `PetSaveMode`) to perform the unsummon operation at the correct future time.

It acts as a bridge between the asynchronous timing mechanism (the scheduler) and the synchronous state-change logic of the `Pet` class. By bundling the target pet and the desired save mode into a single executable object, it ensures that the unsummon action occurs atomically with respect to the event loop, preventing race conditions where a pet might be accessed after being deleted but before its removal from the world is finalized.

## Member-by-Member Behavior

### **UnsummonPetDelayEvent** (Constructor)
The constructor initializes the event object with two critical pieces of data:
1.  **`Pet& pet`**: A non-const reference to the `Pet` instance that is to be unsummoned. Using a reference implies that the lifetime of the `Pet` object must exceed the lifetime of this event; if the `Pet` is destroyed before the event executes, the behavior is undefined (likely a crash due to dangling reference).
2.  **`PetSaveMode mode`**: An enumeration value indicating how the pet's state should be persisted or cleaned up upon unsummoning. This determines whether the pet is saved to the database as deleted, kept in a stable slot, or simply removed from memory without saving.

The constructor delegates initialization to the base class `BasicEvent()` and stores these values in the private members `m_pet` and `m_mode`.

### **Execute** (Method)
Although the implementation of `Execute` is not present in the provided source snippet (it is likely defined in `Pet.cpp` or a related implementation file), its signature and inheritance from `BasicEvent` dictate its role. When the scheduler triggers this event, `Execute` is called. Based on the class design and the `Called by` relationship in the MAP, this method performs the actual unsummoning logic. It likely calls `m_pet.Unsummon(m_mode)` or a similar internal routine to remove the pet from the world, handle its database persistence according to `m_mode`, and clean up associated resources. The return value (bool) typically indicates whether the event was successfully processed or if it needs to be rescheduled (though for a one-time unsummon, it likely returns `false` to indicate completion).

## Cross-Unit Boundaries

### Called By: `Pet.Main/DelayedUnsummon`
The `UnsummonPetDelayEvent` is instantiated and scheduled by the `DelayedUnsummon` method of the `Pet` class (referenced in the MAP as `Pet.Main/DelayedUnsummon`).
*   **Direction**: `Pet` -> `UnsummonPetDelayEvent`
*   **Collaboration**: When a pet needs to be unsummoned after a specific delay (e.g., a temporary summon expiring), `Pet::DelayedUnsummon` creates an instance of `UnsummonPetDelayEvent`. It passes `this` (the current `Pet` instance) and the desired `PetSaveMode` to the event's constructor. The `Pet` class then registers this event with the global scheduler. This decouples the decision to unsummon from the actual execution, allowing the game server to continue processing other tasks during the delay period.

### Calls Out: None
The MAP indicates no outgoing calls to other units from `UnsummonPetDelayEvent` itself. While the `Execute` method logically interacts with the `Pet` object, this is considered an internal interaction within the `Pet` subsystem's lifecycle management rather than a cross-unit dependency in the architectural sense defined by the MAP. The event does not call into external services, databases, or other distinct modules directly; it relies on the `Pet` object to handle those interactions.

## Data Model

This unit does not interact directly with any database tables. It operates entirely in memory, managing the state transition of a `Pet` object. Any database operations resulting from the unsummon process are delegated to the `Pet` class methods (such as `SavePetToDB` or `DeleteFromDB`), which are invoked indirectly through the `Execute` method's interaction with `m_pet`. Therefore, no SQL queries or table schemas are relevant to this specific class.

## Notable Implementation Details

1.  **Reference Lifetime Risk**: The class stores a `Pet&` reference. This is a critical design constraint. The scheduler must guarantee that the `Pet` object remains valid until the event is executed. If the `Pet` is deleted prematurely (e.g., by a different code path that doesn't cancel this scheduled event), the `Execute` method will dereference a dangling pointer, leading to undefined behavior. Proper cancellation of this event in `Pet::RemoveFromWorld` or similar cleanup routines is essential for stability.
2.  **Non-Const Reference**: The use of a non-const reference (`Pet&`) rather than a pointer (`Pet*`) suggests that the existence of the pet is assumed to be guaranteed. If nullability were a concern, a pointer would have been used, requiring null checks in `Execute`. The choice of reference shifts the burden of safety to the caller (`Pet::DelayedUnsummon`) and the scheduler's lifecycle management.
3.  **Minimal State**: The class holds minimal state (only the reference and the mode), making it cheap to instantiate and store in the scheduler's queue. This efficiency is important for high-frequency events like pet summons/despawns in a busy server environment.
4.  **Inheritance from `BasicEvent`**: As a subclass of `BasicEvent`, it integrates into the core event system of the engine. This allows it to be scheduled with a specific time offset, leveraging the existing infrastructure for time-based callbacks.

## Member Reference

**UnsummonPetDelayEvent**
Constructor that initializes the event with a reference to the `Pet` to be unsummoned and the `PetSaveMode` determining how the pet's state is handled. It delegates to `BasicEvent()` and stores the parameters in private members `m_pet` and `m_mode`.

---

<!-- machine-true, projected from graph.json -->

## Map — UnsummonPetDelayEvent

*Source:* Pet.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| UnsummonPetDelayEvent | ctor | — | Pet.Main/DelayedUnsummon | — |
