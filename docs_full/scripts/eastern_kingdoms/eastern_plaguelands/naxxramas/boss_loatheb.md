# boss_loatheb

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_loatheb

**Purpose & Responsibilities**
This unit implements the combat artificial intelligence and spell behaviors for the **Loatheb** encounter in the Naxxramas raid instance. It manages four distinct creature types: the boss **Loatheb**, his summoned **Eye Stalks**, **Rotting Maggots**, and **Diseased Maggots**. Additionally, it provides a custom spell script for the **Corrupted Mind** ability, which applies class-specific effects to players.

The core complexity lies in the **Loatheb** AI (`boss_loathebAI`), which orchestrates a multi-phase fight involving periodic spell casts (Poison Aura, Inevitable Doom, Corrupted Mind), the management of a persistent "Fungal Bloom" environment effect via summoned spores, and a dynamic system for summoning, controlling, and recycling Eye Stalks. The Eye Stalk system uses a pool of 20 logical "slots" to manage up to 6 simultaneous stalks, handling their spawn timers, submerge animations, and forced despawns when the limit is reached.

## Member-by-Member Behavior

### Loatheb Boss Logic (`boss_loathebAI`)
The primary controller for the encounter. It initializes with a random spore location and a pool of 20 Eye Stalk slots.

*   **Constructor**: Selects one of two hardcoded coordinates for spore spawning. Initializes the `events` scheduler and sets up the `eyeStalks` array with randomized initial cooldowns. It links to the `instance_naxxramas` script data to report encounter status.
*   **Aggro**: Starts the fight by scheduling all major abilities. It sets the instance data to `IN_PROGRESS`.
*   **UpdateAI**: The main loop. It delegates Eye Stalk management to `WhackAStalk`, checks for out-of-home evasion via `instance_naxxramas.Main/HandleEvadeOutOfHome`, and processes the event queue.
    *   **EVENT_SUMMON_SPORE**: Summons a spore at the pre-selected location. If a target is selected, the spore adds threat to them. Repeats every 13 seconds.
    *   **EVENT_CORRUPTED_MIND**: Attempts to cast the Corrupted Mind spell. If successful, repeats every 10 seconds; otherwise, retries in 100ms.
    *   **EVENT_POISON_AURA**: Casts Poison Aura. Repeats every 12 seconds on success, or 100ms on failure.
    *   **EVENT_INEVITABLE_DOOM**: Casts Inevitable Doom. Tracks the number of casts (`numDooms`). After 6 casts (approx. 5 minutes), the interval shortens from 30 seconds to 15 seconds.
    *   **EVENT_REMOVE_CURSE**: Casts Remove Curse. Repeats every 30 seconds.
*   **WhackAStalk**: Manages the lifecycle of Eye Stalks. It iterates through 20 logical slots.
    *   If a slot is in `COOLDOWN` and the timer expires, it picks a random available physical location, summons an Eye Stalk, marks the slot as `UP`, and sets a new timer (15-20s) for the stalk to submerge.
    *   If a slot is `UP` and the timer expires, it checks if the corresponding creature exists. If the creature is not channeling a spell, it triggers the submerge sequence (`haveSubmerged = true`) and schedules an unsummon.
*   **SummonedCreatureDespawn / SummonedCreatureJustDied**: Handles cleanup when an Eye Stalk dies or despawns. It returns the physical location index to the `availableEyeLocs` pool. If killed, it assigns a longer cooldown (10-50s + 20s penalty) to that logical slot.
*   **EnterEvadeMode**: On wipe or reset, it despawns all tracked spores and removes the `SPELL_FUNGAL_BLOOM` aura from all players within visibility range.
*   **JustDied / JustReachedHome**: Updates the instance data to `DONE` or `FAIL` respectively.

### Eye Stalk Logic (`mob_eyeStalkAI`)
Controls the behavior of individual Eye Stalks summoned by Loatheb.

*   **Constructor**: Disables assistance calls and combat movement. Initializes state flags for submersion.
*   **Reset**: Roots the creature and stops movement.
*   **MoveInLineOfSight**: Aggro logic. Ignores targets for the first 3 seconds after spawn (`timeSinceSpawn < 3000`). Requires line of sight and proximity (19 yards).
*   **UpdateAI**:
    *   If `haveSubmerged` is true, it casts spell 26234 (submerge visual/effect) once and then exits the update loop effectively (returns early).
    *   Otherwise, if within 35 yards of the victim and not casting, it casts `SPELL_MIND_FLAY`. If too far, it stops attacking.
    *   Performs melee attacks if ready.

### Maggot Logic (`mob_rottingMaggotAI`)
Shared AI for both Rotting and Diseased Maggots. Differentiated by the `isDiseased` boolean passed in the constructor.

*   **Constructor**: Disables assistance calls.
*   **MoveInLineOfSight**: Very tight aggro radius (1.5 yards). Standard aggro checks apply.
*   **Aggro**: Records the position where aggro occurred (`aggroPossition`).
*   **UpdateAI**:
    *   If `isDiseased` is true, ensures the maggot has the `SPELL_RETCHING_PLAGUE` aura.
    *   If the maggot moves more than 40 yards from its aggro position, it evades (despawns).
    *   Otherwise, performs melee attacks.

### Spell Script (`LoathebCorruptedMindAoEScript`)
Handles the secondary effects of the Corrupted Mind spell.

*   **OnEffectExecute**: When the spell hits a player, it checks the player's class.
    *   **Priest/Druid**: Casts spell 29194.
    *   **Paladin**: Casts spell 29196.
    *   **Shaman**: Casts spell 29198.
    *   Other classes return false (no effect).
    *   *Note*: The code comments indicate that Priests originally had a different spell ID (29185) but were changed to match Druids due to issues with damage-triggered effects.

## Cross-Unit Boundaries

*   **`instance_naxxramas.Main`**:
    *   **Called by**: `boss_loathebAI.Aggro`, `JustDied`, `JustReachedHome`, `UpdateAI`.
    *   **Collaboration**: `boss_loathebAI` reports the encounter state (`IN_PROGRESS`, `DONE`, `FAIL`) to the instance script. It also calls `HandleEvadeOutOfHome` to check if the boss has moved too far from his home position, triggering an evade if necessary.
*   **`Creature.Main` / `CreatureAI` / `ScriptedAI`**:
    *   **Called by**: All AI structs.
    *   **Collaboration**: Standard AI framework interactions. `SetNoCallAssistance` prevents these mobs from calling for help. `DoMeleeAttackIfReady`, `DoCastSpellIfCan`, and `EnterEvadeMode` handle standard combat actions.
*   **`WorldObject.Object`**:
    *   **Called by**: `mob_rottingMaggotAI`, `mob_eyeStalkAI`, `boss_loathebAI`.
    *   **Collaboration**: Used for spatial queries (`IsWithinDistInMap`, `IsWithinLOSInMap`, `GetDistance`, `GetPosition`) and summoning/despawning entities (`SummonCreature`, `GetAlivePlayerListInRange`).
*   **`Unit.Main`**:
    *   **Called by**: All AI structs.
    *   **Collaboration**: Threat management (`AddThreat`, `SetInCombatWith`), target selection (`SelectHostileTarget`, `GetVictim`), and state checks (`IsHostileTo`, `HasAura`, `GetClass`).
*   **`SpellCaster` / `Spell`**:
    *   **Called by**: `boss_loathebAI`, `mob_eyeStalkAI`, `LoathebCorruptedMindAoEScript`.
    *   **Collaboration**: Casting spells (`CastSpell`), checking cast states (`IsNonMeleeSpellCasted`), and accessing spell targets/effects in the script hook.
*   **`EventMap`**:
    *   **Called by**: `boss_loathebAI`.
    *   **Collaboration**: Manages the timed abilities of Loatheb. `ScheduleEvent`, `Update`, `ExecuteEvent`, and `Repeat` drive the fight's rhythm.
*   **`shared_Util`**:
    *   **Called by**: `boss_loathebAI`, `mob_rottingMaggotAI`, `mob_eyeStalkAI`.
    *   **Collaboration**: `urand` is used extensively for randomizing timers, spore locations, and eye stalk spawn slots.
*   **`ScriptMgr` / `ScriptLoader`**:
    *   **Called by**: `AddSC_boss_loatheb`.
    *   **Collaboration**: Registers the AI scripts and spell scripts with the server's script manager so they are loaded at startup.

## Data Model

This unit does not interact directly with any database tables. All data (spawn positions, spell IDs, timers) is hardcoded in the source file.

## Notable Implementation Details

*   **Eye Stalk Pool Management**: The `boss_loathebAI` uses a fixed-size array `eyeStalks[20]` to represent logical slots, separate from the physical creatures. This allows for complex timing logic (cooldowns, forced submerges) independent of the creature's existence. The `availableEyeLocs` vector tracks which physical spawn points are free.
*   **Submerge Delay**: Eye Stalks do not despawn immediately when their "up" timer expires. They enter a `haveSubmerged` state, cast a submerge spell, and are unsummoned after a delay (1100ms in `WhackAStalk`). This prevents interrupting the Mind Flay channel abruptly.
*   **Maggot Evade Range**: Maggots have a strict 40-yard leash from their aggro point. If they wander further, they evade. This prevents them from chasing players across the entire room.
*   **Corrupted Mind Class Logic**: The spell script hardcodes specific spell IDs for Priest, Druid, Paladin, and Shaman. Other classes receive no effect. The comment notes a workaround for Priests using the Druid spell ID.
*   **Inevitable Doom Scaling**: The frequency of Inevitable Doom increases after 6 casts. This is tracked by `numDooms` in `boss_loathebAI`.
*   **Spore Location Randomization**: The spore spawn location is chosen once during the boss's construction (`boss_loathebAI` ctor) and remains constant for the entire fight. This matches the observed behavior where spores spawn in one consistent area.
*   **Vampiric Embrace Handling**: The code contains commented-out logic for removing Vampiric Embrace. The comments explain that this was a hotfix in the original game, but the current implementation does not actively dispel it, relying on the `SPELL_REMOVE_CURSE` ability instead.

## Member Reference

**mob_rottingMaggotAI** (ctor): Initializes the maggot AI, disabling assistance calls and calling `Reset`. Sets the `isDiseased` flag.

**Reset#3** (method): Empty override for `mob_rottingMaggotAI`. No action taken.

**MoveInLineOfSight#2** (method): Checks if a unit is within 1.5 yards. If so, and valid for attack, initiates combat or adds threat.

**Aggro#2** (method): Records the creature's position at aggro time into `aggroPossition`.

**UpdateAI#3** (method): Main loop for maggots. Applies `SPELL_RETCHING_PLAGUE` if diseased. Evades if distance from `aggroPossition` exceeds 40 yards. Otherwise, performs melee attacks.

**mob_eyeStalkAI** (ctor): Initializes eye stalk AI, disabling assistance and combat movement. Resets timers and submerge flags.

**Reset#2** (method): Roots the eye stalk, stops movement, and disables assistance calls.

**MoveInLineOfSight** (method): Aggro logic for eye stalks. Ignores targets for first 3 seconds. Requires LOS and 19-yard proximity.

**UpdateAI#2** (method): Main loop for eye stalks. If submerged, casts submerge spell and exits. Otherwise, casts `SPELL_MIND_FLAY` if within 35 yards and not casting. Performs melee attacks.

**boss_loathebAI** (ctor): Initializes boss AI. Picks random spore location. Sets up event scheduler and eye stalk pool with random initial cooldowns. Links to instance data.

**Reset** (method): Resets the event scheduler and `numDooms` counter.

**Aggro** (method): Schedules all boss abilities with initial delays. Sets instance data to `IN_PROGRESS`.

**JustDied** (method): Sets instance data to `DONE`.

**JustReachedHome** (method): Sets instance data to `FAIL`.

**EnterEvadeMode** (method): Despawns all tracked spores. Removes `SPELL_FUNGAL_BLOOM` from nearby players. Calls parent evade mode.

**WhackAStalk** (method): Manages eye stalk lifecycle. Spawns new stalks from cooldown slots. Triggers submerge/unsummon for stalks whose "up" timer expires.

**SummonedCreatureDespawn** (method): Handles eye stalk despawn. Returns location to pool. Sets cooldown if not already in cooldown state.

**SummonedCreatureJustDied** (method): Handles eye stalk death. Sets a longer cooldown (with penalty) for the logical slot.

**UpdateAI** (method): Main boss loop. Calls `WhackAStalk`. Processes events (Summon Spore, Corrupted Mind, Poison Aura, Inevitable Doom, Remove Curse). Performs melee attacks.

**GetAI_boss_loatheb** (function): Factory function returning a new `boss_loathebAI` instance.

**GetAI_mob_rottingMaggot** (function): Factory function returning a new `mob_rottingMaggotAI` instance with `isDiseased=false`.

**GetAI_mob_diseasedMaggot** (function): Factory function returning a new `mob_rottingMaggotAI` instance with `isDiseased=true`.

**GetAI_mob_eyeStalk** (function): Factory function returning a new `mob_eyeStalkAI` instance.

**OnEffectExecute** (method): Spell script hook for Corrupted Mind. Applies class-specific spells to the target.

**GetScript_LoathebCorruptedMindAoE** (function): Factory function returning a new `LoathebCorruptedMindAoEScript` instance.

**AddSC_boss_loatheb** (function): Registers all AI and spell scripts for the Loatheb encounter with the script manager.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_loatheb

*Source:* boss_loatheb.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| mob_rottingMaggotAI | ctor | Creature.Main/SetNoCallAssistance, ScriptedAI/ScriptedAI | — | — |
| Reset#3 | method | — | — | — |
| MoveInLineOfSight#2 | method | Creature.Main/CanInitiateAttack, Creature.Main/SetNoCallAssistance, CreatureAI/AttackStart, Map.Main/IsDungeon, Unit.Main/AddThreat, Unit.Main/GetVictim, Unit.Main/IsHostileTo, Unit.Main/IsInAccessablePlaceFor, Unit.Main/IsTargetableBy, Unit.Main/SetInCombatWith, WorldObject.Object/GetMap, WorldObject.Object/IsWithinDistInMap, WorldObject.Object/IsWithinLOSInMap | — | — |
| Aggro#2 | method | Creature.Main/SetNoCallAssistance, WorldObject.Object/GetPosition | — | — |
| UpdateAI#3 | method | CreatureAI/DoMeleeAttackIfReady, ScriptedAI/EnterEvadeMode, SpellCaster/CastSpell#2, Unit.Main/GetVictim, Unit.Main/HasAura#2, Unit.Main/SelectHostileTarget, WorldObject.Object/GetDistance#2 | — | — |
| mob_eyeStalkAI | ctor | Creature.Main/SetNoCallAssistance, CreatureAI/SetCombatMovement, ScriptedAI/ScriptedAI | — | — |
| Reset#2 | method | Creature.Main/SetNoCallAssistance, Unit.Main/AddUnitState, Unit.Main/SetRooted, Unit.Main/StopMoving | — | — |
| MoveInLineOfSight | method | Creature.Main/CanInitiateAttack, Creature.Main/SetNoCallAssistance, CreatureAI/AttackStart, Map.Main/IsDungeon, Unit.Main/AddThreat, Unit.Main/GetVictim, Unit.Main/IsHostileTo, Unit.Main/IsInAccessablePlaceFor, Unit.Main/IsTargetableBy, Unit.Main/SetInCombatWith, WorldObject.Object/GetMap, WorldObject.Object/IsWithinDistInMap, WorldObject.Object/IsWithinLOSInMap | — | — |
| UpdateAI#2 | method | Creature.Main/SetNoCallAssistance, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, ScriptedAI/DoStopAttack, SpellCaster/CastSpell#2, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, WorldObject.Object/GetDistance#3 | — | — |
| boss_loathebAI | ctor | ObjectGuid/ObjectGuid#5, ScriptedAI/ScriptedAI, shared_Util/urand, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | EventMap/Reset | — | — |
| Aggro | method | EventMap/ScheduleEvent#2, instance_naxxramas.Main/SetData | — | — |
| JustDied | method | instance_naxxramas.Main/SetData | — | — |
| JustReachedHome | method | instance_naxxramas.Main/SetData | — | — |
| EnterEvadeMode | method | Creature.Main/DespawnOrUnsummon, Map.Main/GetCreature, ScriptedAI/EnterEvadeMode, Unit.Main/RemoveAurasDueToSpell, WorldObject.Object/GetAlivePlayerListInRange, WorldObject.Object/GetMap | — | — |
| WhackAStalk | method | Creature.Main/AI, Log.Main/Out, Object/GetObjectGuid, shared_Util/urand, SpellCaster/IsNonMeleeSpellCasted, TemporarySummon/UnSummon, WorldObject.Object/SummonCreature#2, ZoneScript/GetCreature | — | — |
| SummonedCreatureDespawn | method | Object/GetEntry, Object/GetObjectGuid, ObjectGuid/ObjectGuid#5, ObjectGuid/operator==, shared_Util/urand | — | — |
| SummonedCreatureJustDied | method | Object/GetEntry, Object/GetObjectGuid, ObjectGuid/operator==, shared_Util/urand | — | — |
| UpdateAI | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, EventMap/ExecuteEvent, EventMap/Repeat, EventMap/Repeat#3, EventMap/Update, instance_naxxramas.Main/HandleEvadeOutOfHome, Object/GetObjectGuid, Unit.Main/AddThreat, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, WorldObject.Object/SummonCreature#2 | — | — |
| GetAI_boss_loatheb | function | — | — | — |
| GetAI_mob_rottingMaggot | function | — | — | — |
| GetAI_mob_diseasedMaggot | function | — | — | — |
| GetAI_mob_eyeStalk | function | — | — | — |
| OnEffectExecute | method | Spell.Main/GetUnitTarget, SpellCaster/CastSpell#2, Unit.Main/GetClass | — | — |
| GetScript_LoathebCorruptedMindAoE | function | — | — | — |
| AddSC_boss_loatheb | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
