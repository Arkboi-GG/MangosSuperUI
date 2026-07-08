# MailDraft

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`MailDraft` is a builder-style class in `Mail.h` that constructs the payload for an outgoing mail message. It aggregates subject lines, body text (via static text IDs or dynamic strings), monetary amounts, Cash-on-Delivery (COD) fees, and attached `Item` instances. The class enforces a strict lifecycle: it is constructed, populated via setters, and finalized by `SendMailTo` or `SendReturnToSender`. Copying and assignment are disabled to prevent accidental duplication of expensive `Item` pointers; explicit cloning via `CloneFrom` is required.

## Member-by-Member Behavior

### Construction

**`MailDraft` (Default)**
Initializes a blank draft with zero money, COD, subject, and items. Used as the base for most programmatically generated mails.

**`MailDraft` (Template)**
Constructs a draft from a predefined `mailTemplateId`, setting `m_mailTemplateItemsNeed` to indicate if items should be generated from the template. Used for standardized system mails.

**`MailDraft` (Subject/BodyId)**
Creates a draft with a specific subject string and a static text entry ID (`itemTextId`) for the body.

**`MailDraft` (Subject/Body Text)**
Creates a draft with both a subject and a full body text string, allowing for completely dynamic content.

### Content Modification

**`SetMailTemplate`**
Assigns a mail template ID and specifies whether items are required. Used by `Player.Main/DeleteFromDB` and `WorldSession.MailHandler/HandleMailReturnToSender` to configure return mails.

**`SetMoney`**
Sets the copper coin amount. Heavily used by `AuctionHouseMgr`, `ChatHandler`, `Player.Main/RewardQuest`, and `WorldSession.MailHandler` for refunds, rewards, and payments.

**`SetCOD`**
Sets the Cash-on-Delivery fee. Used by `WorldSession.MailHandler/HandleSendMailCallback` when players send mail with COD.

**`AddItem`**
Adds an `Item` pointer to the internal `m_items` map, keyed by the item's GUID low part to prevent duplicates. Implementation resides in `Mail.cpp`.

**`SetSubjectAndBodyId` / `SetSubjectAndBody`**
Modifies the subject and body of an existing draft. Intended for blank drafts; mixing with template-based setups is discouraged but technically allowed.

### Accessors

**`GetMailTemplateId`**, **`GetSubject`**, **`GetBodyId`**, **`GetMoney`**, **`GetCOD`**
Simple getters returning the current state of the draft's fields. Called by `game_Mail_Mail/CloneFrom` and `game_Mail_Mail/SendMailTo` to extract data for persistence.

### Finalization

**`SendMailTo`**
The primary finisher. It validates the draft, prepares items (generating template items if necessary), persists the mail data to the database via `game_Mail_Mail`, sends network packets, and cleans up internal item pointers.

**`SendReturnToSender`**
A specialized finisher for undeliverable mail (e.g., mailbox full). It constructs a return mail to the original sender, refunding money/COD and reattaching items. It relies on `SetMailTemplate` and `SetMoney` internally.

**`CloneFrom`**
Copies the content of another `MailDraft` into this one. Required because copy semantics are disabled. Handles deep copying of items.

## Cross-Unit Boundaries

*   **Called By `ChatHandler.MiscCommands`:**
    *   `HandleSendItemsCommand`, `HandleSendMailCommand`, `HandleSendMassItemsCommand`, `HandleSendMassMailCommand`, `HandleSendMassMoneyCommand`, `HandleSendMoneyCommand`: Game Masters use these to manually construct `MailDraft` objects for sending items, money, or messages.

*   **Called By `Player.Main`:**
    *   `DeleteFromDB`: Processes pending mails when a player account is deleted, creating return mails for undelivered items/money.
    *   `AutoUnequipItemFromSlot`, `_LoadInventory`: May trigger mail creation if inventory slots are full or items need moving upon login/death.
    *   `RewardQuest`: Sends money via mail if the player's bag is full or configured.

*   **Called By `WorldSession.MailHandler`:**
    *   `HandleMailReturnToSender`: Processes logic for returning undeliverable mail.
    *   `HandleMailTakeItem`: Updates mail states or sends confirmations when a player takes an item.
    *   `HandleSendMailCallback`: Finalizes client-initiated mail sending, setting COD and money values.

*   **Called By `AuctionHouseMgr` and `WorldSession.AuctionHouseHandler`:**
    *   `SendAuctionExpiredMail`, `SendAuctionSuccessfulMail`, `SendAuctionCancelledToBidderMail`, `SendAuctionOutbiddedMail`: Trigger `MailDraft` creation for auction outcomes (won, lost, expired, cancelled) to notify bidders/sellers and transfer funds/items.
    *   `HandleAuctionRemoveItem`: Handles item removal from auctions, potentially triggering mail.

*   **Called By `MassMailMgr`:**
    *   `Update`: Creates `MailDraft` objects for mass mail campaigns.

*   **Calls Into `game_Mail_Mail`:**
    *   `MailDraft` getters are called by `game_Mail_Mail/CloneFrom` and `game_Mail_Mail/SendMailTo` to extract data for persistence. `SendMailTo` interacts with the underlying mail persistence layer.

## Data Model

`MailDraft` does not directly interact with database tables. It is an in-memory representation. Its data is persisted into the `mail` table (and `mail_items`) by `SendMailTo` via `game_Mail_Mail`. Fields map conceptually to:
*   `m_subject` -> `subject`
*   `m_bodyId` / `m_mailTemplateId` -> `itemTextId` / `mailTemplateId`
*   `m_money` -> `money`
*   `m_COD` -> `COD`
*   `m_items` -> Rows in `mail_items`

## Notable Implementation Details

1.  **No Copy Semantics:** Copy constructor and assignment operator are private with no definition, preventing accidental copying of `MailDraft` objects and potential double-free errors with `Item` pointers.
2.  **Item Ownership:** `MailDraft` holds raw `Item` pointers in `m_items`. `SendMailTo` transfers ownership to the persistence layer. Failure to send requires careful handling to avoid leaks.
3.  **Template Items:** `m_mailTemplateItemsNeed` flag indicates if items should be generated from a template. `prepareItems` (private) handles this generation.
4.  **Max Items Limit:** `MAX_MAIL_ITEMS` is defined as 1 in the header, possibly a placeholder or specific to certain mail types. Enforcement is in `Mail.cpp`.
5.  **Locale Support:** Template constructor accepts `LocaleConstant` for localized mail subjects/bodies.

## Member Reference

**`MailDraft`** (Default Constructor): Initializes a blank `MailDraft` with zero values for money, COD, subject, and items.

**`MailDraft`** (Template Constructor): Constructs a `MailDraft` using a specified mail template ID, setting the `m_mailTemplateItemsNeed` flag.

**`MailDraft`** (Subject/BodyId Constructor): Constructs a `MailDraft` with a specific subject string and a static text ID for the body.

**`MailDraft`** (Subject/Body Text Constructor): Constructs a `MailDraft` with both a subject and a full body text string.

**`GetMailTemplateId`**: Returns the ID of the mail template associated with this draft.

**`GetSubject`**: Returns the subject string of the mail.

**`GetBodyId`**: Returns the ID of the static text used for the mail body.

**`GetMoney`**: Returns the amount of money included in the mail.

**`GetCOD`**: Returns the Cash-on-Delivery fee for the mail.

**`SetMailTemplate`**: Assigns a mail template ID and sets the flag for whether items are needed from the template.

**`SetMoney`**: Sets the amount of money in the mail.

**`SetCOD`**: Sets the Cash-on-Delivery fee.

**`MailDraft#2`**: Private copy constructor declaration. Prevents implicit copying of `MailDraft` objects to avoid double-free issues with contained `Item` pointers.

**`MailDraft#3`**: Private assignment operator declaration. Prevents implicit assignment of `MailDraft` objects for the same safety reasons as the copy constructor.

**`operator=`**: Private assignment operator declaration. Prevents implicit assignment of `MailDraft` objects for the same safety reasons as the copy constructor.

---

<!-- machine-true, projected from graph.json -->

## Map — MailDraft

*Source:* Mail.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| MailDraft | ctor | — | ChatHandler.MiscCommands/HandleSendItemsCommand, ChatHandler.MiscCommands/HandleSendMailCommand, ChatHandler.MiscCommands/HandleSendMassItemsCommand, ChatHandler.MiscCommands/HandleSendMassMailCommand, ChatHandler.MiscCommands/HandleSendMassMoneyCommand, ChatHandler.MiscCommands/HandleSendMoneyCommand, MassMailMgr/Update, Player.Main/DeleteFromDB, WorldSession.MailHandler/HandleMailReturnToSender | — |
| MailDraft#2 | ctor | — | AuctionHouseMgr/SendAuctionExpiredMail, Player.Main/AutoUnequipItemFromSlot, Player.Main/_LoadInventory, WorldSession.AuctionHouseHandler/HandleAuctionRemoveItem, WorldSession.AuctionHouseHandler/SendAuctionCancelledToBidderMail, WorldSession.AuctionHouseHandler/SendAuctionOutbiddedMail, WorldSession.MailHandler/HandleMailTakeItem | — |
| GetMailTemplateId | method | — | game_Mail_Mail/CloneFrom, game_Mail_Mail/SendMailTo | — |
| GetSubject | method | — | game_Mail_Mail/CloneFrom, game_Mail_Mail/SendMailTo | — |
| GetBodyId | method | — | game_Mail_Mail/CloneFrom, game_Mail_Mail/SendMailTo | — |
| GetMoney | method | — | game_Mail_Mail/CloneFrom, game_Mail_Mail/SendMailTo | — |
| GetCOD | method | — | game_Mail_Mail/CloneFrom, game_Mail_Mail/SendMailTo | — |
| SetMailTemplate | method | — | Player.Main/DeleteFromDB, WorldSession.MailHandler/HandleMailReturnToSender | — |
| SetMoney | method | — | AuctionHouseMgr/SendAuctionSuccessfulMail, ChatHandler.MiscCommands/HandleSendMoneyHelper, Player.Main/DeleteFromDB, Player.Main/RewardQuest, WorldSession.AuctionHouseHandler/SendAuctionCancelledToBidderMail, WorldSession.AuctionHouseHandler/SendAuctionOutbiddedMail, WorldSession.MailHandler/HandleMailReturnToSender, WorldSession.MailHandler/HandleMailTakeItem, WorldSession.MailHandler/HandleSendMailCallback | — |
| SetCOD | method | — | WorldSession.MailHandler/HandleSendMailCallback | — |
| MailDraft#3 | decl | — | — | — |
| operator= | decl | — | — | — |
