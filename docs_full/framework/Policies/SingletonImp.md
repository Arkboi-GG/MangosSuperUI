# SingletonImp

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# SingletonImp

`SingletonImp.h` provides the template method implementations and instantiation macros for the `MaNGOS::Singleton` class, which is declared in `Singleton.h`. This unit implements the core lifecycle management for singleton objects within the MaNGOS framework, handling thread-safe lazy initialization, destruction scheduling, and static storage allocation. It does not contain any database interactions or cross-unit calls beyond its own template dependencies (`ThreadingModel`, `CreatePolicy`, `LifeTimePolicy`).

## Purpose & Responsibilities

The primary responsibility of `SingletonImp.h` is to define **how** a singleton instance is created, accessed, and destroyed, while `Singleton.h` defines **what** the singleton interface looks like. Specifically, this unit handles:

1.  **Lazy Initialization with Thread Safety:** The `Instance()` method implements a double-checked locking pattern to ensure that the singleton object is created only once, even in a multi-threaded environment, minimizing lock contention after the first creation.
2.  **Lifecycle Management:** It coordinates with policy classes (`CreatePolicy`, `LifeTimePolicy`) to allocate memory, schedule destruction, and handle re-initialization if a singleton is accessed after being destroyed.
3.  **Static Storage Allocation:** The `INSTANTIATE_SINGLETON_*` macros generate the necessary static member variables (`si_instance`, `si_destroyed`) and explicit template instantiations required for the singleton pattern to work correctly in C++.

## Member-by-Member Behavior

### `Instance`

This is the primary access point for obtaining a reference to the singleton object of type `T`.

*   **Lazy Creation:** It checks if `si_instance` is null. If it is, it proceeds to create the instance.
*   **Double-Checked Locking:** To avoid unnecessary locking overhead after the singleton is already created, it performs a second check for `si_instance` inside a critical section guarded by `Guard()` (provided by the `ThreadingModel` policy).
*   **Re-initialization Logic:** If `si_destroyed` is true (indicating the singleton was previously destroyed but is now being requested again), it resets `si_destroyed` to false, notifies the `LifeTimePolicy` via `OnDeadReference()`, and then creates a new instance.
*   **Creation & Scheduling:** It calls `CreatePolicy::Create()` to allocate and construct the object. Immediately after creation, it schedules the `DestroySingleton` method to be called later via `LifeTimePolicy::ScheduleCall(&DestroySingleton)`. This ensures that the singleton's cleanup is managed by the framework's lifetime policy (e.g., at server shutdown).
*   **Return Value:** Returns a reference to the `si_instance` pointer.

### `DestroySingleton`

This method is responsible for cleaning up the singleton instance. It is typically called automatically by the `LifeTimePolicy` at the end of the application's lifecycle or when explicitly triggered.

*   **Destruction:** It calls `CreatePolicy::Destroy(si_instance)` to properly deallocate and destruct the object. The specific destruction mechanism depends on the `CreatePolicy` (e.g., `delete` for heap-allocated objects).
*   **State Reset:** It sets `si_instance` to `nullptr` and marks `si_destroyed` as `true`. This allows the singleton to be lazily recreated if `Instance()` is called again in the future.

## Cross-Unit Boundaries

This unit is a self-contained implementation detail for the `MaNGOS::Singleton` template class. It does not call into other named units in the map, nor is it called by other named units in the map. Its interactions are strictly with the policy classes passed as template arguments:

*   **`ThreadingModel`:** Used via `Guard()` in `Instance()` to provide thread synchronization.
*   **`CreatePolicy`:** Used via `Create()` in `Instance()` and `Destroy()` in `DestroySingleton()` to manage object allocation and deallocation.
*   **`LifeTimePolicy`:** Used via `ScheduleCall()` in `Instance()` to register the destructor callback, and `OnDeadReference()` in `Instance()` to handle re-initialization events.

These policies are defined elsewhere (likely in `Singleton.h` or related headers) and are injected at compile time via template parameters.

## Data Model

This unit does not interact with any database tables. It operates entirely in memory, managing the lifecycle of C++ objects.

## Notable Implementation Details

*   **Double-Checked Locking Pattern:** The `Instance()` method uses a classic double-checked locking pattern. Note that in modern C++, this pattern requires careful use of memory barriers or `std::atomic` to be strictly correct. The correctness here depends on the implementation of `Guard()` in the `ThreadingModel` policy and the compiler's memory model guarantees.
*   **Re-initialization Support:** Unlike many singleton implementations that prevent re-creation after destruction, this design explicitly supports re-initialization. If `Instance()` is called after `DestroySingleton()` has run, it will create a new instance. This is facilitated by the `si_destroyed` flag and the `OnDeadReference()` callback.
*   **Macro-Based Instantiation:** The `INSTANTIATE_SINGLETON_*` macros are crucial for using this singleton template. They perform three tasks:
    1.  Define the static member variables `si_instance` and `si_destroyed` with default values (`0` and `false`).
    2.  Explicitly instantiate the template class for the given types.
    3.  Provide convenience macros (`INSTANTIABLE_SINGLETON_1` through `_4`) that allow users to specify varying levels of customization (threading model, creation policy, lifetime policy) or use defaults.
*   **No `using namespace` in Header:** The comment at the top explicitly avoids `using namespace` directives in the header file to prevent namespace pollution in including files. All names are fully qualified (e.g., `MaNGOS::Singleton`).

## Member Reference

**Instance**: Implements lazy initialization with double-checked locking. Checks `si_instance`, acquires a guard if null, checks again, handles re-initialization if `si_destroyed` is true, creates the instance via `CreatePolicy::Create()`, schedules destruction via `LifeTimePolicy::ScheduleCall()`, and returns a reference to the instance.

**DestroySingleton**: Destroys the singleton instance by calling `CreatePolicy::Destroy(si_instance)`, sets `si_instance` to `nullptr`, and sets `si_destroyed` to `true`.

---

<!-- machine-true, projected from graph.json -->

## Map — SingletonImp

*Source:* SingletonImp.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Instance | function | — | — | — |
| DestroySingleton | function | — | — | — |
