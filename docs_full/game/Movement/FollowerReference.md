# FollowerReference

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`FollowerReference` is a lightweight adapter class within the `wowvmangos` engine that manages the lifecycle notifications for a "follower" relationship between two game entities. It inherits from `Reference<Unit, TargetedMovementGeneratorBase>`, indicating it acts as a bidirectional link where the **source** is a `TargetedMovementGeneratorBase` (the entity doing the following) and the **target** is a `Unit` (the entity being followed).

The primary responsibility of `FollowerReference` is to ensure that when the underlying reference link is established or broken, the connected objects are notified appropriately to maintain consistency in their internal states. Specifically:
1.  When the link to the **target** (`Unit`) is built, the target is informed that it has a new follower.
2.  When the link to the **target** (`Unit`) is destroyed, the target is informed that the follower is gone.
3.  When the link to the **source** (`TargetedMovementGeneratorBase`) is destroyed, the movement generator is instructed to stop the following behavior.

This class does not store data or perform calculations; it solely delegates lifecycle events to the connected `Unit` and `TargetedMovementGeneratorBase` objects.

## Member-by-Member Behavior

The unit consists of three virtual methods that override the base `Reference` class hooks. These methods are triggered automatically by the `Reference` infrastructure when the pointers held by the reference change.

### Lifecycle Link Management

*   **`targetObjectBuildLink`**: Called when the `FollowerReference` successfully acquires a pointer to a target `Unit`. It immediately calls `Unit.Main/AddFollower` on that target, passing `this` (the reference object). This registers the follower relationship on the target side.
*   **`targetObjectDestroyLink`**: Called when the `FollowerReference` loses its pointer to the target `Unit` (e.g., the unit dies or despawns). It calls `Unit.Main/RemoveFollower` on the target, passing `this`. This cleans up the follower registration on the target side.
*   **`sourceObjectDestroyLink`**: Called when the `FollowerReference` loses its pointer to the source `TargetedMovementGeneratorBase`. It calls `TargetedMovementGeneratorBase/stopFollowing` on the source. This ensures that if the movement generator itself is removed or invalidated, the following behavior is explicitly halted.

## Cross-Unit Boundaries

`FollowerReference` acts as a bridge between the `Unit` subsystem and the Movement Generator subsystem.

*   **Calls into `Unit.Main`**:
    *   `targetObjectBuildLink` calls `Unit.Main/AddFollower`.
    *   `targetObjectDestroyLink` calls `Unit.Main/RemoveFollower`.
    *   *Direction*: Outbound from `FollowerReference` to `Unit.Main`.
    *   *Why*: To keep the `Unit`'s internal list of followers synchronized with the existence of the `FollowerReference` link. The `Unit` needs to know who is following it for purposes such as pathfinding avoidance, aggro checks, or visual effects.

*   **Calls into `TargetedMovementGeneratorBase`**:
    *   `sourceObjectDestroyLink` calls `TargetedMovementGeneratorBase/stopFollowing`.
    *   *Direction*: Outbound from `FollowerReference` to `TargetedMovementGeneratorBase`.
    *   *Why*: To ensure the movement generator stops attempting to follow if the reference holding it becomes invalid. This prevents dangling pointer access or continued movement logic after the generator's context is lost.

*   **Called By**:
    *   The MAP indicates no external units explicitly call these methods. They are invoked internally by the `Reference` base class mechanism during pointer assignment or reset operations.

## Data Model

This unit interacts exclusively with in-memory C++ objects (`Unit` and `TargetedMovementGeneratorBase`). It does not query or modify any database tables.

## Notable Implementation Details

*   **Inheritance Context**: `FollowerReference` inherits from `Reference<Unit, TargetedMovementGeneratorBase>`. In the MaNGOS/VaMangos architecture, `Reference<Target, Source>` typically implies that the `Reference` object holds a pointer to a `Target` and is owned by or associated with a `Source`. The naming convention `targetObject...` and `sourceObject...` aligns with this template structure.
*   **Passing `this`**: Both `AddFollower` and `RemoveFollower` receive `this` (the `FollowerReference` instance). This suggests that `Unit.Main` stores a collection of `FollowerReference` pointers (or similar handles) rather than just raw `Unit` pointers, allowing the `Unit` to manage the specific links.
*   **Asymmetry in Destruction**: Note that `targetObjectDestroyLink` notifies the target, while `sourceObjectDestroyLink` commands the source to stop. There is no `sourceObjectBuildLink` override in this unit. This implies that the initialization of the following behavior is likely handled elsewhere (possibly in the `TargetedMovementGeneratorBase` constructor or a separate setup method), whereas the teardown is strictly managed by this reference's destruction hooks.
*   **No Error Handling**: The methods assume the pointers returned by `getTarget()` and `getSource()` are valid at the time of invocation, as guaranteed by the `Reference` base class contract. No null checks are performed.

## Member Reference

**targetObjectBuildLink**
Overrides the base class hook to notify the target `Unit` that a follower link has been established. Calls `Unit.Main/AddFollower` with `this`.

**targetObjectDestroyLink**
Overrides the base class hook to notify the target `Unit` that the follower link has been severed. Calls `Unit.Main/RemoveFollower` with `this`.

**sourceObjectDestroyLink**
Overrides the base class hook to handle the destruction of the link to the source movement generator. Calls `TargetedMovementGeneratorBase/stopFollowing` to halt the following behavior.

---

<!-- machine-true, projected from graph.json -->

## Map — FollowerReference

*Source:* FollowerReference.cpp, FollowerReference.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| targetObjectBuildLink | method | Unit.Main/AddFollower | — | — |
| targetObjectDestroyLink | method | Unit.Main/RemoveFollower | — | — |
| sourceObjectDestroyLink | method | TargetedMovementGeneratorBase/stopFollowing | — | — |
