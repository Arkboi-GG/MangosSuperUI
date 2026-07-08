# ScriptedEvent

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ScriptedEvent

## Purpose & Responsibilities

`ScriptedEvent` is a lightweight, transient data structure used within the `Map` class to manage complex, time-bound scripted interactions in the WoWVMaNGOS server. It acts as a container for stateful events triggered by database-defined scripts (via `ScriptCommands`).

Its primary responsibilities are:
1.  **State Management:** Holding arbitrary key-value integer data (`m_mData`) that persists for the duration of the event, allowing scripts to track progress, counts, or flags.
2.  **Target Tracking:** Maintaining a primary source and target object, plus a list of "extra" targets. Each target can have independent success/failure conditions and associated scripts.
3.  **Lifecycle Control:** Managing an expiration timer and providing mechanisms to explicitly end the event with a success or failure status, which triggers subsequent script actions.

It is designed for scenarios where a sequence of actions needs to be coordinated across multiple entities over a period of time, such as a boss mechanic requiring multiple players to interact with objects before a timeout occurs.

## Member-by-Member Behavior

### Construction and Initialization

**`ScriptedEvent` (Constructor)**
Initializes the event with a unique ID, source/target GUIDs, a reference to the owning `Map`, an expiration timestamp, and initial success/failure condition/script IDs. It sets the internal ended flag (`m_bEnded`) to false.

### Target Management

**`SetSourceObject`**
Updates the event's source object GUID. It performs safety checks: the provided `WorldObject` must exist, be in the world, and belong to the same `Map` instance as the event. If valid, it updates `m_Source`.

**`SetTargetObject`**
Updates the event's primary target object GUID. Similar to `SetSourceObject`, it validates that the object is alive and on the correct map before updating `m_Target`.

**`AddOrUpdateExtraTarget`**
Adds a new secondary target to the event or updates an existing one.
1.  Validates the object is alive and on the correct map.
2.  Iterates through `m_vTargets` to see if a target with the same GUID already exists.
3.  If found, it updates the failure/success conditions and script IDs for that existing target.
4.  If not found, it appends a new `ScriptedEventTarget` struct to `m_vTargets`.

### Data Access

**`GetData`**
Retrieves an integer value associated with a specific index from the internal `m_mData` map. Returns `0` if the index does not exist.

**`SetData`**
Sets an integer value for a specific index in `m_mData`. Creates the entry if it doesn't exist.

**`IncrementData`**
Increments the value at a specific index by a given amount. If the index doesn't exist, it initializes it to the increment value (since default initialization of `uint32` in a map insert is 0, effectively adding to 0).

**`DecrementData`**
Decrements the value at a specific index by a given amount. Includes underflow protection: if the current value is less than the decrement amount, it clamps the result to `0` instead of wrapping around.

### Lifecycle and Status

**`ScriptedEvent#2` (Copy Constructor Declaration)**
The copy constructor is explicitly deleted (`= delete`). This prevents accidental copying of `ScriptedEvent` instances, ensuring that each event is uniquely identified by its pointer or ID within the `Map`'s storage. This is critical because `ScriptedEvent` holds references to `Map` and manages dynamic memory via containers.

## Cross-Unit Boundaries

`ScriptedEvent` is tightly coupled with the `Map` class and the scripting system.

*   **Called by `Map.ScriptCommands/ScriptCommand_AddMapEventTarget`:**
    The `AddOrUpdateExtraTarget` method is invoked by this script command handler in `Map`. This allows database scripts to dynamically add participants to an ongoing event. The script command passes the target object and condition/script IDs, which `ScriptedEvent` stores.

*   **Called by `Map.ScriptCommands/ScriptCommand_SetMapEventData`:**
    The `SetData`, `IncrementData`, and `DecrementData` methods are invoked by this script command handler. This enables scripts to manipulate the event's internal state variables during execution.

*   **Called by `Conditions/Evaluate`:**
    The `GetData` method is called by the condition evaluation system. This allows conditional checks in scripts to read the current state of the event (e.g., "Has the counter reached 5?").

*   **Owned by `Map`:**
    While not shown as a direct "Calls out" in the map, `ScriptedEvent` instances are stored in `Map::m_mScriptedEvents`. The `Map` class creates them via `StartScriptedEvent` and iterates over them in `UpdateScriptedEvents` to check for expiration and trigger end-of-event scripts.

## Data Model

`ScriptedEvent` does not directly interact with any database tables. Its data is transient and exists only in memory for the duration of the event. The configuration for these events (IDs, conditions, scripts) is typically defined in database tables like `areatrigger_scripts` or `smart_scripts`, but `ScriptedEvent` itself reads no tables and writes no tables.

## Notable Implementation Details

1.  **Underflow Protection in `DecrementData`:**
    The `DecrementData` method explicitly checks if `m_mData[uiIndex] < uiValue` before subtracting. This prevents unsigned integer underflow, which would otherwise wrap to a very large number. This is a critical safeguard for game logic relying on counters.

2.  **Map Validation in Target Setters:**
    `SetSourceObject`, `SetTargetObject`, and `AddOrUpdateExtraTarget` all verify that the provided `WorldObject` is on the same `Map` instance (`pObject->GetMap() == &m_Map`). This ensures that events cannot accidentally link to objects on different maps (e.g., a player in a dungeon trying to target an NPC in the open world), which would lead to invalid state or crashes.

3.  **No Direct Notification Mechanism:**
    `ScriptedEvent` itself does not send packets or notify players. It only holds state. The actual notification (e.g., sending a message to targets) is handled by the `Map` class methods like `SendEventToAllTargets`, which iterate over the targets stored in `ScriptedEvent`. This separation keeps `ScriptedEvent` as a pure data holder.

4.  **Deleted Copy Constructor:**
    The explicit deletion of the copy constructor (`ScriptedEvent(ScriptedEvent const&) = delete`) enforces unique ownership. Since `ScriptedEvent` is stored in a `std::map` keyed by ID, copying would create duplicate IDs and break the lookup mechanism.

## Member Reference

**`ScriptedEvent`**
Constructor that initializes the event with ID, source/target GUIDs, map reference, expiration time, and initial success/failure conditions. Sets `m_bEnded` to false.

**`SetSourceObject`**
Updates the source object GUID if the provided object is valid, in-world, and on the same map.

**`SetTargetObject`**
Updates the primary target object GUID if the provided object is valid, in-world, and on the same map.

**`AddOrUpdateExtraTarget`**
Adds a new extra target or updates an existing one in `m_vTargets` with new success/failure conditions and scripts. Validates object presence on the map.

**`GetData`**
Returns the integer value associated with a given index from `m_mData`, or 0 if not found. Used by condition evaluators.

**`SetData`**
Sets the integer value for a given index in `m_mData`.

**`IncrementData`**
Increments the value at a given index in `m_mData` by the specified amount.

**`DecrementData`**
Decrements the value at a given index in `m_mData` by the specified amount, clamping to 0 to prevent underflow.

**`ScriptedEvent#2`**
Declares the copy constructor as deleted, preventing copying of `ScriptedEvent` instances.

---

<!-- machine-true, projected from graph.json -->

## Map — ScriptedEvent

*Source:* Map.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ScriptedEvent | ctor | — | — | — |
| SetSourceObject | method | — | — | — |
| SetTargetObject | method | — | — | — |
| AddOrUpdateExtraTarget | method | — | Map.ScriptCommands/ScriptCommand_AddMapEventTarget | — |
| GetData | method | — | Conditions/Evaluate | — |
| SetData | method | — | Map.ScriptCommands/ScriptCommand_SetMapEventData | — |
| IncrementData | method | — | Map.ScriptCommands/ScriptCommand_SetMapEventData | — |
| DecrementData | method | — | Map.ScriptCommands/ScriptCommand_SetMapEventData | — |
| ScriptedEvent#2 | decl | — | — | — |
