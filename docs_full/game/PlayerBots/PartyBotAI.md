# PartyBotAI

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# PartyBotAI

`PartyBotAI` is the artificial intelligence controller for player-controlled "party bots" in the WoWVMaNGOS server. It inherits from `CombatBotBaseAI` and specializes in managing a bot's behavior within a group context, including following a designated leader, coordinating attacks with party members, handling class-specific rotation logic for nine playable classes, and managing out-of-combat utilities like buffing, eating/drinking, and resurrection.

Unlike standard creature AI, `PartyBotAI` operates on a `Player` object (`me`) that is controlled by the server rather than a human user. It relies heavily on the presence of a `PartyLeader` (another `Player`) to determine movement targets, attack priorities, and group composition. The AI updates every 1000ms (`PB_UPDATE_INTERVAL`) via `UpdateAI`, delegating specific combat and non-combat decisions to specialized methods based on the bot's current state (in/out of combat) and class.

## Member-by-Member Behavior

### Initialization and Lifecycle
*   **PartyBotAI** (Constructors): Two constructors exist. The first initializes a temporary bot with specific race, class, level, and spawn coordinates, optionally cloning stats/gear from another player (`pClone`). The second initializes a bot loaded from the database (persistent bot), storing only leader and location data. Both store the leader's GUID and reset the update timer.
*   **OnSessionLoaded**: Triggered when the bot's session loads. If the bot is a persistent character (no race/class set in constructor), it logs in directly. If it is a temporary bot, it spawns a new player character using `PlayerBotAI::SpawnNewPlayer` (called via `PlayerBotAI` unit, though mapped here as internal logic flow) or clones from an existing player.
*   **OnPlayerLogin**: Sets the `UNIT_FLAG_SPAWNING` flag if the bot is not yet initialized, preventing interaction until setup is complete.
*   **UpdateAI**: The main tick function. It handles initialization (joining groups, equipping gear, learning spells), checks for leader validity, manages battleground teleportation, handles death/resurrection, and delegates to `UpdateOutOfCombatAI` or `UpdateInCombatAI`. It also manages basic movement (following the leader or chasing victims) and resource management (eating/drinking).

### Group and Leader Management
*   **GetPartyLeader**: Retrieves the `Player` object associated with `m_leaderGuid`. It validates that the leader is in the same group and battleground state as the bot. Returns `nullptr` if the leader is invalid, offline, or if the bot itself is the leader outside a battleground.
*   **AddToPlayerGroup**: Ensures the bot is in the same `Group` as the leader. If the leader has no group, it creates one. If the bot is in a different group, it removes itself and adds to the leader's group.

### Target Selection and Combat Logic
*   **SelectAttackTarget**: Determines the primary enemy target. Priority order: Duel opponent -> Marked raid icons -> Leader's current victim -> Units attacking the bot -> Attackers of other party members (`SelectPartyAttackTarget`) -> Pet's attacker.
*   **SelectPartyAttackTarget**: Iterates through group members to find an enemy attacking any ally within 50 yards.
*   **SelectResurrectionTarget**: Finds a dead group member within line-of-sight and range of the resurrection spell.
*   **SelectShieldTarget**: Finds a group member below 90% health who is being attacked and is not immune to shields.
*   **GetMarkedTarget**: Resolves a raid icon mark to a `Unit` pointer using the group's marked target list.
*   **AttackStart**: Initiates combat with a victim. Adjusts chase distance based on role (Range DPS chases closer if low mana, Melee DPS chases close). Starts the chase motion generator.
*   **CanTryToCastSpell**: Overrides base logic to prevent casting harmful AoE spells if they would pull aggro from a tank. It calculates threat levels and enemy health/level ratios to decide if the cast is safe.
*   **CanUseCrowdControl**: Checks if a CC spell can be used. Prevents casting if the target is already CC'd by another bot, if the spell interrupts damage (and others are on the target), or if the bot already has a single-target aura on the target.
*   **CrowdControlMarkedTargets**: Attempts to CC enemies marked with raid icons specified in `m_marksToCC`.

### Movement and Positioning
*   **IsValidDistancingTarget**: Checks if a unit is a valid target to run towards for kiting/distancing (alive, in world, same map, 15-30 yards away from bot, and >15 yards from the enemy).
*   **GetDistancingTarget**: Finds a suitable ally to kite towards. Prioritizes tanks/shield-users, then other valid allies. Falls back to the leader if valid.
*   **RunAwayFromTarget**: Moves the bot towards a distancing target or simply moves 15 yards away from the enemy if no target is found.
*   **DrinkAndEat**: Checks health and mana. If below 100%, casts food/drink spells, stopping movement and clearing motion generators to idle.

### Class-Specific AI Updates
The AI delegates detailed spell rotation and ability usage to class-specific methods. These are called from `UpdateOutOfCombatAI` and `UpdateInCombatAI` based on `me->GetClass()`.

*   **Paladin**:
    *   *Out of Combat*: Casts auras, Righteous Fury (if tank), Blessings (buffs), and heals injured allies.
    *   *In Combat*: Uses Divine Shield (low HP), Blessing of Protection/Sacrifice, Lay on Hands (emergency heal), Holy Shield, Turn Evil, Judgement, Hammer of Justice/Wrath, Consecration (AoE), Exorcism/Holy Wrath (undead/demons), and Holy Shock. Healers prioritize healing; DPS prioritize damage.
*   **Shaman**:
    *   *Out of Combat*: Weapon buffs, Lightning Shield, heals, summons totems if victim exists.
    *   *In Combat*: Mana Tide Totem (low mana), Elemental Mastery, Earth/Frost Shock, Stormstrike, Chain Lightning, Purge, Flame Shock, Lightning Bolt. Healers prioritize healing; DPS prioritize damage. Summons totems.
*   **Hunter**:
    *   *Out of Combat*: Aspects, Hunter's Mark, commands pet to attack, summons pet if needed.
    *   *In Combat*: Volley (AoE), Auto Shot (with ammo check), Concussive Shot, Aimed/Arcane Shot, Serpent Sting, Multi-Shot, Scare Beast, Disengage, Aspect of the Monkey, Feign Death (low HP), Wing Clip/Mongoose Bite/Raptor Strike (melee range), Aspect of the Hawk (ranged). Kites if melee range is unsafe.
*   **Mage**:
    *   *Out of Combat*: Arcane Intellect/Brilliance, Ice Armor/Barrier.
    *   *In Combat*: Combustion, Pyroblast (with Presence of Mind or high HP diff), Ice Block (low HP), Mana Shield, Blink (escape), Frost Nova, Cone of Cold, Blast Wave, Arcane Explosion, Counterspell, Blizzard, Polymorph (CC), Arcane Power, Presence of Mind, Scorch, Frostbolt, Fire Blast, Fireball, Evocation (mana regen), Wand shot (low mana).
*   **Priest**:
    *   *Out of Combat*: Fortitude, Spirit, Shadow Protection, Inner Fire, heals.
    *   *In Combat*: Power Word Shield, Fade, Shackle Undead (CC), Inner Focus, Shadowform (DPS), Silence, Vampiric Embrace, Mind Blast, Shadow Word Pain, Devouring Plague, Psychic Scream, Mana Burn, Mind Flay, Holy Nova, Smite, Wand shot (low mana). Healers shield and heal; DPS use shadow spells.
*   **Warlock**:
    *   *Out of Combat*: Detect Invisibility, Demon Armor, commands pet, summons pet.
    *   *In Combat*: Death Coil, Shadowburn (execute), Searing Pain, Banish (CC), Rain of Fire, Demonic Sacrifice, Immolate, Conflagrate, Corruption, Siphon Life, Drain Life, Fear (CC), Curse of Agony, Howl of Terror, Shadow Bolt, Life Tap (mana), Wand shot (low mana).
*   **Warrior**:
    *   *Out of Combat*: Battle Stance, Battle Shout/Bloodrage, Charge.
    *   *In Combat*: Pummel/Shield Bash (interrupt), Execute, Overpower, Last Stand, Concussion Blow, Shield Block/Wall/Slam (defensive stance), Thunder Clap/Sunder Armor (tank), Hamstring, Rend, Intimidating Shout, Retaliation, Sweeping Strikes, Recklessness/Death Wish (burst), Mortal Strike, Bloodthirst, Defensive/Berserker Stance switching, Intercept, Whirlwind, Disarm, Demoralizing Shout, Cleave/Heroic Strike (rage dump).
*   **Rogue**:
    *   *Out of Combat*: Poisons, Stealth/Prowl.
    *   *In Combat*: Premeditation, Garrote/Ambush/Cheap Shot (stealth opener), Vanish (escape), Slice and Dice/Eviscerate/Kidney Shot/Expose Armor/Rupture (finishers), Blind (CC), Adrenaline Rush, Gouge/Kick (interrupt), Evasion, Cold Blood, Blade Flurry, Backstab, Ghostly Strike, Hemorrhage, Sinister Strike, Sprint.
*   **Druid**:
    *   *Out of Combat*: Leaves combat form if healer, Mark of the Wild/Gift of the Wild, Thorns, Nature's Grasp, enters combat form (Cat/Bear/Moonkin), Prowl (Cat), heals if mana high.
    *   *In Combat*: Barkskin, Hibernate (CC), HoTs/Direct Heals, Innervate, Cat Form (Pounce/Ravage/Tiger's Fury/Cower/Ferocious Bite/Rip/Faerie Fire/Dash/Shred/Rake/Claw), Bear Form (Feral Charge/Bash/Frenzied Regen/Faerie Fire/Demoralizing Roar/Swipe/Maul), Moonkin/None (Entangling Roots/Faerie Fire/Insect Swarm/Hurricane/Moonfire/Starfire/Wrath).

### Utility and State Management
*   **CheckForDispelTargets**: Iterates through class-specific dispel spells (Cleanse, Cure Disease/Poison, Remove Lesser Curse, Dispel Magic, Abolish Disease/Poison, Remove Curse) and casts them on appropriate targets found by `SelectDispelTarget`.
*   **ShouldAutoRevive**: Determines if the bot should resurrect itself. Returns true if dead, or if alive but nearby players are not in combat and not healers (preventing spamming revives while healers are busy).
*   **ShouldEnterStealth**: Checks conditions for stealth (mounted, in combat, BG, FFA PvP, low HP, leader status).
*   **EnterStealthIfNeeded**: Casts or cancels stealth based on `ShouldEnterStealth`.
*   **EnterCombatDruidForm**: Switches Druid to Cat, Bear, or Moonkin form based on role.
*   **CloneFromPlayer**: Copies level, spells, honor rank, and equipment from a source player to the bot. Unequips current gear and stores new items.
*   **OnPacketReceived**: Handles specific opcodes. Resets spell data on learn/remove spells. Accepts duel requests automatically.

## Cross-Unit Boundaries

*   **ChatHandler.PlayerBotMgr**: Calls the constructors to create new bots via commands (`HandlePartyBotAddCommand`, `HandlePartyBotCloneCommand`, `HandlePartyBotLoadCommand`). Also calls `AttackStart` via `HandlePartyBotAttackStartCommand` and `HandlePartyBotPullCommand`.
*   **CombatBotBaseAI**: `PartyBotAI` inherits from this. It calls numerous helper methods for spell casting (`DoCastSpell`), role checking (`GetRole`, `IsHealerClass`, etc.), target selection (`SelectDispelTarget`, `SelectBuffTarget`, `FindAndHealInjuredAlly`), and state checks (`IsInDuel`, `IsValidHostileTarget`). It also calls `OnPacketReceived` for default opcode handling.
*   **Player.Main**: Extensively used for accessing player state (`GetLevel`, `GetGroup`, `GetVictim`, `GetHealth`, `GetPower`, `IsInCombat`, `IsDead`, etc.), modifying state (`GiveLevel`, `LearnSpell`, `SetHealth`, `SetPower`, `TeleportTo`, `ResurrectPlayer`), and inventory management (`GetItemByPos`, `StoreNewItemInBestSlots`, `AutoUnequipItemFromSlot`).
*   **Unit.Main**: Used for general unit operations (`Attack`, `GetMotionMaster`, `GetDistance`, `GetMap`, `IsAlive`, `GetClass`, `GetShapeshiftForm`, `HasAura`, `CastSpell`, `InterruptSpell`, `StopMoving`, `SetStandState`, `SetSheath`).
*   **Group**: Used to access group members (`GetFirstMember`, `GetLeaderGuid`, `GetTargetWithIcon`) and manage membership (`AddMember`, `Create`).
*   **ObjectAccessor**: Used to find players by GUID (`FindPlayer`, `FindPlayerNotInWorld`).
*   **ObjectMgr**: Used to add groups to the global manager (`AddGroup`).
*   **SpellMgr**: Used to retrieve spell entries (`GetSpellEntry`, `GetFirstSpellInChain`).
*   **DBCStores**: Used to get talent spell positions (`GetTalentSpellPos`).
*   **game_Objects_Item**: Used to get item prototypes and enchantments (`GetProto`, `GetEnchantmentId`).
*   **HonorMgr**: Used to copy honor ranks (`GetHighestRank`, `SetHighestRank`, `GetRank`, `SetRank`).
*   **Creature.MotionMaster / MotionMaster**: Used to control movement (`MoveChase`, `MoveFollow`, `MoveIdle`, `MoveDistance`, `Clear`, `GetCurrentMovementGeneratorType`).
*   **CreatureAI**: Called on pets to initiate attacks (`AttackStart`).
*   **WorldSession**: Used to queue packets (`QueuePacket`) and login players (`LoginPlayer`).
*   **ChatHandler.Chat / TeleportCommands**: Used internally to teleport the bot to the leader (`HandleGonameCommand`).
*   **SharedDefines / shared_Util**: Used for random number generation (`urand`, `frand`) and form checking (`IsTankingForm`).

## Data Model

This unit does not directly query or modify database tables. It interacts with in-memory objects (`Player`, `Group`, `SpellEntry`, etc.) that may have been loaded from the database by other systems. The `CloneFromPlayer` method modifies the bot's inventory and spells in memory, which may persist to the database upon logout/save, but no direct SQL queries are executed in this unit.

## Notable Implementation Details

*   **Initialization Flow**: `UpdateAI` contains a large block of initialization logic that runs only once when `!m_initialized`. This includes joining groups, cloning gear/spells, learning premade specs, equipping gear, and setting visibility. This ensures the bot is fully set up before engaging in AI logic.
*   **Leader Dependency**: The AI frequently checks for `GetPartyLeader()`. If the leader is invalid or not in the world, the bot marks itself for removal (`botEntry->requestRemoval = true`). This makes the bot strictly dependent on the leader's existence and state.
*   **Battleground Handling**: Special logic exists to handle battleground transitions. If the leader is in a BG and the bot is not, it waits for an invite or sends a port packet. If the bot is dead in a BG, it repops at the graveyard.
*   **Spell Data Reset**: `OnPacketReceived` sets `m_resetSpellData = true` when spell-related opcodes are received. `UpdateAI` then resets and repopulates spell data, ensuring the AI always has current spell information.
*   **Resource Management**: `DrinkAndEat` is called out of combat. If the bot is far from the leader (>100 yards), it instantly restores health/mana instead of eating/drinking, likely to avoid long cast times during travel.
*   **Class-Specific Rotations**: Each class has distinct out-of-combat and in-combat behaviors. For example, Hunters manage pet commands and ammo, Warlocks manage life tap and demon sacrifice, and Druids switch forms based on role and combat state.
*   **Crowd Control Coordination**: `CanUseCrowdControl` and `CrowdControlMarkedTargets` help coordinate CC among bots, preventing multiple bots from CCing the same target unnecessarily and respecting raid marks.
*   **Threat Awareness**: `CanTryToCastSpell` includes logic to avoid pulling aggro with AoE spells if the bot is not the tank, checking threat levels and enemy stats.
*   **Stealth Management**: Rogues and Druids (Cat form) have specific stealth logic (`ShouldEnterStealth`, `EnterStealthIfNeeded`) that considers combat state, health, and leader status.
*   **Hardcoded Spell IDs**: Some spells are hardcoded in enums (e.g., `PB_SPELL_FOOD`, `PB_SPELL_AUTO_SHOT`), while others are dynamically retrieved from `m_spells` structures populated by `PopulateSpellData` (from `CombatBotBaseAI`).

## Member Reference

*   **PartyBotAI**: Constructor initializing temporary bot with race, class, level, clone source, and spawn coords.
*   **PartyBotAI#2**: Constructor initializing persistent bot with leader and spawn coords.
*   **OnSessionLoaded**: Logs in persistent bot or spawns/clones temporary bot.
*   **CloneFromPlayer**: Copies level, spells, honor, and gear from a source player to the bot.
*   **GetPartyLeader**: Returns the leader player if valid and in same group/BG.
*   **IsValidDistancingTarget**: Checks if a unit is a valid kiting target.
*   **GetDistancingTarget**: Finds a suitable ally to kite towards.
*   **RunAwayFromTarget**: Moves bot towards distancing target or away from enemy.
*   **DrinkAndEat**: Casts food/drink if health/mana low, idles movement.
*   **ShouldAutoRevive**: Determines if bot should self-resurrect.
*   **CanTryToCastSpell**: Checks if spell can be cast, including aggro safety for AoE.
*   **CanUseCrowdControl**: Checks if CC spell can be used on target.
*   **AttackStart**: Initiates combat, sets chase distance, starts chase motion.
*   **GetMarkedTarget**: Resolves raid icon to Unit.
*   **SelectAttackTarget**: Selects primary enemy target based on priority.
*   **SelectPartyAttackTarget**: Finds enemy attacking any party member.
*   **SelectResurrectionTarget**: Finds dead party member for resurrection.
*   **SelectShieldTarget**: Finds injured, attacked party member for shielding.
*   **CrowdControlMarkedTargets**: Casts CC on marked targets.
*   **AddToPlayerGroup**: Ensures bot is in leader's group.
*   **OnPacketReceived**: Handles spell change and duel request opcodes.
*   **OnPlayerLogin**: Sets spawning flag if not initialized.
*   **UpdateAI**: Main tick, handles init, leader checks, BG, death, movement, and delegates to class AI.
*   **UpdateOutOfCombatAI**: Delegates to class-specific out-of-combat AI.
*   **UpdateInCombatAI**: Delegates to class-specific in-combat AI.
*   **CheckForDispelTargets**: Casts dispels based on class.
*   **UpdateOutOfCombatAI_Paladin**: Paladin out-of-combat buffs/heals.
*   **UpdateInCombatAI_Paladin**: Paladin in-combat rotation.
*   **UpdateOutOfCombatAI_Shaman**: Shaman out-of-combat buffs/totems.
*   **UpdateInCombatAI_Shaman**: Shaman in-combat rotation.
*   **UpdateOutOfCombatAI_Hunter**: Hunter out-of-combat aspects/pet.
*   **UpdateInCombatAI_Hunter**: Hunter in-combat rotation.
*   **UpdateOutOfCombatAI_Mage**: Mage out-of-combat buffs.
*   **UpdateInCombatAI_Mage**: Mage in-combat rotation.
*   **UpdateOutOfCombatAI_Priest**: Priest out-of-combat buffs/heals.
*   **UpdateInCombatAI_Priest**: Priest in-combat rotation.
*   **UpdateOutOfCombatAI_Warlock**: Warlock out-of-combat buffs/pet.
*   **UpdateInCombatAI_Warlock**: Warlock in-combat rotation.
*   **UpdateOutOfCombatAI_Warrior**: Warrior out-of-combat stances/buffs.
*   **UpdateInCombatAI_Warrior**: Warrior in-combat rotation.
*   **ShouldEnterStealth**: Checks conditions for stealth.
*   **EnterStealthIfNeeded**: Casts/cancels stealth.
*   **UpdateOutOfCombatAI_Rogue**: Rogue out-of-combat poisons/stealth.
*   **UpdateInCombatAI_Rogue**: Rogue in-combat rotation.
*   **EnterCombatDruidForm**: Switches Druid form based on role.
*   **UpdateOutOfCombatAI_Druid**: Druid out-of-combat buffs/forms/heals.
*   **UpdateInCombatAI_Druid**: Druid in-combat rotation by form.

---

<!-- machine-true, projected from graph.json -->

## Map — PartyBotAI

*Source:* PartyBotAI.cpp, PartyBotAI.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| PartyBotAI | ctor | — | ChatHandler.PlayerBotMgr/HandlePartyBotAddCommand, ChatHandler.PlayerBotMgr/HandlePartyBotCloneCommand | — |
| PartyBotAI#2 | ctor | — | ChatHandler.PlayerBotMgr/HandlePartyBotLoadCommand | — |
| OnSessionLoaded | method | ObjectAccessor/FindPlayer, ObjectGuid/ObjectGuid#5, PlayerBotAI/SpawnNewPlayer, WorldSession.CharacterHandler/LoginPlayer | — | — |
| CloneFromPlayer | method | DBCStores/GetTalentSpellPos, game_Objects_Item/GetEnchantmentId, game_Objects_Item/GetProto, HonorMgr/GetHighestRank, HonorMgr/GetRank, HonorMgr/SetHighestRank, HonorMgr/SetRank, Object/GetEntry, Player.Main/AutoUnequipItemFromSlot, Player.Main/GetHonorMgr, Player.Main/GetHonorMgr#2, Player.Main/GetItemByPos, Player.Main/GetSpellMap#2, Player.Main/GiveLevel, Player.Main/HasSpell, Player.Main/InitTalentForLevel, Player.Main/LearnSpell, Player.Main/SatisfyItemRequirements, Player.Main/StoreNewItemInBestSlots, SpellMgr/GetFirstSpellInChain, SpellMgr/GetSpellEntry, SpellMgr/Instance, Unit.Main/GetLevel, WorldObject.Object/SetUInt32Value | — | — |
| GetPartyLeader | method | Group/GetLeaderGuid, Object/GetObjectGuid, ObjectAccessor/FindPlayerNotInWorld, ObjectGuid/operator==, Player.Main/GetGroup, Player.Main/InBattleGround | — | — |
| IsValidDistancingTarget | method | Object/IsInWorld, Unit.Main/IsAlive, WorldObject.Object/GetDistance#3, WorldObject.Object/GetMap | — | — |
| GetDistancingTarget | method | CombatBotBaseAI/IsWearingShield, Group/GetFirstMember, GroupReference/next, Player.Main/GetGroup, SharedDefines/IsTankingForm, Unit.Main/GetShapeshiftForm | — | — |
| RunAwayFromTarget | method | Creature.MotionMaster/MoveDistance, Unit.Main/GetMotionMaster, Unit.Main/MonsterMove, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | — | — |
| DrinkAndEat | method | Creature.MotionMaster/GetCurrentMovementGeneratorType, Creature.MotionMaster/MoveIdle, MotionMaster/Clear, Player.Main/RemoveSpellCooldown, SpellCaster/CastSpell, SpellMgr/GetSpellEntry, SpellMgr/Instance, Unit.Main/GetHealthPercent, Unit.Main/GetMotionMaster, Unit.Main/GetPowerPercent, Unit.Main/GetPowerType, Unit.Main/GetVictim, Unit.Main/HasAura#2, Unit.Main/StopMoving | — | — |
| ShouldAutoRevive | method | CombatBotBaseAI/IsHealerClass, Group/GetFirstMember, GroupReference/next, Player.Main/GetGroup, Unit.Main/GetClass, Unit.Main/GetDeathState, Unit.Main/IsAlive, Unit.Main/IsInCombat, WorldObject.Object/IsWithinDistInMap | — | — |
| CanTryToCastSpell | method | CombatBotBaseAI/CanTryToCastSpell, CombatBotBaseAI/IsInDuel, SpellEntry/GetSpellRadius, SpellEntry/IsAreaOfEffectSpell, SpellEntry/IsPositiveSpell#4, ThreatManager/getThreat, Unit.Main/CanHaveThreatList, Unit.Main/GetEnemyListInRadiusAround, Unit.Main/GetHealth, Unit.Main/GetLevel, Unit.Main/GetMaxHealth, Unit.Main/GetThreatManager, Unit.Main/GetVictim, WorldObject.Object/IsValidAttackTarget | — | — |
| CanUseCrowdControl | method | CombatBotBaseAI/AreOthersOnSameTarget, CombatBotBaseAI/IsInDuel, Object/GetObjectGuid, SpellEntry/HasAuraInterruptFlag, SpellEntry/HasSingleTargetAura, Unit.Main/GetSingleCastSpellTargets | — | — |
| AttackStart | method | CombatBotBaseAI/GetRole, Creature.MotionMaster/MoveChase, Unit.Main/Attack, Unit.Main/GetMotionMaster, Unit.Main/GetPowerPercent, Unit.Main/HasDistanceCasterMovement, Unit.Main/IsMounted, Unit.Main/RemoveSpellsCausingAura, Unit.Main/SetCasterChaseDistance, WorldObject.Object/GetCombatDistance | ChatHandler.PlayerBotMgr/HandlePartyBotAttackStartCommand, ChatHandler.PlayerBotMgr/HandlePartyBotPullCommand | — |
| GetMarkedTarget | method | Group/GetTargetWithIcon, Map.Main/GetUnit, ObjectGuid/IsUnit, Player.Main/GetGroup, WorldObject.Object/GetMap | — | — |
| SelectAttackTarget | method | CombatBotBaseAI/IsInDuel, CombatBotBaseAI/IsValidHostileTarget, Group/GetTargetWithIcon, Map.Main/GetUnit, ObjectGuid/IsUnit, Player.Main/GetGroup, Unit.Main/GetAttackerForHelper, Unit.Main/GetAttackers, Unit.Main/GetPet, Unit.Main/GetVictim, Unit.Main/IsInCombat, WorldObject.Object/GetMap | — | — |
| SelectPartyAttackTarget | method | CombatBotBaseAI/IsValidHostileTarget, Group/GetFirstMember, GroupReference/next, Player.Main/GetGroup, Unit.Main/GetAttackers, WorldObject.Object/IsWithinDist | — | — |
| SelectResurrectionTarget | method | CombatBotBaseAI/IsInDuel, Group/GetFirstMember, GroupReference/next, Player.Main/GetGroup, SpellEntry/IsTargetInRange, Unit.Main/GetDeathState, WorldObject.Object/IsWithinLOSInMap | — | — |
| SelectShieldTarget | method | CombatBotBaseAI/IsInDuel, Group/GetFirstMember, GroupReference/next, Player.Main/GetGroup, Unit.Main/GetAttackers, Unit.Main/GetHealthPercent, Unit.Main/IsImmuneToMechanic | — | — |
| CrowdControlMarkedTargets | method | CombatBotBaseAI/AreOthersOnSameTarget, CombatBotBaseAI/DoCastSpell, CombatBotBaseAI/GetCrowdControlSpell, CombatBotBaseAI/IsValidHostileTarget, Object/GetObjectGuid, Unit.Main/ClearUnitState, Unit.Main/HasUnitState | — | — |
| AddToPlayerGroup | method | game_Group_Group/AddMember, game_Group_Group/Create, game_Group_Group/Group, Object/GetObjectGuid, ObjectAccessor/FindPlayer, ObjectMgr/AddGroup, Player.Main/GetGroup, Player.Main/GetName, Player.Main/RemoveFromGroup | — | — |
| OnPacketReceived | method | CombatBotBaseAI/OnPacketReceived, Object/GetObjectGuid, Player.Main/GetSession, WorldPacket/GetOpcode, WorldSession.Main/QueuePacket | — | — |
| OnPlayerLogin | method | WorldObject.Object/SetFlag | — | — |
| UpdateAI | method | Aura/GetId, ChatHandler.Chat/ChatHandler#2, ChatHandler.TeleportCommands/HandleGonameCommand, CombatBotBaseAI/AddAllSpellReagents, CombatBotBaseAI/AutoAssignRole, CombatBotBaseAI/AutoEquipGear, CombatBotBaseAI/BreakCrowdControlEffects, CombatBotBaseAI/GetRole, CombatBotBaseAI/IsInDuel, CombatBotBaseAI/IsValidHostileTarget, CombatBotBaseAI/LearnPremadeSpecForClass, CombatBotBaseAI/PopulateSpellData, CombatBotBaseAI/ResetSpellData, CombatBotBaseAI/SendBattlefieldPortPacket, CombatBotBaseAI/SummonPetIfNeeded, Creature.MotionMaster/GetCurrentMovementGeneratorType, Creature.MotionMaster/MoveChase, Creature.MotionMaster/MoveFollow, Creature.MotionMaster/MoveIdle, MotionMaster/Clear, Object/GetObjectGuid, Object/IsInWorld, ObjectAccessor/FindPlayer, ObjectGuid/IsEmpty, ObjectGuid/operator==, Player.Main/BuildPlayerRepop, Player.Main/GetName, Player.Main/GiveLevel, Player.Main/HasCheatOption, Player.Main/InBattleGround, Player.Main/InitTalentForLevel, Player.Main/IsBeingTeleported, Player.Main/IsGameMaster, Player.Main/RepopAtGraveyard, Player.Main/ResurrectPlayer, Player.Main/SetCheatOption, Player.Main/SetGameMaster, Player.Main/SpawnCorpseBones, Player.Main/TeleportTo, Player.Main/UpdateSkillsToMaxSkillsForLevel, Player.Main/UpdateVisibilityOf, Player.Main/UpdateZone, shared_Util/frand, shared_Util/urand, ShortTimeTracker/Passed, ShortTimeTracker/Reset, ShortTimeTracker/Update, Spell.Main/getState, SpellCaster/CastSpell#2, SpellCaster/GetCurrentSpell, SpellCaster/InterruptSpell, SpellCaster/IsNonMeleeSpellCasted, SpellCastTargetsInfo/getUnitTarget, SpellEntry/IsHealSpell, Unit.Main/AttackStop, Unit.Main/ClearTarget, Unit.Main/GetAurasByType, Unit.Main/GetClass, Unit.Main/GetDeathState, Unit.Main/GetDisplayId, Unit.Main/GetHealth, Unit.Main/GetLevel, Unit.Main/GetMaxHealth, Unit.Main/GetMaxPower, Unit.Main/GetMotionMaster, Unit.Main/GetNativeDisplayId, Unit.Main/GetPet, Unit.Main/GetPowerType, Unit.Main/GetSheath, Unit.Main/GetStandState, Unit.Main/GetTargetGuid, Unit.Main/GetVictim, Unit.Main/HasAuraType, Unit.Main/HasUnitState, Unit.Main/IsDead, Unit.Main/IsInCombat, Unit.Main/IsInDisallowedMountForm, Unit.Main/IsMounted, Unit.Main/IsStopped, Unit.Main/IsTaxiFlying, Unit.Main/RemoveSpellsCausingAura, Unit.Main/SelectRandomUnfriendlyTarget, Unit.Main/SetHealth, Unit.Main/SetHealthPercent, Unit.Main/SetPower, Unit.Main/SetPowerPercent, Unit.Main/SetSheath, Unit.Main/SetStandState, Unit.Main/SetVisibility, Unit.Main/StopMoving, World/getConfig#4, WorldObject.Object/GetCombatDistance, WorldObject.Object/GetZoneAndAreaId, WorldObject.Object/IsMoving, WorldObject.Object/IsWithinDistInMap, WorldObject.Object/RemoveFlag, WorldObject.Object/SetUInt32Value | — | — |
| UpdateOutOfCombatAI | method | CombatBotBaseAI/DoCastSpell, CombatBotBaseAI/IsInDuel, Unit.Main/GetClass, Unit.Main/GetVictim | — | — |
| UpdateInCombatAI | method | CombatBotBaseAI/DoCastSpell, CombatBotBaseAI/IsInDuel, CombatBotBaseAI/UseTrinketEffects, Unit.Main/AttackStop, Unit.Main/GetClass, Unit.Main/GetVictim | — | — |
| CheckForDispelTargets | method | CombatBotBaseAI/DoCastSpell, CombatBotBaseAI/SelectDispelTarget, Unit.Main/GetClass, Unit.Main/GetShapeshiftForm | — | — |
| UpdateOutOfCombatAI_Paladin | method | CombatBotBaseAI/DoCastSpell, CombatBotBaseAI/FindAndHealInjuredAlly, CombatBotBaseAI/SelectBuffTarget, SpellCaster/HasGCD, Unit.Main/ClearTarget | — | — |
| UpdateInCombatAI_Paladin | method | CombatBotBaseAI/DoCastSpell, CombatBotBaseAI/FindAndHealInjuredAlly, CombatBotBaseAI/FindAndPreHealTarget, CombatBotBaseAI/GetAttackersInRangeCount, CombatBotBaseAI/GetRole, CombatBotBaseAI/HealInjuredTarget, CombatBotBaseAI/IsPhysicalDamageClass, CombatBotBaseAI/SelectAttackerDifferentFrom, Creature.MotionMaster/GetCurrentMovementGeneratorType, Creature.MotionMaster/MoveChase, Object/IsCreature, SpellCaster/CastSpell, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/FindLowestHpFriendlyUnit, Unit.Main/GetAttackers, Unit.Main/GetClass, Unit.Main/GetCreatureType, Unit.Main/GetHealthPercent, Unit.Main/GetMotionMaster, Unit.Main/GetPowerPercent, Unit.Main/GetVictim, Unit.Main/HasAura#2, Unit.Main/HasAuraType, Unit.Main/HasUnitState | — | — |
| UpdateOutOfCombatAI_Shaman | method | CombatBotBaseAI/CastWeaponBuff, CombatBotBaseAI/DoCastSpell, CombatBotBaseAI/FindAndHealInjuredAlly, CombatBotBaseAI/SummonShamanTotems, Unit.Main/GetVictim | — | — |
| UpdateInCombatAI_Shaman | method | CombatBotBaseAI/DoCastSpell, CombatBotBaseAI/FindAndHealInjuredAlly, CombatBotBaseAI/FindAndPreHealTarget, CombatBotBaseAI/GetRole, CombatBotBaseAI/HealInjuredTarget, CombatBotBaseAI/IsValidDispelTarget, CombatBotBaseAI/SummonShamanTotems, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/GetAttackers, Unit.Main/GetHealthPercent, Unit.Main/GetPowerPercent, Unit.Main/GetVictim, WorldObject.Object/IsMoving | — | — |
| UpdateOutOfCombatAI_Hunter | method | CombatBotBaseAI/DoCastSpell, CombatBotBaseAI/SummonPetIfNeeded, Creature.Main/AI, CreatureAI/AttackStart, Unit.Main/GetCharmInfo, Unit.Main/GetPet, Unit.Main/GetVictim, Unit.Main/SetIsCommandAttack | — | — |
| UpdateInCombatAI_Hunter | method | CombatBotBaseAI/AddHunterAmmo, CombatBotBaseAI/DoCastSpell, CombatBotBaseAI/GetAttackersInRangeCount, CombatBotBaseAI/GetRole, Creature.MotionMaster/GetCurrentMovementGeneratorType, Creature.MotionMaster/MoveChase, MotionMaster/Clear, Player.Main/HasSpell, SpellCaster/CastSpell#2, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/GetAttackers, Unit.Main/GetEnemyCountInRadiusAround, Unit.Main/GetHealthPercent, Unit.Main/GetMotionMaster, Unit.Main/GetVictim, Unit.Main/HasUnitState, Unit.Main/IsStopped, Unit.Main/StopMoving, WorldObject.Object/GetCombatDistance, WorldObject.Object/GetDistance#3, WorldObject.Object/IsMoving | — | — |
| UpdateOutOfCombatAI_Mage | method | CombatBotBaseAI/DoCastSpell, CombatBotBaseAI/SelectBuffTarget#2, SpellCaster/HasGCD, Unit.Main/ClearTarget, Unit.Main/GetVictim | — | — |
| UpdateInCombatAI_Mage | method | CombatBotBaseAI/DoCastSpell, CombatBotBaseAI/GetAttackersInRangeCount, CombatBotBaseAI/GetRole, CombatBotBaseAI/SelectAttackerDifferentFrom, Creature.MotionMaster/GetCurrentMovementGeneratorType, Creature.MotionMaster/MoveChase, MotionMaster/Clear, Player.Main/HasSpell, SpellCaster/CastSpell#2, SpellCaster/GetCurrentSpell, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/GetEnemyCountInRadiusAround, Unit.Main/GetHealthPercent, Unit.Main/GetMaxHealth, Unit.Main/GetMotionMaster, Unit.Main/GetPowerPercent, Unit.Main/GetVictim, Unit.Main/HasAura#2, Unit.Main/HasAuraType, Unit.Main/HasUnitState, Unit.Main/IsInCombat, Unit.Main/SetCasterChaseDistance, WorldObject.Object/GetDistance#3, WorldObject.Object/IsMoving | — | — |
| UpdateOutOfCombatAI_Priest | method | CombatBotBaseAI/DoCastSpell, CombatBotBaseAI/FindAndHealInjuredAlly, CombatBotBaseAI/SelectBuffTarget#2, SpellCaster/HasGCD, Unit.Main/ClearTarget, Unit.Main/GetVictim | — | — |
| UpdateInCombatAI_Priest | method | CombatBotBaseAI/DoCastSpell, CombatBotBaseAI/FindAndPreHealTarget, CombatBotBaseAI/GetAttackersInRangeCount, CombatBotBaseAI/GetRole, CombatBotBaseAI/HealInjuredTargetDirect, CombatBotBaseAI/HealInjuredTargetPeriodic, CombatBotBaseAI/SelectHealTarget, CombatBotBaseAI/SelectPeriodicHealTarget, Creature.MotionMaster/GetCurrentMovementGeneratorType, Creature.MotionMaster/MoveChase, Player.Main/HasSpell, SpellCaster/CastSpell#2, SpellCaster/GetCurrentSpell, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/GetAttackers, Unit.Main/GetHealth, Unit.Main/GetMotionMaster, Unit.Main/GetPowerPercent, Unit.Main/GetPowerType, Unit.Main/GetShapeshiftForm, Unit.Main/GetVictim, Unit.Main/HasAuraType, WorldObject.Object/GetDistance#3, WorldObject.Object/IsMoving | — | — |
| UpdateOutOfCombatAI_Warlock | method | CombatBotBaseAI/DoCastSpell, CombatBotBaseAI/SelectBuffTarget, CombatBotBaseAI/SummonPetIfNeeded, Creature.Main/AI, CreatureAI/AttackStart, SpellCaster/HasGCD, Unit.Main/ClearTarget, Unit.Main/GetCharmInfo, Unit.Main/GetPet, Unit.Main/GetVictim, Unit.Main/SetIsCommandAttack | — | — |
| UpdateInCombatAI_Warlock | method | CombatBotBaseAI/DoCastSpell, CombatBotBaseAI/GetAttackersInRangeCount, Creature.MotionMaster/GetCurrentMovementGeneratorType, Creature.MotionMaster/MoveChase, Player.Main/HasSpell, SpellCaster/CastSpell#2, SpellCaster/GetCurrentSpell, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/GetAttackers, Unit.Main/GetEnemyCountInRadiusAround, Unit.Main/GetHealth, Unit.Main/GetHealthPercent, Unit.Main/GetMotionMaster, Unit.Main/GetPet, Unit.Main/GetPowerPercent, Unit.Main/GetVictim, Unit.Main/IsAlive, WorldObject.Object/GetDistance#3, WorldObject.Object/IsMoving | — | — |
| UpdateOutOfCombatAI_Warrior | method | CombatBotBaseAI/DoCastSpell, Unit.Main/GetPower, Unit.Main/GetVictim, Unit.Main/HasAura#2 | — | — |
| UpdateInCombatAI_Warrior | method | CombatBotBaseAI/DoCastSpell, CombatBotBaseAI/GetAttackersInRangeCount, CombatBotBaseAI/IsMeleeWeaponClass, CombatBotBaseAI/IsWearingShield, Creature.MotionMaster/GetCurrentMovementGeneratorType, Creature.MotionMaster/MoveChase, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/GetAttackers, Unit.Main/GetClass, Unit.Main/GetEnemyCountInRadiusAround, Unit.Main/GetHealthPercent, Unit.Main/GetLevel, Unit.Main/GetMotionMaster, Unit.Main/GetPower, Unit.Main/GetShapeshiftForm, Unit.Main/GetVictim, Unit.Main/HasAuraType, Unit.Main/HasUnitState, Unit.Main/IsImmuneToMechanic, WorldObject.Object/IsMoving | — | — |
| ShouldEnterStealth | method | Player.Main/InBattleGround, Player.Main/IsFFAPvP, Unit.Main/GetHealthPercent, Unit.Main/GetVictim, Unit.Main/HasAuraType, Unit.Main/IsDead, Unit.Main/IsFeigningDeathSuccessfully, Unit.Main/IsMounted | — | — |
| EnterStealthIfNeeded | method | CombatBotBaseAI/DoCastSpell, Unit.Main/HasAura#2, Unit.Main/RemoveAurasDueToSpellByCancel | — | — |
| UpdateOutOfCombatAI_Rogue | method | CombatBotBaseAI/CastWeaponBuff, Unit.Main/GetVictim | — | — |
| UpdateInCombatAI_Rogue | method | CombatBotBaseAI/DoCastSpell, CombatBotBaseAI/GetAttackersInRangeCount, CombatBotBaseAI/IsRangedDamageClass, CombatBotBaseAI/SelectAttackerDifferentFrom, Player.Main/GetComboPoints, SpellCaster/IsNonMeleeSpellCasted, SpellCaster/IsSpellReady#2, Unit.Main/AttackStop, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/GetClass, Unit.Main/GetHealthPercent, Unit.Main/GetPower, Unit.Main/GetVictim, Unit.Main/HasAura#2, Unit.Main/HasAuraType, Unit.Main/HasUnitState, Unit.Main/IsCaster, Unit.Main/IsImmuneToMechanic | — | — |
| EnterCombatDruidForm | method | CombatBotBaseAI/DoCastSpell, CombatBotBaseAI/GetRole | — | — |
| UpdateOutOfCombatAI_Druid | method | CombatBotBaseAI/DoCastSpell, CombatBotBaseAI/FindAndHealInjuredAlly, CombatBotBaseAI/GetRole, CombatBotBaseAI/SelectBuffTarget, CombatBotBaseAI/SelectBuffTarget#2, SpellCaster/HasGCD, Unit.Main/ClearTarget, Unit.Main/GetPowerPercent, Unit.Main/GetShapeshiftForm, Unit.Main/GetVictim, Unit.Main/HasAuraType, Unit.Main/RemoveSpellsCausingAura | — | — |
| UpdateInCombatAI_Druid | method | CombatBotBaseAI/DoCastSpell, CombatBotBaseAI/FindAndPreHealTarget, CombatBotBaseAI/GetAttackersInRangeCount, CombatBotBaseAI/GetRole, CombatBotBaseAI/HealInjuredTargetDirect, CombatBotBaseAI/HealInjuredTargetPeriodic, CombatBotBaseAI/SelectHealTarget, CombatBotBaseAI/SelectPeriodicHealTarget, Creature.MotionMaster/GetCurrentMovementGeneratorType, Creature.MotionMaster/MoveChase, Player.Main/GetComboPoints, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/GetAttackers, Unit.Main/GetClass, Unit.Main/GetEnemyCountInRadiusAround, Unit.Main/GetHealthPercent, Unit.Main/GetMotionMaster, Unit.Main/GetPower, Unit.Main/GetPowerPercent, Unit.Main/GetShapeshiftForm, Unit.Main/GetVictim, Unit.Main/HasAuraType, Unit.Main/HasDistanceCasterMovement, Unit.Main/HasUnitState, Unit.Main/RemoveSpellsCausingAura, Unit.Main/SetCasterChaseDistance, WorldObject.Object/GetDistance#3, WorldObject.Object/IsMoving | — | — |
