<!-- provenance: verbose -->
# ObjectPosSelector

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ObjectPosSelector

## Purpose & Responsibilities

`ObjectPosSelector` is a stateful iterator that computes valid placement angles around a central point (`m_center_x`, `m_center_y`) for new game objects, ensuring they do not overlap with existing objects already placed in that vicinity. It implements a packing algorithm that scans angular sectors between occupied positions (obstacles) to identify gaps large enough to accommodate a new object of a specified radius (`m_dist`).

The selector maintains two sorted `std::multimap` lists of occupied angular positions: one for positive angles (`USED_POS_PLUS`) and one for negative angles (`USED_POS_MINUS`). It iterates through these lists to yield candidate angles, supporting:
1.  **Gap Detection:** Identifying spaces between adjacent obstacles.
2.  **Small-Step Iteration:** Yielding multiple angles within a single gap by stepping by a calculated `m_anglestep`.
3.  **Circular Continuity:** Handling the transition across the $\pm \pi$ boundary.

This unit is consumed by `WorldObject.Object/GetNearPointAroundPosition` to determine collision-free coordinates for spawning entities (e.g., corpses, items) near an existing entity.

## Member-by-Member Behavior

### Initialization and State Management

**`ObjectPosSelector` (Constructor)**
Initializes the selector with center coordinates, central object radius (`size`), and required clearance distance (`dist`).
*   **Safety:** If `size` is zero, it defaults to `DEFAULT_WORLD_OBJECT_SIZE` to prevent `m_anglestep` from becoming zero (which would halt iteration).
*   **Step Calculation:** Computes `m_anglestep` as `acos(m_dist / (m_dist + 2 * m_size))`, representing the minimum angular separation between two touching objects of radius `m_size` at distance `m_dist`.
*   **State:** Resets iterators (`m_nextUsedPos`) to list ends and clears small-step tracking variables.

**`InitializeAngle`**
Resets the iteration state to the start of the search. Sets `m_nextUsedPos` iterators to `begin()` of both lists, enables small-step mode (`m_smallStepOk = true`) for both directions, and resets `m_smallStepAngle` to 0.

**`AddUsedPos`**
Registers an existing obstacle. Inserts the angle's absolute value as the key into `m_UsedPosLists[USED_POS_PLUS]` (if angle $\ge$ 0) or `m_UsedPosLists[USED_POS_MINUS]` (if angle $< 0$), storing the sign, size, and distance in the `UsedPos` value. Calls `UsedPos/UsedPos` to construct the value.

### Angle Iteration Entry Points

**`FirstAngle`**
Attempts to find the initial valid angle. Handles asymmetric cases where one list is empty by delegating to `NextAngleFor` for the first entry of the non-empty list. Returns `false` if both lists are empty.

**`NextAngle`**
The primary driver for generating valid angles. Loops while there are unprocessed entries in the used position lists or while small-step iteration is active. Calls `NextPosibleAngle`; if it succeeds, returns `true` with the angle. Returns `false` when all possibilities are exhausted.

**`NextUsedAngle`**
Iterates through used positions to detect blockages. Returns `true` if `NextPosibleAngle` fails (indicating the current sector is blocked or exhausted). Used to identify when a sequence of valid angles ends due to an obstacle.

### Core Iteration Logic

**`NextPosibleAngle`**
The decision engine that selects the next candidate angle. It compares the current iterators of the positive and negative lists to determine which side is "less updated" (smaller absolute angle).
1.  Prioritizes the side with the smaller current angle or the active small-step sequence.
2.  Delegates to `NextSmallStepAngle` if small-step mode is active, otherwise to `NextAngleFor`.
3.  Advances the corresponding iterator (`++m_nextUsedPos`) if the delegation fails.
4.  Returns `false` if both lists are exhausted and no small-step angles remain.

**`NextAngleFor`**
Calculates a candidate angle just outside a specific `usedPos` obstacle: `angle = usedPos.first * sign + angle_step * sign`.
*   Validates the angle against the next obstacle in the same direction using `CheckAngle`.
*   If valid, enables small-step mode (`m_smallStepOk = true`) and records the current angle and next obstacle pointer for subsequent iterations.
*   Returns `false` if the angle overlaps with the next obstacle.

**`NextSmallStepAngle`**
Generates subsequent angles within an identified gap by incrementing the previous angle by `m_anglestep`.
*   Disables small-step mode and returns `false` if the absolute angle exceeds $\pi$ or passes the next known obstacle (`m_smallStepNextUsedPos`).
*   Validates against the next obstacle using `CheckAngle`.

### Validation and Helpers

**`CheckAngle`**
Determines if a proposed `angle` is safe relative to a `nextUsedPos` obstacle. Calculates the obstacle's angular width and adjusts for sign differences (wrap-around) before checking if `fabs(angle) + angle_step2 <= next_angle`.

**`CheckOriginal`**
Checks if angle 0 is valid by verifying it does not overlap with the first obstacle in either list.

**`IsNonBalanced`**
Returns `true` if one used position list is empty while the other is not.

**`GetAngle`**
Calculates the angular radius of an obstacle: `acos(m_dist / (usedPos.dist + usedPos.size + m_size))`.

**`nextUsedPos`**
Returns a pointer to the next obstacle in the specified direction. If the current list is exhausted, it checks the reverse iterator of the opposite list to handle the $\pm \pi$ boundary connection.

**`operator~`**
Free function that toggles `UsedPosType` between `USED_POS_PLUS` and `USED_POS_MINUS`.

## Cross-Unit Boundaries

*   **Called by `WorldObject.Object/GetNearPointAroundPosition`:**
    The primary consumer. `WorldObject` instantiates `ObjectPosSelector`, populates it with nearby objects, and calls `InitializeAngle`, `FirstAngle`, and `NextAngle` to retrieve candidate angles for placement.
*   **Called by `WorldObject.Object/add`:**
    Calls `AddUsedPos` to register a newly placed object into the selector's internal lists, preventing future placements from overlapping with it.
*   **Calls `UsedPos/UsedPos`:**
    Internal constructor call within `AddUsedPos` to create `UsedPos` structs.

## Data Model

This unit does not interact with any database tables. All data is held in memory during the lifetime of the selector instance.

## Notable Implementation Details

1.  **Dual-List Strategy:** Angles are split into positive and negative lists, treating the circle as two linear segments. `nextUsedPos` bridges these segments by checking the opposite list's reverse iterator when one list is exhausted.
2.  **Small-Step Optimization:** Once a gap is identified via `NextAngleFor`, `NextSmallStepAngle` iterates through it with fixed steps, reducing computational overhead compared to recalculating gaps for every candidate.
3.  **Angle Normalization:** Map keys store absolute angles; signs are preserved in `UsedPos`. This allows standard map sorting while manually reconstructing signed angles for calculations.
4.  **Wrap-Around Handling:** `CheckAngle` adjusts `next_angle` by $2\pi$ when comparing angles from opposite sides of the $\pm \pi$ boundary.
5.  **Zero-Size Guard:** The constructor enforces a minimum size to prevent `m_anglestep` from becoming zero, which would cause infinite loops or stalled iteration.

## Member Reference

**`ObjectPosSelector`**
Constructor initializing center coordinates, size, distance, and `m_anglestep`. Defaults size to `DEFAULT_WORLD_OBJECT_SIZE` if zero.

**`operator~`**
Free function toggling `UsedPosType` between `USED_POS_PLUS` and `USED_POS_MINUS`.

**`nextUsedPos`**
Returns pointer to next obstacle in `uptype` direction, handling wrap-around via opposite list's reverse iterator.

**`CheckAngle`**
Validates if `angle` overlaps with `nextUsedPos`, accounting for sign differences and wrap-around.

**`AddUsedPos`**
Inserts obstacle into `m_UsedPosLists` based on angle sign, calling `UsedPos/UsedPos`.

**`CheckOriginal`**
Checks if angle 0 is valid against first obstacles in both lists.

**`InitializeAngle`**
Resets iterators to `begin()`, enables small-step mode, and resets angles to 0.

**`IsNonBalanced`**
Returns `true` if one list is empty and the other is not.

**`NextAngleFor`**
Calculates angle outside `usedPos`, validates against next obstacle, and enables small-step mode if valid.

**`FirstAngle`**
Finds first valid angle, handling empty-list cases by delegating to `NextAngleFor`.

**`NextAngle`**
Main iteration loop calling `NextPosibleAngle` to yield valid angles.

**`NextSmallStepAngle`**
Increments angle by `m_anglestep` within a gap, validating against boundaries and next obstacle.

**`NextUsedAngle`**
Iterates used positions, returning `true` if `NextPosibleAngle` fails (blockage detected).

**`NextPosibleAngle`**
Decision engine selecting next direction based on iterator positions, delegating to `NextSmallStepAngle` or `NextAngleFor`.

**`GetAngle`**
Calculates angular radius of an obstacle using `acos`.

---

<!-- machine-true, projected from graph.json -->

## Map — ObjectPosSelector

*Source:* ObjectPosSelector.cpp, ObjectPosSelector.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ObjectPosSelector | ctor | — | WorldObject.Object/GetNearPointAroundPosition | — |
| operator~ | function | — | — | — |
| nextUsedPos | method | — | — | — |
| CheckAngle | method | — | — | — |
| AddUsedPos | method | UsedPos/UsedPos | WorldObject.Object/add | — |
| CheckOriginal | method | — | WorldObject.Object/GetNearPointAroundPosition | — |
| InitializeAngle | method | — | WorldObject.Object/GetNearPointAroundPosition | — |
| IsNonBalanced | method | — | WorldObject.Object/GetNearPointAroundPosition | — |
| NextAngleFor | method | — | — | — |
| FirstAngle | method | — | WorldObject.Object/GetNearPointAroundPosition | — |
| NextAngle | method | — | WorldObject.Object/GetNearPointAroundPosition | — |
| NextSmallStepAngle | method | — | — | — |
| NextUsedAngle | method | — | WorldObject.Object/GetNearPointAroundPosition | — |
| NextPosibleAngle | method | — | — | — |
| GetAngle | method | — | — | — |
