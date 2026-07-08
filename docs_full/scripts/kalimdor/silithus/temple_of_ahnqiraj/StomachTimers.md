# StomachTimers

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# StomachTimers

**Purpose & Responsibilities**

`StomachTimers` is a private nested structure within `instance_temple_of_ahnqiraj` (defined in `temple_of_ahnqiraj.h`) that encapsulates the state required to manage individual players trapped inside the "Stomach of C'Thun" encounter phase. It tracks timing for environmental hazards (digestive acid), movement constraints (knockback status), and transition timestamps (entry and exit from the stomach). This data is stored per-player in the `playersInStomach` vector of the instance script, allowing the instance manager to update hazard timers and enforce mechanics for multiple players simultaneously.

The structure contains no methods other than its constructor, meaning all logic regarding how these fields are updated resides in the calling unit (`instance_temple_of_ahnqiraj`).

## Member-by-Member Behavior

### **StomachTimers** (Constructor)
This default constructor initializes the four member variables to their safe starting states for a player who has just been added to the stomach list:
*   **acidDebuff**: Initialized to `StomachTimers::ACID_REFRESH_RATE` (5000 ms). This ensures the first check for the digestive acid debuff occurs after 5 seconds.
*   **timeSincePortedFromStomach**: Initialized to `0`. Tracks how long ago the player exited the stomach (if applicable).
*   **timeSincePortedToStomach**: Initialized to `0`. Tracks how long the player has been inside the stomach.
*   **didKnockback**: Initialized to `false`. Indicates whether the player has already suffered the initial knockback effect upon entering or during the encounter.

## Cross-Unit Boundaries

*   **Called by `instance_temple_of_ahnqiraj/AddPlayerToStomach`**:
    The `AddPlayerToStomach` method in `instance_temple_of_ahnqiraj` creates a new `StomachTimers` instance for a player entering the C'Thun stomach phase. It constructs this object to reset the player's state before pushing the pair `{PlayerGuid, StomachTimers}` onto the `playersInStomach` vector. This establishes the baseline timing for acid ticks and knockback eligibility for that specific player.

## Data Model

This unit does not interact directly with any database tables. It operates entirely on in-memory state managed by the `instance_temple_of_ahnqiraj` script.

## Notable Implementation Details

*   **Static Constants**: The structure defines two static constants used for timing logic:
    *   `PUNT_CAST_TIME` (3000 ms): Likely used to throttle or schedule the "punt" upward knockback mechanic.
    *   `ACID_REFRESH_RATE` (5000 ms): Defines the interval at which the digestive acid debuff is applied or refreshed on players inside the stomach.
*   **State Reset**: Because `StomachTimers` is reconstructed every time a player is added to the stomach (via `AddPlayerToStomach`), it inherently resets all state. There is no persistence of previous stomach visits within this structure; if a player re-enters, their `didKnockback` flag is reset to `false`, implying they will be subjected to the initial knockback again.
*   **Memory Layout**: As a simple aggregate of primitive types (`uint32` and `bool`), it is lightweight and suitable for storage in a `std::vector` that is iterated over frequently during the instance's `Update` loop (specifically in `UpdateStomachOfCthun`, which is not part of this unit but consumes this data).

## Member Reference

**StomachTimers**
Default constructor for the `StomachTimers` structure. Initializes `acidDebuff` to `ACID_REFRESH_RATE` (5000), `timeSincePortedFromStomach` and `timeSincePortedToStomach` to 0, and `didKnockback` to `false`. Called by `instance_temple_of_ahnqiraj/AddPlayerToStomach` when a player enters the C'Thun stomach phase.

---

<!-- machine-true, projected from graph.json -->

## Map — StomachTimers

*Source:* temple_of_ahnqiraj.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| StomachTimers | ctor | — | instance_temple_of_ahnqiraj/AddPlayerToStomach | — |
