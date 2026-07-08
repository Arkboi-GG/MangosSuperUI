# Lock

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Lock

## Purpose & Responsibilities

The `Lock` unit comprises two distinct, unrelated classes named `Lock` that serve as RAII (Resource Acquisition Is Initialization) guards for database and threading synchronization within the MaNGOS server architecture. These classes abstract the complexity of mutex management, ensuring that locks are acquired upon construction and released upon destruction, thereby preventing deadlocks and race conditions in concurrent database access scenarios.

1.  **`MaNGOS::SingleThreaded<T>::Lock`**: A no-op placeholder class used in single-threaded builds or contexts. It provides a compatible interface for code that expects a lock object but performs no actual synchronization.
2.  **`MaNGOS::ClassLevelLockable<T, MUTEX>::Lock`**: A static-level lock guard used for class-wide synchronization. It acquires a static mutex associated with the template parameter `T`, protecting shared resources across all instances of that type.
3.  **`SqlConnection::Lock`**: A recursive mutex guard specific to database connections. It ensures exclusive access to a `SqlConnection` instance during query execution, statement preparation, and connection maintenance operations.

These classes are integral to the `Database` subsystem, allowing safe concurrent access to MySQL/MariaDB connections from multiple game server threads.

## Member-by-Member Behavior

### `MaNGOS::SingleThreaded<T>::Lock`

This nested class is defined within `MaNGOS::SingleThreaded<T>` in `ThreadingModel.h`. It is designed for environments where threading is disabled or unnecessary.

*   **Constructors (`Lock#2`, `Lock#3`, `Lock#4`, `Lock#5`)**:
    *   The default constructor `Lock()` initializes an empty object.
    *   The constructor `Lock(const T&)` accepts a reference to the host object but ignores it.
    *   The constructor `Lock(const SingleThreaded<T>&)` accepts a reference to the parent wrapper but ignores it.
    *   These constructors exist solely to satisfy the API contract expected by code written for multi-threaded environments, allowing the same code paths to compile and run without modification in single-threaded modes. No locking occurs.

### `MaNGOS::ClassLevelLockable<T, MUTEX>::Lock`

This nested class is defined within `MaNGOS::ClassLevelLockable<T, MUTEX>` in `ThreadingModel.h`. It provides static-level locking for a given type `T`.

*   **Constructors (`Lock#2`, `Lock#3`, `Lock#4`, `Lock#5`)**:
    *   `Lock(const T& /*host*/)`: Accepts a reference to an instance of type `T` but ignores it. The lock is static, so the specific instance is irrelevant.
    *   `Lock(const ClassLevelLockable<T, MUTEX> &)`: Accepts a reference to the parent wrapper but ignores it.
    *   `Lock()`: Default constructor.
    *   Upon construction, the private member `m_lock` (a `std::unique_lock<MUTEX>`) is initialized with `si_mtx`, the static mutex shared by all instances of `ClassLevelLockable<T, MUTEX>`. This ensures that only one thread can hold the lock for type `T` at any time.

### `SqlConnection::Lock`

This nested class is defined within `SqlConnection` in `Database.h`. It is the primary mechanism for synchronizing access to individual database connections.

*   **Constructor (`Lock#5`)**:
    *   `Lock(SqlConnection * conn)`: Takes a pointer to a `SqlConnection` instance.
    *   It stores the connection pointer in `m_pConn`.
    *   It initializes `m_lock` as a `std::unique_lock<std::recursive_mutex>` bound to `m_pConn->m_mutex`. This acquires the recursive mutex associated with the specific database connection, blocking other threads attempting to access the same connection until this lock is released.
    *   This constructor is called by various `Database` methods (`DirectExecuteStmt`, `FreePreparedStatements`, `Ping`, etc.) to ensure thread-safe interaction with the underlying MySQL connection handle.

*   **`operator->`**:
    *   Returns the stored `SqlConnection*` pointer.
    *   This allows the lock object to be used transparently in place of the raw connection pointer, enabling syntax like `guard->Query(sql)` instead of `conn->Query(sql)`.
    *   Called by `Database::DirectExecuteStmt` and `Database::Ping` to execute operations on the locked connection.

## Cross-Unit Boundaries

### `SqlConnection::Lock`

*   **Called By**:
    *   `Database::DirectExecuteStmt`: Acquires the lock to safely execute a prepared statement on the async connection.
    *   `Database::FreePreparedStatements`: Acquires the lock to clean up prepared statement resources.
    *   `Database::Ping`: Acquires the lock to send a ping command to the database server, verifying connection health.
    *   `SqlOperations::Execute` (variants): Various execution paths acquire the lock to ensure that SQL commands are sent to the database atomically with respect to other operations on the same connection.
*   **Collaboration**: The `Lock` object acts as a gatekeeper. The calling unit passes a `SqlConnection*` to the `Lock` constructor. The `Lock` then manages the lifetime of the mutex acquisition. The caller uses `operator->` to access the connection methods. This pattern ensures that no two threads can simultaneously issue queries or modify state on the same `SqlConnection` instance, which is critical because MySQL C API connections are not thread-safe.

### `MaNGOS::ClassLevelLockable<T, MUTEX>::Lock`

*   **Called By**: None listed in the MAP. However, it is intended to be used by code that needs to synchronize access to static members or global state associated with type `T`.
*   **Collaboration**: The lock accesses the static member `si_mtx` of the enclosing `ClassLevelLockable` class. This creates a dependency between the `Lock` instance and the static storage duration of the mutex.

### `MaNGOS::SingleThreaded<T>::Lock`

*   **Called By**: None listed in the MAP. It is a passive component used for API compatibility.

## Data Model

This unit does not interact directly with database tables. It operates on the connection layer, managing mutexes and pointers to `SqlConnection` objects. The SQL queries executed through these locks may touch various tables, but the `Lock` classes themselves are agnostic to the schema.

## Notable Implementation Details

1.  **Recursive Mutex in `SqlConnection::Lock`**:
    *   The `SqlConnection` class uses `std::recursive_mutex` for `m_mutex`. This allows a thread to acquire the lock multiple times without deadlocking itself. This is necessary because some database operations might internally call other methods that also require the lock. For example, `ExecuteStmt` might call internal helpers that also need to access the connection state.

2.  **RAII Pattern**:
    *   All `Lock` classes rely on the destructor to release the lock. Since `std::unique_lock` is used, the unlock happens automatically when the `Lock` object goes out of scope, even if an exception is thrown. This is a robust way to manage resources in C++.

3.  **Static Mutex in `ClassLevelLockable`**:
    *   The mutex `si_mtx` is declared as `static` within the template class. This means there is one mutex per unique instantiation of `ClassLevelLockable<T, MUTEX>`. Care must be taken to ensure that the static initialization order fiasco does not cause issues, although the use of `std::mutex` generally mitigates this compared to older C-style globals.

4.  **No-Op Locks for Single-Threaded Mode**:
    *   The `SingleThreaded<T>::Lock` class is completely empty in terms of synchronization logic. This is a performance optimization for single-threaded builds, avoiding the overhead of mutex operations entirely.

5.  **Pointer Storage in `SqlConnection::Lock`**:
    *   The `SqlConnection::Lock` stores a raw pointer `m_pConn`. It assumes that the `SqlConnection` object remains valid for the lifetime of the `Lock`. If the `SqlConnection` is deleted while a `Lock` exists, undefined behavior will occur. This is a common risk with RAII wrappers around raw pointers and requires careful ownership management by the caller.

6.  **Template Instantiation Macro**:
    *   The macro `INSTANTIATE_CLASS_MUTEX` is provided to explicitly instantiate `ClassLevelLockable` for specific types and mutex types. This is necessary because templates are not instantiated unless used, and explicit instantiation can help control binary size and linkage.

## Member Reference

**Lock#6** (decl): Declaration of `MaNGOS::SingleThreaded<T>::Lock` in `ThreadingModel.h`.

**Lock#2** (ctor): Constructor `MaNGOS::SingleThreaded<T>::Lock()` default constructor. Also refers to `MaNGOS::ClassLevelLockable<T, MUTEX>::Lock()` default constructor.

**Lock** (ctor): Generic reference to `SqlConnection::Lock(SqlConnection*)` constructor in `Database.h`.

**Lock#5** (ctor): Constructor `SqlConnection::Lock(SqlConnection * conn)` in `Database.h`. Acquires the recursive mutex for the given connection.

**operator->** (method): `SqlConnection::Lock::operator->()` in `Database.h`. Returns the underlying `SqlConnection*` pointer.

**Lock#4** (ctor): Constructor `MaNGOS::SingleThreaded<T>::Lock(const SingleThreaded<T>&)` in `ThreadingModel.h`. No-op.

**Lock#3** (ctor): Constructor `MaNGOS::SingleThreaded<T>::Lock(const T&)` in `ThreadingModel.h`. No-op.

**Lock#7** (decl): Declaration of `MaNGOS::ClassLevelLockable<T, MUTEX>::Lock` in `ThreadingModel.h`.

---

<!-- machine-true, projected from graph.json -->

## Map — Lock

*Source:* ThreadingModel.h, Database.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Lock#6 | decl | — | — | — |
| Lock#2 | ctor | — | — | — |
| Lock | ctor | — | — | — |
| Lock#5 | ctor | — | Database/DirectExecuteStmt, Database/FreePreparedStatements, Database/Ping, SqlOperations/Execute, SqlOperations/Execute#2, SqlOperations/Execute#3, SqlOperations/Execute#5, SqlOperations/Execute#6 | — |
| operator-> | method | — | Database/DirectExecuteStmt, Database/Ping | — |
| Lock#4 | ctor | — | — | — |
| Lock#3 | ctor | — | — | — |
| Lock#7 | decl | — | — | — |
