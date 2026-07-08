# scarlet_monastery

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# scarlet_monastery

## Purpose & Responsibilities

`scarlet_monastery.h` is a header-only definition unit that establishes the enumeration constants used by the Scarlet Monastery instance script within the WoWVMaNGOS codebase. It does not contain executable logic, classes, or functions. Its sole responsibility is to define the symbolic identifiers for encounter types, boss data IDs, and door states required by the instance script implementation (which resides in other translation units, such as `instance_scarlet_monastery.cpp`).

By centralizing these numeric constants in a dedicated header, the codebase ensures consistency between the instance script logic and any other components that might query or manipulate the instance state via these specific IDs.

## Data Model

This unit does not interact with any database tables. It contains no SQL queries, table references, or schema dependencies.

## Notable Implementation Details

- **Header Guard**: The file uses `DEF_SCARLETM_H` as its include guard.
- **Enum Scope**: The enumerators are defined in the global namespace within an unnamed enum block. This means they are accessible directly by name (e.g., `DATA_MOGRAINE`) in any file that includes this header, without requiring a scope qualifier like `ScarletMonastery::`.
- **Encounter Count**: `INSTANCE_SM_MAX_ENCOUNTER` is set to `2`, indicating that the instance script tracks two primary encounter events.
- **Event vs. Data IDs**: The enum distinguishes between "Event" types (`TYPE_...`) and "Data" IDs (`DATA_...`).
    - `TYPE_MOGRAINE_AND_WHITE_EVENT` (1) and `TYPE_ASHBRINGER_EVENT` (2) likely correspond to the instance encounter map entries used for progress tracking or UI display.
    - `DATA_MOGRAINE` (2), `DATA_WHITEMANE` (3), `DATA_VORREL` (5) likely correspond to specific boss GUIDs or state flags stored in the instance data array.
    - `DATA_DOOR_WHITEMANE` (4) and `DATA_DOOR_CHAPEL` (6) likely correspond to GUIDs or state flags for specific doors within the instance.
- **Gap in Enumeration**: Note that value `4` is assigned to `DATA_DOOR_WHITEMANE`, while `DATA_WHITEMANE` is `3` and `DATA_VORREL` is `5`. There is no enumerator for value `4` in the `DATA_*` sequence other than the door, suggesting the instance data array indices are not strictly contiguous for boss data alone, or that door states are interleaved with boss states in the instance memory layout.

## Member Reference

The MAP for this unit lists no members. Consequently, there are no entries to document in this section.

---

<!-- machine-true, projected from graph.json -->

## Map — scarlet_monastery

*Source:* scarlet_monastery.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
