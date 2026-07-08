# ChrRacesEntry

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ChrRacesEntry

**Purpose & Responsibilities**
`ChrRacesEntry` is a C++ struct defined in `DBCStructure.h` that represents a single row from the game's `ChrRaces.dbc` (Data Block Chunk) file. It serves as a low-level data container for character race definitions, holding static configuration data such as race IDs, faction affiliations, display model identifiers, and behavioral flags. The struct is tightly packed (`#pragma pack(1)`) to match the binary layout of the client-side DBC file, ensuring correct memory mapping when the server loads game data. Its primary responsibility is to provide structured access to this raw data, specifically exposing a helper method, `HasFlag`, to interpret the bitwise `Flags` field.

**Member-by-Member Behavior**
The unit contains a single documented member:

*   **`HasFlag`**: An inline method that checks whether a specific bit is set in the `Flags` member of the `ChrRacesEntry` instance. It takes a `ChrRacesFlags` enum value as input, performs a bitwise AND operation with the entry's `Flags`, and returns `true` if the result is non-zero. This allows callers to query specific race properties, such as whether a race is playable, requires bare feet, or can mount, without manually handling bitwise logic.

**Cross-Unit Boundaries**
`ChrRacesEntry` is a passive data structure; it does not initiate calls to other units. However, it is consumed by two distinct subsystems:

1.  **`Unit.Main` (specifically `IsInDisallowedMountForm`)**: Called by `Unit.Main` to determine if a unit's race possesses the `CHRRACES_FLAGS_CAN_MOUNT` flag. This check is part of the logic determining whether a player or creature is allowed to mount, likely filtering out races that are inherently non-mountable according to the DBC data.
2.  **`WorldSession.CharacterHandler` (specifically `HandleCharCreateOpcode`)**: Called during character creation to validate or process race-specific constraints. The handler uses `HasFlag` to check flags like `CHRRACES_FLAGS_NOT_PLAYABLE` to ensure players cannot create characters of non-playable races, or to apply race-specific initialization rules.

**Data Model**
This unit does not interact with SQL database tables. It exclusively reads from the `ChrRaces.dbc` file, a binary data file provided by the game client. The struct maps directly to the columns of this DBC file. No SQL queries are performed by this unit.

**Notable Implementation Details**
*   **Bitwise Flag Interpretation**: The `HasFlag` method uses `!!(Flags & flag)` to convert the result of the bitwise AND into a boolean. The double negation ensures that any non-zero result becomes `true`, while zero becomes `false`. This is a standard idiom for flag checking.
*   **Packed Structure**: The entire `DBCStructure.h` file uses `#pragma pack(1)` to eliminate compiler padding between struct members. This is critical because `ChrRacesEntry` must match the exact byte layout of the DBC file. Any deviation in member order or size would cause data corruption when reading the file.
*   **Enum Dependency**: The method relies on the `ChrRacesFlags` enum defined earlier in the same file. The valid flags are `CHRRACES_FLAGS_NOT_PLAYABLE` (0x01), `CHRRACES_FLAGS_BARE_FEET` (0x02), and `CHRRACES_FLAGS_CAN_MOUNT` (0x04). Callers must use these specific values for the check to be meaningful.
*   **Inline Performance**: The method is marked `inline`, suggesting it is intended for frequent use in performance-critical paths, such as character creation validation or mount eligibility checks, where function call overhead is undesirable.

## Member Reference

**HasFlag**
An inline method that checks if a specific `ChrRacesFlags` bit is set in the entry's `Flags` field. It is called by `Unit.Main/IsInDisallowedMountForm` to verify mount eligibility and by `WorldSession.CharacterHandler/HandleCharCreateOpcode` to enforce race playability rules during character creation.

---

<!-- machine-true, projected from graph.json -->

## Map — ChrRacesEntry

*Source:* DBCStructure.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| HasFlag | method | — | Unit.Main/IsInDisallowedMountForm, WorldSession.CharacterHandler/HandleCharCreateOpcode | — |
