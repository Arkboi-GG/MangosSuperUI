# MageOrgrimmarAttackerAI

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# MageOrgrimmarAttackerAI

**Purpose & Responsibilities**

`MageOrgrimmarAttackerAI` is a specialized artificial intelligence controller for non-player characters (NPCs) or bot players within the World of Warcraft emulation framework (Mangos/WowVM). As indicated by its name, this AI is designed to manage a character that acts as an attacker, specifically utilizing the Mage class, located in or associated with the city of Orgrimmar.

It inherits from `PlayerBotAI`, which itself inherits from `PlayerAI`. This hierarchy indicates that the AI controls a `Player` object (likely a bot or a controlled NPC masquerading as a player) and integrates into the game's core update loop and session management systems. The primary responsibility of this unit is to initialize the bot upon session load and process its behavior during the game's tick cycle via `UpdateAI`.

**Member-by-Member Behavior**

The unit defines two key behaviors: initialization upon loading and periodic updates.

1.  **Initialization (`MageOrgrimmarAttackerAI` constructor)**: The constructor initializes the base `PlayerBotAI` class with the associated `Player` pointer. It sets up the object to be ready for session loading.
2.  **Session Loading (`OnSessionLoaded`)**: Although declared in the header, the implementation is not provided in the source snippet. However, based on the inheritance chain and the `PlayerBotAI` interface, this method is called when the bot's session is loaded. It likely prepares the bot for combat or movement specific to the "Orgrimmar Attacker" role.
3.  **Game Loop Update (`UpdateAI`)**: This method is overridden from `PlayerBotAI` (which overrides `PlayerAI`). It is called periodically by the game engine with a time difference (`diff`) since the last call. This is where the core logic for the attacker's actions—such as targeting, casting spells, or moving—would reside. The parameter `diff` allows for frame-rate independent calculations.

**Cross-Unit Boundaries**

*   **Called by `PlayerBotAI/CreatePlayerBotAI`**: The `MageOrgrimmarAttackerAI` constructor is invoked by the factory function `CreatePlayerBotAI` (defined in `PlayerBotAI.cpp`, though not shown in the provided source). This factory pattern allows the system to instantiate the correct AI subclass based on configuration data (e.g., a string name like "MageOrgrimmarAttacker"). This establishes the lifecycle entry point for this AI.
*   **Inheritance from `PlayerBotAI`**: The unit relies heavily on the infrastructure provided by `PlayerBotAI`. It inherits methods like `Remove`, `SpawnNewPlayer`, and hooks like `OnPlayerLogin`. While `MageOrgrimmarAttackerAI` does not explicitly call other units in the provided map, its existence is contingent on the `PlayerBotAI` framework managing the underlying `Player` object and its session.

**Data Model**

This unit does not directly interact with any database tables. The MAP indicates no tables are touched by its members. Any data required for the AI's operation (such as spell IDs, target priorities, or movement paths) would typically be hardcoded, derived from the `Player` object's state, or passed through the `PlayerBotEntry` structure during the `OnSessionLoaded` phase.

**Notable Implementation Details**

*   **Minimalist Definition**: The provided source code for `MageOrgrimmarAttackerAI` is extremely sparse. It only declares the constructor and overrides `OnSessionLoaded` and `UpdateAI`. The actual logic for *how* it attacks or behaves is not visible in this header file. This suggests that either:
    1.  The implementation is entirely contained within the corresponding `.cpp` file (not provided).
    2.  The logic is inherited or delegated to other parts of the `PlayerBotAI` framework.
    3.  The class is a placeholder or stub for future development.
*   **Override of `UpdateAI`**: The `UpdateAI` method takes a `uint32 const diff` parameter, which is standard in game loops for handling time-based updates. The comment in the base class `PlayerBotAI::UpdateAI` mentions "Handle delayed teleports," but `MageOrgrimmarAttackerAI` overrides this, implying it has its own update logic distinct from simple teleport handling.
*   **No Custom State**: Unlike `PlayerCreatorAI` or `PopulateAreaBotAI`, which store configuration data (race, class, coordinates, radius) in member variables, `MageOrgrimmarAttackerAI` has no custom member variables declared in the header. This implies its behavior is either static, determined by the `Player` object it controls, or configured externally via the `PlayerBotEntry` during session load.

## Member Reference

**MageOrgrimmarAttackerAI**
Constructor for the `MageOrgrimmarAttackerAI` class. It initializes the base `PlayerBotAI` with the provided `Player` pointer. It is called by the `CreatePlayerBotAI` factory function in `PlayerBotAI.cpp` to instantiate this specific AI type.

---

<!-- machine-true, projected from graph.json -->

## Map — MageOrgrimmarAttackerAI

*Source:* PlayerBotAI.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| MageOrgrimmarAttackerAI | ctor | — | PlayerBotAI/CreatePlayerBotAI | — |
