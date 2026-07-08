# SelectionPool

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# SelectionPool

**SelectionPool** is a nested helper class within `BattleGroundQueue` (defined in `BattleGroundMgr.h`) responsible for managing the temporary set of groups selected to fill a specific team slot in a battleground instance. It acts as a staging area during the matchmaking process, accumulating groups until the required player count is met or exceeded, at which point the accumulated groups are committed to the battleground.

## Purpose & Responsibilities

The primary responsibility of `SelectionPool` is to aggregate `GroupQueueInfo` objects representing player groups that have been matched for a specific battleground team (Alliance or Horde). It maintains two key pieces of state:
1.  **`selectedGroups`**: A vector (`GroupsQueueType`) containing pointers to the `GroupQueueInfo` structures of the groups currently selected for this team.
2.  **`playerCount`**: An integer tracking the total number of players represented by the groups in `selectedGroups`.

This class simplifies the logic in `BattleGroundQueue` by encapsulating the bookkeeping required to determine how many players are currently slated for a team. It is used transiently during the `FillPlayersToBg` process; once a battleground is successfully filled, the contents of the `SelectionPool` are processed (players invited/teleported), and the pool is typically reset or discarded for the next iteration.

## Member-by-Member Behavior

### State Access

*   **`GetPlayerCount()`**: Returns the current value of `playerCount`. This is a simple accessor used by the parent `BattleGroundQueue` to check if the minimum or maximum player requirements for a team have been satisfied.

## Cross-Unit Boundaries

`SelectionPool` is tightly coupled with `BattleGroundQueue` (defined in `BattleGroundMgr.h`). It has no external dependencies beyond standard library containers and the `GroupQueueInfo` struct.

*   **Called by `BattleGroundQueue`**:
    *   **`BattleGroundQueue::CheckNormalMatch`**: Uses `SelectionPool` to attempt to match normal (non-premade) groups. It iteratively adds groups and checks `GetPlayerCount` to see if a valid match can be formed.
    *   **`BattleGroundQueue::CheckPremadeMatch`**: Similar to `CheckNormalMatch`, but handles premade groups. It uses `SelectionPool` to accumulate premade groups until the team size requirements are met.
    *   **`BattleGroundQueue::FillPlayersToBg`**: After matches are identified, this method uses the `SelectionPool` (via `m_selectionPools`) to manage the final list of groups being invited to the battleground. It calls `GetPlayerCount` to verify team sizes before proceeding with invitations.
    *   **`BattleGroundQueue::KickGroup`**: While `BattleGroundQueue` has its own `KickGroup` method for removing players from the main queue, the `SelectionPool` is used internally during the matching algorithm to refine the selection. Note that `SelectionPool` itself has a `KickGroup` method (not in the MAP for this unit, but present in the header) used by `BattleGroundQueue` logic to backtrack if a group selection violates constraints.

## Data Model

`SelectionPool` does not interact directly with any database tables. It operates entirely in memory using pointers to `GroupQueueInfo` objects, which themselves are transient structures managed by `BattleGroundQueue`.

## Notable Implementation Details

1.  **Stack-like Behavior**: Although only `GetPlayerCount` is exposed in the MAP for this unit, the header reveals that `SelectionPool` behaves like a stack for groups via `AddGroup` and `KickGroup`. `AddGroup` pushes to the back of `selectedGroups`, and `KickGroup` pops from the back. This Last-In-First-Out (LIFO) order is crucial for the matching algorithm, which typically tries to fit larger or more specific groups first and backs out if constraints are violated.
2.  **No Validation of Group Integrity**: `SelectionPool` assumes that the `GroupQueueInfo` passed to `AddGroup` is valid and that the `desiredCount` accurately reflects the number of players. It does not validate the group's existence or consistency.
3.  **Memory Management**: `SelectionPool` stores raw pointers (`GroupQueueInfo*`). It does not take ownership of these objects; it merely references them. The lifetime of `GroupQueueInfo` objects is managed by `BattleGroundQueue`. Care must be taken to ensure that groups are not removed from the main queue while still referenced in a `SelectionPool` (though the design suggests pools are cleared after use).
4.  **Thread Safety**: There is no explicit locking within `SelectionPool`. Thread safety is expected to be handled by the parent `BattleGroundQueue` or the caller, as `SelectionPool` instances (`m_selectionPools`) are accessed within methods of `BattleGroundQueue` that likely hold appropriate locks (though the commented-out `m_lock` in `BattleGroundQueue` suggests thread safety might be a work-in-progress or handled externally).

## Member Reference

**GetPlayerCount**
Returns the current `playerCount`. Used by callers such as `BattleGroundQueue::CheckNormalMatch`, `BattleGroundQueue::CheckPremadeMatch`, `BattleGroundQueue::FillPlayersToBg`, and `BattleGroundQueue::KickGroup` to check if team size requirements are met.

---

<!-- machine-true, projected from graph.json -->

## Map — SelectionPool

*Source:* BattleGroundMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GetPlayerCount | method | — | BattleGroundMgr/CheckNormalMatch, BattleGroundMgr/CheckPremadeMatch, BattleGroundMgr/FillPlayersToBg, BattleGroundMgr/KickGroup | — |
