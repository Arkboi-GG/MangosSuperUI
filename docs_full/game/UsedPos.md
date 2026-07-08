# UsedPos

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ObjectPosSelector::UsedPos

**Purpose & Responsibilities**

`ObjectPosSelector::UsedPos` is a lightweight data structure (POD-like struct) defined within `ObjectPosSelector.h`. Its sole responsibility is to encapsulate the geometric properties of a single "occupied" position relative to a central point. It stores the necessary parameters to calculate angular exclusion zones around that position, preventing other objects from being placed too close to it during spatial selection algorithms.

It contains no logic of its own; it is purely a container for three `float` values: `sign`, `size`, and `dist`. It is constructed exclusively by the `ObjectPosSelector::AddUsedPos` method (in `ObjectPosSelector.cpp`) and is stored in the `m_UsedPosLists` multimap within the parent `ObjectPosSelector` class.

**Member-by-Member Behavior**

The unit consists of a single constructor and three public data members.

*   **Constructor (`UsedPos`)**: Initializes the struct with three floating-point arguments: `sign_`, `size_`, and `dist_`. These are assigned directly to the corresponding member variables. The constructor is marked inline implicitly by virtue of being defined within the class body.
*   **`sign`**: A `float` representing the directional sign of the position relative to the central axis (typically +1 or -1, corresponding to the `UsedPosType` enum values `USED_POS_PLUS` and `USED_POS_MINUS`). This allows the selector to distinguish between positions on opposite sides of the central object.
*   **`size`**: A `float` representing the physical radius or "size" of the occupied point itself. This value is used in collision/exclusion calculations to determine how much angular space this position blocks.
*   **`dist`**: A `float` representing the distance from the central point to this occupied position. Crucially, the comment indicates this distance *includes* the size of the central point. This value is used in trigonometric calculations (specifically `acos`) to determine the angular width of the exclusion zone created by this position.

**Cross-Unit Boundaries**

*   **Called by `ObjectPosSelector::AddUsedPos`**: The `ObjectPosSelector` class (defined in the same header, implemented in `ObjectPosSelector.cpp`) creates instances of `UsedPos` when registering new occupied positions. `AddUsedPos` calculates the appropriate `sign`, `size`, and `dist` values and passes them to this constructor to create a node that is then inserted into `m_UsedPosLists`.
*   **No Outgoing Calls**: This struct does not call any other units. It is a passive data holder.

**Data Model**

This unit does not interact with any database tables. It operates entirely in memory using geometric calculations.

**Notable Implementation Details**

*   **Trigonometric Dependency**: The `dist` and `size` fields are critical inputs for the `GetAngle` method in the parent `ObjectPosSelector` class. Specifically, `GetAngle` uses the formula `acos(m_dist / (usedPos.dist + usedPos.size + m_size))`. This implies that `usedPos.dist + usedPos.size + m_size` must be greater than or equal to `m_dist` to avoid domain errors in `acos`. The design assumes these values are validated or constrained by the caller (`AddUsedPos`) to ensure valid geometry.
*   **Sign Convention**: The `sign` field is used to differentiate between two separate lists (`m_UsedPosLists[USED_POS_PLUS]` and `m_UsedPosLists[USED_POS_MINUS]`). This separation allows the algorithm to handle angular wrapping and symmetry more efficiently, treating positive and negative angular deviations from the center independently until they need to be merged or compared.
*   **Memory Layout**: As a simple struct with three floats, it has a predictable memory layout (12 bytes on most platforms, potentially 16 with padding depending on alignment). It is stored in a `std::multimap`, meaning it is copied frequently during insertion and iteration. Its small size makes this overhead negligible.

## Member Reference

**UsedPos**
Constructor for the `UsedPos` struct. Takes three `float` arguments (`sign_`, `size_`, `dist_`) and initializes the corresponding member variables. Called exclusively by `ObjectPosSelector::AddUsedPos` to create entries for the `m_UsedPosLists` multimap.

---

<!-- machine-true, projected from graph.json -->

## Map — UsedPos

*Source:* ObjectPosSelector.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| UsedPos | ctor | — | ObjectPosSelector/AddUsedPos | — |
