# BattleGroundAVScore

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# BattleGroundAVScore

**Purpose & Responsibilities**

`BattleGroundAVScore` is a lightweight data structure that extends the base `BattleGroundScore` class to track specific performance metrics for players participating in the Arathi Basin (AV) battleground. It serves as the per-player scoreboard record, accumulating counts for various objectives completed during the match, such as capturing or defending nodes, completing secondary quests, and defeating specific NPCs. It contains no logic of its own; it is purely a container for integer counters initialized by its constructor.

**Member-by-Member Behavior**

The class consists of a default constructor and a destructor, alongside seven public integer members that store score data.

*   **Constructor (`BattleGroundAVScore`)**: Initializes all seven score counters to zero. This ensures that a fresh score object starts with a clean slate when a player joins the battleground.
*   **Destructor (`~BattleGroundAVScore`)**: An empty override of the base class destructor. It performs no cleanup operations, as the class holds no dynamic memory or resources requiring explicit release.

**Cross-Unit Boundaries**

*   **Called by `BattleGroundAV::AddPlayer`**: The `BattleGroundAV` unit (specifically the `AddPlayer` method in `BattleGroundAV.cpp`) instantiates a `BattleGroundAVScore` object when a player enters the battleground. This object is then associated with the player's session to track their individual contributions throughout the match.
*   **No Outgoing Calls**: This unit does not call into any other units. It is a passive data holder.

**Data Model**

This unit does not interact directly with any database tables. It operates entirely in memory during the lifespan of a battleground instance.

**Notable Implementation Details**

*   **Inheritance**: It inherits from `BattleGroundScore`, implying that the base class likely handles common scoring infrastructure (such as linking the score to a player GUID or handling basic honor calculations), while `BattleGroundAVScore` provides the specific fields required for Arathi Basin's unique objective-based scoring system.
*   **Field Semantics**: The member variables correspond to distinct gameplay actions:
    *   `graveyardsAssaulted` / `graveyardsDefended`: Tracks participation in capturing or holding graveyard nodes.
    *   `towersAssaulted` / `towersDefended`: Tracks participation in capturing or holding tower nodes.
    *   `secondaryObjectives`: Likely tracks completion of side quests (e.g., armor scrap quests, taming mounts).
    *   `lieutnantCount`: Tracks kills of Lieutenant-tier NPCs.
    *   `secondaryNPC`: Tracks kills of other secondary NPCs (e.g., Captains, Commanders, or Bosses, depending on how `BattleGroundAV::UpdatePlayerScore` categorizes them).

## Member Reference

**BattleGroundAVScore**
Default constructor that initializes all seven public integer members (`graveyardsAssaulted`, `graveyardsDefended`, `towersAssaulted`, `towersDefended`, `secondaryObjectives`, `lieutnantCount`, `secondaryNPC`) to zero. Called by `BattleGroundAV::AddPlayer` when a player joins the battleground.

**~BattleGroundAVScore**
Empty destructor override. Performs no cleanup.

---

<!-- machine-true, projected from graph.json -->

## Map — BattleGroundAVScore

*Source:* BattleGroundAV.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| BattleGroundAVScore | ctor | — | BattleGroundAV/AddPlayer | — |
| ~BattleGroundAVScore | dtor | — | — | — |
