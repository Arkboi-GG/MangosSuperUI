<!-- provenance: boundary-bleed -->
# ChatHandler.ChatCommands

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ChatHandler.ChatCommands

## Purpose & Responsibilities

`ChatHandler.ChatCommands` implements two administrative commands for monitoring and configuring the `MovementBroadcaster` subsystem. This subsystem manages the multi-threaded broadcasting of movement updates to connected clients. The unit provides:

1.  **Performance Inspection**: A command to view real-time statistics for each broadcaster thread, including update latency and packet throughput, as well as lifecycle counters for `PlayerBroadcaster` instances.
2.  **Dynamic Configuration**: A command to adjust the number of worker threads used by the `MovementBroadcaster` at runtime, including the ability to disable the broadcaster entirely by setting the thread count to zero.

These methods are part of the larger `ChatHandler` class, which parses and executes text commands from game chat or the server console. This specific partial contains only the logic for the "Packet Broadcast" (PBcast) related commands.

## Member-by-Member Behavior

### HandlePBCastStatsCommand
This method retrieves and displays current performance metrics for the `MovementBroadcaster`. It performs the following steps:
1.  Obtains a pointer to the global `MovementBroadcaster` instance by calling `World/GetBroadcaster`.
2.  Retrieves a snapshot of thread statistics by calling `MovementBroadcaster/GetStats`.
3.  Outputs the total number of active threads using `ChatHandler.Chat/PSendSysMessage`.
4.  Iterates through the returned statistics vector. For each thread, it prints the thread index, the time taken for the last update cycle (`update_time` in milliseconds), and the number of packets processed (`num_packets`).
5.  Reports lifecycle counters for `PlayerBroadcaster` objects. It accesses the static members `PlayerBroadcaster::num_bcaster_created` and `PlayerBroadcaster::num_bcaster_deleted` to display how many broadcaster instances have been created and deleted since server startup.

### HandlePBCastSetThreadsCommand
This method allows dynamic reconfiguration of the `MovementBroadcaster`'s thread pool size. It performs the following steps:
1.  Obtains the global `MovementBroadcaster` instance via `World/GetBroadcaster`.
2.  Records the current thread count by calling `MovementBroadcaster/GetNumThreads`.
3.  Parses the user-provided argument to extract the desired new thread count using `ChatHandler.Chat/ExtractUInt32`. If parsing fails, the command returns `false` immediately.
4.  Validates the input:
    *   If the requested thread count exceeds 50, it rejects the change, sends an error message via `ChatHandler.Chat/PSendSysMessage`, and returns `false`.
    *   If the requested count is 0, it sends a message indicating the broadcaster is being disabled.
    *   Otherwise, it sends a message indicating the transition from the old count to the new count.
5.  Applies the new configuration by calling `MovementBroadcaster/UpdateConfiguration`, passing the new thread count and the existing sleep timer (retrieved via `MovementBroadcaster/GetSleepTimer`).
6.  Confirms completion to the user via `ChatHandler.Chat/SendSysMessage` and returns `true`.

## Cross-Unit Boundaries

### Collaboration with MovementBroadcaster
Both methods rely on the `MovementBroadcaster` unit (`MovementBroadcaster.cpp`) to manage the underlying threading logic.
*   **HandlePBCastStatsCommand**: Calls `MovementBroadcaster/GetStats` to retrieve a container of performance structs. It reads the `update_time` and `num_packets` fields from these structs to report per-thread performance.
*   **HandlePBCastSetThreadsCommand**: Calls `MovementBroadcaster/GetNumThreads` to read the current state, `MovementBroadcaster/GetSleepTimer` to preserve timing configuration during reconfiguration, and `MovementBroadcaster/UpdateConfiguration` to apply the new thread count.

### Collaboration with World
Both methods obtain the singleton instance of the broadcaster by calling `World/GetBroadcaster` (`World.cpp`). This establishes the dependency on the global world state object to access the broadcaster subsystem.

### Collaboration with ChatHandler.Chat
The methods use helper functions defined in the main `ChatHandler` unit (`Chat.cpp`) for I/O and parsing. Note that while these helpers are declared in the shared `Chat.h` header, their implementations reside in the `ChatHandler.Chat` partial.
*   **HandlePBCastStatsCommand**: Uses `ChatHandler.Chat/PSendSysMessage` to format and send output strings to the user.
*   **HandlePBCastSetThreadsCommand**: Uses `ChatHandler.Chat/ExtractUInt32` to parse integer arguments, and `ChatHandler.Chat/PSendSysMessage` and `ChatHandler.Chat/SendSysMessage` for output.

### Collaboration with PlayerBroadcaster
`HandlePBCastStatsCommand` accesses static member variables `num_bcaster_created` and `num_bcaster_deleted` from the `PlayerBroadcaster` class (`PlayerBroadcaster.cpp`). This indicates that `PlayerBroadcaster` instances are likely managed per-player or per-entity within the movement broadcasting system, and these counters track memory allocation/deallocation events for debugging purposes.

## Data Model

This unit does not interact with any database tables. All data is retrieved from in-memory objects (`MovementBroadcaster`, `PlayerBroadcaster`) and global state (`World`).

## Notable Implementation Details

*   **Hardcoded Thread Limit**: In `HandlePBCastSetThreadsCommand`, there is a hardcoded upper limit of 50 threads (`if (num_threads_after > 50)`). Attempting to set the thread count higher than this results in a rejection message. This suggests a design decision to prevent excessive resource consumption or context-switching overhead, though the rationale is not documented in the code.
*   **Disabling the Broadcaster**: Setting the thread count to 0 is explicitly handled as a valid operation to disable the broadcaster. The code prints "Disabling broadcaster..." but still calls `UpdateConfiguration`. The behavior of `UpdateConfiguration` when passed 0 threads is determined by the `MovementBroadcaster` unit, but likely involves stopping all worker threads.
*   **Static Counters**: The use of static counters in `PlayerBroadcaster` (`num_bcaster_created`, `num_bcaster_deleted`) implies that these objects are frequently created and destroyed. Monitoring the difference between these two numbers can help identify memory leaks if the difference grows unexpectedly over time.
*   **Argument Parsing Failure**: If `ExtractUInt32` fails in `HandlePBCastSetThreadsCommand`, the method returns `false` immediately without sending an error message. The responsibility for displaying an error message likely lies in the caller (the main command dispatch loop in `ChatHandler`), which typically checks the return value of command handlers.

## Member Reference

**HandlePBCastStatsCommand**
Retrieves and displays performance statistics for the `MovementBroadcaster` threads, including update times, packet counts, and `PlayerBroadcaster` lifecycle counters.

**HandlePBCastSetThreadsCommand**
Allows dynamic adjustment of the `MovementBroadcaster` thread count, enforcing a maximum limit of 50 threads and supporting disabling the broadcaster by setting the count to 0.

---

<!-- machine-true, projected from graph.json -->

## Map — ChatHandler.ChatCommands

*Source:* ChatCommands.cpp, Chat.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| HandlePBCastStatsCommand | method | ChatHandler.Chat/PSendSysMessage, MovementBroadcaster/GetStats, World/GetBroadcaster | — | — |
| HandlePBCastSetThreadsCommand | method | ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, MovementBroadcaster/GetNumThreads, MovementBroadcaster/GetSleepTimer, MovementBroadcaster/UpdateConfiguration, World/GetBroadcaster | — | — |

---

<!-- verify: boundary-bleed | foreign: ChatHandler, disable, update -->
