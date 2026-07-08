# scourge_invasion

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# scourge_invasion

## Purpose & Responsibilities

The `scourge_invasion` unit implements the server-side logic for the **Scourge Invasion** world event in World of Warcraft (Wrath of the Lich King era). This is a persistent, server-wide event where the Scourge periodically attacks specific zones (Winterspring, Tanaris, Azshara, Blasted Lands, Eastern Plaguelands, Burning Steppes) and capital cities (Stormwind, Undercity).

The unit manages three distinct phases of the event:
1.  **Zone Invasions:** The lifecycle of a "Necropolis" flying over a zone, spawning a "Necrotic Shard" on the ground, which is defended by Cultists and Minions. Players must kill the Shard to win the battle.
2.  **City Invasions:** Periodic attacks on Stormwind and Undercity led by a "Pallid Horror" boss, accompanied by "Flameshockers."
3.  **Event Tracking & UI:** Managing global counters for victories and remaining invasions, displayed to players via the Argent Emissary gossip menu.

It handles complex AI behaviors for various NPCs (Shards, Cultists, Minions, Bosses), spell-based communication chains between invisible helper NPCs (Proxies, Relays), and integration with the server's Game Event system to track progress.

## Member-by-Member Behavior

### Utility Functions

These free functions provide shared logic for AI classes and event triggers.

*   **GetCampType**: Determines if a creature (typically a Necrotic Shard) has an active "Camp Type" aura (Ghost/Skeleton, Ghost/Ghoul, or Ghoul/Skeleton). It checks for three specific spell IDs.
*   **IsGuardOrBoss**: Checks if a unit is a specific guard or boss NPC (e.g., Royal Dreadguard, Bolvar, Sylvanas). Used to trigger specific aggro behaviors or dialogue.
*   **SelectRandomFlameshockerSpawnTarget**: Finds a valid target for a Flameshocker to spawn near. It searches for unfriendly units within a radius, filters out non-creatures, units that cannot summon guards, units in different zones, invalid attack targets, and units already near another Flameshocker. It returns a random valid target from the filtered list.
*   **ChangeZoneEventStatus**: Starts or stops the specific `GameEvent` associated with the zone the provided creature (`pMouth`) is in. It maps Zone IDs to specific Game Event IDs (e.g., Winterspring -> `GAME_EVENT_SCOURGE_INVASION_WINTERSPRING`).
*   **DespawnEventDoodads**: Removes visual and functional "doodads" (GameObjects like fires, skull piles, summoner shields) and "Minion Finder" creatures within 60 yards of a shard. Used when a camp is cleared.
*   **DespawnNecropolis**: Despawns the visual Necropolis GameObjects (Tiny to Huge variants) within `ATTACK_DISTANCE` of a unit.
*   **SummonCultists**: Summons four Cultist Engineers around a "Summon Circle" GameObject. It calculates positions in a circle based on the circle's orientation and height. It triggers the `OnScriptEventHappened` script event on each summoned cultist to initialize their behavior.
*   **DespawnCultists**: Forcefully despawns all Cultist Engineers within `INSPECT_DISTANCE` of a unit.
*   **DespawnShadowsOfDoom**: Forcefully despawns "Shadow of Doom" creatures within 200 yards if they are alive and not in combat.
*   **HasMinion**: Counts the number of alive minions (Shocktroopers, Berserkers, Soldiers, etc.) within `ATTACK_DISTANCE` of a summoner.
*   **UncommonMinionspawner**: Determines if a "Rare" minion should spawn instead of a common one. It checks if any rare minions are already present. If not, it rolls a 1 in 217 chance (based on sniffed data) to return `true`.
*   **GetFindersAmount**: Counts the number of "Minion Finder" creatures within 60 yards of a shard.

### GameObject AIs

*   **GoCircle**: A simple AI for the Summon Circle GameObject. On construction, it casts `SPELL_CREATE_CRYSTAL` on itself to spawn the Necrotic Shard.
*   **GoNecropolis**: Sets the Necropolis GameObject to be active and visible up to 3000 yards.

### Creature AIs

#### Mouth of Kel'Thuzad (`MouthAI`)
*   **ctor**: Schedules a random yell event every 2.5 minutes to 1 hour.
*   **Reset#2**: No-op.
*   **OnScriptEventHappened**: Handles two events:
    *   `EVENT_MOUTH_OF_KELTHUZAD_ZONE_START`: Starts the zone's game event, sets weather to storm, and plays a start yell.
    *   `EVENT_MOUTH_OF_KELTHUZAD_ZONE_STOP`: Plays an end yell, stops the game event, clears weather, and removes the mouth from the world.
*   **UpdateAI#2**: Executes scheduled events. Currently only handles the random yell, rescheduling it after execution.
*   **GetAI_Mouth**: Factory function.

#### Necropolis (`NecropolisAI`)
*   **ctor**: Sets visibility and active state.
*   **Reset#3**: No-op.
*   **SpellHit**: If hit by `SPELL_COMMUNIQUE_PROXY_TO_NECROPOLIS` and not already timed, applies `SPELL_COMMUNIQUE_TIMER_NECROPOLIS`.
*   **UpdateAI#3**: No-op.
*   **GetAI_Necropolis**: Factory function.

#### Necropolis Health (`NecropolisHealthAI`)
This invisible NPC tracks the health of the Necropolis. It dies after 3 "zaps" (one for each Shard killed).
*   **ctor**: Sets visibility. Initializes `m_zapped` counter.
*   **Reset#4**: No-op.
*   **SpellHit#2**:
    *   If hit by `SPELL_COMMUNIQUE_CAMP_TO_RELAY_DEATH`, casts `SPELL_ZAP_NECROPOLIS` on itself.
    *   If hit by `SPELL_ZAP_NECROPOLIS`, increments `m_zapped`. If `m_zapped >= 3`, kills itself.
*   **JustDied**:
    *   Finds the nearest `NPC_NECROPOLIS` and casts `SPELL_DESPAWNER_OTHER` on it.
    *   Decrements the saved variable for the current zone's remaining invasions (`VARIABLE_SI_*_REMAINING`) by 1.
*   **SpellHitTarget**: If `SPELL_DESPAWNER_OTHER` hits the Necropolis NPC, it despawns the Necropolis GameObjects, removes the Necropolis NPC, and removes itself.
*   **UpdateAI#4**: No-op.
*   **GetAI_NecropolisHealth**: Factory function.

#### Necropolis Proxy (`NecropolisProxyAI`)
An invisible relay node in the spell chain.
*   **ctor**: Sets visibility/active state. Calls `Reset`.
*   **Reset#5**: No-op.
*   **SpellHit#3**:
    *   `SPELL_COMMUNIQUE_NECROPOLIS_TO_PROXIES`: Casts `SPELL_COMMUNIQUE_PROXY_TO_RELAY` on self.
    *   `SPELL_COMMUNIQUE_RELAY_TO_PROXY`: Casts `SPELL_COMMUNIQUE_PROXY_TO_NECROPOLIS` on self.
    *   `SPELL_COMMUNIQUE_CAMP_TO_RELAY_DEATH`: Finds nearest `NPC_NECROPOLIS_HEALTH` and casts the death spell on it.
*   **SpellHitTarget#2**: If hit by `SPELL_COMMUNIQUE_CAMP_TO_RELAY_DEATH`, removes itself from the world.
*   **UpdateAI#5**: No-op.
*   **GetAI_NecropolisProxy**: Factory function.

#### Necropolis Relay (`NecropolisRelayAI`)
Another invisible relay node.
*   **ctor**: Sets visibility/active state. Calls `Reset`.
*   **Reset#6**: No-op.
*   **SpellHit#4**:
    *   `SPELL_COMMUNIQUE_PROXY_TO_RELAY`: Casts `SPELL_COMMUNIQUE_RELAY_TO_CAMP` on self.
    *   `SPELL_COMMUNIQUE_CAMP_TO_RELAY`: Casts `SPELL_COMMUNIQUE_RELAY_TO_PROXY` on self.
    *   `SPELL_COMMUNIQUE_CAMP_TO_RELAY_DEATH`: Finds nearest `NPC_NECROPOLIS_PROXY` and casts the death spell on it.
*   **SpellHitTarget#3**: If hit by `SPELL_COMMUNIQUE_CAMP_TO_RELAY_DEATH`, removes itself from the world.
*   **UpdateAI#6**: No-op.
*   **GetAI_NecropolisRelay**: Factory function.

#### Necrotic Shard (`NecroticShard`)
The primary objective of the zone invasion.
*   **ctor**:
    *   If `NPC_DAMAGED_NECROTIC_SHARD`: Counts finders, schedules minion spawning (small) and buttress (cultist respawn) events.
    *   If `NPC_NECROTIC_SHARD`: Removes any other shards in contact distance (prevents duplicates).
*   **Reset#7**: No-op.
*   **SpellHit#5**:
    *   `SPELL_ZAP_CRYSTAL_CORPSE`: Deals 25% max health damage to self.
    *   `SPELL_COMMUNIQUE_RELAY_TO_CAMP`: Casts `SPELL_CAMP_RECEIVES_COMMUNIQUE` on self.
    *   `SPELL_CHOOSE_CAMP_TYPE`: Picks a random camp type aura and casts it.
    *   `SPELL_CAMP_RECEIVES_COMMUNIQUE`: If no camp type is set and it's a healthy shard, chooses a type and starts minion spawning.
    *   `SPELL_FIND_CAMP_TYPE`: If minion count is below finder count, allows the caster (Finder) to spawn a minion trap based on the shard's camp type.
*   **SpellHitTarget#4**: If `NPC_DAMAGED_NECROTIC_SHARD` is hit by `SPELL_COMMUNIQUE_CAMP_TO_RELAY_DEATH`, it removes itself.
*   **DamageTaken**: Blocks damage from units not sharing the same faction template (only minions/shard can hurt it).
*   **HealedBy**: Blocks all healing.
*   **JustDied#2**:
    *   If `NPC_NECROTIC_SHARD`: Summons `NPC_DAMAGED_NECROTIC_SHARD` at its location, transfers the camp type, and removes the original.
    *   If `NPC_DAMAGED_NECROTIC_SHARD`: Casts `SPELL_SOUL_REVIVAL` (buff), sends death signal to nearest Relay, despawns cultists and doodads.
*   **UpdateAI#7**:
    *   `EVENT_SHARD_MINION_SPAWNER_SMALL`: Sorts finders by distance. Spawns up to 3 minions via finders that don't already have minions. Finders disappear and respawn in 150-200s.
    *   `EVENT_SHARD_MINION_SPAWNER_BUTTRESS`: Restores full health, despawns Shadows of Doom, and respawns Cultists. Reschedules hourly.
*   **GetAI_necroticShard**: Factory function.

#### Minion Spawner (`MinionspawnerAI`)
Invisible NPC that spawns minions.
*   **ctor**: Schedules first spawn in 2s.
*   **Reset**: No-op.
*   **UpdateAI**:
    *   `EVENT_SPAWNER_SUMMON_MINION`: Determines minion entry based on spawner type and `UncommonMinionspawner` roll. Summons the minion, sets wander distance, and plays spawn spell.
*   **GetAI_Minionspawner**: Factory function.

#### Cultist Engineer (`npc_cultist_engineer`)
Defends the Damaged Shard.
*   **ctor**: Resets events.
*   **Reset#10**: No-op.
*   **JustDied#5**:
    *   Damages the nearest Damaged Shard.
    *   Removes the channeling aura from the Shard.
    *   Deletes the nearest Summoner Shield GameObject.
*   **UpdateAI#10**:
    *   `EVENT_CULTIST_CHANNELING`: Starts channeling `SPELL_BUTTRESS_CHANNEL` on the nearest Damaged Shard. If all 4 are channeling, the Shard gets the aura.
*   **OnScriptEventHappened#3**:
    *   If event is 7166 (Player interaction): Summons `Shadow of Doom` for the player, consumes 8 Necrotic Runes, plays spells, and commits suicide.
    *   If event is `NPC_CULTIST_ENGINEER` (Spawn): Sets corpse delay, creates shield, plays spawn spell, schedules channeling.
*   **GetAI_npc_cultist_engineer**: Factory function.

#### Scourge Minion (`ScourgeMinion`)
Handles Minions, Rares, and Shadow of Doom.
*   **ctor**: Resets events. Calls `Reset`.
*   **Reset#9**: Schedules abilities based on entry (Mindflay/Fear for Shadow of Doom, Touch for Flameshocker).
*   **OnScriptEventHappened#2**:
    *   `NPC_SHADOW_OF_DOOM`: Schedules attack start (remove immunity) in 5s, plays intro text/spell.
    *   `NPC_FLAMESHOCKER`: Schedules despawn check in 60s.
*   **JustDied#4**:
    *   `NPC_SHADOW_OF_DOOM`: Casts `SPELL_ZAP_CRYSTAL_CORPSE` on self (damages shard).
    *   `NPC_FLAMESHOCKER`: Casts `SPELL_FLAMESHOCKERS_REVENGE`.
*   **SpellHit#6**: If hit by `SPELL_SPIRIT_SPAWN_OUT`, despawns in 3s.
*   **MoveInLineOfSight#2**: If Flameshocker, aggroes guards/bosses in line of sight.
*   **UpdateAI#9**:
    *   Executes scheduled abilities (Mindflay, Fear, Touch, Despawn).
    *   `EVENT_DOOM_START_ATTACK`: Removes player immunity flag. Aggroes summoner if in LOS.
    *   Combat: Casts `SPELL_SCOURGE_STRIKE` (instakill) on nearby non-player/non-pet enemies. Performs melee attacks.
*   **GetAI_ScourgeMinion**: Factory function.

#### Argent Emissary Gossip
*   **GossipSelect_npc_argent_emissary**: Handles gossip actions. Displays menus for general info, zone status, and victory counts. Uses `sObjectMgr.GetSavedVariable` to determine if a zone is currently under attack.
*   **GossipHello_npc_argent_emissary**: Sends current world states (victory count, remaining invasions per zone) to the player and opens the main gossip menu.

#### Pallid Horror (`PallidHorrorAI`)
Boss for City Invasions.
*   **ctor**:
    *   Summons 5-9 Flameshockers in a circle if at full health. Tracks their GUIDs.
    *   Schedules yells, damage spells, and Flameshocker summons.
*   **Reset#8**: Applies `SPELL_AURA_OF_FEAR`.
*   **MoveInLineOfSight**: Aggroes guards/bosses.
*   **JustDied#3**:
    *   Plays dialogue with Bolvar or Sylvanas if nearby.
    *   Kills all tracked Flameshockers.
    *   Summons a Cracked/Faint Necrotic Crystal (quest item).
    *   Removes fear aura.
    *   Calculates next attack time (45-60 mins) and saves it to `VARIABLE_SI_UNDERCITY_TIME` or `VARIABLE_SI_STORMWIND_TIME`. Logs the event.
*   **SummonedCreatureJustDied**: Removes GUIDs from tracking set when a summoned Flameshocker dies.
*   **SummonedCreatureDespawn**: Removes GUIDs from tracking set when a summoned Flameshocker despawns.
*   **OnRemoveFromWorld**: Removes all tracked Flameshockers.
*   **UpdateAI#8**:
    *   Executes yells, damage spells, and Flameshocker summons (up to 30 total).
    *   Combat: Melee attacks.
*   **GetAI_PallidHorrorAI**: Factory function.

### Script Registration

*   **AddSC_scourge_invasion**: Registers all AI scripts, gossip scripts, and GameObject scripts with the `ScriptMgr`.

## Cross-Unit Boundaries

*   **GameEventMgr**: `ChangeZoneEventStatus` calls `IsActiveEvent`, `StartEvent`, and `StopEvent` to manage the global state of zone invasions.
*   **ObjectMgr**: `NecropolisHealthAI::JustDied` and `GossipHello_npc_argent_emissary` call `GetSavedVariable` and `SetSavedVariable` to persist event progress (remaining invasions, victory counts, next attack times).
*   **ScriptMgr**: `MouthAI` and `PallidHorrorAI` call `DoScriptText` to broadcast zone-wide yells.
*   **GridSearchers**: Multiple utility functions and AIs use `GetCreatureListWithEntryInGrid` and `GetGameObjectListWithEntryInGrid` to find nearby entities for spawning, despawning, or targeting.
*   **EventMap**: All AI classes use `EventMap` to schedule and execute periodic abilities and events.
*   **CreatureAI/GameObjectAI**: Base class methods like `DoCastSpellIfCan`, `DoMeleeAttackIfReady`, `AttackStart`, and `OnScriptEventHappened` are used extensively.
*   **WorldObject/Object**: Standard methods for positioning, summoning, despawning, and checking validity are used throughout.
*   **shared_Util**: `urand` is used for random number generation in timers, spawn chances, and target selection.

## Data Model

This unit does not interact with SQL tables directly. It relies on the `ObjectMgr`'s saved variables system (likely backed by a `variables` table or similar mechanism in the database, but accessed via C++ API) to store:
*   `VARIABLE_SI_*_REMAINING`: Count of remaining Necropolises for each zone.
*   `VARIABLE_SI_ATTACK_COUNT`: Total victories.
*   `VARIABLE_SI_*_TIME`: Timestamps for the next city attack.

No direct SQL queries are present in this unit.

## Notable Implementation Details

*   **Spell Chain Communication**: The Necropolis, Proxies, Relays, and Shards communicate via a chain of spells (`SPELL_COMMUNIQUE_*`). This is a workaround for lack of direct AI-to-AI messaging, using spell hits to trigger logic in `SpellHit` handlers.
*   **Damaged vs. Healthy Shard**: The `NecroticShard` AI behaves differently depending on whether it is the initial `NPC_NECROTIC_SHARD` or the `NPC_DAMAGED_NECROTIC_SHARD` summoned upon the first's death. The damaged shard spawns Cultists and has a different death sequence.
*   **Minion Finder Logic**: Minions are not spawned directly by the Shard. Instead, "Minion Finder" NPCs are placed around the camp. The Shard's `UpdateAI` triggers these Finders to spawn minions via `SPELL_FIND_CAMP_TYPE`. This mimics the client-side object activation seen in sniffs.
*   **Rare Spawn Chance**: The `UncommonMinionspawner` function uses a hardcoded 1/217 chance for rare minions, based on sniffed data.
*   **City Attack Timing**: The time until the next city attack is calculated in `PallidHorrorAI::JustDied` using `time()` and stored as a Unix timestamp in saved variables.
*   **Flameshocker Targeting**: `SelectRandomFlameshockerSpawnTarget` ensures Flameshockers don't spawn on top of each other or on invalid targets, preventing aggro issues or visual glitches.
*   **Immunity Flags**: `Shadow of Doom` is initially immune to players (`UNIT_FLAG_IMMUNE_TO_PLAYER`) for 5 seconds after spawning, allowing it to play its intro animation safely.
*   **Hardcoded Positions**: `SummonCultists` uses hardcoded offsets (6.95f, 6.75f, 5.0f) relative to the Summon Circle to place Cultists, acknowledging that Blizzard's original positions were inconsistent.

## Member Reference

**GetCampType**: Inline function that checks if a creature has one of three specific camp-type auras.
**IsGuardOrBoss**: Inline function that checks if a unit's entry matches a list of guard or boss NPCs.
**SelectRandomFlameshockerSpawnTarget**: Finds a random valid target for a Flameshocker to spawn near, filtering out invalid or already-targeted units.
**ChangeZoneEventStatus**: Starts or stops the GameEvent associated with the zone of the provided creature.
**DespawnEventDoodads**: Removes visual doodads and Minion Finder creatures near a shard.
**DespawnNecropolis**: Despawns Necropolis GameObjects near a unit.
**SummonCultists**: Summons four Cultist Engineers around a Summon Circle, initializing their AI.
**DespawnCultists**: Forcefully despawns Cultist Engineers near a unit.
**DespawnShadowsOfDoom**: Forcefully despawns non-combatting Shadows of Doom near a unit.
**HasMinion**: Counts alive minions near a summoner.
**UncommonMinionspawner**: Rolls a 1/217 chance to spawn a rare minion, if none are present.
**GetFindersAmount**: Counts Minion Finder creatures near a shard.
**GoCircle**: Constructor for `GoCircle` AI; casts spell to spawn shard.
**GetAI_GoCircle**: Factory function for `GoCircle` AI.
**GoNecropolis**: Constructor for `GoNecropolis` AI; sets visibility.
**GetAI_GoNecropolis**: Factory function for `GoNecropolis` AI.
**MouthAI**: Constructor for `MouthAI`; schedules random yells.
**Reset#2**: No-op reset for `MouthAI`.
**OnScriptEventHappened**: Handles zone start/stop events for `MouthAI`, triggering game events and weather changes.
**UpdateAI#2**: Executes scheduled yells for `MouthAI`.
**GetAI_Mouth**: Factory function for `MouthAI`.
**NecropolisAI**: Constructor for `NecropolisAI`; sets visibility.
**Reset#3**: No-op reset for `NecropolisAI`.
**SpellHit**: Applies timer aura if hit by communique spell in `NecropolisAI`.
**UpdateAI#3**: No-op update for `NecropolisAI`.
**GetAI_Necropolis**: Factory function for `NecropolisAI`.
**NecropolisHealthAI**: Constructor for `NecropolisHealthAI`; sets visibility.
**Reset#4**: No-op reset for `NecropolisHealthAI`.
**SpellHit#2**: Increments zap counter; kills self if zapped 3 times in `NecropolisHealthAI`.
**JustDied**: Despawns Necropolis and decrements zone invasion counter in `NecropolisHealthAI`.
**SpellHitTarget**: Despawns Necropolis GameObjects and self in `NecropolisHealthAI`.
**UpdateAI#4**: No-op update for `NecropolisHealthAI`.
**GetAI_NecropolisHealth**: Factory function for `NecropolisHealthAI`.
**NecropolisProxyAI**: Constructor for `NecropolisProxyAI`; sets visibility.
**Reset#5**: No-op reset for `NecropolisProxyAI`.
**SpellHit#3**: Relays communique spells in `NecropolisProxyAI`.
**SpellHitTarget#2**: Removes self if hit by death spell in `NecropolisProxyAI`.
**UpdateAI#5**: No-op update for `NecropolisProxyAI`.
**GetAI_NecropolisProxy**: Factory function for `NecropolisProxyAI`.
**NecropolisRelayAI**: Constructor for `NecropolisRelayAI`; sets visibility.
**Reset#6**: No-op reset for `NecropolisRelayAI`.
**SpellHit#4**: Relays communique spells in `NecropolisRelayAI`.
**SpellHitTarget#3**: Removes self if hit by death spell in `NecropolisRelayAI`.
**UpdateAI#6**: No-op update for `NecropolisRelayAI`.
**GetAI_NecropolisRelay**: Factory function for `NecropolisRelayAI`.
**NecroticShard**: Constructor for `NecroticShard`; initializes events and removes duplicates.
**Reset#7**: No-op reset for `NecroticShard`.
**SpellHit#5**: Handles damage, communique, camp type selection, and minion spawning triggers in `NecroticShard`.
**SpellHitTarget#4**: Removes damaged shard if hit by death spell in `NecroticShard`.
**DamageTaken**: Blocks damage from non-faction-matching units in `NecroticShard`.
**HealedBy**: Blocks healing in `NecroticShard`.
**JustDied#2**: Transforms healthy shard to damaged, or cleans up camp on damaged shard death in `NecroticShard`.
**UpdateAI#7**: Manages minion spawning via finders and hourly cultist respawn in `NecroticShard`.
**GetAI_necroticShard**: Factory function for `NecroticShard`.
**MinionspawnerAI**: Constructor for `MinionspawnerAI`; schedules first spawn.
**Reset**: No-op reset for `MinionspawnerAI`.
**UpdateAI**: Spawns minions based on spawner type and rarity roll in `MinionspawnerAI`.
**GetAI_Minionspawner**: Factory function for `MinionspawnerAI`.
**npc_cultist_engineer**: Constructor for `npc_cultist_engineer`; resets events.
**Reset#10**: No-op reset for `npc_cultist_engineer`.
**JustDied#5**: Damages shard, removes channeling, and deletes shield on cultist death in `npc_cultist_engineer`.
**UpdateAI#10**: Starts channeling buttress spell on shard in `npc_cultist_engineer`.
**OnScriptEventHappened#3**: Handles player summoning of Shadow of Doom or initial spawn setup in `npc_cultist_engineer`.
**GetAI_npc_cultist_engineer**: Factory function for `npc_cultist_engineer`.
**ScourgeMinion**: Constructor for `ScourgeMinion`; resets events.
**Reset#9**: Schedules abilities based on minion type in `ScourgeMinion`.
**OnScriptEventHappened

---

<!-- machine-true, projected from graph.json -->

## Map — scourge_invasion

*Source:* scourge_invasion.cpp, scourge_invasion.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GetCampType | function | Unit.Main/HasAura#2 | — | — |
| IsGuardOrBoss | function | Object/GetEntry | — | — |
| SelectRandomFlameshockerSpawnTarget | function | AnyUnfriendlyUnitInObjectRangeCheck/AnyUnfriendlyUnitInObjectRangeCheck, Creature.Main/CanSummonGuards, Object/IsCreature, Object/ToCreature, shared_Util/urand, WorldObject.Object/FindNearestCreature, WorldObject.Object/GetZoneId, WorldObject.Object/IsValidAttackTarget | — | — |
| ChangeZoneEventStatus | function | GameEventMgr.Main/IsActiveEvent, GameEventMgr.Main/StartEvent, GameEventMgr.Main/StopEvent, WorldObject.Object/GetZoneId | — | — |
| DespawnEventDoodads | function | Creature.Main/RemoveFromWorld, GameObject/RemoveFromWorld, GridSearchers/GetCreatureListWithEntryInGrid#2, GridSearchers/GetGameObjectListWithEntryInGrid | — | — |
| DespawnNecropolis | function | GameObject/Despawn, GridSearchers/GetGameObjectListWithEntryInGrid | — | — |
| SummonCultists | function | Creature.Main/AI, CreatureAI/OnScriptEventHappened, GameObject/Despawn, GridSearchers/GetGameObjectListWithEntryInGrid#2, WorldObject.Object/FindNearestGameObject, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SummonCreature#2, WorldObject.Object/UpdateGroundPositionZ | — | — |
| DespawnCultists | function | Creature.Main/ForcedDespawn, GridSearchers/GetCreatureListWithEntryInGrid#2 | — | — |
| DespawnShadowsOfDoom | function | Creature.Main/ForcedDespawn, GridSearchers/GetCreatureListWithEntryInGrid#2, Unit.Main/IsAlive, Unit.Main/IsInCombat | — | — |
| HasMinion | function | GridSearchers/GetCreatureListWithEntryInGrid, Unit.Main/IsAlive | — | — |
| UncommonMinionspawner | function | GridSearchers/GetCreatureListWithEntryInGrid, shared_Util/urand | — | — |
| GetFindersAmount | function | GridSearchers/GetCreatureListWithEntryInGrid#2 | — | — |
| GoCircle | ctor | GameObjectAI/GameObjectAI, SpellCaster/CastSpell#2 | — | — |
| GetAI_GoCircle | function | — | — | — |
| GoNecropolis | ctor | GameObjectAI/GameObjectAI, WorldObject.Object/SetActiveObjectState, WorldObject.Object/SetVisibilityModifier | — | — |
| GetAI_GoNecropolis | function | — | — | — |
| MouthAI | ctor | EventMap/Reset, EventMap/ScheduleEvent#3, ScriptedAI/ScriptedAI, shared_Util/urand | — | — |
| Reset#2 | method | — | — | — |
| OnScriptEventHappened | method | Creature.Main/RemoveFromWorld, Map.Main/SetWeather, ScriptMgr/DoScriptText, WorldObject.Object/GetMap, WorldObject.Object/GetZoneId | — | — |
| UpdateAI#2 | method | EventMap/ExecuteEvent, EventMap/ScheduleEvent#3, EventMap/Update, ScriptMgr/DoScriptText, shared_Util/urand | — | — |
| GetAI_Mouth | function | — | — | — |
| NecropolisAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/SetActiveObjectState, WorldObject.Object/SetVisibilityModifier | — | — |
| Reset#3 | method | — | — | — |
| SpellHit | method | Unit.Main/AddAura, Unit.Main/HasAura#2 | — | — |
| UpdateAI#3 | method | — | — | — |
| GetAI_Necropolis | function | — | — | — |
| NecropolisHealthAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/SetVisibilityModifier | — | — |
| Reset#4 | method | — | — | — |
| SpellHit#2 | method | SpellCaster/CastSpell#2, Unit.Main/DoKillUnit | — | — |
| JustDied | method | ObjectMgr/GetSavedVariable, ObjectMgr/SetSavedVariable, SpellCaster/CastSpell#2, WorldObject.Object/FindNearestCreature, WorldObject.Object/GetZoneId | — | — |
| SpellHitTarget | method | Creature.Main/RemoveFromWorld, Object/GetEntry, Object/ToCreature | — | — |
| UpdateAI#4 | method | — | — | — |
| GetAI_NecropolisHealth | function | — | — | — |
| NecropolisProxyAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/SetActiveObjectState, WorldObject.Object/SetVisibilityModifier | — | — |
| Reset#5 | method | — | — | — |
| SpellHit#3 | method | SpellCaster/CastSpell#2, WorldObject.Object/FindNearestCreature | — | — |
| SpellHitTarget#2 | method | Creature.Main/RemoveFromWorld | — | — |
| UpdateAI#5 | method | — | — | — |
| GetAI_NecropolisProxy | function | — | — | — |
| NecropolisRelayAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/SetActiveObjectState, WorldObject.Object/SetVisibilityModifier | — | — |
| Reset#6 | method | — | — | — |
| SpellHit#4 | method | SpellCaster/CastSpell#2, WorldObject.Object/FindNearestCreature | — | — |
| SpellHitTarget#3 | method | Creature.Main/RemoveFromWorld | — | — |
| UpdateAI#6 | method | — | — | — |
| GetAI_NecropolisRelay | function | — | — | — |
| NecroticShard | ctor | Creature.Main/RemoveFromWorld, EventMap/Reset, EventMap/ScheduleEvent#3, GridSearchers/GetCreatureListWithEntryInGrid, Object/GetEntry, ScriptedAI/ScriptedAI, WorldObject.Object/SetActiveObjectState, WorldObject.Object/SetVisibilityModifier | — | — |
| Reset#7 | method | — | — | — |
| SpellHit#5 | method | EventMap/ScheduleEvent#3, Object/GetEntry, SpellCaster/CastSpell#2, Unit.Main/DealDamage, Unit.Main/GetMaxHealth, Unit.Main/HasAura#2 | — | — |
| SpellHitTarget#4 | method | Creature.Main/RemoveFromWorld, Object/GetEntry | — | — |
| DamageTaken | method | Unit.Main/GetFactionTemplateId | — | — |
| HealedBy | method | — | — | — |
| JustDied#2 | method | Creature.Main/RemoveFromWorld, Object/GetEntry, SpellCaster/CastSpell#2, WorldObject.Object/FindNearestCreature, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SummonCreature#2 | — | — |
| UpdateAI#7 | method | Creature.Main/AI, Creature.Main/DisappearAndDie, Creature.Main/SetRespawnDelay, CreatureAI/DoCastSpellIfCan, EventMap/ExecuteEvent, EventMap/ScheduleEvent#3, EventMap/Update, GridSearchers/GetCreatureListWithEntryInGrid#2, ObjectDistanceOrder/ObjectDistanceOrder, shared_Util/urand, Unit.Main/IsAlive, Unit.Main/SetFullHealth | — | — |
| GetAI_necroticShard | function | — | — | — |
| MinionspawnerAI | ctor | EventMap/Reset, EventMap/ScheduleEvent#3, ScriptedAI/ScriptedAI | — | — |
| Reset | method | — | — | — |
| UpdateAI | method | Creature.Main/SetWanderDistance, EventMap/ExecuteEvent, EventMap/Update, Object/GetEntry, Unit.Main/SendSpellGo, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SummonCreature#2 | — | — |
| GetAI_Minionspawner | function | — | — | — |
| npc_cultist_engineer | ctor | EventMap/Reset, ScriptedAI/ScriptedAI | — | — |
| Reset#10 | method | — | — | — |
| JustDied#5 | method | GameObject/Delete, SpellCaster/CastSpell#2, Unit.Main/RemoveAurasDueToSpell, WorldObject.Object/FindNearestCreature, WorldObject.Object/FindNearestGameObject, WorldObject.Object/SetUInt32Value | — | — |
| UpdateAI#10 | method | EventMap/ExecuteEvent, EventMap/Update, Object/GetObjectGuid, Unit.Main/AddAura, Unit.Main/SetChannelObjectGuid, WorldObject.Object/FindNearestCreature, WorldObject.Object/SetUInt32Value | — | — |
| OnScriptEventHappened#3 | method | Creature.Main/AI, Creature.Main/SetCorpseDelay, CreatureAI/OnScriptEventHappened, EventMap/ScheduleEvent#3, Player.Main/DestroyItemCount#2, Player.Main/ToPlayer, SpellCaster/CastSpell#2, Unit.Main/SendSpellGo, Unit.Main/SetFacingToObject, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SetFlag, WorldObject.Object/SummonCreature#2 | — | — |
| GetAI_npc_cultist_engineer | function | — | — | — |
| ScourgeMinion | ctor | EventMap/Reset, ScriptedAI/ScriptedAI | — | — |
| Reset#9 | method | EventMap/ScheduleEvent#3, Object/GetEntry | — | — |
| OnScriptEventHappened#2 | method | EventMap/ScheduleEvent#3, Object/ToUnit, SpellCaster/CastSpell#2, WorldObject.Object/MonsterSay#2 | — | — |
| JustDied#4 | method | Object/GetEntry, SpellCaster/CastSpell#2 | — | — |
| SpellHit#6 | method | Creature.Main/DespawnOrUnsummon | — | — |
| MoveInLineOfSight#2 | method | BasicAI/MoveInLineOfSight, CreatureAI/AttackStart, Object/GetEntry, Object/IsCreature, Unit.Main/AI, Unit.Main/GetVictim, WorldObject.Object/IsWithinDistInMap, WorldObject.Object/IsWithinLOSInMap | — | — |
| UpdateAI#9 | method | Creature.Main/SetDetectionDistance, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, EventMap/ExecuteEvent, EventMap/ScheduleEvent#3, EventMap/Update, Object/GetEntry, Player.Main/ToPlayer, shared_Util/urand, SpellCaster/CastSpell#2, TemporarySummon/GetSummoner, Unit.Main/GetVictim, Unit.Main/IsCharmerOrOwnerPlayerOrPlayerItself, Unit.Main/IsInCombat, Unit.Main/SelectHostileTarget, Unit.Main/SetInCombatWith, WorldObject.Object/IsValidAttackTarget, WorldObject.Object/IsWithinDistInMap, WorldObject.Object/IsWithinLOSInMap, WorldObject.Object/RemoveFlag | — | — |
| GetAI_ScourgeMinion | function | — | — | — |
| GossipSelect_npc_argent_emissary | function | GossipDef/AddMenuItem#5, GossipDef/SendGossipMenu, Object/GetGUID, ObjectGuid/ObjectGuid#5, ObjectMgr/GetSavedVariable, PlayerMenu/GetGossipMenu, shared_Util/urand | — | — |
| GossipHello_npc_argent_emissary | function | GossipDef/AddMenuItem#5, GossipDef/SendGossipMenu, Object/GetGUID, ObjectGuid/ObjectGuid#5, ObjectMgr/GetSavedVariable, Player.Main/SendUpdateWorldState, PlayerMenu/GetGossipMenu | — | — |
| PallidHorrorAI | ctor | Creature.Main/JoinCreatureGroup, Creature.Main/SetCorpseDelay, EventMap/Reset, EventMap/ScheduleEvent#3, Object/GetObjectGuid, ScriptedAI/ScriptedAI, shared_Util/urand, SpellCaster/CastSpell#2, Unit.Main/GetHealthPercent, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SummonCreature#2 | — | — |
| Reset#8 | method | Unit.Main/AddAura | — | — |
| MoveInLineOfSight | method | BasicAI/MoveInLineOfSight, CreatureAI/AttackStart, Object/IsCreature, Unit.Main/AI, Unit.Main/GetVictim, WorldObject.Object/IsWithinDistInMap, WorldObject.Object/IsWithinLOSInMap | — | — |
| JustDied#3 | method | Log.Main/Out, Map.Main/GetCreature, ObjectMgr/SetSavedVariable, ScriptMgr/DoScriptText, shared_Util/urand, SpellCaster/CastSpell#2, Unit.Main/DoKillUnit, Unit.Main/RemoveAurasDueToSpell, WorldObject.Object/FindNearestCreature, WorldObject.Object/GetMap, WorldObject.Object/GetZoneId | — | — |
| SummonedCreatureJustDied | method | Object/GetObjectGuid | — | — |
| SummonedCreatureDespawn | method | Object/GetObjectGuid | — | — |
| OnRemoveFromWorld | method | Map.Main/GetCreature, WorldObject.Object/AddObjectToRemoveList, WorldObject.Object/GetMap | — | — |
| UpdateAI#8 | method | Creature.Main/AI, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, CreatureAI/OnScriptEventHappened, EventMap/ExecuteEvent, EventMap/ScheduleEvent#3, EventMap/Update, Object/GetObjectGuid, ScriptMgr/DoScriptText, shared_Util/urand, SpellCaster/CastSpell#2, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, WorldObject.Object/GetNearPoint, WorldObject.Object/GetOrientation, WorldObject.Object/SummonCreature#2 | — | — |
| GetAI_PallidHorrorAI | function | — | — | — |
| AddSC_scourge_invasion | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
