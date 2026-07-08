# UnitBaseEvent

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# UnitBaseEvent

`UnitBaseEvent` is the abstract base class for event objects within the threat management system of `wowvmangos`. It provides a minimal interface for identifying and filtering events by type using a bitmask integer (`uint32`). The class itself contains no complex logic; its primary responsibility is to serve as a common polymorphic root for two derived classes, `ThreatRefStatusChangeEvent` and `ThreatManagerEvent`, which carry specific data payloads related to changes in a creature's threat list (e.g., a player coming online, threat values changing, or the target switching).

This unit defines the `UnitThreatEventType` enumeration, which assigns specific bit flags to distinct threat-related occurrences. These flags allow the system to batch or filter events efficiently. For instance, `UEV_THREAT_REF_EVENT_MASK` aggregates all events relevant to individual hostile references (players/pets), while `UEV_THREAT_MANAGER_EVENT_MASK` aggregates events relevant to the overall threat manager state (sorting, targeting).

The class does not interact with any database tables. It operates entirely in memory as part of the runtime combat AI logic.

## Member-by-Member Behavior

### Event Type Management
The core functionality of `UnitBaseEvent` revolves around the `iType` member variable, which stores the event's classification.

*   **Construction**: The constructor initializes `iType` with the provided `pType`. This value must correspond to one of the bits defined in `UnitThreatEventType`.
*   **Retrieval**: `getType` returns the raw `uint32` type flag. This is used by consumers to determine the nature of the event.
*   **Filtering**: `matchesTypeMask` performs a bitwise AND operation between the event's `iType` and a provided mask. It returns `true` if any bits overlap. This allows callers to check if an event belongs to a broad category (e.g., "is this a reference-level event?") without needing to switch on every possible specific type.
*   **Modification**: `setType` allows the event type to be changed after construction. While less common, this permits event reuse or dynamic reclassification.

### Cross-Unit Boundaries

*   **Called by `ThreatManager::processThreatEvent`**:
    The `ThreatManager` (in `ThreatManager.cpp`) consumes these events. Specifically, it calls `getType` to dispatch the event to the appropriate handling logic. For example, if the type indicates `UEV_THREAT_SORT_LIST`, the manager knows it needs to re-sort its internal threat container. If the type is `UEV_THREAT_REF_THREAT_CHANGE`, it updates the specific hostile reference. The `matchesTypeMask` method is not explicitly called by `ThreatManager` in the provided map, but its existence supports potential bulk-processing or filtering patterns in other parts of the threat subsystem.

### Data Model

This unit does not access any database tables. All data is transient and held in memory during the lifetime of the event object.

### Notable Implementation Details

1.  **Bitmask Design**: The `UnitThreatEventType` enum uses powers of two (`1<<0`, `1<<1`, etc.), enabling efficient bitwise operations. This design choice suggests that events might be combined or filtered in batches, although the current `UnitBaseEvent` only holds a single type. The derived classes likely follow this pattern.
2.  **Inheritance Hierarchy**: `UnitBaseEvent` is a pure data carrier. Its derived classes, `ThreatRefStatusChangeEvent` and `ThreatManagerEvent`, add context-specific data (pointers to `HostileReference`, `ThreatManager`, `ThreatContainer`, and value unions). `UnitBaseEvent` itself is agnostic to this data, ensuring a clean separation between the event *identity* (type) and the event *payload*.
3.  **Typo in Enum Name**: The enum value `UEV_THREAT_REF_ASSECCIBLE_STATUS` contains a typo ("ASSECCIBLE" instead of "ACCESSIBLE"). This is a minor maintenance note but does not affect functionality as long as the string/bit value is consistent throughout the codebase.
4.  **No Virtual Destructor**: `UnitBaseEvent` does not define a virtual destructor. Since it has no virtual methods and is likely only deleted through pointers to its derived classes (which presumably handle their own cleanup or are stack-allocated), this is acceptable. However, if `UnitBaseEvent` were ever deleted via a base pointer, it would result in undefined behavior. Given the map shows no virtual functions, this is a deliberate design choice for performance/lightweight usage.

## Member Reference

**UnitBaseEvent**
Constructor that initializes the internal `iType` member with the provided `pType` argument. Sets the identity of the event based on the `UnitThreatEventType` bitmask.

**getType**
Returns the `uint32` value of `iType`. Used by `ThreatManager::processThreatEvent` to identify the specific event category and dispatch appropriate handling logic.

**matchesTypeMask**
Performs a bitwise AND between `iType` and the input `pMask`. Returns `true` if the event's type shares any bits with the mask, allowing for categorical filtering (e.g., checking if an event is a "reference" vs. "manager" event).

**setType**
Sets the internal `iType` member to the provided `pType`. Allows modification of the event's classification after construction.

---

<!-- machine-true, projected from graph.json -->

## Map — UnitBaseEvent

*Source:* UnitEvents.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| UnitBaseEvent | ctor | — | — | — |
| getType | method | — | ThreatManager/processThreatEvent | — |
| matchesTypeMask | method | — | — | — |
| setType | method | — | — | — |
