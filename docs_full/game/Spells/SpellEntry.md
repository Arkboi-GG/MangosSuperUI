<!-- provenance: failed-members -->
# SpellEntry

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# SpellEntry

**Purpose & Responsibilities**

`SpellEntry` is the central data structure representing the definition of a single spell in the World of Warcraft server emulation. It maps directly to the `Spell.dbc` file, holding static configuration data such as effects, durations, ranges, attributes, and targeting rules.

This unit serves two primary roles:
1.  **Data Container:** It stores the raw DBC fields for a spell, providing accessor methods for basic properties like ID, school, mechanics, and power costs.
2.  **Logic Interpreter:** It contains complex helper functions that interpret these static fields to determine dynamic behaviors, such as whether a spell is positive or negative, how it interacts with diminishing returns, how its cast time is calculated under various modifiers, and how it stacks with other spells.

The unit exposes both instance methods (operating on a specific `SpellEntry` object) and static helper functions within the `Spells` namespace that operate on spell IDs or compare multiple spell entries. It relies heavily on `SpellMgr` to resolve other spell references and on `Unit`, `Player`, and `Spell` classes for context-aware calculations (e.g., cast time modifications).

**Member-by-Member Behavior**

### Spell Classification and Specific Types

These members categorize spells into specific gameplay categories (e.g., "Well Fed," "Polymorph," "Seal") to enforce stacking rules and special interactions.

*   **GetSpellSpecific**: Determines the high-level "specific type" of a spell (e.g., `SPELL_FOOD`, `SPELL_SEAL`, `SPELL_POLYMORPH`). It uses a combination of `SpellFamilyName`, family flags, aura types, and explicit spell IDs. For example, it identifies Mage Polymorph by checking for `SPELL_AURA_MOD_CONFUSE` combined with `SPELL_PREVENTION_TYPE_SILENCE`. It calls `SpellMgr/GetSpellElixirSpecific` for potions and `SpellMgr/GetSpellEntry` to look up referenced spells.
*   **IsSealSpell**: Checks if a Paladin spell is a Seal by verifying specific family flags (`CF_PALADIN_SEAL_*`).
*   **IsElementalShield**: Identifies Shaman Elemental Shields by family flags or specific spell ID (T2 set bonus).
*   **IsCharmSpell**: Returns true if the spell is flagged internally as a charm spell (`SPELL_INTERNAL_CHARM`).
*   **IsTotemSummonSpell**: Checks if the first effect is a totem summon effect.
*   **IsFromBehindOnlySpell**: Checks custom flags or specific attribute combinations indicating the spell must be cast from behind the target.
*   **IsBinary**: Checks if the spell is internally flagged as binary (`SPELL_INTERNAL_BINARY`), typically meaning it has a strict success/fail outcome without partial effects.
*   **IsDismountSpell**: Returns true if the spell is internally flagged as a dismount spell (`SPELL_INTERNAL_DISMOUNT`).
*   **IsDispel**: Returns true if the spell has the `SPELL_EFFECT_DISPEL` effect.
*   **IsNonPeriodicDispel**: Returns true if the spell is internally flagged as a non-periodic dispel (`SPELL_INTERNAL_NON_PERIODIC_DISPEL`).
*   **IsPvEHeartBeat**: Returns true if the spell is internally flagged as a PvE heartbeat spell (`SPELL_INTERNAL_PVE_HEARTBEAT`).
*   **IsCCSpell**: Returns true if the spell is internally flagged as a crowd control spell (`SPELL_INTERNAL_CROWD_CONTROL`).
*   **IsSpellRequiresRangedAP**: Returns true if the spell is a Hunter spell that is not melee damage class, implying it scales with Ranged Attack Power.
*   **IsSpellWithCasterSourceTargetsOnly**: Returns true if the spell is internally flagged to only target from the caster's source location (`SPELL_INTERNAL_CASTER_SOURCE_TARGETS`).
*   **NeedsComboPoints**: Returns true if the spell has attributes indicating it consumes combo points for damage or duration (`SPELL_ATTR_EX_FINISHING_MOVE_DAMAGE` or `SPELL_ATTR_EX_FINISHING_MOVE_DURATION`).

### Stacking and Rank Comparison

These functions determine how spells interact with existing auras on a target, particularly regarding stacking rules.

*   **CompareAuraRanks**: Compares two spells by ID to determine which has a higher rank/power. It iterates through effects, comparing `EffectBasePoints`. It inverts the difference if both values are negative (debuffs), ensuring that a "stronger" debuff (more negative) is recognized as higher priority. It calls `SpellMgr/GetSpellEntry` to fetch the entries.
*   **CompareSpellSpecificAuras**: Compares two `SpellEntry` pointers to see if one is "better" than the other for stacking purposes. It looks for matching aura types and compares base points and durations. It returns true if `spellInfo_1` is superior to `spellInfo_2`.
*   **IsSingleTargetSpells**: Determines if two spells are mutually exclusive on a single target. It checks if they share the same family and icon, or if they belong to specific mutually exclusive categories like Judgements or Polymorphs. It calls `GetSpellSpecific` to categorize them.
*   **IsSingleFromSpellSpecificPerTargetPerCaster**, **IsSingleFromSpellSpecificSpellRanksPerTarget**, **IsSingleFromSpellSpecificPerTarget**: Static helpers that define strict stacking rules for specific spell types (e.g., Blessings, Curses, Aspects). They return true if two specific spell types cannot coexist on the same target (and optionally same caster).

### Positive/Negative Determination

Determining if a spell is "positive" (beneficial) or "negative" (harmful) is critical for targeting, immunity checks, and threat generation.

*   **IsPositiveSpell**: There are multiple overloads.
    *   The parameterless version checks an internal flag (`SPELL_INTERNAL_POSITIVE`).
    *   The `(WorldObject*, WorldObject*)` version performs a detailed check. It returns false if the spell has the `SPELL_ATTR_AURA_IS_DEBUFF` attribute. Otherwise, it iterates through all effects using `IsPositiveEffect`. If any effect is negative, the whole spell is considered negative.
    *   Static wrappers call `SpellMgr/GetSpellEntry` to resolve the entry before delegating to the instance method.
*   **IsPositiveSpell#2**: Static wrapper taking caster/victim for context-aware positivity check. Calls `SpellMgr/GetSpellEntry`.
*   **IsPositiveSpell#3**: Instance method checking the internal `SPELL_INTERNAL_POSITIVE` flag. Used for quick checks where runtime context is unavailable or unnecessary.
*   **IsPositiveSpell#4**: Instance method that performs the full positivity check including effect iteration, similar to the `(WorldObject*, WorldObject*)` overload but accessible directly on the instance.
*   **IsPositiveEffect**: Evaluates a specific effect index. It handles complex cases:
    *   Self-instakills (suicide) are positive.
    *   Warlock sacrifices are positive for the Warlock.
    *   Dispel positivity depends on faction relationship (calls `GetCharmInfo`, `GetFactionTemplateEntry`, `IsFriendlyTo`).
    *   Aura effects like `MOD_DAMAGE_DONE` are negative if the value is negative, but `MOD_DAMAGE_TAKEN` is positive if the value is negative (reduction).
    *   It recursively checks triggered spells for periodic triggers.
    *   It calls `SpellMgr/GetSpellEntry` to inspect triggered spells.
*   **IsPositiveTarget** / **IsExplicitPositiveTarget** / **IsExplicitNegativeTarget**: Static helpers that classify implicit target types (e.g., `TARGET_UNIT_ENEMY` is negative, `TARGET_UNIT_FRIEND` is positive).
*   **IsPositiveEffectMask**: Checks if all effects in a given mask are positive. If any effect in the mask is negative, it returns false.

### Diminishing Returns (DR)

*   **GetDiminishingReturnsGroup**: Calculates which DR group a spell belongs to. It first checks explicit family-specific rules (e.g., Rogue Kidney Shot, Hunter Freezing Trap, Warlock Fear). It then falls back to mechanic-based grouping (e.g., Stun, Root, Fear). It distinguishes between "controlled" and "triggered" CC for certain client builds. It calls `GetAllSpellMechanicMask` to get the relevant mechanics.
*   **GetDiminishingReturnsGroupType**: Static helper mapping a DR group to a DR type (Player, All, None).
*   **GetDiminishingRate**: Static helper returning the duration multiplier for a given DR level (1.0, 0.5, 0.25, 0.0).

### Cast Time and Duration

*   **GetCastTime**: Calculates the actual cast time in milliseconds.
    *   Returns 0 for triggered spells with redundant data, item procs, or trade-slot applications.
    *   Looks up `SpellCastTimesEntry` via `CastingTimeIndex`.
    *   Adjusts for caster level/rank.
    *   Applies spell mods (calling `Unit/GetSpellModOwner` -> `ApplySpellMod`).
    *   Applies cast speed modifiers from the unit's float values.
    *   Adds 500ms penalty for ranged spells that aren't auto-repeat.
    *   Calls `Spell/IsTriggeredSpellWithRedundentData`, `Spell/IsCastByItem`, `Player/GetTradeData`, `TradeData/GetItem`, `Unit/GetSpellModOwner`, `Object/GetFloatValue`.
*   **GetCastTimeForBonus**: Calculates a normalized cast time used for damage/healing coefficient calculations. It clamps cast times between 1500ms and 7000ms. It adjusts for AoE (halves time), leech (halves time), and additional effects (reduces by 5% per extra effect).
*   **GetDuration** / **GetMaxDuration**: Retrieves base and maximum durations from `SpellDurationEntry`.
*   **CalculateDuration**: Computes the final duration of an aura.
    *   Adds combo point bonuses for Rogues.
    *   Calls `AuraScript/OnDurationCalculate`.
    *   Applies spell mods via `Unit/GetSpellModOwner`.
    *   Ensures duration is not negative.
*   **GetAuraMaxTicks**: Calculates the number of ticks for a periodic effect based on duration and amplitude. Defaults to 6 if amplitude is 0.

### Coefficients and Damage Calculation

*   **CalculateDefaultCoefficient**: Calculates the spell power/attack power coefficient for damage/healing. It factors in cast time, duration (for DoTs), and tick count.
*   **CalculateCustomCoefficient**: Applies class-specific coefficient overrides.
    *   **Paladin Seals:** Sets specific coefficients for Seal of Righteousness (based on weapon type) and Seal of Command. It calls `Player/GetItemByPos` and `Item/isOneHandedWeapon`.
    *   **Shaman:** Handles Chain Lightning/Heal multipliers for T1/T2 set bonuses. It calls `Spell/GetTargetNum` and `Unit/GetSpellModOwner`.

### Targeting and Range

*   **IsTargetInRange**: Checks if a target is within the spell's range.
    *   Handles special range indices (`SELF_ONLY`, `ANYWHERE`, `COMBAT`).
    *   For standard ranges, it looks up `SpellRangeEntry` and compares distance against min/max range.
    *   Calls `WorldObject/GetCombatDistance`, `WorldObject/CanReachWithMeleeSpellAttack`, and `Spells/GetSpellRadius`.
*   **GetSpellRadius**, **GetSpellMinRange**, **GetSpellMaxRange**: Static helpers extracting values from DBC structures.
*   **IsAreaEffectTarget** / **IsAreaAuraEffect**: Static helpers identifying if a target type or effect type represents an Area of Effect.
*   **IsAreaOfEffectSpell**: Returns true if the spell is internally flagged as an Area of Effect spell (`SPELL_INTERNAL_AOE`).
*   **HasAreaAuraEffect**: Returns true if the spell is internally flagged as having an Area Aura effect (`SPELL_INTERNAL_AOE_AURA`).
*   **IsExplicitlySelectedUnitTarget**: Checks if the spell requires the player to manually click a target (vs. auto-targeting).
*   **IsIgnoreLosTarget**: Checks if the spell ignores Line of Sight (e.g., raid-wide heals).
*   **IsCasterSourceTarget**: Checks if the effect originates from the caster's position.
*   **IsPointEffectTarget**: Checks if the effect targets a specific location in the world.
*   **IsScriptTarget**: Checks if the target is determined by custom scripts.
*   **GetAllowedTargetMaskForTargetType**: Maps a target type to a bitmask of valid target flags (Unit, Item, GameObject, etc.).

### Attributes and Flags

*   **HasAttribute**: Overloaded methods checking `Attributes`, `AttributesEx`, `AttributesEx2`, etc.
*   **HasAttribute#2**: Overload checking `AttributesEx2` bitmask.
*   **HasAttribute#3**: Overload checking `AttributesEx3` bitmask.
*   **HasAttribute#4**: Overload checking `AttributesEx4` bitmask.
*   **HasAttribute#5**: Overload checking `AttributesCustom` bitmask.
*   **HasAttribute#6**: Overload checking `AttributesEx` bitmask (distinct from #2 in signature/type).
*   **HasSpellInterruptFlag**, **HasAuraInterruptFlag**, **HasChannelInterruptFlag**: Check specific interrupt conditions (e.g., movement breaks cast, damage breaks channel).
*   **HasEffect**: Iterates through effects to see if a specific `SpellEffect` type is present.
*   **IsPassiveSpell**: Checks if the spell is passive (always active, no cast bar). Static wrappers call `SpellMgr/GetSpellEntry`.
*   **IsPassiveSpell#2**: Instance method checking if the spell is passive by verifying the `SPELL_ATTR_PASSIVE` attribute.
*   **IsPassiveSpellStackableWithRanks**: Returns true if the spell is internally flagged to stack with its own ranks (`SPELL_INTERNAL_PASSIVE_STACK_WITH_RANKS`).
*   **IsAutocastable**: Checks if the spell can be autocast by AI/pets. Static wrappers call `SpellMgr/GetSpellEntry`.
*   **IsAutocastable#2**: Instance method checking if the spell is autocastable by verifying it lacks `SPELL_ATTR_EX_NO_AUTOCAST_AI` and `SPELL_ATTR_PASSIVE` attributes.
*   **IsChanneledSpell**: Checks if the spell uses channels instead of cast bars.
*   **IsRangedSpell**: Checks if the spell uses the ranged attack slot.
*   **IsAutoRepeatRangedSpell**: Checks if the spell is a ranged spell that auto-repeats (uses ranged slot and has `SPELL_ATTR_EX2_AUTO_REPEAT`).
*   **IsNextMeleeSwingSpell**: Checks if the effect applies on the next melee swing.
*   **IsNonCombatSpell**: Checks if the spell can only be cast out of combat.
*   **IsDeathOnlySpell** / **CanTargetDeadTarget** / **CanTargetAliveState**: Determine if the spell interacts with dead units or ghosts.
*   **IsDeathPersistentSpell**: Checks if the aura persists after death.
*   **IsIgnoringCasterAndTargetRestrictions**: Checks if the spell bypasses normal caster/target restrictions.
*   **IsNeedFaceTarget**: Checks if the caster must face the target.
*   **IsNeedCastSpellAtFormApply**: Checks if a passive spell should be applied when entering a shapeshift form.
*   **IsNeedCastSpellAtOutdoor**: Checks if a passive spell is only active outdoors.
*   **IsRemovedOnShapeLostSpell**: Checks if the aura should be removed when leaving a shapeshift form.
*   **IsTargetPowerTypeValid**: Checks if the target has the required power type (e.g., Mana for Mana Burn).
*   **IsAuraRemovedOnEvade**: Returns true unless the spell has the custom flag `SPELL_CUSTOM_NOT_REMOVED_ON_EVADE`. By default, auras are removed on evade.

### Mechanics and Immunities

*   **GetAllSpellMechanicMask**: Combines the base mechanic and effect-specific mechanics into a single bitmask.
*   **GetSpellMechanicMask**: Similar to above, but filtered by an effect mask.
*   **GetEffectMechanic**: Returns the mechanic for a specific effect index.
*   **GetMechanic**: Returns the base `Mechanic` field of the spell.
*   **GetSpellSchoolMask**: Returns the school mask (Fire, Frost, etc.) for the spell.
*   **IsSpellAppliesAura**: Checks if the spell applies an aura. Overloaded versions check specific effect masks.
*   **IsSpellAppliesAura#2**: Overloaded instance method checking if the spell applies an aura for a specific effect mask.
*   **IsSpellAppliesPeriodicAura**: Checks if the spell applies a periodic (DoT/HoT) aura.
*   **HasAura** / **HasSingleAura**: Checks if the spell applies a specific aura type. `HasSingleAura` ensures no other auras are applied.
*   **HasSingleTargetAura**: Returns true if the spell has the custom flag `SPELL_CUSTOM_SINGLE_TARGET_AURA`, indicating it applies an aura to a single target.
*   **HasAuraOrTriggersAnotherSpellWithAura**: Recursively checks if the spell or any triggered spell applies a specific aura. Calls `SpellMgr/GetSpellEntry`.
*   **IsDirectDamageEffect** / **IsDirectDamageWithBonusEffect**: Static helpers identifying effects that deal direct damage.
*   **IsDirectDamageSpell**: Returns true if the spell is internally flagged as dealing direct damage (`SPELL_INTERNAL_DIRECT_DAMAGE`).
*   **IsHealSpell**: Returns true if the spell is internally flagged as a healing spell (`SPELL_INTERNAL_HEAL`).
*   **IsEffectThatCanCrit**: Static helper identifying effects capable of critical hits.
*   **IsThreatEffect**: Static helper identifying effects that generate threat.
*   **IsSummonEffect**: Static helper identifying summoning effects.
*   **IsEffectHandledOnDelayedSpellLaunch**: Checks if the effect should be delayed (e.g., projectile travel time).
*   **IsDelayableEffect**: Checks if the effect is eligible for delay based on config settings.
*   **IsPeriodicRegenerateEffect**: Checks if the effect is a periodic regen (heal/energize).
*   **IsCustomSpell**: Checks for internal custom spell flags.
*   **IsSpellWithDelayableEffects**: Checks for internal delayable flags.
*   **HasDirectThreatIncreaseEffect**: Checks if any effect generates positive threat.
*   **CanTriggerWeaponProcs**: Checks if the spell can trigger weapon procs (weapon-based abilities or custom flag).
*   **CanCrit**: Checks if any effect can critically hit.
*   **HasRealTimeDuration**: Checks if the aura expires in real-time (even if offline).
*   **HasAuraWithSpellTriggerEffect**: Checks if the aura triggers another spell.
*   **IsReflectableSpell**: Checks if the spell can be reflected. The instance method checks internal flags. The overloaded method checks if it is magic, not an ability, not positive, and lacks reflection immunity attributes.
*   **IsReflectableSpell#2**: Overloaded instance method performing the detailed reflection check described above.

### Utility and Metadata

*   **GetRank**: Parses the "Rank X" string from the `Rank` field.
*   **GetRecoveryTime**: Returns the greater of `RecoveryTime` and `CategoryRecoveryTime`.
*   **GetManaCost**: Returns the base mana cost.
*   **GetSpellFamilyName** / **GetSpellFamilyFlags**: Returns family identifiers.
*   **GetStackAmount**: Returns the max stack count.
*   **GetEffectImplicitTargetAByIndex** / **GetEffectImplicitTargetBByIndex**: Returns implicit target types for an effect.
*   **GetEffectApplyAuraNameByIndex**: Returns the aura type for an effect.
*   **GetEffectMiscValue**: Returns the misc value for an effect.
*   **GetAuraInterruptFlags**: Returns the aura interrupt flags.
*   **GetFirstEffectIndexInMask**: Static helper finding the first set bit in a mask.
*   **IsFitToFamilyMask** / **IsFitToFamily**: Template helpers checking if the spell matches specific family flags.
*   **CalculateSimpleValue**: Calculates `EffectBasePoints + EffectBaseDice`.
*   **GetErrorAtShapeshiftedCast**: Determines if a spell can be cast in a specific shapeshift form. It checks `Stances` and `StancesNot` masks. It calls `DBCStores/GetTalentSpellCost` and `Log/Main/Out` for debugging.

**Cross-Unit Boundaries**

*   **SpellMgr**: `SpellEntry` frequently calls `SpellMgr/GetSpellEntry` and `SpellMgr/Instance` to resolve other spells referenced in triggered effects or elixir specifics. It also calls `SpellMgr/GetSpellElixirSpecific` for potion classification.
*   **Unit/Player**: Many calculation methods (`GetCastTime`, `CalculateDuration`, `IsPositiveEffect`) take `Unit` or `Player` pointers to apply modifiers, check factions, or access equipment. They call into `Unit/GetSpellModOwner`, `Player/GetItemByPos`, `Unit/GetCharmInfo`, etc.
*   **Spell**: Methods like `GetCastTime` and `CalculateCustomCoefficient` take `Spell` pointers to access runtime context (e.g., `Spell/IsTriggered`, `Spell/GetTargetNum`).
*   **ChatHandler/Unit.Main/Spell.Main**: Numerous `SpellEntry` methods are called by these units to query spell properties during casting, targeting, aura application, and debug commands. For example, `Spell.Main/DoSpellHitOnUnit` calls `IsFriendlyTarget` and `GetDiminishingReturnsGroupType`.

**Data Model**

`SpellEntry` does not interact with SQL database tables directly. It reads entirely from the `Spell.dbc` file (and related DBCs like `SpellDuration`, `SpellCastTimes`, `SpellRange`). The MAP indicates no tables are touched.

**Notable Implementation Details**

*   **Complex Positivity Logic**: The determination of whether a spell is "positive" is highly nuanced. It's not just a single flag. `IsPositiveEffect` contains extensive switch-case logic handling exceptions like suicide (self-instakill), Warlock sacrifices, and dispels (which depend on faction). This complexity is necessary because a single spell can have mixed effects (e.g., heal self, damage enemy).
*   **Diminishing Returns Evolution**: The DR logic includes preprocessor directives (`#if SUPPORTED_CLIENT_BUILD > ...`) to handle changes in WoW patch history. For example, Hunter Freezing Traps were added to DR in patch 1.10, and Warlock Seduction was grouped with Fear in 1.4. The code must emulate the correct behavior for the supported client build.
*   **Cast Time Modifiers**: `GetCastTime` carefully handles various sources of cast time modification: spell mods (from buffs like Nature's Grace), unit float values (cast speed stats), and fixed penalties (ranged slot). It also has special cases for trade window applications and item procs to ensure instant casting.
*   **Coefficient Calculations**: `CalculateCustomCoefficient` contains hardcoded logic for specific class mechanics (Paladin Seals, Shaman set bonuses). This suggests that while much of spell logic is data-driven, some class-specific nuances are hard-coded in the engine.
*   **Stacking Rules**: The stacking logic is split between general aura stacking (handled elsewhere) and specific "spell specific" stacking (handled here). Functions like `IsSingleFromSpellSpecificPerTarget` define strict exclusivity for categories like Blessings, Curses, and Aspects, preventing players from having multiple different blessings active simultaneously.

## Member Reference

**GetSpellSpecific**: Determines the specific gameplay category of a spell (e.g., Food, Seal, Polymorph) using family flags, aura types, and explicit IDs. Calls `SpellMgr/GetSpellElixirSpecific`, `SpellMgr/GetSpellEntry`.
**GetFirstEffectIndexInMask**: Static helper finding the first set bit in an effect mask.
**GetDiminishingReturnsGroupType**: Static helper mapping a DR group to a DR type (Player, All, None).
**GetDiminishingRate**: Static helper returning the duration multiplier for a given DR level.
**GetSpellRadius**: Static helper extracting radius from `SpellRadiusEntry`.
**GetSpellMinRange**: Static helper extracting min range from `SpellRangeEntry`.
**GetSpellMaxRange**: Static helper extracting max range from `SpellRangeEntry`.
**IsSingleFromSpellSpecificPerTargetPerCaster**: Static helper defining strict stacking rules for specific spell types (same caster).
**IsSingleFromSpellSpecificSpellRanksPerTarget**: Static helper defining strict stacking rules for specific spell types (any caster, same target).
**IsSingleFromSpellSpecificPerTarget**: Static helper defining strict stacking rules for specific spell types (any caster, any target).
**CompareAuraRanks**: Compares two spells by ID to determine rank/power priority. Calls `SpellMgr/GetSpellEntry`.
**IsFriendlyTarget**: Static helper classifying implicit target types as friendly.
**CompareSpellSpecificAuras**: Compares two `SpellEntry` pointers to see if one is superior for stacking.
**IsPositiveTarget**: Static helper classifying implicit target types as positive/negative.
**IsAutocastable**: Static wrapper checking if the spell can be autocast by AI/pets. Calls `SpellMgr/GetSpellEntry`.
**IsPassiveSpell**: Static wrapper checking if the spell is passive. Calls `SpellMgr/GetSpellEntry`.
**IsPositiveSpell**: Static wrapper determining if a spell is beneficial. Calls `SpellMgr/GetSpellEntry`.
**IsPositiveSpell#2**: Static wrapper taking caster/victim for context-aware positivity check. Calls `SpellMgr/GetSpellEntry`.
**IsSingleTargetSpells**: Determines if two spells are mutually exclusive on a single target. Calls `GetSpellSpecific`.
**IsExplicitPositiveTarget**: Static helper identifying targets that require manual selection and are positive.
**IsExplicitNegativeTarget**: Static helper identifying targets that require manual selection and are negative.
**IsExplicitlySelectedUnitTarget**: Static helper identifying targets that require manual unit selection.
**GetDiminishingReturnsGroup**: Calculates the DR group for a spell based on family, mechanics, and client build. Calls `GetAllSpellMechanicMask`.
**IsIgnoreLosTarget**: Static helper identifying targets that ignore Line of Sight.
**IsCasterSourceTarget**: Static helper identifying targets originating from caster position.
**IsPointEffectTarget**: Static helper identifying targets at specific world locations.
**IsScriptTarget**: Static helper identifying targets determined by scripts.
**IsAreaEffectPossitiveTarget**: Static helper identifying positive area-of-effect targets.
**IsAreaEffectTarget**: Static helper identifying area-of-effect targets.
**IsAreaAuraEffect**: Static helper identifying effects that apply area auras.
**GetAllowedTargetMaskForTargetType**: Static helper mapping target types to valid target flag bitmasks.
**GetWeaponAttackType**: Determines the weapon attack type (Melee, Ranged, Off-hand) for the spell.
**GetCastTime**: Calculates the actual cast time in ms, applying modifiers and special cases. Calls `Spell/IsTriggeredSpellWithRedundentData`, `Unit/GetSpellModOwner`,

---

<!-- machine-true, projected from graph.json -->

## Map — SpellEntry

*Source:* SpellEntry.cpp, SpellEntry.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GetSpellSpecific | function | SpellMgr/GetSpellElixirSpecific, SpellMgr/GetSpellEntry, SpellMgr/Instance | ChatHandler.DebugCommands/HandleSpellInfosCommand, Unit.Main/IsPolymorphed, Unit.Main/RemoveNoStackAurasDueToAuraHolder | — |
| GetFirstEffectIndexInMask | function | — | Spell.Main/DoSpellHitOnUnit | — |
| GetDiminishingReturnsGroupType | function | — | Spell.Main/DoSpellHitOnUnit, Unit.Main/ApplyDiminishingToDuration | — |
| GetDiminishingRate | function | — | Unit.Main/ApplyDiminishingToDuration, Unit.SpellAuras/Update#4 | — |
| GetSpellRadius | function | — | PartyBotAI/CanTryToCastSpell, Spell.Effects/EffectPersistentAA, Spell.Effects/EffectSummonGuardian, Spell.Effects/EffectSummonWild, Spell.Effects/EffectTeleUnitsFaceCaster, Spell.Effects/EffectTransmitted, Spell.Main/SetTargetMap, Unit.SpellAuras/AreaAura, Unit.SpellAuras/PeriodicDummyTick | — |
| GetSpellMinRange | function | — | Spell.Effects/EffectTransmitted, Spell.Main/CheckRange | — |
| GetSpellMaxRange | function | — | GameObject/TriggerLinkedGameObject, Spell.Effects/EffectTransmitted, Spell.Main/CheckRange, Spell.Main/SetTargetMap, TotemAI/UpdateAI, Unit.SpellAuras/TriggerSpell, Unit.SpellAuras/Update#4 | — |
| IsSingleFromSpellSpecificPerTargetPerCaster | function | — | Unit.Main/RemoveNoStackAurasDueToAuraHolder | — |
| IsSingleFromSpellSpecificSpellRanksPerTarget | function | — | Unit.Main/RemoveNoStackAurasDueToAuraHolder | — |
| IsSingleFromSpellSpecificPerTarget | function | — | Unit.Main/RemoveNoStackAurasDueToAuraHolder | — |
| CompareAuraRanks | function | SpellMgr/GetSpellEntry, SpellMgr/Instance | PlayerAI/PlayerControlledAI, Unit.Main/RemoveNoStackAurasDueToAuraHolder | — |
| IsFriendlyTarget | function | — | Spell.Main/DoSpellHitOnUnit | — |
| CompareSpellSpecificAuras | function | — | Unit.Main/RemoveNoStackAurasDueToAuraHolder | — |
| IsPositiveTarget | function | — | Spell.Main/cast | — |
| IsAutocastable | function | SpellMgr/GetSpellEntry, SpellMgr/Instance | WorldSession.PetHandler/HandlePetSpellAutocastOpcode | — |
| IsPassiveSpell | function | SpellMgr/GetSpellEntry, SpellMgr/Instance | Pet.Main/CanTakeMoreActiveSpells, Pet.Main/InitPetCreateSpells, Pet.Main/ToggleAutocast, Player.Main/RemoveSpell, Player.Main/ResetTalents, Unit.Main/InitCharmCreateSpells, Unit.Main/InitPossessCreateSpells, Unit.Main/ToggleCreatureAutocast, Unit.SpellAuras/SpellAuraHolder | — |
| IsPositiveSpell | function | SpellMgr/GetSpellEntry, SpellMgr/Instance | PlayerAI/PlayerControlledAI, Unit.AuraProcHandler/HandleProcTriggerSpellAuraProc, Unit.Main/InitCharmCreateSpells, Unit.SpellAuras/HandleAuraModSchoolImmunity, Unit.SpellAuras/HandleAuraTransform, WorldSession.SpellHandler/HandleCancelAuraOpcode | — |
| IsExplicitPositiveTarget | function | — | Spell.Main/CheckCast, SpellMgr/SelectAuraRankForLevel, Unit.Main/IsImmuneToSpell | — |
| IsPositiveSpell#2 | function | SpellMgr/GetSpellEntry, SpellMgr/Instance | — | — |
| IsSingleTargetSpells | function | — | Unit.Main/AddSpellAuraHolder | — |
| IsExplicitNegativeTarget | function | — | Spell.Main/CheckCast | — |
| IsExplicitlySelectedUnitTarget | function | — | Spell.Main/CheckCast, Spell.Main/CheckPetCast, Spell.Main/CheckPower, Spell.Main/prepare#2, WorldSession.PetHandler/HandlePetAction, WorldSession.SpellHandler/HandleCastSpellOpcode | — |
| GetDiminishingReturnsGroup | method | — | Spell.Main/DoSpellHitOnUnit, SpellMgr/IsCCSpell | — |
| IsIgnoreLosTarget | function | — | — | — |
| IsCasterSourceTarget | function | — | SpellMgr/IsSpellWithCasterSourceTargetsOnly | — |
| IsPointEffectTarget | function | — | — | — |
| IsScriptTarget | function | — | Spell.Main/CheckCast, Spell.Main/CheckTarget | — |
| IsAreaEffectPossitiveTarget | function | — | SpellMgr/SelectAuraRankForLevel | — |
| IsAreaEffectTarget | function | — | Spell.Main/CheckCast, SpellMgr/IsAreaOfEffectSpell | — |
| IsAreaAuraEffect | function | — | ChatHandler.UnitCommands/HandleAuraHelper, Spell.Main/CanAutoCast, Spell.Main/FillTargetMap, SpellMgr/HasAreaAuraEffect, Unit.Main/AddAura, Unit.SpellAuras/CreateAura | — |
| GetAllowedTargetMaskForTargetType | function | — | SpellMgr/GetAllowedTargetMask | — |
| GetWeaponAttackType | method | — | Spell.Main/HandleThreatSpells, Spell.Main/Spell, Spell.Main/Spell#2, Unit.SpellAuras/CalculateDotDamage, Unit.SpellAuras/PeriodicTick | — |
| GetCastTime | method | Object/GetFloatValue, Object/GetTypeId, Object/ToUnit, Player.Main/GetTradeData, Spell.Main/GetCaster, Spell.Main/IsAutoRepeat, Spell.Main/IsCastByItem, Spell.Main/IsTriggered, Spell.Main/IsTriggeredSpellWithRedundentData, SpellCastTargetsInfo/getItemTarget, TradeData/GetItem, TradeData/GetTraderData, Unit.Main/GetSpellModOwner, Unit.Main/GetSpellRank | CombatBotBaseAI/DoCastSpell, CombatBotBaseAI/FindAndPreHealTarget, Creature.Main/AddCooldown, Spell.Main/prepare#2, Totem/SetTypeBySummonSpell, TotemAI/TotemAI, Unit.AuraProcHandler/HandleModCastingSpeedNotStackAuraProc | — |
| GetDispellMask | function | — | CombatBotBaseAI/IsValidDispelTarget, Spell.Effects/EffectDispel, Spell.Main/CheckCast, Spell.Main/CheckCasterAuras, Unit.Main/RemoveAurasWithDispelType | — |
| IsEffectAppliesAura | function | — | SpellMgr/IsSpellAppliesAura | — |
| IsDirectDamageEffect | function | — | SpellMgr/IsDirectDamageSpell | — |
| IsEffectThatCanCrit | function | — | — | — |
| GetCastTimeForBonus | method | — | — | — |
| IsDirectDamageWithBonusEffect | function | — | Spell.Main/DoAllEffectOnTarget#3, Spell.Main/HandleDelayedSpellLaunch | — |
| IsThreatEffect | function | — | — | — |
| IsSummonEffect | function | — | Spell.Main/AddUnitTarget#2 | — |
| SpellEntry | ctor | — | — | — |
| ~SpellEntry | dtor | — | — | — |
| CalculateDefaultCoefficient | method | — | ChatHandler.DebugCommands/HandleDebugSpellCoefsCommand, SpellCaster/SpellBonusWithCoeffs | — |
| CalculateCustomCoefficient | method | game_Objects_Item/isOneHandedWeapon, Object/IsPlayer, Object/ToUnit#2, Player.Main/GetItemByPos, Spell.Main/GetTargetNum, Unit.Main/GetSpellModOwner | SpellCaster/SpellBonusWithCoeffs | — |
| IsFitToFamilyMask | method | — | Spell.Main/prepareDataForTriggerSystem, SpellMgr/CheckUsedSpells, SpellMgr/IsNoStackSpellDueToSpell, SpellModifier/IsAffectedOnSpell, Totem/IsImmuneToSpellEffect, Unit.Main/HandleTriggers, Unit.SpellAuras/IsAffectedOnSpell | — |
| IsFitToFamily | method | — | Player.Main/CastHighestStealthRank, Unit.Main/GetAura, Unit.SpellAuras/_RemoveSpellAuraHolder | — |
| GetDuration | method | — | Pet.Main/LoadPetFromDB, PetAI/UpdateAI, Spell.Effects/EffectAddFarsight, Spell.Effects/EffectDuel, Spell.Effects/EffectEnchantHeldItem, Spell.Effects/EffectInterruptCast, Spell.Effects/EffectSummonObject, Spell.Effects/EffectSummonObjectWild, Spell.Effects/EffectSummonPossessed, Spell.Effects/EffectSummonWild, Spell.Effects/EffectTransmitted, Unit.SpellAuras/CleanupTriggeredSpells | — |
| GetMaxDuration | method | — | — | — |
| GetAllSpellMechanicMask | method | — | Spell.Effects/EffectScriptEffect, Unit.Main/RemoveSpellsCausingAuraWithMechanic | — |
| CalculateDuration | method | AuraScript/OnDurationCalculate, Object/ToPlayer#2, Object/ToUnit#2, Player.Main/GetComboPoints, Unit.Main/GetSpellModOwner | Spell.Main/prepare#2, Unit.SpellAuras/SpellAuraHolder | — |
| GetEffectsCount | method | — | Spell.Main/CheckCast | — |
| HasAttribute | method | — | ChatHandler.CharacterCommands/HandleLearnAllCommand, ChatHandler.UnitCommands/HandleFearCommand, CombatBotBaseAI/PopulateSpellData, Creature.Main/IsImmuneToDamage, Creature.Main/IsImmuneToSpell, Creature.Main/StartCooldownForSummoner, Player.Main/AddCooldown, Player.Main/AddGCD, Player.Main/LockOutSpells, Player.Main/RemoveSpellLockout, Spell.Main/CanAutoCast, Spell.Main/CheckCast, Spell.Main/CheckItems, Spell.Main/DoSpellHitOnUnit, Spell.Main/finish, Spell.Main/SendCastResult#2, Spell.Main/SendSpellCooldown, SpellCaster/CalculateSpellEffectValue, SpellCaster/SpellHealingBonusDone, SpellMgr/IsPvEHeartBeat, SpellMgr/IsReflectableSpell, Unit.AuraProcHandler/HandleInvisibilityAuraProc, Unit.Main/AddGameObject, Unit.Main/CalculateDamageAbsorbAndResist, Unit.Main/HandleTriggers, Unit.Main/IsImmuneToDamage, Unit.Main/IsImmuneToSpell, Unit.Main/IsSpellPartiallyBlocked, Unit.Main/RemoveAurasAtMechanicImmunity, Unit.Main/RemoveGameObject, Unit.SpellAuras/CalculateHeartBeat, Unit.SpellAuras/HandleAuraModSchoolImmunity, Unit.SpellAuras/SetAuraFlag, Unit.SpellAuras/TriggerSpell, Unit.SpellAuras/_RemoveSpellAuraHolder, WorldSession.SpellHandler/HandleCancelAuraOpcode | — |
| HasAttribute#3 | method | — | CombatBotBaseAI/UseItemEffect, Player.Main/CastItemUseSpell, Player.Main/IsNeedCastPassiveLikeSpellAtLearn, Spell.Main/AddUnitTarget#2, Spell.Main/cast, Spell.Main/CheckCast, Spell.Main/DoAllEffectOnTarget#3, Spell.Main/DoSpellHitOnUnit, Spell.Main/FillTargetMap, Spell.Main/finish, Spell.Main/GetSpellBatchingEffectDelay, Spell.Main/SendChannelStart, Spell.Main/SendChannelUpdate, SpellMgr/IsNoStackSpellDueToSpell, SpellMgr/IsReflectableSpell, ThreatManager/addThreat#4, Unit.Main/ApplySpellDispelImmunity, Unit.Main/IsImmuneToDamage, Unit.Main/IsImmuneToSchool, Unit.Main/IsImmuneToSpell, Unit.Main/IsImmuneToSpellEffect, Unit.SpellAuras/HandleAuraModSchoolImmunity, Unit.SpellAuras/HandleAuraModStateImmunity, Unit.SpellAuras/HandleModMechanicImmunity, Unit.SpellAuras/HandleModMechanicImmunityMask, Unit.SpellAuras/Update#4, Unit.SpellAuras/_AddSpellAuraHolder, Unit.SpellAuras/_RemoveSpellAuraHolder, WorldSession.PetHandler/HandlePetAction, WorldSession.SpellHandler/HandleCancelAuraOpcode | — |
| HasAttribute#4 | method | — | ChatHandler.CharacterCommands/HandleLearnAllCommand, Player.Main/AddCooldown, Player.Main/IsNeedCastPassiveLikeSpellAtLearn, Spell.Main/cast, Spell.Main/CheckCast, Spell.Main/CheckItems, Spell.Main/DoSpellHitOnUnit, Spell.Main/FillTargetMap, Spell.Main/finish, Spell.Main/OnSpellLaunch, Spell.Main/prepare#2, Spell.Main/SendCastResult#2, Spell.Main/SetTargetMap, Spell.Main/TakePower, SpellMgr/LoadSpell, Unit.Main/IsImmuneToDamage, Unit.Main/IsImmuneToSchool, Unit.Main/IsImmuneToSpell, Unit.Main/ProcDamageAndSpellFor, Unit.SpellAuras/HandleAuraDummy, Unit.SpellAuras/TriggerSpell, Unit.SpellAuras/Update#4 | — |
| HasAttribute#5 | method | — | Spell.Effects/EffectTriggerSpell, Spell.Main/AddUnitTarget#2, Spell.Main/cast, Spell.Main/CheckCast, Spell.Main/CheckItems, Spell.Main/CheckTarget, Spell.Main/DoAllEffectOnTarget#3, Spell.Main/DoSpellHitOnUnit, Spell.Main/IsTriggeredByProc, Spell.Main/prepareDataForTriggerSystem, Spell.Main/SendResurrectRequest, Spell.Main/SetTargetMap, Spell.Main/update, SpellCaster/CalculateSpellDamage, SpellCaster/DealSpellDamage, SpellCaster/MeleeDamageBonusDone, SpellCaster/MeleeSpellHitResult, SpellCaster/SpellDamageBonusDone, SpellCaster/SpellHealingBonusDone, SpellMgr/GetSpellAllowedInLocationError, Unit.AuraProcHandler/IsTriggeredAtSpellProcEvent, Unit.Main/AddSpellAuraHolder, Unit.Main/IsSpellCrit, Unit.Main/IsSpellPartiallyBlocked, Unit.Main/Kill, Unit.Main/ProcDamageAndSpellFor, Unit.SpellAuras/Update, Unit.SpellAuras/Update#3 | — |
| HasAttribute#6 | method | — | HostileRefManager/threatAssist, Spell.Main/HandleAddTargetTriggerAuras, Unit.Main/AddThreat, Unit.Main/MeleeDamageBonusTaken, Unit.Main/SpellDamageBonusTaken | — |
| HasAttribute#2 | method | — | — | — |
| HasSpellInterruptFlag | method | — | Spell.Effects/EffectInterruptCast, Spell.Main/CheckCasterAuras, Spell.Main/Delayed, Spell.Main/update, SpellCaster/IsNoMovementSpellCasted, Unit.Main/DealDamage | — |
| HasAuraInterruptFlag | method | — | PartyBotAI/CanUseCrowdControl, Player.Main/HandleFoodEmotes, Player.Main/SaveAura, PlayerAI/PlayerControlledAI, Spell.Main/CheckCast, Spell.Main/DoAllEffectOnTarget#3, Spell.Main/update, Unit.Main/HasBreakableByDamageAuraType, Unit.Main/IsImmuneToSpell, Unit.SpellAuras/HandleAuraModEffectImmunity, Unit.SpellAuras/PeriodicTick, Unit.SpellAuras/SpellAuraHolder, Unit.SpellAuras/_AddSpellAuraHolder | — |
| HasChannelInterruptFlag | method | — | Spell.Effects/EffectInterruptCast, Spell.Main/update, SpellCaster/IsNoMovementSpellCasted, Unit.Main/DealDamage | — |
| HasEffect | method | — | ChatHandler.CharacterCommands/HandleLearnAllCommand, ChatHandler.CharacterCommands/HandleLearnAllItemsCommand, ChatHandler.UnitCommands/HandleAuraHelper, Creature.Main/CancelSummonPossessedCharm, ObjectMgr/CheckCreatureTemplate, PetAI/UpdateAI, Player.Main/AddSpell, Player.Main/CastItemCombatSpell, ScriptMgr/LoadScripts, Spell.Main/CanAutoCast, Spell.Main/CheckCast, Spell.Main/DoSpellHitOnUnit, Spell.Main/handle_immediate, SpellCaster/MeleeDamageBonusDone, SpellMgr/CheckUsedSpells, SpellMgr/IsCCSpell, SpellMgr/IsNonPeriodicDispel, SpellMgr/LoadSpellLearnSpells, Unit.AuraProcHandler/HandleOverrideClassScriptAuraProc, Unit.AuraProcHandler/HandleProcTriggerSpellAuraProc, Unit.Main/AddAura, Unit.Main/MeleeDamageBonusTaken, Unit.Main/RemoveNoStackAurasDueToAuraHolder, Unit.Main/RemoveSummonPossessedAuras | — |
| GetAuraMaxTicks | method | — | SpellCaster/SpellDamageBonusDone | — |
| IsSpellAppliesAura | method | — | CombatBotBaseAI/CanTryToCastSpell, Creature.Main/ApplyGameEventSpells, Spell.Main/CheckCast, TotemAI/TotemAI, Unit.AuraProcHandler/IsTriggeredAtSpellProcEvent | — |
| IsSpellAppliesAura#2 | method | — | ChatHandler.UnitCommands/HandleAuraHelper, ScriptMgr/LoadScripts, Spell.Main/DoAllEffectOnTarget#3, Spell.Main/DoSpellHitOnUnit, Totem/IsImmuneToSpellEffect, Unit.Main/AddAura | — |
| IsSpellAppliesPeriodicAura | method | — | SpellMgr/IsSpellProcEventCanTriggeredBy | — |
| GetRank | method | — | CombatBotBaseAI/PopulateSpellData | — |
| IsEffectHandledOnDelayedSpellLaunch | method | — | Spell.Main/DoAllEffectOnTarget#3, Spell.Main/HandleDelayedSpellLaunch | — |
| IsPositiveSpell#3 | method | — | PlayerAI/UpdateAI#2, Spell.Main/CheckAtDelay, Spell.Main/CheckCast, Spell.Main/CheckPetCast, Spell.Main/DoSpellHitOnUnit, Spell.Main/SetTargetMap, SpellCaster/SpellHitResult, SpellMgr/AssignInternalSpellFlags, WorldSession.PetHandler/HandlePetAction, WorldSession.SpellHandler/HandleCastSpellOpcode | — |
| IsDelayableEffect | method | — | SpellMgr/IsSpellWithDelayableEffects | — |
| IsPositiveEffect | method | CharmInfo/GetOriginalFactionTemplate, FactionTemplateEntry/IsFriendlyTo, Object/IsUnit, SpellMgr/GetSpellEntry, SpellMgr/Instance, Unit.Main/GetCharmInfo, WorldObject.Object/GetFactionTemplateEntry, WorldObject.Object/IsFriendlyTo | DynamicObject/Create, Player.Main/IsImmuneToSpellEffect, Spell.Main/prepareDataForTriggerSystem, SpellCaster/SpellHitResult, SpellMgr/SelectAuraRankForLevel, Unit.Main/IsImmuneToSpellEffect, Unit.SpellAuras/Aura | — |
| IsPeriodicRegenerateEffect | method | — | Totem/IsImmuneToSpellEffect | — |
| HasAura | method | — | CombatBotBaseAI/BreakCrowdControlEffects, CreatureEventAI/SpellHit, PetAI/UpdateAI, Player.Main/HandleFoodEmotes, Spell.Main/DoAllEffectOnTarget#3, Spell.Main/DoSpellHitOnUnit, Spell.Main/SendChannelUpdate, Spell.Main/update, SpellMgr/CheckUsedSpells, SpellMgr/IsCharmSpell, Unit.AuraProcHandler/HandleModDamageAuraProc, Unit.Main/DealDamage, Unit.Main/RemoveNoStackAurasDueToAuraHolder, Unit.SpellAuras/SpellAuraHolder | — |
| HasSingleAura | method | — | — | — |
| IsCustomSpell | method | — | Unit.SpellAuras/IsNeedVisibleSlot | — |
| IsSpellWithDelayableEffects | method | — | Spell.Main/prepare#2 | — |
| IsNextMeleeSwingSpell | method | — | Spell.Main/CheckRange, Spell.Main/DoAllEffectOnTarget, Spell.Main/DoAllEffectOnTarget#3, Spell.Main/GetCurrentContainer, Spell.Main/prepareDataForTriggerSystem, Spell.Main/update, SpellCaster/InterruptSpellsWithInterruptFlags, SpellCaster/IsNextSwingSpellCasted, SpellMgr/IsSpellWithDelayableEffects, Unit.Main/AttackerStateUpdate | — |
| IsRangedSpell | method | — | Spell.Main/SendSpellGo, Spell.Main/SendSpellStart, SpellMgr/IsSpellWithDelayableEffects | — |
| IsSealSpell | method | — | Spell.Effects/EffectScriptEffect, SpellMgr/IsNoStackSpellDueToSpell, Unit.SpellAuras/_AddSpellAuraHolder, Unit.SpellAuras/_RemoveSpellAuraHolder | — |
| IsElementalShield | method | — | — | — |
| IsFromBehindOnlySpell | method | — | Spell.Main/CheckCast | — |
| IsPassiveSpell#2 | method | — | ChatHandler.LookupCommands/ShowSpellListHelper, Pet.Main/AddSpell, Player.Main/IsActionButtonDataValid, Spell.Main/CheckCast, Spell.Main/SendCastResult#2, SpellMgr/IsPassiveSpellStackableWithRanks, SpellMgr/LoadSpellLearnSpells, SpellMgr/SelectAuraRankForLevel, Unit.Main/ModifyAuraState, WorldSession.PetHandler/HandlePetAction, WorldSession.PetHandler/HandlePetCastSpellOpcode, WorldSession.SpellHandler/HandleCancelAuraOpcode, WorldSession.SpellHandler/HandleCastSpellOpcode | — |
| IsPassiveSpellStackableWithRanks | method | — | Unit.Main/AddSpellAuraHolder, Unit.Main/RemoveNoStackAurasDueToAuraHolder | — |
| IsIgnoringCasterAndTargetRestrictions | method | — | Creature.Main/IsImmuneToDamage, Creature.Main/IsImmuneToSpell, Creature.Main/IsImmuneToSpellEffect, Spell.Main/CheckCasterAuras, Spell.Main/CheckTarget, Spell.Main/update, Unit.Main/IsImmuneToDamage, Unit.Main/IsImmuneToSchool, Unit.Main/IsImmuneToSpell, Unit.Main/IsImmuneToSpellEffect | — |
| IsDeathOnlySpell | method | — | Spell.Main/CheckCast | — |
| CanTargetDeadTarget | method | — | Spell.Effects/EffectApplyAura, Unit.Main/AddSpellAuraHolder | — |
| CanTargetAliveState | method | — | Spell.Main/CheckCast, Spell.Main/HasValidUnitPresentInTargetList, Spell.Main/SetTargetMap | — |
| IsDeathPersistentSpell | method | — | Spell.Effects/EffectApplyAura, Unit.Main/AddSpellAuraHolder, Unit.SpellAuras/SpellAuraHolder | — |
| IsNonCombatSpell | method | — | PetAI/UpdateAI, Spell.Main/CheckCast, Spell.Main/CheckPetCast, Unit.Main/SetInCombatState, WorldSession.SpellHandler/HandleUseItemOpcode | — |
| IsPositiveSpell#4 | method | — | ChatHandler.DebugCommands/HandleSpellInfosCommand, ChatHandler.PlayerBotMgr/HandlePartyBotAoECommand, CombatBotBaseAI/UseItemEffect, Creature.Main/IsImmuneToSpell, Creature.Main/TryToCast, CritterAI/SpellHit, PartyBotAI/CanTryToCastSpell, Pet.Main/_LoadAuras, PetAI/UpdateAI, Player.Main/CastItemCombatSpell, Spell.Main/CheckCast, Spell.Main/CheckTarget, Spell.Main/DoAllEffectOnTarget#3, Spell.Main/DoSpellHitOnUnit, Spell.Main/finish, Spell.Main/OnSpellLaunch, Spell.Main/prepareDataForTriggerSystem, SpellMgr/IsNoStackSpellDueToSpell, SpellMgr/IsReflectableSpell, Totem/IsImmuneToSpellEffect, Unit.AuraProcHandler/HandleInvisibilityAuraProc, Unit.AuraProcHandler/IsTriggeredAtSpellProcEvent, Unit.Main/IsImmuneToDamage, Unit.Main/IsImmuneToSchool, Unit.Main/IsImmuneToSpell, Unit.Main/IsImmuneToSpellEffect, Unit.Main/IsSpellCrit, Unit.SpellAuras/HandleAuraTransform, Unit.SpellAuras/SpellAuraHolder | — |
| IsPositiveEffectMask | method | — | Spell.Main/cast, Unit.Main/IsImmuneToSchool | — |
| IsHealSpell | method | — | PartyBotAI/UpdateAI, Spell.Main/CanAutoCast, Spell.Main/CheckCast, Spell.Main/prepareDataForTriggerSystem, Unit.AuraProcHandler/HandleModDamageAuraProc | — |
| IsDirectDamageSpell | method | — | CritterAI/SpellHit, Spell.Main/prepare#2, Unit.AuraProcHandler/HandleModDamageAuraProc | — |
| HasSingleTargetAura | method | — | PartyBotAI/CanUseCrowdControl, Pet.Main/_LoadAuras, Unit.SpellAuras/SpellAuraHolder | — |
| IsAuraRemovedOnEvade | method | — | Creature.Main/RemoveAurasAtReset | — |
| IsSpellWithCasterSourceTargetsOnly | method | — | Spell.Effects/EffectTriggerSpell, Spell.Main/CheckCast | — |
| IsAreaOfEffectSpell | method | — | ChatHandler.PlayerBotMgr/HandlePartyBotAoECommand, Creature.Main/TryToCast, PartyBotAI/CanTryToCastSpell, Spell.Main/CheckCast, Spell.Main/prepareDataForTriggerSystem, SpellCaster/MagicSpellHitChance, Unit.AuraProcHandler/HandleProcTriggerSpellAuraProc, Unit.Main/RemoveSpellAuraHolder | — |
| HasAreaAuraEffect | method | — | WorldSession.SpellHandler/HandleCancelAuraOpcode | — |
| IsDismountSpell | method | — | Creature.Main/TryToCast | — |
| IsCharmSpell | method | — | CombatBotBaseAI/IsValidDispelTarget, Creature.Main/TryToCast, Spell.Effects/EffectDispel, Spell.Main/CheckCast, Unit.Main/InitCharmCreateSpells, Unit.Main/InitPossessCreateSpells | — |
| IsReflectableSpell | method | — | Spell.Main/Spell, Spell.Main/Spell#2 | — |
| IsReflectableSpell#2 | method | — | Spell.Main/Spell, Spell.Main/Spell#2 | — |
| GetErrorAtShapeshiftedCast | method | DBCStores/GetTalentSpellCost#2, Log.Main/Out | CombatBotBaseAI/CanTryToCastSpell, Player.Main/ApplyEquipSpell, Player.Main/IsNeedCastPassiveLikeSpellAtLearn, Spell.Main/CheckCast | — |
| IsDispel | method | — | Spell.Main/CheckCast, Spell.Main/DoAllEffectOnTarget#3 | — |
| IsBinary | method | — | ChatHandler.DebugCommands/HandleSpellInfosCommand, SpellCaster/MagicSpellHitChance, Unit.Main/CalculateDamageAbsorbAndResist | — |
| IsNonPeriodicDispel | method | — | Spell.Main/CheckCast | — |
| IsPvEHeartBeat | method | — | ChatHandler.DebugCommands/HandleSpellInfosCommand, Unit.SpellAuras/CalculateHeartBeat | — |
| IsCCSpell | method | — | SpellMgr/IsSpellWithDelayableEffects | — |
| IsAutocastable#2 | method | — | PetAI/UpdateAI | — |
| IsAutoRepeatRangedSpell | method | — | Spell.Main/CheckCast, Spell.Main/Spell, Spell.Main/Spell#2 | — |
| IsSpellRequiresRangedAP | method | — | — | — |
| IsChanneledSpell | method | — | ChatHandler.DebugCommands/HandleDebugSendNextChannelSpellVisualCommand, ChatHandler.DebugCommands/HandleSendSpellVisualCommand, DynamicObject/Create, Pet.Main/_SaveAuras, Player.Main/SaveAura, Spell.Main/Execute#2, Spell.Main/handle_immediate, Spell.Main/Spell, Spell.Main/Spell#2, SpellMgr/IsCCSpell, SpellMgr/IsSpellWithDelayableEffects, Unit.Main/AddSpellAuraHolder, Unit.Main/DealDamage, Unit.SpellAuras/SpellAuraHolder, WorldSession.SpellHandler/HandleCancelAuraOpcode | — |
| IsTargetInRange | method | SpellMgr/GetSpellEntry, SpellMgr/Instance, WorldObject.Object/CanReachWithMeleeSpellAttack, WorldObject.Object/GetCombatDistance | BattleBotAI.Main/UpdateFlagCarrierAI, ChatHandler.PlayerBotMgr/HandlePartyBotAoECommand, Creature.Main/MeetsSelectAttackingRequirement, PartyBotAI/SelectResurrectionTarget, Spell.Effects/EffectFeedPet | — |
| NeedsComboPoints | method | — | Spell.Main/CheckPower, Spell.Main/finish | — |
| IsTotemSummonSpell | method | — | — | — |
| HasRealTimeDuration | method | — | Player.Main/LoadAura | — |
| HasAuraWithSpellTriggerEffect | method | — | Unit.Main/DealDamage | — |
| CanCrit | method | — | Spell.Main/AddUnitTarget#2 | — |
| HasAuraOrTriggersAnotherSpellWithAura | method | SpellMgr/GetSpellEntry, SpellMgr/Instance | Unit.Main/IsImmuneToSpell | — |
| HasDirectThreatIncreaseEffect | method | — | Spell.Main/DoSpellHitOnUnit | — |
| CanTriggerWeaponProcs | method | — | Spell.Main/DoAllEffectOnTarget#3 | — |
| IsNeedFaceTarget | method | — | WorldSession.PetHandler/HandlePetAction | — |
| IsNeedCastSpellAtFormApply | method | — | Player.Main/CheckAreaExploreAndOutdoor, Player.Main/IsNeedCastPassiveLikeSpellAtLearn, Unit.SpellAuras/HandleShapeshiftBoosts | — |
| IsNeedCastSpellAtOutdoor | method | — | Player.Main/CheckAreaExploreAndOutdoor | — |
| IsTargetPowerTypeValid | method | — | Creature.Main/TryToCast | — |
| IsRemovedOnShapeLostSpell | method | — | Unit.SpellAuras/HandleShapeshiftBoosts, Unit.SpellAuras/SpellAuraHolder | — |
| GetSpellSchoolMask | method | — | CombatBotBaseAI/IsValidDispelTarget, Creature.Main/TryToCast, HostileRefManager/threatAssist, Player.Main/LockOutSpells, Player.Main/RemoveSpellLockout, Spell.Effects/EffectEnvironmentalDMG, Spell.Effects/EffectInterruptCast, Spell.Effects/EffectThreat, Spell.Main/CalculatePowerCost, Spell.Main/CheckAtDelay, Spell.Main/CheckCast, Spell.Main/CheckCasterAuras, Spell.Main/DoSpellHitOnUnit, Spell.Main/HandleThreatSpells, Spell.Main/Spell, Spell.Main/Spell#2, SpellCaster/IsSpellReady, SpellCaster/MagicSpellHitChance, SpellCaster/MeleeDamageBonusDone, SpellCaster/SpellDamageBonusDone, SpellCaster/SpellHealingBonusDone, SpellCaster/SpellHitResult, spell_mage/OnEffectExecute, spell_shaman/OnEffectExecute, Unit.AuraProcHandler/HandleDummyAuraProc, Unit.Main/CalculateAbsorbResistBlock, Unit.Main/IsImmuneToSchool, Unit.Main/IsImmuneToSpell, Unit.Main/MeleeDamageBonusTaken, Unit.Main/MeleeMissChanceCalc, Unit.Main/SpellDamageBonusTaken, Unit.Main/SpellHealingBonusTaken, Unit.Main/TriggerDamageShields, Unit.SpellAuras/HandleAuraModRoot, Unit.SpellAuras/HandleAuraModSchoolImmunity, Unit.SpellAuras/HandleAuraModStun, Unit.SpellAuras/HandleModCharm, Unit.SpellAuras/HandleSchoolAbsorb, Unit.SpellAuras/ModPossess, Unit.SpellAuras/PeriodicTick | — |
| GetSpellMechanicMask | method | — | Spell.Main/CheckCasterAuras | — |
| GetEffectMechanic | method | — | — | — |
| GetRecoveryTime | method | — | PetAI/UpdateAI, Spell.Effects/EffectDummy, spell_hunter/OnEffectExecute, spell_hunter/OnEffectExecute#2, spell_mage/OnEffectExecute | — |
| CalculateSimpleValue | method | — | ChatHandler.DebugCommands/HandleSpellEffectsCommand, Spell.Effects/EffectScriptEffect, Spell.Main/Spell, Spell.Main/Spell#2, SpellMgr/LoadSpellLearnSkills, SpellMgr/LoadSpellPetAuras, Unit.SpellAuras/Aura, Unit.SpellAuras/PeriodicTick, Unit.SpellAuras/Update | — |
| GetMechanic | method | — | — | — |
| GetManaCost | method | — | — | — |
| GetSpellFamilyName | method | — | — | — |
| GetAuraInterruptFlags | method | — | — | — |
| GetStackAmount | method | — | — | — |
| GetEffectImplicitTargetAByIndex | method | — | — | — |
| GetEffectImplicitTargetBByIndex | method | — | — | — |
| GetEffectApplyAuraNameByIndex | method | — | — | — |
| GetEffectMiscValue | method | — | — | — |
| GetSpellFamilyFlags | method | — | — | — |

---

<!-- verify: failed-members | missing: GetDispellMask, GetEffectsCount, IsEffectAppliesAura, ~SpellEntry -->
