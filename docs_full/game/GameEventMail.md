# GameEventMail

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# GameEventMail

**Purpose & Responsibilities**

`GameEventMail` is a lightweight data structure within the `GameEventMgr` subsystem. It holds the configuration parameters required to send in-game mail to players when a specific game event starts or ends. It acts as a container for the recipient criteria (`raceMask`), associated context (`questId`), content definition (`mailTemplateId`), and sender identity (`senderEntry`). It contains no logic; it is purely a data holder used by `GameEventMgr` to manage automated mail notifications tied to event lifecycles.

## Member-by-Member Behavior

The unit consists entirely of two constructors that initialize the four member variables.

### Constructors

1.  **Default Constructor (`GameEventMail()`)**
    *   Initializes all member fields (`raceMask`, `questId`, `mailTemplateId`, `senderEntry`) to zero.
    *   Used for pre-allocation of containers (e.g., `std::vector`) before population.

2.  **Parameterized Constructor (`GameEventMail(uint32, uint32, uint32, uint32)`)**
    *   Accepts four `uint32` arguments corresponding to `raceMask`, `questId`, `mailTemplateId`, and `senderEntry`.
    *   Assigns these values directly to the respective member variables.
    *   Used during the database loading phase to populate the struct with configured event mail data.

## Cross-Unit Boundaries

*   **Called By:**
    *   `GameEventMgr.Main/LoadFromDB`: The `GameEventMgr` singleton loads event configurations from the database. It instantiates `GameEventMail` objects using the parameterized constructor to store mail settings associated with specific event IDs. These objects are stored in `GameEventMgr`'s internal `mGameEventMails` map.
*   **Calls Out:**
    *   None. This unit is a pure data holder.

## Data Model

The `GameEventMail` struct maps directly to rows in the `game_event_mail` table in the database. The fields correspond to the following logical columns:

*   **`raceMask`**: Corresponds to the `racemask` column. Defines which player races are eligible to receive the mail.
*   **`questId`**: Corresponds to the `quest_id` column. If non-zero, the mail is likely conditional on the player having completed or being eligible for this quest, or the mail serves to reward/notify about this quest.
*   **`mailTemplateId`**: Corresponds to the `mail_template_id` column. References the `mail_template` table, defining the subject, body, and attachments of the mail.
*   **`senderEntry`**: Corresponds to the `sender_entry` column. References a creature entry in the `creature_template` table, defining who appears as the sender of the mail.

## Notable Implementation Details

*   **Zero-Value Semantics**: All fields default to 0. A `raceMask` of 0 implies no races match (unless interpreted otherwise by `GameEventMgr`). A `questId` of 0 indicates no quest association. A `mailTemplateId` of 0 would likely result in no mail being sent if not validated.
*   **No Validation Logic**: The struct contains no validation logic (e.g., checking if `mailTemplateId` exists). Validation is expected to be handled by `GameEventMgr` during the `LoadFromDB` phase or when the mail is actually dispatched.
*   **Memory Layout**: As a simple struct with four `uint32` fields, it is compact and cache-friendly, suitable for storage in `std::vector` containers within `GameEventMgr`.

## Member Reference

**GameEventMail**
Default constructor. Initializes `raceMask`, `questId`, `mailTemplateId`, and `senderEntry` to 0. Used for pre-allocation of containers.

**GameEventMail#2**
Parameterized constructor. Takes `_raceMask`, `_quest`, `_mailTemplateId`, and `_senderEntry` as arguments and assigns them to the corresponding member variables. Used when loading data from the database via `GameEventMgr.LoadFromDB`.

---

<!-- machine-true, projected from graph.json -->

## Map — GameEventMail

*Source:* GameEventMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GameEventMail | ctor | — | GameEventMgr.Main/LoadFromDB | — |
| GameEventMail#2 | ctor | — | — | — |
