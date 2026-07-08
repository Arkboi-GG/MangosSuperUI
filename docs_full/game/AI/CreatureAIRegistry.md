# CreatureAIRegistry

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# CreatureAIRegistry

## Purpose & Responsibilities

`CreatureAIRegistry` provides the initialization routine required to register the server’s built-in creature AI and movement generator implementations with their respective factory systems. It ensures that concrete C++ classes (e.g., `BasicAI`, `RandomMovementGenerator`) are mapped to string identifiers or motion type constants, enabling the engine to instantiate appropriate behaviors for creatures during runtime. This unit contains no behavioral logic; it solely populates the factory registries at startup.

## Member-by-Member Behavior

### `Initialize`

The `Initialize` function registers nine creature AI types and four movement generator types. It does so by constructing temporary factory objects on the heap and invoking `RegisterSelf()` on each, which presumably transfers ownership to the global factory registries.

**Creature AI Registrations:**
Each registration binds a string name to a specific AI class via `CreatureAIFactory`:
- `"NullAI"` → `NullCreatureAI`
- `"BasicAI"` → `BasicAI`
- `"CritterAI"` → `CritterAI`
- `"GuardAI"` → `GuardAI`
- `"PetAI"` → `PetAI`
- `"TotemAI"` → `TotemAI`
- `"EventAI"` → `CreatureEventAI`
- `"PetEventAI"` → `PetEventAI`
- `"GuardEventAI"` → `GuardEventAI`

**Movement Generator Registrations:**
Each registration binds an integer motion type constant to a specific generator class via `MovementGeneratorFactory`:
- `RANDOM_MOTION_TYPE` → `RandomMovementGenerator`
- `WAYPOINT_MOTION_TYPE` → `WaypointMovementGenerator<Creature>`
- `CYCLIC_MOTION_TYPE` → `CyclicMovementGenerator<Creature>`
- `PATROL_MOTION_TYPE` → `PatrolMovementGenerator`

## Cross-Unit Boundaries

### Called By: `World/SetInitialWorldSettings`
*   **Direction:** Inbound
*   **Collaboration:** The `World` unit calls `AIRegistry::Initialize()` during server startup (`SetInitialWorldSettings`). This timing ensures that all standard AI and movement factories are populated before any creatures are spawned or loaded, preventing instantiation failures for default behaviors.

### Calls Out: Factory Templates & AI Classes
*   **Direction:** Outbound
*   **Collaboration:** `Initialize` constructs instances of `CreatureAIFactory` and `MovementGeneratorFactory` (defined in `CreatureAIImpl.h` and `MovementGeneratorImpl.h` respectively). It also includes headers for all registered AI classes (`NullCreatureAI.h`, `BasicAI.h`, etc.) and movement generators (`RandomMovementGenerator.h`, etc.) to satisfy template instantiation requirements. The `RegisterSelf()` method on these factory objects handles the internal registration logic.

## Data Model

This unit does not interact with any database tables.

## Notable Implementation Details

1.  **Heap Allocation & Ownership:** The code uses raw `new` to create factory objects: `(new CreatureAIFactory<...>("..."))->RegisterSelf();`. The returned pointers are discarded. This implies that `RegisterSelf()` takes ownership of the factory instance, storing it in a global or static registry managed by the factory system. If ownership were not transferred, this would constitute a memory leak, though one limited to startup time.
2.  **Hardcoded Identifiers:** The string names (e.g., `"BasicAI"`) and motion type constants are hardcoded. Any external configuration (scripts, database entries) referencing these AI types must match these exact strings and constants.
3.  **Namespace Isolation:** All symbols reside in the `AIRegistry` namespace to avoid global namespace collisions.

## Member Reference

**Initialize**: Registers nine built-in creature AI types (`NullAI`, `BasicAI`, `CritterAI`, `GuardAI`, `PetAI`, `TotemAI`, `EventAI`, `PetEventAI`, `GuardEventAI`) and four movement generator types (`Random`, `Waypoint`, `Cyclic`, `Patrol`) with their respective factory systems by creating factory instances on the heap and calling `RegisterSelf()` on them.

---

<!-- machine-true, projected from graph.json -->

## Map — CreatureAIRegistry

*Source:* CreatureAIRegistry.cpp, CreatureAIRegistry.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Initialize | function | — | World/SetInitialWorldSettings | — |
