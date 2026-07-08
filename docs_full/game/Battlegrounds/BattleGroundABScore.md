# BattleGroundABScore

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# BattleGroundABScore

**Purpose & Responsibilities**

`BattleGroundABScore` is a lightweight data structure used within the Alterac Valley (AV) battleground implementation to track specific objective-based statistics for individual players. It extends the base `BattleGroundScore` class to include two additional metrics relevant to AV’s node-capture gameplay:

1.  **`basesAssaulted`**: The number of enemy or neutral nodes (Stables, Blacksmith, Farm, Lumber Mill, Gold Mine) the player has successfully captured.
2.  **`basesDefended`**: The number of friendly nodes the player has successfully defended from enemy capture.

This class functions strictly as a data holder. It contains no logic for calculating or modifying these scores; that responsibility lies with the `BattleGroundAB` class (defined in `BattleGroundAB.cpp`), which accesses these fields directly via the player’s associated `BattleGroundABScore` instance.

**Member-by-Member Behavior**

The unit consists of a constructor and a destructor. The class also exposes two public data members, `basesAssaulted` and `basesDefended`, which are initialized by the constructor.

*   **Constructor (`BattleGroundABScore`)**: Initializes the object by setting `basesAssaulted` and `basesDefended` to zero using the initializer list. This ensures that every new player entering the battleground starts with a clean slate for these specific objectives.
*   **Destructor (`~BattleGroundABScore`)**: A virtual destructor required because the class inherits from `BattleGroundScore`. It performs no custom cleanup, as the class only contains primitive integer members.

**Cross-Unit Boundaries**

*   **Called by `BattleGroundAB::AddPlayer`**: When a player joins the Alterac Valley battleground, `BattleGroundAB` (defined in `BattleGroundAB.cpp`) allocates a `BattleGroundABScore` object for that player. This establishes the link between the player’s session in the battleground and their specific AV score tracking.
*   **Inherits from `BattleGroundScore`**: The base class provides common scorekeeping functionality (e.g., kills, deaths, honor points) shared across all battleground types. `BattleGroundABScore` augments this with AV-specific metrics.

**Data Model**

This unit does not interact directly with any database tables. It operates entirely in memory during the lifetime of a battleground instance. The scores are transient and are typically persisted to the database (if at all) by higher-level systems after the battleground ends, not by this class itself.

**Notable Implementation Details**

*   **Minimalist Design**: The class is purely a data holder. There are no methods to modify the scores; this enforces encapsulation by forcing all score updates to go through the `BattleGroundAB` manager class, which can validate conditions before incrementing.
*   **Virtual Destructor**: The destructor is declared `virtual` to ensure proper cleanup when deleting a `BattleGroundABScore` object through a pointer to its base class `BattleGroundScore`. This is a standard C++ best practice for polymorphic hierarchies.
*   **Initialization**: The constructor explicitly initializes the counters to zero. While default initialization of `uint32` members might result in zero in some contexts, explicit initialization guarantees correctness regardless of compiler or memory layout quirks.

## Member Reference

**BattleGroundABScore**  
Constructor. Initializes `basesAssaulted` and `basesDefended` to 0. Called by `BattleGroundAB::AddPlayer` to create a score record for a new participant.

**~BattleGroundABScore**  
Virtual destructor. Performs no custom cleanup. Required for safe polymorphic deletion via `BattleGroundScore` pointers.

---

<!-- machine-true, projected from graph.json -->

## Map — BattleGroundABScore

*Source:* BattleGroundAB.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| BattleGroundABScore | ctor | — | BattleGroundAB/AddPlayer | — |
| ~BattleGroundABScore | dtor | — | — | — |
