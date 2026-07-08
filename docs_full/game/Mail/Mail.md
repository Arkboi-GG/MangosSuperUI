# Mail

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`Mail.h` defines the core data structures and enumerations for the mail system within the `wowvmangos` server. It provides the foundational types required to create, track, and manipulate in-game mail messages. The unit is strictly a definition layer; it contains no implementation logic for network handling, database persistence, or item management, which reside in other units (`Mail.cpp`, `MassMailMgr`, `WorldSession`, etc.).

The primary responsibilities of this unit are:
1.  **Defining Mail Metadata:** Establishing enums for mail types (`MailMessageType`), status flags (`MailCheckMask`), stationery styles (`MailStationery`), and lifecycle states (`MailState`).
2.  **Modeling Participants:** Providing `MailSender` and `MailReceiver` classes to encapsulate the identity and context of the parties involved in a transaction.
3.  **Drafting Messages:** Implementing `MailDraft`, a builder-style class that accumulates subject, body, money, COD (Cash on Delivery), and attached items before final transmission.
4.  **Representing Stored Mail:** Defining the `Mail` struct, which mirrors the persistent state of a mail message in the database, including its items, timestamps, and flags.

This unit enforces constraints such as `MAX_MAIL_ITEMS` (currently defined as 1, though the data structures support vectors) and prevents accidental copying of `MailDraft` objects due to the high cost of cloning item instances.

## Member-by-Member Behavior

The members defined in this unit are grouped by the class or struct they belong to. Note that while `MailDraft` and `Mail` contain many methods, only those listed in the MAP are considered "this unit's behavior" for the purpose of this documentation. The remaining methods in the source code (e.g., constructors, accessors, modifiers like `SetMoney`, `AddItem` on `MailDraft`) are part of the interface but are not explicitly tracked in the cross-unit map provided.

### The `Mail` Struct

The `Mail` struct represents a concrete mail message, typically loaded from or saved to the database. It holds all necessary fields to reconstruct the mail's state, including item lists, monetary values, and timestamps.

*   **`AddItem`**: Appends a new item to the mail's internal `items` vector. It takes the item's GUID low part and template ID, wraps them in a `MailItemInfo` struct, and pushes them onto the `items` container. Crucially, it sets the `has_items` flag to `true`. This method performs no validation regarding capacity limits or item legality; it assumes the caller has verified these conditions. It is called by `game_Mail_Mail/prepareTemplateItems` when generating items for template-based mails, by `game_Mail_Mail/SendMailTo` when attaching items during sending, and by `MasterPlayer.Main/LoadMailedItems` when reconstructing mail from the database.

*   **`RemoveItem`**: Searches the `items` vector for an entry matching the provided `itemGuid`. If found, it erases the entry from the vector and returns `true`. If no match is found, it returns `false`. This method is used by `WorldSession.MailHandler/HandleMailTakeItem` when a player retrieves an item from their mailbox.

*   **`HasItems`**: Returns the value of the `has_items` boolean flag. This flag indicates whether the mail contains any items or if template-based items have already been generated (even if the result was none). It is checked by `MasterPlayer.Main/SaveMails` to determine if item data needs to be persisted, by `WorldSession.MailHandler/HandleMailReturnToSender` to handle item return logic, and by `WorldSession.MailHandler/HandleMailTakeItem` to verify item presence before removal.

### Other Classes and Enums

While the following members are defined in this unit, they are not listed in the MAP's "Member" column, implying they are either internal helpers, constructors, or accessors not involved in the specific cross-unit interactions tracked. However, they are essential for understanding the unit's API:

*   **`MailSender`**: Encapsulates sender information. It distinguishes between player senders (using GUIDs) and non-player senders (using entries). It tracks the mail type and stationery style.
*   **`MailReceiver`**: Encapsulates recipient information, holding both a pointer to the `Player` object (if online) and the `ObjectGuid` for persistence.
*   **`MailDraft`**: A builder class for creating new mails. It manages a temporary collection of items (`m_items`) and metadata. It prevents copying via deleted copy constructor/assignment operator to avoid expensive item cloning. Key methods include `SendMailTo` (finalizes the draft and sends it) and `SendReturnToSender` (handles returns).
*   **Enums**: `MailMessageType` defines the origin/type of mail (Normal, Auction, Creature, etc.). `MailCheckMask` uses bitwise flags to track read status, return status, COD payment, etc. `MailStationery` maps to visual styles. `MailState` tracks whether the mail is unchanged, changed, or deleted.

## Cross-Unit Boundaries

The `Mail` struct's methods serve as the bridge between the mail data model and the higher-level game logic and session handling.

### `Mail::AddItem`
*   **Called By:**
    *   `game_Mail_Mail/prepareTemplateItems`: Used when a mail is based on a template (e.g., auction house notifications) and needs to generate specific items dynamically.
    *   `game_Mail_Mail/SendMailTo`: Invoked when a standard mail with attached items is being constructed and sent.
    *   `MasterPlayer.Main/LoadMailedItems`: Called during the loading process to reconstruct the item list for a player's existing mails from the database.
*   **Direction:** Inbound data population. These units provide the item GUIDs and IDs, and `Mail::AddItem` stores them in the `Mail` instance.

### `Mail::RemoveItem`
*   **Called By:**
    *   `WorldSession.MailHandler/HandleMailTakeItem`: Triggered when a player interacts with the mailbox UI to take an item.
*   **Direction:** Outbound modification. The session handler identifies the item to remove and calls this method to update the local `Mail` state. The actual database update and item transfer logic is handled by the caller (`WorldSession.MailHandler`), while this method simply updates the in-memory representation.

### `Mail::HasItems`
*   **Called By:**
    *   `MasterPlayer.Main/SaveMails`: Checks if the mail has items before deciding how to serialize/save the mail data.
    *   `WorldSession.MailHandler/HandleMailReturnToSender`: Determines if items need to be returned to the sender when a mail is bounced.
    *   `WorldSession.MailHandler/HandleMailTakeItem`: Validates that the mail actually contains items before attempting to remove one.
*   **Direction:** Query. These units query the state of the `Mail` object to make control-flow decisions.

## Data Model

The `Mail` struct directly corresponds to the `mail` table in the database, although the schema is not provided in the input. Based on the member variables in `Mail`, the following columns are implicitly touched:
*   `messageID`: Primary key.
*   `messageType`, `stationery`, `mailTemplateId`: Metadata flags.
*   `sender`, `receiverGuid`: Identifiers for participants.
*   `subject`, `itemTextId`: Content references.
*   `has_items`, `items` (serialized): Item attachment data.
*   `removedItems`: Track items already taken.
*   `expire_time`, `deliver_time`: Timestamps.
*   `money`, `COD`: Financial values.
*   `checked`: Status flags.
*   `state`: Lifecycle state.

No other database tables are directly referenced by the members in this unit. Item details (template IDs, GUIDs) are stored within the `Mail` struct's vectors, implying that the database likely stores serialized versions of these vectors or related rows in an `mail_items` table (not shown in schema, but inferred from `MailItemInfoVec`).

## Notable Implementation Details

1.  **`MAX_MAIL_ITEMS` Constraint:** The macro `MAX_MAIL_ITEMS` is defined as `1`. This is a significant constraint. While the `Mail` struct uses `std::vector<MailItemInfo>` which theoretically supports unlimited items, the constant suggests a hard limit enforced elsewhere (likely in `MailDraft::AddItem` or validation logic in `Mail.cpp`). Maintainers must ensure that any logic adding items respects this limit, or the constant is updated if the limit changes.
2.  **`MailDraft` Copy Semantics:** The `MailDraft` class explicitly deletes its copy constructor and assignment operator (`MailDraft(MailDraft const&) = delete;`). The comment explains that cloning items is a "high price operation." This forces users to use `CloneFrom` for explicit cloning or pass by reference/pointer. This is a critical performance optimization to prevent accidental deep copies of item instances.
3.  **`has_items` Flag Logic:** The `has_items` flag in `Mail` is not just a count check. The comment for `HasItems()` notes it returns true if "template items already generated possible none". This means `has_items` acts as a "processed" flag for template-based mails. If a template generates zero items, `has_items` is still set to `true` to indicate that the generation step has occurred and no further generation is needed. This distinction is vital for `prepareTemplateItems` to avoid re-generating items for offline players.
4.  **`MailSender` GUID Handling:** The `MailSender` constructor traps incorrect usage of full GUIDs vs. low GUIDs. The private constructor `MailSender(MailMessageType, uint64, ...)` is a trap to catch developers passing a 64-bit GUID where a 32-bit low GUID or entry is expected. This prevents subtle bugs in sender identification.
5.  **Item Removal Efficiency:** `Mail::RemoveItem` uses a linear search (`std::find_if` equivalent via loop) over the `items` vector. Given `MAX_MAIL_ITEMS` is 1, this is O(1) effectively. If the limit were higher, this would become a performance bottleneck, suggesting the vector approach is optimized for the current small constraint.

## Member Reference

**AddItem**
Appends an item to the `Mail`'s `items` vector and sets the `has_items` flag to true. It accepts the item's GUID low part and template ID. It is called by `game_Mail_Mail/prepareTemplateItems`, `game_Mail_Mail/SendMailTo`, and `MasterPlayer.Main/LoadMailedItems` to populate mail contents from templates, direct attachments, or database loads.

**RemoveItem**
Searches the `Mail`'s `items` vector for an item matching the given GUID. If found, it removes the item and returns true; otherwise, it returns false. It is called by `WorldSession.MailHandler/HandleMailTakeItem` when a player retrieves an item.

**HasItems**
Returns the `has_items` boolean flag, indicating whether the mail contains items or if template items have been generated. It is called by `MasterPlayer.Main/SaveMails`, `WorldSession.MailHandler/HandleMailReturnToSender`, and `WorldSession.MailHandler/HandleMailTakeItem` to check mail state before saving, returning, or taking items.

---

<!-- machine-true, projected from graph.json -->

## Map — Mail

*Source:* Mail.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AddItem | method | — | game_Mail_Mail/prepareTemplateItems, game_Mail_Mail/SendMailTo, MasterPlayer.Main/LoadMailedItems | — |
| RemoveItem | method | — | WorldSession.MailHandler/HandleMailTakeItem | — |
| HasItems | method | — | MasterPlayer.Main/SaveMails, WorldSession.MailHandler/HandleMailReturnToSender, WorldSession.MailHandler/HandleMailTakeItem | — |
