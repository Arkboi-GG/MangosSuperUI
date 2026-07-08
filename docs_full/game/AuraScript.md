# AuraScript

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AuraScript

**Purpose & Responsibilities**

`AuraScript` is a hook-based interface struct defined in `ScriptMgr.h` that allows custom logic to intercept and modify the lifecycle of magical effects (auras) applied to units in the world. It serves as the extension point for server-side scripting systems (such as ScriptDev2 or custom modules) to override default game mechanics regarding spell durations, damage calculations, absorption behaviors, and periodic tick effects.

The struct itself contains only pure virtual methods with default empty implementations. It does not manage state, store data, or interact with databases directly. Instead, it acts as a callback container: when the core engine reaches specific points in an aura's life cycle, it queries the registered `AuraScript` instance for that spell and invokes the corresponding method. If a script implements a method, it can alter parameters (e.g., reducing damage, changing duration) or trigger side effects. If no script is registered or the method is not overridden, the default behavior (usually returning input values unchanged or doing nothing) proceeds.

This unit is part of the broader `ScriptMgr` infrastructure, which manages various script types (creature AI, gossip, quests, etc.). `AuraScript` specifically isolates spell-effect customization from other gameplay systems.

## Member-by-Member Behavior

The members of `AuraScript` are organized by the phase of the aura's lifecycle they intercept. All methods are virtual and provide default no-op or pass-through implementations, allowing scripts to override only the behaviors they care about.

### Initialization Hooks

These methods are called when an aura is first created or attached to a holder.

*   **`~AuraScript`**: The destructor. It is virtual and defaulted, ensuring proper cleanup if derived classes allocate resources. It performs no custom logic.
*   **`OnHolderInit`**: Called when a `SpellAuraHolder` is initialized. This occurs before individual `Aura` objects are fully constructed. It provides access to the `SpellAuraHolder` and the `WorldObject` caster (which may be null). Scripts can use this to set up context for the entire group of auras associated with a single cast.
*   **`OnAuraInit`**: Called immediately after an individual `Aura` object is constructed. This is the earliest point to inspect or modify the specific aura instance itself, before any modifiers are applied or durations calculated.

### Calculation Hooks

These methods allow scripts to modify numerical values associated with the aura, such as its power, duration, or periodic damage amounts.

*   **`OnAuraValueCalculate`**: Invoked whenever the aura's modifier value needs to be recalculated (e.g., due to stat changes or stacking). It receives the current calculated `value` and returns a potentially modified `int32`. Parameters include the `Aura`, `caster`, `target`, `SpellEntry`, `SpellEffectIndex`, and `castItem`. This is critical for spells whose power scales with stats or items.
*   **`OnDurationCalculate`**: Called during the initial calculation of the aura's duration. It takes the base `duration` and returns a modified `int32`. This allows scripts to extend or shorten buff/debuff lengths based on conditions like target level or caster stats. Note that the `target` parameter can be null for channel duration calculations.
*   **`OnPeriodicCalculateAmount`**: Specifically for periodic auras (ticks) that deal damage, healing, or resource drain. It modifies the `float& amount` of the next tick. This is distinct from `OnAuraValueCalculate` as it focuses solely on the recurring effect's magnitude.

### Application Hooks

These methods bracket the actual application of the aura's modifiers to the target unit.

*   **`OnBeforeApply`**: Called just before the aura's modifiers are applied to the target. It receives the `Aura` and a boolean `apply` indicating whether the aura is being added (`true`) or removed (`false`). Scripts can perform pre-application checks or setup.
*   **`OnAfterApply`**: Called immediately after the modifiers have been applied or removed. Like `OnBeforeApply`, it indicates the direction of the change. This is useful for triggering visual effects, sounds, or secondary logic once the state change is confirmed.

### Proc and Trigger Hooks

These methods handle conditional triggers, such as procs from equipment or spell effects.

*   **`OnCheckProc`**: Called during the evaluation of whether a proc should occur. It returns an `optional<SpellProcEventTriggerCheck>`. If the script returns a value, it overrides the default proc check logic. This allows scripts to enable or disable procs based on complex conditions not handled by the standard spell flags. Parameters include the owner, victim, holder, proc spell, and attack type.
*   **`OnProc`**: Called when a proc is triggered. It returns an `optional<SpellAuraProcResult>`. If a value is returned, it defines the outcome of the proc (e.g., success, failure, cooldown reset). This allows scripts to customize the result of a proc event, such as preventing the proc from consuming a charge or altering its effect.

### Absorption Hooks

These methods manage damage and mana absorption mechanics.

*   **`OnAbsorb`**: Called when the aura absorbs damage. It receives references to `currentAbsorb` (the amount absorbed this hit), `remainingDamage` (damage left after absorption), `dropCharge` (whether to consume a charge), and `damageType`. Scripts can modify these values to implement custom absorption logic, such as partial absorption or conditional charge consumption.
*   **`OnManaAbsorb`**: Similar to `OnAbsorb` but specifically for mana shields or mana-draining effects. It modifies `currentAbsorb` and `remainingDamage` for mana-related interactions.

### Periodic Tick Hooks

These methods control the behavior of auras that tick over time.

*   **`OnPeriodicTrigger`**: Called when a periodic spell effect is triggered (e.g., a dot tick). It allows scripts to modify the `spellInfo` pointer, effectively swapping the spell effect that is applied on the tick. This enables dynamic spell changes based on runtime conditions.
*   **`OnPeriodicDummy`**: Called for periodic auras with the "Dummy" effect type. These auras typically have no inherent mechanical effect but serve as markers or triggers for scripts. This method allows scripts to execute custom logic on each tick.
*   **`OnPeriodicTickEnd`**: Called at the end of a periodic tick cycle. This is a general hook for post-tick cleanup or logging.

### Area Aura Hooks

*   **`OnAreaAuraCheckTarget`**: Called for area-of-effect (AoE) auras to determine if a specific `Unit` is a valid target for the aura. It returns a boolean. Returning `false` prevents the aura from affecting that target, allowing scripts to implement custom targeting rules (e.g., excluding certain factions or classes).

## Cross-Unit Boundaries

`AuraScript` is a passive interface; it does not initiate calls to other units. Instead, it is invoked by core engine components when specific events occur. The following table details the callers from other units and the nature of the interaction.

| Member | Called By (Other Units) | Direction | Collaboration Details |
| :--- | :--- | :--- | :--- |
| `OnHolderInit` | `Unit.SpellAuras/SpellAuraHolder` | Inbound | `SpellAuraHolder` initializes the holder object and calls this hook to allow scripts to set up context for the aura group. |
| `OnAuraInit` | `Unit.SpellAuras/CreateAura` | Inbound | `CreateAura` constructs the `Aura` object and calls this hook to allow scripts to initialize the specific aura instance. |
| `OnAuraValueCalculate` | `Unit.SpellAuras/Aura`, `Unit.SpellAuras/SetStackAmount` | Inbound | `Aura` and `SetStackAmount` invoke this to recalculate the aura's power. The script modifies the return value, which is then used by the caller. |
| `OnDurationCalculate` | `SpellEntry/CalculateDuration` | Inbound | `CalculateDuration` uses this hook to adjust the base duration before applying it to the aura. |
| `OnBeforeApply` | `Unit.SpellAuras/ApplyModifier` | Inbound | `ApplyModifier` calls this before modifying the target's stats. Scripts can perform pre-checks. |
| `OnAfterApply` | `Unit.SpellAuras/ApplyModifier` | Inbound | `ApplyModifier` calls this after modifying the target's stats. Scripts can trigger side effects. |
| `OnCheckProc` | `Unit.AuraProcHandler/IsTriggeredAtSpellProcEvent` | Inbound | `IsTriggeredAtSpellProcEvent` evaluates proc conditions. If the script returns a value, it overrides the default check. |
| `OnProc` | `Unit.Main/HandleTriggers` | Inbound | `HandleTriggers` executes the proc. The script's return value determines the proc's outcome. |
| `OnAbsorb` | `Unit.Main/CalculateDamageAbsorbAndResist` | Inbound | `CalculateDamageAbsorbAndResist` invokes this to handle damage absorption. The script modifies the absorb/damage values passed by reference. |
| `OnManaAbsorb` | `Unit.Main/CalculateDamageAbsorbAndResist` | Inbound | Same as `OnAbsorb`, but for mana-related absorption. |
| `OnPeriodicCalculateAmount` | `Unit.SpellAuras/PeriodicTick` | Inbound | `PeriodicTick` calls this to determine the amount of damage/healing for the current tick. |
| `OnPeriodicTrigger` | `Unit.SpellAuras/TriggerSpell` | Inbound | `TriggerSpell` invokes this to allow scripts to swap the spell effect applied on the tick. |
| `OnPeriodicDummy` | `Unit.SpellAuras/PeriodicDummyTick` | Inbound | `PeriodicDummyTick` calls this to execute custom logic for dummy periodic auras. |
| `OnPeriodicTickEnd` | `Unit.SpellAuras/PeriodicTick` | Inbound | `PeriodicTick` calls this at the end of the tick cycle for cleanup or logging. |
| `OnAreaAuraCheckTarget` | `Unit.SpellAuras/Update` | Inbound | `Update` checks potential targets for AoE auras. The script's boolean return value determines if the target is valid. |

Note: `AuraScript` does not call out to any other units. It is purely a callback interface.

## Data Model

`AuraScript` does not interact with any database tables. It operates entirely in memory, using data passed to it by the calling engine components (such as `Aura`, `Unit`, `SpellEntry`, etc.). No SQL queries or table references are present in this unit.

## Notable Implementation Details

1.  **Default Pass-Through Behavior**: Every method in `AuraScript` has a default implementation that either does nothing (void methods) or returns the input value unchanged (calculation methods). This ensures that if a script does not override a specific hook, the game behaves normally. For example, `OnAuraValueCalculate` returns the `value` parameter as-is, and `OnDurationCalculate` returns the `duration` parameter as-is.

2.  **Optional Return Types for Procs**: The methods `OnCheckProc` and `OnProc` return `optional<...>` types. This design allows scripts to explicitly signal whether they want to override the default behavior. If the script returns an empty optional (the default), the engine proceeds with its standard proc logic. If it returns a value, the engine uses that value instead. This provides fine-grained control without requiring scripts to reimplement the entire proc system.

3.  **Reference Parameters for Modifications**: Methods like `OnAbsorb`, `OnManaAbsorb`, and `OnPeriodicCalculateAmount` take parameters by reference (e.g., `int32& currentAbsorb`, `float& amount`). This allows scripts to directly modify the values that will be used by the caller, rather than returning new values. This is efficient and clear for simple modifications.

4.  **Null Pointer Safety**: Several methods note that parameters like `caster` or `target` can be null. For instance, `OnHolderInit` states "caster can be nullptr," and `OnDurationCalculate` notes "target can be nullptr for channel duration calculation." Scripts overriding these methods must handle null pointers appropriately to avoid crashes.

5.  **No State Management**: `AuraScript` itself holds no state. Any persistent data needed across hooks must be managed by the derived script class or stored elsewhere (e.g., in the `Aura` object's custom data fields if supported by the engine). This keeps the interface lightweight and decoupled from specific storage mechanisms.

6.  **Virtual Destructor**: The destructor is virtual, which is crucial for polymorphic deletion. Derived classes that allocate resources must ensure their destructors are called correctly when deleted through a base `AuraScript*` pointer.

## Member Reference

*   **`~AuraScript`**: Virtual destructor. Defaulted. Ensures proper cleanup for derived classes.
*   **`OnHolderInit`**: Virtual method. Called on `SpellAuraHolder` creation. Allows scripts to initialize context for the aura group. Parameters: `SpellAuraHolder*`, `WorldObject*` (caster).
*   **`OnAuraInit`**: Virtual method. Called after `Aura` construction. Allows scripts to initialize the specific aura instance. Parameter: `Aura*`.
*   **`OnAuraValueCalculate`**: Virtual method. Calculates aura modifier amount. Returns modified `int32`. Parameters: `Aura*`, `Unit*` (caster), `Unit*` (target), `SpellEntry const*`, `SpellEffectIndex`, `Item*`, `int32` (value).
*   **`OnDurationCalculate`**: Virtual method. Calculates aura duration. Returns modified `int32`. Parameters: `WorldObject const*` (caster), `Unit const*` (target), `int32` (duration).
*   **`OnBeforeApply`**: Virtual method. Called before applying/removing aura modifiers. Parameters: `Aura*`, `bool` (apply).
*   **`OnAfterApply`**: Virtual method. Called after applying/removing aura modifiers. Parameters: `Aura*`, `bool` (apply).
*   **`OnCheckProc`**: Virtual method. Checks proc eligibility. Returns `optional<SpellProcEventTriggerCheck>`. Parameters: `Unit const*` (owner), `Unit*` (victim), `SpellAuraHolder*`, `SpellEntry const*`, `uint32` (procFlag), `uint32` (procExtra), `WeaponAttackType`, `bool` (isVictim).
*   **`OnProc`**: Virtual method. Handles proc execution. Returns `optional<SpellAuraProcResult>`. Parameters: `Unit*` (owner), `Unit*` (victim), `uint32` (amount), `int32` (originalAmount), `Aura*` (triggeredByAura), `SpellEntry const*`, `uint32` (procFlag), `uint32` (procEx), `uint32` (cooldown).
*   **`OnAbsorb`**: Virtual method. Handles damage absorption. Modifies `currentAbsorb`, `remainingDamage`, `dropCharge` by reference. Parameters: `Aura*`, `int32&`, `int32&`, `bool&`, `DamageEffectType`.
*   **`OnManaAbsorb`**: Virtual method. Handles mana absorption. Modifies `currentAbsorb`, `remainingDamage` by reference. Parameters: `Aura*`, `int32&`, `int32&`.
*   **`OnPeriodicCalculateAmount`**: Virtual method. Calculates periodic tick amount. Modifies `amount` by reference. Parameters: `Aura*`, `float&`.
*   **`OnPeriodicTrigger`**: Virtual method. Triggers periodic spell effect. Can modify `spellInfo`. Parameters: `Aura*`, `Unit*` (caster), `Unit*` (target), `WorldObject*` (targetObject), `SpellEntry const*&`.
*   **`OnPeriodicDummy`**: Virtual method. Handles periodic dummy aura ticks. Parameter: `Aura*`.
*   **`OnPeriodicTickEnd`**: Virtual method. Called at end of periodic tick. Parameter: `Aura*`.
*   **`OnAreaAuraCheckTarget`**: Virtual method. Checks if target is valid for AoE aura. Returns `bool`. Parameters: `Aura const*`, `Unit*` (target).

---

<!-- machine-true, projected from graph.json -->

## Map — AuraScript

*Source:* ScriptMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ~AuraScript | dtor | — | — | — |
| OnHolderInit | method | — | Unit.SpellAuras/SpellAuraHolder | — |
| OnAuraInit | method | — | Unit.SpellAuras/CreateAura | — |
| OnAuraValueCalculate | method | — | Unit.SpellAuras/Aura, Unit.SpellAuras/SetStackAmount | — |
| OnDurationCalculate | method | — | SpellEntry/CalculateDuration | — |
| OnBeforeApply | method | — | Unit.SpellAuras/ApplyModifier | — |
| OnAfterApply | method | — | Unit.SpellAuras/ApplyModifier | — |
| OnCheckProc | method | — | Unit.AuraProcHandler/IsTriggeredAtSpellProcEvent | — |
| OnProc | method | — | Unit.Main/HandleTriggers | — |
| OnAbsorb | method | — | Unit.Main/CalculateDamageAbsorbAndResist | — |
| OnManaAbsorb | method | — | Unit.Main/CalculateDamageAbsorbAndResist | — |
| OnPeriodicCalculateAmount | method | — | Unit.SpellAuras/PeriodicTick | — |
| OnPeriodicTrigger | method | — | Unit.SpellAuras/TriggerSpell | — |
| OnPeriodicDummy | method | — | Unit.SpellAuras/PeriodicDummyTick | — |
| OnPeriodicTickEnd | method | — | Unit.SpellAuras/PeriodicTick | — |
| OnAreaAuraCheckTarget | method | — | Unit.SpellAuras/Update | — |
