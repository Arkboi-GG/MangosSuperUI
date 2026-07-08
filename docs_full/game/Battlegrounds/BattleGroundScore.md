# BattleGroundScore

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# BattleGroundScore

**Purpose & Responsibilities**

`BattleGroundScore` is a lightweight data structure defined in `BattleGround.h` that aggregates per-player statistics for a single session within a battleground instance. It serves as the value type in the `BattleGround::m_playerScores` map, allowing the core `BattleGround` system to track and report individual performance metrics such as kills, deaths, and honor rewards.

The class is intentionally minimal, containing only four public integer members and default constructor/destructor logic. It does not contain any business logic, validation, or serialization code; those responsibilities reside in the owning `BattleGround` class and its subclasses. The primary responsibility of `BattleGroundScore` is to hold state that is updated by `BattleGround::UpdatePlayerScore` (and its overrides in specific battleground implementations) and eventually serialized into the `m_finalScore` packet sent to clients at the end of the match.

**Member-by-Member Behavior**

The class consists of two lifecycle methods and four data fields.

*   **Constructor (`BattleGroundScore`)**: Initializes all statistical fields to zero. This ensures that when a new score object is allocated for a player entering a battleground, their stats start from a clean slate.
*   **Destructor (`~BattleGroundScore`)**: A virtual destructor. Although the class has no dynamic memory management or virtual base classes requiring complex cleanup, it is marked `virtual` because `BattleGroundScore` objects are stored in a map (`std::map<ObjectGuid, BattleGroundScore*>`) within the `BattleGround` class. When the `BattleGround` instance cleans up these scores, it deletes them through base pointers. The comment in the source explicitly notes: *"virtual destructor is used when deleting score from scores map"*.
*   **Data Fields**:
    *   `killingBlows`: Tracks the number of times the player delivered the final blow to an enemy player.
    *   `deaths`: Tracks the number of times the player died during the battleground session.
    *   `honorableKills`: Tracks the total number of honorable kills, which may differ from killing blows depending on game rules (e.g., assisted kills or specific battleground mechanics).
    *   `bonusHonor`: Accumulates extra honor points awarded based on performance or victory conditions.

**Cross-Unit Boundaries**

As a pure data structure, `BattleGroundScore` has no outgoing calls to other units. However, it is heavily integrated into the `BattleGround` ecosystem:

*   **Called by `BattleGround`**: The `BattleGround` class (defined in the same header) manages the lifecycle of `BattleGroundScore` objects. Specifically:
    *   `BattleGround::m_playerScores` is a `std::map<ObjectGuid, BattleGroundScore*>`.
    *   Methods like `BattleGround::UpdatePlayerScore` (virtual, implemented in subclasses) modify the fields of the `BattleGroundScore` instance associated with a player.
    *   `BattleGround::GetFinalScorePacket` likely iterates over `m_playerScores` to serialize this data into a `WorldPacket` for transmission to clients.
*   **Ownership**: The `BattleGround` class is responsible for allocating `new BattleGroundScore()` instances when players join and deleting them when players leave or the battleground ends.

**Data Model**

`BattleGroundScore` does not interact directly with any database tables. It is an in-memory transient object representing the current state of a player's performance in an active battleground instance. No SQL queries or table references are present in this unit.

**Notable Implementation Details**

*   **Virtual Destructor**: The presence of a virtual destructor in a class with no virtual methods (other than the destructor itself) is a specific design choice to support polymorphic deletion via base pointers. While `BattleGroundScore` is not currently inherited from, the virtual destructor suggests that the architecture anticipates potential extensions or simply adheres to a strict rule for heap-allocated objects managed through base pointers in maps.
*   **Public Data Members**: All statistical fields (`killingBlows`, `deaths`, etc.) are public. This allows direct access and modification by `BattleGround` subclasses without needing getter/setter methods, reducing boilerplate code. This is acceptable given that the `BattleGround` class is the sole owner and manager of these objects.
*   **Zero Initialization**: The constructor explicitly initializes all fields to zero. This is critical because `BattleGroundScore` objects are dynamically allocated. Without this initialization, the fields would contain garbage values, leading to incorrect score reporting.

## Member Reference

**BattleGroundScore**
Default constructor. Initializes `killingBlows`, `deaths`, `honorableKills`, and `bonusHonor` to 0. Ensures clean state for new battleground sessions.

**~BattleGroundScore**
Virtual destructor. Required because `BattleGroundScore` objects are deleted through base pointers in the `BattleGround::m_playerScores` map. Performs no custom cleanup logic.

---

<!-- machine-true, projected from graph.json -->

## Map — BattleGroundScore

*Source:* BattleGround.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| BattleGroundScore | ctor | — | — | — |
| ~BattleGroundScore | dtor | — | — | — |
