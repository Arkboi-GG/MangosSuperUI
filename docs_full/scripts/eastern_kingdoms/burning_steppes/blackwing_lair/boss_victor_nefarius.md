# boss_victor_nefarius

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_victor_nefarius

**Purpose & Responsibilities**

`boss_victor_nefarius.cpp` implements the artificial intelligence and event logic for **Lord Victor Nefarius**, a boss encounter in the Blackwing Lair dungeon. The unit manages a complex, multi-phase fight that transitions from a standard combat encounter (Phase 1) to a "Scepter Run" event (Phase 2), culminating in the summoning of the final boss, Nefarian.

Key responsibilities include:
1.  **Phase 1 Combat:** Managing timed spells (Shadow Bolt, Fear, Silence, Mind Control) and periodic spawning of Drakanoid adds.
2.  **Phase Transition:** Detecting when enough adds have been killed to trigger the transition to Phase 2, during which Victor Nefarius becomes invisible and summons Nefarian.
3.  **Scepter Run Event:** Coordinating with the instance data to manage a timed "Scepter Run" quest objective. This involves tracking remaining time, playing sequential taunt lines, and determining success or failure based on whether a specific player completes the associated quest before time expires.
4.  **Cleanup & Reset:** Handling the despawning of Victor Nefarius upon Nefarian's death or the failure of the encounter, ensuring game objects (bones) are cleaned up, and resetting faction/visibility states.

The code explicitly notes that instead of morphing the creature model, the script controls Victor Nefarius in Phase 1 and spawns a separate "Nefarian" creature for Phase 2. Victor Nefarius remains in the world (invisible) to monitor the Scepter Run status and despawn himself when appropriate.

## Member-by-Member Behavior

### Initialization and State Management

**`boss_victor_nefariusAI` (Constructor)**
Initializes the AI object. It retrieves the `ScriptedInstance` pointer to access shared dungeon state. It determines the types of Drakanoid adds to spawn (`m_uiDrakeTypeOne`, `m_uiDrakeTypeTwo`) based on data stored in the instance (`DATA_NEF_COLOR`). It ensures the two selected drake types are distinct. Finally, it calls `Reset()` to initialize timers and flags.

**`Reset`**
Resets all internal timers, counters, and boolean flags to their initial values. It sets the creature's faction to friendly, sets its stand state to sitting, and enables the gossip flag so players can interact with it to start the event. It ensures visibility is ON. Crucially, it calls `LoadScepterRun()` to restore any ongoing Scepter Run state from the instance data, allowing the event to persist across creature resets or reloads if necessary.

**`LoadScepterRun`**
Checks the instance data for the current status of the Scepter Run (`TYPE_SCEPTER_RUN`). If the run is in progress, it restores the remaining time (`scepterRunTime`) and calculates the correct index for the next taunt line (`scepterTauntID`) and the time until that taunt (`nextScepterTauntTime`) based on elapsed time. This ensures continuity if the AI object is recreated while the event is active.

**`StartScepterRun`**
Initiates the Scepter Run event. It plays the start sound/text, resets taunt counters, sets the full duration timer, marks the run as active, and updates the instance data to reflect `IN_PROGRESS`.

### Combat and Event Triggers

**`Aggro`**
Triggered when the creature enters combat. It notifies the instance that the Nefarian encounter has begun (`TYPE_NEFARIAN = IN_PROGRESS`) and marks the creature as being in combat with the zone.

**`EnterEvadeMode`**
Delegates to the base `ScriptedAI::EnterEvadeMode`. This is typically called if the creature leaves combat or is forced to evade.

**`JustReachedHome`**
Called when the creature reaches its home position after evading or dying. It resets the faction to friendly, removes the "not selectable" flag, and notifies the instance that the encounter failed (`TYPE_NEFARIAN = FAIL`). It cleans up any `GO_DRAKONID_BONES` game objects within 250 units. It sets a short respawn delay (10 seconds) and forces the creature to disappear and die. This handles the case where players wipe and need to restart the event.

**`JustSummoned`**
Called when an add (Drakanoid) is summoned. It selects a random hostile target for the summoned creature and initiates combat. It sets a long respawn delay (7 days) for the add, effectively making it permanent until despawned by other means.

**`SummonedCreatureJustDied`**
Handles the death of summoned creatures.
*   If the dead creature is **Nefarian** (`NPC_NEFARIAN`): It checks if the Scepter Run was active. If so, it verifies if the designated champion player has completed the associated quest (`QUEST_NEFARIUS_CORRUPTION`). If the quest is incomplete, the run is marked as `DONE`; otherwise, it fails. It updates the instance data with the result, sets a 7-day respawn delay for itself, and forces itself to despawn.
*   If the dead creature is an **add**: It increments the `m_uiKilledAdds` counter, which drives the phase progression.

### Main AI Loop

**`UpdateAI`**
The core update loop, executed every tick. It handles several concurrent processes:

1.  **Event Start Sequence:** If `NefaEventStart` is true and Phase 1 hasn't started, it runs a sequence of timed events:
    *   Plays introductory speech lines.
    *   Casts `SPELL_NEFARIUS_BARRIER` (immunity).
    *   Removes player immunity flags.
    *   Sets faction to monster.
    *   Adds threat to all alive players.
    *   Roots itself.

2.  **Scepter Run Monitoring:** If `watchScepterRun` is true:
    *   If the run is active, it delegates to `HandleScepterRun`.
    *   If the run is not active, it checks instance data. If the status is `SPECIAL`, it starts the run. If `FAIL`, it stops watching.

3.  **Phase Progression:**
    *   If `m_uiKilledAdds` reaches 42, it sets `phase2bis` to true, stopping further add spawns.
    *   If `m_uiKilledAdds` reaches 40, it triggers Phase 2:
        *   Interrupts current spells.
        *   Roots itself.
        *   Becomes invisible (`VISIBILITY_OFF`).
        *   Summons **Nefarian** at a specific location, making him hover and fly.
        *   Sets `phase2` to true and returns early, skipping further combat logic for Victor Nefarius.

4.  **Add Spawning (Phase 1):**
    *   Periodically spawns two Drakanoids of the pre-selected types.
    *   Periodically spawns two Chromatic Drakanoids.

5.  **Spell Casting (Phase 1):**
    *   **Shadow Bolt:** Random target, random cooldown.
    *   **Shadow Bolt Volley:** Victim, fixed cooldown.
    *   **Fear:** Random target, random cooldown.
    *   **Silence:** Random target, random cooldown.
    *   **Shadow Blink:** A two-step process. First, plays a visual effect. Then, teleports near a random target, attacks them, roots itself, and resets timers.
    *   **Mind Control:** Attempts to charm a random player. If successful, it stores the player's GUID and current threat. If the charm breaks (aura lost), it restores the threat to prevent instant aggro reset issues, then clears the stored data.

### Scepter Run Logic

**`HandleScepterRun`**
Manages the active Scepter Run timer.
*   Decrements the taunt timer. If expired, it plays the next taunt line from the sequence and resets the taunt timer.
*   Decrements the main run timer. If expired, it calls `FailScepterRun`.
*   Updates the instance data with the remaining run time, ensuring persistence.

**`FailScepterRun`**
Marks the Scepter Run as failed in the instance data, stops watching the run, and plays failure sounds/text.

### Registration and Integration

**`GetAI_boss_victor_nefarius`**
Factory function for the AI. It checks the map ID.
*   If in **Blackwing Lair** (`MAP_BLACKWING_LAIR`), it returns a new `boss_victor_nefariusAI`.
*   Otherwise (e.g., UBRS), it sets the creature immune to players and returns a `NullCreatureAI`. This allows the same creature entry to exist in multiple dungeons with different behaviors.

**`NefariusGossipOptionClicked`**
A helper function called when a player interacts with the creature via gossip. It casts the creature's AI to `boss_victor_nefariusAI` and sets the `NefaEventStart` flag to true, triggering the event start sequence in `UpdateAI`.

**`AddSC_boss_victor_nefarius`**
Registers the script with the engine, linking the name "boss_victor_nefarius" to the `GetAI_boss_victor_nefarius` factory function.

## Cross-Unit Boundaries

*   **InstanceData (`instance_blackwing_lair`):**
    *   **Calls Out:** `GetData`, `GetData64`, `SetData`. Used to read/write the state of the Nefarian encounter, Scepter Run status, remaining time, and champion player GUID. This is the primary synchronization mechanism between Victor Nefarius, Nefarian, and the dungeon environment.
    *   **Called By:** None listed in the map, but logically, other scripts (like Nefarian's) would call into this instance data to coordinate.

*   **ScriptedAI:**
    *   **Calls Out:** `ScriptedAI` (base constructor), `EnterEvadeMode`. Standard inheritance usage.

*   **WorldObject / Creature / Unit:**
    *   **Calls Out:** Extensive use of `SetFactionTemplateId`, `SetVisibility`, `SetFlag`, `RemoveFlag`, `SummonCreature`, `GetGameObjectListWithEntryInGrid`, `SelectAttackingTarget`, `SetInCombatWithZone`, `DisappearAndDie`, `ForcedDespawn`, `SetRespawnDelay`, `CastSpell`, `InterruptNonMeleeSpells`, `NearTeleportTo`, `SendSpellGo`, `AddThreat`, `GetThreatManager`, `HasAura`, `IsAlive`, `GetPositionX/Y/Z`, `GetOrientation`, `GetMap`, `GetPlayer`, `GetObjectGuid`. These are standard engine interactions for movement, combat, spell casting, and state management.

*   **ScriptMgr:**
    *   **Calls Out:** `DoScriptText`. Used to play sound and text emotes associated with the event phases and taunts.

*   **Map:**
    *   **Calls Out:** `GetPlayer`, `GetPlayers`. Used to retrieve player objects for threat manipulation and quest status checking.

*   **ThreatManager:**
    *   **Calls Out:** `addThreatDirectly`, `getThreat`, `modifyThreatPercent`. Used to manipulate threat levels, particularly when mind control breaks or when initiating combat.

*   **SpellCaster:**
    *   **Calls Out:** `CastSpell`, `InterruptNonMeleeSpells`. Used for spell execution.

*   **shared_Util:**
    *   **Calls Out:** `urand`. Used for random number generation in timers and target selection.

*   **ObjectGuid:**
    *   **Calls Out:** `Clear`. Used to reset stored GUIDs.

*   **ScriptLoader:**
    *   **Called By:** `AddScripts`. The engine calls `AddSC_boss_victor_nefarius` during startup to register the script.

## Data Model

This unit does not directly query or modify database tables. It interacts exclusively with runtime memory structures via the `InstanceData` interface (`instance_blackwing_lair`). The `InstanceData` class likely persists some of this state to the database upon save/load, but the column definitions and table structures are not exposed in this unit's source code. The unit relies on constants like `TYPE_NEFARIAN`, `TYPE_SCEPTER_RUN`, `DATA_SCEPTER_RUN_TIME`, `DATA_SCEPTER_CHAMPION`, and `DATA_NEF_COLOR` which are defined in the shared `blackwing_lair.h` header (not provided here, but referenced).

## Notable Implementation Details

*   **Two-Creature Phase 2:** Instead of morphing Victor Nefarius into Nefarian, the script spawns a separate Nefarian creature. Victor Nefarius becomes invisible and remains in the background to monitor the Scepter Run. This design choice simplifies model changes but requires careful coordination via `InstanceData` to ensure Victor despawns correctly when Nefarian dies.
*   **Scepter Run Persistence:** The `LoadScepterRun` method allows the Scepter Run timer and taunt state to survive a potential recreation of the AI object (e.g., due to a server reload or creature reset). It reconstructs the state from the instance data, calculating the correct taunt index based on elapsed time.
*   **Mind Control Threat Handling:** When a player is mind-controlled (`SPELL_SHADOW_COMMAND`), the script stores their current threat value. If the aura breaks, it manually restores the threat using `addThreatDirectly` and `modifyThreatPercent` to prevent the player from having zero threat and potentially being ignored by other enemies or causing aggro issues.
*   **Shadow Blink Mechanic:** The "Shadow Blink" ability is implemented as a two-tick process. The first tick plays a visual effect and sets a flag (`Smoke = true`). The second tick performs the teleport and attack. This ensures the visual effect precedes the movement.
*   **Hardcoded Spawn Locations:** The spawn locations for adds and Nefarian are hardcoded in the `aNefarianLocs` array. One location is commented as "hide pos (useless; remove this)".
*   **Respawn Delay Hack:** In `JustReachedHome`, the respawn delay is set to 10 seconds, overriding a commented-out 15-minute delay. A comment indicates a "reset bug" was encountered with the longer delay.
*   **UBRS Compatibility:** The `GetAI_boss_victor_nefarius` function checks the map ID. If the creature is not in Blackwing Lair, it assigns a `NullCreatureAI` and makes the creature immune to players. This suggests the creature entry exists in other dungeons (like UBRS) but should not be interactive there.
*   **Gossip Trigger:** The event start is triggered by a gossip interaction, handled by `NefariusGossipOptionClicked`. This function is likely registered elsewhere in the codebase (e.g., in a gossip handler script) to be called when the specific gossip option is clicked.

## Member Reference

**`boss_victor_nefariusAI`**
Constructor. Initializes the AI, retrieves instance data, selects drake types for spawning, and calls `Reset()`.

**`Reset`**
Resets all timers, flags, and creature state (faction, visibility, gossip flag). Calls `LoadScepterRun()` to restore any ongoing event state.

**`LoadScepterRun`**
Restores Scepter Run state (time remaining, taunt index) from instance data if the run is in progress.

**`StartScepterRun`**
Initiates the Scepter Run event, setting timers and updating instance data.

**`Aggro`**
Notifies instance that the encounter has started and marks the creature as in combat.

**`EnterEvadeMode`**
Delegates to base class `EnterEvadeMode`.

**`JustReachedHome`**
Handles encounter failure/reset. Cleans up game objects, resets faction/flags, sets short respawn delay, and despawns the creature.

**`JustSummoned`**
Initiates combat for summoned adds and sets a long respawn delay.

**`SummonedCreatureJustDied`**
Handles add deaths. If Nefarian dies, it checks Scepter Run success/failure, updates instance data, and despawns Victor Nefarius. If an add dies, it increments the kill counter.

**`UpdateAI`**
Main update loop. Handles event start sequence, Scepter Run monitoring, phase progression (spawning Nefarian at 40 kills), add spawning, and spell casting (Shadow Bolt, Fear, Silence, Mind Control, Shadow Blink).

**`HandleScepterRun`**
Manages the active Scepter Run timer, playing taunts and checking for expiration.

**`FailScepterRun`**
Marks the Scepter Run as failed in instance data and plays failure sounds.

**`GetAI_boss_victor_nefarius`**
Factory function. Returns `boss_victor_nefariusAI` for Blackwing Lair, or `NullCreatureAI` (with immunity) for other maps.

**`NefariusGossipOptionClicked`**
Helper function to set the `NefaEventStart` flag when a player interacts with the creature via gossip.

**`AddSC_boss_victor_nefarius`**
Registers the script with the engine.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_victor_nefarius

*Source:* boss_victor_nefarius.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_victor_nefariusAI | ctor | InstanceData/GetData64, ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | ObjectGuid/Clear, shared_Util/urand, Unit.Main/GetVisibility, Unit.Main/SetFactionTemplateId, Unit.Main/SetStandState, Unit.Main/SetVisibility, WorldObject.Object/SetFlag | — | — |
| LoadScepterRun | method | InstanceData/GetData, InstanceData/GetData64 | — | — |
| StartScepterRun | method | InstanceData/SetData, ScriptMgr/DoScriptText | — | — |
| Aggro | method | Creature.Main/SetInCombatWithZone, InstanceData/SetData | — | — |
| EnterEvadeMode | method | ScriptedAI/EnterEvadeMode | — | — |
| JustReachedHome | method | Creature.Main/DisappearAndDie, Creature.Main/SetRespawnDelay, InstanceData/SetData, Unit.Main/SetFactionTemplateId, WorldObject.Object/DeleteLater, WorldObject.Object/GetGameObjectListWithEntryInGrid, WorldObject.Object/RemoveFlag | — | — |
| JustSummoned | method | Creature.Main/AI, Creature.Main/SelectAttackingTarget, Creature.Main/SetInCombatWithZone, Creature.Main/SetRespawnDelay, CreatureAI/AttackStart | — | — |
| SummonedCreatureJustDied | method | Creature.Main/ForcedDespawn, Creature.Main/SetRespawnDelay, InstanceData/GetData64, InstanceData/SetData, Map.Main/GetPlayer, Object/GetEntry, ObjectGuid/ObjectGuid#5, Player.Main/GetQuestStatus, WorldObject.Object/GetMap | — | — |
| UpdateAI | method | Creature.Main/SelectAttackingTarget, Creature.Main/SetInCombatWithZone, CreatureAI/AttackStart, CreatureAI/DoCastSpellIfCan, InstanceData/GetData, Map.Main/GetPlayer, Map.Main/GetPlayers, Object/GetObjectGuid, ObjectGuid/Clear, ScriptMgr/DoScriptText, shared_Util/urand, SpellCaster/CastSpell#2, SpellCaster/InterruptNonMeleeSpells, ThreatManager/addThreatDirectly, ThreatManager/getThreat, ThreatManager/modifyThreatPercent#2, Unit.Main/AddThreat, Unit.Main/GetThreatManager, Unit.Main/GetVictim, Unit.Main/GetVisibility, Unit.Main/HasAura#2, Unit.Main/IsAlive, Unit.Main/NearTeleportTo, Unit.Main/SelectHostileTarget, Unit.Main/SendSpellGo, Unit.Main/SetFactionTemplateId, Unit.Main/SetFly, Unit.Main/SetVisibility, WorldObject.Object/GetMap, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/RemoveFlag, WorldObject.Object/SummonCreature#2 | — | — |
| HandleScepterRun | method | InstanceData/SetData, ScriptMgr/DoScriptText | — | — |
| FailScepterRun | method | InstanceData/SetData, ScriptMgr/DoScriptText | — | — |
| GetAI_boss_victor_nefarius | function | NullCreatureAI/NullCreatureAI, WorldObject.Object/GetMapId, WorldObject.Object/SetFlag | — | — |
| NefariusGossipOptionClicked | function | Creature.Main/AI | instance_blackwing_lair/SetData | — |
| AddSC_boss_victor_nefarius | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
