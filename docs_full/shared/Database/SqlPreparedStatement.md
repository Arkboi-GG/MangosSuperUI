# SqlPreparedStatement

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# SqlPreparedStatement

**Purpose & Responsibilities**

`SqlPreparedStatement` provides a type-safe, abstraction layer for executing SQL queries within the MaNGOS server engine. It decouples high-level game logic (e.g., saving player data, loading items) from the specifics of the underlying database driver (MySQL vs. plain SQL fallback).

The unit defines three core abstractions:
1.  **`SqlStmtFieldData`**: A variant-like structure that holds a single SQL parameter value (integer, float, string, etc.) along with its type metadata.
2.  **`SqlStmtParameters`**: A container that aggregates multiple `SqlStmtFieldData` objects, representing the full set of bound arguments for a single query.
3.  **`SqlStatement`**: The primary interface used by game code to bind parameters and execute pre-registered SQL statements. It manages the lifecycle of parameters and delegates execution to the `Database` singleton.

Additionally, it implements `SqlPlainPreparedStatement`, a fallback mechanism that simulates prepared statements by performing manual string interpolation and escaping. This allows the system to function even if the native database driver does not support true prepared statements, though at a performance and security cost compared to native binding.

**Member-by-Member Behavior**

### Parameter Data Representation (`SqlStmtFieldData`)

This class acts as a tagged union for SQL parameter values. It stores the raw data in either a fixed-size binary union (`m_binaryData`) or a dynamic string (`m_szStringData`), depending on the type.

*   **Constructors & Setters**: The templated constructor and `set` methods use template specialization to detect the input type (e.g., `uint32`, `char const*`) and store it in the appropriate member while setting the `m_type` enum. This ensures type safety at compile time.
*   **Getters (`toBool`, `toUint8`, ..., `toStr`)**: These methods retrieve the stored value. They include `MANGOS_ASSERT` checks to ensure the requested type matches the stored `m_type`. If a mismatch occurs (e.g., calling `toUint32()` on a string field), the assertion fails, preventing undefined behavior.
*   **Metadata Accessors (`type`, `buff`, `size`)**:
    *   `type()` returns the `SqlStmtFieldType` enum.
    *   `buff()` returns a pointer to the underlying data buffer. For strings, it points to the internal `std::string`'s character array; for numeric types, it points to the union member. This is used by the MySQL driver (`DatabaseMysql`) to pass raw pointers to the C API.
    *   `size()` returns the byte size of the data. For strings, it returns the string length; for numeric types, it returns `sizeof(type)`.

### Parameter Container (`SqlStmtParameters`)

This class manages the collection of parameters for a single SQL execution.

*   **`SqlStmtParameters` (ctor)**: Initializes the internal vector. If `nParams` is greater than zero, it reserves memory to avoid reallocations during binding.
*   **`reset`**: Clears the current parameters and reserves memory based on the expected argument count of the associated `SqlStatement`. This enables reuse of the parameter object for batched or repeated queries, reducing allocation overhead.
*   **`boundParams`**: Returns the number of parameters currently added.
*   **`addParam`**: Appends a `SqlStmtFieldData` to the internal vector.
*   **`params`**: Provides read-only access to the internal vector.

### Statement Execution Interface (`SqlStatement`)

This is the main class used by game entities (Players, Pets, Items, etc.) to interact with the database. It holds a reference to the `Database` instance and a `SqlStatementID` (which contains the statement index and expected argument count).

*   **`operator=`**: Implements deep copy semantics. It copies the statement ID and database pointer. Crucially, it deletes the old `SqlStmtParameters` object and creates a new one if the source has parameters. This prevents double-free errors and ensures each `SqlStatement` instance owns its parameter buffer.
*   **`Execute`**:
    1.  Detaches the current parameters from the statement (transferring ownership to the local scope).
    2.  Validates that the number of bound parameters matches the expected `arguments()` count. If not, it logs an error via `Log.Main/Out`, prints the SQL string via `Database/GetStmtString`, and asserts.
    3.  Delegates execution to `Database/ExecuteStmt`.
    4.  Deletes the parameters after execution.
*   **`DirectExecute`**: Similar to `Execute`, but delegates to `Database/DirectExecuteStmt`. This is likely used for queries that do not return a result set or require different handling by the driver.
*   **`PExecute` (Templates)**: Convenience templates that allow binding 1–5 parameters and executing in a single call. They internally call `arg()` to bind each parameter and then `Execute()`.
*   **`add*` Methods**: Helper methods (e.g., `addUInt32`, `addString`) that wrap the `arg()` template. They provide a clear, typed interface for binding specific data types.
*   **`arg` (Private Template)**: The core binding logic. It retrieves or creates the `SqlStmtParameters` object and adds a new `SqlStmtFieldData` constructed from the input value.

### Plain SQL Fallback (`SqlPlainPreparedStatement`)

This class inherits from `SqlPreparedStatement` and implements prepared statement semantics using plain SQL string manipulation. It is used when the database connection does not support native prepared statements.

*   **`SqlPlainPreparedStatement` (ctor)**:
    *   Counts the number of `?` placeholders in the format string to determine `m_nParams`.
    *   Checks if the query starts with "select" (case-insensitive) to set `m_bIsQuery`.
    *   Sets `m_bPrepared` to `true` immediately, as no server-side preparation is needed.
*   **`bind`**:
    *   Validates that the number of provided parameters matches `m_nParams`.
    *   Iterates through the parameters, converting each to a string representation using `DataToString`.
    *   Replaces each `?` in the original format string with the escaped string value. This effectively constructs the final SQL query string.
*   **`execute`**: Executes the constructed plain SQL string via `SqlConnection/Execute`.
*   **`DataToString`**: Converts a `SqlStmtFieldData` to its SQL literal string representation.
    *   Numeric types are formatted as integers/floats.
    *   Strings are escaped using `Database/escape_string` to prevent SQL injection and syntax errors.
    *   All values are wrapped in single quotes (`'...'`), including numbers. This is a notable implementation detail: while valid SQL, quoting numbers can sometimes interfere with index usage or type coercion in certain database configurations, though it ensures consistency.

**Cross-Unit Boundaries**

*   **`SqlStatement` ↔ `Database`**:
    *   `SqlStatement::Execute` and `DirectExecute` call `Database/ExecuteStmt` and `Database/DirectExecuteStmt` respectively. They pass the statement ID and the detached parameters.
    *   `SqlStatement` receives its `Database` pointer via its protected constructor, which is only accessible to the `Database` class (friend declaration). This enforces that `SqlStatement` objects are created and managed by the `Database` singleton.
*   **`SqlStatement` ↔ `Log.Main`**:
    *   On parameter mismatch, `SqlStatement` logs errors using `Log.Main/Out`.
*   **`SqlStatement` ↔ `Errors`**:
    *   Uses `MANGOS_ASSERT` (from `Errors.h`) to fail fast on critical errors like parameter mismatches or invalid type conversions.
*   **`SqlPlainPreparedStatement` ↔ `SqlConnection`**:
    *   The constructor takes a `SqlConnection&` reference.
    *   `execute()` calls `SqlConnection/Execute` to run the interpolated SQL.
    *   `DataToString` calls `SqlConnection/DB` to get the database handle for `escape_string`.
*   **`SqlStmtFieldData` ↔ `DatabaseMysql`**:
    *   `DatabaseMysql/addParam` calls `SqlStmtFieldData::type`, `buff`, and `size` to extract raw data for native MySQL binding.
    *   `DatabaseMysql/bind`, `execute`, and `prepare` check `SqlPreparedStatement::isPrepared`.
*   **Game Entities ↔ `SqlStatement`**:
    *   Numerous game classes (e.g., `Player.Main`, `Pet.Main`, `game_Objects_Item`) call `SqlStatement::operator=` to assign statement handles and `SqlStatement::Execute`/`PExecute` to perform database operations. This indicates `SqlStatement` is the central hub for all persistent data storage in the engine.

**Data Model**

This unit does not define or directly interact with specific database tables. It operates on abstract SQL statements identified by integer IDs. The actual table structures are defined in the database schema and referenced by the SQL strings stored in the `Database` unit. `SqlPreparedStatement` is agnostic to the schema; it only ensures that the correct number of parameters are bound to the placeholders in those SQL strings.

**Notable Implementation Details**

1.  **Manual Memory Management**: `SqlStatement` manually manages `SqlStmtParameters` via raw pointers (`new`/`delete`). The destructor deletes `m_pParams`, and `operator=` performs deep copying. This requires careful attention to avoid leaks or double-frees, especially since `detach()` transfers ownership.
2.  **Assertion-Heavy Error Handling**: The code relies heavily on `MANGOS_ASSERT` for error conditions (parameter count mismatches, type mismatches). In release builds, these assertions may be disabled, potentially leading to silent failures or crashes if the underlying assumptions are violated. The `Execute` methods log errors before asserting, providing some visibility.
3.  **Quoted Numbers in Plain SQL**: `SqlPlainPreparedStatement::DataToString` wraps all numeric values in single quotes. While this prevents SQL injection, it forces the database to treat numbers as strings, which may impact query plan optimization (e.g., index usage) depending on the database engine's type coercion rules.
4.  **No Result Set Handling**: `SqlStatement` itself does not handle result sets. It only executes the statement. The caller must handle any results via the `Database` or `QueryResult` mechanisms (not shown in this unit). `SqlPlainPreparedStatement` also ignores result sets, simply returning a boolean success status.
5.  **Template Specialization for Type Safety**: The use of template specializations for `SqlStmtFieldData::set` ensures that the type information is captured at compile time, avoiding runtime type checking overhead for the common case. However, it requires explicit specializations for every supported type.

## Member Reference

**toBool**: Retrieves the stored boolean value. Asserts that the field type is `FIELD_BOOL`.
**toUint8**: Retrieves the stored unsigned 8-bit integer. Asserts that the field type is `FIELD_UI8`.
**toInt8**: Retrieves the stored signed 8-bit integer. Asserts that the field type is `FIELD_I8`.
**toUint16**: Retrieves the stored unsigned 16-bit integer. Asserts that the field type is `FIELD_UI16`.
**toInt16**: Retrieves the stored signed 16-bit integer. Asserts that the field type is `FIELD_I16`.
**toUint32**: Retrieves the stored unsigned 32-bit integer. Asserts that the field type is `FIELD_UI32`.
**toInt32**: Retrieves the stored signed 32-bit integer. Asserts that the field type is `FIELD_I32`.
**toUint64**: Retrieves the stored unsigned 64-bit integer. Asserts that the field type is `FIELD_UI64`.
**toInt64**: Retrieves the stored signed 64-bit integer. Asserts that the field type is `FIELD_I64`.
**toFloat**: Retrieves the stored float value. Asserts that the field type is `FIELD_FLOAT`.
**toDouble**: Retrieves the stored double value. Asserts that the field type is `FIELD_DOUBLE`.
**toStr**: Retrieves the stored string as a `const char*`. Asserts that the field type is `FIELD_STRING`.
**SqlStmtParameters**: Constructor that initializes the parameter container, reserving memory if `nParams` > 0.
**reset**: Clears the parameter container and reserves memory based on the expected argument count of the provided `SqlStatement`.
**operator=**: Deep copies the `SqlStatement` from another instance, managing the `SqlStmtParameters` pointer to avoid double-frees.
**SqlStmtFieldData**: Default constructor initializing the type to `FIELD_NONE` and zeroing the binary data.
**~SqlStmtFieldData**: Destructor, currently empty.
**Execute#2**: Detaches parameters, validates count against expected arguments, logs errors on mismatch, and delegates execution to `Database/ExecuteStmt`.
**DirectExecute**: Similar to `Execute`, but delegates to `Database/DirectExecuteStmt`.
**type**: Returns the `SqlStmtFieldType` enum of the stored data.
**buff**: Returns a pointer to the underlying data buffer (string content or binary union).
**size**: Returns the byte size of the stored data (string length or type size).
**SqlPlainPreparedStatement**: Constructor that counts `?` placeholders, detects if the query is a SELECT, and marks the statement as prepared.
**bind**: Validates parameter count, converts each parameter to a string using `DataToString`, and replaces `?` placeholders in the format string.
**set#11**: Template specialization for `int64`.
**set#5**: Template specialization for `uint16`.
**set#6**: Template specialization for `int16`.
**set#12**: Template specialization for `float`.
**set#13**: Template specialization for `double`.
**set#4**: Template specialization for `uint8`.
**set#9**: Template specialization for `int32`.
**set#2**: Template specialization for `bool`.
**set#10**: Template specialization for `uint64`.
**set#3**: Template specialization for `int8`.
**set#8**: Template specialization for `uint32`.
**set#7**: Template specialization for `int64` (Note: Map lists two int64 sets, likely a duplicate or distinct overload in source context, but functionally identical).
**set**: Generic template method for setting the value and type.
**execute**: Executes the interpolated plain SQL string via `SqlConnection/Execute`.
**DataToString**: Converts a `SqlStmtFieldData` to a SQL literal string, escaping strings and quoting all values.
**~SqlPreparedStatement**: Virtual destructor.
**isPrepared**: Returns whether the statement has been prepared.
**isQuery**: Returns whether the statement is a SELECT query.
**params**: Returns the number of parameters expected.
**columns**: Returns the number of columns expected (only for queries).
**prepare**: Pure virtual function declared here, implemented in derived classes.
**bind#2**: Pure virtual function declared here, implemented in derived classes.
**execute#3**: Pure virtual function declared here, implemented in derived classes.
**SqlPreparedStatement**: Protected constructor initializing base fields.

---

<!-- machine-true, projected from graph.json -->

## Map — SqlPreparedStatement

*Source:* SqlPreparedStatement.cpp, SqlPreparedStatement.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| toBool | method | Errors/PrintStacktraceAndThrow | — | — |
| toUint8 | method | Errors/PrintStacktraceAndThrow | — | — |
| toInt8 | method | Errors/PrintStacktraceAndThrow | — | — |
| toUint16 | method | Errors/PrintStacktraceAndThrow | — | — |
| toInt16 | method | Errors/PrintStacktraceAndThrow | — | — |
| toUint32 | method | Errors/PrintStacktraceAndThrow | — | — |
| toInt32 | method | Errors/PrintStacktraceAndThrow | — | — |
| toUint64 | method | Errors/PrintStacktraceAndThrow | — | — |
| toInt64 | method | Errors/PrintStacktraceAndThrow | — | — |
| toFloat | method | Errors/PrintStacktraceAndThrow | — | — |
| toDouble | method | Errors/PrintStacktraceAndThrow | — | — |
| toStr | method | Errors/PrintStacktraceAndThrow | — | — |
| SqlStmtParameters | ctor | — | — | — |
| reset | method | SqlStatement/arguments | — | — |
| operator= | method | — | game_Objects_Item/DeleteAllFromDB, MasterPlayer.Main/SaveMails, Pet.Main/DeleteFromDB#2, Pet.Main/LoadPetFromDB, Pet.Main/SavePetToDB, Pet.Main/_SaveAuras, Pet.Main/_SaveSpellCooldowns, Pet.Main/_SaveSpells, Player.Main/_SaveAuras, Player.Main/_SaveBGData, Player.Main/_SaveInventory, Player.Main/_SaveSpellCooldowns, Player.Main/_SaveStats, WorldSession.CharacterHandler/HandlePlayerLogin | — |
| SqlStmtFieldData | ctor | — | — | — |
| ~SqlStmtFieldData | dtor | — | — | — |
| Execute#2 | method | Database/ExecuteStmt, Database/GetStmtString, Errors/PrintStacktraceAndThrow, Log.Main/Out, SqlStatement/arguments, SqlStatement/detach, SqlStatement/ID, SqlStmtParameters/boundParams | Creature.Main/LogDeath, Creature.Main/LogLongCombat, game_Battlegrounds_BattleGround/EndBattleGround, game_Objects_Item/LoadFromDB, game_Objects_Item/SaveToDB, GMTicketMgr/DeleteFromDB, GMTicketMgr/SaveToDB, MasterPlayer.Main/SaveActions, MasterPlayer.Main/SaveMails, Pet.Main/SavePetToDB, Pet.Main/_SaveAuras, Player.Main/PlayerLogToDB, Player.Main/SaveNewPlayer, Player.Main/SaveToDB, Player.Main/_SaveAuras, Player.Main/_SaveBGData, Player.Main/_SaveInventory, Player.Main/_SaveQuestStatus, Player.Main/_SaveSpellCooldowns, Player.Main/_SaveStats, WardenMac/Update, WardenWin/Update, World/LogMoneyTrade, World/LogTransaction, WorldSession.GMTicketHandler/HandleGMSurveySubmitOpcode, WorldSession.Main/SaveTutorialsData | — |
| DirectExecute | method | Database/DirectExecuteStmt, Database/GetStmtString, Errors/PrintStacktraceAndThrow, Log.Main/Out, SqlStatement/arguments, SqlStatement/detach, SqlStatement/ID, SqlStmtParameters/boundParams | — | — |
| type | method | — | DatabaseMysql/addParam, DatabaseMysql/ToMySQLType | — |
| buff | method | — | DatabaseMysql/addParam | — |
| size | method | — | DatabaseMysql/addParam | — |
| SqlPlainPreparedStatement | ctor | — | Database/CreateStatement#2 | — |
| bind | method | Errors/PrintStacktraceAndThrow, SqlStmtParameters/boundParams, SqlStmtParameters/params | — | — |
| set#11 | method | — | — | — |
| set#5 | method | — | — | — |
| set#6 | method | — | — | — |
| set#12 | method | — | — | — |
| set#13 | method | — | — | — |
| set#4 | method | — | — | — |
| set#9 | method | — | — | — |
| set#2 | method | — | — | — |
| set#10 | method | — | — | — |
| set#3 | method | — | — | — |
| set#8 | method | — | — | — |
| set#7 | method | — | — | — |
| set | method | — | — | — |
| execute | method | Database/Execute#3 | — | — |
| DataToString | method | Database/DB, Database/escape_string | — | — |
| ~SqlPreparedStatement | dtor | — | — | — |
| isPrepared | method | — | DatabaseMysql/bind, DatabaseMysql/execute#2, DatabaseMysql/prepare | — |
| isQuery | method | — | — | — |
| params | method | — | — | — |
| columns | method | — | — | — |
| prepare | decl | — | Database/GetStmt | — |
| bind#2 | decl | — | Database/ExecuteStmt#2 | — |
| execute#3 | decl | — | Database/ExecuteStmt#2 | — |
| SqlPreparedStatement | ctor | — | DatabaseMysql/MySqlPreparedStatement | — |
