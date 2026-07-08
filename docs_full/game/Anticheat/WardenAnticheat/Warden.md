<!-- provenance: verbose -->
# Warden

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Warden Base Class

## Purpose & Responsibilities

`Warden` is the abstract base class for the server-side anti-cheat subsystem. It manages the encrypted communication channel (RC4) with the client-side Warden module or Maiev fallback, orchestrates integrity scans, and tracks client session metadata. As an abstract class, it defines the interface for platform-specific initialization (`InitializeClient`, `GetScanFlags`) and state retrieval (`GetPlayerInfo`), while providing shared logic for packet encryption, scan queuing, and penalty enforcement.

## Member-by-Member Behavior

### Virtual Interface
Derived classes implement these pure virtual methods to handle OS-specific initialization and data retrieval:
*   **`InitializeClient`**: Initializes the client-side module. Called by `Log.Warden/HandlePacket`.
*   **`GetScanFlags`**: Returns the bitmask of enabled scan categories. Called by `Log.Warden/SelectScans`.
*   **`SetCharEnumPacket`**: Stores a character enumeration packet to send after initialization.
*   **`GetPlayerInfo`**: Retrieves client environment details (clock, fingerprint, etc.). Called by `AsyncCommandHandlers/HandlePInfoCommand`.

### Session & State Accessors
*   **`GetAccountId`**, **`GetAccountName`**, **`GetSessionIP`**: Expose protected session metadata for logging. Called by `Log.Warden/OutWarden`.
*   **`HasUsedClickToMove`**: Returns whether the client has used click-to-move. Called by `AsyncCommandHandlers/HandlePInfoCommand`, `WardenWin/LoadScriptedScans`, and `WorldSession.Main/HasUsedClickToMove`.
*   **`SetHasUsedClickToMove`**: Sets the click-to-move flag. Uses `mutable` to allow modification in `const` context. Called by `WardenWin/LoadScriptedScans`.
*   **`IsUsingMaiev`**: Indicates if the Maiev module is active. Called by `WardenScan/GetChecker`.
*   **`GetModule`**: Returns the `WardenModule` configuration. Called by various `WardenScan` units and `WardenWin/LoadScriptedScans`.
*   **`GetXor`**: Returns the XOR byte for packet obfuscation. Called by various `WardenScan` units and `WardenWin/LoadScriptedScans`.

### Lifecycle
*   **`~Warden`**: Defaulted destructor.

## Cross-Unit Boundaries

*   **`Log.Warden`**: Initiates anti-cheat via `InitializeClient` and `GetScanFlags`; logs violations using `GetAccountId`, `GetAccountName`, and `GetSessionIP`.
*   **`AsyncCommandHandlers`**: Queries `HasUsedClickToMove` and `GetPlayerInfo` for admin inspection.
*   **`WardenWin`**: Configures the instance via `SetHasUsedClickToMove`, `GetModule`, and `GetXor` during `LoadScriptedScans`.
*   **`WardenScan`**: Various scan types (Code, Driver, FileHash, Hook, Lua, Memory, Module, Time) use `IsUsingMaiev`, `GetModule`, and `GetXor` to generate payloads and validate results.
*   **`WorldSession.Main`**: Queries `HasUsedClickToMove` for session state.

## Data Model

This unit does not execute SQL queries. It exposes account and IP data for derived classes or logging units to write to external tables.

## Notable Implementation Details

1.  **Mutable State**: `m_hasUsedClickToMove` is `mutable`, enabling `SetHasUsedClickToMove()` to be `const`.
2.  **Dual RC4 Streams**: Separate `RC4` instances (`m_inputCrypto`, `m_outputCrypto`) ensure independent encryption states for full-duplex communication.
3.  **Scan Batching**: `EnqueueScans` batches checks to reduce network overhead; `RequestScans` sends immediately if possible.

## Member Reference

**InitializeClient**
Pure virtual method to initialize the client-side Warden module. Implemented by derived classes. Called by `Log.Warden/HandlePacket`.

**GetScanFlags**
Pure virtual method returning a bitmask of enabled scan types. Implemented by derived classes. Called by `Log.Warden/SelectScans`.

**~Warden**
Defaulted destructor. No custom cleanup logic.

**GetAccountId**
Returns the numeric account ID (`m_accountId`). Called by `Log.Warden/OutWarden`.

**GetAccountName**
Returns the account name string (`m_accountName`). Called by `Log.Warden/OutWarden`.

**GetSessionIP**
Returns the session IP address (`m_sessionIP`). Called by `Log.Warden/OutWarden`.

**HasUsedClickToMove**
Returns whether the client has used click-to-move. Called by `AsyncCommandHandlers/HandlePInfoCommand`, `WardenWin/LoadScriptedScans`, and `WorldSession.Main/HasUsedClickToMove`.

**SetHasUsedClickToMove**
Sets the click-to-move flag to `true`. Marked `const` due to `mutable` member. Called by `WardenWin/LoadScriptedScans`.

**IsUsingMaiev**
Returns `true` if using the Maiev module. Called by `WardenScan/GetChecker`.

**GetModule**
Returns the `WardenModule` configuration pointer. Called by various `WardenScan` units and `WardenWin/LoadScriptedScans`.

**GetXor**
Returns the XOR byte for packet obfuscation. Called by various `WardenScan` units and `WardenWin/LoadScriptedScans`.

**SetCharEnumPacket**
Pure virtual method to store a character enumeration packet for delayed sending. Implemented by derived classes.

**GetPlayerInfo**
Pure virtual method to retrieve client environment details. Implemented by derived classes. Called by `AsyncCommandHandlers/HandlePInfoCommand`.

---

<!-- machine-true, projected from graph.json -->

## Map — Warden

*Source:* Warden.hpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| InitializeClient | decl | — | Log.Warden/HandlePacket | — |
| GetScanFlags | decl | — | Log.Warden/SelectScans | — |
| ~Warden | dtor | — | — | — |
| GetAccountId | method | — | Log.Warden/OutWarden | — |
| GetAccountName | method | — | Log.Warden/OutWarden | — |
| GetSessionIP | method | — | Log.Warden/OutWarden | — |
| HasUsedClickToMove | method | — | AsyncCommandHandlers/HandlePInfoCommand, WardenWin/LoadScriptedScans, WorldSession.Main/HasUsedClickToMove | — |
| SetHasUsedClickToMove | method | — | WardenWin/LoadScriptedScans | — |
| IsUsingMaiev | method | — | WardenScan/GetChecker | — |
| GetModule | method | — | WardenScan/WindowsCodeScan, WardenScan/WindowsDriverScan, WardenScan/WindowsFileHashScan, WardenScan/WindowsHookScan, WardenScan/WindowsLuaScan, WardenScan/WindowsLuaScan#2, WardenScan/WindowsLuaScan#3, WardenScan/WindowsMemoryScan, WardenScan/WindowsMemoryScan#2, WardenScan/WindowsMemoryScan#3, WardenScan/WindowsMemoryScan#4, WardenScan/WindowsModuleScan, WardenScan/WindowsModuleScan#2, WardenScan/WindowsTimeScan, WardenWin/LoadScriptedScans | — |
| GetXor | method | — | WardenScan/WindowsCodeScan, WardenScan/WindowsDriverScan, WardenScan/WindowsFileHashScan, WardenScan/WindowsHookScan, WardenScan/WindowsLuaScan, WardenScan/WindowsLuaScan#2, WardenScan/WindowsLuaScan#3, WardenScan/WindowsMemoryScan, WardenScan/WindowsMemoryScan#2, WardenScan/WindowsMemoryScan#3, WardenScan/WindowsMemoryScan#4, WardenScan/WindowsModuleScan, WardenScan/WindowsModuleScan#2, WardenScan/WindowsTimeScan, WardenWin/LoadScriptedScans | — |
| SetCharEnumPacket | decl | — | — | — |
| GetPlayerInfo | decl | — | AsyncCommandHandlers/HandlePInfoCommand | — |
