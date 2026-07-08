# boss_twinemperors

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_twinemperors

## Purpose & Responsibilities

This unit implements the artificial intelligence and game mechanics for the **Twin Emperors** encounter (Emperor Vek'lor and Emperor Vek'nilash) in the **Temple of Ahn'Qiraj** raid instance. It handles two distinct phases of gameplay: the coordinated dual-boss fight and the management of summoned "Bug" minions that assist the bosses.

Key responsibilities include:
1.  **Shared Health Pool Logic:** Implementing the mechanic where damage taken by one emperor is mirrored onto the other, and healing applied to one is mirrored onto the other.
2.  **Teleportation Mechanic:** Managing the periodic swap of positions between the two emperors, including a brief "freeze" period where they cannot act or be targeted normally, followed by threat adjustment to the nearest player.
3.  **Individual Boss Abilities:**
    *   **Vek'lor:** Ranged Shadowbolt spam, Arcane Burst (close-range AoE), Blizzard (AoE ground effect), and Explode Bug (summoning explosive bugs).
    *   **Vek'nilash:** Melee-focused abilities including Upper Cut, Unbalancing Strike, Double Attack aura, and Mutate Bug (summoning aggressive bugs).
4.  **Bug Minion AI:** Controlling the behavior of mutated/exploding bugs, which cast Pierce Armor and Acid Spit on their targets.
5.  **Encounter State Management:** Interfacing with the `instance_temple_of_ahnqiraj` script to track encounter progress (IN_PROGRESS, DONE, FAIL), manage door states, and trigger spawn events for Anubisath Defenders upon aggro.

## Member-by-Member Behavior

### Bug Minion AI (`mob_TwinsBug`)

The `mob_TwinsBug` struct defines the behavior for the bug minions summoned by the emperors.

*   **`mob_TwinsBug` (ctor):** Initializes the bug AI and calls `Reset` to set initial timers and faction.
*   **`GoBeBadBug`:** Activates the bug. It applies a specific aura (either `SPELL_MUTATE_BUG` or `SPELL_EXPLODEBUG`), sets the faction to hostile (ID 14), and forces combat with the zone. If the bug is a "Mutated" bug, it also restores the bug to full health.
*   **`JustDied`:** Resets the bug's faction to neutral (ID 7) and removes all auras upon death.
*   **`Reset`:** Resets the bug's faction to neutral (ID 7), removes auras, and initializes internal timers for `Pierce Armor` (5s) and `Acid Spit` (6s).
*   **`UpdateAI`:** The main loop for the bug. It checks for a valid victim. If `pierceArmorTimer` expires, it casts `SPELL_PIERCE_ARMOR` on the victim and resets the timer (5–9s). If `acidSpitTimer` expires, it casts `SPELL_ACID_SPIT` on the victim and resets the timer (6–12s). It also attempts melee attacks if ready.

### Shared Emperor AI (`boss_twinemperorsAI`)

This base class contains logic shared by both Vek'lor and Vek'nilash.

*   **`boss_twinemperorsAI` (ctor):** Retrieves the instance data (`instance_temple_of_ahnqiraj`). If the instance pointer is invalid, it logs an error. If the encounter hasn't started (`TwinsDialogueStartedOrDone` returns false), it sets the creature's stand state to kneeling.
*   **`SharedReset`:** Resets shared timers (`respawnBugTimer`, `EnrageTimer`) and flags (`justTeleported`, `didPullDialogue`). It also clears the stunned state from the creature.
*   **`MoveInLineOfSight`:** Triggers aggro if a player enters line of sight within `PULL_RANGE` (50 yards) or the creature's attack radius, provided the creature is not already in combat. It checks for vertical distance constraints (Z-axis difference <= 7) to prevent pulling from distant stairs.
*   **`AttackedBy`:** Prevents the emperor from engaging in combat if the `justTeleported` flag is true, effectively ignoring attacks during the teleport freeze window.
*   **`DamageTaken`:** Implements the shared health pool. It calculates the percentage of health lost by the damaged emperor and applies the equivalent percentage of maximum health loss to the other emperor (`GetOtherBoss`). It uses `SetHealth` and `CountDamageTaken` to ensure the damage is registered correctly in the engine.
*   **`JustDied`:** Marks the encounter as `DONE` in the instance data if not already done. It then kills the other emperor using the original killer's GUID to ensure proper loot/credit handling.
*   **`Aggro`:** Sets the encounter state to `IN_PROGRESS`. It searches for nearby `NPC_ANUBISATH_DEFENDER` creatures (within 800 yards). If any are alive, it activates them and commands them to attack the aggroing player. If no defenders are found, it resets the entrance door (`GO_TWINS_ENTER_DOOR`). It also commands the other emperor to attack the same player.
*   **`JustReachedHome`:** Sets the encounter state to `FAIL` in the instance data if the boss despawns or evades.
*   **`HealedBy`:** Mirrors healing to the other emperor. It calculates the percentage of health gained and applies the equivalent percentage of maximum health gain to the other emperor, capping at maximum health.
*   **`UpdateAI`:** The main update loop.
    *   Checks if the boss is outside the arena (Z > -95.0f) and evades if so.
    *   Handles the `justTeleported` state: If true, it waits for `JUST_TELEPORTED_FREEZE` (2s) to pass. During this time, it identifies the closest player to apply extra threat (`AFTER_TELEPORT_THREAT`) and prepares to re-engage.
    *   If not teleporting, it selects a hostile target.
    *   Calls sub-updates: `CheckEnrage`, `UpdateTeleportToMyBrother`, `HandleBugSpell`, `TryHealBrother`, and `UpdateEmperor` (virtual, implemented by children).
*   **`GetOtherBoss`:** Returns a pointer to the other emperor based on the current creature's entry ID.
*   **`OnStartTeleport`:** Initiates the teleport sequence. Sets `justTeleported` to true, interrupts spells, stops movement, and teleports the creature to the specified coordinates. It casts visual/message spells (`SPELL_TWIN_TELEPORT_MSG`, `SPELL_TWIN_TELEPORT_VISUAL`) and resets the threat list.
*   **`OnEndTeleport`:** Called after the teleport freeze ends. It re-engages the closest player identified during the freeze and calls the virtual `OnEndTeleportVirtual`.
*   **`HandleBugSpell`:** Manages the spawning/activation of bugs. If the timer expires and the boss is not teleporting, it finds nearby bugs (`BUG_TYPE_1`, `BUG_TYPE_2`) within `BUG_SPELL_MAX_DIST` (20 yards) that are alive and not already affected by a mutation/explosion spell. It randomly selects one and calls `GoBeBadBug` with the spell defined by `GetBugSpell`.
*   **`CheckEnrage`:** Applies `SPELL_BERSERK` if the `EnrageTimer` expires and the boss doesn't already have the aura. It resets the timer to 5 minutes.
*   **`GetPlayerInP2PRange`:** Helper function to find a random player within a specific distance range from the threat list, optionally skipping the top aggro target.

### Vek'lor AI (`boss_veklorAI`)

Implements Vek'lor's specific abilities and behaviors.

*   **`boss_veklorAI` (ctor):** Initializes range variables for Shadowbolt and Blizzard by looking up spell range data. Calls `Reset`.
*   **`Reset`:** Calls `SharedReset`. Initializes timers for Shadowbolt, Arcane Burst, Blizzard, Teleport, Heal, and Pull Dialogue. Applies immunity to normal damage. Respawn the twin if dead.
*   **`AttackStart`:** Adjusts chase distance based on target proximity. If the target is close (`<= VEKLOR_DIST`), it sets chase distance to `shadowboltRange` to prevent chasing too far. If far, it sets chase distance to `VEKLOR_DIST`.
*   **`KilledUnit`:** Plays a random kill say if the cooldown allows.
*   **`JustReachedHome`:** Plays a wipe say and calls the parent `JustReachedHome`.
*   **`GetBugSpellCooldown`:** Returns a random cooldown for Explode Bug (7–10s).
*   **`GetBugSpell`:** Returns `SPELL_EXPLODEBUG`.
*   **`UpdateTeleportToMyBrother`:** If the teleport timer expires, it swaps positions with Vek'nilash. It calls `OnStartTeleport` for itself and the other boss, passing each other's coordinates.
*   **`TryHealBrother`:** If the heal timer expires and the boss is not teleporting, it checks if the other boss is within `HEAL_BROTHER_RANGE` (60 yards). If so, it casts `SPELL_HEAL_BROTHER` on the other boss and triggers a reciprocal heal on itself.
*   **`OnEndTeleportVirtual`:** Resets the Shadowbolt timer to 0, allowing immediate casting after teleport.
*   **`UpdateBlizzard`:** If the timer expires, it targets a random player within `blizzardRange` (excluding top aggro) and casts `SPELL_BLIZZARD`.
*   **`updateArcaneBurst`:** If the timer expires, it targets a random player within `ARCANE_BURST_RANGE` (10 yards) and casts `SPELL_ARCANEBURST`.
*   **`UpdateEmperor`:**
    *   Plays pull dialogue after a delay (`VEKLOR_PULL_YELL_DELAY`).
    *   Updates Blizzard and Arcane Burst timers.
    *   Handles Shadowbolt logic: If not in melee and enough time has passed since the last Shadowbolt, it resets the timer to allow immediate casting. If in melee and LOS, it prioritizes melee attacks and reduces Shadowbolt timer. Otherwise, it casts Shadowbolt based on melee/ranged cooldowns.

### Vek'nilash AI (`boss_veknilashAI`)

Implements Vek'nilash's specific abilities and behaviors.

*   **`boss_veknilashAI` (ctor):** Calls `Reset`.
*   **`Reset`:** Calls `SharedReset`. Initializes timers for Upper Cut, Unbalancing Strike, and Bug Mutation. Applies immunity to spell damage. Respawn the twin if dead.
*   **`JustReachedHome`:** Plays a wipe say and calls the parent `JustReachedHome`.
*   **`OnEndTeleportVirtual`:** Currently empty (placeholder).
*   **`GetBugSpellCooldown`:** Returns a random cooldown for Mutate Bug (10–15s).
*   **`GetBugSpell`:** Returns `SPELL_MUTATE_BUG`.
*   **`GetPlayerInMeleeRange`:** Finds a random player from the threat list who is within melee range.
*   **`UpdateEmperor`:**
    *   Plays pull dialogue immediately.
    *   Applies `SPELL_DOUBLE_ATTACK` aura if not present.
    *   Casts `SPELL_UNBALANCING_STRIKE` on the victim if the timer expires.
    *   Casts `SPELL_UPPERCUT` on a random melee player if the timer expires.
    *   Attempts melee attacks.
*   **`KilledUnit`:** Plays a random kill say if the cooldown allows.
*   **`AttackStart`:** Calls the parent `AttackStart`.

### Script Registration Functions

*   **`GetAI_boss_veknilash`**: Factory function returning a new `boss_veknilashAI`.
*   **`GetAI_boss_veklor`**: Factory function returning a new `boss_veklorAI`.
*   **`GetAI_twinsBug`**: Factory function returning a new `mob_TwinsBug`.
*   **`OnSetTargetMap` (EmperorMutateBugScript)**: Limits the `Mutate Bug` spell to affect only 1 target.
*   **`GetScript_EmperorMutateBug`**: Factory function for the Mutate Bug spell script.
*   **`OnSetTargetMap` (EmperorExplodeBugScript)**: Limits the `Explode Bug` spell to affect only 1 target.
*   **`GetScript_EmperorExplodeBug`**: Factory function for the Explode Bug spell script.
*   **`AddSC_boss_twinemperors`**: Registers all scripts (AI and Spell) with the server's script manager.

## Cross-Unit Boundaries

*   **`instance_temple_of_ahnqiraj`**:
    *   **Called by:** `boss_twinemperorsAI` constructor, `JustDied`, `Aggro`, `JustReachedHome`, `GetOtherBoss`.
    *   **Collaboration:** The AI retrieves the instance pointer to manage encounter state (`TYPE_TWINS`), check dialogue status, get the other boss's GUID, and control doors/spawns.
*   **`ScriptedAI`**:
    *   **Called by:** `mob_TwinsBug` constructor, `boss_twinemperorsAI` constructor, `AttackedBy`, `AttackStart` (children).
    *   **Collaboration:** Provides base AI functionality like `DoCastSpellIfCan`, `DoMeleeAttackIfReady`, `EnterEvadeMode`, and `AttackStart`.
*   **`Unit.Main` / `Creature.Main` / `WorldObject.Object`**:
    *   **Called by:** Various methods for health manipulation, position queries, faction changes, aura management, and combat state checks.
    *   **Collaboration:** Direct interaction with the game entity system to modify state and query properties.
*   **`ScriptMgr`**:
    *   **Called by:** `KilledUnit`, `JustReachedHome`, `UpdateEmperor`.
    *   **Collaboration:** Used to broadcast text/say messages (`DoScriptText`).
*   **`Log.Main`**:
    *   **Called by:** `boss_twinemperorsAI` constructor, `UpdateAI`, `OnEndTeleport`.
    *   **Collaboration:** Logs errors (invalid instance cast) and debug info (failed target selection after teleport).
*   **`shared_Util`**:
    *   **Called by:** `UpdateAI`, `Reset`, `GetBugSpellCooldown`, etc.
    *   **Collaboration:** Uses `urand` for random number generation in timers and cooldowns.
*   **`ThreatManager`**:
    *   **Called by:** `UpdateAI`, `GetPlayerInP2PRange`, `GetPlayerInMeleeRange`.
    *   **Collaboration:** Accesses the threat list to identify targets and apply threat modifications.
*   **`SpellCaster`**:
    *   **Called by:** `OnStartTeleport`, `CheckEnrage`, `TryHealBrother`, `UpdateEmperor`.
    *   **Collaboration:** Casts spells directly, often with forced casting (`true` flag) or interrupting non-melee spells.
*   **`GridSearchers`**:
    *   **Called by:** `Aggro`, `HandleBugSpell`.
    *   **Collaboration:** Searches for nearby creatures (Anubisath Defenders, Bugs) within a grid radius.
*   **`ScriptedInstance`**:
    *   **Called by:** `Aggro`.
    *   **Collaboration:** Resets doors and retrieves GameObjects from storage.
*   **`Map.Main` / `ZoneScript`**:
    *   **Called by:** `OnEndTeleport`.
    *   **Collaboration:** Retrieves the map object to find players by GUID.
*   **`ScriptLoader`**:
    *   **Calls:** `AddSC_boss_twinemperors`.
    *   **Collaboration:** The loader invokes this function to register the scripts at server startup.

## Data Model

This unit does not directly interact with any database tables. All data (creature entries, spell IDs, instance states) is managed in-memory via the engine's object systems and instance script interfaces.

## Notable Implementation Details

1.  **Shared Health Pool via Percentage:** The `DamageTaken` and `HealedBy` methods in `boss_twinemperorsAI` do not simply transfer raw damage/heal amounts. They calculate the *percentage* of health change relative to the *maximum* health of the affected boss and apply that same percentage to the other boss's maximum health. This ensures the health bars remain synchronized proportionally, even if the bosses have different max health values (though they likely don't in this encounter).
2.  **Teleport Freeze Window:** The `justTeleported` flag creates a 2-second window where the bosses are effectively invulnerable to aggro generation (`AttackedBy` ignores attacks) and do not perform actions. This prevents players from kiting or disrupting the teleport mechanic. Target selection is deferred until `OnEndTeleport`, where the closest player is identified and given extra threat.
3.  **Bug Activation Logic:** The `HandleBugSpell` method filters bugs by checking for existing auras (`SPELL_MUTATE_BUG` or `SPELL_EXPLODEBUG`). This prevents double-casting on the same bug. It also removes dead bugs from the candidate list.
4.  **Vek'lor's Shadowbolt Priority:** Vek'lor's `UpdateEmperor` has complex logic for Shadowbolt. If the target moves out of melee range, the timer is reset to allow an immediate cast. If in melee, it prioritizes melee attacks and uses a longer, randomized cooldown for Shadowbolt, mimicking observed vanilla behavior.
5.  **Anubisath Defender Spawn:** Upon aggro, the AI searches for `NPC_ANUBISATH_DEFENDER` within 800 yards. If found, they are activated and ordered to attack. If not found, it falls back to resetting the entrance door, suggesting a fallback or alternative spawn mechanism handled elsewhere or via the door reset.
6.  **Hardcoded Spell Immunities:** Both bosses apply spell immunities in their `Reset` methods (`IMMUNITY_DAMAGE` for Vek'lor, `IMMUNITY_DAMAGE` for Vek'nilash). The comments suggest these might be redundant if defined in the database, but they are enforced here.
7.  **Spell Target Limitation:** The `EmperorMutateBugScript` and `EmperorExplodeBugScript` explicitly limit their respective spells to 1 target via `OnSetTargetMap`. This overrides any default spell behavior that might target multiple entities.

## Member Reference

**mob_TwinsBug** (ctor): Initializes the bug AI and calls `Reset`.
**GoBeBadBug**: Activates the bug by applying an aura, setting hostile faction, and forcing combat. Restores health if mutated.
**JustDied#2**: Resets faction to neutral and removes auras on death.
**Reset#3**: Resets faction, auras, and initializes spell timers.
**UpdateAI#2**: Main loop for the bug; casts Pierce Armor and Acid Spit on timers and performs melee attacks.
**UpdateTeleportToMyBrother**: Virtual placeholder in base class; overridden by children to handle position swapping.
**TryHealBrother**: Virtual placeholder in base class; overridden by Vek'lor to attempt healing the other boss.
**boss_twinemperorsAI** (ctor): Retrieves instance data, logs errors if invalid, and sets initial stand state if encounter hasn't started.
**SharedReset**: Resets shared timers and flags, clears stunned state.
**MoveInLineOfSight**: Triggers aggro if a player enters line of sight within range and vertical constraints.
**AttackedBy**: Ignores attacks if the boss is in the teleport freeze window.
**DamageTaken**: Mirrors damage to the other boss based on percentage of max health.
**JustDied**: Marks encounter as done, kills the other boss, and handles instance data.
**Aggro**: Sets encounter to in-progress, spawns/activates Anubisath Defenders, resets doors if no defenders, and aggroes the other boss.
**JustReachedHome**: Marks encounter as failed in instance data.
**HealedBy**: Mirrors healing to the other boss based on percentage of max health.
**UpdateAI**: Main loop; handles evasion, teleport freeze state, target selection, and calls sub-updates for enrage, teleport, bugs, healing, and emperor-specific abilities.
**GetOtherBoss**: Returns a pointer to the other emperor based on entry ID.
**OnStartTeleport**: Initiates teleport, sets freeze flag, interrupts spells, and casts visual effects.
**OnEndTeleport**: Ends freeze, re-engages closest player, and calls virtual end-teleport handler.
**HandleBugSpell**: Finds and activates a nearby bug with the appropriate mutation/explosion spell.
**CheckEnrage**: Applies Berserk aura if timer expires.
**GetPlayerInP2PRange**: Finds a random player within a distance range from the threat list.
**boss_veklorAI** (ctor): Initializes range variables and calls `Reset`.
**Reset**: Calls `SharedReset`, initializes Vek'lor-specific timers, applies immunities, and respawns twin if dead.
**AttackStart**: Adjusts chase distance based on target proximity.
**KilledUnit**: Plays a random kill say if cooldown allows.
**JustReachedHome#2**: Plays a wipe say and calls parent `JustReachedHome`.
**GetBugSpellCooldown**: Returns random cooldown for Explode Bug.
**GetBugSpell**: Returns `SPELL_EXPLODEBUG`.
**UpdateTeleportToMyBrother#2**: Swaps positions with Vek'nilash when timer expires.
**TryHealBrother#2**: Attempts to heal the other boss if within range and not teleporting.
**OnEndTeleportVirtual**: Resets Shadowbolt timer to allow immediate casting.
**UpdateBlizzard**: Casts Blizzard on a random player within range.
**updateArcaneBurst**: Casts Arcane Burst on a random player within close range.
**UpdateEmperor**: Handles pull dialogue, updates Blizzard/Arcane Burst, and manages Shadowbolt/melee priority logic.
**boss_veknilashAI** (ctor): Calls `Reset`.
**Reset#2**: Calls `SharedReset`, initializes Vek'nilash-specific timers, applies immunities, and respawns twin if dead.
**JustReachedHome#3**: Plays a wipe say and calls parent `JustReachedHome`.
**OnEndTeleportVirtual#2**: Placeholder, currently empty.
**GetBugSpellCooldown#2**: Returns random cooldown for Mutate Bug.
**GetBugSpell#2**: Returns `SPELL_MUTATE_BUG`.
**GetPlayerInMeleeRange**: Finds a random player from the threat list within melee range.
**UpdateEmperor#2**: Handles pull dialogue, applies Double Attack aura, casts Unbalancing Strike and Upper Cut, and performs melee attacks.
**KilledUnit#2**: Plays a random kill say if cooldown allows.
**AttackStart#2**: Calls parent `AttackStart`.
**GetAI_boss_veknilash**: Factory function for Vek'nilash AI.
**GetAI_boss_veklor**: Factory function for Vek'lor AI.
**GetAI_twinsBug**: Factory function for Bug AI.
**OnSetTargetMap#2**: Limits Mutate Bug spell to 1 target.
**GetScript_EmperorMutateBug**: Factory function for Mutate Bug spell script.
**OnSetTargetMap**: Limits Explode Bug spell to 1 target.
**GetScript_EmperorExplodeBug**: Factory function for Explode Bug spell script.
**AddSC_boss_twinemperors**: Registers all AI and Spell scripts with the server.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_twinemperors

*Source:* boss_twinemperors.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| mob_TwinsBug | ctor | ScriptedAI/ScriptedAI | — | — |
| GoBeBadBug | method | Creature.Main/SetInCombatWithZone, Unit.Main/AddAura, Unit.Main/SetFactionTemplateId, Unit.Main/SetFullHealth | — | — |
| JustDied#2 | method | Unit.Main/RemoveAllAuras, Unit.Main/SetFactionTemplateId | — | — |
| Reset#3 | method | Unit.Main/RemoveAllAuras, Unit.Main/SetFactionTemplateId | — | — |
| UpdateAI#2 | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| UpdateTeleportToMyBrother | method | — | — | — |
| TryHealBrother | method | — | — | — |
| boss_twinemperorsAI | ctor | instance_temple_of_ahnqiraj/TwinsDialogueStartedOrDone, Log.Main/Out, ScriptedAI/ScriptedAI, Unit.Main/SetStandState, WorldObject.Object/GetInstanceData | — | — |
| SharedReset | method | Unit.Main/ClearUnitState | — | — |
| MoveInLineOfSight | method | Creature.Main/GetAttackDistance, CreatureAI/AttackStart, Unit.Main/GetVictim, Unit.Main/IsHostileTo, Unit.Main/IsInAccessablePlaceFor, Unit.Main/IsInCombat, Unit.Main/IsTargetableBy, WorldObject.Object/GetDistanceZ, WorldObject.Object/IsWithinDistInMap | — | — |
| AttackedBy | method | CreatureAI/AttackedBy | — | — |
| DamageTaken | method | Creature.Main/CountDamageTaken, Unit.Main/GetHealth, Unit.Main/GetMaxHealth, Unit.Main/IsAlive, Unit.Main/SetHealth | — | — |
| JustDied | method | instance_temple_of_ahnqiraj/GetData, instance_temple_of_ahnqiraj/SetData, Unit.Main/Kill | — | — |
| Aggro | method | Creature.Main/AI, Creature.Main/SetInCombatWithZone, CreatureAI/AttackStart, GridSearchers/GetCreatureListWithEntryInGrid#2, instance_temple_of_ahnqiraj/GetData, instance_temple_of_ahnqiraj/SetData, Object/GetGUID, ScriptedInstance/DoResetDoor, ScriptedInstance/GetSingleGameObjectFromStorage, Unit.Main/IsDead, WorldObject.Object/SetActiveObjectState | — | — |
| JustReachedHome | method | instance_temple_of_ahnqiraj/SetData | — | — |
| HealedBy | method | Unit.Main/GetHealth, Unit.Main/GetMaxHealth, Unit.Main/SetHealth | — | — |
| UpdateAI | method | Creature.Main/AI, Creature.Main/GetNearestVictimInRange, Creature.Main/SetInCombatWithZone, CreatureAI/EnterEvadeMode, Log.Main/Out, Object/GetGUID, ObjectGuid/IsEmpty, ObjectGuid/ObjectGuid#5, ScriptedAI/EnterEvadeMode, ThreatManager/addThreat#3, Unit.Main/GetThreatManager, Unit.Main/GetVictim, Unit.Main/IsDead, Unit.Main/SelectHostileTarget, WorldObject.Object/GetPositionZ | — | — |
| GetOtherBoss | method | Object/GetEntry, ScriptedInstance/GetSingleCreatureFromStorage | — | — |
| OnStartTeleport | method | ObjectGuid/ObjectGuid#5, ScriptedAI/DoResetThreat, ScriptedAI/DoStopAttack, SpellCaster/CastSpell#2, SpellCaster/InterruptNonMeleeSpells, Unit.Main/NearTeleportTo, Unit.Main/StopMoving | — | — |
| OnEndTeleport | method | CreatureAI/AttackStart, Log.Main/Out, Map.Main/GetPlayer, Object/GetGUID, ObjectGuid/ObjectGuid#5, ZoneScript/GetMap#2 | — | — |
| HandleBugSpell | method | Creature.Main/AI, GridSearchers/GetCreatureListWithEntryInGrid, shared_Util/urand, Unit.Main/HasAura#2, Unit.Main/IsDead | — | — |
| CheckEnrage | method | SpellCaster/CastSpell#2, Unit.Main/HasAura#2 | — | — |
| GetPlayerInP2PRange | method | Object/ToPlayer, ThreatManager/getThreatList, Unit.Main/GetThreatManager, WorldObject.Object/IsInRange | — | — |
| boss_veklorAI | ctor | — | — | — |
| Reset | method | Creature.Main/Respawn, shared_Util/urand, Unit.Main/ApplySpellImmune, Unit.Main/IsDead | — | — |
| AttackStart | method | CreatureAI/AttackStart, Unit.Main/SetCasterChaseDistance, WorldObject.Object/GetDistance3dToCenter#3 | — | — |
| KilledUnit | method | ScriptMgr/DoScriptText, shared_Util/urand | — | — |
| JustReachedHome#2 | method | ScriptMgr/DoScriptText | — | — |
| GetBugSpellCooldown | method | shared_Util/urand | — | — |
| GetBugSpell | method | — | — | — |
| UpdateTeleportToMyBrother#2 | method | Creature.Main/AI, shared_Util/urand, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | — | — |
| TryHealBrother#2 | method | CreatureAI/DoCastSpellIfCan, SpellCaster/CastSpell#2, Unit.Main/IsAlive, WorldObject.Object/IsWithinDist | — | — |
| OnEndTeleportVirtual | method | — | — | — |
| UpdateBlizzard | method | CreatureAI/DoCastSpellIfCan, shared_Util/urand | — | — |
| updateArcaneBurst | method | CreatureAI/DoCastSpellIfCan, shared_Util/urand | — | — |
| UpdateEmperor | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, ScriptMgr/DoScriptText, shared_Util/urand, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/GetStandState, Unit.Main/GetVictim, Unit.Main/SetStandState, WorldObject.Object/IsMoving, WorldObject.Object/IsWithinLOSInMap | — | — |
| boss_veknilashAI | ctor | — | — | — |
| Reset#2 | method | Creature.Main/Respawn, shared_Util/urand, Unit.Main/ApplySpellImmune, Unit.Main/IsDead | — | — |
| JustReachedHome#3 | method | ScriptMgr/DoScriptText | — | — |
| OnEndTeleportVirtual#2 | method | — | — | — |
| GetBugSpellCooldown#2 | method | shared_Util/urand | — | — |
| GetBugSpell#2 | method | — | — | — |
| GetPlayerInMeleeRange | method | ThreatManager/getThreatList, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/GetThreatManager | — | — |
| UpdateEmperor#2 | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, ScriptMgr/DoScriptText, shared_Util/urand, SpellCaster/CastSpell#2, Unit.Main/GetVictim, Unit.Main/HasAura#2 | — | — |
| KilledUnit#2 | method | ScriptMgr/DoScriptText, shared_Util/urand | — | — |
| AttackStart#2 | method | CreatureAI/AttackStart | — | — |
| GetAI_boss_veknilash | function | — | — | — |
| GetAI_boss_veklor | function | — | — | — |
| GetAI_twinsBug | function | — | — | — |
| OnSetTargetMap#2 | method | — | — | — |
| GetScript_EmperorMutateBug | function | — | — | — |
| OnSetTargetMap | method | — | — | — |
| GetScript_EmperorExplodeBug | function | — | — | — |
| AddSC_boss_twinemperors | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
