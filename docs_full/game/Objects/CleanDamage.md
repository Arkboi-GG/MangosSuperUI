# CleanDamage

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# CleanDamage

`CleanDamage` is a lightweight aggregate struct defined in `SpellCaster.h` that encapsulates the final, resolved statistics of a melee damage event after all defensive mechanics (absorption, resistance, and hit outcome determination) have been applied. It serves as a standardized data carrier passed by reference from the damage calculation subsystem to the damage application and logging subsystems within the `Unit` class hierarchy.

## Purpose & Responsibilities

The primary responsibility of `CleanDamage` is to decouple the complex logic of determining *how much* damage lands and *what kind* of hit occurred from the logic of *applying* that damage to a target's health pool and notifying clients.

In the context of the WoWVMaNGOS engine, melee combat involves multiple stages:
1.  **Rolling:** Determining if the attack hits, misses, dodges, parries, blocks, or glances.
2.  **Calculation:** Computing raw damage based on weapon stats, armor, and modifiers.
3.  **Mitigation:** Applying absorption effects (e.g., Ice Block, Shield Wall) and elemental resistances.
4.  **Application:** Reducing the target's health and triggering combat logs.

`CleanDamage` exists at the boundary between stage 3 and stage 4. It holds the "clean" numbers—the final values that should be subtracted from health and sent to the client—ensuring that the application logic does not need to re-evaluate mitigation or hit outcomes.

## Member-by-Member Behavior

The struct contains five public data members and one constructor. There are no methods beyond the constructor.

### Constructor: `CleanDamage`

**Signature:**
```cpp
CleanDamage(uint32 _damage, WeaponAttackType _attackType, MeleeHitOutcome _hitOutCome, uint32 _Absorb, int32 _Resist)
```

**Behavior:**
This constructor initializes all five fields of the struct. It is the sole way to instantiate a `CleanDamage` object. The parameters correspond directly to the fields:
*   `_damage`: The final damage value to be dealt to the target's health.
*   `_attackType`: Identifies whether the attack was a main-hand (`BASE_ATTACK`) or off-hand (`OFFHAND_ATTACK`) strike. This is crucial for cooldown tracking, threat generation, and specific aura triggers.
*   `_hitOutCome`: An enum value from `MeleeHitOutcome` indicating the result of the combat roll (e.g., `MELEE_HIT_NORMAL`, `MELEE_HIT_CRIT`, `MELEE_HIT_BLOCK`). This determines visual effects and sound cues.
*   `_Absorb`: The amount of damage absorbed by protective effects. This is informational for logging purposes; the `damage` field already reflects the reduction caused by absorption.
*   `_Resist`: The amount of damage resisted by elemental resistance stats. Like absorption, this is informational for logging; the `damage` field already accounts for this reduction.

### Data Members

*   **`uint32 damage`**: The net damage value. This is the amount subtracted from the target's current health. It is guaranteed to be non-negative.
*   **`WeaponAttackType attackType`**: Enumerates the weapon slot used. Defined elsewhere in the codebase, typically distinguishing between main hand and off hand.
*   **`MeleeHitOutcome hitOutCome`**: Enumerates the nature of the hit. Defined in `SpellCaster.h` alongside `CleanDamage`. Values include `MELEE_HIT_EVADE`, `MELEE_HIT_MISS`, `MELEE_HIT_DODGE`, `MELEE_HIT_BLOCK`, `MELEE_HIT_PARRY`, `MELEE_HIT_GLANCING`, `MELEE_HIT_CRIT`, `MELEE_HIT_CRUSHING`, `MELEE_HIT_NORMAL`, and `MELEE_HIT_BLOCK_CRIT`.
*   **`uint32 absorb`**: The quantity of damage prevented by absorb shields. Used primarily for generating combat log entries (`SMSG_COMBAT_LOG_EVENT`) so clients can display "X damage absorbed."
*   **`int32 resist`**: The quantity of damage prevented by resistance stats. Note that this is signed (`int32`), likely to accommodate potential negative resistance values in edge cases or internal calculations, though logically it represents a positive reduction in damage taken. Used for combat log entries showing "Y damage resisted."

## Cross-Unit Boundaries

`CleanDamage` acts as a bridge between the calculation logic in `Unit` and the application logic in `Unit`. It is constructed in one part of the `Unit` class and consumed in another.

### Called By (Consumers)

The following members in other units construct and pass `CleanDamage` instances to apply damage:

1.  **`Unit.Main/CalculateDamageAbsorbAndResist`** (`Unit.cpp`):
    *   **Direction:** Outbound from `Unit.Main` to `CleanDamage` (construction).
    *   **Context:** This method is responsible for the final mitigation step. After raw damage is calculated, it queries the target for active absorb auras and resistance values. It then constructs a `CleanDamage` object containing the reduced damage, the amount absorbed, and the amount resisted. This object is returned or passed to the next stage.

2.  **`Unit.Main/DealMeleeDamage`** (`Unit.cpp`):
    *   **Direction:** Inbound to `Unit.Main` (consumption).
    *   **Context:** This is the core melee damage dealer. It receives a `CleanDamage` pointer (often obtained via `CalculateDamageAbsorbAndResist`). It uses the `damage` field to reduce the target's health, the `hitOutCome` to determine if critical hit effects should trigger, and the `attackType` to handle weapon-specific logic. It also uses the `absorb` and `resist` fields to send appropriate combat log messages to clients.

3.  **`SpellCaster/DealSpellDamage`** (`SpellCaster.cpp`):
    *   **Direction:** Inbound to `SpellCaster` (consumption).
    *   **Context:** While `CleanDamage` is primarily associated with melee, spell damage that mimics melee mechanics or requires similar logging structures may utilize this struct. `DealSpellDamage` processes the `SpellNonMeleeDamage` struct, but in certain hybrid cases or internal refactoring paths, `CleanDamage` might be used to standardize the output for logging. *Correction based on Map:* The map explicitly lists `SpellCaster/DealSpellDamage` as calling `CleanDamage`. This implies `DealSpellDamage` constructs a `CleanDamage` object to pass to downstream functions or to standardize the data before sending logs.

4.  **`Unit.SpellAuras/PeriodicTick`** (`Unit.cpp`):
    *   **Direction:** Inbound to `Unit.SpellAuras` (consumption).
    *   **Context:** Periodic damage effects (DoTs) often need to report damage in a format compatible with the combat log. `PeriodicTick` may construct a `CleanDamage` instance to ensure that DoT damage is logged with consistent fields (damage, absorb, resist) even though it doesn't involve a melee swing.

### Calls Out

`CleanDamage` itself does not call out to any other units. It is a passive data structure.

## Data Model

`CleanDamage` does not interact directly with any database tables. It is an in-memory runtime structure. No SQL queries are executed by this unit.

## Notable Implementation Details

1.  **Pass-by-Pointer Semantics:** In the callers listed in the map (e.g., `DealMeleeDamage`), `CleanDamage` is typically passed as a pointer (`CleanDamage const*`). This allows the caller to optionally provide pre-calculated damage data or `nullptr` if the damage needs to be calculated inline. Maintainers must ensure that if a `nullptr` is passed, the receiving function handles it gracefully (usually by calculating the damage internally).

2.  **Separation of Concerns:** The existence of `CleanDamage` highlights a deliberate architectural choice to separate *calculation* from *application*. Before this struct, damage application might have required re-querying auras for absorb amounts. Now, the calculation phase (`CalculateDamageAbsorbAndResist`) does the heavy lifting of iterating over auras, and `CleanDamage` carries the result. This improves performance by avoiding redundant aura iterations during the actual health subtraction.

3.  **Signed Resistance:** The `resist` field is `int32`. While resistance is conceptually a positive reduction, using a signed integer allows for flexibility in internal math (e.g., if a buff temporarily reduces resistance below zero, or if the calculation involves subtractions that might transiently go negative before clamping). However, in practice, the value passed to the constructor is usually the absolute amount resisted.

4.  **Hit Outcome Granularity:** The `MeleeHitOutcome` enum includes specific outcomes like `MELEE_HIT_BLOCK_CRIT` and `MELEE_HIT_GLANCING`. These are distinct from simple misses or normal hits. `CleanDamage` preserves this granularity, allowing the combat log to accurately reflect whether a block was critical or if a hit was glancing (reduced damage due to level difference). This precision is vital for accurate threat calculation and player feedback.

5.  **No Validation:** The constructor performs no validation. It assumes the caller has already determined valid values for `damage`, `absorb`, and `resist`. If invalid values (e.g., negative damage) are passed, the resulting behavior in `DealMeleeDamage` is undefined and likely to cause bugs (such as healing the target instead of damaging them).

## Member Reference

**CleanDamage**
Constructor for the `CleanDamage` struct. Initializes the `damage`, `attackType`, `hitOutCome`, `absorb`, and `resist` fields with the provided arguments. It is called by `Unit.Main/CalculateDamageAbsorbAndResist`, `Unit.Main/DealMeleeDamage`, `SpellCaster/DealSpellDamage`, and `Unit.SpellAuras/PeriodicTick` to package finalized damage statistics for consumption by damage application and logging routines.

---

<!-- machine-true, projected from graph.json -->

## Map — CleanDamage

*Source:* SpellCaster.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| CleanDamage | ctor | — | SpellCaster/DealSpellDamage, Unit.Main/CalculateDamageAbsorbAndResist, Unit.Main/DealMeleeDamage, Unit.SpellAuras/PeriodicTick | — |
