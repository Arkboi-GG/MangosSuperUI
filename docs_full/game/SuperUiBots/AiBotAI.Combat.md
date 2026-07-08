<!-- provenance: boundary-bleed -->
# AiBotAI.Combat

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AiBotAI.Combat

## Purpose & Responsibilities

`AiBotAI.Combat` (implemented in `AiBotAICombat.cpp`) constitutes the combat domain of the autonomous AI bot. It is responsible for:

1.  **Self-Maintenance:** Managing mounts, eating, and drinking to maintain mobility and resource levels.
2.  **Engagement Discipline:** Preventing fatal errors through pull gating (`OverpullGuard`, `PullReady`) and emergency retreats (`HandleOverpullRetreat`) when the bot is overwhelmed.
3.  **Deadlock Resolution:** Detecting and breaking combat stalemates caused by navigation mesh failures or unreachable targets (`HandleCombatStalemate`).
4.  **Target Selection:** Choosing valid attack targets, filtering out ignored entities, and handling assist logic for group play (`SelectAttackTarget`, `IsValidAssistTarget`).
5.  **Class-Specific Behavior:** Dispatching and implementing the spell rotation, movement, and defensive logic for all nine playable classes (Paladin, Shaman, Hunter, Mage, Priest, Warlock, Warrior, Rogue, Druid) in both in-combat and out-of-combat states.

This unit does not handle the main AI loop (`UpdateAI`), network bridging, or pathfinding generation; it relies on `AiBotAI.Main`, `AiBotAI.Bridge`, and `AiBotAI.Movement` for those concerns.

## Member-by-Member Behavior

### Self-Maintenance Utilities

**GetMountSpellId**
Determines the correct mount spell ID for the bot based on its level (40 or 60), class (Paladin/Warlock have class-specific mounts), and race. Returns 0 if no mount is applicable.

**UseMount**
Attempts to summon a mount. It performs several safety checks before casting:
*   The bot must not already be mounted.
*   The bot must not be moving.
*   The bot must not be in a transformed state (DisplayId != NativeDisplayId).
*   Rogues are excluded from mounting.
*   The bot must not be stealthed.
If all checks pass, it casts the spell returned by `GetMountSpellId`.

**DrinkAndEat**
Manages health and mana restoration. It is called by `AiBotAI.Main/UpdateAI`.
*   It refuses to act if the bot is currently buffing, mounted, or has a combat victim.
*   It calculates needs based on `GetHealthPercent` and `GetPowerPercent` (for Mana users).
*   If eating/drinking is needed and not currently active, it stops movement (`AiBotAI.Movement/StopMoving`), retrieves the spell entry from `SpellMgr`, casts the spell, and removes its cooldown.
*   Returns `true` if action was taken or if resources are still needed.

### Engagement Discipline & Targeting

**GetMaxAggroDistanceForMap**
Returns a constant float value of `50.0f`. This defines the maximum distance at which the bot considers aggroing enemies in open-world scenarios.

**IsCombatIgnored**
Checks if a specific unit GUID is currently in the `m_combatIgnore` map. This map is populated by stalemate and overpull handlers to prevent the bot from immediately re-engaging a target it just fled from. It is called by `AiBotAI.Grind/ScanApproachTarget` and `AiBotAI.Grind/SelectGrindTarget` to filter out recently abandoned targets.

**IsValidAssistTarget**
A public seam for team play logic. It validates if a target is alive and considered a valid hostile target by the base AI (`CombatBotBaseAI/IsValidHostileTarget`). It is called by `AiBotDoctrineTeam/ResolveFocus` to determine if a follower should assist the anchor's current target.

**OverpullGuard**
Prevents the bot from pulling targets that are surrounded by too many other hostiles.
*   If the target is neutral (not hostile to the bot), it returns `false` (safe to pull, as neutrals don't aggro neighbors).
*   If hostile, it counts nearby hostiles using `AiBotAI.Grind/CountNearbyHostiles`.
*   It compares the count against a cap: `AIBOT_OVERPULL_GROUP` (6) if in a group, or `AIBOT_OVERPULL_SOLO` (3) if solo.
*   Returns `true` if the density exceeds the cap, signaling the caller to hold the pull. It is called by `AiBotDoctrineSolo/HoldPull` and `AiBotDoctrineTeam/HoldPull`.

**PullReady**
A gate for *initiating* new combat. It ensures the bot has sufficient resources before starting a fight.
*   Requires Health > `AIBOT_PULL_MIN_HP` (70%).
*   For Mana users, requires Mana > `AIBOT_PULL_MIN_MANA` (50%).
*   Note: This does *not* prevent defending oneself if attacked; it only gates proactive pulls.

**SelectAttackTarget**
Selects the next target to attack.
1.  **Hostile List:** Iterates through the bot's current hostile references. It filters out the excluded unit (`pExcept`), invalid targets, ignored targets (`IsCombatIgnored`), and those out of visibility range. It sorts valid candidates by distance and returns the closest.
2.  **Party Assist:** If in a group, it checks party members within 30 yards. If a party member is attacking a valid, non-ignored target within line-of-sight and reasonable vertical distance, it returns that target to assist.
3.  **Fallback:** Returns `nullptr` if no suitable target is found.

**AttackStart**
Initiates combat with a specific victim.
*   Disables buffing state.
*   Removes mount auras if mounted.
*   Calls `Unit.Main/Attack`.
*   Configures chase distance: Ranged DPS/Healers get a 25-yard chase distance if they have mana and are out of melee range; melee classes get standard chase behavior.
*   Sets the motion master to `MoveChase`.

**CheckForUnreachableTarget**
Handles cases where the bot is chasing a target it cannot reach due to geometry or navmesh issues.
*   If the chase movement generator reports the target is unreachable:
    *   If the target is far away (> visibility distance), it stops attacking and moving.
    *   If the target is a creature and the bot is stationary, it "cheats" by teleporting directly to the target's coordinates (`NearTeleportTo`) after grounding the Z coordinate (`AiBotAI.Movement/ReGroundZ`). This prevents soft-locks on floating mobs or bad mmaps.
    *   If the vertical distance is > 10 yards, it stops attacking and moving.

### Emergency Escape Routines

**HandleCombatStalemate**
Detects and resolves situations where the bot is in combat but neither side is dealing damage (e.g., bot stuck on a navmesh seam, target behind geometry).
*   **No Victim Fix:** If `GetVictim()` is null (e.g., post-kill lag), it resets health tracking and returns false to avoid false positives.
*   **Detection:** Tracks bot and victim health. If neither changes for `AIBOT_STALEMATE_NUDGE_MS` (3s), it triggers a response.
*   **Stage 1 (Nudge):** Performs a short hop (`AIBOT_STALEMATE_NUDGE_DIST`) away from the target or towards the task destination. It temporarily ignores the target GUID to prevent immediate re-aggression.
*   **Stage 2 (Disengage/Flee):** If nudges fail (`AIBOT_STALEMATE_MAX_NUDGES`), it stops combat, ignores the target for 60s, and flees 30 yards away.
*   **Hard Escape:** If flees fail (`AIBOT_STALEMATE_MAX_DISENGAGES`), it teleports to a known safe location (nav boundary outer point or spawn point) and sends a `MOVE_FAILED` event to the C# bridge (`AiBotAI.Bridge/BridgeSendEvent`).

**HandleOverpullRetreat**
Executes a retreat when the bot is overwhelmed by too many attackers. Called by `AiBotAI.Main/UpdateAI`.
*   Counts current attackers. If the count is within the cap (`AIBOT_OVERPULL_GROUP`/`SOLO`), it resets the flee counter.
*   If the count exceeds the cap and max flees haven't been reached:
    *   Calculates the centroid of all attackers.
    *   Moves 30 yards away from this centroid.
    *   Adds all attackers to the `m_combatIgnore` map temporarily.
    *   Stops attacking and moves to the retreat point (`AiBotAI.Movement/MovePointRun`).

### Class-Specific Combat Logic

The following methods implement the spell rotations and behaviors for each class. They are dispatched by `UpdateInCombatAI` and `UpdateOutOfCombatAI`.

**UpdateOutOfCombatAI / UpdateInCombatAI**
Dispatchers that call the specific class implementation based on `me->GetClass()`. `UpdateInCombatAI` also calls `CombatBotBaseAI/UseTrinketEffects` if a victim exists.

**UpdateOutOfCombatAI_Paladin / UpdateInCombatAI_Paladin**
*   **OOC:** Applies auras, blessings, and heals injured allies.
*   **ICC:** Uses Divine Shield if low HP/high mana. Maintains seals. Casts Judgement, Hammer of Justice (on casters), Hammer of Wrath (execute), Holy Shield, Consecration (AoE), Holy Shock, Exorcism (undead). Handles defensive cooldowns (Blessing of Freedom, Cleanse) and healing (Lay on Hands).

**UpdateOutOfCombatAI_Shaman / UpdateInCombatAI_Shaman**
*   **OOC:** Weapon buffs, Lightning Shield, Ghost Wolf (travel form).
*   **ICC:** Mana Tide Totem (low mana), Elemental Mastery, Earth Shock (casters), Frost Shock (moving targets), Stormstrike, Chain Lightning, Purge (dispel), Flame Shock, Lightning Bolt. Summons totems and heals.

**UpdateOutOfCombatAI_Hunter / UpdateInCombatAI_Hunter**
*   **OOC:** Aspect of the Cheetah, Hunter's Mark, commands pet to attack.
*   **ICC:** Auto-shot management (adds ammo if needed). Concussive Shot (moving targets), Aimed Shot, Arcane Shot, Serpent Sting, Multi-Shot. Swaps aspects based on range (Monkey for melee, Hawk for ranged). Uses melee abilities (Mongoose Bite, Raptor Strike) if rooted or in melee. Maintains distance if needed.

**UpdateOutOfCombatAI_Mage / UpdateInCombatAI_Mage**
*   **OOC:** Arcane Brilliance/Intellect, Ice Armor/Barrier.
*   **ICC:** Combustion, Pyroblast (with Presence of Mind), Ice Block (low HP), Mana Shield (vs physical), Counterspell, Cone of Cold (close melee), Blink (escape), Frost Nova, Blast Wave/Arcane Explosion (AoE), Polymorph, Scorch (execute), Frostbolt, Fireball, Evocation (mana regen). Wand shoots if mana is critical.

**UpdateOutOfCombatAI_Priest / UpdateInCombatAI_Priest**
*   **OOC:** BattleGround buffs (Prayer of Fortitude/Spirit/Shadow Protection) or personal buffs (Power Word Fortitude, Divine Spirit, Inner Fire).
*   **ICC:** Power Word Shield, Inner Focus (low mana), Healing, Dispel Magic/Abolish Disease. Shadowform, Silence (casters), Vampiric Embrace, Mind Blast, Shadow Word Pain, Devouring Plague, Psychic Scream (AoE fear), Mana Burn, Mind Flay, Smite. Wand shoots if mana is critical.

**UpdateOutOfCombatAI_Warlock / UpdateInCombatAI_Warlock**
*   **OOC:** Detect Invisibility (BG), Demon Armor, summons pet if missing.
*   **ICC:** Death Coil, Shadowburn (execute), Searing Pain (execute), Shadow Ward (vs Warlocks), Demonic Sacrifice, Immolate, Conflagrate, Corruption, Siphon Life/Drain Life (self-heal), Fear, Curse of Tongues/Exhaustion, Howl of Terror (AoE), Shadow Bolt, Life Tap. Wand shoots if mana is critical.

**UpdateOutOfCombatAI_Warrior / UpdateInCombatAI_Warrior**
*   **OOC:** Battle Stance, Battle Shout/Bloodrage, Charge.
*   **ICC:** Pummel/Shield Bash (casters), Execute, Overpower, Last Stand (low HP), Concussion Blow, Shield Block/Wall/Slam (Defensive Stance), Hamstring/Piercing Howl (moving targets), Rend (vs Rogues), Intimidating Shout (AoE fear), Retaliation, Recklessness/Death Wish/Berserker Rage (burst), Mortal Strike, Bloodthirst, Intercept, Whirlwind, Disarm, Heroic Strike.

**UpdateOutOfCombatAI_Rogue / UpdateInCombatAI_Rogue**
*   **OOC:** Poisons, Stealth.
*   **ICC:** Premeditation (stealth), Garrote/Ambush/Cheap Shot (stealth opener). Vanish (escape low HP). Combo point spenders (Slice and Dice, Eviscerate, etc.). Blind (secondary target), Adrenaline Rush, Gouge/Kick (casters), Evasion, Cold Blood, Blade Flurry, Backstab, Ghostly Strike, Hemorrhage, Sinister Strike, Sprint (gap closer).

**UpdateOutOfCombatAI_Druid / UpdateInCombatAI_Druid**
*   **OOC:** BattleGround buffs (Gift of the Wild, Thorns) or personal buffs (Mark of the Wild, Thorns). Nature's Grasp. Form switching (Cat/Bear for melee/tank, Travel Form for movement).
*   **ICC:** Hibernate (beasts), Healing, Dispels, Innervate (low mana).
    *   **Cat Form:** Pounce/Ravage/Tiger's Fury (stealth), Ferocious Bite/Rip (combo points), Faerie Fire, Dash, Shred, Rake, Claw.
    *   **Bear Form:** Feral Charge, Bash, Frenzied Regeneration, Faerie Fire, Demoralizing Roar, Swipe, Maul.
    *   **Moonkin/None:** Entangling Roots, Faerie Fire, Insect Swarm, Moonfire, Starfire, Wrath.

## Cross-Unit Boundaries

*   **AiBotAI.Movement:**
    *   `StopMoving`, `MovePointRun`, `ReGroundZ`, `FindNavBoundaryNear` are called by `HandleCombatStalemate`, `HandleOverpullRetreat`, `DrinkAndEat`, and `CheckForUnreachableTarget` to control the bot's physical position during escapes, nudges, and self-maintenance.
*   **AiBotAI.Bridge:**
    *   `BridgeSendEvent` is called by `HandleCombatStalemate` to notify the C# coordinator of a `MOVE_FAILED` event when a hard teleport escape is necessary.
*   **AiBotAI.Grind:**
    *   `CountNearbyHostiles` is called by `OverpullGuard` to assess threat density.
    *   `IsCombatIgnored` is called by `ScanApproachTarget` and `SelectGrindTarget` to ensure the bot doesn't re-engage targets it has recently fled.
*   **AiBotDoctrineTeam / AiBotDoctrineSolo:**
    *   `IsValidAssistTarget` is called by `AiBotDoctrineTeam/ResolveFocus` to validate assist targets.
    *   `OverpullGuard` is called by `HoldPull` methods in both doctrines to gate pull initiation.
*   **CombatBotBaseAI:**
    *   Various helper methods (`IsValidHostileTarget`, `CanTryToCastSpell`, `DoCastSpell`, `SelectBuffTarget`, `FindAndHealInjuredAlly`, `GetAttackersInRangeCount`, `IsMeleeDamageClass`, `IsPhysicalDamageClass`, `SelectDispelTarget`, `CastWeaponBuff`, `SummonShamanTotems`, `AddHunterAmmo`, `UseTrinketEffects`, `SelectAttackerDifferentFrom`, `IsRangedDamageClass`, `IsWearingShield`, `IsMeleeWeaponClass`) are called extensively by the class-specific update methods to manage spell casting, target selection, and damage classification.
*   **Unit.Main / WorldObject.Object / SpellCaster / Player.Main / Creature.MotionMaster:**
    *   Standard engine APIs are used for state queries (health, power, position, movement status) and actions (casting spells, attacking, moving).

## Data Model

This unit does not directly interact with any database tables. It operates entirely on in-memory game state and configuration constants.

## Notable Implementation Details

*   **Stalemate Escalation:** The `HandleCombatStalemate` logic has evolved significantly. Early versions simply stopped combat in place, which failed if the mob remained in aggro range. The current implementation uses a multi-stage approach: Nudge -> Flee -> Teleport. This ensures the bot physically leaves the aggro radius or bypasses the navmesh failure entirely.
*   **No-Victim Stalemate Fix:** A specific fix prevents the stalemate detector from triggering when `GetVictim()` is null (e.g., during the brief window after a kill). Without this, the bot would accumulate stalemate time against "guid 0," leading to unnecessary nudges and state corruption.
*   **Overpull Guard Context:** `OverpullGuard` behaves differently for solo vs. grouped bots. Solo bots have a strict cap of 3 nearby hostiles, while groups allow up to 6. This reflects the increased survivability of a group. Crucially, under the "one-picker" doctrine, the anchor bot's pull commits the whole team, so the group cap is enforced on the anchor to prevent mass deaths.
*   **Class-Specific Spell Rotations:** The class update methods are verbatim ports from `BattleBotAI`. They rely heavily on `CanTryToCastSpell` and `DoCastSpell` wrappers from `CombatBotBaseAI`. The logic prioritizes survival (defensive cooldowns), then crowd control/disruption, then damage/healing.
*   **Mount Restrictions:** `UseMount` explicitly prevents Rogues from mounting and checks for stealth and transformation states. This prevents visual glitches and gameplay violations.
*   **Navigation Cheats:** `CheckForUnreachableTarget` contains a "cheat" where the bot teleports directly to a creature's coordinates if it is stuck chasing an unreachable target. This is a pragmatic solution to navmesh imperfections, ensuring the bot doesn't soft-lock.
*   **BattleGround Specifics:** Several class methods (Priest, Warlock, Druid) check for `BattleGround` status to apply specific buffs (e.g., Prayer of Fortitude, Detect Invisibility, Gift of the Wild) only when appropriate.

## Member Reference

**GetMountSpellId**: Determines the correct mount spell ID based on level, class, and race. Returns 0 if no mount is applicable.

**UseMount**: Checks safety conditions (not mounted, not moving, not rogue, not stealthed, not transformed) and casts the mount spell if valid.

**DrinkAndEat**: Manages health and mana restoration. Stops movement, casts food/drink spells, and removes cooldowns if resources are low and not currently consuming.

**GetMaxAggroDistanceForMap**: Returns a constant value of 50.0f, defining the maximum aggro distance for open-world encounters.

**IsCombatIgnored**: Checks if a unit GUID is in the temporary ignore list, preventing re-engagement of recently fled targets.

**IsValidAssistTarget**: Validates if a target is alive and a valid hostile, serving as a seam for team-play assist logic.

**HandleCombatStalemate**: Detects no-damage combat deadlocks. Executes a staged response: nudge, flee, or hard teleport to escape navmesh/geometry traps.

**OverpullGuard**: Assesses threat density around a target. Returns true if the number of nearby hostiles exceeds the solo/group cap, signaling to hold the pull.

**HandleOverpullRetreat**: Executes a retreat maneuver when overwhelmed by attackers. Moves away from the attacker centroid and temporarily ignores them.

**AttackStart**: Initiates combat with a victim, configuring chase distances and movement generators based on class role.

**SelectAttackTarget**: Selects the best attack target from the hostile list or party assists, filtering out ignored and invalid targets.

**CheckForUnreachableTarget**: Handles chase failures by stopping movement or teleporting to the target if stuck due to navmesh issues.

**UpdateOutOfCombatAI**: Dispatcher that calls the specific out-of-combat logic for the bot's class.

**UpdateInCombatAI**: Dispatcher that calls the specific in-combat logic for the bot's class and uses trinket effects.

**UpdateOutOfCombatAI_Paladin**: Applies auras, blessings, and heals allies out of combat.

**UpdateInCombatAI_Paladin**: Executes Paladin spell rotation: shields, seals, judgements, hammers, consecration, cleanses, and healing.

**UpdateOutOfCombatAI_Shaman**: Applies weapon buffs, lightning shield, and ghost wolf travel form.

**UpdateInCombatAI_Shaman**: Executes Shaman spell rotation: shocks, stormstrike, chain lightning, purges, totems, and healing.

**UpdateOutOfCombatAI_Hunter**: Applies aspects, hunter's mark, and commands pet to attack.

**UpdateInCombatAI_Hunter**: Executes Hunter spell rotation: auto-shot, concussive/aimed/arcan shots, stings, multi-shot, aspect swaps, and melee abilities.

**UpdateOutOfCombatAI_Mage**: Applies arcane brilliance/intellect and ice armor/barrier.

**UpdateInCombatAI_Mage**: Executes Mage spell rotation: combustion, pyroblast, ice block, counterspell, cone of cold, blink, frost nova, blast wave, polymorph, scorch, frostbolt, fireball, evocation.

**UpdateOutOfCombatAI_Priest**: Applies battle-ground or personal buffs and inner fire.

**UpdateInCombatAI_Priest**: Executes Priest spell rotation: power word shield, healing, dispels, shadowform, silence, vampiric embrace, mind blast, shadow word pain, devouring plague, psychic scream, mana burn, mind flay, smite.

**UpdateOutOfCombatAI_Warlock**: Applies detect invisibility (BG), demon armor, and summons pet.

**UpdateInCombatAI_Warlock**: Executes Warlock spell rotation: death coil, shadowburn, searing pain, shadow ward, demonic sacrifice, immolate, conflagrate, corruption, siphon/drain life, fear, curses, howl of terror, shadow bolt, life tap.

**UpdateOutOfCombatAI_Warrior**: Applies battle stance, battle shout/bloodrage, and charges target.

**UpdateInCombatAI_Warrior**: Executes Warrior spell rotation: pummel/shield bash, execute, overpower, last stand, concussion blow, shield block/wall/slam, hamstring/piercing howl, rend, intimidating shout, retaliation, recklessness/death wish/berserker rage, mortal strike, bloodthirst, intercept, whirlwind, disarm, heroic strike.

**UpdateOutOfCombatAI_Rogue**: Applies poisons and stealth.

**UpdateInCombatAI_Rogue**: Executes Rogue spell rotation: premeditation, garrote/ambush/cheap shot, vanish, combo spenders, blind, adrenaline rush, gouge/kick, evasion, cold blood, blade flurry, backstab, ghostly strike, hemorrhage, sinister strike, sprint.

**UpdateOutOfCombatAI_Druid**: Applies battle-ground or personal buffs, nature's grasp, and manages forms (cat/bear/travel).

**UpdateInCombatAI_Druid**: Executes Druid spell rotation based on form: Hibernate, healing, dispels, innervate. Cat: pounce/ravage/tiger's fury, ferocious bite/rip, faerie fire, dash, shred, rake, claw. Bear: feral charge, bash, frenzied regeneration, faerie fire, demoralizing roar, swipe, maul. Moonkin/None: entangling roots, faerie fire, insect swarm, moonfire, starfire, wrath.

---

<!-- machine-true, projected from graph.json -->

## Map — AiBotAI.Combat

*Source:* AiBotAICombat.cpp, AiBotAIMain.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GetMountSpellId | method | Unit.Main/GetClass, Unit.Main/GetLevel, Unit.Main/GetRace | — | — |
| UseMount | method | SpellCaster/CastSpell#2, Unit.Main/GetClass, Unit.Main/GetDisplayId, Unit.Main/GetNativeDisplayId, Unit.Main/HasAura#2, Unit.Main/IsMounted, WorldObject.Object/IsMoving | — | — |
| DrinkAndEat | method | AiBotAI.Movement/StopMoving, Creature.MotionMaster/GetCurrentMovementGeneratorType, Player.Main/RemoveSpellCooldown, SpellCaster/CastSpell, SpellMgr/GetSpellEntry, SpellMgr/Instance, Unit.Main/GetHealthPercent, Unit.Main/GetMotionMaster, Unit.Main/GetPowerPercent, Unit.Main/GetPowerType, Unit.Main/GetVictim, Unit.Main/HasAura#2, Unit.Main/IsMounted | AiBotAI.Main/UpdateAI | — |
| GetMaxAggroDistanceForMap | method | — | — | — |
| IsCombatIgnored | method | — | AiBotAI.Grind/ScanApproachTarget, AiBotAI.Grind/SelectGrindTarget | — |
| IsValidAssistTarget | method | CombatBotBaseAI/IsValidHostileTarget, Unit.Main/IsAlive | AiBotDoctrineTeam/ResolveFocus | — |
| HandleCombatStalemate | method | AiBotAI.Bridge/BridgeSendEvent, AiBotAI.Movement/FindNavBoundaryNear, AiBotAI.Movement/MovePointRun, AiBotAI.Movement/ReGroundZ, AiBotAI.Movement/StopMoving, Log.Main/Out, Map.Main/GetCreature, Object/GetObjectGuid, ObjectGuid/Clear, ObjectGuid/GetCounter, ObjectGuid/IsEmpty, Player.Main/GetName, Unit.Main/AttackStop, Unit.Main/CombatStop, Unit.Main/GetHealth, Unit.Main/GetVictim, Unit.Main/IsInCombat, Unit.Main/NearTeleportTo, WorldObject.Object/GetMap, WorldObject.Object/GetMapId, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/GetRandomPoint | AiBotAI.Main/UpdateAI | — |
| OverpullGuard | method | AiBotAI.Grind/CountNearbyHostiles, Player.Main/GetGroup, Unit.Main/IsHostileTo | AiBotDoctrineSolo/HoldPull, AiBotDoctrineTeam/HoldPull | — |
| HandleOverpullRetreat | method | AiBotAI.Movement/MovePointRun, AiBotAI.Movement/StopMoving, Log.Main/Out, Object/GetGUIDLow, Player.Main/GetGroup, Player.Main/GetName, Unit.Main/AttackStop, Unit.Main/GetAttackers, Unit.Main/IsInCombat, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/GetRandomPoint | AiBotAI.Main/UpdateAI | — |
| AttackStart | method | CombatBotBaseAI/IsRangedDamageClass, Creature.MotionMaster/MoveChase, Unit.Main/Attack, Unit.Main/GetClass, Unit.Main/GetMotionMaster, Unit.Main/GetPowerPercent, Unit.Main/HasDistanceCasterMovement, Unit.Main/IsMounted, Unit.Main/RemoveSpellsCausingAura, Unit.Main/SetCasterChaseDistance, WorldObject.Object/GetCombatDistance | AiBotAI.Bridge/BridgeHandleAttackTarget, AiBotAI.Main/UpdateAI | — |
| SelectAttackTarget | method | CombatBotBaseAI/IsValidHostileTarget, Group/GetFirstMember, GroupReference/next, HostileReference/next, HostileRefManager/getFirst, Object/GetGUIDLow, Player.Main/GetGroup, ThreatManager/getSourceUnit, Unit.Main/GetAttackerForHelper, Unit.Main/GetHostileRefManager, WorldObject.Object/GetDistance#3, WorldObject.Object/GetDistanceZ, WorldObject.Object/IsWithinDist, WorldObject.Object/IsWithinLOSInMap | AiBotAI.Main/UpdateAI | — |
| CheckForUnreachableTarget | method | AiBotAI.Movement/ReGroundZ, AiBotAI.Movement/StopMoving, Creature.MotionMaster/GetCurrentMovementGeneratorType, MotionMaster/GetCurrent, MovementGenerator/IsReachable, Object/IsCreature, Unit.Main/AttackStop, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/GetMotionMaster, Unit.Main/NearTeleportTo, WorldObject.Object/GetDistanceZ, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/IsMoving, WorldObject.Object/IsWithinDist | AiBotAI.Main/UpdateAI | — |
| UpdateOutOfCombatAI | method | Unit.Main/GetClass | AiBotAI.Main/UpdateAI | — |
| UpdateInCombatAI | method | CombatBotBaseAI/UseTrinketEffects, Unit.Main/GetClass, Unit.Main/GetVictim | AiBotAI.Main/UpdateAI | — |
| UpdateOutOfCombatAI_Paladin | method | CombatBotBaseAI/CanTryToCastSpell, CombatBotBaseAI/DoCastSpell, CombatBotBaseAI/FindAndHealInjuredAlly, CombatBotBaseAI/SelectBuffTarget, SpellCaster/HasGCD | — | — |
| UpdateInCombatAI_Paladin | method | CombatBotBaseAI/CanTryToCastSpell, CombatBotBaseAI/DoCastSpell, CombatBotBaseAI/FindAndHealInjuredAlly, CombatBotBaseAI/GetAttackersInRangeCount, CombatBotBaseAI/IsMeleeDamageClass, CombatBotBaseAI/IsPhysicalDamageClass, CombatBotBaseAI/SelectDispelTarget, Creature.MotionMaster/GetCurrentMovementGeneratorType, Creature.MotionMaster/MoveChase, Object/IsCreature, SpellCaster/CastSpell, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/FindLowestHpFriendlyUnit, Unit.Main/GetClass, Unit.Main/GetCreatureType, Unit.Main/GetHealthPercent, Unit.Main/GetMotionMaster, Unit.Main/GetPowerPercent, Unit.Main/GetVictim, Unit.Main/HasAura#2, Unit.Main/HasAuraType, Unit.Main/HasUnitState, Unit.Main/IsTotalImmune | — | — |
| UpdateOutOfCombatAI_Shaman | method | CombatBotBaseAI/CanTryToCastSpell, CombatBotBaseAI/CastWeaponBuff, CombatBotBaseAI/DoCastSpell, CombatBotBaseAI/SummonShamanTotems, Unit.Main/GetVictim, Unit.Main/HasAura#2, Unit.Main/IsMounted, WorldObject.Object/IsMoving | — | — |
| UpdateInCombatAI_Shaman | method | CombatBotBaseAI/CanTryToCastSpell, CombatBotBaseAI/DoCastSpell, CombatBotBaseAI/FindAndHealInjuredAlly, CombatBotBaseAI/IsValidDispelTarget, CombatBotBaseAI/SummonShamanTotems, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/GetAttackers, Unit.Main/GetPowerPercent, Unit.Main/GetShapeshiftForm, Unit.Main/GetVictim, Unit.Main/RemoveAurasDueToSpellByCancel, WorldObject.Object/IsMoving | — | — |
| UpdateOutOfCombatAI_Hunter | method | CombatBotBaseAI/CanTryToCastSpell, CombatBotBaseAI/DoCastSpell, Creature.Main/AI, CreatureAI/AttackStart, Unit.Main/GetCharmInfo, Unit.Main/GetPet, Unit.Main/GetVictim, Unit.Main/IsMounted, Unit.Main/SetIsCommandAttack | — | — |
| UpdateInCombatAI_Hunter | method | CombatBotBaseAI/AddHunterAmmo, CombatBotBaseAI/CanTryToCastSpell, CombatBotBaseAI/DoCastSpell, Creature.MotionMaster/GetCurrentMovementGeneratorType, Creature.MotionMaster/MoveChase, Creature.MotionMaster/MoveDistance, MotionMaster/Clear, Player.Main/HasSpell, SpellCaster/CastSpell#2, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/GetMotionMaster, Unit.Main/GetVictim, Unit.Main/HasAura#2, Unit.Main/HasUnitState, Unit.Main/IsStopped, Unit.Main/StopMoving, WorldObject.Object/GetCombatDistance, WorldObject.Object/GetDistance#3, WorldObject.Object/IsMoving | AiBotAI.Main/UpdateAI | — |
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

---

<!-- verify: boundary-bleed | foreign: AiBotAI -->
