# Singleton

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Singleton Template Class

## Purpose & Responsibilities

The `Singleton` template class in `Singleton.h` implements the Singleton design pattern for the MaNGOS server framework. It ensures that exactly one instance of a specified type `T` exists, providing global access via the static `Instance()` method (declared in the header, defined elsewhere).

The class is configurable via three template parameters:
1.  **`ThreadingModel`**: Controls synchronization during access (default: `MaNGOS::SingleThreaded<T>`).
2.  **`CreatePolicy`**: Controls allocation and construction (default: `MaNGOS::OperatorNew<T>`).
3.  **`LifeTimePolicy`**: Controls destruction and cleanup (default: `MaNGOS::ObjectLifeTime<T>`).

It explicitly prohibits copying and assignment to enforce uniqueness.

## Member-by-Member Behavior

### Construction and Access Control

*   **`Singleton()` (Protected Constructor)**: Prevents direct external instantiation. It performs no initialization; actual object creation is delegated to the `CreatePolicy` within the `Instance()` method.
*   **`Singleton(const Singleton&)` (Private Copy Constructor)**: Declared but undefined to prohibit copying the wrapper.
*   **`operator=` (Private Assignment Operator)**: Declared but undefined to prohibit assignment between wrappers.

### Internal State

*   **`si_instance`**: Static pointer to the single instance of `T`.
*   **`si_destroyed`**: Static boolean flag tracking whether the instance has been destroyed, preventing use-after-free during shutdown.
*   **`DestroySingleton()`**: Private static helper that cleans up the instance using `LifeTimePolicy` and sets `si_destroyed` to true.

## Cross-Unit Boundaries

*   **Dependencies**: Relies on `CreationPolicy.h`, `ThreadingModel.h`, and `ObjectLifeTime.h` for policy injection.
*   **Usage**: Used by major subsystems (e.g., `World`, `MapManager`) to provide global access points. No specific cross-unit calls are listed in the MAP for this unit's members.

## Data Model

This unit does not interact with any database tables.

## Notable Implementation Details

1.  **Policy-Based Design**: Thread safety, creation, and lifetime are decoupled into separate policies, allowing customization without modifying the core `Singleton` logic.
2.  **No Hijacking Prevention**: The comment notes that while accidental copying is prevented, intentional bypassing (e.g., direct `new` on `T`) is not stopped.
3.  **Lazy Initialization**: The instance is created on first call to `Instance()`, mitigating static initialization order issues.

## Member Reference

**Singleton<T, ThreadingModel, CreatePolicy, LifeTimePolicy>**
The protected default constructor. It initializes the singleton wrapper but relies on the `CreatePolicy` to construct the actual instance of `T`. It is protected to prevent direct external instantiation.

**Singleton<T, ThreadingModel, CreatePolicy, LifeTimePolicy>#2**
The private copy constructor. It is declared but not defined to prohibit copying of the singleton wrapper, ensuring that only one instance of the wrapper exists.

**operator=**
The private copy assignment operator. It is declared but not defined to prohibit assignment of one singleton wrapper to another, enforcing the single-instance constraint.

---

<!-- machine-true, projected from graph.json -->

## Map — Singleton

*Source:* Singleton.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Singleton<T, ThreadingModel, CreatePolicy, LifeTimePolicy> | ctor | — | — | — |
| Singleton<T, ThreadingModel, CreatePolicy, LifeTimePolicy>#2 | decl | — | — | — |
| operator= | decl | — | — | — |
