# PlayerRelocationNotifier

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# PlayerRelocationNotifier

**Purpose & Responsibilities**

`PlayerRelocationNotifier` is a visitor struct within the `MaNGOS` namespace, defined in `GridNotifiers.h` and implemented in `GridNotifiersImpl.h`. Its sole responsibility is to notify nearby `Creature` objects when a `Player` moves into their grid or changes position significantly enough to trigger proximity-based AI events.

It acts as a specialized worker for the server's spatial partitioning system (grids). When the grid manager detects that a player has entered a new grid cell or moved within one, it invokes this notifier. The notifier iterates over all creatures in that specific grid cell and triggers their Artificial Intelligence (AI) to evaluate whether the player is visible, detectable, or threatening, potentially initiating combat or stealth detection sequences.

This unit does not handle visibility updates for the client (that is handled by `VisibleNotifier`), nor does it handle general object updates (`ObjectUpdater`). It is strictly focused on the **AI reaction of Creatures to Player movement**.

## Member-by-Member Behavior

### `PlayerRelocationNotifier` (Constructor)
*   **Kind:** Constructor
*   **Behavior:** Initializes the notifier with a reference to the `Player` object (`i_player`) who has relocated. This reference is stored for use during the visitation process.
*   **Cross-Unit Boundary:** Called by `Unit.Main/Execute` (specifically, the grid management logic within the `Unit` class hierarchy when processing movement).

### `Visit` (Template Method)
*   **Kind:** Template Method
*   **Signature:** `template<class T> void Visit(GridRefManager<T>&)`
*   **Behavior:** This is a catch-all template method declared in `GridNotifiers.h`. It provides an empty implementation for any `GridRefManager` type `T` that is not explicitly specialized.
*   **Purpose:** In the visitor pattern used by MaNGOS, this ensures that if the notifier is passed a grid containing objects it doesn't care about (e.g., `GameObject`, `Corpse`, `DynamicObject`), it simply does nothing. This avoids the need for conditional checks inside a single loop over mixed object types.

### `Visit` (Specialized for CreatureMapType)
*   **Kind:** Specialized Method
*   **Signature:** `void Visit(CreatureMapType& m)`
*   **Implementation Location:** `GridNotifiersImpl.h`
*   **Behavior:**
    1.  **Pre-checks:** Immediately returns if the associated player (`i_player`) is dead (`!IsAlive()`) or is flying via taxi (`IsTaxiFlying()`). These states imply the player should not interact with ground-based creature AI in standard ways.
    2.  **Iteration:** Iterates through every `Creature` in the provided `CreatureMapType` (which represents the set of creatures in the current grid cell).
    3.  **Filtering:** For each creature, it checks if the creature is alive (`c->IsAlive()`). Dead creatures do not react to player movement.
    4.  **Notification:** If the creature is alive, it calls the free function `PlayerCreatureRelocationWorker(&i_player, c)`.
*   **Cross-Unit Boundary:**
    *   Calls `PlayerCreatureRelocationWorker` (defined in `GridNotifiersImpl.h`).
    *   `PlayerCreatureRelocationWorker` subsequently calls `CallAIMoveLOS`, which interacts with the `CreatureAI` interface (`c->AI()->MoveInLineOfSight` or `c->AI()->OnMoveInStealth`). This bridges the gap between the grid system and the specific AI logic of each creature.

### `Visit#2` (Declaration)
*   **Kind:** Declaration
*   **Note:** The MAP lists `Visit#2` as a declaration. In `GridNotifiers.h`, the specialized `Visit(CreatureMapType&)` is declared. The implementation resides in `GridNotifiersImpl.h`. There is no second distinct `Visit` method for `PlayerRelocationNotifier`; the template `Visit` and the specialized `Visit` constitute the complete interface. The MAP likely distinguishes the template declaration from the specialized declaration.

## Cross-Unit Boundaries

### Incoming Calls
*   **From:** `Unit.Main/Execute` (Grid Management Logic)
    *   **Context:** When a `Unit` (specifically a `Player`) moves, the server's grid system determines which grid cells the unit occupies. If the unit enters a new cell or moves significantly within one, the grid manager instantiates `PlayerRelocationNotifier` and passes it to the relevant grid containers.
    *   **Data Crossing:** The `Player` reference is passed into the constructor. The `CreatureMapType` (container of pointers to `Creature` objects in the grid) is passed into the `Visit` method.

### Outgoing Calls
*   **To:** `PlayerCreatureRelocationWorker` (Free Function in `GridNotifiersImpl.h`)
    *   **Context:** Called inside `Visit(CreatureMapType&)` for each alive creature.
    *   **Data Crossing:** Passes the `Player*` and `Creature*` pointers.
*   **To:** `CreatureAI` Interface (via `PlayerCreatureRelocationWorker` -> `CallAIMoveLOS`)
    *   **Context:** `CallAIMoveLOS` checks visibility and detection conditions. If met, it calls methods on the creature's AI object.
    *   **Methods Called:**
        *   `MoveInLineOfSight(Unit*)`: Triggered if the player is visible/detectable. This allows the creature's AI to decide whether to attack, flee, or ignore the player.
        *   `OnMoveInStealth(Unit*)`: Triggered if the player is stealthed but detected (e.g., by a high perception score or specific mechanics), allowing the AI to react to stealth breaks.

## Data Model

This unit does not access any database tables. It operates entirely on in-memory object references (`Player`, `Creature`) managed by the server's runtime state.

## Notable Implementation Details

1.  **Separation of Concerns:** `PlayerRelocationNotifier` is distinct from `CreatureRelocationNotifier`. The latter handles creatures moving relative to players and other creatures. `PlayerRelocationNotifier` only handles the player moving relative to creatures. This asymmetry exists because player movement is frequent and often triggers AI aggro checks, whereas creature movement might be handled differently depending on the creature's state (patrol, combat, etc.).
2.  **Taxi Flying Exclusion:** The check `i_player.IsTaxiFlying()` is critical. Players flying on taxis are typically considered "out of the world" for most interaction purposes. Notifying creatures would cause unnecessary CPU cycles and potentially incorrect AI behavior (e.g., a creature trying to pathfind to a flying player).
3.  **Dead Player Exclusion:** Dead players do not trigger AI reactions. This prevents corpses from accidentally aggroing mobs if they are dragged into a grid, unless specific corpse-related mechanics are handled elsewhere (which they are not in this notifier).
4.  **Worker Pattern:** The logic is delegated to `PlayerCreatureRelocationWorker` and then `CallAIMoveLOS`. This indirection allows for consistent handling of the "Move in Line of Sight" event regardless of whether it was triggered by a player moving or a creature moving. It centralizes the visibility/detection logic (`IsVisibleForOrDetect`) and the subsequent AI callback dispatch.
5.  **Stealth Handling:** The `CallAIMoveLOS` function specifically checks for `moving->HasStealthAura()` and an `alert` flag from `IsVisibleForOrDetect`. If a stealthed player is detected (alert=true), it calls `OnMoveInStealth` instead of `MoveInLineOfSight`. This allows AI implementations to distinguish between seeing a normal player and detecting a stealthed one, which may have different responses (e.g., immediate aggro vs. cautious approach).
6.  **Template Specialization:** The use of `template<class T> void Visit(GridRefManager<T>&)` with an empty body, combined with the explicit specialization for `CreatureMapType`, is a common C++ idiom in MaNGOS to implement the Visitor pattern efficiently. It avoids virtual function overhead and allows compile-time resolution of which object types are processed.

## Member Reference

**PlayerRelocationNotifier**
Constructor that initializes the notifier with a reference to the `Player` object (`i_player`) who has relocated. It is instantiated by the grid management system when a player moves into a new grid cell or moves significantly within one.

**Visit**
Template method `template<class T> void Visit(GridRefManager<T>&)` declared in `GridNotifiers.h`. It provides an empty implementation for any grid manager type `T` that is not explicitly specialized. This ensures that grids containing objects irrelevant to player relocation (such as Game Objects or Corpses) are ignored efficiently without runtime checks.

**Visit#2**
Refers to the specialized declaration `void Visit(CreatureMapType&)` in `GridNotifiers.h`. The implementation in `GridNotifiersImpl.h` iterates over all creatures in the grid. It skips dead players and players flying on taxis. For each alive creature in the grid, it calls `PlayerCreatureRelocationWorker`, which in turn triggers the creature's AI to evaluate visibility and detection of the player, potentially starting combat or stealth detection sequences.

---

<!-- machine-true, projected from graph.json -->

## Map — PlayerRelocationNotifier

*Source:* GridNotifiers.h, GridNotifiersImpl.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Visit | method | — | — | — |
| PlayerRelocationNotifier | ctor | — | Unit.Main/Execute | — |
| Visit#2 | decl | — | — | — |
