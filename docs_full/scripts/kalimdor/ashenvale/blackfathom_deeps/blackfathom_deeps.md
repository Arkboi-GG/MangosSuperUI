<!-- provenance: verbose, failed-members -->
# blackfathom_deeps

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# blackfathom_deeps

## Purpose & Responsibilities

`blackfathom_deeps.h` is a header-only unit providing constant identifiers for the Blackfathom Deeps instance script. It contains no executable logic, classes, or functions. Its sole responsibility is to define an anonymous `enum` of integer constants that other units (primarily `blackfathom_deeps.cpp`) use to identify entities, track instance state, and categorize encounters, thereby avoiding hard-coded magic numbers in the implementation.

## Member-by-Member Behavior

The unit defines constants in four logical groups:

1.  **Instance Data Keys (`DATA_*`)**: Integers used as keys to store and retrieve the state of specific instance events (e.g., boss kills, shrine completions) in the instance data store.
2.  **Encounter Types (`TYPE_*`)**: Integers categorizing specific encounters, likely used for grouping logic or UI display within the instance manager.
3.  **Entity Identifiers (`NPC_*`, `GO_*`)**: Integer IDs corresponding to specific Non-Player Characters and Game Objects in the game world. These are used to spawn, despawn, or interact with these entities.
4.  **Encounter Indices (`BFD_ENCOUNTER_*`)**: Integers mapping specific encounters to array indices, used for iterating over or storing encounter data.

## Cross-Unit Boundaries

*   **Calls out**: None. This unit contains no executable code.
*   **Called by**: Primarily `blackfathom_deeps.cpp` (the implementation file for this instance). Other scripts may include this header if they need to reference Blackfathom Deeps-specific IDs or data keys.

## Data Model

This unit does not interact with any database tables. It defines constants that may be used by other units to query or update instance data, but no SQL queries or table references are present in this header.

## Notable Implementation Details

*   **Anonymous Enum**: All constants are defined within an anonymous `enum`. This creates integer constants without polluting the global namespace with `#define` macros.
*   **Explicit Values**: Values are explicitly assigned, allowing gaps in the sequence (e.g., jumping from 8 to 10) for logical grouping or future expansion.
*   **Hardcoded IDs**: The numeric values for NPCs and GOs are hardcoded. These must match the values in the game's database (`creature_template`, `gameobject_template`). Any discrepancy will cause runtime errors or missing entities.

## Member Reference

**DATA_SHRINE1**
Enum value (1) representing the state of the first shrine event.

**DATA_SHRINE2**
Enum value (2) representing the state of the second shrine event.

**DATA_SHRINE3**
Enum value (3) representing the state of the third shrine event.

**DATA_SHRINE4**
Enum value (4) representing the state of the fourth shrine event.

**DATA_TWILIGHT_LORD_KELRIS**
Enum value (5) representing the state of the Twilight Lord Kelris boss encounter.

**DATA_SHRINE_OF_GELIHAST**
Enum value (6) representing the state of the Shrine of Gelihast event.

**DATA_ALTAR_OF_THE_DEEPS**
Enum value (7) representing the state of the Altar of the Deeps.

**DATA_MAINDOOR**
Enum value (8) representing the state of the main door.

**TYPE_KELRIS**
Enum value (10) categorizing the Twilight Lord Kelris encounter.

**TYPE_SHRINE**
Enum value (11) categorizing shrine-related encounters.

**TYPE_AQUANIS**
Enum value (12) categorizing the Baron Aquanis encounter.

**NPC_BARON_AQUANIS**
Enum value (12876) identifying the Baron Aquanis NPC.

**GO_FATHOM_STONE**
Enum value (177964) identifying the Fathom Stone game object.

**NPC_AKUMAI_SERVANT**
Enum value (4978) identifying the Akumai Servant NPC.

**NPC_AKUMAI_SNAPJAW**
Enum value (4825) identifying the Akumai Snapjaw NPC.

**NPC_MURKSHALLOW_SNAPCLAW**
Enum value (4815) identifying the Murkshallow Snapclaw NPC.

**NPC_MURKSHALLOW_SOFTSHELL**
Enum value (4977) identifying the Murkshallow Softshell NPC.

**GO_PORTAL_DOOR**
Enum value (21117) identifying the Portal Door game object.

**GO_SHRINE_1**
Enum value (21118) identifying the first Shrine game object.

**GO_SHRINE_2**
Enum value (21119) identifying the second Shrine game object.

**GO_SHRINE_3**
Enum value (21120) identifying the third Shrine game object.

**GO_SHRINE_4**
Enum value (21121) identifying the fourth Shrine game object.

**BFD_ENCOUNTER_KELRIS**
Enum value (0) indexing the Twilight Lord Kelris encounter.

**BFD_ENCOUNTER_SHRINE**
Enum value (1) indexing the shrine encounters.

**BFD_ENCOUNTER_AQUANIS**
Enum value (2) indexing the Baron Aquanis encounter.

**INSTANCE_BFD_MAX_ENCOUNTER**
Enum value (3) defining the maximum number of tracked encounters.

---

<!-- machine-true, projected from graph.json -->

## Map — blackfathom_deeps

*Source:* blackfathom_deeps.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|

---

<!-- verify: failed-members | invented: BFD_ENCOUNTER_AQUANIS, BFD_ENCOUNTER_KELRIS, BFD_ENCOUNTER_SHRINE, DATA_ALTAR_OF_THE_DEEPS, DATA_MAINDOOR, DATA_SHRINE1, DATA_SHRINE2, DATA_SHRINE3, DATA_SHRINE4, DATA_SHRINE_OF_GELIHAST, DATA_TWILIGHT_LORD_KELRIS, GO_FATHOM_STONE, GO_PORTAL_DOOR, GO_SHRINE_1, GO_SHRINE_2, GO_SHRINE_3, GO_SHRINE_4, INSTANCE_BFD_MAX_ENCOUNTER, NPC_AKUMAI_SERVANT, NPC_AKUMAI_SNAPJAW, NPC_BARON_AQUANIS, NPC_MURKSHALLOW_SNAPCLAW, NPC_MURKSHALLOW_SOFTSHELL, TYPE_AQUANIS, TYPE_KELRIS, TYPE_SHRINE -->
