# SelectableMovement

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# SelectableMovement

**Purpose & Responsibilities**

`SelectableMovement` is a lightweight structural base class within the `wowvmangos` movement system. Its sole responsibility is to serve as a common ancestor for factory-based registration of `MovementGenerator` implementations. It inherits from `FactoryHolder<MovementGenerator, MovementGeneratorType>`, enabling the runtime creation of specific movement generator instances based on a `MovementGeneratorType` identifier.

This class does not contain any behavioral logic, state, or virtual methods of its own. It exists purely to define the interface contract required by the `MovementGeneratorFactory` template, which implements the actual object creation logic. By inheriting from `SelectableMovement`, `MovementGeneratorFactory` ensures that all registered movement generators are part of a unified registry managed by the `FactoryHolder` infrastructure.

## Member-by-Member Behavior

The unit contains only one member: the constructor.

### Construction

**`SelectableMovement`**
The constructor accepts a `MovementGeneratorType` enum value. It immediately delegates this value to the base class constructor of `FactoryHolder<MovementGenerator, MovementGeneratorType>`. This registration step associates the specific movement type with the factory instance, allowing the system to look up and instantiate the correct `MovementGenerator` subclass later during gameplay (e.g., when an NPC needs to start patrolling or chasing).

## Cross-Unit Boundaries

*   **Calls Out:** None. The constructor only invokes the base class constructor.
*   **Called By:** `MovementGeneratorFactory` (defined in the same header). `MovementGeneratorFactory` inherits from `SelectableMovement` and passes the `MovementGeneratorType` to this constructor via its own initializer list. This establishes the link between a specific concrete movement generator class (like `WaypointMovementGenerator`) and its abstract type ID.

## Data Model

This unit does not interact with any database tables. It operates entirely in memory as part of the object lifecycle management for movement logic.

## Notable Implementation Details

*   **Inheritance Chain:** `SelectableMovement` sits between `FactoryHolder` and `MovementGeneratorFactory`. This design allows the `FactoryHolder` base to manage the registry of factories, while `SelectableMovement` provides a named, distinct type for the intermediate layer.
*   **No Virtual Functions:** Unlike `MovementGenerator`, which defines the full lifecycle interface (`Initialize`, `Update`, `Finalize`, etc.), `SelectableMovement` has no virtual functions. It is a pure data-carrying wrapper for the factory registration mechanism.
*   **Template Dependency:** While `SelectableMovement` itself is not a template, it is tightly coupled with the `MovementGeneratorFactory` template, which uses it as a base. This pattern ensures that every registered movement generator type is properly typed and registered in the global `MovementGeneratorRegistry`.

## Member Reference

**`SelectableMovement`**
Constructor that takes a `MovementGeneratorType` and forwards it to the `FactoryHolder<MovementGenerator, MovementGeneratorType>` base class constructor to register the movement type for factory-based instantiation.

---

<!-- machine-true, projected from graph.json -->

## Map — SelectableMovement

*Source:* MovementGenerator.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| SelectableMovement | ctor | — | — | — |
