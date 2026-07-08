# instance_scarlet_monastery

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# instance_scarlet_monastery

**Purpose & Responsibilities**
`instance_scarlet_monastery` is the `ScriptedInstance` handler for the Scarlet Monastery dungeon instance in the WoW server emulation. It manages two primary scripted encounters:
1.  **The Mograine & Whitemane Encounter:** A complex, multi-stage boss fight involving Commander Mograine and Inquisitor Whitemane. This includes handling Mograine's temporary death, Whitemane's attempt to revive him, and the final resolution of the fight.
2.  **The Ashbringer Questline:** A cinematic event triggered when a player wielding the Ashbringer sword enters the Cathedral entrance. This involves summoning a spectral Highlord Mograine, playing a dialogue sequence, and altering the faction of various NPCs in the area.

The class tracks the GUIDs of key bosses and doors, maintains the state of these encounters via `m_auiEncounter`, and handles the timing and execution of cinematic events using an `EventMap`. It also persists encounter states to the database upon completion.

**Member-by-Member Behavior**

### Initialization and Object Tracking
*   **`instance_scarlet_monastery` (ctor):** Initializes the instance data, calling `Initialize()` to reset all encounter states and GUIDs.
*   **`Initialize`:** Resets the `m_auiEncounter` array to zero, clears all tracked GUIDs (`m_uiMograineGUID`, etc.), clears the set of NPCs that react to the Ashbringer (`m_ashbringerReactedNpcs`), and resets the `EventMap`.
*   **`OnCreatureCreate`:** Called when a creature spawns in the instance. It checks the creature's entry ID:
    *   If it's one of the specific Scarlet NPCs (Sorcerer, Myridon, Defender, etc.) or Commander Mograine, it adds their GUID to `m_ashbringerReactedNpcs`.
    *   If it's Commander Mograine, Inquisitor Whitemane, or Vorrel Sengutz, it stores their GUID in the respective member variable (`m_uiMograineGUID`, `m_uiWhitemaneGUID`, `m_uiVorrelGUID`).
*   **`OnObjectCreate`:** Called when a game object spawns. It stores the GUIDs for the High Inquisitor Door and the Chapel Door in `m_uiDoorHighInquisitorGUID` and `m_uiChapelDoorGUID` respectively.

### Encounter State Management
*   **`OnCreatureDeath`:** Handles the death of Commander Mograine or Inquisitor Whitemane. If one dies, it checks if the other is also dead. If both are dead, it sets the `TYPE_MOGRAINE_AND_WHITE_EVENT` state to `STAGE_MOGRAINE_DONE`.
*   **`GetData64`:** Returns the stored GUID for a requested entity (Mograine, Whitemane, Vorrel, High Inquisitor Door, or Chapel Door) based on the provided data type.
*   **`IsMograineOrWhitemaneDead`:** Checks if either Mograine or Whitemane is dead or despawned. Returns `true` if either condition is met for either boss.
*   **`SetData`:** The core logic for updating encounter states.
    *   **`TYPE_MOGRAINE_AND_WHITE_EVENT`:**
        *   If setting to `NOT_STARTED` but either boss is already dead, it forces the state to `DONE` and removes the dead bosses from the world.
        *   If setting to `NOT_STARTED` or `IN_PROGRESS`:
            *   Resets the High Inquisitor Door to `GO_STATE_READY`.
            *   If transitioning from `DIED_ONCE` to `NOT_STARTED`, it respawns Whitemane.
            *   If setting to `IN_PROGRESS` and Mograine has a victim, it summons nearby Scarlet assist creatures (Chaplains, Wizards, etc.) to attack Mograine's victim.
        *   If setting to `DIED_ONCE`:
            *   Sets the High Inquisitor Door to `GO_STATE_ACTIVE`.
            *   Makes Whitemane yell and move to Mograine's position.
        *   If setting to `REVIVED`:
            *   Forces both Mograine and Whitemane into combat with the zone if they are alive and not already in combat.
        *   If setting to `DONE`, it saves the instance data to the database.
    *   **`TYPE_ASHBRINGER_EVENT`:**
        *   If setting to `IN_PROGRESS`:
            *   Loads the grid around the Chapel Door.
            *   Activates and loots the Chapel Door.
            *   Despawns Whitemane if she is alive and not in combat.
            *   Changes the faction of all NPCs in `m_ashbringerReactedNpcs` to faction 35 (likely hostile to the Ashbringer wielder).
        *   If setting to `DONE`, it saves the instance data to the database.
*   **`GetData`:** Returns the current state of the `TYPE_MOGRAINE_AND_WHITE_EVENT` or `TYPE_ASHBRINGER_EVENT` from `m_auiEncounter`.

### Persistence
*   **`Load`:** Reads the saved encounter states from the database string. It parses the two encounter states into `m_auiEncounter`. Any state marked as `IN_PROGRESS` is reset to `NOT_STARTED` to prevent stuck instances.
*   **`Save`:** Serializes the two encounter states from `m_auiEncounter` into a space-separated string, stored in `m_strInstData`, and returns a pointer to it.

### Ashbringer Cinematic Event
*   **`OnCreatureSpellHit`:** Triggered when a spell hits a creature. If the Ashbringer event is not active, the caster is a player, the spell is `SPELL_AB_EFFECT_000` (Ashbringer aura), and the target is Commander Mograine, it initiates the cinematic:
    *   Sets the Ashbringer wielder's GUID.
    *   Makes Mograine face the player.
    *   Schedules the first events (`EVENT_KNEEL` and `EVENT_SUMMON`) in the `EventMap`.
    *   Sets `m_ashbringerActive` to `true`.
*   **`Update`:** Processes the `EventMap` if `m_ashbringerActive` is `true`. It executes scheduled events in order:
    *   `EVENT_KNEEL`: Mograine kneels and unsheathes weapons. Schedules `EVENT_TALK1`.
    *   `EVENT_TALK1`: Mograine speaks to the Ashbringer wielder.
    *   `EVENT_SUMMON`: Summons Highlord Mograine (spectral form) near Commander Mograine. The summoned creature is scaled up, given a specific display ID, and moves to a position. Schedules `EVENT_TALK2`.
    *   `EVENT_TALK2`: Highlord stops moving, faces Commander Mograine, and speaks. Schedules `EVENT_STAND`.
    *   `EVENT_STAND`: Commander Mograine stands up, sheathes weapons, and faces Highlord. Schedules `EVENT_TALK3`.
    *   `EVENT_TALK3`: Commander Mograine speaks. Schedules `EVENT_TALK4`.
    *   `EVENT_TALK4`: Highlord speaks. Schedules `EVENT_SECOND_DOUBT`.
    *   `EVENT_SECOND_DOUBT`: Highlord performs a questioning emote. Schedules `EVENT_POINT`.
    *   `EVENT_POINT`: Highlord points. Schedules `EVENT_ROAR`.
    *   `EVENT_ROAR`: Highlord roars. Schedules `EVENT_TALK5`.
    *   `EVENT_TALK5`: Commander Mograine speaks. Schedules `EVENT_SPELL`.
    *   `EVENT_SPELL`: Highlord casts `SPELL_FORGIVENESS` on Commander Mograine. Schedules `EVENT_FORGIVEN`.
    *   `EVENT_FORGIVEN`: Highlord speaks. Schedules `EVENT_DESPAWN`.
    *   `EVENT_DESPAWN`: Highlord despawns. Clears the Ashbringer wielder GUID.

### Script Registration
*   **`GetInstanceData_instance_scarlet_monastery`:** Factory function to create a new `instance_scarlet_monastery` object for a given map.
*   **`AreaTrigger_at_cathedral_entrance`:** Triggered when a player enters area trigger 4089 (Cathedral entrance). If the player has the Ashbringer aura and the Ashbringer event hasn't started, it starts the event (`IN_PROGRESS`) and makes Commander Mograine yell at the player.
*   **`AddSC_instance_scarlet_monastery`:** Registers the instance script and the area trigger script with the script manager.

**Cross-Unit Boundaries**
*   **Calls Out:**
    *   `ScriptedInstance`: Base class functionality for instance management.
    *   `EventMap`: Used for scheduling and executing timed events in the Ashbringer cinematic.
    *   `Object/GetEntry`, `Object/GetGUID`: To identify creatures and game objects.
    *   `Unit.Main/IsDead`, `Unit.Main/IsAlive`, `Unit.Main/IsInCombat`: To check the state of bosses and NPCs.
    *   `ZoneScript/GetCreature`, `ZoneScript/GetGameObject`: To retrieve pointers to creatures and game objects by GUID.
    *   `Creature.Main/AI`, `CreatureAI/AttackStart`: To command assist creatures to attack.
    *   `Creature.Main/DespawnOrUnsummon`, `Creature.Main/Respawn`, `Creature.Main/SetDeathState`: To manage the lifecycle of bosses.
    *   `Creature.MotionMaster/MovePoint`: To move Whitemane and the summoned Highlord.
    *   `GameObject/SetGoState`, `GameObject/SetLootState`, `GameObject/SetFlag`: To control the state of doors.
    *   `GridSearchers/GetCreatureListWithEntryInGrid`: To find nearby assist creatures.
    *   `InstanceData/SaveToDB`: To persist encounter states.
    *   `Log.Main/Out`: For logging save/load operations.
    *   `Map.Main/GetCreature`, `Map.Main/GetGameObject`, `Map.Main/GetId`, `Map.Main/GetInstanceId`, `Map.Main/GetMapName`, `Map.Main/LoadGrid`: For map-level queries and grid loading.
    *   `ObjectGuid/ObjectGuid#5`: For GUID manipulation.
    *   `ScriptMgr/DoScriptText`: To play dialogue lines.
    *   `Unit.Main/GetMotionMaster`, `Unit.Main/GetVictim`, `Unit.Main/SetFactionTemplateId`, `Unit.Main/SetFacingToObject`, `Unit.Main/SetSheath`, `Unit.Main/SetStandState`, `Unit.Main/HandleEmote`, `Unit.Main/HandleEmoteCommand`, `Unit.Main/SetDisplayId`, `Unit.Main/SetObjectScale`, `Unit.Main/StopMoving`: For controlling unit behavior during cinematics.
    *   `WorldObject.Object/AddObjectToRemoveList`, `WorldObject.Object/GetPositionX/Y/Z`, `WorldObject.Object/SummonCreature#2`: For object management and positioning.
    *   `SpellCaster/CastSpell#2`: For casting spells during cinematics.
    *   `TemporarySummon/UnSummon`: To despawn the summoned Highlord.
*   **Called By:**
    *   `ScriptLoader/AddScripts`: Calls `AddSC_instance_scarlet_monastery` during server startup to register the scripts.

**Data Model**
This unit does not directly interact with database tables via SQL queries. It uses the `InstanceData` interface (`SaveToDB`, `Load`, `Save`) to persist encounter states. The actual database schema for instance data is managed by the core engine, typically involving a table like `instance` with columns for `instanceId`, `map`, `data`, etc. The `data` column would contain the space-separated string generated by the `Save()` method.

**Notable Implementation Details**
*   **Ashbringer Faction Change:** When the Ashbringer event starts, all NPCs in `m_ashbringerReactedNpcs` have their faction changed to 35. This likely makes them hostile to the Ashbringer wielder, reflecting the lore where the Ashbringer purges the Scarlet Crusade.
*   **Mograine Assist Spawns:** During the `IN_PROGRESS` stage of the Mograine/Whitemane fight, if Mograine has a victim, nearby Scarlet creatures are spawned to assist him. This adds complexity to the fight.
*   **Cinematic Timing:** The Ashbringer cinematic relies heavily on precise timing via `EventMap`. Delays between dialogue lines and actions are carefully controlled.
*   **State Persistence:** Only the final `DONE` state of the encounters is saved to the database. Intermediate states are lost on reload, which is typical for instance scripts to avoid saving transient cinematic states.
*   **Grid Loading:** The Ashbringer event explicitly loads the grid around the Chapel Door to ensure all relevant objects are present for the cinematic.
*   **Hardcoded Coordinates:** The summoning of Highlord Mograine and his movement use hardcoded coordinates. This makes the cinematic fragile to map changes.
*   **TODO Comment:** There is a `TODO` comment in `EVENT_SPELL` noting that the spell cast by Highlord doesn't produce the expected lightning effect. This indicates a known visual bug.

## Member Reference

**instance_scarlet_monastery**: Constructor for the instance script. Initializes member variables and calls `Initialize()`.

**Initialize**: Resets all encounter states, GUIDs, and the event map to their initial values.

**OnCreatureCreate**: Tracks GUIDs of key bosses and adds specific NPCs to the Ashbringer reaction list when they spawn.

**OnObjectCreate**: Tracks GUIDs of key doors when they spawn.

**OnCreatureDeath**: Checks if both Mograine and Whitemane are dead and updates the encounter state accordingly.

**GetData64**: Returns the GUID of a specified boss or door.

**IsMograineOrWhitemaneDead**: Checks if either Mograine or Whitemane is dead or despawned.

**SetData**: Updates the state of the Mograine/Whitemane or Ashbringer encounters, triggering associated logic like spawning assists, moving NPCs, changing factions, or saving to DB.

**GetData**: Returns the current state of a specified encounter.

**Load**: Parses saved encounter states from the database, resetting any `IN_PROGRESS` states to `NOT_STARTED`.

**Save**: Serializes encounter states into a string for database persistence.

**OnCreatureSpellHit**: Initiates the Ashbringer cinematic if a player with the Ashbringer aura hits Commander Mograine with the Ashbringer spell.

**Update**: Processes the event map for the Ashbringer cinematic, executing timed dialogue, movements, and spell casts.

**GetInstanceData_instance_scarlet_monastery**: Factory function to create a new instance of the script.

**AreaTrigger_at_cathedral_entrance**: Triggers the Ashbringer event when a player with the Ashbringer aura enters the Cathedral entrance.

**AddSC_instance_scarlet_monastery**: Registers the instance script and area trigger script with the script manager.

---

<!-- machine-true, projected from graph.json -->

## Map — instance_scarlet_monastery

*Source:* instance_scarlet_monastery.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| instance_scarlet_monastery | ctor | ScriptedInstance/ScriptedInstance | — | — |
| Initialize | method | EventMap/Reset | — | — |
| OnCreatureCreate | method | Object/GetEntry, Object/GetGUID | — | — |
| OnObjectCreate | method | Object/GetEntry, Object/GetGUID | — | — |
| OnCreatureDeath | method | Object/GetEntry, ObjectGuid/ObjectGuid#5, Unit.Main/IsDead, ZoneScript/GetCreature | — | — |
| GetData64 | method | — | — | — |
| IsMograineOrWhitemaneDead | method | ObjectGuid/ObjectGuid#5, Unit.Main/IsDead, ZoneScript/GetCreature | — | — |
| SetData | method | Cell/Cell#2, Creature.Main/AI, Creature.Main/DespawnOrUnsummon, Creature.Main/Respawn, Creature.Main/SetDeathState, Creature.Main/SetInCombatWithZone, Creature.MotionMaster/MovePoint, CreatureAI/AttackStart, GameObject/SetGoState, GameObject/SetLootState, GridDefines/ComputeCellPair, GridSearchers/GetCreatureListWithEntryInGrid#2, InstanceData/SaveToDB, Log.Main/Out, Map.Main/GetCreature, Map.Main/GetGameObject, Map.Main/GetId, Map.Main/GetInstanceId, Map.Main/GetMapName, Map.Main/LoadGrid, ObjectGuid/ObjectGuid#5, ScriptMgr/DoScriptText, Unit.Main/GetMotionMaster, Unit.Main/GetVictim, Unit.Main/IsAlive, Unit.Main/IsInCombat, Unit.Main/SetFactionTemplateId, WorldObject.Object/AddObjectToRemoveList, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SetFlag, ZoneScript/GetCreature, ZoneScript/GetGameObject | — | — |
| GetData | method | — | — | — |
| Load | method | Log.Main/Out, Map.Main/GetId, Map.Main/GetInstanceId, Map.Main/GetMapName | — | — |
| Save | method | — | — | — |
| OnCreatureSpellHit | method | EventMap/ScheduleEvent, EventMap/ScheduleEvent#2, Object/GetEntry, Object/GetGUID, Object/IsPlayer, Unit.Main/IsDead, Unit.Main/SetFacingToObject | — | — |
| Update | method | Creature.Main/SetFactionTemporary, Creature.Main/SetVirtualItem, Creature.MotionMaster/MovePoint, EventMap/ExecuteEvent, EventMap/ScheduleEvent#2, EventMap/Update, GridSearchers/GetClosestCreatureWithEntry, Map.Main/GetPlayer, ObjectGuid/ObjectGuid#5, ScriptMgr/DoScriptText, SpellCaster/CastSpell#2, TemporarySummon/UnSummon, Unit.Main/GetMotionMaster, Unit.Main/HandleEmote, Unit.Main/HandleEmoteCommand, Unit.Main/SetDisplayId, Unit.Main/SetFacingToObject, Unit.Main/SetSheath, Unit.Main/SetStandState, Unit.Main/StopMoving, WorldObject.Object/SetObjectScale, WorldObject.Object/SummonCreature#2, ZoneScript/GetCreature | — | — |
| GetInstanceData_instance_scarlet_monastery | function | — | — | — |
| AreaTrigger_at_cathedral_entrance | function | InstanceData/GetData, InstanceData/GetData64, InstanceData/SetData, ObjectGuid/ObjectGuid#5, ScriptMgr/DoScriptText, Unit.Main/HasAura#2, Unit.Main/IsAlive, WorldObject.Object/GetInstanceData, ZoneScript/GetCreature | — | — |
| AddSC_instance_scarlet_monastery | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
