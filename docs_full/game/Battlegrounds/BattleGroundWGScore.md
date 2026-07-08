# BattleGroundWGScore

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`BattleGroundWGScore` is a lightweight data structure within the `wowvmangos` codebase that tracks individual player performance metrics specific to the Warsong Gulch (WSG) battleground. It extends the base `BattleGroundScore` class to include two counters: `flagCaptures` and `flagReturns`. This class is instantiated for each player participating in a WSG match to record how many times they successfully captured the enemy flag and how many times they returned their own team's flag to its base. It serves purely as a container for these statistics and contains no logic of its own.

## Member-by-Member Behavior

The class consists of a constructor, a destructor, and two public data members.

### Construction and Destruction

**`BattleGroundWGScore()`**  
The default constructor initializes the object by setting both `flagCaptures` and `flagReturns` to zero. It inherits from `BattleGroundScore`, though no additional initialization is required from the base class in this context.

**`~BattleGroundWGScore()`**  
The virtual destructor performs no custom cleanup. It exists primarily to satisfy polymorphic deletion requirements if pointers to `BattleGroundWGScore` are held via base class pointers (`BattleGroundScore*`).

### Data Members

**`flagCaptures`**  
A `uint32` counter that records the number of times the associated player has successfully captured the opposing team's flag. This value is incremented by the owning battleground instance (`BattleGroundWS`) when a capture event occurs.

**`flagReturns`**  
A `uint32` counter that records the number of times the associated player has returned their own team's flag to its home base after it was dropped or picked up by an enemy. This value is incremented by `BattleGroundWS` when a return event is processed.

## Cross-Unit Boundaries

`BattleGroundWGScore` does not call into any other units. Its lifecycle and data are managed entirely by the `BattleGroundWS` class.

- **Called by `BattleGroundWS::AddPlayer`**: When a player joins a Warsong Gulch instance, `BattleGroundWS::AddPlayer` (defined in `BattleGroundWS.cpp`, not shown here but referenced in the MAP) creates a new `BattleGroundWGScore` object for that player. This object is then stored in the battleground's internal score tracking system, allowing the game to attribute future flag captures and returns to the correct player.

## Data Model

This unit does not interact directly with any database tables. All data is held in memory for the duration of the battleground session and is discarded when the instance ends or the server restarts.

## Notable Implementation Details

- **Inheritance**: The class inherits from `BattleGroundScore`. While the base class definition is not provided in the source snippet, it likely provides common functionality for all battleground score types (e.g., kill counts, damage dealt). `BattleGroundWGScore` specializes this for WSG-specific metrics.
- **Public Data Members**: Unlike typical C++ design principles that favor encapsulation, `flagCaptures` and `flagReturns` are public. This allows `BattleGroundWS` to directly modify these counters without needing setter methods, simplifying the update logic during gameplay events.
- **No Logic**: The class contains no methods beyond construction and destruction. All business logic related to incrementing these counters resides in `BattleGroundWS`.

## Member Reference

**BattleGroundWGScore**  
Default constructor that initializes `flagCaptures` and `flagReturns` to zero. Called by `BattleGroundWS::AddPlayer` when a player enters the battleground.

**~BattleGroundWGScore**  
Virtual destructor. Performs no custom cleanup.

---

<!-- machine-true, projected from graph.json -->

## Map — BattleGroundWGScore

*Source:* BattleGroundWS.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| BattleGroundWGScore | ctor | — | BattleGroundWS/AddPlayer | — |
| ~BattleGroundWGScore | dtor | — | — | — |
