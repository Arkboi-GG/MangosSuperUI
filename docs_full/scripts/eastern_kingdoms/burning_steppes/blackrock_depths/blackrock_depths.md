<!-- provenance: failed-members -->
# blackrock_depths

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Blackrock Depths Instance Scripts (`blackrock_depths`)

## Purpose & Responsibilities

This translation unit implements the scripted behaviors, event logic, and AI for specific encounters and quests within the **Blackrock Depths** dungeon instance. It serves as the bridge between game world objects (Creatures, GameObjects, Players) and the instance-specific state managed by `ScriptedInstance`.

Key responsibilities include:
1.  **The Ring of Law:** Managing the arena challenge event led by Grimstone, including mob spawning, gate control, and wipe detection.
2.  **The Grim Guzzler:** Implementing the complex social and combat interactions involving Mistress Nagmara, Private Rocknot, Boss Plugger Spazzring, and the bar patrons (Phalanx). This includes gossip menus, escort paths, pickpocketing mechanics, and combat transitions.
3.  **Jail Break Quest:** Orchestrating the multi-NPC escort sequence for Marshal Windsor, Reginald Windsor, Dughal Stormwing, and Tobias Seecer, including cell door interactions and combat encounters with prisoners.
4.  **Specific Encounters:** AI for Watchman Doomgrip, Golem Lord Argelmach, and area triggers for the Shadowforge Bridge and Dark Keeper portraits.
5.  **Spell Mechanics:** Custom logic for the "Five Fat Finger Exploding Heart Technique" spell.

The unit relies heavily on `ScriptedInstance` (accessed via `GetInstanceData`) to track the state of these events (e.g., `TYPE_RING_OF_LAW`, `TYPE_PLUGGER`, `TYPE_QUEST_JAIL_BREAK`) using enums defined in `blackrock_depths.h`.

## Member-by-Member Behavior

### The Ring of Law Event

*   **`AreaTrigger_at_ring_of_law`**: Triggered when a player enters the Ring of Law area. If the event is not started, it summons `NPC_GRIMSTONE` and sets the instance data `TYPE_RING_OF_LAW` to `IN_PROGRESS`. If the event is already in progress or done, it notifies Grimstone's AI that a player has re-entered (handling post-wipe scenarios).
*   **`npc_grimstoneAI`**: The AI for Grimstone, inheriting from `npc_escortAI`.
    *   **Constructor**: Initializes instance data, picks a random mob spawn ID, and calls `Reset`.
    *   **`Reset`**: Resets internal timers, mob counts, GUIDs, and flags. Sets Grimstone to "spawning" state.
    *   **`DoGate`**: Helper to change the state of arena gates (`DATA_ARENA1` through `DATA_ARENA4`) using instance data.
    *   **`SummonRingMob`**: Spawns a random mob from the `RingMob` array at a fixed location, moves it to the center, and tracks its GUID. Stops if `MAX_MOB_AMOUNT` (8) is reached.
    *   **`SummonRingBoss`**: Spawns a boss. If the `DATA_THELDREN` quest is active, it spawns a specific group (Theldren + healer + DPS). Otherwise, it spawns a random boss from the `RingBoss` array. Tracks the boss GUID.
    *   **`WaypointReached`**: Handles Grimstone's movement phases. At waypoint 5, it marks the event as `DONE`.
    *   **`UpdateAI`**: The main loop. It manages timers for mob deaths (`MobDeath_Timer`) and event progression (`Event_Timer`). It cycles through `EventPhase` states to control dialogue, gate opening/closing, teleportation, and mob/boss summoning. It delegates movement to `npc_escortAI::UpdateAI` when `CanWalk` is true.
    *   **`CheckForWipe`**: Checks if all players in combat near Grimstone are dead. If so, it opens gates, resets the event state if the boss hasn't spawned, despawns mobs, and resets Grimstone.
    *   **`PlayerEnteredArena`**: Called by the area trigger. If a wipe occurred and the boss is still alive, it closes the jail entrance gate and forces the boss to attack the entering player.
*   **`GetAI_npc_grimstone`**: Factory function to create `npc_grimstoneAI`.

### The Grim Guzzler Event

*   **`GOHello_go_shadowforge_brazier`**: Toggles the `TYPE_LYCEUM` instance state between `IN_PROGRESS` and `DONE`.
*   **`mob_phalanxAI`**: AI for the bar patron Phalanx.
    *   **Constructor**: Initializes instance data and timers.
    *   **`Reset`**: Resets timers and orientation.
    *   **`Activate`**: Called when Plugger dies or Rocknot breaks the keg. Sets Phalanx to hostile faction, moves him to a combat position, and starts a timer to trigger the patrol event (`TYPE_PATROL`).
    *   **`MovementInform`**: Handles pathfinding completion.
    *   **`UpdateAI`**: Manages combat abilities: Thunder Clap, Fireball Volley (below 51% health), and Mighty Blow. Performs melee attacks. Also checks the patrol timer.
*   **`GetAI_mob_phalanx`**: Factory function.
*   **`npc_mistress_nagmaraAI`**: AI for Mistress Nagmara.
    *   **Constructor**: Initializes instance data.
    *   **`Reset`**: Resets phase and timer.
    *   **`DoPotionOfLoveIfCan`**: Removes gossip/quest flags from herself and Rocknot. Starts following Rocknot and sets phase to 1.
    *   **`UpdateAI`**: Manages a multi-phase sequence:
        *   Phase 1: Moves towards Rocknot.
        *   Phase 2: Says dialogue.
        *   Phase 3: Casts `SPELL_POTION_LOVE` on Rocknot, setting instance data `TYPE_NAGMARA` to `SPECIAL`.
        *   Phase 4: Faces Rocknot.
        *   Phase 5: Loops emotes and spells while "kissing" Rocknot.
*   **`GossipHello_npc_mistress_nagmara`**: Displays gossip menu. Adds a special item if the player has completed `QUEST_POTION_LOVE`.
*   **`GossipSelect_npc_mistress_nagmara`**: Handles gossip selection. Action 1 triggers `DoPotionOfLoveIfCan`.
*   **`QuestRewarded_npc_mistress_nagmara`**: Triggers `DoPotionOfLoveIfCan` when `QUEST_POTION_LOVE` is turned in.
*   **`GetAI_npc_mistress_nagmara`**: Factory function.
*   **`npc_rocknotAI`**: AI for Private Rocknot, inheriting from `npc_escortAI`.
    *   **Constructor**: Initializes instance data.
    *   **`Reset`**: Resets timers and orientation. Pauses escort if already escorting.
    *   **`WaypointReached`**: Handles escort waypoints.
        *   WP 0: Pauses if Nagmara event is in progress, jumps to WP 9.
        *   WP 6: Starts timer to break keg.
        *   WP 9: Makes Nagmara follow if she exists.
        *   WP 16: Opens bar back door.
        *   WP 33: Ends escort, positions Nagmara, and hands control to her AI.
    *   **`UpdateEscortAI`**: Manages timers for breaking the keg (`m_uiBreakKegTimer`), breaking the door (`m_uiBreakDoorTimer`), and reacting to the bar (`m_uiBarReactTimer`). If `TYPE_NAGMARA` is `SPECIAL`, it starts the second part of the escort. Activates Phalanx when the keg is broken.
*   **`GetAI_npc_rocknot`**: Factory function.
*   **`QuestRewarded_npc_rocknot`**: Handles turning in `QUEST_ALE`. Sets instance data to `SPECIAL` or `IN_PROGRESS` depending on count, triggers dialogue, and sets an emote timer on Rocknot.
*   **`boss_plugger_spazzringAI`**: AI for Boss Plugger Spazzring.
    *   **Constructor**: Initializes instance data and timers.
    *   **`Reset`**: Resets timers.
    *   **`Aggro`**: Sets initial combat timers.
    *   **`JustDied`**: Sets `TYPE_PLUGGER` to `IN_PROGRESS` and `EVENT_BAR_PATRONS` to `PATRON_HOSTILE`. Activates Phalanx.
    *   **`SpellHit`**: Detects if a player used `SPELL_PICKPOCKET` on Plugger, starting a pickpocket timer.
    *   **`WarnThief`**: Plays a random yell and faces the thief.
    *   **`AttackThief`**: Sets faction to hostile, plays aggro yell, and attacks the thief.
    *   **`UpdateAI`**:
        *   **Combat**: Casts Banish, Immolate, Shadow Bolt, and Curse of Tongues on targets. Melee attacks.
        *   **Out of Combat**: Plays random OOC dialogue. If pickpocket timer expires, yells and becomes hostile. Maintains Demon Armor buff.
*   **`GetAI_boss_plugger_spazzring`**: Factory function.
*   **`GOUse_go_bar_ale_mug`**: Triggered when a player uses an ale mug. Increments the stolen item count via instance data (`TYPE_PLUGGER` -> `SPECIAL`). If the cap is reached (state becomes `IN_PROGRESS`), it triggers `AttackThief` on Plugger; otherwise, it triggers `WarnThief`.

### Other Encounters & Quests

*   **`GOHello_go_dark_keeper_portrait`**: Summons a random Dark Keeper creature and corresponding portrait GameObject. Sets `TYPE_VAULT` to `DONE`.
*   **`GOHello_go_thunderbrew_laguer_keg`**: If `TYPE_THUNDERBREW` is not done, sets it to `IN_PROGRESS`. If done, summons Hurley Blackbreath and his cronies, grouping them together.
*   **`GOHello_go_relic_coffer_door`**: Sets `TYPE_RELIC_COFFER` to `IN_PROGRESS` then `SPECIAL`. If `DONE`, summons Ruinepoigne who yells and attacks the player.
*   **`npc_watchman_doomgripAI`**:
    *   **`JustDied`**: Sets `TYPE_DOOMGRIP` to `DONE`.
    *   **`Aggro`**: Finds nearby Warbringer Constructs, removes their immune/spawning flags, and makes them attack the aggro source.
    *   **`UpdateAI`**: Casts healing potion below 51% health and armor-shattering spell. Melee attacks.
*   **`GetAI_npc_watchman_doomgrip`**: Factory function.
*   **`npc_golem_lord_argelmachAI`**:
    *   **`Aggro`**: Moves to a specific point and sets `DATA_ARGELMACH_AGGRO` to `IN_PROGRESS`.
    *   **`JustDied`**: Sets `DATA_ARGELMACH_AGGRO` to `DONE`.
    *   **`UpdateAI`**: Maintains Lightning Shield aura. Casts Chain Lightning and Hurricane. Melee attacks.
*   **`GetAI_npc_golem_lord_argelmach`**: Factory function.
*   **`AreaTrigger_at_shadowforge_bridge`**: Summons two Anvilrage Guards who attack the player. Sets `TYPE_BRIDGE` to `DONE`.

### Jail Break Quest

*   **`npc_dughal_stormwingAI`**: Escort AI for Dughal.
    *   **`Reset`**: Empty override, relying on parent initialization.
    *   **`WaypointReached`**: Updates instance data for `TYPE_JAIL_DUGHAL`.
    *   **`UpdateEscortAI`**: Toggles visibility based on quest state.
    *   **`OnScriptEventHappened`**: Starts the escort when triggered by a player.
*   **`GetAI_npc_dughal_stormwing`**: Factory function.
*   **`npc_marshal_reginald_windsorAI`**: Escort AI for Reginald Windsor.
    *   **`Reset`**: Resets waypoint tracker and encounter flag if not paused.
    *   **`DoJailBreakQuestCredit`**: Awards quest credit to the escort player.
    *   **`WaypointReached`**: Handles dialogue and pausing at specific cells (Jaz, Shill, Crest, Tobias). Triggers combat with prisoners if their doors are open.
    *   **`EnterCombat`**: Casts frenzy spell and plays dialogue based on attacker.
    *   **`EnterEvadeMode`**: Removes frenzy spell.
    *   **`JustDied`**: Fails the quest.
    *   **`UpdateEscortAI`**: Checks for prisoner deaths to resume escort.
*   **`GetAI_npc_marshal_reginald_windsor`**: Factory function.
*   **`npc_marshal_windsorAI`**: Escort AI for Marshal Windsor.
    *   **`Reset`**: Resets waypoint tracker and dialogue flag if not paused.
    *   **`WaypointReached`**: Handles dialogue, emotes, and opening supply room doors. At WP 19, despawns himself and summons Reginald Windsor to continue the escort.
    *   **`Aggro`**: Plays random aggro dialogue.
    *   **`JustDied`**: Fails the quest.
    *   **`UpdateEscortAI`**: Handles dialogue timing for Dughal's cell status.
*   **`GetAI_npc_marshal_windsor`**: Factory function.
*   **`QuestAccept_npc_marshal_windsor`**: Starts the Jail Break quest event, setting instance data and starting Windsor's escort.
*   **`npc_tobias_seecherAI`**: Escort AI for Tobias.
    *   **`Reset`**: Empty override, relying on parent initialization.
    *   **`WaypointReached`**: Updates `TYPE_JAIL_TOBIAS` and toggles visibility.
    *   **`UpdateEscortAI`**: Toggles visibility based on quest state.
    *   **`OnScriptEventHappened`**: Starts the escort when triggered by a player.
*   **`GetAI_npc_tobias_seecher`**: Factory function.
*   **`go_cell_doorAI`**:
    *   **`OnUse`**: Sets instance data for the door entry. If it's Crest's door, triggers dialogue on Crest.
*   **`GetAI_go_cell_door`**: Factory function to create `go_cell_doorAI`.

### Spell Script

*   **`FiveFatFingerExplodingHeartTechniqueScript`**: Aura script for spell 27673.
    *   **`OnAuraInit`**: Records target's original position.
    *   **`OnPeriodicTrigger`**: If stacks >= 5 and target moved >= 5 yards, changes spell to "Exploding Heart".
    *   **`OnPeriodicTickEnd`**: If stacks >= 5 and target moved >= 5 yards, removes the aura.
*   **`GetScript_FiveFatFingerExplodingHeartTechnique`**: Factory function.

### Registration

*   **`AddSC_blackrock_depths`**: Registers all scripts defined in this unit with the `ScriptMgr`.

## Cross-Unit Boundaries

*   **`InstanceData` (`ScriptedInstance`)**: Almost every member interacts with `ScriptedInstance` via `GetInstanceData()`. This is the primary mechanism for sharing state (e.g., event progress, GUIDs of key NPCs) across different scripts in the instance. Methods like `GetData`, `SetData`, `GetData64`, and `GetCreature` are called extensively.
*   **`WorldObject.Object`**: Used for basic object operations: `GetInstanceData`, `SummonCreature`, `SummonGameObject`, `GetMap`, `GetPositionX/Y/Z`, `SetFlag`, `RemoveFlag`, `GetDistance`, `GetRandomPoint`, `GetContactPoint`.
*   **`Creature.Main` / `Unit.Main`**: Used for creature/unit-specific operations: `SetHomePosition`, `SetInCombatWithZone`, `GetMotionMaster`, `IsAlive`, `IsDead`, `SelectHostileTarget`, `GetVictim`, `GetHealthPercent`, `SetFactionTemplateId`, `SetVisibility`, `CastSpell`, `RemoveAurasDueToSpell`, `HandleEmote`, `FindNearestCreature`.
*   **`Creature.MotionMaster`**: Used for movement: `MovePoint`, `MoveFollow`, `MoveIdle`, `MoveWaypoint`, `Clear`.
*   **`ScriptedEscortAI`**: Inherited by Grimstone, Rocknot, Dughal, Reginald Windsor, Marshal Windsor, and Tobias. Provides `Start`, `UpdateAI`/`UpdateEscortAI`, `WaypointReached`, `GetPlayerForEscort`, `SetEscortPaused`, `setCurrentWP`, `HasEscortState`, `EnterCombat`, `EnterEvadeMode`.
*   **`ScriptedAI`**: Inherited by Phalanx, Nagmara, Doomgrip, Argelmach, and Plugger. Provides base AI functionality.
*   **`GameObject`**: Used for `SetGoState`, `GetGoState`, `Delete`.
*   **`ScriptMgr`**: Used for `DoScriptText` to play dialogue.
*   **`Log.Main`**: Used for debugging output (`sLog.Out`).
*   **`Map.Main`**: Used for `GetCreature`, `GetGameObject`, `GetPlayer`, `GetUnit`, `GetPlayers`.
*   **`ObjectGuid`**: Used for creating and manipulating GUIDs.
*   **`shared_Util`**: Used for `urand` (uniform random).
*   **`GossipDef` / `PlayerMenu`**: Used for gossip menu interactions (`AddMenuItem`, `SendGossipMenu`, `CloseGossip`).
*   **`QuestDef` / `Player.Main`**: Used for quest interactions (`GetQuestId`, `GetQuestRewardStatus`, `PrepareQuestMenu`, `GroupEventHappens`, `GroupEventFailHappens`).
*   **`Aura` / `SpellMgr`**: Used in the spell script for aura manipulation and spell entry lookup.
*   **`GridSearchers`**: Used for `GetCreatureListWithEntryInGrid` and `GetClosestCreatureWithEntry`.
*   **`Script` / `ScriptMgr/RegisterSelf`**: Used in `AddSC_blackrock_depths` to register scripts.

## Data Model

This unit does not directly interact with database tables via SQL queries. All state management is handled in-memory through the `ScriptedInstance` system, which likely persists state to the database at a higher level (not visible in this unit). The enums in `blackrock_depths.h` define the keys used for this in-memory state.

## Notable Implementation Details

*   **Wipe Handling in Ring of Law**: `npc_grimstoneAI::CheckForWipe` detects wipes by checking if any players in combat are within 80 yards. If a wipe is detected before the boss spawns, the event resets completely. If after, it keeps the boss alive and forces aggro on re-entering players.
*   **Grim Guzzler State Machine**: The interaction between Nagmara, Rocknot, and Plugger is complex. Nagmara's gossip/quest reward triggers her AI, which interacts with Rocknot's escort AI. Rocknot's escort triggers Phalanx's activation. Plugger's death or pickpocketing triggers hostility. The instance data `TYPE_PLUGGER`, `TYPE_NAGMARA`, `TYPE_ROCKNOT`, and `EVENT_BAR_PATRONS` coordinate these states.
*   **Pickpocket Mechanic**: `boss_plugger_spazzringAI::SpellHit` detects `SPELL_PICKPOCKET`. Each successful pickpocket increments a counter (via instance data `SPECIAL` state). After 3 steals, Plugger becomes hostile.
*   **Jail Break Escort Chain**: Marshal Windsor's escort ends by summoning Reginald Windsor, who continues the escort. This handoff is managed in `npc_marshal_windsorAI::WaypointReached` at WP 19.
*   **Cell Door Logic**: `go_cell_doorAI::OnUse` sets instance data for the door. The marshal's AI checks this data to decide whether to trigger combat with the prisoner behind the door.
*   **Hardcoded Coordinates**: Many spawn points and movement targets are hardcoded floats (e.g., in `SummonRingMob`, `SummonRingBoss`, `AreaTrigger_at_shadowforge_bridge`).
*   **French Comments/Names**: Some variable names and comments are in French (e.g., `BoireLaPotionDeSoins`, `FracasserArmure`, `BouclierDeFoudre`), indicating legacy code or developer origin.
*   **Magic Numbers**: Timers and thresholds (e.g., 51% health for special abilities, 5 yards for spell explosion) are hardcoded.
*   **Array Bounds**: `SummonRingMob` checks `MobCount >= MAX_MOB_AMOUNT` to prevent overflow.
*   **Null Checks**: Extensive null checks for instance data, creatures, and players are present, which is good practice.
*   **Dynamic Casting**: Heavy use of `dynamic_cast` to access specific AI classes from generic `CreatureAI*` pointers.

## Member Reference

**GOHello_go_shadowforge_brazier**: Toggles `TYPE_LYCEUM` instance state between `IN_PROGRESS` and `DONE` when the brazier is clicked.

**npc_grimstoneAI**: Constructor initializes instance data, random mob ID, and calls `Reset`. Inherits from `npc_escortAI`.

**Reset#5**: Resets Grimstone's internal state: timers, mob counts, GUIDs, flags, and sets spawning flag.

**DoGate**: Helper method to change the state of arena gates (`DATA_ARENA1`-`DATA_ARENA4`) using instance data. Logs debug message.

**SummonRingMob**: Spawns a random mob from `RingMob` array at a fixed location, moves it to the center, tracks its GUID, and increments `MobCount`. Stops if `MAX_MOB_AMOUNT` is reached.

**SummonRingBoss**: Spawns a boss. If `DATA_THELDREN` quest is active, spawns Theldren + healer + DPS. Otherwise, spawns random boss from `RingBoss` array. Tracks boss GUID and increments `MobCount`.

**WaypointReached#2**: Handles Grimstone's movement phases. At WP 5, marks event as `DONE`. Plays dialogue at other WPs.

**UpdateAI#4**: Main loop for Grimstone. Manages mob death timer and event phase timer. Cycles through `EventPhase` states to control dialogue, gates, teleportation, and mob/boss summoning. Delegates movement to parent class when walking.

**CheckForWipe**: Checks if all players in combat near Grimstone are dead. If so, opens gates, resets event if boss not spawned, despawns mobs, and resets Grimstone. Returns true if wiped.

**PlayerEnteredArena**: Called by area trigger. If wipe occurred and boss alive, closes jail gate and forces boss to attack entering player.

**GetAI_npc_grimstone**: Factory function to create `npc_grimstoneAI`. Checks for instance data.

**AreaTrigger_at_ring_of_law**: Triggered on player entry. Summons Grimstone and starts event if not started. Notifies Grimstone if event in progress/done.

**mob_phalanxAI**: Constructor initializes instance data and timers. Inherits from `ScriptedAI`.

**Reset#2**: Resets Phalanx's timers and orientation.

**Activate**: Called when Plugger dies or keg broken. Sets Phalanx hostile, moves to combat position, starts patrol timer.

**MovementInform**: Handles pathfinding completion for Phalanx.

**UpdateAI#2**: Manages Phalanx's combat abilities (Thunder Clap, Fireball Volley <51%, Mighty Blow) and melee attacks. Checks patrol timer.

**GetAI_mob_phalanx**: Factory function to create `mob_phalanxAI`.

**npc_mistress_nagmaraAI**: Constructor initializes instance data. Inherits from `ScriptedAI`.

**Reset#8**: Resets Nagmara's phase and timer.

**DoPotionOfLoveIfCan**: Removes gossip/quest flags from Nagmara and Rocknot. Starts following Rocknot, sets phase to 1.

**UpdateAI#5**: Manages Nagmara's multi-phase sequence: move to Rocknot, dialogue, cast love potion, face Rocknot, loop emotes/spells.

**GossipHello_npc_mistress_nagmara**: Displays gossip menu. Adds special item if `QUEST_POTION_LOVE` completed.

**GossipSelect_npc_mistress_nagmara**: Handles gossip selection. Action 1 triggers `DoPotionOfLoveIfCan`.

**QuestRewarded_npc_mistress_nagmara**: Triggers `DoPotionOfLoveIfCan` when `QUEST_POTION_LOVE` turned in.

**GetAI_npc_mistress_nagmara**: Factory function to create `npc_mistress_nagmaraAI`.

**npc_rocknotAI**: Constructor initializes instance data. Inherits from `npc_escortAI`.

**Reset#9**: Resets Rocknot's timers and orientation. Pauses escort if already escorting.

**WaypointReached#5**: Handles Rocknot's escort waypoints. Pauses/jumps for Nagmara event. Breaks keg/door. Opens bar door. Hands control to Nagmara at end.

**UpdateEscortAI#4**: Manages Rocknot's timers for keg/door breaking and bar reaction. Starts second escort part if Nagmara event special. Activates Phalanx.

**GetAI_npc_rocknot**: Factory function to create `npc_rocknotAI`.

**QuestRewarded_npc_rocknot**: Handles `QUEST_ALE` turn-in. Sets instance state, triggers dialogue, sets emote timer.

**GOHello_go_dark_keeper_portrait**: Summons random Dark Keeper and portrait. Sets `TYPE_VAULT` to `DONE`.

**GOHello_go_thunderbrew_laguer_keg**: Sets `TYPE_THUNDERBREW` to `IN_PROGRESS`. If done, summons Hurley and cronies, groups them.

**GOHello_go_relic_coffer_door**: Sets `TYPE_RELIC_COFFER` to `IN_PROGRESS`/`SPECIAL`. If `DONE`, summons Ruinepoigne who attacks player.

**npc_watchman_doomgripAI**: Constructor initializes instance data. Inherits from `ScriptedAI`.

**JustDied#5**: Sets `TYPE_DOOMGRIP` to `DONE`.

**Reset#11**: Resets Doomgrip's timers.

**Aggro#4**: Finds nearby Warbringer Constructs, removes immune/spawning flags, makes them attack aggro source.

**UpdateAI#6**: Casts healing potion <51% health and armor-shattering spell. Melee attacks.

**GetAI_npc_watchman_doomgrip**: Factory function to create `npc_watchman_doomgripAI`.

**npc_golem_lord_argelmachAI**: Constructor initializes instance data.

---

<!-- machine-true, projected from graph.json -->

## Map — blackrock_depths

*Source:* blackrock_depths.cpp, blackrock_depths.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GOHello_go_shadowforge_brazier | function | InstanceData/GetData, InstanceData/SetData, WorldObject.Object/GetInstanceData | — | — |
| npc_grimstoneAI | ctor | ScriptedEscortAI/npc_escortAI, shared_Util/urand, WorldObject.Object/GetInstanceData | — | — |
| Reset#5 | method | WorldObject.Object/SetFlag | — | — |
| DoGate | method | GameObject/SetGoState, InstanceData/GetData64, Log.Main/Out, Map.Main/GetGameObject, ObjectGuid/ObjectGuid#5 | — | — |
| SummonRingMob | method | Creature.Main/SetHomePosition, Creature.Main/SetInCombatWithZone, Creature.MotionMaster/MovePoint, Object/GetGUID, Unit.Main/GetMotionMaster, WorldObject.Object/SummonCreature#2 | — | — |
| SummonRingBoss | method | Creature.Main/SetHomePosition, Creature.Main/SetInCombatWithZone, Creature.MotionMaster/MovePoint, InstanceData/GetData, InstanceData/GetData64, Log.Main/Out, Map.Main/GetPlayer, Object/GetGUID, ObjectGuid/ObjectGuid#5, Unit.Main/GetMotionMaster, WorldObject.Object/GetMap, WorldObject.Object/SummonCreature#2 | — | — |
| WaypointReached#2 | method | InstanceData/GetData, InstanceData/SetData, Log.Main/Out, ScriptMgr/DoScriptText | — | — |
| UpdateAI#4 | method | CreatureAI/DoCastSpellIfCan, Map.Main/GetCreature, ObjectGuid/ObjectGuid#5, ScriptedEscortAI/Start, ScriptedEscortAI/UpdateAI, ScriptMgr/DoScriptText, shared_Util/urand, Unit.Main/IsAlive, Unit.Main/IsDead, Unit.Main/SetVisibility, WorldObject.Object/GetMap | — | — |
| CheckForWipe | method | Creature.Main/ForcedDespawn, InstanceData/SetData, Map.Main/GetCreature, Map.Main/GetPlayers, ObjectGuid/ObjectGuid#5, Unit.Main/IsInCombat, WorldObject.Object/GetMap, WorldObject.Object/IsWithinDistInMap | — | — |
| PlayerEnteredArena | method | ObjectGuid/ObjectGuid#5, Unit.Main/SetInCombatWith, ZoneScript/GetCreature | — | — |
| GetAI_npc_grimstone | function | WorldObject.Object/GetInstanceData | — | — |
| AreaTrigger_at_ring_of_law | function | Creature.Main/AI, InstanceData/GetData, InstanceData/GetData64, InstanceData/SetData, ObjectGuid/ObjectGuid#5, WorldObject.Object/GetInstanceData, WorldObject.Object/SummonCreature#2, ZoneScript/GetCreature | — | — |
| mob_phalanxAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset#2 | method | — | — | — |
| Activate | method | Creature.Main/SetHomePosition, Creature.MotionMaster/MovePoint, InstanceData/GetData, InstanceData/SetData, ScriptMgr/DoScriptText, Unit.Main/GetMotionMaster, Unit.Main/SetFactionTemplateId | — | — |
| MovementInform | method | Creature.MotionMaster/MovePoint, Unit.Main/GetMotionMaster | — | — |
| UpdateAI#2 | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, InstanceData/GetData, InstanceData/SetData, Unit.Main/GetHealthPercent, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_mob_phalanx | function | — | — | — |
| npc_mistress_nagmaraAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset#8 | method | — | — | — |
| DoPotionOfLoveIfCan | method | Creature.MotionMaster/MoveFollow, Creature.MotionMaster/MoveIdle, InstanceData/GetData64, Map.Main/GetCreature, ObjectGuid/ObjectGuid#5, Unit.Main/GetMotionMaster, WorldObject.Object/GetMap, WorldObject.Object/RemoveFlag | — | — |
| UpdateAI#5 | method | Creature.MotionMaster/MoveFollow, Creature.MotionMaster/MoveIdle, CreatureAI/DoCastSpellIfCan, InstanceData/SetData, ScriptMgr/DoScriptText, SpellCaster/CastSpell#2, Unit.Main/GetMotionMaster, Unit.Main/SetFacingToObject, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/IsWithinDist2d | — | — |
| GossipHello_npc_mistress_nagmara | function | GossipDef/AddMenuItem#5, GossipDef/SendGossipMenu, Object/GetObjectGuid, Player.Main/GetQuestRewardStatus, Player.Main/PrepareQuestMenu, PlayerMenu/GetGossipMenu, Unit.Main/IsQuestGiver | — | — |
| GossipSelect_npc_mistress_nagmara | function | Creature.Main/AI, GossipDef/CloseGossip | — | — |
| QuestRewarded_npc_mistress_nagmara | function | Creature.Main/AI, QuestDef/GetQuestId, WorldObject.Object/GetInstanceData | — | — |
| GetAI_npc_mistress_nagmara | function | — | — | — |
| npc_rocknotAI | ctor | ScriptedEscortAI/npc_escortAI, WorldObject.Object/GetInstanceData | — | — |
| Reset#9 | method | InstanceData/GetData64, Map.Main/GetCreature, ObjectGuid/ObjectGuid#5, ScriptedEscortAI/HasEscortState, WorldObject.Object/GetMap | — | — |
| WaypointReached#5 | method | Creature.Main/AI, Creature.MotionMaster/MoveFollow, Creature.MotionMaster/MoveIdle, Creature.MotionMaster/MovePoint, CreatureAI/DoCastSpellIfCan, GameObject/GetGoState, GameObject/SetGoState, InstanceData/GetData, InstanceData/GetData64, ObjectGuid/ObjectGuid#5, ScriptedEscortAI/setCurrentWP, ScriptedEscortAI/SetEscortPaused, ScriptMgr/DoScriptText, Unit.Main/GetMotionMaster, Unit.Main/SetFacingTo, ZoneScript/GetGameObject | — | — |
| UpdateEscortAI#4 | method | GameObject/GetGoState, GameObject/SetGoState, InstanceData/GetData, InstanceData/GetData64, InstanceData/SetData, Map.Main/GetGameObject, Map.Main/GetUnit, ObjectGuid/ObjectGuid, ObjectGuid/ObjectGuid#5, ScriptedEscortAI/Start, ScriptMgr/DoScriptText, Unit.Main/AI, Unit.Main/HandleEmote, Unit.Main/IsAlive, WorldObject.Object/GetMap, ZoneScript/GetGameObject | — | — |
| GetAI_npc_rocknot | function | — | — | — |
| QuestRewarded_npc_rocknot | function | Creature.Main/AI, InstanceData/GetData, InstanceData/SetData, QuestDef/GetQuestId, ScriptMgr/DoScriptText, Unit.Main/SetFacingToObject, WorldObject.Object/GetInstanceData | — | — |
| GOHello_go_dark_keeper_portrait | function | InstanceData/GetData, InstanceData/SetData, shared_Util/urand, WorldObject.Object/GetInstanceData, WorldObject.Object/SummonCreature#2, WorldObject.Object/SummonGameObject | — | — |
| GOHello_go_thunderbrew_laguer_keg | function | Creature.Main/JoinCreatureGroup, Creature.MotionMaster/MoveWaypoint, InstanceData/GetData, InstanceData/SetData, MotionMaster/Clear, Unit.Main/GetMotionMaster, Unit.Main/SetWalk, WorldObject.Object/GetInstanceData, WorldObject.Object/GetRandomPoint, WorldObject.Object/SummonCreature#2 | — | — |
| GOHello_go_relic_coffer_door | function | Creature.Main/AI, CreatureAI/AttackStart, InstanceData/GetData, InstanceData/SetData, WorldObject.Object/GetInstanceData, WorldObject.Object/MonsterYell, WorldObject.Object/SummonCreature#2 | — | — |
| npc_watchman_doomgripAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| JustDied#5 | method | InstanceData/SetData | — | — |
| Reset#11 | method | — | — | — |
| Aggro#4 | method | Creature.Main/AI, CreatureAI/AttackStart, GridSearchers/GetCreatureListWithEntryInGrid#2, Unit.Main/IsAlive, Unit.Main/RemoveAurasDueToSpell, WorldObject.Object/RemoveFlag | — | — |
| UpdateAI#6 | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Unit.Main/GetHealthPercent, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_npc_watchman_doomgrip | function | — | — | — |
| npc_golem_lord_argelmachAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Aggro#2 | method | Creature.MotionMaster/MovePoint, InstanceData/SetData, Unit.Main/GetMotionMaster | — | — |
| JustDied#2 | method | InstanceData/SetData | — | — |
| Reset#4 | method | — | — | — |
| UpdateAI#3 | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Unit.Main/GetVictim, Unit.Main/HasAura#2, Unit.Main/SelectHostileTarget | — | — |
| GetAI_npc_golem_lord_argelmach | function | — | — | — |
| AreaTrigger_at_shadowforge_bridge | function | Creature.MotionMaster/MoveFollow, Creature.MotionMaster/MovePoint, Creature.MotionMaster/MoveWaypoint, InstanceData/GetData, InstanceData/SetData, Player.Main/IsGameMaster, ScriptMgr/DoScriptText, Unit.Main/GetMotionMaster, Unit.Main/IsAlive, Unit.Main/SetWalk, WorldObject.Object/GetContactPoint, WorldObject.Object/GetInstanceData, WorldObject.Object/SummonCreature#2 | — | — |
| boss_plugger_spazzringAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | — | — | — |
| Aggro | method | shared_Util/urand | — | — |
| JustDied | method | InstanceData/GetData64, InstanceData/SetData, Map.Main/GetUnit, ObjectGuid/ObjectGuid#5, Unit.Main/AI, Unit.Main/IsAlive, WorldObject.Object/GetMap | — | — |
| SpellHit | method | Object/GetTypeId | — | — |
| WarnThief | method | ScriptMgr/DoScriptText, shared_Util/urand, Unit.Main/SetFacingToObject | — | — |
| AttackThief | method | Creature.Main/SetFactionTemporary, CreatureAI/AttackStart, ScriptMgr/DoScriptText, shared_Util/urand, Unit.Main/SetFacingToObject | — | — |
| UpdateAI | method | Creature.Main/SelectAttackingTarget, Creature.Main/SelectAttackingTarget#2, Creature.Main/SetFactionTemporary, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, ScriptMgr/DoScriptText, shared_Util/urand, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_boss_plugger_spazzring | function | — | — | — |
| GOUse_go_bar_ale_mug | function | Creature.Main/AI, InstanceData/GetData, InstanceData/GetData64, InstanceData/SetData, ObjectGuid/ObjectGuid#5, WorldObject.Object/GetInstanceData, ZoneScript/GetCreature | — | — |
| npc_dughal_stormwingAI | ctor | ScriptedEscortAI/npc_escortAI, WorldObject.Object/GetInstanceData | — | — |
| Reset#3 | method | — | — | — |
| WaypointReached | method | InstanceData/GetData, InstanceData/SetData, ScriptedEscortAI/GetPlayerForEscort, ScriptMgr/DoScriptText | — | — |
| UpdateEscortAI | method | InstanceData/GetData, ScriptedEscortAI/UpdateEscortAI, Unit.Main/SetVisibility | — | — |
| OnScriptEventHappened | method | Object/GetObjectGuid, Object/IsPlayer, ScriptedEscortAI/Start, WorldObject.Object/RemoveFlag | — | — |
| GetAI_npc_dughal_stormwing | function | — | — | — |
| npc_marshal_reginald_windsorAI | ctor | ScriptedEscortAI/npc_escortAI, WorldObject.Object/GetInstanceData | — | — |
| Reset#6 | method | ScriptedEscortAI/HasEscortState | — | — |
| DoJailBreakQuestCredit | method | Player.Main/GroupEventHappens, ScriptedEscortAI/GetPlayerForEscort | — | — |
| WaypointReached#3 | method | InstanceData/GetData, InstanceData/SetData, ScriptedEscortAI/GetPlayerForEscort, ScriptedEscortAI/SetEscortPaused, ScriptMgr/DoScriptText, Unit.Main/HandleEmoteCommand, Unit.Main/SetFacingToObject, WorldObject.Object/FindNearestCreature, WorldObject.Object/SetFlag | — | — |
| EnterCombat | method | Object/GetEntry, ScriptedEscortAI/EnterCombat, ScriptMgr/DoScriptText, SpellCaster/CastSpell#2 | — | — |
| EnterEvadeMode | method | ScriptedEscortAI/EnterEvadeMode, Unit.Main/RemoveAurasDueToSpell | — | — |
| JustDied#3 | method | InstanceData/SetData, Player.Main/GroupEventFailHappens, ScriptedEscortAI/GetPlayerForEscort | — | — |
| UpdateEscortAI#2 | method | Creature.Main/AI, CreatureAI/AttackStart, InstanceData/GetData, InstanceData/GetData64, InstanceData/SetData, Map.Main/GetCreature, ObjectGuid/ObjectGuid#5, ScriptedEscortAI/SetEscortPaused, ScriptedEscortAI/UpdateEscortAI, ScriptMgr/DoScriptText, Unit.Main/IsAlive, Unit.Main/IsDead, Unit.Main/SetFactionTemplateId | — | — |
| GetAI_npc_marshal_reginald_windsor | function | — | — | — |
| npc_marshal_windsorAI | ctor | ScriptedEscortAI/npc_escortAI, WorldObject.Object/GetInstanceData | — | — |
| Reset#7 | method | ScriptedEscortAI/HasEscortState | — | — |
| WaypointReached#4 | method | Creature.Main/AI, GameObject/Delete, InstanceData/GetData, InstanceData/GetData64, InstanceData/SetData, Map.Main/GetGameObject, Object/GetObjectGuid, ObjectGuid/ObjectGuid#5, ScriptedEscortAI/GetPlayerForEscort, ScriptedEscortAI/SetEscortPaused, ScriptedEscortAI/Start, ScriptedInstance/DoUseDoorOrButton, ScriptMgr/DoScriptText, Unit.Main/HandleEmoteCommand, Unit.Main/SetFactionTemplateId, Unit.Main/SetVisibility, WorldObject.Object/FindNearestCreature, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SetFlag, WorldObject.Object/SummonCreature#2 | — | — |
| Aggro#3 | method | ScriptedEscortAI/GetPlayerForEscort, ScriptMgr/DoScriptText, shared_Util/urand | — | — |
| JustDied#4 | method | InstanceData/SetData, Player.Main/GroupEventFailHappens, ScriptedEscortAI/GetPlayerForEscort | — | — |
| UpdateEscortAI#3 | method | InstanceData/GetData, InstanceData/SetData, ScriptedEscortAI/GetPlayerForEscort, ScriptedEscortAI/SetEscortPaused, ScriptedEscortAI/UpdateEscortAI, ScriptMgr/DoScriptText | — | — |
| QuestAccept_npc_marshal_windsor | function | Creature.Main/AI, InstanceData/GetData, InstanceData/SetData, Object/GetObjectGuid, QuestDef/GetQuestId, ScriptedEscortAI/Start, Unit.Main/SetFactionTemplateId, WorldObject.Object/GetInstanceData | — | — |
| GetAI_npc_marshal_windsor | function | — | — | — |
| npc_tobias_seecherAI | ctor | ScriptedEscortAI/npc_escortAI, WorldObject.Object/GetInstanceData | — | — |
| Reset#10 | method | — | — | — |
| WaypointReached#6 | method | InstanceData/GetData, InstanceData/SetData, ScriptMgr/DoScriptText | — | — |
| UpdateEscortAI#5 | method | InstanceData/GetData, ScriptedEscortAI/UpdateEscortAI, Unit.Main/SetVisibility | — | — |
| OnScriptEventHappened#2 | method | Object/GetObjectGuid, Object/IsPlayer, ScriptedEscortAI/Start, WorldObject.Object/RemoveFlag | — | — |
| GetAI_npc_tobias_seecher | function | — | — | — |
| go_cell_doorAI | ctor | GameObjectAI/GameObjectAI | — | — |
| OnUse | method | GridSearchers/GetClosestCreatureWithEntry, InstanceData/SetData, Object/GetEntry, ScriptMgr/DoScriptText, WorldObject.Object/GetInstanceData | — | — |
| GetAI_go_cell_door | function | — | — | — |
| OnAuraInit | method | Aura/GetTarget, WorldObject.Object/GetPosition#3 | — | — |
| OnPeriodicTrigger | method | Aura/GetStackAmount, Aura/GetTarget, SpellMgr/GetSpellEntry, SpellMgr/Instance, WorldObject.Object/GetDistance | — | — |
| OnPeriodicTickEnd | method | Aura/GetId, Aura/GetStackAmount, Aura/GetTarget, Unit.Main/RemoveAurasDueToSpell, WorldObject.Object/GetDistance | — | — |
| GetScript_FiveFatFingerExplodingHeartTechnique | function | — | — | — |
| AddSC_blackrock_depths | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |

---

<!-- verify: failed-members | missing: Aggro#2, Aggro#3, JustDied#2, JustDied#3, JustDied#4, OnScriptEventHappened#2, Reset#10, Reset#3, Reset#4, Reset#6, Reset#7, UpdateAI#3, UpdateEscortAI#2, UpdateEscortAI#3, UpdateEscortAI#5, WaypointReached#3, WaypointReached#4, WaypointReached#6 -->
