# AsyncSocket — Class Overview

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AsyncSocket

`AsyncSocket` is the foundational, platform-agnostic abstraction for asynchronous TCP socket communication in the VMaNGOS network layer. It manages the lifecycle, state, and raw byte-stream transport for connections, serving as the base class for all higher-level protocol handlers (`AuthSocket`, `WorldSocket`, `RASocket`). By decoupling protocol logic from I/O mechanics, it enables the server to handle thousands of concurrent clients efficiently using non-blocking, event-driven I/O.

## Class Structure

The class is split into two primary partials to separate portable logic from platform-specific kernel interactions:

*   **`AsyncSocket.Main`**: Implements the class’s portable interface, including construction, destruction, move semantics, and state inspection. It manages the atomic state machine that tracks pending operations and ensures thread-safe cleanup. It also provides utility methods for identifying remote peers and discarding incoming data.
*   **`AsyncSocket._posix`**: Contains the POSIX-specific implementation of I/O operations. It integrates with the operating system’s event notification mechanisms (`epoll` on Linux, `kqueue` on BSD/macOS) to drive non-blocking reads and writes. This partial handles the registration of the socket with the kernel event loop, manages partial I/O transfers, and coordinates context switching to ensure callbacks execute on the correct thread.

## Collaboration Patterns

`AsyncSocket` operates as a low-level transport layer, driven by higher-level socket classes and coordinated by the global I/O context.

*   **Inheritance and Usage**: Classes such as `AuthSocket`, `WorldSocket`, and `RASocket` inherit from `AsyncSocket`. They utilize its `Read`, `Write`, and `CloseSocket` methods to exchange data with clients. For example, `AuthSocket` uses `Read` to receive authentication challenges and `Write` to send realm lists, while `WorldSocket` uses these methods for game world packet exchange.
*   **Event Loop Integration**: The `AsyncSocket._posix` partial registers the socket with the `IoContext` (the central event loop) during initialization. When I/O events occur (e.g., data available for reading), the `IoContext` invokes `OnIoEvent`, which dispatches to `PerformNonBlockingRead` or `PerformNonBlockingWrite`.
*   **Thread Safety and Context Switching**: To maintain thread safety, `AsyncSocket` uses an atomic state machine to prevent concurrent I/O operations. The `EnterIoContext` method allows other parts of the server to schedule callbacks on the I/O thread, ensuring that socket state mutations and packet sending occur in the correct context. This is particularly important for `WorldSocket::SendPacket`, which uses `EnterIoContext` to serialize packet sending.
*   **Resource Management**: The class relies on `SocketDescriptor` for managing the underlying OS socket handle and `ReadableBuffer` for data transmission. Upon destruction, `AsyncSocket` ensures that all pending operations are aborted and resources are released, logging the remote IP for debugging purposes.

## Data Model

`AsyncSocket` operates entirely in memory and interacts with the OS network stack. It does **not** access any database tables. Its state is maintained through atomic flags and in-memory buffers, with no persistence to the `mangos`, `characters`, `realmd`, or `logs` databases.

## Where to Go Deeper

*   **`AsyncSocket.Main`**: Open this doc to understand the class’s lifecycle, move semantics, and how it manages atomic state flags to ensure thread-safe cleanup and prevent double-closure. It details the construction, destruction, and utility methods for remote peer identification.
*   **`AsyncSocket._posix`**: Open this doc to explore the POSIX-specific I/O implementation, including integration with `epoll`/`kqueue`, handling of partial reads/writes, and the mechanisms for context switching and graceful shutdown. It explains how the socket is registered with the kernel event loop and how I/O events are dispatched.

---

<!-- machine-true, projected from graph.json -->

## Map — AsyncSocket

*Source:* AsyncSocket.cpp, AsyncSocket.h, AsyncSocket_posix.cpp

| Member | Partial | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|---|
| AsyncSocket | AsyncSocket.Main | ctor | ReadableBuffer/ReadableBuffer#2, SocketDescriptor/SocketDescriptor | AuthSocket/AuthSocket, RASocket/RASocket, WorldSocket/WorldSocket | — |
| ~AsyncSocket | AsyncSocket.Main | dtor | Errors/PrintStacktraceAndThrow, Log.Main/Out, SocketDescriptor/CloseSocket | — | — |
| IsClosing | AsyncSocket.Main | method | — | AsyncSocket._posix/SetNativeSocketOption_NoDelay, AsyncSocket._posix/SetNativeSocketOption_SystemOutgoingSendBuffer | — |
| ReadSkip | AsyncSocket.Main | method | AsyncSocket._posix/Read | AuthSocket/_HandleRealmList | — |
| GetRemoteEndpoint | AsyncSocket.Main | method | — | AuthSocket/LoadRealmlistAndWriteIntoBuffer | — |
| GetRemoteIpString | AsyncSocket.Main | method | — | AsyncSocket._posix/CloseSocket, AsyncSocket._posix/OnIoEvent, AsyncSocket._posix/PerformNonBlockingRead, AsyncSocket._posix/PerformNonBlockingWrite, AsyncSocket._posix/Read, AsyncSocket._posix/ReadSome, AuthSocket/AuthSocket, RASocket/DoRecvIncomingData, RASocket/HandleInput_Authenticated, RASocket/HandleInput_GotUsername, RASocket/SendAndDisconnect, RASocket/SendAndRecvNextInput, RASocket/Start, RASocket/~RASocket, realmd_Main/main, WorldSocket/DoRecvIncomingData, WorldSocket/WorldSocket, WorldSocketMgr/OnNewClientConnected | — |
| AsyncSocket | AsyncSocket._posix | ctor | SocketDescriptor/SocketDescriptor | Master/SetupRemoteAccessServer, realmd_Main/main, WorldSocketMgr/OnNewClientConnected | — |
| InitializeAndFixateMemoryLocation | AsyncSocket._posix | method | Errors/PrintStacktraceAndThrow, IoContext/GetUnixEpollDescriptor, Log.Main/Out, NetworkError/NetworkError, NetworkError/NetworkError#2, SocketDescriptor/GetNativeSocket, SystemErrorToString/SystemErrorToString | RASocket/Start, realmd_Main/main, WorldSocketMgr/OnNewClientConnected | — |
| Read | AsyncSocket._posix | method | AsyncSocket.Main/GetRemoteIpString, Log.Main/Out, NetworkError/NetworkError, NetworkError/NetworkError#2, SocketDescriptor/GetNativeSocket | AsyncSocket.Main/ReadSkip, AuthSocket/DoRecvIncomingData, AuthSocket/_HandleLogonChallenge, AuthSocket/_HandleLogonProof, AuthSocket/_HandleReconnectChallenge, AuthSocket/_HandleReconnectProof, AuthSocket/_HandleXferResume, ProxyV2Reader/ReadProxyV2Handshake, WorldSocket/DoRecvIncomingData | — |
| ReadSome | AsyncSocket._posix | method | AsyncSocket.Main/GetRemoteIpString, Log.Main/Out, NetworkError/NetworkError, NetworkError/NetworkError#2, SocketDescriptor/GetNativeSocket | RASocket/DoRecvIncomingData | — |
| Write | AsyncSocket._posix | method | Log.Main/Out, NetworkError/NetworkError, NetworkError/NetworkError#2, ReadableBuffer/GetPtr, ReadableBuffer/GetSize, ReadableBuffer/operator=#2, SocketDescriptor/GetNativeSocket | AuthSocket/RepeatInternalXferLoop, AuthSocket/_HandleLogonChallenge, AuthSocket/_HandleLogonProof__PostRecv, AuthSocket/_HandleLogonProof__PostRecv_HandleInvalidVersion, AuthSocket/_HandleRealmList, AuthSocket/_HandleReconnectChallenge, AuthSocket/_HandleReconnectProof, RASocket/DoRecvIncomingData, RASocket/SendAndDisconnect, RASocket/SendAndRecvNextInput, WorldSocket/HandleResultOfAsyncWrite | — |
| CloseSocket | AsyncSocket._posix | method | AsyncSocket.Main/GetRemoteIpString, Log.Main/Out, SocketDescriptor/GetNativeSocket | AuthSocket/CloseSocket, WorldSocket/CloseSocket | — |
| PerformNonBlockingRead | AsyncSocket._posix | method | AsyncSocket.Main/GetRemoteIpString, Log.Main/Out, NetworkError/NetworkError, SocketDescriptor/GetNativeSocket, SystemErrorToString/SystemErrorToString | — | — |
| PerformNonBlockingWrite | AsyncSocket._posix | method | AsyncSocket.Main/GetRemoteIpString, Log.Main/Out, NetworkError/NetworkError, ReadableBuffer/GetPtr, ReadableBuffer/GetSize, ReadableBuffer/operator=#3, SocketDescriptor/GetNativeSocket, SystemErrorToString/SystemErrorToString | — | — |
| PerformContextSwitch | AsyncSocket._posix | method | Errors/PrintStacktraceAndThrow, NetworkError/NetworkError | — | — |
| StopPendingTransactionsAndForceClose | AsyncSocket._posix | method | NetworkError/NetworkError, ReadableBuffer/operator=#3 | — | — |
| EnterIoContext | AsyncSocket._posix | method | IoContext_linux/PostForImmediateInvocation, NetworkError/NetworkError | WorldSocket/SendPacket | — |
| OnIoEvent | AsyncSocket._posix | method | AsyncSocket.Main/GetRemoteIpString, Log.Main/Out, SocketDescriptor/GetNativeSocket, SystemErrorToString/SystemErrorToString | — | — |
| SetNativeSocketOption_NoDelay | AsyncSocket._posix | method | AsyncSocket.Main/IsClosing, NetworkError/FromSystemError, NetworkError/NetworkError, SocketDescriptor/GetNativeSocket | WorldSocketMgr/OnNewClientConnected | — |
| SetNativeSocketOption_SystemOutgoingSendBuffer | AsyncSocket._posix | method | AsyncSocket.Main/IsClosing, Errors/PrintStacktraceAndThrow, NetworkError/FromSystemError, NetworkError/NetworkError, SocketDescriptor/GetNativeSocket | WorldSocketMgr/OnNewClientConnected | — |
