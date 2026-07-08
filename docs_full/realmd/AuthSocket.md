# AuthSocket

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AuthSocket

**AuthSocket** is the core network handler for the `realmd` authentication service. It manages the lifecycle of a single TCP connection from a World of Warcraft client, handling the initial handshake, SRP6-based password authentication, two-factor authentication (PIN/TOTP), geographical locking, client version verification, and patch file distribution. It acts as the gatekeeper, validating credentials against the `account` database and determining which realms the user is permitted to access before handing off control to the game world server.

## Purpose & Responsibilities

The primary responsibility of `AuthSocket` is to securely authenticate a client connecting to the authentication server. It implements the following workflows:
1.  **Connection Management:** Accepts a raw `AsyncSocket`, sets up session timeouts, and manages the asynchronous I/O loop for reading opcodes and payloads.
2.  **SRP6 Authentication:** Performs the Secure Remote Password protocol exchange (`_HandleLogonChallenge`, `_HandleLogonProof`) to verify passwords without transmitting them in plaintext.
3.  **Security Checks:** Enforces IP bans, account bans, email verification requirements, IP locking, and geographical locking.
4.  **Two-Factor Authentication:** Supports static PINs, TOTP (Time-based One-Time Password), and geographic unlock PINs.
5.  **Client Validation:** Verifies the client build number against allowed versions and checks for modified clients via integrity hashes.
6.  **Patch Distribution:** If the client version is outdated but a patch exists, it initiates a file transfer (`XFER`) to update the client.
7.  **Realm Listing:** Upon successful authentication, it queries character counts and realm statuses to send the realm list to the client.

## Member-by-Member Behavior

### Initialization and Lifecycle

*   **`AuthSocket`**: Constructs the socket wrapper, storing the remote IP address immediately by calling `AsyncSocket.Main/GetRemoteIpString`.
*   **`Start`**: Schedules a session duration timeout (configurable via `MaxSessionDuration`) to auto-close idle connections. It kicks off the I/O loop by calling `DoRecvIncomingData`. It logs the start of the session.
*   **`~AuthSocket`**: Cleans up resources, cancels the session timer via `TimerHandle/Cancel`, closes the socket, and logs the disconnection.
*   **`CloseSocket`**: Explicitly closes the underlying `AsyncSocket` by calling `AsyncSocket._posix/CloseSocket`.

### I/O Dispatching

*   **`DoRecvIncomingData`**: The central dispatch loop. It reads the 4-byte opcode from the socket using `AsyncSocket._posix/Read`. Based on the opcode, it validates that the current state (`m_status`) permits the command. If valid, it invokes the specific handler (e.g., `_HandleLogonChallenge`). If the opcode is unknown or the state is invalid, it logs an error. On success, it recursively calls itself to wait for the next command. It handles network errors via `NetworkError/GetErrorType` and `NetworkError/ToString`.

### Authentication Flow (Logon)

*   **`_HandleLogonChallenge`**:
    *   Reads the challenge packet containing username, client build, OS, platform, and locale using `AsyncSocket._posix/Read`.
    *   Checks `ip_banned` table for IP bans via `Database/PQuery`.
    *   Queries the `account` table for user details via `Database/PQuery`.
    *   Enforces email verification if configured via `Config/GetBoolDefault` and `Config/GetIntDefault`.
    *   Checks for IP locks (`IP_LOCK` flag). If locked and the IP doesn't match, it may reject or prompt for PIN depending on 2FA settings.
    *   Validates SRP verifier (`v`) and salt (`s`) from the database using `SRP6/SetVerifier` and `SRP6/SetSalt`.
    *   Generates the SRP server public ephemeral (`B`) using `SRP6/CalculateHostPublicEphemeral` and sends it back to the client along with the prime (`SRP6/GetPrime`), generator (`SRP6/GetGeneratorModulo`), and salt.
    *   If PIN authentication is required (due to IP lock or `ALWAYS_ENFORCE`), it generates a random grid seed and server salt, sending these to the client.
    *   Loads account security levels for realm access control by calling `LoadAccountSecurityLevels`.
    *   Transitions state to `STATUS_LOGON_PROOF`.
    *   Writes the response using `AsyncSocket._posix/Write`.

*   **`_HandleLogonProof`**:
    *   Reads the proof packet using `AsyncSocket._posix/Read`. If `securityFlags` indicate PIN data is present, it reads the additional `PINData` structure.
    *   Delegates to `_HandleLogonProof__PostRecv`.

*   **`_HandleLogonProof__PostRecv`**:
    *   Validates the client build against known versions via `RealmList/FindBuildInfo`. If invalid, delegates to `_HandleLogonProof__PostRecv_HandleInvalidVersion`.
    *   Calculates the SRP session key and proof using `SRP6/CalculateSessionKey`, `SRP6/HashSessionKey`, and `SRP6/CalculateProof`.
    *   Verifies the PIN if prompted:
        *   For `FIXED_PIN`, verifies against the stored hash via `VerifyPinData`.
        *   For `TOTP`, generates expected codes for a window of intervals (-2 to +2) via `GenerateTotpPin` and verifies.
        *   For `GEO_LOCK`, verifies against the generated `geolock_pin` via `VerifyPinData`.
    *   Verifies the SRP proof (password correctness) using `SRP6/Proof`.
    *   If proof fails but version check passes, it logs a "modified client" attempt.
    *   If proof succeeds:
        *   Clears `geolock_pin` if it was used via `Database/PExecute`.
        *   Performs `GeographicalLockCheck`. If the location changed significantly and geo-locking is enabled, it generates a new PIN, emails it to the user, and rejects the login with `WOW_FAIL_PARENTCONTROL`.
        *   Updates the `account` table with the new session key, last IP, login time, and resets failed login count via `Database/PExecute`.
        *   Sends the final success response via `GenerateLogonProofResponse`.
        *   Transitions state to `STATUS_AUTHED`.
    *   If proof fails (wrong password):
        *   Increments `failed_logins` via `Database/PExecute`.
        *   If `failed_logins` exceeds `WrongPass.MaxCount`, it bans the account or IP based on `WrongPass.BanType` via `Database/PExecute`.
        *   Sends failure response.

*   **`_HandleLogonProof__PostRecv_HandleInvalidVersion`**:
    *   Checks if a patch file exists for the client's build and locale using `FileSystem/ToAbsolutePath` and `FileSystem/TryOpenFileReadonly`.
    *   If found, calculates the MD5 hash via `ClientPatchCache/GetOrCalculateHash` and initiates the patch transfer (`CMD_XFER_INITIATE`), transitioning state to `STATUS_PATCH`.
    *   If not found, sends `WOW_FAIL_VERSION_INVALID`.
    *   Writes responses using `AsyncSocket._posix/Write`.

*   **`GenerateLogonProofResponse`**: Constructs the final success packet using `ByteBuffer/ByteBuffer`, adjusting fields based on the client build version (pre-2.0.3, pre-2.4.0, or modern).

### Reconnection Flow

*   **`_HandleReconnectChallenge`**:
    *   Similar to logon challenge but uses the existing `sessionkey` from the database instead of performing a full SRP exchange.
    *   Retrieves `sessionkey` and account ID from `account` via `Database/PQuery`.
    *   Sets the strong session key in the SRP object using `SRP6/SetStrongSessionKey`.
    *   Sends a random reconnect proof value to the client using `BigNumber/SetRand` and `AsyncSocket._posix/Write`.
    *   Transitions state to `STATUS_RECON_PROOF`.

*   **`_HandleReconnectProof`**:
    *   Reads the client's reconnect proof using `AsyncSocket._posix/Read`.
    *   Computes a SHA1 hash of the username, client random, server random, and session key using `Generator.SHA1/Generator`, `Generator.SHA1/UpdateData`, and `Generator.SHA1/GetDigest`.
    *   Compares this hash with the client's proof.
    *   If valid, verifies the client version via `VerifyVersion` and transitions to `STATUS_AUTHED`.
    *   If invalid, closes the socket.
    *   Writes responses using `AsyncSocket._posix/Write`.

### Realm List and Security

*   **`_HandleRealmList`**:
    *   Throttles requests using `MinRealmListDelay` via `Config/GetIntDefault`.
    *   Updates the global realm list if needed via `RealmList/UpdateIfNeed`.
    *   Calls `LoadRealmlistAndWriteIntoBuffer` to construct the packet.
    *   Sends the realm list to the client using `AsyncSocket._posix/Write`.
    *   Skips initial bytes using `AsyncSocket.Main/ReadSkip`.

*   **`LoadRealmlistAndWriteIntoBuffer`**:
    *   Iterates through all realms via `RealmList/Instance`, `RealmList/begin`, `RealmList/end`, and `RealmList/size`.
    *   Queries `realmcharacters` for the character count for this account on each realm via `Database/PQuery`.
    *   Determines realm visibility based on client build compatibility and account security level (`GetSecurityOn`).
    *   Formats the realm data (name, IP, population, flags) according to the client build version using `RealmList/FindBuildInfo`, `RealmList/GetAddressForClient`, and `RealmList/GetRealmCategoryIdByBuildAndZone`.
    *   Constructs the packet using `ByteBuffer/operator<<`.

*   **`GetSecurityOn`**: Returns the GM/security level for a specific realm, falling back to the default level if no realm-specific override exists.

*   **`LoadAccountSecurityLevels`**: Queries `account_access` via `Database/PQuery` to populate the security level map for the authenticated account.

*   **`GeographicalLockCheck`**:
    *   Compares the current IP's geolocation with the last known IP's geolocation using the `geoip` table via `Database/PQuery`.
    *   Returns `true` if the city or country (based on `GEO_CITY` or `GEO_COUNTRY` flags) differs, indicating a potential unauthorized access from a different location.
    *   Checks configuration via `Config/GetBoolDefault`.

### Patch Transfer

*   **`_HandleXferAccept`**: Initiates the patch file transfer by calling `InitAndHandOverControlToPatchHandler`. Logs the action.
*   **`_HandleXferResume`**: Allows resuming a patch download from a specific byte offset. Reads the offset using `AsyncSocket._posix/Read`, seeks the file using `FileHandle/Seek`, and initiates transfer via `InitAndHandOverControlToPatchHandler`.
*   **`_HandleXferCancel`**: Logs cancellation; the socket closes implicitly.
*   **`InitAndHandOverControlToPatchHandler`**: Sets up the initial chunk structure and starts the recursive transfer loop via `RepeatInternalXferLoop`. Throws errors via `Errors/PrintStacktraceAndThrow` if assertions fail.
*   **`RepeatInternalXferLoop`**: Reads chunks from the patch file using `FileHandle/ReadSync` and writes them to the socket using `AsyncSocket._posix/Write`. Recursively calls itself until the file is exhausted or an error occurs. Handles errors via `NetworkError/ToString`.

### Utilities

*   **`VerifyPinData`**: Validates a PIN entered via the grid interface. It remaps the grid based on the server seed, converts the PIN to bytes, and verifies the SHA1 hash against the client-provided hash using `Generator.SHA1/Generator`, `Generator.SHA1/UpdateData`, and `Generator.SHA1/GetDigest`. Uses `BigNumber` for hash comparisons.
*   **`GenerateTotpPin`**: Generates a TOTP code for a given secret and time interval, supporting a window of validity. Uses `Base32/Decode` and `Generator.HMACSHA1/Generator`, `Generator.HMACSHA1/UpdateData`, `Generator.HMACSHA1/GetDigest`. Logs via `Log.Main/Out`.
*   **`VerifyVersion`**: Checks the client's integrity hash against known good hashes for the build. If `StrictVersionCheck` is disabled (via `Config/GetBoolDefault`), it returns true. Uses `Generator.SHA1/Generator`, `Generator.SHA1/UpdateData`, `Generator.SHA1/GetDigest` and `RealmList/FindBuildInfo#2`.
*   **`GetRemoteIpString`**: Returns the cached remote IP address. Called by `realmd_Main/main`.

## Cross-Unit Boundaries

*   **`AsyncSocket`**: `AuthSocket` wraps an `AsyncSocket` instance (`m_socket`). It calls `Read`, `Write`, `CloseSocket`, `GetRemoteIpString`, and `ReadSkip` to manage the TCP connection.
*   **`Config`**: Reads configuration values such as `MaxSessionDuration`, `ReqEmailVerification`, `WrongPass.MaxCount`, `GeoLocking`, `StrictVersionCheck`, `PatchesDir`, `SendMail`, `SendGridKey`, `GeolockGUID`, `MailFrom`, and `MinRealmListDelay`.
*   **`Database`**: Executes queries against the `LoginDatabase` to fetch account details, check bans, update session keys, and load security levels. Uses `PQuery`, `PExecute`, and `escape_string`.
*   **`RealmList`**: Accesses the global realm list singleton to retrieve realm information and update it if necessary. Uses `Instance`, `UpdateIfNeed`, `FindBuildInfo`, `GetAddressForClient`, `GetRealmCategoryIdByBuildAndZone`, `begin`, `end`, and `size`.
*   **`SRP6`**: Uses the `SRP6` class to perform cryptographic operations for password authentication (setting verifiers, calculating ephemerals, session keys, and proofs).
*   **`BigNumber`**: Used for handling large integers in SRP calculations and PIN verification.
*   **`ByteBuffer`**: Used for constructing and parsing network packets.
*   **`Log`**: Logs authentication events, errors, and debug information.
*   **`ClientPatchCache`**: Calculates MD5 hashes for patch files.
*   **`FileSystem`**: Opens and reads patch files from disk.
*   **`Generator.SHA1` / `Generator.HMACSHA1`**: Used for hashing in PIN verification, TOTP generation, and version checking.
*   **`Base32`**: Decodes TOTP secrets.
*   **`MailerService` / `SendgridMail`**: Sends emails containing geographic unlock PINs if geo-locking triggers.
*   **`TimerHandle`**: Used to cancel the session duration timeout.
*   **`NetworkError`**: Used to inspect and stringify network errors.
*   **`FileHandle`**: Used to read patch files and seek within them.
*   **`Errors`**: Used to print stack traces and throw exceptions on critical failures.
*   **`Digest`**: Used to get the size of hash digests.
*   **`ReadableBuffer`**: Used to wrap data for reading.
*   **`IpEndpoint`**: Used to convert IP endpoints to strings.
*   **`Common`**: Used to get locale names.
*   **`shared_Util`**: Used for random number generation (`rand32`, `urand`).

## Data Model

`AuthSocket` interacts with the following database tables:

*   **`account`**:
    *   Used to retrieve user credentials (`v`, `s`), security settings (`locked`, `security`), contact info (`email`), and login stats (`last_ip`, `failed_logins`, `joindate`).
    *   Updated upon successful login to store the new `sessionkey`, `last_ip`, `last_login`, `locale`, `os`, `platform`, and reset `failed_logins`.
    *   Updated to clear or set `geolock_pin` during geographic locking.
*   **`account_banned`**:
    *   Checked to determine if an account is currently banned (`active` = 1).
    *   Inserted into if an account exceeds the maximum failed login attempts (autoban).
*   **`ip_banned`**:
    *   Checked at the start of authentication to block banned IPs.
    *   Inserted into if an IP is autobanned due to excessive failed logins.
*   **`account_access`**:
    *   Queried to load GM/security levels for the account, both globally and per-realm.
*   **`realmcharacters`**:
    *   Queried to get the number of characters (`numchars`) for the account on each realm, displayed in the realm list.
*   **`geoip`**:
    *   Queried to resolve IP addresses to geoname IDs for geographic locking checks.

## Notable Implementation Details

*   **State Machine**: `AuthSocket` uses an internal `eStatus` enum to enforce strict ordering of commands. For example, `CMD_AUTH_LOGON_PROOF` is only accepted if the status is `STATUS_LOGON_PROOF`. This prevents replay attacks or out-of-order packets.
*   **Asynchronous I/O**: All network reads and writes are asynchronous. Callbacks are used to chain operations. This requires careful management of `shared_ptr` to keep the `AuthSocket` alive during pending I/O operations.
*   **SQL Injection Prevention**: User inputs like usernames are escaped using `LoginDatabase.escape_string` before being inserted into SQL queries. IP addresses are used directly in queries but are validated by the socket layer.
*   **Geographic Locking**: This feature is complex. It compares the geolocation of the current IP with the last known IP. If they differ and the account is geo-locked, it generates a new PIN, emails it to the user, and blocks login until the PIN is provided. This happens *after* password verification to prevent enumeration attacks.
*   **Patch Transfer**: The patch transfer is handled by recursively reading chunks from the file and writing them to the socket. This is a blocking-style operation within the async framework, relying on the callback to trigger the next read.
*   **Version Checking**: The server maintains a list of allowed client builds. If a client connects with an unsupported build, it may be offered a patch. If no patch is available, the connection is rejected. Strict version checking also verifies the client's integrity hash to detect modifications.
*   **PIN Grid Remapping**: The PIN grid is randomized for each login attempt. The server generates a seed and sends it to the client. The client remaps the grid and enters the PIN positions. The server performs the same remapping to verify the input.

## Member Reference

*   **AuthSocket**: Constructor that initializes the socket wrapper and caches the remote IP address.
*   **Start**: Method that schedules a session timeout and begins the asynchronous I/O loop.
*   **GetRemoteIpString**: Inline method that returns the cached remote IP address string.
*   **~AuthSocket**: Destructor that cleans up timers, closes the socket, and logs disconnection.
*   **GetSecurityOn**: Method that retrieves the security level for a specific realm, falling back to the default.
*   **DoRecvIncomingData**: Method that reads the next opcode, validates the state, and dispatches to the appropriate handler.
*   **GenerateLogonProofResponse**: Method that constructs the final SRP6 success packet based on the client build.
*   **_HandleLogonChallenge**: Method that processes the initial login challenge, checks bans, validates credentials, and initiates SRP6 exchange.
*   **_HandleLogonProof**: Method that reads the SRP6 proof packet and delegates to post-receive processing.
*   **_HandleLogonProof__PostRecv_HandleInvalidVersion**: Method that handles invalid client versions by checking for patches or rejecting the connection.
*   **_HandleLogonProof__PostRecv**: Method that completes SRP6 verification, handles PINs, checks geographic locks, updates the database, and sends the final response.
*   **_HandleReconnectChallenge**: Method that processes reconnection challenges using the existing session key.
*   **_HandleReconnectProof**: Method that verifies the reconnection proof using SHA1 hashing.
*   **_HandleRealmList**: Method that throttles requests and sends the realm list to the client.
*   **LoadRealmlistAndWriteIntoBuffer**: Method that constructs the realm list packet by querying character counts and realm statuses.
*   **_HandleXferAccept**: Method that initiates patch file transfer.
*   **_HandleXferResume**: Method that resumes patch file transfer from a specified offset.
*   **_HandleXferCancel**: Method that logs patch transfer cancellation.
*   **VerifyPinData**: Method that verifies a PIN against the server-generated grid and hash.
*   **GenerateTotpPin**: Method that generates a TOTP code for a given secret and time interval.
*   **RepeatInternalXferLoop**: Method that recursively reads and writes patch file chunks.
*   **InitAndHandOverControlToPatchHandler**: Method that initializes the patch transfer process.
*   **LoadAccountSecurityLevels**: Method that loads GM/security levels from the `account_access` table.
*   **GeographicalLockCheck**: Method that checks if the user's IP has moved to a different geographic location.
*   **VerifyVersion**: Method that verifies the client's integrity hash against known good hashes.
*   **CloseSocket**: Method that explicitly closes the underlying socket.

---

<!-- machine-true, projected from graph.json -->

## Map — AuthSocket

*Source:* AuthSocket.cpp, AuthSocket.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AuthSocket | ctor | AsyncSocket.Main/AsyncSocket, AsyncSocket.Main/GetRemoteIpString | — | — |
| Start | method | Config/GetIntDefault, Log.Main/Out | realmd_Main/main | — |
| GetRemoteIpString | method | — | realmd_Main/main | — |
| ~AuthSocket | dtor | Log.Main/Out, TimerHandle/Cancel | — | — |
| GetSecurityOn | method | — | — | — |
| DoRecvIncomingData | method | AsyncSocket._posix/Read, Log.Main/Out, NetworkError/GetErrorType, NetworkError/ToString | — | — |
| GenerateLogonProofResponse | method | ByteBuffer/ByteBuffer | — | — |
| _HandleLogonChallenge | method | AsyncSocket._posix/Read, AsyncSocket._posix/Write, BigNumber/AsByteArray, BigNumber/BigNumber, BigNumber/SetHexStr, BigNumber/SetRand, ByteBuffer/append#2, ByteBuffer/append#5, ByteBuffer/operator<<#10, ByteBuffer/operator<<#7, Config/GetBoolDefault, Config/GetIntDefault, Database/escape_string, Database/PQuery, Field/GetBool, Field/GetCppString, Field/GetString, Field/GetUInt32, Field/GetUInt64, Log.Main/Out, NetworkError/ToString, QueryResult/Fetch, QueryResult/operator[], ReadableBuffer/ReadableBuffer#6, shared_Util/rand32, SRP6/CalculateHostPublicEphemeral, SRP6/GetGeneratorModulo, SRP6/GetHostPublicEphemeral, SRP6/GetPrime, SRP6/SetSalt, SRP6/SetVerifier | — | account, account_banned, ip_banned |
| _HandleLogonProof | method | AsyncSocket._posix/Read, Log.Main/Out | — | — |
| _HandleLogonProof__PostRecv_HandleInvalidVersion | method | AsyncSocket._posix/Write, ByteBuffer/ByteBuffer, ByteBuffer/operator<<#7, ClientPatchCache/GetOrCalculateHash, Config/GetStringDefault, Digest/size, Errors/PrintStacktraceAndThrow, FileHandle/GetTotalFileSize, FileSystem/ToAbsolutePath, FileSystem/TryOpenFileReadonly, Log.Main/Out, ReadableBuffer/ReadableBuffer#6 | — | — |
| _HandleLogonProof__PostRecv | method | AsyncSocket._posix/Write, BigNumber/AsHexStr, ByteBuffer/ByteBuffer, ByteBuffer/operator<<#7, Common/GetLocaleByName, Config/GetBoolDefault, Config/GetIntDefault, Database/escape_string, Database/PExecute, Database/PExecute#2, Database/PQuery, Errors/PrintStacktraceAndThrow, Field/GetUInt32, Log.Main/Out, QueryResult/Fetch, ReadableBuffer/ReadableBuffer#6, RealmList/FindBuildInfo, shared_Util/urand, SRP6/CalculateProof, SRP6/CalculateSessionKey, SRP6/Finalize, SRP6/GetStrongSessionKey, SRP6/HashSessionKey, SRP6/Proof | — | account, account_banned, ip_banned |
| _HandleReconnectChallenge | method | AsyncSocket._posix/Read, AsyncSocket._posix/Write, BigNumber/AsByteArray, BigNumber/SetRand, ByteBuffer/append#2, ByteBuffer/append#5, ByteBuffer/operator<<#7, Database/escape_string, Database/PQuery, Field/GetString, Field/GetUInt32, Log.Main/Out, QueryResult/Fetch, ReadableBuffer/ReadableBuffer#6, SRP6/SetStrongSessionKey | — | account |
| _HandleReconnectProof | method | AsyncSocket._posix/Read, AsyncSocket._posix/Write, BigNumber/BigNumber, BigNumber/GetNumBytes, BigNumber/SetBinary, ByteBuffer/operator<<#7, Digest/size#2, Generator.SHA1/Generator, Generator.SHA1/GetDigest, Generator.SHA1/UpdateData, Generator.SHA1/UpdateData#3, Log.Main/Out, ReadableBuffer/ReadableBuffer#6, SRP6/GetStrongSessionKey | — | — |
| _HandleRealmList | method | AsyncSocket.Main/ReadSkip, AsyncSocket._posix/Write, ByteBuffer/append#3, ByteBuffer/ByteBuffer, ByteBuffer/operator<<#13, ByteBuffer/operator<<#7, ByteBuffer/size, Config/GetIntDefault, Log.Main/Out, ReadableBuffer/ReadableBuffer#6, RealmList/Instance, RealmList/UpdateIfNeed | — | — |
| LoadRealmlistAndWriteIntoBuffer | method | AsyncSocket.Main/GetRemoteEndpoint, ByteBuffer/operator<<, ByteBuffer/operator<<#10, ByteBuffer/operator<<#13, ByteBuffer/operator<<#7, ByteBuffer/operator<<#9, Database/PQuery, Field/GetUInt8, IpEndpoint/toString, QueryResult/Fetch, RealmList/begin, RealmList/end, RealmList/FindBuildInfo, RealmList/GetAddressForClient, RealmList/GetRealmCategoryIdByBuildAndZone, RealmList/Instance, RealmList/size | — | realmcharacters |
| _HandleXferAccept | method | Log.Main/Out | — | — |
| _HandleXferResume | method | AsyncSocket._posix/Read, FileHandle/GetTotalFileSize, FileHandle/Seek, Log.Main/Out | — | — |
| _HandleXferCancel | method | Log.Main/Out | — | — |
| VerifyPinData | method | BigNumber/AsByteArray, BigNumber/AsDecStr, BigNumber/BigNumber, BigNumber/SetBinary, Digest/size#2, Generator.SHA1/Generator, Generator.SHA1/GetDigest, Generator.SHA1/UpdateData#2, Generator.SHA1/UpdateData#3, Generator.SHA1/UpdateData#4 | — | — |
| GenerateTotpPin | method | Base32/Decode, Generator.HMACSHA1/Generator, Generator.HMACSHA1/GetDigest, Generator.HMACSHA1/UpdateData#4, Log.Main/Out | — | — |
| RepeatInternalXferLoop | method | AsyncSocket._posix/Write, FileHandle/ReadSync, Log.Main/Out, NetworkError/ToString, ReadableBuffer/ReadableBuffer#16 | — | — |
| InitAndHandOverControlToPatchHandler | method | Errors/PrintStacktraceAndThrow | — | — |
| LoadAccountSecurityLevels | method | Database/PQuery, Field/GetInt32, Field/GetUInt32, QueryResult/Fetch, QueryResult/NextRow | — | account_access |
| GeographicalLockCheck | method | Config/GetBoolDefault, Database/PQuery, Field/GetString, Field/GetUInt32, QueryResult/Fetch | — | geoip |
| VerifyVersion | method | Config/GetBoolDefault, Digest/size#2, Generator.SHA1/Generator, Generator.SHA1/GetDigest, Generator.SHA1/UpdateData#4, RealmList/FindBuildInfo#2 | — | — |
| CloseSocket | method | AsyncSocket._posix/CloseSocket | — | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `account`: id int(11) unsigned PK, username varchar(32), gmlevel tinyint(3) unsigned, sessionkey longtext?, v longtext?, s longtext?, token_key varchar(100), email text?, joindate timestamp, last_ip varchar(30), failed_logins int(11) unsigned, locked tinyint(3) unsigned, lock_country varchar(2), last_login timestamp, online tinyint(4), expansion tinyint(3) unsigned, mutetime bigint(40), locale tinyint(3) unsigned, os varchar(4), platform varchar(4), current_realm tinyint(3) unsigned, flags int(10) unsigned, security varchar(255)?, email_verif tinyint(1), geolock_pin int(11)?
- `account_access`: id int(11) unsigned PK, gmlevel tinyint(3) unsigned, RealmID int(11) PK
- `account_banned`: banid bigint(20), id bigint(20) PK, bandate bigint(40) PK, unbandate bigint(40), bannedby varchar(50), banreason varchar(255), active tinyint(4), realm tinyint(4), gmlevel tinyint(4) unsigned
- `geoip`: network_start_integer int(11)?, network_last_integer int(11)?, geoname_id text?, registered_country_geoname_id text?, represented_country_geoname_id text?, is_anonymous_proxy int(11)?, is_satellite_provider int(11)?, postal_code text?, latitude double?, longitude double?, accuracy_radius int(11)?
- `ip_banned`: ip varchar(32) PK, bandate int(11), unbandate int(11), bannedby varchar(50), banreason varchar(50)
- `realmcharacters`: realmid int(11) unsigned PK, acctid bigint(20) unsigned PK, numchars tinyint(3) unsigned

*`?` = nullable, `PK` = primary key column.*

