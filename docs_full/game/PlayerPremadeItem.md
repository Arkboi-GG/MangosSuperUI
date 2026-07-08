# PlayerPremadeItem

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# PlayerPremadeItem

**Purpose & Responsibilities**

`PlayerPremadeItem` is a lightweight aggregate struct defined in `ObjectMgr.h`. It serves as a data container representing a single item slot within a pre-configured character gear template. Specifically, it holds the necessary identifiers to instantiate an item with a specific enchantment for a specific faction (team) during the creation of a "premade" character (likely an automated bot or test character, given the context of `CombatBotRoles` found in related structures like `PlayerPremadeGearTemplate`).

The struct contains no methods other than its constructor. Its sole responsibility is to bundle three `uint32` values—item entry ID, enchantment ID, and required team ID—into a single object that can be stored in vectors within larger template structures.

**Member-by-Member Behavior**

*   **`PlayerPremadeItem` (Constructor)**: Initializes the three member variables (`itemId`, `enchantId`, `requiredTeam`) with the values passed as arguments. It uses initializer lists for efficiency.

**Cross-Unit Boundaries**

This unit has no outgoing calls to other units and is not called by any other units according to the provided MAP. However, it is tightly coupled with the following structures defined in the same header (`ObjectMgr.h`):
*   **`PlayerPremadeGearTemplate`**: Contains a `std::vector<PlayerPremadeItem>` named `items`. This indicates that `PlayerPremadeItem` instances are aggregated to define the full equipment loadout for a premade character template.
*   **`ObjectMgr`**: The `ObjectMgr` class manages the loading and storage of these templates via `LoadPlayerPremadeTemplates()` and provides accessors like `GetPlayerPremadeGearTemplates()`. While `ObjectMgr` does not directly call the `PlayerPremadeItem` constructor in the visible interface, it populates the maps containing the parent structures that hold these items.

**Data Model**

The `PlayerPremadeItem` struct itself does not interact directly with database tables. It is a runtime representation of data likely loaded from a custom database table (e.g., `player_premade_gear` or similar, though the specific table name is not explicitly queried in the visible snippet of `ObjectMgr.h`, the method `LoadPlayerPremadeTemplates` implies such a source). The fields correspond to standard World of Warcraft database concepts:
*   `itemId`: Corresponds to the `entry` column in the `item_template` table.
*   `enchantId`: Corresponds to the `entry` column in the `enchantment_template` table.
*   `requiredTeam`: Corresponds to the faction/team identifier (Alliance/Horde), often stored as `0` or `1` in various database contexts.

**Notable Implementation Details**

*   **Default Initialization**: The member variables `itemId`, `enchantId`, and `requiredTeam` are declared with default initializers (`= 0`). This ensures that if a `PlayerPremadeItem` is default-constructed (though the provided constructor requires arguments, default construction might occur in certain vector resizing scenarios or if the constructor were omitted), the fields would be zero-initialized. However, the explicit constructor overrides this for direct instantiation.
*   **Aggregate Nature**: As a simple struct with public members and no virtual functions, it is a Plain Old Data (POD)-like structure, making it cheap to copy and store in containers.
*   **Contextual Usage**: The presence of `requiredTeam` suggests that premade characters might be faction-specific, ensuring that items equipped are appropriate for the character's alliance/horde affiliation.

## Member Reference

**PlayerPremadeItem**
Constructor that initializes the `itemId`, `enchantId`, and `requiredTeam` member variables with the provided `uint32` arguments. It takes three parameters: `item` (assigned to `itemId`), `enchant` (assigned to `enchantId`), and `team` (assigned to `requiredTeam`).

---

<!-- machine-true, projected from graph.json -->

## Map — PlayerPremadeItem

*Source:* ObjectMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| PlayerPremadeItem | ctor | — | — | — |
