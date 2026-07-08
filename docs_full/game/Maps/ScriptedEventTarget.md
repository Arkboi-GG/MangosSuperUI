# ScriptedEventTarget

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ScriptedEventTarget

**Purpose & Responsibilities**

`ScriptedEventTarget` is a lightweight data structure (struct) defined within `Map.h` that represents a secondary participant in a complex, database-driven scripted event. While the primary `ScriptedEvent` struct tracks a main source and target object, `ScriptedEventTarget` allows scripts to register additional objects (such as multiple NPCs, game objects, or players) that must be notified when the event succeeds or fails.

Each `ScriptedEventTarget` holds:
1.  The GUID of the target object.
2.  Condition IDs and Script IDs to execute if the overall event **fails**.
3.  Condition IDs and Script IDs to execute if the overall event **succeeds**.

This structure enables flexible event orchestration where different outcomes trigger different follow-up scripts for different participants. It is used exclusively by the `ScriptedEvent` struct, which maintains a `std::vector<ScriptedEventTarget>` named `m_vTargets`.

**Member-by-Member Behavior**

The unit consists of a single constructor.

**Cross-Unit Boundaries**

*   **Called by:** `ScriptedEvent.AddOrUpdateExtraTarget` (defined in `Map.h`, part of the `ScriptedEvent` struct).
    *   **Collaboration:** When a script adds or updates an extra target for a map event, `ScriptedEvent.AddOrUpdateExtraTarget` creates a new `ScriptedEventTarget` instance via this constructor and appends it to the `m_vTargets` vector.
*   **Calls out:** None. This is a pure data holder with no dependencies on other units.

**Data Model**

This unit does not interact directly with database tables. It is populated dynamically during runtime by script commands (e.g., `ScriptCommand_AddMapEventTarget`) which interpret data from database-defined scripts.

**Notable Implementation Details**

*   **Immutability of Target GUID:** Once constructed, the `target` GUID cannot be changed. If a script needs to update the conditions or scripts associated with an existing target, `ScriptedEvent.AddOrUpdateExtraTarget` iterates through the vector, finds the matching GUID, and updates the condition/script fields directly on the existing `ScriptedEventTarget` instance. If the GUID is new, a new `ScriptedEventTarget` is constructed and emplaced.
*   **No Validation:** The constructor performs no validation on the `ObjectGuid` or the script/condition IDs. Invalid GUIDs or non-existent script IDs are stored as-is, potentially leading to silent failures or errors later when `ScriptedEvent` attempts to resolve these IDs during event resolution.

## Member Reference

**ScriptedEventTarget**
Constructor that initializes the `target` GUID and the four uint32 fields for failure/success conditions and scripts. It takes five arguments: `object` (ObjectGuid), `failureCondition` (uint32), `failureScript` (uint32), `successCondition` (uint32), and `successScript` (uint32). These are assigned directly to the member variables `target`, `uiFailureCondition`, `uiFailureScript`, `uiSuccessCondition`, and `uiSuccessScript` respectively.

---

<!-- machine-true, projected from graph.json -->

## Map — ScriptedEventTarget

*Source:* Map.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ScriptedEventTarget | ctor | — | — | — |
