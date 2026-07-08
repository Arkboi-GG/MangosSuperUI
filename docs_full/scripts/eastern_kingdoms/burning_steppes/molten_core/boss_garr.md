# boss_garr

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_garr

**Purpose & Responsibilities**  
`boss_garr.cpp` implements the combat artificial intelligence for **Garr**, a raid boss in the *Molten Core* instance, and his summoned adds, the **Firesworn**. The unit defines two distinct AI classes: `boss_garrAI` for the boss and `mob_fireswornAI` for the adds. It manages Garr’s spell rotations (Antimagic Pulse, Magma Shackles, Massive Eruption), tracks the status of his adds to coordinate the "Separation Anxiety" mechanic and the final "Massive Eruption" phase, and handles the synergy where killing Firesworns buffs Garr and removes them from the eruption pool. The unit also registers these scripts with the server’s script manager via `AddSC_boss_garr`.

**Member-by-Member Behavior**  

### Boss Garr AI (`boss_garrAI`)
*   **Construction & State**: The constructor initializes the instance data pointer and calls `Reset`. `Reset` clears the event map and the list of Firesworn GUIDs. If Garr is alive and the encounter isn't already marked as done, it sets the instance data state to `NOT_STARTED`.
*   **Aggro**: When Garr aggroes, it checks if the encounter is already `DONE`; if so, it deletes itself. Otherwise, it sets the instance state to `IN_PROGRESS`, marks the zone as in combat, and scans the grid for `NPC_FIRESWORN` creatures within 150 yards. It stores their GUIDs in `m_lFiresworn` and casts `SPELL_SEPARATION_ANXIETY` on each valid add. Finally, it schedules combat events.
*   **Combat Loop**: `UpdateAI` drives the main loop, updating the event map, calling `UpdateEvents` to handle timed spells, and performing melee attacks if ready.
*   **Spell Casting Logic (`UpdateEvents`)**:
    *   `EVENT_ANTIMAGICPULSE`: Casts `SPELL_ANTIMAGICPULSE` on self. On success, repeats in 15–20 seconds; on failure, retries in 1 second.
    *   `EVENT_MAGMASHACKLES`: Casts `SPELL_MAGMASHACKLES` on self. On success, repeats in 10–15 seconds; on failure, retries in 1 second.
    *   `EVENT_MASSIVE_ERUPTION`: Triggered initially at 6 minutes, then every 20 seconds. If `m_lFiresworn` is not empty, it picks a random add from the list. If that add is not stunned, Garr says the emote `EMOTE_MASSIVE_ERUPTION` and casts `SPELL_ERUPTION_TRIGGER` on the add. This event repeats every 20 seconds regardless of whether a target was found.
*   **Spell Hit Handling**: `SpellHit` listens for `SPELL_ENRAGE_TRIGGER`. If hit, it casts `SPELL_ENRAGE` on itself (which stacks up to 10 times).
*   **Add Death Coordination**: `FireswornJustDied` is called by the Firesworn AI when an add dies. It removes the add's GUID from `m_lFiresworn`, ensuring it won't be targeted by future Massive Eruptions.
*   **Death**: `JustDied` sets the instance data state to `DONE`.

### Firesworn AI (`mob_fireswornAI`)
*   **Construction & State**: The constructor initializes instance data and calls `Reset`. `Reset` clears the force explosion flag and resets the event map.
*   **Aggro**: Casts `SPELL_THRASH` on self and marks the zone as in combat. Schedules combat events.
*   **Combat Loop**: `UpdateAI` updates the event map, calls `UpdateEvents`, and performs melee attacks.
*   **Spell Casting Logic (`UpdateEvents`)**:
    *   `EVENT_IMMOLATE`: Casts `SPELL_IMMOLATE` on the current victim. On success, repeats in 20 seconds; on failure, retries in 1 second.
*   **Spell Hit Handling**: `SpellHit` listens for `SPELL_ERUPTION_TRIGGER`. If hit, it sets `m_bForceExplosion` to true and immediately casts `SPELL_MASSIVE_ERUPTION` on self.
*   **Death**: `JustDied` performs several actions:
    1.  Finds Garr via instance storage. If Garr is alive, it casts `SPELL_ENRAGE_TRIGGER` on Garr (triggering Garr's buff) and calls `FireswornJustDied` on Garr's AI to remove this add from the eruption list.
    2.  If `m_bForceExplosion` is false (i.e., it died naturally, not from Garr's trigger), it casts `SPELL_ADD_ERUPTION` on self (knockback/damage).

### Script Registration
*   `GetAI_boss_garr` and `GetAI_mob_firesworn` are factory functions returning new instances of their respective AIs.
*   `AddSC_boss_garr` creates `Script` objects for both "boss_garr" and "mob_firesworn", assigns their `GetAI` pointers, and registers them with the script manager.

**Cross-Unit Boundaries**  
*   **Instance Data**: Both AIs heavily rely on `ScriptedInstance` (via `m_pInstance`) to track encounter state (`TYPE_GARR`) and locate Garr (`GetSingleCreatureFromStorage`). `boss_garrAI::Aggro` and `JustDied` set the state; `mob_fireswornAI::JustDied` reads Garr's location.
*   **EventMap**: Both AIs use `EventMap` for scheduling and executing timed events (`Reset`, `RescheduleEvent`, `Update`, `ExecuteEvent`, `Repeat`).
*   **Creature Management**: `boss_garrAI::Aggro` uses `GetCreatureListWithEntryInGrid` and `GetMap()->GetCreature` to find and validate Firesworn adds. `mob_fireswornAI::JustDied` uses `GetSingleCreatureFromStorage` to find Garr.
*   **Spell Casting**: Both AIs use `DoCastSpellIfCan` for conditional casting and direct `CastSpell` for forced effects. `boss_garrAI::SpellHit` and `mob_fireswornAI::SpellHit` react to specific spell IDs.
*   **AI Interaction**: `mob_fireswornAI::JustDied` dynamically casts Garr's AI to `boss_garrAI*` to call `FireswornJustDied`, enabling cross-AI state synchronization.
*   **Script System**: `AddSC_boss_garr` interacts with `Script` and `ScriptMgr` to register the AIs.

**Data Model**  
This unit does not directly query or modify any database tables. It relies entirely on runtime memory structures (`ScriptedInstance`, `EventMap`, `std::vector<ObjectGuid>`) and creature/spell definitions loaded by the server.

**Notable Implementation Details**  
*   **Separation Anxiety Mechanic**: Garr applies `SPELL_SEPARATION_ANXIETY` to all Firesworns at aggro. This likely prevents them from fleeing or despawning, keeping them in the fight until killed or erupted.
*   **Eruption Target Validation**: In `boss_garrAI::UpdateEvents`, before triggering an eruption, the code checks if the selected Firesworn is stunned (`!HasAuraType(SPELL_AURA_MOD_STUN)`). This prevents Garr from triggering an eruption on an already incapacitated add, which might be redundant or visually confusing.
*   **Forced Explosion Flag**: `mob_fireswornAI` uses `m_bForceExplosion` to distinguish between natural death (casting `SPELL_ADD_ERUPTION`) and death caused by Garr's `SPELL_ERUPTION_TRIGGER` (casting `SPELL_MASSIVE_ERUPTION`). This ensures different visual/effects for the two scenarios.
*   **Enrage Stacking**: Garr's `SPELL_ENRAGE` stacks up to 10 times, triggered by each Firesworn death. The AI doesn't explicitly limit the stack count; it relies on the spell definition.
*   **Event Retry Logic**: Both AIs implement a 1-second retry delay for failed spell casts, preventing event spamming if a cast fails temporarily.
*   **GUID List Management**: `boss_garrAI` maintains a `std::vector<ObjectGuid>` of Firesworns. This list is populated at aggro and pruned when adds die. This ensures Garr only targets existing, non-stunned adds for eruptions.

## Member Reference

*   **boss_garrAI** (ctor): Initializes the AI, retrieves instance data, and calls `Reset`.
*   **Reset**: Clears events and add list; sets instance state to `NOT_STARTED` if alive and not done.
*   **Aggro**: Sets instance state to `IN_PROGRESS`, marks zone in combat, finds Firesworns within 150 yards, stores their GUIDs, applies `SPELL_SEPARATION_ANXIETY`, and schedules events.
*   **JustDied**: Sets instance state to `DONE`.
*   **SpellHit**: If hit by `SPELL_ENRAGE_TRIGGER`, casts `SPELL_ENRAGE` on self.
*   **FireswornJustDied**: Removes the given add GUID from `m_lFiresworn`.
*   **UpdateAI**: Updates event map, calls `UpdateEvents`, and performs melee attacks if a victim exists.
*   **ScheduleCombatEvents**: Schedules Antimagic Pulse (15s), Magma Shackles (10s), and Massive Eruption (6m).
*   **UpdateEvents**: Handles timed events: casts Antimagic Pulse/Magma Shackles with random repeat intervals; triggers Massive Eruption on a random, non-stunned Firesworn every 20s after initial 6m delay.
*   **GetAI_boss_garr**: Factory function returning a new `boss_garrAI`.
*   **mob_fireswornAI** (ctor): Initializes the AI, retrieves instance data, and calls `Reset`.
*   **Reset#2**: Clears force explosion flag and event map.
*   **Aggro#2**: Casts `SPELL_THRASH`, marks zone in combat, and schedules events.
*   **JustDied#2**: Finds Garr, casts `SPELL_ENRAGE_TRIGGER` on him, calls `FireswornJustDied` on Garr's AI, and casts `SPELL_ADD_ERUPTION` if not forced to explode.
*   **SpellHit#2**: If hit by `SPELL_ERUPTION_TRIGGER`, sets `m_bForceExplosion` and casts `SPELL_MASSIVE_ERUPTION`.
*   **UpdateAI#2**: Updates event map, calls `UpdateEvents`, and performs melee attacks if a victim exists.
*   **ScheduleCombatEvents#2**: Schedules Immolate (10s).
*   **UpdateEvents#2**: Casts `SPELL_IMMOLATE` on victim every 20s, with 1s retry on failure.
*   **GetAI_mob_firesworn**: Factory function returning a new `mob_fireswornAI`.
*   **AddSC_boss_garr**: Registers "boss_garr" and "mob_firesworn" scripts with the script manager.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_garr

*Source:* boss_garr.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_garrAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | EventMap/Reset, InstanceData/GetData, InstanceData/SetData, Unit.Main/IsAlive | — | — |
| Aggro | method | Creature.Main/SetInCombatWithZone, CreatureAI/DoCastSpellIfCan, GridSearchers/GetCreatureListWithEntryInGrid#2, InstanceData/GetData, InstanceData/SetData, Map.Main/GetCreature, Object/GetObjectGuid, WorldObject.Object/DeleteLater, WorldObject.Object/GetMap | — | — |
| JustDied | method | InstanceData/SetData | — | — |
| SpellHit | method | CreatureAI/DoCastSpellIfCan | — | — |
| FireswornJustDied | method | — | — | — |
| UpdateAI | method | CreatureAI/DoMeleeAttackIfReady, EventMap/Update, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| ScheduleCombatEvents | method | EventMap/RescheduleEvent#2 | — | — |
| UpdateEvents | method | CreatureAI/DoCastSpellIfCan, EventMap/ExecuteEvent, EventMap/Repeat, Map.Main/GetCreature, ScriptMgr/DoScriptText, shared_Util/urand, SpellCaster/CastSpell#2, Unit.Main/HasAuraType, WorldObject.Object/GetMap | — | — |
| GetAI_boss_garr | function | — | — | — |
| mob_fireswornAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset#2 | method | EventMap/Reset | — | — |
| Aggro#2 | method | Creature.Main/SetInCombatWithZone, CreatureAI/DoCastSpellIfCan | — | — |
| JustDied#2 | method | Creature.Main/AI, CreatureAI/DoCastSpellIfCan, Object/GetObjectGuid, ScriptedInstance/GetSingleCreatureFromStorage, SpellCaster/CastSpell#2, Unit.Main/IsAlive | — | — |
| SpellHit#2 | method | SpellCaster/CastSpell#2 | — | — |
| UpdateAI#2 | method | CreatureAI/DoMeleeAttackIfReady, EventMap/Update, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| ScheduleCombatEvents#2 | method | EventMap/RescheduleEvent#2 | — | — |
| UpdateEvents#2 | method | CreatureAI/DoCastSpellIfCan, EventMap/ExecuteEvent, EventMap/Repeat, Unit.Main/GetVictim | — | — |
| GetAI_mob_firesworn | function | — | — | — |
| AddSC_boss_garr | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
