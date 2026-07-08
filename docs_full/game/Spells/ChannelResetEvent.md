# ChannelResetEvent

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ChannelResetEvent

`ChannelResetEvent` is a transient event class defined in `Spell.h` that manages the state transition of a `Unit` following a channeled spell. It inherits from `BasicEvent`, integrating it into the server’s event scheduler. Its primary responsibility is to ensure that the `UNIT_STATE_PENDING_CHANNEL_RESET` flag is correctly applied to the caster when the event is created, marking the unit as being in a transitional state where a channel reset is pending.

The class acts as a deferred action mechanism. While the constructor immediately applies the pending state flag, the actual resolution of this state (clearing the flag or performing final cleanup) is handled by the `Execute` and `Abort` methods, which are declared in this header but implemented in the corresponding source file (`Spell.cpp`).

## Member-by-Member Behavior

### Construction and Initialization
The **`ChannelResetEvent`** constructor accepts a pointer to a `Unit` (the caster). Upon creation, it performs two actions:
1. It stores the `Unit` pointer in the protected member `m_caster`.
2. It immediately calls `caster->AddUnitState(UNIT_STATE_PENDING_CHANNEL_RESET)`. This marks the unit as having a pending channel reset, signaling to other subsystems (such as movement or AI) that the unit is in a specific post-channel state.

### Destruction
The **`~ChannelResetEvent`** destructor is empty. It relies on the base class `BasicEvent` destructor to handle any necessary cleanup. The class does not own the `Unit` pointer, so no manual deletion is performed.

## Cross-Unit Boundaries

### Calls Out
- **`Unit.AddUnitState`**: Called by the **`ChannelResetEvent`** constructor. This is a cross-unit call to the `Unit` class. It adds the `UNIT_STATE_PENDING_CHANNEL_RESET` flag to the caster’s internal state bitmask. This interaction ensures that the unit’s state reflects the pending reset immediately upon event creation.

### Called By
- **`Spell.SendChannelUpdate`**: According to the MAP, this event is instantiated by `Spell.SendChannelUpdate` (located in the `Spell` unit). This occurs during the update cycle of a channeled spell, scheduling the reset event to manage the caster's state after the channeling process concludes or is interrupted.

## Data Model

This unit does not interact with any database tables. All operations are performed in-memory on the `Unit` object’s state.

## Notable Implementation Details

- **Immediate State Modification**: The constructor modifies the `Unit`'s state immediately by adding `UNIT_STATE_PENDING_CHANNEL_RESET`. This means the flag is set before the event is scheduled or executed. If the event is aborted or fails to execute, the `Abort` method (implemented externally) is responsible for cleaning up this state to prevent stale flags.
- **Raw Pointer Usage**: The class stores a raw `Unit*` (`m_caster`). It assumes the `Unit` remains valid for the lifetime of the event. External logic (likely in the `Spell` or `Unit` classes) must ensure that events are canceled if the caster dies or becomes invalid before the event executes.

## Member Reference

**`ChannelResetEvent`**
Constructor. Takes a `Unit*` caster. Stores the pointer in `m_caster` and immediately calls `caster->AddUnitState(UNIT_STATE_PENDING_CHANNEL_RESET)` to mark the unit as pending a channel reset.

**`~ChannelResetEvent`**
Destructor. Empty body. Relies on the base class `BasicEvent` for cleanup.

---

<!-- machine-true, projected from graph.json -->

## Map — ChannelResetEvent

*Source:* Spell.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ChannelResetEvent | ctor | — | Spell.Main/SendChannelUpdate | — |
| ~ChannelResetEvent | dtor | — | — | — |
