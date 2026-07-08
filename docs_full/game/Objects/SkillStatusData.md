# SkillStatusData

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# SkillStatusData

**Purpose & Responsibilities**

`SkillStatusData` is a lightweight aggregate struct defined in `Player.h` within the `wowvmangos` codebase. It serves as the value type for the `SkillStatusMap` (`std::unordered_map<uint32, SkillStatusData>`), which tracks the synchronization state of player skills between the server and the client.

Its primary responsibility is to hold two pieces of metadata for a specific skill ID:
1.  **`pos`**: The position index of the skill within the client's skill list or a relevant internal ordering.
2.  **`uState`**: An enumeration of type `SkillUpdateState` indicating whether the skill is new, changed, unchanged, or deleted relative to the client's current knowledge.

This structure enables the `Player` class to efficiently batch and transmit only the necessary skill updates during login or dynamic skill changes, rather than resending the entire skill list.

## Member-by-Member Behavior

The struct contains only a constructor and two public data members. It has no methods beyond the constructor.

### Constructor: `SkillStatusData`

**Signature:**
```cpp
SkillStatusData(uint8 _pos, SkillUpdateState _uState) : pos(_pos), uState(_uState) {}
```

**Behavior:**
The constructor initializes the two member variables using an initializer list.
*   `_pos` is assigned to the `pos` member.
*   `_uState` is assigned to the `uState` member.

There is no validation logic, default arguments, or side effects in the constructor. It is a straightforward aggregation initializer.

### Data Members

*   **`uint8 pos`**: Stores the position index. This is likely used by the client to place the skill in the correct slot in the UI or by the server to track insertion order if required by the protocol.
*   **`SkillUpdateState uState`**: Stores the update state. The `SkillUpdateState` enum (defined earlier in `Player.h`) has the following values:
    *   `SKILL_UNCHANGED` (0): No update needed.
    *   `SKILL_CHANGED` (1): Existing skill values have changed.
    *   `SKILL_NEW` (2): A new skill has been learned.
    *   `SKILL_DELETED` (3): A skill has been removed.

## Cross-Unit Boundaries

As indicated in the MAP, `SkillStatusData` is instantiated exclusively by members of the `Player` class (specifically the `Player.Main` partial). It does not call into any other units. Its lifecycle is managed entirely within the scope of the `Player` object's skill management logic.

*   **Called By:**
    *   `Player.Main/LoadSkillsFromFields`: Likely constructs `SkillStatusData` entries when initializing skills from persistent storage fields.
    *   `Player.Main/SetSkill`: Constructs or updates a `SkillStatusData` entry when a skill value is explicitly set.
    *   `Player.Main/_LoadSkills`: Constructs entries when loading skills from the database query results.

These callers create instances of `SkillStatusData` to populate the `m_skillStatusMap` member of the `Player` class. The map is then iterated over to generate network packets for the client.

## Data Model

`SkillStatusData` itself does not interact directly with database tables. It is an in-memory representation. However, the data it holds is derived from the `character_skills` table (implied by the `_LoadSkills` and `LoadSkillsFromFields` callers in `Player.Main`). The struct abstracts the raw database rows into a stateful object suitable for network synchronization.

## Notable Implementation Details

1.  **Aggregate Structure**: `SkillStatusData` is a Plain Old Data (POD)-like struct. It has no virtual functions, no complex initialization, and no destructor. This makes it cheap to copy and store in standard containers like `std::unordered_map`.
2.  **No Encapsulation**: The members `pos` and `uState` are public. This allows direct access by the `Player` class without getter/setter overhead, which is appropriate for a simple data carrier.
3.  **Dependency on `SkillUpdateState`**: The behavior of the system relying on this struct depends heavily on the correct usage of the `SkillUpdateState` enum. Incorrectly marking a skill as `SKILL_NEW` when it is `SKILL_CHANGED` could cause client-side desynchronization.
4.  **Usage in `SkillStatusMap`**: The struct is designed to be the value in `SkillStatusMap`. The key of this map is the `skill_id` (uint32). This allows O(1) lookup of the state of any skill by its ID.

## Member Reference

**SkillStatusData**
Constructor that initializes the `pos` and `uState` members. Takes a `uint8` for position and a `SkillUpdateState` enum for the update state. Used by `Player.Main` methods to create entries in the skill status map.

---

<!-- machine-true, projected from graph.json -->

## Map — SkillStatusData

*Source:* Player.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| SkillStatusData | ctor | — | Player.Main/LoadSkillsFromFields, Player.Main/SetSkill, Player.Main/_LoadSkills | — |
