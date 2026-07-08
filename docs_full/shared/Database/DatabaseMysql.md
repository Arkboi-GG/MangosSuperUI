# DatabaseMysql

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# DatabaseMysql

**Purpose & Responsibilities**

`DatabaseMysql` provides the MySQL-specific implementation of the abstract database interface for the MaNGOS-derived server engine. It manages the lifecycle of the MySQL C API library, initializes thread-local MySQL contexts, and handles low-level connection management, including automatic reconnection on network failures.

The unit defines two primary concrete classes:
1.  **`DatabaseMysql`**: A singleton-like manager responsible for initializing and shutting down the global MySQL library (`mysql_library_init`/`end`) and managing thread-local initialization (`mysql_thread_init`/`end`). It ensures the library is thread-safe upon startup.
2.  **`MySQLConnection`**: The active database session handler. It manages the `MYSQL*` handle, performs authentication, configures character sets (UTF-8), and executes SQL statements. It implements robust error handling, distinguishing between recoverable errors (like disconnects, which trigger a reconnect) and fatal errors (like schema mismatches, which halt the server). It also supports transactions and prepared statements via the `MySqlPreparedStatement` helper class.

This unit acts as the bridge between the generic `Database` abstraction layer and the native MySQL C API. It does not define high-level business logic or specific table schemas; instead, it provides the mechanisms for executing arbitrary SQL strings and prepared statements safely within a multi-threaded environment.

## Member-by-Member Behavior

### Library Lifecycle and Thread Management (`DatabaseMysql`)

The `DatabaseMysql` class manages the global state of the MySQL client library. Since the MySQL C API requires explicit initialization, this class uses a static counter (`db_count`) to ensure `mysql_library_init` is called only once when the first instance is created, and `mysql_library_end` is called when the last instance is destroyed.

*   **`DatabaseMysql` (Constructor)**: Increments the static `db_count`. If this is the first instance, it calls `mysql_library_init`. It then verifies that the linked MySQL library is thread-safe via `mysql_thread_safe()`. If the library is not thread-safe, it logs a fatal error and exits the process immediately, as the server engine relies on concurrent database access.
*   **`~DatabaseMysql` (Destructor)**: Calls `StopServer()` (defined in the base `Database` class) to clean up higher-level resources. It then decrements `db_count`. If the count reaches zero, it calls `mysql_library_end` to release global MySQL resources.
*   **`ThreadStart`**: Must be called at the beginning of any thread that intends to use the database. It calls `mysql_thread_init()` to allocate thread-local storage for the MySQL library. This is critical for preventing data races in the MySQL C API.
*   **`ThreadEnd`**: Must be called at the end of a database-using thread. It calls `mysql_thread_end()` to free the thread-local storage allocated by `ThreadStart`.

### Connection Management (`MySQLConnection`)

`MySQLConnection` holds the active `MYSQL*` handle and manages the physical link to the database server.

*   **`CreateConnection`**: Factory method in `DatabaseMysql` that instantiates a `MySQLConnection`.
*   **`OpenConnection`**: Initializes a new `MYSQL` struct via `mysql_init`. It configures the connection to use UTF-8 character sets (`MYSQL_SET_CHARSET_NAME`, `SET NAMES utf8`) to ensure proper handling of international characters. It respects the `m_use_socket` flag to determine whether to connect via TCP/IP or Unix domain sockets. It then attempts `mysql_real_connect`. On success, it explicitly enables `AUTOCOMMIT` mode. The code comments note that while autocommit might seem risky for data integrity, the server engine manages transactions explicitly via `BEGIN`/`COMMIT` commands, and leaving autocommit on prevents unnecessary transaction overhead for simple `SELECT` queries.
*   **`Reconnect`**: Attempts to restore a lost connection. It calls `OpenConnection(true)` and, if successful, calls `FreePreparedStatements()` because prepared statement handles are invalid after a reconnect. This ensures subsequent prepared statement executions will re-prepare the statements.
*   **`HandleMySQLError`**: Centralized error dispatcher. It inspects the MySQL error code (`errNo`) and decides the appropriate action:
    *   **Recoverable Disconnects** (`CR_SERVER_GONE_ERROR`, `CR_SERVER_LOST`, etc.): Closes the current handle and triggers `Reconnect()`.
    *   **Deadlocks** (`ER_LOCK_DEADLOCK`): Returns `false`, allowing the caller to retry the transaction.
    *   **Query-Specific Errors** (`ER_WRONG_VALUE_COUNT`, `ER_DUP_ENTRY`): Returns `false`, indicating the query failed but the connection is valid.
    *   **Fatal Schema/Parse Errors** (`ER_BAD_FIELD_ERROR`, `ER_NO_SUCH_TABLE`, `ER_PARSE_ERROR`): Logs a critical error indicating the database schema is outdated or the SQL is malformed, asserts failure, and terminates the core. This prevents the server from running against an incompatible database structure.
*   **`~MySQLConnection` (Destructor)**: Ensures cleanup by calling `FreePreparedStatements()` to release any cached prepared statements and then closing the MySQL connection with `mysql_close`.

### Query Execution

*   **`_Query`**: The internal workhorse for executing SQL strings that return results. It checks if the connection is alive, reconnecting if necessary. It measures execution time using `WorldTimer` for logging purposes. If `mysql_query` fails, it delegates to `HandleMySQLError`. If the error is recoverable (e.g., a reconnect occurred), it recursively retries the query. On success, it stores the result set (`mysql_store_result`), retrieves row/field counts, and fetches field metadata. It frees the result if no rows were affected/returned.
*   **`Query`**: Executes a SQL string and returns a `std::unique_ptr<QueryResult>`. It wraps the raw MySQL result in a `QueryResultMysql` object and advances to the first row (`NextRow`) before returning.
*   **`QueryNamed`**: Similar to `Query`, but returns a `QueryNamedResult`. It extracts column names from the `MYSQL_FIELD` array into a `QueryFieldNames` container, allowing callers to access columns by name rather than index.
*   **`Execute`**: Executes a SQL string that does not return a result set (e.g., `INSERT`, `UPDATE`, `DELETE`). Like `_Query`, it handles reconnection and error dispatching. It logs execution time if debug filtering is enabled.
*   **`_TransactionCmd`**: Helper for transaction control commands. It executes a simple SQL command (`START TRANSACTION`, `COMMIT`, `ROLLBACK`) and logs errors if they fail.
*   **`BeginTransaction`**, **`CommitTransaction`**, **`RollbackTransaction`**: Thin wrappers around `_TransactionCmd` that send the standard SQL transaction control statements.

### Prepared Statements (`MySqlPreparedStatement`)

`MySqlPreparedStatement` implements the `SqlPreparedStatement` interface for MySQL, wrapping the `mysql_stmt_*` family of functions.

*   **`CreateStatement`**: Factory method in `MySQLConnection` that creates a `MySqlPreparedStatement` instance.
*   **`MySqlPreparedStatement` (Constructor)**: Initializes the statement object with the SQL format string, a reference to the connection, and the raw `MYSQL*` pointer.
*   **`prepare`**: Initializes the statement handle (`mysql_stmt_init`) and prepares the SQL on the server (`mysql_stmt_prepare`). It retrieves parameter count and result metadata. If the statement is a `SELECT` but lacks result metadata, it logs an error. It allocates memory for input bindings (`MYSQL_BIND` array) if parameters are expected.
*   **`bind`**: Takes a `SqlStmtParameters` holder containing the values to bind. It iterates through the parameters and calls `addParam` for each, constructing the `MYSQL_BIND` structures. Finally, it calls `mysql_stmt_bind_param` to associate these bindings with the statement handle. It asserts if the number of bound parameters does not match the expected count.
*   **`addParam`**: Internal helper that maps a generic `SqlStmtFieldData` type to a specific MySQL `enum_field_types` using `ToMySQLType`. It populates the `MYSQL_BIND` structure with the buffer pointer, length, and type information.
*   **`ToMySQLType`**: Static utility that converts the engine's internal field types (e.g., `FIELD_I32`, `FIELD_STRING`) to MySQL C API types (e.g., `MYSQL_TYPE_LONG`, `MYSQL_TYPE_STRING`). It also sets the `is_unsigned` flag appropriately for unsigned integer types.
*   **`execute`**: Executes the prepared statement using `mysql_stmt_execute`. It logs errors if execution fails.
*   **`RemoveBinds`**: Cleans up all resources associated with the prepared statement: deletes binding arrays, frees result metadata, closes the statement handle, and resets internal flags. This is called in the destructor and during re-preparation.
*   **`escape_string`**: Wrapper around `mysql_real_escape_string` to safely escape special characters in strings before embedding them in SQL queries, preventing SQL injection.

## Cross-Unit Boundaries

*   **`CliRunnable/operator()`, `Master/_OnSignal`, `World/CharactersDatabaseWorkerThread`, `WorldRunnable/operator()`** call **`ThreadStart`** and **`ThreadEnd`**. These units represent various worker threads in the server engine. They must call `ThreadStart` before performing any database operations and `ThreadEnd` before exiting to ensure MySQL thread-local storage is correctly managed.
*   **`DatabaseMysql` constructor** calls **`Log.Main/Out`** and **`Log.Main/WaitBeforeContinueIfNeed`**. It uses the logging system to report fatal errors (non-thread-safe library) and pauses execution if configured to allow debugging.
*   **`~DatabaseMysql` destructor** calls **`Database/StopServer`**. This delegates to the base class to perform broader shutdown procedures before cleaning up MySQL-specific resources.
*   **`CreateConnection`** calls **`MySQLConnection/MySQLConnection`**. This is the factory pattern instantiation.
*   **`~MySQLConnection` destructor** calls **`Database/FreePreparedStatements`**. It ensures that any prepared statements held by the connection are cleaned up via the base class mechanism before closing the MySQL handle.
*   **`OpenConnection`**, **`Reconnect`**, **`HandleMySQLError`**, **`_Query`**, **`Execute`**, **`_TransactionCmd`**, **`prepare`**, **`bind`**, **`addParam`**, **`execute#2`** all call **`Log.Main/Out`** or **`Log.Main/HasLogFilter`**. They rely on the central logging system to report connection status, errors, and debug information.
*   **`_Query`** and **`Execute`** call **`shared_Util/getMSTime`** and **`WorldTimer/getMSTimeDiff`**. They use the global timer utilities to measure and log SQL execution latency.
*   **`HandleMySQLError`** calls **`Errors/PrintStacktraceAndThrow`** (implicitly via `ASSERT(false)` or explicit throws in some error paths, though the provided code shows `ASSERT`). It integrates with the error handling framework to terminate the server on fatal conditions.
*   **`Query`** and **`QueryNamed`** call **`QueryResultMysql/QueryResultMysql`** and **`QueryResultMysql/NextRow`**. They construct the result wrapper objects and advance the cursor to the first row, preparing the result for consumption by the caller.
*   **`MySqlPreparedStatement` constructor** calls **`SqlPreparedStatement/SqlPreparedStatement`**. It initializes the base class with the SQL format string and connection reference.
*   **`bind`** and **`addParam`** interact with **`SqlStmtParameters`** and **`SqlPreparedStatement`** members (`boundParams`, `params`, `buff`, `size`, `type`). These are part of the generic prepared statement abstraction, allowing the MySQL-specific implementation to consume generic parameter data.
*   **`prepare`** and **`execute#2`** check **`SqlPreparedStatement/isPrepared`**. They verify the statement's state before attempting MySQL API calls.

## Data Model

This unit does not interact with specific database tables directly. It provides the infrastructure for executing SQL queries against any table. The SQL strings passed to `Query`, `Execute`, or prepared statements are generated by other parts of the codebase. Therefore, no specific table schemas are relevant to this unit's internal logic.

## Notable Implementation Details

*   **Autocommit Mode**: The code explicitly enables `AUTOCOMMIT` after connecting. The comments clarify that this is intentional: the server engine manages transactions explicitly using `BEGIN`/`COMMIT` commands. Leaving autocommit on avoids the overhead of implicit transactions for simple `SELECT` queries, which would otherwise require wrapping in `START TRANSACTION`/`COMMIT` if autocommit were off.
*   **Reconnection Logic**: `HandleMySQLError` distinguishes between transient network errors (which trigger a reconnect) and logical errors (deadlocks, duplicates) or fatal errors (schema mismatch). This allows the server to survive temporary database disconnects without crashing.
*   **Prepared Statement Invalidation on Reconnect**: When `Reconnect` succeeds, it calls `FreePreparedStatements()`. This is crucial because `MYSQL_STMT` handles are tied to the specific connection session. After a reconnect, old handles are invalid, and statements must be re-prepared. The `MySqlPreparedStatement` class handles this by checking `isPrepared()` and re-initializing if necessary.
*   **Thread Safety**: The strict requirement to call `ThreadStart`/`ThreadEnd` reflects the MySQL C API's design. Failure to do so would lead to undefined behavior in a multi-threaded environment. The constructor's check for `mysql_thread_safe()` ensures the server refuses to start if the underlying library cannot support concurrency.
*   **Schema Validation**: `HandleMySQLError` treats `ER_BAD_FIELD_ERROR` and `ER_NO_SUCH_TABLE` as fatal. This is a safety mechanism to prevent the server from running against an outdated or corrupted database schema, which could lead to unpredictable behavior or data loss.
*   **UTF-8 Enforcement**: `OpenConnection` explicitly sets the character set to UTF-8 via both `mysql_options` and `SET NAMES`. This ensures consistent handling of international characters across different server configurations.

## Member Reference

**ThreadStart**: Initializes thread-local storage for the MySQL library by calling `mysql_thread_init()`. Must be called before any database operations in a new thread.

**ThreadEnd**: Frees thread-local storage for the MySQL library by calling `mysql_thread_end()`. Must be called when a thread finishes using the database.

**DatabaseMysql**: Constructor that increments the static instance counter. If this is the first instance, it initializes the global MySQL library (`mysql_library_init`) and verifies thread safety. Exits the process if the library is not thread-safe.

**~DatabaseMysql**: Destructor that calls `StopServer()` from the base class, decrements the instance counter, and calls `mysql_library_end()` if this was the last instance.

**CreateConnection**: Factory method that creates and returns a new `MySQLConnection` instance.

**~MySQLConnection**: Destructor that calls `FreePreparedStatements()` to clean up cached statements and then closes the MySQL connection with `mysql_close()`.

**OpenConnection**: Initializes a new MySQL connection, configures UTF-8 character sets, sets protocol options (socket vs. TCP), and authenticates with the server. Enables autocommit mode. Returns true on success.

**Reconnect**: Attempts to re-establish a lost connection by calling `OpenConnection(true)`. If successful, it frees prepared statements (as they become invalid) and logs the success.

**HandleMySQLError**: Dispatches MySQL error codes. Triggers reconnection for network errors, returns false for deadlocks/query errors, and asserts/fails for fatal schema or parse errors.

**_Query**: Internal method to execute a SQL string that returns results. Handles reconnection, logs execution time, stores the result set, and retrieves metadata. Recursively retries if a reconnect occurs.

**Query**: Executes a SQL string and returns a `std::unique_ptr<QueryResult>` wrapped in `QueryResultMysql`, advancing to the first row.

**QueryNamed**: Executes a SQL string and returns a `std::unique_ptr<QueryNamedResult>`, extracting column names for named access.

**Execute**: Executes a SQL string that does not return results (e.g., INSERT/UPDATE). Handles reconnection and logs execution time.

**_TransactionCmd**: Helper to execute transaction control SQL commands (`START TRANSACTION`, `COMMIT`, `ROLLBACK`) and log errors.

**BeginTransaction**: Wraps `_TransactionCmd` to start a new transaction.

**CommitTransaction**: Wraps `_TransactionCmd` to commit the current transaction.

**RollbackTransaction**: Wraps `_TransactionCmd` to rollback the current transaction.

**escape_string**: Wrapper around `mysql_real_escape_string` to safely escape special characters in strings.

**CreateStatement**: Factory method that creates a new `MySqlPreparedStatement` instance for the given SQL format string.

**MySqlPreparedStatement**: Constructor that initializes the statement object with the SQL format, connection reference, and raw MySQL pointer.

**~MySqlPreparedStatement**: Destructor that calls `RemoveBinds()` to clean up resources.

**prepare**: Initializes the MySQL statement handle, prepares the SQL on the server, retrieves parameter/result metadata, and allocates binding buffers.

**bind**: Binds input parameters from a `SqlStmtParameters` holder to the statement by iterating through parameters and calling `addParam`, then invoking `mysql_stmt_bind_param`.

**addParam**: Internal helper that maps a generic field data type to a MySQL type and populates the corresponding `MYSQL_BIND` structure.

**RemoveBinds**: Cleans up all resources associated with the prepared statement: deletes binding arrays, frees metadata, closes the statement handle, and resets internal state.

**execute#2**: Executes the prepared statement using `mysql_stmt_execute` and logs errors if it fails.

**ToMySQLType**: Static utility that converts the engine's internal field types to MySQL C API types and sets the unsigned flag appropriately.

---

<!-- machine-true, projected from graph.json -->

## Map — DatabaseMysql

*Source:* DatabaseMysql.cpp, DatabaseMysql.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ThreadStart | method | — | CliRunnable/operator(), Master/_OnSignal, World/CharactersDatabaseWorkerThread, WorldRunnable/operator() | — |
| ThreadEnd | method | — | CliRunnable/operator(), World/CharactersDatabaseWorkerThread, WorldRunnable/operator() | — |
| DatabaseMysql | ctor | Log.Main/Out, Log.Main/WaitBeforeContinueIfNeed | — | — |
| ~DatabaseMysql | dtor | Database/StopServer | — | — |
| CreateConnection | method | MySQLConnection/MySQLConnection | — | — |
| ~MySQLConnection | dtor | Database/FreePreparedStatements | — | — |
| OpenConnection | method | Log.Main/Out | — | — |
| Reconnect | method | Database/FreePreparedStatements, Log.Main/Out | — | — |
| HandleMySQLError | method | Errors/PrintStacktraceAndThrow, Log.Main/Out | — | — |
| _Query | method | Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out, shared_Util/getMSTime, WorldTimer/getMSTimeDiff | — | — |
| Query | method | QueryResultMysql/NextRow, QueryResultMysql/QueryResultMysql | — | — |
| QueryNamed | method | QueryResultMysql/NextRow, QueryResultMysql/QueryResultMysql | — | — |
| Execute | method | Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out, shared_Util/getMSTime, WorldTimer/getMSTimeDiff | — | — |
| _TransactionCmd | method | Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out | — | — |
| BeginTransaction | method | — | — | — |
| CommitTransaction | method | — | — | — |
| RollbackTransaction | method | — | — | — |
| escape_string | method | — | — | — |
| CreateStatement | method | — | — | — |
| MySqlPreparedStatement | ctor | SqlPreparedStatement/SqlPreparedStatement | — | — |
| ~MySqlPreparedStatement | dtor | — | — | — |
| prepare | method | Log.Main/Out, SqlPreparedStatement/isPrepared | — | — |
| bind | method | Errors/PrintStacktraceAndThrow, Log.Main/Out, SqlPreparedStatement/isPrepared, SqlStmtParameters/boundParams, SqlStmtParameters/params | — | — |
| addParam | method | Errors/PrintStacktraceAndThrow, SqlPreparedStatement/buff, SqlPreparedStatement/size, SqlPreparedStatement/type | — | — |
| RemoveBinds | method | — | — | — |
| execute#2 | method | Log.Main/Out, SqlPreparedStatement/isPrepared | — | — |
| ToMySQLType | method | SqlPreparedStatement/type | — | — |
