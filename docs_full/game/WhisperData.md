# WhisperData

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# WhisperData

**Purpose & Responsibilities**

`WhisperData` is a lightweight data structure defined within `AccountPersistentData` in `AccountMgr.h`. Its sole responsibility is to track the history and volume of private chat messages (whispers) sent by a specific player character to the account owner associated with the `AccountPersistentData` instance. It serves as the foundational metric for anti-spam and flood-protection mechanisms regarding private messaging.

The structure aggregates three key pieces of information for a single sender-target relationship:
1.  **Timestamp:** When the first whisper in the current tracking window occurred.
2.  **Score:** An accumulated penalty or weight value associated with the whispers.
3.  **Count:** The total number of whispers sent.

Because `WhisperData` is a plain old data structure (POD-like) with no methods other than its constructor, it acts purely as a container for state managed by the surrounding `AccountPersistentData` class.

## Member-by-Member Behavior

### **WhisperData** (Constructor)

The default constructor initializes the tracking state for a new whispering relationship.

*   **Initialization Logic:**
    *   `first_whisp`: Set to the current system time via `time(nullptr)`. This establishes the baseline timestamp for the current session or tracking period.
    *   `score`: Initialized to `0`. This indicates no penalty or weight has been assigned yet.
    *   `whispers_count`: Initialized to `0`. This indicates no messages have been recorded yet.

This initialization ensures that whenever a new entry is created in the `m_whisperTargets` map (which maps `uint32` low GUIDs to `WhisperData`), the counters start from a clean slate, anchored to the moment the first interaction is detected.

## Cross-Unit Boundaries

As a nested struct with no methods, `WhisperData` does not actively call into other units. However, it is tightly coupled with the following units in the `AccountMgr.h` header:

*   **AccountPersistentData:** This is the owning class. `AccountPersistentData` maintains a `std::map<uint32, WhisperData>` named `m_whisperTargets`. Methods such as `WhisperedBy`, `CountWhispersTo`, `CanWhisper`, and `GetWhisperScore` in `AccountPersistentData` read from and write to instances of `WhisperData`.
*   **MasterPlayer:** The `AccountPersistentData` methods that manipulate `WhisperData` take `MasterPlayer*` arguments. This indicates that the identity of the whisperer (the key in the map) is derived from the `MasterPlayer` object's low GUID.

## Data Model

`WhisperData` does not interact directly with any database tables. It is an in-memory runtime structure. The data it holds is transient and exists only for the duration of the server process or until the `AccountPersistentData` instance is cleared/reloaded. There are no SQL queries, inserts, updates, or deletes associated with `WhisperData` itself.

## Notable Implementation Details

*   **Time-Based Tracking:** The use of `time(nullptr)` suggests that the flood protection logic likely relies on time windows (e.g., "X whispers within Y seconds"). The `first_whisp` field allows the system to calculate the duration of the current whisper burst.
*   **Score vs. Count:** The distinction between `score` and `whispers_count` implies that not all whispers may carry equal weight. For example, a whisper containing a link or a specific keyword might increment the `score` more than a simple text message, or the score might decay over time while the count remains absolute. The logic for updating these fields resides in `AccountPersistentData::WhisperedBy` and related methods, not in `WhisperData` itself.
*   **Thread Safety:** While `WhisperData` itself is not thread-safe, the `AccountPersistentData` class uses a `std::shared_timed_mutex` (`m_accountPersistentDataMutex`) to protect access to `m_accountPersistentData`, which contains the `m_whisperTargets` map. Therefore, concurrent access to `WhisperData` instances is mediated by the mutex in the parent class.

## Member Reference

**WhisperData**: Default constructor that initializes `first_whisp` to the current time, `score` to 0, and `whispers_count` to 0.

---

<!-- machine-true, projected from graph.json -->

## Map — WhisperData

*Source:* AccountMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| WhisperData | ctor | — | — | — |
