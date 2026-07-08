<!-- provenance: failed-members -->
# NearestHostileUnitInAggroRangeCheck

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# NearestHostileUnitInAggroRangeCheck

**Purpose & Responsibilities**
`NearestHostileUnitInAggroRangeCheck` is a predicate functor defined in `GridNotifiers.h` within the `MaNGOS` namespace. Its purpose is to identify the closest hostile unit to a specific `Creature` that is eligible to be targeted for aggro or attack. It is designed to be used with grid searchers (such as `UnitLastSearcher`) that iterate through nearby units in the world grid.

The functor implements a strict set of validation rules to ensure the selected target is valid for combat engagement. It filters candidates based on distance (specifically within the creature's attack range), hostility, targetability, visibility/detection status, and optional constraints like Line-of-Sight (LOS) and civilian immunity. Crucially, it maintains an internal state (`m_dist`) that tightens the search radius as closer valid targets are found, allowing the grid searcher to optimize performance by skipping units further away than the current best candidate.

**Member-by-Member Behavior**

*   **`NearestHostileUnitInAggroRangeCheck` (Constructor)**
    Initializes the checker with a pointer to the searching `Creature` (`m_me`). It accepts two optional boolean flags:
    *   `useLOS`: If true, the target must have direct Line-of-Sight to the creature.
    *   `ignoreCivilians`: If true, units flagged as civilians are excluded from consideration.
    The internal distance tracker `m_dist` is initialized to 9999, ensuring that the first valid unit found will update the range.

*   **`operator()`**
    Evaluates whether a candidate `Unit* u` is a valid aggro target. It returns `true` if the unit passes all checks, updating `m_dist` to the distance of this unit (thereby tightening the search radius for subsequent candidates). It returns `false` otherwise. The validation sequence is:
    1.  **Distance & Attack Range:** Checks if `u` is within `m_dist` AND within the creature's specific `GetAttackDistance(u)`. This ensures the creature can physically reach the target.
    2.  **Targetability:** Verifies `u` is targetable by `m_me` via `IsTargetableBy`.
    3.  **Hostility:** Confirms `m_me` considers `u` hostile via `IsHostileTo`.
    4.  **Visibility/Detection:** Ensures `u` is visible or detectable to `m_me` via `IsVisibleForOrDetect`.
    5.  **Civilian Filter:** If `m_ignoreCivilians` is set, checks if `u` is a `Creature` and if so, verifies it is not a civilian.
    6.  **Line-of-Sight:** If `m_useLOS` is set, performs a LOS check between `u` and `m_me`.
    7.  **Update:** If all pass, updates `m_dist` to the current distance and returns `true`.

*   **`NearestHostileUnitInAggroRangeCheck#2` (Declaration)**
    This entry in the map corresponds to the class declaration itself. In the context of the source file `GridNotifiers.h`, this is the single definition of the class. It does not represent a separate functional member but rather the type definition required for the functor to exist.

**Cross-Unit Boundaries**

*   **Called by `Creature.Main/SelectNearestHostileUnitInAggroRange`**:
    The primary consumer of this functor is the creature AI system located in `Creature.Main`. When a creature needs to select a new target (e.g., upon aggroing or re-evaluating threats), `Creature.Main` instantiates this checker and passes it to a grid searcher (typically `UnitLastSearcher` or similar structures defined in the same header). The searcher iterates over units in the grid, calling `operator()` on this functor for each candidate. The functor's role is to filter invalid targets and track the minimum distance to a valid target, allowing the searcher to efficiently find the *nearest* valid hostile unit.

*   **Calls Out**:
    The functor itself does not call out to other high-level architectural units directly. However, its `operator()` relies on methods from `Unit`, `Creature`, and `WorldObject` (which are part of the core entity hierarchy). Specifically, it calls:
    *   `Unit::IsWithinDistInMap`
    *   `Unit::IsTargetableBy`
    *   `Unit::IsHostileTo`
    *   `Unit::IsVisibleForOrDetect`
    *   `Unit::IsWithinLOSInMap`
    *   `Unit::GetDistance`
    *   `Creature::IsCivilian`
    *   `Creature::GetAttackDistance`

**Data Model**
This unit does not interact with any database tables. It operates entirely on runtime memory state of `Unit` and `Creature` objects.

**Notable Implementation Details**

1.  **Dynamic Range Tightening**: The functor modifies its own state (`m_dist`) upon success. This is a critical pattern for "nearest" searches in grid systems. By reducing `m_dist` to the distance of the last found valid target, subsequent iterations of the grid searcher can skip units that are further away than the current best candidate, optimizing performance.
2.  **Attack Distance vs. Aggro Range**: The check `std::min(m_me->GetAttackDistance(u), m_dist)` is significant. It doesn't just check if the unit is within the previous best distance; it also ensures the unit is within the creature's *attack* distance. This prevents selecting a target that is technically "nearest" among hostile units but still out of melee/range reach, which would be an invalid aggro target for immediate engagement.
3.  **Civilian Immunity**: The `ignoreCivilians` flag allows creatures to bypass non-combatant NPCs. This is crucial for preventing mobs from aggroing neutral vendors or quest givers who might be hostile due to faction differences but are not intended to be combat targets.
4.  **Visibility Logic**: The use of `IsVisibleForOrDetect` rather than a simple LOS check (unless `useLOS` is explicitly true) accounts for stealth mechanics and detection ranges. A unit might be hidden (stealthed) but still detectable if the attacker has sufficient perception or if the stealth is broken.
5.  **Const Correctness**: The functor holds `Creature const* m_me`, indicating it does not modify the searching creature's state, only its own internal distance tracker.

## Member Reference

**NearestHostileUnitInAggroRangeCheck**
Constructor that initializes the checker with a `Creature` pointer, and optional flags for Line-of-Sight enforcement and civilian ignoring. Sets initial distance to 9999.

**operator()**
Predicate method that evaluates if a `Unit` is a valid, nearest hostile aggro target. Checks distance (within attack range and current best distance), targetability, hostility, visibility/detection, civilian status, and LOS if enabled. Updates internal distance on success.

**NearestHostileUnitInAggroRangeCheck#2**
Class declaration entry corresponding to the type definition in `GridNotifiers.h`. No distinct behavioral implementation beyond the class structure itself.

---

<!-- machine-true, projected from graph.json -->

## Map — NearestHostileUnitInAggroRangeCheck

*Source:* GridNotifiers.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| NearestHostileUnitInAggroRangeCheck | ctor | — | Creature.Main/SelectNearestHostileUnitInAggroRange | — |
| operator() | method | — | — | — |
| NearestHostileUnitInAggroRangeCheck#2 | decl | — | — | — |

---

<!-- verify: failed-members | invented: operator -->
