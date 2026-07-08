# zulfarrak

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Zulfarrak Instance Scripts

**Purpose & Responsibilities**
This translation unit implements the scripted behaviors for specific NPCs, GameObjects, and Area Triggers within the Zulfarrak instance dungeon. It manages three primary subsystems:
1.  **The Pyramid Event:** A complex, multi-stage sequence involving Sergeant Bly and his crew (Weegli, Raven, Oro, Murta). This includes player interaction via gossip, faction switching to manage combat states, coordinated movement, and a scripted explosion sequence to open the final door.
2.  **Environmental Interactions:** Scripts for shallow graves that randomly spawn zombies or dead heroes, and a table object that advances a specific quest upon interaction.
3.  **Boss/Elite Triggers:** Logic for Witch Doctor Zumrah (summoning skeletons and triggering hostility via area trigger) and Antu'sul (triggering combat and summoning broodlings based on client patch version).

The unit relies heavily on `InstanceData` to track the state of the Pyramid event (`EVENT_PYRAMID`) and coordinates actions between multiple creatures using GUIDs stored in the instance data.

## Member-by-Member Behavior

### Sergeant Bly and Crew (Pyramid Event)

**npc_sergeant_blyAI**
This AI controls Sergeant Bly. It inherits from `ScriptedAI`.
*   **Constructor:** Initializes timers and retrieves `InstanceData`. Calls `Reset()`.
*   **Reset:** Resets spell timers (`ShieldBash_Timer`, `Revenge_Timer`). Note: The code contains a commented-out line setting faction to friendly; currently, it does not reset faction explicitly here.
*   **UpdateAI:**
    *   Manages a "post-gossip" dialogue sequence (`postGossipStep` 1-3).
    *   Step 1 & 2: Plays specific say lines (`SAY_1`, `SAY_2`) with 5-second delays.
    *   Step 3: Sets Bly's faction to `HOSTILE`. Attacks the player who triggered the event (`PlayerGUID`). Triggers Weegli's script event (`OnScriptEventHappened`) to start the door destruction sequence. Calls `switchFactionIfAlive` for crew members (Raven, Oro, Murta) to make them hostile.
    *   Combat Logic: Casts `SPELL_SHIELD_BASH` every 15s and `SPELL_REVENGE` every 10s. Performs melee attacks.
*   **OnScriptEventHappened:** Sets `postGossipStep` to 1, initiating the dialogue sequence.
*   **switchFactionIfAlive:** Helper method. Checks if a crew member (identified by entry ID) is alive. If so, sets their faction to `HOSTILE`.

**OnGossipHello_npc_sergeant_bly**
Handles player interaction with Bly.
*   Checks `InstanceData` for `EVENT_PYRAMID` state.
*   If `PYRAMID_KILLED_ALL_TROLLS`: Offers a gossip option ("That's it! I'm tired...") to start the final phase.
*   If `PYRAMID_NOT_STARTED`: Shows menu text 1515.
*   Otherwise: Shows menu text 1516.

**OnGossipSelect_npc_sergeant_bly**
Handles the selection of the gossip option.
*   Closes gossip menu.
*   Stores the player's GUID in the AI.
*   Calls `OnScriptEventHappened()` on the AI, triggering the dialogue/combat sequence.

**initBlyCrewMember**
Helper function to position and configure a crew member after being freed from cages.
*   Retrieves the creature by GUID from `InstanceData`.
*   Sets combat start position and home position to the provided coordinates.
*   Moves the creature to the coordinates using `MovePoint` with pathfinding and walk mode.
*   Sets faction to `FACTION_FREED` (250), making them hostile to trolls but presumably neutral/friendly to players initially until the event progresses.

**OnGossipHello_go_troll_cage**
Triggered when a player interacts with the troll cage GameObject.
*   Sets `EVENT_PYRAMID` to `PYRAMID_CAGES_OPEN`.
*   Calls `initBlyCrewMember` for all five crew members (Bly, Raven, Oro, Weegli, Murta) with specific coordinates near the stairs.

### Weegli Blastfuse (Door Destruction)

**npc_weegli_blastfuseAI**
Controls Weegli, who destroys the final door.
*   **Constructor:** Initializes timers and flags.
*   **Reset:** Currently empty (commented out logic).
*   **AttackStart:** Calls parent `AttackStart`. Commented out logic suggests he was intended to keep distance.
*   **JustDied:** Currently empty (commented out logic).
*   **UpdateAI:**
    *   **Regen Logic:** If `regen` is false and `EVENT_PYRAMID` is `PYRAMID_KILLED_ALL_TROLLS`, it sets `regen` to true and restores health to 100% for Weegli and all crew members (Oro, Murta, Bly, Raven). This ensures the crew survives the final phase if they were damaged earlier.
    *   **Aggro Sync:** If the event is *not* finished, it checks if crew members (Oro, Murta, Bly) have targets. If not, it forces them to attack Weegli's current victim. This keeps the crew engaged in the same fight.
    *   **Combat:** Casts `SPELL_BOMB` every 10s. Uses `SPELL_SHOOT` (ranged) if out of melee range, otherwise melee attacks. Switches sheath state accordingly.
*   **MovementInform:** Handles waypoints.
    *   Waypoint 1 (if `PYRAMID_CAGES_OPEN`): Sets event to `PYRAMID_ARRIVED_AT_STAIR`, plays say line, moves to next point.
    *   Waypoint 2 (if `PYRAMID_WAVE_1`): Moves to a new position and sets home position.
    *   **Door Destruction Sequence:**
        *   If `destroyingDoor` is true: Summons a GameObject (explosive charge) at specific coords, stores its GUID, sets `destroyingDoor` to false, and calls `RunAfterExplosion1`.
        *   If `runAway` is true and waypoint 1 reached: Finds the explosive GameObject, sets its spell ID, and uses it (triggering explosion). Opens the end door (`GO_END_DOOR`) via `InstanceData`. Plays Chief Ukoroz's say line. Calls `RunAfterExplosion2`. Sets `runAway` to false.
        *   If `disappear` is true and waypoint 2 reached: Forces despawn of Weegli.
*   **OnScriptEventHappened:** Calls `DestroyDoor()`.
*   **DestroyDoor:** Sets faction to friendly (so he doesn't aggro during setup), moves to the door location, plays say line, and sets `destroyingDoor` flag.
*   **RunAfterExplosion1 / RunAfterExplosion2:** Move Weegli away from the explosion site in two stages.

**OnGossipHello_npc_weegli_blastfuse**
*   If `PYRAMID_KILLED_ALL_TROLLS`: Offers gossip option to blow up the door.
*   Other states: Show appropriate menu texts.

**OnGossipSelect_npc_weegli_blastfuse**
*   Closes gossip.
*   Calls `OnScriptEventHappened()` on Weegli's AI, starting the door destruction sequence.

### Environmental Objects

**OnGossipHello_go_shallow_grave**
*   Checks if the grave has been used (`GetUseCount() == 0`).
*   If unused, rolls a random number (0-100).
    *   < 65: Summons a Zombie (entry 7286).
    *   65-75: Summons a Dead Hero (entry 7276).
    *   > 75: Summons nothing.
*   Spawns are temporary (30s or death).
*   Increments use count.

**OnGossipHello_go_table_theka**
*   Checks if player has Quest 2936 incomplete.
*   If so, marks the quest objective as completed (`AreaExploredOrEventHappens`).
*   Shows gossip menu.

### Witch Doctor Zumrah

**ward_zumrahAI**
*   **Reset:** Sets skeleton timer to 5s. Sets movement type to IDLE.
*   **UpdateAI:** Maintains IDLE movement. Casts spell 11088 (Raise Skeleton) every 5 seconds.

**OnTrigger_at_zumrah**
*   Triggered when a player enters the area around Zumrah.
*   Finds nearest Zumrah.
*   If Zumrah is alive and not already hostile (faction != 37):
    *   Sets `EVENT_ZUMRAH` to `IN_PROGRESS`.
    *   Removes immunity flag.
    *   Sets faction to hostile (37).
    *   Plays trigger say line.

### Antu'sul

**OnTrigger_at_antusul**
*   Triggered when a player enters the area around Antu'sul.
*   Finds nearest Antu'sul.
*   Validates: Antu'sul must be alive, not in combat, and event `EVENT_ANTUSUL` must be `NOT_STARTED`.
*   Sets `EVENT_ANTUSUL` to `IN_PROGRESS`.
*   Plays trigger say line.
*   Schedules a lambda event (delayed by `BATCHING_INTERVAL * 3`):
    *   Determines summon count based on `WOW_PATCH_112`. Pre-1.12 summons 2 batches; 1.12+ summons 1 batch.
    *   Each batch summons 4 Sul'lithuz Broodlings at fixed coordinates.
    *   Sets broodlings to combat with zone.
    *   If Antu'sul is still alive and not in combat, moves him to a specific point.

### Registration

**AddSC_zulfarrak**
Registers all scripts defined in this unit with the script manager.

## Cross-Unit Boundaries

*   **InstanceData:** Heavily used by `npc_sergeant_blyAI`, `npc_weegli_blastfuseAI`, `OnGossipHello_go_troll_cage`, `OnTrigger_at_zumrah`, and `OnTrigger_at_antusul`.
    *   *Direction:* Read/Write.
    *   *Purpose:* Tracks `EVENT_PYRAMID` phases, `EVENT_ZUMRAH`, `EVENT_ANTUSUL`, and `EVENT_END_DOOR`. Stores GUIDs for crew members and doors.
*   **ScriptedAI:** Base class for `npc_sergeant_blyAI`, `npc_weegli_blastfuseAI`, `ward_zumrahAI`.
    *   *Direction:* Inheritance/Call.
    *   *Purpose:* Provides base AI functionality (timers, casting, melee).
*   **Creature/Unit/GameObject/Map:** Used by all AI and helper functions.
    *   *Direction:* Call.
    *   *Purpose:* Access creature stats, positions, factions, motion masters, and summoning capabilities.
*   **ScriptMgr:** Used by `AddSC_zulfarrak` and various AI methods (`DoScriptText`).
    *   *Direction:* Call.
    *   *Purpose:* Register scripts and play sound/text events.
*   **GossipDef:** Used by gossip handlers.
    *   *Direction:* Call.
    *   *Purpose:* Send menus and close gossip.

## Data Model

This unit does not directly query or modify database tables. It relies on `InstanceData` (in-memory state) and static configuration entries (creature/gameobject IDs) defined in headers or constants.

## Notable Implementation Details

1.  **Faction Management:** The Pyramid event relies on precise faction switching. Crew members start as `FACTION_FRIENDLY` (35) in cages. Upon release, `initBlyCrewMember` sets them to `FACTION_FREED` (250). During the final phase, `switchFactionIfAlive` sets them to `FACTION_HOSTILE` (14). Weegli is set to `FACTION_FRIENDLY` before destroying the door to prevent aggro issues during the setup animation.
2.  **Health Regen Hack:** In `npc_weegli_blastfuseAI::UpdateAI`, if the event reaches `PYRAMID_KILLED_ALL_TROLLS`, it forcibly sets the health of Weegli and all crew members to 100%. This is a workaround to ensure the crew survives the final encounter regardless of prior damage.
3.  **Aggro Sync:** `npc_weegli_blastfuseAI::UpdateAI` actively checks if crew members have targets. If not, it forces them to attack Weegli's victim. This prevents crew members from standing idle while Weegli fights.
4.  **Patch-Specific Behavior:** `OnTrigger_at_antusul` checks `sWorld.GetWowPatch()` to determine how many broodlings to summon. This reflects a change in WoW patch 1.12.0.
5.  **Commented-Out Code:** Several sections contain commented-out logic (e.g., `Reset` in Weegli's AI, `AttackStartCaster` in Weegli's AI). These indicate unfinished features or debugging remnants. Specifically, the comment in `npc_sergeant_blyAI` notes that `SPELL_REVENGE` should only cast on dodge/parry/block, but the current implementation casts it on a timer.
6.  **Hardcoded Coordinates:** Movement points and summon locations are hardcoded floats. Any map changes would require updating these values.
7.  **Lambda Event:** `OnTrigger_at_antusul` uses a lambda event scheduled with `BATCHING_INTERVAL * 3`. This introduces a delay before broodlings spawn, allowing the player to react or move.

## Member Reference

**npc_sergeant_blyAI**
Constructor for Sergeant Bly's AI. Initializes timers, retrieves instance data, and calls `Reset`.

**Reset**
Resets spell timers for Bly. Does not reset faction.

**UpdateAI**
Manages Bly's dialogue sequence, faction switching, and combat abilities (Shield Bash, Revenge, Melee). Triggers Weegli's door destruction sequence.

**OnScriptEventHappened**
Initiates the post-gossip dialogue sequence by setting `postGossipStep` to 1.

**switchFactionIfAlive**
Helper method. Sets a crew member's faction to hostile if they are alive.

**OnGossipSelect_npc_sergeant_bly**
Handles gossip selection. Stores player GUID and triggers the AI event.

**OnGossipHello_npc_sergeant_bly**
Displays gossip menu based on Pyramid event state. Offers final phase option if trolls are killed.

**GetAI_npc_sergeant_bly**
Factory function to create `npc_sergeant_blyAI`.

**initBlyCrewMember**
Positions and configures a crew member after cage release. Sets faction to `FACTION_FREED`.

**OnGossipHello_go_troll_cage**
Triggers cage opening. Sets event state and initializes all crew members.

**npc_weegli_blastfuseAI**
Constructor for Weegli's AI. Initializes timers and flags.

**Reset#2**
Currently empty.

**AttackStart**
Calls parent `AttackStart`.

**JustDied**
Currently empty.

**UpdateAI#2**
Manages Weegli's combat, health regeneration for crew, aggro synchronization, and bomb/ranged attacks.

**MovementInform**
Handles Weegli's movement waypoints, including the door destruction sequence (summoning explosive, detonating, opening door, despawning).

**OnScriptEventHappened#2**
Calls `DestroyDoor()`.

**DestroyDoor**
Prepares Weegli to destroy the door: sets faction, moves to door, plays say line.

**RunAfterExplosion1**
Moves Weegli away from the door after summoning the explosive.

**RunAfterExplosion2**
Moves Weegli further away after the explosion, leading to despawn.

**OnGossipSelect_npc_weegli_blastfuse**
Handles gossip selection. Triggers Weegli's door destruction sequence.

**OnGossipHello_npc_weegli_blastfuse**
Displays gossip menu based on Pyramid event state.

**GetAI_npc_weegli_blastfuse**
Factory function to create `npc_weegli_blastfuseAI`.

**OnGossipHello_go_shallow_grave**
Randomly spawns a zombie or dead hero from a grave on first use.

**OnGossipHello_go_table_theka**
Advances Quest 2936 and shows gossip menu.

**ward_zumrahAI**
Constructor for Zumrah's AI. Initializes skeleton timer.

**Reset#3**
Resets skeleton timer and sets movement to IDLE.

**UpdateAI#3**
Maintains IDLE movement and casts Raise Skeleton every 5 seconds.

**GetAI_ward_zumrah**
Factory function to create `ward_zumrahAI`.

**OnTrigger_at_zumrah**
Triggers Zumrah's hostility when a player enters the area. Sets event state and removes immunity.

**OnTrigger_at_antusul**
Triggers Antu'sul's event. Schedules broodling summons based on patch version.

**AddSC_zulfarrak**
Registers all Zulfarrak scripts with the script manager.

---

<!-- machine-true, projected from graph.json -->

## Map — zulfarrak

*Source:* zulfarrak.cpp, zulfarrak.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| npc_sergeant_blyAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | — | — | — |
| UpdateAI | method | Creature.Main/AI, CreatureAI/AttackStart, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, CreatureAI/OnScriptEventHappened, InstanceData/GetData64, Map.Main/GetCreature, ObjectGuid/ObjectGuid#5, ScriptMgr/DoScriptText, Unit.Main/GetUnit, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, Unit.Main/SetFactionTemplateId | — | — |
| OnScriptEventHappened | method | — | — | — |
| switchFactionIfAlive | method | InstanceData/GetData64, Map.Main/GetCreature, ObjectGuid/ObjectGuid#5, Unit.Main/IsAlive, Unit.Main/SetFactionTemplateId | — | — |
| OnGossipSelect_npc_sergeant_bly | function | Creature.Main/AI, GossipDef/CloseGossip, Object/GetGUID | — | — |
| OnGossipHello_npc_sergeant_bly | function | GossipDef/AddMenuItem#4, GossipDef/SendGossipMenu, InstanceData/GetData, Object/GetGUID, ObjectGuid/ObjectGuid#5, PlayerMenu/GetGossipMenu, WorldObject.Object/GetInstanceData | — | — |
| GetAI_npc_sergeant_bly | function | — | — | — |
| initBlyCrewMember | function | Creature.Main/SetCombatStartPosition, Creature.Main/SetHomePosition, Creature.MotionMaster/MovePoint, InstanceData/GetData64, Map.Main/GetCreature, ObjectGuid/ObjectGuid#5, Unit.Main/GetMotionMaster, Unit.Main/SetFactionTemplateId | — | — |
| OnGossipHello_go_troll_cage | function | InstanceData/SetData, WorldObject.Object/GetInstanceData | — | — |
| npc_weegli_blastfuseAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset#2 | method | — | — | — |
| AttackStart | method | CreatureAI/AttackStart | — | — |
| JustDied | method | — | — | — |
| UpdateAI#2 | method | Creature.Main/AI, CreatureAI/AttackStart, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, InstanceData/GetData, InstanceData/GetData64, Map.Main/GetCreature, ObjectGuid/ObjectGuid#5, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/GetVictim, Unit.Main/IsAlive, Unit.Main/IsAttackReady, Unit.Main/SelectHostileTarget, Unit.Main/SetHealthPercent, Unit.Main/SetSheath, WorldObject.Object/GetMap | — | — |
| MovementInform | method | Creature.Main/ForcedDespawn, Creature.Main/SetCombatStartPosition, Creature.Main/SetHomePosition, Creature.MotionMaster/MovePoint, GameObject/SetSpellId, GameObject/UseDoorOrButton, InstanceData/GetData, InstanceData/GetData64, InstanceData/SetData, Map.Main/GetCreature, Map.Main/GetGameObject, Object/GetGUID, ObjectGuid/ObjectGuid#5, ScriptedInstance/DoUseDoorOrButton, ScriptMgr/DoScriptText, Unit.Main/GetMotionMaster, Unit.Main/SetWalk, WorldObject.Object/GetMap, WorldObject.Object/SummonGameObject | — | — |
| OnScriptEventHappened#2 | method | — | — | — |
| DestroyDoor | method | Creature.Main/SetCombatStartPosition, Creature.MotionMaster/MovePoint, ScriptMgr/DoScriptText, Unit.Main/GetMotionMaster, Unit.Main/IsAlive, Unit.Main/SetFactionTemplateId, Unit.Main/SetWalk | — | — |
| RunAfterExplosion1 | method | Creature.Main/SetCombatStartPosition, Creature.MotionMaster/MovePoint, Unit.Main/GetMotionMaster, Unit.Main/IsAlive | — | — |
| RunAfterExplosion2 | method | Creature.Main/SetCombatStartPosition, Creature.MotionMaster/MovePoint, Unit.Main/GetMotionMaster, Unit.Main/IsAlive | — | — |
| OnGossipSelect_npc_weegli_blastfuse | function | Creature.Main/AI, CreatureAI/OnScriptEventHappened, GossipDef/CloseGossip | — | — |
| OnGossipHello_npc_weegli_blastfuse | function | GossipDef/AddMenuItem#4, GossipDef/SendGossipMenu, InstanceData/GetData, Object/GetGUID, ObjectGuid/ObjectGuid#5, PlayerMenu/GetGossipMenu, WorldObject.Object/GetInstanceData | — | — |
| GetAI_npc_weegli_blastfuse | function | — | — | — |
| OnGossipHello_go_shallow_grave | function | GameObject/AddUse, GameObject/GetUseCount, shared_Util/urand, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SummonCreature#2 | — | — |
| OnGossipHello_go_table_theka | function | GossipDef/SendGossipMenu, Object/GetGUID, ObjectGuid/ObjectGuid#5, Player.Main/AreaExploredOrEventHappens, Player.Main/GetQuestStatus | — | — |
| ward_zumrahAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#3 | method | Creature.Main/SetDefaultMovementType | — | — |
| UpdateAI#3 | method | Creature.Main/SetDefaultMovementType, CreatureAI/DoCastSpellIfCan | — | — |
| GetAI_ward_zumrah | function | — | — | — |
| OnTrigger_at_zumrah | function | InstanceData/SetData, ScriptMgr/DoScriptText, Unit.Main/GetFactionTemplateId, Unit.Main/IsAlive, Unit.Main/SetFactionTemplateId, WorldObject.Object/FindNearestCreature, WorldObject.Object/GetInstanceData, WorldObject.Object/RemoveFlag | — | — |
| OnTrigger_at_antusul | function | Creature.Main/SetInCombatWithZone, Creature.MotionMaster/MovePoint, InstanceData/GetData, InstanceData/SetData, ScriptMgr/DoScriptText, Unit.Main/GetMotionMaster, Unit.Main/IsAlive, Unit.Main/IsInCombat, World/GetWowPatch, WorldObject.Object/FindNearestCreature, WorldObject.Object/GetInstanceData, WorldObject.Object/SummonCreature#2 | — | — |
| AddSC_zulfarrak | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
