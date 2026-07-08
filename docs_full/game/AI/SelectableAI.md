# SelectableAI

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# SelectableAI

**Purpose & Responsibilities**

`SelectableAI` is a lightweight factory registration struct used within the `wowvmangos` engine to register specific `CreatureAI` implementations into the global AI registry. It serves as the base class for `CreatureAIFactory`, which provides the concrete instantiation logic for creature artificial intelligence objects.

The primary responsibility of `SelectableAI` is to hold a unique identifier string (`char const*`) that maps a creature entry or script name to a specific AI type. It inherits from `FactoryHolder<CreatureAI>`, providing the mechanism to store this ID, and from `Permissible<Creature>`, establishing the interface for determining whether a specific `Creature` instance is eligible to use the associated AI.

This unit contains no complex logic itself; it is a structural component of the dependency injection/factory pattern used to decouple creature data from their behavioral implementations.

## Member-by-Member Behavior

### **SelectableAI**
*Kind: Constructor*

This constructor initializes the `SelectableAI` object with a unique identifier string.

1.  **Parameter**: Takes a `char const* id`. This string typically corresponds to a script name or creature entry identifier used elsewhere in the server to look up the correct AI factory.
2.  **Initialization**: It delegates initialization to its base class `FactoryHolder<CreatureAI>` by calling `FactoryHolder<CreatureAI>(id)`. This stores the ID within the factory holder infrastructure, allowing the system to retrieve this specific factory by name later during creature spawning or AI assignment.
3.  **Base Classes**:
    *   Inherits from `FactoryHolder<CreatureAI>`: Provides the storage for the ID and integration with the global `CreatureAIRegistry`.
    *   Inherits from `Permissible<Creature>`: Establishes the contract that this factory can evaluate a `Creature` to determine if it is allowed to use this AI (via the `Permit` method, which is implemented in derived classes like `CreatureAIFactory`).

## Cross-Unit Boundaries

*   **Calls Out**: None. The constructor performs only base class initialization.
*   **Called By**: This unit is instantiated indirectly by the `CreatureAIFactory<REAL_AI>` template struct. When a developer defines a new AI class (e.g., `MyCustomAI`), they typically instantiate a `CreatureAIFactory<MyCustomAI>` with a name string. This triggers the `SelectableAI` constructor to register the factory under that name.

## Data Model

This unit does not interact with any database tables. It operates entirely in memory as part of the server's startup and runtime object creation process.

## Notable Implementation Details

*   **Template Dependency**: `SelectableAI` is rarely used directly in user code. It is almost exclusively used as the base for `CreatureAIFactory<REAL_AI>`. The `CreatureAIFactory` overrides the `Create` and `Permit` methods required by the factory and permissible interfaces.
*   **Identifier Uniqueness**: The `id` passed to the constructor must be unique within the `CreatureAIRegistry`. If two different AI factories are registered with the same ID, the behavior depends on the `FactoryHolder` implementation (typically the last one registered wins, or an error is logged, depending on the specific `FactoryHolder` policy in `Dynamic/FactoryHolder.h`).
*   **Permissibility Interface**: While `SelectableAI` inherits from `Permissible<Creature>`, it does not implement the `Permit` method. This is intentional; the concrete permission logic is deferred to the derived `CreatureAIFactory`, which calls `REAL_AI::Permissible(c)`. This allows each specific AI class to define its own eligibility criteria (e.g., checking creature family, level, or specific flags).

## Member Reference

**SelectableAI**
Constructor that takes a `char const* id` and passes it to the `FactoryHolder<CreatureAI>` base class. This registers the AI factory under the given identifier in the global registry. It also inherits the `Permissible<Creature>` interface, though the actual permission logic is implemented in derived classes.

---

<!-- machine-true, projected from graph.json -->

## Map — SelectableAI

*Source:* CreatureAI.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| SelectableAI | ctor | — | — | — |
