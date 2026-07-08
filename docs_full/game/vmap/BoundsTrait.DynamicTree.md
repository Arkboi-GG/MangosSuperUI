<!-- provenance: failed-members -->
# BoundsTrait.DynamicTree

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# BoundsTrait.DynamicTree

## Purpose & Responsibilities

`BoundsTrait.DynamicTree` implements the runtime spatial indexing system for dynamic collision objects (`GameObjectModel`) within the `wowvmangos` world simulation. It provides a high-performance interface for inserting, removing, and querying dynamic game objects (such as players, NPCs, and movable props) against other dynamic objects.

The core component is the `DynamicMapTree` class, which acts as a facade over an internal implementation (`DynTreeImpl`). This implementation uses a hybrid spatial partitioning structure: a `RegularGrid2D` combined with a Bounding Interval Hierarchy (`BIHWrap`) to manage `GameObjectModel` instances. The system supports automatic rebalancing based on time intervals and modification counts to maintain query performance.

Additionally, this unit defines several callback structs (`DynamicTreeIntersectionCallback`, etc.) used during ray-casting operations to detect intersections, determine line-of-sight, calculate ground height, and identify specific colliding objects. These callbacks bridge the gap between the generic spatial tree traversal logic and the specific intersection logic of `GameObjectModel`.

**Note:** While `WorldModel.cpp` is included in the source files, it contains implementations for `VMAP::GroupModel`, `VMAP::WorldModel`, and `VMAP::WmoLiquid`, which handle *static* world model (WMO/M2) collision data. These classes are **not** part of the `BoundsTrait.DynamicTree` unit's responsibilities. The `BoundsTrait` template specialization for `VMAP::GroupModel` in `WorldModel.cpp` is distinct from the `BoundsTrait` specializations for `GameObjectModel` in `DynamicTree.cpp`. This documentation focuses exclusively on the `DynamicMapTree` and its associated traits/callbacks defined in `DynamicTree.cpp` and `DynamicTree.h`.

## Member-by-Member Behavior

### Spatial Traits and Helpers

These template specializations allow the underlying spatial grid/tree structures to interact with `GameObjectModel` instances.

*   **`hashCode`**: A static method in the `HashTrait<GameObjectModel>` specialization. It generates a hash code for a `GameObjectModel` by casting its memory address to a `size_t`. This allows the spatial grid to use pointer identity for hashing, ensuring unique entries for distinct object instances.
*   **`getPosition`**: A static method in the `PositionTrait<GameObjectModel>` specialization. It retrieves the center position of a `GameObjectModel` by calling `GameObjectModel::getPosition` (defined in `GameObjectModel`). This is used by the spatial index to place the object in the correct grid cell or node.
*   **`getBounds`**: A static method in the `BoundsTrait<GameObjectModel>` specialization. It retrieves the Axis-Aligned Bounding Box (AABB) of a `GameObjectModel` by calling `GameObjectModel::getBounds` (defined in `GameObjectModel`). This bounding box is used for broad-phase collision detection and spatial partitioning.
*   **`getBounds2`**: A static method in the `BoundsTrait<GameObjectModel>` specialization, taking a pointer instead of a reference. It also retrieves the AABB via `GameObjectModel::getBounds`. This variant likely exists to satisfy different API requirements of the underlying spatial library (`RegularGrid2D` or `BIHWrap`) that may expect pointer-based accessors.

### Internal Implementation (`DynTreeImpl`)

`DynTreeImpl` is a private nested struct within `DynamicTree.cpp` that encapsulates the actual spatial data structure and management logic. It inherits from `RegularGrid2D<GameObjectModel, BIHWrap<GameObjectModel>>`.

*   **`DynTreeImpl` (ctor)**: Initializes the internal timer (`rebalance_timer`) with a period defined by `CHECK_TREE_PERIOD` (200 ms) and sets the `unbalanced_times` counter to zero. It constructs the parent `RegularGrid2D` structure.
*   **`insert`**: Adds a `GameObjectModel` to the spatial grid by calling the base class `insert`. It increments the `unbalanced_times` counter, signaling that the tree structure may need rebalancing.
*   **`remove`**: Removes a `GameObjectModel` from the spatial grid by calling the base class `remove`. It also increments the `unbalanced_times` counter.
*   **`balance`**: Rebuilds or rebalances the underlying spatial tree by calling the base class `balance`. It resets the `unbalanced_times` counter to zero, indicating the tree is now balanced.
*   **`update`**: Performs periodic maintenance. It updates the `rebalance_timer` with the elapsed time (`difftime`). If the timer has passed and the tree has been modified (`unbalanced_times > 0`), it triggers a `balance()` operation and resets the timer. This ensures that frequent small changes don't immediately trigger expensive rebalancing, but significant drift is corrected periodically.

### Public Interface (`DynamicMapTree`)

`DynamicMapTree` is the primary public class exposed to the rest of the engine. It manages the lifetime of the `DynTreeImpl` instance.

*   **`DynamicMapTree` (ctor)**: Allocates a new `DynTreeImpl` instance on the heap and binds the `impl` reference to it.
*   **`~DynamicMapTree` (dtor)**: Deletes the `DynTreeImpl` instance. Note: The code uses `delete &impl`, which is unusual since `impl` is a reference. This implies `impl` was bound to a heap-allocated object in the constructor, and the destructor takes ownership of deleting it. This is a fragile pattern relying on the specific initialization in the constructor.
*   **`insert#2`**: Delegates to `DynTreeImpl::insert`. Called by `Map.Main::InsertGameObjectModel` when a new dynamic object enters the world or becomes active.
*   **`remove#2`**: Delegates to `DynTreeImpl::remove`. Called by `Map.Main::RemoveGameObjectModel` when an object leaves the world or becomes inactive.
*   **`contains`**: Checks if a `GameObjectModel` is present in the tree by delegating to `DynTreeImpl::contains`. Called by `Map.Main::ContainsGameObjectModel`.
*   **`balance#2`**: Delegates to `DynTreeImpl::balance`. Called explicitly by `Map.Main::InsertGameObjectModel` and `Map.Main::RemoveGameObjectModel` in certain scenarios, possibly to force immediate consistency after bulk operations or critical changes.
*   **`size`**: Returns the number of objects in the tree by delegating to `DynTreeImpl::size`.
*   **`update#2`**: Delegates to `DynTreeImpl::update`. Called by `Map.Main::Update#3` during the main game loop tick to perform periodic rebalancing.

### Intersection Callbacks

These structs are used as functors during ray-casting queries. They capture state (like whether a hit occurred) and define the logic for testing individual objects.

*   **`DynamicTreeIntersectionCallback` (ctor)**: Initializes `did_hit` to false. Used for simple boolean intersection checks.
*   **`operator()`**: The functor operator for `DynamicTreeIntersectionCallback`. It calls `GameObjectModel::intersectRay` to test if the ray hits the object. It stores the result in `did_hit` and returns it. This is used by `getIntersectionTime`, `isInLineOfSight`, and `getHeight`.
*   **`didHit`**: Returns the `did_hit` flag.

*   **`DynamicTreeIntersectionCallback_WithLogger` (ctor)**: Initializes `did_hit` to false and logs a debug message "Dynamic Intersection log" via `Log.Main::Out`. Used for debugging intersection queries.
*   **`operator()#2`**: Logs the name of the object being tested via `Log.Main::Out`, then calls `GameObjectModel::intersectRay`. If a hit occurs, it sets `did_hit` to true and logs "result: intersects". Returns the hit status.
*   **`didHit#2`**: Returns the `did_hit` flag.

*   **`DynamicTreeIntersectionCallback_findCollisionObject` (ctor)**: Initializes `did_hit` to false and `hitObj` to `nullptr`. Used when the specific object hit needs to be identified.
*   **`operator()#3`**: Calls `GameObjectModel::intersectRay`. If a hit occurs, it stores the pointer to the object in `hitObj` and sets `did_hit` to true. Crucially, it returns `did_hit` (which is true if *any* hit has occurred so far), which may influence the traversal order or early-out behavior of the ray-caster depending on how the underlying tree interprets the return value.
*   **`didHit#3`**: Returns the `did_hit` flag.

### Query Methods

These methods perform specific geometric queries using the spatial tree and the appropriate callbacks.

*   **`getIntersectionTime`**: Determines if a ray intersects any object within a maximum distance (`pMaxDist`). It creates a `DynamicTreeIntersectionCallback`, performs the ray cast via `impl.intersectRay`, and if a hit is detected, updates `pMaxDist` to the actual intersection distance. Returns true if a hit occurred.
*   **`getObjectHitPos#2`**: An overloaded convenience method that takes raw float coordinates for start and end positions. It converts them to `Vector3`s, calls the main `getObjectHitPos` method, and unpacks the result vector back into floats.
*   **`getObjectHitPos`**: Calculates the exact position where a ray from `pPos1` to `pPos2` hits an object.
    *   It first checks for degenerate cases (zero distance or identical points) to avoid division by zero or NaNs, returning false and setting the result to `pPos2`.
    *   It constructs a normalized direction vector and a `G3D::Ray`.
    *   It calls `getIntersectionTime` to find the distance to the nearest hit.
    *   If a hit is found, it calculates the hit position (`pPos1 + dir * dist`).
    *   It applies `pModifyDist`: if negative, it pushes the hit position *back* towards the start point (useful for preventing clipping into walls), clamping it to not go past the start point. If positive, it pushes the hit position *forward* along the ray.
    *   Returns true if a hit occurred, false otherwise.
*   **`isInLineOfSight`**: Checks if there is a clear line of sight between two points.
    *   It handles degenerate cases (zero distance) by returning true.
    *   It constructs a ray and uses `DynamicTreeIntersectionCallback` to check for intersections.
    *   Returns `!callback.did_hit`, meaning true if no object blocked the path.
    *   Calls `Errors::PrintStacktraceAndThrow` if assertions fail (though the assertion is on `maxDist` magnitude, which is unlikely to fail with valid map coords).
*   **`getHeight`**: Finds the height of the highest object below a given point `(x, y, z)` within a `maxSearchDist`.
    *   It casts a ray downwards (`Vector3::down()`).
    *   Uses `DynamicTreeIntersectionCallback` to find the first intersection.
    *   If a hit is found, it returns the Z-coordinate of the hit point. The calculation `v.z - maxSearchDist` seems incorrect if `maxSearchDist` is the *limit* of the search, not the distance traveled. However, looking at `impl.intersectZAllignedRay`, it likely modifies the ray or distance. *Correction*: The code returns `v.z - maxSearchDist` if `callback.didHit()` is true. This suggests `maxSearchDist` might be interpreted differently or there's a bug. Typically, one would return `ray.origin().z - distance`. Let's look closer. `impl.intersectZAllignedRay` is called. If it hits, `callback.didHit()` is true. The return value `v.z - maxSearchDist` is suspicious. If `maxSearchDist` is the max distance to search, and we hit at distance `d`, the height should be `v.z - d`. The code doesn't seem to retrieve `d` from the callback. This might be a bug or `intersectZAllignedRay` behaves differently. Given the constraint to not invent, I will describe the code as written: it returns `v.z - maxSearchDist` on a hit, which is likely intended to be the floor height but the logic appears flawed unless `maxSearchDist` is updated by the intersection routine (which isn't visible here). *Re-evaluating*: `intersectZAllignedRay` signature isn't shown, but standard raycasting returns distance. If `maxSearchDist` is passed by value, it won't be updated. This is a notable implementation detail/gotcha.
*   **`getObjectHit`**: Identifies the specific `GameObjectModel` hit by a ray from `pPos1` to `pPos2`.
    *   It constructs a ray and uses `DynamicTreeIntersectionCallback_findCollisionObject`.
    *   After the ray cast, it returns the `hitObj` pointer stored in the callback, or `nullptr` if no hit occurred.

## Cross-Unit Boundaries

*   **`GameObjectModel`**:
    *   **Direction**: `BoundsTrait.DynamicTree` calls into `GameObjectModel`.
    *   **Why**: To retrieve spatial data (`getPosition`, `getBounds`) for indexing and to perform detailed intersection tests (`intersectRay`) during queries. `GameObjectModel` represents the physical collision mesh of a dynamic entity.
*   **`ShortTimeTracker`**:
    *   **Direction**: `BoundsTrait.DynamicTree` calls into `ShortTimeTracker`.
    *   **Why**: To manage the periodic rebalancing of the spatial tree. `DynTreeImpl` uses `ShortTimeTracker` to track elapsed time since the last balance check.
*   **`Map.Main`**:
    *   **Direction**: `Map.Main` calls into `BoundsTrait.DynamicTree`.
    *   **Why**: `Map.Main` is responsible for managing game objects on a map. It uses `DynamicMapTree` to insert/remove objects when they enter/leave the map (`InsertGameObjectModel`, `RemoveGameObjectModel`), check for existence (`ContainsGameObjectModel`), and perform periodic updates (`Update#3`). It also uses the tree for collision queries like `GetDynamicObjectHitPos`, `CheckDynamicTreeLoS`, `GetDynamicTreeHeight`, and `FindDynamicObjectCollisionModel`.
*   **`Log.Main`**:
    *   **Direction**: `BoundsTrait.DynamicTree` calls into `Log.Main`.
    *   **Why**: The `DynamicTreeIntersectionCallback_WithLogger` callback logs debug information about intersection tests, aiding in troubleshooting collision issues.
*   **`Errors`**:
    *   **Direction**: `BoundsTrait.DynamicTree` calls into `Errors`.
    *   **Why**: `getObjectHitPos` and `isInLineOfSight` contain assertions (`MANGOS_ASSERT`) that check for valid coordinate ranges. If these fail, `Errors::PrintStacktraceAndThrow` is invoked to halt execution and report the error.

## Data Model

This unit does not interact directly with any database tables. All data is held in memory within the `DynamicMapTree` structure and the `GameObjectModel` instances it indexes.

## Notable Implementation Details

1.  **Memory Management Pattern**: `DynamicMapTree` uses a reference member `struct DynTreeImpl& impl;`. In the constructor, it allocates `DynTreeImpl` on the heap and binds the reference. The destructor deletes the object via `delete &impl;`. This is non-standard C++ practice. References typically do not own memory. This pattern relies on the assumption that `impl` is always bound to a heap-allocated object created in the constructor. If `DynamicMapTree` were copied or moved (it's not, due to the reference member preventing default copy/move), this would break. It effectively makes `DynamicMapTree` non-copyable and non-movable, which is acceptable, but the ownership semantics are obscure.
2.  **Periodic Rebalancing**: The tree does not rebalance after every insert/remove. Instead, it counts modifications (`unbalanced_times`) and only rebalances if the `CHECK_TREE_PERIOD` (200ms) has elapsed *and* modifications have occurred. This optimizes performance for scenes with many small, frequent changes.
3.  **Degenerate Case Handling**: `getObjectHitPos` explicitly checks for zero-length rays (`maxDist < 1e-10f` or `pPos1 == pPos2`) to prevent division by zero and NaN propagation, which could cause infinite loops in the underlying BIH intersection logic.
4.  **Height Calculation Suspicions**: The `getHeight` method returns `v.z - maxSearchDist` if a hit is detected. This logic appears potentially incorrect. Standard raycasting would return `origin.z - hit_distance`. If `maxSearchDist` is merely the maximum search range and not the actual hit distance, this will return an incorrect height. The `intersectZAllignedRay` method's behavior regarding distance output is not visible in this unit, but the lack of retrieving a hit distance from the callback suggests a potential bug or reliance on side-effects not shown.
5.  **Callback Return Values**: The `DynamicTreeIntersectionCallback_findCollisionObject::operator()` returns `did_hit`. In many ray-traversal algorithms, returning `true` can signal "stop traversing" (early out). If the goal is to find the *closest* object, the callback should usually return `false` to allow the traversal to continue finding closer hits, unless the tree traversal guarantees closest-first ordering. The comment in `DynamicTreeIntersectionCallback_findCollisionObject` doesn't specify, but `operator()` in `DynamicTreeIntersectionCallback` returns `did_hit` as well. If the underlying `BIHWrap` or `RegularGrid2D` stops on `true`, this might only find the *first* hit encountered, not necessarily the closest one, depending on the traversal order. This is a critical detail for collision accuracy.
6.  **Static vs. Dynamic Separation**: The inclusion of `WorldModel.cpp` in the source list is misleading for this unit. `WorldModel` and `GroupModel` handle static WMO/M2 data and use their own `BIH` trees (`meshTree`, `groupTree`). `DynamicMapTree` is strictly for `GameObjectModel` (dynamic entities). The `BoundsTrait` specializations are distinct. Engineers must not confuse the static world model collision system with this dynamic tree.

## Member Reference

*   **hashCode**: Static method in `HashTrait<GameObjectModel>`. Generates a hash from the object's memory address.
*   **getPosition**: Static method in `PositionTrait<GameObjectModel>`. Retrieves the object's center position via `GameObjectModel::getPosition`.
*   **getBounds**: Static method in `BoundsTrait<GameObjectModel>`. Retrieves the object's AABB via `GameObjectModel::getBounds`.
*   **getBounds2**: Static method in `BoundsTrait<GameObjectModel>`. Retrieves the object's AABB via `GameObjectModel::getBounds`, accepting a pointer.
*   **DynTreeImpl**: Constructor for the internal implementation struct. Initializes the rebalance timer and unbalanced counter.
*   **insert**: Method in `DynTreeImpl`. Adds an object to the spatial grid and increments the unbalanced counter.
*   **remove**: Method in `DynTreeImpl`. Removes an object from the spatial grid and increments the unbalanced counter.
*   **balance**: Method in `DynTreeImpl`. Rebalances the spatial tree and resets the unbalanced counter.
*   **update**: Method in `DynTreeImpl`. Updates the rebalance timer and triggers `balance()` if the period has passed and modifications occurred.
*   **DynamicMapTree**: Constructor for the public facade class. Allocates `DynTreeImpl` on the heap.
*   **~DynamicMapTree**: Destructor for the public facade class. Deletes the `DynTreeImpl` instance.
*   **insert#2**: Method in `DynamicMapTree`. Delegates to `DynTreeImpl::insert`. Called by `Map.Main::InsertGameObjectModel`.
*   **remove#2**: Method in `DynamicMapTree`. Delegates to `DynTreeImpl::remove`. Called by `Map.Main::RemoveGameObjectModel`.
*   **contains**: Method in `DynamicMapTree`. Delegates to `DynTreeImpl::contains`. Called by `Map.Main::ContainsGameObjectModel`.
*   **balance#2**: Method in `DynamicMapTree`. Delegates to `DynTreeImpl::balance`. Called by `Map.Main::InsertGameObjectModel` and `Map.Main::RemoveGameObjectModel`.
*   **size**: Method in `DynamicMapTree`. Delegates to `DynTreeImpl::size`.
*   **update#2**: Method in `DynamicMapTree`. Delegates to `DynTreeImpl::update`. Called by `Map.Main::Update#3`.
*   **DynamicTreeIntersectionCallback**: Constructor for a callback struct used for simple boolean intersection checks.
*   **operator()**: Functor operator for `DynamicTreeIntersectionCallback`. Calls `GameObjectModel::intersectRay` and stores the result.
*   **didHit**: Method in `DynamicTreeIntersectionCallback`. Returns the hit status.
*   **DynamicTreeIntersectionCallback_WithLogger**: Constructor for a callback struct that logs intersection attempts. Calls `Log.Main::Out`.
*   **operator()#2**: Functor operator for `DynamicTreeIntersectionCallback_WithLogger`. Logs object name, calls `GameObjectModel::intersectRay`, and logs results.
*   **didHit#2**: Method in `DynamicTreeIntersectionCallback_WithLogger`. Returns the hit status.
*   **DynamicTreeIntersectionCallback_findCollisionObject**: Constructor for a callback struct that identifies the hit object.
*   **operator()#3**: Functor operator for `DynamicTreeIntersectionCallback_findCollisionObject`. Calls `GameObjectModel::intersectRay` and stores the hit object pointer.
*   **didHit#3**: Method in `DynamicTreeIntersectionCallback_findCollisionObject`. Returns the hit status.
*   **getIntersectionTime**: Method in `DynamicMapTree`. Checks for ray intersection within a max distance and updates the distance if hit.
*   **getObjectHitPos#2**: Overloaded method in `DynamicMapTree`. Converts float coordinates to `Vector3` and calls the main `getObjectHitPos`.
*   **getObjectHitPos**: Method in `DynamicMapTree`. Calculates the exact hit position of a ray, applying modification distance. Calls `Errors::PrintStacktraceAndThrow` on assertion failure. Called by `Map.Main::GetDynamicObjectHitPos`.
*   **isInLineOfSight**: Method in `DynamicMapTree`. Checks if a path between two points is clear. Calls `Errors::PrintStacktraceAndThrow` on assertion failure. Called by `Map.Main::CheckDynamicTreeLoS`.
*   **getHeight**: Method in `DynamicMapTree`. Finds the height of the highest object below a point. Called by `Map.Main::GetDynamicTreeHeight`.
*   **getObjectHit**: Method in `DynamicMapTree`. Identifies the specific `GameObjectModel` hit by a ray. Called by `Map.Main::FindDynamicObjectCollisionModel`.

---

<!-- machine-true, projected from graph.json -->

## Map — BoundsTrait.DynamicTree

*Source:* DynamicTree.cpp, DynamicTree.h, WorldModel.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| hashCode | method | — | — | — |
| getPosition | method | GameObjectModel/getPosition | — | — |
| getBounds | method | GameObjectModel/getBounds | — | — |
| getBounds2 | method | GameObjectModel/getBounds | — | — |
| DynTreeImpl | ctor | ShortTimeTracker/ShortTimeTracker | — | — |
| insert | method | — | — | — |
| remove | method | — | — | — |
| balance | method | — | — | — |
| update | method | ShortTimeTracker/Passed, ShortTimeTracker/Reset, ShortTimeTracker/Update | — | — |
| DynamicMapTree | ctor | — | — | — |
| ~DynamicMapTree | dtor | — | — | — |
| insert#2 | method | — | Map.Main/InsertGameObjectModel | — |
| remove#2 | method | — | Map.Main/RemoveGameObjectModel | — |
| contains | method | — | Map.Main/ContainsGameObjectModel | — |
| balance#2 | method | — | Map.Main/InsertGameObjectModel, Map.Main/RemoveGameObjectModel | — |
| size | method | — | — | — |
| update#2 | method | — | Map.Main/Update#3 | — |
| DynamicTreeIntersectionCallback | ctor | — | — | — |
| operator() | method | GameObjectModel/intersectRay | — | — |
| didHit | method | — | — | — |
| DynamicTreeIntersectionCallback_WithLogger | ctor | Log.Main/Out | — | — |
| operator()#2 | method | GameObjectModel/intersectRay, Log.Main/Out | — | — |
| didHit#2 | method | — | — | — |
| DynamicTreeIntersectionCallback_findCollisionObject | ctor | — | — | — |
| operator()#3 | method | GameObjectModel/intersectRay | — | — |
| didHit#3 | method | — | — | — |
| getIntersectionTime | method | — | — | — |
| getObjectHitPos#2 | method | — | — | — |
| getObjectHitPos | method | Errors/PrintStacktraceAndThrow | Map.Main/GetDynamicObjectHitPos | — |
| isInLineOfSight | method | Errors/PrintStacktraceAndThrow | Map.Main/CheckDynamicTreeLoS | — |
| getHeight | method | — | Map.Main/GetDynamicTreeHeight | — |
| getObjectHit | method | — | Map.Main/FindDynamicObjectCollisionModel | — |

---

<!-- verify: failed-members | invented: operator -->
