# WorldSocket

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# WorldSocket

## Purpose & Responsibilities

`WorldSocket` is the low-level network abstraction responsible for managing a single TCP connection between the game server (`mangosd`) and a client. It operates as a stateful wrapper around an asynchronous socket (`AsyncSocket`), handling the raw byte stream, packet framing, encryption/decryption, and the initial authentication handshake.

Its primary responsibilities are:
1.  **Connection Lifecycle:** Managing the socket from creation (via `WorldSocketMgr`) through authentication to closure.
2.  **Packet Framing & I/O:** Reading raw bytes from the socket, interpreting the custom packet header (size + opcode), decrypting the payload using `AuthCrypt`, and constructing `WorldPacket` objects. Conversely, it queues outgoing `WorldPacket`s, encrypts headers, and writes them asynchronously to the socket.
3.  **Authentication Handshake:** Processing the `CMSG_AUTH_SESSION` packet to verify client credentials against the `account` database, establish encryption keys, and spawn a `WorldSession` object for higher-level game logic.
4.  **Keep-Alive & Anti-Cheat:** Handling `CMSG_PING` packets to measure latency and detect "overspeed" ping flooding, which can indicate cheating or malicious behavior.
5.  **Security Gatekeeping:** Enforcing IP bans, account locks, version checks, and ensuring clients connect via the realm daemon (`realmd`) by validating IP consistency.

`WorldSocket` is designed to be lightweight and non-blocking. It delegates heavy game logic to `WorldSession` once authentication is complete. It uses `std::enable_shared_from_this` to manage its lifetime safely within asynchronous callbacks.

## Member-by-Member Behavior

### Connection Initialization & Lifecycle

**`WorldSocket` (Constructor)**
Initializes the socket wrapper. It moves the provided `AsyncSocket` into `m_socket`, initializes timing variables for ping tracking, sets `m_Session` to `nullptr` (unauthenticated state), generates a random `m_authSeed` for the authentication challenge, and captures the remote IP string. It explicitly clears `m_sendQueueIsRunning` to ensure the send loop starts cleanly.

**`~WorldSocket` (Destructor)**
Ensures clean shutdown. It calls `CloseSocket()` to terminate the underlying connection and logs the disconnection. If a `m_sessionNoAuthTimeout` timer is active (used to drop connections that haven't authenticated within a configured timeframe), it cancels the timer to prevent dangling callbacks.

**`Start`**
Initiated by `WorldSocketMgr` upon new connection. It checks the configuration for `Network.TimeoutSecsIfNoAuth`. If set, it schedules a one-shot timer (`m_sessionNoAuthTimeout`) that will call `CloseSocket()` if authentication doesn't occur in time. It then calls `SendInitialPacketAndStartRecvLoop()` to begin the handshake.

**`FinalizeSession`**
A simple accessor called by `WorldSession` during its update loop or destruction. It sets `m_Session` to `nullptr`, signaling that the high-level session is gone, though the socket itself may remain open briefly for cleanup.

**`CloseSocket`**
Delegates to `AsyncSocket._posix/CloseSocket` to physically close the TCP connection. This is called by `WorldSession.Main/KickPlayer` or internally by `WorldSocket` on errors.

### Network I/O & Packet Handling

**`SendInitialPacketAndStartRecvLoop`**
Constructs and sends an `SMSG_AUTH_CHALLENGE` packet containing the `m_authSeed`. This prompts the client to respond with `CMSG_AUTH_SESSION`. It then immediately calls `DoRecvIncomingData()` to start the asynchronous read loop.

**`DoRecvIncomingData`**
The core asynchronous read handler. It allocates a buffer for the `ClientPktHeader` (4 bytes: 2 for size, 2 for opcode in some builds, or 4 bytes total depending on structure packing; here `ClientPktHeader` is 4 bytes: `uint16 size`, `uint32 cmd` is 6 bytes? No, looking at `WorldSocket.h`, `ClientPktHeader` is packed: `uint16 size`, `uint32 cmd` -> 6 bytes. Wait, `ServerPktHeader` in cpp is `uint16 size`, `uint16 cmd` (4 bytes). `ClientPktHeader` in h is `uint16 size`, `uint32 cmd` (6 bytes). The code reads `sizeof(ClientPktHeader)`).
1.  Reads the header asynchronously.
2.  On error, logs and closes the socket unless it's a normal close.
3.  Decrypts the header using `AuthCrypt/DecryptRecv`.
4.  Validates the packet size (must be between 4 and 0x2800) and opcode (must not be bogus). Invalid packets cause immediate socket closure.
5.  If the packet has no body (`size == sizeof(cmd)`), it creates an empty `WorldPacket` and passes it to `_HandleCompleteReceivedPacket`.
6.  If the packet has a body, it allocates a `WorldPacket`, resizes it, and reads the remaining bytes directly into the packet's internal buffer.
7.  Upon successful read, it passes the packet to `_HandleCompleteReceivedPacket`. If the result is `Okay`, it recursively calls `DoRecvIncomingData()` to continue the loop.

**`_HandleCompleteReceivedPacket`**
Dispatches fully received packets.
1.  Checks if the socket is closing; if so, fails.
2.  Stamps the packet with the current time.
3.  Switches on the opcode:
    *   `CMSG_PING`: Delegates to `_HandlePing`.
    *   `CMSG_AUTH_SESSION`: Delegates to `_HandleAuthSession`. If a session already exists, it logs an error and fails (prevents re-authentication).
    *   Default: If `m_Session` is null, it logs an error (unauthenticated packet) and fails. Otherwise, it moves the packet to `WorldSession.Main/QueueBinaryPacket` for processing by the game logic.
4.  Catches `ByteBufferException` during parsing. If configured (`CONFIG_BOOL_KICK_PLAYER_ON_BAD_PACKET`), it kicks the player; otherwise, it logs and continues.

**`SendPacket`**
Queues a `WorldPacket` for sending.
1.  Checks if closing; returns early if so.
2.  Locks `m_sendQueueLock`.
3.  Checks queue size; if > 1024, logs an error and closes the socket (flow control/backpressure).
4.  Pushes the packet to `m_sendQueue`.
5.  Uses `m_sendQueueIsRunning` (atomic flag) to ensure only one async write operation is initiated. If not running, it enters the IO context and calls `HandleResultOfAsyncWrite` to start draining the queue.

**`HandleResultOfAsyncWrite`**
The asynchronous write callback.
1.  On error, logs and closes the socket if not a normal close. Clears the running flag.
2.  If the queue is empty, clears the running flag and returns.
3.  Drains `m_sendQueue` into a `ByteBuffer` (`alreadyAllocatedBuffer`).
4.  For each packet, it constructs a `ServerPktHeader` (4 bytes: `uint16 size`, `uint16 cmd`), encrypts the header using `AuthCrypt/EncryptSend`, and appends the header and packet body to the buffer.
5.  Initiates an asynchronous write of the combined buffer to the socket.
6.  Recursively calls `HandleResultOfAsyncWrite` to handle the next batch or completion.

### Authentication Logic

**`_HandleAuthSession`**
Processes the `CMSG_AUTH_SESSION` packet to authenticate the user.
1.  **Parsing:** Extracts `clientBuild`, `serverId`, `account` name, `clientSeed`, and the SHA1 `digest` from the packet.
2.  **Version Check:** Verifies `clientBuild` against `DBCStores/IsAcceptableClientBuild`. Rejects with `AUTH_VERSION_MISMATCH` if invalid.
3.  **Database Lookup:** Queries the `account` table, joined with `account_access` and `account_banned`.
    *   Escapes the username to prevent SQL injection.
    *   Filters for accounts logged in within the last day (`DATEDIFF(NOW(), a.last_login) < 1`). This ensures the client went through `realmd` recently.
    *   Checks for active bans (`account_banned`).
4.  **IP Validation:** Compares the `last_ip` from the database with the current socket's remote IP. If they differ and the current IP is not in the local server address list (resolved via `GetServerAddresses`), it rejects with `AUTH_FAILED`. This prevents direct connections bypassing `realmd`.
5.  **Ban & Security Checks:**
    *   Rejects if the account is banned or the IP is banned (`AccountMgr/IsIPBanned`).
    *   Checks `World/GetPlayerSecurityLimit`; if the account's security level is too low for the current server state (e.g., GM-only mode), rejects with `AUTH_UNAVAILABLE`.
6.  **Cryptographic Verification:**
    *   Computes an expected SHA1 digest using: `account`, `time_t(0)`, `clientSeed`, `m_authSeed`, and the session key `K` from the database.
    *   Compares this with the `digest` received from the client. Mismatch results in `AUTH_FAILED`.
7.  **Session Establishment:**
    *   Updates `last_ip` in the `account` table.
    *   Validates OS and Platform strings ("Win"/"OSX", "x86"/"PPC").
    *   Cancels the auth timeout timer.
    *   Creates a new `WorldSession` object, passing it the account ID, socket shared pointer, security level, mute time, and locale.
    *   Initializes `AuthCrypt` with the session key `K`.
    *   Populates the `WorldSession` with username, build, flags, OS, platform, and email verification status.
    *   Loads global account data and tutorials.
    *   Registers the session with `World/AddSession`.
    *   Sends an addon packet if applicable.
    *   Returns `Okay`.

**`_HandlePing`**
Handles `CMSG_PING` packets.
1.  Extracts `ping` value (and `latency` for newer builds).
2.  Tracks `m_lastPingTime`. If the interval since the last ping is less than 27 seconds, it increments `m_overSpeedPings`.
3.  If `m_overSpeedPings` exceeds `CONFIG_UINT32_MAX_OVERSPEED_PINGS` and the user is a regular player (`SEC_PLAYER`), it logs and returns `Fail` (kicking the player).
4.  Resets `m_overSpeedPings` if the interval is normal (>27s).
5.  If `m_Session` exists, updates latency. If not, logs an error and fails.
6.  Sends back an `SMSG_PONG` packet with the original `ping` value.

### Utility & Accessors

**`GetRemoteIpString`**
Returns the cached remote IP address string. Used by `WorldSession` and `WorldSocketMgr` for logging and identification.

**`IsClosing`**
Delegates to `AsyncSocket` to check if the socket is in the process of closing. Used by `WorldSession` to avoid processing packets on dead connections.

**`GetServerAddresses`**
A static helper function that resolves the server's own hostname to IP addresses and adds `127.0.0.1`. This list is used in `_HandleAuthSession` to allow local connections to bypass the `realmd` IP check.

## Cross-Unit Boundaries

*   **`AsyncSocket.Main` / `AsyncSocket._posix`**: `WorldSocket` wraps an `AsyncSocket`. It calls `Read`, `Write`, `CloseSocket`, `EnterIoContext`, and `GetRemoteIpString`. The `AsyncSocket` provides the non-blocking I/O primitives. `WorldSocket` handles the protocol logic on top of these primitives.
*   **`AuthCrypt`**: `WorldSocket` uses `AuthCrypt` to encrypt outgoing packet headers (`EncryptSend`) and decrypt incoming headers (`DecryptRecv`). The session key `K` is derived during authentication and set via `SetKey`.
*   **`WorldSession.Main`**: Once authenticated, `WorldSocket` creates a `WorldSession` and stores a raw pointer to it (`m_Session`). It routes all non-handshake packets to `WorldSession.QueueBinaryPacket`. `WorldSession` calls `WorldSocket.FinalizeSession` on destruction and `WorldSocket.IsClosing` during updates. `WorldSession` also calls `WorldSocket.SendPacketImpl` (which likely maps to `SendPacket` or similar) to send responses.
*   **`WorldSocketMgr`**: Calls `WorldSocket.Start` when a new connection arrives.
*   **`AccountMgr`**: `_HandleAuthSession` calls `AccountMgr.IsIPBanned` and `AccountMgr.UpdateAccountData`.
*   **`World`**: `_HandleAuthSession` calls `World.GetPlayerSecurityLimit` and `World.AddSession`. `WorldSocket._HandlePing` and `WorldSocket._HandleCompleteReceivedPacket` call `World.getConfig` for various limits and flags.
*   **`Database`**: `_HandleAuthSession` executes queries against `LoginDatabase` to fetch account details and update `last_ip`.
*   **`DBCStores`**: `_HandleAuthSession` calls `IsAcceptableClientBuild` to validate the client version.
*   **`AddonHandler`**: `_HandleAuthSession` calls `BuildAddonPacket` to generate the initial addon data packet.
*   **`Log.Main`**: Extensively used for logging connection events, errors, and debug info.
*   **`NetworkError`**: Used to interpret I/O errors from `AsyncSocket`.
*   **`Opcodes`**: `IsDefinitelyBogusOpcode` is used in `DoRecvIncomingData` to filter invalid opcodes.
*   **`WorldPacket`**: Core data structure for both incoming and outgoing data. `WorldSocket` constructs `WorldPacket`s from raw bytes and extracts data from them.
*   **`Config`**: `Start` reads `Network.TimeoutSecsIfNoAuth`. `_HandlePing` reads `MAX_OVERSPEED_PINGS`.
*   **`DNS`**: `GetServerAddresses` uses `DNS.GetOwnHostname` and `DNS.ResolveDomainAll` to build the list of valid local IPs.
*   **`TimerHandle`**: `m_sessionNoAuthTimeout` is a `TimerHandle` scheduled by `Start` and cancelled in the destructor or upon successful auth.

## Data Model

`WorldSocket` interacts directly with the following database tables via SQL queries in `_HandleAuthSession`:

*   **`account`**:
    *   **Usage**: Primary source of truth for user credentials and session state.
    *   **Columns Accessed**:
        *   `id`: Account identifier.
        *   `username`: Used for lookup and escaping.
        *   `sessionkey`: Cryptographic key used for session encryption and authentication digest verification.
        *   `last_ip`: Validated against the connecting socket's IP to ensure `realmd` traversal. Updated upon successful login.
        *   `v`, `s`: Stored but not explicitly used in this unit's logic (likely for password hashing in other contexts).
        *   `mutetime`: Passed to `WorldSession` to enforce mute status.
        *   `locale`: Passed to `WorldSession` for language settings.
        *   `os`, `platform`: Validated and passed to `WorldSession`.
        *   `flags`: Account flags passed to `WorldSession`.
        *   `email`, `email_verif`: Used to determine email verification status.
        *   `last_login`: Checked to ensure the account was accessed via `realmd` within the last 24 hours.
*   **`account_access`**:
    *   **Usage**: Joined with `account` to retrieve the GM/security level (`gmLevel`) for the specific realm or globally (`RealmID` IN (-1, current_realm)).
*   **`account_banned`**:
    *   **Usage**: Joined with `account` to check for active bans (`active` = 1). If a ban exists and `unbandate` is in the past or equal to `bandate`, the account is considered unbanned; otherwise, it is banned.

## Notable Implementation Details

1.  **Asynchronous I/O Loop**: `DoRecvIncomingData` and `HandleResultOfAsyncWrite` use recursive asynchronous calls. After reading/writing one packet/batch, they schedule the next read/write. This avoids blocking the IO thread and allows handling multiple sockets concurrently.
2.  **Packet Header Encryption**: Only the packet header (`ServerPktHeader`) is encrypted by `AuthCrypt`. The body is sent in plaintext (though the connection itself is TCP, the game protocol relies on this header encryption for basic integrity/auth). Note: In modern WoW, the entire packet is often encrypted, but this codebase (Vanilla/TBC era emulation) only encrypts the header.
3.  **Memory Management in Reads**: `DoRecvIncomingData` uses `std::shared_ptr<std::unique_ptr<WorldPacket>>` to pass the packet through the async lambda. This is a workaround because `std::unique_ptr` cannot be moved into a lambda capture easily. The `shared_ptr` ensures the packet survives the async gap, and the `unique_ptr` inside ensures single ownership semantics until it's moved out in `_HandleCompleteReceivedPacket`.
4.  **Send Queue Backpressure**: `SendPacket` checks if `m_sendQueue.size() > 1024`. If so, it closes the socket. This prevents memory exhaustion if the client stops reading or the network is saturated.
5.  **Auth Timeout**: The `m_sessionNoAuthTimeout` timer prevents resource leaks from clients that connect but never authenticate. It is carefully managed: scheduled in `Start`, cancelled in `_HandleAuthSession` on success, and cancelled in the destructor.
6.  **IP Bypass Logic**: The check `fields[3].GetCppString() != GetRemoteIpString() && serverAddressList.find(GetRemoteIpString()) == serverAddressList.end()` allows local connections (e.g., from the same machine or LAN if configured) to bypass the strict `realmd` IP check. This is crucial for development and certain hosting setups.
7.  **Overspeed Ping Detection**: The 27-second threshold for ping intervals is hardcoded. If pings arrive faster, `m_overSpeedPings` increments. This is a simple anti-cheat mechanism against bots that might spam pings to manipulate latency calculations or overwhelm the server.
8.  **SQL Injection Prevention**: The username is escaped using `LoginDatabase.escape_string` before being inserted into the query. However, the query uses string interpolation for the realm ID and escaped username. While safer than raw concatenation, prepared statements would be more robust. The code comments acknowledge this: "No SQL injection, username escaped."
9.  **Endianness Conversion**: `EndianConvert` and `EndianConvertReverse` are used on packet headers. This suggests the protocol expects network byte order (big-endian) for headers, while the host might be little-endian. The specific functions imply a swap is needed.
10. **Static Server Address List**: `GetServerAddresses` is called once and stored in a static `std::set` inside `_HandleAuthSession`. This list is computed at runtime when the first auth attempt occurs.

## Member Reference

**`data`**: Method defined in `ServerPktHeader` struct (local to `WorldSocket.cpp`). Returns a pointer to the header's memory as `char const*`. Used for appending the header to the send buffer.

**`headerSize`**: Method defined in `ServerPktHeader` struct (local to `WorldSocket.cpp`). Returns `sizeof(ServerPktHeader)`. Used for appending the header to the send buffer.

**`WorldSocket`**: Constructor. Initializes socket, auth seed, remote IP, and clears send queue flag. Calls `AsyncSocket.Main/AsyncSocket`, `AsyncSocket.Main/GetRemoteIpString`, and `shared_Util/rand32`.

**`~WorldSocket`**: Destructor. Closes socket, logs disconnect, and cancels auth timeout timer. Calls `Log.Main/Out` and `TimerHandle/Cancel`.

**`DoRecvIncomingData`**: Asynchronous method to read and parse incoming packets. Handles header decryption, validation, and body reading. Dispatches to `_HandleCompleteReceivedPacket`. Calls `AsyncSocket.Main/GetRemoteIpString`, `AsyncSocket._posix/Read`, `AuthCrypt/DecryptRecv`, `ByteBuffer/contents`, `ByteBuffer/resize`, `ByteBuffer/size`, `Log.Main/Out`, `NetworkError/GetErrorType`, `NetworkError/ToString`, `Opcodes/IsDefinitelyBogusOpcode`, and `WorldPacket/WorldPacket#4`.

**`WorldSocket#3`**: Deleted copy constructor declaration.

**`operator=#2`**: Deleted copy assignment operator declaration.

**`WorldSocket#2`**: Deleted move constructor declaration.

**`operator=`**: Deleted move assignment operator declaration.

**`FinalizeSession`**: Sets `m_Session` to `nullptr`. Called by `WorldSession.Main/Update` and `WorldSession.Main/~WorldSession`.

**`GetRemoteIpString`**: Returns the cached remote IP string. Called by `WorldSession.Main/QueueBinaryPacket`, `WorldSession.Main/WorldSession`, and `WorldSocketMgr/OnNewClientConnected`.

**`IsClosing`**: Returns whether the underlying socket is closing. Called by `WorldSession.Main/CanProcessPackets` and `WorldSession.Main/Update`.

**`_HandleCompleteReceivedPacket`**: Dispatches parsed packets to handlers or `WorldSession`. Handles exceptions and config-based kick decisions. Calls `ByteBuffer/hexlike`, `Errors/PrintStacktraceAndThrow`, `Log.Main/HasLogLevelOrHigher`, `Log.Main/Out`, `shared_Util/getMSTime`, `World/getConfig`, `WorldPacket/FillPacketTime`, `WorldPacket/GetOpcode`, `WorldSession.Main/GetAccountId`, and `WorldSession.Main/QueueBinaryPacket`.

**`GetServerAddresses`**: Static function resolving local server IPs. Called internally by `_HandleAuthSession`. Calls `DNS/GetOwnHostname`, `DNS/ResolveDomainAll`, and `IpAddress/ToString`.

**`_HandleAuthSession`**: Handles the authentication handshake. Validates client build, checks DB for account/bans/IP, verifies cryptographic digest, establishes encryption, and creates `WorldSession`. Calls `AccountMgr/IsIPBanned`, `AccountMgr/UpdateAccountData`, `AddonHandler/BuildAddonPacket`, `AuthCrypt/Init`, `AuthCrypt/SetKey`, `BigNumber/AsByteArray`, `BigNumber/BigNumber`, `BigNumber/SetHexStr`, `ByteBuffer/operator<<#7`, `ByteBuffer/operator>>`, `ByteBuffer/operator>>#9`, `ByteBuffer/read`, `Database/CreateStatement`, `Database/escape_string`, `Database/PQuery`, `DBCStores/IsAcceptableClientBuild`, `Digest/size#2`, `Field/GetBool`, `Field/GetCppString`, `Field/GetString`, `Field/GetUInt32`, `Field/GetUInt64`, `Field/GetUInt8`, `Generator.SHA1/Generator`, `Generator.SHA1/GetDigest`, `Generator.SHA1/UpdateData`, `Generator.SHA1/UpdateData#3`, `Generator.SHA1/UpdateData#4`, `Log.Main/Out`, `QueryResult/Fetch`, `SqlStatementID/SqlStatementID`, `TimerHandle/Cancel`, `World/AddSession`, `World/GetPlayerSecurityLimit`, `WorldPacket/Initialize`, `WorldPacket/WorldPacket`, `WorldPacket/WorldPacket#3`, `WorldSession.Main/LoadGlobalAccountData`, `WorldSession.Main/LoadTutorialsData`, `WorldSession.Main/SetAccountFlags`, `WorldSession.Main/SetGameBuild`, `WorldSession.Main/SetOS`, `WorldSession.Main/SetPlatform`, `WorldSession.Main/SetSessionKey`, `WorldSession.Main/SetUsername`, `WorldSession.Main/SetVerifiedEmail`, and `WorldSession.Main/WorldSession`. Touches tables: `account`, `account_access`, `account_banned`.

**`_HandlePing`**: Handles ping packets, tracks latency, and detects overspeed flooding. Calls `ByteBuffer/operator<<#10`, `ByteBuffer/operator>>#9`, `Log.Main/Out`, `World/getConfig#4`, `WorldPacket/WorldPacket#3`, `WorldPacket/WorldPacket#4`, `WorldSession.Main/GetSecurity`, and `WorldSession.Main/SetLatency`.

**`SendInitialPacketAndStartRecvLoop`**: Sends auth challenge and starts the receive loop. Calls `ByteBuffer/operator<<#10`, `WorldPacket/WorldPacket#3`, and `WorldPacket/WorldPacket#4`.

**`SendPacket`**: Queues a packet for sending and initiates async write if needed. Calls `AsyncSocket._posix/EnterIoContext` and `Log.Main/Out`. Called by `PlayerBroadcaster/SendPacket` and `WorldSession.Main/SendPacketImpl`.

**`HandleResultOfAsyncWrite`**: Async callback for writing packets. Drains queue, encrypts headers, and writes to socket. Calls `AsyncSocket._posix/Write`, `AuthCrypt/EncryptSend`, `ByteBuffer/append#4`, `ByteBuffer/append#5`, `ByteBuffer/clear`, `ByteBuffer/contents`, `ByteBuffer/empty`, `ByteBuffer/size`, `Log.Main/Out`, `NetworkError/GetErrorType`, `NetworkError/ToString`, `ReadableBuffer/ReadableBuffer#15`, `WorldPacket/GetOpcode`, and `WorldPacket/WorldPacket#2`.

**`Start`**: Starts the socket lifecycle, setting up auth timeout and initiating the handshake. Calls `Config/GetIntDefault` and `Log.Main/Out`. Called by `WorldSocketMgr/OnNewClientConnected`.

**`CloseSocket`**: Closes the underlying socket. Calls `AsyncSocket._posix/CloseSocket`. Called by `WorldSession.Main/KickPlayer`.

---

<!-- machine-true, projected from graph.json -->

## Map — WorldSocket

*Source:* WorldSocket.cpp, WorldSocket.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| data | method | — | — | — |
| headerSize | method | — | — | — |
| WorldSocket | ctor | AsyncSocket.Main/AsyncSocket, AsyncSocket.Main/GetRemoteIpString, shared_Util/rand32 | — | — |
| ~WorldSocket | dtor | Log.Main/Out, TimerHandle/Cancel | — | — |
| DoRecvIncomingData | method | AsyncSocket.Main/GetRemoteIpString, AsyncSocket._posix/Read, AuthCrypt/DecryptRecv, ByteBuffer/contents, ByteBuffer/resize, ByteBuffer/size, Log.Main/Out, NetworkError/GetErrorType, NetworkError/ToString, Opcodes/IsDefinitelyBogusOpcode, WorldPacket/WorldPacket#4 | — | — |
| WorldSocket#3 | decl | — | — | — |
| operator=#2 | decl | — | — | — |
| WorldSocket#2 | decl | — | — | — |
| operator= | decl | — | — | — |
| FinalizeSession | method | — | WorldSession.Main/Update, WorldSession.Main/~WorldSession | — |
| GetRemoteIpString | method | — | WorldSession.Main/QueueBinaryPacket, WorldSession.Main/WorldSession, WorldSocketMgr/OnNewClientConnected | — |
| IsClosing | method | — | WorldSession.Main/CanProcessPackets, WorldSession.Main/Update | — |
| _HandleCompleteReceivedPacket | method | ByteBuffer/hexlike, Errors/PrintStacktraceAndThrow, Log.Main/HasLogLevelOrHigher, Log.Main/Out, shared_Util/getMSTime, World/getConfig, WorldPacket/FillPacketTime, WorldPacket/GetOpcode, WorldSession.Main/GetAccountId, WorldSession.Main/QueueBinaryPacket | — | — |
| GetServerAddresses | function | DNS/GetOwnHostname, DNS/ResolveDomainAll, IpAddress/ToString | — | — |
| _HandleAuthSession | method | AccountMgr/IsIPBanned, AccountMgr/UpdateAccountData, AddonHandler/BuildAddonPacket, AuthCrypt/Init, AuthCrypt/SetKey, BigNumber/AsByteArray, BigNumber/BigNumber, BigNumber/SetHexStr, ByteBuffer/operator<<#7, ByteBuffer/operator>>, ByteBuffer/operator>>#9, ByteBuffer/read, Database/CreateStatement, Database/escape_string, Database/PQuery, DBCStores/IsAcceptableClientBuild, Digest/size#2, Field/GetBool, Field/GetCppString, Field/GetString, Field/GetUInt32, Field/GetUInt64, Field/GetUInt8, Generator.SHA1/Generator, Generator.SHA1/GetDigest, Generator.SHA1/UpdateData, Generator.SHA1/UpdateData#3, Generator.SHA1/UpdateData#4, Log.Main/Out, QueryResult/Fetch, SqlStatementID/SqlStatementID, TimerHandle/Cancel, World/AddSession, World/GetPlayerSecurityLimit, WorldPacket/Initialize, WorldPacket/WorldPacket, WorldPacket/WorldPacket#3, WorldSession.Main/LoadGlobalAccountData, WorldSession.Main/LoadTutorialsData, WorldSession.Main/SetAccountFlags, WorldSession.Main/SetGameBuild, WorldSession.Main/SetOS, WorldSession.Main/SetPlatform, WorldSession.Main/SetSessionKey, WorldSession.Main/SetUsername, WorldSession.Main/SetVerifiedEmail, WorldSession.Main/WorldSession | — | account, account_access, account_banned |
| _HandlePing | method | ByteBuffer/operator<<#10, ByteBuffer/operator>>#9, Log.Main/Out, World/getConfig#4, WorldPacket/WorldPacket#3, WorldPacket/WorldPacket#4, WorldSession.Main/GetSecurity, WorldSession.Main/SetLatency | — | — |
| SendInitialPacketAndStartRecvLoop | method | ByteBuffer/operator<<#10, WorldPacket/WorldPacket#3, WorldPacket/WorldPacket#4 | — | — |
| SendPacket | method | AsyncSocket._posix/EnterIoContext, Log.Main/Out | PlayerBroadcaster/SendPacket, WorldSession.Main/SendPacketImpl | — |
| HandleResultOfAsyncWrite | method | AsyncSocket._posix/Write, AuthCrypt/EncryptSend, ByteBuffer/append#4, ByteBuffer/append#5, ByteBuffer/clear, ByteBuffer/contents, ByteBuffer/empty, ByteBuffer/size, Log.Main/Out, NetworkError/GetErrorType, NetworkError/ToString, ReadableBuffer/ReadableBuffer#15, WorldPacket/GetOpcode, WorldPacket/WorldPacket#2 | — | — |
| Start | method | Config/GetIntDefault, Log.Main/Out | WorldSocketMgr/OnNewClientConnected | — |
| CloseSocket | method | AsyncSocket._posix/CloseSocket | WorldSession.Main/KickPlayer | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `account`: id int(11) unsigned PK, username varchar(32), gmlevel tinyint(3) unsigned, sessionkey longtext?, v longtext?, s longtext?, token_key varchar(100), email text?, joindate timestamp, last_ip varchar(30), failed_logins int(11) unsigned, locked tinyint(3) unsigned, lock_country varchar(2), last_login timestamp, online tinyint(4), expansion tinyint(3) unsigned, mutetime bigint(40), locale tinyint(3) unsigned, os varchar(4), platform varchar(4), current_realm tinyint(3) unsigned, flags int(10) unsigned, security varchar(255)?, email_verif tinyint(1), geolock_pin int(11)?
- `account_access`: id int(11) unsigned PK, gmlevel tinyint(3) unsigned, RealmID int(11) PK
- `account_banned`: banid bigint(20), id bigint(20) PK, bandate bigint(40) PK, unbandate bigint(40), bannedby varchar(50), banreason varchar(255), active tinyint(4), realm tinyint(4), gmlevel tinyint(4) unsigned

*`?` = nullable, `PK` = primary key column.*

