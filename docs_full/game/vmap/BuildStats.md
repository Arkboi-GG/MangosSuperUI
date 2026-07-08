# BuildStats

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`BuildStats` is a lightweight, internal helper class nested within `BIH` (Bounding Interval Hierarchy) in `BIH.h`. Its sole responsibility is to accumulate statistical metrics during the construction phase of the BIH spatial acceleration structure. Specifically, it tracks node counts, leaf distribution, object counts per leaf, and tree depth statistics. These metrics are used to evaluate the quality of the built hierarchy (e.g., balance, depth, and leaf occupancy) and can be printed for debugging or optimization tuning via the `printStats()` method (which is declared in the shared header but implemented elsewhere, likely in the corresponding `.cpp` file or another partial).

Because `BuildStats` is a `private` nested class of `BIH`, it is not exposed to external callers. It is instantiated locally within `BIH::build()` and passed by reference to the recursive `buildHierarchy()` and `subdivide()` methods.

## Member-by-Member Behavior

The `BuildStats` class consists of a constructor and two update methods. All members operate on simple integer counters stored as private data members.

### Construction
**`BuildStats`** initializes all statistical counters to neutral or extreme values suitable for accumulation:
- `numNodes`, `numLeaves`, `sumObjects`, `sumDepth`, and `numBVH2` are set to `0`.
- `minObjects` and `minDepth` are initialized to `0x0FFFFFFF` (a large positive integer), ensuring that the first valid value encountered will replace them.
- `maxObjects` and `maxDepth` are initialized to `0xFFFFFFFF` (the maximum unsigned 32-bit integer), ensuring the first valid value will replace them.
- The array `numLeavesN` (size 6) is zeroed out. This array likely categorizes leaves by the number of primitives they contain (e.g., leaves with 1 primitive, 2 primitives, etc.), though the specific bucketing logic resides in the `updateLeaf` method (not part of this unit's MAP).

### Update Methods
**`updateInner`** increments the `numNodes` counter. This method is called whenever an internal (non-leaf) node is created in the hierarchy. It signals that the tree is branching further.

**`updateBVH2`** increments the `numBVH2` counter. This method is called when a specific type of internal node, referred to as "BVH2," is created. Based on the `BIH::intersectRay` and `BIH::intersectPoint` code, BVH2 nodes represent a special case where empty space is cut off on both sides of a split plane (indicated by the bit `1 << 29` in the node encoding). Tracking these separately allows developers to assess how often this optimization is applied during tree construction.

## Cross-Unit Boundaries

`BuildStats` has no external dependencies. It does not call into any other units. However, it is tightly coupled with the `BIH` class itself:

- **Called by `BIH/subdivide`**: The `subdivide` method (part of the `BIH` class, defined in the same header but logically part of the hierarchy building process) invokes `updateInner` and `updateBVH2` to record the creation of internal nodes. This collaboration ensures that every structural decision made during the recursive subdivision of the bounding volume hierarchy is reflected in the final statistics.

## Data Model

`BuildStats` does not interact with any database tables. It operates entirely in memory using primitive integer types.

## Notable Implementation Details

1. **Nested Scope**: `BuildStats` is defined as a `private` nested class within `BIH`. This encapsulation ensures that the statistics are only accessible during the build process and cannot be queried or modified by external code after the tree is constructed.
2. **Initialization Strategy**: The use of `0x0FFFFFFF` for minimums and `0xFFFFFFFF` for maximums is a standard idiom for finding min/max values in a single pass. It avoids the need for separate "first item" flags.
3. **BVH2 Specificity**: The existence of a dedicated counter for `numBVH2` highlights that the BIH implementation distinguishes between standard split nodes and nodes that represent empty-space cutoffs. This distinction is critical for understanding the tree's efficiency, as BVH2 nodes reduce traversal work by eliminating empty regions.
4. **No Thread Safety**: The class assumes single-threaded usage during the build phase, which is consistent with the typical sequential nature of spatial index construction in this codebase.

## Member Reference

**BuildStats**  
Constructor that initializes all statistical counters. Sets counts (`numNodes`, `numLeaves`, `sumObjects`, `sumDepth`, `numBVH2`) to zero, minimums (`minObjects`, `minDepth`) to `0x0FFFFFFF`, maximums (`maxObjects`, `maxDepth`) to `0xFFFFFFFF`, and zeros the `numLeavesN` array.

**updateInner**  
Increments the `numNodes` counter. Called by `BIH/subdivide` when a standard internal node is added to the hierarchy.

**updateBVH2**  
Increments the `numBVH2` counter. Called by `BIH/subdivide` when a BVH2 node (empty-space cutoff node) is added to the hierarchy.

---

<!-- machine-true, projected from graph.json -->

## Map — BuildStats

*Source:* BIH.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| BuildStats | ctor | — | — | — |
| updateInner | method | — | BIH/subdivide | — |
| updateBVH2 | method | — | BIH/subdivide | — |
