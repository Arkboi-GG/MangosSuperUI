# boss_vaelastrasz

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_vaelastrasz

## Purpose & Responsibilities

This translation unit implements the artificial intelligence and interaction scripts for **Vaelastrasz the Corrupt**, the final boss of the *Blackwing Lair* dungeon, along with two associated elite adds: **Death Talon Captain** and **Death Talon Seether**.

The unit handles three distinct subsystems:
1.  **Boss AI (`boss_vaelAI`)**: Manages Vaelastrasz’s complex encounter phases, including a pre-fight cinematic intro involving Lord Nefarian, a dialogue sequence triggered by player gossip, and the combat phase featuring mechanics like *Burning Adrenaline*, *Flame Breath*, and *Cleave*. It also manages state transitions via the `ScriptedInstance` interface.
2.  **Interaction Scripts**: Handles gossip menus (`GossipHello_boss_vael`, `GossipSelect_boss_vael`) and quest acceptance (`QuestAccept_vaelastrasz`) to initiate the encounter or manage the "Scepter Run" prerequisite quest.
3.  **Add AI (`npc_death_talon_CaptainAI`, `npc_death_talon_SeetherAI`)**: Implements behavior for elite mobs found in the dungeon. The Captain manages a proximity-based aura buff for nearby allies and casts targeted spells. The Seether uses a simple melee-and-cast rotation with an engagement check.

There are no direct database table interactions in this unit; all persistent state is managed through the `ScriptedInstance` abstraction.

## Member-by-Member Behavior

### Vaelastrasz the Corrupt (Boss)

#### Initialization and State Management
*   **`boss_vaelAI` (ctor)**: Initializes the AI object. It retrieves the instance data pointer and checks if the introductory event (`TYPE_VAEL_EVENT`) has already occurred. If so, it skips the intro phase. It calls `Reset()` to initialize timers and flags.
*   **`Reset`**: Resets all internal timers (speech, abilities, intro) to their default values. Crucially, it sets Vaelastrasz’s health to **30%** of maximum. This reflects the lore state where Vael is already weakened/corrupted before the fight begins. It clears stored GUIDs for players and Nefarius.
*   **`JustReachedHome`**: Triggered when the creature despawns or resets. It sets the instance data for `TYPE_VAELASTRASZ` to `FAIL`. It restores Vaelastrasz to a friendly/neutral state (faction `FACTION_MONSTER` is set, but flags are adjusted to make him immune to NPCs and remove quest/gossip flags temporarily until re-engaged).

#### Introductory Cinematic
*   **`UpdateAI` (Intro Phase)**: If the intro event hasn't happened yet and the instance data indicates it should, the AI enters a timed sequence (`m_uiIntroPhase`):
    1.  **Phase 0**: Summons **Lord Nefarian** (`NPC_LORD_NEFARIAN`) at specific coordinates near the throne. Nefarian is summoned as a temporary creature with `NullCreatureAI` and moved to idle. He is marked as non-selectable.
    2.  **Phase 1**: Nefarian channels a spell (`SPELL_BANISHEMENT_OF_SCALE`) on Vaelastrasz. Vaelastrasz receives the `SPELL_NEFARIUS_CORRUPTION` aura, which is manually set to last 24 seconds. Nefarian speaks his first line.
    3.  **Phase 2**: Nefarian speaks his second line. Vaelastrasz becomes selectable again after a delay. The instance data for `TYPE_VAELASTRASZ` is set to `SPECIAL`, indicating the boss is ready for interaction but not yet engaged.

#### Dialogue and Engagement
*   **`BeginSpeech`**: Triggered by the gossip menu. It stands Vaelastrasz up, removes quest/gossip flags, and starts a speech timer. It records the player’s GUID who initiated the talk. It also checks if the "Scepter Run" quest is still not started; if so, it fails that quest in the instance data, enforcing the requirement that the scepter must be retrieved before fighting Vael.
*   **`UpdateAI` (Speech Phase)**: While `m_bIsDoingSpeech` is true, the AI cycles through speech lines (`SAY_LINE_1`, `SAY_LINE_2`, `SAY_LINE_3`) with specific delays. After the final line, Vaelastrasz turns hostile (`FACTION_MONSTER`), attacks the player who spoke to him, and casts `SPELL_ESSENCE_OF_THE_RED` (a raid-wide resource regeneration buff).
*   **`Aggro`**: When combat starts, it ensures `SPELL_ESSENCE_OF_THE_RED` is cast. It marks the zone as in combat. If this is the first engagement (`m_bEngaged`), it checks the server patch version. For patches older than 1.8, it enforces a 1-hour respawn delay and a forced despawn timer. It updates the instance data to `IN_PROGRESS`.
*   **`JustDied`**: Sets the respawn delay to 7 days and updates the instance data to `DONE`.

#### Combat Mechanics
*   **`UpdateAI` (Combat Phase)**: The core loop manages several independent timers:
    *   **Burning Adrenaline (Caster)**: Every ~15 seconds, it selects a random alive player using Mana who does not already have the aura and casts `SPELL_BURNING_ADRENALINE` on them. This is a dangerous debuff that increases damage dealt and reduces health over time.
    *   **Burning Adrenaline (Tank)**: Every ~45 seconds, it casts `SPELL_BURNING_ADRENALINE` on the current victim (tank). The code explicitly casts the spell *from the victim onto themselves* to ensure the aura effects apply correctly to the player rather than the boss.
    *   **Cleave**: Every 5–10 seconds, casts `SPELL_CLEAVE` on the victim. This is a chain cleave, requiring careful positioning.
    *   **Flame Breath**: Every 5–10 seconds, casts `SPELL_FLAME_BREATH` on the victim. This applies a stacking fire damage debuff.
    *   **Fire Nova**: Every 2 seconds, casts `SPELL_FIRE_NOVA` on self, damaging nearby enemies.
    *   **Tail Sweep**: Every 4–6 seconds, casts `SPELL_TAIL_SWEEP` on self, knocking back enemies behind the boss.
    *   **Low Health Yell**: If health drops below 15%, Vaelastrasz yells (`SAY_HALFLIFE`).
*   **`KilledUnit`**: If Vaelastrasz kills a player, there is a 20% chance (1 in 5) he will taunt them with `SAY_KILLTARGET`.

#### Interaction Scripts
*   **`GossipHello_boss_vael`**: Displays the initial gossip menu. It checks if the "Razorgore" boss is defeated (prerequisite) and if the user is a GM. If the "Scepter Run" quest is not started, it shows the quest menu. Otherwise, it offers the option to speak to Vael.
*   **`GossipSelect_boss_vael`**: Handles menu selections. Selecting the first item leads to a second menu. Selecting the second item ("Fight Time") closes the menu and triggers `BeginSpeech`.
*   **`QuestAccept_vaelastrasz`**: Handles acceptance of `QUEST_NEFARIUS_CORRUPTION`. It ensures only one player accepts it (checking instance data). Upon acceptance, it binds the player permanently to the instance and records their GUID as the "Scepter Champion".

### Death Talon Captain

*   **`npc_death_talon_CaptainAI` (ctor)**: Initializes the AI and calls `Reset()`.
*   **`Reset`**: Initializes timers for abilities. Casts `SPELL_AURA_FLAMES` on self if not present. Calls `SetAuraFlames(false)` to clear buffs from nearby allies initially.
*   **`MoveInLineOfSight`**: Aggressively attacks players within 29 yards if they are visible, targetable, and accessible.
*   **`Aggro`**: Ensures `SPELL_AURA_FLAMES` is applied permanently. Casts `SPELL_COMMANDING_SHOUT` (a buff) on self.
*   **`SetAuraFlames`**: A helper method that scans for nearby creatures of specific entries (Flamescale, Wyrmkin, Seether) within 50 yards. If `on` is true and the Captain is alive, it applies `SPELL_AURA_FLAMES` to allies within 15 yards and removes it from those further away. If `on` is false, it removes the aura from all nearby allies. This creates a proximity-based buff zone.
*   **`UpdateAI`**:
    *   Calls `SetAuraFlames(true)` every tick to maintain the buff zone.
    *   **Cleave**: Every 4–8 seconds, casts `SPELL_CLEAVE2` on the victim.
    *   **Commanding Shout**: Every 12–25 seconds, refreshes the shout on self.
    *   **Mark of Flames**: Every 15 seconds, casts `SPELL_MARK_FLAMES` on a random target.
    *   **Mark Detonation**: Every 20 seconds, forces a random target to cast `SPELL_MARK_DETONATION` on themselves (likely triggering an explosion).
*   **`JustDied`**: Calls `SetAuraFlames(false)` to remove buffs from nearby allies upon death.

### Death Talon Seether

*   **`npc_death_talon_SeetherAI` (ctor)**: Initializes the AI and calls `Reset()`.
*   **`Reset`**: Initializes timers for `Flame Buffet` and `Frenzy`. Sets `m_bEngaged` to false.
*   **`UpdateAI`**:
    *   **Frenzy**: Every 15 seconds, casts `SPELL_FRENZY` on self and emotes.
    *   **Engagement Check**: If not yet engaged, it checks if it can reach the victim with a melee auto-attack. Once reachable, it sets `m_bEngaged` to true.
    *   **Flame Buffet**: Only casts `SPELL_FLAME_BUFFET` on the victim if `m_bEngaged` is true. This prevents casting ranged spells while out of melee range. The timer resets every 8–12 seconds.

### Registration

*   **`AddSC_boss_vael`**: Registers the three scripts (`boss_vaelastrasz`, `npc_death_talon_Captain`, `npc_death_talon_Seether`) with the script manager, linking their respective AI getters and gossip/quest handlers.

## Cross-Unit Boundaries

*   **`ScriptedInstance`**: The primary interface for dungeon state. `boss_vaelAI` reads/writes `TYPE_VAEL_EVENT`, `TYPE_VAELASTRASZ`, `TYPE_SCEPTER_RUN`, and `DATA_SCEPTER_CHAMPION`. This allows the boss to coordinate with other dungeon events (e.g., ensuring Razorgore is dead, tracking the scepter quest).
*   **`ScriptMgr`**: Used to play sound/text emotes (`DoScriptText`) for Vaelastrasz, Nefarius, and the Seether.
*   **`WorldObject` / `Unit` / `Creature`**: Standard core APIs for movement, health, faction, flags, and summoning. `boss_vaelAI` uses `SummonCreature` to spawn Nefarius during the intro.
*   **`Map`**: Used to retrieve players (`GetPlayer`) and creatures (`GetCreature`) by GUID, essential for targeting specific players for Burning Adrenaline and finding the summoned Nefarius.
*   **`ThreatManager`**: `boss_vaelAI` accesses the threat list to identify mana-users for the Burning Adrenaline mechanic.
*   **`GossipDef` / `PlayerMenu`**: Used in the gossip functions to build and send menus to the player.
*   **`shared_Util`**: `urand` is used extensively for randomizing timer intervals and target selection.

## Data Model

This unit does not interact directly with any database tables. All state is managed in-memory via the `ScriptedInstance` system and creature/player objects.

## Notable Implementation Details

1.  **30% Health Start**: In `Reset`, Vaelastrasz is set to 30% health. This is a critical design choice reflecting his corrupted state. He does not start at full health.
2.  **Burning Adrenaline Casting Logic**: The code explicitly casts `SPELL_BURNING_ADRENALINE` *from the player onto themselves* (`pPlayer->CastSpell(pPlayer, ...)`). This is a workaround to ensure the aura's effects (like the instant kill or threat reduction) apply to the player and not the caster (Vaelastrasz). If Vael cast it on the player, some effects might incorrectly apply to Vael.
3.  **Intro Cinematic Timing**: The intro sequence relies on precise timers (`m_uiIntroTimer`) and phases. Nefarius is summoned with `NullCreatureAI` and manually positioned. The channel spell and corruption aura are applied manually to synchronize the visual and mechanical aspects of the corruption.
4.  **Proximity Buff Zone**: The Death Talon Captain’s `SetAuraFlames` method dynamically manages a buff zone. It adds buffs to allies within 15 yards and removes them from those outside, creating a "safe zone" mechanic for players or other mobs.
5.  **Patch-Specific Respawn**: The `Aggro` method checks `sWorld.GetWowPatch()`. For patches older than 1.8, it enforces a 1-hour respawn delay and a forced despawn. This preserves legacy behavior for servers running older content versions.
6.  **Scepter Run Enforcement**: The gossip and quest acceptance scripts enforce that the "Scepter Run" quest must be completed (or at least started/accepted by one player) before the boss can be properly engaged. If the quest is not started when the speech begins, it is automatically failed.

## Member Reference

**boss_vaelAI** (ctor): Initializes the AI, retrieves instance data, checks intro status, and calls `Reset()`.
**Reset**: Resets timers, flags, and sets health to 30%.
**BeginSpeech**: Starts the dialogue sequence, records the player GUID, and fails the Scepter Run quest if not started.
**KilledUnit**: Taunts killed players with a 20% chance.
**Aggro**: Casts Essence of the Red, sets combat state, enforces patch-specific respawn delays, and updates instance data.
**JustDied**: Sets 7-day respawn delay and marks the boss as DONE in instance data.
**JustReachedHome**: Marks the boss as FAIL in instance data and resets faction/flags.
**UpdateAI**: Manages the intro cinematic, speech sequence, and combat mechanics (Burning Adrenaline, Cleave, Flame Breath, Fire Nova, Tail Sweep).
**GossipSelect_boss_vael**: Handles gossip menu selections, triggering the speech sequence.
**GossipHello_boss_vael**: Displays the initial gossip menu, checking prerequisites.
**QuestAccept_vaelastrasz**: Handles quest acceptance, binding the player to the instance and recording the champion GUID.
**GetAI_boss_vael**: Factory function returning a new `boss_vaelAI` instance.
**npc_death_talon_CaptainAI** (ctor): Initializes the Captain AI and calls `Reset()`.
**Reset#2**: Initializes timers and casts initial auras.
**MoveInLineOfSight**: Aggressively attacks nearby players.
**Aggro#2**: Applies permanent aura and casts Commanding Shout.
**JustDied#2**: Removes auras from nearby allies.
**SetAuraFlames**: Manages the proximity-based buff zone for nearby allies.
**UpdateAI#2**: Manages combat timers for Cleave, Commanding Shout, Mark of Flames, and Mark Detonation.
**GetAI_npc_death_talon_Captain**: Factory function returning a new `npc_death_talon_CaptainAI` instance.
**npc_death_talon_SeetherAI** (ctor): Initializes the Seether AI and calls `Reset()`.
**Reset#3**: Initializes timers and engagement flag.
**UpdateAI#3**: Manages Frenzy and Flame Buffet, with an engagement check for melee range.
**GetAI_npc_death_talon_Seether**: Factory function returning a new `npc_death_talon_SeetherAI` instance.
**AddSC_boss_vael**: Registers all three scripts with the script manager.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_vaelastrasz

*Source:* boss_vaelastrasz.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_vaelAI | ctor | InstanceData/GetData, ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | ObjectGuid/Clear, Unit.Main/SetHealthPercent | — | — |
| BeginSpeech | method | InstanceData/GetData, InstanceData/SetData, Object/GetObjectGuid, ScriptMgr/DoScriptText, Unit.Main/SetStandState, WorldObject.Object/RemoveFlag | — | — |
| KilledUnit | method | Object/GetTypeId, ScriptMgr/DoScriptText, shared_Util/urand | — | — |
| Aggro | method | Creature.Main/ForcedDespawn, Creature.Main/SetInCombatWithZone, Creature.Main/SetRespawnDelay, CreatureAI/DoCastSpellIfCan, InstanceData/SetData, World/GetWowPatch | — | — |
| JustDied | method | Creature.Main/SetRespawnDelay, InstanceData/SetData | — | — |
| JustReachedHome | method | InstanceData/SetData, Unit.Main/SetFactionTemplateId, Unit.Main/SetStandState, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag | — | — |
| UpdateAI | method | Creature.Main/SetAI, Creature.MotionMaster/MoveIdle, CreatureAI/AttackStart, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, InstanceData/GetData, InstanceData/SetData, Map.Main/GetCreature, Map.Main/GetPlayer, NullCreatureAI/NullCreatureAI, Object/GetObjectGuid, Object/SetGuidValue, Object/ToPlayer, ScriptMgr/DoScriptText, shared_Util/urand, SpellAuraHolder/SetAuraDuration, SpellCaster/CastSpell#2, ThreatManager/getThreatList, Unit.Main/AddAura, Unit.Main/GetHealthPercent, Unit.Main/GetMotionMaster, Unit.Main/GetPowerType, Unit.Main/GetThreatManager, Unit.Main/GetVictim, Unit.Main/HasAura, Unit.Main/HasAura#2, Unit.Main/IsAlive, Unit.Main/IsInCombat, Unit.Main/SelectHostileTarget, Unit.Main/SetFactionTemplateId, Unit.Main/SetStandState, Unit.SpellAuras/SetAuraMaxDuration, WorldObject.Object/GetMap, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag, WorldObject.Object/SetUInt32Value, WorldObject.Object/SummonCreature#2 | — | — |
| GossipSelect_boss_vael | function | Creature.Main/AI, GossipDef/AddMenuItem#5, GossipDef/CloseGossip, GossipDef/SendGossipMenu, Object/GetObjectGuid, PlayerMenu/GetGossipMenu | — | — |
| GossipHello_boss_vael | function | GossipDef/AddMenuItem#5, GossipDef/SendGossipMenu, InstanceData/GetData, Object/GetObjectGuid, Player.Main/IsGameMaster, Player.Main/PrepareQuestMenu, PlayerMenu/GetGossipMenu, Unit.Main/IsQuestGiver, WorldObject.Object/GetInstanceData | — | — |
| QuestAccept_vaelastrasz | function | Creature.Main/GetRespawnTimeEx, InstanceData/GetData, InstanceData/SetData, Map.Main/BindToInstanceOrRaid, Object/GetObjectGuid, Player.Main/FailQuest, QuestDef/GetQuestId, WorldObject.Object/GetInstanceData, WorldObject.Object/GetMap | — | — |
| GetAI_boss_vael | function | — | — | — |
| npc_death_talon_CaptainAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset#2 | method | CreatureAI/DoCastSpellIfCan, shared_Util/urand | — | — |
| MoveInLineOfSight | method | CreatureAI/AttackStart, Object/IsPlayer, Unit.Main/GetVictim, Unit.Main/IsInAccessablePlaceFor, Unit.Main/IsTargetableBy, WorldObject.Object/GetDistance2d#3, WorldObject.Object/IsWithinLOSInMap | — | — |
| Aggro#2 | method | CreatureAI/DoCastSpellIfCan, Unit.Main/AddAura, Unit.Main/HasAura#2 | — | — |
| JustDied#2 | method | — | — | — |
| SetAuraFlames | method | GridSearchers/GetCreatureListWithEntryInGrid#2, Unit.Main/AddAura, Unit.Main/HasAura#2, Unit.Main/IsAlive, Unit.Main/RemoveAurasDueToSpell, WorldObject.Object/IsWithinDistInMap | — | — |
| UpdateAI#2 | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, SpellCaster/CastSpell#2, Unit.Main/GetVictim, Unit.Main/IsAlive, Unit.Main/SelectHostileTarget | — | — |
| GetAI_npc_death_talon_Captain | function | — | — | — |
| npc_death_talon_SeetherAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#3 | method | shared_Util/urand | — | — |
| UpdateAI#3 | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, ScriptMgr/DoScriptText, shared_Util/urand, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_npc_death_talon_Seether | function | — | — | — |
| AddSC_boss_vael | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
