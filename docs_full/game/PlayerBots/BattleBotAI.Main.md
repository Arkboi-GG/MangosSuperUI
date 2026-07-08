# BattleBotAI.Main

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# BattleBotAI

**Purpose & Responsibilities**

`BattleBotAI` is the artificial intelligence controller for automated player characters ("battle bots") participating in World of Warcraft battlegrounds, specifically Warsong Gulch (WSG), Alterac Valley (AV), and Arathi Basin (AB). It inherits from `CombatBotBaseAI` and specializes in the unique mechanics of battleground gameplay, including objective-based movement (capturing flags, controlling nodes), team coordination, and survival strategies specific to large-scale PvP environments.

Key responsibilities include:
1.  **Battleground Lifecycle Management:** Handling entry, exit, death, resurrection, and queueing for specific battlegrounds.
2.  **Objective-Oriented Movement:** Integrating with `BattleBotWaypoints` to navigate maps, capture flags, and retreat to bases.
3.  **Class-Specific Combat Logic:** Implementing detailed spell rotation and decision-making trees for nine classes (Paladin, Shaman, Hunter, Mage, Priest, Warlock, Warrior, Rogue, Druid) tailored for battleground scenarios (e.g., flag carrier protection, defensive cooldowns).
4.  **Survival & Utility:** Managing mounts, food/drink consumption, graveyard jumps, and crowd control breaking.
5.  **Target Selection:** Prioritizing flag carriers, low-health enemies, and allies in need of healing or dispels.

This unit does not interact with any database tables. All state is held in memory via the `Player` object and AI-specific member variables.

---

## Member-by-Member Behavior

### Initialization and Lifecycle

*   **`BattleBotAI`**: The constructor initializes the bot's racial, class, level, and spatial coordinates, as well as its assigned battleground ID and temporary status. It resets the update timer to 2000ms.
*   **`OnSessionLoaded`**: Spawns the new player character in the world using the stored coordinates and attributes.
*   **`OnPlayerLogin`**: Sets the `UNIT_FLAG_SPAWNING` flag on the unit if it hasn't been initialized yet, preventing interaction until setup is complete.
*   **`UpdateAI`**: The main heartbeat of the AI, called every ~1000ms (`BB_UPDATE_INTERVAL`). It orchestrates the entire decision-making process:
    *   **Initialization:** On first run, it assigns roles, equips gear, learns spells, summons pets, and sets health/power to 100%.
    *   **Queueing/Entry:** If not in a battleground, it attempts to queue for the assigned battleground using chat commands (`HandleGoAlteracCommand`, etc.) or sends port packets if invited.
    *   **Death/Resurrection:** Handles repopping at graveyards or resurrecting outside battlegrounds.
    *   **Combat/Non-Combat Routing:** Delegates to `UpdateOutOfCombatAI` or `UpdateInCombatAI` based on combat state.
    *   **Utility Checks:** Calls `DrinkAndEat`, `UseMount`, and `UpdateWaypointMovement` when appropriate.
    *   **Temporary Bot Cleanup:** Removes temporary bots if no real players are present in the map during an active battleground.

### Movement and Positioning

*   **`UpdateWaypointMovement`**: Determines if the bot should start a new waypoint path. It checks if the bot is stopped, idle, and not in a "wait join" state. It prioritizes paths to objectives, then from the beginning, then from anywhere.
*   **`DoGraveyardJump`**: Executes a pre-recorded movement path (`vHordeGraveyardJumpPath` or `vAllianceGraveyardJumpPath`) to simulate jumping from a graveyard back into the battlefield in Warsong Gulch. It uses lambda events to relocate the bot frame-by-frame.
*   **`StopMoving`**: Halts all current movement generators and sets the bot to idle.
*   **`CheckForUnreachableTarget`**: Detects if the bot is chasing a target it cannot reach (due to terrain or distance). If stuck, it stops attacking and moving. If the target is a creature and the bot isn't moving, it performs a "cheat" teleport (`NearTeleportTo`) to bypass bad mmap data.

### Combat Engagement

*   **`AttackStart`**: Initiates combat with a victim. It dismounts if mounted, clears existing paths, and sets chase distances based on role (ranged/healers chase closer than melee). It starts the chase motion generator.
*   **`SelectAttackTarget`**: Chooses the next enemy to attack. Priority order:
    1.  Existing combat targets (prioritizing flag carriers).
    2.  Nearby enemy players (prioritizing flag carriers, weak enemies, and those within line-of-sight).
    3.  Attackers of nearby group members.
*   **`SelectFollowTarget`**: Identifies an ally to follow when not in combat. Healers prioritize following non-healer, non-stealth classes that are on the same mount status and within close proximity. Flag carriers are always followed if present.
*   **`ShouldIgnoreCombat`**: Returns `true` if the bot is carrying a flag in Warsong Gulch and is not rooted. This prevents the bot from stopping to fight while carrying the flag.

### Battleground Specifics

*   **`OnEnterBattleGround`**: Triggers when the bot enters a battleground. It summons pets and moves the bot to a random waiting spot near the base depending on the battleground type (WSG, AB, AV).
*   **`OnLeaveBattleGround`**: Clears paths, stops movement, and marks temporary bots for removal.
*   **`UpdateBattleGroundAI`**: Specifically handles Warsong Gulch flag mechanics. It searches for and interacts with dropped flags or stationary flags at bases.
*   **`UpdateFlagCarrierAI`**: Specialized logic for bots carrying a flag. It prioritizes defensive spells (e.g., Paladin's Hammer of Justice, Mage's Frost Nova, Warrior's Shield Wall) and mobility spells (e.g., Rogue's Sprint, Druid's Travel Form) to escape attackers.
*   **`GetMaxAggroDistanceForMap`**: Returns 30.0f for Alterac Valley, otherwise 50.0f. Used in target selection.

### Survival and Utility

*   **`DrinkAndEat`**: Consumes food and drink spells if health or mana is below 100% and the bot is not in combat, mounted, or carrying a flag. It stops movement to eat/drink.
*   **`UseMount`**: Attempts to mount the bot if it is not already mounted, moving, shapeshifted, rogue, in a "wait join" state, carrying a flag, or stealthed. It selects the appropriate mount spell based on level and race/class.
*   **`GetMountSpellId`**: Returns the correct spell ID for a mount based on the bot's level (40 or 60), race, and class (Paladin/Warlock have special mounts).
*   **`OnJustDied`**: Clears paths and stops movement.
*   **`OnJustRevived`**: Summons pets and attempts to find a target. If none found, it performs a graveyard jump if applicable.

### Class-Specific AI Updates

These methods implement the spell rotation and decision logic for each class, split into Out-of-Combat and In-Combat behaviors. They rely heavily on `CombatBotBaseAI` helpers like `CanTryToCastSpell`, `DoCastSpell`, `FindAndHealInjuredAlly`, and `SelectDispelTarget`.

#### Paladin
*   **`UpdateOutOfCombatAI_Paladin`**: Casts auras and blessings on allies. If buffing is complete, it looks for injured allies to heal.
*   **`UpdateInCombatAI_Paladin`**: Uses Divine Shield at low HP, maintains seals, casts Judgement, Hammer of Justice on casters, Holy Shock, Exorcism on undead, Consecration for AoE, Lay on Hands for emergency heals, and Cleanses debuffs. It also protects flag carriers with Blessing of Sacrifice.

#### Shaman
*   **`UpdateOutOfCombatAI_Shaman`**: Applies weapon buffs, Lightning Shield, and Ghost Wolf for mobility. If a victim exists, it summons totems and proceeds to in-combat logic.
*   **`UpdateInCombatAI_Shaman`**: Cancels Ghost Wolf if active. Uses Mana Tide Totem at low mana, Elemental Mastery, Earth Shock on casters, Frost Shock on movers, Stormstrike, Chain Lightning, Purge, Flame Shock, Lightning Bolt, and heals/dispels.

#### Hunter
*   **`UpdateOutOfCombatAI_Hunter`**: Casts Aspect of the Cheetah for mobility. If a victim exists, it applies Hunter's Mark, commands the pet to attack, and proceeds to in-combat logic.
*   **`UpdateInCombatAI_Hunter`**: Manages auto-shot (adding ammo if needed). Uses Concussive Shot on movers, Aimed Shot, Arcane Shot, Serpent Sting, Multi-Shot. Swaps aspects (Monkey/Hawk) based on engagement. Uses Mongoose Bite/Raptor Strike if rooted, Wing Clip, and kites via `MoveDistance`.

#### Mage
*   **`UpdateOutOfCombatAI_Mage`**: Applies Arcane Brilliance/Intellect and Ice Armor/Barrier. If a victim exists, it proceeds to in-combat logic.
*   **`UpdateInCombatAI_Mage`**: Uses Combustion, Pyroblast with Presence of Mind, Ice Block at low HP, Mana Shield vs physical, Counterspell, Blink/Frost Nova for escape, Blast Wave/Arcane Explosion for AoE, Polymorph, Scorch, Frostbolt, Fire Blast, Fireball, Evocation for mana, and wand shots at very low mana.

#### Priest
*   **`UpdateOutOfCombatAI_Priest`**: In "wait join" state, casts Prayers (Fortitude, Spirit, Shadow Protection). In progress, casts Power Word Fortitude/Divine Spirit and Inner Fire. If a victim exists, it proceeds to in-combat logic.
*   **`UpdateInCombatAI_Priest`**: Casts Power Word Shield, Inner Focus, heals, Dispel Magic/Abolish Disease, Shadowform, Silence on casters, Vampiric Embrace, Mind Blast, Shadow Word Pain, Devouring Plague, Psychic Scream for AoE fear, Mana Burn, Mind Flay, Smite, and wand shots at low mana.

#### Warlock
*   **`UpdateOutOfCombatAI_Warlock`**: In "wait join" state, casts Detect Invisibility. Always casts Demon Armor. Summons pet if none exists. If a victim exists, it commands the pet to attack and proceeds to in-combat logic.
*   **`UpdateInCombatAI_Warlock`**: Uses Death Coil, Shadowburn/Searing Pain on low HP, Shadow Ward vs Warlocks, Demonic Sacrifice, Immolate, Conflagrate, Corruption, Siphon Life/Drain Life for healing, Fear, Curse of Tongues/Exhaustion, Howl of Terror for AoE, Shadow Bolt, Life Tap for mana, and wand shots at low mana.

#### Warrior
*   **`UpdateOutOfCombatAI_Warrior`**: Sets Battle Stance, casts Battle Shout/Bloodrage, and Charges if a victim exists.
*   **`UpdateInCombatAI_Warrior`**: Uses Pummel/Shield Bash on casters, Execute, Overpower, Last Stand, Concussion Blow, Shield Block/Wall/Slam in Defensive Stance, Hamstring/Piercing Howl on movers, Rend on Rogues, Intimidating Shout, Retaliation, Recklessness/Death Wish/Berserker Rage for burst, Mortal Strike, Bloodthirst, Intercept, Whirlwind, Disarm, and Heroic Strike.

#### Rogue
*   **`UpdateOutOfCombatAI_Rogue`**: Applies poisons and casts Stealth (unless carrying a flag). If a victim exists, it proceeds to in-combat logic.
*   **`UpdateInCombatAI_Rogue`**: From stealth, uses Premeditation, Garrote/Ambush/Cheap Shot. At low HP, uses Vanish. Uses combo point finishers (Slice and Dice, Eviscerate, etc.), Blind, Adrenaline Rush, Gouge/Kick on casters, Evasion, Cold Blood, Blade Flurry, Backstab, Ghostly Strike, Hemorrhage, Sinister Strike, and Sprint to engage.

#### Druid
*   **`UpdateOutOfCombatAI_Druid`**: In "wait join" state, casts Gift of the Wild/Thorns. Otherwise, casts Mark of the Wild/Thorns/Nature's Grasp. Enters Cat/Bear forms for melee/tank roles, or Prowl in Cat form. If a victim exists, it enters Moonkin form and proceeds to in-combat logic. Uses Travel Form for mobility if not mounted.
*   **`UpdateInCombatAI_Druid`**: Cancels Travel Form. Uses Hibernate on attackers, heals, dispels (Abolish Poison/Cure Poison/Remove Curse), Innervate, and Barkskin. Rotation depends on form:
    *   *Cat*: Pounce, Ravage, Shred, Rip, Faerie Fire Feral, Dash, Claw.
    *   *Bear*: Feral Charge, Bash, Swipe, Maul, Demoralizing Roar.
    *   *Moonkin/None*: Entangling Roots, Moonfire, Starfire, Wrath.

### Packet Handling

*   **`OnPacketReceived`**: Intercepts `MSG_PVP_LOG_DATA` packets. If the battleground ends, it marks temporary bots for removal or queues a leave battlefield packet for permanent bots. Other packets are passed to `CombatBotBaseAI`.

---

## Cross-Unit Boundaries

*   **`BattleBotWaypoints`**: `BattleBotAI` relies heavily on this unit for pathfinding. It calls `ClearPath`, `StartNewPathFromAnywhere`, `StartNewPathFromBeginning`, `StartNewPathToObjective`, and receives callbacks like `AtCaveExit` (which triggers `UseMount`).
*   **`CombatBotBaseAI`**: The parent class provides core combat logic. `BattleBotAI` calls `IsRangedDamageClass`, `IsValidHostileTarget`, `IsHealerClass`, `IsStealthClass`, `SummonPetIfNeeded`, `AddAllSpellReagents`, `AutoAssignRole`, `AutoEquipGear`, `BreakCrowdControlEffects`, `LearnPremadeSpecForClass`, `PopulateSpellData`, `ResetSpellData`, `SendBattlefieldPortPacket`, `SendBattlemasterJoinPacket`, `CanTryToCastSpell`, `DoCastSpell`, `FindAndHealInjuredAlly`, `SelectBuffTarget`, `SelectDispelTarget`, `GetAttackersInRangeCount`, `IsMeleeDamageClass`, `IsPhysicalDamageClass`, `IsValidDispelTarget`, `SelectAttackerDifferentFrom`, `UseTrinketEffects`, `CastWeaponBuff`, `SummonShamanTotems`, `AddHunterAmmo`, and `OnPacketReceived`.
*   **`Unit.Main` / `Player.Main` / `WorldObject.Object`**: Standard API calls for state inspection (health, power, auras, position, team, group) and actions (attack, move, cast spell, relocate).
*   **`BattleGround`**: Used to determine battleground type, status, and team affiliation.
*   **`ChatHandler`**: Used in `UpdateAI` to queue for battlegrounds via internal command handlers (`HandleGoAlteracCommand`, etc.).
*   **`SpellMgr` / `SpellCaster`**: Used to retrieve spell entries and cast spells.
*   **`MotionMaster` / `Creature.MotionMaster`**: Used to control movement generators (chase, follow, point, idle).
*   **`Group` / `GroupReference` / `HostileRefManager`**: Used for target selection within groups and threat lists.
*   **`Log.Main`**: Used for error logging when queueing fails.
*   **`World`**: Used to check configuration settings (`CONFIG_UINT32_BATTLE_BOT_AUTO_EQUIP`).

---

## Data Model

This unit does not interact with any database tables. All data is transient and stored in memory within the `Player` object and `BattleBotAI` member variables.

---

## Notable Implementation Details

1.  **Hardcoded Spell IDs**: Mount spells, food/drink spells, and auto-shot/wand spells are hardcoded in the `BattleBotSpells` enum. This makes the AI brittle if spell IDs change between patches.
2.  **Graveyard Jump Cheating**: `DoGraveyardJump` uses pre-recorded movement packets to simulate jumping from a graveyard. This is a client-side simulation hack to bypass normal respawn mechanics and get the bot back into action quickly in WSG.
3.  **Mmap Bypass**: `CheckForUnreachableTarget` contains a "cheat" where it teleports the bot to the target if the target is a creature and the bot is stuck. This is explicitly commented as a workaround for bad mmap (movement map) data.
4.  **Flag Carrier Priority**: Target selection and combat ignoring logic heavily prioritizes flag carriers in Warsong Gulch. Bots will ignore combat entirely if they are carrying a flag and not rooted.
5.  **Temporary Bot Cleanup**: Temporary bots are marked for removal if the battleground ends or if no real players are present in the map during an active battleground. This prevents resource waste.
6.  **Class-Specific Rotations**: Each class has a detailed, hardcoded spell rotation. While effective, this approach requires manual updates for new spells or changes in spell mechanics.
7.  **Mount Restrictions**: Mounting is disabled for Rogues, stealthed units, flag carriers, and units in "wait join" state. This ensures realistic behavior and prevents exploits.
8.  **Food/Drink Logic**: Bots will stop moving to eat/drink if health/mana is low, unless they are in combat, mounted, or carrying a flag. This can cause them to pause unexpectedly in safe zones.
9.  **Queueing via Chat Commands**: The AI uses internal chat commands to queue for battlegrounds. If these commands fail (e.g., due to level restrictions or unavailability), the bot is marked for removal.
10. **No Database Persistence**: Since there is no database interaction, bot state is lost upon server restart. This is consistent with the "temporary" nature of many battle bots.

## Member Reference

**BattleBotAI**: Constructor initializing race, class, level, coordinates, battleground ID, and temporary status. Resets update timer.

**OnSessionLoaded**: Spawns the player character in the world using stored coordinates and attributes.

**GetMountSpellId**: Returns the appropriate mount spell ID based on the bot's level (40 or 60), race, and class (Paladin/Warlock exceptions).

**UseMount**: Attempts to mount the bot if conditions allow (not moving, not rogue, not stealthed, not carrying flag, not in wait-join state). Calls `GetMountSpellId` and casts the spell.

**DrinkAndEat**: Consumes food/drink if health/mana is low and bot is safe (not in combat, not mounted, not carrying flag). Stops movement to consume.

**GetMaxAggroDistanceForMap**: Returns 30.0f for Alterac Valley, 50.0f otherwise.

**AttackStart**: Initiates combat, dismounts if mounted, clears paths, sets chase distance based on role, and starts chase movement.

**ShouldIgnoreCombat**: Returns true if the bot is carrying a flag in Warsong Gulch and not rooted, preventing combat engagement.

**SelectAttackTarget**: Selects a target prioritizing flag carriers, existing threats, nearby enemies, and attackers of group members.

**SelectFollowTarget**: Selects an ally to follow, prioritizing flag carriers and non-healer/non-stealth allies for healers.

**DoGraveyardJump**: Executes a pre-recorded movement path to simulate jumping from a graveyard in Warsong Gulch.

**StopMoving**: Clears movement generators and sets the bot to idle.

**OnPacketReceived**: Handles `MSG_PVP_LOG_DATA` to detect battleground end and remove temporary bots or queue leave packets. Passes other packets to `CombatBotBaseAI`.

**OnPlayerLogin**: Sets `UNIT_FLAG_SPAWNING` if not initialized.

**UpdateWaypointMovement**: Starts a new waypoint path if the bot is idle, stopped, and not in a wait-join state. Prioritizes objective paths.

**OnJustDied**: Clears paths and stops movement.

**OnJustRevived**: Summons pets and attempts to find a target; performs graveyard jump if no target found.

**OnEnterBattleGround**: Summons pets and moves the bot to a random waiting spot near the base based on battleground type.

**OnLeaveBattleGround**: Clears paths, stops movement, and marks temporary bots for removal.

**CheckForUnreachableTarget**: Detects if the bot is chasing an unreachable target. Stops attack/movement or teleports if stuck due to bad mmap data.

**UpdateAI**: Main loop handling initialization, queueing, death/resurrection, combat routing, utility checks, and temporary bot cleanup.

**UpdateBattleGroundAI**: Handles Warsong Gulch flag mechanics (picking up dropped/stationary flags).

**UpdateFlagCarrierAI**: Specialized logic for flag carriers, prioritizing defensive and mobility spells to escape attackers.

**UpdateOutOfCombatAI**: Dispatches to class-specific out-of-combat AI methods.

**UpdateInCombatAI**: Dispatches to class-specific in-combat AI methods and uses trinket effects.

**UpdateOutOfCombatAI_Paladin**: Casts auras, blessings, and heals injured allies.

**UpdateInCombatAI_Paladin**: Uses defensive cooldowns, seals, judgements, holy shock, exorcism, consecration, lay on hands, and cleanse.

**UpdateOutOfCombatAI_Shaman**: Applies weapon buffs, lightning shield, ghost wolf, and summons totems.

**UpdateInCombatAI_Shaman**: Uses mana tide totem, elemental mastery, earth/frost shock, stormstrike, chain lightning, purge, flame shock, lightning bolt, and heals/dispels.

**UpdateOutOfCombatAI_Hunter**: Casts aspect of cheetah, hunter's mark, and commands pet to attack.

**UpdateInCombatAI_Hunter**: Manages auto-shot, uses concussive/aimed/arcaneshot, serpent sting, multi-shot, aspect swaps, mongoose bite/raptor strike, wing clip, and kites.

**UpdateOutOfCombatAI_Mage**: Applies arcane brilliance/intellect and ice armor/barrier.

**UpdateInCombatAI_Mage**: Uses combustion, pyroblast, ice block, mana shield, counterspell, blink/frost nova, blast wave/arcane explosion, polymorph, scorch, frostbolt, fire blast, fireball, evocation, and wand shots.

**UpdateOutOfCombatAI_Priest**: Casts prayers in wait-join, power word fortitude/divine spirit in progress, and inner fire.

**UpdateInCombatAI_Priest**: Uses power word shield, inner focus, heals, dispel magic/abolish disease, shadowform, silence, vampiric embrace, mind blast, shadow word pain, devouring plague, psychic scream, mana burn, mind flay, smite, and wand shots.

**UpdateOutOfCombatAI_Warlock**: Casts detect invisibility in wait-join, demon armor, and summons pet.

**UpdateInCombatAI_Warlock**: Uses death coil, shadowburn/searing pain, shadow ward, demonic sacrifice, immolate, conflagrate, corruption, siphon/drain life, fear, curses, howl of terror, shadow bolt, life tap, and wand shots.

**UpdateOutOfCombatAI_Warrior**: Sets battle stance, casts battle shout/bloodrage, and charges.

**UpdateInCombatAI_Warrior**: Uses pummel/shield bash, execute, overpower, last stand, concussion blow, shield block/wall/slam, hamstring/piercing howl, rend, intimidating shout, retaliation, recklessness/death wish/berserker rage, mortal strike, bloodthirst, intercept, whirlwind, disarm, and heroic strike.

**UpdateOutOfCombatAI_Rogue**: Applies poisons and casts stealth.

**UpdateInCombatAI_Rogue**: Uses premeditation, garrote/ambush/cheap shot, vanish, combo finishers, blind, adrenaline rush, gouge/kick, evasion, cold blood, blade flurry, backstab, ghostly strike, hemorrhage, sinister strike, and sprint.

**UpdateOutOfCombatAI_Druid**: Casts gift of wild/thorns in wait-join, mark of wild/thorns/nature's grasp, enters cat/bear forms, prowl, moonkin form, and travel form.

**UpdateInCombatAI_Druid**: Uses hibernate, heals, dispels, innervate, barkskin, and form-specific rotations (cat: pounce/ravage/shred/rip; bear: feral charge/bash/swipe/maul; moonkin: entangling roots/moonfire/starfire/wrath).

---

<!-- machine-true, projected from graph.json -->

## Map — BattleBotAI.Main

*Source:* BattleBotAI.cpp, BattleBotAI.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| BattleBotAI | ctor | — | ChatHandler.PlayerBotMgr/AddBattleBot | — |
| OnSessionLoaded | method | — | — | — |
| GetMountSpellId | method | Unit.Main/GetClass, Unit.Main/GetLevel, Unit.Main/GetRace | — | — |
| UseMount | method | BattleGround/GetStatus, Player.Main/GetBattleGround, SpellCaster/CastSpell#2, Unit.Main/GetClass, Unit.Main/GetDisplayId, Unit.Main/GetNativeDisplayId, Unit.Main/HasAura#2, Unit.Main/IsMounted, WorldObject.Object/IsMoving | BattleBotAI.BattleBotWaypoints/AtCaveExit | — |
| DrinkAndEat | method | BattleBotAI.BattleBotWaypoints/ClearPath, BattleGround/GetStatus, Creature.MotionMaster/GetCurrentMovementGeneratorType, Player.Main/GetBattleGround, Player.Main/RemoveSpellCooldown, SpellCaster/CastSpell, SpellMgr/GetSpellEntry, SpellMgr/Instance, Unit.Main/GetHealthPercent, Unit.Main/GetMotionMaster, Unit.Main/GetPowerPercent, Unit.Main/GetPowerType, Unit.Main/GetVictim, Unit.Main/HasAura#2, Unit.Main/IsMounted | — | — |
| GetMaxAggroDistanceForMap | method | BattleGround/GetTypeID, Player.Main/GetBattleGround | — | — |
| AttackStart | method | BattleBotAI.BattleBotWaypoints/ClearPath, CombatBotBaseAI/IsRangedDamageClass, Creature.MotionMaster/MoveChase, Unit.Main/Attack, Unit.Main/GetClass, Unit.Main/GetMotionMaster, Unit.Main/GetPowerPercent, Unit.Main/HasDistanceCasterMovement, Unit.Main/IsMounted, Unit.Main/RemoveSpellsCausingAura, Unit.Main/SetCasterChaseDistance, WorldObject.Object/GetCombatDistance | — | — |
| ShouldIgnoreCombat | method | Unit.Main/HasAura#2, Unit.Main/IsRooted | BattleBotAI.BattleBotWaypoints/MoveToNextPoint, BattleBotAI.BattleBotWaypoints/MoveToNextPointSpecial | — |
| SelectAttackTarget | method | CombatBotBaseAI/IsValidHostileTarget, Group/GetFirstMember, GroupReference/next, HostileReference/next, HostileRefManager/getFirst, Player.Main/GetGroup, Player.Main/GetTeam, ThreatManager/getSourceUnit, Unit.Main/GetAttackerForHelper, Unit.Main/GetHealth, Unit.Main/GetHostileRefManager, Unit.Main/HasAura#2, WorldObject.Object/GetAlivePlayerListInRange, WorldObject.Object/GetDistance#3, WorldObject.Object/GetDistanceZ, WorldObject.Object/IsWithinDist, WorldObject.Object/IsWithinLOSInMap | — | — |
| SelectFollowTarget | method | CombatBotBaseAI/IsHealerClass, CombatBotBaseAI/IsStealthClass, Player.Main/GetTeam, Player.Main/IsGameMaster, Unit.Main/GetClass, Unit.Main/HasAura#2, Unit.Main/IsMounted, WorldObject.Object/GetAlivePlayerListInRange, WorldObject.Object/GetDistance2d#3, WorldObject.Object/GetDistanceZ | — | — |
| DoGraveyardJump | method | BattleGround/GetTypeID, Player.Main/GetBattleGround, Player.Main/GetTeam, Unit.Main/HasUnitState, Unit.Main/SendMovementPacket, WorldObject.Object/Relocate#2, WorldObject.Object/SetUnitMovementFlags | BattleBotAI.BattleBotWaypoints/WSG_AtAllianceGraveyard, BattleBotAI.BattleBotWaypoints/WSG_AtHordeGraveyard | — |
| StopMoving | method | Creature.MotionMaster/MoveIdle, MotionMaster/Clear, Unit.Main/GetMotionMaster, Unit.Main/StopMoving | — | — |
| OnPacketReceived | method | ByteBuffer/contents, CombatBotBaseAI/OnPacketReceived, Player.Main/GetSession, WorldObject.Object/GetMapId, WorldPacket/GetOpcode, WorldSession.Main/QueuePacket | — | — |
| OnPlayerLogin | method | WorldObject.Object/SetFlag | — | — |
| UpdateWaypointMovement | method | BattleBotAI.BattleBotWaypoints/StartNewPathFromAnywhere, BattleBotAI.BattleBotWaypoints/StartNewPathFromBeginning, BattleBotAI.BattleBotWaypoints/StartNewPathToObjective, BattleGround/GetStatus, Creature.MotionMaster/GetCurrentMovementGeneratorType, Player.Main/GetBattleGround, Unit.Main/GetMotionMaster, Unit.Main/HasUnitState, Unit.Main/IsStopped, WorldObject.Object/IsMoving | — | — |
| OnJustDied | method | BattleBotAI.BattleBotWaypoints/ClearPath, Creature.MotionMaster/GetCurrentMovementGeneratorType, Unit.Main/GetMotionMaster | — | — |
| OnJustRevived | method | CombatBotBaseAI/SummonPetIfNeeded, Unit.Main/SelectRandomUnfriendlyTarget | — | — |
| OnEnterBattleGround | method | BattleGround/GetStatus, BattleGround/GetTypeID, CombatBotBaseAI/SummonPetIfNeeded, Creature.MotionMaster/MovePoint, Player.Main/GetBattleGround, Player.Main/GetTeam, shared_Util/frand, shared_Util/urand, Unit.Main/GetMotionMaster | — | — |
| OnLeaveBattleGround | method | BattleBotAI.BattleBotWaypoints/ClearPath, Creature.MotionMaster/GetCurrentMovementGeneratorType, Unit.Main/GetMotionMaster | — | — |
| CheckForUnreachableTarget | method | Creature.MotionMaster/GetCurrentMovementGeneratorType, MotionMaster/GetCurrent, MovementGenerator/IsReachable, Object/IsCreature, Unit.Main/AttackStop, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/GetMotionMaster, WorldObject.Object/GetDistanceZ, WorldObject.Object/GetPosition#3, WorldObject.Object/IsMoving, WorldObject.Object/IsWithinDist | — | — |
| UpdateAI | method | BattleBotAI.BattleBotWaypoints/ClearPath, BattleGround/GetStatus, ChatHandler.Chat/ChatHandler#2, ChatHandler.MiscCommands/HandleGoAlteracCommand, ChatHandler.MiscCommands/HandleGoArathiCommand, ChatHandler.MiscCommands/HandleGoWarsongCommand, CombatBotBaseAI/AddAllSpellReagents, CombatBotBaseAI/AutoAssignRole, CombatBotBaseAI/AutoEquipGear, CombatBotBaseAI/BreakCrowdControlEffects, CombatBotBaseAI/IsValidHostileTarget, CombatBotBaseAI/LearnPremadeSpecForClass, CombatBotBaseAI/PopulateSpellData, CombatBotBaseAI/ResetSpellData, CombatBotBaseAI/SendBattlefieldPortPacket, CombatBotBaseAI/SendBattlemasterJoinPacket, CombatBotBaseAI/SummonPetIfNeeded, Creature.MotionMaster/GetCurrentMovementGeneratorType, Creature.MotionMaster/MoveFollow, Log.Main/Out, Map.Main/HaveRealPlayers, MotionMaster/GetCurrent, Object/GetObjectGuid, Object/IsInWorld, Object/ToggleFlag, ObjectGuid/operator==, Player.Main/BuildPlayerRepop, Player.Main/GetBattleGround, Player.Main/GiveLevel, Player.Main/InBattleGround, Player.Main/InBattleGroundQueue, Player.Main/InitTalentForLevel, Player.Main/IsBeingTeleported, Player.Main/RepopAtGraveyard, Player.Main/ResurrectPlayer, Player.Main/SpawnCorpseBones, Player.Main/UpdateSkillsToMaxSkillsForLevel, Player.Main/UpdateZone, shared_Util/frand, shared_Util/urand, ShortTimeTracker/Passed, ShortTimeTracker/Reset, ShortTimeTracker/Update, SpellCaster/GetCurrentSpell, SpellCaster/InterruptSpell, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/AttackStop, Unit.Main/ClearTarget, Unit.Main/GetClass, Unit.Main/GetDeathState, Unit.Main/GetLevel, Unit.Main/GetMotionMaster, Unit.Main/GetPowerType, Unit.Main/GetSheath, Unit.Main/GetStandState, Unit.Main/GetTargetGuid, Unit.Main/GetVictim, Unit.Main/HasUnitState, Unit.Main/IsAlive, Unit.Main/IsDead, Unit.Main/IsInCombat, Unit.Main/IsMounted, Unit.Main/IsVisibleForOrDetect, Unit.Main/SendMovementPacket, Unit.Main/SetHealthPercent, Unit.Main/SetInFront, Unit.Main/SetPowerPercent, Unit.Main/SetSheath, Unit.Main/SetStandState, Unit.Main/StopMoving, World/getConfig#4, WorldObject.Object/GetCombatDistance, WorldObject.Object/GetMap, WorldObject.Object/GetZoneAndAreaId, WorldObject.Object/HasInArc, WorldObject.Object/IsMoving, WorldObject.Object/IsWithinDist, WorldObject.Object/RemoveFlag, WorldObject.Object/SetUInt32Value | — | — |
| UpdateBattleGroundAI | method | BattleGround/GetTypeID, GameObject/Use, Player.Main/GetBattleGround, Player.Main/GetTeam, WorldObject.Object/FindNearestGameObject | — | — |
| UpdateFlagCarrierAI | method | CombatBotBaseAI/CanTryToCastSpell, SpellCaster/CastSpell, SpellEntry/IsTargetInRange, Unit.Main/GetAttackerForHelper, Unit.Main/GetClass, Unit.Main/GetHealthPercent, Unit.Main/GetShapeshiftForm, Unit.Main/HasAura#2, Unit.Main/HasAuraType, Unit.Main/RemoveAurasDueToSpellByCancel, Unit.Main/RemoveSpellsCausingAura, WorldObject.Object/IsMoving | — | — |
| UpdateOutOfCombatAI | method | Unit.Main/GetClass | — | — |
| UpdateInCombatAI | method | CombatBotBaseAI/UseTrinketEffects, Unit.Main/GetClass, Unit.Main/GetVictim | — | — |
| UpdateOutOfCombatAI_Paladin | method | CombatBotBaseAI/CanTryToCastSpell, CombatBotBaseAI/DoCastSpell, CombatBotBaseAI/FindAndHealInjuredAlly, CombatBotBaseAI/SelectBuffTarget, SpellCaster/HasGCD | — | — |
| UpdateInCombatAI_Paladin | method | CombatBotBaseAI/CanTryToCastSpell, CombatBotBaseAI/DoCastSpell, CombatBotBaseAI/FindAndHealInjuredAlly, CombatBotBaseAI/GetAttackersInRangeCount, CombatBotBaseAI/IsMeleeDamageClass, CombatBotBaseAI/IsPhysicalDamageClass, CombatBotBaseAI/SelectDispelTarget, Creature.MotionMaster/GetCurrentMovementGeneratorType, Creature.MotionMaster/MoveChase, Object/IsCreature, SpellCaster/CastSpell, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/FindLowestHpFriendlyUnit, Unit.Main/GetClass, Unit.Main/GetCreatureType, Unit.Main/GetHealthPercent, Unit.Main/GetMotionMaster, Unit.Main/GetPowerPercent, Unit.Main/GetVictim, Unit.Main/HasAura#2, Unit.Main/HasAuraType, Unit.Main/HasUnitState, Unit.Main/IsTotalImmune | — | — |
| UpdateOutOfCombatAI_Shaman | method | CombatBotBaseAI/CanTryToCastSpell, CombatBotBaseAI/CastWeaponBuff, CombatBotBaseAI/DoCastSpell, CombatBotBaseAI/SummonShamanTotems, Unit.Main/GetVictim, Unit.Main/HasAura#2, Unit.Main/IsMounted, WorldObject.Object/IsMoving | — | — |
| UpdateInCombatAI_Shaman | method | CombatBotBaseAI/CanTryToCastSpell, CombatBotBaseAI/DoCastSpell, CombatBotBaseAI/FindAndHealInjuredAlly, CombatBotBaseAI/IsValidDispelTarget, CombatBotBaseAI/SummonShamanTotems, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/GetAttackers, Unit.Main/GetPowerPercent, Unit.Main/GetShapeshiftForm, Unit.Main/GetVictim, Unit.Main/RemoveAurasDueToSpellByCancel, WorldObject.Object/IsMoving | — | — |
| UpdateOutOfCombatAI_Hunter | method | CombatBotBaseAI/CanTryToCastSpell, CombatBotBaseAI/DoCastSpell, Creature.Main/AI, CreatureAI/AttackStart, Unit.Main/GetCharmInfo, Unit.Main/GetPet, Unit.Main/GetVictim, Unit.Main/IsMounted, Unit.Main/SetIsCommandAttack | — | — |
| UpdateInCombatAI_Hunter | method | CombatBotBaseAI/AddHunterAmmo, CombatBotBaseAI/CanTryToCastSpell, CombatBotBaseAI/DoCastSpell, Creature.MotionMaster/GetCurrentMovementGeneratorType, Creature.MotionMaster/MoveChase, Creature.MotionMaster/MoveDistance, MotionMaster/Clear, Player.Main/HasSpell, SpellCaster/CastSpell#2, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/GetMotionMaster, Unit.Main/GetVictim, Unit.Main/HasAura#2, Unit.Main/HasUnitState, Unit.Main/IsStopped, Unit.Main/StopMoving, WorldObject.Object/GetCombatDistance, WorldObject.Object/GetDistance#3, WorldObject.Object/IsMoving | — | — |
| UpdateOutOfCombatAI_Mage | method | CombatBotBaseAI/CanTryToCastSpell, CombatBotBaseAI/DoCastSpell, Unit.Main/GetVictim | — | — |
| UpdateInCombatAI_Mage | method | CombatBotBaseAI/CanTryToCastSpell, CombatBotBaseAI/DoCastSpell, CombatBotBaseAI/GetAttackersInRangeCount, CombatBotBaseAI/IsPhysicalDamageClass, CombatBotBaseAI/IsValidDispelTarget, CombatBotBaseAI/SelectAttackerDifferentFrom, Creature.MotionMaster/GetCurrentMovementGeneratorType, Creature.MotionMaster/MoveChase, Creature.MotionMaster/MoveDistance, Creature.MotionMaster/MoveIdle, Player.Main/HasSpell, SpellCaster/CastSpell#2, SpellCaster/GetCurrentSpell, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/GetAttackers, Unit.Main/GetClass, Unit.Main/GetHealthPercent, Unit.Main/GetMotionMaster, Unit.Main/GetPowerPercent, Unit.Main/GetVictim, Unit.Main/HasAura#2, Unit.Main/HasAuraType, Unit.Main/HasUnitState, WorldObject.Object/GetDistance#3, WorldObject.Object/IsMoving | — | — |
| UpdateOutOfCombatAI_Priest | method | BattleGround/GetStatus, CombatBotBaseAI/CanTryToCastSpell, CombatBotBaseAI/DoCastSpell, CombatBotBaseAI/IsValidBuffTarget, CombatBotBaseAI/SelectBuffTarget, Player.Main/GetBattleGround, SpellCaster/HasGCD, Unit.Main/GetVictim | — | — |
| UpdateInCombatAI_Priest | method | CombatBotBaseAI/CanTryToCastSpell, CombatBotBaseAI/DoCastSpell, CombatBotBaseAI/FindAndHealInjuredAlly, CombatBotBaseAI/GetAttackersInRangeCount, CombatBotBaseAI/IsValidDispelTarget, CombatBotBaseAI/SelectDispelTarget, Creature.MotionMaster/GetCurrentMovementGeneratorType, Creature.MotionMaster/MoveChase, Player.Main/HasSpell, SpellCaster/CastSpell#2, SpellCaster/GetCurrentSpell, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/GetMotionMaster, Unit.Main/GetPowerPercent, Unit.Main/GetPowerType, Unit.Main/GetShapeshiftForm, Unit.Main/GetVictim, Unit.Main/HasAuraType, WorldObject.Object/GetDistance#3, WorldObject.Object/IsMoving | — | — |
| UpdateOutOfCombatAI_Warlock | method | BattleGround/GetStatus, CombatBotBaseAI/CanTryToCastSpell, CombatBotBaseAI/DoCastSpell, CombatBotBaseAI/SelectBuffTarget, CombatBotBaseAI/SummonPetIfNeeded, Creature.Main/AI, CreatureAI/AttackStart, Player.Main/GetBattleGround, SpellCaster/HasGCD, Unit.Main/GetCharmInfo, Unit.Main/GetPet, Unit.Main/GetVictim, Unit.Main/SetIsCommandAttack | — | — |
| UpdateInCombatAI_Warlock | method | CombatBotBaseAI/CanTryToCastSpell, CombatBotBaseAI/DoCastSpell, CombatBotBaseAI/GetAttackersInRangeCount, Creature.MotionMaster/GetCurrentMovementGeneratorType, Creature.MotionMaster/MoveChase, Player.Main/HasSpell, SpellCaster/CastSpell#2, SpellCaster/GetCurrentSpell, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/GetClass, Unit.Main/GetHealthPercent, Unit.Main/GetMotionMaster, Unit.Main/GetPet, Unit.Main/GetPowerPercent, Unit.Main/GetVictim, Unit.Main/IsAlive, Unit.Main/IsCaster, WorldObject.Object/GetDistance#3, WorldObject.Object/IsMoving | — | — |
| UpdateOutOfCombatAI_Warrior | method | CombatBotBaseAI/CanTryToCastSpell, CombatBotBaseAI/DoCastSpell, Unit.Main/GetPower, Unit.Main/GetVictim, Unit.Main/HasAura#2 | — | — |
| UpdateInCombatAI_Warrior | method | CombatBotBaseAI/CanTryToCastSpell, CombatBotBaseAI/DoCastSpell, CombatBotBaseAI/GetAttackersInRangeCount, CombatBotBaseAI/IsMeleeDamageClass, CombatBotBaseAI/IsMeleeWeaponClass, CombatBotBaseAI/IsPhysicalDamageClass, CombatBotBaseAI/IsWearingShield, Creature.MotionMaster/GetCurrentMovementGeneratorType, Creature.MotionMaster/MoveChase, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/GetAttackers, Unit.Main/GetClass, Unit.Main/GetHealthPercent, Unit.Main/GetMotionMaster, Unit.Main/GetPower, Unit.Main/GetShapeshiftForm, Unit.Main/GetVictim, Unit.Main/HasAuraType, Unit.Main/HasUnitState, Unit.Main/IsImmuneToMechanic, WorldObject.Object/GetCombatDistance, WorldObject.Object/IsMoving | — | — |
| UpdateOutOfCombatAI_Rogue | method | CombatBotBaseAI/CanTryToCastSpell, CombatBotBaseAI/CastWeaponBuff, CombatBotBaseAI/DoCastSpell, Unit.Main/GetVictim, Unit.Main/HasAura#2 | — | — |
| UpdateInCombatAI_Rogue | method | CombatBotBaseAI/CanTryToCastSpell, CombatBotBaseAI/DoCastSpell, CombatBotBaseAI/GetAttackersInRangeCount, CombatBotBaseAI/IsRangedDamageClass, CombatBotBaseAI/SelectAttackerDifferentFrom, Creature.MotionMaster/MoveDistance, Player.Main/GetComboPoints, SpellCaster/IsNonMeleeSpellCasted, SpellCaster/IsSpellReady#2, Unit.Main/AttackStop, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/GetClass, Unit.Main/GetHealthPercent, Unit.Main/GetMotionMaster, Unit.Main/GetPower, Unit.Main/GetVictim, Unit.Main/HasAuraType, Unit.Main/HasUnitState, Unit.Main/IsCaster | — | — |
| UpdateOutOfCombatAI_Druid | method | BattleGround/GetStatus, CombatBotBaseAI/CanTryToCastSpell, CombatBotBaseAI/DoCastSpell, CombatBotBaseAI/FindAndHealInjuredAlly, CombatBotBaseAI/SelectBuffTarget, Player.Main/GetBattleGround, SpellCaster/HasGCD, Unit.Main/GetPowerPercent, Unit.Main/GetShapeshiftForm, Unit.Main/GetVictim, Unit.Main/HasAura#2, Unit.Main/IsMounted, Unit.Main/RemoveAurasDueToSpellByCancel | — | — |
| UpdateInCombatAI_Druid | method | CombatBotBaseAI/CanTryToCastSpell, CombatBotBaseAI/DoCastSpell, CombatBotBaseAI/FindAndHealInjuredAlly, CombatBotBaseAI/GetAttackersInRangeCount, CombatBotBaseAI/IsMeleeDamageClass, CombatBotBaseAI/IsPhysicalDamageClass, CombatBotBaseAI/IsValidDispelTarget, CombatBotBaseAI/SelectDispelTarget, Creature.MotionMaster/GetCurrentMovementGeneratorType, Creature.MotionMaster/MoveChase, Creature.MotionMaster/MoveDistance, Player.Main/GetComboPoints, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/GetAttackers, Unit.Main/GetClass, Unit.Main/GetHealthPercent, Unit.Main/GetMotionMaster, Unit.Main/GetPower, Unit.Main/GetPowerPercent, Unit.Main/GetShapeshiftForm, Unit.Main/GetVictim, Unit.Main/HasAuraType, Unit.Main/HasDistanceCasterMovement, Unit.Main/HasUnitState, Unit.Main/RemoveAurasDueToSpellByCancel, Unit.Main/RemoveSpellsCausingAura, Unit.Main/SetCasterChaseDistance, WorldObject.Object/GetDistance#3, WorldObject.Object/IsMoving | — | — |
