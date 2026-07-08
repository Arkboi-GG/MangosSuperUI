# winterspring

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# winterspring.cpp

## Purpose & Responsibilities

`winterspring.cpp` implements scripted AI for two NPCs in the Winterspring zone: **Artorius** (entries `14531` and `14535`) and the **Umi Yeti**.

The **Artorius** script manages a multi-stage quest event ("Obtain Cache of Mau'ari"). It handles the transformation of "Artorius the Amiable" (passive, waypoint-moving gossip NPC) into "Artorius the Doombringer" (aggressive combatant). Key responsibilities include:
1.  **State Transformation:** Triggered by player interaction, converting the creature entry, updating movement to idle, and resetting combat timers.
2.  **Strict 1v1 Combat:** Enforcing that only one Hunter can engage the Doombringer. If a non-Hunter aggroes, or if multiple targets are on the threat list, the boss despawns and summons a cleanup mob ("The Cleaner") to attack remaining threats.
3.  **Spell Interactions:** Reacting to Hunter's Serpent Sting with a counter-spell, and casting Demonic Frenzy/Doom during combat.
4.  **Dynamic Respawning:** Adjusting respawn delays based on server population (`BLIZZLIKE_REALM_POPULATION`) upon death, or fixed delays upon event failure.

The **Umi Yeti** script is minimal, despawning the creature when hit by a specific unsummon spell (`17163`).

## Member-by-Member Behavior

### Artorius AI (`npc_artoriusAI`)

#### Initialization & State Management
*   **`npc_artoriusAI` (ctor):** Initializes member variables (`m_bTransform`, `m_uiDespawn_Timer`) and calls `Reset()`.
*   **`Reset`:** Configures state based on creature entry:
    *   **Amiable (`14531`):** Sets 35-minute respawn, teleports to home coords (`7909.71, -4598.67, 710.008`), initializes waypoint movement, sets gossip flags, and resets transformation timers (10s transform, 5s emote).
    *   **Doombringer (`14535`):** Initializes a 20-minute despawn timer if not already set, clears the hunter GUID, and resets spell timers (Demonic Doom: 7.5s, Demonic Frenzy: 5–8s random).
*   **`Transform`:** Called after the transformation delay. Updates the creature entry to Doombringer, sets home position to current location, switches movement to idle, and calls `Reset()` to apply Doombringer settings.

#### Event Flow
*   **`BeginEvent`:** Triggered by `OnScriptEventHappened`. Records the player's GUID, stops movement, removes gossip flags, and sets `m_bTransform` to true to start the transformation sequence.
*   **`OnScriptEventHappened`:** External trigger handler. If the invoker is a player, calls `BeginEvent`.

#### Combat & Failure Logic
*   **`Aggro`:** Validates the aggressor. If the attacker is a Hunter and matches the stored `m_hunterGuid` (or if no hunter is stored yet), it updates the GUID. Otherwise, it immediately calls `DemonDespawn()`, failing the encounter.
*   **`JustDied`:** Resets home position to Amiable spawn. Calculates a 3-hour respawn delay, scaled down proportionally if active sessions exceed `BLIZZLIKE_REALM_POPULATION`. Saves the time.
*   **`DemonDespawn`:** Cleanup routine. Resets home position and sets a 15-minute respawn. If `triggered` is true (failed aggro), it summons `NPC_THE_CLEANER` (`14503`), iterates through the boss's threat list, and assigns aggro/combat state to the Cleaner for all alive targets. Finally, forces the boss to despawn.
*   **`SpellHit`:** If hit by Serpent Sting (IDs `13555` or `25295`), casts `SPELL_STINGING_TRAUMA` (`23299`) and plays a poison emote.

#### Update Loop
*   **`UpdateAI`:**
    *   **Transformation:** Counts down emote timer (roar) and transform timer. Calls `Transform()` when ready.
    *   **Despawn:** If `m_uiDespawn_Timer` expires and the boss is alive/not in combat, calls `DemonDespawn(false)`.
    *   **1v1 Check:** If threat list size > 1, calls `DemonDespawn()`.
    *   **Spells:** Casts `SPELL_DEMONIC_FRENZY` (`23257`) every 15–20s. Casts `SPELL_DEMONIC_DOOM` (`23298`) every 7.5s if within 25 yards of victim.
    *   **Melee:** Calls `DoMeleeAttackIfReady()`.

### Umi Yeti AI (`npc_umi_yetiAI`)

*   **`npc_umi_yetiAI` (ctor):** Calls `Reset()`.
*   **`Reset#2`**, **`MoveInLineOfSight`**, **`UpdateAI#2`**: Empty overrides.
*   **`SpellHit#2`**: If hit by `SPELL_UNSUMMON_YETI` (`17163`), stops movement and forces despawn after 1 second.

### Registration
*   **`GetAI_npc_artorius`**, **`GetAI_npc_umi_yeti`**: Factory functions returning new AI instances.
*   **`AddSC_winterspring`**: Registers "npc_artorius" and "npc_umi_yeti" scripts with `ScriptMgr`.

## Cross-Unit Boundaries

*   **`ScriptedAI` / `CreatureAI`:** Base classes for AI framework, spell casting (`DoCastSpellIfCan`), and melee attacks (`DoMeleeAttackIfReady`).
*   **`Creature.Main`:**
    *   `SetHomePosition`, `SetRespawnDelay`, `SetRespawnTime`, `SaveRespawnTime`: Persist respawn data.
    *   `SetDefaultMovementType`, `GetMotionMaster`: Control movement (waypoint vs. idle).
    *   `UpdateEntry`: Changes creature model/stats during transformation.
    *   `ForcedDespawn`, `SummonCreature`: Lifecycle management.
*   **`Unit.Main`:**
    *   `GetThreatManager`, `getThreatList`: Access threat data for 1v1 validation and aggro transfer.
    *   `SetInCombatWith`, `AddThreat`, `AttackStart`: Manage combat state for the summoned Cleaner.
    *   `GetClass`, `GetObjectGuid`: Validate player identity/class.
    *   `HandleEmote`, `IsWithinDistInMap`: Visuals and range checks.
*   **`World`:** `GetActiveSessionCount` scales respawn time in `JustDied`.
*   **`ScriptMgr`:** `DoScriptText` plays emotes; `RegisterSelf` registers scripts.
*   **`shared_Util`:** `urand` generates random spell intervals.

## Data Model

No direct SQL queries. The unit relies on `Creature` class methods to persist respawn times and positions to the underlying `creature` table via the core engine.

## Notable Implementation Details

1.  **Strict 1v1 Enforcement:** `UpdateAI` checks `getThreatList().size() > 1` and despawns immediately. Combined with `Aggro` checking for `CLASS_HUNTER`, this restricts the encounter to a single Hunter.
2.  **Aggro Transfer:** `DemonDespawn` manually iterates the threat list to assign aggro to the summoned Cleaner, ensuring players remain engaged after the boss despawns.
3.  **Dynamic Respawn:** `JustDied` scales the 3-hour respawn delay inversely with server population (`BLIZZLIKE_REALM_POPULATION`).
4.  **Hardcoded Coordinates:** Home positions are hardcoded floats. Map changes require code updates.
5.  **Transformation Delay:** The transformation is not instant; `UpdateAI` waits 5s for an emote, then 10s before calling `Transform()`.

## Member Reference

*   **`npc_artoriusAI`**: Constructor for Artorius AI, initializing timers and calling `Reset()`.
*   **`Reset`**: Configures creature state (position, movement, respawn, timers) based on Amiable or Doombringer entry.
*   **`Transform`**: Changes creature entry to Doombringer, updates home position, and resets AI state.
*   **`BeginEvent`**: Starts event sequence, recording player GUID and initiating transformation countdown.
*   **`Aggro`**: Validates aggressor; if not correct Hunter, triggers `DemonDespawn`.
*   **`JustDied`**: Sets respawn time (scaled by server population) and saves it.
*   **`DemonDespawn`**: Despawn logic; optionally summons Cleaner mob and transfers aggro from boss to it.
*   **`SpellHit`**: Triggers counter-spell if hit by Serpent Sting.
*   **`UpdateAI`**: Main loop handling transformation timers, despawn timers, 1v1 validation, and spell casting.
*   **`OnScriptEventHappened`**: Entry point for external events, triggering `BeginEvent` if invoked by a player.
*   **`GetAI_npc_artorius`**: Factory function creating `npc_artoriusAI`.
*   **`npc_umi_yetiAI`**: Constructor for Umi Yeti AI.
*   **`Reset#2`**: Empty reset override for Umi Yeti.
*   **`MoveInLineOfSight`**: Empty LOS override for Umi Yeti.
*   **`SpellHit#2`**: Despawns Umi Yeti if hit by `SPELL_UNSUMMON_YETI`.
*   **`UpdateAI#2`**: Empty update override for Umi Yeti.
*   **`GetAI_npc_umi_yeti`**: Factory function creating `npc_umi_yetiAI`.
*   **`AddSC_winterspring`**: Registers both NPC scripts with the game world.

---

<!-- machine-true, projected from graph.json -->

## Map — winterspring

*Source:* winterspring.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| npc_artoriusAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | method | Creature.Main/SetDefaultMovementType, Creature.Main/SetHomePosition, Creature.Main/SetRespawnDelay, Creature.Main/SetRespawnTime, Creature.MotionMaster/GetCurrentMovementGeneratorType, Creature.MotionMaster/Initialize, Object/GetEntry, ObjectGuid/Clear, shared_Util/urand, Unit.Main/GetMotionMaster, Unit.Main/NearTeleportTo, WorldObject.Object/SetUInt32Value | — | — |
| Transform | method | Creature.Main/SetDefaultMovementType, Creature.Main/SetHomePosition, Creature.Main/UpdateEntry, Creature.MotionMaster/Initialize, Unit.Main/GetMotionMaster, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | — | — |
| BeginEvent | method | Creature.MotionMaster/MoveIdle, MotionMaster/Clear, Unit.Main/GetMotionMaster, WorldObject.Object/SetUInt32Value | — | — |
| Aggro | method | Object/GetObjectGuid, ObjectGuid/IsEmpty, ObjectGuid/operator==, Unit.Main/GetClass | — | — |
| JustDied | method | Creature.Main/SaveRespawnTime, Creature.Main/SetHomePosition, Creature.Main/SetRespawnDelay, Creature.Main/SetRespawnTime, World/GetActiveSessionCount | — | — |
| DemonDespawn | method | Creature.Main/AI, Creature.Main/ForcedDespawn, Creature.Main/SaveRespawnTime, Creature.Main/SetHomePosition, Creature.Main/SetRespawnDelay, Creature.Main/SetRespawnTime, CreatureAI/AttackStart, ThreatManager/getThreatList, Unit.Main/AddThreat, Unit.Main/GetThreatManager, Unit.Main/IsAlive, Unit.Main/SetInCombatWith, WorldObject.Object/GetAngle, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SummonCreature#2 | — | — |
| SpellHit | method | CreatureAI/DoCastSpellIfCan, ScriptMgr/DoScriptText | — | — |
| UpdateAI | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, ThreatManager/getThreatList, Unit.Main/GetThreatManager, Unit.Main/GetVictim, Unit.Main/HandleEmote, Unit.Main/IsAlive, Unit.Main/IsInCombat, Unit.Main/SelectHostileTarget, WorldObject.Object/IsWithinDistInMap | — | — |
| OnScriptEventHappened | method | Object/GetObjectGuid, Object/IsPlayer | — | — |
| GetAI_npc_artorius | function | — | — | — |
| npc_umi_yetiAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#2 | method | — | — | — |
| MoveInLineOfSight | method | — | — | — |
| SpellHit#2 | method | Creature.Main/ForcedDespawn, Creature.MotionMaster/MoveIdle, Unit.Main/GetMotionMaster | — | — |
| UpdateAI#2 | method | — | — | — |
| GetAI_npc_umi_yeti | function | — | — | — |
| AddSC_winterspring | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
