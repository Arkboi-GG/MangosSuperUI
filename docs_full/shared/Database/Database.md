# Database

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Database & SqlConnection

## Purpose & Responsibilities

The `Database` and `SqlConnection` classes constitute the foundational abstraction layer for all MySQL database interactions within the wowvmangos server. They decouple the game logic from the underlying database driver, providing a unified interface for executing synchronous and asynchronous SQL queries, managing connection pools, handling transactions, and supporting prepared statements.

Key responsibilities include:
- **Connection Management**: Maintaining a pool of synchronous connections (`m_pQueryConnections`) for immediate, blocking queries and a dedicated asynchronous connection (`m_pAsyncConn`) for deferred operations.
- **Asynchronous Execution**: Offloading non-critical or batched SQL operations to worker threads (`SqlDelayThread`) to prevent blocking the main game loop and network threads.
- **Transaction Support**: Providing mechanisms to group multiple SQL statements into atomic transactions, with support for both synchronous and asynchronous commit/rollback.
- **Prepared Statements**: Caching and reusing prepared SQL statements to improve performance for frequently executed queries with varying parameters.
- **Migration Verification**: Checking that the database schema matches expected migration states during startup by querying the `migrations` table.
- **SQL Logging**: Optionally logging executed SQL commands to date-stamped files for debugging and auditing purposes.

## Member-by-Member Behavior

### Connection Initialization & Lifecycle

**`SqlConnection::Initialize`** parses a semicolon-delimited connection string containing host, port/socket, user, password, and database name. It normalizes localhost references (treating "." as localhost with socket usage) and delegates to `OpenConnection` to establish the physical link.

**`Database::Initialize`** sets up the entire database subsystem. It reads configuration values for SQL logging, log directory, and ping intervals from `Config`. It creates a pool of synchronous connections (bounded between 1 and 16) and initializes a single asynchronous connection. It then spawns worker threads via `InitDelayThread`, each with its own connection, to handle asynchronous query processing.

**`Database::StopServer`** gracefully shuts down the database subsystem. It halts all delay threads, cancels pending operations in the result queue via `SqlOperations/CancelAll`, and deletes all connections (both synchronous pool and asynchronous).

**`Database::~Database`** ensures cleanup by calling `StopServer`.

### Synchronous Query Execution

**`Database::Query`** (inline in header) selects a connection from the synchronous pool using round-robin distribution via `getQueryConnection`, locks it, and executes the SQL string immediately. This is the primary path for queries that require immediate results.

**`Database::PQuery`** and **`Database::PQueryNamed`** are variadic wrappers that format SQL strings using `vsnprintf` before delegating to `Query` or `QueryNamed`. They enforce a maximum query length (`MAX_QUERY_LEN`) and log errors via `Log.Main/Out` if truncation occurs.

**`Database::DirectExecute`** executes a SQL string immediately on the asynchronous connection, bypassing the async queue. This is used when synchronous behavior is required but the async connection is preferred.

**`Database::Execute`** determines whether to execute synchronously or asynchronously. If a transaction is active, it adds the request to the transaction's delay queue via `SqlTransaction/DelayExecute`. Otherwise, if async transactions are disabled or the mode requires sync execution, it calls `DirectExecute`. Otherwise, it adds the operation to the async delay queue via `AddToDelayQueue`.

**`Database::PExecute`** and **`Database::DirectPExecute`** are variadic wrappers for `Execute` and `DirectExecute`, respectively.

**`Database::PExecute#2`** is an overloaded variadic wrapper that accepts a `DbExecMode` parameter. It formats the SQL string using `vsnprintf`, checks for truncation (logging an error via `Log.Main/Out` if truncated), and delegates to `Execute(DbExecMode, char const*)`. This allows callers to explicitly specify whether the query *must* be synchronous or *can* be asynchronous.

### Asynchronous Query Processing

**`Database::InitDelayThread`** creates a new worker thread with its own database connection. It wraps the thread body in a `SqlDelayThread` object and stores references to both the thread and the body for later management. It uses `CreateThread/CreateThread` to spawn the OS thread.

**`Database::HaltDelayThread`** stops all worker threads by signaling them to stop via `SqlDelayThread/Stop` and joining them. It clears the stored references and resets the worker count.

**`Database::AddToDelayQueue`** adds an operation to the main async queue. **`Database::NextDelayedOperation`** retrieves the next operation from this queue.

**`Database::AddToSerialDelayQueue`** handles operations that must be executed in sequence (identified by a serial ID). It distributes these operations across worker threads based on the serial ID modulo the number of workers, ensuring that operations with the same serial ID go to the same worker. It calls `SqlDelayThread/addSerialOperation`.

**`Database::HasAsyncQuery`** checks if there are any pending operations in the main delay queue or in any of the worker threads' serial queues via `SqlDelayThread/HasAsyncQuery`.

### Transaction Management

**`Database::BeginTransaction`** creates a new `SqlTransaction` object and stores it in thread-local storage (`m_currentTransaction`). It asserts that no transaction is already active, preventing nested transactions.

**`Database::InTransaction`** returns whether a transaction is currently active for the calling thread.

**`Database::GetTransactionSerialId`** retrieves the serial ID of the current transaction, if any, by calling `SqlOperation/GetSerialId`.

**`Database::CommitTransaction`** finalizes the current transaction. If async transactions are disabled, it commits directly. Otherwise, it releases the transaction object and adds it to either the serial delay queue (if it has a serial ID) or the main delay queue.

**`Database::CommitTransactionDirect`** executes the current transaction immediately on the async connection, bypassing the async queue. It uses `SqlOperations/Execute#6` internally.

**`Database::RollbackTransaction`** discards the current transaction by resetting the thread-local pointer.

### Prepared Statements

**`Database::CreateStatement`** registers a SQL format string in the prepared statement registry. It counts the number of parameter placeholders ("?") and assigns a unique ID. It returns a `SqlStatement` object that can be used to execute the statement with specific parameters.

**`Database::GetStmtString`** retrieves the original SQL format string for a given statement ID, iterating through the registry to find the match.

**`SqlConnection::GetStmt`** retrieves or creates a prepared statement object for a given index. It lazily initializes the statement by fetching the format string from the database object, creating a `SqlPlainPreparedStatement` via `CreateStatement#2`, and preparing it via `SqlPreparedStatement/prepare`. If preparation fails, it logs an error via `Log.Main/Out` and returns null.

**`SqlConnection::ExecuteStmt`** binds parameters to a prepared statement via `SqlPreparedStatement/bind#2` and executes it via `SqlPreparedStatement/execute#3`.

**`Database::ExecuteStmt`** and **`Database::DirectExecuteStmt`** handle prepared statement execution, routing through transactions or direct execution similar to regular SQL statements. `DirectExecuteStmt` acquires a lock on the async connection and calls `SqlConnection::ExecuteStmt`.

### Utility & Maintenance

**`Database::escape_string`** escapes special characters in a string for safe inclusion in SQL queries. It uses the first synchronous connection to perform the escaping, assuming consistency across connections.

**`Database::Ping`** sends a simple "SELECT 1" query to all connections (async and sync pool) to verify they are still alive. It acquires locks on each connection before querying.

**`Database::ProcessResultQueue`** updates the result queue, processing any completed asynchronous operations via `SqlOperations/Update`.

**`Database::CheckRequiredMigrations`** verifies that the database has all required migrations applied. It queries the `migrations` table, compares the applied migrations against a provided list, and logs any missing or extra migrations via `Log.Main/Out`.

**`Database::PExecuteLog`** logs SQL commands to a file if SQL logging is enabled. It formats the command, writes it to a date-stamped file, and then executes it normally via `Execute`.

## Cross-Unit Boundaries

The `Database` class interacts extensively with other units:

- **`SqlDelayThread`**: Worker threads that process asynchronous queries. `Database` creates, manages, and communicates with these threads via `InitDelayThread`, `HaltDelayThread`, `AddToDelayQueue`, and `AddToSerialDelayQueue`.
- **`SqlOperations`**: Provides the underlying mechanism for executing SQL operations. `Database` uses `SqlPlainRequest` and `SqlPreparedRequest` to wrap SQL statements for async execution. `SqlOperations/Execute#3` is called by `Query#2`, `SqlOperations/Execute#5` is called by `Query#2`, `SqlOperations/Execute#6` is called by `CommitTransactionDirect`, `SqlOperations/Update` is called by `ProcessResultQueue`, `SqlOperations/CancelAll` is called by `StopServer`, and `SqlOperations/SqlResultQueue` is instantiated in `Initialize`.
- **`SqlPreparedStatement`**: Represents a prepared SQL statement. `Database` creates and manages these objects via `CreateStatement` and `GetStmt`. `SqlPreparedStatement/prepare` is called by `GetStmt`, `SqlPreparedStatement/bind#2` and `SqlPreparedStatement/execute#3` are called by `ExecuteStmt#2`, `SqlPreparedStatement/execute` is called by `Execute#3`, and `SqlPreparedStatement/DataToString` calls `DB`.
- **`SqlTransaction`**: Represents a group of SQL statements that should be executed atomically. `Database` creates and manages transactions via `BeginTransaction`, `CommitTransaction`, and `RollbackTransaction`. `SqlTransaction/SqlTransaction` is called by `BeginTransaction`, `SqlTransaction/DelayExecute` is called by `Execute` and `ExecuteStmt`, and `SqlOperation/GetSerialId` is called by `GetTransactionSerialId` and `CommitTransaction`.
- **`Config`**: Provides configuration values for SQL logging, log directory, and ping intervals via `Config/GetBoolDefault`, `Config/GetIntDefault`, and `Config/GetStringDefault`.
- **`Log`**: Used for logging errors and warnings related to database operations via `Log.Main/Out`.
- **`Errors`**: Provides exception handling utilities via `Errors/PrintStacktraceAndThrow`, called by `GetStmt`, `BeginTransaction`, and `DirectExecuteStmt`.
- **`shared_Util`**: Provides string splitting functionality via `shared_Util/StrSplit`, used in `Initialize#2`.
- **`IO/Multithreading/CreateThread`**: Used to create worker threads via `CreateThread/CreateThread`.

Numerous other units call into `Database` to execute queries, including `AccountMgr`, `ObjectMgr`, `Player`, `GuildMgr`, `ChatHandler`, and many others. These calls typically use `Query`, `PQuery`, `Execute`, `PExecute`, or prepared statement APIs.

## Data Model

The `Database` class interacts with the `migrations` table to verify schema integrity. This table contains a single column `id` (varchar(255), primary key) that stores the identifiers of applied migrations.

Other database tables are accessed indirectly through SQL queries executed by various units, but `Database` itself does not define or manage these tables.

## Notable Implementation Details

- **Round-Robin Connection Selection**: `getQueryConnection` uses a simple counter to distribute synchronous queries across the connection pool. The counter wraps around at `1 << 31` to avoid overflow.
- **Lazy Statement Preparation**: `SqlConnection::GetStmt` lazily prepares statements on first use, caching them in `m_holder`. This reduces startup time but may cause delays on first execution.
- **No Nested Transactions**: `BeginTransaction` asserts that no transaction is already active, preventing nested transactions. This simplifies transaction management but limits flexibility.
- **Async Transaction Distribution**: `AddToSerialDelayQueue` distributes serial operations across worker threads based on the serial ID modulo the number of workers. This ensures ordering within a serial ID but does not balance load across workers.
- **SQL Truncation Handling**: Variadic query functions check for truncation and log errors if the formatted query exceeds `MAX_QUERY_LEN`. This prevents silent data corruption but may lead to unexpected failures.
- **Thread-Local Transactions**: `m_currentTransaction` is thread-local, allowing each thread to have its own active transaction. This avoids locking but requires careful management to ensure transactions are committed or rolled back before the thread ends.

## Member Reference

**`CreateStatement#2`** Creates a new `SqlPlainPreparedStatement` object with the given format string.

**`FreePreparedStatements`** Deletes all cached prepared statements and clears the holder vector.

**`~SqlConnection`** Virtual destructor, default implementation.

**`OpenConnection`** Pure virtual method to open the database connection.

**`GetStmt`** Retrieves or creates a prepared statement for the given index, caching it for future use.

**`Query#2`** Pure virtual method to execute a SQL query and return results.

**`QueryNamed#2`** Pure virtual method to execute a SQL query and return named results.

**`Execute#3`** Pure virtual method to execute a SQL statement without returning results.

**`escape_string#2`** Escapes special characters in a string for safe SQL inclusion.

**`BeginTransaction#2`** Begins a database transaction.

**`CommitTransaction#2`** Commits the current database transaction.

**`RollbackTransaction#2`** Rolls back the current database transaction.

**`DatabaseName`** Returns the name of the connected database.

**`DB`** Returns a reference to the parent `Database` object.

**`Initialize#2`** Parses the connection string and initializes connection parameters.

**`SqlConnection`** Constructor, protected, takes a reference to the parent `Database` object.

**`ExecuteStmt#2`** Binds parameters to a prepared statement and executes it.

**`Query`** Selects a connection from the sync pool and executes a SQL query.

**`~Database`** Destructor, calls `StopServer` to clean up resources.

**`QueryNamed`** Selects a connection from the sync pool and executes a SQL query with named results.

**`Initialize`** Sets up the database subsystem, including connection pools and worker threads.

**`DirectExecute`** Executes a SQL string immediately on the async connection.

**`StopServer`** Shuts down the database subsystem, stopping threads and closing connections.

**`InitDelayThread`** Creates a new worker thread with its own database connection.

**`HaltDelayThread`** Stops all worker threads and cleans up resources.

**`ThreadStart`** Empty placeholder for thread-specific initialization.

**`ThreadEnd`** Empty placeholder for thread-specific cleanup.

**`GetPingIntervalMs`** Returns the configured ping interval in milliseconds.

**`ProcessResultQueue`** Updates the result queue, processing completed async operations.

**`escape_string`** Escapes special characters in a string for safe SQL inclusion.

**`AllowAsyncTransactions`** Enables asynchronous transaction processing.

**`AddToDelayQueue`** Adds an operation to the main async queue.

**`NextDelayedOperation`** Retrieves the next operation from the main async queue.

**`AddToSerialDelayQueue#2`** Adds an operation to a worker's serial queue, ensuring order.

**`NextSerialDelayedOperation`** Declared but not defined in this unit.

**`getQueryConnection`** Selects a connection from the sync pool using round-robin distribution.

**`Database`** Constructor, initializes member variables.

**`CreateConnection`** Pure virtual factory method to create `SqlConnection` objects.

**`Ping`** Sends a simple query to all connections to verify they are alive.

**`getAsyncConnection`** Returns the async connection pointer.

**`PExecuteLog`** Logs a SQL command to a file and then executes it.

**`PQuery`** Formats and executes a SQL query, returning results.

**`PQueryNamed`** Formats and executes a SQL query, returning named results.

**`Execute#2`** Determines whether to execute synchronously or asynchronously.

**`Execute`** Executes a SQL string, routing through transactions or async queue.

**`PExecute`** Formats and executes a SQL statement.

**`DirectPExecute`** Formats and executes a SQL statement immediately on the async connection.

**`BeginTransaction`** Creates a new transaction and stores it in thread-local storage.

**`InTransaction`** Checks if a transaction is currently active.

**`GetTransactionSerialId`** Retrieves the serial ID of the current transaction.

**`CommitTransaction`** Finalizes the current transaction, routing to async queue if enabled.

**`CommitTransactionDirect`** Executes the current transaction immediately on the async connection.

**`RollbackTransaction`** Discards the current transaction.

**`AddToSerialDelayQueue`** Distributes serial operations across worker threads.

**`HasAsyncQuery`** Checks if there are any pending async operations.

**`CheckRequiredMigrations`** Verifies that the database has all required migrations applied.

**`ExecuteStmt`** Handles prepared statement execution, routing through transactions or async queue.

**`DirectExecuteStmt`** Executes a prepared statement immediately on the async connection.

**`CreateStatement`** Registers a SQL format string in the prepared statement registry.

**`GetStmtString`** Retrieves the original SQL format string for a given statement ID.

**`PExecute#2`** Formats a SQL string with explicit execution mode (`DbExecMode`) and delegates to `Execute(DbExecMode, char const*)`.

---

<!-- machine-true, projected from graph.json -->

## Map — Database

*Source:* Database.cpp, Database.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| CreateStatement#2 | method | SqlPreparedStatement/SqlPlainPreparedStatement | — | — |
| FreePreparedStatements | method | Lock/Lock#5 | DatabaseMysql/Reconnect, DatabaseMysql/~MySQLConnection | — |
| ~SqlConnection | dtor | — | — | — |
| OpenConnection | decl | — | — | — |
| GetStmt | method | Errors/PrintStacktraceAndThrow, Log.Main/Out, SqlPreparedStatement/prepare | — | — |
| Query#2 | decl | — | SqlDelayThread/run, SqlOperations/Execute#3, SqlOperations/Execute#5 | — |
| QueryNamed#2 | decl | — | — | — |
| Execute#3 | decl | — | SqlOperations/Execute, SqlPreparedStatement/execute | — |
| escape_string#2 | method | — | — | — |
| BeginTransaction#2 | method | — | SqlOperations/Execute#6 | — |
| CommitTransaction#2 | method | — | SqlOperations/Execute#6 | — |
| RollbackTransaction#2 | method | — | SqlOperations/Execute#6 | — |
| DatabaseName | method | — | — | — |
| DB | method | — | SqlPreparedStatement/DataToString | — |
| Initialize#2 | method | shared_Util/StrSplit | — | — |
| SqlConnection | ctor | — | — | — |
| ExecuteStmt#2 | method | SqlPreparedStatement/bind#2, SqlPreparedStatement/execute#3 | SqlOperations/Execute#2 | — |
| Query | method | — | AccountMgr/Load, AccountMgr/LoadAccountWarnings, AuctionHouseMgr/LoadAuctionItems, AuctionHouseMgr/LoadAuctions, AuraRemovalMgr/LoadFromDB, AutoBroadCastMgr/Load, BattleGroundMgr/LoadBattleEventIndexes, BattleGroundMgr/LoadBattleMastersEntry, CharacterDatabaseCache/LoadCharacterPet, CharacterDatabaseCache/LoadPetAura, CharacterDatabaseCache/LoadPetSpell, CharacterDatabaseCache/LoadPetSpellCooldown, ChatHandler.AccountCommands/HandleBanListAccountCommand, ChatHandler.AccountCommands/HandleBanListIPCommand, ChatHandler.AuctionHouseBotMgr/Load, ChatHandler.CharacterCommands/GetDeletedCharacterInfoList, ChatHandler.CharacterCommands/HandleCharacterPremadeSaveGearCommand, ChatHandler.CharacterCommands/HandleCharacterPremadeSaveSpecCommand, ChatHandler.CharacterCommands/HandleCleanCharactersItemsCommand, ChatHandler.CharacterCommands/HandleCleanCharactersToDeleteCommand, ChatHandler.CharacterCommands/HandleServiceDeleteCharacters, ChatHandler.Chat/getCommandTable, ChatHandler.Chat/LoadRbacPermissions, ChatHandler.ServerCommands/HandleReloadIPBanList, CreatureEventAIMgr/LoadCreatureEventAI_Events, CreatureGroups/Load, CreatureLinkingMgr/LoadFromDB, GameEventMgr.Main/Initialize, GameEventMgr.Main/LoadFromDB, GMTicketMgr/LoadSurveys, GMTicketMgr/LoadTickets, GuildMgr/LoadGuilds, GuildMgr/LoadPetitions, HonorMgr/Initialize, HonorMgr/LoadWeeklyScores, InstanceStatistics/LoadFromDB, MapManager/InitMaxInstanceId, MapPersistentStateMgr/LoadCreatureRespawnTimes, MapPersistentStateMgr/LoadGameobjectRespawnTimes, MapPersistentStateMgr/LoadResetTimes, MapPersistentStateMgr/PackInstances, MapPersistentStateMgr/ScheduleAllDungeonResets, ObjectMgr/FillObtainedItemsList, ObjectMgr/LoadAllIdentifiers, ObjectMgr/LoadAreaLocales, ObjectMgr/LoadAreaTriggerLocales, ObjectMgr/LoadBattlegroundEntranceTriggers, ObjectMgr/LoadBroadcastTextLocales, ObjectMgr/LoadBroadcastTexts, ObjectMgr/LoadCinematicsWaypoints, ObjectMgr/LoadCorpses, ObjectMgr/LoadCreatureClassLevelStats, ObjectMgr/LoadCreatureLocales, ObjectMgr/LoadCreatures, ObjectMgr/LoadCreatureSpells, ObjectMgr/LoadExplorationBaseXP, ObjectMgr/LoadFactionChangeItems, ObjectMgr/LoadFactionChangeMounts, ObjectMgr/LoadFactionChangeQuests, ObjectMgr/LoadFactionChangeReputations, ObjectMgr/LoadFactionChangeSpells, ObjectMgr/LoadFactions, ObjectMgr/LoadFishingBaseSkillLevel, ObjectMgr/LoadGameObjectLocales, ObjectMgr/LoadGameobjects, ObjectMgr/LoadGameobjectsRequirements, ObjectMgr/LoadGameTele, ObjectMgr/LoadGossipMenu, ObjectMgr/LoadGossipMenuItems, ObjectMgr/LoadGossipMenuItemsLocales, ObjectMgr/LoadGroups, ObjectMgr/LoadItemLocales, ObjectMgr/LoadItemRequiredTarget, ObjectMgr/LoadItemTexts, ObjectMgr/LoadMapLootDisabled, ObjectMgr/LoadNpcGossips, ObjectMgr/LoadNPCText, ObjectMgr/LoadPageTextLocales, ObjectMgr/LoadPetLevelInfo, ObjectMgr/LoadPetNames, ObjectMgr/LoadPlayerCacheData, ObjectMgr/LoadPlayerInfo, ObjectMgr/LoadPlayerPhaseFromDb, ObjectMgr/LoadPlayerPremadeTemplates, ObjectMgr/LoadPointOfInterestLocales, ObjectMgr/LoadPointsOfInterest, ObjectMgr/LoadQuestAreaTriggers, ObjectMgr/LoadQuestGreetings, ObjectMgr/LoadQuestLocales, ObjectMgr/LoadReputationRewardRate, ObjectMgr/LoadReputationSpilloverTemplate, ObjectMgr/LoadReservedPlayersNames, ObjectMgr/LoadSavedVariable, ObjectMgr/LoadSoundEntries, ObjectMgr/LoadSpellDisabledEntrys, ObjectMgr/LoadTaxiNodes, ObjectMgr/LoadTrainerGreetings, ObjectMgr/LoadTrainerTemplates, ObjectMgr/LoadVendorTemplates, ObjectMgr/LoadWorldSafeLocsFacing, ObjectMgr/PackGroupIds, ObjectMgr/RestoreDeletedItems, ObjectMgr/SetHighestGuids, PoolManager/LoadFromDB, realmd_Main/main, RealmList/LoadAllowedClients, RealmList/UpdateRealms, ScriptMgr/CollectPossibleEventIds, ScriptMgr/LoadAreaTriggerScripts, ScriptMgr/LoadCreatureEventAIScripts, ScriptMgr/LoadEventIdScripts, ScriptMgr/LoadQuestEndScripts, ScriptMgr/LoadQuestStartScripts, ScriptMgr/LoadSpellScripts, SpellMgr/LoadExistingSpellIds, SpellMgr/LoadSpellAreas, SpellMgr/LoadSpellCones, SpellMgr/LoadSpellEnchantCharges, SpellMgr/LoadSpellPetAuras, SpellMgr/LoadSpellProcItemEnchant, SpellMgr/LoadSpells, SpellMgr/LoadSpellScriptTarget, SpellModMgr/LoadSpellMods, WardenScanMgr/LoadFromDB, WaypointManager/Cleanup, WaypointManager/Load, Weather/LoadWeatherZoneChances | — |
| ~Database | dtor | — | — | — |
| QueryNamed | method | — | — | — |
| Initialize | method | Config/GetBoolDefault, Config/GetIntDefault, Config/GetStringDefault, SqlOperations/SqlResultQueue | Master/StartDB, realmd_Main/StartDB | — |
| DirectExecute | method | — | HonorMgr/FlushRankPoints, MapPersistentStateMgr/LoadCreatureRespawnTimes, MapPersistentStateMgr/LoadGameobjectRespawnTimes, WaypointManager/Cleanup | — |
| StopServer | method | SqlOperations/CancelAll | DatabaseMysql/~DatabaseMysql, Master/Run | — |
| InitDelayThread | method | CreateThread/CreateThread, SqlDelayThread/run | — | — |
| HaltDelayThread | method | SqlDelayThread/Stop | Master/_StartDB, realmd_Main/main, realmd_Main/StartDB | — |
| ThreadStart | method | — | — | — |
| ThreadEnd | method | — | — | — |
| GetPingIntervalMs | method | — | SqlDelayThread/run | — |
| ProcessResultQueue | method | SqlOperations/Update | World/UpdateResultQueue | — |
| escape_string | method | — | AccountMgr/ChangeUsername, AccountMgr/GetId, AuthSocket/_HandleLogonChallenge, AuthSocket/_HandleLogonProof__PostRecv, AuthSocket/_HandleReconnectChallenge, ChatHandler.AccountCommands/HandleBanAllIPCommand, ChatHandler.AccountCommands/HandleBanInfoIPCommand, ChatHandler.AccountCommands/HandleBanListAccountCommand, ChatHandler.AccountCommands/HandleBanListCharacterCommand, ChatHandler.AccountCommands/HandleBanListIPCommand, ChatHandler.CharacterCommands/GetDeletedCharacterInfoList, ChatHandler.CharacterCommands/HandleAddItemCommand, ChatHandler.CharacterCommands/HandleCharacterCopySkinCommand, ChatHandler.CharacterCommands/HandleDeleteItemCommand, ChatHandler.CharacterCommands/HandleGoldRemoval, ChatHandler.CharacterCommands/HandlePetRenameCommand, ChatHandler.LookupCommands/HandleLookupAccountEmailCommand, ChatHandler.LookupCommands/HandleLookupAccountNameCommand, ChatHandler.LookupCommands/HandleLookupPlayerAccountCommand, ChatHandler.LookupCommands/HandleLookupPlayerCharacterCommand, ChatHandler.LookupCommands/HandleLookupPlayerEmailCommand, ChatHandler.LookupCommands/HandleLookupPlayerIpCommand, ChatHandler.LookupCommands/HandleLookupPlayerNameCommand, ChatHandler.LookupCommands/ShowAccountIpListHelper, ChatHandler.ObjectCommands/HandleGameObjectTargetCommand, ChatHandler.TeleportCommands/HandleGoCreatureCommand, ChatHandler.TeleportCommands/HandleGoObjectCommand, game_Guild_Guild/AddMember, game_Guild_Guild/Create#2, game_Guild_Guild/CreateRank, game_Guild_Guild/LoadRanksFromDB, game_Guild_Guild/Rename, game_Guild_Guild/SetGINFO, game_Guild_Guild/SetMOTD, game_Guild_Guild/SetOFFNOTE, game_Guild_Guild/SetPNOTE, game_Guild_Guild/SetRankName, game_Mail_Mail/SendMailTo, GuildMgr/Rename, GuildMgr/SaveToDB, InstanceData/SaveToDB, MapPersistentStateMgr/SaveToDB, MapPersistentStateMgr/_DelHelper, Master/Run, ObjectMgr/CreateItemText, PlayerDump/CreateDumpString, PlayerDump/LoadDump, SqlPreparedStatement/DataToString, World/BanAccount, World/BanAccount#2, World/RemoveBanAccount, World/WarnAccount, WorldSession.CharacterHandler/HandleCharRenameOpcode, WorldSession.Main/SetAccountData, WorldSession.PetHandler/HandlePetRename, WorldSocket/_HandleAuthSession | — |
| AllowAsyncTransactions | method | — | Master/Run, realmd_Main/main | — |
| AddToDelayQueue | method | — | — | — |
| NextDelayedOperation | method | — | SqlDelayThread/ProcessRequests | — |
| AddToSerialDelayQueue#2 | method | — | — | — |
| NextSerialDelayedOperation | decl | — | — | — |
| getQueryConnection | method | — | — | — |
| Database | ctor | — | — | — |
| CreateConnection | decl | — | — | — |
| Ping | method | Lock/Lock#5, Lock/operator-> | realmd_Main/main, SqlDelayThread/run | — |
| getAsyncConnection | method | — | — | — |
| PExecuteLog | method | Log.Main/Out | ChatHandler.CreatureCommands/HandleEscortHideWpCommand, ChatHandler.CreatureCommands/HandleNpcMoveHelperCommand, ChatHandler.CreatureCommands/HandleNpcSpawnSetAurasCommand, ChatHandler.CreatureCommands/HandleNpcSpawnSetDisplayIdCommand, ChatHandler.CreatureCommands/HandleNpcSpawnSetEmoteStateCommand, ChatHandler.CreatureCommands/HandleNpcSpawnSetEntryCommand, ChatHandler.CreatureCommands/HandleNpcSpawnSetMoveTypeCommand, ChatHandler.CreatureCommands/HandleNpcSpawnSetRespawnTimeCommand, ChatHandler.CreatureCommands/HandleNpcSpawnSetSheathStateCommand, ChatHandler.CreatureCommands/HandleNpcSpawnSetStandStateCommand, ChatHandler.CreatureCommands/HandleNpcSpawnWanderDistCommand, Creature.Main/DeleteFromDB#2, Creature.Main/SaveToDB#2, GameObject/DeleteFromDB, GameObject/SaveToDB#2, ObjectMgr/AddGameTele, ObjectMgr/AddGraveYardLink, ObjectMgr/AddVendorItem, ObjectMgr/DeleteGameTele, ObjectMgr/RemoveVendorItem, WaypointManager/AddNode, WaypointManager/DeleteNode, WaypointManager/DeletePath, WaypointManager/SetNodeOrientation, WaypointManager/SetNodePosition, WaypointManager/SetNodeScriptId, WaypointManager/SetNodeWaittime | — |
| PQuery | method | Log.Main/Out | AccountMgr/ChangeUsername, AccountMgr/CheckPassword, AccountMgr/DeleteAccount, AccountMgr/GetCharactersCount, AccountMgr/GetId, AccountMgr/GetName, AccountMgr/LoadAccountBanList, AccountMgr/LoadAccountData, AiBotAI.Main/OnSessionLoaded, AuthSocket/GeographicalLockCheck, AuthSocket/LoadAccountSecurityLevels, AuthSocket/LoadRealmlistAndWriteIntoBuffer, AuthSocket/_HandleLogonChallenge, AuthSocket/_HandleLogonProof__PostRecv, AuthSocket/_HandleReconnectChallenge, BattleGroundMgr/CreateInitialBattleGrounds, CharacterDatabaseCache/LoadCharacterPet, CharacterDatabaseCache/LoadPetAura, CharacterDatabaseCache/LoadPetSpell, CharacterDatabaseCache/LoadPetSpellCooldown, CharacterDatabaseCleaner/CheckUnique, CharacterDatabaseCleaner/CleanDatabase, ChatHandler.AccountCommands/HandleAccountOnlineListCommand, ChatHandler.AccountCommands/HandleBanAllIPCommand, ChatHandler.AccountCommands/HandleBanInfoHelper, ChatHandler.AccountCommands/HandleBanInfoIPCommand, ChatHandler.AccountCommands/HandleBanListAccountCommand, ChatHandler.AccountCommands/HandleBanListCharacterCommand, ChatHandler.AccountCommands/HandleBanListHelper, ChatHandler.AccountCommands/HandleBanListIPCommand, ChatHandler.CharacterCommands/GetDeletedCharacterInfoList, ChatHandler.CharacterCommands/HandleAddItemCommand, ChatHandler.CharacterCommands/HandleCharacterCopySkinCommand, ChatHandler.CharacterCommands/HandleCharacterHasItemCommand, ChatHandler.CharacterCommands/HandleCharacterPremadeSaveSpecCommand, ChatHandler.CharacterCommands/HandleDeleteItemCommand, ChatHandler.CharacterCommands/HandleListItemCommand, ChatHandler.CharacterCommands/HandlePetRenameCommand, ChatHandler.CharacterCommands/HandleQuestStatusCommandHelper, ChatHandler.CharacterCommands/HandleRemoveRidingCommand, ChatHandler.CreatureCommands/HandleEscortAddWpCommand, ChatHandler.CreatureCommands/HandleEscortHideWpCommand, ChatHandler.CreatureCommands/HandleWpAddCommand, ChatHandler.LookupCommands/HandleListCreatureCommand, ChatHandler.LookupCommands/HandleListObjectCommand, ChatHandler.LookupCommands/HandleLookupPlayerAccountCommand, ChatHandler.LookupCommands/HandleLookupPlayerCharacterCommand, ChatHandler.LookupCommands/HandleLookupPlayerEmailCommand, ChatHandler.LookupCommands/HandleLookupPlayerIpCommand, ChatHandler.MiscCommands/HandleGMListFullCommand, ChatHandler.ObjectCommands/HandleGameObjectNearCommand, ChatHandler.ObjectCommands/HandleGameObjectTargetCommand, ChatHandler.PlayerBotMgr/Load, ChatHandler.TeleportCommands/HandleGoCreatureCommand, ChatHandler.TeleportCommands/HandleGoObjectCommand, CreatureLinkingMgr/IsLinkingEntryValid, GameEventMgr.Main/LoadFromDB, game_Guild_Guild/LoadGuildEventLogFromDB, HonorMgr/SetCityRanks, ItemEnchantmentMgr/LoadRandomEnchantmentsTable, LootMgr/LoadLootTable, Map.Main/CreateInstanceData, MapPersistentStateMgr/_DelHelper, Master/Run, ObjectGuid/LoadFromDB, ObjectMgr/GetPlayerClassByGUID, ObjectMgr/LoadAreaTriggers, ObjectMgr/LoadAreaTriggerTeleports, ObjectMgr/LoadCreatureClassLevelStats, ObjectMgr/LoadCreatureTemplate, ObjectMgr/LoadCreatureTemplates, ObjectMgr/LoadEquipmentTemplates, ObjectMgr/LoadFactions, ObjectMgr/LoadGameObjectTemplate, ObjectMgr/LoadGameObjectTemplates, ObjectMgr/LoadGraveyardZones, ObjectMgr/LoadItemPrototypes, ObjectMgr/LoadMangosStrings#2, ObjectMgr/LoadPetCreateSpells, ObjectMgr/LoadPlayerCacheData, ObjectMgr/LoadPlayerInfo, ObjectMgr/LoadQuestRelationsHelper, ObjectMgr/LoadQuests, ObjectMgr/LoadReputationOnKill, ObjectMgr/LoadSkillLineAbility, ObjectMgr/LoadTavernAreaTriggers, ObjectMgr/LoadTaxiNodes, ObjectMgr/LoadTaxiPathTransitions, ObjectMgr/LoadTrainers#2, ObjectMgr/LoadVendors#2, Player.Main/DeleteFromDB, Player.Main/DeleteOldCharacters#2, Player.Main/GetGuildIdFromDB, Player.Main/GetLevelFromDB, Player.Main/GetRankFromDB, Player.Main/GetZoneIdFromDB, Player.Main/LoadPositionFromDB, PlayerDump/DumpTableContent, PlayerDump/LoadDump, PoolManager/LoadFromDB, ScriptMgr/CollectPossibleEventIds, ScriptMgr/CollectPossibleGenericIds, ScriptMgr/LoadCreatureEventAIScripts, ScriptMgr/LoadEscortData, ScriptMgr/LoadScriptNames, ScriptMgr/LoadScripts, ScriptMgr/LoadScriptTexts, ScriptMgr/LoadScriptTextsCustom, ScriptMgr/LoadScriptWaypoints, SpellMgr/CheckUsedSpells, SpellMgr/LoadSpellChains, SpellMgr/LoadSpellElixirs, SpellMgr/LoadSpellGroups, SpellMgr/LoadSpellGroupStackRules, SpellMgr/LoadSpellLearnSpells, SpellMgr/LoadSpellProcEvents, SpellMgr/LoadSpells, SpellMgr/LoadSpellScriptTarget, SpellMgr/LoadSpellTargetPositions, SpellMgr/LoadSpellThreats, TransportMgr/LoadTransportTemplates, WaypointManager/Load, WorldSession.Main/LoadGlobalAccountData, WorldSession.Main/LoadTutorialsData, WorldSession.MiscHandler/HandleWhoisOpcode, WorldSession.SpellHandler/HandleOpenItemOpcode, WorldSocket/_HandleAuthSession | — |
| PQueryNamed | method | Log.Main/Out | — | — |
| Execute#2 | method | — | AccountMgr/CreateAccount, CharacterDatabaseCleaner/CheckUnique, CharacterDatabaseCleaner/CleanDatabase, ChatHandler.AccountCommands/HandleBanListAccountCommand, ChatHandler.AccountCommands/HandleBanListCharacterCommand, ChatHandler.AccountCommands/HandleBanListIPCommand, Corpse/SaveToDB, GameEventMgr.Main/Initialize, GMTicketMgr/ResetTickets, HonorMgr/SetCityRanks, MapPersistentStateMgr/PackInstances, Master/clearOnlineAccounts, ObjectMgr/CreateItemText, Player.Main/SavePositionInDB, PlayerDump/LoadDump, realmd_Main/main, World/SetInitialWorldSettings | — |
| Execute | method | SqlOperations/SqlPlainRequest, SqlTransaction/DelayExecute | — | — |
| PExecute | method | Log.Main/Out | AuthSocket/_HandleLogonProof__PostRecv | — |
| PExecute#2 | method | Log.Main/Out | AccountMgr/ChangePassword, AccountMgr/ChangeUsername, AccountMgr/CreateAccount, AccountMgr/DeleteAccount, AccountMgr/SetSecurity, AiBotAI.Main/OnSessionLoaded, AsyncCommandHandlers/HandleGoldLookupResult, AuctionHouseMgr/DeleteFromDB, AuctionHouseMgr/SaveToDB, AuctionHouseMgr/SendAuctionExpiredMail, AuctionHouseMgr/SendAuctionWonMail, AuthSocket/_HandleLogonProof__PostRecv, ChatHandler.AccountCommands/HandleAccountClearDataCommand, ChatHandler.AccountCommands/HandleAccountLockCommand, ChatHandler.AccountCommands/HandleAccountSetAddonCommand, ChatHandler.AccountCommands/HandleAccountSetLockedCommand, ChatHandler.AccountCommands/HandleMuteCommand, ChatHandler.AccountCommands/HandleUnmuteCommand, ChatHandler.CharacterCommands/HandleCharacterLevel, ChatHandler.CharacterCommands/HandleCharacterPremadeSaveGearCommand, ChatHandler.CharacterCommands/HandleCharacterPremadeSaveSpecCommand, ChatHandler.CharacterCommands/HandleCharacterRenameCommand, ChatHandler.CharacterCommands/HandleDeleteItemCommand, ChatHandler.CharacterCommands/HandlePetDeleteCommand, ChatHandler.CharacterCommands/HandlePetRenameCommand, ChatHandler.CharacterCommands/HandleRemoveRidingCommand, ChatHandler.CharacterCommands/HandleResetAllCommand, ChatHandler.CharacterCommands/HandleResetTalentsCommand, ChatHandler.CreatureCommands/HandleEscortAddWpCommand, ChatHandler.CreatureCommands/HandleEscortClearWpCommand, ChatHandler.CreatureCommands/HandleEscortCreateCommand, ChatHandler.CreatureCommands/HandleEscortModifyWpCommand, ChatHandler.CreatureCommands/HandleNpcAddEntryCommand, ChatHandler.CreatureCommands/HandleNpcGroupLinkCommand, ChatHandler.DebugCommands/HandleSpellIconFixCommand, ChatHandler.HardcodedEvents/ResetThings, ChatHandler.MiscCommands/HandleCinematicAddWpCommand, ChatHandler.ServerCommands/HandleAntiSpamAdd, ChatHandler.ServerCommands/HandleAntiSpamRemove, ChatHandler.ServerCommands/HandleAntiSpamRemoveReplace, ChatHandler.ServerCommands/HandleAntiSpamReplace, ChatHandler.ServerCommands/HandleGroupAddSpellCommand, ChatHandler.ServerCommands/HandleGroupSetRuleCommand, CreatureGroups/DeleteFromDb, CreatureGroups/SaveToDb, GameEventMgr.Main/ApplyNewEvent, GameEventMgr.Main/EnableEvent, GameEventMgr.Main/UnApplyEvent, game_Group_Group/BindToInstance, game_Group_Group/ConvertToRaid, game_Group_Group/Create, game_Group_Group/Disband, game_Group_Group/ResetInstances, game_Group_Group/UnbindInstance, game_Group_Group/_addMember#2, game_Group_Group/_removeMember, game_Group_Group/_setAssistantFlag, game_Group_Group/_setLeader, game_Group_Group/_setMainAssistant, game_Group_Group/_setMainTank, game_Group_Group/_setMembersGroup, game_Group_Group/_swapMembersGroup, game_Guild_Guild/AddMember, game_Guild_Guild/ChangeRank, game_Guild_Guild/Create#2, game_Guild_Guild/CreateDefaultGuildRanks, game_Guild_Guild/CreateRank, game_Guild_Guild/DelMember, game_Guild_Guild/DelRank, game_Guild_Guild/Disband, game_Guild_Guild/LoadMembersFromDB, game_Guild_Guild/LoadRanksFromDB, game_Guild_Guild/LogGuildEvent, game_Guild_Guild/Rename, game_Guild_Guild/SetEmblem, game_Guild_Guild/SetGINFO, game_Guild_Guild/SetLeader, game_Guild_Guild/SetMOTD, game_Guild_Guild/SetOFFNOTE, game_Guild_Guild/SetPNOTE, game_Guild_Guild/SetRankName, game_Guild_Guild/SetRankRights, game_Mail_Mail/deleteIncludedItems, game_Mail_Mail/prepareTemplateItems, game_Mail_Mail/SendMailTo, game_Mail_Mail/SendReturnToSender, game_Objects_Item/DeleteAllFromDB#2, game_Objects_Item/LoadLootFromDB, GuildMgr/Delete, GuildMgr/LoadGuilds, GuildMgr/LoadPetitions, GuildMgr/Rename, GuildMgr/SaveToDB, GuildMgr/SaveToDB#2, HonorMgr/Reset, HonorMgr/Save, HonorMgr/SaveStoredData, HonorMgr/SetCityRanks, InstanceData/SaveToDB, InstanceStatistics/IncrementCustomCounter, InstanceStatistics/Save, InstanceStatistics/Save#2, Map.Main/CreateInstanceData, MapPersistentStateMgr/CleanupInstances, MapPersistentStateMgr/DeleteInstanceFromDB, MapPersistentStateMgr/DeleteRespawnTimesAndData, MapPersistentStateMgr/PackInstances, MapPersistentStateMgr/SaveToDB, MapPersistentStateMgr/_DelHelper, MapPersistentStateMgr/_ResetOrWarnAll, Master/clearOnlineAccounts, Master/Run, MasterPlayer.Main/LoadMailedItems, ObjectMgr/Callback, ObjectMgr/Callback#2, ObjectMgr/LoadGroups, ObjectMgr/PackGroupIds, ObjectMgr/RemoveGraveYardLink, ObjectMgr/RestoreDeletedItems, ObjectMgr/ReturnOrDeleteOldMails, ObjectMgr/SetHighestGuids, ObjectMgr/_SaveVariable, Pet.Main/AddSpell, Player.Main/AddSpell, Player.Main/BindToInstance, Player.Main/ConvertInstancesToGroup, Player.Main/DeleteFromDB, Player.Main/GetZoneIdFromDB, Player.Main/LoadFromDB, Player.Main/RemovePetitionsAndSigns, Player.Main/SetGMVisible, Player.Main/SetHomebindToLocation, Player.Main/UnbindInstance, Player.Main/_LoadBoundInstances, Player.Main/_LoadHomeBind, Player.Main/_LoadInventory, Player.Main/_LoadItemLoot, Player.Main/_LoadSkills, SocialMgr/AddToSocialList, SocialMgr/RemoveFromSocialList, WaypointManager/Load, World/BanAccount, World/BanAccount#2, World/HandleAccountSelectResult, World/RemoveBanAccount, World/SetInitialWorldSettings, World/SetPlayerLimit, World/Update, World/WarnAccount, World/_UpdateRealmCharCount, WorldSession.AuctionHouseHandler/HandleAuctionPlaceBid, WorldSession.CharacterHandler/HandleChangePlayerNameOpcodeCallBack, WorldSession.CharacterHandler/HandleCharCreateOpcode, WorldSession.ItemHandler/HandleWrapItemOpcode, WorldSession.MailHandler/HandleMailReturnToSender, WorldSession.MailHandler/HandleSendMailCallback, WorldSession.Main/ProcessAnticheatAction, WorldSession.Main/SetAccountData, WorldSession.PetHandler/HandlePetRename | — |
| DirectPExecute | method | Log.Main/Out | ChatHandler.CharacterCommands/HandleCharacterDeletedRestoreHelper, ChatHandler.CharacterCommands/HandleCharacterPremadeSaveGearCommand, ChatHandler.CharacterCommands/HandleCharacterPremadeSaveSpecCommand, ChatHandler.CharacterCommands/HandleDeleteItemCommand, HonorMgr/FlushRankPoints, HonorMgr/SetMaintenanceDays, HonorMgr/ToggleMaintenanceMarker, MapPersistentStateMgr/LoadResetTimes, MapPersistentStateMgr/ScheduleAllDungeonResets, MapPersistentStateMgr/Update, Master/Run | — |
| BeginTransaction | method | Errors/PrintStacktraceAndThrow, SqlTransaction/SqlTransaction | AccountMgr/DeleteAccount, AsyncCommandHandlers/HandleGoldLookupResult, ChatHandler.CharacterCommands/HandleCleanCharactersItemsCommand, Creature.Main/DeleteFromDB#2, Creature.Main/SaveToDB#2, GameObject/SaveToDB#2, game_Group_Group/Create, game_Group_Group/Disband, game_Group_Group/_addMember#2, game_Group_Group/_removeMember, game_Group_Group/_setAssistantFlag, game_Group_Group/_setLeader, game_Group_Group/_setMainAssistant, game_Group_Group/_setMainTank, game_Group_Group/_setMembersGroup, game_Group_Group/_swapMembersGroup, game_Guild_Guild/Create#2, game_Guild_Guild/Disband, game_Guild_Guild/LoadRanksFromDB, game_Mail_Mail/prepareTemplateItems, game_Mail_Mail/SendMailTo, game_Mail_Mail/SendReturnToSender, GuildMgr/Delete, HonorMgr/SetCityRanks, InstanceStatistics/IncrementCustomCounter, InstanceStatistics/Save, InstanceStatistics/Save#2, MapPersistentStateMgr/CleanupInstances, MapPersistentStateMgr/DeleteInstanceFromDB, MapPersistentStateMgr/DeleteRespawnTimesAndData, MapPersistentStateMgr/PackInstances, MapPersistentStateMgr/_ResetOrWarnAll, MasterPlayer.Main/SaveToDB, ObjectMgr/PackGroupIds, ObjectMgr/SetHighestGuids, ObjectMgr/_SaveVariable, Pet.Main/DeleteFromDB#2, Pet.Main/LoadPetFromDB, Pet.Main/SavePetToDB, Player.Main/AutoUnequipItemFromSlot, Player.Main/DeleteFromDB, Player.Main/RemovePetitionsAndSigns, Player.Main/SaveInventoryAndGoldToDB, Player.Main/SaveNewPlayer, Player.Main/SaveToDB, PlayerDump/LoadDump, realmd_Main/main, WardenMac/Update, WardenWin/Update, WorldSession.AuctionHouseHandler/HandleAuctionPlaceBid, WorldSession.AuctionHouseHandler/HandleAuctionRemoveItem, WorldSession.AuctionHouseHandler/HandleAuctionSellItem, WorldSession.CharacterHandler/HandleChangePlayerNameOpcodeCallBack, WorldSession.ItemHandler/HandleWrapItemOpcode, WorldSession.MailHandler/HandleMailReturnToSender, WorldSession.MailHandler/HandleMailTakeItem, WorldSession.MailHandler/HandleMailTakeMoney, WorldSession.MailHandler/HandleSendMailCallback, WorldSession.PetHandler/HandlePetRename | — |
| InTransaction | method | — | Player.Main/SaveInventoryAndGoldToDB | — |
| GetTransactionSerialId | method | SqlOperation/GetSerialId | Player.Main/SaveInventoryAndGoldToDB | — |
| CommitTransaction | method | SqlOperation/GetSerialId | AccountMgr/DeleteAccount, AsyncCommandHandlers/HandleGoldLookupResult, AuctionHouseMgr/SendAuctionWonMail, ChatHandler.CharacterCommands/HandleCleanCharactersItemsCommand, Creature.Main/DeleteFromDB#2, Creature.Main/SaveToDB#2, GameObject/SaveToDB#2, game_Group_Group/Create, game_Group_Group/Disband, game_Group_Group/_addMember#2, game_Group_Group/_removeMember, game_Group_Group/_setAssistantFlag, game_Group_Group/_setLeader, game_Group_Group/_setMainAssistant, game_Group_Group/_setMainTank, game_Group_Group/_setMembersGroup, game_Group_Group/_swapMembersGroup, game_Guild_Guild/Create#2, game_Guild_Guild/Disband, game_Guild_Guild/LoadRanksFromDB, game_Mail_Mail/prepareTemplateItems, game_Mail_Mail/SendMailTo, game_Mail_Mail/SendReturnToSender, GuildMgr/Delete, InstanceStatistics/IncrementCustomCounter, InstanceStatistics/Save, InstanceStatistics/Save#2, MapPersistentStateMgr/CleanupInstances, MapPersistentStateMgr/DeleteInstanceFromDB, MapPersistentStateMgr/DeleteRespawnTimesAndData, MapPersistentStateMgr/PackInstances, MapPersistentStateMgr/_ResetOrWarnAll, MasterPlayer.Main/SaveToDB, ObjectMgr/PackGroupIds, ObjectMgr/SetHighestGuids, ObjectMgr/_SaveVariable, Pet.Main/DeleteFromDB#2, Pet.Main/LoadPetFromDB, Pet.Main/SavePetToDB, Player.Main/AutoUnequipItemFromSlot, Player.Main/DeleteFromDB, Player.Main/RemovePetitionsAndSigns, Player.Main/SaveInventoryAndGoldToDB, Player.Main/SaveNewPlayer, Player.Main/SaveToDB, PlayerDump/LoadDump, realmd_Main/main, WardenMac/Update, WardenWin/Update, WorldSession.AuctionHouseHandler/HandleAuctionPlaceBid, WorldSession.AuctionHouseHandler/HandleAuctionRemoveItem, WorldSession.AuctionHouseHandler/HandleAuctionSellItem, WorldSession.CharacterHandler/HandleChangePlayerNameOpcodeCallBack, WorldSession.ItemHandler/HandleWrapItemOpcode, WorldSession.MailHandler/HandleMailReturnToSender, WorldSession.MailHandler/HandleMailTakeItem, WorldSession.MailHandler/HandleMailTakeMoney, WorldSession.MailHandler/HandleSendMailCallback, WorldSession.PetHandler/HandlePetRename | — |
| CommitTransactionDirect | method | SqlOperations/Execute#6 | HonorMgr/SetCityRanks | — |
| RollbackTransaction | method | — | PlayerDump/LoadDump | — |
| AddToSerialDelayQueue | method | SqlDelayThread/addSerialOperation, SqlOperation/GetSerialId | SqlOperations/Execute#4 | — |
| HasAsyncQuery | method | SqlDelayThread/HasAsyncQuery | World/CharactersDatabaseWorkerThread | — |
| CheckRequiredMigrations | method | Field/GetString, Log.Main/Out, QueryResult/Fetch, QueryResult/NextRow | Master/StartDB, realmd_Main/StartDB | migrations |
| ExecuteStmt | method | SqlOperations/SqlPreparedRequest, SqlStatementID/ID, SqlTransaction/DelayExecute | SqlPreparedStatement/Execute#2 | — |
| DirectExecuteStmt | method | Errors/PrintStacktraceAndThrow, Lock/Lock#5, Lock/operator->, SqlStatementID/ID | SqlPreparedStatement/DirectExecute | — |
| CreateStatement | method | SqlStatement/SqlStatement#2, SqlStatementID/init, SqlStatementID/initialized | Corpse/DeleteFromDB, Creature.Main/LogDeath, Creature.Main/LogLongCombat, game_Battlegrounds_BattleGround/EndBattleGround, game_Objects_Item/DeleteAllFromDB, game_Objects_Item/DeleteFromDB, game_Objects_Item/DeleteFromInventoryDB, game_Objects_Item/LoadFromDB, game_Objects_Item/SaveToDB, GMTicketMgr/DeleteFromDB, GMTicketMgr/SaveToDB, MapPersistentStateMgr/SaveCreatureRespawnTime, MapPersistentStateMgr/SaveGORespawnTime, MasterPlayer.Main/SaveActions, MasterPlayer.Main/SaveMails, Pet.Main/DeleteFromDB#2, Pet.Main/LoadPetFromDB, Pet.Main/SavePetToDB, Pet.Main/_SaveAuras, Pet.Main/_SaveSpellCooldowns, Pet.Main/_SaveSpells, Player.Main/DestroyItem, Player.Main/PlayerLogToDB, Player.Main/SaveGoldToDB, Player.Main/SaveNewPlayer, Player.Main/SaveToDB, Player.Main/_SaveAuras, Player.Main/_SaveBGData, Player.Main/_SaveInventory, Player.Main/_SaveQuestStatus, Player.Main/_SaveSkills, Player.Main/_SaveSpellCooldowns, Player.Main/_SaveSpells, Player.Main/_SaveStats, ReputationMgr/SaveToDB, WardenMac/Update, WardenWin/Update, World/AddSession_, World/LogMoneyTrade, World/LogTransaction, WorldSession.CharacterHandler/HandlePlayerLogin, WorldSession.GMTicketHandler/HandleGMSurveySubmitOpcode, WorldSession.Main/LogoutPlayer, WorldSession.Main/SaveTutorialsData, WorldSession.SpellHandler/HandleOpenItemOpcode, WorldSocket/_HandleAuthSession | — |
| GetStmtString | method | — | SqlPreparedStatement/DirectExecute, SqlPreparedStatement/Execute#2 | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `migrations`: id varchar(255) PK

*`?` = nullable, `PK` = primary key column.*

