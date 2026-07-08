<!-- provenance: boundary-bleed -->
# ThreatListCopier.battleground_alterac

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ThreatListCopier.battleground_alterac

**Purpose & Responsibilities**

The `ThreatListCopier` class, defined within `battleground_alterac.cpp`, is a utility helper designed to synchronize threat lists between units during the Alterac Valley battleground encounter. It implements the `ThreatListProcesser` interface, enabling a source unit to iterate through its current threat list and force a destination unit to immediately enter combat with every entity present on that list.

This mechanism is primarily utilized by the `npc_alterac_bossHelper` class (also defined in `battleground_alterac.cpp`) to ensure that when primary bosses like Vanndar or Drek'Thar engage players, their linked minions (adds) also instantly aggro those same players. This prevents scenarios where the boss fights while adds remain passive, ensuring coordinated engagement.

Note: A structurally identical `ThreatListCopier` class exists in `boss_ragnaros.cpp` for the Molten Core encounter. That implementation is part of the `boss_ragnaros` unit and is not described here. The behavior documented below applies strictly to the instance defined in `battleground_alterac.cpp`.

**Member-by-Member Behavior**

### Construction and Initialization

**`ThreatListCopier`**
*   **Kind**: Constructor
*   **Behavior**: Initializes the internal pointer `_dest` to the `Unit` object that will receive the copied threat entries. This destination unit is the one that will be forced to attack the targets found in the source's threat list.
*   **Cross-Unit Boundaries**: None.

### Processing Logic

**`Process`**
*   **Kind**: Method
*   **Behavior**: This method overrides the virtual function from the `ThreatListProcesser` base class. It is invoked by the engine for every `Unit` found in the source's threat list during a `ProcessThreatList` call.
    1.  It retrieves the AI instance of the destination unit (`_dest`).
    2.  It calls `AttackStart(unit)` on the destination's AI, passing the current `unit` from the source's threat list. This forces the destination to begin attacking the specified unit, effectively adding that unit to the destination's threat list and initiating combat.
    3.  It returns `false`. In the context of `ThreatListProcesser`, returning `false` indicates that the processing should continue to the next item in the list (i.e., do not stop iteration).
*   **Cross-Unit Boundaries**:
    *   **Calls**: `CreatureAI::AttackStart` (accessed via `Unit::AI()`). This delegates the actual combat initiation logic to the specific AI implementation of the destination creature.
    *   **Calls**: `Unit::AI`. Retrieves the AI controller for the destination unit.

**Notable Implementation Details**

1.  **Memory Management**: The class is designed for short-lived usage. In `battleground_alterac.cpp`, instances are created on the heap using `new`, used immediately within a `ProcessThreatList` call in `npc_alterac_bossHelper::AggroLinkedMobsIfNeeded`, and then explicitly deleted. This pattern ensures no memory leaks but requires the caller to manage the lifecycle strictly.
2.  **No Filtering**: The `Process` method does not perform any checks on the `unit` being processed (e.g., whether it is alive, hostile, or valid). It blindly attempts to start an attack. The responsibility for validating the target lies with the `AttackStart` implementation in the destination's AI or the underlying `Creature`/`Unit` classes.
3.  **Directionality**: The copy is one-way. The source unit's threat list is read-only in this process. The destination unit's threat list is modified by the side effects of `AttackStart`. The source unit is unaffected.

**Data Model**

This unit does not interact with any database tables. It operates entirely on in-memory game state objects (`Unit`, `Creature`, `ThreatList`).

## Member Reference

**ThreatListCopier**
Constructor that initializes the `_dest` member variable with the pointer to the destination `Unit` that will inherit the threat list.

**Process**
Virtual method override from `ThreatListProcesser`. Called for each unit in the source's threat list. It invokes `AttackStart` on the destination unit's AI for the current unit, forcing the destination to engage the target. Returns `false` to continue iteration.

**npc_alterac_bossHelper**
Constructor for the helper class that manages linked mob aggro synchronization for Alterac Valley bosses.

**AddLinkedMob**
Method in `npc_alterac_bossHelper` that registers a creature entry ID to be considered a "linked mob" for aggro copying purposes.

**AggroLinkedMobsIfNeeded**
Method in `npc_alterac_bossHelper` that searches for linked mobs within a 100-yard radius. For each alive, non-combat linked mob, it creates a `ThreatListCopier` instance and processes the boss's threat list to force the mob to attack the same targets as the boss.

**npc_VanndarAI**
Constructor for the AI of NPC Vanndar (Alliance boss). Initializes linked mobs and timers.

**Reset#19**
Method in `npc_VanndarAI` that resets spell timers, clears root states, and re-enables combat phases upon respawn or evade.

**EnterEvadeMode#3**
Method in `npc_VanndarAI` that plays appropriate dialogue based on whether the boss was leashed or wiped, then calls the parent `ScriptedAI::EnterEvadeMode`.

**Aggro#13**
Method in `npc_VanndarAI` that plays the initial aggro dialogue if not already played.

**MoveInLineOfSight#5**
Method in `npc_VanndarAI` that initiates combat with a hostile unit within 23 yards if the boss has no current victim.

**UpdateAI#15**
Method in `npc_VanndarAI` that handles spell rotation (Avatar, Storm Bolt, Thunder Clap), health-based dialogue triggers, leash detection (if moved too far from center), and calls `AggroLinkedMobsIfNeeded` to sync adds.

**GetAI_npc_Vanndar**
Factory function that returns a new instance of `npc_VanndarAI`.

**npc_DrekTharAI**
Constructor for the AI of NPC Drek'Thar (Horde boss). Initializes linked mobs (wolves and adds) and timers.

**Reset#17**
Method in `npc_DrekTharAI` that resets spell timers, clears root states, removes frenzy auras, and respawns dead wolf pets if necessary.

**EnterEvadeMode#2**
Method in `npc_DrekTharAI` that plays appropriate dialogue based on whether the boss was leashed or wiped, then calls the parent `ScriptedAI::EnterEvadeMode`.

**Aggro#11**
Method in `npc_DrekTharAI` that plays the initial aggro dialogue if not already played.

**MoveInLineOfSight#3**
Method in `npc_DrekTharAI` that initiates combat with a hostile unit within 22 yards if the boss has no current victim.

**UpdateAI#13**
Method in `npc_DrekTharAI` that handles spell rotation (Whirlwind, Knockdown, Frenzy), health-based dialogue triggers, leash detection, and calls `AggroLinkedMobsIfNeeded` to sync adds.

**GetAI_npc_DrekThar**
Factory function that returns a new instance of `npc_DrekTharAI`.

**npc_BalindaAI**
Constructor for the AI of NPC Balinda Stonepeak (Alliance boss). Initializes timers and flags.

**Reset#16**
Method in `npc_BalindaAI` that resets spell timers, clears root states, and plays reset dialogue if applicable.

**MoveInLineOfSight#2**
Method in `npc_BalindaAI` that initiates combat with a hostile unit within 28 yards if the boss has no current victim.

**Aggro#10**
Method in `npc_BalindaAI` that plays the initial aggro dialogue if not already played.

**UpdateAI#12**
Method in `npc_BalindaAI` that handles spell rotation (Fireball, Frostbolt, Cone of Cold, Arcane Explosion, Polymorph), health-based dialogue, leash detection, and melee attacks.

**GetAI_npc_Balinda**
Factory function that returns a new instance of `npc_BalindaAI`.

**npc_GalvangarAI**
Constructor for the AI of NPC Galvangar (Horde boss). Initializes timers and flags.

**Aggro#12**
Method in `npc_GalvangarAI` that plays the initial aggro dialogue if not already played.

**Reset#18**
Method in `npc_GalvangarAI` that resets spell timers, clears root states, and plays reset dialogue if applicable.

**MoveInLineOfSight#4**
Method in `npc_GalvangarAI` that initiates combat with a hostile unit within 28 yards if the boss has no current victim.

**UpdateAI#14**
Method in `npc_GalvangarAI` that handles spell rotation (Whirlwind, Mortal Strike, Cleave, Frightening Shout), health-based dialogue, leash detection, and melee attacks.

**GetAI_npc_Galvangar**
Factory function that returns a new instance of `npc_GalvangarAI`.

**npc_WarMasterAI**
Constructor for the AI of War Master NPCs (adds for Vanndar/Drek'Thar). Initializes timers and kill flag.

**Reset#20**
Method in `npc_WarMasterAI` that resets spell timers and clears root states.

**MoveInLineOfSight#6**
Method in `npc_WarMasterAI` that initiates combat and casts Charge if a valid target is within range.

**JustDied#4**
Method in `npc_WarMasterAI` that sets an internal flag indicating the creature has died, preventing respawn bugs.

**UpdateAI#16**
Method in `npc_WarMasterAI` that checks the death flag (deleting the creature if set), handles spell rotation (Whirlwind, Enrage, Charge, Cleave, Demoralizing Shout), and melee attacks.

**GetAI_npc_WarMaster**
Factory function that returns a new instance of `npc_WarMasterAI`.

**npc_AlteracBowmanAI**
Constructor for the AI of Alterac Bowman NPCs. Applies a permanent root aura and disables combat movement.

**JustReachedHome#2**
Method in `npc_AlteracBowmanAI` that reapplies the permanent root aura when the creature reaches home.

**Reset#14**
Method in `npc_AlteracBowmanAI` that resets shoot and reset timers.

**TargetWithinShootRange**
Method in `npc_AlteracBowmanAI` that checks if a target is within 80 yards and has line of sight.

**MoveInLineOfSight**
Method in `npc_AlteracBowmanAI` that initiates combat if a valid target is within shoot range.

**UpdateAI#10**
Method in `npc_AlteracBowmanAI` that handles shooting logic, evade mode if no target is found after 3 seconds, and melee attacks.

**GetAIInformation**
Method in `npc_AlteracBowmanAI` that outputs debug information about the AI's state to the chat handler.

**GetAI_npc_AlteracBowman**
Factory function that returns a new instance of `npc_AlteracBowmanAI`.

**npc_AlteracDardoshAI**
Constructor for the AI of NPC Alterac Dardosh. Initializes cleave timer.

**Reset#15**
Method in `npc_AlteracDardoshAI` that resets the cleave timer.

**UpdateAI#11**
Method in `npc_AlteracDardoshAI` that handles Cleave casting and melee attacks.

**GetAI_npc_AlteracDardosh**
Factory function that returns a new instance of `npc_AlteracDardoshAI`.

**AV_NpcEventTroopsAI**
Constructor for the AI of cavalry/troops NPCs involved in events. Inherits from `npc_escortAI`.

**Reset#5**
Method in `AV_NpcEventTroopsAI` that mounts the creature if it is a rider and starts escorting if conditions are met.

**Aggro#3**
Method in `AV_NpcEventTroopsAI` that unmounts riders and pauses escorting upon aggro.

**WaypointReached#2**
Method in `AV_NpcEventTroopsAI` that handles waypoint arrival (currently empty in this partial).

**UpdateEscortAI#2**
Method in `AV_NpcEventTroopsAI` that checks if the leader is dead and despawns the troop after a delay if so, otherwise handles melee combat.

**npc_korrak_the_bloodragerAI**
Constructor for the AI of Korrak the Bloodrager. Initializes appearance and yell flags.

**Reset#22**
Method in `npc_korrak_the_bloodragerAI` (empty in this partial).

**UpdateAI#17**
Method in `npc_korrak_the_bloodragerAI` that despawns the creature initially, sets respawn time, and then engages in melee combat after appearing.

**AV_NpcEventAI**
Constructor for the AI of various event NPCs (Thurloga, Renferal, Cavalry Commanders, etc.). Inherits from `npc_escortAI`.

**checkTroopsStatus**
Method in `AV_NpcEventAI` that spawns ground troops (Reavers/Commandos) based on resource levels and faction status.

**checkCavalryStatus**
Method in `AV_NpcEventAI` that spawns cavalry units (Wolf/Ram Riders) based on faction status.

**checkAerialStatus**
Method in `AV_NpcEventAI` that transforms NPCs into War Riders/Gryphons and summons aerial units based on beacon status.

**JustRespawned**
Method in `AV_NpcEventAI` that resets event flags, teleports the NPC to its home position, and respawns associated adds (Shamans/Druids/Troops).

**Reset#4**
Method in `AV_NpcEventAI` that mounts commanders, resets spell timers for bosses, and resumes escorting if paused due to aggro.

**Aggro#2**
Method in `AV_NpcEventAI` that unmounts commanders/bosses, casts defensive spells (Earthbind Totem), and pauses escorting.

**WaypointReached**
Method in `AV_NpcEventAI` that handles complex waypoint logic for various NPCs, including mounting/unmounting adds, casting invocation spells, summoning game objects, and stopping escort paths.

**JustDied**
Method in `AV_NpcEventAI` that unlocks speech flags, determines follower types based on resource levels, and restarts escorting for surviving troops.

**UpdateRenferalAI**
Method in `AV_NpcEventAI` that handles spell rotation for Archdruid Renferal (Entangling Roots, Starfire, Rejuvenation).

**UpdateThurlogaAI**
Method in `AV_NpcEventAI` that handles spell rotation for Primalist Thurloga (Chain Lightning, Flame Shock, Lightning Bolt, Healing Wave, Earthbind Totem).

**UpdateEscortAI**
Method in `AV_NpcEventAI` that dispatches to specific status checks (Aerial, Cavalry, Troops), handles event timers for speeches and group formations, and manages boss-specific AI updates.

**QuestComplete_npc_AVBlood_collector**
Function that handles quest completion for blood collectors, updating challenge invocation counters and triggering world boss events if conditions are met.

**GossipHello_npc_AVBlood_collector**
Function that generates gossip menus for various NPCs based on resource levels, reputation, and challenge readiness.

**AV_npc_troops_chief_EventAI**
Constructor for the AI of Troops Chief NPCs (Marshal Teravaine/War Master Commander). Inherits from `npc_escortAI`.

**Reset#10**
Method in `AV_npc_troops_chief_EventAI` that resumes escorting and moves to combat start position if previously aggroed.

**Aggro#6**
Method in `AV_npc_troops_chief_EventAI` that pauses escorting upon aggro.

**WaypointReached#5**
Method in `AV_npc_troops_chief_EventAI` that handles waypoint arrival, playing speeches and stopping the escort path.

**JustDied#2**
Method in `AV_npc_troops_chief_EventAI` that restarts escorting for surviving troops when the chief dies.

**UpdateEscortAI#5**
Method in `AV_npc_troops_chief_EventAI` that handles speech timers, joins troops into a creature group, and manages melee combat.

**QuestComplete_AV_npc_troops_chief**
Function that handles quest completion for troops chiefs, resetting ground challenge invocations and starting the escort event.

**GossipSelect_npc_AVBlood_collector**
Function that handles gossip selections, resetting challenge resources, awarding items, and triggering assault events.

**AV_WarRiderAI**
Constructor for the AI of War Rider/Gryphon NPCs. Sets chase distance and wander distance.

**Reset#8**
Method in `AV_WarRiderAI` that enables flying and disables walking.

**JustReachedHome**
Method in `AV_WarRiderAI` that initiates random movement when the creature reaches home.

**UpdateAI#4**
Method in `AV_WarRiderAI` that handles patrol logic, target selection, and spell rotation (Fireball, Fireball Volley, Stun Bomb Attack).

**AV_BeaconInvocationObjectAI**
Constructor for the AI of Beacon Game Objects. Sets owner GUID and faction.

**Reset**
Method in `AV_BeaconInvocationObjectAI` that resets the invocation timer.

**UpdateAI**
Method in `AV_BeaconInvocationObjectAI` that summons a War Rider/Gryphon and deletes the beacon after the timer expires.

**OnUse**
Method in `AV_BeaconInvocationObjectAI` that deletes the beacon when used by a player.

**go_av_landmineAI**
Constructor for the AI of Landmine Game Objects. Initializes respawn timer and event index.

**UpdateAI#9**
Method in `go_av_landmineAI` that adjusts respawn time based on active events.

**OnUse#2**
Method in `go_av_landmineAI` that despawns the mine and deals damage if the user is hostile.

**av_world_boss_baseai**
Constructor for the base AI of World Bosses. Checks if the event is already active and spawns the event if not.

**EnterEvadeMode**
Method in `av_world_boss_baseai` that removes negative auras, deletes threat list, stops combat, and returns to combat start position.

**JustDied#3**
Method in `av_world_boss_baseai` that despawns the world boss event and calls the parent `JustDied`.

**AV_NpcEventWorldBoss_H_AI**
Constructor for the Horde World Boss (Lok'holar). Interrupts invocation spells and deletes invocation objects.

**Reset#7**
Method in `AV_NpcEventWorldBoss_H_AI` that resets spell timers and resumes escorting if previously aggroed.

**Aggro#5**
Method in `AV_NpcEventWorldBoss_H_AI` that pauses escorting upon aggro.

**WaypointReached#4**
Method in `AV_NpcEventWorldBoss_H_AI` that plays dialogue and stops the escort path at specific waypoints.

**KilledUnit**
Method in `AV_NpcEventWorldBoss_H_AI` that plays dialogue and casts Swell of Souls when a player is killed.

**UpdateEscortAI#4**
Method in `AV_NpcEventWorldBoss_H_AI` that handles initial spawn dialogue, engagement timer, and spell rotation (Blizzard, Frostbolt, Frost Nova, Frost Shock, Ice Blast, Ice Tomb).

**AV_NpcEventWorldBoss_A_AI**
Constructor for the Alliance World Boss (Ivus). Interrupts invocation spells and deletes invocation objects.

**Reset#6**
Method in `AV_NpcEventWorldBoss_A_AI` that resets spell timers and resumes escorting if previously aggroed.

**Aggro#4**
Method in `AV_NpcEventWorldBoss_A_AI` that pauses escorting upon aggro.

**WaypointReached#3**
Method in `AV_NpcEventWorldBoss_A_AI` that plays dialogue and stops the escort path at specific waypoints.

**UpdateEscortAI#3**
Method in `AV_NpcEventWorldBoss_A_AI` that handles initial spawn dialogue, engagement timer, and spell rotation (Roots, Faerie Fire, Moonfire, Starfire, Wrath).

**AV_CommanderAI**
Constructor for the AI of Commander NPCs. Initializes Grip of Command timer.

**Reset#2**
Method in `AV_CommanderAI` (empty in this partial).

**UpdateAI#2**
Method in `AV_CommanderAI` that casts Grip of Command periodically and handles melee attacks.

**AV_DismountAI**
Constructor for the AI of Lieutenant/Commander NPCs that dismount on aggro. Initializes mount ID.

**Reset#3**
Method in `AV_DismountAI` that mounts the creature with the appropriate mount ID.

**Aggro**
Method in `AV_DismountAI` that unmounts the creature upon aggro.

**UpdateAI#3**
Method in `AV_DismountAI` that casts Grip of Command (for specific commanders) and handles melee attacks.

**FrostwolfShamanAI**
Constructor for the AI of Frostwolf Shaman NPCs. Initializes channeling and mount flags.

**Reset#12**
Method in `FrostwolfShamanAI` that casts Lightning Shield, resets timers, and resumes channeling/mounting if applicable.

**Aggro#8**
Method in `FrostwolfShamanAI` that records mount/channeling state, interrupts spells if channeling, and unmounts.

**UpdateAI#7**
Method in `FrostwolfShamanAI` that handles spell rotation (Frost Shock, Healing Wave, Lightning Shield) and melee attacks.

**DruidOfTheGroveAI**
Constructor for the AI of Druid of the Grove NPCs. Initializes channeling and mount flags.

**Reset#11**
Method in `DruidOfTheGroveAI` that casts Thorns, resets timers, and resumes channeling/mounting if applicable.

**Aggro#7**
Method in `DruidOfTheGroveAI` that records mount/channeling state, interrupts spells if channeling, and unmounts.

**UpdateAI#6**
Method in `DruidOfTheGroveAI` that handles spell rotation (Entangling Roots, Starfire) and melee attacks.

**GetAI_FrostwolfShamanAI**
Factory function that returns a new instance of `FrostwolfShamanAI`.

**GetAI_AV_DismountAI**
Factory function that returns a new instance of `AV_DismountAI`.

**GetAI_AV_CommanderAI**
Factory function that returns a new instance of `AV_CommanderAI`.

**GetAI_npc_worldboss_A_AV**
Factory function that returns a new instance of `AV_NpcEventWorldBoss_A_AI`.

**GetAI_npc_worldboss_H_AV**
Factory function that returns a new instance of `AV_NpcEventWorldBoss_H_AI`.

**GetAI_npc_troops_chiefAV**
Factory function that returns a new instance of `AV_npc_troops_chief_EventAI`.

**GetAI_npc_eventAV**
Factory function that returns a new instance of `AV_NpcEventAI`.

**GetAI_npc_eventTroopsAV**
Factory function that returns a new instance of `AV_NpcEventTroopsAI`.

**GetAI_npc_korrak_the_bloodrager**
Factory function that returns a new instance of `npc_korrak_the_bloodragerAI`.

**GetAI_AV_WarRiderAI**
Factory function that returns a new instance of `AV_WarRiderAI`.

**GetAI_AV_BeaconInvocationObjectAI**
Factory function that returns a new instance of `AV_BeaconInvocationObjectAI`.

**GetAI_DruidOfTheGroveAI**
Factory function that returns a new instance of `DruidOfTheGroveAI`.

**GetAI_go_av_landmine**
Factory function that returns a new instance of `go_av_landmineAI`.

**MineNPC_AI**
Constructor for the AI of Mine NPC NPCs. Initializes bomb and flash bomb timers.

**Reset#13**
Method in `MineNPC_AI` that resets bomb, flash bomb, and landmine timers.

**JustRespawned#3**
Method in `MineNPC_AI` that activates the corresponding landmine event in the battleground.

**Aggro#9**
Method in `MineNPC_AI` that sets the creature in combat with the zone.

**UpdateAI#8**
Method in `MineNPC_AI` that handles Flash Bomb (targeting players/pets), Bomb (random target), and Landmine placement, along with melee attacks.

**GetAI_AV_MineNPC_AI**
Factory function that returns a new instance of `MineNPC_AI`.

**AV_mineNpcAI**
Constructor for the AI of Mine NPCs that change entry based on reinforcement level. Stores original entry and position.

**Reset#9**
Method in `AV_mineNpcAI` (empty in this partial).

**JustRespawned#2**
Method in `AV_mineNpcAI` that updates the creature's entry based on the current reinforcement level.

**SelectCreatureEntry**
Method in `AV_mineNpcAI` that determines the new creature entry ID based on the faction's reinforcement level and the original entry.

**UpdateAI#5**
Method in `AV_mineNpcAI` that updates the creature's entry if the reinforcement level has changed, and handles melee attacks.

**GetAI_AV_Mines_AI**
Factory function that returns a new instance of `AV_mineNpcAI` if in Alterac Valley, otherwise `BasicAI`.

**npc_av_trigger_for_questAI**
Constructor for the AI of quest trigger NPCs. Enables MoveInLineOfSight events.

**Reset#21**
Method in `npc_av_trigger_for_questAI` that enables MoveInLineOfSight events.

**MoveInLineOfSight#7**
Method in `npc_av_trigger_for_questAI` that grants quest credit to players within 10 yards.

**GetAI_npc_av_trigger_for_quest**
Factory function that returns a new instance of `npc_av_trigger_for_questAI`.

**OnSummon**
Method in `AVCreateShredderScript` that sets the creator GUID and trigger spell for the summoned shredder.

**GetScript_AVCreateShredder**
Factory function that returns a new instance of `AVCreateShredderScript`.

**AddSC_bg_alterac**
Function that registers all scripts defined in this file with the script manager.

---

<!-- machine-true, projected from graph.json -->

## Map — ThreatListCopier.battleground_alterac

*Source:* battleground_alterac.cpp, boss_ragnaros.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ThreatListCopier | ctor | — | — | — |
| Process | method | CreatureAI/AttackStart, Unit.Main/AI | — | — |
| npc_alterac_bossHelper | ctor | — | — | — |
| AddLinkedMob | method | — | — | — |
| AggroLinkedMobsIfNeeded | method | Creature.Main/ProcessThreatList, GridSearchers/GetCreatureListWithEntryInGrid#2, Unit.Main/IsAlive, Unit.Main/IsInCombat | — | — |
| npc_VanndarAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#19 | method | shared_Util/urand, Unit.Main/ClearUnitState | — | — |
| EnterEvadeMode#3 | method | ScriptedAI/EnterEvadeMode, ScriptMgr/DoScriptText | — | — |
| Aggro#13 | method | ScriptMgr/DoScriptText | — | — |
| MoveInLineOfSight#5 | method | CreatureAI/AttackStart, Unit.Main/GetVictim, Unit.Main/IsHostileTo, WorldObject.Object/GetDistance#3 | — | — |
| UpdateAI#15 | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, ScriptMgr/DoScriptText, shared_Util/urand, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/ClearUnitState, Unit.Main/CombatStop, Unit.Main/GetHealthPercent, Unit.Main/GetMaxHealth, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, Unit.Main/SetHealth, WorldObject.Object/GetDistance2d#4, WorldObject.Object/GetMapId | — | — |
| GetAI_npc_Vanndar | function | — | — | — |
| npc_DrekTharAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#17 | method | Creature.Main/Respawn, GridSearchers/GetCreatureListWithEntryInGrid#2, shared_Util/urand, Unit.Main/ClearUnitState, Unit.Main/IsAlive, Unit.Main/RemoveAurasDueToSpell | — | — |
| EnterEvadeMode#2 | method | ScriptedAI/EnterEvadeMode, ScriptMgr/DoScriptText | — | — |
| Aggro#11 | method | ScriptMgr/DoScriptText | — | — |
| MoveInLineOfSight#3 | method | CreatureAI/AttackStart, Unit.Main/GetVictim, Unit.Main/IsHostileTo, WorldObject.Object/GetDistance#3 | — | — |
| UpdateAI#13 | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, ScriptMgr/DoScriptText, shared_Util/urand, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/AddUnitState, Unit.Main/ClearUnitState, Unit.Main/CombatStop, Unit.Main/GetHealthPercent, Unit.Main/GetMaxHealth, Unit.Main/GetVictim, Unit.Main/HasUnitState, Unit.Main/SelectHostileTarget, Unit.Main/SetHealth, WorldObject.Object/GetDistance2d#4, WorldObject.Object/GetMapId | — | — |
| GetAI_npc_DrekThar | function | — | — | — |
| npc_BalindaAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#16 | method | ScriptMgr/DoScriptText, Unit.Main/ClearUnitState | — | — |
| MoveInLineOfSight#2 | method | CreatureAI/AttackStart, Unit.Main/GetVictim, Unit.Main/IsHostileTo, WorldObject.Object/GetDistance#3 | — | — |
| Aggro#10 | method | ScriptMgr/DoScriptText | — | — |
| UpdateAI#12 | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, ScriptedAI/EnterEvadeMode, ScriptMgr/DoScriptText, shared_Util/urand, SpellCaster/IsNonMeleeSpellCasted, ThreatManager/getThreatList, Unit.Main/AddUnitState, Unit.Main/ClearUnitState, Unit.Main/CombatStop, Unit.Main/GetHealthPercent, Unit.Main/GetMaxHealth, Unit.Main/GetThreatManager, Unit.Main/GetVictim, Unit.Main/HasUnitState, Unit.Main/SelectHostileTarget, Unit.Main/SetHealth, WorldObject.Object/GetDistance#3, WorldObject.Object/GetDistance2d#4, WorldObject.Object/GetMapId, WorldObject.Object/IsWithinDistInMap, WorldObject.Object/IsWithinLOSInMap | — | — |
| GetAI_npc_Balinda | function | — | — | — |
| npc_GalvangarAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Aggro#12 | method | ScriptMgr/DoScriptText | — | — |
| Reset#18 | method | ScriptMgr/DoScriptText, shared_Util/urand, Unit.Main/ClearUnitState | — | — |
| MoveInLineOfSight#4 | method | CreatureAI/AttackStart, Unit.Main/GetVictim, Unit.Main/IsHostileTo, WorldObject.Object/GetDistance#3 | — | — |
| UpdateAI#14 | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, ScriptedAI/EnterEvadeMode, ScriptMgr/DoScriptText, shared_Util/urand, SpellCaster/IsNonMeleeSpellCasted, ThreatManager/getThreatList, Unit.Main/AddUnitState, Unit.Main/ClearUnitState, Unit.Main/CombatStop, Unit.Main/GetHealthPercent, Unit.Main/GetMaxHealth, Unit.Main/GetThreatManager, Unit.Main/GetVictim, Unit.Main/HasUnitState, Unit.Main/SelectHostileTarget, Unit.Main/SetHealth, WorldObject.Object/GetDistance2d#4, WorldObject.Object/GetMapId, WorldObject.Object/IsWithinDistInMap | — | — |
| GetAI_npc_Galvangar | function | — | — | — |
| npc_WarMasterAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#20 | method | Unit.Main/ClearUnitState | — | — |
| MoveInLineOfSight#6 | method | CreatureAI/AttackStart, CreatureAI/DoCastSpellIfCan, Unit.Main/GetVictim, WorldObject.Object/GetDistance#3, WorldObject.Object/IsValidAttackTarget | — | — |
| JustDied#4 | method | — | — | — |
| UpdateAI#16 | method | Creature.Main/GetVictimInRange, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/AddUnitState, Unit.Main/ClearUnitState, Unit.Main/GetHealthPercent, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, WorldObject.Object/DeleteLater | — | — |
| GetAI_npc_WarMaster | function | — | — | — |
| npc_AlteracBowmanAI | ctor | CreatureAI/SetCombatMovement, ScriptedAI/ScriptedAI, Unit.Main/AddAura | — | — |
| JustReachedHome#2 | method | Unit.Main/AddAura | — | — |
| Reset#14 | method | — | — | — |
| TargetWithinShootRange | method | WorldObject.Object/GetDistance#3, WorldObject.Object/IsWithinLOSInMap | — | — |
| MoveInLineOfSight | method | CreatureAI/AttackStart, Unit.Main/GetVictim, WorldObject.Object/IsValidAttackTarget | — | — |
| UpdateAI#10 | method | Creature.Main/IsInEvadeMode, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, ScriptedAI/EnterEvadeMode, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAIInformation | method | ChatHandler.Chat/PSendSysMessage, Creature.Main/IsInEvadeMode, CreatureAI/GetAIInformation, Unit.Main/GetVictim, WorldObject.Object/GetName | — | — |
| GetAI_npc_AlteracBowman | function | — | — | — |
| npc_AlteracDardoshAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#15 | method | — | — | — |
| UpdateAI#11 | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_npc_AlteracDardosh | function | — | — | — |
| AV_NpcEventTroopsAI | ctor | ScriptedEscortAI/npc_escortAI | — | — |
| Reset#5 | method | Creature.Main/AI, Creature.Main/GetRespawnDelay, Object/GetEntry, ScriptedEscortAI/getCurrentWP, ScriptedEscortAI/HasEscortState, ScriptedEscortAI/setCurrentWP, ScriptedEscortAI/SetEscortPaused, ScriptedEscortAI/Start, Unit.Main/Mount | — | — |
| Aggro#3 | method | Object/GetEntry, ScriptedEscortAI/SetEscortPaused, Unit.Main/Unmount | — | — |
| WaypointReached#2 | method | — | — | — |
| UpdateEscortAI#2 | method | Creature.Main/DisappearAndDie, CreatureAI/DoMeleeAttackIfReady, GridSearchers/GetCreatureListWithEntryInGrid#2, Object/GetEntry, Unit.Main/GetVictim, Unit.Main/IsDead, Unit.Main/SelectHostileTarget | — | — |
| npc_korrak_the_bloodragerAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#22 | method | — | — | — |
| UpdateAI#17 | method | Creature.Main/DisappearAndDie, Creature.Main/SetRespawnTime, CreatureAI/DoMeleeAttackIfReady, ScriptMgr/DoScriptText, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| AV_NpcEventAI | ctor | ScriptedEscortAI/npc_escortAI, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | — | — |
| checkTroopsStatus | method | BattleGroundAV/getPlayerGoStatus, BattleGroundAV/getReinforcementLevelGroundUnit, BattleGroundAV/setPlayerGoStatus, BattleGroundMap/GetBG, WorldObject.Object/GetMap, WorldObject.Object/SummonCreature#2 | — | — |
| checkCavalryStatus | method | BattleGroundAV/getPlayerGoStatus, BattleGroundAV/setPlayerGoStatus, BattleGroundMap/GetBG, WorldObject.Object/GetMap, WorldObject.Object/SummonCreature#2 | — | — |
| checkAerialStatus | method | BattleGroundAV/getPlayerGoStatus, BattleGroundMap/GetBG, Creature.MotionMaster/MovePoint, CreatureAI/DoCastSpellIfCan, CreatureAI/IsCombatMovementEnabled, CreatureAI/SetCombatMovement, Unit.Main/AddAura, Unit.Main/GetMotionMaster, Unit.Main/SetDisplayId, Unit.Main/SetFly, Unit.Main/SetWalk, WorldObject.Object/GetMap, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/RemoveFlag, WorldObject.Object/SummonCreature#2 | — | — |
| JustRespawned | method | Creature.Main/DisappearAndDie, Creature.Main/RemoveCorpse, Creature.Main/Respawn, Creature.Main/SetDeathState, Creature.Main/SetHomePosition, Creature.Main/SetRespawnDelay, Creature.MotionMaster/MovePoint, GridSearchers/GetCreatureListWithEntryInGrid#2, Object/GetEntry, ScriptedAI/DoTeleportTo#2, ScriptedEscortAI/JustRespawned, ScriptedEscortAI/Stop, Unit.Main/GetMotionMaster, Unit.Main/Unmount | — | — |
| Reset#4 | method | Object/GetEntry, ScriptedEscortAI/SetEscortPaused, Unit.Main/Mount, Unit.Main/SetStandState, Unit.Main/SetWalk, WorldObject.Object/GetDistance#4 | — | — |
| Aggro#2 | method | Object/GetEntry, ScriptedEscortAI/SetEscortPaused, SpellCaster/CastSpell#2, Unit.Main/Unmount | — | — |
| WaypointReached | method | Creature.Main/SetHomePosition, GridSearchers/GetCreatureListWithEntryInGrid#2, Object/GetEntry, ScriptedEscortAI/SetEscortPaused, ScriptedEscortAI/Stop, ScriptMgr/DoScriptText, SpellCaster/CastSpell#2, Unit.Main/Mount, Unit.Main/SetWalk, Unit.Main/Unmount, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SetFlag, WorldObject.Object/SummonGameObject | — | — |
| JustDied | method | BattleGroundAV/getReinforcementLevelGroundUnit, BattleGroundMap/GetBG, Creature.Main/AI, Creature.Main/SetRespawnDelay, GridSearchers/GetCreatureListWithEntryInGrid#2, Object/GetEntry, ScriptedEscortAI/getCurrentWP, ScriptedEscortAI/setCurrentWP, ScriptedEscortAI/Start, WorldObject.Object/GetMap | — | — |
| UpdateRenferalAI | method | CreatureAI/DoCastSpellIfCan, shared_Util/urand, SpellCaster/CastSpell#2, Unit.Main/GetVictim | — | — |
| UpdateThurlogaAI | method | CreatureAI/DoCastSpellIfCan, shared_Util/urand, SpellCaster/CastSpell#2, Unit.Main/GetHealthPercent, Unit.Main/GetVictim, WorldObject.Object/FindNearestCreature | — | — |
| UpdateEscortAI | method | Creature.Main/DisappearAndDie, Creature.Main/JoinCreatureGroup, CreatureAI/DoMeleeAttackIfReady, GridSearchers/GetCreatureListWithEntryInGrid#2, Object/GetEntry, ScriptedEscortAI/SetEscortPaused, ScriptMgr/DoScriptText, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, Unit.Main/SetWalk, Unit.Main/Unmount, WorldObject.Object/FindNearestGameObject, WorldObject.Object/GetAngle, WorldObject.Object/GetDistance#3, WorldObject.Object/GetDistance#4, WorldObject.Object/GetOrientation | — | — |
| QuestComplete_npc_AVBlood_collector | function | BattleGround/GetTypeID, BattleGroundAV/isWorldBossChallengeInvocationReady, BattleGroundAV/resetWorldBossChallengeInvocation, BattleGroundAV/setChallengeInvocationCounter, BattleGroundAV/setPlayerGoStatus, Creature.Main/AI, Log.Main/Out, Player.Main/GetBattleGround, Player.Main/GetTeam, QuestDef/GetQuestId, ScriptedEscortAI/Start, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag | — | — |
| GossipHello_npc_AVBlood_collector | function | BattleGround/GetTypeID, BattleGroundAV/GetActualArmorRessources, BattleGroundAV/getChallengeInvocationCounter, BattleGroundAV/getChallengeInvocationGoals, BattleGroundAV/getMinReputationNeeded, BattleGroundAV/getReinforcementLevelGroundUnit, BattleGroundAV/isAerialChallengeInvocationReady, BattleGroundAV/isCavalryChallengeInvocationReady, BattleGroundAV/isGroundChallengeInvocationReady, BattleGroundMap/GetBG, Creature.Main/AI, GossipDef/AddMenuItem#5, GossipDef/SendGossipMenu, Object/GetEntry, Object/GetGUID, Object/GetObjectGuid, Object/HasFlag, ObjectGuid/ObjectGuid#5, ObjectMgr/GetCreatureQuestRelationsMapBounds, ObjectMgr/GetQuestTemplate, Player.Main/GetBattleGround, Player.Main/GetGossipTextId, Player.Main/GetReputationRank, Player.Main/GetTeam, Player.Main/PrepareQuestMenu, PlayerMenu/GetGossipMenu, QuestDef/GetQuestId, ScriptedEscortAI/Start, SpellCaster/CastSpell#2, Unit.Main/GetFactionTemplateId, Unit.Main/HandleEmote, Unit.Main/IsQuestGiver, Unit.Main/IsVendor, Unit.Main/SetFactionTemplateId, Unit.Main/SetWalk, WorldObject.Object/GetDistance#4, WorldObject.Object/GetMap, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag, WorldObject.Object/SetUInt32Value | — | — |
| AV_npc_troops_chief_EventAI | ctor | ScriptedEscortAI/npc_escortAI | — | — |
| Reset#10 | method | Creature.Main/GetCombatStartPosition, Creature.MotionMaster/MovePoint, ScriptedEscortAI/SetEscortPaused, Unit.Main/GetMotionMaster | — | — |
| Aggro#6 | method | ScriptedEscortAI/SetEscortPaused | — | — |
| WaypointReached#5 | method | Creature.Main/SetHomePosition, Object/GetEntry, ScriptedEscortAI/SetEscortPaused, ScriptedEscortAI/Stop, ScriptMgr/DoScriptText, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | — | — |
| JustDied#2 | method | Creature.Main/AI, GridSearchers/GetCreatureListWithEntryInGrid#2, Object/GetEntry, ScriptedEscortAI/Start | — | — |
| UpdateEscortAI#5 | method | Creature.Main/JoinCreatureGroup, CreatureAI/DoMeleeAttackIfReady, GridSearchers/GetCreatureListWithEntryInGrid#2, Object/GetEntry, ScriptedEscortAI/SetEscortPaused, ScriptMgr/DoScriptText, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, Unit.Main/SetWalk, WorldObject.Object/GetAngle, WorldObject.Object/GetDistance#3, WorldObject.Object/GetOrientation | — | — |
| QuestComplete_AV_npc_troops_chief | function | BattleGround/GetTypeID, BattleGroundAV/resetGroundChallengeInvocation, BattleGroundAV/setPlayerGoStatus, Creature.Main/AI, Object/GetEntry, Player.Main/GetBattleGround, Player.Main/GetTeam, QuestDef/GetQuestId, ScriptedEscortAI/Start, Unit.Main/HandleEmote, Unit.Main/SetWalk, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag | — | — |
| GossipSelect_npc_AVBlood_collector | function | BattleGround/GetTypeID, BattleGroundAV/resetAerialChallengeInvocation, BattleGroundAV/resetCavalryChallengeInvocation, BattleGroundAV/resetGroundChallengeInvocation, BattleGroundAV/setPlayerGoStatus, BattleGroundAV/UpgradeArmor, Creature.Main/AI, game_Objects_Item/GenerateItemRandomPropertyId, GossipDef/CloseGossip, GossipDef/SendGossipMenu, Object/GetEntry, Object/GetGUID, Object/HasFlag, ObjectGuid/ObjectGuid#5, Player.Main/CanStoreNewItem, Player.Main/GetBattleGround, Player.Main/GetSession, Player.Main/GetTeam, Player.Main/HasItemCount, Player.Main/SendNewItem, Player.Main/StoreNewItem, ScriptedEscortAI/Start, ScriptMgr/DoScriptText, Unit.Main/HandleEmote, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag, WorldSession.ItemHandler/SendListInventory | — | — |
| AV_WarRiderAI | ctor | Creature.Main/SetWanderDistance, ScriptedAI/ScriptedAI, Unit.Main/SetCasterChaseDistance | — | — |
| Reset#8 | method | Unit.Main/SetFly, Unit.Main/SetWalk | — | — |
| JustReachedHome | method | Creature.MotionMaster/MoveRandom, Unit.Main/GetMotionMaster | — | — |
| UpdateAI#4 | method | Creature.Main/SetHomePosition, Creature.MotionMaster/GetCurrentMovementGeneratorType, CreatureAI/AttackStart, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Object/GetEntry, ScriptedAI/EnterEvadeMode, Unit.Main/GetMotionMaster, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, Unit.Main/SelectNearestTarget, WorldObject.Object/IsWithinDistInMap | — | — |
| AV_BeaconInvocationObjectAI | ctor | GameObject/SetOwnerGuid, GameObjectAI/GameObjectAI, Object/GetEntry, ObjectGuid/ObjectGuid, WorldObject.Object/SetUInt32Value | — | — |
| Reset | method | — | — | — |
| UpdateAI | method | WorldObject.Object/AddObjectToRemoveList, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SummonCreature#2 | — | — |
| OnUse | method | WorldObject.Object/AddObjectToRemoveList | — | — |
| go_av_landmineAI | ctor | GameObject/GetGOData, GameObjectAI/GameObjectAI, GameObjectData/GetRandomRespawnTime, Object/GetEntry | — | — |
| UpdateAI#9 | method | BattleGround/IsActiveEvent, BattleGroundMap/GetBG, GameObject/isSpawned, GameObject/SetRespawnTime, Map.Main/IsBattleGround, shared_Util/urand, WorldObject.Object/GetMap | — | — |
| OnUse#2 | method | GameObject/Despawn, GameObject/IsHostileTo | — | — |
| av_world_boss_baseai | ctor | BattleGround/IsActiveEvent, BattleGroundMap/GetBG, game_Battlegrounds_BattleGround/SpawnEvent, Map.Main/IsBattleGround, ScriptedEscortAI/npc_escortAI, WorldObject.Object/DeleteLater, WorldObject.Object/GetMap | — | — |
| EnterEvadeMode | method | Creature.Main/SetLootRecipient, ScriptedEscortAI/Reset, ScriptedEscortAI/ReturnToCombatStartPosition, Unit.Main/CombatStop, Unit.Main/DeleteThreatList, Unit.Main/GetSpellAuraHolderMap, Unit.Main/RemoveSpellAuraHolder, Unit.SpellAuras/IsPositive | — | — |
| JustDied#3 | method | BattleGroundMap/GetBG, CreatureAI/JustDied, game_Battlegrounds_BattleGround/SpawnEvent, Map.Main/IsBattleGround, WorldObject.Object/GetMap | — | — |
| AV_NpcEventWorldBoss_H_AI | ctor | GameObject/Delete, GridSearchers/GetCreatureListWithEntryInGrid#2, GridSearchers/GetGameObjectListWithEntryInGrid#2, Map.Main/GetPlayers, SpellCaster/InterruptNonMeleeSpells, Unit.Main/IsAlive, Unit.Main/RemoveAurasDueToSpell, WorldObject.Object/GetMap | — | — |
| Reset#7 | method | Creature.Main/GetCombatStartPosition, Creature.MotionMaster/MovePoint, ScriptedEscortAI/SetEscortPaused, Unit.Main/GetMotionMaster, Unit.Main/SetWalk | — | — |
| Aggro#5 | method | ScriptedEscortAI/SetEscortPaused | — | — |
| WaypointReached#4 | method | Creature.Main/SetHomePosition, ScriptedEscortAI/Stop, ScriptMgr/DoScriptText, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | — | — |
| KilledUnit | method | CreatureAI/DoCastSpellIfCan, Object/GetTypeId, ScriptMgr/DoScriptText | — | — |
| UpdateEscortAI#4 | method | Creature.Main/SetDefaultMovementType, Creature.Main/SetHomePosition, Creature.Main/SetWanderDistance, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Map.Main/ScriptCommandStart, Object/GetObjectGuid, ScriptedEscortAI/Start, ScriptInfo/ScriptInfo, ScriptMgr/DoScriptText, shared_Util/urand, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, Unit.Main/SetWalk, WorldObject.Object/GetMap | — | — |
| AV_NpcEventWorldBoss_A_AI | ctor | GameObject/Delete, GridSearchers/GetCreatureListWithEntryInGrid#2, GridSearchers/GetGameObjectListWithEntryInGrid#2, Map.Main/GetPlayers, SpellCaster/InterruptNonMeleeSpells, Unit.Main/IsAlive, Unit.Main/RemoveAurasDueToSpell, WorldObject.Object/GetMap | — | — |
| Reset#6 | method | Creature.Main/GetCombatStartPosition, Creature.MotionMaster/MovePoint, ScriptedEscortAI/SetEscortPaused, Unit.Main/GetMotionMaster, Unit.Main/SetWalk | — | — |
| Aggro#4 | method | ScriptedEscortAI/SetEscortPaused | — | — |
| WaypointReached#3 | method | ScriptedEscortAI/Stop, ScriptMgr/DoScriptText | — | — |
| UpdateEscortAI#3 | method | Creature.Main/SetDefaultMovementType, Creature.Main/SetHomePosition, Creature.Main/SetWanderDistance, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, ScriptedEscortAI/Start, ScriptMgr/DoScriptText, shared_Util/urand, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, Unit.Main/SetWalk | — | — |
| AV_CommanderAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#2 | method | — | — | — |
| UpdateAI#2 | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Object/GetEntry, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| AV_DismountAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#3 | method | Object/GetEntry, Unit.Main/Mount | — | — |
| Aggro | method | Unit.Main/Unmount | — | — |
| UpdateAI#3 | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Object/GetEntry, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| FrostwolfShamanAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#12 | method | SpellCaster/CastSpell#2, Unit.Main/Mount | — | — |
| Aggro#8 | method | SpellCaster/InterruptNonMeleeSpells, Unit.Main/HasAura#2, Unit.Main/IsMounted, Unit.Main/Unmount | — | — |
| UpdateAI#7 | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, SpellCaster/CastSpell#2, Unit.Main/GetHealthPercent, Unit.Main/GetVictim, Unit.Main/HasAura#2, Unit.Main/SelectHostileTarget | — | — |
| DruidOfTheGroveAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#11 | method | SpellCaster/CastSpell#2, Unit.Main/Mount | — | — |
| Aggro#7 | method | SpellCaster/InterruptNonMeleeSpells, Unit.Main/HasAura#2, Unit.Main/IsMounted, Unit.Main/Unmount | — | — |
| UpdateAI#6 | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, SpellCaster/CastSpell#2, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_FrostwolfShamanAI | function | — | — | — |
| GetAI_AV_DismountAI | function | — | — | — |
| GetAI_AV_CommanderAI | function | — | — | — |
| GetAI_npc_worldboss_A_AV | function | — | — | — |
| GetAI_npc_worldboss_H_AV | function | — | — | — |
| GetAI_npc_troops_chiefAV | function | — | — | — |
| GetAI_npc_eventAV | function | — | — | — |
| GetAI_npc_eventTroopsAV | function | — | — | — |
| GetAI_npc_korrak_the_bloodrager | function | — | — | — |
| GetAI_AV_WarRiderAI | function | — | — | — |
| GetAI_AV_BeaconInvocationObjectAI | function | — | — | — |
| GetAI_DruidOfTheGroveAI | function | — | — | — |
| GetAI_go_av_landmine | function | — | — | — |
| MineNPC_AI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#13 | method | — | — | — |
| JustRespawned#3 | method | BattleGround/ActivateEventWithoutSpawn, BattleGroundMap/GetBG, Map.Main/IsBattleGround, Object/GetEntry, WorldObject.Object/GetMap | — | — |
| Aggro#9 | method | Creature.Main/SetInCombatWithZone | — | — |
| UpdateAI#8 | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Map.Main/GetPlayers, MapRefManager/begin#2, MapRefManager/end#2, Unit.Main/GetPet, Unit.Main/GetVictim, Unit.Main/IsNoWeaponShapeShift, Unit.Main/SelectHostileTarget, WorldObject.Object/GetDistance2d#3, WorldObject.Object/GetMap | — | — |
| GetAI_AV_MineNPC_AI | function | — | — | — |
| AV_mineNpcAI | ctor | Object/GetEntry, ScriptedAI/ScriptedAI, WorldObject.Object/GetPosition#2 | — | — |
| Reset#9 | method | — | — | — |
| JustRespawned#2 | method | Creature.Main/UpdateEntry, Object/GetEntry | — | — |
| SelectCreatureEntry | method | BattleGroundAV/getReinforcementLevelGroundUnit, BattleGroundMap/GetBG, Map.Main/IsBattleGround, Unit.Main/GetFactionTemplateId, WorldObject.Object/GetMap | — | — |
| UpdateAI#5 | method | Creature.Main/UpdateEntry, CreatureAI/DoMeleeAttackIfReady, Object/GetEntry, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_AV_Mines_AI | function | BasicAI/BasicAI, WorldObject.Object/GetMapId | — | — |
| npc_av_trigger_for_questAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#21 | method | Creature.Main/EnableMoveInLosEvent | — | — |
| MoveInLineOfSight#7 | method | Object/GetEntry, Object/GetObjectGuid, Object/IsPlayer, Object/ToPlayer, Player.Main/KilledMonsterCredit, WorldObject.Object/IsWithinDist | — | — |
| GetAI_npc_av_trigger_for_quest | function | — | — | — |
| OnSummon | method | Object/GetObjectGuid, Unit.Main/SetCreatorGuid, WorldObject.Object/SetUInt32Value | — | — |
| GetScript_AVCreateShredder | function | — | — | — |
| AddSC_bg_alterac | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |

---

<!-- verify: boundary-bleed | foreign: aggro, KilledUnit, Process, ThreatListCopier, UpdateAI -->
