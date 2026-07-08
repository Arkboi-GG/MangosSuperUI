# BIH

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# BIH (Bounding Interval Hierarchy)

## Purpose & Responsibilities

The `BIH` class implements a **Bounding Interval Hierarchy**, a spatial acceleration structure used for efficient ray-object intersection testing and point containment queries. It is designed primarily for rendering or physics engines (evidenced by the `intersectRay` and `intersectPoint` methods and the reference to "Sunflow, a Java Raytracer" in the header comments).

The core responsibilities of this unit are:
1.  **Construction**: Building a hierarchical tree structure from a set of primitives (geometric objects) by recursively subdividing space based on bounding boxes.
2.  **Traversal**: Efficiently traversing this hierarchy to identify candidate primitives that intersect with a given ray or contain a specific point, avoiding brute-force checks against all objects.
3.  **Persistence**: Serializing and deserializing the constructed hierarchy to/from binary files.
4.  **Statistics**: Collecting and reporting metrics on the quality of the built tree (depth, leaf distribution, etc.) during construction.

This unit does not store the geometric primitives themselves; it stores indices into an external array of primitives (managed by the caller via templates) and the internal tree structure (`tree` vector) and object indices (`objects` vector).

## Member-by-Member Behavior

### Construction and Hierarchy Building

**`buildHierarchy`**
Initializes the temporary tree buffer with a dummy root node and seeds the recursive subdivision process. It calculates the global bounding box (`gridBox`) from the provided primitive bounds and invokes `subdivide` to populate the tree.

**`subdivide`**
The core recursive algorithm for building the BIH. It partitions a range of primitives (`left` to `right`) based on their bounding boxes.
*   **Leaf Creation**: If the number of primitives is below `maxPrims` or the recursion depth exceeds `MAX_STACK_SIZE`, it creates a leaf node containing the indices of the primitives in that range.
*   **Splitting Logic**:
    *   It selects the axis with the largest extent (`primaryAxis`).
    *   It attempts to split the primitives into left and right subsets based on the midpoint of the current bounding box.
    *   **BVH2 Optimization**: If the primitives occupy a small fraction of the current node's bounding box (specifically, if `1.3f * nodeNewW < nodeBoxW`), it inserts a special "BVH2" node. This node explicitly clips the empty space, allowing the traversal logic to skip large empty regions more efficiently.
    *   **Empty Space Handling**: If a split results in one side being empty (all primitives go left or right), it handles this by potentially creating intermediate nodes to represent the empty space, ensuring the tree remains balanced and traversal logic remains consistent.
*   **Recursion**: It recursively calls itself for the left and right children, updating the bounding boxes (`gridBox`, `nodeBox`) and tracking statistics via `BuildStats`.

**`createNode`**
A static helper that writes a leaf node entry into the `tempTree` vector. It encodes the axis (3 for leaf), the starting index of the primitives, and the count of primitives in the leaf.

**`init_empty`**
Resets the `tree` and `objects` vectors and initializes the tree with a dummy root node. This ensures the tree is in a valid, albeit empty, state.

**`BIH` (Constructor)**
Calls `init_empty` to set up the initial state.

### Traversal (Ray and Point Intersection)

**`intersectRay`**
Traverses the BIH to find primitives intersected by a ray.
*   **Bounding Box Check**: First checks if the ray intersects the global `bounds`. If not, it returns immediately.
*   **Stack-Based Traversal**: Uses a fixed-size stack (`StackNode`) to manage backtracking.
*   **Node Processing**:
    *   **Interior Nodes**: Determines which child nodes (front/back relative to the ray) the ray enters. It updates the ray's `intervalMin` and `intervalMax` (entry/exit distances) as it descends. If the ray passes through both children, it pushes the farther child onto the stack and processes the nearer child first.
    *   **BVH2 Nodes**: Handles the special clipping nodes by adjusting the ray's interval to exclude the clipped empty space.
    *   **Leaf Nodes**: Iterates through the primitives stored in the leaf, invoking the `intersectCallback` for each. The callback determines if an actual intersection occurs and can update `maxDist` or signal to stop early (`stopAtFirst`).

**`intersectPoint`**
Traverses the BIH to find primitives containing a specific point `p`.
*   **Bounding Box Check**: Returns immediately if `p` is outside the global `bounds`.
*   **Stack-Based Traversal**: Similar to `intersectRay`, but uses simpler logic since points don't have direction or intervals.
*   **Node Processing**:
    *   **Interior Nodes**: Checks which side of the split plane the point lies on. If it lies in both (due to overlapping bounds or empty space handling), it pushes one child onto the stack and processes the other.
    *   **BVH2 Nodes**: Checks if the point falls within the clipped region. If not, it breaks out of the traversal for that branch.
    *   **Leaf Nodes**: Invokes the `intersectCallback` for each primitive in the leaf.

### Persistence

**`writeToFile`**
Serializes the BIH state to a binary file. It writes:
1.  Global bounding box low/high coordinates (3 floats each).
2.  Tree size and the tree data itself.
3.  Object count and the object indices array.
It verifies the write operation by checking the number of items written.

**`readFromFile`**
Deserializes the BIH state from a binary file. It reads the bounding box, tree size, tree data, object count, and object indices, resizing internal vectors accordingly. It verifies the read operation similarly.

### Utilities and Statistics

**`primCount`**
Returns the number of primitives stored in the hierarchy (`objects.size()`).

**`printStats`**
Prints detailed statistics about the built tree to `stdout`, including:
*   Total nodes and leaves.
*   Min/max/average number of objects per leaf.
*   Min/max/average tree depth.
*   Distribution of leaf sizes (0 to >4 objects).
*   Number and percentage of BVH2 nodes.

**`floatToRawIntBits`** / **`intBitsToFloat`**
Static helper functions that reinterpret cast `float` values to `uint32` and vice versa using unions. These are used to store floating-point split positions in the integer-based `tree` vector for compact storage and fast comparison.

## Cross-Unit Boundaries

*   **Called by `BoundsTrait.TileAssembler/convertWorld2`**:
    *   `BIH`: Constructs a new BIH instance.
    *   `writeToFile`: Saves the constructed hierarchy to disk.
    *   *Context*: This suggests that tile assembly processes generate world geometry, build a BIH for it, and persist it.

*   **Called by `BoundsTrait.WorldModel/writeFile` and `BoundsTrait.WorldModel/writeToFile`**:
    *   `writeToFile`: Used to serialize the BIH as part of a larger WorldModel persistence operation.

*   **Called by `BoundsTrait.WorldModel/readFile` and `BoundsTrait.WorldModel/readFromFile`**:
    *   `readFromFile`: Used to deserialize the BIH when loading a WorldModel from disk.

*   **Called by `MapTree/InitMap`**:
    *   `primCount`: Queries the number of primitives, likely to validate or configure the map initialization.
    *   `readFromFile`: Loads the pre-built BIH for the map.

*   **Calls out to `BuildStats/updateBVH2` and `BuildStats/updateInner`**:
    *   `subdivide`: Updates internal counters for BVH2 nodes and internal nodes during the build process. Note that `BuildStats` is a nested class within `BIH`, so these are technically internal calls, but the MAP lists them as cross-unit interactions, possibly due to how the analysis tool views nested classes. In reality, `BuildStats` is tightly coupled with `BIH`.

## Data Model

This unit does not interact with any database tables. All data is held in memory within `std::vector<uint32>` structures (`tree` and `objects`) and serialized to binary files.

## Notable Implementation Details

1.  **BVH2 Nodes**: The implementation includes a specialized node type (BVH2) that explicitly represents empty space. This is triggered when the primitives in a node occupy less than ~77% (`1/1.3`) of the node's bounding box volume along the split axis. This optimization allows the traversal code to quickly reject rays/points that enter the empty space, improving performance for sparse geometries.
2.  **Fixed-Size Stack**: Both `intersectRay` and `intersectPoint` use a fixed-size array `StackNode stack[MAX_STACK_SIZE]` for backtracking. `MAX_STACK_SIZE` is defined as 64. If the tree depth exceeds 64, this will cause a stack overflow (buffer overrun). This is a hard limit on the complexity of the geometry that can be safely queried.
3.  **Floating-Point Bit Casting**: The use of `floatToRawIntBits` and `intBitsToFloat` allows storing split positions as integers in the tree vector. This saves space (4 bytes instead of potentially more if using structs) and allows for fast bitwise operations during traversal. However, it relies on IEEE 754 representation and endianness consistency between build and load times.
4.  **Memory Management in `build`**: The `build` method allocates `dat.indices` and `dat.primBound` using `new[]` and manually deletes them at the end. This is prone to leaks if an exception is thrown during `buildHierarchy`. Modern C++ would prefer `std::vector` or smart pointers here.
5.  **Thread Safety**: The class is not thread-safe. Concurrent calls to `build`, `intersectRay`, or `intersectPoint` on the same instance will lead to data races. However, multiple instances can be used concurrently.
6.  **Dummy Root Node**: The tree always starts with a dummy leaf node `(3 << 30)` followed by two zeros. This simplifies the traversal logic by ensuring the root is always at index 0 and has a consistent structure, even if the tree is empty.

## Member Reference

**`buildHierarchy`**: Initializes the temporary tree buffer with a dummy root and seeds the recursive `subdivide` process to construct the BIH from the provided primitive data.

**`subdivide`**: Recursively partitions primitives into left/right subsets based on bounding box extents, creating interior nodes, BVH2 clipping nodes, or leaf nodes, while updating build statistics.

**`floatToRawIntBits`**: Static utility that reinterprets a `float` as a `uint32` using a union, used for compact storage of split positions in the tree.

**`intBitsToFloat`**: Static utility that reinterprets a `uint32` as a `float` using a union, used to retrieve split positions during traversal.

**`init_empty`**: Resets the internal `tree` and `objects` vectors and initializes the tree with a dummy root node, ensuring a valid empty state.

**`BIH`**: Constructor that calls `init_empty` to set up the initial state of the hierarchy.

**`primCount`**: Returns the number of primitives currently stored in the hierarchy (`objects.size()`).

**`writeToFile`**: Serializes the global bounds, tree structure, and object indices to a binary file, verifying the write count.

**`readFromFile`**: Deserializes the global bounds, tree structure, and object indices from a binary file, resizing internal vectors and verifying the read count.

**`updateLeaf`**: (Member of nested `BuildStats` class) Updates statistics for a newly created leaf node, including depth, object count, and distribution histograms.

**`printStats`**: (Member of nested `BuildStats` class) Prints detailed metrics about the built tree (nodes, leaves, depth, object distribution, BVH2 usage) to standard output.

**`createNode`**: Static helper that writes a leaf node entry into the `tempTree` vector, encoding the axis (3), start index, and count of primitives.

---

<!-- machine-true, projected from graph.json -->

## Map — BIH

*Source:* BIH.cpp, BIH.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| buildHierarchy | method | — | — | — |
| subdivide | method | BuildStats/updateBVH2, BuildStats/updateInner | — | — |
| floatToRawIntBits | function | — | — | — |
| intBitsToFloat | function | — | — | — |
| init_empty | method | — | — | — |
| BIH | ctor | — | BoundsTrait.TileAssembler/convertWorld2 | — |
| primCount | method | — | MapTree/InitMap | — |
| writeToFile | method | — | BoundsTrait.TileAssembler/convertWorld2, BoundsTrait.WorldModel/writeFile, BoundsTrait.WorldModel/writeToFile | — |
| readFromFile | method | — | BoundsTrait.WorldModel/readFile, BoundsTrait.WorldModel/readFromFile, MapTree/InitMap | — |
| updateLeaf | method | — | — | — |
| printStats | method | — | — | — |
| createNode | method | — | — | — |
