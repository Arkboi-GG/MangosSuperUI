# DiminishingReturn

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Unit.h — `DiminishingReturn`

## Purpose & Responsibilities

The `DiminishingReturn` struct is a lightweight data container within the `Unit` class hierarchy of the WoWVMaNGOS server emulation. Its sole responsibility is to store the state required to implement **Diminishing Returns (DR)** mechanics for crowd control (CC) spells and effects.

In World of Warcraft, repeatedly applying certain crowd control effects (such as stuns, fears, or silences) to the same target results in progressively shorter durations for subsequent applications. This mechanic prevents players or NPCs from locking down a target indefinitely using a sequence of CC abilities. The `DiminishingReturn` struct tracks the timing and frequency of these applications for a specific `DiminishingGroup` (e.g., Stun, Fear, Silence) on a specific `Unit`.

This unit contains no executable logic itself; it is purely a data structure. All behavioral logic regarding how this data is queried, updated, and applied to spell durations resides in the `Unit` class methods (`GetDiminishing`, `IncrDiminishing`, `ApplyDiminishingToDuration`, `ApplyDiminishingAura`) declared in `Unit.h` but implemented in other translation units (likely `Unit.cpp` or `Spell.cpp`).

## Member-by-Member Behavior

The `DiminishingReturn` struct defines one constructor and four member variables.

### Constructor: `DiminishingReturn`

```cpp
DiminishingReturn(DiminishingGroup group, uint32 t, uint32 count)
    : DRGroup(group), stack(0), hitTime(t), hitCount(count)
{}
```

*   **Purpose**: Initializes a new diminishing return tracking record.
*   **Parameters**:
    *   `group`: The `DiminishingGroup` identifier (e.g., Stun, Fear) that this record tracks.
    *   `t`: The timestamp (in milliseconds) of the initial hit.
    *   `count`: The initial hit count (typically 1).
*   **Behavior**:
    *   Assigns `group` to the `DRGroup` bitfield.
    *   Initializes `stack` to `0`. The comment in the source suggests this field is modified by `Unit::ApplyDiminishingAura`, likely to track the number of active auras contributing to this DR group or similar stacking logic.
    *   Assigns `t` to `hitTime`, marking when the first effect in this DR cycle occurred.
    *   Assigns `count` to `hitCount`, initializing the counter for how many times this DR group has been triggered.

### Member Variables

#### `DRGroup`
*   **Type**: `DiminishingGroup` (stored as a 16-bit bitfield).
*   **Role**: Identifies which category of crowd control this record applies to. Different groups (e.g., Stun vs. Fear) are tracked independently. A unit can have multiple `DiminishingReturn` instances, one for each active DR group.

#### `stack`
*   **Type**: `uint16` (stored as a 16-bit bitfield).
*   **Role**: Tracks an internal stack count. While the primary DR logic relies on `hitCount` and `hitTime`, this field is noted in comments as being modified by `Unit::ApplyDiminishingAura`. It likely serves to track the number of concurrent aura instances affecting this DR group or to manage removal logic when auras expire.

#### `hitTime`
*   **Type**: `uint32`.
*   **Role**: Records the timestamp (in milliseconds) of the most recent hit that contributed to this DR group.
*   **Logic Context**: As described in the source comments, if the time elapsed since `hitTime` exceeds 15 seconds (15,000 ms), the diminishing return effect resets. Specifically, `hitCount` is reset to `DIMINISHING_LEVEL_1`, meaning the next application of the CC effect will have its full duration. This implements the "decay" aspect of diminishing returns.

#### `hitCount`
*   **Type**: `uint32`.
*   **Role**: Records the cumulative number of times a spell/effect from this `DiminishingGroup` has successfully hit the target within the current decay window.
*   **Logic Context**: This value directly determines the severity of the diminishing return. Higher `hitCount` values correspond to higher `DiminishingLevels` (e.g., `DIMINISHING_LEVEL_2`, `DIMINISHING_LEVEL_IMMUNE`), which result in shorter spell durations or complete immunity, as calculated by `Unit::ApplyDiminishingToDuration`.

## Cross-Unit Boundaries

The `DiminishingReturn` struct is tightly coupled with the `Unit` class. It does not call out to other units, nor is it directly called by external units. Instead, it is managed internally by the `Unit` class.

*   **Storage**: Instances of `DiminishingReturn` are stored in the `std::vector<DiminishingReturn> m_Diminishing` member variable of the `Unit` class.
*   **Interaction Direction**:
    *   **Creation**: `Unit` methods (specifically `IncrDiminishing` or similar initialization logic) construct `DiminishingReturn` objects and push them into `m_Diminishing`.
    *   **Reading**: `Unit::GetDiminishing` iterates over `m_Diminishing` to find the relevant `DiminishingReturn` instance for a given `DiminishingGroup` and reads its `hitCount` and `hitTime` to determine the current DR level.
    *   **Updating**: `Unit::IncrDiminishing` updates the `hitCount` and `hitTime` of the existing `DiminishingReturn` instance.
    *   **Application**: `Unit::ApplyDiminishingToDuration` uses the data from `DiminishingReturn` to calculate the reduced duration for incoming spells.
    *   **Removal/Decay**: `Unit::ApplyDiminishingAura` and potentially `Unit::ClearDiminishings` manage the lifecycle of these structs, removing them when they are no longer needed or resetting their state.

## Data Model

The `DiminishingReturn` struct does not interact with any database tables. It is a transient, in-memory data structure representing the runtime state of a game entity (`Unit`). No SQL queries or table references are present in this unit.

## Notable Implementation Details

1.  **Bitfield Packing**: The `DRGroup` and `stack` fields are defined as bitfields (`:16`). This packs two 16-bit values into a single 32-bit word (assuming standard alignment/packing), optimizing memory usage for the `std::vector<DiminishingReturn>` stored in each `Unit`. Given that units may track multiple DR groups, this optimization reduces the memory footprint of the `m_Diminishing` vector.
2.  **15-Second Decay Window**: The logic for resetting DR is hardcoded to a 15-second window (15,000 ms). If `hitTime` is older than 15 seconds relative to the current time, the DR level resets. This is a critical gameplay balance parameter embedded in the design of this struct's usage.
3.  **No Internal Logic**: The struct contains no methods other than the constructor. All logic for finding, updating, and interpreting the DR state is externalized to the `Unit` class. This separation of concerns keeps the data structure simple and allows the `Unit` class to handle complex iteration and state management.
4.  **Stack Field Ambiguity**: The `stack` field is initialized to 0 in the constructor but its exact semantic meaning is less clear than `hitCount`. The comment indicates it is modified by `Unit::ApplyDiminishingAura`. Maintainers should refer to the implementation of `Unit::ApplyDiminishingAura` to understand how `stack` influences DR behavior, as it may relate to aura stacking or removal order rather than the primary DR level calculation.

## Member Reference

**DiminishingReturn**
Constructor for the `DiminishingReturn` struct. Initializes the `DRGroup` with the specified `DiminishingGroup`, sets `stack` to 0, records the initial hit timestamp in `hitTime`, and sets the initial hit count in `hitCount`. This struct is used by the `Unit` class to track diminishing returns for crowd control effects.

---

<!-- machine-true, projected from graph.json -->

## Map — DiminishingReturn

*Source:* Unit.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| DiminishingReturn | ctor | — | — | — |
