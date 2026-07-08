<!-- provenance: boundary-bleed -->
# Log.Warden

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Log.Warden

## Purpose & Responsibilities

The `Log.Warden` unit implements the server-side core of the **Warden anti-cheat system** for the WoWVMaNGOS server. It manages the lifecycle of Warden sessions for individual players, handling cryptographic handshakes, module distribution, integrity scan orchestration, and penalty enforcement.

The `Warden` class acts as a base controller, with platform-specific subclasses (`WardenWin`, `WardenMac`) handling OS-specific initialization and scan logic. This unit is responsible for:
1.  **Secure Communication:** Establishing RC4-encrypted channels using keys derived from a shared secret (`BigNumber`) and validated via a challenge-response handshake.
2.  **Module Management:** Uploading custom Warden modules to clients that lack them, verifying successful loading, and transitioning from default "Maiev" mode to module-based mode.
3.  **Scan Orchestration:** Selecting, queuing, and transmitting integrity checks (scans) to the client, then parsing and validating the results.
4.  **Enforcement:** Detecting protocol violations (e.g., bad checksums, missing modules) or positive hack detections, and applying configured penalties (kick or ban).
5.  **Logging Interface:** Providing a specialized logging function (`Log::OutWarden`) that tags output with Warden-specific context (Account ID, Name, IP). Note that the broader `Log` class infrastructure (file management, color codes, general output methods) resides in the `Log.Main` unit; this unit only defines the Warden-specific entry point.

## Member-by-Member Behavior

### Logging Infrastructure

**`OutWarden`**
A static method in the `Log` unit (defined in this file) that serves as the primary logging interface for Warden operations. It accepts a `Warden` instance pointer to extract contextual information (Account Name, Account ID, Session IP) and prepends it to the log message.
-   It respects global log levels (`m_consoleLevel`, `m_fileLevel`) and specific Warden debug settings (`m_wardenDebug`).
-   It outputs to both the console (using `Log.Main/SetColor`, `Log.Main/OutTime`, and `shared_Util/vutf8printf`) and the `LOG_ANTICHEAT` log file (using `Log.Main/OutTimestamp`).
-   It uses `Warden/GetAccountId`, `Warden/GetAccountName`, and `Warden/GetSessionIP` to retrieve context from the Warden instance.

### Initialization & Cryptography

**`Warden`**
The constructor initializes the Warden session for a specific `WorldSession`.
-   It extracts session metadata (Account ID, GUID, Build, OS, Platform, IP, Username) from the `WorldSession` via `WorldSession.Main/GetAccountId`, `WorldSession.Main/GetGUID`, etc.
-   It generates RC4 encryption keys for input (client-to-server) and output (server-to-client) streams using `WardenKeyGenerator`. The generator derives two 16-byte keys from the shared `BigNumber` `K` (converted via `BigNumber/AsByteArray`).
-   It initializes the `RC4` ciphers (`m_inputCrypto`, `m_outputCrypto`) with these keys using `RC4/Init`.
-   It sets the initial XOR mask (`m_xor`) to the first byte of the input key.
-   It logs the initialization event and the generated hex keys (via `shared_Util/ByteArrayToHexStr`) for debugging purposes using `Log.Warden/OutWarden`.

**`RequestChallenge`**
Initiates the cryptographic handshake.
-   It selects a random `ChallengeResponseEntry` (CRK) from the associated `WardenModule`'s list using `shared_Util/urand`.
-   It constructs a `WARDEN_SMSG_HASH_REQUEST` packet containing the CRK's seed.
-   It sends the packet via `SendPacket` and starts the timeout clock via `BeginTimeoutClock`.

**`HandleChallengeResponse`**
Processes the client's response to the hash challenge.
-   It validates that a challenge was previously sent (`m_crk` is not null).
-   It verifies the client's reply matches the expected `reply` field in the CRK.
-   If valid, it re-initializes the RC4 ciphers with the specific `clientKey` and `serverKey` from the CRK using `RC4/Init`, establishing the secure channel for subsequent Warden traffic.
-   If invalid, it applies a penalty (Kick) via `ApplyPenalty`.

### Module Distribution

**`SendModuleUse`**
Informs the client that a specific Warden module should be used.
-   It sends a `WARDEN_SMSG_MODULE_USE` packet containing the module's hash, key, and binary size.
-   It stops the scan clock and starts the timeout clock.
-   It sets `m_maiev` to false, indicating the transition from the default "Maiev" mode to module-based mode.

**`SendModuleToClient`**
Transfers the actual module binary to the client.
-   It splits the module binary into chunks of up to 500 bytes.
-   It sends each chunk as a `WARDEN_SMSG_MODULE_CACHE` packet.
-   It sets `m_moduleSendPending` to true, indicating the server is waiting for the client to acknowledge receipt and loading.

### Scan Management

**`SelectScans`**
Delegates to `WardenScanMgr` to retrieve a list of random scans appropriate for the given `ScanFlags` and client build. It combines the provided flags with any platform-specific flags returned by `GetScanFlags()` (implemented in subclasses) using `WardenScan/operator|`.

**`EnqueueScans`**
Adds a vector of scans to the internal `m_enqueuedScans` queue. This allows batching scans before transmission.

**`RequestScans`**
Constructs and sends the scan request packet (`WARDEN_SMSG_CHEAT_CHECKS_REQUEST`).
-   It moves scans from `m_enqueuedScans` to `m_pendingScans` until buffer limits (`MaxRequest`, `MaxReply`) or configuration limits (`AC_WARDEN_NUM_SCANS`, retrieved via `World/getConfig#4`) are reached.
-   It builds the packet payload by calling `WardenScan/Build` on each selected scan.
-   For Windows clients not in Maiev mode, it includes a string table and a terminator byte.
-   It encrypts and sends the packet via `SendPacket`, then starts the timeout clock.

**`ReadScanResults`**
Processes the client's response to scan requests.
-   It iterates through `m_pendingScans`.
-   For each scan, it calls the scan's `WardenScan/Check` method with the response buffer.
-   If a scan returns `true` (indicating a hack detection), it calls `ApplyPenalty` and `LogPositiveToDB`.
-   If new scans were enqueued during the check process, it recursively calls `RequestScans` to send them immediately.

### Packet Handling & Protocol Logic

**`HandlePacket`**
The main dispatcher for incoming Warden packets.
-   It decrypts the packet using `DecryptData`.
-   It reads the opcode and validates it against the current state (e.g., ensuring only `HASH_RESULT` is received during a challenge).
-   It handles specific opcodes:
    -   `MODULE_MISSING`: Triggers `SendModuleToClient`.
    -   `MODULE_OK`: Triggers `RequestChallenge`.
    -   `CHEAT_CHECKS_RESULT`: Validates checksums (for Windows), calls `ReadScanResults`, and transitions state (e.g., starting scan clock or sending module use).
    -   `HASH_RESULT`: Calls `HandleChallengeResponse` and initializes the client if the build supports it.
    -   `MODULE_FAILED`: Kicks the session.
    -   Unknown opcodes trigger a penalty.

**`Update`**
Called periodically by the anti-cheat manager.
-   It processes any queued raw packet data from `m_packetDataQueue`, converting them to `ByteBuffer`s and passing them to `HandlePacket`.
-   It checks the timeout clock; if expired, it kicks the session.
-   If no scans are pending, it checks the scan clock. If expired, it selects new scans and requests them.

### Penalties & Enforcement

**`ApplyPenalty`**
Executes the configured punishment for a violation.
-   It determines the final penalty action (Kick or Ban) based on the scan's specific penalty setting or global configuration defaults (`World/getConfig#4`).
-   **Kick:** Calls `KickSession`.
-   **Ban:** Constructs a ban reason string and calls `World/BanAccount` via the messager system.
-   It logs the violation via `OutWarden` and broadcasts a GM announcement via `World/SendGMText`.

**`KickSession`**
Schedules a kick for the associated session via the `World` messager, ensuring thread safety by looking up the session via `World/FindSession` and calling `WorldSession.Main/KickPlayer`.

**`LogPositiveToDB`**
Currently a stub that logs the scan ID and penalty level via `OutWarden`. It is intended for database recording of positive detections but does not perform SQL operations in this unit.

### Utilities & Helpers

**`SendPacket` / `SendPacketDirect`**
Encapsulates a `ByteBuffer` into a `WorldPacket` (`SMSG_WARDEN_DATA`), encrypts it using `EncryptData`, and sends it to the client. `SendPacket` uses the messager for thread safety; `SendPacketDirect` sends immediately via `WorldSession.Main/SendPacket` (used during construction).

**`EncryptData` / `DecryptData`**
Wrappers around the `RC4` cipher's `RC4/UpdateData` method for output and input streams respectively.

**`BuildChecksum`**
Computes a 32-bit checksum from data by XORing four 32-bit segments of the SHA1 hash of the data (via `Generator.SHA1/ComputeFrom#4`). Used to verify integrity of scan result packets on Windows.

**`BeginTimeoutClock` / `StopTimeoutClock` / `TimeoutClockStarted`**
Manages the timer for client response timeouts. `BeginTimeoutClock` uses `shared_Util/getMSTime` and configuration `AC_WARDEN_CLIENT_RESPONSE_DELAY` (via `World/getConfig#4`). `TimeoutClockStarted` returns true if the clock is active.

**`BeginScanClock` / `StopScanClock`**
Manages the timer for periodic scan execution. `BeginScanClock` uses `shared_Util/getMSTime` and configuration `AC_WARDEN_SCAN_FREQUENCY` (via `World/getConfig#4`).

**`LoadScriptedScans`**
Static method that triggers the loading of scripted scans from both `WardenWin` and `WardenMac` units. It logs the count of newly loaded scans via `Log.Main/Out` and `WardenScanMgr/Count`.

## Cross-Unit Boundaries

### Collaboration with `Log.Main`
-   **Direction:** `Log.Warden` calls `Log.Main`.
-   **Context:** `OutWarden` relies on `Log.Main`'s `OutTime`, `OutTimestamp`, `SetColor`, and `ResetColor` methods to format and output log entries to the console and files. `LoadScriptedScans` uses `Log.Main/Out` for general logging.

### Collaboration with `WardenWin` and `WardenMac`
-   **Direction:** Bidirectional.
-   **Context:**
    -   `LoadScriptedScans` calls `WardenWin/LoadScriptedScans` and `WardenMac/LoadScriptedScans` to populate the scan registry.
    -   `SelectScans` is called by `WardenWin/Update` and `WardenMac/Update` to determine which scans to run.
    -   `EnqueueScans` is called by `WardenWin/LoadScriptedScans` and `WardenWin/ValidateEndScene` to batch scans.
    -   `TimeoutClockStarted` and `BeginScanClock` are called by the platform-specific `Update` methods to manage timing.
    -   `BuildChecksum` is called by `WardenWin/BuildFileHashInit`, `WardenWin/BuildLuaInit`, and `WardenWin/BuildTimingInit` to generate hashes for file integrity checks.

### Collaboration with `WardenScanMgr`
-   **Direction:** `Log.Warden` calls `WardenScanMgr`.
-   **Context:** `SelectScans` calls `WardenScanMgr/GetRandomScans` to retrieve the actual scan objects to be sent to the client. `LoadScriptedScans` calls `WardenScanMgr/Count` to report statistics.

### Collaboration with `WorldSession.Main`
-   **Direction:** `Log.Warden` calls `WorldSession.Main`.
-   **Context:** The `Warden` constructor extracts session details (Account ID, GUID, Build, OS, IP, Username) from the `WorldSession`. `SendPacket` and `KickSession` interact with `WorldSession` via the `World` messager to ensure thread-safe packet sending and kicking.

### Collaboration with `World`
-   **Direction:** `Log.Warden` calls `World`.
-   **Context:** `ApplyPenalty` calls `World/BanAccount` to enforce bans. `SendPacket` and `KickSession` use `World/GetMessager` and `World/FindSession` to route actions to the correct session thread. `BeginTimeoutClock` and `BeginScanClock` read configuration values via `World/getConfig#4`. `ApplyPenalty` also uses `World/SendGMText` for announcements.

### Collaboration with `shared_Util`
-   **Direction:** `Log.Warden` calls `shared_Util`.
-   **Context:** Uses `ByteArrayToHexStr` for logging keys and seeds. Uses `urand` for selecting random challenges. Uses `getMSTime` for clock management. Uses `vutf8printf` for console logging.

### Collaboration with `Crypto` (`RC4`, `BigNumber`, `SHA1`)
-   **Direction:** `Log.Warden` calls `Crypto`.
-   **Context:** `RC4` is used for packet encryption/decryption (`RC4/Init`, `RC4/UpdateData`). `BigNumber` is used to derive keys (`BigNumber/AsByteArray`). `SHA1` is used in `BuildChecksum` (`Generator.SHA1/ComputeFrom#4`) for integrity verification.

### Collaboration with `WardenKeyGenerator`
-   **Direction:** `Log.Warden` calls `WardenKeyGenerator`.
-   **Context:** The constructor uses `WardenKeyGenerator/WardenKeyGenerator` and `WardenKeyGenerator/Generate` to derive the input and output RC4 keys from the shared secret `K`.

### Collaboration with `Anticheat`
-   **Direction:** `Anticheat` calls `Log.Warden`.
-   **Context:** `Anticheat/LoadAnticheatData` calls `LoadScriptedScans` to initialize the scan library. `Anticheat/UpdateWardenSessions` calls `Update` to drive the state machine.

## Data Model

This unit does not directly interact with database tables. The `LogPositiveToDB` method is a stub that logs to the console/file but does not execute SQL queries. Any database persistence for Warden violations would occur in other units (e.g., via `World::BanAccount` which interacts with the `account_banned` table, though that interaction is abstracted away from this unit).

## Notable Implementation Details

1.  **Thread Safety via Messager:**
    Methods like `SendPacket`, `KickSession`, and `ApplyPenalty` (for bans) use `sWorld.GetMessager().AddMessage` to schedule actions on the main world thread. This is critical because Warden updates may occur on a worker thread (via `Anticheat::UpdateWardenSessions`), and direct access to `WorldSession` or `World` state from worker threads is unsafe.

2.  **RC4 Re-initialization:**
    The RC4 ciphers are initialized twice: once in the constructor with derived keys, and again in `HandleChallengeResponse` with keys from the specific Challenge-Response-Key (CRK) entry. This ensures that the encryption keys are unique per session and tied to the successful completion of the handshake.

3.  **Maiev Mode vs. Module Mode:**
    The boolean `m_maiev` tracks whether the client is using the default "Maiev" anti-cheat or a custom module.
    -   If `m_maiev` is true, the server skips sending module binaries and uses simpler scan protocols.
    -   If a module is available, the server sends `SendModuleUse`, then waits for `MODULE_OK` before proceeding to the challenge phase.
    -   This distinction affects packet structure (e.g., string tables are only included for Windows clients in non-Maiev mode).

4.  **Scan Queueing Logic:**
    `RequestScans` implements a complex queuing mechanism. It attempts to pack as many scans as possible into a single packet, respecting size limits (`MaxRequest`, `MaxReply`) and count limits. If a scan is too large to fit, it remains in the `m_enqueuedScans` queue for the next cycle. This prevents packet fragmentation and ensures efficient network usage.

5.  **Checksum Verification:**
    For Windows clients in module mode, `HandlePacket` verifies a checksum on `CHEAT_CHECKS_RESULT` packets. The checksum is computed by XORing four 32-bit words of the SHA1 hash of the payload. This protects against tampering with scan results in transit.

6.  **Timeout Handling:**
    Timeouts are managed by `m_timeoutClock` and `m_scanClock`. The `Update` method checks these clocks against the current time. Note that timeout checks are disabled in `_DEBUG` builds to prevent false positives during single-stepping debugging.

7.  **Stubbed Database Logging:**
    `LogPositiveToDB` is currently empty except for logging. This suggests that database recording of positive detections is either handled elsewhere or is a planned feature not yet implemented in this unit.

## Member Reference

**OutWarden**: Logs Warden-specific messages to console and file, prepending Account Name, ID, and IP. Respects log levels and debug settings.

**LoadScriptedScans**: Static method that loads scripted scans from `WardenWin` and `WardenMac` units and logs the count.

**Warden**: Constructor that initializes session metadata, derives RC4 keys from `BigNumber` `K`, and sets up encryption ciphers.

**RequestChallenge**: Sends a random hash challenge seed to the client and starts the timeout clock.

**HandleChallengeResponse**: Validates the client's hash response, re-initializes RC4 ciphers with CRK-specific keys, and applies penalty on failure.

**SendModuleUse**: Sends module hash/key/size to client, transitioning out of Maiev mode.

**SendModuleToClient**: Streams module binary to client in 500-byte chunks.

**SelectScans**: Retrieves random scans from `WardenScanMgr` based on flags and client build.

**EnqueueScans**: Adds scans to the internal queue for batched transmission.

**RequestScans**: Builds and sends scan request packets, respecting size/count limits, and manages the pending/enqueued scan queues.

**ReadScanResults**: Processes scan responses, calls `Check` on each scan, and applies penalties/log for positives.

**SendPacket**: Encrypts and sends a `ByteBuffer` to the client via the thread-safe `World` messager.

**SendPacketDirect**: Encrypts and sends a `ByteBuffer` directly to a `WorldSession` (used in constructor).

**KickSession**: Schedules a kick for the session via the `World` messager.

**DecryptData**: Decrypts input buffer using the RC4 input cipher.

**EncryptData**: Encrypts output buffer using the RC4 output cipher.

**BeginTimeoutClock**: Sets the timeout clock based on current time and configured delay.

**StopTimeoutClock**: Resets the timeout clock to zero.

**TimeoutClockStarted**: Returns true if the timeout clock is active.

**BeginScanClock**: Sets the scan clock based on current time and configured frequency.

**StopScanClock**: Resets the scan clock to zero.

**BuildChecksum**: Computes a 32-bit checksum by XORing segments of the SHA1 hash of the data.

**ApplyPenalty**: Executes kick or ban based on penalty type, logs the violation, and announces to GMs.

**HandlePacket**: Decrypts and dispatches incoming Warden packets, handling state transitions and errors.

**Update**: Processes queued packets, checks timeouts, and triggers new scan requests if the scan clock expires.

**LogPositiveToDB**: Stub method that logs scan ID and penalty; does not write to DB.

---

<!-- machine-true, projected from graph.json -->

## Map — Log.Warden

*Source:* Warden.cpp, Warden.hpp, Log.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| OutWarden | method | Log.Main/OutTime, Log.Main/OutTimestamp, Log.Main/ResetColor, Log.Main/SetColor, shared_Util/vutf8printf, Warden/GetAccountId, Warden/GetAccountName, Warden/GetSessionIP | WardenWin/InitializeClient, WardenWin/LoadScriptedScans, WardenWin/ValidateEndScene | — |
| LoadScriptedScans | method | Log.Main/Out, WardenMac/LoadScriptedScans, WardenScanMgr/Count, WardenWin/LoadScriptedScans | Anticheat/LoadAnticheatData | — |
| Warden | ctor | BigNumber/AsByteArray, RC4/Init, RC4/RC4#2, shared_Util/ByteArrayToHexStr, WardenKeyGenerator/Generate, WardenKeyGenerator/WardenKeyGenerator, WorldSession.Main/GetAccountId, WorldSession.Main/GetGameBuild, WorldSession.Main/GetGUID, WorldSession.Main/GetOS, WorldSession.Main/GetPlatform, WorldSession.Main/GetRemoteAddress, WorldSession.Main/GetUsername | WardenMac/WardenMac, WardenWin/WardenWin | — |
| RequestChallenge | method | ByteBuffer/append#5, ByteBuffer/ByteBuffer#4, ByteBuffer/operator<<#7, Errors/PrintStacktraceAndThrow, shared_Util/ByteArrayToHexStr, shared_Util/urand | — | — |
| HandleChallengeResponse | method | ByteBuffer/contents, ByteBuffer/rpos#2, ByteBuffer/wpos, RC4/Init | — | — |
| SendModuleUse | method | ByteBuffer/append#5, ByteBuffer/ByteBuffer#4, ByteBuffer/operator<<#10, ByteBuffer/operator<<#7, Digest/size, shared_Util/ByteArrayToHexStr | — | — |
| SendModuleToClient | method | ByteBuffer/append#5, ByteBuffer/ByteBuffer#4, ByteBuffer/operator<<#13, ByteBuffer/operator<<#7 | — | — |
| SelectScans | method | Warden/GetScanFlags, WardenScan/operator|, WardenScanMgr/GetRandomScans | WardenMac/Update, WardenWin/Update | — |
| EnqueueScans | method | — | WardenWin/LoadScriptedScans, WardenWin/ValidateEndScene | — |
| RequestScans | method | ByteBuffer/append#3, ByteBuffer/append#4, ByteBuffer/ByteBuffer, ByteBuffer/operator<<#7, ByteBuffer/wpos, Errors/PrintStacktraceAndThrow, WardenScan/Build, World/getConfig#4 | WardenMac/Update, WardenWin/Update | — |
| ReadScanResults | method | WardenScan/Check | — | — |
| SendPacket | method | ByteBuffer/append#3, ByteBuffer/contents, ByteBuffer/wpos, World/FindSession, World/GetMessager, WorldPacket/WorldPacket#4, WorldSession.Main/GetGUID, WorldSession.Main/SendPacket | WardenWin/InitializeClient | — |
| SendPacketDirect | method | ByteBuffer/append#3, ByteBuffer/contents, ByteBuffer/wpos, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| KickSession | method | World/FindSession, World/GetMessager, WorldSession.Main/GetGUID, WorldSession.Main/KickPlayer | — | — |
| DecryptData | method | RC4/UpdateData | — | — |
| EncryptData | method | RC4/UpdateData | — | — |
| BeginTimeoutClock | method | shared_Util/getMSTime, World/getConfig#4 | — | — |
| StopTimeoutClock | method | — | — | — |
| TimeoutClockStarted | method | — | WardenMac/Update, WardenWin/Update | — |
| BeginScanClock | method | shared_Util/getMSTime, World/getConfig#4 | WardenMac/Update, WardenWin/Update | — |
| StopScanClock | method | — | — | — |
| BuildChecksum | method | Generator.SHA1/ComputeFrom#4 | WardenWin/BuildFileHashInit, WardenWin/BuildLuaInit, WardenWin/BuildTimingInit | — |
| ApplyPenalty | method | World/BanAccount, World/getConfig#4, World/GetMessager, World/SendGMText | — | — |
| HandlePacket | method | ByteBuffer/contents, ByteBuffer/operator>>#12, ByteBuffer/operator>>#6, ByteBuffer/operator>>#9, ByteBuffer/rpos, ByteBuffer/rpos#2, ByteBuffer/size, ByteBuffer/wpos, Warden/InitializeClient | — | — |
| Update | method | ByteBuffer/from, shared_Util/getMSTime | Anticheat/UpdateWardenSessions, WardenMac/Update, WardenWin/Update | — |
| LogPositiveToDB | method | — | — | — |

---

<!-- verify: boundary-bleed | foreign: Log -->
