# WardenWin

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# WardenWin

`WardenWin` is the Windows-specific implementation of the Warden anti-cheat module within the `wowvmangos` server. It extends the base `Warden` class to handle client-side memory scanning, integrity verification, and system fingerprinting specific to the Windows operating system and the World of Warcraft Classic/TBC/WotLK clients.

Its primary responsibilities are:
1.  **Client Initialization:** Sending module-specific initialization packets to the client to configure Lua, File Hash, and Timing scan modules with correct memory offsets for the specific client build.
2.  **System Fingerprinting:** Reading the client's `SYSTEM_INFO` structure to determine CPU architecture, type, and processor counts, then persisting this data to the database for account linking and fraud detection.
3.  **Integrity Scanning:** Defining and managing a complex chain of scripted memory scans to detect:
    *   Hooked rendering functions (`EndScene`) commonly used by bots.
    *   Tampered game engine flags (`CWorld::enables`).
    *   Presence of known hypervisors (VirtualBox, ESXi) and proxy tools (Proxifier).
    *   Obfuscated assembly code indicative of botting frameworks (e.g., WRobot).
4.  **Timing Verification:** Monitoring client clock drift and hardware interaction timestamps to detect AFK hacks or clock manipulation.

This unit is only compiled for client builds newer than `CLIENT_BUILD_1_5_1`.

## Member-by-Member Behavior

### Initialization and Setup

**WardenWin**
The constructor initializes the `WardenWin` instance. It calls `WardenModuleMgr::GetWindowsModule` to retrieve the Windows-specific Warden module configuration and passes it to the base `Warden` constructor. It initializes internal state variables such as `m_wardenAddress`, `m_sysInfo`, and various boolean flags (`m_sysInfoSaved`, `m_proxifierFound`, etc.) to their default states. It is instantiated by `Anticheat::CreateWardenForInternal`.

**InitializeClient**
This method prepares the client for scanning by constructing and sending three distinct initialization packets:
1.  **Lua Module:** Configured via `BuildLuaInit` to use the `GetText` function offset.
2.  **File Hash Module:** Configured via `BuildFileHashInit` with offsets for `Open`, `Size`, `Read`, and `Close` file operations.
3.  **Timing Module:** Configured via `BuildTimingInit` with the `TickCount` offset.

It uses `GetClientOffets` to resolve the correct memory offsets for the connected client's build. If offsets are found, it concatenates the three packets and sends them via `Log.Warden::SendPacket`. It sets `m_offsetsInitialized` to true upon success.

**BuildLuaInit**, **BuildFileHashInit**, **BuildTimingInit**
These helper methods construct the binary payloads for the respective module initialization packets. They follow a similar pattern:
1.  Create a `ByteBuffer` with a header containing the message type (`WARDEN_SMSG_MODULE_INITIALIZE`), payload length, and a placeholder for the checksum.
2.  Append module-specific parameters (module name, function offsets, calling convention flags).
3.  Calculate the checksum over the payload using `Log.Warden::BuildChecksum`.
4.  Write the checksum into the placeholder position in the buffer.

### Scripted Scan Definition

**LoadScriptedScans**
This static method defines the core anti-cheat logic by registering numerous `WindowsScan` objects with `WardenScanMgr::AddWindowsScan`. These scans form a dependency graph where the completion of one scan often triggers the next. Key scans include:

*   **Warden Locate Chain:** A multi-stage scan (`Warden locate` -> `Intermediate sysinfo locate` -> `Sysinfo locate`) that reads the Warden module address from client memory, then reads the `SYSTEM_INFO` structure. It validates that the architecture is x86 and the processor type is valid for WoW Classic/TBC/WotLK.
*   **CWorld::enables Check:** Reads the `CWorld::enables` bitmask from client memory. It verifies that required rendering flags (Terrain, Doodads, Water, etc.) are set and prohibited debugging flags (Wireframe, Normals, Tris, etc.) are unset.
*   **Anti-AFK/Timing Check:** Reads `CSimpleTop::m_eventTime` (last hardware action) and compares it against the current game tick count. It detects if the last hardware action occurred in the future relative to the game clock.
*   **Hypervisor Detection:** Uses HMAC-SHA1 hashed device names to check for the presence of VirtualBox (`VBoxGuest`) and VMware ESXi (`vmmemctl`) drivers.
*   **Warden Memory Read Check:** Verifies the integrity of the Warden module's own memory reading function by searching for a specific byte pattern in client memory.
*   **EndScene Hook Detection:** A complex 4-stage chain (`EndScene locate stage 1` through `4`) that dereferences pointers to find the `EndScene` function address. Once found, it reads the first 16 bytes of the function. `ValidateEndScene` analyzes this code for NOP sleds, INT3 breakpoints, or JMP hooks. If a JMP is found, it reads the destination code and runs `ValidateEndSceneHook` to check for obfuscation patterns typical of WRobot.
*   **Proxifier Check:** Checks for the presence of `prxdrvpe.dll`.
*   **Click-to-Move Check:** Reads the click-to-move position coordinates. If non-zero, it marks the client as having used click-to-move.

### Runtime Updates and Data Persistence

**Update**
Called periodically by the game loop. It first calls the base `Warden::Update`.
1.  If not initialized, it requests Maiev scans if applicable.
2.  If system info has been read (`m_sysInfo.lpMaximumApplicationAddress` is non-zero) but not yet saved (`!m_sysInfoSaved`), it performs the following:
    *   Calculates the number of active CPUs from the processor mask.
    *   Inserts a record into the `system_fingerprint_usage` table using `LogsDatabase`. The record includes the account ID, IP, realm, architecture string, CPU type string, active/total CPU counts, and page size.
    *   Sets `m_sysInfoSaved` to true.
    *   If a character enumeration packet was held back (`m_charEnum`), it sends it now via `World::GetMessager` to ensure the client doesn't proceed until Warden is fully initialized.

**SetCharEnumPacket**
Stores a `WorldPacket` intended for the client. If system info is already saved, it sends the packet immediately. Otherwise, it stores it in `m_charEnum` to be sent later during the `Update` cycle once fingerprinting is complete. This ensures the client cannot bypass Warden initialization by proceeding to character selection too quickly.

**GetPlayerInfo**
Populates output strings with human-readable diagnostic information for logging or admin inspection:
*   **Clock:** Last hardware action time, client time, idle duration, and age of the last check.
*   **Fingerprint:** Architecture, CPU type, page size, active/total CPUs.
*   **Hypervisors:** Names of detected hypervisors.
*   **Renderer:** Detected rendering API (OpenGL/Direct3D) and EndScene address if found.
*   **Proxifier:** Status of Proxifier detection.

### Helper Functions

**GetClientOffets**
Looks up the `ClientOffsets` structure for a given client build number from a static array. Returns `nullptr` if the build is unsupported.

**ArchitectureString**
Converts a Windows processor architecture integer (e.g., 0 for x86, 9 for x64) into a human-readable string.

**CPUTypeAndRevision**
Converts Windows processor type and revision integers into a descriptive string (e.g., "Pentium (i586) Model: X Stepping: Y").

**DeobfuscateAsm**
A heuristic assembler parser that strips obfuscation instructions from a byte vector. It removes NOPs, self-referential MOV/XCHG instructions, and jumps over junk data. It recursively processes the code until no more changes occur. This is used to clean up hooked function prologues before analysis.

**ValidateEndSceneHook**
Takes a snippet of assembly code, runs `DeobfuscateAsm` on it, and checks for signatures of botting frameworks. Specifically, it looks for a `pushfd` (`0x9C`) followed by `pushad` (`0x60`), which is a signature of WRobot's EndScene hook. It also flags hooks where the original code size is 200 bytes but the deobfuscated size is less than 15 bytes.

**ValidateEndScene**
Analyzes the raw bytes of the `EndScene` function. It skips leading NOPs and checks for:
*   `INT3` (`0xCC`) breakpoints.
*   `JMP` (`0xE9`) instructions. If a JMP is found, it calculates the absolute destination address and enqueues a new scan to read and validate the code at that destination using `ValidateEndSceneHook`.

**GetScanFlags**
Returns a bitmask of `ScanFlags` indicating the current state of the Warden instance (Windows, Maiev, OffsetsInitialized).

## Cross-Unit Boundaries

*   **Warden:** `WardenWin` inherits from `Warden`. It calls `Warden::Update`, `Warden::GetModule`, `Warden::GetXor`, `Warden::HasUsedClickToMove`, and `Warden::SetHasUsedClickToMove`. It overrides `InitializeClient`, `GetScanFlags`, `SetCharEnumPacket`, and `GetPlayerInfo`.
*   **WardenModuleMgr:** Calls `GetWindowsModule` in the constructor to load the Windows-specific module configuration.
*   **WardenScanMgr:** Calls `AddWindowsScan` extensively in `LoadScriptedScans` to register all defined scans.
*   **Log.Warden:** Uses `OutWarden` for detailed anti-cheat logging, `EnqueueScans` to trigger dependent scans, `BuildChecksum` for packet integrity, `SendPacket` to transmit initialization data, `BeginScanClock`, `RequestScans`, `SelectScans`, and `TimeoutClockStarted` for scan lifecycle management.
*   **Log.Main/Out:** Used in `ValidateEndSceneHook` for debug logging of deobfuscation results.
*   **Database/LogsDatabase:** `Update` uses `BeginTransaction`, `CreateStatement`, and `CommitTransaction` to insert fingerprint data.
*   **World/WorldSession:** `Update` and `SetCharEnumPacket` use `World::FindSession` and `WorldSession::SendPacket` via `World::GetMessager` to safely send packets to the client session.
*   **Crypto/Hash/HMACSHA1:** Used in `LoadScriptedScans` to generate HMAC-SHA1 digests for hypervisor device name checks and pattern matching.
*   **shared_Util:** Uses `getMSTime` for server-side timestamps and `rand32` for generating seeds for cryptographic hashes.
*   **Errors:** Calls `PrintStacktraceAndThrow` in `ClientRenderingApiToString` if an invalid rendering API is encountered (assertion failure).

## Data Model

`WardenWin` interacts with one database table:

*   **`system_fingerprint_usage`**: Used in `Update` to store hardware fingerprints for accounts.
    *   Columns written: `fingerprint` (always 0), `account`, `ip`, `realm`, `architecture`, `cputype`, `activecpus`, `totalcpus`, `pagesize`.
    *   The `id` column is auto-incremented by the database.
    *   This data links hardware configurations to accounts and IPs, aiding in the detection of account sharing or ban evasion.

## Notable Implementation Details

*   **Build-Specific Offsets:** The `Offsets` array contains hardcoded memory addresses for various client builds (4878 to 6141). If a client connects with a build not in this list, `GetClientOffets` returns `nullptr`, and many scans will fail to execute or initialize correctly.
*   **EndScene Pointer Chain:** Locating the `EndScene` function requires dereferencing a chain of pointers (`g_theGxDevicePtr` -> `OfsDevice2` -> `OfsDevice3` -> `OfsDevice4`). This is implemented as a series of dependent scans that enqueue the next stage upon successful completion.
*   **Obfuscation Detection:** `DeobfuscateAsm` is a custom, lightweight disassembler designed specifically to strip common obfuscation techniques used by botting frameworks. It does not fully decode instructions but removes noise to reveal the underlying structure.
*   **Delayed Character Enum:** The `SetCharEnumPacket` mechanism ensures that the client cannot proceed to the character selection screen until the Warden module has successfully initialized and fingerprinted the system. This prevents clients from bypassing Warden by disconnecting or timing out before initialization completes.
*   **Hypervisor Detection:** Instead of directly checking for driver names (which could be hidden), it uses HMAC-SHA1 hashes of the device names with a random seed. This makes it harder for cheats to patch out the check by simply looking for string comparisons.
*   **Timing Check Nuance:** The anti-AFK check ignores failures in the timing module itself (which can happen under Wine or VMs) but strictly enforces that the last hardware action time cannot be in the future relative to the game clock.

## Member Reference

**GetClientOffets**: Static function that looks up memory offsets for a given client build from a predefined array.

**ArchitectureString**: Static function that converts a Windows processor architecture integer to a string.

**CPUTypeAndRevision**: Static function that converts Windows processor type and revision integers to a descriptive string.

**DeobfuscateAsm**: Static function that strips obfuscation instructions (NOPs, self-referential MOV/XCHG, jumps) from a byte vector.

**ValidateEndSceneHook**: Static function that checks deobfuscated assembly code for signatures of botting frameworks (specifically WRobot).

**LoadScriptedScans**: Static method that registers all Windows-specific Warden scans with the scan manager, including system info, integrity, hypervisor, and hook detection scans.

**BuildLuaInit**: Method that constructs the initialization packet for the Lua scan module.

**BuildFileHashInit**: Method that constructs the initialization packet for the File Hash scan module.

**BuildTimingInit**: Method that constructs the initialization packet for the Timing scan module.

**WardenWin**: Constructor that initializes the WardenWin instance and retrieves the Windows module configuration.

**ClientRenderingApiToString**: Static function that converts a `ClientRenderingApi` enum value to a string.

**ValidateEndScene**: Method that analyzes the raw bytes of the `EndScene` function for hooks and enqueues further validation if necessary.

**GetScanFlags**: Method that returns the current scan flags bitmask for the instance.

**InitializeClient**: Method that sends initialization packets to the client for Lua, File Hash, and Timing modules.

**Update**: Method that handles periodic updates, including saving system fingerprints to the database and sending delayed character enumeration packets.

**SetCharEnumPacket**: Method that stores or immediately sends a character enumeration packet, ensuring Warden initialization is complete first.

**GetPlayerInfo**: Method that populates output strings with diagnostic information about the client's system and Warden status.

---

<!-- machine-true, projected from graph.json -->

## Map — WardenWin

*Source:* WardenWin.cpp, WardenWin.hpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GetClientOffets | function | — | — | — |
| ArchitectureString | function | — | — | — |
| CPUTypeAndRevision | function | — | — | — |
| DeobfuscateAsm | function | — | — | — |
| ValidateEndSceneHook | function | Log.Main/Out | — | — |
| LoadScriptedScans | method | ByteBuffer/append#5, ByteBuffer/operator<<#10, ByteBuffer/operator<<#7, ByteBuffer/read, Digest/size#2, Errors/PrintStacktraceAndThrow, Generator.HMACSHA1/Generator#2, Generator.HMACSHA1/GetDigest, Generator.HMACSHA1/UpdateData, Generator.HMACSHA1/UpdateData#4, Log.Warden/EnqueueScans, Log.Warden/OutWarden, shared_Util/getMSTime, shared_Util/rand32, Warden/GetModule, Warden/GetXor, Warden/HasUsedClickToMove, Warden/SetHasUsedClickToMove, WardenScan/operator|, WardenScanMgr/AddWindowsScan | Log.Warden/LoadScriptedScans | — |
| BuildLuaInit | method | ByteBuffer/append#4, ByteBuffer/ByteBuffer#4, ByteBuffer/contents, ByteBuffer/operator<<#10, ByteBuffer/operator<<#13, ByteBuffer/operator<<#7, ByteBuffer/operator=, ByteBuffer/wpos, ByteBuffer/wpos#2, Log.Warden/BuildChecksum | — | — |
| BuildFileHashInit | method | ByteBuffer/append#4, ByteBuffer/ByteBuffer#4, ByteBuffer/contents, ByteBuffer/operator<<#10, ByteBuffer/operator<<#13, ByteBuffer/operator<<#7, ByteBuffer/operator=, ByteBuffer/wpos, ByteBuffer/wpos#2, Log.Warden/BuildChecksum | — | — |
| BuildTimingInit | method | ByteBuffer/append#4, ByteBuffer/ByteBuffer#4, ByteBuffer/contents, ByteBuffer/operator<<#10, ByteBuffer/operator<<#13, ByteBuffer/operator<<#7, ByteBuffer/operator=, ByteBuffer/wpos, ByteBuffer/wpos#2, Log.Warden/BuildChecksum | — | — |
| WardenWin | ctor | Log.Warden/Warden, WardenModuleMgr/GetWindowsModule | Anticheat/CreateWardenForInternal | — |
| ClientRenderingApiToString | function | Errors/PrintStacktraceAndThrow | — | — |
| ValidateEndScene | method | ByteBuffer/read, Log.Warden/EnqueueScans, Log.Warden/OutWarden | — | — |
| GetScanFlags | method | WardenScan/operator| | — | — |
| InitializeClient | method | ByteBuffer/append#3, ByteBuffer/ByteBuffer, ByteBuffer/ByteBuffer#4, ByteBuffer/wpos, Errors/PrintStacktraceAndThrow, Log.Warden/OutWarden, Log.Warden/SendPacket | — | — |
| Update | method | ByteBuffer/clear, ByteBuffer/empty, Database/BeginTransaction, Database/CommitTransaction, Database/CreateStatement, Log.Warden/BeginScanClock, Log.Warden/RequestScans, Log.Warden/SelectScans, Log.Warden/TimeoutClockStarted, Log.Warden/Update, SqlPreparedStatement/Execute#2, SqlStatement/addString#2, SqlStatement/addUInt32, SqlStatementID/SqlStatementID, World/FindSession, World/GetMessager, WorldSession.Main/GetGUID, WorldSession.Main/SendPacket | — | system_fingerprint_usage |
| SetCharEnumPacket | method | World/FindSession, World/GetMessager, WorldPacket/operator=, WorldSession.Main/GetGUID, WorldSession.Main/SendPacket | — | — |
| GetPlayerInfo | method | WorldTimer/getMSTimeDiffToNow | — | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `system_fingerprint_usage`: id int(10) unsigned PK, fingerprint int(10) unsigned, account int(10) unsigned, ip varchar(16), realm int(10) unsigned, time timestamp, architecture varchar(16)?, cputype varchar(64)?, activecpus int(10) unsigned?, totalcpus int(10) unsigned?, pagesize int(10) unsigned?

*`?` = nullable, `PK` = primary key column.*

