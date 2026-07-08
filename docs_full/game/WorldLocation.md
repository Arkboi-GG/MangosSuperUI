# WorldLocation

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# WorldLocation

`WorldLocation` is a lightweight aggregate struct defined in `SharedDefines.h` that represents a specific point in the game world. It encapsulates the map identifier (`mapId`) and the spatial coordinates (`x`, `y`, `z`) along with the orientation (`o`) required to place a unit or effect within the 3D environment.

Unlike the simpler `Position` struct also defined in this file, `WorldLocation` explicitly includes the `mapId`. This distinction is critical because many operations—such as teleporting a player, spawning a creature, or targeting a spell effect—require knowing not just *where* in the local coordinate space an entity is, but *which* map instance that coordinate space belongs to. `WorldLocation` serves as the standard contract for passing complete location data across module boundaries, ensuring that callers do not inadvertently apply coordinates to the wrong map.

The struct provides two constructors and a validation method. It does not manage memory, handle persistence, or interact with the database directly; it is purely a data carrier. Its usage is pervasive in movement handling, spell targeting, instance management, and boss encounter logic.

## Member-by-Member Behavior

### Construction

**`WorldLocation` (default/parameterized constructor)**
This constructor initializes the `mapId`, `x`, `y`, `z`, and `o` members. It accepts optional arguments for each field, defaulting all to zero if not provided. This allows for the creation of an "empty" or null location easily, which is often used as a sentinel value to indicate that no valid location has been set yet.

**`WorldLocation` (copy constructor)**
This constructor creates a new `WorldLocation` by copying the values from an existing `WorldLocation` instance. It ensures that all five fields (`mapId`, `x`, `y`, `z`, `o`) are duplicated exactly. This is essential for passing locations by value without aliasing issues, particularly in high-frequency contexts like spell effect resolution or movement updates.

### Validation

**`IsEmpty`**
This method returns `true` if all fields (`mapId`, `x`, `y`, `z`, `o`) are zero. It serves as a quick validity check. Since valid game coordinates can theoretically include zeros, this check is primarily used to detect uninitialized or explicitly cleared location objects. Callers use this to avoid processing invalid teleport targets or spell positions.

## Cross-Unit Boundaries

`WorldLocation` is a passive data structure; it does not call out to other units. However, it is heavily consumed by other parts of the system. Below is a breakdown of how other units interact with `WorldLocation`.

### Creation and Initialization

Several subsystems construct `WorldLocation` instances to define target states for entities:

*   **`boss_nefarian/OnEffectExecute`**: Constructs a `WorldLocation` to determine where Nefarian should move or cast during his encounter phases. This ensures the boss logic operates with precise spatial awareness.
*   **`boss_ouro/OnUse`**: Uses `WorldLocation` to calculate positioning for Ouro's mechanics, likely for spawning effects or moving the boss to specific arena zones.
*   **`MapManager/CreateNewInstancesForPlayers`**: Creates `WorldLocation` objects to place players into newly generated instance maps. This is crucial for dungeon entry logic, ensuring players spawn at the correct entrance coordinates within the correct map ID.
*   **`Player.Main/ChangeRace`**: When a player changes race (likely via GM command or specific quest), this method constructs a `WorldLocation` to reposition the player, possibly to a safe zone or their homebind, ensuring they don't get stuck in geometry incompatible with their new model.
*   **`Player.Main/ExecuteTeleportFar`**: Builds a `WorldLocation` for long-distance teleports, such as those triggered by mounts or specific spells, ensuring the destination map and coordinates are correctly packaged.
*   **`Player.Main/SetBattleGroundEntryPoint`** (both overloads): Prepares `WorldLocation` objects to define where a player enters a battleground. One overload may handle initial queue placement, while the other handles final spawn point assignment.
*   **`Player.Main/TeleportTo`**: The core teleportation method constructs a `WorldLocation` from input parameters to move the player to a specified spot.
*   **`Player.Main/TeleportToHomebind`**: Constructs a `WorldLocation` using the player's saved homebind data to return them to their inn or graveyard.
*   **`Player.Main/_LoadBGData`**: Loads battleground-specific location data into a `WorldLocation` structure, likely for restoring state or preparing spawn points.
*   **`Spell.Effects/EffectBind`**: When a spell binds a soul (like a Hearthstone), it captures the current `WorldLocation` to store as the player's homebind.
*   **`SpellMgr/LoadSpellTargetPositions`**: Reads spell target positions from data sources and populates `WorldLocation` structures, ensuring spells cast at specific coordinates (like targeted AoEs) land correctly.
*   **`WorldSession.MovementHandler/HandleMoveWorldportAck`**: Upon receiving acknowledgment from the client that a world port has completed, this handler may construct or verify a `WorldLocation` to synchronize server-side state with the client's new position.

### Consumption

*   **`Player.Main/SetBattleGroundEntryPoint`**: This method is also listed as calling the `WorldLocation` constructor, indicating it actively builds these objects to pass to other systems or store internally for later use during battleground transitions.

## Data Model

`WorldLocation` does not directly interact with any database tables. It is a transient in-memory structure. While the data it holds (coordinates and map IDs) originates from database queries (e.g., `creature` table, `gameobject` table, or player save data), the struct itself performs no I/O.

## Notable Implementation Details

1.  **Zero as Invalid**: The `IsEmpty()` method treats a `mapId` of 0 as part of the "empty" state. In World of Warcraft, Map ID 0 is Eastern Kingdoms, which is a valid map. Therefore, `IsEmpty()` returning `true` for a location on Eastern Kingdoms at coordinates `(0,0,0)` with orientation `0` is technically ambiguous. However, in practice, `(0,0,0)` is rarely a valid spawn point for players or creatures, so this heuristic is generally safe for detecting uninitialized objects. Maintainers should be cautious if relying on `IsEmpty()` for locations that might legitimately be near the origin.
2.  **Copy Semantics**: The explicit copy constructor ensures that `WorldLocation` behaves as a value type. This prevents accidental sharing of mutable state if the struct were ever modified to contain pointers (it does not currently).
3.  **Default Initialization**: All fields are initialized to zero in the default constructor. This guarantees that a `WorldLocation` declared on the stack is immediately in a known "empty" state, reducing the risk of using garbage values.
4.  **No Validation Logic**: The struct does not validate whether the `mapId` exists or whether the coordinates are within the bounds of the map. This validation is deferred to the consumers (e.g., `Player.TeleportTo`), which must ensure the location is safe before applying it.

## Member Reference

**WorldLocation#2**
The parameterized constructor that initializes `mapId`, `x`, `y`, `z`, and `o` with provided values or defaults to zero. Used extensively by `boss_nefarian/OnEffectExecute`, `boss_ouro/OnUse`, `MapManager/CreateNewInstancesForPlayers`, `Player.Main/ChangeRace`, `Player.Main/ExecuteTeleportFar`, `Player.Main/SetBattleGroundEntryPoint`, `Player.Main/SetBattleGroundEntryPoint#2`, `Player.Main/TeleportTo`, `Player.Main/TeleportToHomebind`, `Player.Main/_LoadBGData`, `Spell.Effects/EffectBind`, `SpellMgr/LoadSpellTargetPositions`, and `WorldSession.MovementHandler/HandleMoveWorldportAck` to create fully specified location objects.

**WorldLocation**
The copy constructor that duplicates an existing `WorldLocation` instance. Called by `Player.Main/SetBattleGroundEntryPoint` to preserve location data during battleground setup.

**IsEmpty**
A method that returns `true` if all fields (`mapId`, `x`, `y`, `z`, `o`) are zero. Used to detect uninitialized or null locations. No external callers are listed in the map, but it is logically used by consumers to validate location objects before processing.

---

<!-- machine-true, projected from graph.json -->

## Map — WorldLocation

*Source:* SharedDefines.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| WorldLocation#2 | ctor | — | boss_nefarian/OnEffectExecute, boss_ouro/OnUse, MapManager/CreateNewInstancesForPlayers, Player.Main/ChangeRace, Player.Main/ExecuteTeleportFar, Player.Main/SetBattleGroundEntryPoint, Player.Main/SetBattleGroundEntryPoint#2, Player.Main/TeleportTo, Player.Main/TeleportToHomebind, Player.Main/_LoadBGData, Spell.Effects/EffectBind, SpellMgr/LoadSpellTargetPositions, WorldSession.MovementHandler/HandleMoveWorldportAck | — |
| WorldLocation | ctor | — | Player.Main/SetBattleGroundEntryPoint | — |
| IsEmpty | method | — | — | — |
