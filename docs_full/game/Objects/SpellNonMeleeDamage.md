# SpellNonMeleeDamage

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# SpellNonMeleeDamage

**SpellNonMeleeDamage** is a lightweight data structure (aggregate struct) defined in `SpellCaster.h` that encapsulates the complete state of a single instance of magical or ranged damage dealt to a target. It serves as the primary payload for communicating damage results between the spell execution engine and the combat logging/networking subsystems.

The structure mirrors the fields required by the `SMSG_SPELLNONMELEEDAMAGELOG` network opcode, ensuring that all necessary information—such as raw damage, absorption, resistance, reflection status, and hit outcome—is gathered in one place before being transmitted to clients or processed for further game logic (e.g., triggering procs or updating threat).

It is not a class with behavior; it holds no methods other than its constructor. Its responsibility is purely to aggregate data generated during the resolution of a spell effect.

## Member-by-Member Behavior

### **SpellNonMeleeDamage** (Constructor)
This constructor initializes the `SpellNonMeleeDamage` instance with the core entities involved in the damage event: the attacker (`SpellCaster*`), the target (`Unit*`), the specific spell identifier (`uint32`), and the damage school (`SpellSchools`).

Crucially, it initializes all numeric damage-related fields (`damage`, `absorb`, `blocked`) to `0` and the resistance field (`resist`) to `0`. It also sets boolean flags (`periodicLog`, `reflected`) to `false` and the `HitInfo` to `0`. The `spell` pointer is initialized to `nullptr`.

This zero-initialization is vital because the structure is typically populated incrementally. The caller (usually within `Spell` or `Unit` logic) calculates the final damage, applies modifiers (armor, resistances, absorbs), and updates these fields before passing the structure to the logging functions. By starting with known zeros, the system ensures that unmodified fields do not contain garbage values.

## Cross-Unit Boundaries

The `SpellNonMeleeDamage` struct acts as a bridge between the high-level spell execution logic and the low-level combat logging/networking layer.

*   **Called by `Spell.Main/DoAllEffectOnTarget#3`**: During the execution of a spell's effects, specifically when dealing direct damage, the `Spell` class constructs this structure to record the outcome. It populates the fields with the calculated damage, absorption, and resistance values derived from the target's defenses and the caster's stats.
*   **Called by `Spell.Main/HandleDelayedSpellLaunch`**: For spells that launch projectiles or have delayed impact, this structure is used to pre-calculate or store the damage intent before the projectile hits, ensuring consistency between the visual launch and the final damage application.
*   **Called by `SpellCaster/SendSpellNonMeleeDamageLog#2`**: This is the primary consumer of the data. The `SpellCaster` class takes the fully populated `SpellNonMeleeDamage` struct and serializes its contents into the `SMSG_SPELLNONMELEEDAMAGELOG` packet sent to the client. This ensures the client displays the correct damage numbers, resist messages, and absorption effects.
*   **Called by `Unit.AuraProcHandler/HandleProcTriggerDamageAuraProc`**: When a damage event triggers secondary effects (procs) via auras, this structure is passed along to allow the proc handler to inspect the damage details (e.g., was it a critical hit? how much was absorbed?) to determine if specific aura conditions are met.
*   **Called by `Unit.SpellAuras/PeriodicTick`**: For damage-over-time (DoT) effects, the periodic tick mechanism uses this structure to log each individual tick of damage, setting the `periodicLog` flag to `true` to distinguish it from direct damage events in the combat log.

## Data Model

This unit does not interact directly with any database tables. It operates entirely in memory as part of the real-time combat simulation and networking pipeline.

## Notable Implementation Details

*   **Zero-Initialization Strategy**: The constructor explicitly sets `damage`, `absorb`, `resist`, `blocked`, and `HitInfo` to `0`. This is a defensive programming practice. Since the struct is often declared on the stack or allocated dynamically and then partially filled by various calculation steps, ensuring a clean baseline prevents undefined behavior if a particular modifier (like resistance) is not applicable to a specific spell school.
*   **Separation of Concerns**: The struct separates the *calculation* of damage from the *reporting* of damage. The `Spell` and `Unit` classes perform the complex arithmetic (armor reduction, spell power scaling, critical hit chance), populate this struct, and then hand it off to `SpellCaster::SendSpellNonMeleeDamageLog`. This decoupling allows the logging logic to remain simple and focused solely on network serialization.
*   **Reflection and Periodic Flags**: The `reflected` and `periodicLog` booleans are essential for accurate combat log representation. `reflected` indicates if the damage was bounced back to the attacker (common with shield spells), while `periodicLog` tells the client that this is a tick from a DoT, which affects how the damage number is displayed (often smaller font or different color) and how it interacts with threat generation.
*   **HitInfo Field**: The `HitInfo` field (typically a bitmask or enum value) stores the outcome of the hit check (e.g., normal, critical, resisted). This is distinct from the numeric damage values and is used by the client to play appropriate visual effects (crit sparkles, resist text).

## Member Reference

**SpellNonMeleeDamage**
Constructor that initializes the damage log structure. It accepts the attacker, target, spell ID, and school as arguments. It sets all damage, absorption, resistance, and block values to zero, and flags like `periodicLog` and `reflected` to false. It also initializes the `spell` pointer to null. This provides a clean slate for the calling logic to populate with the results of the damage calculation.

---

<!-- machine-true, projected from graph.json -->

## Map — SpellNonMeleeDamage

*Source:* SpellCaster.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| SpellNonMeleeDamage | ctor | — | Spell.Main/DoAllEffectOnTarget#3, Spell.Main/HandleDelayedSpellLaunch, SpellCaster/SendSpellNonMeleeDamageLog#2, Unit.AuraProcHandler/HandleProcTriggerDamageAuraProc, Unit.SpellAuras/PeriodicTick | — |
