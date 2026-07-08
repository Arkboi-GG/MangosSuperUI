<!-- provenance: failed-members -->
# CallOfHelpCreatureInRangeDo

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# CallOfHelpCreatureInRangeDo

**Purpose & Responsibilities**
`CallOfHelpCreatureInRangeDo` is a functor class defined in `GridNotifiers.h` within the `MaNGOS` namespace. It encapsulates the logic for reacting to a "Call for Help" event initiated by a `Unit`. When a unit is attacked and calls for assistance, this functor is applied to nearby creatures to determine if they should intervene. Its primary responsibility is to evaluate each candidate creature's eligibility, proximity, and line-of-sight relative to the caller, and then direct the creature to either attack the enemy or flee from the threat, depending on the creature's capabilities and state.

This class is designed to be used with the grid notification system (specifically `CreatureWorker` or similar iteration mechanisms) to apply this logic to all creatures within a specified radius of the caller.

**Member-by-Member Behavior**

*   **`CallOfHelpCreatureInRangeDo` (Constructor)**
    Initializes the functor with three parameters that define the context of the call for help:
    1.  `i_funit`: A pointer to the `Unit` that issued the call for help (the ally needing assistance).
    2.  `i_enemy`: A pointer to the `Unit` that is threatening `i_funit`.
    3.  `i_range`: A `float` representing the maximum distance within which creatures are considered potential responders.

*   **`operator()` (Method)**
    This method is invoked for each `Creature* u` passed to it by the grid iterator. It executes the following sequence of checks and actions:
    1.  **Self-Exclusion:** Returns immediately if the candidate creature `u` is identical to the caller `i_funit`.
    2.  **Proximity Check:** Returns if `u` is outside `i_range` of `i_funit`, determined by `i_funit->IsWithinDistInMap(u, i_range)`.
    3.  **Eligibility Check:** Calls `u->CanBeTargetedByCallForHelp(i_funit, i_enemy, false)`. This delegates complex social, faction, and state rules to the `Creature` class to determine if `u` is allowed to respond to this specific call. If ineligible, it returns.
    4.  **Line-of-Sight Check:** Returns if `i_funit` cannot see `u` (`!i_funit->IsWithinLOSInMap(u)`). The caller must have line-of-sight to the responder.
    5.  **Action Decision:**
        *   If `u` can respond to the call against the enemy (`u->CanRespondToCallForHelpAgainst(i_enemy)`) and has an active AI (`u->AI()`), it triggers `u->AI()->AttackStart(i_enemy)`, initiating combat against the enemy.
        *   Otherwise, if `u` can flee from the call against the enemy (`u->CanFleeFromCallForHelpAgainst(i_enemy)`), it triggers `u->MoveAwayFromTarget(i_enemy, 10.0f)`, causing the creature to move away from the threat.

**Cross-Unit Boundaries**

*   **Called by `Creature.Main/CallForHelp`**:
    As indicated in the MAP, this functor is instantiated and utilized by the `Creature` class's `CallForHelp` logic. The `Creature` unit identifies a threat and initiates a grid search for allies. It passes itself as `i_funit`, the attacker as `i_enemy`, and a relevant range to this functor. The grid system then invokes `operator()` on each nearby creature.

*   **Calls into `Creature` and `Unit` methods**:
    While the MAP lists "—" for calls out, the source code demonstrates dependencies on methods within `Creature` and `Unit` classes. Key interactions include:
    *   `Creature::CanBeTargetedByCallForHelp`: Determines if the creature is socially and mechanically capable of responding.
    *   `Creature::CanRespondToCallForHelpAgainst` / `Creature::CanFleeFromCallForHelpAgainst`: Determines the specific type of response (attack vs. flee).
    *   `Unit::IsWithinDistInMap` / `Unit::IsWithinLOSInMap`: Spatial and visibility checks.
    *   `Creature::AI()`: Retrieves the AI interface to trigger `AttackStart`.
    *   `Creature::MoveAwayFromTarget`: Triggers movement behavior.

**Data Model**
This unit does not interact with any database tables. It operates entirely on in-memory game objects (`Unit`, `Creature`) and their spatial relationships.

**Notable Implementation Details**

*   **Functor Pattern**: This class follows the standard C++ functor pattern used throughout MaNGOS for grid operations. It stores state (`i_funit`, `i_enemy`, `i_range`) and defines `operator()` to be called by generic iterators like `CreatureWorker`.
*   **Strict Visibility Requirement**: The check `!i_funit->IsWithinLOSInMap(u)` ensures that the *caller* must have line-of-sight to the *responder*. This is distinct from many other checks where the responder must see the caller or enemy. This implies that if a creature is hidden behind a wall relative to the person calling for help, they will not respond, even if they can see the enemy.
*   **Fallback to Fleeing**: The logic provides a nuanced response. Not all allies will attack. Creatures that cannot or should not attack (perhaps due to low health, specific AI flags, or faction rules) might instead flee. This is handled by the `else if` branch checking `CanFleeFromCallForHelpAgainst`.
*   **AI Dependency**: The attack action requires `u->AI()` to be non-null. If a creature has no AI assigned, it will not attack, even if it passes the other checks. It will also not flee unless `CanFleeFromCallForHelpAgainst` returns true (which likely also depends on AI or movement capabilities).
*   **No Range Update**: Unlike some "Nearest" check classes in this file (e.g., `NearestAssistCreatureInCreatureRangeCheck`), this `Do` class does not update `i_range` during iteration. It is a "Do" action, not a "Search" filter, so it processes all candidates within the initial fixed range.

## Member Reference

**CallOfHelpCreatureInRangeDo**
Constructor that initializes the functor with the calling unit (`i_funit`), the enemy unit (`i_enemy`), and the effective range (`i_range`). These values are stored as `const` pointers for `i_funit` and `i_enemy`, and a `float` for `i_range`.

**operator()**
The execution method invoked for each `Creature* u` in the grid. It filters out the self-caller, checks proximity and line-of-sight from the caller to the candidate, verifies eligibility via `CanBeTargetedByCallForHelp`, and then directs the creature to either `AttackStart` the enemy (if capable and having AI) or `MoveAwayFromTarget` (if it can flee).

---

<!-- machine-true, projected from graph.json -->

## Map — CallOfHelpCreatureInRangeDo

*Source:* GridNotifiers.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| CallOfHelpCreatureInRangeDo | ctor | — | Creature.Main/CallForHelp | — |
| operator() | method | — | — | — |

---

<!-- verify: failed-members | invented: operator -->
