# MailReceiver

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# MailReceiver

**Purpose & Responsibilities**

`MailReceiver` is a lightweight data holder within the MaNGOS mail system (`Mail.h`) that encapsulates the identity of a mail recipient. Its primary responsibility is to bind a specific `ObjectGuid` (the unique identifier of the receiving player) to an optional, transient pointer to the live `Player` object representing that recipient.

This class serves as a bridge between persistent mail data (which relies on GUIDs) and runtime game logic (which often requires direct access to the `Player` instance for validation, inventory checks, or immediate feedback). By separating the GUID from the pointer, the system can construct mail recipients for players who are currently offline (where the pointer is null) or online (where the pointer is valid), ensuring that mail operations can proceed regardless of the recipient's login status.

**Member-by-Member Behavior**

The `MailReceiver` class contains three constructors and two accessor methods. All members operate on the private state variables `m_receiver` (a `Player*`) and `m_receiver_guid` (an `ObjectGuid`).

### Initialization

*   **`MailReceiver(ObjectGuid receiver_guid)`**: This explicit constructor initializes a `MailReceiver` with only a GUID. It sets `m_receiver_guid` to the provided value and explicitly sets `m_receiver` to `nullptr`. This is the standard way to create a recipient object when the player is known by ID but not necessarily logged in or accessible via a pointer at the moment of construction.
*   **`MailReceiver(Player* receiver)`**: This constructor takes a pointer to a live `Player` object. It initializes `m_receiver` with this pointer. Crucially, it derives the `m_receiver_guid` from the `Player` object itself (implicitly, via the `Player`'s internal GUID mechanism, though the specific derivation logic is handled by the `Player` class interface). This ensures consistency between the pointer and the GUID.
*   **`MailReceiver(Player* receiver, ObjectGuid receiver_guid)`**: This constructor allows explicit specification of both the player pointer and the GUID. It initializes both `m_receiver` and `m_receiver_guid` with the provided arguments. This form is likely used in scenarios where the GUID might differ from the player's current primary GUID (e.g., during certain teleportation or instance transitions) or for safety in complex object lifecycles, although typically the GUID should match the player's identity.

### Accessors

*   **`GetPlayer()`**: Returns the `Player*` stored in `m_receiver`. Callers must check for `nullptr` before dereferencing, as this pointer may be invalid if the recipient was constructed via the GUID-only constructor or if the player has logged out since the object was created.
*   **`GetPlayerGuid()`**: Returns a constant reference to the `ObjectGuid` stored in `m_receiver_guid`. This is the stable identifier for the recipient, suitable for database queries, logging, or passing to systems that do not require a live object pointer.

**Cross-Unit Boundaries**

`MailReceiver` acts primarily as a data carrier passed into other subsystems. It does not initiate calls to other units; rather, it provides data *to* them.

*   **Called by `game_Mail_Mail/SendMailTo`**: The core mail sending logic resides in the `Mail` class (specifically the `SendMailTo` method in `Mail.cpp`, referred to here as `game_Mail_Mail/SendMailTo`). When `SendMailTo` is invoked, it accepts a `MailReceiver` const reference. Inside `SendMailTo`, the system calls `GetPlayer()` and `GetPlayerGuid()` to determine how to process the mail.
    *   If `GetPlayer()` returns a valid pointer, the mail system can perform immediate validations (e.g., checking if the mailbox is full) or apply effects directly to the online player.
    *   If `GetPlayer()` returns `nullptr`, the system relies on `GetPlayerGuid()` to persist the mail to the database for later retrieval when the player logs in.
    *   This boundary defines the contract: `MailReceiver` provides the "who," and `Mail::SendMailTo` handles the "how."

*   **Called by `AuctionHouseMgr/LoadAuctions` and `ObjectMgr/RestoreDeletedItems`**: These units instantiate `MailReceiver` objects during server startup or recovery processes.
    *   `AuctionHouseMgr/LoadAuctions` loads auction data from the database. When an auction ends or expires, it may need to send mail to the winner or bidder. It constructs a `MailReceiver` using the GUID loaded from the database row. Since these processes occur during initialization or background tasks, the players are almost certainly offline, so the GUID-only constructor is used.
    *   `ObjectMgr/RestoreDeletedItems` handles the restoration of items that were deleted due to bugs or crashes. If these items need to be mailed back to their owners, the manager constructs a `MailReceiver` using the owner's GUID stored in the item's metadata. Again, this is a GUID-only instantiation because the context is administrative recovery, not real-time gameplay.

**Data Model**

The `MailReceiver` class itself does not directly interact with database tables. It holds no SQL query logic. However, the `ObjectGuid` it stores corresponds to the `receiver` column in the `mail` table (and potentially `mail_items` indirectly via the mail message ID). The class is purely an in-memory representation of the recipient identity, decoupled from the persistence layer.

**Notable Implementation Details**

1.  **Explicit Constructor**: The single-argument constructor `MailReceiver(ObjectGuid)` is marked `explicit`. This prevents accidental implicit conversions from `ObjectGuid` to `MailReceiver`, which could lead to subtle bugs where a GUID is passed to a function expecting a full `MailReceiver` object, potentially bypassing intended validation or initialization steps.
2.  **Null Pointer Safety**: The class design acknowledges that the `Player*` may be null. There is no assertion or exception thrown if `GetPlayer()` returns null. The burden of checking for null lies entirely with the caller (e.g., `Mail::SendMailTo`). This is a critical design choice for a server environment where players log in and out frequently; mail objects may outlive the player's session.
3.  **Const Correctness**: Both accessors return `const` references or pointers where appropriate (`GetPlayerGuid` returns `const&`, `GetPlayer` returns `Player*` but the method itself is `const`). This ensures that `MailReceiver` instances can be passed by const reference to functions like `SendMailTo` without allowing modification of the recipient's identity after creation.
4.  **No Validation Logic**: The class does not validate whether the `Player*` and `ObjectGuid` match. If a user constructs a `MailReceiver` with mismatched data (via the two-argument constructor), the class will store it as-is. Consistency is assumed to be enforced by the caller.

## Member Reference

**MailReceiver** (ctor): Constructs a `MailReceiver` with a given `ObjectGuid`, setting the player pointer to `nullptr`. Used when the recipient is identified by ID but not currently online or accessible.

**MailReceiver** (ctor): Constructs a `MailReceiver` from a `Player*` pointer, deriving the GUID from the player. Used when the recipient is known to be online and the pointer is available.

**MailReceiver** (ctor): Constructs a `MailReceiver` with both a `Player*` and an explicit `ObjectGuid`. Allows overriding the default GUID derivation, though typically the GUID should match the player.

**GetPlayer**: Returns the `Player*` pointer stored in the object. May return `nullptr` if the recipient was constructed via GUID-only or if the player has logged out. Callers must check for null.

**GetPlayerGuid**: Returns a constant reference to the `ObjectGuid` of the recipient. This is the stable identifier used for database lookups and logging, regardless of the player's online status.

---

<!-- machine-true, projected from graph.json -->

## Map — MailReceiver

*Source:* Mail.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| MailReceiver | ctor | — | AuctionHouseMgr/LoadAuctions, ObjectMgr/RestoreDeletedItems | — |
| GetPlayer | method | — | game_Mail_Mail/SendMailTo | — |
| GetPlayerGuid | method | — | game_Mail_Mail/SendMailTo | — |
