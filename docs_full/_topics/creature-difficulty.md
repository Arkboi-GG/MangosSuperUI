<!-- provenance: invented-config -->
# Creature Difficulty (HP & Damage)

<!-- aliases: harder mobs, mob damage, creature hp, mob difficulty, buff creatures, nerf mobs, creature stats -->
<!-- documentation: model-written from source via the local LLM; review before trusting -->

Creature difficulty in VMaNGOS is determined by a layered pipeline: static database templates provide base stats, the `creature_classlevelstats` table defines scaling curves, and runtime configuration keys apply global multipliers. When a creature spawns or changes form, the server calculates its final HP and damage by combining these sources.

The process begins at startup when `ObjectMgr.LoadCreatureTemplates` reads `creature_template` rows and `ObjectMgr.LoadCreatureClassLevelStats` reads `creature_classlevelstats`. These define the raw potential of a creature type. When a specific creature instance enters the world or changes its entry (via `Creature.UpdateEntry`), it selects a level within its template's range using `Creature.SelectLevel`. It then initializes its stats based on the class/level curve. Finally, `Creature.UpdateAllStats` applies the configured rate multipliers to produce the final visible HP and damage values.

Operators can tune this behavior globally via configuration keys, per-creature via database multipliers, or by editing the base stat tables.

## How to Modify

### Config

Two primary configuration keys control the baseline difficulty of normal (non-elite) creatures. These keys act as global multipliers applied during stat calculation.

*   **`Rate.Creature.Normal.HP`** (default `1`)
    Multiplies the calculated health of normal creatures. Setting this to `2` doubles the HP of all normal mobs. Elite creatures use separate keys (e.g., `Rate.Creature.Elite.Elite.HP`), so this key does not affect bosses or elites unless those keys are also adjusted.
*   **`Rate.Creature.Normal.Damage`** (default `1`)
    Multiplies the melee and spell damage of normal creatures. Like HP, this applies only to normal rank creatures. Increasing this value makes mobs hit harder.

These keys are read by `World.LoadConfigSettings` and stored in the `World` singleton for access by the stat system. Changes require a server restart or a config reload command (`reload config`).

### Database

Database modifications allow for granular control over specific creatures or classes.

*   **`creature_template`**: This table holds the definition for each creature entry.
    *   **`health_multiplier`** and **`damage_multiplier`**: These columns apply a direct multiplier to the creature's base stats before global config rates are applied. For example, setting `health_multiplier` to `1.5` on a specific boss makes it 50% tankier than its base level would suggest, regardless of the global `Rate.Creature.Normal.HP` setting.
    *   **`level_min`** and **`level_max`**: Changing these alters the level range `Creature.SelectLevel` uses. Higher levels generally result in higher stats due to the class/level scaling curves.
    *   **`unit_class`**: Determines which row in `creature_classlevelstats` is used for base stats. Changing a creature's class can drastically alter its stat distribution (e.g., switching from a Warrior-like high-HP class to a Mage-like low-HP/high-mana class).
*   **`creature_classlevelstats`**: This table defines the base stats (HP, melee damage, ranged damage, etc.) for each creature class at each level.
    *   Editing columns like **`health`**, **`melee_damage`**, or **`ranged_damage`** changes the fundamental power curve for all creatures of that class and level. This is a powerful way to rebalance entire tiers of content. For instance, increasing `health` for level 60 warriors affects all level 60 warrior-type mobs.
    *   Note: If a level is missing for a class, `ObjectMgr.LoadCreatureClassLevelStats` will interpolate values from adjacent levels, but explicit rows are preferred for precision.

Changes to these tables require a server restart or a reload command (`reload creature_template`, `reload creature_classlevelstats`).

### Code

If configuration and database adjustments are insufficient, the following code members can be edited. Rebuilding the server is required.

*   **`Creature.UpdateEntry`** (`Creature.cpp`): This method orchestrates the stat recalculation when a creature's entry changes. It calls `SelectLevel`, `InitStatsForLevel`, and `UpdateAllStats`. Modifying the order or conditions here can change how stats are applied during transformations.
*   **`Creature.SelectLevel`** (`Creature.cpp`): Currently, it picks a random level between `level_min` and `level_max`. You could modify this to always pick the maximum level, or to scale level based on player count or other factors.
*   **`Creature.UpdateAllStats`** (`StatSystem.cpp`): This method applies the final calculations. While the multipliers themselves come from config, the logic for how they are combined with base stats resides here. Advanced operators might add custom modifiers here (e.g., scaling damage based on zone difficulty).
*   **`ObjectMgr.LoadCreatureClassLevelStats`** (`ObjectMgr.cpp`): This loads the base stats from the database. If you want to hardcode stat changes or add complex interpolation logic beyond what the database allows, this is the place to do it. However, database edits are strongly preferred for maintainability.

## Path Reference

**Creature.Main/UpdateEntry** (`Creature.cpp`)
Core method that re-initializes a creature's stats, abilities, and appearance when its entry ID changes. It triggers `SelectLevel` and `UpdateAllStats` to apply new difficulty values.

**Creature.Main/SelectLevel** (`Creature.cpp`)
Determines the creature's operational level within its template-defined range (`level_min` to `level_max`). This level dictates which row of `creature_classlevelstats` is used for base HP and damage.

**ObjectMgr/LoadAllIdentifiers** (`ObjectMgr.cpp`)
Startup routine that builds sets of valid IDs from various tables, including `creature_template`. Ensures integrity checks later in the loading process can validate references.

**ObjectMgr/LoadCreatureTemplates** (`ObjectMgr.cpp`)
Loads all creature definitions from `creature_template` into memory. Parses columns like `health_multiplier`, `damage_multiplier`, and `level_min/max` which directly influence difficulty.

**ObjectMgr/LoadCreatureTemplate** (`ObjectMgr.cpp`)
Loads a single creature template by entry ID. Used for dynamic reloading or specific lookups, applying the same parsing logic as the bulk loader.

**ObjectMgr/CheckCreatureTemplate** (`ObjectMgr.cpp`)
Validates loaded creature data for consistency. Checks for invalid values in stats, factions, and spells, logging errors if difficulty-related fields (like `damage_school`) are out of bounds.

**ObjectMgr/LoadCreatureClassLevelStats** (`ObjectMgr.cpp`)
Loads the base stat curves from `creature_classlevelstats`. These values (HP, melee damage, etc.) are the foundation upon which multipliers and config rates are applied.

**ObjectMgr/LoadTrainers#2** (`ObjectMgr.cpp`)
Loads trainer spell data. While not directly setting HP/damage, it validates creature entries against trainer flags, ensuring NPCs with combat stats aren't misconfigured as pure trainers.

**ObjectMgr/LoadTrainerTemplates** (`ObjectMgr.cpp`)
Loads shared trainer templates. Similar to above, it cross-references `creature_template` to ensure consistency in NPC roles.

**ObjectMgr/LoadVendorTemplates** (`ObjectMgr.cpp`)
Loads vendor item lists. Validates that creatures marked as vendors have appropriate flags, indirectly supporting the distinction between combatants and merchants.

**World/LoadConfigSettings** (`World.cpp`)
Reads `mangosd.conf` and stores rate multipliers like `Rate.Creature.Normal.HP` and `Rate.Creature.Normal.Damage`. These values are accessed by the stat system to scale creature difficulty globally.

**boss_dathrohan_balnazzar/Reset** (`boss_dathrohan_balnazzar.cpp`)
Boss-specific reset logic. Calls `UpdateEntry` to revert the boss to its Dathrohan form, triggering a full stat recalculation based on that entry's template.

**boss_dathrohan_balnazzar/UpdateAI** (`boss_dathrohan_balnazzar.cpp`)
Main AI loop for the Dathrohan/Balnazzar encounter. Triggers `UpdateEntry` during phase transitions, causing the boss to adopt the stats of the new form (e.g., Balnazzar's higher damage profile).

**boss_dragon_of_nightmare/GetAI_boss_dragon_of_nightmare** (`boss_dragon_of_nightmare.cpp`)
Factory function for Dragon of Nightmare drakes. Calls `UpdateEntry` to assign a specific drake type (Emeriss, Lethon, etc.) based on saved variables, thereby setting its unique stats.

**burning_steppes/Transform** (`burning_steppes.cpp`)
Handles Nelson's transformation into Klinfran. Calls `UpdateEntry` to swap the creature's template, updating its HP and damage to match the demon form.

**ChatHandler.CreatureCommands/HandleNpcSpawnSetEntryCommand** (`CreatureCommands.cpp`)
Console command to permanently change a creature's entry in the database. Calls `UpdateEntry` to apply the new stats immediately and persists the change.

**ChatHandler.CreatureCommands/HandleNpcSetEntryCommand** (`CreatureCommands.cpp`)
Console command to temporarily change a creature's entry in memory. Calls `UpdateEntry` to apply new stats without altering the database.

**CreatureAI/SetSpellsList#2** (`CreatureAI.cpp`)
Assigns a spell list to a creature based on its entry. While not directly setting HP/damage, it ensures the creature has the correct abilities for its new difficulty tier after an entry change.

**darkshore/EffectDummyCreature_npc_rabid_thistle_bear** (`darkshore.cpp`)
Spell effect handler that captures a bear. Calls `UpdateEntry` to change the bear's template, altering its stats and behavior for the quest.

**felwood/WaypointReached#2** (`felwood.cpp`)
Escort quest waypoint handler. Calls `UpdateEntry` to transform Arkonarin, updating his stats for the final combat phase.

**instance_blackrock_depths/ReplacePrincessIfPossible** (`instance_blackrock_depths.cpp`)
Checks quest completion and transforms Princess Moira. Calls `UpdateEntry` to change her template, likely adjusting her stats for the transformed state.

**instance_blackrock_spire/OnCreatureCreate** (`instance_blackrock_spire.cpp`)
Instance script that randomizes trash spawns. Calls `UpdateEntry` to assign specific mob types, each with their own difficulty profile.

**instance_blackwing_lair/OnCreatureRespawn** (`instance_blackwing_lair.cpp`)
Manages respawn logic for BWL trash. Calls `UpdateEntry` to randomize whelp types, ensuring varied difficulty and elemental immunities.

**instance_blackwing_lair/OnCreatureCreate** (`instance_blackwing_lair.cpp`)
Initializes BWL creatures. Calls `UpdateEntry` for randomized spawns, setting their base stats upon entry into the world.

**instance_molten_core/OnCreatureRespawn** (`instance_molten_core.cpp`)
Handles MC trash respawns. Calls `UpdateEntry` to randomize Lava Annihilators/Firelords, swapping their stats and abilities.

**instance_molten_core/OnCreatureCreate** (`instance_molten_core.cpp`)
Initializes MC creatures. Calls `UpdateEntry` for randomized spawns, establishing their difficulty at spawn time.

**instance_naxxramas.Main/ChangeColor** (`instance_naxxramas.cpp`)
Naxxramas boss mechanic that changes a creature's color/form. Calls `UpdateEntry` to apply the new template, which may have different stats.

**instance_temple_of_ahnqiraj/OnCreatureCreate** (`instance_temple_of_ahnqiraj.cpp`)
Initializes AQ40 creatures. Calls `UpdateEntry` to randomize trash packs, assigning varied difficulty profiles to mobs.

**Map.ScriptCommands/ScriptCommand_UpdateEntry** (`ScriptCommands.cpp`)
Script command that allows scripts to change a creature's entry. Calls `UpdateEntry` to apply the new stats dynamically during gameplay.

**ObjectMgr/GetFactionEntry** (`ObjectMgr.h`)
Retrieves faction data. Used by `UpdateEntry` to set reputation and PvP flags, which can indirectly affect threat and aggro behavior.

**ObjectMgr/GetFactionTemplateEntry** (`ObjectMgr.h`)
Retrieves faction template data. Used by `UpdateEntry` to determine faction relationships, influencing how creatures react to players.

**Player.StatSystem/UpdateAllStats** (`StatSystem.cpp`)
Note: The source slice shows `Creature::UpdateAllStats`. This method recalculates all creature stats, applying multipliers from config and database. It is the final step in determining HP and damage.

**quest_stormwind_rendezvous/UpdateAI** (`quest_stormwind_rendezvous.cpp`)
Quest AI for Windsor. While it doesn't directly change difficulty, it manages summoning guards, which inherit their stats from their templates upon creation.

**Unit.Main/SetAttackTime** (`Unit.h`)
Sets the attack speed of a unit. Called by `UpdateEntry` to apply the `base_attack_time` from the template, influencing DPS.

**Unit.Main/RemoveAurasDueToSpellByCancel** (`Unit.cpp`)
Removes auras from a unit. Called by `UpdateEntry` to clean up old auras when a creature's entry changes, ensuring no lingering buffs/debuffs affect the new stats.

**Unit.Main/SetFly** (`Unit.cpp`)
Enables or disables flight. Called by `UpdateEntry` to apply movement capabilities from the new template, which can affect combat positioning and effectiveness.

**Unit.Main/SetPvP** (`Unit.cpp`)
Sets PvP status. Called by `UpdateEntry` to apply faction-based PvP flags, which can influence threat and aggro mechanics.

**WorldObject.Object/SetByteValue** (`Object.cpp`)
Low-level method to update object bytes. Used by `UpdateEntry` to sync visual and functional flags with the client.

**WorldObject.Object/SetVisibilityModifier** (`Object.cpp`)
Adjusts visibility distance. Called by `UpdateEntry` for large/gigantic creatures, affecting when they enter combat and thus their effective difficulty.

**WorldObject.Object/SetActiveObjectState** (`Object.cpp`)
Controls whether an object is actively updated by the server. Called by `UpdateEntry` for large creatures, ensuring they remain active in combat zones.

---

<!-- machine-true, projected from graph.json -->

## Map — Creature Difficulty (HP & Damage)

*Source:* Creature.cpp, ObjectMgr.cpp, World.cpp, boss_dathrohan_balnazzar.cpp, boss_dragon_of_nightmare.cpp, burning_steppes.cpp, CreatureCommands.cpp, CreatureAI.cpp, darkshore.cpp, felwood.cpp, instance_blackrock_depths.cpp, instance_blackrock_spire.cpp, instance_blackwing_lair.cpp, instance_molten_core.cpp, instance_naxxramas.cpp, instance_temple_of_ahnqiraj.cpp, ScriptCommands.cpp, ObjectMgr.h, StatSystem.cpp, quest_stormwind_rendezvous.cpp +3 more
*Config keys:* Rate.Creature.Normal.Damage (default 1), Rate.Creature.Normal.HP (default 1)
*Tables:* creature_template

| Member | Kind | Source | Role |
|---|---|---|---|
| Creature.Main/UpdateEntry | method | Creature.cpp:484-638 | seed — Creature.*/UpdateEntry* |
| Creature.Main/SelectLevel | method | Creature.cpp:1709-1720 | seed — Creature.*/SelectLevel* |
| ObjectMgr/LoadAllIdentifiers | method | ObjectMgr.cpp:180-328 | seed — queries creature_template |
| ObjectMgr/LoadCreatureTemplates | method | ObjectMgr.cpp:1188-1205 | seed — queries creature_template |
| ObjectMgr/LoadCreatureTemplate | method | ObjectMgr.cpp:1207-1224 | seed — queries creature_template |
| ObjectMgr/CheckCreatureTemplate | method | ObjectMgr.cpp:1419-1629 | seed — queries creature_template |
| ObjectMgr/LoadCreatureClassLevelStats | method | ObjectMgr.cpp:2064-2276 | seed — queries creature_template |
| ObjectMgr/LoadTrainers#2 | method | ObjectMgr.cpp:10313-10457 | seed — queries creature_template |
| ObjectMgr/LoadTrainerTemplates | method | ObjectMgr.cpp:10459-10491 | seed — queries creature_template |
| ObjectMgr/LoadVendorTemplates | method | ObjectMgr.cpp:10544-10576 | seed — queries creature_template |
| World/LoadConfigSettings | method | World.cpp:440-1245 | seed — reads config Rate.Creature.Normal.Damage |
| boss_dathrohan_balnazzar/Reset | method | boss_dathrohan_balnazzar.cpp:126-147 | related — 1 hop from a seed |
| boss_dathrohan_balnazzar/UpdateAI | method | boss_dathrohan_balnazzar.cpp:178-347 | related — 1 hop from a seed |
| boss_dragon_of_nightmare/GetAI_boss_dragon_of_nightmare | function | boss_dragon_of_nightmare.cpp:234-255 | related — 1 hop from a seed |
| burning_steppes/Transform | method | burning_steppes.cpp:486-493 | related — 1 hop from a seed |
| ChatHandler.CreatureCommands/HandleNpcSpawnSetEntryCommand | method | CreatureCommands.cpp:188-215 | related — 1 hop from a seed |
| ChatHandler.CreatureCommands/HandleNpcSetEntryCommand | method | CreatureCommands.cpp:217-237 | related — 1 hop from a seed |
| CreatureAI/SetSpellsList#2 | method | CreatureAI.cpp:81-89 | related — 1 hop from a seed |
| darkshore/EffectDummyCreature_npc_rabid_thistle_bear | function | darkshore.cpp:878-898 | related — 1 hop from a seed |
| felwood/WaypointReached#2 | method | felwood.cpp:199-264 | related — 1 hop from a seed |
| instance_blackrock_depths/ReplacePrincessIfPossible | method | instance_blackrock_depths.cpp:721-744 | related — 1 hop from a seed |
| instance_blackrock_spire/OnCreatureCreate | method | instance_blackrock_spire.cpp:316-362 | related — 1 hop from a seed |
| instance_blackwing_lair/OnCreatureRespawn | method | instance_blackwing_lair.cpp:344-431 | related — 1 hop from a seed |
| instance_blackwing_lair/OnCreatureCreate | method | instance_blackwing_lair.cpp:451-583 | related — 1 hop from a seed |
| instance_molten_core/OnCreatureRespawn | method | instance_molten_core.cpp:170-258 | related — 1 hop from a seed |
| instance_molten_core/OnCreatureCreate | method | instance_molten_core.cpp:290-428 | related — 1 hop from a seed |
| instance_naxxramas.Main/ChangeColor | method | instance_naxxramas.cpp:1639-1652 | related — 1 hop from a seed |
| instance_temple_of_ahnqiraj/OnCreatureCreate | method | instance_temple_of_ahnqiraj.cpp:275-318 | related — 1 hop from a seed |
| Map.ScriptCommands/ScriptCommand_UpdateEntry | method | ScriptCommands.cpp:982-996 | related — 1 hop from a seed |
| ObjectMgr/GetFactionEntry | method | ObjectMgr.h:1374-1381 | related — 1 hop from a seed |
| ObjectMgr/GetFactionTemplateEntry | method | ObjectMgr.h:1386-1393 | related — 1 hop from a seed |
| Player.StatSystem/UpdateAllStats | method | StatSystem.cpp:719-742 | related — 1 hop from a seed |
| quest_stormwind_rendezvous/UpdateAI | method | quest_stormwind_rendezvous.cpp:241-799 | related — 1 hop from a seed |
| Unit.Main/SetAttackTime | method | Unit.h:438-443 | related — 1 hop from a seed |
| Unit.Main/RemoveAurasDueToSpellByCancel | method | Unit.cpp:3887-3896 | related — 1 hop from a seed |
| Unit.Main/SetFly | method | Unit.cpp:7511-7517 | related — 1 hop from a seed |
| Unit.Main/SetPvP | method | Unit.cpp:10136-10148 | related — 1 hop from a seed |
| WorldObject.Object/SetByteValue | method | Object.cpp:1211-1227 | related — 1 hop from a seed |
| WorldObject.Object/SetVisibilityModifier | method | Object.cpp:1493-1496 | related — 1 hop from a seed |
| WorldObject.Object/SetActiveObjectState | method | Object.cpp:3292-3320 | related — 1 hop from a seed |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `areatrigger_template`: id smallint(4) unsigned PK, build smallint(4) unsigned PK, name varchar(128)?, map_id smallint(3) unsigned, x float, y float, z float, radius float, box_x float, box_y float, box_z float, box_orientation float, cooldown int(10) unsigned, condition_id int(10) unsigned, script_id int(10) unsigned, script_name varchar(64)
- `conditions`: condition_entry mediumint(8) unsigned PK, type tinyint(3), value1 int(11), value2 int(11), value3 int(11), value4 int(11), flags tinyint(3) unsigned
- `creature`: guid int(10) unsigned PK, id mediumint(8) unsigned, id2 mediumint(8) unsigned, id3 mediumint(8) unsigned, id4 mediumint(8) unsigned, id5 mediumint(8) unsigned, map smallint(5) unsigned, position_x float, position_y float, position_z float, orientation float, spawntimesecsmin int(10) unsigned, spawntimesecsmax int(10) unsigned, wander_distance float, health_percent float, mana_percent float unsigned, movement_type tinyint(3) unsigned, spawn_flags int(10) unsigned, visibility_mod float?, patch_min tinyint(3) unsigned, patch_max tinyint(3) unsigned
- `creature_classlevelstats`: class tinyint(3) unsigned PK, level tinyint(3) unsigned PK, melee_damage float, ranged_damage float, attack_power int(11), ranged_attack_power int(11), health int(11), base_health int(11), mana int(11), base_mana int(11), strength int(11), agility int(11), stamina int(11), intellect int(11), spirit int(11), armor int(11)
- `creature_spells`: entry int(11) unsigned PK, name varchar(255), spellId_1 smallint(5) unsigned, probability_1 tinyint(3) unsigned, castTarget_1 tinyint(2) unsigned, targetParam1_1 smallint(5) unsigned, targetParam2_1 smallint(5) unsigned, castFlags_1 smallint(5) unsigned, delayInitialMin_1 smallint(5) unsigned, delayInitialMax_1 smallint(5) unsigned, delayRepeatMin_1 smallint(5) unsigned, delayRepeatMax_1 smallint(5) unsigned, scriptId_1 mediumint(8) unsigned, spellId_2 smallint(5) unsigned, probability_2 tinyint(3) unsigned, castTarget_2 tinyint(2) unsigned, targetParam1_2 smallint(5) unsigned, targetParam2_2 smallint(5) unsigned, castFlags_2 smallint(5) unsigned, delayInitialMin_2 smallint(5) unsigned, delayInitialMax_2 smallint(5) unsigned, delayRepeatMin_2 smallint(5) unsigned, delayRepeatMax_2 smallint(5) unsigned, scriptId_2 mediumint(8) unsigned, spellId_3 smallint(5) unsigned, probability_3 tinyint(3) unsigned, castTarget_3 tinyint(2) unsigned, targetParam1_3 smallint(5) unsigned, targetParam2_3 smallint(5) unsigned, castFlags_3 smallint(5) unsigned, delayInitialMin_3 smallint(5) unsigned, delayInitialMax_3 smallint(5) unsigned, delayRepeatMin_3 smallint(5) unsigned, delayRepeatMax_3 smallint(5) unsigned, scriptId_3 mediumint(8) unsigned, spellId_4 smallint(5) unsigned, probability_4 tinyint(3) unsigned, castTarget_4 tinyint(2) unsigned, targetParam1_4 smallint(5) unsigned, targetParam2_4 smallint(5) unsigned, castFlags_4 smallint(5) unsigned, delayInitialMin_4 smallint(5) unsigned, delayInitialMax_4 smallint(5) unsigned, delayRepeatMin_4 smallint(5) unsigned, delayRepeatMax_4 smallint(5) unsigned, scriptId_4 mediumint(8) unsigned, spellId_5 smallint(5) unsigned, probability_5 tinyint(3) unsigned, castTarget_5 tinyint(2) unsigned, targetParam1_5 smallint(5) unsigned, targetParam2_5 smallint(5) unsigned, castFlags_5 smallint(5) unsigned, delayInitialMin_5 smallint(5) unsigned, delayInitialMax_5 smallint(5) unsigned, delayRepeatMin_5 smallint(5) unsigned, delayRepeatMax_5 smallint(5) unsigned, scriptId_5 mediumint(8) unsigned, spellId_6 smallint(5) unsigned, probability_6 tinyint(3) unsigned, castTarget_6 tinyint(2) unsigned, targetParam1_6 smallint(5) unsigned, targetParam2_6 smallint(5) unsigned, castFlags_6 smallint(5) unsigned, delayInitialMin_6 smallint(5) unsigned, delayInitialMax_6 smallint(5) unsigned, delayRepeatMin_6 smallint(5) unsigned, delayRepeatMax_6 smallint(5) unsigned, scriptId_6 mediumint(8) unsigned, spellId_7 smallint(5) unsigned, probability_7 tinyint(3) unsigned, castTarget_7 tinyint(2) unsigned, targetParam1_7 smallint(5) unsigned, targetParam2_7 smallint(5) unsigned, castFlags_7 smallint(5) unsigned, delayInitialMin_7 smallint(5) unsigned, delayInitialMax_7 smallint(5) unsigned, delayRepeatMin_7 smallint(5) unsigned, delayRepeatMax_7 smallint(5) unsigned, scriptId_7 mediumint(8) unsigned, spellId_8 smallint(5) unsigned, probability_8 tinyint(3) unsigned, castTarget_8 tinyint(2) unsigned, targetParam1_8 smallint(5) unsigned, targetParam2_8 smallint(5) unsigned, castFlags_8 smallint(5) unsigned, delayInitialMin_8 smallint(5) unsigned, delayInitialMax_8 smallint(5) unsigned, delayRepeatMin_8 smallint(5) unsigned, delayRepeatMax_8 smallint(5) unsigned, scriptId_8 mediumint(8) unsigned
- `creature_template`: entry mediumint(8) unsigned PK, patch tinyint(3) unsigned PK, name char(100), subname char(100)?, level_min tinyint(3) unsigned, level_max tinyint(3) unsigned, faction smallint(5) unsigned, npc_flags int(10) unsigned, gossip_menu_id mediumint(8) unsigned, display_id1 mediumint(8) unsigned, display_id2 mediumint(8) unsigned, display_id3 mediumint(8) unsigned, display_id4 mediumint(8) unsigned, display_scale1 float, display_scale2 float, display_scale3 float, display_scale4 float, display_probability1 smallint(5) unsigned, display_probability2 smallint(5) unsigned, display_probability3 smallint(5) unsigned, display_probability4 smallint(5) unsigned, display_total_probability smallint(5) unsigned, mount_display_id smallint(5) unsigned, speed_walk float, speed_run float, detection_range float, call_for_help_range float, leash_range float, type tinyint(3) unsigned, pet_family tinyint(4) unsigned, rank tinyint(3) unsigned, unit_class tinyint(3) unsigned, xp_multiplier float, health_multiplier float, mana_multiplier float, armor_multiplier float, damage_multiplier float, damage_variance float, damage_school tinyint(4) unsigned, base_attack_time int(10) unsigned, ranged_attack_time int(10) unsigned, holy_res smallint(5), fire_res smallint(5), nature_res smallint(5), frost_res smallint(5), shadow_res smallint(5), arcane_res smallint(5), trainer_type tinyint(4) unsigned, trainer_spell smallint(5) unsigned, trainer_class tinyint(3) unsigned, trainer_race tinyint(3) unsigned, loot_id mediumint(8) unsigned, pickpocket_loot_id mediumint(8) unsigned, skinning_loot_id mediumint(8) unsigned, gold_min mediumint(8) unsigned, gold_max mediumint(8) unsigned, spell_id1 smallint(5) unsigned, spell_id2 smallint(5) unsigned, spell_id3 smallint(5) unsigned, spell_id4 smallint(5) unsigned, spell_list_id int(11) unsigned, pet_spell_list_id mediumint(8) unsigned, spawn_spell_id smallint(5) unsigned, auras text?, ai_name char(64), movement_type tinyint(3) unsigned, inhabit_type tinyint(3) unsigned, civilian tinyint(3) unsigned, racial_leader tinyint(3) unsigned, equipment_id mediumint(8) unsigned, trainer_id mediumint(8) unsigned, vendor_id mediumint(8) unsigned, mechanic_immune_mask int(10) unsigned, school_immune_mask int(10) unsigned, immunity_flags int(10) unsigned, static_flags1 int(10) unsigned, static_flags2 int(10) unsigned, flags_extra int(10) unsigned, script_name char(64)
- `gameobject`: guid int(10) unsigned PK, id mediumint(8) unsigned, map smallint(5) unsigned, position_x float, position_y float, position_z float, orientation float, rotation0 float, rotation1 float, rotation2 float, rotation3 float, spawntimesecsmin int(11), spawntimesecsmax int(11), animprogress tinyint(3) unsigned, state tinyint(3) unsigned, spawn_flags int(10) unsigned, visibility_mod float?, patch_min tinyint(3) unsigned, patch_max tinyint(3) unsigned
- `gameobject_template`: entry mediumint(8) unsigned PK, patch tinyint(3) unsigned PK, type tinyint(3) unsigned, displayId mediumint(8) unsigned, name varchar(100), icon varchar(100), faction smallint(5) unsigned, flags int(10) unsigned, size float, data0 int(10), data1 int(11), data2 int(10), data3 int(10), data4 int(10), data5 int(10), data6 int(11), data7 int(10), data8 int(10), data9 int(10), data10 int(10), data11 int(10), data12 int(10), data13 int(10), data14 int(10), data15 int(10), data16 int(10), data17 int(10), data18 int(10), data19 int(10), data20 int(10), data21 int(10), data22 int(10), data23 int(10), mingold mediumint(8) unsigned, maxgold mediumint(8) unsigned, script_name varchar(64)
- `gossip_menu`: entry smallint(6) unsigned PK, text_id mediumint(8) unsigned PK, script_id mediumint(8) unsigned, condition_id mediumint(8) unsigned
- `item_template`: entry mediumint(8) unsigned PK, patch tinyint(3) unsigned PK, class tinyint(3) unsigned, subclass tinyint(3) unsigned, name varchar(255), description varchar(255), display_id mediumint(8) unsigned, quality tinyint(3) unsigned, flags int(10) unsigned, buy_count tinyint(3) unsigned, buy_price int(10) unsigned, sell_price int(10) unsigned, inventory_type tinyint(3) unsigned, allowable_class mediumint(9), allowable_race mediumint(9), item_level tinyint(3) unsigned, required_level tinyint(3) unsigned, required_skill smallint(5) unsigned, required_skill_rank smallint(5) unsigned, required_spell smallint(5) unsigned, required_honor_rank mediumint(8) unsigned, required_city_rank mediumint(8) unsigned, required_reputation_faction smallint(5) unsigned, required_reputation_rank smallint(5) unsigned, max_count smallint(5) unsigned, stackable smallint(5) unsigned, container_slots tinyint(3) unsigned, stat_type1 tinyint(3) unsigned, stat_value1 smallint(6), stat_type2 tinyint(3) unsigned, stat_value2 smallint(6), stat_type3 tinyint(3) unsigned, stat_value3 smallint(6), stat_type4 tinyint(3) unsigned, stat_value4 smallint(6), stat_type5 tinyint(3) unsigned, stat_value5 smallint(6), stat_type6 tinyint(3) unsigned, stat_value6 smallint(6), stat_type7 tinyint(3) unsigned, stat_value7 smallint(6), stat_type8 tinyint(3) unsigned, stat_value8 smallint(6), stat_type9 tinyint(3) unsigned, stat_value9 smallint(6), stat_type10 tinyint(3) unsigned, stat_value10 smallint(6), delay smallint(5) unsigned, range_mod float, ammo_type tinyint(3) unsigned, dmg_min1 float, dmg_max1 float, dmg_type1 tinyint(3) unsigned, dmg_min2 float, dmg_max2 float, dmg_type2 tinyint(3) unsigned, dmg_min3 float, dmg_max3 float, dmg_type3 tinyint(3) unsigned, dmg_min4 float, dmg_max4 float, dmg_type4 tinyint(3) unsigned, dmg_min5 float, dmg_max5 float, dmg_type5 tinyint(3) unsigned, block mediumint(8) unsigned, armor smallint(5), holy_res smallint(5), fire_res smallint(5), nature_res smallint(5), frost_res smallint(5), shadow_res smallint(5), arcane_res smallint(5), spellid_1 smallint(5) unsigned, spelltrigger_1 tinyint(3) unsigned, spellcharges_1 tinyint(4), spellppmrate_1 float, spellcooldown_1 int(11), spellcategory_1 smallint(5) unsigned, spellcategorycooldown_1 int(11), spellid_2 smallint(5) unsigned, spelltrigger_2 tinyint(3) unsigned, spellcharges_2 tinyint(4), spellppmrate_2 float, spellcooldown_2 int(11), spellcategory_2 smallint(5) unsigned, spellcategorycooldown_2 int(11), spellid_3 smallint(5) unsigned, spelltrigger_3 tinyint(3) unsigned, spellcharges_3 tinyint(4), spellppmrate_3 float, spellcooldown_3 int(11), spellcategory_3 smallint(5) unsigned, spellcategorycooldown_3 int(11), spellid_4 smallint(5) unsigned, spelltrigger_4 tinyint(3) unsigned, spellcharges_4 tinyint(4), spellppmrate_4 float, spellcooldown_4 int(11), spellcategory_4 smallint(5) unsigned, spellcategorycooldown_4 int(11), spellid_5 smallint(5) unsigned, spelltrigger_5 tinyint(3) unsigned, spellcharges_5 tinyint(4), spellppmrate_5 float, spellcooldown_5 int(11), spellcategory_5 smallint(5) unsigned, spellcategorycooldown_5 int(11), bonding tinyint(3) unsigned, page_text mediumint(8) unsigned, page_language tinyint(3) unsigned, page_material tinyint(3) unsigned, start_quest mediumint(8) unsigned, lock_id mediumint(8) unsigned, material tinyint(4), sheath tinyint(3) unsigned, random_property mediumint(8) unsigned, set_id mediumint(8) unsigned, max_durability smallint(5) unsigned, area_bound mediumint(8) unsigned, map_bound smallint(6), duration int(11) unsigned, bag_family mediumint(9), disenchant_id mediumint(8) unsigned, food_type tinyint(3) unsigned, min_money_loot int(10) unsigned, max_money_loot int(10) unsigned, wrapped_gift mediumint(8) unsigned, extra_flags tinyint(1) unsigned, other_team_entry int(11) unsigned?
- `npc_vendor_template`: entry mediumint(8) unsigned PK, slot smallint(5) unsigned, item mediumint(8) unsigned PK, maxcount tinyint(3) unsigned, incrtime int(10) unsigned, itemflags int(10) unsigned, condition_id mediumint(8) unsigned
- `quest_template`: entry mediumint(8) unsigned PK, patch tinyint(3) unsigned PK, Method tinyint(3) unsigned, ZoneOrSort smallint(6), MinLevel tinyint(3) unsigned, MaxLevel tinyint(3) unsigned, QuestLevel tinyint(3) unsigned, Type smallint(5) unsigned, RequiredClasses smallint(5) unsigned, RequiredRaces smallint(5) unsigned, RequiredSkill smallint(5) unsigned, RequiredSkillValue smallint(5) unsigned, RequiredCondition mediumint(8) unsigned, RepObjectiveFaction smallint(5) unsigned, RepObjectiveValue mediumint(9), RequiredMinRepFaction smallint(5) unsigned, RequiredMinRepValue mediumint(9), RequiredMaxRepFaction smallint(5) unsigned, RequiredMaxRepValue mediumint(9), SuggestedPlayers tinyint(3) unsigned, LimitTime int(10) unsigned, QuestFlags smallint(5) unsigned, SpecialFlags tinyint(3) unsigned, PrevQuestId mediumint(9), NextQuestId mediumint(9), ExclusiveGroup mediumint(9), BreadcrumbForQuestId mediumint(9) unsigned, NextQuestInChain mediumint(8) unsigned, SrcItemId mediumint(8) unsigned, SrcItemCount tinyint(3) unsigned, SrcSpell smallint(5) unsigned, Title text?, Details text?, Objectives text?, OfferRewardText text?, RequestItemsText text?, EndText text?, ObjectiveText1 text?, ObjectiveText2 text?, ObjectiveText3 text?, ObjectiveText4 text?, ReqItemId1 mediumint(8) unsigned, ReqItemId2 mediumint(8) unsigned, ReqItemId3 mediumint(8) unsigned, ReqItemId4 mediumint(8) unsigned, ReqItemCount1 smallint(5) unsigned, ReqItemCount2 smallint(5) unsigned, ReqItemCount3 smallint(5) unsigned, ReqItemCount4 smallint(5) unsigned, ReqSourceId1 mediumint(8) unsigned, ReqSourceId2 mediumint(8) unsigned, ReqSourceId3 mediumint(8) unsigned, ReqSourceId4 mediumint(8) unsigned, ReqSourceCount1 mediumint(8) unsigned, ReqSourceCount2 mediumint(8) unsigned, ReqSourceCount3 mediumint(8) unsigned, ReqSourceCount4 mediumint(8) unsigned, ReqCreatureOrGOId1 mediumint(9), ReqCreatureOrGOId2 mediumint(9), ReqCreatureOrGOId3 mediumint(9), ReqCreatureOrGOId4 mediumint(9), ReqCreatureOrGOCount1 smallint(5) unsigned, ReqCreatureOrGOCount2 smallint(5) unsigned, ReqCreatureOrGOCount3 smallint(5) unsigned, ReqCreatureOrGOCount4 smallint(5) unsigned, ReqSpellCast1 smallint(5) unsigned, ReqSpellCast2 smallint(5) unsigned, ReqSpellCast3 smallint(5) unsigned, ReqSpellCast4 smallint(5) unsigned, RewChoiceItemId1 mediumint(8) unsigned, RewChoiceItemId2 mediumint(8) unsigned, RewChoiceItemId3 mediumint(8) unsigned, RewChoiceItemId4 mediumint(8) unsigned, RewChoiceItemId5 mediumint(8) unsigned, RewChoiceItemId6 mediumint(8) unsigned, RewChoiceItemCount1 smallint(5) unsigned, RewChoiceItemCount2 smallint(5) unsigned, RewChoiceItemCount3 smallint(5) unsigned, RewChoiceItemCount4 smallint(5) unsigned, RewChoiceItemCount5 smallint(5) unsigned, RewChoiceItemCount6 smallint(5) unsigned, RewItemId1 mediumint(8) unsigned, RewItemId2 mediumint(8) unsigned, RewItemId3 mediumint(8) unsigned, RewItemId4 mediumint(8) unsigned, RewItemCount1 smallint(5) unsigned, RewItemCount2 smallint(5) unsigned, RewItemCount3 smallint(5) unsigned, RewItemCount4 smallint(5) unsigned, RewRepFaction1 smallint(5) unsigned, RewRepFaction2 smallint(5) unsigned, RewRepFaction3 smallint(5) unsigned, RewRepFaction4 smallint(5) unsigned, RewRepFaction5 smallint(5) unsigned, RewRepValue1 mediumint(9), RewRepValue2 mediumint(9), RewRepValue3 mediumint(9), RewRepValue4 mediumint(9), RewRepValue5 mediumint(9), RewRepSpilloverMask tinyint(3) unsigned, RewXP mediumint(9) unsigned, RewOrReqMoney int(11), RewMoneyMaxLevel int(10) unsigned, RewSpell smallint(5) unsigned, RewSpellCast smallint(5) unsigned, RewMailTemplateId mediumint(8), RewMailDelaySecs int(11) unsigned, RewMailMoney int(10) unsigned, PointMapId smallint(5) unsigned, PointX float, PointY float, PointOpt mediumint(8) unsigned, DetailsEmote1 smallint(5) unsigned, DetailsEmote2 smallint(5) unsigned, DetailsEmote3 smallint(5) unsigned, DetailsEmote4 smallint(5) unsigned, DetailsEmoteDelay1 int(11) unsigned, DetailsEmoteDelay2 int(11) unsigned, DetailsEmoteDelay3 int(11) unsigned, DetailsEmoteDelay4 int(11) unsigned, IncompleteEmote smallint(5) unsigned, CompleteEmote smallint(5) unsigned, OfferRewardEmote1 smallint(5) unsigned, OfferRewardEmote2 smallint(5) unsigned, OfferRewardEmote3 smallint(5) unsigned, OfferRewardEmote4 smallint(5) unsigned, OfferRewardEmoteDelay1 int(11) unsigned, OfferRewardEmoteDelay2 int(11) unsigned, OfferRewardEmoteDelay3 int(11) unsigned, OfferRewardEmoteDelay4 int(11) unsigned, StartScript mediumint(8) unsigned, CompleteScript mediumint(8) unsigned

*`?` = nullable, `PK` = primary key column.*


---

<!-- verify: invented-config | keys: Creature.SelectLevel, Creature.UpdateAllStats, Creature.UpdateEntry, ObjectMgr.LoadCreatureClassLevelStats, World.LoadConfigSettings -->
