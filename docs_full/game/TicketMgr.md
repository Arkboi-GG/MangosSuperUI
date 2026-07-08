# TicketMgr

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# TicketMgr

**TicketMgr** is the singleton manager responsible for the in-memory lifecycle, state, and retrieval of Game Master (GM) support tickets within the server. It acts as the central authority for ticket data, bridging the gap between persistent storage (database), client-side interactions (network opcodes), and administrative commands (chat interface).

The unit maintains a `std::map` (`_ticketList`) of active `GmTicket` objects, keyed by ticket ID. It handles the generation of unique ticket IDs, tracks the global enable/disable status of the ticket system, and manages auxiliary counters such as survey IDs and open ticket counts. While the `GmTicket` class (defined in the same header) holds the specific data for a single ticket, **TicketMgr** orchestrates the collection, ensuring that operations like assignment, closure, and escalation are reflected consistently across all clients and administrative tools.

## Member-by-Member Behavior

### Initialization and Lifecycle Management

*   **`instance`**: Implements the Meyers Singleton pattern. It provides global access to the single `TicketMgr` object via the macro `sTicketMgr`. This ensures that all parts of the server (chat handlers, world sessions, player logic) interact with the same ticket data store.
*   **`Initialize`**: Called during server startup (`World/SetInitialWorldSettings`). It sets the initial status of the ticket system (enabled/disabled) based on configuration and likely triggers the loading of existing tickets from the database.
*   **`ResetTickets`**: Clears the in-memory ticket list. This is typically used during a reload command or server restart to wipe stale data before reloading fresh records from the database.

### Ticket Retrieval and Navigation

These methods provide various ways to locate specific tickets within `_ticketList`. They are heavily used by chat commands to allow GMs to navigate through queues.

*   **`GetTicket`**: Retrieves a ticket by its unique numeric ID. Returns `nullptr` if not found. Used by most administrative commands (assign, close, delete, escalate) to target a specific ticket.
*   **`GetTicketByPlayer`**: Iterates through `_ticketList` to find an *open* ticket associated with a specific `ObjectGuid` (player). Crucially, it ignores closed tickets, ensuring that players cannot interact with resolved issues. Used when a player attempts to create, update, or delete their own ticket.
*   **`GetOldestOpenTicket`**: Returns the first open, non-completed ticket in the map. Since `_ticketList` is a `std::map` ordered by key (ID), and IDs are monotonically increasing, this effectively returns the oldest active ticket. Used by `WritePacket` to broadcast the current head of the queue to clients.
*   **`GetNextTicket`**: Finds the next open, non-completed ticket with an ID greater than the provided `counter`. Allows GMs to step forward through the queue.
*   **`GetPreviousTicket`**: Uses a reverse iterator to find the previous open, non-completed ticket with an ID less than the provided `counter`. Allows GMs to step backward through the queue.

### State and Status Management

*   **`GetStatus` / `SetStatus`**: Manages the boolean `_status` flag indicating whether the ticket system is globally enabled. `SetStatus` is called by the toggle command and during initialization. `GetStatus` is checked before allowing new ticket creation or reporting system status to clients.
*   **`GetLastChange` / `UpdateLastChange`**: Tracks the timestamp of the last modification to the ticket system (e.g., assignment, closure). `UpdateLastChange` is called whenever a ticket's state changes significantly. `GetLastChange` is used by `WritePacket` to inform clients if they need to refresh their local ticket lists.
*   **`GetLastTicketId` / `GenerateTicketId`**: Manages the `_lastTicketId` counter. `GenerateTicketId` increments and returns the next available ID, ensuring uniqueness. `GetLastTicketId` exposes the current maximum ID, used by the counter command and potentially for synchronization.
*   **`GetOpenTicketCount`**: Returns the cached count of open tickets (`_openTicketCount`). Used by the reset command to report statistics.
*   **`GetNextSurveyID`**: Increments and returns a unique ID for customer satisfaction surveys. Decoupled from ticket IDs, this allows surveys to be tracked independently.

### Administrative and Utility Operations

*   **`ShowList`, `ShowClosedList`, `ShowEscalatedList`**: Helper methods that format and send lists of tickets to a `ChatHandler`. These abstract the iteration and formatting logic required for the various `/gticket list` subcommands.
*   **`SendTicket`**: Serializes a specific `GmTicket` into a network packet and sends it to a `WorldSession`. Used when a GM views a specific ticket.
*   **`ReloadTicket` / `ReloadTicketCallback`**: Handles the asynchronous reloading of a specific ticket from the database. `ReloadTicket` initiates the query, and `ReloadTicketCallback` processes the result, updating the in-memory object. This ensures data consistency after manual database edits or corruption recovery.

## Cross-Unit Boundaries

**TicketMgr** serves as a hub, interacting with three primary domains:

1.  **ChatHandler.TicketCommands**:
    *   **Direction**: ChatHandler -> TicketMgr.
    *   **Collaboration**: Almost all administrative commands (`HandleGMTicketAssignToCommand`, `HandleGMTicketCloseByIdCommand`, etc.) call `TicketMgr` methods to retrieve tickets (`GetTicket`), modify state (`UpdateLastChange`), or generate IDs (`GenerateTicketId`). The chat handler acts as the user interface, while `TicketMgr` performs the business logic and data manipulation.

2.  **WorldSession.GMTicketHandler**:
    *   **Direction**: WorldSession -> TicketMgr.
    *   **Collaboration**: Client-side actions (creating a ticket, updating text, submitting a survey) trigger opcodes handled by `WorldSession`. These handlers call `TicketMgr` to validate existence (`GetTicketByPlayer`), check system status (`GetStatus`), and persist changes. For example, `HandleGMTicketCreateOpcode` checks if a ticket already exists via `GetTicketByPlayer` before calling `AddTicket` (implicitly via `GmTicket` construction and insertion).

3.  **Player.Main / GMTicketMgr Internal Logic**:
    *   **Direction**: Mixed.
    *   **Collaboration**: `Player.Main/Player#5` calls `GetLastTicketId` likely for debugging or logging purposes. Internally, `GmTicket` methods (like `SaveToDB`) are called by `TicketMgr` methods (like `CloseTicket`) to persist changes. Note that `GmTicket` is a separate class but defined in the same header; `TicketMgr` owns the collection of these objects.

## Data Model

**TicketMgr** itself does not execute SQL queries directly in the provided source snippet. However, it manages `GmTicket` objects which contain methods `LoadFromDB`, `SaveToDB`, and `DeleteFromDB`. These methods imply interaction with database tables (likely `gm_ticket` and related tables for comments/history).

The MAP indicates that `TicketMgr` members do not directly touch tables, but the `GmTicket` class (which `TicketMgr` manages) does. The specific table structures are not provided in the SCHEMA section, so we rely on the code's usage:
*   Tickets are loaded into memory at startup (`LoadTickets`).
*   Changes are persisted immediately upon action (e.g., `CloseTicket` likely calls `SaveToDB` on the ticket object).
*   The `ReloadTicket` mechanism suggests that direct database updates bypassing the game server require a manual reload to sync the in-memory state.

## Notable Implementation Details

1.  **Map Ordering for Queue Logic**:
    The `_ticketList` is a `std::map<uint32, GmTicket*>`. Because `std::map` keeps keys sorted, and ticket IDs are generated sequentially via `GenerateTicketId`, iterating from `begin()` to `end()` naturally yields tickets in chronological order. This allows `GetOldestOpenTicket` and `GetNextTicket` to work efficiently without needing a separate priority queue or timestamp sorting.

2.  **Singleton Pattern**:
    The use of `static TicketMgr instance` inside `instance()` is thread-safe in C++11 and later. This ensures safe concurrent access from multiple threads (e.g., different player sessions) assuming internal data structures are protected by locks elsewhere (not visible in this unit, but critical for correctness).

3.  **Soft Deletes vs. Hard Deletes**:
    `RemoveTicket` removes the ticket from `_ticketList`. However, `CloseTicket` marks a ticket as closed (`SetClosedBy`) but does *not* remove it from the list immediately. This allows closed tickets to remain accessible for review (`ShowClosedList`) until they are explicitly deleted or aged out. The `GetTicketByPlayer` method explicitly filters out closed tickets, preventing players from reopening them.

4.  **Survey ID Separation**:
    Survey IDs are managed separately from ticket IDs (`_lastSurveyId` vs `_lastTicketId`). This decoupling allows surveys to be submitted independently of ticket creation or closure, supporting a more flexible feedback loop.

5.  **No Direct SQL in TicketMgr**:
    The `TicketMgr` class delegates database persistence to the `GmTicket` class. This separation of concerns keeps the manager focused on orchestration and retrieval, while the entity class handles its own serialization.

## Member Reference

*   **`instance`**: Returns the singleton pointer to the `TicketMgr` object.
*   **`GetTicket`**: Retrieves a `GmTicket` pointer by its unique ID from `_ticketList`.
*   **`GetTicketByPlayer`**: Iterates `_ticketList` to find an open ticket belonging to the specified player GUID.
*   **`GetOldestOpenTicket`**: Returns the first open, non-completed ticket in the sorted map (oldest by ID).
*   **`GetNextTicket`**: Finds the next open ticket with an ID greater than the given counter.
*   **`GetPreviousTicket`**: Finds the previous open ticket with an ID less than the given counter using reverse iteration.
*   **`GetStatus`**: Returns the boolean flag indicating if the ticket system is enabled.
*   **`SetStatus`**: Sets the boolean flag for the ticket system's enabled state.
*   **`GetLastChange`**: Returns the timestamp of the last significant change to the ticket system.
*   **`UpdateLastChange`**: Updates the last change timestamp to the current time.
*   **`GetLastTicketId`**: Returns the highest ticket ID generated so far.
*   **`GenerateTicketId`**: Increments and returns the next unique ticket ID.
*   **`GetOpenTicketCount`**: Returns the cached count of currently open tickets.
*   **`GetNextSurveyID`**: Increments and returns the next unique survey ID.

---

<!-- machine-true, projected from graph.json -->

## Map — TicketMgr

*Source:* GMTicketMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| instance | method | — | ChatHandler.TicketCommands/HandleGMTicketAssignToCommand, ChatHandler.TicketCommands/HandleGMTicketCloseByIdCommand, ChatHandler.TicketCommands/HandleGMTicketCommentCommand, ChatHandler.TicketCommands/HandleGMTicketCompleteCommand, ChatHandler.TicketCommands/HandleGMTicketCounterCommand, ChatHandler.TicketCommands/HandleGMTicketDeleteByIdCommand, ChatHandler.TicketCommands/HandleGMTicketEscalateCommand, ChatHandler.TicketCommands/HandleGMTicketListClosedCommand, ChatHandler.TicketCommands/HandleGMTicketListCommand, ChatHandler.TicketCommands/HandleGMTicketListEscalatedCommand, ChatHandler.TicketCommands/HandleGMTicketListOnlineCommand, ChatHandler.TicketCommands/HandleGMTicketNextCommand, ChatHandler.TicketCommands/HandleGMTicketPreviousCommand, ChatHandler.TicketCommands/HandleGMTicketReloadCommand, ChatHandler.TicketCommands/HandleGMTicketResetCommand, ChatHandler.TicketCommands/HandleGMTicketResponseResetCommand, ChatHandler.TicketCommands/HandleGMTicketUnAssignCommand, ChatHandler.TicketCommands/HandleToggleGMTicketSystem, ChatHandler.TicketCommands/ViewTicket, ChatHandler.TicketCommands/ViewTicketByIdOrName, ChatHandler.TicketCommands/_HandleGMTicketResponseAppendCommand, GMTicketMgr/GmTicket#2, GMTicketMgr/ResetTickets, GMTicketMgr/WritePacket, Player.Main/Player#5, World/SetInitialWorldSettings, WorldSession.GMTicketHandler/HandleGMSurveySubmitOpcode, WorldSession.GMTicketHandler/HandleGMTicketCreateOpcode, WorldSession.GMTicketHandler/HandleGMTicketDeleteTicketOpcode, WorldSession.GMTicketHandler/HandleGMTicketGetTicketOpcode, WorldSession.GMTicketHandler/HandleGMTicketSystemStatusOpcode, WorldSession.GMTicketHandler/HandleGMTicketUpdateTextOpcode | — |
| GetTicket | method | — | ChatHandler.TicketCommands/HandleGMTicketAssignToCommand, ChatHandler.TicketCommands/HandleGMTicketCloseByIdCommand, ChatHandler.TicketCommands/HandleGMTicketCommentCommand, ChatHandler.TicketCommands/HandleGMTicketCompleteCommand, ChatHandler.TicketCommands/HandleGMTicketDeleteByIdCommand, ChatHandler.TicketCommands/HandleGMTicketEscalateCommand, ChatHandler.TicketCommands/HandleGMTicketResponseResetCommand, ChatHandler.TicketCommands/HandleGMTicketUnAssignCommand, ChatHandler.TicketCommands/ViewTicketByIdOrName, ChatHandler.TicketCommands/_HandleGMTicketResponseAppendCommand, GMTicketMgr/CloseTicket, GMTicketMgr/ReloadTicketCallback, GMTicketMgr/RemoveTicket | — |
| GetTicketByPlayer | method | — | ChatHandler.TicketCommands/ViewTicketByIdOrName, WorldSession.GMTicketHandler/HandleGMTicketCreateOpcode, WorldSession.GMTicketHandler/HandleGMTicketDeleteTicketOpcode, WorldSession.GMTicketHandler/HandleGMTicketGetTicketOpcode, WorldSession.GMTicketHandler/HandleGMTicketUpdateTextOpcode | — |
| GetOldestOpenTicket | method | — | GMTicketMgr/WritePacket | — |
| GetNextTicket | method | — | ChatHandler.TicketCommands/HandleGMTicketNextCommand | — |
| GetPreviousTicket | method | — | ChatHandler.TicketCommands/HandleGMTicketPreviousCommand | — |
| GetStatus | method | — | ChatHandler.TicketCommands/HandleToggleGMTicketSystem, WorldSession.GMTicketHandler/HandleGMTicketCreateOpcode, WorldSession.GMTicketHandler/HandleGMTicketSystemStatusOpcode | — |
| SetStatus | method | — | ChatHandler.TicketCommands/HandleToggleGMTicketSystem, GMTicketMgr/Initialize | — |
| GetLastChange | method | — | GMTicketMgr/WritePacket | — |
| UpdateLastChange | method | — | ChatHandler.TicketCommands/HandleGMTicketAssignToCommand, ChatHandler.TicketCommands/HandleGMTicketCloseByIdCommand, ChatHandler.TicketCommands/HandleGMTicketCommentCommand, ChatHandler.TicketCommands/HandleGMTicketCompleteCommand, ChatHandler.TicketCommands/HandleGMTicketDeleteByIdCommand, ChatHandler.TicketCommands/HandleGMTicketEscalateCommand, ChatHandler.TicketCommands/HandleGMTicketUnAssignCommand, WorldSession.GMTicketHandler/HandleGMTicketCreateOpcode | — |
| GetLastTicketId | method | — | ChatHandler.TicketCommands/HandleGMTicketCounterCommand, Player.Main/Player#5 | — |
| GenerateTicketId | method | — | GMTicketMgr/GmTicket#2 | — |
| GetOpenTicketCount | method | — | ChatHandler.TicketCommands/HandleGMTicketResetCommand | — |
| GetNextSurveyID | method | — | WorldSession.GMTicketHandler/HandleGMSurveySubmitOpcode | — |
