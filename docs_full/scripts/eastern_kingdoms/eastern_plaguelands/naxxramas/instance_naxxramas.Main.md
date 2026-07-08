<!-- provenance: boundary-bleed -->
# instance_naxxramas.Main

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# instance_naxxramas

`instance_naxxramas` is the `ScriptedInstance` implementation for the Naxxramas raid instance. It manages the persistent state of the instance, including boss encounter statuses, door and gate states, teleporter availability, and specific mechanics for bosses like Gothik and Kel'Thuzad. Additionally, this unit contains AI implementations for several trash mobs (Spirit of Naxxramas, Gargoyles, Plague Slimes, Toxic Tunnels, Dark Touched Warriors), gossip scripts for the NPC Master Craftsman Omarion, and custom spell/aura scripts for specific abilities.

The unit does not interact with any database tables directly; it relies on the core's `ScriptedInstance` infrastructure to save and load instance data via the `Save()` and `Load()` methods, which serialize the `m_auiEncounter` array to/from a string stored in the core's instance data system. Note that the `Save()` method itself is implemented in the shared header `naxxramas.h` and is not part of this unit's source code.

## Member-by-Member Behavior

### Instance State Management

*   **instance_naxxramas**: Constructor initializes member variables and calls `Initialize`.
*   **Initialize**: Resets the encounter array `m_auiEncounter` and the event map `m_events`. It schedules two recurring events: `EVENT_THADDIUS_SCREAM` (randomly between 2-5 minutes) and `EVENT_SUMMON_FROGGER_WAVE` (every 6 seconds).
*   **IsEncounterInProgress**: Returns true if any encounter in `m_auiEncounter` is in `IN_PROGRESS` or `SPECIAL` state.
*   **SetData**: The primary method for updating instance state. It handles:
    *   Updating `m_auiEncounter` for each boss type.
    *   Triggering door/gate state changes via helper methods (`UpdateManualDoor`, `UpdateBossGate`, `UpdateAutomaticBossEntranceDoor`, `UpdateTeleporters`).
    *   Scheduling specific events (e.g., wing boss death yells, Four Horsemen dialogue, Sapphiron summoning sequence).
    *   Handling special logic for Four Horsemen (counting deaths, respawning on fail, granting reputation on success).
    *   Handling Kel'Thuzad's start condition (checking if players are near the chamber center).
    *   Incrementing wipe counters in `InstanceStatistics` if a boss fails after being in combat for >10 seconds.
    *   Saving instance data to the database if an encounter is `DONE` or Sapphiron is `SPECIAL`.
*   **Load**: Parses the saved instance data string, restoring `m_auiEncounter`. It converts `IN_PROGRESS` states to `NOT_STARTED` and `SPECIAL` Thaddius states to `FAIL` to handle server restarts gracefully.
*   **GetData**: Returns the current state of a specific encounter type from `m_auiEncounter`. Logs an error if an invalid type is requested.
*   **GetData64**: Currently unimplemented, logs a basic message and returns 0.
*   **GetGOUuid**: Retrieves the GUID of a GameObject from `m_mNpcEntryGuidStore` (note: the variable name suggests NPCs, but it stores GO GUIDs keyed by `NaxxGOs` enum values in `OnObjectCreate`). Logs an error if not found.

### Door, Gate, and Teleporter Management

*   **UpdateManualDoor**: Overload accepting `NaxxGOs` retrieves the GameObject and delegates to the `GameObject*` overload.
*   **UpdateManualDoor#2**: Overload accepting `GameObject*`. Sets the `GO_FLAG_LOCKED` flag on the GameObject based on whether the associated encounter data is `DONE`.
*   **UpdateBossGate**: Overload accepting `NaxxGOs` retrieves the GameObject and delegates to the `GameObject*` overload.
*   **UpdateBossGate#2**: Overload accepting `GameObject*`. Sets the GameObject state to `GO_STATE_ACTIVE` if the encounter data is `DONE`, otherwise `GO_STATE_READY`. Logs an error if the GO is null.
*   **UpdateAutomaticBossEntranceDoor**: Overload accepting `NaxxGOs` retrieves the GameObject and delegates to the `GameObject*` overload.
*   **UpdateAutomaticBossEntranceDoor#2**: Overload accepting `GameObject*`. Closes the door (sets `GO_FLAG_NO_INTERACT` and `GO_STATE_READY`) if the encounter is `IN_PROGRESS` or `SPECIAL`, or if a required pre-boss condition isn't met. Otherwise, it opens the door (`GO_STATE_ACTIVE`). Logs an error if the GO is null.
*   **UpdateTeleporters**: Updates the visual state (ramps, eye bosses) and interaction state (portals) for the four wing teleporters based on the status of the corresponding end-boss (Maexxna, Thaddius, Loatheb, Four Horsemen). It also checks `WingsAreCleared()` to enable/disable the central hub portal (`GO_HUB_PORTAL`).
*   **SetTeleporterVisualState**: Sets the GO state to `GO_STATE_ACTIVE` if data is `DONE`, else `GO_STATE_READY`.
*   **SetTeleporterState**: Calls `SetTeleporterVisualState` and additionally removes/sets the `GO_FLAG_NO_INTERACT` flag based on whether the data is `DONE`.
*   **WingsAreCleared**: Checks if all encounters from `TYPE_ANUB_REKHAN` to `TYPE_SAPPHIRON` are `DONE`.

### Boss-Specific Mechanics

#### Gothik
*   **SetGothTriggers**: Iterates through `m_lGothTriggerList` (populated in `OnCreatureCreate` for `NPC_SUB_BOSS_TRIGGER`), determines if each trigger is on the "right side" (using `IsInRightSideGothArea`) and if it's an "anchor" (based on Z position relative to Gothik), and stores this info in `m_mGothTriggerMap`.
*   **GetClosestAnchorForGoth**: Finds the closest anchor creature to a source creature on the specified side (left/right) from `m_mGothTriggerMap`.
*   **GetGothSummonPointCreatures**: Populates a list with non-anchor creatures from `m_mGothTriggerMap` on the specified side.
*   **IsInRightSideGothArea**: Determines if a unit is on the right side of the combat gate (`GO_MILI_GOTH_COMBAT_GATE`) by comparing Y positions. Logs an error if the gate is not found.

#### Kel'Thuzad
*   **SetChamberCenterCoords**: Stores the X, Y, Z coordinates of the chamber center, used for checking player proximity during the encounter start.
*   **ToggleKelThuzadWindows**: Sets the state of the four Kel'Thuzad window GameObjects (`GO_KT_WINDOW_1` to `GO_KT_WINDOW_4`) to `GO_STATE_ACTIVE` or `GO_STATE_READY` based on the `setOpen` parameter.
*   **GetNumEndbossDead**: Counts how many of the four wing bosses (Maexxna, Thaddius, Four Horsemen, Loatheb) are `DONE`. Used for Kel'Thuzad's taunts.

#### Other
*   **HandleEvadeOutOfHome**: Checks if a boss creature has moved too far from its home position or crossed specific coordinate thresholds. If so, it forces the creature to evade. Special logic exists for Faerlina, Razuvious, Heigan, and the Four Horsemen. Logs an error for unsupported creatures.
*   **OnCreatureEnterCombat**: If a `NPC_SewageSlime` enters combat, it finds other slimes within 100 yards and casts an aggro spell on those not already in combat.
*   **OnPlayerDeath**: If Anub'Rekhan is in progress, it spawns 5 scarabs at the dead player's location and attacks a random hostile target.
*   **OnCreatureDeath**: Handles specific trash mob deaths:
    *   Mr. Bigglesworth: Schedules a yell from Kel'Thuzad and increments a custom statistic.
    *   Frenzied Bat, Plagued Bat, Mutated Grub, Plague Beast: Forced despawn after 10 seconds.
    *   Embalming Slime: Forced despawn after 30 seconds.
    *   Lightning Totem: Deleted immediately.
*   **onNaxxramasAreaTrigger**: Handles various area triggers:
    *   `AREATRIGGER_HUB_TO_FROSTWYRM`: Teleports player to Sapphiron's lair if wings are cleared or player is GM.
    *   `AREATRIGGER_KELTHUZAD`: Delegates to `OnKTAreaTrigger` (implemented in `boss_kelthuzad.cpp`).
    *   `AREATRIGGER_FAERLINA`: Triggers Faerlina's greet message if not already done.
    *   `AREATRIGGER_THADDIUS_ENTRANCE`: Triggers Thaddius's greet message if not already done.
    *   `AREATRIGGER_START_DK_WING`: Triggers the Death Knight Wing intro dialogue if not already done and Four Horsemen are not defeated.

### Event Loop

*   **Update**: Processes the `EventMap`. Handles scheduled events like Thaddius's screams, wing boss death yells, Kel'Thuzad/Lich King dialogue sequences, Four Horsemen dialogue, DK Wing intro, Frogger wave summons, and Sapphiron spawning.

### Creature Creation and Respawning

*   **OnCreatureCreate**: Stores GUIDs of boss creatures in `m_mNpcEntryGuidStore`. Adds sub-boss triggers to `m_lGothTriggerList`. Sets wander distance for Sewage Slimes. Implements a hack to limit the number of Bile Sludges to 20. Respawn Four Horsemen if they are dead and the encounter is not `DONE`. Calls `OnCreatureRespawn`.
*   **OnObjectCreate**: Stores GUIDs of important GameObjects in `m_mGoEntryGuidStore`. Categorizes Heigan's traps into `m_alHeiganTrapGuids`. Initializes the state of doors, gates, teleporters, and windows based on the current encounter data. Handles special cases like the Horsemen chest and Sapphiron's spawn point (respawning Sapphiron if the instance was loaded with `SPECIAL` state).
*   **OnCreatureRespawn**: Checks if a boss is `DONE` and force-despawns the creature if so. Also force-despawns specific Gothik-related trash mobs if Gothik is `DONE`.

### Trash Mob AIs

*   **mob_spiritOfNaxxramasAI**:
    *   **ctor**: Casts stealth detection spell.
    *   **DespawnPortal**: Unsummons the portal of shadows if it exists.
    *   **Reset#4**: Resets timers and despawns portal.
    *   **JustDied**: Despawns portal.
    *   **UpdateAI#4**: Summons a portal of shadows after 5 seconds, then casts Shadow Bolt Volley every 10 seconds. Performs melee attacks.
*   **mob_naxxramasGarboyleAI**:
    *   **ctor**: Calls `EnterStoneform`. Casts stealth detection if idle and entry is 16168.
    *   **EnterStoneform**: Applies stoneform visual spell if idle and entry is 16168.
    *   **Reset#2**: Randomizes Acid Volley timer.
    *   **JustReachedHome**: Calls `EnterStoneform`.
    *   **MoveInLineOfSight**: If in stoneform, attacks players within 17 yards who are not feigning death or unattackable. Otherwise, uses default behavior.
    *   **Aggro**: Removes stoneform visual.
    *   **UpdateAI#2**: Casts Stoneskin below 30% health. Casts Acid Volley on a timer (skipping one specific gargoyle). Performs melee attacks.
*   **mob_naxxramasPlagueSlimeAI**:
    *   **ctor**: Calls `Reset`.
    *   **ChangeColor**: Randomly selects a color spell, updates the creature's entry, removes previous color aura, casts the new spell, and sets scale to 2.0.
    *   **Reset#3**: Resets timer and calls `ChangeColor`.
    *   **Aggro#2**: Calls for help.
    *   **UpdateAI#3**: Changes color on a timer. Performs melee attacks.
*   **mob_toxic_tunnelAI**:
    *   **ctor**: Calls `Reset`.
    *   **Reset#5**: Resets timers.
    *   **AttackStart**: Empty override to prevent aggro.
    *   **MoveInLineOfSight#2**: Empty override to prevent aggro.
    *   **EnterCombat**: Starts a 5-second timer to evade.
    *   **UpdateAI#5**: Evades after the timer expires. Recasts poison aura if missing.
*   **mob_dark_touched_warriorAI**:
    *   **ctor**: Calls `Reset`.
    *   **Reset**: Resets `hasFled` flag.
    *   **FleeToHorse**: Finds the nearest Deathcharger Steed and moves towards it, interrupting spells and clearing targets.
    *   **UpdateAI**: Flees to a horse once when health drops below 50%. Performs melee attacks.

### Gossip and Spells

*   **LearnCraftIfCan**: Helper function to teach a crafting recipe if the player meets reputation and skill requirements.
*   **GossipSelect_npc_MasterCraftsmanOmarion**: Handles gossip selections for Omarion. Presents options based on player's crafting skills and Argent Dawn reputation. Teaches recipes or gives items upon selection.
*   **GossipHello_npc_MasterCraftsmanOmarion**: Initializes the gossip menu for Omarion, showing options based on player's crafting skills.
*   **GargoyleStoneformScript::OnBeforeApply**: Sets the target's stand state to `MAX_UNIT_STAND_STATE` and adds `UNIT_FLAG_NOT_SELECTABLE` when the aura is applied. Reverts these changes when removed.
*   **UnrelentingRiderShadowBoltVolleyScript::OnCheckTarget**: Ensures the spell only targets units with the "Shadow Mark" aura (ID 27825).

### Registration

*   **GetInstanceData_instance_naxxramas**: Factory function to create an `instance_naxxramas` object.
*   **AreaTrigger_at_naxxramas**: Wrapper for `onNaxxramasAreaTrigger`, allowing GMs to bypass certain triggers.
*   **GetAI_mob_spiritOfNaxxramas**, **GetAI_mob_naxxramasGargoyle**, **GetAI_mob_plagueSlimeAI**, **GetAI_toxic_tunnel**, **GetAI_dark_touched_warrior**: Factory functions for the respective AI classes.
*   **GetScript_GargoyleStoneform**, **GetScript_UnrelentingRiderShadowBoltVolley**: Factory functions for the aura and spell scripts.
*   **AddSC_instance_naxxramas**: Registers all scripts defined in this unit with the `ScriptMgr`.

## Cross-Unit Boundaries

*   **Calls Out**:
    *   `ScriptedInstance`: Base class functionality for instance management (e.g., `GetSingleCreatureFromStorage`, `GetSingleGameObjectFromStorage`, `DoRespawnGameObject`, `SaveToDB`).
    *   `EventMap`: Manages timed events (`ScheduleEvent`, `ExecuteEvent`, `Update`, `Reset`, `Repeat`).
    *   `GameObject`/`WorldObject.Object`: Manipulates game objects (state, flags, summoning creatures).
    *   `Creature`/`CreatureAI`/`Unit.Main`: Interacts with creatures and units (AI control, movement, combat, spells, health, position).
    *   `GridSearchers`: Finds creatures in the grid.
    *   `Log.Main`: Logging errors and debug information.
    *   `InstanceStatistics`: Tracks wipe counts and custom counters.
    *   `Map.Main`: Accesses map-level information (players, creatures, instance ID).
    *   `ObjectMgr`/`ReputationMgr`: Retrieves faction data and modifies player reputation.
    *   `shared_Util`: Utility functions like `urand`.
    *   `ZoneScript`: Gets the map object.
    *   `Geometry`: Checks if a point is left of a line (used for Four Horsemen evade logic).
    *   `GossipDef`/`PlayerMenu`: Handles gossip menus.
    *   `SpellCaster`/`SpellMgr`: Casting spells and retrieving spell entries.
    *   `Aura`/`SpellScript`/`AuraScript`: Custom spell and aura behavior.
    *   `Script`/`ScriptMgr`: Script registration.
*   **Called By**:
    *   Various boss scripts (`boss_anubrekhan`, `boss_faerlina`, etc.) call `SetData`, `GetData`, `GetData64`, `GetGOUuid`, `HandleEvadeOutOfHome`, `UpdateAutomaticBossEntranceDoor`, `UpdateManualDoor`, `UpdateBossGate`, `UpdateTeleporters`, `SetGothTriggers`, `GetClosestAnchorForGoth`, `GetGothSummonPointCreatures`, `IsInRightSideGothArea`, `SetChamberCenterCoords`, `ToggleKelThuzadWindows`, `OnKTAreaTrigger`, `WingsAreCleared`, `GetNumEndbossDead`.
    *   `ScriptLoader` calls `AddSC_instance_naxxramas`.

## Data Model

This unit does not directly interact with any database tables. Instance state is managed in-memory via the `m_auiEncounter` array and other member variables, and persisted through the core's `ScriptedInstance` mechanism using the `Save()` and `Load()` methods, which serialize/deserialize data to/from a string stored in the core's instance data system.

## Notable Implementation Details

*   **Four Horsemen Logic**: The `SetData` method for `TYPE_FOUR_HORSEMEN` tracks individual deaths with `m_horsemenDeathCounter`. The encounter is marked `DONE` only after all four are dead. If the encounter fails (`FAIL`), the counter resets, and dead horsemen are respawned. Upon completion, reputation is granted, and specific trash mobs are deleted.
*   **Heigan Trap Management**: `OnObjectCreate` categorizes numerous trap GameObjects into `m_alHeiganTrapGuids` based on their entry IDs and DB table GUIDs, likely for use by the Heigan boss script.
*   **Sapphiron Summoning**: The `SetData` method for `TYPE_SAPPHIRON` with `SPECIAL` data schedules the `EVENT_SPAWN_SAPPHIRON` event. The `Update` method handles the actual summoning. `OnObjectCreate` also handles respawning Sapphiron if the instance was loaded with `SPECIAL` state, indicating a server restart during the summoning sequence.
*   **Kel'Thuzad Start Condition**: `SetData` for `TYPE_KELTHUZAD` with `SPECIAL` data checks if all players are within 15 yards of the chamber center coordinates (set by `SetChamberCenterCoords`) before starting the encounter.
*   **Gothik Side Determination**: `IsInRightSideGothArea` uses the Y position of the combat gate to determine sides, which might be fragile if the gate's position changes.
*   **Bile Sludge Limit**: A hack in `OnCreatureCreate` limits the number of Bile Sludges to 20 to prevent excessive spawning.
*   **Faerlina Door Hack**: A comment in `OnObjectCreate` and `SetData` indicates a workaround for Faerlina's door not locking properly, using `GO_FLAG_NO_INTERACT` instead of relying solely on `GO_FLAG_LOCKED`.
*   **Thaddius Screams**: `Initialize` schedules `EVENT_THADDIUS_SCREAM`, and `Update` handles the periodic screaming if Thaddius is not dead.
*   **Frogger Waves**: `Initialize` schedules `EVENT_SUMMON_FROGGER_WAVE`, and `Update` summons Living Poison creatures at predefined positions every 6 seconds.
*   **Mr. Bigglesworth**: `OnCreatureDeath` handles his death, triggering a yell from Kel'Thuzad and incrementing a custom statistic.
*   **Trash Mob AIs**: Several trash mobs have specific behaviors implemented in this unit, such as Spirit of Naxxramas summoning portals, Gargoyles entering stoneform, Plague Slimes changing color, Toxic Tunnels evading after entering combat, and Dark Touched Warriors fleeing to horses.
*   **Omarion Gossip**: Complex gossip logic for Master Craftsman Omarion, teaching recipes based on profession, skill level, and Argent Dawn reputation.
*   **Custom Scripts**: Includes custom aura script for Gargoyle Stoneform and spell script for Unrelenting Rider's Shadow Bolt Volley.

## Member Reference

**instance_naxxramas**: Constructor initializes member variables and calls `Initialize`.

**Initialize**: Resets the encounter array `m_auiEncounter` and the event map `m_events`. It schedules two recurring events: `EVENT_THADDIUS_SCREAM` (randomly between 2-5 minutes) and `EVENT_SUMMON_FROGGER_WAVE` (every 6 seconds).

**SetTeleporterVisualState**: Sets the GO state to `GO_STATE_ACTIVE` if data is `DONE`, else `GO_STATE_READY`.

**SetTeleporterState**: Calls `SetTeleporterVisualState` and additionally removes/sets the `GO_FLAG_NO_INTERACT` flag based on whether the data is `DONE`.

**GetNumEndbossDead**: Counts how many of the four wing bosses (Maexxna, Thaddius, Four Horsemen, Loatheb) are `DONE`. Used for Kel'Thuzad's taunts.

**HandleEvadeOutOfHome**: Checks if a boss creature has moved too far from its home position or crossed specific coordinate thresholds. If so, it forces the creature to evade. Special logic exists for Faerlina, Razuvious, Heigan, and the Four Horsemen. Logs an error for unsupported creatures.

**OnCreatureEnterCombat**: If a `NPC_SewageSlime` enters combat, it finds other slimes within 100 yards and casts an aggro spell on those not already in combat.

**WingsAreCleared**: Checks if all encounters from `TYPE_ANUB_REKHAN` to `TYPE_SAPPHIRON` are `DONE`.

**UpdateAutomaticBossEntranceDoor**: Overload accepting `NaxxGOs` retrieves the GameObject and delegates to the `GameObject*` overload.

**UpdateAutomaticBossEntranceDoor#2**: Overload accepting `GameObject*`. Closes the door (sets `GO_FLAG_NO_INTERACT` and `GO_STATE_READY`) if the encounter is `IN_PROGRESS` or `SPECIAL`, or if a required pre-boss condition isn't met. Otherwise, it opens the door (`GO_STATE_ACTIVE`). Logs an error if the GO is null.

**UpdateManualDoor**: Overload accepting `NaxxGOs` retrieves the GameObject and delegates to the `GameObject*` overload.

**UpdateManualDoor#2**: Overload accepting `GameObject*`. Sets the `GO_FLAG_LOCKED` flag on the GameObject based on whether the associated encounter data is `DONE`.

**UpdateBossGate**: Overload accepting `NaxxGOs` retrieves the GameObject and delegates to the `GameObject*` overload.

**UpdateBossGate#2**: Overload accepting `GameObject*`. Sets the GameObject state to `GO_STATE_ACTIVE` if the encounter data is `DONE`, otherwise `GO_STATE_READY`. Logs an error if the GO is null.

**UpdateTeleporters**: Updates the visual state (ramps, eye bosses) and interaction state (portals) for the four wing teleporters based on the status of the corresponding end-boss (Maexxna, Thaddius, Loatheb, Four Horsemen). It also checks `WingsAreCleared()` to enable/disable the central hub portal (`GO_HUB_PORTAL`).

**OnCreatureCreate**: Stores GUIDs of boss creatures in `m_mNpcEntryGuidStore`. Adds sub-boss triggers to `m_lGothTriggerList`. Sets wander distance for Sewage Slimes. Implements a hack to limit the number of Bile Sludges to 20. Respawn Four Horsemen if they are dead and the encounter is not `DONE`. Calls `OnCreatureRespawn`.

**OnObjectCreate**: Stores GUIDs of important GameObjects in `m_mGoEntryGuidStore`. Categorizes Heigan's traps into `m_alHeiganTrapGuids`. Initializes the state of doors, gates, teleporters, and windows based on the current encounter data. Handles special cases like the Horsemen chest and Sapphiron's spawn point (respawning Sapphiron if the instance was loaded with `SPECIAL` state).

**OnCreatureRespawn**: Checks if a boss is `DONE` and force-despawns the creature if so. Also force-despawns specific Gothik-related trash mobs if Gothik is `DONE`.

**IsEncounterInProgress**: Returns true if any encounter in `m_auiEncounter` is in `IN_PROGRESS` or `SPECIAL` state.

**SetData**: The primary method for updating instance state. It handles updating `m_auiEncounter`, triggering door/gate state changes, scheduling specific events, handling special logic for Four Horsemen and Kel'Thuzad, incrementing wipe counters, and saving instance data.

**Load**: Parses the saved instance data string, restoring `m_auiEncounter`. It converts `IN_PROGRESS` states to `NOT_STARTED` and `SPECIAL` Thaddius states to `FAIL` to handle server restarts gracefully.

**GetData**: Returns the current state of a specific encounter type from `m_auiEncounter`. Logs an error if an invalid type is requested.

**GetData64**: Currently unimplemented, logs a basic message and returns 0.

**GetGOUuid**: Retrieves the GUID of a GameObject from `m_mNpcEntryGuidStore`. Logs an error if not found.

**SetGothTriggers**: Iterates through `m_lGothTriggerList`, determines if each trigger is on the "right side" and if it's an "anchor", and stores this info in `m_mGothTriggerMap`.

**GetClosestAnchorForGoth**: Finds the closest anchor creature to a source creature on the specified side (left/right) from `m_mGothTriggerMap`.

**GetGothSummonPointCreatures**: Populates a list with non-anchor creatures from `m_mGothTriggerMap` on the specified side.

**IsInRightSideGothArea**: Determines if a unit is on the right side of the combat gate (`GO_MILI_GOTH_COMBAT_GATE`) by comparing Y positions. Logs an error if the gate is not found.

**SetChamberCenterCoords**: Stores the X, Y, Z coordinates of the chamber center, used for checking player proximity during the encounter start.

**ToggleKelThuzadWindows**: Sets the state of the four Kel'Thuzad window GameObjects (`GO_K

---

<!-- machine-true, projected from graph.json -->

## Map — instance_naxxramas.Main

*Source:* instance_naxxramas.cpp, naxxramas.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| instance_naxxramas | ctor | ScriptedInstance/ScriptedInstance | — | — |
| Initialize | method | EventMap/Reset, EventMap/ScheduleEvent#2, EventMap/ScheduleEvent#3, shared_Util/urand | — | — |
| SetTeleporterVisualState | method | GameObject/SetGoState | — | — |
| SetTeleporterState | method | WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag | — | — |
| GetNumEndbossDead | method | — | — | — |
| HandleEvadeOutOfHome | method | Creature.Main/AI, Creature.Main/GetHomePosition#2, Creature.Main/IsInEvadeMode, CreatureAI/EnterEvadeMode, Log.Main/Out, Object/GetEntry, ScriptedInstance/GetSingleCreatureFromStorage, Unit.Main/IsAlive, WorldObject.Object/GetDistance2d, WorldObject.Object/GetPosition#3, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | boss_anubrekhan/UpdateAI, boss_faerlina/UpdateAI, boss_four_horsemen/UpdateAI#2, boss_four_horsemen/UpdateAI#3, boss_four_horsemen/UpdateAI#4, boss_four_horsemen/UpdateAI#5, boss_gothik/UpdateAI, boss_grobbulus/UpdateAI, boss_heigan/UpdateAI, boss_loatheb/UpdateAI, boss_noth/UpdateAI, boss_razuvious/UpdateAI, instance_naxxramas.boss_kelthuzad/UpdateAI | — |
| OnCreatureEnterCombat | method | GridSearchers/GetCreatureListWithEntryInGrid#2, Object/GetEntry, SpellCaster/CastSpell#2, Unit.Main/IsInCombat | — | — |
| WingsAreCleared | method | — | — | — |
| UpdateAutomaticBossEntranceDoor | method | ScriptedInstance/GetSingleGameObjectFromStorage | boss_heigan/JustDied, boss_heigan/JustReachedHome, boss_heigan/UpdateAI | — |
| UpdateAutomaticBossEntranceDoor#2 | method | GameObject/SetGoState, Log.Main/Out, WorldObject.Object/SetFlag | — | — |
| UpdateManualDoor | method | ScriptedInstance/GetSingleGameObjectFromStorage | — | — |
| UpdateManualDoor#2 | method | WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag | — | — |
| UpdateBossGate | method | ScriptedInstance/GetSingleGameObjectFromStorage | — | — |
| UpdateBossGate#2 | method | GameObject/SetGoState, Log.Main/Out | — | — |
| UpdateTeleporters | method | GameObject/SetGoState, Log.Main/Out, ScriptedInstance/GetSingleGameObjectFromStorage | — | — |
| OnCreatureCreate | method | Creature.Main/ForcedDespawn, Creature.Main/Respawn, Creature.Main/SetWanderDistance, GridSearchers/GetCreatureListWithEntryInGrid#2, Object/GetEntry, Object/GetGUID, Object/GetObjectGuid, Unit.Main/IsDead | — | — |
| OnObjectCreate | method | GameObject/GetDBTableGUIDLow, GameObject/GetGoType, GameObject/SetGoState, Object/GetEntry, Object/GetGUID, Object/GetObjectGuid, WorldObject.Object/DeleteLater, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag, WorldObject.Object/SummonCreature#2 | — | — |
| OnCreatureRespawn | method | Creature.Main/GetDBTableGUIDLow, Object/GetEntry, WorldObject.Object/AddObjectToRemoveList | — | — |
| IsEncounterInProgress | method | — | — | — |
| SetData | method | Creature.Main/GetCombatTime, Creature.Main/Respawn, Errors/PrintStacktraceAndThrow, EventMap/ScheduleEvent#2, EventMap/ScheduleEvent#3, GameObject/SetGoState, GridSearchers/GetCreatureListWithEntryInGrid, InstanceData/SaveToDB, InstanceStatistics/IncrementWipeCounter, LinkedListHead/isEmpty, Log.Main/Out, Map.Main/GetId, Map.Main/GetInstanceId, Map.Main/GetMapName, Map.Main/GetPlayers, ObjectMgr/GetFactionEntry, Player.Main/GetReputationMgr, ReputationMgr/ModifyReputation, ScriptedInstance/DoRespawnGameObject, ScriptedInstance/GetSingleCreatureFromStorage, ScriptedInstance/GetSingleGameObjectFromStorage, Unit.Main/IsDead, WorldObject.Object/DeleteLater, WorldObject.Object/IsWithinDist2d, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag, ZoneScript/GetMap#2 | boss_anubrekhan/Aggro, boss_anubrekhan/JustDied, boss_anubrekhan/JustReachedHome, boss_faerlina/Aggro, boss_faerlina/JustDied, boss_faerlina/JustReachedHome, boss_four_horsemen/Aggro, boss_four_horsemen/JustDied, boss_four_horsemen/JustReachedHome, boss_gluth/Aggro, boss_gluth/JustDied, boss_gluth/JustReachedHome, boss_gothik/Aggro, boss_gothik/JustDied, boss_gothik/JustReachedHome, boss_grobbulus/Aggro, boss_grobbulus/JustDied, boss_grobbulus/JustReachedHome, boss_heigan/Aggro, boss_heigan/JustDied, boss_heigan/JustReachedHome, boss_loatheb/Aggro, boss_loatheb/JustDied, boss_loatheb/JustReachedHome, boss_maexxna/Aggro, boss_maexxna/JustDied, boss_maexxna/JustReachedHome, boss_noth/Aggro, boss_noth/JustDied, boss_noth/JustReachedHome, boss_patchwerk/Aggro, boss_patchwerk/JustDied, boss_patchwerk/JustReachedHome, boss_razuvious/Aggro, boss_razuvious/JustDied, boss_razuvious/JustReachedHome, boss_sapphiron/Aggro, boss_sapphiron/JustDied, boss_sapphiron/OnUse, boss_sapphiron/Reset, boss_thaddius/Aggro#4, boss_thaddius/DamageTaken, boss_thaddius/JustDied, boss_thaddius/JustDied#2, boss_thaddius/JustDied#3, boss_thaddius/JustReachedHome, boss_thaddius/JustReachedHome#2, instance_naxxramas.boss_kelthuzad/JustDied, instance_naxxramas.boss_kelthuzad/JustReachedHome, instance_naxxramas.boss_kelthuzad/StartEncounter | — |
| Load | method | Log.Main/Out, Map.Main/GetId, Map.Main/GetInstanceId, Map.Main/GetMapName | — | — |
| GetData | method | Log.Main/Out | boss_anubrekhan/CheckSpawnInitialCryptGuards, boss_four_horsemen/Aggro, boss_four_horsemen/AggroRadius, boss_razuvious/RespawnAdds, boss_sapphiron/OnUse, boss_sapphiron/Reset, boss_thaddius/CheckSpawnAdds, boss_thaddius/JustDied, boss_thaddius/JustDied#2, boss_thaddius/UpdateAI, instance_naxxramas.boss_kelthuzad/OnKTAreaTrigger, instance_naxxramas.boss_kelthuzad/UpdateAI, instance_naxxramas.boss_kelthuzad/UpdateP1 | — |
| GetData64 | method | Log.Main/Out | — | — |
| GetGOUuid | method | Log.Main/Out | — | — |
| SetGothTriggers | method | Map.Main/GetCreature, Object/GetGUID, ObjectGuid/ObjectGuid#5, ScriptedInstance/GetSingleCreatureFromStorage, WorldObject.Object/GetPositionZ | boss_gothik/Aggro | — |
| GetClosestAnchorForGoth | method | Map.Main/GetCreature, ObjectDistanceOrder/ObjectDistanceOrder, ObjectGuid/ObjectGuid#5 | boss_gothik/EffectDummyCreature_spell_anchor, boss_gothik/SummonedCreatureJustDied | — |
| GetGothSummonPointCreatures | method | Map.Main/GetCreature, ObjectGuid/ObjectGuid#5 | boss_gothik/EffectDummyCreature_spell_anchor, boss_gothik/SummonAdds | — |
| IsInRightSideGothArea | method | Log.Main/Out, ScriptedInstance/GetSingleGameObjectFromStorage, WorldObject.Object/GetPositionY | boss_gothik/SummonAdd, boss_gothik/UpdateAI | — |
| SetChamberCenterCoords | method | — | — | — |
| ToggleKelThuzadWindows | method | GameObject/SetGoState, ScriptedInstance/GetSingleGameObjectFromStorage | instance_naxxramas.boss_kelthuzad/JustReachedHome, instance_naxxramas.boss_kelthuzad/StartEncounter, instance_naxxramas.boss_kelthuzad/UpdateP2P3 | — |
| OnPlayerDeath | method | Creature.Main/AI, Creature.Main/SelectAttackingTarget, Creature.Main/SetInCombatWithZone, CreatureAI/AttackStart, ScriptedInstance/GetSingleCreatureFromStorage, Unit.Main/AddThreat, Unit.Main/SendSpellGo, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SummonCreature#2 | — | — |
| OnCreatureDeath | method | Creature.Main/ForcedDespawn, EventMap/ScheduleEvent#3, InstanceStatistics/IncrementCustomCounter, Object/GetEntry, WorldObject.Object/DeleteLater | — | — |
| Update | method | Creature.MotionMaster/MovePoint, EventMap/ExecuteEvent, EventMap/Repeat, EventMap/ScheduleEvent#2, EventMap/ScheduleEvent#3, EventMap/Update, ScriptedInstance/DoOrSimulateScriptTextForThisInstance, ScriptedInstance/GetPlayerInMap, ScriptedInstance/GetSingleCreatureFromStorage, ScriptMgr/DoOrSimulateScriptTextForMap, shared_Util/urand, Unit.Main/GetMotionMaster, WorldObject.Object/SummonCreature, WorldObject.Object/SummonCreature#2, ZoneScript/GetMap#2 | — | — |
| GetInstanceData_instance_naxxramas | function | — | — | — |
| onNaxxramasAreaTrigger | method | EventMap/ScheduleEvent#3, instance_naxxramas.boss_kelthuzad/OnKTAreaTrigger, Player.Main/IsGameMaster, ScriptedInstance/GetSingleCreatureFromStorage, ScriptMgr/DoOrSimulateScriptTextForMap, ScriptMgr/DoScriptText, Unit.Main/IsAlive, ZoneScript/GetMap#2 | — | — |
| AreaTrigger_at_naxxramas | function | Player.Main/IsGameMaster, Unit.Main/IsAlive, WorldObject.Object/GetInstanceData | — | — |
| mob_spiritOfNaxxramasAI | ctor | ScriptedAI/ScriptedAI, SpellCaster/CastSpell#2 | — | — |
| DespawnPortal | method | Map.Main/GetCreature, ObjectGuid/ObjectGuid#5, ObjectGuid/operator!, TemporarySummon/UnSummon, WorldObject.Object/GetMap | — | — |
| Reset#4 | method | — | — | — |
| JustDied | method | — | — | — |
| UpdateAI#4 | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Object/GetObjectGuid, SpellCaster/CastSpell#2, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, Unit.Main/SendSpellGo, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SummonCreature#2 | — | — |
| mob_naxxramasGarboyleAI | ctor | Creature.Main/GetDefaultMovementType, Object/GetEntry, ScriptedAI/ScriptedAI, SpellCaster/CastSpell#2 | — | — |
| EnterStoneform | method | Creature.Main/GetDefaultMovementType, Object/GetEntry, SpellCaster/CastSpell#2 | — | — |
| Reset#2 | method | shared_Util/urand | — | — |
| JustReachedHome | method | — | — | — |
| MoveInLineOfSight | method | BasicAI/MoveInLineOfSight, CreatureAI/AttackStart, Object/GetTypeId, Unit.Main/HasAura#2, Unit.Main/HasAuraType, Unit.Main/IsInCombat, WorldObject.Object/IsWithinDistInMap, WorldObject.Object/IsWithinLOSInMap | — | — |
| Aggro | method | Unit.Main/HasAura#2, Unit.Main/RemoveAurasDueToSpellByCancel | — | — |
| UpdateAI#2 | method | Creature.Main/GetDBTableGUIDLow, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, ScriptMgr/DoScriptText, SpellCaster/CastSpell#2, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/GetHealthPercent, Unit.Main/GetVictim, Unit.Main/HasAura#2, Unit.Main/SelectHostileTarget | — | — |
| mob_naxxramasPlagueSlimeAI | ctor | ScriptedAI/ScriptedAI | — | — |
| ChangeColor | method | Creature.Main/UpdateEntry, CreatureAI/DoCastSpellIfCan, shared_Util/urand, SpellMgr/GetSpellEntry, SpellMgr/Instance, Unit.Main/RemoveAurasDueToSpell, WorldObject.Object/SetObjectScale | — | — |
| Reset#3 | method | — | — | — |
| Aggro#2 | method | Creature.Main/CallForHelp | — | — |
| UpdateAI#3 | method | CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| mob_toxic_tunnelAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#5 | method | — | — | — |
| AttackStart | method | — | — | — |
| MoveInLineOfSight#2 | method | — | — | — |
| EnterCombat | method | — | — | — |
| UpdateAI#5 | method | ScriptedAI/EnterEvadeMode, SpellCaster/CastSpell#2, Unit.Main/HasAura#2 | — | — |
| mob_dark_touched_warriorAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | method | — | — | — |
| FleeToHorse | method | Creature.MotionMaster/MoveSeekAssistance, NearestCreatureEntryWithLiveStateInObjectRangeCheck/NearestCreatureEntryWithLiveStateInObjectRangeCheck, ObjectGuid/ObjectGuid, SpellCaster/InterruptSpellsWithInterruptFlags, Unit.Main/GetMotionMaster, Unit.Main/GetVictim, Unit.Main/HasAuraType, Unit.Main/SetTargetGuid, Unit.Main/UpdateSpeed, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | — | — |
| UpdateAI | method | CreatureAI/DoMeleeAttackIfReady, Unit.Main/GetHealthPercent, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_mob_spiritOfNaxxramas | function | — | — | — |
| GetAI_mob_naxxramasGargoyle | function | — | — | — |
| GetAI_mob_plagueSlimeAI | function | — | — | — |
| GetAI_toxic_tunnel | function | — | — | — |
| GetAI_dark_touched_warrior | function | — | — | — |
| LearnCraftIfCan | function | Player.Main/GetReputationRank, Player.Main/HasSpell, SpellCaster/CastSpell#2 | — | — |
| GossipSelect_npc_MasterCraftsmanOmarion | function | GossipDef/AddMenuItem#4, GossipDef/AddMenuItem#5, GossipDef/CloseGossip, GossipDef/SendGossipMenu, Object/GetGUID, ObjectGuid/ObjectGuid#5, Player.Main/AddItem, Player.Main/GetReputationRank, Player.Main/GetSkillValue, Player.Main/HasItemCount, PlayerMenu/GetGossipMenu | — | — |
| GossipHello_npc_MasterCraftsmanOmarion | function | GossipDef/AddMenuItem#5, GossipDef/SendGossipMenu, Object/GetGUID, ObjectGuid/ObjectGuid#5, Player.Main/GetSkillValue, PlayerMenu/GetGossipMenu, Unit.Main/HandleEmote | — | — |
| OnBeforeApply | method | Aura/GetTarget, Unit.Main/SetStandState, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag | — | — |
| GetScript_GargoyleStoneform | function | — | — | — |
| OnCheckTarget | method | Unit.Main/HasAura#2 | — | — |
| GetScript_UnrelentingRiderShadowBoltVolley | function | — | — | — |
| AddSC_instance_naxxramas | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |

---

<!-- verify: boundary-bleed | foreign: save -->
