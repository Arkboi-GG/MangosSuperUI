# GMTicketMgr

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# GMTicketMgr

## Purpose & Responsibilities

`GMTicketMgr` implements the server-side logic for the Game Master (GM) Ticket system, a support mechanism allowing players to submit requests for assistance (e.g., stuck characters, bug reports, harassment complaints) to staff. The unit consists of two primary classes:

1.  **`GmTicket`**: Represents a single support ticket. It encapsulates the ticket's metadata (ID, creator, location, message), state (open, closed, completed, escalated), and persistence logic (loading/saving to the `gm_tickets` database table). It also handles serialization of ticket data into network packets for transmission to the client.
2.  **`TicketMgr`**: A singleton manager responsible for the lifecycle of all active tickets. It loads tickets from the database at startup, maintains an in-memory cache (`std::map<uint32, GmTicket*>`), manages ticket creation, closure, and deletion, and provides utilities for listing and filtering tickets for GMs. It also tracks the highest used ticket ID and survey ID to ensure uniqueness.

The system supports ticket assignment to specific GMs, escalation queues for high-priority issues, and basic chat logging. It interacts heavily with `ChatHandler.TicketCommands` for GM console commands and `WorldSession.GMTicketHandler` for player-facing network opcodes.

## Member-by-Member Behavior

### Ticket Lifecycle and State Management (`GmTicket`)

*   **Constructors/Destructors**:
    *   `GmTicket()` initializes a blank ticket with default values (unassigned, not completed, moderator security level).
    *   `GmTicket(Player* player)` creates a new ticket for a specific player. It generates a unique ID via `TicketMgr::GenerateTicketId`, captures the player's name and GUID, and sets creation/modification timestamps to the current time.
    *   `~GmTicket()` is a trivial destructor.

*   **State Queries**:
    *   `IsClosed()`: Returns true if `m_closedBy` is not empty. A ticket is considered closed if it has been explicitly closed by a GM, deleted, or abandoned.
    *   `IsCompleted()`: Returns true if `m_completed` is set. This indicates a GM has marked the ticket as resolved, though it may still be open for viewing.
    *   `IsFromPlayer(ObjectGuid guid)`: Checks if the ticket belongs to the specified player GUID.
    *   `IsAssigned()`: Returns true if `m_assignedTo` is not empty.
    *   `IsAssignedTo(ObjectGuid guid)`: Checks if the ticket is assigned to a specific GM.
    *   `IsAssignedNotTo(ObjectGuid guid)`: Returns true if the ticket is assigned to someone *other* than the specified GUID. Used to prevent GMs from modifying tickets assigned to colleagues.

*   **State Mutators**:
    *   `SetAssignedTo(ObjectGuid guid, bool isAdmin)`: Assigns the ticket to a GM. If the GM is an admin and the ticket was in the escalation queue, it updates the status to `TICKET_ESCALATED_ASSIGNED`. Otherwise, it moves to `TICKET_ASSIGNED`.
    *   `SetUnassigned()`: Clears the assigned GM. It also adjusts the escalation status: if it was assigned, it becomes unassigned; if it was escalated-assigned, it returns to the escalation queue.
    *   `SetClosedBy(ObjectGuid value)`: Marks the ticket as closed by the specified entity (GM GUID, or special values for console/abandonment).
    *   `SetCompleted()`: Marks the ticket as resolved.
    *   `SetEscalatedStatus(GMTicketEscalationStatus)`: Manually sets the escalation level.
    *   `SetNeededSecurityLevel(uint8 sec)`: Sets the required security level for the ticket, used for escalation prioritization.

*   **Content Management**:
    *   `SetMessage(std::string const& message)`: Updates the ticket's main message and refreshes `m_lastModifiedTime`.
    *   `SetComment(std::string const& comment)`: Adds a GM-only comment to the ticket.
    *   `AppendResponse(std::string const& response)`: Appends text to the GM's response field.
    *   `ResetResponse()`: Clears the GM's response.
    *   `SetChatLog(...)`: Formats a raw chat log string with timestamps derived from a list of time deltas. Note: This data is transient and not persisted to the DB.
    *   `SetPosition(...)`: Updates the ticket's associated map and coordinates.
    *   `SetGmAction(...)`: Interprets client flags to set internal boolean states `m_needResponse` and `m_needMoreHelp`. The magic number `17` is used to indicate a response is needed.

### Persistence (`GmTicket`)

*   `LoadFromDB(Field* fields)`: Populates the `GmTicket` object from a database row. It maps fields sequentially, handling GUID reconstruction and enum casting. It reads 20 fields corresponding to the `gm_tickets` table schema.
*   `SaveToDB()`: Persists the ticket using a `REPLACE INTO` statement. This ensures that if a ticket with the same ID exists, it is updated; otherwise, it is inserted. It uses prepared statements for safety.
*   `DeleteFromDB()`: Removes the ticket from the `gm_tickets` table by ID.

### Network Serialization (`GmTicket`)

*   `WritePacket(WorldPacket& data)`: Serializes ticket data for the client.
    *   It constructs a display message string. If completed, it appends a separator and the GM response.
    *   It sends the ticket type, age of the last modification, age of the oldest open ticket (via `TicketMgr::GetOldestOpenTicket`), and an estimated wait time (via `TicketMgr::GetLastChange`).
    *   It caps the escalation status sent to the client at `TICKET_IN_ESCALATION_QUEUE` (value 2), hiding the internal `TICKET_ESCALATED_ASSIGNED` (value 3) state from the player client.
    *   It indicates whether the ticket has been viewed by a GM.
*   `SendResponse(WorldSession* session)`: Wraps `WritePacket` in an `SMSG_GMTICKET_GETTICKET` packet and sends it to the player, followed by a system message indicating the ticket has been responded to.

### Formatting and Utilities (`GmTicket`)

*   `FormatMessageString(ChatHandler&, bool detailed)`: Generates a human-readable summary for GMs. It includes ID, player name, age since creation, age since last modification, assigned GM name, and optionally the message, comments, and responses.
*   `FormatMessageString(ChatHandler&, char const*...)`: An overloaded version for generating status change notifications (e.g., "Ticket #123 closed by GM_Name"). It accepts optional strings for closed-by, assigned-to, etc.
*   `GetTicketCategoryName(TicketType)`: Maps internal ticket type enums to user-friendly strings (e.g., "Stuck", "Behavior", "Billing").
*   `GetPlayer()` / `GetAssignedPlayer()`: Retrieves the `Player` object from the world state using `ObjectAccessor::FindPlayer`. Returns null if offline.
*   `GetAssignedToName()`: Retrieves the name of the assigned GM from `ObjectMgr` if the GUID is valid.
*   `TeleportTo(Player* player)`: Teleports a GM to the ticket creator's saved location.
*   `GetAge(uint64 t)`: A free function (defined inline in the cpp) that calculates the age of a timestamp in days.

### Ticket Manager Operations (`TicketMgr`)

*   **Initialization**:
    *   `Initialize()`: Sets the global ticket system status based on the `CONFIG_BOOL_GMTICKETS_ENABLE` world configuration.
    *   `LoadTickets()`: Clears the existing cache and reloads all tickets from `gm_tickets`. It counts open tickets and tracks the maximum ID seen to initialize `_lastTicketId`.
    *   `LoadSurveys()`: Queries the maximum `survey_id` from `gm_surveys` to initialize `_lastSurveyId`. It does not load survey data into memory.

*   **Ticket Manipulation**:
    *   `AddTicket(GmTicket* ticket)`: Inserts a new ticket into the cache, increments the open ticket count if applicable, and saves it to the DB.
    *   `CloseTicket(uint32 ticketId, ObjectGuid source)`: Finds the ticket, sets the closer, decrements the open count if the source is valid (not console/abandonment), and saves.
    *   `RemoveTicket(uint32 ticketId)`: Deletes the ticket from the DB, removes it from the cache, and frees memory.
    *   `ResetTickets()`: Iterates through the cache, removing closed tickets from memory and truncating the `gm_tickets` table. It resets the ID counter.

*   **Listing and Reporting**:
    *   `ShowList(...)`: Sends a list of open, non-completed tickets to a GM. Supports filtering by online status and ticket category.
    *   `ShowClosedList(...)`: Sends a list of closed tickets.
    *   `ShowEscalatedList(...)`: Sends a list of tickets in the escalation queue, including their required security level.

*   **Network and Reloading**:
    *   `SendTicket(...)`: Sends a ticket packet to a session. If the ticket pointer is null, it sends a default status packet.
    *   `ReloadTicket(uint32 ticketId)`: Initiates an asynchronous database query to refresh a specific ticket's data. It uses `_reloadTicketsSet` to prevent duplicate reload requests for the same ticket.
    *   `ReloadTicketCallback(...)`: Processes the result of the async reload. If the ticket is new, it adds it to the cache and notifies GMs. If it exists, it compares states (closed, completed, assigned) and sends specific notification messages to GMs if changes occurred.

## Cross-Unit Boundaries

*   **`ChatHandler.TicketCommands`**: The primary consumer of `TicketMgr` and `GmTicket` methods.
    *   Calls `SaveToDB`, `IsClosed`, `IsCompleted`, `IsAssigned`, `GetId`, `SetAssignedTo`, `SetEscalatedStatus`, `SetNeededSecurityLevel`, `SetComment`, `SetViewed`, `AppendResponse`, `ResetResponse`, `FormatMessageString`, `GetPlayer`, `GetAssignedPlayer`, `GetAssignedToName`, `SetUnassigned`, `SetPosition`, `SetCompleted`, `SetClosedBy`, `DeleteFromDB` (indirectly via `RemoveTicket`), and `TeleportTo`.
    *   Uses `TicketMgr::ShowList`, `ShowClosedList`, `ShowEscalatedList`, `ResetTickets`, `ReloadTicket`, `CloseTicket`, `RemoveTicket`, and `SendTicket`.
*   **`WorldSession.GMTicketHandler`**: Handles player-facing network opcodes.
    *   Calls `GmTicket` constructor to create tickets.
    *   Calls `IsClosed`, `IsCompleted`, `GetId`, `SetMessage`, `SetTicketType`, `SetPosition`, `SendResponse`, `WritePacket` (via `SendTicket`), and `AddTicket` (via `TicketMgr`).
    *   Uses `TicketMgr::SendTicket` to transmit data back to the client.
*   **`ObjectAccessor`**: Used by `GmTicket::GetPlayer` and `GetAssignedPlayer` to resolve GUIDs to live `Player` objects.
*   **`ObjectMgr`**: Used by `GmTicket::GetAssignedToName` and `FormatMessageString` to resolve GM GUIDs to names.
*   **`World`**: `TicketMgr::Initialize` reads config from `World`. `TicketMgr::ReloadTicketCallback` sends global notifications via `World::SendGMTicketText`.
*   **`Database`**: `GmTicket` and `TicketMgr` interact directly with `CharacterDatabase` for persistence. `TicketMgr::LoadTickets` and `LoadSurveys` run synchronous queries during startup. `GmTicket::SaveToDB` and `DeleteFromDB` use prepared statements. `TicketMgr::ReloadTicket` uses an asynchronous query.

## Data Model

The unit primarily interacts with two tables in the `character` database:

1.  **`gm_tickets`**: Stores all ticket data.
    *   **Key Columns**: `ticket_id` (PK), `guid` (player GUID), `name` (player name), `message` (initial report), `create_time`, `last_modified_time`.
    *   **Location**: `map`, `position_x`, `position_y`, `position_z`.
    *   **State**: `closed_by` (GUID of closer), `assigned_to` (GUID of assigned GM), `completed` (boolean), `escalated` (enum status), `viewed` (boolean), `have_ticket` (boolean, likely legacy/client sync), `ticket_type` (enum), `security_needed` (byte).
    *   **Comments/Responses**: `comment` (GM notes), `response` (GM reply).
    *   **Usage**: `GmTicket::LoadFromDB` reads all columns. `GmTicket::SaveToDB` writes all columns using `REPLACE INTO`. `GmTicket::DeleteFromDB` deletes by `ticket_id`. `TicketMgr::LoadTickets` selects all rows. `TicketMgr::ResetTickets` truncates the table.

2.  **`gm_surveys`**: Stores post-ticket satisfaction surveys.
    *   **Key Columns**: `survey_id` (PK), `guid` (player GUID), `main_survey`, `overall_comment`, `create_time`.
    *   **Usage**: `TicketMgr::LoadSurveys` only queries `MAX(survey_id)` to determine the next available ID. It does not load survey content into memory.

## Notable Implementation Details

*   **Magic Number in `SetGmAction`**: The method `GmTicket::SetGmAction` checks if `needResponse == 17`. The comment notes "17 = true, 1 = false". This is a hardcoded protocol detail from the client that should ideally be abstracted into an enum or constant.
*   **Escalation Status Hiding**: In `WritePacket`, the escalation status sent to the client is capped at `TICKET_IN_ESCALATION_QUEUE` (2). The internal state `TICKET_ESCALATED_ASSIGNED` (3) is never sent to the player. This suggests the client does not distinguish between "in queue" and "assigned from queue," or that the distinction is purely server-side for GM workflow.
*   **Transient Chat Log**: `m_chatLog` is populated by `SetChatLog` but is never saved to the database. The comment states "No need to store in db, will be refreshed every session client side." This implies the chat history is reconstructed or fetched separately, or simply lost on server restart.
*   **Async Reload Safety**: `TicketMgr::ReloadTicket` uses `_reloadTicketsSet` to prevent concurrent reloads of the same ticket. However, `ReloadTicketCallback` creates a *new* `GmTicket` object and compares it to the cached one. If the ticket was modified in memory *after* the async query started but *before* the callback runs, the callback might overwrite recent in-memory changes with stale DB data, or vice-versa, depending on the exact comparison logic. The current logic only sends notifications if specific fields differ; it does *not* replace the cached ticket object with the new one unless it's a new ticket. This means `ReloadTicket` is primarily for notification purposes, not for syncing state.
*   **ID Generation**: `TicketMgr` generates IDs by incrementing `_lastTicketId`. This relies on the DB not having gaps larger than the in-memory counter or the server restarting without persisting the max ID. `LoadTickets` corrects `_lastTicketId` by scanning all loaded tickets, mitigating restart issues, but concurrent ticket creation across multiple shards (if applicable) or rapid creation could theoretically cause collisions if not handled carefully by the DB constraint (which `REPLACE INTO` handles by overwriting, potentially losing data if IDs collide unexpectedly).
*   **`GetOldestOpenTicket`**: `TicketMgr::GetOldestOpenTicket` iterates the `_ticketList` map. Since `std::map` is ordered by key (ticket ID), and IDs are monotonically increasing, this effectively returns the ticket with the lowest ID that is open and not completed. This assumes lower ID equals older, which is generally true but not strictly guaranteed if IDs are recycled or manipulated.

## Member Reference

**GetAge**
Free function calculating the age of a timestamp in days.

**GmTicket**
Default constructor initializing a blank ticket.

**GmTicket#2**
Constructor creating a ticket for a specific player, generating an ID and capturing player info.

**~GmTicket**
Trivial destructor.

**LoadFromDB**
Populates the ticket object from a database row, mapping 20 fields.

**SaveToDB**
Persists the ticket to `gm_tickets` using `REPLACE INTO`.

**IsClosed**
Returns true if the ticket has a closer GUID.

**IsCompleted**
Returns true if the ticket is marked as completed.

**IsFromPlayer**
Checks if the ticket belongs to a specific player GUID.

**IsAssigned**
Returns true if the ticket is assigned to a GM.

**IsAssignedTo**
Checks if the ticket is assigned to a specific GM GUID.

**IsAssignedNotTo**
Returns true if the ticket is assigned to a GM other than the specified GUID.

**GetId**
Returns the ticket ID.

**GetPlayerName**
Returns the name of the ticket creator.

**GetMessage**
Returns the initial ticket message.

**GetAssignedToGUID**
Returns the GUID of the assigned GM.

**GetLastModifiedTime**
Returns the timestamp of the last modification.

**GetEscalatedStatus**
Returns the current escalation status enum.

**SetEscalatedStatus**
Sets the escalation status enum.

**SetAssignedTo**
Assigns the ticket to a GM, updating escalation status if applicable.

**DeleteFromDB**
Removes the ticket from the `gm_tickets` table.

**SetClosedBy**
Sets the GUID of the entity that closed the ticket.

**SetCompleted**
Marks the ticket as completed.

**WritePacket**
Serializes ticket data into a `WorldPacket` for client transmission.

**SetMessage**
Updates the ticket message and refreshes the modification timestamp.

**SetComment**
Adds a GM comment to the ticket.

**SetViewed**
Marks the ticket as viewed by a GM.

**AppendResponse**
Appends text to the GM response.

**ResetResponse**
Clears the GM response.

**GetChatLog**
Returns the transient chat log string.

**GetTicketType**
Returns the ticket type enum.

**SetTicketType**
Sets the ticket type enum.

**SendResponse**
Sends the ticket packet and a system message to the player session.

**SetNeededSecurityLevel**
Sets the required security level for the ticket.

**GetNeededSecurityLevel**
Returns the required security level.

**FormatMessageString#2**
Generates a status notification string for GMs, accepting optional names for closed/assigned/completed actions.

**FormatMessageString**
Generates a detailed or brief summary string for GMs.

**GetTicketCategoryName**
Maps ticket type enums to human-readable strings.

**GetPlayer**
Retrieves the live `Player` object for the ticket creator.

**GetAssignedPlayer**
Retrieves the live `Player` object for the assigned GM.

**GetAssignedToName**
Retrieves the name of the assigned GM from the object manager.

**SetUnassigned**
Clears the assigned GM and adjusts escalation status.

**SetPosition**
Updates the ticket's map and coordinates.

**SetGmAction**
Interprets client flags to set internal response/help needs.

**TeleportTo**
Teleports a GM to the ticket creator's location.

**SetChatLog**
Formats and stores a transient chat log with timestamps.

**TicketMgr**
Singleton constructor.

**~TicketMgr**
Destructor freeing all cached tickets.

**Initialize**
Sets the global ticket system status from world config.

**ResetTickets**
Clears closed tickets from memory and truncates the DB table.

**LoadTickets**
Loads all tickets from the DB into the cache.

**LoadSurveys**
Queries the max survey ID from the DB.

**AddTicket**
Adds a new ticket to the cache and saves it.

**CloseTicket**
Marks a ticket as closed and saves it.

**RemoveTicket**
Deletes a ticket from the cache and DB.

**ShowList**
Sends a list of open tickets to a GM, with optional filters.

**ShowClosedList**
Sends a list of closed tickets to a GM.

**ShowEscalatedList**
Sends a list of escalated tickets to a GM.

**SendTicket**
Sends a ticket packet to a session.

**ReloadTicketCallback**
Processes async reload results, notifying GMs of changes.

**ReloadTicket**
Initiates an async reload of a specific ticket.

---

<!-- machine-true, projected from graph.json -->

## Map — GMTicketMgr

*Source:* GMTicketMgr.cpp, GMTicketMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GetAge | function | — | — | — |
| GmTicket | ctor | — | — | — |
| GmTicket#2 | ctor | Object/GetGUID, ObjectGuid/ObjectGuid#5, Player.Main/GetName, TicketMgr/GenerateTicketId, TicketMgr/instance | WorldSession.GMTicketHandler/HandleGMTicketCreateOpcode | — |
| ~GmTicket | dtor | — | — | — |
| LoadFromDB | method | Field/GetBool, Field/GetFloat, Field/GetInt32, Field/GetString, Field/GetUInt16, Field/GetUInt32, Field/GetUInt8, ObjectGuid/ObjectGuid#2, ObjectGuid/ObjectGuid#5 | — | — |
| SaveToDB | method | Database/CreateStatement, ObjectGuid/GetCounter, SqlPreparedStatement/Execute#2, SqlStatement/addFloat, SqlStatement/addInt32, SqlStatement/addString#2, SqlStatement/addUInt16, SqlStatement/addUInt32, SqlStatement/addUInt64, SqlStatement/addUInt8, SqlStatementID/SqlStatementID | ChatHandler.TicketCommands/HandleGMTicketAssignToCommand, ChatHandler.TicketCommands/HandleGMTicketCommentCommand, ChatHandler.TicketCommands/HandleGMTicketCompleteCommand, ChatHandler.TicketCommands/HandleGMTicketResponseResetCommand, ChatHandler.TicketCommands/HandleGMTicketUnAssignCommand, ChatHandler.TicketCommands/ViewTicket, ChatHandler.TicketCommands/_HandleGMTicketResponseAppendCommand, WorldSession.GMTicketHandler/HandleGMTicketUpdateTextOpcode | — |
| IsClosed | method | — | ChatHandler.TicketCommands/HandleGMTicketAssignToCommand, ChatHandler.TicketCommands/HandleGMTicketCloseByIdCommand, ChatHandler.TicketCommands/HandleGMTicketCommentCommand, ChatHandler.TicketCommands/HandleGMTicketCompleteCommand, ChatHandler.TicketCommands/HandleGMTicketDeleteByIdCommand, ChatHandler.TicketCommands/HandleGMTicketEscalateCommand, ChatHandler.TicketCommands/HandleGMTicketResponseResetCommand, ChatHandler.TicketCommands/HandleGMTicketUnAssignCommand, ChatHandler.TicketCommands/ViewTicketByIdOrName, ChatHandler.TicketCommands/_HandleGMTicketResponseAppendCommand, WorldSession.GMTicketHandler/HandleGMTicketCreateOpcode | — |
| IsCompleted | method | — | ChatHandler.TicketCommands/HandleGMTicketCloseByIdCommand, ChatHandler.TicketCommands/HandleGMTicketCompleteCommand, ChatHandler.TicketCommands/HandleGMTicketEscalateCommand, ChatHandler.TicketCommands/ViewTicketByIdOrName, WorldSession.GMTicketHandler/HandleGMTicketCreateOpcode, WorldSession.GMTicketHandler/HandleGMTicketGetTicketOpcode, WorldSession.GMTicketHandler/HandleGMTicketUpdateTextOpcode | — |
| IsFromPlayer | method | — | — | — |
| IsAssigned | method | — | ChatHandler.TicketCommands/HandleGMTicketUnAssignCommand | — |
| IsAssignedTo | method | — | ChatHandler.TicketCommands/HandleGMTicketAssignToCommand | — |
| IsAssignedNotTo | method | — | ChatHandler.TicketCommands/HandleGMTicketAssignToCommand, ChatHandler.TicketCommands/HandleGMTicketCloseByIdCommand, ChatHandler.TicketCommands/HandleGMTicketCommentCommand, ChatHandler.TicketCommands/HandleGMTicketCompleteCommand, ChatHandler.TicketCommands/HandleGMTicketResponseResetCommand, ChatHandler.TicketCommands/_HandleGMTicketResponseAppendCommand | — |
| GetId | method | — | ChatHandler.TicketCommands/HandleGMTicketAssignToCommand, ChatHandler.TicketCommands/HandleGMTicketCloseByIdCommand, ChatHandler.TicketCommands/HandleGMTicketCommentCommand, ChatHandler.TicketCommands/HandleGMTicketCompleteCommand, ChatHandler.TicketCommands/HandleGMTicketDeleteByIdCommand, ChatHandler.TicketCommands/HandleGMTicketNextCommand, ChatHandler.TicketCommands/HandleGMTicketPreviousCommand, ChatHandler.TicketCommands/HandleGMTicketResponseResetCommand, ChatHandler.TicketCommands/HandleGMTicketUnAssignCommand, ChatHandler.TicketCommands/_HandleGMTicketResponseAppendCommand, WorldSession.GMTicketHandler/HandleGMTicketCreateOpcode, WorldSession.GMTicketHandler/HandleGMTicketDeleteTicketOpcode, WorldSession.GMTicketHandler/HandleGMTicketUpdateTextOpcode | — |
| GetPlayerName | method | — | — | — |
| GetMessage | method | — | — | — |
| GetAssignedToGUID | method | — | ChatHandler.TicketCommands/HandleGMTicketCompleteCommand, ChatHandler.TicketCommands/HandleGMTicketUnAssignCommand | — |
| GetLastModifiedTime | method | — | — | — |
| GetEscalatedStatus | method | — | ChatHandler.TicketCommands/HandleGMTicketEscalateCommand | — |
| SetEscalatedStatus | method | — | ChatHandler.TicketCommands/HandleGMTicketEscalateCommand | — |
| SetAssignedTo | method | — | ChatHandler.TicketCommands/HandleGMTicketAssignToCommand, ChatHandler.TicketCommands/HandleGMTicketCompleteCommand | — |
| DeleteFromDB | method | Database/CreateStatement, SqlPreparedStatement/Execute#2, SqlStatement/addUInt32, SqlStatementID/SqlStatementID | — | gm_tickets |
| SetClosedBy | method | — | — | — |
| SetCompleted | method | — | ChatHandler.TicketCommands/HandleGMTicketCompleteCommand | — |
| WritePacket | method | ByteBuffer/operator<<, ByteBuffer/operator<<#10, ByteBuffer/operator<<#7, ByteBuffer/operator<<#9, TicketMgr/GetLastChange, TicketMgr/GetOldestOpenTicket, TicketMgr/instance | — | — |
| SetMessage | method | — | WorldSession.GMTicketHandler/HandleGMTicketCreateOpcode, WorldSession.GMTicketHandler/HandleGMTicketUpdateTextOpcode | — |
| SetComment | method | — | ChatHandler.TicketCommands/HandleGMTicketCommentCommand | — |
| SetViewed | method | — | ChatHandler.TicketCommands/ViewTicket | — |
| AppendResponse | method | — | ChatHandler.TicketCommands/HandleGMTicketCompleteCommand, ChatHandler.TicketCommands/_HandleGMTicketResponseAppendCommand | — |
| ResetResponse | method | — | ChatHandler.TicketCommands/HandleGMTicketResponseResetCommand | — |
| GetChatLog | method | — | — | — |
| GetTicketType | method | — | — | — |
| SetTicketType | method | — | WorldSession.GMTicketHandler/HandleGMTicketCreateOpcode, WorldSession.GMTicketHandler/HandleGMTicketUpdateTextOpcode | — |
| SendResponse | method | ChatHandler.Chat/ChatHandler#3, ChatHandler.Chat/SendSysMessage#2, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | ChatHandler.TicketCommands/HandleGMTicketCompleteCommand, WorldSession.GMTicketHandler/HandleGMTicketGetTicketOpcode | — |
| SetNeededSecurityLevel | method | — | ChatHandler.TicketCommands/HandleGMTicketEscalateCommand | — |
| GetNeededSecurityLevel | method | — | — | — |
| FormatMessageString#2 | method | ChatHandler.Chat/PGetParseString, ChatHandler.Chat/playerLink, ObjectMgr/GetPlayerNameByGUID, shared_Util/secsToTimeString | ChatHandler.TicketCommands/HandleGMTicketResponseResetCommand, ChatHandler.TicketCommands/ViewTicket | — |
| FormatMessageString | method | ChatHandler.Chat/PGetParseString | ChatHandler.TicketCommands/HandleGMTicketAssignToCommand, ChatHandler.TicketCommands/HandleGMTicketCloseByIdCommand, ChatHandler.TicketCommands/HandleGMTicketCommentCommand, ChatHandler.TicketCommands/HandleGMTicketCompleteCommand, ChatHandler.TicketCommands/HandleGMTicketDeleteByIdCommand, ChatHandler.TicketCommands/HandleGMTicketUnAssignCommand | — |
| GetTicketCategoryName | method | — | WorldSession.GMTicketHandler/HandleGMTicketCreateOpcode | — |
| GetPlayer | method | ObjectAccessor/FindPlayer | ChatHandler.TicketCommands/HandleGMTicketCloseByIdCommand, ChatHandler.TicketCommands/HandleGMTicketCompleteCommand, ChatHandler.TicketCommands/HandleGMTicketDeleteByIdCommand, ChatHandler.TicketCommands/HandleGMTicketEscalateCommand, ChatHandler.TicketCommands/ViewTicket | — |
| GetAssignedPlayer | method | ObjectAccessor/FindPlayer | ChatHandler.TicketCommands/HandleGMTicketUnAssignCommand | — |
| GetAssignedToName | method | ObjectMgr/GetPlayerNameByGUID | ChatHandler.TicketCommands/HandleGMTicketCommentCommand, ChatHandler.TicketCommands/HandleGMTicketUnAssignCommand | — |
| SetUnassigned | method | ObjectGuid/Clear | ChatHandler.TicketCommands/HandleGMTicketUnAssignCommand | — |
| SetPosition | method | — | WorldSession.GMTicketHandler/HandleGMTicketCreateOpcode | — |
| SetGmAction | method | — | — | — |
| TeleportTo | method | Player.Main/TeleportTo | — | — |
| SetChatLog | method | shared_Util/secsToTimeString | — | — |
| TicketMgr | ctor | — | — | — |
| ~TicketMgr | dtor | — | — | — |
| Initialize | method | TicketMgr/SetStatus, World/getConfig | World/SetInitialWorldSettings | — |
| ResetTickets | method | Database/Execute#2, TicketMgr/instance | ChatHandler.TicketCommands/HandleGMTicketResetCommand | — |
| LoadTickets | method | Database/Query, Log.Main/Out, QueryResult/Fetch, QueryResult/NextRow, shared_Util/getMSTime, WorldTimer/getMSTimeDiffToNow | World/SetInitialWorldSettings | gm_tickets |
| LoadSurveys | method | Database/Query, Field/GetUInt32, Log.Main/Out, QueryResult/operator[], shared_Util/getMSTime, WorldTimer/getMSTimeDiffToNow | World/SetInitialWorldSettings | gm_surveys |
| AddTicket | method | — | WorldSession.GMTicketHandler/HandleGMTicketCreateOpcode | — |
| CloseTicket | method | TicketMgr/GetTicket | ChatHandler.TicketCommands/HandleGMTicketCloseByIdCommand, WorldSession.GMTicketHandler/HandleGMTicketCreateOpcode, WorldSession.GMTicketHandler/HandleGMTicketDeleteTicketOpcode | — |
| RemoveTicket | method | TicketMgr/GetTicket | ChatHandler.TicketCommands/HandleGMTicketDeleteByIdCommand | — |
| ShowList | method | ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SendSysMessage#2 | ChatHandler.TicketCommands/HandleGMTicketListCommand, ChatHandler.TicketCommands/HandleGMTicketListOnlineCommand | — |
| ShowClosedList | method | ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SendSysMessage#2 | ChatHandler.TicketCommands/HandleGMTicketListClosedCommand | — |
| ShowEscalatedList | method | ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2 | ChatHandler.TicketCommands/HandleGMTicketListEscalatedCommand | — |
| SendTicket | method | ByteBuffer/operator<<#10, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | ChatHandler.TicketCommands/HandleGMTicketEscalateCommand, ChatHandler.TicketCommands/ViewTicket, WorldSession.GMTicketHandler/HandleGMTicketDeleteTicketOpcode, WorldSession.GMTicketHandler/HandleGMTicketGetTicketOpcode, WorldSession.GMTicketHandler/HandleGMTicketUpdateTextOpcode | — |
| ReloadTicketCallback | method | NullChatHandler/NullChatHandler, ObjectGuid/operator!=, QueryResult/Fetch, TicketMgr/GetTicket, World/SendGMTicketText, World/SendGMTicketText#2 | — | — |
| ReloadTicket | method | — | ChatHandler.TicketCommands/HandleGMTicketReloadCommand | gm_tickets |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `gm_surveys`: survey_id int(10) unsigned PK, guid int(10) unsigned, main_survey int(10) unsigned, overall_comment longtext, create_time int(10) unsigned
- `gm_tickets`: ticket_id int(10) unsigned PK, guid int(10) unsigned, name varchar(12), message text, create_time bigint(20) unsigned, map smallint(5) unsigned, position_x float, position_y float, position_z float, last_modified_time bigint(20) unsigned, closed_by int(10), assigned_to int(10) unsigned, comment text, response text, completed tinyint(3) unsigned, escalated tinyint(3) unsigned, viewed tinyint(3) unsigned, have_ticket tinyint(3) unsigned, ticket_type tinyint(3) unsigned, security_needed tinyint(3) unsigned

*`?` = nullable, `PK` = primary key column.*

