<!-- provenance: verbose -->
# ThreadingModel

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ThreadingModel

## Purpose & Responsibilities

`ThreadingModel.h` defines lightweight synchronization primitives and abstract lock interfaces for the MaNGOS server engine. It provides three mechanisms for managing concurrent access:

1.  **`GeneralLock<MUTEX>`**: A RAII wrapper that acquires a `std::unique_lock` on a provided mutex instance upon construction. Used for fine-grained locking where the mutex is a member of the protected object.
2.  **`ClassLevelLockable<T, MUTEX>`**: A mixin providing a static mutex shared by all instances of type `T`. Its nested `Lock` class acquires this static mutex, enabling coarse-grained locking across all instances of a class.
3.  **`SingleThreaded<T>`**: An empty lock interface for contexts where threading is disabled, allowing compilation against a unified locking API with zero synchronization overhead.

## Member-by-Member Behavior

### GeneralLock<MUTEX>

A template class wrapping a mutex instance for automatic locking/unlocking.

*   **`GeneralLock<MUTEX>` (Constructor)**: Takes a reference to a mutex `m`, stores it in `i_mutex`, and initializes `m_lock` (`std::unique_lock<MUTEX>`), which acquires the mutex.
*   **`~GeneralLock<MUTEX>` (Destructor)**: Explicitly defined but empty. Unlocking occurs automatically when `m_lock` is destroyed.
*   **`GeneralLock<MUTEX>#2` (Copy Constructor)**: Deleted (`= delete`) to prevent double-unlocking.
*   **`operator=` (Assignment Operator)**: Deleted (`= delete`) to prevent double-unlocking.

### ClassLevelLockable<T, MUTEX>

A mixin providing a static mutex for class `T`.

*   **`ClassLevelLockable<T, MUTEX>` (Constructor)**: Default constructor; performs no initialization as the static mutex `si_mtx` is initialized at startup.
*   **Nested `Lock` Class**:
    *   **`Lock(const T& host)`**, **`Lock(const ClassLevelLockable<T, MUTEX>&)`**, **`Lock()`**: Constructors that acquire the static mutex `si_mtx` via `m_lock`. The host arguments are ignored.
    *   **`m_lock`**: A `std::unique_lock<MUTEX>` holding the lock on `si_mtx`.
*   **`si_mtx`**: Static member variable of type `MUTEX`, shared across all instances. Defined outside the class template.

### SingleThreaded<T>

Provides a dummy lock interface for single-threaded contexts.

*   **Nested `Lock` Class**:
    *   **`Lock()`**, **`Lock(const T&)`**, **`Lock(const SingleThreaded<T>&)`**: Constructors that perform no operations.

## Cross-Unit Boundaries

These classes are foundational utilities. They do not call out to other units. They are called by various data structures and managers throughout the MaNGOS codebase that require thread safety. `GeneralLock` is used for per-instance locking, `ClassLevelLockable` for global class-level locking, and `SingleThreaded` for non-threaded builds or single-threaded objects.

## Data Model

This unit does not interact with any database tables. It is purely a C++ concurrency utility.

## Notable Implementation Details

1.  **Static Mutex Initialization**: `si_mtx` in `ClassLevelLockable` is defined outside the class template to ensure each specialization has its own unique static mutex.
2.  **RAII Pattern**: Both `GeneralLock` and `ClassLevelLockable::Lock` use `std::unique_lock` to guarantee unlocking even if exceptions occur.
3.  **Deleted Copy/Move Operations**: `GeneralLock` deletes copy and assignment operators to prevent undefined behavior from double-unlocking.
4.  **Empty Locks for Single-Threaded Mode**: `SingleThreaded<T>::Lock` is empty, allowing uniform locking interfaces with zero runtime overhead in single-threaded mode.
5.  **Friend Declaration**: `ClassLevelLockable` declares `friend class Lock;` to allow the nested `Lock` class to access the private static member `si_mtx`.

## Member Reference

*   **GeneralLock<MUTEX>**: Constructs a `GeneralLock` by acquiring the provided mutex `m` via a `std::unique_lock`. Stores the mutex reference in `i_mutex`.
*   **~GeneralLock<MUTEX>**: Destructor for `GeneralLock`. Empty body; unlocking is handled by the destruction of the `std::unique_lock` member `m_lock`.
*   **GeneralLock<MUTEX>#2**: Deleted copy constructor for `GeneralLock`. Prevents copying of lock objects to avoid double-unlocking.
*   **operator=**: Deleted assignment operator for `GeneralLock`. Prevents assignment of lock objects to avoid double-unlocking.
*   **ClassLevelLockable<T, MUTEX>**: Default constructor for `ClassLevelLockable`. Performs no initialization as the static mutex `si_mtx` is already initialized.

---

<!-- machine-true, projected from graph.json -->

## Map — ThreadingModel

*Source:* ThreadingModel.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GeneralLock<MUTEX> | ctor | — | — | — |
| ~GeneralLock<MUTEX> | dtor | — | — | — |
| GeneralLock<MUTEX>#2 | decl | — | — | — |
| operator= | decl | — | — | — |
| ClassLevelLockable<T, MUTEX> | ctor | — | — | — |
