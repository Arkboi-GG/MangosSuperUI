# boss_thaddius

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_thaddius

## Purpose & Responsibilities

This unit implements the complete encounter logic for **Thaddius**, a boss in the Naxxramas raid instance. The encounter is divided into two distinct phases:

1.  **Phase 1 (Adds):** Players must defeat two adds, **Stalagg** and **Feugen**, simultaneously. These adds are linked to Tesla Coils via electrical chains. If the link is broken (by moving too far away), the coils overload and shock players. The adds share mechanics like Warstomp, Power Surge, Static Field, and Magnetic Pull (which swaps tank targets).
2.  **Phase 2 (Boss):** Once both adds are defeated, Thaddius awakens. He casts **Polarity Shift**, randomly assigning players either a Positive or Negative charge. Players with the same charge standing near each other take amplified damage. Thaddius also uses Chain Lightning, Ball Lightning (ranged attack when out of melee range), and Berserk.

The unit defines AI classes for Thaddius (`boss_thaddiusAI`), his adds (`boss_thaddiusAddsAI`, `boss_stalaggAI`, `boss_feugenAI`), and the Tesla Coils (`npc_tesla_coilAI`). It also includes custom Spell and Aura scripts to handle the specific mechanics of the charges and magnetic pull.

## Member-by-Member Behavior

### Thaddius Main AI (`boss_thaddiusAI`)

*   **Constructor (`boss_thaddiusAI`)**: Initializes the instance data pointer, resets the AI, checks/spawns the adds and coils, applies a visual "spawn" aura (`SPELL_THADIUS_SPAWN`) to keep Thaddius inactive/dark, and sets him as non-selectable.
*   **`CheckSpawnAdds`**: Iterates through Stalagg and Feugen, calling `HandleCheckSpawnAdd` for each. It ensures the adds and their corresponding Tesla Coils are summoned and linked. It also cross-references the adds so each knows the GUID of the other.
*   **`HandleCheckSpawnAdd`**: Handles the lifecycle of a single add/coil pair. If the add exists, it unsummons it (to refresh state). It then summons a fresh add and a fresh Tesla Coil creature. It activates the corresponding Tesla Coil Game Object (GO) and instructs the Coil AI to establish the chain link to the add.
*   **`HandleUnsummonCoil`**: Unsummons the Tesla Coil creature and deactivates the corresponding Tesla Coil GO. Used during the transition to Phase 2.
*   **`HandleUnsummonAdd`**: Unsummons the specified add (Stalagg or Feugen). Used during the transition to Phase 2.
*   **`SummonedCreatureDespawn`**: Currently empty/commented out. Originally intended to clean up coils when adds despawn, but manual handling in `TransitionToPhase` is used instead.
*   **`Reset`**: Resets the event map, ball lightning timer, phase to `THAD_NOT_STARTED`, and kill say cooldown.
*   **`Aggro#3`**: Currently commented out. Previously handled random aggro shouts and making Thaddius selectable.
*   **`JustReachedHome`**: Triggered on wipe/retreat. Sets instance data to `FAIL`, respawns adds via `CheckSpawnAdds`, reapplies the spawn aura, and makes Thaddius non-selectable again.
*   **`KilledUnit#3`**: Plays a random kill shout if the victim is a player and the cooldown has expired.
*   **`JustDied#3`**: Plays death shout, sets instance data to `DONE`. Comments indicate adds despawn themselves.
*   **`TransitionToPhase`**: Core state machine driver.
    *   `THAD_NOT_STARTED`: Leaves combat.
    *   `THAD_PHASE1`: Enters combat zone (adds are fighting).
    *   `THAD_TRANSITION`: Schedules events for the cinematic sequence: Coil overload emotes (10s), Beam zap (13s), and Engagement (14s).
    *   `THAD_PHASE2`: Resets threat, selects nearest target, schedules Berserk (5 min), initial Polarity Shift (10s), and Chain Lightning.
*   **`UpdateTransitionPhase`**: Handles the timed events during the transition.
    *   `EVENT_TRANSITION_1`: Unsummons adds, plays overload emotes on coils.
    *   `EVENT_TRANSITION_2`: Coils cast `SPELL_SHOCK_OVERLOAD` on Thaddius, removes the spawn aura, makes Thaddius selectable, and casts a visual lightning spell.
    *   `EVENT_TRANSITION_3`: Unsummons coils, transitions to `THAD_PHASE2`.
*   **`RemoveDebuffsFromPlayer`**: Helper to strip all charge-related auras (Positive/Negative Apply, Tick, Amp) from a player before applying new ones.
*   **`DoPolarityShift`**: Implements the Polarity Shift mechanic. It gathers all living players in the instance, shuffles them, and assigns the first half Positive Charge and the second half Negative Charge. It calls `RemoveDebuffsFromPlayer` first.
*   **`DoSpellChain`**: Targets a random hostile unit and casts Chain Lightning. Retries quickly if it fails.
*   **`UpdateP2`**: Main update loop for Phase 2.
    *   Processes events: Polarity Shift (every 30s, with a 3s delay for the actual assignment), Chain Lightning, Berserk, and Polarity Change execution.
    *   Manages `m_uiBallLightningTimer`: If Thaddius cannot reach his victim with melee, he counts down this timer. If it expires and he isn't casting another spell, he casts Ball Lightning. Successful melee attacks reset this timer.
*   **`UpdateAI`**: Main update loop.
    *   Checks if threat list is empty to revert to `THAD_NOT_STARTED`.
    *   Switches on current phase:
        *   `THAD_NOT_STARTED`: Waits for instance data `IN_PROGRESS` (set by adds aggroing) to move to `THAD_PHASE1`.
        *   `THAD_PHASE1`: Waits for `FAIL` (wipe) or `SPECIAL` (adds killed) to transition.
        *   `THAD_TRANSITION`: Calls `UpdateTransitionPhase`.
        *   `THAD_PHASE2`: Calls `UpdateP2`.

### Add AI (`boss_thaddiusAddsAI`)

*   **Constructor (`boss_thaddiusAddsAI`)**: Initializes instance data, stores whether it is Stalagg or Feugen (`m_SorF`), and resets.
*   **`Reset#2`**: Resets events, timers, and flags. Restores health, stand state, and removes `NOT_SELECTABLE` flag.
*   **`WarstompTimer` / `PowerSurgeTimer` / `MagneticPullTimer` / `StaticFiledTimer`**: Return randomized or fixed intervals for these abilities.
*   **`GetOtherAdd`**: Retrieves the other add (Stalagg or Feugen) using the stored `otherAdd` GUID.
*   **`Aggro#4`**: If not in fake death, sets combat zone, sets instance data to `IN_PROGRESS`, and forces the other add to attack the same target if it isn't already in combat.
*   **`JustRespawned`**: Calls `Reset`.
*   **`JustReachedHome#2`**: Resets events and sets instance data to `FAIL`.
*   **`HandleMagneticPull`**: Implements the tank swap.
    *   Verifies both adds are alive and have victims.
    *   Calculates threat differences between the two adds' threat lists.
    *   Adjusts threat values so that each add takes on the other's current tank.
    *   Casts `SPELL_MAGNETIC_PULL` on the new targets.
    *   Forces both adds to `AttackStart` their new targets.
*   **`HandleReviveEvent`**: Called after fake death. Resets the add, clears threat, and aggros the nearest target.
*   **`UpdateAI#2`**:
    *   If in `m_bFakeDeath`, waits for `fakeDeathTimer` to expire, then calls `HandleReviveEvent` unless both adds died (`bothDeath`).
    *   Otherwise, processes events: Warstomp (delayed if recently pulled), Static Field, Power Surge, and Magnetic Pull.
    *   Performs melee attacks.
*   **`AttackStart`**: Ignores if in fake death.
*   **`DamageTaken`**:
    *   If damage would kill the add:
        *   Checks if the other add is already in fake death. If so, sets `bothDeath = true` and sets instance data to `SPECIAL` (triggering Thaddius wake-up).
        *   Plays death sound.
        *   Nullifies damage (`uiDamage = 0`).
        *   Enters fake death: Sets health to 0, stand state to DEAD, stops movement, clears reactives/combo points, removes auras, and sets `NOT_SELECTABLE` flag. Starts 5-second `fakeDeathTimer`.

### Specific Add AIs (`boss_stalaggAI`, `boss_feugenAI`)

*   **`boss_stalaggAI`**: Inherits from `boss_thaddiusAddsAI`.
    *   **`Aggro#2`**: Plays Stalagg aggro sound, schedules Warstomp, Power Surge, and Magnetic Pull events. Calls parent `Aggro`.
    *   **`JustDied#2`**: If `bothDeath` is true and instance isn't already `SPECIAL`, sets instance to `SPECIAL`.
    *   **`KilledUnit#2`**: Plays Stalagg kill sound.
*   **`boss_feugenAI`**: Inherits from `boss_thaddiusAddsAI`.
    *   **`Aggro`**: Plays Feugen aggro sound, schedules Warstomp and Static Field events. Calls parent `Aggro`.
    *   **`JustDied`**: Same logic as Stalagg.
    *   **`KilledUnit`**: Plays Feugen kill sound.

### Tesla Coil AI (`npc_tesla_coilAI`)

*   **Constructor (`npc_tesla_coilAI`)**: Initializes instance data and resets.
*   **`Reset#3`**: Sets wander distance to 0, initializes motion master, resets shock timer and link status.
*   **`MoveInLineOfSight` / `Aggro#5`**: Empty/No-op. Coils do not aggro independently.
*   **`ReApplyChain`**: Stores the target add's GUID and entry. Determines if it links to Feugen or Stalagg. Casts the appropriate chain spell (`SPELL_FEUGEN_CHAIN` or `SPELL_STALAGG_CHAIN`).
*   **`UpdateAI#3`**:
    *   Checks if the linked add is in combat; if so, enters combat zone.
    *   Calculates distance to the linked add.
    *   **If distance > 60 yards (Link Broken)**:
        *   Interrupts spells.
        *   If previously linked, plays "Losing Link" emote.
        *   Starts/continues `shockTimer`. If timer expires, casts `SPELL_SHOCK` on the nearest player.
    *   **If distance <= 60 yards (Linked)**:
        *   Resets `shockTimer`.
        *   If not casting, recasts the chain spell to maintain the link.

### Spell & Aura Scripts

*   **`ThaddiusPositiveChargeAuraScript` / `ThaddiusNegativeChargeAuraScript`**:
    *   **`OnPeriodicTrigger` / `OnPeriodicTrigger#2`**: Counts how many other living players within 13 yards have the *same* charge type. If count > 0, applies/updates the Amplify aura (`SPELL_POSITIVE_CHARGE_AMP` or `SPELL_NEGATIVE_CHARGE_AMP`) with stacks equal to the count. If count is 0, removes the Amplify aura.
    *   **`OnAfterApply` / `OnAfterApply#2`**: Removes the Amplify aura if the base charge aura is removed.
*   **`ThaddiusPositiveChargeScript` / `ThaddiusNegativeChargeScript`**:
    *   **`OnEffectExecute` / `OnEffectExecute#2` / `OnEffectExecute#3`**: If the target already has the *same* charge aura, sets spell damage to 0 (preventing self-damage or friendly-fire from same-polarity ticks).
*   **`ThaddiusMagneticPullScript`**:
    *   **`OnEffectExecute`**: Customizes the knockback. Calculates speed based on distance and spell misc value. Applies a knockback from the caster to the target. Returns `false` to prevent default knockback behavior.

### Factory Functions & Registration

*   **`GetAI_boss_feugen` / `GetAI_boss_stalagg` / `GetAI_boss_thaddius` / `GetAI_npc_tesla_coil`**: Factory functions returning new instances of the respective AI classes.
*   **`GetScript_ThaddiusPositiveChargeAura` / `GetScript_ThaddiusNegativeChargeAura`**: Factory functions returning new instances of the aura scripts.
*   **`GetScript_ThaddiusPositiveCharge` / `GetScript_ThaddiusNegativeCharge`**: Factory functions returning new instances of the charge tick spell scripts.
*   **`GetScript_ThaddiusMagneticPull`**: Factory function returning the magnetic pull spell script.
*   **`AddSC_boss_thaddius`**: Registers all scripts (AI, Spell, Aura) with the Script Manager.

## Cross-Unit Boundaries

*   **`instance_naxxramas`**:
    *   **Called by**: `boss_thaddiusAI`, `boss_thaddiusAddsAI`, `npc_tesla_coilAI`.
    *   **Interaction**: Used to get/set encounter state (`TYPE_THADDIUS`: `IN_PROGRESS`, `SPECIAL`, `DONE`, `FAIL`), retrieve creature/game object GUIDs, and access the map.
*   **`ScriptedAI` / `Scripted_NoMovementAI`**:
    *   **Called by**: All AI constructors.
    *   **Interaction**: Base AI functionality (event scheduling, melee attacks, spell casting helpers).
*   **`Creature` / `Unit` / `WorldObject`**:
    *   **Called by**: All AI methods.
    *   **Interaction**: Standard entity manipulation (health, position, combat state, aura management, threat manipulation, summoning/unsummoning).
*   **`ScriptMgr`**:
    *   **Called by**: AI methods for text emotes/sounds.
*   **`SpellCaster`**:
    *   **Called by**: AI methods for casting spells.
*   **`ThreatManager`**:
    *   **Called by**: `boss_thaddiusAddsAI::HandleMagneticPull`.
    *   **Interaction**: Directly manipulates threat values to swap tanks.
*   **`Aura` / `Spell`**:
    *   **Called by**: Aura/Spell scripts.
    *   **Interaction**: Accessing aura holders, targets, and modifying spell damage/knockback.

## Data Model

This unit does not interact directly with database tables. It relies entirely on in-memory instance data (`instance_naxxramas`) and creature/game object templates defined in the world database.

## Notable Implementation Details

*   **Fake Death Mechanic**: Stalagg and Feugen do not truly die immediately. They enter a "fake death" state for 5 seconds. If both are in this state, the encounter transitions to Phase 2. If only one dies, it revives after 5 seconds. This requires careful coordination in `DamageTaken` and `UpdateAI`.
*   **Magnetic Pull Threat Swap**: The implementation manually adjusts threat values in the `ThreatManager` to ensure tanks swap correctly. It calculates the difference in threat between the two adds' perspectives and applies it to force the swap.
*   **Tesla Coil Link Logic**: The coil AI continuously checks distance to its linked add. If the link breaks (>60 yards), it shocks players. The link is re-established by `ReApplyChain` called from Thaddius's AI during setup/respawn.
*   **Polarity Shift Shuffling**: `DoPolarityShift` collects all players, shuffles them using `std::shuffle` with a `std::mt19937` generator, and assigns charges. This ensures a random 50/50 split.
*   **Ball Lightning Ranged Attack**: Thaddius uses a timer-based system to cast Ball Lightning only when he cannot reach his target with melee. This prevents spamming ranged attacks while in melee range.
*   **Transition Cinematic**: The transition from Phase 1 to Phase 2 is handled by a series of scheduled events in `UpdateTransitionPhase`, coordinating unsummons, emotes, and spell casts to mimic the original cinematic.
*   **Hardcoded Timers**: Many timers (Warstomp, Power Surge, etc.) are hardcoded estimates based on video analysis, as noted in comments.
*   **TODOs/Comments**: Several comments indicate uncertainty about specific spell IDs or behaviors (e.g., `SPELL_FLASH` for Feugen, Tesla Coil overload emote existence).

## Member Reference

*   **PolarityShiftTimer**: Helper function returning the initial (10s) or subsequent (30s) timer interval for Polarity Shift.
*   **ChainLightningTimer**: Helper function returning a randomized interval (5–7s) for Chain Lightning casts.
*   **npc_tesla_coilAI**: Constructor for the Tesla Coil AI, initializing instance data and resetting state.
*   **Reset#3**: Resets Tesla Coil movement, shock timer, and link status.
*   **MoveInLineOfSight**: No-op for Tesla Coils.
*   **Aggro#5**: No-op for Tesla Coils; they do not aggro independently.
*   **ReApplyChain**: Establishes the electrical link between the coil and its assigned add (Stalagg or Feugen).
*   **UpdateAI#3**: Main loop for Tesla Coils; manages link integrity, shock casting, and combat state synchronization with the linked add.
*   **boss_thaddiusAddsAI**: Constructor for the base Add AI, initializing instance data and add identity.
*   **Reset#2**: Resets add state, health, and flags, preparing for a new engagement or revival.
*   **WarstompTimer**: Returns a randomized interval for the Warstomp ability.
*   **PowerSurgeTimer**: Returns a randomized interval for the Power Surge ability.
*   **MagneticPullTimer**: Returns a fixed interval (20.5s) for the Magnetic Pull ability.
*   **StaticFiledTimer**: Returns a fixed interval (6s) for the Static Field ability.
*   **GetOtherAdd**: Retrieves the other add (Stalagg or Feugen) via the instance data.
*   **Aggro#4**: Handles add aggro, setting instance state to IN_PROGRESS and syncing the other add's target.
*   **JustRespawned**: Calls Reset to ensure proper state initialization upon respawn.
*   **JustReachedHome#2**: Handles add retreat/wipe, setting instance state to FAIL.
*   **HandleMagneticPull**: Executes the tank-swap mechanic by adjusting threat tables and casting Magnetic Pull.
*   **HandleReviveEvent**: Revives an add from fake death, resetting threat and targeting the nearest player.
*   **UpdateAI#2**: Main loop for adds; handles fake death timers, ability scheduling, and melee attacks.
*   **AttackStart**: Initiates combat, ignoring calls if the add is in fake death.
*   **DamageTaken**: Handles lethal damage by entering fake death, checking for simultaneous death of both adds, and triggering Phase 2 if applicable.
*   **boss_thaddiusAI**: Constructor for Thaddius, initializing instance data, spawning adds/coils, and applying initial stuns.
*   **HandleCheckSpawnAdd**: Spawns or refreshes an add and its associated Tesla Coil, establishing links.
*   **CheckSpawnAdds**: Orchestrates the spawning of both adds and their coils, linking them together.
*   **HandleUnsummonCoil**: Unsummons a Tesla Coil creature and deactivates its Game Object.
*   **HandleUnsummonAdd**: Unsummons an add creature.
*   **SummonedCreatureDespawn**: Currently empty; originally intended for cleanup.
*   **Reset**: Resets Thaddius's internal state, timers, and phase.
*   **Aggro#3**: Currently commented out; previously handled aggro shouts.
*   **JustReachedHome**: Handles Thaddius retreat/wipe, resetting state and respawning adds.
*   **KilledUnit#3**: Plays a kill shout if the victim is a player and cooldown allows.
*   **JustDied#3**: Plays death shout and sets instance state to DONE.
*   **TransitionToPhase**: Drives the state machine, scheduling events for Phase 1, Transition, and Phase 2.
*   **UpdateTransitionPhase**: Executes the cinematic sequence events (overload, zap, engage).
*   **RemoveDebuffsFromPlayer**: Strips all charge-related auras from a player.
*   **DoPolarityShift**: Assigns random Positive/Negative charges to players.
*   **DoSpellChain**: Casts Chain Lightning on a random target.
*   **UpdateP2**: Main loop for Phase 2; handles Polarity Shift, Chain Lightning, Berserk, and Ball Lightning.
*   **UpdateAI**: Main loop for Thaddius; manages phase transitions based on instance state and threat list.
*   **boss_stalaggAI**: Constructor for Stalagg, inheriting from base Add AI.
*   **Aggro#2**: Schedules Stalagg-specific abilities and plays aggro shout.
*   **JustDied#2**: Checks for simultaneous death to trigger Phase 2.
*   **KilledUnit#2**: Plays Stalagg's kill shout.
*   **boss_feugenAI**: Constructor for Feugen, inheriting from base Add AI.
*   **Aggro**: Schedules Feugen-specific abilities and plays aggro shout.
*   **JustDied**: Checks for simultaneous death to trigger Phase 2.
*   **KilledUnit**: Plays Feugen's kill shout.
*   **GetAI_boss_feugen**: Factory function for Feugen's AI.
*   **GetAI_boss_stalagg**: Factory function for Stalagg's AI.
*   **GetAI_npc_tesla_coil**: Factory function for Tesla Coil's AI.
*   **GetAI_boss_thaddius**: Factory function for Thaddius's AI.
*   **OnPeriodicTrigger#2**: Periodic check for Negative Charge amplification based on nearby players with the same charge.
*   **OnAfterApply#2**: Removes Negative Charge amplification if the base aura is removed.
*   **GetScript_ThaddiusPositiveChargeAura**: Factory function for Positive Charge Aura script.
*   **OnPeriodicTrigger**: Periodic check for Positive Charge amplification based on nearby players with the same charge.
*   **OnAfterApply**: Removes Positive Charge amplification if the base aura is removed.
*   **GetScript_ThaddiusNegativeChargeAura**: Factory function for Negative Charge Aura script.
*   **OnEffectExecute#3**: Modifies Positive Charge tick damage to zero if the target has the same charge.
*   **GetScript_ThaddiusPositiveCharge**: Factory function for Positive Charge tick script.
*   **OnEffectExecute#2**: Modifies Negative Charge tick damage to zero if the target has the same charge.
*   **GetScript_ThaddiusNegativeCharge**: Factory function for Negative Charge tick script.
*   **OnEffectExecute**: Customizes Magnetic Pull knockback physics.
*   **GetScript_ThaddiusMagneticPull**: Factory function for Magnetic Pull script.
*   **AddSC_boss_thaddius**: Registers all Thaddius encounter scripts with the engine.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_thaddius

*Source:* boss_thaddius.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| PolarityShiftTimer | function | — | — | — |
| ChainLightningTimer | function | shared_Util/urand | — | — |
| npc_tesla_coilAI | ctor | Scripted_NoMovementAI/Scripted_NoMovementAI, WorldObject.Object/GetInstanceData | — | — |
| Reset#3 | method | Creature.Main/SetDefaultMovementType, Creature.Main/SetWanderDistance, Creature.MotionMaster/Initialize, Unit.Main/GetMotionMaster | — | — |
| MoveInLineOfSight | method | — | — | — |
| Aggro#5 | method | — | — | — |
| ReApplyChain | method | CreatureAI/DoCastSpellIfCan, Log.Main/Out | — | — |
| UpdateAI#3 | method | Creature.Main/SelectAttackingTarget, Creature.Main/SetInCombatWithZone, CreatureAI/DoCastSpellIfCan, ScriptMgr/DoScriptText, SpellCaster/CastSpell#2, SpellCaster/InterruptNonMeleeSpells, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/IsInCombat, WorldObject.Object/GetDistance2d#3, ZoneScript/GetCreature | — | — |
| boss_thaddiusAddsAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset#2 | method | EventMap/Reset, Unit.Main/GetMaxHealth, Unit.Main/SetHealth, Unit.Main/SetStandState, WorldObject.Object/RemoveFlag | — | — |
| WarstompTimer | method | shared_Util/urand | — | — |
| PowerSurgeTimer | method | shared_Util/urand | — | — |
| MagneticPullTimer | method | — | — | — |
| StaticFiledTimer | method | — | — | — |
| GetOtherAdd | method | ZoneScript/GetCreature | — | — |
| Aggro#4 | method | Creature.Main/AI, Creature.Main/SetInCombatWithZone, CreatureAI/AttackStart, instance_naxxramas.Main/SetData, Unit.Main/IsInCombat | — | — |
| JustRespawned | method | — | — | — |
| JustReachedHome#2 | method | EventMap/Reset, instance_naxxramas.Main/SetData | — | — |
| HandleMagneticPull | method | Creature.Main/AI, CreatureAI/AttackStart, SpellCaster/CastSpell#2, SpellCaster/InterruptNonMeleeSpells, ThreatManager/addThreat#3, ThreatManager/getThreat, Unit.Main/GetThreatManager, Unit.Main/GetVictim | — | — |
| HandleReviveEvent | method | Creature.Main/SelectAttackingTarget, ScriptedAI/DoResetThreat | — | — |
| UpdateAI#2 | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, EventMap/ExecuteEvent, EventMap/Repeat#3, EventMap/Update, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| AttackStart | method | CreatureAI/AttackStart | — | — |
| DamageTaken | method | Creature.Main/AI, Creature.MotionMaster/MoveIdle, instance_naxxramas.Main/SetData, MotionMaster/Clear, ScriptMgr/DoScriptText, SpellCaster/InterruptNonMeleeSpells, Unit.Main/AttackStop, Unit.Main/ClearAllReactives, Unit.Main/ClearComboPointHolders, Unit.Main/GetHealth, Unit.Main/GetMotionMaster, Unit.Main/RemoveAllAurasOnDeath, Unit.Main/SetHealth, Unit.Main/SetStandState, Unit.Main/StopMoving, WorldObject.Object/SetFlag | — | — |
| boss_thaddiusAI | ctor | ScriptedAI/ScriptedAI, SpellCaster/CastSpell#2, Unit.Main/HasAura#2, WorldObject.Object/GetInstanceData, WorldObject.Object/SetFlag | — | — |
| HandleCheckSpawnAdd | method | Creature.Main/AI, GameObject/SetGoState, Log.Main/Out, Object/GetObjectGuid, ScriptedInstance/GetSingleGameObjectFromStorage, TemporarySummon/UnSummon, WorldObject.Object/SummonCreature#2, ZoneScript/GetCreature | — | — |
| CheckSpawnAdds | method | Creature.Main/AI, instance_naxxramas.Main/GetData, ZoneScript/GetCreature | — | — |
| HandleUnsummonCoil | method | GameObject/SetGoState, ObjectGuid/ObjectGuid#5, ScriptedInstance/GetSingleGameObjectFromStorage, TemporarySummon/UnSummon, ZoneScript/GetCreature | — | — |
| HandleUnsummonAdd | method | ObjectGuid/ObjectGuid#5, TemporarySummon/UnSummon, ZoneScript/GetCreature | — | — |
| SummonedCreatureDespawn | method | — | — | — |
| Reset | method | EventMap/Reset | — | — |
| Aggro#3 | method | — | — | — |
| JustReachedHome | method | instance_naxxramas.Main/SetData, SpellCaster/CastSpell#2, Unit.Main/HasAura#2, WorldObject.Object/SetFlag | — | — |
| KilledUnit#3 | method | Object/GetTypeId, ScriptMgr/DoScriptText | — | — |
| JustDied#3 | method | instance_naxxramas.Main/SetData, ScriptMgr/DoScriptText | — | — |
| TransitionToPhase | method | Creature.Main/OnLeaveCombat, Creature.Main/SelectAttackingTarget, Creature.Main/SetInCombatWithZone, CreatureAI/AttackStart, EventMap/ScheduleEvent#3, Log.Main/Out, ScriptedAI/DoResetThreat | — | — |
| UpdateTransitionPhase | method | EventMap/ExecuteEvent, EventMap/Update, ScriptMgr/DoScriptText, SpellCaster/CastSpell#2, Unit.Main/RemoveAurasDueToSpell, WorldObject.Object/RemoveFlag, ZoneScript/GetCreature | — | — |
| RemoveDebuffsFromPlayer | method | Unit.Main/RemoveAurasDueToSpell | — | — |
| DoPolarityShift | method | Map.Main/GetPlayers, ScriptMgr/DoScriptText, SpellCaster/CastSpell#2, Unit.Main/IsDead, ZoneScript/GetMap#2 | — | — |
| DoSpellChain | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, EventMap/Repeat#3 | — | — |
| UpdateP2 | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, EventMap/ExecuteEvent, EventMap/Repeat#3, EventMap/ScheduleEvent#3, EventMap/Update, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| UpdateAI | method | instance_naxxramas.Main/GetData, Log.Main/Out, ThreatManager/isThreatListEmpty, Unit.Main/GetThreatManager, Unit.Main/IsInCombat | — | — |
| boss_stalaggAI | ctor | — | — | — |
| Aggro#2 | method | EventMap/ScheduleEvent#3, ScriptMgr/DoScriptText | — | — |
| JustDied#2 | method | instance_naxxramas.Main/GetData, instance_naxxramas.Main/SetData | — | — |
| KilledUnit#2 | method | Object/GetTypeId, ScriptMgr/DoScriptText | — | — |
| boss_feugenAI | ctor | — | — | — |
| Aggro | method | EventMap/ScheduleEvent#3, ScriptMgr/DoScriptText | — | — |
| JustDied | method | instance_naxxramas.Main/GetData, instance_naxxramas.Main/SetData | — | — |
| KilledUnit | method | Object/GetTypeId, ScriptMgr/DoScriptText | — | — |
| GetAI_boss_feugen | function | — | — | — |
| GetAI_boss_stalagg | function | — | — | — |
| GetAI_npc_tesla_coil | function | — | — | — |
| GetAI_boss_thaddius | function | — | — | — |
| OnPeriodicTrigger#2 | method | Aura/GetHolder, Map.Main/GetId, Map.Main/GetPlayers, Object/GetGUID, Unit.Main/AddAura, Unit.Main/GetAura#2, Unit.Main/HasAura#2, Unit.Main/IsDead, Unit.Main/RemoveAurasDueToSpell, Unit.SpellAuras/SetStackAmount, WorldObject.Object/GetDistance2d#3, WorldObject.Object/GetMap | — | — |
| OnAfterApply#2 | method | Aura/GetEffIndex, Aura/GetTarget, Unit.Main/HasAura#2, Unit.Main/RemoveAurasDueToSpell | — | — |
| GetScript_ThaddiusPositiveChargeAura | function | — | — | — |
| OnPeriodicTrigger | method | Aura/GetHolder, Map.Main/GetId, Map.Main/GetPlayers, Object/GetGUID, Unit.Main/AddAura, Unit.Main/GetAura#2, Unit.Main/HasAura#2, Unit.Main/IsDead, Unit.Main/RemoveAurasDueToSpell, Unit.SpellAuras/SetStackAmount, WorldObject.Object/GetDistance2d#3, WorldObject.Object/GetMap | — | — |
| OnAfterApply | method | Aura/GetEffIndex, Aura/GetTarget, Unit.Main/HasAura#2, Unit.Main/RemoveAurasDueToSpell | — | — |
| GetScript_ThaddiusNegativeChargeAura | function | — | — | — |
| OnEffectExecute#3 | method | Spell.Main/GetUnitTarget, Unit.Main/HasAura#2 | — | — |
| GetScript_ThaddiusPositiveCharge | function | — | — | — |
| OnEffectExecute#2 | method | Spell.Main/GetUnitTarget, Unit.Main/HasAura#2 | — | — |
| GetScript_ThaddiusNegativeCharge | function | — | — | — |
| OnEffectExecute | method | Spell.Main/GetUnitTarget, Unit.Main/KnockBackFrom, WorldObject.Object/GetDistance#3 | — | — |
| GetScript_ThaddiusMagneticPull | function | — | — | — |
| AddSC_boss_thaddius | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
