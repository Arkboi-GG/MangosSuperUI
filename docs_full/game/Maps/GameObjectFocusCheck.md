<!-- provenance: failed-members -->
# GameObjectFocusCheck

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# GameObjectFocusCheck

**Purpose & Responsibilities**

`GameObjectFocusCheck` is a predicate functor defined in `GridNotifiers.h` within the `MaNGOS` namespace. It serves as a specialized filter for spatial searches involving `GameObject` instances. Specifically, it identifies game objects that act as "Spell Focuses" (e.g., ritual circles, portals, or magical anchors) required by certain spells.

The class encapsulates the logic to verify if a candidate `GameObject` matches a specific `focusId` and is within the operational range defined by the object's own static data. It is designed to be passed to generic grid searchers (such as `GameObjectSearcher` or `GameObjectListSearcher`) to efficiently locate valid targets for spell resolution without embedding search criteria directly into the spell handling logic.

**Member-by-Member Behavior**

*   **Constructor (`GameObjectFocusCheck`)**: Initializes the predicate with two arguments:
    *   `caster`: A `const WorldObject*` representing the entity attempting to use the focus (typically the spell caster). This object serves as the origin for distance calculations.
    *   `focusId`: A `uint32` value specifying the unique identifier of the required spell focus. This ID is compared against the configuration of candidate game objects.

*   **`GetFocusObject`**: Returns a constant reference to the `caster` object stored during construction. This method satisfies the interface contract expected by many grid searchers, allowing them to access properties of the initiating object (such as phase mask or position) if necessary for broader filtering or logging.

*   **`operator()`**: The core evaluation logic. It accepts a `GameObject*` and returns `true` if and only if all the following conditions are satisfied:
    1.  **Type Verification**: The game object's type is `GAMEOBJECT_TYPE_SPELL_FOCUS`. Objects of other types are immediately rejected.
    2.  **Spawn Status**: The game object must be currently spawned (`go->isSpawned()`). Despawned or inactive objects are ignored.
    3.  **ID Matching**: The `focusId` stored in the game object's static information (`goInfo->spellFocus.focusId`) must exactly match the `i_focusId` provided to the constructor.
    4.  **Proximity Check**: The game object must be within a specific distance of the `caster`. Crucially, this distance threshold is not fixed by the spell or the checker; it is retrieved from the game object's static configuration (`goInfo->spellFocus.dist`). The check uses `IsWithinDistInMap`, ensuring the calculation respects map boundaries.

**Cross-Unit Boundaries**

*   **Called by `Spell.Main/CheckItems`**: The map indicates that `GameObjectFocusCheck` is instantiated and utilized by the `Spell` module, specifically within the `CheckItems` logic or related target-finding routines. The `Spell` system creates an instance of this predicate with the current caster and the required focus ID, then passes it to a grid searcher. This decouples the spell engine from the specific mechanics of locating focus objects, promoting reusability.
*   **Calls Out**: The members of `GameObjectFocusCheck` do not call out to other external units as defined in the map. They rely on methods from `GameObject`, `GameObjectInfo`, and `WorldObject` (e.g., `GetGOInfo`, `isSpawned`, `IsWithinDistInMap`), which are part of the core object hierarchy and not considered cross-unit boundaries in this context.

**Data Model**

This unit does not interact directly with database tables. It operates entirely on in-memory representations of game objects. The `focusId` and `dist` values used in the comparison are loaded from the database into the `GameObjectInfo` structure at server startup or object creation, but `GameObjectFocusCheck` only reads these pre-loaded values via the `GameObject` API.

**Notable Implementation Details**

*   **Dynamic Range Source**: The maximum allowed distance for the check is derived from the target `GameObject`'s static data (`goInfo->spellFocus.dist`) rather than being passed into the constructor. This design implies that the effective range of a spell focus is an intrinsic property of the focus object itself, allowing different focus objects of the same type to potentially have different ranges.
*   **Const-Correctness**: The `operator()` is marked `const`, ensuring that evaluating a game object does not modify the state of the `GameObjectFocusCheck` instance. This allows the same predicate instance to be safely reused across multiple search iterations.
*   **Optimized Filtering Order**: The `operator()` checks conditions in an order that minimizes computational cost: Type -> Spawn Status -> ID Match -> Distance. This ensures that expensive distance calculations are only performed on objects that have already passed cheaper identity and state checks.

## Member Reference

**GameObjectFocusCheck**
Constructor that initializes the predicate with a pointer to the casting `WorldObject` (`i_caster`) and the required `uint32` focus identifier (`i_focusId`).

**GetFocusObject**
Returns a constant reference to the `i_caster` object. Used by grid searchers to access properties of the initiating entity, such as its phase mask.

**operator()**
Evaluates whether a given `GameObject` is a valid target. Returns `true` if the object is of type `GAMEOBJECT_TYPE_SPELL_FOCUS`, is spawned, has a matching `focusId` in its static info, and is within the distance specified by its static info (`spellFocus.dist`) from the `i_caster`.

---

<!-- machine-true, projected from graph.json -->

## Map — GameObjectFocusCheck

*Source:* GridNotifiers.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GameObjectFocusCheck | ctor | — | Spell.Main/CheckItems | — |
| GetFocusObject | method | — | — | — |
| operator() | method | — | — | — |

---

<!-- verify: failed-members | invented: operator -->
