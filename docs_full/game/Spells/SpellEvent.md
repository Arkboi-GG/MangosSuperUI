# SpellEvent

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# SpellEvent

## Purpose & Responsibilities

`SpellEvent` is a lightweight adapter class that integrates the `Spell` lifecycle with the server's global event scheduler (`BasicEvent`). Its sole responsibility is to manage the asynchronous execution of a spell cast. When a spell requires a cast time or a delay before its effects are applied, the `Spell` object registers a `SpellEvent` instance with the scheduler. The scheduler invokes `SpellEvent::Execute` when the timer expires, triggering the spell's resolution logic. If the spell is cancelled or fails during the cast window, `SpellEvent::Abort` is invoked to clean up resources and notify relevant systems.

This unit does not contain spell logic itself; it delegates all behavioral decisions to the `Spell` class. It serves strictly as the bridge between the time-based event loop and the state machine of the `Spell` object.

## Member-by-Member Behavior

### Construction and Destruction

*   **`SpellEvent(Spell* spell)`**: Constructs the event, storing a raw pointer to the `Spell` instance it governs. This establishes the link between the scheduled task and the spell data.
*   **`~SpellEvent()`**: Destructor. As with most event classes in this codebase, it relies on the caller (the event scheduler) to manage memory.

### Event Execution

*   **`Execute(uint64 e_time, uint32 p_time)`**: This is the core entry point called by the event scheduler when the spell's cast time or delay has elapsed.
    *   It retrieves the associated `Spell` object via `GetSpell()`.
    *   It checks if the spell is still valid and active.
    *   It typically calls `Spell::cast()` or similar methods to proceed from the "casting" state to the "effect application" state.
    *   It returns `true` if the event should be deleted (one-shot event) or `false` if it needs to persist (though spells are generally one-shot events).

*   **`Abort(uint64 e_time)`**: Called when the spell is interrupted, cancelled, or fails validation before the cast completes.
    *   It retrieves the `Spell` object.
    *   It triggers cancellation logic within the `Spell` class (e.g., `Spell::cancel()`), which handles sending interruption packets to clients, removing temporary states, and cleaning up target lists.

### State Management

*   **`IsDeletable() const`**: Returns whether the event can be safely removed from the scheduler. This is crucial for preventing double-deletion or accessing freed memory if the spell object has already been destroyed by another mechanism. It likely checks if the internal `Spell` pointer is valid and if the spell itself is in a deletable state.

*   **`GetSpell()`**: A simple accessor that returns the raw pointer to the managed `Spell` object. This is primarily used by external systems (like interrupt handlers) that hold a reference to the `SpellEvent` and need to access the underlying spell data.

## Cross-Unit Boundaries

`SpellEvent` acts as a passive holder; it does not initiate complex collaborations but reacts to scheduler calls and exposes the `Spell` object.

*   **Called by `Player.Main/InterruptSpellsWithCastItem`**:
    *   **Direction**: Inbound.
    *   **Context**: When a player equips a new item that interrupts current casting (e.g., swapping weapons while casting a melee spell), the `Player` class iterates through active spells.
    *   **Interaction**: The `Player` unit calls `SpellEvent::GetSpell()` to retrieve the `Spell` object associated with the event. It then inspects the spell to determine if it should be interrupted based on the new item's properties. This allows the `Player` logic to make decisions about spell validity without needing direct knowledge of the event scheduling internals.

*   **Called by `Unit.Main/InterruptSpellsCastedOnMe`**:
    *   **Direction**: Inbound.
    *   **Context**: When a unit is targeted by an effect that interrupts spells cast upon them (e.g., a silence or knockback effect), the `Unit` class needs to identify and cancel those spells.
    *   **Interaction**: Similar to the player case, the `Unit` unit calls `SpellEvent::GetSpell()` to access the `Spell` object. It verifies if the spell is cast on the unit and if it is interruptible, then proceeds to abort the event.

*   **Calls out**: None. `SpellEvent` does not call into other units directly; it delegates all work to the `Spell` object, which in turn interacts with the rest of the engine.

## Data Model

`SpellEvent` does not interact with any database tables. It operates entirely in memory, managing transient runtime state for spell casting.

## Notable Implementation Details

*   **Raw Pointer Ownership**: `SpellEvent` stores a raw `Spell*` (`m_Spell`). It does not take ownership of the `Spell` object (no `std::unique_ptr` or similar). This implies that the lifetime of the `Spell` object is managed elsewhere (likely by the `SpellCaster` or the `Spell` manager), and `SpellEvent` must ensure it does not access the pointer after the `Spell` has been deleted. The `IsDeletable()` check helps mitigate use-after-free risks.
*   **Event Scheduler Integration**: By inheriting from `BasicEvent`, `SpellEvent` fits into the standard MaNGOS event loop. The `Execute` and `Abort` signatures match the expected interface for timed events.
*   **No Internal Logic**: The class contains no business logic regarding spell mechanics, damage calculation, or targeting. All such logic resides in the `Spell` class. This separation keeps the event handling layer thin and focused solely on timing and lifecycle management.
*   **Thread Safety**: Like most event classes in this engine, `SpellEvent` assumes it is accessed only from the main game thread (where the event scheduler runs). No mutexes or atomic operations are present.

## Member Reference

**GetSpell**
Accessor that returns the raw pointer to the associated `Spell` object. Used by `Player.Main/InterruptSpellsWithCastItem` and `Unit.Main/InterruptSpellsCastedOnMe` to inspect spell details during interruption checks.

---

<!-- machine-true, projected from graph.json -->

## Map — SpellEvent

*Source:* Spell.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GetSpell | method | — | Player.Main/InterruptSpellsWithCastItem, Unit.Main/InterruptSpellsCastedOnMe | — |
