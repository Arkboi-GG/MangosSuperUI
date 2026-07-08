<!-- provenance: failed-members -->
# RespawnDo

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# RespawnDo

**Purpose & Responsibilities**

`RespawnDo` is a functor class defined in `GridNotifiers.h` within the `MaNGOS` namespace. It implements the "Do" half of the Visitor pattern used by the MaNGOS grid system to perform actions on objects within a specific spatial grid cell. Specifically, `RespawnDo` is designed to trigger the respawn process for dead `Creature`s and inactive `GameObject`s.

It is primarily instantiated and invoked by administrative commands (specifically `ChatHandler.CreatureCommands/HandleRespawnCommand`) to manually force the resurrection of entities that are currently despawned or dead, bypassing normal respawn timers or conditions.

**Member-by-Member Behavior**

The class contains three primary members: a constructor and two overloads of the `operator()` method, plus implicit default behavior for other types.

1.  **`RespawnDo` (Constructor)**
    *   Initializes the functor. It takes no arguments, relying on default initialization. This simplicity allows it to be instantiated easily within command handlers without needing to pass context-specific data, as the action (respawn) is self-contained within the target object's methods.

2.  **`operator()(Creature* u)`**
    *   This overload handles `Creature` objects. When invoked on a creature pointer `u`, it triggers the creature's internal respawn logic. In the context of MaNGOS, this typically involves resetting the creature's health, removing death flags, and potentially re-enabling its AI if it was disabled upon death. The exact behavior depends on the `Creature::Respawn()` implementation (not shown here, but standard for the engine), which ensures the creature becomes active and visible again.

3.  **`operator()(GameObject* u)`**
    *   This overload handles `GameObject` objects. When invoked on a game object pointer `u`, it triggers the game object's respawn logic. This usually involves changing the game object's state from "inactive" or "closed" back to its default active state (e.g., opening a door, re-enabling a quest giver, or respawning a resource node).

4.  **`operator()(WorldObject*)` and `operator()(Corpse*)`**
    *   These are catch-all overloads for other `WorldObject` derivatives (like `Player`, `DynamicObject`) and `Corpse` objects. They are defined as empty functions (`{}`). This ensures that if the grid iterator passes a non-target type (e.g., a player or a corpse) to the `RespawnDo` functor, no action is taken, preventing errors or unintended side effects on objects that cannot or should not be "respawned" in this context.

**Cross-Unit Boundaries**

*   **Called By:** `ChatHandler.CreatureCommands/HandleRespawnCommand`
    *   **Direction:** Inbound (Other unit calls `RespawnDo`).
    *   **Collaboration:** The `ChatHandler` module, responsible for processing console and chat commands, instantiates `RespawnDo` when an administrator issues a respawn command (likely `/respawn` or similar). The handler identifies the target entity (creature or game object) and uses the grid system to apply the `RespawnDo` functor to that entity. This decouples the command parsing logic from the actual entity manipulation logic, adhering to the engine's design principle of using functors for grid-based operations.

*   **Calls Out:** None.
    *   `RespawnDo` does not directly call into other units listed in the map. Its work is delegated entirely to the methods of the `Creature` and `GameObject` classes passed to it via `operator()`.

**Data Model**

This unit does not interact directly with any database tables. It operates purely on in-memory object states (`Creature` and `GameObject` instances). Any persistence changes resulting from a respawn (such as updating respawn times in the database) would be handled internally by the `Creature` or `GameObject` classes themselves, not by `RespawnDo`.

**Notable Implementation Details**

*   **Functor Pattern:** `RespawnDo` is a classic example of the Functor pattern in C++. By overloading `operator()`, it acts like a function but can hold state (though it holds none in this simple case). This allows it to be passed to generic grid iteration algorithms that expect a callable object.
*   **Type Safety via Overloading:** The class provides specific implementations for `Creature` and `GameObject` while providing empty no-op implementations for `WorldObject` and `Corpse`. This ensures type safety during grid traversal; if the grid contains mixed types, only the relevant ones are affected.
*   **Const Correctness:** Both `operator()` overloads are marked `const`, indicating that invoking them does not modify the state of the `RespawnDo` functor itself. This is important for allowing the functor to be used in contexts that require const-correctness, such as certain STL algorithms or const grid iterators.
*   **No Side Effects on Non-Targets:** The empty bodies for `WorldObject*` and `Corpse*` ensure that accidental inclusion of these types in the grid query does not cause crashes or undefined behavior.

## Member Reference

**RespawnDo**
Constructor for the `RespawnDo` functor. Takes no arguments. Initializes the object for use in grid-based respawn operations.

**operator()#2**
Overload of `operator()` for `GameObject*` arguments. Triggers the respawn logic for the specified game object, restoring it to its active state.

**operator()**
Overload of `operator()` for `Creature*` arguments. Triggers the respawn logic for the specified creature, restoring it to life and activity. Note: There are also implicit no-op overloads for `WorldObject*` and `Corpse*` defined in the class body, which do nothing.

---

<!-- machine-true, projected from graph.json -->

## Map — RespawnDo

*Source:* GridNotifiers.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| RespawnDo | ctor | — | ChatHandler.CreatureCommands/HandleRespawnCommand | — |
| operator()#2 | method | — | — | — |
| operator() | method | — | — | — |

---

<!-- verify: failed-members | invented: operator -->
