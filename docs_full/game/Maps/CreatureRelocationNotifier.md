# CreatureRelocationNotifier

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# CreatureRelocationNotifier

**Purpose & Responsibilities**

`CreatureRelocationNotifier` is a visitor struct within the `MaNGOS` namespace, designed to handle the side effects of a `Creature` changing its position in the world grid. It is part of the server's spatial indexing system, which divides the world into grids to optimize visibility and interaction checks.

When a creature moves, the server must notify nearby entities (Players and other Creatures) so they can update their AI states, such as detecting the moving creature in their line of sight or entering combat. `CreatureRelocationNotifier` encapsulates the logic to iterate over the relevant grid maps (`PlayerMapType` and `CreatureMapType`) and trigger these notifications. It ensures that dead creatures do not trigger notifications and that taxi-flying players are ignored, adhering to game mechanics where flying characters are generally invisible to ground-based detection routines.

**Member-by-Member Behavior**

The unit consists of a constructor and two overloaded `Visit` methods. These methods are invoked by the grid management system (specifically `Unit.Main/Execute` as noted in the MAP) when the creature's grid coordinates change or when the grid needs to refresh visibility for the creature.

1.  **Constructor (`CreatureRelocationNotifier`)**: Initializes the notifier with a reference to the `Creature` instance that has relocated. This reference (`i_creature`) is stored and used in all subsequent `Visit` calls to determine validity (alive status) and to pass to worker functions.
2.  **`Visit(PlayerMapType&)`**: This specialization handles interactions with players in the same grid cell. It iterates through all players in the map. For each player, it checks if the player is alive and not taxi-flying. If valid, it delegates to the `PlayerCreatureRelocationWorker` function (defined in `GridNotifiersImpl.h`), which in turn calls `CallAIMoveLOS`. This triggers the player's AI (if applicable, though primarily for NPCs) or the creature's AI to register the player's presence. Specifically, it allows the creature to detect the player if the player is visible, potentially initiating aggro or stealth detection.
3.  **`Visit(CreatureMapType&)`**: This specialization handles interactions with other creatures in the same grid cell. It iterates through all creatures. It skips the creature itself (`i_creature`) and any dead creatures. For valid targets, it calls `CreatureCreatureRelocationWorker`, which invokes `CallAIMoveLOS` in both directions (creature A sees B, and B sees A). This ensures mutual awareness between NPCs, crucial for group behaviors, assists, and aggro propagation among mobs.

**Cross-Unit Boundaries**

*   **Called by `Unit.Main/Execute`**: The grid system executes this notifier as part of the update loop when a creature's position changes. The `Unit` class (likely `Unit.cpp` or related grid handlers) instantiates `CreatureRelocationNotifier` and passes it to the grid's visitation mechanism.
*   **Calls into `GridNotifiersImpl.h` (Internal Logic)**: While technically in the same header file structure, the logic relies heavily on helper functions defined in `GridNotifiersImpl.h`:
    *   `PlayerCreatureRelocationWorker`: Bridges the gap between the visitor pattern and the specific AI notification logic.
    *   `CreatureCreatureRelocationWorker`: Similar bridge for creature-to-creature interactions.
    *   `CallAIMoveLOS`: The core function that checks visibility and calls `CreatureAI::MoveInLineOfSight` or `CreatureAI::OnMoveInStealth`. This creates a dependency on the `CreatureAI` interface.
*   **Calls into `Creature.AI`**: Through `CallAIMoveLOS`, the notifier indirectly calls virtual methods on the `CreatureAI` class associated with the involved creatures. This is the primary side effect: updating AI state based on spatial proximity.

**Data Model**

This unit does not interact with any database tables. It operates entirely on in-memory object states (`Creature`, `Player`, `GridRefManager`).

**Notable Implementation Details**

*   **Dead Creature Filtering**: Both `Visit` specializations immediately return if `!i_creature.IsAlive()`. This prevents dead bodies from triggering AI updates, which is efficient and logically correct.
*   **Taxi Flying Exclusion**: In `Visit(PlayerMapType&)`, players who are `IsTaxiFlying()` are skipped. This reflects the game mechanic where flying mounts often bypass ground-level aggro or visibility checks, or simply because the grid logic for flying zones might differ.
*   **Self-Exclusion**: In `Visit(CreatureMapType&)`, the code explicitly checks `if (c != &i_creature)` to avoid notifying the creature of its own movement, which would be redundant and potentially cause infinite loops or unnecessary processing.
*   **Mutual Notification**: `CreatureCreatureRelocationWorker` calls `CallAIMoveLOS` twice: once for `c1` seeing `c2`, and once for `c2` seeing `c1`. This ensures that if two creatures move into each other's range, both are aware of the other.
*   **Template Specialization**: The `Visit` methods are template specializations (`template<>`). This is necessary because the base `CreatureRelocationNotifier` class in `GridNotifiers.h` declares a generic template `Visit(GridRefManager<T>&)` that does nothing. The implementations in `GridNotifiersImpl.h` provide the actual behavior for specific map types (`PlayerMapType` and `CreatureMapType`).
*   **Compiler Specifics**: The header `GridNotifiers.h` uses `#ifdef _MSC_VER` to declare the `Visit<Player>` specialization explicitly for MSVC, while the implementation in `GridNotifiersImpl.h` uses `#ifndef WIN32` guards for the explicit template instantiation declarations. This suggests careful handling of template visibility across different compilers.

## Member Reference

**CreatureRelocationNotifier**
Constructs the notifier with a reference to the `Creature` that has moved. Stores this reference in `i_creature` for use during grid visits.

**Visit**
Two overloaded versions exist in this unit's implementation context:
1.  `Visit(PlayerMapType&)`: Iterates over players in the grid. Skips dead or taxi-flying players. Calls `PlayerCreatureRelocationWorker` to notify the creature's AI of the player's presence.
2.  `Visit(CreatureMapType&)`: Iterates over creatures in the grid. Skips the creature itself and dead creatures. Calls `CreatureCreatureRelocationWorker` to notify both creatures' AIs of each other's presence.

**Visit#2**
Placeholder entry corresponding to the second `Visit` method listed in the structural map; functionally identical to the `Visit(CreatureMapType&)` overload described above, representing the template specialization for creature-to-creature relocation notifications.

---

<!-- machine-true, projected from graph.json -->

## Map — CreatureRelocationNotifier

*Source:* GridNotifiers.h, GridNotifiersImpl.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Visit#2 | method | — | — | — |
| Visit | method | — | — | — |
| CreatureRelocationNotifier | ctor | — | Unit.Main/Execute | — |
