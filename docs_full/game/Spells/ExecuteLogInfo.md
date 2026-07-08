# ExecuteLogInfo

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ExecuteLogInfo

## Purpose & Responsibilities

`ExecuteLogInfo` is a nested `struct` within the `Spell` class in `Spell.h`. It serves as a lightweight data container for recording specific outcomes of spell effects that require detailed logging to the client via the "Execute Log" packet (`SMSG_LOG_EXECUTE_*`).

Unlike general damage or healing logs, certain spell effects—such as draining power, creating items, feeding pets, or adding extra attacks—require distinct fields in the execute log packet to convey context-specific data (e.g., the ID of the created item, the amount of power drained, or the number of extra attacks granted). `ExecuteLogInfo` encapsulates these varying data requirements using a C++ `union`, allowing the `Spell` system to accumulate heterogeneous effect results during execution and transmit them efficiently to the client in a single batch.

The struct is instantiated by various `Spell` effect handlers (members of the `Spell` class defined in other source files, such as `SpellEffects.cpp`) and stored in the `Spell::m_executeLogInfo` vector. It is not a standalone unit with complex logic; it is purely a data structure.

## Member-by-Member Behavior

The `ExecuteLogInfo` struct contains two constructors and a set of data members organized into a union.

### Constructors

*   **`ExecuteLogInfo()`**: The default constructor. It initializes the struct with empty/default values. This is typically used when the specific type of log entry is determined later or when a generic placeholder is needed before population.
*   **`ExecuteLogInfo(ObjectGuid _targetGuid)`**: A parameterized constructor that accepts an `ObjectGuid`. It initializes the `targetGuid` member with the provided GUID. This is the primary constructor used by effect handlers to associate the log entry with a specific target unit, item, or game object.

### Data Members

*   **`targetGuid`**: An `ObjectGuid` representing the target of the spell effect. This is the common denominator for all execute log entries, identifying who or what was affected.
*   **Union Fields**: The struct uses a `union` to store effect-specific data. Only one of these sub-structures is valid for any given instance, depending on the spell effect type:
    *   **`powerDrain`**: Used for effects like `EffectPowerDrain`. Contains:
        *   `power`: The type of power drained (e.g., Mana, Rage).
        *   `amount`: The quantity of power drained.
        *   `multiplier`: A float multiplier, likely used for scaling or display purposes.
    *   **`extraAttacks`**: Used for effects like `EffectAddExtraAttacks`. Contains:
        *   `count`: The number of extra attacks granted.
    *   **`createItem`**: Used for effects like `EffectCreateItem`. Contains:
        *   `itemEntry`: The entry ID of the item created.
    *   **`interruptCast`**: Used for effects like `EffectInterruptCast`. Contains:
        *   `spellId`: The ID of the spell that was interrupted.
    *   **`feedPet`**: Used for effects like `EffectFeedPet`. Contains:
        *   `itemEntry`: The entry ID of the item fed to the pet.
    *   **`durabilityDamage`**: Used for effects like `EffectDurabilityDamage`. Contains:
        *   `itemEntry`: The entry ID of the item damaged.
        *   `unk`: An unknown integer field, possibly reserved for future use or specific durability mechanics.
    *   **`heal`**: Used for healing effects. Contains:
        *   `amount`: The amount healed.
        *   `critical`: A boolean flag indicating if the heal was critical.
    *   **`energize`**: Used for effects like `EffectEnergize`. Contains:
        *   `amount`: The amount of energy/power granted.
        *   `powerType`: The type of power granted.

## Cross-Unit Boundaries

`ExecuteLogInfo` is a passive data structure. It does not call out to other units. However, it is heavily integrated into the `Spell` class's execution flow.

*   **Called By (Instantiation)**:
    *   `Spell.Effects/EffectCreateItem` (in `SpellEffects.cpp`): Instantiates `ExecuteLogInfo` to log item creation.
    *   `Spell.Effects/EffectFeedPet` (in `SpellEffects.cpp`): Instantiates `ExecuteLogInfo` to log pet feeding.
    *   `Spell.Effects/EffectAddExtraAttacks` (in `SpellEffects.cpp`): Instantiates `ExecuteLogInfo` to log extra attacks.
    *   `Spell.Effects/EffectDismissPet` (in `SpellEffects.cpp`): Instantiates `ExecuteLogInfo` to log pet dismissal.
    *   `Spell.Effects/EffectDispel` (in `SpellEffects.cpp`): Instantiates `ExecuteLogInfo` to log dispels.
    *   `Spell.Effects/EffectDispelMechanic` (in `SpellEffects.cpp`): Instantiates `ExecuteLogInfo` to log mechanic dispels.
    *   `Spell.Effects/EffectDistract` (in `SpellEffects.cpp`): Instantiates `ExecuteLogInfo` to log distraction.
    *   `Spell.Effects/EffectDurabilityDamage` (in `SpellEffects.cpp`): Instantiates `ExecuteLogInfo` to log durability damage.
    *   `Spell.Effects/EffectInterruptCast` (in `SpellEffects.cpp`): Instantiates `ExecuteLogInfo` to log cast interruptions.
    *   `Spell.Effects/EffectModifyThreatPercent` (in `SpellEffects.cpp`): Instantiates `ExecuteLogInfo` to log threat modifications.
    *   `Spell.Effects/EffectOpenLock` (in `SpellEffects.cpp`): Instantiates `ExecuteLogInfo` to log lock opening.
    *   `Spell.Effects/EffectPowerDrain` (in `SpellEffects.cpp`): Instantiates `ExecuteLogInfo` to log power draining.
    *   `Spell.Effects/EffectResurrect` (in `SpellEffects.cpp`): Instantiates `ExecuteLogInfo` to log resurrection.
    *   `Spell.Effects/EffectResurrectNew` (in `SpellEffects.cpp`): Instantiates `ExecuteLogInfo` to log new-style resurrection.
    *   `Spell.Effects/EffectSanctuary` (in `SpellEffects.cpp`): Instantiates `ExecuteLogInfo` to log sanctuary effects.
    *   `Spell.Effects/EffectSkinPlayerCorpse` (in `SpellEffects.cpp`): Instantiates `ExecuteLogInfo` to log corpse skinning.
    *   `Spell.Effects/EffectSummon` (in `SpellEffects.cpp`): Instantiates `ExecuteLogInfo` to log summons.
    *   `Spell.Effects/EffectSummonCritter` (in `SpellEffects.cpp`): Instantiates `ExecuteLogInfo` to log critter summons.
    *   `Spell.Effects/EffectSummonDemon` (in `SpellEffects.cpp`): Instantiates `ExecuteLogInfo` to log demon summons.
    *   `Spell.Effects/EffectSummonGuardian` (in `SpellEffects.cpp`): Instantiates `ExecuteLogInfo` to log guardian summons.
    *   `Spell.Effects/EffectSummonObject` (in `SpellEffects.cpp`): Instantiates `ExecuteLogInfo` to log object summons.
    *   `Spell.Effects/EffectSummonObjectWild` (in `SpellEffects.cpp`): Instantiates `ExecuteLogInfo` to log wild object summons.
    *   `Spell.Effects/EffectSummonPet` (in `SpellEffects.cpp`): Instantiates `ExecuteLogInfo` to log pet summons.
    *   `Spell.Effects/EffectSummonTotem` (in `SpellEffects.cpp`): Instantiates `ExecuteLogInfo` to log totem summons.
    *   `Spell.Effects/EffectSummonWild` (in `SpellEffects.cpp`): Instantiates `ExecuteLogInfo` to log wild summons.
    *   `Spell.Effects/EffectTaunt` (in `SpellEffects.cpp`): Instantiates `ExecuteLogInfo` to log taunts.
    *   `Spell.Effects/EffectThreat` (in `SpellEffects.cpp`): Instantiates `ExecuteLogInfo` to log threat changes.
    *   `Spell.Effects/EffectTransmitted` (in `SpellEffects.cpp`): Instantiates `ExecuteLogInfo` to log transmitted effects.

*   **Consumed By**:
    *   The `Spell` class itself (specifically methods like `SendLogExecute` in `Spell.cpp`) iterates over the `m_executeLogInfo` vector, extracting data from `ExecuteLogInfo` instances to construct and send `WorldPacket`s to clients.

## Data Model

`ExecuteLogInfo` does not interact directly with any database tables. It operates entirely in memory during the spell execution phase. The data it holds is transient and is discarded once the spell log packets are sent to the client.

## Notable Implementation Details

*   **Union Usage**: The use of a `union` for the effect-specific data is a space-saving measure. Since only one type of effect data is relevant for any single log entry, storing all possible fields simultaneously would waste memory. However, this requires careful handling by the code that reads the struct to ensure it accesses the correct sub-structure based on the effect type. Incorrect access to the wrong union member leads to undefined behavior and potential crashes or corrupted packets.
*   **Transient Nature**: Instances of `ExecuteLogInfo` are short-lived. They are created during the `HandleEffects` phase of a spell, stored in `m_executeLogInfo`, and then consumed by the logging mechanism. They are not persisted or reused across different spell casts.
*   **Target Guid Initialization**: The parameterized constructor ensures that the `targetGuid` is always set correctly at instantiation. This is crucial for the client to identify the target of the effect in the combat log.
*   **No Validation**: The struct itself performs no validation on the data passed to it. It is the responsibility of the calling effect handlers (e.g., `EffectPowerDrain`) to ensure that the data placed into the union fields is valid and consistent with the expected format for the corresponding execute log packet.

## Member Reference

*   **ExecuteLogInfo**: Default constructor for the `ExecuteLogInfo` struct. Initializes all members to default values. Used when the target GUID is not known at construction time or for generic initialization.
*   **ExecuteLogInfo#2**: Parameterized constructor for the `ExecuteLogInfo` struct. Takes an `ObjectGuid` and initializes the `targetGuid` member. This is the primary way effect handlers create log entries, ensuring the target is immediately associated with the log data.

---

<!-- machine-true, projected from graph.json -->

## Map — ExecuteLogInfo

*Source:* Spell.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ExecuteLogInfo | ctor | — | Spell.Effects/EffectCreateItem, Spell.Effects/EffectFeedPet | — |
| ExecuteLogInfo#2 | ctor | — | Spell.Effects/EffectAddExtraAttacks, Spell.Effects/EffectDismissPet, Spell.Effects/EffectDispel, Spell.Effects/EffectDispelMechanic, Spell.Effects/EffectDistract, Spell.Effects/EffectDurabilityDamage, Spell.Effects/EffectInterruptCast, Spell.Effects/EffectModifyThreatPercent, Spell.Effects/EffectOpenLock, Spell.Effects/EffectPowerDrain, Spell.Effects/EffectResurrect, Spell.Effects/EffectResurrectNew, Spell.Effects/EffectSanctuary, Spell.Effects/EffectSkinPlayerCorpse, Spell.Effects/EffectSummon, Spell.Effects/EffectSummonCritter, Spell.Effects/EffectSummonDemon, Spell.Effects/EffectSummonGuardian, Spell.Effects/EffectSummonObject, Spell.Effects/EffectSummonObjectWild, Spell.Effects/EffectSummonPet, Spell.Effects/EffectSummonTotem, Spell.Effects/EffectSummonWild, Spell.Effects/EffectTaunt, Spell.Effects/EffectThreat, Spell.Effects/EffectTransmitted | — |
