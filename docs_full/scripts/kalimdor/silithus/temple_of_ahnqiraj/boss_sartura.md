# boss_sartura

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_sartura

## Purpose & Responsibilities

The `boss_sartura` translation unit implements the artificial intelligence and encounter logic for three distinct creature types within the **Temple of Ahn'Qiraj** raid instance:

1.  **Battleguard Sartura (`boss_sarturaAI`)**: The primary boss of this encounter. Her mechanics revolve around a high-threat management cycle involving a damaging area-of-effect spell (**Whirlwind**) that forces her to switch targets rapidly, a cleave attack, and two stages of enrage (one health-based, one time-based). She also has a leash mechanism that resets the encounter if she moves too far from her spawn point.
2.  **Sartura's Royal Guard (`mob_sartura_royal_guardAI`)**: Elite adds that accompany Sartura. They mirror some of her mechanics, including their own Whirlwind and a Knockback ability. Crucially, they share a leash mechanic with Sartura; if a guard leashes, it triggers a reset of the entire encounter, including Sartura herself.
3.  **Vekniss Guardian (`mob_vekniss_guardianAI`)**: Adds that provide assistance during the fight. Their primary mechanic is **Impale**, a charge attack triggered when they drop below 25% health. If multiple guardians are nearby, they assist the low-health guardian by charging toward it. They also enter a **Frenzy** state below 30% health.

This unit handles all combat timers, spell casting, threat manipulation, movement checks, and instance data synchronization for these creatures. It does not interact with any database tables directly; all data is managed via the in-memory `instance_temple_of_ahnqiraj` script instance.

## Member-by-Member Behavior

### Battleguard Sartura (`boss_sarturaAI`)

#### Initialization and State Management
*   **`boss_sarturaAI` (ctor)**: Initializes the AI by retrieving the instance data (`instance_temple_of_ahnqiraj`) and calling `Reset()` to set initial timer values.
*   **`Reset`**: Resets all combat timers (`m_uiCleaveTimer`, `m_uiWhirlWindTimer`, etc.) to random or fixed starting values. It clears the enraged state flags (`m_bIsEnraged`, `m_bAttackOff`).
*   **`JustReachedHome`**: Called when the creature returns to its spawn point after evading. It updates the instance data to mark the encounter as failed (`FAIL`).

#### Combat Engagement and Disengagement
*   **`MoveInLineOfSight`**: Overrides the default aggro range. Sartura has a large aggro radius (85 yards). If a player is within line of sight, distance, and not feigning death, she initiates combat.
*   **`Aggro`**: Triggered when combat starts. Plays an aggro sound/text, marks the zone as in combat, and sets the instance data to `IN_PROGRESS`.
*   **`EnterEvadeMode`**: Called when the creature evades (e.g., due to leash or death). It calls `LeashEncounter()` to reset associated guards before calling the base class evade logic.
*   **`LeashEncounter`**: Retrieves the list of Royal Guard GUIDs from the instance. For each guard, it checks if they are dead (respawns them) or alive (forces them to evade/reset). This ensures the entire pack resets together.
*   **`JustDied`**: Plays death text and updates the instance data to `DONE`.
*   **`KilledUnit`**: Plays a kill text when a player dies.

#### Core Combat Mechanics
*   **`AssignRandomThreat`**: A helper method used to manipulate threat. It selects a random hostile target within visible range, resets all threat, and adds a large amount of direct threat (1000–2000) to that specific target. This forces Sartura to focus on a new target, simulating the chaotic nature of her Whirlwind.
*   **`UpdateAI`**: The main update loop.
    *   **Whirlwind Phase**: If `m_uiWhirlWindEndTimer` is active, Sartura is in Whirlwind. She frequently calls `AssignRandomThreat` to switch targets. When the timer expires, she stops Whirlwind, restores her normal attack speed (removing the haste modifier applied during Whirlwind), and resets timers.
    *   **Normal Phase**: If not in Whirlwind, she casts **Whirlwind** on a timer. She also casts **Sundering Cleave** on her current victim. Periodically, she calls `AssignRandomThreat` to vary her target selection.
    *   **Enrage**: If health drops below 20%, she casts **Enrage**. If the fight lasts 10 minutes, she casts **Hard Enrage**. These spells are cast even during Whirlwind (using `CF_TRIGGERED` flag to bypass normal checks).
    *   **Leash Check**: Every 2.5 seconds, it checks if Sartura's Y-coordinate exceeds 1780. If so, she evades and resets the encounter.

### Sartura's Royal Guard (`mob_sartura_royal_guardAI`)

#### Initialization and State Management
*   **`mob_sartura_royal_guardAI` (ctor)**: Initializes the AI and retrieves instance data.
*   **`Reset`**: Resets timers for Knockback, Whirlwind, and threat management.
*   **`Aggro`**: Marks the zone as in combat.

#### Core Combat Mechanics
*   **`AssignRandomThreat`**: Similar to Sartura's version, it resets threat and focuses on a random target. Note: Unlike Sartura's version, this does not check `IsWithinDist` before adding threat, potentially targeting players outside immediate melee range if they are in the threat list.
*   **`LeashEncounter`**: This is critical. If a guard leashes, it first checks if Sartura is alive. If so, it forces **Sartura** to evade. Then, it resets all other Royal Guards. This creates a chain reaction where any guard leaving the arena resets the whole boss fight.
*   **`UpdateAI`**:
    *   **Whirlwind**: Casts **Guard Whirlwind** on a timer. During Whirlwind, it switches targets randomly.
    *   **Knockback**: Casts **Knockback** on the current victim if in melee range.
    *   **Leash Check**: Checks Y-coordinate > 1780. If exceeded, calls `LeashEncounter()`.

### Vekniss Guardian (`mob_vekniss_guardianAI`)

#### Initialization and State Management
*   **`mob_vekniss_guardianAI` (ctor)**: Initializes the AI.
*   **`Reset`**: Resets flags for help calls, frenzy, and timers.
*   **`Aggro`**: Checks if the creature's GUID is in the static list `aEmoteGUIDs`. If so, it schedules an emote to play shortly after aggro.

#### Core Combat Mechanics
*   **`MoveInLineOfSight`**: Sets aggro radius to 50 yards.
*   **`EnterEvadeMode`**: Restores normal run speed before evading.
*   **`ImpaleAssist`**: Called by other guardians. It sets the guardian's run speed to 2.5x, moves it to the position of the requesting guardian (`pWho`), and plays a charge sound.
*   **`MovementInform`**: Triggered when the move point finishes. It casts **Impale** on the guardian itself (likely hitting the target it was moving towards or standing near), restores normal speed, and resumes movement toward the victim.
*   **`DamageTaken`**:
    *   If health drops below 25% and help hasn't been called yet:
        *   It searches for other Vekniss Guardians within 45 yards.
        *   For each alive, line-of-sight guardian, it calls `ImpaleAssist` on that guardian, passing itself as the target. This causes allies to charge toward the low-health guardian.
        *   If no allies are found (`m_bIsAlone` remains true), it casts **Impale** on itself immediately.
*   **`UpdateAI`**:
    *   **Evade Check**: If the creature is in evade mode, it relocates to its last victim's position (likely a cleanup step).
    *   **Emote**: Plays the scheduled aggro emote if applicable.
    *   **Frenzy**: If health drops below 30%, it casts **Frenzy** and plays an emote.
    *   **Impale Timer**: If `m_uiImpaleTimer` is set (though it is never set in the provided code paths, likely reserved for self-cast delays), it waits before resuming normal attacks.

### Script Registration

*   **`GetAI_boss_sartura`**, **`GetAI_mob_sartura_royal_guard`**, **`GetAI_mob_vekniss_guardian`**: Factory functions that instantiate the respective AI classes.
*   **`AddSC_boss_sartura`**: Registers the scripts with the engine. It creates `Script` objects for each creature type, assigns the appropriate `GetAI` function, and registers them with the `ScriptMgr`.

## Cross-Unit Boundaries

### Collaboration with `instance_temple_of_ahnqiraj`
*   **Direction**: `boss_sartura` -> `instance_temple_of_ahnqiraj`
*   **Why**: To synchronize encounter state.
*   **Details**:
    *   `boss_sarturaAI::Aggro` calls `SetData(TYPE_SARTURA, IN_PROGRESS)` to inform the instance script that the boss fight has begun.
    *   `boss_sarturaAI::JustDied` calls `SetData(TYPE_SARTURA, DONE)` to signal victory.
    *   `boss_sarturaAI::JustReachedHome` calls `SetData(TYPE_SARTURA, FAIL)` to signal failure.
    *   `boss_sarturaAI::LeashEncounter` and `mob_sartura_royal_guardAI::LeashEncounter` call `GetRoyalGuardGUIDList` to retrieve the list of guard GUIDs associated with this encounter.
    *   `mob_sartura_royal_guardAI::LeashEncounter` calls `GetSingleCreatureFromStorage(NPC_BATTLEGUARD_SARTURA)` to find Sartura's object pointer to force her evasion.

### Collaboration with `ScriptMgr`
*   **Direction**: `boss_sartura` -> `ScriptMgr`
*   **Why**: To play sounds and text emotes.
*   **Details**: All AI classes call `DoScriptText` to trigger predefined speech strings or emotes (e.g., `SAY_AGGRO`, `EMOTE_ENRAGE`).

### Collaboration with Base AI Classes (`ScriptedAI`, `CreatureAI`, `BasicAI`)
*   **Direction**: `boss_sartura` -> Base Classes
*   **Why**: To leverage standard AI behaviors.
*   **Details**:
    *   `MoveInLineOfSight` calls `BasicAI::MoveInLineOfSight` and `CreatureAI::AttackStart`.
    *   `EnterEvadeMode` calls `ScriptedAI::EnterEvadeMode`.
    *   `UpdateAI` calls `CreatureAI::DoCastSpellIfCan` and `CreatureAI::DoMeleeAttackIfReady`.
    *   `mob_vekniss_guardianAI::MovementInform` calls `ScriptedAI::DoStartMovement`.

### Collaboration with `Unit` and `WorldObject`
*   **Direction**: `boss_sartura` -> `Unit`/`WorldObject`
*   **Why**: To access creature state, position, and threat management.
*   **Details**:
    *   Uses `GetHealthPercent`, `IsInCombat`, `IsWithinDistInMap`, `IsWithinLOSInMap` for condition checks.
    *   Uses `GetThreatManager().addThreatDirectly` for threat manipulation.
    *   Uses `GetPositionY` for leash checks.
    *   Uses `ApplyAttackTimePercentMod` and `SetAttackTimer` to modify attack speeds during Whirlwind.
    *   Uses `GetMotionMaster()->MovePoint` and `UpdateSpeed` for Vekniss Guardian charges.

## Data Model

This unit does not interact with any database tables. All encounter state is managed in memory via the `instance_temple_of_ahnqiraj` script instance.

## Notable Implementation Details

### Threat Manipulation via `AssignRandomThreat`
Both `boss_sarturaAI` and `mob_sartura_royal_guardAI` implement a custom threat system. Instead of relying solely on damage-based threat, they periodically call `AssignRandomThreat`. This method:
1.  Selects a random target from the threat list.
2.  Calls `DoResetThreat()` (from `ScriptedAI`) to clear all existing threat.
3.  Adds a fixed amount of threat (1000–2000) directly to the selected target.
This effectively forces the creature to attack a random player, creating a "chaotic" aggro pattern typical of whirlwind mechanics. **Note**: `mob_sartura_royal_guardAI::AssignRandomThreat` lacks the `IsWithinDist` check present in `boss_sarturaAI::AssignRandomThreat`, meaning it might assign threat to players who are out of melee range, potentially causing the guard to run across the map to engage them.

### Whirlwind Attack Speed Modification
In `boss_sarturaAI::UpdateAI`, when entering Whirlwind, the code does not explicitly slow down attacks. However, when exiting Whirlwind, it calls:
```cpp
m_creature->ApplyAttackTimePercentMod(BASE_ATTACK, 0, true);
m_creature->SetAttackTimer(BASE_ATTACK, 100);
```
This suggests that during Whirlwind, an attack time modifier was likely applied elsewhere (possibly by the spell `SPELL_WHIRLWIND` itself or in a missing code segment) to increase attack speed (haste). The exit logic removes this modifier and resets the timer to 100% (normal speed). If the modifier isn't applied on entry, this code simply resets the timer to normal, which is safe but potentially redundant.

### Leash Chain Reaction
The leash mechanic is tightly coupled between Sartura and her guards.
*   If **Sartura** leashes, `LeashEncounter` resets all guards.
*   If a **Guard** leashes, `LeashEncounter` first forces **Sartura** to evade, then resets all other guards.
This ensures that the encounter cannot partially complete; if any participant leaves the arena, the entire group resets.

### Vekniss Guardian Assist Logic
`mob_vekniss_guardianAI::DamageTaken` implements a "call for help" mechanic. When a guardian drops below 25% HP:
1.  It finds all other Vekniss Guardians within 45 yards.
2.  It calls `ImpaleAssist` on each valid ally, passing itself as the target.
3.  `ImpaleAssist` makes the ally charge to the low-health guardian's position.
4.  Upon arrival (`MovementInform`), the ally casts `SPELL_IMPALE`.
This creates a visual effect of allies rushing to aid a struggling guardian, potentially dealing damage to players near the low-health guardian.

### Hardcoded Coordinates
The leash check uses a hardcoded Y-coordinate value: `1780`.
```cpp
if (m_creature->GetPositionY() > 1780)
```
This implies the encounter arena is bounded by this coordinate. Any creature moving beyond this point will trigger a reset. This is fragile if the map geometry changes, but standard for static raid encounters.

### Static Emote List
`mob_vekniss_guardianAI` uses a static array `aEmoteGUIDs` to determine which guardians should emote on aggro. This is a hardcoded list of 8 GUIDs. Only guardians with these specific GUIDs will play the aggro emote.

## Member Reference

**boss_sarturaAI**
Constructor for the Battleguard Sartura AI. Initializes instance data and calls `Reset`.

**Reset**
Resets all timers and state flags for Sartura. Sets initial values for Cleave, Whirlwind, Aggro Reset, Enrage, and Evade Check timers.

**MoveInLineOfSight**
Checks if a player is within 85 yards and line of sight. If so, and not feigning death, initiates combat. Calls base class implementation.

**EnterEvadeMode**
Calls `LeashEncounter` to reset guards, then calls base class evade logic.

**Aggro**
Plays aggro text, sets zone combat flag, and updates instance data to `IN_PROGRESS`.

**KilledUnit**
Plays kill text when a player dies.

**JustDied**
Plays death text and updates instance data to `DONE`.

**JustReachedHome**
Updates instance data to `FAIL` when Sartura returns to spawn.

**AssignRandomThreat**
Selects a random target within visible range, resets all threat, and adds direct threat to the selected target. Used to simulate chaotic aggro during Whirlwind.

**LeashEncounter**
Retrieves Royal Guard GUIDs from instance. Respawn dead guards, force evade alive guards. Ensures entire pack resets together.

**UpdateAI**
Main update loop. Manages Whirlwind phase (target switching, timer expiration), Normal phase (casting Whirlwind, Cleave, random threat), Enrage (health/time based), and Leash check (Y > 1780).

**mob_sartura_royal_guardAI**
Constructor for the Royal Guard AI. Initializes instance data and calls `Reset`.

**Reset#2**
Resets timers for Knockback, Whirlwind, Aggro Reset, and Evade Check.

**Aggro#2**
Sets zone combat flag.

**AssignRandomThreat#2**
Selects a random target, resets threat, and adds direct threat. Does not check distance, unlike Sartura's version.

**LeashEncounter#2**
If Sartura is alive, forces her to evade. Then resets all other Royal Guards. Creates a chain-reset effect.

**UpdateAI#2**
Manages Whirlwind phase, Knockback casting (if in melee), and Leash check (Y > 1780).

**mob_vekniss_guardianAI**
Constructor for the Vekniss Guardian AI. Initializes instance data and timers.

**Reset#3**
Resets help/frenzy flags and timers.

**Aggro#3**
Checks if creature GUID is in `aEmoteGUIDs`. If so, schedules an emote.

**MoveInLineOfSight#2**
Checks if a player is within 50 yards and line of sight. If so, initiates combat.

**EnterEvadeMode#2**
Restores normal run speed, then calls base class evade logic.

**ImpaleAssist**
Sets run speed to 2.5x, moves to target position, plays charge sound. Used by allies to assist a low-health guardian.

**MovementInform**
Triggered on move completion. Casts `SPELL_IMPALE`, restores speed, resumes movement to victim.

**DamageTaken**
If health < 25% and help not called, finds nearby guardians. Calls `ImpaleAssist` on allies to charge toward self. If alone, casts `SPELL_IMPALE` on self.

**UpdateAI#3**
Manages evade relocation, aggro emote, Frenzy (health < 30%), and Impale timer (unused in current code).

**GetAI_boss_sartura**
Factory function to create `boss_sarturaAI`.

**GetAI_mob_sartura_royal_guard**
Factory function to create `mob_sartura_royal_guardAI`.

**GetAI_mob_vekniss_guardian**
Factory function to create `mob_vekniss_guardianAI`.

**AddSC_boss_sartura**
Registers all three scripts with the engine.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_sartura

*Source:* boss_sartura.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_sarturaAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | shared_Util/urand | — | — |
| MoveInLineOfSight | method | BasicAI/MoveInLineOfSight, CreatureAI/AttackStart, Object/GetTypeId, Unit.Main/HasAuraType, Unit.Main/IsInCombat, WorldObject.Object/IsWithinDistInMap, WorldObject.Object/IsWithinLOSInMap | — | — |
| EnterEvadeMode | method | ScriptedAI/EnterEvadeMode | — | — |
| Aggro | method | Creature.Main/SetInCombatWithZone, instance_temple_of_ahnqiraj/SetData, ScriptMgr/DoScriptText | — | — |
| KilledUnit | method | ScriptMgr/DoScriptText | — | — |
| JustDied | method | instance_temple_of_ahnqiraj/SetData, ScriptMgr/DoScriptText | — | — |
| JustReachedHome | method | instance_temple_of_ahnqiraj/SetData | — | — |
| AssignRandomThreat | method | Creature.Main/SelectAttackingTarget, ScriptedAI/DoResetThreat, shared_Util/urand, ThreatManager/addThreatDirectly, Unit.Main/GetThreatManager, WorldObject.Object/IsWithinDist | — | — |
| LeashEncounter | method | Creature.Main/AI, Creature.Main/Respawn, CreatureAI/EnterEvadeMode, instance_temple_of_ahnqiraj/GetRoyalGuardGUIDList, Map.Main/GetCreature, Unit.Main/IsDead, WorldObject.Object/GetMap | — | — |
| UpdateAI | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, ScriptMgr/DoScriptText, shared_Util/urand, Unit.Main/ApplyAttackTimePercentMod, Unit.Main/GetHealthPercent, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, Unit.Main/SetAttackTimer, WorldObject.Object/GetPositionY | — | — |
| mob_sartura_royal_guardAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset#2 | method | shared_Util/urand | — | — |
| Aggro#2 | method | Creature.Main/SetInCombatWithZone | — | — |
| AssignRandomThreat#2 | method | Creature.Main/SelectAttackingTarget, ScriptedAI/DoResetThreat, shared_Util/urand, ThreatManager/addThreatDirectly, Unit.Main/GetThreatManager | — | — |
| LeashEncounter#2 | method | Creature.Main/AI, Creature.Main/Respawn, CreatureAI/EnterEvadeMode, instance_temple_of_ahnqiraj/GetRoyalGuardGUIDList, Map.Main/GetCreature, ScriptedInstance/GetSingleCreatureFromStorage, Unit.Main/IsAlive, Unit.Main/IsDead, WorldObject.Object/GetMap | — | — |
| UpdateAI#2 | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, WorldObject.Object/GetPositionY | — | — |
| mob_vekniss_guardianAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset#3 | method | — | — | — |
| Aggro#3 | method | Object/GetGUIDLow | — | — |
| MoveInLineOfSight#2 | method | BasicAI/MoveInLineOfSight, CreatureAI/AttackStart, Object/GetTypeId, Unit.Main/HasAuraType, Unit.Main/IsInCombat, WorldObject.Object/IsWithinDistInMap, WorldObject.Object/IsWithinLOSInMap | — | — |
| EnterEvadeMode#2 | method | ScriptedAI/EnterEvadeMode, Unit.Main/UpdateSpeed | — | — |
| ImpaleAssist | method | Creature.MotionMaster/MovePoint, Unit.Main/GetMotionMaster, Unit.Main/UpdateSpeed, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/PlayDistanceSound | — | — |
| MovementInform | method | CreatureAI/DoCastSpellIfCan, ScriptedAI/DoStartMovement, Unit.Main/GetVictim, Unit.Main/UpdateSpeed | — | — |
| DamageTaken | method | Creature.Main/AI, CreatureAI/DoCastSpellIfCan, GridSearchers/GetCreatureListWithEntryInGrid#2, Object/GetObjectGuid, ObjectGuid/operator==, Unit.Main/GetHealthPercent, Unit.Main/IsAlive, WorldObject.Object/IsWithinLOSInMap | — | — |
| UpdateAI#3 | method | Creature.Main/IsInEvadeMode, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, ScriptMgr/DoScriptText, Unit.Main/GetHealthPercent, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/Relocate | — | — |
| GetAI_boss_sartura | function | — | — | — |
| GetAI_mob_sartura_royal_guard | function | — | — | — |
| GetAI_mob_vekniss_guardian | function | — | — | — |
| AddSC_boss_sartura | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
