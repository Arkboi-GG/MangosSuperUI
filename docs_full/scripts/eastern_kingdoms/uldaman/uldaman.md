<!-- provenance: verbose -->
# uldaman

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Uldaman Dungeon Scripts

## Purpose & Responsibilities

The `uldaman` translation unit implements creature AI, game object interactions, event handlers, and spell modifications for the **Uldaman** dungeon. It manages three specific trash mob behaviors (Stone Keeper, Jadespine Basilisk, Annora), triggers for instance state changes (Ironaya door, Stone Keeper awakening), and a spell targeting fix for the Vault Warder encounter. It does not implement the main bosses (Ironaya or Archaedas) but provides the supporting mechanics required for their encounters to function correctly.

## Member-by-Member Behavior

### Instance Triggers & Game Objects

**`GOHello_go_keystone_chamber`**
Handles player interaction with the Keystone Chamber game object. It retrieves the `ScriptedInstance` and records the interacting player’s GUID in the instance data (index 0), likely to designate them as the initial target for the subsequent Ironaya encounter. It marks the `ULDAMAN_ENCOUNTER_IRONAYA_DOOR` as `DONE` to persist the door-open state and sets the `GO_FLAG_INTERACT_COND` flag on the object to prevent further interactions.

**`ProcessEventId_event_awaken_stone_keeper`**
A hook for scripted event ID `2228` (triggered by `SPELL_ULDMAN_SUB_BOSS_AGGRO`). It verifies the source is a Player and a target exists. If valid, it sets the `ULDAMAN_ENCOUNTER_STONE_KEEPERS` state to `IN_PROGRESS` in the instance data. Returning `true` suppresses any default database script execution for this event.

### Stone Keeper AI (`mob_stone_keeperAI`)

**`mob_stone_keeperAI`**
Constructor that initializes the AI, caches the `ScriptedInstance` pointer, and calls `Reset()`.

**`Reset#3`**
Sets `m_uiTrample_Timer` to a random interval between 4000ms and 9000ms.

**`EnterEvadeMode`**
Attempts to re-aggro the nearest hostile unit in range. If successful, it starts attacking. If no target is found, it resets timers and marks the `ULDAMAN_ENCOUNTER_STONE_KEEPERS` encounter as `FAIL` in the instance data, indicating that losing aggro on these mobs breaks the encounter progression.

**`JustDied`**
Marks the `ULDAMAN_ENCOUNTER_STONE_KEEPERS` encounter as `IN_PROGRESS` in the instance data, signaling that this mob has been cleared.

**`UpdateAI#3`**
Standard combat loop. If `m_uiTrample_Timer` expires, it casts `SPELL_TRAMPLE` on the current victim and resets the timer to a random 4000–10000ms interval. It also performs melee attacks when ready.

**`GetAI_mob_stone_keeper`**
Factory function returning a new `mob_stone_keeperAI` instance.

### Jadespine Basilisk AI (`mob_jadespine_basiliskAI`)

**`mob_jadespine_basiliskAI`**
Constructor that initializes the AI and calls `Reset()`.

**`Reset#2`**
Sets `Cslumber_Timer` to 2000ms.

**`UpdateAI#2`**
Implements the "Crystalline Slumber" mechanic. When `Cslumber_Timer` expires:
1. Casts `SPELL_CRYSTALLINE_SLUMBER` on the current victim.
2. Reduces the victim's threat by 100% to remove them from the threat table.
3. Resets the timer to 28000ms.
4. Selects a new target: it picks the top aggro target, but if that target is the same as the current victim (who just slept), it skips to the second-highest aggro target.
5. Starts attacking the new target.
It also performs melee attacks when ready.

**`GetAI_mob_jadespine_basilisk`**
Factory function returning a new `mob_jadespine_basiliskAI` instance.

### Annora AI (`AnnoraAI`)

**`AnnoraAI`**
Constructor that hides the creature (`VISIBILITY_OFF`), sets immunity flags (`UNIT_FLAG_IMMUNE_TO_PLAYER | UNIT_FLAG_IMMUNE_TO_NPC`) to prevent accidental aggro, initializes counters, and calls `Reset()`.

**`Reset`**
Empty implementation.

**`Aggro`**
Empty implementation; the creature is immune and hidden until spawned.

**`UpdateAI`**
Manages conditional spawning. If `isSpawned` is false, it searches for creatures with entry `7078` (Earthen Guardians/Scorpions) within 30 yards. If none are alive, it sets visibility to `VISIBILITY_ON`, moves the creature to coordinates `(-164.3657, 210.7687, -49.572)` via `MovePoint`, and sets `isSpawned` to true. Once spawned, it performs standard melee attacks if engaged.

**`GetAI_annora`**
Factory function returning a new `AnnoraAI` instance.

### Spell Modification

**`OnSetTargetMap`**
Part of `UldamanAwakenVaultWarderScript`. Overrides the targeting logic for spell `10258` ("Awaken Vault Warder") to hardcode `unMaxTargets` to 2, ensuring it awakens exactly two Vault Warders regardless of other targeting parameters.

**`GetScript_UldamanAwakenVaultWarder`**
Factory function returning a new `UldamanAwakenVaultWarderScript` instance.

### Registration

**`AddSC_uldaman`**
Registers all scripts defined in this unit (`mob_annora`, `mob_jadespine_basilisk`, `mob_stone_keeper`, `go_keystone_chamber`, `event_awaken_stone_keeper`, `spell_uldaman_awaken_vault_warder`) with the `ScriptMgr`.

## Cross-Unit Boundaries

*   **InstanceData:** `GOHello_go_keystone_chamber`, `ProcessEventId_event_awaken_stone_keeper`, and `mob_stone_keeperAI` call `SetData`/`SetData64` on `ScriptedInstance` to update dungeon state (encounter progress, failure, or player tracking).
*   **WorldObject/Object:** Used to retrieve instance data pointers (`GetInstanceData`) and modify game object flags (`SetUInt32Value`).
*   **CreatureAI/ScriptedAI:** All creature AIs inherit from `ScriptedAI` and use helpers like `DoCastSpellIfCan`, `DoMeleeAttackIfReady`, and `AttackStart`.
*   **Unit/Main:** Used for target selection (`SelectHostileTarget`, `SelectAttackingTarget`), victim retrieval (`GetVictim`), threat manipulation (`GetThreatManager`), and movement (`GetMotionMaster`).
*   **shared_Util:** `urand` is used for randomizing spell timers.
*   **GridSearchers:** `AnnoraAI` uses `GetCreatureListWithEntryInGrid` to detect nearby scorpions.

## Data Model

This unit does not access database tables directly. It relies on the in-memory `ScriptedInstance` data structure, using enum indices defined in `uldaman.h` (e.g., `ULDAMAN_ENCOUNTER_IRONAYA_DOOR`) to store and retrieve encounter states.

## Notable Implementation Details

*   **Basilisk Threat Management:** `mob_jadespine_basiliskAI` explicitly reduces threat by 100% after casting `CRYSTALLINE_SLUMBER` and implements logic to skip the current victim when selecting a new target, preventing the mob from re-engaging the sleeping player.
*   **Annora Spawn Condition:** Annora remains hidden and immune until `UpdateAI` confirms zero alive creatures of entry `7078` within 30 yards. Her spawn position is hardcoded.
*   **Stone Keeper Fail State:** `EnterEvadeMode` in `mob_stone_keeperAI` marks the encounter as `FAIL`, making these mobs critical to encounter progression rather than optional trash.
*   **Hardcoded Spell Targets:** `OnSetTargetMap` forces the "Awaken Vault Warder" spell to target exactly 2 units, overriding default spell targeting behavior.

## Member Reference

**`GOHello_go_keystone_chamber`**: Handles interaction with the Keystone Chamber. Records the player's GUID in instance data, marks the Ironaya door encounter as done, and sets the game object's interaction condition flag.

**`ProcessEventId_event_awaken_stone_keeper`**: Processes the scripted event to awaken Stone Keepers. Validates the source is a player and updates instance data to mark the encounter as in progress.

**`mob_stone_keeperAI`**: Constructor for the Stone Keeper AI. Initializes instance data pointer and resets timers.

**`Reset#3`**: Resets the Stone Keeper's trample timer to a random value between 4000ms and 9000ms.

**`EnterEvadeMode`**: Handles evasion for the Stone Keeper. Attempts to re-aggro the nearest hostile unit. If unsuccessful, resets timers and marks the Stone Keepers encounter as failed in instance data.

**`JustDied`**: Marks the Stone Keepers encounter as in progress in instance data upon the creature's death.

**`UpdateAI#3`**: Main combat loop for the Stone Keeper. Casts `SPELL_TRAMPLE` on the victim when the timer expires and performs melee attacks.

**`GetAI_mob_stone_keeper`**: Factory function to create a new `mob_stone_keeperAI` instance.

**`mob_jadespine_basiliskAI`**: Constructor for the Jadespine Basilisk AI. Initializes and resets timers.

**`Reset#2`**: Resets the Basilisk's slumber timer to 2000ms.

**`UpdateAI#2`**: Main combat loop for the Jadespine Basilisk. Casts `SPELL_CRYSTALLINE_SLUMBER` on the current victim, reduces their threat by 100%, and switches to a new target (skipping the current victim if they are still the highest threat). Performs melee attacks.

**`GetAI_mob_jadespine_basilisk`**: Factory function to create a new `mob_jadespine_basiliskAI` instance.

**`AnnoraAI`**: Constructor for Annora's AI. Hides the creature, makes it immune to damage, and initializes state variables.

**`Reset`**: Empty reset function for Annora.

**`Aggro`**: Empty aggro function for Annora.

**`UpdateAI`**: Checks if all nearby scorpions (entry 7078) are dead. If so, makes Annora visible, moves her to a specific location, and enables combat. Otherwise, performs melee attacks if engaged.

**`GetAI_annora`**: Factory function to create a new `AnnoraAI` instance.

**`OnSetTargetMap`**: Modifies the "Awaken Vault Warder" spell to target exactly 2 units.

**`GetScript_UldamanAwakenVaultWarder`**: Factory function to create the spell script for "Awaken Vault Warder".

**`AddSC_uldaman`**: Registers all scripts defined in this file (creature AIs, game object handlers, event processors, and spell scripts) with the server.

---

<!-- machine-true, projected from graph.json -->

## Map — uldaman

*Source:* uldaman.cpp, uldaman.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GOHello_go_keystone_chamber | function | InstanceData/SetData, InstanceData/SetData64, Object/GetGUID, WorldObject.Object/GetInstanceData, WorldObject.Object/SetUInt32Value | — | — |
| ProcessEventId_event_awaken_stone_keeper | function | InstanceData/SetData, Object/GetTypeId, WorldObject.Object/GetInstanceData | — | — |
| mob_stone_keeperAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset#3 | method | shared_Util/urand | — | — |
| EnterEvadeMode | method | Creature.Main/SelectNearestHostileUnitInAggroRange, CreatureAI/AttackStart, InstanceData/SetData | — | — |
| JustDied | method | InstanceData/SetData | — | — |
| UpdateAI#3 | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_mob_stone_keeper | function | — | — | — |
| mob_jadespine_basiliskAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#2 | method | — | — | — |
| UpdateAI#2 | method | Creature.Main/SelectAttackingTarget, CreatureAI/AttackStart, CreatureAI/DoMeleeAttackIfReady, SpellCaster/CastSpell#2, ThreatManager/modifyThreatPercent#2, Unit.Main/GetThreatManager, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_mob_jadespine_basilisk | function | — | — | — |
| AnnoraAI | ctor | ScriptedAI/ScriptedAI, Unit.Main/SetVisibility, WorldObject.Object/SetFlag | — | — |
| Reset | method | — | — | — |
| Aggro | method | — | — | — |
| UpdateAI | method | Creature.MotionMaster/MovePoint, CreatureAI/DoMeleeAttackIfReady, GridSearchers/GetCreatureListWithEntryInGrid#2, Unit.Main/GetMotionMaster, Unit.Main/GetVictim, Unit.Main/IsAlive, Unit.Main/SelectHostileTarget, Unit.Main/SetVisibility | — | — |
| GetAI_annora | function | — | — | — |
| OnSetTargetMap | method | — | — | — |
| GetScript_UldamanAwakenVaultWarder | function | — | — | — |
| AddSC_uldaman | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
