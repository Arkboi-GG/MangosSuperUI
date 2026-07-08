<!-- provenance: verbose -->
# Anticheat

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Anticheat Manager

The `AnticheatManager` singleton coordinates server-side anti-cheat systems in wowvmangos, primarily managing **Warden** (client integrity scanning) and **Movement Anticheat** (player movement validation). It also provides stub interfaces for antispam functionality, which currently return default values rather than enforcing active filters.

As a global singleton (`sAnticheatMgr`), the manager initializes these systems during world startup, maintains a dedicated background thread to update active Warden sessions, and handles resource cleanup on shutdown. It acts as a factory for creating OS-specific Warden instances and as a thread-safe registry for tracking active sessions.

## Member-by-Member Behavior

### Initialization and Lifecycle

**`instance`**
Static method implementing the Meyers Singleton pattern, ensuring a single `AnticheatManager` instance exists for the application's lifetime. Accessed globally via `GetAnticheatLib`.

**`~AnticheatManager`**
Destructor that cleans up managed resources. It stops the Warden update thread, processes any pending session additions/removals to ensure list consistency, and deletes all remaining `Warden` objects in `m_wardenSessions` to prevent memory leaks.

**`LoadAnticheatData`**
Initializes the Warden subsystem, guarded by `SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_5_1`. It logs progress via `Log.Main/Out`, loads scripted scans from the database via `WardenScanMgr/LoadFromDB`, and initializes Warden modules.

**`StartWardenUpdateThread`**
Creates a background thread named "WardenSessions" using `CreateThread/CreateThread` to run the `UpdateWardenSessions` loop.

**`StopWardenUpdateThread`**
Joins the Warden update thread if active, ensuring clean termination during shutdown.

### Warden Session Management

**`CreateWardenForInternal`**
Private helper determining if a Warden instance should be created. It checks `World/getConfig` for `CONFIG_BOOL_AC_WARDEN_PLAYERS_ONLY` to exempt high-security accounts, and `WorldSession.Main/GetOS` to select the appropriate implementation: `WardenMac/WardenMac` for macOS or `WardenWin/WardenWin` for Windows, respecting respective enable flags. Returns `nullptr` if conditions aren't met or the client build is unsupported.

**`CreateWardenFor`**
Public factory method. Calls `CreateWardenForInternal`; if a valid `Warden` is returned, it registers the session via `AddWardenSession` for tracking by the update thread.

**`UpdateWardenSessions`**
The background thread's main loop. While `World/IsStopped` is false, it locks `m_wardenSessionsMutex`, calls `AddOrRemovePendingSessions` to sync the main list, iterates `m_wardenSessions` calling `Update()` on each non-null `Warden`, and sleeps for one second.

**`AddOrRemovePendingSessions`**
Processes `m_wardenSessionsToAdd` and `m_wardenSessionsToRemove` queues. It delegates to `AddWardenSessionInternal` and `RemoveWardenSessionInternal` to modify the main `m_wardenSessions` list, then clears the queues. Always called under mutex protection.

**`AddWardenSessionInternal`**
Adds a `Warden` pointer to `m_wardenSessions`. It optimizes by reusing existing `nullptr` slots in the vector to avoid reallocation; if none exist, it pushes back.

**`RemoveWardenSessionInternal`**
Removes a `Warden` pointer from `m_wardenSessions`. It sets the slot to `nullptr` (for reuse) and deletes the `Warden` object.

**`AddWardenSession`**
Public interface to queue a `Warden` for addition. Locks `m_wardenSessionsMutex` and pushes the pointer to `m_wardenSessionsToAdd`.

**`RemoveWardenSession`**
Public interface to queue a `Warden` for removal. Locks `m_wardenSessionsMutex` and pushes the pointer to `m_wardenSessionsToRemove`.

### Movement Anticheat

**`CreateAnticheatFor`**
Factory method creating a `MovementAnticheat` instance for a `Player`. It allocates the object, calls `MovementAnticheat/Init`, and returns the pointer.

### Antispam Stubs

**`GetAntispam`**
Returns `nullptr`. Indicates no functional antispam interface is loaded. Callers must handle the null case.

**`CanWhisper`**
Always returns `true`. Imposes no server-side whisper restrictions; permissions are handled elsewhere (e.g., `AccountMgr`).

## Cross-Unit Boundaries

### Called By

*   **`World/SetInitialWorldSettings`**: Calls `LoadAnticheatData` and `StartWardenUpdateThread` to initialize anticheat systems at startup.
*   **`World/Shutdown`**: Calls `StopWardenUpdateThread` to terminate the background thread.
*   **`WorldSession.Main/InitCheatData`** & **`WorldSession.Main/GetCheatData`**: Call `CreateAnticheatFor` to set up movement anticheat for players.
*   **`WorldSession.Main/InitWarden`**: Calls `CreateWardenFor` to initialize Warden integrity checks.
*   **`WorldSession.Main/Update`** & **`WorldSession.Main/~WorldSession`**: Call `RemoveWardenSession` to clean up Warden instances when sessions end or update.
*   **`AccountMgr/CanWhisper`**: Calls `CanWhisper` (always returns true).
*   **`ChatHandler.*`** (Account/Server Commands): Call `GetAnticheatLib` and `GetAntispam` for spam-related commands. Since `GetAntispam` returns null, these commands likely fail silently or rely on other mechanisms.
*   **`game_Guild_Guild/Create#2`** & **`WorldSession.PetitionsHandler/HandlePetitionBuyOpcode`**: Call `GetAnticheatLib` and `GetAntispam`, potentially for logging or filtering, though the null return suggests limited functionality.

### Calls Out

*   **`Log.Main/Out`**: Logs initialization steps in `LoadAnticheatData`.
*   **`WardenScanMgr/LoadFromDB`**: Loads Warden scan definitions from the database.
*   **`WardenMac/WardenMac`** & **`WardenWin/WardenWin`**: Constructors for OS-specific Warden implementations.
*   **`World/getConfig`**: Retrieves configuration flags for Warden enablement and exemptions.
*   **`WorldSession.Main/GetOS`** & **`WorldSession.Main/GetSecurity`**: Determine client OS and account security level for Warden creation logic.
*   **`CreateThread/CreateThread`**: Creates the background Warden update thread.
*   **`World/IsStopped`**: Checked in the update loop to determine exit condition.
*   **`MovementAnticheat/Init`** & **`MovementAnticheat/MovementAnticheat`**: Initialize and construct movement anticheat objects.

## Data Model

The `AnticheatManager` does not directly query or modify database tables. It relies on `WardenScanMgr/LoadFromDB` to load Warden scan definitions, but the specific tables are managed by that external unit. All anticheat state (sessions, queues) is held in memory.

## Notable Implementation Details

1.  **Thread-Safe Session Queueing**: `AddWardenSession` and `RemoveWardenSession` queue operations to `m_wardenSessionsToAdd` and `m_wardenSessionsToRemove` under mutex protection. The background thread processes these queues in `UpdateWardenSessions`, preventing main-thread blocking during session list modifications.
2.  **Vector Slot Reuse**: `AddWardenSessionInternal` and `RemoveWardenSessionInternal` reuse `nullptr` slots in `m_wardenSessions` to minimize vector reallocations and maintain index stability.
3.  **Stub Antispam**: `GetAntispam` returns `nullptr` and `CanWhisper` returns `true`. This indicates antispam functionality is either disabled or handled elsewhere; callers must account for null returns.
4.  **Client Build Guard**: Warden functionality is guarded by `#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_5_1`. Older clients bypass Warden checks entirely.
5.  **GM Exemption**: If `CONFIG_BOOL_AC_WARDEN_PLAYERS_ONLY` is enabled, accounts with security > `SEC_PLAYER` are exempt from Warden checks.

## Member Reference

**`instance`**: Static method returning the singleton `AnticheatManager` instance.

**`GetAnticheatLib`**: Global function returning the singleton `AnticheatManager` instance.

**`~AnticheatManager`**: Destructor stopping the Warden thread, processing pending sessions, and deleting all `Warden` objects.

**`LoadAnticheatData`**: Initializes Warden scans and modules from the database and config, logging progress.

**`CreateAnticheatFor`**: Factory method creating and initializing a `MovementAnticheat` object for a player.

**`CreateWardenForInternal`**: Private helper creating a `WardenMac` or `WardenWin` object based on client OS and config, or `nullptr` if exempt/disabled.

**`CreateWardenFor`**: Public factory method creating a Warden object and registering it with the manager.

**`StartWardenUpdateThread`**: Starts the background thread that updates Warden sessions.

**`StopWardenUpdateThread`**: Joins and stops the background Warden update thread.

**`GetAntispam`**: Returns `nullptr`, indicating no antispam interface is available.

**`CanWhisper`**: Always returns `true`, imposing no whisper restrictions.

**`UpdateWardenSessions`**: Background thread loop that synchronizes pending sessions and updates all active Warden sessions every second.

**`AddOrRemovePendingSessions`**: Processes the queues of sessions to add or remove, updating the main session list.

**`AddWardenSessionInternal`**: Adds a Warden pointer to the main session list, reusing empty slots if possible.

**`RemoveWardenSessionInternal`**: Removes a Warden pointer from the main session list, sets the slot to null, and deletes the object.

**`AddWardenSession`**: Queues a Warden pointer for addition to the main session list.

**`RemoveWardenSession`**: Queues a Warden pointer for removal from the main session list.

---

<!-- machine-true, projected from graph.json -->

## Map — Anticheat

*Source:* Anticheat.cpp, Anticheat.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| instance | method | — | — | — |
| GetAnticheatLib | function | — | AccountMgr/CanWhisper, ChatHandler.AccountCommands/HandleSpamerList, ChatHandler.AccountCommands/HandleSpamerMute, ChatHandler.AccountCommands/HandleSpamerUnmute, ChatHandler.ServerCommands/HandleAntiSpamAdd, ChatHandler.ServerCommands/HandleAntiSpamRemove, ChatHandler.ServerCommands/HandleAntiSpamRemoveReplace, ChatHandler.ServerCommands/HandleAntiSpamReplace, ChatHandler.ServerCommands/HandleReloadAnticheatCommand, game_Guild_Guild/Create#2, World/SetInitialWorldSettings, World/Shutdown, WorldSession.ChatHandler/HandleChatMessageOpcode, WorldSession.Main/GetCheatData, WorldSession.Main/InitCheatData, WorldSession.Main/InitWarden, WorldSession.Main/Update, WorldSession.Main/~WorldSession, WorldSession.PetitionsHandler/HandlePetitionBuyOpcode | — |
| ~AnticheatManager | dtor | — | — | — |
| LoadAnticheatData | method | Log.Main/Out, Log.Warden/LoadScriptedScans, WardenScanMgr/LoadFromDB | ChatHandler.ServerCommands/HandleReloadAnticheatCommand, World/SetInitialWorldSettings | — |
| CreateAnticheatFor | method | MovementAnticheat/Init, MovementAnticheat/MovementAnticheat | WorldSession.Main/GetCheatData, WorldSession.Main/InitCheatData | — |
| CreateWardenForInternal | method | WardenMac/WardenMac, WardenWin/WardenWin, World/getConfig, WorldSession.Main/GetOS, WorldSession.Main/GetSecurity | — | — |
| CreateWardenFor | method | — | WorldSession.Main/InitWarden | — |
| StartWardenUpdateThread | method | CreateThread/CreateThread | World/SetInitialWorldSettings | — |
| StopWardenUpdateThread | method | — | World/Shutdown | — |
| GetAntispam | method | — | ChatHandler.AccountCommands/HandleSpamerList, ChatHandler.AccountCommands/HandleSpamerMute, ChatHandler.AccountCommands/HandleSpamerUnmute, ChatHandler.ServerCommands/HandleAntiSpamAdd, ChatHandler.ServerCommands/HandleAntiSpamRemove, ChatHandler.ServerCommands/HandleAntiSpamRemoveReplace, ChatHandler.ServerCommands/HandleAntiSpamReplace, game_Guild_Guild/Create#2, WorldSession.ChatHandler/HandleChatMessageOpcode, WorldSession.PetitionsHandler/HandlePetitionBuyOpcode | — |
| CanWhisper | method | — | AccountMgr/CanWhisper | — |
| UpdateWardenSessions | method | Log.Warden/Update, World/IsStopped | — | — |
| AddOrRemovePendingSessions | method | — | — | — |
| AddWardenSessionInternal | method | — | — | — |
| RemoveWardenSessionInternal | method | — | — | — |
| AddWardenSession | method | — | — | — |
| RemoveWardenSession | method | — | WorldSession.Main/Update, WorldSession.Main/~WorldSession | — |
