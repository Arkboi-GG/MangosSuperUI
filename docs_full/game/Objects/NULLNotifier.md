# NULLNotifier

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# NULLNotifier

**NULLNotifier** is a minimal utility class defined in `Object.h` within the wowvmangos codebase. It serves as a "null object" or "no-op" visitor implementation for the grid-based spatial partitioning system. Its sole purpose is to provide a valid callable interface that performs no action when iterating over game objects in a grid cell or camera map. This allows algorithms that require a visitor pattern to proceed safely even when no side effects (such as updates, deletions, or notifications) are desired for the visited objects.

The class contains two overloaded methods named **Visit**. Both are empty functions that take no action. One accepts a reference to a `GridRefManager<T>` (a template class managing references to objects within a spatial grid cell), and the other accepts a `CameraMapType&` (a map tracking objects relevant to camera rendering or visibility). By providing these empty implementations, `NULLNotifier` satisfies the type requirements of visitor-based iteration loops without executing any logic, effectively acting as a placeholder to suppress operations.

There are no database tables associated with this unit. It does not read from or write to any SQL schema.

## Member Reference

**Visit**
This member exists as two overloaded methods in the `NULLNotifier` class. The first overload is a template method `template<class T> void Visit(GridRefManager<T>& m)` that accepts a reference to a grid reference manager. The second overload is `void Visit(CameraMapType&)` that accepts a camera map reference. Both implementations contain empty bodies (`{}`), meaning they perform no operations, do not access the passed arguments, and return immediately. They are designed to be passed as visitor callbacks to iteration routines that traverse spatial grids or camera maps, allowing the caller to skip processing for those specific collections while maintaining a uniform interface.

---

<!-- machine-true, projected from graph.json -->

## Map — NULLNotifier

*Source:* Object.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Visit | method | — | — | — |
