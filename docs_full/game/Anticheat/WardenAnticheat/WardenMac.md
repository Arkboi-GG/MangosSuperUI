<!-- provenance: verbose -->
# WardenMac

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# WardenMac

**WardenMac** implements the macOS-specific server-side logic for the Warden anti-cheat system, inheriting from `Warden`. It is conditionally compiled for client builds newer than `CLIENT_BUILD_1_5_1`. Its primary responsibilities are registering Mac-specific string hash scans, managing the delayed delivery of the Character Enumeration packet until the client agent initializes, and recording a placeholder system fingerprint in the database upon successful initialization.

## Member-by-Member Behavior

### Initialization and Scans

**LoadScriptedScans**
Static method called during server startup to register two `MacStringHashScan` instances with `WardenScanMgr` (one enabled, one disabled), populating the pool of scans available for Mac clients.

**WardenMac**
Constructor that initializes the base `Warden` class. It checks the client platform via `WorldSession::GetPlatform()`: if `CLIENT_PLATFORM_X86`, it retrieves the Mac module via `WardenModuleMgr::GetMacModule`; otherwise, it passes `nullptr`. It initializes the internal state `m_fingerprintSaved` to `false`.

**GetScanFlags**
Returns `ScanFlags::Mac` combined with `ScanFlags::Maiev` if the Maiev component is active. The base class uses this to determine which scans to request from the client.

**InitializeClient**
Sets the inherited `m_initialized` flag to `true`, signaling that the client-side Warden agent is ready. This transition allows `Update` to proceed with fingerprint persistence and packet dispatch.

### Update Loop and Packet Handling

**Update**
Overrides `Warden::Update()` to manage state transitions:
1.  **Pre-Initialization (`!m_initialized`):** If Maiev is active and the timeout clock hasn't started, it requests Maiev scans via `Log.Warden` methods. If no module is loaded (`!m_module`), it starts the scan clock. Returns early.
2.  **Post-Initialization:** If `m_fingerprintSaved` is false, it inserts a record into `system_fingerprint_usage` in `LogsDatabase`. The `fingerprint` column is hardcoded to `0` ("not implemented"), while `account`, `ip`, and `realm` are populated from session data. It then sets `m_fingerprintSaved` to `true`.
3.  **Packet Dispatch:** If a character enumeration packet (`m_charEnum`) is buffered, it schedules sending via `World::GetMessager()`. The callback verifies the session still exists and matches the stored GUID before calling `WorldSession::SendPacket()`.

**SetCharEnumPacket**
Manages the Character Enumeration packet delivery. If `m_initialized` is true, it immediately schedules the packet for sending via the World messager (with session verification). If not initialized, it buffers the packet in `m_charEnum` for later dispatch in `Update`.

**GetPlayerInfo**
Empty implementation. Does not populate clock, fingerprint, hypervisor, renderer, or proxifier strings for Mac clients.

## Cross-Unit Boundaries

*   **WardenScanMgr:** `LoadScriptedScans` calls `AddMacScan` to register scans.
*   **WardenModuleMgr:** Constructor calls `GetMacModule` to retrieve the Mac-specific binary module.
*   **WorldSession.Main:** Constructor calls `GetPlatform`; `Update` and `SetCharEnumPacket` call `GetGUID` and `SendPacket` for session interaction.
*   **World:** `Update` and `SetCharEnumPacket` call `GetMessager` and `FindSession` to safely schedule and verify packet transmission.
*   **Log.Warden:** Inherits and calls base methods (`BeginScanClock`, `RequestScans`, `SelectScans`, `TimeoutClockStarted`, `Update`) for scan lifecycle management.
*   **Database:** `Update` uses `LogsDatabase` to persist fingerprint data.

## Data Model

**system_fingerprint_usage**
Records system fingerprint usage events.
*   **Columns Written:** `fingerprint` (hardcoded `0`), `account` (player ID), `ip` (session IP), `realm` (realm ID).
*   **Columns Ignored:** `id`, `time`, `architecture`, `cputype`, `activecpus`, `totalcpus`, `pagesize` are not populated by this unit.

## Notable Implementation Details

*   **Hardcoded Fingerprint:** `Update` inserts `0` for the `fingerprint` column, noting "not implemented". Mac clients do not contribute unique hardware fingerprints to this table.
*   **Thread-Safe Packet Dispatch:** `Update` and `SetCharEnumPacket` use `World::GetMessager()` to defer packet sending. The lambda captures `accountId` and `sessionGuid` to re-verify the session's existence and identity before sending, preventing crashes if the player disconnects during the delay.
*   **Architecture Check:** The constructor only loads the Mac module for `CLIENT_PLATFORM_X86`. Non-x86 platforms receive a `nullptr` module, causing `Update` to start the scan clock immediately without a module.

## Member Reference

**LoadScriptedScans**: Registers two `MacStringHashScan` instances with `WardenScanMgr`.

**WardenMac**: Initializes base `Warden`, loads Mac module if platform is `CLIENT_PLATFORM_X86`, sets `m_fingerprintSaved` to false.

**Update**: Handles pre-init scan requests; if initialized, saves fingerprint (as 0) to `system_fingerprint_usage` and dispatches buffered `m_charEnum` packet via World messager.

**GetPlayerInfo**: Empty method; does not populate system info strings.

**SetCharEnumPacket**: Buffers packet if not initialized; otherwise schedules immediate send via World messager with session verification.

**GetScanFlags**: Returns `ScanFlags::Mac` | `ScanFlags::Maiev` (if Maiev active).

**InitializeClient**: Sets `m_initialized` to true.

---

<!-- machine-true, projected from graph.json -->

## Map — WardenMac

*Source:* WardenMac.cpp, WardenMac.hpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| LoadScriptedScans | method | WardenScanMgr/AddMacScan | Log.Warden/LoadScriptedScans | — |
| WardenMac | ctor | Log.Warden/Warden, WardenModuleMgr/GetMacModule, WorldSession.Main/GetPlatform | Anticheat/CreateWardenForInternal | — |
| Update | method | ByteBuffer/clear, ByteBuffer/empty, Database/BeginTransaction, Database/CommitTransaction, Database/CreateStatement, Log.Warden/BeginScanClock, Log.Warden/RequestScans, Log.Warden/SelectScans, Log.Warden/TimeoutClockStarted, Log.Warden/Update, SqlPreparedStatement/Execute#2, SqlStatement/addString#2, SqlStatement/addUInt32, SqlStatementID/SqlStatementID, World/FindSession, World/GetMessager, WorldSession.Main/GetGUID, WorldSession.Main/SendPacket | — | system_fingerprint_usage |
| GetPlayerInfo | method | — | — | — |
| SetCharEnumPacket | method | World/FindSession, World/GetMessager, WorldPacket/operator=, WorldSession.Main/GetGUID, WorldSession.Main/SendPacket | — | — |
| GetScanFlags | method | WardenScan/operator| | — | — |
| InitializeClient | method | — | — | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `system_fingerprint_usage`: id int(10) unsigned PK, fingerprint int(10) unsigned, account int(10) unsigned, ip varchar(16), realm int(10) unsigned, time timestamp, architecture varchar(16)?, cputype varchar(64)?, activecpus int(10) unsigned?, totalcpus int(10) unsigned?, pagesize int(10) unsigned?

*`?` = nullable, `PK` = primary key column.*

