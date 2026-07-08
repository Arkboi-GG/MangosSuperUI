<!-- provenance: boundary-bleed -->
# ChatHandler.TicketCommands

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ChatHandler.TicketCommands

## Purpose & Responsibilities

This unit implements the server-side command handlers for the Game Master (GM) Ticket system within the `wowvmangos` codebase. Located in `TicketCommands.cpp`, it serves as the interface between game administrators (via in-game chat or the server console) and the core ticket management logic provided by `TicketMgr` and `GMTicketMgr`.

Its primary responsibilities are:
1.  **Ticket Lifecycle Management:** Processing commands to view, assign, comment on, escalate, complete, close, and permanently delete support tickets.
2.  **Queue Navigation:** Enabling GMs to iterate through open tickets using `next` and `previous` commands, managed by a per-player ticket counter stored in the `Player` object.
3.  **System Administration:** Providing controls to toggle the global ticket system status, reset the ticket queue, reload specific tickets, and configure individual player notification preferences.
4.  **Input Parsing & Validation:** Parsing command-line arguments (ticket IDs, player names, comment text) and enforcing security constraints, such as ensuring a GM cannot modify a ticket assigned to a higher-security-level GM unless operating via the console.

This unit does not store ticket data or execute SQL queries directly. It acts as a controller, delegating state mutations and data retrieval to `TicketMgr` (accessed via the singleton `sTicketMgr`) and `GMTicketMgr` methods. It relies on `ChatHandler` utilities (defined in the shared `Chat.h` header but implemented in other units) for argument extraction and system messaging.

## Member-by-Member Behavior

### Ticket Assignment and Unassignment

**HandleGMTicketAssignToCommand**
Assigns a specific ticket to a target player or GM.
1.  Parses the ticket ID and the target player's name from the arguments.
2.  Normalizes the target name using `ObjectMgr::normalizePlayerName`.
3.  Retrieves the `GmTicket` object via `TicketMgr::GetTicket`. If the ticket does not exist or is already closed, it returns an error.
4.  Resolves the target player's GUID and Account ID using `ObjectMgr`.
5.  **Validation:**
    *   If the ticket is already assigned to the target, it reports an error.
    *   If the ticket is assigned to a *different* player, it checks if the current user is a console admin. If the current user is an in-game player (`GetSession()` is valid) and the ticket is assigned to someone else, the action is blocked. Console users bypass this restriction.
6.  Calls `GMTicketMgr::SetAssignedTo`, passing the target GUID and a boolean indicating if the assignee has admin security (checked against `CONFIG_UINT32_GMTICKETS_ADMIN_SECURITY`).
7.  Persists changes via `GMTicketMgr::SaveToDB` and updates the global last-change timestamp via `TicketMgr::UpdateLastChange`.
8.  Formats a broadcast message and sends it to all relevant clients via `World::SendGMTicketText`.

**HandleGMTicketUnAssignCommand**
Removes the assignment from a ticket.
1.  Parses the ticket ID.
2.  Validates that the ticket exists and is not closed.
3.  Checks if the ticket is currently assigned; if not, it returns an error.
4.  Determines the security level of the *currently assigned* player. If the assigned player is online, it uses their session security; otherwise, it looks up their account security via `AccountMgr::GetSecurity`.
5.  **Security Check:** Compares the assigned player's security level against the current user's security level. If the assigned player has higher security, the unassignment is blocked (unless the current user is console, which implicitly has the highest security).
6.  Calls `GMTicketMgr::SetUnassigned`, saves to DB, and updates the last change timestamp.
7.  Broadcasts the unassignment event.

### Ticket Status Changes (Close, Complete, Delete)

**HandleGMTicketCloseByIdCommand**
Closes a ticket without marking it as completed (often used for invalid or duplicate tickets).
1.  Parses the ticket ID.
2.  Validates that the ticket exists and is not already closed or completed.
3.  **Assignment Check:** If the current user is an in-game player, they must be the assigned GM. Console users bypass this check.
4.  Calls `TicketMgr::CloseTicket`, passing the closer's GUID (or a special "Console" GUID if applicable).
5.  Updates the last change timestamp.
6.  Broadcasts the closure.
7.  Sends a `SMSG_GMTICKET_DELETETICKET` packet to the original ticket submitter to remove it from their client UI.

**HandleGMTicketCompleteCommand**
Marks a ticket as resolved/completed.
1.  Parses the ticket ID and an optional response comment.
2.  Validates that the ticket exists and is not already closed or completed.
3.  If a response comment is provided:
    *   Checks assignment ownership (same rule as Close: in-game users must be assigned; console bypasses).
    *   Appends the response to the ticket via `GMTicketMgr::AppendResponse`.
4.  Sets the ticket status to completed via `GMTicketMgr::SetCompleted`.
5.  If the ticket was previously unassigned and the current user is an in-game player, it automatically assigns the ticket to them.
6.  Saves to DB and sends the response packet to the ticket submitter via `GMTicketMgr::SendResponse`.
7.  Broadcasts the completion event.

**HandleGMTicketDeleteByIdCommand**
Permanently deletes a ticket from the database.
1.  Parses the ticket ID.
2.  Validates that the ticket exists.
3.  **Critical Constraint:** The ticket *must* be closed. If it is open, the command fails with `LANG_COMMAND_TICKETCLOSEFIRST`.
4.  Broadcasts the deletion event.
5.  Calls `TicketMgr::RemoveTicket` to delete the record.
6.  Updates the last change timestamp.
7.  Sends a `SMSG_GMTICKET_DELETETICKET` packet to the original submitter to force-remove it from their UI.

### Comments and Responses

**HandleGMTicketCommentCommand**
Adds an internal comment to a ticket (visible to GMs, not necessarily the player).
1.  Parses the ticket ID and the comment text.
2.  Validates that the ticket exists and is not closed.
3.  **Assignment Check:** In-game users must be the assigned GM; console users bypass this.
4.  Sets the comment via `GMTicketMgr::SetComment`.
5.  Saves to DB and updates the last change timestamp.
6.  Broadcasts the addition of the comment.

**HandleGMTicketResponseResetCommand**
Clears the GM's response text from a ticket.
1.  Parses the ticket ID.
2.  Validates that the ticket exists and is not closed.
3.  **Assignment Check:** In-game users must be the assigned GM; console users bypass this.
4.  Calls `GMTicketMgr::ResetResponse` and saves to DB.
5.  Displays the reset confirmation.

**_HandleGMTicketResponseAppendCommand** (Private Helper)
Appends text to the GM's response field.
1.  Parses the ticket ID and response text.
2.  Validates that the ticket exists and is not closed.
3.  **Assignment Check:** In-game users must be the assigned GM; console users bypass this.
4.  Calls `GMTicketMgr::AppendResponse`. If the `newLine` flag is true, it appends a newline character as well.
5.  Saves to DB.

**HandleGMTicketResponseAppendCommand**
Calls `_HandleGMTicketResponseAppendCommand` with `newLine = false`.

**HandleGMTicketResponseAppendLnCommand**
Calls `_HandleGMTicketResponseAppendCommand` with `newLine = true`.

### Escalation

**HandleGMTicketEscalateCommand**
Moves a ticket to the escalation queue for higher-level review.
1.  Parses the ticket ID.
2.  Validates that the ticket exists and is not closed or completed.
3.  Checks if the ticket is already escalated (`GetEscalatedStatus != TICKET_UNASSIGNED`). If so, it fails.
4.  Sets the escalation status to `TICKET_IN_ESCALATION_QUEUE` via `GMTicketMgr::SetEscalatedStatus`.
5.  Sets the required security level to the current user's security level + 1 via `GMTicketMgr::SetNeededSecurityLevel`.
6.  Sends the updated ticket data to the submitter via `TicketMgr::SendTicket`.
7.  Updates the last change timestamp and confirms the escalation.

### Viewing and Listing Tickets

**ViewTicketByIdOrName**
Internal helper to locate and display a ticket.
1.  Accepts either a ticket ID string or a player name string.
2.  If a ticket ID is provided, it retrieves the ticket via `TicketMgr::GetTicket`.
3.  If a player name is provided:
    *   Normalizes the name.
    *   Attempts to find the player online via `ObjectAccessor::FindPlayerByName`.
    *   If offline, resolves the GUID via `ObjectMgr::GetPlayerGuidByName`.
    *   Retrieves the ticket via `TicketMgr::GetTicketByPlayer`.
4.  If no ticket is found, it returns an error.
5.  If the ticket is closed or completed, it reports it as archived.
6.  Calls `ViewTicket` to display the details.

**ViewTicket**
Displays the details of a `GmTicket` object.
1.  Marks the ticket as viewed via `GMTicketMgr::SetViewed`.
2.  If the ticket submitter is online, sends them a notification that the ticket has been viewed via `TicketMgr::SendTicket`.
3.  Saves the "viewed" state to DB.
4.  Formats and prints the ticket details to the GM using `GMTicketMgr::FormatMessageString`.

**HandleGMTicketGetByIdOrNameCommand**, **HandleGMTicketGetByIdCommand**, **HandleGMTicketGetByNameCommand**
Public wrappers that call `ViewTicketByIdOrName` with the appropriate arguments (ID, Name, or both).

**HandleGMTicketListCommand**
Lists open tickets.
1.  Defines a static map of category names (e.g., "stuck", "behavior") to numeric IDs.
2.  If the argument matches a category, it calls `TicketMgr::ShowList` filtered by that category.
3.  Otherwise, it lists all open tickets.

**HandleGMTicketListOnlineCommand**
Lists open tickets for players who are currently online.
1.  Uses the same category mapping as `HandleGMTicketListCommand`.
2.  Calls `TicketMgr::ShowList` with the `onlineOnly` flag set to `true`.

**HandleGMTicketListClosedCommand**
Calls `TicketMgr::ShowClosedList` to display archived/closed tickets.

**HandleGMTicketListEscalatedCommand**
Calls `TicketMgr::ShowEscalatedList` to display tickets in the escalation queue.

### Queue Navigation and Counters

**HandleGMTicketNextCommand**
Moves the GM's cursor to the next ticket in the queue.
1.  Gets the current ticket counter from the GM's player object.
2.  Calls `TicketMgr::GetNextTicket` to find the next valid ticket ID.
3.  If found, updates the GM's counter and calls `ViewTicket` to display it.

**HandleGMTicketPreviousCommand**
Moves the GM's cursor to the previous ticket in the queue.
1.  Gets the current ticket counter.
2.  Calls `TicketMgr::GetPreviousTicket`.
3.  If found, updates the GM's counter and calls `ViewTicket`.

**HandleGMTicketCounterCommand**
Sets the GM's personal ticket counter to a specific ID.
1.  Extracts a uint32 counter value.
2.  Caps the value at the last known ticket ID (`TicketMgr::GetLastTicketId`) to prevent out-of-bounds errors.
3.  Sets the counter on the player object.

### System Administration

**HandleToggleGMTicketSystem**
Toggles the global enable/disable state of the ticket system.
1.  Reads the current status via `TicketMgr::GetStatus`.
2.  Inverts the status and sets it via `TicketMgr::SetStatus`.
3.  Sends a confirmation message.

**HandleGMTicketResetCommand**
Resets the entire ticket queue (clears all open tickets).
1.  Checks if there are any open tickets via `TicketMgr::GetOpenTicketCount`.
2.  If yes, it prevents the reset and warns the user.
3.  If no, it calls `TicketMgr::ResetTickets` and confirms.

**HandleGMTicketReloadCommand**
Adds a specific ticket to a reload list (likely for debugging or re-processing).
1.  Extracts the ticket ID.
2.  Calls `TicketMgr::ReloadTicket`.
3.  Confirms the action.

**HandleGMTicketNotifyCommand**
Toggles whether the player receives notifications for new tickets.
1.  Extracts an On/Off boolean.
2.  Sets the player's `AcceptTicket` flag via `Player::SetAcceptTicket`.
3.  Confirms the setting.

## Cross-Unit Boundaries

This unit interacts extensively with other parts of the server to resolve entities, enforce permissions, and persist data.

*   **TicketMgr / GMTicketMgr (`TicketMgr/instance`, `GMTicketMgr/*`):**
    *   *Direction:* Outbound calls.
    *   *Purpose:* This is the core dependency. `TicketCommands` delegates all state mutations (assign, close, comment, save) and data retrieval (get ticket, list tickets) to these managers. `TicketMgr::instance` provides the singleton access point.
    *   *Key Interactions:* `GetTicket`, `SaveToDB`, `SetAssignedTo`, `CloseTicket`, `RemoveTicket`, `ShowList`.

*   **ObjectMgr (`ObjectMgr/*`):**
    *   *Direction:* Outbound calls.
    *   *Purpose:* Resolves player names to GUIDs and Account IDs.
    *   *Key Interactions:* `GetPlayerGuidByName`, `GetPlayerAccountIdByGUID`, `normalizePlayerName`.

*   **AccountMgr (`AccountMgr/GetSecurity`):**
    *   *Direction:* Outbound calls.
    *   *Purpose:* Retrieves the security level (GM rank) of an account to enforce permission checks during assignment/unassignment.

*   **WorldSession / Player (`WorldSession.Main/*`, `Player.Main/*`):**
    *   *Direction:* Outbound calls.
    *   *Purpose:* Identifies the current user (GM) executing the command, retrieves their name, GUID, and security level, and sends packets to the ticket submitter.
    *   *Key Interactions:* `GetSession`, `GetPlayer`, `GetPlayerName`, `GetSecurity`, `SendPacket`.

*   **ChatHandler.Chat (`ChatHandler.Chat/*`):**
    *   *Direction:* Outbound calls (internal to the class hierarchy, but distinct units in the map).
    *   *Purpose:* Provides utility functions for parsing arguments (`atoi`, `strtok` wrappers), sending system messages (`SendSysMessage`, `PSendSysMessage`), and extracting formatted strings (`PGetParseString`). Note: Methods like `GetSession` and `SendSysMessage` are declared in `Chat.h` but implemented in other partials of `ChatHandler`; this unit only consumes them.

*   **World (`World/*`):**
    *   *Direction:* Outbound calls.
    *   *Purpose:* Broadcasts ticket-related text to all online GMs (`SendGMTicketText`) and retrieves configuration values (`getConfig`).

*   **Object / ObjectGuid (`Object/*`, `ObjectGuid/*`):**
    *   *Direction:* Outbound calls.
    *   *Purpose:* Low-level GUID manipulation and validation.

## Data Model

This unit does not directly execute SQL queries or define database schemas. It relies entirely on the `TicketMgr` and `GMTicketMgr` classes to handle database interactions. Therefore, no specific table structures are exposed in this unit's source code. The "Tables" column in the MAP is empty for all members, confirming that data persistence is abstracted away from the command handling layer.

## Notable Implementation Details

1.  **Console vs. In-Game Security Bypass:**
    Many commands (e.g., `HandleGMTicketAssignToCommand`, `HandleGMTicketCloseByIdCommand`, `HandleGMTicketCommentCommand`) include a check:
    ```cpp
    Player* player = GetSession() ? GetSession()->GetPlayer() : nullptr;
    if (player && ticket->IsAssignedNotTo(player->GetGUID())) { ... }
    ```
    This logic explicitly allows console users (where `GetSession()` returns `nullptr`) to bypass assignment restrictions. An in-game GM can only modify tickets assigned to themselves, whereas a console admin can modify any ticket regardless of assignment. This is a critical operational distinction for server administrators.

2.  **Automatic Assignment on Completion:**
    In `HandleGMTicketCompleteCommand`, if a ticket is marked as completed and was previously unassigned, the code automatically assigns it to the completing GM:
    ```cpp
    if (GetSession() && GetSession()->GetPlayer() && !ticket->GetAssignedToGUID())
        ticket->SetAssignedTo(GetSession()->GetPlayer()->GetObjectGuid(), true);
    ```
    This ensures that completed tickets always have an owner record, likely for audit trails.

3.  **Category Mapping Duplication:**
    The `categories` unordered map is defined identically in both `HandleGMTicketListCommand` and `HandleGMTicketListOnlineCommand`. While functional, this represents duplicated data that could be refactored into a static class member or a shared utility function to maintain consistency if categories change.

4.  **Ticket Deletion Constraint:**
    `HandleGMTicketDeleteByIdCommand` enforces that a ticket must be closed before it can be deleted. This prevents accidental deletion of active support requests. The error message `LANG_COMMAND_TICKETCLOSEFIRST` guides the user to close it first.

5.  **Response Appending Logic:**
    The private helper `_HandleGMTicketResponseAppendCommand` handles both standard appending and newline-appending. This separation allows `HandleGMTicketResponseAppendLnCommand` to simply pass `true` for the `newLine` flag, keeping the main command handlers clean.

6.  **Counter Capping:**
    In `HandleGMTicketCounterCommand`, the input counter is capped at `sTicketMgr->GetLastTicketId()`. This prevents the GM's cursor from pointing to a non-existent future ticket ID, which would cause `GetNextTicket` or `GetPreviousTicket` to fail or behave unexpectedly.

## Member Reference

**HandleGMTicketAssignToCommand**: Assigns a ticket to a specified player/GM, enforcing assignment ownership rules for in-game users while allowing console bypass. Persists changes and broadcasts the assignment.

**HandleGMTicketCloseByIdCommand**: Closes a ticket, requiring the closer to be the assigned GM (unless console). Notifies the submitter and updates the ticket status.

**HandleGMTicketCommentCommand**: Adds an internal comment to a ticket, restricted to the assigned GM (unless console). Persists the comment and broadcasts the update.

**HandleGMTicketListClosedCommand**: Delegates to `TicketMgr::ShowClosedList` to display archived tickets.

**HandleGMTicketCompleteCommand**: Marks a ticket as completed, optionally appending a response. Automatically assigns the ticket to the completer if it was unassigned. Notifies the submitter.

**HandleGMTicketDeleteByIdCommand**: Permanently deletes a ticket, strictly requiring it to be closed first. Notifies the submitter to remove it from their UI.

**HandleGMTicketEscalateCommand**: Moves a ticket to the escalation queue, setting the required security level to the current user's level + 1.

**HandleGMTicketListEscalatedCommand**: Delegates to `TicketMgr::ShowEscalatedList` to display escalated tickets.

**HandleGMTicketListCommand**: Lists open tickets, optionally filtered by category (e.g., "stuck", "behavior").

**HandleGMTicketListOnlineCommand**: Lists open tickets for online players, optionally filtered by category.

**HandleGMTicketResetCommand**: Resets the ticket queue if no open tickets exist, preventing accidental data loss.

**HandleToggleGMTicketSystem**: Toggles the global enable/disable state of the ticket system.

**HandleGMTicketUnAssignCommand**: Removes the assignment from a ticket, enforcing security checks to prevent lower-level GMs from unassigning tickets held by higher-level GMs.

**ViewTicketByIdOrName**: Internal helper that resolves a ticket by ID or player name, validates its status, and delegates to `ViewTicket`.

**ViewTicket**: Marks a ticket as viewed, notifies the submitter if online, persists the state, and displays the ticket details to the GM.

**HandleGMTicketGetByIdOrNameCommand**: Wrapper that calls `ViewTicketByIdOrName` with both ID and name arguments.

**HandleGMTicketGetByIdCommand**: Wrapper that calls `ViewTicketByIdOrName` with only the ID argument.

**HandleGMTicketGetByNameCommand**: Wrapper that calls `ViewTicketByIdOrName` with only the name argument.

**HandleGMTicketResponseResetCommand**: Clears the GM's response text from a ticket, restricted to the assigned GM (unless console).

**_HandleGMTicketResponseAppendCommand**: Private helper that appends text to the GM's response, optionally adding a newline. Restricted to the assigned GM (unless console).

**HandleGMTicketResponseAppendCommand**: Wrapper that calls `_HandleGMTicketResponseAppendCommand` without a newline.

**HandleGMTicketResponseAppendLnCommand**: Wrapper that calls `_HandleGMTicketResponseAppendCommand` with a newline.

**HandleGMTicketNotifyCommand**: Toggles the player's preference for receiving ticket notifications.

**HandleGMTicketCounterCommand**: Sets the GM's personal ticket counter, capping it at the last known ticket ID.

**HandleGMTicketNextCommand**: Advances the GM's ticket counter to the next available ticket and displays it.

**HandleGMTicketPreviousCommand**: Retreats the GM's ticket counter to the previous available ticket and displays it.

**HandleGMTicketReloadCommand**: Adds a specific ticket to a reload list for administrative processing.

---

<!-- machine-true, projected from graph.json -->

## Map — ChatHandler.TicketCommands

*Source:* TicketCommands.cpp, Chat.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| HandleGMTicketAssignToCommand | method | AccountMgr/GetSecurity, ChatHandler.Chat/GetSession, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, GMTicketMgr/FormatMessageString, GMTicketMgr/GetId, GMTicketMgr/IsAssignedNotTo, GMTicketMgr/IsAssignedTo, GMTicketMgr/IsClosed, GMTicketMgr/SaveToDB, GMTicketMgr/SetAssignedTo, Object/GetGUID, ObjectGuid/ObjectGuid#5, ObjectMgr/GetPlayerAccountIdByGUID, ObjectMgr/GetPlayerGuidByName, ObjectMgr/normalizePlayerName, TicketMgr/GetTicket, TicketMgr/instance, TicketMgr/UpdateLastChange, World/getConfig#4, World/SendGMTicketText, WorldSession.Main/GetPlayer, WorldSession.Main/GetPlayerName | — | — |
| HandleGMTicketCloseByIdCommand | method | ByteBuffer/operator<<#10, ChatHandler.Chat/GetSession, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, GMTicketMgr/CloseTicket, GMTicketMgr/FormatMessageString, GMTicketMgr/GetId, GMTicketMgr/GetPlayer, GMTicketMgr/IsAssignedNotTo, GMTicketMgr/IsClosed, GMTicketMgr/IsCompleted, Object/GetGUID, Object/GetObjectGuid, ObjectGuid/ObjectGuid#5, Player.Main/GetName, Player.Main/GetSession, TicketMgr/GetTicket, TicketMgr/instance, TicketMgr/UpdateLastChange, World/SendGMTicketText, WorldPacket/WorldPacket#4, WorldSession.Main/GetPlayer, WorldSession.Main/SendPacket | — | — |
| HandleGMTicketCommentCommand | method | ChatHandler.Chat/GetSession, ChatHandler.Chat/PGetParseString, ChatHandler.Chat/PSendSysMessage#2, GMTicketMgr/FormatMessageString, GMTicketMgr/GetAssignedToName, GMTicketMgr/GetId, GMTicketMgr/IsAssignedNotTo, GMTicketMgr/IsClosed, GMTicketMgr/SaveToDB, GMTicketMgr/SetComment, Object/GetGUID, ObjectGuid/ObjectGuid#5, Player.Main/GetName, TicketMgr/GetTicket, TicketMgr/instance, TicketMgr/UpdateLastChange, World/SendGMTicketText, WorldSession.Main/GetPlayer | — | — |
| HandleGMTicketListClosedCommand | method | GMTicketMgr/ShowClosedList, TicketMgr/instance | — | — |
| HandleGMTicketCompleteCommand | method | ChatHandler.Chat/GetSession, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, GMTicketMgr/AppendResponse, GMTicketMgr/FormatMessageString, GMTicketMgr/GetAssignedToGUID, GMTicketMgr/GetId, GMTicketMgr/GetPlayer, GMTicketMgr/IsAssignedNotTo, GMTicketMgr/IsClosed, GMTicketMgr/IsCompleted, GMTicketMgr/SaveToDB, GMTicketMgr/SendResponse, GMTicketMgr/SetAssignedTo, GMTicketMgr/SetCompleted, Object/GetGUID, Object/GetObjectGuid, ObjectGuid/ObjectGuid#5, ObjectGuid/operator!, Player.Main/GetName, Player.Main/GetSession, TicketMgr/GetTicket, TicketMgr/instance, TicketMgr/UpdateLastChange, World/SendGMTicketText, WorldSession.Main/GetPlayer | — | — |
| HandleGMTicketDeleteByIdCommand | method | ByteBuffer/operator<<#10, ChatHandler.Chat/GetSession, ChatHandler.Chat/SendSysMessage#2, GMTicketMgr/FormatMessageString, GMTicketMgr/GetId, GMTicketMgr/GetPlayer, GMTicketMgr/IsClosed, GMTicketMgr/RemoveTicket, Player.Main/GetName, Player.Main/GetSession, TicketMgr/GetTicket, TicketMgr/instance, TicketMgr/UpdateLastChange, World/SendGMTicketText, WorldPacket/WorldPacket#4, WorldSession.Main/GetPlayer, WorldSession.Main/SendPacket | — | — |
| HandleGMTicketEscalateCommand | method | ChatHandler.Chat/GetSession, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, GMTicketMgr/GetEscalatedStatus, GMTicketMgr/GetPlayer, GMTicketMgr/IsClosed, GMTicketMgr/IsCompleted, GMTicketMgr/SendTicket, GMTicketMgr/SetEscalatedStatus, GMTicketMgr/SetNeededSecurityLevel, Player.Main/GetSession, TicketMgr/GetTicket, TicketMgr/instance, TicketMgr/UpdateLastChange, WorldSession.Main/GetSecurity | — | — |
| HandleGMTicketListEscalatedCommand | method | GMTicketMgr/ShowEscalatedList, TicketMgr/instance | — | — |
| HandleGMTicketListCommand | method | GMTicketMgr/ShowList, TicketMgr/instance | — | — |
| HandleGMTicketListOnlineCommand | method | GMTicketMgr/ShowList, TicketMgr/instance | — | — |
| HandleGMTicketResetCommand | method | ChatHandler.Chat/SendSysMessage#2, GMTicketMgr/ResetTickets, TicketMgr/GetOpenTicketCount, TicketMgr/instance | — | — |
| HandleToggleGMTicketSystem | method | ChatHandler.Chat/PSendSysMessage#2, TicketMgr/GetStatus, TicketMgr/instance, TicketMgr/SetStatus | — | — |
| HandleGMTicketUnAssignCommand | method | AccountMgr/GetSecurity, ChatHandler.Chat/GetSession, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, GMTicketMgr/FormatMessageString, GMTicketMgr/GetAssignedPlayer, GMTicketMgr/GetAssignedToGUID, GMTicketMgr/GetAssignedToName, GMTicketMgr/GetId, GMTicketMgr/IsAssigned, GMTicketMgr/IsClosed, GMTicketMgr/SaveToDB, GMTicketMgr/SetUnassigned, ObjectMgr/GetPlayerAccountIdByGUID, Player.Main/GetName, Player.Main/GetSession, TicketMgr/GetTicket, TicketMgr/instance, TicketMgr/UpdateLastChange, World/SendGMTicketText, WorldSession.Main/GetPlayer, WorldSession.Main/GetSecurity | — | — |
| ViewTicketByIdOrName | method | ChatHandler.Chat/SendSysMessage#2, GMTicketMgr/IsClosed, GMTicketMgr/IsCompleted, Object/GetGUID, ObjectAccessor/FindPlayerByName, ObjectGuid/ObjectGuid, ObjectGuid/ObjectGuid#5, ObjectGuid/operator!, ObjectMgr/GetPlayerGuidByName, ObjectMgr/normalizePlayerName, TicketMgr/GetTicket, TicketMgr/GetTicketByPlayer, TicketMgr/instance | — | — |
| ViewTicket | method | ChatHandler.Chat/SendSysMessage, GMTicketMgr/FormatMessageString#2, GMTicketMgr/GetPlayer, GMTicketMgr/SaveToDB, GMTicketMgr/SendTicket, GMTicketMgr/SetViewed, Player.Main/GetSession, TicketMgr/instance | — | — |
| HandleGMTicketGetByIdOrNameCommand | method | — | — | — |
| HandleGMTicketGetByIdCommand | method | — | — | — |
| HandleGMTicketGetByNameCommand | method | — | — | — |
| HandleGMTicketResponseResetCommand | method | ChatHandler.Chat/GetSession, ChatHandler.Chat/PSendSysMessage#2, GMTicketMgr/FormatMessageString#2, GMTicketMgr/GetId, GMTicketMgr/IsAssignedNotTo, GMTicketMgr/IsClosed, GMTicketMgr/ResetResponse, GMTicketMgr/SaveToDB, Object/GetGUID, ObjectGuid/ObjectGuid#5, TicketMgr/GetTicket, TicketMgr/instance, WorldSession.Main/GetPlayer | — | — |
| _HandleGMTicketResponseAppendCommand | function | ChatHandler.Chat/GetSession, ChatHandler.Chat/PSendSysMessage#2, GMTicketMgr/AppendResponse, GMTicketMgr/GetId, GMTicketMgr/IsAssignedNotTo, GMTicketMgr/IsClosed, GMTicketMgr/SaveToDB, Object/GetGUID, ObjectGuid/ObjectGuid#5, TicketMgr/GetTicket, TicketMgr/instance, WorldSession.Main/GetPlayer | — | — |
| HandleGMTicketResponseAppendCommand | method | — | — | — |
| HandleGMTicketResponseAppendLnCommand | method | — | — | — |
| HandleGMTicketNotifyCommand | method | ChatHandler.Chat/ExtractOnOff, ChatHandler.Chat/GetSession, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/SetAcceptTicket, WorldSession.Main/GetPlayer | — | — |
| HandleGMTicketCounterCommand | method | ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/GetSession, ChatHandler.Chat/PSendSysMessage#2, Player.Main/SetGMTicketCounter, TicketMgr/GetLastTicketId, TicketMgr/instance, WorldSession.Main/GetPlayer | — | — |
| HandleGMTicketNextCommand | method | ChatHandler.Chat/GetSession, ChatHandler.Chat/SendSysMessage#2, GMTicketMgr/GetId, Player.Main/GetGMTicketCounter, Player.Main/SetGMTicketCounter, TicketMgr/GetNextTicket, TicketMgr/instance, WorldSession.Main/GetPlayer | — | — |
| HandleGMTicketPreviousCommand | method | ChatHandler.Chat/GetSession, ChatHandler.Chat/SendSysMessage#2, GMTicketMgr/GetId, Player.Main/GetGMTicketCounter, Player.Main/SetGMTicketCounter, TicketMgr/GetPreviousTicket, TicketMgr/instance, WorldSession.Main/GetPlayer | — | — |
| HandleGMTicketReloadCommand | method | ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/PSendSysMessage, GMTicketMgr/ReloadTicket, TicketMgr/instance | — | — |

---

<!-- verify: boundary-bleed | foreign: ChatHandler, disable, enable, update -->
