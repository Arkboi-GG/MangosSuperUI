# boss_jeklik

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_jeklik

**Purpose & Responsibilities**
This unit implements the artificial intelligence and combat behaviors for three distinct creature types within the Zul'Gurub raid instance: the boss **Jeklik**, his summoned minion **Bat Riders** (`mob_batrider`), and the trash mob **Guru Bat Riders** (`npc_guru_bat_rider`).

The primary responsibility is managing Jeklik’s two-phase encounter. In Phase 1, Jeklik is a flying bat form that summons minions and uses physical/melee-range spells. At 50% health, he transitions to Phase 2, losing flight, becoming grounded, and switching to shadow magic and healing abilities. The unit also handles the specific mechanics of the summoned Bat Riders, which act as aerial bombardment units targeting players near Jeklik, and the Guru Bat Riders, which have a suicide-explosion mechanic at low health.

All AI classes inherit from `ScriptedAI` (defined in `ScriptedAI`) and interact heavily with the instance data system (`InstanceData`) to track encounter progress and coordinate with other scripts.

## Member-by-Member Behavior

### Jeklik Boss AI (`boss_jeklikAI`)

**boss_jeklikAI**
Constructs the AI for the boss Jeklik. It retrieves the instance data pointer via `WorldObject.Object/GetInstanceData`, initializes timers in `Reset`, and immediately casts `SPELL_GREENCHANNELING` on itself using `SpellCaster/CastSpell`. This visual spell likely represents his initial idle state or aura.

**Reset**
Resets all combat timers to their base intervals. It sets Jeklik’s object scale to 1.5f via `WorldObject.Object/SetObjectScale`. Crucially, if the instance data exists and the creature is alive, it marks the encounter as failed (`FAIL`) in the instance data via `InstanceData/SetData`. This ensures that if the boss resets while alive (e.g., due to a wipe timeout or manual reset), the instance state reflects that the boss was not defeated.

**JustReachedHome**
Called when the creature despawns or returns to its spawn point after a fight. It recasts `SPELL_GREENCHANNELING` and resets the object scale to 1.0f via `WorldObject.Object/SetObjectScale`, returning him to his normal visual size.

**Aggro**
Triggered when combat begins. It adds the `UNIT_STATE_IGNORE_PATHFINDING` state to allow complex movement, plays the aggro text via `ScriptMgr/DoScriptText`, applies the `SPELL_BAT_FORM` aura via `Unit.Main/AddAura`, enables flight via `Unit.Main/SetFly`, and scales the model up to 2.0f via `WorldObject.Object/SetObjectScale`. It updates the instance data to `IN_PROGRESS` via `InstanceData/SetData` and calls the parent `ScriptedAI/Aggro`.

**JustDied**
Plays the death text via `ScriptMgr/DoScriptText` and updates the instance data to `DONE` via `InstanceData/SetData`. It then casts `SPELL_HAKKAR_POWER_DOWN` on itself. Note: While `SPELL_HAKKAR_POWER_DOWN` is not defined in the local enum, it is called here to remove a stack of Hakkar’s power, indicating Jeklik contributes to the raid-wide buff/debuff system associated with the final boss, Hakkar.

**EnterEvadeMode**
Called when the boss loses aggro or evades. It calls the parent `ScriptedAI/EnterEvadeMode`, respawns the creature via `Creature.Main/Respawn`, and teleports it back to its home position via `Creature.Main/GetHomePosition` (used in `NearTeleportTo`).

**UpdateAI**
The core combat loop. It first checks if the boss has a valid target.
1.  **Phase Transition:** If health drops below 50% and Phase 2 hasn't started, it triggers the transition: removes invincibility threshold, removes `SPELL_BAT_FORM`, disables flight, sets scale to 1.5f, resets threat via `ScriptedAI/DoResetThreat`, and sets `PhaseTwo` to true.
2.  **Minion Summons (Phase 1):** If health > 50%, it periodically summons 6 standard bats (`11368`) around fixed coordinates with random offsets using `shared_Util/frand`. These bats are targeted at a random player via `Creature.Main/SelectAttackingTarget` and told to attack via `Creature.Main/AI`.
3.  **Minion Summons (Phase 2):** Periodically summons a "Flying Bat" (`14965`) above a random player. This bat is handled by the `mob_batriderAI` script.
4.  **Spell Casting Logic:** Uses a global cooldown and a `skillStarted` flag to prevent multiple spells from casting simultaneously. It processes timers for Phase 1 spells (Charge, Screech, Sonic Burst, Swoop, Pierce Armor) and Phase 2 spells (Shadow Word Pain, Great Heal, Mind Flay, Curse of Blood). Spells are cast using `CreatureAI/DoCastSpellIfCan` or `Creature.Main/CastSpellOnNearestVictim`. Targets are selected via `Unit.Main/SelectHostileTarget` or `Creature.Main/SelectAttackingTarget`.

### Bat Rider Minion AI (`mob_batriderAI`)

**mob_batriderAI**
Constructs the AI for the summoned bat rider. It retrieves instance data via `WorldObject.Object/GetInstanceData` and sets the creature as non-selectable via `WorldObject.Object/SetFlag`.

**Reset#2**
Initializes the `Bomb_Timer` to 2000ms.

**AttackStart**
Empty override. Prevents the bat from engaging in standard melee combat.

**MoveInLineOfSight**
Empty override. Prevents aggro generation from line-of-sight events.

**DoAttack**
The primary action method. If the bomb timer is active, it returns early. Otherwise, it finds the nearest Jeklik via `WorldObject.Object/FindNearestCreature`. If found, it selects a random attacking target from Jeklik’s threat list via `Creature.Main/SelectAttackingTarget` and casts `SPELL_THROW_LIQUID_FIRE` on that target via `SpellCaster/CastSpell`. If Jeklik or a target cannot be found, it logs an error via `Log.Main/Out`.

**SpellHitTarget**
Triggered when a spell cast by this creature hits a target. If the spell is `SPELL_THROW_LIQUID_FIRE`, it casts `SPELL_BOMB` at the target’s coordinates (`WorldObject.Object/GetPositionX/Y/Z`) via `SpellCaster/CastSpell`. This creates an area-of-effect explosion on the ground where the liquid fire landed.

**UpdateAI#2**
Checks the instance data state via `InstanceData/GetData`. If the encounter is `IN_PROGRESS`, it calls `DoAttack`. Otherwise (e.g., if Jeklik dies or the encounter ends), it adds the creature to the removal list via `WorldObject.Object/AddObjectToRemoveList`, effectively despawning it.

### Guru Bat Rider Trash AI (`npc_guru_bat_riderAI`)

**npc_guru_bat_riderAI**
Constructs the AI for the trash mob. Inherits from `ScriptedAI`.

**Reset#3**
Resets timers and the `GoingToExplose` flag.

**Aggro#2**
Casts `SPELL_DEMORALIZING_SHOUT` on self via `SpellCaster/CastSpell` and calls parent `ScriptedAI/Aggro`.

**UpdateAI#3**
Manages combat abilities.
1.  **Suicide Mechanic:** If health drops below 40% and not already exploding, it sets `GoingToExplose` to true, displays a random emote via `WorldObject.Object/MonsterTextEmote`, grants fear immunity via `Unit.Main/ApplySpellImmune`, and casts `SPELL_EXPLOSION` on self.
2.  **Abilities:** Casts `SPELL_BATTLE_COMBAT` (self-buff), `SPELL_INFECTED_BITE` (on victim), and `SPELL_THRASH` (on victim) based on timers, using `CreatureAI/DoCastSpellIfCan`.
3.  **Melee:** Calls `CreatureAI/DoMeleeAttackIfReady`.

### Registration Functions

**GetAI_boss_jeklik**
Factory function returning a new `boss_jeklikAI` instance.

**GetAI_mob_batrider**
Factory function returning a new `mob_batriderAI` instance.

**GetAI_guru_bat_rider**
Factory function returning a new `npc_guru_bat_riderAI` instance.

**AddSC_boss_jeklik**
Registers the three scripts ("boss_jeklik", "mob_batrider", "npc_guru_bat_rider") with the `ScriptMgr` via `ScriptMgr/RegisterSelf`. This function is called by `ScriptLoader/AddScripts` during server startup.

## Cross-Unit Boundaries

*   **InstanceData (`zulgurub.h` / `InstanceData`):**
    *   **Direction:** `boss_jeklikAI` and `mob_batriderAI` call into `InstanceData`.
    *   **Collaboration:** `boss_jeklikAI` writes the encounter state (`FAIL`, `IN_PROGRESS`, `DONE`) to `TYPE_JEKLIK` via `SetData`. `mob_batriderAI` reads this state via `GetData` to determine whether to continue attacking or despawn. This ensures minions clean up properly if the boss dies or the encounter fails.
*   **ScriptedAI (`ScriptedAI`):**
    *   **Direction:** All AI classes inherit from and call methods in `ScriptedAI`.
    *   **Collaboration:** Used for base AI functionality like `Aggro`, `EnterEvadeMode`, `DoResetThreat`, and `DoCastSpellIfCan`.
*   **ScriptMgr (`ScriptMgr`):**
    *   **Direction:** `boss_jeklikAI` and `npc_guru_bat_riderAI` call into `ScriptMgr`.
    *   **Collaboration:** Used to play sound/text events (`DoScriptText`) and register scripts (`RegisterSelf`).
*   **Creature/Unit APIs (`Creature.Main`, `Unit.Main`, `WorldObject.Object`):**
    *   **Direction:** All AI classes call into these core engine classes.
    *   **Collaboration:** Used for movement (`SetFly`, `SetObjectScale`), targeting (`SelectAttackingTarget`, `FindNearestCreature`), spell casting (`CastSpell`, `AddAura`), and state management (`IsAlive`, `GetHealthPercent`).
*   **Shared Utilities (`shared_Util`):**
    *   **Direction:** `boss_jeklikAI` and `mob_batriderAI` call into `shared_Util`.
    *   **Collaboration:** Used for random number generation (`urand`, `frand`) for timer variations and summon positions.

## Data Model

This unit does not directly access any database tables. All data is managed in-memory through the game engine’s object system and the instance data structure.

## Notable Implementation Details

1.  **Phase Transition Logic:** The phase change in `boss_jeklikAI::UpdateAI` is triggered strictly by health percentage (< 50%). It manually manages visual states (scale, flight) and threat reset. The `skillStarted` flag is used to ensure only one spell is attempted per update cycle, preventing race conditions in the timer logic.
2.  **Minion Coordination:** The `mob_batriderAI` relies entirely on finding `NPC_JEKLIK` nearby to function. If Jeklik is not found, it logs an error but does not crash. It also cleans itself up if the instance data indicates the encounter is no longer in progress, providing robustness against unexpected encounter ends.
3.  **Hakkar Power Integration:** `boss_jeklikAI::JustDied` casts `SPELL_HAKKAR_POWER_DOWN`. This implies that defeating Jeklik reduces a raid-wide debuff/buff stack related to the final boss, Hakkar. This is a critical raid mechanic integration.
4.  **Trash Mob Suicide:** `npc_guru_bat_riderAI` has a deterministic suicide mechanic at 40% health. It grants fear immunity before exploding, ensuring it cannot be kited away easily during its final moments.
5.  **Timer Management:** The AI uses a `GlobalCooldown` and `Diff_Add` mechanism in `boss_jeklikAI::UpdateAI` to handle spell casting delays and ensure spells don't overlap. This is a common pattern in ScriptDev2 to manage GCD-like behavior for bosses.

## Member Reference

**boss_jeklikAI**: Constructor for Jeklik’s AI. Initializes instance data, resets timers, and casts the initial green channeling spell.
**Reset**: Resets all timers, sets object scale to 1.5f, and marks the encounter as failed in instance data if the boss is alive.
**JustReachedHome**: Recasts the green channeling spell and resets object scale to 1.0f when the boss returns to spawn.
**Aggro**: Starts combat by enabling flight, scaling up to 2.0f, applying bat form, playing aggro text, and marking the encounter as in progress.
**JustDied**: Plays death text, marks the encounter as done, and casts `SPELL_HAKKAR_POWER_DOWN` to reduce Hakkar’s power stack.
**EnterEvadeMode**: Handles evasion by calling the parent evade method, respawning the creature, and teleporting it home.
**UpdateAI**: Main combat loop. Manages phase transition at 50% health, summons minions (bats in P1, flying bats in P2), and casts phase-specific spells using timers and global cooldowns.
**mob_batriderAI**: Constructor for the summoned bat rider. Sets it as non-selectable and initializes timers.
**Reset#2**: Initializes the bomb timer for the bat rider.
**AttackStart**: Empty override to prevent standard melee engagement.
**MoveInLineOfSight**: Empty override to prevent aggro from line-of-sight.
**DoAttack**: Finds Jeklik, selects a random target from Jeklik’s threat list, and casts `SPELL_THROW_LIQUID_FIRE`. Logs errors if targets are missing.
**SpellHitTarget**: Triggers an AoE bomb spell at the target’s location when `SPELL_THROW_LIQUID_FIRE` hits.
**UpdateAI#2**: Checks instance state; if in progress, calls `DoAttack`; otherwise, despawns the creature.
**GetAI_boss_jeklik**: Factory function to create `boss_jeklikAI`.
**GetAI_mob_batrider**: Factory function to create `mob_batriderAI`.
**npc_guru_bat_riderAI**: Constructor for the Guru Bat Rider trash mob.
**Reset#3**: Resets timers and explosion flag for the Guru Bat Rider.
**Aggro#2**: Casts demoralizing shout and calls parent aggro.
**UpdateAI#3**: Manages combat abilities (battle combat, infected bite, thrash) and triggers a suicide explosion at 40% health with fear immunity.
**GetAI_guru_bat_rider**: Factory function to create `npc_guru_bat_riderAI`.
**AddSC_boss_jeklik**: Registers all three scripts with the script manager.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_jeklik

*Source:* boss_jeklik.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_jeklikAI | ctor | ScriptedAI/ScriptedAI, SpellCaster/CastSpell#2, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | InstanceData/SetData, Unit.Main/IsAlive, WorldObject.Object/SetObjectScale | — | — |
| JustReachedHome | method | SpellCaster/CastSpell#2, WorldObject.Object/SetObjectScale | — | — |
| Aggro | method | InstanceData/SetData, ScriptedAI/Aggro, ScriptMgr/DoScriptText, Unit.Main/AddAura, Unit.Main/AddUnitState, Unit.Main/SetFly, WorldObject.Object/SetObjectScale | — | — |
| JustDied | method | InstanceData/SetData, ScriptMgr/DoScriptText, SpellCaster/CastSpell#2 | — | — |
| EnterEvadeMode | method | Creature.Main/GetHomePosition#2, Creature.Main/Respawn, ScriptedAI/EnterEvadeMode | — | — |
| UpdateAI | method | Creature.Main/AI, Creature.Main/CastSpellOnNearestVictim, Creature.Main/SelectAttackingTarget, CreatureAI/AttackStart, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, ScriptedAI/DoResetThreat, ScriptMgr/DoScriptText, shared_Util/frand, shared_Util/urand, SpellCaster/InterruptNonMeleeSpells, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/GetHealthPercent, Unit.Main/GetVictim, Unit.Main/RemoveAurasDueToSpell, Unit.Main/SelectHostileTarget, Unit.Main/SetFly, Unit.Main/SetInvincibilityHpThreshold, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SetObjectScale, WorldObject.Object/SummonCreature#2 | — | — |
| mob_batriderAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData, WorldObject.Object/SetFlag | — | — |
| Reset#2 | method | — | — | — |
| AttackStart | method | — | — | — |
| MoveInLineOfSight | method | — | — | — |
| DoAttack | method | Creature.Main/SelectAttackingTarget, Log.Main/Out, shared_Util/urand, SpellCaster/CastSpell#2, WorldObject.Object/FindNearestCreature | — | — |
| SpellHitTarget | method | SpellCaster/CastSpell#4, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | — | — |
| UpdateAI#2 | method | InstanceData/GetData, WorldObject.Object/AddObjectToRemoveList | — | — |
| GetAI_boss_jeklik | function | — | — | — |
| GetAI_mob_batrider | function | — | — | — |
| npc_guru_bat_riderAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#3 | method | — | — | — |
| Aggro#2 | method | ScriptedAI/Aggro, SpellCaster/CastSpell#2 | — | — |
| UpdateAI#3 | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, SpellCaster/CastSpell#2, Unit.Main/ApplySpellImmune, Unit.Main/GetHealthPercent, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, WorldObject.Object/MonsterTextEmote | — | — |
| GetAI_guru_bat_rider | function | — | — | — |
| AddSC_boss_jeklik | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
