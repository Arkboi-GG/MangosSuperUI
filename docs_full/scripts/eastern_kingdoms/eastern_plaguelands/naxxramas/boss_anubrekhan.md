# boss_anubrekhan

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_anubrekhan

**Purpose & Responsibilities**
`boss_anubrekhan.cpp` implements the encounter logic for **Anub'Rekhan**, a boss in the Naxxramas raid instance, along with his associated adds (**Crypt Guards**) and the introductory **door** object. The unit defines three distinct AI behaviors:
1.  **`boss_anubrekhanAI`**: Controls the boss's rotation, including casting **Impale** and **Locust Swarm**, managing the spawning of additional Crypt Guards during the fight, and triggering the explosion of dead Crypt Guard corpses into **Corpse Scarabs**.
2.  **`mob_cryptguardsAI`**: Controls the behavior of the Crypt Guard adds, including their enrage mechanic at 50% health, and their rotational spells (**Web**, **Cleave**, **Acid Spit**).
3.  **`anub_doorAI`**: Handles the one-time interaction with the entrance door, triggering Anub'Rekhan's greeting text and disabling further interaction.

The unit relies heavily on the `instance_naxxramas` script instance for data persistence (boss state, creature retrieval) and uses standard `ScriptedAI` and `GameObjectAI` bases. It contains no direct database table interactions; all data is managed in-memory via the instance script and AI state variables.

## Member-by-Member Behavior

### Boss Anub'Rekhan (`boss_anubrekhanAI`)

#### Initialization and Setup
*   **`boss_anubrekhanAI` (ctor)**: Initializes the AI by retrieving the `instance_naxxramas` pointer. It logs an error if the instance data is missing. It immediately calls `CheckSpawnInitialCryptGuards` to spawn the two starting Crypt Guards and then calls `Reset` to initialize timers.
*   **`CheckSpawnInitialCryptGuards`**: Spawns two Crypt Guards (`MOB_CRYPT_GUARD`) at predefined coordinates (`CGs[0]` and `CGs[1]`). It stores their GUIDs in `summonedCryptGuards`. If the boss is already marked as `DONE` in the instance data, it skips spawning.
*   **`IMPALE_CD`**: A helper function returning a random cooldown between 12,000ms and 18,000ms for the Impale ability.
*   **`LOCUST_SWARM_CD`**: A helper function returning a random cooldown for Locust Swarm. It returns 80,000–120,000ms for the initial cast and 90,000–110,000ms for subsequent casts.

#### Combat State Management
*   **`Reset`**: Resets all internal timers (`m_uiImpaleTimer`, `m_uiLocustSwarmTimer`, `m_uiCorpseExplosionTimer`, `m_uiRestoreTargetTimer`) and flags (`m_firstBlood`). It also cleans up any lingering Corpse Scarabs (`MOB_CORPSE_SCARAB`) within 300 yards, removing excess ones if more than 30 exist.
*   **`JustReachedHome`**: Triggered when the boss evades or resets. It sets the instance data to `FAIL`. It iterates through `summonedCryptGuards` and `deadCryptGuards`, unsummoning any existing creatures. Finally, it respawns the initial two Crypt Guards via `CheckSpawnInitialCryptGuards`.
*   **`Aggro`**: Sets the instance data to `IN_PROGRESS`. It forces the boss into combat with the zone and commands all currently summoned Crypt Guards (from `summonedCryptGuards`) to attack the aggroing unit. It plays a random aggro sound.
*   **`JustDied`**: Sets the instance data to `DONE`.
*   **`MoveInLineOfSight`**: Overrides the base behavior to pull players within 55 yards who are not feigning death, provided the boss is not already in combat.

#### Abilities and Mechanics
*   **`UpdateAI`**: The main tick loop.
    *   **Target Restoration**: If `m_uiRestoreTargetTimer` is active, it restores the boss's orientation and target to the current victim after 1 second. This is used after casting Impale to ensure the boss faces the target.
    *   **Impale**: Checks if the boss is not under the effect of Locust Swarm and not currently casting a non-melee spell. If the timer expires, it selects a random target, faces them, sets a 1-second restoration timer, and casts `SPELL_IMPALE`. The timer is paused while Locust Swarm is active or casting.
    *   **Corpse Explosion**: If the timer expires, it calls `ExplodeOneDeadCryptGuard`. If successful, it resets the timer to 20–80 seconds; otherwise, it resets to 10–20 seconds.
    *   **Locust Swarm**: If the timer expires, it restores the target (if pending), casts `SPELL_LOCUSTSWARM` on itself. On success, it resets the timer and spawns a new Crypt Guard at the initial engage position (`CGs[2]`), adding it to `summonedCryptGuards` and commanding it to attack a random target.
    *   **Melee**: Calls `DoMeleeAttackIfReady`.
*   **`ExplodeOneDeadCryptGuard`**: Selects a random GUID from `deadCryptGuards`, removes it from the list, and retrieves the creature. It plays the visual spell `SPELL_SELF_SPAWN_10` on the corpse. It then manually summons 10 Corpse Scarabs (`MOB_CORPSE_SCARAB`) at the corpse's location. Each scarab is put into combat, targets a random player, and adds 5000 threat to that target. Finally, it unsummons the Crypt Guard corpse.
*   **`SummonedCreatureJustDied`**: Called when a summoned creature dies. If the creature is a Crypt Guard, its GUID is added to `deadCryptGuards` for later explosion.
*   **`KilledUnit`**: If the victim is a player, it checks if it's the first kill (`m_firstBlood`). If so, it plays a slay sound and sets the flag. Subsequent kills have a 20% chance (1 in 5) of doing nothing (likely intended for taunts, though currently empty).

### Crypt Guards (`mob_cryptguardsAI`)

#### Initialization
*   **`mob_cryptguardsAI` (ctor)**: Retrieves the instance data and calls `Reset`.
*   **`Reset#2`**: Resets the `isEnraged` flag and initializes timers for Web, Acid Spit, and Cleave.

#### Combat Behavior
*   **`Aggro#2`**: Attempts to pull the boss Anub'Rekhan (`NPC_ANUB_REKHAN`) into combat by calling `AttackStart` on the boss's AI. This ensures the boss joins the fight if a guard is pulled first.
*   **`UpdateAI#2`**:
    *   **Enrage**: If the guard is below 50% health and not yet enraged, it casts `SPELL_CRYPTGUARD_ENRAGE` and sets `isEnraged` to true.
    *   **Web**: If the timer expires, it casts `SPELL_CRYPTGUARD_WEB` and resets threat (`DoResetThreat`).
    *   **Cleave**: If the timer expires, it casts `SPELL_CRYPTGUARD_CLEAVE` on the victim.
    *   **Acid Spit**: If the timer expires, it casts `SPELL_CRYPTGUARD_ACID` on the victim.
    *   **Melee**: Calls `DoMeleeAttackIfReady`.

### Door Interaction (`anub_doorAI`)

#### Initialization
*   **`anub_doorAI` (ctor)**: Retrieves the instance data and logs an error if missing. Initializes `haveDoneIntro` to false.

#### Interaction
*   **`OnUse`**: If the intro has already happened, it returns false. Otherwise, it sets `haveDoneIntro` to true. It retrieves Anub'Rekhan from storage. If alive, it plays a random greeting or taunt sound. It then sets the `GO_FLAG_NO_INTERACT` flag on the door to prevent further use. It always returns `false` (standard for non-consumable interactions in this framework).

## Cross-Unit Boundaries

*   **`instance_naxxramas`**:
    *   **Called by**: `boss_anubrekhanAI` (ctor, `CheckSpawnInitialCryptGuards`, `JustReachedHome`, `Aggro`, `JustDied`, `ExplodeOneDeadCryptGuard`, `UpdateAI`), `mob_cryptguardsAI` (ctor, `Aggro#2`), `anub_doorAI` (ctor, `OnUse`).
    *   **Collaboration**: The AI classes retrieve the `instance_naxxramas` pointer to access instance-wide data. They use `GetData`/`SetData` to track the boss's phase (IN_PROGRESS, DONE, FAIL). They use `GetCreature`/`GetSingleCreatureFromStorage` to locate specific NPCs (Anub'Rekhan, Crypt Guards) by GUID or entry ID. This allows the adds to pull the boss and the boss to manage its adds.
*   **`ScriptedAI` / `GameObjectAI`**:
    *   **Called by**: All AI constructors.
    *   **Collaboration**: Provides the base interface for AI ticks, combat states, and spell casting helpers (`DoCastSpellIfCan`, `DoMeleeAttackIfReady`).
*   **`ScriptMgr`**:
    *   **Called by**: `boss_anubrekhanAI` (`KilledUnit`, `Aggro`), `mob_cryptguardsAI` (`UpdateAI#2`), `anub_doorAI` (`OnUse`).
    *   **Collaboration**: Used to trigger sound/text events (`DoScriptText`) for emotes and speech.
*   **`WorldObject` / `Creature` / `Unit`**:
    *   **Called by**: Various methods for positioning, targeting, summoning, and state checks.
    *   **Collaboration**: Standard engine interactions for movement, combat, and entity management.

## Data Model

This unit does not interact with any database tables directly. All state is maintained in memory via the `instance_naxxramas` script instance and local AI member variables.

## Notable Implementation Details

1.  **Corpse Explosion Logic**: The boss does not automatically explode corpses upon death. Instead, `SummonedCreatureJustDied` records the GUID of dead Crypt Guards. `UpdateAI` periodically calls `ExplodeOneDeadCryptGuard`, which manually summons 10 Corpse Scarabs at the corpse's location. This manual summoning is necessary because the original spell (`SPELL_SELF_SPAWN_10`) is noted in comments as buggy and only playing a visual effect.
2.  **Locust Swarm Timer Pausing**: The Impale timer is explicitly paused while Locust Swarm is active or being cast. The code comments indicate uncertainty about whether this is correct, noting that the timer might need to reset or continue. Currently, Impale will fire immediately after Locust Swarm ends if the timer had expired during the cast.
3.  **Target Restoration**: After casting Impale, the boss's target is temporarily switched to the Impale target. A 1-second timer (`m_uiRestoreTargetTimer`) is set to restore the boss's orientation and target back to the primary victim. This prevents the boss from getting stuck facing the Impale target.
4.  **Crypt Guard Enrage**: The enrage spell (`SPELL_CRYPTGUARD_ENRAGE`) is cast once when health drops below 50%. The comment notes that the spell ID might be wrong, as it provides a 50% attack speed increase and 100 extra damage, which may not match retail behavior.
5.  **Door Interaction**: The door (`anub_doorAI`) plays a random sound from a pool of greetings and taunts. The developer comments express doubt about whether taunts should play on door open, suggesting only the greeting might be appropriate.
6.  **Scarab Threat**: Corpse Scarabs are manually assigned 5000 threat to their target. This is a hardcoded value intended to make them "stick" to a target, mimicking observed behavior.
7.  **Initial Spawn Coordinates**: The initial Crypt Guards spawn at fixed coordinates defined in the `CGs` array. A third coordinate set is used for spawning additional guards during Locust Swarm.

## Member Reference

*   **IMPALE_CD**: Returns a random integer between 12000 and 18000, representing the cooldown for the Impale ability.
*   **LOCUST_SWARM_CD**: Returns a random integer for the Locust Swarm cooldown: 80000–120000 for the initial cast, 90000–110000 for subsequent casts.
*   **boss_anubrekhanAI**: Constructor for the boss AI. Retrieves instance data, spawns initial Crypt Guards, and resets timers.
*   **CheckSpawnInitialCryptGuards**: Spawns two Crypt Guards at predefined locations if the boss is not already defeated. Stores their GUIDs.
*   **SummonedCreatureJustDied**: Adds the GUID of a dead Crypt Guard to the `deadCryptGuards` list for future explosion.
*   **Reset**: Resets all boss timers and flags. Cleans up excess Corpse Scarabs in the area.
*   **JustReachedHome**: Handles boss reset/evade. Marks instance as failed, unsummons all adds, and respawns initial Crypt Guards.
*   **KilledUnit**: Plays a sound on the first player kill. Has a 20% chance of doing nothing on subsequent kills.
*   **Aggro**: Marks instance as in progress. Puts boss and all summoned Crypt Guards into combat with the aggroing unit. Plays aggro sound.
*   **JustDied**: Marks the instance data as DONE.
*   **MoveInLineOfSight**: Pulls players within 55 yards if not in combat and not feigning death.
*   **ExplodeOneDeadCryptGuard**: Removes a dead Crypt Guard from the list, plays a visual spell, summons 10 Corpse Scarabs at the location, assigns them threat, and unsummons the corpse.
*   **UpdateAI**: Main AI loop. Manages Impale, Locust Swarm, and Corpse Explosion timers. Handles target restoration and melee attacks.
*   **mob_cryptguardsAI**: Constructor for Crypt Guard AI. Retrieves instance data and resets timers.
*   **Reset#2**: Resets Crypt Guard timers and enrage flag.
*   **Aggro#2**: Pulls the boss Anub'Rekhan into combat.
*   **UpdateAI#2**: Main AI loop for Crypt Guards. Handles Enrage, Web, Cleave, Acid Spit, and melee attacks.
*   **anub_doorAI**: Constructor for the door AI. Retrieves instance data.
*   **OnUse**: Triggers the boss's greeting/taunt sound and disables further interaction with the door.
*   **GetAI_boss_anubrekhan**: Factory function to create the `boss_anubrekhanAI` instance.
*   **GetAI_mob_cryptguards**: Factory function to create the `mob_cryptguardsAI` instance.
*   **GetAI_anub_door**: Factory function to create the `anub_doorAI` instance.
*   **AddSC_boss_anubrekhan**: Registers the three scripts (boss, adds, door) with the Script Manager.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_anubrekhan

*Source:* boss_anubrekhan.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| IMPALE_CD | function | shared_Util/urand | — | — |
| LOCUST_SWARM_CD | function | shared_Util/urand | — | — |
| boss_anubrekhanAI | ctor | Log.Main/Out, ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| CheckSpawnInitialCryptGuards | method | instance_naxxramas.Main/GetData, Log.Main/Out, Object/GetObjectGuid, WorldObject.Object/SummonCreature#2 | — | — |
| SummonedCreatureJustDied | method | Object/GetEntry, Object/GetObjectGuid | — | — |
| Reset | method | GridSearchers/GetCreatureListWithEntryInGrid#2, shared_Util/urand, TemporarySummon/UnSummon | — | — |
| JustReachedHome | method | instance_naxxramas.Main/SetData, TemporarySummon/UnSummon, ZoneScript/GetCreature | — | — |
| KilledUnit | method | Object/GetTypeId, ScriptMgr/DoScriptText, shared_Util/urand | — | — |
| Aggro | method | Creature.Main/AI, Creature.Main/SetInCombatWithZone, CreatureAI/AttackStart, instance_naxxramas.Main/SetData, ScriptMgr/DoScriptText, ZoneScript/GetCreature | — | — |
| JustDied | method | instance_naxxramas.Main/SetData | — | — |
| MoveInLineOfSight | method | BasicAI/MoveInLineOfSight, CreatureAI/AttackStart, Object/GetTypeId, Unit.Main/HasAuraType, Unit.Main/IsInCombat, WorldObject.Object/IsWithinDistInMap | — | — |
| ExplodeOneDeadCryptGuard | method | Creature.Main/AI, Creature.Main/SelectAttackingTarget, Creature.Main/SetInCombatWithZone, CreatureAI/AttackStart, shared_Util/urand, TemporarySummon/UnSummon, Unit.Main/AddThreat, Unit.Main/SendSpellGo, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SummonCreature#2, ZoneScript/GetCreature | — | — |
| UpdateAI | method | Creature.Main/AI, Creature.Main/SelectAttackingTarget, Creature.Main/SetInCombatWithZone, CreatureAI/AttackStart, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, instance_naxxramas.Main/HandleEvadeOutOfHome, Object/GetObjectGuid, shared_Util/urand, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/GetVictim, Unit.Main/HasAura#2, Unit.Main/SelectHostileTarget, Unit.Main/SetInFront, Unit.Main/SetTargetGuid, WorldObject.Object/SummonCreature#2 | — | — |
| mob_cryptguardsAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset#2 | method | — | — | — |
| Aggro#2 | method | Creature.Main/AI, CreatureAI/AttackStart, ScriptedInstance/GetSingleCreatureFromStorage | — | — |
| UpdateAI#2 | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, ScriptedAI/DoResetThreat, ScriptMgr/DoScriptText, Unit.Main/GetHealthPercent, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| anub_doorAI | ctor | GameObjectAI/GameObjectAI, Log.Main/Out, WorldObject.Object/GetInstanceData | — | — |
| OnUse | method | Log.Main/Out, ScriptedInstance/GetSingleCreatureFromStorage, ScriptMgr/DoScriptText, Unit.Main/IsAlive, WorldObject.Object/GetInstanceId, WorldObject.Object/SetFlag | — | — |
| GetAI_boss_anubrekhan | function | — | — | — |
| GetAI_mob_cryptguards | function | — | — | — |
| GetAI_anub_door | function | — | — | — |
| AddSC_boss_anubrekhan | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
