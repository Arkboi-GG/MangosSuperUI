# DatabaseEnv

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# DatabaseEnv

## Purpose & Responsibilities

`DatabaseEnv` is a header-only configuration unit (`DatabaseEnv.h`) that establishes the global interface to the database layer within the `wowvmangos` server. It has no executable logic. Its responsibilities are strictly compile-time configuration and linkage:

1.  **Database Abstraction:** It defines a unified type alias `DatabaseType` that resolves to either `DatabasePostgre` or `DatabaseMysql` depending on the presence of the `DO_POSTGRESQL` macro. This allows the rest of the codebase to interact with databases using a consistent type, regardless of the underlying SQL engine.
2.  **SQL Syntax Normalization:** It provides preprocessor macros (`_LIKE_`, `_TABLE_SIM_`, `_CONCAT2_`, `_CONCAT3_`, `_OFFSET_`) that abstract syntactic differences between PostgreSQL and MySQL, enabling portable SQL string generation in C++ code.
3.  **Global Instance Declaration:** It declares four external global variables (`WorldDatabase`, `CharacterDatabase`, `LoginDatabase`, `LogsDatabase`) of type `DatabaseType`. These serve as the single points of access for the respective database connections throughout the server application.

## Cross-Unit Boundaries

This unit contains no functions, so it makes no calls. It establishes dependencies and interfaces:

*   **Dependencies:** It includes `Common.h`, `Database/Field.h`, `Database/QueryResult.h`, and either the PostgreSQL or MySQL specific database headers (`Database/DatabasePostgre.h` or `Database/DatabaseMysql.h`). It relies on these units to provide the base types that `DatabaseType` aliases.
*   **Dependents:** Any unit requiring access to the global database connections or portable SQL syntax macros includes `DatabaseEnv.h`. Units executing queries access the declared global variables (`WorldDatabase`, etc.) and use the macros to construct SQL strings compatible with the compiled backend.

## Data Model

This unit does not interact with specific database tables. It provides the infrastructure (connection objects and SQL syntax helpers) that other units use to interact with tables. No tables are associated with `DatabaseEnv` itself.

## Notable Implementation Details

1.  **Compile-Time Backend Selection:** The choice between PostgreSQL and MySQL is determined entirely at compile time via `#ifdef DO_POSTGRESQL`. There is no runtime detection or switching; the server binary is built for one specific backend.
2.  **Macro-Based SQL Generation:** SQL syntax differences are handled via macros. For example, `_CONCAT2_` expands to `( A || B )` for PostgreSQL or `CONCAT( A , B )` for MySQL. This avoids runtime overhead but requires careful usage to ensure proper quoting and escaping, which these macros do not handle.
3.  **Global State:** The reliance on global variables for database connections simplifies access but centralizes state. Initialization and lifecycle management of these globals occur in other units (likely a corresponding `.cpp` file not part of this unit).

## Member Reference

This unit has no members in the sense of functions or methods. The MAP is empty because `DatabaseEnv.h` is a declarative header file containing only type definitions, macro definitions, and external variable declarations.

---

<!-- machine-true, projected from graph.json -->

## Map — DatabaseEnv

*Source:* DatabaseEnv.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
