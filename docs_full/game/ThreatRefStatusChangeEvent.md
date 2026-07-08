# ThreatRefStatusChangeEvent

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`ThreatRefStatusChangeEvent` is a lightweight data carrier within the `wowvmangos` threat management subsystem. It encapsulates state changes for a specific `HostileReference` (a player or pet on a creature's aggro list). Inheriting from `UnitBaseEvent`, it carries an event type bitmask and a payload stored in a C-style `union` (`float`, `int32`, or `bool`). This allows `ThreatManager` to handle diverse updates—such as threat modifications, online status changes, or removals—through a uniform interface. The class is passive; it holds pointers to the affected `HostileReference` and the owning `ThreatManager`, populated by `ThreatManager` methods and consumed by `ThreatManager::processThreatEvent`.

## Member-by-Member Behavior

### Construction
Four constructors initialize the event with a `UnitThreatEventType` and optional payload. All set `iThreatManager` to `nullptr` initially.
*   **No-arg payload**: Initializes only the type. Unused by mapped callers.
*   **Reference-only**: Initializes type and `HostileReference*`. Used by `ThreatManager::removeReference`, `ThreatManager::setAccessibleState`, and `ThreatManager::setOnlineOfflineState` for binary or null-state changes.
*   **Float payload**: Initializes type, reference, and `float`. Used by `ThreatManager::addThreat` to pass threat magnitude.
*   **Bool payload**: Initializes type, reference, and `bool`. Unused by mapped callers.

### Accessors & Mutators
*   **`getIValue`**, **`getFValue`**, **`getBValue`**: Retrieve the union payload as `int32`, `float`, or `bool`. Only `getFValue` is called by `ThreatManager::processThreatEvent`.
*   **`setBValue`**: Sets the boolean payload. Unused by mapped callers.
*   **`getReference`**: Returns the `HostileReference*`. Called by `ThreatManager::processThreatEvent` to identify the target.
*   **`setThreatManager`**: Injects the `ThreatManager*` pointer. Called by `ThreatManager::processThreatEvent` to provide context.
*   **`GetThreatManager`**: Returns the stored `ThreatManager*`. Unused by mapped callers.

## Cross-Unit Boundaries

The class interacts exclusively with `ThreatManager`:
*   **Creation**: `ThreatManager` constructs events via `addThreat` (float payload) or `removeReference`/`setAccessibleState`/`setOnlineOfflineState` (reference-only).
*   **Processing**: `ThreatManager::processThreatEvent` consumes the event, calling `getReference` and `getFValue`, and injecting itself via `setThreatManager`.

## Data Model

This unit operates entirely in memory. It does not touch any database tables.

## Notable Implementation Details

1.  **Union Safety**: The payload is a raw `union`. Reading a different type than written yields undefined behavior. Callers must track the type (e.g., `addThreat` writes `float`, `processThreatEvent` reads `float`).
2.  **Context Injection**: `ThreatManager` sets its own pointer on the event during processing, enabling callbacks or further logic to access the manager’s state.
3.  **Unused Members**: The boolean constructor, `setBValue`, `getIValue`, `getBValue`, and `GetThreatManager` are defined but not used by mapped callers, suggesting legacy code or reserved capacity.

## Member Reference

**ThreatRefStatusChangeEvent** (ctor): Initializes event type; reference and manager are null. Unused by mapped units.

**ThreatRefStatusChangeEvent#2** (ctor): Initializes event type and `HostileReference*`. Called by `ThreatManager::removeReference`, `ThreatManager::setAccessibleState`, and `ThreatManager::setOnlineOfflineState`.

**ThreatRefStatusChangeEvent#4** (ctor): Initializes event type, `HostileReference*`, and `float`. Called by `ThreatManager::addThreat`.

**ThreatRefStatusChangeEvent#3** (ctor): Initializes event type, `HostileReference*`, and `bool`. Unused by mapped units.

**getIValue**: Returns union as `int32`. Unused by mapped units.

**getFValue**: Returns union as `float`. Called by `ThreatManager::processThreatEvent`.

**getBValue**: Returns union as `bool`. Unused by mapped units.

**setBValue**: Sets union as `bool`. Unused by mapped units.

**getReference**: Returns `HostileReference*`. Called by `ThreatManager::processThreatEvent`.

**setThreatManager**: Sets `ThreatManager*`. Called by `ThreatManager::processThreatEvent`.

**GetThreatManager**: Returns `ThreatManager*`. Unused by mapped units.

---

<!-- machine-true, projected from graph.json -->

## Map — ThreatRefStatusChangeEvent

*Source:* UnitEvents.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ThreatRefStatusChangeEvent | ctor | — | — | — |
| ThreatRefStatusChangeEvent#2 | ctor | — | ThreatManager/removeReference, ThreatManager/setAccessibleState, ThreatManager/setOnlineOfflineState | — |
| ThreatRefStatusChangeEvent#4 | ctor | — | ThreatManager/addThreat | — |
| ThreatRefStatusChangeEvent#3 | ctor | — | — | — |
| getIValue | method | — | — | — |
| getFValue | method | — | ThreatManager/processThreatEvent | — |
| getBValue | method | — | — | — |
| setBValue | method | — | — | — |
| getReference | method | — | ThreatManager/processThreatEvent | — |
| setThreatManager | method | — | ThreatManager/processThreatEvent | — |
| GetThreatManager | method | — | — | — |
