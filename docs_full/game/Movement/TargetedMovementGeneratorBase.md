# TargetedMovementGeneratorBase

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# TargetedMovementGeneratorBase

**Purpose & Responsibilities**

`TargetedMovementGeneratorBase` is a minimal base class within the MaNGOS movement generation system. Its sole responsibility is to manage the lifetime linkage between a movement generator instance and the `Unit` it is targeting (chasing or following). It establishes this link via a `FollowerReference` object, ensuring that if the target `Unit` is destroyed, the movement generator is notified and can clean up its association. It provides no movement logic itself; all behavioral logic is implemented in derived classes (`TargetedMovementGeneratorMedium`, `ChaseMovementGenerator`, `FollowMovementGenerator`) which are declared in the same header but belong to different conceptual units.

This unit is strictly a structural anchor. It contains no database interactions, no pathfinding logic, and no coordinate calculations.

## Member-by-Member Behavior

### Initialization and Linking
The constructor `TargetedMovementGeneratorBase` initializes the internal `FollowerReference` member `i_target`. It passes a pointer to the target `Unit` and a pointer to `this` (the generator instance) to `i_target.link()`. This creates a weak-reference-style relationship where the `FollowerReference` tracks the validity of the target `Unit`. If the target `Unit` is deleted, the `FollowerReference` mechanism ensures that the generator is informed, preventing dangling pointers.

### Cleanup Notification
The method `stopFollowing` is a virtual hook intended to be overridden by derived classes to perform cleanup when the target link is broken. In `TargetedMovementGeneratorBase` itself, this method is empty. It serves as a placeholder for the interface contract required by the `FollowerReference` system.

## Cross-Unit Boundaries

### Collaboration with `FollowerReference`
*   **Direction:** `TargetedMovementGeneratorBase` calls into `FollowerReference` during construction.
*   **Mechanism:** The constructor invokes `i_target.link(&target, this)`.
*   **Purpose:** To register the movement generator as a dependent of the target `Unit`. This allows the `FollowerReference` system to notify the generator if the target `Unit` is destroyed.

### Collaboration with `FollowerReference` (Incoming Call)
*   **Direction:** `FollowerReference` calls into `TargetedMovementGeneratorBase`.
*   **Member:** `stopFollowing`
*   **Context:** As indicated in the MAP, `stopFollowing` is called by `FollowerReference::sourceObjectDestroyLink`.
*   **Purpose:** When the target `Unit` (the "source object" of the reference) is destroyed, `FollowerReference` triggers this callback to allow the movement generator to cease operations related to that target. While the base implementation is empty, derived classes use this to interrupt movement paths or reset states.

## Data Model

This unit interacts with no database tables. It operates entirely on in-memory `Unit` objects and reference counts.

## Notable Implementation Details

1.  **Empty Base Implementation:** The `stopFollowing` method is explicitly empty `{ }`. This is intentional design; the base class defines the interface, but the actual cleanup logic (such as stopping pathfinding or resetting timers) is implemented in the derived `TargetedMovementGeneratorMedium` or further down the hierarchy.
2.  **Template Dependency:** Although `TargetedMovementGeneratorBase` is not a template, it is always inherited by `TargetedMovementGeneratorMedium`, which is a template class. This means `TargetedMovementGeneratorBase` is effectively part of a templated inheritance chain, but its own code remains non-templated and simple.
3.  **No Virtual Destructor:** The class does not define a destructor. Since it is a base class for polymorphic usage (via `stopFollowing` being called virtually or through the reference system), relying on implicit destruction is standard here because the derived classes handle the complex resource management. However, note that `TargetedMovementGeneratorMedium` has an explicit destructor, suggesting careful lifecycle management in the derived layers.

## Member Reference

**TargetedMovementGeneratorBase**
Constructor that initializes the `i_target` member by linking the provided `Unit` reference to this generator instance via `FollowerReference::link`.

**stopFollowing**
An empty method that serves as a callback hook for the `FollowerReference` system. It is invoked by `FollowerReference::sourceObjectDestroyLink` when the target `Unit` is destroyed, allowing derived classes to override it and perform necessary cleanup.

---

<!-- machine-true, projected from graph.json -->

## Map — TargetedMovementGeneratorBase

*Source:* TargetedMovementGenerator.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| TargetedMovementGeneratorBase | ctor | — | — | — |
| stopFollowing | method | — | FollowerReference/sourceObjectDestroyLink | — |
