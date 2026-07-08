# SocketConnector

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# SocketConnector

**Purpose & Responsibilities**

`SocketConnector` is a utility class within the `IO::Networking` namespace responsible for establishing synchronous, blocking TCP connections to remote IPv4 endpoints. It abstracts away the platform-specific complexities of socket creation, non-blocking mode configuration, and connection completion detection using `select()`. The class is designed as a static helper; it contains no instance state and provides a single primary interface, `ConnectBlocking`, which returns a `SocketDescriptor` upon success or a `NetworkError` upon failure. It serves as the initial step in setting up an asynchronous I/O session by providing a connected socket handle that can subsequently be bound to an `AsyncSocket` object.

## Member-by-Member Behavior

### Connection Establishment

The core functionality resides in the `ConnectBlocking` method. This method performs the following sequence:

1.  **Socket Creation**: Creates a new IPv4 TCP socket (`AF_INET`, `SOCK_STREAM`, `IPPROTO_TCP`). If this fails, it returns an `InternalError` wrapped in `nonstd::unexpected`.
2.  **Address Configuration**: Constructs a `sockaddr_in` structure from the provided `IpEndpoint` (IP address and port). It uses `Internal::inet_pton` to convert the IP address string to binary format.
3.  **Non-Blocking Mode**: Sets the socket to non-blocking mode. This is crucial because the subsequent `::connect` call will return immediately, allowing the method to use `select()` to wait for the connection to complete with a specific timeout, rather than blocking indefinitely.
    *   On Unix-like systems (Linux, macOS, BSDs), it calls `Utils_Unix::SetFdStatusFlag` to set the `O_NONBLOCK` flag.
    *   On Windows, it uses `ioctlsocket` with `FIONBIO`.
4.  **Connection Attempt**: Initiates the connection via `::connect`. Since the socket is non-blocking, this call typically returns `-1` with an error code indicating progress (`EINPROGRESS` on Unix, `WSAEWOULDBLOCK` on Windows). If the error is anything else, the connection failed immediately, and the socket is closed.
5.  **Wait for Completion**: Uses `::select` to wait for the socket to become writable (indicating the connection attempt succeeded or failed). The wait duration is determined by the `timeoutMs` parameter.
    *   If `select` returns `-1`, an internal error occurred.
    *   If `select` returns `0`, the timeout expired before the connection completed.
    *   If `select` returns `>0`, the socket is ready.
6.  **Error Verification**: After `select` indicates readiness, the method retrieves the actual socket error status using `getsockopt` with `SO_ERROR`. A non-zero error value means the connection was refused or otherwise failed after the initial attempt.
7.  **Result**: If all checks pass, it wraps the native socket handle and the target endpoint into a `SocketDescriptor` and returns it. Otherwise, it closes the socket and returns the appropriate `NetworkError`.

### Class Structure

The class declaration enforces that `SocketConnector` cannot be instantiated. The default constructor is deleted (`= delete`), ensuring all usage is through the static `ConnectBlocking` methods.

## Cross-Unit Boundaries

*   **Calls `Internal::CloseSocket`**: Used to clean up the native socket handle in case of any failure during the connection process. This ensures resources are freed even if the connection fails.
*   **Calls `Internal::inet_pton`**: Used to parse the IP address string from the `IpEndpoint` into the binary format required by `sockaddr_in`.
*   **Calls `Utils_Unix::SetFdStatusFlag`**: On Unix-like platforms, this is used to set the `O_NONBLOCK` flag on the socket file descriptor.
*   **Calls `SocketDescriptor` constructor**: Upon successful connection, the native socket handle and target endpoint are passed to the `SocketDescriptor` constructor to create the return value.
*   **Calls `NetworkError` constructor**: Various points in the code construct `NetworkError` objects to represent different failure modes (internal errors, timeouts).

## Data Model

This unit does not interact with any database tables. It operates purely on network sockets and memory-resident data structures.

## Notable Implementation Details

*   **Platform-Specific Error Codes**: The code explicitly handles different error codes for non-blocking connect attempts on Unix (`EINPROGRESS`) versus Windows (`WSAEWOULDBLOCK`). This is a critical detail for cross-platform compatibility.
*   **Select-Based Timeout**: Instead of relying on system-level socket timeouts (which can be less portable or harder to control precisely), the implementation uses `select()` with a custom `timeval` structure derived from the `timeoutMs` parameter. This provides consistent timeout behavior across platforms.
*   **Double-Check for Connection Errors**: After `select()` indicates the socket is ready, the code *must* check `SO_ERROR` via `getsockopt`. A writable socket after a non-blocking connect does not guarantee success; it could mean the connection was refused. This double-check is essential for correct error reporting.
*   **Resource Cleanup**: Every error path correctly calls `Internal::CloseSocket` to prevent resource leaks. This is vital for robustness, especially under high load or frequent connection failures.
*   **Template Convenience**: The header provides a templated version of `ConnectBlocking` that accepts any `std::chrono::duration` type, casting it to milliseconds internally. This improves usability while keeping the core implementation focused on millisecond precision.

## Member Reference

**ConnectBlocking**: Static method that creates a TCP socket, sets it to non-blocking mode, initiates a connection to the specified `IpEndpoint`, waits for completion using `select()` with a given timeout, verifies the connection status via `getsockopt`, and returns a `SocketDescriptor` on success or a `NetworkError` on failure. Handles platform-specific differences in error codes and non-blocking socket setup.

**SocketConnector**: Class declaration with a deleted default constructor, enforcing static-only usage. Contains the declaration for the `ConnectBlocking` method.

---

<!-- machine-true, projected from graph.json -->

## Map — SocketConnector

*Source:* SocketConnector.cpp, SocketConnector.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ConnectBlocking | method | Internal/CloseSocket, Internal/inet_pton, NetworkError/NetworkError#2, SocketDescriptor/SocketDescriptor#2, Utils_Unix/SetFdStatusFlag | — | — |
| SocketConnector | decl | — | — | — |
