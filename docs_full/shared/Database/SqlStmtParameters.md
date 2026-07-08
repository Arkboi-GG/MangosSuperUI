# SqlStmtParameters

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# SqlStmtParameters

**SqlStmtParameters** is a lightweight container class responsible for holding the typed input arguments for a prepared SQL statement within the `wowvmangos` database abstraction layer. It acts as the bridge between high-level C++ data types and the low-level MySQL binding interface.

Its primary responsibility is to aggregate a sequence of `SqlStmtFieldData` objects—each representing a single parameter value (integer, float, string, etc.)—into a contiguous `std::vector`. This collection is then consumed by the database driver to bind values to placeholders (`?`) in a prepared statement before execution. The class supports reuse patterns common in high-throughput server loops, allowing parameters to be cleared and re-bound without reallocating memory.

It does not perform any SQL parsing, validation, or execution itself. It strictly manages the lifecycle and storage of parameter data until it is handed off to `DatabaseMysql` or `SqlPreparedStatement` implementations.

## Member-by-Member Behavior

The members of `SqlStmtParameters` are focused on managing the internal `ParameterContainer` (a `std::vector<SqlStmtFieldData>`).

### Construction and Destruction
*   **`~SqlStmtParameters`**: The destructor is empty. Since `SqlStmtFieldData` contains only standard types (`std::string`, primitive integers/floats, and a union), no custom cleanup is required. The vector’s automatic destruction handles memory release.

### Parameter Management
*   **`boundParams`**: Returns the current number of parameters stored in the container. This is used by callers to verify that the correct number of arguments have been supplied before attempting to bind or execute. It is called by `DatabaseMysql::bind`, `SqlPreparedStatement::bind`, `SqlPreparedStatement::DirectExecute`, and `SqlPreparedStatement::Execute#2` to validate argument counts against the expected statement signature.
*   **`addParam`**: Appends a `SqlStmtFieldData` object to the internal vector. This is the core mechanism for building up the argument list. It is typically invoked indirectly through the `SqlStatement::arg` template helper, which constructs a `SqlStmtFieldData` from a raw C++ value and passes it here.
*   **`params`**: Provides const access to the underlying `ParameterContainer`. This allows the database binding layer (specifically `DatabaseMysql::bind` and `SqlPreparedStatement::bind`) to iterate over the stored parameters and apply them to the MySQL C API or plain SQL string formatting.

### State Manipulation
*   **`swap`**: Declared but not defined in this unit. It swaps the internal parameter containers with another `SqlStmtParameters` instance. This enables efficient transfer of ownership or state exchange without copying data, useful for optimizing hot paths where parameter lists are reused or exchanged between statement instances.
*   **`operator=`**: Declared as private and undefined. This explicitly disables copy assignment, enforcing that `SqlStmtParameters` objects are either moved (via `swap`) or constructed fresh. This prevents accidental shallow copies of the internal vector, which could lead to double-free errors or inconsistent state if the original object is modified after a copy.

## Cross-Unit Boundaries

`SqlStmtParameters` sits at the center of the prepared statement workflow, interacting primarily with the statement wrapper and the database driver.

### Called By: `DatabaseMysql`
*   **`DatabaseMysql::bind`**: The MySQL-specific implementation of the abstract `SqlPreparedStatement::bind` method calls `SqlStmtParameters::boundParams` to check the count and `SqlStmtParameters::params` to retrieve the vector of `SqlStmtFieldData`. It then iterates through this vector, converting each `SqlStmtFieldData` into the appropriate `MYSQL_BIND` structure for the MySQL C API. This is the critical handoff point where C++ types become native MySQL binary/text blobs.

### Called By: `SqlPreparedStatement`
*   **`SqlPreparedStatement::bind`**: The abstract base class defines the contract. Concrete implementations (like `SqlPlainPreparedStatement` or `DatabaseMysql`'s internal prepared statement handler) receive a `SqlStmtParameters` const reference. They use `boundParams` to ensure the caller provided enough arguments and `params` to access the data.
*   **`SqlPreparedStatement::DirectExecute`** and **`SqlPreparedStatement::Execute#2`**: These execution methods call `boundParams` to validate that the parameter count matches the statement's expected argument count before proceeding. If the counts mismatch, execution is aborted to prevent SQL errors or security issues (e.g., missing bounds checks).

### Called By: `SqlStatement` (Implicitly via `arg`)
*   Although not listed in the "Called by" column of the map for `addParam`, the `SqlStatement` class (defined in the same header) uses `SqlStmtParameters::addParam` internally. When a user calls `SqlStatement::PExecute` or `SqlStatement::addUInt32`, these methods invoke the private `SqlStatement::arg` template, which creates a `SqlStmtFieldData` and pushes it into the `SqlStmtParameters` instance owned by the `SqlStatement`. Thus, `SqlStmtParameters` is the sink for all user-provided query arguments.

## Data Model

This unit does not interact directly with database tables. It operates entirely in memory, holding transient data for a single query execution cycle. No SQL queries are issued by `SqlStmtParameters`, and no table schemas are referenced.

## Notable Implementation Details

### Type Safety via `SqlStmtFieldData`
The class relies heavily on `SqlStmtFieldData` (also defined in `SqlPreparedStatement.h`) to handle type erasure. `SqlStmtFieldData` uses a union (`SqlStmtField`) for numeric types and a separate `std::string` for text. This design avoids the overhead of `std::variant` or `boost::any` while maintaining type safety through the `SqlStmtFieldType` enum. `SqlStmtParameters` stores these opaque blobs, delegating type interpretation to the binder.

### Memory Reuse Strategy
The `reset` method (declared in the class but not in the MAP for this specific partial, though `swap` is) suggests a design intent for object pooling. In high-frequency game server loops, allocating and deallocating vectors for every database query is expensive. By providing `swap` and likely a `reset` mechanism (to clear the vector while retaining capacity), `SqlStmtParameters` minimizes heap fragmentation. The `operator=` being disabled reinforces this: users are expected to reuse instances via `swap` or `reset`, not copy them.

### Const-Correctness and Access Control
*   `params()` returns a `const` reference, preventing external modification of the parameter list once built. This ensures that the data bound to the statement remains stable during the binding process.
*   `boundParams()` returns `int`, matching the return type of `std::vector::size()` cast to int. This is consistent with the rest of the codebase’s use of `int` for counts, though `size_t` would be more idiomatic for modern C++. The cast is safe because SQL statements rarely exceed 2 billion parameters.

### No Null-Termination Handling for Strings
When `SqlStmtFieldData` stores a string, it uses `std::string`. The `buff()` method in `SqlStmtFieldData` returns `m_szStringData.c_str()` for strings. This ensures null-termination is handled correctly for C-API functions expecting `char*`. `SqlStmtParameters` itself does not manage this detail; it simply holds the `SqlStmtFieldData` objects. However, it is crucial that the binder (`DatabaseMysql`) respects the `FIELD_STRING` type and uses the correct length (from `size()`) rather than assuming null-termination for binary data, although `std::string` is always null-terminated.

## Member Reference

**~SqlStmtParameters**
Destructor. Does nothing explicitly; relies on automatic cleanup of the `std::vector` member.

**boundParams**
Returns the number of parameters currently stored in the internal vector. Used by `DatabaseMysql::bind`, `SqlPreparedStatement::bind`, `SqlPreparedStatement::DirectExecute`, and `SqlPreparedStatement::Execute#2` to validate argument counts.

**addParam**
Appends a `SqlStmtFieldData` object to the internal parameter vector. This is the primary way parameters are added to the statement, typically called by `SqlStatement::arg`.

**swap**
Declared method to swap the internal parameter container with another `SqlStmtParameters` instance. Enables efficient state exchange without copying.

**params**
Returns a const reference to the internal `ParameterContainer` (`std::vector<SqlStmtFieldData>`). Accessed by `DatabaseMysql::bind` and `SqlPreparedStatement::bind` to retrieve the actual data for binding.

**operator=**
Declared as private and undefined. Disables copy assignment to prevent accidental shallow copies and enforce move/swap semantics for performance and safety.

---

<!-- machine-true, projected from graph.json -->

## Map — SqlStmtParameters

*Source:* SqlPreparedStatement.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ~SqlStmtParameters | dtor | — | — | — |
| boundParams | method | — | DatabaseMysql/bind, SqlPreparedStatement/bind, SqlPreparedStatement/DirectExecute, SqlPreparedStatement/Execute#2 | — |
| addParam | method | — | — | — |
| swap | decl | — | — | — |
| params | method | — | DatabaseMysql/bind, SqlPreparedStatement/bind | — |
| operator= | decl | — | — | — |
