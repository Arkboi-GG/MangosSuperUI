<!-- provenance: verbose -->
# boss_urok

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_urok

## Purpose & Responsibilities

`boss_urok.cpp` implements the scripted encounter for **Urok Doomhowl** (`NPC_UROK_DOOMHOWL`). The encounter is triggered by interacting with a central `GameObject` (`GO_CHALLENGE_UROK`, entry 175584). It operates as a wave-based summoning system: the controller spawns six rune circles, then summons underlings (Ogre Magi and Enforcers) at these locations. Players must defeat these underlings; as they die, new ones spawn in a fixed sequence until eight total deaths occur, at which point the boss is summoned.

A concurrent "fail state" mechanic requires players to prevent underlings from reaching and destroying the central challenge object. Underlings move toward the object and cast `SPELL_DESTROY_SPEAR`; if successful, the encounter ends immediately. Players can right-click the challenge object to cast a high-damage spell (`SPELL_KILL_UROK_ADD`) on the nearest underling, aiding in wave clearance.

This unit contains:
1.  **`go_urok_challengeAI`**: The controller for the central `GameObject`. It manages the timeline, spawns runes/underlings/boss, tracks deaths, and handles player interaction.
2.  **`urokUnderlingAI`**: The base AI for summoned underlings. It handles movement toward the banner to destroy it, combat logic, and reporting death back to the controller.
3.  **`urokEnforcerAI`** and **`urokOgreMagusAI`**: Subclasses of `urokUnderlingAI` defining specific spell rotations.
4.  **Helper functions**: `DefineGoChallenge` links underlings to the controller; `ProcessEventId_event_banner_destroyed` handles the event trigger for banner destruction.

## Member-by-Member Behavior

### Encounter Controller (`go_urok_challengeAI`)

Inherits from `GameObjectAI`. State is tracked via `_actived`, `_step`, `_timer`, `_spellTimer`, `_runes` (array of 6 GUIDs), `nbDeadUnderlings`, and `guidCurrentUnderlings` (array of 3 GUIDs).

*   **`go_urok_challengeAI` (ctor)**: Initializes `_actived` to true, timers to 0, and `nbDeadUnderlings` to 0.
*   **`UpdateAI`**: Decrements timers. If `_actived`:
    *   **Step 0**: Spawns 6 rune circles (`GO_SUMMON_CIRCLE`) at hardcoded coordinates via `SpawnRune`. Sets `_timer` to 3000ms.
    *   **Step 1**: Spawns initial wave: one Ogre Mage (`NPC_UROK_MAGE`) at rune 0, two Enforcers (`NPC_UROK_MASSACRER`) at runes 2 and 3 via `SpawnAtRune`. Sets `_timer` to 10000ms.
    *   Increments `_step`. No further actions are defined for steps > 1; subsequent spawns are driven by `UrokUnderlingDied`.
*   **`SpawnAtRune`**: Retrieves the `GameObject` for the given rune index. If valid, summons the creature entry at the rune's position.
    *   If the creature is the boss (`NPC_UROK_DOOMHOWL`), casts visual spell `SPELL_UROK_SUMMONED`.
    *   Otherwise, stores the creature's GUID in `guidCurrentUnderlings[i]`, calls `DefineGoChallenge` to link it to this controller, and casts visual spell `SPELL_UROK_ADD_SUMMONED`.
    *   Aggroes all alive, non-friendly players on the map by calling `AttackStart` on the new creature.
*   **`SpawnBoss`**: Calls `SpawnAtRune` for the boss at rune 5, then calls `DespawnRunes`.
*   **`SpawnRune`**: Summons a `GO_SUMMON_CIRCLE` at specified coordinates/orientation and stores its GUID in `_runes[i]`.
*   **`DespawnRunes`**: Iterates `_runes`, finds each `GameObject`, and adds it to the removal list.
*   **`NearestOgre`**: Finds the closest `NPC_UROK_MASSACRER` or `NPC_UROK_MAGE` within 20 yards. Prioritizes the closer of the two if both exist. Returns `nullptr` if none found.
*   **`OnUse`**: Triggered by player right-click. If `_actived` and `_spellTimer` is 0:
    *   Finds nearest underling via `NearestOgre`.
    *   If found, sets `_spellTimer` to 30000ms and casts `SPELL_KILL_UROK_ADD` on the target.
*   **`EventBannerDestroyed`**: Sets `_actived` to false, calls `DespawnRunes`, and despawns the challenge object itself.
*   **`UrokUnderlingDied`**: Called when an underling dies.
    *   Checks if the dead GUID matches any in `guidCurrentUnderlings`.
    *   If matched, increments `nbDeadUnderlings`.
    *   If `nbDeadUnderlings` < 8: Spawns a new random underling (Mage or Enforcer) at the rune index specified by `runeOrder[nbDeadUnderlings+2]`, replacing the dead one's slot in `guidCurrentUnderlings`.
    *   If `nbDeadUnderlings` == 8: Calls `SpawnBoss`.

### Underling Base AI (`urokUnderlingAI`)

Inherits from `ScriptedAI`. Tracks `timer` and `guidMound` (GUID of the challenge object).

*   **`urokUnderlingAI` (ctor)**: Initializes `timer` to 0.
*   **`Reset`**: Resets `timer` to 0.
*   **`JustDied`**: Retrieves the `GameObject` for `guidMound`. If found, casts its AI to `go_urok_challengeAI` and calls `UrokUnderlingDied` with the creature's GUID.
*   **`MovementInform`**: If movement type is `POINT_MOTION_TYPE` and ID is 2, calls `HitBanner`.
*   **`AttackStart`**: Aborts if the creature is channeling a spell (protecting banner destruction). Otherwise, calls `ScriptedAI::AttackStart`.
*   **`BannerDestroyed`**: Finds nearest `GO_CHALLENGE_UROK`. If found, casts its AI to `go_urok_challengeAI` and calls `EventBannerDestroyed`.
*   **`HitBanner`**: Finds nearest `GO_CHALLENGE_UROK` within `CONTACT_DISTANCE+1`. If found, casts `SPELL_DESTROY_SPEAR` at its position, sets `timer` to 11000ms, and returns true.
*   **`UpdateAI#2`**:
    *   If no victim:
        *   If `timer` expires, attempts `HitBanner`.
        *   If `HitBanner` fails, finds the challenge object, calculates a contact point, and moves to it (`MovePoint` ID 2). Resets `timer` to 10000ms.
        *   Else decrements `timer`.
    *   If victim exists: Calls `abilityCombatUpdate` and `DoMeleeAttackIfReady`.
*   **`abilityCombatUpdate#3`**: Virtual placeholder, overridden by subclasses.
*   **`SetMoundGuid`**: Stores the challenge object's GUID in `guidMound`.

### Specialized Underling AIs

*   **`urokEnforcerAI` (ctor)**: Initializes via `urokUnderlingAI`.
*   **`abilityCombatUpdate`**: Updates spell lists and performs melee attacks.
*   **`GetAI_npc_urok_enforcer`**: Factory for `urokEnforcerAI`.
*   **`urokOgreMagusAI` (ctor)**: Initializes via `urokUnderlingAI`.
*   **`abilityCombatUpdate#2`**: Updates spell lists and performs melee attacks.
*   **`GetAI_npc_urok_ogre_magus`**: Factory for `urokOgreMagusAI`.

### Helpers & Registration

*   **`GetAIgo_urok_challenge`**: Factory for `go_urok_challengeAI`.
*   **`ProcessEventId_event_banner_destroyed`**: Handles event ID 4777.
    *   If `target` is null and `source` is a creature, casts its AI to `urokUnderlingAI` and calls `BannerDestroyed`.
    *   If `target` is a GameObject, casts its AI to `go_urok_challengeAI` and calls `EventBannerDestroyed`.
    *   Returns `true` to override default DB handling.
*   **`DefineGoChallenge`**: Casts creature AI to `urokUnderlingAI` and calls `SetMoundGuid` with the provided GUID.
*   **`AddSC_boss_urok`**: Registers scripts for `go_urok_challenge`, `npc_urok_enforcer`, `npc_urok_ogre_magus`, and `event_banner_destroyed`.

## Cross-Unit Boundaries

*   **`go_urok_challengeAI` -> `Creature`/`CreatureAI`**: `SpawnAtRune` summons creatures (`WorldObject.Object/SummonCreature`), sets respawn delays (`Creature.Main/SetRespawnDelay`), sends visual spells (`Unit.Main/SendSpellGo`), and initiates combat (`CreatureAI/AttackStart`).
*   **`go_urok_challengeAI` -> `GameObject`/`Map`**: `SpawnRune` summons runes (`WorldObject.Object/SummonGameObject`). `DespawnRunes` removes them (`WorldObject.Object/AddObjectToRemoveList`). `SpawnAtRune` iterates map players (`Map.Main/GetPlayers`).
*   **`urokUnderlingAI` -> `go_urok_challengeAI`**: `JustDied` and `BannerDestroyed` retrieve the challenge object (`Map.Main/GetGameObject` or `WorldObject.Object/FindNearestGameObject`), cast its AI, and call `UrokUnderlingDied` or `EventBannerDestroyed`.
*   **`urokUnderlingAI` -> `GameObject`**: `HitBanner` and `UpdateAI#2` locate the challenge object (`WorldObject.Object/FindNearestGameObject`), get positions (`WorldObject.Object/GetPosition`), and calculate contact points (`WorldObject.Object/GetContactPoint`).
*   **`ProcessEventId_event_banner_destroyed` -> `urokUnderlingAI`/`go_urok_challengeAI`**: Acts as an event bridge, casting `Creature/AI` or `GameObject/AI` to route to `BannerDestroyed` or `EventBannerDestroyed`.

## Data Model

This unit does not query or modify any database tables. All configuration (entries, spells, coordinates, timers) is hardcoded.

## Notable Implementation Details

1.  **Fixed Wave Logic**: `guidCurrentUnderlings` holds exactly 3 GUIDs. The system assumes exactly 3 underlings are active during the wave phase. `UrokUnderlingDied` replaces the dead GUID in this array. If more than 3 were spawned, tracking would fail.
2.  **Rune Order**: Respawn positions are dictated by the hardcoded `runeOrder` array `{0,2,3,1,4,5,1,2,3,5}`. This sequence does not check if a rune is currently occupied, relying on the assumption that the dead underling's slot corresponds to the next spawn index.
3.  **Channeling Protection**: `urokUnderlingAI::AttackStart` checks for `CURRENT_CHANNELED_SPELL`. This prevents players from interrupting the banner destruction spell by attacking the underling mid-cast.
4.  **No Boss AI**: This file only summons the boss (`NPC_UROK_DOOMHOWL`). The boss's own AI is not defined here.
5.  **Event Fragility**: `ProcessEventId_event_banner_destroyed` handles null targets, suggesting the spell effect triggering the event may not always provide a valid target object.

## Member Reference

**go_urok_challengeAI** (ctor): Initializes the challenge object AI, setting activation state, timers, and counters.
**UpdateAI**: Manages the encounter timeline, spawning runes and initial underlings based on step and timer.
**SpawnAtRune**: Summons a creature at a specific rune, links it to the controller, applies visuals, and aggroes players.
**SpawnBoss**: Wrapper to summon the boss at rune 5 and despawn all runes.
**SpawnRune**: Summons a rune circle GameObject at given coordinates and stores its GUID.
**DespawnRunes**: Iterates through stored rune GUIDs and despawns the corresponding GameObjects.
**NearestOgre**: Finds the closest underling (Mage or Enforcer) within 20 yards, prioritizing distance.
**OnUse**: Allows players to cast a damaging spell on the nearest underling, subject to a 30s cooldown.
**EventBannerDestroyed**: Deactivates the encounter, despawns runes and the challenge object.
**UrokUnderlingDied**: Tracks underling deaths, spawns replacements based on a fixed order, or summons the boss after 8 deaths.
**GetAIgo_urok_challenge**: Factory function for `go_urok_challengeAI`.
**urokUnderlingAI** (ctor): Initializes the underling AI timer.
**Reset**: Resets the underling AI timer.
**JustDied**: Reports the creature's death to the challenge object controller.
**MovementInform**: Triggers banner hitting logic when the creature reaches the designated movement point.
**AttackStart**: Prevents attack initiation if the creature is channeling a spell (e.g., destroying the banner).
**BannerDestroyed**: Notifies the challenge object controller that the banner has been destroyed.
**HitBanner**: Casts a spell to destroy the banner if the creature is close enough.
**UpdateAI#2**: Main underling loop; moves to banner if not in combat, fights if in combat.
**abilityCombatUpdate#3**: Virtual placeholder for combat abilities, overridden by subclasses.
**SetMoundGuid**: Stores the GUID of the challenge object for later reference.
**urokEnforcerAI** (ctor): Initializes the Enforcer AI.
**abilityCombatUpdate**: Implements combat logic for Enforcers (spells and melee).
**GetAI_npc_urok_enforcer**: Factory function for `urokEnforcerAI`.
**urokOgreMagusAI** (ctor): Initializes the Ogre Magus AI.
**abilityCombatUpdate#2**: Implements combat logic for Ogre Magi (spells and melee).
**GetAI_npc_urok_ogre_magus**: Factory function for `urokOgreMagusAI`.
**ProcessEventId_event_banner_destroyed**: Handles the global event for banner destruction, routing to appropriate AI methods.
**DefineGoChallenge**: Links a summoned underling to the challenge object controller by setting its mound GUID.
**AddSC_boss_urok**: Registers all scripts defined in this file with the script manager.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_urok

*Source:* boss_urok.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| go_urok_challengeAI | ctor | GameObjectAI/GameObjectAI | — | — |
| UpdateAI | method | — | — | — |
| SpawnAtRune | method | Creature.Main/AI, Creature.Main/SetRespawnDelay, CreatureAI/AttackStart, Map.Main/GetGameObject, Map.Main/GetPlayers, Object/GetGUID, Unit.Main/IsAlive, Unit.Main/IsFriendlyTo, Unit.Main/SendSpellGo, WorldObject.Object/GetMap, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SummonCreature#2 | — | — |
| SpawnBoss | method | — | — | — |
| SpawnRune | method | Object/GetObjectGuid, WorldObject.Object/SummonGameObject | — | — |
| DespawnRunes | method | Map.Main/GetGameObject, WorldObject.Object/AddObjectToRemoveList, WorldObject.Object/GetMap | — | — |
| NearestOgre | method | WorldObject.Object/FindNearestCreature, WorldObject.Object/GetDistance#3 | — | — |
| OnUse | method | SpellCaster/CastSpell#2 | — | — |
| EventBannerDestroyed | method | GameObject/Despawn | — | — |
| UrokUnderlingDied | method | shared_Util/urand | — | — |
| GetAIgo_urok_challenge | function | — | — | — |
| urokUnderlingAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | method | — | — | — |
| JustDied | method | GameObject/AI, Map.Main/GetGameObject, Object/GetGUID, ObjectGuid/ObjectGuid#5, WorldObject.Object/GetMap | — | — |
| MovementInform | method | — | — | — |
| AttackStart | method | CreatureAI/AttackStart, SpellCaster/GetCurrentSpell | — | — |
| BannerDestroyed | method | GameObject/AI, Object/GetGUID, WorldObject.Object/FindNearestGameObject | — | — |
| HitBanner | method | SpellCaster/CastSpell#4, WorldObject.Object/FindNearestGameObject, WorldObject.Object/GetPosition#2 | — | — |
| UpdateAI#2 | method | Creature.MotionMaster/MovePoint, CreatureAI/DoMeleeAttackIfReady, Unit.Main/GetMotionMaster, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, WorldObject.Object/FindNearestGameObject, WorldObject.Object/GetContactPoint | — | — |
| abilityCombatUpdate#3 | method | — | — | — |
| SetMoundGuid | method | — | — | — |
| urokEnforcerAI | ctor | — | — | — |
| abilityCombatUpdate | method | CreatureAI/DoMeleeAttackIfReady, CreatureAI/UpdateSpellsList | — | — |
| GetAI_npc_urok_enforcer | function | — | — | — |
| urokOgreMagusAI | ctor | — | — | — |
| abilityCombatUpdate#2 | method | CreatureAI/DoMeleeAttackIfReady, CreatureAI/UpdateSpellsList | — | — |
| GetAI_npc_urok_ogre_magus | function | — | — | — |
| ProcessEventId_event_banner_destroyed | function | Creature.Main/AI, GameObject/AI, Object/GetGUID, Object/GetObjectGuid, Object/IsCreature, ObjectGuid/IsGameObject | — | — |
| DefineGoChallenge | function | Creature.Main/AI | — | — |
| AddSC_boss_urok | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
