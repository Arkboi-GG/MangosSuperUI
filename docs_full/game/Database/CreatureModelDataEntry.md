# CreatureModelDataEntry

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`CreatureModelDataEntry` is a C++ struct defined in `DBCStructure.h` that represents a single row from the `CreatureModelData.dbc` client data file. It serves as a lightweight data container for physical properties of creature models, specifically exposing the model's ID, flags, name, scale, and collision height.

The primary responsibility of this unit is to provide a query interface for specific bit-flags within the `flags` field. Currently, it exposes only one such check: whether the creature model is capable of mounting. This information is critical for game logic that determines valid mount forms for players or creatures, ensuring that entities cannot mount while in forms that physically or logically prohibit it (e.g., certain shapeshifted states or non-mountable creature types).

## Member-by-Member Behavior

### Flag Querying

**`HasFlag`**
This inline method checks if a specific `CreatureModelDataFlags` bit is set in the struct's `flags` member. It performs a bitwise AND operation between the stored `flags` and the provided `flag` argument, returning `true` if the result is non-zero.

Currently, the only defined flag in the associated enum `CreatureModelDataFlags` is `CREATURE_MODEL_DATA_FLAGS_CAN_MOUNT` (`0x00000080`). Therefore, this method is exclusively used to determine if the creature model represented by this entry permits mounting.

## Cross-Unit Boundaries

### Called By: `Unit.Main/IsInDisallowedMountForm`

The `HasFlag` method is invoked by `Unit.Main/IsInDisallowedMountForm` (located in the `Unit` class, likely in `Unit.cpp` or a related AI/Movement module).

*   **Direction:** Data flows from `CreatureModelDataEntry` to `Unit`.
*   **Collaboration:** The `Unit` class needs to determine if a player or creature is currently in a form that disallows mounting. To do this, it retrieves the `CreatureModelDataEntry` corresponding to the unit's current display/model ID. It then calls `HasFlag(CREATURE_MODEL_DATA_FLAGS_CAN_MOUNT)` on that entry.
*   **Logic:** If `HasFlag` returns `false`, the unit is considered to be in a "disallowed mount form." This prevents the client or server from allowing mount usage when the underlying model data explicitly forbids it. This ensures consistency between visual representation (model data) and gameplay mechanics (mounting rules).

## Data Model

This unit does not interact with any SQL database tables. It maps directly to the `CreatureModelData.dbc` file, which is a binary client-side data file loaded by the server engine. The struct fields correspond to columns in this DBC file:

*   `ID`: The unique identifier for the creature model data entry.
*   `flags`: A bitmask containing various properties, including mount capability.
*   `modelName`: The filename of the model.
*   `modelScale`: The base scale of the model.
*   `collisionHeight`: The height of the collision box.

No SQL queries are executed by this unit.

## Notable Implementation Details

1.  **Inline Performance:** The `HasFlag` method is declared `inline`. Given that mount checks may occur frequently during movement or action validation, this minimizes function call overhead.
2.  **Bitwise Logic:** The implementation uses `!!(flags & flag)` to ensure a strict boolean return value, avoiding potential issues if the result of the bitwise AND is treated as an integer in contexts expecting a bool.
3.  **Limited Flag Exposure:** Although the `flags` field in the DBC file may contain other bits, only `CREATURE_MODEL_DATA_FLAGS_CAN_MOUNT` is currently exposed via the `CreatureModelDataFlags` enum and the `HasFlag` interface. Other potential flags (e.g., related to collision or animation) are not accessible through this specific method signature unless new flags are added to the enum and the calling code is updated.
4.  **Const Correctness:** The method is marked `const`, indicating it does not modify the state of the `CreatureModelDataEntry` object, allowing it to be called on constant references.

## Member Reference

**HasFlag**
An inline method that checks if a specified `CreatureModelDataFlags` bit is set in the `flags` member. It is primarily used to verify if the creature model allows mounting (`CREATURE_MODEL_DATA_FLAGS_CAN_MOUNT`). Called by `Unit.Main/IsInDisallowedMountForm` to enforce mount restrictions based on model data.

---

<!-- machine-true, projected from graph.json -->

## Map — CreatureModelDataEntry

*Source:* DBCStructure.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| HasFlag | method | — | Unit.Main/IsInDisallowedMountForm | — |
