# PlayerBotMgr

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# PlayerBotMgr

**Purpose & Responsibilities**

`PlayerBotMgr` is the singleton manager for the Player Bot subsystem. It maintains the in-memory registry of bot entities (`PlayerBotEntry`), tracks their states (offline, loading, online), and provides configuration-driven controls for bot behavior. Key responsibilities include:
1.  **Registry Management**: Storing bot metadata (GUID, account ID, AI pointer, state) in `m_bots`.
2.  **Lifecycle Hooks**: Exposing methods to check if saving is allowed (`IsSavingAllowed`) and generating unique account IDs (`GenBotAccountId`) for bot creation.
3.  **Administration Interface**: Providing statistics (`GetStats`) and runtime toggles (`Start`) for administrative commands.

The unit does not directly execute SQL queries; it operates on in-memory data structures populated by other subsystems.

## Member-by-Member Behavior

### Administrative & Utility Methods

*   **`IsSavingAllowed`**: Returns the boolean flag `m_confAllowSaving`. This gates whether bot characters are permitted to persist data to the database, preventing unnecessary I/O or conflicts with human player data.
*   **`GenBotAccountId`**: Increments and returns the internal counter `m_maxAccountId`. This generates unique, sequential account IDs for new bots, ensuring uniqueness within the server session.
*   **`GetStats`**: Returns a reference to the `PlayerBotStats` struct, exposing real-time metrics (online/loading counts) and configuration limits to administrators.
*   **`Start`**: Sets `m_confEnableRandomBots` to `true`, dynamically enabling the automatic population of random bots without requiring a server restart.

## Cross-Unit Boundaries

### Called By: `ChatHandler.PlayerBotMgr`

The `ChatHandler` unit uses `PlayerBotMgr` to implement console commands for bot management:
*   **`GenBotAccountId`**: Called by `ChatHandler.PlayerBotMgr/AddBot` and `ChatHandler.PlayerBotMgr/Load` to obtain a unique account ID when creating or loading bots via command.
*   **`GetStats`**: Called by `ChatHandler.PlayerBotMgr/HandleBotInfoCommand` to retrieve and display current bot statistics.
*   **`Start`**: Called by `ChatHandler.PlayerBotMgr/HandleBotStartCommand` to enable random bot generation at runtime.

### Called By: `WorldSession`

The `WorldSession` unit consults `PlayerBotMgr` to determine save permissions for bot sessions:
*   **`IsSavingAllowed`**: Called by `WorldSession.CharacterHandler/HandlePlayerLogin`, `WorldSession.Main/Update`, and `WorldSession.Main/~WorldSession`. If this returns `false`, the session skips saving character data, optimizing performance and reducing database load for bot accounts.

## Data Model

This unit does not directly interact with database tables. It manages in-memory `PlayerBotEntry` objects. Any database persistence is handled by other units or deferred based on the `m_confAllowSaving` flag.

## Notable Implementation Details

1.  **Singleton Access**: Accessed globally via `sPlayerBotMgr` macro, wrapping `MaNGOS::Singleton<PlayerBotMgr>`.
2.  **Inline Implementations**: The four documented members are implemented inline in the header for performance, as they are simple getters or atomic increments.
3.  **No Direct DB Calls**: Despite including `DatabaseEnv.h`, this unit performs no SQL operations. It relies on other components to handle persistence.

## Member Reference

*   **IsSavingAllowed**: Returns `m_confAllowSaving`. Used by `WorldSession` to gate database saves for bot characters.
*   **GenBotAccountId**: Increments `m_maxAccountId` and returns the result. Used by `ChatHandler.PlayerBotMgr` to assign unique IDs to new bots.
*   **GetStats**: Returns a reference to `m_stats`. Used by `ChatHandler.PlayerBotMgr` to report bot population metrics.
*   **Start**: Sets `m_confEnableRandomBots` to `true`. Used by `ChatHandler.PlayerBotMgr` to activate random bot generation.

---

<!-- machine-true, projected from graph.json -->

## Map — PlayerBotMgr

*Source:* PlayerBotMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| IsSavingAllowed | method | — | WorldSession.CharacterHandler/HandlePlayerLogin, WorldSession.Main/Update, WorldSession.Main/~WorldSession | — |
| GenBotAccountId | method | — | ChatHandler.PlayerBotMgr/AddBot, ChatHandler.PlayerBotMgr/Load | — |
| GetStats | method | — | ChatHandler.PlayerBotMgr/HandleBotInfoCommand | — |
| Start | method | — | ChatHandler.PlayerBotMgr/HandleBotStartCommand | — |
