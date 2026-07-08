# UnitActionBarEntry

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# UnitActionBarEntry

**Purpose & Responsibilities**

`UnitActionBarEntry` is a lightweight data structure defined in `Unit.h` that represents a single slot on a unit’s action bar (e.g., a pet’s ability bar or a charmed creature’s spell bar). Its primary responsibility is to pack two pieces of information—a **spell/action ID** and an **activation state**—into a single `uint32` integer (`packedData`). This compact representation allows the server to efficiently store, transmit, and manipulate action bar configurations for pets, guardians, and charmed entities without requiring complex object overhead for each button.

The structure supports three distinct activation states via the `ActiveStates` enum (implicitly referenced):
1.  **ACT_DISABLED**: The slot is empty or the ability is unavailable.
2.  **ACT_ENABLED**: The ability is active and can be cast.
3.  **ACT_PASSIVE**: The ability is passive (always active, no cast time).

It distinguishes between **spell-based actions** (which have these three states) and **non-spell actions** (such as item usage or macro commands), which are handled differently by the client and server logic.

## Member-by-Member Behavior

### Construction and Initialization
*   **`UnitActionBarEntry` (ctor)**: Initializes the entry to a disabled state. It sets `packedData` such that the type bits correspond to `ACT_DISABLED` and the action ID is zero. This ensures that newly created action bar slots are safe defaults until explicitly configured.

### Accessors (Read Operations)
*   **`GetType`**: Extracts the activation state (`ActiveStates`) from the upper 8 bits of `packedData`. It uses the `UNIT_ACTION_BUTTON_TYPE` macro to shift and mask the data. This is critical for determining if a button is clickable, passive, or empty.
*   **`GetAction`**: Extracts the action ID (usually a Spell ID) from the lower 24 bits of `packedData` using the `UNIT_ACTION_BUTTON_ACTION` macro. This ID is used to look up spell definitions or item prototypes.
*   **`IsActionBarForSpell`**: Determines if the current entry represents a spell-based action. It returns `true` if the type is `ACT_DISABLED`, `ACT_ENABLED`, or `ACT_PASSIVE`. This distinction is vital because non-spell actions (like using an item in a bag slot) do not follow the same autocast or passive logic as spells.

### Mutators (Write Operations)
*   **`SetActionAndType`**: Completely replaces the entry’s content. It packs the provided `action` ID and `type` state into `packedData` using the `MAKE_UNIT_ACTION_BUTTON` macro. This is the primary method for initializing or overwriting a slot.
*   **`SetType`**: Updates only the activation state (upper 8 bits) while preserving the existing action ID. This is used when toggling an ability on/off (e.g., enabling/disabling autocast) without changing the underlying spell.
*   **`SetAction`**: Updates only the action ID (lower 24 bits) while preserving the existing activation state. This is less common but allows swapping the spell associated with a button while keeping its enabled/disabled status intact.

## Cross-Unit Boundaries

`UnitActionBarEntry` is a pure data structure with no internal dependencies on other classes. However, it is heavily integrated into the **Pet**, **Charm**, and **Unit** subsystems.

### Called By: Pet Management (`Pet.Main`)
*   **`CleanupActionBar`**: Iterates through action bar entries to remove invalid or expired spells. It relies on `GetAction` and `IsActionBarForSpell` to identify which slots contain valid spells that need verification.
*   **`SavePetToDB`**: Serializes the pet’s action bar to the database. It calls `GetType` and `GetAction` to extract the state and ID for each slot, formatting them into a string for storage.

### Called By: Player Interaction (`WorldSession.PetHandler`)
*   **`HandlePetSetAction`**: Processes client requests to assign or remove abilities from a pet’s action bar. It uses `SetActionAndType` (via `CharmInfo::SetActionBar`) to update the slot based on user input. It also reads `GetType` and `GetAction` to validate the request against the pet’s known spells.
*   **`HandlePetUnlearnOpcode`**: Handles the removal of learned spells. It scans the action bar using `GetAction` and `IsActionBarForSpell` to find and clear any slots referencing the unlearned spell ID.

### Called By: Unit Logic (`Unit.Main`)
*   **`AddSpellToActionBar`**: Adds a new spell to the first available slot. It uses `SetAction` and `SetType` (or `SetActionAndType`) to populate the entry.
*   **`RemoveSpellFromActionBar`**: Clears slots containing a specific spell ID. It compares `GetAction` results against the target ID.
*   **`LoadPetActionBar`**: Deserializes action bar data from the database or initial creation. It calls `SetActionAndType` to reconstruct the state of each slot.
*   **`SetSpellAutocast` / `ToggleCreatureAutocast`**: Manages the active/passive state of abilities. These methods call `SetType` to toggle between `ACT_ENABLED` and `ACT_PASSIVE` (or `ACT_DISABLED`) based on the autocast flag.
*   **`CharmInfo` / `InitCharmCreateSpells`**: Initializes the action bar for charmed creatures. It uses `SetActionAndType` to populate the bar with the creature’s innate spells.

## Data Model

`UnitActionBarEntry` itself does not interact directly with database tables. It is a transient in-memory structure. However, the data it holds is persisted in the `pet` table (specifically the `actionbar` column) and potentially in the `characters` table for player-controlled pets. The serialization logic resides in `Pet.Main/SavePetToDB` and `Unit.Main/LoadPetActionBar`, which convert the `packedData` format into a string representation (typically space-separated integers or hex strings) for storage.

## Notable Implementation Details

1.  **Bit-Packing Efficiency**: The structure uses bit manipulation macros (`MAKE_UNIT_ACTION_BUTTON`, `UNIT_ACTION_BUTTON_TYPE`, `UNIT_ACTION_BUTTON_ACTION`) to pack two values into one `uint32`. This reduces memory footprint and simplifies network transmission, as the entire action bar can be sent as an array of integers.
2.  **Magic Numbers**: The masks `0xFF000000` (type) and `0x00FFFFFF` (action) are hardcoded in the macros. The type occupies the highest byte, ensuring that sign-extension issues are avoided if the data were ever treated as signed (though it is unsigned).
3.  **Passive vs. Enabled**: The distinction between `ACT_ENABLED` and `ACT_PASSIVE` is crucial for game mechanics. Passive abilities (like a pet’s innate armor buff) should not trigger cast animations or consume resources, whereas enabled abilities do. `IsActionBarForSpell` treats both as "spell-like," but higher-level logic in `Pet` or `Unit` must differentiate them for casting purposes.
4.  **Thread Safety**: As a simple struct with no pointers or dynamic allocation, `UnitActionBarEntry` is trivially thread-safe for reading/writing its `packedData` member, assuming atomic access to the `uint32` (which is generally guaranteed on modern architectures). However, concurrent modification of the same entry by multiple threads (e.g., during a save operation while a client updates the bar) must be synchronized at the `CharmInfo` or `Pet` level.

## Member Reference

*   **`UnitActionBarEntry`**: Constructor that initializes the entry to a disabled state (`ACT_DISABLED`, action ID 0).
*   **`GetType`**: Returns the `ActiveStates` enum value extracted from the upper 8 bits of `packedData`.
*   **`GetAction`**: Returns the action ID (spell/item ID) extracted from the lower 24 bits of `packedData`.
*   **`IsActionBarForSpell`**: Returns `true` if the entry’s type is `ACT_DISABLED`, `ACT_ENABLED`, or `ACT_PASSIVE`, indicating it holds a spell-related action.
*   **`SetActionAndType`**: Sets both the action ID and activation state by packing them into `packedData`.
*   **`SetType`**: Updates only the activation state (upper 8 bits) while preserving the current action ID.
*   **`SetAction`**: Updates only the action ID (lower 24 bits) while preserving the current activation state.

---

<!-- machine-true, projected from graph.json -->

## Map — UnitActionBarEntry

*Source:* Unit.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| UnitActionBarEntry | ctor | — | — | — |
| GetType | method | — | Pet.Main/SavePetToDB, WorldSession.PetHandler/HandlePetSetAction | — |
| GetAction | method | — | Pet.Main/CleanupActionBar, Pet.Main/SavePetToDB, Player.Main/CharmSpellInitialize, Unit.Main/AddSpellToActionBar, Unit.Main/LoadPetActionBar, Unit.Main/RemoveSpellFromActionBar, Unit.Main/SetSpellAutocast, Unit.Main/ToggleCreatureAutocast, WorldSession.PetHandler/HandlePetSetAction, WorldSession.PetHandler/HandlePetUnlearnOpcode | — |
| IsActionBarForSpell | method | — | Pet.Main/CleanupActionBar, Unit.Main/AddSpellToActionBar, Unit.Main/LoadPetActionBar, Unit.Main/RemoveSpellFromActionBar, Unit.Main/SetSpellAutocast, WorldSession.PetHandler/HandlePetUnlearnOpcode | — |
| SetActionAndType | method | — | Unit.Main/CharmInfo, Unit.Main/InitCharmCreateSpells, Unit.Main/LoadPetActionBar | — |
| SetType | method | — | Unit.Main/SetSpellAutocast, Unit.Main/ToggleCreatureAutocast | — |
| SetAction | method | — | Unit.Main/AddSpellToActionBar | — |
