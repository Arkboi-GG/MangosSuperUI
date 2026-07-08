<!-- provenance: failed-members -->
# DatabasePostgre

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# DatabasePostgre

## Purpose & Responsibilities

`DatabasePostgre` and `PostgreSQLConnection` provide the PostgreSQL-specific implementation for the `wowvmangos` database abstraction layer. Conditionally compiled when `DO_POSTGRESQL` is defined, this unit wraps the native `libpq` C library to manage database connections, execute SQL queries and commands, handle transactions, and parse results. It ensures thread-safety of the underlying library, manages connection lifecycles, and translates generic database interface calls into specific `libpq` operations.

## Member-by-Member Behavior

### Initialization and Factory

*   **`DatabasePostgre::DatabasePostgre()`**: This constructor performs a one-time global initialization check. It increments a static counter `db_count`. If this is the first instance created (`db_count` was 0), it verifies that the linked `libpq` library is thread-safe by calling `PQisthreadsafe()`. If the library is not thread-safe, it logs a fatal error and terminates the process immediately. Subsequent instances skip this check.
*   **`DatabasePostgre::~DatabasePostgre()`**: The destructor is empty. Resource cleanup for individual connections is handled by `PostgreSQLConnection`.
*   **`DatabasePostgre::CreateConnection()`**: A protected virtual factory method inherited from the base `Database` class. It instantiates and returns a new `PostgreSQLConnection` object, allowing the base class to create the correct connection type without knowing the specific implementation.

### Connection Management

*   **`PostgreSQLConnection::OpenConnection(bool reconnect)`**: Establishes the physical connection to the PostgreSQL server using `PQsetdbLogin`. It determines whether to use a Unix domain socket or TCP/IP based on the `m_port_or_socket` member: if it equals `"localhost"`, it passes `nullptr` for the host (triggering socket usage); otherwise, it uses the configured host and port. After attempting the connection, it checks the status via `PQstatus()`. If the connection fails, it logs the error message from `libpq`, frees the connection handle, and returns `false`. On success, it logs the connection event and the server version.
*   **`PostgreSQLConnection::~PostgreSQLConnection()`**: Cleans up the database connection by calling `PQfinish(mPGconn)` to release resources associated with the `PGconn` handle.

### Query Execution

*   **`PostgreSQLConnection::_Query(...)`**: A private helper method that executes a SQL query string using `PQexec`. It records the start time using `WorldTimer` to measure execution duration. It validates the result status; if it is not `PGRES_TUPLES_OK`, it logs the SQL statement and the error message, clears the result, and returns `false`. If successful, it logs the query text and duration (if debug logging is enabled), then extracts the number of rows (`PQntuples`) and fields (`PQnfields`). Crucially, if the result set contains zero rows, it clears the result and returns `false`, indicating an empty result set rather than an error.
*   **`PostgreSQLConnection::Query(std::string const& sql)`**: Executes a SQL query and returns a `std::unique_ptr<QueryResult>`. It delegates to `_Query` to perform the execution. If `_Query` succeeds, it wraps the raw `PGresult` in a `QueryResultPostgre` object, advances to the first row via `NextRow()`, and returns the unique pointer. It returns `nullptr` if the connection is invalid, the query fails, or the result set is empty.
*   **`PostgreSQLConnection::QueryNamed(std::string const& sql)`**: Similar to `Query`, but returns a `std::unique_ptr<QueryNamedResult>`. After obtaining the result via `_Query`, it iterates through the fields using `PQfname` to build a `QueryFieldNames` vector mapping column indices to names. It then wraps the result in a `QueryNamedResult` object, enabling column access by name.

### Command Execution and Transactions

*   **`PostgreSQLConnection::Execute(std::string const& sql)`**: Executes a non-query SQL command (such as INSERT, UPDATE, or DELETE) using `PQexec`. It expects the result status to be `PGRES_COMMAND_OK`. If the status indicates an error, it logs the SQL and the error message, and returns `false`. On success, it logs the query duration (if debug logging is enabled), clears the result, and returns `true`.
*   **`PostgreSQLConnection::_TransactionCmd(std::string const& sql)`**: A private helper that executes transaction control commands. It sends the SQL string via `PQexec` and checks for `PGRES_COMMAND_OK`. On failure, it logs the error at `LOG_LVL_ERROR`; on success, it logs the command at `LOG_LVL_DEBUG`.
*   **`PostgreSQLConnection::BeginTransaction()`**: Initiates a database transaction by calling `_TransactionCmd` with `"START TRANSACTION"`.
*   **`PostgreSQLConnection::CommitTransaction()`**: Commits the current transaction by calling `_TransactionCmd` with `"COMMIT"`.
*   **`PostgreSQLConnection::RollbackTransaction()`**: Rolls back the current transaction by calling `_TransactionCmd` with `"ROLLBACK"`.

### Utility

*   **`PostgreSQLConnection::escape_string(char* to, char const* from, unsigned long length)`**: Wraps the `libpq` function `PQescapeString` to safely escape special characters in input strings, preventing SQL injection. It validates that the connection handle and pointers are non-null before proceeding.

## Cross-Unit Boundaries

*   **Calls Out:**
    *   **`sLog`**: Used extensively for logging connection statuses, SQL errors, query text, and execution times.
    *   **`WorldTimer`**: Used in `_Query` and `Execute` to measure the duration of SQL operations in milliseconds.
    *   **`libpq`**: All direct interactions with the PostgreSQL server are performed via functions from `libpq-fe.h` (e.g., `PQconnectdb`, `PQexec`, `PQfinish`).
    *   **`QueryResultPostgre` / `QueryNamedResult`**: These classes are instantiated to wrap raw `PGresult` data and return it to callers via smart pointers.
    *   **`MaNGOS::OperatorNew`**: Referenced in the header as a friend class, likely for custom memory allocation policies.

*   **Called By:**
    *   **`Database` (Base Class)**: The base `Database` class calls `CreateConnection()` to instantiate the connection and invokes the overridden virtual methods (`Query`, `Execute`, `BeginTransaction`, etc.) through the `SqlConnection` interface.

## Data Model

This unit does not interact with specific database tables. It operates on raw SQL strings provided by callers and is entirely agnostic to the database schema.

## Notable Implementation Details

1.  **Thread Safety Enforcement**: The constructor of `DatabasePostgre` enforces that `libpq` is thread-safe. If `PQisthreadsafe()` returns false, the application exits immediately. This is a critical safeguard for multi-threaded server environments.
2.  **Empty Result Semantics**: The `_Query` method returns `false` if the result set is empty (`PQntuples` is 0). Consequently, `Query()` and `QueryNamed()` return `nullptr` for valid queries that yield no rows. Callers must distinguish between a `nullptr` return value indicating "no rows" versus "connection error" by checking the connection state or relying on the fact that errors are logged.
3.  **Localhost Socket Detection**: `OpenConnection` uses a simple string comparison (`m_port_or_socket == "localhost"`) to decide whether to use Unix domain sockets. This assumes that "localhost" always implies a local socket connection, which may not hold true in all network configurations.
4.  **Manual Memory Management**: While `std::unique_ptr` is used for returning `QueryResult` objects, internal `PGresult` handles are managed manually with `PQclear` to prevent memory leaks. Care must be taken to ensure `PQclear` is called in all error paths.

## Member Reference

**DatabasePostgre::DatabasePostgre()**
Constructor that initializes the static `db_count` and verifies that the libpq library is thread-safe. Exits the application if thread safety is not guaranteed.

**DatabasePostgre::~DatabasePostgre()**
Destructor. Currently empty, as resource management is handled by the `PostgreSQLConnection` instances.

**DatabasePostgre::CreateConnection()**
Factory method that creates and returns a new `PostgreSQLConnection` object.

**PostgreSQLConnection::~PostgreSQLConnection()**
Destructor that calls `PQfinish` to close the database connection and free resources.

**PostgreSQLConnection::OpenConnection(bool reconnect)**
Establishes a connection to the PostgreSQL server using `PQsetdbLogin`. Handles localhost socket vs. TCP distinction. Logs connection status and server version.

**PostgreSQLConnection::Query(std::string const& sql)**
Executes a SQL query and returns a `std::unique_ptr<QueryResult>` wrapping the result set. Returns `nullptr` if the query fails or returns no rows.

**PostgreSQLConnection::QueryNamed(std::string const& sql)**
Executes a SQL query and returns a `std::unique_ptr<QueryNamedResult>` that maps column names to indices. Returns `nullptr` if the query fails or returns no rows.

**PostgreSQLConnection::Execute(std::string const& sql)**
Executes a SQL command (non-query) and returns `true` on success. Logs errors if the command fails.

**PostgreSQLConnection::escape_string(char* to, char const* from, unsigned long length)**
Wraps `PQescapeString` to safely escape special characters in SQL strings, preventing injection attacks.

**PostgreSQLConnection::BeginTransaction()**
Begins a database transaction by executing `START TRANSACTION`.

**PostgreSQLConnection::CommitTransaction()**
Commits the current transaction by executing `COMMIT`.

**PostgreSQLConnection::RollbackTransaction()**
Rolls back the current transaction by executing `ROLLBACK`.

**PostgreSQLConnection::_TransactionCmd(std::string const& sql)**
Private helper that executes a transaction control command and logs the result.

**PostgreSQLConnection::_Query(std::string const& sql, PGresult** pResult, uint64* pRowCount, uint32* pFieldCount)**
Private helper that executes a SQL query, checks for errors, and populates the result pointer, row count, and field count. Returns `false` on error or if no rows are returned.

---

<!-- machine-true, projected from graph.json -->

## Map — DatabasePostgre

*Source:* DatabasePostgre.cpp, DatabasePostgre.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|

---

<!-- verify: failed-members | invented: DatabasePostgre::CreateConnection, DatabasePostgre::DatabasePostgre, DatabasePostgre::~DatabasePostgre, PostgreSQLConnection::BeginTransaction, PostgreSQLConnection::CommitTransaction, PostgreSQLConnection::Execute, PostgreSQLConnection::OpenConnection, PostgreSQLConnection::Query, PostgreSQLConnection::QueryNamed, PostgreSQLConnection::RollbackTransaction, PostgreSQLConnection::_Query, PostgreSQLConnection::_TransactionCmd, PostgreSQLConnection::~PostgreSQLConnection -->
