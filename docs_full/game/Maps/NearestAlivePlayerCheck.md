<!-- provenance: failed-members -->
# NearestAlivePlayerCheck

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# NearestAlivePlayerCheck

**Purpose & Responsibilities**
`NearestAlivePlayerCheck` is a predicate functor defined in `GridNotifiers.h` within the `MaNGOS` namespace. Its sole responsibility is to identify the closest living `Player` object to a specified source `WorldObject`, excluding Game Masters and the source object itself if it is a player. It is designed to be used in conjunction with grid-based search utilities (such as `PlayerSearcher`) to efficiently locate players within a specific radius.

The class implements a "nearest-first" optimization strategy: upon finding a valid player, it updates its internal range limit to the distance of that player. Subsequent evaluations will reject any players further away than the current best candidate, allowing the calling search algorithm to prune unnecessary distance calculations or terminate early if the search order is spatially sorted.

## Member-by-Member Behavior

### **NearestAlivePlayerCheck** (Constructor)
Initializes the check with a reference to the source object (`me`) and an initial maximum search radius (`m_range`).
- **Parameters:**
  - `source`: A pointer to the `WorldObject` from which distances are calculated.
  - `dist`: The initial maximum distance threshold.
- **Behavior:** Stores `source` in the private member `me` and `dist` in `m_range`. No validation of `source` is performed here; validity is assumed by the caller.

### **operator()** (Method)
This is the core evaluation function invoked by search algorithms for each candidate `Player*`. It returns `true` if the player is a valid candidate and is closer than any previously accepted candidate, updating the internal range accordingly. It returns `false` otherwise.

**Evaluation Logic (in order):**
1. **Self-Exclusion:** If the candidate player `pPlayer` is identical to the source object `me`, it returns `false`. This prevents a player from targeting themselves in searches initiated by themselves.
2. **Game Master Exclusion:** If `pPlayer->IsGameMaster()` returns `true`, the player is ignored. This ensures that GMs are not considered for mechanics like aggro, looting, or social interactions that typically exclude staff accounts.
3. **Liveness Check:** If `!pPlayer->IsAlive()`, the player is ignored. Dead players (corpses) are not considered "alive" for this check.
4. **Distance Check:** Calls `me->IsWithinDistInMap(pPlayer, m_range)`. If the player is outside the current `m_range`, it returns `false`.
5. **Range Optimization:** If all previous checks pass, it updates `m_range` to the exact distance between `me` and `pPlayer` via `me->GetDistance(pPlayer)`. This tightens the constraint for subsequent candidates.
6. **Success:** Returns `true`, indicating this player is the current nearest valid candidate.

## Cross-Unit Boundaries

### Called By: `WorldObject.Object/FindNearestPlayer`
- **Direction:** Outbound call from `WorldObject` to `NearestAlivePlayerCheck`.
- **Collaboration:** The `WorldObject` class (likely in `WorldObject.cpp` or a related header) provides a high-level interface `FindNearestPlayer`. This method constructs a `NearestAlivePlayerCheck` instance with the desired range and passes it to a grid searcher (e.g., `PlayerSearcher`). The searcher iterates over players in the relevant grid cells, invoking `NearestAlivePlayerCheck::operator()` on each. The check filters and ranks the results, allowing `FindNearestPlayer` to return the single closest player or `nullptr` if none exist.

### Calls Out: None
- `NearestAlivePlayerCheck` does not call into other units directly. It relies on methods of `Player` (`IsGameMaster`, `IsAlive`) and `WorldObject` (`IsWithinDistInMap`, `GetDistance`) which are part of the core object hierarchy, not separate architectural units in the context of this map.

## Data Model
This unit does not interact with any database tables. It operates entirely on in-memory object states.

## Notable Implementation Details

1. **Mutating State in Predicate:** Unlike pure predicates, `operator()` modifies the internal state `m_range`. This is intentional and critical for the "nearest" logic. It assumes that the search algorithm processes candidates in an order where tightening the range is beneficial (e.g., processing closer grid cells first or simply pruning distant candidates). If the search order is random, this optimization still works correctly but may not prune as aggressively until a close candidate is found.
2. **GM Exclusion:** The explicit check `pPlayer->IsGameMaster()` is a common pattern in MMORPG servers to prevent GMs from interfering with normal gameplay mechanics (like being targeted by NPCs or triggering events). This behavior is hardcoded here, meaning any search using this check will inherently ignore GMs.
3. **Self-Reference Handling:** The check `me == pPlayer` handles the case where the source object is itself a `Player`. Without this, a player searching for the nearest player would always find themselves at distance 0.
4. **No Line-of-Sight (LOS) Check:** This check uses `IsWithinDistInMap`, which calculates Euclidean distance within the same map ID. It does **not** perform a Line-of-Sight check. If LOS is required, a different check or additional filtering by the caller is necessary.
5. **Const-Correctness:** The `operator()` is not marked `const` because it modifies `m_range`. This is a deliberate design choice to allow the range optimization. Users of this functor must ensure it is passed by value or reference appropriately to the searcher, understanding that the instance state changes during iteration.

## Member Reference

**NearestAlivePlayerCheck**
Constructor that initializes the source object `me` and the initial search radius `m_range`. Takes a `WorldObject const*` and a `float`.

**operator()**
Predicate method that evaluates a `Player*` candidate. Returns `true` if the player is alive, not a GM, not the source object, and within the current `m_range`. Updates `m_range` to the distance of the candidate if valid, enabling nearest-neighbor optimization. Returns `false` otherwise.

---

<!-- machine-true, projected from graph.json -->

## Map — NearestAlivePlayerCheck

*Source:* GridNotifiers.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| NearestAlivePlayerCheck | ctor | — | WorldObject.Object/FindNearestPlayer | — |
| operator() | method | — | — | — |

---

<!-- verify: failed-members | invented: operator -->
