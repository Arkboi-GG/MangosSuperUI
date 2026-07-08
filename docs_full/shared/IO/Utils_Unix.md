# Utils_Unix

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`Utils_Unix` provides a single low-level utility function for manipulating file descriptor flags on POSIX-compatible systems. Its sole responsibility is to safely toggle specific status flags (such as `O_NONBLOCK`) on a socket handle without disturbing existing flags. This abstraction isolates the error-prone `fcntl` system call sequence—getting current flags, modifying them, and setting them back—into a reusable component that returns structured network errors rather than raw OS error codes.

## Member-by-Member Behavior

The unit contains one member:

**SetFdStatusFlag**
This inline function configures a specific status flag on a given socket handle. It operates in three steps:
1.  **Retrieve:** It calls `fcntl` with `F_GETFL` to fetch the current file status flags of the socket. If this fails, it immediately returns an `InternalError` wrapped in an `IO::NetworkError`, preserving the `errno`.
2.  **Modify:** It performs a bitwise OR operation between the retrieved flags and the requested `status` flag. This ensures that only the specified bit is set, while all other existing flags remain unchanged.
3.  **Apply:** It calls `fcntl` with `F_SETFL` to apply the new flag combination. If this fails, it similarly returns an `InternalError` with the associated `errno`.
4.  **Success:** If both operations succeed, it returns an `IO::NetworkError` indicating `NoError`.

## Cross-Unit Boundaries

`SetFdStatusFlag` is a leaf function in the call graph; it does not call into other C++ units within the `wowvmangos` codebase. However, it is invoked by two distinct networking components to configure socket behavior:

1.  **Called by `AsyncSocketAcceptor_posix/OnNewClientToAcceptAvailable`:**
    *   **Context:** When a new client connection is accepted by the asynchronous acceptor, the resulting socket is initially in blocking mode (inherited from the listening socket or default OS behavior).
    *   **Collaboration:** The acceptor calls `SetFdStatusFlag` to set the `O_NONBLOCK` flag on the newly accepted socket. This is critical for the asynchronous I/O model, ensuring that subsequent read/write operations on this socket will not block the event loop thread.

2.  **Called by `SocketConnector/ConnectBlocking`:**
    *   **Context:** During a blocking connection attempt, the connector may need to adjust socket flags temporarily or permanently depending on the connection strategy.
    *   **Collaboration:** The connector uses `SetFdStatusFlag` to modify the socket's status flags. While the name `ConnectBlocking` suggests synchronous behavior, the use of this utility implies that the underlying socket might still require non-blocking configuration for certain stages of the connection handshake or to align with the broader I/O framework's expectations for managed sockets.

## Data Model

This unit does not interact with any database tables. It operates exclusively on OS-level file descriptors and memory structures.

## Notable Implementation Details

*   **Bitwise Preservation:** The implementation correctly uses `originalFileStatus | status` rather than overwriting the flags. This is crucial because sockets often have multiple flags set (e.g., `O_NONBLOCK` combined with other platform-specific flags). Overwriting would inadvertently disable other necessary behaviors.
*   **Error Propagation:** The function translates POSIX `errno` values into the application's `IO::NetworkError` type. This allows higher-level networking code to handle errors uniformly without needing to include `<cerrno>` or interpret raw integer error codes.
*   **Inline Definition:** The function is defined as `inline` in the header. This eliminates function call overhead for this frequent operation, which is beneficial in high-throughput networking scenarios where sockets are created and configured rapidly.
*   **Namespace Typo:** The closing namespace comment in the source code reads `// namespace UI::Util`, but the actual namespace declaration is `IO::Utils`. This is a cosmetic discrepancy in the comment and does not affect compilation or runtime behavior, but it may cause confusion for readers scanning the file structure.

## Member Reference

**SetFdStatusFlag**
An inline function that sets a specific status flag (e.g., `O_NONBLOCK`) on a POSIX socket handle. It retrieves the current flags via `fcntl(F_GETFL)`, applies the new flag using a bitwise OR, and sets the result via `fcntl(F_SETFL)`. Returns an `IO::NetworkError` indicating success (`NoError`) or failure (`InternalError` with `errno`). Called by `AsyncSocketAcceptor_posix/OnNewClientToAcceptAvailable` to enable non-blocking I/O on new connections and by `SocketConnector/ConnectBlocking` during connection setup.

---

<!-- machine-true, projected from graph.json -->

## Map — Utils_Unix

*Source:* Utils_Unix.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| SetFdStatusFlag | function | — | AsyncSocketAcceptor_posix/OnNewClientToAcceptAvailable, SocketConnector/ConnectBlocking | — |
