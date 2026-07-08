# instance_dire_maul

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# instance_dire_maul

## Purpose & Responsibilities

`instance_dire_maul` is the script module implementing the logic for the **Dire Maul** dungeon instance in World of Warcraft. It manages the state, events, and AI behaviors for all three wings of the instance (East, West, and North), including bosses, trash mobs, quest NPCs, and interactive game objects.

Key responsibilities include:
1.  **Instance State Management:** Tracking boss kills, quest progress (e.g., Gordok Tribute, Broken Trap), and event phases (e.g., Immol'Thar's crystal ritual) via `SetData`/`GetData`.
2.  **Boss AI Implementation:** Providing specific combat behaviors for bosses such as Alzzin the Wildshaper, Prince Tortheldrin, Magister Kalendris, Ferra, Kromcrush, and the Gordok Guards.
3.  **Event Coordination:** Handling complex multi-step events, such as the Immol'Thar crystal sorting mechanism, the Gordok Tribute guard deaths, and the Knot Thimblejack escape sequence.
4.  **Quest Integration:** Managing gossip menus, quest rewards, and NPC interactions for quests like "A Broken Trap," "Free Knot," and "Gordok Ogre Suit."

## Member-by-Member Behavior

### Instance Script (`instance_dire_maul`)

#### Initialization and Lifecycle
*   **`instance_dire_maul`**: Constructor initializes all GUIDs to 0, sets default states (e.g., `m_uiGuardAliveCount` to 6), and calls `Initialize()` to zero out encounter arrays and clear GUID lists.
*   **`Initialize`**: Resets the `m_auiEncounter` array and `m_auiCristalsGUID` array to 0, and clears the `m_lFelvineShardGUIDs` list. This ensures a clean state for a new instance load.
*   **`~instance_dire_maul`**: Destructor. No custom cleanup logic is implemented.
*   **`Save`**: Returns the string representation of the instance data (`strInstData`) for persistence.
*   **`Load`**: Parses the saved string data into the `m_auiEncounter` array. Crucially, it resets any `IN_PROGRESS` states to `NOT_STARTED` to prevent stuck events after a server restart.

#### Player Interaction
*   **`OnPlayerEnter`**:
    *   Checks for the `ITEM_GORDOK_INNER_DOOR_KEY`. If the player has it but the Moldar boss (`TYPE_MOLDAR`) is not done, it destroys the item to prevent an exploit.
    *   If the `TYPE_BROKEN_TRAP` event is marked as `DONE`, it respawns the fixed trap game object at the broken trap's location and deletes the broken trap object, ensuring the quest state persists correctly across reloads.
*   **`OnPlayerLeave`**: Removes the `SPELL_KING_OF_GORDOK` aura from the player to prevent retaining buffs outside the instance.

#### Object and Creature Tracking
*   **`OnObjectCreate`**: Registers GUIDs for critical game objects (doors, crystals, tribute items, traps) when they spawn. It also checks the current instance state to immediately activate doors (e.g., `GO_CRUMBLE_WALL`) if the corresponding boss (`TYPE_ALZZIN`) is already defeated.
*   **`OnCreatureCreate`**: Registers GUIDs for bosses and adds them to specific lists (e.g., `m_lCristalsEventtMobGUIDList` for crystal event mobs). It applies initial flags, such as making Immol'Thar unselectable if the crystal event isn't complete, or setting Cho'Rush's faction and stand state if the Gordok Tribute is complete.
*   **`OnCreatureDeath`**: Triggers specific events upon boss death:
    *   **Alzzin**: Sets `TYPE_ALZZIN` to `DONE`.
    *   **Immol'Thar**: Makes Tortheldrin yell a specific line.
    *   **Guard Moldar**: Sets `TYPE_MOLDAR` to `DONE` and triggers the Gordok Tribute special state if not already done.
    *   **Other Guards/Kromcrush/Cho'Rush**: Triggers the Gordok Tribute special state.
    *   **King Gordok**: Summons `NPC_MIZZLE_THE_CRAFTY` and makes Cho'Rush friendly and speak after a delay.

#### Data Management
*   **`SetData`**: The central hub for updating instance state.
    *   **`TYPE_CRISTAL_EVENT`**: If `DONE`, it disables force fields, enables Immol'Thar, and commands nearby guardians to attack him.
    *   **`TYPE_IMMOL_THAR`**: If `DONE`, it removes immunity from Tortheldrin and sets his faction to hostile.
    *   **`TYPE_GORDOK_TRIBUTE`**: Handles the complex logic of tracking guard deaths. When a guard dies, it decrements `m_uiGuardAliveCount`. When the tribute is `DONE`, it respawns a specific Gordok Tribute game object based on how many guards survived.
    *   **`TYPE_ALZZIN`**: Opens doors and respawns Felvine Shards.
    *   **Persistence**: If any data is set to `DONE`, it serializes the encounter array into `strInstData` and calls `SaveToDB()`.
*   **`SetData64`**: Handles 64-bit data updates.
    *   **`TYPE_CRISTAL_EVENT`**: Used during the crystal sorting phase. It removes a mob GUID from the sorted list for a specific crystal. If a crystal's mob list becomes empty, it activates that crystal's door. If all crystals are empty, it sets the event to `DONE`.
    *   **`DATA_DREADSTEED_RITUAL_PLAYER`**: Stores the GUID of the player involved in the dreadsteed ritual.
*   **`GetData`**: Returns the state of a specific encounter type or the `m_bIsTanninLooted` boolean.
*   **`GetData64`**: Returns stored GUIDs for bosses, game objects, or the ritual player.
*   **`GetChoRushEquipment`**: Returns a random equipment ID (1-3) for Cho'Rush if not already set, storing it in the instance data.
*   **`DoSortCristalsEventMobs`**: Iterates through all crystal event mobs and assigns them to the nearest crystal's sorted list based on distance (< 20.0f). This prepares the data for the `SetData64` logic.

### Trash Mob AIs

#### `npc_reste_manaAI`
*   **`npc_reste_manaAI`**: Constructor stores the instance pointer and calls `Reset()`.
*   **`Reset#13`**: Sets timers for Blink and Chain Lightning. Applies arcane immunity.
*   **`JustDied#5`**: Calls `SetData64` on the instance to report its death for the crystal event.
*   **`UpdateAI#11`**: Standard combat loop casting Blink and Chain Lightning on timers, with melee attacks.
*   **`GetAI_npc_reste_mana`**: Factory function returning a new `npc_reste_manaAI`.

#### `npc_arcane_aberrationAI`
*   **`npc_arcane_aberrationAI`**: Constructor stores instance pointer and calls `Reset()`.
*   **`Reset#9`**: Sets Arcane Bolt timer. Applies arcane immunity.
*   **`JustDied#3`**: Reports death to instance for crystal event.
*   **`DamageTaken`**: If health drops below 5%, casts Manaburn once.
*   **`UpdateAI#9`**: Casts Arcane Bolt on timer, melee attacks.
*   **`GetAI_npc_arcane_aberration`**: Factory function returning a new `npc_arcane_aberrationAI`.

#### `npc_residual_montruosityAI`
*   **`npc_residual_montruosityAI`**: Constructor stores instance pointer and calls `Reset()`.
*   **`Reset#12`**: Sets timers for Arcane Blast and Arcane Bolt. Applies arcane immunity.
*   **`JustDied#4`**: Casts `SPELL_SUMMON_MANABURSTS`.
*   **`UpdateFormationSpeed`**: Adjusts walking speed based on proximity to other monstrosities to maintain formation spacing.
*   **`UpdateAI#10`**: If not in combat, updates formation speed. In combat, casts Arcane Bolt and Arcane Blast on timers, melee attacks.
*   **`GetAI_npc_residual_montruosity`**: Factory function returning a new `npc_residual_montruosityAI`.

### Quest and NPC AIs

#### `go_broken_trap`
*   **`QuestRewarded_go_broken_trap`**: Triggered when the quest "A Broken Trap" is turned in. It marks the instance data as `DONE`, flags the game object as non-interactable, summons the fixed trap, and deletes the broken trap.

#### `npc_mizzle_the_craftyAI`
*   **`npc_mizzle_the_craftyAI`**: Constructor sets home position, moves to home, and plays an initial sound.
*   **`Reset#11`**: Empty.
*   **`JustReachedHome`**: Plays a second sound and enables gossip interaction.
*   **`SpellHitTarget#2`**: Forces orientation to a specific angle.
*   **`GetAI_npc_mizzle_the_crafty`**: Factory function returning a new `npc_mizzle_the_craftyAI`.

#### `npc_knot_thimblejackAI`
*   **`npc_knot_thimblejackAI`**: Constructor stores instance pointer.
*   **`Reset#10`**: Empty.
*   **`MovementInform#3`**: Handles a multi-point path sequence. Upon reaching point 13, it forces a despawn after 5 seconds.
*   **`GossipHello_npc_knot_thimblejack`**: Displays gossip options based on quest status and skill levels (Leatherworking/Tailoring).
*   **`GossipSelect_npc_knot_thimblejack`**: Handles gossip selections, teaching spells or showing further menu options.
*   **`QuestRewarded_npc_knot_thimblejack`**: When "Free Knot" is rewarded, it deletes the ball and chain game object, removes gossip/quest giver flags, and starts the NPC's escape path.
*   **`GetAI_npc_knot_thimblejack`**: Factory function returning a new `npc_knot_thimblejackAI`.

#### `GordokBruteAI`
*   **`GordokBruteAI`**: Constructor saves current equipment ID.
*   **`Reset`**: Loads equipment and sets timers for Bruising Blow, Pummel, Uppercut, and Backhand.
*   **`Aggro`**: Plays a random yell.
*   **`UpdateAI`**:
    *   Above 30% health: Casts Bruising Blow and Pummel (if victim is casting).
    *   Below 30% health: Enrages, drops weapon, and switches to Backhand attacks.
    *   Always casts Uppercut on timer.
*   **`GetAI_mob_gordok_brute`**: Factory function returning a new `GordokBruteAI`.

#### `boss_guardsAI`
*   **`boss_guardsAI`**: Constructor stores instance pointer.
*   **`Reset#4`**: Sets timers for Shield Charge, Strike, Knock Away, Shield Bash.
*   **`JustDied`**: If the Gordok Tribute is done, it prevents loot and plays a betrayal line.
*   **`SpellHitTarget`**: Reduces threat by 50% if hit by Knock Away.
*   **`UpdateAI#4`**:
    *   Includes a workaround timer (`m_uiCombatBugTimer`) to force Slip'kik out of combat if he gets stuck with Ice Lock.
    *   Casts Shield Charge (targeting farthest player), Shield Bash (if victim casting), Strike, and Knock Away on timers.
    *   Enrages at 50% health, calling for help.
*   **`GetAI_boss_guards`**: Factory function returning a new `boss_guardsAI`.

#### `go_fixed_trap`
*   **`go_fixed_trap`**: Constructor stores instance pointer.
*   **`UpdateAI#8`**: Checks if Slip'kik is within 2.0f. If so, it stops his combat, deletes his threat list, makes him immune, casts Ice Lock, plays an animation, and deletes itself.
*   **`GetAI_go_fixed_trap`**: Factory function returning a new `go_fixed_trap`.

#### `boss_kromcrushAI`
*   **`boss_kromcrushAI`**: Constructor stores instance pointer.
*   **`Reset#5`**: Sets timers for Mortal Cleave, Intimidating Shout.
*   **`goToFengus`**: Moves Kromcrush to Fengus, enrages him, and makes him unselectable.
*   **`Aggro#2`**: Plays aggro sound.
*   **`MovementInform#2`**: Handles pathfinding points. At point 5, he relocates, becomes selectable, and plays a sound.
*   **`JustDied#2`**: Prevents loot if Gordok Tribute is done.
*   **`EnterEvadeMode`**: If Ogre Suit quest is done, continues pathfinding.
*   **`JustSummoned`**: Makes summoned reavers attack a random target.
*   **`CallReavers`**: Summons Gordok Reavers at specific coordinates based on Kromcrush's position.
*   **`UpdateAI#5`**: Casts Mortal Cleave, Intimidating Shout, Retaliation (at 25%), and calls Reavers (at 50%).
*   **`GossipHello_boss_kromcrush` / `GossipSelect_boss_kromcrush`**: Handles gossip interactions, allowing players to trigger the `goToFengus` sequence or receive quests.
*   **`GetAI_boss_kromcrush`**: Factory function returning a new `boss_kromcrushAI`.

#### `boss_prince_tortheldrinAI`
*   **`boss_prince_tortheldrinAI`**: Constructor stores instance pointer.
*   **`Reset#7`**: Sets timers for Arcane Blast, Counterspell, Summon, Whirlwind. Casts Thrash.
*   **`UpdateAI#7`**:
    *   Casts Summon on timer.
    *   Casts Whirlwind if melee attackers are present.
    *   Casts Arcane Blast on timer, resetting threat.
    *   Casts Counterspell on mana-using players casting spells.
*   **`GetAI_boss_prince_tortheldrin`**: Factory function returning a new `boss_prince_tortheldrinAI`.

#### `boss_alzzin_the_wildshaperAI`
*   **`boss_alzzin_the_wildshaperAI`**: Constructor stores instance pointer.
*   **`Reset#2`**: Sets timers for various spells and phases. Casts Thorns.
*   **`SummonAdds`**: Summons 15 minions at predefined coordinates.
*   **`ChangeForm`**: Randomly switches between Normal, Wolf, and Tree forms, removing old form auras and applying new ones.
*   **`AuraRemoved`**: Tracks if Thorns aura falls off.
*   **`SummonedCreatureJustDied`**: Despawns minions if Alzzin hasn't summoned them yet (likely a cleanup for pre-spawned adds).
*   **`MovementInform`**: Handles out-of-combat movement between two coordinates.
*   **`UpdateAI#2`**:
    *   Out of combat: Moves between coordinates.
    *   In combat:
        *   Summons adds and calls for help at 45% health.
        *   Evades if moved too far from spawn zone.
        *   Changes form on timer.
        *   Casts form-specific spells: Wither/Enervate (Normal), Mangle/Vicious Bite (Wolf), Knock Away/Disarm/Wild Regeneration (Tree).
*   **`GetAI_boss_alzzin_the_wildshaper`**: Factory function returning a new `boss_alzzin_the_wildshaperAI`.

#### `npc_alzzins_minionAI`
*   **`npc_alzzins_minionAI`**: Constructor calls `Reset()`.
*   **`Reset#8`**: If temporary summon, follows summoner.
*   **`MoveInLineOfSight#2`**: Aggroes players within 30 yards if not in combat.
*   **`GetAI_npc_alzzins_minion`**: Factory function returning a new `npc_alzzins_minionAI`.

#### `boss_ferraAI`
*   **`boss_ferraAI`**: Constructor stores instance pointer.
*   **`Reset#3`**: Sets timers. Disables call for assistance.
*   **`MoveInLineOfSight`**: Aggroes players within 80 yards, but checks Z-distance to prevent aggroing through floors.
*   **`UpdateAI#3`**: Casts Maul and Charge on timers.
*   **`GetAI_boss_ferra`**: Factory function returning a new `boss_ferraAI`.

#### `boss_magister_kalendrisAI`
*   **`boss_magister_kalendrisAI`**: Constructor stores instance pointer.
*   **`Reset#6`**: Sets timers.
*   **`UpdateAI#6`**:
    *   Casts Shadow Word Pain, Mind Flay, Mind Blast, Dominate Mind on timers.
    *   Enters Shadowform at 50% health.
    *   Manages movement: Moves to melee range if too far or low mana, stays at range if in optimal distance and mana.
*   **`GetAI_boss_magister_kalendris`**: Factory function returning a new `boss_magister_kalendrisAI`.

#### `go_warpwood_pod`
*   **`go_warpwood_pod`**: Constructor.
*   **`OnUse`**: If the pod has a linked trap, it casts the trap's spell on the user and sets the loot state to deactivated.
*   **`GetAI_go_warpwood_pod`**: Factory function returning a new `go_warpwood_pod`.

### Registration
*   **`AddSC_instance_dire_maul`**: Registers all scripts (instance, AIs, gossip, quest handlers) with the script manager.
*   **`GetInstanceData_instance_dire_maul`**: Function that creates and returns a new `instance_dire_maul` object.

## Cross-Unit Boundaries

*   **`WorldObject.Object`**: Used extensively for positioning, flagging, summoning, and communication (yells/says).
*   **`ScriptedInstance`**: Base class for `instance_dire_maul`. Provides `DoRespawnGameObject`, `DoUseDoorOrButton`, and `SaveToDB`.
*   **`ScriptedAI`**: Base class for most creature AIs. Provides `DoCastSpellIfCan`, `DoMeleeAttackIfReady`, `SelectHostileTarget`, etc.
*   **`GameObjectAI`**: Base class for `go_fixed_trap` and `go_warpwood_pod`.
*   **`Map.Main`**: Used to retrieve creatures and game objects by GUID (`GetCreature`, `GetGameObject`).
*   **`Player.Main`**: Used for item checks (`HasItemCount`, `DestroyItemCount`) and quest status (`GetQuestStatus`, `GetSkillValueBase`).
*   **`Unit.Main`**: Used for faction changes, health/power checks, threat management, and motion control.
*   **`ScriptMgr`**: Used to play sounds (`DoScriptText`).
*   **`GossipDef`**: Used to build and send gossip menus.
*   **`QuestDef`**: Used to identify quests in reward handlers.
*   **`shared_Util`**: Used for random number generation (`urand`, `frand`, `roll_chance_i`).
*   **`Log.Main`**: Used for debug logging (`sLog.Out`).
*   **`Errors`**: `PrintStacktraceAndThrow` is called in `GetData` if an invalid type is requested.
*   **`GridSearchers`**: `GetCreatureListWithEntryInGrid` is used in `npc_residual_montruosityAI` to find nearby allies for formation logic.
*   **`ThreatManager`**: Used in `boss_guardsAI` to modify threat.
*   **`MotionMaster`**: Used for movement commands (`MovePoint`, `MoveTargetedHome`, `Clear`).
*   **`CreatureAI`**: Base class for AI methods like `AttackStart`.
*   **`SpellCaster`**: Used for `IsNonMeleeSpellCasted` and `CastSpell`.
*   **`ObjectGuid`**: Used for GUID manipulation.
*   **`ZoneScript`**: `GetMap` is used in `OnCreatureDeath` to summon creatures.
*   **`Creature.MotionMaster`**: Specific motion master methods.
*   **`Creature.Main`**: Specific creature methods like `SetFactionTemporary`, `SetLootRecipient`, `LoadEquipment`.
*   **`GameObject`**: Specific game object methods like `SetGoState`, `Delete`, `SendGameObjectCustomAnim`.
*   **`Object`**: Basic object methods like `GetEntry`, `GetGUID`, `GetObjectGuid`.
*   **`TemporarySummon`**: Used in `npc_alzzins_minionAI` to get summoner GUID.
*   **`PlayerMenu`**: Used to access gossip menu objects.
*   **`ObjectMgr`**: Used in `go_warpwood_pod` to get game object template info.

## Data Model

This unit does not directly interact with database tables via SQL queries. It relies on the `ScriptedInstance` base class to handle saving and loading instance data to/from the `instance` table (specifically the `data` column) using the `SaveToDB` and `Load` methods. The data format is a space-separated string of encounter states.

## Notable Implementation Details

*   **Crystal Event Logic**: The Immol'Thar crystal event is complex. `DoSortCristalsEventMobs` pre-calculates which mobs belong to which crystal based on distance. As mobs die, `SetData64` removes them from the sorted lists. When a list is empty, the corresponding crystal door opens. This decouples the mob death from the immediate door opening, allowing for a synchronized event completion.
*   **Gordok Tribute**: The system tracks the number of alive guards (`m_uiGuardAliveCount`). When guards die, they signal `SPECIAL` state, decrementing the count. When the tribute is completed, the specific tribute game object respawned depends on how many guards survived, creating a variable outcome.
*   **Exploit Prevention**: `OnPlayerEnter` explicitly checks for and removes the `ITEM_GORDOK_INNER_DOOR_KEY` if the associated boss isn't dead, preventing players from carrying keys across instance resets.
*   **Slip'kik Trap Bug Fix**: `boss_guardsAI` includes a specific timer (`m_uiCombatBugTimer`) to force Slip'kik out of combat if he gets stuck with the `SPELL_ICE_LOCK` aura, addressing a known bug where he wouldn't leave combat properly.
*   **Alzzin's Form Switching**: Alzzin uses a bitmask-like approach (`m_uiPhaseMask`) to determine his next form, ensuring he doesn't repeat the same form consecutively.
*   **Kromcrush Pathfinding**: Kromcrush's movement is handled via `MovementInform` with hardcoded waypoints. His gossip interaction can trigger a specific path (`goToFengus`) that leads him to another boss.
*   **Hardcoded Coordinates**: Many movement paths and summon locations use hardcoded coordinates, which may require adjustment if map geometry changes.
*   **Debug Logging**: Extensive debug logging is present but guarded by `#ifdef DEBUG_ON`, indicating this code was likely in active development or testing.

## Member Reference

**EnableCreature**: Function that removes `UNIT_FLAG_NOT_SELECTABLE`, `UNIT_FLAG_SPAWNING`, and `UNIT_FLAG_IMMUNE_TO_NPC` flags from a creature, making it interactable and attackable.

**instance_dire_maul**: Constructor for the instance script. Initializes all GUIDs to 0, sets default counts, and calls `Initialize()`.

**Initialize**: Method that zeros out the encounter array and crystal GUID array, and clears the Felvine Shard GUID list.

**OnPlayerEnter**: Method triggered when a player enters the instance. Checks for and removes exploit items, and respawns the fixed trap if the broken trap quest is complete.

**OnPlayerLeave**: Method triggered when a player leaves. Removes the `SPELL_KING_OF_GORDOK` aura.

**OnObjectCreate**: Method triggered when a game object spawns. Registers GUIDs for doors, crystals, and tribute items, and activates doors if relevant bosses are dead.

**~instance_dire_maul**: Destructor. No custom logic.

**Save**: Method that returns the serialized instance data string for database persistence.

**OnCreatureDeath**: Method triggered when a creature dies. Updates instance state for boss kills, triggers specific events (e.g., summoning Mizzle, making Cho'Rush friendly), and handles guard deaths for the Gordok Tribute.

**OnCreatureCreate**: Method triggered when a creature spawns. Registers GUIDs, applies initial flags (e.g., unselectable Immol'Thar), and sets faction/stand state for Cho'Rush if tribute is complete.

**SetData**: Method that updates instance state based on type and data. Handles crystal event completion, boss deaths, Gordok Tribute logic, and door openings. Saves to DB if state is `DONE`.

**SetData64**: Method that updates 64-bit data. Handles crystal event mob removal and dreadsteed ritual player storage.

**Load**: Method that parses saved instance data from the database. Resets `IN_PROGRESS` states to `NOT_STARTED`.

**GetData**: Method that returns the state of a specific encounter type or the `m_bIsTanninLooted` boolean. Throws an error if an invalid type is requested.

**GetData64**: Method that returns stored GUIDs for bosses, game objects, or the ritual player.

**GetChoRushEquipment**: Method that returns a random equipment ID (1-3) for Cho'Rush if not already set, storing it in instance data.

**DoSortCristalsEventMobs**: Method that assigns crystal event mobs to the nearest crystal's sorted list based on distance.

**GetInstanceData_instance_dire_maul**: Function that creates and returns a new `instance_dire_maul` object.

**npc_reste_manaAI**: Constructor for the Reste Mana AI. Stores instance pointer and calls `Reset()`.

**Reset#13**: Method for `npc_reste_manaAI`. Sets timers for Blink and Chain Lightning, and applies arcane immunity.

**JustDied#5**: Method for `npc_reste_mana

---

<!-- machine-true, projected from graph.json -->

## Map — instance_dire_maul

*Source:* instance_dire_maul.cpp, dire_maul.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| EnableCreature | function | WorldObject.Object/RemoveFlag | — | — |
| instance_dire_maul | ctor | ScriptedInstance/ScriptedInstance | — | — |
| Initialize | method | — | — | — |
| OnPlayerEnter | method | GameObject/Delete, Map.Main/GetGameObject, ObjectGuid/ObjectGuid#5, Player.Main/DestroyItemCount#2, Player.Main/HasItemCount, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SummonGameObject | — | — |
| OnPlayerLeave | method | Unit.Main/RemoveAurasDueToSpell | — | — |
| OnObjectCreate | method | GameObject/SetGoState, Object/GetEntry, Object/GetGUID, Object/GetObjectGuid | — | — |
| ~instance_dire_maul | dtor | — | — | — |
| Save | method | — | — | — |
| OnCreatureDeath | method | Creature.Main/SetFactionTemporary, Map.Main/GetCreature, Object/GetEntry, ObjectGuid/ObjectGuid#5, ScriptMgr/DoScriptText, Unit.Main/IsAlive, WorldObject.Object/MonsterYell#2, WorldObject.Object/SummonCreature, ZoneScript/GetMap#2 | — | — |
| OnCreatureCreate | method | Object/GetEntry, Object/GetGUID, Unit.Main/SetFactionTemplateId, Unit.Main/SetStandState, WorldObject.Object/SetFlag | — | — |
| SetData | method | Creature.Main/AI, Creature.Main/SetFactionTemporary, CreatureAI/AttackStart, InstanceData/SaveToDB, Log.Main/Out, Map.Main/GetCreature, Map.Main/GetId, Map.Main/GetInstanceId, Map.Main/GetMapName, Object/GetGUIDLow, ObjectGuid/ObjectGuid#5, ScriptedInstance/DoRespawnGameObject, ScriptedInstance/DoUseDoorOrButton, ScriptMgr/DoScriptText, Unit.Main/IsAlive, Unit.Main/SetFactionTemplateId, WorldObject.Object/GetDistance#3, WorldObject.Object/RemoveFlag | boss_immol_thar/JustDied, boss_tendris_warpwood/AttackStart, boss_zevrim/JustDied | — |
| SetData64 | method | ScriptedInstance/DoUseDoorOrButton | dreadsteed_ritual/EventStart | — |
| Load | method | Log.Main/Out, Map.Main/GetId, Map.Main/GetInstanceId, Map.Main/GetMapName | — | — |
| GetData | method | Errors/PrintStacktraceAndThrow | — | — |
| GetData64 | method | — | boss_gordok_king/UpdateAI, boss_gordok_king/UpdateAI#2, dreadsteed_ritual/UpdateAI#4 | — |
| GetChoRushEquipment | method | shared_Util/urand | boss_gordok_king/Reset | — |
| DoSortCristalsEventMobs | method | Map.Main/GetCreature, Map.Main/GetGameObject, ObjectGuid/ObjectGuid#5, Unit.Main/IsAlive, WorldObject.Object/GetDistance#3 | — | — |
| GetInstanceData_instance_dire_maul | function | — | — | — |
| npc_reste_manaAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset#13 | method | shared_Util/urand, Unit.Main/ApplySpellImmune | — | — |
| JustDied#5 | method | Object/GetGUID | — | — |
| UpdateAI#11 | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_npc_reste_mana | function | — | — | — |
| npc_arcane_aberrationAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset#9 | method | shared_Util/urand, Unit.Main/ApplySpellImmune | — | — |
| JustDied#3 | method | Object/GetGUID | — | — |
| DamageTaken | method | CreatureAI/DoCastSpellIfCan, Unit.Main/GetHealthPercent | — | — |
| UpdateAI#9 | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_npc_arcane_aberration | function | — | — | — |
| npc_residual_montruosityAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset#12 | method | shared_Util/urand, Unit.Main/ApplySpellImmune | — | — |
| JustDied#4 | method | CreatureAI/DoCastSpellIfCan | — | — |
| UpdateFormationSpeed | method | GridSearchers/GetCreatureListWithEntryInGrid#2, Unit.Main/GetVictim, Unit.Main/SetSpeedRate, WorldObject.Object/GetDistance#3, WorldObject.Object/HasInArc, WorldObject.Object/IsWithinDistInMap | — | — |
| UpdateAI#10 | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_npc_residual_montruosity | function | — | — | — |
| QuestRewarded_go_broken_trap | function | GameObject/Delete, QuestDef/GetQuestId, WorldObject.Object/GetInstanceData, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SetFlag, WorldObject.Object/SummonGameObject | — | — |
| npc_mizzle_the_craftyAI | ctor | Creature.Main/SetHomePosition, Creature.MotionMaster/MoveTargetedHome, ScriptedAI/ScriptedAI, ScriptMgr/DoScriptText, Unit.Main/GetMotionMaster, WorldObject.Object/GetInstanceData | — | — |
| Reset#11 | method | — | — | — |
| JustReachedHome | method | ScriptMgr/DoScriptText, WorldObject.Object/SetFlag | — | — |
| SpellHitTarget#2 | method | WorldObject.Object/SetOrientation | — | — |
| GetAI_npc_mizzle_the_crafty | function | — | — | — |
| npc_knot_thimblejackAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset#10 | method | — | — | — |
| MovementInform#3 | method | Creature.Main/ForcedDespawn, Creature.MotionMaster/MovePoint, Unit.Main/GetMotionMaster | — | — |
| GetAI_npc_knot_thimblejack | function | — | — | — |
| GossipHello_npc_knot_thimblejack | function | GossipDef/AddMenuItem#4, GossipDef/SendGossipMenu, Object/GetObjectGuid, Player.Main/GetQuestRewardStatus, Player.Main/GetQuestStatus, Player.Main/GetSkillValueBase, Player.Main/HasSpell, Player.Main/PrepareQuestMenu, PlayerMenu/GetGossipMenu, Unit.Main/IsQuestGiver | — | — |
| GossipSelect_npc_knot_thimblejack | function | GossipDef/ClearMenus, GossipDef/SendGossipMenu, Object/GetObjectGuid, SpellCaster/CastSpell#2 | — | — |
| QuestRewarded_npc_knot_thimblejack | function | Creature.MotionMaster/MovePoint, GameObject/Delete, QuestDef/GetQuestId, Unit.Main/GetMotionMaster, WorldObject.Object/FindNearestGameObject, WorldObject.Object/GetInstanceData, WorldObject.Object/RemoveFlag | — | — |
| GordokBruteAI | ctor | Creature.Main/GetCurrentEquipmentId, ScriptedAI/ScriptedAI | — | — |
| Reset | method | Creature.Main/LoadEquipment | — | — |
| Aggro | method | shared_Util/urand, WorldObject.Object/GetName, WorldObject.Object/MonsterSay | — | — |
| UpdateAI | method | Creature.Main/LoadEquipment, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, SpellCaster/CastSpell#2, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/GetHealthPercent, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, WorldObject.Object/MonsterTextEmote | — | — |
| GetAI_mob_gordok_brute | function | — | — | — |
| boss_guardsAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset#4 | method | shared_Util/urand | — | — |
| JustDied | method | Creature.Main/SetLootRecipient, WorldObject.Object/MonsterSay | — | — |
| SpellHitTarget | method | ThreatManager/modifyThreatPercent#2, Unit.Main/GetThreatManager | — | — |
| UpdateAI#4 | method | Creature.Main/CallForHelp, Creature.Main/SelectAttackingTarget#2, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Object/GetEntry, ScriptMgr/DoScriptText, shared_Util/urand, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/CombatStop, Unit.Main/DeleteThreatList, Unit.Main/GetHealthPercent, Unit.Main/GetVictim, Unit.Main/HasAura#2, Unit.Main/IsInCombat, Unit.Main/SelectHostileTarget | — | — |
| GetAI_boss_guards | function | — | — | — |
| go_fixed_trap | ctor | GameObjectAI/GameObjectAI, WorldObject.Object/GetInstanceData | — | — |
| UpdateAI#8 | method | GameObject/Delete, GameObject/SendGameObjectCustomAnim, Map.Main/GetCreature, ObjectGuid/ObjectGuid#5, SpellCaster/CastSpell#2, Unit.Main/CombatStop, Unit.Main/DeleteThreatList, WorldObject.Object/GetMap, WorldObject.Object/IsWithinDist, WorldObject.Object/SetFlag | — | — |
| GetAI_go_fixed_trap | function | — | — | — |
| boss_kromcrushAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset#5 | method | shared_Util/urand | — | — |
| goToFengus | method | Creature.MotionMaster/MovePoint, CreatureAI/DoCastSpellIfCan, ScriptMgr/DoScriptText, Unit.Main/GetMotionMaster, Unit.Main/SetWalk, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag | — | — |
| Aggro#2 | method | ScriptMgr/DoScriptText | — | — |
| MovementInform#2 | method | Creature.MotionMaster/MovePoint, MotionMaster/Clear, ScriptMgr/DoScriptText, Unit.Main/GetMotionMaster, WorldObject.Object/Relocate, WorldObject.Object/RemoveFlag | — | — |
| JustDied#2 | method | Creature.Main/SetLootRecipient, WorldObject.Object/MonsterSay | — | — |
| EnterEvadeMode | method | Creature.Main/AI, CreatureAI/MovementInform, MotionMaster/Clear, ScriptedAI/EnterEvadeMode, Unit.Main/GetMotionMaster | — | — |
| JustSummoned | method | Creature.Main/AI, Creature.Main/SelectAttackingTarget, CreatureAI/AttackStart | — | — |
| CallReavers | method | Unit.Main/HandleEmote, WorldObject.Object/GetDistance#4, WorldObject.Object/SummonCreature#2 | — | — |
| UpdateAI#5 | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, ScriptMgr/DoScriptText, shared_Util/urand, Unit.Main/GetHealthPercent, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GossipHello_boss_kromcrush | function | GossipDef/AddMenuItem#4, GossipDef/SendGossipMenu, Object/GetObjectGuid, Player.Main/PrepareQuestMenu, PlayerMenu/GetGossipMenu, Unit.Main/IsQuestGiver, WorldObject.Object/GetInstanceData | — | — |
| GossipSelect_boss_kromcrush | function | Creature.Main/AI, GossipDef/AddMenuItem#4, GossipDef/ClearMenus, GossipDef/CloseGossip, GossipDef/SendGossipMenu, Object/GetObjectGuid, PlayerMenu/GetGossipMenu, WorldObject.Object/GetInstanceData, WorldObject.Object/SetFlag | — | — |
| GetAI_boss_kromcrush | function | — | — | — |
| boss_prince_tortheldrinAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset#7 | method | CreatureAI/DoCastSpellIfCan, shared_Util/urand | — | — |
| UpdateAI#7 | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Map.Main/GetUnit, Object/GetGUID, ObjectGuid/ObjectGuid#5, ScriptedAI/DoResetThreat, shared_Util/urand, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/GetAttackers, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, WorldObject.Object/GetMap, WorldObject.Object/IsInRange | — | — |
| GetAI_boss_prince_tortheldrin | function | — | — | — |
| boss_alzzin_the_wildshaperAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset#2 | method | CreatureAI/DoCastSpellIfCan, shared_Util/urand | — | — |
| SummonAdds | method | Creature.Main/AI, CreatureAI/AttackStart, Unit.Main/GetVictim, WorldObject.Object/SummonCreature#2 | — | — |
| ChangeForm | method | CreatureAI/DoCastSpellIfCan, shared_Util/roll_chance_i, Unit.Main/RemoveAurasDueToSpell | — | — |
| AuraRemoved | method | — | — | — |
| SummonedCreatureJustDied | method | Creature.Main/DespawnOrUnsummon | — | — |
| MovementInform | method | Creature.MotionMaster/Initialize, Creature.MotionMaster/MoveIdle, CreatureAI/DoCastSpellIfCan, shared_Util/urand, Unit.Main/GetMotionMaster, Unit.Main/SetFacingTo | — | — |
| UpdateAI#2 | method | Creature.Main/AI, Creature.Main/CallForHelp, Creature.Main/SelectAttackingTarget, Creature.MotionMaster/MovePoint, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, CreatureAI/EnterEvadeMode, InstanceData/GetData, InstanceData/SetData, shared_Util/urand, SpellCaster/InterruptNonMeleeSpells, Unit.Main/GetHealthPercent, Unit.Main/GetMotionMaster, Unit.Main/GetVictim, Unit.Main/HasAura#2, Unit.Main/SelectHostileTarget, WorldObject.Object/IsInRange3d | — | — |
| npc_alzzins_minionAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#8 | method | Creature.Main/IsTemporarySummon, Creature.MotionMaster/MoveFollow, Map.Main/GetUnit, shared_Util/frand, TemporarySummon/GetSummonerGuid, Unit.Main/GetMotionMaster, Unit.Main/IsAlive, WorldObject.Object/GetMap | — | — |
| MoveInLineOfSight#2 | method | Object/IsPlayer, Unit.Main/AttackedBy, Unit.Main/IsInCombat, Unit.Main/IsTargetableBy, WorldObject.Object/IsWithinDistInMap, WorldObject.Object/IsWithinLOSInMap | — | — |
| GetAI_boss_alzzin_the_wildshaper | function | — | — | — |
| GetAI_npc_alzzins_minion | function | — | — | — |
| boss_ferraAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset#3 | method | Creature.Main/SetNoCallAssistance, shared_Util/urand | — | — |
| MoveInLineOfSight | method | Creature.Main/AI, CreatureAI/AttackStart, Object/IsPlayer, Unit.Main/IsInCombat, Unit.Main/IsTargetableBy, WorldObject.Object/GetPositionZ, WorldObject.Object/IsWithinDistInMap, WorldObject.Object/IsWithinLOSInMap | — | — |
| UpdateAI#3 | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_boss_ferra | function | — | — | — |
| boss_magister_kalendrisAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset#6 | method | shared_Util/urand | — | — |
| UpdateAI#6 | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, CreatureAI/IsCombatMovementEnabled, CreatureAI/SetCombatMovement, ScriptedAI/DoStartMovement, ScriptedAI/DoStartNoMovement, shared_Util/urand, Unit.Main/GetHealthPercent, Unit.Main/GetPowerPercent, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, WorldObject.Object/GetDistance2d#3, WorldObject.Object/IsWithinLOSInMap | — | — |
| GetAI_boss_magister_kalendris | function | — | — | — |
| go_warpwood_pod | ctor | GameObjectAI/GameObjectAI | — | — |
| OnUse | method | GameObject/GetGOInfo, GameObject/SetLootState, ObjectMgr/GetGameObjectTemplate, SpellCaster/CastSpell#2 | — | — |
| GetAI_go_warpwood_pod | function | — | — | — |
| AddSC_instance_dire_maul | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
