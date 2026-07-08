# AccountPersistentData

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`AccountPersistentData` is a lightweight data structure within the `wowvmangos` codebase designed to track transient, in-memory behavioral metrics for a specific user account. It resides in `AccountMgr.h` and serves two primary anti-abuse functions:

1.  **Whisper Flood Protection:** It tracks the volume and recency of private messages (whispers) sent to or received by the account to detect spamming behavior.
2.  **Mail Flood Protection:** It records recent mail-sending activity to prevent mass-mailing abuse.

Crucially, `AccountPersistentData` is **not** responsible for persistence itself. As its name implies, it holds *persistent* data relative to the server session (surviving character logouts), but this data is stored entirely in memory within the `AccountMgr` singleton. It does not interact with the database directly; all database interactions are handled by the owning `AccountMgr` unit. The class is tightly coupled with `AccountMgr`, which is declared as a `friend`, allowing `AccountMgr` to manage the lifecycle and storage of these objects.

## Member-by-Member Behavior

The unit contains a single documented member in the provided MAP, though the header reveals additional methods that support the core functionality.

### **CountDifferentWhispTargets**

*   **Kind:** Method (Inline)
*   **Signature:** `uint32 CountDifferentWhispTargets() const`
*   **Behavior:** Returns the number of unique players (identified by their low GUID) to whom the account has recently whispered. It achieves this by returning the size of the internal `std::map<uint32, WhisperData>` named `m_whisperTargets`.
*   **Context:** This metric is likely used by external systems (such as chat handlers or anti-spam modules) to determine if an account is engaging in broadcast-style whispering (sending the same message to many different targets), which is a common spam tactic.

*(Note: While not in the MAP, the following members are part of this class definition and provide context for how `CountDifferentWhispTargets` fits into the larger system.)*

*   **WhisperedBy:** Records that the account received a whisper from a specific `MasterPlayer`. It updates the `WhisperData` for that sender, tracking the timestamp of the first whisper, a calculated "score," and the total count.
*   **CanWhisper:** Determines if the account is allowed to send a whisper to a specific `MasterPlayer`. This likely checks flood limits based on the data stored in `m_whisperTargets`.
*   **GetWhisperScore:** Retrieves the calculated spam score for a specific interaction between the account and another player.
*   **JustMailed:** Records that the account sent a mail to a specific target account ID. Updates the `m_mailsSent` map.
*   **CanMail:** Checks if the account is allowed to send mail to a specific target, presumably by checking the recency of entries in `m_mailsSent`.

## Cross-Unit Boundaries

The `AccountPersistentData` class operates almost entirely in isolation regarding direct calls, relying on its container `AccountMgr` for integration.

*   **Called By:** The MAP indicates no external units call `CountDifferentWhispTargets`. However, logically, this method would be called by anti-spam logic residing in other units (e.g., `ChatHandler` or a dedicated `AntiSpam` module) after retrieving the `AccountPersistentData` object from `AccountMgr`.
*   **Calls Out:** The member `CountDifferentWhispTargets` makes no calls to other units. It is a simple accessor.
*   **Friendship with AccountMgr:** The `AccountMgr` class is declared as a `friend`. This allows `AccountMgr` to access the protected members of `AccountPersistentData` (such as `m_whisperTargets` and `m_mailsSent`) if necessary, although the current interface provides public getters/setters for most operations. `AccountMgr` stores instances of `AccountPersistentData` in its `m_accountPersistentData` map, keyed by account ID.

## Data Model

This unit does **not** interact with any database tables directly. All data (`m_whisperTargets`, `m_mailsSent`, etc.) is held in volatile memory. The `AccountMgr` unit handles any persistence of account-related data to the database, but `AccountPersistentData` itself is purely an in-memory cache for rate-limiting logic.

## Notable Implementation Details

1.  **In-Memory Volatility:** Because `AccountPersistentData` is stored in `AccountMgr`'s `std::map<uint32, AccountPersistentData> m_accountPersistentData`, all whisper and mail flood counters are lost upon server restart. This means flood protection is only effective during a single server uptime.
2.  **GUID-Based Tracking:** Whisper tracking uses `uint32` low GUIDs (`MasterPlayer*` is passed, but the key is `lowguid`). This assumes that the low GUID is sufficient to uniquely identify the target player for the duration of the server session. If a player logs out and back in, they may receive a new GUID, potentially resetting their whisper counter in the abuser's `m_whisperTargets` map.
3.  **Score Calculation:** The `WhisperData` struct includes a `score` field, initialized to 0. The logic for updating this score is not visible in the header (likely implemented in the corresponding `.cpp` file, which is not provided in the source snippet but is implied by the `WhisperedBy` method). The score likely decays over time or increases with frequency, serving as a heuristic for malicious intent.
4.  **Thread Safety:** The `AccountPersistentData` object itself is not thread-safe. Access to it is mediated by `AccountMgr`, which uses a `std::shared_timed_mutex m_accountPersistentDataMutex` to protect the map containing these objects. Individual methods like `CountDifferentWhispTargets` are marked `const`, implying they are safe to read if the underlying mutex is held.

## Member Reference

**CountDifferentWhispTargets**
Returns the number of unique players (by low GUID) present in the `m_whisperTargets` map. This value represents the count of distinct recipients the account has whispered to recently, used for detecting broadcast spam. It is an inline method that simply returns `m_whisperTargets.size()`.

---

<!-- machine-true, projected from graph.json -->

## Map — AccountPersistentData

*Source:* AccountMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| CountDifferentWhispTargets | method | — | — | — |
