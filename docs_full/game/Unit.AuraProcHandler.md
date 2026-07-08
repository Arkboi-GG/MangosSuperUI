<!-- provenance: boundary-bleed -->
# Unit.AuraProcHandler

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Unit.AuraProcHandler

## Purpose & Responsibilities

`Unit.AuraProcHandler` implements the core logic for determining whether active auras on a `Unit` should "proc" (trigger secondary effects) in response to combat events, such as taking damage, dealing damage, or casting spells. It serves as the execution engine for the game's proc system, bridging the gap between high-level combat events and specific spell behaviors defined in the database or hard-coded for legacy compatibility.

The unit performs two distinct phases:
1.  **Evaluation (`IsTriggeredAtSpellProcEvent`):** Determines if a specific aura *should* trigger based on complex criteria including spell families, equipment requirements, location (indoors/outdoors), chance rolls (including Per-Minute Proc calculations), and hard-coded exceptions for specific spells (e.g., Eye for an Eye, Sweeping Strikes).
2.  **Execution (`Handle...AuraProc` methods):** If an aura is determined to trigger, the corresponding handler function executes the effect. These effects range from casting new spells (`HandleProcTriggerSpellAuraProc`), dealing direct damage (`HandleProcTriggerDamageAuraProc`), modifying stats, removing other auras (breaking roots/fears), or applying cooldowns.

This unit relies heavily on the `AuraProcHandler` global function pointer array to dispatch calls to the correct handler based on the `AuraType` enum. It contains significant hard-coded logic for specific World of Warcraft spells (particularly from the Classic/TBC era) that were not fully abstracted into the `spell_proc_event` database table.

## Member-by-Member Behavior

### Proc Evaluation Logic

**SpellCanTrigger**
A free-standing utility function used internally by `IsTriggeredAtSpellProcEvent`. It checks if a `procSpell` matches the family and effect item type of the triggering `spellProto`. This is used for specific hard-coded checks like "Frosty Zap."

**IsTriggeredAtSpellProcEvent**
The central decision-making method for proc eligibility. It takes the victim, the aura holder, the spell that caused the event (`procSpell`), and various flags.
*   **Hard-Coded Exclusions:** Immediately fails for specific spells like Flurry (on extra attacks) or Sap (weapon procs).
*   **Spell-Specific Logic:** Contains extensive `if` blocks for specific spells (e.g., Eye for an Eye, Improved Lay on Hands, Wrath of Cenarius, Omen of Clarity, Inspiration, Sweeping Strikes, Elemental Mastery). These blocks often check spell IDs, icons, and family flags to enforce rules that differ from standard proc logic.
*   **Script Hook:** Calls `AuraScript::OnCheckProc` if the aura has a custom script, allowing scripts to override the default logic.
*   **Database Lookup:** Retrieves `SpellProcEventEntry` from `SpellMgr`. If present, it uses the database-defined flags and chances; otherwise, it falls back to the spell prototype's `procFlags`.
*   **General Constraints:**
    *   Checks if the kill grants Honor/XP (for `PROC_FLAG_KILL`).
    *   Prevents self-triggering for non-periodic auras.
    *   Validates equipment requirements (weapon/armor class and subclass) for players.
    *   Checks for "No Proc" attributes and "Can Proc From Procs" attributes to prevent infinite chains.
    *   Checks location constraints (e.g., `SPELL_ATTR_EX3_ONLY_PROC_OUTDOORS`).
    *   Calculates final chance using base chance, custom DB chance, or PPM (Per-Minute Proc) formula based on weapon speed.
    *   Applies spell modifiers and cheat options (`PLAYER_CHEAT_ALWAYS_PROC`).
    *   Returns `SPELL_PROC_TRIGGER_OK`, `SPELL_PROC_TRIGGER_FAILED`, or `SPELL_PROC_TRIGGER_ROLL_FAILED`.

### Proc Execution Handlers

These methods are invoked when an aura successfully passes the evaluation phase. They perform the actual effect.

**TriggerProccedSpell#2**
An overload of `TriggerProccedSpell` that accepts a `triggeredSpellId` (uint32) instead of a `SpellEntry` pointer. It retrieves the spell entry via `SpellMgr::GetSpellEntry` and delegates to the primary `TriggerProccedSpell` method. It logs an error if the spell ID is invalid.

**TriggerProccedSpell**
The core mechanism for casting a spell as a result of a proc.
*   Validates that the target is alive.
*   Checks if the spell is ready (not on cooldown) via `SpellCaster::IsSpellReady`.
*   If `basepoints` are provided (customizing spell power/damage), it calls `SpellCaster::CastCustomSpell`. Otherwise, it calls `SpellCaster::CastSpell`.
*   Applies a cooldown to the triggered spell if specified.

**HandleHasteAuraProc**
Handles procs for haste-related auras. Specifically, it contains logic for the "Flurry" talent (Spell Icon 108). If Flurry is on its last charge and the proc is caused by a critical hit, it prevents the charge from being consumed (reapplying the buff instead of decrementing).

**HandleDummyAuraProc**
A massive switch-case handler for `SPELL_AURA_DUMMY`. Dummy auras are placeholders that trigger specific effects defined by code rather than generic spell effects. This method handles dozens of specific spells, including:
*   **Generic:** Illusion Passive (despawn creature), Eye for an Eye (reflect damage), Sweeping Strikes (select random target and deal damage), Retaliation (counter-attack), Twisted Reflection, Unstable Power (consume charges), Viscidus mechanics (freeze/explode), Adaptive Warding (Mage set bonus), Obsidian Armor.
*   **Mage:** Magic Absorption (refund mana), Master of Elements (refund mana on crit).
*   **Warrior:** (Currently empty/break).
*   **Priest:** Vampiric Embrace (heal on damage taken), Oracle Healing Bonus, Greater Heal (Tier 3 bonus).
*   **Druid:** Healing Touch refunds (Dreamwalker Raiment, Idol of Longevity).
*   **Rogue:** Clean Escape (Vanish on CC), Blade Flurry (spread damage).
*   **Paladin:** Seal of Righteousness (calculate damage based on weapon speed and talents, cast damage spell, trigger weapon enchants), Holy Power (Redemption Armor set).
*   **Shaman:** Totemic Power (Earthshatterer set), Lesser Healing Wave refund.

**HandleProcTriggerSpellAuraProc**
Handles `SPELL_AURA_PROC_TRIGGER_SPELL`. It determines the target and base points for the triggered spell.
*   **Hard-Coded Cases:** Handles specific spells like Aegis of Preservation, Mana Drain Trigger, Deadly Swiftness, Talisman of Ascendance, Pyroclasm (Warlock), Cheat Death, Shadowguard (Priest), Blessed Recovery, Aspect of the Cheetah/Pack, Seal of Righteousness (legacy code path), Judgement of Light/Wisdom, Illumination, Lightning Shield, and Mana Surge.
*   **Target Selection:** Defaults to the victim for harmful spells and the caster for positive spells, unless overridden by specific logic (e.g., Judgement of Light heals the attacker).
*   **Timing:** For some effects (like Ruthlessness), it schedules the cast via `m_Events.AddLambdaEventAtOffset` to ensure combo points are added *after* the finishing move resolves.

**HandleProcTriggerDamageAuraProc**
Handles `SPELL_AURA_PROC_TRIGGER_DAMAGE`. It calculates damage directly without casting a visible spell.
*   Checks for miss/resist via `SpellCaster::SpellHitResult`.
*   Calculates damage using `SpellCaster::CalculateSpellEffectValue` and applies bonuses via `SpellDamageBonusDone`/`SpellDamageBonusTaken`.
*   Applies absorbs/resists/blocks via `Unit::CalculateAbsorbResistBlock`.
*   Deals the damage and sends log packets to the client.

**HandleOverrideClassScriptAuraProc**
Handles `SPELL_AURA_OVERRIDE_CLASS_SCRIPTS`. It uses the `miscvalue` of the aura modifier as a script ID to determine behavior.
*   Handles spells like Crepuscule, Improved Blizzard, Improved Mend Pet, Corrupted Healing, and Druid Tier 3 bonuses.
*   Often involves rolling a chance and casting a specific spell based on the victim's power type or the proc spell's properties.

**HandleModCastingSpeedNotStackAuraProc**
A simple filter for `SPELL_AURA_MOD_CASTING_SPEED_NOT_STACK`. It ensures the proc only occurs if the triggering event was a spell cast with a non-zero cast time (ignoring melee hits and instant casts).

**HandleReflectSpellsSchoolAuraProc**
Filters `SPELL_AURA_REFLECT_SPELLS_SCHOOL`. It ensures the proc only occurs if the triggering spell's school matches the mask defined in the aura's `miscvalue`.

**HandleModPowerCostSchoolAuraProc**
Filters `SPELL_AURA_MOD_POWER_COST_SCHOOL`. It ensures the proc only occurs if the triggering spell had a mana cost and its school matches the aura's `miscvalue`.

**HandleMechanicImmuneResistanceAuraProc**
Filters `SPELL_AURA_MECHANIC_IMMUNITY`. It ensures the proc only occurs if the triggering spell's mechanic matches the aura's `miscvalue`.

**HandleAddTargetTriggerAuraProc**
Handles `SPELL_AURA_ADD_TARGET_TRIGGER`. It reads the chance from the spell's `EffectBasePoints[0]`. It contains specific logic for Blizzard (dividing chance by 8 ticks) and specific spells like "Casque tete de loup" and "Gelee soudaine" to determine if the spell should be cast on the caster or the victim.

**HandleModResistanceAuraProc**
Handles `SPELL_AURA_MOD_RESISTANCE`. Specifically checks for "Inner Fire" (Priest), ensuring it only procs on real damage (amount > 0).

**HandleModDamageAuraProc**
Handles `SPELL_AURA_MOD_DAMAGE_DONE`.
*   Checks if the triggering spell's school matches the aura's mask.
*   Contains specific logic for "Zandalarian Hero Charm" (Unstable Power), checking if the proc spell is a direct damage/heal spell or specific totems, and consuming a charge via `RemoveAuraHolderFromStack`.

**HandleRemoveByDamageChanceProc**
Handles `SPELL_AURA_MOD_ROOT`. It calculates a chance to break the root based on the damage taken relative to the caster's level (`25 * Level - 150`). If the roll succeeds, it removes the aura.

**HandleRemoveFearByDamageChanceProc**
Handles `SPELL_AURA_MOD_FEAR`. Similar to root breaking but with more complex logic:
*   Checks if the mechanic is Fear or Turn.
*   Adjusts the difficulty to break based on whether the target is a Player (3x easier) or a mob.
*   Adjusts difficulty based on whether the damage source is a DOT (3x harder to break after patch 1.11).
*   Uses the final damage amount (post-modifiers) for the calculation.

**HandleInvisibilityAuraProc**
Handles `SPELL_AURA_MOD_INVISIBILITY`. If the triggering event is not passive and the spell is positive, it removes the invisibility aura via `RemoveAurasDueToSpell`.

## Cross-Unit Boundaries

*   **Called By:**
    *   `Unit.Main/ProcDamageAndSpellFor`: This is the primary entry point. When a unit deals or takes damage/spells, `Unit.Main` iterates through active auras and calls `IsTriggeredAtSpellProcEvent` to see if they should fire. If they do, it invokes the corresponding handler from the `AuraProcHandler` array.
    *   `spell_mage/OnProc#2`: Specific mage spell scripts may call `TriggerProccedSpell#2` to manually trigger a proc effect.

*   **Calls Out:**
    *   **Spell System (`SpellCaster`, `SpellMgr`, `SpellEntry`):** Extensively used to retrieve spell data, check cooldowns, cast spells, calculate damage, and verify spell attributes.
    *   **Unit System (`Unit.Main`, `Player.Main`, `Creature.Main`):** Used to check unit states (alive, mounted, class), retrieve stats (health, mana, level), select targets, and modify unit properties (remove auras, deal damage).
    *   **Item System (`game_Objects_Item`):** Used to verify equipment requirements for procs (weapon/armor class) and retrieve item prototypes.
    *   **Utility (`shared_Util`):** Used for random number generation (`roll_chance_f`, `roll_chance_u`, `dither`) to determine proc success and calculate varied damage values.
    *   **Logging (`Log.Main`):** Used to output errors for missing spells or unhandled cases.
    *   **World/Grid (`WorldObject.Object`, `GridMap`, `Map.Main`):** Used for terrain checks (indoors/outdoors) and retrieving units from the map for target selection.

## Data Model

This unit does not directly query or modify database tables. It relies on data loaded into memory by `SpellMgr` (from `spell_dbc` and `spell_proc_event` tables) and `ItemPrototype` (from `item_template`). No direct SQL interactions occur within this translation unit.

## Notable Implementation Details

*   **Hard-Coded Spell Logic:** A significant portion of `IsTriggeredAtSpellProcEvent` and `HandleDummyAuraProc` consists of hard-coded `if` statements for specific spell IDs. This was necessary in earlier versions of WoW emulation because the `spell_proc_event` database table did not support all the complex conditional logic required by certain talents and items. Maintainers should be cautious when adding new spells; if they don't fit the generic model, they may require hard-coded entries here.
*   **PPM Calculation:** The unit implements the Per-Minute Proc (PPM) formula. If a `spellProcEvent` has a `ppmRate`, the chance is calculated as `(ppmRate * WeaponSpeed) / 60.0`. This ensures that faster weapons do not proc more frequently than slower ones for PPM-based items.
*   **Legacy Code Paths:** There are numerous `#if SUPPORTED_CLIENT_BUILD` directives. These indicate that the behavior of certain spells (e.g., Eye for an Eye, Sweeping Strikes, Fear breaking) changed across different patches of World of Warcraft. The code maintains backward compatibility for older client builds.
*   **Thread Safety:** The unit assumes it is called from the main game loop thread. It accesses `Unit` members directly without locks, relying on the single-threaded nature of the entity update loop.
*   **Charge Consumption:** Some handlers (e.g., `HandleModDamageAuraProc` for Zandalarian Hero Charm, `HandleDummyAuraProc` for Unstable Power) explicitly call `RemoveAuraHolderFromStack` to consume charges. This is a side-effect of the proc evaluation.
*   **Error Handling:** If a triggered spell ID is invalid or not found in `SpellMgr`, the unit logs an error via `sLog.Out` and returns `SPELL_AURA_PROC_FAILED`. This prevents crashes but may result in silent failures if logging is not monitored.

## Member Reference

**SpellCanTrigger**
A free function that checks if a `procSpell` matches the family and effect item type of a `spellProto`. Used for specific hard-coded proc checks.

**IsTriggeredAtSpellProcEvent**
Determines if an aura should proc based on spell flags, equipment, location, chance rolls (including PPM), and hard-coded exceptions for specific spells. Returns a trigger check status.

**TriggerProccedSpell#2**
Overload that accepts a `triggeredSpellId`, retrieves the `SpellEntry`, and delegates to `TriggerProccedSpell`.

**TriggerProccedSpell**
Casts a triggered spell on a target, handling custom base points, cooldowns, and validity checks.

**HandleHasteAuraProc**
Handles haste aura procs, specifically preserving Flurry charges on critical hits.

**HandleDummyAuraProc**
Executes effects for `SPELL_AURA_DUMMY` auras, containing extensive hard-coded logic for specific spells like Sweeping Strikes, Eye for an Eye, and class set bonuses.

**HandleProcTriggerSpellAuraProc**
Executes effects for `SPELL_AURA_PROC_TRIGGER_SPELL` auras, determining targets and base points for the triggered spell, with hard-coded cases for specific abilities.

**HandleProcTriggerDamageAuraProc**
Executes direct damage for `SPELL_AURA_PROC_TRIGGER_DAMAGE` auras, calculating damage, applying bonuses/resists, and sending log packets.

**HandleOverrideClassScriptAuraProc**
Executes effects for `SPELL_AURA_OVERRIDE_CLASS_SCRIPTS` auras, using the aura's miscvalue as a script ID to determine behavior.

**HandleModCastingSpeedNotStackAuraProc**
Filters procs for `SPELL_AURA_MOD_CASTING_SPEED_NOT_STACK`, ensuring they only trigger on non-instant spell casts.

**HandleReflectSpellsSchoolAuraProc**
Filters procs for `SPELL_AURA_REFLECT_SPELLS_SCHOOL`, ensuring the triggering spell's school matches the aura's mask.

**HandleModPowerCostSchoolAuraProc**
Filters procs for `SPELL_AURA_MOD_POWER_COST_SCHOOL`, ensuring the triggering spell had a mana cost and matching school.

**HandleMechanicImmuneResistanceAuraProc**
Filters procs for `SPELL_AURA_MECHANIC_IMMUNITY`, ensuring the triggering spell's mechanic matches the aura's miscvalue.

**HandleAddTargetTriggerAuraProc**
Executes effects for `SPELL_AURA_ADD_TARGET_TRIGGER` auras, reading chance from spell base points and handling specific target selection logic.

**HandleModResistanceAuraProc**
Executes effects for `SPELL_AURA_MOD_RESISTANCE` auras, specifically checking for Inner Fire.

**HandleModDamageAuraProc**
Executes effects for `SPELL_AURA_MOD_DAMAGE_DONE` auras, checking school masks and handling charge consumption for specific items like Zandalarian Hero Charm.

**HandleRemoveByDamageChanceProc**
Calculates a chance to break roots based on damage taken and level, removing the aura if successful.

**HandleRemoveFearByDamageChanceProc**
Calculates a chance to break fears based on damage taken, level, player status, and DOT sources, removing the aura if successful.

**HandleInvisibilityAuraProc**
Removes invisibility auras when triggered by non-passive, positive spells.

---

<!-- machine-true, projected from graph.json -->

## Map — Unit.AuraProcHandler

*Source:* UnitAuraProcHandler.cpp, Unit.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| SpellCanTrigger | function | — | — | — |
| IsTriggeredAtSpellProcEvent | method | AuraScript/OnCheckProc, game_Objects_Item/GetProto, game_Objects_Item/IsBroken, GridMap/IsOutdoors, Object/GetObjectGuid, Object/IsPlayer, ObjectGuid/operator!=, Player.Main/GetItemByPos, Player.Main/HasCheatOption, Player.Main/IsHonorOrXPTarget, shared_Util/roll_chance_f, shared_Util/roll_chance_u, SpellAuraHolder/GetAuraScript, SpellAuraHolder/GetCasterGuid, SpellAuraHolder/GetSpellProto, SpellAuraHolder/GetTarget, SpellEntry/HasAttribute#5, SpellEntry/IsPositiveSpell#4, SpellEntry/IsSpellAppliesAura, SpellMgr/GetSpellProcEvent, SpellMgr/Instance, SpellMgr/IsSpellProcEventCanTriggeredBy, Unit.Main/CanUseEquippedWeapon, Unit.Main/GetAttackTime, Unit.Main/GetPPMProcChance, Unit.Main/GetSpellModOwner, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/GetTerrain | Unit.Main/ProcDamageAndSpellFor | — |
| TriggerProccedSpell#2 | method | Log.Main/Out, ObjectGuid/ObjectGuid, SpellMgr/GetSpellEntry, SpellMgr/Instance | spell_mage/OnProc#2 | — |
| TriggerProccedSpell | method | ObjectGuid/ObjectGuid, SpellCaster/AddCooldown, SpellCaster/CastCustomSpell, SpellCaster/CastSpell, SpellCaster/IsSpellReady, Unit.Main/IsAlive | — | — |
| HandleHasteAuraProc | method | Aura/GetHolder, Aura/GetSpellProto, SpellAuraHolder/GetAuraCharges | — | — |
| HandleDummyAuraProc | method | Aura/GetCasterGuid, Aura/GetCastItemGuid, Aura/GetEffIndex, Aura/GetId, Aura/GetModifier, Aura/GetSpellProto, Creature.Main/DespawnOrUnsummon, game_Objects_Item/GetProto, Log.Main/Out, Object/GetObjectGuid, Object/GetTypeId, Object/ToCreature, ObjectGuid/operator!=, Player.Main/CastItemCombatSpell, Player.Main/GetItemByGuid, Player.Main/GetItemByPos, Player.Main/GetSpellMod, shared_Util/dither, shared_Util/ditheru, SpellCaster/CalcArmorReducedDamage, SpellCaster/CastCustomSpell#2, SpellCaster/SpellDamageBonusDone, SpellDefines/GetFirstSchoolInMask, SpellDefines/GetSchoolMask, SpellEntry/GetSpellSchoolMask, Unit.Main/GetAurasByType, Unit.Main/GetClass, Unit.Main/GetCreateMana, Unit.Main/GetHealthPercent, Unit.Main/GetMaxHealth, Unit.Main/GetMaxPower, Unit.Main/GetPowerType, Unit.Main/HasUnitState, Unit.Main/IsAlive, Unit.Main/RemoveAuraHolderFromStack, Unit.Main/RemoveAurasDueToSpell, Unit.Main/SelectRandomUnfriendlyTarget, Unit.Main/SpellDamageBonusTaken, WorldObject.Object/HasInArc, WorldObject.Object/MonsterTextEmote#2 | — | — |
| HandleProcTriggerSpellAuraProc | method | Aura/GetCastItemGuid, Aura/GetEffIndex, Aura/GetModifier, Aura/GetSpellProto, Log.Main/Out, Map.Main/GetUnit, Object/GetObjectGuid, Object/GetTypeId, Object/IsInWorld, Object/ToPlayer, Player.Main/GetItemByGuid, shared_Util/dither, shared_Util/roll_chance_f, SpellCaster/CastSpell#2, SpellCaster/GetCurrentSpell, SpellCastTargetsInfo/getUnitTarget, SpellEntry/HasEffect, SpellEntry/IsAreaOfEffectSpell, SpellEntry/IsPositiveSpell, SpellMgr/GetSpellEntry, SpellMgr/Instance, Unit.Main/GetHealth, Unit.Main/GetMaxHealth, Unit.Main/GetVictim, Unit.Main/IsAlive, World/getConfig#4, WorldObject.Object/GetMap | — | — |
| HandleProcTriggerDamageAuraProc | method | Aura/GetEffIndex, Aura/GetId, Aura/GetModifier, Aura/GetSpellProto, Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out, shared_Util/ditheru, SpellCaster/CalculateSpellEffectValue, SpellCaster/DealDamageMods, SpellCaster/DealSpellDamage, SpellCaster/SendSpellDamageResist, SpellCaster/SendSpellNonMeleeDamageLog, SpellCaster/SpellDamageBonusDone, SpellCaster/SpellHitResult, SpellNonMeleeDamage/SpellNonMeleeDamage, Unit.Main/CalculateAbsorbResistBlock, Unit.Main/IsAlive, Unit.Main/SpellDamageBonusTaken | — | — |
| HandleOverrideClassScriptAuraProc | method | Aura/GetCastItemGuid, Aura/GetModifier, Object/GetTypeId, Player.Main/GetItemByGuid, shared_Util/roll_chance_i, SpellEntry/HasEffect, Unit.Main/GetPowerType, Unit.Main/IsAlive | — | — |
| HandleModCastingSpeedNotStackAuraProc | method | SpellEntry/GetCastTime | — | — |
| HandleReflectSpellsSchoolAuraProc | method | Aura/GetModifier, SpellDefines/GetSchoolMask | — | — |
| HandleModPowerCostSchoolAuraProc | method | Aura/GetModifier, SpellDefines/GetSchoolMask | — | — |
| HandleMechanicImmuneResistanceAuraProc | method | Aura/GetModifier | — | — |
| HandleAddTargetTriggerAuraProc | method | Aura/GetSpellProto, Log.Main/Out, Object/GetGUID, shared_Util/roll_chance_f, SpellCaster/CastSpell#2 | — | — |
| HandleModResistanceAuraProc | method | Aura/GetSpellProto | — | — |
| HandleModDamageAuraProc | method | Aura/GetId, Aura/GetModifier, SpellDefines/GetSchoolMask, SpellEntry/HasAura, SpellEntry/IsDirectDamageSpell, SpellEntry/IsHealSpell, Unit.Main/RemoveAuraHolderFromStack | — | — |
| HandleRemoveByDamageChanceProc | method | Aura/GetCasterGuid, Aura/GetId, Aura/SetInUse, shared_Util/roll_chance_f, Unit.Main/GetLevel, Unit.Main/RemoveAurasByCasterSpell | — | — |
| HandleRemoveFearByDamageChanceProc | method | Aura/GetCasterGuid, Aura/GetId, Aura/GetSpellProto, Aura/SetInUse, Object/IsPlayer, shared_Util/roll_chance_f, Unit.Main/GetLevel, Unit.Main/RemoveAurasByCasterSpell | — | — |
| HandleInvisibilityAuraProc | method | Aura/GetId, Aura/GetSpellProto, SpellEntry/HasAttribute, SpellEntry/IsPositiveSpell#4, Unit.Main/RemoveAurasDueToSpell | — | — |

---

<!-- verify: boundary-bleed | foreign: attack, aura, kill, Unit, update -->
