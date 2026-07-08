# boss_vectus

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_vectus

## Purpose & Responsibilities

`boss_vectus.cpp` implements the artificial intelligence and encounter logic for **Vectus**, a boss in the **Scholomance** dungeon, and his associated adds, the **Scholomance Students**. The primary responsibility of this unit is to manage the "Dawn's Gambit" event sequence, a scripted phase where Vectus transforms neutral students into hostile skeletons and turns a neutral NPC, Marduk Blackpool, into an enemy. It also handles Vectus's standard combat abilities (Flamestrike, Blast Wave) and the specific lifecycle management of the student adds, including their transformation, despawning upon death, and re-application of transformation effects upon respawn or evasion.

The unit operates within the `ScriptedAI` framework, inheriting base behaviors and overriding specific hooks (`UpdateAI`, `MoveInLineOfSight`, `SpellHit`, etc.) to inject custom logic. It relies heavily on grid-based creature searches to identify targets and allies within a 100-yard radius.

## Member-by-Member Behavior

### Vectus AI (`boss_vectusAI`)

This struct manages the boss Vectus. It tracks timers for spells and event phases, and boolean flags to control the progression of the Dawn's Gambit event.

*   **Initialization**: The constructor initializes all timers and flags. It sets `m_bStartedDialogue` to false, indicating the initial dialogue trigger hasn't occurred, and `eventGambitDone` to false, meaning the transformation event hasn't completed. It calls `Reset()` to set initial timer values.
*   **Dialogue Trigger**: The `MoveInLineOfSight` method checks if a player enters Vectus's line of sight within 32 yards. If the dialogue hasn't started, it triggers Vectus's waypoint movement (presumably to position him for the event) and marks the dialogue as started. This ensures the event only begins once players are close enough to witness it.
*   **Dawn's Gambit Event Logic**: The core of the encounter is handled in `UpdateAI`.
    1.  **Phase 1 (Find Gambit)**: If `findGambit` is false, Vectus searches for a GameObject with entry `GO_DAWN_S_GAMBIT` within 100 yards. If found, it sets a 2-second timer and marks `findGambit` as true.
    2.  **Phase 2 (Delete Gambit)**: After the 2-second delay, the GameObject is deleted, and a 12-second timer starts. `eventGambitStart` is marked true.
    3.  **Phase 3 (Transformation)**: After the 12-second delay, the event executes:
        *   **Marduk Blackpool**: Finds the nearest Marduk within 100 yards, sets his faction to Monster (hostile), sets his react state to aggressive, and initializes his AI.
        *   **Students**: Finds all `NPC_STUDENT` creatures within 100 yards. For each:
            *   Casts `SPELL_VIEWING_ROOM_STUDENT_TRANSFORM_EFFECT` to visually transform them into skeletons.
            *   Removes them from any existing `CreatureGroup` to prevent unintended grouping behaviors.
            *   Sets their faction to Monster and react state to aggressive.
            *   Initializes their AI.
        *   **Vectus**: Yells a speech (`VECTUS_SPEECH_GAMBIT_EVENT_START`), sets his own faction to Monster, react state to aggressive, and initializes his AI.
        *   Marks `eventGambitDone` as true, preventing this sequence from running again.
*   **Combat Abilities**: Once in combat (indicated by having a victim), Vectus casts:
    *   **Flamestrike**: Every 30 seconds on himself.
    *   **Blast Wave**: Every 12 seconds on his current victim.
    *   **Melee Attacks**: Standard melee attacks are handled via `DoMeleeAttackIfReady`.
*   **Full Aggro Pull**: When Vectus first gains a hostile target, he pulls all nearby `NPC_STUDENT` creatures into combat by setting their faction to Monster and calling `AttackStart` on their AI with the victim. This ensures all students engage immediately when Vectus is pulled.

### Scholomance Student AI (`npc_scholomance_studentAI`)

This struct manages the individual student adds. They start as neutral but become hostile during the Dawn's Gambit event or if attacked.

*   **Initialization**: The constructor retrieves the instance data (`ScriptedInstance`) to track GUIDs for Vectus and Marduk later. It sets `isTransformed` to false.
*   **Transformation Handling**: The `SpellHit` method listens for `SPELL_VIEWING_ROOM_STUDENT_TRANSFORM_EFFECT`. If hit:
    *   Sets `isTransformed` to true.
    *   **Special Case**: If the student's low GUID is `48949`, it forces a despawn and immediate respawn. This likely resets the student's state or triggers a specific visual effect for this particular student.
*   **Aggro Propagation**: When a student enters combat (`Aggro`), it ensures all nearby students, Marduk, and Vectus are set to the Monster faction. This creates a unified hostile group, ensuring that pulling one student pulls the entire room.
*   **Death Handling**: In `JustDied`:
    *   If the student was transformed (`isTransformed`), it checks if any other `NPC_STUDENT` remains within 100 yards.
    *   **Boss Desync**: If no other students remain, it attempts to desync Vectus and Marduk from their `CreatureGroup`. It retrieves their GUIDs from the instance data, finds the units on the map, and removes them from their shared group. This prevents Vectus and Marduk from being treated as a single unit for threat or targeting purposes after all adds are dead.
    *   Finally, it forces the student to despawn.
*   **Respawn & Evasion**:
    *   `JustRespawned`: If the student was previously transformed, it re-applies the transformation aura.
    *   `EnterEvadeMode`: If players leave combat, the student removes all auras, clears threat, stops combat, and loads its addon. Crucially, if it was transformed, it re-applies the transformation aura before moving home. This preserves the visual state of the student even if combat ends prematurely.

## Cross-Unit Boundaries

*   **`ScriptedAI`**: Both `boss_vectusAI` and `npc_scholomance_studentAI` inherit from `ScriptedAI` (`ScriptedAI/ScriptedAI`). This provides base AI functionality like timer management and basic combat actions.
*   **`Creature`**: Extensive use of `Creature` methods:
    *   `EnableMoveInLosEvent`, `SetDefaultMovementType`, `GetMotionMaster`, `Initialize`: Used by `boss_vectusAI::MoveInLineOfSight` to trigger Vectus's movement.
    *   `FindNearestCreature`, `GetCreatureListWithEntryInGrid`: Used by both AIs to locate targets and allies within a grid range.
    *   `SetFactionTemplateId`, `SetReactState`, `AIM_Initialize`: Used to change creature behavior dynamically (neutral to hostile).
    *   `CastSpell`, `MonsterYell`: Used for spell casting and emotes.
    *   `GetCreatureGroup`, `SetCreatureGroup`: Used to manage grouping logic.
    *   `ForcedDespawn`, `Respawn`: Used by `npc_scholomance_studentAI::SpellHit` and `JustDied` to manage student lifecycle.
*   **`GameObject`**: `boss_vectusAI::UpdateAI` uses `GetClosestGameObjectWithEntry` to find the Dawn's Gambit object and `Delete` to remove it.
*   **`CreatureGroups`**: `boss_vectusAI::UpdateAI` and `npc_scholomance_studentAI::JustDied` use `RemoveMember` to detach creatures from groups.
*   **`InstanceData`**: `npc_scholomance_studentAI::JustDied` uses `GetData64` to retrieve stored GUIDs for Vectus and Marduk from the instance script (`scholomance.h`).
*   **`Map`**: `npc_scholomance_studentAI::JustDied` uses `GetUnit` to fetch Vectus and Marduk from the map using their GUIDs.
*   **`ScriptMgr`**: `AddSC_boss_vectus` registers the scripts with the game world via `ScriptMgr/RegisterSelf`.

## Data Model

This unit does not directly interact with any database tables. All data is managed in-memory through creature states, instance data, and game objects.

## Notable Implementation Details

*   **Hardcoded GUID Check**: In `npc_scholomance_studentAI::SpellHit`, there is a check for `m_creature->GetGUIDLow() == 48949`. This is a hardcoded low GUID, which is generally fragile as GUIDs can change between database resets or server restarts. It suggests a specific student has unique behavior (despawn/respawn) upon transformation.
*   **Event Timing**: The Dawn's Gambit event has fixed timers: 2 seconds to find the object, 12 seconds after deletion to start the transformation. These are hardcoded in `UpdateAI`.
*   **Faction Management**: The unit manually sets factions to `FACTION_MONSTER` (16) and react states to `REACT_AGGRESSIVE` to force hostility. This bypasses standard threat generation for the initial pull.
*   **Group Desyncing**: The logic in `npc_scholomance_studentAI::JustDied` to desync Vectus and Marduk is complex and relies on instance data storing their GUIDs. If the instance data is incorrect or the units are not found, this step fails silently.
*   **Transformation Persistence**: The `EnterEvadeMode` and `JustRespawned` methods ensure that the transformation aura is reapplied if the student was previously transformed. This maintains visual consistency.
*   **Commented Out Code**: There is commented-out code for a Frenzy ability and a Fire Shield spell in `boss_vectusAI::UpdateAI` and the enum. This indicates incomplete or disabled features.
*   **Language Mix**: Comments are in English and French (e.g., `//spell qui transforme les étudiants élites en squelettes`, `//delink les deux boss`). This reflects the development history but doesn't affect functionality.

## Member Reference

*   **boss_vectusAI**: Constructor for the Vectus AI. Initializes timers, flags, and calls `Reset`. Inherits from `ScriptedAI`.
*   **Reset**: Resets Vectus's spell timers and enables move-in-line-of-sight events.
*   **MoveInLineOfSight**: Triggers Vectus's dialogue and waypoint movement when a player enters LOS within 32 yards, if dialogue hasn't started.
*   **UpdateAI**: Main update loop. Handles the Dawn's Gambit event phases (find object, delete object, transform adds/boss), casts Flamestrike and Blast Wave on timers, and pulls all students on initial aggro.
*   **GetAI_boss_vectus**: Factory function to create a new `boss_vectusAI` instance.
*   **npc_scholomance_studentAI**: Constructor for the Student AI. Retrieves instance data and initializes flags. Inherits from `ScriptedAI`.
*   **Reset#2**: Empty reset method for the Student AI.
*   **SpellHit**: Handles the transformation spell. Sets `isTransformed` flag and forces despawn/respawn for a specific hardcoded GUID.
*   **Aggro**: Sets all nearby students, Marduk, and Vectus to hostile faction when a student enters combat.
*   **JustDied**: If transformed, checks if last student died. If so, desyncs Vectus and Marduk from their creature group using instance data. Forces despawn.
*   **JustRespawned**: Reapplies transformation aura if the student was previously transformed.
*   **EnterEvadeMode**: Handles combat evasion. Removes auras/threat, but reapplies transformation aura if applicable before moving home.
*   **GetAI_npc_scholomance_student**: Factory function to create a new `npc_scholomance_studentAI` instance.
*   **AddSC_boss_vectus**: Registers the `boss_vectus` and `npc_scholomance_student` scripts with the game world.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_vectus

*Source:* boss_vectus.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_vectusAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | method | Creature.Main/EnableMoveInLosEvent | — | — |
| MoveInLineOfSight | method | BasicAI/MoveInLineOfSight, Creature.Main/SetDefaultMovementType, Creature.MotionMaster/Initialize, Object/IsPlayer, Unit.Main/GetMotionMaster, WorldObject.Object/IsWithinDistInMap, WorldObject.Object/IsWithinLOSInMap | — | — |
| UpdateAI | method | Creature.Main/AI, Creature.Main/AIM_Initialize, Creature.Main/GetCreatureGroup, Creature.Main/SetCreatureGroup, CreatureAI/AttackStart, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, CreatureGroups/RemoveMember, GameObject/Delete, GridSearchers/GetClosestGameObjectWithEntry, Object/GetGUID, ObjectGuid/ObjectGuid#5, SpellCaster/CastSpell#2, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, Unit.Main/SetFactionTemplateId, Unit.Main/SetReactState, WorldObject.Object/FindNearestCreature, WorldObject.Object/GetCreatureListWithEntryInGrid, WorldObject.Object/MonsterYell#2 | — | — |
| GetAI_boss_vectus | function | — | — | — |
| npc_scholomance_studentAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset#2 | method | — | — | — |
| SpellHit | method | Creature.Main/ForcedDespawn, Creature.Main/Respawn, Object/GetGUIDLow | — | — |
| Aggro | method | Unit.Main/SetFactionTemplateId, WorldObject.Object/FindNearestCreature, WorldObject.Object/GetCreatureListWithEntryInGrid | — | — |
| JustDied | method | Creature.Main/ForcedDespawn, Creature.Main/GetCreatureGroup, Creature.Main/SetCreatureGroup, CreatureGroups/RemoveMember, InstanceData/GetData64, Map.Main/GetUnit, Object/GetGUID, ObjectGuid/ObjectGuid#5, WorldObject.Object/FindNearestCreature, WorldObject.Object/GetMap | — | — |
| JustRespawned | method | Unit.Main/AddAura | — | — |
| EnterEvadeMode | method | Creature.Main/LoadCreatureAddon, Creature.Main/SetLootRecipient, Creature.MotionMaster/MoveTargetedHome, Unit.Main/AddAura, Unit.Main/CombatStop, Unit.Main/DeleteThreatList, Unit.Main/GetMotionMaster, Unit.Main/IsAlive, Unit.Main/RemoveAllAuras | — | — |
| GetAI_npc_scholomance_student | function | — | — | — |
| AddSC_boss_vectus | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
