# boss_dragon_of_nightmare

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_dragon_of_nightmare

**Purpose & Responsibilities**

This unit implements the core artificial intelligence and script infrastructure for the "Dragons of Nightmare" encounter in the Emerald Dream instance. It serves two primary functions:

1.  **Base Dragon AI (`boss_dragon_of_nightmareAI`):** Provides a shared foundation for the four main dragon bosses (Emeriss, Lethon, Taerar, and Ysondre). It manages common mechanics such as periodic spells (Aura of Nature, Seeping Fog, Noxious Breath, Tail Sweep), health-phase transitions via `DoSpecialAbility`, and a player-summoning mechanic when targets are out of range or line-of-sight.
2.  **Encounter Scripts:** Registers and provides factory functions for all creatures, game objects, and spells involved in the encounter, including dream fog minions, spirit shades, putrid shrooms, and specific boss variants. It also handles the dynamic permutation system that determines which dragon appears in which slot during the encounter.

The unit does not interact with any database tables directly; all data is derived from in-memory object managers and hardcoded constants.

## Member-by-Member Behavior

### Base Dragon AI (`boss_dragon_of_nightmareAI`)

This class inherits from `ScriptedAI` and defines the common behavior for all four nightmare dragons.

*   **`boss_dragon_of_nightmareAI` (Constructor):** Initializes the AI by calling `Reset`. Crucially, it sets `SetAInitializeOnRespawn(true)` on the creature. This ensures that when the boss respawns, the AI is re-initialized, allowing the entry permutation logic (handled in `GetAI_boss_dragon_of_nightmare`) to apply correctly to the new instance.
*   **`Reset`:** Resets all internal timers (`m_uiAuraOfNatureTimer`, `m_uiSeepingFogTimer`, etc.) to zero or their initial random/delayed values. It resets `m_uiEventCounter` to 1, which tracks the progression of special abilities based on health thresholds.
*   **`Aggro`:** Casts `SPELL_MARK_OF_NATURE` on the creature itself if it doesn't already have the aura. This is likely a buff or debuff applied at the start of combat.
*   **`EnterEvadeMode`:** Cleans up the encounter state by removing guardians (summons) and the `SPELL_MARK_OF_NATURE` aura. It then calls the parent `ScriptedAI::EnterEvadeMode` to handle standard evasion logic.
*   **`JustDied`:** Calls the parent `ScriptedAI::JustDied` implementation. No additional cleanup is performed in this base class.
*   **`UpdateAI`:** The main update loop.
    *   Checks for a valid hostile target.
    *   Manages `m_uiAuraOfNatureTimer`: Casts `SPELL_AURA_OF_NATURE` every 3–5 seconds.
    *   Calls `EnterEvadeIfOutOfHomeArea` to force evasion if the boss leaves the designated area.
    *   **Phase Transition Logic:** Checks if the creature's health percentage has dropped below a threshold determined by `m_uiEventCounter` (specifically, `< 100.0f - m_uiEventCounter * 25.0f`). If so, it calls the pure virtual `DoSpecialAbility()`. If that returns true, it increments `m_uiEventCounter`. This allows derived classes to trigger unique events at 75%, 50%, and 25% health.
    *   Calls `UpdateDragonAI(uiDiff)`, a virtual hook for derived classes to manage their own timers. If this returns false, the rest of the update is skipped.
    *   **Summon Player Mechanic:** If the current victim is not reachable via melee auto-attack or is not within line-of-sight, it increments `m_uiSummonPlayerTimer`. If this timer exceeds 6 seconds, it casts `SPELL_SUMMON_PLAYER` on the victim. This pulls players back into range if they kite too far or hide behind obstacles.
    *   Manages `m_uiSeepingFogTimer`: Every ~2 minutes, casts both `SPELL_SEEPING_FOG_RIGHT` and `SPELL_SEEPING_FOG_LEFT`.
    *   Manages `m_uiNoxiousBreathTimer`: Casts `SPELL_NOXIOUS_BREATH` on the victim every 9–11 seconds.
    *   Manages `m_uiTailSweepTimer`: Casts `SPELL_TAIL_SWEEP` every 6–8 seconds.
    *   Calls `DoMeleeAttackIfReady` to perform physical attacks.

### Dream Fog Minion AI (`npc_dream_fogAI`)

This class inherits from `ScriptedPetAI` and controls the "Dream Fog" adds spawned during the encounter.

*   **`npc_dream_fogAI` (Constructor):** Sets the react state to `REACT_AGGRESSIVE`. Calls `Reset` and `ResetCreature`.
*   **`Reset` (Reset#2):** Resets `m_uiChangeTargetTimer` to 0. This is the `Reset` method belonging to the `npc_dream_fogAI` class.
*   **`ResetCreature`:** Casts `SPELL_DREAM_FOG_AURA` on the creature itself if not present. This likely applies a visual effect or minor buff/debuff.
*   **`AttackedBy`:** Empty override. Prevents default threat generation or reaction logic from the parent class.
*   **`GetNextTarget`:** Determines the next target for the fog. It looks at the owner's (the dragon's) threat list and selects a random player target using `SelectAttackingTarget`. If the selected target is different from the current victim, it returns it. Otherwise, it returns `nullptr`.
*   **`ChangeTarget`:** If `GetNextTarget()` returns a valid target, it removes 100% of the threat from the current victim (effectively resetting aggro on the old target) and starts attacking the new target. It then sets `m_uiChangeTargetTimer` to a random value between 6 and 10 seconds.
*   **`UpdatePetAI`:** The main update loop.
    *   If `m_uiChangeTargetTimer` expires, it calls `ChangeTarget()`.
    *   If the creature is within `CONTACT_DISTANCE` of its victim, it resets `m_uiChangeTargetTimer` to a shorter random interval (4–8 seconds), encouraging more frequent target switching when engaged in melee.

### Putrid Shroom Game Object AI (`go_putrid_shroomAI`)

This class inherits from `GameObjectAI` and controls the lifespan of "Putrid Shroom" objects.

*   **`go_putrid_shroomAI` (Constructor):** Sets `m_uiDespawnTimer` to 2 minutes and 1 second.
*   **`UpdateAI` (UpdateAI#2):** Decrements the timer. If the timer expires, it deletes the game object (`me->Delete()`). This is a simple despawn timer for the `go_putrid_shroomAI` class.

### Factory Functions & Script Registration

These functions create instances of the various AIs and register them with the script manager.

*   **`GetDrakeVar`:** Maps a creature entry ID to a variable index (`VAR_PERM_1` through `VAR_PERM_4`). This is used to look up the saved permutation for the encounter.
*   **`GetAI_npc_dream_fog`:** Creates and returns a new `npc_dream_fogAI`.
*   **`GetAI_npc_spirit_shade`:** Creates and returns a new `npc_spirit_shadeAI` (defined in `boss_lethon.cpp`).
*   **`GetAI_npc_shade_of_taerar`:** Creates and returns a new `npc_shade_of_taerarAI` (defined in `boss_taerar.cpp`).
*   **`GetAI_npc_demented_druid`:** Creates and returns a new `npc_demented_druidAI` (defined in `boss_ysondre.cpp`).
*   **`GetAI_go_putrid_shroom`:** Creates and returns a new `go_putrid_shroomAI`.
*   **`GetAI_boss_dragon_of_nightmare`:** The central factory for the dragon bosses.
    1.  It retrieves the current permutation variable index using `GetDrakeVar`.
    2.  It fetches the saved variable value from `sObjectMgr.GetSavedVariable`. This value represents the entry ID of the dragon that should appear in this slot.
    3.  If the saved entry differs from the creature's current entry, it updates the creature's entry using `pCreature->UpdateEntry`. This dynamically changes the model, stats, and potentially the AI of the creature to match the permuted dragon.
    4.  It then switches on the *current* entry (which may have just been updated) to instantiate the correct derived AI class (`boss_emerissAI`, `boss_lethonAI`, `boss_taerarAI`, or `boss_ysondreAI`).
*   **`EmeraldDragonsDreamFogScript::OnSetTargetMap`:** A spell script hook for `SPELL_SEEPING_FOG` (or related). It limits the maximum number of targets for the spell effect to 1. This ensures the spell hits only one target, likely the caster or a specific point, rather than multiple players.
*   **`GetScript_EmeraldDragonsDreamFog`:** Returns a new instance of `EmeraldDragonsDreamFogScript`.
*   **`AddSC_dragons_of_nightmare`:** Registers all the scripts defined in this unit with the `ScriptMgr`. It creates `Script` objects for each AI and spell, assigns the appropriate `GetAI` or `GetSpellScript` function pointers, and calls `RegisterSelf()`.

## Cross-Unit Boundaries

*   **`boss_dragon_of_nightmareAI` Constructor:**
    *   Calls `Creature::SetAInitializeOnRespawn` to ensure proper re-initialization.
    *   Inherits from `ScriptedAI`.
    *   Is called by the constructors of `boss_emerissAI`, `boss_lethonAI`, `boss_taerarAI`, and `boss_ysondreAI` (via inheritance).
*   **`boss_dragon_of_nightmareAI::Reset`:**
    *   Calls `shared_Util::urand` for randomizing timer values.
    *   Is overridden by `Reset` methods in `boss_emeriss`, `boss_lethon`, `boss_taerar`, and `boss_ysondre`.
*   **`boss_dragon_of_nightmareAI::Aggro`:**
    *   Calls `CreatureAI::DoCastSpellIfCan` to cast `SPELL_MARK_OF_NATURE`.
    *   Is overridden by `Aggro` methods in `boss_emeriss`, `boss_lethon`, `boss_taerar`, and `boss_ysondre`.
*   **`boss_dragon_of_nightmareAI::EnterEvadeMode`:**
    *   Calls `ScriptedAI::EnterEvadeMode`, `Unit::RemoveAurasDueToSpell`, and `Unit::RemoveGuardians`.
    *   Is overridden by `EnterEvadeMode` in `boss_taerar`.
*   **`boss_dragon_of_nightmareAI::JustDied`:**
    *   Calls `CreatureAI::JustDied`.
*   **`boss_dragon_of_nightmareAI::UpdateAI`:**
    *   Calls `CreatureAI::DoCastSpellIfCan`, `CreatureAI::DoMeleeAttackIfReady`, `ScriptedAI::EnterEvadeIfOutOfHomeArea`, `shared_Util::urand`, `Unit::CanReachWithMeleeAutoAttack`, `Unit::GetHealthPercent`, `Unit::GetVictim`, `Unit::SelectHostileTarget`, and `WorldObject::IsWithinLOSInMap`.
*   **`npc_dream_fogAI` Constructor:**
    *   Inherits from `ScriptedPetAI`.
    *   Calls `Unit::SetReactState`.
*   **`npc_dream_fogAI::ResetCreature`:**
    *   Calls `CreatureAI::DoCastSpellIfCan`.
*   **`npc_dream_fogAI::GetNextTarget`:**
    *   Calls `Creature::SelectAttackingTarget`, `Object::ToUnit`, `Unit::GetOwner`, and `Unit::GetVictim`.
*   **`npc_dream_fogAI::ChangeTarget`:**
    *   Calls `ScriptedPetAI::AttackStart`, `shared_Util::urand`, `ThreatManager::getThreat`, `ThreatManager::modifyThreatPercent`, `Unit::GetThreatManager`, and `Unit::GetVictim`.
*   **`npc_dream_fogAI::UpdatePetAI`:**
    *   Calls `shared_Util::urand`, `Unit::GetVictim`, and `WorldObject::IsWithinDistInMap`.
*   **`go_putrid_shroomAI` Constructor:**
    *   Inherits from `GameObjectAI`.
*   **`go_putrid_shroomAI::UpdateAI` (UpdateAI#2):**
    *   Calls `GameObject::Delete`.
*   **`GetAI_npc_spirit_shade`:**
    *   Calls `boss_lethon::npc_spirit_shadeAI` constructor.
*   **`GetAI_npc_shade_of_taerar`:**
    *   Calls `boss_taerar::npc_shade_of_taerarAI` constructor.
*   **`GetAI_npc_demented_druid`:**
    *   Calls `boss_ysondre::npc_demented_druidAI` constructor.
*   **`GetAI_boss_dragon_of_nightmare`:**
    *   Calls `boss_emeriss::boss_emerissAI`, `boss_lethon::boss_lethonAI`, `boss_taerar::boss_taerarAI`, `boss_ysondre::boss_ysondreAI` constructors.
    *   Calls `Creature::UpdateEntry`, `Object::GetEntry`, and `ObjectMgr::GetSavedVariable`.
*   **`AddSC_dragons_of_nightmare`:**
    *   Calls `Script::Script` constructor and `ScriptMgr::RegisterSelf`.
    *   Is called by `ScriptLoader::AddScripts`.

## Data Model

This unit does not interact with any database tables directly. All data is managed in-memory via the `ObjectMgr` and hardcoded constants.

## Notable Implementation Details

*   **Permutation System:** The `GetAI_boss_dragon_of_nightmare` function implements a dynamic boss permutation system. It uses `GetSavedVariable` to determine which dragon should appear in a given slot. If the creature's entry doesn't match the saved variable, it updates the entry on the fly. This allows the encounter to randomize the order or identity of the dragons without changing the underlying creature templates in the database.
*   **Health-Based Phase Transitions:** The `UpdateAI` method in `boss_dragon_of_nightmareAI` checks the creature's health percentage against a threshold calculated from `m_uiEventCounter`. This triggers `DoSpecialAbility()` at specific health milestones (75%, 50%, 25%). Derived classes implement `DoSpecialAbility` to perform unique actions at these phases.
*   **Player Summoning:** The `UpdateAI` method includes a mechanism to summon players back into range if they are out of melee reach or line-of-sight. This prevents players from kiting the boss indefinitely or hiding behind obstacles.
*   **Dream Fog Target Switching:** The `npc_dream_fogAI` class implements a complex target-switching logic. It periodically selects a new random target from the owner's threat list and resets threat on the previous target. This makes the fog adds unpredictable and forces players to manage multiple threats.
*   **Spell Target Limitation:** The `EmeraldDragonsDreamFogScript` limits the number of targets for a specific spell effect to 1. This is likely to ensure that the "Seeping Fog" spell behaves as intended, possibly hitting only the caster or a specific point rather than multiple players.

## Member Reference

*   **GetDrakeVar**: Function that maps a creature entry ID to a permutation variable index (`VAR_PERM_1`–`VAR_PERM_4`).
*   **boss_dragon_of_nightmareAI**: Constructor for the base dragon AI; initializes timers and sets `SetAInitializeOnRespawn(true)`.
*   **Reset**: Method in `boss_dragon_of_nightmareAI` that resets all common timers and the event counter.
*   **Aggro**: Method in `boss_dragon_of_nightmareAI` that casts `SPELL_MARK_OF_NATURE` on aggro.
*   **DoSpecialAbility**: Pure virtual declaration in `boss_dragon_of_nightmareAI` for phase-specific abilities.
*   **UpdateDragonAI**: Virtual method in `boss_dragon_of_nightmareAI` allowing derived classes to manage custom timers.
*   **EnterEvadeMode**: Method in `boss_dragon_of_nightmareAI` that cleans up auras/guardians and calls parent evade.
*   **JustDied**: Method in `boss_dragon_of_nightmareAI` that calls parent `JustDied`.
*   **UpdateAI**: Main update loop in `boss_dragon_of_nightmareAI` handling common spells, phase checks, and player summoning.
*   **npc_dream_fogAI**: Constructor for the dream fog minion AI; sets aggressive react state.
*   **Reset#2**: Method in `npc_dream_fogAI` that resets the target change timer.
*   **ResetCreature**: Method in `npc_dream_fogAI` that applies `SPELL_DREAM_FOG_AURA`.
*   **AttackedBy**: Empty override in `npc_dream_fogAI` to suppress default threat reactions.
*   **GetNextTarget**: Method in `npc_dream_fogAI` that selects a new random player target from the owner's threat list.
*   **ChangeTarget**: Method in `npc_dream_fogAI` that switches attack target and resets threat on the old target.
*   **UpdatePetAI**: Main update loop in `npc_dream_fogAI` managing target switching timers.
*   **go_putrid_shroomAI**: Constructor for the putrid shroom game object AI; sets despawn timer.
*   **UpdateAI#2**: Method in `go_putrid_shroomAI` that decrements the despawn timer and deletes the object when expired.
*   **GetAI_npc_dream_fog**: Factory function returning a new `npc_dream_fogAI`.
*   **GetAI_npc_spirit_shade**: Factory function returning a new `npc_spirit_shadeAI` (from `boss_lethon`).
*   **GetAI_npc_shade_of_taerar**: Factory function returning a new `npc_shade_of_taerarAI` (from `boss_taerar`).
*   **GetAI_npc_demented_druid**: Factory function returning a new `npc_demented_druidAI` (from `boss_ysondre`).
*   **GetAI_go_putrid_shroom**: Factory function returning a new `go_putrid_shroomAI`.
*   **GetAI_boss_dragon_of_nightmare**: Central factory for dragon bosses; handles entry permutation and instantiates the correct derived AI.
*   **OnSetTargetMap**: Spell script hook in `EmeraldDragonsDreamFogScript` limiting spell targets to 1.
*   **GetScript_EmeraldDragonsDreamFog**: Factory function returning a new `EmeraldDragonsDreamFogScript`.
*   **AddSC_dragons_of_nightmare**: Registration function that adds all scripts in this unit to the `ScriptMgr`.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_dragon_of_nightmare

*Source:* boss_dragon_of_nightmare.cpp, boss_dragon_of_nightmare.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GetDrakeVar | function | — | — | — |
| boss_dragon_of_nightmareAI | ctor | Creature.Main/SetAInitializeOnRespawn, ScriptedAI/ScriptedAI | boss_emeriss/boss_emerissAI, boss_lethon/boss_lethonAI, boss_taerar/boss_taerarAI, boss_ysondre/boss_ysondreAI | — |
| Reset | method | shared_Util/urand | boss_emeriss/Reset, boss_lethon/Reset, boss_taerar/Reset, boss_ysondre/Reset | — |
| Aggro | method | CreatureAI/DoCastSpellIfCan | boss_emeriss/Aggro, boss_lethon/Aggro, boss_taerar/Aggro, boss_ysondre/Aggro | — |
| DoSpecialAbility | decl | — | — | — |
| UpdateDragonAI | method | — | — | — |
| EnterEvadeMode | method | ScriptedAI/EnterEvadeMode, Unit.Main/RemoveAurasDueToSpell, Unit.Main/RemoveGuardians | boss_taerar/EnterEvadeMode | — |
| JustDied | method | CreatureAI/JustDied | — | — |
| UpdateAI | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, ScriptedAI/EnterEvadeIfOutOfHomeArea, shared_Util/urand, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/GetHealthPercent, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, WorldObject.Object/IsWithinLOSInMap | — | — |
| npc_dream_fogAI | ctor | ScriptedPetAI/ScriptedPetAI, Unit.Main/SetReactState | — | — |
| Reset#2 | method | — | — | — |
| ResetCreature | method | CreatureAI/DoCastSpellIfCan | — | — |
| AttackedBy | method | — | — | — |
| GetNextTarget | method | Creature.Main/SelectAttackingTarget, Object/ToUnit, Unit.Main/GetOwner, Unit.Main/GetVictim | — | — |
| ChangeTarget | method | ScriptedPetAI/AttackStart, shared_Util/urand, ThreatManager/getThreat, ThreatManager/modifyThreatPercent#2, Unit.Main/GetThreatManager, Unit.Main/GetVictim | — | — |
| UpdatePetAI | method | shared_Util/urand, Unit.Main/GetVictim, WorldObject.Object/IsWithinDistInMap | — | — |
| go_putrid_shroomAI | ctor | GameObjectAI/GameObjectAI | — | — |
| UpdateAI#2 | method | GameObject/Delete | — | — |
| GetAI_npc_dream_fog | function | — | — | — |
| GetAI_npc_spirit_shade | function | boss_lethon/npc_spirit_shadeAI | — | — |
| GetAI_npc_shade_of_taerar | function | boss_taerar/npc_shade_of_taerarAI | — | — |
| GetAI_npc_demented_druid | function | boss_ysondre/npc_demented_druidAI | — | — |
| GetAI_go_putrid_shroom | function | — | — | — |
| GetAI_boss_dragon_of_nightmare | function | boss_emeriss/boss_emerissAI, boss_lethon/boss_lethonAI, boss_taerar/boss_taerarAI, boss_ysondre/boss_ysondreAI, Creature.Main/UpdateEntry, Object/GetEntry, ObjectMgr/GetSavedVariable | — | — |
| OnSetTargetMap | method | — | — | — |
| GetScript_EmeraldDragonsDreamFog | function | — | — | — |
| AddSC_dragons_of_nightmare | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
