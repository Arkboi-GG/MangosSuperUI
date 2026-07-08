<!-- provenance: failed-members -->
# PlayerAtMinimumRangeAway

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# PlayerAtMinimumRangeAway

**Purpose & Responsibilities**

`PlayerAtMinimumRangeAway` is a predicate functor defined within the `MaNGOS` namespace in `GridNotifiers.h`. Its sole responsibility is to evaluate whether a specific `Player` object satisfies a set of criteria relative to a reference `Unit`, primarily serving as a filter during grid-based spatial searches.

Specifically, it identifies players who are:
1.  **Alive.**
2.  **Not Game Masters** (excluding GMs from consideration).
3.  **Outside a specified minimum radius** (`fMinRange`) from the reference `Unit`.

This structure is designed to be passed to grid search utilities (such as `PlayerSearcher` or `PlayerListSearcher`) to efficiently locate players who are sufficiently distant from a given entity, often used for mechanics requiring separation, such as stealth checks, aggro reset conditions, or specific spell targeting constraints. It does not perform threat list validation; the comment in the source explicitly notes that threat checks must be handled externally if combat context is required.

**Member-by-Member Behavior**

### Constructor: **PlayerAtMinimumRangeAway**
The constructor initializes the predicate with two parameters:
*   `Unit const* unit`: The reference entity from which distances are calculated. Stored in the private member `pUnit`.
*   `float fMinRange`: The minimum distance threshold. Stored in the private member `fRange`.

This setup allows the functor to be instantiated with specific context before being passed to a search algorithm.

### Method: **operator()**
The `operator()` method implements the core filtering logic. It accepts a `Player*` pointer and returns a `bool` indicating whether the player meets the criteria.

The evaluation proceeds as follows:
1.  **Game Master Exclusion:** Checks `!pPlayer->IsGameMaster()`. If the player is a GM, the function immediately returns `false`. This ensures GMs are ignored by this specific check, likely to prevent them from interfering with NPC logic or player-vs-player mechanics dependent on this predicate.
2.  **Alive Status:** Checks `pPlayer->IsAlive()`. Dead players are excluded.
3.  **Distance Check:** Evaluates `!pUnit->IsWithinDist(pPlayer, fRange, false)`.
    *   It calculates the distance between the reference `Unit` (`pUnit`) and the candidate `Player`.
    *   The third argument `false` indicates that the distance calculation is **2D** (ignoring Z-axis/elevation differences).
    *   The logical NOT (`!`) means the function returns `true` only if the player is **NOT** within the minimum range. In other words, the player must be *outside* the circle defined by `fMinRange`.

If all three conditions are met, the function returns `true`; otherwise, it returns `false`.

**Cross-Unit Boundaries**

*   **Called By:** `ScriptedAI/GetPlayerAtMinimumRange`
    *   The MAP indicates that this functor is instantiated and used by logic in `ScriptedAI` (specifically a function or method named `GetPlayerAtMinimumRange`). This suggests that AI routines use this predicate to find players who are far enough away from the creature to satisfy certain behavioral triggers (e.g., fleeing, despawning, or ignoring targets).
*   **Calls Out:** None.
    *   The functor itself contains no outgoing calls to other units. It relies on methods inherent to the `Player` and `Unit` classes (`IsGameMaster`, `IsAlive`, `IsWithinDist`) which are part of the core object hierarchy, not distinct cross-unit dependencies in the context of this MAP.

**Data Model**

This unit does not interact with any database tables. It operates entirely on in-memory game objects (`Player` and `Unit`) and their spatial coordinates.

**Notable Implementation Details**

1.  **2D Distance Calculation:** The call to `IsWithinDist` passes `false` for the 3D flag. This is a critical detail: elevation changes do not affect the outcome. A player standing on a high cliff directly above the `Unit` might be considered "within range" if their X/Y coordinates are close, even if the vertical distance is large. Conversely, a player far away horizontally but at the same elevation will be correctly identified as outside the range. Maintainers must ensure this 2D assumption aligns with the intended game mechanic.
2.  **No Threat Validation:** The source code contains a prominent comment: `//No threat list check, must be done explicit if expected to be in combat with creature`. This warns users of this functor that it does not verify if the player is on the creature's threat list. If the calling code assumes that players found by this functor are valid combat targets or enemies, additional checks are required elsewhere.
3.  **GM Exclusion:** The hard-coded exclusion of Game Masters (`!pPlayer->IsGameMaster()`) is a deliberate design choice. This prevents GMs from triggering effects that depend on player proximity/distance, which is common in server-side logic to avoid accidental interference with NPC behaviors during testing or administration.
4.  **Const Correctness:** The reference `Unit` is stored as `Unit const*`, ensuring the functor cannot modify the reference entity. The `Player` argument in `operator()` is non-const (`Player*`), though the functor only reads from it. This matches the signature requirements of standard library algorithms or grid searchers that may pass non-const pointers.

## Member Reference

**PlayerAtMinimumRangeAway**
Constructor that initializes the functor with a reference `Unit` pointer and a minimum distance float. Stores these in private members `pUnit` and `fRange` respectively.

**operator()**
Predicate method that returns `true` if the provided `Player` is alive, is not a Game Master, and is located outside the 2D minimum range (`fRange`) from the reference `Unit` (`pUnit`). Returns `false` otherwise.

---

<!-- machine-true, projected from graph.json -->

## Map — PlayerAtMinimumRangeAway

*Source:* GridNotifiers.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| PlayerAtMinimumRangeAway | ctor | — | ScriptedAI/GetPlayerAtMinimumRange | — |
| operator() | method | — | — | — |

---

<!-- verify: failed-members | invented: operator -->
