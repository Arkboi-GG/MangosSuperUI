# molten_core

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# molten_core

**Purpose & Responsibilities**  
This unit implements the artificial intelligence scripts for five specific creature types found in the Molten Core instance: **Firewalker**, **Ancient Core Hound**, **Core Hound**, **Firelord**, and **Lava Surger**. It defines their combat behaviors, spell rotations, special mechanics (such as feigning death or summoning minions), and their interaction with the instance’s boss progression state (specifically for Magmadar and Garr). The unit also registers these scripts with the server’s script manager so they are loaded at startup.

**Member-by-Member Behavior**  

### Firewalker (`FirewalkerAI`)
The Firewalker is a caster-type mob that uses fire-based area-of-effect and single-target spells.
*   **`FirewalkerAI` (ctor)**: Initializes the AI, retrieves the instance data pointer, and calls `Reset`.
*   **`Reset`**: Resets all internal timers. `m_uiFireBlossomCasting_Timer` starts at 6s, `m_uiInciteFlames_Timer` at 20s. `m_uiNbBlossom` (number of blossoms to cast) is set to 0.
*   **`Aggro`**: Marks the creature as being in combat with the zone.
*   **`UpdateAI`**: The main loop.
    *   If `m_uiFireBlossomCasting_Timer` expires, it casts `SPELL_FIREBLOSSOM_CASTING` (19636). This triggers a sequence: it sets `m_uiNbBlossom` to 6 and starts a 1s preparation timer.
    *   If `m_uiInciteFlames_Timer` expires, it casts `SPELL_INCITE_FLAMES` (19635) on its current victim.
    *   If `m_uiFireBlossomPreparing_Timer` expires and `m_uiNbBlossom` > 0, it selects a random hostile target and casts `SPELL_FIREBLOSSOM` (19637) on them. It decrements `m_uiNbBlossom` and resets the preparation timer to 1s. This creates a rapid-fire burst of 6 blossoms over 6 seconds.
    *   Performs melee attacks if ready.
*   **`GetAI_Firewalker`**: Factory function returning a new `FirewalkerAI` instance.

### Ancient Core Hound (`mob_ancient_core_houndAI`)
These hounds have randomized debuff abilities and specific post-death behavior tied to the Magmadar boss encounter.
*   **`mob_ancient_core_houndAI` (ctor)**: Initializes AI, gets instance data, calls `Reset`.
*   **`Reset`**: 
    *   Randomly assigns one of six debuff spells (`RandDebuff`) using `urand(0, 5)`: Ground Stomp, Ancient Dread, Cauterizing Flames, Withering Heat, Ancient Despair, or Ancient Hysteria.
    *   Sets initial timers: Cone of Fire (4-7s), Random Debuff (12-15s), Bite (4s).
    *   Calls `SetNoCallAssistance(true)` to prevent them from calling for help.
*   **`JustDied`**: Checks if the Magmadar boss (`TYPE_MAGMADAR`) is marked as `DONE` in the instance data. If so, it sets the respawn time to 7 days and saves it to the database. This prevents them from respawning immediately after the boss is defeated, keeping the area clear.
*   **`UpdateAI`**:
    *   If `m_uiConeOfFireTimer` expires, casts `SPELL_CONE_OF_FIRE` (19630) on itself. Resets timer to 6-8s.
    *   If `m_uiRandomDebuffTimer` expires, casts the assigned `RandDebuff` spell on itself. Resets timer to 14-24s.
    *   If `m_uiBiteTimer` expires and the victim is in melee range, casts `SPELL_BITE` (19771) on the victim. Resets timer to 6s.
    *   If melee attack is ready and victim is in range, casts `SPELL_VICIOUS_BITE` (19319) and resets the attack timer.
*   **`GetAI_mob_ancient_core_hound`**: Factory function.

### Core Hound (`mob_core_houndAI`)
These hounds have a unique "feign death" mechanic. They pretend to die when low on health, then resurrect if their pack is still fighting.
*   **`mob_core_houndAI` (ctor)**: Initializes AI, gets instance data, calls `Reset`.
*   **`ResurrectSelf`**: Internal helper. Removes pacify aura, casts full heal, stands up, removes dead flag, stops attacking, enables melee/combat movement, and sets `m_bDead` to false.
*   **`FeignDeath`**: Internal helper. Emotes fake death text, sets health to 1, removes all auras, casts pacify self, clears motion master and moves idle, sets stand state to dead, sets dead dynamic flag, disables melee/combat movement, sets resurrection timer to 10s, and sets `m_bDead` to true.
*   **`Kill_Self`**: Internal helper. Sets invincibility threshold to 0, deals 1 damage to itself to trigger death, and forces despawn.
*   **`Reset`**: Sets Serrated Bite timer (4-7s) and Resurrect timer (10s). Calls `ResurrectSelf()` to ensure the hound starts in a healthy, active state.
*   **`Aggro`**: Marks creature as in combat with zone.
*   **`DamageTaken`**: If the incoming damage would reduce health below 0 (i.e., kill it), and it’s not already dead (`!m_bDead`), it calls `FeignDeath()` instead of dying. This intercepts the death event.
*   **`UpdateAI`**:
    *   If `m_bDead` is true:
        *   If `m_uiResurrectTimer` expires, it checks for other Core Hounds (`NPC_CORE_HOUND`) within 100 yards. If any are in combat and have health > 1, it sets `m_bResurrectionOkay` to true.
        *   If `m_bResurrectionOkay`, it calls `ResurrectSelf()`, casts a visual fire nova, and emotes revive text.
        *   Otherwise, it calls `Kill_Self()` to permanently despawn.
        *   Returns early.
    *   If alive:
        *   If `m_uiSerratedBiteTimer` expires, casts `SPELL_SERRATED_BITE` (19771) on victim. Resets timer to 4-7s.
        *   Performs melee attacks if ready and not dead.
*   **`GetAI_mob_core_hound`**: Factory function.

### Firelord (`mob_firelordAI`)
Firelords summon lava spawns and use soul burn on players.
*   **`mob_firelordAI` (ctor)**: Initializes AI, gets instance data, calls `Reset`.
*   **`Reset`**: Sets Summon Lava Spawn timer (7.5-12.5s) and Soul Burn timer (4-6s).
*   **`Aggro`**: Casts `SPELL_INCINERATE_AURA` (19396) on itself if not already present.
*   **`JustSummoned`**: When a lava spawn is summoned, it commands the summoned creature to attack a random target selected by the Firelord.
*   **`UpdateAI`**:
    *   If `m_uiSummonLavaSpawnTimer` expires, casts `SPELL_LAVASPAWN` (19569) to summon a minion. Resets timer to 15-20s.
    *   If `m_uiSoulBurnTimer` expires, selects a random player target and casts `SPELL_SOULBURN` (19393) on them. Resets timer to 3-4s.
    *   Performs melee attacks if ready.
*   **`GetAI_mob_firelord`**: Factory function.

### Lava Surger (`mob_lava_surgerAI`)
Lava Surgers cast Surge on farthest players, but skip updates if stunned (banished). Their respawn is tied to the Garr boss encounter.
*   **`mob_lava_surgerAI` (ctor)**: Initializes AI, gets instance data, calls `Reset`.
*   **`Reset`**: Sets Surge timer to 1-2s.
*   **`JustDied`**: Checks if the Garr boss (`TYPE_GARR`) is marked as `DONE`. If so, sets respawn time to 7 days and saves it.
*   **`UpdateAI`**:
    *   If the creature has a stun aura (`SPELL_AURA_MOD_STUN`), it returns early, skipping all updates. This handles the "Banish" mechanic.
    *   If `m_uiSurgeTimer` expires:
        *   Selects the farthest player target in line of sight.
        *   If the distance to that target is greater than 7 yards, casts `SPELL_SURGE` (19196) on them. Resets timer to 5-6s.
    *   Performs melee attacks if ready.
*   **`GetAI_mob_lava_surger`**: Factory function.

### Script Registration
*   **`AddSC_molten_core`**: Creates `Script` objects for each of the five mobs, assigns their respective `GetAI` factory functions, and registers them with the script manager. This function is called by `ScriptLoader/AddScripts` during server startup.

**Cross-Unit Boundaries**  
*   **`ScriptedAI/ScriptedAI`**: All AI classes inherit from `ScriptedAI`, providing base functionality for timers, casting, and melee attacks.
*   **`WorldObject.Object/GetInstanceData`**: Used in constructors to retrieve the `ScriptedInstance` pointer, allowing access to instance-wide state (e.g., boss completion flags).
*   **`Creature.Main/SetInCombatWithZone`**: Called in `Aggro` methods to properly register the creature in the zone’s combat system.
*   **`CreatureAI/DoCastSpellIfCan`**: Used extensively to attempt spell casts, respecting cooldowns and channeling states.
*   **`CreatureAI/DoMeleeAttackIfReady`**: Used in `UpdateAI` loops to perform standard melee attacks.
*   **`Unit.Main/GetVictim` / `Unit.Main/SelectHostileTarget`**: Used to determine the current target and validate combat state.
*   **`Creature.Main/SelectAttackingTarget`**: Used to select random or specific targets for spells.
*   **`shared_Util/urand`**: Used for randomizing timers and debuff assignments.
*   **`SpellCaster/CastSpell#2`**: Direct spell casting used in `mob_ancient_core_houndAI` and `mob_core_houndAI` for specific spells where `DoCastSpellIfCan` isn’t used or for triggered effects.
*   **`Unit.Main/CanReachWithMeleeAutoAttack`**: Checks if a target is within melee range before casting bite spells.
*   **`Unit.Main/IsAttackReady` / `Unit.Main/ResetAttackTimer`**: Manages melee attack readiness.
*   **`Creature.Main/SetNoCallAssistance`**: Prevents ancient core hounds from calling for help.
*   **`Creature.Main/SaveRespawnTime` / `Creature.Main/SetRespawnTime`**: Persists extended respawn times after boss encounters.
*   **`InstanceData/GetData`**: Reads instance state to check if Magmadar or Garr are defeated.
*   **`CreatureAI/SetCombatMovement` / `CreatureAI/SetMeleeAttack`**: Enables/disables movement and melee attacks during feign death/resurrection.
*   **`Unit.Main/AttackStop`**: Stops current attack during resurrection.
*   **`Unit.Main/RemoveAurasDueToSpell` / `Unit.Main/RemoveAllAuras`**: Clears auras during state changes.
*   **`Unit.Main/SetStandState`**: Changes visual stand state (standing/dead).
*   **`WorldObject.Object/RemoveFlag` / `WorldObject.Object/SetFlag`**: Manages dynamic flags like `UNIT_DYNFLAG_DEAD`.
*   **`Creature.MotionMaster/MoveIdle` / `MotionMaster/Clear` / `Unit.Main/GetMotionMaster`**: Controls movement during feign death.
*   **`Unit.Main/SetHealth`**: Sets health to 1 during feign death.
*   **`WorldObject.Object/MonsterTextEmote#2`**: Emotes text for fake death and revival.
*   **`Creature.Main/ForcedDespawn`**: Permanently removes the creature if resurrection fails.
*   **`Unit.Main/DealDamage`**: Deals self-damage to trigger death.
*   **`Unit.Main/SetInvincibilityHpThreshold`**: Prepares for forced death.
*   **`Unit.Main/GetHealth`**: Checks health in `DamageTaken` and resurrection logic.
*   **`Unit.Main/IsInCombat`**: Checks combat state of other hounds.
*   **`GridSearchers/GetCreatureListWithEntryInGrid#2`**: Finds nearby core hounds for resurrection check.
*   **`Creature.Main/AI` / `CreatureAI/AttackStart`**: Commands summoned creatures to attack.
*   **`Unit.Main/HasAuraType`**: Checks for stun aura in Lava Surger.
*   **`WorldObject.Object/GetDistance2d#3`**: Checks distance for Surge spell.
*   **`Script/Script` / `ScriptMgr/RegisterSelf`**: Registers scripts with the server.

**Data Model**  
This unit does not directly query or modify any database tables. It interacts with the instance data system via `GetInstanceData` and `GetData`, which abstracts the underlying storage. Respawn times are saved via `SaveRespawnTime`, which likely writes to a creature respawn table, but the unit does not handle the SQL directly.

**Notable Implementation Details**  
*   **Firewalker Blossom Sequence**: The Firewalker’s `SPELL_FIREBLOSSOM` is not cast immediately. Instead, `SPELL_FIREBLOSSOM_CASTING` initiates a 6-second sequence where 6 individual blossoms are cast on random targets every second. This is managed by `m_uiNbBlossom` and `m_uiFireBlossomPreparing_Timer`.
*   **Ancient Core Hound Random Debuff**: Each Ancient Core Hound is assigned a single random debuff spell upon reset. This spell is then used periodically throughout its life. This adds variety to packs of hounds.
*   **Core Hound Feign Death**: The `DamageTaken` method intercepts lethal damage. If the hound is not already dead, it calls `FeignDeath()`, setting health to 1 and entering a dead state. After 10 seconds, it checks if other hounds are still fighting. If so, it resurrects; otherwise, it permanently despawns. This mechanic requires careful handling of health, auras, and movement states.
*   **Boss-Tied Respawns**: Both Ancient Core Hounds and Lava Surgers check the instance data for the completion of Magmadar and Garr, respectively. If the boss is defeated, their respawn time is set to 7 days, preventing them from cluttering the area after the encounter.
*   **Lava Surger Stun Check**: The Lava Surger skips its entire `UpdateAI` loop if it has a stun aura. This is likely to handle a banish mechanic where the surger is temporarily disabled.
*   **Firelord Summon Control**: When a Firelord summons a Lava Spawn, `JustSummoned` is called, which immediately orders the spawn to attack a random target. This ensures the summons engage quickly.

## Member Reference

**FirewalkerAI** (ctor): Initializes the Firewalker AI, retrieves instance data, and calls `Reset`. Inherits from `ScriptedAI`.

**Reset**: Resets Firewalker timers: Fire Blossom Casting (6s), Incite Flames (20s), and blossom count to 0.

**Aggro**: Marks the Firewalker as being in combat with the zone.

**UpdateAI**: Main loop for Firewalker. Handles Fire Blossom casting sequence (6 blossoms over 6s), Incite Flames on victim, and melee attacks.

**GetAI_Firewalker**: Factory function returning a new `FirewalkerAI` instance.

**mob_ancient_core_houndAI** (ctor): Initializes Ancient Core Hound AI, retrieves instance data, and calls `Reset`. Inherits from `ScriptedAI`.

**Reset#2**: Assigns a random debuff spell, sets initial timers for Cone of Fire, Debuff, and Bite, and disables assistance calls.

**JustDied**: If Magmadar is defeated, sets respawn time to 7 days and saves it.

**UpdateAI#2**: Main loop for Ancient Core Hound. Handles Cone of Fire, random debuff, bite spell, and melee attacks with Vicious Bite.

**GetAI_mob_ancient_core_hound**: Factory function returning a new `mob_ancient_core_houndAI` instance.

**mob_core_houndAI** (ctor): Initializes Core Hound AI, retrieves instance data, and calls `Reset`. Inherits from `ScriptedAI`.

**ResurrectSelf**: Internal helper to restore the hound to full health, remove dead state, and re-enable combat.

**FeignDeath**: Internal helper to simulate death: sets health to 1, applies pacify, stops movement/attacks, and sets dead flags.

**Kill_Self**: Internal helper to permanently despawn the hound by dealing self-damage.

**Reset#3**: Sets initial timers and calls `ResurrectSelf` to ensure the hound starts active.

**Aggro#2**: Marks the Core Hound as being in combat with the zone.

**DamageTaken**: Intercepts lethal damage to trigger `FeignDeath` instead of actual death.

**UpdateAI#3**: Main loop for Core Hound. If dead, checks for resurrection conditions after 10s. If alive, handles Serrated Bite and melee attacks.

**GetAI_mob_core_hound**: Factory function returning a new `mob_core_houndAI` instance.

**mob_firelordAI** (ctor): Initializes Firelord AI, retrieves instance data, and calls `Reset`. Inherits from `ScriptedAI`.

**Reset#4**: Sets initial timers for Summon Lava Spawn and Soul Burn.

**Aggro#3**: Casts Incinerate Aura on itself upon aggro.

**JustSummoned**: Orders newly summoned Lava Spawns to attack a random target.

**UpdateAI#4**: Main loop for Firelord. Handles summoning Lava Spawns, casting Soul Burn on players, and melee attacks.

**GetAI_mob_firelord**: Factory function returning a new `mob_firelordAI` instance.

**mob_lava_surgerAI** (ctor): Initializes Lava Surger AI, retrieves instance data, and calls `Reset`. Inherits from `ScriptedAI`.

**Reset#5**: Sets initial Surge timer to 1-2s.

**JustDied#2**: If Garr is defeated, sets respawn time to 7 days and saves it.

**UpdateAI#5**: Main loop for Lava Surger. Skips updates if stunned. Handles casting Surge on farthest players and melee attacks.

**GetAI_mob_lava_surger**: Factory function returning a new `mob_lava_surgerAI` instance.

**AddSC_molten_core**: Registers all five Molten Core mob scripts with the server’s script manager.

---

<!-- machine-true, projected from graph.json -->

## Map — molten_core

*Source:* molten_core.cpp, molten_core.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| FirewalkerAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | — | — | — |
| Aggro | method | Creature.Main/SetInCombatWithZone | — | — |
| UpdateAI | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_Firewalker | function | — | — | — |
| mob_ancient_core_houndAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset#2 | method | Creature.Main/SetNoCallAssistance, shared_Util/urand | — | — |
| JustDied | method | Creature.Main/SaveRespawnTime, Creature.Main/SetRespawnTime, InstanceData/GetData | — | — |
| UpdateAI#2 | method | CreatureAI/DoCastSpellIfCan, shared_Util/urand, SpellCaster/CastSpell#2, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/GetVictim, Unit.Main/IsAttackReady, Unit.Main/ResetAttackTimer, Unit.Main/SelectHostileTarget | — | — |
| GetAI_mob_ancient_core_hound | function | — | — | — |
| mob_core_houndAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| ResurrectSelf | method | CreatureAI/SetCombatMovement, CreatureAI/SetMeleeAttack, SpellCaster/CastSpell#2, Unit.Main/AttackStop, Unit.Main/RemoveAurasDueToSpell, Unit.Main/SetStandState, WorldObject.Object/RemoveFlag | — | — |
| FeignDeath | method | Creature.MotionMaster/MoveIdle, CreatureAI/SetCombatMovement, CreatureAI/SetMeleeAttack, MotionMaster/Clear, SpellCaster/CastSpell#2, Unit.Main/GetMotionMaster, Unit.Main/GetVictim, Unit.Main/RemoveAllAuras, Unit.Main/SelectHostileTarget, Unit.Main/SetHealth, Unit.Main/SetStandState, WorldObject.Object/MonsterTextEmote#2, WorldObject.Object/SetFlag | — | — |
| Kill_Self | method | Creature.Main/ForcedDespawn, Unit.Main/DealDamage, Unit.Main/SetInvincibilityHpThreshold | — | — |
| Reset#3 | method | shared_Util/urand | — | — |
| Aggro#2 | method | Creature.Main/SetInCombatWithZone | — | — |
| DamageTaken | method | Unit.Main/GetHealth | — | — |
| UpdateAI#3 | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, GridSearchers/GetCreatureListWithEntryInGrid#2, shared_Util/urand, Unit.Main/GetHealth, Unit.Main/GetVictim, Unit.Main/IsInCombat, Unit.Main/SelectHostileTarget, WorldObject.Object/MonsterTextEmote#2 | — | — |
| GetAI_mob_core_hound | function | — | — | — |
| mob_firelordAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset#4 | method | shared_Util/urand | — | — |
| Aggro#3 | method | CreatureAI/DoCastSpellIfCan | — | — |
| JustSummoned | method | Creature.Main/AI, Creature.Main/SelectAttackingTarget, CreatureAI/AttackStart | — | — |
| UpdateAI#4 | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_mob_firelord | function | — | — | — |
| mob_lava_surgerAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset#5 | method | shared_Util/urand | — | — |
| JustDied#2 | method | Creature.Main/SaveRespawnTime, Creature.Main/SetRespawnTime, InstanceData/GetData | — | — |
| UpdateAI#5 | method | Creature.Main/SelectAttackingTarget#2, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, Unit.Main/GetVictim, Unit.Main/HasAuraType, Unit.Main/SelectHostileTarget, WorldObject.Object/GetDistance2d#3 | — | — |
| GetAI_mob_lava_surger | function | — | — | — |
| AddSC_molten_core | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
