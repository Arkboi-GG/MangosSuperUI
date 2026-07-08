# Script

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ScriptMgr

**Purpose & Responsibilities**

`ScriptMgr` is the central registry and dispatcher for the server’s scripting system. It bridges the core engine logic with user-defined scripts that implement specific game behaviors, such as boss mechanics, quest interactions, gossip menus, and area triggers.

Its primary responsibilities are:
1.  **Registration:** It maintains global maps (e.g., `sAreaTriggerScripts`, `sCreatureAIScripts`) that link entity IDs (creature entries, spell IDs, area trigger IDs) to specific script implementations.
2.  **Dispatch:** When the core engine encounters a scripted event (e.g., a player clicks a creature, a spell hits a target), `ScriptMgr` looks up the relevant ID in its maps and invokes the corresponding function pointer stored in a `Script` struct.
3.  **Data Management:** It loads and caches auxiliary script data from the database, including custom text strings, waypoint coordinates for movement scripts, and escort quest configurations.
4.  **Lifecycle Management:** It provides hooks for creating AI objects (`CreatureAI`, `GameObjectAI`) and instance data (`InstanceData`) dynamically based on script registration.

The class is implemented as a Singleton (`sScriptMgr`), ensuring a single point of access for all script-related lookups throughout the server.

## Member-by-Member Behavior

### Initialization and Loading

The `ScriptMgr` initializes its internal state and populates its lookup tables from the database and registered scripts.

*   **`ScriptMgr()` / `~ScriptMgr()`**: The constructor initializes the atomic counter `m_scheduledScripts` to zero. The destructor cleans up resources.
*   **`Initialize()`**: Performs final setup steps after all scripts have been registered and database data loaded.
*   **`LoadDatabase()`**: Triggers the loading of script-related data from the database tables (text, waypoints, escort data).
*   **`LoadScriptNames()`**: Populates `m_scriptNames` with script identifiers, allowing lookup by name or ID.
*   **`LoadScriptTexts()` / `LoadScriptTextsCustom()`**: Loads custom text strings defined in the database into `m_mTextDataMap`. These texts are used by scripts to broadcast messages, replacing standard DBC text entries.
*   **`LoadScriptWaypoints()`**: Loads waypoint data into `m_mPointMoveMap`. This data defines paths for creatures using movement scripts.
*   **`LoadEscortData()`**: Loads escort quest configuration into `m_mEscortDataMap`, linking creature entries to quest IDs and faction settings for escort missions.
*   **`LoadAreaTriggerScripts()`**, **`LoadGameObjectScripts()`**, **`LoadQuestEndScripts()`**, **`LoadQuestStartScripts()`**, **`LoadEventScripts()`**, **`LoadSpellScripts()`**, **`LoadCreatureSpellScripts()`**, **`LoadGenericScripts()`**, **`LoadGossipScripts()`**, **`LoadCreatureMovementScripts()`**, **`LoadCreatureEventAIScripts()`**: These methods populate the respective global `ScriptMapMap` structures (e.g., `sAreaTriggerScripts`, `sCreatureAIScripts`). They iterate over the registered `Script` objects and insert them into the appropriate maps based on the IDs they handle.

### Script Dispatch and Invocation

These methods are called by the core engine when a specific event occurs. They perform the lookup in the appropriate map and invoke the registered callback.

*   **`OnGossipHello(Player*, Creature*)` / `OnGossipHello(Player*, GameObject*)`**: Invoked when a player initiates gossip with a creature or game object. Looks up the script in `sGossipScripts` and calls the `pGossipHello` or `pGOGossipHello` function pointer.
*   **`OnGossipSelect(Player*, Creature*, ...)` / `OnGossipSelect(Player*, GameObject*, ...)`**: Invoked when a player selects an option from the gossip menu. Looks up the script and calls `pGossipSelect` or `pGOGossipSelect` (with or without code, depending on the overload).
*   **`OnQuestAccept(Player*, Creature*, Quest const*)` / `OnQuestAccept(Player*, GameObject*, Quest const*)`**: Invoked when a player accepts a quest from an NPC or GO. Calls `pQuestAcceptNPC` or `pGOQuestAccept`.
*   **`OnQuestRewarded(Player*, Creature*, Quest const*)` / `OnQuestRewarded(Player*, GameObject*, Quest const*)`**: Invoked when a player completes a quest. Calls `pQuestRewardedNPC` or `pQuestRewardedGO`.
*   **`GetDialogStatus(Player*, Creature*)` / `GetDialogStatus(Player*, GameObject*)`**: Determines the dialog status (e.g., whether gossip is available) by calling `pNPCDialogStatus` or `pGODialogStatus`.
*   **`OnGameObjectUse(Player*, GameObject*)`**: Handles general use events for game objects.
*   **`OnGameObjectOpen(Player*, GameObject*)`**: Handles opening events (e.g., chests) by calling `pGOOpen`.
*   **`OnAreaTrigger(Player*, AreaTriggerEntry const*)`**: Invoked when a player enters an area trigger zone. Looks up the trigger ID in `sAreaTriggerScripts` and calls `pAreaTrigger`.
*   **`OnProcessEvent(uint32, Object*, Object*, bool)`**: Handles generic event IDs. Looks up the event ID in `sEventScripts` and calls `pProcessEventId`.
*   **`OnEffectDummy(WorldObject*, uint32, SpellEffectIndex, Creature*)` / `OnEffectDummy(..., GameObject*)`**: Handles "dummy" spell effects, which are placeholders for custom scripted behavior. Calls `pEffectDummyCreature` or `pEffectDummyGameObj`.
*   **`OnAuraDummy(Aura const*, bool)`**: Handles dummy aura effects. Calls `pEffectAuraDummy`.

### AI and Instance Data Creation

These methods allow scripts to provide custom AI or instance data classes.

*   **`GetCreatureAI(Creature*)`**: Retrieves the `CreatureAI` object for a creature. If a script has registered a `GetAI` function for that creature's entry, it calls that function to create the AI. Otherwise, it likely returns a default AI or null.
*   **`GetGameObjectAI(GameObject*)`**: Similar to `GetCreatureAI`, but for `GameObjectAI`. Calls the registered `GOGetAI` function.
*   **`CreateInstanceData(Map*)`**: Creates the `InstanceData` object for a map. Calls the registered `GetInstanceData` function for that map's script.
*   **`GetSpellScript(SpellEntry const*)`**: Retrieves a `SpellScript` object for a given spell. Calls the registered `GetSpellScript` function.
*   **`GetAuraScript(SpellEntry const*)`**: Retrieves an `AuraScript` object for a given spell. Calls the registered `GetAuraScript` function.

### Data Accessors

These methods provide read-only access to the cached script data.

*   **`GetTextData(int32)`**: Returns a pointer to `StringTextData` for a given text ID from `m_mTextDataMap`.
*   **`GetEscortData(int32)`**: Returns a pointer to `CreatureEscortData` for a given creature ID from `m_mEscortDataMap`.
*   **`GetPointMoveList(uint32)`**: Returns a vector of `ScriptPointMove` structs for a given creature entry from `m_mPointMoveMap`.
*   **`IsCreatureGuidReferencedInScripts(uint32)` / `IsGameObjectGuidReferencedInScripts(uint32)`**: Checks if a specific GUID is referenced in any script (likely used for persistence or cleanup logic).
*   **`GetScriptName(uint32)` / `GetScriptId(char const*)` / `GetScriptIdsCount()`**: Utilities for managing script names and IDs.
*   **`GetEventIdScriptId(uint32)`**: Maps an event ID to a script ID.

### Utility and Validation

*   **`CheckAllScriptTexts()`**: Validates that all text IDs used in scripts exist in the database or custom text tables.
*   **`CheckScriptTargets(...)`**: Validates target parameters for scripts.
*   **`IncreaseScheduledScriptsCount()` / `DecreaseScheduledScriptCount()` / `IsScriptScheduled()`**: Manages an atomic counter `m_scheduledScripts` to track the number of active scheduled scripts. This is likely used to prevent server shutdown while scripts are running or to manage script execution order.

## Cross-Unit Boundaries

`ScriptMgr` is heavily integrated with the rest of the server. It is called by nearly every script module to register its callbacks.

*   **Called By:** All script modules (e.g., `boss_anubrekhan`, `instance_naxxramas`, `spell_druid`) call `ScriptMgr` methods (via the `Script` struct's `RegisterSelf` or direct calls to `sScriptMgr`) during server startup to register their handlers. The core engine calls `ScriptMgr` dispatch methods (e.g., `OnGossipHello`) during gameplay.
*   **Calls Out:** `ScriptMgr` itself does not call out to other units for business logic. It primarily interacts with the database layer (implicitly via `LoadDatabase` and related methods) and the core engine classes (`Creature`, `GameObject`, `Spell`, etc.) as parameters to its dispatch methods. The actual script logic resides in the separate script modules.

## Data Model

`ScriptMgr` interacts with several database tables to load script configuration data. While the exact schema is not provided, the code implies the following tables:

*   **`script_texts`**: Stores custom text strings used by scripts. Columns likely include `entry` (ID), `content_default`, `sound`, `type`, `language`, `emote`.
*   **`script_waypoints`**: Stores waypoint data for movement scripts. Columns likely include `entry` (creature entry), `pointid`, `position_x`, `position_y`, `position_z`, `waittime`.
*   **`script_escort_data`**: Stores data for escort quests. Columns likely include `creature_entry`, `quest_entry`, `escort_faction`, `last_waypoint_entry`.
*   **`script_names`**: Maps script IDs to names.
*   **Various script registration tables**: Tables like `area_trigger_scripts`, `creature_ai_scripts`, `gameobject_scripts`, etc., store the mapping between entity IDs and script names/IDs.

## Notable Implementation Details

*   **Function Pointers:** The `Script` struct uses raw function pointers for all callbacks. This allows for high-performance dispatch without the overhead of virtual function tables or reflection. However, it requires careful management of function signatures.
*   **Atomic Counter:** The use of `std::atomic<int>` for `m_scheduledScripts` suggests that script scheduling is thread-safe or at least needs to be tracked accurately across multiple threads.
*   **Global Maps:** The global `ScriptMapMap` variables (e.g., `sAreaTriggerScripts`) are populated by `ScriptMgr` but accessed directly by other parts of the codebase. This decouples the lookup logic from the manager, potentially improving performance but reducing encapsulation.
*   **Dummy Effects:** The "dummy" spell and aura effects are a key mechanism for implementing custom spell behavior. Scripts register handlers for these effects, which are then invoked by `ScriptMgr` when the spell or aura is applied.
*   **Text Source Ranges:** The constants `TEXT_SOURCE_RANGE`, `TEXT_SOURCE_TEXT_START`, etc., define ranges for different types of text sources (DBC vs. custom). This allows the system to distinguish between standard game text and custom script text.

## Member Reference

*   **Script**: Constructor for the `Script` struct. Initializes all function pointers to `nullptr` and the name to an empty string. Called by all script modules to create a `Script` instance for registration.

---

<!-- machine-true, projected from graph.json -->

## Map — Script

*Source:* ScriptMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Script | ctor | — | arathi_highlands/AddSC_arathi_highlands, areatrigger_scripts/AddSC_areatrigger_scripts, arena_challenge_ai/AddSC_blackrock_depths_arena_challenge, ashenvale/AddSC_ashenvale, azshara/AddSC_azshara, blackrock_depths/AddSC_blackrock_depths, blasted_lands/AddSC_blasted_lands, boss_anubrekhan/AddSC_boss_anubrekhan, boss_anubshiah/AddSC_boss_anubshiah, boss_arcanist_doan/AddSC_boss_arcanist_doan, boss_archaedas/AddSC_boss_archaedas, boss_arlokk/AddSC_boss_arlokk, boss_ayamiss/AddSC_boss_ayamiss, boss_baroness_anastari/AddSC_boss_baroness_anastari, boss_baron_geddon/AddSC_boss_baron_geddon, boss_broodlord_lashlayer/AddSC_boss_broodlord, boss_bug_trio/AddSC_bug_trio, boss_buru/AddSC_boss_buru, boss_cannon_master_willey/AddSC_boss_cannon_master_willey, boss_celebras_the_cursed/AddSC_boss_celebras_the_cursed, boss_chromaggus/AddSC_boss_chromaggus, boss_cthun/AddSC_boss_cthun, boss_dathrohan_balnazzar/AddSC_boss_dathrohan_balnazzar, boss_doctor_theolen_krastinov/AddSC_boss_theolenkrastinov, boss_dragon_of_nightmare/AddSC_dragons_of_nightmare, boss_ebonroc/AddSC_boss_ebonroc, boss_emperor_dagran_thaurissan/AddSC_boss_draganthaurissan, boss_faerlina/AddSC_boss_faerlina, boss_fankriss/AddSC_boss_fankriss, boss_firemaw/AddSC_boss_firemaw, boss_flamegor/AddSC_boss_flamegor, boss_four_horsemen/AddSC_boss_four_horsemen, boss_gahzranka/AddSC_boss_gahzranka, boss_garr/AddSC_boss_garr, boss_gehennas/AddSC_boss_gehennas, boss_general_angerforge/AddSC_boss_general_angerforge, boss_gluth/AddSC_boss_gluth, boss_golemagg/AddSC_boss_golemagg, boss_gordok_king/AddSC_npc_king_gordok, boss_gorosh_the_dervish/AddSC_boss_gorosh_the_dervish, boss_gothik/AddSC_boss_gothik, boss_grizzle/AddSC_boss_grizzle, boss_grobbulus/AddSC_boss_grobbulus, boss_hakkar/AddSC_boss_hakkar, boss_halycon/AddSC_boss_halycon, boss_heigan/AddSC_boss_heigan, boss_herod/AddSC_boss_herod, boss_highlord_omokk/AddSC_boss_highlordomokk, boss_high_inquisitor_fairbanks/AddSC_boss_high_inquisitor_fairbanks, boss_high_interrogator_gerstahn/AddSC_boss_high_interrogator_gerstahn, boss_houndmaster_loksey/AddSC_boss_houndmaster_loksey, boss_huhuran/AddSC_boss_huhuran, boss_illucia_barov/AddSC_boss_illuciabarov, boss_immol_thar/AddSC_boss_immol_thar, boss_instructor_malicia/AddSC_boss_instructormalicia, boss_interrogator_vishas/AddSC_boss_interrogator_vishas, boss_ironaya/AddSC_boss_ironaya, boss_jandice_barov/AddSC_boss_jandicebarov, boss_jeklik/AddSC_boss_jeklik, boss_jindo/AddSC_boss_jindo, boss_kurinnaxx/AddSC_boss_kurinnaxx, boss_landslide/AddSC_boss_landslide, boss_loatheb/AddSC_boss_loatheb, boss_lord_alexei_barov/AddSC_boss_lordalexeibarov, boss_lorekeeper_polkelt/AddSC_boss_lorekeeperpolkelt, boss_lucifron/AddSC_boss_lucifron, boss_maexxna/AddSC_boss_maexxna, boss_magistrate_barthilas/AddSC_boss_magistrate_barthilas, boss_magmus/AddSC_boss_magmus, boss_majordomo_executus/AddSC_boss_majordomo, boss_maleki_the_pallid/AddSC_boss_maleki_the_pallid, boss_mandokir/AddSC_boss_mandokir, boss_marli/AddSC_boss_marli, boss_moam/AddSC_boss_moam, boss_mr_smite/AddSC_boss_mr_smite, boss_nefarian/AddSC_boss_nefarian, boss_nerubenkan/AddSC_boss_nerubenkan, boss_noth/AddSC_boss_noth, boss_noxxion/AddSC_boss_noxxion, boss_omen/AddSC_boss_omen, boss_onyxia/AddSC_boss_onyxia, boss_order_of_silver_hand/AddSC_boss_order_of_silver_hand, boss_ossirian/AddSC_boss_ossirian, boss_ouro/AddSC_boss_ouro, boss_overlord_wyrmthalak/AddSC_boss_overlordwyrmthalak, boss_patchwerk/AddSC_boss_patchwerk, boss_postmaster_malown/AddSC_boss_postmaster_malown, boss_ramstein_the_gorger/AddSC_boss_ramstein_the_gorger, boss_ras_frostwhisper/AddSC_boss_rasfrost, boss_razorgore/AddSC_boss_razorgore, boss_razuvious/AddSC_boss_razuvious, boss_renataki/AddSC_boss_renataki, boss_sapphiron/AddSC_boss_sapphiron, boss_sartura/AddSC_boss_sartura, boss_shadow_hunter_voshgajin/AddSC_boss_shadowvosh, boss_shazzrah/AddSC_boss_shazzrah, boss_skeram/AddSC_boss_skeram, boss_sulfuron_harbinger/AddSC_boss_sulfuron, boss_tendris_warpwood/AddSC_boss_tendris_warpwood, boss_thaddius/AddSC_boss_thaddius, boss_thermaplugg/AddSC_boss_thermaplugg, boss_the_beast/AddSC_boss_thebeast, boss_the_ravenian/AddSC_boss_theravenian, boss_timmy_the_cruel/AddSC_boss_timmy_the_cruel, boss_tomb_of_seven/AddSC_boss_tomb_of_seven, boss_twinemperors/AddSC_boss_twinemperors, boss_urok/AddSC_boss_urok, boss_vaelastrasz/AddSC_boss_vael, boss_vectus/AddSC_boss_vectus, boss_venoxis/AddSC_boss_venoxis, boss_victor_nefarius/AddSC_boss_victor_nefarius, boss_viscidus/AddSC_boss_viscidus, boss_warmaster_voone/AddSC_boss_warmastervoone, boss_zevrim/AddSC_boss_zevrim, burning_steppes/AddSC_burning_steppes, custom_creatures/AddSC_custom_creatures, darkshore/AddSC_darkshore, deadmines/AddSC_deadmines, desolace/AddSC_desolace, dreadsteed_ritual/AddSC_dreadsteed_ritual, dun_morogh/AddSC_dun_morogh, durotar/AddSC_durotar, duskwood/AddSC_duskwood, dustwallow_marsh/AddSC_dustwallow_marsh, eastern_plaguelands/AddSC_eastern_plaguelands, elemental_invasions/AddSC_elemental_invasions, elwynn_forest/AddSC_elwynn_forest, felwood/AddSC_felwood, feralas/AddSC_feralas, fireworks_show/AddSC_event_fireworks, gnomeregan/AddSC_gnomeregan, go_scripts/AddSC_go_scripts, hillsbrad_foothills/AddSC_hillsbrad_foothills, hinterlands/AddSC_hinterlands, instance_blackfathom_deeps/AddSC_instance_blackfathom_deeps, instance_blackrock_depths/AddSC_instance_blackrock_depths, instance_blackrock_spire/AddSC_instance_blackrock_spire, instance_blackwing_lair/AddSC_instance_blackwing_lair, instance_deadmines/AddSC_instance_deadmines, instance_dire_maul/AddSC_instance_dire_maul, instance_gnomeregan/AddSC_instance_gnomeregan, instance_maraudon/AddSC_instance_maraudon, instance_molten_core/AddSC_instance_molten_core, instance_naxxramas.boss_kelthuzad/AddSC_boss_kelthuzad, instance_naxxramas.Main/AddSC_instance_naxxramas, instance_onyxia_lair/AddSC_instance_onyxia_lair, instance_razorfen_downs/AddSC_instance_razorfen_downs, instance_razorfen_kraul/AddSC_instance_razorfen_kraul, instance_ruins_of_ahnqiraj/AddSC_instance_ruins_of_ahnqiraj, instance_scarlet_monastery/AddSC_instance_scarlet_monastery, instance_scholomance/AddSC_instance_scholomance, instance_shadowfang_keep/AddSC_instance_shadowfang_keep, instance_stratholme/AddSC_instance_stratholme, instance_sunken_temple/AddSC_instance_sunken_temple, instance_temple_of_ahnqiraj/AddSC_instance_temple_of_ahnqiraj, instance_uldaman/AddSC_instance_uldaman, instance_wailing_caverns/AddSC_instance_wailing_caverns, instance_zulfarrak/AddSC_instance_zulfarrak, instance_zulgurub/AddSC_instance_zulgurub, loch_modan/AddSC_loch_modan, mob_anubisath_sentinel/AddSC_mob_anubisath_sentinel, molten_core/AddSC_molten_core, moonglade/AddSC_moonglade, mulgore/AddSC_mulgore, npcs_special/AddSC_npcs_special, npc_j_eevee/AddSC_npc_j_eevee, npc_sandstalker/AddSC_npc_sandstalker, quest_stormwind_rendezvous/AddSC_quest_stormwind_rendezvous, razorfen_downs/AddSC_razorfen_downs, razorfen_kraul/AddSC_razorfen_kraul, redridge_mountains/AddSC_redridge_mountains, ruins_of_ahnqiraj/AddSC_ruins_of_ahnqiraj, scholo_trash/AddSC_scholo_trash, scourge_invasion/AddSC_scourge_invasion, scripts_battlegrounds_battleground/AddSC_battleground, searing_gorge/AddSC_searing_gorge, silithus/AddSC_silithus, silverpine_forest/AddSC_silverpine_forest, spell_druid/AddSC_druid_spell_scripts, spell_hunter/AddSC_hunter_spell_scripts, spell_item/AddSC_item_spell_scripts, spell_mage/AddSC_mage_spell_scripts, spell_paladin/AddSC_paladin_spell_scripts, spell_priest/AddSC_priest_spell_scripts, spell_rogue/AddSC_rogue_spell_scripts, spell_shaman/AddSC_shaman_spell_scripts, spell_special/AddSC_special_spell_scripts, spell_warlock/AddSC_warlock_spell_scripts, spell_warrior/AddSC_warrior_spell_scripts, stonetalon_mountains/AddSC_stonetalon_mountains, stormwind_city/AddSC_stormwind_city, stranglethorn_vale/AddSC_stranglethorn_vale, stratholme/AddSC_stratholme, sunken_temple/AddSC_sunken_temple, swamp_of_sorrows/AddSC_swamp_of_sorrows, tanaris/AddSC_tanaris, teldrassil/AddSC_teldrassil, the_barrens/AddSC_the_barrens, thousand_needles/AddSC_thousand_needles, ThreatListCopier.battleground_alterac/AddSC_bg_alterac, ThreatListCopier.boss_ragnaros/AddSC_boss_ragnaros, totems/AddSC_Totems, ubrs_trash/AddSC_ubrs_trash, uldaman/AddSC_uldaman, undercity/AddSC_undercity, ungoro_crater/AddSC_ungoro_crater, wailing_caverns/AddSC_wailing_caverns, western_plaguelands/AddSC_western_plaguelands, westfall/AddSC_westfall, wetlands/AddSC_wetlands, winterspring/AddSC_winterspring, world_event_wareffort/AddSC_war_effort, zulfarrak/AddSC_zulfarrak, zulgurub_trash/AddSC_zg_trash | — |
