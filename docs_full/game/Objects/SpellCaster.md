# SpellCaster

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# SpellCaster

`SpellCaster` is an abstract base class derived from `WorldObject` that provides the common infrastructure for spell casting, combat resolution, and cooldown management for entities capable of casting spells—primarily `Unit` (players and creatures) and `GameObject`. It does not contain AI logic or movement generation; rather, it implements the deterministic rules of the World of Warcraft combat engine regarding hit chances, damage/healing calculations, critical strikes, resistances, armor reduction, and the lifecycle of active spells (casting, channeling, interrupting, and finishing).

Because `Unit` and `GameObject` both inherit from `SpellCaster`, this unit centralizes logic that applies to both, such as calculating a caster's effective level for a target, determining weapon skill values, managing Global Cooldowns (GCD), and handling dynamic objects (visual effects like fireballs or frostbolts) associated with a cast.

## Purpose & Responsibilities

1.  **Spell Casting Lifecycle:** Manages the creation, targeting, and execution of spells via `CastSpell` and `CastCustomSpell` variants. It tracks currently active spells (`m_currentSpells`) to enforce rules about interrupting one spell with another (e.g., casting a generic spell interrupts a channeled spell).
2.  **Combat Resolution:** Implements the core mathematics for determining whether a spell or melee attack hits, misses, dodges, parries, blocks, or reflects. This includes `SpellHitResult`, `MeleeSpellHitResult`, and `MagicSpellHitResult`.
3.  **Damage & Healing Calculation:** Computes final damage and healing amounts by applying bonuses from stats, auras, equipment, and spell power. Key methods include `CalculateSpellDamage`, `MeleeDamageBonusDone`, `SpellDamageBonusDone`, and `SpellHealingBonusDone`. It handles critical strike bonuses and armor reduction.
4.  **Cooldown & GCD Management:** Tracks Global Cooldowns, spell-specific cooldowns, category cooldowns, and school-based lockouts. It ensures spells cannot be cast while on cooldown or locked out.
5.  **Proc System Coordination:** Acts as the entry point for triggering reactive auras and procs when damage or healing occurs. It uses `ProcSystemArguments` to pass context to `Unit` methods that actually evaluate and trigger auras.
6.  **Client Communication:** Sends network packets (`WorldPacket`) to clients to display combat logs, spell misses, heals, energizes, and damage numbers.

## Member-by-Member Behavior

### Spell Casting & Targeting

*   **CastSpell** (multiple overloads): Initiates a spell cast. It validates the spell ID, creates a `Spell` object, sets up targets (unit, game object, or coordinates), and calls `Spell::prepare`. Variants allow casting on a specific `SpellCaster` target, a `Unit` target, or at specific `(x, y, z)` coordinates. It handles triggered spells (from auras/items) by preserving the original caster and triggering aura context.
*   **CastCustomSpell**: Similar to `CastSpell` but allows overriding the base points of the spell effects (`bp0`, `bp1`, `bp2`). This is used for scripted spells or effects that need dynamic values not defined in the DBC.
*   **SelectMagnetTarget**: Checks if a spell should be redirected to a different target due to a "Spell Magnet" aura (e.g., Grounding Totem). If the victim has a magnet aura and the spell is magic-based, it redirects the spell to the magnet's caster, consuming a charge from the aura.
*   **GetCurrentSpell**: Returns the currently active spell of a specific type (Generic, Channeled, Autorepeat, Melee).
*   **FindCurrentSpellBySpellId**: Iterates through active spells to find one matching a specific ID.
*   **SetCurrentCastedSpell**: Updates the internal array of active spells. It automatically interrupts conflicting spells (e.g., starting a generic spell interrupts a channeled spell) unless specific exceptions apply (like Auto Shot).
*   **MoveChannelledSpellWithCastTime**: Handles the transition of a spell from the "Generic" slot to the "Channeled" slot when a channeled spell with a cast time begins its channeling phase.

### Spell Interruption & State

*   **InterruptSpell**: Cancels a specific active spell. It sends client notifications for autorepeat spells and cleans up the spell object.
*   **InterruptNonMeleeSpells**: Interrupts generic, autorepeat, and channeled spells. Used when a unit takes damage or moves, depending on spell flags.
*   **InterruptSpellsWithInterruptFlags**: Interrupts spells that have specific interrupt flags (e.g., damage, movement) set in their DBC data.
*   **InterruptSpellsWithChannelFlags**: Interrupts channeled spells based on channel interrupt flags.
*   **IsNonMeleeSpellCasted**: Checks if the caster is currently casting any non-melee spell (generic, channeled, or autorepeat). Used by AI and movement generators to determine if movement should be halted.
*   **IsNextSwingSpellCasted**: Checks if a melee swing spell (like Whirlwind) is active.
*   **IsNoMovementSpellCasted**: Checks if the current spell prevents movement.
*   **FinishSpell**: Marks a spell as finished, sending final channel updates if necessary.
*   **CheckAndIncreaseCastCounter**: Prevents infinite spell chains by limiting the number of triggered spells in a sequence.

### Combat Resolution (Hit/Miss/Reflect)

*   **SpellHitResult**: The main entry point for determining the outcome of a spell hit. It checks for evasion, immunity, reflection, and then delegates to `MagicSpellHitResult` or `MeleeSpellHitResult` based on the spell's damage class.
*   **MagicSpellHitResult**: Determines if a magic spell hits or is resisted. It uses `MagicSpellHitChance` to calculate the probability.
*   **MagicSpellHitChance**: Calculates the hit chance for magic spells based on level difference, spell school, victim's avoidance auras, and attacker's hit rating. It applies a minimum hit chance cap (22%) for large level differences.
*   **MeleeSpellHitResult**: Determines the outcome of melee/ranged spells (miss, dodge, parry, block). It calculates skill differences and rolls against various chances.
*   **MeleeSpellMissChance**: Calculates the base miss chance for melee attacks based on skill difference and level.
*   **GetSpellResistChance**: Calculates the percentage chance for a spell to be resisted based on the victim's resistance stats and the attacker's spell penetration.

### Damage & Healing Calculations

*   **CalculateSpellDamage**: Finalizes the damage value for a spell. It applies caster bonuses (`SpellDamageBonusDone` or `MeleeDamageBonusDone`), victim mitigation (armor), and critical strike bonuses.
*   **CalculateSpellEffectValue**: Computes the raw value of a spell effect before bonuses, including dice rolls, level scaling, and combo point additions.
*   **SpellDamageBonusDone**: Calculates the total damage bonus added by the caster, including flat spell power, percentage increases, and aura modifiers.
*   **SpellBaseDamageBonusDone**: Retrieves flat damage bonuses from auras and stats.
*   **SpellHealingBonusDone**: Calculates healing bonuses similar to damage bonuses.
*   **SpellBaseHealingBonusDone**: Retrieves flat healing bonuses from auras and stats.
*   **MeleeDamageBonusDone**: Calculates melee damage bonuses, including attack power conversion, weapon damage scaling, and pet happiness modifiers.
*   **SpellCriticalDamageBonus**: Adds the critical strike multiplier to damage (100% for melee/ranged, 50% for magic).
*   **SpellCriticalHealingBonus**: Adds the critical strike multiplier to healing.
*   **CalcArmorReducedDamage**: Applies armor reduction to physical damage using the standard WoW formula.
*   **GetAPMultiplier**: Determines the Attack Power multiplier for weapon damage based on weapon type (1h, 2h, ranged, dagger).
*   **SpellBonusWithCoeffs**: Applies spell power coefficients and level penalties to bonus damage/healing.
*   **CalculateLevelPenalty**: Calculates the penalty for casting a low-level spell on a high-level target.

### Procs & Events

*   **ProcDamageAndSpell**: Entry point for processing procs. It checks if the caster/victim should trigger skills/reactives immediately or delay them.
*   **ProcDamageAndSpell_real**: Executes the actual proc logic by calling `Unit::ProcDamageAndSpellFor` and `Unit::HandleTriggers`.
*   **ProcDamageAndSpell_delayed**: Processes procs that were delayed, ensuring the victim is still valid.
*   **UpdatePendingProcs**: Timer callback that processes delayed procs.
*   **ProcSystemArguments**: Constructor for the struct holding proc context (victim, flags, amount, spell).

### Cooldowns & Lockouts

*   **AddGCD**: Adds a Global Cooldown for a spell category.
*   **HasGCD**: Checks if a GCD is active for a specific category or any category.
*   **ResetGCD**: Clears the GCD for a specific category or all categories.
*   **AddCooldown**: Adds a spell or category cooldown.
*   **UpdateCooldowns**: Advances cooldown timers and removes expired ones. Called periodically by `Unit::Update` and `GameObject::Update`.
*   **IsSpellReady**: Checks if a spell is off cooldown, not locked out, and not silenced.
*   **IsSpellOnPermanentCooldown**: Checks if a spell has a permanent cooldown (e.g., from a trinket or quest).
*   **LockOutSpells**: Prevents casting spells of certain schools for a duration (e.g., silence effects).
*   **CheckLockout**: Checks if a specific school is locked out.
*   **RemoveSpellCooldown**: Removes a specific spell's cooldown.
*   **RemoveSpellCategoryCooldown**: Removes a category cooldown.
*   **RemoveAllCooldowns**: Clears all cooldowns and lockouts.
*   **PrintCooldownList**: Debug utility to print active cooldowns to a chat handler.
*   **GetExpireTime**: Retrieves the expiration time of a cooldown.

### Dynamic Objects & Networking

*   **AddDynObject**: Registers a `DynamicObject` (visual effect) with the caster.
*   **RemoveDynObject**: Removes and deletes a dynamic object by spell ID.
*   **RemoveDynObjectWithGUID**: Removes a dynamic object by its GUID.
*   **RemoveAllDynObjects**: Cleans up all dynamic objects associated with the caster.
*   **GetDynObject** / **GetDynObjects**: Retrieves dynamic objects by spell ID or effect index.
*   **SendSpellMiss**: Sends `SMSG_SPELLLOGMISS` to clients.
*   **SendSpellDamageResist**: Sends `SMSG_PROCRESIST` to clients.
*   **SendSpellOrDamageImmune**: Sends `SMSG_SPELLORDAMAGE_IMMUNE` to clients.
*   **SendSpellNonMeleeDamageLog**: Sends `SMSG_SPELLNONMELEEDAMAGELOG` to clients.
*   **SendHealSpellLog**: Sends `SMSG_SPELLHEALLOG` to clients.
*   **SendEnergizeSpellLog**: Sends `SMSG_SPELLENERGIZELOG` to clients.
*   **DealHeal**: Applies health to a victim and triggers AI events.
*   **EnergizeBySpell**: Applies power (mana, rage, etc.) to a victim.
*   **DealDamage**: Delegates to `Unit::DealDamage` to apply damage and handle absorption/resistance.
*   **DealDamageMods**: Pre-damage hook for AI events and sanity checks (e.g., Spirit of Redemption).
*   **DealSpellDamage**: Wrapper that prepares a `CleanDamage` struct and calls `DealDamage`.

### Utility & Stats

*   **GetLevelForTarget**: Returns the effective level of the caster for the purpose of calculating hit/resist chances against a target. For world bosses, it adds a configured level difference.
*   **GetWeaponSkillValue**: Returns the weapon skill for a player or the melee skill for a creature.
*   **GetDefenseSkillValue**: Returns the defense skill for a player or creature.
*   **GetSkillMaxForLevel**: Returns the maximum skill value for a given level (Level * 5).
*   **GetUnitMeleeSkill**: Returns the base melee skill (Level * 5).
*   **DecreaseCastCounter**: Decrements the cast counter when a spell finishes.
*   **ConvertMillisecondToStr**: Helper function to format durations for debug output.

## Cross-Unit Boundaries

*   **Spell.Main**: `SpellCaster` creates `Spell` objects via `CastSpell` and interacts with them via `GetCurrentSpell`, `InterruptSpell`, and `FinishSpell`. It relies on `Spell` for detailed effect execution, while `SpellCaster` handles the high-level casting state and combat resolution.
*   **Unit.Main**: `SpellCaster` calls `Unit` methods for proc evaluation (`ProcDamageAndSpellFor`, `HandleTriggers`), damage application (`DealDamage`), and stat retrieval (`GetArmor`, `GetResistance`). `Unit` inherits from `SpellCaster` and overrides virtual methods like `GetLevel` and `DealDamage`.
*   **Creature.Main**: `SpellCaster` checks creature-specific flags (`IsWorldBoss`, `HasStaticFlag`, `IsPet`) and retrieves creature info for damage modifiers.
*   **Player.Main**: `SpellCaster` interacts with `Player` for weapon skill, defense skill, and combo points.
*   **Aura**: `SpellCaster` queries auras for magnets, reflect chances, and damage/healing bonuses. It also interacts with `SpellAuraHolder` to consume charges.
*   **DynamicObject**: `SpellCaster` manages the lifecycle of `DynamicObject`s created by spells.
*   **World**: `SpellCaster` accesses global configuration (`sWorld.getConfig`) for settings like world boss level difference and spell proc delay.
*   **ChatHandler**: `PrintCooldownList` uses `ChatHandler` to send debug messages.
*   **Log.Main**: `CastSpell` and `DealSpellDamage` log errors or debug information.

## Data Model

This unit does not directly access database tables. It operates entirely on in-memory objects (`SpellEntry`, `Unit`, `Aura`, `CooldownData`). Cooldowns and lockouts are tracked in memory via `m_cooldownMap`, `m_GCDCatMap`, and `m_lockoutMap`.

## Notable Implementation Details

*   **Proc Delaying:** The `UpdatePendingProcs` and `ProcDamageAndSpell` methods implement a configurable delay for proc evaluation. This prevents infinite recursion and allows the server to batch proc checks. Kill procs are always processed instantly.
*   **Spell Magnet Logic:** `SelectMagnetTarget` specifically handles the "Spell Magnet" aura (e.g., Grounding Totem). It checks for magic spells, verifies the magnet is alive and in range, consumes a charge, and redirects the spell. Non-magic spells or spells with `SPELL_ATTR_EX_NO_REDIRECTION` bypass this.
*   **Critical Strike Bonuses:** Critical damage is calculated differently for melee/ranged (100% bonus) vs. magic (50% bonus). Additional modifiers from auras (`SPELL_AURA_MOD_CRIT_PERCENT_VERSUS`) are applied multiplicatively.
*   **Armor Reduction:** `CalcArmorReducedDamage` uses the classic WoW armor formula: `Damage * (1 - (0.1 * Armor / (8.5 * Level + 40)) / (1 + 0.1 * Armor / (8.5 * Level + 40)))`. It caps the reduction at 75%.
*   **Level Penalty:** `CalculateLevelPenalty` applies a penalty to spell power coefficients when casting low-level spells. The formula is `1 - ((20 - SpellLevel) * 0.0375)` for spells below level 20.
*   **GCD Handling:** The GCD is tracked per category in `m_GCDCatMap`. `AddGCD` adds an entry, and `UpdateCooldowns` removes expired entries. `HasGCD` checks if any GCD is active for a given category.
*   **Interrupt Logic:** `SetCurrentCastedSpell` carefully manages interrupting existing spells. For example, casting a generic spell interrupts a channeled spell, but Auto Shot (category 351) does not interrupt other spells.
*   **Debug Utilities:** `PrintCooldownList` and `ConvertMillisecondToStr` provide detailed debugging information about active cooldowns, lockouts, and GCDs, useful for developers troubleshooting spell issues.

## Member Reference

**SelectMagnetTarget**: Redirects magic spells to a target with a Spell Magnet aura, consuming a charge.
**GetLevelForTarget**: Returns the caster's effective level for combat calculations, adding a bonus for world bosses.
**GetWeaponSkillValue**: Returns the weapon skill for players or melee skill for creatures.
**GetDefenseSkillValue**: Returns the defense skill for players or creatures.
**SpellHitResult**: Determines the outcome of a spell hit (miss, immune, reflect, hit) by delegating to magic/melee handlers.
**ProcSystemArguments**: Constructor for the struct holding proc context.
**UpdatePendingProcs**: Timer callback that processes delayed procs.
**ProcDamageAndSpell**: Entry point for processing procs, deciding whether to process instantly or delay.
**ProcDamageAndSpell_delayed**: Processes procs that were delayed.
**ProcDamageAndSpell_real**: Executes the actual proc logic by calling Unit methods.
**MeleeSpellMissChance**: Calculates the base miss chance for melee attacks.
**GetLevel**: Virtual declaration for getting the caster's level.
**GetSkillMaxForLevel**: Returns the maximum skill value for a given level (Level * 5).
**GetUnitMeleeSkill**: Returns the base melee skill (Level * 5).
**GetCurrentSpell**: Returns the currently active spell of a specific type.
**DecreaseCastCounter**: Decrements the cast counter when a spell finishes.
**IsSpellCrit**: Virtual declaration for checking if a spell is a critical hit.
**MeleeSpellHitResult**: Determines the outcome of melee/ranged spells (miss, dodge, parry, block).
**RemoveAllCooldowns**: Clears all cooldowns and lockouts.
**SpellCaster**: Default constructor.
**ToSpellCaster**: Static helper to cast an Object to SpellCaster.
**ToSpellCaster#2**: Static helper to cast a const Object to const SpellCaster.
**MagicSpellHitResult**: Determines if a magic spell hits or is resisted.
**MagicSpellHitChance**: Calculates the hit chance for magic spells.
**GetSpellResistChance**: Calculates the percentage chance for a spell to be resisted.
**SendSpellMiss**: Sends SMSG_SPELLLOGMISS to clients.
**SendSpellDamageResist**: Sends SMSG_PROCRESIST to clients.
**SendSpellOrDamageImmune**: Sends SMSG_SPELLORDAMAGE_IMMUNE to clients.
**SpellCriticalDamageBonus**: Adds the critical strike multiplier to damage.
**SpellCriticalHealingBonus**: Adds the critical strike multiplier to healing.
**DealHeal**: Applies health to a victim and triggers AI events.
**SendHealSpellLog**: Sends SMSG_SPELLHEALLOG to clients.
**EnergizeBySpell**: Applies power to a victim.
**SendEnergizeSpellLog**: Sends SMSG_SPELLENERGIZELOG to clients.
**SendSpellNonMeleeDamageLog**: Sends SMSG_SPELLNONMELEEDAMAGELOG to clients.
**SendSpellNonMeleeDamageLog#2**: Overload that constructs the log struct before sending.
**GetMeleeDamageSchoolMask**: Returns the school mask for melee damage (Normal).
**CalcArmorReducedDamage**: Applies armor reduction to physical damage.
**CalculateSpellEffectValue**: Computes the raw value of a spell effect.
**CalculateSpellDamage**: Finalizes the damage value for a spell.
**MeleeDamageBonusDone**: Calculates melee damage bonuses.
**SpellHealingBonusDone**: Calculates healing bonuses.
**SpellBaseHealingBonusDone**: Retrieves flat healing bonuses.
**SpellDamageBonusDone**: Calculates spell damage bonuses.
**SpellBaseDamageBonusDone**: Retrieves flat damage bonuses.
**SpellBonusWithCoeffs**: Applies spell power coefficients and level penalties.
**DealDamageMods**: Pre-damage hook for AI events and sanity checks.
**CalculateLevelPenalty**: Calculates the penalty for casting a low-level spell.
**GetAPMultiplier**: Determines the Attack Power multiplier for weapon damage.
**DealSpellDamage**: Wrapper that prepares a CleanDamage struct and calls DealDamage.
**DealDamage**: Delegates to Unit::DealDamage.
**CheckAndIncreaseCastCounter**: Prevents infinite spell chains.
**MoveChannelledSpellWithCastTime**: Transitions a spell from Generic to Channeled slot.
**SetCurrentCastedSpell**: Updates the active spell and interrupts conflicting spells.
**FindCurrentSpellBySpellId**: Finds an active spell by ID.
**IsNonMeleeSpellCasted**: Checks if any non-melee spell is active.
**IsNextSwingSpellCasted**: Checks if a melee swing spell is active.
**IsNoMovementSpellCasted**: Checks if the current spell prevents movement.
**InterruptSpellsWithInterruptFlags**: Interrupts spells with specific interrupt flags.
**InterruptSpellsWithChannelFlags**: Interrupts channeled spells with specific channel flags.
**InterruptNonMeleeSpells**: Interrupts generic, autorepeat, and channeled spells.
**InterruptSpell**: Cancels a specific active spell.
**FinishSpell**: Marks a spell as finished.
**GetDynObjects**: Retrieves dynamic objects by spell ID and effect index.
**GetDynObject**: Retrieves a dynamic object by spell ID and effect index.
**GetDynObject#2**: Retrieves a dynamic object by spell ID.
**AddDynObject**: Registers a dynamic object with the caster.
**RemoveDynObject**: Removes and deletes a dynamic object by spell ID.
**RemoveDynObjectWithGUID**: Removes a dynamic object by its GUID.
**RemoveAllDynObjects**: Cleans up all dynamic objects.
**CastSpell#2**: Initiates a spell cast on a target.
**CastSpell**: Initiates a spell cast on a target with SpellEntry.
**CastCustomSpell#2**: Initiates a custom spell cast with overridden base points.
**CastCustomSpell**: Initiates a custom spell cast with SpellEntry.
**CastSpell#4**: Initiates a spell cast at coordinates.
**CastSpell#3**: Initiates a spell cast at coordinates with SpellEntry.
**AddGCD**: Adds a Global Cooldown.
**HasGCD**: Checks if a GCD is active.
**AddCooldown**: Adds a spell or category cooldown.
**UpdateCooldowns**: Advances cooldown timers.
**CheckLockout**: Checks if a school is locked out.
**GetExpireTime**: Retrieves the expiration time of a cooldown.
**IsSpellReady**: Checks if a spell is off cooldown and not locked out.
**IsSpellReady#2**: Overload that takes a spell ID.
**IsSpellOnPermanentCooldown**: Checks if a spell has a permanent cooldown.
**LockOutSpells**: Prevents casting spells of certain schools.
**RemoveSpellCooldown#2**: Removes a spell cooldown by ID.
**RemoveSpellCooldown**: Removes a spell cooldown.
**RemoveSpellCategoryCooldown**: Removes a category cooldown.
**ResetGCD**: Clears the GCD.
**ConvertMillisecondToStr**: Formats durations for debug output.
**PrintCooldownList**: Prints active cooldowns to a chat handler.

---

<!-- machine-true, projected from graph.json -->

## Map — SpellCaster

*Source:* SpellCaster.cpp, SpellCaster.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| SelectMagnetTarget | method | Aura/GetCaster, Aura/GetHolder, Spell.Main/CheckTarget, SpellAuraHolder/DropAuraCharge, Unit.Main/GetAurasByType, Unit.Main/IsAlive, Unit.Main/RemoveAurasDueToSpell, Unit.Main/RemoveSpellAuraHolder, Unit.SpellAuras/GetId, WorldObject.Object/IsInMap | Spell.Main/FillTargetMap, Spell.Main/SetTargetMap | — |
| GetLevelForTarget | method | Creature.Main/IsWorldBoss, GameObject/GetGOInfo, Object/GetUInt32Value, Object/IsUnit, Object/ToCreature#2, Object/ToGameObject#2, Object/ToUnit#2, Unit.Main/GetLevel, Unit.Main/ToUnit#2, World/getConfig#4 | Creature.Main/GetAttackDistance, Player.Main/UpdateCombatSkills, Unit.Main/CanDetectStealthOf | — |
| GetWeaponSkillValue | method | game_Objects_Item/GetProficiencySkill, game_Objects_Item/GetProto, Object/ToPlayer#2, Player.Main/GetSkillValue, Player.Main/GetWeaponForAttack#2, Unit.Main/IsNoWeaponShapeShift | Player.StatSystem/UpdateCritPercentage, Unit.Main/CalculateMeleeDamage, Unit.Main/GetUnitCriticalChance, Unit.Main/MeleeMissChanceCalc, Unit.Main/RollMeleeOutcomeAgainst#2, Unit.Main/RollSpellBlockChanceOutcome | — |
| GetDefenseSkillValue | method | Creature.Main/HasStaticFlag, Object/IsPlayer, Object/ToCreature#2, Object/ToPlayer#2, Player.Main/GetSkillMax, Player.Main/GetSkillValue | Player.StatSystem/UpdateBlockPercentage, Player.StatSystem/UpdateDodgePercentage, Player.StatSystem/UpdateParryPercentage, Unit.Main/CalculateMeleeDamage, Unit.Main/DealMeleeDamage, Unit.Main/GetUnitCriticalChance, Unit.Main/MeleeMissChanceCalc, Unit.Main/RollMeleeOutcomeAgainst#2 | — |
| SpellHitResult | method | Aura/GetModifier, Creature.Main/IsInEvadeMode, Object/IsCreature, shared_Util/roll_chance_i, SpellEntry/GetSpellSchoolMask, SpellEntry/IsPositiveEffect, SpellEntry/IsPositiveSpell#3, Unit.Main/GetAurasByType, Unit.Main/GetTotalAuraModifier, Unit.Main/IsImmuneToDamage, Unit.Main/IsImmuneToSpell | Spell.Main/AddUnitTarget#2, Unit.AuraProcHandler/HandleProcTriggerDamageAuraProc, Unit.Main/TriggerDamageShields, Unit.SpellAuras/PeriodicTick | — |
| ProcSystemArguments | ctor | Object/GetObjectGuid, Spell.Main/IsCastByItem, Spell.Main/IsTriggered, Spell.Main/IsTriggeredByAura, World/GetGameTime | Spell.Main/AddUnitTarget#2, Spell.Main/cast, Spell.Main/DoAllEffectOnTarget#3, spell_hunter/OnPeriodicTrigger, Unit.Main/AttackerStateUpdate, Unit.Main/Kill, Unit.SpellAuras/PeriodicTick | — |
| UpdatePendingProcs | method | Object/IsInWorld, World/getConfig#4 | GameObject/Update, Unit.Main/Update | — |
| ProcDamageAndSpell | method | Object/IsInWorld, Object/IsUnit, Object/ToUnit, Unit.Main/IsAlive, Unit.Main/ProcSkillsAndReactives, World/getConfig#4, WorldObject.Object/IsInMap | Spell.Main/AddUnitTarget#2, Spell.Main/cast, Spell.Main/DoAllEffectOnTarget#3, spell_hunter/OnPeriodicTrigger, Unit.Main/AttackerStateUpdate, Unit.Main/Kill, Unit.SpellAuras/PeriodicTick | — |
| ProcDamageAndSpell_delayed | method | Map.Main/GetUnit, WorldObject.Object/GetMap | — | — |
| ProcDamageAndSpell_real | method | Object/IsUnit, Object/ToPlayer, Object/ToUnit, Player.Main/IsStandUpScheduled, Unit.Main/HandleTriggers, Unit.Main/IsAlive, Unit.Main/ProcDamageAndSpellFor, Unit.Main/SetStandState | — | — |
| MeleeSpellMissChance | method | Object/GetTypeId, Object/IsPlayer, Object/ToUnit, Unit.Main/GetLevel, Unit.Main/GetSpellModOwner, Unit.Main/GetTotalAuraModifier, Unit.Main/GetWeaponBasedAuraModifier, Unit.Main/IsStandingUp | — | — |
| GetLevel | decl | — | Spell.Effects/EffectSummonDemon, Spell.Effects/EffectSummonWild | — |
| GetSkillMaxForLevel | method | — | Player.Main/UpdateSkillsForLevel, Player.Main/UpdateSpellTrainedSkills, Player.Main/_LoadSkills, Player.StatSystem/UpdateBlockPercentage, Player.StatSystem/UpdateCritPercentage, Player.StatSystem/UpdateDodgePercentage, Player.StatSystem/UpdateParryPercentage, Unit.Main/GetUnitCriticalChance, Unit.Main/RollMeleeOutcomeAgainst#2, Unit.Main/RollSpellBlockChanceOutcome | — |
| GetUnitMeleeSkill | method | — | Unit.Main/DealMeleeDamage | — |
| GetCurrentSpell | method | — | AiBotAI.Combat/UpdateInCombatAI_Mage, AiBotAI.Combat/UpdateInCombatAI_Priest, AiBotAI.Combat/UpdateInCombatAI_Warlock, AiBotAI.Main/UpdateAI, ashenvale/AttackStart, ashenvale/UpdateAI#2, BattleBotAI.BattleBotWaypoints/AtFlag, BattleBotAI.Main/UpdateAI, BattleBotAI.Main/UpdateInCombatAI_Mage, BattleBotAI.Main/UpdateInCombatAI_Priest, BattleBotAI.Main/UpdateInCombatAI_Warlock, boss_cthun/UpdateAI#4, boss_cthun/UpdateAI#7, boss_urok/AttackStart, ChatHandler.PlayerBotMgr/HandlePartyBotAoECommand, Creature.Main/SendAreaSpiritHealerQueryOpcode, GameObject/Use, instance_zulgurub/Thekal_GetUnitCastingRez, PartyBotAI/UpdateAI, PartyBotAI/UpdateInCombatAI_Mage, PartyBotAI/UpdateInCombatAI_Priest, PartyBotAI/UpdateInCombatAI_Warlock, Player.Main/ActivateTaxiPathTo, Player.Main/InterruptSpellsWithCastItem, Player.Main/RemoveItemDependentAurasAndCasts, Player.Main/RestoreAllSpellMods, Spell.Effects/EffectInterruptCast, Spell.Main/Abort, Spell.Main/DoSpellHitOnUnit, Spell.Main/SendChannelUpdate, spell_item/OnPeriodicCalculateAmount, Unit.AuraProcHandler/HandleProcTriggerSpellAuraProc, Unit.Main/DealDamage, Unit.Main/HasBreakableByDamageCrowdControlAura, Unit.Main/InterruptSpellsCastedOnMe, Unit.Main/RemoveSpellAuraHolder, Unit.Main/SetInCombatState, Unit.SpellAuras/HandleAuraModSilence, Unit.SpellAuras/HandleShapeshiftBoosts, Unit.SpellAuras/PeriodicTick, Unit.SpellAuras/Update, wailing_caverns/AttackedBy, wailing_caverns/UpdateEscortAI, WorldSession.MiscHandler/HandleSetSelectionOpcode, WorldSession.SpellHandler/HandleCancelAuraOpcode, WorldSession.SpellHandler/HandleCancelChanneling | — |
| DecreaseCastCounter | method | — | Spell.Main/cast | — |
| IsSpellCrit | method | — | Spell.Main/AddUnitTarget#2 | — |
| MeleeSpellHitResult | method | Creature.Main/HasExtraFlag, Object/GetTypeId, Object/IsPlayer, Object/ToCreature#2, Player.Main/CanBlock, Player.Main/CanParry, shared_Util/urand, SpellEntry/HasAttribute#5, Unit.Main/GetLevel, Unit.Main/GetTotalAuraModifierByMiscValue, Unit.Main/GetUnitDodgeChance, Unit.Main/GetUnitParryChance, Unit.Main/RollSpellBlockChanceOutcome, WorldObject.Object/HasInArc | — | — |
| RemoveAllCooldowns | method | — | ChatHandler.UnitCommands/HandleCooldownClearCommand, Map.ScriptCommands/ScriptCommand_RemoveSpellCooldown, Pet.Main/RemoveAllCooldowns, spell_special/OnEffectExecute#3 | — |
| SpellCaster | ctor | — | GameObject/GameObject, Unit.Main/Unit | — |
| ToSpellCaster | function | — | Map.ScriptCommands/ScriptCommand_CastSpell | — |
| ToSpellCaster#2 | function | — | — | — |
| MagicSpellHitResult | method | Creature.Main/HasStaticFlag, Object/IsCreature, shared_Util/irand, Unit.Main/IsAlive | Unit.SpellAuras/HandleFeignDeath | — |
| MagicSpellHitChance | method | Object/GetTypeId, Object/ToUnit, SpellEntry/GetSpellSchoolMask, SpellEntry/IsAreaOfEffectSpell, SpellEntry/IsBinary, Unit.Main/GetSpellModOwner, Unit.Main/GetTotalAuraModifier, Unit.Main/GetTotalAuraModifierByMiscMask, Unit.Main/GetTotalAuraModifierByMiscValue | Unit.SpellAuras/CalculateHeartBeat | — |
| GetSpellResistChance | method | Object/GetTypeId, Object/ToUnit#2, SpellDefines/GetFirstSchoolInMask, Unit.Main/GetResistance, Unit.Main/GetTotalAuraModifierByMiscMask | Unit.Main/CalculateDamageAbsorbAndResist | — |
| SendSpellMiss | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#7, Object/GetObjectGuid, ObjectGuid/operator<<, WorldObject.Object/SendObjectMessageToSet, WorldPacket/WorldPacket#4 | Spell.Main/DoSpellHitOnUnit, Unit.Main/TriggerDamageShields, Unit.SpellAuras/PeriodicTick | — |
| SendSpellDamageResist | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#7, Object/GetObjectGuid, ObjectGuid/operator<<, WorldObject.Object/SendMessageToSet, WorldPacket/WorldPacket#4 | Unit.AuraProcHandler/HandleProcTriggerDamageAuraProc | — |
| SendSpellOrDamageImmune | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#7, Object/GetObjectGuid, ObjectGuid/operator<<, WorldObject.Object/SendMessageToSet, WorldPacket/WorldPacket#4 | Unit.Main/TriggerDamageShields, Unit.SpellAuras/PeriodicTick | — |
| SpellCriticalDamageBonus | method | Object/ToUnit, Unit.Main/GetCreatureTypeMask, Unit.Main/GetSpellModOwner, Unit.Main/GetTotalAuraMultiplierByMiscMask | — | — |
| SpellCriticalHealingBonus | method | Object/ToUnit#2, Unit.Main/GetCreatureTypeMask, Unit.Main/GetTotalAuraMultiplierByMiscMask | Spell.Main/DoAllEffectOnTarget#3 | — |
| DealHeal | method | Creature.Main/IsTotem, CreatureAI/HealedBy, Object/IsCreature, Object/IsPlayer, Object/ToUnit, Unit.Main/AI, Unit.Main/GetOwner, Unit.Main/ModifyHealth | Spell.Effects/EffectHealMechanical, Spell.Effects/EffectHealthLeech, Spell.Main/DoAllEffectOnTarget#3, Unit.SpellAuras/PeriodicTick, Unit.SpellAuras/TriggerSpell | — |
| SendHealSpellLog | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#7, Object/GetPackGUID, ObjectGuid/operator<<#2, WorldObject.Object/SendMessageToSet, WorldPacket/WorldPacket#4 | — | — |
| EnergizeBySpell | method | Unit.Main/ModifyPower | Spell.Effects/EffectEnergize, Unit.SpellAuras/TriggerSpell | — |
| SendEnergizeSpellLog | method | ByteBuffer/operator<<#10, Object/GetPackGUID, ObjectGuid/operator<<#2, WorldObject.Object/SendMessageToSet, WorldPacket/WorldPacket#4 | spell_special/OnPeriodicTrigger, Unit.SpellAuras/TriggerSpell | — |
| SendSpellNonMeleeDamageLog | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#4, ByteBuffer/operator<<#7, Object/GetPackGUID, ObjectGuid/operator<<#2, WorldObject.Object/SendMessageToSet, WorldPacket/WorldPacket#4 | Spell.Main/DoAllEffectOnTarget#3, Unit.AuraProcHandler/HandleProcTriggerDamageAuraProc, Unit.SpellAuras/PeriodicTick | — |
| SendSpellNonMeleeDamageLog#2 | method | SpellDefines/GetFirstSchoolInMask, SpellNonMeleeDamage/SpellNonMeleeDamage | Spell.Effects/EffectEnvironmentalDMG, Unit.Main/CalculateDamageAbsorbAndResist, Unit.SpellAuras/PeriodicTick | — |
| GetMeleeDamageSchoolMask | method | — | Unit.Main/CalculateMeleeDamage, Unit.Main/MeleeDamageBonusTaken, Unit.Main/RollMeleeOutcomeAgainst#2 | — |
| CalcArmorReducedDamage | method | Object/ToUnit#2, Unit.Main/GetArmor, Unit.Main/GetTotalAuraModifierByMiscMask | ChatHandler.UnitCommands/HandleDamageCommand, Unit.AuraProcHandler/HandleDummyAuraProc, Unit.Main/CalculateMeleeDamage | — |
| CalculateSpellEffectValue | method | Object/GetObjectGuid, Object/ToPlayer#2, Object/ToUnit#2, ObjectGuid/operator==, ObjectMgr/GetCreatureClassLevelStats, Player.Main/GetComboPoints, Player.Main/GetComboTargetGuid, shared_Util/irand, SpellEntry/HasAttribute, Unit.Main/GetSpellModOwner | GameObject/GetSpellForLock, Spell.Main/HandleAddTargetTriggerAuras, spell_warlock/OnEffectExecute#5, Unit.AuraProcHandler/HandleProcTriggerDamageAuraProc, Unit.SpellAuras/Aura, Unit.SpellAuras/SetStackAmount | — |
| CalculateSpellDamage | method | shared_Util/ditheru, SpellDefines/GetSchoolMask, SpellEntry/HasAttribute#5, Unit.Main/IsAlive, Unit.Main/MeleeDamageBonusTaken, Unit.Main/SpellDamageBonusTaken | Spell.Main/DoAllEffectOnTarget#3, Spell.Main/HandleDelayedSpellLaunch, Unit.SpellAuras/PeriodicTick | — |
| MeleeDamageBonusDone | method | Aura/GetModifier, Aura/GetSpellProto, Creature.Main/GetCreatureInfo, Creature.Main/IsPet, Creature.Main/_GetSpellDamageMod, game_Objects_Item/IsFitToSpellRequirements, Object/GetTypeId, Object/IsPet, Object/ToUnit, ObjectGuid/IsPlayer, Pet.Main/GetBonusDamage, Pet.Main/GetHappinessState, Pet.Main/GetPetType, Player.Main/GetWeaponForAttack#2, SpellEntry/GetSpellSchoolMask, SpellEntry/HasAttribute#5, SpellEntry/HasEffect, Unit.Main/GetAurasByType, Unit.Main/GetCreatureTypeMask, Unit.Main/GetModifierValue, Unit.Main/GetOwnerGuid, Unit.Main/GetSpellModOwner, Unit.Main/GetTotalAuraModifier, Unit.Main/GetTotalAuraModifierByMiscMask, Unit.Main/GetTotalAuraMultiplierByMiscMask | Unit.Main/CalculateMeleeDamage, Unit.SpellAuras/CalculateDotDamage | — |
| SpellHealingBonusDone | method | Aura/GetModifier, Creature.Main/IsTotem, Object/GetTypeId, Object/ToUnit, SpellEntry/GetSpellSchoolMask, SpellEntry/HasAttribute, SpellEntry/HasAttribute#5, Unit.Main/GetAurasByType, Unit.Main/GetOwner, Unit.Main/GetSpellModOwner, Unit.SpellAuras/IsAffectedOnSpell | Spell.Effects/EffectHeal, Spell.Effects/EffectHealMechanical, Unit.SpellAuras/HandlePeriodicHeal | — |
| SpellBaseHealingBonusDone | method | Aura/GetModifier, Object/GetTypeId, Object/ToUnit, Unit.Main/GetAurasByType, Unit.Main/GetStat | Unit.SpellAuras/HandleSchoolAbsorb | — |
| SpellDamageBonusDone | method | Aura/GetModifier, Aura/GetSpellProto, Creature.Main/GetCreatureInfo, Creature.Main/IsPet, Creature.Main/IsTotem, Creature.Main/_GetSpellDamageMod, game_Objects_Item/IsFitToSpellRequirements, Object/GetTypeId, Object/IsPet, Object/ToUnit, ObjectGuid/IsPlayer, Pet.Main/GetBonusDamage, Pet.Main/GetHappinessState, Pet.Main/GetPetType, Player.Main/GetWeaponForAttack#2, SpellEntry/GetAuraMaxTicks, SpellEntry/GetSpellSchoolMask, SpellEntry/HasAttribute#5, Unit.Main/GetAurasByType, Unit.Main/GetCreatureTypeMask, Unit.Main/GetOwner, Unit.Main/GetOwnerGuid, Unit.Main/GetSpellModOwner, Unit.Main/GetTotalAuraModifierByMiscMask, Unit.Main/GetTotalAuraMultiplierByMiscMask, Unit.SpellAuras/IsAffectedOnSpell | Spell.Effects/EffectHealthLeech, Spell.Effects/EffectPowerDrain, Spell.Effects/EffectWeaponDmg, spell_paladin/OnEffectExecute, spell_paladin/OnEffectExecute#3, spell_warlock/OnCheckCast#3, spell_warlock/OnEffectExecute#5, Unit.AuraProcHandler/HandleDummyAuraProc, Unit.AuraProcHandler/HandleProcTriggerDamageAuraProc, Unit.Main/TriggerDamageShields, Unit.SpellAuras/CalculateDotDamage, Unit.SpellAuras/HandlePeriodicHealthFunnel, Unit.SpellAuras/HandlePeriodicLeech, Unit.SpellAuras/PeriodicTick | — |
| SpellBaseDamageBonusDone | method | Aura/GetModifier, Aura/GetSpellProto, Object/GetTypeId, Object/ToUnit, Unit.Main/GetAurasByType, Unit.Main/GetStat | Player.StatSystem/UpdateSpellDamageAndHealingBonus, spell_shaman/OnEffectExecute, Unit.SpellAuras/HandleSchoolAbsorb | — |
| SpellBonusWithCoeffs | method | Object/ToUnit#2, SpellEntry/CalculateCustomCoefficient, SpellEntry/CalculateDefaultCoefficient, Unit.Main/GetSpellModOwner | Unit.Main/MeleeDamageBonusTaken, Unit.Main/SpellDamageBonusTaken, Unit.Main/SpellHealingBonusTaken | — |
| DealDamageMods | method | Creature.Main/AI, Creature.Main/IsInEvadeMode, CreatureAI/DamageDeal, CreatureAI/DamageTaken, Object/IsCreature, Object/ToCreature, Object/ToUnit, Unit.Main/GetClass, Unit.Main/HasAura#2, Unit.Main/IsAlive, Unit.Main/IsTaxiFlying | ChatHandler.UnitCommands/HandleDamageCommand, Player.Main/EnvironmentalDamage, Spell.Main/DoAllEffectOnTarget#3, Unit.AuraProcHandler/HandleProcTriggerDamageAuraProc, Unit.Main/AttackerStateUpdate, Unit.Main/CalculateDamageAbsorbAndResist, Unit.Main/TriggerDamageShields, Unit.SpellAuras/PeriodicTick | — |
| CalculateLevelPenalty | method | — | Unit.SpellAuras/HandleSchoolAbsorb | — |
| GetAPMultiplier | method | game_Objects_Item/GetProto, Object/GetTypeId, Object/ToUnit#2, Player.Main/GetWeaponForAttack#2, Unit.Main/GetAttackTime | Player.StatSystem/CalculateMinMaxDamage | — |
| DealSpellDamage | method | CleanDamage/CleanDamage, Creature.Main/IsInEvadeMode, Log.Main/Out, Object/GetTypeId, SpellDefines/GetSchoolMask, SpellEntry/HasAttribute#5, SpellMgr/GetSpellEntry, SpellMgr/Instance, Unit.Main/IsAlive, Unit.Main/IsTaxiFlying | Spell.Main/DoAllEffectOnTarget#3, Unit.AuraProcHandler/HandleProcTriggerDamageAuraProc, Unit.SpellAuras/PeriodicTick | — |
| DealDamage | method | Unit.Main/DealDamage | Spell.Effects/EffectDummy, Spell.Effects/EffectInstaKill, Unit.Main/CalculateDamageAbsorbAndResist | — |
| CheckAndIncreaseCastCounter | method | World/getConfig#4 | Spell.Main/cast | — |
| MoveChannelledSpellWithCastTime | method | Errors/PrintStacktraceAndThrow, Spell.Main/getState | Spell.Main/handle_immediate | — |
| SetCurrentCastedSpell | method | Errors/PrintStacktraceAndThrow, Object/ToUnit, Spell.Main/GetCurrentContainer, Spell.Main/SetReferencedFromCurrent | Spell.Main/prepare#2 | — |
| FindCurrentSpellBySpellId | method | — | — | — |
| IsNonMeleeSpellCasted | method | Spell.Main/getState | AiBotAI.Combat/UpdateInCombatAI_Hunter, AiBotAI.Combat/UpdateInCombatAI_Mage, AiBotAI.Combat/UpdateInCombatAI_Paladin, AiBotAI.Combat/UpdateInCombatAI_Priest, AiBotAI.Combat/UpdateInCombatAI_Rogue, AiBotAI.Combat/UpdateInCombatAI_Shaman, AiBotAI.Combat/UpdateInCombatAI_Warlock, AiBotAI.Combat/UpdateInCombatAI_Warrior, AiBotAI.Main/UpdateAI, BattleBotAI.Main/UpdateAI, BattleBotAI.Main/UpdateInCombatAI_Hunter, BattleBotAI.Main/UpdateInCombatAI_Mage, BattleBotAI.Main/UpdateInCombatAI_Paladin, BattleBotAI.Main/UpdateInCombatAI_Priest, BattleBotAI.Main/UpdateInCombatAI_Rogue, BattleBotAI.Main/UpdateInCombatAI_Shaman, BattleBotAI.Main/UpdateInCombatAI_Warlock, BattleBotAI.Main/UpdateInCombatAI_Warrior, boss_anubrekhan/UpdateAI, boss_arcanist_doan/UpdateAI, boss_cthun/AttackStart#3, boss_cthun/SelectHostileTargetMelee, boss_dathrohan_balnazzar/UpdateAI, boss_firemaw/UpdateAI, boss_grobbulus/DoCastMutagenInjection, boss_hakkar/UpdateAI, boss_herod/UpdateAI, boss_high_inquisitor_fairbanks/UpdateAI, boss_immol_thar/UpdateAI, boss_jeklik/UpdateAI, boss_loatheb/UpdateAI#2, boss_loatheb/WhackAStalk, boss_mandokir/UpdateAI, boss_marli/UpdateAI, boss_ouro/UpdateAI, boss_sapphiron/UpdateAI, boss_tendris_warpwood/UpdateAI, boss_thaddius/UpdateAI#3, boss_thaddius/UpdateP2, boss_twinemperors/UpdateEmperor, boss_venoxis/UpdateAI, boss_zevrim/UpdateAI, CombatBotBaseAI/AreOthersOnSameTarget, Creature.Main/TryToCast, Creature.Main/TryToCast#2, CreatureAI/DoCast, CreatureAI/DoCastAOE, CreatureAI/DoSpellsListCasts, CreatureEventAI/ProcessEvent, dustwallow_marsh/UpdateAI#5, instance_dire_maul/UpdateAI, instance_dire_maul/UpdateAI#10, instance_dire_maul/UpdateAI#11, instance_dire_maul/UpdateAI#4, instance_dire_maul/UpdateAI#7, instance_dire_maul/UpdateAI#9, instance_naxxramas.boss_kelthuzad/UpdateP2P3, instance_naxxramas.Main/UpdateAI#2, instance_temple_of_ahnqiraj/UpdateAI, Map.ScriptCommands/ScriptCommand_CastSpell, MovementAnticheat/CheckBotting, npcs_special/UpdateAI, PartyBotAI/UpdateAI, PartyBotAI/UpdateInCombatAI_Hunter, PartyBotAI/UpdateInCombatAI_Mage, PartyBotAI/UpdateInCombatAI_Paladin, PartyBotAI/UpdateInCombatAI_Priest, PartyBotAI/UpdateInCombatAI_Rogue, PartyBotAI/UpdateInCombatAI_Shaman, PartyBotAI/UpdateInCombatAI_Warlock, PartyBotAI/UpdateInCombatAI_Warrior, PetAI/UpdateAI, Player.Main/ActivateTaxiPathTo, Player.Main/ExecuteTeleportFar, Player.Main/SwitchInstance, PlayerAI/UpdateAI#2, PlayerBotAI/UpdateAI, ruins_of_ahnqiraj/DamageTaken, ScriptedAI/DoCastSpell, silithus/UpdateAI, silithus/UpdateAI#5, silithus/UpdateAI#6, Spell.Main/CheckPetCast, Spell.Main/Execute#2, Spell.Main/prepare#2, ThreatListCopier.battleground_alterac/UpdateAI#12, ThreatListCopier.battleground_alterac/UpdateAI#13, ThreatListCopier.battleground_alterac/UpdateAI#14, ThreatListCopier.battleground_alterac/UpdateAI#15, ThreatListCopier.battleground_alterac/UpdateAI#16, ThreatListCopier.boss_ragnaros/CheckForMelee, TotemAI/UpdateAI, ubrs_trash/UpdateAI, Unit.Main/AttackerStateUpdate, Unit.Main/CombatStop, Unit.Main/GetUnitBlockChance, Unit.Main/GetUnitDodgeChance, Unit.Main/GetUnitParryChance, Unit.Main/IsCaster, Unit.Main/SetDeathState, Unit.Main/StopAttackFaction, Unit.Main/UpdateMeleeAttackingState, Unit.Main/_UpdateAutoRepeatSpell, Unit.SpellAuras/PeriodicTick, western_plaguelands/UpdateAI, WorldSession.ItemHandler/HandleWrapItemOpcode, WorldSession.LootHandler/HandleLootOpcode, WorldSession.SpellHandler/HandleCancelCastOpcode, WorldSession.SpellHandler/HandleOpenItemOpcode | — |
| IsNextSwingSpellCasted | method | SpellEntry/IsNextMeleeSwingSpell | WorldSession.SpellHandler/HandleCancelCastOpcode | — |
| IsNoMovementSpellCasted | method | Spell.Main/getState, SpellEntry/HasChannelInterruptFlag, SpellEntry/HasSpellInterruptFlag | RandomMovementGenerator/UpdateAsync, WaypointMovementGenerator/Update#2, WaypointMovementGenerator/Update#3, WorldSession.PetHandler/HandlePetAction | — |
| InterruptSpellsWithInterruptFlags | method | Spell.Main/GetCastedTime, Spell.Main/getState, Spell.Main/IsAutoRepeat, Spell.Main/IsChanneled, Spell.Main/IsTriggered, SpellEntry/IsNextMeleeSwingSpell | ChatHandler.PlayerBotMgr/HandlePartyBotComeToMeHelper, Creature.Main/DoFlee, Creature.Main/DoFleeToGetAssistance, Creature.Main/MoveAwayFromTarget, instance_naxxramas.Main/FleeToHorse, Unit.Main/DealDamage, Unit.Main/HandleInterruptsOnMovement | — |
| InterruptSpellsWithChannelFlags | method | Spell.Main/getState | Player.Main/SetEnvironmentFlags, Player.Main/TeleportTo, Unit.Main/HandleInterruptsOnMovement, Unit.Main/Mount, Unit.Main/SetInCombatState, Unit.Main/Unmount, WorldSession.BattleGroundHandler/HandleBattlemasterHelloOpcode, WorldSession.ChatHandler/HandleEmoteOpcode, WorldSession.ChatHandler/HandleTextEmoteOpcode, WorldSession.CombatHandler/HandleSetSheathedOpcode, WorldSession.ItemHandler/SendListInventory, WorldSession.MiscHandler/HandleStandStateChangeOpcode, WorldSession.NPCHandler/HandleBinderActivateOpcode, WorldSession.NPCHandler/HandleBuyStableSlot, WorldSession.NPCHandler/HandleGossipHelloOpcode, WorldSession.NPCHandler/HandleGossipSelectOptionOpcode, WorldSession.NPCHandler/HandleListStabledPetsOpcode, WorldSession.NPCHandler/HandleRepairItemOpcode, WorldSession.NPCHandler/HandleSpiritHealerActivateOpcode, WorldSession.NPCHandler/HandleStablePet, WorldSession.NPCHandler/HandleStableSwapPet, WorldSession.NPCHandler/HandleTabardVendorActivateOpcode, WorldSession.NPCHandler/HandleTrainerBuySpellOpcode, WorldSession.NPCHandler/HandleUnstablePet, WorldSession.NPCHandler/SendTrainerList, WorldSession.QuestHandler/HandleQuestgiverHelloOpcode | — |
| InterruptNonMeleeSpells | method | — | boss_baron_geddon/UpdateAI, boss_celebras_the_cursed/UpdateAI, boss_cthun/EnterDarkGlarePhase, boss_cthun/Reset#5, boss_cthun/UpdateAI#7, boss_cthun/UpdateStomachGrab, boss_dathrohan_balnazzar/UpdateAI, boss_heigan/EventDanceEnd, boss_jandice_barov/UpdateAI, boss_jeklik/UpdateAI, boss_landslide/UpdateAI, boss_marli/UpdateAI, boss_nefarian/OnPeriodicTickEnd, boss_noxxion/UpdateAI, boss_onyxia/DoMovement, boss_onyxia/PhaseTransition, boss_sapphiron/setHover, boss_sapphiron/UpdateAI, boss_thaddius/DamageTaken, boss_thaddius/HandleMagneticPull, boss_thaddius/UpdateAI#3, boss_twinemperors/OnStartTeleport, boss_venoxis/UpdateAI, boss_victor_nefarius/UpdateAI, burning_steppes/JustDidDialogueStep, ChatHandler.PlayerBotMgr/HandlePartyBotToggleCastingCommand, ChatHandler.PlayerBotMgr/StopPartyBotAttackHelper, Creature.Main/TryToCast, GameObject/CleanupsBeforeDelete, instance_dire_maul/UpdateAI#2, instance_naxxramas.boss_kelthuzad/UpdateP1, Map.ScriptCommands/ScriptCommand_CastSpell, Map.ScriptCommands/ScriptCommand_InterruptCasts, moonglade/UpdateAI, PetAI/CanAttack, PetAI/KilledUnit, PetAI/_stopAttack, Player.Main/ExecuteTeleportFar, Player.Main/SwitchInstance, PlayerAI/UpdateTarget, ScriptedPetAI/UpdateAI, scripts_battlegrounds_battleground/UpdateAI#2, ThreatListCopier.battleground_alterac/Aggro#7, ThreatListCopier.battleground_alterac/Aggro#8, ThreatListCopier.battleground_alterac/AV_NpcEventWorldBoss_A_AI, ThreatListCopier.battleground_alterac/AV_NpcEventWorldBoss_H_AI, Unit.Main/CleanupsBeforeDelete, Unit.Main/CombatStop, Unit.Main/HandlePetCommand, Unit.Main/Kill, Unit.Main/KnockBack, Unit.Main/ModConfuseSpell, Unit.Main/SetDeathState, Unit.Main/SetFeignDeath, Unit.Main/StopAttackFaction, Unit.SpellAuras/HandleAuraModStun, Unit.SpellAuras/HandleModCharm, Unit.SpellAuras/TriggerSpell, wailing_caverns/UpdateEscortAI, WorldSession.LootHandler/HandleLootOpcode, WorldSession.PetHandler/HandlePetAction, WorldSession.SpellHandler/HandleCancelCastOpcode, WorldSession.SpellHandler/HandleOpenItemOpcode | — |
| InterruptSpell | method | Errors/PrintStacktraceAndThrow, Object/GetTypeId, Player.Main/SendAutoRepeatCancel, Spell.Main/cancel, Spell.Main/getState, Spell.Main/SetReferencedFromCurrent | AiBotAI.Main/UpdateAI, BattleBotAI.Main/UpdateAI, boss_cthun/UpdateAI#4, ChatHandler.PlayerBotMgr/HandlePartyBotAoECommand, PartyBotAI/UpdateAI, Player.Main/ActivateTaxiPathTo, Player.Main/InterruptSpellsWithCastItem, Player.Main/RemoveItemDependentAurasAndCasts, ScriptedAI/EnterVanish, Spell.Effects/EffectInterruptCast, Spell.Main/DelayedChannel, Spell.Main/DoSpellHitOnUnit, Spell.Main/finish, Unit.Main/AttackStop, Unit.Main/DealDamage, Unit.Main/InterruptSpellsCastedOnMe, Unit.Main/RemoveSpellAuraHolder, Unit.Main/SetInCombatState, Unit.Main/_UpdateAutoRepeatSpell, Unit.SpellAuras/HandleAuraModSilence, Unit.SpellAuras/HandleShapeshiftBoosts, Unit.SpellAuras/Update#4, WorldSession.SpellHandler/HandleCancelAuraOpcode, WorldSession.SpellHandler/HandleCancelAutoRepeatSpellOpcode, WorldSession.SpellHandler/HandleCancelCastOpcode, WorldSession.SpellHandler/HandleCancelChanneling | — |
| FinishSpell | method | Spell.Main/finish, Spell.Main/SendChannelUpdate | GameObject/Update, GameObject/Use, Unit.Main/SetFeignDeath, Unit.SpellAuras/PeriodicTick | — |
| GetDynObjects | method | DynamicObject/GetEffIndex, DynamicObject/GetSpellId, Map.Main/GetDynamicObject, WorldObject.Object/GetMap | — | — |
| GetDynObject | method | DynamicObject/GetEffIndex, DynamicObject/GetSpellId, Map.Main/GetDynamicObject, WorldObject.Object/GetMap | Spell.Main/DelayedChannel, Spell.Main/SendChannelStart | — |
| GetDynObject#2 | method | DynamicObject/GetSpellId, Map.Main/GetDynamicObject, WorldObject.Object/GetMap | Player.Main/UpdateLongSight | — |
| AddDynObject | method | Object/GetObjectGuid, WorldObject.Object/GetWorldMask, WorldObject.Object/SetWorldMask | Player.Main/SetLongSight, Spell.Effects/EffectAddFarsight, Spell.Effects/EffectPersistentAA | — |
| RemoveDynObject | method | DynamicObject/Delete, DynamicObject/GetSpellId, Map.Main/GetDynamicObject, WorldObject.Object/GetMap | Spell.Main/cancel, Spell.Main/SendChannelUpdate | — |
| RemoveDynObjectWithGUID | method | ObjectGuid/operator== | DynamicObject/Update | — |
| RemoveAllDynObjects | method | DynamicObject/Delete, Map.Main/GetDynamicObject, WorldObject.Object/GetMap | GameObject/RemoveFromWorld, ObjectGridLoader/Visit#6, ObjectGridLoader/Visit#7, Player.Main/ExecuteTeleportFar, Player.Main/SwitchInstance, Unit.Main/RemoveFromWorld | — |
| CastSpell#2 | method | Aura/GetEffIndex, Aura/GetId, Log.Main/Out, Object/GetGuidStr, ObjectGuid/ObjectGuid, SpellMgr/GetSpellEntry, SpellMgr/Instance | AiBotAI.Combat/UpdateInCombatAI_Hunter, AiBotAI.Combat/UpdateInCombatAI_Mage, AiBotAI.Combat/UpdateInCombatAI_Priest, AiBotAI.Combat/UpdateInCombatAI_Warlock, AiBotAI.Combat/UseMount, BattleBotAI.BattleBotWaypoints/AtFlag, BattleBotAI.Main/UpdateInCombatAI_Hunter, BattleBotAI.Main/UpdateInCombatAI_Mage, BattleBotAI.Main/UpdateInCombatAI_Priest, BattleBotAI.Main/UpdateInCombatAI_Warlock, BattleBotAI.Main/UseMount, BattleGroundWS/EventPlayerClickedOnFlag, BattleGroundWS/EventPlayerDroppedFlag, blackrock_depths/EnterCombat, blackrock_depths/UpdateAI#5, blasted_lands/GOHello_go_stone_of_binding, boss_archaedas/UpdateAI, boss_archaedas/UpdateAI#2, boss_arlokk/JustDied, boss_arlokk/UpdateAI, boss_ayamiss/UpdateAI#2, boss_baron_geddon/UpdateAI, boss_buru/JustDied#2, boss_buru/UpdateAI, boss_cthun/EnterDarkGlarePhase, boss_cthun/ResetartUnvulnerablePhase, boss_cthun/SummonedCreatureDespawn, boss_cthun/UpdateInvulnerablePhase, boss_cthun/updateNormal, boss_cthun/UpdateStomachGrab, boss_fankriss/UpdateAI#2, boss_four_horsemen/Aggro#2, boss_four_horsemen/JustDied, boss_four_horsemen/UpdateAI, boss_garr/JustDied#2, boss_garr/SpellHit#2, boss_garr/UpdateEvents, boss_gluth/Reset#2, boss_golemagg/OnPeriodicDummy, boss_gothik/Aggro, boss_gothik/EffectDummyCreature_spell_anchor, boss_gothik/SummonedCreatureJustDied, boss_grobbulus/OnBeforeApply, boss_heigan/SendEruptCustomLocation, boss_heigan/SummmonPlagueWave, boss_high_inquisitor_fairbanks/SpellHit, boss_immol_thar/UpdateAI, boss_ironaya/UpdateAI, boss_jeklik/Aggro#2, boss_jeklik/boss_jeklikAI, boss_jeklik/DoAttack, boss_jeklik/JustDied, boss_jeklik/JustReachedHome, boss_jeklik/UpdateAI#3, boss_lethon/SummonedMovementInform, boss_lethon/UpdateAI, boss_loatheb/OnEffectExecute, boss_loatheb/UpdateAI#2, boss_loatheb/UpdateAI#3, boss_maexxna/UpdateWraps, boss_magistrate_barthilas/UpdateAI, boss_majordomo_executus/DomoEvent, boss_mandokir/UpdateAI#2, boss_marli/JustDied, boss_nefarian/HandleClassCall, boss_nefarian/OnEffectExecute#2, boss_nefarian/OnEffectExecute#3, boss_nefarian/OnEffectExecute#4, boss_nefarian/OnPeriodicTickEnd, boss_nefarian/UpdateAI, boss_noth/TeleportToBalc, boss_onyxia/DoMovement, boss_onyxia/MovementInform, boss_onyxia/PhaseTransition, boss_onyxia/PhaseTwo, boss_ossirian/Aggro, boss_ossirian/OnUse, boss_ouro/JustDied, boss_ouro/JustReachedHome, boss_ouro/JustSummoned#2, boss_ouro/UpdateAI, boss_ouro/UpdateAI#2, boss_patchwerk/DoHatefulStrike, boss_ras_frostwhisper/Reset, boss_razorgore/MortPhaseUn, boss_renataki/UpdateAI, boss_sapphiron/JustDied, boss_sapphiron/UpdateAI, boss_skeram/CastBlink#2, boss_skeram/UpdateAI, boss_tendris_warpwood/UpdateAI, boss_thaddius/boss_thaddiusAI, boss_thaddius/DoPolarityShift, boss_thaddius/HandleMagneticPull, boss_thaddius/JustReachedHome, boss_thaddius/UpdateAI#3, boss_thaddius/UpdateTransitionPhase, boss_the_beast/SpellHit, boss_the_beast/UpdateAI, boss_timmy_the_cruel/UpdateAI, boss_tomb_of_seven/UpdateAI, boss_twinemperors/CheckEnrage, boss_twinemperors/OnStartTeleport, boss_twinemperors/TryHealBrother#2, boss_twinemperors/UpdateEmperor#2, boss_urok/OnUse, boss_vaelastrasz/UpdateAI, boss_vaelastrasz/UpdateAI#2, boss_vectus/UpdateAI, boss_venoxis/JustDied, boss_venoxis/UpdateAI, boss_victor_nefarius/UpdateAI, boss_viscidus/HackyScaleUpdate, boss_viscidus/SummonedMovementInform, boss_viscidus/UpdateAI, boss_viscidus/UpdateAI#2, boss_viscidus/UpdateAI#3, boss_warmaster_voone/SpellHitTarget, ChatHandler.CreatureCommands/HandleNpcTameCommand, ChatHandler.PlayerBotMgr/ShowBattleBotPathHelper, ChatHandler.TeleportCommands/HandleStartCommand, ChatHandler.TeleportCommands/HandleUnstuckCommand, ChatHandler.UnitCommands/HandleCastBackCommand, ChatHandler.UnitCommands/HandleCastCommand, ChatHandler.UnitCommands/HandleCastSelfCommand, ChatHandler.UnitCommands/HandleCastTargetCommand, ChatHandler.UnitCommands/HandleFreezeCommand, CombatBotBaseAI/SummonPetIfNeeded, Creature.Main/ApplyGameEventSpells, Creature.Main/CastSpawnSpell, Creature.Main/CastSpellOnFarthestVictim, Creature.Main/CastSpellOnHostileCasterInRange, Creature.Main/CastSpellOnNearestVictim, CreatureAI/DoCast, CreatureAI/DoCastAOE, darkshore/SetSleeping, dreadsteed_ritual/JustDied, dreadsteed_ritual/UpdateAI#4, duskwood/UpdateAI#3, dustwallow_marsh/GossipSelect_npc_cassa_crimsonwing, dustwallow_marsh/GossipSelect_npc_lady_jaina_proudmoore, dustwallow_marsh/QuestRewarded_npc_archmage_tervosh, dustwallow_marsh/UpdateAI#3, eastern_plaguelands/JustSummoned#2, eastern_plaguelands/SpellHit, eastern_plaguelands/UpdateAI#3, felwood/OnPeriodicDummy, feralas/UpdateAI, GameObject/FinishRitual, GameObject/Update, GameObject/Use, game_Battlegrounds_BattleGround/CastSpellOnTeam, gnomeregan/UpdateFollowerAI, go_scripts/GOHello_go_cat_figurine, go_scripts/GOHello_go_field_repair_bot_74A, go_scripts/GOHello_go_silithyste, instance_blackfathom_deeps/DoSpawnMobs, instance_blackrock_depths/HandleBarPatrons, instance_blackwing_lair/GOHello_go_orb_of_domination, instance_blackwing_lair/OnUse, instance_dire_maul/GossipSelect_npc_knot_thimblejack, instance_dire_maul/OnUse, instance_dire_maul/UpdateAI, instance_dire_maul/UpdateAI#8, instance_naxxramas.boss_kelthuzad/UpdateAI, instance_naxxramas.boss_kelthuzad/UpdateAI#4, instance_naxxramas.Main/EnterStoneform, instance_naxxramas.Main/LearnCraftIfCan, instance_naxxramas.Main/mob_naxxramasGarboyleAI, instance_naxxramas.Main/mob_spiritOfNaxxramasAI, instance_naxxramas.Main/OnCreatureEnterCombat, instance_naxxramas.Main/UpdateAI#2, instance_naxxramas.Main/UpdateAI#4, instance_naxxramas.Main/UpdateAI#5, instance_scarlet_monastery/Update, instance_shadowfang_keep/OnPeriodicDummy, instance_stratholme/Update, instance_temple_of_ahnqiraj/AddPlayerToStomach, instance_temple_of_ahnqiraj/PerformCthunKnockback, instance_temple_of_ahnqiraj/UpdateStomachOfCthun, instance_uldaman/SetData, instance_uldaman/SetFrozenState, instance_zulgurub/ProcessEventId_event_summon_gahzranka, instance_zulgurub/SpawnRandomBoss, instance_zulgurub/UpdateHakkarPowerStacks, Map.ScriptCommands/ScriptCommand_CastSpell, molten_core/FeignDeath, molten_core/ResurrectSelf, molten_core/UpdateAI#2, moonglade/SummonedMovementInform, npcs_special/MoveInLineOfSight#4, npcs_special/npc_target_dummyAI, npcs_special/ReceiveEmote#2, npcs_special/ResetCreature, npcs_special/ResetCreature#2, npcs_special/UpdateAI, npcs_special/UpdateAI#10, npcs_special/UpdateAI#18, npcs_special/UpdateAI#4, npcs_special/UpdateAI#6, npcs_special/UpdateAI#7, npc_j_eevee/MovementInform, OutdoorPvPEP/BuffTeams, OutdoorPvPEP/ChangeState, OutdoorPvPEP/ChangeState#2, OutdoorPvPEP/ChangeState#3, OutdoorPvPEP/ChangeState#4, OutdoorPvPEP/OnPlayerEnter, OutdoorPvPSI/HandleAreaTrigger, OutdoorPvPSI/HandleDropFlag, OutdoorPvPSI/OnPlayerEnter, PartyBotAI/UpdateAI, PartyBotAI/UpdateInCombatAI_Hunter, PartyBotAI/UpdateInCombatAI_Mage, PartyBotAI/UpdateInCombatAI_Priest, PartyBotAI/UpdateInCombatAI_Warlock, Pet.Main/AddSpell, Pet.Main/CastPetAura, Player.Main/AddQuest, Player.Main/AddSpell, Player.Main/ApplyEnchantment, Player.Main/ApplyGhostForm, Player.Main/BuildPlayerRepop, Player.Main/CastItemCombatSpell, Player.Main/LearnQuestRewardedSpells#2, Player.Main/OnGossipSelect, Player.Main/ProcessDelayedOperations, Player.Main/ResurrectPlayer, Player.Main/RewardQuest, Player.Main/SendInitialPacketsAfterAddToMap, Player.Main/UpdateAreaDependentAuras, Player.Main/UpdateTerainEnvironmentFlags, Player.Main/UpdateZoneDependentAuras, Player.Main/_LoadAuras, PlayerAI/UpdateAI#2, PlayerBotAI/UpdateAI, quest_stormwind_rendezvous/EndScene, quest_stormwind_rendezvous/UpdateAI, ruins_of_ahnqiraj/DamageTaken#2, ruins_of_ahnqiraj/OssirianTornadoAI, ruins_of_ahnqiraj/UpdateAI, scholo_trash/JustDied, scholo_trash/Resurrect, scourge_invasion/GoCircle, scourge_invasion/JustDied, scourge_invasion/JustDied#2, scourge_invasion/JustDied#3, scourge_invasion/JustDied#4, scourge_invasion/JustDied#5, scourge_invasion/OnScriptEventHappened#2, scourge_invasion/OnScriptEventHappened#3, scourge_invasion/PallidHorrorAI, scourge_invasion/SpellHit#2, scourge_invasion/SpellHit#3, scourge_invasion/SpellHit#4, scourge_invasion/SpellHit#5, scourge_invasion/UpdateAI#8, scourge_invasion/UpdateAI#9, ScriptedAI/Ambush, scripts_battlegrounds_battleground/GossipHello_npc_spirit_guide, scripts_battlegrounds_battleground/UpdateAI, scripts_battlegrounds_battleground/UpdateAI#2, silithus/DoCastTriggerSpellOnEnemies, silithus/DoTimeStopArmy, silithus/UpdateAI#7, silithus/UpdateAI#9, Spell.Effects/EffectDummy, Spell.Effects/EffectScriptEffect, spell_hunter/OnAfterApply, spell_item/OnAfterHit, spell_item/OnCast, spell_item/OnEffectExecute, spell_item/OnEffectExecute#10, spell_item/OnEffectExecute#11, spell_item/OnEffectExecute#2, spell_item/OnEffectExecute#3, spell_item/OnEffectExecute#5, spell_item/OnEffectExecute#6, spell_item/OnEffectExecute#7, spell_item/OnEffectExecute#8, spell_item/OnSuccessfulFinish, spell_mage/OnProc, spell_paladin/OnAfterHit, spell_paladin/OnEffectExecute#2, spell_priest/OnEffectExecute, spell_priest/OnHit, spell_special/OnAfterApply#2, spell_special/OnAfterApply#3, spell_special/OnAfterApply#4, spell_special/OnEffectExecute#4, spell_special/OnSuccessfulFinish, spell_warlock/OnEffectExecute#4, spell_warlock/OnSuccessfulDispel, spell_warlock/OnSummon, spell_warrior/OnEffectExecute#5, stranglethorn_vale/UpdateAI#3, stratholme/GOOpen_go_stratholme_postbox, stratholme/JustSummoned, stratholme/OnPeriodicDummy, stratholme/ReceiveEmote#2, stratholme/UpdateAI#4, sunken_temple/UpdateAI, the_barrens/JustSummoned, thousand_needles/ReceiveEmote, thousand_needles/Reset#4, ThreatListCopier.battleground_alterac/Aggro#2, ThreatListCopier.battleground_alterac/GossipHello_npc_AVBlood_collector, ThreatListCopier.battleground_alterac/Reset#11, ThreatListCopier.battleground_alterac/Reset#12, ThreatListCopier.battleground_alterac/UpdateAI#6, ThreatListCopier.battleground_alterac/UpdateAI#7, ThreatListCopier.battleground_alterac/UpdateRenferalAI, ThreatListCopier.battleground_alterac/UpdateThurlogaAI, ThreatListCopier.battleground_alterac/WaypointReached, Totem/Summon, TotemAI/UpdateAI, uldaman/UpdateAI#2, Unit.AuraProcHandler/HandleAddTargetTriggerAuraProc, Unit.AuraProcHandler/HandleProcTriggerSpellAuraProc, Unit.Main/DealDamage, Unit.Main/DealMeleeDamage, Unit.Main/InitCharmCreateSpells, Unit.Main/InitPossessCreateSpells, Unit.Main/ModifyAuraState, Unit.SpellAuras/HandleAuraDummy, Unit.SpellAuras/HandleAuraModShapeshift, Unit.SpellAuras/HandleCastOnAuraRemoval, Unit.SpellAuras/HandlePeriodicTriggerSpell, Unit.SpellAuras/HandleShapeshiftBoosts, Unit.SpellAuras/HandleSpellSpecificBoosts, Unit.SpellAuras/HandleSpiritOfRedemption, Unit.SpellAuras/ModPossess, Unit.SpellAuras/PeriodicDummyTick, Unit.SpellAuras/PeriodicTick, Unit.SpellAuras/ReapplyAffectedPassiveAuras#2, Unit.SpellAuras/TriggerSpell, wailing_caverns/UpdateEscortAI, WaypointMovementGenerator/Finalize, western_plaguelands/UpdateAI, wetlands/JustStartedEscort, wetlands/Reset, WorldSession.DuelHandler/HandleDuelCancelledOpcode, WorldSession.NPCHandler/SendBindPoint, WorldSession.QuestHandler/HandleQuestgiverAcceptQuestOpcode, WorldSession.SkillHandler/HandleTalentWipeConfirmOpcode, world_event_wareffort/EnterCombat, world_event_wareffort/JustDied, ZoneScript/TeamCastSpell, zulgurub_trash/JustDied, zulgurub_trash/UpdateAI, zulgurub_trash/UpdateAI#2, zulgurub_trash/UpdateAI#3, zulgurub_trash/UpdateAI#4, zulgurub_trash/UpdateAI#5, zulgurub_trash/UpdateAI#6 | — |
| CastSpell | method | Aura/GetCasterGuid, Aura/GetEffIndex, Aura/GetId, Aura/GetSpellProto, Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out, Object/GetGuidStr, Object/ToGameObject, Object/ToUnit, ObjectGuid/ObjectGuid, ObjectGuid/operator!, Spell.Main/GetCastingObject, Spell.Main/prepare, Spell.Main/SetCastItem, Spell.Main/Spell, Spell.Main/Spell#2, SpellCastTargetsInfo/setDestination, SpellCastTargetsInfo/setGOTarget, SpellCastTargetsInfo/setSource, SpellCastTargetsInfo/setUnitTarget, SpellCastTargetsInfo/SpellCastTargets, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | AiBotAI.Combat/DrinkAndEat, AiBotAI.Combat/UpdateInCombatAI_Paladin, BattleBotAI.Main/DrinkAndEat, BattleBotAI.Main/UpdateFlagCarrierAI, BattleBotAI.Main/UpdateInCombatAI_Paladin, boss_patchwerk/DoHatefulStrike, ChatHandler.PlayerBotMgr/HandlePartyBotAoECommand, CombatBotBaseAI/DoCastSpell, CombatBotBaseAI/UseItemEffect, Creature.Main/LoadDefaultAuras, game_Battlegrounds_BattleGround/RewardSpellCast, PartyBotAI/DrinkAndEat, PartyBotAI/UpdateInCombatAI_Paladin, Player.Main/ApplyEquipSpell, Player.Main/CastHighestStealthRank, Player.Main/CheckAreaExploreAndOutdoor, ScriptedAI/DoCastSpell, Spell.Effects/EffectTriggerSpell, Spell.Main/HandleAddTargetTriggerAuras, spell_paladin/OnEffectExecute#4, TotemAI/TotemAI, Unit.AuraProcHandler/TriggerProccedSpell, Unit.SpellAuras/TriggerSpell, WorldSession.PetHandler/HandlePetAction, WorldSession.SpellHandler/HandleSelfResOpcode | — |
| CastCustomSpell#2 | method | Aura/GetEffIndex, Aura/GetId, Log.Main/Out, Object/GetGuidStr, ObjectGuid/ObjectGuid, SpellMgr/GetSpellEntry, SpellMgr/Instance | boss_baron_geddon/UpdateAI, boss_four_horsemen/SpellHitTarget, boss_nefarian/SetAura, ChatHandler.UnitCommands/HandleModifySpellPowerCommand, ChatHandler.UnitCommands/HandlePossessCommand, Spell.Effects/EffectDummy, Spell.Effects/EffectFeedPet, Spell.Effects/EffectScriptEffect, spell_druid/OnEffectExecute, spell_item/OnAfterApply#3, spell_shaman/OnEffectExecute, spell_shaman/OnPeriodicTrigger, spell_warlock/OnEffectExecute#5, spell_warrior/OnCast, Unit.AuraProcHandler/HandleDummyAuraProc, Unit.Main/Kill, Unit.SpellAuras/HandleAuraModIncreaseHealth, Unit.SpellAuras/HandleAuraModStat, Unit.SpellAuras/HandleShapeshiftBoosts, Unit.SpellAuras/TriggerSpell | — |
| CastCustomSpell | method | Aura/GetCasterGuid, Aura/GetEffIndex, Aura/GetId, Aura/GetSpellProto, Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out, Object/GetGuidStr, Object/ToGameObject, Object/ToUnit, ObjectGuid/ObjectGuid, ObjectGuid/operator!, Spell.Main/prepare, Spell.Main/SetCastItem, Spell.Main/Spell, Spell.Main/Spell#2, SpellCastTargetsInfo/setUnitTarget, SpellCastTargetsInfo/SpellCastTargets | Unit.AuraProcHandler/TriggerProccedSpell | — |
| CastSpell#4 | method | Aura/GetEffIndex, Aura/GetId, Log.Main/Out, Object/GetGuidStr, ObjectGuid/ObjectGuid, SpellMgr/GetSpellEntry, SpellMgr/Instance | ashenvale/HitBanner, boss_jeklik/SpellHitTarget, boss_urok/HitBanner, ChatHandler.UnitCommands/HandleCastDistCommand, instance_blackwing_lair/UpdateAI#3, instance_onyxia_lair/OnObjectCreate | — |
| CastSpell#3 | method | Aura/GetCasterGuid, Aura/GetEffIndex, Aura/GetId, Aura/GetSpellProto, Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out, Object/GetGuidStr, Object/ToGameObject, Object/ToUnit, ObjectGuid/ObjectGuid, ObjectGuid/operator!, Spell.Main/prepare, Spell.Main/SetCastItem, Spell.Main/Spell, Spell.Main/Spell#2, SpellCastTargetsInfo/setDestination, SpellCastTargetsInfo/SpellCastTargets | Spell.Effects/EffectTriggerMissileSpell, Unit.SpellAuras/TriggerSpell | — |
| AddGCD | method | World/GetCurrentClockTime | Player.Main/AddGCD, Spell.Main/prepare#2 | — |
| HasGCD | method | — | AiBotAI.Combat/UpdateOutOfCombatAI_Druid, AiBotAI.Combat/UpdateOutOfCombatAI_Paladin, AiBotAI.Combat/UpdateOutOfCombatAI_Priest, AiBotAI.Combat/UpdateOutOfCombatAI_Warlock, BattleBotAI.Main/UpdateOutOfCombatAI_Druid, BattleBotAI.Main/UpdateOutOfCombatAI_Paladin, BattleBotAI.Main/UpdateOutOfCombatAI_Priest, BattleBotAI.Main/UpdateOutOfCombatAI_Warlock, CombatBotBaseAI/CanTryToCastSpell, PartyBotAI/UpdateOutOfCombatAI_Druid, PartyBotAI/UpdateOutOfCombatAI_Mage, PartyBotAI/UpdateOutOfCombatAI_Paladin, PartyBotAI/UpdateOutOfCombatAI_Priest, PartyBotAI/UpdateOutOfCombatAI_Warlock, PetAI/UpdateAI, Player.Main/CastItemCombatSpell, Spell.Main/CheckCast | — |
| AddCooldown | method | CooldownContainer/AddCooldown, World/GetCurrentClockTime | Creature.Main/StartCooldownForSummoner, Map.ScriptCommands/ScriptCommand_AddSpellCooldown, Spell.Effects/EffectDummy, Spell.Effects/EffectScriptEffect, Spell.Main/SendSpellCooldown, Unit.AuraProcHandler/TriggerProccedSpell, Unit.Main/AddGameObject, Unit.Main/ProcDamageAndSpellFor, Unit.Main/RemoveGameObject, Unit.SpellAuras/_RemoveSpellAuraHolder | — |
| UpdateCooldowns | method | CooldownContainer/Update | GameObject/Update, Map.Main/ProcessSessionPackets, Unit.Main/Update | — |
| CheckLockout | method | — | Spell.Main/CheckCasterAuras | — |
| GetExpireTime | method | CooldownContainer/end, CooldownContainer/FindBySpellId, CooldownData/GetSpellCDExpireTime, CooldownData/IsPermanent | Player.Main/LockOutSpells | — |
| IsSpellReady | method | CooldownContainer/end, CooldownContainer/FindByCategory, CooldownContainer/FindBySpellId, SpellEntry/GetSpellSchoolMask | CombatBotBaseAI/UseItemEffect, Player.Main/CastHighestStealthRank, Spell.Main/CheckCast, Spell.Main/CheckPetCast, Unit.AuraProcHandler/TriggerProccedSpell, WorldSession.PetHandler/HandlePetAction, WorldSession.PetHandler/HandlePetCastSpellOpcode | — |
| IsSpellReady#2 | method | SpellMgr/GetSpellEntry, SpellMgr/Instance | AiBotAI.Combat/UpdateInCombatAI_Rogue, BattleBotAI.Main/UpdateInCombatAI_Rogue, ChatHandler.TeleportCommands/HandleUnstuckCommand, CombatBotBaseAI/CanTryToCastSpell, PartyBotAI/UpdateInCombatAI_Rogue, PetAI/UpdateAI, Player.Main/SelectResurrectionSpellId, PlayerBotAI/UpdateAI, Spell.Main/finish, Unit.Main/ProcDamageAndSpellFor, WorldSession.PetHandler/HandlePetCastSpellOpcode | — |
| IsSpellOnPermanentCooldown | method | CooldownContainer/end, CooldownContainer/FindBySpellId, CooldownData/IsPermanent, CooldownData/IsSpellCDExpired, World/GetCurrentClockTime | Spell.Main/SendCastResult#2 | — |
| LockOutSpells | method | World/GetCurrentClockTime | Creature.Main/LockOutSpells, Player.Main/LockOutSpells, Spell.Effects/EffectInterruptCast | — |
| RemoveSpellCooldown#2 | method | SpellMgr/GetSpellEntry, SpellMgr/Instance | Map.ScriptCommands/ScriptCommand_RemoveSpellCooldown | — |
| RemoveSpellCooldown | method | CooldownContainer/RemoveBySpellId | ChatHandler.UnitCommands/HandleCooldownClearCommand, Spell.Main/SetTargetMap | — |
| RemoveSpellCategoryCooldown | method | CooldownContainer/RemoveByCategory | — | — |
| ResetGCD | method | — | Spell.Main/cancel | — |
| ConvertMillisecondToStr | function | — | — | — |
| PrintCooldownList | method | ChatHandler.Chat/PSendSysMessage, CooldownData/GetCatCDExpireTime, CooldownData/GetSpellCDExpireTime, CooldownData/IsPermanent, World/GetCurrentClockTime | ChatHandler.UnitCommands/HandleCooldownListCommand | — |
