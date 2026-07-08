# SqlStatement

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# SqlStatement

**SqlStatement** is a lightweight wrapper around a pre-registered SQL statement identifier (`SqlStatementID`) and a `Database` connection. It provides a type-safe, fluent interface for binding parameters to prepared SQL statements and executing them. Its primary responsibility is to abstract the complexity of parameter binding (handling different C++ types, converting them to a uniform internal representation, and managing memory for the parameter buffer) while exposing simple execution methods (`Execute`, `DirectExecute`, and templated `PExecute`).

This unit does not handle the preparation of the SQL statement itself (which is managed by `Database` and the `SqlPreparedStatement` hierarchy) nor the low-level database driver interactions. Instead, it acts as the client-facing API for interacting with prepared statements within the WowVMaNGOS codebase.

## Purpose & Responsibilities

1.  **Parameter Binding:** It converts various C++ primitive types (`uint32`, `float`, `std::string`, etc.) and `nullptr` into a unified internal structure (`SqlStmtFieldData`) stored in a `SqlStmtParameters` object.
2.  **Execution Orchestration:** It triggers the execution of the prepared statement associated with its ID via the linked `Database` object.
3.  **Resource Management:** It manages the lifecycle of the `SqlStmtParameters` object, allocating it lazily when the first parameter is added and cleaning it up upon destruction or detachment.
4.  **Convenience APIs:** It provides templated `PExecute` methods for common cases (1–5 parameters) to reduce boilerplate code in callers.

## Member-by-Member Behavior

### Construction and Destruction

*   **`SqlStatement` (ctor)**: Initializes the statement with a `SqlStatementID` and a reference to a `Database` object. The `SqlStatementID` contains the unique index of the prepared statement in the database registry and the expected number of arguments. The constructor sets `m_pParams` to `nullptr`, deferring allocation until needed. This constructor is `protected` and only accessible to `Database` (via friendship), ensuring that `SqlStatement` objects are created exclusively through the `Database`'s factory methods (e.g., `CreateStatement`).
*   **`~SqlStatement` (dtor)**: Deletes the dynamically allocated `SqlStmtParameters` object (`m_pParams`) if it exists. This ensures no memory leaks occur for bound parameters.

### Parameter Binding Methods

These methods accept a value of a specific type, wrap it in a `SqlStmtFieldData` object, and append it to the internal `SqlStmtParameters` container. They all follow the same pattern:
1.  Call `get()` to ensure `m_pParams` is allocated (creating it if necessary with capacity reserved for the expected number of arguments).
2.  Create a `SqlStmtFieldData` from the input value using its templated constructor.
3.  Call `addParam` on `m_pParams`.

*   **`addNull`**: Binds a SQL `NULL` value. Internally calls `arg(nullptr)`, which uses the `std::nullptr_t` specialization of `SqlStmtFieldData::set` to mark the field type as `FIELD_NONE`.
*   **`addBool`**: Binds a `bool` value.
*   **`addUInt8` / `addInt8`**: Binds 8-bit unsigned/signed integers.
*   **`addUInt16` / `addInt16`**: Binds 16-bit unsigned/signed integers.
*   **`addUInt32` / `addInt32`**: Binds 32-bit unsigned/signed integers. These are heavily used throughout the codebase for IDs, counts, and flags.
*   **`addUInt64` / `addInt64`**: Binds 64-bit unsigned/signed integers. Used for large IDs (like GUIDs) or timestamps.
*   **`addFloat` / `addDouble`**: Binds floating-point numbers.
*   **`addString` (overloads)**:
    *   `addString(char const* var)`: Binds a C-string.
    *   `addString(std::string const& var)`: Binds a `std::string` by passing its `c_str()`.
    *   `addString(std::ostringstream& ss)`: Binds the content of an `ostringstream`. **Notable Implementation Detail**: After extracting the string, this overload clears the stream (`ss.str(std::string())`). This allows callers to reuse the same `ostringstream` object for multiple bindings without accumulating previous content, though it modifies the caller's object state.

### Execution and State Access

*   **`ID`**: Returns the integer ID of the prepared statement from `m_index`. Used by `SqlPreparedStatement` implementations to identify which statement to execute.
*   **`arguments`**: Returns the expected number of arguments for this statement from `m_index`. Used for validation and memory reservation.
*   **`get`**: A private helper that lazily initializes `m_pParams` if it is null. It allocates a new `SqlStmtParameters` object with capacity equal to the statement's expected argument count. This optimization avoids repeated reallocations if many parameters are bound.
*   **`detach`**: Detaches the current `SqlStmtParameters` object from the `SqlStatement`, setting `m_pParams` to `nullptr`. It returns ownership of the parameters to the caller. If no parameters were bound, it creates an empty `SqlStmtParameters` object. This is used by `SqlPreparedStatement` to take ownership of the bound parameters for execution.
*   **`PExecute` (templates)**: A family of templated methods (`PExecute<ParamType1>`, `PExecute<ParamType1, ParamType2>`, etc., up to 5 parameters). Each variant calls `arg()` for each parameter and then calls `Execute()`. This provides a concise syntax for simple queries, e.g., `stmt.PExecute(playerGUID, timestamp)`.

## Cross-Unit Boundaries

### Calls Out (Dependencies)

*   **`Database`**: The `SqlStatement` holds a raw pointer to a `Database` object (`m_pDB`). While the MAP does not explicitly list `Database` in the "Calls out" column for these specific members, the `SqlStatement` constructor requires a `Database&`, and the execution methods (`Execute`, `DirectExecute`) implicitly rely on `m_pDB` to perform the actual SQL execution. The `SqlStatementID` initialization is also controlled by `Database`.
*   **`SqlStmtParameters`**: The `SqlStatement` creates and manages a `SqlStmtParameters` object. It calls `SqlStmtParameters::addParam`, `SqlStmtParameters::reset` (indirectly via reconstruction), and `SqlStmtParameters::swap` (not directly in this unit, but part of the collaboration).
*   **`SqlStmtFieldData`**: The `arg` template method constructs `SqlStmtFieldData` objects to wrap input values before passing them to `SqlStmtParameters`.

### Called By (Consumers)

The MAP lists numerous callers for the binding methods, indicating widespread use across the codebase for persisting entity states:

*   **`Player.Main`**: Uses `addUInt32`, `addUInt64`, `addFloat`, `addString`, `addInt8`, `addNull` extensively for saving player data (`SaveToDB`, `SaveNewPlayer`, `_SaveAuras`, `_SaveInventory`, `_SaveQuestStatus`, `_SaveSpellCooldowns`, `_SaveStats`, `_SaveBGData`) and logging (`PlayerLogToDB`).
*   **`game_Objects_Item`**: Uses `addUInt32`, `addUInt8`, `addUInt16`, `addString` for item persistence (`SaveToDB`) and loading (`LoadFromDB`).
*   **`GMTicketMgr`**: Uses `addUInt32`, `addUInt64`, `addUInt8`, `addUInt16`, `addFloat`, `addString` for ticket management (`SaveToDB`, `DeleteFromDB`).
*   **`Pet.Main`**: Uses `addUInt32`, `addUInt64`, `addFloat`, `addString` for pet data (`SavePetToDB`, `_SaveAuras`).
*   **`Creature.Main`**: Uses `addUInt32`, `addString` for logging creature events (`LogDeath`, `LogLongCombat`).
*   **`World`**: Uses `addUInt32`, `addString` for transaction logging (`LogMoneyTrade`, `LogTransaction`).
*   **`WardenMac` / `WardenWin`**: Use `addUInt32`, `addString` for anti-cheat updates.
*   **`MasterPlayer.Main`**: Uses `addUInt32`, `addUInt64` for mail and action bar saves.
*   **`WorldSession.GMTicketHandler`**: Uses `addUInt32`, `addString` for survey submissions.

## Data Model

The `SqlStatement` unit itself does not define or interact with specific database tables directly. It operates on abstract prepared statement IDs. The tables it indirectly interacts with are determined by the `SqlStatementID` passed during construction and the SQL strings registered in the `Database` unit. Based on the callers listed in the MAP, it supports operations on tables related to:

*   **Player Data**: `characters`, `character_aura`, `character_inventory`, `character_queststatus`, `character_spellcooldowns`, `character_stats`, `character_bg_data`.
*   **Items**: `item_instance`.
*   **Pets**: `pet_aura`, `pet_save`.
*   **Tickets**: `gm_ticket`.
*   **Logs**: `log_death`, `log_long_combat`, `log_money_trade`, `log_transaction`.
*   **Mails**: `mail_items`, `mail_lists`.

No specific column names or schemas are defined in this unit. The mapping between bound parameters and table columns is established in the SQL strings registered elsewhere (likely in `Database` or specific manager classes).

## Notable Implementation Details

1.  **Lazy Allocation**: `SqlStmtParameters` is not allocated in the constructor. It is allocated on-demand in `get()` when the first parameter is added. This saves memory for statements that might be created but never executed or bound.
2.  **Memory Ownership Transfer**: The `detach()` method transfers ownership of the `SqlStmtParameters` object to the caller (typically `SqlPreparedStatement`). The `SqlStatement` then sets its pointer to `null`. This design allows the `SqlPreparedStatement` to manage the lifetime of the parameters during execution, potentially reusing them or clearing them after the query completes.
3.  **Type Safety via Templates**: The `arg` template and `SqlStmtFieldData` specializations provide compile-time type safety. Passing an incorrect type (e.g., a `double` to `addUInt32`) would require an explicit cast, preventing accidental truncation or misinterpretation.
4.  **`ostringstream` Side Effect**: The `addString(std::ostringstream&)` overload clears the stream after use. This is a subtle side effect that callers must be aware of if they intend to reuse the stream for other purposes immediately after binding.
5.  **Copy Constructor**: The copy constructor deep-copies the `SqlStmtParameters` if it exists. This allows `SqlStatement` objects to be copied safely, which might be useful for debugging or logging purposes, although typically statements are used by reference or moved.
6.  **Friendship with `Database`**: The constructor is `protected` and `Database` is a `friend`. This enforces that `SqlStatement` instances are only created through the `Database`'s controlled interface, ensuring that the statement ID is valid and the database connection is properly associated.

## Member Reference

*   **`~SqlStatement`**: Destructor. Deletes the `SqlStmtParameters` object if it exists.
*   **`SqlStatement`**: Protected constructor. Initializes the statement with a `SqlStatementID` and `Database` reference. Only callable by `Database`.
*   **`ID`**: Returns the integer ID of the prepared statement.
*   **`arguments`**: Returns the expected number of arguments for the statement.
*   **`addNull`**: Binds a SQL NULL value.
*   **`addBool`**: Binds a boolean value.
*   **`addUInt8`**: Binds an 8-bit unsigned integer.
*   **`addInt8`**: Binds an 8-bit signed integer.
*   **`addUInt16`**: Binds a 16-bit unsigned integer.
*   **`addInt16`**: Binds a 16-bit signed integer.
*   **`addUInt32`**: Binds a 32-bit unsigned integer.
*   **`addInt32`**: Binds a 32-bit signed integer.
*   **`addUInt64`**: Binds a 64-bit unsigned integer.
*   **`addInt64`**: Binds a 64-bit signed integer.
*   **`addFloat`**: Binds a 32-bit floating-point number.
*   **`addDouble`**: Binds a 64-bit floating-point number.
*   **`addString#3`**: Binds the content of an `ostringstream`, clearing the stream afterward.
*   **`addString#2`**: Binds a `std::string` by converting it to a C-string.
*   **`addString`**: Binds a C-string (`char const*`).
*   **`SqlStatement#2`**: Copy constructor. Deep-copies the `SqlStmtParameters` if present.
*   **`get`**: Private helper. Lazily allocates `SqlStmtParameters` if not already present.
*   **`detach`**: Detaches and returns ownership of the `SqlStmtParameters` object, setting the internal pointer to null.

---

<!-- machine-true, projected from graph.json -->

## Map — SqlStatement

*Source:* SqlPreparedStatement.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ~SqlStatement | dtor | — | — | — |
| SqlStatement | ctor | — | — | — |
| ID | method | — | SqlPreparedStatement/DirectExecute, SqlPreparedStatement/Execute#2 | — |
| arguments | method | — | SqlPreparedStatement/DirectExecute, SqlPreparedStatement/Execute#2, SqlPreparedStatement/reset | — |
| addNull | method | — | Player.Main/PlayerLogToDB | — |
| addBool | method | — | — | — |
| addUInt8 | method | — | game_Objects_Item/SaveToDB, GMTicketMgr/SaveToDB, Pet.Main/_SaveAuras, Player.Main/SaveNewPlayer, Player.Main/SaveToDB, Player.Main/_SaveAuras, Player.Main/_SaveInventory, Player.Main/_SaveQuestStatus | — |
| addInt8 | method | — | Player.Main/SaveNewPlayer, Player.Main/_SaveAuras | — |
| addUInt16 | method | — | game_Objects_Item/SaveToDB, GMTicketMgr/SaveToDB, Player.Main/SaveToDB | — |
| addInt16 | method | — | — | — |
| addUInt32 | method | — | game_Battlegrounds_BattleGround/EndBattleGround, game_Objects_Item/LoadFromDB, game_Objects_Item/SaveToDB, GMTicketMgr/DeleteFromDB, GMTicketMgr/SaveToDB, MasterPlayer.Main/SaveActions, MasterPlayer.Main/SaveMails, Pet.Main/SavePetToDB, Pet.Main/_SaveAuras, Player.Main/PlayerLogToDB, Player.Main/SaveNewPlayer, Player.Main/SaveToDB, Player.Main/_SaveAuras, Player.Main/_SaveBGData, Player.Main/_SaveInventory, Player.Main/_SaveQuestStatus, Player.Main/_SaveSpellCooldowns, Player.Main/_SaveStats, WardenMac/Update, WardenWin/Update, World/LogMoneyTrade, World/LogTransaction, WorldSession.GMTicketHandler/HandleGMSurveySubmitOpcode, WorldSession.Main/SaveTutorialsData | — |
| addInt32 | method | — | Creature.Main/LogDeath, Creature.Main/LogLongCombat, game_Objects_Item/SaveToDB, GMTicketMgr/SaveToDB, Pet.Main/SavePetToDB, Pet.Main/_SaveAuras, Player.Main/SaveToDB, Player.Main/_SaveAuras, Player.Main/_SaveStats | — |
| addUInt64 | method | — | GMTicketMgr/SaveToDB, MasterPlayer.Main/SaveMails, Pet.Main/SavePetToDB, Pet.Main/_SaveAuras, Player.Main/SaveNewPlayer, Player.Main/SaveToDB, Player.Main/_SaveAuras, Player.Main/_SaveQuestStatus, Player.Main/_SaveSpellCooldowns | — |
| addInt64 | method | — | — | — |
| addFloat | method | — | GMTicketMgr/SaveToDB, Pet.Main/_SaveAuras, Player.Main/PlayerLogToDB, Player.Main/SaveNewPlayer, Player.Main/SaveToDB, Player.Main/_SaveAuras, Player.Main/_SaveBGData, Player.Main/_SaveStats | — |
| addDouble | method | — | — | — |
| addString#3 | method | — | Creature.Main/LogDeath, Creature.Main/LogLongCombat, Player.Main/PlayerLogToDB, World/LogMoneyTrade, World/LogTransaction | — |
| addString#2 | method | — | Creature.Main/LogDeath, Creature.Main/LogLongCombat, game_Objects_Item/SaveToDB, GMTicketMgr/SaveToDB, Pet.Main/SavePetToDB, Player.Main/PlayerLogToDB, Player.Main/SaveNewPlayer, Player.Main/SaveToDB, WardenMac/Update, WardenWin/Update, World/LogTransaction, WorldSession.GMTicketHandler/HandleGMSurveySubmitOpcode | — |
| addString | method | — | Pet.Main/SavePetToDB, Player.Main/SaveNewPlayer, Player.Main/SaveToDB | — |
| SqlStatement#2 | ctor | — | Database/CreateStatement | — |
| get | method | — | — | — |
| detach | method | — | SqlPreparedStatement/DirectExecute, SqlPreparedStatement/Execute#2 | — |
