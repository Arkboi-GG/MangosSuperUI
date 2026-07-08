# boss_warmaster_voone

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_warmaster_voone

**Purpose & Responsibilities**
`boss_warmaster_voone` implements the combat AI for Warmaster Voone in Blackrock Spire. The script manages a rotation of melee and ranged abilities (Snap Kick, Cleave, Uppercut, Mortal Strike, Pummel, Throw Axe) and handles a specific weapon-state mechanic: after throwing two axes, the boss becomes unarmed, applying a passive aura and clearing virtual equipment slots. The unit contains no database interactions.

## Member-by-Member Behavior

### Initialization and State
*   **`boss_warmastervooneAI`**: Constructs the AI, inheriting from `ScriptedAI`, and immediately calls `Reset()` to initialize timers and counters.
*   **`Reset`**: Sets initial cooldowns for all abilities (Snap Kick: 8s, Cleave: 14s, Uppercut: 20s, Mortal Strike: 12s, Pummel: 32s, Throw Axe: 1s) and initializes `m_axesThrownCount` to 0. Note that `m_axesThrownCount` is only set here; subsequent calls to `Reset()` (if any) would not reset the counter, though this method is primarily invoked during construction.

### Combat Loop
*   **`UpdateAI`**: The primary tick handler. It first validates that a hostile target exists via `SelectHostileTarget` and `GetVictim`. It then processes five fixed-rotation spells (Snap Kick, Cleave, Uppercut, Mortal Strike, Pummel), decrementing their timers and casting them via `DoCastSpellIfCan` upon expiration, resetting each to a shorter post-cast cooldown. The `Throw Axe` ability is gated by `HasAura(SPELL_UNARMED_PASSIVE)`; it only casts if the boss is still armed, resetting its timer to 8s. Finally, it triggers standard melee attacks via `DoMeleeAttackIfReady`.

### Event Handling
*   **`SpellHitTarget`**: Intercepts spell hits to manage weapon visuals. If the hit spell is `SPELL_THROWAXE`, it increments `m_axesThrownCount`. On the first throw (count 0), it sets the main-hand virtual item to ID `12348` and clears the off-hand. On the second throw (count 1), it clears both virtual items and casts `SPELL_UNARMED_PASSIVE` on the boss, which subsequently blocks further axe throws in `UpdateAI`.

### Registration
*   **`GetAI_boss_warmastervoone`**: Factory function allocating a new `boss_warmastervooneAI` instance.
*   **`AddSC_boss_warmastervoone`**: Registers the script with `ScriptMgr` by creating a `Script` object named "boss_warmaster_voone" and linking the `GetAI` function. Called by `ScriptLoader::AddScripts`.

## Cross-Unit Boundaries

*   **`ScriptedAI`**: Base class providing `m_creature` access and AI framework.
*   **`Creature.Main`**:
    *   `SetVirtualItem`: Called in `SpellHitTarget` to update weapon models.
    *   `SelectHostileTarget` / `GetVictim`: Called in `UpdateAI` for target validation.
    *   `HasAura`: Called in `UpdateAI` to gate `Throw Axe`.
*   **`SpellCaster`**: `CastSpell` called in `SpellHitTarget` to apply `SPELL_UNARMED_PASSIVE`.
*   **`CreatureAI`**:
    *   `DoCastSpellIfCan`: Used in `UpdateAI` for ability casting.
    *   `DoMeleeAttackIfReady`: Used in `UpdateAI` for melee swings.
*   **`Script` / `ScriptMgr`**: `AddSC_boss_warmastervoone` uses `Script` and `RegisterSelf` to register with `ScriptMgr`.
*   **`ScriptLoader`**: `AddScripts` calls `AddSC_boss_warmastervoone`.

## Data Model

This unit does not interact with any database tables.

## Notable Implementation Details

*   **Timer Discrepancy**: Initial timers in `Reset()` differ from post-cast resets in `UpdateAI` (e.g., Snap Kick starts at 8s, resets to 6s). This creates an initial delay before the first cast.
*   **Unarmed Mechanic**: The transition to unarmed state relies on `m_axesThrownCount` reaching 2. The `SPELL_UNARMED_PASSIVE` aura prevents further `Throw Axe` casts. If `Reset()` were called mid-fight, `m_axesThrownCount` would not reset, potentially leaving the boss permanently unarmed if the aura was removed externally, though this scenario is unlikely in normal gameplay.
*   **Virtual Item ID**: Hardcoded ID `12348` is used for the main-hand axe after the first throw.

## Member Reference

*   **`boss_warmastervooneAI`**: Constructor initializing `ScriptedAI` and calling `Reset()`.
*   **`Reset`**: Sets initial ability timers and `m_axesThrownCount` to 0.
*   **`SpellHitTarget`**: Handles `SPELL_THROWAXE` hits: updates virtual items and applies `SPELL_UNARMED_PASSIVE` after two throws.
*   **`UpdateAI`**: Manages ability rotation, gates `Throw Axe` by aura, and handles melee attacks.
*   **`GetAI_boss_warmastervoone`**: Factory function returning a new `boss_warmastervooneAI` instance.
*   **`AddSC_boss_warmastervoone`**: Registers the script with `ScriptMgr`.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_warmaster_voone

*Source:* boss_warmaster_voone.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_warmastervooneAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | method | — | — | — |
| SpellHitTarget | method | Creature.Main/SetVirtualItem, SpellCaster/CastSpell#2 | — | — |
| UpdateAI | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Unit.Main/GetVictim, Unit.Main/HasAura#2, Unit.Main/SelectHostileTarget | — | — |
| GetAI_boss_warmastervoone | function | — | — | — |
| AddSC_boss_warmastervoone | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
