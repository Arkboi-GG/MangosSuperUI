# PlayerBotFleeingAI

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# PlayerBotFleeingAI

## Purpose & Responsibilities

`PlayerBotFleeingAI` is a specialized subclass of `PlayerBotAI` within the WoWVMaNGOS bot framework. As its name implies, it represents an artificial intelligence profile intended for bots that exhibit fleeing behavior. However, in the current implementation provided in `PlayerBotAI.h`, this class is structurally minimal. It inherits the base functionality of `PlayerBotAI` but currently overrides only the `OnPlayerLogin` lifecycle hook. The specific logic for "fleeing" is not defined within this header or the associated constructor; rather, the class serves as a distinct type identifier that can be instantiated by the factory function `CreatePlayerBotAI` (defined in `PlayerBotAI.cpp`, though not shown in the source snippet, it is referenced in the MAP).

The class exists primarily to allow the bot system to distinguish between different behavioral archetypes (e.g., attackers, populators, and fleers) during initialization and login sequences.

## Member-by-Member Behavior

### Construction and Initialization

**`PlayerBotFleeingAI`**
This is the default constructor for the class. It performs two key actions:
1.  It invokes the base class constructor `PlayerBotAI()` with no arguments. This initializes the `PlayerAI` base and sets the `botEntry` pointer to `nullptr`.
2.  It establishes the object as an instance of `PlayerBotFleeingAI`.

According to the MAP, this constructor is called exclusively by `PlayerBotAI/CreatePlayerBotAI`. This indicates that instances of `PlayerBotFleeingAI` are created dynamically via the factory pattern used by the bot management system, likely when a bot is assigned the "fleeing" AI type.

### Lifecycle Hooks

**`OnPlayerLogin`**
This method overrides the virtual `OnPlayerLogin` function declared in the base class `PlayerBotAI`. In the provided header, the declaration is present, but the definition is not visible in the snippet. However, based on standard C++ inheritance patterns in this codebase, this hook is triggered when the bot's associated `Player` object successfully logs into the world. While the specific implementation details are not in the header, the presence of this override suggests that `PlayerBotFleeingAI` may perform specific setup tasks upon login that differ from the default empty implementation in `PlayerBotAI`.

## Cross-Unit Boundaries

### Incoming Calls

*   **`PlayerBotAI/CreatePlayerBotAI`**: The MAP indicates that the `PlayerBotFleeingAI` constructor is called by `CreatePlayerBotAI`. This factory function resides in the `PlayerBotAI` unit (likely `PlayerBotAI.cpp`). When the bot system determines that a bot should use the "fleeing" AI profile, it calls this factory, which instantiates a `PlayerBotFleeingAI` object. This is the primary entry point for creating instances of this class.

### Outgoing Calls

*   **None**: The MAP shows no outgoing calls from `PlayerBotFleeingAI` members to other units. The constructor calls the base class constructor, which is internal to the inheritance hierarchy. The `OnPlayerLogin` override may contain logic, but without the `.cpp` source, we cannot confirm external dependencies. Based strictly on the provided MAP and Header, it does not explicitly call out to other named units like `Movement` or `Combat`.

## Data Model

`PlayerBotFleeingAI` does not interact directly with any database tables. It operates entirely in memory, managing the state of a bot character during runtime. Any persistent data related to the bot (such as its AI type assignment) would be handled by higher-level managers or the `PlayerBotEntry` structure, which is passed to other hooks like `OnSessionLoaded` in sibling classes, but not directly queried by this specific AI class in the provided interface.

## Notable Implementation Details

1.  **Minimalist Design**: The class is extremely lightweight. It adds no member variables and only overrides one virtual function (`OnPlayerLogin`) in addition to the constructor. This suggests that the "fleeing" behavior might be implemented elsewhere (e.g., in a separate movement module or combat handler) or that this class is a placeholder for future expansion.
2.  **Inheritance Chain**: It inherits from `PlayerBotAI`, which in turn inherits from `PlayerAI`. This grants it access to all standard player AI utilities, such as movement commands, target selection, and state updates, even if it doesn't override them.
3.  **Factory Dependency**: The class relies on `CreatePlayerBotAI` for instantiation. Engineers modifying this class should ensure that the factory function correctly handles the string identifier for "fleeing" AI to instantiate this specific class.

## Member Reference

**PlayerBotFleeingAI**
The default constructor for the `PlayerBotFleeingAI` class. It initializes the object by calling the base `PlayerBotAI()` constructor. It is instantiated by the `PlayerBotAI/CreatePlayerBotAI` factory function when a bot is assigned the fleeing AI profile.

---

<!-- machine-true, projected from graph.json -->

## Map — PlayerBotFleeingAI

*Source:* PlayerBotAI.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| PlayerBotFleeingAI | ctor | — | PlayerBotAI/CreatePlayerBotAI | — |
