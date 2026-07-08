# AreaInfo

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AreaInfo

**AreaInfo** is a lightweight data structure within the `VMAP` namespace, defined in `MapTree.h`. It serves as a container for the results of area-specific queries performed by the virtual map system. Specifically, it aggregates geometric and logical metadata about a specific point in 3D space relative to the game world's terrain and model hierarchy.

The structure holds six fields:
1.  **result**: A boolean indicating whether the query successfully retrieved valid area information.
2.  **ground_Z**: The height of the ground at the queried location, initialized to negative infinity (`-G3D::inf()`) to signify an invalid or uncomputed value.
3.  **flags**: A bitmask (`uint32`) likely representing area attributes such as water, flight zones, or other environmental properties.
4.  **adtId**: An identifier (`int32`) for the ADT (Area Description Table) tile, which corresponds to the underlying terrain grid cell.
5.  **rootId**: An identifier (`int32`) for the root node in the model hierarchy, used for collision and visibility calculations.
6.  **groupId**: An identifier (`int32`) for the group within the model hierarchy.

This structure is designed to be populated by methods such as `StaticMapTree::getAreaInfo`, allowing callers to retrieve multiple pieces of contextual data in a single operation without requiring multiple separate queries.

## Member Reference

**AreaInfo**
Default constructor that initializes all members to safe default values. Sets `result` to `false`, `ground_Z` to negative infinity, `flags` to `0`, and all ID fields (`adtId`, `rootId`, `groupId`) to `0`. This ensures that an uninitialized `AreaInfo` object clearly indicates invalid data until explicitly populated by a successful query.

---

<!-- machine-true, projected from graph.json -->

## Map — AreaInfo

*Source:* MapTree.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AreaInfo | ctor | — | — | — |
