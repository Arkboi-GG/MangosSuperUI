# CombatBotBaseAI

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# CombatBotBaseAI

`CombatBotBaseAI` is the foundational abstract base class for player-controlled bot artificial intelligence within the `wowvmangos` codebase. It provides the shared infrastructure required for bots to function as viable combatants, healers, or support characters in a World of Warcraft-like environment. Rather than implementing specific combat rotations, this unit handles the "plumbing" of bot behavior: initializing character capabilities (spells, talents, gear), managing inventory and reagents, selecting valid targets for healing, buffing, and crowd control, and handling network packet responses for teleportation, trading, and battlegrounds.

It serves as the parent class for specialized AI implementations (`AiBotAI`, `BattleBotAI`, `PartyBotAI`), which inherit these utilities and implement the actual decision-making logic for specific classes and roles via pure virtual methods declared in this header.

## Purpose & Responsibilities

The primary responsibilities of `CombatBotBaseAI` are:

1.  **Character Initialization:** Automatically assigning roles (Tank, Healer, DPS), learning necessary proficiencies, applying premade talent/gear templates, or generating random viable gear and talents if templates are unavailable.
2.  **Spell Management:** Scanning the bot's known spells to populate structured data members (`m_spells`) with pointers to the highest-rank versions of key abilities (e.g., `pFireball`, `pHeal`). It categorizes spells into direct heals, periodic heals, taunts, and resurrection spells.
3.  **Target Selection Logic:** Providing robust helper functions to determine if a unit is a valid target for healing, buffing, dispelling, or attacking, considering factors like line-of-sight, distance, health percentages, and group composition.
4.  **Combat Support Utilities:** Managing pet summoning for Hunters and Warlocks, ensuring Hunters have ammo, casting weapon buffs for Shamans/Rogues, and breaking crowd control effects using class-specific defensive cooldowns or trinkets.
5.  **Network Simulation:** Intercepting server-to-client packets (teleports, trade requests, resurrection offers) and automatically generating the appropriate client-to-server responses to keep the bot synchronized with the game world without human input.

## Member-by-Member Behavior

### Initialization and Role Assignment

**`AutoAssignRole`** determines the bot's primary role (`ROLE_TANK`, `ROLE_HEALER`, `ROLE_MELEE_DPS`, `ROLE_RANGE_DPS`) based on its class and learned spells. It uses specific spell IDs as heuristics:
*   **Warriors:** Tank if they know `SPELL_SHIELD_SLAM`, otherwise Melee DPS.
*   **Paladins:** Tank if they know `SPELL_HOLY_SHIELD`, Melee DPS if they know `SPELL_SANCTITY_AURA`, otherwise Healer.
*   **Priests:** Range DPS if they know `SPELL_SHADOWFORM`, otherwise Healer.
*   **Shamans:** Range DPS if they know `SPELL_ELEMENTAL_MASTERY`, Melee DPS if they know `SPELL_STORMSTRIKE`, otherwise Healer.
*   **Druids:** Range DPS if they know `SPELL_MOONKIN_FORM`, Melee DPS if they know `SPELL_LEADER_OF_THE_PACK`, otherwise Healer.
*   **Rogues/Hunters/Mages/Warlocks:** Fixed as Melee or Range DPS respectively.

**`PopulateSpellData`** iterates through every spell the bot knows (`me->GetSpellMap()`). It filters out passive and hidden spells. For each active spell, it checks the spell name against hardcoded strings to identify key abilities (e.g., "Fireball", "Heal", "Polymorph"). It maintains pointers to the highest rank of each identified spell by comparing spell IDs or rank numbers. It also populates generic lists:
*   `m_spellListDirectHeal`: Spells with `SPELL_EFFECT_HEAL`.
*   `m_spellListPeriodicHeal`: Spells applying `SPELL_AURA_PERIODIC_HEAL`.
*   `m_spellListTaunt`: Spells with `SPELL_EFFECT_ATTACK_ME` or `SPELL_AURA_MOD_TAUNT`.
*   `m_resurrectionSpell`: Spells with `SPELL_EFFECT_RESURRECT`.

After scanning, it performs class-specific finalization:
*   **Paladins:** Selects a Seal, Blessing, and Aura based on role and availability.
*   **Shamans:** Selects Air, Earth, Fire, and Water totems, plus a weapon buff.
*   **Mages:** Selects a Polymorph variant and ensures an Ice/Frost Armor is set.
*   **Rogues:** Identifies available poisons and selects the highest rank suitable for the bot's level.

**`ResetSpellData`** clears all pointers in `m_spells` and empties the healing/taunt/resurrection lists, preparing the bot for a fresh scan.

**`LearnPremadeSpecForClass`** attempts to apply a predefined talent and gear template matching the bot's class, level, and assigned role. If no exact match exists, it falls back to a lower-level template. If no templates exist, it invokes `LearnRandomTalents` and GM commands (`HandleLearnAllTrainerCommand`, `HandleLearnAllItemsCommand`) to force-learn all available spells and items, then assigns random talents.

**`LearnRandomTalents`** fills available talent points by randomly selecting talent tabs and talents within those tabs, respecting prerequisites and row dependencies. It shuffles the order of talents to create varied builds.

**`EquipPremadeGearTemplate`** applies a gear template matching the bot's class, level, and role. If multiple templates exist for the same level, one is selected randomly.

**`EquipRandomGearInEmptySlots`** generates gear for the bot if no premade template is used. It scans the entire item database (`sObjectMgr.GetItemPrototypeMap()`), filtering for:
*   Discoverable, obtainable items.
*   Items matching the bot's race/class restrictions.
*   Items within a reasonable level range.
*   Items the bot can equip (proficiency, stats).
*   Items with the primary stat for the bot's role (Strength for Warriors/Tanks, Agility for Rogues/Hunters, Intellect for Casters).
*   Preferentially selects PvP items if available.
It then equips one random item per empty slot, ensuring off-hand slots respect two-handed weapon usage and tank/shield requirements.

**`AutoEquipGear`** dispatches to either `AddStartingItems`, `EquipRandomGearInEmptySlots`, or `EquipPremadeGearTemplate` based on the provided option, then updates the visual honor rank.

**`LearnArmorProficiencies`** ensures the bot learns Mail Proficiency (level 40+) for Hunters/Shamans and Plate Proficiency (level 40+) for Warriors/Paladins if they don't already have them.

### Target Selection and Validation

**`IsValidHostileTarget`** checks if a unit can be attacked: it must be a valid attack target, visible/detectable, not immune to damage, not under a breakable crowd control effect, and on the same transport as the bot.

**`IsValidHealTarget`** checks if a unit needs and can receive healing: health below threshold, valid helpful target, within line-of-sight, and within 30 yards.

**`SelectHealTarget`** prioritizes self-healing if below `selfHealPercent`. Otherwise, it iterates through group members to find the lowest-health ally who is a valid heal target. It avoids targeting pets if players are injured, and skips targets already being healed by other party members (via `AreOthersOnSameTarget`) unless the target is a tank.

**`SelectPeriodicHealTarget`** similar to `SelectHealTarget`, but specifically looks for targets who do *not* already have a periodic heal aura.

**`FindAndPreHealTarget`** proactively heals targets taking significant incoming melee damage (`GetIncomingdamage`). It calculates the average damage of all attackers in melee range and casts a direct heal if the projected damage plus current missing health exceeds half the target's max health.

**`SelectBuffTarget`** (two overloads):
1.  Single spell: Finds the first group member who is a valid helpful target, not a GM, within LOS/dist, and doesn't already have the buff or a superior version.
2.  Single vs. Group spell: Determines whether to cast a single-target or group buff based on how many members are missing the buff. If more than one member is missing it, it prefers the group buff.

**`SelectDispelTarget`** Finds the first group member who is a valid helpful target, within LOS/dist, and has a dispellable aura compatible with the provided dispel spell.

**`IsValidDispelTarget`** Complex logic to determine if a dispel spell can remove an aura from a target. It checks:
*   Immunity to the dispel's school mask (unless friendly).
*   Whether the aura is dispellable by the spell's dispel type (Magic, Disease, Poison).
*   Whether the aura is positive/negative relative to the target's faction.
*   Special handling for charm auras, checking original faction templates to avoid breaking beneficial charms on hostile mobs.

**`AreOthersOnSameTarget`** Checks if any other group member is currently attacking (melee or casting) the specified GUID. Used to distribute healing/buffing load.

**`GetAttackersInRangeCount`** Counts how many attackers are within a specified range of the bot.

**`SelectAttackerDifferentFrom`** Returns an attacker of the bot that is not the specified unit, useful for switching targets.

### Combat Actions and Spell Casting

**`CanTryToCastSpell`** Validates if a spell can be cast on a target. Checks:
*   Global Cooldown (GCD).
*   Spell readiness.
*   Required aura states on caster/target.
*   Power cost (Health or Mana/Energy/etc.).
*   Target immunity.
*   Shapeshift form restrictions.
*   Existing aura on target (if spell applies an aura).
*   Range constraints.

**`DoCastSpell`** Executes the cast. It faces the target, dismounts if mounted, sets the target, and calls `me->CastSpell`. If the cast fails due to movement, it stops the bot. If it fails due to missing reagents/ammo, it attempts to generate the item via `AddItemToInventory`.

**`HealInjuredTarget`** Attempts to heal a target. If the target is above 80% health and lacks a HoT, it tries `HealInjuredTargetPeriodic`. Otherwise, it tries `HealInjuredTargetDirect`.

**`HealInjuredTargetPeriodic` / `HealInjuredTargetDirect`** Select the most efficient spell from the respective lists (`m_spellListPeriodicHeal` / `m_spellListDirectHeal`) using `SelectMostEfficientHealingSpell` and cast it.

**`SelectMostEfficientHealingSpell`** Iterates through a set of healing spells, calculating total healing potential (base points + periodic ticks). It selects the spell whose total healing value is closest to (but not significantly exceeding) the target's missing health, optimizing resource usage.

**`SummonPetIfNeeded`** Handles pet management for Hunters and Warlocks.
*   **Hunters:** Revives dead pets, calls stable pets, or summons a random tameable creature (Wolf, Cat, Bear, etc.) and tames it if no pet exists.
*   **Warlocks:** Summons a random demon (Imp, Voidwalker, Felhunter, Succubus) if none is active.

**`SummonShamanTotems`** Attempts to cast Air, Earth, Fire, and Water totems if the corresponding slot is empty and the spell is ready.

**`CastWeaponBuff`** Applies a weapon enchantment (e.g., Windfury, Rockbiter) to a specified equipment slot, checking for existing temporary enchants.

**`UseTrinketEffects`** Activates trinket effects, optionally filtering for only those that break crowd control (`onlyToBreakCC`).

**`UseItemEffect`** Iterates through an item's spell triggers, casting positive spells on self or negative spells on the current victim, skipping transformation effects.

**`BreakCrowdControlEffects`** Class-specific logic to escape CC:
*   **Paladins:** Cast Divine Shield.
*   **Mages:** Blink if stunned, or Ice Block.
*   **Druids:** Shift out of Polymorph by entering a form appropriate for their role (Bear, Cat, Moonkin).
It schedules a lambda event to cancel these defensive auras after 1 second if health is high and threat is low.

**`AddHunterAmmo`** Ensures the Hunter has the highest-level usable ammo compatible with their equipped ranged weapon.

**`AddAllSpellReagents`** Iterates through all populated spells and adds any missing reagents or totems to the bot's inventory.

**`AddItemToInventory`** Stores a new item in the bot's bag, generating random properties if applicable.

**`EquipOrUseNewItem`** Processes newly acquired items: consumes consumables, equips weapons/armor (learning proficiencies if needed), and destroys old items in conflicting slots.

### Network and Utility

**`OnPacketReceived`** Handles server packets to simulate client behavior:
*   `SMSG_NEW_WORLD` / `MSG_MOVE_TELEPORT_ACK`: Sends acknowledgment packets for teleportation.
*   `SMSG_LOGIN_SETTIMESPEED`: Updates visual honor rank.
*   `SMSG_TRADE_STATUS`: Accepts trades, completes trades, and equips new loot.
*   `SMSG_RESURRECT_REQUEST`: Accepts resurrection.
*   `SMSG_BATTLEFIELD_STATUS`: Tracks battleground invites.
*   `SMSG_LOOT_START_ROLL`: Passes on loot rolls.

**`SendBattlefieldPortPacket`** Simulates clicking the "Port to Battleground" button for queued battlegrounds.

**`SendBattlemasterJoinPacket`** Queues the bot for a specific battleground (AV, WS, AB).

**`SendAreaTriggerPacket` / `ActivateNearbyAreaTrigger`** Detects and activates area triggers near the bot's position, useful for quest triggers or zone events.

**`GetRole`** Returns the current role, overriding Healer to DPS if in a duel.

**`IsInDuel`** Checks if the bot is currently in a duel.

**`GetIncomingdamage`** Calculates the average melee damage per second from all attackers in melee range.

**`GetHighestHonorRankFromEquippedItems` / `UpdateVisualHonorRankBasedOnItems`** Updates the bot's displayed honor rank based on the highest honor requirement of equipped items, ensuring visual consistency.

### Static Class Helpers

The following static methods provide quick classification of player classes:
*   `IsPhysicalDamageClass`: Warrior, Paladin, Rogue, Hunter, Shaman, Druid.
*   `IsRangedDamageClass`: Hunter, Priest, Shaman, Mage, Warlock, Druid.
*   `IsMeleeDamageClass`: Warrior, Paladin, Rogue, Shaman, Druid.
*   `IsMeleeWeaponClass`: Warrior, Paladin, Rogue, Shaman.
*   `IsShieldClass`: Warrior, Paladin, Shaman.
*   `IsTankClass`: Warrior, Paladin, Druid.
*   `IsHealerClass`: Paladin, Priest, Shaman, Druid.
*   `IsStealthClass`: Rogue, Druid.

**`GetCrowdControlSpell`** Returns the primary CC spell for the bot's class (e.g., Hammer of Justice for Paladin, Polymorph for Mage).

## Cross-Unit Boundaries

`CombatBotBaseAI` acts as a utility provider for three main AI subclasses: `AiBotAI`, `BattleBotAI`, and `PartyBotAI`.

*   **Called by `AiBotAI.Main/UpdateAI`**: Invokes `AutoAssignRole`, `ResetSpellData`, `PopulateSpellData`, `AddAllSpellReagents`, `SummonPetIfNeeded`, `LearnPremadeSpecForClass`, `AutoEquipGear`, and `BreakCrowdControlEffects` during initialization and combat updates.
*   **Called by `BattleBotAI.Main/UpdateAI`**: Similar to `AiBotAI`, uses these methods for setup and maintenance. Also calls `SendBattlefieldPortPacket` and `SendBattlemasterJoinPacket` for battleground participation.
*   **Called by `PartyBotAI/UpdateAI`**: Uses the same initialization and utility methods. Specifically relies on `FindAndHealInjuredAlly`, `HealInjuredTarget`, `SelectHealTarget`, `SelectBuffTarget`, `SelectDispelTarget`, and `IsValidDispelTarget` for party support behaviors.
*   **Called by `ChatHandler.PlayerBotMgr`**: Commands like `HandlePartyBotSetRoleCommand` trigger `ResetSpellData` and `PopulateSpellData` to refresh the bot's capabilities after a role change.

## Data Model

This unit does not directly interact with database tables. It relies on in-memory data structures (`SpellEntry`, `ItemPrototype`, `PlayerPremadeSpecTemplate`, `PlayerPremadeGearTemplate`) managed by `ObjectMgr` and `SpellMgr`. The `CharacterDatabaseCache` is accessed indirectly via `SummonPetIfNeeded` to check for existing stable pets, but no direct SQL queries are executed in this unit.

## Notable Implementation Details

*   **String-Based Spell Identification:** `PopulateSpellData` relies heavily on `std::string::find` against `SpellName[0]`. This is fragile; if spell names change between patches or locales, the bot may fail to identify key abilities.
*   **Hardcoded Spell IDs:** Many critical behaviors depend on hardcoded spell IDs (e.g., `SPELL_SHIELD_SLAM`, `SPELL_SUMMON_IMP`). These must be updated if the game client version changes.
*   **Random Gear Generation:** `EquipRandomGearInEmptySlots` scans the *entire* item prototype map. This is computationally expensive and should be cached or limited in scope. It prioritizes items with the "primary stat" for the role, which is a simple but effective heuristic.
*   **Healing Efficiency:** `SelectMostEfficientHealingSpell` attempts to minimize over-healing by choosing the spell closest to the missing health. However, it does not account for healing amplification debuffs (like Mortal Strike) or target resistances.
*   **CC Breaking Logic:** `BreakCrowdControlEffects` uses lambda events scheduled for 1ms later to cancel defensive auras. This is a workaround to ensure the aura is applied before cancellation logic runs, but it relies on the event system processing these events promptly.
*   **Pet Summoning for Hunters:** If a Hunter has no pet, it summons a random creature from a hardcoded list and tames it. This bypasses normal taming mechanics and may result in invalid pet states if the creature cannot be tamed by the bot's level.

## Member Reference

**AutoAssignRole**
Determines the bot's role (Tank, Healer, DPS) based on class and learned spells. Sets `m_role`.

**CombatBotBaseAI**
Constructor initializes `m_spells` pointers to nullptr.

**ResetSpellData**
Clears all spell pointers and healing/taunt/resurrection lists.

**PopulateSpellData**
Scans known spells, identifies key abilities by name, populates `m_spells` structs with highest-rank pointers, and categorizes heals/taunts/resurrections.

**UpdateInCombatAI**
Pure virtual declaration. Implemented by subclasses.

**UpdateOutOfCombatAI**
Pure virtual declaration. Implemented by subclasses.

**UpdateInCombatAI_Paladin**
Pure virtual declaration. Implemented by subclasses.

**UpdateOutOfCombatAI_Paladin**
Pure virtual declaration. Implemented by subclasses.

**UpdateInCombatAI_Shaman**
Pure virtual declaration. Implemented by subclasses.

**UpdateOutOfCombatAI_Shaman**
Pure virtual declaration. Implemented by subclasses.

**UpdateInCombatAI_Hunter**
Pure virtual declaration. Implemented by subclasses.

**UpdateOutOfCombatAI_Hunter**
Pure virtual declaration. Implemented by subclasses.

**UpdateInCombatAI_Mage**
Pure virtual declaration. Implemented by subclasses.

**UpdateOutOfCombatAI_Mage**
Pure virtual declaration. Implemented by subclasses.

**UpdateInCombatAI_Priest**
Pure virtual declaration. Implemented by subclasses.

**UpdateOutOfCombatAI_Priest**
Pure virtual declaration. Implemented by subclasses.

**UpdateInCombatAI_Warlock**
Pure virtual declaration. Implemented by subclasses.

**UpdateOutOfCombatAI_Warlock**
Pure virtual declaration. Implemented by subclasses.

**UpdateInCombatAI_Warrior**
Pure virtual declaration. Implemented by subclasses.

**UpdateOutOfCombatAI_Warrior**
Pure virtual declaration. Implemented by subclasses.

**UpdateInCombatAI_Rogue**
Pure virtual declaration. Implemented by subclasses.

**UpdateOutOfCombatAI_Rogue**
Pure virtual declaration. Implemented by subclasses.

**UpdateInCombatAI_Druid**
Pure virtual declaration. Implemented by subclasses.

**UpdateOutOfCombatAI_Druid**
Pure virtual declaration. Implemented by subclasses.

**IsPhysicalDamageClass**
Static helper returning true for Warrior, Paladin, Rogue, Hunter, Shaman, Druid.

**IsRangedDamageClass**
Static helper returning true for Hunter, Priest, Shaman, Mage, Warlock, Druid.

**IsMeleeDamageClass**
Static helper returning true for Warrior, Paladin, Rogue, Shaman, Druid.

**IsMeleeWeaponClass**
Static helper returning true for Warrior, Paladin, Rogue, Shaman.

**IsShieldClass**
Static helper returning true for Warrior, Paladin, Shaman.

**IsTankClass**
Static helper returning true for Warrior, Paladin, Druid.

**IsHealerClass**
Static helper returning true for Paladin, Priest, Shaman, Druid.

**IsStealthClass**
Static helper returning true for Rogue, Druid.

**GetCrowdControlSpell**
Returns the primary CC spell pointer for the bot's class.

**AddAllSpellReagents**
Adds missing reagents/totems for all populated spells to inventory.

**AreOthersOnSameTarget**
Checks if other group members are attacking/casting on a specific GUID.

**FindAndHealInjuredAlly**
Selects a heal target and attempts to heal them.

**GetIncomingdamage**
Calculates average melee DPS from attackers in range.

**HealInjuredTarget**
Attempts to heal a target, preferring HoTs for high-health targets.

**HealInjuredTargetPeriodic**
Casts the most efficient periodic heal on a target.

**HealInjuredTargetDirect**
Casts the most efficient direct heal on a target.

**IsValidHealTarget**
Checks if a unit is a valid, needy heal target within range/LOS.

**SelectHealTarget**
Selects the best heal target from self/group, avoiding duplicates.

**SelectPeriodicHealTarget**
Selects a target lacking a HoT.

**FindAndPreHealTarget**
Proactively heals targets taking high incoming melee damage.

**IsValidHostileTarget**
Checks if a unit is a valid attack target.

**IsValidDispelTarget**
Complex check for dispellable auras on a target, considering faction and charm status.

**GetAttackersInRangeCount**
Counts attackers within a range.

**SelectAttackerDifferentFrom**
Returns an attacker other than the specified unit.

**IsValidBuffTarget**
Checks if a target lacks a buff or superior version.

**SelectBuffTarget**
Selects a group member for a buff, choosing between single/group cast based on need.

**SelectBuffTarget#2**
Overload for single/group buff selection logic.

**SelectDispelTarget**
Selects a group member needing a dispel.

**SummonPetIfNeeded**
Manages pet summoning/revival for Hunters and Warlocks.

**LearnArmorProficiencies**
Ensures Mail/Plate proficiency is learned at level 40+.

**LearnPremadeSpecForClass**
Applies premade talent/gear templates or generates random ones.

**LearnRandomTalents**
Fills talent points randomly.

**EquipPremadeGearTemplate**
Applies a premade gear template.

**GetPrimaryItemStatForClassAndRole**
Static function returning the primary stat (Str/Agi/Int) for a class/role.

**EquipRandomGearInEmptySlots**
Generates and equips random viable gear for empty slots.

**AutoEquipGear**
Dispatches to starting/random/premade gear equipping.

**CanTryToCastSpell**
Validates spell cast conditions (GCD, power, range, immunity, etc.).

**DoCastSpell**
Executes a spell cast, handling facing, mounting, and reagent generation.

**AddItemToInventory**
Stores an item in the bot's bag.

**AddHunterAmmo**
Ensures Hunter has appropriate ammo.

**EquipOrUseNewItem**
Processes new items: consume, equip, or discard.

**GetHighestHonorRankFromEquippedItems**
Finds the highest honor rank required by equipped items.

**UpdateVisualHonorRankBasedOnItems**
Updates the bot's visual honor rank byte fields.

**SummonShamanTotems**
Attempts to cast all four totem types if slots are empty.

**CastWeaponBuff**
Applies a weapon enchantment to a slot.

**UseTrinketEffects**
Activates trinket effects, optionally filtering for CC breaks.

**UseItemEffect**
Casts item-triggered spells.

**BreakCrowdControlEffects**
Uses class-specific abilities or trinkets to break CC.

**IsWearingShield**
Checks if a player has a shield equipped.

**IsInDuel**
Checks if the bot is in a duel.

**GetRole**
Returns the current role, adjusting for duels.

**SendBattlefieldPortPacket**
Simulates porting to a queued battleground.

**SendBattlemasterJoinPacket**
Queues the bot for a battleground.

**SendAreaTriggerPacket**
Sends an area trigger activation packet.

**ActivateNearbyAreaTrigger**
Detects and activates nearby area triggers.

**OnPacketReceived**
Handles server packets to simulate client responses (teleport, trade, resurrect, etc.).

---

<!-- machine-true, projected from graph.json -->

## Map — CombatBotBaseAI

*Source:* CombatBotBaseAI.cpp, CombatBotBaseAI.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AutoAssignRole | method | Player.Main/HasSpell, Unit.Main/GetClass | AiBotAI.Main/UpdateAI, BattleBotAI.Main/UpdateAI, PartyBotAI/UpdateAI | — |
| CombatBotBaseAI | ctor | — | — | — |
| ResetSpellData | method | — | AiBotAI.Bridge/BridgeHandleTrain, AiBotAI.Main/UpdateAI, BattleBotAI.Main/UpdateAI, ChatHandler.PlayerBotMgr/HandlePartyBotSetRoleCommand, PartyBotAI/UpdateAI | — |
| PopulateSpellData | method | Player.Main/GetSpellMap, SpellEntry/GetRank, SpellEntry/HasAttribute, SpellMgr/GetMaxSpellId, SpellMgr/GetSpellEntry, SpellMgr/Instance, Unit.Main/GetClass, Unit.Main/GetLevel | AiBotAI.Bridge/BridgeHandleTrain, AiBotAI.Main/UpdateAI, BattleBotAI.Main/UpdateAI, ChatHandler.PlayerBotMgr/HandlePartyBotSetRoleCommand, PartyBotAI/UpdateAI | — |
| UpdateInCombatAI | decl | — | — | — |
| UpdateOutOfCombatAI | decl | — | — | — |
| UpdateInCombatAI_Paladin | decl | — | — | — |
| UpdateOutOfCombatAI_Paladin | decl | — | — | — |
| UpdateInCombatAI_Shaman | decl | — | — | — |
| UpdateOutOfCombatAI_Shaman | decl | — | — | — |
| UpdateInCombatAI_Hunter | decl | — | — | — |
| UpdateOutOfCombatAI_Hunter | decl | — | — | — |
| UpdateInCombatAI_Mage | decl | — | — | — |
| UpdateOutOfCombatAI_Mage | decl | — | — | — |
| UpdateInCombatAI_Priest | decl | — | — | — |
| UpdateOutOfCombatAI_Priest | decl | — | — | — |
| UpdateInCombatAI_Warlock | decl | — | — | — |
| UpdateOutOfCombatAI_Warlock | decl | — | — | — |
| UpdateInCombatAI_Warrior | decl | — | — | — |
| UpdateOutOfCombatAI_Warrior | decl | — | — | — |
| UpdateInCombatAI_Rogue | decl | — | — | — |
| UpdateOutOfCombatAI_Rogue | decl | — | — | — |
| UpdateInCombatAI_Druid | decl | — | — | — |
| UpdateOutOfCombatAI_Druid | decl | — | — | — |
| IsPhysicalDamageClass | method | — | AiBotAI.Combat/UpdateInCombatAI_Druid, AiBotAI.Combat/UpdateInCombatAI_Mage, AiBotAI.Combat/UpdateInCombatAI_Paladin, AiBotAI.Combat/UpdateInCombatAI_Warrior, BattleBotAI.Main/UpdateInCombatAI_Druid, BattleBotAI.Main/UpdateInCombatAI_Mage, BattleBotAI.Main/UpdateInCombatAI_Paladin, BattleBotAI.Main/UpdateInCombatAI_Warrior, PartyBotAI/UpdateInCombatAI_Paladin | — |
| IsRangedDamageClass | method | — | AiBotAI.Combat/AttackStart, AiBotAI.Combat/UpdateInCombatAI_Rogue, BattleBotAI.Main/AttackStart, BattleBotAI.Main/UpdateInCombatAI_Rogue, PartyBotAI/UpdateInCombatAI_Rogue | — |
| IsMeleeDamageClass | method | — | AiBotAI.Combat/UpdateInCombatAI_Druid, AiBotAI.Combat/UpdateInCombatAI_Paladin, AiBotAI.Combat/UpdateInCombatAI_Warrior, BattleBotAI.Main/UpdateInCombatAI_Druid, BattleBotAI.Main/UpdateInCombatAI_Paladin, BattleBotAI.Main/UpdateInCombatAI_Warrior, ChatHandler.PlayerBotMgr/HandlePartyBotAddCommand, ChatHandler.PlayerBotMgr/HandlePartyBotSetRoleCommand | — |
| IsMeleeWeaponClass | method | — | AiBotAI.Combat/UpdateInCombatAI_Warrior, BattleBotAI.Main/UpdateInCombatAI_Warrior, PartyBotAI/UpdateInCombatAI_Warrior | — |
| IsShieldClass | method | — | — | — |
| IsTankClass | method | — | — | — |
| IsHealerClass | method | — | BattleBotAI.Main/SelectFollowTarget, PartyBotAI/ShouldAutoRevive | — |
| IsStealthClass | method | — | BattleBotAI.Main/SelectFollowTarget | — |
| GetCrowdControlSpell | method | — | PartyBotAI/CrowdControlMarkedTargets | — |
| AddAllSpellReagents | method | Player.Main/HasItemCount | AiBotAI.Main/UpdateAI, BattleBotAI.Main/UpdateAI, PartyBotAI/UpdateAI | — |
| AreOthersOnSameTarget | method | Group/GetFirstMember, GroupReference/next, Object/GetObjectGuid, ObjectGuid/operator==, Player.Main/GetGroup, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/GetTargetGuid, Unit.Main/HasUnitState | PartyBotAI/CanUseCrowdControl, PartyBotAI/CrowdControlMarkedTargets | — |
| FindAndHealInjuredAlly | method | — | AiBotAI.Combat/UpdateInCombatAI_Druid, AiBotAI.Combat/UpdateInCombatAI_Paladin, AiBotAI.Combat/UpdateInCombatAI_Priest, AiBotAI.Combat/UpdateInCombatAI_Shaman, AiBotAI.Combat/UpdateOutOfCombatAI_Druid, AiBotAI.Combat/UpdateOutOfCombatAI_Paladin, BattleBotAI.Main/UpdateInCombatAI_Druid, BattleBotAI.Main/UpdateInCombatAI_Paladin, BattleBotAI.Main/UpdateInCombatAI_Priest, BattleBotAI.Main/UpdateInCombatAI_Shaman, BattleBotAI.Main/UpdateOutOfCombatAI_Druid, BattleBotAI.Main/UpdateOutOfCombatAI_Paladin, PartyBotAI/UpdateInCombatAI_Paladin, PartyBotAI/UpdateInCombatAI_Shaman, PartyBotAI/UpdateOutOfCombatAI_Druid, PartyBotAI/UpdateOutOfCombatAI_Paladin, PartyBotAI/UpdateOutOfCombatAI_Priest, PartyBotAI/UpdateOutOfCombatAI_Shaman | — |
| GetIncomingdamage | method | Object/GetFloatValue, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/GetAttackers | — | — |
| HealInjuredTarget | method | Unit.Main/GetHealthPercent, Unit.Main/HasAuraType | PartyBotAI/UpdateInCombatAI_Paladin, PartyBotAI/UpdateInCombatAI_Shaman | — |
| HealInjuredTargetPeriodic | method | — | PartyBotAI/UpdateInCombatAI_Druid, PartyBotAI/UpdateInCombatAI_Priest | — |
| HealInjuredTargetDirect | method | — | PartyBotAI/UpdateInCombatAI_Druid, PartyBotAI/UpdateInCombatAI_Priest | — |
| IsValidHealTarget | method | Unit.Main/GetHealthPercent, WorldObject.Object/IsValidHelpfulTarget, WorldObject.Object/IsWithinDist, WorldObject.Object/IsWithinLOSInMap | — | — |
| SelectHealTarget | method | Group/GetFirstMember, GroupReference/next, Object/GetObjectGuid, Player.Main/GetGroup, Unit.Main/GetClass, Unit.Main/GetHealthPercent, Unit.Main/GetPet | PartyBotAI/UpdateInCombatAI_Druid, PartyBotAI/UpdateInCombatAI_Priest | — |
| SelectPeriodicHealTarget | method | Group/GetFirstMember, GroupReference/next, Player.Main/GetGroup, Unit.Main/GetHealthPercent, Unit.Main/HasAuraType | PartyBotAI/UpdateInCombatAI_Druid, PartyBotAI/UpdateInCombatAI_Priest | — |
| FindAndPreHealTarget | method | Group/GetFirstMember, GroupReference/next, Object/GetObjectGuid, Player.Main/GetGroup, SpellEntry/GetCastTime, Unit.Main/GetClass, Unit.Main/GetHealth, Unit.Main/GetMaxHealth | PartyBotAI/UpdateInCombatAI_Druid, PartyBotAI/UpdateInCombatAI_Paladin, PartyBotAI/UpdateInCombatAI_Priest, PartyBotAI/UpdateInCombatAI_Shaman | — |
| IsValidHostileTarget | method | Unit.Main/HasBreakableByDamageCrowdControlAura, Unit.Main/IsTotalImmune, Unit.Main/IsVisibleForOrDetect, WorldObject.Object/GetTransport, WorldObject.Object/IsValidAttackTarget | AiBotAI.Bridge/BridgeHandleAttackTarget, AiBotAI.Combat/IsValidAssistTarget, AiBotAI.Combat/SelectAttackTarget, AiBotAI.Grind/CountNearbyHostiles, AiBotAI.Grind/ScanApproachTarget, AiBotAI.Grind/SelectGrindTarget, AiBotAI.Main/UpdateAI, BattleBotAI.Main/SelectAttackTarget, BattleBotAI.Main/UpdateAI, PartyBotAI/CrowdControlMarkedTargets, PartyBotAI/SelectAttackTarget, PartyBotAI/SelectPartyAttackTarget, PartyBotAI/UpdateAI | — |
| IsValidDispelTarget | method | CharmInfo/GetOriginalFactionTemplate, FactionTemplateEntry/IsFriendlyTo, SpellAuraHolder/GetSpellProto, SpellEntry/GetDispellMask, SpellEntry/GetSpellSchoolMask, SpellEntry/IsCharmSpell, Unit.Main/GetCharmInfo, Unit.Main/GetSpellAuraHolderMap#2, Unit.Main/IsFriendlyTo, Unit.Main/IsImmuneToSchoolMask, Unit.SpellAuras/IsPositive, WorldObject.Object/GetFactionTemplateEntry, WorldObject.Object/IsValidAttackTarget | AiBotAI.Combat/UpdateInCombatAI_Druid, AiBotAI.Combat/UpdateInCombatAI_Mage, AiBotAI.Combat/UpdateInCombatAI_Priest, AiBotAI.Combat/UpdateInCombatAI_Shaman, BattleBotAI.Main/UpdateInCombatAI_Druid, BattleBotAI.Main/UpdateInCombatAI_Mage, BattleBotAI.Main/UpdateInCombatAI_Priest, BattleBotAI.Main/UpdateInCombatAI_Shaman, PartyBotAI/UpdateInCombatAI_Shaman | — |
| GetAttackersInRangeCount | method | Unit.Main/GetAttackers, WorldObject.Object/GetCombatDistance | AiBotAI.Combat/UpdateInCombatAI_Druid, AiBotAI.Combat/UpdateInCombatAI_Mage, AiBotAI.Combat/UpdateInCombatAI_Paladin, AiBotAI.Combat/UpdateInCombatAI_Priest, AiBotAI.Combat/UpdateInCombatAI_Rogue, AiBotAI.Combat/UpdateInCombatAI_Warlock, AiBotAI.Combat/UpdateInCombatAI_Warrior, BattleBotAI.Main/UpdateInCombatAI_Druid, BattleBotAI.Main/UpdateInCombatAI_Mage, BattleBotAI.Main/UpdateInCombatAI_Paladin, BattleBotAI.Main/UpdateInCombatAI_Priest, BattleBotAI.Main/UpdateInCombatAI_Rogue, BattleBotAI.Main/UpdateInCombatAI_Warlock, BattleBotAI.Main/UpdateInCombatAI_Warrior, PartyBotAI/UpdateInCombatAI_Druid, PartyBotAI/UpdateInCombatAI_Hunter, PartyBotAI/UpdateInCombatAI_Mage, PartyBotAI/UpdateInCombatAI_Paladin, PartyBotAI/UpdateInCombatAI_Priest, PartyBotAI/UpdateInCombatAI_Rogue, PartyBotAI/UpdateInCombatAI_Warlock, PartyBotAI/UpdateInCombatAI_Warrior | — |
| SelectAttackerDifferentFrom | method | Unit.Main/GetAttackers | AiBotAI.Combat/UpdateInCombatAI_Mage, AiBotAI.Combat/UpdateInCombatAI_Rogue, BattleBotAI.Main/UpdateInCombatAI_Mage, BattleBotAI.Main/UpdateInCombatAI_Rogue, PartyBotAI/UpdateInCombatAI_Mage, PartyBotAI/UpdateInCombatAI_Paladin, PartyBotAI/UpdateInCombatAI_Rogue | — |
| IsValidBuffTarget | method | SpellMgr/Instance, SpellMgr/IsRankSpellDueToSpell, SpellMgr/ListMorePowerfulSpells, Unit.Main/GetSpellAuraHolderMap#2 | AiBotAI.Combat/UpdateOutOfCombatAI_Priest, BattleBotAI.Main/UpdateOutOfCombatAI_Priest | — |
| SelectBuffTarget | method | Group/GetFirstMember, GroupReference/next, Player.Main/GetGroup, Player.Main/IsGameMaster, WorldObject.Object/IsValidHelpfulTarget, WorldObject.Object/IsWithinDist, WorldObject.Object/IsWithinLOSInMap | AiBotAI.Combat/UpdateOutOfCombatAI_Druid, AiBotAI.Combat/UpdateOutOfCombatAI_Paladin, AiBotAI.Combat/UpdateOutOfCombatAI_Priest, AiBotAI.Combat/UpdateOutOfCombatAI_Warlock, BattleBotAI.Main/UpdateOutOfCombatAI_Druid, BattleBotAI.Main/UpdateOutOfCombatAI_Paladin, BattleBotAI.Main/UpdateOutOfCombatAI_Priest, BattleBotAI.Main/UpdateOutOfCombatAI_Warlock, PartyBotAI/UpdateOutOfCombatAI_Druid, PartyBotAI/UpdateOutOfCombatAI_Paladin, PartyBotAI/UpdateOutOfCombatAI_Warlock | — |
| SelectBuffTarget#2 | method | Group/GetFirstMember, GroupReference/next, Player.Main/GetGroup, Player.Main/IsGameMaster, WorldObject.Object/IsValidHelpfulTarget, WorldObject.Object/IsWithinDist, WorldObject.Object/IsWithinLOSInMap | PartyBotAI/UpdateOutOfCombatAI_Druid, PartyBotAI/UpdateOutOfCombatAI_Mage, PartyBotAI/UpdateOutOfCombatAI_Priest | — |
| SelectDispelTarget | method | Group/GetFirstMember, GroupReference/next, Player.Main/GetGroup, Player.Main/IsGameMaster, WorldObject.Object/IsValidHelpfulTarget, WorldObject.Object/IsWithinDist, WorldObject.Object/IsWithinLOSInMap | AiBotAI.Combat/UpdateInCombatAI_Druid, AiBotAI.Combat/UpdateInCombatAI_Paladin, AiBotAI.Combat/UpdateInCombatAI_Priest, BattleBotAI.Main/UpdateInCombatAI_Druid, BattleBotAI.Main/UpdateInCombatAI_Paladin, BattleBotAI.Main/UpdateInCombatAI_Priest, PartyBotAI/CheckForDispelTargets | — |
| SummonPetIfNeeded | method | CharacterDatabaseCache/GetCharacterPetByOwner, CharacterDatabaseCache/instance, Object/GetGUIDLow, Player.Main/HasSpell, SpellCaster/CastSpell#2, Unit.Main/GetCharmGuid, Unit.Main/GetClass, Unit.Main/GetLevel, Unit.Main/GetPet, Unit.Main/GetPetGuid, Unit.Main/IsAlive, Unit.Main/SetLevel, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SummonCreature#2 | AiBotAI.Combat/UpdateOutOfCombatAI_Warlock, AiBotAI.Main/UpdateAI, BattleBotAI.Main/OnEnterBattleGround, BattleBotAI.Main/OnJustRevived, BattleBotAI.Main/UpdateAI, BattleBotAI.Main/UpdateOutOfCombatAI_Warlock, PartyBotAI/UpdateAI, PartyBotAI/UpdateOutOfCombatAI_Hunter, PartyBotAI/UpdateOutOfCombatAI_Warlock | — |
| LearnArmorProficiencies | method | Player.Main/HasSpell, Player.Main/LearnSpell, Unit.Main/GetClass, Unit.Main/GetLevel | — | — |
| LearnPremadeSpecForClass | method | ChatHandler.CharacterCommands/HandleLearnAllItemsCommand, ChatHandler.CharacterCommands/HandleLearnAllTrainerCommand, ChatHandler.Chat/ChatHandler#2, ObjectMgr/ApplyPremadeSpecTemplateToPlayer, ObjectMgr/GetPlayerPremadeSpecTemplates, Unit.Main/GetClass, Unit.Main/GetLevel | AiBotAI.Main/UpdateAI, BattleBotAI.Main/UpdateAI, PartyBotAI/UpdateAI | — |
| LearnRandomTalents | method | Player.Main/GetFreeTalentPoints, Player.Main/LearnTalent, Unit.Main/GetClassMask | — | — |
| EquipPremadeGearTemplate | method | ObjectMgr/ApplyPremadeGearTemplateToPlayer, ObjectMgr/GetPlayerPremadeGearTemplates, Unit.Main/GetClass, Unit.Main/GetLevel | — | — |
| GetPrimaryItemStatForClassAndRole | function | — | — | — |
| EquipRandomGearInEmptySlots | method | game_Objects_Item/GetAllowedEquipSlots, game_Objects_Item/GetProficiencySkill, game_Objects_Item/GetProto, ItemPrototype/HasExtraFlag, ObjectMgr/GetItemPrototypeMap, Player.Main/CanDualWield, Player.Main/CanUseItem#2, Player.Main/GetHighestKnownArmorProficiency, Player.Main/GetItemByPos, Player.Main/GetReputationRank, Player.Main/GetSkillValue, Player.Main/SatisfyItemRequirements, Player.Main/StoreNewItemInBestSlots, shared_Util/urand, Unit.Main/GetClass, Unit.Main/GetClassMask, Unit.Main/GetLevel, Unit.Main/GetRaceMask, World/getConfig#4 | — | — |
| AutoEquipGear | method | Player.Main/AddStartingItems | AiBotAI.Main/UpdateAI, BattleBotAI.Main/UpdateAI, PartyBotAI/UpdateAI | — |
| CanTryToCastSpell | method | Spell.Main/CalculatePowerCost, SpellCaster/HasGCD, SpellCaster/IsSpellReady#2, SpellEntry/GetErrorAtShapeshiftedCast, SpellEntry/IsSpellAppliesAura, Unit.Main/GetHealth, Unit.Main/GetPower, Unit.Main/GetShapeshiftForm, Unit.Main/HasAura#2, Unit.Main/HasAuraState, Unit.Main/IsImmuneToSpell, WorldObject.Object/GetCombatDistance | AiBotAI.Combat/UpdateInCombatAI_Druid, AiBotAI.Combat/UpdateInCombatAI_Hunter, AiBotAI.Combat/UpdateInCombatAI_Mage, AiBotAI.Combat/UpdateInCombatAI_Paladin, AiBotAI.Combat/UpdateInCombatAI_Priest, AiBotAI.Combat/UpdateInCombatAI_Rogue, AiBotAI.Combat/UpdateInCombatAI_Shaman, AiBotAI.Combat/UpdateInCombatAI_Warlock, AiBotAI.Combat/UpdateInCombatAI_Warrior, AiBotAI.Combat/UpdateOutOfCombatAI_Druid, AiBotAI.Combat/UpdateOutOfCombatAI_Hunter, AiBotAI.Combat/UpdateOutOfCombatAI_Mage, AiBotAI.Combat/UpdateOutOfCombatAI_Paladin, AiBotAI.Combat/UpdateOutOfCombatAI_Priest, AiBotAI.Combat/UpdateOutOfCombatAI_Rogue, AiBotAI.Combat/UpdateOutOfCombatAI_Shaman, AiBotAI.Combat/UpdateOutOfCombatAI_Warlock, AiBotAI.Combat/UpdateOutOfCombatAI_Warrior, BattleBotAI.Main/UpdateFlagCarrierAI, BattleBotAI.Main/UpdateInCombatAI_Druid, BattleBotAI.Main/UpdateInCombatAI_Hunter, BattleBotAI.Main/UpdateInCombatAI_Mage, BattleBotAI.Main/UpdateInCombatAI_Paladin, BattleBotAI.Main/UpdateInCombatAI_Priest, BattleBotAI.Main/UpdateInCombatAI_Rogue, BattleBotAI.Main/UpdateInCombatAI_Shaman, BattleBotAI.Main/UpdateInCombatAI_Warlock, BattleBotAI.Main/UpdateInCombatAI_Warrior, BattleBotAI.Main/UpdateOutOfCombatAI_Druid, BattleBotAI.Main/UpdateOutOfCombatAI_Hunter, BattleBotAI.Main/UpdateOutOfCombatAI_Mage, BattleBotAI.Main/UpdateOutOfCombatAI_Paladin, BattleBotAI.Main/UpdateOutOfCombatAI_Priest, BattleBotAI.Main/UpdateOutOfCombatAI_Rogue, BattleBotAI.Main/UpdateOutOfCombatAI_Shaman, BattleBotAI.Main/UpdateOutOfCombatAI_Warlock, BattleBotAI.Main/UpdateOutOfCombatAI_Warrior, PartyBotAI/CanTryToCastSpell | — |
| DoCastSpell | method | Object/GetObjectGuid, Player.Main/DestroyItem, Player.Main/GetItemByPos, SpellCaster/CastSpell, SpellEntry/GetCastTime, Unit.Main/IsMounted, Unit.Main/IsStopped, Unit.Main/RemoveSpellsCausingAura, Unit.Main/SetFacingToObject, Unit.Main/SetTargetGuid, Unit.Main/StopMoving, WorldObject.Object/IsMoving | AiBotAI.Combat/UpdateInCombatAI_Druid, AiBotAI.Combat/UpdateInCombatAI_Hunter, AiBotAI.Combat/UpdateInCombatAI_Mage, AiBotAI.Combat/UpdateInCombatAI_Paladin, AiBotAI.Combat/UpdateInCombatAI_Priest, AiBotAI.Combat/UpdateInCombatAI_Rogue, AiBotAI.Combat/UpdateInCombatAI_Shaman, AiBotAI.Combat/UpdateInCombatAI_Warlock, AiBotAI.Combat/UpdateInCombatAI_Warrior, AiBotAI.Combat/UpdateOutOfCombatAI_Druid, AiBotAI.Combat/UpdateOutOfCombatAI_Hunter, AiBotAI.Combat/UpdateOutOfCombatAI_Mage, AiBotAI.Combat/UpdateOutOfCombatAI_Paladin, AiBotAI.Combat/UpdateOutOfCombatAI_Priest, AiBotAI.Combat/UpdateOutOfCombatAI_Rogue, AiBotAI.Combat/UpdateOutOfCombatAI_Shaman, AiBotAI.Combat/UpdateOutOfCombatAI_Warlock, AiBotAI.Combat/UpdateOutOfCombatAI_Warrior, BattleBotAI.Main/UpdateInCombatAI_Druid, BattleBotAI.Main/UpdateInCombatAI_Hunter, BattleBotAI.Main/UpdateInCombatAI_Mage, BattleBotAI.Main/UpdateInCombatAI_Paladin, BattleBotAI.Main/UpdateInCombatAI_Priest, BattleBotAI.Main/UpdateInCombatAI_Rogue, BattleBotAI.Main/UpdateInCombatAI_Shaman, BattleBotAI.Main/UpdateInCombatAI_Warlock, BattleBotAI.Main/UpdateInCombatAI_Warrior, BattleBotAI.Main/UpdateOutOfCombatAI_Druid, BattleBotAI.Main/UpdateOutOfCombatAI_Hunter, BattleBotAI.Main/UpdateOutOfCombatAI_Mage, BattleBotAI.Main/UpdateOutOfCombatAI_Paladin, BattleBotAI.Main/UpdateOutOfCombatAI_Priest, BattleBotAI.Main/UpdateOutOfCombatAI_Rogue, BattleBotAI.Main/UpdateOutOfCombatAI_Shaman, BattleBotAI.Main/UpdateOutOfCombatAI_Warlock, BattleBotAI.Main/UpdateOutOfCombatAI_Warrior, PartyBotAI/CheckForDispelTargets, PartyBotAI/CrowdControlMarkedTargets, PartyBotAI/EnterCombatDruidForm, PartyBotAI/EnterStealthIfNeeded, PartyBotAI/UpdateInCombatAI, PartyBotAI/UpdateInCombatAI_Druid, PartyBotAI/UpdateInCombatAI_Hunter, PartyBotAI/UpdateInCombatAI_Mage, PartyBotAI/UpdateInCombatAI_Paladin, PartyBotAI/UpdateInCombatAI_Priest, PartyBotAI/UpdateInCombatAI_Rogue, PartyBotAI/UpdateInCombatAI_Shaman, PartyBotAI/UpdateInCombatAI_Warlock, PartyBotAI/UpdateInCombatAI_Warrior, PartyBotAI/UpdateOutOfCombatAI, PartyBotAI/UpdateOutOfCombatAI_Druid, PartyBotAI/UpdateOutOfCombatAI_Hunter, PartyBotAI/UpdateOutOfCombatAI_Mage, PartyBotAI/UpdateOutOfCombatAI_Paladin, PartyBotAI/UpdateOutOfCombatAI_Priest, PartyBotAI/UpdateOutOfCombatAI_Shaman, PartyBotAI/UpdateOutOfCombatAI_Warlock, PartyBotAI/UpdateOutOfCombatAI_Warrior | — |
| AddItemToInventory | method | game_Objects_Item/GenerateItemRandomPropertyId, game_Objects_Item/SetCount, Player.Main/CanStoreNewItem, Player.Main/StoreNewItem | — | — |
| AddHunterAmmo | method | game_Objects_Item/GetProto, ItemPrototype/GetMaxStackSize, ObjectMgr/GetItemPrototypeMap, Player.Main/CanUseAmmo, Player.Main/DestroyItem, Player.Main/GetItemByPos, Player.Main/SetAmmo, Unit.Main/GetLevel | AiBotAI.Combat/UpdateInCombatAI_Hunter, BattleBotAI.Main/UpdateInCombatAI_Hunter, PartyBotAI/UpdateInCombatAI_Hunter | — |
| EquipOrUseNewItem | method | game_Objects_Item/GetProficiencySpell, game_Objects_Item/GetProto, game_Objects_Item/IsEquipped, Player.Main/CastItemUseSpell, Player.Main/DestroyItem, Player.Main/EquipItem, Player.Main/FindEquipSlot, Player.Main/GetItemByPos, Player.Main/HasSpell, Player.Main/LearnSpell, Player.Main/RemoveItem, SpellCastTargetsInfo/setUnitTarget, SpellCastTargetsInfo/SpellCastTargets | — | — |
| GetHighestHonorRankFromEquippedItems | method | game_Objects_Item/GetProto, Player.Main/GetItemByPos | — | — |
| UpdateVisualHonorRankBasedOnItems | method | WorldObject.Object/SetByteValue | — | — |
| SummonShamanTotems | method | Unit.Main/GetTotem | AiBotAI.Combat/UpdateInCombatAI_Shaman, AiBotAI.Combat/UpdateOutOfCombatAI_Shaman, BattleBotAI.Main/UpdateInCombatAI_Shaman, BattleBotAI.Main/UpdateOutOfCombatAI_Shaman, PartyBotAI/UpdateInCombatAI_Shaman, PartyBotAI/UpdateOutOfCombatAI_Shaman | — |
| CastWeaponBuff | method | game_Objects_Item/GetEnchantmentId, ObjectGuid/ObjectGuid, Player.Main/GetItemByPos, Spell.Main/prepare, Spell.Main/Spell#2, SpellCastTargetsInfo/setItemTarget, SpellCastTargetsInfo/SpellCastTargets | AiBotAI.Combat/UpdateOutOfCombatAI_Rogue, AiBotAI.Combat/UpdateOutOfCombatAI_Shaman, BattleBotAI.Main/UpdateOutOfCombatAI_Rogue, BattleBotAI.Main/UpdateOutOfCombatAI_Shaman, PartyBotAI/UpdateOutOfCombatAI_Rogue, PartyBotAI/UpdateOutOfCombatAI_Shaman | — |
| UseTrinketEffects | method | Player.Main/GetItemByPos | AiBotAI.Combat/UpdateInCombatAI, BattleBotAI.Main/UpdateInCombatAI, PartyBotAI/UpdateInCombatAI | — |
| UseItemEffect | method | game_Objects_Item/GetProto, SpellCaster/CastSpell, SpellCaster/IsSpellReady, SpellEntry/HasAttribute#3, SpellEntry/IsPositiveSpell#4, SpellMgr/GetSpellEntry, SpellMgr/Instance, Unit.Main/GetVictim | — | — |
| BreakCrowdControlEffects | method | Aura/GetSpellProto, SpellEntry/HasAura, Unit.Main/GetAttackers, Unit.Main/GetAurasByType, Unit.Main/GetClass, Unit.Main/GetHealthPercent, Unit.Main/HasUnitState, Unit.Main/RemoveAurasDueToSpellByCancel | AiBotAI.Main/UpdateAI, BattleBotAI.Main/UpdateAI, PartyBotAI/UpdateAI | — |
| IsWearingShield | method | game_Objects_Item/GetProto, Player.Main/GetItemByPos | AiBotAI.Combat/UpdateInCombatAI_Warrior, BattleBotAI.Main/UpdateInCombatAI_Warrior, PartyBotAI/GetDistancingTarget, PartyBotAI/UpdateInCombatAI_Warrior | — |
| IsInDuel | method | — | PartyBotAI/CanTryToCastSpell, PartyBotAI/CanUseCrowdControl, PartyBotAI/SelectAttackTarget, PartyBotAI/SelectResurrectionTarget, PartyBotAI/SelectShieldTarget, PartyBotAI/UpdateAI, PartyBotAI/UpdateInCombatAI, PartyBotAI/UpdateOutOfCombatAI | — |
| GetRole | method | Unit.Main/GetClass | PartyBotAI/AttackStart, PartyBotAI/EnterCombatDruidForm, PartyBotAI/UpdateAI, PartyBotAI/UpdateInCombatAI_Druid, PartyBotAI/UpdateInCombatAI_Hunter, PartyBotAI/UpdateInCombatAI_Mage, PartyBotAI/UpdateInCombatAI_Paladin, PartyBotAI/UpdateInCombatAI_Priest, PartyBotAI/UpdateInCombatAI_Shaman, PartyBotAI/UpdateOutOfCombatAI_Druid | — |
| SendBattlefieldPortPacket | method | BattleFieldPort/BattleFieldPort, Player.Main/GetSession, Player.Main/IsInvitedForBattleGroundQueueType, SharedDefines/GetBattleGrounMapIdByTypeId, WorldSession.BattleGroundHandler/HandleBattleFieldPortOpcode | BattleBotAI.Main/UpdateAI, PartyBotAI/UpdateAI | — |
| SendBattlemasterJoinPacket | method | Log.Main/Out, Object/GetObjectGuid, Player.Main/GetSession, WorldSession.BattleGroundHandler/RequestBgJoinQueue | BattleBotAI.Main/UpdateAI | — |
| SendAreaTriggerPacket | method | AreaTrigger/AreaTrigger, Player.Main/GetSession, WorldSession.MiscHandler/HandleAreaTriggerOpcode | — | — |
| ActivateNearbyAreaTrigger | method | ObjectMgr/GetAreaTriggersMap, ObjectMgr/IsPointInAreaTriggerZone, WorldObject.Object/GetMapId, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | BattleBotAI.BattleBotWaypoints/MovementInform | — |
| OnPacketReceived | method | ByteBuffer/contents, Object/GetObjectGuid, ObjectGuid/ObjectGuid#5, Player.Main/GetResurrector, Player.Main/GetSession, Player.Main/InBattleGround, Player.Main/IsBeingTeleported, Player.Main/IsInvitedForBattleGroundQueueType, Unit.Main/GetLastCounterForMovementChangeType, WorldPacket/GetOpcode, WorldSession.Main/QueuePacket | AiBotAI.Main/OnPacketReceived, BattleBotAI.Main/OnPacketReceived, PartyBotAI/OnPacketReceived | — |
