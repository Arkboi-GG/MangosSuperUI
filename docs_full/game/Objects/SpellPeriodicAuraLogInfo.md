# SpellPeriodicAuraLogInfo

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Unit: `SpellPeriodicAuraLogInfo`

## Purpose & Responsibilities

`SpellPeriodicAuraLogInfo` is a lightweight aggregate structure defined in `Unit.h` within the `wowvmangos` codebase. Its sole responsibility is to encapsulate the data required to generate a combat log message for periodic spell effects (such as damage-over-time or healing-over-time ticks).

It acts as a transient data carrier, bundling the source `Aura`, the calculated `damage`, `absorb`, `resist`, and a `multiplier` into a single object. This allows the calling code to pass complex combat resolution results to the network logging system without managing multiple disparate arguments.

## Member-by-Member Behavior

The unit consists of a single constructor and five public data members.

### Constructor: `SpellPeriodicAuraLogInfo`

**Signature:**
```cpp
SpellPeriodicAuraLogInfo(Aura* _aura, uint32 _damage, uint32 _absorb, int32 _resist, float _multiplier)
```

**Behavior:**
This constructor initializes all five member variables using an initializer list. It performs no validation, side effects, or memory allocation. It simply maps the input parameters to the corresponding fields:
*   `_aura` → `aura`
*   `_damage` → `damage`
*   `_absorb` → `absorb`
*   `_resist` → `resist`
*   `_multiplier` → `multiplier`

### Data Members

*   **`Aura* aura`**: A pointer to the `Aura` instance responsible for the periodic tick. This identifies the spell effect in the combat log.
*   **`uint32 damage`**: The final amount of damage dealt by the tick.
*   **`uint32 absorb`**: The amount of damage absorbed by shields or similar effects during the tick.
*   **`int32 resist`**: The amount of damage resisted by the target's resistances. Note that this is signed (`int32`), likely to accommodate potential negative resistance values or specific calculation intermediates, though standard resistance is typically non-negative.
*   **`float multiplier`**: A floating-point multiplier applied to the base effect. This is often used for critical strike multipliers or other percentage-based modifiers relevant to the log entry.

## Cross-Unit Boundaries

### Called By

The constructor is invoked by two distinct subsystems within the `Unit` class hierarchy, as indicated by the MAP:

1.  **`Unit.SpellAuras`**: Likely called during the general application or refresh of spell auras where immediate logging of the initial application or a specific tick is required.
2.  **`Unit.PeriodicTick`**: This is the primary consumer. During the server's update loop, periodic auras (DoTs/HoTs) tick. When a tick occurs, the damage/healing is calculated, and a `SpellPeriodicAuraLogInfo` object is constructed to send the result to the client via the combat log system.

### Calls Out

This unit does not call out to any other units. It is a pure data structure with no behavioral dependencies.

## Data Model

This unit does not interact with any database tables. It operates entirely in memory during the runtime processing of spell effects.

## Notable Implementation Details

1.  **Aggregate Structure**: `SpellPeriodicAuraLogInfo` is a Plain Old Data (POD)-like structure. It has no virtual functions, no private members, and no custom destructors. This makes it cheap to construct and copy, which is appropriate for high-frequency operations like periodic spell ticks.
2.  **Pointer Ownership**: The `aura` member is a raw pointer (`Aura*`). The `SpellPeriodicAuraLogInfo` instance does not take ownership of the `Aura`. It is crucial that the `Aura` object remains valid for the duration of the logging operation. Since this structure is typically constructed and immediately passed to a logging function (e.g., `SendPeriodicAuraLog` in `Unit`), the lifetime management is handled by the caller, ensuring the `Aura` is not destroyed before the log packet is sent.
3.  **Signed Resistance**: The use of `int32` for `resist` instead of `uint32` is a notable design choice. While resistance usually reduces damage (positive value), certain game mechanics or calculation bugs might theoretically produce negative resistance (increasing damage). Using a signed integer prevents overflow/underflow issues in these edge cases.
4.  **No Validation**: The constructor accepts any values, including null pointers for `aura` or negative values for damage/absorb (though `uint32` prevents negative storage, the input parameter is `uint32`, so negative inputs would wrap around). The integrity of the data relies entirely on the correctness of the calling code in `Unit.SpellAuras` and `Unit.PeriodicTick`.

## Member Reference

**SpellPeriodicAuraLogInfo**
Constructor that initializes the `aura`, `damage`, `absorb`, `resist`, and `multiplier` fields from the provided arguments. It is called by `Unit.SpellAuras` and `Unit.PeriodicTick` to prepare data for combat log messages regarding periodic spell effects.

---

<!-- machine-true, projected from graph.json -->

## Map — SpellPeriodicAuraLogInfo

*Source:* Unit.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| SpellPeriodicAuraLogInfo | ctor | — | Unit.SpellAuras/PeriodicTick | — |
