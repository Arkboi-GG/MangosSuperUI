# ThreatListProcesser

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ThreatListProcesser

## Purpose & Responsibilities

`ThreatListProcesser` is an abstract base class (interface) defined in `Creature.h` that implements the **Strategy Pattern** for iterating over and processing a creature's threat list (aggro list). It allows the `Creature` class to traverse its internal list of hostile units without exposing the list's internal structure or coupling the iteration logic to specific actions.

The primary responsibility of `ThreatListProcesser` is to define a contract (`Process`) that derived classes must implement. These derived classes encapsulate specific logic—such as selecting a target based on distance, threat value, or spell immunity—and return a boolean indicating whether the iteration should continue or terminate early. This design enables `Creature` to perform complex target selection queries (e.g., "find the nearest hostile caster") by passing different strategy objects to a single generic traversal method.

## Member-by-Member Behavior

### Construction and Destruction

*   **`ThreatListProcesser()`**: The default constructor. It performs no initialization, as the class contains no state.
*   **`~ThreatListProcesser()`**: The virtual destructor. It is declared virtual to ensure proper cleanup when deleting a derived class instance through a base class pointer, although the class itself holds no resources.

### Core Interface

*   **`Process(Unit* unit)`**: This is the pure virtual function that defines the interface. Derived classes must implement this method to specify how a single `Unit` in the threat list should be evaluated.
    *   **Parameter**: `unit` is a pointer to a `Unit` currently being iterated over from the creature's threat list.
    *   **Return Value**: Returns a `bool`. In the context of typical iterator patterns in this codebase, returning `true` usually signals that the operation is complete (e.g., a target was found) and the loop should stop, while `false` indicates the iteration should continue to the next unit. The exact semantics depend on the caller's implementation in `Creature::ProcessThreatList`.

## Cross-Unit Boundaries

### Collaboration with `Creature`

*   **Called By**: `Creature.Main/ProcessThreatList` (specifically `Creature::ProcessThreatList`).
    *   **Direction**: `Creature` calls into `ThreatListProcesser`.
    *   **Mechanism**: The `Creature` class maintains a list of units it is in combat with (the threat list). When `Creature::ProcessThreatList` is invoked, it accepts a pointer to a `ThreatListProcesser` instance. It iterates through its internal threat list, calling `Process(unit)` on the provided strategy object for each unit.
    *   **Why**: This decouples the storage and management of the threat list (owned by `Creature`) from the logic used to query or manipulate that list. For example, `Creature` might need to find the "nearest victim" for one spell and the "farthest victim" for another. Instead of writing two separate loops in `Creature`, it uses the same `ProcessThreatList` method with two different `ThreatListProcesser` derivatives.

## Data Model

This unit does not interact with any database tables. It operates entirely on in-memory `Unit` objects passed during runtime combat processing.

## Notable Implementation Details

*   **Abstract Nature**: The class is purely abstract (`virtual ... = 0`). It cannot be instantiated directly. Any usage requires a derived class that implements `Process`.
*   **Polymorphic Dispatch**: Because `Process` is virtual, the specific behavior is determined at runtime by the type of the derived object passed to `Creature::ProcessThreatList`. This allows for flexible, open-closed principle-compliant extensions where new targeting strategies can be added without modifying `Creature`.
*   **No State**: The base class holds no member variables. Any state required for the processing logic (e.g., storing the best candidate found so far, tracking minimum/maximum distances) must be implemented in the derived classes.

## Member Reference

*   **ThreatListProcesser**: Default constructor for the abstract base class. Initializes nothing.
*   **~ThreatListProcesser**: Virtual destructor ensuring safe deletion of derived instances via base pointers.
*   **Process**: Pure virtual method defining the strategy interface. Takes a `Unit*` and returns a `bool` to control iteration flow. Must be implemented by derived classes.

---

<!-- machine-true, projected from graph.json -->

## Map — ThreatListProcesser

*Source:* Creature.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ThreatListProcesser | ctor | — | — | — |
| ~ThreatListProcesser | dtor | — | — | — |
| Process | decl | — | Creature.Main/ProcessThreatList | — |
