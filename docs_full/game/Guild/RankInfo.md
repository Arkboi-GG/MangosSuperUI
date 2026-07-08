# RankInfo

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# RankInfo

**Purpose & Responsibilities**

`RankInfo` is a lightweight data structure (`struct`) defined in `Guild.h` that represents a single guild rank within the WoWVMaNGOS server. It encapsulates two pieces of information required to define a rank's identity and permissions:
1.  **Name**: The human-readable string displayed to players (e.g., "Officer", "Initiate").
2.  **Rights**: A bitmask (`uint32`) defining the specific permissions associated with this rank, such as the ability to promote/demote members, speak in officer chat, or modify guild information.

This struct serves as the value type stored in the `Guild::m_Ranks` vector (typedef'd as `RankList`). It does not contain logic for validation, persistence, or network serialization; those responsibilities belong to the `Guild` class methods that manipulate `RankInfo` instances.

**Member-by-Member Behavior**

The unit contains a single member: the constructor.

*   **Constructor (`RankInfo`)**: Initializes the `Name` and `Rights` members. It takes a constant reference to a `std::string` for the name and a `uint32` for the rights bitmask. The initialization is performed via the member initializer list, ensuring efficient construction without default-initializing the members before assignment.

**Cross-Unit Boundaries**

*   **Called by `game_Guild_Guild/AddRank`**: The `Guild` class method `AddRank` (defined in the `Guild` unit, likely `Guild.cpp`) constructs a new `RankInfo` instance when a guild master creates a new rank or modifies an existing one. The `Guild` unit passes the desired rank name and the calculated rights bitmask to this constructor. The resulting `RankInfo` object is then appended to the `Guild`'s internal `m_Ranks` list.

**Data Model**

This unit does not directly interact with database tables. It is a transient in-memory representation. However, the data it holds corresponds to columns in the `guild_rank` table (specifically `rank` and `rights`), which are loaded by `Guild::LoadRanksFromDB` and saved by `Guild::SaveToDB`. No SQL queries or schema definitions are present in this unit itself.

**Notable Implementation Details**

*   **Bitmask Rights**: The `Rights` field uses a bitmask system defined by the `GuildRankRights` enum in `Guild.h`. For example, `GR_RIGHT_GCHATLISTEN` is `0x00000041`. The `Guild` class uses bitwise operations (e.g., `&`, `!= GR_RIGHT_EMPTY`) to check if a specific right is granted. `RankInfo` itself is agnostic to these bit patterns; it simply stores the integer value.
*   **No Validation**: The constructor does not validate the length of `_name` or the validity of `_rights`. Length constraints (e.g., `GUILD_RANK_MAX_LENGTH = 15`) are enforced by the caller (`Guild::AddRank` or `Guild::SetRankName`) before constructing the `RankInfo`.
*   **Aggregate Structure**: As a `struct` with a public constructor and public members, `RankInfo` acts as a simple aggregate. It is designed for easy access and modification by the owning `Guild` object.

## Member Reference

**RankInfo**
Constructs a `RankInfo` instance with the specified `_name` (string) and `_rights` (uint32 bitmask). Initializes the `Name` and `Rights` members directly via the initializer list. Called exclusively by `Guild::AddRank` (in the `Guild` unit) when creating or updating guild ranks.

---

<!-- machine-true, projected from graph.json -->

## Map — RankInfo

*Source:* Guild.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| RankInfo | ctor | — | game_Guild_Guild/AddRank | — |
