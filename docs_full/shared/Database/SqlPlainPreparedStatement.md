# SqlPlainPreparedStatement

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# SqlPlainPreparedStatement

**Purpose & Responsibilities**

`SqlPlainPreparedStatement` is a concrete implementation of the abstract `SqlPreparedStatement` interface within the `wowvmangos` database abstraction layer. It provides a "plain SQL" execution strategy for prepared statements. Unlike native database driver prepared statements (which send parameterized queries to the server for compilation and caching), `SqlPlainPreparedStatement` constructs the final SQL query string entirely in C++ memory before sending it to the database.

It achieves this by taking a format string containing `?` placeholders and a set of typed parameters (`SqlStmtParameters`). During the `bind` phase, it replaces each `?` with the properly escaped and formatted literal value of the corresponding parameter. The resulting complete SQL string is then executed as a standard text query. This approach allows the system to support prepared-statement semantics (parameter binding, type safety) even if the underlying database connection does not support native prepared statements, or if the overhead of native preparation is undesirable for simple, infrequent queries.

**Member-by-Member Behavior**

The unit consists of two primary members defined in the MAP: the destructor and the `prepare` method. Both are trivial implementations reflecting the design philosophy that "plain" statements require no server-side preparation step.

1.  **Destruction**: The destructor cleans up the object. Since `SqlPlainPreparedStatement` holds no raw pointers to heap-allocated resources (it inherits from `SqlPreparedStatement`, which manages its own lifetime relative to the `SqlConnection`), the destructor is empty.
2.  **Preparation**: The `prepare` method is overridden to always return `true`. In the context of this class, "preparation" is a no-op because the SQL is assembled dynamically at bind time. There is no server-side statement handle to initialize.

**Cross-Unit Boundaries**

*   **Inheritance from `SqlPreparedStatement`**: `SqlPlainPreparedStatement` inherits from `SqlPreparedStatement` (defined in `SqlPreparedStatement.h`). It overrides `prepare()`, `bind()`, and `execute()`. While `bind()` and `execute()` are part of this class's logic, they are not listed in the MAP for this specific unit, implying the MAP focuses on the unique or overriding aspects relevant to the "Plain" variant's lifecycle initialization. However, the behavior of `prepare` is explicitly defined here.
*   **No Outgoing Calls**: The `prepare` method and destructor do not call into any other units. They are self-contained.
*   **No Incoming Calls**: The MAP indicates no other units explicitly call `~SqlPlainPreparedStatement` or `prepare` from outside the class hierarchy. These are typically invoked internally by the base class or the `Database`/`SqlStatement` orchestration logic during the statement lifecycle.

**Data Model**

This unit does not interact directly with database tables. It operates on SQL strings and parameter data. The actual table interactions occur downstream in the `execute` method (not detailed in the MAP but present in the source), where the constructed string is sent to the `SqlConnection`. Therefore, no specific tables are associated with `SqlPlainPreparedStatement` itself.

**Notable Implementation Details**

*   **Always Prepared**: The `prepare()` method returns `true` unconditionally. This signals to the caller that the statement is ready for binding and execution immediately. This contrasts with native prepared statement implementations that might need to send a `PREPARE` command to the database server and wait for a response.
*   **String-Based Execution**: The core logic of this class (visible in the non-MAP members `bind` and `execute`) relies on string manipulation. The `bind` method iterates through parameters, converts them to strings using `DataToString`, and replaces `?` placeholders in the format string. This makes the class vulnerable to SQL injection if the `DataToString` escaping logic is flawed, though it provides compatibility across different database backends that may not support native prepared statements uniformly.
*   **Stateless Preparation**: Because the preparation step is a no-op, the object does not maintain any state related to a server-side statement handle. All state is contained within the `m_szPlainRequest` string (built during bind) and the inherited base class members.

## Member Reference

**~SqlPlainPreparedStatement**
Destructor for `SqlPlainPreparedStatement`. It is an empty override of the base class destructor. It performs no cleanup actions as the class does not manage any dynamic memory resources directly.

**prepare**
Overrides the pure virtual `prepare` method from `SqlPreparedStatement`. It returns `true` immediately, indicating that the statement is considered "prepared" without performing any actual preparation steps. This reflects the design that plain SQL statements do not require server-side compilation or handle allocation.

---

<!-- machine-true, projected from graph.json -->

## Map — SqlPlainPreparedStatement

*Source:* SqlPreparedStatement.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ~SqlPlainPreparedStatement | dtor | — | — | — |
| prepare | method | — | — | — |
