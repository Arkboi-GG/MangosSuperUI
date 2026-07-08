# PetitionSignature

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`PetitionSignature` is a lightweight data structure representing a single player's signature on a guild charter. It stores the signer's `ObjectGuid` and account ID, along with a raw pointer to the parent `Petition`. The class serves as a passive data holder; it contains no business logic, validation, or thread-safety mechanisms. Its sole responsibilities are initializing these identifiers and providing accessors for them. Lifecycle management and persistence are handled externally by `Petition` and `GuildMgr`.

## Member-by-Member Behavior

### Construction
**`PetitionSignature`**
The constructor initializes the three private members: `m_petition` (pointer to the parent `Petition`), `m_playerGuid` (the signer's unique identifier), and `m_playerAccount` (the signer's account ID). This constructor is called by `GuildMgr::LoadPetitions` during server startup to reconstruct signatures from database records.

### Accessors
**`GetSignatureGuid`**
Returns a constant reference to `m_playerGuid`. This method is used by `GuildMgr` to identify signers when building network packets (`BuildSignatureData`), deleting signatures (`DeleteSignatureByPlayer`), or looking up signatures by player (`GetSignatureForPlayerGuid`). It is also accessed by the `game_Guild_Guild/Create` handler during guild formation.

**`GetSignatureAccountId`**
Returns the `m_playerAccount` value. This allows `GuildMgr::GetSignatureForAccount` to verify that a specific account has not already signed a petition, enforcing the "one signature per account" rule.

## Cross-Unit Boundaries

`PetitionSignature` has no outgoing calls. It is exclusively consumed by `GuildMgr` and indirectly by `Petition` (which owns the list of signatures).

*   **`GuildMgr::LoadPetitions`**: Instantiates `PetitionSignature` objects using the constructor, passing data retrieved from the database.
*   **`GuildMgr::BuildSignatureData`**: Calls `GetSignatureGuid` to serialize signer identities into network packets sent to clients inspecting the charter.
*   **`GuildMgr::DeleteSignatureByPlayer`** & **`GuildMgr::GetSignatureForPlayerGuid`**: Call `GetSignatureGuid` to locate specific signatures for removal or retrieval.
*   **`GuildMgr::GetSignatureForAccount`**: Calls `GetSignatureAccountId` to check for duplicate signatures from the same account.
*   **`game_Guild_Guild/Create`**: Calls `GetSignatureGuid` to validate signers when finalizing guild creation.

## Data Model

The `PetitionSignature` class does not contain SQL queries or direct database interaction logic. Persistence is handled by `GuildMgr` and `Petition`. No database tables are directly referenced in this unit's source code.

## Notable Implementation Details

1.  **Raw Pointer Ownership**: The class holds a raw `Petition* m_petition`. It assumes the `Petition` object outlives the `PetitionSignature`. The `Petition` class owns the `PetitionSignatureList` and is responsible for deleting signatures when the petition is destroyed.
2.  **No Validation**: The constructor accepts any GUID and Account ID. All eligibility checks (level, guild status, duplicate accounts) are performed by `GuildMgr` or `Petition` before instantiation.
3.  **Thread Safety**: The class is not thread-safe. Concurrent access is managed by mutexes in `GuildMgr` (`m_petitionsMutex`) and `Petition`.

## Member Reference

**`PetitionSignature`**
Constructor initializing the signature with a parent `Petition` pointer, signer `ObjectGuid`, and signer account ID. Called by `GuildMgr::LoadPetitions`.

**`GetSignatureGuid`**
Returns the signer's `ObjectGuid`. Called by `GuildMgr::BuildSignatureData`, `GuildMgr::DeleteSignatureByPlayer`, `GuildMgr::GetSignatureForPlayerGuid`, and `game_Guild_Guild/Create`.

**`GetSignatureAccountId`**
Returns the signer's account ID. Called by `GuildMgr::GetSignatureForAccount` to prevent duplicate signatures from the same account.

---

<!-- machine-true, projected from graph.json -->

## Map — PetitionSignature

*Source:* GuildMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| PetitionSignature | ctor | — | GuildMgr/LoadPetitions | — |
| GetSignatureGuid | method | — | game_Guild_Guild/Create, GuildMgr/BuildSignatureData, GuildMgr/DeleteSignatureByPlayer, GuildMgr/GetSignatureForPlayerGuid | — |
| GetSignatureAccountId | method | — | GuildMgr/GetSignatureForAccount | — |
