<!-- provenance: verbose -->
# MySQLConnection

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# MySQLConnection

## Purpose & Responsibilities

`MySQLConnection` is the concrete implementation of the `SqlConnection` abstract base class for MySQL databases within the WoWVMaNGOS server. It encapsulates the low-level interaction with the MySQL C API (`libmysqlclient`), providing a thread-safe, transaction-aware interface for executing SQL queries, managing prepared statements, and handling connection lifecycle events.

Its primary responsibilities are:
1.  **Connection Management:** Establishing, maintaining, and closing connections to the MySQL server. It handles reconnection logic and error recovery specific to MySQL.
2.  **Query Execution:** Executing raw SQL strings via `Query()` (for SELECTs returning results) and `Execute()` (for DML/DDL commands). It translates MySQL result sets into the engine's internal `QueryResult` objects.
3.  **Transaction Control:** Providing methods to begin, commit, and rollback database transactions.
4.  **Prepared Statement Support:** Creating and managing `MySqlPreparedStatement` instances, which handle parameter binding and execution using the MySQL prepared statement API.
5.  **Thread Safety:** Ensuring that each thread using the database has its own dedicated connection instance, managed through the `DatabaseMysql` singleton's thread-local storage mechanisms.

This unit does not define high-level business logic; rather, it serves as the foundational I/O layer for all database operations in the server. It is strictly coupled to the MySQL C API and relies on the `Database` and `SqlConnection` abstractions to integrate with the rest of the engine.

## Member-by-Member Behavior

### Construction and Lifecycle

**`MySQLConnection` (Constructor)**
The constructor initializes the `MySQLConnection` object. It takes a reference to the parent `Database` instance and passes it to the `SqlConnection` base class constructor. It explicitly sets the internal `mMysql` pointer (of type `MYSQL*`) to `nullptr`. This indicates that the actual network connection to the MySQL server is not established at construction time but is deferred until `OpenConnection` is called. This lazy initialization pattern allows for better control over connection pooling and thread-specific setup.

### Connection Management

**`OpenConnection`**
Although not detailed in the provided source snippet's body (only declared in the header), `OpenConnection` is responsible for initializing the `MYSQL` structure and connecting to the server. It likely parses the connection string (hostname, username, password, database) provided during the `DatabaseMysql` initialization phase. The `reconnect` boolean suggests it may attempt to restore a previous session or establish a fresh one.

**`Reconnect`**
This method attempts to re-establish a lost or closed connection to the MySQL server. It is crucial for handling transient network failures or server restarts. It likely calls `mysql_real_connect` again after resetting the internal state.

**`HandleMySQLError`**
This helper method processes errors returned by the MySQL C API. It takes an error number (`errNo`) and likely logs the error, potentially triggering a reconnection sequence if the error indicates a broken connection (e.g., "Lost connection to MySQL server"). It returns a boolean indicating whether the error was handled successfully or if the caller should abort the operation.

### Query Execution

**`Query`**
Executes a SELECT-style SQL query that returns a result set. It takes a SQL string, executes it via the MySQL API, and wraps the resulting `MYSQL_RES` and `MYSQL_FIELD` structures into a `std::unique_ptr<QueryResult>`. This abstraction allows the rest of the engine to iterate over rows and access columns by name or index without dealing with MySQL-specific types.

**`QueryNamed`**
Similar to `Query`, but returns a `std::unique_ptr<QueryNamedResult>`. This variant likely provides additional metadata or a different iteration interface optimized for accessing columns by name, improving readability in complex queries.

**`Execute`**
Executes a non-query SQL statement (INSERT, UPDATE, DELETE, CREATE, etc.). It returns a boolean indicating success or failure. It does not return a result set. This method is used for data modification and schema changes.

**`escape_string`**
Escapes special characters in a string for safe inclusion in SQL queries. This is a critical security feature to prevent SQL injection attacks. It uses the MySQL API's `mysql_escape_string` function to transform the input string `from` into the output buffer `to`, respecting the specified `length`.

### Transaction Control

**`BeginTransaction`**
Starts a new database transaction. It likely executes the SQL command `START TRANSACTION` or `BEGIN`. Subsequent queries executed on this connection will be part of this transaction until it is committed or rolled back.

**`CommitTransaction`**
Commits the current transaction, making all changes permanent. It executes the SQL command `COMMIT`.

**`RollbackTransaction`**
Aborts the current transaction, discarding all changes made since `BeginTransaction` was called. It executes the SQL command `ROLLBACK`.

### Prepared Statements

**`CreateStatement`**
Factory method that creates a new `MySqlPreparedStatement` object. It takes a format string (the SQL template with placeholders) and returns a pointer to the base class `SqlPreparedStatement`. This allows the engine to use prepared statements generically, while the MySQL-specific implementation handles the underlying C API calls.

### Internal Helpers

**`_TransactionCmd`**
A private helper method used by `BeginTransaction`, `CommitTransaction`, and `RollbackTransaction`. It executes the specific SQL command string passed to it (e.g., "START TRANSACTION") and returns the success status. This reduces code duplication in the transaction control methods.

**`_Query`**
A private helper method that performs the core work of executing a SQL query. It takes the SQL string and pointers to output variables for the result set (`pResult`), field metadata (`pFields`), row count (`pRowCount`), and field count (`pFieldCount`). It likely calls `mysql_query` or `mysql_real_query`, checks for errors, and populates the output pointers. This separation allows `Query` and `QueryNamed` to share the same execution logic while differing only in how they wrap the results.

## Cross-Unit Boundaries

### Called By: `DatabaseMysql::CreateConnection`
The `MySQLConnection` constructor is invoked exclusively by `DatabaseMysql::CreateConnection`. This method is part of the `DatabaseMysql` singleton, which manages the pool of database connections. When a new thread starts or requires a database connection, `DatabaseMysql` calls `CreateConnection`, which instantiates a `MySQLConnection` object. This establishes the dependency: `DatabaseMysql` is the factory for `MySQLConnection` instances.

### Calls Out: None Explicitly Listed in Map
While the `MySQLConnection` class internally uses the MySQL C API (`libmysqlclient`), the MAP does not list these as "calls out to other units" because they are external library calls, not calls to other C++ units within the WoWVMaNGOS codebase. However, it is important to note that `MySQLConnection` relies heavily on the `MYSQL*` structure and functions like `mysql_real_connect`, `mysql_query`, `mysql_store_result`, etc.

### Called By: Other Database Users
Although not explicitly listed in the "Called by" column of the MAP, `MySQLConnection` instances are used by various parts of the engine that need to interact with the database. These include:
-   **Game Objects:** Loading and saving game object data.
-   **Characters:** Saving character progress, inventory, and positions.
-   **World Events:** Managing scheduled events and timers.
-   **Chat Logs:** Recording chat messages.

These components typically obtain a `SqlConnection` pointer from the `Database` singleton and cast it to `MySQLConnection` implicitly or explicitly to perform their specific queries.

## Data Model

The `MySQLConnection` unit itself does not define or manipulate specific database tables. It is a generic database driver layer. The tables it interacts with are determined by the SQL strings passed to its `Query` and `Execute` methods by higher-level units. Therefore, no specific table schema is associated with this unit.

## Notable Implementation Details

1.  **MySQL Version Compatibility Warning:**
    The header contains a preprocessor check for `MYSQL_VERSION_ID >= 80000`. If detected, it issues a warning ("You are using an incompatible mysql version!"). This is because MySQL 8.0 removed the `my_bool` type, which this code relies on. The code defines `my_bool` as `char` to maintain compatibility, but this is a fragile workaround. Maintainers should be aware that upgrading to MySQL 8.0+ may require more extensive changes to this unit.

2.  **Thread-Local Connections:**
    The `DatabaseMysql` class has `ThreadStart` and `ThreadEnd` methods, indicating that each thread gets its own `MySQLConnection` instance. This is a common pattern in multi-threaded servers to avoid locking contention on a single database connection. `MySQLConnection` must therefore be designed to be thread-safe in terms of its own state, but it does not provide cross-thread synchronization.

3.  **Memory Management:**
    The `MySQLConnection` destructor (not shown in source but declared) is responsible for freeing the `MYSQL*` resource using `mysql_close`. It also likely cleans up any pending prepared statements or result sets. Proper cleanup is critical to prevent memory leaks and resource exhaustion.

4.  **Error Handling Strategy:**
    The `HandleMySQLError` method suggests a centralized error handling strategy. Instead of checking for errors after every MySQL API call, the code likely delegates error processing to this method. This simplifies the calling code but requires `HandleMySQLError` to be robust and capable of distinguishing between recoverable and fatal errors.

5.  **Prepared Statement Binding:**
    The `MySqlPreparedStatement` class (declared in the same header) handles the complexity of binding parameters to prepared statements. It converts the engine's generic `SqlStmtFieldData` into MySQL-specific `MYSQL_BIND` structures. This abstraction hides the intricacies of the MySQL C API's binding mechanism from the rest of the engine.

## Member Reference

**`MySQLConnection`**: Constructor that initializes the `MySQLConnection` object, setting the internal `mMysql` pointer to `nullptr` and passing the parent `Database` reference to the `SqlConnection` base class. It defers actual connection establishment to `OpenConnection`.

---

<!-- machine-true, projected from graph.json -->

## Map — MySQLConnection

*Source:* DatabaseMysql.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| MySQLConnection | ctor | — | DatabaseMysql/CreateConnection | — |
