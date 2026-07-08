# MassMailMgr

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# MassMailMgr

**Purpose & Responsibilities**

`MassMailMgr` is a singleton manager that handles bulk mail operations (sending items, money, or messages to many players) in a non-blocking manner. It decouples recipient identification from mail delivery to prevent server lag.

1.  **Queueing:** Callers submit a mail template and criteria. `MassMailMgr` asynchronously queries the database to identify recipient GUIDs.
2.  **Processing:** On every server tick, `Update()` processes a limited number of queued tasks, cloning the mail template and sending it to individual recipients.

Pending tasks are not persisted; they are lost on server shutdown.

## Member-by-Member Behavior

### Task Submission

Three overloads of `AddMassMailTask` initiate mass mail jobs:

*   **`AddMassMailTask(MailDraft*, MailSender, uint32 raceMask)`**: High-level interface. Generates an SQL query filtering `characters` by `race` (using bitmasks) and `deleted_time IS NULL`. If the mask includes all playable races, it skips the race filter. Delegates to the SQL-string overload.
*   **`AddMassMailTask(MailDraft*, MailSender, char const* query)`**: Mid-level interface. Submits the provided SQL query to `CharacterDatabase.AsyncPQuery` with `HandleQueryCallback` as the handler. The `mailProto` is passed to the callback for ownership transfer or deletion.
*   **`AddMassMailTask(MailDraft*, MailSender)`**: Low-level interface. Creates a `MassMail` task in `m_massMails` and returns a reference to its `m_receivers` set. Used internally by the callback to populate recipients.

### Callback Handling

*   **`HandleQueryCallback`**: Static handler for async query results. If the result is null, it deletes `mailProto`. Otherwise, it calls the low-level `AddMassMailTask` to create the task container, then iterates through the `QueryResult`, inserting each recipient GUID (first field) into the task's `m_receivers` set.

### Execution

*   **`Update`**: Called by `World::Update` and `Master::Run`. Processes tasks from `m_massMails` up to `CONFIG_UINT32_MASS_MAILER_SEND_PER_TICK` per tick.
    *   For each task, it iterates through `m_receivers`.
    *   It resolves the recipient GUID to a `Player*` via `sObjectMgr.GetPlayer`.
    *   **Cloning:** If receivers remain after the current one, it clones `m_protoMail` via `MailDraft::CloneFrom`. If this is the last receiver, it uses the original `m_protoMail` directly to save an allocation.
    *   It sends the mail using `MailDraft::SendMailTo` with `MAIL_CHECK_MASK_RETURNED` to suppress bounce notifications.
    *   Completed tasks are removed from the queue.

### Statistics

*   **`GetStatistic`**: Returns the count of pending tasks, total pending mails, and estimated completion time (seconds), calculated as `50ms * total_mails / send_per_tick`.

## Cross-Unit Boundaries

*   **ChatHandler.MiscCommands**: Calls `AddMassMailTask` (raceMask) for `.sendmassitems`, `.sendmassmail`, and `.sendmassmoney`.
*   **GameEventMgr.Main**: Calls `AddMassMailTask` (raceMask) via `SendEventMails` for event rewards.
*   **CharacterDatabase**: Executes async queries via `AsyncPQuery`.
*   **World**: Drives `Update` and provides `getConfig` for limits.
*   **ObjectMgr**: Resolves GUIDs to `Player*` objects in `Update`.
*   **game_Mail_Mail**: Handles actual mail creation (`MailDraft`, `MailReceiver`) and sending (`SendMailTo`).
*   **Errors/PrintStacktraceAndThrow**: Triggered by `MANGOS_ASSERT` in `MassMail` ctor if `mailProto` is null.

## Data Model

*   **`characters`**: Read-only access via SQL queries in `AddMassMailTask`.
    *   Columns: `guid` (recipient ID), `race` (filter), `deleted_time` (exclude deleted chars).
    *   Mail storage occurs in the `mail` table, managed by `game_Mail_Mail`, not directly by `MassMailMgr`.

## Notable Implementation Details

1.  **Async Decoupling**: Database queries run asynchronously. Recipients are added to the task only when results arrive, preventing main-thread stalls.
2.  **Memory Ownership**: `MailDraft*` is owned by `MassMail` (as `unique_ptr`). The copy constructor moves the pointer. Failed queries delete the draft in the callback to prevent leaks.
3.  **Last-Item Optimization**: `Update` avoids cloning the draft for the final recipient of a task, using the original pointer instead.
4.  **Offline Players**: `sObjectMgr.GetPlayer` returns `nullptr` for offline players. `MailReceiver` handles this by writing directly to the database, but `MassMailMgr` treats online/offline recipients identically.
5.  **Race Mask Assumption**: Filtering uses `(1 << (race - 1))`, assuming contiguous race IDs starting at 1.

## Member Reference

**MassMail#2**
Constructor for `MassMail`. Initializes `m_protoMail` and `m_sender`. Asserts `mailProto` is not null.

**AddMassMailTask#3**
Overload with `raceMask`. Builds SQL to select `guid` from `characters` filtered by race and `deleted_time`. Delegates to SQL-string overload.

**MassMailMgr**
Default constructor for the singleton.

**HandleQueryCallback**
Async callback. Deletes `mailProto` on failure. On success, creates task via low-level `AddMassMailTask` and populates `m_receivers` with GUIDs from `QueryResult`.

**AddMassMailTask#2**
Overload with SQL query string. Submits to `CharacterDatabase.AsyncPQuery` with `HandleQueryCallback`.

**Update**
Processes queued tasks up to config limit. Clones draft for all but last recipient. Sends mail with `MAIL_CHECK_MASK_RETURNED`. Removes completed tasks.

**AddMassMailTask**
Low-level overload. Creates `MassMail` task in `m_massMails` and returns reference to `m_receivers` for caller population.

**MassMail**
Struct holding `unique_ptr<MailDraft>`, `MailSender`, and `ReceiversList` (set of uint32 GUIDs).

**GetStatistic**
Returns task count, mail count, and estimated time in seconds.

---

<!-- machine-true, projected from graph.json -->

## Map — MassMailMgr

*Source:* MassMailMgr.cpp, MassMailMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| MassMail#2 | ctor | Errors/PrintStacktraceAndThrow | — | — |
| AddMassMailTask#3 | method | — | ChatHandler.MiscCommands/HandleSendMassItemsCommand, ChatHandler.MiscCommands/HandleSendMassMailCommand, ChatHandler.MiscCommands/HandleSendMassMoneyCommand, GameEventMgr.Main/SendEventMails | characters |
| MassMailMgr | ctor | — | — | — |
| HandleQueryCallback | method | Field/GetUInt32, QueryResult/Fetch, QueryResult/NextRow | — | — |
| AddMassMailTask#2 | method | — | GameEventMgr.Main/SendEventMails | — |
| Update | method | game_Mail_Mail/CloneFrom, game_Mail_Mail/MailReceiver#2, game_Mail_Mail/SendMailTo, MailDraft/MailDraft, ObjectGuid/ObjectGuid#2, ObjectMgr/GetPlayer, World/getConfig#4 | Master/Run, World/Update | — |
| AddMassMailTask | method | — | — | — |
| MassMail | ctor | — | — | — |
| GetStatistic | method | World/getConfig#4 | — | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `characters`: guid int(11) unsigned PK, account int(11) unsigned, name varchar(12), race tinyint(3) unsigned, class tinyint(3) unsigned, gender tinyint(3) unsigned, skin tinyint(3) unsigned, face tinyint(3) unsigned, hair_style tinyint(3) unsigned, hair_color tinyint(3) unsigned, facial_hair tinyint(3) unsigned, level tinyint(3) unsigned, xp int(10) unsigned, money int(10) unsigned, character_flags int(10) unsigned, zone int(11) unsigned, map int(11) unsigned, instance int(11) unsigned, position_x float, position_y float, position_z float, orientation float, transport_guid bigint(20) unsigned, transport_x float, transport_y float, transport_z float, transport_o float, known_taxi_mask longtext?, current_taxi_path text?, online tinyint(3) unsigned, played_time_total int(11) unsigned, played_time_level int(11) unsigned, create_time bigint(20) unsigned, logout_time bigint(20) unsigned, rest_bonus float, reset_talents_multiplier int(11) unsigned, reset_talents_time bigint(20) unsigned, death_expire_time bigint(20) unsigned, stable_slots tinyint(1) unsigned, bank_bag_slots tinyint(1) unsigned, extra_flags int(11) unsigned, honor_rank_points float, honor_highest_rank int(11) unsigned, honor_standing int(11) unsigned, honor_last_week_hk int(11) unsigned, honor_last_week_cp float, honor_stored_hk int(11), honor_stored_dk int(11), watched_faction int(11), drunk smallint(5) unsigned, health int(10) unsigned, power1 int(10) unsigned, power2 int(10) unsigned, power3 int(10) unsigned, power4 int(10) unsigned, power5 int(10) unsigned, explored_zones longtext?, equipment_cache longtext?, ammo_id int(10) unsigned, action_bars tinyint(3) unsigned, deleted_account int(11) unsigned?, deleted_name varchar(12)?, deleted_time bigint(20)?, world_phase_mask int(11)?

*`?` = nullable, `PK` = primary key column.*

