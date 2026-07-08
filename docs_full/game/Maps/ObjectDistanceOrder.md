<!-- provenance: failed-members -->
# ObjectDistanceOrder

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ObjectDistanceOrder

`ObjectDistanceOrder` is a comparator functor defined in `GridSearchers.h`. Its purpose is to provide a strict weak ordering for `WorldObject` instances based on their spatial distance from a specific source `Unit`. It is designed to be used with standard library algorithms (such as `std::sort` or `std::min_element`) to arrange collections of game objects by proximity to a central entity.

The struct holds a constant pointer to a `Unit` (`m_pSource`) which serves as the origin point for all distance calculations. When invoked via its `operator()`, it delegates the actual comparison logic to the `Unit` class's `GetDistanceOrder` method, passing two `WorldObject` candidates. This design encapsulates the "distance from X" concept into a reusable, stateful comparator that can be passed to grid search utilities or sorting routines.

## Member Behavior

### Construction and State
**ObjectDistanceOrder** (constructor) initializes the functor by storing a pointer to the source `Unit`. This pointer is immutable after construction, ensuring that the reference point for distance calculations remains consistent throughout the lifetime of the functor instance. The constructor takes a `Unit const*` parameter, indicating that the source object itself does not need to be modified during the comparison process.

### Comparison Logic
**operator()** implements the core comparison logic required by STL algorithms expecting a binary predicate. It accepts two `WorldObject const*` arguments, representing two potential targets in the game world. The method returns a boolean value indicating whether `pLeft` should precede `pRight` in a sorted sequence. Specifically, it returns `true` if `pLeft` is closer to `m_pSource` than `pRight` (as determined by `m_pSource->GetDistanceOrder`). This effectively sorts objects in ascending order of distance from the source unit.

## Cross-Unit Boundaries

`ObjectDistanceOrder` acts as a bridge between high-level script logic and low-level spatial queries. It does not perform database operations or complex AI calculations itself but relies on the `Unit` class for geometric computations.

### Called By
Several boss and instance scripts instantiate `ObjectDistanceOrder` to find the nearest target among a group of entities. These callers typically construct the functor with themselves (or a specific anchor unit) as the source, then use it to sort a list of creatures or game objects retrieved from the grid.

*   **`boss_gothik/SummonAdds`**: Uses the functor to determine the closest adds or targets during the Gothik encounter in Naxxramas, likely for positioning or targeting mechanics.
*   **`boss_marli/SelectNextEgg`**: Employs the comparator to select the nearest egg object in the Marli encounter, ensuring the boss interacts with the most relevant target.
*   **`instance_naxxramas.Main/GetClosestAnchorForGoth`**: Utilizes the functor to find the optimal anchor point for Gothik's mechanics, sorting potential anchors by distance to a reference unit.
*   **`razorfen_kraul/DoFindNewTuber`**: Uses the comparator to identify the closest tuber creature in Razorfen Kraul, facilitating movement or attack targeting.
*   **`scourge_invasion/UpdateAI#7`**: Applies the functor during the Scourge Invasion event to prioritize targets or locations based on proximity to a specific unit.

### Calls Out
*   **`Unit.GetDistanceOrder`**: The `operator()` method calls this method on the stored `m_pSource` unit. This cross-boundary call transfers the two candidate `WorldObject` pointers to the `Unit` class, which performs the actual distance calculation and comparison. This delegation ensures that distance logic is centralized in the `Unit` class while allowing scripts to easily create custom sorting criteria.

## Data Model

This unit does not interact with any database tables. All operations are performed in memory using runtime object pointers and geometric data stored within the `Unit` and `WorldObject` classes.

## Notable Implementation Details

*   **Const Correctness**: Both the source unit (`m_pSource`) and the objects being compared (`pLeft`, `pRight`) are treated as `const`. This guarantees that the comparison operation is side-effect-free and safe to use in concurrent contexts where objects might be accessed by multiple threads (provided the underlying `Unit` methods are thread-safe).
*   **Delegation Pattern**: Rather than implementing distance calculation logic directly, `ObjectDistanceOrder` delegates to `Unit::GetDistanceOrder`. This avoids code duplication and ensures consistency with how distances are calculated elsewhere in the engine.
*   **Functor Design**: As a struct with a public constructor and `operator()`, it fits the standard C++ functor pattern. This allows it to be seamlessly integrated with STL algorithms like `std::sort`, `std::min_element`, or custom grid search implementations that accept comparators.
*   **No Reversed Variant**: While `GridSearchers.h` also defines `ObjectDistanceOrderReversed`, `ObjectDistanceOrder` strictly provides ascending order (closest first). Scripts requiring farthest-first ordering must use the reversed variant or invert the result manually.

## Member Reference

**ObjectDistanceOrder** (constructor): Initializes the functor with a pointer to the source `Unit` (`m_pSource`) that will serve as the reference point for distance comparisons. Takes a `Unit const*` argument.

**operator()** (method): Compares two `WorldObject` pointers (`pLeft` and `pRight`) by delegating to `m_pSource->GetDistanceOrder(pLeft, pRight)`. Returns `true` if `pLeft` is closer to the source unit than `pRight`, enabling ascending sort order by distance.

---

<!-- machine-true, projected from graph.json -->

## Map — ObjectDistanceOrder

*Source:* GridSearchers.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ObjectDistanceOrder | ctor | — | boss_gothik/SummonAdds, boss_marli/SelectNextEgg, instance_naxxramas.Main/GetClosestAnchorForGoth, razorfen_kraul/DoFindNewTuber, scourge_invasion/UpdateAI#7 | — |
| operator() | method | — | — | — |

---

<!-- verify: failed-members | invented: operator -->
