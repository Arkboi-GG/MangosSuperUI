# boss_marli

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_marli

**Purpose & Responsibilities**
`boss_marli.cpp` implements the artificial intelligence and combat mechanics for **High Priestess Mar'li**, a raid boss in the Zul'Gurub instance. The script manages a complex two-phase fight involving transformation between a humanoid troll form and a giant spider form. Key responsibilities include:
1.  **Phase Management:** Transitioning between Phase 1 (Troll) and Phase 2 (Spider) every 35 seconds, altering abilities, damage output, and threat dynamics.
2.  **Summoning Mechanics:** Spawning "Spawn of Mar'li" (spiders) from interactive game objects ("Eggs") located in the arena.
3.  **Ability Rotation:** Executing specific spell rotations for each phase, including crowd control (Enveloping Webs), burst damage (Charge), and self-buffs (Aggrandir).
4.  **Instance Integration:** Updating the `ScriptedInstance` state (`TYPE_MARLI`) upon aggro and death, and interacting with the global patch version to handle legacy summon cleanup.

**Member-by-Member Behavior**

### Initialization and Lifecycle
*   **`boss_marliAI` (Constructor):** Initializes the AI object. It retrieves the instance data pointer (`m_pInstance`) and stores the creature's default display ID (`m_uiDefaultModel`) to restore it after transformations. It immediately calls `Reset()` to initialize timers and states.
*   **`Reset`:** Resets all internal timers to their base values. It resets the instance data state to `NOT_STARTED` if the boss is not already marked `DONE`. It iterates through all `GO_EGG` game objects in the grid, resetting any that are `ACTIVE` back to `READY`. Crucially, it checks the server's configured WoW patch version via `World/GetWowPatch`; if the patch is 1.8.0 or higher, it manually unsummons any existing temporary summons of `NPC_SPAWN_OF_MARLI` to comply with client-side despawn rules. Finally, it calls `Creature/Main/ResetStats` to clear any lingering buff/debuff modifiers from previous phases.
*   **`JustDied`:** Plays the death sound/text. Updates the instance data to `DONE`. Casts `SPELL_HAKKAR_POWER_DOWN` on itself, which removes a stack of the "Hakkar Power" buff (a mechanic shared across Zul'Gurub bosses).

### Combat Entry and Summoning
*   **`Aggro`:** Marks the instance event as `IN_PROGRESS`. If this is the first time entering combat (`!m_bFirstSpidersAreSpawned`), it plays a spawn text, casts the visual `SPELL_HATCH`, and spawns four spiders by activating four distinct eggs. It sets `m_bFirstSpidersAreSpawned` to true to prevent re-spawning these initial four on subsequent aggro events (e.g., after a wipe).
*   **`SelectNextEgg`:** A helper method that finds the nearest `GO_EGG` game object that is in `GO_STATE_READY`. It sorts available eggs by distance using `ObjectDistanceOrder` to ensure spiders spawn in a spread pattern rather than clustering. Returns `nullptr` if no ready eggs are found.
*   **`JustSummoned`:** Triggered when a creature is summoned by this AI. If the summoned creature is `NPC_SPAWN_OF_MARLI`, it selects a random hostile target for the spider and initiates an attack.

### Core AI Loop (`UpdateAI`)
The `UpdateAI` method drives the combat logic, branching based on `m_bIsInPhaseTwo`.

**Phase 1 (Troll Form):**
*   **`SPELL_POISONVOLLEY`:** Cast on victim every 10–20 seconds.
*   **`SPELL_DRAIN_LIFE`:** Cast on victim every 20–50 seconds.
*   **`SPELL_AGGRANDIR`:** Applies a self-buff every 10–20 seconds if not already present.
*   **Spider Spawning:** Every 20–30 seconds, spawns 1–4 additional spiders using `SelectNextEgg`.

**Phase 2 (Spider Form):**
*   **`SPELL_ENVELOPINGWEBS`:** Cast on victim every 10–15 seconds. This sets `m_bHasWebbed` to true and primes the `m_uiCharge_Timer`.
*   **`SPELL_CHARGE`:** Triggered 1 second after webbing. Targets the top-agro player. **Notable Logic:** Immediately calls `DoResetThreat()` to clear the threat table, then attacks the charged target. This prevents players from maintaining high threat during the charge sequence.
*   **`SPELL_CORROSIVE_POISON`:** Cast on victim every 25–35 seconds.

**Transformation Logic:**
*   Every 35 seconds (`m_uiTransformBack_Timer`), the boss transforms.
*   **To Spider:** Interrupts current spells, plays transform text, casts `SPELL_SPIDER_FORM`. Increases melee damage range by 35%. Resets threat. Sets `m_bIsInPhaseTwo` to true.
*   **To Troll:** Interrupts current spells, casts `SPELL_TRANSFORM_BACK`. Restores the original display ID. Resets melee damage range to baseline (1% increase, effectively neutralizing the previous boost). Sets `m_bIsInPhaseTwo` to false.

**Global Abilities:**
*   **`SPELL_TRASH`:** Cast on victim every 10–20 seconds regardless of phase.

### Utility and Hooks
*   **`SpellHitTarget`:** Monitors spells hitting targets. If `SPELL_ENVELOPINGWEBS` hits a player, it reduces the caster's (Mar'li's) threat toward that player by 100%. This ensures the webbed player does not remain the primary target immediately after being webbed, facilitating the Charge mechanic targeting the highest-threat non-webbed player (though the current code targets top agro before reset, the threat reduction helps manage post-charge aggro).
*   **`GetAI_boss_marli`:** Factory function returning a new `boss_marliAI` instance.
*   **`AddSC_boss_marli`:** Registers the script with the engine.

**Cross-Unit Boundaries**

*   **`ScriptedInstance` (`m_pInstance`):**
    *   *Direction:* Inbound/Outbound.
    *   *Usage:* `Reset` and `Aggro` check/set `TYPE_MARLI` state. `JustDied` sets state to `DONE`. This synchronizes the boss's status with the instance-wide event tracker.
*   **`GameObject` (Eggs):**
    *   *Direction:* Outbound.
    *   *Usage:* `Reset` and `SelectNextEgg` query and modify the state of `GO_EGG` objects. `Aggro` and `UpdateAI` use egg positions to summon spiders.
*   **`Creature` (Summons):**
    *   *Direction:* Outbound.
    *   *Usage:* `Reset` cleans up old summons. `Aggro` and `UpdateAI` summon new `NPC_SPAWN_OF_MARLI`. `JustSummoned` directs their initial aggression.
*   **`World` (`sWorld`):**
    *   *Direction:* Outbound.
    *   *Usage:* `Reset` calls `GetWowPatch` to determine if legacy summon cleanup is required.
*   **`ScriptMgr` / `Log`:**
    *   *Direction:* Outbound.
    *   *Usage:* `Aggro`, `JustDied`, `UpdateAI` use `DoScriptText` for emotes. `Reset` and `SelectNextEgg` use `sLog.Out` for debug logging if eggs are missing.

**Data Model**
This unit does not directly access database tables. It interacts with runtime game objects (`gameobject` table entries loaded into memory) and creature templates, but performs no SQL queries.

**Notable Implementation Details**

1.  **Patch-Dependent Cleanup:** The `Reset` method contains a specific check for `WOW_PATCH_108`. Prior to patch 1.8.0, summoned creatures did not automatically despawn on boss reset. The code manually iterates and unsummons them if the server is configured for 1.8.0+, preventing ghost summons from persisting across wipes.
2.  **Threat Manipulation in Charge:** During Phase 2, the `SPELL_CHARGE` sequence explicitly calls `DoResetThreat()` before attacking. This is a critical mechanic to prevent the boss from sticking to the player who generated the most threat prior to the charge, allowing the tank to regain aggro or for the charge to hit a different target safely.
3.  **Damage Scaling:** The transformation logic manually calculates and sets `BASE_ATTACK` damage ranges. In spider form, damage is increased by 35% (`dmgMin + (dmgMin / 100) * 35`). In troll form, it is set to `+1%`, which effectively restores the base value since the previous 35% boost was additive. This manual adjustment bypasses standard aura-based damage modifiers, ensuring consistent damage output regardless of external buffs.
4.  **Egg Selection Strategy:** `SelectNextEgg` uses `ObjectDistanceOrder` to pick the closest ready egg. This ensures that spiders spawn sequentially around the arena rather than all from the same location, creating a wider area-of-effect hazard for players.
5.  **Initial Spawn Guard:** The boolean `m_bFirstSpidersAreSpawned` ensures that the initial four spiders are only spawned once per encounter (on first aggro). Subsequent aggro events (e.g., after a wipe) do not trigger this initial spawn, relying instead on the periodic `m_uiSpawnSpider_Timer` in `UpdateAI`.

## Member Reference

**boss_marliAI**
Constructor that initializes the AI, retrieves instance data, stores the default model ID, and calls `Reset`.

**Reset**
Resets all timers, instance state, and game object states. Cleans up temporary summons if the server patch is >= 1.8.0. Resets creature stats to clear phase buffs.

**Aggro**
Sets instance state to IN_PROGRESS. If first aggro, spawns 4 spiders from eggs and plays hatch text.

**SelectNextEgg**
Finds the nearest `GO_EGG` in `GO_STATE_READY` by sorting available eggs by distance. Returns the GameObject pointer or nullptr.

**JustSummoned**
If the summoned creature is a Spawn of Mar'li, assigns it a random hostile target and starts attacking.

**JustDied**
Plays death text, sets instance state to DONE, and casts `SPELL_HAKKAR_POWER_DOWN` to remove a Hakkar Power stack.

**SpellHitTarget**
If `SPELL_ENVELOPINGWEBS` hits a player, reduces Mar'li's threat toward that player by 100%.

**UpdateAI**
Main combat loop. Handles Phase 1 (Troll) and Phase 2 (Spider) ability rotations. Manages the 35-second transformation timer, adjusting damage ranges and resetting threat during transitions. Spawns spiders periodically.

**GetAI_boss_marli**
Factory function that creates and returns a new `boss_marliAI` instance for the given creature.

**AddSC_boss_marli**
Registers the `boss_marli` script with the ScriptMgr, linking the name to the `GetAI_boss_marli` factory function.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_marli

*Source:* boss_marli.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_marliAI | ctor | ScriptedAI/ScriptedAI, Unit.Main/GetDisplayId, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | Creature.Main/IsTemporarySummon, Creature.Main/ResetStats, GameObject/GetGoState, GameObject/SetGoState, GridSearchers/GetCreatureListWithEntryInGrid#2, GridSearchers/GetGameObjectListWithEntryInGrid#2, InstanceData/GetData, InstanceData/SetData, Log.Main/Out, TemporarySummon/UnSummon, World/GetWowPatch | — | — |
| Aggro | method | CreatureAI/DoCastSpellIfCan, GameObject/SetGoState, InstanceData/GetData, InstanceData/SetData, ScriptMgr/DoScriptText, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SummonCreature#2 | — | — |
| SelectNextEgg | method | GameObject/GetGoState, GridSearchers/GetGameObjectListWithEntryInGrid#2, Log.Main/Out, ObjectDistanceOrder/ObjectDistanceOrder | — | — |
| JustSummoned | method | Creature.Main/AI, Creature.Main/SelectAttackingTarget, CreatureAI/AttackStart, Object/GetEntry | — | — |
| JustDied | method | InstanceData/SetData, ScriptMgr/DoScriptText, SpellCaster/CastSpell#2 | — | — |
| SpellHitTarget | method | Object/GetTypeId, ThreatManager/modifyThreatPercent#2, Unit.Main/GetThreatManager | — | — |
| UpdateAI | method | Creature.Main/GetDefaultDamageRange, Creature.Main/SelectAttackingTarget, CreatureAI/AttackStart, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, GameObject/SetGoState, Player.StatSystem/UpdateDamagePhysical, ScriptedAI/DoResetThreat, ScriptMgr/DoScriptText, shared_Util/urand, SpellCaster/InterruptNonMeleeSpells, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/GetVictim, Unit.Main/HasAura#2, Unit.Main/SelectHostileTarget, Unit.Main/SetBaseWeaponDamage, Unit.Main/SetDisplayId, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SummonCreature#2 | — | — |
| GetAI_boss_marli | function | — | — | — |
| AddSC_boss_marli | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
