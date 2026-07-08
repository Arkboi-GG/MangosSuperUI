<!-- provenance: verbose -->
# Path

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Path

## Purpose & Responsibilities

`Path` is a template container managing a sequence of spatial points (`PathNode`) representing a trajectory. It stores nodes in a `std::deque`, providing methods to manipulate the sequence (resize, crop, clear) and calculate Euclidean distances along the path. The class is specialized via the `PointPath` typedef for `PathNode` structures containing `x`, `y`, and `z` coordinates. It is a self-contained utility with no database interactions or external dependencies.

## Member-by-Member Behavior

### Path Management

*   **`size`**, **`empty`**: Return the node count and emptiness status, respectively.
*   **`resize`**: Adjusts the internal deque to the specified size.
*   **`clear`**: Removes all nodes.
*   **`crop`**: Trims the path by removing `start` nodes from the front and `end` nodes from the back. It safely handles cases where the crop count exceeds the current size by stopping when the deque is empty.

### Distance Calculation

*   **`GetTotalLength` (overload #2)**: Calculates the cumulative Euclidean distance between consecutive nodes from index `start` to `end`. It iterates from `start + 1` to `end`, summing the 3D distance between each adjacent pair.
*   **`GetTotalLength`**: Convenience overload calculating the total length of the entire path (index 0 to `size()`).
*   **`GetPassedLength`**: Calculates the distance from the path start to a specific point `(x, y, z)` associated with node index `curnode`. It sums the full segments up to `curnode - 1` and adds the partial distance from that previous node to the current coordinates. If `curnode` is 0, it returns 0.0f.

### Accessors

*   **`operator[]`**: Provides indexed access to a `PathNode`. Two overloads exist: one for non-const reference modification and one for const read-only access.
*   **`set`**: Assigns a new `PathElem` value to a specific index, allowing updates without replacing the entire element structure.

## Cross-Unit Boundaries

This unit has no outgoing calls to other units and is not called by other units according to the provided map. It is a standalone utility class.

## Data Model

This unit does not interact with any database tables. All data is held in memory within the `std::deque<PathElem>` member variable.

## Notable Implementation Details

*   **Template Design**: `Path` is templated on `PathElem` (storage) and `PathNode` (access). By default, `PathNode` equals `PathElem`. This allows flexibility if storage and access types differ, though `PointPath` uses `PathNode` for both.
*   **Deque Usage**: `std::deque` is used instead of `std::vector` to support efficient `pop_front` and `pop_back` operations in `crop`, providing constant-time removal from both ends.
*   **Distance Logic**: `GetTotalLength(uint32 start, uint32 end)` measures segments between indices `start` and `end`. The loop runs from `start + 1` to `end` (exclusive), calculating distances between `i-1` and `i`. `GetPassedLength` assumes `curnode` is the current node index and calculates distance from the start to the point `(x, y, z)` relative to the previous node (`curnode - 1`).

## Member Reference

**size**: Returns the number of nodes in the path.

**empty**: Returns true if the path has no nodes.

**resize**: Resizes the internal deque to the specified size.

**crop**: Removes `start` nodes from the front and `end` nodes from the back of the path.

**clear**: Removes all nodes from the path.

**GetTotalLength#2**: Calculates the total Euclidean distance between nodes from index `start` to `end`.

**GetTotalLength**: Calculates the total Euclidean distance of the entire path.

**GetPassedLength**: Calculates the distance from the start of the path to a specific point `(x, y, z)` relative to the node at `curnode`.

**operator[]**: Provides indexed access to a `PathNode` (non-const version).

**operator[]#2**: Provides indexed access to a `PathNode` (const version).

**set**: Sets the value of a specific node in the path by index.

---

<!-- machine-true, projected from graph.json -->

## Map — Path

*Source:* Path.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| size | function | — | — | — |
| empty | function | — | — | — |
| resize | function | — | — | — |
| crop | function | — | — | — |
| clear | function | — | — | — |
| GetTotalLength#2 | function | — | — | — |
| GetTotalLength | function | — | — | — |
| GetPassedLength | function | — | — | — |
| operator[] | function | — | — | — |
| operator[]#2 | function | — | — | — |
| set | function | — | — | — |
