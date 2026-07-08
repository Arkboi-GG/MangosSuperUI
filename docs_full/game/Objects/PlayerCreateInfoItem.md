# PlayerCreateInfoItem

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# PlayerCreateInfoItem

## Purpose & Responsibilities

`PlayerCreateInfoItem` is a lightweight data structure (POD-like struct) defined in `Player.h`. Its sole responsibility is to represent a single starting item grant for a new character creation. It holds the database identifier (`item_id`) of the item and the quantity (`item_amount`) that should be awarded to the player upon account or character initialization.

This struct is part of the broader character creation data pipeline. It is instantiated during the loading of static configuration data from the database by the `ObjectMgr` subsystem and is subsequently used by the `Player` class logic to populate a new character's inventory.

## Member-by-Member Behavior

The unit contains only one member: the constructor.

### Constructor: `PlayerCreateInfoItem`

*   **Signature:** `PlayerCreateInfoItem(uint32 id, uint32 amount)`
*   **Behavior:** This is an inlined constructor that initializes the two public member variables:
    *   `item_id`: Set to the `id` parameter.
    *   `item_amount`: Set to the `amount` parameter.
*   **Context:** It is exclusively called by `ObjectMgr::LoadPlayerInfo` (as indicated in the MAP). During server startup or reload, `ObjectMgr` queries the database for starting equipment configurations. For each row returned, it constructs a `PlayerCreateInfoItem` and appends it to a `std::vector<PlayerCreateInfoItem>` (typedef'd as `PlayerCreateInfoItems`) within the `PlayerInfo` structure. This vector is later accessed when a new `Player` object is created via `Player::Create`, ensuring the new character receives the correct starting gear.

## Cross-Unit Boundaries

*   **Called By: `ObjectMgr/LoadPlayerInfo`**
    *   **Direction:** `ObjectMgr` calls into `PlayerCreateInfoItem`.
    *   **Collaboration:** `ObjectMgr` (specifically the `LoadPlayerInfo` method) acts as the factory consumer. It reads raw query results containing item IDs and quantities. It instantiates `PlayerCreateInfoItem` objects to encapsulate this data into a strongly-typed structure. This decouples the raw database parsing logic from the data representation used by the `Player` class. The `ObjectMgr` stores these items in the `PlayerInfo` struct associated with specific race/class combinations.

## Data Model

This unit does not directly interact with database tables. However, it represents data derived from the `character_creation_info` table (or similar configuration tables depending on the specific MaNGOS/WowVMaNGOS schema version). The `ObjectMgr::LoadPlayerInfo` method performs the SQL query that populates these structs. The columns typically involved are `item_id` and `item_count` (or `amount`). Since no SCHEMA section is provided for this specific unit's direct interaction, and the unit itself contains no SQL, no specific column types or constraints are documented here beyond the `uint32` types used in the C++ struct.

## Notable Implementation Details

*   **Public Members:** Both `item_id` and `item_amount` are public `uint32` members. This allows direct read-only access by the `Player` class when iterating through the starting items vector.
*   **No Validation:** The constructor performs no validation on the `id` or `amount`. It assumes the data provided by `ObjectMgr` is valid. Invalid item IDs would likely cause errors later when `Player::AddItem` or similar methods attempt to look up the `ItemPrototype`.
*   **Memory Layout:** As a simple struct with two `uint32`s, it has a predictable size (8 bytes) and is efficiently stored in `std::vector` containers.
*   **Typedef Usage:** It is primarily used via the typedef `PlayerCreateInfoItems` (`std::vector<PlayerCreateInfoItem>`), indicating it is always handled in collections rather than as isolated instances.

## Member Reference

**PlayerCreateInfoItem**
Constructor that initializes the `item_id` and `item_amount` members with the provided `id` and `amount` arguments. It is called by `ObjectMgr::LoadPlayerInfo` to package starting item data retrieved from the database.

---

<!-- machine-true, projected from graph.json -->

## Map — PlayerCreateInfoItem

*Source:* Player.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| PlayerCreateInfoItem | ctor | — | ObjectMgr/LoadPlayerInfo | — |
