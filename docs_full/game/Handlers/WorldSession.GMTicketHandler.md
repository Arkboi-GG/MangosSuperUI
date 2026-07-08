<!-- provenance: boundary-bleed -->
# WorldSession.GMTicketHandler

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# WorldSession.GMTicketHandler

## Purpose & Responsibilities

The `WorldSession.GMTicketHandler` partial implements the server-side logic for handling Game Master (GM) Ticket system network packets within the `WorldSession` class. It acts as the entry point for client requests related to creating, viewing, updating, deleting, and querying the status of GM tickets, as well as submitting player surveys.

This unit does not manage the ticket lifecycle itself (storage, assignment, or completion logic); instead, it delegates these operations to the singleton `TicketMgr` (accessed via `sTicketMgr`). Its primary responsibilities are:
1.  **Packet Parsing:** Extracting data from incoming client packets (`GmTicketCreate`, `GmTicketUpdateText`, `GMSurveySubmit`).
2.  **Validation:** Checking preconditions such as player level, existing ticket status, and global ticket system availability.
3.  **Delegation:** Calling appropriate methods on `TicketMgr` to perform actions like creating, closing, or updating tickets.
4.  **Response Generation:** Sending confirmation packets back to the client (`SMSG_GMTICKET_CREATE`, `SMSG_GMTICKET_UPDATETEXT`, etc.) and broadcasting relevant events to the world via `World`.
5.  **Persistence:** Directly inserting survey data into the `CharacterDatabase` for the survey submission opcode.

## Member-by-Member Behavior

### Ticket Retrieval and Status
**`HandleGMTicketGetTicketOpcode`**
Retrieves the current ticket associated with the player. It first sends a query time response to synchronize client/server time via `WorldSession.QueryHandler/SendQueryTimeResponse`. It then queries `TicketMgr::GetTicketByPlayer` using the player's GUID obtained from `WorldSession.Main/GetPlayer`.
- If a ticket exists and is marked as completed (`GMTicketMgr/IsCompleted`), it sends the response details via `GMTicketMgr/SendResponse`.
- If a ticket exists but is not completed, it sends the full ticket details via `GMTicketMgr/SendTicket`.
- If no ticket exists, it sends a null ticket response via `GMTicketMgr/SendTicket`.

**`HandleGMTicketSystemStatusOpcode`**
Responds to a client request for the current operational status of the GM ticket system. It queries `TicketMgr/GetStatus`. If the status indicates enabled, it sends `GMTICKET_QUEUE_STATUS_ENABLED`; otherwise, it sends `GMTICKET_QUEUE_STATUS_DISABLED`. The resulting packet (`SMSG_GMTICKET_SYSTEMSTATUS`) informs the client whether to enable or grey out the ticket UI.

### Ticket Creation
**`HandleGMTicketCreateOpcode`**
Handles the creation of a new GM ticket.
1.  **Global Check:** Immediately returns if `TicketMgr/GetStatus` indicates the queue is disabled.
2.  **Existing Ticket Handling:** Retrieves any existing ticket for the player. If one exists and is completed (`GMTicketMgr/IsCompleted`), it closes it via `GMTicketMgr/CloseTicket` to allow the player to submit a new one.
3.  **Eligibility Checks:**
    - Ensures the player does not have an active, unclosed ticket (`GMTicketMgr/IsClosed`).
    - Verifies the player's level meets the minimum requirement configured in `World/getConfig#4` (`CONFIG_UINT32_GMTICKETS_MINLEVEL`). If not, it sends a system message via `ChatHandler.Chat/PSendSysMessage` and aborts.
    - Validates the ticket type index is within bounds (`GMTICKET_MAX`).
4.  **Creation:**
    - Instantiates a new `GmTicket` object.
    - Sets position, message, and type from the packet via `GMTicketMgr/SetPosition`, `GMTicketMgr/SetMessage`, and `GMTicketMgr/SetTicketType`.
    - Registers the ticket with `TicketMgr/AddTicket`.
    - Updates the last change timestamp via `TicketMgr/UpdateLastChange`.
    - Broadcasts the new ticket event to the world via `World/SendGMTicketText#2`.
5.  **Response:** Sends `SMSG_GMTICKET_CREATE` with a success or error code via `WorldSession.Main/SendPacket`.

### Ticket Modification
**`HandleGMTicketUpdateTextOpcode`**
Allows a player to update the text or type of their open ticket.
1.  **Existence Check:** Retrieves the player's ticket. If none exists, it defaults to an error response.
2.  **Completion Check:** If the ticket is completed (`GMTicketMgr/IsCompleted`), it sends a read-only error message via `ChatHandler.Chat/SendSysMessage#2` and refreshes the ticket view via `GMTicketMgr/SendTicket`. No changes are made.
3.  **Update:** If the ticket is open, it updates the message and type via `GMTicketMgr/SetMessage` and `GMTicketMgr/SetTicketType`, then persists the change via `GMTicketMgr/SaveToDB`.
4.  **Broadcast:** Announces the update to the world via `World/SendGMTicketText#2`.
5.  **Response:** Sends `SMSG_GMTICKET_UPDATETEXT` with a success code via `WorldSession.Main/SendPacket`.

### Ticket Deletion
**`HandleGMTicketDeleteTicketOpcode`**
Allows a player to abandon/delete their open ticket.
1.  **Existence Check:** Retrieves the player's ticket. If none exists, the function returns silently.
2.  **Client Notification:** Sends `SMSG_GMTICKET_DELETETICKET` to the client immediately via `WorldSession.Main/SendPacket`.
3.  **Broadcast:** Announces the abandonment to the world via `World/SendGMTicketText#2`.
4.  **Closure:** Calls `GMTicketMgr/CloseTicket` to mark the ticket as closed in the manager.
5.  **Refresh:** Sends a null ticket response via `GMTicketMgr/SendTicket` to clear the client's ticket view.

### Survey Submission
**`HandleGMSurveySubmitOpcode`**
*(Only compiled for client builds newer than 1.10.2)*
Handles the submission of a player satisfaction survey. This is the only member in this unit that performs direct database writes.
1.  **ID Generation:** Obtains a unique survey ID from `TicketMgr/GetNextSurveyID`.
2.  **Sub-survey Insertion:** Iterates through the submitted sub-surveys. It uses a `std::set` to prevent duplicate sub-survey IDs from being inserted. For each unique sub-survey, it prepares and executes an `INSERT` statement into the `gm_subsurveys` table using `Database/CreateStatement` and `SqlPreparedStatement/Execute#2`.
3.  **Main Survey Insertion:** Prepares and executes an `INSERT` statement into the `gm_surveys` table, recording the player's GUID (from `Object/GetGUIDLow`), the generated survey ID, the main survey category, the overall comment, and the current timestamp.

## Cross-Unit Boundaries

### Collaboration with `TicketMgr`
The `TicketMgr` singleton is the central authority for all ticket-related state.
- **Direction:** `WorldSession.GMTicketHandler` calls into `TicketMgr`.
- **Why:** `TicketMgr` holds the in-memory collection of tickets, manages their lifecycle (creation, closure, completion), and handles persistence for ticket data. `WorldSession` merely triggers these actions based on client input.
- **Key Interactions:**
    - `GetTicketByPlayer`: Used by almost all handlers to locate the relevant ticket.
    - `AddTicket`, `CloseTicket`, `SaveToDB`: Used by Create, Delete, and Update handlers to mutate state.
    - `SendTicket`, `SendResponse`: Used to generate the specific packet payloads for ticket views.
    - `GetStatus`: Used to check if the system is globally enabled.

### Collaboration with `World`
- **Direction:** `WorldSession.GMTicketHandler` calls into `World`.
- **Why:** To broadcast significant ticket events (new, updated, abandoned) to other players or game masters who may be monitoring the ticket queue.
- **Key Interaction:** `World/SendGMTicketText#2` is called after successful creation, update, or deletion to log the event.

### Collaboration with `ChatHandler`
- **Direction:** `WorldSession.GMTicketHandler` calls into `ChatHandler`.
- **Why:** To send localized system messages to the player regarding errors (e.g., level too low, ticket read-only).
- **Key Interactions:** `ChatHandler.Chat/PSendSysMessage` and `ChatHandler.Chat/SendSysMessage#2`.

### Collaboration with `WorldSession` (Other Parts)
- **Direction:** Internal calls within `WorldSession`.
- **Why:** To access the player object, send raw packets, and handle query time synchronization.
- **Key Interactions:**
    - `WorldSession.Main/GetPlayer`: Retrieves the `Player` object associated with the session.
    - `WorldSession.Main/SendPacket`: Transmits the final response packets to the client.
    - `WorldSession.QueryHandler/SendQueryTimeResponse`: Synchronizes time at the start of ticket retrieval.

## Data Model

This unit interacts with two database tables exclusively within the `HandleGMSurveySubmitOpcode` method. All other ticket data is managed indirectly through `TicketMgr`.

### `gm_surveys`
Stores the main survey record.
- **Columns Used:**
    - `guid`: The player's GUID (from `Object/GetGUIDLow`).
    - `survey_id`: The unique ID generated by `TicketMgr/GetNextSurveyID`.
    - `main_survey`: The category/type of the survey (from packet).
    - `overall_comment`: The player's comment (from packet).
    - `create_time`: Set to `UNIX_TIMESTAMP(NOW())` in the SQL statement.

### `gm_subsurveys`
Stores individual sub-survey responses linked to the main survey.
- **Columns Used:**
    - `survey_id`: Links to the parent survey in `gm_surveys`.
    - `subsurvey_id`: The ID of the specific sub-question (from packet).
    - `rank`: The rating given by the player (from packet).
    - `comment`: Optional comment for the sub-survey (from packet).

*Note: The SQL statements use prepared statements (`SqlStatement`) to bind these values securely.*

## Notable Implementation Details

1.  **Duplicate Sub-Survey Prevention:** In `HandleGMSurveySubmitOpcode`, a `std::set<uint32>` named `surveyIds` is used to track inserted sub-survey IDs. If a duplicate ID is encountered in the packet, it is skipped. This prevents redundant database entries if the client sends malformed or repeated data.
2.  **Automatic Closure of Completed Tickets:** In `HandleGMTicketCreateOpcode`, if a player attempts to create a new ticket but already has a completed one, the code automatically closes the old ticket (`sTicketMgr->CloseTicket`) before proceeding. This allows players to reuse the ticket interface without manual intervention from a GM to close the old one.
3.  **Level Restriction Enforcement:** The creation handler enforces a minimum player level (`CONFIG_UINT32_GMTICKETS_MINLEVEL`). This is a server-side safeguard, as the client UI may not reliably block low-level players.
4.  **Conditional Compilation:** The survey submission handler is wrapped in `#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_10_2`. This indicates that the survey feature was introduced in later client versions and is ignored for older clients.
5.  **Immediate Client Feedback:** In `HandleGMTicketDeleteTicketOpcode`, the deletion confirmation packet is sent to the client *before* the ticket is actually closed in the manager. This ensures the client UI updates immediately, though the server state follows shortly after.
6.  **No Database Access for Core Ticket Ops:** Unlike the survey handler, the core ticket operations (Create, Update, Delete) do not execute SQL directly in this unit. They rely entirely on `TicketMgr` to handle persistence, keeping the session handler focused on network I/O and validation.

## Member Reference

**HandleGMTicketGetTicketOpcode**
Retrieves the player's current ticket from `TicketMgr`. If the ticket is completed, it sends the response; otherwise, it sends the ticket details. If no ticket exists, it sends a null ticket response. Always sends a query time response first.

**HandleGMTicketUpdateTextOpcode**
Updates the text and type of an existing open ticket. Validates that the ticket exists and is not completed. Persists changes via `TicketMgr::SaveToDB` and broadcasts the update. Sends a success or error response to the client.

**HandleGMTicketDeleteTicketOpcode**
Deletes the player's open ticket. Sends a deletion confirmation to the client, broadcasts the abandonment event, closes the ticket in `TicketMgr`, and clears the client's ticket view.

**HandleGMTicketCreateOpcode**
Creates a new GM ticket. Checks global system status, player level, and existing ticket state. Automatically closes any completed existing tickets. Validates input, creates the `GmTicket` object, registers it with `TicketMgr`, broadcasts the event, and sends a creation response.

**HandleGMTicketSystemStatusOpcode**
Returns the current operational status of the GM ticket system (enabled/disabled) to the client, allowing the UI to reflect whether tickets can be submitted.

**HandleGMSurveySubmitOpcode**
*(Client build > 1.10.2)* Inserts a new survey record into `gm_surveys` and its associated sub-surveys into `gm_subsurveys`. Prevents duplicate sub-survey insertions using a set. Uses prepared statements for database safety.

---

<!-- machine-true, projected from graph.json -->

## Map — WorldSession.GMTicketHandler

*Source:* GMTicketHandler.cpp, WorldSession.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| HandleGMTicketGetTicketOpcode | method | GMTicketMgr/IsCompleted, GMTicketMgr/SendResponse, GMTicketMgr/SendTicket, Object/GetGUID, ObjectGuid/ObjectGuid#5, TicketMgr/GetTicketByPlayer, TicketMgr/instance, WorldSession.Main/GetPlayer, WorldSession.QueryHandler/SendQueryTimeResponse | — | — |
| HandleGMTicketUpdateTextOpcode | method | ByteBuffer/operator<<#10, ChatHandler.Chat/ChatHandler#3, ChatHandler.Chat/SendSysMessage#2, GMTicketMgr/GetId, GMTicketMgr/IsCompleted, GMTicketMgr/SaveToDB, GMTicketMgr/SendTicket, GMTicketMgr/SetMessage, GMTicketMgr/SetTicketType, Object/GetGUID, ObjectGuid/ObjectGuid#5, Player.Main/GetName, TicketMgr/GetTicketByPlayer, TicketMgr/instance, World/SendGMTicketText#2, WorldPacket/WorldPacket#4, WorldSession.Main/GetPlayer, WorldSession.Main/SendPacket | — | — |
| HandleGMTicketDeleteTicketOpcode | method | ByteBuffer/operator<<#10, GMTicketMgr/CloseTicket, GMTicketMgr/GetId, GMTicketMgr/SendTicket, Object/GetGUID, ObjectGuid/ObjectGuid#5, Player.Main/GetName, TicketMgr/GetTicketByPlayer, TicketMgr/instance, World/SendGMTicketText#2, WorldPacket/WorldPacket#4, WorldSession.Main/GetPlayer, WorldSession.Main/SendPacket | — | — |
| HandleGMTicketCreateOpcode | method | ByteBuffer/operator<<#10, ChatHandler.Chat/ChatHandler#3, ChatHandler.Chat/PSendSysMessage, GMTicketMgr/AddTicket, GMTicketMgr/CloseTicket, GMTicketMgr/GetId, GMTicketMgr/GetTicketCategoryName, GMTicketMgr/GmTicket#2, GMTicketMgr/IsClosed, GMTicketMgr/IsCompleted, GMTicketMgr/SetMessage, GMTicketMgr/SetPosition, GMTicketMgr/SetTicketType, Object/GetGUID, ObjectGuid/ObjectGuid#5, Player.Main/GetName, TicketMgr/GetStatus, TicketMgr/GetTicketByPlayer, TicketMgr/instance, TicketMgr/UpdateLastChange, Unit.Main/GetLevel, World/getConfig#4, World/SendGMTicketText#2, WorldPacket/WorldPacket#4, WorldSession.Main/GetPlayer, WorldSession.Main/SendPacket | — | — |
| HandleGMTicketSystemStatusOpcode | method | ByteBuffer/operator<<#10, TicketMgr/GetStatus, TicketMgr/instance, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| HandleGMSurveySubmitOpcode | method | Database/CreateStatement, Object/GetGUIDLow, SqlPreparedStatement/Execute#2, SqlStatement/addString#2, SqlStatement/addUInt32, SqlStatementID/SqlStatementID, TicketMgr/GetNextSurveyID, TicketMgr/instance, WorldSession.Main/GetPlayer | — | gm_subsurveys, gm_surveys |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `gm_subsurveys`: survey_id int(10) unsigned PK, subsurvey_id int(10) unsigned PK, rank int(10) unsigned, comment text
- `gm_surveys`: survey_id int(10) unsigned PK, guid int(10) unsigned, main_survey int(10) unsigned, overall_comment longtext, create_time int(10) unsigned

*`?` = nullable, `PK` = primary key column.*


---

<!-- verify: boundary-bleed | foreign: Update, WorldSession -->
