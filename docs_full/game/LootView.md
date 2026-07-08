# LootView

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# LootView

`LootView` is a lightweight aggregate struct defined in `LootMgr.h` that encapsulates the context required to generate a loot window for a specific player. It does not contain logic or state beyond holding references to the underlying loot data (`Loot`), the requesting player (`Player`), and the permission rules (`PermissionTypes`) that govern who can take what. Its sole purpose is to serve as the argument for serializing loot information into a network packet via the `operator<<` overload, ensuring that the server sends only the items and metadata relevant to the viewing player’s permissions and quest status.

## Purpose & Responsibilities

The core responsibility of `LootView` is to bridge the gap between the raw, server-side `Loot` object—which contains all possible drops, including those hidden from certain players due to permissions, quests, or group roles—and the client-side representation. When a player opens a loot window, the server must determine which items are visible, which are locked, which require a roll, and which are free-for-all. `LootView` packages these three critical pieces of context:
1.  **`loot`**: The `Loot` object containing the generated items, gold, and quest items.
2.  **`viewer`**: The `Player` object representing the character attempting to view the loot.
3.  **`permission`**: An enum value (`PermissionTypes`) defining the loot distribution rule (e.g., Master Looter, Free-for-All, Round Robin).

By bundling these together, `LootView` allows the serialization logic (implemented in `LootMgr.cpp`, though not shown here) to access all necessary data through a single parameter, simplifying the interface for packet construction.

## Member-by-Member Behavior

### `LootView` (Constructor)

The constructor initializes the three member variables. It accepts a reference to a `Loot` object, a pointer to a `Player`, and an optional `PermissionTypes` enum, defaulting to `ALL_PERMISSION`.

*   **`loot`**: Stores the reference to the `Loot` object. This object holds the actual item data, gold amounts, and quest item maps.
*   **`viewer`**: Stores the pointer to the `Player` who is viewing the loot. This is used during serialization to filter items based on the player's level, class, race, and active quests.
*   **`permission`**: Stores the permission type. This dictates how the loot window behaves (e.g., whether items are automatically assigned, require a roll, or are restricted to the group leader).

## Cross-Unit Boundaries

### Called By: `Player.Main/SendLoot`

The `LootView` constructor is invoked by the `SendLoot` method within the `Player` class (specifically the `Main` partial, as indicated by the map). This occurs when a player interacts with a corpse, fishing hole, pickpocket target, or disenchantable item.

*   **Direction**: `Player.Main/SendLoot` → `LootView`
*   **Collaboration**: `Player::SendLoot` constructs a `LootView` instance passing the current `Loot` object, the player pointer (`this`), and the determined permission type. This `LootView` is then passed to the `operator<<` function to serialize the loot data into a `ByteBuffer` for transmission to the client. This design ensures that the serialization logic remains decoupled from the `Player` class while having access to all necessary context.

## Data Model

`LootView` itself does not interact with any database tables. It operates entirely on in-memory objects (`Loot`, `Player`). The `Loot` object it references may have been populated using data from loot tables (e.g., `creature_loot_template`, `gameobject_loot_template`), but `LootView` does not perform any database queries or schema interactions.

## Notable Implementation Details

*   **Aggregate Struct**: `LootView` is a simple aggregate with no methods other than the constructor. It relies on the `friend` declaration of `operator<<` in the `Loot` struct to allow direct access to private members of `Loot` during serialization.
*   **Default Permission**: The constructor defaults `permission` to `ALL_PERMISSION`. This is significant because if the calling code fails to specify a permission type, the loot window will behave as if everyone can loot everything, potentially bypassing intended group restrictions. However, in practice, `Player::SendLoot` typically determines the correct permission based on group settings before constructing the `LootView`.
*   **No Ownership**: `LootView` holds a reference to `Loot` and a pointer to `Player`. It does not own these objects. Therefore, the lifetime of the `Loot` and `Player` objects must exceed the duration of the serialization process. Since serialization happens synchronously within the `SendLoot` call, this is generally safe, but care must be taken if `LootView` were ever stored or passed asynchronously.

## Member Reference

**LootView**
Constructor that initializes the `LootView` struct with a reference to a `Loot` object, a pointer to a `Player`, and a `PermissionTypes` enum. It defaults the permission to `ALL_PERMISSION` if not specified. This struct is used to package loot context for serialization into a network packet.

---

<!-- machine-true, projected from graph.json -->

## Map — LootView

*Source:* LootMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| LootView | ctor | — | Player.Main/SendLoot | — |
