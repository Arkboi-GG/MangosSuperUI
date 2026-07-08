# SpellScript

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# SpellScript

**Purpose & Responsibilities**

`SpellScript` is a base interface class defined in `ScriptMgr.h` that allows custom C++ logic to hook into specific phases of the spell casting and resolution lifecycle. It implements the Strategy pattern for spell behavior, enabling server-side scripts (typically loaded via the `ScriptMgr` system) to override default spell mechanics without modifying the core `Spell` class.

The class defines a set of virtual methods, each corresponding to a distinct event in the spell's life cycle—from initialization and target validation to effect execution, hit resolution, and summoning. By default, all methods are empty or return "pass-through" values (e.g., `true`, `SPELL_CAST_OK`), meaning a derived script only needs to implement the specific hooks it intends to modify.

This unit does not contain any implementation logic itself; it serves purely as a declaration of the contract between the core spell engine (`Spell.Main`) and external script modules.

## Member-by-Member Behavior

The members of `SpellScript` are organized by the phase of the spell lifecycle they intercept.

### Initialization and Casting Checks

*   **`OnInit`**: Called once when a `Spell` object is initialized. This is typically used for one-time setup or validation before the cast begins. It is invoked by `Spell.Main/Spell` and `Spell.Main/Spell#2`.
*   **`OnSuccessfulStart`**: Triggered immediately after a spell successfully passes the initial preparation phase (`Spell::Prepare`). It indicates that the spell has started casting but has not yet finished. Invoked by `Spell.Main/prepare#2`.
*   **`OnCheckCast`**: Executed at the end of the `Spell::CheckCast` routine. This hook allows scripts to veto a cast attempt based on custom conditions. The `strict` parameter indicates whether the check is part of the rigorous preparation phase. It returns a `SpellCastResult`; the default implementation returns `SPELL_CAST_OK`. Invoked by `Spell.Main/CheckCast`.
*   **`OnCast`**: Called within `Spell::cast` after all standard checks have passed and reagents have been consumed. This is the final step before effects begin resolving. Invoked by `Spell.Main/cast`.

### Targeting

*   **`OnSetTargetMap`**: Used during the targeting phase to dynamically adjust the spell's area-of-effect parameters. It can modify the target mode, radius, maximum number of targets, and whether the closest targets should be selected. Invoked by `Spell.Main/SetTargetMap`.
*   **`OnCheckTarget` (GameObject)**: Validates whether a specific `GameObject` is a valid target for the spell effect. Returns `true` by default. Invoked by `Spell.Main/AddGOTarget#2`.
*   **`OnCheckTarget#2` (Unit)**: Validates whether a specific `Unit` is a valid target for the spell effect. Returns `true` by default. Invoked by `Spell.Main/CheckTarget`.

### Effect Execution and Resolution

*   **`OnEffectExecute`**: Called before a specific spell effect is executed. Returning `false` prevents the effect from running. This allows scripts to conditionally disable individual effects within a multi-effect spell. Invoked by `Spell.Main/HandleEffects`.
*   **`OnHit`**: Triggered in `Spell::DoAllEffectOnTarget` for Unit targets, immediately before damage, healing, or other direct impacts are applied and before procs occur. This is a critical hook for modifying incoming damage or triggering pre-hit events. Invoked by `Spell.Main/DoAllEffectOnTarget`, `Spell.Main/DoAllEffectOnTarget#2`, and `Spell.Main/DoAllEffectOnTarget#3`.
*   **`OnAfterHit`**: Triggered in `Spell::DoAllEffectOnTarget` for Unit targets, after damage/healing has been applied and procs have resolved. This is used for post-resolution logic, such as applying secondary effects or logging. Invoked by `Spell.Main/DoAllEffectOnTarget#3`.
*   **`OnSuccessfulFinish`**: Called upon successful completion of the entire spell cast. For channeled spells, this only fires if the channel completes without interruption. Invoked by `Spell.Main/finish`.

### Summoning and Dispel

*   **`OnSummon` (Creature)**: Called after a creature is summoned by various summon effects (including critters, demons, guardians, possessed entities, totems, and wild summons). Allows scripts to configure or modify the newly created `Creature`. Invoked by `Spell.Effects/EffectSummon`, `Spell.Effects/EffectSummonCritter`, `Spell.Effects/EffectSummonDemon`, `Spell.Effects/EffectSummonGuardian`, `Spell.Effects/EffectSummonPossessed`, `Spell.Effects/EffectSummonTotem`, and `Spell.Effects/EffectSummonWild`.
*   **`OnSummon#2` (GameObject)**: Called after a `GameObject` is summoned. Allows scripts to configure the newly created object. Invoked by `Spell.Effects/EffectSummonObject` and `Spell.Effects/EffectSummonObjectWild`.
*   **`OnSuccessfulDispel`**: Called from the dispel effect handler when a debuff is successfully removed from a target. Invoked by `Spell.Effects/EffectDispel`.

### Destructor

*   **`~SpellScript`**: A virtual destructor ensuring proper cleanup of derived script classes.

## Cross-Unit Boundaries

`SpellScript` acts as a callback interface. It does not initiate calls to other units; rather, it is called by them. The collaboration is strictly inbound:

1.  **Core Spell Engine (`Spell.Main`)**:
    *   **Direction**: `Spell.Main` → `SpellScript`
    *   **Context**: The `Spell` class manages the lifecycle of a cast. At specific checkpoints (initialization, target validation, effect execution, hit resolution), it queries the attached `SpellScript` instance.
    *   **Data Crossing**: The `Spell` pointer is passed to all hooks, allowing the script to inspect or modify the spell state. Specific hooks also receive targets (`Unit`, `GameObject`), effect indices, or mutable references to targeting parameters (in `OnSetTargetMap`).
    *   **Why**: To allow modular customization of spell behavior without hardcoding special cases into the core `Spell` class.

2.  **Spell Effects (`Spell.Effects`)**:
    *   **Direction**: `Spell.Effects` → `SpellScript`
    *   **Context**: Specific effect handlers (e.g., `EffectSummon`, `EffectDispel`) invoke specialized hooks (`OnSummon`, `OnSuccessfulDispel`) after performing their primary action.
    *   **Data Crossing**: The `Spell` pointer and the resulting entity (`Creature`, `GameObject`) or effect index are passed.
    *   **Why**: To allow scripts to react to the *result* of an effect (e.g., configuring a summoned pet's AI or stats) rather than just the intent.

## Data Model

This unit interacts with **no database tables**. It is a pure C++ interface definition. Any data required by scripts implementing this interface is typically loaded by the `ScriptMgr` from database tables (such as `spell_scripts`), but the `SpellScript` class itself contains no SQL queries or table dependencies.

## Notable Implementation Details

*   **Default Pass-Through Behavior**: All virtual methods in `SpellScript` have default implementations that either do nothing (`void` returns) or return a permissive value (`true`, `SPELL_CAST_OK`). This ensures that if a derived script fails to implement a specific hook, the spell proceeds normally. This is crucial for backward compatibility and ease of use.
*   **Const Correctness**: Most hooks are marked `const`, indicating they should not modify the internal state of the `SpellScript` object itself. However, they often receive non-const pointers to `Spell`, `Unit`, or `GameObject`, allowing them to modify the game world state.
*   **Overloaded `OnCheckTarget`**: There are two distinct `OnCheckTarget` methods: one for `GameObject` and one for `Unit`. They are differentiated by their second parameter type. This reflects the different validation logic required for static objects versus dynamic entities.
*   **Overloaded `OnSummon`**: Similarly, `OnSummon` is overloaded for `Creature` and `GameObject`. This separation acknowledges that creatures and game objects have fundamentally different APIs and lifecycles.
*   **No State Storage**: The `SpellScript` struct contains no member variables. It is a stateless interface. Any state required for a specific spell cast must be stored in the `Spell` object, the target units, or external script data structures managed by the `ScriptMgr`.

## Member Reference

*   **`~SpellScript`**: Virtual destructor for the `SpellScript` interface. Ensures proper cleanup of derived classes.
*   **`OnInit`**: Virtual method called during spell initialization. Invoked by `Spell.Main/Spell` and `Spell.Main/Spell#2`. Default implementation is empty.
*   **`OnSuccessfulStart`**: Virtual method called after successful spell preparation. Invoked by `Spell.Main/prepare#2`. Default implementation is empty.
*   **`OnSuccessfulFinish`**: Virtual method called upon successful spell completion. Invoked by `Spell.Main/finish`. Default implementation is empty.
*   **`OnCheckCast`**: Virtual method called to validate cast conditions. Invoked by `Spell.Main/CheckCast`. Returns `SpellCastResult`; defaults to `SPELL_CAST_OK`.
*   **`OnEffectExecute`**: Virtual method called before effect execution. Invoked by `Spell.Main/HandleEffects`. Returns `bool`; defaults to `true`.
*   **`OnSetTargetMap`**: Virtual method called to adjust targeting parameters. Invoked by `Spell.Main/SetTargetMap`. Default implementation is empty.
*   **`OnCheckTarget`**: Virtual method (GameObject overload) to validate GameObject targets. Invoked by `Spell.Main/AddGOTarget#2`. Returns `bool`; defaults to `true`.
*   **`OnCheckTarget#2`**: Virtual method (Unit overload) to validate Unit targets. Invoked by `Spell.Main/CheckTarget`. Returns `bool`; defaults to `true`.
*   **`OnCast`**: Virtual method called after cast checks and reagent consumption. Invoked by `Spell.Main/cast`. Default implementation is empty.
*   **`OnHit`**: Virtual method called before damage/heal application on Units. Invoked by `Spell.Main/DoAllEffectOnTarget`, `Spell.Main/DoAllEffectOnTarget#2`, and `Spell.Main/DoAllEffectOnTarget#3`. Default implementation is empty.
*   **`OnAfterHit`**: Virtual method called after damage/heal application on Units. Invoked by `Spell.Main/DoAllEffectOnTarget#3`. Default implementation is empty.
*   **`OnSummon`**: Virtual method (Creature overload) called after creature summoning. Invoked by `Spell.Effects/EffectSummon`, `Spell.Effects/EffectSummonCritter`, `Spell.Effects/EffectSummonDemon`, `Spell.Effects/EffectSummonGuardian`, `Spell.Effects/EffectSummonPossessed`, `Spell.Effects/EffectSummonTotem`, and `Spell.Effects/EffectSummonWild`. Default implementation is empty.
*   **`OnSummon#2`**: Virtual method (GameObject overload) called after GameObject summoning. Invoked by `Spell.Effects/EffectSummonObject` and `Spell.Effects/EffectSummonObjectWild`. Default implementation is empty.
*   **`OnSuccessfulDispel`**: Virtual method called after successful dispel. Invoked by `Spell.Effects/EffectDispel`. Default implementation is empty.

---

<!-- machine-true, projected from graph.json -->

## Map — SpellScript

*Source:* ScriptMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ~SpellScript | dtor | — | — | — |
| OnInit | method | — | Spell.Main/Spell, Spell.Main/Spell#2 | — |
| OnSuccessfulStart | method | — | Spell.Main/prepare#2 | — |
| OnSuccessfulFinish | method | — | Spell.Main/finish | — |
| OnCheckCast | method | — | Spell.Main/CheckCast | — |
| OnEffectExecute | method | — | Spell.Main/HandleEffects | — |
| OnSetTargetMap | method | — | Spell.Main/SetTargetMap | — |
| OnCheckTarget | method | — | Spell.Main/AddGOTarget#2 | — |
| OnCheckTarget#2 | method | — | Spell.Main/CheckTarget | — |
| OnCast | method | — | Spell.Main/cast | — |
| OnHit | method | — | Spell.Main/DoAllEffectOnTarget, Spell.Main/DoAllEffectOnTarget#2, Spell.Main/DoAllEffectOnTarget#3 | — |
| OnAfterHit | method | — | Spell.Main/DoAllEffectOnTarget#3 | — |
| OnSummon | method | — | Spell.Effects/EffectSummon, Spell.Effects/EffectSummonCritter, Spell.Effects/EffectSummonDemon, Spell.Effects/EffectSummonGuardian, Spell.Effects/EffectSummonPossessed, Spell.Effects/EffectSummonTotem, Spell.Effects/EffectSummonWild | — |
| OnSummon#2 | method | — | Spell.Effects/EffectSummonObject, Spell.Effects/EffectSummonObjectWild | — |
| OnSuccessfulDispel | method | — | Spell.Effects/EffectDispel | — |
