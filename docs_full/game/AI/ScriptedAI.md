# ScriptedAI

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ScriptedAI

**Purpose & Responsibilities**

`ScriptedAI` is the foundational base class for scripted creature artificial intelligence within the `wowvmangos` server. It extends `BasicAI` to provide a standardized interface for handling combat states, movement, spell casting, summoning, and threat management. Its primary responsibility is to abstract common engine operations (such as resetting a creature upon evasion, spawning adds, or modifying threat lists) into reusable helper methods, allowing individual boss and mob scripts (derived classes) to focus on high-level logic rather than low-level engine API calls.

It also defines `Scripted_NoMovementAI`, a specialized subclass for creatures that should not move during combat, overriding the standard attack start behavior to enforce idle positioning.

The unit does not interact with any database tables directly; all data manipulation occurs through the in-memory object model (`Creature`, `Unit`, `Map`).

## Member-by-Member Behavior

### Initialization and State Management

*   **`ScriptedAI` (Constructor)**: Initializes the AI by calling the `BasicAI` constructor. It stores a pointer to the controlled `Creature` in `me`. It initializes an evade cooldown timer (`m_uiEvadeCheckCooldown`) to 2500ms and records the creature's starting area ID in `m_uiHomeArea`. Crucially, it checks the creature's spawn data (`Creature.Main/GetCreatureData`) for the `SPAWN_FLAG_EVADE_OUT_HOME_AREA` flag. If set, `m_bEvadeOutOfHomeArea` is enabled, instructing the AI to automatically evade if the creature leaves its home zone.
*   **`~ScriptedAI` (Destructor)**: A trivial destructor that performs no cleanup, relying on the engine to manage the `Creature` object lifecycle.
*   **`EnterCombat`**: Triggered when the creature enters combat. It validates the enemy pointer and delegates to the virtual `Aggro` method. Derived classes typically override `Aggro` to perform specific initialization (e.g., casting initial buffs, announcing arrival).
*   **`Aggro`**: A virtual hook called by `EnterCombat`. The base implementation is empty. Derived classes override this to define behavior immediately upon engaging a target.
*   **`OnCombatStop`**: A virtual hook called when combat ends. The base implementation is empty.
*   **`EnterEvadeMode`**: The core reset logic invoked when a creature evades (loses aggro, despawns, or resets). It performs a comprehensive cleanup:
    1.  Clears combo points and removes auras associated with combat.
    2.  Deletes the threat list and stops combat state via `Unit.Main/CombatStop`.
    3.  Reloads the creature's addon data (`Creature.Main/LoadCreatureAddon`).
    4.  If alive, commands the motion master to return home (`Creature.MotionMaster/MoveTargetedHome`).
    5.  Handles loot recipient logic: if the creature is not a world boss or is dead, it clears the loot recipient to prevent raid loot loss issues on grid unloads.
    6.  Resets the creature's spell list to its default template via `CreatureAI/SetSpellsList#2`, which also resets internal spell timers.
    7.  Calls the pure virtual `Reset` method, allowing derived classes to reset their specific state variables.
*   **`JustRespawned`**: Called when the creature respawns. It invokes `Reset` and then `ResetCreature`, providing two distinct hooks for derived classes to handle respawn logic (e.g., resetting phase variables vs. resetting equipment).
*   **`Reset`**: A pure virtual function that derived classes **must** implement. It is called during evasion and respawn to reset script-specific state.
*   **`ResetCreature`**: A virtual function with an empty base implementation. Called only on death/respawn, not on evade. Used for logic that should persist across evasions but reset on death.

### Movement and Positioning

*   **`DoStartMovement`**: Commands the creature to chase a specific victim. It retrieves the motion master (`Unit.Main/GetMotionMaster`) and issues a `MoveChase` command with optional distance and angle offsets.
*   **`DoStartNoMovement`**: Forces the creature to stop moving and enter an idle state. It calls `MoveIdle` and explicitly stops any current movement via `Unit.Main/StopMoving`. This is often used for casters or stationary bosses.
*   **`DoGoHome`**: A convenience method to send the creature back to its spawn point. It checks if the creature has no victim and is alive before issuing `MoveTargetedHome`.
*   **`DoTeleportTo`**: Teleports the creature itself to specified X/Y/Z coordinates using `Unit.Main/NearTeleportTo` to ensure safe placement.
*   **`DoTeleportTo#2`**: An overload of `DoTeleportTo` that accepts a 4-element float array representing X, Y, Z, and Orientation. It passes these values to `Unit.Main/NearTeleportTo`.
*   **`DoTeleportAll`**: Teleports all alive players in the current dungeon instance to specified coordinates. It verifies the map is a dungeon (`Map.Main/IsDungeon`), iterates through the player list (`Map.Main/GetPlayers`), and teleports each alive player using `Player.Main/TeleportTo`. This is typically used for phase transitions in instances.
*   **`DoTeleportPlayer`**: Teleports a specific `Unit` (verified to be a Player) to coordinates. It logs an error if a non-player unit is passed. It uses `TELE_TO_NOT_LEAVE_COMBAT` to maintain combat state during the teleport.

### Combat and Threat Management

*   **`DoStopAttack`**: Stops the creature's current melee attack if it has a victim.
*   **`DoResetThreat`**: Resets the threat value of all units on the threat list to zero. It calls `Unit.Main/DoResetThreat`. Note that this does *not* remove units from the list, just neutralizes their threat.
*   **`DoGetThreat`**: Retrieves the current threat value a specific unit holds against the creature.
*   **`DoModifyThreatPercent`**: Modifies the threat percentage of a specific unit by a given integer amount. Positive values increase threat (taunt-like), negative values decrease it (vanish-like).
*   **`AttackStart` (in `Scripted_NoMovementAI`)**: Overrides the base attack start behavior. If the attack succeeds, it adds threat, sets combat flags, and then calls `DoStartNoMovement` to ensure the creature remains stationary while attacking.

### Spell Casting and Summoning

*   **`DoCastSpell`**: Casts a spell on a target. It includes a safety check: if the creature is already casting a non-melee spell and the new cast is not triggered, it aborts to prevent spell interruption or invalid states. It delegates to `SpellCaster/CastSpell`.
*   **`DoSpawnCreature`**: Spawns a creature at a position relative to the AI's current location (offset X, Y, Z). It calculates absolute coordinates and calls `WorldObject.Object/SummonCreature#2`.
*   **`DoSpawnCreature#2`**: An overload that spawns a creature at a random walkable position within a specified distance from the AI. It uses `Map.Main/GetWalkRandomPosition` to find a valid spot, calculates the angle to face away from the spawner, and summons the creature.
*   **`DoPlaySoundToSet`**: Plays a direct sound effect from a source object. It is static, allowing usage even without an instance context, though typically called from within the AI.

### Utility and Detection

*   **`DoFindFriendlyCC`**: Searches the grid for friendly creatures that are currently crowd-controlled (CC'd) within a range. It uses `MaNGOS::FriendlyCCedInRangeCheck` and returns a list of `Creature*`.
*   **`DoFindFriendlyMissingBuff`**: Searches for friendly creatures missing a specific buff within a range. Uses `MaNGOS::FriendlyMissingBuffInRangeCheck`.
*   **`GetPlayerAtMinimumRange`**: Finds a player who is *at least* a specified distance away from the creature. Useful for abilities that require targets to be far away.
*   **`GetPlayersWithinRange`**: Populates a list with all players within a specified range.
*   **`SetEquipmentSlots`**: Manages the visual equipment of the creature. If `bLoadDefault` is true, it loads the default equipment from the creature template. Otherwise, it manually sets the item IDs for main hand, off hand, and ranged slots using `WorldObject.Object/SetUInt32Value`. Negative values indicate no change.

### Specialized Behaviors

*   **`EnterEvadeIfOutOfCombatArea`**: A specialized evasion check used for specific bosses (Broodlord, Viscidus, Sylvanas, Varimathras). It enforces a 2.5-second cooldown between checks. Depending on the creature entry, it checks if the creature has moved beyond specific Z-height thresholds or distance limits. If the condition is met, it triggers `EnterEvadeMode`. This is a "hacklike" solution for creatures that should not leave specific zones.
*   **`EnterEvadeIfOutOfHomeArea`**: Checks if the creature's current Area ID differs from `m_uiHomeArea`. If `m_bEvadeOutOfHomeArea` is true (set in constructor), it triggers evasion. This automates zone-boundary evasion.
*   **`EnterVanish`**: Implements a stealth/vanish mechanic. It sets visibility to OFF, adds spawning/not-selectable flags, interrupts spells and attacks, and reduces threat by 100% for all units on the threat list.
*   **`LeaveVanish`**: Reverses `EnterVanish`. Sets visibility to ON and removes the stealth flags.
*   **`Ambush`**: Implements an ambush sequence. It teleports the creature behind the victim (`NearTeleportTo`), faces the victim, casts an optional ambush spell, and initiates combat via `AttackStart`.

## Cross-Unit Boundaries

`ScriptedAI` acts as a bridge between high-level script logic and the core engine systems.

*   **Calls Out:**
    *   **`BasicAI`**: Inherits basic AI functionality.
    *   **`Creature.Main` / `Unit.Main`**: Extensively used for state management (combat, auras, threat, movement, equipment).
    *   **`Creature.MotionMaster`**: Used for all movement commands (`MoveChase`, `MoveIdle`, `MoveTargetedHome`).
    *   **`SpellCaster`**: Used for casting spells.
    *   **`WorldObject.Object`**: Used for spatial queries (position, angle, area ID) and summoning.
    *   **`Map.Main`**: Used for dungeon checks, player iteration, and random position generation.
    *   **`GridSearchers` (`MaNGOS::*Check`)**: Used for querying entities in the vicinity (friendly CC, missing buffs, players).
    *   **`Log.Main`**: Used for error logging in `DoTeleportPlayer` and `EnterEvadeIfOutOfCombatArea`.

*   **Called By:**
    *   **Hundreds of Boss/Mob Scripts**: As seen in the MAP, nearly every boss and significant mob in the server inherits from `ScriptedAI` or `Scripted_NoMovementAI`. They call these helpers to standardize behavior.
    *   **`ScriptedFollowerAI`**: Calls `Reset` and `EnterEvadeMode` to integrate escort mechanics with the base AI reset logic.
    *   **`ThreatListCopier`**: Uses `EnterEvadeMode` and `UpdateAI` hooks to synchronize threat lists in battlegrounds.

## Data Model

This unit does not access any database tables. All operations are performed on in-memory objects.

## Notable Implementation Details

1.  **Evade Cooldown**: `EnterEvadeIfOutOfCombatArea` implements a manual cooldown (`m_uiEvadeCheckCooldown`) to prevent performance spikes from checking evasion conditions every tick. The cooldown resets to 2500ms after a check.
2.  **Hardcoded Boss Logic**: `EnterEvadeIfOutOfCombatArea` contains hardcoded entry IDs and coordinate checks for specific bosses (Broodlord, Viscidus, etc.). This is noted in comments as a temporary/hacklike solution due to lack of better data extraction methods. Maintainers adding new bosses requiring similar behavior must update this switch statement.
3.  **Loot Recipient Safety**: In `EnterEvadeMode`, the code explicitly clears the loot recipient for non-world-bosses or dead creatures. This prevents a known bug where raid loot could be lost if the creature's grid unloaded while the loot was still pending.
4.  **Spell Casting Guard**: `DoCastSpell` checks `IsNonMeleeSpellCasted` to prevent interrupting ongoing casts unless the new cast is triggered. This preserves intended spell sequences.
5.  **Vanish Threat Reduction**: `EnterVanish` manually iterates the threat list and reduces threat by 100%. This ensures that even if a player had massive threat, they will not re-aggro immediately upon leaving vanish if they don't take damage.
6.  **No-Movement AI**: `Scripted_NoMovementAI` overrides `AttackStart` to force `DoStartNoMovement`. This is critical for bosses that must remain stationary (e.g., casters, traps) to avoid pathfinding issues or breaking encounter geometry.

## Member Reference

**ScriptedAI**
Constructor that initializes the AI, sets up evade cooldowns, records home area, and checks for the `SPAWN_FLAG_EVADE_OUT_HOME_AREA` flag to enable automatic zone-boundary evasion.

**~ScriptedAI**
Trivial destructor with no custom cleanup logic.

**EnterCombat**
Validates the enemy pointer and delegates to the virtual `Aggro` method to initiate combat-specific setup.

**Aggro**
Virtual hook called upon entering combat. Base implementation is empty; derived classes override to define initial engagement behavior.

**OnCombatStop**
Virtual hook called when combat ends. Base implementation is empty.

**EnterEvadeMode**
Comprehensive reset routine: clears auras/combo points, deletes threat list, stops combat, reloads addons, moves home, handles loot recipient safety, resets spell templates, and calls `Reset`.

**Reset**
Pure virtual function that derived classes must implement to reset script-specific state during evasion or respawn.

**ResetCreature**
Virtual function called only on death/respawn (not evade). Base implementation is empty.

**JustRespawned**
Called on respawn; invokes `Reset` followed by `ResetCreature`.

**DoStartMovement**
Commands the creature to chase a victim using `MoveChase` with optional distance/angle offsets.

**DoStartNoMovement**
Forces the creature to stop moving and enter an idle state via `MoveIdle` and `StopMoving`.

**DoStopAttack**
Stops the creature's current melee attack if a victim exists.

**DoCastSpell**
Casts a spell on a target, guarding against interrupting existing non-melee casts unless triggered.

**DoPlaySoundToSet**
Static method to play a direct sound effect from a source object.

**DoSpawnCreature**
Spawns a creature at a position relative to the AI's current location (offset X, Y, Z).

**DoSpawnCreature#2**
Spawns a creature at a random walkable position within a specified distance from the AI.

**DoResetThreat**
Resets threat values of all units on the threat list to zero without removing them from the list.

**DoTeleportPlayer**
Teleports a verified Player unit to specified coordinates, maintaining combat state. Logs errors for non-player units.

**DoFindFriendlyCC**
Returns a list of friendly creatures currently crowd-controlled within a specified range.

**DoFindFriendlyMissingBuff**
Returns a list of friendly creatures missing a specific buff within a specified range.

**GetPlayerAtMinimumRange**
Finds a player who is at least a specified distance away from the creature.

**GetPlayersWithinRange**
Populates a list with all players within a specified range of the creature.

**SetEquipmentSlots**
Manages creature equipment visuals. Loads default equipment or manually sets item IDs for main/off/ranged slots.

**EnterEvadeIfOutOfCombatArea**
Specialized evasion check with a 2.5s cooldown. Contains hardcoded logic for specific bosses (Broodlord, Viscidus, etc.) to evade if they move beyond specific Z-heights or distances.

**EnterEvadeIfOutOfHomeArea**
Checks if the creature has left its home area ID. If `m_bEvadeOutOfHomeArea` is true, triggers evasion.

**AttackStart**
Defined in `Scripted_NoMovementAI`. Overrides base behavior to add threat, set combat flags, and force `DoStartNoMovement` to keep the creature stationary.

**DoGoHome**
Convenience method to send the creature home if it has no victim and is alive.

**DoGetThreat**
Retrieves the current threat value a specific unit holds against the creature.

**DoModifyThreatPercent**
Modifies the threat percentage of a specific unit by a given integer amount.

**DoTeleportTo**
Teleports the creature to specified X/Y/Z coordinates using `NearTeleportTo`.

**DoTeleportTo#2**
Overload of `DoTeleportTo` that accepts a 4-element float array (X, Y, Z, Orientation) and teleports the creature using `NearTeleportTo`.

**DoTeleportAll**
Teleports all alive players in the current dungeon instance to specified coordinates.

**EnterVanish**
Implements stealth: sets visibility off, adds stealth flags, interrupts spells/attacks, and reduces threat by 100% for all units.

**LeaveVanish**
Reverses vanish: sets visibility on and removes stealth flags.

**Ambush**
Teleports the creature behind a victim, faces them, casts an optional spell, and initiates combat.

---

<!-- machine-true, projected from graph.json -->

## Map — ScriptedAI

*Source:* ScriptedAI.cpp, ScriptedAI.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ScriptedAI | ctor | BasicAI/BasicAI, Creature.Main/GetCreatureData, WorldObject.Object/GetAreaId | arena_challenge_ai/npc_korvAI, arena_challenge_ai/npc_leftyAI, arena_challenge_ai/npc_malgen_longspearAI, arena_challenge_ai/npc_snokh_blackspineAI, arena_challenge_ai/npc_theldrenAI, arena_challenge_ai/npc_va_jashniAI, arena_challenge_ai/npc_volidaAI, ashenvale/npc_enraged_foulwealdAI, azshara/mob_mawsAI, blackrock_depths/boss_plugger_spazzringAI, blackrock_depths/mob_phalanxAI, blackrock_depths/npc_golem_lord_argelmachAI, blackrock_depths/npc_mistress_nagmaraAI, blackrock_depths/npc_watchman_doomgripAI, boss_anubrekhan/boss_anubrekhanAI, boss_anubrekhan/mob_cryptguardsAI, boss_anubshiah/boss_anubshiahAI, boss_arcanist_doan/boss_arcanist_doanAI, boss_archaedas/boss_archaedasAI, boss_archaedas/mob_archaedas_minionsAI, boss_arlokk/boss_arlokkAI, boss_arlokk/mob_prowlerAI, boss_ayamiss/boss_ayamissAI, boss_ayamiss/mob_zara_larvaAI, boss_baroness_anastari/boss_baroness_anastariAI, boss_baron_geddon/boss_baron_geddonAI, boss_broodlord_lashlayer/boss_broodlordAI, boss_bug_trio/boss_bug_trioAI, boss_buru/boss_buruAI, boss_buru/mob_buru_eggAI, boss_cannon_master_willey/boss_cannon_master_willeyAI, boss_celebras_the_cursed/celebras_the_cursedAI, boss_chromaggus/boss_chromaggusAI, boss_cthun/cthunAI, boss_cthun/cthunTentacle, boss_cthun/eye_of_cthunAI, boss_dathrohan_balnazzar/boss_dathrohan_balnazzarAI, boss_doctor_theolen_krastinov/boss_theolenkrastinovAI, boss_dragon_of_nightmare/boss_dragon_of_nightmareAI, boss_ebonroc/boss_ebonrocAI, boss_emperor_dagran_thaurissan/boss_emperor_dagran_thaurissanAI, boss_emperor_dagran_thaurissan/boss_moira_bronzebeardAI, boss_faerlina/boss_faerlinaAI, boss_fankriss/boss_fankrissAI, boss_fankriss/creature_spawn_fankrissAI, boss_fankriss/creature_vekniss_hatchlingAI, boss_firemaw/boss_firemawAI, boss_flamegor/boss_flamegorAI, boss_four_horsemen/boss_four_horsemen_shared, boss_gahzranka/boss_gahzrankaAI, boss_garr/boss_garrAI, boss_garr/mob_fireswornAI, boss_gehennas/boss_gehennasAI, boss_general_angerforge/boss_general_angerforgeAI, boss_gluth/boss_gluthAI, boss_gluth/mob_zombieChow, boss_golemagg/boss_golemaggAI, boss_golemagg/mob_core_ragerAI, boss_gordok_king/boss_chorushAI, boss_gordok_king/boss_king_gordokAI, boss_gorosh_the_dervish/boss_gorosh_the_dervishAI, boss_gothik/boss_gothikAI, boss_gothik/gothikTriggerAI, boss_grizzle/boss_grizzleAI, boss_grobbulus/boss_grobbulusAI, boss_hakkar/boss_hakkarAI, boss_halycon/boss_halyconAI, boss_heigan/boss_heiganAI, boss_herod/boss_herodAI, boss_herod/mob_scarlet_traineeAI, boss_highlord_omokk/boss_highlordomokkAI, boss_high_inquisitor_fairbanks/boss_high_inquisitor_fairbanksAI, boss_high_interrogator_gerstahn/boss_high_interrogator_gerstahnAI, boss_houndmaster_loksey/boss_houndmaster_lokseyAI, boss_huhuran/boss_huhuranAI, boss_illucia_barov/boss_illuciabarovAI, boss_immol_thar/boss_immol_tharAI, boss_instructor_malicia/boss_instructormaliciaAI, boss_interrogator_vishas/boss_interrogator_vishasAI, boss_ironaya/boss_ironayaAI, boss_jandice_barov/boss_jandicebarovAI, boss_jandice_barov/mob_illusionofjandicebarovAI, boss_jeklik/boss_jeklikAI, boss_jeklik/mob_batriderAI, boss_jeklik/npc_guru_bat_riderAI, boss_jindo/boss_jindoAI, boss_jindo/mob_brain_wash_totemAI, boss_jindo/mob_shade_of_jindoAI, boss_kurinnaxx/boss_kurinnaxxAI, boss_landslide/boss_landslideAI, boss_lethon/npc_spirit_shadeAI, boss_loatheb/boss_loathebAI, boss_loatheb/mob_eyeStalkAI, boss_loatheb/mob_rottingMaggotAI, boss_lord_alexei_barov/boss_lordalexeibarovAI, boss_lorekeeper_polkelt/boss_lorekeeperpolkeltAI, boss_lucifron/boss_lucifronAI, boss_maexxna/boss_maexxnaAI, boss_maexxna/mob_webwrapAI, boss_magistrate_barthilas/boss_magistrate_barthilasAI, boss_magmus/boss_magmusAI, boss_majordomo_executus/boss_majordomoAI, boss_maleki_the_pallid/boss_maleki_the_pallidAI, boss_mandokir/boss_mandokirAI, boss_mandokir/mob_chainedSpiritsAI, boss_mandokir/mob_ohganAI, boss_mandokir/mob_vilebrancheAI, boss_marli/boss_marliAI, boss_moam/boss_moamAI, boss_mr_smite/boss_mr_smiteAI, boss_nefarian/boss_nefarianAI, boss_nefarian/npc_corrupted_totemAI, boss_nerubenkan/boss_nerubenkanAI, boss_noth/boss_nothAI, boss_noxxion/boss_noxxionAI, boss_omen/boss_omenAI, boss_onyxia/boss_onyxiaAI, boss_onyxia/OnyxianWhelpAI, boss_order_of_silver_hand/boss_silver_hand_bossesAI, boss_ossirian/boss_ossirianAI, boss_ossirian/generic_random_moveAI, boss_ouro/boss_ouroAI, boss_ouro/npc_dirt_moundAI, boss_ouro/npc_ouro_scarabAI, boss_overlord_wyrmthalak/boss_overlordwyrmthalakAI, boss_patchwerk/boss_patchwerkAI, boss_postmaster_malown/boss_postmaster_malownAI, boss_ramstein_the_gorger/boss_ramstein_the_gorgerAI, boss_ras_frostwhisper/boss_rasfrostAI, boss_razorgore/boss_razorgoreAI, boss_razorgore/trigger_orb_of_commandAI, boss_razuvious/boss_razuviousAI, boss_razuvious/mob_deathknightUnderstudyAI, boss_renataki/boss_renatakiAI, boss_sapphiron/boss_sapphironAI, boss_sapphiron/npc_sapphiron_blizzardAI, boss_sartura/boss_sarturaAI, boss_sartura/mob_sartura_royal_guardAI, boss_sartura/mob_vekniss_guardianAI, boss_shadow_hunter_voshgajin/boss_shadowvoshAI, boss_shazzrah/boss_shazzrahAI, boss_skeram/boss_skeramAI, boss_sulfuron_harbinger/boss_sulfuronAI, boss_tendris_warpwood/boss_tendris_warpwoodAI, boss_thaddius/boss_thaddiusAddsAI, boss_thaddius/boss_thaddiusAI, boss_thermaplugg/boss_thermapluggAI, boss_the_beast/boss_thebeastAI, boss_the_ravenian/boss_theravenianAI, boss_timmy_the_cruel/boss_timmy_the_cruelAI, boss_timmy_the_cruel/npc_crimson_guardsmanAI, boss_tomb_of_seven/boss_doomrelAI, boss_twinemperors/boss_twinemperorsAI, boss_twinemperors/mob_TwinsBug, boss_urok/urokUnderlingAI, boss_vaelastrasz/boss_vaelAI, boss_vaelastrasz/npc_death_talon_CaptainAI, boss_vaelastrasz/npc_death_talon_SeetherAI, boss_vectus/boss_vectusAI, boss_vectus/npc_scholomance_studentAI, boss_venoxis/boss_venoxisAI, boss_victor_nefarius/boss_victor_nefariusAI, boss_viscidus/boss_viscidusAI, boss_viscidus/mob_viscidus_globAI, boss_viscidus/mob_viscidus_triggerAI, boss_warmaster_voone/boss_warmastervooneAI, boss_ysondre/npc_demented_druidAI, boss_zevrim/boss_zevrimAI, burning_steppes/npc_klinfranAI, custom_creatures/npc_summon_debugAI, custom_creatures/npc_training_dummyAI, darkshore/npc_murkdeepAI, desolace/npc_magrami_spetreAI, dreadsteed_ritual/boss_lordHelNurathAI, dreadsteed_ritual/boss_xorothianDreadsteedAI, dun_morogh/npc_narm_faulkAI, durotar/LazyPeonAI, duskwood/npc_commander_felstromAI, duskwood/npc_sirra_vonindiAI, duskwood/npc_twilight_corrupterAI, duskwood/npc_watcher_blombergAI, dustwallow_marsh/npc_archmage_tervoshAI, dustwallow_marsh/npc_emberstrifeAI, dustwallow_marsh/npc_lady_jaina_proudmooreAI, eastern_plaguelands/npc_caravan_muleAI, eastern_plaguelands/npc_demetriaAI, eastern_plaguelands/npc_eris_havenfireAI, eastern_plaguelands/npc_eris_havenfire_peasantAI, eastern_plaguelands/npc_guard_didierAI, eastern_plaguelands/npc_joseph_redpathAI, elemental_invasions/npc_invaderAI, elwynn_forest/npc_henze_faulkAI, felwood/npc_cursed_oozeAI, felwood/npc_tainted_oozeAI, feralas/MushgogAI, feralas/npc_captured_sprite_darterAI, feralas/SkarrTheUnbreakableAI, feralas/TheRazzaAI, instance_blackwing_lair/CorruptedWhelpAI, instance_blackwing_lair/npc_blackwing_technicianAI, instance_blackwing_lair/npc_death_talonAI, instance_dire_maul/boss_alzzin_the_wildshaperAI, instance_dire_maul/boss_ferraAI, instance_dire_maul/boss_guardsAI, instance_dire_maul/boss_kromcrushAI, instance_dire_maul/boss_magister_kalendrisAI, instance_dire_maul/boss_prince_tortheldrinAI, instance_dire_maul/GordokBruteAI, instance_dire_maul/npc_alzzins_minionAI, instance_dire_maul/npc_arcane_aberrationAI, instance_dire_maul/npc_knot_thimblejackAI, instance_dire_maul/npc_mizzle_the_craftyAI, instance_dire_maul/npc_residual_montruosityAI, instance_dire_maul/npc_reste_manaAI, instance_naxxramas.boss_kelthuzad/boss_kelthuzadAI, instance_naxxramas.boss_kelthuzad/kt_p1AddAI, instance_naxxramas.boss_kelthuzad/mob_guardian_icecrownAI, instance_naxxramas.boss_kelthuzad/mob_shadow_fissureAI, instance_naxxramas.Main/mob_dark_touched_warriorAI, instance_naxxramas.Main/mob_naxxramasGarboyleAI, instance_naxxramas.Main/mob_naxxramasPlagueSlimeAI, instance_naxxramas.Main/mob_spiritOfNaxxramasAI, instance_naxxramas.Main/mob_toxic_tunnelAI, instance_temple_of_ahnqiraj/AI_QirajiMindslayer, mob_anubisath_sentinel/aqsentinelAI, molten_core/FirewalkerAI, molten_core/mob_ancient_core_houndAI, molten_core/mob_core_houndAI, molten_core/mob_firelordAI, molten_core/mob_lava_surgerAI, moonglade/boss_eranikusAI, npcs_special/npc_doctorAI, npcs_special/npc_goblin_land_mineAI, npcs_special/npc_injured_patientAI, npcs_special/npc_kwee_peddlefeetAI, npcs_special/npc_pats_firework_guyAI, npcs_special/npc_riggle_bassbaitAI, npcs_special/npc_steam_tonkAI, npcs_special/npc_summon_possessedAI, npcs_special/npc_target_dummyAI, npcs_special/npc_the_cleanerAI, npcs_special/npc_tonk_mineAI, npcs_special/npc_tonk_mortarAI, npc_j_eevee/npc_j_eevee_dreadsteedAI, npc_j_eevee/npc_j_eevee_scholomanceAI, npc_sandstalker/npc_sandstalkerAI, quest_stormwind_rendezvous/npc_reginald_windsorAI, quest_stormwind_rendezvous/npc_squire_roweAI, ruins_of_ahnqiraj/HiveZaraSoldierAI, ruins_of_ahnqiraj/HiveZaraStingerAI, ruins_of_ahnqiraj/mob_anubisath_guardianAI, ruins_of_ahnqiraj/mob_flesh_hunterAI, ruins_of_ahnqiraj/ObsidianDestroyerAI, ruins_of_ahnqiraj/OssirianTornadoAI, ruins_of_ahnqiraj/QirajiGladiatorAI, ruins_of_ahnqiraj/QirajiSwarmguardAI, ruins_of_ahnqiraj/QirajiWarriorAI, ruins_of_ahnqiraj/SilicateFeederAI, ruins_of_ahnqiraj/SwarmguardNeedlerAI, ruins_of_ahnqiraj/TuubidAI, scholo_trash/npc_reanimated_corpseAI, scholo_trash/npc_spectral_projectionAI, scholo_trash/npc_unstable_corpseAI, scourge_invasion/MinionspawnerAI, scourge_invasion/MouthAI, scourge_invasion/NecropolisAI, scourge_invasion/NecropolisHealthAI, scourge_invasion/NecropolisProxyAI, scourge_invasion/NecropolisRelayAI, scourge_invasion/NecroticShard, scourge_invasion/npc_cultist_engineer, scourge_invasion/PallidHorrorAI, scourge_invasion/ScourgeMinion, ScriptedEscortAI/npc_escortAI, ScriptedFollowerAI/FollowerAI, scripts_battlegrounds_battleground/npc_spirit_guideAI, searing_gorge/npc_obsidionAI, silithus/mob_HiveRegal_HunterKillerAI, silithus/npc_anachronos_the_ancientAI, silithus/npc_colossusAI, silithus/npc_creeping_doomAI, silithus/npc_Emissary_RomankhanAI, silithus/npc_Geologist_LarksbaneAI, silithus/npc_Krug_SkullSplitAI, silithus/npc_MerokAI, silithus/npc_ShaiAI, silithus/npc_solenorAI, stonetalon_mountains/npc_piznikAI, stormwind_city/npc_bartlebyAI, stormwind_city/npc_dashel_stonefistAI, stranglethorn_vale/mob_assistant_kryll, stranglethorn_vale/mob_yennikuAI, stranglethorn_vale/npc_pats_hellfire_guyAI, stranglethorn_vale/npc_witch_doctor_unbagwaAI, stratholme/AI_mobs_rat_pestifere, stratholme/mobs_cristal_zugguratAI, stratholme/mobs_spectral_ghostly_citizenAI, stratholme/mob_freed_soulAI, stratholme/mob_restless_soulAI, sunken_temple/npc_malfurionAI, the_barrens/npc_mission_possible_but_not_probableAI, the_barrens/npc_pollyAI, the_barrens/npc_sarilus_foulborneAI, the_barrens/npc_twiggy_flatheadAI, thousand_needles/npc_grenka_bloodscreechAI, thousand_needles/npc_plucky_johnsonAI, ThreatListCopier.battleground_alterac/AV_CommanderAI, ThreatListCopier.battleground_alterac/AV_DismountAI, ThreatListCopier.battleground_alterac/AV_mineNpcAI, ThreatListCopier.battleground_alterac/AV_WarRiderAI, ThreatListCopier.battleground_alterac/DruidOfTheGroveAI, ThreatListCopier.battleground_alterac/FrostwolfShamanAI, ThreatListCopier.battleground_alterac/MineNPC_AI, ThreatListCopier.battleground_alterac/npc_AlteracBowmanAI, ThreatListCopier.battleground_alterac/npc_AlteracDardoshAI, ThreatListCopier.battleground_alterac/npc_av_trigger_for_questAI, ThreatListCopier.battleground_alterac/npc_BalindaAI, ThreatListCopier.battleground_alterac/npc_DrekTharAI, ThreatListCopier.battleground_alterac/npc_GalvangarAI, ThreatListCopier.battleground_alterac/npc_korrak_the_bloodragerAI, ThreatListCopier.battleground_alterac/npc_VanndarAI, ThreatListCopier.battleground_alterac/npc_WarMasterAI, ThreatListCopier.boss_ragnaros/boss_ragnarosAI, ubrs_trash/npc_blackhand_veteranAI, uldaman/AnnoraAI, uldaman/mob_jadespine_basiliskAI, uldaman/mob_stone_keeperAI, undercity/boss_sylvanasAI, ungoro_crater/mob_captured_felwood_oozeAI, ungoro_crater/npc_precious_the_devourerAI, ungoro_crater/npc_simone_seductressAI, ungoro_crater/npc_simone_the_inconspicuousAI, wailing_caverns/EvolvingEctoplasmAI, western_plaguelands/npc_highprotectorlorikAI, western_plaguelands/npc_the_scourge_cauldronAI, wetlands/npc_slims_friendAI, winterspring/npc_artoriusAI, winterspring/npc_umi_yetiAI, world_event_wareffort/npc_aqwar_cenarionhold_attackAI, world_event_wareffort/npc_aqwar_saurfangAI, world_event_wareffort/npc_infantrymanAI, world_event_wareffort/npc_resonating_CrystalAI, zulfarrak/npc_sergeant_blyAI, zulfarrak/npc_weegli_blastfuseAI, zulfarrak/ward_zumrahAI, zulgurub_trash/GurubashiAxeThrowerAI, zulgurub_trash/GurubashiBerserkerAI, zulgurub_trash/npc_esprit_vaudou, zulgurub_trash/npc_fils_hakkar, zulgurub_trash/npc_hakkari_doctor, zulgurub_trash/npc_jinxed_voodoo_pileAI | — |
| ~ScriptedAI | dtor | — | — | — |
| EnterCombat | method | — | boss_fankriss/EnterCombat, boss_overlord_wyrmthalak/EnterCombat | — |
| Aggro | method | — | boss_cthun/Aggro, boss_fankriss/Aggro#2, boss_hakkar/Aggro, boss_jeklik/Aggro, boss_jeklik/Aggro#2, boss_postmaster_malown/Aggro, instance_blackwing_lair/Aggro, searing_gorge/Aggro, world_event_wareffort/Aggro, world_event_wareffort/EnterCombat | — |
| OnCombatStop | method | — | — | — |
| EnterEvadeMode | method | Creature.Main/GetCreatureInfo, Creature.Main/IsWorldBoss, Creature.Main/LoadCreatureAddon, Creature.Main/RemoveAurasAtReset, Creature.Main/SetLootRecipient, Creature.MotionMaster/MoveTargetedHome, CreatureAI/SetSpellsList#2, Unit.Main/ClearComboPointHolders, Unit.Main/CombatStop, Unit.Main/DeleteThreatList, Unit.Main/GetMotionMaster, Unit.Main/IsAlive, Unit.Main/IsDead | boss_archaedas/EnterEvadeMode, boss_arlokk/JustSummoned, boss_bug_trio/EnterEvadeMode, boss_cannon_master_willey/EnterEvadeMode, boss_cthun/CheckRespawnEye, boss_cthun/UpdateAI#2, boss_dragon_of_nightmare/EnterEvadeMode, boss_fankriss/UpdateAI, boss_gluth/UpdateAI, boss_golemagg/EnterEvadeMode, boss_gothik/EnterEvadeMode, boss_hakkar/UpdateAI, boss_herod/EnterEvadeMode, boss_immol_thar/EnterEvadeMode, boss_jeklik/EnterEvadeMode, boss_loatheb/EnterEvadeMode, boss_loatheb/UpdateAI#3, boss_majordomo_executus/SummonedCreatureJustDied, boss_mr_smite/PhaseEquipEnd, boss_onyxia/EnterEvadeMode, boss_overlord_wyrmthalak/LeashIfOutOfCombatArea, boss_sapphiron/UpdateAI, boss_sartura/EnterEvadeMode, boss_sartura/EnterEvadeMode#2, boss_twinemperors/UpdateAI, boss_venoxis/EnterEvadeMode, boss_victor_nefarius/EnterEvadeMode, custom_creatures/UpdateAI#2, instance_dire_maul/EnterEvadeMode, instance_naxxramas.Main/UpdateAI#5, npcs_special/EnterEvadeMode, npcs_special/EnterEvadeMode#3, silithus/EnterEvadeMode#2, stormwind_city/DamageTaken, stranglethorn_vale/UpdateAI#2, stratholme/ReceiveEmote#2, thousand_needles/UpdateAI#2, ThreatListCopier.battleground_alterac/EnterEvadeMode#2, ThreatListCopier.battleground_alterac/EnterEvadeMode#3, ThreatListCopier.battleground_alterac/UpdateAI#10, ThreatListCopier.battleground_alterac/UpdateAI#12, ThreatListCopier.battleground_alterac/UpdateAI#14, ThreatListCopier.battleground_alterac/UpdateAI#4, ungoro_crater/EnterEvadeMode, world_event_wareffort/EnterEvadeMode, world_event_wareffort/UpdateAI#5 | — |
| Reset | decl | — | ScriptedFollowerAI/EnterEvadeMode, ScriptedFollowerAI/JustRespawned | — |
| ResetCreature | method | — | — | — |
| JustRespawned | method | — | boss_ouro/JustRespawned, stonetalon_mountains/JustRespawned, world_event_wareffort/JustRespawned | — |
| DoStartMovement | method | Creature.MotionMaster/MoveChase, Unit.Main/GetMotionMaster | boss_bug_trio/UpdateAI, boss_cannon_master_willey/UpdateAI, boss_gordok_king/UpdateAIMage, boss_gordok_king/UpdateAIPrist, boss_gordok_king/UpdateAIShaman, boss_sartura/MovementInform, instance_dire_maul/UpdateAI#6 | — |
| DoStartNoMovement | method | Creature.MotionMaster/MoveIdle, Unit.Main/GetMotionMaster, Unit.Main/StopMoving | boss_cannon_master_willey/UpdateAI, boss_gordok_king/UpdateAIMage, boss_gordok_king/UpdateAIPrist, boss_gordok_king/UpdateAIShaman, instance_dire_maul/UpdateAI#6, npcs_special/UpdateAI#7 | — |
| DoStopAttack | method | Unit.Main/AttackStop, Unit.Main/GetVictim | boss_cthun/EnterDarkGlarePhase, boss_cthun/UpdateMelee, boss_gothik/Aggro, boss_heigan/EventStartDance, boss_loatheb/UpdateAI#2, boss_noth/TeleportToBalc, boss_skeram/CastBlink#2, boss_twinemperors/OnStartTeleport | — |
| DoCastSpell | method | SpellCaster/CastSpell, SpellCaster/IsNonMeleeSpellCasted | — | — |
| DoPlaySoundToSet | method | WorldObject.Object/PlayDirectSound | — | — |
| DoSpawnCreature | method | WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SummonCreature#2 | boss_cthun/CheckRespawnEye, boss_cthun/cthunAI, boss_cthun/cthunPortalTentacle, boss_ysondre/DoSpecialAbility, darkshore/WaypointReached | — |
| DoSpawnCreature#2 | method | Map.Main/GetWalkRandomPosition, WorldObject.Object/GetAngle#2, WorldObject.Object/GetMap, WorldObject.Object/GetPosition#2, WorldObject.Object/SummonCreature#2 | boss_jandice_barov/SummonIllusions, boss_nerubenkan/RaiseUndeadScarab, boss_noxxion/SummonAdds | — |
| DoResetThreat | method | Unit.Main/DoResetThreat | boss_anubrekhan/UpdateAI#2, boss_bug_trio/UpdateAI, boss_bug_trio/UpdateBugAI#4, boss_cthun/updateBurried, boss_gahzranka/UpdateAI, boss_gothik/ResetThreatAndAttackNearestTarget, boss_jeklik/UpdateAI, boss_majordomo_executus/UpdateAI, boss_marli/UpdateAI, boss_noth/BlinkAndRepeatEvent, boss_noth/OnRemoveVulnerability, boss_onyxia/PhaseTransition, boss_ouro/Submerge, boss_razorgore/PhaseSwitch, boss_razorgore/UpdateAI#2, boss_sartura/AssignRandomThreat, boss_sartura/AssignRandomThreat#2, boss_shazzrah/UpdateAI, boss_skeram/CastBlink#2, boss_thaddius/HandleReviveEvent, boss_thaddius/TransitionToPhase, boss_twinemperors/OnStartTeleport, boss_venoxis/UpdateAI, boss_viscidus/ResetViscidusState, instance_dire_maul/UpdateAI#7, instance_naxxramas.boss_kelthuzad/DoChains, instance_naxxramas.boss_kelthuzad/UpdateP1, ThreatListCopier.boss_ragnaros/UpdateAI, zulgurub_trash/UpdateAI#2 | — |
| DoTeleportPlayer | method | Log.Main/Out, Object/GetEntry, Object/GetGUID, Object/GetTypeId, Player.Main/TeleportTo, WorldObject.Object/GetMapId | boss_cthun/UpdateStomachGrab | — |
| DoFindFriendlyCC | method | FriendlyCCedInRangeCheck/FriendlyCCedInRangeCheck | — | — |
| DoFindFriendlyMissingBuff | method | FriendlyMissingBuffInRangeCheck/FriendlyMissingBuffInRangeCheck | boss_sulfuron_harbinger/UpdateAI | — |
| GetPlayerAtMinimumRange | method | PlayerAtMinimumRangeAway/PlayerAtMinimumRangeAway | arena_challenge_ai/UpdateAI#3, arena_challenge_ai/UpdateAI#5 | — |
| GetPlayersWithinRange | method | AnyPlayerInObjectRangeCheck/AnyPlayerInObjectRangeCheck | boss_skeram/UpdateAI | — |
| SetEquipmentSlots | method | Creature.Main/GetCreatureInfo, Creature.Main/LoadEquipment, WorldObject.Object/SetUInt32Value | westfall/WaypointReached | — |
| EnterEvadeIfOutOfCombatArea | method | Creature.Main/IsInEvadeMode, Log.Main/Out, Object/GetEntry, Unit.Main/GetVictim, WorldObject.Object/GetDistance#4, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | boss_broodlord_lashlayer/UpdateAI, boss_viscidus/UpdateAI, undercity/UpdateAI | — |
| EnterEvadeIfOutOfHomeArea | method | WorldObject.Object/GetAreaId | boss_dragon_of_nightmare/UpdateAI | — |
| AttackStart | method | Unit.Main/AddThreat, Unit.Main/Attack, Unit.Main/SetInCombatWith | — | — |
| DoGoHome | method | Creature.MotionMaster/MoveTargetedHome, Unit.Main/GetMotionMaster, Unit.Main/GetVictim, Unit.Main/IsAlive | — | — |
| DoGetThreat | method | ThreatManager/getThreat, Unit.Main/GetThreatManager | boss_arlokk/UpdateAI#2 | — |
| DoModifyThreatPercent | method | ThreatManager/modifyThreatPercent#2, Unit.Main/GetThreatManager | boss_arlokk/UpdateAI#2, boss_doctor_theolen_krastinov/UpdateAI | — |
| DoTeleportTo#2 | method | Unit.Main/NearTeleportTo, WorldObject.Object/GetOrientation | ThreatListCopier.battleground_alterac/JustRespawned | — |
| DoTeleportTo | method | Unit.Main/NearTeleportTo | — | — |
| DoTeleportAll | method | Map.Main/GetPlayers, Map.Main/IsDungeon, Player.Main/TeleportTo, Unit.Main/IsAlive, WorldObject.Object/GetMap, WorldObject.Object/GetMapId | — | — |
| EnterVanish | method | HostileReference/getThreat, SpellCaster/InterruptSpell, ThreatManager/getThreatList, ThreatManager/isThreatListEmpty, ThreatManager/modifyThreatPercent#2, Unit.Main/AttackStop, Unit.Main/CanHaveThreatList, Unit.Main/GetThreatManager, Unit.Main/InterruptAttacksOnMe, Unit.Main/InterruptSpellsCastedOnMe, Unit.Main/SetVisibility, WorldObject.Object/SetFlag | boss_arlokk/UpdateAI, boss_renataki/UpdateAI, npc_sandstalker/JustReachedHome, npc_sandstalker/Reset, npc_sandstalker/UpdateAI | — |
| LeaveVanish | method | Unit.Main/SetVisibility, WorldObject.Object/RemoveFlag | boss_arlokk/JustDied, boss_arlokk/UpdateAI, boss_renataki/JustDied, boss_renataki/UpdateAI, npc_sandstalker/Aggro, npc_sandstalker/JustDied, npc_sandstalker/UpdateAI | — |
| Ambush | method | Creature.Main/AI, CreatureAI/AttackStart, SpellCaster/CastSpell#2, Unit.Main/NearTeleportTo, Unit.Main/SetFacingToObject, WorldObject.Object/GetRelativePositions#2 | boss_arlokk/UpdateAI, boss_renataki/UpdateAI, npc_sandstalker/Aggro, npc_sandstalker/UpdateAI | — |
