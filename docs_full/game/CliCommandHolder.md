# CliCommandHolder

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# CliCommandHolder

**CliCommandHolder** is a lightweight, stack-allocated storage struct defined in `World.h` within the `wowvmangos` codebase. It serves as a wrapper for administrative commands issued via external interfaces—specifically the Remote Administration (RA) socket interface and the SOAP web service interface—that require asynchronous or deferred execution on the main game server thread.

Its primary responsibility is to bundle a command string with the necessary context (issuer identity, privilege level, and callback pointers) so that the `World` singleton can safely process the command later, outside the immediate network I/O thread that received the raw input. It ensures that the command string persists in memory until processing is complete, handling its own heap allocation and deallocation.

## Purpose & Responsibilities

The `CliCommandHolder` struct addresses a specific concurrency and lifecycle problem in the server architecture:
1.  **Context Preservation:** Commands received via `RASocket` or `MaNGOSsoap` arrive in separate threads or contexts. To execute them correctly, the server needs to know *who* issued the command (`m_cliAccountId`) and *what privileges* they hold (`m_cliAccessLevel`).
2.  **String Lifetime Management:** The incoming command is typically a temporary buffer in the network handler. `CliCommandHolder` takes ownership of this string by copying it onto the heap, ensuring it remains valid when the main loop processes it.
3.  **Callback Routing:** It stores function pointers (`m_print` and `m_commandFinished`) and an opaque argument (`m_callbackArg`). These allow the command processor to send output back to the original requester (e.g., printing results to the RA console or returning XML to the SOAP client) once execution finishes.

It is **not** a class with complex behavior; it is a simple aggregate with manual memory management for the command string.

## Member-by-Member Behavior

### Construction and Destruction

*   **`CliCommandHolder` (Constructor)**
    *   **Purpose:** Initializes the holder with the issuer's metadata and the command string.
    *   **Behavior:**
        1.  Assigns `m_cliAccountId`, `m_cliAccessLevel`, `m_callbackArg`, `m_print`, and `m_commandFinished` from the arguments.
        2.  Calculates the length of the input `command` string (including the null terminator).
        3.  Allocates a new `char` array on the heap of that size.
        4.  Copies the input `command` into this new array using `memcpy`.
    *   **Note:** This is the only point where heap allocation occurs for this struct.

*   **`~CliCommandHolder` (Destructor)**
    *   **Purpose:** Cleans up resources.
    *   **Behavior:** Calls `delete[]` on `m_command`. This frees the heap memory allocated in the constructor.

### Data Members

*   **`m_cliAccountId`**: Stores the numeric ID of the account issuing the command. A value of `0` indicates the command originated from the local console, while non-zero values indicate remote sources (RA/SOAP).
*   **`m_cliAccessLevel`**: Stores the security level (`AccountTypes`) of the issuer. This is critical for permission checks during command execution.
*   **`m_callbackArg`**: An opaque `void*` pointer passed through to the callbacks. Typically points to the specific session or connection object associated with the requester.
*   **`m_command`**: A dynamically allocated `char*` holding the null-terminated command string. Owned exclusively by this struct.
*   **`m_print`**: A function pointer of type `Print` (`void (*)(void*, char const*)`). Used to stream output messages back to the requester.
*   **`m_commandFinished`**: A function pointer of type `CommandFinished` (`void (*)(void*, bool)`). Called when the command execution completes, indicating success or failure.

## Cross-Unit Boundaries

`CliCommandHolder` acts as a data bridge between network/interface layers and the core world simulation loop.

### Incoming Dependencies (Called By)

1.  **`CliRunnable/operator()`**
    *   **Direction:** `CliRunnable` creates a `CliCommandHolder` instance.
    *   **Context:** This likely represents the local console command handler. When a GM types a command in the server console, `CliRunnable` wraps it in a `CliCommandHolder` (with `m_cliAccountId` = 0) to pass it into the processing pipeline.

2.  **`MaNGOSsoap/ns1__executeCommand`**
    *   **Direction:** The SOAP web service handler creates a `CliCommandHolder`.
    *   **Context:** When an external tool sends a command via the SOAP API, this method parses the request and constructs a `CliCommandHolder` containing the SOAP session's account ID, access level, and callbacks to return the result over HTTP/XML.

3.  **`RASocket/HandleInput_Authenticated`**
    *   **Direction:** The Remote Administration socket handler creates a `CliCommandHolder`.
    *   **Context:** When a GM connects via the RA protocol (port 8085 usually), incoming command packets are parsed here. A `CliCommandHolder` is instantiated to capture the command and link it back to the specific TCP connection for response delivery.

### Outgoing Dependencies (Calls Into)

*   **None.** The `CliCommandHolder` struct itself performs no calls to other units. It is purely a data container. Its members are consumed by `World::ProcessCliCommands` (defined in `World.cpp`, not shown here but referenced in `World.h`), which dequeues these holders and executes the commands.

## Data Model

This unit does not interact with any database tables. It operates entirely in memory, managing transient command strings and metadata.

## Notable Implementation Details

1.  **Manual Memory Management:**
    Unlike modern C++ idioms that might use `std::string`, `CliCommandHolder` uses raw `new char[]` and `delete[]`. This suggests the code was written with performance or legacy compatibility in mind, avoiding the overhead of `std::string` construction/destruction in a high-frequency path. However, it requires strict adherence to RAII: if the constructor throws (unlikely here, but possible if `new` fails), the destructor won't run, leading to a leak. In practice, `strlen` and `memcpy` are very low-risk.

2.  **Opaque Callbacks:**
    The use of `void* m_callbackArg` and function pointers allows `CliCommandHolder` to remain decoupled from the specific implementations of `RASocket` and `MaNGOSsoap`. Each interface passes its own context object and function addresses, enabling polymorphic behavior without inheritance.

3.  **Thread Safety Implications:**
    While the struct itself is not thread-safe, it is designed to be moved from a producer thread (network handler) to a consumer thread (main world loop) via the `LockedQueue<CliCommandHolder*>` in `World`. The heap allocation ensures the data survives the handoff. The caller is responsible for ensuring the `CliCommandHolder` object itself (the struct instance) is properly managed (likely deleted by the queue processor after use).

4.  **Console vs. Remote Distinction:**
    The comment `// 0 for console and real account id for RA/soap` on `m_cliAccountId` is crucial. Logic downstream (in `World::ProcessCliCommands` or the command handlers themselves) likely uses this to determine whether to print output to the server console log or invoke the `m_print` callback.

## Member Reference

**CliCommandHolder**
Constructor. Initializes all members. Allocates a new `char` array on the heap and copies the input `command` string into it. Sets `m_cliAccountId`, `m_cliAccessLevel`, `m_callbackArg`, `m_print`, and `m_commandFinished`.

**~CliCommandHolder**
Destructor. Frees the heap-allocated `m_command` string using `delete[]`.

---

<!-- machine-true, projected from graph.json -->

## Map — CliCommandHolder

*Source:* World.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| CliCommandHolder | ctor | — | CliRunnable/operator(), MaNGOSsoap/ns1__executeCommand, RASocket/HandleInput_Authenticated | — |
| ~CliCommandHolder | dtor | — | — | — |
