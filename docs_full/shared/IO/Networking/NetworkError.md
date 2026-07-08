# NetworkError

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`IO::NetworkError` is a lightweight, value-type struct representing networking failures within the `wowvmangos` I/O subsystem. It encapsulates a high-level logical `ErrorType` (e.g., `Timeout`, `SocketClosed`) and an optional OS-specific error code (`m_additionalOsErrorCode`). Designed for cheap copying and construction (`constexpr`), it standardizes error propagation from low-level POSIX socket operations (`AsyncSocket._posix`) up to protocol handlers (`AuthSocket`, `WorldSocket`, `RASocket`). It is not an exception class but a structured return value used to signal failure states in asynchronous I/O loops.

## Member-by-Member Behavior

### Construction
*   **`NetworkError(ErrorType)`**: Initializes the error with a logical type and sets the OS code to `0`. Used for purely logical failures (e.g., timeouts) where no OS errno is relevant.
*   **`NetworkError(ErrorType, int)`**: The primary constructor, accepting both a logical type and an OS error code. Used when system calls fail, preserving the raw OS error for diagnostics.

### State Inspection
*   **`GetErrorType()`**: Returns the stored `ErrorType`. Callers use this to determine the failure reason (e.g., distinguishing `SocketClosed` from `Timeout`).
*   **`operator bool()`**: Enables boolean context usage. Returns `true` if `GetErrorType() != NoError`, simplifying checks like `if (err)`.
*   **`FromSystemError(int)`**: Static factory creating a `NetworkError` with `ErrorType::InternalError` and the provided OS code. Used by `AsyncSocket._posix` to wrap raw system failures.

### String Representation
*   **`ToString()`**: Generates a human-readable string. It combines the base string for the `ErrorType` (via `GetErrorBaseString`) with the OS code and its description (via `SystemErrorToString`) if present. Critical for logging in socket handlers.

## Cross-Unit Boundaries

`NetworkError` is constructed by low-level units and consumed by high-level protocol handlers.

*   **Construction (`AsyncSocket._posix`, `SocketConnector`, `ProxyV2Reader`)**:
    *   `AsyncSocket._posix` constructs errors in I/O methods (`Read`, `Write`, `PerformNonBlockingRead/Write`), configuration (`SetNativeSocketOption_*`), and lifecycle (`InitializeAndFixateMemoryLocation`, `EnterIoContext`, etc.) to report failures.
    *   `SocketConnector::ConnectBlocking` and `ProxyV2Reader::ReadProxyV2Handshake` construct errors for connection and handshake failures.
*   **Consumption (`AuthSocket`, `WorldSocket`, `RASocket`)**:
    *   These units call `GetErrorType()` to branch logic (e.g., handling disconnects) and `ToString()` to log errors in methods like `DoRecvIncomingData`, `HandleResultOfAsyncWrite`, and `SendAndDisconnect`.
*   **Logging (`AsyncSocketAcceptor_posix`, `WorldSocketMgr`, `realmd_Main`)**:
    *   `AsyncSocketAcceptor_posix::OnNewClientToAcceptAvailable`, `WorldSocketMgr::OnNewClientConnected`, and `realmd_Main::main` call `ToString()` to log connection acceptance and establishment failures.

## Data Model

This unit does not interact with any database tables.

## Notable Implementation Details

1.  **Static String Caching**: `GetErrorBaseString` uses `static std::string` variables for each error type, ensuring single-time construction and avoiding heap allocations during error reporting.
2.  **InternalError Catch-All**: `FromSystemError` maps all OS errors to `ErrorType::InternalError`. The specific OS cause is only accessible via `m_additionalOsErrorCode` (private) or through `ToString()`, indicating that OS-specific branching is not intended for higher-level logic.
3.  **OS Error Integration**: `ToString()` conditionally appends OS error details only if `m_additionalOsErrorCode` is non-zero, keeping logs concise for logical errors while providing depth for system failures.

## Member Reference

**GetErrorBaseString**
Free function mapping `IO::NetworkError::ErrorType` to static `std::string` literals (e.g., "NoError", "SocketClosed"). Called by `NetworkError::ToString()` to provide the base error message.

**NetworkError**
Primary constructor initializing `m_error` and `m_additionalOsErrorCode`. Called by `AsyncSocket._posix` (I/O, config, lifecycle methods), `SocketConnector::ConnectBlocking`, and `ProxyV2Reader::ReadProxyV2Handshake` to represent various failure states.

**NetworkError#2**
Convenience constructor taking only `ErrorType`, defaulting OS code to `0`. Delegates to the primary constructor. Used by `AsyncSocket._posix` methods like `InitializeAndFixateMemoryLocation`, `Read`, `ReadSome`, and `Write` for logical errors.

**GetErrorType**
Const method returning the stored `ErrorType`. Called by `AuthSocket::DoRecvIncomingData`, `WorldSocket::DoRecvIncomingData`, and `WorldSocket::HandleResultOfAsyncWrite` to determine failure reasons for control flow.

**FromSystemError**
Static factory creating a `NetworkError` with `ErrorType::InternalError` and the given OS code. Called by `AsyncSocket._posix::SetNativeSocketOption_NoDelay` and `AsyncSocket._posix::SetNativeSocketOption_SystemOutgoingSendBuffer` to wrap socket option failures.

**ToString**
Const method generating a human-readable error string by combining the base error type string with OS error details (via `SystemErrorToString`) if present. Called by `AuthSocket`, `WorldSocket`, `RASocket`, `AsyncSocketAcceptor_posix`, `WorldSocketMgr`, and `realmd_Main` for logging.

---

<!-- machine-true, projected from graph.json -->

## Map — NetworkError

*Source:* NetworkError.cpp, NetworkError.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GetErrorBaseString | function | — | — | — |
| NetworkError | ctor | — | AsyncSocket._posix/EnterIoContext, AsyncSocket._posix/InitializeAndFixateMemoryLocation, AsyncSocket._posix/PerformContextSwitch, AsyncSocket._posix/PerformNonBlockingRead, AsyncSocket._posix/PerformNonBlockingWrite, AsyncSocket._posix/Read, AsyncSocket._posix/ReadSome, AsyncSocket._posix/SetNativeSocketOption_NoDelay, AsyncSocket._posix/SetNativeSocketOption_SystemOutgoingSendBuffer, AsyncSocket._posix/StopPendingTransactionsAndForceClose, AsyncSocket._posix/Write, ProxyV2Reader/ReadProxyV2Handshake | — |
| NetworkError#2 | ctor | — | AsyncSocket._posix/InitializeAndFixateMemoryLocation, AsyncSocket._posix/Read, AsyncSocket._posix/ReadSome, AsyncSocket._posix/Write, SocketConnector/ConnectBlocking | — |
| GetErrorType | method | — | AuthSocket/DoRecvIncomingData, WorldSocket/DoRecvIncomingData, WorldSocket/HandleResultOfAsyncWrite | — |
| FromSystemError | method | — | AsyncSocket._posix/SetNativeSocketOption_NoDelay, AsyncSocket._posix/SetNativeSocketOption_SystemOutgoingSendBuffer | — |
| ToString | method | SystemErrorToString/SystemErrorToString | AsyncSocketAcceptor_posix/OnNewClientToAcceptAvailable, AuthSocket/DoRecvIncomingData, AuthSocket/RepeatInternalXferLoop, AuthSocket/_HandleLogonChallenge, RASocket/DoRecvIncomingData, RASocket/SendAndDisconnect, RASocket/SendAndRecvNextInput, RASocket/Start, realmd_Main/main, WorldSocket/DoRecvIncomingData, WorldSocket/HandleResultOfAsyncWrite, WorldSocketMgr/OnNewClientConnected | — |
