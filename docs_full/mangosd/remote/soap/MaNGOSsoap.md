# MaNGOSsoap

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`MaNGOSsoap` provides a SOAP-based remote administration interface for the MaNGOS server. It runs a dedicated listener thread that accepts HTTP/SOAP connections, authenticates clients against the server's account system, and executes console commands on behalf of authorized administrators. Because command execution must occur on the main game loop thread (`World`), this unit bridges the asynchronous SOAP request/response cycle with synchronous command execution by queuing commands to `World` and blocking the SOAP thread until completion.

## Member-by-Member Behavior

### Thread Lifecycle

**`StartSoapThread`** initializes the gSOAP runtime, configures UTF-8 encoding and timeouts (3s accept, 5s send/recv), and binds a socket to the specified host and port. If binding succeeds, it spawns a background thread running `SoapThreadBody` via `IO::Multithreading::CreateThreadPtr` and returns a `unique_ptr` to the thread. On failure, it cleans up the soap context and returns `nullptr`.

**`SoapThreadBody`** runs the listener loop. While `World::IsStopped()` is false, it accepts connections, logs the client IP (converted via `IO::Networking::IpAddress`), and calls `soap_serve` to handle the request. Upon shutdown, it cleans up the soap context.

### Command Execution

**`ns1__executeCommand`** is the SOAP endpoint handler. It performs strict authentication: verifying HTTP Basic Auth credentials via `AccountMgr` (`GetId`, `CheckPassword`) and ensuring the account has `SEC_ADMINISTRATOR` security or higher. It validates that the `command` parameter is non-empty. To execute the command, it creates a local `SOAPCommand` instance and a heap-allocated `CliCommandHolder` containing the command and callbacks (`OnPrint`, `OnCommandFinished`). It queues the holder to `World::QueueCliCommand` and immediately blocks on `SOAPCommand::WaitAndGetSuccessStatus`. Once the `World` thread finishes execution and signals completion, the function copies the accumulated output buffer to the SOAP response. If the command failed, it returns a SOAP fault; otherwise, it returns the output string.

### Synchronization Helpers (`SOAPCommand` class)

The local `SOAPCommand` class facilitates communication between the SOAP thread and the `World` thread.

**`WaitAndGetSuccessStatus`** blocks the SOAP thread by waiting on a `std::future<bool>` until the `World` thread signals completion.

**`OnPrint`** is a static callback invoked by `CliCommandHolder` during execution. It appends output strings to the `SOAPCommand` instance's `m_printBuffer`.

**`OnCommandFinished`** is a static callback invoked by `CliCommandHolder` upon termination. It sets the promise value, unblocking `WaitAndGetSuccessStatus`.

## Cross-Unit Boundaries

*   **`AccountMgr`**: Called by `ns1__executeCommand` to resolve usernames to IDs, verify passwords, and check security levels.
*   **`World`**: Called by `SoapThreadBody` (`IsStopped`) to manage the listener loop lifecycle, and by `ns1__executeCommand` (`QueueCliCommand`) to offload command execution to the main thread.
*   **`CliCommandHolder`**: Constructed by `ns1__executeCommand` to package the command and callbacks for the `World` thread.
*   **`IO::Networking::IpAddress`**: Called by `SoapThreadBody` to format client IPs for logging.
*   **`IO::Multithreading::CreateThread`**: Called by `StartSoapThread` to spawn the listener thread.
*   **`Log.Main`**: Called by all major functions for operational logging (bindings, connections, auth failures, errors).

## Data Model

This unit does not interact with database tables directly. Authentication data is accessed exclusively through `AccountMgr`.

## Notable Implementation Details

*   **Blocking Design**: `ns1__executeCommand` blocks the SOAP thread for the entire duration of the command execution. Long-running commands will tie up a connection slot.
*   **Ownership Transfer**: `CliCommandHolder` is allocated on the heap and passed to `World`. The `World` thread takes ownership and deletes it; `ns1__executeCommand` must not access the pointer after queuing.
*   **Callback Pattern**: Static methods `OnPrint` and `OnCommandFinished` use an `opaquePointer` cast to `SOAPCommand*` to update state in the SOAP thread's stack frame from the `World` thread.
*   **Namespace Definition**: The `namespaces` array is required by gSOAP to map the `ns1` prefix to `urn:MaNGOS`.

## Member Reference

**`WaitAndGetSuccessStatus`**: Method of local `SOAPCommand` class. Blocks the caller until the command execution completes by waiting on the internal `std::future<bool>`. Returns the success status set by `OnCommandFinished`.

**`OnPrint`**: Static method of local `SOAPCommand` class. Callback invoked by `CliCommandHolder` to append output text to the `m_printBuffer` of the `SOAPCommand` instance.

**`OnCommandFinished`**: Static method of local `SOAPCommand` class. Callback invoked by `CliCommandHolder` upon command completion. Sets the value of the `m_successStatusPromise` to unblock `WaitAndGetSuccessStatus`.

**`SoapThreadBody`**: Function. Main loop for the SOAP listener thread. Accepts connections, logs client IPs via `IO::Networking::IpAddress`, and serves SOAP requests via `soap_serve`. Runs until `World::IsStopped()` is true, then cleans up the soap context.

**`StartSoapThread`**: Function. Initializes the gSOAP context, binds to the specified host/port, and spawns the `SoapThreadBody` thread via `IO::Multithreading::CreateThreadPtr`. Returns a `unique_ptr` to the thread or `nullptr` on failure.

**`ns1__executeCommand`**: Function. The SOAP endpoint for executing commands. Authenticates the user via `AccountMgr`, validates the command, queues it to the `World` thread via `CliCommandHolder`, waits for completion using `SOAPCommand` synchronization, and returns the output or error as a SOAP response.

---

<!-- machine-true, projected from graph.json -->

## Map — MaNGOSsoap

*Source:* MaNGOSsoap.cpp, MaNGOSsoap.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| WaitAndGetSuccessStatus | method | — | — | — |
| OnPrint | method | — | — | — |
| OnCommandFinished | method | — | — | — |
| SoapThreadBody | function | IpAddress/FromIpv4Uint32, IpAddress/ToString, Log.Main/Out, World/IsStopped | — | — |
| StartSoapThread | function | CreateThread/CreateThreadPtr, Log.Main/Out | Master/Run | — |
| ns1__executeCommand | function | AccountMgr/CheckPassword, AccountMgr/GetId, AccountMgr/GetSecurity, CliCommandHolder/CliCommandHolder, Log.Main/Out, World/QueueCliCommand | — | — |
