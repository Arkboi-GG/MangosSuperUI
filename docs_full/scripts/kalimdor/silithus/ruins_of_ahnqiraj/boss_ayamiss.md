<!-- provenance: failed-members -->
# boss_ayamiss

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_ayamiss

**Purpose & Responsibilities**  
`boss_ayamiss.cpp` implements the scripted artificial intelligence for two creatures in the *Ruins of Ahn'Qiraj* instance: the boss **Ayamiss the Hunter** (`boss_ayamissAI`) and her summoned minion **Hive'Zara Larva** (`mob_zara_larvaAI`). The unit handles Ayamiss’s multi-phase combat rotation, threat manipulation during her “sacrifice” mechanic, periodic summoning of swarms and larvae, and the larva’s waypoint-driven movement toward paralyzed players to trigger a secondary spell. It registers both AI classes with the server’s script manager so they are instantiated when the corresponding creatures spawn.

---

## Member-by-Member Behavior

### `boss_ayamissAI` Construction & State Initialization
The constructor **boss_ayamissAI** initializes the AI by casting the creature’s instance data to `ScriptedInstance`, storing it in `m_pInstance`, and calling **Reset**. This ensures all timers, phase flags, and movement states start in a known baseline before combat begins.

### Reset Logic
**Reset** reinitializes all internal timers to their default intervals (e.g., Stinger Spray at 10 s, Poison Stinger at 5 s, Swarmer summon at 60 s). It clears phase flags (`m_bIsInPhaseTwo`, `m_bIsEnraged`, etc.), resets the sacrifice target GUID and stored aggro value, and configures Ayamiss to fly while disabling walk mode. If combat movement was previously enabled, it disables it to prevent erratic pathing during reset. Crucially, it forces the despawn of any lingering **Hive'Zara Hornet** (`NPC_HIVEZARA_HORNET`) or **Swarmers** (`NPC_HIVEZARA_SWARMER`) within a 300-unit radius by iterating over grid-searched lists and adding alive creatures to the removal queue. Finally, it notifies the instance script that the encounter state is `NOT_STARTED`.

### Combat State Transitions
- **Aggro**: When Ayamiss enters combat, **Aggro** sets the instance data to `IN_PROGRESS`, signaling the raid frame or UI that the boss fight has begun.
- **JustDied**: Upon death, **JustDied** updates the instance data to `DONE`, allowing the instance script to proceed with loot or phase transitions.

### Sacrifice Mechanic Tracking
**SpellHitTarget** intercepts spell hits on Ayamiss. If the spell is **Paralyze** (`SPELL_PARALYZE`) and the caster is a player, it records the player’s GUID in `m_uiSacrificeGuid` and stores their current threat value in `m_fSacrificeAggro`. It then reduces that player’s threat by 100% to remove them from immediate aggro priority. It also snapshots whether Ayamiss is already in Phase Two (`m_bPhaseTwoBeforeTeleport`) to determine later whether to restore threat.

### Summoned Minion Initialization
**JustSummoned** is called when Ayamiss summons a creature (typically a Larva). It immediately places the summoned creature into combat with the zone, ensuring it is recognized as hostile by nearby players and can participate in combat mechanics.

### Main Combat Loop (`UpdateAI`)
**UpdateAI** drives Ayamiss’s behavior each tick:

1. **Initial Relocation**: If not yet relocated and still in Phase One, Ayamiss moves upward 20 units vertically via `MovePoint` and `MonsterMove`, setting `m_bRelocated = true`.
2. **Phase Two Transition**: When health drops to ≤70%, Ayamiss enters Phase Two:
   - Enables combat movement.
   - Chases the current victim.
   - Disables pathfinding (`UNIT_STATE_IGNORE_PATHFINDING`).
   - Resets threat for all players by reducing their threat by 100%.
3. **Enrage**: At ≤20% health, if not already enraged, Ayamiss casts **Frenzy** (`SPELL_FRENZY`) and emits an emote, setting `m_bIsEnraged = true`.
4. **Swarmer Summons**: Every 60 seconds, Ayamiss spawns 20 **Hive'Zara Swarmers** at a fixed location with minor random offsets. Each swarmer is flagged to ignore pathfinding.
5. **Player Sacrifice Cycle**: Every 15 seconds, Ayamiss selects a random attacking target and casts **Paralyze**. If successful, she summons a **Hive'Zara Larva** at one of two predefined locations. The larva is configured to walk (not fly). A 10-second timer starts to potentially restore the paralyzed player’s threat.
6. **Threat Restoration**: After 10 seconds, if the sacrifice target is still alive and Ayamiss was *not* in Phase Two when the paralyze occurred, the original threat value is restored via `addThreatDirectly`. This prevents players from being permanently removed from aggro tables unless the phase transition intervened.
7. **Spell Rotation**:
   - **Phase One**: Casts **Poison Stinger** every ~3 seconds and **Stinger Spray** every 10–15 seconds.
   - **Phase Two**: Drops Poison Stinger; adds **Lash** and **Trash** spells (both every 10–20 seconds) alongside continued Stinger Spray. Also performs melee attacks when ready.

### `mob_zara_larvaAI` Construction & State
The constructor **mob_zara_larvaAI** initializes the larva’s AI and calls **Reset**, which sets the initial activation delay to 2 seconds, clears the victim pointer, resets the waypoint index, and disables combat movement to allow controlled waypoint traversal.

### Larva Movement & Feeding Logic (`UpdateAI`)
**UpdateAI#2** for the larva operates on a 2-second tick cycle. It iterates over all players on the map to find one affected by **Paralyze**. Using a state machine driven by `m_waypoint`, it moves the larva through four predefined coordinates (`LarvaMove` array):
- Waypoint 0 → 1 → 2 → 3: Sequential movement toward the paralyzed player’s general area.
- At Waypoint 0, if a paralyzed player is found, the larva targets them, initiates attack, and assigns massive threat (5 billion) to ensure the player remains focused on the larva.
- At Waypoint 4 (after reaching the final coordinate), if the victim still has the Paralyze aura, the larva casts **Feed** (`SPELL_FEED`) on the victim, which summons a **Hive'Zara Hornet**.

This logic ensures the larva reliably reaches the paralyzed player before triggering the hornet summon, avoiding premature or missed executions.

### AI Factory Functions
- **GetAI_mob_zara_larva**: Returns a new `mob_zara_larvaAI` instance for the given creature.
- **GetAI_boss_ayamiss**: Returns a new `boss_ayamissAI` instance for the given creature.

### Script Registration
**AddSC_boss_ayamiss** creates two `Script` objects: one for `boss_ayamiss` and one for `mob_zara_larva`. Each script’s `GetAI` pointer is set to the corresponding factory function, and `RegisterSelf()` is called to integrate them into the server’s script system. This function is invoked by `ScriptLoader::AddScripts` during server startup.

---

## Cross-Unit Boundaries

- **Instance Data Integration**:  
  `boss_ayamissAI` calls `InstanceData::SetData` in **Reset**, **Aggro**, and **JustDied** to update the encounter state (`NOT_STARTED`, `IN_PROGRESS`, `DONE`). This allows other scripts (e.g., instance-wide events, loot managers) to react to Ayamiss’s status.

- **Threat Management**:  
  **SpellHitTarget** and **UpdateAI** interact with `ThreatManager` methods (`getThreat`, `modifyThreatPercent`, `addThreatDirectly`, `getThreatList`) to manipulate player aggro during the sacrifice mechanic and phase transitions. This ensures proper tanking dynamics and prevents accidental taunts or aggro spikes.

- **Creature Summoning & Despawning**:  
  **Reset** uses `GridSearchers::GetCreatureListWithEntryInGrid` to locate and despawn stray Hornets and Swarmers. **UpdateAI** uses `WorldObject::SummonCreature` to spawn Swarmers and Larvae. These calls interface with the world’s entity management system.

- **Movement & Pathfinding**:  
  Ayamiss and the Larva use `MotionMaster::MovePoint`, `MotionMaster::MoveChase`, and `Unit::MonsterMove` to control positioning. Ayamiss also toggles `UNIT_STATE_IGNORE_PATHFINDING` in Phase Two to bypass complex path calculations, relying instead on direct chase logic.

- **Spell Casting**:  
  Both AIs use `CreatureAI::DoCastSpellIfCan` to cast abilities, respecting cooldowns and conditions. The Larva uses `SpellCaster::CastSpell` directly for **Feed**, bypassing the AI helper for precise control.

- **Script System**:  
  **AddSC_boss_ayamiss** registers the AIs via `Script::RegisterSelf`, which hooks into `ScriptMgr` to make the AIs available globally. This is called by `ScriptLoader::AddScripts` during initialization.

---

## Data Model

This unit does not interact with any database tables. All state is maintained in-memory via creature AI members, instance data, and temporary summon timers. No SQL queries or table references appear in the source.

---

## Notable Implementation Details

- **Threat Snapshotting**:  
  The sacrifice mechanic stores the player’s threat *before* removing it, then restores it after 10 seconds—unless Ayamiss entered Phase Two during that window. This prevents threat restoration if the phase change already reset aggro, avoiding double-dips or stale threat values.

- **Pathfinding Disable in Phase Two**:  
  Ayamiss disables pathfinding upon entering Phase Two (`UNIT_STATE_IGNORE_PATHFINDING`). This is likely because her vertical relocation and chase behavior are simpler than navigating complex terrain, and pathfinding might cause delays or errors during high-mobility phases.

- **Larva Waypoint Precision**:  
  The larva’s movement relies on exact coordinate matches (`GetPositionX() == LarvaMove[n].x`, etc.). Floating-point comparisons here are fragile; if the larva’s position drifts slightly due to network interpolation or physics, the waypoint progression may stall. However, since `MonsterMove` is used (which typically snaps positions), this may be acceptable in practice.

- **Random Offset in Swarmer Spawns**:  
  Swarmers are spawned with `rand() % 10` offsets in X, Y, Z. This uses the C standard library `rand()`, which is not thread-safe and may produce predictable sequences. In a multi-threaded server, this could lead to race conditions or identical spawn patterns across instances. A better approach would use a thread-local or seeded RNG.

- **Hardcoded Timers & Cooldowns**:  
  All spell timers are hardcoded (e.g., `m_uiStingerSpray_Timer = 10000`). While simple, this makes tuning difficult without recompiling. A more flexible design would load these values from configuration or database tables.

- **No Evade Handling**:  
  The script comment notes “evade return to start position missing.” Indeed, **UpdateAI** does not handle the `EVADING` state; if Ayamiss evades (e.g., due to desync or manual reset), she will remain in her last position rather than returning to spawn. This is a known limitation.

- **Massive Threat Assignment**:  
  The larva assigns 5 billion threat to the victim upon targeting. This is an extreme value designed to ensure the player cannot lose aggro to other sources, but it may overflow 32-bit threat counters in some implementations. Verify that the threat system supports 64-bit values.

---

## Member Reference

**boss_ayamissAI** (ctor): Initializes the AI by retrieving instance data and calling **Reset**.

**Reset**: Reinitializes timers, phase flags, and movement states; despawns stray minions; sets instance state to `NOT_STARTED`.

**Aggro**: Sets instance state to `IN_PROGRESS` when combat begins.

**JustDied**: Sets instance state to `DONE` upon death.

**SpellHitTarget**: Records paralyzed player’s GUID and threat, removes 100% threat, and snapshots phase state for later restoration.

**JustSummoned**: Places summoned creatures into combat with the zone.

**UpdateAI**: Drives Ayamiss’s combat loop: relocates vertically, transitions to Phase Two at 70% HP, enrages at 20% HP, summons Swarmers and Larvae, manages sacrifice threat restoration, and rotates spells/melee attacks based on phase.

**mob_zara_larvaAI** (ctor): Initializes larva AI and calls **Reset**.

**Reset#2**: Sets initial activation delay, clears victim, resets waypoint, disables combat movement.

**UpdateAI#2**: Moves larva through waypoints toward paralyzed players; casts **Feed** at final waypoint to summon a Hornet.

**GetAI_mob_zara_larva**: Factory function returning a new `mob_zara_larvaAI` instance.

**GetAI_boss_ayamiss**: Factory function returning a new `boss_ayamissAI` instance.

**AddSC_boss_ayamiss**: Registers both AI scripts with the server’s script manager.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_ayamiss

*Source:* boss_ayamiss.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_ayamissAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | CreatureAI/IsCombatMovementEnabled, CreatureAI/SetCombatMovement, GridSearchers/GetCreatureListWithEntryInGrid#2, InstanceData/SetData, ObjectGuid/Clear, Unit.Main/IsAlive, Unit.Main/SetFly, Unit.Main/SetWalk, WorldObject.Object/AddObjectToRemoveList | — | — |
| Aggro | method | InstanceData/SetData | — | — |
| JustDied | method | InstanceData/SetData | — | — |
| SpellHitTarget | method | Object/GetObjectGuid, Object/GetTypeId, ThreatManager/getThreat, ThreatManager/modifyThreatPercent#2, Unit.Main/GetThreatManager | — | — |
| JustSummoned | method | Creature.Main/SetInCombatWithZone | — | — |
| UpdateAI | method | Creature.Main/SelectAttackingTarget, Creature.MotionMaster/MoveChase, Creature.MotionMaster/MovePoint, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, CreatureAI/SetCombatMovement, GridSearchers/GetCreatureListWithEntryInGrid#2, Map.Main/GetPlayer, Object/ToPlayer, ObjectGuid/Clear, ScriptMgr/DoScriptText, shared_Util/urand, ThreatManager/addThreatDirectly, ThreatManager/getThreatList, ThreatManager/modifyThreatPercent#2, Unit.Main/AddUnitState, Unit.Main/AttackerStateUpdate, Unit.Main/GetHealthPercent, Unit.Main/GetMotionMaster, Unit.Main/GetThreatManager, Unit.Main/GetVictim, Unit.Main/IsAlive, Unit.Main/MonsterMove, Unit.Main/SelectHostileTarget, Unit.Main/SetFly, Unit.Main/SetWalk, WorldObject.Object/GetMap, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SummonCreature#2 | — | — |
| mob_zara_larvaAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#2 | method | CreatureAI/SetCombatMovement | — | — |
| UpdateAI#2 | method | Creature.Main/AI, CreatureAI/AttackStart, Map.Main/GetPlayers, SpellCaster/CastSpell#2, ThreatManager/addThreat#3, Unit.Main/GetThreatManager, Unit.Main/HasAura#2, Unit.Main/MonsterMove, WorldObject.Object/GetMap, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | — | — |
| GetAI_mob_zara_larva | function | — | — | — |
| GetAI_boss_ayamiss | function | — | — | — |
| AddSC_boss_ayamiss | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |

---

<!-- verify: failed-members | invented: Feed -->
