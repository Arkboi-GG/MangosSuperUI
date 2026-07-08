# SqlStatementID

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# SqlStatementID

**SqlStatementID** is a lightweight wrapper class within the `wowvmangos` codebase that encapsulates the identity and metadata of a prepared SQL statement. It serves as the opaque handle used throughout the server to reference specific database queries without exposing the underlying `Database` management logic or raw integer indices to general game logic.

Its primary responsibilities are:
1.  **Identity Management:** Storing the unique integer index (`m_nIndex`) assigned to a prepared statement by the `Database` system.
2.  **Metadata Storage:** Tracking the number of expected arguments (`m_nArguments`) for the associated SQL query.
3.  **State Validation:** Maintaining an initialization flag (`m_bInitialized`) to ensure that a statement ID is valid before it is used, preventing runtime errors from uninitialized references.

This class is designed to be immutable from the perspective of most of the codebase. Its internal state is set exclusively by the `Database` unit during the preparation phase, and it is then passed by value or reference to various game entities (Players, Creatures, Items, etc.) to facilitate safe, type-checked database interactions.

## Member-by-Member Behavior

The members of `SqlStatementID` are minimal, reflecting its role as a simple data carrier.

### Construction and Initialization
*   **`SqlStatementID()`**: The default constructor initializes the object in an invalid state. It sets `m_bInitialized` to `false`. This ensures that any instance created on the stack or heap is immediately recognizable as unconfigured until explicitly initialized by the `Database` system.

### Accessors
*   **`ID()`**: Returns the internal integer index (`m_nIndex`) of the prepared statement. This value is used by the `Database` unit to look up the actual prepared statement object in its cache.
*   **`arguments()`**: Returns the count of parameters (`m_nArguments`) expected by the SQL query associated with this ID. This allows callers to verify that they are binding the correct number of arguments before execution.
*   **`initialized()`**: Returns the boolean flag `m_bInitialized`. This is a critical safety check used by the `Database` unit to determine if a statement ID has been properly registered. Using an uninitialized ID typically results in a failure to execute or a debug assertion.

### Internal State Setup
*   **`init(int nID, int nArgs)`**: A private method that configures the statement ID. It assigns the provided `nID` to `m_nIndex`, sets `nArgs` to `m_nArguments`, and marks the object as initialized. This method is declared as a `friend` of the `Database` class, meaning only the `Database` unit can call it. This enforces a strict separation of concerns: game logic cannot create or modify statement IDs directly; they must be obtained through the `Database` interface.

## Cross-Unit Boundaries

`SqlStatementID` acts as a bridge between the high-level game logic and the low-level database abstraction layer.

### Called By: Game Logic Units
A vast number of units in the codebase hold or receive instances of `SqlStatementID`. These units do not manipulate the ID's internals; they simply pass the ID to the `Database` system for execution. Key consumers include:

*   **Entity Persistence (Player, Creature, Item, Pet):**
    *   `Player.Main`, `Creature.Main`, `game_Objects_Item`, and `Pet.Main` use these IDs to save and load entity states (inventory, spells, auras, stats) to and from the database. For example, `Player.Main/SaveToDB` likely holds an ID for the "save player" query, passing it to the database along with the player's data.
    *   `Corpse.DeleteFromDB` and `GMTicketMgr/DeleteFromDB` use IDs for cleanup operations.
*   **World Session Handling:**
    *   `WorldSession` handlers (e.g., `HandlePlayerLogin`, `LogoutPlayer`, `HandleOpenItemOpcode`) use these IDs to perform immediate database updates triggered by client actions.
    *   `WorldSocket/_HandleAuthSession` uses an ID during the authentication process.
*   **System Managers:**
    *   `MapPersistentStateMgr` uses IDs to save respawn times for creatures and game objects.
    *   `ReputationMgr` and `MasterPlayer.Main` use IDs for saving reputation and action bars.
    *   `WardenMac` and `WardenWin` use IDs for anti-cheat logging.
    *   `World` uses IDs for global logging (`LogMoneyTrade`, `LogTransaction`) and session management (`AddSession_`).

In all these cases, the direction of interaction is **outbound**: the game unit possesses the `SqlStatementID` and passes it (often implicitly via a `SqlStatement` wrapper) to the `Database` unit for execution.

### Called By: Database Unit
*   **`Database/CreateStatement`**: This is the sole creator of valid `SqlStatementID` instances. When the server starts or when a new query is prepared, `Database` creates a `SqlStatementID`, calls its private `init()` method to assign a unique index and argument count, and then distributes this ID to the relevant game units.
*   **`Database/DirectExecuteStmt` and `Database/ExecuteStmt`**: These methods consume the `ID()` returned by `SqlStatementID` to locate the corresponding prepared statement in the database connection pool and execute it with the bound parameters.

## Data Model

`SqlStatementID` itself does not interact directly with database tables. It is a metadata container. However, the IDs it represents correspond to SQL queries that touch numerous tables across the `wowvmangos` database. Based on the callers listed in the MAP, these queries involve tables such as:

*   **Character Data:** `characters`, `character_inventory`, `character_skills`, `character_spells`, `character_auras`, `character_spell_cooldowns`, `character_actionbars`, `character_mail`, `character_tutorial_flags`.
*   **World Data:** `creature_respawn`, `gameobject_respawn`, `corpse`, `item_instance`, `items`.
*   **Logging & Admin:** `gm_tickets`, `gm_survey`, `warden_checks`, `money_trade_log`, `transaction_log`.

The `SqlStatementID` abstracts away the specific table structure, allowing game logic to focus on *what* data is being saved or loaded rather than *how* the SQL is constructed.

## Notable Implementation Details

1.  **Friend Class Restriction:** The `init()` method is private and only accessible to the `Database` class. This is a deliberate design choice to prevent accidental corruption of statement IDs. Game logic cannot change the ID or argument count of a statement once it has been prepared.
2.  **Default Invalid State:** The default constructor sets `m_bInitialized` to `false`. This is a safety mechanism. If a developer forgets to initialize a `SqlStatementID` before using it, the `Database` unit can detect this via the `initialized()` check and fail gracefully (or assert in debug builds) rather than executing a random or null query.
3.  **Value Semantics:** `SqlStatementID` is a small, trivially copyable struct-like class. It is passed by value in many contexts (e.g., as a member variable in `SqlStatement`). This is efficient and safe because it contains only primitive types (`int`, `bool`) and no pointers or dynamic allocations.
4.  **No String Storage:** Unlike some ORM systems, `SqlStatementID` does not store the SQL string itself. The string is stored in the `Database` unit's cache, associated with the integer ID. This keeps the ID object lightweight and avoids redundant string storage across multiple game entities.

## Member Reference

**SqlStatementID**
The default constructor for the `SqlStatementID` class. It initializes the object with `m_bInitialized` set to `false`, ensuring that any newly created instance is considered invalid until explicitly initialized by the `Database` unit. This prevents the use of uninitialized statement IDs.

**ID**
A public accessor method that returns the internal integer index (`m_nIndex`) of the prepared statement. This value is used by the `Database` unit to retrieve the actual prepared statement object from its cache. It is called by `Database/DirectExecuteStmt` and `Database/ExecuteStmt` to identify which query to run.

**arguments**
A public accessor method that returns the number of parameters (`m_nArguments`) expected by the SQL query associated with this ID. This allows the caller to verify that the correct number of arguments are being bound before execution. It is primarily used internally by the `SqlStatement` class to manage parameter binding.

**initialized**
A public accessor method that returns the boolean flag `m_bInitialized`. This indicates whether the `SqlStatementID` has been properly configured by the `Database` unit. It is called by `Database/CreateStatement` to check the state of the ID, and potentially by other parts of the database system to validate IDs before execution.

**init**
A private method that initializes the `SqlStatementID` with a specific ID (`nID`) and argument count (`nArgs`). It sets `m_nIndex`, `m_nArguments`, and marks the object as initialized. This method is declared as a `friend` of the `Database` class, meaning only the `Database` unit can call it. This enforces the rule that statement IDs are created and managed exclusively by the database abstraction layer.

---

<!-- machine-true, projected from graph.json -->

## Map — SqlStatementID

*Source:* SqlPreparedStatement.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| SqlStatementID | ctor | — | Corpse/DeleteFromDB, Creature.Main/LogDeath, Creature.Main/LogLongCombat, game_Battlegrounds_BattleGround/EndBattleGround, game_Objects_Item/DeleteAllFromDB, game_Objects_Item/DeleteFromDB, game_Objects_Item/DeleteFromInventoryDB, game_Objects_Item/LoadFromDB, game_Objects_Item/SaveToDB, GMTicketMgr/DeleteFromDB, GMTicketMgr/SaveToDB, MapPersistentStateMgr/SaveCreatureRespawnTime, MapPersistentStateMgr/SaveGORespawnTime, MasterPlayer.Main/SaveActions, MasterPlayer.Main/SaveMails, Pet.Main/DeleteFromDB#2, Pet.Main/LoadPetFromDB, Pet.Main/SavePetToDB, Pet.Main/_SaveAuras, Pet.Main/_SaveSpellCooldowns, Pet.Main/_SaveSpells, Player.Main/DestroyItem, Player.Main/PlayerLogToDB, Player.Main/SaveGoldToDB, Player.Main/SaveNewPlayer, Player.Main/SaveToDB, Player.Main/_SaveAuras, Player.Main/_SaveBGData, Player.Main/_SaveInventory, Player.Main/_SaveQuestStatus, Player.Main/_SaveSkills, Player.Main/_SaveSpellCooldowns, Player.Main/_SaveSpells, Player.Main/_SaveStats, ReputationMgr/SaveToDB, WardenMac/Update, WardenWin/Update, World/AddSession_, World/LogMoneyTrade, World/LogTransaction, WorldSession.CharacterHandler/HandlePlayerLogin, WorldSession.GMTicketHandler/HandleGMSurveySubmitOpcode, WorldSession.Main/LogoutPlayer, WorldSession.Main/SaveTutorialsData, WorldSession.SpellHandler/HandleOpenItemOpcode, WorldSocket/_HandleAuthSession | — |
| ID | method | — | Database/DirectExecuteStmt, Database/ExecuteStmt | — |
| arguments | method | — | — | — |
| initialized | method | — | Database/CreateStatement | — |
| init | method | — | Database/CreateStatement | — |
